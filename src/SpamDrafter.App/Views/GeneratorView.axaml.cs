using Avalonia.Controls;
using SpamDrafter.App.ViewModels;

namespace SpamDrafter.App.Views;

public partial class GeneratorView : UserControl
{
    public GeneratorView()
    {
        InitializeComponent();
        PanelLayout.Attach(RootGrid, "generator", ResetLayout);
        Picker.CardActivated += (_, _) => (DataContext as GeneratorViewModel)?.AddCommand.Execute(null);
    }
}
