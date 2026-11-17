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
    private sealed record Palette(string Night, string Panel, string Violet);

    /// <summary>Palettes (🟡 valeurs de départ, à ajuster à l'œil).</summary>
    private static readonly Dictionary<string, Palette> Palettes = new()
    {
        ["default"] = new("#1A1033", "#2B1B52", "#4B2E8C"),
        ["fire"] = new("#2A0E0A", "#4A1A12", "#A03E20"),
        ["light"] = new("#2B2610", "#51461A", "#A88F2A"),
        ["earth"] = new("#241808", "#463014", "#8C6A2E"),
        ["nature"] = new("#0B2412", "#17401F", "#2F8A44"),
        ["shadow"] = new("#0C0A16", "#211A38", "#5A4A8A"),
        ["water"] = new("#0A1A33", "#12305A", "#1F63B0"),
    };

    public static IReadOnlyList<(string Id, string Label)> Options { get; } = new[]
    {
        ("default", "Violet (défaut)"), ("auto", "Selon la carte choisie"), ("fire", "Feu"), ("light", "Lumière"),
        ("earth", "Terre"), ("nature", "Nature"), ("shadow", "Ombre"), ("water", "Eau"),
    };

    /// <summary>Thème d'un élément de carte (« fire », « element-fire »…), ou le thème par défaut.</summary>
    public static string ForElement(string? elementId)
    {
        var id = (elementId ?? "").ToLowerInvariant();
        return Palettes.Keys.FirstOrDefault(k => k != "default" && id.Contains(k)) ?? "default";
    }

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
    }

    private static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static void Set(Application app, string key, Color color)
    {
        if (app.TryGetResource(key, app.ActualThemeVariant, out var r) && r is SolidColorBrush brush) brush.Color = color;
    }
}
