using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace SpamDrafter.App.Views;

/// <summary>
/// Vignette « paresseuse » de la grille de cartes : la carte (lourde : cadre, ombres, reflets, capacités) n'est construite que lorsque la vignette
/// est visible ou proche de l'être (une page d'écran autour), et retirée quand elle est très loin. La vignette elle-même (juste une boîte de la
/// bonne taille) existe toujours : la mise en page, le défilement et la sélection ne changent pas, mais seules ~20 cartes sont construites au lieu de ~150.
/// </summary>
public sealed class LazyTile : ContentControl
{
    public static readonly StyledProperty<object?> ItemProperty = AvaloniaProperty.Register<LazyTile, object?>(nameof(Item));
    /// <summary>La carte à afficher (devient le contenu quand la vignette est visible ou proche).</summary>
    public object? Item { get => GetValue(ItemProperty); set => SetValue(ItemProperty, value); }

    protected override Type StyleKeyOverride => typeof(ContentControl);

    private bool _queued;

    public LazyTile()
    {
        EffectiveViewportChanged += OnViewport;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemProperty && Content is not null) Content = Item;   // la vignette est réutilisée pour une autre carte
    }

    private void OnViewport(object? sender, EffectiveViewportChangedEventArgs e)
    {
        var vp = e.EffectiveViewport;
        var h = Bounds.Height;
        if (double.IsInfinity(vp.Height) || double.IsNaN(vp.Height)) { Realize(); return; }   // pas de zone de défilement : on construit
        if (vp.Height <= 0 && vp.Width <= 0) return;                                       // mesure pas encore faite : on attend l'événement suivant
        // Écart vertical entre la vignette (0..h) et la zone visible : négatif ou nul = visible.
        var gap = Math.Max(vp.Top - h, -vp.Bottom);
        var screen = Math.Max(vp.Height, 200);
        if (gap <= 0) Realize();
        else if (gap < screen) { if (Content is null && !_queued) { _queued = true; Dispatcher.UIThread.Post(() => { _queued = false; if (IsEffectivelyVisible) Realize(); }, DispatcherPriority.Background); } }
        else if (gap > screen * 3 && Content is not null) Content = null;
    }

    private void Realize()
    {
        if (Content is null && Item is not null) Content = Item;
    }
}
