using System.Text.Json;
using System.Text.Json.Serialization;
using SpamDrafter.Core.Knowledge;
using SpamDrafter.Core.Text;

namespace SpamDrafter.Core.Challenges;

/// <summary>
/// Ce que le deck garde d'un challenge : son identifiant, son titre et ses conditions (affichés au survol du badge).
/// Écrit dans le fichier du deck ; un deck qui en a un n'est plus modifiable.
/// </summary>
public sealed class DeckChallenge
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Conditions { get; set; } = "";
    /// <summary>Survol du badge : le titre seulement.</summary>
    [JsonIgnore] public string Tooltip => Title;
}

/// <summary>Règle de vérification d'un challenge par défaut. <see cref="Custom"/> : challenge écrit par le joueur, sans vérification automatique.</summary>
public enum ChallengeKind
{
    Custom,
    DifferentElements, SameRegion, SameSpecies, NoInnateTox, ThreeSlots, HpBelow800, HpAbove800,
    SpamtonsAtMost25, SpamtonsAtLeast40, SharedFirstLetter, FacingLeft, FacingRight, Quadruped,
    AtMostTwoSameElement, FourSameElement, WearsClothes, NoClothes, TwoAnimals,
}

public sealed record ChallengeDefinition(string Id, string Title, string Conditions, ChallengeKind Kind)
{
    public bool IsCustom => Kind == ChallengeKind.Custom;
    public DeckChallenge ToBadge() => new() { Id = Id, Title = Title, Conditions = Conditions };
}

/// <summary>Étiquettes de l'illustration d'une carte (🟡 estimées à la main, corrigeables dans <c>card_tags.json</c>) : le jeu ne les fournit pas.</summary>
public sealed record CardTag(string Facing, bool Quadruped, bool Clothes, int Animals);

/// <summary>Une carte vue par les règles : nom, élément, lore (🃏 textes du jeu), PV, revenu, compétences et Tox de base, étiquettes.</summary>
public sealed record CardView(Card Card, string Name, string Species, string Region, CardTag? Tag)
{
    public string Element => Card.Element;
    public int Hp => Card.Hp;
    public int Income => Card.GoldIncome;
    public int InnateTox => Card.InnatePassives?.Count ?? 0;
    public int Slots => Card.Abilities.Count + InnateTox;
    /// <summary>Première lettre du nom, sans accent ni majuscule.</summary>
    public char Letter => TextSearch.Normalize(Name).FirstOrDefault(char.IsLetterOrDigit);
}

/// <summary>Lecture des étiquettes livrées avec l'application.</summary>
public static class CardTags
{
    private sealed class FileShape { public Dictionary<string, TagShape> Cards { get; set; } = new(); }
    private sealed class TagShape { public string Facing { get; set; } = "N"; public bool Quadruped { get; set; } public bool Clothes { get; set; } public int Animals { get; set; } }

    private static IReadOnlyDictionary<string, CardTag>? _all;
    public static IReadOnlyDictionary<string, CardTag> All => _all ??= Load();

    private static IReadOnlyDictionary<string, CardTag> Load()
    {
        try
        {
            using var s = typeof(CardTags).Assembly.GetManifestResourceStream("card_tags.json");
            if (s is null) return new Dictionary<string, CardTag>();
            var f = JsonSerializer.Deserialize<FileShape>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return f?.Cards.ToDictionary(p => p.Key, p => new CardTag(p.Value.Facing, p.Value.Quadruped, p.Value.Clothes, p.Value.Animals)) ?? new();
        }
        catch (JsonException) { return new Dictionary<string, CardTag>(); }
    }
}

/// <summary>Les cartes du jeu telles que les règles les voient (construit une fois).</summary>
public sealed class ChallengeContext
{
    private readonly Dictionary<string, CardView> _views;
    public ChallengeContext(KnowledgeBase kb, IReadOnlyDictionary<string, CardTag>? tags = null)
    {
        tags ??= CardTags.All;
        _views = kb.Cards.ToDictionary(c => c.Id, c =>
        {
            var lore = kb.Lore(c);
            return new CardView(c, kb.CardName(c), lore?.Species ?? "", lore?.Region ?? "", tags.GetValueOrDefault(c.SanitizedName));
        });
        MinIncome = _views.Values.Min(v => v.Income);
        MaxIncome = _views.Values.Max(v => v.Income);
    }
    public int MinIncome { get; }
    public int MaxIncome { get; }
    public IEnumerable<CardView> All => _views.Values;
    public CardView? View(string cardId) => _views.GetValueOrDefault(cardId);
    public CardView View(Card c) => _views[c.Id];
}

/// <summary>Les 18 challenges par défaut et leur vérification.</summary>
public static class ChallengeCatalog
{
    public const int DeckSize = 4;

    public static readonly IReadOnlyList<ChallengeDefinition> Defaults = new ChallengeDefinition[]
    {
        new("elements-differents", "Jouer des cartes de types élémentaires différents", "Les 4 cartes ont chacune un élément différent.", ChallengeKind.DifferentElements),
        new("meme-region", "Jouer des cartes de la même région", "Les 4 cartes viennent de la même région (lore du jeu).", ChallengeKind.SameRegion),
        new("meme-espece", "Jouer des cartes de la même espèce", "Les 4 cartes sont de la même espèce (lore du jeu).", ChallengeKind.SameSpecies),
        new("sans-tox-de-depart", "Jouer des cartes sans tox de départ", "Aucune des 4 cartes n'a de Tox de base.", ChallengeKind.NoInnateTox),
        new("trois-emplacements", "Jouer des cartes avec les 3 emplacements occupés", "Chaque carte a 3 emplacements pris : compétences + Tox de base = 3.", ChallengeKind.ThreeSlots),
        new("moins-de-800-pv", "Jouer des cartes à - de 800 PV", "Chaque carte a moins de 800 PV.", ChallengeKind.HpBelow800),
        new("plus-de-800-pv", "Jouer des cartes à + de 800 PV", "Chaque carte a plus de 800 PV.", ChallengeKind.HpAbove800),
        new("spamtons-25", "Jouer un deck dont la somme de Spamtons est inférieure ou égale à 25", "Le revenu total des 4 cartes est de 25 Spamtons ou moins.", ChallengeKind.SpamtonsAtMost25),
        new("spamtons-40", "Jouer un deck dont la somme de Spamtons est supérieure ou égale à 40", "Le revenu total des 4 cartes est de 40 Spamtons ou plus.", ChallengeKind.SpamtonsAtLeast40),
        new("meme-lettre", "Jouer au moins 2 cartes qui commencent par la même lettre", "Au moins 2 des 4 cartes ont un nom qui commence par la même lettre.", ChallengeKind.SharedFirstLetter),
        new("regard-gauche", "Jouer des cartes qui regardent vers la gauche", "Chaque carte regarde vers la gauche (point de vue de l'observateur).", ChallengeKind.FacingLeft),
        new("regard-droite", "Jouer des cartes qui regardent vers la droite", "Chaque carte regarde vers la droite (point de vue de l'observateur).", ChallengeKind.FacingRight),
        new("quadrupedes", "Jouer des cartes quadripèdes", "Chaque carte représente un animal à quatre pattes.", ChallengeKind.Quadruped),
        new("deux-meme-element", "Jouer au plus 2 cartes du même élément", "Pas plus de 2 cartes d'un même élément dans le deck.", ChallengeKind.AtMostTwoSameElement),
        new("quatre-meme-element", "Jouer 4 cartes du même élément", "Les 4 cartes ont le même élément.", ChallengeKind.FourSameElement),
        new("avec-vetements", "Jouer des cartes qui ont des vêtements", "Chaque carte représente un personnage vêtu.", ChallengeKind.WearsClothes),
        new("sans-vetements", "Jouer des cartes qui n'ont pas de vêtements", "Aucune carte ne représente un personnage vêtu.", ChallengeKind.NoClothes),
        new("deux-animaux", "Jouer des cartes avec au moins 2 animaux représentés", "Chaque carte montre au moins 2 animaux.", ChallengeKind.TwoAnimals),
    };

    /// <summary>Challenges dont la vérification dépend des étiquettes d'illustration (🟡).</summary>
    public static bool UsesTags(ChallengeKind k) => k is ChallengeKind.FacingLeft or ChallengeKind.FacingRight or ChallengeKind.Quadruped
        or ChallengeKind.WearsClothes or ChallengeKind.NoClothes or ChallengeKind.TwoAnimals;

    /// <summary>Règle qui porte sur chaque carte prise seule ; null pour les règles qui portent sur l'ensemble du deck.</summary>
    private static Func<CardView, bool>? PerCard(ChallengeKind k) => k switch
    {
        ChallengeKind.NoInnateTox => v => v.InnateTox == 0,
        ChallengeKind.ThreeSlots => v => v.Slots == 3,
        ChallengeKind.HpBelow800 => v => v.Hp < 800,
        ChallengeKind.HpAbove800 => v => v.Hp > 800,
        ChallengeKind.FacingLeft => v => v.Tag?.Facing == "L",
        ChallengeKind.FacingRight => v => v.Tag?.Facing == "R",
        ChallengeKind.Quadruped => v => v.Tag?.Quadruped == true,
        ChallengeKind.WearsClothes => v => v.Tag?.Clothes == true,
        ChallengeKind.NoClothes => v => v.Tag is { Clothes: false },
        ChallengeKind.TwoAnimals => v => v.Tag?.Animals >= 2,
        _ => null,
    };

    /// <summary>La carte peut-elle faire partie d'un deck de ce challenge, seule ? (règles par carte uniquement ; les autres : toujours vrai)</summary>
    public static bool CardEligible(ChallengeDefinition d, CardView v) => PerCard(d.Kind)?.Invoke(v) ?? true;

    private static bool LoreKnown(string s) => s.Length > 0;

    /// <summary>Le deck de cartes <paramref name="cards"/> respecte-t-il le challenge ? <paramref name="complete"/> : le deck a ses 4 cartes (les règles d'ensemble ne se jugent qu'alors).</summary>
    public static bool Satisfied(ChallengeDefinition d, IReadOnlyList<CardView> cards, bool complete = true)
    {
        if (d.IsCustom) return true;
        if (PerCard(d.Kind) is { } f) return cards.All(f);
        switch (d.Kind)
        {
            case ChallengeKind.DifferentElements: return cards.Select(c => c.Element).Distinct().Count() == cards.Count;
            case ChallengeKind.SameRegion: return cards.All(c => LoreKnown(c.Region)) && cards.Select(c => c.Region).Distinct().Count() <= 1;
            case ChallengeKind.SameSpecies: return cards.All(c => LoreKnown(c.Species)) && cards.Select(c => c.Species).Distinct().Count() <= 1;
            case ChallengeKind.AtMostTwoSameElement: return cards.GroupBy(c => c.Element).All(g => g.Count() <= 2);
            case ChallengeKind.FourSameElement: return cards.Select(c => c.Element).Distinct().Count() <= 1;
            case ChallengeKind.SpamtonsAtMost25: return cards.Sum(c => c.Income) <= 25;
            case ChallengeKind.SpamtonsAtLeast40: return !complete || cards.Sum(c => c.Income) >= 40;
            case ChallengeKind.SharedFirstLetter: return !complete || cards.GroupBy(c => c.Letter).Any(g => g.Count() >= 2);
            default: return true;
        }
    }

    /// <summary>Raisons pour lesquelles le deck ne respecte pas le challenge (vide si tout va bien). Les 4 cartes sont exigées.</summary>
    public static IReadOnlyList<string> Problems(ChallengeDefinition d, IReadOnlyList<CardView> cards)
    {
        var problems = new List<string>();
        if (d.IsCustom) return problems;
        if (cards.Count != DeckSize) { problems.Add($"Un deck de challenge contient {DeckSize} cartes ({cards.Count} choisie{(cards.Count > 1 ? "s" : "")})."); }
        if (PerCard(d.Kind) is { } f)
        {
            var bad = cards.Where(c => !f(c)).Select(c => c.Name).ToList();
            if (bad.Count > 0) problems.Add("Ne respecte pas le challenge : " + string.Join(", ", bad) + ".");
        }
        else if (!Satisfied(d, cards, complete: cards.Count == DeckSize))
            problems.Add("Le deck ne respecte pas le challenge : " + d.Conditions);
        return problems;
    }

    /// <summary>
    /// Peut-on ajouter <paramref name="candidate"/> à <paramref name="current"/> en gardant la possibilité de finir un deck valide ?
    /// Sert à ne proposer que les cartes permises dans l'éditeur de challenge.
    /// </summary>
    public static bool CanAdd(ChallengeDefinition d, IReadOnlyList<CardView> current, CardView candidate, ChallengeContext ctx)
    {
        if (d.IsCustom) return true;
        if (current.Any(c => c.Card.Id == candidate.Card.Id)) return false;
        var next = current.Append(candidate).ToList();
        var remaining = DeckSize - next.Count;
        if (remaining < 0) return false;
        switch (d.Kind)
        {
            case ChallengeKind.SpamtonsAtMost25: return next.Sum(c => c.Income) + remaining * ctx.MinIncome <= 25;
            case ChallengeKind.SpamtonsAtLeast40: return next.Sum(c => c.Income) + remaining * ctx.MaxIncome >= 40;
            case ChallengeKind.SharedFirstLetter: return remaining > 0 || Satisfied(d, next);
            default: return Satisfied(d, next, complete: remaining == 0);
        }
    }

    public static ChallengeDefinition? Find(string id, IEnumerable<ChallengeDefinition>? custom = null) =>
        Defaults.Concat(custom ?? Array.Empty<ChallengeDefinition>()).FirstOrDefault(c => c.Id == id);
}
