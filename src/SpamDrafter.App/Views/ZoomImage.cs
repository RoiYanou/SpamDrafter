using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SpamDrafter.Core.Games;

namespace SpamDrafter.App.Views;

/// <summary>
/// Montre une zone d'une image (<see cref="Window"/>, en fractions de l'image) remplie à la taille du contrôle.
/// Quand la zone change, elle glisse de l'ancienne à la nouvelle en un tiers de seconde (sauf si les animations sont réduites).
/// </summary>
public sealed class ZoomImage : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty = AvaloniaProperty.Register<ZoomImage, Bitmap?>(nameof(Source));
    public static readonly StyledProperty<DetailWindow> WindowProperty = AvaloniaProperty.Register<ZoomImage, DetailWindow>(nameof(Window), new DetailWindow(0, 0, 1, 1));

    public Bitmap? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public DetailWindow Window { get => GetValue(WindowProperty); set => SetValue(WindowProperty, value); }
    /// <summary>Rayon des coins : l'image est découpée aux coins arrondis du cadre.</summary>
    public double CornerRadius { get; set; } = 9;

    private DetailWindow _shown = new(0, 0, 1, 1);
    private DetailWindow _from, _to;
    private DateTime _start;
    private bool _placed;
    private DispatcherTimer? _timer;
    private const double Seconds = 0.35;

    static ZoomImage()
    {
        AffectsRender<ZoomImage>(SourceProperty);
        WindowProperty.Changed.AddClassHandler<ZoomImage>((z, e) => z.Move((DetailWindow)e.NewValue!));
    }

    private void Move(DetailWindow target)
    {
        if (!_placed || Motion.Reduce || Source is null) { _placed = true; _shown = target; InvalidateVisual(); return; }
        _from = _shown; _to = target; _start = DateTime.UtcNow;
        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick -= Tick; _timer.Tick += Tick;
        _timer.Start();
    }

    private void Tick(object? sender, EventArgs e)
    {
        var u = Math.Clamp((DateTime.UtcNow - _start).TotalSeconds / Seconds, 0, 1);
        var k = 1 - Math.Pow(1 - u, 3);
        double L(double a, double b) => a + (b - a) * k;
        _shown = new DetailWindow(L(_from.X, _to.X), L(_from.Y, _to.Y), L(_from.Width, _to.Width), L(_from.Height, _to.Height));
        InvalidateVisual();
        if (u >= 1) _timer?.Stop();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Source is not { } bmp) return;
        var size = bmp.PixelSize;
        var src = new Rect(_shown.X * size.Width, _shown.Y * size.Height, _shown.Width * size.Width, _shown.Height * size.Height);
        using (context.PushClip(new RoundedRect(new Rect(Bounds.Size), CornerRadius)))
            context.DrawImage(bmp, src, new Rect(Bounds.Size));
    }
}
