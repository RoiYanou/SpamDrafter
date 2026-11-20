using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using Avalonia.VisualTree;
using SpamDrafter.App.ViewModels;

namespace SpamDrafter.App.Views;

public partial class GamesView : UserControl
{
    private Border? _pressedAura;

    public GamesView()
    {
        InitializeComponent();
        // Clic sur un bouton du menu : le cadre lumineux s'enfonce avec lui (la lueur suit le bouton).
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            var aura = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("aura"));
            if (aura is null) return;
            _pressedAura = aura; aura.Classes.Add("down");
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        void Release() { _pressedAura?.Classes.Remove("down"); _pressedAura = null; }
        AddHandler(PointerReleasedEvent, (_, _) => Release(), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, (_, _) => Release(), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not GamesViewModel { Rng: { } rng }) return;
            rng.StageRevealed += OnStage;
            rng.ScoreStepped += () => PopIn.Play(TotalLive, 0.85);
            rng.ResultShown += () => PopIn.Play(ResultBorder, 0.8);
        };
    }

    /// <summary>Un élément de la carte du Tirage du jour vient d'être tiré : il apparaît avec une petite animation.</summary>
    private void OnStage(int stage)
    {
        // Étapes : 1 illustration, 2 nom, 3 rareté (logo et contour), 4 élément, 5 Spamtons, 6 PV, 7 à 9 compétences / Tox.
        string[] classes = stage switch
        {
            1 => [], 2 => ["popname"], 3 => ["poprar"], 4 => ["popel"], 5 => ["popgold"], 6 => ["pophp"], _ => ["popab"],
        };
        if (stage == 1) PopIn.Play(FormingCard, 0.7);
        foreach (var c in FormingCard.GetVisualDescendants().OfType<Control>().Where(c => classes.Any(c.Classes.Contains))) PopIn.Play(c);
        // Le fond dégradé du nom apparaît en fondu avec le texte (il ne grossit pas avec lui).
        if (stage == 2) foreach (var c in FormingCard.GetVisualDescendants().OfType<Control>().Where(c => c.Classes.Contains("popnamebg"))) PopIn.FadeIn(c, 0.5);
    }

    /// <summary>Un gain de points apparaît en haut de la liste : il grossit un instant.</summary>
    private void ScoreRow_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control row && DataContext is GamesViewModel { Rng.IsScoring: true }) PopIn.Play(row, 0.85);
    }
}
