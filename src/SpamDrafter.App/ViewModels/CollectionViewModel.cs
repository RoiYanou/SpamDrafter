using System.Collections.ObjectModel;
using SpamDrafter.Core.Collection;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;
using SpamDrafter.Core.Settings;
using SpamDrafter.Core.Text;

namespace SpamDrafter.App.ViewModels;

/// <summary>Une diapositive du tutoriel : une ou plusieurs lignes de texte et une illustration (null si le paquet de données n'en a pas).</summary>
public sealed record TutorialStep(IReadOnlyList<string> Lines, Avalonia.Media.Imaging.Bitmap? Image)
{
    public bool HasImage => Image is not null;
}

/// <summary>Une ligne de la liste de l'importation précise : la carte et ce que le joueur en possède.</summary>
public sealed record CollectionRow(CardItem Item, string Status, bool Owned);

/// <summary>
/// Phase 15 (D-087) : la collection du joueur, l'interrupteur « Cartes et talents possédés / Toutes les cartes et talents »
/// et les deux importations manuelles (le jeu ne permet pas de lire la collection).
/// <list type="bullet">
/// <item><b>Importation complète</b> : toutes les cartes, une à une, dans l'ordre du jeu. Le résultat remplace la collection à la fin ;
/// « Continuer plus tard » garde la position, « Annuler » jette le brouillon (la collection reste telle qu'elle était).</item>
/// <item><b>Importation précise</b> : on cherche une carte et on règle ce qu'on en possède, tout de suite.</item>
/// </list>
/// </summary>
public sealed class CollectionViewModel : ViewModelBase
{
    private readonly KnowledgeBase _kb;
    private readonly IReadOnlyList<CardItem> _ordered;
    private readonly Dictionary<string, CardItem> _byId;
    private readonly string _path;
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;

    public CollectionViewModel(KnowledgeBase kb, IReadOnlyList<CardItem> items, string collectionPath, AppSettings settings, Action saveSettings)
    {
        _kb = kb; _path = collectionPath; _settings = settings; _saveSettings = saveSettings;
        _ordered = items.OrderBy(i => i.Card.Order).ToList();
        _byId = items.ToDictionary(i => i.Card.Id);
        Data = PlayerCollection.Load(collectionPath);
        // Les cartes disparues d'une nouvelle version du jeu sont ignorées (jamais supprimées du fichier avant la prochaine importation).
        AnswerCommand = new RelayCommand(p => { if (TryLevel(p, out var l)) Answer(l); });
        BackCommand = new RelayCommand(_ => Back(), _ => IsFull && _fullIndex > 0);
        FullCommand = new RelayCommand(_ => StartFull());
        PreciseCommand = new RelayCommand(_ => StartPrecise());
        LaterCommand = new RelayCommand(_ => Later());
        CancelImportCommand = new RelayCommand(_ => ConfirmingCancel = true);   // demande d'abord confirmation
        ConfirmCancelImportCommand = new RelayCommand(_ => { ConfirmingCancel = false; CancelImport(); });
        KeepImportCommand = new RelayCommand(_ => ConfirmingCancel = false);
        CloseCommand = new RelayCommand(_ => Close());
        OpenCommand = new RelayCommand(_ => OpenMenu());
        MenuCommand = new RelayCommand(_ => { if (IsFull) Later(); else if (IsPrecise && HasPending) Review(); else OpenMenu(); });
        ReviewCommand = new RelayCommand(_ => Review(), _ => HasPending);
        ConfirmPreciseCommand = new RelayCommand(_ => ApplyPending());
        KeepEditingCommand = new RelayCommand(_ => Go(3));
        DiscardPendingCommand = new RelayCommand(_ => { _pending.Clear(); Message = "Changements abandonnés : ta collection n'a pas changé."; OpenMenu(); });
        TutorialCommand = new RelayCommand(_ => { if (!ShowTutorial) TutorialPage = 0; ShowTutorial = !ShowTutorial; });   // s'ouvre toujours à la page 1
        ClearCommand = new RelayCommand(_ => { ClearText = ""; ConfirmingClear = true; Message = ""; });
        ConfirmClearCommand = new RelayCommand(_ => { if (CanConfirmClear) { ConfirmingClear = false; ClearAll(); } }, _ => CanConfirmClear);
        CancelClearCommand = new RelayCommand(_ => ConfirmingClear = false);
        SetModeCommand = new RelayCommand(p => OwnedOnly = p is "owned");
        SetPreciseCommand = new RelayCommand(p => { if (TryLevel(p, out var l)) SetPrecise(l); });
        SelectRowCommand = new RelayCommand(p => { if (p is CollectionRow r) PreciseSelected = r; });
    }

    public PlayerCollection Data { get; private set; }

    /// <summary>Met à jour les étoiles de talents des cartes (mode « possédés » seulement), puis prévient les listes.</summary>
    private void Notify()
    {
        foreach (var i in _ordered) i.SetStars(OwnedOnly ? Math.Max(0, Data.LevelOf(i.Card.Id)) : 3);
        Changed?.Invoke();
    }

    /// <summary>À appeler une fois les listes branchées : affiche les étoiles de l'état enregistré.</summary>
    public void Start() { foreach (var i in _ordered) i.SetStars(OwnedOnly ? Math.Max(0, Data.LevelOf(i.Card.Id)) : 3); }
    /// <summary>La collection ou le mode d'affichage ont changé : les listes se rafraîchissent.</summary>
    public event Action? Changed;

    // ----- Interrupteur -----
    /// <summary>Vrai : l'application ne montre que les cartes et talents possédés ; faux : tout le jeu (comportement d'origine).</summary>
    public bool OwnedOnly
    {
        get => _settings.OwnedOnly;
        set
        {
            if (_settings.OwnedOnly == value) return;
            _settings.OwnedOnly = value;   // en mémoire seulement : jamais enregistré entre les démarrages
            Raise(); Raise(nameof(ModeText));
            Notify();
        }
    }
    public string ModeText => OwnedOnly ? "Cartes et talents possédés" : "Toutes les cartes et talents";
    public string Summary => Data.IsEmpty ? "Aucune carte importée" : $"{Data.Count} carte(s) importée(s) sur {_ordered.Count}";

    // ----- Règles de visibilité (utilisées par les listes) -----
    /// <summary>La carte est montrée : toujours en mode « toutes », seulement si possédée en mode « possédées ».</summary>
    public bool Shows(CardItem c) => !OwnedOnly || Data.Owns(c.Card.Id);

    /// <summary>Les talents proposables pour la carte : tous, ou les n premiers possédés.</summary>
    public IReadOnlyList<string> AllowedTalents(CardItem c) =>
        OwnedOnly ? Data.OwnedTalents(c.Card.Id, c.Card.Talents) : c.Card.Talents;

    /// <summary>Un deck est montré s'il est possédé (cartes et talents) en mode « possédées », toujours sinon.</summary>
    public bool ShowsDeck(Deck d) => !OwnedOnly || Data.OwnsDeck(d, id => _byId.TryGetValue(id, out var c) ? c.Card.Talents : null);

    /// <summary>Pour le Grand Cuisinier : carte → niveau de talent, ou null en mode « toutes ».</summary>
    public IReadOnlyDictionary<string, int>? OwnedMap => OwnedOnly ? new Dictionary<string, int>(Data.Cards) : null;
    /// <summary>Empreinte pour la mémoire du générateur (vide en mode « toutes »).</summary>
    public string Scope => !OwnedOnly ? "" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(Data.Fingerprint())))[..16];

    private void Persist(bool changedData = true)
    {
        Data.GameVersion = _kb.Manifest.GameVersion;
        Data.TrySave(_path);
        Raise(nameof(Summary)); Raise(nameof(HasDraft)); Raise(nameof(MenuFull));
        if (changedData) Notify();
    }

    // ----- Fenêtre -----
    private int _screen;   // 0 fermée, 1 menu, 2 complète, 3 précise
    public bool IsOpen => _screen != 0;
    public bool IsMenu => _screen == 1;
    public bool IsFull => _screen == 2;
    public bool IsPrecise => _screen == 3;
    /// <summary>Importation précise : écran de confirmation avec la liste des changements.</summary>
    public bool IsPreciseConfirm => _screen == 4;
    /// <summary>Le tutoriel ne concerne que l'importation complète : son bouton n'est que dans le menu et dans l'importation complète.</summary>
    public bool ShowTutorialButton => _screen is 1 or 2;
    private void Go(int screen)
    {
        _screen = screen;
        ConfirmingCancel = false;
        foreach (var n in new[] { nameof(IsOpen), nameof(IsMenu), nameof(IsFull), nameof(IsPrecise), nameof(IsPreciseConfirm), nameof(ShowTutorialButton) }) Raise(n);
        BackCommand.RaiseCanExecuteChanged();
    }

    public RelayCommand OpenCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand MenuCommand { get; }
    public RelayCommand FullCommand { get; }
    public RelayCommand PreciseCommand { get; }
    public RelayCommand AnswerCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand LaterCommand { get; }
    public RelayCommand CancelImportCommand { get; }
    public RelayCommand ConfirmCancelImportCommand { get; }
    public RelayCommand KeepImportCommand { get; }

    private bool _confirmingCancel;
    /// <summary>La confirmation « Annuler l'importation ? » est affichée.</summary>
    public bool ConfirmingCancel { get => _confirmingCancel; private set => Set(ref _confirmingCancel, value); }
    public RelayCommand TutorialCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand ConfirmClearCommand { get; }
    public RelayCommand CancelClearCommand { get; }

    private bool _confirmingClear;
    private string _clearText = "";
    /// <summary>La confirmation d'effacement est affichée : le joueur doit écrire « confirmer ».</summary>
    public bool ConfirmingClear { get => _confirmingClear; private set => Set(ref _confirmingClear, value); }
    public string ClearText { get => _clearText; set { if (Set(ref _clearText, value)) { Raise(nameof(CanConfirmClear)); ConfirmClearCommand.RaiseCanExecuteChanged(); } } }
    public bool CanConfirmClear => string.Equals(_clearText.Trim(), "confirmer", StringComparison.OrdinalIgnoreCase);
    /// <summary>Interrupteur : paramètre « owned » (cartes et talents possédés) ou « all » (toutes les cartes et talents).</summary>
    public RelayCommand SetModeCommand { get; }
    public RelayCommand SetPreciseCommand { get; }
    public RelayCommand ReviewCommand { get; }
    public RelayCommand ConfirmPreciseCommand { get; }
    public RelayCommand KeepEditingCommand { get; }
    public RelayCommand DiscardPendingCommand { get; }
    public RelayCommand SelectRowCommand { get; }

    private string _message = "";
    /// <summary>Dernier message (importation terminée, collection effacée…).</summary>
    public string Message { get => _message; private set { if (Set(ref _message, value)) Raise(nameof(HasMessage)); } }
    public bool HasMessage => _message.Length > 0;

    private bool _showTutorial;
    /// <summary>Le tutoriel d'utilisation (contenu à écrire : l'utilisateur le guidera).</summary>
    public bool ShowTutorial { get => _showTutorial; private set => Set(ref _showTutorial, value); }
    /// <summary>Tutoriel de l'importation complète (texte de l'utilisateur) ; les illustrations viennent du paquet de données
    /// (<c>tutorial/etapeN.webp</c>, D-016) : sans elles, seul le texte s'affiche.</summary>
    public IReadOnlyList<TutorialStep> TutorialSteps => _steps ??= BuildSteps();
    private IReadOnlyList<TutorialStep>? _steps;

    // Tutoriel en diapositives : une étape par page, avec « Page n / N ».
    private int _tutorialPage;
    public int TutorialPage { get => _tutorialPage; private set { if (Set(ref _tutorialPage, value)) RaiseTutorialPage(); } }
    public TutorialStep CurrentStep => TutorialSteps[Math.Clamp(_tutorialPage, 0, TutorialSteps.Count - 1)];
    public string PageText => $"Page {_tutorialPage + 1} / {TutorialSteps.Count}";
    public bool CanPreviousPage => _tutorialPage > 0;
    public bool IsLastPage => _tutorialPage >= TutorialSteps.Count - 1;
    public string NextLabel => IsLastPage ? "Terminer" : "Suivant →";
    public RelayCommand PreviousPageCommand => _prevPage ??= new RelayCommand(_ => { if (_tutorialPage > 0) TutorialPage = _tutorialPage - 1; }, _ => CanPreviousPage);
    public RelayCommand NextPageCommand => _nextPage ??= new RelayCommand(_ => { if (IsLastPage) ShowTutorial = false; else TutorialPage = _tutorialPage + 1; });
    private RelayCommand? _prevPage, _nextPage;
    private void RaiseTutorialPage()
    {
        foreach (var n in new[] { nameof(CurrentStep), nameof(PageText), nameof(CanPreviousPage), nameof(IsLastPage), nameof(NextLabel) }) Raise(n);
        PreviousPageCommand.RaiseCanExecuteChanged();
    }
    private IReadOnlyList<TutorialStep> BuildSteps()
    {
        // Les étapes 1 et 2 partagent la première diapositive ; l'image d'une diapositive est celle de sa dernière étape (etapeN.webp).
        var slides = new (string[] Lines, int ImageStep)[]
        {
            (new[] { "1. Lancez le jeu officiel SPAM.", "2. Allez dans la collection et vérifiez d'avoir ces paramètres de filtres." }, 2),
            (new[] { "3. Allez dans les paramètres de SPAM et désactivez le plein écran." }, 3),
            (new[] { "4. Mettez SPAM et SpamDrafter côte à côte sur votre écran et lancez l'importation complète." }, 4),
            (new[] { "5. Aidez-vous de l'indication de talent puis sélectionnez !" }, 5),
            (new[] { "Bonus : Profite du filtre « NOUVEAUTÉS » lorsqu'il apparaît pour mettre à jour SpamDrafter avec l'importation précise !" }, 6),
        };
        return slides.Select(sl =>
        {
            Avalonia.Media.Imaging.Bitmap? image = null;
            if (_kb.ImagePath($"tutorial/etape{sl.ImageStep}.webp") is { } path)
            {
                try { using var s = File.OpenRead(path); image = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(s, 1400); }
                catch (Exception) { }
            }
            return new TutorialStep(sl.Lines, image);
        }).ToList();
    }

    private void OpenMenu() { ShowTutorial = false; ConfirmingClear = false; Go(1); Raise(nameof(Summary)); Raise(nameof(HasDraft)); Raise(nameof(MenuFull)); }
    private void Close()
    {
        if (IsFull) { Later(); return; }
        if (IsPrecise && HasPending) { Review(); return; }
        if (IsPreciseConfirm) { Go(3); return; }
        ShowTutorial = false; ConfirmingClear = false; Message = ""; Go(0);
    }

    private static bool TryLevel(object? p, out int level)
    {
        level = 0;
        return p is string s && int.TryParse(s, out level) && level is >= PlayerCollection.NotOwned and <= PlayerCollection.MaxTalentLevel;
    }

    private static string LevelText(int level) => level switch
    {
        PlayerCollection.NotOwned => "carte non possédée", 0 => "carte sans talent", _ => $"carte avec le talent {level} (et ceux d'avant)",
    };

    // ----- Menu -----
    public bool HasDraft => Data.Draft is { Position: > 0 } or { Answers.Count: > 0 };
    public string MenuFull => HasDraft ? $"Reprendre l'importation complète ({Data.Draft!.Position} / {_ordered.Count})" : "Importation complète (~5 minutes)";

    private void ClearAll()
    {
        Data.Clear(); Data.Draft = null;
        Persist();
        Message = "Ta collection a été effacée.";
    }

    // ----- Importation complète -----
    private int _fullIndex;
    private Dictionary<string, int> _answers = new();
    private CardItem? _current;
    /// <summary>Illustration de base de la carte à l'importation (jamais le skin choisi dans l'application).</summary>
    public Avalonia.Media.Imaging.Bitmap? CurrentImage { get; private set; }
    public CardItem? Current { get => _current; private set { if (Set(ref _current, value)) { CurrentImage = value?.LoadBaseImage(520); Raise(nameof(CurrentImage)); foreach (var n in new[] { nameof(CardTitle), nameof(ProgressText), nameof(ProgressValue), nameof(TalentLines), nameof(CanTalent1), nameof(CanTalent2), nameof(CanTalent3), nameof(CurrentAnswerText) }) Raise(n); } } }
    public string CardTitle => Current?.Name ?? "";
    public string ProgressText => $"Carte {Math.Min(_fullIndex + 1, _ordered.Count)} sur {_ordered.Count}";
    public double ProgressValue => 100.0 * _fullIndex / Math.Max(1, _ordered.Count);
    public bool CanTalent1 => Current?.Card.Talents.Count >= 1;
    public bool CanTalent2 => Current?.Card.Talents.Count >= 2;
    public bool CanTalent3 => Current?.Card.Talents.Count >= 3;
    public IReadOnlyList<string> TalentLines => Current is null ? Array.Empty<string>()
        : Current.Card.Talents.Select((t, i) => $"Talent {i + 1} : {_kb.TalentName(t)}").ToList();
    public string CurrentAnswerText => Current is not null && _answers.TryGetValue(Current.Card.Id, out var l) ? "Réponse enregistrée : " + LevelText(l) : "";

    private void StartFull()
    {
        ShowTutorial = false; Message = "";
        var d = Data.Draft;
        _answers = d is null ? new() : new Dictionary<string, int>(d.Answers);
        // Brouillon d'une autre version du jeu : on reprend là où on était, les cartes disparues sont simplement ignorées.
        _fullIndex = d is null ? 0 : Math.Clamp(d.Position, 0, _ordered.Count);
        if (_fullIndex >= _ordered.Count) _fullIndex = Math.Max(0, _ordered.Count - 1);
        Go(2);
        ShowCard();
    }

    private void ShowCard()
    {
        Current = _ordered.Count == 0 ? null : _ordered[_fullIndex];
        BackCommand.RaiseCanExecuteChanged();
        Raise(nameof(ProgressText)); Raise(nameof(ProgressValue)); Raise(nameof(CurrentAnswerText));
    }

    private void Answer(int level)
    {
        if (!IsFull || Current is null) return;
        var max = Current.Card.Talents.Count;
        _answers[Current.Card.Id] = level > max ? max : level;
        if (_fullIndex + 1 >= _ordered.Count) { FinishFull(); return; }
        _fullIndex++;
        SaveDraft();
        ShowCard();
    }

    private void Back()
    {
        if (_fullIndex <= 0) return;
        _fullIndex--; SaveDraft(); ShowCard();
    }

    private void SaveDraft()
    {
        Data.Draft = new ImportDraft { Position = _fullIndex, Answers = new Dictionary<string, int>(_answers) };
        Data.TrySave(_path);   // le brouillon seulement : la collection elle-même ne change pas, aucune liste à rafraîchir
    }

    private void FinishFull()
    {
        Data.Cards = _answers.Where(a => a.Value >= 0 && _byId.ContainsKey(a.Key)).ToDictionary(a => a.Key, a => a.Value);
        Data.ModifiedUtc = DateTime.UtcNow;
        Data.Draft = null;
        Persist();
        Message = $"Importation terminée : {Data.Count} carte(s) possédée(s) sur {_ordered.Count}.";
        Go(1);
        Raise(nameof(Summary)); Raise(nameof(HasDraft)); Raise(nameof(MenuFull));
    }

    /// <summary>« Continuer plus tard » : la position et les réponses sont gardées, la collection reste telle qu'elle était.</summary>
    private void Later()
    {
        SaveDraft();
        Message = "Importation mise en pause : tu pourras la reprendre là où tu t'es arrêté.";
        Go(1);
        Raise(nameof(HasDraft)); Raise(nameof(MenuFull));
    }

    /// <summary>« Annuler l'importation » : le brouillon est jeté, la collection reste telle qu'elle était.</summary>
    private void CancelImport()
    {
        if (IsFull || HasDraft)
        {
            Data.Draft = null; Data.TrySave(_path); _answers.Clear(); _fullIndex = 0;
            Message = "Importation annulée : ta collection n'a pas changé.";
            Go(1);
            Raise(nameof(HasDraft)); Raise(nameof(MenuFull));
        }
    }

    // ----- Importation précise -----
    private string _search = "";
    private CollectionRow? _selected;
    public ObservableCollection<CollectionRow> Rows { get; } = new();
    public string Search { get => _search; set { if (Set(ref _search, value)) RefreshRows(); } }
    public CollectionRow? PreciseSelected
    {
        get => _selected;
        set { if (Set(ref _selected, value)) { PreciseImage = value?.Item.LoadBaseImage(480); Raise(nameof(PreciseImage)); Raise(nameof(PreciseCurrent)); Raise(nameof(HasPrecise)); Raise(nameof(PreciseStatus)); Raise(nameof(PreciseTalentLines)); Raise(nameof(PreciseCanTalent1)); Raise(nameof(PreciseCanTalent2)); Raise(nameof(PreciseCanTalent3)); } }
    }
    public CardItem? PreciseCurrent => _selected?.Item;
    public Avalonia.Media.Imaging.Bitmap? PreciseImage { get; private set; }
    public bool HasPrecise => _selected is not null;
    public string PreciseStatus => _selected is null ? "Choisis une carte dans la liste." : $"{_selected.Item.Name} : {_selected.Status}";
    public bool PreciseCanTalent1 => _selected?.Item.Card.Talents.Count >= 1;
    public bool PreciseCanTalent2 => _selected?.Item.Card.Talents.Count >= 2;
    public bool PreciseCanTalent3 => _selected?.Item.Card.Talents.Count >= 3;
    public IReadOnlyList<string> PreciseTalentLines => _selected is null ? Array.Empty<string>()
        : _selected.Item.Card.Talents.Select((t, i) => $"Talent {i + 1} : {_kb.TalentName(t)}").ToList();
    public string RowsCount => $"{Rows.Count} / {_ordered.Count} cartes";

    private void StartPrecise()
    {
        ShowTutorial = false; Message = "";
        _search = ""; Raise(nameof(Search));
        _pending.Clear(); RaisePending();
        Go(3);
        RefreshRows();
    }

    // Changements de l'importation précise, en attente de confirmation : identifiant de carte → nouveau niveau.
    private readonly Dictionary<string, int> _pending = new();
    private int EffectiveLevel(CardItem c) => _pending.TryGetValue(c.Card.Id, out var l) ? l : Data.LevelOf(c.Card.Id);
    private static string LevelName(CardItem c, int level) => level switch
    {
        PlayerCollection.NotOwned => "non possédée", 0 => "possédée, sans talent", var l => $"possédée, talent {Math.Min(l, c.Card.Talents.Count)}",
    };
    private string StatusOf(CardItem c) => LevelName(c, EffectiveLevel(c)) + (_pending.ContainsKey(c.Card.Id) ? " (à valider)" : "");

    public bool HasPending => _pending.Count > 0;
    public string PendingText => _pending.Count == 0 ? "" : $"{_pending.Count} changement(s) en attente";
    public IReadOnlyList<string> Changes { get; private set; } = Array.Empty<string>();
    public string ConfirmTitle => $"Confirmer {_pending.Count} changement(s) ?";
    private void RaisePending() { Raise(nameof(HasPending)); Raise(nameof(PendingText)); Raise(nameof(ConfirmTitle)); ReviewCommand.RaiseCanExecuteChanged(); }

    /// <summary>Affiche la liste des changements (ancien état → nouvel état) avant de les enregistrer.</summary>
    private void Review()
    {
        if (!HasPending) return;
        Changes = _pending.Where(p => _byId.ContainsKey(p.Key)).Select(p => _byId[p.Key])
            .OrderBy(c => c.Card.Order).Select(c => $"{c.Name} : {LevelName(c, Data.LevelOf(c.Card.Id))} → {LevelName(c, _pending[c.Card.Id])}").ToList();
        Raise(nameof(Changes)); Raise(nameof(ConfirmTitle));
        Go(4);
    }

    private void ApplyPending()
    {
        var n = _pending.Count;
        foreach (var p in _pending) Data.SetLevel(p.Key, p.Value);
        _pending.Clear(); RaisePending();
        Persist();
        Message = $"{n} changement(s) enregistré(s).";
        OpenMenu();
    }

    private void RefreshRows()
    {
        var keep = _selected?.Item.Card.Id;
        Rows.Clear();
        foreach (var c in _ordered.Where(c => TextSearch.ContainsAllWords(c.SearchText, _search)))
            Rows.Add(new CollectionRow(c, StatusOf(c), EffectiveLevel(c) >= 0));
        Raise(nameof(RowsCount));
        PreciseSelected = keep is null ? null : Rows.FirstOrDefault(r => r.Item.Card.Id == keep) ?? (_byId.TryGetValue(keep, out var c2) ? new CollectionRow(c2, StatusOf(c2), EffectiveLevel(c2) >= 0) : null);
    }

    private void SetPrecise(int level)
    {
        if (_selected is null) return;
        var card = _selected.Item;
        var wanted = level < 0 ? level : Math.Min(level, card.Card.Talents.Count);
        if (wanted == Data.LevelOf(card.Card.Id)) _pending.Remove(card.Card.Id); else _pending[card.Card.Id] = wanted;   // pas de changement réel : rien à confirmer
        RaisePending();
        RefreshRows();
        Message = "";
    }
}
