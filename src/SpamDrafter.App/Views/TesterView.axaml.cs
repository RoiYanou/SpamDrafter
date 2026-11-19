using Avalonia.Controls;
using SpamDrafter.App.ViewModels;

namespace SpamDrafter.App.Views;

public partial class TesterView : UserControl
{
    public TesterView()
    {
        InitializeComponent();
        PanelLayout.Attach(RootGrid, "tester", ResetLayout);
        // Mode « Comparer » : un clic remplit A ou B ; la liste n'en garde pas le surlignage.
        DeckList.SelectionChanged += (_, _) =>
        {
            if (DataContext is TesterViewModel { IsCompare: true } && DeckList.SelectedItem is not null)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => DeckList.SelectedItem = null);
        };
        // Comme dans le Drafter : un clic sur le deck déjà choisi le désélectionne.
        DeckList.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(DeckList).Properties.IsLeftButtonPressed || e.ClickCount > 1) return;
            var source = e.Source as Avalonia.Visual;
            if (Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Avalonia.Controls.ListBoxItem>(source) is { IsSelected: true })
            {
                DeckList.SelectedItem = null;
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
}
