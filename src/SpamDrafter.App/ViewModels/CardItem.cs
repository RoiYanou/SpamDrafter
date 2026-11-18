using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SpamDrafter.Core.Knowledge;
using SpamDrafter.Core.Text;

namespace SpamDrafter.App.ViewModels;

/// <summary>Une carte telle qu'affichée (nom, textes traduits, images chargées à la demande).</summary>
public sealed class CardItem : ViewModelBase
{
    private readonly KnowledgeBase _kb;
    private Bitmap? _thumb;
    private bool _thumbRequested;
    private string? _skinId;

    public CardItem(Card card, KnowledgeBase kb)
    {
        Card = card; _kb = kb;
        Name = kb.CardName(card);
        ElementName = kb.ElementName(card.Element);
        RarityName = kb.RarityName(card.Rarity);
        Lore = kb.Lore(card);
        // Texte de recherche : nom, capacités actives et talents, sans accents (phase 12, lot A).
        SearchText = TextSearch.Normalize(string.Join(' ',
            new[] { Name }.Concat(card.Abilities.Select(kb.AbilityName)).Concat(card.Talents.Select(kb.TalentName))));
    }

    /// <summary>Les skins alternatifs de la carte (vide si elle n'en a pas).</summary>
    public IReadOnlyList<CardSkin> Skins => Card.Skins ?? Array.Empty<CardSkin>();

    /// <summary>Déclenché quand l'affichage par défaut de la carte change (pour l'enregistrer).</summary>
    public event Action<CardItem>? SkinChanged;

    /// <summary>Identifiant du skin affiché par défaut ; null = la carte de base. Un identifiant inconnu revient à la carte de base.</summary>
    public string? SelectedSkinId
    {
        get => _skinId;
        set
        {
            if (value is not null && Skins.All(s => s.Id != value)) value = null;
            if (_skinId == value) return;
            _skinId = value;
            _thumb = null; _thumbRequested = false;
            Raise(nameof(SelectedSkinId)); Raise(nameof(Thumbnail));
            SkinChanged?.Invoke(this);
        }
    }

    /// <summary>Le skin affiché (null = carte de base).</summary>
    public CardSkin? SelectedSkin => Skins.FirstOrDefault(s => s.Id == _skinId);

    /// <summary>Nom, capacités et talents de la carte, normalisés pour la recherche.</summary>
    public string SearchText { get; }

    public Card Card { get; }

    /// <summary>Icône de l'élément de la carte (null sans icône dans la base).</summary>
    public Bitmap? ElementIcon => Icons.Element(_kb, Card.Element, 32);
    /// <summary>Icône de la rareté de la carte.</summary>
    public Bitmap? RarityIcon => Icons.Rarity(_kb, Card.Rarity, 128);
    /// <summary>Couleur du cadre de la carte : celle de sa rareté dans le jeu (phase 13).</summary>
    public Avalonia.Media.IBrush RarityBrush => Icons.FrameBrush(_kb, Card.Rarity);   // couleur de la rareté, éclaircie ; commune : le gris du jeu (#333) est invisible sur le fond violet, on prend son gris clair
    private int RarityOrder => _kb.Rarity(Card.Rarity)?.Order ?? 1;
    /// <summary>Épaisseur du cadre : de 1,5 (commune) à 3 (légendaire, divine) ; plus la rareté est haute, plus il est épais.</summary>
    public Avalonia.Thickness FrameThickness => new(Math.Min(3, 1.5 + 0.5 * (RarityOrder - 1)));
    /// <summary>Raretés légendaire et divine : un reflet passe sur le cadre.</summary>
    public bool IsShiny => RarityOrder >= 4;

    /// <summary>Icône du Spamton et revenu de la carte (affichés en haut à droite).</summary>
    public Bitmap? SpamtonIcon => Icons.Spamton(_kb);
    public string IncomeText => Card.GoldIncome.ToString();
    /// <summary>Illustration de bonne qualité (560 px de haut), gardée en mémoire : decks agrandis de l'accueil. Refaite si le skin change.</summary>
    public Bitmap? MediumImage
    {
        get
        {
            if (_medium is null || _mediumSkin != _skinId) { _medium = LoadFullImage(560); _mediumSkin = _skinId; }
            return _medium;
        }
    }
    private Bitmap? _medium; private string? _mediumSkin;

    /// <summary>Illustration complète décodée à la hauteur demandée (carte du jour : excellente qualité).</summary>
    public Bitmap? LoadFullImage(int height)
    {
        var path = _kb.ImagePath(SelectedSkin?.Image ?? Card.Image);
        if (path is null) return null;
        try { using var s = File.OpenRead(path); return Bitmap.DecodeToHeight(s, height); }
        catch (Exception) { return null; }
    }

    /// <summary>Raretés légendaire et divine (épaisseur du cadre maximale).</summary>
    public bool IsTopRarity => RarityOrder >= 4;

    /// <summary>« 500 PV » : la fin de la ligne de résumé, à côté des icônes.</summary>
    public string HpText => $"{Card.Hp} PV";
    /// <summary>Espèce, région, chronologie et récit de la carte (null sans lore).</summary>
    public CardLore? Lore { get; }
    public string Name { get; }
    public string ElementName { get; }
    public string RarityName { get; }
    public string Summary => $"{ElementName} · {RarityName} · {Card.Hp} PV";

    /// <summary>Miniature décodée en petit (évite de charger 100 Mo d'images en mémoire).</summary>
    public Bitmap? Thumbnail
    {
        get
        {
            if (!_thumbRequested) { _thumbRequested = true; _ = LoadThumbAsync(); }
            return _thumb;
        }
    }

    private async Task LoadThumbAsync()
    {
        var requestedSkin = _skinId;
        var path = _kb.ImagePath(SelectedSkin?.Thumbnail ?? Card.Thumbnail);
        if (path is null) return;
        var bmp = await Task.Run(() =>
        {
            try { using var s = File.OpenRead(path); return Bitmap.DecodeToWidth(s, 260); }
            catch (Exception) { return null; }
        });
        await Dispatcher.UIThread.InvokeAsync(() => { if (_skinId != requestedSkin) return; _thumb = bmp; Raise(nameof(Thumbnail)); });
    }

    /// <summary>Illustration complète, décodée à une hauteur raisonnable pour la fiche détail.</summary>
    public Bitmap? LoadFullImage() => LoadFullImage(SelectedSkin);

    /// <summary>Illustration complète d'un skin donné (null = la carte de base).</summary>
    public Bitmap? LoadFullImage(CardSkin? skin)
    {
        var path = _kb.ImagePath(skin?.Image ?? Card.Image);
        if (path is null) return null;
        try { using var s = File.OpenRead(path); return Bitmap.DecodeToHeight(s, 640); }
        catch (Exception) { return null; }
    }

    public bool Matches(string text, string? element, string? rarity) => Matches(text, element, rarity, null, null, null);

    /// <summary>Filtres espèce, région et chronologie : le texte du jeu (« Créature », « Aelther »…), null = pas de filtre.</summary>
    public bool Matches(string text, string? element, string? rarity, string? species, string? region, string? chronology) =>
        (element is null || Card.Element == element) &&
        (rarity is null || Card.Rarity == rarity) &&
        (species is null || Lore?.Species == species) &&
        (region is null || Lore?.Region == region) &&
        (chronology is null || Lore?.Chronology == chronology) &&
        TextSearch.ContainsAllWords(SearchText, text);
}
