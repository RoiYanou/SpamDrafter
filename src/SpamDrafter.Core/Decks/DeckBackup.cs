using System.IO.Compression;
using System.Text.Json;

namespace SpamDrafter.Core.Decks;

/// <summary>Résultat d'une restauration de sauvegarde.</summary>
public sealed record RestoreResult(int Added, int Updated, int Skipped, IReadOnlyList<string> Problems);

/// <summary>
/// Sauvegarde et restauration des decks en un fichier .zip (phase 12, lot A). La restauration ne supprime jamais un deck :
/// elle ajoute les decks absents et met à jour ceux dont la copie sauvegardée est plus récente.
/// </summary>
public static class DeckBackup
{
    public const string FilePrefix = "SpamDrafter-decks-";

    /// <summary>Nom proposé pour le fichier de sauvegarde.</summary>
    public static string SuggestedName(DateTime now) => $"{FilePrefix}{now:yyyy-MM-dd}.zip";

    /// <summary>Écrit tous les decks du dossier dans un .zip. Renvoie le nombre de decks sauvegardés.</summary>
    public static int Create(string decksDirectory, string zipPath)
    {
        var files = Directory.Exists(decksDirectory)
            ? Directory.EnumerateFiles(decksDirectory, "*.json").Order().ToList() : new List<string>();
        var tmp = zipPath + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            foreach (var f in files) zip.CreateEntryFromFile(f, Path.GetFileName(f), CompressionLevel.Optimal);
        File.Move(tmp, zipPath, overwrite: true);
        return files.Count;
    }

    /// <summary>Restaure les decks d'un .zip dans le dossier des decks. Un fichier qui n'est pas un deck est ignoré et signalé.</summary>
    public static RestoreResult Restore(string zipPath, string decksDirectory)
    {
        var problems = new List<string>();
        int added = 0, updated = 0, skipped = 0;
        var store = new DeckStore(decksDirectory);
        var existing = store.LoadAll().ToDictionary(d => d.Id);
        ZipArchive zip;
        try { zip = ZipFile.OpenRead(zipPath); }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            return new RestoreResult(0, 0, 0, new[] { "Ce fichier n'est pas une sauvegarde de decks lisible : " + e.Message });
        }
        using (zip)
        {
            foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                                                         && !e.FullName.Contains('/') && !e.FullName.Contains('\\')))
            {
                Deck? deck;
                try
                {
                    using var s = entry.Open();
                    using var reader = new StreamReader(s);
                    deck = store.Deserialize(reader.ReadToEnd());
                }
                catch (JsonException e) { problems.Add($"{entry.Name} : {e.Message}"); continue; }
                // Un vrai deck a toujours un nom et au moins une carte (la sauvegarde du Deck builder l'exige) : « {} » n'en est pas un.
                if (deck is null || deck.Id == Guid.Empty || deck.Slots is null || deck.Slots.Count == 0
                    || string.IsNullOrWhiteSpace(deck.Name)) { problems.Add($"{entry.Name} : pas un deck."); continue; }
                if (!existing.TryGetValue(deck.Id, out var current)) { store.Save(deck); added++; continue; }
                if ((deck.ModifiedUtc ?? DateTime.MinValue) > (current.ModifiedUtc ?? DateTime.MinValue)) { store.Save(deck); updated++; }
                else skipped++;
            }
        }
        return new RestoreResult(added, updated, skipped, problems);
    }
}
