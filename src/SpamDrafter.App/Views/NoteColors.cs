using Avalonia;
using Avalonia.Media;

namespace SpamDrafter.App.Views;

/// <summary>Couleur de chaque niveau de note : Parfait (multicolore), Excellent (bleu), Bon (vert), Passable (jaune), Faible (orange), Intankable (rouge).</summary>
public static class NoteColors
{
    public static readonly IBrush Rainbow = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops =
        [
            new GradientStop(Color.Parse("#FF5A5A"), 0.00), new GradientStop(Color.Parse("#FFB347"), 0.20),
            new GradientStop(Color.Parse("#FFE14A"), 0.40), new GradientStop(Color.Parse("#5FD86B"), 0.60),
            new GradientStop(Color.Parse("#4FA3FF"), 0.80), new GradientStop(Color.Parse("#C58BFF"), 1.00),
        ],
    };
    private static readonly IBrush Blue = new SolidColorBrush(Color.Parse("#4FA3FF"));
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#5FD86B"));
    private static readonly IBrush Yellow = new SolidColorBrush(Color.Parse("#FFE14A"));
    private static readonly IBrush Orange = new SolidColorBrush(Color.Parse("#FF9F5A"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#FF4D4D"));

    /// <summary>Le pinceau d'un niveau (« Parfait », « Excellent »…) ; blanc pour un mot inconnu.</summary>
    public static IBrush For(string label) => label switch
    {
        "Parfait" => Rainbow,
        "Excellent" => Blue,
        "Bon" => Green,
        "Passable" => Yellow,
        "Faible" => Orange,
        "Intankable" => Red,
        _ => Brushes.White,
    };

    /// <summary>Les niveaux, pour repérer le mot dans un texte.</summary>
    public const string Pattern = "Parfait|Excellent|Bon|Passable|Faible|Intankable";
}
