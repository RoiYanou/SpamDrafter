using SpamDrafter.Engines.Analysis;
using SpamDrafter.Engines.Cooking;
using SpamDrafter.Engines.Profiles;
using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using SpamDrafter.Simulator.Players;

namespace SpamDrafter.Engines.Tests;

/// <summary>Phase 8, lot G (D-050) : profils des cartes, recherche guidée, « pourquoi ça marche ».</summary>
public class Phase8LotGTests
{
    private static SimKnowledge? Kb => Phase8LotATests.Kb.Value;

    private static string Id(SimKnowledge kb, string name) => kb.Cards.First(c => kb.CardName(c) == name).Id;

    [Fact]
    public void Profils_lus_dans_les_donnees_de_Shinobi_et_Astry()
    {
        if (Kb is not { } kb) return;
        var p = CardProfiles.For(kb);
        var shinobi = p.Card(Id(kb, "Shinobi Éclairant"));
        Assert.Equal(3, shinobi.CritRatio);                    // 🃏 Éclair jaune et Kung fu : critique ×3
        Assert.Equal(25, shinobi.CritChance);                  // 🃏 Kung fu : 25 %
        Assert.True(shinobi.Uses.ContainsKey("light"));
        Assert.Empty(shinobi.Generates);                       // ne génère aucun élément
        Assert.InRange(shinobi.Uses.Values.Sum(), 0.999, 1.001);
        var astry = p.Card(Id(kb, "Astry"));
        Assert.True(astry.Generates.ContainsKey("*"));         // 🃏 Génie incompris : +40 dans un élément au hasard
        var aristocrate = p.Card(Id(kb, "Aristocrate Nébulaire"));
        Assert.True(aristocrate.GivesCrit > 0);                // donne du critique
    }

    [Fact]
    public void Affinites_un_generateur_de_lumiere_aide_Shinobi_plus_qu_une_carte_sans_lien()
    {
        if (Kb is not { } kb) return;
        var p = CardProfiles.For(kb);
        var shinobi = Id(kb, "Shinobi Éclairant");
        var helpers = new[] { "Ameru", "Aristocrate Nébulaire", "Maîtresse des Teintes" }.Select(n => p.Helps(Id(kb, n), shinobi)).ToList();
        Assert.All(helpers, h => Assert.True(h > 0.2, $"aide trop faible : {h}"));
        // Une carte qui ne génère rien et ne donne pas de critique n'aide pas.
        var none = kb.Cards.Select(c => c.Id).First(c => p.Card(c).Generates.Count == 0 && p.Card(c).GivesCrit == 0 && p.Card(c).GivesCritRatio == 0);
        Assert.Equal(0, p.Helps(none, shinobi));
        Assert.Equal(p.Helps(shinobi, none) + p.Helps(none, shinobi), p.Pair(shinobi, none), 9);
    }

    [Fact]
    public void Le_guide_prefere_les_cartes_qui_aident()
    {
        if (Kb is not { } kb) return;
        var p = CardProfiles.For(kb);
        var shinobi = Id(kb, "Shinobi Éclairant");
        var ids = kb.Cards.Select(c => c.Id).Order(StringComparer.Ordinal).ToArray();
        var guide = new GrandCuisinier.Guide(p, share: 1.0);
        var rng = new Rng(5);
        var picks = Enumerable.Range(0, 400).Select(_ => guide.Pick([shinobi], ids, [shinobi], rng)!).ToList();
        var helping = picks.Count(c => p.Pair(c, shinobi) >= 0.2);
        var share = kb.Cards.Count(c => c.Id != shinobi && p.Pair(c.Id, shinobi) >= 0.2) / (double)(kb.Cards.Count - 1);
        Assert.True(helping / 400.0 > 2 * share, $"guidé : {helping / 400.0:P0}, hasard : {share:P0}");
        Assert.DoesNotContain(shinobi, picks);
        Assert.Null(new GrandCuisinier.Guide(p, share: 0).Pick([shinobi], ids, [shinobi], rng));   // 0 = toujours au hasard
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public void Statistiques_de_critique_et_de_niveaux_d_element()
    {
        if (Kb is not { } kb) return;
        var rng = new Rng(11);
        var crits = 0;
        var given = 0;
        for (var g = 0; g < 40; g++)
        {
            var duel = new Duel(kb, Bench.RandomDeck(kb, rng), Bench.RandomDeck(kb, rng), rng.NextULong())
                { Log = { Enabled = false }, Stats = new DuelStats(4) };
            DuelRunner.Play(duel, new FastPlayer(rng.NextULong()), new FastPlayer(rng.NextULong()));
            for (var pl = 0; pl < 2; pl++)
            {
                crits += duel.Stats!.Cards[pl].Sum(c => c.CritHits);
                given += duel.Stats.Cards[pl].Sum(c => c.ElementGiven);
                Assert.All(duel.Stats.Cards[pl], c => Assert.True(c.CritBonus >= 0));
                // Le plus haut niveau noté est au moins le niveau final de chaque élément.
                foreach (var (el, lvl) in duel.Players[pl].ElementLevels)
                    if (lvl > 0) Assert.True(duel.Stats.PeakLevels[pl].GetValueOrDefault(el) >= lvl);
            }
        }
        Assert.True(crits > 0);
        Assert.True(given > 0);
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public void La_fiche_explique_pourquoi_le_deck_marche()
    {
        if (Kb is not { } kb) return;
        string T(string card, string talent) => kb.Card(card).Talents.First(t => kb.AbilityName(t) == talent);
        var deck = new[] { ("Commando Gerz", "Vitalité des Âmes"), ("Shinobi Éclairant", "Boule de Crit"), ("Astry", "Mystère"), ("Pyrocoq", "Vitalité des Âmes") }
            .Select(x => new DuelSlot(Id(kb, x.Item1), T(Id(kb, x.Item1), x.Item2))).ToList();
        var settings = AnalysisSettings.Sheet with
        {
            Player = "rapide", Opponents = 6, Pairs = 2, TalentPairs = 1, RoleVariants = 2, PoolSize = 30, CalibrationOpponents = 4,
            DangerMinGames = 2, ToxMinGames = 1, FieldSeed = 95,
        };
        var r = GrandAnalyseur.Analyse(kb, deck, settings);
        Assert.NotEmpty(r.PeakLevels);
        var why = SheetWriter.Why(kb, r);
        Assert.InRange(why.Count, 1, 3);
        Assert.Contains("POURQUOI ÇA MARCHE", SheetWriter.ToText(kb, r));
        Assert.Contains(why, l => l.Contains("Astry") || l.Contains("Niveau"));
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public void Une_proposition_beaucoup_plus_faible_n_est_pas_montree()
    {
        if (Kb is not { } kb) return;
        var s = new CookSettings
        {
            ScreenOpponents = 4, Population = 8, Elites = 3, MutantsPerElite = 2, Generations = 2, ChampionEvery = 0,
            ConfirmPlayer = "rapide", ConfirmCandidates = 5, ConfirmOpponents = 4, Proposals = 3,
        };
        var card = Id(kb, "Haut-bois des Glaces");
        foreach (var gap in new[] { 0.0, 0.2, 1.0 })
        {
            var res = GrandCuisinier.Cook(kb, [new FixedCard(card)], s with { ProposalGap = gap, Seed = 8 });
            var best = res.Proposals[0].Confirmation.Score;
            Assert.All(res.Proposals, p => Assert.True(p.Confirmation.Score >= best - gap - 1e-9));
        }
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public void Sans_guide_la_cuisine_reste_possible_et_reproductible()
    {
        if (Kb is not { } kb) return;
        var s = new CookSettings
        {
            ScreenOpponents = 4, Population = 6, Elites = 2, MutantsPerElite = 2, Generations = 2, ChampionEvery = 0,
            ConfirmPlayer = "rapide", ConfirmCandidates = 3, ConfirmOpponents = 4, Proposals = 2,
        };
        var card = Id(kb, "Shinobi Éclairant");
        foreach (var guided in new[] { 0.0, 0.5 })
        {
            var a = GrandCuisinier.Cook(kb, [new FixedCard(card)], s with { Guided = guided, Seed = 3 });
            var b = GrandCuisinier.Cook(kb, [new FixedCard(card)], s with { Guided = guided, Seed = 3 });
            Assert.Equal(a.Proposals.Select(p => GrandCuisinier.Key(p.Deck)), b.Proposals.Select(p => GrandCuisinier.Key(p.Deck)));
        }
    }
}
