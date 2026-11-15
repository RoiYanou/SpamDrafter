using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpamDrafter.Core.Knowledge;

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
