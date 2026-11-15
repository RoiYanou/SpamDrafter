using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;
using SpamDrafter.Engines;
using SpamDrafter.Engines.Cooking;
using SpamDrafter.Simulator.Engine;

namespace SpamDrafter.App.ViewModels;

/// <summary>Une carte imposée, avec son talent (null = au choix du Cuisinier).</summary>
public sealed class FixedSlotViewModel : ViewModelBase
{
    private TalentOption _talent;

    public FixedSlotViewModel(CardItem card, KnowledgeBase kb)
    {
        Card = card;
        TalentOptions = new[] { new TalentOption(null, "Talent au choix du Cuisinier") }
            .Concat(card.Card.Talents.Select((t, i) => new TalentOption(t, $"{i + 1}. {kb.TalentName(t)}"))).ToList();
        _talent = TalentOptions[0];
    }

    public CardItem Card { get; }
    public IReadOnlyList<TalentOption> TalentOptions { get; }
    public TalentOption Talent { get => _talent; set => Set(ref _talent, value); }
}

public sealed record EffortOption(Effort Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Un deck proposé par le Cuisinier.</summary>
public sealed class ProposalViewModel : ViewModelBase
{
    private bool _saved;
    public ProposalViewModel(int rank, GeneratedDeck deck, IReadOnlyList<string> lines)
    {
        Deck = deck;
        Title = $"Proposition {rank}";
        Score = $"{100 * deck.Score:0} % (± {100 * deck.Margin95:0}) sur {deck.Games} duels de confirmation";
        Lines = lines;
    }
    public GeneratedDeck Deck { get; }
    public string Title { get; }
    public string Score { get; }
    public IReadOnlyList<string> Lines { get; }
    public bool Saved { get => _saved; set { if (Set(ref _saved, value)) Raise(nameof(SaveLabel)); } }
    public string SaveLabel => Saved ? "Sauvegardé ✓" : "Sauvegarder";
}

/// <summary>
/// Onglet Génération de deck (phase 8, lot D) : choisir 1 à 4 cartes, lancer le Grand Cuisinier, lire les propositions,
/// demander le rapport du Grand Analyseur, sauvegarder un deck (origine « généré », visible dans le Drafter, D-017).
/// Les calculs passent par le coordinateur, sur un fil de travail : l'interface reste utilisable et le calcul s'annule.
/// </summary>
public sealed partial class GeneratorViewModel : ViewModelBase
{
    private readonly KnowledgeBase _kb;
    private readonly Coordinator _coordinator;
    private readonly DeckLibrary _library;
    private readonly Dictionary<string, CardItem> _byId;
    private readonly Action<Action> _post;
    private CancellationTokenSource? _cts;
    private EffortOption _effort;
    private string _status = "Choisis 1 à 4 cartes, puis lance la génération.";
    private string _step = "";
    private double _progress;
    private bool _busy;
    private string _report = "";
    private string _reportTitle = "";
    private Deck? _savedToAnalyse;
    private string _sheet = "";
    private string _detail = "";
    private bool _showDetail;
    private (IReadOnlyList<DuelSlot> Deck, string Label)? _lastAnalysed;

    /// <param name="post">Exécute une action sur le fil de l'interface (par défaut, le répartiteur d'Avalonia).</param>
    public GeneratorViewModel(CardBrowser browser, IReadOnlyList<CardItem> items, KnowledgeBase kb, Coordinator coordinator,
        DeckLibrary library, Action<Action>? post = null)
    {
        Browser = browser; _kb = kb; _coordinator = coordinator; _library = library;
        _post = post ?? (a => Avalonia.Threading.Dispatcher.UIThread.Post(a));
        _byId = items.ToDictionary(i => i.Card.Id);
        EffortOptions =
        [
            new(Engines.Effort.Rapide, "Rapide (≈ 4 fois moins de duels)"),
            new(Engines.Effort.Normal, "Normal (≈ 1 minute)"),
            new(Engines.Effort.Precis, "Précis (≈ 2 à 3 fois plus de duels)"),
        ];
        _effort = EffortOptions[1];
        AddCommand = new RelayCommand(_ => AddSelected(), _ => !IsBusy);
        RemoveCommand = new RelayCommand(p => { if (p is FixedSlotViewModel s && !IsBusy) { Fixed.Remove(s); Refresh(); } });
        ClearCommand = new RelayCommand(_ => { if (!IsBusy) { Fixed.Clear(); Refresh(); } });
        GenerateCommand = new RelayCommand(_ => _ = GenerateAsync(), _ => !IsBusy && Fixed.Count is > 0 and <= Deck.MaxCards);
        CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsBusy);
        AnalyseCommand = new RelayCommand(p => { if (p is ProposalViewModel pr) _ = AnalyseAsync(pr.Deck.Deck, pr.Title); }, _ => !IsBusy);
        ToggleDetailCommand = new RelayCommand(_ => ShowDetail = !ShowDetail);
        CompleteAnalysisCommand = new RelayCommand(_ => { if (_lastAnalysed is { } a) _ = AnalyseAsync(a.Deck, a.Label, complete: true); },
            _ => !IsBusy && _lastAnalysed is not null);
        AnalyseSavedCommand = new RelayCommand(_ => AnalyseSaved(), _ => !IsBusy && _savedToAnalyse is not null);
        SaveCommand = new RelayCommand(p => { if (p is ProposalViewModel pr) Save(pr); });
        library.Changed += (_, _) => RefreshSaved();
        RefreshSaved();
    }

    public CardBrowser Browser { get; }
    public ObservableCollection<FixedSlotViewModel> Fixed { get; } = new();
    public ObservableCollection<ProposalViewModel> Proposals { get; } = new();
    public ObservableCollection<Deck> CompleteDecks { get; } = new();
    public IReadOnlyList<EffortOption> EffortOptions { get; }

    public RelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand GenerateCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand AnalyseCommand { get; }
    public RelayCommand AnalyseSavedCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ToggleDetailCommand { get; }
    public RelayCommand CompleteAnalysisCommand { get; }

    public string FixedText => $"{Fixed.Count} / {Deck.MaxCards} cartes imposées";
    public EffortOption SelectedEffort { get => _effort; set => Set(ref _effort, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Step { get => _step; private set => Set(ref _step, value); }
    /// <summary>Avancement de 0 à 100.</summary>
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (!Set(ref _busy, value)) return;
            foreach (var c in new[] { AddCommand, GenerateCommand, CancelCommand, AnalyseCommand, AnalyseSavedCommand, CompleteAnalysisCommand })
                c.RaiseCanExecuteChanged();
        }
    }
    /// <summary>Texte affiché : la fiche joueur, ou le détail si demandé.</summary>
    public string Report { get => _report; private set { if (Set(ref _report, value)) Raise(nameof(HasReport)); } }
    /// <summary>Fiche joueur (lot E, D-050) : l'essentiel pour jouer le deck.</summary>
    public string Sheet { get => _sheet; private set { if (Set(ref _sheet, value)) UpdateReport(); } }
    /// <summary>Rapport détaillé (toutes les mesures).</summary>
    public string Detail { get => _detail; private set { if (Set(ref _detail, value)) UpdateReport(); } }
    public bool ShowDetail
    {
        get => _showDetail;
        set { if (Set(ref _showDetail, value)) { Raise(nameof(DetailButtonLabel)); UpdateReport(); } }
    }
    public string DetailButtonLabel => ShowDetail ? "Revenir à la fiche" : "Voir le détail";
    private void UpdateReport() => Report = ShowDetail ? Detail : Sheet;
    public bool HasReport => Report.Length > 0;
    public string ReportTitle { get => _reportTitle; private set => Set(ref _reportTitle, value); }
    public Deck? SavedToAnalyse
    {
        get => _savedToAnalyse;
        set { if (Set(ref _savedToAnalyse, value)) AnalyseSavedCommand.RaiseCanExecuteChanged(); }
    }

    private void AddSelected()
    {
        var item = Browser.Selected;
        if (item is null) { Status = "Choisis d'abord une carte dans la liste."; return; }
        if (Fixed.Count >= Deck.MaxCards) { Status = $"La sélection contient déjà {Deck.MaxCards} cartes."; return; }
        if (Fixed.Any(f => f.Card.Card.Id == item.Card.Id)) { Status = "Cette carte est déjà choisie (pas de doublon, D-020)."; return; }
        Fixed.Add(new FixedSlotViewModel(item, _kb));
        Refresh();
    }

    private void Refresh()
    {
        Raise(nameof(FixedText));
        GenerateCommand.RaiseCanExecuteChanged();
        Status = Fixed.Count == 0 ? "Choisis 1 à 4 cartes, puis lance la génération."
            : Fixed.Count == Deck.MaxCards ? "4 cartes imposées : le Cuisinier choisira les talents et l'ordre."
            : Fixed.Count == 1 && _coordinator.Opponents == OpponentSource.Synthetic && _coordinator.Synthetic.OneCard is not null
                ? "Le Cuisinier complétera avec 3 cartes. Avec le talent au choix (réglage rapide ou normal), les propositions sont " +
                  "immédiates : elles ont été calculées à l'avance."
            : $"Le Cuisinier complétera avec {Deck.MaxCards - Fixed.Count} carte(s).";
    }

    private void RefreshSaved()
    {
        var keep = _savedToAnalyse?.Id;
        CompleteDecks.Clear();
        foreach (var d in _library.Decks.Where(d => d.Slots.Count == Deck.MaxCards && d.GameVersion == _kb.Manifest.GameVersion))
            CompleteDecks.Add(d);
        SavedToAnalyse = CompleteDecks.FirstOrDefault(d => d.Id == keep);
    }

    public async Task GenerateAsync()
    {
        if (IsBusy || Fixed.Count == 0) return;
        var selection = Fixed.Select(f => new FixedCard(f.Card.Card.Id, f.Talent.Id)).ToList();
        Proposals.Clear();
        Sheet = ""; Detail = ""; ReportTitle = "";
        await Run("Génération", async (cancel, progress) =>
        {
            var decks = await _coordinator.GenerateAsync(selection, SelectedEffort.Value, Seed(), cancel, progress);
            var rank = 0;
            foreach (var d in decks) Proposals.Add(new ProposalViewModel(++rank, d, d.Deck.Select(Describe).ToList()));
            Status = decks.Count == 0 ? "Aucune proposition."
                : $"{decks.Count} proposition(s){(decks[0].Precomputed ? ", calculées à l'avance par le Studio" : "")}. " +
                  $"Score = victoires + moitié des égalités contre {decks[0].RandomOpponents} {OpponentsText(_coordinator.Opponents)} et les autres candidats.";
        });
    }

    private void AnalyseSaved()
    {
        if (_savedToAnalyse is not { } d) return;
        _ = AnalyseAsync(d.Slots.Select(s => new DuelSlot(s.CardId, s.TalentId)).ToList(), $"« {d.Name} »");
    }

    /// <param name="complete">Faux : fiche joueur (rapide). Vrai : analyse complète (liens entre cartes, plus longue).</param>
    public async Task AnalyseAsync(IReadOnlyList<DuelSlot> deck, string label, bool complete = false)
    {
        if (IsBusy) return;
        Sheet = ""; Detail = ""; ReportTitle = "";
        _lastAnalysed = (deck, label);
        await Run(complete ? "Analyse complète" : "Analyse", async (cancel, progress) =>
        {
            var res = await _coordinator.AnalyseAsync(deck, complete, SelectedEffort.Value, Seed(), cancel, progress);
            ReportTitle = (complete ? "Analyse complète — " : "Fiche du deck — ") + label;
            Sheet = res.Sheet;
            Detail = ToPlainText(res.Markdown);
            ShowDetail = complete;
            Status = $"Analyse terminée : {res.Report.Duels} duels simulés en {res.Report.Elapsed.TotalSeconds:0} s.";
        });
        CompleteAnalysisCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Lance un calcul : avancement, annulation, erreurs. Le corps reprend sur le fil de l'interface.</summary>
    private async Task Run(string what, Func<CancellationToken, IProgress<WorkProgress>, Task> body)
    {
        _cts = new CancellationTokenSource();
        IsBusy = true;
        Progress = 0; Step = "Préparation…";
        Status = $"{what} en cours… (tu peux annuler)";
        try { await body(_cts.Token, new Throttled(this)); }
        catch (OperationCanceledException) { Status = $"{what} annulée."; }
        catch (Exception e) { Status = $"{what} impossible : {e.Message}"; }
        finally
        {
            IsBusy = false;
            Step = ""; Progress = 0;
            _cts.Dispose(); _cts = null;
        }
    }

    private void Save(ProposalViewModel p)
    {
        if (p.Saved) return;
        var names = p.Deck.Deck.Select(s => _byId.TryGetValue(s.CardId, out var c) ? c.Name : s.CardId);
        var deck = new Deck
        {
            Name = "Généré — " + string.Join(", ", names),
            Origin = DeckOrigin.Generated,
            GameVersion = _kb.Manifest.GameVersion,
            Slots = p.Deck.Deck.Select(s => new DeckSlot(s.CardId, s.TalentId)).ToList(),
        };
        _library.Save(deck);
        p.Saved = true;
        Status = $"« {deck.Name} » sauvegardé : il apparaît dans le Deck builder et le Drafter.";
    }

    private string Describe(DuelSlot s)
    {
        var name = _byId.TryGetValue(s.CardId, out var c) ? c.Name : s.CardId;
        var imposed = Fixed.Any(f => f.Card.Card.Id == s.CardId) ? " (imposée)" : "";
        return s.TalentId is null ? $"{name}{imposed} — aucun talent" : $"{name}{imposed} — {_kb.TalentName(s.TalentId)}";
    }

    /// <summary>
    /// Decks de référence (D-050) : les decks sauvegardés cochés « référence », complets et de la même version de SPAM,
    /// deviennent les adversaires des moteurs ; la liste suit chaque changement de la bibliothèque.
    /// </summary>
    public static void FollowReferenceDecks(Coordinator coordinator, DeckLibrary library, string gameVersion)
    {
        void Update() => coordinator.Meta = library.Decks
            .Where(d => d.IsReference && d.Slots.Count == Deck.MaxCards && d.GameVersion == gameVersion)
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(d => new SpamDrafter.Engines.Analysis.NamedDeck(d.Name, d.Slots.Select(x => new DuelSlot(x.CardId, x.TalentId)).ToList()))
            .ToList();
        Update();
        library.Changed += (_, _) => Update();
    }

    /// <summary>Nom des adversaires dans les messages.</summary>
    public static string OpponentsText(OpponentSource s) => s switch
    {
        OpponentSource.Reference => "decks de référence",
        OpponentSource.Synthetic => "decks de la méta synthétique (aucun deck de référence)",
        _ => "decks au hasard (ni deck de référence, ni méta synthétique)",
    };

    /// <summary>Même graine à chaque fois : même sélection = mêmes propositions (reproductible).</summary>
    private static ulong Seed() => Coordinator.AppSeed;

    /// <summary>Le rapport Markdown en texte lisible (sans les marques ** et # ; tableaux gardés).</summary>
    public static string ToPlainText(string markdown)
    {
        var text = markdown.Replace("**", "");
        text = Heading().Replace(text, m => m.Groups[1].Value.ToUpperInvariant());
        return Separator().Replace(text, "");
    }

    [GeneratedRegex(@"^#{1,3} (.*)$", RegexOptions.Multiline)]
    private static partial Regex Heading();
    [GeneratedRegex(@"^\|(---\|)+\r?\n", RegexOptions.Multiline)]
    private static partial Regex Separator();

    /// <summary>Avancement reçu des fils de calcul (très souvent) : transmis à l'interface au plus tous les 0,5 %.</summary>
    private sealed class Throttled(GeneratorViewModel vm) : IProgress<WorkProgress>
    {
        private readonly object _lock = new();
        private double _last = -1;
        private string _step = "";
        public void Report(WorkProgress value)
        {
            lock (_lock)
            {
                if (value.Step == _step && value.Fraction - _last < 0.005) return;
                _last = value.Fraction; _step = value.Step;
            }
            vm._post(() => { if (vm.IsBusy) { vm.Progress = 100 * value.Fraction; vm.Step = value.Step; } });
        }
    }
}
