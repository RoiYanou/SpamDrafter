using System.Text.Json;

namespace SpamDrafter.Core.Games;

/// <summary>Une ligne du détail d'une partie enregistrée (une réponse, une question…). <c>Mark</c> : 1 = juste, −1 = fausse, 0 = sans verdict.</summary>
public sealed class DailyHistoryLine
{
    public string Text { get; set; } = "";
    public string Detail { get; set; } = "";
    public int Mark { get; set; }
}

/// <summary>Une partie terminée d'un jeu quotidien (énigme, détail, quizz), enregistrée avec sa date.</summary>
public sealed class DailyHistoryEntry
{
    /// <summary>Identifiant du jeu (<see cref="RiddleGame.GameId"/>, <see cref="DetailGame.GameId"/>, <see cref="QuizGame.GameId"/>).</summary>
    public string Game { get; set; } = "";
    /// <summary>Jour de la partie (« 2026-11-20 », heure de Paris).</summary>
    public string Day { get; set; } = "";
    public DateTime PlayedUtc { get; set; }
    /// <summary>Résultat en une phrase : « Trouvé en 3 essais », « 4 sur 5 », « Abandon »…</summary>
    public string Headline { get; set; } = "";
    /// <summary>Partie réussie (pour colorer la ligne).</summary>
    public bool Good { get; set; }
    /// <summary>Carte à trouver (énigme, détail), pour la vignette.</summary>
    public string? CardId { get; set; }
    public string CardName { get; set; } = "";
    public List<DailyHistoryLine> Lines { get; set; } = new();
}

/// <summary>
/// L'historique des jeux quotidiens du joueur (<c>daily-history.json</c> à côté des réglages) : une entrée par jeu et par jour, la plus récente d'abord.
/// Le tirage du jour a son propre historique (<see cref="RngHistory"/>). Un fichier absent ou illisible donne un historique vide.
/// </summary>
public sealed class DailyHistory
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public List<DailyHistoryEntry> Entries { get; set; } = new();

    public static string DefaultPath(string settingsPath) => Path.Combine(Path.GetDirectoryName(settingsPath)!, "daily-history.json");

    public static DailyHistory Load(string path)
    {
        try { if (File.Exists(path)) return JsonSerializer.Deserialize<DailyHistory>(File.ReadAllText(path), Options) ?? new(); }
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

    /// <summary>Ajoute la partie d'un jeu pour un jour (ou remplace celle du même jeu et du même jour).</summary>
    public void Record(DailyHistoryEntry entry)
    {
        Entries.RemoveAll(e => e.Game == entry.Game && e.Day == entry.Day);
        Entries.Add(entry);
        Entries = Entries.OrderByDescending(e => e.Day, StringComparer.Ordinal).ThenByDescending(e => e.PlayedUtc).ToList();
    }

    /// <summary>Les parties d'un jeu, les plus récentes d'abord.</summary>
    public IEnumerable<DailyHistoryEntry> For(string game) => Entries.Where(e => e.Game == game);
}
