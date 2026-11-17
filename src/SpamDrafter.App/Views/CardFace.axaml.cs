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
    /// <summary>Épaisseur du cadre : la même pour toutes les raretés, proportionnelle à la taille d'affichage de la carte.</summary>
    public Thickness FrameThickness { get => GetValue(FrameThicknessProperty); private set => SetValue(FrameThicknessProperty, value); }
    /// <summary>L'illustration est rentrée sous le cadre (moitié de son épaisseur) et rognée un peu plus serré : ses coins ne dépassent jamais.</summary>
    /// <summary>Le symbole est remonté pour que son dessin (placé bas dans son image) chevauche le milieu du bord du cadre.</summary>
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
    private Popup? _zoom;
    private ScrollViewer? _scroll;

    private void OnScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (_zoom?.IsOpen != true) return;
        HideZoom();
        Avalonia.Threading.DispatcherTimer.RunOnce(() => { if (Root.IsPointerOver && HoverZoom && !Motion.Reduce) ShowZoom(); }, TimeSpan.FromMilliseconds(40));
    }

    public CardFace()
    {
        InitializeComponent();
        Root.DataContext = this;
        HoloLayer.Background = _holo;
        Root.PointerEntered += (_, _) =>
        {
            if (Holo && !Motion.Reduce) HoloLayer.Opacity = 1;
            if (HoverZoom && !Motion.Reduce) ShowZoom();
        };
        Root.PointerExited += (_, _) => { HoloLayer.Opacity = 0; HideZoom(); };
        Root.PointerMoved += (_, e) =>
        {
            if (HoverZoom && !Motion.Reduce && _zoom?.IsOpen != true) ShowZoom();   // reprend après un défilement de la liste
            if (!Holo || Motion.Reduce || Root.Bounds.Width < 1 || Root.Bounds.Height < 1) return;
            var p = e.GetPosition(Root);
            double rx = p.X / Root.Bounds.Width, ry = p.Y / Root.Bounds.Height;
            _holo.StartPoint = new RelativePoint(rx - 0.4, ry - 0.4, RelativeUnit.Relative);
            _holo.EndPoint = new RelativePoint(rx + 0.4, ry + 0.4, RelativeUnit.Relative);
        };
        // Quand la liste défile sous la souris, l'exemplaire agrandi est replacé sur la carte qui se trouve alors sous le pointeur.
        AttachedToVisualTree += (_, _) =>
        {
            _scroll = this.FindAncestorOfType<ScrollViewer>();
            if (_scroll is not null) _scroll.ScrollChanged += OnScrolled;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            HideZoom();
            if (_scroll is not null) _scroll.ScrollChanged -= OnScrolled;
            _scroll = null;
        };
        SizeChanged += (_, _) => Adapt();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowNameProperty || change.Property == ShowRarityProperty) Adapt();
        else if (change.Property == TalentTagProperty || change.Property == TalentAtBottomProperty)
        {
            TopTag = TalentAtBottom ? null : TalentTag;
            BottomTag = TalentAtBottom ? TalentTag : null;
        }
    }

    /// <summary>Un exemplaire agrandi (×1,4) flotte au centre de la carte, devant tout le reste.</summary>
    private void ShowZoom()
    {
        if (Item is null || Root.Child is not Panel host || TopLevel.GetTopLevel(this) is not { } top) return;
        // Taille réellement affichée à l'écran (l'interface peut être agrandie à 130 % par une transformation de mise en page).
        var m = Root.TransformToVisual(top);
        if (m is null) return;
        var a = m.Value.Transform(new Point(0, 0)); var b = m.Value.Transform(new Point(Root.Bounds.Width, Root.Bounds.Height));
        double w = Math.Abs(b.X - a.X) * 1.15, h = Math.Abs(b.Y - a.Y) * 1.15;
        if (w < 20) return;
        var face = new CardFace
        {
            Item = Item, ImageOverride = ImageOverride, ShowName = true, ShowElement = true, ShowRarity = ShowRarity, ShowIncome = true,
            TalentTag = TalentTag, TalentName = TalentName, Credit = Credit, HoverZoom = false, Holo = false, Width = w, Height = h, IsHitTestVisible = false,
        };
        if (_zoom is null)
        {
            _zoom = new Popup { PlacementTarget = Root, Placement = PlacementMode.Center, IsLightDismissEnabled = false, IsHitTestVisible = false };
            host.Children.Add(_zoom);
        }
        _zoom.Child = face;
        _zoom.IsOpen = true;
        StartWatch();

    }

    private void HideZoom()
    {
        if (_zoom is { } z) z.IsOpen = false;
        _watch?.Stop();
    }

    /// <summary>Tant que l'exemplaire agrandi est ouvert, on vérifie que la souris est toujours sur la carte : sinon il se referme (sécurité).</summary>
    private Avalonia.Threading.DispatcherTimer? _watch;
    private void StartWatch()
    {
        _watch ??= new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _watch.Tick -= WatchTick; _watch.Tick += WatchTick;
        _watch.Start();
    }
    private void WatchTick(object? sender, EventArgs e)
    {
        if (!Root.IsPointerOver) HideZoom();
    }

    /// <summary>Tailles qui suivent la largeur de la carte : coins, nom, pastilles, médaillon de rareté.</summary>
    private void Adapt()
    {
        var w = Math.Max(Bounds.Width, 30);
        Corner = new CornerRadius(Math.Clamp(w / 12, 6, 18));
        NameSize = Math.Clamp(w / 11.5, 8, 17);
        Glyph = Math.Clamp(w / 6.2, 14, 40);
        RarityBadge = Math.Clamp(w / 4.2, 20, 60);   // le symbole chevauche le cadre : sa moitié est sur la bordure, devant
        TagSize = Math.Clamp(Glyph * 0.52, 9, 18);
        TalentMaxWidth = Math.Max(40, w * 0.62);
        var inset = Math.Clamp(w / 28, 3, 9);
        OverlayMargin = new Thickness(inset + 1, ShowName ? NameSize * 1.9 + inset : inset + 1, inset + 1, inset);
        // Le corps de la carte s'arrête à mi-hauteur du médaillon : celui-ci chevauche le bas du cadre sans sortir de la carte.
        BodyMargin = new Thickness(0, 0, 0, ShowRarity ? RarityBadge / 2 : 0);
        var t = Math.Clamp(w / 55, 1.5, 7);
        FrameThickness = new Thickness(t);
        ImageMargin = new Thickness(t * 0.6);
        RarityMargin = new Thickness(0, 0, 0, RarityBadge * 0.3);
        var r = Math.Max(2, Corner.TopLeft - t * 0.6);
        ImageCorner = new CornerRadius(r);
        CreditSize = Math.Clamp(w / 22, 8, 14);
        var lift = (ShowRarity ? RarityBadge / 2 : 0) + inset;
        CreditMargin = new Thickness(inset, inset, inset, lift);
        BottomLeftMargin = new Thickness(inset + 1, 0, 0, lift);
    }
}
