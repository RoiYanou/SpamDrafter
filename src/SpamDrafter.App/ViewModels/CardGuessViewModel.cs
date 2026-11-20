using System.Collections.ObjectModel;
using SpamDrafter.Core.Games;
using SpamDrafter.Core.Text;

namespace SpamDrafter.App.ViewModels;

/// <summary>Une réponse déjà donnée dans un jeu « retrouve la carte ».</summary>
public sealed record RiddleGuess(CardItem Item, bool Correct)
{
    public string Name => Item.Name;
    public string Mark => Correct ? "✔" : "✘";
    public string Color => Correct ? "#6EE08A" : "#FF7A7A";
}

/// <summary>
/// Socle des jeux quotidiens où l'on doit retrouver une carte (énigme, détail) : saisie avec suggestions, réponses données,
/// nombre d'essais, une partie par jour (heure de Paris) enregistrée et reprise si on ferme l'application.
/// </summary>
public abstract class CardGuessViewModel : ViewModelBase
{
    private readonly IReadOnlyList<CardItem> _items;
    private readonly DailyBook _book;
    private readonly string _bookPath;
    private readonly string _gameId;
    private readonly int _maxAttempts;
    private DailyRecord _record = new();
    private string _search = "";

    protected CardGuessViewModel(IReadOnlyList<CardItem> items, DailyBook book, string bookPath, string gameId, int maxAttempts, GameHistoryViewModel history)
    {
        History = history;
        _items = items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        _book = book; _bookPath = bookPath; _gameId = gameId; _maxAttempts = maxAttempts;
    }

    /// <summary>Identifiant de la carte à trouver ce jour-là ; null si le jeu n'a rien à proposer.</summary>
    protected abstract string? AnswerCardId { get; }
    /// <summary>Prépare le jeu du jour (appelé à chaque changement de jour, avant l'affichage des réponses).</summary>
    protected abstract void PrepareDay(DateOnly day);
    /// <summary>Les réponses ont changé : à recalculer (indices, zoom…).</summary>
    protected abstract void GuessesChanged();
    protected abstract string GameName { get; }

    /// <summary>À appeler en fin de constructeur de la classe dérivée.</summary>
    protected void Start() => Load(DailyClock.Today());

    public DateOnly Day { get; private set; }
    /// <summary>L'historique de ce jeu (parties passées, avec leur date).</summary>
    public GameHistoryViewModel History { get; }
    private bool _showHistory;
    /// <summary>On regarde l'historique au lieu de jouer.</summary>
    public bool ShowHistory { get => _showHistory; set { if (Set(ref _showHistory, value)) { Raise(nameof(ShowGame)); Raise(nameof(HistoryButtonText)); } } }
    public bool ShowGame => !_showHistory;
    public string HistoryButtonText => _showHistory ? "Retour au jeu" : "Historique";
    private RelayCommand? _toggleHistory, _giveUp;
    public RelayCommand ToggleHistoryCommand => _toggleHistory ??= new RelayCommand(_ => ShowHistory = !ShowHistory);
    /// <summary>Ce jeu propose un bouton « Abandonner » qui donne la réponse (nombre d'essais infini).</summary>
    public virtual bool CanGiveUp => false;
    /// <summary>Le numéro de l'essai est affiché (« Essai 3 »).</summary>
    public virtual bool ShowAttempts => true;
    /// <summary>La carte à trouver est montrée dans l'encadré de fin de partie (pas pour le Détail, dont l'illustration entière est déjà à l'écran).</summary>
    public virtual bool ShowAnswerCard => true;
    public bool ShowGiveUp => CanGiveUp && IsPlaying;
    public bool GaveUp => _record.GaveUp;
    /// <summary>Abandonne : la réponse est donnée, la partie est finie (et ne se rejoue pas aujourd'hui).</summary>
    public RelayCommand GiveUpCommand => _giveUp ??= new RelayCommand(_ => GiveUp());
    public ObservableCollection<RiddleGuess> Guesses { get; } = new();
    public ObservableCollection<CardItem> Suggestions { get; } = new();
    /// <summary>La liste des cartes proposées est ouverte (elle passe par-dessus la page).</summary>
    public bool HasSuggestions => Suggestions.Count > 0;

    public bool Available => AnswerCardId is not null;
    public bool Unavailable => !Available;
    public bool IsFinished => _record.Finished;
    public bool IsPlaying => Available && !IsFinished;
    public bool Won => _record.Won;
    public int WrongGuesses => Guesses.Count(g => !g.Correct);
    public CardItem? AnswerItem => AnswerCardId is { } id ? _items.FirstOrDefault(i => i.Card.Id == id) : null;
    public string AttemptText => _maxAttempts == int.MaxValue ? $"Essai {Guesses.Count + 1}" : $"Essai {Math.Min(Guesses.Count + 1, _maxAttempts)} sur {_maxAttempts}";
    public string DayText => Day.ToString("dddd d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
    public string ResultTitle => Won ? "Bien joué !" : GaveUp ? "Tu as abandonné" : "Perdu…";
    public string ResultText => Won
        ? $"Tu as trouvé {AnswerItem?.Name} en {Guesses.Count} essai{(Guesses.Count > 1 ? "s" : "")}."
        : $"C'était {AnswerItem?.Name}.";
    /// <summary>Affiché à côté de la date une fois la partie finie.</summary>
    public virtual string NextGameText => "Nouveau jeu demain";

    public string Search
    {
        get => _search;
        set { if (Set(ref _search, value)) UpdateSuggestions(); }
    }

    private RelayCommand? _guess;
    public RelayCommand GuessCommand => _guess ??= new RelayCommand(p => { if (p is CardItem item) Guess(item); });

    /// <summary>Recharge la partie si le jour (heure de Paris) a changé depuis l'affichage.</summary>
    public void Refresh()
    {
        var today = DailyClock.Today();
        if (today != Day) Load(today);
    }

    /// <summary>Recharge la partie du jour (après la réinitialisation des jeux quotidiens).</summary>
    public void Reload() => Load(DailyClock.Today());

    private void Load(DateOnly day)
    {
        Day = day;
        PrepareDay(day);
        _record = _book.For(_gameId, day);
        Guesses.Clear();
        if (AnswerCardId is { } answer)
            foreach (var id in _record.Guesses)
                if (_items.FirstOrDefault(i => i.Card.Id == id) is { } item) Guesses.Add(new RiddleGuess(item, id == answer));
        GuessesChanged();
        _search = "";
        UpdateSuggestions();
        RaiseAll();
    }

    private void GiveUp()
    {
        if (!IsPlaying || !CanGiveUp) return;
        _record.Finished = true; _record.Won = false; _record.GaveUp = true;
        _book.TrySave(_bookPath);
        RecordHistory();
        GuessesChanged();
        _search = ""; UpdateSuggestions();
        RaiseAll();
    }

    /// <summary>Enregistre la partie terminée dans l'historique du jeu.</summary>
    private void RecordHistory()
    {
        var n = Guesses.Count;
        History.Add(new DailyHistoryEntry
        {
            Day = DailyClock.Key(Day), Good = Won, CardId = AnswerCardId, CardName = AnswerItem?.Name ?? "",
            Headline = Won ? $"Trouvé en {n} essai{(n > 1 ? "s" : "")}" : _record.GaveUp ? (n == 0 ? "Abandon" : $"Abandon après {n} essai{(n > 1 ? "s" : "")}") : "Perdu",
            Lines = Guesses.Select(g => new DailyHistoryLine { Text = g.Name, Mark = g.Correct ? 1 : -1 }).ToList(),
        });
    }

    private void Guess(CardItem item)
    {
        if (AnswerCardId is not { } answer || _record.Finished || _record.Guesses.Contains(item.Card.Id)) return;
        var correct = item.Card.Id == answer;
        _record.Guesses.Add(item.Card.Id);
        Guesses.Add(new RiddleGuess(item, correct));
        if (correct || Guesses.Count >= _maxAttempts) { _record.Finished = true; _record.Won = correct; RecordHistory(); }
        _book.TrySave(_bookPath);
        GuessesChanged();
        _search = ""; UpdateSuggestions();
        RaiseAll();
    }

    private void UpdateSuggestions()
    {
        Suggestions.Clear();
        Raise(nameof(HasSuggestions));
        var q = TextSearch.Normalize(_search);
        if (q.Length == 0 || _record.Finished) return;
        var taken = _record.Guesses.ToHashSet();
        foreach (var i in _items.Where(i => !taken.Contains(i.Card.Id) && TextSearch.Normalize(i.Name).Contains(q)).Take(8)) Suggestions.Add(i);
        Raise(nameof(HasSuggestions));
    }

    /// <summary>Notifie l'interface ; les classes dérivées ajoutent leurs propres propriétés par <see cref="RaiseMore"/>.</summary>
    private void RaiseAll()
    {
        foreach (var n in new[] { nameof(Day), nameof(DayText), nameof(Available), nameof(Unavailable), nameof(IsFinished), nameof(IsPlaying), nameof(Won),
                     nameof(AnswerItem), nameof(AttemptText), nameof(ResultTitle), nameof(ResultText), nameof(Search), nameof(WrongGuesses), nameof(ShowGiveUp), nameof(GaveUp) }) Raise(n);
        RaiseMore();
    }

    protected virtual void RaiseMore() { }
}
