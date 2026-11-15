using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;

namespace SpamDrafter.Simulator.Players;

/// <summary>Réglages du planificateur de tour (réglages de l'IA, pas du jeu).</summary>
public sealed record PlannerSettings
{
    /// <summary>Premières actions étudiées (les mieux notées par l'heuristique), en plus de « finir le tour ».</summary>
    public int TopK { get; init; } = 3;
    /// <summary>Suites gardées à chaque étape (recherche « en faisceau »).</summary>
    public int Beam { get; init; } = 3;
    /// <summary>Nombre maximal d'actions imaginées dans le tour (la première comprise).</summary>
    public int MaxDepth { get; init; } = 6;

    public static PlannerSettings Default { get; } = new();
}

/// <summary>
/// Planificateur de tour (phase 8, lot H) : au lieu de choisir une action à la fois, il imagine plusieurs suites
/// complètes pour SON tour (sur des copies de la partie, avec un hasard à lui) et joue la première action de la
/// meilleure suite. Il peut donc jouer un renfort qui ne rapporte rien tout de suite mais rend l'attaque suivante
/// plus forte (combo dans le tour). Il recommence à chaque décision, avec le vrai résultat des tirages.
/// Mêmes règles d'information que l'heuristique pour la 1ʳᵉ action (Fumée, KI-030) ; dans les suites imaginées,
/// les copies voient les cartes cachées (approximation notée KI-056).
/// </summary>
public sealed class TurnPlanner : IDuelPlayer
{
    private readonly HeuristicPlayer _h;       // classement de la vraie position (2 essais par action)
    private readonly HeuristicPlayer _inner;   // classement dans les suites imaginées (1 essai : plus rapide)
    private readonly PlannerSettings _s;
    private readonly Rng _rng;

    public TurnPlanner(ulong seed, PlannerSettings? settings = null, HeuristicWeights? weights = null)
    {
        _h = new HeuristicPlayer(seed ^ 0x51ED270B27UL, weights);
        _inner = new HeuristicPlayer(seed ^ 0x7F4A7C159E37UL, weights, samples: 1);
        _s = settings ?? PlannerSettings.Default;
        _rng = new Rng(seed);
    }

    public DuelAction? Choose(Duel duel, IReadOnlyList<DuelAction> options)
    {
        _h.ClearPlan();
        if (options.Count == 0) return null;
        var me = duel.CurrentPlayer;
        var root = _h.Rank(duel, options);
        var best = -1;
        var bestValue = root.Baseline;   // finir le tour maintenant
        foreach (var i in Top(root, options.Count, _s.TopK))
        {
            var value = Continue(duel, options[i], root.Plans[i], me);
            if (value > bestValue) { bestValue = value; best = i; }
        }
        return best < 0 ? null : _h.Commit(options, root, best);
    }

    /// <summary>Valeur de la meilleure suite de mon tour qui commence par <paramref name="action"/>.</summary>
    private double Continue(Duel duel, DuelAction action, HeuristicPlayer.Plan? plan, int me)
    {
        var start = Step(duel, action, plan?.Index, me);
        var bestValue = Evaluator.Score(start, me, _h.Weights);
        var beams = new List<(Duel Duel, double Score)> { (start, bestValue) };
        for (var depth = 1; depth < _s.MaxDepth && beams.Count > 0; depth++)
        {
            var next = new List<(Duel, double)>();
            foreach (var (b, _) in beams)
            {
                if (b.Phase != DuelPhase.Main || b.CurrentPlayer != me) continue;
                var opts = b.LegalActions();
                if (opts.Count == 0) continue;
                var r = _inner.Rank(b, opts);
                foreach (var i in Top(r, opts.Count, _s.TopK))
                {
                    var child = Step(b, opts[i], r.Plans[i]?.Index, me);
                    var score = Evaluator.Score(child, me, _h.Weights);
                    next.Add((child, score));
                    if (score > bestValue) bestValue = score;
                }
            }
            beams = next.OrderByDescending(x => x.Item2).Take(_s.Beam).ToList();
        }
        return bestValue;
    }

    /// <summary>Une action jouée sur une copie (hasard à moi), avec la réponse prévue à sa 1ʳᵉ question.</summary>
    private Duel Step(Duel duel, DuelAction action, int? planIndex, int me)
    {
        var copy = duel.Clone(reseed: _rng.NextULong());
        copy.Deciders[me] = new HeuristicPlayer.Script(_h.Rules, me, planIndex);
        copy.Deciders[1 - me] = _h.Rules;
        copy.Apply(copy.Resolve(duel.RefOf(action)));
        return copy;
    }

    /// <summary>Les meilleures actions jouables, une seule par groupe « carte cachée » (même note).</summary>
    private static IEnumerable<int> Top(HeuristicPlayer.Ranking r, int count, int k)
    {
        var seen = new HashSet<int>();
        return Enumerable.Range(0, count).Where(i => r.Playable[i]).OrderByDescending(i => r.Scores[i])
            .Where(i => !r.HiddenGroups.TryGetValue(i, out var g) || seen.Add(g[0])).Take(k);
    }

    public CardState ChooseTarget(Duel duel, CardState caster, AbilityDef ability, IReadOnlyList<CardState> targets) =>
        _h.ChooseTarget(duel, caster, ability, targets);

    public int ChooseOption(Duel duel, CardState caster, AbilityDef ability, IReadOnlyList<AbilityDef> options) =>
        _h.ChooseOption(duel, caster, ability, options);

    public int ChooseAmong(Duel duel, CardState card, string kind, IReadOnlyList<string> options) =>
        _h.ChooseAmong(duel, card, kind, options);
}
