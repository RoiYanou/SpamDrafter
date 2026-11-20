using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.Core.Games;

/// <summary>Ce qu'on a tiré pour un emplacement de la carte.</summary>
public enum RngPartKind { Element, Rarity, Name, Illustration, Hp, Spamtons, Ability, Tox, Nothing }

/// <summary>
/// Un tirage : son nom (« Élément », « Compétence 1 »…), la valeur tirée, les points que sa rareté rapporte et sa rareté en « bits » (log₂ de 1 / probabilité).
/// <c>CardId</c> : la carte tirée (nom ou illustration).
/// </summary>
public sealed record RngPart(string Label, string Value, int Points, RngPartKind Kind, string? CardId = null, string? ItemId = null, double Bits = 0);

/// <summary>Un bonus de cohérence : des tirages qui s'accordent, comme sur une vraie carte (<c>Bits</c> : rareté de la coïncidence).</summary>
public sealed record RngBonus(string Label, string Value, int Points, double Bits = 0);

/// <summary>Une ligne du détail de la note (tirage ou bonus) : la raison, la valeur concernée, les points gagnés et leur rareté en bits.</summary>
public sealed record RngScoreLine(string Reason, string Value, int Points, bool IsBonus, double Bits = 0);

/// <summary>
/// « Tirage du jour » (phase 16, lot D, D-095) : une carte est assemblée au hasard, emplacement par emplacement, puis notée.
/// <para>Tirages, dans l'ordre où la carte se forme : illustration, nom, rareté, élément, Spamtons, PV, compétence 1 (toujours la 1ʳᵉ compétence d'une carte existante),
/// puis les emplacements 2 et 3. Chaque valeur existe sur une vraie carte, <b>à cette même place</b> : la probabilité qu'il y ait un 2ᵉ emplacement est la part des cartes
/// qui en ont un ; celle d'un 3ᵉ emplacement est calculée parmi les cartes qui ont au moins 2 emplacements remplis (compétences, puis Tox).</para>
/// <para><b>Points</b> = 500 × log₂(1 / probabilité) pour un bonus de cohérence, 50 × log₂(1 / probabilité) pour un simple tirage : les coïncidences font le score.
/// <b>La probabilité d'une coïncidence est calculée sur les valeurs réellement tirées</b> : « illustration et nom du même artiste » rapporte plus si l'artiste a fait peu de cartes,
/// « compétence possédée par une carte du même élément » dépend des cartes qui possèdent cette compétence, « élément, rareté, PV et Spamtons qui forment une carte existante »
/// est la chance qu'un tirage au hasard tombe sur une carte réelle, etc. Quelques coïncidences d'un genre particulier (nombres d'emplacements, types…) sont mesurées sur 12 000 cartes
/// tirées avec les mêmes règles (graine fixe). Le <b>rang</b> compare la note à ces 100 000 cartes.</para>
/// <para><b>Carte parfaite</b> (le nom, l'illustration, la rareté, l'élément, les PV, les Spamtons, les compétences et les Tox d'une même carte du jeu) : son bonus dépasse tout ce
/// que peut rapporter une carte non parfaite : c'est le meilleur score possible. Chaque joueur tire sa propre carte (graine au hasard) ; les notes se comparent.</para>
/// </summary>
public sealed class RngGame
{
    public const string GameId = "rng";
    private const int Samples = 100_000;
    /// <summary>Points d'un bonus : 500 × log₂(1 / probabilité).</summary>
    private const int BonusScale = 500;
    /// <summary>Points d'un tirage : 50 × log₂(1 / probabilité), soit 10 fois moins qu'un bonus.</summary>
    private const int DrawScale = 50;
    private const double MinProbability = 1e-7;

    private RngGame(IReadOnlyList<RngPart> parts, IReadOnlyList<RngBonus> bonuses, Card nameCard, Card illustrationCard, double percentile, bool perfect)
    {
        Parts = parts; Bonuses = bonuses; NameCard = nameCard; IllustrationCard = illustrationCard; Percentile = percentile; IsPerfect = perfect;
        // Ce qui rapporte 0 point (le nom et l'illustration : tirés parmi toutes les cartes, donc pareil pour toutes) n'est pas montré.
        Lines = parts.Select(p => new RngScoreLine("Tirage : " + p.Label, p.Value, p.Points, false, p.Bits))
            .Concat(bonuses.Select(b => new RngScoreLine(b.Label, b.Value, b.Points, true, b.Bits)))
            .Where(l => l.Points > 0)
            .Select((l, i) => (l, i)).OrderBy(x => x.l.Points).ThenBy(x => x.i).Select(x => x.l).ToList();
    }

    public IReadOnlyList<RngPart> Parts { get; }
    public IReadOnlyList<RngBonus> Bonuses { get; }
    /// <summary>Détail de la note, du plus petit gain au plus grand (l'ordre dans lequel il est montré).</summary>
    public IReadOnlyList<RngScoreLine> Lines { get; }
    /// <summary>Carte dont le nom a été tiré.</summary>
    public Card NameCard { get; }
    /// <summary>Carte dont l'illustration a été tirée.</summary>
    public Card IllustrationCard { get; }
    public bool IsPerfect { get; }
    public int DrawPoints => Parts.Sum(p => p.Points);
    public int BonusPoints => Bonuses.Sum(b => b.Points);
    public int Total => DrawPoints + BonusPoints;
    /// <summary>Part (0 à 100) des cartes tirées au hasard avec les mêmes règles qui ont une note plus basse.</summary>
    public double Percentile { get; }

    /// <summary>Qualité de la carte, d'après son rang (mêmes paliers que les couleurs : rouge, gris, jaune, vert, bleu, violet, multicolore).</summary>
    public string Title => IsPerfect ? "Carte parfaite !" : Percentile switch
    {
        > 97 => "Carte légendaire", > 85 => "Excellente carte", > 60 => "Belle carte", > 30 => "Carte correcte", > 2 => "Carte banale", _ => "Carte désastreuse",
    };

    // ---------------------------------------------------------------- statistiques du jeu

    private sealed class Info
    {
        public Card Card = null!;
        public HashSet<string> Abilities = [], Tox = [];
        public string? Species, Region, Artist;
        public string Pattern = "";
        public int Slots;
    }

    private sealed class Stats
    {
        public IReadOnlyList<Card> Cards = [];
        public double N, N2, N3;   // toutes les cartes ; cartes avec au moins 2 emplacements remplis ; au moins 3
        public Dictionary<char, double> N2k = new() { ['a'] = 0, ['t'] = 0 }, N3k = new() { ['a'] = 0, ['t'] = 0 };   // idem, selon la nature du 2ᵉ emplacement (a : compétence, t : Tox)
        public Dictionary<string, Info> Info = [];
        public List<string> Elements = [], Rarities = [], FirstAbilities = [];
        public List<int> Hps = [], Golds = [];
        public Dictionary<string, int> ElementCount = [], RarityCount = [], FirstCount = [], SpeciesCount = [], RegionCount = [], ArtistCount = [];
        public Dictionary<int, int> HpCount = [], GoldCount = [];
        public Dictionary<string, string> AbilityElement = [];
        public Dictionary<string, string?> ToxElement = [], ToxRarity = [];
        public Dictionary<string, List<Info>> AbilityOwners = [], ToxOwners = [];
        public HashSet<(string, string)> ER = []; public HashSet<(string, int)> EH = [], EG = [], RH = [], RG = []; public HashSet<(int, int)> HG = [];
        public HashSet<(string, int, int)> EHG = []; public HashSet<(string, string, int)> ERH = [], ERG = []; public HashSet<(string, int, int)> RHG = [];
        public HashSet<(string, string, int, int)> Quad = [];
        public Dictionary<string, double> PExist = [];   // chance qu'un tirage au hasard tombe sur une combinaison qui existe
        public HashSet<string> AbilitySets = [];
        public Dictionary<string, int> ModeSlots = [], ModeAbilities = [];
        // Ce que les vraies cartes ont en 2ᵉ et 3ᵉ emplacement (compétence ou Tox, dans l'ordre : compétences, puis Tox).
        // Les cartes listent leurs compétences, puis leurs Tox : un 3ᵉ emplacement dépend donc de la nature du 2ᵉ (clé : nature du 2ᵉ emplacement).
        public List<string> S2A = [], S2T = [];
        public Dictionary<char, List<string>> S3A = new() { ['a'] = [], ['t'] = [] }, S3T = new() { ['a'] = [], ['t'] = [] };
        public Dictionary<string, int> S2ACount = [], S2TCount = [];
        public Dictionary<char, Dictionary<string, int>> S3ACount = new() { ['a'] = [], ['t'] = [] }, S3TCount = new() { ['a'] = [], ['t'] = [] };
    }

    private sealed class Draw
    {
        public string Element = "", Rarity = ""; public Card Name = null!, Illu = null!; public int Hp, Gold;
        public List<string> Abilities = [], Tox = [];
        public string Pattern = "";    // nature des emplacements 2 et 3 : a (compétence), t (Tox), n (rien)
        public List<(char Kind, string Id)> Slots = [];
    }

    private static string SetKey(IEnumerable<string> ids) => string.Join("|", ids.OrderBy(i => i, StringComparer.Ordinal));
    private static string PatternOf(Card c)
    {
        var kinds = new List<char>();
        for (var i = 1; i < c.Abilities.Count; i++) kinds.Add('a');
        foreach (var _ in c.InnatePassives ?? []) kinds.Add('t');
        while (kinds.Count < 2) kinds.Add('n');
        return new string(kinds.Take(2).ToArray());
    }

    private static Stats Build(KnowledgeBase kb, IReadOnlyDictionary<string, string> abilityElements)
    {
        var st = new Stats { Cards = kb.Cards, N = kb.Cards.Count, AbilityElement = abilityElements.ToDictionary(k => k.Key, k => k.Value) };
        string? Clean(string? s) => string.IsNullOrWhiteSpace(s) || s == "Inconnue" ? null : s.Trim();
        foreach (var c in kb.Cards)
        {
            var tox = c.InnatePassives ?? [];
            var lore = kb.Lore(c);
            var info = new Info
            {
                Card = c, Abilities = c.Abilities.ToHashSet(), Tox = tox.ToHashSet(), Species = Clean(lore?.Species), Region = Clean(lore?.Region), Artist = c.Artist,
                Pattern = PatternOf(c), Slots = c.Abilities.Count + tox.Count,
            };
            st.Info[c.Id] = info;
            st.Elements.Add(c.Element); st.Rarities.Add(c.Rarity); st.Hps.Add(c.Hp); st.Golds.Add(c.GoldIncome);
            if (c.Abilities.Count > 0) st.FirstAbilities.Add(c.Abilities[0]);
            var items = c.Abilities.Select(a => ('a', a)).Concat(tox.Select(t => ('t', t))).ToList();
            if (items.Count > 1) { st.N2++; st.N2k[items[1].Item1]++; (items[1].Item1 == 'a' ? st.S2A : st.S2T).Add(items[1].Item2); }
            if (items.Count > 2) { st.N3++; st.N3k[items[1].Item1]++; (items[2].Item1 == 'a' ? st.S3A : st.S3T)[items[1].Item1].Add(items[2].Item2); }
            var e = c.Element; var r = c.Rarity; var h = c.Hp; var g = c.GoldIncome;
            st.ER.Add((e, r)); st.EH.Add((e, h)); st.EG.Add((e, g)); st.RH.Add((r, h)); st.RG.Add((r, g)); st.HG.Add((h, g));
            st.EHG.Add((e, h, g)); st.ERH.Add((e, r, h)); st.ERG.Add((e, r, g)); st.RHG.Add((r, h, g)); st.Quad.Add((e, r, h, g));
            st.AbilitySets.Add(SetKey(c.Abilities));
            var ab = c.Abilities.Distinct().ToArray();
            for (var i = 0; i < ab.Length; i++) for (var j = i + 1; j < ab.Length; j++)
            {
                st.AbilitySets.Add(SetKey([ab[i], ab[j]]));
                for (var k = j + 1; k < ab.Length; k++) st.AbilitySets.Add(SetKey([ab[i], ab[j], ab[k]]));
            }
            foreach (var a in ab) { if (!st.AbilityOwners.TryGetValue(a, out var l)) st.AbilityOwners[a] = l = []; l.Add(info); }
            foreach (var t in tox.Distinct())
            {
                if (!st.ToxOwners.TryGetValue(t, out var l)) st.ToxOwners[t] = l = [];
                l.Add(info);
                if (!st.ToxElement.ContainsKey(t)) { var te = kb.Tox(t); st.ToxElement[t] = te?.Element; st.ToxRarity[t] = te?.Rarity; }
            }
            if (info.Species is not null) st.SpeciesCount[info.Species] = st.SpeciesCount.GetValueOrDefault(info.Species) + 1;
            if (info.Region is not null) st.RegionCount[info.Region] = st.RegionCount.GetValueOrDefault(info.Region) + 1;
            if (info.Artist is not null) st.ArtistCount[info.Artist] = st.ArtistCount.GetValueOrDefault(info.Artist) + 1;
        }
        void Count<T>(IEnumerable<T> src, Dictionary<T, int> d) where T : notnull { foreach (var x in src) d[x] = d.GetValueOrDefault(x) + 1; }
        Count(st.Elements, st.ElementCount); Count(st.Rarities, st.RarityCount); Count(st.FirstAbilities, st.FirstCount); Count(st.Hps, st.HpCount); Count(st.Golds, st.GoldCount);
        Count(st.S2A, st.S2ACount); Count(st.S2T, st.S2TCount);
        foreach (var k in new[] { 'a', 't' }) { Count(st.S3A[k], st.S3ACount[k]); Count(st.S3T[k], st.S3TCount[k]); }
        foreach (var grp in kb.Cards.GroupBy(c => c.Rarity))
        {
            st.ModeSlots[grp.Key] = grp.GroupBy(c => st.Info[c.Id].Slots).OrderByDescending(x => x.Count()).ThenBy(x => x.Key).First().Key;
            st.ModeAbilities[grp.Key] = grp.GroupBy(c => c.Abilities.Count).OrderByDescending(x => x.Count()).ThenBy(x => x.Key).First().Key;
        }
        // Chance qu'un tirage au hasard (chaque valeur tirée séparément) tombe sur une combinaison qui existe sur une vraie carte.
        double FE(string v) => st.ElementCount[v] / st.N; double FR(string v) => st.RarityCount[v] / st.N; double FH(int v) => st.HpCount[v] / st.N; double FG(int v) => st.GoldCount[v] / st.N;
        st.PExist["ER"] = st.ER.Sum(x => FE(x.Item1) * FR(x.Item2));
        st.PExist["EH"] = st.EH.Sum(x => FE(x.Item1) * FH(x.Item2));
        st.PExist["EG"] = st.EG.Sum(x => FE(x.Item1) * FG(x.Item2));
        st.PExist["RH"] = st.RH.Sum(x => FR(x.Item1) * FH(x.Item2));
        st.PExist["RG"] = st.RG.Sum(x => FR(x.Item1) * FG(x.Item2));
        st.PExist["HG"] = st.HG.Sum(x => FH(x.Item1) * FG(x.Item2));
        st.PExist["EHG"] = st.EHG.Sum(x => FE(x.Item1) * FH(x.Item2) * FG(x.Item3));
        st.PExist["ERH"] = st.ERH.Sum(x => FE(x.Item1) * FR(x.Item2) * FH(x.Item3));
        st.PExist["ERG"] = st.ERG.Sum(x => FE(x.Item1) * FR(x.Item2) * FG(x.Item3));
        st.PExist["RHG"] = st.RHG.Sum(x => FR(x.Item1) * FH(x.Item2) * FG(x.Item3));
        st.PExist["Quad"] = st.Quad.Sum(x => FE(x.Item1) * FR(x.Item2) * FH(x.Item3) * FG(x.Item4));
        return st;
    }

    private static readonly object StatsLock = new();
    private static (KnowledgeBase Kb, IReadOnlyDictionary<string, string>? Elements, Stats Stats)? _statsCache;

    /// <summary>Les statistiques du jeu, calculées une fois par base de connaissances.</summary>
    private static Stats StatsFor(KnowledgeBase kb, IReadOnlyDictionary<string, string>? abilityElements)
    {
        lock (StatsLock)
        {
            if (_statsCache is { } c && ReferenceEquals(c.Kb, kb) && ReferenceEquals(c.Elements, abilityElements)) return c.Stats;
            var st = Build(kb, abilityElements ?? new Dictionary<string, string>());
            _statsCache = (kb, abilityElements, st);
            return st;
        }
    }

    private static int Pts(double p, int scale = BonusScale) => (int)Math.Round(scale * Math.Log2(1 / Math.Max(p, MinProbability)));
    private static int PtsD(double p) => Pts(p, DrawScale);
    private static double Bits(double p) => Math.Log2(1 / Math.Max(p, MinProbability));
    private static T Pick<T>(IReadOnlyList<T> list, SeededRandom rng) => list[rng.Next(list.Count)];

    // ---------------------------------------------------------------- probabilités des emplacements

    /// <summary>
    /// Probabilité du tirage d'une compétence ou d'un Tox à l'emplacement 1, 2 ou 3. Le 3ᵉ est calculé parmi les cartes dont le 2ᵉ emplacement est de la même nature
    /// (<paramref name="prev"/> : nature du 2ᵉ emplacement) : une carte liste ses compétences avant ses Tox, donc un 3ᵉ emplacement « compétence » suppose un 2ᵉ « compétence ».
    /// </summary>
    private static double SlotProbability(Stats st, int slot, char kind, string id, char prev)
    {
        if (slot == 1) return st.FirstCount.GetValueOrDefault(id) / (double)Math.Max(1, st.FirstAbilities.Count);
        if (slot == 2) return (kind == 'a' ? st.S2ACount : st.S2TCount).GetValueOrDefault(id) / st.N;
        return (kind == 'a' ? st.S3ACount[prev] : st.S3TCount[prev]).GetValueOrDefault(id) / Math.Max(1, st.N2k[prev]);
    }

    /// <summary>Probabilité du tirage « rien » à l'emplacement 2 (parmi toutes les cartes) ou 3 (parmi celles dont le 2ᵉ emplacement a la même nature).</summary>
    private static double NothingProbability(Stats st, int slot, char prev) => slot == 2 ? 1 - st.N2 / st.N : 1 - st.N3k[prev] / Math.Max(1, st.N2k[prev]);

    // ---------------------------------------------------------------- tirage (identifiants seulement)

    private static Draw MakeDraw(Stats st, SeededRandom rng)
    {
        var d = new Draw
        {
            Element = Pick(st.Elements, rng),
            Name = st.Cards[rng.Next(st.Cards.Count)],
            Illu = st.Cards[rng.Next(st.Cards.Count)],
            Hp = Pick(st.Hps, rng),
            Gold = Pick(st.Golds, rng),
        };
        var first = Pick(st.FirstAbilities, rng);
        d.Abilities.Add(first);
        d.Slots.Add(('a', first));
        var used = new HashSet<string> { first };
        var pattern = "";
        var prev = ' ';
        for (var slot = 2; slot <= 3; slot++)
        {
            // Un emplacement est rempli avec la fréquence des vraies cartes (le 3ᵉ : parmi celles dont le 2ᵉ emplacement a la même nature), puis sa nature et sa valeur
            // viennent de ce que ces cartes ont à cette place.
            bool filled; List<string> a, t;
            if (slot == 2) { filled = rng.Next((int)st.N) < st.N2; a = st.S2A; t = st.S2T; }
            else { var n2 = st.N2k[prev]; filled = n2 > 0 && rng.Next((int)n2) < st.N3k[prev]; a = st.S3A[prev]; t = st.S3T[prev]; }
            if (!filled || a.Count + t.Count == 0) { pattern += "n"; d.Slots.Add(('n', "")); break; }
            var kind = rng.Next(a.Count + t.Count) < a.Count ? 'a' : 't';
            var pool = kind == 'a' ? a : t;
            string id; var tries = 0;
            do { id = Pick(pool, rng); } while (used.Contains(id) && ++tries < 50);
            if (kind == 'a') d.Abilities.Add(id); else d.Tox.Add(id);
            used.Add(id); d.Slots.Add((kind, id)); pattern += kind;
            if (slot == 2) prev = kind;
        }
        d.Pattern = pattern.PadRight(2, 'n');
        d.Rarity = Pick(st.Rarities, rng);   // tiré en dernier : le reste du tirage ne dépend pas de la rareté
        return d;
    }

    // ---------------------------------------------------------------- coïncidences

    private static string ToxList(KnowledgeBase kb, IEnumerable<string> ids) => string.Join(", ", ids.Select(t => kb.Tox(t) is { } e ? kb.ToxName(e) : t));

    /// <summary>
    /// Une coïncidence : sa clé, son libellé, la valeur montrée et sa probabilité <c>P</c> calculée sur les valeurs tirées (0 : probabilité mesurée sur les cartes simulées, d'après la clé).
    /// </summary>
    private readonly record struct Ev(string Key, string Label, Func<string> Value, double P);

    private static List<Ev> Events(KnowledgeBase kb, Stats st, Draw d)
    {
        var res = new List<Ev>();
        void Add(string key, string label, Func<string> value, double p = 0) => res.Add(new Ev(key, label, value, p));
        var n = st.Info[d.Name.Id]; var i = st.Info[d.Illu.Id];
        var same = d.Illu.Id == d.Name.Id;
        double N = st.N;
        string Name() => kb.CardName(d.Name);
        string IlluName() => kb.CardName(d.Illu);
        double FE(string v) => st.ElementCount.GetValueOrDefault(v) / N; double FR(string v) => st.RarityCount.GetValueOrDefault(v) / N;
        double FH(int v) => st.HpCount.GetValueOrDefault(v) / N; double FG(int v) => st.GoldCount.GetValueOrDefault(v) / N;
        var abSlots = d.Slots.Select((s, k) => (Slot: k + 1, s.Kind, s.Id)).Where(x => x.Kind == 'a').ToList();
        var toxSlots = d.Slots.Select((s, k) => (Slot: k + 1, s.Kind, s.Id)).Where(x => x.Kind == 't').ToList();
        char PrevOf(int slot) => slot == 3 ? d.Slots[1].Kind : ' ';
        double SlotsIn(int slot, char kind, HashSet<string> set) => set.Sum(x => SlotProbability(st, slot, kind, x, PrevOf(slot)));

        // --- Les quatre valeurs tirées (élément, rareté, PV, Spamtons) face aux cartes qui comptent dans le tirage.
        (string Key, string Cap, string Same, Func<Info, string> Sel, string Drawn, Func<string, double> Freq, Func<string> Show)[] attrs =
        [
            ("El", "Élément", "du même élément", x => x.Card.Element, d.Element, FE, () => kb.ElementName(d.Element)),
            ("Ra", "Rareté", "de la même rareté", x => x.Card.Rarity, d.Rarity, FR, () => kb.RarityName(d.Rarity)),
            ("PV", "PV", "avec les mêmes PV", x => x.Card.Hp.ToString(), d.Hp.ToString(), v => FH(int.Parse(v)), () => d.Hp + " PV"),
            ("Sp", "Spamtons", "avec les mêmes Spamtons", x => x.Card.GoldIncome.ToString(), d.Gold.ToString(), v => FG(int.Parse(v)), () => d.Gold + " Spamtons"),
        ];
        foreach (var (key, cap, sameText, sel, drawn, freq, show) in attrs)
        {
            if (sel(n) == drawn) Add("N" + key, cap + " de la carte du nom", show, freq(drawn));
            if (!same && sel(i) == drawn) Add("I" + key, cap + " de la carte de l'illustration", show, freq(drawn));
            // Les cartes qui possèdent les compétences / Tox tirés : l'une d'elles a-t-elle la même valeur ? (probabilité : part des valeurs possibles de ces propriétaires)
            foreach (var (kindName, items, owners) in new[] { ("Ab", d.Abilities, st.AbilityOwners), ("Tx", d.Tox, st.ToxOwners) })
            {
                var matched = items.Where(x => owners[x].Any(o => sel(o) == drawn)).ToList();
                var maxLevel = key is "El" or "Ra" ? (kindName == "Ab" ? 3 : 2) : 1;
                for (var k = 1; k <= Math.Min(maxLevel, matched.Count); k++)
                {
                    var chosen = matched.Take(k).ToList();
                    HashSet<string>? common = null;
                    foreach (var x in chosen) { var vals = owners[x].Select(sel).ToHashSet(); common = common is null ? vals : [.. common.Intersect(vals)]; }
                    var p = common!.Sum(freq);
                    var label = kindName == "Ab" ? (k == 1 ? "Compétence possédée par une carte " + sameText : $"{k} compétences possédées par des cartes " + sameText)
                                                 : (k == 1 ? "Tox présent sur une carte " + sameText : $"{k} Tox présents sur des cartes " + sameText);
                    Add($"{kindName}{key}{k}", label, () => kindName == "Ab" ? string.Join(", ", chosen.Select(kb.AbilityName)) : ToxList(kb, chosen), p);
                }
            }
        }

        // --- Le nom et l'illustration entre eux : probabilité = part des cartes qui ont la même valeur (un artiste qui a fait peu de cartes rapporte plus).
        if (same) Add("IllNom", "Illustration et nom de la même carte", Name, 1 / N);
        else
        {
            if (d.Illu.Element == d.Name.Element) Add("IllNomEl", "Illustration et nom du même élément", () => kb.ElementName(d.Name.Element), FE(d.Name.Element));
            if (d.Illu.Rarity == d.Name.Rarity) Add("IllNomRa", "Illustration et nom de la même rareté", () => kb.RarityName(d.Name.Rarity), FR(d.Name.Rarity));
            if (n.Species is not null && n.Species == i.Species) Add("IllNomEsp", "Illustration et nom de la même espèce", () => n.Species!, st.SpeciesCount[n.Species] / N);
            if (n.Region is not null && n.Region == i.Region) Add("IllNomReg", "Illustration et nom de la même région", () => n.Region!, st.RegionCount[n.Region] / N);
            if (n.Artist is not null && n.Artist == i.Artist) Add("IllNomArt", "Illustration et nom du même artiste", () => kb.Artist(n.Artist)?.Name ?? "", st.ArtistCount[n.Artist] / N);
            if (d.Illu.Hp == d.Name.Hp) Add("IllNomPV", "Illustration et nom avec les mêmes PV", () => d.Name.Hp + " PV", FH(d.Name.Hp));
            if (d.Illu.GoldIncome == d.Name.GoldIncome) Add("IllNomSp", "Illustration et nom avec les mêmes Spamtons", () => d.Name.GoldIncome.ToString(), FG(d.Name.GoldIncome));
        }
        if (d.Element == d.Name.Element && d.Element == d.Illu.Element && !same) Add("ElTrio", "Élément, nom et illustration du même élément", () => kb.ElementName(d.Element), FE(d.Element) * FE(d.Element));
        if (d.Rarity == d.Name.Rarity && d.Rarity == d.Illu.Rarity && !same) Add("RaTrio", "Rareté, nom et illustration de la même rareté", () => kb.RarityName(d.Rarity), FR(d.Rarity) * FR(d.Rarity));

        // --- Espèce, région et artiste partagés entre le nom / l'illustration et les propriétaires des compétences et des Tox tirés (probabilité mesurée, par valeur).
        var owned = d.Abilities.SelectMany(a => st.AbilityOwners[a]).Concat(d.Tox.SelectMany(t => st.ToxOwners[t])).Distinct().ToList();
        foreach (var (src, srcName, srcLabel) in new[] { (n, "N", "du nom"), (i, "I", "de l'illustration") })
        {
            if (srcName == "I" && same) continue;
            if (src.Species is not null && owned.Any(o => o.Species == src.Species)) Add($"Own{srcName}Esp:{src.Species}", $"Compétence ou Tox d'une carte de la même espèce que la carte {srcLabel}", () => src.Species!);
            if (src.Region is not null && owned.Any(o => o.Region == src.Region)) Add($"Own{srcName}Reg:{src.Region}", $"Compétence ou Tox d'une carte de la même région que la carte {srcLabel}", () => src.Region!);
            if (src.Artist is not null && owned.Any(o => o.Artist == src.Artist)) Add($"Own{srcName}Art:{src.Artist}", $"Compétence ou Tox d'une carte du même artiste que la carte {srcLabel}", () => kb.Artist(src.Artist)?.Name ?? "");
        }

        // --- Des statistiques qui existent ensemble sur une vraie carte (probabilité : chance qu'un tirage au hasard tombe sur une combinaison réelle).
        var E = d.Element; var R = d.Rarity; var H = d.Hp; var G = d.Gold;
        string El() => kb.ElementName(E); string Ra() => kb.RarityName(R);
        if (st.ER.Contains((E, R))) Add("PairER", "Élément et rareté qui existent sur une même carte", () => $"{El()} · {Ra()}", st.PExist["ER"]);
        if (st.EH.Contains((E, H))) Add("PairEH", "Élément et PV qui existent sur une même carte", () => $"{El()} · {H} PV", st.PExist["EH"]);
        if (st.EG.Contains((E, G))) Add("PairEG", "Élément et Spamtons qui existent sur une même carte", () => $"{El()} · {G} Spamtons", st.PExist["EG"]);
        if (st.RH.Contains((R, H))) Add("PairRH", "Rareté et PV qui existent sur une même carte", () => $"{Ra()} · {H} PV", st.PExist["RH"]);
        if (st.RG.Contains((R, G))) Add("PairRG", "Rareté et Spamtons qui existent sur une même carte", () => $"{Ra()} · {G} Spamtons", st.PExist["RG"]);
        if (st.HG.Contains((H, G))) Add("PairHG", "PV et Spamtons qui existent sur une même carte", () => $"{H} PV · {G} Spamtons", st.PExist["HG"]);
        if (st.EHG.Contains((E, H, G))) Add("TripEHG", "Élément, PV et Spamtons qui forment une carte existante", () => $"{El()} · {H} PV · {G} Spamtons", st.PExist["EHG"]);
        if (st.ERH.Contains((E, R, H))) Add("TripERH", "Élément, rareté et PV qui forment une carte existante", () => $"{El()} · {Ra()} · {H} PV", st.PExist["ERH"]);
        if (st.ERG.Contains((E, R, G))) Add("TripERG", "Élément, rareté et Spamtons qui forment une carte existante", () => $"{El()} · {Ra()} · {G} Spamtons", st.PExist["ERG"]);
        if (st.RHG.Contains((R, H, G))) Add("TripRHG", "Rareté, PV et Spamtons qui forment une carte existante", () => $"{Ra()} · {H} PV · {G} Spamtons", st.PExist["RHG"]);
        if (st.Quad.Contains((E, R, H, G))) Add("Quad", "Élément, rareté, PV et Spamtons qui forment une carte existante", () => $"{El()} · {Ra()} · {H} PV · {G} Spamtons", st.PExist["Quad"]);
        bool Stats(Card c) => c.Element == E && c.Rarity == R && c.Hp == H && c.GoldIncome == G;
        double StatsP(Card c) => FE(c.Element) * FR(c.Rarity) * FH(c.Hp) * FG(c.GoldIncome);
        if (Stats(d.Name)) Add("QuadNom", "Élément, rareté, PV et Spamtons identiques à ceux de la carte du nom", Name, StatsP(d.Name));
        if (!same && Stats(d.Illu)) Add("QuadIll", "Élément, rareté, PV et Spamtons identiques à ceux de la carte de l'illustration", IlluName, StatsP(d.Illu));

        // --- Compétences et Tox : probabilité = chance qu'un tirage à cet emplacement tombe sur ce que possède la carte concernée.
        foreach (var (src, srcName, srcLabel, card) in new[] { (n, "Nom", "du nom", d.Name), (i, "Ill", "de l'illustration", d.Illu) })
        {
            if (srcName == "Ill" && same) continue;
            var matched = abSlots.Where(x => src.Abilities.Contains(x.Id)).ToList();
            for (var k = 1; k <= Math.Min(3, matched.Count); k++)
            {
                var p = matched.Take(k).Aggregate(1.0, (acc, x) => acc * SlotsIn(x.Slot, 'a', src.Abilities));
                Add("Ab" + srcName + k, k == 1 ? $"Compétence de la carte {srcLabel}" : $"{k} compétences de la carte {srcLabel}", srcName == "Nom" ? Name : IlluName, p);
            }
            if (card.Abilities.Count > 0 && d.Abilities[0] == card.Abilities[0])
                Add("Ab1" + srcName, $"Première compétence de la carte {srcLabel}", () => kb.AbilityName(d.Abilities[0]), SlotProbability(st, 1, 'a', d.Abilities[0], ' '));
            var matchedTox = toxSlots.Where(x => src.Tox.Contains(x.Id)).ToList();
            if (matchedTox.Count > 0) Add("Tox" + srcName, $"Tox de la carte {srcLabel}", () => ToxList(kb, d.Tox), matchedTox.Aggregate(1.0, (acc, x) => acc * SlotsIn(x.Slot, 't', src.Tox)));
            if (d.Abilities.Count == card.Abilities.Count) Add($"NbAb{srcName}:{d.Abilities.Count}", $"Même nombre de compétences que la carte {srcLabel}", () => d.Abilities.Count.ToString());
            if (srcName == "Nom" && d.Tox.Count == (card.InnatePassives ?? []).Count) Add($"NbTox:{d.Tox.Count}", "Même nombre de Tox que la carte du nom", () => d.Tox.Count.ToString());
            var slots = d.Abilities.Count + d.Tox.Count;
            if (slots == src.Slots) Add($"NbSlot{srcName}:{slots}", $"Même nombre d'emplacements remplis que la carte {srcLabel}", () => slots.ToString());
            if (d.Pattern == src.Pattern) Add($"Pat{srcName}:{d.Pattern}", $"Mêmes types d'emplacements que la carte {srcLabel}", srcName == "Nom" ? Name : IlluName);
        }
        if (d.Abilities.Count >= 2 && st.AbilitySets.Contains(SetKey(d.Abilities))) Add("AbSame" + Math.Min(3, d.Abilities.Count), $"{d.Abilities.Count} compétences qui existent sur une même carte", () => string.Join(", ", d.Abilities.Select(kb.AbilityName)));
        if (d.Tox.Count >= 1 && st.AbilityOwners[d.Abilities[0]].Any(o => d.Abilities.All(o.Abilities.Contains) && d.Tox.All(o.Tox.Contains)))
            Add("SameCard", "Compétences et Tox qui existent ensemble sur une même carte", () => string.Join(", ", d.Abilities.Select(kb.AbilityName)) + " + " + ToxList(kb, d.Tox));
        var allSlots = d.Abilities.Count + d.Tox.Count;
        if (d.Abilities.Count == st.ModeAbilities[R]) Add($"NbAbRar:{d.Abilities.Count}:{R}", "Nombre de compétences courant pour cette rareté", () => $"{d.Abilities.Count} · {Ra()}");
        if (allSlots == st.ModeSlots[R]) Add($"NbSlotRar:{allSlots}:{R}", "Nombre d'emplacements courant pour cette rareté", () => $"{allSlots} · {Ra()}");

        // --- Éléments des compétences et des Tox eux-mêmes.
        var ofElement = d.Abilities.Where(a => st.AbilityElement.TryGetValue(a, out var e) && e == E).ToList();
        if (ofElement.Count >= 1) Add("AbEl1", "Compétence de l'élément de la carte", El, FE(E));
        for (var k = 2; k <= 3; k++)
            if (ofElement.Count >= k) Add("AbEl" + k, $"{k} compétences de l'élément de la carte", El);
        if (d.Abilities.Any(a => st.AbilityElement.TryGetValue(a, out var e) && e == d.Name.Element)) Add("AbElNom:" + d.Name.Element, "Compétence de l'élément de la carte du nom", () => kb.ElementName(d.Name.Element));
        if (!same && d.Abilities.Any(a => st.AbilityElement.TryGetValue(a, out var e) && e == d.Illu.Element)) Add("AbElIll:" + d.Illu.Element, "Compétence de l'élément de la carte de l'illustration", () => kb.ElementName(d.Illu.Element));
        if (d.Abilities.Count >= 2 && st.AbilityElement.ContainsKey(d.Abilities[0]) && d.Abilities.Select(a => st.AbilityElement.GetValueOrDefault(a)).Distinct().Count() == 1)
            Add("AbSameEl:" + st.AbilityElement[d.Abilities[0]], "Toutes les compétences du même élément", () => kb.ElementName(st.AbilityElement[d.Abilities[0]]));
        var toxOfElement = d.Tox.FirstOrDefault(t => st.ToxElement.GetValueOrDefault(t) == E);
        if (toxOfElement is not null) Add("TxElem", "Tox de l'élément de la carte", El, FE(E));
        if (d.Tox.Any(t => st.ToxElement.GetValueOrDefault(t) == d.Name.Element)) Add("TxElemNom:" + d.Name.Element, "Tox de l'élément de la carte du nom", () => kb.ElementName(d.Name.Element));
        var toxOfRarity = d.Tox.FirstOrDefault(t => st.ToxRarity.GetValueOrDefault(t) == R);
        if (toxOfRarity is not null) Add("TxRar", "Tox de la rareté de la carte", Ra, FR(R));
        return res;
    }

    private static bool IsPerfectDraw(Draw d)
    {
        if (d.Illu.Id != d.Name.Id) return false;
        var c = d.Name;
        return d.Element == c.Element && d.Rarity == c.Rarity && d.Hp == c.Hp && d.Gold == c.GoldIncome
            && SetKey(d.Abilities) == SetKey(c.Abilities) && SetKey(d.Tox) == SetKey(c.InnatePassives ?? []);
    }

    // ---------------------------------------------------------------- calibrage (mesuré une fois par base)

    private sealed class Calibration
    {
        public Dictionary<string, int> EmpiricalPoints = [];
        public Dictionary<string, double> EmpiricalBits = [];
        public int[] SortedTotals = [];
        public int PerfectBonus;
    }

    private static readonly object CacheLock = new();
    private static (KnowledgeBase Kb, Calibration Cal)? _cache;

    /// <summary>Points des tirages (les valeurs tirées) : 50 × log₂(1 / probabilité) pour chacun.</summary>
    private static int PointsOf(Stats st, Draw d)
    {
        var n = st.N;
        var pts = PtsD(st.ElementCount[d.Element] / n) + PtsD(st.RarityCount[d.Rarity] / n) + PtsD(st.HpCount[d.Hp] / n) + PtsD(st.GoldCount[d.Gold] / n)
                  + PtsD(SlotProbability(st, 1, 'a', d.Abilities[0], ' '));
        for (var k = 1; k < d.Slots.Count; k++) pts += PtsD(SlotPartProbability(st, k + 1, d.Slots[k].Kind, d.Slots[k].Id, k == 2 ? d.Slots[1].Kind : ' '));
        return pts;
    }

    private static double SlotPartProbability(Stats st, int slot, char kind, string id, char prev) => kind == 'n' ? NothingProbability(st, slot, prev) : SlotProbability(st, slot, kind, id, prev);

    private static Calibration Calibrate(KnowledgeBase kb, Stats st)
    {
        lock (CacheLock)
        {
            if (_cache is { } c && ReferenceEquals(c.Kb, kb)) return c.Cal;
            // 100 000 cartes en 50 tranches de 2 000, chacune avec sa graine : le résultat ne dépend ni du nombre de processeurs ni de l'ordre d'exécution.
            const int chunkSize = 2000;
            var chunks = new (Dictionary<string, int> Hits, List<(int Fixed, List<string> Keys)> Draws)[Samples / chunkSize];
            Parallel.For(0, chunks.Length, chunk =>
            {
                var rng = new SeededRandom(SeededRandom.SeedOf("rng-samples|" + chunk));
                var hitsLocal = new Dictionary<string, int>();
                var list = new List<(int, List<string>)>(chunkSize);
                for (var s = 0; s < chunkSize; s++)
                {
                    var d = MakeDraw(st, rng);
                    var fixedPts = PointsOf(st, d);
                    var keys = new List<string>();
                    foreach (var e in Events(kb, st, d))
                    {
                        if (e.P > 0) fixedPts += Pts(e.P);
                        else { keys.Add(e.Key); hitsLocal[e.Key] = hitsLocal.GetValueOrDefault(e.Key) + 1; }
                    }
                    list.Add((fixedPts, keys));
                }
                chunks[chunk] = (hitsLocal, list);
            });
            var hits = new Dictionary<string, int>();
            var draws = new List<(int Fixed, List<string> Keys)>(Samples);
            foreach (var (h, list) in chunks) { foreach (var (k, v) in h) hits[k] = hits.GetValueOrDefault(k) + v; draws.AddRange(list); }
            var cal = new Calibration();
            foreach (var (k, h) in hits) { var p = (h + 0.5) / (Samples + 1.0); cal.EmpiricalPoints[k] = Pts(p); cal.EmpiricalBits[k] = Bits(p); }
            cal.SortedTotals = draws.Select(x => x.Fixed + x.Keys.Sum(k => cal.EmpiricalPoints[k])).Order().ToArray();
            // La carte parfaite doit dépasser toute carte qui ne l'est pas : on la place bien au-dessus de la meilleure carte simulée.
            cal.PerfectBonus = 2 * cal.SortedTotals[^1] + 1000;
            _cache = (kb, cal);
            return cal;
        }
    }

    // ---------------------------------------------------------------- carte d'un jour

    /// <summary>
    /// Une carte d'après un jour (utile aux tests : le jeu tire sa carte avec <see cref="ForSeed"/>). <paramref name="abilityElements"/> : élément de chaque compétence
    /// (identifiant → élément), lu dans abilities.json ; sans lui, il n'y a pas de bonus de compétence d'un élément.
    /// </summary>
    public static RngGame? ForDay(KnowledgeBase kb, DateOnly day, IReadOnlyDictionary<string, string>? abilityElements = null) =>
        ForSeed(kb, SeededRandom.SeedOf("rng|" + Math.Max(0, DailyClock.Number(day))), abilityElements);

    /// <summary>
    /// La carte qu'une graine fait tirer : la même graine donne toujours la même carte. L'application tire une graine au hasard à chaque tirage du jour
    /// (chaque joueur a sa propre carte, mais les notes se comparent) et l'enregistre pour retrouver la carte plus tard.
    /// </summary>
    public static RngGame? ForSeed(KnowledgeBase kb, ulong seed, IReadOnlyDictionary<string, string>? abilityElements = null)
    {
        if (kb.Cards.Count < 2) return null;
        var st = StatsFor(kb, abilityElements);
        if (st.FirstAbilities.Count == 0) return null;
        return Make(kb, st, MakeDraw(st, new SeededRandom(seed)));
    }

    private static RngGame Make(KnowledgeBase kb, Stats st, Draw d)
    {
        var cal = Calibrate(kb, st);
        var n = st.N;
        // La couleur d'un gain suit les points qu'il rapporte : la rareté d'un tirage est comptée au même barème que celle d'un bonus (donc 10 fois moins).
        RngPart Part(string label, string value, double p, RngPartKind kind, string? cardId = null, string? itemId = null) => new(label, value, PtsD(p), kind, cardId, itemId, Bits(p) * DrawScale / BonusScale);
        var parts = new List<RngPart>
        {
            // Le nom et l'illustration sont tirés parmi toutes les cartes : ils valent pareil pour toutes, donc 0 point (seules leurs coïncidences comptent).
            new RngPart("Illustration", kb.CardName(d.Illu), 0, RngPartKind.Illustration, d.Illu.Id),
            new RngPart("Nom", kb.CardName(d.Name), 0, RngPartKind.Name, d.Name.Id),
            Part("Rareté", kb.RarityName(d.Rarity), st.RarityCount[d.Rarity] / n, RngPartKind.Rarity, itemId: d.Rarity),
            Part("Élément", kb.ElementName(d.Element), st.ElementCount[d.Element] / n, RngPartKind.Element, itemId: d.Element),
            Part("Spamtons", d.Gold.ToString(System.Globalization.CultureInfo.InvariantCulture), st.GoldCount[d.Gold] / n, RngPartKind.Spamtons),
            Part("PV", d.Hp.ToString(System.Globalization.CultureInfo.InvariantCulture), st.HpCount[d.Hp] / n, RngPartKind.Hp),
            Part("Compétence 1", kb.AbilityName(d.Abilities[0]), SlotProbability(st, 1, 'a', d.Abilities[0], ' '), RngPartKind.Ability, itemId: d.Abilities[0]),
        };
        for (var s = 1; s < d.Slots.Count; s++)
        {
            var (kind, id) = d.Slots[s];
            var label = $"Emplacement {s + 1}";
            var p = SlotPartProbability(st, s + 1, kind, id, s == 2 ? d.Slots[1].Kind : ' ');
            if (kind == 'n') parts.Add(Part(label, "Rien", p, RngPartKind.Nothing));
            else if (kind == 'a') parts.Add(Part(label, "Compétence : " + kb.AbilityName(id), p, RngPartKind.Ability, itemId: id));
            else parts.Add(Part(label, "Tox : " + ToxList(kb, [id]), p, RngPartKind.Tox, itemId: id));
        }
        // Une coïncidence qui arrive presque à chaque tirage ne rapporte rien : elle n'est pas montrée.
        var bonuses = Events(kb, st, d).Select(e => e.P > 0
                ? new RngBonus(e.Label, e.Value(), Pts(e.P), Bits(e.P))
                : new RngBonus(e.Label, e.Value(), cal.EmpiricalPoints.GetValueOrDefault(e.Key, Pts(1.0 / (Samples + 1))), cal.EmpiricalBits.GetValueOrDefault(e.Key, Bits(1.0 / (Samples + 1)))))
            .Where(b => b.Points > 0).ToList();
        var perfect = IsPerfectDraw(d);
        if (perfect) bonuses.Add(new RngBonus("Carte parfaite : identique à une carte du jeu", kb.CardName(d.Name), cal.PerfectBonus, 30));
        var game = new RngGame(parts, bonuses, d.Name, d.Illu, 0, perfect);
        var lower = Array.BinarySearch(cal.SortedTotals, game.Total);
        if (lower < 0) lower = ~lower; else while (lower > 0 && cal.SortedTotals[lower - 1] == game.Total) lower--;   // nombre de cartes qui font strictement moins
        var below = lower;
        return new RngGame(parts, bonuses, d.Name, d.Illu, 100.0 * below / cal.SortedTotals.Length, perfect);
    }

    /// <summary>Une carte entièrement choisie (pour les tests et l'étude du barème) : retourne la note qu'elle aurait.</summary>
    public static RngGame ForCard(KnowledgeBase kb, Card card, IReadOnlyDictionary<string, string>? abilityElements = null)
    {
        var st = StatsFor(kb, abilityElements);
        var d = new Draw { Element = card.Element, Rarity = card.Rarity, Name = card, Illu = card, Hp = card.Hp, Gold = card.GoldIncome };
        d.Abilities.AddRange(card.Abilities); d.Tox.AddRange(card.InnatePassives ?? []);
        d.Slots.Add(('a', card.Abilities.Count > 0 ? card.Abilities[0] : ""));
        foreach (var a in card.Abilities.Skip(1)) d.Slots.Add(('a', a));
        foreach (var t in card.InnatePassives ?? []) d.Slots.Add(('t', t));
        d.Pattern = PatternOf(card);
        return Make(kb, st, d);
    }

    /// <summary>Lit l'élément de chaque compétence (abilities.json) ; vide si le fichier manque. Jamais d'exception.</summary>
    public static IReadOnlyDictionary<string, string> LoadAbilityElements(string knowledgeDirectory)
    {
        try
        {
            var file = Path.Combine(knowledgeDirectory, "abilities.json");
            if (!File.Exists(file)) return new Dictionary<string, string>();
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            var map = new Dictionary<string, string>();
            foreach (var e in doc.RootElement.EnumerateArray())
                if (e.TryGetProperty("id", out var id) && e.TryGetProperty("element", out var el) && id.GetString() is { } i && el.GetString() is { } v) map[i] = v;
            return map;
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { return new Dictionary<string, string>(); }
    }
}
