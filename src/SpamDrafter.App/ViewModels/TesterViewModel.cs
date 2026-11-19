using System.Collections.ObjectModel;
using SpamDrafter.Core.Decks;
using SpamDrafter.Engines;
using SpamDrafter.Engines.Analysis;
using SpamDrafter.Engines.History;
using SpamDrafter.Simulator.Engine;

namespace SpamDrafter.App.ViewModels;

/// <summary>Un deck proposé dans l'onglet Tester.</summary>
public sealed record TesterDeck(Deck Deck, string Name);

/// <summary>Une ligne du tableau des cartes d'une confrontation (moyennes par duel).</summary>
public sealed record CompareCardRow(DeckChip Chip, string Damage, string Healing, string Taken, string Kills, string Fall);

/// <summary>
/// Onglet Tester (phase 14, D-085). Lot A : choisir un deck complet dans « Mes decks » (mêmes recherche, tri et filtres que le
/// Deck builder et le Drafter). Lot B : mode « Un deck » : la note sur 100 et la Fiche rapport (Grand Analyseur), puis
/// l'analyse complète. Même note que partout (D-074) ; un rapport déjà calculé est repris de la mémoire sans recalcul.
/// Lot D : mode « Comparer 2 decks » : confrontation des deux decks (joueur fin) et verdict.
/// </summary>
public sealed class TesterViewModel : ViewModelBase
{
    private readonly DeckLibrary _library;
    private readonly DeckBuilderViewModel? _builder;
    private readonly Coordinator? _coordinator;
    private readonly GenerationHistory? _history;
    private readonly Action<Action> _post;
    private bool _refreshPending;
    private TesterDeck? _selected;
    private Guid? _selectedId;
    private string _message = "";
    private CancellationTokenSource? _cts;
    private bool _busy;
    private string _status = "";
    private string _step = "";
    private double _progress;
    private string? _reportKey;          // le deck (cartes et talents) auquel appartient le rapport affiché
    private bool _reportComplete;
    private string _sheet = "", _detail = "", _reportTitle = "";
    private bool _showDetail;
    private string _noteText = "", _noteLabel = "", _noteHint = "", _noteDetail = "";
    private double _gauge;
    // Mode « Comparer 2 decks » (lot D)
    private bool _isCompare;
    private TesterDeck? _deckA, _deckB;
    private int _activeSlot;                 // 0 = le prochain deck choisi va en A, 1 = en B
    private ComparisonResult? _cmp;
    private string? _cmpKey;                 // la paire (A, B) à laquelle appartient le résultat affiché
    private string _cmpText = "", _cmpName = "", _cmpRealA = "", _cmpRealB = "";
    private const string LabelA = "Deck A", LabelB = "Deck B";   // dans le texte du résultat ; le vrai nom est dans l'info-bulle

    /// <summary>Sans <paramref name="builder"/> : tous les decks de la bibliothèque, par nom. Sans <paramref name="coordinator"/> : aucun calcul.</summary>
    /// <param name="post">Exécute une action sur le fil de l'interface (par défaut, le répartiteur d'Avalonia).</param>
    public TesterViewModel(DeckLibrary library, DeckBuilderViewModel? builder = null, Coordinator? coordinator = null,
        GenerationHistory? history = null, Action<Action>? post = null)
    {
        _library = library; _builder = builder; _coordinator = coordinator; _history = history;
        _post = post ?? (a => Avalonia.Threading.Dispatcher.UIThread.Post(a));
        OpenInBuilderCommand = new RelayCommand(_ => { if (Selected is { } s) OpenRequested?.Invoke(s.Deck); }, _ => Selected is not null);
        ClearSelectionCommand = new RelayCommand(_ => Selected = null, _ => Selected is not null && !IsBusy);
        TestCommand = new RelayCommand(_ => _ = TestAsync(complete: false), _ => CanRun);
        CompleteCommand = new RelayCommand(_ => _ = TestAsync(complete: true), _ => CanRun && !(HasReport && _reportComplete));
        CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsBusy);
        ToggleDetailCommand = new RelayCommand(_ => ShowDetail = !ShowDetail, _ => HasReport && _reportComplete);
        ModeOneCommand = new RelayCommand(_ => IsCompare = false, _ => !IsBusy);
        ModeCompareCommand = new RelayCommand(_ => IsCompare = true, _ => !IsBusy);
        CompareCommand = new RelayCommand(_ => _ = CompareAsync(), _ => CanCompare);
        SwapCommand = new RelayCommand(_ => Swap(), _ => !IsBusy && (_deckA is not null || _deckB is not null));
        ClearACommand = new RelayCommand(_ => { if (!IsBusy) { DeckA = null; ActiveSlot = 0; } }, _ => _deckA is not null);
        ClearBCommand = new RelayCommand(_ => { if (!IsBusy) { DeckB = null; ActiveSlot = 1; } }, _ => _deckB is not null);
        ActivateACommand = new RelayCommand(_ => ActiveSlot = 0, _ => !IsBusy);
        ActivateBCommand = new RelayCommand(_ => ActiveSlot = 1, _ => !IsBusy);
        if (builder is not null)
            builder.SavedDecks.CollectionChanged += (_, _) =>
            {
                if (_refreshPending) return;
                _refreshPending = true;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { _refreshPending = false; Refresh(); });
            };
        library.Changed += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>Recherche, tri et filtres des decks (ceux du Builder) ; null hors de l'application complète.</summary>
    public DeckBuilderViewModel? Decks => _builder;

    /// <summary>Les decks complets (4 cartes), dans l'ordre de « Mes decks ».</summary>
    public ObservableCollection<TesterDeck> Results { get; } = new();

    public RelayCommand OpenInBuilderCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand TestCommand { get; }
    public RelayCommand CompleteCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ToggleDetailCommand { get; }
    public RelayCommand ModeOneCommand { get; }
    public RelayCommand ModeCompareCommand { get; }
    public RelayCommand CompareCommand { get; }
    public RelayCommand SwapCommand { get; }
    public RelayCommand ClearACommand { get; }
    public RelayCommand ClearBCommand { get; }
    public RelayCommand ActivateACommand { get; }
    public RelayCommand ActivateBCommand { get; }

    /// <summary>Demande d'ouverture du deck dans le Deck builder.</summary>
    public event Action<Deck>? OpenRequested;

    public string Message { get => _message; private set => Set(ref _message, value); }

    /// <summary>Le deck à tester ; null si aucun n'est choisi.</summary>
    public TesterDeck? Selected
    {
        get => _isCompare ? null : _selected;   // en mode « Comparer », la liste ne garde aucune sélection : chaque clic remplit A ou B
        set
        {
            if (_isCompare)
            {
                if (value is not null && !IsBusy) AssignToSlot(value);
                Raise(nameof(Selected));
                return;
            }
            SetSingle(value);
        }
    }

    /// <summary>Mode « Un deck » : choisit le deck à tester (aussi utilisé par la mise à jour de la liste, quel que soit le mode).</summary>
    private void SetSingle(TesterDeck? value)
    {
        {
            if (IsBusy && value?.Deck.Id != _selected?.Deck.Id) return;   // pas de changement de deck pendant un calcul
            if (!Set(ref _selected, value)) return;
            _selectedId = value?.Deck.Id;
            Raise(nameof(HasSelection)); Raise(nameof(ShowNoSelection)); Raise(nameof(SelectedChips)); Raise(nameof(SelectedTitle));
            OpenInBuilderCommand.RaiseCanExecuteChanged(); ClearSelectionCommand.RaiseCanExecuteChanged();
            // Le rapport affiché ne vaut que pour les mêmes cartes et talents : sinon on l'efface, puis on cherche en mémoire.
            if (value is null || SlotsKey(value.Deck) != _reportKey)
            {
                ClearReport();
                if (value is not null) LoadFromMemory(value.Deck);
            }
            RaiseRunStates();
        }
    }

    public bool HasSelection => !_isCompare && _selected is not null;
    /// <summary>Mode « Un deck » sans deck choisi : le message d'invitation.</summary>
    public bool ShowNoSelection => !_isCompare && _selected is null;
    public string SelectedTitle => _selected?.Name ?? "";
    public IReadOnlyList<DeckChip> SelectedChips => _selected is null ? Array.Empty<DeckChip>() : DeckChipsConverter.Build(_selected.Deck.Slots);

    // ----- Calcul -----
    /// <summary>Faux sans moteur (tests du cadre) : l'onglet ne calcule rien.</summary>
    public bool CanCompute => _coordinator is not null;
    private bool CanRun => _coordinator is not null && _selected is not null && !IsBusy;
    public bool IsBusy
    {
        get => _busy;
        private set { if (Set(ref _busy, value)) { Raise(nameof(NotBusy)); RaiseRunStates(); } }
    }
    public bool NotBusy => !_busy;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Step { get => _step; private set => Set(ref _step, value); }
    /// <summary>Avancement de 0 à 100.</summary>
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    /// <summary>Astuce affichée pendant l'attente.</summary>
    public string Tip { get; private set; } = Tips.Any();

    private void RaiseRunStates()
    {
        TestCommand.RaiseCanExecuteChanged(); CompleteCommand.RaiseCanExecuteChanged(); CancelCommand.RaiseCanExecuteChanged();
        ToggleDetailCommand.RaiseCanExecuteChanged(); ClearSelectionCommand.RaiseCanExecuteChanged();
        foreach (var c in new[] { ModeOneCommand, ModeCompareCommand, CompareCommand, SwapCommand, ClearACommand, ClearBCommand, ActivateACommand, ActivateBCommand })
            c.RaiseCanExecuteChanged();
    }

    // ----- Rapport affiché -----
    public bool HasReport => _reportKey is not null && _sheet.Length > 0;
    public string ReportTitle { get => _reportTitle; private set => Set(ref _reportTitle, value); }
    /// <summary>Note sur 100 (D-072, D-074), par exemple « 54 / 100 ».</summary>
    public string NoteText { get => _noteText; private set => Set(ref _noteText, value); }
    /// <summary>Intankable, Faible, Passable, Bon, Excellent ou Parfait.</summary>
    public string NoteLabel { get => _noteLabel; private set => Set(ref _noteLabel, value); }
    /// <summary>Couleur du niveau de la note.</summary>
    public Avalonia.Media.IBrush NoteBrush { get => _noteBrush; private set => Set(ref _noteBrush, value); }
    private Avalonia.Media.IBrush _noteBrush = Avalonia.Media.Brushes.White;
    public string NoteHint { get => _noteHint; private set { if (Set(ref _noteHint, value)) Raise(nameof(HasNoteHint)); } }
    public bool HasNoteHint => _noteHint.Length > 0;
    /// <summary>Détail de la mesure (info-bulle).</summary>
    public string NoteDetail { get => _noteDetail; private set => Set(ref _noteDetail, value); }
    /// <summary>Ce que mesure la note, en une phrase (contre la méta, ou des decks au hasard sans méta).</summary>
    public string NoteExplanation => _coordinator is null ? "" :
        DeckNote.Explanation(_coordinator.Synthetic.Meta.Count, _coordinator.Opponents == OpponentSource.Synthetic);
    /// <summary>Jauge de 0 à 100 : part de 0 puis monte jusqu'à la note (animée par le style « gauge »).</summary>
    public double Gauge { get => _gauge; private set => Set(ref _gauge, value); }

    /// <summary>Texte affiché : la fiche, ou le rapport détaillé si demandé (après l'analyse complète).</summary>
    public string Report => ShowDetail && _detail.Length > 0 ? _detail : _sheet;
    public bool ShowDetail
    {
        get => _showDetail;
        set { if (Set(ref _showDetail, value)) { Raise(nameof(DetailButtonLabel)); Raise(nameof(Report)); } }
    }
    public string DetailButtonLabel => ShowDetail ? "Revenir à la fiche" : "Voir le détail";
    public bool CanShowDetail => HasReport && _reportComplete;
    public bool CanRunComplete => CanCompute && !(HasReport && _reportComplete);

    private static string SlotsKey(Deck d) => string.Join(",", d.Slots.Select(s => $"{s.CardId}:{s.TalentId}"));
    private static IReadOnlyList<DuelSlot> AsDuel(Deck d) => d.Slots.Select(s => new DuelSlot(s.CardId, s.TalentId)).ToList();

    private void ClearReport()
    {
        _reportKey = null; _reportComplete = false;
        _sheet = ""; _detail = ""; ShowDetail = false; ReportTitle = ""; Status = "";
        NoteText = ""; NoteLabel = ""; NoteHint = ""; NoteDetail = ""; Gauge = 0;
        NotifyReport();
    }

    private void NotifyReport()
    {
        Raise(nameof(HasReport)); Raise(nameof(Report)); Raise(nameof(CanShowDetail)); Raise(nameof(CanRunComplete));
        ToggleDetailCommand.RaiseCanExecuteChanged(); CompleteCommand.RaiseCanExecuteChanged();
    }

    private void ShowNote(double score, double margin95, int games)
    {
        var n = DeckNote.From(score, margin95);
        NoteText = n.Text; NoteLabel = n.Label; NoteBrush = SpamDrafter.App.Views.NoteColors.For(n.Label);
        NoteHint = n.NeedsConfirmation ? "à confirmer : peu de duels" : "";
        NoteDetail = $"Mesuré sur {games} duels simulés : {100 * score:0} % de victoires (marge d'erreur ± {100 * margin95:0} points). " +
                     "La note va de 0 à 100 ; 100 = niveau d'un deck de la méta.";
        Gauge = 0;
        var points = n.Points;
        Avalonia.Threading.DispatcherTimer.RunOnce(() => Gauge = points, TimeSpan.FromMilliseconds(120));
    }

    private void ShowReport(Deck deck, string title, string sheet, string detail, bool complete)
    {
        _reportKey = SlotsKey(deck); _reportComplete = complete;
        ReportTitle = title; _sheet = sheet; _detail = detail;
        ShowDetail = false;
        NotifyReport();
    }

    /// <summary>Rapport déjà calculé pour ce deck (même version de SPAM, méta et moteurs) : affiché tout de suite, sans calcul.</summary>
    private void LoadFromMemory(Deck deck)
    {
        if (_history is null) return;
        var slots = AsDuel(deck);
        var hit = _history.FindReport(slots, complete: true, Effort.Normal) is { NoteScore: not null, Sheet: not null } full ? full
            : _history.FindReport(slots, complete: false, Effort.Normal) is { NoteScore: not null, Sheet: not null } sheet ? sheet : null;
        if (hit is null) return;
        ShowReport(deck, hit.Title ?? deck.Name, hit.Sheet!, hit.Detail ?? "", hit.Complete);
        ShowNote(hit.NoteScore!.Value, hit.NoteMargin95, hit.NoteGames);
        Status = $"Rapport retrouvé en mémoire (calculé {HistoryText.When(hit.CreatedUtc)}) : aucun nouveau calcul.";
    }

    /// <summary>Calcule la note et la fiche du deck choisi (<paramref name="complete"/> : analyse complète, plus longue).</summary>
    public async Task TestAsync(bool complete)
    {
        if (_coordinator is not { } coordinator || _selected is not { } sel || IsBusy) return;
        var deck = sel.Deck;
        var slots = AsDuel(deck);
        var key = SlotsKey(deck);
        // Même rapport déjà calculé : repris de la mémoire.
        if (_history?.FindReport(slots, complete, Effort.Normal) is { NoteScore: not null, Sheet: not null } hit)
        {
            ClearReport();
            ShowReport(deck, hit.Title ?? deck.Name, hit.Sheet!, hit.Detail ?? "", hit.Complete);
            ShowNote(hit.NoteScore!.Value, hit.NoteMargin95, hit.NoteGames);
            ShowDetail = complete;
            Status = $"Rapport retrouvé en mémoire (calculé {HistoryText.When(hit.CreatedUtc)}) : aucun nouveau calcul.";
            return;
        }
        _cts = new CancellationTokenSource();
        IsBusy = true;
        Tip = Tips.Any(); Raise(nameof(Tip));
        Progress = 0; Step = "Préparation…";
        Status = (complete ? "Analyse complète" : "Test") + " en cours… (tu peux annuler)";
        try
        {
            var res = await coordinator.AnalyseAsync(slots, complete, Effort.Normal, Coordinator.AppSeed, _cts.Token, new Throttled(this));
            if (_selected is null || SlotsKey(_selected.Deck) != key) { Status = "Le deck a changé pendant le calcul : résultat non affiché."; return; }
            var title = (complete ? "Analyse complète — " : "Fiche du deck — ") + deck.Name;
            var detail = HistoryText.ToPlain(res.Markdown);
            ShowReport(deck, title, res.Sheet, detail, complete);
            ShowDetail = complete;
            var m = res.Report.ForNote;
            ShowNote(m.Value, m.Margin95, m.Games);
            Status = $"Test terminé : {res.Report.Duels} duels simulés en {res.Report.Elapsed.TotalSeconds:0} s.";
            _history?.AddReport(slots, complete, Effort.Normal, deck.Name, title, res.Sheet, detail, res.Report.Duels, res.Report.Elapsed.TotalSeconds, m);
        }
        catch (OperationCanceledException) { Status = "Test annulé."; }
        catch (InvalidOperationException e) when (coordinator.IsBusy) { Status = "Un autre calcul est en cours (Cuisine ?) : réessaie dans un instant. " + e.Message; }
        catch (Exception e) { Status = "Test impossible : " + e.Message; }
        finally
        {
            IsBusy = false; Step = ""; Progress = 0;
            _cts?.Dispose(); _cts = null;
        }
    }

    // ===== Mode « Comparer 2 decks » (lot D) =====
    public bool IsCompare
    {
        get => _isCompare;
        set
        {
            if (IsBusy || !Set(ref _isCompare, value)) return;
            Raise(nameof(IsSingle)); Raise(nameof(ModeIndex)); Raise(nameof(Selected)); Raise(nameof(HasSelection)); Raise(nameof(ShowNoSelection));
            RaiseRunStates();
        }
    }
    /// <summary>Sous-onglet affiché : 0 = « Un deck », 1 = « Comparer 2 decks » (même rendu que les sous-onglets de la Bibliothèque).</summary>
    public int ModeIndex
    {
        get => _isCompare ? 1 : 0;
        set { IsCompare = value == 1; Raise(nameof(ModeIndex)); }   // pendant un calcul, le sous-onglet reste où il est
    }
    public bool IsSingle => !_isCompare;

    public TesterDeck? DeckA { get => _deckA; private set { if (Set(ref _deckA, value)) { RaiseSlots(); SyncCompare(); } } }
    public TesterDeck? DeckB { get => _deckB; private set { if (Set(ref _deckB, value)) { RaiseSlots(); SyncCompare(); } } }
    /// <summary>0 : le prochain deck cliqué va en A ; 1 : en B.</summary>
    public int ActiveSlot { get => _activeSlot; private set { if (Set(ref _activeSlot, value)) { Raise(nameof(ActiveA)); Raise(nameof(ActiveB)); } } }
    public bool ActiveA => _activeSlot == 0;
    public bool ActiveB => _activeSlot == 1;
    public string TitleA => _deckA?.Name ?? "Choisis le deck A dans la liste";
    public string TitleB => _deckB?.Name ?? "Choisis le deck B dans la liste";
    public IReadOnlyList<DeckChip> ChipsA => _deckA is null ? Array.Empty<DeckChip>() : DeckChipsConverter.Build(_deckA.Deck.Slots);
    public IReadOnlyList<DeckChip> ChipsB => _deckB is null ? Array.Empty<DeckChip>() : DeckChipsConverter.Build(_deckB.Deck.Slots);
    public bool HasA => _deckA is not null;
    public bool HasB => _deckB is not null;

    private bool CanCompare => _coordinator is not null && _deckA is not null && _deckB is not null && !IsBusy && SlotsKey(_deckA.Deck) != SlotsKey(_deckB.Deck);
    /// <summary>Message sous les deux decks : ce qui manque pour lancer la confrontation.</summary>
    public string CompareHint =>
        _deckA is null || _deckB is null ? "Choisis deux decks dans la liste : le premier clic remplit A, le suivant B."
        : SlotsKey(_deckA.Deck) == SlotsKey(_deckB.Deck) ? "Ces deux decks ont exactement les mêmes cartes et talents : choisis-en deux différents."
        : "";
    public bool HasCompareHint => CompareHint.Length > 0;
    public bool CanCompute2 => CanCompute && _deckA is not null && _deckB is not null;

    private void RaiseSlots()
    {
        foreach (var n in new[] { nameof(TitleA), nameof(TitleB), nameof(ChipsA), nameof(ChipsB), nameof(HasA), nameof(HasB), nameof(CompareHint), nameof(HasCompareHint), nameof(CanCompute2) }) Raise(n);
        RaiseRunStates();
    }

    /// <summary>Un deck cliqué dans la liste : il va dans le camp actif, puis le camp actif passe à l'autre si celui-ci est vide.</summary>
    private void AssignToSlot(TesterDeck d)
    {
        if (_activeSlot == 0) { DeckA = d; if (_deckB is null) ActiveSlot = 1; }
        else { DeckB = d; if (_deckA is null) ActiveSlot = 0; }
    }

    private void Swap()
    {
        (_deckA, _deckB) = (_deckB, _deckA);
        Raise(nameof(DeckA)); Raise(nameof(DeckB));
        RaiseSlots();
        SyncCompare();
    }

    // Résultat affiché
    public bool HasCompareResult => _cmp is not null && _cmpKey is not null;
    public string CompareTitle => _cmpName;
    /// <summary>Noms des deux decks du résultat affiché : mis en couleur (A, B) dans le texte.</summary>
    public string CompareNameA => _cmpRealA.Length == 0 ? "" : LabelA;
    public string CompareNameB => _cmpRealB.Length == 0 ? "" : LabelB;
    /// <summary>Les vrais noms, montrés au survol de « Deck A » et « Deck B ».</summary>
    public string CompareTipA => _cmpRealA;
    public string CompareTipB => _cmpRealB;
    public string CompareReport => _cmpText;
    /// <summary>Part des duels gagnés par A, de 0 à 100 (barre).</summary>
    public double CompareBar => _cmp is null ? 50 : 100 * _cmp.Confrontation.ScoreA.Value;
    public string CompareScoreText => _cmp is null ? "" :
        $"{LabelA}  {100 * _cmp.Confrontation.ScoreA.Value:0} %   ·   {100 * (1 - _cmp.Confrontation.ScoreA.Value):0} %  {LabelB}";
    public string CompareVerdict => _cmp is null ? "" : ConfrontationWriter.VerdictLine(LabelA, LabelB, _cmp.Confrontation);

    private string? PairKey() => _deckA is null || _deckB is null ? null : GenerationHistory.ComparisonKey(AsDuel(_deckA.Deck), AsDuel(_deckB.Deck));

    /// <summary>Le résultat affiché ne vaut que pour la paire (A, B) actuelle : sinon on l'efface, puis on cherche en mémoire.</summary>
    private void SyncCompare()
    {
        if (_cmpKey is not null && _cmpKey == PairKey()) return;
        _cmp = null; _cmpKey = null; _cmpText = ""; _cmpName = ""; _cmpRealA = ""; _cmpRealB = "";
        CardsA = Array.Empty<CompareCardRow>(); CardsB = Array.Empty<CompareCardRow>();
        if (_isCompare) Status = "";
        if (_deckA is not null && _deckB is not null && _history is not null && _coordinator is not null
            && _history.FindComparison(AsDuel(_deckA.Deck), AsDuel(_deckB.Deck)) is { Comparison: not null } hit)
        {
            ShowComparison(hit.Comparison!);
            Status = $"Confrontation retrouvée en mémoire (calculée {HistoryText.When(hit.CreatedUtc)}) : aucun nouveau calcul.";
        }
        RaiseCompareResult();
    }

    private void ShowComparison(ComparisonResult c)
    {
        _cmp = c; _cmpKey = PairKey();
        _cmpRealA = _deckA!.Name; _cmpRealB = _deckB!.Name;
        _cmpName = $"{_cmpRealA} contre {_cmpRealB}";
        _cmpText = _coordinator!.Describe(LabelA, LabelB, c);
        CardsA = CardRows(_deckA, c.Confrontation.CardsA);
        CardsB = CardRows(_deckB, c.Confrontation.CardsB);
        RaiseCompareResult();
    }

    /// <summary>Tableau des cartes (moyennes par duel) : l'illustration de la carte avec son talent, puis les chiffres.</summary>
    public IReadOnlyList<CompareCardRow> CardsA { get; private set; } = Array.Empty<CompareCardRow>();
    public IReadOnlyList<CompareCardRow> CardsB { get; private set; } = Array.Empty<CompareCardRow>();

    private static IReadOnlyList<CompareCardRow> CardRows(TesterDeck deck, IReadOnlyList<SideCard> cards)
    {
        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        var chips = DeckChipsConverter.Build(deck.Deck.Slots);
        return chips.Zip(cards, (chip, c) => new CompareCardRow(chip, c.DamageDone.ToString("0", fr), c.HealingDone.ToString("0", fr),
            c.DamageTaken.ToString("0", fr), c.Kills.ToString("0.0", fr), (100 * c.DeathRate).ToString("0", fr) + " %")).ToList();
    }

    private void RaiseCompareResult()
    {
        foreach (var n in new[] { nameof(HasCompareResult), nameof(CompareTitle), nameof(CompareNameA), nameof(CompareNameB), nameof(CompareTipA), nameof(CompareTipB), nameof(CardsA), nameof(CardsB), nameof(CompareReport), nameof(CompareBar), nameof(CompareScoreText), nameof(CompareVerdict) }) Raise(n);
    }

    /// <summary>La note déjà mesurée d'un deck (rapport en mémoire), sinon null : elle n'est alors pas recalculée deux fois.</summary>
    private Measured? KnownNote(Deck d)
    {
        if (_history is null) return null;
        var slots = AsDuel(d);
        var e = _history.FindReport(slots, true, Effort.Normal);
        if (e?.NoteScore is null) e = _history.FindReport(slots, false, Effort.Normal);
        return e?.NoteScore is { } v ? new Measured(v, e.NoteMargin95, e.NoteGames) : null;
    }

    /// <summary>Confronte A et B (puis mesure leurs notes si elles ne sont pas déjà en mémoire).</summary>
    public async Task CompareAsync()
    {
        if (_coordinator is not { } coordinator || _deckA is not { } a || _deckB is not { } b || IsBusy || !CanCompare) return;
        var slotsA = AsDuel(a.Deck); var slotsB = AsDuel(b.Deck);
        var key = PairKey();
        if (_cmpKey == key && _cmp is not null) { Status = "Cette confrontation est déjà affichée."; return; }
        _cts = new CancellationTokenSource();
        IsBusy = true;
        Tip = Tips.Any(); Raise(nameof(Tip));
        Progress = 0; Step = "Préparation…";
        Status = "Confrontation en cours… (tu peux annuler)";
        try
        {
            var res = await coordinator.CompareAsync(slotsA, slotsB, KnownNote(a.Deck), KnownNote(b.Deck), Coordinator.AppSeed, _cts.Token, new Throttled(this));
            if (PairKey() != key) { Status = "Les decks ont changé pendant le calcul : résultat non affiché."; return; }
            ShowComparison(res);
            var c = res.Confrontation;
            Status = $"Confrontation terminée en {c.ElapsedSeconds:0} s. Celui qui commence change une fois sur deux.";
            _history?.AddComparison(slotsA, slotsB, a.Name, b.Name, res);
        }
        catch (OperationCanceledException) { Status = "Confrontation annulée."; }
        catch (InvalidOperationException e) when (coordinator.IsBusy) { Status = "Un autre calcul est en cours (Cuisine ?) : réessaie dans un instant. " + e.Message; }
        catch (Exception e) { Status = "Confrontation impossible : " + e.Message; }
        finally
        {
            IsBusy = false; Step = ""; Progress = 0;
            _cts?.Dispose(); _cts = null;
        }
    }

    /// <summary>Choisit un deck depuis un autre onglet (Générateur, Deck builder). Faux si ce deck n'est pas testable.</summary>
    public bool Select(Deck deck)
    {
        if (!IsTestable(deck) || IsBusy) return false;
        // Le filtre du Builder peut cacher ce deck : on l'efface pour qu'il soit dans la liste.
        if (Results.All(r => r.Deck.Id != deck.Id) && _builder is not null && _builder.DeckFiltersActive) _builder.ClearDeckFiltersCommand.Execute(null);
        Refresh();
        IsCompare = false;
        Selected = Results.FirstOrDefault(r => r.Deck.Id == deck.Id);
        return Selected is not null;
    }

    /// <summary>Seuls les decks de 4 cartes ont une note : un brouillon n'est pas testable.</summary>
    public static bool IsTestable(Deck deck) => deck.Slots.Count == Deck.MaxCards;

    private IReadOnlyList<Deck> Source => _builder is null ? _library.Decks.ToList() : _builder.SavedDecks.ToList();

    private void Refresh()
    {
        var source = Source;
        var testable = source.Where(IsTestable).ToList();
        Results.Clear();
        foreach (var d in testable) Results.Add(new TesterDeck(d, d.Name));
        // Le deck choisi reste choisi s'il est toujours dans la liste, avec ses données à jour.
        var wanted = _selectedId is { } id ? Results.FirstOrDefault(r => r.Deck.Id == id) : null;
        if (IsBusy && wanted is null) wanted = _selected;   // pendant un calcul, le deck testé reste affiché
        SetSingle(wanted);
        // Mode « Comparer » : A et B suivent leur deck (données à jour) ou disparaissent s'il est supprimé.
        TesterDeck? Fresh(TesterDeck? d) => d is null ? null : Results.FirstOrDefault(r => r.Deck.Id == d.Deck.Id);
        if (_deckA is not null || _deckB is not null) { _deckA = Fresh(_deckA); _deckB = Fresh(_deckB); RaiseSlots(); SyncCompare(); }
        var drafts = source.Count - testable.Count;
        Message = _library.Decks.Count == 0
            ? "Aucun deck sauvegardé pour l'instant : crée-en un dans le Deck Builder ou la Cuisine."
            : testable.Count == 0
                ? "Aucun deck complet ne correspond. Seuls les decks de 4 cartes peuvent être testés."
                : $"{testable.Count} deck(s) testable(s)" + (drafts > 0 ? $" · {drafts} brouillon(s) incomplet(s) non proposé(s)." : ".");
    }

    /// <summary>Avancement reçu des fils de calcul (très souvent) : transmis à l'interface au plus tous les 0,5 %.</summary>
    private sealed class Throttled(TesterViewModel vm) : IProgress<WorkProgress>
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
