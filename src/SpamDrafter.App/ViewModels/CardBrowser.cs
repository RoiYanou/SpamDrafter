using System.Collections.ObjectModel;
using SpamDrafter.Core.Knowledge;
using SpamDrafter.Core.Settings;

namespace SpamDrafter.App.ViewModels;

public sealed record FilterOption(string? Id, string Label, Avalonia.Media.Imaging.Bitmap? Icon = null)
{
    public override string ToString() => Label;
}

/// <summary>Une case à cocher de filtre (un élément, une rareté) : on peut en cocher plusieurs et les combiner.</summary>
public sealed class ToggleOption : ViewModelBase
{
    private bool _checked;
    private readonly Action _changed;
    public ToggleOption(string id, string label, Avalonia.Media.Imaging.Bitmap? icon, Action changed) { Id = id; Label = label; Icon = icon; _changed = changed; }
    public string Id { get; }
    public string Label { get; }
    public Avalonia.Media.Imaging.Bitmap? Icon { get; }
    public bool IsChecked { get => _checked; set { if (Set(ref _checked, value)) _changed(); } }
    /// <summary>Change l'état sans prévenir la liste (elle se rafraîchit une seule fois après).</summary>
    internal void SetSilently(bool value) { _checked = value; Raise(nameof(IsChecked)); }
}

/// <summary>Liste de cartes filtrable (recherche, élément, rareté), partagée par la Bibliothèque et le Deck builder.</summary>
public sealed class CardBrowser : ViewModelBase
{
    private readonly IReadOnlyList<CardItem> _all;
    private string _search = "";
    private FilterOption _element;
    private FilterOption _rarity;
    private FilterOption _species;
    private FilterOption _region;
    private FilterOption _chronology;
    private FilterOption _sort;
    private CardItem? _selected;
    private readonly KnowledgeBase _kb;

    public CardBrowser(IReadOnlyList<CardItem> all, KnowledgeBase kb)
    {
        _all = all; _kb = kb;
        ElementOptions = new[] { new FilterOption(null, "Tous les éléments") }
            .Concat(kb.Elements.Select(e => new FilterOption(e.Id, kb.ElementName(e.Id), Icons.Load(kb, e.Icon, 32)))).ToList();
        RarityOptions = new[] { new FilterOption(null, "Toutes les raretés") }
            .Concat(kb.Rarities.Select(r => new FilterOption(r.Id, kb.RarityName(r.Id), Icons.Load(kb, r.Icon, 32)))).ToList();
        ElementChoices = kb.Elements.Select(e => new ToggleOption(e.Id, kb.ElementName(e.Id), Icons.Load(kb, e.Icon, 32), ChoiceChanged)).ToList();
        RarityChoices = kb.Rarities.Select(r => new ToggleOption(r.Id, kb.RarityName(r.Id), Icons.Load(kb, r.Icon, 32), ChoiceChanged)).ToList();
        _element = ElementOptions[0];
        _rarity = RarityOptions[0];
        SpeciesOptions = LoreOptions("Toutes les espèces", all.Select(i => i.Lore?.Species));
        RegionOptions = LoreOptions("Toutes les régions", all.Select(i => i.Lore?.Region));
        ChronologyOptions = LoreOptions("Toutes les périodes", all.Select(i => i.Lore?.Chronology), ChronologicalOrder);
        _species = SpeciesOptions[0]; _region = RegionOptions[0]; _chronology = ChronologyOptions[0];
        SortOptions = new FilterOption[]
        {
            new("order", "Trier par"), new("name", "Nom (A → Z)"), new("hp", "PV (du plus haut)"),
            new("income", "Spamtons (du plus haut)"), new("element", "Élément"), new("rarity", "Rareté (de la plus haute)"),
        };
        _sort = SortOptions[0];
        Items = new ObservableCollection<CardItem>(all);
    }

    /// <summary>Cases à cocher : éléments et raretés (aucune cochée = toutes).</summary>
    public IReadOnlyList<ToggleOption> ElementChoices { get; }
    public IReadOnlyList<ToggleOption> RarityChoices { get; }
    private void ChoiceChanged() => Refresh();
    private static List<string> Checked(IEnumerable<ToggleOption> choices) => choices.Where(c => c.IsChecked).Select(c => c.Id).ToList();
    private static void SetChecks(IEnumerable<ToggleOption> choices, IEnumerable<string> ids)
    {
        var set = ids.ToHashSet();
        foreach (var c in choices) c.SetSilently(set.Contains(c.Id));
    }
    public IReadOnlyList<FilterOption> ElementOptions { get; }
    public IReadOnlyList<FilterOption> RarityOptions { get; }
    public IReadOnlyList<FilterOption> SpeciesOptions { get; }
    public IReadOnlyList<FilterOption> RegionOptions { get; }
    public IReadOnlyList<FilterOption> ChronologyOptions { get; }
    public IReadOnlyList<FilterOption> SortOptions { get; }

    /// <summary>« Filtrer par » : choisir une famille de lore (régions, espèces, périodes), puis une valeur de cette famille.</summary>
    public IReadOnlyList<FilterOption> LoreKindOptions { get; } = new FilterOption[]
    {
        new(null, "Filtrer par"), new("region", "Régions"), new("species", "Espèces"), new("chronology", "Périodes"),
    };
    private FilterOption _loreKind = null!;
    public FilterOption LoreKind
    {
        get => _loreKind ??= LoreKindOptions[0];
        set
        {
            if (value is null || ReferenceEquals(value, _loreKind)) return;
            _loreKind = value;
            _species = SpeciesOptions[0]; _region = RegionOptions[0]; _chronology = ChronologyOptions[0];
            Raise(nameof(LoreKind)); Raise(nameof(LoreValueOptions)); Raise(nameof(LoreValue)); Raise(nameof(HasLoreKind));
            Refresh();
        }
    }
    public bool HasLoreKind => LoreKind.Id is not null;
    /// <summary>Texte du menu « Filtrer par » : « Filtrer par », ou la famille et la valeur choisies (« Régions : Aelther »).</summary>
    public string LoreSummary => LoreKind.Id is null ? "Filtrer par"
        : LoreValue is { Id: not null } v ? $"{LoreKind.Label} : {v.Label}" : LoreKind.Label;
    public IReadOnlyList<FilterOption> LoreValueOptions => LoreKind.Id switch
    {
        "region" => RegionOptions, "species" => SpeciesOptions, "chronology" => ChronologyOptions, _ => Array.Empty<FilterOption>(),
    };
    public FilterOption? LoreValue
    {
        get => LoreKind.Id switch { "region" => _region, "species" => _species, "chronology" => _chronology, _ => null };
        set
        {
            if (value is null) return;
            switch (LoreKind.Id)
            {
                case "region": Region = value; break;
                case "species": Species = value; break;
                case "chronology": Chronology = value; break;
            }
            Raise(nameof(LoreValue));
        }
    }
    private void SyncLoreKind()
    {
        _loreKind = _region.Id is not null ? LoreKindOptions[1] : _species.Id is not null ? LoreKindOptions[2]
            : _chronology.Id is not null ? LoreKindOptions[3] : LoreKindOptions[0];
        Raise(nameof(LoreKind)); Raise(nameof(LoreValueOptions)); Raise(nameof(LoreValue)); Raise(nameof(HasLoreKind));
    }

    /// <summary>Les valeurs du lore, classées sans tenir compte des accents ; une option « toutes » en tête.</summary>
    private static IReadOnlyList<FilterOption> LoreOptions(string all, IEnumerable<string?> values, IReadOnlyList<string>? order = null) =>
        new[] { new FilterOption(null, all) }.Concat(values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).Distinct()
            .OrderBy(v => Rank(v, order)).ThenBy(v => SpamDrafter.Core.Text.TextSearch.Normalize(v), StringComparer.Ordinal)
            .Select(v => new FilterOption(v, v))).ToList();

    /// <summary>
    /// Ordre des périodes, de la plus ancienne à la plus récente (indiqué par l'utilisateur, 16/11/2026 ; l'ordre n'est pas dans
    /// les données du jeu). Les noms sont comparés sans accents ni majuscules ; une période inconnue (nouvelle version du jeu)
    /// vient après les autres, par ordre alphabétique.
    /// </summary>
    internal static readonly IReadOnlyList<string> ChronologicalOrder =
        new[] { "aube primordiale", "age des divinites", "grand cataclysme", "malediction des rois", "ere prospere" };

    private static int Rank(string value, IReadOnlyList<string>? order)
    {
        if (order is null) return 0;
        var key = SpamDrafter.Core.Text.TextSearch.Normalize(value);
        for (var i = 0; i < order.Count; i++)
            if (key == order[i] || key.EndsWith(" " + order[i], StringComparison.Ordinal)) return i;
        return order.Count;
    }
    public ObservableCollection<CardItem> Items { get; }

    private CollectionViewModel? _collection;
    /// <summary>Collection du joueur (phase 15) : en mode « possédées », seules ses cartes sont listées.</summary>
    public CollectionViewModel? Collection
    {
        get => _collection;
        set { _collection = value; if (value is not null) value.Changed += Refresh; Refresh(); }
    }
    private int PoolCount => _collection is null ? _all.Count : _all.Count(c => _collection.Shows(c));

    public string Search { get => _search; set { if (Set(ref _search, value)) Refresh(); } }
    /// <summary>Un seul élément (ou « tous ») : raccourci pour le code et les réglages ; l'écran utilise les cases à cocher.</summary>
    public FilterOption Element
    {
        get => _element;
        set { if (value is null) return; _element = value; SetChecks(ElementChoices, value.Id is null ? Array.Empty<string>() : new[] { value.Id }); Raise(); Refresh(); }
    }
    public FilterOption Rarity
    {
        get => _rarity;
        set { if (value is null) return; _rarity = value; SetChecks(RarityChoices, value.Id is null ? Array.Empty<string>() : new[] { value.Id }); Raise(); Refresh(); }
    }
    public FilterOption Species { get => _species; set { if (value is not null && Set(ref _species, value)) Refresh(); } }
    public FilterOption Region { get => _region; set { if (value is not null && Set(ref _region, value)) Refresh(); } }
    public FilterOption Chronology { get => _chronology; set { if (value is not null && Set(ref _chronology, value)) Refresh(); } }
    public FilterOption Sort { get => _sort; set { if (value is not null && Set(ref _sort, value)) Refresh(); } }
    /// <summary>Réduit tout le menu de tri et de filtres pour laisser plus de place aux cartes.</summary>
    private bool _filtersCollapsed = true;   // réduit de base
    public bool FiltersCollapsed { get => _filtersCollapsed; set { if (Set(ref _filtersCollapsed, value)) { Raise(nameof(FiltersVisible)); Raise(nameof(FiltersToggleText)); } } }
    public bool FiltersVisible => !_filtersCollapsed;
    /// <summary>Au moins un filtre (recherche, élément, rareté, lore) est actif.</summary>
    public bool AnyFilterActive => _search.Trim().Length > 0 || Checked(ElementChoices).Count > 0 || Checked(RarityChoices).Count > 0 || HasLoreFilter;
    public string FiltersToggleText => _filtersCollapsed ? "Filtres ▾" : "Filtres ▴";
    public CardItem? Selected { get => _selected; set => Set(ref _selected, value); }
    /// <summary>Retrouve les filtres élément et rareté retenus (un identifiant disparu est ignoré).</summary>
    public void ApplyFilters(BrowserSettings? saved)
    {
        if (saved is null) return;
        SetChecks(ElementChoices, (saved.Element ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Where(id => ElementChoices.Any(c => c.Id == id)));
        SetChecks(RarityChoices, (saved.Rarity ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Where(id => RarityChoices.Any(c => c.Id == id)));
        Species = SpeciesOptions.FirstOrDefault(o => o.Id == saved.Species) ?? SpeciesOptions[0];
        Region = RegionOptions.FirstOrDefault(o => o.Id == saved.Region) ?? RegionOptions[0];
        Chronology = ChronologyOptions.FirstOrDefault(o => o.Id == saved.Chronology) ?? ChronologyOptions[0];
        Sort = SortOptions.FirstOrDefault(o => o.Id == saved.Sort) ?? SortOptions[0];
        SyncLoreKind();
        FiltersCollapsed = saved.FiltersCollapsed ?? true;
    }

    public BrowserSettings CaptureFilters() => new()
    {
        Element = Checked(ElementChoices) is { Count: > 0 } els ? string.Join(",", els) : null,
        Rarity = Checked(RarityChoices) is { Count: > 0 } ras ? string.Join(",", ras) : null, Species = _species.Id, Region = _region.Id, Chronology = _chronology.Id, Sort = _sort.Id, FiltersCollapsed = _filtersCollapsed,
    };

    /// <summary>Remet tous les filtres à zéro (la recherche, l'élément, la rareté, le lore) ; le tri est gardé.</summary>
    public void ClearFilters()
    {
        _search = ""; _element = ElementOptions[0]; _rarity = RarityOptions[0];
        SetChecks(ElementChoices, Array.Empty<string>()); SetChecks(RarityChoices, Array.Empty<string>());
        _species = SpeciesOptions[0]; _region = RegionOptions[0]; _chronology = ChronologyOptions[0];
        foreach (var n in new[] { nameof(Search), nameof(Element), nameof(Rarity), nameof(Species), nameof(Region), nameof(Chronology) }) Raise(n);
        SyncLoreKind();
        Refresh();
    }

    /// <summary>Au moins un filtre de lore est actif (le panneau des filtres avancés l'indique).</summary>
    public bool HasLoreFilter => _species.Id is not null || _region.Id is not null || _chronology.Id is not null;
    public string AdvancedHeader => HasLoreFilter ? "Tri et filtres supplémentaires · filtre actif" : "Tri et filtres supplémentaires";

    public string CountText => $"{Items.Count} / {PoolCount} cartes";

    private void Refresh()
    {
        Items.Clear();
        var els = Checked(ElementChoices); var ras = Checked(RarityChoices);
        var shown = _all.Where(c => (_collection?.Shows(c) ?? true) && (els.Count == 0 || els.Contains(c.Card.Element)) && (ras.Count == 0 || ras.Contains(c.Card.Rarity))
                                    && c.Matches(_search, null, null, _species?.Id, _region?.Id, _chronology?.Id));
        foreach (var c in Sorted(shown)) Items.Add(c);
        Raise(nameof(CountText)); Raise(nameof(HasLoreFilter)); Raise(nameof(AdvancedHeader)); Raise(nameof(LoreSummary)); Raise(nameof(AnyFilterActive)); Raise(nameof(FiltersToggleText));
    }

    /// <summary>Tri choisi. À valeur égale, l'ordre du jeu : le résultat ne dépend jamais du hasard.</summary>
    private IEnumerable<CardItem> Sorted(IEnumerable<CardItem> cards)
    {
        int ElementIndex(CardItem c) => _kb.Elements.Select((e, i) => (e, i)).FirstOrDefault(x => x.e.Id == c.Card.Element).i;
        int RarityIndex(CardItem c) => _kb.Rarities.Select((r, i) => (r, i)).FirstOrDefault(x => x.r.Id == c.Card.Rarity).i;
        return _sort.Id switch
        {
            "name" => cards.OrderBy(c => SpamDrafter.Core.Text.TextSearch.Normalize(c.Name), StringComparer.Ordinal).ThenBy(c => c.Card.Order),
            "hp" => cards.OrderByDescending(c => c.Card.Hp).ThenBy(c => c.Card.Order),
            "income" => cards.OrderByDescending(c => c.Card.GoldIncome).ThenBy(c => c.Card.Order),
            "element" => cards.OrderBy(ElementIndex).ThenBy(c => c.Card.Order),
            "rarity" => cards.OrderByDescending(RarityIndex).ThenBy(c => c.Card.Order),
            _ => cards.OrderBy(c => c.Card.Order),
        };
    }
}
