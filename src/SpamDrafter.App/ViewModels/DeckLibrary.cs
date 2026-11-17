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
    /// <summary>Decks dont le nom a reçu « (2) » au chargement parce qu'il existait en double (D-072).</summary>
    public int RenamedDuplicates { get; private set; }

    /// <summary>Déclenché après chaque sauvegarde, suppression ou rechargement.</summary>
    public event EventHandler? Changed;

    public void Reload()
    {
        var errors = new List<string>();
        Decks.Clear();
        var loaded = _store.LoadAll(errors);
        var renamed = DeckNames.FixDuplicates(loaded);
        foreach (var d in renamed) _store.Save(d);
        RenamedDuplicates = renamed.Count;
        foreach (var d in loaded.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)) Decks.Add(d);
        UnreadableFiles = errors.Count;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Un autre deck porte-t-il déjà ce nom ? (sans accents ni casse, D-072)</summary>
    public bool NameTaken(string name, Guid exceptId) => DeckNames.IsTaken(Decks, name, exceptId);

    /// <summary>Sauvegarde tous les decks dans un .zip ; renvoie leur nombre.</summary>
    public int BackupTo(string zipPath) => DeckBackup.Create(_store.Directory, zipPath);

    /// <summary>Restaure les decks d'un .zip (rien n'est supprimé) puis recharge la liste.</summary>
    public RestoreResult RestoreFrom(string zipPath)
    {
        var result = DeckBackup.Restore(zipPath, _store.Directory);
        Reload();
        return result;
    }

    public void Save(Deck deck) { deck.ModifiedUtc = DateTime.UtcNow; _store.Save(deck); Reload(); }
    public void Delete(Guid id) { _store.Delete(id); Reload(); }
}
