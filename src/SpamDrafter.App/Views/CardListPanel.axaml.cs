using Avalonia.Controls;
using Avalonia.Input;

namespace SpamDrafter.App.Views;

public partial class CardListPanel : UserControl
{
    public CardListPanel()
    {
        InitializeComponent();
        List.DoubleTapped += (_, e) => CardActivated?.Invoke(this, e);
        // Échap vide la recherche (phase 12, lot A).
        SearchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is ViewModels.CardBrowser b) { b.Search = ""; e.Handled = true; }
        };
    }

    private void ClearFilters_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        (DataContext as ViewModels.CardBrowser)?.ClearFilters();

    /// <summary>Double-clic sur une carte de la liste.</summary>
    public event EventHandler<TappedEventArgs>? CardActivated;
}
