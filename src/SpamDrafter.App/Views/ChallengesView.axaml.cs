using Avalonia.Controls;
using Avalonia;
using SpamDrafter.App.ViewModels;

namespace SpamDrafter.App.Views;

public partial class ChallengesView : UserControl
{
    public ChallengesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not ChallengesViewModel vm) return;
            // La roue tourne quand le modèle de vue le demande ; elle lui dit quand elle s'arrête.
            vm.SpinRequested += fraction => Wheel.SpinTo(fraction, vm.FinishSpin);
            vm.SlicesChanged += () => { Wheel.Slices = vm.Slices; Wheel.InvalidateVisual(); };
            Wheel.Slices = vm.Slices;
        };
    }

    /// <summary>Le champ du pourcentage prend le focus dès qu'il apparaît (et sélectionne son texte).</summary>
    private void PercentBox_Attached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextBox box) return;
        box.PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty && box.IsVisible)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { box.Focus(); box.SelectAll(); });
        };
    }

    private void PercentBox_LostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ChallengeEntry entry }) entry.Commit();
    }
}
