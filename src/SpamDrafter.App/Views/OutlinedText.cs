using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SpamDrafter.App.Views;

/// <summary>
/// Texte d'une ligne avec un contour noir (nom des capacités sur les cartes) : le texte noir est dessiné autour du texte de couleur, décalé dans
/// 16 directions, puis le texte de couleur par-dessus. Centré dans sa largeur, coupé par « … » s'il est trop long.
/// </summary>
public sealed class OutlinedText : Control
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<OutlinedText, string?>(nameof(Text));
    public static readonly StyledProperty<double> FontSizeProperty = AvaloniaProperty.Register<OutlinedText, double>(nameof(FontSize), 12);
    public static readonly StyledProperty<FontWeight> FontWeightProperty = AvaloniaProperty.Register<OutlinedText, FontWeight>(nameof(FontWeight), FontWeight.Normal);
    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<OutlinedText, IBrush?>(nameof(Foreground), Brushes.White);

    public static readonly StyledProperty<FontFamily?> FontFamilyProperty = AvaloniaProperty.Register<OutlinedText, FontFamily?>(nameof(FontFamily));
    /// <summary>Police (null : celle du parent).</summary>
    public FontFamily? FontFamily { get => GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontWeight FontWeight { get => GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    // ----- Défilement au survol de la carte : un texte coupé par « … » défile de droite à gauche tant que la souris est sur la carte -----
    private CardFace? _card;
    private DispatcherTimer? _timer;
    private DateTime _since;
    private bool _scrolling;

    private const double Speed = 45;      // pixels par seconde
    private const double PauseStart = 0.7, PauseEnd = 0.9;   // secondes d'arrêt au début et à la fin

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _card = this.FindAncestorOfType<CardFace>();
        if (_card is not null) _card.PropertyChanged += OnCardChanged;
        UpdateScrolling();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_card is not null) _card.PropertyChanged -= OnCardChanged;
        _card = null;
        Stop();
    }

    private void OnCardChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsPointerOverProperty) UpdateScrolling();
    }

    private void UpdateScrolling()
    {
        if (_card is { IsPointerOver: true } && !Motion.Reduce) Start(); else Stop();
    }

    private void Start()
    {
        if (_timer is not null) return;
        _since = DateTime.UtcNow;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => InvalidateVisual();
        _timer.Start();
    }

    private void Stop()
    {
        if (_timer is null) return;
        _timer.Stop();
        _timer = null;
        InvalidateVisual();
    }

    /// <summary>Décalage du texte (en pixels, vers la gauche) à cet instant du cycle : arrêt, défilement, arrêt, retour au début.</summary>
    private double Offset(double travel)
    {
        var scroll = travel / Speed;
        var cycle = PauseStart + scroll + PauseEnd;
        var t = (DateTime.UtcNow - _since).TotalSeconds % cycle;
        if (t < PauseStart) return 0;
        if (t < PauseStart + scroll) return (t - PauseStart) * Speed;
        return travel;
    }

    static OutlinedText()
    {
        AffectsMeasure<OutlinedText>(TextProperty, FontSizeProperty, FontWeightProperty, FontFamilyProperty);
        AffectsRender<OutlinedText>(TextProperty, FontSizeProperty, FontWeightProperty, ForegroundProperty);
    }

    private FormattedText Format(IBrush brush, double maxWidth) => new(Text ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(FontFamily ?? Avalonia.Controls.Documents.TextElement.GetFontFamily(this), FontStyle.Normal, FontWeight), FontSize, brush)
    {
        MaxTextWidth = double.IsInfinity(maxWidth) || maxWidth < 1 ? double.MaxValue : maxWidth,
        MaxLineCount = 1,
        Trimming = TextTrimming.CharacterEllipsis,
        TextAlignment = double.IsInfinity(maxWidth) || maxWidth < 1 ? TextAlignment.Left : TextAlignment.Center,
    };

    // Texte à sa largeur naturelle (noir et couleur) : refait seulement si le texte, la taille ou la police changent, pas à chaque redimensionnement.
    private FormattedText? _natBlack, _natFore;
    private IBrush? _natBrush;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == FontSizeProperty || change.Property == FontWeightProperty
            || change.Property == FontFamilyProperty || change.Property == ForegroundProperty) { _natBlack = null; _natFore = null; }
    }

    private FormattedText NaturalBlack => _natBlack ??= Format(Brushes.Black, double.PositiveInfinity);
    private FormattedText NaturalFore
    {
        get
        {
            var brush = Foreground ?? Brushes.White;
            if (_natFore is null || !ReferenceEquals(_natBrush, brush)) { _natFore = Format(brush, double.PositiveInfinity); _natBrush = brush; }
            return _natFore;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var outline = Math.Max(1, FontSize * 0.09);
        var f = NaturalBlack;
        var width = double.IsInfinity(availableSize.Width) ? f.WidthIncludingTrailingWhitespace : Math.Min(f.WidthIncludingTrailingWhitespace, Math.Max(0, availableSize.Width - 2 * outline));
        return new Size(width + 2 * outline, f.Height + 2 * outline);
    }

    public override void Render(DrawingContext context)
    {
        if (string.IsNullOrEmpty(Text) || Bounds.Width < 1) return;
        var outline = Math.Max(1, FontSize * 0.09);
        var width = Bounds.Width - 2 * outline;
        var natural = NaturalBlack.WidthIncludingTrailingWhitespace;
        var travel = natural - width;
        FormattedText black, fore;
        double x;
        _scrolling = _timer is not null && travel > 0.5;
        if (_scrolling)
        {
            // Texte trop long et souris sur la carte : il défile (largeur naturelle, rogné à la zone), de droite à gauche.
            black = NaturalBlack; fore = NaturalFore;
            x = outline - Offset(travel);
        }
        else if (travel <= 0.5)
        {
            // Il tient : largeur naturelle, centré (aucune mise en forme à refaire).
            black = NaturalBlack; fore = NaturalFore;
            x = outline + (width - natural) / 2;
        }
        else
        {
            black = Format(Brushes.Black, width); fore = Format(Foreground ?? Brushes.White, width);   // coupé par « … »
            x = outline;
        }
        using var clip = context.PushClip(new Rect(Bounds.Size));
        const int steps = 12;
        for (var i = 0; i < steps; i++)
        {
            var angle = 2 * Math.PI * i / steps;
            context.DrawText(black, new Point(x + outline * Math.Cos(angle), outline + outline * Math.Sin(angle)));
        }
        context.DrawText(fore, new Point(x, outline));
    }
}
