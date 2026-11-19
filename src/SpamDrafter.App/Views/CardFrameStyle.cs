namespace SpamDrafter.App.Views;

/// <summary>Contour des cartes : à la couleur de leur rareté (par défaut), noir, multicolore, ou à la couleur secondaire du thème.</summary>
public enum CardFrameMode { Rarity, Black, Multicolor, Theme }

/// <summary>Réglage « Contour des cartes » : un seul réglage lu par toutes les cartes, qui se redessinent à chaque changement.</summary>
public static class CardFrameStyle
{
    public static CardFrameMode Mode { get; private set; } = CardFrameMode.Rarity;
    public static event Action? Changed;

    public static void Set(string? id)
    {
        var mode = id switch { "black" => CardFrameMode.Black, "multicolor" => CardFrameMode.Multicolor, "theme" => CardFrameMode.Theme, _ => CardFrameMode.Rarity };
        if (mode == Mode) return;
        Mode = mode;
        Changed?.Invoke();
    }

    public static IReadOnlyList<(string Id, string Label)> Options { get; } = new[] { ("rarity", "Par rareté"), ("black", "Noir"), ("multicolor", "Multicolore"), ("theme", "Couleur du thème") };
}
