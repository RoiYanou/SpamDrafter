using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace SpamDrafter.App.Views;

/// <summary>Petit rebond d'un élément qu'on vient de choisir : il se tasse, grossit un peu, puis retrouve sa taille (« Désactiver les animations » le coupe).</summary>
public static class Bounce
{
    /// <summary>Apparition avec rebond (agrandissement au survol d'une carte) : part un peu plus petit, dépasse légèrement, se pose.</summary>
    public static void Appear(Control control)
    {
        if (Motion.Reduce) return;
        var scale = new ScaleTransform(0.88, 0.88);
        control.RenderTransform = scale;
        var start = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var t = (DateTime.UtcNow - start).TotalMilliseconds / 420;
            if (t >= 1) { timer.Stop(); scale.ScaleX = scale.ScaleY = 1; return; }
            scale.ScaleX = scale.ScaleY = 1 - 0.12 * Math.Exp(-3.5 * t) * Math.Cos(2 * Math.PI * 1.25 * t);
        };
        timer.Start();
    }

    public static void Run(Control control)
    {
        if (Motion.Reduce) return;
        var scale = new ScaleTransform();
        control.RenderTransform = scale;
        var start = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var t = (DateTime.UtcNow - start).TotalMilliseconds / 380;
            if (t >= 1) { timer.Stop(); control.RenderTransform = null; return; }
            scale.ScaleX = scale.ScaleY = 1 - 0.14 * Math.Sin(t * Math.PI * 3) * (1 - t);
        };
        timer.Start();
    }
}
