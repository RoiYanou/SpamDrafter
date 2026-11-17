using SpamDrafter.App.ViewModels;
using SpamDrafter.Core.Knowledge;
using SpamDrafter.Core.Text;

namespace SpamDrafter.Core.Tests;

/// <summary>Phase 12, lot A : recherche sans accents, par capacité et par talent.</summary>
public class SearchTests
{
    private static KnowledgeBase? Kb()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "knowledge", "1.30");
            if (File.Exists(Path.Combine(p, "manifest.json"))) return KnowledgeBase.Load(p);
        }
        return null;
    }

    [Theory]
    [InlineData("Panthère imaginaire", "panthere imaginaire")]
    [InlineData("  ÉTERNEL  ", "eternel")]
    [InlineData("Piou-piou-piou", "piou piou piou")]
    [InlineData("Cœur d'Abîme", "coeur d abime")]
    [InlineData("", "")]
    public void Normalize_retire_accents_casse_et_separateurs(string input, string expected) =>
        Assert.Equal(expected, TextSearch.Normalize(input));

    [Fact]
    public void Tous_les_mots_doivent_etre_presents_dans_n_importe_quel_ordre()
    {
        var hay = TextSearch.Normalize("Panthère imaginaire Souffle de feu");
        Assert.True(TextSearch.ContainsAllWords(hay, "PANTHERE feu"));
        Assert.True(TextSearch.ContainsAllWords(hay, "feu panthere"));
        Assert.False(TextSearch.ContainsAllWords(hay, "panthere eau"));
        Assert.True(TextSearch.ContainsAllWords(hay, "   "));
    }

    private static List<string> Find(KnowledgeBase kb, string query) =>
        kb.Cards.Select(c => new CardItem(c, kb)).Where(i => i.Matches(query, null, null)).Select(i => i.Name).ToList();

    [Fact]
    public void Une_carte_se_trouve_sans_accent()
    {
        if (Kb() is not { } kb) return;
        Assert.Contains("Panthère imaginaire", Find(kb, "panthere"));
        Assert.Contains("Panthère imaginaire", Find(kb, "PANTHERE IMAGINAIRE"));
    }

    [Fact]
    public void Une_carte_se_trouve_par_sa_capacite_active()
    {
        if (Kb() is not { } kb) return;
        Assert.Contains("Oisillons de Midas", Find(kb, "piou-piou-piou"));
        Assert.Contains("Oisillons de Midas", Find(kb, "piou piou piou"));
    }

    [Fact]
    public void Une_carte_se_trouve_par_un_de_ses_talents()
    {
        if (Kb() is not { } kb) return;
        var card = kb.Cards.First(c => kb.CardName(c) == "Oisillons de Midas");
        var talent = kb.TalentName(card.Talents[1]);
        Assert.Contains("Oisillons de Midas", Find(kb, talent));
        Assert.Contains("Oisillons de Midas", Find(kb, TextSearch.Normalize(talent)));
    }

    [Fact]
    public void Une_recherche_sans_resultat_est_vide_et_une_recherche_vide_garde_tout()
    {
        if (Kb() is not { } kb) return;
        Assert.Empty(Find(kb, "zzzzqqqq"));
        Assert.Equal(kb.Cards.Count, Find(kb, "").Count);
    }
}
