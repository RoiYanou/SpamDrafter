using System.Text.Json;

namespace SpamDrafter.Simulator.Data;

// Données du duel, lues dans la base de connaissances (knowledge/<version>/).
// Ce sont des copies en lecture seule : le simulateur ne modifie jamais ces objets.

/// <summary>Une action d'une capacité (un effet, ses valeurs min/max, son élément).</summary>
public sealed record ActionDef(
    string Effect,
    double Min,
    double Max,
    string? Element,          // null = élément tiré au hasard (🔒 GetComputedValue)
    string? Condition,
    bool UseMissRoll,
    double PercentHpToProc = -1);   // seuil de PV (%) pour les talents « changement de palier », −1 = aucun

/// <summary>Un coût de capacité (identifiant du coût et valeur).</summary>
public sealed record CostDef(string Cost, int Value);

/// <summary>Capacité d'une carte (extrait de abilities.json).</summary>
public sealed record AbilityDef(
    string Id,
    string? Element,
    string Trigger,
    string Target,
    IReadOnlyList<string> ConditionsTrigger,
    IReadOnlyList<string> ConditionsTarget,
    IReadOnlyList<string> FiltersTarget,
    IReadOnlyList<CostDef> Costs,
    IReadOnlyList<ActionDef> Actions,
    double ChanceOfMiss,
    double ChanceOfCrit,
    double CritDamageRatio,
    int Cooldown,
    int NumberOfActivation,
    bool Exhaust,
    double ChanceOfResetCooldown,
    IReadOnlyList<string> ChainAbilities,
    IReadOnlyList<string> ComboAbilities,
    IReadOnlyList<string> Passives,
    int Duration = 0,
    bool CooldownDropsPerCardGone = false,
    bool FreezeOnlyOnCrit = false,
    bool ChoosesSlot = false,
    bool IsTalent = false,
    bool SmoothChance = true,
    bool DecouverteEnable = false)
{
    public bool IsActive => Trigger == "Activate";
}

/// <summary>Tox, passif ou statut de talent (extrait de tox.json).</summary>
public sealed record ToxDef(
    string Uuid,
    string AssetName,
    string StatusType,
    double Value,
    string? Element,
    string? Rarity,
    int Cost,
    string? ShopPool,
    bool InMysteryPool = false,
    bool InHeartPool = false);

/// <summary>Brique générique (effet, condition, coût, filtre) : classe C# du jeu + paramètres bruts.</summary>
public sealed record BrickDef(string Id, string Class, IReadOnlyDictionary<string, JsonElement> Parameters)
{
    // Vitesse (phase 8) : les nombres sont lus une fois dans le JSON, puis gardés (avant : relus à chaque appel).
    private Dictionary<string, double>? _numbers;
    private Dictionary<string, double> Numbers => _numbers ??= Parameters
        .Where(kv => kv.Value.ValueKind == JsonValueKind.Number).ToDictionary(kv => kv.Key, kv => kv.Value.GetDouble());

    public int Int(string name, int fallback = 0) =>
        Numbers.TryGetValue(name, out var v) ? (int)v : fallback;

    public bool Bool(string name) => Int(name) != 0;

    /// <summary>Lien « type:id » ramené à l'id (ex. « element:fire » → « fire »). Null si absent.</summary>
    public string? Link(string name) =>
        Parameters.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!.Split(':', 2)[^1]
            : null;
}

/// <summary>Carte jouable (extrait de cards.json).</summary>
public sealed record CardDef(
    string Id,
    string SanitizedName,
    string NameKey,
    string Element,
    string Rarity,
    int Hp,
    int GoldIncome,
    IReadOnlyDictionary<string, int> Resistances,
    IReadOnlyList<string> Abilities,
    IReadOnlyList<string> InnatePassives,
    IReadOnlyList<string> Talents);

/// <summary>
/// Réglages du duel lus dans settings.json (GameplayData). Aucun réglage n'est codé en dur (règle n° 9) :
/// une clé absente provoque une erreur au chargement.
/// </summary>
public sealed record DuelSettings(
    int StartLuck,
    double LuckPerTurn,
    int TurnAfterOvertime,
    double OvertimeReducingRatio,
    double OvertimeScalingRatio,
    int GoldPerKill,
    bool FrozenBrokenByPoison,
    bool FrozenBrokenByHeal,
    int PlayerIdFirstPlayer,
    int CardsPerDeck,
    IReadOnlyList<string> RandomElements,
    double EchoSmoothing,
    bool FrozenBrokenByToxEquip,
    int FrozenOwnerTurns,
    IReadOnlyList<string> MysteryPool,      // uuid des Tox du « Mystère »
    IReadOnlyList<string> HeartPool,        // uuid des Tox « cœur » (Cœur corrompu)
    ShopSettings Shop);

/// <summary>Point d'une courbe d'animation Unity (temps, valeur, tangentes).</summary>
public sealed record CurveKey(float Time, float Value, float InTangent, float OutTangent);

/// <summary>
/// Réglages de la boutique (📊 GameplayData) : listes de Tox par rareté (commun, rare, épique, légendaire),
/// courbes de rareté selon la chance, prix de relance, nombre de Tox, récompense pour passer, bannissements.
/// </summary>
public sealed record ShopSettings(
    IReadOnlyList<IReadOnlyList<string>> Pools,
    IReadOnlyList<IReadOnlyList<CurveKey>> RarityCurves,
    int RerollCost,
    int ItemShopMax,
    double LuckForSkip,
    IReadOnlyList<int> SkipPerCardAlive,
    int BanPerPlayer);
