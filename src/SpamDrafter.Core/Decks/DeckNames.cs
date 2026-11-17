using SpamDrafter.Core.Text;

namespace SpamDrafter.Core.Decks;

/// <summary>Noms de decks uniques, sans tenir compte des accents ni de la casse (D-072).</summary>
public static class DeckNames
{
    /// <summary>Forme de comparaison d'un nom.</summary>
    public static string Key(string? name) => TextSearch.Normalize(name);

    /// <summary>Vrai si un autre deck (identifiant différent) porte déjà ce nom.</summary>
    public static bool IsTaken(IEnumerable<Deck> decks, string name, Guid exceptId)
    {
        var key = Key(name);
        return decks.Any(d => d.Id != exceptId && Key(d.Name) == key);
    }

    /// <summary>Le nom demandé s'il est libre, sinon « nom (2) », « nom (3) »…</summary>
    public static string MakeUnique(IEnumerable<Deck> decks, string name, Guid exceptId)
    {
        var others = decks.Where(d => d.Id != exceptId).Select(d => Key(d.Name)).ToHashSet();
        var baseName = name.Trim();
        if (!others.Contains(Key(baseName))) return baseName;
        for (var n = 2; ; n++)
        {
            var candidate = $"{baseName} ({n})";
            if (!others.Contains(Key(candidate))) return candidate;
        }
    }

    /// <summary>
    /// Donne un nom unique aux doublons déjà enregistrés : le premier deck (dans l'ordre donné) garde son nom, les suivants
    /// reçoivent « (2) », « (3) »… Rien n'est supprimé. Renvoie les decks renommés.
    /// </summary>
    public static IReadOnlyList<Deck> FixDuplicates(IReadOnlyList<Deck> decks)
    {
        var renamed = new List<Deck>();
        var seen = new HashSet<string>();
        foreach (var d in decks)
        {
            var key = Key(d.Name);
            if (key.Length > 0 && seen.Add(key)) continue;
            if (key.Length == 0) continue;   // un nom vide est refusé à la sauvegarde ; on ne touche pas aux anciens fichiers
            for (var n = 2; ; n++)
            {
                var candidate = $"{d.Name.Trim()} ({n})";
                if (seen.Add(Key(candidate))) { d.Name = candidate; renamed.Add(d); break; }
            }
        }
        return renamed;
    }
}
