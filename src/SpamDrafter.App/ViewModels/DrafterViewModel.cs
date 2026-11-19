using System.Collections.ObjectModel;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.App.ViewModels;

public sealed record DrafterResult(Deck Deck, string Name, string Badge, IReadOnlyList<string> Lines);

/// <summary>Onglet Drafter : decks sauvegardés qui contiennent toutes les cartes choisies (D-017).</summary>
public sealed class DrafterViewModel : ViewModelBase
{
    private readonly KnowledgeBase _kb;
    private readonly DeckLibrary _library;
    private readonly Dictionary<string, CardItem> _byId;
    private string _message = "";

    private readonly DeckBuilderViewModel? _builder;

    /// <summary>
    /// Sans <paramref name="builder"/> : tous les decks de la bibliothèque. Avec : les decks tels que « Mes decks » du Builder les affiche
    /// (mêmes recherche, tri et filtres, partagés entre les deux onglets).
    /// </summary>
    public DrafterViewModel(CardBrowser browser, IReadOnlyList<CardItem> items, KnowledgeBase kb, DeckLibrary library, DeckBuilderViewModel? builder = null)
    {
        Browser = browser; _kb = kb; _library = library; _builder = builder;
        if (builder is not null)
            builder.SavedDecks.CollectionChanged += (_, _) =>
            {
                if (_searchPending) return;
                _searchPending = true;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { _searchPending = false; Search(); });
            };
        _byId = items.ToDictionary(i => i.Card.Id);
        AddCommand = new RelayCommand(_ => AddSelected());
        RemoveCommand = new RelayCommand(p => { if (p is WantedSlotViewModel w) { Wanted.Remove(w); Raise(nameof(WantedText)); SearchSoon(); } });
        ClearCommand = new RelayCommand(_ => { Wanted.Clear(); Raise(nameof(WantedText)); SearchSoon(); });
        OpenCommand = new RelayCommand(p => { if (p is DrafterResult r) OpenRequested?.Invoke(r.Deck); });
        library.Changed += (_, _) => Search();
        Search();
    }

    private bool _searchPending;
    /// <summary>Recherche, tri et filtres des decks (ceux du Builder) ; null hors de l'application complète.</summary>
    public DeckBuilderViewModel? Decks => _builder;
    private CollectionViewModel? _collection;
    /// <summary>Collection du joueur (phase 15) : en mode « possédés », seuls les decks entièrement possédés sont proposés.
    /// Avec le Builder, sa liste « Mes decks » est déjà filtrée.</summary>
    public CollectionViewModel? Collection
    {
        get => _collection;
        set { _collection = value; if (value is not null) value.Changed += Search; Search(); }
    }
    private bool OwnedOnly => _collection?.OwnedOnly == true;
    private IReadOnlyList<Deck> Source => _builder is null ? _library.Decks.Where(d => _collection?.ShowsDeck(d) ?? true).ToList() : _builder.SavedDecks.ToList();
    public CardBrowser Browser { get; }
    public ObservableCollection<WantedSlotViewModel> Wanted { get; } = new();
    public ObservableCollection<DrafterResult> Results { get; } = new();

    public RelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand OpenCommand { get; }

    /// <summary>Demande d'ouverture d'un deck dans le Deck builder.</summary>
    public event Action<Deck>? OpenRequested;

    public string WantedText => $"{Wanted.Count} / {Deck.MaxCards} cartes recherchées";
    public string Message { get => _message; private set => Set(ref _message, value); }

    private void AddSelected()
    {
        var item = Browser.Selected;
        if (item is null) { Message = "Choisis d'abord une carte dans la liste."; return; }
        if (Wanted.Count >= Deck.MaxCards) { Message = $"Une recherche porte sur {Deck.MaxCards} cartes au plus."; return; }
        if (Wanted.Any(w => w.Card == item)) { Message = "Cette carte fait déjà partie de la recherche."; return; }
        Wanted.Add(new WantedSlotViewModel(item, _kb, OwnedOnly ? _collection?.AllowedTalents(item) : null, SearchSoon));
        Search();
    }

    private bool _soonPending;
    /// <summary>
    /// Relance la recherche un instant plus tard (les clics sur ✕ restent instantanés) : plusieurs retraits de suite ne déclenchent qu'une seule recherche,
    /// qui reconstruit la liste des decks (le travail le plus lourd).
    /// </summary>
    private void SearchSoon()
    {
        if (_soonPending) return;
        _soonPending = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { _soonPending = false; Search(); }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void Search()
    {
        Results.Clear();
        Raise(nameof(WantedText));
        var source = Source;
        IEnumerable<DeckMatch> matches = Wanted.Count == 0
            ? source.Select(d => new DeckMatch(d, d.Slots.Count == Deck.MaxCards))      // aucune carte choisie : tous les decks
            : DeckSearch.Find(source, Wanted.Select(w => (w.Card.Card.Id, w.Talent.Id)).ToList())
                .OrderBy(m => source.ToList().FindIndex(d => d.Id == m.Deck.Id));           // l'ordre choisi dans « Mes decks »
        foreach (var m in matches)
            Results.Add(new DrafterResult(m.Deck, m.Deck.Name,
                (m.IsComplete ? "Complet" : $"Brouillon · {m.Deck.Slots.Count} carte(s)") +
                (m.Deck.Origin == DeckOrigin.Generated ? " · deck du chef" : " · créé à la main"),
                m.Deck.Slots.Select(Describe).ToList()));
        if (Wanted.Count == 0)
        {
            Message = _library.Decks.Count == 0
                ? "Aucun deck sauvegardé pour l'instant : crée-en un dans le Deck builder."
                : Results.Count == 0 && OwnedOnly ? "Aucun de tes decks n'est entièrement possédé (cartes et talents). Passe sur « Toutes les cartes et talents » pour les voir tous."
                : $"{Results.Count} deck(s). Choisis 1 à 4 cartes pour affiner.";
            return;
        }
        Message = Results.Count > 0
            ? $"{Results.Count} deck(s) trouvé(s)."
            : _library.Decks.Count == 0
                ? "Aucun deck sauvegardé pour l'instant : crée-en un dans le Deck builder."
                : OwnedOnly ? "Aucun de tes decks possédés ne contient toutes ces cartes."
                : "Aucun de tes decks sauvegardés ne contient toutes ces cartes.";
    }

    private string Describe(DeckSlot s)
    {
        var name = _byId.TryGetValue(s.CardId, out var c) ? c.Name : s.CardId;
        return s.TalentId is null ? $"{name} — aucun talent" : $"{name} — {_kb.TalentName(s.TalentId)}";
    }
}

/// <summary>
/// Une carte de la recherche du Drafter, comme dans « Deck en cours » du Builder : la carte et, dessous, le talent qu'on veut y voir
/// (« Talent indifférent » : les decks qui ont cette carte avec n'importe quel talent).
/// </summary>
public sealed class WantedSlotViewModel : ViewModelBase
{
    private TalentOption _talent;
    private readonly Action _changed;

    public WantedSlotViewModel(CardItem card, KnowledgeBase kb, IReadOnlyList<string>? allowedTalents, Action changed)
    {
        Card = card; _changed = changed;
        TalentOptions = new[] { new TalentOption(null, "Tous") }
            .Concat(card.Card.Talents.Select((t, i) => (t, i)).Where(x => allowedTalents is null || allowedTalents.Contains(x.t))
                .Select(x => new TalentOption(x.t, $"{x.i + 1}. {kb.TalentName(x.t)}"))).ToList();
        _talent = TalentOptions[0];
    }

    public CardItem Card { get; }
    public IReadOnlyList<TalentOption> TalentOptions { get; }
    public TalentOption Talent { get => _talent; set { if (value is not null && Set(ref _talent, value)) _changed(); } }
}
