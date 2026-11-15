using SpamDrafter.Simulator.Data;

namespace SpamDrafter.Simulator.Engine;

/// <summary>
/// Choix demandés aux joueurs PENDANT la résolution d'une capacité : cible d'une capacité enchaînée,
/// option d'une capacité à choix multiples. Sans décideur, le simulateur prend la première option.
/// </summary>
public interface IDuelDecider
{
    CardState ChooseTarget(Duel duel, CardState caster, AbilityDef ability, IReadOnlyList<CardState> targets);
    int ChooseOption(Duel duel, CardState caster, AbilityDef ability, IReadOnlyList<AbilityDef> options);

    /// <summary>Choix d'un Tox spécial (Métamorphose : une carte ; Découverte : une capacité). Par défaut, le premier.</summary>
    int ChooseAmong(Duel duel, CardState card, string kind, IReadOnlyList<string> options) => 0;
}

// Lancer une capacité : conditions (§3.1), ciblage (§3.2), résolution (§4.2), enchaînements, choix, Écho.
public sealed partial class Duel
{
    /// <summary>Décideurs des deux joueurs (null = première option).</summary>
    public IDuelDecider?[] Deciders { get; } = new IDuelDecider?[2];

    private CardState? _lastTarget;      // « last_target » : remis à zéro à chaque début de tour
    private CardState? _lastPlayed;      // « last_played » : dernière carte qui a lancé une capacité
    private CardState? _castCaster;      // lancer en cours (Vengeur, Camouflage)
    private AbilityDef? _castAbility;
    private CardState? _vengeurKiller;
    private bool _insertNext;            // 🔒 ResolveQueue.SetInsertNext : la Revanche passe en tête
    private string? _lastTeamEffect;     // dernier élément tiré au hasard (EffectStealElement)

    // ================================================================ peut-on lancer ? (§3.1)

    public bool CanCast(CardState card, AbilityState ability)
    {
        if (!CanCastExceptTargets(card, ability)) return false;
        return ability.Def.Target switch
        {
            "SelectTarget" => ValidSelectTargets(card, ability.Def).Any(),
            "ChoiceSelector" => SelectableChoices(card, ability.Def).Count > 0,
            _ => true,
        };
    }

    /// <summary>Toutes les vérifications de <see cref="CanCast"/> sauf « au moins une cible / un choix possible ».</summary>
    private bool CanCastExceptTargets(CardState card, AbilityState ability)
    {
        if (Phase != DuelPhase.Main || card.Owner != CurrentPlayer) return false;
        if (!Active.Board.Contains(card) || !card.IsAlive) return false;
        if (!card.Abilities.Contains(ability) || !ability.Def.IsActive) return false;
        if (!card.CanDoActivatedAbilities) return false;                 // paralysie, silence, gel
        if (!CanPay(card, ability.Def)) return false;
        return TriggerConditionsMet(card, ability.Def);
    }

    /// <summary>🔒 Player.CanPayAbility : épuisement, recharge, activations, coût.</summary>
    private bool CanPay(CardState card, AbilityDef def)
    {
        var rt = card.Abilities.FirstOrDefault(a => a.Def.Id == def.Id);  // capacité de la carte (pas de correspondance « Echo »)
        if (card.Exhausted && def.Exhaust) return false;
        if (rt is not null && rt.CurrentCooldown > 0) return false;
        if (def.NumberOfActivation > 0 && rt is not null && rt.ActivationsLeft is <= 0) return false;
        return CostsMet(card, def);
    }

    /// <summary>Tous les lancers possibles du joueur actif (une option par cible pour « cible choisie »).</summary>
    public List<CastOption> LegalCasts()
    {
        var result = new List<CastOption>();
        if (Phase != DuelPhase.Main) return result;
        foreach (var card in Active.Board)
            foreach (var ab in card.Abilities)
            {
                // Vitesse (phase 8) : les cibles ne sont calculées qu'une fois (avant : CanCast puis de nouveau ici).
                if (!CanCastExceptTargets(card, ab)) continue;
                if (ab.Def.Target == "SelectTarget")
                    foreach (var t in ValidSelectTargets(card, ab.Def))
                        result.AddRange(SlotOrigins(ab.Def, t).Select(o => new CastOption(card, ab, t, o)));
                else if (ab.Def.Target != "ChoiceSelector" || SelectableChoices(card, ab.Def).Count > 0)
                    result.Add(new CastOption(card, ab, null));
            }
        return result;
    }

    /// <summary>Cibles valides d'une capacité « cible choisie » : cartes vivantes que l'on peut viser.</summary>
    public List<CardState> ValidSelectTargets(CardState caster, AbilityDef ability) =>
        AllCards.Where(t => t.IsAlive && CanTarget(caster, ability, t)).ToList();

    /// <summary>
    /// 🔒 GameLogic.SelectChoice + Game.CanSelectAbility : options d'un choix multiple que l'on peut prendre
    /// (on écarte aussi une option « cible choisie » sans cible possible, que le joueur ne pourrait pas finir).
    /// </summary>
    public List<AbilityDef> SelectableChoices(CardState caster, AbilityDef choice) =>
        choice.ChainAbilities.Where(_kb.HasAbility).Select(_kb.Ability)
            .Where(sub => caster.CanDoAbilities && CanPay(caster, sub) && TriggerConditionsMet(caster, sub) &&
                          (sub.Target != "SelectTarget" || ValidSelectTargets(caster, sub).Count > 0))
            .ToList();

    /// <summary>
    /// 🔒 AbilityData.CanTarget (§3.2) : impossible de viser une carte en Furtivité, camouflée,
    /// ou Protégée (une alliée provoque) quand on est l'adversaire. Puis conditions de cible.
    /// </summary>
    public bool CanTarget(CardState caster, AbilityDef ability, CardState target)
    {
        if (target.Has(StatusKind.Stealth)) return false;
        if (target.IsCamouflaged) return false;
        // 🔒 Protégée : l'adversaire ne peut pas la viser, sauf avec un Tox Brut.
        if (caster.Owner != target.Owner && IsProtected(target) && !caster.HasTox("Brut")) return false;
        return TargetConditionsMet(caster, ability, target);
    }

    /// <summary>
    /// 🔒 GameLogic.UpdateOngoing : une carte qui provoque (et n'est ni en Furtivité ni camouflée)
    /// rend « Protégées » ses alliées qui ne provoquent pas.
    /// </summary>
    public bool IsProtected(CardState card) =>
        !card.Has(StatusKind.Taunt) &&
        Players[card.Owner].Board.Any(a => a != card && a.Has(StatusKind.Taunt) &&
                                           !a.Has(StatusKind.Stealth) && !a.IsCamouflaged);

    // ================================================================ lancer (§3.1, §4)

    public void Cast(CardState card, AbilityState ability, CardState? target = null, string? origin = null) =>
        Cast(new CastOption(card, ability, target, origin));

    public void Cast(CastOption option)
    {
        var (card, ability, target, origin) = option;
        if (!CanCast(card, ability))
            throw new InvalidOperationException($"{Name(card)} ne peut pas lancer « {ability.Def.Id} ».");
        var def = ability.Def;
        if (def.Target == "SelectTarget" && (target is null || !target.IsAlive || !CanTarget(card, def, target)))
            throw new InvalidOperationException($"Cible invalide pour « {def.Id} ».");
        if (origin is not null && (!def.ChoosesSlot || target is null || !SlotOrigins(def, target).Contains(origin)))
            throw new InvalidOperationException($"Emplacement invalide pour « {def.Id} » : {origin}.");
        Record(new Step(StepKind.Action, Action: RefOf(option)));
        // 🔒 GameLogic.SelectTarget : l'emplacement choisi est retenu pour la cible pendant la résolution.
        _slotTarget = origin is null ? null : target;
        _slotOrigin = origin is null ? null : SlotObject(target!, origin);

        Log.Add(Round, $"{Name(card)} lance « {_kb.AbilityName(def.Id)} »" +
                       (target is null ? "." : $" sur {Name(target)}."));
        if (Stats is not null) Stats.Of(card).Casts++;
        _lastPlayed = card;
        _castCaster = card;
        _castAbility = def;
        _vengeurKiller = null;
        card.Remove(StatusKind.Stealth);                       // §3.1 : le lancer fait perdre la Furtivité
        Resolve(new Ctx(def, card, null, true), target);
        _slotTarget = null;
        _slotOrigin = null;
        Trigger("OnCastAbility", card, card);
        Drain();
        _castCaster = null;
        _castAbility = null;
    }

    /// <summary>
    /// Contexte de résolution (« AbilityResolutionContext ») : capacité, lanceur, capacité principale,
    /// talent d'origine (pour les talents et leurs enchaînements) et inversion des valeurs.
    /// </summary>
    private sealed record Ctx(AbilityDef Ability, CardState Caster, AbilityDef? Main, bool IsLastChain)
    {
        public TalentState? Talent { get; init; }
        public bool Inverse { get; init; }
    }

    // ================================================================ file de résolution (🔒 ResolveQueue)

    private readonly LinkedList<Ctx> _queue = new();

    /// <summary>Met une résolution en attente (« premier arrivé, premier servi » ; en tête pour la Revanche).</summary>
    private void Enqueue(Ctx ctx, bool front = false)
    {
        if (front || _insertNext) _queue.AddFirst(ctx); else _queue.AddLast(ctx);
    }

    /// <summary>Résout tout ce qui est en attente, dans l'ordre (garde-fou contre une boucle sans fin).</summary>
    private void Drain()
    {
        var guard = 0;
        while (_queue.Count > 0 && Phase != DuelPhase.Ended)
        {
            if (++guard > 5000)
            {
                Log.Add(Round, "  (file de résolution interrompue : boucle détectée)");
                _queue.Clear();
                break;
            }
            var ctx = _queue.First!.Value;
            _queue.RemoveFirst();
            Resolve(ctx);
        }
        if (Phase == DuelPhase.Ended) _queue.Clear();
    }

    /// <summary>
    /// 🔒 GameLogic.Update / ResolveDeathsAndCheckWinner : quand la file est vide, le jeu traite les morts
    /// (ManageDiscard) puis résout aussitôt ce qu'elles ont déclenché (« à ma mort »…), jusqu'à 16 fois.
    /// Corrige (phase 7) un oubli : ces déclenchements restaient en attente jusqu'à l'action suivante.
    /// </summary>
    private void Settle()
    {
        for (var i = 0; i < 16; i++)
        {
            Drain();
            UpdateOngoing();
            CheckForWinner();
            if (_queue.Count == 0 || Phase == DuelPhase.Ended) break;
        }
        if (Phase == DuelPhase.Ended) _queue.Clear();
    }

    /// <summary>🔒 GameLogic.ResolveCardAbility.</summary>
    private void Resolve(Ctx ctx, CardState? chosenTarget = null)
    {
        if (Phase == DuelPhase.Ended) return;
        var (def, caster, main, _) = ctx;

        // Talent limité : activations revérifiées au moment de la résolution.
        if (ctx.Talent is { ActivationsLeft: <= 0 } && main is null) return;

        // Une carte gelée ou réduite au silence ne résout pas la capacité (sauf un talent) ;
        // la recharge est quand même appliquée.
        if (!def.IsTalent && !caster.CanDoAbilities)
        {
            Log.Add(Round, $"  « {_kb.AbilityName(def.Id)} » est annulée (gel ou silence).");
            if (main is not null && MustManageCooldown(ctx)) ApplyCooldown(ctx);
            return;
        }
        if (def.IsTalent) Log.Add(Round, $"  Talent « {_kb.AbilityName(def.Id)} » de {Name(caster)}.");

        switch (def.Target)
        {
            case "SelectTarget":
            {
                var target = chosenTarget;
                if (target is null)
                {
                    var options = ValidSelectTargets(caster, def);
                    if (options.Count == 0)
                    {
                        // Dans le jeu, le sélecteur resterait ouvert jusqu'à l'annulation : rien n'est résolu.
                        Log.Add(Round, $"  « {_kb.AbilityName(def.Id)} » : aucune cible possible, rien ne se passe.");
                        if (MustManageCooldown(ctx)) ApplyCooldown(ctx);
                        return;
                    }
                    target = AskTarget(caster, def, options);
                    Log.Add(Round, $"  « {_kb.AbilityName(def.Id)} » vise {Name(target)}.");
                }
                ResolveOnCard(ctx, target);
                AfterResolved(ctx);
                return;
            }
            case "ChoiceSelector":
            {
                var options = SelectableChoices(caster, def);
                if (options.Count == 0) { Log.Add(Round, "  Aucun choix possible."); return; }
                var k = AskOption(caster, def, options);
                Log.Add(Round, $"  Choix : « {_kb.AbilityName(options[k].Id)} ».");
                // 🔒 SelectChoice : l'option est résolue avec la capacité à choix comme capacité principale.
                Resolve(new Ctx(options[k], caster, def, true) { Talent = ctx.Talent });
                return;
            }
        }

        if (!IsTargetModeSupported(def.Target))
            Log.Add(Round, $"  (mode de ciblage « {def.Target} » non simulé)");
        foreach (var t in def.Passives.Where(t => !IsToxSupported(t)))
            Log.Add(Round, $"  (statut posé par la capacité non simulé : {t})");
        foreach (var p in PlayerTargets(caster, def))
            ResolveOnPlayer(ctx, p);
        foreach (var t in CardTargets(caster, def))
            ResolveOnCard(ctx, t);
        AfterResolved(ctx);
    }

    private IDuelDecider Decide(CardState caster) => Deciders[caster.Owner] ?? FirstOptionDecider.Instance;

    // ================================================================ emplacement choisi (« choosesSlot », KI-029)

    private CardState? _slotTarget;
    private object? _slotOrigin;   // ToxState ou AbilityState de la cible

    /// <summary>
    /// 🔒 GameLogic.TryTakeSlotOrigin + Card.GetSlotOccupant / CanFreeVisualSlot : emplacements qu'une capacité
    /// « choosesSlot » peut libérer sur la cible. null = emplacement vide (seulement si la carte n'est pas pleine) ;
    /// sinon un Tox (« T:n ») ou une capacité autre que celle lancée (« A:id »). Hors « choosesSlot » : [null].
    /// </summary>
    public List<string?> SlotOrigins(AbilityDef def, CardState target)
    {
        var result = new List<string?>();
        if (!def.ChoosesSlot) { result.Add(null); return result; }
        if (VisualSlots(target) < 3) result.Add(null);
        for (var k = 0; k < target.Toxes.Count; k++) result.Add("T:" + k);
        foreach (var a in target.Abilities.Where(a => a.Def.Id != def.Id)) result.Add("A:" + a.Def.Id);
        if (result.Count == 0) result.Add(null);   // carte pleine sans rien à libérer : choix par défaut
        return result;
    }

    private static object SlotObject(CardState target, string origin) =>
        origin.StartsWith("T:") ? target.Toxes[int.Parse(origin[2..])]
            : target.Abilities.First(a => a.Def.Id == origin[2..]);

    /// <summary>
    /// 🔒 GameLogic.TakeSlotOrigin : l'emplacement choisi n'est utilisé qu'une fois, et seulement sur la cible choisie.
    /// Faux = aucun choix explicite (le simulateur applique alors le choix par défaut du jeu).
    /// </summary>
    private bool TakeChosenSlot(CardState target)
    {
        if (_slotTarget != target || _slotOrigin is null) return false;
        var origin = _slotOrigin;
        _slotTarget = null;
        _slotOrigin = null;
        if (origin is ToxState tox && target.Toxes.Remove(tox))
            Log.Add(Round, $"  {Name(target)} perd le Tox « {tox.Def.AssetName} » (emplacement choisi).");
        else if (origin is AbilityState ab && target.Abilities.Remove(ab))
            Log.Add(Round, $"  {Name(target)} perd « {_kb.AbilityName(ab.Def.Id)} » (emplacement choisi).");
        return true;
    }

    /// <summary>
    /// 🔒 EffectExecutor.Execute (cible carte) : pour chaque action, condition, jet de raté, effet.
    /// Talents « changement de palier » : une action à seuil ne s'applique que quand les PV du lanceur
    /// passent sous le seuil, et s'applique à l'envers quand ils repassent au-dessus.
    /// </summary>
    private void ResolveOnCard(Ctx ctx, CardState target, bool setLastTarget = true)
    {
        var def = ctx.Ability;
        var palier = def.Trigger == "OnHPPalierChange";
        var pct = ctx.Caster.HpMax > 0 ? (float)ctx.Caster.Hp * 100f / ctx.Caster.HpMax : 0f;
        for (var i = 0; i < def.Actions.Count; i++)
        {
            var action = def.Actions[i];
            var proceed = true;
            var inverse = ctx.Inverse;
            string? key = null;
            var active = false;
            if (palier && action.PercentHpToProc != -1)
            {
                key = def.Id + "#" + i;
                active = pct < action.PercentHpToProc;
                proceed = active != ctx.Caster.HpPalierLines.GetValueOrDefault(key);
                inverse = !active;
            }
            if (!ActionConditionMet(ctx.Caster, def, action, target, null)) continue;
            if (MissRoll(ctx, action)) { Log.Add(Round, $"  Raté sur {Name(target)}."); continue; }
            if (!proceed) continue;
            if (key is not null) ctx.Caster.HpPalierLines[key] = active;
            ApplyEffectOnCard(inverse == ctx.Inverse ? ctx : ctx with { Inverse = inverse }, i, target);
        }
        // 🔒 EffectExecutor.Execute : les statuts Provocation et Silence de la capacité sont posés sur la cible
        // (pour la durée de la capacité), même si les actions ont raté.
        foreach (var tox in def.Passives.Select(_kb.Tox).OfType<ToxDef>())
        {
            if (tox.StatusType == "Protection") ApplyStatus(target, ctx.Caster, StatusKind.Taunt, 0, def.Duration);
            else if (tox.StatusType == "Silenced") ApplyStatus(target, ctx.Caster, StatusKind.Silenced, 0, def.Duration);
        }
        if (setLastTarget) _lastTarget = target;
    }

    private void ResolveOnPlayer(Ctx ctx, PlayerState target)
    {
        for (var i = 0; i < ctx.Ability.Actions.Count; i++)
        {
            var action = ctx.Ability.Actions[i];
            if (!ActionConditionMet(ctx.Caster, ctx.Ability, action, null, target)) continue;
            if (MissRoll(ctx, action)) { Log.Add(Round, "  Raté."); continue; }
            ApplyEffectOnPlayer(ctx, i, target);
        }
    }

    /// <summary>🔒 GameLogic.AfterAbilityResolved (§4.2 étape 5).</summary>
    private void AfterResolved(Ctx ctx)
    {
        var (def, caster, main, _) = ctx;
        caster.Exhausted |= def.Exhaust;
        if (caster.Runtime(def.Id) is { } rt)                    // AfterAbilityResolve
        {
            rt.WasCrit = rt.WasMiss = rt.LastHitCrit = false;
            rt.CounterLaunched++;
        }
        PayCosts(caster, def);
        UpdateOngoing();
        CheckForWinner();

        // Capacités enchaînées : mises en file, avec cette capacité comme principale.
        if (def.Target != "ChoiceSelector" && Phase != DuelPhase.Ended)
        {
            for (var i = 0; i < def.ChainAbilities.Count; i++)
            {
                if (!_kb.HasAbility(def.ChainAbilities[i])) continue;
                var chain = _kb.Ability(def.ChainAbilities[i]);
                // 🔒 TriggerCardAbilityCardTrigger : pas de résolution si le lanceur est gelé ou réduit au silence.
                if ((!chain.IsTalent && caster.IsSilencedOrFrozen) || !TriggerConditionsMet(caster, chain, ctx.Talent)) continue;
                Enqueue(new Ctx(chain, caster, def, i == def.ChainAbilities.Count - 1) { Talent = ctx.Talent });
            }
        }
        var chainOnSilence = main is null && def.ChainAbilities.Count > 0 && caster.IsSilencedOrFrozen;
        if (MustManageCooldown(ctx) || chainOnSilence)
            ApplyCooldown(ctx);
        // Combos : aucune capacité atteignable n'en a en 1.30 (vérifié par un test).
    }

    /// <summary>🔒 GameLogic.MustManageCooldown : quand la recharge de la capacité principale est-elle remise ?</summary>
    private static bool MustManageCooldown(Ctx ctx)
    {
        var mainHasChain = ctx.Main is not null && ctx.Main.ChainAbilities.Count > 0;
        var alone = ctx.Main is null && ctx.Ability.ChainAbilities.Count == 0;
        var choice = mainHasChain && ctx.Main!.Target == "ChoiceSelector" && ctx.Main.Id != ctx.Ability.Id;
        var lastChain = mainHasChain && ctx.IsLastChain;
        return alone || choice || lastChain;
    }

    /// <summary>
    /// 🔒 GameLogic.ApplyCooldown : recharge remise au maximum, puis jet d'Écho (§8.6) ;
    /// sans Écho réussi, une activation est consommée.
    /// </summary>
    private void ApplyCooldown(Ctx ctx)
    {
        var (def, caster, main, _) = ctx;
        if (ctx.Talent is { } talent)
        {
            // Talent : pas de recharge (0) ni d'Écho ; une activation est consommée si elle est limitée.
            if (talent.ActivationsLeft is not null) talent.ActivationsLeft--;
            if (Stats is not null) Stats.Of(caster).TalentActivations++;
            return;
        }
        var target = main ?? def;
        if (caster.Runtime(target.Id) is { } rt)
        {
            // 🔒 Tox Reset : après un ennemi tué, la recharge n'est pas remise (la capacité reste prête).
            if (!rt.DontUpdateCooldown) rt.CurrentCooldown = rt.MaxCooldown + rt.BaseCooldownModificator;
            rt.DontUpdateCooldown = false;
        }
        if (caster == _castCaster && target == _castAbility)
        {
            RevealCamouflage(caster);                              // 🔒 CamouflageOnCastEnd
            if (_vengeurKiller is not null) MakeAllAvailable(caster);  // 🔒 ReplayVengeurAfterCooldown
            _vengeurKiller = null;
        }

        // Écho : seulement pour la capacité de la carte elle-même (identifiant identique).
        var echoRt = caster.Abilities.FirstOrDefault(a => a.Def.Id == def.Id);
        // 🔒 Card.GetEchoChance : Écho de la capacité + statuts de talent Résonance et Roulette.
        var echoChance = Math.Min(100.0, def.ChanceOfResetCooldown + caster.TalentStatusValue("Resonance") +
                                          caster.TalentStatusValue("Roulette"));
        if (echoRt is not null && echoChance != 0)
        {
            var roll = _rng.NextDouble() * 100.0;
            if (roll < echoChance + echoRt.EchoModifier)
            {
                echoRt.EchoModifier -= (100 - echoChance) * _s.EchoSmoothing;
                echoRt.CurrentCooldown = 0;
                Log.Add(Round, $"  Écho ! « {_kb.AbilityName(def.Id)} » est de nouveau prête.");
                Trigger("OnEcho", caster, caster);
                return;                                        // pas d'activation consommée
            }
            echoRt.EchoModifier += echoChance * _s.EchoSmoothing;
        }
        if (caster.Runtime(def.Id) is { ActivationsLeft: not null } act) act.ActivationsLeft--;
    }

    private static readonly HashSet<string> SupportedTargets =
        new() { "SelectTarget", "AllCardsBoard", "Self", "PlayerSelf", "PlayerOpponent", "ChoiceSelector", "LastTargeted", "LastPlayed" };

    public static bool IsTargetModeSupported(string target) => SupportedTargets.Contains(target);

    private IEnumerable<PlayerState> PlayerTargets(CardState caster, AbilityDef def)
    {
        var p = def.Target switch
        {
            "PlayerSelf" => Players[caster.Owner],
            "PlayerOpponent" => Opponent(caster.Owner),
            _ => null,
        };
        if (p is null || !def.ConditionsTarget.All(id => ConditionOnPlayer(id, caster, p))) return [];
        return ApplyFilters(def, new List<PlayerState> { p });
    }

    private List<CardState> CardTargets(CardState caster, AbilityDef def)
    {
        var list = def.Target switch
        {
            "Self" => TargetConditionsMet(caster, def, caster) ? new List<CardState> { caster } : new(),
            // 🔒 GetCardTargets : toutes les cartes vivantes qui remplissent les conditions
            // (la Furtivité ne protège pas des effets « toutes les cartes »).
            "AllCardsBoard" => AllCards.Where(c => c.IsAlive && TargetConditionsMet(caster, def, c)).ToList(),
            "LastTargeted" => _lastTarget is { } t && Players[t.Owner].Board.Contains(t) &&
                              TargetConditionsMet(caster, def, t) ? new List<CardState> { t } : new(),
            "LastPlayed" => _lastPlayed is { } lp && Players[lp.Owner].Board.Contains(lp) &&
                            TargetConditionsMet(caster, def, lp) ? new List<CardState> { lp } : new(),
            _ => new(),
        };
        return ApplyFilters(def, list);
    }

    /// <summary>Filtre « au hasard » (seul filtre utilisé, §3.2) : garde N cibles tirées au hasard.</summary>
    private List<T> ApplyFilters<T>(AbilityDef def, List<T> list)
    {
        foreach (var id in def.FiltersTarget)
        {
            var f = _kb.Filter(id);
            if (f?.Class != "FilterRandom") continue;          // aucun autre filtre n'est utilisé
            var amount = f.Int("amount", 1);
            var pool = new List<T>(list);
            var picked = new List<T>();
            while (picked.Count < amount && pool.Count > 0)
            {
                var k = _rng.Next(0, pool.Count);
                picked.Add(pool[k]);
                pool.RemoveAt(k);
            }
            list = picked;
        }
        return list;
    }

    /// <summary>
    /// 🔒 EffectExecutor.CheckMiss : un jet par action. Raté si « % de raté (capacité principale + bonus)
    /// ≥ jet × 100 » et si l'action utilise le jet de raté.
    /// </summary>
    private bool MissRoll(Ctx ctx, ActionDef action)
    {
        var reference = ctx.Main ?? ctx.Ability;
        var rt = ctx.Caster.Runtime(reference.Id);
        var roll = _rng.NextDouble();
        var miss = !(reference.ChanceOfMiss + (rt?.BonusMiss ?? 0) < roll * 100.0) && action.UseMissRoll;
        if (miss)
        {
            if (rt is not null) rt.WasMiss = true;
            if (ctx.Caster.Runtime(ctx.Ability.Id) is { } own) own.LastHitCrit = false;
        }
        return miss;
    }

    // ================================================================ coûts

    private bool CostsMet(CardState card, AbilityDef def)
    {
        foreach (var c in def.Costs)
        {
            var brick = _kb.Cost(c.Cost);
            switch (brick?.Class)
            {
                case "CostDataElement":
                    // 🔒 CostDataElement : niveau ≠ 0 et comparaison (oper) avec la valeur.
                    var level = (int)Players[card.Owner].Level(brick.Link("team") ?? "");
                    if (level == 0 || !CompareInt(level, brick.Int("oper"), c.Value)) return false;
                    break;
                case "CostDataHP":
                    if (!CompareInt(card.Hp, brick.Int("oper"), c.Value)) return false;
                    break;
                default:
                    return false; // coût inconnu : on refuse plutôt que d'inventer
            }
        }
        return true;
    }

    private void PayCosts(CardState card, AbilityDef def)
    {
        foreach (var c in def.Costs)
        {
            var brick = _kb.Cost(c.Cost)!;
            if (brick.Class == "CostDataElement")
            {
                var p = Players[card.Owner];
                var e = brick.Link("team")!;
                var value = c.Value == 0 ? (int)p.Level(e) : c.Value;
                p.ElementLevels[e] = p.Level(e) - value;
                Log.Add(Round, $"  Coût : −{value} niveau(x) {e} pour J{p.Id + 1}.");
            }
            else if (brick.Class == "CostDataHP" && card.IsAlive)
            {
                var hpBefore = card.Hp;
                card.Damage = Math.Max(card.Damage + c.Value, 0);
                Stats?.HpChange(card, card, hpBefore, card.Hp);
                if (!card.IsAlive) card.Killer ??= card;
                Log.Add(Round, $"  Coût : {Name(card)} perd {c.Value} PV.");
                CheckHpPalier(card);
            }
        }
    }

    private static bool CompareInt(int a, int oper, int b) => CompareFloat(a, oper, b);

    private static bool CompareFloat(double a, int oper, double b) => oper switch
    {
        0 => a == b,
        1 => a != b,
        2 => a >= b,
        3 => a <= b,
        4 => a > b,
        5 => a < b,
        _ => false,
    };
}

/// <summary>Décideur par défaut : toujours la première option (utile aux tests).</summary>
public sealed class FirstOptionDecider : IDuelDecider
{
    public static readonly FirstOptionDecider Instance = new();
    public CardState ChooseTarget(Duel duel, CardState caster, AbilityDef ability, IReadOnlyList<CardState> targets) => targets[0];
    public int ChooseOption(Duel duel, CardState caster, AbilityDef ability, IReadOnlyList<AbilityDef> options) => 0;
}
