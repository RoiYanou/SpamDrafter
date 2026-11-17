using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpamDrafter.Core.Versions;

public enum ChangeKind { Added, Removed, Changed }

/// <summary>
/// Une différence sur une carte entre deux versions de SPAM. <c>Field</c> : <c>hp</c>, <c>goldIncome</c>, <c>element</c>, <c>rarity</c>,
/// <c>resistance.&lt;élément&gt;</c>, <c>abilities</c> / <c>talents</c> (listes d'identifiants séparés par « | »),
/// <c>ability.changed</c> (la capacité d'identifiant <c>Before</c> a changé : détail dans <see cref="ChangeSet.Abilities"/>).
/// Les valeurs sont des données brutes (identifiants, nombres) : le texte affiché est fabriqué par l'application, dans sa langue.
/// </summary>
public sealed record FieldChange(string Field, string? Before, string? After);

public sealed record CardChange(string CardId, string Name, ChangeKind Kind, IReadOnlyList<FieldChange> Fields);

/// <summary>
/// Un talent ou une capacité ajouté, retiré ou modifié. Les descriptions (chiffrées, voir <c>DescriptionFormatter</c>) sont
/// celles de la version d'origine et de la nouvelle ; null quand le jeu n'en donne pas. Quand elles sont identiques alors que les
/// réglages internes ont changé, <see cref="InternalOnly"/> est vrai.
/// </summary>
public sealed record EntryChange(string Id, string Name, ChangeKind Kind, string? DescriptionBefore, string? DescriptionAfter, bool InternalOnly);

/// <summary>
/// Les différences entre deux versions de SPAM (phase 12, lot E). Fabriquées par le Studio à chaque nouvelle version de SPAM,
/// livrées avec les données (<c>changes/from-&lt;version&gt;.json</c>), lues par l'application pour comparer un deck créé avec une
/// version plus ancienne à la version actuelle.
/// </summary>
public sealed record ChangeSet(int FormatVersion, string From, string To,
    IReadOnlyList<CardChange> Cards, IReadOnlyList<EntryChange> Talents, IReadOnlyList<EntryChange> Abilities)
{
    public const int CurrentFormat = 1;

    public bool IsEmpty => Cards.Count == 0 && Talents.Count == 0 && Abilities.Count == 0;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Nom du fichier dans <c>knowledge/&lt;version&gt;/changes/</c> pour les différences depuis une version.</summary>
    public static string FileName(string fromVersion) => "from-" + Safe(fromVersion) + ".json";

    private static string Safe(string v) => string.Concat(v.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));

    public void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Lit un fichier de différences ; null s'il est absent, illisible ou d'un format que ce programme ne connaît pas.</summary>
    public static ChangeSet? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var cs = JsonSerializer.Deserialize<ChangeSet>(File.ReadAllText(path), Options);
            return cs is { FormatVersion: CurrentFormat } ? cs : null;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    public CardChange? Card(string id) => Cards.FirstOrDefault(c => c.CardId == id);
    public EntryChange? Talent(string id) => Talents.FirstOrDefault(t => t.Id == id);
    public EntryChange? Ability(string id) => Abilities.FirstOrDefault(a => a.Id == id);
}
