using System.Collections.ObjectModel;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.App.ViewModels;

public sealed record FilterOption(string? Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Liste de cartes filtrable (recherche, élément, rareté), partagée par la Bibliothèque et le Deck builder.</summary>
public sealed class CardBrowser : ViewModelBase
{
    private readonly IReadOnlyList<CardItem> _all;
    private string _search = "";
    private FilterOption _element;
    private FilterOption _rarity;
    private CardItem? _selected;

    public CardBrowser(IReadOnlyList<CardItem> all, KnowledgeBase kb)
    {
        _all = all;
        ElementOptions = new[] { new FilterOption(null, "Tous les éléments") }
            .Concat(kb.Elements.Select(e => new FilterOption(e.Id, kb.ElementName(e.Id)))).ToList();
        RarityOptions = new[] { new FilterOption(null, "Toutes les raretés") }
            .Concat(kb.Rarities.Select(r => new FilterOption(r.Id, kb.RarityName(r.Id)))).ToList();
        _element = ElementOptions[0];
        _rarity = RarityOptions[0];
        Items = new ObservableCollection<CardItem>(all);
    }

    public IReadOnlyList<FilterOption> ElementOptions { get; }
    public IReadOnlyList<FilterOption> RarityOptions { get; }
    public ObservableCollection<CardItem> Items { get; }

    public string Search { get => _search; set { if (Set(ref _search, value)) Refresh(); } }
    public FilterOption Element { get => _element; set { if (Set(ref _element, value)) Refresh(); } }
    public FilterOption Rarity { get => _rarity; set { if (Set(ref _rarity, value)) Refresh(); } }
    public CardItem? Selected { get => _selected; set => Set(ref _selected, value); }
    public string CountText => $"{Items.Count} / {_all.Count} cartes";

    private void Refresh()
    {
        Items.Clear();
        foreach (var c in _all.Where(c => c.Matches(_search, _element?.Id, _rarity?.Id))) Items.Add(c);
        Raise(nameof(CountText));
    }
}
