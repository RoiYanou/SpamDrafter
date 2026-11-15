using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.Core.DataPackages;

/// <summary>Cherche un paquet de données plus récent que celui installé, le télécharge et l'installe.</summary>
public sealed class DataUpdater
{
    private readonly DataFeed _feed;
    private readonly DataStore _store;

    public DataUpdater(DataFeed feed, DataStore store) { _feed = feed; _store = store; }

    /// <summary>Le paquet à installer (aucun installé, ou plus récent), ou null si les données sont à jour.</summary>
    public async Task<PackageEntry?> CheckAsync(CancellationToken ct = default)
    {
        var index = await _feed.ReadIndexAsync(ct);
        if (index is null || index.PackageFormat != DataPackage.PackageFormat) return null;
        var latest = index.Latest(KnowledgeBase.SupportedFormatVersion);
        if (latest is null) return null;
        var active = _store.FindActive()?.Manifest;
        return active is null || IsNewer(latest, active) ? latest : null;
    }

    public static bool IsNewer(PackageEntry candidate, PackageManifest installed)
    {
        var c = DataPackage.ParseGameVersion(candidate.GameVersion);
        var i = DataPackage.ParseGameVersion(installed.GameVersion);
        return c > i || (c == i && candidate.Revision > installed.Revision);
    }

    /// <summary>Télécharge (0–50 %) puis installe (50–100 %) le paquet ; le .zip provisoire est supprimé à la fin.</summary>
    public async Task<PackageManifest> DownloadAndInstallAsync(PackageEntry entry, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var downloads = Path.Combine(_store.Root, ".telechargement");
        var zip = Path.Combine(downloads, entry.File);
        try
        {
            await _feed.DownloadAsync(entry, zip, Scale(progress, 0), ct);
            return await Task.Run(() => _store.Install(zip, entry.Sha256, Scale(progress, 50)), ct);
        }
        finally
        {
            try { Directory.Delete(downloads, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private static IProgress<int>? Scale(IProgress<int>? p, int offset) =>
        p is null ? null : new SyncProgress(v => p.Report(offset + v / 2));

    /// <summary>Progression transmise immédiatement (sans passer par un contexte de synchronisation).</summary>
    private sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
