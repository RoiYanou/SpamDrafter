using Avalonia.Threading;
using SpamDrafter.Core.Decks;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.App.ViewModels;

/// <summary>Un succès de l'accueil : acquis (jaune) ou encore caché (« ??? »).</summary>
public sealed record Achievement(string Label, bool Done)
{
    public string Text => Done ? Label : "???";
}

/// <summary>Astuces de l'accueil et de l'attente (génération) : des faits sur l'application, jamais des règles du jeu inventées.</summary>
public static class Tips
{
    public static IReadOnlyList<string> All { get; } = new[]
    {
        "Un deck contient de 1 à 4 cartes, sans doublon.",
        "Ctrl+S sauvegarde le deck en cours ; Ctrl+Z et Ctrl+Y annulent et rétablissent.",
        "Dans la fiche d'une carte, choisis « Skin » pour changer son affichage partout dans l'application.",
        "Le Drafter retrouve tes decks qui contiennent toutes les cartes que tu choisis.",
        "Ctrl+F place le curseur dans la recherche ; Ctrl+1 à Ctrl+5 changent d'onglet.",
        "Dans la fiche d'une carte, un reflet holographique suit ta souris.",
        "Dans Réglages, tu peux changer la couleur de l'application selon un élément.",
        "Le bouton « Présenter » met ton deck en scène et l'enregistre en image à partager.",
        "Dans « Mes decks », les flèches ▲ ▼ montent ou descendent un deck pour choisir ton propre ordre.",
    };

    /// <summary>L'astuce du jour (la même toute la journée).</summary>
    public static string OfTheDay(DateOnly day) => All[Math.Abs(day.DayNumber) % All.Count];

    private static readonly Random Rng = new();
    public static string Any() => All[Rng.Next(All.Count)];
}

public enum TipKind { Text, Card, Ability, Talent, Tox }

/// <summary>Un morceau d'astuce : du texte simple, ou un nom (carte, compétence, talent, Tox) coloré et cliquable vers la Bibliothèque.</summary>
public sealed record TipSegment(string Text, TipKind Kind = TipKind.Text, string? Id = null)
{
    public bool IsLink => Kind != TipKind.Text && Id is not null;
}

/// <summary>
/// Astuces du bandeau de l'accueil (demande de l'utilisateur, 17/11/2026). Les noms sont écrits <c>{carte:id}</c>, <c>{competence:id}</c>,
/// <c>{talent:id}</c>, <c>{tox:clé}</c> : le nom affiché est lu dans les textes du jeu (donc toujours celui de la Bibliothèque).
/// </summary>
public static class HomeTips
{
    public static IReadOnlyList<string> All { get; } = new[]
    {
        "Une carte avec le tox {tox:Immunity} est insensible à la compétence {competence:Veuve noire} de {carte:75491473-57fa-4b8f-aced-9d61d0404e65}.",
        "Une carte avec le tox {tox:Tenacite} survivra à 1 PV aux compétences {competence:Veuve noire} et {competence:Avalement}.",
        "Le tox {tox:Aoe} partage aussi les points de vie que la carte se soigne aux alliés adjacents (par exemple avec les tox {tox:Life Steal} et {tox:Primitive heal}).",
        "As-tu déjà entendu parler des vecteurs de scaling ? Diversifie tes augmentations de dégâts !",
        "Certaines illustrations de carte cachent des secrets, ouvrez l’œil !",
        "Le tox {tox:Phoenix} ne peut pas ressusciter une carte qui a 0 point de vie maximum (par exemple à cause de la brûlure après le tour 20).",
        "N’abandonne jamais un combat ! Un événement insoupçonné pourrait te redonner une chance…",
        "Au début de ton tour, tu gagnes des Spamtons bonus en fonction du tour actuel ! (Tour 1 → +1 Spamton, tour 2 → +2 Spamtons…)",
        "La mort d’une carte donne 50 Spamtons à son propriétaire ! (Augmenté à 100 avec le talent {talent:Heritage})",
    };

    private static readonly System.Text.RegularExpressions.Regex Marker = new(@"\{(carte|competence|talent|tox):([^}]+)\}");

    /// <summary>Découpe une astuce en morceaux ; un nom introuvable reste du texte simple (jamais de lien cassé).</summary>
    public static IReadOnlyList<TipSegment> Parse(string tip, KnowledgeBase kb, IReadOnlyList<CardItem> items)
    {
        var result = new List<TipSegment>();
        var pos = 0;
        foreach (System.Text.RegularExpressions.Match m in Marker.Matches(tip))
        {
            if (m.Index > pos) result.Add(new TipSegment(tip[pos..m.Index]));
            var id = m.Groups[2].Value;
            var seg = m.Groups[1].Value switch
            {
                "carte" => items.FirstOrDefault(i => i.Card.Id == id) is { } c ? new TipSegment(c.Name, TipKind.Card, id) : null,
                "competence" => items.Any(i => i.Card.Abilities.Contains(id)) ? new TipSegment(kb.AbilityName(id), TipKind.Ability, id) : null,
                "talent" => kb.FindTalent(id) is not null ? new TipSegment(kb.TalentName(id), TipKind.Talent, id) : null,
                _ => kb.AllTox.FirstOrDefault(t => DescriptionFormatter.ToxKey(t) == id) is { } t ? new TipSegment(kb.ToxName(t), TipKind.Tox, id) : null,
            };
            result.Add(seg ?? new TipSegment(id));
            pos = m.Index + m.Length;
        }
        if (pos < tip.Length) result.Add(new TipSegment(tip[pos..]));
        return result;
    }
}

/// <summary>
/// Page d'accueil (phase 13, D-078) : le dernier deck, la carte du jour, les nouveautés, les succès, et une astuce.
/// Tout est calculé à partir de ce que l'application sait déjà : rien de plus n'est enregistré.
/// La carte du jour est la même pour tous les joueurs et change à 00:00 heure de Paris (<see cref="CardOfTheDay"/>).
/// </summary>
public sealed class HomeViewModel : ViewModelBase
{
    private readonly DeckLibrary _decks;
    private readonly IReadOnlyList<CardItem> _items;
    private readonly KnowledgeBase _kb;
    private readonly Func<DateTime> _utcNow;
    private DateOnly _day;
    private int _doneCount;

    public HomeViewModel(KnowledgeBase kb, IReadOnlyList<CardItem> items, DeckLibrary decks, Action<Deck> openDeck, Action<CardItem> openCard,
        Func<DateTime>? utcNow = null)
    {
        _kb = kb; _items = items; _decks = decks; _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _day = ParisClock.Today(_utcNow());
        _tipIndex = Random.Shared.Next(HomeTips.All.Count);
        TipSegments = HomeTips.Parse(HomeTips.All[_tipIndex], kb, items);
        OpenLastDeckCommand = new RelayCommand(_ => { if (LastDeck is { } d) openDeck(d); }, _ => LastDeck is not null);
        OpenCardOfDayCommand = new RelayCommand(_ => { if (CardOfDay is { } c) openCard(c); }, _ => CardOfDay is not null);
        decks.Changed += (_, _) => Refresh();
        foreach (var i in items) i.SkinChanged += _ => { Raise(nameof(Achievements)); CheckAchievements(); };
        _doneCount = Achievements.Count(a => a.Done);
        Refresh();
        // Minuit à Paris : la carte du jour change toute seule, sans relancer l'application.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += (_, _) => CheckDay();
        timer.Start();
    }

    public RelayCommand OpenLastDeckCommand { get; }
    /// <summary>Sans deck (premier lancement) : le bouton de l'accueil emmène dans l'onglet Builder.</summary>
    public Action? OpenBuilder { get; set; }
    public RelayCommand OpenBuilderCommand => _openBuilder ??= new RelayCommand(_ => OpenBuilder?.Invoke());
    private RelayCommand? _openBuilder;
    public RelayCommand OpenCardOfDayCommand { get; }

    private static readonly string[] Mentions =
    {
        "Tu te rappelles de ce deck ?", "La carte de gauche est magnifique, tu trouves pas ?", "Et si tu essayais ce deck aujourd'hui ?",
        "Tu trouves pas le regard de la carte de droite un peu vicieux ?", "Si tu gagnes avec ce deck, je t'offre un cookie !",
        "Ça c'est du deck qui deckapote !",
    };
    // Un deck mis en avant et une phrase pour CHAQUE mode (« toutes » = 0, « possédés » = 1), tirés indépendamment à chaque démarrage.
    private readonly Guid[] _featuredByMode = new Guid[2];
    private readonly string[] _mentionByMode = { Mentions[Random.Shared.Next(Mentions.Length)], Mentions[Random.Shared.Next(Mentions.Length)] };
    /// <summary>Vrai en mode « Cartes et talents possédés ».</summary>
    public Func<bool> OwnedMode { get; set; } = () => false;
    private int Mode => OwnedMode() ? 1 : 0;
    /// <summary>Vrai quand le joueur n'a importé aucune carte en mode « possédés » : l'accueil l'invite à importer.</summary>
    public Func<bool> NoCards { get; set; } = () => false;
    public Action? OpenImport { get; set; }
    public RelayCommand OpenImportCommand => _openImport ??= new RelayCommand(_ => OpenImport?.Invoke());
    private RelayCommand? _openImport;
    public bool ShowImportPrompt => LastDeck is null && NoCards();
    public bool ShowCreatePrompt => LastDeck is null && !NoCards();

    private static readonly (string Text, string Language)[] Greetings =
    {
        ("Bonjour !", "Français"), ("Hello!", "Anglais"), ("Hola!", "Espagnol"), ("Ciao!", "Italien"), ("Hallo!", "Allemand"),
        ("Olá!", "Portugais"), ("Hej!", "Suédois"), ("Hei!", "Finnois"), ("Merhaba!", "Turc"), ("Aloha!", "Hawaïen"),
        ("Namaste!", "Hindi"), ("Shalom!", "Hébreu"), ("Привет!", "Russe"), ("こんにちは！", "Japonais"), ("안녕하세요!", "Coréen"),
        ("你好！", "Chinois"), ("Γειά σου!", "Grec"), ("Salam!", "Arabe"), ("Dzień dobry!", "Polonais"), ("Ahoj!", "Tchèque"),
        ("Szia!", "Hongrois"), ("Sawubona!", "Zoulou"), ("Jambo!", "Swahili"),
    };
    private static readonly (string Text, string Language) PickedGreeting = Greetings[Random.Shared.Next(Greetings.Length)];
    /// <summary>« Bonjour ! » dans une langue au hasard, changé à chaque démarrage (le même pour les deux modes).</summary>
    public string Greeting => PickedGreeting.Text;
    /// <summary>Nom de la langue du bonjour, affiché au survol.</summary>
    public string GreetingLanguage => PickedGreeting.Language;
    /// <summary>Teinte de départ (0 à 360) de la couleur animée du bonjour ; 1 chance sur 100 : arc-en-ciel animé.</summary>
    public double GreetingHue { get; } = Random.Shared.NextDouble() * 360;
    public bool GreetingRainbow { get; } = Random.Shared.NextDouble() < 0.01;

    public Deck? LastDeck { get; private set; }
    public bool HasLastDeck => LastDeck is not null;
    public bool HasNoDeck => LastDeck is null;
    /// <summary>Le deck mis en avant : un deck de la collection tiré au hasard à chaque démarrage, avec une petite phrase au hasard.</summary>
    public string LastDeckTitle => LastDeck is null ? "" : LastDeck.Name;
    public string DeckMention => LastDeck is null ? "" : _mentionByMode[Mode];
    public IReadOnlyList<DeckChip> LastDeckChips { get; private set; } = Array.Empty<DeckChip>();
    /// <summary>« 3 cartes · 2 talents choisis » : un résumé sans calcul de synergie (la note vient de l'onglet Génération).</summary>
    public string LastDeckSummary { get; private set; } = "";

    /// <summary>La carte du jour : la même pour tous, changeante à 00:00 heure de Paris.</summary>
    public CardItem? CardOfDay
    {
        get
        {
            var id = CardOfTheDay.Pick(_items.Select(i => i.Card.Id), _utcNow());
            return id is null ? null : _items.FirstOrDefault(i => i.Card.Id == id);
        }
    }
    public string CardOfDayName => CardOfDay?.Name ?? "";
    /// <summary>« Illustration : Nom » de l'artiste de la carte du jour (ou de son skin).</summary>
    public string CardOfDayCredit
    {
        get
        {
            var c = CardOfDay;
            if (c is null) return "";
            var artistId = c.SelectedSkin is { } sk ? sk.Artist ?? c.Card.Artist : c.Card.Artist;
            return _kb.Artist(artistId) is { } a ? $"Illustration : {a.Name}" : "";
        }
    }
    /// <summary>Illustration de la carte du jour en excellente qualité (1 024 px de haut), comme dans la fiche détaillée.</summary>
    public Avalonia.Media.Imaging.Bitmap? CardOfDayImage
    {
        get
        {
            var c = CardOfDay;
            if (c is null) return null;
            if (_dayImageId != c.Card.Id || _dayImageSkin != c.SelectedSkinId) { _dayImage = c.LoadFullImage(1024); _dayImageId = c.Card.Id; _dayImageSkin = c.SelectedSkinId; }
            return _dayImage;
        }
    }
    private Avalonia.Media.Imaging.Bitmap? _dayImage; private string? _dayImageId, _dayImageSkin;

    /// <summary>La carte du jour en grande fenêtre (un clic sur la carte l'ouvre, un clic en dehors la ferme).</summary>
    public bool ZoomOpen { get; private set; }
    public RelayCommand OpenZoomCommand => _openZoom ??= new RelayCommand(_ => SetZoom(true), _ => CardOfDay is not null);
    public RelayCommand CloseZoomCommand => _closeZoom ??= new RelayCommand(_ => SetZoom(false));
    private RelayCommand? _openZoom, _closeZoom;
    private void SetZoom(bool open) { ZoomOpen = open; Raise(nameof(ZoomOpen)); }
    /// <summary>L'astuce du bandeau : tirée au hasard à chaque démarrage de l'application.</summary>
    public IReadOnlyList<TipSegment> TipSegments { get; }
    private int _tipIndex;
    /// <summary>Une nouvelle astuce au hasard (jamais deux fois de suite la même) : le bandeau l'appelle à chaque passage.</summary>
    public IReadOnlyList<TipSegment> NextTip()
    {
        if (HomeTips.All.Count > 1) _tipIndex = (_tipIndex + 1 + Random.Shared.Next(HomeTips.All.Count - 1)) % HomeTips.All.Count;
        return HomeTips.Parse(HomeTips.All[_tipIndex], _kb, _items);
    }
    /// <summary>Un clic sur un nom coloré de l'astuce : le branchement vers la Bibliothèque est fait par la fenêtre principale.</summary>
    public Action<TipSegment>? OpenTipTarget { get; set; }
    public void OpenTip(TipSegment segment) { if (segment.IsLink) OpenTipTarget?.Invoke(segment); }

    public IReadOnlyList<string> News => new[]
    {
        $"SPAM {_kb.Manifest.GameVersion} · {_kb.Cards.Count} cartes",
        $"{_kb.Cards.Sum(c => c.Skins?.Count ?? 0)} skins alternatifs, à choisir dans la fiche d'une carte",
        "Nouvelle identité visuelle : cadres de rareté, sons, animations, thèmes par élément",
    };

    public IReadOnlyList<Achievement> Achievements
    {
        get
        {
            var generated = _decks.Decks.Count(d => d.IsGenerated);
            return new[]
            {
                new Achievement("Premier deck", _decks.Decks.Count >= 1),
                new Achievement("10 decks", _decks.Decks.Count >= 10),
                new Achievement("Deck du chef", generated >= 1),
                new Achievement("10 decks du chef", generated >= 10),
                new Achievement("Chasseur de skins", _items.Any(i => i.SelectedSkinId is not null)),
            };
        }
    }

    private void CheckDay()
    {
        var today = ParisClock.Today(_utcNow());
        if (today == _day) return;
        _day = today;
        foreach (var n in new[] { nameof(CardOfDay), nameof(CardOfDayName), nameof(CardOfDayCredit), nameof(CardOfDayImage) }) Raise(n);
        OpenCardOfDayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Un nouveau succès : petit carillon.</summary>
    private void CheckAchievements()
    {
        var n = Achievements.Count(a => a.Done);
        if (n > _doneCount) Sounds.Play(SoundKind.Success);
        _doneCount = n;
    }

    /// <summary>Decks que le Deck builder montre (mode « possédés » : decks entièrement possédés) ; le deck mis en avant en fait partie.</summary>
    public Func<Deck, bool> DeckFilter { get; set; } = _ => true;

    public void Refresh()
    {
        var visible = _decks.Decks.Where(DeckFilter).ToList();
        // Le deck mis en avant est tiré une fois par démarrage ; s'il disparaît (supprimé), on en tire un autre.
        LastDeck = visible.FirstOrDefault(d => d.Id == _featuredByMode[Mode]);
        if (LastDeck is null && visible.Count > 0)
        {
            LastDeck = visible[Random.Shared.Next(visible.Count)];
            _featuredByMode[Mode] = LastDeck.Id;
        }
        LastDeckChips = LastDeck is null ? Array.Empty<DeckChip>() : DeckChipsConverter.Build(LastDeck.Slots);
        LastDeckSummary = LastDeck is null ? "" :
            $"{LastDeck.Slots.Count} carte{(LastDeck.Slots.Count > 1 ? "s" : "")} · {LastDeck.Slots.Count(s => s.TalentId is not null)} talent(s) choisi(s)";
        foreach (var n in new[] { nameof(LastDeck), nameof(HasLastDeck), nameof(HasNoDeck), nameof(ShowImportPrompt), nameof(ShowCreatePrompt), nameof(LastDeckTitle), nameof(DeckMention), nameof(LastDeckChips), nameof(LastDeckSummary), nameof(Achievements) })
            Raise(n);
        OpenLastDeckCommand.RaiseCanExecuteChanged();
        CheckAchievements();
    }
}
