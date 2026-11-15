using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;

namespace SpamDrafter.Simulator.Players;

/// <summary>Un joueur qui décide pendant son tour : une action (lancer, boutique), ou finir le tour (null).</summary>
public interface IDuelPlayer : IDuelDecider
{
    DuelAction? Choose(Duel duel, IReadOnlyList<DuelAction> options);
}

/// <summary>
/// Joueur « au hasard » : choisit au hasard une sorte d'action (lancer, acheter, relancer, passer, bannir),
/// puis une action de cette sorte, jusqu'à ce qu'il n'y en ait plus.
/// Sert aux tests en masse et de référence pour le futur Grand Joueur (phase 7).
/// </summary>
public sealed class RandomPlayer : IDuelPlayer
{
    private readonly Rng _rng;
    public RandomPlayer(ulong seed) { _rng = new Rng(seed); }

    public DuelAction? Choose(Duel duel, IReadOnlyList<DuelAction> options)
    {
        if (options.Count == 0) return null;
        var kinds = options.Select(o => o.Kind).Distinct().ToList();
        var kind = kinds[_rng.Next(0, kinds.Count)];
        var ofKind = options.Where(o => o.Kind == kind).ToList();
        return ofKind[_rng.Next(0, ofKind.Count)];
    }

    public CardState ChooseTarget(Duel duel, CardState caster, AbilityDef ability, IReadOnlyList<CardState> targets) =>
        targets[_rng.Next(0, targets.Count)];

    public int ChooseOption(Duel duel, CardState caster, AbilityDef ability, IReadOnlyList<AbilityDef> options) =>
        _rng.Next(0, options.Count);

    public int ChooseAmong(Duel duel, CardState card, string kind, IReadOnlyList<string> options) =>
        _rng.Next(0, options.Count);
}

/// <summary>Résultat d'un duel joué jusqu'au bout.</summary>
public sealed record DuelResult(int? Winner, int Rounds, int Casts, bool Finished);

public static class DuelRunner
{
    /// <summary>Garde-fou : nombre maximal d'actions par tour (aucune capacité de carte n'a une recharge de 0).</summary>
    public const int MaxCastsPerTurn = 100;
    /// <summary>Garde-fou : l'overtime termine toute partie bien avant (§8.3).</summary>
    public const int MaxRounds = 200;

    public static DuelResult Play(Duel duel, IDuelPlayer p0, IDuelPlayer p1)
    {
        var players = new[] { p0, p1 };
        duel.Deciders[0] = p0;
        duel.Deciders[1] = p1;
        if (duel.Phase == DuelPhase.NotStarted) duel.Start();
        var casts = 0;
        while (duel.Phase == DuelPhase.Main && duel.Round <= MaxRounds)
        {
            for (var n = 0; n < MaxCastsPerTurn && duel.Phase == DuelPhase.Main; n++)
            {
                var choice = players[duel.CurrentPlayer].Choose(duel, duel.LegalActions());
                if (choice is null) break;
                duel.Apply(choice);
                casts++;
            }
            if (duel.Phase == DuelPhase.Main) duel.EndTurn();
        }
        return new DuelResult(duel.Winner, duel.Round, casts, duel.Phase == DuelPhase.Ended);
    }
}

/// <summary>Joueurs disponibles par leur nom (commande de duel, banc d'essai).</summary>
public static class PlayerCatalog
{
    public static IReadOnlyList<string> Names { get; } = ["hasard", "heuristique", "recherche", "rapide", "planificateur"];

    /// <summary>Fabrique du joueur nommé (null si le nom est inconnu).</summary>
    public static Func<ulong, IDuelPlayer>? Factory(string name) => name.ToLowerInvariant() switch
    {
        "hasard" or "random" => seed => new RandomPlayer(seed),
        "heuristique" or "heuristic" => seed => new HeuristicPlayer(seed),
        "recherche" or "search" => seed => new SearchPlayer(seed),
        "rapide" or "fast" => seed => new FastPlayer(seed),
        "planificateur" or "planner" => seed => new TurnPlanner(seed),
        // Versions d'avant le lot H de la phase 8, gardées pour comparer.
        "heuristique-v1" => seed => new HeuristicPlayer(seed, HeuristicWeights.V1),
        // Réglage d'essai : « heuristique-combo:X » = la note du lot H avec X tours pour les niveaux et le critique.
        _ when name.StartsWith("heuristique-combo:", StringComparison.OrdinalIgnoreCase)
               && double.TryParse(name[18..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
            => seed => new HeuristicPlayer(seed, HeuristicWeights.Default with { ElementUse = x, CritUse = x }),
        "rapide-v1" => seed => new FastPlayer(seed) { BuffFirst = false },
        _ => null,
    };
}
