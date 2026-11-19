using Avalonia.Media;

namespace SpamDrafter.App.Views;

/// <summary>Les deux couleurs de l'onglet Tester, mode « Comparer 2 decks » : une pour le deck A, une pour le deck B (boutons, noms, texte du résultat).</summary>
public static class CompareColors
{
    public static readonly Color AColor = Color.Parse("#FF9F5A");   // orange
    public static readonly Color BColor = Color.Parse("#5AB0FF");   // bleu
    public static readonly IBrush A = new SolidColorBrush(AColor);
    public static readonly IBrush B = new SolidColorBrush(BColor);
}
