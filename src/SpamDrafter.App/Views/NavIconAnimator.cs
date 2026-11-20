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
        public bool Hover => Button.IsPointerOver && !Motion.Reduce && !Button.Classes.Contains("locked");
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

    // ------------------------------------------------------------------ Challenges
    /// <summary>La roue tourne en boucle tant que l'onglet est survolé ; à la fin de chaque tour elle s'arrête sur une couleur tirée au hasard, sous la flèche.</summary>
    public void AddWheel(Button button, Canvas wheel)
    {
        var rot = new RotateTransform(0);
        wheel.RenderTransform = rot;
        wheel.RenderTransformOrigin = new RelativePoint(0.5, 13.0 / 24, RelativeUnit.Relative);
        Add(button, new WheelIcon(rot));
    }

    private sealed class WheelIcon(RotateTransform rot) : Icon
    {
        private const double Cycle = 2.8, SpinTime = 2.0;
        private int _cycle = -1;
        private double _from, _to;
        private readonly Random _rng = new();
        public override bool Step(double dt)
        {
            if (!Hover)
            {
                if (_cycle < 0 && rot.Angle == 0) return false;
                Time = 0; _cycle = -1; _from = _to = 0; rot.Angle = 0;
                return false;
            }
            Time += dt;
            var cycle = (int)(Time / Cycle);
            if (cycle != _cycle)
            {
                _cycle = cycle;
                // Cinq parts de 72° (rose, bleu, vert, jaune, violet) : la flèche est en haut, la part tirée finit dessous.
                var k = _rng.Next(5);
                _from = _to % 360;
                var target = (360 - (72 * k + 36)) % 360;
                var delta = ((target - _from) % 360 + 360) % 360;
                _to = _from + 720 + delta;
            }
            var u = Math.Clamp(Time % Cycle / SpinTime, 0, 1);
            rot.Angle = _from + (_to - _from) * (1 - Math.Pow(1 - u, 4));
            return true;
        }
    }

    // ------------------------------------------------------------------ Jeux
    /// <summary>
    /// Le dé : au survol il est lancé (il saute, rebondit, tourne et change vite de face) puis retombe sur une face de 1 à 6 tirée au hasard, et recommence.
    /// Au repos, il montre le 5. Les 7 points sont posés sur une grille 3 × 3 centrée sur le dé, dans l'ordre : haut-gauche, haut-droite, milieu-gauche, centre,
    /// milieu-droite, bas-gauche, bas-droite (chaque face est symétrique par rapport au centre du dé).
    /// </summary>
    public void AddDice(Button button, Canvas die, Shape[] pips)
    {
        var rot = new RotateTransform(0);
        var scale = new ScaleTransform(1, 1);
        var move = new TranslateTransform(0, 0);
        die.RenderTransform = new TransformGroup { Children = { scale, rot, move } };
        die.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        Add(button, new DiceIcon(rot, scale, move, pips));
    }

    private sealed class DiceIcon : Icon
    {
        private static readonly int[][] Faces =
        [
            [], [3], [0, 6], [0, 3, 6], [0, 1, 5, 6], [0, 1, 3, 5, 6], [0, 1, 2, 4, 5, 6],
        ];
        private const double Cycle = 3.7, RollTime = 2.35;
        private readonly RotateTransform _rot;
        private readonly ScaleTransform _scale;
        private readonly TranslateTransform _move;
        private readonly Shape[] _pips;
        private readonly Random _rng = new();
        private int _cycle = -1, _face = 5, _final = 5;
        private double _spins, _nextChange;

        public DiceIcon(RotateTransform rot, ScaleTransform scale, TranslateTransform move, Shape[] pips)
        {
            _rot = rot; _scale = scale; _move = move; _pips = pips; Show(5);
        }

        private void Show(int face)
        {
            _face = face;
            for (var i = 0; i < _pips.Length; i++) _pips[i].IsVisible = Faces[face].Contains(i);
        }

        private void Rest()
        {
            _rot.Angle = 0; _scale.ScaleX = _scale.ScaleY = 1; _move.X = 0; _move.Y = 0;
        }

        public override bool Step(double dt)
        {
            if (!Hover)
            {
                if (_cycle < 0 && _face == 5 && _rot.Angle == 0 && _move.Y == 0) return false;
                Time = 0; _cycle = -1; Rest(); Show(5);
                return false;
            }
            Time += dt;
            var cycle = (int)(Time / Cycle);
            if (cycle != _cycle)
            {
                _cycle = cycle; _nextChange = 0;
                _final = _rng.Next(1, 7);                                   // face d'arrivée : de 1 à 6, au hasard
                _spins = 360 * (1 + _rng.Next(2)) * (_rng.Next(2) == 0 ? 1 : -1);
            }
            var t = Time % Cycle;
            if (t < RollTime)
            {
                var u = t / RollTime;
                _rot.Angle = _spins * (1 - Math.Pow(1 - u, 3));               // tourne en ralentissant
                // Deux rebonds qui s'amortissent, avec un léger grossissement en l'air.
                var hop = Math.Abs(Math.Sin(u * 2 * Math.PI)) * 4.0 * (1 - u);
                _move.Y = -hop;
                _scale.ScaleX = _scale.ScaleY = 1 + 0.12 * hop / 4.0;
                // Les faces défilent de plus en plus lentement, la dernière est la face tirée.
                if (t >= _nextChange)
                {
                    if (u >= 0.88) { if (_face != _final) Show(_final); _nextChange = double.MaxValue; }
                    else
                    {
                        int f; do { f = _rng.Next(1, 7); } while (f == _face || f == _final && u > 0.8);
                        Show(f);
                        _nextChange = t + 0.13 + 0.28 * u * u;
                    }
                }
            }
            else { Rest(); if (_face != _final) Show(_final); }
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
