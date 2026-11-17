using Avalonia.Controls;

namespace SpamDrafter.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        // Un clic sur la carte du jour l'ouvre en grand ; un clic sur le fond assombri la referme.
        Tilt.Attach(CardOfDayBox); Tilt.Attach(ZoomBox);
        CardOfDayBox.PointerPressed += (_, _) => (DataContext as ViewModels.HomeViewModel)?.OpenZoomCommand.Execute(null);
        ZoomBackdrop.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.Source, ZoomBackdrop)) (DataContext as ViewModels.HomeViewModel)?.CloseZoomCommand.Execute(null);
        };
    }
}
