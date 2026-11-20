using Avalonia;
using Avalonia.Controls;

namespace SpamDrafter.App.Views;

public partial class GuessPanel : UserControl
{
    public GuessPanel()
    {
        InitializeComponent();
        // La liste des cartes proposées prend toute la largeur du champ de saisie.
        SearchBox.SizeChanged += (_, e) => SuggestBox.Width = e.NewSize.Width;
        // Quand on quitte l'écran du jeu (retour, autre onglet), la liste se ferme : le champ est vidé.
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property.Name == nameof(IsEffectivelyVisible) && change.NewValue is false && SearchBox is not null) SearchBox.Text = "";
    }
}
