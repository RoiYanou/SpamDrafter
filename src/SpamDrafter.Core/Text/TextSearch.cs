using System.Globalization;
using System.Text;

namespace SpamDrafter.Core.Text;

/// <summary>
/// Recherche de texte tolérante (phase 12, lot A) : sans accents, sans majuscules, et les tirets, apostrophes et
/// espaces multiples comptent pareil (« piou piou piou » trouve « Piou-piou-piou »).
/// </summary>
public static class TextSearch
{
    /// <summary>Forme de comparaison d'un texte : minuscules, sans accents, séparateurs ramenés à un espace.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastWasSpace = true;
        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark) continue;   // l'accent lui-même
            var c = char.ToLowerInvariant(ch);
            switch (c)
            {
                case 'œ': AppendText("oe"); continue;
                case 'æ': AppendText("ae"); continue;
            }
            if (char.IsLetterOrDigit(c)) { Append(c); continue; }
            if (!lastWasSpace) { sb.Append(' '); lastWasSpace = true; }
        }
        return sb.ToString().TrimEnd();

        void AppendText(string s) { sb.Append(s); lastWasSpace = false; }
        void Append(char c) { sb.Append(c); lastWasSpace = false; }
    }

    /// <summary>Vrai si chaque mot de la recherche apparaît dans le texte (déjà normalisé par <see cref="Normalize"/>).</summary>
    public static bool ContainsAllWords(string normalizedHaystack, string? query)
    {
        var q = Normalize(query);
        if (q.Length == 0) return true;
        foreach (var word in q.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (!normalizedHaystack.Contains(word, StringComparison.Ordinal)) return false;
        return true;
    }
}
