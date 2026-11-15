using SpamDrafter.Simulator.Data;

namespace SpamDrafter.Simulator.Engine;

/// <summary>Statuts gérés par le simulateur v1 (statuts principaux, REGLES_DU_DUEL §6).</summary>
public enum StatusKind
{
    Poisoned,
    Frozen,
    Silenced,
    Paralysed,
    /// <summary>Provocation (« Protection » dans le code du jeu) : rend les alliées « Protégées ».</summary>
    Taunt,
    Shield,
    Stealth,
    /// <summary>Fumée : la carte a été déplacée (Fumée de l'attaquant) ; permanent jusqu'à être dissipé.</summary>
    Smoked,
    Sleep,
    /// <summary>« SolRevealed » : une carte camouflée qui vient d'agir est révélée jusqu'à son prochain tour.</summary>
    Revealed,
}

/// <summary>Tox posé sur une carte (« RuntimeModificator » avec un ModificatorStatusData) ; « original » = passif inné.</summary>
public sealed record ToxState(ToxDef Def, bool IsOriginal);

/// <summary>Un statut posé sur une carte : valeur et durée (0 = permanent, §6.4).</summary>
public sealed class Status
{
    public StatusKind Kind { get; }
    public int Value { get; set; }
    public int Duration { get; set; }
    public bool Permanent { get; set; }
    /// <summary>Carte qui a posé le statut (poison, gel) ; null = la carte elle-même.</summary>
    public CardState? Caster { get; }

    public Status(StatusKind kind, int value, int duration, CardState? caster = null)
    {
        Kind = kind; Value = value; Duration = duration; Permanent = duration == 0; Caster = caster;
    }
}

/// <summary>
/// État d'une capacité pendant le duel (« AbilityRuntimeData » du jeu) : recharge, activations,
/// et bonus de statistiques propres à cette capacité sur cette carte.
/// </summary>
public sealed class AbilityState
{
    public AbilityDef Def { get; }
    public int CurrentCooldown { get; set; }
    public int MaxCooldown { get; set; }
    /// <summary>Activations restantes ; null = illimité.</summary>
    public int? ActivationsLeft { get; set; }

    // Bonus ajoutés aux valeurs de la capacité (🔒 Card.AddChanceCrit, AddChanceMiss, AddBaseDamage…).
    public double BonusCrit { get; set; }
    public double BonusMiss { get; set; }
    public double BonusCritRatio { get; set; }
    public double BaseDamageBonus { get; set; }
    public double DamageRatio { get; set; } = 1;
    public int BaseCooldownModificator { get; set; }
    /// <summary>Nombre de lancers résolus (« counterLaunched »).</summary>
    public int CounterLaunched { get; set; }
    /// <summary>Correction du hasard lissé de l'Écho (§8.6).</summary>
    public double EchoModifier { get; set; }

    // Mémoire du lancer en cours (conditions « was_critick », « was_notMiss ») ; remise à zéro après chaque lancer.
    public bool WasCrit { get; set; }
    public bool WasMiss { get; set; }
    public bool LastHitCrit { get; set; }
    /// <summary>🔒 « dontUpdateCooldownAfterUse » : Tox Reset après un ennemi tué, la recharge n'est pas remise.</summary>
    public bool DontUpdateCooldown { get; set; }

    public AbilityState(AbilityDef def)
    {
        Def = def;
        MaxCooldown = def.Cooldown;
        CurrentCooldown = def.Cooldown - 1;   // 🔒 Card.AddAbility : départ à « recharge − 1 »
        ActivationsLeft = def.NumberOfActivation > 0 ? def.NumberOfActivation : null;
    }

    /// <summary>Copie (phase 7) : tous les champs sont des valeurs ou des données fixes.</summary>
    internal AbilityState Clone() => (AbilityState)MemberwiseClone();

    /// <summary>🔒 Card.ResetForResurrection : état neuf.</summary>
    public void Reset()
    {
        MaxCooldown = Def.Cooldown;
        CurrentCooldown = Def.Cooldown - 1;
        ActivationsLeft = Def.NumberOfActivation > 0 ? Def.NumberOfActivation : null;
        BonusCrit = BonusMiss = BonusCritRatio = BaseDamageBonus = EchoModifier = 0;
        DamageRatio = 1;
        BaseCooldownModificator = CounterLaunched = 0;
        WasCrit = WasMiss = LastHitCrit = DontUpdateCooldown = false;
    }
}

/// <summary>
/// Talent attaché à une carte (« RuntimeModificator » du jeu) : activations restantes, correction du hasard lissé,
/// tour tiré au sort pour la condition « tour tiré ».
/// </summary>
public sealed class TalentState
{
    public AbilityDef Def { get; }
    public int? ActivationsLeft { get; set; }
    public double ChanceModifier { get; set; }
    public int DrawnTurn { get; set; }

    internal TalentState Clone() => (TalentState)MemberwiseClone();

    public TalentState(AbilityDef def)
    {
        Def = def;
        ActivationsLeft = def.NumberOfActivation > 0 ? def.NumberOfActivation : null;
    }
}

/// <summary>Statut de talent (« talentsModificator ») : type du Tox et sa valeur.</summary>
public sealed record TalentStatus(string Type, double Value);

/// <summary>Une carte sur le plateau.</summary>
public sealed class CardState
{
    public string Uid { get; }
    /// <summary>Carte d'origine ; change en cas de Métamorphose.</summary>
    public CardDef Def { get; set; }
    public int Owner { get; }
    /// <summary>Emplacement x = 1 à 4 (§1.2 : 1ʳᵉ carte du deck en x = 4).</summary>
    public int Slot { get; set; }
    public string Element { get; set; }
    /// <summary>PV max (« hp » dans le code du jeu).</summary>
    public int HpMax { get; set; }
    /// <summary>Dégâts subis (« damage » dans le code du jeu).</summary>
    public int Damage { get; set; }
    public int Hp => Math.Max(HpMax - Damage, 0);
    public bool IsAlive => Hp > 0;
    public Dictionary<string, int> Resistances { get; }
    public List<AbilityState> Abilities { get; }
    public List<TalentState> Talents { get; } = new();
    /// <summary>Tox posés sur la carte, dans l'ordre de pose (le dernier est retiré en premier).</summary>
    public List<ToxState> Toxes { get; } = new();

    public ToxDef? Tox(string type) => Toxes.FirstOrDefault(t => t.Def.StatusType == type)?.Def;
    public bool HasTox(string type) => Toxes.Any(t => t.Def.StatusType == type);
    public List<TalentStatus> TalentStatuses { get; } = new();
    /// <summary>Palier de PV (0 à 4) ; −1 = jamais calculé (compte comme 4).</summary>
    public int HpPalier { get; set; } = -1;
    /// <summary>Lignes « seuil de PV » actives des talents (clé : talent#action).</summary>
    public Dictionary<string, bool> HpPalierLines { get; } = new();

    public double TalentStatusValue(string type) => TalentStatuses.Where(t => t.Type == type).Sum(t => t.Value);
    public bool HasTalentStatus(string type) => TalentStatuses.Any(t => t.Type == type);
    public List<Status> Statuses { get; } = new();
    public bool Exhausted { get; set; }
    /// <summary>La mort de la carte a déjà été traitée (« hasProcDeathPassives »).</summary>
    public bool DeathProcessed { get; set; }
    public CardState? Killer { get; set; }
    /// <summary>Revenu ajouté par des effets (« coinsGeneration »), jamais négatif.</summary>
    public int IncomeBonus { get; set; }
    public int Income => IncomeBonus + Def.GoldIncome;

    /// <summary>
    /// 🔒 Card.GetAbilitiesRuntimeID : une capacité enchaînée nommée « Echo&lt;nom&gt;_n » partage les
    /// statistiques de la capacité « &lt;nom&gt; » de la carte. Null si la carte n'a pas cette capacité.
    /// </summary>
    public AbilityState? Runtime(string abilityId)
    {
        var id = abilityId;
        if (id.StartsWith("Echo", StringComparison.Ordinal))
        {
            var cut = id.IndexOf('_');
            id = cut == -1 ? id[4..] : id[4..cut];
        }
        return Abilities.FirstOrDefault(a => a.Def.Id == id);
    }

    public CardState(string uid, CardDef def, int owner, int slot, IEnumerable<AbilityState> abilities)
    {
        Uid = uid; Def = def; Owner = owner; Slot = slot;
        Element = def.Element;
        HpMax = def.Hp;
        Resistances = new Dictionary<string, int>(def.Resistances);
        Abilities = abilities.ToList();
    }

    /// <summary>Copie (phase 7) : les statuts et le tueur pointent encore vers les cartes d'origine (voir FixLinks).</summary>
    internal CardState(CardState o)
    {
        Uid = o.Uid; Def = o.Def; Owner = o.Owner; Slot = o.Slot; Element = o.Element;
        HpMax = o.HpMax; Damage = o.Damage;
        Resistances = new Dictionary<string, int>(o.Resistances);
        Abilities = o.Abilities.Select(a => a.Clone()).ToList();
        Talents.AddRange(o.Talents.Select(t => t.Clone()));
        Toxes.AddRange(o.Toxes);
        TalentStatuses.AddRange(o.TalentStatuses);
        HpPalier = o.HpPalier;
        foreach (var (k, v) in o.HpPalierLines) HpPalierLines[k] = v;
        Statuses.AddRange(o.Statuses);
        Exhausted = o.Exhausted; DeathProcessed = o.DeathProcessed; Killer = o.Killer; IncomeBonus = o.IncomeBonus;
    }

    /// <summary>Relie la copie aux cartes copiées (lanceur des statuts, tueur).</summary>
    internal void FixLinks(Func<CardState?, CardState?> map)
    {
        for (var i = 0; i < Statuses.Count; i++)
        {
            var s = Statuses[i];
            Statuses[i] = new Status(s.Kind, s.Value, s.Duration, map(s.Caster)) { Permanent = s.Permanent };
        }
        Killer = map(Killer);
    }

    /// <summary>🔒 Card.ResetForResurrection : PV, élément, résistances, statuts et capacités d'origine.</summary>
    public void ResetForResurrection()
    {
        Statuses.Clear();
        Exhausted = false;
        Damage = 0;
        Element = Def.Element;
        HpMax = Def.Hp;
        Resistances.Clear();
        foreach (var (k, v) in Def.Resistances) Resistances[k] = v;
        foreach (var a in Abilities) a.Reset();
        HpPalier = -1;
        HpPalierLines.Clear();
        DeathProcessed = false;
        Killer = null;
    }

    public bool Has(StatusKind kind) => Statuses.Any(s => s.Kind == kind);
    public Status? Get(StatusKind kind) => Statuses.FirstOrDefault(s => s.Kind == kind);
    public int StatusValue(StatusKind kind) => Statuses.Where(s => s.Kind == kind).Sum(s => s.Value);
    public void Remove(StatusKind kind) => Statuses.RemoveAll(s => s.Kind == kind);

    /// <summary>
    /// Ajoute un statut (🔒 §6.4) : un statut déjà présent cumule sa valeur et garde la durée la plus longue,
    /// sauf le Poison, qui s'empile (une entrée par application).
    /// </summary>
    public void AddStatus(StatusKind kind, int value, int duration, CardState? caster = null)
    {
        var existing = kind == StatusKind.Poisoned ? null : Get(kind);
        if (existing is null)
        {
            Statuses.Add(new Status(kind, value, duration, caster));
            return;
        }
        existing.Value += value;
        existing.Duration = Math.Max(existing.Duration, duration);
        existing.Permanent = existing.Permanent || duration == 0;
    }

    /// <summary>🔒 Card.IsCamouflaged : Tox Camouflage, pas révélée, et ne provoque pas.</summary>
    public bool IsCamouflaged => HasTox("SolCamouflage") && !Has(StatusKind.Revealed) && !Has(StatusKind.Taunt);

    /// <summary>
    /// 🔒 Card.IsSmokeHidden : carte enfumée, sans Provocation et non camouflée. Dans le jeu, son identité est masquée
    /// pour l'adversaire (🔒 HiddenInfoMask) : le Grand Joueur ne doit pas s'en servir (KI-030).
    /// </summary>
    public bool IsSmokeHidden => Has(StatusKind.Smoked) && !Has(StatusKind.Taunt) && !IsCamouflaged;

    /// <summary>🔒 Card.IsSilencedOrFrozen.</summary>
    public bool IsSilencedOrFrozen => Has(StatusKind.Silenced) || Has(StatusKind.Frozen);

    /// <summary>🔒 Card.CanDoAbilities : ni réduite au silence, ni gelée.</summary>
    public bool CanDoAbilities => !IsSilencedOrFrozen;

    /// <summary>🔒 Card.CanDoActivatedAbilities : ni paralysée, ni réduite au silence, ni gelée.</summary>
    public bool CanDoActivatedAbilities =>
        !Has(StatusKind.Paralysed) && !Has(StatusKind.Silenced) && !Has(StatusKind.Frozen);

    public override string ToString() => $"{Def.SanitizedName}#{Uid}";
}

/// <summary>Un joueur : plateau, Spamtons, chance, niveaux d'élément.</summary>
public sealed class PlayerState
{
    public int Id { get; }
    /// <summary>
    /// 🔒 Les conditions « ai_is_enemy » / « ai_is_ally » (ConditionOwnerAI) ne s'appliquent qu'aux joueurs IA du jeu.
    /// Un humain peut, par exemple, viser sa propre carte (ex. Corne 75 pour remonter une alliée à 75 % de ses PV).
    /// Faux par défaut depuis la phase 8 (D-053) : les duels simulés représentent des parties classées entre humains
    /// (210 capacités étaient restreintes à tort quand c'était vrai).
    /// </summary>
    public bool IsAi { get; set; }
    public List<CardState> Board { get; } = new();
    public List<CardState> Discard { get; } = new();
    public int Spamtons { get; private set; }
    public double Luck { get; set; }
    public int KillCount { get; set; }
    /// <summary>Niveau d'élément par élément (+10 % de dégâts par niveau, §5.1).</summary>
    public Dictionary<string, double> ElementLevels { get; } = new();

    public PlayerState(int id) { Id = id; }

    /// <summary>Copie (phase 7) sans les cartes : elles sont ajoutées par Duel.Clone.</summary>
    internal PlayerState CloneEmpty()
    {
        var p = new PlayerState(Id) { IsAi = IsAi, Spamtons = Spamtons, Luck = Luck, KillCount = KillCount };
        foreach (var (k, v) in ElementLevels) p.ElementLevels[k] = v;
        return p;
    }

    /// <summary>🔒 Player.AddSpamton : jamais négatif.</summary>
    public void AddSpamtons(int value) => Spamtons = Math.Max(0, Spamtons + value);

    public double Level(string element) => ElementLevels.GetValueOrDefault(element);
}
