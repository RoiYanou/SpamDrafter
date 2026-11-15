using SpamDrafter.Simulator.Data;

namespace SpamDrafter.Simulator.Engine;

/// <summary>Sorte d'action d'un joueur pendant son tour.</summary>
public enum ActionKind { Cast, Buy, Reroll, Skip, Ban }

/// <summary>
/// Une action possible : lancer une capacité, ou agir sur la boutique. Pour un achat : le Tox, la carte qui le reçoit,
/// et l'emplacement libéré si la carte est pleine (« A:capacité » ou « T:n° de Tox »).
/// </summary>
public sealed record DuelAction(ActionKind Kind, CastOption? Cast = null, ToxDef? Item = null, CardState? Card = null,
    string? Origin = null)
{
    public static DuelAction Of(CastOption cast) => new(ActionKind.Cast, Cast: cast);
}

// Boutique de Tox (phase 6, lot D) — 🔒 InGameShop.cs, GameLogic.BuyItem / RerollShop / SkipShop, ShopRarityCurves.cs.
public sealed partial class Duel
{
    /// <summary>Tox proposés en ce moment (vide = boutique fermée).</summary>
    public List<ToxDef> ShopItems { get; } = new();
    /// <summary>Tox bannis (pour les deux joueurs : 🔒 InGameShop.BanItem utilise la liste commune).</summary>
    public HashSet<string> Banned { get; } = new();
    private readonly int[] _addedRerollCost = new int[2];
    private readonly int[] _banDone = new int[2];
    private readonly bool[] _boughtThisWindow = new bool[2];

    private ShopSettings Shop => _s.Shop;
    private static readonly string[] RarityNames = ["common", "rare", "epic", "legendary"];

    // ================================================================ rareté selon la chance (§8.5)

    /// <summary>🔒 ShopRarityCurves.Sample : courbe d'Hermite (tangentes non pondérées), bornée aux extrémités.</summary>
    public static float EvaluateCurve(IReadOnlyList<CurveKey> keys, float t)
    {
        var n = keys.Count;
        if (n == 1 || t <= keys[0].Time) return keys[0].Value;
        if (t >= keys[n - 1].Time) return keys[n - 1].Value;
        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) >> 1;
            if (keys[mid].Time <= t) lo = mid; else hi = mid;
        }
        var a = keys[lo];
        var b = keys[lo + 1];
        var dt = b.Time - a.Time;
        if (dt <= 0f) return b.Value;
        if (float.IsInfinity(a.OutTangent) || float.IsInfinity(b.InTangent)) return a.Value;
        var u = (t - a.Time) / dt;
        var u2 = u * u;
        var u3 = u2 * u;
        return (2f * u3 - 3f * u2 + 1f) * a.Value + (u3 - 2f * u2 + u) * dt * a.OutTangent
             + (-2f * u3 + 3f * u2) * b.Value + (u3 - u2) * dt * b.InTangent;
    }

    /// <summary>🔒 InGameShop.EvaluateRarityGates : poids des 4 raretés (négatif → 0).</summary>
    public float[] RarityGates(double luck)
    {
        var gates = new float[4];
        for (var i = 0; i < 4; i++) gates[i] = Math.Max(0f, EvaluateCurve(Shop.RarityCurves[i], (float)luck));
        return gates;
    }

    /// <summary>🔒 InGameShop.RarityIndexForRoll : tirage de 0 à 999 converti en rareté.</summary>
    public static int RarityIndexForRoll(int roll, float[] gates)
    {
        var sum = gates[0] + gates[1] + gates[2] + gates[3];
        if (sum <= 0f) return 0;
        var x = ((float)roll + 0.5f) / 10f * sum / 100f;
        if (x < gates[0]) return 0;
        if (x < gates[0] + gates[1]) return 1;
        if (x < gates[0] + gates[1] + gates[2]) return 2;
        return 3;
    }

    /// <summary>Probabilités exactes (en %) de chaque rareté pour une chance donnée (comme InGameShop.ComputeRarityOdds).</summary>
    public double[] RarityOdds(double luck)
    {
        var gates = RarityGates(luck);
        var counts = new int[4];
        for (var r = 0; r < 1000; r++) counts[RarityIndexForRoll(r, gates)]++;
        return counts.Select(c => c / 10.0).ToArray();
    }

    // ================================================================ ouverture et tirage

    /// <summary>
    /// 🔒 InGameShop.RandomizeForPassives : 3 Tox différents, non bannis, tirés selon la chance du joueur
    /// (Tox Chanceux : 25 % par rareté).
    /// </summary>
    private void RandomizeShop(PlayerState p)
    {
        ShopItems.Clear();
        var gates = RarityGates(p.Luck);
        var lucky = p.Board.Any(c => c.HasTalentStatus("ToxChanceux"));
        for (var i = 0; i < Shop.ItemShopMax; i++)
        {
            var rarity = RarityIndexForRoll(_rng.Next(0, 1000), gates);
            if (lucky) rarity = _rng.Next(0, 4);
            var pool = Shop.Pools[rarity].Select(_kb.Tox).OfType<ToxDef>()
                .Where(t => !Banned.Contains(t.Uuid) && !ShopItems.Contains(t)).ToList();
            if (pool.Count == 0) break;   // le jeu boucle sans fin dans ce cas ; on s'arrête (KI)
            ShopItems.Add(pool[_rng.Next(0, pool.Count)]);
        }
        Log.Add(Round, $"Boutique de J{p.Id + 1} (chance {p.Luck:0.##}) : " +
                       string.Join(", ", ShopItems.Select(t => $"{t.AssetName} ({t.Cost})")) + ".");
    }

    /// <summary>§2 étape 12 : la boutique s'ouvre seulement les rounds pairs.</summary>
    private void OpenShopIfEvenRound(PlayerState p)
    {
        if (Round % 2 == 0) RandomizeShop(p);
    }

    /// <summary>🔒 InGameShop.GetSkipReward : selon le nombre de cartes sur le plateau (📊 skipPerCardAlive).</summary>
    public int SkipReward(PlayerState p)
    {
        if (p.Board.Count == 0 || Shop.SkipPerCardAlive.Count == 0) return 0;
        return Shop.SkipPerCardAlive[Math.Clamp(p.Board.Count - 1, 0, Shop.SkipPerCardAlive.Count - 1)];
    }

    /// <summary>Prix de la prochaine relance (10 + 5 par relance déjà faite ce tour ; Casino : la 1ʳᵉ est gratuite).</summary>
    public int RerollPrice(PlayerState p) =>
        _addedRerollCost[p.Id] == 0 && p.Board.Any(c => c.HasTalentStatus("Casino")) ? 0 : Shop.RerollCost + _addedRerollCost[p.Id];

    // ================================================================ actions possibles

    /// <summary>Toutes les actions possibles du joueur actif : lancers, puis actions de boutique.</summary>
    public List<DuelAction> LegalActions()
    {
        var result = LegalCasts().Select(DuelAction.Of).ToList();
        if (Phase != DuelPhase.Main || ShopItems.Count == 0) return result;
        var p = Active;
        foreach (var item in ShopItems.Where(t => !Banned.Contains(t.Uuid) && t.Cost <= p.Spamtons))
            foreach (var card in p.Board.Where(c => c.IsAlive))
            {
                if (VisualSlots(card) < 3) result.Add(new DuelAction(ActionKind.Buy, Item: item, Card: card));
                else
                {
                    foreach (var a in card.Abilities) result.Add(new DuelAction(ActionKind.Buy, Item: item, Card: card, Origin: "A:" + a.Def.Id));
                    for (var k = 0; k < card.Toxes.Count; k++) result.Add(new DuelAction(ActionKind.Buy, Item: item, Card: card, Origin: "T:" + k));
                }
            }
        if (!_boughtThisWindow[p.Id] && p.Spamtons >= RerollPrice(p)) result.Add(new DuelAction(ActionKind.Reroll));
        result.Add(new DuelAction(ActionKind.Skip));
        if (_banDone[p.Id] < Shop.BanPerPlayer)
            foreach (var item in ShopItems.Where(t => !Banned.Contains(t.Uuid)))
                result.Add(new DuelAction(ActionKind.Ban, Item: item));
        return result;
    }

    /// <summary>Applique une action du joueur actif.</summary>
    public void Apply(DuelAction action)
    {
        switch (action.Kind)
        {
            case ActionKind.Cast: Cast(action.Cast!); break;
            case ActionKind.Buy: Buy(action.Item!, action.Card!, action.Origin); break;
            case ActionKind.Reroll: Reroll(); break;
            case ActionKind.Skip: SkipShop(); break;
            case ActionKind.Ban: Ban(action.Item!); break;
        }
    }

    // ================================================================ acheter, relancer, passer, bannir

    /// <summary>
    /// 🔒 GameLogic.BuyItem : le Tox doit être proposé, non banni et payable ; la carte (vivante, au joueur) libère
    /// l'emplacement choisi si elle est pleine. La boutique se ferme. Petit Prix : achat gratuit (statut consommé).
    /// L'achat brise le Gel (📊) et révèle une carte camouflée (sauf Tox Camouflage).
    /// </summary>
    public void Buy(ToxDef item, CardState card, string? origin = null)
    {
        var p = Active;
        if (Phase != DuelPhase.Main || !ShopItems.Contains(item) || Banned.Contains(item.Uuid))
            throw new InvalidOperationException("Ce Tox n'est pas en vente.");
        if (!p.Board.Contains(card) || !card.IsAlive) throw new InvalidOperationException("Carte invalide pour l'achat.");
        if (item.Cost > p.Spamtons) throw new InvalidOperationException("Pas assez de Spamtons.");
        if (VisualSlots(card) >= 3 && origin is null) throw new InvalidOperationException("Carte pleine : choisir l'emplacement à libérer.");
        Record(new Step(StepKind.Action, Action: new ActionRef(ActionKind.Buy, card.Uid, -1, null, item.Uuid, origin)));

        var ice = card.Get(StatusKind.Frozen);
        if (origin is not null) RemoveOrigin(card, origin);
        Log.Add(Round, $"J{p.Id + 1} achète « {item.AssetName} » pour {Name(card)}.");
        AddTox(card, item, isOriginal: false);
        if (Stats is not null)
        {
            Stats.Of(card).ToxBought++;
            Stats.Purchases.Add(new DuelStats.Purchase(p.Id, DuelStats.DeckIndex(card), item.Uuid, Round));
        }
        ShopItems.Clear();
        if (p.Board.FirstOrDefault(c => c.HasTalentStatus("PetitPrix")) is { } cheap)
        {
            cheap.TalentStatuses.RemoveAll(t => t.Type == "PetitPrix");
            Log.Add(Round, "  Petit Prix : achat gratuit.");
        }
        else AddSpamtons(p, -item.Cost, null);
        if (ice is not null && card.Get(StatusKind.Frozen) == ice && _s.FrozenBrokenByToxEquip && card.IsAlive)
            BreakIce(card, card, thaw: true);
        if (card.IsCamouflaged && item.StatusType != "SolCamouflage") RevealCamouflage(card);   // 🔒 CamouflageOnToxPlaced
        _boughtThisWindow[p.Id] = true;
        Settle();
    }

    /// <summary>🔒 Card.RemoveAbilityOrPassive : retire le Tox ou la capacité choisi pour faire de la place.</summary>
    private void RemoveOrigin(CardState card, string origin)
    {
        if (origin.StartsWith("T:") && int.TryParse(origin[2..], out var k) && k >= 0 && k < card.Toxes.Count)
        {
            Log.Add(Round, $"  {Name(card)} retire le Tox « {card.Toxes[k].Def.AssetName} ».");
            card.Toxes.RemoveAt(k);
        }
        else if (origin.StartsWith("A:") && card.Abilities.FirstOrDefault(a => a.Def.Id == origin[2..]) is { } ab)
        {
            Log.Add(Round, $"  {Name(card)} retire « {_kb.AbilityName(ab.Def.Id)} ».");
            card.Abilities.Remove(ab);
        }
        else throw new InvalidOperationException($"Emplacement inconnu : {origin}.");
    }

    /// <summary>🔒 GameLogic.RerollShop : nouveaux Tox contre 10 + 5 par relance déjà faite ce tour (Casino : 1ʳᵉ gratuite).</summary>
    public void Reroll()
    {
        var p = Active;
        if (Phase != DuelPhase.Main || ShopItems.Count == 0 || _boughtThisWindow[p.Id])
            throw new InvalidOperationException("Relance impossible.");
        var price = RerollPrice(p);
        if (p.Spamtons < price) throw new InvalidOperationException("Pas assez de Spamtons pour relancer.");
        Record(new Step(StepKind.Action, Action: new ActionRef(ActionKind.Reroll, null, -1, null, null, null)));
        AddSpamtons(p, -price, null);
        _addedRerollCost[p.Id] += 5;
        Log.Add(Round, $"J{p.Id + 1} relance la boutique ({price} Spamtons).");
        RandomizeShop(p);
        Drain();
    }

    /// <summary>🔒 GameLogic.SkipShop : passer donne la récompense (📊 +10) et la chance prévue (📊 +0) ; la boutique se ferme.</summary>
    public void SkipShop()
    {
        var p = Active;
        if (Phase != DuelPhase.Main || ShopItems.Count == 0) throw new InvalidOperationException("Boutique fermée.");
        Record(new Step(StepKind.Action, Action: new ActionRef(ActionKind.Skip, null, -1, null, null, null)));
        p.Luck += Shop.LuckForSkip;
        var reward = SkipReward(p);
        Log.Add(Round, $"J{p.Id + 1} passe la boutique (+{reward} Spamtons).");
        AddSpamtons(p, reward, null);
        ShopItems.Clear();
        Drain();
    }

    /// <summary>
    /// 🔒 InGameShop.BanItem : bannit ce Tox et tous ceux de même type et même élément, pour toute la partie
    /// (📊 1 bannissement par joueur). Le Tox reste affiché mais ne peut plus être acheté.
    /// </summary>
    public void Ban(ToxDef item)
    {
        var p = Active;
        if (Phase != DuelPhase.Main || !ShopItems.Contains(item) || _banDone[p.Id] >= Shop.BanPerPlayer)
            throw new InvalidOperationException("Bannissement impossible.");
        Record(new Step(StepKind.Action, Action: new ActionRef(ActionKind.Ban, null, -1, null, item.Uuid, null)));
        Banned.Add(item.Uuid);
        foreach (var t in _kb.AllTox.Where(t => t.StatusType == item.StatusType && t.Element == item.Element))
            Banned.Add(t.Uuid);
        _banDone[p.Id]++;
        Log.Add(Round, $"J{p.Id + 1} bannit « {item.AssetName} » (et les Tox du même type).");
    }

    /// <summary>Fin de tour : boutique vidée, surcoût de relance remis à 0 (🔒 GameLogic.EndTurn).</summary>
    private void CloseShop(PlayerState active)
    {
        ShopItems.Clear();
        _addedRerollCost[active.Id] = 0;
        _boughtThisWindow[active.Id] = false;
    }
}
