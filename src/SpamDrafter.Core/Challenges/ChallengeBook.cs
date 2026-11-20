using System.Text.Json;

namespace SpamDrafter.Core.Challenges;

/// <summary>Un challenge écrit par le joueur (sans vérification automatique).</summary>
public sealed class CustomChallenge
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Conditions { get; set; } = "";
    public ChallengeDefinition ToDefinition() => new(Id, Title, Conditions, ChallengeKind.Custom);
}

/// <summary>Pré-set de roue : les challenges présents (les autres sont désactivés) et leur poids.</summary>
public sealed class WheelPreset
{
    public string Name { get; set; } = "";
    public Dictionary<string, int> Weights { get; set; } = new();
}

/// <summary>
/// Réglages de l'onglet Challenges, enregistrés à côté des réglages (<c>challenges.json</c>) : challenges du joueur, poids (probabilités),
/// challenges désactivés, pré-sets. Un fichier absent ou illisible donne les réglages par défaut.
/// </summary>
public sealed class ChallengeBook
{
    public const int DefaultWeight = 10, MaxWeight = 1000;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public List<CustomChallenge> Custom { get; set; } = new();
    /// <summary>Poids d'un challenge (sa part de la roue = son poids / somme des poids). Absent : <see cref="DefaultWeight"/>.</summary>
    public Dictionary<string, int> Weights { get; set; } = new();
    public List<string> Disabled { get; set; } = new();
    public List<WheelPreset> Presets { get; set; } = new();
    public string? CurrentPreset { get; set; }

    public static string DefaultPath(string settingsPath) => Path.Combine(Path.GetDirectoryName(settingsPath)!, "challenges.json");

    public static ChallengeBook Load(string path)
    {
        try { if (File.Exists(path)) return JsonSerializer.Deserialize<ChallengeBook>(File.ReadAllText(path), Options) ?? new(); }
        catch (Exception e) when (e is JsonException or IOException) { }
        return new();
    }

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

    public IReadOnlyList<ChallengeDefinition> AllDefinitions() =>
        ChallengeCatalog.Defaults.Concat(Custom.Where(c => c.Title.Trim().Length > 0).Select(c => c.ToDefinition())).ToList();

    public int WeightOf(string id) => Math.Clamp(Weights.TryGetValue(id, out var w) ? w : DefaultWeight, 0, MaxWeight);
    public bool IsEnabled(string id) => !Disabled.Contains(id);

    /// <summary>Nom du pré-set intégré : tous les challenges par défaut, poids égaux.</summary>
    public const string DefaultPresetName = "Tous les challenges";

    /// <summary>Applique un pré-set : seuls ses challenges restent actifs, avec leurs poids.</summary>
    public void Apply(WheelPreset p)
    {
        Disabled = AllDefinitions().Select(d => d.Id).Where(id => !p.Weights.ContainsKey(id)).ToList();
        foreach (var (id, w) in p.Weights) Weights[id] = Math.Clamp(w, 1, MaxWeight);
        CurrentPreset = p.Name;
    }

    public void ApplyDefault()
    {
        Disabled = new(); Weights = new(); CurrentPreset = DefaultPresetName;
    }

    /// <summary>Enregistre l'état actuel sous ce nom (remplace un pré-set du même nom).</summary>
    public WheelPreset SavePreset(string name)
    {
        name = name.Trim();
        var preset = new WheelPreset { Name = name, Weights = AllDefinitions().Where(d => IsEnabled(d.Id)).ToDictionary(d => d.Id, d => Math.Max(1, WeightOf(d.Id))) };
        Presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
        Presets.Add(preset);
        CurrentPreset = name;
        return preset;
    }
}

/// <summary>Calculs de la roue : parts et tirage selon les poids. Un tour complet = 1.</summary>
public static class WheelMath
{
    /// <summary>Début et fin (de 0 à 1) de la part de chaque entrée, dans l'ordre ; les poids nuls n'ont pas de part.</summary>
    public static IReadOnlyList<(double Start, double End)> Slices(IReadOnlyList<int> weights)
    {
        var total = weights.Sum(w => Math.Max(0, w));
        var list = new List<(double, double)>();
        double at = 0;
        foreach (var w in weights)
        {
            var share = total == 0 ? 0 : Math.Max(0, w) / (double)total;
            list.Add((at, at + share)); at += share;
        }
        return list;
    }

    /// <summary>Indice tiré au hasard selon les poids, ou -1 si tous sont nuls. <paramref name="roll"/> : nombre de 0 (inclus) à 1 (exclu).</summary>
    public static int Pick(IReadOnlyList<int> weights, double roll)
    {
        var slices = Slices(weights);
        for (var i = 0; i < slices.Count; i++)
            if (slices[i].End > slices[i].Start && roll >= slices[i].Start && roll < slices[i].End) return i;
        return slices.ToList().FindLastIndex(s => s.End > s.Start);
    }
}
