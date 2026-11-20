using System.Collections.ObjectModel;
using SpamDrafter.Core.Games;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.App.ViewModels;

/// <summary>
/// « Énigme du jour » (phase 16, lot A) : une phrase, une carte à trouver, sans limite d'essais ni indices ; « Abandonner » donne la réponse.
/// Une partie par jour (heure de Paris) ; la partie en cours est enregistrée et reprise si on ferme l'application.
/// </summary>
public sealed class RiddleViewModel : CardGuessViewModel
{
    private readonly KnowledgeBase _kb;
    private readonly IReadOnlyList<Riddle> _riddles;
    private RiddleGame? _game;

    public RiddleViewModel(KnowledgeBase kb, IReadOnlyList<CardItem> items, string knowledgeDirectory, DailyBook book, string bookPath, GameHistoryViewModel history)
        : base(items, book, bookPath, RiddleGame.GameId, int.MaxValue, history)
    {
        _kb = kb;
        _riddles = RiddleGame.LoadRiddles(knowledgeDirectory);
        Start();
    }

    public string Text => _game?.Text ?? "";

    protected override string? AnswerCardId => _game?.Answer.Id;
    protected override string GameName => "énigme";
    protected override void PrepareDay(DateOnly day) => _game = RiddleGame.ForDay(_kb, _riddles, day);
    protected override void GuessesChanged() { }
    public override bool CanGiveUp => true;
    public override string NextGameText => "Nouvelle énigme demain";
    protected override void RaiseMore() => Raise(nameof(Text));
}
