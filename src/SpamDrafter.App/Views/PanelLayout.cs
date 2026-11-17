using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using SpamDrafter.Core.Settings;

namespace SpamDrafter.App.Views;

/// <summary>
/// Dimensions des « bulles » (colonnes) des onglets Builder, Drafter et Génération : le joueur les règle en tirant les barres entre
/// colonnes ; la disposition est retenue dans les réglages (donc gardée après un redémarrage) et le bouton « Réinitialiser les
/// dimensions » n'apparaît que lorsqu'elle diffère de celle d'origine.
/// </summary>
public static class PanelLayout
{
    public static AppSettings? Settings { get; set; }
    public static Action? Save { get; set; }

    /// <param name="grid">La grille : colonnes de contenu aux rangs pairs, barres aux rangs impairs.</param>
    /// <param name="key">Nom de la disposition dans les réglages.</param>
    /// <param name="reset">Le bouton « Réinitialiser les dimensions » (caché tant que rien n'a changé).</param>
    public static void Attach(Grid grid, string key, Button reset)
    {
        var content = Enumerable.Range(0, grid.ColumnDefinitions.Count).Where(i => i % 2 == 0).ToList();
        var defaults = content.Select(i => grid.ColumnDefinitions[i].Width).ToList();
        double[] DefaultRatios() => defaults.Select(d => d.Value).ToArray();

        void Apply(IReadOnlyList<double> ratios)
        {
            for (var k = 0; k < content.Count; k++) grid.ColumnDefinitions[content[k]].Width = new GridLength(ratios[k], GridUnitType.Star);
        }
        bool IsModified()
        {
            if (Settings is null || !Settings.PanelLayouts.TryGetValue(key, out var saved) || saved.Count != content.Count) return false;
            var d = DefaultRatios();
            double ds = d.Sum(), ss = saved.Sum();
            return ss > 0 && Enumerable.Range(0, d.Length).Any(i => Math.Abs(d[i] / ds - saved[i] / ss) > 0.01);
        }
        void Refresh() => reset.IsVisible = IsModified();

        if (Settings is not null && Settings.PanelLayouts.TryGetValue(key, out var start) && start.Count == content.Count && start.All(v => v > 0)) Apply(start);
        Refresh();

        foreach (var splitter in grid.Children.OfType<GridSplitter>())
            splitter.DragCompleted += (_, _) =>
            {
                if (Settings is null) return;
                var widths = content.Select(i => grid.ColumnDefinitions[i].ActualWidth).ToList();
                if (widths.Any(w => w < 1)) return;
                Settings.PanelLayouts[key] = widths;
                Apply(widths);
                Save?.Invoke();
                Refresh();
            };
        reset.Click += (_, _) =>
        {
            Settings?.PanelLayouts.Remove(key);
            Apply(DefaultRatios());
            Save?.Invoke();
            Refresh();
        };
    }
}
