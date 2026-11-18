using Avalonia.Controls;
using SpamDrafter.App.ViewModels;

namespace SpamDrafter.App.Views;

public partial class DrafterView : UserControl
{
    public DrafterView()
    {
        InitializeComponent();
        PanelLayout.Attach(RootGrid, "drafter", ResetLayout);
        Picker.CardActivated += (_, _) => (DataContext as DrafterViewModel)?.AddCommand.Execute(null);
        // Comme dans le Builder : un clic sur le deck déjà présélectionné le désélectionne ; un double-clic l'ouvre dans le Deck builder.
        ResultsList.DoubleTapped += (_, _) =>
        {
            if (DataContext is DrafterViewModel vm && ResultsList.SelectedItem is DrafterResult r) vm.OpenCommand.Execute(r);
        };
        ResultsList.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(ResultsList).Properties.IsLeftButtonPressed || e.ClickCount > 1) return;
            var source = e.Source as Avalonia.Visual;
            if (Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Avalonia.Controls.Button>(source) is not null) return;
            if (Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Avalonia.Controls.ListBoxItem>(source) is { IsSelected: true })
            {
                ResultsList.SelectedItem = null;
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
}
