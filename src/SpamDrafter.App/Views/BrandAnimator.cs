using Avalonia;
using Avalonia.Controls;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Media;
using Avalonia.Threading;

namespace SpamDrafter.App.Views;

/// <summary>
/// Animation du logo de la barre latérale au survol : les deux cartes se redressent ; la carte rose (derrière la jaune) glisse vers la droite
/// par-dessus le mot en dévoilant les lettres « pamDrafter » comme un rideau, et s'arrête juste après la dernière : le logo « SpamDrafter » est formé.
/// Les rayons lumineux de la bannière apparaissent alors, centrés derrière le logo, et tournent. Au repli (fin du survol), les rayons disparaissent
/// vite et la carte rose revient derrière la carte « S » en ramenant les lettres avec elle (la ligne de temps <c>T</c> se rejoue à l'envers).
/// Avec « Désactiver les animations », le logo passe directement à son état final ou initial.
/// </summary>
public sealed class BrandAnimator
{
    private const double Total = 0.92;   // les cartes se redressent (0,30 s), puis la rose glisse jusqu'au bout du mot (0,62 s)
    private const double LettersLeft = 41;   // distance du bord gauche de la zone au premier « p » (collé à la carte « S »)
    private readonly Path _pink, _yellow, _glyph, _rays, _crown;
    private readonly Control _pinkBox, _lettersClip, _raysBox, _lettersRow;
    private readonly RotateTransform _pinkRot = new(14, 19, 20), _yellowRot = new(-8, 19, 20), _glyphRot = new(-8, 19, 20), _raysRot = new(0, 0, 0);
    private readonly ScaleTransform _raysScale = new(0, 0);
    private readonly TranslateTransform _pinkMove = new(), _raysMove = new(), _shift = new(), _pinkShift = new();   // _shift : décalage qui centre le logo formé
    private readonly Control? _yellowBox;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
    private double _t, _r;   // _t : cartes et lettres ; _r : rayons (démarrent une fois le logo formé)
    private long _last;
    private double _lettersWidth;

    public bool Hover { get; set; }

    public BrandAnimator(Path pink, Path yellow, Path glyph, Control pinkBox, Control lettersClip, Control lettersRow, Control raysBox, Path rays, Path crown)
    {
        _crown = crown; _pink = pink; _yellow = yellow; _glyph = glyph; _pinkBox = pinkBox; _lettersClip = lettersClip; _lettersRow = lettersRow; _raysBox = raysBox; _rays = rays;
        // Les transformations utilisent leurs propres centres : l'origine par défaut (le milieu de chaque tracé) les décalerait.
        foreach (var shape in new[] { _pink, _yellow, _glyph, _rays, _crown }) shape.RenderTransformOrigin = RelativePoint.TopLeft;
        _crown.RenderTransform = _pinkRot;   // la couronne suit l'inclinaison de la carte rose
        _pink.RenderTransform = _pinkRot; _yellow.RenderTransform = _yellowRot; _glyph.RenderTransform = _glyphRot;
        _pinkBox.RenderTransform = new TransformGroup { Children = { _pinkMove, _pinkShift } };
        _lettersClip.RenderTransform = _shift;
        _yellowBox = _yellow.Parent?.Parent as Control;   // la boîte (Viewbox) de la carte jaune
        if (_yellowBox is not null) _yellowBox.RenderTransform = _shift;
        _rays.RenderTransform = new TransformGroup { Children = { _raysScale, _raysRot, _raysMove } };   // d'abord l'échelle (les rayons sortent du centre), puis la rotation, puis le placement
        _timer.Tick += (_, _) => Step();
        Apply();
    }

    /// <summary>Relance la boucle d'animation (appelé quand l'état de survol change).</summary>
    public void Kick()
    {
        if (_timer.IsEnabled) return;
        _lettersWidth = 0;   // remesure la largeur du mot (la police est chargée à ce stade) pour bien centrer les rayons
        _last = Environment.TickCount64;
        _timer.Start();
    }

    private void Step()
    {
        var now = Environment.TickCount64;
        var dt = Math.Min(0.05, (now - _last) / 1000.0);
        _last = now;
        if (Motion.Reduce) { _t = Hover ? Total : 0; _r = Hover ? 1 : 0; }
        else
        {
            _t = Math.Clamp(_t + (Hover ? dt : -dt * 1.5), 0, Total);
            // Les rayons apparaissent juste après la formation du logo, et disparaissent vite dès que la barre se replie.
            _r = Hover && _t >= Total ? Math.Min(1, _r + dt / 0.6) : Math.Max(0, _r - dt / 0.18);
        }
        if (_raysScale.ScaleX > 0.01 && !Motion.Reduce) _raysRot.Angle = (_raysRot.Angle + dt * 28) % 360;
        Apply();
        if (!Hover && _t <= 0 && _r <= 0) _timer.Stop();
    }

    private static double Clamp01(double v) => Math.Clamp(v, 0, 1);
    private static double Ease(double x) => 1 - Math.Pow(1 - Clamp01(x), 3);
    private static double InOut(double x) { x = Clamp01(x); return x < 0.5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2; }

    private void Apply()
    {
        if (_lettersWidth <= 0)
        {
            _lettersRow.Measure(Size.Infinity);
            _lettersWidth = _lettersRow.DesiredSize.Width;
            // Les rayons tournent autour du centre exact du logo complet : du bord gauche de la carte « S » au bord droit de la dernière lettre,
            // à mi-hauteur de la zone (positions réelles une fois la fenêtre mise en page).
            var left = (_pinkBox.Bounds.Width > 0 ? _pinkBox.Bounds.X + _pinkBox.Bounds.Width * 0.2 : 11 + 7.6);
            var textLeft = _lettersClip.Bounds.Width > 0 || _lettersClip.Bounds.X > 0 ? _lettersClip.Bounds.X : LettersLeft;
            _raysMove.Y = _raysBox.Bounds.Height > 0 ? _raysBox.Bounds.Height / 2 : 24;
        }
        // Logo formé = centré dans la barre : le décalage grandit avec le mot et vaut 0 au repos (la carte « S » reste alors à sa place) ; il peut être négatif, le logo formé étant plus large que l'espace où la carte « S » repose.
        var boxW = _raysBox.Bounds.Width;
        var logoW = _lettersWidth + 49.1;                // du bord gauche de la carte « S » au bord droit de la carte rose, une fois le mot formé
        var centerShift = boxW > 0 ? Math.Max(-14, (boxW - logoW) / 2 - 17.2) : 0;
        var straight = Ease(_t / 0.30);                  // 1. les cartes se redressent
        var outward = InOut((_t - 0.30) / 0.62);         // 2. la rose glisse vers la droite, par-dessus le mot
        var rays = Ease(_r);                             // 3. une fois le logo formé, les rayons sortent du centre et s'étirent
        // Au repli, tout se rejoue à l'envers : la carte rose revient derrière la jaune en ramenant les lettres avec elle.

        _yellowRot.Angle = -8 * (1 - straight);
        _glyphRot.Angle = -8 * (1 - straight);
        _pinkRot.Angle = 14 * (1 - straight) + 7 * Math.Sin(Math.PI * outward);
        var end = _lettersWidth + 24;                    // la carte dépasse un peu la dernière lettre
        _shift.X = centerShift * outward;
        _pinkShift.X = centerShift * outward;
        if (boxW > 0) _raysMove.X = boxW / 2;            // les rayons tournent autour du centre de la barre, donc du logo centré
        _pinkMove.X = outward * end;                     // elle s'arrête juste après la dernière lettre : le logo « SpamDrafter » est formé
        // Les lettres apparaissent à gauche de la carte rose, comme découvertes par elle (et restent ensuite).
        _lettersClip.Width = Math.Clamp(outward * end - 6, 0, _lettersWidth);
        _raysScale.ScaleX = _raysScale.ScaleY = rays;
        _crown.Opacity = Clamp01((_t - 0.05) / 0.25);   // la couronne apparaît quand les cartes se redressent
    }
}
