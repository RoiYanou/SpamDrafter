namespace SpamDrafter.Core.Knowledge;

/// <summary>Carte jouable (extrait de cards.json).</summary>
public sealed record Card(
    string Id,
    int Order,
    string SanitizedName,
    string NameKey,
    string Element,
    string Rarity,
    int Hp,
    int GoldIncome,
    IReadOnlyDictionary<string, int> Resistances,
    IReadOnlyList<string> Abilities,
    IReadOnlyList<string> Talents,
    string? Image,
    string? Thumbnail);

/// <summary>Talent (extrait de talents.json). L'identifiant est le nom interne du jeu.</summary>
public sealed record Talent(string Id, bool Reachable, string Element);

public sealed record Ordered(string Id, int Order, string NameKey);

public sealed record Manifest(string GameVersion, string FormatVersion);
