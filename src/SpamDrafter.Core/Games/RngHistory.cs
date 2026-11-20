using System.Text.Json;

namespace SpamDrafter.Core.Games;

/// <summary>Une ligne du détail d'un tirage enregistré : la raison, la valeur et les points.</summary>
public sealed class RngHistoryLine
{
    public string Reason { get; set; } = "";
    public string Value { get; set; } = "";
    public int Points { get; set; }
    public bool IsBonus { get; set; }
    /// <summary>Rareté du gain (log₂ de 1 / probabilité), pour sa couleur ; 0 dans les anciens tirages.</summary>
    public double Bits { get; set; }
}

/// <summary>Un tirage du jour enregistré avec sa date : la note, le rang et tout le détail pour le revoir plus tard.</summary>
public sealed class RngHistoryEntry
{
    /// <summary>Jour du tirage (« 2026-11-20 », heure de Paris).</summary>
    public string Day { get; set; } = "";
    /// <summary>Instant où le joueur a fait le tirage (UTC).</summary>
    public DateTime PlayedUtc { get; set; }
    public int Total { get; set; }
    public double Percentile { get; set; }
    public string Title { get; set; } = "";
    public bool Perfect { get; set; }
    /// <summary>Carte dont le nom a été tiré, carte dont l'illustration a été tirée (pour la vignette).</summary>
    public string NameCardId { get; set; } = "";
    public string IllustrationCardId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<RngHistoryLine> Lines { get; set; } = new();
}

/// <summary>
/// L'historique des tirages du jour (<c>rng-history.json</c> à côté des réglages), du plus récent au plus ancien.
/// Un fichier absent ou illisible donne un historique vide. Un jour n'a qu'un tirage : le refaire (réinitialisation) remplace l'ancien.
/// </summary>
public sealed class RngHistory
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public List<RngHistoryEntry> Entries { get; set; } = new();

    public static string DefaultPath(string settingsPath) => Path.Combine(Path.GetDirectoryName(settingsPath)!, "rng-history.json");

    public static RngHistory Load(string path)
    {
        try { if (File.Exists(path)) return JsonSerializer.Deserialize<RngHistory>(File.ReadAllText(path), Options) ?? new(); }
        catch (Exception e) when (e is JsonException or IOException) { }
        return new();
    }

    public bool TrySave(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Ajoute le tirage d'un jour (ou remplace celui du même jour) ; les plus récents d'abord.</summary>
    public void Record(RngHistoryEntry entry)
    {
        Entries.RemoveAll(e => e.Day == entry.Day);
        Entries.Add(entry);
        Entries = Entries.OrderByDescending(e => e.Day, StringComparer.Ordinal).ToList();
    }

    /// <summary>Construit l'entrée de l'historique d'un tirage.</summary>
    public static RngHistoryEntry EntryFor(RngGame game, DateOnly day, string name, DateTime playedUtc) => new()
    {
        Day = DailyClock.Key(day), PlayedUtc = playedUtc, Total = game.Total, Percentile = game.Percentile, Title = game.Title, Perfect = game.IsPerfect,
        NameCardId = game.NameCard.Id, IllustrationCardId = game.IllustrationCard.Id, Name = name,
        Summary = string.Join(" · ", game.Parts.Take(5).Select(p => p.Value)),
        Lines = game.Lines.Select(l => new RngHistoryLine { Reason = l.Reason, Value = l.Value, Points = l.Points, IsBonus = l.IsBonus, Bits = l.Bits }).ToList(),
    };
}
