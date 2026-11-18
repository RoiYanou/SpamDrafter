using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace SpamDrafter.App.Views;

/// <summary>
/// Texte sur une ligne qui s'arrête au bord de sa zone ; quand il est trop long, il défile de droite à gauche tant que la souris est
/// dessus (comme un bandeau), puis revient à sa place quand la souris part. « Réduire les animations » coupe le défilement.
/// </summary>
public sealed class MarqueeText : Decorator
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<MarqueeText, string?>(nameof(Text));
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public static readonly StyledProperty<double> FontSizeProperty = TextBlock.FontSizeProperty.AddOwner<MarqueeText>();
    public static readonly StyledProperty<FontWeight> FontWeightProperty = TextBlock.FontWeightProperty.AddOwner<MarqueeText>();
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextBlock.ForegroundProperty.AddOwner<MarqueeText>();
    public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontWeight FontWeight { get => GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    private const double Speed = 70;   // pixels par seconde
    private const double Pause = 0.7;  // secondes d'arrêt en début et en fin de course
    private readonly TextBlock _block = new() { TextWrapping = TextWrapping.NoWrap, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private DateTime _last;
    private double _offset, _wait;

    public MarqueeText()
    {
        ClipToBounds = true;
        Child = _block;
        _timer.Tick += (_, _) => Step();
        PointerEntered += (_, _) => Start();
        PointerExited += (_, _) => Stop();
        DetachedFromVisualTree += (_, _) => Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty) _block.Text = Text;
        else if (change.Property == FontSizeProperty) _block.FontSize = FontSize;
        else if (change.Property == FontWeightProperty) _block.FontWeight = FontWeight;
        else if (change.Property == ForegroundProperty) _block.Foreground = Foreground;
    }

    private double Overflow => Math.Max(0, _block.DesiredSize.Width - Bounds.Width);

    protected override Size MeasureOverride(Size availableSize)
    {
        _block.Measure(Size.Infinity);
        return new Size(Math.Min(_block.DesiredSize.Width, double.IsInfinity(availableSize.Width) ? _block.DesiredSize.Width : availableSize.Width), _block.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _block.Arrange(new Rect(-_offset, 0, Math.Max(_block.DesiredSize.Width, finalSize.Width), finalSize.Height));
        return finalSize;
    }

    private void Start()
    {
        if (Motion.Reduce || Overflow <= 1) return;
        _offset = 0; _wait = Pause; _last = DateTime.UtcNow;
        _timer.Start();
    }

    private void Stop()
    {
        _timer.Stop();
        _offset = 0;
        InvalidateArrange();
    }

    private void Step()
    {
        var now = DateTime.UtcNow;
        var dt = Math.Min((now - _last).TotalSeconds, 0.1);
        _last = now;
        if (_wait > 0) { _wait -= dt; return; }
        _offset += Speed * dt;
        if (_offset >= Overflow + 12)
        {
            _offset = 0; _wait = Pause;   // fin de course : retour au début, nouvelle lecture tant que la souris reste
        }
        InvalidateArrange();
    }
}
