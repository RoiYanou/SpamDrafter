using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace SpamDrafter.App.Views;

/// <summary>
/// Fiche de deck en texte enrichi : titres de section en jaune, note mise en avant, gains en vert et pertes en rouge, pourcentages en
/// cyan, conseils en couleur. Le texte reste le même (sélectionnable, copiable) : seules les couleurs et les graisses changent.
/// Comparaison de deux decks : <see cref="NameA"/> et <see cref="NameB"/> sont les noms des decks, mis en couleur (A, B) partout où ils apparaissent.
/// </summary>
public sealed class RichReport : SelectableTextBlock
{
    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<RichReport, string?>(nameof(Source));
    public string? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public static readonly StyledProperty<string?> NameAProperty = AvaloniaProperty.Register<RichReport, string?>(nameof(NameA));
    public static readonly StyledProperty<string?> NameBProperty = AvaloniaProperty.Register<RichReport, string?>(nameof(NameB));
    /// <summary>Nom du deck A : coloré comme <see cref="CompareColors.A"/> dans le texte.</summary>
    public string? NameA { get => GetValue(NameAProperty); set => SetValue(NameAProperty, value); }
    /// <summary>Nom du deck B : coloré comme <see cref="CompareColors.B"/> dans le texte.</summary>
    public string? NameB { get => GetValue(NameBProperty); set => SetValue(NameBProperty, value); }
    public static readonly StyledProperty<string?> TipAProperty = AvaloniaProperty.Register<RichReport, string?>(nameof(TipA));
    public static readonly StyledProperty<string?> TipBProperty = AvaloniaProperty.Register<RichReport, string?>(nameof(TipB));
    /// <summary>Le vrai nom du deck A, montré au survol de <see cref="NameA"/> (« Deck A ») dans le texte.</summary>
    public string? TipA { get => GetValue(TipAProperty); set => SetValue(TipAProperty, value); }
    public string? TipB { get => GetValue(TipBProperty); set => SetValue(TipBProperty, value); }

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
        + @"|(?<grade>Note : \d+ / 100 — (?<gname>" + NoteColors.Pattern + @"))"
        + @"|(?<note>Note : [^—\n]+)"
        + @"|(?<up>\+\d+(?:[.,]\d+)? ?pts?)"
        + @"|(?<down>[−-]\d+(?:[.,]\d+)? ?pts?)"
        + @"|(?<pct>\d+(?:[.,]\d+)? ?%)"
        + @"|(?<good>Protège-la\.?)"
        + @"|(?<warn>À remplacer(?: si tu as mieux)?\.?)");

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty || change.Property == NameAProperty || change.Property == NameBProperty || change.Property == TipAProperty || change.Property == TipBProperty) Build(Source ?? "");
    }

    /// <summary>Les noms des decks A et B, du plus long au plus court, ou null s'il n'y en a pas.</summary>
    private Regex? NameRegex()
    {
        var names = new[] { NameA, NameB }.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(n => n.Length).Select(Regex.Escape).ToList();
        return names.Count == 0 ? null
            : new Regex(@"(?<![\p{L}\p{N}])(?:" + string.Join("|", names) + @")(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private IBrush? NameBrush(string name)
    {
        if (!string.IsNullOrWhiteSpace(NameA) && string.Equals(name, NameA.Trim(), StringComparison.OrdinalIgnoreCase)) return CompareColors.A;
        if (!string.IsNullOrWhiteSpace(NameB) && string.Equals(name, NameB.Trim(), StringComparison.OrdinalIgnoreCase)) return CompareColors.B;
        return null;
    }

    private void Build(string text)
    {
        Inlines ??= new InlineCollection();
        Inlines.Clear();
        var names = NameRegex();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (Heading.IsMatch(line.Trim()) && !line.Contains('|'))
                AddSegments(line.Trim(), names, piece => Inlines!.Add(new Run(piece) { Foreground = Yellow, FontWeight = FontWeight.Bold, FontSize = FontSize + 4 }), nameSize: FontSize + 4);
            else AddLine(line, names);
            if (i < lines.Length - 1) Inlines.Add(new LineBreak());
        }
    }

    /// <summary>Découpe <paramref name="text"/> aux noms de decks : un nom devient un passage en couleur, le reste passe par <paramref name="plain"/>.</summary>
    private void AddSegments(string text, Regex? names, Action<string> plain, double? nameSize = null)
    {
        var pos = 0;
        if (names is not null)
            foreach (Match m in names.Matches(text))
            {
                if (NameBrush(m.Value) is not { } brush) continue;
                if (m.Index > pos) plain(text[pos..m.Index]);
                // Un petit bloc de texte (et non un simple passage) : il peut porter une info-bulle avec le vrai nom du deck.
                var isA = brush == CompareColors.A;
                var label = new TextBlock { Text = m.Value, Foreground = brush, FontWeight = FontWeight.Bold, FontSize = nameSize ?? FontSize };
                if ((isA ? TipA : TipB) is { Length: > 0 } tip) ToolTip.SetTip(label, tip);
                label.RenderTransform = new TranslateTransform(0, 6);   // le bloc est posé un peu trop haut sur la ligne : on le redescend sur la ligne de base du texte
                Inlines!.Add(new InlineUIContainer(label) { BaselineAlignment = BaselineAlignment.Baseline });
                pos = m.Index + m.Length;
            }
        if (pos < text.Length) plain(text[pos..]);
    }

    private void AddLine(string line, Regex? names)
    {
        if (line.StartsWith("• ")) { Inlines!.Add(new Run("• ") { Foreground = Cyan, FontWeight = FontWeight.Bold }); line = line[2..]; }
        AddSegments(line, names, AddTokens);
    }

    private void AddTokens(string line)
    {
        var pos = 0;
        foreach (Match m in Token.Matches(line))
        {
            if (m.Index < pos) continue;
            if (m.Index > pos) Inlines!.Add(new Run(line[pos..m.Index]));
            if (m.Groups["grade"].Success)
            {
                // « Note : 39 / 100 — Passable » : la note et son niveau prennent la couleur du niveau.
                var gb = NoteColors.For(m.Groups["gname"].Value);
                var cut = m.Value.LastIndexOf(" — ", StringComparison.Ordinal);
                Inlines!.Add(new Run(m.Value[..cut]) { Foreground = gb, FontWeight = FontWeight.Bold, FontSize = FontSize + 3 });
                Inlines.Add(new Run(" — "));
                Inlines.Add(new Run(m.Groups["gname"].Value) { Foreground = gb, FontWeight = FontWeight.Bold });
                pos = m.Index + m.Length;
                continue;
            }
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
