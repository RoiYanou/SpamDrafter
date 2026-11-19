using System.Text.Json;
using System.Text.Json.Serialization;
using SpamDrafter.Core.Decks;

namespace SpamDrafter.Core.Collection;

/// <summary>
/// Brouillon d'une importation complète interrompue (« Continuer plus tard ») : la position et les réponses déjà données.
/// La collection du joueur n'est remplacée qu'à la fin de l'importation (annuler ne change donc rien).
/// </summary>
public sealed class ImportDraft
{
    /// <summary>Numéro (à partir de 0) de la prochaine carte à montrer.</summary>
    public int Position { get; set; }
    /// <summary>Réponses données : identifiant de carte → niveau de talent (−1 : carte non possédée).</summary>
    public Dictionary<string, int> Answers { get; set; } = new();
}

/// <summary>
/// Les cartes et talents que le joueur possède dans le jeu officiel (phase 15, D-087). Le jeu ne permet pas de les lire :
/// le joueur les saisit à la main (importation complète ou précise).
/// <para>Pour chaque carte possédée, un <b>niveau de talent</b> de 0 à 3 : 0 = aucun talent, 1 = « Talent 1 », 2 = « Talent 2 »
/// (et donc aussi le 1), 3 = « Talent 3 » (et donc aussi le 1 et le 2). « Talent n » est le n-ième talent de la carte,
/// comme partout dans l'application. Une carte absente de la liste n'est pas possédée.</para>
/// </summary>
public sealed class PlayerCollection
{
    public const int NotOwned = -1;
    public const int MaxTalentLevel = 3;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Identifiant de la carte → niveau de talent (0 à 3).</summary>
    public Dictionary<string, int> Cards { get; set; } = new();
    /// <summary>Version de SPAM pendant laquelle la collection a été saisie (information, jamais bloquante).</summary>
    public string GameVersion { get; set; } = "";
    public DateTime? ModifiedUtc { get; set; }
    /// <summary>Importation complète en pause, ou null.</summary>
    public ImportDraft? Draft { get; set; }

    [JsonIgnore] public int Count => Cards.Count;
    [JsonIgnore] public bool IsEmpty => Cards.Count == 0;

    /// <summary>Niveau de talent de la carte (0 à 3), ou <see cref="NotOwned"/> (−1) si elle n'est pas possédée.</summary>
    public int LevelOf(string cardId) => Cards.TryGetValue(cardId, out var l) ? Math.Clamp(l, 0, MaxTalentLevel) : NotOwned;

    public bool Owns(string cardId) => Cards.ContainsKey(cardId);

    /// <summary>Change le niveau d'une carte ; <see cref="NotOwned"/> la retire.</summary>
    public void SetLevel(string cardId, int level)
    {
        if (level < 0) Cards.Remove(cardId);
        else Cards[cardId] = Math.Min(level, MaxTalentLevel);
        ModifiedUtc = DateTime.UtcNow;
    }

    public void Clear() { Cards.Clear(); ModifiedUtc = DateTime.UtcNow; }

    /// <summary>
    /// Les talents possédés d'une carte, dans l'ordre de la carte : les <c>niveau</c> premiers. <paramref name="talents"/> est la
    /// liste des talents de la carte (« Talent 1 », « Talent 2 », « Talent 3 »).
    /// </summary>
    public IReadOnlyList<string> OwnedTalents(string cardId, IReadOnlyList<string> talents)
    {
        var level = LevelOf(cardId);
        return level <= 0 ? Array.Empty<string>() : talents.Take(level).ToList();
    }

    /// <summary>La carte est possédée et, si un talent est choisi, ce talent aussi (null = « aucun talent » : toujours possédé).</summary>
    public bool OwnsSlot(string cardId, string? talentId, IReadOnlyList<string> talents) =>
        Owns(cardId) && (talentId is null || OwnedTalents(cardId, talents).Contains(talentId));

    /// <summary>
    /// Un deck est « possédé » quand chacune de ses cartes l'est avec le talent choisi (un deck de 4 cartes : les 4 cartes et
    /// les 4 talents). <paramref name="talentsOf"/> donne les talents d'une carte (null = carte inconnue : deck non possédé).
    /// </summary>
    public bool OwnsDeck(Deck deck, Func<string, IReadOnlyList<string>?> talentsOf) =>
        deck.Slots.Count > 0 && deck.Slots.All(s => talentsOf(s.CardId) is { } t && OwnsSlot(s.CardId, s.TalentId, t));

    /// <summary>Empreinte de la collection (change dès qu'une carte ou un niveau change) : entre dans la clé de la mémoire du générateur.</summary>
    public string Fingerprint() =>
        string.Join(";", Cards.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => $"{c.Key}:{Math.Clamp(c.Value, 0, MaxTalentLevel)}"));

    // ----- Fichier -----

    public static string DefaultPath(string settingsPath) => Path.Combine(Path.GetDirectoryName(settingsPath)!, "collection.json");

    /// <summary>Fichier absent ou abîmé : collection vide, sans erreur.</summary>
    public static PlayerCollection Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<PlayerCollection>(File.ReadAllText(path), Options) ?? new PlayerCollection();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
        return new PlayerCollection();
    }

    /// <summary>Enregistre sans jamais lever d'erreur.</summary>
    public bool TrySave(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}
