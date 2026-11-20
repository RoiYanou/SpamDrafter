using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace SpamDrafter.App.Views;

/// <summary>
/// Petites animations d'apparition. <see cref="Play"/> : l'élément grossit depuis une taille réduite en rebondissant comme une balle (il ne dépasse jamais sa taille normale,
/// donc rien n'est coupé par le cadre qui le contient) avec un fondu. <see cref="FadeOut"/> : fondu de sortie. Sans effet si les animations sont réduites.
/// </summary>
public static class PopIn
{
    private const double Seconds = 0.6;

    /// <summary>Joue l'animation sur un élément ; <paramref name="from"/> : taille de départ (0,6 = 60 %).</summary>
    public static void Play(Control c, double from = 0.6)
    {
        if (Motion.Reduce) return;
        var scale = new ScaleTransform(from, from);
        c.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        c.RenderTransform = scale;
        c.Opacity = 0;
        var start = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var u = Math.Clamp((DateTime.UtcNow - start).TotalSeconds / Seconds, 0, 1);
            // Rebonds amortis : la taille revient à 100 % puis retombe de moins en moins bas.
            var s = 1 - (1 - from) * Math.Exp(-3.2 * u) * Math.Abs(Math.Cos(7.5 * u));
            scale.ScaleX = scale.ScaleY = u >= 1 ? 1 : s;
            c.Opacity = Math.Min(1, u / 0.35);
            if (u >= 1) { timer.Stop(); c.Opacity = 1; c.RenderTransform = null; }
        };
        timer.Start();
    }

    /// <summary>Fait apparaître un élément en fondu (sans changer sa taille).</summary>
    public static void FadeIn(Control c, double seconds)
    {
        if (Motion.Reduce) { c.Opacity = 1; return; }
        c.Opacity = 0;
        var start = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var u = Math.Clamp((DateTime.UtcNow - start).TotalSeconds / seconds, 0, 1);
            c.Opacity = u;
            if (u >= 1) timer.Stop();
        };
        timer.Start();
    }

    /// <summary>Fait disparaître un élément en fondu.</summary>
    public static void FadeOut(Control c, double seconds)
    {
        if (Motion.Reduce) { c.Opacity = 0; return; }
        var start = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var u = Math.Clamp((DateTime.UtcNow - start).TotalSeconds / seconds, 0, 1);
            c.Opacity = 1 - u;
            if (u >= 1) timer.Stop();
        };
        timer.Start();
    }
}
