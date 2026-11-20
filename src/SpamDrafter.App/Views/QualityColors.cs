using Avalonia.Media;

namespace SpamDrafter.App.Views;

/// <summary>
/// Couleurs du Tirage du jour. <b>Qualité de la carte (score et classement)</b> : rouge (les pires), gris, jaune, vert, bleu, violet, puis multicolore (la carte parfaite, « Top 0 % »).
/// <b>Rareté d'un gain de points</b> : gris (très courant), jaune, vert, bleu, violet, multicolore (très rare), selon la probabilité de la coïncidence.
/// </summary>
public static class QualityColors
{
    private static readonly IBrush Gray = new SolidColorBrush(Color.Parse("#B4B4C0"));
    private static readonly IBrush Violet = new SolidColorBrush(Color.Parse("#C58BFF"));

    /// <summary>Couleur d'une carte d'après son rang (<paramref name="percentile"/> : part des cartes tirées au hasard qui font moins bien).</summary>
    public static IBrush BrushFor(double percentile, bool perfect) =>
        perfect ? NoteColors.Rainbow
        : percentile > 97 ? Violet
        : percentile > 85 ? NoteColors.For("Excellent")
        : percentile > 60 ? NoteColors.For("Bon")
        : percentile > 30 ? NoteColors.For("Passable")
        : percentile > 2 ? Gray
        : NoteColors.For("Intankable");

    /// <summary>Couleur d'un gain de points selon sa rareté en « bits » (log₂ de 1 / probabilité) : 1 bit = une chance sur 2, 10 bits = une sur 1 000.</summary>
    public static IBrush RarityBrush(double bits) =>
        bits >= 9 ? NoteColors.Rainbow
        : bits >= 7 ? Violet
        : bits >= 5 ? NoteColors.For("Excellent")
        : bits >= 3 ? NoteColors.For("Bon")
        : bits >= 1.5 ? NoteColors.For("Passable")
        : Gray;

    /// <summary>« Top x % » : la part des cartes tirées au hasard qui font mieux (de 1 à 100 %).</summary>
    public static int TopPercent(double percentile) => Math.Clamp((int)Math.Ceiling(100 - percentile), 1, 100);
}
