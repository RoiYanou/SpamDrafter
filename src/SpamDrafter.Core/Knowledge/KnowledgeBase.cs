using System.Text.Json;
using System.Text.Json.Serialization;
using Text_ = SpamDrafter.Core.Text;

namespace SpamDrafter.Core.Knowledge;

/// <summary>Le lore d'une carte (textes du jeu) : espèce, région, chronologie et récit.</summary>
public sealed record CardLore(string Species, string Region, string Chronology, string Description);

/// <summary>Base de connaissances en lecture seule (dossier knowledge/&lt;version&gt;/).</summary>
public sealed class KnowledgeBase
{
    /// <summary>Version du format de la base (manifest.json, « formatVersion ») que ce programme sait lire.</summary>
    public const string SupportedFormatVersion = "1";

    private readonly Dictionary<string, string> _texts;
    private readonly Dictionary<string, Card> _cardsById;
    private readonly Dictionary<string, Talent> _talentsById;

    public string Directory { get; }
    public Manifest Manifest { get; }
    public IReadOnlyList<Card> Cards { get; }
    public IReadOnlyList<Talent> Talents { get; }
    public IReadOnlyList<Ordered> Elements { get; }
    public IReadOnlyList<Ordered> Rarities { get; }
    public string Language { get; }

    private KnowledgeBase(string dir, string language, Manifest manifest, List<Card> cards, List<Talent> talents,
        List<Ordered> elements, List<Ordered> rarities, Dictionary<string, string> texts)
    {
        Directory = dir; Language = language; Manifest = manifest;
        Cards = cards; Talents = talents; Elements = elements; Rarities = rarities; _texts = texts;
        _cardsById = cards.ToDictionary(c => c.Id);
        _talentsById = talents.ToDictionary(t => t.Id);
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static KnowledgeBase Load(string dir, string language = "fr")
    {
        if (!System.IO.Directory.Exists(dir))
            throw new DirectoryNotFoundException($"Base de connaissances introuvable : {dir}");

        T Read<T>(string file) =>
            JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(dir, file)), Options)
            ?? throw new InvalidDataException($"Fichier vide ou invalide : {file}");

        var manifest = Read<Manifest>("manifest.json");
        var cards = Read<List<Card>>("cards.json");
        var talents = Read<List<Talent>>("talents.json");
        var elements = Read<List<Ordered>>("elements.json").OrderBy(e => e.Order).ToList();
        var rarities = Read<List<Ordered>>("rarities.json").OrderBy(r => r.Order).ToList();
        var texts = Read<Dictionary<string, string>>(Path.Combine("texts", language + ".json"));

        var talentIds = talents.Select(t => t.Id).ToHashSet();
        foreach (var c in cards)
            foreach (var t in c.Talents)
                if (!talentIds.Contains(t))
                    throw new InvalidDataException($"Carte « {c.SanitizedName} » : talent inconnu « {t} ».");

        return new KnowledgeBase(dir, language, manifest, cards.OrderBy(c => c.Order).ToList(), talents,
            elements, rarities, texts);
    }

    public Card? FindCard(string id) => _cardsById.GetValueOrDefault(id);
    public Talent? FindTalent(string id) => _talentsById.GetValueOrDefault(id);

    /// <summary>Texte traduit ; renvoie la clé elle-même si elle est absente (jamais d'exception).</summary>
    public string Text(string key) => _texts.TryGetValue(key, out var v) ? v : key;

    public string CardName(Card c) => Text(c.NameKey);

    /// <summary>
    /// Lore d'une carte (🃏 textes du jeu : <c>Card.Lore.&lt;nom&gt;.Cat1/Cat2/Cat3/Description</c>). Cat1 = espèce, Cat2 = région,
    /// Cat3 = chronologie (constaté sur les 151 cartes : 25 espèces, 17 régions, 5 périodes). Null si le jeu n'a pas de lore pour la carte.
    /// </summary>
    public CardLore? Lore(Card c)
    {
        var prefix = "Card.Lore." + c.SanitizedName;
        if (!_texts.TryGetValue(prefix + ".Description", out var description) && !_texts.ContainsKey(prefix + ".Cat1")) return null;
        string Cat(string n) => _texts.TryGetValue(prefix + ".Cat" + n, out var v) ? v.Trim() : "";
        return new CardLore(Cat("1"), Cat("2"), Cat("3"), (description ?? "").Replace("\r\n", "\n").Trim());
    }

    private Dictionary<string, ToxEntry>? _tox;

    /// <summary>Un Tox par son identifiant (tox.json) ; null s'il est inconnu ou si la base n'a pas de tox.json.</summary>
    public ToxEntry? Tox(string uuid)
    {
        _tox ??= LoadTox();
        return _tox.GetValueOrDefault(uuid);
    }

    private Dictionary<string, ToxEntry> LoadTox()
    {
        var result = new Dictionary<string, ToxEntry>();
        var path = Path.Combine(Directory, "tox.json");
        if (!File.Exists(path)) return result;
        try
        {
            var list = JsonSerializer.Deserialize<List<ToxEntry>>(File.ReadAllText(path), Options) ?? new();
            foreach (var t in list) result[t.Uuid] = t;
        }
        catch (Exception e) when (e is JsonException or IOException) { }
        return result;
    }

    /// <summary>Tous les Tox du jeu (tox.json), dans l'ordre du fichier.</summary>
    public IReadOnlyList<ToxEntry> AllTox { get { _tox ??= LoadTox(); return _tox.Values.ToList(); } }

    private Dictionary<string, ArtistEntry>? _artists;

    /// <summary>Tous les artistes du jeu (artists.json), par ordre alphabétique sans accents ; vide si la base n'a pas ce fichier.</summary>
    public IReadOnlyList<ArtistEntry> Artists
    {
        get
        {
            _artists ??= LoadArtists();
            return _artists.Values.OrderBy(a => Text_.TextSearch.Normalize(a.Name), StringComparer.Ordinal).ToList();
        }
    }

    public ArtistEntry? Artist(string? id)
    {
        _artists ??= LoadArtists();
        return id is null ? null : _artists.GetValueOrDefault(id);
    }

    private Dictionary<string, ArtistEntry> LoadArtists()
    {
        var result = new Dictionary<string, ArtistEntry>();
        var path = Path.Combine(Directory, "artists.json");
        if (!File.Exists(path)) return result;
        try
        {
            foreach (var a in JsonSerializer.Deserialize<List<ArtistEntry>>(File.ReadAllText(path), Options) ?? new()) result[a.Id] = a;
        }
        catch (Exception e) when (e is JsonException or IOException) { }
        return result;
    }

    /// <summary>Nom d'un Tox (texte du jeu « Card.Passif.&lt;titre&gt; »), à défaut son titre.</summary>
    public string ToxName(ToxEntry tox)
    {
        var key = "Card.Passif." + DescriptionFormatter.ToxKey(tox);
        return _texts.TryGetValue(key, out var v) ? v : tox.Title;
    }

    /// <summary>Tox que la carte possède de base, dans l'ordre du jeu.</summary>
    public IReadOnlyList<ToxEntry> InnateTox(Card card) =>
        (card.InnatePassives ?? Array.Empty<string>()).Select(Tox).OfType<ToxEntry>().ToList();

    public Ordered? Element(string id) => Elements.FirstOrDefault(e => e.Id == id);
    public Ordered? Rarity(string id) => Rarities.FirstOrDefault(r => r.Id == id);

    private readonly Dictionary<string, Versions.ChangeSet?> _changes = new();

    /// <summary>
    /// Différences entre une version plus ancienne de SPAM et cette base (fichier <c>changes/from-&lt;version&gt;.json</c> du paquet de
    /// données, fabriqué par le Studio). Null si le fichier n'existe pas : on ne peut alors rien dire des différences.
    /// </summary>
    public Versions.ChangeSet? ChangesFrom(string fromVersion)
    {
        if (string.IsNullOrWhiteSpace(fromVersion) || fromVersion == Manifest.GameVersion) return null;
        if (!_changes.TryGetValue(fromVersion, out var cs))
            _changes[fromVersion] = cs = Versions.ChangeSet.TryRead(Path.Combine(Directory, "changes", Versions.ChangeSet.FileName(fromVersion)));
        return cs;
    }

    private DescriptionFormatter? _descriptions;
    /// <summary>Descriptions chiffrées des capacités et des talents (KI-021).</summary>
    public DescriptionFormatter Descriptions => _descriptions ??= new DescriptionFormatter(this);
    /// <summary>Nom d'une capacité de carte (clé « Card.Ability.&lt;id&gt; ») ; à défaut, son identifiant.</summary>
    public string AbilityName(string id) { var key = "Card.Ability." + id; return _texts.TryGetValue(key, out var v) ? v : id; }
    public string TalentName(string id) => Text("Talent.Name." + id);
    public string TalentDescription(string id) => Text("Talent.Description." + id);
    public string ElementName(string id) => Text("Element.Name." + id);
    public string RarityName(string id) => Text("Rarity.Name." + id.Replace("rarity-", ""));

    /// <summary>Chemin absolu d'une image (chemin relatif de la base), ou null si le fichier manque.</summary>
    public string? ImagePath(string? relative)
    {
        if (string.IsNullOrEmpty(relative)) return null;
        var p = Path.Combine(Directory, relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(p) ? p : null;
    }
}
