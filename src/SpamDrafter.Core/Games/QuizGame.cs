using System.Text.Json;

namespace SpamDrafter.Core.Games;

/// <summary>Une question du quizz : 4 réponses, une seule bonne (<c>knowledge/&lt;version&gt;/games/quiz.json</c>, générée à partir des données du jeu). <c>Card</c> : nom interne de la carte dont la question parle (son illustration est montrée), ou null.</summary>
public sealed record QuizQuestion(string Id, string Category, string Question, IReadOnlyList<string> Options, int Answer, string Explanation, string? Card = null);

/// <summary>
/// « Quizz du jour » : <see cref="Count"/> questions sur le jeu, les mêmes pour tous (tirage à partir de la date, D-095).
/// Les questions du jour viennent de catégories différentes ; dans une catégorie, aucune question ne revient avant d'avoir fait le tour.
/// Les réponses du joueur sont enregistrées dans <see cref="DailyRecord.Guesses"/> (numéro de la réponse choisie, « 0 » à « 3 », ou « -1 » pour une question laissée sans réponse).
/// </summary>
public sealed class QuizGame
{
    public const string GameId = "quiz";
    public const int Count = 5;

    public QuizGame(IReadOnlyList<QuizQuestion> questions) { Questions = questions; }

    public IReadOnlyList<QuizQuestion> Questions { get; }

    /// <summary>Lit la banque de questions (sans fichier : liste vide). Les questions mal formées (pas 4 réponses, bonne réponse hors liste) sont ignorées. Jamais d'exception.</summary>
    public static IReadOnlyList<QuizQuestion> Load(string knowledgeDirectory)
    {
        try
        {
            var file = Path.Combine(knowledgeDirectory, "games", "quiz.json");
            if (!File.Exists(file)) return [];
            var list = JsonSerializer.Deserialize<List<QuizQuestion>>(File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            return list.Where(q => !string.IsNullOrWhiteSpace(q.Question) && q.Options is { Count: 4 } && q.Answer is >= 0 and < 4 && q.Options.Distinct().Count() == 4).ToList();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>Les questions d'un jour ; null s'il y a moins de <see cref="Count"/> catégories utilisables.</summary>
    public static QuizGame? ForDay(IReadOnlyList<QuizQuestion> bank, DateOnly day)
    {
        var byCategory = bank.GroupBy(q => q.Category).OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(q => q.Id, StringComparer.Ordinal).ToList());
        if (byCategory.Count < Count) return null;
        var number = Math.Max(0, DailyClock.Number(day));
        var cats = byCategory.Keys.ToArray();
        var rng = new SeededRandom(SeededRandom.SeedOf("quiz-cat|" + number));
        for (var i = cats.Length - 1; i > 0; i--) { var j = rng.Next(i + 1); (cats[i], cats[j]) = (cats[j], cats[i]); }
        var picked = new List<QuizQuestion>();
        foreach (var cat in cats.Take(Count))
        {
            var list = byCategory[cat];
            var round = number / list.Count;
            var order = Enumerable.Range(0, list.Count).ToArray();
            var r = new SeededRandom(SeededRandom.SeedOf("quiz|" + cat + "|" + round));
            for (var i = order.Length - 1; i > 0; i--) { var j = r.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
            picked.Add(list[order[number % list.Count]]);
        }
        return new QuizGame(picked);
    }

    /// <summary>Nombre de bonnes réponses parmi celles enregistrées (dans l'ordre des questions).</summary>
    public int Score(IReadOnlyList<string> answers)
    {
        var score = 0;
        for (var i = 0; i < Math.Min(answers.Count, Questions.Count); i++)
            if (int.TryParse(answers[i], out var a) && a == Questions[i].Answer) score++;
        return score;
    }
}
