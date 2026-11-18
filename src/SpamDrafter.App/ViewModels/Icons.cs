using System.Collections.Concurrent;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.App.ViewModels;

/// <summary>Une icône suivie d'un texte (compteurs du deck, résistances).</summary>
public sealed record IconText(Bitmap? Icon, string Text);

/// <summary>
/// Icônes du jeu (éléments, raretés, Tox) : chargées à la demande depuis la base de connaissances, décodées à la taille d'affichage
/// et gardées en mémoire. Une icône absente (base plus ancienne, fichier manquant) donne null : l'écran s'affiche sans.
/// </summary>
public static class Icons
{
    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new();

    public static Bitmap? Load(KnowledgeBase kb, string? relative, int height = 64)
    {
        if (string.IsNullOrEmpty(relative) || kb.ImagePath(relative) is not { } path) return null;
        return Cache.GetOrAdd($"{path}|{height}", _ =>
        {
            try { using var s = File.OpenRead(path); return Bitmap.DecodeToHeight(s, height); }
            catch (Exception) { return null; }
        });
    }

    public static Bitmap? Element(KnowledgeBase kb, string? elementId, int height = 40) =>
        elementId is null ? null : Load(kb, kb.Element(elementId)?.Icon, height);

    public static Bitmap? Rarity(KnowledgeBase kb, string? rarityId, int height = 40) =>
        rarityId is null ? null : Load(kb, kb.Rarity(rarityId)?.Icon, height);

    public static Bitmap? Tox(KnowledgeBase kb, ToxEntry tox, int height = 96) => Load(kb, tox.Image, height);

    /// <summary>Couleur « #RRGGBB » d'une rareté (jeu : <c>_primaryColor</c>) ; gris neutre si la base n'en donne pas.</summary>
    public static IBrush RarityBrush(KnowledgeBase kb, string? rarityId) => Brush(kb.Rarity(rarityId ?? "")?.Color, "#666666");

    /// <summary>Couleur de texte d'une rareté (jeu : <c>_primaryTextColor</c>).</summary>
    public static IBrush RarityTextBrush(KnowledgeBase kb, string? rarityId) => Brush(kb.Rarity(rarityId ?? "")?.TextColor, "#CCCCCC");

    /// <summary>
    /// Couleur du cadre d'une carte : celle de la rareté du jeu, éclaircie (le bleu des cartes rares était trop sombre sur le fond violet) ;
    /// la commune prend le gris clair du jeu.
    /// </summary>
    public static IBrush FrameBrush(KnowledgeBase kb, string? rarityId)
    {
        var order = kb.Rarity(rarityId ?? "")?.Order ?? 1;
        if (order <= 1) return RarityTextBrush(kb, rarityId);
        var c = ((SolidColorBrush)RarityBrush(kb, rarityId)).Color;
        double t = 0.42;
        return new SolidColorBrush(Color.FromRgb((byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t)));
    }

    /// <summary>Icône du Spamton (la monnaie du jeu), à côté du revenu d'une carte ; null si la base ne la contient pas.</summary>
    public static Bitmap? Spamton(KnowledgeBase kb, int height = 40) => Load(kb, "images/icons/spamton.webp", height);

    /// <summary>Couleur d'interface d'un élément (choix de présentation, absent de nos données) ; blanc si inconnu.</summary>
    public static IBrush ElementAccent(string? elementId) => new SolidColorBrush(Color.Parse(elementId switch
    {
        "fire" => "#FF7043", "water" => "#4FB3FF", "earth" => "#C08A55", "nature" => "#6FD65A",
        "light" => "#FFD84A", "shadow" => "#A57BFF", "divine" => "#FF9AE6", _ => "#FFFFFF",
    }));

    /// <summary>La même couleur avec une transparence donnée (0 à 255).</summary>
    public static IBrush WithAlpha(IBrush brush, byte alpha) =>
        brush is ISolidColorBrush b ? new SolidColorBrush(Color.FromArgb(alpha, b.Color.R, b.Color.G, b.Color.B)) : brush;

    private static IBrush Brush(string? hex, string fallback) =>
        new SolidColorBrush(Color.TryParse(hex ?? fallback, out var c) ? c : Color.Parse(fallback));
}
