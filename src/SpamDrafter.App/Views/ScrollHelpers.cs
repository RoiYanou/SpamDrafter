using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SpamDrafter.App.Views;

/// <summary>
/// Défilement au clic molette, comme sur une page web : un clic molette pose un repère, puis déplacer la souris vers le haut ou le bas
/// fait défiler d'autant plus vite qu'on s'éloigne du repère. Un nouveau clic (n'importe quel bouton), Échap, ou le relâchement de la molette après
/// un glissement arrête le défilement. Fonctionne dans toutes les zones défilantes de l'application.
/// </summary>
public sealed class AutoScroll
{
    private const double DeadZone = 12;
    private readonly TopLevel _top;
    private ScrollViewer? _sv;
    private Point _anchor, _cur;
    private DispatcherTimer? _timer;
    private Control? _marker;
    private DateTime _since;
    private bool _holding;
    private Cursor? _oldCursor;

    private AutoScroll(TopLevel top) { _top = top; }

    public static void Install(TopLevel top)
    {
        var a = new AutoScroll(top);
        top.AddHandler(InputElement.PointerPressedEvent, a.OnPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        top.AddHandler(InputElement.PointerReleasedEvent, a.OnReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        top.AddHandler(InputElement.PointerMovedEvent, a.OnMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        top.AddHandler(InputElement.KeyDownEvent, (_, e) => { if (a._sv is not null && e.Key == Key.Escape) { a.Stop(); e.Handled = true; } }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        top.AddHandler(InputElement.PointerWheelChangedEvent, (_, _) => a.Stop(), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        if (top is Window w) w.Deactivated += (_, _) => a.Stop();
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(_top).Properties;
        if (_sv is not null) { Stop(); if (props.IsMiddleButtonPressed) e.Handled = true; return; }   // un clic arrête (le clic molette n'est pas rendu à l'élément)
        if (!props.IsMiddleButtonPressed) return;
        if (FindScrollable(e.Source as Visual) is not { } sv) return;
        _sv = sv; _anchor = _cur = e.GetPosition(_top); _since = DateTime.UtcNow; _holding = true;
        e.Handled = true;
        _oldCursor = _top.Cursor;
        _top.Cursor = new Cursor(StandardCursorType.SizeAll);
        ShowMarker();
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_sv is null || e.InitialPressMouseButton != MouseButton.Middle || !_holding) return;
        _holding = false;
        // Molette maintenue puis relâchée après un glissement : le défilement s'arrête (comme dans un navigateur) ; un simple clic le laisse actif.
        if ((DateTime.UtcNow - _since).TotalMilliseconds > 350 && Math.Abs(e.GetPosition(_top).Y - _anchor.Y) + Math.Abs(e.GetPosition(_top).X - _anchor.X) > DeadZone) Stop();
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_sv is not null) _cur = e.GetPosition(_top);
    }

    /// <summary>Zone défilante sous le pointeur : la plus proche qui a vraiment de quoi défiler (verticalement ou horizontalement).</summary>
    private static ScrollViewer? FindScrollable(Visual? from)
    {
        for (var v = from; v is not null; v = v.GetVisualParent())
            if (v is ScrollViewer s && (s.Extent.Height > s.Viewport.Height + 1 || s.Extent.Width > s.Viewport.Width + 1)) return s;
        return null;
    }

    private static double Speed(double d)
    {
        var a = Math.Abs(d) - DeadZone;
        if (a <= 0) return 0;
        return Math.Sign(d) * Math.Min(70, Math.Pow(a, 1.25) * 0.22);   // pixels par image : doux près du repère, rapide au loin
    }

    private void Tick()
    {
        if (_sv is null) return;
        var d = _cur - _anchor;
        double vy = _sv.Extent.Height > _sv.Viewport.Height + 1 ? Speed(d.Y) : 0;
        double vx = _sv.Extent.Width > _sv.Viewport.Width + 1 ? Speed(d.X) : 0;
        if (vx == 0 && vy == 0) return;
        var maxY = Math.Max(0, _sv.Extent.Height - _sv.Viewport.Height);
        var maxX = Math.Max(0, _sv.Extent.Width - _sv.Viewport.Width);
        _sv.Offset = new Vector(Math.Clamp(_sv.Offset.X + vx, 0, maxX), Math.Clamp(_sv.Offset.Y + vy, 0, maxY));
    }

    private void ShowMarker()
    {
        if (OverlayLayer.GetOverlayLayer(_top) is not { } layer) return;
        var dot = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(15), IsHitTestVisible = false,
            Background = new SolidColorBrush(Color.Parse("#99120A24")), BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5),
            Child = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M 7,0 L 14,8 L 0,8 Z M 7,22 L 14,14 L 0,14 Z"), Fill = Brushes.White,
                Width = 14, Height = 22, Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(_anchor.X - 15, _anchor.Y - 15, 0, 0),
        };
        layer.Children.Add(dot);
        _marker = dot;
    }

    private void Stop()
    {
        _timer?.Stop(); _timer = null;
        if (_marker is not null && OverlayLayer.GetOverlayLayer(_top) is { } layer) layer.Children.Remove(_marker);
        _marker = null; _sv = null; _holding = false;
        _top.Cursor = _oldCursor;
    }
}
