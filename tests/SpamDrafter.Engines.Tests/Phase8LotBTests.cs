using SpamDrafter.Engines.Cooking;
using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;

namespace SpamDrafter.Engines.Tests;

/// <summary>Phase 8, lot B : Grand Cuisinier.</summary>
public class Phase8LotBTests
{
    private static SimKnowledge? Kb => Phase8LotATests.Kb.Value;

    /// <summary>Réglages minuscules pour des tests rapides (le principe est le même).</summary>
    private static readonly CookSettings Tiny = new()
    {
        ScreenOpponents = 4, Population = 8, Elites = 3, MutantsPerElite = 2, Generations = 4, ChampionEvery = 2, Champions = 2,
        ConfirmPlayer = "rapide", ConfirmCandidates = 4, ConfirmOpponents = 4, Proposals = 3,
    };

    private static string[] Ids(SimKnowledge kb) => kb.Cards.Select(c => c.Id).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void Une_selection_invalide_est_refusee()
    {
        if (Kb is not { } kb) return;
        var ids = Ids(kb);
        Assert.Throws<ArgumentException>(() => GrandCuisinier.Cook(kb, [], Tiny));
        Assert.Throws<ArgumentException>(() => GrandCuisinier.Cook(kb, ids.Take(5).Select(i => new FixedCard(i)).ToList(), Tiny));
        Assert.Throws<ArgumentException>(() => GrandCuisinier.Cook(kb, [new FixedCard(ids[0]), new FixedCard(ids[0])], Tiny));
        Assert.Throws<ArgumentException>(() => GrandCuisinier.Cook(kb, [new FixedCard("inconnue")], Tiny));
        var other = kb.Cards.First(c => c.Talents.Count > 0 && c.Id != ids[0]).Talents[0];
        var first = kb.Card(ids[0]);
        if (!first.Talents.Contains(other))
            Assert.Throws<ArgumentException>(() => GrandCuisinier.Cook(kb, [new FixedCard(ids[0], other)], Tiny));
    }

    [Fact]
    public void Completions_et_variantes_respectent_la_selection()
    {
        if (Kb is not { } kb) return;
        var ids = Ids(kb);
        var withTalent = kb.Cards.First(c => c.Talents.Count > 0);
        var selection = new List<FixedCard> { new(withTalent.Id, withTalent.Talents[0]), new(ids.First(i => i != withTalent.Id)) };
        var rng = new Rng(17);
        for (var n = 0; n < 300; n++)
        {
            var d = GrandCuisinier.RandomCompletion(kb, selection, ids, rng);
            Check(d);
            for (var m = 0; m < 5; m++) { d = GrandCuisinier.Mutate(kb, selection, d, ids, rng); Check(d); }
            var t = GrandCuisinier.WithRandomTalents(kb, d, selection, rng);
            Assert.Equal(d.Select(x => x.CardId), t.Select(x => x.CardId));
            Check(t);
        }

        void Check(IReadOnlyList<DuelSlot> d)
        {
            Assert.Equal(4, d.Count);
            Assert.Equal(4, d.Select(x => x.CardId).Distinct().Count());                     // D-020
            Assert.All(selection, f => Assert.Contains(d, x => x.CardId == f.CardId));
            Assert.Equal(withTalent.Talents[0], d.First(x => x.CardId == withTalent.Id).TalentId);
            Assert.All(d, x => Assert.True(x.TalentId is null || kb.Card(x.CardId).Talents.Contains(x.TalentId)));
        }
    }

    [Fact]
    public void Les_decks_choisis_sont_assez_differents()
    {
        var sel = new List<FixedCard> { new("A") };
        IReadOnlyList<DuelSlot> D(params string[] c) => c.Select(x => new DuelSlot(x)).ToList();
        var ordered = new[] { D("A", "B", "C", "D"), D("A", "B", "C", "E"), D("A", "F", "G", "D"), D("A", "H", "I", "J") };
        var strict = GrandCuisinier.PickDiverse(ordered, sel, 3, 2, fill: false);
        Assert.Equal([ordered[0], ordered[2], ordered[3]], strict);
        var loose = GrandCuisinier.PickDiverse(ordered, sel, 2, 1, fill: false);
        Assert.Equal([ordered[0], ordered[1]], loose);
        Assert.Equal(3, GrandCuisinier.MinDifference(3, strict: true) + GrandCuisinier.MinDifference(3, strict: false));
        Assert.Equal(0, GrandCuisinier.MinDifference(0, strict: true));
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public void La_cuisine_est_reproductible_et_respecte_la_selection()
    {
        if (Kb is not { } kb) return;
        var ids = Ids(kb);
        var selection = new List<FixedCard> { new(ids[5]), new(ids[40]) };
        var a = GrandCuisinier.Cook(kb, selection, Tiny with { Seed = 9 });
        var b = GrandCuisinier.Cook(kb, selection, Tiny with { Seed = 9 });
        Assert.NotEmpty(a.Proposals);
        Assert.Equal(a.Proposals.Select(p => GrandCuisinier.Key(p.Deck)), b.Proposals.Select(p => GrandCuisinier.Key(p.Deck)));
        Assert.Equal(a.Proposals.Select(p => p.Confirmation.Score), b.Proposals.Select(p => p.Confirmation.Score));
        Assert.All(a.Proposals, p =>
        {
            Assert.All(selection, f => Assert.Contains(p.Deck, x => x.CardId == f.CardId));
            Assert.Equal(0, p.Confirmation.Errors);
        });
        // Propositions différentes : au moins une carte libre différente deux à deux (2 cartes libres).
        for (var i = 0; i < a.Proposals.Count; i++)
            for (var j = i + 1; j < a.Proposals.Count; j++)
                Assert.NotEqual(Free(a.Proposals[i].Deck), Free(a.Proposals[j].Deck));
        // Classées de la meilleure à la moins bonne.
        Assert.Equal(a.Proposals.Select(p => p.Confirmation.Score).OrderDescending(), a.Proposals.Select(p => p.Confirmation.Score));

        string Free(IReadOnlyList<DuelSlot> d) =>
            string.Join(",", d.Select(x => x.CardId).Where(id => selection.All(f => f.CardId != id)).Order(StringComparer.Ordinal));
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public void Quatre_cartes_imposees_seuls_talents_et_places_changent()
    {
        if (Kb is not { } kb) return;
        var ids = kb.Cards.Where(c => c.Talents.Count > 0).Select(c => c.Id).Order(StringComparer.Ordinal).Take(4).ToArray();
        var res = GrandCuisinier.Cook(kb, ids.Select(i => new FixedCard(i)).ToList(), Tiny with { Seed = 4 });
        Assert.NotEmpty(res.Proposals);
        Assert.All(res.Proposals, p => Assert.Equal(ids.Order(StringComparer.Ordinal), p.Deck.Select(x => x.CardId).Order(StringComparer.Ordinal)));
        Assert.Equal(res.Proposals.Count, res.Proposals.Select(p => GrandCuisinier.Key(p.Deck)).Distinct().Count());
    }

    [Trait("Categorie", "Lent")]
    [Fact]
    public void Avancement_puis_fin_et_annulation()
    {
        if (Kb is not { } kb) return;
        var ids = Ids(kb);
        var reports = new List<CookProgress>();
        GrandCuisinier.Cook(kb, [new FixedCard(ids[3])], Tiny, progress: new Sync<CookProgress>(r => { lock (reports) reports.Add(r); }));
        Assert.Contains(reports, r => r.Stage == CookStage.Screening);
        Assert.Contains(reports, r => r.Stage == CookStage.Confirming);
        Assert.Equal(new CookProgress(CookStage.Done, 1), reports[^1]);
        Assert.All(reports, r => Assert.InRange(r.Fraction, 0, 1));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => GrandCuisinier.Cook(kb, [new FixedCard(ids[3])], Tiny, cts.Token));
    }

    private sealed class Sync<T>(Action<T> f) : IProgress<T> { public void Report(T value) => f(value); }
}
