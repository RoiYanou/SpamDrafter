using Avalonia.Controls;
using SpamDrafter.App.ViewModels;

namespace SpamDrafter.App.Views;

public partial class DeckBuilderView : UserControl
{
    public DeckBuilderView()
    {
        InitializeComponent();
        Picker.CardActivated += (_, _) => (DataContext as DeckBuilderViewModel)?.AddCommand.Execute(null);
    }
}
