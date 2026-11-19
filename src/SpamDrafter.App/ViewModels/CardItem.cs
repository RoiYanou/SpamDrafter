using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SpamDrafter.Core.Knowledge;
using SpamDrafter.Core.Text;

namespace SpamDrafter.App.ViewModels;

/// <summary>Une étoile de talent : jaune (possédé) ou grise.</summary>
public sealed record StarItem(Avalonia.Media.IBrush Fill);

/// <summary>Une carte telle qu'affichée (nom, textes traduits, images chargées à la demande).</summary>
public sealed class CardItem : ViewModelBase
{
    private readonly KnowledgeBase _kb;
    private Bitmap? _thumb;
    private bool _thumbRequested;
    private Bitmap? _large;
    private bool _largeRequested;
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
            _thumb = null; _thumbRequested = false; _large = null; _largeRequested = false;
            Raise(nameof(SelectedSkinId)); Raise(nameof(Thumbnail)); Raise(nameof(LargeThumbnail));
            SkinChanged?.Invoke(this);
        }
    }

    /// <summary>Le skin affiché (null = carte de base).</summary>
    public CardSkin? SelectedSkin => Skins.FirstOrDefault(s => s.Id == _skinId);

    /// <summary>Nom, capacités et talents de la carte, normalisés pour la recherche.</summary>
    public string SearchText { get; }

    public Card Card { get; }

    private IReadOnlyList<StarItem> _stars = Array.Empty<StarItem>();
    /// <summary>Étoiles de talents (phase 15) : vide hors du mode « possédés » ; sinon une par talent de la carte, jaune si possédé, grise sinon.</summary>
    public IReadOnlyList<StarItem> Stars => _stars;
    /// <summary>Illustration de base (jamais le skin choisi), pour l'écran d'importation.</summary>
    public Bitmap? LoadBaseImage(int height)
    {
        var path = _kb.ImagePath(Card.Image);
        if (path is null) return null;
        try { using var s = File.OpenRead(path); return Bitmap.DecodeToHeight(s, height); }
        catch (Exception) { return null; }
    }
    private static readonly Avalonia.Media.IBrush StarOn = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFC93C"));
    private static readonly Avalonia.Media.IBrush StarOff = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#6E6E7C"));
    /// <summary>Niveau de talent possédé (0 à 3) pour colorer les étoiles (3 = toutes jaunes, mode « toutes les cartes et talents »).</summary>
    public void SetStars(int? level)
    {
        _stars = level is null ? Array.Empty<StarItem>() : Enumerable.Range(0, Card.Talents.Count).Select(i => new StarItem(i < level ? StarOn : StarOff)).ToList();
        Raise(nameof(Stars));
    }

    private IReadOnlyList<AbilityTag>? _abilityTags;
    /// <summary>
    /// Capacités de la carte pour les bandeaux affichés dessus, comme dans le jeu : nom en majuscules, recharge, et icônes des éléments
    /// qu'elles mettent en jeu (voir <see cref="DescriptionFormatter.AbilityElements"/>).
    /// </summary>
    public IReadOnlyList<AbilityTag> AbilityTags => _abilityTags ??= Card.Abilities.Select(BuildTag).Concat(BuildToxTags()).ToList();

    private AbilityTag BuildTag(string a)
    {
        var (elements, logos) = Icons.AbilityPictures(_kb, a);
        return new AbilityTag(_kb.AbilityName(a).ToUpper(System.Globalization.CultureInfo.GetCultureInfo("fr-FR")),
                              _kb.Descriptions.Stats(a) is { Cooldown: > 0 } st ? st.Cooldown.ToString() : "", elements, logos,
                              Icons.Load(_kb, "images/icons/stats/cooldown.webp", 96));
    }

    /// <summary>Tox de base de la carte, présentés comme des capacités : leur image à gauche, leur nom au centre, pas de recharge.</summary>
    private IEnumerable<AbilityTag> BuildToxTags() => _kb.InnateTox(Card).Select(t =>
        new AbilityTag(_kb.ToxName(t).ToUpper(System.Globalization.CultureInfo.GetCultureInfo("fr-FR")), "", Array.Empty<Bitmap>(),
                       Icons.Load(_kb, t.Image, 64) is { } img ? new[] { img } : Array.Empty<Bitmap>(), null)
        {
            IsTox = true,
            Frame = Icons.RarityBrush(_kb, t.Rarity),   // encadré de la couleur de rareté du Tox
            RarityName = t.Rarity is { } rid ? _kb.RarityName(rid) : "",
            RarityText = Icons.RarityTextBrush(_kb, t.Rarity),
        });

    /// <summary>Icône de l'élément de la carte (null sans icône dans la base).</summary>
    /// <summary>Icône d'élément cerclée de noir, et son masque sans contour (pastille de la carte).</summary>
    public Bitmap? ElementBadge => Icons.ElementBadge(_kb, Card.Element)?.Badge;
    public Bitmap? ElementBadgeMask => Icons.ElementBadge(_kb, Card.Element)?.Mask;
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
    /// <summary>Spamton cerclé de noir, et son masque sans contour (reflet métallique).</summary>
    public Bitmap? SpamtonBadge => Icons.SpamtonBadge(_kb)?.Badge;
    public Bitmap? SpamtonBadgeMask => Icons.SpamtonBadge(_kb)?.Mask;
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

    private static int _pendingLoads;
    /// <summary>Nombre d'images en cours de chargement (le préchargement du démarrage attend qu'il retombe à 0).</summary>
    public static int PendingLoads => Volatile.Read(ref _pendingLoads);

    /// <summary>Lance tout de suite, en arrière-plan, le chargement de la miniature (au démarrage : elle est prête avant le premier clic sur un onglet).</summary>
    public void PreloadThumbnail() { _ = Thumbnail; }

    /// <summary>Miniature décodée en petit (évite de charger 100 Mo d'images en mémoire).</summary>
    public Bitmap? Thumbnail
    {
        get
        {
            if (!_thumbRequested) { _thumbRequested = true; _ = LoadThumbAsync(); }
            return _thumb;
        }
    }

    /// <summary>
    /// Vignette nette pour les grandes tuiles (Bibliothèque, 6 colonnes) : la miniature de 256 px devient floue au-delà de ≈ 200 px à l'écran.
    /// Décodée à 440 px de large depuis l'illustration complète, seulement quand une tuile en a besoin.
    /// </summary>
    public Bitmap? LargeThumbnail
    {
        get
        {
            if (!_largeRequested) { _largeRequested = true; _ = LoadLargeAsync(); }
            return _large;
        }
    }

    private async Task LoadLargeAsync()
    {
        var requestedSkin = _skinId;
        var path = _kb.ImagePath(SelectedSkin?.Image ?? Card.Image);
        if (path is null) return;
        Interlocked.Increment(ref _pendingLoads);
        try
        {
            var bmp = await Task.Run(() =>
            {
                try { using var s = File.OpenRead(path); return Bitmap.DecodeToWidth(s, 440); }
                catch (Exception) { return null; }
            });
            await Dispatcher.UIThread.InvokeAsync(() => { if (_skinId != requestedSkin) return; _large = bmp; Raise(nameof(LargeThumbnail)); });
        }
        finally { Interlocked.Decrement(ref _pendingLoads); }
    }

    private async Task LoadThumbAsync()
    {
        var requestedSkin = _skinId;
        var path = _kb.ImagePath(SelectedSkin?.Thumbnail ?? Card.Thumbnail);
        if (path is null) return;
        Interlocked.Increment(ref _pendingLoads);
        try
        {
            var bmp = await Task.Run(() =>
            {
                try { using var s = File.OpenRead(path); return Bitmap.DecodeToWidth(s, 260); }
                catch (Exception) { return null; }
            });
            await Dispatcher.UIThread.InvokeAsync(() => { if (_skinId != requestedSkin) return; _thumb = bmp; Raise(nameof(Thumbnail)); });
        }
        finally { Interlocked.Decrement(ref _pendingLoads); }
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

/// <summary>Une capacité sur la carte : son nom et sa recharge (vide si elle n'en a pas).</summary>
public sealed record AbilityTag(string Name, string Cooldown, IReadOnlyList<Bitmap> Elements, IReadOnlyList<Bitmap> Logos, Bitmap? CooldownIcon)
{
    public bool HasCooldown => Cooldown.Length > 0;
    /// <summary>Tox de base (et non capacité) : encadré à la couleur de sa rareté, sans fond sombre, logo collé au nom.</summary>
    public bool IsTox { get; init; }
    public bool IsAbility => !IsTox;
    public Avalonia.Media.IBrush? Frame { get; init; }
    /// <summary>Nom de la rareté du Tox (vide pour une capacité) et sa couleur de texte.</summary>
    public string RarityName { get; init; } = "";
    public Avalonia.Media.IBrush? RarityText { get; init; }
    public Avalonia.Media.IBrush Back => IsTox ? Avalonia.Media.Brushes.Transparent : AbilityBack;
    private static readonly Avalonia.Media.IBrush AbilityBack = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#78120A24"));
    public bool HasElements => Elements.Count > 0;
    /// <summary>Logo de critique et/ou de précision, pour une capacité sans élément qui ne touche que ces statistiques (fusionnés en un disque s'il y en a deux).</summary>
    public bool HasLogos => Elements.Count == 0 && Logos.Count > 0;
    public bool HasLogosOfAbility => IsAbility && HasLogos;
}
