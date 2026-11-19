using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Path = Avalonia.Controls.Shapes.Path;
using Shape = Avalonia.Controls.Shapes.Shape;

namespace SpamDrafter.App.Views;

/// <summary>
/// Animations des icônes de la barre latérale, jouées en boucle tant que la souris reste sur l'onglet :
/// Accueil (la porte s'ouvre), Bibliothèque (le livre s'ouvre), Deck Builder (les cubes se déplient dans le sens des aiguilles d'une montre, couleurs
/// tirées au hasard à chaque tour), Drafter (les flèches se dessinent de gauche à droite), Contact (les trois points « écrivent »).
/// Accueil et Bibliothèque restent ouverts quand leur onglet est sélectionné. Une seule horloge sert à toutes les icônes ; elle s'arrête quand plus rien ne bouge.
/// Avec « Désactiver les animations », seules les positions « ouvertes » (onglet sélectionné) sont appliquées, sans mouvement.
/// </summary>
public sealed class NavIconAnimator
{
    private abstract class Icon
    {
        public Button Button = null!;
        public double Time;
        public bool Hover => Button.IsPointerOver && !Motion.Reduce;
        public bool Selected => Button.Classes.Contains("on");
        /// <summary>Fait avancer l'icône ; renvoie vrai tant qu'elle bouge encore.</summary>
        public abstract bool Step(double dt);
        protected static bool Approach(ref double value, double target, double dt, double speed)
        {
            value += (target - value) * (1 - Math.Exp(-dt * speed));
            if (Math.Abs(target - value) < 0.003) { value = target; return false; }
            return true;
        }
    }

    /// <summary>Applique une transformation autour d'un point (le dos du livre, la charnière de la porte).</summary>
    private static TransformGroup Around(Transform t, double cx, double cy) =>
        new() { Children = { new TranslateTransform(cx, cy), t, new TranslateTransform(-cx, -cy) } };

    private readonly List<Icon> _icons = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
    private long _last;

    public NavIconAnimator()
    {
        _timer.Tick += (_, _) => Tick();
    }

    private void Add(Button button, Icon icon)
    {
        icon.Button = button;
        _icons.Add(icon);
        button.PointerEntered += (_, _) => Kick();
        button.PointerExited += (_, _) => { icon.Time = 0; Kick(); };
        button.Classes.CollectionChanged += (_, _) => Kick();
    }

    public void Kick()
    {
        if (_timer.IsEnabled) return;
        _last = Environment.TickCount64;
        _timer.Start();
    }

    private void Tick()
    {
        var now = Environment.TickCount64;
        var dt = Math.Min(0.05, (now - _last) / 1000.0);
        _last = now;
        var busy = false;
        foreach (var i in _icons) busy |= i.Step(dt);
        if (!busy) _timer.Stop();
    }

    // ------------------------------------------------------------------ Accueil
    public void AddHome(Button button, Path hole, Path door)
    {
        hole.Fill = new SolidColorBrush(Color.Parse("#4A1E0C"));
        var slide = new TranslateTransform();
        door.RenderTransform = slide;
        Add(button, new HomeIcon(hole, slide));
    }

    private sealed class HomeIcon(Path hole, TranslateTransform slide) : Icon
    {
        private double _open = -1;
        private static readonly Color Dark = Color.Parse("#4A1E0C"), Light = Color.Parse("#E8782E");
        public override bool Step(double dt)
        {
            var hover = Hover;
            if (hover) Time += dt;
            // Au survol : la porte coulisse vers la gauche et disparaît, reste ouverte un instant, revient, et ça recommence (même si l'onglet est sélectionné).
            // Sans survol : ouverte si l'onglet est sélectionné, fermée sinon.
            var target = hover ? (Time % 2.4 < 1.3 ? 1 : 0) : Selected ? 1 : 0;
            var start = _open < 0;
            if (start) _open = target;
            var moving = start ? true : Approach(ref _open, target, dt, 8);
            var o = _open;
            // Porte coulissante : le battant garde sa taille et glisse vers la gauche, rogné à l'embrasure (il disparaît dans le mur) ; l'intérieur apparaît (orange du contour).
            slide.X = -5.6 * o;
            ((SolidColorBrush)hole.Fill!).Color = Color.FromRgb((byte)(Dark.R + (Light.R - Dark.R) * o), (byte)(Dark.G + (Light.G - Dark.G) * o), (byte)(Dark.B + (Light.B - Dark.B) * o));
            return moving || hover;
        }
    }

    // ------------------------------------------------------------------ Bibliothèque
    public void AddBook(Button button, Canvas group, Canvas cover, Control coverOuter, Control coverInner)
    {
        // Le livre se recentre et rétrécit un peu en s'ouvrant (il passe de 13,5 à 27 de large) ; la couverture pivote autour du dos (x = 6).
        var move = new TranslateTransform();
        var scale = new ScaleTransform(1, 1);
        group.RenderTransform = new TransformGroup { Children = { new TranslateTransform(12, 12), scale, new TranslateTransform(-12, -12), move } };
        var flip = new ScaleTransform(1, 1);
        cover.RenderTransform = new TransformGroup { Children = { new TranslateTransform(6, 12), flip, new TranslateTransform(-6, -12) } };
        Add(button, new BookIcon(move, scale, flip, coverOuter, coverInner));
    }

    private sealed class BookIcon(TranslateTransform move, ScaleTransform scale, ScaleTransform flip, Control outer, Control inner) : Icon
    {
        private double _open = -1;
        public override bool Step(double dt)
        {
            var hover = Hover;
            if (hover) Time += dt;
            var target = hover ? (Time % 2.6 < 1.4 ? 1 : 0) : Selected ? 1 : 0;   // survol : s'ouvre et se referme en boucle, même si l'onglet est sélectionné
            var start = _open < 0;
            if (start) _open = target;
            var moving = start ? true : Approach(ref _open, target, dt, 4.5);
            var o = _open;
            var e = o * o * (3 - 2 * o);                          // départ et arrivée en douceur
            move.X = 6 * e;
            scale.ScaleX = scale.ScaleY = 1 - 0.18 * e;
            flip.ScaleX = Math.Cos(Math.PI * e);                  // la couverture tourne comme une vraie page (vue de face : elle s'amincit puis se retourne)
            flip.ScaleY = 1 + 0.07 * Math.Sin(Math.PI * e);       // et se soulève un peu au milieu du mouvement
            var inside = flip.ScaleX < 0;
            outer.IsVisible = !inside;
            inner.IsVisible = inside;
            return moving || hover;
        }
    }

    // ------------------------------------------------------------------ Deck Builder
    private static readonly string[] Palette =
    {
        "#FF6B6B", "#FFA94D", "#FFD84D", "#6EE08A", "#38D9A9", "#4DA3FF", "#748FFC", "#B28DFF", "#FF7AC6", "#22D3EE", "#F783AC", "#A9E34B",
    };

    public void AddBuilder(Button button, Shape topLeft, Shape topRight, Shape bottomRight, Shape bottomLeft)
    {
        // Ordre de dépliage : haut gauche, haut droite, bas droite, bas gauche.
        var squares = new[] { topLeft, topRight, bottomRight, bottomLeft };
        var groups = new TransformGroup[4];
        var scales = new ScaleTransform[4];
        var rots = new RotateTransform[4];
        for (var i = 0; i < 4; i++)
        {
            scales[i] = new ScaleTransform(1, 1); rots[i] = new RotateTransform(0);
            groups[i] = new TransformGroup { Children = { scales[i], rots[i] } };
            squares[i].RenderTransform = groups[i];
            squares[i].RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        }
        Add(button, new BuilderIcon(squares, scales, rots));
    }

    private sealed class BuilderIcon(Shape[] squares, ScaleTransform[] scales, RotateTransform[] rots) : Icon
    {
        private const double Cycle = 2.7;
        private int _cycle = -1;
        private readonly Random _rng = new();
        public override bool Step(double dt)
        {
            if (!Hover)
            {
                if (Time == 0 && scales.All(s => s.ScaleX == 1)) return false;
                Time = 0; _cycle = -1;
                for (var i = 0; i < 4; i++) { scales[i].ScaleX = scales[i].ScaleY = 1; rots[i].Angle = 0; }
                return false;
            }
            Time += dt;
            var cycle = (int)(Time / Cycle);
            if (cycle != _cycle)
            {
                _cycle = cycle;
                var colors = Palette.OrderBy(_ => _rng.Next()).Take(4).ToArray();   // 4 couleurs différentes, nouvelles à chaque tour
                for (var i = 0; i < 4; i++) squares[i].Fill = new SolidColorBrush(Color.Parse(colors[i]));
            }
            var ph = Time % Cycle;
            for (var i = 0; i < 4; i++)
            {
                var u = Math.Clamp((ph - i * 0.3) / 0.45, 0, 1);              // chacun à son tour
                var grow = BackOut(u);
                var fold = Math.Clamp((ph - 2.15) / 0.4, 0, 1);               // puis tous se replient avant le tour suivant
                var s = Math.Max(0, grow) * (1 - fold * fold);
                scales[i].ScaleX = scales[i].ScaleY = s;
                rots[i].Angle = -75 * (1 - u) + 40 * fold;
            }
            return true;
        }
        private static double BackOut(double x) { const double c1 = 1.70158, c3 = c1 + 1; return 1 + c3 * Math.Pow(x - 1, 3) + c1 * Math.Pow(x - 1, 2); }
    }

    // ------------------------------------------------------------------ Drafter
    public void AddDrafter(Button button, Canvas canvas, Path pink, Path headPink, Path purpleA, Path purpleB, Path headPurple)
        => Add(button, new DrafterIcon(canvas, pink, headPink, purpleA, purpleB, headPurple));

    private sealed class DrafterIcon(Canvas canvas, Path pink, Path headPink, Path purpleA, Path purpleB, Path headPurple) : Icon
    {
        private const double Cycle = 2.5;
        private int _cycle = -1;
        private readonly Random _rng = new();
        private bool _active;
        public override bool Step(double dt)
        {
            if (!Hover)
            {
                if (!_active) return false;
                _active = false; Time = 0; _cycle = -1;
                canvas.Clip = null; canvas.Opacity = 1;
                return false;
            }
            _active = true;
            Time += dt;
            if (_cycle < 0)
            {
                // Chaque flèche reçoit une couleur au hasard (deux différentes), nouvelle à chaque nouveau survol.
                _cycle = 0;
                var c = Palette.OrderBy(_ => _rng.Next()).Take(2).Select(x => new SolidColorBrush(Color.Parse(x))).ToArray();
                pink.Stroke = headPink.Fill = c[0];
                purpleA.Stroke = purpleB.Stroke = headPurple.Fill = c[1];
            }
            var ph = Time % Cycle;
            // Les deux flèches se dessinent ensemble, de gauche à droite : une fenêtre de révélation avance sur toute la largeur de l'icône
            // (les pointes apparaissent donc naturellement quand le tracé les atteint), puis pause, puis fondu avant de recommencer.
            var u = Math.Clamp(ph / 1.4, 0, 1);
            var eased = 1 - (1 - u) * (1 - u);   // démarrage franc (pas de longue hésitation sur les points de départ), arrivée douce
            var x = 1.0 + eased * 23.0;
            canvas.Clip = new RectangleGeometry(new Rect(0, 0, x, 24));
            canvas.Opacity = 1 - Math.Clamp((ph - 2.0) / 0.4, 0, 1);
            return true;
        }
    }

    // ------------------------------------------------------------------ Contact
    public void AddContact(Button button, Shape dot1, Shape dot2, Shape dot3)
    {
        var dots = new[] { dot1, dot2, dot3 };
        var moves = new TranslateTransform[3];
        for (var i = 0; i < 3; i++) { moves[i] = new TranslateTransform(); dots[i].RenderTransform = moves[i]; }
        Add(button, new ContactIcon(dots, moves));
    }

    private sealed class ContactIcon(Shape[] dots, TranslateTransform[] moves) : Icon
    {
        private bool _active;
        public override bool Step(double dt)
        {
            if (!Hover)
            {
                if (!_active) return false;
                _active = false; Time = 0;
                for (var i = 0; i < 3; i++) { moves[i].Y = 0; dots[i].Opacity = 1; }
                return false;
            }
            _active = true;
            Time += dt;
            // « Quelqu'un écrit… » : les trois points montent et s'allument l'un après l'autre, en vague.
            for (var i = 0; i < 3; i++)
            {
                var w = Math.Max(0, Math.Sin(Time * 6.0 - i * 0.95));
                moves[i].Y = -2.6 * w;
                dots[i].Opacity = 0.35 + 0.65 * w;
            }
            return true;
        }
    }
}
