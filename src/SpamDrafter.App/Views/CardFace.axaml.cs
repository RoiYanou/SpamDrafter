using System.ComponentModel;
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
/// de tout (une fenêtre flottante, donc jamais rognée par la liste qui contient la carte) ; « Désactiver les animations » le coupe.
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
    public static readonly StyledProperty<IImage?> LargeImageProperty = AvaloniaProperty.Register<CardFace, IImage?>(nameof(LargeImage));
    /// <summary>Version nette de l'illustration, seulement pour une grande tuile (voir <see cref="RefreshLarge"/>).</summary>
    public IImage? LargeImage { get => GetValue(LargeImageProperty); private set => SetValue(LargeImageProperty, value); }
    public static readonly StyledProperty<bool> ShowThumbProperty = AvaloniaProperty.Register<CardFace, bool>(nameof(ShowThumb), true);
    /// <summary>La miniature ne sert que tant que la version nette n'est pas arrivée (dessous, elle serait redessinée pour rien à chaque image).</summary>
    public bool ShowThumb { get => GetValue(ShowThumbProperty); private set => SetValue(ShowThumbProperty, value); }
    private CardItem? _watched;

    /// <summary>La miniature de 256 px suffit jusqu'à ≈ 230 pixels réels ; au-delà (tuile large, écran à fort zoom), on charge la version nette.</summary>
    private void RefreshLarge()
    {
        var scale = (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1) * 1.3;   // 1,3 : taille d'interface par défaut
        LargeImage = Item is { } it && ImageOverride is null && Bounds.Width * scale > 230 ? it.LargeThumbnail : null;
    }

    public static readonly StyledProperty<bool> CreditBelowAbilitiesProperty = AvaloniaProperty.Register<CardFace, bool>(nameof(CreditBelowAbilities));
    /// <summary>Fiche détaillée : la signature reste sous l'emplacement des capacités, qu'elles soient affichées ou non (elle ne bouge pas).</summary>
    public bool CreditBelowAbilities { get => GetValue(CreditBelowAbilitiesProperty); set => SetValue(CreditBelowAbilitiesProperty, value); }
    public static readonly StyledProperty<bool> CompactAbilitiesProperty = AvaloniaProperty.Register<CardFace, bool>(nameof(CompactAbilities));
    /// <summary>Vrai : le bloc des capacités est rapproché des PV (cartes des sélecteurs et des decks du Builder, du Drafter et du Générateur : l'écart sous la 3ᵉ capacité y paraissait trop grand).</summary>
    public bool CompactAbilities { get => GetValue(CompactAbilitiesProperty); set => SetValue(CompactAbilitiesProperty, value); }
    public static readonly StyledProperty<bool> ShowAbilitiesProperty = AvaloniaProperty.Register<CardFace, bool>(nameof(ShowAbilities));
    public static readonly StyledProperty<bool> AbilitiesVisibleProperty = AvaloniaProperty.Register<CardFace, bool>(nameof(AbilitiesVisible));
    public static readonly StyledProperty<double> AbilitySizeProperty = AvaloniaProperty.Register<CardFace, double>(nameof(AbilitySize), 9);
    public static readonly StyledProperty<Thickness> AbilitiesMarginProperty = AvaloniaProperty.Register<CardFace, Thickness>(nameof(AbilitiesMargin));
    /// <summary>Vrai : les capacités de la carte (nom et recharge) sont écrites sur l'illustration, comme dans le jeu ; masquées si la carte est trop petite.</summary>
    public bool ShowAbilities { get => GetValue(ShowAbilitiesProperty); set => SetValue(ShowAbilitiesProperty, value); }
    public bool AbilitiesVisible { get => GetValue(AbilitiesVisibleProperty); private set => SetValue(AbilitiesVisibleProperty, value); }
    public static readonly StyledProperty<double> AbilityRowProperty = AvaloniaProperty.Register<CardFace, double>(nameof(AbilityRow), 20);
    public static readonly StyledProperty<double> AbilitiesHeightProperty = AvaloniaProperty.Register<CardFace, double>(nameof(AbilitiesHeight), 70);
    /// <summary>Hauteur d'un bandeau de capacité, et de la zone qui en contient trois (les bandeaux sont alignés par le haut dans cette zone).</summary>
    /// <summary>Taille de l'illustration des éléments et du logo de recharge dans un bandeau.</summary>
    public static readonly StyledProperty<double> CooldownFontProperty = AvaloniaProperty.Register<CardFace, double>(nameof(CooldownFont), 8);
    /// <summary>Taille du nombre de tours dans le logo de recharge d'un bandeau (petit : il doit tenir dans le logo, même à deux chiffres).</summary>
    public double CooldownFont { get => GetValue(CooldownFontProperty); private set => SetValue(CooldownFontProperty, value); }
    public static readonly StyledProperty<CornerRadius> ToxCornerProperty = AvaloniaProperty.Register<CardFace, CornerRadius>(nameof(ToxCorner), new CornerRadius(2));
    public CornerRadius ToxCorner { get => GetValue(ToxCornerProperty); private set => SetValue(ToxCornerProperty, value); }
    public static readonly StyledProperty<double> ToxNameWidthProperty = AvaloniaProperty.Register<CardFace, double>(nameof(ToxNameWidth), 80);
    /// <summary>Largeur disponible pour le nom d'un Tox de base (le logo est collé à gauche du nom).</summary>
    public double ToxNameWidth { get => GetValue(ToxNameWidthProperty); private set => SetValue(ToxNameWidthProperty, value); }
    public static readonly StyledProperty<double> ToxSizeProperty = AvaloniaProperty.Register<CardFace, double>(nameof(ToxSize), 14);
    public static readonly StyledProperty<Thickness> ToxMarginProperty = AvaloniaProperty.Register<CardFace, Thickness>(nameof(ToxMargin));
    public static readonly StyledProperty<Thickness> AbilityTextMarginProperty = AvaloniaProperty.Register<CardFace, Thickness>(nameof(AbilityTextMargin));
    /// <summary>Taille d'une icône de Tox de base (bas droite), marge de cette rangée, et marges du nom d'une capacité (place des deux colonnes d'icônes).</summary>
    public double ToxSize { get => GetValue(ToxSizeProperty); private set => SetValue(ToxSizeProperty, value); }
    public Thickness ToxMargin { get => GetValue(ToxMarginProperty); private set => SetValue(ToxMarginProperty, value); }
    public Thickness AbilityTextMargin { get => GetValue(AbilityTextMarginProperty); private set => SetValue(AbilityTextMarginProperty, value); }
    public static readonly StyledProperty<double> AbilityIconProperty = AvaloniaProperty.Register<CardFace, double>(nameof(AbilityIcon), 16);
    public static readonly StyledProperty<double> AbilityLogoProperty = AvaloniaProperty.Register<CardFace, double>(nameof(AbilityLogo), 18);
    public double AbilityIcon { get => GetValue(AbilityIconProperty); private set => SetValue(AbilityIconProperty, value); }
    public double AbilityLogo { get => GetValue(AbilityLogoProperty); private set => SetValue(AbilityLogoProperty, value); }
    public double AbilityRow { get => GetValue(AbilityRowProperty); private set => SetValue(AbilityRowProperty, value); }
    public double AbilitiesHeight { get => GetValue(AbilitiesHeightProperty); private set => SetValue(AbilitiesHeightProperty, value); }
    public double AbilitySize { get => GetValue(AbilitySizeProperty); private set => SetValue(AbilitySizeProperty, value); }
    public Thickness AbilitiesMargin { get => GetValue(AbilitiesMarginProperty); private set => SetValue(AbilitiesMarginProperty, value); }

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
    public static readonly StyledProperty<Thickness> NamePaddingProperty = AvaloniaProperty.Register<CardFace, Thickness>(nameof(NamePadding), new Thickness(4, 4, 4, 12));
    public Thickness NamePadding { get => GetValue(NamePaddingProperty); private set => SetValue(NamePaddingProperty, value); }
    public static readonly StyledProperty<Thickness> HpMarginProperty = AvaloniaProperty.Register<CardFace, Thickness>(nameof(HpMargin));
    public Thickness HpMargin { get => GetValue(HpMarginProperty); private set => SetValue(HpMarginProperty, value); }
    public static readonly StyledProperty<Thickness> StarMarginProperty = AvaloniaProperty.Register<CardFace, Thickness>(nameof(StarMargin));
    public Thickness StarMargin { get => GetValue(StarMarginProperty); private set => SetValue(StarMarginProperty, value); }
    public static readonly StyledProperty<double> StarSizeProperty = AvaloniaProperty.Register<CardFace, double>(nameof(StarSize), 8);
    public double StarSize { get => GetValue(StarSizeProperty); private set => SetValue(StarSizeProperty, value); }
    public static readonly StyledProperty<double> ToxRarityWidthProperty = AvaloniaProperty.Register<CardFace, double>(nameof(ToxRarityWidth), 30);
    public double ToxRarityWidth { get => GetValue(ToxRarityWidthProperty); private set => SetValue(ToxRarityWidthProperty, value); }
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
        var stops = new GradientStops();
        switch (CardFrameStyle.Mode)
        {
            case CardFrameMode.Black:
                var k = Color.FromRgb(22, 20, 28);
                stops.Add(new GradientStop(k, 0)); stops.Add(new GradientStop(Mix(k, Colors.White, 0.35), 0.2)); stops.Add(new GradientStop(k, 0.38));
                stops.Add(new GradientStop(Colors.Black, 0.62)); stops.Add(new GradientStop(Mix(k, Colors.White, 0.2), 0.82)); stops.Add(new GradientStop(k, 1));
                break;
            case CardFrameMode.Multicolor:
                foreach (var (hex, at) in new[] { ("#FF5A5A", 0.0), ("#FFC93C", 0.17), ("#7BE08A", 0.34), ("#5BC0F0", 0.5), ("#9A7BFF", 0.67), ("#FF70D0", 0.84), ("#FF5A5A", 1.0) })
                    stops.Add(new GradientStop(Color.Parse(hex), at));
                break;
            case CardFrameMode.Theme:
                var t = ThemeManager.Accent;   // couleur secondaire du thème en cours
                stops.Add(new GradientStop(t, 0)); stops.Add(new GradientStop(Mix(t, Colors.White, 0.6), 0.2)); stops.Add(new GradientStop(t, 0.38));
                stops.Add(new GradientStop(Mix(t, Colors.Black, 0.35), 0.62)); stops.Add(new GradientStop(Mix(t, Colors.White, 0.3), 0.82)); stops.Add(new GradientStop(t, 1));
                break;
            default:
                stops.Add(new GradientStop(c, 0)); stops.Add(new GradientStop(Mix(c, Colors.White, 0.6), 0.2)); stops.Add(new GradientStop(c, 0.38));
                stops.Add(new GradientStop(Mix(c, Colors.Black, 0.35), 0.62)); stops.Add(new GradientStop(Mix(c, Colors.White, 0.3), 0.82)); stops.Add(new GradientStop(c, 1));
                break;
        }
        _shine = new LinearGradientBrush
        {
            SpreadMethod = GradientSpreadMethod.Repeat,
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = stops,
        };
        FrameShine = _shine;
    }

    private void OnFrameStyleChanged() => RebuildShine();

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
            CardFrameStyle.Changed += OnFrameStyleChanged;
            ThemeManager.Changed += OnFrameStyleChanged;
            RebuildShine();
            if (Holo) StartShine();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            DismissZooms -= DismissZoom;
            CardFrameStyle.Changed -= OnFrameStyleChanged;
            ThemeManager.Changed -= OnFrameStyleChanged;
            StopShine();
            ResetZoom();
        };
        SizeChanged += (_, _) => Adapt();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemProperty)
        {
            RebuildShine();
            if (_watched is not null) _watched.PropertyChanged -= OnItemChanged;
            _watched = Item;
            if (_watched is not null) _watched.PropertyChanged += OnItemChanged;
            RefreshLarge();
        }
        if (change.Property == CreditCommandProperty) CreditBox.IsHitTestVisible = CreditCommand is not null;
        if (change.Property == ShowIncomeProperty || change.Property == TalentTagProperty || change.Property == TalentAtBottomProperty)
            HpVisible = ShowIncome && !(TalentAtBottom && !string.IsNullOrEmpty(TalentTag));
        if (change.Property == LargeImageProperty || change.Property == ImageOverrideProperty) ShowThumb = ImageOverride is null && LargeImage is null;
        if (change.Property == ShowNameProperty || change.Property == ShowRarityProperty || change.Property == ShowAbilitiesProperty || change.Property == CompactAbilitiesProperty || change.Property == CreditBelowAbilitiesProperty) Adapt();
        else if (change.Property == TalentTagProperty || change.Property == TalentAtBottomProperty)
        {
            TopTag = TalentAtBottom ? null : TalentTag;
            BottomTag = TalentAtBottom ? TalentTag : null;
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CardItem.LargeThumbnail) or nameof(CardItem.SelectedSkinId)) RefreshLarge();
    }

    /// <summary>Rebond au clic : fait par la vignette de la liste (la carte agrandie est la carte elle-même) ; toujours faux ici.</summary>
    public bool BounceZoom() => false;

    /// <summary>Tailles qui suivent la largeur de la carte : coins, nom, pastilles, médaillon de rareté.</summary>
    private double _adaptWidth = -1;
    private int _adaptKey = -1;

    private void Adapt()
    {
        var w = Math.Max(Bounds.Width, 30);
        RefreshLarge();
        // Redimensionner une boîte relance Adapt pour chaque carte : on ne refait le calcul que si la largeur ou les options ont changé.
        var key = (ShowName ? 1 : 0) | (ShowRarity ? 2 : 0) | (ShowAbilities ? 4 : 0) | (ShowElement ? 8 : 0) | (ShowIncome ? 16 : 0) | (CompactAbilities ? 32 : 0) | (CreditBelowAbilities ? 64 : 0);
        if (Math.Abs(w - _adaptWidth) < 0.3 && key == _adaptKey) return;
        _adaptWidth = w; _adaptKey = key;
        Corner = new CornerRadius(Math.Clamp(w / 24, 3, 9));
        NameSize = Math.Clamp(w / 11.5, 8, 17);
        Glyph = Math.Clamp(w / 6.2, 14, 40);
        RarityBadge = Math.Clamp(w / 4.2, 20, 60);   // le symbole chevauche le cadre : sa moitié est sur la bordure, devant
        TagSize = Math.Clamp(Glyph * 0.52, 9, 18);
        IncomeFont = Math.Max(7, Glyph * 0.42);
        TalentMaxWidth = Math.Max(40, w * 0.62);
        var inset = Math.Clamp(w / 28, 3, 9);
        // Élément (haut gauche) et Spamton (haut droite) : dans leur coin, avec les mêmes marges que les PV et les étoiles (en bas) ;
        // le nom, centré, laisse la place des deux icônes de chaque côté.
        OverlayMargin = new Thickness(inset + 5, inset + 3, inset + 5, inset + 3);
        var nameSide = ShowElement || ShowIncome ? Glyph + inset + 5 : 4;
        NamePadding = new Thickness(nameSide, 4, nameSide, 12);
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
        StarMargin = new Thickness(0, 0, inset + 5, inset + 3);
        StarSize = Math.Max(7, NameSize * 0.78);
        // Capacités : au bas de l'illustration, au-dessus de la ligne des PV ; trop petites pour être lues sous 120 px de large.
        HpSize = Math.Max(7, NameSize * 0.68);   // calculé AVANT les marges des capacités qui en dépendent (sinon la valeur du calcul précédent servait : capacités trop hautes)
        AbilitiesVisible = ShowAbilities && w >= 70;
        AbilitySize = Math.Clamp(w / 17, 7, 14);
        AbilityRow = AbilitySize * 2.2;
        AbilityIcon = AbilityRow * 0.74;      // disque d'éléments, logos de statistiques et icônes de Tox : une seule taille
        AbilityLogo = AbilityRow * 0.7;       // logo de recharge : jamais plus grand que les logos d'élément
        AbilityTextMargin = new Thickness(AbilityIcon + 3, 0, AbilityIcon + 3, 0);   // même marge des deux côtés : le nom reste centré et aligné
        AbilitiesHeight = AbilityRow * 3 + 10;   // 3 bandeaux + leurs 2 espaces + une marge : aucun bas de bandeau coupé   // trois bandeaux au plus : une carte à une capacité l'affiche à la même hauteur que le premier d'une carte à trois
        CooldownFont = Math.Max(5.5, AbilitySize * 0.68);
        var side = inset + 2 + w * 0.045;   // bandeaux un peu moins larges que la carte
        AbilitiesMargin = new Thickness(side, 0, side, inset + 3 + HpSize * 2.4 + AbilityRow * 0.55 - 6 - AbilityRow * 0.2 - (CompactAbilities ? AbilityRow * 0.5 : 0));   // 0,2 ligne plus bas qu'avant (retouche phase 15)
        // Signature de l'artiste : sous les capacités (jamais par-dessus), le plus bas possible dans l'illustration.
        if (CreditBelowAbilities)
            CreditMargin = new Thickness(inset, inset, inset, Math.Max(1, Math.Min(lift, AbilitiesMargin.Bottom - CreditSize * 1.7 - 3)));
        ToxSize = Math.Max(11, AbilityIcon * 1.1);
        ToxCorner = new CornerRadius(Math.Max(1, AbilityIcon * 0.12));   // coins à peine arrondis : le cadre du logo ne devient jamais un cercle
        var toxRoom = Math.Max(30, w - 2 * (inset + 2 + w * 0.045) - AbilityIcon - 14);
        ToxNameWidth = toxRoom * 0.55;     // le nom du Tox et le nom de sa rareté se partagent la place
        ToxRarityWidth = toxRoom * 0.45;
        ToxMargin = new Thickness(0, 0, inset + 4, inset + 3);
    }
}
