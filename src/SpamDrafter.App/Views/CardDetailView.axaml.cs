using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using SpamDrafter.App.ViewModels;

namespace SpamDrafter.App.Views;

/// <summary>
/// Fiche détaillée d'une carte. La grande carte de la fiche « vitrine » s'incline selon la souris (±14°) et se retourne en 300 ms
/// quand on passe de la carte de base au skin. « Réduire les animations » (<see cref="Motion.Reduce"/>) coupe les deux.
/// </summary>
public partial class CardDetailView : UserControl
{
    private readonly Rotate3DTransform _rotation = new() { Depth = 700 };
    private readonly DispatcherTimer _flip = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private DateTime _flipStart;
    private LibraryViewModel? _vm;

    public CardDetailView()
    {
        InitializeComponent();
        Hero.RenderTransform = _rotation;
        // Un clic sur le fond assombri (en dehors de la fiche) ferme la fiche.
        Backdrop.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.Source, Backdrop)) { _vm?.CloseOverlayCommand.Execute(null); e.Handled = true; }
        };
        Hero.PointerMoved += (_, e) =>
        {
            if (Motion.Reduce || _flip.IsEnabled) return;
            var p = e.GetPosition(Hero);
            _rotation.AngleY = (p.X / Hero.Bounds.Width - 0.5) * 28;
            _rotation.AngleX = -(p.Y / Hero.Bounds.Height - 0.5) * 28;
        };
        Hero.PointerExited += (_, _) => { if (!_flip.IsEnabled) { _rotation.AngleX = 0; _rotation.AngleY = 0; } };
        _flip.Tick += (_, _) =>
        {
            var t = (DateTime.UtcNow - _flipStart).TotalMilliseconds / 300;
            if (t >= 1) { _flip.Stop(); _rotation.AngleY = 0; _rotation.AngleX = 0; return; }
            var ease = 1 - Math.Pow(1 - t, 3);            // départ rapide, arrivée douce
            _rotation.AngleY = -90 * (1 - ease);
        };
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.SkinFlipped -= Flip;
            _vm = DataContext as LibraryViewModel;
            if (_vm is not null) _vm.SkinFlipped += Flip;
        };
    }

    private void Flip()
    {
        if (Motion.Reduce) return;
        _flipStart = DateTime.UtcNow;
        _flip.Start();
    }
}
