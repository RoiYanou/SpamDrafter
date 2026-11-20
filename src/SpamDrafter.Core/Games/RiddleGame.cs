using System.Text.Json;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.Core.Games;

/// <summary>Une énigme écrite pour une carte (<c>knowledge/&lt;version&gt;/games/riddles.json</c>, paquet de données).</summary>
public sealed record Riddle(string CardName, string Text);

/// <summary>Un indice dévoilé après une mauvaise réponse.</summary>
public sealed record RiddleHint(string Label, string Value);

/// <summary>
/// « Énigme du jour » : une phrase énigmatique, une seule carte à retrouver en <see cref="MaxAttempts"/> essais.
/// Chaque mauvaise réponse dévoile un indice. La carte du jour est la même pour tous (tirage à partir de la date, D-095).
/// </summary>
public sealed class RiddleGame
{
    public const string GameId = "riddle";
    public const int MaxAttempts = 6;

    private readonly KnowledgeBase _kb;

    public RiddleGame(KnowledgeBase kb, Card answer, string text)
    {
        _kb = kb; Answer = answer; Text = text;
    }

    public Card Answer { get; }
    public string Text { get; }

    /// <summary>Lit les énigmes de la base (sans fichier : liste vide). Jamais d'exception.</summary>
    public static IReadOnlyList<Riddle> LoadRiddles(string knowledgeDirectory)
    {
        try
        {
            var file = Path.Combine(knowledgeDirectory, "games", "riddles.json");
            if (!File.Exists(file)) return [];
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var list = new List<Riddle>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var card = e.GetProperty("card").GetString();
                var text = e.GetProperty("text").GetString();
                if (!string.IsNullOrWhiteSpace(card) && !string.IsNullOrWhiteSpace(text)) list.Add(new Riddle(card, text.Trim()));
            }
            return list;
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or KeyNotFoundException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// L'énigme d'un jour. Les énigmes (dont la carte existe) sont classées par nom, puis mélangées par « tour » (un tour = autant de jours que d'énigmes) :
    /// aucune carte ne revient avant la fin du tour. Null s'il n'y a aucune énigme utilisable.
    /// </summary>
    public static RiddleGame? ForDay(KnowledgeBase kb, IReadOnlyList<Riddle> riddles, DateOnly day)
    {
        var usable = riddles
            .Select(r => (Riddle: r, Card: kb.Cards.FirstOrDefault(c => c.SanitizedName == r.CardName)))
            .Where(x => x.Card is not null)
            .OrderBy(x => x.Riddle.CardName, StringComparer.Ordinal)
            .ToList();
        if (usable.Count == 0) return null;
        var number = Math.Max(0, DailyClock.Number(day));
        var round = number / usable.Count;
        var order = Enumerable.Range(0, usable.Count).ToArray();
        var rng = new SeededRandom(SeededRandom.SeedOf("riddle|" + round));
        for (var i = order.Length - 1; i > 0; i--) { var j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
        var pick = usable[order[number % usable.Count]];
        return new RiddleGame(kb, pick.Card!, pick.Riddle.Text);
    }

    /// <summary>Tous les indices possibles, dans l'ordre où ils sont dévoilés (le premier après la 1ʳᵉ erreur…). Ceux que la carte n'a pas sont ignorés.</summary>
    public IReadOnlyList<RiddleHint> Hints()
    {
        var list = new List<RiddleHint>
        {
            new("Élément", _kb.ElementName(Answer.Element)),
            new("Rareté", _kb.RarityName(Answer.Rarity)),
        };
        var lore = _kb.Lore(Answer);
        if (lore is not null && lore.Region.Length > 0) list.Add(new("Région", lore.Region));
        if (lore is not null && lore.Species.Length > 0) list.Add(new("Espèce", lore.Species));
        list.Add(new("PV", Answer.Hp.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var name = _kb.CardName(Answer).Trim();
        if (name.Length > 0) list.Add(new("Première lettre", name[..1].ToUpperInvariant()));
        return list;
    }

    /// <summary>Indices visibles après <paramref name="wrongGuesses"/> mauvaises réponses.</summary>
    public IReadOnlyList<RiddleHint> VisibleHints(int wrongGuesses) => Hints().Take(Math.Max(0, wrongGuesses)).ToList();

    public bool IsCorrect(string cardId) => cardId == Answer.Id;
}
