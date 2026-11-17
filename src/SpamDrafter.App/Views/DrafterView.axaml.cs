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
    }
}
