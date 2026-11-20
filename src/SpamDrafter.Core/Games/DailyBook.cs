using System.Text.Json;

namespace SpamDrafter.Core.Games;

/// <summary>Où en est le joueur dans un jeu quotidien, pour un jour donné.</summary>
public sealed class DailyRecord
{
    /// <summary>Jour joué (« 2026-11-20 », heure de Paris).</summary>
    public string Day { get; set; } = "";
    /// <summary>Réponses données (pour reprendre la partie si l'application est fermée).</summary>
    public List<string> Guesses { get; set; } = new();
    /// <summary>La partie est finie (gagnée ou perdue) : le jeu est fermé jusqu'au lendemain.</summary>
    public bool Finished { get; set; }
    /// <summary>La partie a été lancée (quizz : le joueur a accepté l'avertissement ; une partie lancée puis quittée compte les questions restantes comme fausses).</summary>
    public bool Started { get; set; }
    /// <summary>Le joueur a abandonné (énigme : la réponse lui a été donnée).</summary>
    public bool GaveUp { get; set; }
    public bool Won { get; set; }
}

/// <summary>
/// Les parties quotidiennes du joueur, dans <c>daily.json</c> à côté des réglages. Le contrôle « une fois par jour » est local
/// (il peut être contourné en supprimant le fichier : accepté, D-095). Un fichier absent ou illisible donne un carnet vide.
/// </summary>
public sealed class DailyBook
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Identifiant du jeu → dernière partie.</summary>
    public Dictionary<string, DailyRecord> Games { get; set; } = new();

    public static string DefaultPath(string settingsPath) => Path.Combine(Path.GetDirectoryName(settingsPath)!, "daily.json");

    public static DailyBook Load(string path)
    {
        try { if (File.Exists(path)) return JsonSerializer.Deserialize<DailyBook>(File.ReadAllText(path), Options) ?? new(); }
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

    /// <summary>La partie du jour pour ce jeu : celle en cours ou terminée, ou une nouvelle si le dernier jeu joué date d'un autre jour.</summary>
    public DailyRecord For(string gameId, DateOnly day)
    {
        var key = DailyClock.Key(day);
        if (Games.TryGetValue(gameId, out var r) && r.Day == key) return r;
        r = new DailyRecord { Day = key };
        Games[gameId] = r;
        return r;
    }
}
