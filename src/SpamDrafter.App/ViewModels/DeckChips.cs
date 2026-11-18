using System.Globalization;
using Avalonia.Data.Converters;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.App.ViewModels;

/// <summary>
/// Une carte d'un deck telle que l'affiche la liste des decks (phase 13, D-078) : la carte, la pastille du talent choisi
/// (« T2 ») et son nom complet pour l'info-bulle. <see cref="Card"/> est null pour un emplacement encore libre.
/// </summary>
public sealed record DeckChip(CardItem? Card, string TalentTag, string TalentName)
{
    public bool HasCard => Card is not null;
    public bool IsEmpty => Card is null;
    public bool HasTalent => TalentTag.Length > 0;
    /// <summary>Illustration en bonne qualité pour les pastilles agrandies de l'accueil (la miniature serait floue).</summary>
    public Avalonia.Media.Imaging.Bitmap? HomeImage => Card?.MediumImage;
}

/// <summary>Transforme les emplacements d'un deck en pastilles (toujours 4 : les emplacements libres sont vides).</summary>
public sealed class DeckChipsConverter : IValueConverter
{
    public static readonly DeckChipsConverter Instance = new();
    private static KnowledgeBase? _kb;
    private static Dictionary<string, CardItem> _items = new();

    /// <summary>Appelé au chargement des données : le convertisseur retrouve les cartes par leur identifiant.</summary>
    public static void Register(KnowledgeBase kb, IEnumerable<CardItem> items)
    {
        _kb = kb;
        _items = items.ToDictionary(i => i.Card.Id);
    }

    public static IReadOnlyList<DeckChip> Build(IEnumerable<DeckSlot> slots)
    {
        var chips = new List<DeckChip>();
        foreach (var s in slots.Take(Deck.MaxCards))
        {
            if (!_items.TryGetValue(s.CardId, out var item)) { chips.Add(new DeckChip(null, "", "")); continue; }
            var index = s.TalentId is null ? -1 : item.Card.Talents.ToList().IndexOf(s.TalentId);
            chips.Add(index < 0 || _kb is null
                ? new DeckChip(item, "", $"{item.Name} · aucun talent choisi")
                : new DeckChip(item, $"T{index + 1}", $"{item.Name} · Talent {index + 1} : {_kb.TalentName(s.TalentId!)}"));
        }
        while (chips.Count < Deck.MaxCards) chips.Add(new DeckChip(null, "", ""));
        return chips;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is IEnumerable<DeckSlot> slots ? Build(slots) : Array.Empty<DeckChip>();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
