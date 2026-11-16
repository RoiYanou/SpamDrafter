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
    /// Deck témoin (ancien « deck de référence », D-050) : plus utilisé ni affiché par l'application (D-065). Gardé dans
    /// les fichiers pour l'outil de mesure du Studio (`spamdrafter-duel` : écart simulation / vrais taux de victoire).
    /// Absent des anciens fichiers : faux par défaut ; conservé tel quel quand le joueur modifie et sauvegarde le deck.
    /// </summary>
    public bool IsReference { get; set; }
    public string GameVersion { get; set; } = "";
    public List<DeckSlot> Slots { get; set; } = new();
}
