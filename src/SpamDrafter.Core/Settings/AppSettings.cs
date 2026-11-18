using System.Text.Json;

namespace SpamDrafter.Core.Settings;

/// <summary>Décor d'un deck choisi par le joueur (couleur « #RRGGBB » et emoji) ; il n'est pas dans le fichier du deck.</summary>
public sealed class DeckStyle
{
    public string? Color { get; set; }
    public string? Emoji { get; set; }
}

/// <summary>Filtres retenus d'une liste de cartes (élément et rareté ; la recherche en texte n'est pas retenue).</summary>
public sealed class BrowserSettings
{
    public string? Element { get; set; }
    public string? Rarity { get; set; }
    public string? Species { get; set; }
    public string? Region { get; set; }
    public string? Chronology { get; set; }
    public string? Sort { get; set; }
    /// <summary>Le menu de tri et de filtres est réduit (plus de place pour les cartes).</summary>
    public bool? FiltersCollapsed { get; set; }   // absent = réduit (par défaut)
}

/// <summary>
/// Ce que l'application retient d'une fois sur l'autre (phase 12, lot A) : onglet, fenêtre, tri et filtres.
/// Un fichier absent, illisible ou incomplet donne les réglages par défaut : l'application ne plante jamais pour ça.
/// </summary>
public sealed class AppSettings
{
    public int SelectedTab { get; set; }
    /// <summary>Effets sonores (phase 13) : actifs par défaut.</summary>
    public bool SoundsEnabled { get; set; } = true;
    /// <summary>Volume des effets sonores, de 0 à 1.</summary>
    public double SoundVolume { get; set; } = 0.5;
    /// <summary>« Réduire les animations » : coupe l'inclinaison, le retournement, le reflet, les étoiles et les transitions.</summary>
    public bool ReduceAnimations { get; set; }
    /// <summary>Thème de couleur : « default » (violet), un élément : fire, light, earth, nature, shadow, water.</summary>
    public string ElementTheme { get; set; } = "default";
    /// <summary>Fond étoilé : vitesse du scintillement (1 = normal), nombre, taille (1 = normale) et couleur (#RRGGBB).</summary>
    public double StarSpeed { get; set; } = 1;
    public int StarCount { get; set; } = 90;
    public double StarSize { get; set; } = 1;
    public string StarColor { get; set; } = "auto";   // « auto » : couleurs propres au thème
    /// <summary>Accessibilité : taille de l'interface (1, 1,15 ou 1,3) et contraste élevé.</summary>
    public double UiScale { get; set; } = 1.3;
    /// <summary>Vrai une fois la taille d'interface passée à 130 % par défaut (réglage ajouté en phase 13, ajusté sur demande) : on ne le refait pas.</summary>
    public bool UiScale130Applied { get; set; }
    public bool HighContrast { get; set; }
    /// <summary>Couleur et emoji choisis pour des decks (identifiant du deck → style) ; ordre manuel de « Mes decks ».</summary>
    public Dictionary<string, DeckStyle> DeckStyles { get; set; } = new();
    public List<string> DeckManualOrder { get; set; } = new();
    /// <summary>Largeur des colonnes réglées à la souris (« builder », « drafter », « generator ») ; absent = disposition d'origine.</summary>
    public Dictionary<string, List<double>> PanelLayouts { get; set; } = new();
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public bool Maximized { get; set; }
    public string DeckSort { get; set; } = "name";
    public string DeckOrigin { get; set; } = "all";
    public bool FavoritesOnly { get; set; }
    /// <summary>
    /// Dernière version de SPAM dont le joueur a vu les nouveautés. Quand les données passent à une autre version, l'application
    /// affiche « Quoi de neuf » jusqu'à ce qu'il le ferme. Null : première ouverture (rien à montrer).
    /// </summary>
    public string? LastSeenGameVersion { get; set; }
    /// <summary>Affichage par défaut choisi pour une carte : identifiant de la carte → identifiant du skin. Absent = la carte de base.</summary>
    public Dictionary<string, string> CardSkins { get; set; } = new();
    /// <summary>Filtres des listes de cartes, par nom de liste (« library », « builder », « drafter », « generator »).</summary>
    public Dictionary<string, BrowserSettings> Browsers { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpamDrafter", "settings.json");

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
        return new AppSettings();
    }

    /// <summary>Enregistre sans jamais lever d'erreur (un réglage perdu ne doit pas empêcher de fermer l'application).</summary>
    public bool TrySave(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}
