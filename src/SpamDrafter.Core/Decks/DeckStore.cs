using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpamDrafter.Core.Decks;

/// <summary>Decks sauvegardés en JSON, un fichier par deck (D-022). Source unique du Drafter (D-017).</summary>
public sealed class DeckStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _dir;
    public DeckStore(string directory) { _dir = directory; }

    /// <summary>Dossier des decks.</summary>
    public string Directory => _dir;

    private string PathOf(Guid id) => Path.Combine(_dir, id + ".json");

    public void Save(Deck deck)
    {
        System.IO.Directory.CreateDirectory(_dir);
        var tmp = PathOf(deck.Id) + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(deck, Options));
        File.Move(tmp, PathOf(deck.Id), overwrite: true);
    }

    /// <summary>Lit un deck depuis son texte JSON (null si le texte est vide ou « null »).</summary>
    public Deck? Deserialize(string json) => JsonSerializer.Deserialize<Deck>(json, Options);

    public void Delete(Guid id) { if (File.Exists(PathOf(id))) File.Delete(PathOf(id)); }

    /// <summary>Charge tous les decks ; un fichier illisible est ignoré et signalé dans <paramref name="errors"/>.</summary>
    public List<Deck> LoadAll(List<string>? errors = null)
    {
        var result = new List<Deck>();
        if (!System.IO.Directory.Exists(_dir)) return result;
        foreach (var f in System.IO.Directory.EnumerateFiles(_dir, "*.json").Order())
        {
            try
            {
                var d = JsonSerializer.Deserialize<Deck>(File.ReadAllText(f), Options);
                if (d is not null) result.Add(d);
            }
            catch (Exception e) when (e is JsonException or IOException)
            {
                errors?.Add($"{Path.GetFileName(f)} : {e.Message}");
            }
        }
        return result.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
