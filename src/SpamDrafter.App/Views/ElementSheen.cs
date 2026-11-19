using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;

namespace SpamDrafter.App.Views;

/// <summary>
/// Reflet métallique de l'icône d'élément d'une carte, animé en permanence : un seul pinceau dégradé est partagé par toutes les icônes
/// (une icône sert de masque à un rectangle peint de ce pinceau). Un minuteur le fait glisser en diagonale : un éclat clair, bordé d'une ombre,
/// traverse l'icône, puis une courte pause. Immobile si « Désactiver les animations » est coché.
/// </summary>
public static class ElementSheen
{
    public static LinearGradientBrush Brush { get; } = Create();

    private const double CycleSeconds = 3.6;
    private static readonly DateTime Origin = DateTime.UtcNow;
    private static readonly DispatcherTimer Timer = Start();

    private static LinearGradientBrush Create()
    {
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(-1.4, -1.4, RelativeUnit.Relative), EndPoint = new RelativePoint(-0.4, -0.4, RelativeUnit.Relative) };
        // L'éclat tient dans la bande 0,25–0,75 du dégradé ; le reste est transparent (pinceau « Pad » : transparent hors du dégradé).
        brush.GradientStops.Add(new GradientStop(Color.Parse("#00FFFFFF"), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.Parse("#00FFFFFF"), 0.22));
        brush.GradientStops.Add(new GradientStop(Color.Parse("#50000000"), 0.34));
        brush.GradientStops.Add(new GradientStop(Color.Parse("#F0FFFFFF"), 0.5));
        brush.GradientStops.Add(new GradientStop(Color.Parse("#40FFFFFF"), 0.62));
        brush.GradientStops.Add(new GradientStop(Color.Parse("#00FFFFFF"), 0.78));
        brush.GradientStops.Add(new GradientStop(Color.Parse("#00FFFFFF"), 1.0));
        return brush;
    }

    private static DispatcherTimer Start()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(45) };
        timer.Tick += (_, _) => Step();
        timer.Start();
        return timer;
    }

    private static long _lastScroll;
    /// <summary>Signale un défilement : le reflet est mis en pause un instant (il ne redessine plus toutes les cartes pendant que la liste bouge).</summary>
    public static void NoteScroll() { _lastScroll = Environment.TickCount64; State.On = false; }

    /// <summary>
    /// Les rectangles du reflet ne sont présents que lorsque l'éclat est visible (traversée de l'icône) et que la liste ne défile pas : un rectangle
    /// à masque d'opacité coûte un calque de dessin par icône et par image de l'écran, même quand l'éclat est hors de l'icône.
    /// </summary>
    public sealed class SheenState : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _on = true;
        public bool On { get => _on; set { if (_on == value) return; _on = value; PropertyChanged?.Invoke(this, new(nameof(On))); } }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
    public static SheenState State { get; } = new();

    private static void Step()
    {
        if (Environment.TickCount64 - _lastScroll < 250) { State.On = false; return; }
        if (Motion.Reduce)
        {
            Place(0.5);   // reflet fixe, au milieu de l'icône
            State.On = true;
            return;
        }
        var t = ((DateTime.UtcNow - Origin).TotalSeconds % CycleSeconds) / CycleSeconds;
        Place(t < 0.7 ? t / 0.7 : 1.0);   // 70 % du cycle : traversée ; ensuite : éclat hors de l'icône (pause)
        State.On = t < 0.7;
    }

    /// <summary>Position de l'éclat : 0 = avant l'icône, 1 = après (au-delà de 1 : hors icône).</summary>
    private static void Place(double progress)
    {
        var s = -1.2 + progress * 2.4;   // début du dégradé, de -1,2 à +1,2 sur la diagonale
        Brush.StartPoint = new RelativePoint(s, s, RelativeUnit.Relative);
        Brush.EndPoint = new RelativePoint(s + 1, s + 1, RelativeUnit.Relative);
    }

    /// <summary>Force la création du pinceau et du minuteur (appelé par les cartes).</summary>
    public static void Ensure() => _ = Timer;
}
