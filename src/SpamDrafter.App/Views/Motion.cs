namespace SpamDrafter.App.Views;

/// <summary>
/// Réglage « Réduire les animations » (phase 13, D-078) : vrai = pas d'inclinaison, de retournement ni d'étoiles qui scintillent.
/// Un seul interrupteur pour toutes les animations ; les vues le consultent, elles ne gardent pas leur propre réglage.
/// </summary>
public static class Motion
{
    private static bool _reduce;

    public static bool Reduce
    {
        get => _reduce;
        set { _reduce = value; StarField.Animate = !value; }
    }
}
