using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using SpamDrafter.Core.Games;

namespace SpamDrafter.App.ViewModels;

/// <summary>Une réponse proposée : neutre tant qu'on n'a pas répondu, puis verte (la bonne) ou rouge (celle choisie à tort).</summary>
public sealed class QuizOption : ViewModelBase
{
    public QuizOption(int index, string text) { Index = index; Text = text; }
    public int Index { get; }
    public string Text { get; }
    private string _background = "#33FFFFFF";
    public string Background { get => _background; set => Set(ref _background, value); }
}

/// <summary>Une ligne du récapitulatif de fin de partie.</summary>
public sealed record QuizRecap(string Question, string Chosen, string Correct, bool Right)
{
    public string Mark => Right ? "✔" : "✘";
    public string Color => Right ? "#6EE08A" : "#FF7A7A";
    public bool ShowCorrect => !Right;
}

/// <summary>
/// « Quizz du jour » (phase 16, lot C) : 5 questions à 4 réponses, les mêmes pour tous. La réponse est validée dès qu'on clique dessus ; les bonnes réponses et les erreurs ne sont annoncées qu'à la fin.
/// Une partie par jour (heure de Paris). Pour éviter la triche, une fois lancé le quizz <b>verrouille les onglets</b> jusqu'à la fin ; le quitter
/// (bouton « Abandonner », fermeture de l'application) compte les questions sans réponse comme fausses.
/// Les questions qui parlent d'une carte montrent son illustration seule (jamais ses PV, son élément, etc.).
/// </summary>
public sealed class QuizViewModel : ViewModelBase
{
    private readonly IReadOnlyList<QuizQuestion> _bank;
    private readonly IReadOnlyDictionary<string, CardItem> _cards;
    private readonly DailyBook _book;
    private readonly string _bookPath;
    private QuizGame? _game;
    private DailyRecord _record = new();
    private bool _feedback;   // la dernière réponse vient d'être donnée : on montre la correction avant de passer à la suite
    private Bitmap? _image;
    private string? _imageCard;

    public QuizViewModel(string knowledgeDirectory, IReadOnlyList<CardItem> items, DailyBook book, string bookPath, GameHistoryViewModel history)
    {
        History = history;
        _bank = QuizGame.Load(knowledgeDirectory);
        _cards = items.GroupBy(i => i.Card.SanitizedName).ToDictionary(g => g.Key, g => g.First());
        _book = book; _bookPath = bookPath;
        Load(DailyClock.Today());
    }

    /// <summary>Le verrouillage des onglets change (pour que la fenêtre le sache).</summary>
    public event Action? LockChanged;

    public DateOnly Day { get; private set; }
    /// <summary>L'historique du quizz (parties passées, avec leur date).</summary>
    public GameHistoryViewModel History { get; }
    private bool _showHistory;
    public bool ShowHistory { get => _showHistory; set { if (Set(ref _showHistory, value)) { Raise(nameof(ShowGame)); Raise(nameof(HistoryButtonText)); } } }
    public bool ShowGame => !_showHistory;
    public string HistoryButtonText => _showHistory ? "Retour au jeu" : "Historique";
    private RelayCommand? _toggleHistory;
    public RelayCommand ToggleHistoryCommand => _toggleHistory ??= new RelayCommand(_ => { if (!IsLocked) ShowHistory = !ShowHistory; });
    public string DayText => Day.ToString("dddd d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
    public ObservableCollection<QuizOption> Options { get; } = new();
    public ObservableCollection<QuizRecap> Recap { get; } = new();

    public bool Available => _game is not null;
    public bool Unavailable => _game is null;
    private int Answered => _record.Guesses.Count;
    /// <summary>Le quizz est fini (toutes les questions répondues ou laissées fausses) et la correction de la dernière est passée.</summary>
    public bool IsFinished => Available && Answered >= QuizGame.Count && !_feedback;
    /// <summary>Avertissement avant de commencer.</summary>
    public bool ShowIntro => Available && !_record.Started && !_record.Finished;
    /// <summary>Quizz en cours : les onglets sont verrouillés.</summary>
    public bool IsPlaying => Available && _record.Started && !IsFinished;
    public bool IsLocked => IsPlaying;
    public bool NotLocked => !IsLocked;
    /// <summary>La correction de la question qui vient d'être répondue est affichée.</summary>
    public bool ShowFeedback => _feedback;
    public bool CanAnswer => IsPlaying && !_feedback;

    private int ShownIndex => _feedback ? Answered - 1 : Math.Min(Answered, QuizGame.Count - 1);
    public QuizQuestion? Current => _game is null ? null : _game.Questions[ShownIndex];
    public string QuestionText => Current?.Question ?? "";
    /// <summary>Illustration de la carte dont parle la question (null si elle ne parle d'aucune carte précise).</summary>
    public Bitmap? CardImage => IsPlaying ? _image : null;
    public bool HasCardImage => CardImage is not null;
    public string ProgressText => $"Question {ShownIndex + 1} sur {QuizGame.Count}";
    public string FeedbackTitle => _feedback && IsRight(ShownIndex) ? "Bonne réponse !" : "Raté…";
    public string FeedbackText => _feedback ? Current?.Explanation ?? "" : "";
    public string NextLabel => Answered >= QuizGame.Count ? "Voir mon score" : "Question suivante";
    public int Score => _game?.Score(_record.Guesses) ?? 0;
    public string ScoreText => $"{Score} sur {QuizGame.Count}";
    public string ResultTitle => Score switch { QuizGame.Count => "Sans faute !", >= 3 => "Bien joué !", _ => "Tu peux mieux faire…" };
    /// <summary>Affiché à côté de la date une fois le quizz fini.</summary>
    public string NextGameText => "Nouveau quizz demain";

    private bool IsRight(int i) => _game is not null && i < _record.Guesses.Count && int.TryParse(_record.Guesses[i], out var a) && a == _game.Questions[i].Answer;

    private RelayCommand? _answer, _next, _start, _abandon;
    public RelayCommand AnswerCommand => _answer ??= new RelayCommand(p => { if (p is QuizOption o) Answer(o.Index); });
    public RelayCommand NextCommand => _next ??= new RelayCommand(_ => { _feedback = false; Rebuild(); });
    /// <summary>Le joueur a lu l'avertissement : le quizz commence et les onglets se verrouillent.</summary>
    public RelayCommand StartCommand => _start ??= new RelayCommand(_ =>
    {
        if (!ShowIntro) return;
        _showHistory = false;
        _record.Started = true;
        _book.TrySave(_bookPath);
        Rebuild();
    });
    /// <summary>Abandonne le quizz : les questions sans réponse comptent comme fausses.</summary>
    public RelayCommand AbandonCommand => _abandon ??= new RelayCommand(_ => { if (IsPlaying) Abandon(); });

    /// <summary>Recharge la partie si le jour (heure de Paris) a changé depuis l'affichage.</summary>
    public void Refresh()
    {
        var today = DailyClock.Today();
        if (today != Day && !IsLocked) Load(today);
    }

    private void Load(DateOnly day)
    {
        Day = day;
        _game = QuizGame.ForDay(_bank, day);
        _record = _book.For(QuizGame.GameId, day);
        _feedback = false;
        // Un quizz lancé puis interrompu (application fermée) : les questions restantes sont fausses, la partie est finie.
        if (_game is not null && _record is { Started: true, Finished: false }) { Abandon(); return; }
        Rebuild();
    }

    private void Abandon()
    {
        while (_record.Guesses.Count < QuizGame.Count) _record.Guesses.Add("-1");
        _record.Finished = true; _record.Won = Score >= 3;
        _feedback = false;
        _book.TrySave(_bookPath);
        RecordHistory(abandoned: true);
        Rebuild();
    }

    private void Answer(int option)
    {
        if (_game is null || !CanAnswer || option is < 0 or > 3) return;
        _record.Guesses.Add(option.ToString());
        if (_record.Guesses.Count >= QuizGame.Count) { _record.Finished = true; _record.Won = Score >= 3; RecordHistory(abandoned: false); }
        _book.TrySave(_bookPath);
        Rebuild();   // la réponse est enregistrée sans être corrigée : les bonnes réponses et les erreurs ne sont annoncées qu'à la fin
    }

    /// <summary>Recharge le quizz du jour depuis les parties enregistrées.</summary>
    public void Reload() { _feedback = false; Load(DailyClock.Today()); }

    /// <summary>Enregistre la partie terminée dans l'historique du quizz.</summary>
    private void RecordHistory(bool abandoned)
    {
        if (_game is null) return;
        var lines = new List<DailyHistoryLine>();
        for (var i = 0; i < _game.Questions.Count; i++)
        {
            var q = _game.Questions[i];
            var chosen = int.TryParse(_record.Guesses[i], out var c) && c is >= 0 and < 4 ? q.Options[c] : "Pas de réponse";
            lines.Add(new DailyHistoryLine { Text = q.Question, Detail = IsRight(i) ? "Réponse : " + chosen : $"Ta réponse : {chosen} · Bonne réponse : {q.Options[q.Answer]}", Mark = IsRight(i) ? 1 : -1 });
        }
        History.Add(new DailyHistoryEntry { Day = DailyClock.Key(Day), Good = Score >= 3, Headline = $"{Score} sur {QuizGame.Count}" + (abandoned ? " (abandon)" : ""), Lines = lines });
    }

    private void Rebuild()
    {
        var wasLocked = _wasLocked;
        Options.Clear();
        Recap.Clear();
        if (_game is not null)
        {
            var q = _game.Questions[ShownIndex];
            for (var i = 0; i < 4; i++)
            {
                var o = new QuizOption(i, q.Options[i]);
                if (_feedback)
                {
                    var chosen = int.Parse(_record.Guesses[ShownIndex]);
                    if (i == q.Answer) o.Background = "#886EE08A";
                    else if (i == chosen) o.Background = "#88FF7A7A";
                }
                Options.Add(o);
            }
            // Illustration de la carte de la question (chargée une fois par carte).
            if (q.Card is { } cardName && _cards.TryGetValue(cardName, out var item))
            {
                if (_imageCard != cardName) { _image = item.LoadBaseImage(480); _imageCard = cardName; }
            }
            else { _image = null; _imageCard = null; }
            if (IsFinished)
                for (var i = 0; i < _game.Questions.Count; i++)
                {
                    var qq = _game.Questions[i];
                    var chosen = int.TryParse(_record.Guesses[i], out var c) && c is >= 0 and < 4 ? qq.Options[c] : "Pas de réponse";
                    Recap.Add(new QuizRecap(qq.Question, chosen, qq.Options[qq.Answer], IsRight(i)));
                }
        }
        foreach (var n in new[] { nameof(Day), nameof(DayText), nameof(Available), nameof(Unavailable), nameof(IsFinished), nameof(IsPlaying), nameof(ShowIntro), nameof(IsLocked), nameof(NotLocked),
                     nameof(ShowFeedback), nameof(CanAnswer), nameof(Current), nameof(QuestionText), nameof(CardImage), nameof(HasCardImage), nameof(ProgressText), nameof(FeedbackTitle),
                     nameof(FeedbackText), nameof(NextLabel), nameof(Score), nameof(ScoreText), nameof(ResultTitle), nameof(ShowHistory), nameof(ShowGame) })
            Raise(n);
        _wasLocked = IsLocked;
        if (wasLocked != IsLocked) LockChanged?.Invoke();
    }

    private bool _wasLocked;
}
