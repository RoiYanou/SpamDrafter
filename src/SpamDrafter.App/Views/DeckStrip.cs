using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SpamDrafter.App.ViewModels;

namespace SpamDrafter.App.Views;

/// <summary>
/// Fond stylisé d'un deck : les illustrations de ses cartes (brutes, sans cadre), découpées en bandes obliques et recollées côte à côte
/// en diagonale, avec un voile sombre à gauche pour que le nom reste lisible.
/// </summary>
public sealed class DeckStrip : Control
{
    public static readonly StyledProperty<IReadOnlyList<DeckChip>?> ChipsProperty = AvaloniaProperty.Register<DeckStrip, IReadOnlyList<DeckChip>?>(nameof(Chips));
    public IReadOnlyList<DeckChip>? Chips { get => GetValue(ChipsProperty); set => SetValue(ChipsProperty, value); }

    private readonly List<CardItem> _watched = new();

    static DeckStrip() => AffectsRender<DeckStrip>(ChipsProperty);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ChipsProperty) return;
        Unwatch();
        foreach (var c in Chips ?? Array.Empty<DeckChip>())
            if (c.Card is { } card) { card.PropertyChanged += OnCardChanged; _watched.Add(card); }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Unwatch(); base.OnDetachedFromVisualTree(e); }
    private void Unwatch() { foreach (var c in _watched) c.PropertyChanged -= OnCardChanged; _watched.Clear(); }
    // Les miniatures se chargent en arrière-plan : le fond se redessine quand l'une d'elles arrive (ou quand le skin change).
    private void OnCardChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(CardItem.Thumbnail)) InvalidateVisual(); }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 4 || h < 4) return;
        var cards = (Chips ?? Array.Empty<DeckChip>()).Where(c => c.Card is not null).Select(c => c.Card!).ToList();
        using var _ = ctx.PushClip(new RoundedRect(new Rect(0, 0, w, h), 10));
        ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#2B1B52")), null, new Rect(0, 0, w, h));
        var n = cards.Count;
        if (n == 0) return;
        var slant = h * 0.7;                    // inclinaison des coupes
        var step = (w + slant) / n;
        for (var i = 0; i < n; i++)
        {
            double x0 = i * step, x1 = (i + 1) * step;
            // Bande oblique : le haut va de x0 à x1, le bas est décalé vers la gauche de « slant ».
            var strip = new PolylineGeometry(new[] { new Point(x0, 0), new Point(x1, 0), new Point(x1 - slant, h), new Point(x0 - slant, h) }, true);
            using var clip = ctx.PushGeometryClip(strip);
            if (cards[i].Thumbnail is { } bmp)
            {
                var dest = new Rect(x0 - slant, 0, step + slant, h);
                double bw = bmp.PixelSize.Width, bh = bmp.PixelSize.Height;
                var destAspect = dest.Width / dest.Height;
                double sw = bw, sh = bw / destAspect;
                if (sh > bh) { sh = bh; sw = bh * destAspect; }
                // Cadrage sur le centre de l'illustration (un peu au-dessus du milieu : c'est là que se trouve le sujet).
                var src = new Rect((bw - sw) / 2, (bh - sh) * 0.4, sw, sh);
                ctx.DrawImage(bmp, src, dest);
            }
        }
        // Traits clairs le long des coupes.
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1.5);
        for (var i = 1; i < n; i++) ctx.DrawLine(pen, new Point(i * step, 0), new Point(i * step - slant, h));
        // Voile sombre à gauche : le nom du deck reste lisible.
        var veil = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(215, 18, 10, 36), 0), new GradientStop(Color.FromArgb(150, 18, 10, 36), 0.55), new GradientStop(Color.FromArgb(70, 18, 10, 36), 1) },
        };
        ctx.DrawRectangle(veil, null, new Rect(0, 0, w, h));
    }
}
