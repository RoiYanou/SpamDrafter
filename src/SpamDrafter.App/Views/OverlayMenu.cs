using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SpamDrafter.App.Views;

/// <summary>
/// Aide commune des menus qui s'ouvrent par-dessus l'interface (fenêtres flottantes) : elles ne sont pas dans la transformation qui agrandit
/// l'interface (réglage « Taille de l'interface »), donc on leur applique la même échelle ; leur fond reprend celui du thème, rendu translucide.
/// </summary>
public static class OverlayMenu
{
    /// <summary>Échelle réellement affichée du contrôle (1,3 si l'interface est agrandie à 130 %).</summary>
    public static double ScaleOf(Visual v)
    {
        if (TopLevel.GetTopLevel(v) is not { } top || v.TransformToVisual(top) is not { } m) return 1;
        var s = Math.Sqrt(m.M11 * m.M11 + m.M12 * m.M12);
        return s < 0.5 ? 1 : s;
    }

    /// <summary>Cadre du menu : fond du thème à environ 88 % d'opacité, bord du thème.</summary>
    public static void StyleFrame(Control host, Border frame)
    {
        if (host.TryFindResource("BrushPanelSolid", host.ActualThemeVariant, out var res) && res is ISolidColorBrush p)
            frame.Background = new SolidColorBrush(Color.FromArgb(0xE0, p.Color.R, p.Color.G, p.Color.B));
        if (host.TryFindResource("BrushControlBorder", host.ActualThemeVariant, out var edge) && edge is IBrush e) frame.BorderBrush = e;
    }

    /// <summary>Met le menu à l'échelle de l'interface et à la largeur de l'élément sous lequel (ou au-dessus duquel) il s'ouvre.</summary>
    public static void Fit(Control target, LayoutTransformControl scaleBox, Control frame)
    {
        var s = ScaleOf(target);
        scaleBox.LayoutTransform = new ScaleTransform(s, s);
        frame.Width = target.Bounds.Width;
    }
}
