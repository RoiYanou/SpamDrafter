using System.Collections.ObjectModel;
using System.Globalization;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;
using SpamDrafter.Core.Settings;
using SpamDrafter.Core.Text;
using SpamDrafter.Core.Versions;

namespace SpamDrafter.App.ViewModels;

/// <summary>Une pastille de couleur de repère : sa couleur « #RRGGBB » et si c'est celle du deck ouvert.</summary>
public sealed record ColorChoice(string Color, bool IsSelected)
{
    /// <summary>Épaisseur du contour : épais (et blanc) pour la couleur choisie.</summary>
    public double Ring => IsSelected ? 3 : 1;
}

public sealed record TalentOption(string? Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Choix dans une liste de « Mes decks » (tri, origine, étiquette).</summary>
public sealed record DeckListOption(string Id, string Label)
{
    public override string ToString() => Label;
}

public sealed class SlotViewModel : ViewModelBase
{
    private TalentOption _talent;

    public SlotViewModel(CardItem card, KnowledgeBase kb, string? talentId, Action changed, bool editable = true, IReadOnlyList<string>? allowedTalents = null)
    {
        Card = card;
        IsEditable = editable;
        // Phase 15 : en mode « possédés », seuls les talents du joueur sont proposés (le talent déjà choisi reste, pour ne rien changer en silence).
        TalentOptions = new[] { new TalentOption(null, "Aucun talent") }
            .Concat(card.Card.Talents.Select((t, i) => (t, i)).Where(x => allowedTalents is null || allowedTalents.Contains(x.t) || x.t == talentId)
                .Select(x => new TalentOption(x.t, $"{x.i + 1}. {kb.TalentName(x.t)}"))).ToList();
        _talent = TalentOptions.FirstOrDefault(o => o.Id == talentId) ?? TalentOptions[0];
        Changed = changed;
    }

    private Action Changed { get; }
    public CardItem Card { get; }
    public IReadOnlyList<TalentOption> TalentOptions { get; }
    /// <summary>Faux pour un deck généré : son talent ne se change pas (la liste reste visible mais grisée).</summary>
    public bool IsEditable { get; }
    public TalentOption Talent { get => _talent; set { if (!IsEditable) { Raise(); return; } if (Set(ref _talent, value)) Changed(); } }
}

/// <summary>
/// Onglet Deck builder : 4 cartes + 1 talent par carte, sauvegarde en JSON. Phase 12, lot A : noms uniques (D-072),
/// annuler/rétablir, alerte avant de perdre des modifications, copier/coller, dupliquer, favoris, étiquettes, tri et
/// recherche dans « Mes decks ».
/// </summary>
public sealed class DeckBuilderViewModel : ViewModelBase
{
    /// <summary>État d'édition d'un deck : sert à l'annulation et à savoir s'il y a des modifications non sauvegardées.</summary>
    private sealed record EditState(string Name, IReadOnlyList<DeckSlot> Slots, string Tags, bool Favorite)
    {
        public bool SameAs(EditState o) =>
            Name == o.Name && Tags == o.Tags && Favorite == o.Favorite && Slots.SequenceEqual(o.Slots);
    }

    private static readonly EditState Empty = new("", Array.Empty<DeckSlot>(), "", false);
    private const int MaxUndo = 100;

    private readonly KnowledgeBase _kb;
    private readonly DeckLibrary _library;
    private readonly Dictionary<string, CardItem> _byId;
    private Deck _current = new();
    private Deck? _selectedSaved;
    private string _name = "";
    private string _tagsText = "";
    private bool _favorite;
    private string _status = "";
    private bool _loading;
    private EditState _last = Empty;    // dernier état connu (base de l'annulation)
    private EditState _saved = Empty;   // état de la dernière sauvegarde
    private bool _lastEditWasName;
    private readonly List<EditState> _undo = new();
    private readonly List<EditState> _redo = new();
    private Action? _pending;
    private string _confirmText = "";
    private bool _confirmCanSave;

    // Liste « Mes decks » : recherche, tri, filtres.
    private string _deckFilter = "";
    private bool _favoritesOnly;
    private bool _impactedOnly;
    private DeckListOption _sort;
    private DeckListOption _originFilter;
    private DeckListOption _tagFilter;

    public DeckBuilderViewModel(CardBrowser browser, IReadOnlyList<CardItem> items, KnowledgeBase kb, DeckLibrary library)
    {
        Browser = browser; _kb = kb; _library = library;
        _byId = items.ToDictionary(i => i.Card.Id);
        SortOptions = new DeckListOption[]
        {
            new("name", "Nom (A → Z)"), new("recent", "Modifiés récemment"), new("favorites", "Favoris d'abord"),
            new("manual", "Mon ordre (glisser-déposer)"),
        };
        OriginOptions = new DeckListOption[]
        {
            new("all", "Tous les decks"), new("manual", "Faits à la main"), new("generated", "Du chef"),
        };
        _sort = SortOptions[0];
        _originFilter = OriginOptions[0];
        TagOptions = new ObservableCollection<DeckListOption> { new("", "Tous les tags") };
        _tagFilter = TagOptions[0];

        NewCommand = new RelayCommand(_ => RequestLeave(NewDeck, "Créer un nouveau deck"));
        SaveCommand = new RelayCommand(_ => Save());
        DeleteCommand = new RelayCommand(_ => DeleteSelected(), _ => ListSelection is not null || _selectedSaved is not null);
        AddCommand = new RelayCommand(_ => AddSelectedCard());
        RemoveCommand = new RelayCommand(p =>
        {
            if (IsLocked) { Status = LockedMessage; return; }
            if (p is SlotViewModel s) { Slots.Remove(s); OnEdited(); }
        });
        UndoCommand = new RelayCommand(_ => Undo(), _ => _undo.Count > 0);
        RedoCommand = new RelayCommand(_ => Redo(), _ => _redo.Count > 0);
        DuplicateCommand = new RelayCommand(_ => Duplicate());
        ToggleFavoriteCommand = new RelayCommand(_ => ToggleFavorite());
        CopyTextCommand = new RelayCommand(_ => CopyText());
        ConfirmSaveCommand = new RelayCommand(_ => ConfirmSave());
        ConfirmDiscardCommand = new RelayCommand(_ => ConfirmDiscard());
        ConfirmCancelCommand = new RelayCommand(_ => EndConfirm());
        ClearDeckFiltersCommand = new RelayCommand(_ =>
        {
            DeckFilter = ""; FavoritesOnly = false; ImpactedOnly = false; OriginFilter = OriginOptions[0]; TagFilter = TagOptions[0];
        });
        MarkCheckedCommand = new RelayCommand(_ => MarkChecked(), _ => CanMarkChecked);
        Slots.CollectionChanged += (_, _) => { Raise(nameof(SlotsText)); RaiseStats(); RefreshVersion(); };
        library.Changed += (_, _) => RefreshDeckList();

        var startup = "";
        if (library.UnreadableFiles > 0) startup = $"{library.UnreadableFiles} fichier(s) de deck illisible(s) ignoré(s).";
        if (library.RenamedDuplicates > 0)
            startup = (startup.Length > 0 ? startup + " " : "") +
                $"{library.RenamedDuplicates} deck(s) avaient le même nom qu'un autre : « (2) » a été ajouté.";
        NewDeck();
        RefreshDeckList();
        Status = startup;
    }

    public CardBrowser Browser { get; }
    public ObservableCollection<SlotViewModel> Slots { get; } = new();
    /// <summary>Decks sauvegardés visibles (après recherche, tri et filtres).</summary>
    public ObservableCollection<Deck> SavedDecks { get; } = new();
    public ObservableCollection<string> Problems { get; } = new();
    public IReadOnlyList<DeckListOption> SortOptions { get; }
    public IReadOnlyList<DeckListOption> OriginOptions { get; }
    public ObservableCollection<DeckListOption> TagOptions { get; }

    public RelayCommand NewCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand ToggleFavoriteCommand { get; }
    public RelayCommand CopyTextCommand { get; }
    public RelayCommand ConfirmSaveCommand { get; }
    public RelayCommand ConfirmDiscardCommand { get; }
    public RelayCommand ConfirmCancelCommand { get; }
    public RelayCommand ClearDeckFiltersCommand { get; }
    public RelayCommand MarkCheckedCommand { get; }

    // ----- Version de SPAM du deck (phase 12, lot E) -----
    /// <summary>« Version de SPAM : 1.30 » ou « Créé avec SPAM 1.30 · version actuelle : 1.31 ».</summary>
    public string VersionText { get; private set; } = "";
    /// <summary>Phrase de synthèse (⚠ différences, aucune différence, ou différences indisponibles) ; vide si la version est à jour.</summary>
    public string VersionNotice { get; private set; } = "";
    public bool HasVersionNotice => VersionNotice.Length > 0;
    /// <summary>Une ligne par différence qui touche ce deck.</summary>
    public IReadOnlyList<string> VersionLines { get; private set; } = Array.Empty<string>();
    public bool HasVersionLines => VersionLines.Count > 0;
    /// <summary>Une carte ou un talent du deck n'existe plus dans la version actuelle.</summary>
    public bool VersionBroken { get; private set; }

    /// <summary>Le deck est enregistré, d'une autre version que la version actuelle, et toujours valide : le joueur peut le marquer vérifié.</summary>
    public bool CanMarkChecked => _library.Decks.Any(d => d.Id == _current.Id) && !string.IsNullOrEmpty(_current.GameVersion)
        && _current.GameVersion != _kb.Manifest.GameVersion && !VersionBroken;

    private void RefreshVersion()
    {
        var current = _kb.Manifest.GameVersion;
        var created = string.IsNullOrEmpty(_current.GameVersion) ? current : _current.GameVersion;
        VersionLines = Array.Empty<string>();
        VersionBroken = false;
        VersionNotice = "";
        if (created == current) VersionText = $"Version de SPAM : {current}";
        else
        {
            VersionText = $"Créé avec SPAM {created} · version actuelle : {current}";
            if (_kb.ChangesFrom(created) is not { } cs)
                VersionNotice = $"Les différences entre SPAM {created} et {current} ne sont pas disponibles pour ce deck.";
            else
            {
                var impact = ChangeText.Impact(cs, BuildDeck(), _kb);
                VersionLines = impact.Lines;
                VersionBroken = impact.Broken;
                VersionNotice = !impact.IsImpacted ? $"Aucune différence entre SPAM {created} et {current} ne touche ce deck."
                    : impact.Broken ? $"⚠ Ce deck n'est plus valide dans SPAM {current} : remplace les éléments supprimés."
                    : $"⚠ {impact.Lines.Count} différence(s) entre SPAM {created} et {current} touchent ce deck.";
            }
        }
        foreach (var n in new[] { nameof(VersionText), nameof(VersionNotice), nameof(HasVersionNotice), nameof(VersionLines), nameof(HasVersionLines), nameof(VersionBroken), nameof(CanMarkChecked) })
            Raise(n);
        MarkCheckedCommand?.RaiseCanExecuteChanged();
    }

    /// <summary>Le joueur a lu les différences : le deck passe à la version actuelle (enregistré tout de suite, comme le favori).</summary>
    private void MarkChecked()
    {
        if (!CanMarkChecked) return;
        if (_library.Decks.FirstOrDefault(d => d.Id == _current.Id) is not { } saved) return;
        var updated = new Deck
        {
            Id = saved.Id, Name = saved.Name, Origin = saved.Origin, IsReference = saved.IsReference,
            GameVersion = _kb.Manifest.GameVersion, Favorite = saved.Favorite, Tags = saved.Tags.ToList(),
            ModifiedUtc = saved.ModifiedUtc, Slots = saved.Slots.ToList(),
        };
        _library.Save(updated);
        _current = _library.Decks.First(d => d.Id == updated.Id);
        Status = $"Deck marqué comme vérifié pour SPAM {_kb.Manifest.GameVersion}.";
        RefreshVersion();
    }

    // ----- Compteurs du deck (phase 12, lot D) -----
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    public bool HasStats => Slots.Count > 0;
    public string ElementsText { get; private set; } = "";
    /// <summary>Éléments du deck et résistances moyennes, avec leurs icônes (l'écran) ; les textes ci-dessus disent la même chose.</summary>
    /// <summary>Résistances moyennes, présentées comme sur la fiche d'une carte : grosse icône de l'élément, valeur dessous.</summary>
    public IReadOnlyList<ResistanceItem> ResistanceItems { get; private set; } = Array.Empty<ResistanceItem>();
    public string TotalsText { get; private set; } = "";
    /// <summary>PV totaux et revenu total en Spamtons, présentés comme sur la fiche d'une carte (♥, puis l'icône du Spamton avec le nombre dessus).</summary>
    public string HpTotalShort { get; private set; } = "";
    public string IncomeTotalShort { get; private set; } = "";
    public Avalonia.Media.Imaging.Bitmap? SpamtonIcon => Icons.Spamton(_kb);
    public string ResistancesText { get; private set; } = "";

    /// <summary>Recalcule les compteurs (instantané : lus dans les données des cartes, aucune simulation).</summary>
    private void RaiseStats()
    {
        var stats = DeckStats.Compute(Slots.Select(s => s.Card.Card), _kb);
        string El(string id) => _kb.ElementName(id);
        ElementsText = "Éléments : " + string.Join(" · ", stats.Elements.Select(e => $"{El(e.ElementId)} ×{e.Count}"));
        ResistanceItems = stats.Resistances.Select(r => new ResistanceItem(Icons.Element(_kb, r.ElementId, 64), El(r.ElementId), $"{r.Average.ToString("0.#", Fr)} %", AbilityRichText.ElementBrush(r.ElementId))).ToList();
        HpTotalShort = $"{stats.TotalHp.ToString("N0", Fr)} PV"; IncomeTotalShort = stats.TotalIncome.ToString("N0", Fr);
        TotalsText = $"PV totaux : {stats.TotalHp.ToString("N0", Fr)} · Revenu total : {stats.TotalIncome.ToString("N0", Fr)}";
        ResistancesText = "Résistances moyennes : " + string.Join(" · ", stats.Resistances.Select(r => $"{El(r.ElementId)} {r.Average.ToString("0.#", Fr)} %"));
        foreach (var n in new[] { nameof(HasStats), nameof(ElementsText), nameof(ResistanceItems), nameof(TotalsText), nameof(HpTotalShort), nameof(IncomeTotalShort), nameof(SpamtonIcon), nameof(ResistancesText) }) Raise(n);
    }

    /// <summary>Demande à la fenêtre de placer ce texte dans le presse-papiers.</summary>
    public event Action<string>? ClipboardWriteRequested;

    public string SlotsText => $"{Slots.Count} / {Deck.MaxCards} cartes";
    public string Name { get => _name; set { if (Set(ref _name, value)) OnEdited(nameEdit: true); } }
    public string Status { get => _status; private set => Set(ref _status, value); }

    /// <summary>Tags du deck en cours, séparées par des virgules.</summary>
    public string TagsText { get => _tagsText; set { if (Set(ref _tagsText, value)) OnEdited(); } }
    public bool Favorite { get => _favorite; private set { if (Set(ref _favorite, value)) Raise(nameof(FavoriteLabel)); } }
    public string FavoriteLabel => _favorite ? "★ Favori" : "☆ Favori";

    /// <summary>Le deck en cours a des modifications que la sauvegarde n'a pas encore enregistrées.</summary>
    public bool IsDirty => !Capture().SameAs(_saved);
    public string DirtyText => IsDirty ? "● modifications non sauvegardées" : "";

    // ----- Alerte avant de perdre des modifications -----
    public bool ConfirmVisible => _pending is not null;
    public string ConfirmText { get => _confirmText; private set => Set(ref _confirmText, value); }
    public bool ConfirmCanSave { get => _confirmCanSave; private set => Set(ref _confirmCanSave, value); }

    // ----- Liste « Mes decks » -----
    // Liste « Mes decks » : uniquement les noms. Un clic sur un nom le présélectionne (un seul à la fois) et montre ses cartes juste dessous,
    // sans l'ouvrir ; un second clic le désélectionne ; le bouton « Ouvrir » ou un clic droit l'ouvre dans « Deck en cours ».
    // La présélection est retenue par identifiant : elle survit à un changement de décor ou à tout rafraîchissement de la liste.
    private Guid? _previewId;
    private bool _refreshing;
    public Deck? ListSelection
    {
        get => _previewId is { } id ? SavedDecks.FirstOrDefault(d => d.Id == id) : null;
        set
        {
            if (_refreshing) return;   // la liste se reconstruit : ce n'est pas un choix du joueur
            if (_previewId == value?.Id) return;
            _previewId = value?.Id; Raise(); DeleteCommand.RaiseCanExecuteChanged();
        }
    }
    private RelayCommand? _openDeck;
    /// <summary>Ouvre un deck de la liste dans la zone « Deck en cours » (avec la confirmation habituelle s'il y a des modifications en cours).</summary>
    public RelayCommand OpenDeckCommand => _openDeck ??= new RelayCommand(p => { if (p is Deck d) SelectedSaved = d; });

    /// <summary>Titre du menu « Tri et filtres » : indique quand un filtre (ou la recherche) est actif.</summary>
    public bool DeckFiltersActive => TextSearch.Normalize(_deckFilter).Length > 0 || _favoritesOnly || _impactedOnly
                                     || !ReferenceEquals(_originFilter, OriginOptions[0]) || _tagFilter.Id.Length > 0;
    /// <summary>Nombre de filtres actifs (recherche, favoris, decks touchés, origine, tag).</summary>
    public int DeckFiltersCount => (TextSearch.Normalize(_deckFilter).Length > 0 ? 1 : 0) + (_favoritesOnly ? 1 : 0) + (_impactedOnly ? 1 : 0)
                                   + (ReferenceEquals(_originFilter, OriginOptions[0]) ? 0 : 1) + (_tagFilter.Id.Length > 0 ? 1 : 0);
    /// <summary>« ● filtre actif » ou « ● filtres actifs » selon le nombre de filtres actifs.</summary>
    public string DeckFiltersLabel => DeckFiltersCount > 1 ? "● filtres actifs" : "● filtre actif";
    public string DeckFiltersHeader => DeckFiltersActive ? "Tri et filtres  " + DeckFiltersLabel : "Tri et filtres";
    public string DeckFilter { get => _deckFilter; set { if (Set(ref _deckFilter, value)) RefreshDeckList(); } }
    public bool FavoritesOnly { get => _favoritesOnly; set { if (Set(ref _favoritesOnly, value)) RefreshDeckList(); } }
    /// <summary>Ne montre que les decks touchés par une mise à jour de SPAM (lot E).</summary>
    public bool ImpactedOnly { get => _impactedOnly; set { if (Set(ref _impactedOnly, value)) RefreshDeckList(); } }
    public int ImpactedDeckCount => _library.Decks.Count(d => d.VersionImpacted);
    /// <summary>Au moins un deck est modifié par une mise à jour de SPAM : sinon le filtre n'est pas affiché.</summary>
    public bool HasImpactedDecks => ImpactedDeckCount > 0;
    public DeckListOption Sort { get => _sort; set { if (value is not null && Set(ref _sort, value)) RefreshDeckList(); } }
    public DeckListOption OriginFilter { get => _originFilter; set { if (value is not null && Set(ref _originFilter, value)) RefreshDeckList(); } }
    public DeckListOption TagFilter { get => _tagFilter; set { if (value is not null && Set(ref _tagFilter, value)) RefreshDeckList(); } }
    public string DeckCountText => SavedDecks.Count == _library.Decks.Count
        ? $"{_library.Decks.Count} deck(s)" : $"{SavedDecks.Count} / {_library.Decks.Count} deck(s)";

    public Deck? SelectedSaved
    {
        get => _selectedSaved;
        set
        {
            if (ReferenceEquals(_selectedSaved, value)) return;
            if (value is null) { _selectedSaved = null; Raise(); DeleteCommand.RaiseCanExecuteChanged(); return; }
            if (IsDirty && value.Id != _current.Id)
            {
                // Modifications en cours : on garde la sélection actuelle et on demande confirmation.
                Raise();
                RequestLeave(() => OpenDeck(value), $"Ouvrir « {value.Name} »");
                return;
            }
            OpenDeck(value);
        }
    }

    // ----- Deck généré : composition verrouillée -----
    /// <summary>Un deck venu du générateur ne se modifie pas (cartes et talents) ; son nom, ses étiquettes et son favori restent libres.</summary>
    public bool IsLocked => _current.Origin == DeckOrigin.Generated;
    public bool CanEditCards => !IsLocked;
    public string LockedNotice => "Deck du chef : ses cartes et ses talents sont verrouillés. Tu peux le renommer, lui ajouter des tags, " +
                                   "le mettre en favori, ou le dupliquer pour en faire une copie modifiable.";
    private const string LockedMessage = "Ce deck est un deck du chef : ses cartes et ses talents ne se modifient pas. " +
                                         "Utilise « Dupliquer » pour en faire une copie modifiable.";

    private SlotViewModel MakeSlot(CardItem card, string? talentId) =>
        new(card, _kb, talentId, () => OnEdited(), editable: !IsLocked, allowedTalents: _collection?.OwnedOnly == true ? _collection.AllowedTalents(card) : null);

    private CollectionViewModel? _collection;
    /// <summary>Collection du joueur (phase 15) : en mode « possédés », « Mes decks » ne montre que les decks entièrement possédés.</summary>
    public CollectionViewModel? Collection
    {
        get => _collection;
        set { _collection = value; if (value is not null) value.Changed += () => { RefreshDeckList(); Raise(nameof(DeckCountText)); }; RefreshDeckList(); }
    }

    // ----- Ouverture, création -----
    private void NewDeck()
    {
        _loading = true;
        _current = new Deck { GameVersion = _kb.Manifest.GameVersion };
        Slots.Clear();
        _tagsText = ""; Raise(nameof(TagsText));
        Favorite = false;
        _errorsShown = false;
        _name = "Nouveau deck"; Raise(nameof(Name));
        _loading = false;
        SetSelected(null);
        ResetHistory(saved: true);
        Status = "";
        Revalidate();
        RefreshVersion();
    }

    /// <summary>Ouvre un deck sauvegardé (aussi appelé par le Drafter, via <see cref="RequestOpen"/>).</summary>
    private bool _generatedInfo;
    /// <summary>Pop-up d'information, affiché à chaque ouverture d'un deck généré ; le joueur peut le fermer.</summary>
    public bool GeneratedInfoVisible { get => _generatedInfo; set => Set(ref _generatedInfo, value); }
    private RelayCommand? _closeGeneratedInfo;
    public RelayCommand CloseGeneratedInfoCommand => _closeGeneratedInfo ??= new RelayCommand(_ => GeneratedInfoVisible = false);

    public void OpenDeck(Deck d)
    {
        GeneratedInfoVisible = d.Origin == DeckOrigin.Generated;
        _errorsShown = false;
        _loading = true;
        _current = d;
        Slots.Clear();
        foreach (var s in d.Slots)
            if (_byId.TryGetValue(s.CardId, out var item))
                Slots.Add(MakeSlot(item, s.TalentId));
        _name = d.Name; Raise(nameof(Name));
        _tagsText = string.Join(", ", d.Tags); Raise(nameof(TagsText));
        Favorite = d.Favorite;
        _loading = false;
        RaiseStats();
        SetSelected(_library.Decks.FirstOrDefault(x => x.Id == d.Id) ?? d);
        ResetHistory(saved: true);
        Status = "";
        Revalidate();
        RefreshVersion();
    }

    /// <summary>Ouverture demandée depuis un autre onglet : demande d'abord confirmation s'il y a des modifications.</summary>
    public void RequestOpen(Deck d) => RequestLeave(() => OpenDeck(d), $"Ouvrir « {d.Name} »");

    private void SetSelected(Deck? d)
    {
        _selectedSaved = d;
        Raise(nameof(IsLocked)); Raise(nameof(CanEditCards));
        Raise(nameof(SelectedSaved));
        DeleteCommand.RaiseCanExecuteChanged();
    }

    // ----- Ajout / retrait de cartes -----
    private void AddSelectedCard()
    {
        if (IsLocked) { Status = LockedMessage; return; }
        var item = Browser.Selected;
        if (item is null) { Status = "Choisis d'abord une carte dans la liste."; return; }
        if (Slots.Count >= Deck.MaxCards) { Status = $"Le deck contient déjà {Deck.MaxCards} cartes."; return; }
        if (Slots.Any(s => s.Card.Card.Id == item.Card.Id)) { Status = "Cette carte est déjà dans le deck."; return; }
        Slots.Add(MakeSlot(item, (_collection?.AllowedTalents(item) ?? item.Card.Talents).FirstOrDefault()));   // talent 1 par défaut (celui du joueur en mode « possédés »)
        Status = "";
        OnEdited();
    }

    // ----- Historique (annuler / rétablir) et modifications non sauvegardées -----
    private EditState Capture() => new(Name.Trim(),
        Slots.Select(s => new DeckSlot(s.Card.Card.Id, s.Talent.Id)).ToList(), string.Join(", ", ParseTags(_tagsText)), _favorite);

    private static List<string> ParseTags(string text) =>
        text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .DistinctBy(TextSearch.Normalize).ToList();

    private void RaiseDirty() { Raise(nameof(IsDirty)); Raise(nameof(DirtyText)); }

    private void RaiseUndoRedo() { UndoCommand.RaiseCanExecuteChanged(); RedoCommand.RaiseCanExecuteChanged(); }

    private void ResetHistory(bool saved)
    {
        _undo.Clear(); _redo.Clear();
        _last = Capture();
        if (saved) _saved = _last;
        _lastEditWasName = false;
        RaiseUndoRedo();
        RaiseDirty();
    }

    /// <summary>À appeler après chaque modification : met à jour l'historique, les problèmes et l'indicateur.</summary>
    private void OnEdited(bool nameEdit = false)
    {
        if (_loading) return;
        var now = Capture();
        if (!now.SameAs(_last))
        {
            // Plusieurs frappes de suite dans le nom comptent pour une seule étape d'annulation.
            if (!(nameEdit && _lastEditWasName))
            {
                _undo.Add(_last);
                if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
            }
            _redo.Clear();
            _last = now;
            _lastEditWasName = nameEdit;
            RaiseUndoRedo();
        }
        Revalidate();
        RaiseDirty();
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Add(Capture());
        var s = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Apply(s);
    }

    private void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Add(Capture());
        var s = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Apply(s);
    }

    private void Apply(EditState s)
    {
        _loading = true;
        Slots.Clear();
        foreach (var slot in s.Slots)
            if (_byId.TryGetValue(slot.CardId, out var item))
                Slots.Add(MakeSlot(item, slot.TalentId));
        _name = s.Name; Raise(nameof(Name));
        _tagsText = s.Tags; Raise(nameof(TagsText));
        Favorite = s.Favorite;
        _loading = false;
        _last = Capture();
        _lastEditWasName = false;
        RaiseUndoRedo();
        Revalidate();
        RaiseDirty();
    }

    // ----- Alerte « modifications non sauvegardées » -----
    /// <summary>Exécute <paramref name="go"/> tout de suite, ou après confirmation s'il y a des modifications non sauvegardées.</summary>
    public void RequestLeave(Action go, string what)
    {
        if (!IsDirty) { go(); return; }
        _pending = go;
        ConfirmText = $"Le deck « {(_name.Trim().Length > 0 ? _name.Trim() : "sans nom")} » a des modifications non sauvegardées. " +
                      $"{what} les perdra.";
        ConfirmCanSave = DeckValidator.Validate(BuildDeck(), _kb, requireComplete: !AllowIncompleteDecks).Count == 0 && !NameTaken();
        Raise(nameof(ConfirmVisible));
    }

    private void ConfirmSave()
    {
        var go = _pending;
        if (go is null) return;
        if (!Save()) return;   // sauvegarde refusée : on reste là, le message est affiché
        EndConfirm();
        go();
    }

    private void ConfirmDiscard()
    {
        var go = _pending;
        EndConfirm();
        go?.Invoke();
    }

    private void EndConfirm() { _pending = null; Raise(nameof(ConfirmVisible)); }

    // ----- Validation et sauvegarde -----
    private bool NameTaken() => _library.NameTaken(Name.Trim(), _current.Id);

    /// <summary>Copie du deck tel qu'il est en cours d'édition (le deck de la liste n'est jamais modifié avant la sauvegarde).</summary>
    private Deck BuildDeck() => new()
    {
        Id = _current.Id,
        Name = Name.Trim(),
        Origin = _current.Origin,
        IsReference = _current.IsReference,
        // La version de SPAM de création est conservée (phase 12, lot E) : elle ne change pas quand on modifie le deck.
        GameVersion = string.IsNullOrEmpty(_current.GameVersion) ? _kb.Manifest.GameVersion : _current.GameVersion,
        Favorite = _favorite,
        Tags = ParseTags(_tagsText),
        ModifiedUtc = _current.ModifiedUtc,
        Slots = Slots.Select(s => new DeckSlot(s.Card.Card.Id, s.Talent.Id)).ToList(),
    };

    private bool _errorsShown;
    /// <summary>« 2 erreurs détectées » : affiché sur « Détails et repères » seulement après un essai de sauvegarde refusé.</summary>
    public string ErrorSummary => !_errorsShown || Problems.Count == 0 ? ""
        : Problems.Count == 1 ? "● 1 erreur détectée" : $"● {Problems.Count} erreurs détectées";
    public bool HasErrorSummary => ErrorSummary.Length > 0;
    private void ShowErrors() { _errorsShown = true; Revalidate(); }

    private void Revalidate()
    {
        Problems.Clear();
        foreach (var p in DeckValidator.Validate(BuildDeck(), _kb)) Problems.Add(p);
        if (Name.Trim().Length > 0 && NameTaken())
            Problems.Add("Un autre deck porte déjà ce nom (les accents et les majuscules ne comptent pas).");
        Raise(nameof(ErrorSummary)); Raise(nameof(HasErrorSummary));
    }

    /// <summary>Interrupteur réservé aux essais automatiques : par défaut, un deck de moins de 4 cartes ne peut pas être sauvegardé.</summary>
    public bool AllowIncompleteDecks { get; set; }

    private bool Save()
    {
        var deck = BuildDeck();
        // Un deck contient toujours 4 cartes : plus de brouillon incomplet.
        if (Slots.Count < Deck.MaxCards && !AllowIncompleteDecks)
        {
            Status = $"Sauvegarde impossible : un deck doit contenir {Deck.MaxCards} cartes ({Slots.Count} choisie{(Slots.Count > 1 ? "s" : "")}).";
            ShowErrors();
            ValidationFailed?.Invoke();
            return false;
        }
        var blocking = DeckValidator.Validate(deck, _kb, requireComplete: !AllowIncompleteDecks);
        if (blocking.Count > 0) { Status = "Sauvegarde impossible : " + blocking[0]; ShowErrors(); ValidationFailed?.Invoke(); return false; }
        if (NameTaken())
        {
            Status = $"Sauvegarde impossible : un autre deck s'appelle déjà « {deck.Name} ». Choisis un autre nom.";
            ShowErrors();
            ValidationFailed?.Invoke();
            return false;
        }
        _errorsShown = false;
        _library.Save(deck);
        _current = _library.Decks.FirstOrDefault(d => d.Id == deck.Id) ?? deck;
        SetSelected(_library.Decks.FirstOrDefault(d => d.Id == deck.Id) is { } shown && SavedDecks.Contains(shown) ? shown : null);
        ResetHistory(saved: true);
        Status = Slots.Count == Deck.MaxCards ? "Deck sauvegardé." : "Brouillon sauvegardé (deck incomplet).";
        RefreshVersion();
        return true;
    }

    /// <summary>Le deck dont la suppression attend la confirmation du joueur (fenêtre qui nomme le deck).</summary>
    private Deck? _deleteTarget;
    public bool DeleteConfirmVisible => _deleteTarget is not null;
    public string DeleteConfirmText => _deleteTarget is null ? "" : $"Supprimer le deck « {_deleteTarget.Name} » ?";
    public IReadOnlyList<DeckChip> DeleteConfirmChips => _deleteTarget is null ? Array.Empty<DeckChip>() : DeckChipsConverter.Build(_deleteTarget.Slots);
    public string DeleteConfirmDetail => _deleteTarget is null ? "" : $"{_deleteTarget.Slots.Count} carte(s). Cette action est définitive.";
    public RelayCommand DeleteConfirmCommand => _deleteConfirm ??= new RelayCommand(_ => DoDelete());
    public RelayCommand DeleteCancelCommand => _deleteCancel ??= new RelayCommand(_ => EndDelete());
    private RelayCommand? _deleteConfirm, _deleteCancel;

    private void DeleteSelected()
    {
        // Le bouton « Supprimer » vise le deck présélectionné dans « Mes decks » ; sans présélection, le deck ouvert.
        _deleteTarget = ListSelection ?? _selectedSaved;
        if (_deleteTarget is null) return;
        RaiseDeleteConfirm();
    }

    private void DoDelete()
    {
        if (_deleteTarget is not { } d) return;
        EndDelete();
        _library.Delete(d.Id);
        if (_previewId == d.Id) { _previewId = null; Raise(nameof(ListSelection)); }
        if (d.Id == _current.Id) NewDeck();   // le deck ouvert disparaît : on repart d'un deck neuf ; sinon l'éditeur n'est pas touché
        DeleteCommand.RaiseCanExecuteChanged();
        Status = $"Deck « {d.Name} » supprimé.";
    }

    private void EndDelete() { _deleteTarget = null; RaiseDeleteConfirm(); }
    private void RaiseDeleteConfirm()
    {
        Raise(nameof(DeleteConfirmVisible)); Raise(nameof(DeleteConfirmText)); Raise(nameof(DeleteConfirmDetail)); Raise(nameof(DeleteConfirmChips));
    }

    // ----- Dupliquer, favori, copier / coller -----
    /// <summary>Enregistre le deck en cours (modifications comprises) sous un nouveau nom et l'ouvre ; l'original reste tel qu'il était.</summary>
    private void Duplicate()
    {
        var copy = BuildDeck();
        var blocking = DeckValidator.Validate(copy, _kb, requireComplete: false);
        if (blocking.Count > 0) { Status = "Duplication impossible : " + blocking[0]; return; }
        copy.Id = Guid.NewGuid();
        copy.Name = DeckNames.MakeUnique(_library.Decks, (copy.Name.Length > 0 ? copy.Name : "Deck") + " (copie)", copy.Id);
        copy.Favorite = false;
        copy.Origin = DeckOrigin.Manual;   // la copie d'un deck généré est un deck à soi, modifiable
        _library.Save(copy);
        OpenDeck(_library.Decks.First(d => d.Id == copy.Id));
        Status = $"Copie « {copy.Name} » créée et ouverte.";
    }

    private void ToggleFavorite()
    {
        Favorite = !_favorite;
        if (_library.Decks.FirstOrDefault(d => d.Id == _current.Id) is { } saved)
        {
            // Deck déjà sauvegardé : le favori s'enregistre tout de suite, sans toucher à ses autres modifications en cours.
            var onDisk = new Deck
            {
                Id = saved.Id, Name = saved.Name, Origin = saved.Origin, IsReference = saved.IsReference,
                GameVersion = saved.GameVersion, Favorite = _favorite, Tags = saved.Tags.ToList(),
                ModifiedUtc = saved.ModifiedUtc, Slots = saved.Slots.ToList(),
            };
            _library.Save(onDisk);
            _current = _library.Decks.First(d => d.Id == onDisk.Id);
            _saved = _saved with { Favorite = _favorite };
            _last = _last with { Favorite = _favorite };
        }
        RaiseDirty();
    }

    /// <summary>Texte du deck en cours, à partager (voir <see cref="DeckText"/>).</summary>
    public string ExportText() => DeckText.Format(BuildDeck(), _kb);

    private void CopyText()
    {
        ClipboardWriteRequested?.Invoke(ExportText());
        Status = "Deck copié dans le presse-papiers.";
    }

    /// <summary>Ouvre un deck collé comme nouveau deck non sauvegardé. Renvoie le message à afficher.</summary>
    public string ImportText(string text)
    {
        var result = DeckText.Parse(text, _kb);
        if (!result.Ok)
        {
            Status = "Collage impossible : " + string.Join(" ", result.Problems);
            return Status;
        }
        var deck = result.Deck!;
        void Go()
        {
            NewDeck();
            _loading = true;
            foreach (var s in deck.Slots)
                Slots.Add(MakeSlot(_byId[s.CardId], s.TalentId));
            _name = string.IsNullOrWhiteSpace(deck.Name) ? "Deck collé" : deck.Name; Raise(nameof(Name));
            _loading = false;
            ResetHistory(saved: false);
            _saved = Empty;   // pas encore sauvegardé
            Revalidate();
            RaiseDirty();
            Status = $"Deck collé : {Slots.Count} carte(s). Clique sur « Sauvegarder » pour le garder.";
        }
        RequestLeave(Go, "Coller un deck");
        return Status;
    }

    /// <summary>Reprend le tri et les filtres de la dernière fois.</summary>
    private AppSettings? _settings;
    /// <summary>Déclenché quand un réglage lié aux decks change (ordre, couleur, emoji) : l'application l'enregistre.</summary>
    public event Action? SettingsChanged;
    /// <summary>Déclenché quand une sauvegarde est refusée : la vue secoue le champ du nom et joue le son d'erreur.</summary>
    public event Action? ValidationFailed;

    private int ManualRank(Guid id)
    {
        var i = _settings?.DeckManualOrder.IndexOf(id.ToString()) ?? -1;
        return i < 0 ? int.MaxValue : i;
    }

    /// <summary>Glisser-déposer : met le deck <paramref name="dragged"/> à la place de <paramref name="target"/> et passe au tri « Mon ordre ».</summary>
    public void MoveDeck(Guid dragged, Guid target)
    {
        if (_settings is null || dragged == target) return;
        var order = SavedDecks.Select(d => d.Id.ToString()).ToList();
        foreach (var d in _library.Decks.Where(d => !order.Contains(d.Id.ToString()))) order.Add(d.Id.ToString());
        if (!order.Remove(dragged.ToString())) return;
        var at = order.IndexOf(target.ToString());
        order.Insert(at < 0 ? order.Count : at, dragged.ToString());
        _settings.DeckManualOrder = order;
        _sort = SortOptions.First(o => o.Id == "manual");
        Raise(nameof(Sort));
        RefreshDeckList();
        SettingsChanged?.Invoke();
    }

    /// <summary>Flèches ▲ ▼ : fait monter ou descendre un deck d'un cran dans « Mes decks » (passe au tri « Mon ordre »).</summary>
    public void MoveDeckBy(Guid id, int delta)
    {
        if (_settings is null) return;
        var order = SavedDecks.Select(d => d.Id.ToString()).ToList();
        foreach (var d in _library.Decks.Where(d => !order.Contains(d.Id.ToString()))) order.Add(d.Id.ToString());
        var i = order.IndexOf(id.ToString()); var j = i + delta;
        if (i < 0 || j < 0 || j >= order.Count) return;
        (order[i], order[j]) = (order[j], order[i]);
        _settings.DeckManualOrder = order;
        _sort = SortOptions.First(o => o.Id == "manual");
        Raise(nameof(Sort));
        RefreshDeckList();
        SettingsChanged?.Invoke();
    }
    public RelayCommand MoveUpCommand => _moveUp ??= new RelayCommand(p => { if (p is Deck d) MoveDeckBy(d.Id, -1); });
    public RelayCommand MoveDownCommand => _moveDown ??= new RelayCommand(p => { if (p is Deck d) MoveDeckBy(d.Id, 1); });
    private RelayCommand? _moveUp, _moveDown;

    public IReadOnlyList<string> DeckColors => DeckDecor.Colors;
    /// <summary>Les pastilles de couleur, avec la couleur du deck ouvert marquée.</summary>
    public IReadOnlyList<ColorChoice> DeckColorChoices =>
        DeckDecor.Colors.Select(c => new ColorChoice(c, string.Equals(c, DeckDecor.StyleOf(_current.Id)?.Color, StringComparison.OrdinalIgnoreCase))).ToList();
    /// <summary>Couleur de repère du deck ouvert (transparente s'il n'en a pas) : une barre à côté de son nom.</summary>
    public Avalonia.Media.IBrush DeckColorBrush =>
        DeckDecor.StyleOf(_current.Id)?.Color is { } c && Avalonia.Media.Color.TryParse(c, out var col)
            ? new Avalonia.Media.SolidColorBrush(col) : Avalonia.Media.Brushes.Transparent;
    /// <summary>Emojis proposés pour décorer un deck (le joueur en choisit un, ou aucun).</summary>
    public IReadOnlyList<string> DeckEmojis => DeckDecor.Emojis;
    /// <summary>Emoji du deck ouvert (un ou deux caractères).</summary>
    public string DeckEmoji
    {
        get => DeckDecor.StyleOf(_current.Id)?.Emoji ?? "";
        set { UpdateStyle(s => s.Emoji = string.IsNullOrWhiteSpace(value) ? null : value.Trim()); Raise(nameof(DeckEmoji)); }
    }
    /// <summary>Choisit la couleur du deck ouvert (paramètre « #RRGGBB », ou vide pour l'enlever).</summary>
    public RelayCommand SetDeckColorCommand => _setColor ??= new RelayCommand(p => UpdateStyle(s => s.Color = p is string { Length: > 0 } c ? c : null));
    private RelayCommand? _setColor;

    private void UpdateStyle(Action<DeckStyle> change)
    {
        if (_settings is null) return;
        var key = _current.Id.ToString();
        var style = _settings.DeckStyles.TryGetValue(key, out var s) ? s : new DeckStyle();
        change(style);
        if (style.Color is null && style.Emoji is null) _settings.DeckStyles.Remove(key); else _settings.DeckStyles[key] = style;
        RaiseDecor();
        RefreshDeckList();
        SettingsChanged?.Invoke();
    }

    /// <summary>Les cartes du deck en cours, en pastilles (mode « Présenter »).</summary>
    public IReadOnlyList<DeckChip> PresentationChips() =>
        DeckChipsConverter.Build(Slots.Select(s => new DeckSlot(s.Card.Card.Id, s.Talent.Id)));
    public string PresentationTitle => Name.Trim().Length > 0 ? Name.Trim() : "Mon deck";

    public void ApplySettings(AppSettings s)
    {
        _settings = s;
        DeckDecor.Settings = s;
        RaiseDecor();
        _sort = SortOptions.FirstOrDefault(o => o.Id == s.DeckSort) ?? SortOptions[0];
        _originFilter = OriginOptions.FirstOrDefault(o => o.Id == s.DeckOrigin) ?? OriginOptions[0];
        _favoritesOnly = s.FavoritesOnly;
        Raise(nameof(Sort)); Raise(nameof(OriginFilter)); Raise(nameof(FavoritesOnly));
        RefreshDeckList();
    }

    public void CaptureSettings(AppSettings s)
    {
        s.DeckSort = _sort.Id; s.DeckOrigin = _originFilter.Id; s.FavoritesOnly = _favoritesOnly;
    }

    /// <summary>Sauvegarde de tous les decks (bouton « Sauvegarder mes decks… »).</summary>
    public string BackupDecks(string zipPath)
    {
        try { Status = $"{_library.BackupTo(zipPath)} deck(s) sauvegardé(s) dans {Path.GetFileName(zipPath)}."; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Status = "Sauvegarde impossible : " + e.Message; }
        return Status;
    }

    /// <summary>Restauration d'une sauvegarde (bouton « Restaurer des decks… ») : rien n'est supprimé.</summary>
    public string RestoreDecks(string zipPath)
    {
        try
        {
            var r = _library.RestoreFrom(zipPath);
            Status = r.Problems.Count > 0 && r.Added + r.Updated == 0
                ? r.Problems[0]
                : $"Restauration : {r.Added} deck(s) ajouté(s), {r.Updated} mis à jour, {r.Skipped} déjà à jour" +
                  (r.Problems.Count > 0 ? $", {r.Problems.Count} fichier(s) ignoré(s)." : ".");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Status = "Restauration impossible : " + e.Message; }
        return Status;
    }

    // ----- Liste « Mes decks » -----
    private void RefreshDeckList()
    {
        var keepTag = _tagFilter.Id;
        var tags = _library.Decks.SelectMany(d => d.Tags).DistinctBy(TextSearch.Normalize)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (!TagOptions.Skip(1).Select(o => o.Id).SequenceEqual(tags))
        {
            TagOptions.Clear();
            TagOptions.Add(new DeckListOption("", "Tous les tags"));
            foreach (var t in tags) TagOptions.Add(new DeckListOption(t, "# " + t));
            _tagFilter = TagOptions.FirstOrDefault(o => o.Id == keepTag) ?? TagOptions[0];
            Raise(nameof(TagFilter));
        }

        // Decks touchés par une mise à jour de SPAM : calculé ici pour le badge ⚠ et le filtre.
        foreach (var d in _library.Decks) d.VersionImpacted = ChangeText.ImpactOf(d, _kb).IsImpacted;
        Raise(nameof(ImpactedDeckCount)); Raise(nameof(HasImpactedDecks));
        // Plus aucun deck concerné (par exemple après « marquer vérifié ») : le filtre disparaît, il ne doit pas rester actif.
        if (_impactedOnly && !HasImpactedDecks) { _impactedOnly = false; Raise(nameof(ImpactedOnly)); }
        IEnumerable<Deck> q = _library.Decks;
        if (_collection is { OwnedOnly: true }) q = q.Where(_collection.ShowsDeck);
        if (_favoritesOnly) q = q.Where(d => d.Favorite);
        if (_impactedOnly) q = q.Where(d => d.VersionImpacted);
        if (_originFilter.Id == "manual") q = q.Where(d => d.Origin == DeckOrigin.Manual);
        else if (_originFilter.Id == "generated") q = q.Where(d => d.Origin == DeckOrigin.Generated);
        if (_tagFilter.Id.Length > 0)
            q = q.Where(d => d.Tags.Any(t => TextSearch.Normalize(t) == TextSearch.Normalize(_tagFilter.Id)));
        if (TextSearch.Normalize(_deckFilter).Length > 0)
            q = q.Where(d => TextSearch.ContainsAllWords(
                TextSearch.Normalize(d.Name + " " + string.Join(" ", d.Tags) + " " + string.Join(" ", d.Slots.Select(SlotText))),
                _deckFilter));
        q = _sort.Id switch
        {
            "recent" => q.OrderByDescending(d => d.ModifiedUtc ?? DateTime.MinValue)
                .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase),
            "favorites" => q.OrderByDescending(d => d.Favorite)
                .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase),
            "manual" => q.OrderBy(d => ManualRank(d.Id)).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => q.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        var list = q.ToList();
        _refreshing = true;
        try { SavedDecks.Clear(); foreach (var d in list) SavedDecks.Add(d); }
        finally { _refreshing = false; }
        Raise(nameof(ListSelection)); Raise(nameof(DeckFiltersActive)); Raise(nameof(DeckFiltersCount)); Raise(nameof(DeckFiltersLabel)); Raise(nameof(DeckFiltersHeader));
        // La sélection suit le deck ouvert (l'objet a été rechargé depuis le disque).
        _selectedSaved = SavedDecks.FirstOrDefault(d => d.Id == _current.Id);
        Raise(nameof(SelectedSaved));
        DeleteCommand.RaiseCanExecuteChanged();
        Raise(nameof(DeckCountText));
    }

    /// <summary>Affiche un message dans la zone d'état du deck.</summary>
    public void Notify(string text) => Status = text;

    private void RaiseDecor() { Raise(nameof(DeckEmoji)); Raise(nameof(DeckColorBrush)); Raise(nameof(DeckColorChoices)); }

    /// <summary>Carte et talent d'une place : la recherche dans « Mes decks » trouve aussi par carte ou talent.</summary>
    private string SlotText(DeckSlot s) =>
        (_byId.TryGetValue(s.CardId, out var c) ? c.Name : s.CardId) + (s.TalentId is null ? "" : " " + _kb.TalentName(s.TalentId));
}
