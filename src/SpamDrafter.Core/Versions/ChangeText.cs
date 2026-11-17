using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.Core.Versions;

/// <summary>Ce qu'une mise à jour de SPAM change pour un deck : une ligne par différence, et si le deck n'est plus valide.</summary>
public sealed record DeckImpact(IReadOnlyList<string> Lines, bool Broken)
{
    public bool IsImpacted => Lines.Count > 0;
    public static DeckImpact None { get; } = new(Array.Empty<string>(), false);
}

/// <summary>
/// Met les différences entre deux versions en phrases (phase 12, lot E) : la comparaison est faite par le Studio sur des données
/// brutes ; ici on les écrit dans la langue de l'application, à partir de la base de connaissances actuelle.
/// </summary>
public static class ChangeText
{
    /// <summary>Une différence de carte, en une ligne.</summary>
    public static string Describe(FieldChange f, ChangeSet cs, KnowledgeBase kb)
    {
        string El(string? id) => string.IsNullOrEmpty(id) ? "—" : kb.ElementName(id);
        string Plain(string? s) => string.IsNullOrEmpty(s) ? "—" : s;
        string AbilityName(string id) => cs.Ability(id)?.Name ?? kb.Text("Card.Ability." + id);
        string TalentName(string id) => cs.Talent(id)?.Name ?? kb.TalentName(id);
        switch (f.Field)
        {
            case "hp": return $"PV : {Plain(f.Before)} → {Plain(f.After)}";
            case "goldIncome": return $"Revenu : {Plain(f.Before)} → {Plain(f.After)}";
            case "element": return $"Élément : {El(f.Before)} → {El(f.After)}";
            case "rarity": return $"Rareté : {Rarity(f.Before, kb)} → {Rarity(f.After, kb)}";
            case "abilities":
            {
                var b = Split(f.Before); var a = Split(f.After);
                var added = a.Except(b).Select(AbilityName).ToList();
                var removed = b.Except(a).Select(AbilityName).ToList();
                var parts = new List<string>();
                if (added.Count > 0) parts.Add("ajoutée(s) : " + string.Join(", ", added));
                if (removed.Count > 0) parts.Add("retirée(s) : " + string.Join(", ", removed));
                return parts.Count > 0 ? "Capacités " + string.Join(" ; ", parts) : "Ordre des capacités modifié";
            }
            case "talents":
            {
                var b = Split(f.Before); var a = Split(f.After);
                var parts = new List<string>();
                for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
                {
                    var x = i < b.Count ? b[i] : null; var y = i < a.Count ? a[i] : null;
                    if (x == y) continue;
                    parts.Add($"Talent {i + 1} : {(x is null ? "—" : TalentName(x))} → {(y is null ? "—" : TalentName(y))}");
                }
                return string.Join(" ; ", parts);
            }
            case "ability.changed":
            {
                var id = f.Before ?? "";
                var entry = cs.Ability(id);
                return $"Capacité « {AbilityName(id)} » modifiée" + Detail(entry);
            }
            default:
                if (f.Field.StartsWith("resistance.", StringComparison.Ordinal))
                    return $"Résistance {El(f.Field["resistance.".Length..])} : {Plain(f.Before)} % → {Plain(f.After)} %";
                return $"{f.Field} : {Plain(f.Before)} → {Plain(f.After)}";
        }
    }

    private static string Rarity(string? id, KnowledgeBase kb) => string.IsNullOrEmpty(id) ? "—" : kb.RarityName(id);

    private static List<string> Split(string? s) => string.IsNullOrEmpty(s) ? new() : s.Split('|').ToList();

    private static string OneLine(string? s) => (s ?? "—").Replace("\r", "").Replace("\n", " / ");

    /// <summary>« : avant → après », ou « (réglages internes, description inchangée) ».</summary>
    private static string Detail(EntryChange? e) => e switch
    {
        null => "",
        { InternalOnly: true } => " (réglages internes ; la description n'a pas changé)",
        _ => $" : {OneLine(e.DescriptionBefore)} → {OneLine(e.DescriptionAfter)}",
    };

    /// <summary>
    /// Impact de la version actuelle sur un deck créé avec une version plus ancienne (celle de <see cref="Deck.GameVersion"/>).
    /// Aucun impact si le deck est de la version actuelle, ou si les différences ne sont pas disponibles pour sa version.
    /// </summary>
    public static DeckImpact ImpactOf(Deck deck, KnowledgeBase kb) =>
        kb.ChangesFrom(deck.GameVersion) is { } cs ? Impact(cs, deck, kb) : DeckImpact.None;

    /// <summary>Ce que la mise à jour change pour ce deck (cartes et talents choisis). Vide : le deck n'est pas touché.</summary>
    public static DeckImpact Impact(ChangeSet cs, Deck deck, KnowledgeBase kb)
    {
        var lines = new List<string>();
        var broken = false;
        foreach (var slot in deck.Slots)
        {
            var card = cs.Card(slot.CardId);
            var name = card?.Name ?? (kb.FindCard(slot.CardId) is { } c ? kb.CardName(c) : slot.CardId);
            if (card is { Kind: ChangeKind.Removed })
            {
                lines.Add($"{name} : carte supprimée du jeu.");
                broken = true;
            }
            else if (card is { Kind: ChangeKind.Changed })
                foreach (var f in card.Fields) lines.Add($"{name} — {Describe(f, cs, kb)}");
            if (slot.TalentId is { } talentId && cs.Talent(talentId) is { } t)
            {
                if (t.Kind == ChangeKind.Removed) { lines.Add($"{name} — talent « {t.Name} » supprimé du jeu."); broken = true; }
                else if (t.Kind == ChangeKind.Changed) lines.Add($"{name} — talent « {t.Name} » modifié" + Detail(t));
            }
        }
        return new DeckImpact(lines, broken);
    }

    /// <summary>Toutes les nouveautés d'une version, une ligne par élément (« Quoi de neuf »).</summary>
    public static IReadOnlyList<string> Summary(ChangeSet cs, KnowledgeBase kb)
    {
        var lines = new List<string>();
        void List(string title, IEnumerable<string> names)
        {
            var l = names.ToList();
            if (l.Count > 0) lines.Add($"{title} ({l.Count}) : {string.Join(", ", l)}");
        }
        List("Cartes ajoutées", cs.Cards.Where(c => c.Kind == ChangeKind.Added).Select(c => c.Name));
        List("Cartes retirées", cs.Cards.Where(c => c.Kind == ChangeKind.Removed).Select(c => c.Name));
        foreach (var c in cs.Cards.Where(c => c.Kind == ChangeKind.Changed))
            foreach (var f in c.Fields) lines.Add($"{c.Name} — {Describe(f, cs, kb)}");
        List("Talents ajoutés", cs.Talents.Where(t => t.Kind == ChangeKind.Added).Select(t => t.Name));
        List("Talents retirés", cs.Talents.Where(t => t.Kind == ChangeKind.Removed).Select(t => t.Name));
        foreach (var t in cs.Talents.Where(t => t.Kind == ChangeKind.Changed)) lines.Add($"Talent « {t.Name} » modifié" + Detail(t));
        return lines;
    }
}
