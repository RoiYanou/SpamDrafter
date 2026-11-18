using SpamDrafter.Core.DataPackages;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;
using SpamDrafter.Core.Settings;

namespace SpamDrafter.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    public MainViewModel()
    {
        // SPAMDRAFTER_SETTINGS : autre fichier de réglages (essais, sans toucher à ceux du joueur).
        SettingsPath = Environment.GetEnvironmentVariable("SPAMDRAFTER_SETTINGS") is { Length: > 0 } sp ? sp : AppSettings.DefaultPath;
        var firstRun = !File.Exists(SettingsPath);
        Settings = AppSettings.Load(SettingsPath);
        SpamDrafter.App.Views.PanelLayout.Settings = Settings;
        SpamDrafter.App.Views.PanelLayout.Save = () => Settings.TrySave(SettingsPath);
        if (!Settings.UiScale130Applied) { Settings.UiScale = 1.3; Settings.UiScale130Applied = true; Settings.TrySave(SettingsPath); }   // 130 % par défaut
        Sounds.Enabled = Settings.SoundsEnabled; Sounds.Volume = Settings.SoundVolume;
        SpamDrafter.App.Views.Motion.Reduce = Settings.ReduceAnimations;
        SpamDrafter.App.Views.StarSettings.Set(Settings.StarCount, Settings.StarSize, Settings.StarSpeed, Settings.StarColor);
        ApplyTheme();
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
            DeckChipsConverter.Register(kb, items);
            // Affichage par défaut choisi pour chaque carte (carte de base ou skin alternatif), retenu d'une fois sur l'autre.
            foreach (var item in items)
            {
                if (Settings.CardSkins.TryGetValue(item.Card.Id, out var skin)) item.SelectedSkinId = skin;
                item.SkinChanged += changed =>
                {
                    if (changed.SelectedSkinId is { } id) Settings.CardSkins[changed.Card.Id] = id; else Settings.CardSkins.Remove(changed.Card.Id);
                    Settings.TrySave(SettingsPath);
                };
            }
            // SPAMDRAFTER_DECKS : autre dossier de decks (essais, sans toucher aux decks du joueur).
            var decksDir = Environment.GetEnvironmentVariable("SPAMDRAFTER_DECKS") is { Length: > 0 } custom ? custom
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpamDrafter", "decks");

            var decks = new DeckLibrary(new DeckStore(decksDir));
            _browsers["library"] = new CardBrowser(items, kb);
            _browsers["builder"] = new CardBrowser(items, kb);
            _browsers["drafter"] = new CardBrowser(items, kb);
            _browsers["generator"] = new CardBrowser(items, kb);
            foreach (var (name, b) in _browsers) b.ApplyFilters(Settings.Browsers.GetValueOrDefault(name));
            Library = new LibraryViewModel(_browsers["library"], kb, decks, items);
            LibraryTabs = new LibraryTabsViewModel(Library, new TalentsViewModel(kb, items), new ToxViewModel(kb, items), new ArtistsViewModel(kb, items));
            LibraryTabs.SelectRandomRows();
            QuickDetail = new LibraryViewModel(new CardBrowser(items, kb), kb, decks, items);
            LibraryTabs.CardOpener = OpenCardDetail;   // un clic sur une carte dans Talents, Tox, Artistes ou une astuce ouvre la même fiche que le clic droit
            DeckBuilder = new DeckBuilderViewModel(_browsers["builder"], items, kb, decks);
            DeckBuilder.ApplySettings(Settings);
            DeckBuilder.SettingsChanged += () => Settings.TrySave(SettingsPath);
            DeckBuilder.ValidationFailed += () => Sounds.Play(SoundKind.Error);
            Drafter = new DrafterViewModel(_browsers["drafter"], items, kb, decks, DeckBuilder);
            // « Quoi de neuf » (lot E) : bandeau quand les données passent à une autre version de SPAM.
            WhatsNew = new WhatsNewViewModel(kb, Settings, () => Settings.TrySave(SettingsPath), () => DeckBuilder.ImpactedDeckCount);
            WhatsNew.ShowImpactedRequested += () => { DeckBuilder.ImpactedOnly = true; SelectedTab = 1; };
            decks.Changed += (_, _) => { if (WhatsNew.IsVisible) WhatsNew.Refresh(); };
            Drafter.OpenRequested += deck => { DeckBuilder.RequestOpen(deck); SelectedTab = 1; };
            QuickDetail.OpenDeckRequested += deck => { QuickDetail.CloseOverlayCommand.Execute(null); DeckBuilder.RequestOpen(deck); SelectedTab = 1; };
            Library.OpenDeckRequested += deck => { DeckBuilder.RequestOpen(deck); SelectedTab = 1; };
            try
            {
                // Le simulateur lit la même base ; s'il échoue, seul l'onglet Génération est indisponible.
                var sim = SpamDrafter.Simulator.Data.SimKnowledge.Load(dir);
                // Méta synthétique et pré-calcul livrés avec les données (phase 9, lot D, D-054).
                var coordinator = new SpamDrafter.Engines.Coordinator(sim, SpamDrafter.Engines.SyntheticData.Load(dir, sim));
                // Mémoire et historique des générations : à côté des réglages, valables pour ce contexte (version, méta, poids, moteurs).
                var history = new SpamDrafter.Engines.History.GenerationHistory(
                    Path.Combine(Path.GetDirectoryName(SettingsPath)!, "history.json"), coordinator.CacheContext);
                Generator = new GeneratorViewModel(_browsers["generator"], items, kb, coordinator, decks, history: history);
                _ = coordinator.WarmUpAsync();   // classement de référence des fiches, préparé en arrière-plan
            }
            catch (Exception e)
            {
                GeneratorError = "Générateur indisponible : " + e.Message;
            }
            var revision = installed is { } i && i.Directory == dir ? $" · données révision {i.Manifest.Revision}" : "";
            Header = $"SPAM {kb.Manifest.GameVersion} · {kb.Cards.Count} cartes{revision}";
            Home = new HomeViewModel(kb, items, decks, deck => { DeckBuilder.RequestOpen(deck); SelectedTab = 1; }, card => OpenCardDetail(card));
            Home.OpenBuilder = () => SelectedTab = 1;
            // Nom de l'illustrateur cliqué dans une fiche : l'artiste s'ouvre dans la Bibliothèque (la fiche rapide d'un autre onglet est fermée).
            Library.OpenArtistRequested += id => { LibraryTabs.ShowArtist(id); SelectedTab = 0; };
            QuickDetail.OpenArtistRequested += id => { QuickDetail.CloseOverlayCommand.Execute(null); LibraryTabs.ShowArtist(id); SelectedTab = 0; };
            // Noms colorés de l'astuce : carte, talent et Tox ouvrent leur fiche ; une compétence ouvre la première carte qui l'a (c'est là qu'elle est décrite).
            Home.OpenTipTarget = seg =>
            {
                switch (seg.Kind)
                {
                    case TipKind.Card: if (items.FirstOrDefault(i => i.Card.Id == seg.Id) is { } c) LibraryTabs.ShowCard(c); break;
                    case TipKind.Ability: if (items.FirstOrDefault(i => i.Card.Abilities.Contains(seg.Id!)) is { } a) LibraryTabs.ShowCard(a); break;
                    case TipKind.Talent: LibraryTabs.ShowTalent(seg.Id!); break;
                    case TipKind.Tox: LibraryTabs.ShowTox(seg.Id!); break;
                }
                SelectedTab = 0;
            };
            // Thème « selon la carte choisie » : la couleur suit l'élément de la carte ouverte dans la Bibliothèque.
            _lastWorkTab = Settings.SelectedTab is >= 0 and <= 3 && (Settings.SelectedTab != 3 || HasGenerator) ? Settings.SelectedTab : 0;
            // Sons : sauvegarde d'un deck, fin d'une génération.
            decks.Changed += (_, _) => { if (_ready) Sounds.Play(SoundKind.Success); };
            if (Generator is { } gen)
                gen.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(GeneratorViewModel.IsBusy) && !gen.IsBusy && _ready) Sounds.Play(SoundKind.Success); };
            _ready = true;
            // Toujours l'accueil au démarrage ; le travail en cours n'est pas perdu : « Reprendre là où j'en étais » ramène à l'onglet d'avant.
            SelectedTab = 4;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            LoadError = e.Message;
            Header = "Erreur de chargement";
        }
    }

    private readonly Dictionary<string, CardBrowser> _browsers = new();
    private bool _ready;
    private int _lastWorkTab;

    /// <summary>Applique le thème de couleur choisi (un ancien réglage « selon la carte » retombe sur le thème par défaut).</summary>
    private void ApplyTheme()
    {
        var theme = Settings.ElementTheme;
        SpamDrafter.App.Views.ThemeManager.Apply(theme, Settings.HighContrast);
    }

    // ----- Thème par élément, étoiles et accessibilité (réglages) -----
    public IReadOnlyList<string> ThemeOptions { get; } = SpamDrafter.App.Views.ThemeManager.Options.Select(o => o.Label).ToList();
    public int ThemeIndex
    {
        get => Math.Max(0, SpamDrafter.App.Views.ThemeManager.Options.ToList().FindIndex(o => o.Id == Settings.ElementTheme));
        set { Settings.ElementTheme = SpamDrafter.App.Views.ThemeManager.Options[Math.Clamp(value, 0, SpamDrafter.App.Views.ThemeManager.Options.Count - 1)].Id; Raise(nameof(ThemeIndex)); ApplyTheme(); Settings.TrySave(SettingsPath); }
    }
    public bool HighContrast
    {
        get => Settings.HighContrast;
        set { Settings.HighContrast = value; Raise(nameof(HighContrast)); ApplyTheme(); Settings.TrySave(SettingsPath); }
    }
    public IReadOnlyList<string> UiScaleOptions { get; } = new[] { "100 %", "115 %", "130 %" };
    private static readonly double[] UiScales = { 1, 1.15, 1.3 };
    public int UiScaleIndex
    {
        get => Math.Max(0, Array.FindIndex(UiScales, s => Math.Abs(s - Settings.UiScale) < 0.01));
        set { Settings.UiScale = UiScales[Math.Clamp(value, 0, 2)]; Raise(nameof(UiScaleIndex)); Raise(nameof(UiTransform)); Settings.TrySave(SettingsPath); }
    }
    /// <summary>Zoom de l'interface (accessibilité : taille du texte et des commandes).</summary>
    public Avalonia.Media.ITransform UiTransform => new Avalonia.Media.ScaleTransform(Settings.UiScale, Settings.UiScale);

    private void UpdateStars()
    {
        SpamDrafter.App.Views.StarSettings.Set(Settings.StarCount, Settings.StarSize, Settings.StarSpeed, Settings.StarColor);
        Settings.TrySave(SettingsPath);
        Raise(nameof(ParticlesModified));
    }

    /// <summary>Vrai si un réglage des particules n'est pas celui par défaut : le bouton « Réglages par défaut » n'apparaît alors seulement.</summary>
    public bool ParticlesModified
    {
        get
        {
            var d = new SpamDrafter.Core.Settings.AppSettings();
            return Settings.StarCount != d.StarCount || Math.Abs(Settings.StarSize - d.StarSize) > 0.01
                || Math.Abs(Settings.StarSpeed - d.StarSpeed) > 0.01 || !string.Equals(Settings.StarColor, d.StarColor, StringComparison.OrdinalIgnoreCase);
        }
    }

    private RelayCommand? _resetParticles;
    public RelayCommand ResetParticlesCommand => _resetParticles ??= new RelayCommand(_ =>
    {
        var d = new SpamDrafter.Core.Settings.AppSettings();
        Settings.StarCount = d.StarCount; Settings.StarSize = d.StarSize; Settings.StarSpeed = d.StarSpeed; Settings.StarColor = d.StarColor;
        foreach (var n in new[] { nameof(StarCount), nameof(StarSize), nameof(StarSpeed), nameof(StarColorIndex) }) Raise(n);
        UpdateStars();
    });
    public double StarSpeed { get => Settings.StarSpeed; set { Settings.StarSpeed = Math.Round(value, 1); Raise(nameof(StarSpeed)); UpdateStars(); } }
    public double StarCount { get => Settings.StarCount; set { Settings.StarCount = (int)Math.Round(value); Raise(nameof(StarCount)); UpdateStars(); } }
    public double StarSize { get => Settings.StarSize; set { Settings.StarSize = Math.Round(value, 1); Raise(nameof(StarSize)); UpdateStars(); } }
    public IReadOnlyList<string> StarColorOptions { get; } = new[] { "Selon le thème", "Blanc", "Jaune", "Bleu", "Rose", "Vert" };
    // « auto » : chaque thème garde ses couleurs propres (pluie bleue, braises orange…) ; une autre couleur teinte les particules de TOUS les thèmes.
    private static readonly string[] StarColors = { "auto", "#FFFFFD", "#FFC93C", "#8FB8FF", "#FF9ED8", "#9BF0B5" };
    public int StarColorIndex
    {
        get => Math.Max(0, Array.FindIndex(StarColors, c => string.Equals(c, Settings.StarColor, StringComparison.OrdinalIgnoreCase)));
        set { Settings.StarColor = StarColors[Math.Clamp(value, 0, StarColors.Length - 1)]; Raise(nameof(StarColorIndex)); UpdateStars(); }
    }

    public HomeViewModel? Home { get; }

    // ----- Réglages d'ambiance (phase 13) : sons et animations, enregistrés aussitôt -----
    public bool SoundsEnabled
    {
        get => Settings.SoundsEnabled;
        set { Settings.SoundsEnabled = value; Sounds.Enabled = value; Raise(nameof(SoundsEnabled)); Settings.TrySave(SettingsPath); if (value) Sounds.Play(SoundKind.Click); }
    }
    public double SoundVolume
    {
        get => Settings.SoundVolume;
        set { Settings.SoundVolume = Math.Round(value, 2); Sounds.Volume = Settings.SoundVolume; Raise(nameof(SoundVolume)); Settings.TrySave(SettingsPath); }
    }
    public bool ReduceAnimations
    {
        get => Settings.ReduceAnimations;
        set { Settings.ReduceAnimations = value; SpamDrafter.App.Views.Motion.Reduce = value; Raise(nameof(ReduceAnimations)); Settings.TrySave(SettingsPath); }
    }

    /// <summary>Ce que l'application retient d'une fois sur l'autre (phase 12, lot A).</summary>
    public AppSettings Settings { get; }
    public string SettingsPath { get; }

    /// <summary>Enregistre l'onglet, les filtres et le tri ; <paramref name="window"/> y ajoute la taille et la position de la fenêtre.</summary>
    public void SaveSettings(Action<AppSettings>? window = null)
    {
        if (!IsLoaded) return;
        Settings.SelectedTab = _lastWorkTab;   // l'onglet de travail, pas l'accueil : il sert à « Reprendre »
        foreach (var (name, b) in _browsers) Settings.Browsers[name] = b.CaptureFilters();
        DeckBuilder?.CaptureSettings(Settings);
        window?.Invoke(Settings);
        Settings.TrySave(SettingsPath);
    }

    public string Header { get; }
    public UpdaterViewModel Updater { get; }
    public WhatsNewViewModel? WhatsNew { get; }
    public string? LoadError { get; }
    public bool HasError => LoadError is not null;
    public bool IsLoaded => LoadError is null;
    public LibraryViewModel? Library { get; }
    /// <summary>La Bibliothèque en sous-onglets : Cartes, Talents, Tox, Artistes.</summary>
    public LibraryTabsViewModel? LibraryTabs { get; }
    /// <summary>Fiche détaillée ouverte par un clic droit sur une carte (indépendante de la Bibliothèque).</summary>
    public LibraryViewModel? QuickDetail { get; }
    public DeckBuilderViewModel? DeckBuilder { get; }
    public DrafterViewModel? Drafter { get; }
    public GeneratorViewModel? Generator { get; }
    public string? GeneratorError { get; }
    public bool HasGenerator => Generator is not null;

    private int _selectedTab;
    /// <summary>0 = Bibliothèque, 1 = Deck builder, 2 = Drafter, 3 = Génération de deck.</summary>
    public int SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (!Set(ref _selectedTab, value)) return;
            if (value != 4) _lastWorkTab = value;
            foreach (var n in new[] { nameof(IsTab0), nameof(IsTab1), nameof(IsTab2), nameof(IsTab3), nameof(IsTab4) }) Raise(n);
            if (_ready) Sounds.Play(SoundKind.Tab);
        }
    }

    // Colonne d'icônes (phase 13) : une entrée par onglet.
    public bool IsTab0 => _selectedTab == 0;
    public bool IsTab1 => _selectedTab == 1;
    public bool IsTab2 => _selectedTab == 2;
    public bool IsTab3 => _selectedTab == 3;
    /// <summary>L'accueil (numéro 4 pour ne pas changer les numéros des autres onglets, déjà enregistrés).</summary>
    public bool IsTab4 => _selectedTab == 4;
    /// <summary>Choisit un onglet depuis la colonne d'icônes (le paramètre est son numéro, 0 à 3).</summary>
    /// <summary>Ouvre la fiche détaillée d'une carte (clic droit sur une carte) dans la Bibliothèque.</summary>
    public void OpenCardDetail(CardItem card)
    {
        if (QuickDetail is null) return;
        QuickDetail.Browser.Selected = null;
        QuickDetail.Browser.Selected = card;   // la fiche s'ouvre par-dessus l'onglet en cours : le joueur ne le quitte pas
    }

    public RelayCommand SelectTabCommand => _selectTab ??= new RelayCommand(p =>
    {
        if (p is string s && int.TryParse(s, out var i) && (i != 3 || HasGenerator) && i is >= 0 and <= 4)
        {
            SelectedTab = i;
            if (i == 0 && LibraryTabs is not null) LibraryTabs.SelectedTab = 0;   // un clic sur « Biblio » ramène toujours au sous-onglet « Cartes »
        }
    });
    private RelayCommand? _selectTab;

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
