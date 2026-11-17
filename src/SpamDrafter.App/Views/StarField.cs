using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace SpamDrafter.App.Views;

/// <summary>Réglages du fond étoilé (Réglages → Étoiles) : nombre, taille, vitesse du scintillement et couleur.</summary>
public static class StarSettings
{
    public static int Count { get; private set; } = 90;
    public static double Size { get; private set; } = 1;
    public static double Speed { get; private set; } = 1;
    public static Color Color { get; private set; } = Colors.White;
    public static event Action? Changed;

    public static void Set(int count, double size, double speed, string color)
    {
        Count = Math.Clamp(count, 0, 400);
        Size = Math.Clamp(size, 0.3, 4);
        Speed = Math.Clamp(speed, 0.1, 5);
        Color = Avalonia.Media.Color.TryParse(color, out var c) ? c : Colors.White;
        Changed?.Invoke();
    }
}

/// <summary>
/// Fond étoilé qui scintille doucement (phase 13, D-078). Les étoiles sont posées avec une graine fixe (même ciel à chaque lancement) ;
/// <see cref="Animate"/> faux (option « Réduire les animations ») les fige. Nombre, taille, vitesse et couleur : <see cref="StarSettings"/>.
/// </summary>
public sealed class StarField : Control
{
    private (double X, double Y, double Phase, double Size)[] _stars = Array.Empty<(double, double, double, double)>();
    private readonly DispatcherTimer _timer;
    private double _time;

    /// <summary>Faux : étoiles fixes (réglage « Réduire les animations »).</summary>
    public static bool Animate { get; set; } = true;

    public StarField()
    {
        Rebuild();
        StarSettings.Changed += () => { Rebuild(); InvalidateVisual(); };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _timer.Tick += (_, _) => { if (Animate && _stars.Length > 0) { _time += 0.06 * StarSettings.Speed; InvalidateVisual(); } };
        IsHitTestVisible = false;
    }

    private void Rebuild()
    {
        var rnd = new Random(1730);
        _stars = Enumerable.Range(0, StarSettings.Count).Select(_ => (rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble() * Math.PI * 2, 1.5 + rnd.NextDouble() * 2)).ToArray();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _timer.Start(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { base.OnDetachedFromVisualTree(e); _timer.Stop(); }

    public override void Render(DrawingContext context)
    {
        var b = Bounds;
        var color = StarSettings.Color;
        foreach (var (x, y, phase, size) in _stars)
        {
            var alpha = Animate ? 0.25 + 0.65 * (0.5 + 0.5 * Math.Sin(_time * 0.9 + phase)) : 0.6;
            var r = size * StarSettings.Size / 2;
            context.DrawEllipse(new SolidColorBrush(color, alpha), null, new Point(x * b.Width, y * b.Height), r, r);
        }
    }
}
