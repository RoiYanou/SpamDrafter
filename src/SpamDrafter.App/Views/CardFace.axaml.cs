using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using SpamDrafter.App.ViewModels;

namespace SpamDrafter.App.Views;

/// <summary>
/// Une carte à cadre de rareté (phase 13). Les tailles du nom, des pastilles et des coins suivent la largeur de la carte.
/// Reflet holographique : seulement sur la fiche détaillée (<see cref="Holo"/>). Au survol, un exemplaire plus grand flotte au-dessus
/// de tout (une fenêtre flottante, donc jamais rognée par la liste qui contient la carte) ; « Réduire les animations » le coupe.
/// </summary>
public partial class CardFace : UserControl
{
    public static readonly StyledProperty<CardItem?> ItemProperty = AvaloniaProperty.Register<CardFace, CardItem?>(nameof(Item));
    /// <summary>Image à montrer à la place de la miniature de la carte (skin, illustration complète).</summary>
    public static readonly StyledProperty<IImage?> ImageOverrideProperty = AvaloniaProperty.Register<CardFace, IImage?>(nameof(ImageOverride));
    public static readonly StyledProperty<bool> ShowNameProperty = AvaloniaProperty.Register<CardFace, bool>(nameof(ShowName));
    public static readonly StyledProperty<bool> ShowElementProperty = AvaloniaProperty.Register<CardFace, bool>(nameof(ShowElement));
    public static readonly StyledProperty<bool> ShowRarityProperty = AvaloniaProperty.Register<CardFace, bool>(nameof(ShowRarity), true);
    /// <summary>Le revenu en Spamtons (icône + nombre), en haut à droite.</summary>
    public static readonly StyledProperty<bool> ShowIncomeProperty = AvaloniaProperty.Register<CardFace, bool>(nameof(ShowIncome));
    /// <summary>Étiquette du talent choisi (« T2 »), en haut à droite ; vide = pas d'étiquette.</summary>
    public static readonly StyledProperty<string?> TalentTagProperty = AvaloniaProperty.Register<CardFace, string?>(nameof(TalentTag));
    /// <summary>Nom du talent, écrit sous l'étiquette (présentation d'un deck).</summary>
    public static readonly StyledProperty<string?> TalentNameProperty = AvaloniaProperty.Register<CardFace, string?>(nameof(TalentName));
    public static readonly StyledProperty<string?> TooltipTextProperty = AvaloniaProperty.Register<CardFace, string?>(nameof(TooltipText));
    public static readonly StyledProperty<bool> HoverZoomProperty = AvaloniaProperty.Register<CardFace, bool>(nameof(HoverZoom), true);
    /// <summary>« Illustration : Nom » : la signature de l'artiste, en bas à droite de l'illustration.</summary>
    public static readonly StyledProperty<string?> CreditProperty = AvaloniaProperty.Register<CardFace, string?>(nameof(Credit));
    /// <summary>Talent en bas à gauche de la carte au lieu du haut à droite.</summary>
    public static readonly StyledProperty<bool> TalentAtBottomProperty = AvaloniaProperty.Register<CardFace, bool>(nameof(TalentAtBottom));
    public static readonly StyledProperty<string?> TopTagProperty = AvaloniaProperty.Register<CardFace, string?>(nameof(TopTag));
    public static readonly StyledProperty<string?> BottomTagProperty = AvaloniaProperty.Register<CardFace, string?>(nameof(BottomTag));
    public static readonly StyledProperty<Thickness> FrameThicknessProperty = AvaloniaProperty.Register<CardFace, Thickness>(nameof(FrameThickness), new Thickness(2));
    public static readonly StyledProperty<Thickness> RarityMarginProperty = AvaloniaProperty.Register<CardFace, Thickness>(nameof(RarityMargin));
    public static readonly StyledProperty<Thickness> ImageMarginProperty = AvaloniaProperty.Register<CardFace, Thickness>(nameof(ImageMargin));
    public static readonly StyledProperty<CornerRadius> ImageCornerProperty = AvaloniaProperty.Register<CardFace, CornerRadius>(nameof(ImageCorner), new CornerRadius(8));
    public static readonly StyledProperty<Thickness> CreditMarginProperty = AvaloniaProperty.Register<CardFace, Thickness>(nameof(CreditMargin));
    public static readonly StyledProperty<Thickness> BottomLeftMarginProperty = AvaloniaProperty.Register<CardFace, Thickness>(nameof(BottomLeftMargin));
    public static readonly StyledProperty<double> CreditSizeProperty = AvaloniaProperty.Register<CardFace, double>(nameof(CreditSize), 9);
    public static readonly StyledProperty<bool> HoloProperty = AvaloniaProperty.Register<CardFace, bool>(nameof(Holo));

    public static readonly StyledProperty<CornerRadius> CornerProperty = AvaloniaProperty.Register<CardFace, CornerRadius>(nameof(Corner), new CornerRadius(10));
    public static readonly StyledProperty<double> NameSizeProperty = AvaloniaProperty.Register<CardFace, double>(nameof(NameSize), 11);
    public static readonly StyledProperty<double> GlyphProperty = AvaloniaProperty.Register<CardFace, double>(nameof(Glyph), 22);
    public static readonly StyledProperty<double> RarityLogoProperty = AvaloniaProperty.Register<CardFace, double>(nameof(RarityLogo), 10);
    public static readonly StyledProperty<Thickness> RarityLogoMarginProperty = AvaloniaProperty.Register<CardFace, Thickness>(nameof(RarityLogoMargin));
    public static readonly StyledProperty<Thickness> BodyReserveProperty = AvaloniaProperty.Register<CardFace, Thickness>(nameof(BodyReserve));
    /// <summary>Espace réservé sous le corps de la carte pour la moitié basse du logo de rareté.</summary>
    public Thickness BodyReserve { get => GetValue(BodyReserveProperty); private set => SetValue(BodyReserveProperty, value); }
    public static readonly StyledProperty<double> RarityLogoHeightProperty = AvaloniaProperty.Register<CardFace, double>(nameof(RarityLogoHeight), 5);
    /// <summary>Hauteur du logo : les images de rareté sont deux fois plus larges que hautes.</summary>
    public double RarityLogoHeight { get => GetValue(RarityLogoHeightProperty); private set => SetValue(RarityLogoHeightProperty, value); }
    public double RarityLogo { get => GetValue(RarityLogoProperty); private set => SetValue(RarityLogoProperty, value); }
    public Thickness RarityLogoMargin { get => GetValue(RarityLogoMarginProperty); private set => SetValue(RarityLogoMarginProperty, value); }
    public static readonly StyledProperty<double> RarityBadgeProperty = AvaloniaProperty.Register<CardFace, double>(nameof(RarityBadge), 26);
    public static readonly StyledProperty<double> TagSizeProperty = AvaloniaProperty.Register<CardFace, double>(nameof(TagSize), 11);
    public static readonly StyledProperty<double> TalentMaxWidthProperty = AvaloniaProperty.Register<CardFace, double>(nameof(TalentMaxWidth), 80);
    public static readonly StyledProperty<Thickness> OverlayMarginProperty = AvaloniaProperty.Register<CardFace, Thickness>(nameof(OverlayMargin), new Thickness(4));
    public static readonly StyledProperty<Thickness> BodyMarginProperty = AvaloniaProperty.Register<CardFace, Thickness>(nameof(BodyMargin));

    public CardItem? Item { get => GetValue(ItemProperty); set => SetValue(ItemProperty, value); }
    public IImage? ImageOverride { get => GetValue(ImageOverrideProperty); set => SetValue(ImageOverrideProperty, value); }
    public bool ShowName { get => GetValue(ShowNameProperty); set => SetValue(ShowNameProperty, value); }
    public bool ShowElement { get => GetValue(ShowElementProperty); set => SetValue(ShowElementProperty, value); }
    public bool ShowRarity { get => GetValue(ShowRarityProperty); set => SetValue(ShowRarityProperty, value); }
    public bool ShowIncome { get => GetValue(ShowIncomeProperty); set => SetValue(ShowIncomeProperty, value); }
    public string? TalentTag { get => GetValue(TalentTagProperty); set => SetValue(TalentTagProperty, value); }
    public string? TalentName { get => GetValue(TalentNameProperty); set => SetValue(TalentNameProperty, value); }
    public string? TooltipText { get => GetValue(TooltipTextProperty); set => SetValue(TooltipTextProperty, value); }
    public bool HoverZoom { get => GetValue(HoverZoomProperty); set => SetValue(HoverZoomProperty, value); }
    public string? Credit { get => GetValue(CreditProperty); set => SetValue(CreditProperty, value); }
    public bool TalentAtBottom { get => GetValue(TalentAtBottomProperty); set => SetValue(TalentAtBottomProperty, value); }
    public string? TopTag { get => GetValue(TopTagProperty); private set => SetValue(TopTagProperty, value); }
    public string? BottomTag { get => GetValue(BottomTagProperty); private set => SetValue(BottomTagProperty, value); }
    public static readonly StyledProperty<Thickness> HpMarginProperty = AvaloniaProperty.Register<CardFace, Thickness>(nameof(HpMargin));
    public Thickness HpMargin { get => GetValue(HpMarginProperty); private set => SetValue(HpMarginProperty, value); }
    public static readonly StyledProperty<double> HpSizeProperty = AvaloniaProperty.Register<CardFace, double>(nameof(HpSize), 8);
    public double HpSize { get => GetValue(HpSizeProperty); private set => SetValue(HpSizeProperty, value); }
    public static readonly StyledProperty<bool> HpVisibleProperty = AvaloniaProperty.Register<CardFace, bool>(nameof(HpVisible));
    /// <summary>Les PV (« ♥ 500 PV ») s'affichent dans le coin bas gauche des cartes qui montrent leurs Spamtons, sauf là où l'étiquette d'un talent occupe ce coin.</summary>
    public bool HpVisible { get => GetValue(HpVisibleProperty); private set => SetValue(HpVisibleProperty, value); }
    /// <summary>Épaisseur du cadre : la même pour toutes les raretés, proportionnelle à la taille d'affichage de la carte.</summary>
    public Thickness FrameThickness { get => GetValue(FrameThicknessProperty); private set => SetValue(FrameThicknessProperty, value); }
    /// <summary>L'illustration est rentrée sous le cadre (moitié de son épaisseur) et rognée un peu plus serré : ses coins ne dépassent jamais.</summary>
    /// <summary>Le symbole est remonté pour que son dessin (placé bas dans son image) chevauche le milieu du bord du cadre.</summary>
    public static readonly StyledProperty<double> RarityHeightProperty = AvaloniaProperty.Register<CardFace, double>(nameof(RarityHeight));
    public double RarityHeight { get => GetValue(RarityHeightProperty); private set => SetValue(RarityHeightProperty, value); }
    public static readonly StyledProperty<IBrush?> FrameShineProperty = AvaloniaProperty.Register<CardFace, IBrush?>(nameof(FrameShine));
    /// <summary>Cadre « métallique » : la couleur de la rareté avec des reflets clairs et sombres (un dégradé qui glisse au survol).</summary>
    public IBrush? FrameShine { get => GetValue(FrameShineProperty); private set => SetValue(FrameShineProperty, value); }
    public Thickness RarityMargin { get => GetValue(RarityMarginProperty); private set => SetValue(RarityMarginProperty, value); }
    public Thickness ImageMargin { get => GetValue(ImageMarginProperty); private set => SetValue(ImageMarginProperty, value); }
    public CornerRadius ImageCorner { get => GetValue(ImageCornerProperty); private set => SetValue(ImageCornerProperty, value); }
    public Thickness CreditMargin { get => GetValue(CreditMarginProperty); private set => SetValue(CreditMarginProperty, value); }
    public Thickness BottomLeftMargin { get => GetValue(BottomLeftMarginProperty); private set => SetValue(BottomLeftMarginProperty, value); }
    public double CreditSize { get => GetValue(CreditSizeProperty); private set => SetValue(CreditSizeProperty, value); }
    public bool Holo { get => GetValue(HoloProperty); set => SetValue(HoloProperty, value); }
    public CornerRadius Corner { get => GetValue(CornerProperty); private set => SetValue(CornerProperty, value); }
    public double NameSize { get => GetValue(NameSizeProperty); private set => SetValue(NameSizeProperty, value); }
    public double Glyph { get => GetValue(GlyphProperty); private set => SetValue(GlyphProperty, value); }
    public double RarityBadge { get => GetValue(RarityBadgeProperty); private set => SetValue(RarityBadgeProperty, value); }
    public double TagSize { get => GetValue(TagSizeProperty); private set => SetValue(TagSizeProperty, value); }
    public static readonly StyledProperty<double> IncomeFontProperty = AvaloniaProperty.Register<CardFace, double>(nameof(IncomeFont), 10);
    /// <summary>Taille du nombre écrit dans l'icône du Spamton.</summary>
    public double IncomeFont { get => GetValue(IncomeFontProperty); private set => SetValue(IncomeFontProperty, value); }
    public double TalentMaxWidth { get => GetValue(TalentMaxWidthProperty); private set => SetValue(TalentMaxWidthProperty, value); }
    public Thickness OverlayMargin { get => GetValue(OverlayMarginProperty); private set => SetValue(OverlayMarginProperty, value); }
    public Thickness BodyMargin { get => GetValue(BodyMarginProperty); private set => SetValue(BodyMarginProperty, value); }

    private readonly LinearGradientBrush _holo = new()
    {
        StartPoint = new RelativePoint(-1, -1, RelativeUnit.Relative), EndPoint = new RelativePoint(-0.3, -0.3, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
            new GradientStop(Color.FromArgb(70, 255, 130, 240), 0.32),
            new GradientStop(Color.FromArgb(110, 255, 255, 255), 0.5),
            new GradientStop(Color.FromArgb(70, 110, 240, 255), 0.68),
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
        },
    };
    /// <summary>Referme tous les exemplaires agrandis (ex. quand une fiche détaillée s'ouvre par-dessus : sinon ils restent visibles un instant).</summary>
    public static readonly StyledProperty<System.Windows.Input.ICommand?> CreditCommandProperty =
        AvaloniaProperty.Register<CardFace, System.Windows.Input.ICommand?>(nameof(CreditCommand));
    /// <summary>Si elle est donnée, un clic sur le nom de l'illustrateur l'exécute (fiche détaillée : ouvre l'artiste dans la Bibliothèque).</summary>
    public System.Windows.Input.ICommand? CreditCommand { get => GetValue(CreditCommandProperty); set => SetValue(CreditCommandProperty, value); }
    public static event Action? DismissZooms;
    public static void DismissAllZooms() => DismissZooms?.Invoke();
    private void DismissZoom() { HoloLayer.Opacity = 0; ResetZoom(); }
    /// <summary>Vrai tant qu'une fiche détaillée est ouverte : aucun agrandissement au survol (il resterait visible par-dessus la fiche).</summary>
    public static bool SuppressZoom { get; set; }
    private LinearGradientBrush? _shine;
    private Avalonia.Threading.DispatcherTimer? _shineTimer;
    private DateTime _shineLast;
    private double _phase;

    private static Color Mix(Color a, Color b, double k) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));

    /// <summary>Dégradé du cadre : la couleur de la rareté, avec une bande claire, une bande plus sombre et un second reflet (aspect métal poli).</summary>
    private void RebuildShine()
    {
        if (Item?.RarityBrush is not SolidColorBrush sb) { FrameShine = null; _shine = null; return; }
        var c = sb.Color;
        _shine = new LinearGradientBrush
        {
            SpreadMethod = GradientSpreadMethod.Repeat,
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(c, 0), new GradientStop(Mix(c, Colors.White, 0.6), 0.2), new GradientStop(c, 0.38),
                new GradientStop(Mix(c, Colors.Black, 0.35), 0.62), new GradientStop(Mix(c, Colors.White, 0.3), 0.82), new GradientStop(c, 1),
            },
        };
        FrameShine = _shine;
    }

    /// <summary>Fait glisser les reflets sur le cadre (survol, fiche détaillée) ; la couleur de la rareté ne change pas.</summary>
    public void StartShine()
    {
        if (Motion.Reduce || _shine is null) return;
        _shineTimer ??= new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _shineTimer.Tick -= ShineTick; _shineTimer.Tick += ShineTick;
        _shineLast = DateTime.UtcNow;
        _shineTimer.Start();
    }
    public void StopShine() => _shineTimer?.Stop();

    private void ShineTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        _phase = (_phase + (now - _shineLast).TotalSeconds * 0.7) % 1;
        _shineLast = now;
        if (_shine is null) return;
        _shine.StartPoint = new RelativePoint(_phase, _phase, RelativeUnit.Relative);
        _shine.EndPoint = new RelativePoint(_phase + 1, _phase + 1, RelativeUnit.Relative);
    }

    // ----- Survol : la carte elle-même grossit en place (plus de fenêtre flottante ni d'exemplaire en double) -----
    private ScaleTransform? _hoverScale;
    private Rotate3DTransform? _tilt;
    private Avalonia.Threading.DispatcherTimer? _anim;
    private double _scaleNow = 1;
    private bool _hovering;

    // Tailles en proportion de la carte : départ 100 %, rebond jusqu'à 105 %, arrivée 104 % ; à la sortie, creux à 99 % puis retour à 100 %.
    private const double HoverPeak = 1.05, HoverRest = 1.04, ExitDip = 0.99;

    private void EnsureTransform()
    {
        if (_hoverScale is not null) return;
        _hoverScale = new ScaleTransform(1, 1);
        _tilt = new Rotate3DTransform { Depth = 700 };
        var group = new TransformGroup();
        group.Children.Add(_hoverScale);
        group.Children.Add(_tilt);
        Root.Transitions = null;   // les animations de survol sont faites ici, pas par le thème
        Root.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        Root.RenderTransform = group;
    }

    /// <summary>Anime la taille de la carte par étapes (valeur visée, durée en ms, retour doux) ; <paramref name="relaxTilt"/> ramène aussi l'inclinaison à plat.</summary>
    private void Animate(bool relaxTilt, params (double To, double Ms, bool Soft)[] stages)
    {
        EnsureTransform();
        _anim?.Stop();
        var from = _scaleNow;
        var tiltX = _tilt!.AngleX; var tiltY = _tilt.AngleY;
        var start = DateTime.UtcNow;
        var timer = _anim = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(8) };
        var total = stages.Sum(x => x.Ms);
        timer.Tick += (_, _) =>
        {
            var ms = (DateTime.UtcNow - start).TotalMilliseconds;
            if (ms >= total)
            {
                timer.Stop();
                SetScale(stages[^1].To);
                if (relaxTilt) { _tilt.AngleX = 0; _tilt.AngleY = 0; }
                if (!_hovering && Math.Abs(_scaleNow - 1) < 0.0005) { Root.RenderTransform = null; _hoverScale = null; _tilt = null; }
                return;
            }
            double acc = 0, prev = from;
            foreach (var st in stages)
            {
                if (ms < acc + st.Ms)
                {
                    var t = (ms - acc) / st.Ms;
                    var e = st.Soft ? t * t * (3 - 2 * t) : 1 - Math.Pow(1 - t, 3);
                    SetScale(prev + (st.To - prev) * e);
                    break;
                }
                acc += st.Ms; prev = st.To;
            }
            if (relaxTilt && _tilt is not null) { var k = Math.Max(0, 1 - ms / stages[0].Ms); _tilt.AngleX = tiltX * k; _tilt.AngleY = tiltY * k; }
        };
        timer.Start();
    }

    private void SetScale(double k)
    {
        _scaleNow = k;
        if (_hoverScale is not null) { _hoverScale.ScaleX = k; _hoverScale.ScaleY = k; }
    }

    private void HoverEnter()
    {
        _hovering = true;
        if (!HoverZoom || Motion.Reduce || SuppressZoom) return;
        Animate(false, (HoverPeak, 200, false), (HoverRest, 140, true));
    }

    private void HoverExit()
    {
        _hovering = false;
        if (_hoverScale is null) return;
        if (Motion.Reduce) { ResetZoom(); return; }
        Animate(true, (ExitDip, 110, false), (1.0, 90, true));
    }

    /// <summary>Remet la carte à plat et à sa taille tout de suite (fiche ouverte, carte retirée de l'écran).</summary>
    private void ResetZoom()
    {
        _anim?.Stop();
        _hovering = false;
        _scaleNow = 1;
        _hoverScale = null; _tilt = null;
        Root.RenderTransform = null;
    }

    public CardFace()
    {
        InitializeComponent();
        ClipToBounds = false;   // la carte agrandie au survol déborde de sa case : rien ne doit la rogner
        Root.DataContext = this;
        CreditBox.PointerPressed += (_, e) => { if (CreditCommand is { } c && c.CanExecute(null)) { c.Execute(null); e.Handled = true; } };
        HoloLayer.Background = _holo;
        Root.PointerEntered += (_, _) =>
        {
            if (Holo && !Motion.Reduce) HoloLayer.Opacity = 1;
            StartShine();
            HoverEnter();
        };
        Root.PointerExited += (_, _) => { HoloLayer.Opacity = 0; HoverExit(); if (!Holo) StopShine(); };
        Root.PointerMoved += (_, e) =>
        {
            // La carte s'incline doucement selon la position de la souris sur elle (±9°).
            if (_hovering && HoverZoom && !Motion.Reduce && !SuppressZoom && Root.Bounds.Width > 1 && Root.Bounds.Height > 1)
            {
                EnsureTransform();
                var q = e.GetPosition(Root);
                _tilt!.AngleY = Math.Clamp(q.X / Root.Bounds.Width - 0.5, -0.5, 0.5) * 18;
                _tilt.AngleX = -Math.Clamp(q.Y / Root.Bounds.Height - 0.5, -0.5, 0.5) * 18;
                if (_anim is not { IsEnabled: true } && Math.Abs(_scaleNow - HoverRest) > 0.001) SetScale(HoverRest);
            }
            if (!Holo || Motion.Reduce || Root.Bounds.Width < 1 || Root.Bounds.Height < 1) return;
            var p = e.GetPosition(Root);
            double rx = p.X / Root.Bounds.Width, ry = p.Y / Root.Bounds.Height;
            _holo.StartPoint = new RelativePoint(rx - 0.4, ry - 0.4, RelativeUnit.Relative);
            _holo.EndPoint = new RelativePoint(rx + 0.4, ry + 0.4, RelativeUnit.Relative);
        };
        AttachedToVisualTree += (_, _) =>
        {
            DismissZooms += DismissZoom;
            if (Holo) StartShine();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            DismissZooms -= DismissZoom;
            StopShine();
            ResetZoom();
        };
        SizeChanged += (_, _) => Adapt();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemProperty) RebuildShine();
        if (change.Property == CreditCommandProperty) CreditBox.IsHitTestVisible = CreditCommand is not null;
        if (change.Property == ShowIncomeProperty || change.Property == TalentTagProperty || change.Property == TalentAtBottomProperty)
            HpVisible = ShowIncome && !(TalentAtBottom && !string.IsNullOrEmpty(TalentTag));
        if (change.Property == ShowNameProperty || change.Property == ShowRarityProperty) Adapt();
        else if (change.Property == TalentTagProperty || change.Property == TalentAtBottomProperty)
        {
            TopTag = TalentAtBottom ? null : TalentTag;
            BottomTag = TalentAtBottom ? TalentTag : null;
        }
    }

    /// <summary>Rebond au clic : fait par la vignette de la liste (la carte agrandie est la carte elle-même) ; toujours faux ici.</summary>
    public bool BounceZoom() => false;

    /// <summary>Tailles qui suivent la largeur de la carte : coins, nom, pastilles, médaillon de rareté.</summary>
    private void Adapt()
    {
        var w = Math.Max(Bounds.Width, 30);
        Corner = new CornerRadius(Math.Clamp(w / 12, 6, 18));
        NameSize = Math.Clamp(w / 11.5, 8, 17);
        Glyph = Math.Clamp(w / 6.2, 14, 40);
        RarityBadge = Math.Clamp(w / 4.2, 20, 60);   // le symbole chevauche le cadre : sa moitié est sur la bordure, devant
        TagSize = Math.Clamp(Glyph * 0.52, 9, 18);
        IncomeFont = Math.Max(7, Glyph * 0.42);
        TalentMaxWidth = Math.Max(40, w * 0.62);
        var inset = Math.Clamp(w / 28, 3, 9);
        OverlayMargin = new Thickness(inset + 1, ShowName ? NameSize * 1.9 + inset : inset + 1, inset + 1, inset);
        // Le symbole (2 fois plus large que haut) est centré sur le bord bas du cadre ; la carte s'arrête exactement au bas du symbole : aucune marge en dessous.
        BodyMargin = new Thickness(0);
        var t = Math.Clamp(w / 55, 1.5, 7);
        FrameThickness = new Thickness(t);
        ImageMargin = new Thickness(t * 0.6);
        // Symbole au même endroit qu'à l'origine : son centre est à 0,3 × sa largeur au-dessus du bord bas du cadre (son bas touche presque le bord).
        // Logo de rareté : petit, au milieu du bas de la carte, juste au-dessus du cadre.
        RarityLogo = Math.Clamp(w * 0.27, 18, 80);
        RarityLogoHeight = RarityLogo / 2;
        BodyReserve = new Thickness(0, 0, 0, ShowRarity ? RarityLogoHeight / 2 : 0);
        RarityLogoMargin = new Thickness(0);   // aligné en bas : son centre tombe sur le bord bas du corps de la carte
        RarityMargin = new Thickness(0, 0, 0, RarityBadge * 0.05);
        RarityHeight = RarityBadge / 2;
        var r = Math.Max(2, Corner.TopLeft - t * 0.6);
        ImageCorner = new CornerRadius(r);
        CreditSize = Math.Clamp(w / 22, 8, 14);
        var lift = (ShowRarity ? RarityLogo / 4 + 2 : 0) + inset;
        CreditMargin = new Thickness(inset, inset, inset, lift);
        BottomLeftMargin = new Thickness(inset + 1, 0, 0, lift);
        HpMargin = new Thickness(inset + 5, 0, 0, inset + 3);
        HpSize = Math.Max(7, NameSize * 0.68);   // assez petit pour tenir entre le bord gauche et le médaillon de rareté
    }
}
