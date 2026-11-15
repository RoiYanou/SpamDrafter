using Avalonia.Media.Imaging;
using SpamDrafter.Core.Knowledge;

namespace SpamDrafter.App.ViewModels;

public sealed record TalentLine(string Name, string Description);

/// <summary>Onglet Bibliothèque : liste filtrable + fiche détaillée de la carte choisie.</summary>
public sealed class LibraryViewModel : ViewModelBase
{
    private readonly KnowledgeBase _kb;
    private Bitmap? _image;

    public LibraryViewModel(CardBrowser browser, KnowledgeBase kb)
    {
        Browser = browser; _kb = kb;
        browser.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CardBrowser.Selected)) UpdateDetail();
        };
    }

    public CardBrowser Browser { get; }

    public Bitmap? Image { get => _image; private set => Set(ref _image, value); }
    public string Title { get; private set; } = "Choisis une carte dans la liste";
    public string Stats { get; private set; } = "";
    public string Resistances { get; private set; } = "";
    public IReadOnlyList<string> Abilities { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<TalentLine> Talents { get; private set; } = Array.Empty<TalentLine>();

    private void UpdateDetail()
    {
        var item = Browser.Selected;
        if (item is null) return;
        var c = item.Card;
        Title = item.Name;
        Stats = $"{item.Summary} · Revenu : {c.GoldIncome}";
        Resistances = string.Join(" · ", _kb.Elements
            .Where(e => c.Resistances.ContainsKey(e.Id))
            .Select(e => $"{_kb.ElementName(e.Id)} {c.Resistances[e.Id]}"));
        Abilities = c.Abilities.Select(a => _kb.Text("Card.Ability." + a)).ToList();
        Talents = c.Talents.Select(t => new TalentLine(_kb.TalentName(t), DescribeTalent(t))).ToList();
        Image = item.LoadFullImage();
        foreach (var n in new[] { nameof(Title), nameof(Stats), nameof(Resistances), nameof(Abilities), nameof(Talents) })
            Raise(n);
    }

    /// <summary>
    /// Les descriptions contiennent des emplacements de valeurs (« &lt;value1&gt; ») dont le calcul relève du moteur de règles.
    /// Tant qu'ils ne sont pas résolus, on n'affiche pas un texte à trous.
    /// </summary>
    internal string DescribeTalent(string id)
    {
        var d = _kb.TalentDescription(id);
        return d.Contains('<') ? "(description chiffrée : à venir)" : d;
    }
}
