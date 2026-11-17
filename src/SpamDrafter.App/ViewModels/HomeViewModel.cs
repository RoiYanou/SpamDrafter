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

/// <summary>
/// Page d'accueil (phase 13, D-078) : le dernier deck, la carte du jour, les nouveautés, les succès, une astuce et « Reprendre là où
/// j'en étais ». Tout est calculé à partir de ce que l'application sait déjà : rien de plus n'est enregistré.
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
    private Action? _resume;

    public HomeViewModel(KnowledgeBase kb, IReadOnlyList<CardItem> items, DeckLibrary decks, Action<Deck> openDeck, Action<CardItem> openCard,
        Func<DateTime>? utcNow = null)
    {
        _kb = kb; _items = items; _decks = decks; _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _day = ParisClock.Today(_utcNow());
        OpenLastDeckCommand = new RelayCommand(_ => { if (LastDeck is { } d) openDeck(d); }, _ => LastDeck is not null);
        OpenCardOfDayCommand = new RelayCommand(_ => { if (CardOfDay is { } c) openCard(c); }, _ => CardOfDay is not null);
        ResumeCommand = new RelayCommand(_ => _resume?.Invoke(), _ => _resume is not null);
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
    public RelayCommand ResumeCommand { get; }

    private static readonly string[] Mentions =
    {
        "Tu te rappelles de ce deck ?", "La carte à gauche est magnifique, tu trouves pas ?", "Et si t'essayais ce deck aujourd'hui ?",
        "Tu trouves pas le regard de la carte de droite un peu vicieux ?", "Si tu gagnes avec ce deck, je t'offre un cookie !",
        "Ça c'est du deck qui deckapote !",
    };
    private Guid _featured;
    private readonly string _mention = Mentions[Random.Shared.Next(Mentions.Length)];

    public string Greeting => "Bon retour !";
    public string Subtitle => "Reprends là où tu t'étais arrêté.";

    /// <summary>« Reprendre : Deck builder » : revient à l'onglet où le joueur travaillait avant de fermer l'application.</summary>
    public string ResumeLabel { get; private set; } = "";
    public bool HasResume => _resume is not null;
    public void SetResume(string? label, Action? go)
    {
        _resume = go; ResumeLabel = label is null ? "" : "Reprendre : " + label;
        Raise(nameof(ResumeLabel)); Raise(nameof(HasResume)); ResumeCommand.RaiseCanExecuteChanged();
    }

    public Deck? LastDeck { get; private set; }
    public bool HasLastDeck => LastDeck is not null;
    public bool HasNoDeck => LastDeck is null;
    /// <summary>Le deck mis en avant : un deck de la collection tiré au hasard à chaque démarrage, avec une petite phrase au hasard.</summary>
    public string LastDeckTitle => LastDeck is null ? "" : LastDeck.Name;
    public string DeckMention => LastDeck is null ? "" : _mention;
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
    public string Tip => Tips.OfTheDay(_day);

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
                new Achievement("Deck généré", generated >= 1),
                new Achievement("10 decks générés", generated >= 10),
                new Achievement("Chasseur de skins", _items.Any(i => i.SelectedSkinId is not null)),
            };
        }
    }

    private void CheckDay()
    {
        var today = ParisClock.Today(_utcNow());
        if (today == _day) return;
        _day = today;
        foreach (var n in new[] { nameof(CardOfDay), nameof(CardOfDayName), nameof(CardOfDayCredit), nameof(CardOfDayImage), nameof(Tip) }) Raise(n);
        OpenCardOfDayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Un nouveau succès : petit carillon.</summary>
    private void CheckAchievements()
    {
        var n = Achievements.Count(a => a.Done);
        if (n > _doneCount) Sounds.Play(SoundKind.Success);
        _doneCount = n;
    }

    private void Refresh()
    {
        // Le deck mis en avant est tiré une fois par démarrage ; s'il disparaît (supprimé), on en tire un autre.
        LastDeck = _decks.Decks.FirstOrDefault(d => d.Id == _featured);
        if (LastDeck is null && _decks.Decks.Count > 0)
        {
            LastDeck = _decks.Decks[Random.Shared.Next(_decks.Decks.Count)];
            _featured = LastDeck.Id;
        }
        LastDeckChips = LastDeck is null ? Array.Empty<DeckChip>() : DeckChipsConverter.Build(LastDeck.Slots);
        LastDeckSummary = LastDeck is null ? "" :
            $"{LastDeck.Slots.Count} carte{(LastDeck.Slots.Count > 1 ? "s" : "")} · {LastDeck.Slots.Count(s => s.TalentId is not null)} talent(s) choisi(s)";
        foreach (var n in new[] { nameof(LastDeck), nameof(HasLastDeck), nameof(HasNoDeck), nameof(LastDeckTitle), nameof(DeckMention), nameof(LastDeckChips), nameof(LastDeckSummary), nameof(Achievements) })
            Raise(n);
        OpenLastDeckCommand.RaiseCanExecuteChanged();
        CheckAchievements();
    }
}
