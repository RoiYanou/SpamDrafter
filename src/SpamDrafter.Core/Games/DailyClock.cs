namespace SpamDrafter.Core.Games;

/// <summary>
/// Le « jour » des jeux quotidiens : la date à Paris (D-095). Tous les joueurs changent de jeu au même instant, quel que soit le fuseau de leur PC.
/// </summary>
public static class DailyClock
{
    /// <summary>Premier jour du calendrier des jeux (numéro 0).</summary>
    public static readonly DateOnly Epoch = new(2026, 1, 1);

    private static readonly Lazy<TimeZoneInfo?> Paris = new(() =>
    {
        foreach (var id in new[] { "Europe/Paris", "Romance Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        }
        return null;
    });

    /// <summary>La date à Paris à cet instant. Sans fuseau connu sur la machine : règle européenne écrite à la main (UTC+1, UTC+2 du dernier dimanche de mars au dernier dimanche d'octobre, à 1 h UTC).</summary>
    public static DateOnly DayOf(DateTimeOffset instant)
    {
        var utc = instant.UtcDateTime;
        if (Paris.Value is { } tz) return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, tz));
        var start = LastSunday(utc.Year, 3).AddHours(1);
        var end = LastSunday(utc.Year, 10).AddHours(1);
        return DateOnly.FromDateTime(utc.AddHours(utc >= start && utc < end ? 2 : 1));
    }

    public static DateOnly Today() => DayOf(DateTimeOffset.UtcNow);

    /// <summary>Numéro du jour depuis <see cref="Epoch"/>.</summary>
    public static int Number(DateOnly day) => day.DayNumber - Epoch.DayNumber;

    /// <summary>Clé écrite dans les fichiers : « 2026-11-20 ».</summary>
    public static string Key(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static DateTime LastSunday(int year, int month)
    {
        var d = new DateTime(year, month, DateTime.DaysInMonth(year, month), 0, 0, 0, DateTimeKind.Utc);
        while (d.DayOfWeek != DayOfWeek.Sunday) d = d.AddDays(-1);
        return d;
    }
}

/// <summary>
/// Tirage pseudo-aléatoire <b>identique sur tous les PC et dans toutes les versions de .NET</b> (SplitMix64) : indispensable pour que
/// « le jeu du jour » soit le même pour tous. Ne pas remplacer par <see cref="Random"/>, dont l'algorithme n'est pas garanti.
/// </summary>
public sealed class SeededRandom
{
    private ulong _state;
    public SeededRandom(ulong seed) { _state = seed; }

    public ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Entier de 0 (inclus) à <paramref name="max"/> (exclus).</summary>
    public int Next(int max) => (int)(NextUInt64() % (ulong)max);

    /// <summary>Graine stable à partir d'un texte (FNV-1a 64 bits ; <see cref="string.GetHashCode()"/> change à chaque lancement).</summary>
    public static ulong SeedOf(string text)
    {
        var h = 14695981039346656037UL;
        foreach (var c in text) { h ^= c; h *= 1099511628211UL; }
        return h;
    }
}
