using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace SpamDrafter.App.Views;

/// <summary>Texte dont les passages entre doubles accolades {{comme ceci}} sont mis en couleur (orange, en gras).</summary>
public sealed class MarkedText : TextBlock
{
    protected override Type StyleKeyOverride => typeof(TextBlock);

    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<MarkedText, string?>(nameof(Source));
    public string? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    private static readonly IBrush Highlight = new SolidColorBrush(Color.Parse("#FF9F5A"));
    private static readonly Regex Mark = new(@"\{\{(.*?)\}\}", RegexOptions.Singleline);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SourceProperty) return;
        Inlines ??= new InlineCollection();
        Inlines.Clear();
        var text = Source ?? "";
        var pos = 0;
        foreach (Match m in Mark.Matches(text))
        {
            if (m.Index > pos) Inlines.Add(new Run(text[pos..m.Index]));
            Inlines.Add(new Run(m.Groups[1].Value) { Foreground = Highlight, FontWeight = FontWeight.Bold });
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) Inlines.Add(new Run(text[pos..]));
    }
}
