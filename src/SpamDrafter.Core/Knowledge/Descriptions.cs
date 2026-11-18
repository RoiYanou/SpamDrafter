using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpamDrafter.Core.Knowledge;

/// <summary>Une action d'une capacité ou d'un talent : son effet et sa plage de valeurs (min à max).</summary>
public sealed record AbilityAction(string Effect, double Min, double Max);

/// <summary>
/// Ce qu'il faut d'une capacité ou d'un talent (abilities.json, talents.json) pour écrire sa description : ses valeurs, sa
/// durée, son nombre d'utilisations, ses capacités enchaînées.
/// </summary>
public sealed record AbilityRecord(string Id, IReadOnlyList<AbilityAction> Actions, int Duration, int NumberOfActivation,
    double ChanceOfResetCooldown, IReadOnlyList<string> ChainAbilities, bool ChoiceSelector,
    double ChanceOfMiss = 0, bool CanCrit = false, double ChanceOfCrit = 0, double CritDamageRatio = 1, int Cooldown = 0, string? Element = null,
    IReadOnlyList<string>? Passives = null);

/// <summary>
/// Descriptions chiffrées des capacités et des talents (phase 12, lot C, KI-021).
/// Les textes du jeu contiennent des emplacements de valeurs (« &lt;value1D&gt; », « &lt;min1&gt; »…). Le jeu les remplace dans
/// <c>AbilityData.GetDescWithDynamicValue</c> ; ici on reproduit ce calcul **pour une carte hors partie** (aucun joueur, aucun
/// niveau d'élément, aucun bonus) : les valeurs « D » (dynamiques) valent alors la valeur de base arrondie, et les
/// « statiques » la valeur ou la plage telle que dans les données. 🔒 Règles lues dans le code décompilé :
/// <list type="bullet">
/// <item><c>&lt;valueN&gt;</c> : |(min + max) / 2| tronqué à l'entier ; <c>&lt;minN&gt;</c> / <c>&lt;maxN&gt;</c> : |min| / |max| ;</item>
/// <item><c>&lt;valueND&gt;</c>, <c>&lt;minND&gt;</c>, <c>&lt;maxND&gt;</c> : la même valeur arrondie à l'entier le plus proche (moitié vers le pair) ;</item>
/// <item><c>&lt;duration&gt;</c>, <c>&lt;chanceOfReset&gt;</c> (0 à 100), <c>&lt;limitedUse1&gt;</c> : durée, chance de remise à zéro, nombre d'utilisations ;</item>
/// <item>les capacités enchaînées s'ajoutent à la suite (séparées par « Ou » quand le joueur choisit).</item>
/// </list>
/// Les icônes et les couleurs du jeu deviennent du texte simple : « FEU[FEU] » donne « Feu », « [CRITIQUE] » « critique ».
/// </summary>
public sealed class DescriptionFormatter
{
    private static readonly (string Tag, string Element)[] ElementTags =
    {
        ("FEU", "fire"), ("EAU", "water"), ("TERRE", "earth"), ("NATURE", "nature"), ("LUMIERE", "light"), ("OMBRE", "shadow"),
    };

    private readonly KnowledgeBase _kb;
    private readonly Lazy<Dictionary<string, AbilityRecord>> _abilities;
    private readonly Lazy<Dictionary<string, AbilityRecord>> _talents;

    public DescriptionFormatter(KnowledgeBase kb)
    {
        _kb = kb;
        _abilities = new(() => Load("abilities.json"));
        _talents = new(() => Load("talents.json"));
    }

    private Dictionary<string, AbilityRecord> Load(string file)
    {
        var path = Path.Combine(_kb.Directory, file);
        var result = new Dictionary<string, AbilityRecord>();
        if (!File.Exists(path)) return result;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var id = e.GetProperty("id").GetString();
                if (id is null) continue;
                var actions = new List<AbilityAction>();
                if (e.TryGetProperty("actions", out var acts) && acts.ValueKind == JsonValueKind.Array)
                    foreach (var a in acts.EnumerateArray())
                        actions.Add(new AbilityAction(Str(a, "effect"), Num(a, "min"), Num(a, "max")));
                var chain = new List<string>();
                if (e.TryGetProperty("chainAbilities", out var ch) && ch.ValueKind == JsonValueKind.Array)
                    chain.AddRange(ch.EnumerateArray().Select(x => x.GetString()).OfType<string>());
                result[id] = new AbilityRecord(id, actions, (int)Num(e, "duration"), (int)Num(e, "numberOfActivation"),
                    Num(e, "chanceOfResetCooldown"), chain, Str(e, "target") == "ChoiceSelector",
                    Num(e, "chanceOfMiss"), Bool(e, "canCrit"), Num(e, "chanceOfCrit"), Num(e, "critDamageRatio"), (int)Num(e, "cooldown"),
                    Str(e, "element") is { Length: > 0 } el ? el : null,
                    e.TryGetProperty("passives", out var ps) && ps.ValueKind == JsonValueKind.Array ? ps.EnumerateArray().Select(x => x.GetString()).OfType<string>().ToList() : null);
            }
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidOperationException) { result.Clear(); }
        return result;

        static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
        static double Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    }

    /// <summary>Description d'une capacité de carte, chiffrée ; vide si le jeu n'en donne pas.</summary>
    public string Ability(string id) => Describe("Card.AbilityDesc." + id, id, _abilities.Value, new HashSet<string>());

    /// <summary>
    /// La même description, avec chaque repère d'élément remplacé par <see cref="ElementOpen"/> + identifiant de l'élément + <see cref="ElementClose"/>
    /// (au lieu de son nom) : l'écran y met l'icône et la couleur de l'élément.
    /// </summary>
    public string AbilityRich(string id) => Describe("Card.AbilityDesc." + id, id, _abilities.Value, new HashSet<string>(), rich: true);

    /// <summary>Tox (passifs) appliqués par une capacité et par ses capacités enchaînées : ceux dont le nom apparaît dans sa description.</summary>
    public IReadOnlyList<ToxEntry> AbilityTox(string id)
    {
        var result = new List<ToxEntry>();
        var seen = new HashSet<string>();
        void Visit(string a)
        {
            if (!seen.Add(a) || !_abilities.Value.TryGetValue(a, out var r)) return;
            foreach (var uuid in r.Passives ?? Array.Empty<string>())
                if (_kb.Tox(uuid) is { } t) result.Add(t);
            foreach (var c in r.ChainAbilities) Visit(c);
        }
        Visit(id);
        return result;
    }

    public const char ElementOpen = '';
    public const char ElementClose = '';

    /// <summary>Précision, critiques et recharge d'une capacité de carte ; null si la capacité est inconnue.</summary>
    public AbilityStats? Stats(string abilityId) => _abilities.Value.TryGetValue(abilityId, out var r)
        ? new AbilityStats(r.ChanceOfMiss, r.CanCrit, r.ChanceOfCrit, r.CritDamageRatio, r.Cooldown, r.Element) : null;

    /// <summary>
    /// Description d'un Tox, comme le jeu (<c>StatusData.GetDesc</c>) : texte « Card.PassifDesc.&lt;titre avant le « _ »&gt; » dont
    /// <c>&lt;value1&gt;</c> devient la valeur absolue du Tox.
    /// </summary>
    public string Tox(ToxEntry tox)
    {
        var raw = _kb.Text("Card.PassifDesc." + ToxKey(tox));
        if (raw == "Card.PassifDesc." + ToxKey(tox)) return "";
        var value = Math.Abs(tox.Value).ToString("0.##", CultureInfo.InvariantCulture);
        return Resolve(raw.Replace("<value1>", value), null);
    }

    /// <summary>Clé de texte d'un Tox : son titre avant le premier « _ » (« Phoenix_C » donne « Phoenix »).</summary>
    public static string ToxKey(ToxEntry tox) => tox.Title.Split('_')[0];

    /// <summary>Description d'un talent, chiffrée.</summary>
    public string Talent(string id) => Describe("Talent.Description." + id, id, _talents.Value, new HashSet<string>());

    private string Describe(string textKey, string id, Dictionary<string, AbilityRecord> source, HashSet<string> visited, bool rich = false)
    {
        var raw = _kb.Text(textKey);
        if (raw == textKey) raw = "";   // pas de texte dans le jeu
        source.TryGetValue(id, out var record);
        var text = raw.Length == 0 ? "" : Resolve(raw, record, rich);
        // Capacités enchaînées : leur description s'ajoute à la suite (comme dans le jeu).
        if (record is { ChainAbilities.Count: > 0 } && visited.Add(id))
        {
            var or = _kb.Text("Game.UI.Or");
            if (or == "Game.UI.Or") or = "Ou";
            var n = 0;
            var sb = new StringBuilder(text);
            foreach (var chainId in record.ChainAbilities)
            {
                var chain = Describe("Card.AbilityDesc." + chainId, chainId, _abilities.Value, visited, rich);
                if (string.IsNullOrWhiteSpace(chain)) continue;
                if (sb.Length > 0) sb.Append(record.ChoiceSelector && n > 0 ? "\n" + or + "\n" : "\n");
                sb.Append(chain);
                n++;
            }
            text = sb.ToString();
        }
        return text;
    }

    /// <summary>Remplace les emplacements de valeurs, les repères d'éléments, puis retire les balises de mise en forme.</summary>
    public string Resolve(string description, AbilityRecord? record, bool rich = false)
    {
        var d = description.Replace("\r\n", "\n");
        if (record is not null)
        {
            // « Usage limité <limitedUse1D>/<limitedUse1> » : hors partie, on donne le nombre d'utilisations total.
            d = d.Replace("<limitedUse1D>/<limitedUse1>", record.NumberOfActivation.ToString(CultureInfo.InvariantCulture));
            for (var i = 0; i < record.Actions.Count; i++)
            {
                var a = record.Actions[i];
                var n = (i + 1).ToString(CultureInfo.InvariantCulture);
                var mean = Math.Abs((int)((a.Min + a.Max) / 2));
                d = d.Replace($"<value{n}A>", mean.ToString(CultureInfo.InvariantCulture))
                     .Replace($"<min{n}A>", Plain(Math.Abs(a.Min))).Replace($"<max{n}A>", Plain(Math.Abs(a.Max)))
                     .Replace($"<value{n}D>", Rounded((int)((a.Min + a.Max) / 2)))
                     .Replace($"<min{n}D>", Rounded(a.Min)).Replace($"<max{n}D>", Rounded(a.Max))
                     .Replace($"<value{n}>", mean.ToString(CultureInfo.InvariantCulture))
                     .Replace($"<min{n}>", Plain(Math.Abs(a.Min))).Replace($"<max{n}>", Plain(Math.Abs(a.Max)));
            }
            d = d.Replace("<duration>", record.Duration.ToString(CultureInfo.InvariantCulture))
                 .Replace("<chanceOfReset>", ((int)Math.Clamp(record.ChanceOfResetCooldown, 0, 100)).ToString(CultureInfo.InvariantCulture))
                 .Replace("<limitedUse1>", record.NumberOfActivation.ToString(CultureInfo.InvariantCulture))
                 .Replace("<limitedUse1D>", record.NumberOfActivation.ToString(CultureInfo.InvariantCulture));
        }
        // Dépend de l'état de la partie (PV manquants de la carte) : hors partie, on ne peut pas donner de chiffre.
        d = d.Replace("<missingHpBonusD>", "X");

        foreach (var (tag, element) in ElementTags)
            d = Regex.Replace(d, @"(?:\s?\p{L}+)?" + Regex.Escape("[" + tag + "]"), " " + (rich ? ElementOpen + element + ElementClose : _kb.ElementName(element)));
        d = d.Replace("[CRITIQUE]", " critique ").Replace("[PRECISION]", " précision ");

        // Balises de mise en forme du jeu (couleurs, icônes, barré, gras…) : du texte simple.
        d = Regex.Replace(d, @"</?(color|sprite|s|b|i|u|material|size|link|mark|style|align|nobr|font|voffset|cspace)\b[^>]*>", "");
        d = Regex.Replace(d, @"[ \t]{2,}", " ");
        return string.Join("\n", d.Split('\n').Select(l => l.Trim())).Trim();
    }

    /// <summary>Valeur « dynamique » d'une carte hors partie : la valeur absolue, arrondie (moitié vers le pair, comme Unity).</summary>
    private static string Rounded(double raw) =>
        ((int)Math.Round(Math.Abs(raw), MidpointRounding.ToEven)).ToString(CultureInfo.InvariantCulture);

    private static string Plain(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
