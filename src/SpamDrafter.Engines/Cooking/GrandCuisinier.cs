using SpamDrafter.Engines.Evaluation;
using SpamDrafter.Engines.Profiles;
using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;

namespace SpamDrafter.Engines.Cooking;

/// <summary>Une carte imposée par le joueur ; <see cref="TalentId"/> null = le Cuisinier choisit le talent.</summary>
public sealed record FixedCard(string CardId, string? TalentId = null);

/// <summary>Réglages du Grand Cuisinier (réglages de l'outil, pas du jeu). Le budget est un nombre de duels, pas une durée :
/// le résultat ne dépend donc pas de la vitesse de l'ordinateur.</summary>
public sealed record CookSettings
{
    /// <summary>Joueur du premier tri (beaucoup de decks, peu de duels chacun).</summary>
    public string ScreenPlayer { get; init; } = "rapide";
    public int ScreenOpponents { get; init; } = 20;
    public int ScreenPairs { get; init; } = 1;
    /// <summary>Decks tirés au hasard au départ.</summary>
    public int Population { get; init; } = 48;
    /// <summary>Meilleurs decks gardés à chaque génération.</summary>
    public int Elites { get; init; } = 12;
    /// <summary>Variantes (mutations) par deck gardé et par génération.</summary>
    public int MutantsPerElite { get; init; } = 4;
    public int Generations { get; init; } = 20;
    /// <summary>Toutes les N générations, les meilleurs decks trouvés rejoignent le groupe d'adversaires du tri (0 = jamais).</summary>
    public int ChampionEvery { get; init; } = 5;
    /// <summary>Nombre de ces « champions ».</summary>
    public int Champions { get; init; } = 10;
    /// <summary>
    /// Vitesse (KI-054) : nombre de decks de référence utilisés pendant le TRI (les premiers de la liste, c.-à-d. les plus
    /// joués) ; la confirmation les utilise tous. 0 = tous.
    /// </summary>
    public int ScreenMeta { get; init; } = 0;
    /// <summary>
    /// Vitesse (KI-054) : moins de travail quand il reste moins de cartes à choisir. Avec 3 cartes libres : réglages
    /// complets ; avec 2 : générations × 0,4 et 6 candidats confirmés ; avec 1 (ou 0) : 6 générations et 5 candidats.
    /// Faux = toujours les réglages complets.
    /// </summary>
    public bool ScaleByFree { get; init; } = true;

    /// <summary>Joueur de la confirmation (les meilleurs candidats, plus de duels, autre groupe d'adversaires).</summary>
    public string ConfirmPlayer { get; init; } = "heuristique";
    public int ConfirmCandidates { get; init; } = 10;
    public int ConfirmOpponents { get; init; } = 30;
    public int ConfirmPairs { get; init; } = 1;

    /// <summary>Nombre de decks proposés (différents les uns des autres).</summary>
    public int Proposals { get; init; } = 3;
    public ulong Seed { get; init; } = 1;
    /// <summary>
    /// Decks de référence (« méta », D-050) : s'il y en a, ils remplacent les adversaires tirés au hasard, au tri comme
    /// à la confirmation. Le Cuisinier cherche alors des decks qui battent les decks réellement joués.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<DuelSlot>>? Meta { get; init; }
    /// <summary>
    /// Collection du joueur (phase 15, D-087) : identifiant de carte → niveau de talent (0 = aucun talent, 1 à 3 = les n premiers
    /// talents de la carte). Si elle est donnée, le Cuisinier ne propose que des cartes et des talents possédés. Null = tout le jeu.
    /// </summary>
    public IReadOnlyDictionary<string, int>? Owned { get; init; }
    /// <summary>
    /// Part des cartes essayées choisies selon les profils (lot G) : les cartes qui aident celles déjà dans le deck
    /// (éléments générés, critique donné) ont plus de chances d'être essayées. 0 = tirage purement au hasard.
    /// </summary>
    public double Guided { get; init; } = 0.5;
    /// <summary>
    /// Écart maximal (en score de confirmation) entre la meilleure proposition et les suivantes : une proposition
    /// différente mais beaucoup plus faible n'est pas montrée (réglage de présentation).
    /// </summary>
    public double ProposalGap { get; init; } = 0.20;

    public static CookSettings Default { get; } = new();
    /// <summary>Environ 4 fois moins de duels que le réglage par défaut.</summary>
    public static CookSettings Fast { get; } = new() { Population = 24, Elites = 8, MutantsPerElite = 3, Generations = 10, ConfirmCandidates = 6, ConfirmOpponents = 20 };
    /// <summary>Environ 2 à 3 fois plus de duels que le réglage par défaut.</summary>
    public static CookSettings Precise { get; } = new() { Generations = 40, ScreenOpponents = 30, ConfirmCandidates = 14, ConfirmOpponents = 40, ConfirmPairs = 2 };
}

public enum CookStage { Screening, Confirming, Done }

/// <summary>Avancement : étape et fraction du travail total (0 à 1).</summary>
public sealed record CookProgress(CookStage Stage, double Fraction);

/// <summary>
/// Un deck proposé : dernière note du tri, et évaluation de confirmation (joueur plus fin, contre d'autres adversaires
/// au hasard et contre les autres candidats).
/// </summary>
public sealed record CookProposal(IReadOnlyList<DuelSlot> Deck, double ScreenScore, DeckEvaluation Confirmation);

/// <param name="RandomOpponents">Adversaires de la confirmation autres que les candidats : decks au hasard, ou decks de référence.</param>
/// <param name="UsesMeta">Les adversaires sont les decks de référence (sinon, des decks tirés au hasard).</param>
public sealed record CookResult(IReadOnlyList<CookProposal> Proposals, int DecksScreened, int Duels, TimeSpan Elapsed, int RandomOpponents,
    bool UsesMeta = false, TimeSpan ScreenTime = default, TimeSpan ConfirmTime = default);

/// <summary>
/// Grand Cuisinier (phase 8, lot B) : complète une sélection de 1 à 4 cartes en un deck de 4 cartes avec talents.
/// <list type="number">
/// <item>Tri : des decks au hasard (cartes imposées + cartes libres + talents), puis des générations de variantes
/// des meilleurs (changer une carte libre, un talent, ou l'ordre de deux cartes), notées par le joueur rapide
/// contre un groupe d'adversaires ;</item>
/// <item>Confirmation : les meilleurs candidats, choisis variés, rejouent avec le joueur heuristique contre un AUTRE
/// groupe d'adversaires (pour ne pas garder un deck qui a juste eu de la chance au tri) ;</item>
/// <item>Propositions : les meilleurs decks confirmés, différents les uns des autres.</item>
/// </list>
/// Même sélection + mêmes réglages = mêmes propositions.
/// </summary>
public static class GrandCuisinier
{
    public static CookResult Cook(SimKnowledge kb, IReadOnlyList<FixedCard> selection, CookSettings? settings = null,
        CancellationToken cancel = default, IProgress<CookProgress>? progress = null)
    {
        var s = settings ?? CookSettings.Default;
        var size = kb.Settings.CardsPerDeck;
        Validate(kb, selection, size, s.Owned);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var rng = new Rng(s.Seed);
        var owned = s.Owned;
        var allIds = kb.Cards.Select(c => c.Id).Where(id => owned is null || owned.ContainsKey(id)).Order(StringComparer.Ordinal).ToArray();
        if (owned is not null && allIds.Length < size)
            throw new ArgumentException($"Il faut au moins {size} cartes possédées pour cuisiner un deck (tu en as {allIds.Length}).");
        var free = size - selection.Count;
        var guide = s.Guided > 0 ? new Guide(CardProfiles.For(kb), s.Guided) : null;

        var useMeta = s.Meta is { Count: > 0 };
        var randomScreen = useMeta
            ? ReferenceField.Of(s.ScreenMeta > 0 ? s.Meta!.Take(s.ScreenMeta) : s.Meta!)
            : ReferenceField.Random(kb, s.ScreenOpponents, s.Seed ^ 0x5C4EE11UL);
        var screenField = randomScreen;
        var randomConfirm = useMeta ? ReferenceField.Of(s.Meta!) : ReferenceField.Random(kb, s.ConfirmOpponents, s.Seed ^ 0xC0F1A3DUL);
        var screenSettings = new EvaluationSettings { Player = s.ScreenPlayer, PairsPerOpponent = s.ScreenPairs, Seed = s.Seed * 31 + 7, CollectStats = false };
        var confirmSettings = new EvaluationSettings { Player = s.ConfirmPlayer, PairsPerOpponent = s.ConfirmPairs, Seed = s.Seed * 37 + 11 };

        // Prévision pour l'avancement (un duel de confirmation, plus lent, compte pour ~20 duels rapides).
        var scale = s.ScaleByFree && free < 3;
        var generations = !scale ? s.Generations : free == 2 ? Math.Max(Math.Min(6, s.Generations), (int)Math.Round(s.Generations * 0.4)) : Math.Min(6, s.Generations);
        var confirmCount = !scale ? s.ConfirmCandidates : Math.Min(s.ConfirmCandidates, Math.Max(s.Proposals, free == 2 ? 6 : 5));
        var avgScreenField = randomScreen.Decks.Count + (s.ChampionEvery > 0 ? s.Champions * 0.75 : 0);
        var plannedScreen = (s.Population + generations * s.Elites * (s.MutantsPerElite + 0.5)) * avgScreenField * s.ScreenPairs * 2;
        var plannedConfirm = 20.0 * confirmCount * (randomConfirm.Decks.Count + confirmCount) * s.ConfirmPairs * 2;
        var duels = 0;

        // Notes de l'« époque » en cours (même groupe d'adversaires, donc comparables), decks déjà vus, dernière note connue.
        var scores = new Dictionary<string, (IReadOnlyList<DuelSlot> Deck, double Score)>();
        var seen = new HashSet<string>();
        var lastScore = new Dictionary<string, double>();
        void Screen(List<IReadOnlyList<DuelSlot>> batch)
        {
            if (batch.Count == 0) return;
            var baseDone = duels;
            var p = progress is null ? null : new SyncProgress(n =>
                progress.Report(new CookProgress(CookStage.Screening, Math.Min(0.99, (baseDone + n) / (plannedScreen + plannedConfirm)))));
            var evals = DeckEvaluator.EvaluateMany(kb, batch, screenField, screenSettings, cancel, p);
            duels += batch.Count * screenField.Decks.Count * s.ScreenPairs * 2;
            foreach (var e in evals)
            {
                var k = Key(e.Deck);
                scores[k] = (e.Deck, e.Score);
                seen.Add(k);
                lastScore[k] = e.Score;
            }
        }

        // 1. Population de départ (tous différents).
        var start = new List<IReadOnlyList<DuelSlot>>();
        for (var tries = 0; start.Count < s.Population && tries < s.Population * 20; tries++)
        {
            var d = RandomCompletion(kb, selection, allIds, rng, guide, owned);
            if (start.All(x => Key(x) != Key(d))) start.Add(d);
        }
        Screen(start);

        // 2. Générations de variantes des meilleurs.
        for (var g = 0; g < generations; g++)
        {
            cancel.ThrowIfCancellationRequested();
            if (s.ChampionEvery > 0 && g > 0 && g % s.ChampionEvery == 0)
            {
                // Les adversaires au hasard ne suffisent plus à départager les meilleurs (beaucoup gagnent presque tout) :
                // les meilleurs decks trouvés rejoignent le groupe d'adversaires. Nouvelle époque : on renote les meilleurs.
                var ranked0 = Ranked(scores).Select(r => r.Deck).ToList();
                var champions = PickDiverse(ranked0, selection, s.Champions, MinDifference(free, strict: false), fill: true);
                screenField = ReferenceField.Of(randomScreen.Decks.Concat(champions));
                var keep = ranked0.Take(2 * s.Elites).ToList();
                scores.Clear();
                Screen(keep);
            }
            var elites = Ranked(scores).Take(s.Elites).ToList();
            var batch = new List<IReadOnlyList<DuelSlot>>();
            var keys = new HashSet<string>();
            foreach (var e in elites)
                for (var m = 0; m < s.MutantsPerElite; m++)
                    for (var tries = 0; tries < 10; tries++)
                    {
                        var d = Mutate(kb, selection, e.Deck, allIds, rng, guide, owned);
                        var k = Key(d);
                        if (seen.Contains(k) || !keys.Add(k)) continue;
                        batch.Add(d);
                        break;
                    }
            if (batch.Count == 0) break;   // plus rien de nouveau à essayer (petite sélection : tout est vu)
            Screen(batch);
        }

        // 3. Confirmation des meilleurs candidats, choisis variés, avec le joueur plus fin, contre un autre groupe
        //    au hasard ET contre les autres candidats (pour départager des decks qui battent presque tous les decks au hasard).
        var ranked = Ranked(scores).ToList();
        // D'abord des candidats assez différents pour faire des propositions distinctes, puis les meilleurs suivants.
        var rankedDecks = ranked.Select(r => r.Deck).ToList();
        var candidates = PickDiverse(rankedDecks, selection, s.Proposals, MinDifference(free, strict: true), fill: false);
        foreach (var d in PickDiverse(rankedDecks, selection, confirmCount, MinDifference(free, strict: false), fill: true))
            if (candidates.Count < confirmCount && candidates.All(c => Key(c) != Key(d))) candidates.Add(d);
        var confirmField = ReferenceField.Of(randomConfirm.Decks.Concat(candidates));
        var screenTime = watch.Elapsed;
        var screenDone = duels;
        var cp = progress is null ? null : new SyncProgress(n =>
            progress.Report(new CookProgress(CookStage.Confirming, Math.Min(0.99, (screenDone + 20.0 * n) / (screenDone + plannedConfirm)))));
        var confirmed = DeckEvaluator.EvaluateMany(kb, candidates, confirmField, confirmSettings, cancel, cp);
        duels += candidates.Count * confirmField.Decks.Count * s.ConfirmPairs * 2;

        // 4. Propositions : meilleurs confirmés, différents les uns des autres.
        var byConfirm = confirmed.OrderByDescending(e => e.Score).ThenBy(e => Key(e.Deck), StringComparer.Ordinal).ToList();
        var chosen = PickDiverse(byConfirm.Select(e => e.Deck).ToList(), selection, s.Proposals, MinDifference(free, strict: true), fill: false);
        var proposals = chosen.Select(d => new CookProposal(d, lastScore[Key(d)], byConfirm.First(e => Key(e.Deck) == Key(d)))).ToList();
        if (proposals.Count > 1)
        {
            var bestScore = proposals[0].Confirmation.Score;
            proposals = proposals.Where((p, k) => k == 0 || p.Confirmation.Score >= bestScore - s.ProposalGap).ToList();
        }
        progress?.Report(new CookProgress(CookStage.Done, 1));
        return new CookResult(proposals, seen.Count, duels, watch.Elapsed, randomConfirm.Decks.Count, useMeta, screenTime, watch.Elapsed - screenTime);
    }

    /// <summary>Clé d'un deck (ordre des cartes compris : la place change le jeu, §1.2).</summary>
    public static string Key(IReadOnlyList<DuelSlot> deck) => string.Join("|", deck.Select(x => x.CardId + "/" + x.TalentId));

    internal static void Validate(SimKnowledge kb, IReadOnlyList<FixedCard> selection, int size, IReadOnlyDictionary<string, int>? owned = null)
    {
        if (selection.Count is 0 || selection.Count > size)
            throw new ArgumentException($"La sélection doit contenir de 1 à {size} cartes.");
        if (selection.Select(f => f.CardId).Distinct().Count() != selection.Count)
            throw new ArgumentException("Une carte ne peut pas être choisie deux fois.");
        foreach (var f in selection)
        {
            var card = kb.Cards.FirstOrDefault(c => c.Id == f.CardId) ?? throw new ArgumentException($"Carte inconnue : {f.CardId}.");
            if (f.TalentId is not null && !card.Talents.Contains(f.TalentId))
                throw new ArgumentException($"Le talent {f.TalentId} n'appartient pas à la carte {kb.CardName(card)}.");
            if (owned is not null)
            {
                if (!owned.ContainsKey(f.CardId)) throw new ArgumentException($"La carte {kb.CardName(card)} n'est pas dans ta collection.");
                if (f.TalentId is not null && !Allowed(kb, f.CardId, owned).Contains(f.TalentId))
                    throw new ArgumentException($"Tu ne possèdes pas ce talent de la carte {kb.CardName(card)}.");
            }
        }
    }

    /// <summary>La sélection complétée au hasard (cartes, talents non imposés, places) : la référence des mesures.</summary>
    public static IReadOnlyList<DuelSlot> RandomCompletion(SimKnowledge kb, IReadOnlyList<FixedCard> selection, Rng rng) =>
        RandomCompletion(kb, selection, kb.Cards.Select(c => c.Id).Order(StringComparer.Ordinal).ToArray(), rng);

    /// <summary>Le même deck (mêmes cartes, mêmes places) avec des talents au hasard, sauf les talents imposés.</summary>
    public static IReadOnlyList<DuelSlot> WithRandomTalents(SimKnowledge kb, IReadOnlyList<DuelSlot> deck,
        IReadOnlyList<FixedCard> selection, Rng rng) =>
        deck.Select(x => selection.FirstOrDefault(f => f.CardId == x.CardId)?.TalentId is { } t
            ? x with { TalentId = t } : x with { TalentId = RandomTalent(kb, x.CardId, rng) }).ToList();

    /// <summary>Talent au hasard parmi ceux de la carte, ou aucun (comme le Deck builder).</summary>
    private static string? RandomTalent(SimKnowledge kb, string cardId, Rng rng, IReadOnlyDictionary<string, int>? owned = null)
    {
        var talents = Allowed(kb, cardId, owned);
        var k = rng.Next(0, talents.Count + 1);
        return k == 0 ? null : talents[k - 1];
    }

    /// <summary>Les talents qu'on peut mettre sur la carte : tous (sans collection), ou les n premiers que le joueur possède.</summary>
    internal static IReadOnlyList<string> Allowed(SimKnowledge kb, string cardId, IReadOnlyDictionary<string, int>? owned)
    {
        var talents = kb.Card(cardId).Talents;
        if (owned is null) return talents;
        var level = owned.TryGetValue(cardId, out var l) ? l : 0;
        return talents.Take(Math.Max(0, level)).ToList();
    }

    internal static IReadOnlyList<DuelSlot> RandomCompletion(SimKnowledge kb, IReadOnlyList<FixedCard> selection, string[] allIds, Rng rng,
        Guide? guide = null, IReadOnlyDictionary<string, int>? owned = null)
    {
        var deck = selection.Select(f => new DuelSlot(f.CardId, f.TalentId ?? RandomTalent(kb, f.CardId, rng, owned))).ToList();
        var used = deck.Select(d => d.CardId).ToHashSet();
        while (deck.Count < kb.Settings.CardsPerDeck)
        {
            var id = guide?.Pick(deck.Select(d => d.CardId).ToList(), allIds, used, rng) ?? allIds[rng.Next(0, allIds.Length)];
            if (used.Add(id)) deck.Add(new DuelSlot(id, RandomTalent(kb, id, rng, owned)));
        }
        // Place des cartes au hasard (les cartes imposées n'ont pas de place imposée).
        return deck.OrderBy(_ => rng.NextULong()).ToList();
    }

    /// <summary>Une variante : changer une carte libre (avec un talent au hasard), changer un talent libre, ou échanger deux places.</summary>
    internal static IReadOnlyList<DuelSlot> Mutate(SimKnowledge kb, IReadOnlyList<FixedCard> selection, IReadOnlyList<DuelSlot> deck,
        string[] allIds, Rng rng, Guide? guide = null, IReadOnlyDictionary<string, int>? owned = null)
    {
        var d = deck.ToList();
        var fixedIds = selection.ToDictionary(f => f.CardId, f => f.TalentId);
        var freeSlots = Enumerable.Range(0, d.Count).Where(i => !fixedIds.ContainsKey(d[i].CardId)).ToList();
        var talentSlots = Enumerable.Range(0, d.Count)
            .Where(i => (!fixedIds.TryGetValue(d[i].CardId, out var t) || t is null) && Allowed(kb, d[i].CardId, owned).Count > 0).ToList();
        var roll = rng.NextDouble();
        if (freeSlots.Count > 0 && roll < 0.5)
        {
            var i = freeSlots[rng.Next(0, freeSlots.Count)];
            var used = d.Select(x => x.CardId).ToHashSet();
            var others = d.Where((_, k) => k != i).Select(x => x.CardId).ToList();
            var id = guide?.Pick(others, allIds, used, rng);
            if (id is null)
                do id = allIds[rng.Next(0, allIds.Length)]; while (used.Contains(id));
            d[i] = new DuelSlot(id, RandomTalent(kb, id, rng, owned));
        }
        else if (talentSlots.Count > 0 && roll < 0.85)
        {
            var i = talentSlots[rng.Next(0, talentSlots.Count)];
            var talents = Allowed(kb, d[i].CardId, owned);
            var options = new List<string?> { null };
            options.AddRange(talents);
            options.Remove(d[i].TalentId);
            d[i] = d[i] with { TalentId = options[rng.Next(0, options.Count)] };
        }
        else
        {
            var a = rng.Next(0, d.Count);
            var b = (a + 1 + rng.Next(0, d.Count - 1)) % d.Count;
            (d[a], d[b]) = (d[b], d[a]);
        }
        return d;
    }

    private static IEnumerable<(IReadOnlyList<DuelSlot> Deck, double Score)> Ranked(Dictionary<string, (IReadOnlyList<DuelSlot> Deck, double Score)> scores) =>
        scores.OrderByDescending(kv => kv.Value.Score).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value);

    /// <summary>
    /// Différence minimale entre deux decks retenus (nombre de cartes libres différentes). Propositions (strict) :
    /// au moins 1 carte libre différente, 2 si 3 cartes sont libres ; sans carte libre, seuls les talents ou l'ordre diffèrent.
    /// Candidats à confirmer : au moins 1 carte libre différente.
    /// </summary>
    internal static int MinDifference(int free, bool strict) => free == 0 ? 0 : strict && free >= 3 ? 2 : 1;

    /// <summary>
    /// Choisit dans l'ordre les decks assez différents des précédents. <paramref name="fill"/> : s'il en manque,
    /// complète avec les suivants (decks distincts) ; sinon, en rend moins.
    /// </summary>
    internal static List<IReadOnlyList<DuelSlot>> PickDiverse(IReadOnlyList<IReadOnlyList<DuelSlot>> ordered,
        IReadOnlyList<FixedCard> selection, int count, int minDifference, bool fill)
    {
        var fixedIds = selection.Select(f => f.CardId).ToHashSet();
        HashSet<string> Free(IReadOnlyList<DuelSlot> d) => d.Select(x => x.CardId).Where(id => !fixedIds.Contains(id)).ToHashSet();
        var chosen = new List<IReadOnlyList<DuelSlot>>();
        foreach (var d in ordered)
        {
            if (chosen.Count == count) break;
            var f = Free(d);
            if (chosen.All(c => f.Except(Free(c)).Count() >= minDifference && Key(c) != Key(d))) chosen.Add(d);
        }
        foreach (var d in ordered)
        {
            if (chosen.Count >= count) break;
            if (fill && chosen.All(c => Key(c) != Key(d))) chosen.Add(d);
        }
        return chosen;
    }

    /// <summary>
    /// Choix guidé d'une carte (lot G) : avec la probabilité <c>share</c>, une carte tirée en proportion de son affinité
    /// avec les cartes déjà présentes ; sinon null (l'appelant tire au hasard). Le hasard reste nécessaire : les profils
    /// ne voient pas tout (Tox, talents, effets rares), seule la simulation tranche.
    /// </summary>
    internal sealed class Guide(CardProfiles profiles, double share)
    {
        private const double Floor = 0.02;   // toute carte garde une petite chance
        public string? Pick(IReadOnlyList<string> present, string[] allIds, HashSet<string> used, Rng rng)
        {
            if (present.Count == 0 || rng.NextDouble() >= share) return null;
            var candidates = allIds.Where(id => !used.Contains(id)).ToArray();
            var weights = candidates.Select(c => Floor + present.Sum(p => profiles.Pair(c, p))).ToArray();
            var x = rng.NextDouble() * weights.Sum();
            for (var k = 0; k < candidates.Length; k++)
            {
                x -= weights[k];
                if (x <= 0) return candidates[k];
            }
            return candidates[^1];
        }
    }

    /// <summary>Avancement appelé directement (sans passer par le fil de l'interface).</summary>
    private sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
