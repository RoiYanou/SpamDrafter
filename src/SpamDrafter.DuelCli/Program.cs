using SpamDrafter.Core.Decks;
using SpamDrafter.Engines.Analysis;
using SpamDrafter.Engines.Cooking;
using SpamDrafter.Engines.Evaluation;
using SpamDrafter.Engines.Meta;
using SpamDrafter.Simulator.Data;
using SpamDrafter.Simulator.Engine;
using SpamDrafter.Simulator.Players;

// Petite commande de la phase 5 : fait jouer deux decks sauvegardés l'un contre l'autre
// (deux joueurs « au hasard ») et affiche le journal du duel, ou un bilan sur plusieurs parties.

Console.OutputEncoding = System.Text.Encoding.UTF8;

string? knowledgeDir = null, decksDir = null;
ulong seed = (ulong)Environment.TickCount64;
var games = 1;
var bench = 0;
var tournament = 0;
var evaluate = false;
var correlation = 0;
var opponents = 50;
var pairs = 2;
ulong fieldSeed = 2026;
string? cook = null;
var cookTest = 0;
var cookGames = 100;
var cookPreset = "normal";
var analyse = false;
var useMeta = true;
double? guided = null;
string? metaRanking = null;
var metaTest = 0;
var metaCards = 1;
var sheet = false;
string? buildMeta = null;
string? synthMeta = null;
string? checkMeta = null;
var metaSize = 20;
var metaRounds = 3;
var metaSample = 0;
var resume = false;
var analyseTest = 0;
string? reportFile = null;
string p1Name = "hasard", p2Name = "hasard";
var list = false;
var search = SearchSettings.Default;
var names = new List<string>();

for (var i = 0; i < args.Length; i++)
{
    // Argument facultatif (chemin de fichier) : vide si l'argument suivant est une option ou absent.
    string NextOrDefault() => i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "";
    switch (args[i])
    {
        case "--calculer-meta" or "--build-meta": buildMeta = NextOrDefault(); break;
        case "--meta-synthetique" or "--synthetic-meta": synthMeta = NextOrDefault(); break;
        case "--controle-meta" or "--check-meta": checkMeta = NextOrDefault(); break;
        case "--meta-taille": metaSize = int.Parse(args[++i]); break;
        case "--meta-tours": metaRounds = int.Parse(args[++i]); break;
        case "--meta-cartes": metaSample = int.Parse(args[++i]); break;
        case "--reprendre" or "--resume": resume = true; break;
        case "--knowledge": knowledgeDir = args[++i]; break;
        case "--decks": decksDir = args[++i]; break;
        case "--seed": seed = ulong.Parse(args[++i]); break;
        case "--games": games = int.Parse(args[++i]); break;
        case "--list": list = true; break;
        case "--bench": bench = int.Parse(args[++i]); break;
        case "--tournament" or "--tournoi": tournament = int.Parse(args[++i]); break;
        case "--p1": p1Name = args[++i]; break;
        case "--evaluer" or "--evaluate": evaluate = true; break;
        case "--correlation": correlation = int.Parse(args[++i]); break;
        case "--cuisiner" or "--cook": cook = args[++i]; break;
        case "--test-cuisinier" or "--cook-test": cookTest = int.Parse(args[++i]); break;
        case "--duels-test": cookGames = int.Parse(args[++i]); break;
        case "--reglage" or "--preset": cookPreset = args[++i]; break;
        case "--analyser" or "--analyse": analyse = true; break;
        case "--sans-meta" or "--no-meta": useMeta = false; break;
        case "--guide" or "--guided": guided = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
        case "--test-meta": metaTest = int.Parse(args[++i]); break;
        case "--cartes-imposees": metaCards = int.Parse(args[++i]); break;
        case "--classement-meta": metaRanking = args[++i]; break;
        case "--fiche" or "--sheet": analyse = true; sheet = true; break;
        case "--test-analyseur" or "--analyse-test": analyseTest = int.Parse(args[++i]); break;
        case "--rapport" or "--report": reportFile = args[++i]; break;
        case "--adversaires" or "--opponents": opponents = int.Parse(args[++i]); break;
        case "--paires" or "--pairs": pairs = int.Parse(args[++i]); break;
        case "--groupe" or "--field-seed": fieldSeed = ulong.Parse(args[++i]); break;
        case "--p2": p2Name = args[++i]; break;
        case "--topk": search = search with { TopK = int.Parse(args[++i]) }; break;
        case "--rollouts": search = search with { Rollouts = int.Parse(args[++i]) }; break;
        case "--horizon": search = search with { Horizon = int.Parse(args[++i]) }; break;
        case "--greedy-rest": search = search with { GreedyRest = true }; break;
        case "-h" or "--help": PrintHelp(); return 0;
        default: names.Add(args[i]); break;
    }
}

knowledgeDir ??= FindKnowledgeDirectory();
string MetaPath(string p) => p == "" ? SyntheticMeta.DefaultPath(knowledgeDir!) : p;
if (knowledgeDir is null)
{
    Console.Error.WriteLine("Base de connaissances introuvable. Utilise --knowledge <dossier knowledge/version>.");
    return 2;
}
decksDir ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpamDrafter", "decks");

var kb = SimKnowledge.Load(knowledgeDir);

// Decks de référence (D-050) : les decks sauvegardés cochés « référence », de la même version de SPAM.
var meta = !useMeta ? new List<NamedDeck>() : new DeckStore(decksDir).LoadAll()
    .Where(d => d.IsReference && d.Slots.Count == kb.Settings.CardsPerDeck && d.GameVersion == kb.GameVersion)
    .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
    .Select(d => new NamedDeck(d.Name, d.Slots.Select(x => new DuelSlot(x.CardId, x.TalentId)).ToList()))
    .ToList();
IReadOnlyList<NamedDeck>? MetaOrNull() => meta.Count >= 2 ? meta : null;

if (tournament > 0)
{
    // Tournoi : chaque paire de joueurs s'affronte sur N duels (decks au hasard, miroir, places échangées).
    var players = new[] { "recherche", "heuristique", "rapide", "hasard" };
    Console.WriteLine($"Tournoi : {tournament} duels par paire, decks au hasard en miroir, graine {seed}.");
    Console.WriteLine("| A | B | Victoires A | Victoires B | Égalités | Erreurs | Score de A | Durée |");
    Console.WriteLine("|---|---|---|---|---|---|---|---|");
    var failed = 0;
    for (var i = 0; i < players.Length; i++)
        for (var j = i + 1; j < players.Length; j++)
        {
            var res = Bench.Run(kb, Factory(players[i])!, Factory(players[j])!, tournament, seed);
            failed += res.Errors;
            Console.WriteLine($"| {players[i]} | {players[j]} | {res.WinsA} | {res.WinsB} | {res.Draws} | {res.Errors} | " +
                              $"{100 * res.ScoreA:0.0} % (± {100 * res.Margin95:0.0}) | {res.Elapsed.TotalSeconds:0} s |");
        }
    return failed == 0 ? 0 : 1;
}

CookSettings? Preset(ulong s) => (cookPreset.ToLowerInvariant() switch
{
    "rapide" or "fast" => CookSettings.Fast with { Seed = s },
    "normal" or "default" => CookSettings.Default with { Seed = s },
    "precis" or "précis" or "precise" => CookSettings.Precise with { Seed = s },
    _ => null,
}) is { } c ? c with { Meta = MetaOrNull()?.Select(m => m.Deck).ToList(), Guided = guided ?? c.Guided } : null;
string CardLabel(DuelSlot x) => kb.CardName(kb.Card(x.CardId)) + (x.TalentId is { } t ? $" ({kb.AbilityName(t)})" : " (sans talent)");

if (cook is not null)
{
    // Phase 8, lot B : compléter une sélection de cartes (noms séparés par des virgules ; « Nom:Talent » pour imposer un talent).
    if (Preset(seed) is not { } cs) { Console.Error.WriteLine("Réglage inconnu (rapide, normal, precis)."); return 2; }
    var selection = new List<FixedCard>();
    // Séparateur « ; » si présent (certains noms contiennent une virgule, ex. « Sarah, Mélodie Marine »), sinon « , ».
    foreach (var part in cook.Split(cook.Contains(';') ? ';' : ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var bits = part.Split(':', 2, StringSplitOptions.TrimEntries);
        var card = kb.Cards.FirstOrDefault(c => string.Equals(kb.CardName(c), bits[0], StringComparison.CurrentCultureIgnoreCase) || c.Id == bits[0]);
        if (card is null) { Console.Error.WriteLine($"Carte introuvable : « {bits[0]} »."); return 2; }
        string? talent = null;
        if (bits.Length == 2)
        {
            talent = card.Talents.FirstOrDefault(t => string.Equals(kb.AbilityName(t), bits[1], StringComparison.CurrentCultureIgnoreCase) || t == bits[1]);
            if (talent is null) { Console.Error.WriteLine($"Talent introuvable pour {kb.CardName(card)} : « {bits[1]} »."); return 2; }
        }
        selection.Add(new FixedCard(card.Id, talent));
    }
    var last = -10;
    var res = GrandCuisinier.Cook(kb, selection, cs, progress: new Progress<CookProgress>(pr =>
    {
        var pct = (int)(pr.Fraction * 100);
        if (pct / 10 != last / 10) { last = pct; Console.Error.Write($"{pct} % "); }
    }));
    Console.Error.WriteLine();
    Console.WriteLine($"Grand Cuisinier : {res.DecksScreened} decks triés, {res.Duels} duels, {res.Elapsed.TotalSeconds:0} s " +
                      $"(tri {res.ScreenTime.TotalSeconds:0} s, confirmation {res.ConfirmTime.TotalSeconds:0} s ; réglage {cookPreset}, graine {seed}).");
    var n = 0;
    foreach (var pr in res.Proposals)
    {
        n++;
        Console.WriteLine($"Proposition {n} : {100 * pr.Confirmation.Score:0.0} % (± {100 * pr.Confirmation.Margin95:0.0}) contre {res.RandomOpponents} {(res.UsesMeta ? "decks de référence" : "adversaires au hasard")} et les autres candidats " +
                          $"[tri : {100 * pr.ScreenScore:0} %]");
        foreach (var x in pr.Deck) Console.WriteLine($"  - {CardLabel(x)}");
    }
    return res.Proposals.Sum(p => p.Confirmation.Errors) == 0 ? 0 : 1;
}

if (cookTest > 0)
{
    // Phase 8, lot B : banc de test du Cuisinier (seuils D-049). Pour chaque sélection au hasard (1, 2 ou 3 cartes) :
    // deck généré contre la même sélection complétée au hasard, puis contre ses propres cartes avec des talents au hasard.
    // Duels joués par le joueur heuristique avec des graines jamais vues par le Cuisinier.
    if (Preset(1) is null) { Console.Error.WriteLine("Réglage inconnu (rapide, normal, precis)."); return 2; }
    var heur = PlayerCatalog.Factory("heuristique")!;
    var testRng = new Rng(seed);
    var ids = kb.Cards.Select(c => c.Id).Order(StringComparer.Ordinal).ToArray();
    Console.WriteLine($"Test du Cuisinier : {cookTest} sélections, réglage {cookPreset}, {cookGames} duels par mesure, graine {seed}.");
    Console.WriteLine("| # | Sélection | Durée | Contre complétion au hasard | Contre talents au hasard |");
    Console.WriteLine("|---|---|---|---|---|");
    int wc = 0, nc = 0, wt = 0, nt = 0, errs = 0;
    double dc = 0, dt = 0, maxTime = 0, totalTime = 0;
    for (var k = 0; k < cookTest; k++)
    {
        var count = 1 + k % 3;
        var sel = ids.OrderBy(_ => testRng.NextULong()).Take(count).Select(id => new FixedCard(id)).ToList();
        var cookSeed = testRng.NextULong();
        var benchSeed = testRng.NextULong();
        var res = GrandCuisinier.Cook(kb, sel, Preset(cookSeed));
        var best = res.Proposals[0].Deck;
        var vsRandom = Bench.Run(kb, heur, heur, cookGames, benchSeed, r => (best, GrandCuisinier.RandomCompletion(kb, sel, r)));
        var vsTalents = Bench.Run(kb, heur, heur, cookGames, benchSeed ^ 0x7A1E57UL, r => (best, GrandCuisinier.WithRandomTalents(kb, best, sel, r)));
        wc += vsRandom.WinsA; dc += vsRandom.Draws; nc += vsRandom.Counted;
        wt += vsTalents.WinsA; dt += vsTalents.Draws; nt += vsTalents.Counted;
        errs += vsRandom.Errors + vsTalents.Errors + res.Proposals.Sum(p => p.Confirmation.Errors);
        maxTime = Math.Max(maxTime, res.Elapsed.TotalSeconds);
        totalTime += res.Elapsed.TotalSeconds;
        Console.WriteLine($"| {k + 1} | {string.Join(", ", sel.Select(f => kb.CardName(kb.Card(f.CardId))))} | {res.Elapsed.TotalSeconds:0} s | " +
                          $"{100 * vsRandom.ScoreA:0.0} % | {100 * vsTalents.ScoreA:0.0} % |");
    }
    double Score(int w, double d, int n2) => n2 == 0 ? 0 : (w + d / 2) / n2;
    double Margin(double p, int n2) => n2 == 0 ? 1 : 1.96 * Math.Sqrt(p * (1 - p) / n2);
    var sc = Score(wc, dc, nc);
    var st = Score(wt, dt, nt);
    Console.WriteLine($"Ensemble : contre complétion au hasard {100 * sc:0.0} % (± {100 * Margin(sc, nc):0.0}) sur {nc} duels (seuil 65 %) · " +
                      $"contre talents au hasard {100 * st:0.0} % (± {100 * Margin(st, nt):0.0}) sur {nt} duels (seuil 55 %).");
    Console.WriteLine($"Durée de génération : moyenne {totalTime / cookTest:0} s, maximum {maxTime:0} s (seuil 120 s) · erreurs : {errs}.");
    return errs == 0 ? 0 : 1;
}

AnalysisSettings? AnalysisPreset(ulong s) => (cookPreset.ToLowerInvariant() switch
{
    "rapide" or "fast" => AnalysisSettings.Fast with { Seed = s },
    "normal" or "default" => AnalysisSettings.Default with { Seed = s },
    "precis" or "précis" or "precise" => AnalysisSettings.Precise with { Seed = s },
    _ => null,
}) is { } a ? a with { Meta = MetaOrNull() } : null;

if (metaRanking is not null)
{
    // Phase 8, lot H : l'IA classe-t-elle les decks de référence comme les vrais joueurs ? Chaque deck affronte tous
    // les autres (joueur --p1), puis on compare ce classement aux taux de victoire réels (fichier « deck;pourcent »).
    if (meta.Count < 2) { Console.Error.WriteLine("Il faut au moins 2 decks de référence."); return 2; }
    var real = File.ReadAllLines(metaRanking).Skip(1).Select(l => l.Split(';')).Where(x => x.Length >= 2)
        .ToDictionary(x => x[0].Trim(), x => double.Parse(x[1], System.Globalization.CultureInfo.InvariantCulture));
    var player = p1Name == "hasard" ? "heuristique" : p1Name;
    var field = ReferenceField.Of(meta.Select(m => m.Deck));
    var settings = new EvaluationSettings { Player = player, PairsPerOpponent = pairs, Seed = seed, CollectStats = false };
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var evals = DeckEvaluator.EvaluateMany(kb, meta.Select(m => m.Deck).ToList(), field, settings);
    var rows = meta.Select((m, k) => (Name: m.Name, Sim: evals[k].Score, Real: real.FirstOrDefault(r => m.Name.StartsWith(r.Key + " ")).Value)).ToList();
    Console.WriteLine($"Classement des {meta.Count} decks de référence entre eux : joueur « {player} », {pairs} paire(s) par adversaire, graine {seed}, " +
                      $"{watch.Elapsed.TotalSeconds:0} s, erreurs : {evals.Sum(e => e.Errors)}.");
    Console.WriteLine("| Deck | Simulation | Réel |");
    Console.WriteLine("|---|---|---|");
    foreach (var r in rows.OrderByDescending(r => r.Sim)) Console.WriteLine($"| {r.Name.Split(" — ")[0]} | {100 * r.Sim:0} % | {r.Real:0} % |");
    var known = rows.Where(r => r.Real > 0).ToList();
    Console.WriteLine($"Corrélation de Spearman simulation / réel : {Statistics.Spearman(known.Select(r => r.Sim).ToList(), known.Select(r => r.Real).ToList()):0.00} " +
                      $"({known.Count} decks) · écart type des scores simulés : {100 * Statistics.StdDev(rows.Select(r => r.Sim).ToList()):0} %.");
    return 0;
}

if (buildMeta is not null)
{
    // Phase 8, lot I (D-052) : calcul de la méta synthétique, sans voir les decks de référence.
    var path = MetaPath(buildMeta);
    if (Preset(1) is not { } cs0) { Console.Error.WriteLine("Réglage inconnu (rapide, normal, precis)."); return 2; }
    var cards = kb.Cards.Select(c => c.Id).Order(StringComparer.Ordinal).ToList();
    if (metaSample > 0 && metaSample < cards.Count)
    {
        var r0 = new Rng(seed);
        cards = cards.OrderBy(_ => r0.NextULong()).Take(metaSample).Order(StringComparer.Ordinal).ToList();
    }
    var ms = new MetaSettings { Size = metaSize, MaxRounds = metaRounds, Seed = seed, Cards = cards, Cook = cs0 with { Proposals = 1, Meta = null } };
    IReadOnlyList<MetaDeck>? start = resume && File.Exists(path) ? SyntheticMeta.Load(path, kb) : null;
    Console.WriteLine($"Méta synthétique : {cards.Count} cartes de départ, méta de {metaSize} decks, {metaRounds} tour(s) au plus, " +
                      $"réglage {cookPreset}, graine {seed}{(start is null ? "" : $", reprise de {path}")}. Decks de référence : non utilisés.");
    var lastStep = "";
    var mres = SyntheticMeta.Build(kb, ms, start, progress: new SyncProgress<MetaProgress>(pr =>
    {
        var step = $"tour {pr.Round} : {pr.Step.Split(" (")[0]}";
        if (step != lastStep) { lastStep = step; Console.Error.WriteLine($"[{DateTime.Now:HH:mm}] {step}"); }
    }), roundDone: r =>
    {
        SyntheticMeta.Save(path, kb, r.Meta, seed, r.Round, false, r.Elapsed);
        Console.WriteLine($"Tour {r.Round} : {r.Candidates} candidats, {r.Kept}/{r.Meta.Count} decks gardés du tour précédent, " +
                          $"{r.Duels} duels, {r.Elapsed.TotalMinutes:0.0} min, erreurs : {r.Errors} (enregistré : {path}).");
    });
    SyntheticMeta.Save(path, kb, mres.Meta, seed, mres.Rounds[^1].Round, mres.Stable, mres.Elapsed);
    Console.WriteLine($"Méta {(mres.Stable ? "stable" : "non stable (tours épuisés)")} en {mres.Elapsed.TotalMinutes:0} min · erreurs : {mres.Errors}.");
    Console.WriteLine("| # | Score | Deck | Carte de départ |");
    Console.WriteLine("|---|---|---|---|");
    var idx = 0;
    foreach (var m in mres.Meta)
        Console.WriteLine($"| {++idx} | {100 * m.Score:0} % | {string.Join(", ", m.Deck.Select(CardLabel))} | {kb.CardName(kb.Card(m.FromCard))} |");
    return mres.Errors == 0 ? 0 : 1;
}

if (checkMeta is not null)
{
    // Phase 8, lot I : contrôle. Les decks de la méta synthétique contre les decks de référence (joueur heuristique).
    if (meta.Count < 2) { Console.Error.WriteLine("Il faut au moins 2 decks de référence (case « deck de référence »)."); return 2; }
    var synth = SyntheticMeta.Load(MetaPath(checkMeta), kb);
    var field = ReferenceField.Of(meta.Select(m => m.Deck));
    var check = new EvaluationSettings { Player = "heuristique", PairsPerOpponent = cookGames, Seed = seed ^ 0xC4EC7UL, CollectStats = false };
    var evals = DeckEvaluator.EvaluateMany(kb, synth.Select(m => m.Deck).Concat(meta.Select(m => m.Deck)).ToList(), field, check);
    var sEv = evals.Take(synth.Count).ToList();
    var rEv = evals.Skip(synth.Count).ToList();
    Console.WriteLine($"Contrôle : {synth.Count} decks de la méta synthétique contre les {meta.Count} decks de référence, {cookGames} paire(s) par adversaire, graine {seed}.");
    Console.WriteLine("| # | Contre la référence | Deck |");
    Console.WriteLine("|---|---|---|");
    for (var k = 0; k < synth.Count; k++)
        Console.WriteLine($"| {k + 1} | {100 * sEv[k].Score:0.0} % (± {100 * sEv[k].Margin95:0}) | {string.Join(", ", synth[k].Deck.Select(CardLabel))} |");
    Console.WriteLine($"Ensemble : méta synthétique {100 * sEv.Average(e => e.Score):0.0} % en moyenne contre la référence " +
                      $"(≥ 50 % : {sEv.Count(e => e.Score >= 0.5)}/{sEv.Count}) · decks de référence entre eux : {100 * rEv.Average(e => e.Score):0.0} % · " +
                      $"erreurs : {evals.Sum(e => e.Errors)}.");
    return evals.Sum(e => e.Errors) == 0 ? 0 : 1;
}

if (metaTest > 0)
{
    // Phase 8, lot F (critère de compétitivité, D-050) : pour N decks de référence, une de leurs cartes est imposée au
    // Cuisinier ; sa meilleure proposition et le vrai deck sont ensuite mesurés contre tous les decks de référence
    // (joueur heuristique, graines jamais vues par le Cuisinier). Critère : proposition ≥ 50 % et ≥ le vrai deck.
    if (meta.Count < 2) { Console.Error.WriteLine("Il faut au moins 2 decks de référence (case « deck de référence »)."); return 2; }
    if (Preset(1) is null) { Console.Error.WriteLine("Réglage inconnu (rapide, normal, precis)."); return 2; }
    var field = ReferenceField.Of(meta.Select(m => m.Deck));
    var check = new EvaluationSettings { Player = "heuristique", PairsPerOpponent = cookGames, Seed = seed ^ 0x7E57UL, CollectStats = false };
    // Lot I : avec --meta-synthetique, le Cuisinier ne voit PAS les decks de référence (il affronte la méta synthétique).
    // « hasard » : le Cuisinier affronte des decks au hasard (point de comparaison, sans méta du tout).
    var cookMeta = synthMeta is null ? Preset(1)!.Meta
        : synthMeta == "hasard" ? null
        : SyntheticMeta.Load(MetaPath(synthMeta), kb).Select(m => m.Deck).ToList();
    if (synthMeta is not null)
        Console.WriteLine(cookMeta is null ? "Le Cuisinier affronte des decks au hasard : il ne voit pas les decks de référence."
            : $"Le Cuisinier affronte la méta synthétique ({cookMeta.Count} decks) : il ne voit pas les decks de référence.");
    string Name(string id) => kb.CardName(kb.Card(id));
    Console.WriteLine($"Test contre la méta : {Math.Min(metaTest, meta.Count)} decks de référence, réglage {cookPreset}, " +
                      $"{meta.Count} adversaires × {cookGames} paire(s), graine {seed}.");
    Console.WriteLine("| Deck de référence | Carte imposée | Durée | Vrai deck | Proposition | Écart | Proposition |");
    Console.WriteLine("|---|---|---|---|---|---|---|");
    int ok50 = 0, okRef = 0, n = 0, errs = 0;
    double sumP = 0, sumR = 0, maxTime = 0;
    for (var k = 0; k < Math.Min(metaTest, meta.Count); k++)
    {
        var refDeck = meta[k];
        var imposed = Enumerable.Range(0, Math.Clamp(metaCards, 1, 3)).Select(j => refDeck.Deck[(k + j) % refDeck.Deck.Count].CardId).ToList();
        var card = imposed[0];
        var res = GrandCuisinier.Cook(kb, imposed.Select(c => new FixedCard(c)).ToList(), Preset(seed + (ulong)k)! with { Meta = cookMeta });
        var best = res.Proposals[0].Deck;
        var evals = DeckEvaluator.EvaluateMany(kb, [refDeck.Deck, best], field, check);
        errs += evals.Sum(e => e.Errors);
        var (r, p) = (evals[0].Score, evals[1].Score);
        n++; sumP += p; sumR += r; maxTime = Math.Max(maxTime, res.Elapsed.TotalSeconds);
        if (p >= 0.5) ok50++;
        if (p >= r) okRef++;
        Console.WriteLine($"| {refDeck.Name} | {string.Join(" + ", imposed.Select(Name))} | {res.Elapsed.TotalSeconds:0} s | {100 * r:0.0} % | {100 * p:0.0} % (± {100 * evals[1].Margin95:0}) | " +
                          $"{100 * (p - r):+0.0;-0.0} | {string.Join(", ", best.Select(x => Name(x.CardId)))} |");
    }
    Console.WriteLine($"Ensemble : proposition {100 * sumP / n:0.0} % en moyenne contre la méta (vrais decks : {100 * sumR / n:0.0} %) · " +
                      $"≥ 50 % : {ok50}/{n} · ≥ vrai deck : {okRef}/{n} · durée maximale {maxTime:0} s · erreurs : {errs}.");
    return errs == 0 ? 0 : 1;
}

if (analyseTest > 0)
{
    // Phase 8, lot C : banc de test de l'Analyseur. Pour N decks au hasard : rapport reproductible (deux analyses
    // identiques), puis le deck avec les talents conseillés contre le même deck avec des talents au hasard
    // (joueur heuristique, graines jamais vues par l'Analyseur).
    if (AnalysisPreset(1) is null) { Console.Error.WriteLine("Réglage inconnu (rapide, normal, precis)."); return 2; }
    var heur = PlayerCatalog.Factory("heuristique")!;
    var testRng = new Rng(seed);
    Console.WriteLine($"Test de l'Analyseur : {analyseTest} decks au hasard, réglage {cookPreset}, {cookGames} duels par mesure, graine {seed}.");
    Console.WriteLine("| # | Deck | Durée | Reproductible | Talents changés | Conseillés contre talents au hasard |");
    Console.WriteLine("|---|---|---|---|---|---|");
    int w = 0, cnt = 0, errs = 0, notSame = 0;
    double dr = 0, maxTime = 0, totalTime = 0;
    for (var k = 0; k < analyseTest; k++)
    {
        var deck = Bench.RandomDeck(kb, testRng);
        var aSeed = testRng.NextULong();
        var benchSeed = testRng.NextULong();
        var rep1 = GrandAnalyseur.Analyse(kb, deck, AnalysisPreset(aSeed));
        var same = true;
        if (k < 3)   // la double analyse coûte cher : vérifiée sur les 3 premiers decks
        {
            var rep2 = GrandAnalyseur.Analyse(kb, deck, AnalysisPreset(aSeed));
            same = Strip(ReportWriter.ToMarkdown(kb, rep1)) == Strip(ReportWriter.ToMarkdown(kb, rep2));
            if (!same) notSame++;
        }
        var advised = deck.Select((x, i) => x with { TalentId = rep1.Talents[i].Best }).ToList();
        var changed = advised.Where((x, i) => x.TalentId != deck[i].TalentId).Count();
        var none = new List<FixedCard>();
        var res = Bench.Run(kb, heur, heur, cookGames, benchSeed, r => (advised, GrandCuisinier.WithRandomTalents(kb, advised, none, r)));
        w += res.WinsA; dr += res.Draws; cnt += res.Counted;
        errs += res.Errors + rep1.Errors;
        maxTime = Math.Max(maxTime, rep1.Elapsed.TotalSeconds);
        totalTime += rep1.Elapsed.TotalSeconds;
        Console.WriteLine($"| {k + 1} | {string.Join(", ", deck.Select(x => kb.CardName(kb.Card(x.CardId))))} | {rep1.Elapsed.TotalSeconds:0} s | " +
                          $"{(k < 3 ? (same ? "oui" : "NON") : "—")} | {changed} | {100 * res.ScoreA:0.0} % |");
    }
    var sc = cnt == 0 ? 0 : (w + dr / 2) / cnt;
    Console.WriteLine($"Ensemble : talents conseillés contre talents au hasard {100 * sc:0.0} % (± {100 * 1.96 * Math.Sqrt(sc * (1 - sc) / Math.Max(cnt, 1)):0.0}) " +
                      $"sur {cnt} duels (seuil 55 %).");
    Console.WriteLine($"Durée d'une analyse : moyenne {totalTime / analyseTest:0} s, maximum {maxTime:0} s · rapports différents : {notSame} · erreurs : {errs}.");
    return errs == 0 && notSame == 0 ? 0 : 1;

    // La ligne « Méthode » contient la durée, qui change d'une fois à l'autre.
    static string Strip(string md) => string.Join("\n", md.Split('\n').Where(l => !l.Contains(" duels simulés (SPAM")));
}

if (correlation > 0)
{
    // Phase 8, lot A : le classement des decks par le joueur « rapide » ressemble-t-il à celui du joueur « heuristique » ?
    var field = ReferenceField.Random(kb, opponents, fieldSeed);
    var deckRng = new Rng(seed);
    var decksToRank = Enumerable.Range(0, correlation).Select(_ => (IReadOnlyList<DuelSlot>)Bench.RandomDeck(kb, deckRng)).ToList();
    Console.WriteLine($"Corrélation des classements : {correlation} decks au hasard (graine {seed}), groupe de {opponents} adversaires " +
                      $"(graine {fieldSeed}), {pairs} paire(s) par adversaire.");
    double[] Scores(string player, ulong evalSeed, out double seconds, out int errs)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var settings = new EvaluationSettings { Player = player, PairsPerOpponent = pairs, Seed = evalSeed, CollectStats = false };
        var evals = decksToRank.Select(d => DeckEvaluator.Evaluate(kb, d, field, settings)).ToList();
        seconds = watch.Elapsed.TotalSeconds;
        errs = evals.Sum(e => e.Errors);
        return evals.Select(e => e.Score).ToArray();
    }
    var fastA = Scores("rapide", 1, out var tFastA, out var eFastA);
    var fastB = Scores("rapide", 2, out var tFastB, out var eFastB);
    var heurA = Scores("heuristique", 1, out var tHeurA, out var eHeurA);
    var heurB = Scores("heuristique", 2, out var tHeurB, out var eHeurB);
    Console.WriteLine($"Durées : rapide {tFastA:0} s + {tFastB:0} s · heuristique {tHeurA:0} s + {tHeurB:0} s · erreurs : {eFastA + eFastB + eHeurA + eHeurB}");
    Console.WriteLine($"Écart type des scores : rapide {100 * Statistics.StdDev(fastA):0.0} % · heuristique {100 * Statistics.StdDev(heurA):0.0} %");
    Console.WriteLine("| Comparaison | Spearman |");
    Console.WriteLine("|---|---|");
    Console.WriteLine($"| rapide (hasard 1) / rapide (hasard 2) — répétabilité | {Statistics.Spearman(fastA, fastB):0.00} |");
    Console.WriteLine($"| heuristique (hasard 1) / heuristique (hasard 2) — répétabilité | {Statistics.Spearman(heurA, heurB):0.00} |");
    var fastMean = fastA.Zip(fastB, (x, y) => (x + y) / 2).ToArray();
    var heurMean = heurA.Zip(heurB, (x, y) => (x + y) / 2).ToArray();
    Console.WriteLine($"| rapide / heuristique (même hasard 1) | {Statistics.Spearman(fastA, heurA):0.00} |");
    Console.WriteLine($"| rapide (moyenne des 2) / heuristique (moyenne des 2) | {Statistics.Spearman(fastMean, heurMean):0.00} |");
    return eFastA + eFastB + eHeurA + eHeurB == 0 ? 0 : 1;
}

if (bench > 0 && names.Count == 0)
{
    // Banc d'essai sur des decks au hasard (le même pour les deux joueurs), places échangées à chaque paire.
    if (Factory(p1Name) is not { } fa || Factory(p2Name) is not { } fb)
    {
        Console.Error.WriteLine($"Joueur inconnu. Joueurs disponibles : {string.Join(", ", PlayerCatalog.Names)}.");
        return 2;
    }
    var res = Bench.Run(kb, fa, fb, bench, seed);
    Console.WriteLine($"Banc d'essai (decks au hasard, miroir, graine {seed}) :");
    Console.WriteLine(res.Summary($"A ({p1Name})", $"B ({p2Name})"));
    foreach (var e in res.ErrorSamples) Console.WriteLine($"  erreur : {e}");
    return res.Errors == 0 ? 0 : 1;
}
var errors = new List<string>();
var decks = new DeckStore(decksDir).LoadAll(errors);
foreach (var e in errors) Console.Error.WriteLine($"Deck illisible ignoré : {e}");

if (list || names.Count == 0)
{
    Console.WriteLine($"Decks sauvegardés ({decksDir}) :");
    foreach (var d in decks)
        Console.WriteLine($"  {d.Name}  ({d.Slots.Count} carte(s), SPAM {d.GameVersion})");
    if (names.Count == 0 && !list) PrintHelp();
    return 0;
}
if ((evaluate || analyse) && names.Count == 1) names.Add(names[0]);
if (names.Count != 2)
{
    Console.Error.WriteLine("Donne exactement deux noms de decks.");
    return 2;
}

List<DuelSlot>? Find(string name)
{
    var d = decks.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase))
            ?? decks.FirstOrDefault(x => x.Id.ToString().StartsWith(name, StringComparison.OrdinalIgnoreCase));
    if (d is null) { Console.Error.WriteLine($"Deck introuvable : « {name} »."); return null; }
    if (d.Slots.Count == 0) { Console.Error.WriteLine($"Le deck « {d.Name} » est vide."); return null; }
    // Règle n° 7 : ne jamais mélanger deux versions de SPAM.
    if (!string.IsNullOrEmpty(d.GameVersion) && d.GameVersion != kb.GameVersion)
    {
        Console.Error.WriteLine($"Le deck « {d.Name} » est pour SPAM {d.GameVersion}, la base est en {kb.GameVersion}.");
        return null;
    }
    return d.Slots.Select(s => new DuelSlot(s.CardId, s.TalentId)).ToList();
}

var deckA = Find(names[0]);
var deckB = Find(names[1]);
if (deckA is null || deckB is null) return 1;

if (analyse)
{
    // Phase 8, lot C : rapport du Grand Analyseur.
    if (AnalysisPreset(seed) is not { } aset) { Console.Error.WriteLine("Réglage inconnu (rapide, normal, precis)."); return 2; }
    if (sheet) aset = AnalysisSettings.Sheet with { Seed = seed };
    if (deckA.Count != kb.Settings.CardsPerDeck) { Console.Error.WriteLine($"L'analyse demande un deck de {kb.Settings.CardsPerDeck} cartes."); return 2; }
    var last = -10;
    if (sheet) aset = aset with { Meta = MetaOrNull() };
    var report = GrandAnalyseur.Analyse(kb, deckA, aset, progress: new Progress<AnalysisProgress>(pr =>
    {
        var pct = (int)(pr.Fraction * 100);
        if (pct / 10 != last / 10) { last = pct; Console.Error.Write($"{pct} % "); }
    }));
    Console.Error.WriteLine();
    var md = sheet ? SheetWriter.ToText(kb, report) + Environment.NewLine + $"(fiche calculée en {report.Elapsed.TotalSeconds:0} s)"
                   : ReportWriter.ToMarkdown(kb, report);
    if (reportFile is not null) { File.WriteAllText(reportFile, md); Console.WriteLine($"Rapport écrit : {reportFile}"); }
    else Console.WriteLine(md);
    return report.Errors == 0 ? 0 : 1;
}

if (evaluate)
{
    // Phase 8, lot A : force d'un deck contre le groupe de référence.
    var field = ReferenceField.Random(kb, opponents, fieldSeed);
    var player = p1Name == "hasard" ? "heuristique" : p1Name;
    var settings = new EvaluationSettings { Player = player, PairsPerOpponent = pairs, Seed = seed };
    var ev = DeckEvaluator.Evaluate(kb, deckA, field, settings);
    Console.WriteLine($"Évaluation de « {names[0]} » : {opponents} adversaires au hasard (groupe {fieldSeed}), {pairs} paire(s) chacun, " +
                      $"joueur « {player} », graine {seed}.");
    Console.WriteLine(ev.Summary);
    Console.WriteLine($"Durée moyenne d'un duel : {ev.AverageRounds:0.0} rounds.");
    Console.WriteLine("| Carte | Talent | Dégâts | Soins | Dégâts subis | Éliminations | Lancers | Talent activé | Tox achetés | Mort |");
    Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|");
    foreach (var c in ev.CardAverages())
        Console.WriteLine($"| {kb.CardName(kb.Card(c.Slot.CardId))} | {(c.Slot.TalentId is { } tal ? kb.AbilityName(tal) : "—")} | {c.DamageDone:0} | {c.HealingDone:0} | {c.DamageTaken:0} | {c.Kills:0.00} | " +
                          $"{c.Casts:0.0} | {c.TalentActivations:0.0} | {c.ToxBought:0.0} | {100 * c.DeathRate:0} % |");
    Console.WriteLine("(moyennes par duel)");
    foreach (var g in ev.Games.Where(g => g.Error is not null).Take(5)) Console.WriteLine($"  erreur : {g.Error}");
    return ev.Errors == 0 ? 0 : 1;
}

if (bench > 0)
{
    if (Factory(p1Name) is not { } fa || Factory(p2Name) is not { } fb)
    {
        Console.Error.WriteLine($"Joueur inconnu. Joueurs disponibles : {string.Join(", ", PlayerCatalog.Names)}.");
        return 2;
    }
    var res = Bench.Run(kb, fa, fb, bench, seed, _ => (deckA, deckB));
    Console.WriteLine($"Banc d'essai : {p1Name} avec « {names[0]} » contre {p2Name} avec « {names[1]} » (graine {seed}) :");
    Console.WriteLine(res.Summary($"A ({p1Name})", $"B ({p2Name})"));
    foreach (var e in res.ErrorSamples) Console.WriteLine($"  erreur : {e}");
    return res.Errors == 0 ? 0 : 1;
}

if (Factory(p1Name) is not { } f1 || Factory(p2Name) is not { } f2)
{
    Console.Error.WriteLine($"Joueur inconnu. Joueurs disponibles : {string.Join(", ", PlayerCatalog.Names)}.");
    return 2;
}

if (games == 1)
{
    var duel = new Duel(kb, deckA, deckB, seed);
    var result = DuelRunner.Play(duel, f1(seed + 1), f2(seed + 2));
    Console.WriteLine(duel.Log);
    Console.WriteLine();
    Console.WriteLine($"Joueurs : J1 = {p1Name}, J2 = {p2Name} · graine : {seed} · rounds : {result.Rounds} · lancers : {result.Casts}");
    return 0;
}

var wins = new int[3];
var rng = new Rng(seed);
for (var g = 0; g < games; g++)
{
    var duel = new Duel(kb, deckA, deckB, rng.NextULong()) { Log = { Enabled = false } };
    var r = DuelRunner.Play(duel, f1(rng.NextULong()), f2(rng.NextULong()));
    wins[(r.Winner ?? -1) + 1]++;
}
Console.WriteLine($"{games} duels (J1 = {p1Name}, J2 = {p2Name}, graine {seed}) :");
Console.WriteLine($"  « {names[0]} » gagne : {wins[1]} ({100.0 * wins[1] / games:0.#} %)");
Console.WriteLine($"  « {names[1]} » gagne : {wins[2]} ({100.0 * wins[2] / games:0.#} %)");
Console.WriteLine($"  égalités : {wins[0]}");
return 0;

Func<ulong, IDuelPlayer>? Factory(string name) =>
    name.ToLowerInvariant() is "recherche" or "search" ? seed => new SearchPlayer(seed, search) : PlayerCatalog.Factory(name);

static void PrintHelp()
{
    Console.WriteLine("""
        Utilisation :
          spamdrafter-duel --list
          spamdrafter-duel "<deck 1>" "<deck 2>" [--seed N] [--games N]
          spamdrafter-duel --bench N [--p1 joueur] [--p2 joueur] [--seed N]
          spamdrafter-duel --tournoi N [--seed N]
          spamdrafter-duel "<deck>" --evaluer [--adversaires N] [--paires N] [--groupe N] [--p1 joueur] [--seed N]
          spamdrafter-duel --cuisiner "Carte 1, Carte 2:Talent" [--reglage rapide|normal|precis] [--seed N]
          spamdrafter-duel --test-cuisinier N [--duels-test N] [--reglage ...] [--seed N]
          spamdrafter-duel "<deck>" --analyser [--reglage rapide|normal|precis] [--rapport fichier.md] [--seed N]
          spamdrafter-duel "<deck>" --fiche [--seed N]
          spamdrafter-duel --test-analyseur N [--duels-test N] [--reglage ...] [--seed N]
          spamdrafter-duel --correlation N [--adversaires N] [--paires N] [--groupe N] [--seed N]
          spamdrafter-duel "<deck 1>" "<deck 2>" --bench N [--p1 joueur] [--p2 joueur]
        Options :
          --bench N      banc d'essai : N duels entre deux joueurs, places échangées à chaque paire
                         (sans noms de decks : decks au hasard, le même pour les deux joueurs)
          --topk N / --rollouts N / --horizon N / --greedy-rest
                         réglages du joueur « recherche » (actions approfondies, suites imaginées, tours adverses)
          --tournoi N    tournoi : chaque paire de joueurs s'affronte sur N duels (tableau du résultat)
          --evaluer      force du deck contre un groupe d'adversaires au hasard (par défaut 50, 2 paires chacun,
                         joueur heuristique) et moyennes par carte
          --adversaires N / --paires N / --groupe N
                         taille du groupe de référence, paires de duels par adversaire, graine du groupe (défaut 2026)
          --cuisiner S   Grand Cuisinier : complète la sélection S (noms de cartes séparés par des virgules,
                         « Carte:Talent » pour imposer un talent) et propose des decks
          --test-cuisinier N  banc de test du Cuisinier sur N sélections au hasard (seuils de la phase 8)
          --analyser     Grand Analyseur : rapport en français sur un deck de 4 cartes (force, rôle des cartes,
                         synergies, talents, Tox, style, adversaires difficiles)
          --test-analyseur N  banc de test de l'Analyseur sur N decks au hasard
          --test-meta N  critère de compétitivité : N decks de référence, une carte imposée, proposition contre la méta
          --guide X      part des cartes essayées choisies selon les profils (0 à 1 ; défaut 0,5)
          --classement-meta F  classe les decks de référence entre eux (joueur --p1) et compare aux taux réels du fichier F
          --calculer-meta [F]  lot I : calcule la méta synthétique (sans les decks de référence) et l'enregistre dans F
                         (défaut : knowledge/<version>/meta/synthetique.json) ; --meta-taille N (20), --meta-tours N (3),
                         --meta-cartes N (cartes de départ tirées au hasard ; défaut : toutes), --reprendre (repart de F)
          --controle-meta [F]  la méta synthétique F contre les decks de référence (--duels-test = paires par adversaire)
          --meta-synthetique [F]  avec --test-meta : le Cuisinier affronte la méta synthétique F, pas les decks de référence
          --sans-meta    ne pas utiliser les decks de référence (adversaires tirés au hasard)
          --correlation N  compare les classements de N decks au hasard par les joueurs rapide et heuristique
          --p1 / --p2    joueurs (banc d'essai, ou duel entre deux decks ; par défaut : hasard) (disponibles : hasard, heuristique, recherche, rapide)
          --games N      joue N duels et affiche le bilan (sinon : un duel avec son journal)
          --seed N       graine du hasard (même graine = même partie)
          --knowledge D  dossier de la base (par défaut : knowledge/<version> le plus récent)
          --decks D      dossier des decks (par défaut : %APPDATA%\SpamDrafter\decks)
        """);
}

static string? FindKnowledgeDirectory()
{
    var env = Environment.GetEnvironmentVariable("SPAMDRAFTER_KNOWLEDGE");
    if (!string.IsNullOrWhiteSpace(env)) return env;
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
    {
        var root = Path.Combine(d.FullName, "knowledge");
        if (!Directory.Exists(root)) continue;
        var latest = Directory.EnumerateDirectories(root)
            .Where(p => File.Exists(Path.Combine(p, "manifest.json")))
            .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (latest is not null) return latest;
    }
    return null;
}

/// <summary>Avancement appelé directement (affichage immédiat dans la console).</summary>
sealed class SyncProgress<T>(Action<T> f) : IProgress<T> { public void Report(T value) => f(value); }
