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
    string? Thumbnail,
    IReadOnlyList<string>? InnatePassives = null,
    string? Artist = null,
    IReadOnlyList<CardSkin>? Skins = null);

/// <summary>
/// Skin alternatif d'une carte (illustration alternative du jeu) : son identifiant (« cardstyle-alternativeart_… »), ses images
/// (chemins relatifs dans la base), son artiste et s'il est animé. Absent des bases plus anciennes : la carte n'a alors que son illustration de base.
/// </summary>
public sealed record CardSkin(string Id, string? Image, string? Thumbnail, string? Artist = null, bool Animated = false);

/// <summary>Talent (extrait de talents.json). L'identifiant est le nom interne du jeu.</summary>
public sealed record Talent(string Id, bool Reachable, string Element);

/// <summary>
/// Élément ou rareté. <c>Icon</c> : icône du jeu (chemin relatif dans la base) ; <c>Color</c> / <c>TextColor</c> (raretés) : les couleurs
/// du jeu en « #RRGGBB ». Absents des bases plus anciennes : l'application fonctionne sans.
/// </summary>
public sealed record Ordered(string Id, int Order, string NameKey, string? Icon = null, string? Color = null, string? TextColor = null);

/// <summary>Un Tox (passif) : ceux qu'une carte possède de base (<c>Card.InnatePassives</c>) et ceux de la boutique.</summary>
public sealed record ToxEntry(string Uuid, string AssetName, string Title, string StatusType, double Value, string? Element, string? Rarity, string? Image,
    int Cost = 0, string? ShopPool = null, int InnateOnCards = 0)
{
    /// <summary>Vendu en boutique : le jeu le range dans une liste de la boutique (🃏 <c>shopPool</c>) ; sinon le prix n'a pas de sens.</summary>
    public bool InShop => ShopPool is not null && Cost > 0;
}

/// <summary>Un artiste du jeu (artists.json) : nom et lien public (réseau social, site).</summary>
public sealed record ArtistEntry(string Id, string Name, string? Link);

/// <summary>Précision, critiques et recharge d'une capacité de carte (🃏 données du jeu).</summary>
public sealed record AbilityStats(double ChanceOfMiss, bool CanCrit, double CritChance, double CritRatio, int Cooldown, string? Element = null)
{
    /// <summary>Précision en % : 100 − chance de rater.</summary>
    public double Precision => 100 - ChanceOfMiss;
}

public sealed record Manifest(string GameVersion, string FormatVersion);
