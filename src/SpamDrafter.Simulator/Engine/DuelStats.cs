namespace SpamDrafter.Simulator.Engine;

/// <summary>
/// Statistiques d'un duel (phase 8, lot A), relevées seulement si <see cref="Duel.Stats"/> est rempli
/// (les copies de partie utilisées par l'IA n'en relèvent pas). Elles servent au Grand Analyseur.
/// Les cartes sont repérées par leur place dans le deck (0 = 1ʳᵉ carte), qui ne change pas,
/// même après une Métamorphose.
/// </summary>
public sealed class DuelStats
{
    /// <summary>Ce qu'a fait une carte pendant le duel.</summary>
    public sealed class CardLine
    {
        /// <summary>PV retirés aux cartes ennemies (y compris éclaboussures AoE, poison, Érosion).</summary>
        public int DamageDone { get; set; }
        /// <summary>PV rendus (à soi ou à ses alliées).</summary>
        public int HealingDone { get; set; }
        /// <summary>PV perdus, quelle qu'en soit la source.</summary>
        public int DamageTaken { get; set; }
        /// <summary>Cartes ennemies tuées.</summary>
        public int Kills { get; set; }
        /// <summary>Capacités lancées par le joueur (pas les déclenchements automatiques).</summary>
        public int Casts { get; set; }
        /// <summary>Activations du talent.</summary>
        public int TalentActivations { get; set; }
        /// <summary>Tox achetés pour cette carte.</summary>
        public int ToxBought { get; set; }
        /// <summary>La carte est morte pendant le duel.</summary>
        public bool Died { get; set; }
        /// <summary>Coups critiques portés (dégâts ou soins calculés).</summary>
        public int CritHits { get; set; }
        /// <summary>Valeur ajoutée par les critiques (avant résistances) : total − total / multiplicateur.</summary>
        public double CritBonus { get; set; }
        /// <summary>Niveaux d'élément apportés à son joueur (lot G).</summary>
        public int ElementGiven { get; set; }
    }

    public sealed record Purchase(int Player, int DeckIndex, string ToxUuid, int Round);

    /// <summary>Lignes par joueur (0, 1), puis par place dans le deck.</summary>
    public CardLine[][] Cards { get; }
    public List<Purchase> Purchases { get; } = new();
    /// <summary>Plus haut niveau atteint par élément, pour chaque joueur (0, 1).</summary>
    public Dictionary<string, double>[] PeakLevels { get; } = [new(), new()];

    public DuelStats(int cardsPerDeck)
    {
        Cards = [Enumerable.Range(0, cardsPerDeck).Select(_ => new CardLine()).ToArray(),
                 Enumerable.Range(0, cardsPerDeck).Select(_ => new CardLine()).ToArray()];
    }

    /// <summary>Place dans le deck d'une carte (son identifiant est « joueur.place », place à partir de 1).</summary>
    public static int DeckIndex(CardState c) => int.Parse(c.Uid.AsSpan(c.Uid.IndexOf('.') + 1)) - 1;

    public CardLine Of(CardState c) => Cards[c.Owner][DeckIndex(c)];

    internal void Crit(CardState caster, double bonus)
    {
        var line = Of(caster);
        line.CritHits++;
        line.CritBonus += Math.Max(bonus, 0);
    }

    internal void ElementLevel(CardState? source, int player, string element, int value, double level)
    {
        if (source is not null && source.Owner == player && value > 0) Of(source).ElementGiven += value;
        if (!PeakLevels[player].TryGetValue(element, out var peak) || level > peak) PeakLevels[player][element] = level;
    }

    internal void HpChange(CardState? source, CardState target, int hpBefore, int hpAfter)
    {
        var delta = hpBefore - hpAfter;
        if (delta == 0) return;
        if (delta > 0)
        {
            Of(target).DamageTaken += delta;
            if (source is not null && source.Owner != target.Owner) Of(source).DamageDone += delta;
        }
        else if (source is not null && source.Owner == target.Owner) Of(source).HealingDone -= delta;
    }
}
