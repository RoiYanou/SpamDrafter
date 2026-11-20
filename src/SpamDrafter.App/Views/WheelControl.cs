using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using SpamDrafter.App.ViewModels;

namespace SpamDrafter.App.Views;

/// <summary>
/// La roue des challenges : une part colorée par challenge actif (largeur = poids), une flèche fixe en haut, et un tour animé
/// qui ralentit jusqu'à laisser la part gagnante sous la flèche. L'angle est en tours (1 = un tour complet, sens des aiguilles d'une montre).
/// </summary>
public sealed class WheelControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<WheelSlice>?> SlicesProperty = AvaloniaProperty.Register<WheelControl, IReadOnlyList<WheelSlice>?>(nameof(Slices));
    public static readonly StyledProperty<double> AngleProperty = AvaloniaProperty.Register<WheelControl, double>(nameof(Angle));

    public IReadOnlyList<WheelSlice>? Slices { get => GetValue(SlicesProperty); set => SetValue(SlicesProperty, value); }
    public double Angle { get => GetValue(AngleProperty); set => SetValue(AngleProperty, value); }

    static WheelControl() => AffectsRender<WheelControl>(SlicesProperty, AngleProperty);

    private DispatcherTimer? _timer;
    private Action? _done;

    /// <summary>Durée du tour (la roue ralentit sur toute cette durée).</summary>
    public static readonly TimeSpan SpinDuration = TimeSpan.FromSeconds(5.5);

    /// <summary>
    /// Fait tourner la roue (au moins 5 tours) pour que le point <paramref name="fraction"/> (0 à 1 le long du tour, tel que dessiné à angle 0)
    /// finisse sous la flèche, puis appelle <paramref name="done"/>. Avec « Désactiver les animations », elle saute directement.
    /// </summary>
    public void SpinTo(double fraction, Action done)
    {
        _timer?.Stop();
        _done = done;
        var start = Angle;
        var delta = ((-fraction - start) % 1 + 1) % 1;
        var end = start + 5 + delta;
        if (Motion.Reduce)
        {
            Angle = end; Finish();
            return;
        }
        var began = DateTime.UtcNow;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) =>
        {
            var t = Math.Clamp((DateTime.UtcNow - began) / SpinDuration, 0, 1);
            var eased = 1 - Math.Pow(1 - t, 4);   // départ rapide, arrêt doux
            Angle = start + (end - start) * eased;
            if (t >= 1) { _timer!.Stop(); Finish(); }
        };
        _timer.Start();
    }

    private void Finish() { var d = _done; _done = null; d?.Invoke(); }

    public override void Render(DrawingContext context)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size < 40) return;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2 + 8);
        var radius = size / 2 - 22;
        var slices = Slices ?? Array.Empty<WheelSlice>();

        // Disque de fond et anneau.
        context.DrawEllipse(new SolidColorBrush(Color.Parse("#2A1B52")), new Pen(new SolidColorBrush(Color.Parse("#FFD84D")), 5), center, radius + 4, radius + 4);
        if (slices.Count == 0)
        {
            DrawText(context, "Aucun challenge actif", center.X - radius * 0.6, center.Y - radius * 0.45, radius * 1.2, 15, Colors.White, TextAlignment.Center);
        }
        foreach (var s in slices)
        {
            var span = s.End - s.Start;
            if (span <= 0) continue;
            var brush = new SolidColorBrush(Color.Parse(s.Color));
            var edge = new Pen(new SolidColorBrush(Color.Parse("#1A1033")), 1.5);
            if (span >= 0.9999) { context.DrawEllipse(brush, edge, center, radius, radius); continue; }
            var a0 = -Math.PI / 2 + 2 * Math.PI * (s.Start + Angle);
            var a1 = -Math.PI / 2 + 2 * Math.PI * (s.End + Angle);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(center, true);
                g.LineTo(new Point(center.X + radius * Math.Cos(a0), center.Y + radius * Math.Sin(a0)));
                g.ArcTo(new Point(center.X + radius * Math.Cos(a1), center.Y + radius * Math.Sin(a1)), new Size(radius, radius), 0, a1 - a0 > Math.PI, SweepDirection.Clockwise);
                g.EndFigure(true);
            }
            context.DrawGeometry(brush, edge, geo);
        }
        // Textes (après les parts, pour qu'aucune part ne les recouvre).
        foreach (var s in slices)
        {
            var span = s.End - s.Start;
            if (span < 0.028) continue;   // part trop fine pour un texte : le nom est dans la liste
            var mid = -Math.PI / 2 + 2 * Math.PI * ((s.Start + s.End) / 2 + Angle);
            // Le texte suit toujours le rayon, dans le même sens (de l'intérieur vers le bord) : il ne se retourne jamais, même à l'envers.
            var fontSize = Math.Clamp(radius * 0.075, 9, 15);
            var width = radius * 0.66;
            using (context.PushTransform(Matrix.CreateRotation(mid) * Matrix.CreateTranslation(center.X, center.Y)))
                DrawText(context, s.Label, radius * 0.3, -fontSize * 0.7, width, fontSize, Color.Parse("#1A1033"), TextAlignment.Left);
        }
        // Moyeu et flèche fixe.
        context.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.Parse("#1A1033")), 3), center, radius * 0.09, radius * 0.09);
        var tip = new Point(center.X, center.Y - radius + 12);
        var arrow = new StreamGeometry();
        using (var g = arrow.Open())
        {
            g.BeginFigure(new Point(tip.X - 14, tip.Y - 30), true);
            g.LineTo(new Point(tip.X + 14, tip.Y - 30));
            g.LineTo(tip);
            g.EndFigure(true);
        }
        context.DrawGeometry(new SolidColorBrush(Color.Parse("#FF5C7A")), new Pen(Brushes.White, 2.5), arrow);
    }

    private void DrawText(DrawingContext context, string text, double x, double y, double maxWidth, double size, Color color, TextAlignment align)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(Avalonia.Controls.Documents.TextElement.GetFontFamily(this), FontStyle.Normal, FontWeight.SemiBold), size, new SolidColorBrush(color))
        {
            MaxTextWidth = maxWidth, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = align,
        };
        context.DrawText(ft, new Point(x, y));
    }
}
