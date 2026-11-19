using System.Collections.ObjectModel;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;
using SpamDrafter.Engines;
using SpamDrafter.Engines.Analysis;
using SpamDrafter.Engines.Cooking;
using SpamDrafter.Engines.History;
using SpamDrafter.Simulator.Engine;

namespace SpamDrafter.App.ViewModels;

/// <summary>Une carte imposée, avec son talent (null = « Choix du chef »).</summary>
public sealed class FixedSlotViewModel : ViewModelBase
{
    private TalentOption _talent;

    public FixedSlotViewModel(CardItem card, KnowledgeBase kb, IReadOnlyList<string>? allowedTalents = null)
    {
        Card = card;
        // Phase 15 : en mode « possédés », seuls les talents possédés se proposent.
        TalentOptions = new[] { new TalentOption(null, "Choix du chef") }
            .Concat(card.Card.Talents.Select((t, i) => (t, i)).Where(x => allowedTalents is null || allowedTalents.Contains(x.t))
                .Select(x => new TalentOption(x.t, $"{x.i + 1}. {kb.TalentName(x.t)}"))).ToList();
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

/// <summary>Une carte imposée pour une génération : son nom (et le talent utilisé s'il y en a un) ; un clic ouvre sa fiche.</summary>
public sealed record SourceCard(CardItem Card, string Label);

/// <summary>Un deck proposé par le Cuisinier.</summary>
public sealed class ProposalViewModel : ViewModelBase
{
    private bool _saved;
    public ProposalViewModel(int rank, GeneratedDeck deck, IReadOnlyList<string> lines, IReadOnlyList<SourceCard>? source = null)
    {
        Deck = deck;
        Source = source ?? Array.Empty<SourceCard>();
        Rank = rank;
        Title = $"Proposition {rank}";
        Lines = lines;
        SetNote(deck);
    }

    private void SetNote(GeneratedDeck deck)
    {
        if (!deck.Measured && deck.Precomputed)
        {
            // Pré-calculée par le Studio : la note de référence est mesurée juste après (D-074), en quelques secondes.
            NoteText = "… / 100"; NoteLabel = "mesure en cours"; NoteHint = ""; NoteDetail = "La note est en cours de mesure.";
            return;
        }
        var n = DeckNote.From(deck.Score, deck.Margin95);
        // Jauge : part de 0 puis monte jusqu'à la note (la barre s'anime, voir le style « gauge » du thème).
        Avalonia.Threading.DispatcherTimer.RunOnce(() => { Gauge = n.Points; Raise(nameof(Gauge)); }, TimeSpan.FromMilliseconds(120));
        NoteText = n.Text;
        NoteLabel = n.Label;
        NoteBrush = SpamDrafter.App.Views.NoteColors.For(n.Label);
        NoteHint = n.NeedsConfirmation ? "à confirmer : peu de duels" : "";
        // Le détail de la mesure reste disponible, mais n'encombre pas l'écran (info-bulle).
        NoteDetail = $"Mesuré sur {deck.Games} duels simulés : {100 * deck.Score:0} % de victoires (marge d'erreur ± {100 * deck.Margin95:0} points). " +
                     "La note va de 0 à 100 ; 100 = niveau d'un deck de la méta.";
    }
    public GeneratedDeck Deck { get; }
    /// <summary>Les cartes de la proposition en pastilles (cadre de rareté, talent choisi), comme dans la liste des decks.</summary>
    public IReadOnlyList<DeckChip> Chips => DeckChipsConverter.Build(Deck.Deck.Select(s => new SpamDrafter.Core.Decks.DeckSlot(s.CardId, s.TalentId)));
    public int Rank { get; }
    /// <summary>Les cartes (et talents) imposées pour générer cette proposition, affichées à droite de la boîte.</summary>
    public IReadOnlyList<SourceCard> Source { get; }
    public bool HasSource => Source.Count > 0;
    /// <summary>« Ingrédient imposé: » au singulier quand il n'y a qu'une carte.</summary>
    public string SourceTitle => Source.Count > 1 ? "Ingrédients imposés:" : "Ingrédient imposé:";
    public string Title { get; }
    /// <summary>Note sur 100 (D-072, D-074), par exemple « 54 / 100 ».</summary>
    public string NoteText { get; private set; } = "";
    /// <summary>Couleur du niveau de la note (Parfait multicolore, Excellent bleu, Bon vert, Passable jaune, Faible orange, Intankable rouge).</summary>
    public Avalonia.Media.IBrush NoteBrush { get; private set; } = Avalonia.Media.Brushes.White;
    /// <summary>Intankable, Faible, Passable, Bon, Excellent ou Parfait.</summary>
    /// <summary>Note de 0 à 100 pour la jauge animée (0 au début, puis la note).</summary>
    public double Gauge { get; private set; }
    public string NoteLabel { get; private set; } = "";
    public string NoteHint { get; private set; } = "";
    public string NoteDetail { get; private set; } = "";
    public bool HasNoteHint => NoteHint.Length > 0;
    public IReadOnlyList<string> Lines { get; }
    public bool Saved { get => _saved; set { if (Set(ref _saved, value)) Raise(nameof(SaveLabel)); } }
    /// <summary>Le deck sauvegardé à partir de cette proposition (null tant qu'elle ne l'est pas).</summary>
    public Deck? SavedDeck { get; set; }
    public string SaveLabel => Saved ? "Sauvegardé ✓" : "Sauvegarder";
}

/// <summary>Une ligne de l'historique : une génération ou un rapport gardés en mémoire.</summary>
public sealed record HistoryItem(HistoryEntry Entry, string Title, string Subtitle);

/// <summary>
/// Onglet Génération de deck (phase 8, lot D) : choisir 1 à 4 cartes, lancer le Grand Cuisinier, lire les propositions,
/// demander le rapport du Grand Analyseur, sauvegarder un deck (origine « généré », visible dans le Drafter, D-017).
/// Les calculs passent par le coordinateur, sur un fil de travail : l'interface reste utilisable et le calcul s'annule.
/// </summary>
public sealed class GeneratorViewModel : ViewModelBase
{
    private readonly KnowledgeBase _kb;
    private readonly Coordinator _coordinator;
    private readonly DeckLibrary _library;
    private readonly Dictionary<string, CardItem> _byId;
    private readonly Action<Action> _post;
    private CancellationTokenSource? _cts;
    private EffortOption _effort;
    private string _status = "Choisis 1 à 4 cartes, puis lance la cuisine.";
    private string _step = "";
    private double _progress;
    private bool _busy;
    private readonly GenerationHistory? _history;

    /// <param name="post">Exécute une action sur le fil de l'interface (par défaut, le répartiteur d'Avalonia).</param>
    public GeneratorViewModel(CardBrowser browser, IReadOnlyList<CardItem> items, KnowledgeBase kb, Coordinator coordinator,
        DeckLibrary library, Action<Action>? post = null, GenerationHistory? history = null)
    {
        Browser = browser; _kb = kb; _coordinator = coordinator; _library = library; _history = history;
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
        OpenCardCommand = new RelayCommand(p => { if (p is SourceCard sc) OpenCardRequested?.Invoke(sc.Card); });
        RemoveProposalCommand = new RelayCommand(p => { if (p is ProposalViewModel pr) RemoveProposal(pr); });
        ClearCommand = new RelayCommand(_ => { if (!IsBusy) { Fixed.Clear(); Refresh(); } });
        GenerateCommand = new RelayCommand(_ => _ = GenerateAsync(), _ => !IsBusy && Fixed.Count is > 0 and <= Deck.MaxCards);
        CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsBusy);
        SaveCommand = new RelayCommand(p => { if (p is ProposalViewModel pr) Save(pr); });
        TestCommand = new RelayCommand(p => { if (p is ProposalViewModel pr && !IsBusy) TestProposal(pr); }, _ => !IsBusy);
        Proposals.CollectionChanged += (_, _) => Raise(nameof(HasProposals));
        OpenHistoryCommand = new RelayCommand(p => { if (p is HistoryItem h) OpenHistory(h.Entry); }, _ => !IsBusy);
        RemoveHistoryCommand = new RelayCommand(p => { if (p is HistoryItem h) _history?.Remove(h.Entry.Id); });
        ClearHistoryCommand = new RelayCommand(_ => _history?.ClearKind(HistoryKind.Generation), _ => HistoryItems.Count > 0);
        if (_history is not null) { _history.Changed += (_, _) => RefreshHistory(); RefreshHistory(); }
    }

    private CollectionViewModel? _collection;
    /// <summary>Collection du joueur (phase 15) : en mode « possédés », le Cuisinier ne propose que ses cartes et ses talents.</summary>
    public CollectionViewModel? Collection
    {
        get => _collection;
        set
        {
            _collection = value;
            if (value is null) return;
            value.Changed += () =>
            {
                // Une carte imposée qui n'est plus possédée est retirée ; les talents imposés sont revérifiés.
                if (!IsBusy)
                {
                    foreach (var f in Fixed.Where(f => !value.Shows(f.Card)).ToList()) Fixed.Remove(f);
                    foreach (var f in Fixed.Where(f => f.Talent.Id is { } t && !value.AllowedTalents(f.Card).Contains(t) && value.OwnedOnly).ToList())
                    { var i = Fixed.IndexOf(f); Fixed[i] = new FixedSlotViewModel(f.Card, _kb, value.AllowedTalents(f.Card)); }
                    Refresh();
                }
                Raise(nameof(OwnedOnly));
            };
        }
    }
    /// <summary>Le générateur est limité aux cartes et talents possédés.</summary>
    public bool OwnedOnly => _collection?.OwnedOnly == true;

    public CardBrowser Browser { get; }
    public ObservableCollection<FixedSlotViewModel> Fixed { get; } = new();
    public ObservableCollection<ProposalViewModel> Proposals { get; } = new();
    public IReadOnlyList<EffortOption> EffortOptions { get; }

    public RelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand RemoveProposalCommand { get; }
    public RelayCommand OpenCardCommand { get; }
    /// <summary>Demande l'ouverture de la fiche détaillée d'une carte (un clic sur son nom).</summary>
    public event Action<CardItem>? OpenCardRequested;
    public RelayCommand ClearCommand { get; }
    public RelayCommand GenerateCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand SaveCommand { get; }
    /// <summary>« Tester ce deck → » : sauvegarde la proposition si besoin, puis l'ouvre dans l'onglet Tester.</summary>
    public RelayCommand TestCommand { get; }
    /// <summary>Demande d'ouverture d'un deck sauvegardé dans l'onglet Tester.</summary>
    public event Action<Deck>? TestRequested;
    /// <summary>Vrai quand la colonne « Résultat » a des propositions à montrer.</summary>
    public bool HasProposals => Proposals.Count > 0;
    public RelayCommand OpenHistoryCommand { get; }
    public RelayCommand RemoveHistoryCommand { get; }
    public RelayCommand ClearHistoryCommand { get; }

    /// <summary>Dernières générations et derniers rapports, du plus récent au plus ancien.</summary>
    public ObservableCollection<HistoryItem> HistoryItems { get; } = new();
    public bool HasHistory => HistoryItems.Count > 0;
    public string HistoryTitle => HistoryItems.Count == 0 ? "Historique (vide)" : $"Historique ({HistoryItems.Count})";

    /// <summary>Explication courte, pour quelqu'un qui découvre l'onglet.</summary>
    public string HowItWorks =>
        "• Tu choisis 1 à 4 cartes que tu veux jouer. Le Grand Cuisinier cherche les meilleures cartes et talents pour les compléter : " +
        "il assemble des milliers de decks et les fait jouer les uns contre les autres dans un simulateur de duels, puis garde les meilleurs." +
        Environment.NewLine +
        "• La note va de 0 à 100. Elle vient du pourcentage de duels simulés que le deck gagne contre " +
        (_coordinator.Opponents == OpponentSource.Synthetic ? $"{_coordinator.Synthetic.Meta.Count} decks forts livrés avec l'application (la « méta »)" : "des decks tirés au hasard") +
        " : 0 = il ne gagne presque jamais, 100 = il gagne la moitié de ses duels ou plus, donc il est au niveau d'un deck de la méta. " +
        "Intankable : 0. Faible : 1 à 34. Passable : 35 à 54. Bon : 55 à 74. Excellent : 75 à 99. Parfait : 100. La proposition et la fiche d'un même deck donnent toujours la même note." +
        Environment.NewLine +
        "• Ce sont des parties simulées par une intelligence artificielle, pas de vraies parties : {{prends les propositions comme des idées à essayer}}. " +
        "Une même demande donne toujours le même résultat, et elle est gardée en mémoire : la relancer est instantané.";

    public string FixedText => $"{Fixed.Count} / {Deck.MaxCards} ingrédient{(Fixed.Count > 1 ? "s" : "")} imposé{(Fixed.Count > 1 ? "s" : "")}";
    public EffortOption SelectedEffort { get => _effort; set => Set(ref _effort, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    /// <summary>Le seul message affiché dans la colonne « Générateur » : un refus, une annulation ou une erreur (le reste est silencieux).</summary>
    public string Problem { get => _problem; private set { if (Set(ref _problem, value)) Raise(nameof(HasProblem)); } }
    public bool HasProblem => _problem.Length > 0;
    private string _problem = "";
    private void Warn(string text) { Status = text; Problem = text; }
    public string Step { get => _step; private set => Set(ref _step, value); }
    /// <summary>Avancement de 0 à 100.</summary>
    public double Progress
    {
        get => _progress;
        private set { if (Set(ref _progress, value)) { Raise(nameof(CookStage)); Raise(nameof(CookPercent)); } }
    }

    /// <summary>Étape de la « cuisine » selon l'avancement : épluchage, découpe, cuisson, dressage (un quart chacune).</summary>
    public string CookStage => _progress < 25 ? "Épluchage des ingrédients"
        : _progress < 50 ? "Découpe des ingrédients"
        : _progress < 75 ? "Cuisson en cours"
        : "Dressage en cours";
    /// <summary>Les trois points qui apparaissent l'un après l'autre, en boucle (voir <see cref="_dotTimer"/>).</summary>
    public string CookDots => new string('.', _dots);
    public string CookPercent => $"{_progress:0} %";
    private int _dots;
    private Avalonia.Threading.DispatcherTimer? _dotTimer;

    private void StartDots()
    {
        _dots = 1; Raise(nameof(CookDots));
        _dotTimer ??= new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        _dotTimer.Tick -= DotsTick; _dotTimer.Tick += DotsTick;
        _dotTimer.Start();
    }

    private void StopDots() => _dotTimer?.Stop();

    private void DotsTick(object? sender, EventArgs e)
    {
        _dots = _dots % 3 + 1;   // . puis .. puis ... puis on recommence
        Raise(nameof(CookDots));
    }
    /// <summary>Astuce affichée pendant l'attente d'une génération.</summary>
    public string Tip { get; private set; } = Tips.Any();
    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (!Set(ref _busy, value)) return;
            if (value) { Tip = Tips.Any(); Raise(nameof(Tip)); StartDots(); } else StopDots();
            foreach (var c in new[] { AddCommand, GenerateCommand, CancelCommand, TestCommand, OpenHistoryCommand })
                c.RaiseCanExecuteChanged();
        }
    }
    private void AddSelected()
    {
        var item = Browser.Selected;
        if (item is null) { Warn("Choisis d'abord une carte dans la liste."); return; }
        if (Fixed.Count >= Deck.MaxCards) { Warn($"La sélection contient déjà {Deck.MaxCards} cartes."); return; }
        if (Fixed.Any(f => f.Card.Card.Id == item.Card.Id)) { Warn("Cette carte est déjà choisie."); return; }
        Problem = "";
        Fixed.Add(new FixedSlotViewModel(item, _kb, OwnedOnly ? _collection!.AllowedTalents(item) : null));
        Refresh();
    }

    private void Refresh()
    {
        Raise(nameof(FixedText));
        GenerateCommand.RaiseCanExecuteChanged();
        Status = Fixed.Count == 0 ? "Choisis 1 à 4 cartes, puis lance la génération."
            : Fixed.Count == Deck.MaxCards ? "4 ingrédients imposés : le Cuisinier choisira les talents et l'ordre."
            : Fixed.Count == 1 && !OwnedOnly && _coordinator.Opponents == OpponentSource.Synthetic && _coordinator.Synthetic.OneCard is not null
                ? "Le Cuisinier complétera avec 3 cartes. Avec le talent au choix (réglage rapide ou normal), les propositions sont " +
                  "immédiates : elles ont été calculées à l'avance."
            : $"Le Cuisinier complétera avec {Deck.MaxCards - Fixed.Count} carte(s).";
    }

    public async Task GenerateAsync()
    {
        if (IsBusy || Fixed.Count == 0) return;
        var owned = _collection?.OwnedMap;
        if (owned is not null && owned.Count < Deck.MaxCards)
        {
            Warn($"Il faut au moins {Deck.MaxCards} cartes possédées pour cuisiner un deck (tu en as {owned.Count}). Importe tes cartes (« Importer ») ou passe sur « Toutes les cartes et talents ».");
            return;
        }
        var scope = _collection?.Scope ?? "";
        var selection = Fixed.Select(f => new FixedCard(f.Card.Card.Id, f.Talent.Id)).ToList();
        var asSlots = selection.Select(f => new DuelSlot(f.CardId, f.TalentId)).ToList();
        Problem = "";
        // Même demande déjà calculée : on la reprend en mémoire, sans attendre (les moteurs sont reproductibles).
        if (_history?.FindGeneration(asSlots, SelectedEffort.Value, scope) is { Proposals: { Count: > 0 } kept } hit)
        {
            ShowProposals(kept, asSlots);
            Status = $"Résultat retrouvé en mémoire (calculé {HistoryText.When(hit.CreatedUtc)}) : aucun nouveau calcul. " + NoteExplanation();
            return;
        }
        var effort = SelectedEffort.Value;
        await Run("Cuisine", async (cancel, progress) =>
        {
            var decks = await _coordinator.GenerateAsync(selection, effort, Seed(), cancel, progress, owned);
            ShowProposals(decks, asSlots, newBatch: true);
            if (decks.Count > 0 && decks[0].Precomputed && !decks[0].Measured)
            {
                // Propositions pré-calculées par le Studio : elles s'affichent tout de suite, puis leur note est mesurée comme
                // celle de la fiche (D-074) en quelques secondes, et gardée en mémoire pour la prochaine fois.
                Status = $"{decks.Count} proposition(s) calculée(s) à l'avance par le Studio. Mesure de la note…";
                Step = "Mesure de la note…";
                decks = await _coordinator.MeasureNotesAsync(decks, Seed(), cancel, progress);
                ShowProposals(decks, asSlots, newBatch: false);   // même lot : les notes mesurées remplacent les provisoires
            }
            if (decks.Count == 0) Problem = "Aucune proposition.";
            Status = decks.Count == 0 ? "Aucune proposition."
                : $"{decks.Count} proposition(s){(decks[0].Precomputed ? ", calculées à l'avance par le Studio" : "")}. " + NoteExplanation();
            if (decks.Count > 0) _history?.AddGeneration(asSlots, effort, GenerationLabel(asSlots, effort), decks, scope);
        });
    }

    // Les résultats s'accumulent : chaque génération ajoute ses propositions en haut de la liste (les précédentes restent) ; chacune se retire avec sa croix.
    private readonly List<ProposalViewModel> _batch = new();
    private int _nextRank = 1;

    private static string DeckKey(GeneratedDeck d) => string.Join("|", d.Deck.Select(s => s.CardId + ":" + s.TalentId).OrderBy(x => x, StringComparer.Ordinal));

    /// <param name="newBatch">Vrai : nouveau lot, ajouté au-dessus des résultats existants. Faux : le lot précédent est remplacé (notes mesurées).</param>
    private void ShowProposals(IReadOnlyList<GeneratedDeck> decks, IReadOnlyList<DuelSlot> source, bool newBatch = true)
    {
        int firstRank;
        if (newBatch) { _batch.Clear(); firstRank = _nextRank; }
        else
        {
            firstRank = _batch.Count > 0 ? _batch.Min(b => b.Rank) : _nextRank;
            foreach (var old in _batch) Proposals.Remove(old);
            _batch.Clear();
        }
        // Un deck déjà présent dans les résultats n'est pas ajouté une deuxième fois.
        var chips = source.Where(x => _byId.ContainsKey(x.CardId))
            .Select(x => { var c = _byId[x.CardId]; return new SourceCard(c, x.TalentId is null ? c.Name : $"{c.Name} · {_kb.TalentName(x.TalentId)}"); }).ToList();
        var existing = Proposals.Select(p => DeckKey(p.Deck)).ToHashSet();
        var rank = firstRank - 1;
        var insertAt = 0;
        foreach (var d in decks)
        {
            if (!existing.Add(DeckKey(d))) continue;
            var vm = new ProposalViewModel(++rank, d, d.Deck.Select(Describe).ToList(), chips);
            _batch.Add(vm);
            Proposals.Insert(insertAt++, vm);
        }
        _nextRank = Math.Max(_nextRank, rank + 1);
    }

    private void RemoveProposal(ProposalViewModel p) { Proposals.Remove(p); _batch.Remove(p); }

    private string NoteExplanation() =>
        DeckNote.Explanation(_coordinator.Synthetic.Meta.Count, _coordinator.Opponents == OpponentSource.Synthetic);

    private string GenerationLabel(IReadOnlyList<DuelSlot> slots, Effort effort) =>
        string.Join(" + ", slots.Select(s => _byId.TryGetValue(s.CardId, out var c) ? c.Name : s.CardId)) + " · " + EffortName(effort);

    private static string EffortName(Effort e) => e switch { Effort.Rapide => "rapide", Effort.Precis => "précis", _ => "normal" };

    // ----- Historique -----
    private void RefreshHistory()
    {
        HistoryItems.Clear();
        foreach (var e in _history!.Entries.Where(e => e.Kind == HistoryKind.Generation))   // les rapports et les confrontations sont dans l'onglet Tester
            HistoryItems.Add(new HistoryItem(e, e.Label, "Cuisine · " + HistoryText.When(e.CreatedUtc)));
        Raise(nameof(HasHistory)); Raise(nameof(HistoryTitle));
        ClearHistoryCommand.RaiseCanExecuteChanged();
    }

    private void OpenHistory(HistoryEntry e)
    {
        if (IsBusy) return;
        if (e.Kind == HistoryKind.Generation && e.Proposals is { } props)
        {
            Fixed.Clear();
            foreach (var slot in e.Slots)
                if (_byId.TryGetValue(slot.CardId, out var card))
                {
                    var f = new FixedSlotViewModel(card, _kb);
                    f.Talent = f.TalentOptions.FirstOrDefault(o => o.Id == slot.TalentId) ?? f.TalentOptions[0];
                    Fixed.Add(f);
                }
            if (Enum.TryParse<Effort>(e.Effort, out var effort)) SelectedEffort = EffortOptions.First(o => o.Value == effort);
            Refresh();
            ShowProposals(props, e.Slots);
            Status = $"Cuisine rouverte depuis l'historique (calculée {HistoryText.When(e.CreatedUtc)}). " + NoteExplanation();
        }
    }

    /// <summary>Lance un calcul : avancement, annulation, erreurs. Le corps reprend sur le fil de l'interface.</summary>
    private async Task Run(string what, Func<CancellationToken, IProgress<WorkProgress>, Task> body)
    {
        _cts = new CancellationTokenSource();
        IsBusy = true;
        Progress = 0; Step = "Préparation…";
        Problem = "";
        Status = $"{what} en cours… (tu peux annuler)";
        try { await body(_cts.Token, new Throttled(this)); }
        catch (OperationCanceledException) { Warn($"{what} annulée."); }
        catch (Exception e) { Warn($"{what} impossible : {e.Message}"); }
        finally
        {
            IsBusy = false;
            Step = ""; Progress = 0;
            _cts.Dispose(); _cts = null;
        }
    }

    private Deck? Save(ProposalViewModel p)
    {
        if (p.Saved) return p.SavedDeck;
        var names = p.Deck.Deck.Select(s => _byId.TryGetValue(s.CardId, out var c) ? c.Name : s.CardId);
        var deck = new Deck
        {
            Name = DeckNames.MakeUnique(_library.Decks, string.Join(", ", names), Guid.Empty),
            Origin = DeckOrigin.Generated,
            GameVersion = _kb.Manifest.GameVersion,
            Slots = p.Deck.Deck.Select(s => new DeckSlot(s.CardId, s.TalentId)).ToList(),
        };
        _library.Save(deck);
        p.Saved = true;
        p.SavedDeck = deck;
        Status = $"« {deck.Name} » sauvegardé : il apparaît dans le Deck builder et le Drafter.";
        return deck;
    }

    private void TestProposal(ProposalViewModel p)
    {
        if (Save(p) is { } deck) TestRequested?.Invoke(deck);
    }

    private string Describe(DuelSlot s)
    {
        var name = _byId.TryGetValue(s.CardId, out var c) ? c.Name : s.CardId;
        var imposed = Fixed.Any(f => f.Card.Card.Id == s.CardId) ? " (imposée)" : "";
        return s.TalentId is null ? $"{name}{imposed} — aucun talent" : $"{name}{imposed} — {_kb.TalentName(s.TalentId)}";
    }


    /// <summary>Nom des adversaires dans les messages.</summary>
    public static string OpponentsText(OpponentSource s) => s switch
    {
        OpponentSource.Synthetic => "decks de la méta synthétique",
        _ => "decks au hasard (données sans méta synthétique)",
    };

    /// <summary>Même graine à chaque fois : même sélection = mêmes propositions (reproductible).</summary>
    private static ulong Seed() => Coordinator.AppSeed;

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
