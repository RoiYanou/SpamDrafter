using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using SpamDrafter.App.ViewModels;

namespace SpamDrafter.App.Views;

/// <summary>Texte fait de morceaux colorés et d'icônes (<see cref="RichSegment"/>), avec retours à la ligne automatiques.</summary>
public sealed class SegmentText : TextBlock
{
    protected override Type StyleKeyOverride => typeof(TextBlock);

    public static readonly StyledProperty<IReadOnlyList<RichSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<SegmentText, IReadOnlyList<RichSegment>?>(nameof(Segments));
    public IReadOnlyList<RichSegment>? Segments { get => GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SegmentsProperty || change.Property == FontSizeProperty) Build();
    }

    private void Build()
    {
        Inlines ??= new InlineCollection();
        Inlines.Clear();
        foreach (var s in Segments ?? Array.Empty<RichSegment>())
        {
            if (s.Icon is not null)
            {
                var h = FontSize + 4;
                Inlines.Add(new InlineUIContainer(new Image { Source = s.Icon, Height = h, Width = h, Stretch = Stretch.Uniform, Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }));
                continue;
            }
            var parts = s.Text.Replace("\r", "").Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0) Inlines.Add(new LineBreak());
                if (parts[i].Length == 0) continue;
                var run = new Run(parts[i]) { FontWeight = s.Bold ? FontWeight.Bold : FontWeight.Normal };
                if (s.Brush is not null) run.Foreground = s.Brush;   // sans couleur : celle du texte autour (ne jamais la mettre à null)
                Inlines.Add(run);
            }
        }
    }
}
