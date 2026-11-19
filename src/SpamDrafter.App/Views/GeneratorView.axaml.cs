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

    /// <summary>Clic sur le nom d'une carte imposée : ouvre sa fiche détaillée.</summary>
    private void SourceCard_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SourceCard card) (DataContext as GeneratorViewModel)?.OpenCardCommand.Execute(card);
        e.Handled = true;
    }
}
