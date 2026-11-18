using System.Text.RegularExpressions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.App.ViewModels;

/// <summary>Un morceau de description : du texte (« \n » = retour à la ligne), éventuellement coloré, ou une icône.</summary>
public sealed record RichSegment(string Text, IBrush? Brush = null, bool Bold = false, Bitmap? Icon = null);

/// <summary>
/// Description d'une capacité « à la manière du jeu » : les dégâts d'un élément (« 25 Feu ») prennent la couleur et l'icône de cet élément,
/// les effets (Éternité, Disparition…) sont en couleur, « critique » en jaune. Le texte lui-même ne change pas.
/// </summary>
public static class AbilityRichText
{
    /// <summary>Clés de texte des effets du jeu mis en couleur (« Keyword.Title.&lt;clé&gt; »).</summary>
    private static readonly string[] StatusKeys =
    {
        "eternity", "temporality", "vanish", "freeze", "poison", "stun", "erosion", "echo", "absorb", "camouflage",
        "vampirism", "provocation", "purge", "smoked",
    };

    private static readonly IBrush StatusBrush = new SolidColorBrush(Color.Parse("#F5B85A"));
    private static readonly IBrush CritBrush = new SolidColorBrush(Color.Parse("#FFD84A"));

    /// <summary>Couleur d'un élément dans le texte (choix de présentation : le feu rouge, la lumière jaune…).</summary>
    public static IBrush ElementBrush(string? elementId) => new SolidColorBrush(Color.Parse(elementId switch
    {
        "fire" => "#FF5A4D", "water" => "#4FB3FF", "earth" => "#C9915C", "nature" => "#6FD65A",
        "light" => "#FFD84A", "shadow" => "#B98BFF", "divine" => "#FF9AE6", _ => "#FFFFFF",
    }));

    public static IReadOnlyList<RichSegment> Build(KnowledgeBase kb, string rich, IReadOnlyList<ToxEntry>? tox = null)
    {
        // Les Tox de la capacité prennent la couleur de leur rareté ; les autres effets, l'or.
        var toxBrush = new Dictionary<string, IBrush>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tox ?? Array.Empty<ToxEntry>()) toxBrush[kb.ToxName(t)] = Icons.FrameBrush(kb, t.Rarity);
        var names = StatusKeys.Select(k => kb.Text("Keyword.Title." + k))
            .Where(n => n.Length > 0 && !n.StartsWith("Keyword.Title.", StringComparison.Ordinal))
            .Concat(toxBrush.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(Regex.Escape).ToList();
        var o = DescriptionFormatter.ElementOpen; var c = DescriptionFormatter.ElementClose;
        var pattern = $@"(?:(?<num>\[?\d+(?:\s+à\s+\d+)?\]?)\s+)?{o}(?<el>[a-z]+){c}"
                      + (names.Count > 0 ? $@"|(?<kw>(?<!\p{{L}})(?:{string.Join("|", names)})s?(?!\p{{L}}))" : "")
                      + @"|(?<crit>(?<!\p{L})critiques?(?!\p{L}))";
        var rx = new Regex(pattern, RegexOptions.IgnoreCase);

        var result = new List<RichSegment>();
        var pos = 0;
        foreach (Match m in rx.Matches(rich))
        {
            if (m.Index > pos) result.Add(new RichSegment(rich[pos..m.Index]));
            pos = m.Index + m.Length;
            if (m.Groups["el"].Success)
            {
                var id = m.Groups["el"].Value;
                var brush = ElementBrush(id);
                if (m.Groups["num"].Success) result.Add(new RichSegment(m.Groups["num"].Value + " "));
                var icon = Icons.Element(kb, id, 48);
                if (icon is not null) result.Add(new RichSegment("", Icon: icon));
                result.Add(new RichSegment((icon is null ? "" : " ") + kb.ElementName(id), brush, true));
            }
            else if (m.Groups["kw"].Success)
                result.Add(new RichSegment(m.Value, toxBrush.GetValueOrDefault(m.Value) ?? toxBrush.GetValueOrDefault(m.Value.TrimEnd('s', 'S')) ?? StatusBrush, true));
            else result.Add(new RichSegment(m.Value, CritBrush, true));
        }
        if (pos < rich.Length) result.Add(new RichSegment(rich[pos..]));
        return result;
    }
}
