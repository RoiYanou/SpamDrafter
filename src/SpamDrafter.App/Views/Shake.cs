using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace SpamDrafter.App.Views;

/// <summary>
/// Secousse douce d'un champ invalide (phase 13) : trois allers-retours en 350 ms, à la place d'une fenêtre d'erreur.
/// Sans effet quand « Désactiver les animations » est coché.
/// </summary>
public static class Shake
{
    public static void Run(Control control)
    {
        if (Motion.Reduce) return;
        var move = new TranslateTransform();
        control.RenderTransform = move;
        var start = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var t = (DateTime.UtcNow - start).TotalMilliseconds / 350;
            if (t >= 1) { timer.Stop(); control.RenderTransform = null; return; }
            move.X = Math.Sin(t * Math.PI * 6) * 8 * (1 - t);
        };
        timer.Start();
    }
}
