using SpamDrafter.Engines.Analysis;
using SpamDrafter.Engines.Cooking;
using SpamDrafter.Engines.Meta;
using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;

namespace SpamDrafter.Engines;

/// <summary>Niveau d'effort choisi par le joueur : moins de duels (plus vite) ou plus de duels (plus juste).</summary>
public enum Effort { Rapide, Normal, Precis }

/// <summary>Une proposition prête à afficher : le deck, son score de confirmation et sa marge.</summary>
public sealed record GeneratedDeck(IReadOnlyList<DuelSlot> Deck, double Score, double Margin95, int Games, int RandomOpponents, bool UsesMeta = false,
    bool Precomputed = false);

/// <summary>Adversaires des moteurs : decks de référence du joueur (au moins 2), sinon méta synthétique livrée avec les
/// données (D-054), sinon decks tirés au hasard.</summary>
public enum OpponentSource { Random, Synthetic, Reference }

/// <summary>Méta synthétique et pré-calcul des générations à une carte, lus dans le dossier des données (phase 9, lot D).</summary>
public sealed record SyntheticData(IReadOnlyList<NamedDeck> Meta, IReadOnlyDictionary<string, IReadOnlyList<GeneratedDeck>>? OneCard,
    string? Warning)
{
    public static SyntheticData None { get; } = new([], null, null);

    /// <summary>
    /// Lit <c>meta/synthetique.json</c> et <c>meta/generations-1-carte.json</c>. Absents : rien (adversaires au hasard).
    /// Le pré-calcul n'est gardé que s'il a été fait contre ce fichier de méta exactement (empreinte), au réglage normal
    /// et avec la graine de l'application : il donne alors les mêmes propositions qu'un calcul lancé maintenant.
    /// </summary>
    public static SyntheticData Load(string knowledgeDir, SimKnowledge kb)
    {
        var metaPath = SyntheticMeta.DefaultPath(knowledgeDir);
        if (!File.Exists(metaPath)) return None;
        // Nom affiché : numéro dans la méta et les 4 cartes (le joueur reconnaît le deck à craindre).
        var meta = SyntheticMeta.Load(metaPath, kb).Select((m, i) => new NamedDeck(
            $"Méta {i + 1} ({string.Join(", ", m.Deck.Select(s => kb.CardName(kb.Card(s.CardId))))})", m.Deck)).ToList();
        var genPath = OneCardGenerations.DefaultPath(knowledgeDir);
        if (!File.Exists(genPath)) return new(meta, null, null);
        try
        {
            var head = OneCardGenerations.Read(genPath);
            if (head.MetaSha256 != OneCardGenerations.FileSha256(metaPath))
                return new(meta, null, "pré-calcul fait contre une autre méta : ignoré");
            if (head.Effort != Effort.Normal.ToString().ToLowerInvariant() || head.Seed != Coordinator.AppSeed)
                return new(meta, null, "pré-calcul fait avec d'autres réglages : ignoré");
            return new(meta, OneCardGenerations.Load(genPath, kb), null);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or System.Text.Json.JsonException)
        {
            return new(meta, null, "pré-calcul illisible : " + e.Message);
        }
    }
}

/// <summary>Rapport prêt à afficher : la fiche joueur (texte court) et le détail (Markdown).</summary>
public sealed record AnalysisResult(AnalysisReport Report, string Sheet, string Markdown);

/// <summary>Avancement commun aux deux travaux : texte de l'étape et fraction (0 à 1).</summary>
public sealed record WorkProgress(string Step, double Fraction);

/// <summary>
/// Coordinateur central (phase 8, lot D) : reçoit les demandes de l'interface, appelle le Grand Cuisinier ou le Grand
/// Analyseur sur un fil de travail, et rend des résultats prêts à afficher. Il ne contient aucune règle de jeu.
/// Un seul travail à la fois (les deux utilisent tous les cœurs).
/// </summary>
public sealed class Coordinator
{
    private readonly SimKnowledge _kb;
    private int _busy;

    public Coordinator(SimKnowledge kb, SyntheticData? synthetic = null) { _kb = kb; Synthetic = synthetic ?? SyntheticData.None; }

    /// <summary>Graine utilisée par l'application (même sélection = mêmes propositions) et par le pré-calcul.</summary>
    public const ulong AppSeed = 1;

    /// <summary>Méta synthétique et pré-calcul livrés avec les données (D-054).</summary>
    public SyntheticData Synthetic { get; }

    public string GameVersion => _kb.GameVersion;

    /// <summary>
    /// Decks de référence (D-050, D-051) : adversaires du Cuisinier et de l'Analyseur quand il y en a au moins 2.
    /// Mis à jour par l'application quand la liste des decks change.
    /// </summary>
    public IReadOnlyList<NamedDeck> Meta { get; set; } = [];
    /// <summary>Adversaires utilisés en ce moment : les decks de référence l'emportent sur la méta synthétique.</summary>
    public OpponentSource Opponents => Meta.Count >= 2 ? OpponentSource.Reference
        : Synthetic.Meta.Count >= 2 ? OpponentSource.Synthetic : OpponentSource.Random;
    public bool UsesMeta => Opponents != OpponentSource.Random;
    private IReadOnlyList<NamedDeck>? ActiveMeta => Opponents switch
    {
        OpponentSource.Reference => Meta,
        OpponentSource.Synthetic => Synthetic.Meta,
        _ => null,
    };
    private AnalysisSettings WithOpponents(AnalysisSettings s) =>
        s with { Meta = ActiveMeta, MetaIsSynthetic = Opponents == OpponentSource.Synthetic };

    /// <summary>
    /// Propositions pré-calculées (lot D) : une seule carte, talent au choix, adversaires = méta synthétique, réglage
    /// rapide ou normal (le pré-calcul est au réglage normal, au moins aussi juste que le rapide). Sinon null.
    /// </summary>
    public IReadOnlyList<GeneratedDeck>? Precomputed(IReadOnlyList<FixedCard> selection, Effort effort, ulong seed) =>
        selection is [{ TalentId: null } one] && effort != Effort.Precis && seed == AppSeed
        && Opponents == OpponentSource.Synthetic && Synthetic.OneCard is { } all
        && all.TryGetValue(one.CardId, out var props) && props.Count > 0 ? props : null;
    public bool IsBusy => Volatile.Read(ref _busy) == 1;

    public static CookSettings CookSettingsFor(Effort e) => e switch
    {
        Effort.Rapide => CookSettings.Fast,
        Effort.Precis => CookSettings.Precise,
        _ => CookSettings.Default,
    };

    public static AnalysisSettings AnalysisSettingsFor(Effort e) => e switch
    {
        Effort.Rapide => AnalysisSettings.Fast,
        Effort.Precis => AnalysisSettings.Precise,
        _ => AnalysisSettings.Default,
    };

    /// <summary>Complète une sélection de 1 à 4 cartes (talent null = au choix du Cuisinier).</summary>
    public Task<IReadOnlyList<GeneratedDeck>> GenerateAsync(IReadOnlyList<FixedCard> selection, Effort effort, ulong seed = 1,
        CancellationToken cancel = default, IProgress<WorkProgress>? progress = null) =>
        Precomputed(selection, effort, seed) is { } ready ? Task.FromResult(ready) : RunExclusive(() =>
        {
            var p = progress is null ? null : new Relay<CookProgress>(c => progress.Report(new WorkProgress(c.Stage switch
            {
                CookStage.Screening => "Tri de nombreux decks (joueur rapide)…",
                CookStage.Confirming => "Confirmation des meilleurs (joueur heuristique)…",
                _ => "Terminé.",
            }, c.Fraction)));
            var meta = ActiveMeta?.Select(m => m.Deck).ToList();
            var res = GrandCuisinier.Cook(_kb, selection, CookSettingsFor(effort) with { Seed = seed, Meta = meta }, cancel, p);
            return (IReadOnlyList<GeneratedDeck>)res.Proposals.Select(x => new GeneratedDeck(x.Deck, x.Confirmation.Score,
                x.Confirmation.Margin95, x.Confirmation.Counted, res.RandomOpponents, res.UsesMeta)).ToList();
        }, cancel);

    /// <summary>
    /// Prépare en arrière-plan le classement de référence des analyses (à l'ouverture de l'application) :
    /// la première fiche est alors aussi rapide que les suivantes. Ne bloque pas les autres calculs.
    /// </summary>
    public Task WarmUpAsync(CancellationToken cancel = default) =>
        Task.Run(() => GrandAnalyseur.WarmUp(_kb, WithOpponents(AnalysisSettings.Sheet), cancel), cancel);

    /// <summary>Analyse un deck de 4 cartes : <paramref name="complete"/> = faux pour la fiche joueur (rapide),
    /// vrai pour l'analyse complète (liens entre cartes, talents avec le joueur fin).</summary>
    public Task<AnalysisResult> AnalyseAsync(IReadOnlyList<DuelSlot> deck, bool complete, Effort effort = Effort.Normal, ulong seed = 1,
        CancellationToken cancel = default, IProgress<WorkProgress>? progress = null) =>
        RunExclusive(() =>
        {
            var p = progress is null ? null : new Relay<AnalysisProgress>(a => progress.Report(new WorkProgress(a.Stage switch
            {
                AnalysisStage.Calibration => "Classement des adversaires…",
                AnalysisStage.Strength => "Mesure de la force…",
                AnalysisStage.Talents => "Essai des talents…",
                AnalysisStage.Roles => "Rôle des cartes et liens entre elles…",
                AnalysisStage.Dangers => "Recherche des adversaires difficiles…",
                _ => "Terminé.",
            }, a.Fraction)));
            var settings = WithOpponents(complete ? AnalysisSettingsFor(effort) : AnalysisSettings.Sheet) with { Seed = seed };
            var report = GrandAnalyseur.Analyse(_kb, deck, settings, cancel, p);
            return new AnalysisResult(report, SheetWriter.ToText(_kb, report), ReportWriter.ToMarkdown(_kb, report));
        }, cancel);

    private Task<T> RunExclusive<T>(Func<T> work, CancellationToken cancel)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            throw new InvalidOperationException("Un calcul est déjà en cours.");
        return Task.Run(() =>
        {
            try { return work(); }
            finally { Volatile.Write(ref _busy, 0); }
        }, CancellationToken.None);
    }

    private sealed class Relay<T>(Action<T> f) : IProgress<T> { public void Report(T value) => f(value); }
}
