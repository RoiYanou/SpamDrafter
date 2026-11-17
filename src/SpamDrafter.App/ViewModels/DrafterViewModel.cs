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
        RemoveCommand = new RelayCommand(p => { if (p is CardItem c) { Wanted.Remove(c); Search(); } });
        ClearCommand = new RelayCommand(_ => { Wanted.Clear(); Search(); });
        OpenCommand = new RelayCommand(p => { if (p is DrafterResult r) OpenRequested?.Invoke(r.Deck); });
        library.Changed += (_, _) => Search();
        Search();
    }

    private bool _searchPending;
    /// <summary>Recherche, tri et filtres des decks (ceux du Builder) ; null hors de l'application complète.</summary>
    public DeckBuilderViewModel? Decks => _builder;
    private IReadOnlyList<Deck> Source => _builder is null ? _library.Decks.ToList() : _builder.SavedDecks.ToList();
    public CardBrowser Browser { get; }
    public ObservableCollection<CardItem> Wanted { get; } = new();
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
        if (Wanted.Contains(item)) { Message = "Cette carte fait déjà partie de la recherche."; return; }
        Wanted.Add(item);
        Search();
    }

    private void Search()
    {
        Results.Clear();
        Raise(nameof(WantedText));
        var source = Source;
        IEnumerable<DeckMatch> matches = Wanted.Count == 0
            ? source.Select(d => new DeckMatch(d, d.Slots.Count == Deck.MaxCards))      // aucune carte choisie : tous les decks
            : DeckSearch.Find(source, Wanted.Select(w => w.Card.Id).ToList())
                .OrderBy(m => source.ToList().FindIndex(d => d.Id == m.Deck.Id));           // l'ordre choisi dans « Mes decks »
        foreach (var m in matches)
            Results.Add(new DrafterResult(m.Deck, m.Deck.Name,
                (m.IsComplete ? "Complet" : $"Brouillon · {m.Deck.Slots.Count} carte(s)") +
                (m.Deck.Origin == DeckOrigin.Generated ? " · généré" : " · créé à la main"),
                m.Deck.Slots.Select(Describe).ToList()));
        if (Wanted.Count == 0)
        {
            Message = _library.Decks.Count == 0
                ? "Aucun deck sauvegardé pour l'instant : crée-en un dans le Deck builder."
                : $"{Results.Count} deck(s). Choisis 1 à 4 cartes pour affiner.";
            return;
        }
        Message = Results.Count > 0
            ? $"{Results.Count} deck(s) trouvé(s)."
            : _library.Decks.Count == 0
                ? "Aucun deck sauvegardé pour l'instant : crée-en un dans le Deck builder."
                : "Aucun de tes decks sauvegardés ne contient toutes ces cartes.";
    }

    private string Describe(DeckSlot s)
    {
        var name = _byId.TryGetValue(s.CardId, out var c) ? c.Name : s.CardId;
        return s.TalentId is null ? $"{name} — aucun talent" : $"{name} — {_kb.TalentName(s.TalentId)}";
    }
}
