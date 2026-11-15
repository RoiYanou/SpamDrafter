using Avalonia.Controls;
using Avalonia.Input;

namespace SpamDrafter.App.Views;

public partial class CardListPanel : UserControl
{
    public CardListPanel()
    {
        InitializeComponent();
        List.DoubleTapped += (_, e) => CardActivated?.Invoke(this, e);
    }

    /// <summary>Double-clic sur une carte de la liste.</summary>
    public event EventHandler<TappedEventArgs>? CardActivated;
}
