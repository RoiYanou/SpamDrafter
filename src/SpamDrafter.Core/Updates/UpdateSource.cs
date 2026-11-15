namespace SpamDrafter.Core.Updates;

/// <summary>
/// Où l'application cherche ses mises à jour (D-058). Ordre : variable SPAMDRAFTER_UPDATE_SOURCE, puis le fichier
/// <c>%APPDATA%\SpamDrafter\update-source.txt</c> (première ligne non vide) : une même source (dossier ou adresse
/// web) pour le programme et les données. Sinon (cas normal des joueurs, phase 10) : GitHub Releases du dépôt public,
/// le programme dans les versions publiées, les données dans la pré-version <see cref="DataReleaseTag"/>.
/// </summary>
public static class UpdateSource
{
    public const string EnvironmentVariable = "SPAMDRAFTER_UPDATE_SOURCE";
    public const string FileName = "update-source.txt";

    /// <summary>Dépôt public de SpamDrafter (D-019, D-036).</summary>
    public const string GitHubRepository = "https://github.com/RoiYanou/SpamDrafter";
    /// <summary>Pré-version GitHub qui contient donnees.json et les paquets de données (jamais « la dernière version »).</summary>
    public const string DataReleaseTag = "donnees";
    /// <summary>Adresse de la source des données sur GitHub : {adresse}/donnees.json, {adresse}/SpamDrafter-donnees-….zip.</summary>
    public static string GitHubDataFeed => $"{GitHubRepository}/releases/download/{DataReleaseTag}";

    public static string DefaultSettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpamDrafter");

    /// <summary>Renvoie la source configurée, ou null s'il n'y en a pas.</summary>
    public static string? Resolve(string? environmentValue, string settingsDirectory)
    {
        if (!string.IsNullOrWhiteSpace(environmentValue)) return environmentValue.Trim();
        var file = Path.Combine(settingsDirectory, FileName);
        if (!File.Exists(file)) return null;
        return File.ReadLines(file).Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));
    }

    public static string? Resolve() =>
        Resolve(Environment.GetEnvironmentVariable(EnvironmentVariable), DefaultSettingsDirectory);
}
