namespace SpamDrafter.Simulator.Engine;

/// <summary>
/// Générateur de hasard reproductible (SplitMix64) : même graine = même suite de nombres,
/// quelle que soit la version de .NET (principe 5 de l'architecture).
/// </summary>
public sealed class Rng
{
    private ulong _state;

    public Rng(ulong seed) { _state = seed; }

    /// <summary>Copie exacte : la copie donnera la même suite de nombres.</summary>
    public Rng Clone() => new(0) { _state = _state };

    public ulong NextULong()
    {
        var z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Nombre dans [0 ; 1[.</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Entier dans [min ; max[ (comme System.Random.Next).</summary>
    public int Next(int min, int max) => max <= min ? min : min + (int)(NextDouble() * (max - min));
}
