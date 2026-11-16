using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using SpamDrafter.Simulator.Players;
using Xunit.Abstractions;

namespace SpamDrafter.Simulator.Tests;

/// <summary>
/// Tests sur la vraie base de SPAM 1.30 (knowledge/1.30, dépôt privé).
/// Ignorés silencieusement si la base est absente (dépôt public seul), comme les tests du Core.
/// </summary>
public class RealDataTests(ITestOutputHelper output)
{
    private static readonly Lazy<SimKnowledge?> Kb = new(() =>
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "knowledge", "1.30");
            if (File.Exists(Path.Combine(p, "manifest.json"))) return SimKnowledge.Load(p);
        }
        return null;
    });

    [Fact]
    public void Les_reglages_reels_sont_ceux_du_monde_d_essai_et_des_regles()
    {
        if (Kb.Value is not { } kb) return;
        Assert.Equal("1.30", kb.GameVersion);
        var s = kb.Settings;
        var t = TestWorld.Settings130;
        Assert.Equal(t.StartLuck, s.StartLuck);
        Assert.Equal(t.LuckPerTurn, s.LuckPerTurn);
        Assert.Equal(t.TurnAfterOvertime, s.TurnAfterOvertime);
        Assert.Equal(t.OvertimeReducingRatio, s.OvertimeReducingRatio, 6);
        Assert.Equal(t.OvertimeScalingRatio, s.OvertimeScalingRatio, 6);
        Assert.Equal(t.GoldPerKill, s.GoldPerKill);
        Assert.Equal(t.FrozenBrokenByHeal, s.FrozenBrokenByHeal);
        Assert.Equal(t.FrozenBrokenByPoison, s.FrozenBrokenByPoison);
        Assert.Equal(-1, s.PlayerIdFirstPlayer);           // 📊 premier joueur tiré au sort
        Assert.Equal(4, s.CardsPerDeck);
        Assert.Equal(t.RandomElements.Order(), s.RandomElements.Order());
    }

    [Fact]
    public void Les_151_cartes_se_placent_et_leurs_capacites_existent()
    {
        if (Kb.Value is not { } kb) return;
        Assert.Equal(151, kb.Cards.Count);
        foreach (var c in kb.Cards)
        {
            var duel = new Duel(kb, [new DuelSlot(c.Id)], [new DuelSlot(c.Id)], 1);
            Assert.Equal(c.Abilities.Count, duel.Players[0].Board[0].Abilities.Count);
        }
    }

    [Fact]
    public void Aucune_capacite_de_carte_n_a_une_recharge_de_0()
    {
        // Garantit qu'un tour se termine toujours (chaque capacité ne sert qu'une fois par tour).
        if (Kb.Value is not { } kb) return;
        var onCards = kb.Cards.SelectMany(c => c.Abilities).Distinct().Select(kb.Ability).Where(a => a.IsActive);
        Assert.All(onCards, a => Assert.True(a.Cooldown >= 1, a.Id));
    }

    [Fact]
    public void Overtime_round_de_disparition_selon_les_PV_de_base()
    {
        // Sans soin ni gain de PV max : à quel round chaque carte disparaît-elle ?
        if (Kb.Value is not { } kb) return;
        var w = new TestWorld();
        var duel = w.Duel([w.Card("X")], [w.Card("Y")]);
        var rounds = new Dictionary<int, int>();
        foreach (var hp in kb.Cards.Select(c => c.Hp).Distinct())
        {
            var card = new CardState("t", new CardDef("t", "t", "t", "fire", "", hp, 0,
                new Dictionary<string, int>(), [], [], []), 0, 1, []);
            var r = kb.Settings.TurnAfterOvertime;
            while (true) { card.HpMax -= duel.OvertimeLoss(card, r); if (card.Hp <= 0) break; r++; }
            rounds[hp] = r;
        }
        foreach (var g in rounds.GroupBy(kv => kv.Value).OrderBy(g => g.Key))
            output.WriteLine($"Round {g.Key} : PV de base {string.Join(", ", g.Select(kv => kv.Key).Order())}");
        Assert.All(rounds.Values, r => Assert.InRange(r, 20, 25));
    }

    [Fact]
    public void Couverture_v1_des_capacites_actives()
    {
        if (Kb.Value is not { } kb) return;
        var any = kb.Cards.First();
        var duel = new Duel(kb, [new DuelSlot(any.Id)], [new DuelSlot(any.Id)], 1);
        var actives = kb.Cards.SelectMany(c => c.Abilities).Distinct().Select(kb.Ability).Where(a => a.IsActive).ToList();
        var full = actives.Count(duel.IsAbilityFullySupported);
        output.WriteLine($"Capacités actives des cartes entièrement simulées : {full} / {actives.Count}");
        foreach (var g in actives.SelectMany(duel.UnsupportedParts).GroupBy(x => x).OrderByDescending(g => g.Count()))
            output.WriteLine($"  {g.Count(),3} × {g.Key}");
        // Garde-fou : 108 / 228 en phase 5, 199 / 228 après le lot A de la phase 6 ; ne doit jamais reculer.
        // Reste (lot C) : statuts posés par les capacités, choix d'emplacement de Tox, ajout de Tox.
        Assert.Equal(actives.Count, full);                   // lot C : toutes les capacités actives sont simulées
    }

    [Fact]
    public void Couverture_des_talents()
    {
        if (Kb.Value is not { } kb) return;
        var any = kb.Cards.First();
        var duel = new Duel(kb, [new DuelSlot(any.Id)], [new DuelSlot(any.Id)], 1);
        var talents = kb.Cards.SelectMany(c => c.Talents).Distinct().Select(kb.Ability).ToList();
        var full = talents.Count(duel.IsAbilityFullySupported);
        output.WriteLine($"Talents des cartes entièrement simulés : {full} / {talents.Count}");
        foreach (var g in talents.SelectMany(duel.UnsupportedParts).GroupBy(x => x).OrderByDescending(g => g.Count()))
            output.WriteLine($"  {g.Count(),3} × {g.Key}");
        Assert.Equal(84, talents.Count);
        Assert.True(full >= 82, $"{full}/{talents.Count}");    // lot C : 82 ; Casino et Petit Prix agissent sur la boutique (lot D)
    }

    [Fact]
    public void Aucune_capacite_atteignable_n_a_de_combo_et_tous_les_enchainements_existent()
    {
        // Le simulateur ne gère pas les combos : on vérifie qu'aucune capacité de SPAM 1.30 n'en a.
        if (Kb.Value is not { } kb) return;
        var seen = new HashSet<string>();
        var todo = new Stack<string>(kb.Cards.SelectMany(c => c.Abilities));
        while (todo.Count > 0)
        {
            var id = todo.Pop();
            if (!seen.Add(id)) continue;
            Assert.True(kb.HasAbility(id), $"capacité absente : {id}");
            var a = kb.Ability(id);
            Assert.Empty(a.ComboAbilities);
            foreach (var c in a.ChainAbilities) todo.Push(c);
        }
        Assert.Equal(302, seen.Count);                       // 228 capacités de cartes + 74 enchaînées
    }

    [Fact]
    public void Les_capacites_du_Roi_Nature_sont_entierement_simulees()
    {
        if (Kb.Value is not { } kb) return;
        var roi = kb.Cards.Single(c => kb.CardName(c) == "Roi Nature");
        var duel = new Duel(kb, [new DuelSlot(roi.Id)], [new DuelSlot(roi.Id)], 1);
        Assert.All(roi.Abilities, id => Assert.Empty(duel.UnsupportedParts(kb.Ability(id))));
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public void Mille_duels_au_hasard_se_terminent_sans_erreur()
    {
        if (Kb.Value is not { } kb) return;
        var ids = kb.Cards.Select(c => c.Id).Order().ToList();
        var rng = new Rng(2026);
        var wins = new int[3];
        var maxRound = 0;
        for (var g = 0; g < 1000; g++)
        {
            // Decks au hasard, talent au hasard (ou aucun), comme le permet le Deck builder.
            List<DuelSlot> Deck() => ids.OrderBy(_ => rng.NextULong()).Take(4).Select(id =>
            {
                var k = rng.Next(0, 4);
                return new DuelSlot(id, k == 0 ? null : kb.Card(id).Talents[k - 1]);
            }).ToList();
            var duel = new Duel(kb, Deck(), Deck(), rng.NextULong());
            duel.Log.Enabled = false;
            var result = DuelRunner.Play(duel, new RandomPlayer(rng.NextULong()), new RandomPlayer(rng.NextULong()));
            Assert.True(result.Finished, $"partie {g} non terminée");
            // Avec les Tox (PV max, Phénix, soins), une partie peut dépasser le round 25 ; l'overtime retire
            // plus de 100 % des PV de base par tour dès le round 38 : aucune partie ne peut aller au-delà sans résurrection.
            Assert.InRange(result.Rounds, 1, 40);
            wins[result.Winner!.Value + 1]++;
            maxRound = Math.Max(maxRound, result.Rounds);
        }
        output.WriteLine($"J1 : {wins[1]}, J2 : {wins[2]}, égalités : {wins[0]}, round max : {maxRound}");
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public void Deux_cents_duels_sans_boucle_ni_effet_non_simule()
    {
        if (Kb.Value is not { } kb) return;
        var ids = kb.Cards.Select(c => c.Id).Order().ToList();
        var rng = new Rng(77);
        for (var g = 0; g < 200; g++)
        {
            List<DuelSlot> Deck() => ids.OrderBy(_ => rng.NextULong()).Take(4).Select(id =>
            {
                var k = rng.Next(0, 4);
                return new DuelSlot(id, k == 0 ? null : kb.Card(id).Talents[k - 1]);
            }).ToList();
            var duel = new Duel(kb, Deck(), Deck(), rng.NextULong());
            DuelRunner.Play(duel, new RandomPlayer(rng.NextULong()), new RandomPlayer(rng.NextULong()));
            var bad = duel.Log.Lines.Where(l => l.Contains("boucle") || l.Contains("non simulé")).ToList();
            // Seuls Casino et Petit Prix (boutique, lot D) peuvent encore apparaître comme non simulés.
            Assert.All(bad, l => Assert.True(l.Contains("AddSCasino") || l.Contains("AddSPetitPrix"), l));
        }
    }

    [Fact]
    public void Les_34_classes_d_effets_utilisees_sont_celles_de_la_liste_de_couverture()
    {
        if (Kb.Value is not { } kb) return;
        var seen = new HashSet<string>();
        var todo = new Stack<string>(kb.Cards.SelectMany(c => c.Abilities.Concat(c.Talents)));
        var classes = new HashSet<string>();
        while (todo.Count > 0)
        {
            var id = todo.Pop();
            if (!seen.Add(id)) continue;
            var a = kb.Ability(id);
            foreach (var act in a.Actions) classes.Add(kb.Effect(act.Effect)!.Class);
            foreach (var c in a.ChainAbilities) todo.Push(c);
        }
        Assert.Equal(CoverageTests.UsedEffectClasses.Order(), classes.Order());
    }

    [Theory]
    [InlineData(0, 100, 0, 0, 0)]
    [InlineData(2, 100, 0, 0, 0)]
    [InlineData(4, 80, 20, 0, 0)]
    [InlineData(6, 50, 45, 5, 0)]
    [InlineData(8, 27, 53, 20, 0)]
    [InlineData(10, 10, 50, 40, 0)]
    [InlineData(12, 4.4, 41.7, 52.7, 1.2)]
    [InlineData(14, 0, 35, 60, 5)]
    [InlineData(16, 0, 27.8, 58.9, 13.3)]
    [InlineData(18, 0, 20.8, 54.5, 24.7)]
    [InlineData(20, 0, 14.9, 50.1, 35)]
    [InlineData(30, 0, 0, 30, 70)]
    [InlineData(50, 0, 0, 0, 100)]
    public void Rarete_selon_la_chance_comme_le_tableau_des_regles(double luck, double c, double r, double e, double l)
    {
        // REGLES_DU_DUEL §8.5 (calculé pendant l'audit avec l'algorithme du jeu) ; recalculé ici par le simulateur.
        if (Kb.Value is not { } kb) return;
        var any = kb.Cards.First();
        var duel = new Duel(kb, [new DuelSlot(any.Id)], [new DuelSlot(any.Id)], 1);
        var odds = duel.RarityOdds(luck);
        Assert.Equal(c, odds[0], 1);
        Assert.Equal(r, odds[1], 1);
        Assert.Equal(e, odds[2], 1);
        Assert.Equal(l, odds[3], 1);
    }

    [Fact]
    public void Les_joueurs_au_hasard_utilisent_la_boutique()
    {
        if (Kb.Value is not { } kb) return;
        var ids = kb.Cards.Select(c => c.Id).Order().ToList();
        var rng = new Rng(5);
        var buys = 0; var rerolls = 0; var skips = 0; var bans = 0;
        for (var g = 0; g < 50; g++)
        {
            List<DuelSlot> Deck() => ids.OrderBy(_ => rng.NextULong()).Take(4).Select(id => new DuelSlot(id)).ToList();
            var duel = new Duel(kb, Deck(), Deck(), rng.NextULong());
            DuelRunner.Play(duel, new RandomPlayer(rng.NextULong()), new RandomPlayer(rng.NextULong()));
            buys += duel.Log.Lines.Count(l => l.Contains(" achète "));
            rerolls += duel.Log.Lines.Count(l => l.Contains("relance la boutique"));
            skips += duel.Log.Lines.Count(l => l.Contains("passe la boutique"));
            bans += duel.Log.Lines.Count(l => l.Contains(" bannit "));
        }
        output.WriteLine($"50 duels : {buys} achats, {rerolls} relances, {skips} passes, {bans} bannissements");
        Assert.True(buys > 0 && rerolls > 0 && skips > 0 && bans > 0);
    }

    [Fact]
    public void Un_duel_reel_est_reproductible()
    {
        if (Kb.Value is not { } kb) return;
        string Run()
        {
            var ids = kb.Cards.Select(c => c.Id).Order().ToList();
            var d0 = ids.Take(4).Select(id => new DuelSlot(id)).ToList();
            var d1 = ids.Skip(10).Take(4).Select(id => new DuelSlot(id)).ToList();
            var duel = new Duel(kb, d0, d1, 7);
            DuelRunner.Play(duel, new RandomPlayer(8), new RandomPlayer(9));
            return duel.Log.ToString();
        }
        Assert.Equal(Run(), Run());
    }
}
