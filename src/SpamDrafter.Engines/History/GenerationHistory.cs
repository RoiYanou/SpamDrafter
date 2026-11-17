using System.Text.Json;
using System.Text.Json.Serialization;
using SpamDrafter.Simulator.Engine;

namespace SpamDrafter.Engines.History;

public enum HistoryKind { Generation, Report }

/// <summary>
/// Un résultat gardé en mémoire (phase 12, lot B) : les propositions d'une génération, ou la fiche / l'analyse d'un deck.
/// Les moteurs sont reproductibles : une même demande avec le même contexte redonne exactement ce résultat.
/// </summary>
public sealed class HistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public HistoryKind Kind { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Identifie la demande (cartes, talents, effort) : deux demandes identiques ont la même clé.</summary>
    public string Key { get; set; } = "";
    /// <summary>Texte court affiché dans l'historique.</summary>
    public string Label { get; set; } = "";
    public string Effort { get; set; } = "";
    /// <summary>Génération : les cartes imposées (talent vide = au choix du Cuisinier). Rapport : le deck analysé.</summary>
    public List<DuelSlot> Slots { get; set; } = new();
    public List<GeneratedDeck>? Proposals { get; set; }
    public bool Complete { get; set; }
    public string? Title { get; set; }
    public string? Sheet { get; set; }
    public string? Detail { get; set; }
    public int Duels { get; set; }
    public double ElapsedSeconds { get; set; }
}

/// <summary>
/// Historique et mémoire des générations et des rapports. Garde les dernières demandes (au plus <c>maxPerKind</c> de chaque
/// sorte) dans un fichier JSON. Les résultats faits avec un autre contexte (autre version de SPAM, autre méta, autres poids
/// du Grand Joueur, autre version des moteurs, voir <see cref="Coordinator.CacheContext"/>) sont oubliés : on ne
/// réutilise jamais un résultat qui ne serait plus exactement celui qu'un nouveau calcul donnerait.
/// Un fichier absent ou abîmé donne un historique vide : jamais d'erreur pour le joueur.
/// </summary>
public sealed class GenerationHistory
{
    private sealed class FileContent
    {
        public string Context { get; set; } = "";
        public List<HistoryEntry> Entries { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _lock = new();
    private readonly string _path;
    private readonly string _context;
    private readonly int _maxPerKind;
    private List<HistoryEntry> _entries = new();

    public GenerationHistory(string path, string context, int maxPerKind = 30)
    {
        _path = path; _context = context; _maxPerKind = maxPerKind;
        Load();
    }

    /// <summary>Déclenché après chaque ajout, retrait ou vidage.</summary>
    public event EventHandler? Changed;

    /// <summary>Entrées, la plus récente d'abord.</summary>
    public IReadOnlyList<HistoryEntry> Entries { get { lock (_lock) return _entries.ToList(); } }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var content = JsonSerializer.Deserialize<FileContent>(File.ReadAllText(_path), Options);
            if (content is null || content.Context != _context) return;   // autre contexte : on repart de zéro
            _entries = content.Entries.OrderByDescending(e => e.CreatedUtc).ToList();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { _entries = new(); }
    }

    // ----- Clés des demandes -----
    private static string SlotsKey(IEnumerable<DuelSlot> slots) => string.Join(",", slots.Select(s => $"{s.CardId}:{s.TalentId}"));

    public static string GenerationKey(IReadOnlyList<DuelSlot> selection, Effort effort) => $"gen|{effort}|{SlotsKey(selection)}";

    /// <summary>La fiche joueur ne dépend pas de l'effort ; l'analyse complète, si.</summary>
    public static string ReportKey(IReadOnlyList<DuelSlot> deck, bool complete, Effort effort) =>
        complete ? $"full|{effort}|{SlotsKey(deck)}" : $"sheet|{SlotsKey(deck)}";

    // ----- Lecture -----
    public HistoryEntry? Find(string key)
    {
        lock (_lock) return _entries.FirstOrDefault(e => e.Key == key);
    }

    public HistoryEntry? FindGeneration(IReadOnlyList<DuelSlot> selection, Effort effort) => Find(GenerationKey(selection, effort));
    public HistoryEntry? FindReport(IReadOnlyList<DuelSlot> deck, bool complete, Effort effort) => Find(ReportKey(deck, complete, effort));

    // ----- Écriture -----
    public HistoryEntry AddGeneration(IReadOnlyList<DuelSlot> selection, Effort effort, string label, IReadOnlyList<GeneratedDeck> proposals) =>
        Add(new HistoryEntry
        {
            Kind = HistoryKind.Generation, Key = GenerationKey(selection, effort), Label = label, Effort = effort.ToString(),
            Slots = selection.ToList(), Proposals = proposals.ToList(),
        });

    public HistoryEntry AddReport(IReadOnlyList<DuelSlot> deck, bool complete, Effort effort, string label, string title,
        string sheet, string detail, int duels, double elapsedSeconds) =>
        Add(new HistoryEntry
        {
            Kind = HistoryKind.Report, Key = ReportKey(deck, complete, effort), Label = label, Effort = effort.ToString(),
            Slots = deck.ToList(), Complete = complete, Title = title, Sheet = sheet, Detail = detail,
            Duels = duels, ElapsedSeconds = elapsedSeconds,
        });

    private HistoryEntry Add(HistoryEntry entry)
    {
        lock (_lock)
        {
            _entries.RemoveAll(e => e.Key == entry.Key);   // une même demande n'apparaît qu'une fois, en tête
            _entries.Insert(0, entry);
            foreach (var kind in new[] { HistoryKind.Generation, HistoryKind.Report })
            {
                var extra = _entries.Where(e => e.Kind == kind).Skip(_maxPerKind).ToList();
                foreach (var e in extra) _entries.Remove(e);
            }
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    public void Remove(Guid id)
    {
        lock (_lock) { if (_entries.RemoveAll(e => e.Id == id) > 0) Save(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_lock) { _entries.Clear(); Save(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Écrit le fichier ; une erreur d'écriture est ignorée (la mémoire sert seulement à gagner du temps).</summary>
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new FileContent { Context = _context, Entries = _entries }, Options));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
