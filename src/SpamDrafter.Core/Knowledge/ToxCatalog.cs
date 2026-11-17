using System.Globalization;

namespace SpamDrafter.Core.Knowledge;

/// <summary>
/// Règles de présentation des Tox dans l'onglet Bibliothèque → Tox (demandées par l'utilisateur, 17/11/2026). Les données du jeu ne sont
/// pas modifiées : ces règles ne changent que ce que l'application liste et affiche.
/// <list type="bullet">
/// <item><b>Passifs de talent</b> : un « Tox » qui n'a pas d'image, qu'aucune carte ne possède de base et qui n'est pas vendu en boutique
/// n'est pas un Tox mais un passif de talent : il n'est pas listé (les trois conditions ensemble).</item>
/// <item><b>Exclus</b> : « Voleur », « SolCamouflage » (le Tox vendu du même nom que « Camouflage »), « Cœur élémentaire C » et « Étourdissement » (Stun_L), à la demande de l'utilisateur.</item>
/// <item><b>Prix</b> : « Camouflage » est vendu 70 Spamtons (les données ne le rangent pas dans une liste de la boutique).</item>
/// </list>
/// </summary>
public static class ToxCatalog
{
    /// <summary>Monnaie de la boutique.</summary>
    public const string Currency = "Spamton";

    private static readonly HashSet<string> Excluded = new(StringComparer.Ordinal) { "Voleur_R", "SolCamouflage_R", "Coeur elementaire C", "Stun_L" };
    private static readonly Dictionary<string, int> ShopPrices = new(StringComparer.Ordinal) { ["Camouflage"] = 70 };

    /// <summary>Ce Tox est-il listé dans l'onglet Tox ?</summary>
    public static bool IsListed(ToxEntry tox) =>
        !Excluded.Contains(tox.Title) && !IsTalentPassive(tox);

    /// <summary>Sans image, sans carte qui le possède de base, et pas vendu en boutique (les trois ensemble).</summary>
    public static bool IsTalentPassive(ToxEntry tox) =>
        string.IsNullOrEmpty(tox.Image) && tox.InnateOnCards == 0 && tox.ShopPool is null && !ShopPrices.ContainsKey(tox.Title);

    /// <summary>Prix à la boutique en Spamtons ; null si le Tox n'y est pas vendu.</summary>
    public static int? ShopPrice(ToxEntry tox) =>
        ShopPrices.TryGetValue(tox.Title, out var price) ? price : tox.InShop ? tox.Cost : null;

    /// <summary>« 1 Spamton », « 45 Spamtons », « 1 000 Spamtons » : le pluriel dès 2.</summary>
    public static string PriceText(int amount) =>
        $"{amount.ToString("N0", CultureInfo.GetCultureInfo("fr-FR"))} {Currency}{(amount > 1 ? "s" : "")}";
}
