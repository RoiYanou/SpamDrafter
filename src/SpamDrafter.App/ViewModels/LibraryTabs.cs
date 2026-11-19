using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SpamDrafter.Core.Knowledge;
using SpamDrafter.Core.Text;

namespace SpamDrafter.App.ViewModels;

/// <summary>Une carte dans une liste d'un sous-onglet (talent, Tox, artiste) : la carte et une précision (« Talent 2 », « épique »…).</summary>
public sealed record CardLink(CardItem Card, string Note, System.Windows.Input.ICommand? Open = null, CardSkin? Skin = null, Bitmap? SkinThumbnail = null)
{
    /// <summary>Vrai pour un skin alternatif : sa miniature remplace celle de la carte de base.</summary>
    public bool IsSkin => Skin is not null;

    /// <summary>Faux quand il n'y a rien à préciser (artistes) : le « · » du gabarit n'est alors pas affiché.</summary>
    public bool HasNote => Note.Length > 0;
}

/// <summary>
/// La Bibliothèque en quatre sous-onglets (phase 12 bis) : Cartes (la fiche détaillée), Talents, Tox et Artistes. Un clic sur une
/// carte dans un sous-onglet l'ouvre dans « Cartes ».
/// </summary>
public sealed class LibraryTabsViewModel : ViewModelBase
{
    private int _selectedTab;

    public LibraryTabsViewModel(LibraryViewModel cards, TalentsViewModel talents, ToxViewModel tox, ArtistsViewModel artists)
    {
        Cards = cards; Talents = talents; Tox = tox; Artists = artists;
        foreach (var open in new Action<Action<CardItem>>[] { h => talents.OpenCardRequested += h, h => tox.OpenCardRequested += h, h => artists.OpenCardRequested += h })
            open(ShowCard);
    }

    /// <summary>Au démarrage de l'application : une ligne au hasard est choisie dans Talents, Tox et Artistes.</summary>
    public void SelectRandomRows()
    {
        if (Talents.Rows.Count > 0) Talents.Selected = Talents.Rows[Random.Shared.Next(Talents.Rows.Count)];
        if (Tox.Rows.Count > 0) Tox.Selected = Tox.Rows[Random.Shared.Next(Tox.Rows.Count)];
        if (Artists.Rows.Count > 0) Artists.Selected = Artists.Rows[Random.Shared.Next(Artists.Rows.Count)];
    }

    public LibraryViewModel Cards { get; }
    public TalentsViewModel Talents { get; }
    public ToxViewModel Tox { get; }
    public ArtistsViewModel Artists { get; }

    /// <summary>0 = Cartes, 1 = Talents, 2 = Tox, 3 = Artistes.</summary>
    public int SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (!Set(ref _selectedTab, value)) return;
            Raise(nameof(IsCardsTab)); Raise(nameof(IsTalentsTab)); Raise(nameof(IsToxTab)); Raise(nameof(IsArtistsTab));
        }
    }
    // Les quatre vues restent construites et en place : changer de sous-onglet n'est qu'un changement de visibilité (pas de reconstruction des 151 cartes).
    public bool IsCardsTab => _selectedTab == 0;
    public bool IsTalentsTab => _selectedTab == 1;
    public bool IsToxTab => _selectedTab == 2;
    public bool IsArtistsTab => _selectedTab == 3;

    /// <summary>Ouvre un talent dans le sous-onglet « Talents » (la recherche est vidée pour qu'il soit dans la liste).</summary>
    public void ShowTalent(string id)
    {
        Talents.Search = "";
        if (Talents.Rows.FirstOrDefault(r => r.Id == id) is { } row) { Talents.Selected = row; SelectedTab = 1; }
    }

    /// <summary>Ouvre un artiste dans le sous-onglet « Artistes » (recherche vidée, tri alphabétique conservé).</summary>
    public void ShowArtist(string id)
    {
        Artists.Search = "";
        if (Artists.Rows.FirstOrDefault(r => r.Id == id) is { } row) { Artists.Selected = row; SelectedTab = 3; }
    }

    /// <summary>Ouvre un Tox (par sa clé de famille) dans le sous-onglet « Tox » (recherche et rareté remises à zéro).</summary>
    public void ShowTox(string key)
    {
        Tox.Search = ""; Tox.RarityIndex = 0;
        if (Tox.Rows.FirstOrDefault(r => r.Key == key) is { } row) { Tox.Selected = row; SelectedTab = 2; }
    }

    /// <summary>
    /// Ouvreur de la fiche de carte de la fenêtre principale (celle du clic droit, par-dessus tout : on ne peut plus changer d'onglet pendant
    /// qu'elle est affichée). Renseigné par l'application ; sans lui (tests), la carte s'ouvre dans le sous-onglet « Cartes ».
    /// </summary>
    public Action<CardItem>? CardOpener { get; set; }

    /// <summary>Ouvre la fiche d'une carte (toujours la même que le clic droit) ; à défaut d'ouvreur, dans le sous-onglet « Cartes ».</summary>
    public void ShowCard(CardItem card)
    {
        if (CardOpener is { } open) { open(card); return; }
        Cards.Browser.ClearFilters();
        Cards.Browser.Selected = card;
        SelectedTab = 0;
    }
}

public sealed record TalentRow(string Id, string Name, int CardCount)
{
    public string Count => CardCount == 1 ? "1 carte" : $"{CardCount} cartes";
}

/// <summary>
/// Sous-onglet « Talents » : les talents que des cartes possèdent, par ordre alphabétique ; en choisir un montre sa description
/// et les cartes qui l'ont.
/// </summary>
public sealed class TalentsViewModel : ViewModelBase
{
    private readonly KnowledgeBase _kb;
    private readonly IReadOnlyList<CardItem> _items;
    private List<TalentRow> _all;
    private string _search = "";
    private TalentRow? _selected;

    public TalentsViewModel(KnowledgeBase kb, IReadOnlyList<CardItem> items)
    {
        _kb = kb; _items = items;
        _all = BuildAll();
        Rows = new ObservableCollection<TalentRow>(_all);
        OpenCardCommand = new RelayCommand(p => { if (p is CardLink l) OpenCardRequested?.Invoke(l.Card); });
    }

    /// <summary>Les talents de chaque carte (le n-ième talent d'une carte n'est montré que si le joueur le possède, en mode « possédés »).</summary>
    private IEnumerable<(CardItem Card, string Talent)> Pairs() =>
        _items.Where(i => _collection?.Shows(i) ?? true)
              .SelectMany(i => (_collection?.AllowedTalents(i) ?? i.Card.Talents).Select(t => (i, t)));

    private List<TalentRow> BuildAll() => Pairs().GroupBy(p => p.Talent)
        .Select(g => new TalentRow(g.Key, _kb.TalentName(g.Key), g.Count()))
        .OrderBy(t => TextSearch.Normalize(t.Name), StringComparer.Ordinal).ThenBy(t => t.Id, StringComparer.Ordinal).ToList();

    private CollectionViewModel? _collection;
    /// <summary>Collection du joueur (phase 15) : en mode « possédés », seuls les talents possédés sont listés.</summary>
    public CollectionViewModel? Collection
    {
        get => _collection;
        set
        {
            _collection = value;
            if (value is null) return;
            value.Changed += () =>
            {
                var keep = _selected?.Id;
                _all = BuildAll(); ApplySearch();
                _selected = keep is null ? null : Rows.FirstOrDefault(r => r.Id == keep);
                Raise(nameof(Selected)); Raise(nameof(HasSelection));
                if (_selected is not null) UpdateDetail();
            };
            _all = BuildAll(); ApplySearch();
        }
    }

    private void ApplySearch()
    {
        Rows.Clear();
        foreach (var t in _all.Where(t => TextSearch.ContainsAllWords(TextSearch.Normalize(t.Name), _search))) Rows.Add(t);
        Raise(nameof(CountText));
    }

    public ObservableCollection<TalentRow> Rows { get; }
    public RelayCommand OpenCardCommand { get; }
    public event Action<CardItem>? OpenCardRequested;

    public string Search
    {
        get => _search;
        set
        {
            if (!Set(ref _search, value)) return;
            ApplySearch();
        }
    }

    public string CountText => Rows.Count == _all.Count ? $"{_all.Count} talents" : $"{Rows.Count} / {_all.Count} talents";

    public TalentRow? Selected
    {
        get => _selected;
        set { if (Set(ref _selected, value)) UpdateDetail(); }
    }

    public bool HasSelection => _selected is not null;
    public string Title { get; private set; } = "Choisis un talent dans la liste";
    public string Description { get; private set; } = "";
    public string CardsTitle { get; private set; } = "";
    public IReadOnlyList<CardLink> Cards { get; private set; } = Array.Empty<CardLink>();

    private void UpdateDetail()
    {
        if (_selected is null) return;
        Title = _selected.Name;
        var d = _kb.Descriptions.Talent(_selected.Id);
        Description = d.Length > 0 ? d : "(pas de description dans les données du jeu)";
        Cards = Pairs().Where(p => p.Talent == _selected.Id).Select(p => p.Card)
            .OrderBy(i => TextSearch.Normalize(i.Name), StringComparer.Ordinal)
            .Select(i => new CardLink(i, $"Talent {i.Card.Talents.ToList().IndexOf(_selected.Id) + 1}", OpenCardCommand)).ToList();
        CardsTitle = Cards.Count == 1 ? "Carte qui possède ce talent" : $"Cartes qui possèdent ce talent ({Cards.Count})";
        foreach (var n in new[] { nameof(HasSelection), nameof(Title), nameof(Description), nameof(CardsTitle), nameof(Cards) }) Raise(n);
    }
}

// ================= Tox =================

public sealed record ToxFamilyRow(string Key, string Name, Bitmap? Image, int Variants, IBrush? Frame = null);

/// <summary>Un Tox d'une famille pour une rareté : valeur, prix à la boutique, description.</summary>
public sealed record ToxVariantRow(string RarityName, Bitmap? RarityIcon, IBrush Border, IBrush TextColor, bool IsRated, string Value, string Price,
    bool InShop, string Description, string Innate);

/// <summary>
/// Sous-onglet « Tox » : les Tox du jeu par ordre alphabétique ; en choisir un montre, pour chaque rareté où il existe, sa valeur,
/// son prix à la boutique et sa description, et les cartes qui le possèdent de base.
/// </summary>
public sealed class ToxViewModel : ViewModelBase
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private readonly KnowledgeBase _kb;
    private readonly IReadOnlyList<CardItem> _items;
    private readonly List<ToxFamilyRow> _all;
    private readonly Dictionary<string, List<ToxEntry>> _byKey;
    private string _search = "";
    private ToxFamilyRow? _selected;
    private readonly List<string> _rarityIds;
    private int _rarityIndex;

    public ToxViewModel(KnowledgeBase kb, IReadOnlyList<CardItem> items)
    {
        _kb = kb; _items = items;
        // Les passifs de talent et les Tox exclus ne sont pas listés (voir ToxCatalog).
        _byKey = kb.AllTox.Where(ToxCatalog.IsListed).GroupBy(DescriptionFormatter.ToxKey).ToDictionary(g => g.Key, g => g.ToList());
        // Deux Tox différents du jeu peuvent porter le même nom (« Camouflage ») : on ajoute alors leur identifiant pour les distinguer.
        var names = _byKey.ToDictionary(kv => kv.Key, kv => kb.ToxName(kv.Value[0]));
        var duplicated = names.GroupBy(n => TextSearch.Normalize(n.Value)).Where(g => g.Count() > 1).SelectMany(g => g.Select(n => n.Key)).ToHashSet();
        _all = _byKey.Select(kv =>
            {
                var first = kv.Value.FirstOrDefault(t => t.Image is not null) ?? kv.Value[0];
                var name = duplicated.Contains(kv.Key) ? $"{names[kv.Key]} ({kv.Key})" : names[kv.Key];
                return new ToxFamilyRow(kv.Key, name, Icons.Tox(kb, first, 64), kv.Value.Count, FrameFor(kb, kv.Value));
            })
            .OrderBy(f => TextSearch.Normalize(f.Name), StringComparer.Ordinal).ThenBy(f => f.Key, StringComparer.Ordinal).ToList();
        Rows = new ObservableCollection<ToxFamilyRow>(_all);
        // Raretés où existe au moins un Tox ; l'entrée 0 = « Toutes les raretés ».
        var present = _byKey.Values.SelectMany(v => v).Select(t => t.Rarity).Where(r => r is not null).Select(r => r!).ToHashSet();
        _rarityIds = kb.Rarities.Where(r => present.Contains(r.Id)).Select(r => r.Id).ToList();
        RarityOptions = new[] { "Toutes les raretés" }.Concat(_rarityIds.Select(kb.RarityName)).ToList();
        OpenCardCommand = new RelayCommand(p => { if (p is CardLink l) OpenCardRequested?.Invoke(l.Card); });
    }

    /// <summary>Cadre multicolore d'un Tox : un dégradé des couleurs des raretés où il existe (une seule couleur s'il n'a qu'une rareté).</summary>
    private static IBrush FrameFor(KnowledgeBase kb, IEnumerable<ToxEntry> family)
    {
        var colours = family.Select(t => t.Rarity).Distinct().Select(r => (kb.Rarity(r ?? "")?.Order ?? int.MaxValue, r))
            .OrderBy(x => x.Item1).Select(x => ((SolidColorBrush)Icons.FrameBrush(kb, x.r)).Color).ToList();
        if (colours.Count == 0) colours.Add(Color.Parse("#666666"));
        if (colours.Count == 1) return new SolidColorBrush(colours[0]);
        var g = new LinearGradientBrush { StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative), EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative) };
        for (var i = 0; i < colours.Count; i++) g.GradientStops.Add(new GradientStop(colours[i], i / (double)(colours.Count - 1)));
        return g;
    }

    public ObservableCollection<ToxFamilyRow> Rows { get; }
    public RelayCommand OpenCardCommand { get; }
    public event Action<CardItem>? OpenCardRequested;

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value)) Refresh();
        }
    }

    public IReadOnlyList<string> RarityOptions { get; }

    /// <summary>0 = toutes les raretés ; sinon, seuls les Tox qui existent dans la rareté choisie sont listés.</summary>
    public int RarityIndex
    {
        get => _rarityIndex;
        set { if (Set(ref _rarityIndex, value)) Refresh(); }
    }

    private void Refresh()
    {
        var rarity = _rarityIndex > 0 && _rarityIndex <= _rarityIds.Count ? _rarityIds[_rarityIndex - 1] : null;
        Rows.Clear();
        foreach (var f in _all.Where(f => TextSearch.ContainsAllWords(TextSearch.Normalize(f.Name), _search)
                                          && (rarity is null || _byKey[f.Key].Any(t => t.Rarity == rarity))))
            Rows.Add(f);
        Raise(nameof(CountText));
    }

    public string CountText => Rows.Count == _all.Count ? $"{_all.Count} Tox" : $"{Rows.Count} / {_all.Count} Tox";

    public ToxFamilyRow? Selected
    {
        get => _selected;
        set { if (Set(ref _selected, value)) UpdateDetail(); }
    }

    public bool HasSelection => _selected is not null;
    public string Title { get; private set; } = "Choisis un Tox dans la liste";
    public Bitmap? Image { get; private set; }
    public IBrush? Frame => _selected?.Frame;
    public IReadOnlyList<ToxVariantRow> Variants { get; private set; } = Array.Empty<ToxVariantRow>();
    public string CardsTitle { get; private set; } = "";
    public IReadOnlyList<CardLink> InnateCards { get; private set; } = Array.Empty<CardLink>();

    private void UpdateDetail()
    {
        if (_selected is null) return;
        var family = _byKey[_selected.Key];
        Title = _selected.Name;
        Image = family.Select(t => Icons.Tox(_kb, t, 128)).FirstOrDefault(i => i is not null);
        var rarityOrder = _kb.Rarities.Select((r, i) => (r.Id, i)).ToDictionary(x => x.Id, x => x.i);
        Variants = family
            .OrderBy(t => t.Rarity is { } r && rarityOrder.TryGetValue(r, out var o) ? o : int.MaxValue).ThenBy(t => t.Title, StringComparer.Ordinal)
            .Select(Row).ToList();
        var uuids = family.ToDictionary(t => t.Uuid);
        InnateCards = _items.Where(i => (i.Card.InnatePassives ?? Array.Empty<string>()).Any(uuids.ContainsKey))
            .OrderBy(i => TextSearch.Normalize(i.Name), StringComparer.Ordinal)
            .Select(i => new CardLink(i, string.Join(", ", i.Card.InnatePassives!.Where(uuids.ContainsKey).Select(u => RarityLabel(uuids[u]))), OpenCardCommand))
            .ToList();
        CardsTitle = InnateCards.Count == 0 ? "Aucune carte ne le possède de base"
            : InnateCards.Count == 1 ? "Carte qui le possède de base" : $"Cartes qui le possèdent de base ({InnateCards.Count})";
        foreach (var n in new[] { nameof(HasSelection), nameof(Title), nameof(Image), nameof(Frame), nameof(Variants), nameof(CardsTitle), nameof(InnateCards) }) Raise(n);
    }

    private string RarityLabel(ToxEntry t) => t.Rarity is { } r ? _kb.RarityName(r) : "sans rareté";

    private ToxVariantRow Row(ToxEntry t)
    {
        var description = _kb.Descriptions.Tox(t);
        return new ToxVariantRow(
            RarityLabel(t), Icons.Rarity(_kb, t.Rarity, 32), Icons.RarityBrush(_kb, t.Rarity), Icons.RarityTextBrush(_kb, t.Rarity), t.Rarity is not null,
            $"Valeur : {t.Value.ToString("0.##", Fr)}",
            ToxCatalog.ShopPrice(t) is { } price ? $"Prix en boutique : {ToxCatalog.PriceText(price)}" : "Pas vendu en boutique",
            ToxCatalog.ShopPrice(t) is not null,
            description.Length > 0 ? description : "(pas de description dans les données du jeu)",
            t.InnateOnCards switch { 0 => "", 1 => "Possédé de base par 1 carte", var n => $"Possédé de base par {n} cartes" });
    }
}

// ================= Artistes =================

public sealed record ArtistRow(string Id, string Name, int CardCount, int SkinCount = 0)
{
    /// <summary>« 3 cartes », « 2 cartes + 1 skin », « 2 skins » (les skins alternatifs sont comptés à part).</summary>
    public string Count
    {
        get
        {
            var cards = CardCount == 0 ? "" : CardCount == 1 ? "1 carte" : $"{CardCount} cartes";
            var skins = SkinCount == 0 ? "" : SkinCount == 1 ? "1 skin" : $"{SkinCount} skins";
            return cards.Length > 0 && skins.Length > 0 ? $"{cards} + {skins}" : cards + skins;
        }
    }
}

/// <summary>Sous-onglet « Artistes » : les artistes des illustrations (cartes de base et skins alternatifs), triés par ordre alphabétique ou par nombre de cartes, et leurs cartes.</summary>
public sealed class ArtistsViewModel : ViewModelBase
{
    private readonly KnowledgeBase _kb;
    private readonly IReadOnlyList<CardItem> _items;
    private readonly List<ArtistRow> _all;
    private string _search = "";
    private ArtistRow? _selected;
    private int _sortIndex;

    public ArtistsViewModel(KnowledgeBase kb, IReadOnlyList<CardItem> items)
    {
        _kb = kb; _items = items;
        // Seuls les artistes qui ont au moins une carte jouable ou un skin alternatif sont listés (demandes de l'utilisateur, 17/11/2026).
        _all = kb.Artists.Select(a => new ArtistRow(a.Id, a.Name, items.Count(i => i.Card.Artist == a.Id),
                items.Sum(i => i.Skins.Count(s => s.Artist == a.Id))))
            .Where(a => a.CardCount + a.SkinCount > 0).ToList();
        Rows = new ObservableCollection<ArtistRow>();
        Refresh();
        OpenCardCommand = new RelayCommand(p => { if (p is CardLink l) OpenCardRequested?.Invoke(l.Card); });
        OpenLinkCommand = new RelayCommand(_ => OpenLink(), _ => HasLink);
    }

    public ObservableCollection<ArtistRow> Rows { get; }
    public RelayCommand OpenCardCommand { get; }
    public RelayCommand OpenLinkCommand { get; }
    public event Action<CardItem>? OpenCardRequested;

    /// <summary>Ouvre une adresse dans le navigateur (remplaçable pour les essais) ; faux si elle n'a pas pu être ouverte.</summary>
    public Func<string, bool> LinkLauncher { get; set; } = url =>
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); return true; }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    };

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value)) Refresh();
        }
    }

    public IReadOnlyList<string> SortOptions { get; } = new[] { "Ordre alphabétique", "Nombre de cartes" };

    /// <summary>0 = ordre alphabétique, 1 = nombre de cartes (le plus grand d'abord, puis alphabétique).</summary>
    public int SortIndex
    {
        get => _sortIndex;
        set { if (Set(ref _sortIndex, value)) Refresh(); }
    }

    private void Refresh()
    {
        var found = _all.Where(a => TextSearch.ContainsAllWords(TextSearch.Normalize(a.Name), _search));
        var byName = found.OrderBy(a => TextSearch.Normalize(a.Name), StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal);
        var sorted = _sortIndex == 1
            ? found.OrderByDescending(a => a.CardCount).ThenByDescending(a => a.SkinCount).ThenBy(a => TextSearch.Normalize(a.Name), StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal)
            : byName;
        Rows.Clear();
        foreach (var a in sorted) Rows.Add(a);
        Raise(nameof(CountText));
    }

    public string CountText => Rows.Count == _all.Count ? $"{_all.Count} artistes" : $"{Rows.Count} / {_all.Count} artistes";

    public ArtistRow? Selected
    {
        get => _selected;
        set { if (Set(ref _selected, value)) UpdateDetail(); }
    }

    public bool HasSelection => _selected is not null;
    public string Title { get; private set; } = "Choisis un artiste dans la liste";
    public string Link { get; private set; } = "";
    /// <summary>Seuls les liens web (http, https) s'ouvrent depuis l'application.</summary>
    public bool HasLink { get; private set; }
    public string Message { get; private set; } = "";
    public string CardsTitle { get; private set; } = "";
    public IReadOnlyList<CardLink> Cards { get; private set; } = Array.Empty<CardLink>();

    private void UpdateDetail()
    {
        if (_selected is null) return;
        var artist = _kb.Artist(_selected.Id);
        Title = _selected.Name;
        Link = artist?.Link ?? "";
        HasLink = Uri.TryCreate(Link, UriKind.Absolute, out var u) && u.Scheme is "https" or "http";
        Message = "";
        var links = _items.Where(i => i.Card.Artist == _selected.Id)
            .Select(i => new CardLink(i, "", OpenCardCommand))
            .Concat(_items.SelectMany(i => i.Skins.Where(s => s.Artist == _selected.Id).Select(s => new CardLink(i, "Skin alternatif", OpenCardCommand, s, SkinThumb(s)))));
        // Par ordre alphabétique ; une carte de base avant ses skins.
        Cards = links.OrderBy(l => TextSearch.Normalize(l.Card.Name), StringComparer.Ordinal).ThenBy(l => l.IsSkin).ThenBy(l => l.Skin?.Id, StringComparer.Ordinal).ToList();
        CardsTitle = Cards.Count == 1 ? (Cards[0].IsSkin ? "Skin illustré" : "Carte illustrée")
            : Cards.Any(l => l.IsSkin) ? $"Cartes et skins illustrés ({Cards.Count})" : $"Cartes illustrées ({Cards.Count})";
        OpenLinkCommand.RaiseCanExecuteChanged();
        foreach (var n in new[] { nameof(HasSelection), nameof(Title), nameof(Link), nameof(HasLink), nameof(Message), nameof(CardsTitle), nameof(Cards) }) Raise(n);
    }

    private Bitmap? SkinThumb(CardSkin skin)
    {
        var path = _kb.ImagePath(skin.Thumbnail);
        if (path is null) return null;
        try { using var s = File.OpenRead(path); return Bitmap.DecodeToWidth(s, 260); }
        catch (Exception) { return null; }
    }

    private void OpenLink()
    {
        if (!HasLink) return;
        Message = LinkLauncher(Link) ? "" : "Impossible d'ouvrir ce lien.";
        Raise(nameof(Message));
    }
}
