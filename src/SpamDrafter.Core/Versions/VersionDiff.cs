using System.Text;
using System.Text.Json;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.Core.Versions;

/// <summary>
/// Compare deux bases de connaissances (deux versions de SPAM) et en tire un <see cref="ChangeSet"/> (phase 12, lot E). Compare
/// des données uniquement (aucune règle de jeu) : cartes (PV, revenu, élément, rareté, résistances, liste de capacités et de
/// talents), et toute capacité ou tout talent dont une donnée a changé (même un réglage interne).
/// </summary>
public static class VersionDiff
{
    /// <summary>Propriétés ignorées dans la comparaison : des identifiants d'import, pas du contenu de jeu.</summary>
    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal) { "sourceFile", "uuid", "talentUuid" };

    public static ChangeSet Compute(string fromDir, string toDir)
    {
        var from = KnowledgeBase.Load(fromDir);
        var to = KnowledgeBase.Load(toDir);
        var abilities = Entries(Raw(fromDir, "abilities.json"), Raw(toDir, "abilities.json"),
            id => from.Descriptions.Ability(id), id => to.Descriptions.Ability(id),
            id => from.Text("Card.Ability." + id), id => to.Text("Card.Ability." + id));
        var talents = Entries(Raw(fromDir, "talents.json"), Raw(toDir, "talents.json"),
            id => from.Descriptions.Talent(id), id => to.Descriptions.Talent(id), from.TalentName, to.TalentName);
        var changedAbilities = abilities.Where(a => a.Kind == ChangeKind.Changed).Select(a => a.Id).ToHashSet();

        var cards = new List<CardChange>();
        var fromCards = from.Cards.ToDictionary(c => c.Id);
        var toCards = to.Cards.ToDictionary(c => c.Id);
        foreach (var id in fromCards.Keys.Union(toCards.Keys).OrderBy(i => i, StringComparer.Ordinal))
        {
            fromCards.TryGetValue(id, out var a);
            toCards.TryGetValue(id, out var b);
            if (a is null) { cards.Add(new CardChange(id, to.CardName(b!), ChangeKind.Added, Array.Empty<FieldChange>())); continue; }
            if (b is null) { cards.Add(new CardChange(id, from.CardName(a), ChangeKind.Removed, Array.Empty<FieldChange>())); continue; }
            var fields = new List<FieldChange>();
            void Compare(string field, string? x, string? y) { if (x != y) fields.Add(new FieldChange(field, x, y)); }
            Compare("hp", a.Hp.ToString(), b.Hp.ToString());
            Compare("goldIncome", a.GoldIncome.ToString(), b.GoldIncome.ToString());
            Compare("element", a.Element, b.Element);
            Compare("rarity", a.Rarity, b.Rarity);
            foreach (var el in a.Resistances.Keys.Union(b.Resistances.Keys).Order(StringComparer.Ordinal))
                Compare("resistance." + el, a.Resistances.GetValueOrDefault(el).ToString(), b.Resistances.GetValueOrDefault(el).ToString());
            Compare("abilities", string.Join("|", a.Abilities), string.Join("|", b.Abilities));
            Compare("talents", string.Join("|", a.Talents), string.Join("|", b.Talents));
            // Une capacité que la carte garde dans les deux versions mais dont le contenu a changé.
            foreach (var ab in a.Abilities.Intersect(b.Abilities).Where(changedAbilities.Contains))
                fields.Add(new FieldChange("ability.changed", ab, ab));
            if (fields.Count > 0) cards.Add(new CardChange(id, to.CardName(b), ChangeKind.Changed, fields));
        }
        return new ChangeSet(ChangeSet.CurrentFormat, from.Manifest.GameVersion, to.Manifest.GameVersion, cards, talents, abilities);
    }

    // ----- Capacités et talents -----
    private static List<EntryChange> Entries(Dictionary<string, string> before, Dictionary<string, string> after,
        Func<string, string> descBefore, Func<string, string> descAfter, Func<string, string> nameBefore, Func<string, string> nameAfter)
    {
        var result = new List<EntryChange>();
        foreach (var id in before.Keys.Union(after.Keys).OrderBy(i => i, StringComparer.Ordinal))
        {
            var hadBefore = before.TryGetValue(id, out var x);
            var hasAfter = after.TryGetValue(id, out var y);
            static string? Or(string s) => s.Length == 0 ? null : s;
            if (!hadBefore) result.Add(new EntryChange(id, nameAfter(id), ChangeKind.Added, null, Or(descAfter(id)), false));
            else if (!hasAfter) result.Add(new EntryChange(id, nameBefore(id), ChangeKind.Removed, Or(descBefore(id)), null, false));
            else if (x != y)
            {
                var d1 = Or(descBefore(id)); var d2 = Or(descAfter(id));
                result.Add(new EntryChange(id, nameAfter(id), ChangeKind.Changed, d1, d2, InternalOnly: d1 == d2));
            }
        }
        return result;
    }

    /// <summary>Contenu de chaque élément d'un fichier JSON (capacités, talents), sans les propriétés ignorées, par identifiant.</summary>
    private static Dictionary<string, string> Raw(string dir, string file)
    {
        var result = new Dictionary<string, string>();
        var path = Path.Combine(dir, file);
        if (!File.Exists(path)) return result;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var e in doc.RootElement.EnumerateArray())
            if (e.TryGetProperty("id", out var id) && id.GetString() is { } key) result[key] = Normalize(e);
        return result;
    }

    private static string Normalize(JsonElement e)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream)) Write(w, e);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(Utf8JsonWriter w, JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                w.WriteStartObject();
                foreach (var p in e.EnumerateObject().Where(p => !Ignored.Contains(p.Name))) { w.WritePropertyName(p.Name); Write(w, p.Value); }
                w.WriteEndObject();
                break;
            case JsonValueKind.Array:
                w.WriteStartArray();
                foreach (var i in e.EnumerateArray()) Write(w, i);
                w.WriteEndArray();
                break;
            default:
                e.WriteTo(w);
                break;
        }
    }
}
