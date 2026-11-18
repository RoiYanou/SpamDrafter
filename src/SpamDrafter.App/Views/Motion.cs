using Avalonia;
using Avalonia.Controls;
namespace SpamDrafter.App.Views;

/// <summary>
/// Réglage « Réduire les animations » (phase 13, D-078) : vrai = pas d'inclinaison, de retournement ni d'étoiles qui scintillent.
/// Un seul interrupteur pour toutes les animations ; les vues le consultent, elles ne gardent pas leur propre réglage.
/// </summary>
public static class Motion
{
    private static bool _reduce;

    public static bool Reduce
    {
        get => _reduce;
        set { _reduce = value; StarField.Animate = !value; }
    }
}

/// <summary>
/// Transition d'entrée d'une page (changement d'onglet) : fondu + glissement vers le haut, 300 ms, décélération douce. Pilotée par un
/// minuteur (comme le retournement de la carte) : la page rejoue l'effet à chaque fois qu'elle devient visible ou qu'elle est (ré)attachée.
/// « Réduire les animations » la coupe. Utilisation : <c>v:PageFx.Enter="True"</c>.
/// </summary>
public static class PageFx
{
    public static readonly Avalonia.AttachedProperty<bool> EnterProperty =
        Avalonia.AvaloniaProperty.RegisterAttached<Avalonia.Controls.Control, Avalonia.Controls.Control, bool>("Enter");
    public static bool GetEnter(Avalonia.Controls.Control c) => c.GetValue(EnterProperty);
    public static void SetEnter(Avalonia.Controls.Control c, bool v) => c.SetValue(EnterProperty, v);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Avalonia.Controls.Control, Avalonia.Threading.DispatcherTimer> Timers = new();

    static PageFx()
    {
        EnterProperty.Changed.AddClassHandler<Avalonia.Controls.Control>((c, e) =>
        {
            if (e.NewValue is not true) return;
            c.AttachedToVisualTree += (_, _) => Play(c);
            c.PropertyChanged += (_, a) => { if (a.Property == Avalonia.Visual.IsVisibleProperty && c.IsVisible) Play(c); };
        });
    }

    /// <summary>Vrai pendant le préchargement des pages : aucune animation d'entrée.</summary>
    public static bool Suppress { get; set; }

    private static void Play(Avalonia.Controls.Control c)
    {
        if (Suppress) return;
        if (Timers.TryGetValue(c, out var old)) old.Stop();
        if (Motion.Reduce) { c.Opacity = 1; c.RenderTransform = null; return; }
        const double ms = 300, rise = 20;
        var move = new Avalonia.Media.TranslateTransform(0, rise);
        c.RenderTransform = move;
        c.Opacity = 0;
        var start = DateTime.UtcNow;
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var t = (DateTime.UtcNow - start).TotalMilliseconds / ms;
            if (t >= 1) { timer.Stop(); c.Opacity = 1; c.RenderTransform = null; return; }
            var ease = 1 - Math.Pow(1 - t, 3);
            c.Opacity = ease;
            move.Y = rise * (1 - ease);
        };
        Timers.AddOrUpdate(c, timer);
        timer.Start();
    }
}
