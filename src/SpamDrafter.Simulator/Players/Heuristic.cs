using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;

namespace SpamDrafter.Simulator.Players;

/// <summary>
/// Poids de la note de position. Ce sont des réglages DE L'IA (choisis et ajustés par mesure au banc d'essai),
/// pas des réglages du jeu : la règle « ne jamais coder en dur un réglage du jeu » ne s'y applique pas (D-046).
/// </summary>
public sealed record HeuristicWeights
{
    /// <summary>Valeur d'une carte en vie (en plus de ses PV) : tuer une carte rapporte beaucoup.</summary>
    public double CardAlive { get; init; } = 300;
    /// <summary>
    /// Valeur supplémentaire d'une carte selon sa santé (PV / PV max) puissance <see cref="FocusExponent"/> :
    /// avec un exposant inférieur à 1, un coup sur une carte déjà abîmée vaut plus (pousse à finir les cartes).
    /// </summary>
    public double Focus { get; init; } = 600;
    public double FocusExponent { get; init; } = 0.5;
    /// <summary>
    /// Danger d'une carte : la partie « concentration » (<see cref="Focus"/>) est multipliée par
    /// 1 + Threat × (dégâts moyens par tour de ses capacités / 100). Pousse à abattre d'abord les cartes qui frappent fort.
    /// </summary>
    public double Threat { get; init; } = 0;
    /// <summary>Valeur d'un PV.</summary>
    public double Hp { get; init; } = 1;
    /// <summary>
    /// Part des résistances dans la valeur des PV (0 = ignorées, 1 = PV effectifs : PV ÷ (1 − résistance moyenne)).
    /// </summary>
    public double Resistance { get; init; } = 0;
    /// <summary>Valeur d'un point de bonus de dégâts de base sur une capacité (bonus durable, « investissement »).</summary>
    public double DamageBonus { get; init; } = 0;
    /// <summary>Valeur d'un point de bouclier.</summary>
    public double Shield { get; init; } = 0.8;
    /// <summary>Valeur d'une capacité sur une carte en vie.</summary>
    public double Ability { get; init; } = 250;
    /// <summary>Valeur d'un Tox selon sa rareté (commun, rare, épique, légendaire, autre).</summary>
    public double[] ToxByRarity { get; init; } = [30, 70, 120, 185, 40];
    /// <summary>
    /// Si positif, la valeur d'un Tox vient de son prix plutôt que de sa rareté : ToxCostFactor × prix + ToxCostBase
    /// (le prix reflète la force du Tox).
    /// </summary>
    public double ToxCostFactor { get; init; } = 0;
    public double ToxCostBase { get; init; } = 20;
    /// <summary>Valeur d'un Spamton.</summary>
    public double Spamton { get; init; } = 1;
    /// <summary>Valeur d'un point de revenu par tour (Spamtons gagnés à chaque tour).</summary>
    public double Income { get; init; } = 4;
    /// <summary>Valeur d'un point de chance.</summary>
    public double Luck { get; init; } = 2;
    /// <summary>Valeur d'un niveau d'élément (+10 % de dégâts de cet élément).</summary>
    public double ElementLevel { get; init; } = 10;
    /// <summary>
    /// Phase 8, lot H : nombre de tours pendant lesquels un niveau d'élément profite aux dégâts de MES cartes vivantes
    /// dans cet élément (🔒 +10 % par niveau). 0 = ancienne note (tous les niveaux se valent).
    /// </summary>
    public double ElementUse { get; init; } = 3;
    /// <summary>
    /// Phase 8, lot H : nombre de tours pendant lesquels le critique gagné (chance ou puissance, au-delà des valeurs de base)
    /// profite aux dégâts de la capacité. 0 = ancienne note.
    /// </summary>
    public double CritUse { get; init; } = 3;
    /// <summary>
    /// Phase 8, lot H (audit) : nombre de tours pendant lesquels un bonus PERMANENT de dégâts de base d'une capacité
    /// (ex. Explosion : +10 après chaque utilisation) rapporte, à raison d'un lancer par recharge. 0 = ignoré
    /// (l'IA refusait alors d'investir : −100 PV pour 10 dégâts tout de suite).
    /// </summary>
    public double BaseDamageUse { get; init; } = 6;
    /// <summary>Pénalité d'un statut qui empêche d'agir (gel, silence, paralysie, sommeil).</summary>
    public double Disabled { get; init; } = 90;
    /// <summary>Bonus d'une protection (Provocation, Furtivité).</summary>
    public double Protection { get; init; } = 25;
    /// <summary>
    /// Tolérance d'un lancer : il est joué même s'il fait baisser la note de moins que cette valeur
    /// (un lancer a souvent des effets que la note ne voit pas tout de suite ; ne pas l'utiliser le perd).
    /// </summary>
    public double CastTolerance { get; init; } = 30;
    /// <summary>Agressivité : la valeur du camp adverse compte × Aggression (1 = neutre ; plus = viser la destruction).</summary>
    public double Aggression { get; init; } = 1;
    /// <summary>Valeur d'une victoire (domine tout le reste).</summary>
    public double Win { get; init; } = 1_000_000;

    public static HeuristicWeights Default { get; } = new();
    /// <summary>Note de la phase 7 (avant le lot H), gardée pour comparer.</summary>
    public static HeuristicWeights V1 { get; } = new() { ElementUse = 0, CritUse = 0, BaseDamageUse = 0 };
}

/// <summary>
/// Note de position (phase 7, lot B) : un nombre qui dit si la partie va bien pour un joueur.
/// Positif = avantage pour <c>me</c>. Somme « mes cartes − cartes adverses » + ressources du joueur.
/// </summary>
public static class Evaluator
{
    public static double Score(Duel duel, int me, HeuristicWeights? weights = null)
    {
        var w = weights ?? HeuristicWeights.Default;
        if (duel.Phase == DuelPhase.Ended)
            return duel.Winner == me ? w.Win : duel.Winner == 1 - me ? -w.Win : 0;
        return Side(duel, duel.Players[me], w) - w.Aggression * Side(duel, duel.Players[1 - me], w);
    }

    private static double Side(Duel duel, PlayerState p, HeuristicWeights w)
    {
        var v = p.Spamtons * w.Spamton + p.Luck * w.Luck + p.ElementLevels.Values.Sum() * w.ElementLevel;
        foreach (var c in p.Board)
        {
            if (!c.IsAlive) continue;
            v += Card(duel.Knowledge, c, w);
        }
        if (w.ElementUse > 0 || w.CritUse > 0 || w.BaseDamageUse > 0) v += Combo(duel.Knowledge, p, w);
        return v;
    }

    /// <summary>
    /// Phase 8, lot H : ce que valent les niveaux d'élément et le critique accumulés pour CE joueur, en dégâts futurs
    /// (équivalents PV) : un niveau de lumière ne vaut rien sans carte vivante qui attaque en lumière.
    /// </summary>
    private static double Combo(SimKnowledge kb, PlayerState p, HeuristicWeights w)
    {
        Dictionary<string, double>? use = null;
        var crit = 0.0;
        var baseBonus = 0.0;
        foreach (var c in p.Board)
        {
            if (!c.IsAlive) continue;
            foreach (var rt in c.Abilities)
            {
                if (w.BaseDamageUse > 0 && rt.BaseDamageBonus > 0 && rt.Def.IsActive)
                {
                    // 🔒 Le bonus s'ajoute à chaque action de dégâts, avant le bonus d'élément (§5.2).
                    foreach (var (el, _) in ElementDamage(kb, rt.Def))
                    {
                        var hits = rt.Def.Actions.Count(x => (x.Element ?? rt.Def.Element) == el && kb.Effect(x.Effect)?.Class is { } cls && cls.Contains("Damage"));
                        baseBonus += rt.BaseDamageBonus * hits * (1 + 0.1 * Math.Max(0, p.Level(el))) / Math.Max(1, rt.Def.Cooldown);
                    }
                }
                if (w.ElementUse > 0)
                    foreach (var (el, dpt) in ElementDamage(kb, rt.Def))
                    {
                        use ??= new Dictionary<string, double>();
                        use[el] = use.GetValueOrDefault(el) + dpt;
                    }
                if (w.CritUse > 0 && (rt.BonusCrit != 0 || rt.BonusCritRatio != 0))
                {
                    var dpt = DamagePerTurn(kb, rt.Def);
                    if (dpt <= 0) continue;
                    var baseChance = Math.Clamp(rt.Def.ChanceOfCrit, 0, 100) / 100;
                    var chance = Math.Clamp(rt.Def.ChanceOfCrit + rt.BonusCrit, 0, 100) / 100;
                    var ratio = Math.Max(1, rt.Def.CritDamageRatio + rt.BonusCritRatio);
                    // Gain d'espérance de dégâts par tour dû aux bonus de critique (au-delà de la carte d'origine).
                    crit += (chance * (ratio - 1) - baseChance * (rt.Def.CritDamageRatio - 1)) * dpt;
                }
            }
        }
        var v = crit * w.CritUse + baseBonus * w.BaseDamageUse;
        if (use is not null)
            foreach (var (el, level) in p.ElementLevels)
                if (level > 0 && use.TryGetValue(el, out var dpt)) v += level * 0.1 * dpt * w.ElementUse;
        return v;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<AbilityDef, KeyValuePair<string, double>[]> ElementDamageCache =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Dégâts moyens par tour d'une capacité, par élément (actions de dégâts dont l'élément est connu).</summary>
    public static KeyValuePair<string, double>[] ElementDamage(SimKnowledge kb, AbilityDef a) => ElementDamageCache.GetOrAdd(a, def =>
    {
        if (!def.IsActive) return [];
        return def.Actions.Where(x => kb.Effect(x.Effect)?.Class is { } cls && cls.Contains("Damage") && (x.Element ?? def.Element) is not null)
            .GroupBy(x => (x.Element ?? def.Element)!)
            .Select(g => new KeyValuePair<string, double>(g.Key, g.Sum(x => (x.Min + x.Max) / 2) / Math.Max(1, def.Cooldown)))
            .ToArray();
    });

    public static double Card(SimKnowledge kb, CardState c, HeuristicWeights w)
    {
        var ehp = (double)c.Hp;
        if (w.Resistance > 0 && c.Resistances.Count > 0)
        {
            var res = Math.Clamp(c.Resistances.Values.Average(), -50, 50) / 100.0;
            if (c.HasTalentStatus("Paradoxe")) res = -res;
            ehp = c.Hp * (1 + w.Resistance * (1 / (1 - res) - 1));
        }
        var v = w.CardAlive + ehp * w.Hp + c.Income * w.Income + c.Abilities.Count * w.Ability;
        if (w.DamageBonus != 0) v += c.Abilities.Sum(a => a.BaseDamageBonus) * w.DamageBonus;
        var health = c.HpMax > 0 ? Math.Clamp((double)c.Hp / c.HpMax, 0, 1) : 0;
        var danger = w.Threat == 0 ? 0 : c.Abilities.Sum(a => DamagePerTurn(kb, a.Def));
        v += w.Focus * (1 + w.Threat * danger / 100) * Math.Pow(health, w.FocusExponent);
        foreach (var t in c.Toxes) v += ToxValue(t.Def, w) * (0.5 + 0.5 * health);   // un Tox sert moins sur une carte mourante
        foreach (var s in c.Statuses)
        {
            switch (s.Kind)
            {
                case StatusKind.Shield: v += s.Value * w.Shield; break;
                case StatusKind.Poisoned: v -= Math.Min(c.Hp, s.Value * Math.Max(1, (s.Duration + 1) / 2)) * w.Hp; break;
                case StatusKind.Frozen or StatusKind.Silenced or StatusKind.Paralysed or StatusKind.Sleep: v -= w.Disabled; break;
                case StatusKind.Taunt or StatusKind.Stealth: v += w.Protection; break;
            }
        }
        return v;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<AbilityDef, double> DamageCache =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Dégâts moyens par tour d'une capacité : moyenne des actions de dégâts, divisée par sa recharge.</summary>
    public static double DamagePerTurn(SimKnowledge kb, AbilityDef a) => DamageCache.GetOrAdd(a, def =>
    {
        if (!def.IsActive) return 0;
        var dmg = def.Actions.Where(x => kb.Effect(x.Effect)?.Class is { } cls && cls.Contains("Damage"))
            .Sum(x => (x.Min + x.Max) / 2);
        return dmg / Math.Max(1, def.Cooldown);
    });

    public static double ToxValue(ToxDef t, HeuristicWeights w) => w.ToxCostFactor > 0 ? w.ToxCostFactor * t.Cost + w.ToxCostBase : t.Rarity switch
    {
        "rarity-common" => w.ToxByRarity[0],
        "rarity-rare" => w.ToxByRarity[1],
        "rarity-epic" => w.ToxByRarity[2],
        "rarity-legendary" => w.ToxByRarity[3],
        _ => w.ToxByRarity[4],
    };
}

/// <summary>
/// Choix PENDANT un lancer, par des règles simples (on ne peut pas copier une partie au milieu d'un lancer) :
/// une capacité qui soigne ou protège vise l'alliée la plus abîmée ; les autres visent l'ennemie la plus faible
/// parmi celles que le joueur peut voir (Fumée : KI-030).
/// </summary>
public sealed class RuleDecider : IDuelDecider
{
    private readonly Rng _rng;
    public RuleDecider(ulong seed) { _rng = new Rng(seed); }

    private static readonly HashSet<string> FriendlyClasses =
    [
        "EffectHeal", "EffectHealPVMAX", "EffectCleanse", "EffectAddSpamtonGeneration",
    ];

    /// <summary>Vrai si la capacité aide sa cible (soin, nettoyage…) et ne lui fait pas de dégâts.</summary>
    public static bool IsFriendly(SimKnowledge kb, AbilityDef ability)
    {
        var classes = ability.Actions.Select(a => kb.Effect(a.Effect)?.Class).Where(c => c is not null).ToList();
        var friendly = classes.Any(c => FriendlyClasses.Contains(c!));
        var hostile = classes.Any(c => c!.Contains("Damage") || c == "EffectDestroy");
        return friendly && !hostile;
    }

    public CardState ChooseTarget(Duel duel, CardState caster, AbilityDef ability, IReadOnlyList<CardState> targets)
    {
        if (IsFriendly(duel.Knowledge, ability))
        {
            var allies = targets.Where(t => t.Owner == caster.Owner).ToList();
            if (allies.Count > 0) return allies.MaxBy(t => t.Damage)!;
        }
        var foes = targets.Where(t => t.Owner != caster.Owner).ToList();
        var visible = foes.Where(t => !t.IsSmokeHidden).ToList();
        if (visible.Count > 0) return visible.MinBy(t => t.Hp)!;
        if (foes.Count > 0) return foes[_rng.Next(0, foes.Count)];    // cartes cachées : au hasard
        return targets[_rng.Next(0, targets.Count)];
    }

    /// <summary>Choix multiple : l'option la plus « forte » (somme des valeurs maximales de ses actions).</summary>
    public int ChooseOption(Duel duel, CardState caster, AbilityDef ability, IReadOnlyList<AbilityDef> options)
    {
        var best = 0;
        for (var i = 1; i < options.Count; i++)
            if (Power(options[i]) > Power(options[best])) best = i;
        return best;
    }

    private static double Power(AbilityDef a) => a.Actions.Sum(x => Math.Abs(x.Max));

    public int ChooseAmong(Duel duel, CardState card, string kind, IReadOnlyList<string> options) =>
        _rng.Next(0, options.Count);
}

/// <summary>
/// Grand Joueur heuristique (phase 7, lot B). À chaque décision, il essaie chaque action possible sur une copie de
/// la partie (avec un hasard à lui : il ne connaît pas les vrais tirages à venir), note la position obtenue
/// et garde la meilleure. Il finit son tour quand plus aucune action n'est utile.
/// Information cachée (KI-030) : il ne distingue pas les cartes ennemies cachées par la Fumée ; viser l'une d'elles
/// est noté comme la moyenne sur toutes, et la carte visée est tirée au hasard parmi elles.
/// </summary>
public sealed class HeuristicPlayer : IDuelPlayer
{
    private readonly Rng _rng;
    private readonly RuleDecider _rules;
    internal RuleDecider Rules => _rules;
    /// <summary>Poids utilisés : ceux donnés au joueur, sinon ceux des données (meta/joueur.json), sinon les intégrés.</summary>
    internal HeuristicWeights WeightsFor(SimKnowledge kb) => _explicit ?? kb.PlayerWeights ?? HeuristicWeights.Default;
    private readonly HeuristicWeights? _explicit;
    private readonly int _samples;

    /// <param name="samples">Nombre d'essais par action (hasards différents) : plus = plus juste, mais plus lent.</param>
    public HeuristicPlayer(ulong seed, HeuristicWeights? weights = null, int samples = 2)
    {
        _rng = new Rng(seed);
        _rules = new RuleDecider(seed ^ 0x5DEECE66DUL);
        _explicit = weights;
        _samples = Math.Max(1, samples);
    }

    /// <summary>Classement des actions par la note d'un coup à l'avance (utilisé aussi par le joueur par recherche).</summary>
    internal sealed record Ranking(double Baseline, double[] Scores, Plan?[] Plans, Dictionary<int, List<int>> HiddenGroups,
        bool[] Playable);

    internal Ranking Rank(Duel duel, IReadOnlyList<DuelAction> options)
    {
        var me = duel.CurrentPlayer;
        var w = WeightsFor(duel.Knowledge);
        var baseline = Evaluator.Score(duel, me, w);

        var scores = new double[options.Count];
        var plans = new Plan?[options.Count];
        for (var i = 0; i < options.Count; i++) (scores[i], plans[i]) = Evaluate(duel, options[i], me);

        // Fumée : les actions qui ne diffèrent que par la carte cachée visée reçoivent la même note (la moyenne).
        var groups = Enumerable.Range(0, options.Count)
            .Where(i => options[i].Cast is { Target: { } t } && t.Owner != me && t.IsSmokeHidden)
            .GroupBy(i => (options[i].Cast!.Card, options[i].Cast!.Ability, options[i].Cast!.Origin));
        var hiddenGroup = new Dictionary<int, List<int>>();
        foreach (var g in groups)
        {
            var list = g.ToList();
            var avg = list.Average(i => scores[i]);
            foreach (var i in list) { scores[i] = avg; hiddenGroup[i] = list; }
        }

        // Un lancer qui ne fait pas (trop) baisser la note est jouable : ne pas l'utiliser le perd pour ce tour ;
        // une action de boutique doit améliorer la note.
        var playable = new bool[options.Count];
        for (var i = 0; i < options.Count; i++)
        {
            var threshold = options[i].Kind == ActionKind.Cast ? baseline - w.CastTolerance - 1e-6 : baseline + 1e-6;
            playable[i] = scores[i] >= threshold;
        }
        return new Ranking(baseline, scores, plans, hiddenGroup, playable);
    }

    public DuelAction? Choose(Duel duel, IReadOnlyList<DuelAction> options)
    {
        _plan = null;
        if (options.Count == 0) return null;
        var r = Rank(duel, options);
        var best = -1;
        for (var i = 0; i < options.Count; i++)
            if (r.Playable[i] && (best < 0 || r.Scores[i] > r.Scores[best])) best = i;
        if (best < 0) return null;
        return Commit(options, r, best);
    }

    /// <summary>Retient l'action choisie (carte cachée tirée au hasard dans son groupe) et son plan.</summary>
    internal DuelAction Commit(IReadOnlyList<DuelAction> options, Ranking r, int best)
    {
        if (r.HiddenGroups.TryGetValue(best, out var hidden)) best = hidden[_rng.Next(0, hidden.Count)];
        _plan = r.Plans[best];
        return options[best];
    }

    internal void ClearPlan() => _plan = null;

    /// <summary>Réponse prévue à la 1ʳᵉ question posée pendant le lancer choisi (null = règles simples).</summary>
    internal sealed record Plan(int Index, bool Hidden);

    private Plan? _plan;

    /// <summary>
    /// Note d'une action. Si elle pose une question pendant sa résolution (cible d'un enchaînement, option d'un
    /// choix multiple, Métamorphose, Découverte), chaque réponse à la 1ʳᵉ question est essayée ; la meilleure est
    /// retenue comme plan. Réponses « carte ennemie cachée par la Fumée » : notées ensemble (moyenne).
    /// </summary>
    private (double Score, Plan? Plan) Evaluate(Duel duel, DuelAction action, int me)
    {
        var (score, first) = Try(duel, action, me, null);
        if (first is not { Count: > 1 } q) return (score, null);
        var byAnswer = new double[q.Count];
        for (var k = 0; k < q.Count; k++) byAnswer[k] = Try(duel, action, me, k).Score;
        var hiddenIdx = Enumerable.Range(0, q.Count).Where(k => q.Hidden[k]).ToList();
        if (hiddenIdx.Count > 0)
        {
            var avg = hiddenIdx.Average(k => byAnswer[k]);
            foreach (var k in hiddenIdx) byAnswer[k] = avg;
        }
        var best = 0;
        for (var k = 1; k < q.Count; k++) if (byAnswer[k] > byAnswer[best]) best = k;
        return (byAnswer[best], new Plan(best, q.Hidden[best]));
    }

    /// <summary>Première question posée pendant un essai : nombre de réponses et réponses « cachées ».</summary>
    internal sealed record Question(int Count, bool[] Hidden);

    private (double Score, Question? First) Try(Duel duel, DuelAction action, int me, int? forced)
    {
        var total = 0.0;
        var w = WeightsFor(duel.Knowledge);
        Question? first = null;
        for (var s = 0; s < _samples; s++)
        {
            var copy = duel.Clone(reseed: _rng.NextULong());
            var script = new Script(_rules, me, forced);
            copy.Deciders[me] = script;
            copy.Deciders[1 - me] = _rules;
            copy.Apply(copy.Resolve(duel.RefOf(action)));
            total += Evaluator.Score(copy, me, w);
            first ??= script.First;
        }
        return (total / _samples, first);
    }

    /// <summary>Décideur des essais : impose la réponse à la 1ʳᵉ question (si demandée), puis applique les règles.</summary>
    internal sealed class Script(RuleDecider rules, int me, int? forced) : IDuelDecider
    {
        public Question? First { get; private set; }
        private int _asked;

        private int Pick(int count, bool[] hidden, Func<int> rule)
        {
            if (_asked++ > 0) return rule();
            First = new Question(count, hidden);
            return forced is { } k && k < count ? k : rule();
        }

        public CardState ChooseTarget(Duel duel, CardState caster, AbilityDef ability, IReadOnlyList<CardState> targets)
        {
            var hidden = targets.Select(t => t.Owner != me && t.IsSmokeHidden).ToArray();
            var k = Pick(targets.Count, hidden, () => IndexOf(targets, rules.ChooseTarget(duel, caster, ability, targets)));
            return targets[k];
        }

        public int ChooseOption(Duel duel, CardState caster, AbilityDef ability, IReadOnlyList<AbilityDef> options) =>
            Pick(options.Count, new bool[options.Count], () => rules.ChooseOption(duel, caster, ability, options));

        public int ChooseAmong(Duel duel, CardState card, string kind, IReadOnlyList<string> options) =>
            Pick(options.Count, new bool[options.Count], () => rules.ChooseAmong(duel, card, kind, options));
    }

    private static int IndexOf(IReadOnlyList<CardState> list, CardState c)
    {
        for (var i = 0; i < list.Count; i++) if (ReferenceEquals(list[i], c)) return i;
        return 0;
    }

    /// <summary>Applique le plan à la 1ʳᵉ question réelle (s'il est encore valable), sinon les règles.</summary>
    private int? TakePlan(int count)
    {
        var plan = _plan;
        _plan = null;
        return plan is { } p && p.Index < count ? p.Index : null;
    }

    public CardState ChooseTarget(Duel duel, CardState caster, AbilityDef ability, IReadOnlyList<CardState> targets)
    {
        var hiddenPlan = _plan is { Hidden: true };
        if (TakePlan(targets.Count) is { } k)
        {
            if (!hiddenPlan) return targets[k];
            // Plan « une carte cachée » : on ne sait pas laquelle est laquelle, on en prend une au hasard.
            var hidden = targets.Where(t => t.Owner != caster.Owner && t.IsSmokeHidden).ToList();
            if (hidden.Count > 0) return hidden[_rng.Next(0, hidden.Count)];
        }
        return _rules.ChooseTarget(duel, caster, ability, targets);
    }

    public int ChooseOption(Duel duel, CardState caster, AbilityDef ability, IReadOnlyList<AbilityDef> options) =>
        TakePlan(options.Count) ?? _rules.ChooseOption(duel, caster, ability, options);

    public int ChooseAmong(Duel duel, CardState card, string kind, IReadOnlyList<string> options) =>
        TakePlan(options.Count) ?? _rules.ChooseAmong(duel, card, kind, options);
}
