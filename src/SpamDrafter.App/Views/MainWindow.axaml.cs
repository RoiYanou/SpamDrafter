using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SpamDrafter.App.ViewModels;

namespace SpamDrafter.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

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
