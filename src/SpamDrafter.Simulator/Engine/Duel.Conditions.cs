using SpamDrafter.Simulator.Data;

namespace SpamDrafter.Simulator.Engine;

// Conditions des capacités. Chaque classe du jeu est réécrite d'après son comportement (D-012).
// Une classe inconnue rend la condition fausse (on n'invente rien) et est signalée par IsConditionSupported.
public sealed partial class Duel
{
    /// <summary>Codes de StatusType du jeu (🔒 StatusType.cs) correspondant aux statuts de la v1.</summary>
    private static readonly Dictionary<int, StatusKind> GameStatusCodes = new()
    {
        [180] = StatusKind.Paralysed,
        [190] = StatusKind.Poisoned,
        [200] = StatusKind.Taunt,       // « Protection » = Provocation
        [270] = StatusKind.Shield,
        [280] = StatusKind.Silenced,
        [285] = StatusKind.Frozen,
        [340] = StatusKind.Stealth,
        [290] = StatusKind.Sleep,
        [300] = StatusKind.Smoked,
        [473] = StatusKind.Revealed,
    };

    /// <summary>Codes StatusType (🔒 StatusType.cs) des Tox, pour « la carte a un Tox de ce type ».</summary>
    private static readonly Dictionary<int, string> ToxStatusCodes = new()
    {
        [5] = "Aoe", [10] = "Berserk", [20] = "Brut", [21] = "Bomba", [25] = "Chance", [30] = "Cimetierre",
        [40] = "CoeurElement", [50] = "CoeurVital", [51] = "CoeurResistance", [55] = "ContreCoup", [70] = "Disparition",
        [80] = "Erosion", [90] = "Eternity", [110] = "ImunityStatus", [115] = "Inversement", [130] = "LifeSteal",
        [140] = "Mimetisme", [150] = "Multiplicator", [160] = "Mystere", [170] = "Optimisation", [185] = "Phoenix",
        [190] = "Poisoned", [200] = "Protection", [220] = "Renvoie", [230] = "Reset", [240] = "Revanche",
        [260] = "SacDeFrappe", [270] = "Shield", [280] = "Silenced", [285] = "Frozen", [300] = "Smoked",
        [310] = "SoinPrimitif", [320] = "SpamLand", [350] = "Temporality", [360] = "Tenacite", [380] = "Vengeur",
        [445] = "SolCamouflage",
    };

    private static readonly HashSet<string> SupportedConditionClasses = new()
    {
        "ConditionSlotEmpty", "ConditionOwnerAI", "ConditionOwner", "ConditionSelf", "ConditionStatus",
        "ConditionWasCritick", "ConditionWasMiss", "HasFullHP", "IsNotCardElement",
        "IsHPMatch", "ConditionTurnNumber", "HasAmountSpamton", "HasPassiveRunning",
        "ConditionDrawnTurn", "ConditionModificatorStatus",
    };

    public bool IsConditionSupported(string id) =>
        _kb.Condition(id) is { } c && SupportedConditionClasses.Contains(c.Class);

    /// <summary>
    /// 🔒 AbilityData.AreTriggerConditionsMet : chaque condition de déclenchement doit être vraie
    /// « au déclenchement » ET « comme condition de cible » sur la carte qui déclenche (le lanceur par défaut) ;
    /// le coût doit aussi être payable.
    /// </summary>
    private bool TriggerConditionsMet(CardState caster, AbilityDef def, TalentState? talent = null, CardState? triggerCard = null) =>
        def.ConditionsTrigger.All(id => TriggerPart(id, caster, talent) && ConditionOnCard(id, caster, def, triggerCard ?? caster)) &&
        CostsMet(caster, def);

    /// <summary>Partie « au déclenchement » : seules trois classes en ont une ; les autres valent « vrai ».</summary>
    private bool TriggerPart(string id, CardState caster, TalentState? talent) => _kb.Condition(id) switch
    {
        null => false,
        // 🟡 Le jeu dérive ce tour d'un identifiant tiré au hasard : tour tiré uniformément à l'attachement.
        { Class: "ConditionDrawnTurn" } => talent is not null && talent.DrawnTurn == Round,
        { Class: "IsNotCardElement" } c => caster.Element != c.Link("elementToTest"),
        { Class: "ConditionTurnNumber" } c => CompareInt(Round, c.Int("oper"), c.Int("value")),
        { Class: var cls } => SupportedConditionClasses.Contains(cls),
    };

    private bool TargetConditionsMet(CardState caster, AbilityDef def, CardState target) =>
        def.ConditionsTarget.All(id => ConditionOnCard(id, caster, def, target));

    private bool ActionConditionMet(CardState caster, AbilityDef def, ActionDef action, CardState? card, PlayerState? player)
    {
        if (string.IsNullOrEmpty(action.Condition)) return true;
        return card is not null ? ConditionOnCard(action.Condition, caster, def, card)
                                : ConditionOnPlayer(action.Condition, caster, player!, def);
    }

    /// <summary>« oper » booléen du jeu : 0 = est vrai, 1 = est faux.</summary>
    private static bool CompareBool(bool condition, int oper) => oper == 1 ? !condition : condition;

    /// <summary>🔒 « was_critick » / « was_notMiss » lisent la mémoire de la capacité (partagée avec ses « Echo… »).</summary>
    private static bool WasCrit(CardState caster, AbilityDef def, bool previousHitOnly) =>
        caster.Runtime(def.Id) is { } rt && (previousHitOnly ? rt.LastHitCrit : rt.WasCrit);

    private static bool WasMiss(CardState caster, AbilityDef def) => caster.Runtime(def.Id)?.WasMiss ?? false;

    /// <summary>🔒 IsHPMatch : PV / PV max (en % si demandé) comparés au seuil.</summary>
    private static bool HpMatch(BrickDef c, CardState card)
    {
        var ratio = card.HpMax == 0 ? 0 : (float)card.Hp / card.HpMax * (c.Bool("isInPercent") ? 100f : 1f);
        var threshold = c.Parameters.TryGetValue("percentHPThreshold", out var t) ? t.GetDouble() : 0;
        return CompareFloat(ratio, c.Int("oper"), threshold);
    }

    private bool ConditionOnCard(string id, CardState caster, AbilityDef def, CardState target)
    {
        var c = _kb.Condition(id);
        if (c is null) return false;
        var oper = c.Int("oper");
        return c.Class switch
        {
            "ConditionSlotEmpty" => CompareBool(false, oper),           // une carte n'est jamais un emplacement vide
            "ConditionOwnerAI" => !Players[caster.Owner].IsAi || CompareBool(caster.Owner == target.Owner, oper),
            "ConditionOwner" => CompareBool(caster.Owner == target.Owner, oper),
            "ConditionSelf" => CompareBool(caster == target, oper),
            "ConditionStatus" => CompareBool(
                GameStatusCodes.TryGetValue(c.Int("has_status"), out var k) && target.Has(k) &&
                target.StatusValue(k) >= c.Int("value"), oper),
            "ConditionWasCritick" => CompareBool(WasCrit(caster, def, c.Bool("previousHitOnly")), oper),
            "ConditionWasMiss" => CompareBool(WasMiss(caster, def), oper),
            "HasFullHP" => CompareBool(c.Int("targetType") == 30 ? caster.Hp == caster.HpMax : target.Hp == target.HpMax, oper),
            "IsNotCardElement" => caster.Element != c.Link("elementToTest"),
            "IsHPMatch" => HpMatch(c, c.Int("targetType") == 1 ? caster : target),
            "ConditionTurnNumber" => true,                              // condition de déclenchement seulement
            "HasAmountSpamton" => Players[caster.Owner].Spamtons >= c.Int("value"),
            "HasPassiveRunning" => StatusOfTox(c.Link("passiveToTest")) is { } k2 && target.Has(k2),
            "ConditionModificatorStatus" => CompareBool(
                ToxStatusCodes.TryGetValue(c.Int("has_status"), out var toxType) && target.HasTox(toxType), oper),
            "ConditionDrawnTurn" => true,
            _ => false,
        };
    }

    private bool ConditionOnPlayer(string id, CardState caster, PlayerState target, AbilityDef? def = null)
    {
        var c = _kb.Condition(id);
        if (c is null) return false;
        var oper = c.Int("oper");
        return c.Class switch
        {
            "ConditionSlotEmpty" => CompareBool(false, oper),
            "ConditionOwnerAI" => !Players[caster.Owner].IsAi || CompareBool(caster.Owner == target.Id, oper),
            "ConditionOwner" => CompareBool(caster.Owner == target.Id, oper),
            "ConditionSelf" => CompareBool(caster.Owner == target.Id, oper),
            "ConditionStatus" => CompareBool(false, oper),              // v1 : pas de statut de joueur
            "ConditionWasCritick" => def is not null && CompareBool(WasCrit(caster, def, c.Bool("previousHitOnly")), oper),
            "ConditionWasMiss" => def is not null && CompareBool(WasMiss(caster, def), oper),
            "HasFullHP" => true,                                        // pas de version « joueur » dans le jeu
            "IsNotCardElement" => caster.Element != c.Link("elementToTest"),
            "IsHPMatch" => c.Int("targetType") == 1 && HpMatch(c, caster),
            "ConditionTurnNumber" or "HasAmountSpamton" or "HasPassiveRunning" or "ConditionDrawnTurn"
                or "ConditionModificatorStatus" => true, // pas de version « joueur »
            _ => false,
        };
    }
}
