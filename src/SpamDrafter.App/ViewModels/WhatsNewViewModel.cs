using SpamDrafter.Core.Knowledge;
using SpamDrafter.Core.Settings;
using SpamDrafter.Core.Versions;

namespace SpamDrafter.App.ViewModels;

/// <summary>
/// « Quoi de neuf » (phase 12, lot E) : quand les données du jeu passent à une autre version de SPAM, un bandeau résume ce qui a changé
/// (cartes, talents) et combien de decks du joueur sont touchés, jusqu'à ce qu'il le ferme. La version déjà vue est retenue dans les
/// réglages. À la toute première ouverture (rien de retenu), la version actuelle devient la version « déjà vue » sans bandeau :
/// on ne peut pas savoir de quelle version le joueur vient.
/// </summary>
public sealed class WhatsNewViewModel : ViewModelBase
{
    private readonly KnowledgeBase _kb;
    private readonly AppSettings _settings;
    private readonly Action _save;
    private readonly Func<int> _impactedDecks;
    private bool _visible;
    private bool _showDetail;

    public WhatsNewViewModel(KnowledgeBase kb, AppSettings settings, Action save, Func<int> impactedDecks)
    {
        _kb = kb; _settings = settings; _save = save; _impactedDecks = impactedDecks;
        DismissCommand = new RelayCommand(_ => Dismiss());
        ToggleDetailCommand = new RelayCommand(_ => ShowDetail = !ShowDetail);
        ShowImpactedCommand = new RelayCommand(_ => ShowImpactedRequested?.Invoke());
        Refresh();
    }

    public RelayCommand DismissCommand { get; }
    public RelayCommand ToggleDetailCommand { get; }
    public RelayCommand ShowImpactedCommand { get; }
    /// <summary>Demande d'afficher les decks touchés (Deck builder, filtre « touchés par une mise à jour »).</summary>
    public event Action? ShowImpactedRequested;

    public bool IsVisible { get => _visible; private set => Set(ref _visible, value); }
    public bool ShowDetail
    {
        get => _showDetail;
        private set { if (Set(ref _showDetail, value)) { Raise(nameof(DetailButtonLabel)); Raise(nameof(DetailVisible)); } }
    }
    public string DetailButtonLabel => ShowDetail ? "Masquer le détail" : "Voir le détail";
    public bool DetailVisible => ShowDetail && Lines.Count > 0;
    public string Title { get; private set; } = "";
    public string Summary { get; private set; } = "";
    public IReadOnlyList<string> Lines { get; private set; } = Array.Empty<string>();
    public bool HasImpactedDecks { get; private set; }
    public string ImpactedLabel { get; private set; } = "";

    /// <summary>Recalcule le bandeau (au démarrage, et si les decks changent).</summary>
    public void Refresh()
    {
        var current = _kb.Manifest.GameVersion;
        var last = _settings.LastSeenGameVersion;
        if (string.IsNullOrEmpty(last))
        {
            _settings.LastSeenGameVersion = current;   // première ouverture : rien à montrer
            _save();
            IsVisible = false;
            return;
        }
        if (last == current) { IsVisible = false; return; }

        Title = $"SPAM est passé de la version {last} à la version {current}";
        if (_kb.ChangesFrom(last) is { } cs)
        {
            Lines = ChangeText.Summary(cs, _kb);
            var cards = cs.Cards.Count; var talents = cs.Talents.Count;
            Summary = cs.IsEmpty ? "Aucune carte ni aucun talent n'a changé."
                : $"{cards} carte(s) et {talents} talent(s) concernés.";
        }
        else
        {
            Lines = Array.Empty<string>();
            Summary = "Les différences entre ces deux versions ne sont pas disponibles.";
        }
        var impacted = _impactedDecks();
        HasImpactedDecks = impacted > 0;
        ImpactedLabel = impacted > 0 ? $"Voir mes decks touchés ({impacted})" : "";
        foreach (var n in new[] { nameof(Title), nameof(Summary), nameof(Lines), nameof(DetailVisible), nameof(HasImpactedDecks), nameof(ImpactedLabel) }) Raise(n);
        IsVisible = true;
    }

    /// <summary>Le joueur a lu : la version actuelle devient la version « déjà vue ».</summary>
    public void Dismiss()
    {
        _settings.LastSeenGameVersion = _kb.Manifest.GameVersion;
        _save();
        ShowDetail = false;
        IsVisible = false;
    }
}
