using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using static SpamDrafter.Simulator.Tests.TestWorld;

namespace SpamDrafter.Simulator.Tests;

/// <summary>
/// Preuve de fin de phase 6 (feuille de route) : « 100 % des effets utilisés couverts par des tests ».
/// 1. Chaque classe d'effet utilisée par SPAM 1.30 existe dans le monde d'essai (et a ses tests de règle dans RuleTests, LotA à LotD).
/// 2. Chaque effet du monde d'essai est réellement exécuté par le moteur (sur une carte ou sur un joueur).
/// </summary>
public class CoverageTests
{
    /// <summary>Les 34 classes d'effets utilisées par les capacités et talents atteignables de SPAM 1.30 (🔒 effects.json, used = true).</summary>
    public static readonly string[] UsedEffectClasses =
    [
        "EffectAddPassive", "EffectAddStat", "EffectAddTalentStatus", "EffectAddAllStat", "EffectHeal", "EffectAddCooldown",
        "EffectAddSpamton", "EffectDamage", "EffectReplaceLastWithPassive", "EffectHealPVMAX", "EffectAddLuck",
        "EffectAddStatPerLaunch", "EffectAddSpamtonGeneration", "EffectApplyRevanche", "ChangeCardElement", "EffectDamageAll",
        "EffectDamageCasterHP", "EffectDamageOnShield", "EffectDamageOpponentHPmax", "EffectDamageWithCardElement",
        "EffectEvolution", "EffectHealCasterWithTargetMaxHP", "EffectMultiplyAllHP", "EffectRemoveStatusRunning",
        "EffectReplaceLastAbility", "EffectReplaceLastWithRandomAbility", "EffectResurrection", "EffectSetHP",
        "EffectSetSpamton", "EffectStealElement", "EffectStealMaxHPWithDamage", "StealSpamton", "_EffectNULL", "EffectDestroy",
    ];

    [Fact]
    public void Il_y_a_bien_34_classes()
    {
        Assert.Equal(34, UsedEffectClasses.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Classes))]
    public void Chaque_classe_utilisee_est_executee_par_le_moteur(string effectClass)
    {
        var w = new TestWorld();
        var kb = w.Build();
        var bricks = EffectIdsOfClass(kb, effectClass);
        Assert.True(bricks.Count > 0, $"aucun effet d'essai de la classe {effectClass}");
        var applied = new HashSet<string>();
        foreach (var id in bricks)
            foreach (var target in new[] { "SelectTarget", "PlayerOpponent", "PlayerSelf" })
                applied.UnionWith(Run(id, target));
        Assert.Contains(effectClass, applied);
    }

    public static TheoryData<string> Classes()
    {
        var data = new TheoryData<string>();
        foreach (var c in UsedEffectClasses) data.Add(c);
        return data;
    }

    private static List<string> EffectIdsOfClass(SimKnowledge kb, string cls)
    {
        var ids = new List<string>();
        foreach (var id in AllTestEffectIds)
            if (kb.Effect(id)?.Class == cls) ids.Add(id);
        return ids;
    }

    /// <summary>Identifiants des effets du monde d'essai (voir TestWorld).</summary>
    private static readonly string[] AllTestEffectIds =
    [
        "damage", "DamageAll", "healTarget", "AddAllPlayerAttack", "AddCooldown", "_NULL", "healCaster", "AddAllPlayerCritick",
        "AddAllPlayerAttackCountActivation1", "AddAllPlayerHPMax", "AddAllPlayerResistance", "AddCardCriticChance",
        "AddCardBaseDamage", "AddAbilityBaseDamage", "AddOppCardMiss", "AddOppCardCriticRatio", "AddOppCardResistance",
        "AddCardHPMax", "StealOppHPMax", "AddOppCardBaseDamageCountActivation1", "SetHPInPercent", "healPVmaxTarget",
        "HealCasterWithOppHPMAX", "destroy", "AddSpamtonGenerationCaster", "ResetSpamton", "DamageCasterHP",
        "DamageCasterSpamton", "DamageWithCardElement", "DamageOpponentHPMAX", "DamageOnShield2", "StealMaxHPWithDamageTarget",
        "ChangeElementTargetRandom", "StealElement", "RemoveTauntRunning", "Resurection", "ResetCooldownTarget",
        "AddCardResistance", "AddSpamton", "AddLuck", "ApplyRevanche", "MultiplyAllHP_x2", "SetSpamton", "StealSpamtonTaken",
        "ReplaceLastByBreath", "ReplaceLastWithRandomCD8", "AddSResonance", "AddSChanceVitale", "AddSMeta",
        "ReplaceLastWithTenacite", "EffectEvolution", "AddSCasino", "AddSPetitPrix",
    ];

    /// <summary>Lance une capacité d'essai qui n'a que cet effet, et renvoie les classes appliquées.</summary>
    private static HashSet<string> Run(string effectId, string target)
    {
        var w = new TestWorld();
        w.Ability("Souffle", "SelectTarget", [Act("damage", 10)]);   // pour « remplacer la dernière capacité »
        w.Tox("MetamorphSimple");
        var ab = w.Ability("Essai", target, [Act(effectId, 50)], conditions: ["is_not_empty", "is_enemy"]);
        var me = w.Card("Moi", abilities: ab);
        var duel = w.Duel([me, w.Card("Allie", hp: 10)], [w.Card("Lui"), w.Card("Autre")]);
        var c = duel.Players[0].Board[0];
        if (!duel.CanCast(c, c.Abilities[0])) return [];
        duel.Cast(c, c.Abilities[0], target == "SelectTarget" ? duel.Players[1].Board[0] : null);
        return duel.AppliedEffectClasses;
    }
}
