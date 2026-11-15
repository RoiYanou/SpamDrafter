using SpamDrafter.Simulator.Data;

namespace SpamDrafter.Simulator.Engine;

/// <summary>Carte d'un deck de duel et son talent (null = aucun). Les talents ne sont pas simulés en v1.</summary>
public sealed record DuelSlot(string CardId, string? TalentId = null);

public enum DuelPhase { NotStarted, Main, Ended }

/// <summary>
/// Un lancer possible : carte, capacité et cible (pour les capacités à cible choisie).
/// <paramref name="Origin"/> : emplacement de la cible libéré par une capacité « choosesSlot »
/// (« T:n° de Tox » ou « A:capacité » ; null = choix par défaut du jeu).
/// </summary>
public sealed record CastOption(CardState Card, AbilityState Ability, CardState? Target, string? Origin = null);

/// <summary>
/// Moteur du duel, version 1 (phase 5). Notre propre réécriture des règles de
/// docs/game/REGLES_DU_DUEL.md (D-012) : aucun code du jeu n'est copié ici.
/// Déterministe : même graine + mêmes décisions = même partie.
/// </summary>
public sealed partial class Duel
{
    private readonly SimKnowledge _kb;
    private Rng _rng;
    private readonly DuelSettings _s;

    public PlayerState[] Players { get; }
    public int Round { get; private set; }
    public int CurrentPlayer { get; private set; }
    public int FirstPlayer { get; private set; }
    public DuelPhase Phase { get; private set; } = DuelPhase.NotStarted;
    /// <summary>Gagnant : 0 ou 1 ; −1 = égalité ; null = partie en cours.</summary>
    public int? Winner { get; private set; }
    public DuelLog Log { get; } = new();
    /// <summary>Statistiques du duel (phase 8) ; null = non relevées (cas des copies de l'IA).</summary>
    public DuelStats? Stats { get; set; }
    public SimKnowledge Knowledge => _kb;

    public PlayerState Active => Players[CurrentPlayer];
    public PlayerState Opponent(int playerId) => Players[1 - playerId];

    public Duel(SimKnowledge kb, IReadOnlyList<DuelSlot> deck0, IReadOnlyList<DuelSlot> deck1, ulong seed)
    {
        _kb = kb;
        _s = kb.Settings;
        _rng = new Rng(seed);
        _seed = seed;
        _decks = [deck0.ToList(), deck1.ToList()];
        Players = new[] { new PlayerState(0), new PlayerState(1) };
        Place(Players[0], deck0);
        Place(Players[1], deck1);
    }

    /// <summary>§1.2 : 1ʳᵉ carte du deck en x = 4, 2ᵉ en x = 3, etc.</summary>
    private void Place(PlayerState p, IReadOnlyList<DuelSlot> deck)
    {
        if (deck.Count == 0 || deck.Count > _s.CardsPerDeck)
            throw new ArgumentException($"Un deck doit contenir de 1 à {_s.CardsPerDeck} cartes.");
        for (var i = 0; i < deck.Count; i++)
        {
            var def = _kb.Card(deck[i].CardId);
            var abilities = def.Abilities.Select(id => new AbilityState(_kb.Ability(id)));
            var card = new CardState($"{p.Id}.{i + 1}", def, p.Id, _s.CardsPerDeck - i, abilities);
            if (deck[i].TalentId is { } tid)
            {
                // 🔒 Le serveur refuse un talent qui n'appartient pas à la carte.
                if (!def.Talents.Contains(tid) || _kb.Talent(tid) is not { } talent)
                    throw new ArgumentException($"Le talent « {tid} » n'appartient pas à la carte {def.SanitizedName}.");
                _chosenTalents[card] = talent;
            }
            p.Board.Add(card);
            // Passifs innés : Tox posés dès le départ (🔒 Card.SetAbilities, « isOriginal »).
            foreach (var uuid in def.InnatePassives)
                if (_kb.Tox(uuid) is { } tox) AddTox(card, tox, isOriginal: true);
        }
    }

    // ================================================================ lancement (§1.4)

    /// <summary>Lance la partie : premier joueur, valeurs de départ, compensation du second joueur, 1ᵉʳ tour.</summary>
    public void Start()
    {
        if (Phase != DuelPhase.NotStarted) throw new InvalidOperationException("Partie déjà lancée.");
        Record(new Step(StepKind.Start, IsAi: [Players[0].IsAi, Players[1].IsAi]));
        FirstPlayer = _s.PlayerIdFirstPlayer is 0 or 1 ? _s.PlayerIdFirstPlayer : _rng.Next(0, 2);
        CurrentPlayer = FirstPlayer;
        Round = 1;
        foreach (var p in Players)
        {
            p.Luck = _s.StartLuck;
            foreach (var e in _s.RandomElements) p.ElementLevels[e] = 0;
        }
        // Compensation : le second joueur commence avec toutes ses recharges réduites de 1 (minimum 0).
        foreach (var c in Opponent(FirstPlayer).Board)
            foreach (var a in c.Abilities)
                a.CurrentCooldown = Math.Max(0, a.CurrentCooldown - 1);

        Log.Add(0, $"Début du duel. Premier joueur : J{FirstPlayer + 1}.");
        foreach (var c in Players.SelectMany(p => p.Board).Where(c => c.Toxes.Count > 0))
            Log.Add(0, $"{Name(c)} : Tox de départ {string.Join(", ", c.Toxes.Select(t => t.Def.AssetName))}.");
        AttachTalents();
        StartTurn();
    }

    // ================================================================ début de tour (§2)

    private void StartTurn()
    {
        if (Phase == DuelPhase.Ended) return;
        var p = Active;
        Log.Add(Round, $"— Tour de J{p.Id + 1} (round {Round}) —");

        _sub = SubPhase.StartTurn;
        _lastTarget = null;                                             // étape 1 (ClearTurnData)
        _lastPlayed = null;
        p.Luck += _s.LuckPerTurn;                                       // étape 3
        ApplyOvertime(p);                                               // étape 4
        ChargeShields(p);                                               // étape 5
        foreach (var c in p.Board.Where(c => !c.Has(StatusKind.Sleep))) c.Exhausted = false;  // étape 6
        var income = Round + p.Board.Sum(c => c.Income);               // étape 7
        AddSpamtons(p, income, null);
        foreach (var c in p.Board) c.Remove(StatusKind.Revealed);       // étape 8 : les cartes révélées se recachent
        foreach (var c in p.Board.ToList()) Trigger("StartOfTurn", c, c);   // étape 10
        for (var i = p.Board.Count - 1; i >= 0; i--)                    // étape 11
            if (i < p.Board.Count) StartTurnToxTicks(p.Board[i], p);
        OpenShopIfEvenRound(p);                                         // étape 12 : boutique les rounds pairs
        Settle();                                                       // étape 13 : résolution de la file
        _sub = SubPhase.Main;
        if (Phase != DuelPhase.Ended) Phase = DuelPhase.Main;          // étape 14
    }

    /// <summary>
    /// 🔒 + 📊 Overtime (§8.3) : à partir du round seuil, chaque carte du joueur actif perd des PV max.
    /// perte = max(1, arrondi bas(PVbase × R × (1 + S × (round − seuil)))), PVbase = max(PV max, PV de base).
    /// </summary>
    private void ApplyOvertime(PlayerState p)
    {
        if (Round < _s.TurnAfterOvertime) return;
        foreach (var c in p.Board)
        {
            var loss = OvertimeLoss(c, Round);
            c.HpMax -= loss;
            DestroyCamouflage(c);
            Log.Add(Round, $"Overtime : {Name(c)} perd {loss} PV max ({c.Hp}/{Math.Max(c.HpMax, 0)}).");
            if (c.Hp <= 0) c.Killer ??= c;
            if (c.Has(StatusKind.Frozen) && c.Hp > 0) BreakIce(c, c, thaw: true);
        }
    }

    /// <summary>Calcul exact de la perte de PV max en overtime (public pour les tests).</summary>
    public int OvertimeLoss(CardState c, int round)
    {
        // Calcul en nombres « float » comme le jeu (Unity), pour obtenir les mêmes arrondis.
        var baseHp = (float)Math.Max(Math.Max(c.HpMax, 0), c.Def.Hp) * (float)_s.OvertimeReducingRatio;
        var extra = Math.Max(0, round - _s.TurnAfterOvertime);
        return Math.Max(1, (int)(baseHp + baseHp * extra * (float)_s.OvertimeScalingRatio));
    }

    // ================================================================ fin de tour (§8.1)

    public void EndTurn()
    {
        if (Phase != DuelPhase.Main) throw new InvalidOperationException("Pas de tour en cours.");
        Record(new Step(StepKind.EndTurn));
        _sub = SubPhase.EndTurn;
        CloseShop(Active);                                              // boutique vidée
        // 1. Durées des statuts −1 (deux joueurs).
        foreach (var c in Players.SelectMany(p => p.Board))
        {
            foreach (var s in c.Statuses.Where(s => !s.Permanent)) s.Duration--;
            c.Statuses.RemoveAll(s => !s.Permanent && s.Duration <= 0);
        }
        // 2. Déclencheurs de fin de tour : v2. 3. Recharges −1 pour le joueur actif.
        foreach (var c in Active.Board)
            foreach (var a in c.Abilities)
                a.CurrentCooldown = Math.Max(0, a.CurrentCooldown - 1);

        // 4. Joueur suivant ; le round augmente quand on revient au premier joueur.
        CurrentPlayer = 1 - CurrentPlayer;
        if (CurrentPlayer == FirstPlayer) Round++;
        CheckForWinner();
        StartTurn();
    }

    /// <summary>
    /// Après chaque résolution : retire les cartes mortes (« ManageDiscard »), puis recalcule les recharges
    /// qui baissent avec les cartes disparues (🔒 Card.SetGoneCardsCooldowns).
    /// </summary>
    private void UpdateOngoing()
    {
        SettleDeaths();
        var gone = Players.Sum(p => p.Discard.Count + p.Board.Count(c => !c.IsAlive));
        foreach (var rt in AllCards.SelectMany(c => c.Abilities).Where(a => a.Def.CooldownDropsPerCardGone))
        {
            var newMax = Math.Max(rt.Def.Cooldown - gone, 0);
            var delta = newMax - rt.MaxCooldown;
            rt.MaxCooldown = newMax;
            rt.CurrentCooldown = Math.Clamp(rt.CurrentCooldown + delta, 0, Math.Max(newMax + rt.BaseCooldownModificator, 0));
        }
    }

    // ================================================================ mort et victoire (§7, §8.2)

    /// <summary>
    /// 🔒 GameLogic.KillCard (§7) : Revanche → Reset → Cimetière et Vengeur → « ennemi tué » → Bomba →
    /// défausse (Provocation et Fumée retirées, 50 Spamtons, « mort d'une autre carte », « à ma mort »).
    /// </summary>
    private void KillCard(CardState killer, CardState target, AbilityDef? ability = null)
    {
        if (target.DeathProcessed || !Players[target.Owner].Board.Contains(target)) return;
        target.DeathProcessed = true;
        if (Stats is not null)
        {
            Stats.Of(target).Died = true;
            if (killer.Owner != target.Owner) Stats.Of(killer).Kills++;
        }
        target.Remove(StatusKind.Frozen);                         // dégel sans dégâts
        Log.Add(Round, $"{Name(target)} meurt.");
        if (target.HasTox("Revanche") && !target.IsSilencedOrFrozen)
        {
            _insertNext = true;                                   // la Revanche passe en tête de la file
            Revanche(target, killer);
            _insertNext = false;
        }
        ManageReset(killer, target, ability);
        foreach (var c in AllCards.ToList())
        {
            if (ToxSum(c, "Cimetierre") is { Count: > 0 } grave)
                AddElementLevel(Players[c.Owner], null, (int)grave.Total, source: c);
            if (c.HasTox("Vengeur") && c != killer)
            {
                MakeAllAvailable(c);
                if (c == _castCaster) _vengeurKiller = killer;
            }
        }
        if (killer.Owner != target.Owner)
        {
            Players[killer.Owner].KillCount++;
            Trigger("OnKill", killer, target);
        }
        ManageBomba(target, ability);
        target.Remove(StatusKind.Taunt);
        target.Remove(StatusKind.Smoked);
        Log.Add(Round, $"  J{target.Owner + 1} gagne {_s.GoldPerKill} Spamtons.");
        AddSpamtons(Players[target.Owner], _s.GoldPerKill, null);  // 📊 goldPerKill
        // 🔒 DiscardCard : « à la mort d'une autre carte » pour toutes les autres cartes, puis « à ma mort ».
        foreach (var other in AllCards.Where(c => c != target).ToList()) Trigger("OnDeathOther", other, target);
        Trigger("OnDeath", target, target);
    }

    /// <summary>
    /// 🔒 GameLogic.ManageDiscard : retire du plateau les cartes mortes, fait revenir celles qui ont un Phénix,
    /// puis la dernière carte d'un joueur perd son Camouflage.
    /// </summary>
    private void SettleDeaths()
    {
        _settlingDeaths = true;
        foreach (var p in Players)
            foreach (var dead in p.Board.Where(c => c.Hp <= 0 || c.DeathProcessed).ToList())
            {
                KillCard(dead.Killer ?? dead, dead);
                p.Board.Remove(dead);
                p.Discard.Add(dead);
            }
        _settlingDeaths = false;
        foreach (var p in Players) PhoenixRevive(p);
        foreach (var p in Players)
        {
            var alive = p.Board.Where(c => c.IsAlive).ToList();
            if (alive.Count == 1) DestroyCamouflage(alive[0]);
        }
    }

    private void CheckForWinner()
    {
        if (Phase == DuelPhase.Ended) return;
        var alive = Players.Where(p => p.Board.Count > 0).ToList();
        if (alive.Count == 2) return;
        Winner = alive.Count == 1 ? alive[0].Id : -1;
        Phase = DuelPhase.Ended;
        Log.Add(Round, Winner == -1 ? "Égalité : plus aucune carte des deux côtés." : $"Victoire de J{Winner + 1}.");
    }

    // ================================================================ outils

    public string Name(CardState c) => $"{_kb.CardName(c.Def)} (J{c.Owner + 1}, x={c.Slot})";

    /// <summary>Arrondi de Unity (Mathf.RoundToInt) : au plus proche, à égalité vers le pair.</summary>
    internal static int RoundToInt(double v) => (int)Math.Round((float)v, MidpointRounding.ToEven);

    public IEnumerable<CardState> AllCards => Players.SelectMany(p => p.Board);
}
