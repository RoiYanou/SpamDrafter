using System.Text.Json;

namespace SpamDrafter.Simulator.Data;

/// <summary>
/// Tout ce dont le simulateur a besoin, chargé depuis knowledge/&lt;version&gt;/.
/// Le chargement échoue franchement si un fichier ou un réglage manque.
/// </summary>
public sealed class SimKnowledge
{
    private readonly Dictionary<string, CardDef> _cards;
    private readonly Dictionary<string, AbilityDef> _abilities;
    private readonly Dictionary<string, AbilityDef> _talents;
    private readonly Dictionary<string, BrickDef> _effects;
    private readonly Dictionary<string, BrickDef> _conditions;
    private readonly Dictionary<string, BrickDef> _costs;
    private readonly Dictionary<string, BrickDef> _filters;
    private readonly Dictionary<string, ToxDef> _tox;
    private readonly Dictionary<string, string> _texts;

    public string GameVersion { get; }
    public DuelSettings Settings { get; }
    public IReadOnlyCollection<CardDef> Cards => _cards.Values;

    public SimKnowledge(string gameVersion, DuelSettings settings, IEnumerable<CardDef> cards,
        IEnumerable<AbilityDef> abilities, IEnumerable<BrickDef> effects, IEnumerable<BrickDef> conditions,
        IEnumerable<BrickDef> costs, IEnumerable<BrickDef>? filters = null,
        IReadOnlyDictionary<string, string>? texts = null, IEnumerable<ToxDef>? tox = null,
        IEnumerable<AbilityDef>? talents = null)
    {
        GameVersion = gameVersion;
        Settings = settings;
        _cards = cards.ToDictionary(c => c.Id);
        _abilities = abilities.ToDictionary(a => a.Id);
        _talents = (talents ?? []).ToDictionary(t => t.Id);
        _effects = effects.ToDictionary(e => e.Id);
        _conditions = conditions.ToDictionary(c => c.Id);
        _costs = costs.ToDictionary(c => c.Id);
        _filters = (filters ?? []).ToDictionary(f => f.Id);
        _tox = (tox ?? []).ToDictionary(t => t.Uuid);
        _texts = texts is null ? new() : new Dictionary<string, string>(texts);
    }

    public CardDef Card(string id) => _cards.TryGetValue(id, out var c) ? c
        : throw new KeyNotFoundException($"Carte inconnue : {id}");
    /// <summary>Capacité ou talent (les identifiants ne se recouvrent pas en 1.30).</summary>
    public AbilityDef Ability(string id) => _abilities.TryGetValue(id, out var a) ? a
        : _talents.TryGetValue(id, out var t) ? t
        : throw new KeyNotFoundException($"Capacité inconnue : {id}");
    public AbilityDef? Talent(string id) => _talents.GetValueOrDefault(id);
    public IEnumerable<AbilityDef> Talents => _talents.Values;
    public BrickDef? Effect(string id) => _effects.GetValueOrDefault(id);
    public BrickDef? Condition(string id) => _conditions.GetValueOrDefault(id);
    public BrickDef? Cost(string id) => _costs.GetValueOrDefault(id);
    public BrickDef? Filter(string id) => _filters.GetValueOrDefault(id);
    public ToxDef? Tox(string uuid) => _tox.GetValueOrDefault(uuid);
    public IEnumerable<ToxDef> AllTox => _tox.Values;
    public bool HasAbility(string id) => _abilities.ContainsKey(id) || _talents.ContainsKey(id);
    public IEnumerable<AbilityDef> Abilities => _abilities.Values;

    public string CardName(CardDef c) => _texts.GetValueOrDefault(c.NameKey) ?? c.SanitizedName;
    public string AbilityName(string id) =>
        _texts.GetValueOrDefault("Card.Ability." + id) ?? _texts.GetValueOrDefault("Talent.Name." + id) ?? id;
    /// <summary>Texte du jeu par sa clé (null si absent), par exemple « Talent.Description.&lt;id&gt; ».</summary>
    public string? Text(string key) => _texts.GetValueOrDefault(key);

    // ------------------------------------------------------------------ chargement

    public static SimKnowledge Load(string dir, string language = "fr")
    {
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException($"Base de connaissances introuvable : {dir}");

        JsonElement Read(string file)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, file)));
            return doc.RootElement.Clone();
        }

        var manifest = Read("manifest.json");
        var settings = ReadSettings(Read("settings.json"));
        var cards = Read("cards.json").EnumerateArray().Select(ReadCard).ToList();
        var abilities = Read("abilities.json").EnumerateArray().Select(a => ReadAbility(a, false)).ToList();
        var talents = Read("talents.json").EnumerateArray().Select(a => ReadAbility(a, true)).ToList();
        var effects = Read("effects.json").EnumerateArray().Select(ReadBrick).ToList();
        var conditions = Read("conditions.json").EnumerateArray().Select(ReadBrick).ToList();
        var costs = Read("costs.json").EnumerateArray().Select(ReadBrick).ToList();
        var filters = Read("filters.json").EnumerateArray().Select(ReadBrick).ToList();
        var tox = Read("tox.json").EnumerateArray().Select(ReadTox).ToList();

        Dictionary<string, string>? texts = null;
        var textFile = Path.Combine(dir, "texts", language + ".json");
        if (File.Exists(textFile))
            texts = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(textFile));

        return new SimKnowledge(manifest.GetProperty("gameVersion").GetString() ?? "?", settings,
            cards, abilities, effects, conditions, costs, filters, texts, tox, talents);
    }

    private static DuelSettings ReadSettings(JsonElement s)
    {
        double Num(string key) => s.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : throw new InvalidDataException($"settings.json : réglage « {key} » absent.");

        if (!s.TryGetProperty("team_data", out var team) || team.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("settings.json : réglage « team_data » absent.");

        return new DuelSettings(
            StartLuck: (int)Num("startLuck"),
            LuckPerTurn: Num("luckPerTurn"),
            TurnAfterOvertime: (int)Num("turnAfterOvertime"),
            OvertimeReducingRatio: Num("overTimeReducingRatio"),
            OvertimeScalingRatio: Num("overtimeScalingRatio"),
            GoldPerKill: (int)Num("goldPerKill"),
            FrozenBrokenByPoison: Num("frozenBrokenByPoison") != 0,
            FrozenBrokenByHeal: Num("frozenBrokenByHeal") != 0,
            PlayerIdFirstPlayer: (int)Num("playerIDFirstPlayer"),
            CardsPerDeck: (int)Num("cards_perDeck"),
            RandomElements: team.EnumerateArray().Select(e => e.GetString()!.Split(':', 2)[^1]).ToList(),
            EchoSmoothing: Num("echoSmoothing"),
            FrozenBrokenByToxEquip: Num("frozenBrokenByToxEquip") != 0,
            FrozenOwnerTurns: (int)Num("frozenOwnerTurns"),
            MysteryPool: Links(s, "mysteryPassivePool"),
            HeartPool: Links(s, "allHeartPassivePool"),
            Shop: new ShopSettings(
                Pools: new[] { "commonStatusModificator", "rareStatusModificator", "epicStatusModificator", "legendaryStatusModificator" }
                    .Select(k => (IReadOnlyList<string>)Links(s, k)).ToList(),
                RarityCurves: new[] { "floatTrackCommon", "floatTrackRare", "floatTrackEpic", "floatTrackLeg" }
                    .Select(k => (IReadOnlyList<CurveKey>)Curve(s, k)).ToList(),
                RerollCost: (int)Num("rerollCost"),
                ItemShopMax: (int)Num("itemShopMax"),
                LuckForSkip: Num("luckForSkip"),
                SkipPerCardAlive: s.TryGetProperty("skipPerCardAlive", out var skip) && skip.ValueKind == JsonValueKind.Array
                    ? skip.EnumerateArray().Select(x => (int)x.GetDouble()).ToList()
                    : throw new InvalidDataException("settings.json : réglage « skipPerCardAlive » absent."),
                BanPerPlayer: (int)Num("banGlobalPerPlayer")));
    }

    private static List<CurveKey> Curve(JsonElement s, string key)
    {
        if (!s.TryGetProperty(key, out var c) || !c.TryGetProperty("m_Curve", out var keys))
            throw new InvalidDataException($"settings.json : courbe « {key} » absente.");
        return keys.EnumerateArray().Select(k => new CurveKey(
            (float)k.GetProperty("time").GetDouble(), (float)k.GetProperty("value").GetDouble(),
            (float)k.GetProperty("inSlope").GetDouble(), (float)k.GetProperty("outSlope").GetDouble())).ToList();
    }

    private static List<string> Links(JsonElement s, string key)
    {
        if (!s.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"settings.json : réglage « {key} » absent.");
        return v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()!.Split(':', 2)[^1]).ToList();
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double Dbl(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True ||
                                             (v.ValueKind == JsonValueKind.Number && v.GetDouble() != 0));

    private static List<string> Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : new List<string>();

    private static CardDef ReadCard(JsonElement c) => new(
        Str(c, "id")!, Str(c, "sanitizedName")!, Str(c, "nameKey")!, Str(c, "element")!, Str(c, "rarity") ?? "",
        (int)Dbl(c, "hp"), (int)Dbl(c, "goldIncome"),
        c.GetProperty("resistances").EnumerateObject().ToDictionary(p => p.Name, p => (int)p.Value.GetDouble()),
        Strings(c, "abilities"), Strings(c, "innatePassives"), Strings(c, "talents"));

    private static AbilityDef ReadAbility(JsonElement a, bool isTalent) => new(
        Str(a, "id")!, Str(a, "element"), Str(a, "trigger") ?? "", Str(a, "target") ?? "",
        Strings(a, "conditionsTrigger"), Strings(a, "conditionsTarget"), Strings(a, "filtersTarget"),
        a.GetProperty("costs").EnumerateArray().Select(x => new CostDef(Str(x, "cost")!, (int)Dbl(x, "value"))).ToList(),
        a.GetProperty("actions").EnumerateArray().Select(x => new ActionDef(
            Str(x, "effect") ?? "", Dbl(x, "min"), Dbl(x, "max"), Str(x, "element"), Str(x, "condition"),
            Bool(x, "useMissRoll"),
            x.TryGetProperty("percentHPToProc", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : -1)).ToList(),
        Dbl(a, "chanceOfMiss"), Dbl(a, "chanceOfCrit"), Dbl(a, "critDamageRatio"),
        (int)Dbl(a, "cooldown"), (int)Dbl(a, "numberOfActivation"), Bool(a, "exhaust"),
        Dbl(a, "chanceOfResetCooldown"), Strings(a, "chainAbilities"), Strings(a, "comboAbilities"),
        Strings(a, "passives"), (int)Dbl(a, "duration"), Bool(a, "cooldownDropsPerCardGone"),
        Bool(a, "freezeOnlyOnCrit"), Bool(a, "choosesSlot"),
        isTalent, !isTalent || !a.TryGetProperty("smoothChance", out var sc) || sc.ValueKind != JsonValueKind.False,
        Bool(a, "decouverteEnable"));

    private static ToxDef ReadTox(JsonElement t) => new(
        Str(t, "uuid")!, Str(t, "assetName") ?? "", Str(t, "statusType") ?? "", Dbl(t, "value"),
        Str(t, "element")?.Split(':', 2)[^1], Str(t, "rarity")?.Split(':', 2)[^1], (int)Dbl(t, "cost"), Str(t, "shopPool"),
        Bool(t, "inMysteryPool"), Bool(t, "inHeartPool"));

    private static BrickDef ReadBrick(JsonElement b) => new(
        Str(b, "id")!, Str(b, "class") ?? "",
        b.TryGetProperty("parameters", out var p) && p.ValueKind == JsonValueKind.Object
            ? p.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone())
            : new Dictionary<string, JsonElement>());
}
