using System.Security.Cryptography;
using System.Text;

namespace SpamDrafter.Core.Knowledge;

/// <summary>
/// Heure de Paris sans dépendre de la base de fuseaux de l'ordinateur : l'heure d'été européenne (UTC+2) court du dernier dimanche
/// de mars 01:00 UTC au dernier dimanche d'octobre 01:00 UTC ; le reste de l'année, UTC+1.
/// </summary>
public static class ParisClock
{
    private static DateTime LastSunday(int year, int month)
    {
        var d = new DateTime(year, month, DateTime.DaysInMonth(year, month), 1, 0, 0, DateTimeKind.Utc);
        while (d.DayOfWeek != DayOfWeek.Sunday) d = d.AddDays(-1);
        return d;
    }

    public static TimeSpan OffsetAt(DateTime utc)
    {
        utc = utc.ToUniversalTime();
        return utc >= LastSunday(utc.Year, 3) && utc < LastSunday(utc.Year, 10) ? TimeSpan.FromHours(2) : TimeSpan.FromHours(1);
    }

    /// <summary>L'heure qu'il est à Paris.</summary>
    public static DateTime Now(DateTime utc) => utc.ToUniversalTime() + OffsetAt(utc);

    /// <summary>La date du jour à Paris.</summary>
    public static DateOnly Today(DateTime utc) => DateOnly.FromDateTime(Now(utc));

    /// <summary>Le prochain minuit à Paris, en UTC (moment où la carte du jour change). Les changements d'heure ont lieu à 01:00 UTC, jamais à minuit.</summary>
    public static DateTime NextMidnightUtc(DateTime utc)
    {
        var tomorrow = Today(utc).AddDays(1).ToDateTime(TimeOnly.MinValue);   // minuit, heure de Paris
        var probe = DateTime.SpecifyKind(tomorrow - TimeSpan.FromHours(1), DateTimeKind.Utc);
        return DateTime.SpecifyKind(tomorrow - OffsetAt(probe), DateTimeKind.Utc);
    }
}

/// <summary>
/// Carte du jour : la même pour tous les joueurs (elle ne dépend que de la date à Paris et de la liste des cartes), et elle change
/// chaque jour à 00:00 heure de Paris. Le choix n'utilise pas <see cref="Random"/> (dont le résultat peut changer d'une version de .NET
/// à l'autre) mais un hachage SHA-256 de la date.
/// </summary>
public static class CardOfTheDay
{
    public static int IndexFor(DateOnly day, int count)
    {
        if (count <= 0) return -1;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("spamdrafter-carte-du-jour-" + day.ToString("yyyy-MM-dd")));
        return (int)(BitConverter.ToUInt32(hash, 0) % (uint)count);
    }

    /// <summary>La carte du jour parmi les identifiants de cartes (triés pour que l'ordre de chargement ne compte pas).</summary>
    public static string? Pick(IEnumerable<string> cardIds, DateTime utcNow)
    {
        var ids = cardIds.OrderBy(i => i, StringComparer.Ordinal).ToList();
        var i = IndexFor(ParisClock.Today(utcNow), ids.Count);
        return i < 0 ? null : ids[i];
    }
}
