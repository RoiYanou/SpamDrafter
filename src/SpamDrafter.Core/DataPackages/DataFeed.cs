namespace SpamDrafter.Core.DataPackages;

/// <summary>
/// Lecture de la source des mises à jour pour les données : un dossier local (phase 9)
/// ou une adresse web (phase 10). Même source que le programme (<see cref="Updates.UpdateSource"/>).
/// </summary>
public sealed class DataFeed
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private readonly string _source;

    public DataFeed(string source) { _source = source.Trim().TrimEnd('/', '\\'); }

    private bool IsWeb => _source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                       || _source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>L'index donnees.json, ou null si la source n'en publie pas.</summary>
    public async Task<PackageIndex?> ReadIndexAsync(CancellationToken ct = default)
    {
        if (IsWeb)
        {
            using var r = await Http.GetAsync($"{_source}/{DataPackage.IndexFile}", ct);
            if (r.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            r.EnsureSuccessStatusCode();
            await using var s = await r.Content.ReadAsStreamAsync(ct);
            return await System.Text.Json.JsonSerializer.DeserializeAsync<PackageIndex>(s, DataPackage.Json, ct);
        }
        var path = Path.Combine(_source, DataPackage.IndexFile);
        return File.Exists(path) ? DataPackage.ReadJson<PackageIndex>(path) : null;
    }

    /// <summary>Copie (ou télécharge) le paquet vers <paramref name="destination"/>.</summary>
    public async Task DownloadAsync(PackageEntry entry, string destination, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (entry.File.Contains('/') || entry.File.Contains('\\') || entry.File.Contains(".."))
            throw new InvalidDataException($"Nom de paquet interdit : {entry.File}");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        HttpResponseMessage? response = null;
        Stream input;
        if (IsWeb)
        {
            response = await Http.GetAsync($"{_source}/{entry.File}", HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            input = await response.Content.ReadAsStreamAsync(ct);
        }
        else input = File.OpenRead(Path.Combine(_source, entry.File));
        try
        {
            await using var output = File.Create(destination);
            var buffer = new byte[1 << 20];
            long total = 0;
            int n, last = -1;
            while ((n = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, n), ct);
                total += n;
                var pct = entry.Size > 0 ? (int)Math.Min(100, total * 100 / entry.Size) : 0;
                if (pct != last) { progress?.Report(pct); last = pct; }
            }
        }
        finally
        {
            await input.DisposeAsync();
            response?.Dispose();
        }
    }
}
