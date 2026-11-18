using SpamDrafter.App.ViewModels;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.Core.Tests;

/// <summary>Astuces du bandeau de l'accueil : chaque nom écrit entre accolades doit exister dans les données (jamais de lien cassé).</summary>
public class HomeTipsTests
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

    [Fact]
    public void Les_9_astuces_sont_la_et_chaque_nom_colore_est_resolu()
    {
        Assert.Equal(9, HomeTips.All.Count);
        if (Kb() is not { } kb) return;
        var items = kb.Cards.Select(c => new CardItem(c, kb)).ToList();
        foreach (var tip in HomeTips.All)
        {
            var segs = HomeTips.Parse(tip, kb, items);
            Assert.DoesNotContain(segs, s => s.Text.Contains('{'));
            Assert.Equal(tip.Count(c => c == '{'), segs.Count(s => s.IsLink));
        }
        var first = HomeTips.Parse(HomeTips.All[0], kb, items);
        Assert.Contains(first, s => s.Kind == TipKind.Card && s.Text == "Tisseuse de Mots");
        Assert.Contains(first, s => s.Kind == TipKind.Tox && s.Text == "Immunité");
    }
}
