using SpamDrafter.Simulator.Data;

namespace SpamDrafter.Simulator.Engine;

public enum DamageOrigin { Ability, Poison, Thaw }

// Effets des capacités, calcul des dégâts (§5), soin (§5.4), Gel (§6.2), statistiques.
public sealed partial class Duel
{
    // ================================================================ quels effets sont simulés ?

    /// <summary>Classes d'effets réellement appliquées pendant ce duel (sert à prouver la couverture des tests).</summary>
    public HashSet<string> AppliedEffectClasses { get; } = new();

    /// <summary>Codes « EffectAddAllStatType » du jeu (🔒) simulés.</summary>
    private static readonly HashSet<int> SupportedStats = [1, 25, 40, 70, 80, 92, 100, 110, 120];

    /// <summary>Vrai si l'effet (identifiant de effects.json) est simulé, avec ses paramètres.</summary>
    public bool IsEffectSupported(string effectId)
    {
        var e = _kb.Effect(effectId);
        return e?.Class switch
        {
            "_EffectNULL" or "EffectDamage" or "EffectDamageAll" or "EffectHeal" or "EffectHealPVMAX"
                or "EffectDamageCasterHP" or "EffectDamageWithCardElement" or "EffectDamageOpponentHPmax"
                or "EffectDamageOnShield" or "EffectStealMaxHPWithDamage" or "EffectHealCasterWithTargetMaxHP"
                or "EffectDestroy" or "EffectSetHP" or "EffectAddSpamton" or "EffectAddSpamtonGeneration"
                or "ChangeCardElement" or "EffectStealElement" or "EffectRemoveStatusRunning"
                or "EffectResurrection" or "EffectAddLuck" or "EffectApplyRevanche"
                or "EffectMultiplyAllHP" or "EffectSetSpamton" or "StealSpamton" or "EffectReplaceLastAbility"
                or "EffectReplaceLastWithRandomAbility" or "EffectEvolution" => true,
            "EffectAddPassive" => IsToxSupported(e.Link("modificatorToAdd") ?? "") &&
                                  (e.Link("modificatorToAdd2") is not { } m2 || IsToxSupported(m2)),
            "EffectReplaceLastWithPassive" => IsToxSupported(e.Link("gain_passive") ?? ""),
            "EffectAddAllStat" => SupportedStats.Contains(e.Int("StatToModify")) && e.Int("valueForEffect") == 1,
            "EffectAddStat" or "EffectAddStatPerLaunch" =>
                SupportedStats.Contains(e.Int("StatToModify")) && e.Int("valueForEffect") == 1,
            "EffectAddCooldown" => e.Int("type") == 0 || !e.Bool("resetAbilityOnly"),
            // Statut de talent : simulé si son effet l'est (Casino et Petit Prix agissent sur la boutique : lot D).
            "EffectAddTalentStatus" => IsToxSupported(e.Link("modificatorToAdd") ?? ""),
            _ => false,
        };
    }

    /// <summary>
    /// Vrai si toute la capacité est simulée : ciblage, effets, conditions, coûts, statuts posés (passifs),
    /// et, récursivement, ses capacités enchaînées ou ses options.
    /// </summary>
    public bool IsAbilityFullySupported(AbilityDef def) => UnsupportedParts(def).Count == 0;

    /// <summary>Ce qui n'est pas encore simulé dans une capacité (et ses enchaînements) ; vide = tout est simulé.</summary>
    public List<string> UnsupportedParts(AbilityDef def)
    {
        var result = new List<string>();
        Collect(def, new HashSet<string>());
        return result.Distinct().ToList();

        void Collect(AbilityDef d, HashSet<string> seen)
        {
            if (!seen.Add(d.Id)) return;
            if (!IsTargetModeSupported(d.Target)) result.Add("ciblage " + d.Target);
            foreach (var a in d.Actions)
            {
                if (!IsEffectSupported(a.Effect)) result.Add($"effet {_kb.Effect(a.Effect)?.Class}/{a.Effect}");
                if (a.Condition is not null && !IsConditionSupported(a.Condition)) result.Add("condition " + a.Condition);
            }
            foreach (var c in d.ConditionsTarget.Concat(d.ConditionsTrigger).Where(c => !IsConditionSupported(c)))
                result.Add("condition " + c);
            foreach (var c in d.Costs.Where(c => _kb.Cost(c.Cost)?.Class is not ("CostDataElement" or "CostDataHP")))
                result.Add("coût " + c.Cost);
            foreach (var t in d.Passives.Where(t => !IsToxSupported(t)))
                result.Add("statut posé non simulé " + (_kb.Tox(t)?.StatusType ?? t));
            if (d.ComboAbilities.Count > 0) result.Add("combo");
            foreach (var id in d.ChainAbilities)
            {
                if (_kb.HasAbility(id)) Collect(_kb.Ability(id), seen);
                else result.Add("enchaînement inconnu " + id);
            }
        }
    }

    // ================================================================ application d'un effet sur une carte

    private void Unsupported(string effect) => Log.Add(Round, $"  (effet « {effect} » non simulé)");

    private void ApplyEffectOnCard(Ctx ctx, int i, CardState target)
    {
        var (def, caster, _, _) = ctx;
        var action = def.Actions[i];
        var e = _kb.Effect(action.Effect);
        if (e is null || !IsEffectSupported(action.Effect)) { Unsupported(action.Effect); return; }
        AppliedEffectClasses.Add(e.Class);

        switch (e.Class)
        {
            case "_EffectNULL": break;

            case "EffectDamage":
            {
                var useSpamton = e.Bool("useCasterSpamton");
                var dmg = ComputeDamage(ctx, i, new DamageOptions { MustUseElement = e.Bool("mustUseElement") && !useSpamton, UseCasterSpamton = useSpamton }, out var element);
                DamageCard(caster, target, def, useSpamton ? null : element, dmg);
                break;
            }
            case "EffectDamageCasterHP":
            {
                var dmg = ComputeDamage(ctx, i, new DamageOptions { UseCasterHp = true }, out var element);
                DamageCard(caster, target, def, element, dmg);
                break;
            }
            case "EffectDamageWithCardElement":
            {
                var dmg = ComputeDamage(ctx, i, new DamageOptions { UseCasterElement = true }, out var element);
                DamageCard(caster, target, def, element, dmg);
                break;
            }
            case "EffectDamageOpponentHPmax":
            {
                var dmg = ComputeDamage(ctx, i, new DamageOptions { UseOpponentHpMax = true, MustUseElement = false, TargetCard = target }, out var element);
                DamageCard(caster, target, def, element, dmg);
                break;
            }
            case "EffectDamageOnShield":
            {
                // 🔒 Dégâts × ratioOnShield si la cible provoque (statut « Protection »).
                var dmg = ComputeDamage(ctx, i, new DamageOptions(), out var element);
                DamageCard(caster, target, def, element, dmg * (target.Has(StatusKind.Taunt) ? e.Int("ratioOnShield", 2) : 1));
                break;
            }
            case "EffectStealMaxHPWithDamage":
            {
                DrawValue(action, integer: false, caster);                 // tirage fait par le jeu, puis ignoré
                var dmg = ComputeDamage(ctx, i, new DamageOptions { TargetCard = target }, out var element);
                DamageCard(caster, target, def, element, dmg);
                if (caster.IsAlive) AddHpMax(caster, caster, dmg);
                break;
            }
            case "EffectHeal":
                switch (e.Int("targetType"))
                {
                    case 20: HealWithComputed(ctx, i, target, e); break;     // la cible
                    case 30: HealWithComputed(ctx, i, caster, e); break;     // le lanceur
                    case 40:                                                   // toutes mes cartes
                        foreach (var c in Players[caster.Owner].Board.ToList()) HealWithComputed(ctx, i, c, e);
                        break;
                }
                break;
            case "EffectHealPVMAX":
            {
                // 🔒 Soin = X % des PV max du LANCEUR, sans élément.
                var who = e.Int("targetType") == 1 ? caster : target;
                var value = (int)(DrawValue(action, integer: false, caster) / 100.0 * caster.HpMax);
                HealCard(caster, who, def, null, value);
                break;
            }
            case "EffectHealCasterWithTargetMaxHP":
                // 🔒 Le lanceur gagne les PV max de la cible… sans gagner de PV actuels.
                if (caster.IsAlive && target.HpMax > 0 && !ImmuneTo(target, caster))
                {
                    var gain = target.HpMax;
                    caster.HpMax += gain;
                    caster.Damage += gain;
                    Log.Add(Round, $"  {Name(caster)} gagne {gain} PV max ({caster.Hp}/{caster.HpMax}).");
                }
                break;
            case "EffectDestroy":
                if (ImmuneTo(target, caster)) { Log.Add(Round, $"  {Name(target)} est immunisée."); break; }
                if (target.Hp > 1 && target.HasTox("Tenacite"))
                {
                    // 🔒 Ténacité : la carte reste à 1 PV au lieu d'être détruite.
                    target.Damage = target.HpMax - 1;
                    Log.Add(Round, $"  Ténacité : {Name(target)} reste à 1 PV.");
                    CheckHpPalier(target);
                    break;
                }
                Stats?.HpChange(caster, target, target.Hp, 0);
                target.Damage = target.HpMax + 1;
                target.Killer = caster;
                Log.Add(Round, $"  {Name(target)} est détruite.");
                KillCard(caster, target, def);
                break;
            case "EffectSetHP":
            {
                if (!target.IsAlive) break;
                var r = DrawValue(action, integer: false, caster);
                var hpBefore = target.Hp;
                target.Damage = Math.Max((int)(target.HpMax * (100.0 - r) / 100.0), 0);
                Stats?.HpChange(caster, target, hpBefore, target.Hp);
                Log.Add(Round, $"  Les PV de {Name(target)} passent à {target.Hp}/{target.HpMax}.");
                if (!target.IsAlive) target.Killer ??= caster;
                CheckHpPalier(target);
                break;
            }
            case "EffectAddSpamton":
                AddSpamtonsEffect(e, action, Players[target.Owner], def, caster);
                break;
            case "EffectSetSpamton":
                Players[target.Owner].AddSpamtons((int)DrawValue(action, integer: true, caster) - Players[target.Owner].Spamtons);
                Log.Add(Round, $"  Spamtons de J{target.Owner + 1} fixés à {Players[target.Owner].Spamtons}.");
                break;
            case "StealSpamton":
                StealSpamtons(e, action, Players[caster.Owner], Players[target.Owner]);
                break;
            case "EffectAddLuck":
                AddLuck(action, Players[target.Owner], caster);
                break;
            case "EffectApplyRevanche":
                if (caster.IsAlive) Revanche(caster, target);
                break;
            case "EffectAddPassive":
            {
                // 🔒 EffectAddPassive : X % de chances (valeur max de l'action) de poser le 1ᵉʳ Tox, sinon le 2ᵉ (s'il existe).
                var roll = _rng.Next(0, 100);
                var first = roll <= action.Max;
                var uuid = e.Link("modificatorToAdd2") is { } second ? (first ? e.Link("modificatorToAdd") : second)
                    : first ? e.Link("modificatorToAdd") : null;
                if (uuid is null || _kb.Tox(uuid) is not { } added) break;
                // Emplacement remplacé : celui choisi par le joueur (KI-029), sinon le choix par défaut du jeu.
                if (def.ChoosesSlot && !TakeChosenSlot(target)) FreeSlot(target, def.Id);
                PlaceTox(target, added);
                break;
            }
            case "EffectReplaceLastWithPassive":
            {
                if (_kb.Tox(e.Link("gain_passive") ?? "") is not { } gained || target.HasTox(gained.StatusType)) break;
                FreeSlot(target, null);
                PlaceTox(target, gained);
                break;
            }
            case "EffectEvolution":
                Evolve(target, e);
                break;
            case "EffectAddTalentStatus":
                if (_kb.Tox(e.Link("modificatorToAdd") ?? "") is { } tox)
                {
                    target.TalentStatuses.Add(new TalentStatus(tox.StatusType, tox.Value));
                    Log.Add(Round, $"  {Name(target)} gagne le statut de talent {tox.StatusType} ({tox.Value:0.##}).");
                }
                break;
            case "EffectMultiplyAllHP":
            {
                // 🔒 PV max et dégâts subis multipliés (arrondis) : les PV actuels sont multipliés aussi.
                var r = DrawValue(action, integer: false, caster);
                if (r <= 0) break;
                target.HpMax = RoundToInt(target.HpMax * r);
                target.Damage = RoundToInt(target.Damage * r);
                Log.Add(Round, $"  PV de {Name(target)} × {r:0.##} → {target.Hp}/{target.HpMax}.");
                CheckHpPalier(target);
                break;
            }
            case "EffectReplaceLastAbility":
                if (e.Link("gain_ability") is { } gainId && _kb.HasAbility(gainId)) GainAbility(target, _kb.Ability(gainId));
                break;
            case "EffectReplaceLastWithRandomAbility":
            {
                // 🔒 Une capacité active tirée parmi celles de toutes les cartes jouables (filtre de recharge éventuel).
                var poolCd = e.Int("poolCooldown", -1);
                var pool = _kb.Cards.SelectMany(c => c.Abilities).Distinct().Select(_kb.Ability)
                    .Where(a => a.IsActive && (poolCd < 0 || a.Cooldown == poolCd) && target.Abilities.All(x => x.Def.Id != a.Id))
                    .ToList();
                if (pool.Count > 0) GainAbility(target, pool[_rng.Next(0, pool.Count)], makeAvailable: true);
                break;
            }
            case "EffectResurrection":
                ResurrectCard(ctx, i, target);
                break;
            case "EffectAddSpamtonGeneration":
            {
                var card = e.Int("targetToModify") == 10 ? target : caster;
                card.IncomeBonus = Math.Max(0, card.IncomeBonus + (int)DrawValue(action, integer: true, caster));
                Log.Add(Round, $"  Revenu de {Name(card)} : {card.Income} par tour.");
                break;
            }
            case "ChangeCardElement":
            {
                var card = e.Int("targetToModify") == 20 ? target : caster;
                var fixedElement = e.Link("teamUseForChange");
                string next;
                if (fixedElement is not null) next = fixedElement;
                else do next = RandomElement(); while (next == caster.Element);
                card.Element = next;
                Log.Add(Round, $"  {Name(card)} devient de l'élément {next}.");
                break;
            }
            case "EffectStealElement":
                StealElement(e, action, caster, Players[target.Owner]);
                break;
            case "EffectRemoveStatusRunning":
                if (StatusOfTox(e.Link("statusToRemove")) is { } kind && target.Has(kind))
                {
                    target.Remove(kind);
                    Log.Add(Round, $"  {Name(target)} perd le statut {kind}.");
                }
                break;
            case "EffectAddAllStat":
                AddAllStat(ctx, i, e, e.Int("targetToModify") == 10 ? Opponent(caster.Owner) : Players[caster.Owner]);
                break;
            case "EffectAddStat" or "EffectAddStatPerLaunch":
                AddStatOnCard(ctx, i, e, e.Int("targetToModify") == 1 ? caster : target);
                break;
            case "EffectAddCooldown":
                AddCooldownEffect(ctx, i, e, e.Int("targetToModify") == 20 ? target : caster);
                break;
            default:
                Unsupported(action.Effect);
                break;
        }
    }

    // ================================================================ application d'un effet sur un joueur

    private void ApplyEffectOnPlayer(Ctx ctx, int i, PlayerState target)
    {
        var (def, caster, _, _) = ctx;
        var action = def.Actions[i];
        var e = _kb.Effect(action.Effect);
        if (e is null || !IsEffectSupported(action.Effect)) { Unsupported(action.Effect); return; }
        AppliedEffectClasses.Add(e.Class);

        switch (e.Class)
        {
            case "EffectDamageAll":
                // 🔒 EffectDamageAll : un jet de raté et un calcul par carte du joueur visé, puis les morts.
                foreach (var c in target.Board.ToList())
                {
                    if (MissRoll(ctx, action)) { Log.Add(Round, $"  Raté sur {Name(c)}."); continue; }
                    var dmg = ComputeDamage(ctx, i, new DamageOptions(), out var element);
                    DamageCard(caster, c, def, element, dmg, canProcDeath: false);
                    c.Remove(StatusKind.Smoked);
                }
                foreach (var c in target.Board.Where(c => !c.IsAlive).ToList()) KillCard(caster, c, def);
                break;
            case "EffectHeal":
                switch (e.Int("targetType"))
                {
                    case 30: HealWithComputed(ctx, i, caster, e); break;
                    case 40 or 50:
                        foreach (var c in Players[caster.Owner].Board.ToList()) HealWithComputed(ctx, i, c, e);
                        break;
                }
                break;
            case "EffectAddAllStat":
                AddAllStat(ctx, i, e, e.Int("targetToModify") == 10 ? Opponent(caster.Owner) : Players[caster.Owner]);
                break;
            case "EffectAddStat" or "EffectAddStatPerLaunch":
                if (e.Int("targetToModify") != 10) AddStatOnCard(ctx, i, e, caster);
                break;
            case "EffectAddCooldown":
                foreach (var c in target.Board.ToList()) AddCooldownEffect(ctx, i, e, c);
                break;
            case "EffectAddSpamton":
                AddSpamtonsEffect(e, action, target, def, caster);
                break;
            case "EffectSetSpamton":
                target.AddSpamtons((int)DrawValue(action, integer: true, caster) - target.Spamtons);
                Log.Add(Round, $"  Spamtons de J{target.Id + 1} fixés à {target.Spamtons}.");
                break;
            case "StealSpamton":
                StealSpamtons(e, action, Players[caster.Owner], target);
                break;
            case "EffectAddLuck":
                AddLuck(action, target, caster);
                break;
            case "EffectApplyRevanche":
                if (caster.IsAlive) Revanche(caster, caster);
                break;
            case "EffectStealElement":
                StealElement(e, action, caster, target);
                break;
            case "EffectResurrection":
                Resurrect(ctx, i, target);
                break;
            // Les autres effets n'ont pas de version « joueur » dans le jeu : rien ne se passe.
        }
    }

    // ================================================================ tirage des valeurs

    /// <summary>
    /// 🔒 AbilityData.DrawValueAtIndex : valeur entre min et max. Pour un effet « entier » à bornes entières,
    /// tirage d'un entier de min à max inclus. Si le lanceur a un Tox Optimisation : toujours le max.
    /// </summary>
    private double DrawValue(ActionDef action, bool integer, CardState? caster = null)
    {
        if (caster is not null && caster.HasTox("Optimisation")) return action.Max;
        var r = _rng.NextDouble();
        if (integer && action.Max > action.Min && action.Min == Math.Floor(action.Min) && action.Max == Math.Floor(action.Max))
            return (int)action.Min + (int)(r * ((int)action.Max - (int)action.Min + 1));
        return action.Min + r * (action.Max - action.Min);
    }

    private string RandomElement()
    {
        var e = _s.RandomElements[_rng.Next(0, _s.RandomElements.Count)];
        _lastTeamEffect = e;
        return e;
    }

    // ================================================================ calcul des dégâts (§5.1, §5.2)

    /// <summary>Options de calcul (« ComputeDamageOption » du jeu).</summary>
    private sealed class DamageOptions
    {
        public bool UseCasterHp, UseOpponentHpMax, UseCasterElement, UseCasterSpamton;
        public bool MustUseElement = true;
        public CardState? TargetCard;
    }

    /// <summary>
    /// 🔒 Player.GetComputedValue puis GetComputedDamage :
    /// 1. critique si « % critique (capacité + bonus) > jet × 100 » (sauf dégâts en % des PV max) ;
    /// 2. élément de l'action (ou de la carte, ou au hasard) ; 3. niveau d'élément du joueur du lanceur ;
    /// 4. valeur tirée + bonus de dégâts de base ; 5. × (1 + niveau × 0,1) ; 6. × critique ;
    /// 7. + (PV max − PV) / 2 si demandé ; 8. × ratio de dégâts ; arrondi.
    /// </summary>
    private int ComputeDamage(Ctx ctx, int actionIndex, DamageOptions o, out string? element)
    {
        var (def, caster, _, _) = ctx;
        var action = def.Actions[actionIndex];
        var rt = caster.Runtime(def.Id);
        var roll = _rng.NextDouble();
        var crit = def.ChanceOfCrit + (rt?.BonusCrit ?? 0) > roll * 100.0 && !o.UseOpponentHpMax;  // 🔒 « canCrit » n'est pas lu
        var critRatio = crit ? def.CritDamageRatio + (rt?.BonusCritRatio ?? 0) : 1.0;
        if (rt is not null)
        {
            rt.LastHitCrit = crit;
            if (crit) rt.WasCrit = true;
        }
        if (crit) Log.Add(Round, "  Coup critique !");

        element = o.UseCasterElement ? caster.Element : action.Element ?? RandomElement();
        // 🔒 Multiplicateur (Tox) : le niveau d'élément est multiplié par la somme des multiplicateurs.
        var mult = ToxSum(caster, "Multiplicator", def);
        var level = o.MustUseElement
            ? Math.Max(Players[caster.Owner].Level(element) * (mult.Count > 0 ? mult.Total : 1), 0) : 0;
        var value = DrawValue(action, integer: false, caster);
        if (o.UseCasterSpamton) value = Players[caster.Owner].Spamtons;
        var baseValue = value + (rt?.BaseDamageBonus ?? 0);
        var total = baseValue + baseValue * level * 0.1;
        if (o.TargetCard is not null && o.UseOpponentHpMax) total = o.TargetCard.HpMax * (value / 100.0);
        if (crit && Stats is not null && critRatio > 0) Stats.Crit(caster, total * critRatio - total);
        total *= critRatio;
        if (o.UseCasterHp) total += (caster.HpMax - caster.Hp) / 2.0;
        total *= rt?.DamageRatio ?? 1;
        // 🔒 Player.DoesCardsHaveBerserk : chaque carte du joueur avec Berserk ajoute X % aux dégâts.
        var berserk = Players[caster.Owner].Board.Select(c => ToxSum(c, "Berserk", def)).ToList();
        if (berserk.Any(b => b.Count > 0)) total *= 1 + berserk.Sum(b => b.Total) / 100.0;
        return RoundToInt(total);
    }

    // ================================================================ réception des dégâts (§5.3)

    /// <summary>
    /// 🔒 GameLogic.DamageCard : Chance vitale / Inversement → Bouclier (Brut l'ignore) → Résistance (Brut l'ignore,
    /// Paradoxe l'inverse) → Érosion ou dégâts (Ténacité, AoE) → bris du Gel → statuts à l'impact → déclencheurs → mort.
    /// <paramref name="element"/> null = dégâts sans élément (poison, Bomba…) : pas de résistance.
    /// </summary>
    public void DamageCard(CardState attacker, CardState target, AbilityDef? ability, string? element, int value,
        bool canProcDeath = true, DamageOrigin origin = DamageOrigin.Ability, bool canProcInversement = true,
        bool canProcAoe = true, bool canTriggerTalents = true, bool useAllStatus = true)
    {
        if (!target.IsAlive) return;
        var wasFrozen = target.Has(StatusKind.Frozen);
        var hpBefore = target.Hp;

        // 🔒 Chance vitale (statut de talent) : X % de chances que les dégâts deviennent un soin.
        var vital = target.HasTalentStatus("ChanceVitale") && _rng.Next(0, 100) < target.TalentStatusValue("ChanceVitale");
        if (canProcInversement && (attacker.HasTox("Inversement") || vital))
        {
            Log.Add(Round, vital ? $"  Chance vitale : {Name(target)} est soignée au lieu d'être touchée."
                                 : $"  Inversement : les dégâts deviennent un soin.");
            HealCard(attacker, target, ability, element, value, useAllStatus, canProcInversement: false, canTriggerTalents);
            return;
        }

        var brut = ToxSum(attacker, "Brut", ability).Count > 0;
        if (target.Has(StatusKind.Shield) && value > 0 && canTriggerTalents)
        {
            if (!brut || attacker.Owner == target.Owner)
            {
                // 🔒 La réduction vient des Tox Bouclier de la carte.
                var reduction = Math.Clamp(ToxSum(target, "Shield").Total, 0, 100) / 100.0;
                value = RoundToInt(value * (1 - reduction));
                Log.Add(Round, $"  Le Bouclier de {Name(target)} absorbe {reduction:P0}.");
            }
            target.Remove(StatusKind.Shield);
            if (value <= 0) return;
        }

        if (!brut && element is not null && target.Resistances.TryGetValue(element, out var res))
        {
            if (target.HasTalentStatus("Paradoxe")) res = -res;          // 🔒 Paradoxe : résistance inversée
            value = RoundToInt(value * (1 - res / 100.0));
        }
        if (brut && canTriggerTalents && attacker.Owner != target.Owner) target.Remove(StatusKind.Taunt);

        var targetImmune = ImmuneTo(target, attacker);
        var erosion = attacker.HasTox("Erosion") || AbilityTox(ability, "Erosion") is not null;
        if (useAllStatus && !targetImmune && erosion)
        {
            // 🔒 Érosion : une part des dégâts (X %, 100 % si X = 0) retire des PV max au lieu de PV.
            var total = ToxSum(attacker, "Erosion", ability).Total;
            var pct = total > 0 ? Math.Min(total, 100) : 100;
            var maxLoss = RoundToInt(value * pct / 100.0);
            var damageBefore = target.Damage;
            if (value > 0) DoDamage(attacker, target, ability, value, canProcAoe);
            var hpAfterHit = target.Hp;
            target.Damage = damageBefore + RoundToInt(value * (1 - pct / 100.0));
            target.HpMax = hpBefore - maxLoss + damageBefore;
            Stats?.HpChange(attacker, target, hpAfterHit, target.Hp);
            Log.Add(Round, $"  Érosion : {Name(target)} perd {maxLoss} PV max → {target.Hp}/{target.HpMax} PV.");
        }
        else
        {
            DoDamage(attacker, target, ability, value, canProcAoe);
            Log.Add(Round, $"  {Name(target)} subit {value} dégâts{(element is null ? "" : $" ({element})")} → {target.Hp}/{target.HpMax} PV.");
        }

        if (wasFrozen && value > 0 && target.IsAlive && (origin != DamageOrigin.Poison || _s.FrozenBrokenByPoison))
            BreakIce(target, attacker, thaw: true);

        ProcStatus(attacker, target, ability, value, isHeal: false, useAllStatus, canTriggerTalents, wasFrozen);
        if (canTriggerTalents)
        {
            Trigger("OnDamageTaken", target, attacker);
            Trigger("OnDamageDone", attacker, target);
        }
        if (target.Hp != hpBefore) CheckHpPalier(target);

        if (!target.IsAlive)
        {
            target.Killer = attacker;
            if (canProcDeath) KillCard(attacker, target, ability);
        }
    }

    /// <summary>
    /// 🔒 Card.DoDamage : dégâts (valeur positive) ou soin (négative), jamais au-dessus des PV max.
    /// Ténacité : un coup mortel laisse la carte à 1 PV (sauf si elle y est déjà). AoE : X % aux voisins (x ± 1).
    /// </summary>
    private void DoDamage(CardState source, CardState target, AbilityDef? ability, int value, bool mustProcAoe)
    {
        if (!target.IsAlive) return;
        var hpBefore = target.Hp;
        if (target.Damage + value >= target.HpMax && target.HpMax - 1 != target.Damage && target.HasTox("Tenacite"))
        {
            target.Damage = target.HpMax - 1;
            Log.Add(Round, $"  Ténacité : {Name(target)} reste à 1 PV.");
        }
        else
        {
            target.Damage = Math.Max(target.Damage + value, 0);
            if (value > 0 && !target.IsAlive) target.Killer ??= source;
        }
        Stats?.HpChange(source, target, hpBefore, target.Hp);
        if (!mustProcAoe) return;
        var aoe = ToxSum(source, "Aoe", ability);
        if (aoe.Count <= 0) return;
        var splash = (int)(aoe.Total / 100.0 * value);
        foreach (var n in Players[target.Owner].Board.Where(c => Math.Abs(c.Slot - target.Slot) == 1 && c.IsAlive).ToList())
        {
            var before = n.Hp;
            DoDamage(source, n, ability, splash, mustProcAoe: false);
            Log.Add(Round, $"  AoE : {Name(n)} {(splash >= 0 ? "subit" : "récupère")} {Math.Abs(splash)} → {n.Hp}/{n.HpMax} PV.");
            if (splash != 0 && n.IsAlive && n.Has(StatusKind.Frozen) && (splash > 0 || _s.FrozenBrokenByHeal))
                BreakIce(n, source, thaw: true);
            if (n.Hp != before) CheckHpPalier(n);
        }
    }

    /// <summary>Changement de PV sans effet secondaire (coûts, effets directs).</summary>
    private void ApplyHpChange(CardState source, CardState target, int value)
    {
        if (!target.IsAlive) return;
        var hpBefore = target.Hp;
        target.Damage = Math.Max(target.Damage + value, 0);
        Stats?.HpChange(source, target, hpBefore, target.Hp);
        if (value > 0 && !target.IsAlive) target.Killer ??= source;
    }

    // ================================================================ soin (§5.4)

    private void HealWithComputed(Ctx ctx, int i, CardState target, BrickDef effect)
    {
        var mustUseElement = ctx.Ability.Actions[i].Effect != "healCasterWithoutElement";
        var value = ComputeDamage(ctx, i, new DamageOptions { MustUseElement = mustUseElement }, out var element);
        HealCard(ctx.Caster, target, ctx.Ability, element, value);
    }

    /// <summary>
    /// 🔒 GameLogic.HealCard : Inversement → statuts à l'impact → soin plafonné aux PV max (AoE : soigne aussi les
    /// voisines) → « soin reçu » → bris du Gel (📊 frozenBrokenByHeal).
    /// </summary>
    public void HealCard(CardState caster, CardState target, AbilityDef? ability, string? element, int value,
        bool useAllStatus = true, bool canProcInversement = true, bool canTriggerTalents = true)
    {
        if (canProcInversement && caster.HasTox("Inversement"))
        {
            Log.Add(Round, "  Inversement : le soin devient des dégâts.");
            DamageCard(caster, target, ability, element, value, canProcInversement: false, canTriggerTalents: canTriggerTalents,
                useAllStatus: useAllStatus);
            return;
        }
        if (!target.IsAlive) return;
        var wasFrozen = target.Has(StatusKind.Frozen);
        var before = target.Hp;
        ProcStatus(caster, target, ability, value, isHeal: true, useAllStatus, canTriggerTalents, targetWasFrozen: false);
        DoDamage(caster, target, ability, -value, mustProcAoe: true);
        Log.Add(Round, $"  {Name(target)} est soignée de {target.Hp - before} → {target.Hp}/{target.HpMax} PV.");
        if (target.Hp != before) CheckHpPalier(target);
        if (canTriggerTalents && value > 0) Trigger("OnHealTaken", target, caster);
        if (wasFrozen && value > 0 && target.IsAlive && _s.FrozenBrokenByHeal)
            BreakIce(target, caster, thaw: true);
    }

    // ================================================================ Gel (§6.2)

    /// <summary>
    /// 🔒 Card.BreakIce : retire le Gel ; avec dégel, la carte subit des dégâts égaux à la valeur du Gel
    /// (sans Bouclier ni résistance).
    /// </summary>
    private void BreakIce(CardState card, CardState breaker, bool thaw)
    {
        var ice = card.Get(StatusKind.Frozen);
        if (ice is null) return;
        card.Remove(StatusKind.Frozen);
        Log.Add(Round, $"  Le Gel de {Name(card)} est brisé.");
        if (!thaw || ice.Value <= 0 || !card.IsAlive) return;
        var source = ice.Caster ?? breaker;
        ApplyHpChange(source, card, ice.Value);
        Log.Add(Round, $"  Dégel : {Name(card)} subit {ice.Value} → {card.Hp}/{card.HpMax} PV.");
        CheckHpPalier(card);
        if (!card.IsAlive) card.Killer = breaker;
    }

    // ================================================================ PV max, Spamtons, éléments, résurrection

    /// <summary>🔒 Card.AddHpMaxModificator : les PV max changent, les dégâts subis restent (les PV suivent).</summary>
    private void AddHpMax(CardState caster, CardState card, int value)
    {
        card.HpMax += value;
        Log.Add(Round, $"  PV max de {Name(card)} {value:+0;-0} → {card.Hp}/{card.HpMax}.");
        if (!card.IsAlive) card.Killer ??= caster;
        CheckHpPalier(card);
    }

    private void AddLuck(ActionDef action, PlayerState p, CardState caster)
    {
        var v = (int)DrawValue(action, integer: true, caster);
        p.Luck += v;
        Log.Add(Round, $"  J{p.Id + 1} : chance {v:+0;-0} → {p.Luck:0.##}.");
    }

    /// <summary>🔒 StealSpamton : le lanceur prend N Spamtons (au plus ce que la cible possède, si demandé).</summary>
    private void StealSpamtons(BrickDef e, ActionDef action, PlayerState caster, PlayerState target)
    {
        var n = (int)DrawValue(action, integer: true);
        if (e.Bool("onlyWhatTargetHas")) n = Math.Min(n, Math.Max(0, target.Spamtons));
        AddSpamtons(caster, n, null);
        AddSpamtons(target, -n, null);
        Log.Add(Round, $"  J{caster.Id + 1} vole {n} Spamtons à J{target.Id + 1}.");
    }

    private void AddSpamtonsEffect(BrickDef e, ActionDef action, PlayerState p, AbilityDef source, CardState caster)
    {
        if (e.Bool("mustReset"))
        {
            p.AddSpamtons(-p.Spamtons);
            Log.Add(Round, $"  J{p.Id + 1} perd tous ses Spamtons.");
            return;
        }
        var v = (int)DrawValue(action, integer: true, caster);
        AddSpamtons(p, v, source);
        Log.Add(Round, $"  J{p.Id + 1} : Spamtons {v:+0;-0} → {p.Spamtons}.");
    }

    /// <summary>🔒 EffectStealElement : le joueur visé perd N niveaux d'un élément, le lanceur les gagne.</summary>
    private void StealElement(BrickDef e, ActionDef action, CardState caster, PlayerState target)
    {
        var element = _s.RandomElements[_rng.Next(0, _s.RandomElements.Count)];  // tiré d'abord dans tous les cas
        switch (e.Int("type"))
        {
            case 0 when _lastTeamEffect is not null: element = _lastTeamEffect; break;   // dernier élément tiré
            case 1: element = action.Element ?? element; break;                           // élément de l'action
        }
        var v = DrawValue(action, integer: false, caster);
        target.ElementLevels[element] = target.Level(element) - v;
        Players[caster.Owner].ElementLevels[element] = Players[caster.Owner].Level(element) + v;
        Log.Add(Round, $"  J{caster.Owner + 1} vole {v:0.##} niveau(x) {element} à J{target.Id + 1}.");
    }

    /// <summary>🔒 EffectResurrection (version « carte ») : la carte visée (morte ou non) revient avec X % de ses PV.</summary>
    private void ResurrectCard(Ctx ctx, int i, CardState card)
    {
        var p = Players[card.Owner];
        if (!p.Board.Contains(card) && p.Board.Count >= _s.CardsPerDeck) return;
        p.Discard.Remove(card);
        if (!p.Board.Contains(card)) p.Board.Add(card);
        ReviveCommon(ctx, i, card, p);
    }

    private void ReviveCommon(Ctx ctx, int i, CardState card, PlayerState p)
    {
        card.ResetForResurrection();
        if (p.Board.Any(c => c != card && c.Slot == card.Slot))
        {
            var free = Enumerable.Range(1, _s.CardsPerDeck).Where(x => p.Board.All(c => c.Slot != x)).ToList();
            if (free.Count > 0) card.Slot = free[_rng.Next(0, free.Count)];
        }
        var r = DrawValue(ctx.Ability.Actions[i], integer: false, ctx.Caster);
        card.Damage = Math.Max((int)((100.0 - r) / 100.0 * card.HpMax), 0);
        Log.Add(Round, $"  {Name(card)} revient en jeu avec {card.Hp}/{card.HpMax} PV.");
        CheckHpPalier(card);
    }

    /// <summary>🔒 EffectResurrection (version « joueur ») : la dernière carte morte revient avec X % de ses PV.</summary>
    private void Resurrect(Ctx ctx, int i, PlayerState p)
    {
        if (p.Discard.Count == 0 || p.Board.Count >= _s.CardsPerDeck) return;
        var card = p.Discard[^1];
        p.Discard.RemoveAt(p.Discard.Count - 1);
        p.Board.Add(card);
        ReviveCommon(ctx, i, card, p);
    }

    // ================================================================ statistiques

    /// <summary>Le statut v1 correspondant au type d'un Tox (« tox:uuid »), ou null.</summary>
    private StatusKind? StatusOfTox(string? toxUuid) =>
        toxUuid is not null && _kb.Tox(toxUuid) is { } t ? t.StatusType switch
        {
            "Protection" => StatusKind.Taunt,
            "Frozen" => StatusKind.Frozen,
            "Silenced" => StatusKind.Silenced,
            "Paralysed" => StatusKind.Paralysed,
            "Poisoned" => StatusKind.Poisoned,
            "Shield" => StatusKind.Shield,
            "Stealth" => StatusKind.Stealth,
            "Smoked" => StatusKind.Smoked,
            _ => null,
        } : null;

    /// <summary>Valeur d'un effet de statistique : tirage (entier pour recharge, élément, PV max) + bonus par lancer.</summary>
    private double StatValue(Ctx ctx, int i, BrickDef e, bool integer, bool strongestOnOptimisation = false)
    {
        var action = ctx.Ability.Actions[i];
        double v;
        if (strongestOnOptimisation)
        {
            // 🔒 EffectAddStat : tirage sans lanceur, puis Optimisation = borne la plus forte (en valeur absolue).
            v = DrawValue(action, integer);
            if (ctx.Caster.HasTox("Optimisation")) v = Math.Abs(action.Min) > Math.Abs(action.Max) ? action.Min : action.Max;
        }
        else v = DrawValue(action, integer, ctx.Caster);
        var perLaunch = e.Int("ratioNumberActivation");
        if (perLaunch != 0) v += perLaunch * (ctx.Caster.Abilities.FirstOrDefault(a => a.Def.Id == ctx.Ability.Id)?.CounterLaunched ?? 0);
        return v;
    }

    /// <summary>🔒 EffectAddAllStat : modifie une statistique pour toutes les cartes d'un joueur.</summary>
    private void AddAllStat(Ctx ctx, int i, BrickDef e, PlayerState target)
    {
        var (def, caster, _, _) = ctx;
        var stat = e.Int("StatToModify");
        var value = StatValue(ctx, i, e, integer: stat is 1 or 80 or 100);
        var team = def.Actions[i].Element;
        switch (stat)
        {
            case 1:   // niveau d'élément
            {
                string? element = e.Bool("useCardTypeForDamage") ? caster.Element
                    : e.Bool("randomTeam") ? RandomElement() : team;
                AddElementLevel(target, element, (int)value, source: caster);
                break;
            }
            case 25: foreach (var c in target.Board) AddCritChance(c, caster, team, value, false); break;
            case 70: foreach (var c in target.Board) AddMissChance(c, caster, team, value, false); break;
            case 80: foreach (var c in target.Board) AddAllAbilityCooldown(caster, def, c, (int)value, team, false); break;
            case 40: foreach (var c in target.Board) AddCritRatio(c, caster, team, value, false); break;
            case 92:
            {
                var element = e.Bool("randomTeam") ? RandomElement() : team;
                foreach (var c in target.Board) AddBaseDamageAll(c, caster, element, value, false);
                break;
            }
            case 100:
                if (e.Bool("randomTeam")) RandomElement();
                foreach (var c in target.Board.ToList()) AddHpMax(caster, c, (int)value);
                break;
            case 110: foreach (var c in target.Board) MultiplyDamageRatio(c, caster, value); break;
            case 120: foreach (var c in target.Board) AddResistance(c, caster, team, value); break;
        }
    }

    /// <summary>🔒 EffectAddStat : modifie une statistique d'une seule carte.</summary>
    private void AddStatOnCard(Ctx ctx, int i, BrickDef e, CardState card)
    {
        var (def, caster, _, _) = ctx;
        var stat = e.Int("StatToModify");
        // 🔒 EffectAddStat : valeur inversée quand le talent « palier » se défait (PV repassés au-dessus du seuil).
        var value = StatValue(ctx, i, e, integer: stat is 80 or 75, strongestOnOptimisation: true) * (ctx.Inverse ? -1 : 1);
        var team = def.Actions[i].Element;
        var useCardType = e.Int("teamCondition") == 1;   // 1 = élément de la carte, 25 = élément de la capacité
        switch (stat)
        {
            case 1: AddElementLevel(Players[card.Owner], team, (int)value, source: caster); break;
            case 25: AddCritChance(card, caster, team, value, useCardType); break;
            case 70: AddMissChance(card, caster, team, value, useCardType); break;
            case 80: AddAllAbilityCooldown(caster, def, card, (int)value, team, useCardType); break;
            case 40: AddCritRatio(card, caster, team, value, useCardType); break;
            case 92:
                if (e.Int("teamCondition") == 25)
                {
                    // 🔒 Bonus sur la capacité lancée elle-même, si la carte visée la possède.
                    if (!ImmuneTo(card, caster) && card.Runtime(def.Id) is { } rt && Matches(card, rt, team, false))
                    {
                        rt.BaseDamageBonus += value;
                        Log.Add(Round, $"  « {_kb.AbilityName(rt.Def.Id)} » de {Name(card)} : dégâts de base {value:+0.##;-0.##}.");
                    }
                }
                else AddBaseDamageAll(card, caster, team, value, false);
                break;
            case 100:
            {
                AddHpMaxScaled(caster, card, def, i, value);
                if (e.Bool("transferToCaster") && card != caster && caster.IsAlive)
                    AddHpMaxScaled(caster, caster, def, i, -value);
                break;
            }
            case 110: MultiplyDamageRatio(card, caster, value); break;
            case 120: AddResistance(card, caster, team ?? RandomElement(), value); break;
        }
    }

    /// <summary>🔒 EffectAddStat (PV max) : la valeur augmente de 10 % par niveau de l'élément de l'action.</summary>
    private void AddHpMaxScaled(CardState caster, CardState card, AbilityDef def, int i, double value)
    {
        var num = value;
        var element = def.Actions[i].Element;
        if (element is not null)
            num += Players[card.Owner].Level(element) * (1 + ToxSum(caster, "Multiplicator", def).Total) * 0.1 * value;
        AddHpMax(caster, card, (int)num);
    }

    private static bool Matches(CardState card, AbilityState rt, string? team, bool useCardType) =>
        team is null || (useCardType ? card.Def.Element == team : rt.Def.Element == team);

    /// <summary>
    /// 🔒 Player.AddBaseDamage : +N niveaux d'élément (tous les éléments connus si aucun). Mimétisme : chaque Tox
    /// Mimétisme d'une carte adverse donne aussi +1 dans cet élément à son propriétaire.
    /// </summary>
    private void AddElementLevel(PlayerState target, string? element, int value, bool canMimetisme = true, CardState? source = null)
    {
        if (canMimetisme && value > 0)
            foreach (var mimic in Opponent(target.Id).Board.SelectMany(c => c.Toxes).Where(t => t.Def.StatusType == "Mimetisme").ToList())
                AddElementLevel(Opponent(target.Id), element, 1, canMimetisme: false);
        if (element is null)
        {
            // 🔒 Player.AddBaseDamage sans élément : tous les niveaux déjà connus du joueur changent.
            foreach (var k in target.ElementLevels.Keys.ToList())
            {
                target.ElementLevels[k] += value;
                Stats?.ElementLevel(source, target.Id, k, value, target.ElementLevels[k]);
            }
            Log.Add(Round, $"  J{target.Id + 1} : tous les niveaux d'élément {value:+0;-0}.");
            return;
        }
        target.ElementLevels[element] = target.Level(element) + value;
        Stats?.ElementLevel(source, target.Id, element, value, target.ElementLevels[element]);
        Log.Add(Round, $"  J{target.Id + 1} : niveau {element} {value:+0;-0} → {target.Level(element)}.");
    }

    /// <summary>🔒 Card.AddChanceCrit : bonus borné pour que la chance totale reste entre 0 et 100 %.</summary>
    private void AddCritChance(CardState card, CardState caster, string? team, double value, bool useCardType)
    {
        if (ImmuneTo(card, caster)) return;
        foreach (var rt in card.Abilities.Where(rt => Matches(card, rt, team, useCardType)))
            rt.BonusCrit = Math.Clamp(rt.BonusCrit + value, -rt.Def.ChanceOfCrit, 100 - rt.Def.ChanceOfCrit);
        Log.Add(Round, $"  {Name(card)} : chance de critique {value:+0.##;-0.##} %.");
    }

    /// <summary>🔒 Card.AddChanceMiss : valeur arrondie, chance totale bornée entre 0 et 99 %.</summary>
    private void AddMissChance(CardState card, CardState caster, string? team, double value, bool useCardType)
    {
        if (ImmuneTo(card, caster)) return;
        var v = Math.Round(value);
        foreach (var rt in card.Abilities.Where(rt => Matches(card, rt, team, useCardType)))
            rt.BonusMiss = Math.Clamp(rt.BonusMiss + v, -rt.Def.ChanceOfMiss, 100 - rt.Def.ChanceOfMiss - 1);
        Log.Add(Round, $"  {Name(card)} : chance de rater {v:+0;-0} %.");
    }

    /// <summary>🔒 Card.AddCritickDamageRatio : le multiplicateur total ne descend pas sous 1.</summary>
    private void AddCritRatio(CardState card, CardState caster, string? team, double value, bool useCardType)
    {
        if (ImmuneTo(card, caster)) return;
        foreach (var rt in card.Abilities.Where(rt => Matches(card, rt, team, useCardType)))
            rt.BonusCritRatio = Math.Max(rt.BonusCritRatio + value, 1 - rt.Def.CritDamageRatio);
        Log.Add(Round, $"  {Name(card)} : multiplicateur de critique {value:+0.##;-0.##}.");
    }

    private void AddBaseDamageAll(CardState card, CardState caster, string? team, double value, bool useCardType)
    {
        if (ImmuneTo(card, caster)) return;
        foreach (var rt in card.Abilities.Where(rt => Matches(card, rt, team, useCardType)))
            rt.BaseDamageBonus += value;
        Log.Add(Round, $"  {Name(card)} : dégâts de base {value:+0.##;-0.##}.");
    }

    /// <summary>🔒 Card.AddDamageRatio : multiplie le ratio de dégâts de toutes les capacités (minimum 0).</summary>
    private void MultiplyDamageRatio(CardState card, CardState caster, double value)
    {
        if (ImmuneTo(card, caster)) return;
        foreach (var rt in card.Abilities) rt.DamageRatio = Math.Max(rt.DamageRatio * value, 0);
        Log.Add(Round, $"  {Name(card)} : ratio de dégâts × {value:0.##}.");
    }

    /// <summary>🔒 Card.AddResistance : résistance bornée entre −50 et +50 ; sans élément, toutes les résistances.</summary>
    private void AddResistance(CardState card, CardState caster, string? element, double value)
    {
        if (ImmuneTo(card, caster)) return;
        var keys = element is null ? card.Resistances.Keys.ToList()
            : card.Resistances.ContainsKey(element) ? [element] : new List<string>();
        foreach (var k in keys)
            card.Resistances[k] = (int)Math.Clamp(card.Resistances[k] + value, -50, 50);
        Log.Add(Round, $"  {Name(card)} : résistance {(element ?? "toutes")} {value:+0.##;-0.##}.");
    }

    /// <summary>🔒 EffectAddCooldown : ajout de N, ou remise des recharges au maximum de base.</summary>
    private void AddCooldownEffect(Ctx ctx, int i, BrickDef e, CardState card)
    {
        if (e.Int("type") == 0)
        {
            AddAllAbilityCooldown(ctx.Caster, ctx.Ability, card, (int)DrawValue(ctx.Ability.Actions[i], integer: true, ctx.Caster), null, false);
            return;
        }
        // 🔒 Card.ResetAllCooldown : chaque recharge repart de la recharge de base de la capacité.
        if (ImmuneTo(card, ctx.Caster)) return;
        foreach (var rt in card.Abilities) rt.CurrentCooldown = Math.Max(rt.Def.Cooldown, 0);
        Log.Add(Round, $"  Recharges de {Name(card)} remises au maximum.");
    }

    /// <summary>
    /// 🔒 Card.AddAllAbilityCooldown : ajoute N à la recharge des capacités de la carte (minimum 0),
    /// filtrées par élément, sauf la capacité en cours de lancer quand elle appartient au même joueur.
    /// </summary>
    private void AddAllAbilityCooldown(CardState caster, AbilityDef? castDef, CardState card, int value, string? team, bool useCardType)
    {
        // 🔒 Immunité : protège d'une hausse de recharge (Tox), et de tout changement venant de l'adversaire.
        if (value > 0 ? card.HasTox("ImunityStatus") : ImmuneTo(card, caster)) return;
        if (caster.HasTox("Inversement")) value = -value;                       // 🔒 Inversement
        if (value > 0 && card.HasTalentStatus("InsensibleEternity")) return;     // 🔒 Insensible à l'Éternité
        var castRuntimeId = castDef is null ? null : caster.Runtime(castDef.Id)?.Def.Id ?? castDef.Id;
        foreach (var a in card.Abilities)
        {
            if (a.Def.Id == castRuntimeId && caster.Owner == card.Owner) continue;
            if (!Matches(card, a, team, useCardType)) continue;
            a.CurrentCooldown = Math.Max(0, a.CurrentCooldown + value);
        }
        Log.Add(Round, $"  Recharges de {Name(card)} {value:+0;-0}.");
    }
}
