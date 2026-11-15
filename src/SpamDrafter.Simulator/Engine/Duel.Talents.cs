using SpamDrafter.Simulator.Data;

namespace SpamDrafter.Simulator.Engine;

// Talents (phase 6, lot B) : attachement, déclencheurs, chance lissée, paliers de PV, Revanche,
// gain de capacité, Spamtons.
public sealed partial class Duel
{
    private readonly Dictionary<CardState, AbilityDef> _chosenTalents = new();

    /// <summary>
    /// 🔒 GameLogic.TriggerStartGameTalent : chaque carte reçoit le talent de son deck, puis ses capacités
    /// « Début de partie » sont mises en file (elles se résolvent au premier tour, avec la file).
    /// </summary>
    private void AttachTalents()
    {
        foreach (var p in Players)
            foreach (var card in p.Board)
            {
                if (!_chosenTalents.TryGetValue(card, out var def)) continue;
                var t = new TalentState(def);
                var drawn = def.ConditionsTrigger.Select(_kb.Condition).FirstOrDefault(c => c?.Class == "ConditionDrawnTurn");
                if (drawn is not null)
                {
                    var first = drawn.Int("firstTurn", 1);
                    var last = drawn.Int("lastTurn", 20);
                    t.DrawnTurn = first + _rng.Next(0, Math.Max(1, last - first + 1));
                }
                card.Talents.Add(t);
                Log.Add(0, $"{Name(card)} : talent « {_kb.AbilityName(def.Id)} ».");
                Trigger("StartOfGame", card, card);
            }
    }

    /// <summary>
    /// 🔒 GameLogic.TriggerCardAbilityType : met en file les capacités et talents de la carte qui ont ce déclencheur.
    /// Une capacité ordinaire ne se déclenche pas si la carte est gelée ou réduite au silence ; un talent, si.
    /// </summary>
    private void Trigger(string trigger, CardState card, CardState? triggerer = null, bool inverse = false)
    {
        if (Phase == DuelPhase.Ended) return;
        foreach (var ab in card.Abilities.Where(a => a.Def.Trigger == trigger).ToList())
            if (!card.IsSilencedOrFrozen && TriggerConditionsMet(card, ab.Def, null, triggerer))
                Enqueue(new Ctx(ab.Def, card, null, true));
        // 🔒 Talent : chance (Game.CanCastTalent, conditions sur la carte elle-même), puis conditions
        // revérifiées sur la carte qui déclenche (TriggerCardAbilityCardTrigger).
        foreach (var t in card.Talents.Where(t => t.Def.Trigger == trigger).ToList())
            if (CanCastTalent(card, t) && TriggerConditionsMet(card, t.Def, t, triggerer))
                Enqueue(new Ctx(t.Def, card, null, true) { Talent = t, Inverse = inverse });
    }

    /// <summary>
    /// 🔒 Game.CanCastTalent : activations restantes, conditions de déclenchement, puis chance
    /// (100 − % de raté du talent), lissée comme l'Écho si le talent le demande.
    /// </summary>
    private bool CanCastTalent(CardState card, TalentState t)
    {
        if (t.ActivationsLeft is <= 0) return false;
        if (!TriggerConditionsMet(card, t.Def, t)) return false;
        var roll = _rng.NextDouble() * 100.0;
        var chance = 100.0 - t.Def.ChanceOfMiss;
        if (chance >= 100) return true;
        if (!t.Def.SmoothChance) return roll < chance;
        if (roll < chance + t.ChanceModifier)
        {
            t.ChanceModifier -= (100 - chance) * _s.EchoSmoothing;
            return true;
        }
        t.ChanceModifier += chance * _s.EchoSmoothing;
        Log.Add(Round, $"  Talent « {_kb.AbilityName(t.Def.Id)} » de {Name(card)} : pas de déclenchement cette fois.");
        return false;
    }

    // ================================================================ paliers de PV

    /// <summary>🔒 Card.HpPalierOf : 4 = PV pleins, 3 = plus de 75 %, 2 = plus de 50 %, 1 = plus de 25 %, 0 sinon.</summary>
    public static int HpPalierOf(int hp, int hpMax)
    {
        if (hpMax <= 0) return 0;
        var pct = (float)hp / hpMax * 100f;
        return pct >= 100f ? 4 : pct > 75f ? 3 : pct > 50f ? 2 : pct > 25f ? 1 : 0;
    }

    /// <summary>🔒 GameLogic.ManageHpPalierProc : déclenche « changement de palier » quand le palier change.</summary>
    private void CheckHpPalier(CardState card)
    {
        var old = card.HpPalier >= 0 ? card.HpPalier : 4;
        var now = HpPalierOf(card.Hp, card.HpMax);
        if (now == old) return;
        card.HpPalier = now;
        Trigger("OnHPPalierChange", card, card, inverse: now > old);
    }

    // ================================================================ Spamtons

    /// <summary>
    /// 🔒 Player.AddSpamton : jamais négatif ; déclenche « Spamtons modifiés » sur les cartes du joueur,
    /// sauf si c'est un talent de ce type qui les donne (pas de boucle).
    /// </summary>
    private void AddSpamtons(PlayerState p, int value, AbilityDef? source)
    {
        p.AddSpamtons(value);
        if (source?.Trigger != "OnSpamtonChange")
            foreach (var c in p.Board.ToList()) Trigger("OnSpamtonChange", c, c);
    }

    // ================================================================ Revanche

    /// <summary>
    /// 🔒 Card.Revanche : la carte applique immédiatement toutes ses capacités (de la dernière à la première)
    /// sur <paramref name="target"/>, sans recharge ni coût. Une capacité à choix prend une option au hasard.
    /// </summary>
    private int _revancheDepth;

    private void Revanche(CardState owner, CardState target)
    {
        // Garde-fou du simulateur : une capacité qui contiendrait elle-même « Revanche » se rappellerait sans fin
        // (aucune en 1.30 : seuls des talents l'utilisent, et ils ne font pas partie des capacités de la carte).
        if (_revancheDepth > 0) { Log.Add(Round, "  (Revanche imbriquée ignorée)"); return; }
        _revancheDepth++;
        try { RevancheAll(owner, target); }
        finally { _revancheDepth--; }
    }

    private void RevancheAll(CardState owner, CardState target)
    {
        Log.Add(Round, $"  Revanche de {Name(owner)} sur {Name(target)} !");
        for (var i = owner.Abilities.Count - 1; i >= 0; i--)
        {
            var def = owner.Abilities[i].Def;
            if (def.Target == "ChoiceSelector")
            {
                if (def.ChainAbilities.Count == 0) continue;
                var pick = def.ChainAbilities[_rng.Next(0, def.ChainAbilities.Count)];
                if (_kb.HasAbility(pick)) RevancheOne(_kb.Ability(pick), owner, target);
            }
            else RevancheOne(def, owner, target);
            if (Phase == DuelPhase.Ended) return;
        }
    }

    private void RevancheOne(AbilityDef def, CardState owner, CardState target)
    {
        var ctx = new Ctx(def, owner, def, true);
        switch (def.Target)
        {
            case "SelectTarget":
                if (target.Owner == owner.Owner)
                {
                    // Cible alliée : une cible possible est tirée au hasard.
                    var options = AllCards.Where(c => c.IsAlive && TargetConditionsMet(owner, def, c)).ToList();
                    if (options.Count == 0) return;
                    target = options[_rng.Next(0, options.Count)];
                }
                ResolveOnCard(ctx, target, setLastTarget: false);
                break;
            case "PlayerSelf":
            case "PlayerOpponent":
                ResolveOnPlayer(ctx, def.Target == "PlayerSelf" ? Players[owner.Owner] : Opponent(owner.Owner));
                break;
            case "Self":
                ResolveOnCard(ctx, owner, setLastTarget: false);
                break;
            case "AllCardsBoard":
                foreach (var t in CardTargets(owner, def))
                {
                    ResolveOnCard(ctx, t);
                    if (Phase == DuelPhase.Ended) return;
                    foreach (var id in def.ChainAbilities.Where(_kb.HasAbility))
                    {
                        var chain = _kb.Ability(id);
                        if (!owner.IsSilencedOrFrozen && TriggerConditionsMet(owner, chain))
                            Enqueue(new Ctx(chain, owner, def, true));
                    }
                }
                break;
        }
    }

    // ================================================================ gain de capacité

    /// <summary>
    /// 🔒 EffectReplaceLastAbility.GainAbility : la carte gagne la capacité ; si ses 3 emplacements sont pris,
    /// elle perd d'abord un Tox (lot C), sinon sa dernière capacité.
    /// </summary>
    private void GainAbility(CardState card, AbilityDef def, bool makeAvailable = false)
    {
        if (card.Abilities.Any(a => a.Def.Id == def.Id)) return;
        FreeSlot(card, def.Id);
        var gained = new AbilityState(def);
        if (makeAvailable) gained.CurrentCooldown = 0;
        card.Abilities.Add(gained);
        Log.Add(Round, $"  {Name(card)} gagne « {_kb.AbilityName(def.Id)} ».");
    }
}
