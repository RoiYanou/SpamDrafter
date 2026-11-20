using SpamDrafter.App.ViewModels;
using SpamDrafter.Core.Games;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.Core.Tests;

/// <summary>Phase 16, lot D : Tirage du jour (carte assemblée au hasard, notée selon la probabilité des tirages et leur cohérence, avec historique).</summary>
public class Phase16RngTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spamdrafter-rng-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static string? KnowledgeDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "knowledge", "1.30");
            if (File.Exists(Path.Combine(p, "manifest.json"))) return p;
        }
        return null;
    }

    [Fact]
    public void Une_graine_donne_toujours_la_meme_carte_et_des_graines_differentes_des_cartes_differentes()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var kb = KnowledgeBase.Load(dir);
        var el = RngGame.LoadAbilityElements(dir);
        var a = RngGame.ForSeed(kb, 424242, el)!; var b = RngGame.ForSeed(kb, 424242, el)!;
        Assert.Equal(a.Parts.Select(p => (p.Label, p.Value, p.Points)), b.Parts.Select(p => (p.Label, p.Value, p.Points)));
        Assert.Equal(a.Lines.Select(l => (l.Reason, l.Value, l.Points)), b.Lines.Select(l => (l.Reason, l.Value, l.Points)));
        Assert.Equal(a.Total, b.Total);
        Assert.Equal(a.Percentile, b.Percentile);
        var different = Enumerable.Range(1, 30).Count(i => !RngGame.ForSeed(kb, (ulong)i * 7919, el)!.Parts.Select(p => p.Value).SequenceEqual(a.Parts.Select(p => p.Value)));
        Assert.Equal(30, different);
    }

    [Fact]
    public void Les_regles_des_emplacements_sont_respectees_sur_300_jours()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var kb = KnowledgeBase.Load(dir);
        var el = RngGame.LoadAbilityElements(dir);
        var allAbilities = kb.Cards.SelectMany(c => c.Abilities).ToHashSet();
        var firstAbilities = kb.Cards.Where(c => c.Abilities.Count > 0).Select(c => c.Abilities[0]).ToHashSet();
        var allTox = kb.Cards.SelectMany(c => c.InnatePassives ?? []).ToHashSet();
        var hps = kb.Cards.Select(c => c.Hp.ToString()).ToHashSet();
        var golds = kb.Cards.Select(c => c.GoldIncome.ToString()).ToHashSet();
        var sawNothing = false; var sawTox = false; var sawThree = false;
        for (var i = 0; i < 300; i++)
        {
            var g = RngGame.ForDay(kb, DailyClock.Epoch.AddDays(i), el)!;
            Assert.Equal(["Illustration", "Nom", "Rareté", "Élément", "Spamtons", "PV", "Compétence 1"], g.Parts.Select(p => p.Label).Take(7));
            Assert.Contains(g.Parts[2].ItemId, kb.Cards.Select(c => c.Rarity));
            Assert.Contains(g.Parts[3].ItemId, kb.Cards.Select(c => c.Element));
            Assert.Contains(g.Parts[4].Value, golds);
            Assert.Contains(g.Parts[5].Value, hps);
            Assert.Contains(g.Parts[6].ItemId!, firstAbilities);                        // compétence 1 : toujours une compétence activable de carte
            Assert.True(g.Parts.Count is >= 8 and <= 9);
            var slot2 = g.Parts[7];
            if (slot2.Kind == RngPartKind.Nothing) { Assert.Equal(8, g.Parts.Count); sawNothing = true; }   // rien en 2 : pas d'emplacement 3
            else
            {
                Assert.Equal(9, g.Parts.Count); sawThree = true;
                foreach (var p in g.Parts.Skip(7))
                {
                    if (p.Kind == RngPartKind.Tox) { Assert.Contains(p.ItemId!, allTox); sawTox = true; }
                    else if (p.Kind == RngPartKind.Ability) Assert.Contains(p.ItemId!, allAbilities);
                    else Assert.True(p.Kind == RngPartKind.Nothing && p.Label == "Emplacement 3", "seul l'emplacement 3 peut être vide ici");
                }
                if (g.Parts[8].Kind != RngPartKind.Nothing) Assert.NotEqual(g.Parts[7].ItemId, g.Parts[8].ItemId);
            }
            Assert.All(g.Parts, p => Assert.True(p.Points >= 0));
            Assert.InRange(g.Percentile, 0, 100);
        }
        Assert.True(sawNothing && sawTox && sawThree);
    }

    [Fact]
    public void Les_points_suivent_la_probabilite_et_le_detail_est_trie_du_plus_petit_au_plus_grand()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var kb = KnowledgeBase.Load(dir);
        var el = RngGame.LoadAbilityElements(dir);
        var n = kb.Cards.Count;
        var expected = (int)Math.Round(50 * Math.Log2(n));    // un nom ou une illustration vaut 50 × log2(nombre de cartes) : les tirages comptent peu
        for (var i = 0; i < 60; i++)
        {
            var g = RngGame.ForDay(kb, DailyClock.Epoch.AddDays(i), el)!;
            Assert.Equal(0, g.Parts[0].Points);                        // l'illustration et le nom valent pareil pour toutes les cartes : 0 point
            Assert.Equal(0, g.Parts[1].Points);
            var share = kb.Cards.Count(c => c.Element == g.Parts[3].ItemId) / (double)n;
            Assert.Equal((int)Math.Round(50 * Math.Log2(1 / share)), g.Parts[3].Points);
            var rshare = kb.Cards.Count(c => c.Rarity == g.Parts[2].ItemId) / (double)n;
            Assert.Equal((int)Math.Round(50 * Math.Log2(1 / rshare)), g.Parts[2].Points);
            Assert.Equal(g.DrawPoints + g.BonusPoints, g.Total);
            Assert.Equal(g.Total, g.Lines.Sum(l => l.Points));
            Assert.Equal(g.Parts.Count(p => p.Points > 0) + g.Bonuses.Count, g.Lines.Count);   // ce qui rapporte 0 point n'est pas montré
            Assert.DoesNotContain(g.Lines, l => l.Points <= 0);
            Assert.Equal(g.Lines.Select(l => l.Points).Order(), g.Lines.Select(l => l.Points));   // du plus petit au plus grand
            Assert.All(g.Bonuses, b => Assert.True(b.Points > 0));
            // Chaque bonus décrit un accord réel.
            foreach (var b in g.Bonuses)
            {
                if (b.Label == "Illustration et nom de la même carte") Assert.Equal(g.NameCard.Id, g.IllustrationCard.Id);
                if (b.Label == "Illustration et nom du même élément") Assert.Equal(g.NameCard.Element, g.IllustrationCard.Element);
                if (b.Label == "Illustration et nom de la même rareté") Assert.Equal(g.NameCard.Rarity, g.IllustrationCard.Rarity);
                if (b.Label == "Élément de la carte du nom") Assert.Equal(g.NameCard.Element, g.Parts[3].ItemId);
                if (b.Label == "Rareté de la carte du nom") Assert.Equal(g.NameCard.Rarity, g.Parts[2].ItemId);
                if (b.Label == "PV de la carte du nom") Assert.Equal(g.NameCard.Hp.ToString(), g.Parts[5].Value);
                if (b.Label == "Spamtons de la carte du nom") Assert.Equal(g.NameCard.GoldIncome.ToString(), g.Parts[4].Value);
            }
        }
    }

    [Fact]
    public void Il_y_a_beaucoup_de_bonus_differents_et_chaque_carte_du_jeu_est_une_carte_parfaite()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var kb = KnowledgeBase.Load(dir);
        var el = RngGame.LoadAbilityElements(dir);
        var seen = new HashSet<string>();
        var best = 0;
        for (var i = 0; i < 800; i++)
        {
            var g = RngGame.ForDay(kb, DailyClock.Epoch.AddDays(i), el)!;
            foreach (var b in g.Bonuses) seen.Add(b.Label);
            Assert.False(g.IsPerfect, "une carte parfaite est quasi impossible à tirer");
            best = Math.Max(best, g.Total);
        }
        Assert.True(seen.Count >= 60, $"au moins 60 bonus différents vus, trouvés : {seen.Count}");
        // Une carte qui existe (illustration, nom, élément, PV, Spamtons, compétences, Tox) est parfaite et bat toute carte non parfaite.
        foreach (var c in kb.Cards.Where(c => c.Abilities.Count > 0).Take(40))
        {
            var g = RngGame.ForCard(kb, c, el);
            Assert.True(g.IsPerfect, kb.CardName(c));
            Assert.True(g.Total > best, $"{kb.CardName(c)} : {g.Total} ≤ {best}");
            Assert.Equal(100, g.Percentile, 6);
            Assert.Contains(g.Bonuses, b => b.Label.StartsWith("Carte parfaite"));
        }
    }

    [Fact]
    public void Le_rang_compare_la_note_a_des_cartes_tirees_au_hasard()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var kb = KnowledgeBase.Load(dir);
        var el = RngGame.LoadAbilityElements(dir);
        var games = Enumerable.Range(0, 120).Select(i => RngGame.ForDay(kb, DailyClock.Epoch.AddDays(i), el)!).ToList();
        foreach (var a in games) foreach (var b in games.Where(b => b.Total > a.Total)) Assert.True(b.Percentile >= a.Percentile);   // plus de points = rang pas plus bas
        Assert.True(games.Max(g => g.Percentile) > games.Min(g => g.Percentile));
    }

    [Fact]
    public void Un_clic_cree_une_carte_au_hasard_et_le_tirage_est_enregistre_avec_sa_date()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var kb = KnowledgeBase.Load(dir);
        var el = RngGame.LoadAbilityElements(dir);
        var items = kb.Cards.Select(c => new CardItem(c, kb)).ToList();
        var path = Path.Combine(_dir, "daily.json"); var hist = Path.Combine(_dir, "rng-history.json");
        ulong seed = 777;
        var vm = new RngViewModel(kb, items, dir, DailyBook.Load(path), path, RngHistory.Load(hist), hist, seedSource: () => seed++);   // pas d'animation : tout apparaît d'un coup
        Assert.True(vm.Available && vm.CanStart && !vm.Started && !vm.ShowCardFace && vm.NoHistory);
        vm.StartCommand.Execute(null);
        Assert.True(vm.Started && vm.IsFinished && !vm.CanStart);
        Assert.True(vm.ShowCardFace && vm.ShowName && vm.ShowElement && vm.ShowStats && vm.ShowIncome);
        Assert.NotNull(vm.Card);
        var expected = RngGame.ForSeed(kb, 777, el)!;                    // la carte vient de la graine tirée
        Assert.Equal(expected.Lines.Count, vm.Scores.Count);
        Assert.Equal(expected.Total, vm.ShownTotal);
        Assert.Equal(expected.Lines.Last().Points.ToString(), vm.Scores[0].PointsText.TrimStart('+'));
        Assert.Equal(expected.Total, vm.DisplayedTotal);   // le plus grand gain est en haut
        vm.StartCommand.Execute(null);                                   // un 2ᵉ clic ne change rien
        Assert.Single(vm.History);

        // L'historique garde la date et le détail, y compris après fermeture de l'application ; la même carte est retrouvée.
        var saved = RngHistory.Load(hist);
        var e = Assert.Single(saved.Entries);
        Assert.Equal(DailyClock.Key(vm.Day), e.Day);
        Assert.True(e.Total == expected.Total && e.Lines.Count > 7 && e.PlayedUtc > DateTime.UtcNow.AddMinutes(-5));
        Assert.Equal(e.Total, e.Lines.Sum(l => l.Points));
        var again = new RngViewModel(kb, items, dir, DailyBook.Load(path), path, saved, hist);
        Assert.True(again.Started && again.IsFinished);
        Assert.Equal(expected.Total, again.ShownTotal);
        Assert.Single(again.History);
        Assert.Equal(vm.History[0].ScoreText, again.History[0].ScoreText);
        Assert.NotEmpty(again.History[0].DayText);

        // Jours différents : plusieurs entrées, les plus récentes d'abord ; un même jour remplace l'ancien tirage.
        saved.Record(new RngHistoryEntry { Day = "2026-11-18", Total = 10 });
        saved.Record(new RngHistoryEntry { Day = "2026-11-22", Total = 20 });
        saved.Record(new RngHistoryEntry { Day = "2026-11-18", Total = 99 });
        Assert.Equal(["2026-11-22", DailyClock.Key(vm.Day), "2026-11-18"], saved.Entries.Select(x => x.Day).Order(StringComparer.Ordinal).Reverse());
        Assert.Equal(99, saved.Entries.Single(x => x.Day == "2026-11-18").Total);
    }

    [Fact]
    public void Deux_joueurs_n_ont_pas_la_meme_carte_et_la_reinitialisation_permet_de_rejouer()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var kb = KnowledgeBase.Load(dir);
        var items = kb.Cards.Select(c => new CardItem(c, kb)).ToList();
        var p1 = Path.Combine(_dir, "a.json"); var p2 = Path.Combine(_dir, "b.json"); var h1 = Path.Combine(_dir, "ha.json"); var h2 = Path.Combine(_dir, "hb.json");
        var book = DailyBook.Load(p1);
        var one = new RngViewModel(kb, items, dir, book, p1, RngHistory.Load(h1), h1);    // graine tirée au hasard par défaut
        var two = new RngViewModel(kb, items, dir, DailyBook.Load(p2), p2, RngHistory.Load(h2), h2);
        one.StartCommand.Execute(null); two.StartCommand.Execute(null);
        Assert.NotEqual(DailyBook.Load(p1).Games["rng"].Guesses[0], DailyBook.Load(p2).Games["rng"].Guesses[0]);

        // Réinitialisation (bouton de test) : le tirage du jour peut être refait.
        book.Games.Clear(); book.TrySave(p1);
        one.Reload();
        Assert.True(one.CanStart && !one.Started && !one.ShowCardFace);
        one.StartCommand.Execute(null);
        Assert.True(one.IsFinished);
        Assert.Single(one.History);                                                                      // même jour : l'ancien tirage est remplacé
    }

    [Fact]
    public void Chaque_emplacement_vient_du_meme_emplacement_d_une_carte_existante()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var kb = KnowledgeBase.Load(dir);
        var el = RngGame.LoadAbilityElements(dir);
        // Ce que les vraies cartes ont en 1ʳᵉ, 2ᵉ et 3ᵉ place (compétences d'abord, puis Tox).
        var slot1 = kb.Cards.Where(c => c.Abilities.Count > 0).Select(c => c.Abilities[0]).ToHashSet();
        List<(char, string)> Items(Card c) => c.Abilities.Select(a => ('a', a)).Concat((c.InnatePassives ?? []).Select(t => ('t', t))).ToList();
        var slot2 = kb.Cards.Select(Items).Where(i => i.Count > 1).Select(i => i[1]).ToHashSet();
        var slot3 = kb.Cards.Select(Items).Where(i => i.Count > 2).Select(i => i[2]).ToHashSet();
        for (var i = 0; i < 400; i++)
        {
            var g = RngGame.ForDay(kb, DailyClock.Epoch.AddDays(i), el)!;
            Assert.Contains(g.Parts[6].ItemId!, slot1);
            foreach (var (part, pool, label) in new[] { (7, slot2, "2"), (8, slot3, "3") })
            {
                if (g.Parts.Count <= part || g.Parts[part].Kind == RngPartKind.Nothing) continue;
                var kind = g.Parts[part].Kind == RngPartKind.Ability ? 'a' : 't';
                Assert.True(pool.Contains((kind, g.Parts[part].ItemId!)), $"emplacement {label} : {g.Parts[part].Value} n'existe pas à cette place sur une vraie carte");
            }
        }
    }

    [Fact]
    public void Les_bonus_de_coherence_rapportent_beaucoup_plus_que_les_tirages()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var kb = KnowledgeBase.Load(dir);
        var el = RngGame.LoadAbilityElements(dir);
        var games = Enumerable.Range(0, 300).Select(i => RngGame.ForDay(kb, DailyClock.Epoch.AddDays(i), el)!).ToList();
        // L'illustration et le nom (présents sur toutes les cartes) valent peu ; un bonus rare vaut bien plus.
        Assert.All(games, g => Assert.True(g.Parts[0].Points < 500 && g.Parts[1].Points < 500));
        Assert.True(games.SelectMany(g => g.Bonuses).Max(b => b.Points) > 3000);
        Assert.True(games.Average(g => g.BonusPoints) > games.Average(g => g.DrawPoints), "en moyenne, les bonus pèsent plus que les tirages");
    }

    [Fact]
    public void La_probabilite_d_un_2e_et_d_un_3e_emplacement_vient_des_cartes_existantes()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var kb = KnowledgeBase.Load(dir);
        var el = RngGame.LoadAbilityElements(dir);
        int Items(Card c) => c.Abilities.Count + (c.InnatePassives ?? []).Count;
        var n = (double)kb.Cards.Count;
        var n2 = kb.Cards.Count(c => Items(c) >= 2); var n3 = kb.Cards.Count(c => Items(c) >= 3);
        var games = Enumerable.Range(0, 3000).Select(i => RngGame.ForSeed(kb, (ulong)i * 2654435761UL + 11, el)!).ToList();
        var withSlot2 = games.Count(g => g.Parts.Count >= 8 && g.Parts[7].Kind != RngPartKind.Nothing);
        var withSlot3 = games.Count(g => g.Parts.Count >= 9 && g.Parts[8].Kind != RngPartKind.Nothing);
        Assert.InRange(withSlot2 / 3000.0, n2 / n - 0.04, n2 / n + 0.04);                   // 2ᵉ emplacement : part des cartes qui en ont un
        Assert.InRange(withSlot3 / (double)withSlot2, n3 / (double)n2 - 0.05, n3 / (double)n2 + 0.05);   // 3ᵉ : parmi celles qui ont déjà un 2ᵉ
        // Chaque ligne du détail porte sa rareté (bits) : plus c'est rare, plus ça rapporte.
        var lines = games.Take(200).SelectMany(g => g.Lines).Where(l => l.IsBonus).ToList();
        Assert.All(lines, l => Assert.True(l.Bits > 0));
        Assert.True(lines.Where(l => l.Bits > 8).Average(l => l.Points) > lines.Where(l => l.Bits < 4).Average(l => l.Points));
    }

    [Fact]
    public void Un_3e_emplacement_competence_suppose_un_2e_emplacement_competence()
    {
        var dir = KnowledgeDir();
        if (dir is null) return;
        var kb = KnowledgeBase.Load(dir);
        var el = RngGame.LoadAbilityElements(dir);
        // Une carte liste ses compétences avant ses Tox : « Tox puis compétence » n'existe sur aucune vraie carte, donc ne sort jamais.
        for (var i = 0; i < 1500; i++)
        {
            var g = RngGame.ForSeed(kb, (ulong)i * 40503UL + 7, el)!;
            if (g.Parts.Count < 9) continue;
            if (g.Parts[7].Kind == RngPartKind.Tox) Assert.NotEqual(RngPartKind.Ability, g.Parts[8].Kind);
        }
    }
}
