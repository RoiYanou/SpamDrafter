using System.Collections.ObjectModel;
using Avalonia.Threading;
using SpamDrafter.Core.Games;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.App.ViewModels;

/// <summary>Une ligne du détail de la note : la raison, la valeur concernée et les points gagnés.</summary>
public sealed record RngScoreRow(string Reason, string Value, string PointsText, bool IsBonus, double Bits)
{
    /// <summary>Couleur du gain selon la rareté de la coïncidence (gris, jaune, vert, bleu, violet, multicolore).</summary>
    public Avalonia.Media.IBrush Brush => SpamDrafter.App.Views.QualityColors.RarityBrush(Bits);
}

/// <summary>Un tirage de l'historique : la date, la note, le rang et le détail (déplié à la demande).</summary>
public sealed class RngHistoryRow : ViewModelBase
{
    public RngHistoryRow(RngHistoryEntry entry, CardItem? item)
    {
        Entry = entry; Item = item;
        DayText = DateOnly.ParseExact(entry.Day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).ToString("dddd d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        Lines = entry.Lines.AsEnumerable().Reverse().Select(l => new RngScoreRow(l.Reason, l.Value, "+" + l.Points, l.IsBonus, l.Bits > 0 ? l.Bits : l.Points / 500.0)).ToList();   // du plus grand au plus petit, comme à l'écran
    }
    private bool _isOpen;
    /// <summary>Le détail des gains est déplié sous la ligne.</summary>
    public bool IsOpen { get => _isOpen; set { if (Set(ref _isOpen, value)) Raise(nameof(Arrow)); } }
    public string Arrow => _isOpen ? "▴" : "▾";
    private RelayCommand? _toggle;
    public RelayCommand ToggleCommand => _toggle ??= new RelayCommand(_ => IsOpen = !IsOpen);
    public RngHistoryEntry Entry { get; }
    public CardItem? Item { get; }
    public string DayText { get; }
    public string Name => Entry.Name;
    public string Summary => Entry.Summary;
    public string ScoreText => $"{Entry.Total} points";
    public string RankText => Entry.Perfect ? "Top 0 %" : $"Top {SpamDrafter.App.Views.QualityColors.TopPercent(Entry.Percentile)} %";
    public string QualityTitle => Entry.Title;
    public Avalonia.Media.IBrush RankBrush => SpamDrafter.App.Views.QualityColors.BrushFor(Entry.Percentile, Entry.Perfect);
    public IReadOnlyList<RngScoreRow> Lines { get; }
}

/// <summary>
/// « Tirage du jour » (phase 16, lot D) : un clic crée une carte tirée au hasard (propre à chaque joueur) ; ses éléments apparaissent un par un sur
/// l'illustration pour former une carte du jeu, puis les points s'affichent du plus petit gain au plus grand avec leur raison, jusqu'à la note finale
/// et son rang par rapport à des cartes tirées au hasard. Le tirage est enregistré avec sa date dans l'historique.
/// </summary>
public sealed class RngViewModel : ViewModelBase
{
    private readonly KnowledgeBase _kb;
    private readonly IReadOnlyDictionary<string, CardItem> _itemsById;
    private readonly IReadOnlyDictionary<string, string> _abilityElements;
    private readonly DailyBook _book;
    private readonly string _bookPath;
    private readonly RngHistory _history;
    private readonly string _historyPath;
    private RngGame? _game;
    private DailyRecord _record = new();
    private DispatcherTimer? _timer;
    private int _built, _scored;
    private CardItem? _card;
    private bool _loaded;
    private readonly Func<ulong> _seedSource;
    private readonly bool _canPlay;

    public RngViewModel(KnowledgeBase kb, IReadOnlyList<CardItem> items, string knowledgeDirectory, DailyBook book, string bookPath, RngHistory history, string historyPath, bool deferLoad = false, Func<ulong>? seedSource = null)
    {
        _seedSource = seedSource ?? (() => BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)));
        _kb = kb; _itemsById = items.ToDictionary(i => i.Card.Id);
        _abilityElements = RngGame.LoadAbilityElements(knowledgeDirectory);
        _book = book; _bookPath = bookPath; _history = history; _historyPath = historyPath;
        Day = DailyClock.Today();
        _canPlay = kb.Cards.Count >= 2 && kb.Cards.Any(c => c.Abilities.Count > 0);
        if (deferLoad)
        {
            // Au démarrage de l'application, on ne charge rien : le calibrage du barème (12 000 cartes simulées) se prépare en arrière-plan,
            // et la carte du jour est construite à la première ouverture du jeu.
            var today = Day;
            _ = Task.Run(() => { try { RngGame.ForSeed(kb, 1, _abilityElements); } catch (Exception) { } });
        }
        else Load(Day);
    }

    /// <summary>Joue les animations (apparition des éléments, des points) ; désactivé dans les tests et quand les animations sont réduites.</summary>
    public bool Animate { get; set; }
    /// <summary>Délais des animations, en millisecondes.</summary>
    public int BuildDelay { get; set; } = 800;
    public int ScoreDelay { get; set; } = 420;
    /// <summary>Un élément de la carte vient d'être tiré (numéro de l'étape, de 1 à 9) : la fenêtre joue son animation d'apparition.</summary>
    public event Action<int>? StageRevealed;
    /// <summary>Un gain de points vient d'apparaître : la fenêtre fait « battre » le total.</summary>
    public event Action? ScoreStepped;
    /// <summary>Tous les points sont affichés : la fenêtre met le résultat en scène.</summary>
    public event Action? ResultShown;

    public DateOnly Day { get; private set; }
    public string DayText => Day.ToString("dddd d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
    public ObservableCollection<RngScoreRow> Scores { get; } = new();
    public ObservableCollection<RngHistoryRow> History { get; } = new();

    public bool Available => _canPlay;
    public bool Unavailable => !_canPlay;
    /// <summary>Le tirage a été lancé (en cours d'animation ou terminé).</summary>
    public bool Started => _record.Started;
    public bool CanStart => Available && !Started;
    /// <summary>Recharge le tirage du jour depuis les parties enregistrées.</summary>
    public void Reload() => Load(DailyClock.Today());
    public bool IsBuilding => Started && _game is not null && _built < _game.Parts.Count;
    public bool IsScoring => Started && _game is not null && _built >= _game.Parts.Count && _scored < _game.Lines.Count;
    public bool IsFinished => Started && _game is not null && _built >= _game.Parts.Count && _scored >= _game.Lines.Count;
    private bool _showHistory;
    /// <summary>On regarde l'historique des tirages au lieu de la carte (bouton « Historique » de l'en-tête, comme dans les autres jeux).</summary>
    public bool ShowHistory { get => _showHistory; set { if (Set(ref _showHistory, value)) { Raise(nameof(ShowGame)); Raise(nameof(HistoryButtonText)); } } }
    public bool ShowGame => !_showHistory;
    public string HistoryButtonText => _showHistory ? "Retour au jeu" : "Historique";
    private RelayCommand? _toggleHistory;
    public RelayCommand ToggleHistoryCommand => _toggleHistory ??= new RelayCommand(_ => ShowHistory = !ShowHistory);
    public bool HasHistory => History.Count > 0;
    public bool NoHistory => History.Count == 0;

    // La carte qui se forme (les drapeaux pilotent l'affichage de la vraie carte du jeu).
    public CardItem? Card => _card;
    public bool ShowCardFace => _built >= 1;   // l'illustration est tirée en premier : la carte se forme dessus
    public bool ShowName => _built >= 2;
    public bool ShowRarity => _built >= 3;
    public bool ShowElement => _built >= 4;
    public bool ShowIncome => _built >= 5;     // Spamtons
    public bool ShowStats => _built >= 6;      // PV, puis en dessous les compétences au fur et à mesure
    /// <summary>La carte est entièrement créée : elle réagit au survol comme dans le jeu.</summary>
    public bool CardComplete => _game is not null && _built >= _game.Parts.Count;

    public int ShownTotal => _game is null ? 0 : _game.Lines.Take(_scored).Sum(l => l.Points);
    public string ShownTotalText => $"{ShownTotal} points";
    private int _displayed;
    /// <summary>Total affiché : il monte rapidement jusqu'à ShownTotal à chaque gain.</summary>
    public int DisplayedTotal => _displayed;
    public string DisplayedTotalText => $"{_displayed} points";
    public string FinalTitle => _game?.Title ?? "";
    /// <summary>Couleur de la qualité de la carte (titre, score, classement).</summary>
    public Avalonia.Media.IBrush QualityBrush => _game is null ? Avalonia.Media.Brushes.White : SpamDrafter.App.Views.QualityColors.BrushFor(_game.Percentile, _game.IsPerfect);
    /// <summary>Classement : « Top 12 % ».</summary>
    public string TopText => _game is null ? "" : _game.IsPerfect ? "Top 0 %" : $"Top {SpamDrafter.App.Views.QualityColors.TopPercent(_game.Percentile)} %";
    public string DrawPointsText => _game is null ? "" : $"{_game.Parts.Count} tirages : {_game.DrawPoints} points · {_game.Bonuses.Count} bonus de cohérence : {_game.BonusPoints} points";
    /// <summary>Affiché à côté de la date du jour une fois le tirage fait.</summary>
    public string NextDrawText => "Nouveau tirage demain";

    private RelayCommand? _start;
    public RelayCommand StartCommand => _start ??= new RelayCommand(_ => StartDraw());

    /// <summary>Recharge la partie si le jour (heure de Paris) a changé depuis l'affichage.</summary>
    public void Refresh()
    {
        var today = DailyClock.Today();
        if ((!_loaded || today != Day) && !IsBuilding && !IsScoring) Load(today);
    }

    private void Load(DateOnly day)
    {
        _timer?.Stop();
        _loaded = true;
        Day = day;
        _record = _book.For(RngGame.GameId, day);
        // Un tirage déjà fait (même interrompu) est retrouvé grâce à sa graine, et montré en entier.
        _game = _record.Started && _record.Guesses.Count > 0 && ulong.TryParse(_record.Guesses[0], out var seed) ? RngGame.ForSeed(_kb, seed, _abilityElements) : null;
        if (_game is null && _record.Started) { _record.Started = false; _record.Finished = false; _record.Guesses.Clear(); }
        _built = _game?.Parts.Count ?? 0;
        _scored = _game?.Lines.Count ?? 0;
        _countTimer?.Stop(); _displayed = ShownTotal;
        RebuildCard(); RebuildScores(); RebuildHistory();
        RaiseAll();
    }

    private void StartDraw()
    {
        if (!CanStart) return;
        var seed = _seedSource();                       // chaque tirage est au hasard : deux joueurs n'ont jamais la même carte
        _game = RngGame.ForSeed(_kb, seed, _abilityElements);
        if (_game is null) return;
        _record.Guesses.Clear(); _record.Guesses.Add(seed.ToString());
        _record.Started = true; _record.Finished = true; _record.Won = _game.Percentile >= 60;
        _book.TrySave(_bookPath);
        // Le tirage est enregistré dans l'historique avec sa date dès qu'il est lancé.
        _history.Record(RngHistory.EntryFor(_game, Day, _kb.CardName(_game.NameCard), DateTime.UtcNow));
        _history.TrySave(_historyPath);
        RebuildHistory();
        if (!Animate) { _built = _game.Parts.Count; _scored = _game.Lines.Count; _displayed = ShownTotal; RebuildCard(); RebuildScores(); RaiseAll(); return; }
        _built = 1; _scored = 0; _displayed = 0;
        RebuildCard(); RebuildScores(); RaiseAll();
        StageRevealed?.Invoke(_built);
        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(BuildDelay) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_game is null) { _timer?.Stop(); return; }
        if (_built < _game.Parts.Count)
        {
            _built++;
            if (_built == _game.Parts.Count) _timer!.Interval = TimeSpan.FromMilliseconds(ScoreDelay + 500);   // petite pause avant le décompte des points
            RebuildCard(); RaiseAll();
            StageRevealed?.Invoke(_built);
        }
        else if (_scored < _game.Lines.Count)
        {
            _timer!.Interval = TimeSpan.FromMilliseconds(ScoreDelay);
            var l = _game.Lines[_scored];
            _scored++;
            Scores.Insert(0, new RngScoreRow(l.Reason, l.Value, "+" + l.Points, l.IsBonus, l.Bits));   // le dernier gain s'ajoute en haut
            CountUpTo(ShownTotal);
            RaiseAll();
            ScoreStepped?.Invoke();
        }
        if (_built >= _game.Parts.Count && _scored >= _game.Lines.Count)
        {
            _timer?.Stop();
            RaiseAll();
            ResultShown?.Invoke();
        }
    }

    private DispatcherTimer? _countTimer;
    /// <summary>Fait monter le total affiché jusqu'à <paramref name="target"/> en un tiers de seconde (rapide au début, doux à l'arrivée).</summary>
    private void CountUpTo(int target)
    {
        _countTimer?.Stop();
        var from = _displayed; var start = DateTime.UtcNow;
        _countTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _countTimer.Tick += (_, _) =>
        {
            var u = Math.Clamp((DateTime.UtcNow - start).TotalSeconds / 0.32, 0, 1);
            _displayed = from + (int)Math.Round((target - from) * (1 - Math.Pow(1 - u, 3)));
            if (u >= 1) { _displayed = target; _countTimer!.Stop(); }
            Raise(nameof(DisplayedTotal)); Raise(nameof(DisplayedTotalText));
        };
        _countTimer.Start();
    }

    /// <summary>La vraie carte du jeu, avec ce qui est déjà tiré : l'élément, le nom, l'illustration, les PV, les Spamtons puis les compétences.</summary>
    private void RebuildCard()
    {
        if (_game is null || _built == 0) { _card = null; return; }
        var abilities = new List<string>(); var tox = new List<string>();
        for (var i = 6; i < _built && i < _game.Parts.Count; i++)
        {
            var p = _game.Parts[i];
            if (p.Kind == RngPartKind.Ability) abilities.Add(p.ItemId!);
            else if (p.Kind == RngPartKind.Tox) tox.Add(p.ItemId!);
        }
        var n = _game.NameCard; var ill = _game.IllustrationCard;
        var gold = int.Parse(_game.Parts[4].Value); var hp = int.Parse(_game.Parts[5].Value);
        var card = new Card("tirage-" + SpamDrafter.Core.Games.DailyClock.Key(Day), 0, n.SanitizedName, n.NameKey, _game.Parts[3].ItemId!, _built >= 3 ? _game.Parts[2].ItemId! : _kb.Rarities[0].Id, hp, gold,
            n.Resistances, abilities, [], ill.Image, ill.Thumbnail, tox, ill.Artist, null);
        _card = new CardItem(card, _kb);
    }

    private void RebuildScores()
    {
        Scores.Clear();
        if (_game is null) return;
        foreach (var l in _game.Lines.Take(_scored)) Scores.Insert(0, new RngScoreRow(l.Reason, l.Value, "+" + l.Points, l.IsBonus, l.Bits));   // le dernier gain s'ajoute en haut
    }

    private void RebuildHistory()
    {
        History.Clear();
        foreach (var e in _history.Entries) History.Add(new RngHistoryRow(e, _itemsById.GetValueOrDefault(e.IllustrationCardId)));
    }

    private void RaiseAll()
    {
        foreach (var n in new[] { nameof(Day), nameof(DayText), nameof(Available), nameof(Unavailable), nameof(Started), nameof(CanStart), nameof(IsBuilding), nameof(IsScoring), nameof(IsFinished),
                     nameof(HasHistory), nameof(NoHistory), nameof(Card), nameof(ShowCardFace), nameof(ShowElement), nameof(ShowName), nameof(ShowRarity), nameof(ShowStats), nameof(ShowIncome),
                     nameof(ShownTotal), nameof(ShownTotalText), nameof(DisplayedTotal), nameof(DisplayedTotalText), nameof(FinalTitle), nameof(QualityBrush), nameof(TopText), nameof(CardComplete), nameof(DrawPointsText) })
            Raise(n);
    }
}
