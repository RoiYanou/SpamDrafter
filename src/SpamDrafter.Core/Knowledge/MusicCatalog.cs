using System.Text.Json;

namespace SpamDrafter.Core.Knowledge;

/// <summary>Une musique du jeu : identifiant stable, titre affiché et fichier .ogg (dans le paquet de données).</summary>
public sealed record MusicTrack(string Id, string Title, string Path);

/// <summary>
/// Musiques du jeu SPAM, fournies par le paquet de données (dossier <c>music/</c> + <c>music/music.json</c>, D-016) : elles ne sont
/// jamais dans le programme. Sans paquet, ou sans musique dans le paquet, la liste est vide et l'application reste silencieuse.
/// </summary>
public static class MusicCatalog
{
    public const string Folder = "music";
    public const string IndexFile = "music.json";

    /// <summary>Lit la liste des musiques de la base ; les fichiers manquants sont ignorés. Jamais d'exception.</summary>
    public static IReadOnlyList<MusicTrack> Load(string knowledgeDirectory)
    {
        try
        {
            var dir = System.IO.Path.Combine(knowledgeDirectory, Folder);
            var index = System.IO.Path.Combine(dir, IndexFile);
            if (!File.Exists(index)) return [];
            using var doc = JsonDocument.Parse(File.ReadAllText(index));
            var list = new List<MusicTrack>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var id = e.GetProperty("id").GetString();
                var title = e.GetProperty("title").GetString();
                var file = e.GetProperty("file").GetString();
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(file)) continue;
                var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, file));
                if (!path.StartsWith(System.IO.Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
                list.Add(new MusicTrack(id, title, path));
            }
            return list;
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or KeyNotFoundException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
