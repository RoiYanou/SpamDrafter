using SpamDrafter.Core.DataPackages;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    public MainViewModel()
    {
        // Données du jeu installées (paquet séparé, lot B) : on retire d'abord les anciennes révisions.
        var store = new DataStore(DataStore.DefaultRoot);
        store.CleanUp();
        Updater = new UpdaterViewModel(store);
        _ = Updater.CheckAsync();   // en arrière-plan ; ne bloque pas l'ouverture
        try
        {
            var installed = store.FindActive();
            var dir = FindKnowledgeDirectory() ?? installed?.Directory
                ?? throw new DirectoryNotFoundException(
                    "Les données du jeu (textes, illustrations) ne sont pas encore installées. " +
                    (Updater.IsInstalled ? "SpamDrafter les télécharge (progression en haut à droite) ; clique ensuite sur « Redémarrer pour mettre à jour ». " : "") +
                    "Sans connexion : clique sur « Importer des données… » et choisis un fichier « SpamDrafter-donnees-<version>-r<N>.zip », " +
                    "puis sur « Redémarrer pour mettre à jour ».");
            var kb = KnowledgeBase.Load(dir);
            var items = kb.Cards.Select(c => new CardItem(c, kb)).ToList();
            // SPAMDRAFTER_DECKS : autre dossier de decks (essais, sans toucher aux decks du joueur).
            var decksDir = Environment.GetEnvironmentVariable("SPAMDRAFTER_DECKS") is { Length: > 0 } custom ? custom
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpamDrafter", "decks");

            var decks = new DeckLibrary(new DeckStore(decksDir));
            Library = new LibraryViewModel(new CardBrowser(items, kb), kb);
            DeckBuilder = new DeckBuilderViewModel(new CardBrowser(items, kb), items, kb, decks);
            Drafter = new DrafterViewModel(new CardBrowser(items, kb), items, kb, decks);
            Drafter.OpenRequested += deck => { DeckBuilder.OpenDeck(deck); SelectedTab = 1; };
            try
            {
                // Le simulateur lit la même base ; s'il échoue, seul l'onglet Génération est indisponible.
                var sim = SpamDrafter.Simulator.Data.SimKnowledge.Load(dir);
                // Méta synthétique et pré-calcul livrés avec les données (phase 9, lot D, D-054).
                var coordinator = new SpamDrafter.Engines.Coordinator(sim, SpamDrafter.Engines.SyntheticData.Load(dir, sim));
                Generator = new GeneratorViewModel(new CardBrowser(items, kb), items, kb, coordinator, decks);
                _ = coordinator.WarmUpAsync();   // classement de référence des fiches, préparé en arrière-plan
            }
            catch (Exception e)
            {
                GeneratorError = "Génération indisponible : " + e.Message;
            }
            var revision = installed is { } i && i.Directory == dir ? $" · données révision {i.Manifest.Revision}" : "";
            Header = $"SPAM {kb.Manifest.GameVersion} · {kb.Cards.Count} cartes{revision}";
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            LoadError = e.Message;
            Header = "Erreur de chargement";
        }
    }

    public string Header { get; }
    public UpdaterViewModel Updater { get; }
    public string? LoadError { get; }
    public bool HasError => LoadError is not null;
    public bool IsLoaded => LoadError is null;
    public LibraryViewModel? Library { get; }
    public DeckBuilderViewModel? DeckBuilder { get; }
    public DrafterViewModel? Drafter { get; }
    public GeneratorViewModel? Generator { get; }
    public string? GeneratorError { get; }
    public bool HasGenerator => Generator is not null;

    private int _selectedTab;
    /// <summary>0 = Bibliothèque, 1 = Deck builder, 2 = Drafter, 3 = Génération de deck.</summary>
    public int SelectedTab { get => _selectedTab; set => Set(ref _selectedTab, value); }

    /// <summary>
    /// Variable d'environnement, sinon dossier knowledge/&lt;version&gt; au-dessus de l'exécutable (développement).
    /// À défaut, l'appelant prend le paquet de données installé.
    /// </summary>
    private static string? FindKnowledgeDirectory()
    {
        var env = Environment.GetEnvironmentVariable("SPAMDRAFTER_KNOWLEDGE");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var root = Path.Combine(d.FullName, "knowledge");
            if (!Directory.Exists(root)) continue;
            var latest = Directory.EnumerateDirectories(root)
                .Where(p => File.Exists(Path.Combine(p, "manifest.json")))
                .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (latest is not null) return latest;
        }
        return null;
    }
}
