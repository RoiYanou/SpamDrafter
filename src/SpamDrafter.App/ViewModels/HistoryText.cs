using System.Text.RegularExpressions;

namespace SpamDrafter.App.ViewModels;

/// <summary>Textes communs au Générateur et à Tester : dates de l'historique et rapport en texte lisible.</summary>
public static partial class HistoryText
{
    /// <summary>« aujourd'hui à 14:32 », « hier à 09:10 » ou « le 12/11 à 18:00 ».</summary>
    public static string When(DateTime utc)
    {
        var local = utc.ToLocalTime();
        var day = local.Date == DateTime.Today ? "aujourd'hui" : local.Date == DateTime.Today.AddDays(-1) ? "hier" : $"le {local:dd/MM}";
        return $"{day} à {local:HH:mm}";
    }

    /// <summary>Le rapport Markdown en texte lisible (sans les marques ** et # ; tableaux gardés).</summary>
    public static string ToPlain(string markdown)
    {
        var text = markdown.Replace("**", "");
        text = Heading().Replace(text, m => m.Groups[1].Value.ToUpperInvariant());
        return Separator().Replace(text, "");
    }

    [GeneratedRegex(@"^#{1,3} (.*)$", RegexOptions.Multiline)]
    private static partial Regex Heading();
    [GeneratedRegex(@"^\|(---\|)+\r?\n", RegexOptions.Multiline)]
    private static partial Regex Separator();
}
