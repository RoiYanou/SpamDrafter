using System.Runtime.CompilerServices;

namespace SpamDrafter.Simulator.Engine;

/// <summary>Journal lisible de la partie, ligne par ligne.</summary>
public sealed class DuelLog
{
    private readonly List<string> _lines = new();

    /// <summary>Faux = journal désactivé (duels en masse, plus rapides).</summary>
    public bool Enabled { get; set; } = true;
    public IReadOnlyList<string> Lines => _lines;

    public void Add(int round, string text)
    {
        if (Enabled) _lines.Add(round > 0 ? $"[R{round}] {text}" : text);
    }

    /// <summary>
    /// Version pour les textes avec « {…} » : quand le journal est désactivé, le texte n'est même pas fabriqué
    /// (phase 8, vitesse : avant, chaque ligne était formatée puis jetée, des millions de fois par génération).
    /// </summary>
    public void Add(int round, [InterpolatedStringHandlerArgument("")] ref LineHandler text)
    {
        if (Enabled) Add(round, text.ToStringAndClear());
    }

    /// <summary>Fabrique le texte seulement si le journal est actif.</summary>
    [InterpolatedStringHandler]
    public ref struct LineHandler
    {
        private DefaultInterpolatedStringHandler _inner;

        public LineHandler(int literalLength, int formattedCount, DuelLog log, out bool isEnabled)
        {
            isEnabled = log.Enabled;
            _inner = isEnabled ? new DefaultInterpolatedStringHandler(literalLength, formattedCount) : default;
        }

        public void AppendLiteral(string s) => _inner.AppendLiteral(s);
        public void AppendFormatted<T>(T value) => _inner.AppendFormatted(value);
        public void AppendFormatted<T>(T value, string? format) => _inner.AppendFormatted(value, format);
        public void AppendFormatted<T>(T value, int alignment) => _inner.AppendFormatted(value, alignment);
        public void AppendFormatted<T>(T value, int alignment, string? format) => _inner.AppendFormatted(value, alignment, format);
        public void AppendFormatted(string? value) => _inner.AppendFormatted(value);
        internal string ToStringAndClear() => _inner.ToStringAndClear();
    }

    public override string ToString() => string.Join(Environment.NewLine, _lines);
}
