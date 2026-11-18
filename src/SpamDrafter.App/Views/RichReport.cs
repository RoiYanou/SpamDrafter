using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace SpamDrafter.App.Views;

/// <summary>
/// Fiche de deck en texte enrichi : titres de section en jaune, note mise en avant, gains en vert et pertes en rouge, pourcentages en
/// cyan, conseils en couleur. Le texte reste le même (sélectionnable, copiable) : seules les couleurs et les graisses changent.
/// </summary>
public sealed class RichReport : SelectableTextBlock
{
    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<RichReport, string?>(nameof(Source));
    public string? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    private static IBrush Yellow => new SolidColorBrush(Views.ThemeManager.Accent);   // couleur secondaire du thème en cours
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#82E9A8"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#FF8A8A"));
    private static readonly IBrush Cyan = new SolidColorBrush(Color.Parse("#7DE3FF"));
    private static readonly IBrush Orange = new SolidColorBrush(Color.Parse("#FF9F5A"));
    private static readonly IBrush Pink = new SolidColorBrush(Color.Parse("#FF9AE0"));
    private static readonly IBrush White = Brushes.White;

    private static readonly Regex Heading = new(@"^[A-ZÀ-ÖØ-Þ0-9][A-ZÀ-ÖØ-Þ0-9 '’\-/(),:]{3,}$");
    private static readonly Regex Token = new(
        @"(?<label>Carte clé|Carte la plus faible|Carte qui encaisse)"
        + @"|(?<note>Note : [^—\n]+)"
        + @"|(?<up>\+\d+(?:[.,]\d+)? ?pts?)"
        + @"|(?<down>[−-]\d+(?:[.,]\d+)? ?pts?)"
        + @"|(?<pct>\d+(?:[.,]\d+)? ?%)"
        + @"|(?<good>Protège-la\.?)"
        + @"|(?<warn>À remplacer(?: si tu as mieux)?\.?)");

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty) Build(Source ?? "");
    }

    private void Build(string text)
    {
        Inlines ??= new InlineCollection();
        Inlines.Clear();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (Heading.IsMatch(line.Trim()) && !line.Contains('|'))
                Inlines.Add(new Run(line.Trim()) { Foreground = Yellow, FontWeight = FontWeight.Bold, FontSize = FontSize + 4 });
            else AddLine(line);
            if (i < lines.Length - 1) Inlines.Add(new LineBreak());
        }
    }

    private void AddLine(string line)
    {
        var pos = 0;
        if (line.StartsWith("• ")) { Inlines!.Add(new Run("• ") { Foreground = Cyan, FontWeight = FontWeight.Bold }); pos = 2; }
        foreach (Match m in Token.Matches(line))
        {
            if (m.Index < pos) continue;
            if (m.Index > pos) Inlines!.Add(new Run(line[pos..m.Index]));
            var (brush, size) = Style(m);
            Inlines!.Add(new Run(m.Value) { Foreground = brush, FontWeight = FontWeight.Bold, FontSize = size });
            pos = m.Index + m.Length;
        }
        if (pos < line.Length) Inlines!.Add(new Run(line[pos..]));
    }

    private (IBrush, double) Style(Match m)
    {
        if (m.Groups["label"].Success)
            return (m.Value.Contains("clé") ? Green : m.Value.Contains("faible") ? Red : Cyan, FontSize);
        if (m.Groups["note"].Success) return (White, FontSize + 3);
        if (m.Groups["up"].Success) return (Green, FontSize);
        if (m.Groups["down"].Success) return (Red, FontSize);
        if (m.Groups["pct"].Success) return (Cyan, FontSize);
        if (m.Groups["good"].Success) return (Green, FontSize);
        return (Orange, FontSize);
    }
}
