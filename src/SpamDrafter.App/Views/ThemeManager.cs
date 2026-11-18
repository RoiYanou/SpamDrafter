using Avalonia;
using Avalonia.Media;

namespace SpamDrafter.App.Views;

/// <summary>
/// Thème de couleur (phase 13) : le violet « Nuit étoilée » par défaut, ou un thème par élément (feu, lumière, terre, nature, ombre, eau)
/// qui change le fond, les panneaux et les bordures ; le jaune des actions reste. Le contraste élevé rend les panneaux opaques et
/// éclaircit bordures et textes secondaires. Les couleurs sont changées dans les pinceaux déclarés dans <c>Styles/Theme.axaml</c> :
/// toutes les vues les utilisent, aucune n'a de couleur en dur.
/// </summary>
public static class ThemeManager
{
    /// <summary>Night, Panel, Violet = fonds ; Edge1-3 = teintes douces des dégradés ; Accent / AccentEnd = couleur secondaire (haut et bas des boutons principaux).</summary>
    private sealed record Palette(string Night, string Panel, string Violet, string Edge1, string Edge2, string Edge3, string Accent, string AccentEnd);

    /// <summary>Palettes (🟡 valeurs de départ, à ajuster à l'œil).</summary>
    private static readonly Dictionary<string, Palette> Palettes = new()
    {
        // Night, Panel, Violet = fonds ; Edge1-3 = les trois teintes douces des dégradés (contours, boutons, sélections).
        ["default"] = new("#1A1033", "#2B1B52", "#4B2E8C", "#9A7BFF", "#E070D0", "#5BC0F0", "#FFC93C", "#FFA43A"),
        ["fire"] = new("#2A0E0A", "#4A1A12", "#A03E20", "#FF9A5A", "#E5565D", "#FFB347", "#4FC7D4", "#2FA3C4"),
        ["light"] = new("#2B2610", "#51461A", "#A88F2A", "#FFE08A", "#FFC15A", "#FFF3C4", "#A596FF", "#7A68E8"),
        ["earth"] = new("#241808", "#463014", "#8C6A2E", "#E0A56A", "#A9C46E", "#D9B98A", "#6BBBDA", "#4A94BA"),
        ["nature"] = new("#0B2412", "#17401F", "#2F8A44", "#7BE08A", "#C9E36A", "#4FC9B0", "#F5A0BE", "#E27A9F"),
        ["shadow"] = new("#0C0A16", "#211A38", "#5A4A8A", "#A18CF0", "#E070C8", "#6EA8F0", "#BDE56F", "#8FC94A"),
        ["water"] = new("#0A1A33", "#12305A", "#1F63B0", "#5BC0F0", "#4FD6C8", "#9AA8FF", "#FFAA70", "#F28A4A"),
    };

    public static IReadOnlyList<(string Id, string Label)> Options { get; } = new[]
    {
        ("default", "Violet (défaut)"), ("fire", "Feu"), ("light", "Lumière"),
        ("earth", "Terre"), ("nature", "Nature"), ("shadow", "Ombre"), ("water", "Eau"),
    };

    /// <summary>Couleur secondaire du thème en cours (pour le code qui dessine lui-même) et signal envoyé à chaque changement de thème.</summary>
    public static Color Accent { get; private set; } = Color.Parse("#FFC93C");
    public static event Action? Changed;

    public static void Apply(string theme, bool highContrast)
    {
        if (Application.Current is not { } app) return;
        var p = Palettes.GetValueOrDefault(theme) ?? Palettes["default"];
        Set(app, "BrushNight", Color.Parse(p.Night));
        var panel = Color.Parse(p.Panel);
        Set(app, "BrushPanel", Color.FromArgb(highContrast ? (byte)0xF2 : (byte)0xB8, panel.R, panel.G, panel.B));
        Set(app, "BrushPanelSolid", panel);
        Set(app, "BrushRail", panel);
        var violet = Color.Parse(p.Violet);
        if (highContrast) violet = Mix(violet, Colors.White, 0.45);          // bordures bien visibles
        Set(app, "BrushViolet", violet);
        Set(app, "BrushTextSecondary", highContrast ? Colors.White : Color.FromArgb(0xB3, 255, 255, 255));
        var control = Mix(Mix(panel, violet, 0.55), Colors.White, 0.04);
        Set(app, "BrushControl", Color.FromArgb(0xA0, control.R, control.G, control.B));
        var edge = Mix(violet, Colors.White, 0.42);
        Set(app, "BrushControlBorder", edge);
        var night = Color.Parse(p.Night);
        Set(app, "BrushField", Color.FromArgb(0xD0, night.R, night.G, night.B));
        Set(app, "ComboBoxDropDownBackground", panel);
        Set(app, "ComboBoxDropDownBorderBrush", edge);
        Set(app, "FlyoutPresenterBackground", panel);
        Set(app, "FlyoutBorderThemeBrush", edge);
        Set(app, "ComboBoxItemBackgroundPointerOver", violet);
        Set(app, "ComboBoxItemBackgroundSelected", Mix(violet, Colors.White, 0.15));
        Set(app, "ComboBoxItemBackgroundSelectedPointerOver", Mix(violet, Colors.White, 0.25));
        Set(app, "ComboBoxBackground", Color.FromArgb(0xA0, control.R, control.G, control.B));
        Set(app, "ComboBoxBorderBrush", edge);
        Set(app, "ScrollBarThumbFill", edge);
        Set(app, "ToolTipBackground", Color.FromArgb(0xF0, night.R, night.G, night.B));
        Set(app, "ExpanderHeaderBackground", Color.FromArgb(0xB8, panel.R, panel.G, panel.B));
        Set(app, "ExpanderHeaderBackgroundPointerOver", violet);
        Set(app, "ExpanderHeaderBackgroundPressed", violet);
        // Dégradés : même thème, trois teintes douces + une touche de jaune sur les contours.
        Color e1 = Color.Parse(p.Edge1), e2 = Color.Parse(p.Edge2), e3 = Color.Parse(p.Edge3);
        byte pa = highContrast ? (byte)0xF2 : (byte)0xD0;
        SetStops(app, "BrushPanelFill", WithAlpha(Mix(panel, e1, 0.30), pa), WithAlpha(panel, (byte)(pa - 12)), WithAlpha(Mix(panel, e2, 0.28), pa));
        SetStops(app, "BrushPanelEdge", e1, e2, Color.Parse(p.Accent), e3);
        SetStops(app, "BrushButtonFill", WithAlpha(Mix(violet, Colors.White, 0.30), 0xC8), WithAlpha(Mix(violet, Colors.Black, 0.12), 0xC8));
        SetStops(app, "BrushButtonHover", WithAlpha(Mix(violet, Colors.White, 0.5), 0xE6), WithAlpha(Mix(violet, e1, 0.25), 0xE6));
        SetStops(app, "BrushSelectFill", WithAlpha(Mix(violet, e1, 0.3), 0xC0), WithAlpha(Mix(violet, e2, 0.4), 0xC0));
        StarField.SetKind(theme);
        // Couleur secondaire : titres, boutons principaux, poignées, onglet actif, survols, lueurs (jaune par défaut).
        Color acc = Color.Parse(p.Accent), accEnd = Color.Parse(p.AccentEnd);
        Set(app, "BrushYellow", acc);
        SetStops(app, "BrushPrimaryFill", Mix(acc, Colors.White, 0.15), accEnd);
        SetStops(app, "BrushPrimaryHover", Mix(acc, Colors.White, 0.4), Mix(accEnd, Colors.White, 0.25));
        Set(app, "BrushAccentDark", Mix(acc, Colors.Black, 0.2));
        Set(app, "BrushAccentLight", Mix(acc, Colors.White, 0.4));
        Set(app, "BrushAccentGlow", WithAlpha(acc, 0x66));
        Set(app, "ComboBoxBorderBrushPointerOver", acc);
        Set(app, "ComboBoxBorderBrushPressed", acc);
        Set(app, "ToolTipBorderBrush", acc);
        Set(app, "ScrollBarThumbFillPointerOver", acc);
        Set(app, "ScrollBarThumbFillPressed", Mix(acc, Colors.Black, 0.2));
        if (app.TryGetResource("GlowEffect", app.ActualThemeVariant, out var glow) && glow is DropShadowEffect shadow) shadow.Color = acc;
        Accent = acc;
        Changed?.Invoke();
    }

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    private static void SetStops(Application app, string key, params Color[] colours)
    {
        if (app.TryGetResource(key, app.ActualThemeVariant, out var r) && r is GradientBrush g && g.GradientStops.Count == colours.Length)
            for (var i = 0; i < colours.Length; i++) g.GradientStops[i].Color = colours[i];
    }

    private static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static void Set(Application app, string key, Color color)
    {
        if (app.TryGetResource(key, app.ActualThemeVariant, out var r) && r is SolidColorBrush brush) brush.Color = color;
    }
}
