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
    bool Precomputed = false, bool Measured = false);

/// <summary>Adversaires des moteurs (D-065) : la méta synthétique livrée avec les données, contrôlée par le Studio ;
/// à défaut (données sans méta, développement), des decks tirés au hasard.</summary>
public enum OpponentSource { Random, Synthetic }

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
            if (head.PlayerSha256 != SpamDrafter.Simulator.Players.PlayerWeightsFile.Fingerprint(kb.PlayerWeights))
                return new(meta, null, "pré-calcul fait avec d'autres poids du Grand Joueur : ignoré");
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
    /// À changer quand un moteur change de façon à modifier ses résultats : les résultats gardés en mémoire (phase 12, lot B)
    /// d'une autre version des moteurs ne sont alors plus réutilisés.
    /// </summary>
    public const int EngineVersion = 2;   // 2 : note sur 100 mesurée de la même façon partout (D-074)

    /// <summary>
    /// Ce dont dépendent les résultats d'un moteur en dehors de la demande : version de SPAM, adversaires (méta), poids du
    /// Grand Joueur, version des moteurs. Deux demandes identiques avec le même contexte donnent exactement les mêmes
    /// résultats (les moteurs sont reproductibles) : un résultat gardé en mémoire est alors sûr à réutiliser.
    /// </summary>
    public string CacheContext
    {
        get
        {
            var meta = string.Join(";", Synthetic.Meta.Select(m => string.Join(",", m.Deck.Select(s => $"{s.CardId}:{s.TalentId}"))));
            var text = $"{_kb.GameVersion}|{Opponents}|{meta}|{SpamDrafter.Simulator.Players.PlayerWeightsFile.Fingerprint(_kb.PlayerWeights)}|e{EngineVersion}";
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..24];
        }
    }

    /// <summary>Adversaires utilisés : la méta synthétique (D-065), ou des decks au hasard si les données n'en ont pas.</summary>
    public OpponentSource Opponents => Synthetic.Meta.Count >= 2 ? OpponentSource.Synthetic : OpponentSource.Random;
    public bool UsesMeta => Opponents != OpponentSource.Random;
    private IReadOnlyList<NamedDeck>? ActiveMeta => Opponents == OpponentSource.Synthetic ? Synthetic.Meta : null;
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

    /// <summary>Tests automatiques seulement : réglages miniatures du Cuisinier (les tests de l'interface vérifient les
    /// boutons et messages ; la qualité des moteurs est vérifiée par leurs propres tests).</summary>
    public CookSettings? CookOverride { get; init; }
    /// <summary>Tests automatiques seulement : réglages miniatures de l'Analyseur (fiche et analyse complète).</summary>
    public AnalysisSettings? AnalysisOverride { get; init; }

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

    /// <summary>Réglages de la mesure de référence de la note (D-074) : ceux de la fiche joueur, avec la méta ou des decks au hasard.</summary>
    private AnalysisSettings NoteSettings(ulong seed) => WithOpponents(AnalysisOverride ?? AnalysisSettings.Sheet) with { Seed = seed };

    /// <summary>
    /// Mesure chaque proposition avec le protocole de la fiche et remplace son score par ce résultat : la note de la
    /// proposition est alors <b>la même</b> que celle de sa fiche. Les propositions sont reclassées par cette mesure.
    /// (Le score du Cuisinier sert à choisir les candidats ; il est un peu optimiste, car le meilleur de plusieurs candidats
    /// est gardé d'après ces mêmes duels.)
    /// </summary>
    public Task<IReadOnlyList<GeneratedDeck>> MeasureNotesAsync(IReadOnlyList<GeneratedDeck> decks, ulong seed = 1,
        CancellationToken cancel = default, IProgress<WorkProgress>? progress = null) =>
        RunExclusive(() => MeasureNotes(decks, seed, cancel, progress, 0, 1), cancel);

    private IReadOnlyList<GeneratedDeck> MeasureNotes(IReadOnlyList<GeneratedDeck> decks, ulong seed, CancellationToken cancel,
        IProgress<WorkProgress>? progress, double from, double to)
    {
        var settings = NoteSettings(seed);
        var measured = new List<GeneratedDeck>();
        for (var i = 0; i < decks.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Report(new WorkProgress($"Mesure de la note : deck {i + 1} sur {decks.Count}…", from + (to - from) * i / decks.Count));
            var m = GrandAnalyseur.MeasureStrength(_kb, decks[i].Deck, settings, cancel);
            measured.Add(decks[i] with { Score = m.Value, Margin95 = m.Margin95, Games = m.Games, Measured = true });
        }
        progress?.Report(new WorkProgress("Terminé.", to));
        return measured.OrderByDescending(d => d.Score).ToList();   // tri stable : à note égale, l'ordre du Cuisinier
    }

    /// <summary>Complète une sélection de 1 à 4 cartes (talent null = au choix du Cuisinier).</summary>
    public Task<IReadOnlyList<GeneratedDeck>> GenerateAsync(IReadOnlyList<FixedCard> selection, Effort effort, ulong seed = 1,
        CancellationToken cancel = default, IProgress<WorkProgress>? progress = null) =>
        Precomputed(selection, effort, seed) is { } ready ? Task.FromResult(ready) : RunExclusive(() =>
        {
            // 90 % du temps pour la recherche, 10 % pour la mesure de la note de chaque proposition (D-074).
            var p = progress is null ? null : new Relay<CookProgress>(c => progress.Report(new WorkProgress(c.Stage switch
            {
                CookStage.Screening => "Tri de nombreux decks (joueur rapide)…",
                CookStage.Confirming => "Confirmation des meilleurs (joueur heuristique)…",
                _ => "Mesure de la note…",
            }, 0.9 * c.Fraction)));
            var meta = ActiveMeta?.Select(m => m.Deck).ToList();
            var res = GrandCuisinier.Cook(_kb, selection, (CookOverride ?? CookSettingsFor(effort)) with { Seed = seed, Meta = meta }, cancel, p);
            var proposals = res.Proposals.Select(x => new GeneratedDeck(x.Deck, x.Confirmation.Score,
                x.Confirmation.Margin95, x.Confirmation.Counted, res.RandomOpponents, res.UsesMeta)).ToList();
            return MeasureNotes(proposals, seed, cancel, progress, 0.9, 1.0);
        }, cancel);

    /// <summary>
    /// Prépare en arrière-plan le classement de référence des analyses (à l'ouverture de l'application) :
    /// la première fiche est alors aussi rapide que les suivantes. Ne bloque pas les autres calculs.
    /// </summary>
    public Task WarmUpAsync(CancellationToken cancel = default) =>
        Task.Run(() => GrandAnalyseur.WarmUp(_kb, WithOpponents(AnalysisOverride ?? AnalysisSettings.Sheet), cancel), cancel);

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
            var settings = WithOpponents(AnalysisOverride ?? (complete ? AnalysisSettingsFor(effort) : AnalysisSettings.Sheet)) with { Seed = seed };
            var report = GrandAnalyseur.Analyse(_kb, deck, settings, cancel, p);
            // Note sur 100 (D-074) : toujours la mesure de référence. La fiche l'a déjà (mêmes réglages) ; l'analyse complète
            // la mesure à part, pour que la note ne change pas d'un écran à l'autre.
            var reference = NoteSettings(seed);
            report = report with { NoteStrength = complete && settings != reference ? GrandAnalyseur.MeasureStrength(_kb, deck, reference, cancel) : report.Strength };
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
