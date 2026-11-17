using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia;
using SpamDrafter.App.ViewModels;

namespace SpamDrafter.App.Views;

public partial class MainWindow : Window
{
    private bool _allowClose;

    /// <summary>Sons : un petit clic à chaque bouton, un « pop » à chaque choix dans une liste (phase 13).</summary>
    /// <summary>Ctrl+F : le curseur va dans la recherche de l'onglet visible.</summary>
    private void FocusSearch()
    {
        var box = this.GetVisualDescendants().OfType<TextBox>()
            .FirstOrDefault(t => t.IsEffectivelyVisible && t.PlaceholderText?.StartsWith("Rechercher", StringComparison.Ordinal) == true);
        if (box is null) return;
        box.Focus(); box.SelectAll();
    }

    private void WireSounds()
    {
        // Clic droit sur une carte, dans n'importe quel onglet : sa fiche détaillée s'ouvre dans la Bibliothèque.
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
            if ((e.Source as Visual)?.FindAncestorOfType<CardFace>() is { Item: { } card } && DataContext is MainViewModel vm)
            {
                vm.OpenCardDetail(card);
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.F && e.KeyModifiers == Avalonia.Input.KeyModifiers.Control) { FocusSearch(); e.Handled = true; }
        };
        AddHandler(Button.ClickEvent, (_, _) => Sounds.Play(SoundKind.Click), handledEventsToo: true);
        AddHandler(SelectingItemsControl.SelectionChangedEvent, (_, e) =>
        {
            if (e.Source is ListBox && e.AddedItems.Count > 0) Sounds.Play(SoundKind.Select);
        });
    }

    /// <summary>Reprend la taille, la position et l'état de la fenêtre de la dernière fois (sans sortir de l'écran).</summary>
    private void RestoreWindow()
    {
        if (DataContext is not MainViewModel { IsLoaded: true } vm) return;
        var s = vm.Settings;
        if (s.WindowWidth is > 400 and < 10000 && s.WindowHeight is > 300 and < 10000)
        {
            Width = s.WindowWidth.Value; Height = s.WindowHeight.Value;
        }
        if (s.WindowX is { } x && s.WindowY is { } y
            && Screens.All.Any(sc => sc.WorkingArea.Contains(new PixelPoint(x + 60, y + 20))))
            Position = new PixelPoint(x, y);
        if (s.Maximized) WindowState = Avalonia.Controls.WindowState.Maximized;
    }

    private void SaveWindow()
    {
        if (DataContext is not MainViewModel vm) return;
        vm.SaveSettings(s =>
        {
            s.Maximized = WindowState == Avalonia.Controls.WindowState.Maximized;
            if (WindowState == Avalonia.Controls.WindowState.Normal)
            {
                s.WindowWidth = Width; s.WindowHeight = Height;
                s.WindowX = Position.X; s.WindowY = Position.Y;
            }
        });
    }

    public MainWindow()
    {
        InitializeComponent();
        WireSounds();
        Opened += (_, _) => RestoreWindow();
        Closed += (_, _) => SaveWindow();
        // Phase 12, lot A : fermer la fenêtre avec un deck modifié et non sauvegardé demande d'abord confirmation.
        Closing += (_, e) =>
        {
            if (_allowClose || DataContext is not MainViewModel { DeckBuilder: { IsDirty: true } builder } vm) return;
            e.Cancel = true;
            vm.SelectedTab = 1;
            builder.RequestLeave(() => { _allowClose = true; Close(); }, "Quitter SpamDrafter");
        };
    }

    /// <summary>Bouton « Importer des données… » (D-060) : le joueur choisit un paquet SpamDrafter-donnees-….zip.</summary>
    private async void ImportData_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { Updater: { } updater } || !updater.CanImport) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choisir un paquet de données SpamDrafter",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Paquet de données SpamDrafter (.zip)") { Patterns = ["*.zip"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        await updater.ImportDataAsync(path);
    }
}
