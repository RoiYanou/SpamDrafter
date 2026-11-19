using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace SpamDrafter.App.Views;

/// <summary>Liste de cartes en quadrillage (phase 13). Même rôle que <see cref="CardListPanel"/>, en vignettes ; <see cref="TileWidth"/> règle leur taille.</summary>
public partial class CardGridPanel : UserControl
{
    public static readonly StyledProperty<double> TileWidthProperty = AvaloniaProperty.Register<CardGridPanel, double>(nameof(TileWidth), 130);
    public static readonly StyledProperty<double> TileHeightProperty = AvaloniaProperty.Register<CardGridPanel, double>(nameof(TileHeight), 185);
    public static readonly StyledProperty<double> FitWidthProperty = AvaloniaProperty.Register<CardGridPanel, double>(nameof(FitWidth), 130);
    public static readonly StyledProperty<double> FitHeightProperty = AvaloniaProperty.Register<CardGridPanel, double>(nameof(FitHeight), 185);
    public static readonly StyledProperty<bool> ClickToAddProperty = AvaloniaProperty.Register<CardGridPanel, bool>(nameof(ClickToAdd));
    /// <summary>Nombre de colonnes imposé (0 : autant que la largeur en permet). Les vignettes s'agrandissent pour remplir la ligne.</summary>
    public static readonly StyledProperty<int> ColumnsProperty = AvaloniaProperty.Register<CardGridPanel, int>(nameof(Columns));
    public int Columns { get => GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }
    public double TileWidth { get => GetValue(TileWidthProperty); set => SetValue(TileWidthProperty, value); }
    public double TileHeight { get => GetValue(TileHeightProperty); set => SetValue(TileHeightProperty, value); }
    /// <summary>Taille réellement affichée : les vignettes sont ajustées pour remplir chaque ligne, donc le même espace partout (côtés, haut, bas, entre les cartes).</summary>
    public double FitWidth { get => GetValue(FitWidthProperty); private set => SetValue(FitWidthProperty, value); }
    public double FitHeight { get => GetValue(FitHeightProperty); private set => SetValue(FitHeightProperty, value); }
    /// <summary>Vrai (sélections de cartes des onglets de deck) : un clic gauche ajoute directement la carte, sans la présélectionner.</summary>
    public static readonly StyledProperty<bool> ClickOpensDetailProperty = AvaloniaProperty.Register<CardGridPanel, bool>(nameof(ClickOpensDetail));
    /// <summary>Vrai (Bibliothèque) : un clic gauche ouvre la fiche rapide, comme un clic droit (elle suit le joueur d'un onglet à l'autre).</summary>
    public bool ClickOpensDetail { get => GetValue(ClickOpensDetailProperty); set => SetValue(ClickOpensDetailProperty, value); }
    public bool ClickToAdd { get => GetValue(ClickToAddProperty); set => SetValue(ClickToAddProperty, value); }
    /// <summary>Modèle de carte : celui des sélecteurs (capacités rapprochées des PV) quand un clic ajoute la carte, sinon celui de la Bibliothèque.</summary>
    public Avalonia.Controls.Templates.IDataTemplate? ThumbTemplate =>
        Application.Current?.TryFindResource(ClickToAdd ? "CardThumbPicker" : "CardThumb", out var t) == true ? t as Avalonia.Controls.Templates.IDataTemplate : null;

    // Espace autour d'une vignette : marge 9 de chaque côté, sans remplissage (voir Theme.axaml, « ListBox.grid »).
    private const double Around = 18;

    private void Fit()
    {
        var sv = List.Scroll as ScrollViewer;
        var avail = (sv is { Viewport.Width: > 0 } ? sv.Viewport.Width : List.Bounds.Width) - List.Padding.Left - List.Padding.Right;
        if ((Columns <= 0 && avail < TileWidth) || TileWidth <= 0 || avail <= 0) return;
        avail -= 4;   // marge de sécurité : sans elle, une dernière colonne « juste » ne tient pas et passe à la ligne
        var columns = Columns > 0 ? Columns : Math.Max(1, (int)Math.Floor(avail / (TileWidth + Around)));
        var width = Math.Floor((avail / columns - Around) * 2) / 2;
        if (Columns <= 0) width = Math.Min(width, TileWidth * 1.35);   // une seule colonne dans un panneau large : la carte ne devient pas géante
        if (Math.Abs(width - FitWidth) < 0.5) return;
        FitWidth = width; FitHeight = width * TileHeight / TileWidth;
    }

    public CardGridPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BuildLoreMenu();
        FilterPopup.PlacementTarget = SearchRow;
        FilterPopup.OverlayInputPassThroughElement = SearchRow;
        FilterPopup.Opened += (_, _) => { OverlayMenu.StyleFrame(this, FilterFrame); OverlayMenu.Fit(SearchRow, FilterScale, FilterFrame); };
        // Un clic en dehors du menu le ferme : on le range aussi dans le réglage (le bouton « Filtres » repasse en position fermée).
        FilterPopup.Closed += (_, _) => { if (DataContext is ViewModels.CardBrowser fb && fb.FiltersVisible) fb.FiltersCollapsed = true; };
        List.DoubleTapped += (_, _) => { if (!ClickToAdd) CardActivated?.Invoke(this, EventArgs.Empty); };
        List.AddHandler(PointerPressedEvent, OnTilePressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        List.SizeChanged += (_, _) => Fit();
        List.TemplateApplied += (_, _) => { if (List.Scroll is ScrollViewer sv) { sv.ScrollChanged += (_, _) => { ElementSheen.NoteScroll(); Fit(); }; } Fit(); };
        PropertyChanged += (_, e) => { if (e.Property == TileWidthProperty || e.Property == TileHeightProperty || e.Property == ColumnsProperty) Fit(); };
        SearchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is ViewModels.CardBrowser b) { b.Search = ""; e.Handled = true; }
        };
    }

    /// <summary>Menu « Filtrer par » : Régions, Espèces, Périodes, chacun avec ses valeurs en sous-menu, et « Aucun filtre ».</summary>
    private void BuildLoreMenu()
    {
        if (DataContext is not ViewModels.CardBrowser b) return;
        var menu = new Avalonia.Controls.MenuFlyout { Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedLeft };
        var none = new Avalonia.Controls.MenuItem { Header = "Aucun filtre" };
        none.Click += (_, _) => b.LoreKind = b.LoreKindOptions[0];
        menu.Items.Add(none);
        void Family(ViewModels.FilterOption kind, IReadOnlyList<ViewModels.FilterOption> values)
        {
            var parent = new Avalonia.Controls.MenuItem { Header = kind.Label };
            foreach (var v in values.Skip(1))
            {
                var item = new Avalonia.Controls.MenuItem { Header = v.Label };
                item.Click += (_, _) => { b.LoreKind = kind; b.LoreValue = v; };
                parent.Items.Add(item);
            }
            menu.Items.Add(parent);
        }
        Family(b.LoreKindOptions[1], b.RegionOptions);
        Family(b.LoreKindOptions[2], b.SpeciesOptions);
        Family(b.LoreKindOptions[3], b.ChronologyOptions);
        LoreButton.Flyout = menu;
    }

    private void ToggleFilters_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.CardBrowser b) b.FiltersCollapsed = !b.FiltersCollapsed;
    }

    private void ClearFilters_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        (DataContext as ViewModels.CardBrowser)?.ClearFilters();

    /// <summary>Clic gauche sur une vignette : ajout direct (rebond de la vignette), la carte n'est pas gardée comme sélection.</summary>
    private void OnTilePressed(object? sender, PointerPressedEventArgs e)
    {
        if (ClickOpensDetail && e.GetCurrentPoint(List).Properties.IsLeftButtonPressed
            && (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>() is { DataContext: ViewModels.CardItem opened }
            && TopLevel.GetTopLevel(this)?.DataContext is ViewModels.MainViewModel main)
        {
            main.OpenCardDetail(opened);
            e.Handled = true;
            return;
        }
        if (!ClickToAdd || DataContext is not ViewModels.CardBrowser b || !e.GetCurrentPoint(List).Properties.IsLeftButtonPressed) return;
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>() is not { DataContext: ViewModels.CardItem card } tile) return;
        b.Selected = card;
        CardActivated?.Invoke(this, EventArgs.Empty);
        b.Selected = null;
        // Le rebond est sur la carte agrandie au survol (celle que le joueur voit) ; à défaut, sur la vignette.
        if (tile.FindDescendantOfType<CardFace>() is not { } face || !face.BounceZoom()) Bounce.Run(tile);
        ViewModels.Sounds.Play(ViewModels.SoundKind.Pop);
        e.Handled = true;
    }

    /// <summary>Ajout d'une carte demandé (clic gauche, ou double-clic ailleurs).</summary>
    public event EventHandler? CardActivated;
}
