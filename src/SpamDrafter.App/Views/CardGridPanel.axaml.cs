using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace SpamDrafter.App.Views;

/// <summary>Liste de cartes en quadrillage (phase 13). Même rôle que <see cref="CardListPanel"/>, en vignettes ; <see cref="TileWidth"/> règle leur taille.</summary>
public partial class CardGridPanel : UserControl
{
    public static readonly StyledProperty<double> TileWidthProperty = AvaloniaProperty.Register<CardGridPanel, double>(nameof(TileWidth), 130);
    public static readonly StyledProperty<double> TileHeightProperty = AvaloniaProperty.Register<CardGridPanel, double>(nameof(TileHeight), 185);
    public double TileWidth { get => GetValue(TileWidthProperty); set => SetValue(TileWidthProperty, value); }
    public double TileHeight { get => GetValue(TileHeightProperty); set => SetValue(TileHeightProperty, value); }

    public CardGridPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BuildLoreMenu();
        List.DoubleTapped += (_, e) => CardActivated?.Invoke(this, e);
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

    /// <summary>Double-clic sur une carte.</summary>
    public event EventHandler<TappedEventArgs>? CardActivated;
}
