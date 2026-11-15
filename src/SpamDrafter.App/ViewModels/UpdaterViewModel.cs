using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using SpamDrafter.Core.DataPackages;
using SpamDrafter.Core.Updates;
using Velopack;

namespace SpamDrafter.App.ViewModels;

/// <summary>
/// Mises à jour automatiques (D-013, D-058), depuis la source configurée :
/// 1. le programme (Velopack) ; 2. s'il est à jour, le paquet de données du jeu (lot B, D-016).
/// Chaque mise à jour est téléchargée puis installée au redémarrage proposé.
/// Les decks (%APPDATA%\SpamDrafter) et les données (%LOCALAPPDATA%\SpamDrafter\data) sont hors du
/// dossier du programme : une mise à jour du programme n'y touche pas.
/// </summary>
public sealed class UpdaterViewModel : ViewModelBase
{
    private readonly UpdateManager? _manager;
    private readonly DataUpdater? _data;
    private readonly DataStore _store;
    private UpdateInfo? _pendingProgram;
    private bool _dataInstalled;

    public UpdaterViewModel(DataStore store)
    {
        _store = store;
        var source = UpdateSource.Resolve();
        try
        {
            if (source is not null)
            {
                // Source configurée (essais, dossier local) : programme et données au même endroit.
                _manager = new UpdateManager(source);
                _data = new DataUpdater(new DataFeed(source), store);
            }
            else
            {
                // Cas normal : GitHub Releases (versions publiées seulement, pas les pré-versions).
                _manager = new UpdateManager(new Velopack.Sources.GithubSource(UpdateSource.GitHubRepository, null, false));
                _data = new DataUpdater(new DataFeed(UpdateSource.GitHubDataFeed), store);
            }
        }
        catch (Exception e) { _status = "Mise à jour : source invalide (" + e.Message + ")"; }

        var installed = _manager is { IsInstalled: true } ? _manager.CurrentVersion?.ToString() : null;
        Version = installed ?? typeof(UpdaterViewModel).Assembly.GetName().Version?.ToString(3) ?? "?";
        IsInstalled = installed is not null;
        CheckCommand = new RelayCommand(_ => _ = CheckAsync(), _ => CanCheck);
        RestartCommand = new RelayCommand(_ => Restart(), _ => UpdateReady);
        if (_status is null)
            _status = IsInstalled ? "" : "Version de développement : pas de mise à jour";
    }

    public string Version { get; }
    public bool IsInstalled { get; }
    public RelayCommand CheckCommand { get; }
    public RelayCommand RestartCommand { get; }

    private string? _status;
    public string? Status { get => _status; private set => Set(ref _status, value); }

    private bool _busy;
    private bool CanCheck => _manager is { IsInstalled: true } && !_busy && !UpdateReady;
    /// <summary>Import d'un paquet de données depuis un fichier (D-060) : possible dès qu'aucun autre travail n'est en cours.</summary>
    public bool CanImport => !_busy && !UpdateReady;

    private bool _updateReady;
    public bool UpdateReady { get => _updateReady; private set => Set(ref _updateReady, value); }

    public async Task CheckAsync()
    {
        if (!CanCheck) return;
        _busy = true; Refresh();
        try
        {
            Status = "Recherche d'une mise à jour…";
            var info = await _manager!.CheckForUpdatesAsync();
            if (info is not null)
            {
                await DownloadProgramAsync(info);
                return;
            }
            // Programme à jour : on regarde les données du jeu (elles peuvent demander ce programme-ci).
            var entry = await _data!.CheckAsync();
            if (entry is null)
            {
                // Sans données installées, « À jour » serait trompeur : la source ne propose aucun paquet lisible.
                Status = _store.FindActive() is null
                    ? "Aucune donnée du jeu à la source des mises à jour : utilise « Importer des données… »"
                    : "À jour";
                return;
            }
            await DownloadDataAsync(entry);
        }
        catch (Exception e)
        {
            Status = "Mise à jour impossible : " + e.Message;
        }
        finally { _busy = false; Refresh(); }
    }

    private async Task DownloadProgramAsync(UpdateInfo info)
    {
        var target = info.TargetFullRelease.Version;
        Status = $"Téléchargement de la version {target}…";
        var done = false;
        // Velopack télécharge le delta puis reconstruit le paquet complet (KI-061).
        await _manager!.DownloadUpdatesAsync(info, p => Dispatcher.UIThread.Post(() =>
        {
            if (!done) Status = $"Téléchargement et préparation de la version {target}… {p} %";
        }));
        done = true;
        _pendingProgram = info;
        UpdateReady = true;
        Status = $"Version {target} prête";
    }

    private async Task DownloadDataAsync(PackageEntry entry)
    {
        var label = $"données SPAM {entry.GameVersion} (révision {entry.Revision}, {entry.Size / 1024 / 1024} Mo)";
        Status = $"Téléchargement des {label}…";
        var done = false;
        await _data!.DownloadAndInstallAsync(entry, new Progress<int>(p =>
        {
            if (!done) Status = p < 50 ? $"Téléchargement des {label}… {p * 2} %" : $"Vérification et installation des données… {(p - 50) * 2} %";
        }));
        done = true;
        _dataInstalled = true;
        UpdateReady = true;
        Status = $"Données SPAM {entry.GameVersion} (révision {entry.Revision}) prêtes";
    }

    /// <summary>
    /// Installe un paquet de données choisi par le joueur (D-060), par exemple sans connexion ; le redémarrage
    /// proposé ensuite charge les nouvelles données, comme après un téléchargement.
    /// </summary>
    public async Task ImportDataAsync(string zipPath)
    {
        if (!CanImport) return;
        _busy = true; Refresh();
        try
        {
            Status = "Import des données… 0 %";
            var done = false;
            var progress = new Progress<int>(p => { if (!done) Status = $"Vérification et installation des données… {p} %"; });
            var m = await Task.Run(() => _store.ImportFile(zipPath, progress));
            done = true;
            _dataInstalled = true;
            UpdateReady = true;
            Status = $"Données SPAM {m.GameVersion} (révision {m.Revision}) importées";
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Status = "Import impossible : " + e.Message;
        }
        finally { _busy = false; Refresh(); }
    }

    private void Restart()
    {
        if (_pendingProgram is not null && _manager is not null)
        {
            _manager.ApplyUpdatesAndRestart(_pendingProgram.TargetFullRelease);
            return;
        }
        if (!_dataInstalled || Environment.ProcessPath is not { } exe) return;
        // Nouvelles données : on relance simplement le programme pour les charger.
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
        else Environment.Exit(0);
    }

    private void Refresh()
    {
        Dispatcher.UIThread.Post(() => { CheckCommand.RaiseCanExecuteChanged(); RestartCommand.RaiseCanExecuteChanged(); });
    }
}
