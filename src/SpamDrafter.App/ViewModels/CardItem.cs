using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.App.ViewModels;

/// <summary>Une carte telle qu'affichée (nom, textes traduits, images chargées à la demande).</summary>
public sealed class CardItem : ViewModelBase
{
    private readonly KnowledgeBase _kb;
    private Bitmap? _thumb;
    private bool _thumbRequested;

    public CardItem(Card card, KnowledgeBase kb)
    {
        Card = card; _kb = kb;
        Name = kb.CardName(card);
        ElementName = kb.ElementName(card.Element);
        RarityName = kb.RarityName(card.Rarity);
    }

    public Card Card { get; }
    public string Name { get; }
    public string ElementName { get; }
    public string RarityName { get; }
    public string Summary => $"{ElementName} · {RarityName} · {Card.Hp} PV";

    /// <summary>Miniature décodée en petit (évite de charger 100 Mo d'images en mémoire).</summary>
    public Bitmap? Thumbnail
    {
        get
        {
            if (!_thumbRequested) { _thumbRequested = true; _ = LoadThumbAsync(); }
            return _thumb;
        }
    }

    private async Task LoadThumbAsync()
    {
        var path = _kb.ImagePath(Card.Thumbnail);
        if (path is null) return;
        var bmp = await Task.Run(() =>
        {
            try { using var s = File.OpenRead(path); return Bitmap.DecodeToWidth(s, 120); }
            catch (Exception) { return null; }
        });
        await Dispatcher.UIThread.InvokeAsync(() => { _thumb = bmp; Raise(nameof(Thumbnail)); });
    }

    /// <summary>Illustration complète, décodée à une hauteur raisonnable pour la fiche détail.</summary>
    public Bitmap? LoadFullImage()
    {
        var path = _kb.ImagePath(Card.Image);
        if (path is null) return null;
        try { using var s = File.OpenRead(path); return Bitmap.DecodeToHeight(s, 640); }
        catch (Exception) { return null; }
    }

    public bool Matches(string text, string? element, string? rarity) =>
        (element is null || Card.Element == element) &&
        (rarity is null || Card.Rarity == rarity) &&
        (string.IsNullOrWhiteSpace(text) ||
         Name.Contains(text.Trim(), StringComparison.CurrentCultureIgnoreCase));
}
