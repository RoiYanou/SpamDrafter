namespace SpamDrafter.App.ViewModels;

/// <summary>Une vignette de jeu quotidien dans le menu de l'onglet.</summary>
public sealed class DailyGameEntry : ViewModelBase
{
    public DailyGameEntry(string id, string emoji, string title, string description, bool ready) { Id = id; Emoji = emoji; Title = title; Description = description; Ready = ready; }
    public string Id { get; }
    public string Emoji { get; }
    public string Title { get; }
    public string Description { get; }
    public bool Ready { get; }
    public string Status => Ready ? "" : "Bientôt";
    private bool _pending;
    /// <summary>Le jeu du jour n'a pas encore été fait : son bouton brille.</summary>
    public bool Pending { get => _pending; set => Set(ref _pending, value); }
}

/// <summary>
/// Onglet « Jeux » (phase 16, D-095) : deux sous-onglets, « Quotidiens » (une partie par jour) et « Permanent » (à volonté, à définir).
/// Lots A à D : le RNG, l'énigme, le détail et le quizz du jour.
/// </summary>
public sealed class GamesViewModel : ViewModelBase
{
    public GamesViewModel(RiddleViewModel riddle, DetailViewModel detail, QuizViewModel quiz, RngViewModel rng)
    {
        Riddle = riddle; Detail = detail; Quiz = quiz; Rng = rng;
        Quiz.LockChanged += () => Raise(nameof(NotLocked));
        Daily =
        [
            new("rng", "🎲", "Tirage du jour", "Une carte est tirée au hasard, puis notée selon la rareté et la cohérence de ses éléments. Retrouve tes anciens tirages dans l'historique.", true),
            new("riddle", "💬", "Énigme du jour", "Une phrase énigmatique : retrouve la carte.", true),
            new("detail", "🔍", "Détail du jour", "Un détail très zoomé d'une carte : retrouve-la, l'image se dézoome à chaque essai.", true),
            new("quiz", "❓", "Quizz du jour", "5 questions sur le jeu.", true),
        ];
        UpdatePending();
    }

    public RiddleViewModel Riddle { get; }
    public DetailViewModel Detail { get; }
    public QuizViewModel Quiz { get; }
    public RngViewModel Rng { get; }
    public IReadOnlyList<DailyGameEntry> Daily { get; }

    private int _subTab;
    /// <summary>0 = Quotidiens, 1 = Permanent.</summary>
    public int SubTab
    {
        get => _subTab;
        set { if (Quiz.IsLocked) return; if (Set(ref _subTab, value)) { Raise(nameof(ShowDaily)); Raise(nameof(ShowPermanent)); } }
    }
    /// <summary>Faux pendant le quizz : les sous-onglets et le retour au menu sont bloqués.</summary>
    public bool NotLocked => !Quiz.IsLocked;
    public bool ShowDaily => _subTab == 0;
    public bool ShowPermanent => _subTab == 1;

    private string? _openGame;
    /// <summary>Identifiant du jeu quotidien ouvert, ou null : on voit alors la liste des jeux.</summary>
    public string? OpenGame
    {
        get => _openGame;
        private set { if (Set(ref _openGame, value)) { Raise(nameof(ShowMenu)); Raise(nameof(ShowRiddle)); Raise(nameof(ShowDetail)); Raise(nameof(ShowQuiz)); Raise(nameof(ShowRng)); } }
    }
    public bool ShowMenu => _openGame is null;
    public bool ShowRiddle => _openGame == "riddle";
    public bool ShowDetail => _openGame == "detail";
    public bool ShowQuiz => _openGame == "quiz";
    public bool ShowRng => _openGame == "rng";

    private RelayCommand? _open, _back;
    public RelayCommand OpenCommand => _open ??= new RelayCommand(p =>
    {
        if (p is DailyGameEntry { Ready: true } g) { Riddle.ShowHistory = Detail.ShowHistory = Quiz.ShowHistory = false; OpenGame = g.Id; if (g.Id == "riddle") Riddle.Refresh(); if (g.Id == "detail") Detail.Refresh(); if (g.Id == "quiz") Quiz.Refresh(); if (g.Id == "rng") Rng.Refresh(); }
    });
    public RelayCommand BackCommand => _back ??= new RelayCommand(_ => { if (!Quiz.IsLocked) { OpenGame = null; UpdatePending(); } });

    /// <summary>Fait briller le bouton des jeux du jour qui n'ont pas encore été faits.</summary>
    public void UpdatePending()
    {
        foreach (var g in Daily)
            g.Pending = g.Id switch
            {
                "rng" => Rng.Available && !Rng.Started,
                "riddle" => Riddle.Available && !Riddle.IsFinished,
                "detail" => Detail.Available && !Detail.IsFinished,
                "quiz" => Quiz.Available && !Quiz.IsFinished,
                _ => false,
            };
    }

    /// <summary>Appelé quand l'onglet est affiché : un jour nouveau (minuit à Paris) renouvelle les jeux.</summary>
    public void Refresh() { Riddle.Refresh(); Detail.Refresh(); Quiz.Refresh(); Rng.Refresh(); UpdatePending(); }
}
