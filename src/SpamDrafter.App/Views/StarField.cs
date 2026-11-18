using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace SpamDrafter.App.Views;

/// <summary>Réglages des particules de fond (Réglages → Particules) : nombre, taille, vitesse et couleur. Ils valent pour tous les thèmes.</summary>
public static class StarSettings
{
    public static int Count { get; private set; } = 90;
    public static double Size { get; private set; } = 1;
    public static double Speed { get; private set; } = 1;
    /// <summary>Teinte choisie par le joueur pour toutes les particules ; null = « selon le thème ».</summary>
    public static Color? Tint { get; private set; }
    public static event Action? Changed;

    public static void Set(int count, double size, double speed, string color)
    {
        Count = Math.Clamp(count, 0, 400);
        Size = Math.Clamp(size, 0.3, 4);
        Speed = Math.Clamp(speed, 0.1, 5);
        Tint = Avalonia.Media.Color.TryParse(color, out var c) ? c : null;
        Changed?.Invoke();
    }
}

/// <summary>
/// Fond animé (phase 13, D-078). Par défaut : étoiles qui scintillent. Avec un thème d'élément, ce sont des détails qui lui ressemblent :
/// pluie fine (eau), braises qui montent (feu), feuilles et pollen qui flottent (nature), étincelles dorées (lumière), poussière qui
/// dérive (terre), volutes sombres qui montent lentement (ombre). Toujours doux et peu contrasté. Les particules sont posées avec une
/// graine fixe ; <see cref="Animate"/> faux (« Réduire les animations ») les fige. Nombre, taille et vitesse : <see cref="StarSettings"/>.
/// </summary>
public sealed class StarField : Control
{
    private (double X, double Y, double Phase, double Size, double Speed)[] _stars = Array.Empty<(double, double, double, double, double)>();
    private readonly DispatcherTimer _timer;
    private double _time;

    /// <summary>Faux : particules fixes (réglage « Réduire les animations »).</summary>
    public static bool Animate { get; set; } = true;

    /// <summary>Thème d'élément du fond (« default », « water », « fire »…).</summary>
    public static string Kind { get; private set; } = "default";
    private static event Action? KindChanged;
    public static void SetKind(string kind) { if (Kind == kind) return; Kind = kind; KindChanged?.Invoke(); }

    public StarField()
    {
        Rebuild();
        StarSettings.Changed += () => { Rebuild(); InvalidateVisual(); };
        KindChanged += () => InvalidateVisual();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += (_, _) => { if (Animate && _stars.Length > 0) { _time += 0.05 * StarSettings.Speed; InvalidateVisual(); } };
        IsHitTestVisible = false;
    }

    private void Rebuild()
    {
        var rnd = new Random(1730);
        _stars = Enumerable.Range(0, StarSettings.Count)
            .Select(_ => (rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble() * Math.PI * 2, 1.5 + rnd.NextDouble() * 2, 0.5 + rnd.NextDouble())).ToArray();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _timer.Start(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { base.OnDetachedFromVisualTree(e); _timer.Stop(); }

    private static double Frac(double v) => v - Math.Floor(v);
    /// <summary>La couleur propre au thème, sauf si le joueur a choisi une teinte (elle vaut alors pour tous les thèmes).</summary>
    private static Color Pick(Color native) => StarSettings.Tint ?? native;
    private static Color Pick(byte r, byte g, byte b, byte a = 255) { var c = StarSettings.Tint ?? Color.FromRgb(r, g, b); return Color.FromArgb(a, c.R, c.G, c.B); }

    public override void Render(DrawingContext ctx)
    {
        var b = Bounds;
        double w = b.Width, h = b.Height, size = StarSettings.Size;
        var t = Animate ? _time : 0;
        foreach (var (x0, y0, phase, s, speed) in _stars)
        {
            switch (Kind)
            {
                case "water":
                {
                    // Pluie fine : de petites gouttes qui tombent en biais, d'un bleu pâle ; un cercle d'éclaboussure en bas de course.
                    var y = Frac(y0 + t * 0.16 * speed);
                    var x = x0 * w - y * 14;
                    var a = 0.22 + 0.3 * speed / 1.5;
                    var pen = new Pen(new SolidColorBrush(Pick(150, 205, 255, (byte)(a * 255))), 1.1 * size);
                    ctx.DrawLine(pen, new Point(x, y * h), new Point(x - 1.6, y * h + 7 * size + speed * 3));
                    if (y > 0.93) ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Pick(170, 220, 255, 70)), 0.8), new Point(x, y * h + 8), (y - 0.93) * 80 * size, (y - 0.93) * 26 * size);
                    break;
                }
                case "fire":
                {
                    // Braises : montent lentement en ondulant, orange puis jaune pâle, et s'éteignent en haut.
                    var y = 1 - Frac(y0 + t * 0.05 * speed);
                    var x = x0 * w + Math.Sin(t * 0.8 + phase) * 14;
                    var a = Math.Clamp(1 - y, 0, 1) * 0.65 * Math.Min(1, y * 6);
                    var c = y > 0.5 ? Pick(255, 140, 70) : Pick(255, 200, 110);
                    var r = s * size * 0.45;
                    ctx.DrawEllipse(new SolidColorBrush(c, a * 0.25), null, new Point(x, y * h), r * 2.6, r * 2.6);
                    ctx.DrawEllipse(new SolidColorBrush(c, a), null, new Point(x, y * h), r, r);
                    break;
                }
                case "nature":
                {
                    // Feuilles et pollen : tombent doucement en se balançant ; une feuille sur trois est un peu plus grande.
                    var y = Frac(y0 + t * 0.035 * speed);
                    var x = x0 * w + Math.Sin(t * 0.6 + phase) * 22;
                    var leaf = s > 2.8;
                    var c = leaf ? Pick(140, 215, 130) : Pick(215, 230, 130);
                    var tilt = Math.Sin(t * 0.7 + phase) * 0.9;
                    using (ctx.PushTransform(Matrix.CreateRotation(tilt) * Matrix.CreateTranslation(x, y * h)))
                        ctx.DrawEllipse(new SolidColorBrush(c, leaf ? 0.4 : 0.5), null, new Point(0, 0), leaf ? s * size * 1.6 : s * size * 0.45, leaf ? s * size * 0.75 : s * size * 0.45);
                    break;
                }
                case "light":
                {
                    // Étincelles dorées : scintillent en croix, sans bouger.
                    var a = 0.15 + 0.6 * (0.5 + 0.5 * Math.Sin(t * 1.1 + phase));
                    var x = x0 * w; var y = y0 * h; var r = s * size * 1.7;
                    var c = Pick(255, 226, 140);
                    var pen = new Pen(new SolidColorBrush(c, a), 1);
                    ctx.DrawLine(pen, new Point(x - r, y), new Point(x + r, y));
                    ctx.DrawLine(pen, new Point(x, y - r), new Point(x, y + r));
                    ctx.DrawEllipse(new SolidColorBrush(Pick(255, 245, 210), a), null, new Point(x, y), s * size * 0.35, s * size * 0.35);
                    break;
                }
                case "earth":
                {
                    // Poussière : de petites particules beiges qui dérivent lentement vers la droite et un peu vers le haut.
                    var x = Frac(x0 + t * 0.02 * speed) * w;
                    var y = (y0 * h - t * 2 * speed) % h; if (y < 0) y += h;
                    var a = 0.2 + 0.25 * (0.5 + 0.5 * Math.Sin(t * 0.5 + phase));
                    ctx.DrawEllipse(new SolidColorBrush(Pick(225, 190, 140), a), null, new Point(x, y), s * size * 0.5, s * size * 0.5);
                    break;
                }
                case "shadow":
                {
                    // Volutes : grosses taches violettes très pâles qui montent et respirent.
                    var y = 1 - Frac(y0 + t * 0.025 * speed);
                    var x = x0 * w + Math.Sin(t * 0.3 + phase) * 30;
                    var a = 0.06 + 0.05 * (0.5 + 0.5 * Math.Sin(t * 0.6 + phase));
                    var r = s * size * 7;
                    ctx.DrawEllipse(new SolidColorBrush(Pick(160, 140, 235), a), null, new Point(x, y * h), r, r);
                    ctx.DrawEllipse(new SolidColorBrush(Pick(235, 140, 220), a * 0.8), null, new Point(x, y * h), r * 0.45, r * 0.45);
                    break;
                }
                default:
                {
                    var alpha = Animate ? 0.25 + 0.65 * (0.5 + 0.5 * Math.Sin(t * 0.9 + phase)) : 0.6;
                    var r = s * size / 2;
                    ctx.DrawEllipse(new SolidColorBrush(StarSettings.Tint ?? Colors.White, alpha), null, new Point(x0 * w, y0 * h), r, r);
                    break;
                }
            }
        }
    }
}
