using System.Collections.ObjectModel;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.App.ViewModels;

public sealed record TalentOption(string? Id, string Label)
{
    public override string ToString() => Label;
}

public sealed class SlotViewModel : ViewModelBase
{
    private TalentOption _talent;

    public SlotViewModel(CardItem card, KnowledgeBase kb, string? talentId, Action changed)
    {
        Card = card;
        TalentOptions = new[] { new TalentOption(null, "Aucun talent") }
            .Concat(card.Card.Talents.Select((t, i) => new TalentOption(t, $"{i + 1}. {kb.TalentName(t)}"))).ToList();
        _talent = TalentOptions.FirstOrDefault(o => o.Id == talentId) ?? TalentOptions[0];
        Changed = changed;
    }

    private Action Changed { get; }
    public CardItem Card { get; }
    public IReadOnlyList<TalentOption> TalentOptions { get; }
    public TalentOption Talent { get => _talent; set { if (Set(ref _talent, value)) Changed(); } }
}

/// <summary>Onglet Deck builder : 4 cartes + 1 talent par carte, sauvegarde en JSON.</summary>
public sealed class DeckBuilderViewModel : ViewModelBase
{
    private readonly KnowledgeBase _kb;
    private readonly DeckLibrary _library;
    private readonly Dictionary<string, CardItem> _byId;
    private Deck _current = new();
    private Deck? _selectedSaved;
    private string _name = "";
    private string _status = "";
    private bool _isReference;

    public DeckBuilderViewModel(CardBrowser browser, IReadOnlyList<CardItem> items, KnowledgeBase kb, DeckLibrary library)
    {
        Browser = browser; _kb = kb; _library = library;
        _byId = items.ToDictionary(i => i.Card.Id);
        NewCommand = new RelayCommand(_ => NewDeck());
        SaveCommand = new RelayCommand(_ => Save());
        DeleteCommand = new RelayCommand(_ => DeleteSelected(), _ => _selectedSaved is not null);
        AddCommand = new RelayCommand(_ => AddSelectedCard());
        RemoveCommand = new RelayCommand(p => { if (p is SlotViewModel s) { Slots.Remove(s); Revalidate(); } });
        Slots.CollectionChanged += (_, _) => { Raise(nameof(SlotsText)); };
        if (library.UnreadableFiles > 0) Status = $"{library.UnreadableFiles} fichier(s) de deck illisible(s) ignoré(s).";
        NewDeck();
    }

    public CardBrowser Browser { get; }
    public ObservableCollection<SlotViewModel> Slots { get; } = new();
    public ObservableCollection<Deck> SavedDecks => _library.Decks;
    public ObservableCollection<string> Problems { get; } = new();

    public RelayCommand NewCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }

    public string SlotsText => $"{Slots.Count} / {Deck.MaxCards} cartes";
    public string Name { get => _name; set { if (Set(ref _name, value)) Revalidate(); } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    /// <summary>Deck qui se joue réellement dans le jeu (D-050) : sert d'adversaire de référence aux moteurs.</summary>
    public bool IsReference { get => _isReference; set => Set(ref _isReference, value); }

    public Deck? SelectedSaved
    {
        get => _selectedSaved;
        set
        {
            if (!Set(ref _selectedSaved, value)) return;
            DeleteCommand.RaiseCanExecuteChanged();
            if (value is not null) OpenDeck(value);
        }
    }

    private void NewDeck()
    {
        _current = new Deck { GameVersion = _kb.Manifest.GameVersion };
        Slots.Clear();
        _selectedSaved = null; Raise(nameof(SelectedSaved)); DeleteCommand.RaiseCanExecuteChanged();
        Name = "Nouveau deck";
        IsReference = false;
        Status = "";
    }

    /// <summary>Ouvre un deck sauvegardé (aussi appelé depuis le Drafter).</summary>
    public void OpenDeck(Deck d)
    {
        _current = d;
        Slots.Clear();
        foreach (var s in d.Slots)
            if (_byId.TryGetValue(s.CardId, out var item))
                Slots.Add(new SlotViewModel(item, _kb, s.TalentId, Revalidate));
        Name = d.Name;
        IsReference = d.IsReference;
        Status = "";
        Revalidate();
    }

    private void AddSelectedCard()
    {
        var item = Browser.Selected;
        if (item is null) { Status = "Choisis d'abord une carte dans la liste."; return; }
        if (Slots.Count >= Deck.MaxCards) { Status = $"Le deck contient déjà {Deck.MaxCards} cartes."; return; }
        if (Slots.Any(s => s.Card.Card.Id == item.Card.Id)) { Status = "Cette carte est déjà dans le deck."; return; }
        Slots.Add(new SlotViewModel(item, _kb, item.Card.Talents[0], Revalidate));
        Status = "";
        Revalidate();
    }

    private Deck Snapshot()
    {
        _current.Name = Name.Trim();
        _current.IsReference = IsReference;
        _current.GameVersion = _kb.Manifest.GameVersion;
        _current.Slots = Slots.Select(s => new DeckSlot(s.Card.Card.Id, s.Talent.Id)).ToList();
        return _current;
    }

    private void Revalidate()
    {
        Problems.Clear();
        foreach (var p in DeckValidator.Validate(Snapshot(), _kb)) Problems.Add(p);
    }

    private void Save()
    {
        var deck = Snapshot();
        // Un brouillon incomplet peut être sauvegardé ; seules les erreurs de fond bloquent.
        var blocking = DeckValidator.Validate(deck, _kb, requireComplete: false);
        if (blocking.Count > 0) { Status = "Sauvegarde impossible : " + blocking[0]; return; }
        _library.Save(deck);
        _selectedSaved = SavedDecks.FirstOrDefault(d => d.Id == deck.Id);
        Raise(nameof(SelectedSaved)); DeleteCommand.RaiseCanExecuteChanged();
        Status = Slots.Count == Deck.MaxCards ? "Deck sauvegardé." : "Brouillon sauvegardé (deck incomplet).";
    }

    private void DeleteSelected()
    {
        if (_selectedSaved is null) return;
        _library.Delete(_selectedSaved.Id);
        NewDeck();
        Status = "Deck supprimé.";
    }
}
