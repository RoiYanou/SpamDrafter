using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia;
using Avalonia.VisualTree;
using SpamDrafter.Core.Decks;
using SpamDrafter.App.ViewModels;

namespace SpamDrafter.App.Views;

public partial class DeckBuilderView : UserControl
{
    private DeckBuilderViewModel? _wired;

    public DeckBuilderView()
    {
        InitializeComponent();
        PanelLayout.Attach(RootGrid, "builder", ResetLayout);
        Picker.CardActivated += (_, _) => (DataContext as DeckBuilderViewModel)?.AddCommand.Execute(null);
        DeckSearchBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is DeckBuilderViewModel vm) { vm.DeckFilter = ""; e.Handled = true; }
        };
        DataContextChanged += (_, _) =>
        {
            if (_wired is not null) { _wired.ClipboardWriteRequested -= OnClipboardWrite; _wired.ValidationFailed -= OnValidationFailed; }
            _wired = DataContext as DeckBuilderViewModel;
            if (_wired is not null) { _wired.ClipboardWriteRequested += OnClipboardWrite; _wired.ValidationFailed += OnValidationFailed; }
        };
    }

    private void OnValidationFailed() => Shake.Run(NameBox);

    /// <summary>Un emoji choisi dans la liste (le paramètre vide = aucun emoji).</summary>
    private void Emoji_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DeckBuilderViewModel vm && sender is Button { Tag: string emoji }) vm.DeckEmoji = emoji;
        EmojiButton.Flyout?.Hide();
    }

    private void Present_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DeckBuilderViewModel vm) return;
        var chips = vm.PresentationChips();
        if (!chips.Any(c => c.HasCard)) { vm.Notify("Ajoute au moins une carte pour présenter le deck."); return; }
        new PresentationWindow(vm.PresentationTitle, chips).Show(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException());
    }

    private async void OnClipboardWrite(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }

    /// <summary>Bouton « Coller un deck… » : lit le texte du presse-papiers (voir « Copier le deck »).</summary>
    private async void Paste_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DeckBuilderViewModel vm) return;
        var text = TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
        if (string.IsNullOrWhiteSpace(text)) { vm.ImportText(""); return; }
        vm.ImportText(text);
    }

    /// <summary>Bouton « Sauvegarder mes decks… » : tous les decks dans un fichier .zip.</summary>
    private async void Backup_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DeckBuilderViewModel vm || TopLevel.GetTopLevel(this) is not { } top) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Sauvegarder mes decks",
            SuggestedFileName = DeckBackup.SuggestedName(DateTime.Now),
            DefaultExtension = "zip",
            FileTypeChoices = [new FilePickerFileType("Sauvegarde de decks (.zip)") { Patterns = ["*.zip"] }],
        });
        if (file?.TryGetLocalPath() is { } path) vm.BackupDecks(path);
    }

    /// <summary>Bouton « Restaurer des decks… » : ajoute les decks d'un .zip (rien n'est supprimé).</summary>
    private async void Restore_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DeckBuilderViewModel vm || TopLevel.GetTopLevel(this) is not { } top) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Restaurer des decks",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Sauvegarde de decks (.zip)") { Patterns = ["*.zip"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) vm.RestoreDecks(path);
    }
}
