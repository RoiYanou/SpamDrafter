using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SpamDrafter.Core.Settings;

namespace SpamDrafter.App.ViewModels;

/// <summary>
/// Couleur et emoji choisis par le joueur pour ses decks (phase 13). Ils sont retenus dans les réglages (pas dans le fichier du deck :
/// le format des decks ne change pas) ; un deck sans décor s'affiche comme avant.
/// </summary>
public static class DeckDecor
{
    public static AppSettings? Settings { get; set; }

    public static readonly IReadOnlyList<string> Colors = new[] { "#E05A4F", "#F2A33A", "#4CC38A", "#4F9BE0", "#A66BE0", "#E06BB0" };

    /// <summary>Emojis proposés dans le Deck builder (choix en un clic).</summary>
    public static readonly IReadOnlyList<string> Emojis = new[]
    {
        "🔥", "💧", "🌿", "⚡", "🌙", "🌞", "🌪", "🌋", "🌊", "💀", "👑", "💎", "⭐", "🌟", "💥", "🌈",
        "🎯", "🎲", "🃏", "🧪", "🔮", "🪄", "🏹", "🏆", "🚀", "🧠", "❤", "💜", "💛", "🍀", "🎵", "🍄",
        "🐉", "🦊", "🐺", "🦅", "🐍", "🐱", "🐶", "🦄", "👻", "🤖", "👽", "😈", "😎", "🤡", "🥶", "🤯",
    };

    public static DeckStyle? StyleOf(Guid id) => Settings is not null && Settings.DeckStyles.TryGetValue(id.ToString(), out var s) ? s : null;

    /// <summary>Barre de couleur à gauche du deck (transparente sans couleur choisie).</summary>
    public static readonly IValueConverter ColorBrush = new FuncConverter(v =>
        v is Guid id && StyleOf(id)?.Color is { } c && Color.TryParse(c, out var col) ? new SolidColorBrush(col) : Brushes.Transparent);

    /// <summary>Emoji du deck (vide sans emoji choisi).</summary>
    public static readonly IValueConverter Emoji = new FuncConverter(v => v is Guid id ? StyleOf(id)?.Emoji ?? "" : "");

    private sealed class FuncConverter(Func<object?, object?> f) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => f(value);
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
