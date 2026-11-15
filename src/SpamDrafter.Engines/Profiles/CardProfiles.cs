using System.Collections.Concurrent;
using SpamDrafter.Simulator.Data;

namespace SpamDrafter.Engines.Profiles;

/// <summary>
/// Profil d'une carte (phase 8, lot G, D-050), lu dans les données du jeu : ce qu'elle UTILISE et ce qu'elle DONNE.
/// Les codes de statistique viennent du code du jeu (🔒 EffectAddStat / EffectAddAllStat, « StatToModify ») :
/// 1 = niveau d'élément, 25 = chance de critique, 40 = puissance critique. Les valeurs viennent des cartes (🃏).
/// </summary>
/// <param name="Uses">Éléments de ses attaques et soins : part de chaque élément (somme 1).</param>
/// <param name="Generates">Niveaux d'élément donnés à son joueur, par tour de recharge (« * » = tous les éléments).</param>
/// <param name="CritRatio">Plus grand multiplicateur critique de ses attaques (ex. 3 = ×3).</param>
/// <param name="CritChance">Plus grande chance de critique de base de ses attaques (%).</param>
/// <param name="GivesCrit">Chance de critique donnée (somme des bonus positifs, %).</param>
/// <param name="GivesCritRatio">Puissance critique donnée (somme des bonus positifs).</param>
public sealed record CardProfile(string CardId, string Element, IReadOnlyDictionary<string, double> Uses,
    IReadOnlyDictionary<string, double> Generates, double CritRatio, double CritChance, double GivesCrit, double GivesCritRatio)
{
    public double TotalGenerated => Generates.Values.Sum();
}

/// <summary>Profils de toutes les cartes et affinités entre cartes (combien une carte en aide une autre).</summary>
public sealed class CardProfiles
{
    private static readonly ConcurrentDictionary<SimKnowledge, CardProfiles> Cache = new();

    private readonly Dictionary<string, CardProfile> _cards;
    private readonly Dictionary<string, CardProfile> _talents;

    public static CardProfiles For(SimKnowledge kb) => Cache.GetOrAdd(kb, k => new CardProfiles(k));

    private CardProfiles(SimKnowledge kb)
    {
        _cards = kb.Cards.ToDictionary(c => c.Id, c => Build(kb, c.Id, c.Element, c.Abilities));
        _talents = kb.Talents.ToDictionary(t => t.Id, t => Build(kb, t.Id, "", [t.Id]));
    }

    public CardProfile Card(string cardId) => _cards[cardId];
    /// <summary>Ce que le talent ajoute à la carte (mêmes rubriques), ou null.</summary>
    public CardProfile? Talent(string? talentId) => talentId is not null && _talents.TryGetValue(talentId, out var p) ? p : null;

    /// <summary>
    /// Combien <paramref name="helper"/> aide <paramref name="user"/> (0 = pas d'aide, ~1 = forte) :
    /// il génère les éléments que l'autre utilise, ou donne du critique à une carte qui frappe fort en critique.
    /// </summary>
    public double Helps(string helper, string user, string? helperTalent = null)
    {
        var h = _cards[helper];
        var u = _cards[user];
        var gen = new Dictionary<string, double>(h.Generates);
        double givesCrit = h.GivesCrit, givesRatio = h.GivesCritRatio;
        if (Talent(helperTalent) is { } t)
        {
            foreach (var (k, v) in t.Generates) gen[k] = gen.GetValueOrDefault(k) + v;
            givesCrit += t.GivesCrit;
            givesRatio += t.GivesCritRatio;
        }
        var all = gen.GetValueOrDefault("*");
        // Échelle qui sature : +1 niveau par tour vaut déjà beaucoup ; un générateur énorme (Astry, +40) ne vaut pas 40 fois plus.
        var element = u.Uses.Sum(kv => kv.Value * Saturate(gen.GetValueOrDefault(kv.Key) + all));
        // Chance de critique donnée : utile surtout aux cartes à fort multiplicateur (×3 et plus).
        // Puissance critique donnée : utile surtout aux cartes à forte chance de critique (25 % et plus).
        var chanceNeed = u.CritRatio >= 3 ? 1.0 : u.CritRatio >= 2 ? 0.3 : 0;
        var ratioNeed = Math.Min(1, u.CritChance / 30.0);
        var crit = chanceNeed * Math.Min(1, givesCrit / 20.0) + ratioNeed * Math.Min(1, givesRatio / 1.0);
        return element + crit;
    }

    private static double Saturate(double perTurn) => perTurn <= 0 ? 0 : perTurn / (perTurn + 0.5);

    /// <summary>Affinité d'une paire : l'aide dans les deux sens.</summary>
    public double Pair(string a, string b) => Helps(a, b) + Helps(b, a);

    private static CardProfile Build(SimKnowledge kb, string id, string element, IEnumerable<string> abilityIds)
    {
        var uses = new Dictionary<string, double>();
        var gen = new Dictionary<string, double>();
        double critRatio = 1, critChance = 0, givesCrit = 0, givesRatio = 0;
        var seen = new HashSet<string>();
        void Walk(string aid)
        {
            if (!seen.Add(aid) || !kb.HasAbility(aid)) return;
            var a = kb.Ability(aid);
            var perTurn = 1.0 / Math.Max(1, a.Cooldown);
            var deals = false;
            // 🔒 CostDataElement : une capacité qui coûte des niveaux d'un élément en a besoin (ex. Sacrifice : 10 terre).
            foreach (var cost in a.Costs)
                if (kb.Cost(cost.Cost) is { Class: "CostDataElement" } brick && brick.Link("team") is { } team)
                    uses[team] = uses.GetValueOrDefault(team) + Math.Max(1, cost.Value) * 5 * perTurn;
            foreach (var act in a.Actions)
            {
                var e = kb.Effect(act.Effect);
                if (e is null) continue;
                switch (e.Class)
                {
                    case "EffectDamage" or "EffectDamageAll" or "EffectHeal":
                    {
                        var el = act.Element ?? a.Element ?? element;
                        if (el.Length > 0) uses[el] = uses.GetValueOrDefault(el) + Math.Max(0, (act.Min + (double.IsNaN(act.Max) ? act.Min : act.Max)) / 2) * perTurn;
                        deals |= e.Class != "EffectHeal";
                        break;
                    }
                    case "EffectAddAllStat" or "EffectAddStat" or "EffectAddStatPerLaunch" when act.Min > 0:
                        switch (e.Int("StatToModify"))
                        {
                            case 1:
                                // 🔒 randomTeam = élément tiré au hasard ; useCardTypeForDamage = élément de la carte.
                                var el = e.Bool("randomTeam") ? "*"
                                    : e.Bool("useCardTypeForDamage") ? (element.Length > 0 ? element : "*")
                                    : act.Element ?? a.Element ?? "*";
                                gen[el] = gen.GetValueOrDefault(el) + act.Min * perTurn;
                                break;
                            case 25: givesCrit += act.Min; break;
                            case 40: givesRatio += act.Min; break;
                        }
                        break;
                }
            }
            if (deals)
            {
                critRatio = Math.Max(critRatio, a.CritDamageRatio);
                critChance = Math.Max(critChance, a.ChanceOfCrit);
            }
            foreach (var c in a.ChainAbilities.Concat(a.ComboAbilities)) Walk(c);
        }
        foreach (var aid in abilityIds) Walk(aid);
        var total = uses.Values.Sum();
        var usesShare = total > 0 ? uses.ToDictionary(kv => kv.Key, kv => kv.Value / total) : new Dictionary<string, double>();
        return new CardProfile(id, element, usesShare, gen, critRatio, critChance, givesCrit, givesRatio);
    }
}
