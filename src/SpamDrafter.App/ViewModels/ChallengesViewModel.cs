using System.Collections.ObjectModel;
using SpamDrafter.Core.Challenges;

namespace SpamDrafter.App.ViewModels;

/// <summary>Une part de la roue : son texte court, sa couleur et sa place (de 0 à 1, dans le sens des aiguilles d'une montre depuis le haut).</summary>
public sealed record WheelSlice(string Label, string Color, double Start, double End);

/// <summary>Un challenge dans la liste de l'onglet : actif ou non, son poids (sa part de la roue) et ses boutons.</summary>
public sealed class ChallengeEntry : ViewModelBase
{
    private readonly ChallengesViewModel _owner;
    public ChallengeEntry(ChallengeDefinition definition, ChallengesViewModel owner) { Definition = definition; _owner = owner; }

    public ChallengeDefinition Definition { get; }
    public string Title => Definition.Title;
    public string Conditions => Definition.Conditions;
    public bool IsCustom => Definition.IsCustom;
    private static readonly Dictionary<string, string> ShortNames = new()
    {
        ["elements-differents"] = "Éléments différents", ["meme-region"] = "Même région", ["meme-espece"] = "Même espèce",
        ["sans-tox-de-depart"] = "Sans Tox de départ", ["trois-emplacements"] = "3 emplacements", ["moins-de-800-pv"] = "Moins de 800 PV",
        ["plus-de-800-pv"] = "Plus de 800 PV", ["spamtons-25"] = "Spamtons ≤ 25", ["spamtons-40"] = "Spamtons ≥ 40",
        ["meme-lettre"] = "Même lettre", ["regard-gauche"] = "Regard à gauche", ["regard-droite"] = "Regard à droite",
        ["quadrupedes"] = "Quadrupèdes", ["deux-meme-element"] = "2 du même élément max", ["quatre-meme-element"] = "4 du même élément",
        ["avec-vetements"] = "Avec vêtements", ["sans-vetements"] = "Sans vêtements", ["deux-animaux"] = "2 animaux ou plus",
    };
    /// <summary>Texte court écrit sur la roue (le titre complet est dans la liste).</summary>
    public string ShortTitle => ShortNames.TryGetValue(Definition.Id, out var s) ? s : Definition.Title.Trim();

    private bool _enabled = true;
    public bool Enabled { get => _enabled; set { if (Set(ref _enabled, value)) _owner.EntryChanged(this); } }

    private int _weight = ChallengeBook.DefaultWeight;
    private double _sliderMax = 100;
    public int Weight
    {
        get => _weight;
        set
        {
            var v = Math.Clamp(value, 1, ChallengeBook.MaxWeight);
            if (v == _weight) return;
            // Le curseur s'agrandit d'abord (un pourcentage tapé à la main peut dépasser 100), puis la valeur est posée : il se replace toujours au bon endroit.
            var max = Math.Max(100, Math.Ceiling(v / 50.0) * 50);
            if (max != _sliderMax) { _sliderMax = max; Raise(nameof(SliderMax)); }
            _weight = v;
            Raise(nameof(Weight)); Raise(nameof(WeightValue));
            _owner.EntryChanged(this);
        }
    }
    /// <summary>Slider : nombre décimal.</summary>
    public double WeightValue { get => _weight; set => Weight = (int)Math.Round(value); }
    public double SliderMax => _sliderMax;

    private bool _expanded;
    /// <summary>La petite flèche est dépliée : le bouton « Faire un deck avec cette restriction » est visible.</summary>
    public bool IsExpanded { get => _expanded; set { if (Set(ref _expanded, value)) Raise(nameof(Arrow)); } }
    public string Arrow => _expanded ? "▴" : "▾";
    public RelayCommand ToggleCommand => _toggle ??= new RelayCommand(_ => IsExpanded = !IsExpanded);
    private RelayCommand? _toggle;

    // ----- Pourcentage modifiable à la main -----
    private bool _editing;
    public bool IsEditingPercent { get => _editing; private set { if (Set(ref _editing, value)) Raise(nameof(NotEditingPercent)); } }
    public bool NotEditingPercent => !_editing;
    private string _percentEdit = "";
    public string PercentEdit { get => _percentEdit; set => Set(ref _percentEdit, value); }
    public RelayCommand BeginEditCommand => _begin ??= new RelayCommand(_ =>
    {
        if (!Enabled) return;
        PercentEdit = _percent.Replace("%", "").Trim(); IsEditingPercent = true;
    });
    public RelayCommand CommitCommand => _commit ??= new RelayCommand(_ => Commit());
    public RelayCommand CancelEditCommand => _cancel ??= new RelayCommand(_ => IsEditingPercent = false);
    private RelayCommand? _begin, _commit, _cancel;
    /// <summary>Valide le pourcentage tapé : le poids est calculé pour que la part sur la roue soit celle demandée.</summary>
    public void Commit()
    {
        if (!IsEditingPercent) return;
        IsEditingPercent = false;
        var text = _percentEdit.Replace("%", "").Replace(',', '.').Trim();
        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p)) _owner.SetShare(this, p);
    }

    private string _percent = "";
    /// <summary>Part de la roue, « 5,6 % » ; vide si le challenge est désactivé.</summary>
    public string Percent { get => _percent; set => Set(ref _percent, value); }

    public string Color { get; set; } = "#888888";

    public void RaiseWeight() { Raise(nameof(Weight)); Raise(nameof(WeightValue)); }
}

/// <summary>
/// Onglet Challenges : une roue animée qui tire un challenge au hasard (selon les poids réglés), la liste des challenges (par défaut et
/// les vôtres), des pré-sets de roue, et un bouton « Deck » par challenge qui ouvre l'éditeur de deck restreint (Deck Builder).
/// </summary>
public sealed class ChallengesViewModel : ViewModelBase
{
    private readonly ChallengeBook _book;
    private readonly string _path;
    private readonly Func<double> _roll;
    private bool _loading;

    public ChallengesViewModel(string path, Func<double>? roll = null)
    {
        _path = path; _roll = roll ?? (() => Random.Shared.NextDouble());
        _book = ChallengeBook.Load(path);
        _loading = true;
        Rebuild();
        _loading = false;
        Recompute();
        ReloadPresetNames();
        SpinCommand = new RelayCommand(_ => Spin(), _ => CanSpin);
        AddCommand = new RelayCommand(_ => AddCustom());
        DeleteCommand = new RelayCommand(p => { if (p is ChallengeEntry e) DeleteCustom(e); });
        OpenEditorCommand = new RelayCommand(p => { if (p is ChallengeEntry e) OpenEditor?.Invoke(e.Definition); });
        OpenResultCommand = new RelayCommand(_ => { if (Result is { } r) OpenEditor?.Invoke(r.Definition); }, _ => Result is not null);
        SavePresetCommand = new RelayCommand(_ => SavePreset());
        DeletePresetCommand = new RelayCommand(_ => DeletePreset(), _ => CanDeletePreset);
        ApplyPresetCommand = new RelayCommand(_ => ApplyPreset());
        EqualChancesCommand = new RelayCommand(_ => EqualChances());
    }

    public ObservableCollection<ChallengeEntry> Entries { get; } = new();
    public ObservableCollection<string> PresetNames { get; } = new();

    /// <summary>Demande l'éditeur de deck restreint pour ce challenge (branché sur le Deck Builder).</summary>
    public event Action<ChallengeDefinition>? OpenEditor;
    /// <summary>La roue doit tourner : l'angle de la part gagnante (de 0 à 1 le long du tour) qui doit finir sous la flèche.</summary>
    public event Action<double>? SpinRequested;
    /// <summary>La liste des parts a changé : la roue se redessine.</summary>
    public event Action? SlicesChanged;

    public RelayCommand SpinCommand { get; }
    public RelayCommand AddCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand OpenEditorCommand { get; }
    public RelayCommand OpenResultCommand { get; }
    public RelayCommand SavePresetCommand { get; }
    public RelayCommand DeletePresetCommand { get; }
    public RelayCommand ApplyPresetCommand { get; }
    public RelayCommand EqualChancesCommand { get; }

    /// <summary>« Chances égales » : tous les challenges reprennent le même poids (les curseurs et les pourcentages suivent).</summary>
    public void EqualChances()
    {
        _loading = true;
        foreach (var e in Entries) { e.Weight = ChallengeBook.DefaultWeight; _book.Weights[e.Definition.Id] = ChallengeBook.DefaultWeight; }
        _loading = false;
        _book.CurrentPreset = null; Raise(nameof(SelectedPreset));
        Recompute(); Save();
    }

    // ----- Roue -----
    public IReadOnlyList<WheelSlice> Slices { get; private set; } = Array.Empty<WheelSlice>();
    private IReadOnlyList<ChallengeEntry> _active = Array.Empty<ChallengeEntry>();
    public bool CanSpin => !_spinning && _active.Count > 0;
    public bool HasNoActive => _active.Count == 0;

    private bool _spinning;
    public bool IsSpinning { get => _spinning; private set { if (Set(ref _spinning, value)) { Raise(nameof(CanSpin)); SpinCommand.RaiseCanExecuteChanged(); } } }

    private ChallengeEntry? _result, _pending;
    /// <summary>Challenge tiré par la roue (affiché une fois la roue arrêtée).</summary>
    public ChallengeEntry? Result
    {
        get => _result;
        private set { if (Set(ref _result, value)) { Raise(nameof(HasResult)); Raise(nameof(ResultTitle)); Raise(nameof(ResultConditions)); OpenResultCommand.RaiseCanExecuteChanged(); } }
    }
    public bool HasResult => _result is not null;
    public string ResultTitle => _result?.Title ?? "";
    public string ResultConditions => _result?.Conditions ?? "";
    /// <summary>Texte d'aide sous la roue.</summary>
    public string Hint => IsSpinning ? "La roue tourne…" : HasNoActive ? "Active au moins un challenge dans la liste." : HasResult ? "" : "Lance la roue pour tirer un challenge.";

    /// <summary>Lance la roue : le challenge gagnant est tiré tout de suite (selon les poids), la roue tourne ensuite jusqu'à lui.</summary>
    public void Spin()
    {
        if (!CanSpin) return;
        var weights = _active.Select(e => e.Weight).ToList();
        var i = WheelMath.Pick(weights, _roll());
        if (i < 0) return;
        var s = Slices[i];
        // Un point au hasard dans la part, sans toucher aux bords.
        var at = s.Start + (s.End - s.Start) * (0.15 + 0.7 * _roll());
        _pending = _active[i];
        Result = null;
        IsSpinning = true; Raise(nameof(Hint));
        if (SpinRequested is { } h) h(at); else FinishSpin();
    }

    /// <summary>La roue s'est arrêtée (appelé par la vue) : le challenge tiré apparaît.</summary>
    public void FinishSpin()
    {
        Result = _pending; _pending = null;
        IsSpinning = false; Raise(nameof(Hint));
    }

    // ----- Liste -----
    private void Rebuild()
    {
        Entries.Clear();
        foreach (var d in _book.AllDefinitions())
            Entries.Add(new ChallengeEntry(d, this) { Color = "#888888" }.With(e => { e.Enabled = _book.IsEnabled(d.Id); e.Weight = _book.WeightOf(d.Id) < 1 ? 1 : _book.WeightOf(d.Id); }));
    }

    /// <summary>Règle le poids de <paramref name="e"/> pour que sa part de la roue soit <paramref name="percent"/> % (les autres gardent leurs poids).</summary>
    internal void SetShare(ChallengeEntry e, double percent)
    {
        var others = Entries.Where(x => x.Enabled && x != e).Sum(x => x.Weight);
        if (!e.Enabled || others == 0) return;
        var p = Math.Clamp(percent, 0.5, 95);
        e.Weight = (int)Math.Round(p / (100 - p) * others);
        Recompute();   // même si le poids est resté le même (valeur arrondie), le texte reprend la part réelle
    }

    internal void EntryChanged(ChallengeEntry e)
    {
        if (_loading) return;
        _book.Weights[e.Definition.Id] = e.Weight;
        _book.Disabled.RemoveAll(id => id == e.Definition.Id);
        if (!e.Enabled) _book.Disabled.Add(e.Definition.Id);
        _book.CurrentPreset = null; Raise(nameof(SelectedPreset));
        Recompute();
        Save();
    }

    private void Recompute()
    {
        _active = Entries.Where(e => e.Enabled).ToList();
        var slices = WheelMath.Slices(_active.Select(e => e.Weight).ToList());
        var list = new List<WheelSlice>();
        for (var i = 0; i < _active.Count; i++)
        {
            var color = HueColor(i, _active.Count);
            _active[i].Color = color;
            list.Add(new WheelSlice(_active[i].ShortTitle, color, slices[i].Start, slices[i].End));
        }
        foreach (var e in Entries)
        {
            var idx = _active.ToList().IndexOf(e);
            e.Percent = idx < 0 ? "" : ((slices[idx].End - slices[idx].Start) * 100).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " %";
        }
        Slices = list;
        Raise(nameof(Slices)); Raise(nameof(CanSpin)); Raise(nameof(HasNoActive)); Raise(nameof(Hint));
        SpinCommand?.RaiseCanExecuteChanged();
        SlicesChanged?.Invoke();
    }

    /// <summary>Couleur d'une part : la teinte avance régulièrement le long de la roue.</summary>
    public static string HueColor(int index, int count)
    {
        var h = count <= 0 ? 0 : index * 360.0 / count;
        // Alternance de clarté pour que deux parts voisines se distinguent même avec des teintes proches.
        var (s, v) = index % 2 == 0 ? (0.62, 0.92) : (0.78, 0.78);
        var c = (v * s); var x = c * (1 - Math.Abs(h / 60 % 2 - 1)); var m = v - c;
        var (r, g, b) = h switch { < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x), < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x) };
        return $"#{(int)Math.Round((r + m) * 255):X2}{(int)Math.Round((g + m) * 255):X2}{(int)Math.Round((b + m) * 255):X2}";
    }

    private void Save() { if (!_loading) _book.TrySave(_path); }

    // ----- Challenges du joueur -----
    private string _newTitle = "", _newConditions = "", _addMessage = "";
    public string NewTitle { get => _newTitle; set { if (Set(ref _newTitle, value)) AddMessage = ""; } }
    public string NewConditions { get => _newConditions; set => Set(ref _newConditions, value); }
    public string AddMessage { get => _addMessage; private set => Set(ref _addMessage, value); }

    private void AddCustom()
    {
        var title = NewTitle.Trim();
        if (title.Length == 0) { AddMessage = "Écris d'abord le nom du challenge."; return; }
        if (Entries.Any(e => string.Equals(e.Title, title, StringComparison.CurrentCultureIgnoreCase))) { AddMessage = "Un challenge porte déjà ce nom."; return; }
        var c = new CustomChallenge { Title = title, Conditions = NewConditions.Trim() };
        _book.Custom.Add(c);
        var entry = new ChallengeEntry(c.ToDefinition(), this);
        Entries.Add(entry);   // actif d'office, au poids par défaut
        NewTitle = ""; NewConditions = "";
        AddMessage = "Challenge ajouté. SpamDrafter ne vérifie pas les decks de tes challenges : à toi de respecter la règle.";
        _book.CurrentPreset = null; Raise(nameof(SelectedPreset));
        Recompute(); Save();
    }

    private void DeleteCustom(ChallengeEntry e)
    {
        if (!e.IsCustom) return;
        _book.Custom.RemoveAll(c => c.Id == e.Definition.Id);
        _book.Weights.Remove(e.Definition.Id); _book.Disabled.Remove(e.Definition.Id);
        foreach (var p in _book.Presets) p.Weights.Remove(e.Definition.Id);
        Entries.Remove(e);
        if (_result == e) Result = null;
        Recompute(); Save();
    }

    // ----- Pré-sets -----
    private string _presetName = "", _presetMessage = "";
    public string PresetName { get => _presetName; set => Set(ref _presetName, value); }
    public string PresetMessage { get => _presetMessage; private set => Set(ref _presetMessage, value); }

    private string? _selectedPreset;
    /// <summary>Pré-set choisi dans la liste ; le choisir l'applique à la roue.</summary>
    public string? SelectedPreset
    {
        get => _book.CurrentPreset;
        set { if (value is null || value == _book.CurrentPreset) return; _selectedPreset = value; ApplyPreset(); }
    }
    public bool CanDeletePreset => SelectedPreset is { } n && _book.Presets.Any(p => p.Name == n);

    private void ReloadPresetNames()
    {
        PresetNames.Clear();
        PresetNames.Add(ChallengeBook.DefaultPresetName);
        foreach (var p in _book.Presets) PresetNames.Add(p.Name);
        Raise(nameof(SelectedPreset)); Raise(nameof(CanDeletePreset)); DeletePresetCommand?.RaiseCanExecuteChanged();
    }

    private void ApplyPreset()
    {
        var name = _selectedPreset;
        if (name is null) return;
        if (name == ChallengeBook.DefaultPresetName) _book.ApplyDefault();
        else if (_book.Presets.FirstOrDefault(p => p.Name == name) is { } p) _book.Apply(p);
        else return;
        _loading = true;
        foreach (var e in Entries) { e.Enabled = _book.IsEnabled(e.Definition.Id); e.Weight = Math.Max(1, _book.WeightOf(e.Definition.Id)); }
        _loading = false;
        _book.CurrentPreset = name;
        Recompute(); Save();
        Raise(nameof(SelectedPreset)); Raise(nameof(CanDeletePreset)); DeletePresetCommand.RaiseCanExecuteChanged();
        PresetMessage = $"Pré-set « {name} » appliqué.";
    }

    private void SavePreset()
    {
        var name = PresetName.Trim();
        if (name.Length == 0) { PresetMessage = "Donne un nom au pré-set."; return; }
        if (string.Equals(name, ChallengeBook.DefaultPresetName, StringComparison.CurrentCultureIgnoreCase)) { PresetMessage = "Ce nom est réservé."; return; }
        _book.SavePreset(name);
        _selectedPreset = name;
        ReloadPresetNames();
        PresetName = "";
        PresetMessage = $"Pré-set « {name} » enregistré.";
        Save();
    }

    private void DeletePreset()
    {
        if (SelectedPreset is not { } n || !_book.Presets.Any(p => p.Name == n)) return;
        _book.Presets.RemoveAll(p => p.Name == n);
        _book.CurrentPreset = null; _selectedPreset = null;
        ReloadPresetNames();
        PresetMessage = $"Pré-set « {n} » supprimé.";
        Save();
    }
}

internal static class EntryExtensions
{
    public static T With<T>(this T item, Action<T> init) { init(item); return item; }
}
