using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SpamDrafter.App.Views;

/// <summary>
/// Illustration des éléments d'une capacité, comme dans le jeu : une icône seule, ou un disque partagé en parts égales (2, 3 ou 6 éléments),
/// chaque part montrant l'icône de son élément. Pour 2 éléments : moitié gauche / moitié droite ; pour 3 : parts de 120° (gauche, haut-droite, bas).
/// </summary>
public sealed class ElementCombo : Control
{
    public static readonly StyledProperty<IReadOnlyList<IImage>?> IconsProperty = AvaloniaProperty.Register<ElementCombo, IReadOnlyList<IImage>?>(nameof(Icons));
    public IReadOnlyList<IImage>? Icons { get => GetValue(IconsProperty); set => SetValue(IconsProperty, value); }

    static ElementCombo()
    {
        AffectsRender<ElementCombo>(IconsProperty);
        AffectsMeasure<ElementCombo>(IconsProperty);
    }

    public override void Render(DrawingContext context)
    {
        if (Icons is not { Count: > 0 } icons) return;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var box = new Rect((Bounds.Width - size) / 2, (Bounds.Height - size) / 2, size, size);
        if (icons.Count == 1) { Draw(context, icons[0], box); return; }
        var center = box.Center;
        var radius = size / 2;
        var n = icons.Count;
        var step = 2 * Math.PI / n;
        // Première part : 2 éléments, côté gauche (départ en bas) ; 3 éléments, à gauche (départ à 150°) ; 6 éléments, départ en haut.
        var start = n == 2 ? Math.PI / 2 : n == 3 ? 5 * Math.PI / 6 : -Math.PI / 2;
        for (var i = 0; i < n; i++)
        {
            var a0 = start + i * step;
            var a1 = a0 + step;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(center, true);
                g.LineTo(new Point(center.X + radius * Math.Cos(a0), center.Y + radius * Math.Sin(a0)));
                g.ArcTo(new Point(center.X + radius * Math.Cos(a1), center.Y + radius * Math.Sin(a1)), new Size(radius, radius), 0, step > Math.PI, SweepDirection.Clockwise);
                g.EndFigure(true);
            }
            using (context.PushGeometryClip(geo)) Draw(context, icons[i], box);
        }
    }

    private static void Draw(DrawingContext context, IImage icon, Rect box)
    {
        var s = icon.Size;
        context.DrawImage(icon, new Rect(0, 0, s.Width, s.Height), box);
    }
}
