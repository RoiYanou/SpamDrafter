using Avalonia.Controls;
using SpamDrafter.App.ViewModels;

namespace SpamDrafter.App.Views;

public partial class DrafterView : UserControl
{
    public DrafterView()
    {
        InitializeComponent();
        Picker.CardActivated += (_, _) => (DataContext as DrafterViewModel)?.AddCommand.Execute(null);
    }
}
