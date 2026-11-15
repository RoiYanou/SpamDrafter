using SpamDrafter.Simulator.Data;

namespace SpamDrafter.Simulator.Engine;

// Tox et statuts (phase 6, lot C). Un Tox est un objet posé sur une carte (passif inné, effet, boutique) ;
// il agit à l'impact, au début du tour, à la mort, ou modifie les calculs. Les statuts (Gel, Poison…)
// sont les effets temporaires qu'il pose.
public sealed partial class Duel
{
    /// <summary>Types de Tox simulés (StatusType du jeu, 🔒 StatusType.cs).</summary>
    private static readonly HashSet<string> SupportedToxTypes =
    [
        "Aoe", "Berserk", "Brut", "Bomba", "Chance", "Cimetierre", "CoeurElement", "CoeurVital", "CoeurResistance",
        "ContreCoup", "Disparition", "Erosion", "Eternity", "ImunityStatus", "Inversement", "LifeSteal", "Mimetisme",
        "Multiplicator", "Mystere", "Optimisation", "Phoenix", "Poisoned", "Protection", "Renvoie", "Reset", "Revanche",
        "SacDeFrappe", "Shield", "Silenced", "Frozen", "Smoked", "SoinPrimitif", "SpamLand", "Temporality", "Tenacite",
        "Vengeur", "Metamorph", "MetamorphSimple", "Decouverte", "CoeurCorrompu", "StealTox", "SolCamouflage",
        // statuts de talent
        "Resonance", "Roulette", "ChanceVitale", "Paradoxe", "CoeurAcier", "AttackMultiColor",
        "PlumePhoenix", "InsensibleDisparition", "InsensibleEternity", "Casino", "PetitPrix", "ToxChanceux",
    ];

    public bool IsToxSupported(string uuid) => _kb.Tox(uuid) is { } t && SupportedToxTypes.Contains(t.StatusType);

    private enum SubPhase { StartTurn, Main, EndTurn }
    private SubPhase _sub = SubPhase.StartTurn;
    private bool _settlingDeaths;

    // ================================================================ lecture des Tox

    /// <summary>Tox de ce type porté par la capacité (statut posé à l'impact), ou null.</summary>
    private ToxDef? AbilityTox(AbilityDef? ability, string type) =>
        ability?.Passives.Select(_kb.Tox).FirstOrDefault(t => t?.StatusType == type);

    /// <summary>
    /// 🔒 Card.CountModificatorStatus(type, ability) : nombre et somme des valeurs des Tox de ce type
    /// portés par la carte, plus celui porté par la capacité.
    /// </summary>
    private (int Count, double Total) ToxSum(CardState card, string type, AbilityDef? ability = null)
    {
        var list = card.Toxes.Where(t => t.Def.StatusType == type).Select(t => t.Def).ToList();
        if (AbilityTox(ability, type) is { } a) list.Add(a);
        return (list.Count, list.Sum(t => t.Value));
    }

    /// <summary>🔒 Card.IsImmuneTo : un Tox Immunité protège des effets de l'adversaire.</summary>
    private static bool ImmuneTo(CardState target, CardState caster) =>
        caster.Owner != target.Owner && target.HasTox("ImunityStatus");

    /// <summary>🔒 Card.AddStatus(game, caster…) : statut posé, sauf si la cible est immunisée.</summary>
    private void ApplyStatus(CardState target, CardState caster, StatusKind kind, int value, int duration)
    {
        if (ImmuneTo(target, caster)) { Log.Add(Round, $"  {Name(target)} est immunisée."); return; }
        if (kind == StatusKind.Smoked && target.IsCamouflaged) return;
        target.AddStatus(kind, value, duration, caster);
        Log.Add(Round, $"  {Name(target)} : {kind}{(duration == 0 ? " (permanent)" : $" ({duration} fins de tour)")}.");
    }

    // ================================================================ pose d'un Tox

    /// <summary>
    /// 🔒 Card.AddModificator : pose un Tox. Cas particuliers : Mystère (Tox tiré au hasard), Immunité (efface
    /// les statuts), Métamorphose (la carte change), Découverte (nouvelle capacité), Cœur corrompu (cœur épique
    /// au hasard), Vol de Tox (pris à l'adversaire).
    /// </summary>
    private void AddTox(CardState card, ToxDef tox, bool isOriginal)
    {
        if (tox.StatusType == "Mystere")
        {
            tox = RandomMystery(allowChoice: _sub == SubPhase.Main && Phase == DuelPhase.Main);
            Log.Add(Round, $"  Mystère : {Name(card)} reçoit « {tox.AssetName} ».");
        }
        switch (tox.StatusType)
        {
            case "ImunityStatus":
                card.Statuses.Clear();
                break;
            case "Metamorph":
            {
                var options = new List<CardDef>();
                for (var tries = 30; options.Count < 3 && tries > 0; tries--)
                {
                    var pick = RandomMetamorphCard(card, sameElement: true);
                    if (pick is not null && !options.Contains(pick)) options.Add(pick);
                }
                if (options.Count == 0) return;
                var k = AskAmong(card, "Métamorphose", options.Select(o => _kb.CardName(o)).ToList());
                SetCard(card, options[k]);
                return;
            }
            case "MetamorphSimple":
                if (RandomMetamorphCard(card, sameElement: false) is { } simple) SetCard(card, simple);
                return;
            case "Decouverte":
            {
                var pool = _kb.Abilities.Where(a => a.IsActive && a.DecouverteEnable).ToList();
                if (pool.Count == 0) return;
                var options = new List<AbilityDef>();
                for (var tries = 30; options.Count < 3 && tries > 0; tries--)
                {
                    var pick = pool[_rng.Next(0, pool.Count)];
                    if (!options.Contains(pick)) options.Add(pick);
                }
                var k = AskAmong(card, "Découverte", options.Select(o => _kb.AbilityName(o.Id)).ToList());
                card.Abilities.Add(new AbilityState(options[k]));
                Log.Add(Round, $"  Découverte : {Name(card)} gagne « {_kb.AbilityName(options[k].Id)} ».");
                return;
            }
            case "CoeurCorrompu":
            {
                var hearts = _s.HeartPool.Select(_kb.Tox).OfType<ToxDef>()
                    .Where(t => t.Rarity == "rarity-epic" && t.StatusType == "CoeurElement").ToList();
                if (hearts.Count > 0) AddTox(card, hearts[_rng.Next(0, hearts.Count)], isOriginal);
                return;
            }
            case "StealTox":
            {
                var all = Opponent(card.Owner).Board.SelectMany(c => c.Toxes.Select(t => (c, t))).ToList();
                if (all.Count == 0) return;
                var (from, stolen) = all[_rng.Next(0, all.Count)];
                from.Toxes.Remove(stolen);
                Log.Add(Round, $"  {Name(card)} vole « {stolen.Def.AssetName} » à {Name(from)}.");
                AddTox(card, stolen.Def, isOriginal);
                return;
            }
        }
        card.Toxes.Add(new ToxState(tox, isOriginal));
        if (!isOriginal) Log.Add(Round, $"  {Name(card)} reçoit le Tox « {tox.AssetName} ».");
    }

    /// <summary>🔒 GameplayData.GetRandomMysteryPassive : hors phase principale, pas de Tox qui ouvre un choix.</summary>
    private ToxDef RandomMystery(bool allowChoice)
    {
        var pool = _s.MysteryPool.Select(_kb.Tox).OfType<ToxDef>().ToList();
        static bool OpensChoice(ToxDef t) => t.StatusType is "Metamorph" or "Decouverte";
        var pick = pool[_rng.Next(0, pool.Count)];
        if (allowChoice || !OpensChoice(pick)) return pick;
        for (var i = 0; i < 16 && OpensChoice(pick); i++) pick = pool[_rng.Next(0, pool.Count)];
        return !OpensChoice(pick) ? pick : pool.FirstOrDefault(t => !OpensChoice(t)) ?? pick;
    }

    /// <summary>🔒 Card.PickRandomMetamorphCard : même élément (ou même rareté), hors cartes déjà dans le deck du joueur.</summary>
    private CardDef? RandomMetamorphCard(CardState card, bool sameElement)
    {
        bool Matches(CardDef c) => sameElement ? c.Element == card.Def.Element : c.Rarity == card.Def.Rarity;
        var p = Players[card.Owner];
        var excluded = p.Board.Concat(p.Discard).Select(c => c.Def.Id).ToHashSet();
        var list = _kb.Cards.Where(c => Matches(c) && !excluded.Contains(c.Id)).ToList();
        if (list.Count == 0) list = _kb.Cards.Where(Matches).ToList();
        return list.Count == 0 ? null : list[_rng.Next(0, list.Count)];
    }

    /// <summary>
    /// 🔒 Card.SetCard (Métamorphose) : la carte devient une autre carte, PV pleins, nouvelles capacités et
    /// passifs innés ; talent, statuts de talent, Tox et statuts sont perdus (le Gel disparaît sans dégâts).
    /// </summary>
    private void SetCard(CardState card, CardDef def)
    {
        Log.Add(Round, $"  Métamorphose : {Name(card)} devient {_kb.CardName(def)}.");
        card.Def = def;
        card.Element = def.Element;
        card.HpMax = def.Hp;
        card.Damage = 0;
        card.Exhausted = false;
        card.Toxes.Clear();
        card.Talents.Clear();
        card.TalentStatuses.Clear();
        card.Statuses.Clear();
        card.Abilities.Clear();
        card.Abilities.AddRange(def.Abilities.Select(id => new AbilityState(_kb.Ability(id))));
        card.Resistances.Clear();
        foreach (var (k, v) in def.Resistances) card.Resistances[k] = v;
        foreach (var uuid in def.InnatePassives)
            if (_kb.Tox(uuid) is { } t) AddTox(card, t, isOriginal: true);
    }

    /// <summary>
    /// Pose d'un Tox par un effet : si la carte était gelée, le Gel se brise (📊 frozenBrokenByToxEquip).
    /// (La révélation d'une carte camouflée n'a lieu qu'à l'achat en boutique : lot D.)
    /// </summary>
    private void PlaceTox(CardState card, ToxDef tox)
    {
        var ice = card.Get(StatusKind.Frozen);
        AddTox(card, tox, isOriginal: false);
        if (ice is not null && card.Get(StatusKind.Frozen) == ice && _s.FrozenBrokenByToxEquip && card.IsAlive)
            BreakIce(card, card, thaw: true);
    }

    /// <summary>Emplacements visibles : capacités + Tox (3 au maximum, 🔒 MAX_VISUAL_SLOTS).</summary>
    private static int VisualSlots(CardState card) => card.Abilities.Count + card.Toxes.Count;

    /// <summary>
    /// 🔒 Libère un emplacement quand les 3 sont pris : d'abord le dernier Tox, sinon la dernière capacité
    /// (sauf <paramref name="keepAbilityId"/>).
    /// </summary>
    private void FreeSlot(CardState card, string? keepAbilityId)
    {
        if (VisualSlots(card) < 3) return;
        if (card.Toxes.Count > 0)
        {
            var last = card.Toxes[^1];
            card.Toxes.RemoveAt(card.Toxes.Count - 1);
            Log.Add(Round, $"  {Name(card)} perd le Tox « {last.Def.AssetName} ».");
            return;
        }
        var ab = card.Abilities.LastOrDefault(a => a.Def.Id != keepAbilityId);
        if (ab is null) return;
        card.Abilities.Remove(ab);
        Log.Add(Round, $"  {Name(card)} perd « {_kb.AbilityName(ab.Def.Id)} ».");
    }

    /// <summary>🔒 Card.ForgetOneAbility (Disparition) : la cible perd son dernier Tox.</summary>
    private void ForgetOneTox(CardState target, CardState caster)
    {
        if (ImmuneTo(target, caster) || target.HasTalentStatus("InsensibleDisparition")) return;
        if (target.Toxes.Count == 0) return;
        var last = target.Toxes[^1];
        target.Toxes.RemoveAt(target.Toxes.Count - 1);
        Log.Add(Round, $"  Disparition : {Name(target)} perd « {last.Def.AssetName} ».");
    }

    // ================================================================ statuts à l'impact (§6.1)

    /// <summary>
    /// 🔒 GameLogic.ProcStatus : après chaque coup ou soin. Si la cible n'est pas immunisée : Disparition, Poison,
    /// Éternité, Temporalité, Silence, Gel, Provocation de l'attaquant, réveil. Pour des dégâts : Sac de frappe,
    /// Cœur d'acier, Vol de vie, Renvoi, Contrecoup. Enfin : Fumée.
    /// </summary>
    private void ProcStatus(CardState attacker, CardState target, AbilityDef? ability, int value, bool isHeal,
        bool useAllStatus, bool canTriggerTalents, bool targetWasFrozen)
    {
        var sameSide = attacker.Owner == target.Owner;
        if (canTriggerTalents && attacker.HasTalentStatus("AttackMultiColor"))
            AddElementLevel(Players[attacker.Owner], null, (int)attacker.TalentStatusValue("AttackMultiColor"), source: attacker);
        if (!useAllStatus) return;

        if (!ImmuneTo(target, attacker))
        {
            if (attacker.HasTox("Disparition") || AbilityTox(ability, "Disparition") is not null) ForgetOneTox(target, attacker);

            var poison = ToxSum(attacker, "Poisoned", ability);
            if (poison.Count > 0)
                ApplyStatus(target, attacker, StatusKind.Poisoned, (int)(target.HpMax * poison.Total / 100.0), 6);

            if (attacker.HasTox("Eternity") || AbilityTox(ability, "Eternity") is not null)
                AddAllAbilityCooldown(attacker, ability, target, 1, null, false);
            if (attacker.HasTox("Temporality") || AbilityTox(ability, "Temporality") is not null)
                AddAllAbilityCooldown(attacker, ability, target, -1, null, false);

            if ((attacker.Tox("Silenced") ?? AbilityTox(ability, "Silenced")) is not null)
                ApplyStatus(target, attacker, StatusKind.Silenced, 1, ability is { Duration: > 0 } ? ability.Duration : 2);

            ManageFreeze(attacker, target, ability, targetWasFrozen);

            if ((attacker.Tox("Protection") ?? AbilityTox(ability, "Protection")) is not null)
                ApplyStatus(attacker, attacker, StatusKind.Taunt, 1, 2);
            target.Remove(StatusKind.Sleep);
        }

        if (!isHeal)
        {
            if (!sameSide)
            {
                var bag = ToxSum(target, "SacDeFrappe");
                if (bag.Count > 0) AddElementLevel(Players[target.Owner], target.Def.Element, (int)bag.Total, source: target);
            }
            if (target.HasTalentStatus("CoeurAcier"))
            {
                var v = target.TalentStatusValue("CoeurAcier");
                AddResistance(target, target, null, v);
            }
            var steal = ToxSum(attacker, "LifeSteal", ability);
            if (steal.Count > 0)
                HealCard(attacker, attacker, ability, null, (int)(value * steal.Total / 100.0), useAllStatus: false);
            var reflect = ToxSum(target, "Renvoie");
            if (reflect.Count > 0)
                DamageCard(attacker, attacker, ability, null, (int)(reflect.Total / 100.0 * value),
                    useAllStatus: false, canProcAoe: false, canTriggerTalents: false);
            var counter = ToxSum(attacker, "ContreCoup", ability);
            if (counter.Count > 0)
                DamageCard(attacker, attacker, ability, null, (int)(counter.Total / 100.0 * value),
                    useAllStatus: false, canProcAoe: false, canTriggerTalents: false);
        }

        if ((AbilityTox(ability, "Smoked") ?? attacker.Tox("Smoked")) is not null)
            ShuffleBoard(Players[attacker.Owner], attacker);
    }

    /// <summary>
    /// 🔒 GameLogic.ManageFreeze : le Gel le plus fort (Tox de l'attaquant ou de la capacité, ce dernier seulement
    /// sur un critique si « freezeOnlyOnCrit ») ; durée 2 × frozenOwnerTurns, ajustée selon le moment.
    /// </summary>
    private void ManageFreeze(CardState attacker, CardState target, AbilityDef? ability, bool targetWasFrozen)
    {
        if (targetWasFrozen || target.Has(StatusKind.Frozen) || !target.IsAlive) return;
        var best = attacker.Toxes.Where(t => t.Def.StatusType == "Frozen").Select(t => t.Def)
            .OrderByDescending(t => t.Value).FirstOrDefault();
        var lastCrit = ability is not null && (attacker.Runtime(ability.Id)?.LastHitCrit ?? false);
        if (ability is not null && (!ability.FreezeOnlyOnCrit || lastCrit) && AbilityTox(ability, "Frozen") is { } fromAbility &&
            (best is null || fromAbility.Value > best.Value))
            best = fromAbility;
        if (best is null) return;
        var duration = 2 * Math.Max(1, _s.FrozenOwnerTurns);
        if (attacker.Owner != CurrentPlayer) duration += _sub != SubPhase.StartTurn ? 1 : -1;
        if (_sub == SubPhase.EndTurn) duration--;
        ApplyStatus(target, attacker, StatusKind.Frozen, RoundToInt(best.Value), duration);
    }

    /// <summary>
    /// 🔒 Player.ShuffleBoard (Fumée) : les cartes du joueur changent de place au hasard (les vivantes sur les
    /// premiers emplacements) et reçoivent le statut Fumée.
    /// </summary>
    private void ShuffleBoard(PlayerState p, CardState caster)
    {
        var slots = Enumerable.Range(1, _s.CardsPerDeck).ToList();
        for (var i = slots.Count - 1; i > 0; i--)
        {
            var j = _rng.Next(0, i + 1);
            (slots[i], slots[j]) = (slots[j], slots[i]);
        }
        var cards = p.Board.ToList();
        var n = Math.Min(cards.Count, slots.Count);
        var order = Enumerable.Range(0, n).OrderBy(k => slots[k]).ToArray();
        for (var r = 0; r < n; r++) slots[order[r]] = r + 1;
        for (var k = 0; k < n; k++)
        {
            ApplyStatus(cards[k], caster, StatusKind.Smoked, 2, 0);
            cards[k].Slot = slots[k];
        }
        Log.Add(Round, $"  Fumée : les cartes de J{p.Id + 1} changent de place.");
    }

    // ================================================================ début de tour (§2)

    /// <summary>🔒 ChargeShieldsForOpponent : les cartes de l'adversaire avec un Tox Bouclier retrouvent leur bouclier.</summary>
    private void ChargeShields(PlayerState active)
    {
        foreach (var c in Opponent(active.Id).Board)
            if (c.Tox("Shield") is { } shield)
            {
                c.Remove(StatusKind.Shield);
                c.AddStatus(StatusKind.Shield, (int)shield.Value, 1, c);
            }
    }

    /// <summary>
    /// 🔒 StartTurnModificatorStatus, carte par carte : Poison → Soin primitif → SpamLand → Cœur vital →
    /// Cœur d'élément → Cœur de résistance → Chance.
    /// </summary>
    private void StartTurnToxTicks(CardState c, PlayerState p)
    {
        foreach (var poison in c.Statuses.Where(s => s.Kind == StatusKind.Poisoned).ToList())
        {
            Log.Add(Round, $"Poison : {Name(c)} subit {poison.Value}.");
            DamageCard(poison.Caster ?? c, c, null, null, poison.Value, origin: DamageOrigin.Poison,
                canProcAoe: false, canTriggerTalents: false, useAllStatus: false);
        }
        var heal = ToxSum(c, "SoinPrimitif");
        if (heal.Count > 0 && (int)(c.HpMax * heal.Total / 100.0) is var h and > 0)
            HealCard(c, c, null, null, h, useAllStatus: false, canTriggerTalents: false);
        var land = ToxSum(c, "SpamLand");
        if (land.Count > 0) AddSpamtons(p, (int)land.Total, null);
        if (c.IsAlive && ToxSum(c, "CoeurVital") is { Count: > 0 } vital) AddHpMax(c, c, (int)vital.Total);
        foreach (var heart in c.Toxes.Where(t => t.Def.StatusType == "CoeurElement").ToList())
            AddElementLevel(p, heart.Def.Element ?? c.Def.Element, (int)heart.Def.Value, source: c);
        var res = ToxSum(c, "CoeurResistance");
        if (res.Count > 0)
            foreach (var ally in p.Board.ToList()) AddResistance(ally, c, null, (int)res.Total);
        var luck = ToxSum(c, "Chance");
        if (luck.Count > 0) { p.Luck += luck.Total; Log.Add(Round, $"  J{p.Id + 1} : chance +{luck.Total:0.##}."); }
    }

    // ================================================================ Camouflage

    /// <summary>🔒 RevealCamouflage : une carte camouflée qui agit est révélée jusqu'au début de son prochain tour.</summary>
    private void RevealCamouflage(CardState card)
    {
        if (card.IsAlive && card.HasTox("SolCamouflage") && !card.Has(StatusKind.Revealed))
        {
            card.AddStatus(StatusKind.Revealed, 1, 0);
            Log.Add(Round, $"  {Name(card)} est révélée.");
        }
    }

    /// <summary>🔒 DestroyCamouflage : le Tox Camouflage est retiré (overtime, dernière carte).</summary>
    private void DestroyCamouflage(CardState card)
    {
        if (!card.IsAlive) return;
        var camo = card.Toxes.FirstOrDefault(t => t.Def.StatusType == "SolCamouflage");
        if (camo is null) return;
        card.Toxes.Remove(camo);
        card.Remove(StatusKind.Revealed);
        Log.Add(Round, $"  {Name(card)} perd son Camouflage.");
    }

    // ================================================================ mort (§7)

    /// <summary>🔒 ManageReset : un ennemi tué par une carte avec Reset → la capacité reste disponible.</summary>
    private void ManageReset(CardState attacker, CardState target, AbilityDef? ability)
    {
        if (attacker.Owner == target.Owner) return;
        if (!attacker.HasTox("Reset") && AbilityTox(ability, "Reset") is null) return;
        if (ability is null || attacker.Runtime(ability.Id) is not { } rt) return;
        if (_settlingDeaths)
        {
            if (rt.CurrentCooldown > 0) rt.CurrentCooldown = 0;
        }
        else rt.DontUpdateCooldown = true;
        Log.Add(Round, $"  Reset : « {_kb.AbilityName(rt.Def.Id)} » reste disponible.");
    }

    /// <summary>🔒 ManageBomba : chaque Tox Bomba inflige sa valeur à une carte adverse au hasard (sans élément).</summary>
    private void ManageBomba(CardState target, AbilityDef? ability)
    {
        var bombs = target.Toxes.Where(t => t.Def.StatusType == "Bomba").Select(t => t.Def).ToList();
        if (AbilityTox(ability, "Bomba") is { } extra) bombs.Add(extra);
        foreach (var b in bombs)
        {
            var foes = Opponent(target.Owner).Board;
            if (foes.Count == 0) return;
            var victim = foes[_rng.Next(0, foes.Count)];
            Log.Add(Round, $"  Bomba de {Name(target)} sur {Name(victim)} !");
            DamageCard(target, victim, null, null, (int)b.Value, useAllStatus: false, canTriggerTalents: false);
            victim.Remove(StatusKind.Smoked);
        }
    }

    /// <summary>🔒 Card.MakeAllAbilitiesAvailable (Vengeur) : toutes les recharges reviennent à 0.</summary>
    private void MakeAllAvailable(CardState card)
    {
        foreach (var a in card.Abilities.Where(a => !a.DontUpdateCooldown)) a.CurrentCooldown = 0;
        Log.Add(Round, $"  Vengeur : les capacités de {Name(card)} sont prêtes.");
    }

    /// <summary>
    /// 🔒 ManageDiscard (Phénix) : une carte morte avec un Tox Phénix revient avec X % de ses PV ; elle perd ce Tox
    /// (sauf Plume de Phénix réussie) et ses statuts.
    /// </summary>
    private void PhoenixRevive(PlayerState p)
    {
        foreach (var card in p.Discard.Where(c => c.HasTox("Phoenix")).ToList())
        {
            var phoenix = card.Toxes.First(t => t.Def.StatusType == "Phoenix");
            p.Discard.Remove(card);
            card.Damage = (int)(card.HpMax * (100 - phoenix.Def.Value) / 100.0);
            var plume = card.TalentStatusValue("PlumePhoenix");
            if (card.HasTalentStatus("PlumePhoenix") && _rng.Next(0, 100) < plume)
            {
                card.TalentStatuses.RemoveAll(t => t.Type == "PlumePhoenix");
                Log.Add(Round, $"  Plume de Phénix : {Name(card)} garde son Phénix.");
            }
            else card.Toxes.Remove(phoenix);
            card.Statuses.Clear();
            card.DeathProcessed = false;
            card.Killer = null;
            p.Board.Add(card);
            Log.Add(Round, $"  Phénix : {Name(card)} revient avec {card.Hp}/{card.HpMax} PV.");
        }
    }

    // ================================================================ Évolution (talent)

    /// <summary>
    /// 🔒 EffectEvolution : selon le % de PV, les Tox d'origine de la carte sont remplacés par la même sorte
    /// (même type, même élément) d'une autre rareté.
    /// </summary>
    private void Evolve(CardState target, BrickDef effect)
    {
        if (target.HpMax <= 0 || !effect.Parameters.TryGetValue("evolutionData", out var data)) return;
        var pct = (float)target.Hp / target.HpMax * 100f;
        string? rarity = null;
        foreach (var step in data.EnumerateArray())
            if (pct <= step.GetProperty("palierValue").GetDouble())
            {
                rarity = step.GetProperty("rarity").GetString()?.Split(':', 2)[^1];
                break;
            }
        if (rarity is null) return;
        static bool Special(string t) => t is "Mystere" or "Metamorph" or "MetamorphSimple" or "Decouverte" or "CoeurCorrompu" or "StealTox";
        var ice = target.Get(StatusKind.Frozen);
        var changed = false;
        foreach (var t in target.Toxes.Where(t => t.IsOriginal && !Special(t.Def.StatusType) && t.Def.Rarity is not null && t.Def.Rarity != rarity).ToList())
        {
            var repl = _kb.AllTox.FirstOrDefault(x => x.StatusType == t.Def.StatusType && x.Element == t.Def.Element && x.Rarity == rarity);
            if (repl is null) continue;
            target.Toxes.Remove(t);
            AddTox(target, repl, isOriginal: true);
            Log.Add(Round, $"  Évolution : « {t.Def.AssetName} » devient « {repl.AssetName} ».");
            changed = true;
        }
        if (changed && ice is not null && target.Get(StatusKind.Frozen) == ice && _s.FrozenBrokenByToxEquip)
            BreakIce(target, target, thaw: true);
    }
}
