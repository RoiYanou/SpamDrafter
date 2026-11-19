using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace SpamDrafter.App.Views;

/// <summary>
/// Texture animée du fond, propre à chaque thème et dessinée derrière les particules : nébuleuse violette (défaut), vagues et reflets (eau),
/// flammes qui montent (feu), prairie sous le vent et lucioles (nature), rayons dorés qui tournent (lumière), strates et poussière
/// (terre), sceau occulte qui tourne (ombre). Très douce et peu contrastée, pour ne jamais gêner la lecture ; mouvements lents, redessinée une quinzaine
/// de fois par seconde. « Réduire les animations » la fige.
/// </summary>
public sealed class ThemeTexture : Control
{
    private readonly DispatcherTimer _timer;
    private double _time;
    private static readonly Dictionary<(Color, byte), RadialGradientBrush> Glows = new();

    public ThemeTexture()
    {
        IsHitTestVisible = false;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(66) };
        _timer.Tick += (_, _) => { if (StarField.Animate) { _time += 0.066; InvalidateVisual(); } };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _timer.Start(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { base.OnDetachedFromVisualTree(e); _timer.Stop(); }

    /// <summary>Tache de lumière douce : dégradé radial dont le centre a l'opacité donnée et le bord est transparent.</summary>
    private static void Glow(DrawingContext ctx, double x, double y, double rx, double ry, Color c, byte alpha)
    {
        alpha = (byte)Math.Min(255, alpha * 2.6);   // intensité générale de la texture
        if (!Glows.TryGetValue((c, alpha), out var brush))
        {
            brush = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(alpha, c.R, c.G, c.B), 0),
                    new GradientStop(Color.FromArgb((byte)(alpha * 0.45), c.R, c.G, c.B), 0.55),
                    new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1),
                },
            };
            Glows[(c, alpha)] = brush;
        }
        ctx.DrawEllipse(brush, null, new Point(x, y), rx, ry);
    }

    private static Color C(string hex) => Color.Parse(hex);

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 10 || h < 10) return;
        var t = StarField.Animate ? _time : 0;
        switch (StarField.Kind)
        {
            case "water": Water(ctx, w, h, t); break;
            case "fire": Fire(ctx, w, h, t); break;
            case "nature": Nature(ctx, w, h, t); break;
            case "light": Rays(ctx, w, h, t); break;
            case "earth": Earth(ctx, w, h, t); break;
            case "shadow": Sigil(ctx, w, h, t); break;
            default: Nebula(ctx, w, h, t); Aurora(ctx, w, h, t); break;
        }
    }

    /// <summary>Nébuleuse : grandes taches violettes, roses et bleues qui dérivent lentement sur des orbites croisées.</summary>
    private static void Nebula(DrawingContext ctx, double w, double h, double t)
    {
        var blobs = new[] { ("#7B4BE0", 0.0, 0.40, 0.30), ("#E070D0", 2.1, 0.30, 0.55), ("#5BC0F0", 4.0, 0.25, 0.70), ("#9A7BFF", 1.2, 0.45, 0.45), ("#E070D0", 5.3, 0.20, 0.25) };
        foreach (var (hex, ph, ox, oy) in blobs)
        {
            var x = w * (ox + 0.18 * Math.Sin(t * 0.06 + ph)); var y = h * (oy + 0.15 * Math.Cos(t * 0.05 + ph * 1.3));
            Glow(ctx, x, y, w * 0.34, h * 0.42, C(hex), 48);
        }
    }

    /// <summary>Rubans d'aurore : trois bandes lumineuses qui ondulent lentement sur toute la largeur (thème par défaut).</summary>
    private static void Aurora(DrawingContext ctx, double w, double h, double t)
    {
        var colors = new[] { "#9A7BFF", "#E070D0", "#5BC0F0" };
        for (var k = 0; k < 3; k++)
        {
            var baseY = h * (0.22 + 0.27 * k); var thick = h * 0.10;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                double Y(double x) => baseY + 50 * Math.Sin(x / 330 + t * 0.20 + k * 2.1) + 24 * Math.Sin(x / 140 - t * 0.33 + k);
                g.BeginFigure(new Point(0, Y(0)), true);
                for (double x = 20; x <= w + 20; x += 20) g.LineTo(new Point(x, Y(x)));
                for (double x = w + 20; x >= 0; x -= 20) g.LineTo(new Point(x, Y(x) + thick * (1 + 0.4 * Math.Sin(x / 200 + t * 0.3 + k))));
                g.EndFigure(true);
            }
            var c = C(colors[k]);
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 0), new GradientStop(Color.FromArgb(70, c.R, c.G, c.B), 0.5), new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1) },
            };
            ctx.DrawGeometry(brush, null, geo);
        }
    }

    /// <summary>Eau : bandes de vagues qui ondulent à des vitesses différentes, et reflets turquoise qui scintillent.</summary>
    private static void Water(DrawingContext ctx, double w, double h, double t)
    {
        for (var layer = 0; layer < 5; layer++)
        {
            var baseY = h * (0.30 + layer * 0.15);
            var amp = 14 + layer * 5; var len = 260 + layer * 90; var speed = (layer % 2 == 0 ? 1 : -1) * (0.25 + layer * 0.07);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(0, h), true);
                for (double x = 0; x <= w + 20; x += 20)
                    g.LineTo(new Point(x, baseY + amp * Math.Sin(x / len * 2 * Math.PI + t * speed) + amp * 0.4 * Math.Sin(x / (len * 0.43) - t * speed * 1.4)));
                g.LineTo(new Point(w, h));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(new SolidColorBrush(C(layer % 2 == 0 ? "#5BC0F0" : "#4FD6C8"), 0.11), new Pen(new SolidColorBrush(C("#9AE0FF"), 0.26), 1.2), geo);
        }
        for (var i = 0; i < 6; i++)   // reflets : taches claires qui miroitent
        {
            var x = w * ((i * 0.19 + 0.08 + 0.03 * Math.Sin(t * 0.1 + i)) % 1.0); var y = h * (0.18 + 0.12 * (i % 3) + 0.04 * Math.Sin(t * 0.15 + i * 2));
            Glow(ctx, x, y, 140, 50, C("#BFEFFF"), (byte)(14 + 12 * (0.5 + 0.5 * Math.Sin(t * 0.5 + i * 1.7))));
        }
    }

    // ----- Feu -----
    // Dégradé d'une flamme, du bas vers le haut : cœur jaune pâle, orange, rouge, puis transparent à la pointe (relatif à la forme de la flamme).
    private static readonly LinearGradientBrush FlameBrush = new()
    {
        StartPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative), EndPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(235, 255, 235, 150), 0.0), new GradientStop(Color.FromArgb(215, 255, 170, 60), 0.22),
            new GradientStop(Color.FromArgb(150, 245, 90, 40), 0.55), new GradientStop(Color.FromArgb(0, 190, 30, 30), 1.0),
        },
    };

    /// <summary>Forme de flamme : base large, bords qui s'incurvent, pointe qui se courbe sous l'effet du vent (<paramref name="sway"/>).</summary>
    private static StreamGeometry Flame(double cx, double baseY, double halfW, double height, double sway)
    {
        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(new Point(cx - halfW, baseY), true);
        g.CubicBezierTo(new Point(cx - halfW * 1.15, baseY - height * 0.35), new Point(cx - halfW * 0.35 + sway * 0.5, baseY - height * 0.72), new Point(cx + sway, baseY - height));
        g.CubicBezierTo(new Point(cx + halfW * 0.35 + sway * 0.5, baseY - height * 0.66), new Point(cx + halfW * 1.15, baseY - height * 0.3), new Point(cx + halfW, baseY));
        g.EndFigure(true);
        return geo;
    }

    /// <summary>
    /// Feu : un lit de flammes en bas de l'écran (deux rangées qui vacillent, l'arrière plus haute et plus rouge), et des flammes qui se détachent,
    /// montent jusqu'en haut de l'écran en rétrécissant et s'éteignent, comme des langues de feu emportées par le courant d'air chaud.
    /// </summary>
    private static void Fire(DrawingContext ctx, double w, double h, double t)
    {
        Glow(ctx, w * 0.5, h * 1.0, w * 0.85, h * 0.55, C("#FF6A2C"), 60);
        for (var row = 0; row < 2; row++)
        {
            var n = row == 0 ? 11 : 15;
            for (var i = 0; i < n; i++)
            {
                var phase = i * 1.93 + row * 2.4;
                var cx = w * (i + 0.5) / n + Math.Sin(t * 0.6 + phase) * 18;
                var hw = w / n * (row == 0 ? 0.95 : 0.78);
                var life = 0.55 + 0.45 * Math.Sin(t * 1.5 + phase) * Math.Sin(t * 0.8 + phase * 0.7);   // la flamme grandit et rétrécit
                var height = h * (row == 0 ? 0.50 : 0.33) * (0.6 + 0.55 * life) * (0.8 + 0.4 * (0.5 + 0.5 * Math.Sin(phase * 3.1)));
                var sway = hw * (0.9 * Math.Sin(t * 1.3 + phase) + 0.5 * Math.Sin(t * 2.7 + phase * 1.6));
                using (ctx.PushOpacity(row == 0 ? 0.50 : 0.72))
                    ctx.DrawGeometry(FlameBrush, null, Flame(cx, h + 6, hw, height, sway));
            }
        }
        // Flammes détachées : elles montent jusqu'en haut, plus fines et plus pâles à mesure qu'elles s'éloignent.
        const int wisps = 18;
        for (var i = 0; i < wisps; i++)
        {
            var p = (t * 0.05 + i / (double)wisps + (i % 3) * 0.07) % 1.0;
            var cx = w * ((i * 0.6180339) % 1.0) + Math.Sin(t * 0.9 + i * 2.1) * (20 + 50 * p);
            var y = h * (1.0 - p);
            var size = 1 - 0.65 * p;
            using (ctx.PushOpacity(Math.Pow(1 - p, 1.3) * 0.75))
                ctx.DrawGeometry(FlameBrush, null, Flame(cx, y + 60 * size, 20 * size, 110 * size, 16 * Math.Sin(t * 2.0 + i)));
        }
    }

    // ----- Nature -----
    private static readonly Color[] GrassColors = { C("#3F9B4A"), C("#6FBF5A"), C("#9AD05A") };

    /// <summary>
    /// Nature : une prairie. Deux rangées d'herbes en bas de l'écran ondulent sous une même vague de vent qui traverse l'écran de gauche à droite ;
    /// des lucioles dorées vagabondent et clignotent au-dessus, dans une brume verte.
    /// </summary>
    private static void Nature(DrawingContext ctx, double w, double h, double t)
    {
        Glow(ctx, w * 0.5, h * 1.02, w * 0.8, h * 0.40, C("#8FE08A"), 36);   // brume au ras du sol
        for (var layer = 0; layer < 2; layer++)
        {
            var n = layer == 0 ? 90 : 60;
            var maxH = layer == 0 ? 0.20 : 0.30;
            for (var i = 0; i < n; i++)
            {
                var seed = (i * 0.754877666 + layer * 0.31) % 1.0;
                var x = w * ((i + 0.5 + (seed - 0.5) * 0.8) / n);
                var height = h * maxH * (0.45 + 0.55 * ((seed * 7.3) % 1.0));
                // Vent : une grande vague lente + un frémissement rapide ; plus la lame est haute, plus elle plie.
                var wind = 0.6 * Math.Sin(x / 300 - t * 0.9) + 0.3 * Math.Sin(x / 110 - t * 1.9 + layer) + 0.1 * Math.Sin(t * 3.1 + i);
                var tip = wind * height * 0.45;
                var bw = (layer == 0 ? 5.0 : 7.0) * (0.7 + 0.6 * seed);
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(new Point(x - bw, h + 4), true);
                    g.QuadraticBezierTo(new Point(x - bw * 0.4 + tip * 0.3, h - height * 0.55), new Point(x + tip, h - height));
                    g.QuadraticBezierTo(new Point(x + bw * 0.4 + tip * 0.3, h - height * 0.5), new Point(x + bw, h + 4));
                    g.EndFigure(true);
                }
                var c = GrassColors[(i + layer) % GrassColors.Length];
                ctx.DrawGeometry(new SolidColorBrush(c, layer == 0 ? 0.20 : 0.30), null, geo);
            }
        }
        // Lucioles : points dorés qui dérivent en lacets et clignotent chacun à son rythme.
        for (var i = 0; i < 16; i++)
        {
            var sx = (i * 0.754877666) % 1.0; var sy = (i * 0.569840291) % 1.0;
            var x = w * sx + Math.Sin(t * 0.25 + i * 1.7) * 60 + Math.Sin(t * 0.7 + i) * 18;
            var y = h * (0.12 + 0.6 * sy) + Math.Cos(t * 0.21 + i * 2.3) * 45;
            var blink = Math.Pow(0.5 + 0.5 * Math.Sin(t * 0.9 + i * 2.9), 2);
            Glow(ctx, x, y, 30, 30, C("#E8F27A"), (byte)(20 + 60 * blink));
            ctx.DrawEllipse(new SolidColorBrush(C("#FFFBC0"), 0.25 + 0.65 * blink), null, new Point(x, y), 2.2, 2.2);
        }
    }

    /// <summary>Lumière : rayons dorés en éventail qui tournent très lentement autour d'un point au-dessus de l'écran, et halo central.</summary>
    private static void Rays(DrawingContext ctx, double w, double h, double t)
    {
        var cx = w * 0.5; var cy = -h * 0.15; var len = Math.Sqrt(w * w + h * h) * 1.3;
        using (ctx.PushTransform(Matrix.CreateRotation(t * 0.012) * Matrix.CreateTranslation(cx, cy)))
            for (var i = 0; i < 14; i++)
            {
                var a0 = i * Math.PI * 2 / 14; var a1 = a0 + Math.PI / 14 * (0.55 + 0.25 * Math.Sin(t * 0.2 + i));
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(new Point(0, 0), true);
                    g.LineTo(new Point(len * Math.Cos(a0), len * Math.Sin(a0)));
                    g.LineTo(new Point(len * Math.Cos(a1), len * Math.Sin(a1)));
                    g.EndFigure(true);
                }
                ctx.DrawGeometry(new SolidColorBrush(C("#FFE08A"), 0.085 + 0.04 * Math.Sin(t * 0.3 + i)), null, geo);
            }
        Glow(ctx, w * 0.5, h * 0.05, w * 0.55, h * 0.45, C("#FFE08A"), 36);
    }

    /// <summary>Terre : strates ondulées couleur sable qui glissent lentement, et nuages de poussière chaude.</summary>
    private static void Earth(DrawingContext ctx, double w, double h, double t)
    {
        for (var layer = 0; layer < 6; layer++)
        {
            var baseY = h * (0.15 + layer * 0.15);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(0, baseY + 40), true);
                for (double x = 0; x <= w + 30; x += 30)
                    g.LineTo(new Point(x, baseY + 18 * Math.Sin(x / (380 + layer * 70) + t * 0.05 * (layer % 2 == 0 ? 1 : -1) + layer)));
                g.LineTo(new Point(w, baseY + 60 + layer * 4)); g.LineTo(new Point(0, baseY + 60 + layer * 4));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(new SolidColorBrush(C(layer % 2 == 0 ? "#E0A56A" : "#A9C46E"), 0.10), new Pen(new SolidColorBrush(C("#E0B880"), 0.18), 1), geo);
        }
        for (var i = 0; i < 5; i++)
            Glow(ctx, w * ((i * 0.23 + t * 0.004) % 1.2 - 0.1), h * (0.25 + 0.14 * i % 0.7), 220, 90, C("#D9B98A"), 22);
    }

    /// <summary>
    /// Ombre : un grand sceau occulte au centre de l'écran. Des cercles, un hexagramme, un octogone et une couronne de graduations tournent lentement
    /// en sens opposés, des points orbitent autour, et un halo violet pulse ; les bords de l'écran s'assombrissent en vignette.
    /// </summary>
    private static void Sigil(DrawingContext ctx, double w, double h, double t)
    {
        var center = new Point(w * 0.5, h * 0.5);
        var r = Math.Min(w, h) * 0.47;
        var violet = C("#B89CFF"); var magenta = C("#E070C8");
        var pulse = 0.65 + 0.35 * Math.Sin(t * 0.6);
        Glow(ctx, center.X, center.Y, r * 1.5, r * 1.5, C("#7A5AD8"), (byte)(28 * pulse));

        Pen P(Color c, double a, double thickness, bool dashed = false) =>
            new(new SolidColorBrush(c, a), thickness, dashed ? new DashStyle(new double[] { 3, 5 }, 0) : null);

        // Cercles concentriques (l'un en pointillés).
        ctx.DrawEllipse(null, P(violet, 0.34, 2.2), center, r, r);
        ctx.DrawEllipse(null, P(violet, 0.22, 1.2), center, r * 0.93, r * 0.93);
        ctx.DrawEllipse(null, P(magenta, 0.30, 1.6, true), center, r * 0.80, r * 0.80);
        ctx.DrawEllipse(null, P(violet, 0.26, 1.5), center, r * 0.56, r * 0.56);

        // Couronne de graduations (tourne dans un sens), avec un repère plus long tous les 6.
        using (ctx.PushTransform(Matrix.CreateRotation(t * 0.035) * Matrix.CreateTranslation(center.X, center.Y)))
            for (var i = 0; i < 72; i++)
            {
                var a = i * Math.PI * 2 / 72; var len = i % 6 == 0 ? 0.075 : 0.035;
                ctx.DrawLine(P(violet, i % 6 == 0 ? 0.42 : 0.24, i % 6 == 0 ? 2 : 1.2),
                    new Point(r * 0.93 * Math.Cos(a), r * 0.93 * Math.Sin(a)), new Point(r * (0.93 - len) * Math.Cos(a), r * (0.93 - len) * Math.Sin(a)));
            }

        // Polygones : hexagramme (deux triangles) dans un sens, octogone dans l'autre.
        void Polygon(int sides, double radius, double angle, Pen pen, int skip = 1)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                for (var i = 0; i <= sides; i++)
                {
                    var a = angle + i * skip * Math.PI * 2 / sides;
                    var pt = new Point(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a));
                    if (i == 0) g.BeginFigure(pt, false); else g.LineTo(pt);
                }
                g.EndFigure(true);
            }
            ctx.DrawGeometry(null, pen, geo);
        }
        Polygon(3, r * 0.80, -t * 0.05, P(magenta, 0.34, 1.8));
        Polygon(3, r * 0.80, -t * 0.05 + Math.PI, P(magenta, 0.34, 1.8));
        Polygon(8, r * 0.56, t * 0.04, P(violet, 0.30, 1.5));
        Polygon(8, r * 0.38, -t * 0.07, P(violet, 0.22, 1.2), 3);

        // Points qui orbitent, comme des esprits attirés par le sceau.
        for (var i = 0; i < 6; i++)
        {
            var a = t * (0.10 + 0.015 * i) * (i % 2 == 0 ? 1 : -1) + i * 1.05;
            var rr = r * (0.62 + 0.065 * i);
            var p = new Point(center.X + rr * Math.Cos(a), center.Y + rr * Math.Sin(a));
            Glow(ctx, p.X, p.Y, 26, 26, magenta, 40);
            ctx.DrawEllipse(new SolidColorBrush(C("#F3D8FF"), 0.7), null, p, 3, 3);
        }

        // Vignette : les bords de l'écran s'assombrissent, le sceau paraît émerger de l'ombre.
        var vignette = new RadialGradientBrush
        {
            RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0, 8, 4, 20), 0.55), new GradientStop(Color.FromArgb(150, 8, 4, 20), 1) },
        };
        ctx.DrawRectangle(vignette, null, new Rect(0, 0, w, h));
    }
}
