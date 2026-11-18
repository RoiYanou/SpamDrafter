using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace SpamDrafter.App.Views;

/// <summary>
/// Menu déroulant « à bouton » : un bouton (le titre à gauche, une flèche tout à droite) dont le contenu s'ouvre PAR-DESSUS ce qui l'entoure
/// (cartes, liste de decks…), sur un fond semi-transparent, de la largeur du bouton. La flèche montre toujours le sens d'ouverture
/// (<see cref="OpensUp"/> : vers le haut) et se retourne quand le menu est ouvert.
/// </summary>
public sealed class Collapse : ContentControl
{
    public static readonly StyledProperty<object?> HeaderProperty = AvaloniaProperty.Register<Collapse, object?>(nameof(Header));
    public static readonly StyledProperty<bool> IsOpenProperty = AvaloniaProperty.Register<Collapse, bool>(nameof(IsOpen));
    public static readonly StyledProperty<bool> OpensUpProperty = AvaloniaProperty.Register<Collapse, bool>(nameof(OpensUp));
    public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    /// <summary>Vrai : le menu s'ouvre vers le haut (menu placé en bas de son panneau).</summary>
    public bool OpensUp { get => GetValue(OpensUpProperty); set => SetValue(OpensUpProperty, value); }

    protected override Type StyleKeyOverride => typeof(ContentControl);

    private TextBlock? _arrow;
    private Border? _frame;
    private Popup? _popup;

    public Collapse()
    {
        Template = new FuncControlTemplate<Collapse>((owner, _) =>
        {
            var title = new ContentPresenter { VerticalAlignment = VerticalAlignment.Center };
            title.Bind(ContentPresenter.ContentProperty, new Binding(nameof(Header)) { Source = owner });
            _arrow = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
            row.Children.Add(title);
            Grid.SetColumn(_arrow, 1);
            row.Children.Add(_arrow);
            var button = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 8) };
            button.Click += (_, _) => IsOpen = !IsOpen;

            var body = new ContentPresenter { Margin = new Thickness(0) };
            body.Bind(ContentPresenter.ContentProperty, new Binding(nameof(Content)) { Source = owner });
            body.Bind(ContentPresenter.ContentTemplateProperty, new Binding(nameof(ContentTemplate)) { Source = owner });
            var frame = _frame = new Border
            {
                Child = body, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.5),
                Padding = new Thickness(4), BoxShadow = BoxShadows.Parse("0 6 18 0 #99000000"),
            };
            var scaleBox = new LayoutTransformControl { Child = frame };
            var popup = new Popup
            {
                PlacementTarget = button, IsLightDismissEnabled = true, OverlayInputPassThroughElement = button,
                Child = scaleBox,
            };
            popup.Bind(Popup.IsOpenProperty, new Binding(nameof(IsOpen)) { Source = owner, Mode = BindingMode.TwoWay });
            // Le menu a la largeur (et l'échelle) du bouton ; il s'ouvre au-dessus ou au-dessous selon le sens voulu.
            popup.Opened += (_, _) => { OverlayMenu.StyleFrame(this, frame); OverlayMenu.Fit(button, scaleBox, frame); };
            popup.Placement = OpensUp ? PlacementMode.Top : PlacementMode.Bottom;
            _popup = popup;
            Refresh();
            var panel = new StackPanel();
            panel.Children.Add(button);
            panel.Children.Add(popup);
            return panel;
        });
    }

    private void Refresh()
    {
        if (_arrow is null) return;
        // Fermé : la flèche montre le sens d'ouverture ; ouvert : elle montre le sens de fermeture.
        _arrow.Text = OpensUp != IsOpen ? "▴" : "▾";
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == OpensUpProperty && _popup is not null) _popup.Placement = OpensUp ? PlacementMode.Top : PlacementMode.Bottom;
        if (change.Property == IsOpenProperty || change.Property == OpensUpProperty) Refresh();
    }
}
