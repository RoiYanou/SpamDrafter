using System.Text;
using SpamDrafter.Simulator.Data;

namespace SpamDrafter.Simulator.Engine;

/// <summary>Sorte d'étape de l'historique d'une partie.</summary>
public enum StepKind { Start, Action, EndTurn, Reseed }

/// <summary>
/// Action d'un joueur décrite sans référence aux objets d'une partie précise (pour la rejouer dans une copie) :
/// carte par son identifiant, capacité par sa place dans la carte, Tox par son identifiant.
/// </summary>
public sealed record ActionRef(ActionKind Kind, string? CardUid, int AbilityIndex, string? TargetUid,
    string? ItemUuid, string? Origin);

/// <summary>Une étape de l'historique : lancement, action, fin de tour ou nouveau hasard.</summary>
public sealed record Step(StepKind Kind, ActionRef? Action = null, ulong Seed = 0, bool[]? IsAi = null);

// Historique et copie d'une partie (phase 7, lot A) : le Grand Joueur « essaie » des coups sur une copie.
// Principe : le simulateur est déterministe (même graine + mêmes décisions = même partie). Une copie est donc
// obtenue en rejouant, sur une nouvelle partie, la même graine et toutes les décisions prises jusqu'ici.
// Aucune copie champ par champ : impossible d'oublier un morceau de l'état.
public sealed partial class Duel
{
    private readonly ulong _seed;
    private readonly List<DuelSlot>[] _decks;
    private readonly List<Step> _steps = new();
    /// <summary>Décisions prises PENDANT les lancers (cibles, options, choix), dans l'ordre.</summary>
    private readonly List<int> _choices = new();
    /// <summary>Pendant un rejeu : décisions à reprendre au lieu de demander aux décideurs.</summary>
    private Queue<int>? _replay;

    public IReadOnlyList<Step> Steps => _steps;
    public IReadOnlyList<int> Choices => _choices;

    private void Record(Step step) => _steps.Add(step);

    // ================================================================ décisions pendant un lancer

    private int Ask(Func<int> decide, int count)
    {
        var k = _replay is { } q
            ? (q.Count > 0 ? q.Dequeue() : throw new InvalidOperationException("Rejeu : décision manquante."))
            : Math.Clamp(decide(), 0, count - 1);
        _choices.Add(k);
        return k;
    }

    private CardState AskTarget(CardState caster, AbilityDef def, List<CardState> options)
    {
        var k = Ask(() =>
        {
            var chosen = Decide(caster).ChooseTarget(this, caster, def, options);
            var i = options.IndexOf(chosen);
            return i >= 0 ? i : throw new InvalidOperationException("Le décideur a choisi une cible hors de la liste.");
        }, options.Count);
        return options[k];
    }

    private int AskOption(CardState caster, AbilityDef def, List<AbilityDef> options) =>
        Ask(() => Decide(caster).ChooseOption(this, caster, def, options), options.Count);

    private int AskAmong(CardState card, string kind, List<string> options) =>
        Ask(() => Decide(card).ChooseAmong(this, card, kind, options), options.Count);

    // ================================================================ hasard de réflexion

    /// <summary>
    /// Remplace le générateur de hasard par une nouvelle graine. Sert aux parties « imaginées » par le Grand Joueur :
    /// il ne doit pas connaître les vrais tirages à venir (dés, boutique). L'étape est gardée dans l'historique.
    /// </summary>
    public void Reseed(ulong seed)
    {
        Record(new Step(StepKind.Reseed, Seed: seed));
        _rng = new Rng(seed);
    }

    // ================================================================ copie par rejeu

    /// <summary>
    /// Copie exacte de la partie dans son état actuel, sans journal ni décideurs. Avec <paramref name="reseed"/>,
    /// la suite de la copie utilise un autre hasard que la partie réelle.
    /// À appeler entre deux actions (jamais pendant la résolution d'un lancer). Une partie modifiée directement
    /// (sans passer par Start, Apply, Cast, la boutique ou EndTurn) ne peut pas être copiée fidèlement.
    /// </summary>
    public Duel Copy(ulong? reseed = null)
    {
        var copy = new Duel(_kb, _decks[0], _decks[1], _seed);
        copy.Log.Enabled = false;
        copy.Players[0].IsAi = Players[0].IsAi;
        copy.Players[1].IsAi = Players[1].IsAi;
        copy._replay = new Queue<int>(_choices);
        try
        {
            foreach (var step in _steps)
                switch (step.Kind)
                {
                    case StepKind.Start:
                        copy.Players[0].IsAi = step.IsAi![0];
                        copy.Players[1].IsAi = step.IsAi![1];
                        copy.Start();
                        break;
                    case StepKind.Action: copy.Apply(copy.Resolve(step.Action!)); break;
                    case StepKind.EndTurn: copy.EndTurn(); break;
                    case StepKind.Reseed: copy.Reseed(step.Seed); break;
                }
            if (copy._replay.Count > 0) throw new InvalidOperationException("Rejeu : décisions en trop.");
        }
        finally { copy._replay = null; }
        copy.Players[0].IsAi = Players[0].IsAi;
        copy.Players[1].IsAi = Players[1].IsAi;
        if (reseed is { } s) copy.Reseed(s);
        return copy;
    }

    /// <summary>Décrit une action de cette partie sans référence à ses objets.</summary>
    public ActionRef RefOf(DuelAction action) => action.Kind switch
    {
        ActionKind.Cast => RefOf(action.Cast!),
        ActionKind.Buy => new ActionRef(ActionKind.Buy, action.Card!.Uid, -1, null, action.Item!.Uuid, action.Origin),
        ActionKind.Ban => new ActionRef(ActionKind.Ban, null, -1, null, action.Item!.Uuid, null),
        _ => new ActionRef(action.Kind, null, -1, null, null, null),
    };

    private static ActionRef RefOf(CastOption c) =>
        new(ActionKind.Cast, c.Card.Uid, c.Card.Abilities.IndexOf(c.Ability), c.Target?.Uid, null, c.Origin);

    /// <summary>Retrouve, dans cette partie, l'action décrite par <paramref name="r"/> (par exemple prise sur une copie).</summary>
    public DuelAction Resolve(ActionRef r)
    {
        switch (r.Kind)
        {
            case ActionKind.Cast:
            {
                var card = CardByUid(r.CardUid!);
                var target = r.TargetUid is null ? null : CardByUid(r.TargetUid);
                return DuelAction.Of(new CastOption(card, card.Abilities[r.AbilityIndex], target, r.Origin));
            }
            case ActionKind.Buy:
                return new DuelAction(ActionKind.Buy, Item: ShopItem(r.ItemUuid!), Card: CardByUid(r.CardUid!), Origin: r.Origin);
            case ActionKind.Ban:
                return new DuelAction(ActionKind.Ban, Item: ShopItem(r.ItemUuid!));
            default:
                return new DuelAction(r.Kind);
        }
    }

    private CardState CardByUid(string uid) =>
        Players.SelectMany(p => p.Board.Concat(p.Discard)).FirstOrDefault(c => c.Uid == uid)
        ?? throw new InvalidOperationException($"Carte introuvable : {uid}.");

    private ToxDef ShopItem(string uuid) =>
        ShopItems.FirstOrDefault(t => t.Uuid == uuid) ?? throw new InvalidOperationException($"Tox absent de la boutique : {uuid}.");

    // ================================================================ empreinte de l'état

    /// <summary>
    /// Texte qui résume tout l'état visible d'une partie (tests de la copie, comparaison de positions).
    /// Deux parties dans le même état ont la même empreinte.
    /// </summary>
    public string StateKey()
    {
        var sb = new StringBuilder();
        sb.Append($"R{Round} J{CurrentPlayer} P{Phase} W{Winner} S{_sub}|");
        foreach (var p in Players)
        {
            sb.Append($"[J{p.Id} $={p.Spamtons} L={p.Luck:R} K={p.KillCount} IA={p.IsAi} ");
            foreach (var (e, v) in p.ElementLevels.OrderBy(x => x.Key, StringComparer.Ordinal)) sb.Append($"{e}={v:R} ");
            sb.Append("plateau:");
            foreach (var c in p.Board) Card(sb, c);
            sb.Append(" défausse:");
            foreach (var c in p.Discard) Card(sb, c);
            sb.Append(']');
        }
        sb.Append(" boutique:").Append(string.Join(",", ShopItems.Select(t => t.Uuid)));
        sb.Append(" bannis:").Append(string.Join(",", Banned.Order(StringComparer.Ordinal)));
        sb.Append($" relance:{_addedRerollCost[0]},{_addedRerollCost[1]} ban:{_banDone[0]},{_banDone[1]}");
        sb.Append($" achat:{_boughtThisWindow[0]},{_boughtThisWindow[1]}");
        return sb.ToString();

        static void Card(StringBuilder sb, CardState c)
        {
            sb.Append($" {{{c.Uid} {c.Def.Id} x{c.Slot} {c.Element} {c.HpMax}-{c.Damage} ex={c.Exhausted} inc={c.IncomeBonus} pal={c.HpPalier}");
            foreach (var (k, v) in c.Resistances.OrderBy(x => x.Key, StringComparer.Ordinal)) sb.Append($" r{k}={v}");
            foreach (var a in c.Abilities)
                sb.Append($" a:{a.Def.Id}/{a.CurrentCooldown}/{a.MaxCooldown}/{a.ActivationsLeft}/{a.CounterLaunched}/{a.EchoModifier:R}");
            foreach (var t in c.Talents) sb.Append($" t:{t.Def.Id}/{t.ActivationsLeft}/{t.ChanceModifier:R}/{t.DrawnTurn}");
            foreach (var t in c.Toxes) sb.Append($" x:{t.Def.Uuid}/{t.IsOriginal}");
            foreach (var t in c.TalentStatuses) sb.Append($" ts:{t.Type}={t.Value:R}");
            foreach (var s in c.Statuses) sb.Append($" s:{s.Kind}/{s.Value}/{s.Duration}/{s.Permanent}/{s.Caster?.Uid}");
            sb.Append('}');
        }
    }
}
