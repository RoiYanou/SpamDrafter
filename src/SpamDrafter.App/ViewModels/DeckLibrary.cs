using System.Collections.ObjectModel;
using SpamDrafter.Core.Decks;

namespace SpamDrafter.App.ViewModels;

/// <summary>Liste des decks sauvegardés, partagée par le Deck builder et le Drafter (source unique, D-017).</summary>
public sealed class DeckLibrary
{
    private readonly DeckStore _store;

    public DeckLibrary(DeckStore store) { _store = store; Reload(); }

    public ObservableCollection<Deck> Decks { get; } = new();
    public int UnreadableFiles { get; private set; }

    /// <summary>Déclenché après chaque sauvegarde, suppression ou rechargement.</summary>
    public event EventHandler? Changed;

    public void Reload()
    {
        var errors = new List<string>();
        Decks.Clear();
        foreach (var d in _store.LoadAll(errors)) Decks.Add(d);
        UnreadableFiles = errors.Count;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Save(Deck deck) { _store.Save(deck); Reload(); }
    public void Delete(Guid id) { _store.Delete(id); Reload(); }
}
