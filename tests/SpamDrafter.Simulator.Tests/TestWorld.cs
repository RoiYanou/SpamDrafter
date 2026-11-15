using System.Text.Json;
using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;

namespace SpamDrafter.Simulator.Tests;

/// <summary>
/// Petit monde d'essai construit en mémoire : cartes et capacités inventées POUR LES TESTS,
/// avec les briques (effets, conditions, coûts) et les réglages réels de SPAM 1.30.
/// Ainsi chaque règle se teste isolément, sans dépendre des données du jeu (absentes du dépôt public).
/// </summary>
public sealed class TestWorld
{
    /// <summary>Réglages réels de SPAM 1.30 (vérifiés contre settings.json par RealDataTests).</summary>
    public static DuelSettings Settings130 => new(
        StartLuck: 0, LuckPerTurn: 1, TurnAfterOvertime: 20, OvertimeReducingRatio: 0.1,
        OvertimeScalingRatio: 0.5, GoldPerKill: 50, FrozenBrokenByPoison: true, FrozenBrokenByHeal: true,
        PlayerIdFirstPlayer: 0, CardsPerDeck: 4,
        RandomElements: ["nature", "earth", "fire", "light", "shadow", "water"], EchoSmoothing: 0.5,
        FrozenBrokenByToxEquip: true, FrozenOwnerTurns: 1, MysteryPool: ["mystere_pick"], HeartPool: [],
        Shop: new ShopSettings(
            // Boutique d'essai : seulement des communs (courbe « commun » à 100, les autres à 0).
            Pools: [["shop_brut", "shop_bouclier", "shop_tenacite", "shop_brut2"], ["shop_rare"], ["shop_rare"], ["shop_rare"]],
            RarityCurves: [[new CurveKey(0, 100, 0, 0)], [new CurveKey(0, 0, 0, 0)], [new CurveKey(0, 0, 0, 0)], [new CurveKey(0, 0, 0, 0)]],
            RerollCost: 10, ItemShopMax: 3, LuckForSkip: 0, SkipPerCardAlive: [10, 10, 10, 10], BanPerPlayer: 1));

    private readonly List<CardDef> _cards = new();
    private readonly List<AbilityDef> _abilities = new();
    public DuelSettings Settings { get; set; } = Settings130;

    private static BrickDef Brick(string id, string cls, object parameters) =>
        new(id, cls, JsonSerializer.SerializeToElement(parameters).EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone()));

    // Briques telles qu'elles existent dans les données 1.30 (mêmes identifiants, classes et paramètres).
    private static readonly BrickDef[] Effects =
    [
        Brick("damage", "EffectDamage", new { mustUseElement = 1, useCasterSpamton = 0 }),
        Brick("DamageAll", "EffectDamageAll", new { }),
        Brick("healTarget", "EffectHeal", new { targetType = 20, mustUseElement = 1 }),
        Brick("AddAllPlayerAttack", "EffectAddAllStat",
            new { StatToModify = 1, targetToModify = 1, valueForEffect = 1, randomTeam = 0, ratioNumberActivation = 0, useCardTypeForDamage = 0 }),
        Brick("AddCooldown", "EffectAddCooldown", new { type = 0, targetToModify = 20, resetAbilityOnly = 0 }),
        Brick("_NULL", "_EffectNULL", new { }),
        Brick("healCaster", "EffectHeal", new { targetType = 30, mustUseElement = 1 }),
        Brick("AddAllPlayerCritick", "EffectAddAllStat",
            new { StatToModify = 25, targetToModify = 1, valueForEffect = 1, randomTeam = 0, ratioNumberActivation = 0, useCardTypeForDamage = 0 }),
        Brick("AddAllPlayerAttackCountActivation1", "EffectAddAllStat",
            new { StatToModify = 1, targetToModify = 1, valueForEffect = 1, randomTeam = 0, ratioNumberActivation = 1, useCardTypeForDamage = 0 }),
        Brick("AddAllPlayerHPMax", "EffectAddAllStat",
            new { StatToModify = 100, targetToModify = 1, valueForEffect = 1, randomTeam = 0, ratioNumberActivation = 0, useCardTypeForDamage = 0 }),
        Brick("AddAllPlayerResistance", "EffectAddAllStat",
            new { StatToModify = 120, targetToModify = 1, valueForEffect = 1, randomTeam = 0, ratioNumberActivation = 0, useCardTypeForDamage = 0 }),
        Brick("AddCardCriticChance", "EffectAddStat", new { StatToModify = 25, targetToModify = 1, valueForEffect = 1, teamCondition = 1, transferToCaster = 0 }),
        Brick("AddCardBaseDamage", "EffectAddStat", new { StatToModify = 92, targetToModify = 1, valueForEffect = 1, teamCondition = 1, transferToCaster = 0 }),
        Brick("AddAbilityBaseDamage", "EffectAddStat", new { StatToModify = 92, targetToModify = 1, valueForEffect = 1, teamCondition = 25, transferToCaster = 0 }),
        Brick("AddOppCardMiss", "EffectAddStat", new { StatToModify = 70, targetToModify = 10, valueForEffect = 1, teamCondition = 1, transferToCaster = 0 }),
        Brick("AddOppCardCriticRatio", "EffectAddStat", new { StatToModify = 40, targetToModify = 10, valueForEffect = 1, teamCondition = 1, transferToCaster = 0 }),
        Brick("AddOppCardResistance", "EffectAddStat", new { StatToModify = 120, targetToModify = 10, valueForEffect = 1, teamCondition = 1, transferToCaster = 0 }),
        Brick("AddCardHPMax", "EffectAddStat", new { StatToModify = 100, targetToModify = 1, valueForEffect = 1, teamCondition = 1, transferToCaster = 0 }),
        Brick("StealOppHPMax", "EffectAddStat", new { StatToModify = 100, targetToModify = 10, valueForEffect = 1, teamCondition = 1, transferToCaster = 1 }),
        Brick("AddOppCardBaseDamageCountActivation1", "EffectAddStatPerLaunch",
            new { StatToModify = 92, targetToModify = 10, valueForEffect = 1, teamCondition = 1, transferToCaster = 0, ratioNumberActivation = 1 }),
        Brick("SetHPInPercent", "EffectSetHP", new { }),
        Brick("healPVmaxTarget", "EffectHealPVMAX", new { targetType = 20 }),
        Brick("HealCasterWithOppHPMAX", "EffectHealCasterWithTargetMaxHP", new { }),
        Brick("destroy", "EffectDestroy", new { }),
        Brick("AddSpamtonGenerationCaster", "EffectAddSpamtonGeneration", new { targetToModify = 1 }),
        Brick("ResetSpamton", "EffectAddSpamton", new { mustReset = 1 }),
        Brick("DamageCasterHP", "EffectDamageCasterHP", new { }),
        Brick("DamageCasterSpamton", "EffectDamage", new { mustUseElement = 1, useCasterSpamton = 1 }),
        Brick("DamageWithCardElement", "EffectDamageWithCardElement", new { }),
        Brick("DamageOpponentHPMAX", "EffectDamageOpponentHPmax", new { }),
        Brick("DamageOnShield2", "EffectDamageOnShield", new { ratioOnShield = 2 }),
        Brick("StealMaxHPWithDamageTarget", "EffectStealMaxHPWithDamage", new { targetType = 20 }),
        Brick("ChangeElementTargetRandom", "ChangeCardElement", new { targetToModify = 20 }),
        Brick("StealElement", "EffectStealElement", new { type = 1 }),
        Brick("RemoveTauntRunning", "EffectRemoveStatusRunning", new { statusToRemove = "tox:" + TauntTox }),
        Brick("Resurection", "EffectResurrection", new { }),
        Brick("ResetCooldownTarget", "EffectAddCooldown", new { type = 1, targetToModify = 20, resetAbilityOnly = 0 }),
        Brick("AddCardResistance", "EffectAddStat", new { StatToModify = 120, targetToModify = 1, valueForEffect = 1, teamCondition = 1, transferToCaster = 0 }),
        Brick("AddSpamton", "EffectAddSpamton", new { mustReset = 0 }),
        Brick("AddLuck", "EffectAddLuck", new { }),
        Brick("ApplyRevanche", "EffectApplyRevanche", new { }),
        Brick("MultiplyAllHP_x2", "EffectMultiplyAllHP", new { }),
        Brick("SetSpamton", "EffectSetSpamton", new { }),
        Brick("StealSpamtonTaken", "StealSpamton", new { onlyWhatTargetHas = 1 }),
        Brick("ReplaceLastByBreath", "EffectReplaceLastAbility", new { gain_ability = "ability:Souffle" }),
        Brick("ReplaceLastWithRandomCD8", "EffectReplaceLastWithRandomAbility", new { poolCooldown = -1 }),
        Brick("AddSResonance", "EffectAddTalentStatus", new { modificatorToAdd = "tox:resonance" }),
        Brick("AddSChanceVitale", "EffectAddTalentStatus", new { modificatorToAdd = "tox:chancevitale" }),
        Brick("AddSMeta", "EffectAddPassive", new { modificatorToAdd = "tox:MetamorphSimple_0__rarity-rare" }),
        Brick("AddSBouclier", "EffectAddPassive", new { modificatorToAdd = "tox:shop_bouclier" }),
        Brick("ReplaceLastWithTenacite", "EffectReplaceLastWithPassive", new { gain_passive = "tox:shop_tenacite" }),
        Brick("EffectEvolution", "EffectEvolution", new
        {
            evolutionData = new object[]
            {
                new { palierValue = 25, rarity = "rarity:rarity-legendary" }, new { palierValue = 50, rarity = "rarity:rarity-epic" },
                new { palierValue = 75, rarity = "rarity:rarity-rare" }, new { palierValue = 100, rarity = "rarity:rarity-common" },
            },
        }),
        Brick("AddSCasino", "EffectAddTalentStatus", new { modificatorToAdd = "tox:casino" }),
        Brick("AddSPetitPrix", "EffectAddTalentStatus", new { modificatorToAdd = "tox:petitprix" }),
        Brick("effet_v2", "EffectClasseInconnue", new { }),  // classe inexistante : jamais simulée
    ];

    /// <summary>Tox « Taunt_R » de la 1.30 (statut Protection = Provocation).</summary>
    public const string TauntTox = "fd1a6b2b-1cc8-4949-b75d-4cef66683519";

    private readonly List<ToxDef> _extraTox = new();

    /// <summary>Tox d'essai (type StatusType du jeu, valeur, élément).</summary>
    public ToxDef Tox(string type, double value = 0, string? element = null, string rarity = "rarity-rare", string? uuid = null)
    {
        var t = new ToxDef(uuid ?? $"{type}_{value}_{element}_{rarity}", type, type, value, element, rarity, 50, null);
        _extraTox.Add(t);
        return t;
    }

    private static readonly ToxDef[] Toxes =
    [
        new("mystere_pick", "Berserk_R", "Berserk", 25, null, "rarity-rare", 0, null),
        new("shop_brut", "Brut_R", "Brut", 0, null, "rarity-common", 20, null),
        new("shop_brut2", "Brut_X", "Brut", 0, null, "rarity-rare", 30, null),
        new("shop_bouclier", "Shield_C", "Shield", 25, null, "rarity-common", 15, null),
        new("shop_tenacite", "Tenacite_C", "Tenacite", 0, null, "rarity-common", 5, null),
        new("shop_rare", "Optimisation_R", "Optimisation", 0, null, "rarity-rare", 100, null),
        new("casino", "Casino", "Casino", 0, null, null, 0, null),
        new("petitprix", "PetitPrix", "PetitPrix", 0, null, null, 0, null),
        new(TauntTox, "Taunt_R", "Protection", 0, null, "rarity-rare", 0, null),
        // Statuts de talent (valeurs réelles 1.30 : Résonance 5, Chance vitale 10).
        new("resonance", "Resonance", "Resonance", 5, null, null, 0, null),
        new("chancevitale", "ChanceVitale", "ChanceVitale", 10, null, null, 0, null),
    ];

    private static readonly BrickDef[] Conditions =
    [
        Brick("is_not_empty", "ConditionSlotEmpty", new { oper = 1 }),
        Brick("is_enemy", "ConditionOwner", new { oper = 1 }),
        Brick("is_ally", "ConditionOwner", new { oper = 0 }),
        Brick("ai_is_enemy", "ConditionOwnerAI", new { oper = 1 }),
        Brick("was_critick", "ConditionWasCritick", new { oper = 0, previousHitOnly = 0 }),
        Brick("isCasterHPBelow50Percent", "IsHPMatch", new { targetType = 1, isInPercent = 1, percentHPThreshold = 50, oper = 3, value = 0 }),
        Brick("ConditionTurn=6", "ConditionTurnNumber", new { oper = 0, value = 6 }),
        Brick("HasSpamton100", "HasAmountSpamton", new { value = 100 }),
        Brick("HasTaunt", "HasPassiveRunning", new { passiveToTest = "tox:" + TauntTox }),
        Brick("isCasterHPBelow25Percent", "IsHPMatch", new { targetType = 1, isInPercent = 1, percentHPThreshold = 25, oper = 3, value = 0 }),
        Brick("ConditionDrawnTurn1-20", "ConditionDrawnTurn", new { firstTurn = 1, lastTurn = 20 }),
        Brick("hasNoPhoenix", "ConditionModificatorStatus", new { has_status = 185, oper = 1 }),
    ];

    private static readonly BrickDef[] Costs =
    [
        Brick("CostElementFire", "CostDataElement", new { team = "element:fire", oper = 2 }),
        Brick("CostHP", "CostDataHP", new { oper = 2 }),
    ];

    private static readonly BrickDef[] Filters = [Brick("filter_random_1", "FilterRandom", new { amount = 1 })];

    public static ActionDef Act(string effect, double min, double max = double.NaN, string? element = "fire",
        string? condition = null, bool useMiss = true) =>
        new(effect, min, double.IsNaN(max) ? min : max, element, condition, useMiss);

    public static ActionDef Line(string effect, double value, string element, double percentHp) =>
        new(effect, value, value, element, null, false, percentHp);

    private readonly List<AbilityDef> _talents = new();

    /// <summary>Talent d'essai (actions sans jet de raté, comme tous les talents de la 1.30).</summary>
    public AbilityDef Talent(string id, string trigger, string target, ActionDef[] actions, double chanceOfMiss = 0,
        bool smooth = true, int activations = 0, string[]? conditions = null, string[]? triggerConditions = null)
    {
        actions = actions.Select(a => a with { UseMissRoll = false }).ToArray();
        var t = new AbilityDef(id, "fire", trigger, target, triggerConditions ?? [], conditions ?? [], [], [], actions,
            chanceOfMiss, 0, 2, 0, activations, false, 0, [], [], [], IsTalent: true, SmoothChance: smooth);
        _talents.Add(t);
        return t;
    }

    public AbilityDef Ability(string id, string target, ActionDef[] actions, int cooldown = 1,
        double miss = 0, double crit = 0, double critRatio = 2, string[]? conditions = null,
        CostDef[]? costs = null, string[]? filters = null, int activations = 0, string[]? chain = null,
        double echo = 0, string element = "fire", string[]? triggerConditions = null, bool dropsPerCardGone = false,
        ToxDef[]? passives = null, int duration = 0, bool freezeOnlyOnCrit = false, bool choosesSlot = false)
    {
        var a = new AbilityDef(id, element, "Activate", target, triggerConditions ?? [], conditions ?? ["is_not_empty"],
            filters ?? [], costs ?? [], actions, miss, crit, critRatio, cooldown, activations, false, echo,
            chain ?? [], [], (passives ?? []).Select(t => t.Uuid).ToList(), duration, dropsPerCardGone, freezeOnlyOnCrit, choosesSlot);
        _abilities.Add(a);
        return a;
    }

    /// <summary>Capacité « frapper » : 100 dégâts de feu sur une cible choisie, sans raté ni critique.</summary>
    public AbilityDef Strike(string id = "Frapper", double value = 100, int cooldown = 1, double crit = 0) =>
        Ability(id, "SelectTarget", [Act("damage", value)], cooldown, crit: crit);

    public CardDef Card(string id, int hp = 1000, string element = "fire", int income = 10, int resist = 0,
        AbilityDef[]? talents = null, ToxDef[]? innate = null, string rarity = "rarity-common", params AbilityDef[] abilities)
    {
        var c = new CardDef(id, id, "Card.Name." + id, element, rarity, hp, income,
            new[] { "fire", "water", "earth", "nature", "shadow", "light" }.ToDictionary(e => e, _ => resist),
            abilities.Select(a => a.Id).ToList(), (innate ?? []).Select(t => t.Uuid).ToList(),
            (talents ?? []).Select(t => t.Id).ToList());
        _cards.Add(c);
        return c;
    }

    public SimKnowledge Build() =>
        new("test", Settings, _cards, _abilities, Effects, Conditions, Costs, Filters, null,
            Toxes.Concat(_extraTox).DistinctBy(t => t.Uuid), _talents);

    /// <summary>Crée et lance un duel (J1 commence, réglage playerIDFirstPlayer = 0 du monde d'essai).</summary>
    public Duel Duel(CardDef[] deck0, CardDef[] deck1, ulong seed = 1, bool start = true)
    {
        var d = new Duel(Build(), deck0.Select(c => new DuelSlot(c.Id)).ToList(),
            deck1.Select(c => new DuelSlot(c.Id)).ToList(), seed);
        if (start) d.Start();
        return d;
    }

    /// <summary>Crée et lance un duel avec des talents choisis (null = aucun).</summary>
    public Duel Duel((CardDef card, AbilityDef? talent)[] deck0, (CardDef card, AbilityDef? talent)[] deck1, ulong seed = 1)
    {
        var d = new Duel(Build(), deck0.Select(x => new DuelSlot(x.card.Id, x.talent?.Id)).ToList(),
            deck1.Select(x => new DuelSlot(x.card.Id, x.talent?.Id)).ToList(), seed);
        d.Start();
        return d;
    }
}
