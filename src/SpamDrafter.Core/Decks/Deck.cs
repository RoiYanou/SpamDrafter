namespace SpamDrafter.Core.Decks;

public enum DeckOrigin { Manual, Generated }

/// <summary>Une carte du deck et son talent choisi (null = aucun talent, choix « 0 » du jeu).</summary>
public sealed record DeckSlot(string CardId, string? TalentId);

/// <summary>Deck de 1 à 4 cartes (D-020 : pas de doublon).</summary>
public sealed class Deck
{
    public const int MaxCards = 4;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public DeckOrigin Origin { get; set; } = DeckOrigin.Manual;
    /// <summary>
    /// Deck de référence (D-050) : un deck qui se joue réellement dans le jeu, saisi par le joueur. Les moteurs s'en
    /// servent comme adversaires à battre (« méta »). Absent des anciens fichiers : faux par défaut.
    /// </summary>
    public bool IsReference { get; set; }
    public string GameVersion { get; set; } = "";
    public List<DeckSlot> Slots { get; set; } = new();
}
