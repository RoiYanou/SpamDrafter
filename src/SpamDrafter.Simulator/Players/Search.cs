using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;

namespace SpamDrafter.Simulator.Players;

/// <summary>
/// Politique rapide (phase 7, lot C) : sert à IMAGINER la suite d'une partie pendant la recherche, pour les deux camps.
/// Elle lance ses capacités dans l'ordre, avec les règles de ciblage simples (pas de coup sur ses alliées sauf soin),
/// et passe la boutique. Bien plus rapide que le joueur heuristique, mais moins fine.
/// </summary>
public sealed class FastPlayer : IDuelPlayer
{
    private readonly RuleDecider _rules;
    /// <summary>Phase 8, lot H : lancer d'abord les capacités qui donnent des niveaux d'élément ou du critique.</summary>
    public bool BuffFirst { get; init; } = true;
    public FastPlayer(ulong seed) { _rules = new RuleDecider(seed); }
    internal FastPlayer(RuleDecider rules) { _rules = rules; }

    public DuelAction? Choose(Duel duel, IReadOnlyList<DuelAction> options)
    {
        DuelAction? skip = null;
        var groups = options.Where(o => o.Kind == ActionKind.Cast).GroupBy(o => (o.Cast!.Card, o.Cast!.Ability));
        if (BuffFirst) groups = groups.OrderByDescending(g => BuffValue(duel.Knowledge, g.Key.Ability.Def));   // tri stable
        foreach (var group in groups)
        {
            var (card, ability) = group.Key;
            var targets = group.Select(o => o.Cast!.Target).OfType<CardState>().Distinct().ToList();
            if (targets.Count == 0) return group.First();
            var t = _rules.ChooseTarget(duel, card, ability.Def, targets);
            if (t.Owner == card.Owner && !RuleDecider.IsFriendly(duel.Knowledge, ability.Def)) continue;
            return group.First(o => o.Cast!.Target == t);
        }
        foreach (var o in options) if (o.Kind == ActionKind.Skip) skip = o;
        return skip;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<AbilityDef, double> BuffCache =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Ce qu'une capacité donne à son camp (🔒 StatToModify 1 = niveaux d'élément, 25 = chance de critique,
    /// 40 = puissance critique ; valeurs positives seulement). 0 = rien à lancer en premier.
    /// </summary>
    public static double BuffValue(SimKnowledge kb, AbilityDef a) => BuffCache.GetOrAdd(a, def =>
        def.Actions.Where(x => x.Min > 0 && kb.Effect(x.Effect)?.Class is "EffectAddAllStat" or "EffectAddStat" or "EffectAddStatPerLaunch")
            .Sum(x => kb.Effect(x.Effect)!.Int("StatToModify") switch { 1 => x.Min, 25 => x.Min / 10, 40 => x.Min, _ => 0 }));

    public CardState ChooseTarget(Duel duel, CardState caster, AbilityDef ability, IReadOnlyList<CardState> targets) =>
        _rules.ChooseTarget(duel, caster, ability, targets);

    public int ChooseOption(Duel duel, CardState caster, AbilityDef ability, IReadOnlyList<AbilityDef> options) =>
        _rules.ChooseOption(duel, caster, ability, options);

    public int ChooseAmong(Duel duel, CardState card, string kind, IReadOnlyList<string> options) =>
        _rules.ChooseAmong(duel, card, kind, options);
}

/// <summary>Réglages du joueur par recherche (réglages de l'IA, pas du jeu).</summary>
public sealed record SearchSettings
{
    /// <summary>Nombre d'actions approfondies (les mieux classées par le joueur heuristique).</summary>
    public int TopK { get; init; } = 6;
    /// <summary>Nombre de suites imaginées par action (hasards différents, les mêmes pour toutes les actions).</summary>
    public int Rollouts { get; init; } = 8;
    /// <summary>Nombre de tours adverses imaginés après le mien (1 = jusqu'au début de mon prochain tour).</summary>
    public int Horizon { get; init; } = 1;
    /// <summary>
    /// Dans les suites imaginées, le reste de MON tour est joué par le joueur heuristique (1 essai par action)
    /// au lieu de la politique rapide : plus juste, mais plus lent.
    /// </summary>
    public bool GreedyRest { get; init; } = false;
    /// <summary>Considérer aussi « finir mon tour maintenant ».</summary>
    public bool ConsiderEndTurn { get; init; } = true;

    public static SearchSettings Default { get; } = new();
}

/// <summary>
/// Grand Joueur par recherche (phase 7, lot C). Le joueur heuristique classe les actions ; pour les meilleures,
/// le joueur imagine plusieurs fois la suite (avec un hasard à lui) : son action, le reste de son tour puis le tour
/// adverse joués par la politique rapide. Il garde l'action dont la note moyenne à la fin est la meilleure.
/// Mêmes règles d'information que l'heuristique (Fumée : KI-030 ; tirages futurs inconnus).
/// </summary>
public sealed class SearchPlayer : IDuelPlayer
{
    private readonly HeuristicPlayer _h;
    private readonly SearchSettings _s;
    private readonly Rng _rng;
    private readonly FastPlayer _fast;
    private readonly HeuristicPlayer _rest;

    public SearchPlayer(ulong seed, SearchSettings? settings = null, HeuristicWeights? weights = null)
    {
        _h = new HeuristicPlayer(seed ^ 0x2545F4914F6CDD1DUL, weights);
        _s = settings ?? SearchSettings.Default;
        _rng = new Rng(seed);
        _fast = new FastPlayer(_h.Rules);
        _rest = new HeuristicPlayer(seed ^ 0x9E3779B97F4A7C15UL, weights, samples: 1);
    }

    public DuelAction? Choose(Duel duel, IReadOnlyList<DuelAction> options)
    {
        _h.ClearPlan();
        if (options.Count == 0) return null;
        var r = _h.Rank(duel, options);
        var playable = Enumerable.Range(0, options.Count).Where(i => r.Playable[i])
            .OrderByDescending(i => r.Scores[i]).ToList();
        // Fumée : une seule action par groupe « carte cachée » (elles ont la même note).
        var seen = new HashSet<int>();
        var candidates = new List<int>();
        foreach (var i in playable)
        {
            if (r.HiddenGroups.TryGetValue(i, out var g) && !seen.Add(g[0])) continue;
            candidates.Add(i);
            if (candidates.Count == _s.TopK) break;
        }
        if (candidates.Count == 0) return null;
        if (candidates.Count == 1 && !_s.ConsiderEndTurn) return _h.Commit(options, r, candidates[0]);

        var me = duel.CurrentPlayer;
        var seeds = Enumerable.Range(0, _s.Rollouts).Select(_ => _rng.NextULong()).ToArray();
        var best = -1;
        var bestValue = double.NegativeInfinity;
        foreach (var i in candidates)
        {
            var v = seeds.Average(seed => Rollout(duel, options[i], r.Plans[i], me, seed));
            if (v > bestValue) { bestValue = v; best = i; }
        }
        if (_s.ConsiderEndTurn && seeds.Average(seed => Rollout(duel, null, null, me, seed)) > bestValue) return null;
        return _h.Commit(options, r, best);
    }

    /// <summary>Une suite imaginée : l'action (null = finir le tour), le reste de mon tour, puis les tours adverses.</summary>
    private double Rollout(Duel duel, DuelAction? action, HeuristicPlayer.Plan? plan, int me, ulong seed)
    {
        var copy = duel.Clone(reseed: seed);
        copy.Deciders[me] = new HeuristicPlayer.Script(_h.Rules, me, plan?.Index);
        copy.Deciders[1 - me] = _fast;
        if (action is not null)
        {
            copy.Apply(copy.Resolve(duel.RefOf(action)));
            if (_s.GreedyRest)
            {
                copy.Deciders[me] = _rest;
                PlayTurn(copy, _rest);
            }
            else PlayTurn(copy);
            copy.Deciders[me] = _fast;
        }
        if (copy.Phase == DuelPhase.Main) copy.EndTurn();
        for (var h = 0; h < _s.Horizon && copy.Phase == DuelPhase.Main; h++)
        {
            PlayTurn(copy);
            if (copy.Phase == DuelPhase.Main) copy.EndTurn();
            if (h + 1 < _s.Horizon && copy.Phase == DuelPhase.Main)   // mon tour suivant, puis le leur
            {
                PlayTurn(copy);
                if (copy.Phase == DuelPhase.Main) copy.EndTurn();
            }
        }
        return Evaluator.Score(copy, me, _h.Weights);
    }

    private void PlayTurn(Duel copy, IDuelPlayer? policy = null)
    {
        policy ??= _fast;
        var player = copy.CurrentPlayer;
        for (var n = 0; n < 60 && copy.Phase == DuelPhase.Main && copy.CurrentPlayer == player; n++)
        {
            var c = policy.Choose(copy, copy.LegalActions());
            if (c is null) return;
            copy.Apply(c);
        }
    }

    public CardState ChooseTarget(Duel duel, CardState caster, AbilityDef ability, IReadOnlyList<CardState> targets) =>
        _h.ChooseTarget(duel, caster, ability, targets);

    public int ChooseOption(Duel duel, CardState caster, AbilityDef ability, IReadOnlyList<AbilityDef> options) =>
        _h.ChooseOption(duel, caster, ability, options);

    public int ChooseAmong(Duel duel, CardState card, string kind, IReadOnlyList<string> options) =>
        _h.ChooseAmong(duel, card, kind, options);
}
