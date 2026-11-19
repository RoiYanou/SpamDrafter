using Avalonia.Input.Platform;
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
            var pages = new (Control Page, int Tab)[] { (PageLibrary, 0), (PageBuilder, 1), (PageDrafter, 2), (PageGenerator, 3), (PageTester, 5) };
            foreach (var (page, tab) in pages)
            {
                if (vm.SelectedTab == tab || page.IsVisible) continue;
                if (tab == 3 && !vm.HasGenerator) continue;
                if (tab == 5 && !vm.HasTester) continue;
                page.Opacity = 0; page.IsHitTestVisible = false;
                page.SetCurrentValue(IsVisibleProperty, true);
                await Task.Delay(40);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                await WaitImagesAsync();
                if (tab == 0 && vm.LibraryTabs is { } tabs)
                {
                    // Sous-onglets Talents, Tox, Artistes : construits à leur première sélection, donc on les parcourt une fois.
                    var original = tabs.SelectedTab;
                    for (var i = 1; i <= 3 && vm.SelectedTab != 0; i++)
                    {
                        tabs.SelectedTab = i;
                        await Task.Delay(40);
                        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                        await WaitImagesAsync();
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
                foreach (var (p, tab) in new (Control Page, int Tab)[] { (PageLibrary, 0), (PageBuilder, 1), (PageDrafter, 2), (PageGenerator, 3), (PageTester, 5) })
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

    /// <summary>Attend (au plus 2,5 s) que les images de cartes demandées soient chargées, pour qu'elles ne tombent pas toutes d'un coup au premier clic.</summary>
    private static async Task WaitImagesAsync()
    {
        var until = Environment.TickCount64 + 2500;
        while (CardItem.PendingLoads > 0 && Environment.TickCount64 < until) await Task.Delay(40);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    /// <summary>Contact : copie le nom Discord dans le presse-papiers.</summary>
    private async void CopyDiscord_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try { if (Clipboard is { } cb) await cb.SetTextAsync("RoiYanou"); } catch (Exception) { }
    }

    /// <summary>Contact : ouvre la page GitHub du projet dans le navigateur.</summary>
    private async void OpenGitHub_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try { await Launcher.LaunchUriAsync(new Uri(SpamDrafter.Core.Updates.UpdateSource.GitHubRepository)); } catch (Exception) { }
    }

    /// <summary>
    /// Transition entre « Toutes les cartes et talents » et « Cartes et talents possédés » : les listes se reconstruisent d'un coup ;
    /// l'écran est mis à 0 puis revient en fondu, au lieu d'un saut brutal. Sans effet avec « Désactiver les animations ».
    /// </summary>
    private void HookModeTransition()
    {
        if (DataContext is not MainViewModel vm) return;
        void Hook()
        {
            if (vm.Collection is not { } c) return;
            c.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(c.OwnedOnly) || Motion.Reduce) return;
                PagesHost.Transitions = null;
                PagesHost.Opacity = 0;
                Dispatcher.UIThread.Post(() =>
                {
                    PagesHost.Transitions = new Avalonia.Animation.Transitions
                    {
                        new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(320), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
                    };
                    PagesHost.Opacity = 1;
                }, DispatcherPriority.Render);
            };
        }
        if (vm.IsLoaded) Hook();
        else vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.IsLoaded) && vm.IsLoaded) Hook(); };
    }

    private void WireSounds()
    {
        ContactButton.Flyout!.Opened += (_, _) => Rail.Classes.Set("pinned", true);
        ContactButton.Flyout!.Closed += (_, _) => Rail.Classes.Set("pinned", false);
        AutoScroll.Install(this);   // clic molette : défilement automatique, comme sur une page web
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
        BrandBox.PointerPressed += (_, _) => { Bounce.Run(BrandBox); Sounds.PlayAnyTitle(); };
        // Logo de la barre latérale : animé tant que la barre est survolée (ou que le menu Contact est ouvert).
        var brand = new BrandAnimator(PinkPath, YellowPath, GlyphPath, PinkBox, LettersClip, LettersRow, RaysBox, RaysPath, CrownPath);
        var icons = new NavIconAnimator();
        icons.AddHome(NavHome, HomeHole, HomeDoor);
        icons.AddBook(NavLibrary, BookGroup, BookCover, BookCoverOuter, BookCoverInner);
        icons.AddBuilder(NavBuilder, BuilderSq1, BuilderSq2, BuilderSq3, BuilderSq4);
        icons.AddDrafter(NavDrafter, DrafterCanvas, DrafterPink, DrafterHeadPink, DrafterPurpleA, DrafterPurpleB, DrafterHeadPurple);
        icons.AddContact(ContactButton, ContactDot1, ContactDot2, ContactDot3);
        icons.Kick();   // état de départ (porte et livre ouverts si leur onglet est celui choisi)
        Rail.PointerEntered += (_, _) => { brand.Hover = true; brand.Kick(); };
        Rail.PointerExited += (_, _) => { brand.Hover = Rail.Classes.Contains("pinned"); brand.Kick(); };
        ContactButton.Flyout!.Opened += (_, _) => { brand.Hover = true; brand.Kick(); };
        ContactButton.Flyout!.Closed += (_, _) => { brand.Hover = Rail.IsPointerOver; brand.Kick(); };
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
        Opened += (_, _) => { RestoreWindow(); _ = Task.Run(Sounds.Warm); _ = PrewarmAsync(); HookModeTransition(); };
        Closed += (_, _) => { SaveWindow(); SpamDrafter.App.ViewModels.MusicPlayer.Shutdown(); };
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
