using System.Collections.ObjectModel;
using SpamDrafter.Core.Games;

namespace SpamDrafter.App.ViewModels;

/// <summary>Une ligne du détail d'une partie de l'historique.</summary>
public sealed record GameHistoryLine(string Mark, string Color, string Text, string Detail)
{
    public bool HasDetail => Detail.Length > 0;
    /// <summary>Le détail, précédé d'un séparateur (vide s'il n'y en a pas).</summary>
    public string DetailSuffix => Detail.Length > 0 ? "  ·  " + Detail : "";
}

/// <summary>Une partie de l'historique d'un jeu quotidien : la date, le résultat, la carte à trouver et le détail (déplié à la demande).</summary>
public sealed class GameHistoryRow : ViewModelBase
{
    public GameHistoryRow(DailyHistoryEntry entry, CardItem? item)
    {
        Entry = entry; Item = item;
        DayText = DateOnly.ParseExact(entry.Day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).ToString("dddd d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        Lines = entry.Lines.Select(l => new GameHistoryLine(l.Mark > 0 ? "✔" : l.Mark < 0 ? "✘" : "•", l.Mark > 0 ? "#6EE08A" : l.Mark < 0 ? "#FF7A7A" : "#CCCCCC", l.Text, l.Detail)).ToList();
    }
    private bool _isOpen;
    /// <summary>Le détail est déplié sous la ligne.</summary>
    public bool IsOpen { get => _isOpen; set { if (Set(ref _isOpen, value)) Raise(nameof(Arrow)); } }
    public string Arrow => _isOpen ? "▴" : "▾";
    private RelayCommand? _toggle;
    public RelayCommand ToggleCommand => _toggle ??= new RelayCommand(_ => IsOpen = !IsOpen);
    public DailyHistoryEntry Entry { get; }
    public CardItem? Item { get; }
    public bool HasItem => Item is not null;
    public string DayText { get; }
    public string Headline => Entry.Headline;
    public string CardName => Entry.CardName;
    public string Color => Entry.Good ? "#6EE08A" : "#FF9F5A";
    public IReadOnlyList<GameHistoryLine> Lines { get; }
}

/// <summary>L'historique d'un jeu quotidien (énigme, détail, quizz) : ses parties passées avec leur date, les plus récentes d'abord.</summary>
public sealed class GameHistoryViewModel : ViewModelBase
{
    private readonly DailyHistory _history;
    private readonly string _path;
    private readonly string _game;
    private readonly IReadOnlyDictionary<string, CardItem> _items;

    public GameHistoryViewModel(string game, DailyHistory history, string path, IReadOnlyList<CardItem> items)
    {
        _game = game; _history = history; _path = path;
        _items = items.ToDictionary(i => i.Card.Id);
        Refresh();
    }

    public ObservableCollection<GameHistoryRow> Rows { get; } = new();
    public bool HasRows => Rows.Count > 0;
    public bool NoRows => Rows.Count == 0;

    /// <summary>Enregistre une partie terminée et met la liste à jour.</summary>
    public void Add(DailyHistoryEntry entry)
    {
        entry.Game = _game;
        entry.PlayedUtc = DateTime.UtcNow;
        _history.Record(entry);
        _history.TrySave(_path);
        Refresh();
    }

    public void Refresh()
    {
        Rows.Clear();
        foreach (var e in _history.For(_game)) Rows.Add(new GameHistoryRow(e, e.CardId is { } id ? _items.GetValueOrDefault(id) : null));
        Raise(nameof(HasRows)); Raise(nameof(NoRows));
    }
}
