using System.Text.Json.Serialization;
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
    /// <summary>Deck mis en favori (phase 12, lot A). Absent des anciens fichiers : faux.</summary>
    public bool Favorite { get; set; }
    /// <summary>Étiquettes libres (« ranked », « essai »…). Absentes des anciens fichiers : liste vide.</summary>
    public List<string> Tags { get; set; } = new();
    /// <summary>Dernière sauvegarde (UTC) ; null pour les decks d'avant la phase 12.</summary>
    public DateTime? ModifiedUtc { get; set; }
    public List<DeckSlot> Slots { get; set; } = new();
    /// <summary>
    /// Challenge relevé avec ce deck (onglet Challenges) : le deck porte un badge avec les conditions et ses cartes et talents ne se modifient plus.
    /// Absent des anciens fichiers : aucun.
    /// </summary>
    public SpamDrafter.Core.Challenges.DeckChallenge? Challenge { get; set; }

    /// <summary>Pour l'affichage : le deck relève un challenge.</summary>
    [JsonIgnore] public bool IsChallenge => Challenge is not null;
    /// <summary>Pour l'affichage : conditions du challenge, affichées au survol du badge.</summary>
    [JsonIgnore] public string ChallengeTip => Challenge?.Tooltip ?? "";

    /// <summary>Pour l'affichage : le deck vient du générateur (le badge reste après un renommage).</summary>
    [JsonIgnore] public bool IsGenerated => Origin == DeckOrigin.Generated;
    /// <summary>Pour l'affichage : une mise à jour de SPAM touche ce deck (calculé par le Deck builder, jamais enregistré).</summary>
    [JsonIgnore] public bool VersionImpacted { get; set; }
    /// <summary>Pour l'affichage : étiquettes sous la forme « # ranked  # essai ».</summary>
    [JsonIgnore] public string TagsLabel => string.Join("  ", Tags.Select(t => "# " + t));
}
