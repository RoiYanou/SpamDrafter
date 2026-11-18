using Avalonia.Threading;
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

    /// <summary>
    /// Préchargement : une fois la fenêtre affichée, chaque page (et chaque sous-onglet de la Bibliothèque) est affichée une fraction de seconde,
    /// invisible, pour que ses contrôles soient construits, mesurés et dessinés avant le premier clic du joueur (plus de latence au premier changement d'onglet).
    /// Si le joueur va sur une page pendant ce temps, elle est laissée affichée et rien d'autre n'est touché.
    /// </summary>
    private async Task PrewarmAsync()
    {
        try
        {
            if (DataContext is not MainViewModel { IsLoaded: true } vm) return;
            await Task.Delay(300);
            PageFx.Suppress = true;
            var pages = new (Control Page, int Tab)[] { (PageLibrary, 0), (PageBuilder, 1), (PageDrafter, 2), (PageGenerator, 3) };
            foreach (var (page, tab) in pages)
            {
                if (vm.SelectedTab == tab || page.IsVisible) continue;
                if (tab == 3 && !vm.HasGenerator) continue;
                page.Opacity = 0; page.IsHitTestVisible = false;
                page.SetCurrentValue(IsVisibleProperty, true);
                await Task.Delay(40);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                if (tab == 0 && vm.LibraryTabs is { } tabs)
                {
                    // Sous-onglets Talents, Tox, Artistes : construits à leur première sélection, donc on les parcourt une fois.
                    var original = tabs.SelectedTab;
                    for (var i = 1; i <= 3 && vm.SelectedTab != 0; i++)
                    {
                        tabs.SelectedTab = i;
                        await Task.Delay(40);
                        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    }
                    tabs.SelectedTab = original;
                }
                if (vm.SelectedTab != tab) page.SetCurrentValue(IsVisibleProperty, false);
                page.Opacity = 1; page.IsHitTestVisible = true;
            }
        }
        catch (Exception e) when (e is InvalidOperationException or NullReferenceException) { }
        finally
        {
            await Task.Delay(100);
            PageFx.Suppress = false;
            // Sécurité : toute page restée affichée sans que le joueur l'ait choisie est recachée.
            if (DataContext is MainViewModel vm2)
                foreach (var (p, tab) in new (Control Page, int Tab)[] { (PageLibrary, 0), (PageBuilder, 1), (PageDrafter, 2), (PageGenerator, 3) })
                {
                    if (vm2.SelectedTab != tab && p.IsVisible) p.SetCurrentValue(IsVisibleProperty, false);
                    p.Opacity = 1; p.IsHitTestVisible = true;
                }
            // Le voile s'efface en fondu, puis ne gêne plus rien.
            PrewarmCover.Opacity = 0;
            await Task.Delay(350);
            PrewarmCover.IsVisible = false;
        }
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
        // Titre de l'application : un clic le fait rebondir et joue l'un de trois petits sons voisins, au hasard.
        AppTitle.PointerPressed += (_, _) => { Bounce.Run(AppTitle); Sounds.PlayAnyTitle(); };
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

    private bool _quitAsked, _watchingConfirm;

    public MainWindow()
    {
        InitializeComponent();
        WireSounds();
        Opened += (_, _) => { RestoreWindow(); _ = Task.Run(Sounds.Warm); _ = PrewarmAsync(); };
        Closed += (_, _) => SaveWindow();
        // Phase 12, lot A : fermer la fenêtre avec un deck modifié et non sauvegardé demande d'abord confirmation.
        Closing += (_, e) =>
        {
            if (_allowClose || DataContext is not MainViewModel { DeckBuilder: { IsDirty: true } builder } vm) return;
            // Deuxième clic sur la croix alors que la question « Quitter SpamDrafter » est toujours affichée : le joueur abandonne ses modifications.
            if (_quitAsked && builder.ConfirmVisible) { _allowClose = true; return; }
            e.Cancel = true;
            vm.SelectedTab = 1;
            if (!_watchingConfirm)
            {
                _watchingConfirm = true;
                builder.PropertyChanged += (_, a) => { if (a.PropertyName == nameof(builder.ConfirmVisible) && !builder.ConfirmVisible) _quitAsked = false; };
            }
            builder.RequestLeave(() => { _allowClose = true; Close(); }, "Quitter SpamDrafter");
            _quitAsked = builder.ConfirmVisible;
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
