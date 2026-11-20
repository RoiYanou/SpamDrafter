using Avalonia.Media.Imaging;
using SpamDrafter.Core.Games;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.App.ViewModels;

/// <summary>
/// « Détail du jour » (phase 16, lot B) : une zone très zoomée de l'illustration d'une carte, à retrouver en 6 essais ;
/// chaque erreur dézoome un peu, et l'illustration entière apparaît à la fin de la partie.
/// </summary>
public sealed class DetailViewModel : CardGuessViewModel
{
    private readonly KnowledgeBase _kb;
    private DetailGame? _game;
    private Bitmap? _image;
    private string? _imageCard;

    public DetailViewModel(KnowledgeBase kb, IReadOnlyList<CardItem> items, DailyBook book, string bookPath, GameHistoryViewModel history)
        : base(items, book, bookPath, DetailGame.GameId, int.MaxValue, history)
    {
        _kb = kb;
        Start();
    }

    /// <summary>Illustration de la carte du jour (version de base, jamais un skin).</summary>
    public Bitmap? Image => _image;
    /// <summary>Zone visible de l'illustration (fixe tant que la partie dure, l'image entière à la fin).</summary>
    public DetailWindow Window => _game?.WindowFor(IsFinished) ?? new DetailWindow(0, 0, 1, 1);
    public override bool CanGiveUp => true;
    public override bool ShowAnswerCard => false;

    protected override string? AnswerCardId => _game?.Answer.Id;
    protected override string GameName => "détail";
    public override string NextGameText => "Nouveau détail demain";

    protected override void PrepareDay(DateOnly day)
    {
        _game = DetailGame.ForDay(_kb, day);
        if (_game is null) { _image = null; _imageCard = null; return; }
        if (_imageCard == _game.Answer.Id) return;
        // Version pleine taille (2048 px de haut) quand les données la contiennent : le zoom reste net ; sinon l'illustration habituelle.
        _image = LoadDetailImage(_game.Answer) ?? AnswerItem?.LoadBaseImage(1024);
        _imageCard = _game.Answer.Id;
    }

    private Bitmap? LoadDetailImage(Card card)
    {
        var path = _kb.ImagePath($"images/detail/{card.SanitizedName}.webp");
        if (path is null || !File.Exists(path)) return null;
        try { using var s = File.OpenRead(path); return new Bitmap(s); }
        catch (Exception) { return null; }
    }

    protected override void GuessesChanged() { }
    protected override void RaiseMore() { Raise(nameof(Image)); Raise(nameof(Window)); }
}
