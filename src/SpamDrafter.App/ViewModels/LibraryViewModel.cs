using Avalonia.Media;
using Avalonia.Media.Imaging;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.App.ViewModels;

/// <summary>Une résistance : icône, nom de l'élément (dans sa couleur) et valeur.</summary>
public sealed record ResistanceItem(Bitmap? Icon, string Name, string Value, IBrush Brush)
{
    public string Text => $"{Name} {Value}";
    /// <summary>Valeur affichée sous l'icône : « 5% ».</summary>
    public string Percent => Value.Replace(" %", "%");
}

public sealed record TalentLine(string Name, string Description);

/// <summary>Une capacité de la carte et sa description chiffrée.</summary>
/// <summary>
/// Une capacité de la fiche, présentée comme dans le jeu : bouton à la couleur de l'élément, recharge entourée, et à droite
/// précision / chance de critique / multiplicateur de critique avec leurs icônes.
/// </summary>
public sealed record AbilityLine(string Name, string Description, string Stats = "")
{
    /// <summary>Description colorée : dégâts avec l'icône et la couleur de leur élément, effets en couleur.</summary>
    public IReadOnlyList<RichSegment> Rich { get; init; } = Array.Empty<RichSegment>();
    public bool HasStats { get; init; }
    public bool HasCooldown { get; init; }
    public string CooldownText { get; init; } = "";
    public string PrecisionText { get; init; } = "";
    public string CritChanceText { get; init; } = "";
    public string CritRatioText { get; init; } = "";
    public bool CanCrit { get; init; }
    public bool NoCrit => HasStats && !CanCrit;
    public Bitmap? CooldownIcon { get; init; }
    public Bitmap? PrecisionIcon { get; init; }
    public Bitmap? CritIcon { get; init; }
    public Bitmap? RatioIcon { get; init; }
}

/// <summary>Un Tox possédé de base : image dans un cadre de la couleur de sa rareté, nom, rareté et description.</summary>
public sealed record ToxLine(string Name, string RarityName, string Description, Avalonia.Media.Imaging.Bitmap? Image,
    Avalonia.Media.IBrush Border, Avalonia.Media.IBrush TextColor, bool HasImage);

/// <summary>Un deck de l'utilisateur qui contient la carte.</summary>
public sealed record DeckLine(Deck Deck, string Label);

/// <summary>Un affichage possible d'une carte : la carte de base (Skin null) ou l'un de ses skins alternatifs.</summary>
public sealed class SkinOption : ViewModelBase
{
    private bool _isSelected;
    public SkinOption(CardSkin? skin, string label, string? artistId = null) { Skin = skin; Label = label; ArtistId = artistId; }
    public CardSkin? Skin { get; }
    public string Label { get; }
    public string? ArtistId { get; }
    /// <summary>Vrai pour l'affichage en cours : son bouton est jaune.</summary>
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

/// <summary>
/// Onglet Bibliothèque : liste filtrable + fiche détaillée de la carte choisie (phase 12, lot C) : lore, capacités et
/// talents avec leurs descriptions chiffrées, mes decks qui l'utilisent, puis le lore à la fin.
/// </summary>
public sealed class LibraryViewModel : ViewModelBase
{
    private readonly KnowledgeBase _kb;
    private readonly DeckLibrary? _decks;
    private readonly Dictionary<string, CardItem> _byId;
    private Bitmap? _image;

    public LibraryViewModel(CardBrowser browser, KnowledgeBase kb, DeckLibrary? decks = null, IReadOnlyList<CardItem>? items = null)
    {
        Browser = browser; _kb = kb; _decks = decks;
        _byId = (items ?? browser.Items).ToDictionary(i => i.Card.Id);
        OpenDeckCommand = new RelayCommand(p => { if (p is DeckLine l) OpenDeckRequested?.Invoke(l.Deck); });
        browser.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CardBrowser.Selected)) { UpdateDetail(); Raise(nameof(HasOverlay)); }
        };
        if (decks is not null) decks.Changed += (_, _) => UpdateDecksUsing();
    }

    public CardBrowser Browser { get; }

    /// <summary>Vrai quand une carte est choisie : sa fiche s'affiche par-dessus la grille.</summary>
    public bool HasOverlay => Browser.Selected is not null;
    /// <summary>Ferme la fiche (touche Échap, bouton ✕) : la grille réapparaît.</summary>
    public RelayCommand CloseOverlayCommand => _closeOverlay ??= new RelayCommand(_ => Browser.Selected = null);
    private RelayCommand? _closeOverlay;

    public RelayCommand OpenDeckCommand { get; }
    /// <summary>Demande d'ouverture d'un deck dans le Deck builder.</summary>
    public event Action<Deck>? OpenDeckRequested;
    private string? _artistId;
    /// <summary>Clic sur le nom de l'illustrateur : demande l'ouverture de cet artiste dans l'onglet Artistes.</summary>
    public event Action<string>? OpenArtistRequested;
    public RelayCommand OpenArtistCommand => _openArtist ??= new RelayCommand(_ => { if (_artistId is { } id) OpenArtistRequested?.Invoke(id); });
    private RelayCommand? _openArtist;

    public Bitmap? Image { get => _image; private set => Set(ref _image, value); }

    private IReadOnlyList<SkinOption> _skinOptions = Array.Empty<SkinOption>();
    private SkinOption? _selectedSkinOption;
    private bool _updatingSkins;

    /// <summary>Les affichages de la carte choisie : « Carte de base » puis ses skins alternatifs (une seule entrée = pas de choix à montrer).</summary>
    public IReadOnlyList<SkinOption> SkinOptions { get => _skinOptions; private set { _skinOptions = value; Raise(nameof(SkinOptions)); Raise(nameof(HasSkins)); } }
    public bool HasSkins => _skinOptions.Count > 1;

    /// <summary>L'affichage choisi : le choisir change l'illustration et devient l'affichage par défaut de la carte (listes comprises).</summary>
    public SkinOption? SelectedSkinOption
    {
        get => _selectedSkinOption;
        set
        {
            if (_updatingSkins || value is null || ReferenceEquals(_selectedSkinOption, value)) return;
            _selectedSkinOption = value;
            foreach (var o in _skinOptions) o.IsSelected = ReferenceEquals(o, value);
            Raise(nameof(SelectedSkinOption));
            if (Browser.Selected is not { } item) return;
            item.SelectedSkinId = value.Skin?.Id;
            _ = SwitchSkinAsync(item, value);
        }
    }

    // Illustrations déjà décodées (carte + skin) : le changement d'affichage ne décode plus rien sur le fil de l'interface.
    private readonly Dictionary<string, Avalonia.Media.Imaging.Bitmap?> _fullImages = new();
    private static string ImageKey(CardItem item, CardSkin? skin) => item.Card.Id + "|" + (skin?.Id ?? "");

    private async Task<Avalonia.Media.Imaging.Bitmap?> FullImageAsync(CardItem item, CardSkin? skin)
    {
        var key = ImageKey(item, skin);
        if (_fullImages.TryGetValue(key, out var cached)) return cached;
        var bmp = await Task.Run(() => item.LoadFullImage(skin));
        _fullImages[key] = bmp;
        return bmp;
    }

    /// <summary>Après l'ouverture d'une fiche, décode en arrière-plan les autres affichages de la carte : leur sélection devient instantanée.</summary>
    private async Task PreloadSkinsAsync(CardItem item)
    {
        try
        {
            await FullImageAsync(item, null);
            foreach (var s in item.Skins) await FullImageAsync(item, s);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException) { }
    }

    /// <summary>
    /// Change d'affichage sans à-coup : l'image est prête (déjà décodée, sinon décodée hors du fil de l'interface) AVANT de lancer le retournement,
    /// qui démarre à l'image suivante, une fois la nouvelle illustration en place.
    /// </summary>
    private async Task SwitchSkinAsync(CardItem item, SkinOption option)
    {
        var bmp = await FullImageAsync(item, option.Skin);
        if (!ReferenceEquals(_selectedSkinOption, option) || !ReferenceEquals(Browser.Selected, item)) return;   // le joueur a déjà changé d'avis
        ShowIllustration(item, option, bmp);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => SkinFlipped?.Invoke(), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Choisit un affichage depuis les boutons « Carte de base » / « Skin » de la fiche.</summary>
    public RelayCommand SelectSkinCommand => _selectSkin ??= new RelayCommand(p => { if (p is SkinOption o) SelectedSkinOption = o; });
    private RelayCommand? _selectSkin;
    /// <summary>Déclenché quand l'affichage change : la vue retourne la carte.</summary>
    public event Action? SkinFlipped;

    /// <summary>Cadre de la grande carte : couleur et épaisseur de sa rareté.</summary>
    public Avalonia.Media.IBrush FrameBrush { get; private set; } = Avalonia.Media.Brushes.Gray;
    public Avalonia.Thickness FrameThickness { get; private set; } = new(0);   // pas de cadre tant qu'aucune carte n'est choisie
    public bool IsShiny { get; private set; }

    private void ShowIllustration(CardItem item, SkinOption option, Avalonia.Media.Imaging.Bitmap? ready = null)
    {
        if (ready is null)
        {
            var key = ImageKey(item, option.Skin);
            if (!_fullImages.TryGetValue(key, out ready)) { ready = item.LoadFullImage(option.Skin); _fullImages[key] = ready; }
        }
        Image = ready;
        var artistId = option.Skin is null ? item.Card.Artist : option.Skin.Artist ?? item.Card.Artist;
        _artistId = artistId;
        ArtistCredit = _kb.Artist(artistId) is { } artist ? $"Illustration : {artist.Name}" : "";
        Raise(nameof(ArtistCredit)); Raise(nameof(HasArtistCredit));
    }

    /// <summary>Nom d'un skin : « Skin alternatif », « Skin alternatif (peinture) » pour les variantes peintes.</summary>
    private static string SkinLabel(CardSkin skin, int index, int count) =>
        (skin.Id.EndsWith("_paint", StringComparison.Ordinal) ? "Skin alternatif (peinture)" : "Skin alternatif") +
        (skin.Animated ? " animé" : "");
    public string Title { get; private set; } = "Choisis une carte dans la liste";
    public string Stats { get; private set; } = "";
    /// <summary>« 500 PV · Revenu : 10 » : la suite de l'en-tête, après les icônes d'élément et de rareté.</summary>
    public string StatsShort { get; private set; } = "";
    /// <summary>« 500 PV » et le revenu (un nombre, précédé de l'icône du Spamton dans la fiche).</summary>
    public string HpShort { get; private set; } = "";
    public string IncomeShort { get; private set; } = "";
    public Bitmap? SpamtonIcon => _spamton ??= Icons.Spamton(_kb);
    private Bitmap? _spamton;
    /// <summary>« Illustration : Amytis » : la signature affichée en bas à droite de l'illustration ; vide si la carte n'a pas d'artiste connu.</summary>
    public string ArtistCredit { get; private set; } = "";
    public bool HasArtistCredit => ArtistCredit.Length > 0;
    public string Resistances { get; private set; } = "";
    /// <summary>Icône et nom de l'élément, de la rareté de la carte (en-tête de la fiche).</summary>
    public Bitmap? ElementIcon { get; private set; }
    public Bitmap? RarityIcon { get; private set; }
    public string ElementName { get; private set; } = "";
    public string RarityName { get; private set; } = "";
    public Avalonia.Media.IBrush RarityTextBrush { get; private set; } = Avalonia.Media.Brushes.White;
    /// <summary>Résistances de la carte, un élément par ligne : icône et « 10 % ».</summary>
    public IReadOnlyList<ResistanceItem> ResistanceItems { get; private set; } = Array.Empty<ResistanceItem>();
    /// <summary>Tox possédés de base par la carte (🃏 « passifs » de la carte).</summary>
    public IReadOnlyList<ToxLine> InnateTox { get; private set; } = Array.Empty<ToxLine>();
    public bool HasInnateTox => InnateTox.Count > 0;
    public IReadOnlyList<string> Abilities { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<AbilityLine> AbilityLines { get; private set; } = Array.Empty<AbilityLine>();
    /// <summary>« Capacité » au singulier quand la carte n'en a qu'une.</summary>
    public string AbilitiesTitle => AbilityLines.Count == 1 ? "Capacité" : "Capacités";
    public IReadOnlyList<TalentLine> Talents { get; private set; } = Array.Empty<TalentLine>();

    // ----- Lore -----
    public bool HasLore { get; private set; }
    /// <summary>Couleur du nom de l'élément de la carte.</summary>
    public IBrush ElementBrush { get; private set; } = Brushes.White;
    /// <summary>Le lore de la carte est affiché (bouton Lore en haut à droite de la fiche) ; refermé à chaque nouvelle carte.</summary>
    public bool ShowLore { get; private set; }
    public RelayCommand ToggleLoreCommand => _toggleLore ??= new RelayCommand(_ => { ShowLore = !ShowLore; Raise(nameof(ShowLore)); });
    private RelayCommand? _toggleLore;
    /// <summary>« Créature · Aelther · Ère prospère » : espèce, région, chronologie.</summary>
    public string LoreTags { get; private set; } = "";
    public string LoreText { get; private set; } = "";

    // ----- Mes decks -----
    public IReadOnlyList<DeckLine> DecksUsing { get; private set; } = Array.Empty<DeckLine>();
    public bool HasDecksUsing => DecksUsing.Count > 0;
    public string DecksUsingTitle => DecksUsing.Count == 0 ? "" : $"Dans mes decks ({DecksUsing.Count})";
    public bool HasCard { get; private set; }

    private void UpdateDetail()
    {
        var item = Browser.Selected;
        if (item is null) return;
        var c = item.Card;
        HasCard = true;
        Title = item.Name;
        Stats = $"{item.Summary} · Revenu : {c.GoldIncome}";
        StatsShort = $"{c.Hp} PV · Revenu : {c.GoldIncome}";
        HpShort = $"{c.Hp} PV"; IncomeShort = c.GoldIncome.ToString();
        // Résistances en pourcentage (une valeur négative est une faiblesse), avec l'icône de l'élément.
        var resisted = _kb.Elements.Where(e => c.Resistances.ContainsKey(e.Id)).ToList();
        Resistances = string.Join(" · ", resisted.Select(e => $"{_kb.ElementName(e.Id)} {c.Resistances[e.Id]} %"));
        ResistanceItems = resisted.Select(e => new ResistanceItem(Icons.Load(_kb, e.Icon, 64), _kb.ElementName(e.Id), $"{c.Resistances[e.Id]} %", AbilityRichText.ElementBrush(e.Id))).ToList();
        ElementIcon = Icons.Element(_kb, c.Element, 48);
        RarityIcon = Icons.Rarity(_kb, c.Rarity, 48);
        ElementBrush = AbilityRichText.ElementBrush(c.Element); Raise(nameof(ElementBrush));
        ElementName = item.ElementName; RarityName = item.RarityName;
        RarityTextBrush = Icons.RarityTextBrush(_kb, c.Rarity);
        InnateTox = _kb.InnateTox(c).Select(t =>
        {
            var image = Icons.Tox(_kb, t);
            return new ToxLine(_kb.ToxName(t), t.Rarity is { } r ? _kb.RarityName(r) : "", _kb.Descriptions.Tox(t), image,
                Icons.RarityBrush(_kb, t.Rarity), Icons.RarityTextBrush(_kb, t.Rarity), image is not null);
        }).ToList();
        Abilities = c.Abilities.Select(a => _kb.Text("Card.Ability." + a)).ToList();
        AbilityLines = c.Abilities.Select(a => BuildAbilityLine(a, c.Element)).ToList();
        // « Talent 1 : nom », « Talent 2 : nom », « Talent 3 : nom » (l'ordre des talents de la carte, comme dans le Deck builder).
        Talents = c.Talents.Select((t, i) => new TalentLine($"Talent {i + 1} : {_kb.TalentName(t)}", DescribeTalent(t))).ToList();
        _updatingSkins = true;
        SkinOptions = new[] { new SkinOption(null, "Carte de base") }
            .Concat(item.Skins.Select((s, i) => new SkinOption(s, SkinLabel(s, i, item.Skins.Count), s.Artist))).ToList();
        _selectedSkinOption = SkinOptions.FirstOrDefault(o => o.Skin?.Id == item.SelectedSkinId) ?? SkinOptions[0];
        foreach (var o in SkinOptions) o.IsSelected = ReferenceEquals(o, _selectedSkinOption);
        FrameBrush = item.RarityBrush; FrameThickness = item.FrameThickness; IsShiny = item.IsShiny;
        Raise(nameof(FrameBrush)); Raise(nameof(FrameThickness)); Raise(nameof(IsShiny));
        Raise(nameof(SelectedSkinOption));
        _updatingSkins = false;
        ShowIllustration(item, _selectedSkinOption);
        if (item.Skins.Count > 0) _ = PreloadSkinsAsync(item);

        var lore = item.Lore;
        HasLore = lore is not null;
        ShowLore = false; Raise(nameof(ShowLore));
        LoreTags = lore is null ? "" : string.Join(" · ", new[] { lore.Species, lore.Region, lore.Chronology }.Where(x => x.Length > 0));
        LoreText = lore?.Description ?? "";

        UpdateDecksUsing();
        foreach (var n in new[]
                 {
                     nameof(HasCard), nameof(Title), nameof(Stats), nameof(StatsShort), nameof(HpShort), nameof(IncomeShort), nameof(SpamtonIcon), nameof(ArtistCredit), nameof(HasArtistCredit), nameof(Resistances), nameof(ResistanceItems), nameof(InnateTox), nameof(HasInnateTox),
                     nameof(ElementIcon), nameof(RarityIcon), nameof(ElementName), nameof(RarityName), nameof(RarityTextBrush),
                     nameof(Abilities), nameof(AbilityLines), nameof(AbilitiesTitle),
                     nameof(Talents), nameof(HasLore), nameof(LoreTags), nameof(LoreText),
                 })
            Raise(n);
    }

    private AbilityLine BuildAbilityLine(string a, string? cardElement)
    {
        var line = new AbilityLine(_kb.Text("Card.Ability." + a), _kb.Descriptions.Ability(a), StatsText(a));
        if (_kb.Descriptions.Stats(a) is not { } s) return line;
        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        return line with
        {
            Rich = AbilityRichText.Build(_kb, _kb.Descriptions.AbilityRich(a), _kb.Descriptions.AbilityTox(a)),
            HasStats = true,
            HasCooldown = s.Cooldown > 0,
            CooldownText = s.Cooldown.ToString(),
            PrecisionText = $"{s.Precision.ToString("0.##", fr)}%",
            CanCrit = s.CanCrit,
            CritChanceText = $"{s.CritChance.ToString("0.##", fr)}%",
            CritRatioText = $"×{s.CritRatio.ToString("0.##", fr)}",
            CooldownIcon = Icons.Load(_kb, "images/icons/stats/cooldown.webp", 96),
            PrecisionIcon = Icons.Load(_kb, "images/icons/stats/accuracy.webp", 64),
            CritIcon = Icons.Load(_kb, "images/icons/stats/critical.webp", 64),
            RatioIcon = Icons.Load(_kb, "images/icons/stats/multiplier.webp", 64),
        };
    }

    /// <summary>« Précision 100 % · Critique 1 % (×2) · 3 tours de recharge » : les données de la capacité (🃏).</summary>
    public string StatsText(string abilityId)
    {
        if (_kb.Descriptions.Stats(abilityId) is not { } s) return "";
        var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        var parts = new List<string> { $"Précision {s.Precision.ToString("0.##", fr)} %" };
        parts.Add(s.CanCrit
            ? $"Critique {s.CritChance.ToString("0.##", fr)} % (×{s.CritRatio.ToString("0.##", fr)})"
            : "Pas de coup critique");
        if (s.Cooldown > 0) parts.Add($"{s.Cooldown} tour{(s.Cooldown > 1 ? "s" : "")} de recharge");
        return string.Join(" · ", parts);
    }

    private void UpdateDecksUsing()
    {
        var id = Browser.Selected?.Card.Id;
        DecksUsing = id is null || _decks is null
            ? Array.Empty<DeckLine>()
            : _decks.Decks.Where(d => d.Slots.Any(s => s.CardId == id))
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(d => new DeckLine(d, (d.Favorite ? "★ " : "") + (d.IsGenerated ? "⚙ " : "") + d.Name +
                                            (d.Slots.Count < Deck.MaxCards ? " (brouillon)" : "")))
                .ToList();
        Raise(nameof(DecksUsing)); Raise(nameof(HasDecksUsing)); Raise(nameof(DecksUsingTitle));
    }

    /// <summary>
    /// Description chiffrée d'un talent (KI-021) : les emplacements de valeurs des textes du jeu sont remplis comme le jeu le
    /// fait pour une carte hors partie (voir <see cref="DescriptionFormatter"/>).
    /// </summary>
    internal string DescribeTalent(string id)
    {
        var d = _kb.Descriptions.Talent(id);
        return d.Length > 0 ? d : "(pas de description dans les données du jeu)";
    }
}
