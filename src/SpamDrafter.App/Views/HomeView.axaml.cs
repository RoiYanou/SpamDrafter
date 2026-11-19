using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SpamDrafter.App.ViewModels;

namespace SpamDrafter.App.Views;

public partial class HomeView : UserControl
{
    private enum TipEffect { Wave, Shine, Caterpillar, Rainbow, Bounce, Pulse }

    /// <summary>Une lettre du bandeau : son bloc de texte, ses deux transformations (déplacement, échelle) et sa couleur de base.</summary>
    private sealed class Glyph(TextBlock block, TranslateTransform move, ScaleTransform scale, Color baseColor, bool isLink)
    {
        public TextBlock Block = block; public TranslateTransform Move = move; public ScaleTransform Scale = scale;
        public Color Base = baseColor; public bool IsLink = isLink; public int Level = -1;
    }

    private const double Speed = 90;   // pixels par seconde
    private readonly StackPanel _tip = new() { Orientation = Orientation.Horizontal };
    private readonly List<Glyph> _glyphs = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DateTime _start = DateTime.UtcNow;
    private DateTime _last;
    private double _x;
    private bool _paused, _needsStart = true;
    private TipEffect _effect;

    private readonly DispatcherTimer _greetTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DateTime _greetStart = DateTime.UtcNow;

    /// <summary>Couleur animée du bonjour : une teinte au hasard qui ondule et respire ; 1 fois sur 100, un arc-en-ciel qui défile.</summary>
    private void AnimateGreeting()
    {
        if (DataContext is not ViewModels.HomeViewModel vm) return;
        var t = Motion.Reduce ? 0 : (DateTime.UtcNow - _greetStart).TotalSeconds;
        var color = vm.GreetingRainbow
            ? FromHsv((t * 70) % 360, 0.75, 1)
            : FromHsv((vm.GreetingHue + 14 * Math.Sin(t * 1.3) + 360) % 360, 0.55 + 0.15 * Math.Sin(t * 0.9), 0.9 + 0.1 * Math.Sin(t * 2.1));
        GreetingText.Foreground = new SolidColorBrush(color);
    }

    private static Color FromHsv(double h, double s, double v)
    {
        var c = v * s; var x = c * (1 - Math.Abs(h / 60 % 2 - 1)); var m = v - c;
        var (r, g, b) = (int)(h / 60) switch { 0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x), 3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x) };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    public HomeView()
    {
        InitializeComponent();
        _greetTimer.Tick += (_, _) => AnimateGreeting();
        AttachedToVisualTree += (_, _) => { AnimateGreeting(); _greetTimer.Start(); };
        DetachedFromVisualTree += (_, _) => _greetTimer.Stop();
        // Un clic sur la carte du jour l'ouvre en grand ; un clic sur le fond assombri la referme.
        Tilt.Attach(CardOfDayBox); Tilt.Attach(ZoomBox);
        CardOfDayBox.PointerPressed += (_, _) => (DataContext as ViewModels.HomeViewModel)?.OpenZoomCommand.Execute(null);
        ZoomBackdrop.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.Source, ZoomBackdrop)) (DataContext as ViewModels.HomeViewModel)?.CloseZoomCommand.Execute(null);
        };

        // L'ornement de la carte du jour suit la couleur secondaire du thème.
        void Glow()
        {
            var a = ThemeManager.Accent;
            CardOfDayPanel.BoxShadow = BoxShadows.Parse($"0 0 22 2 #90{a.R:X2}{a.G:X2}{a.B:X2}, inset 0 0 18 0 #30{a.R:X2}{a.G:X2}{a.B:X2}");
        }
        Glow();
        ThemeManager.Changed += Glow;
        DetachedFromVisualTree += (_, _) => ThemeManager.Changed -= Glow;
        AttachedToVisualTree += (_, _) => { ThemeManager.Changed -= Glow; ThemeManager.Changed += Glow; Glow(); };
        TipTrack.Children.Add(_tip);
        TipViewport.PointerEntered += (_, _) => _paused = true;
        TipViewport.PointerExited += (_, _) => _paused = false;
        _timer.Tick += (_, _) => Step();
        DataContextChanged += (_, _) => { if (DataContext is HomeViewModel vm) Build(vm, vm.TipSegments); };
        AttachedToVisualTree += (_, _) => { _last = DateTime.UtcNow; _timer.Start(); };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    private static Color Colour(TipKind kind) => kind switch
    {
        TipKind.Card => Colors.Gold,
        TipKind.Ability => Color.Parse("#FF9F5A"),
        TipKind.Talent => Color.Parse("#82E9A8"),
        TipKind.Tox => Color.Parse("#FF9AE0"),
        _ => Colors.White,
    };

    /// <summary>Une astuce, lettre par lettre (pour animer chaque lettre) ; un effet au hasard est choisi pour ce passage.</summary>
    private void Build(HomeViewModel vm, IReadOnlyList<TipSegment> segments)
    {
        _tip.Children.Clear(); _glyphs.Clear();
        _effect = (TipEffect)Random.Shared.Next(Enum.GetValues<TipEffect>().Length);
        foreach (var seg in segments)
        {
            var colour = Colour(seg.Kind);
            foreach (var ch in seg.Text)
            {
                var move = new TranslateTransform(); var scale = new ScaleTransform();
                var block = new TextBlock
                {
                    Text = ch.ToString(), FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(colour),
                    RenderTransform = new TransformGroup { Children = { scale, move } },
                };
                if (seg.IsLink)
                {
                    block.FontWeight = FontWeight.Bold;
                    block.TextDecorations = TextDecorations.Underline;
                    block.Cursor = new Cursor(StandardCursorType.Hand);
                    ToolTip.SetTip(block, "Voir dans la Bibliothèque");
                    block.PointerPressed += (_, e) => { vm.OpenTip(seg); e.Handled = true; };
                }
                _tip.Children.Add(block);
                _glyphs.Add(new Glyph(block, move, scale, colour, seg.IsLink));
            }
        }
        _tip.Measure(Size.Infinity);
        _needsStart = true;   // repart du bord droit au prochain pas
    }

    private void Step()
    {
        var now = DateTime.UtcNow;
        var dt = Math.Min((now - _last).TotalSeconds, 0.1);
        _last = now;
        var view = TipViewport.Bounds.Width;
        if (view <= 0 || _glyphs.Count == 0) return;
        var width = _tip.DesiredSize.Width;
        if (_needsStart) { _x = view; _needsStart = false; }
        if (!_paused) _x -= Speed * dt;
        // Sorti à gauche : nouvelle astuce au hasard et nouvel effet, qui revient par la droite.
        if (_x < -width && DataContext is HomeViewModel vm) { Build(vm, vm.NextTip()); return; }
        Canvas.SetLeft(_tip, _x);
        Animate((now - _start).TotalSeconds);
    }

    private void Animate(double t)
    {
        var n = _glyphs.Count;
        for (var i = 0; i < n; i++)
        {
            var g = _glyphs[i];
            double dx = 0, dy = 0, sc = 1, shine = 0;
            Color? tint = null;
            switch (_effect)
            {
                case TipEffect.Wave: dy = Math.Sin(t * 6 + i * 0.55) * 5; break;
                case TipEffect.Shine: { var p = (t * 28) % (n + 24) - 12; shine = Math.Max(0, 1 - Math.Abs(i - p) / 6); break; }
                case TipEffect.Caterpillar: dx = Math.Sin(t * 4.5 - i * 0.4) * 5; sc = 1 + 0.12 * Math.Sin(t * 4.5 - i * 0.4); break;
                case TipEffect.Rainbow: if (!g.IsLink) tint = new HsvColor(1, (t * 140 + i * 14) % 360, 0.55, 1).ToRgb(); else shine = 0.5 + 0.5 * Math.Sin(t * 5 + i * 0.3); break;
                case TipEffect.Bounce: dy = -Math.Abs(Math.Sin(t * 4 - i * 0.35)) * 8; break;
                case TipEffect.Pulse: sc = 1 + 0.3 * Math.Max(0, Math.Sin(t * 5 - i * 0.3)); break;
            }
            g.Move.X = dx; g.Move.Y = dy; g.Scale.ScaleX = g.Scale.ScaleY = sc;
            // Couleur : recalculée seulement si elle a changé (peu de niveaux), pour ne pas recréer des pinceaux à chaque image.
            var level = tint is { } c ? (c.R << 16 | c.G << 8 | c.B) : (int)Math.Round(shine * 10);
            if (level == g.Level) continue;
            g.Level = level;
            var colour = tint ?? Lerp(g.Base, Colors.White, shine);
            g.Block.Foreground = new SolidColorBrush(colour);
        }
    }

    private static Color Lerp(Color a, Color b, double k) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));
}
