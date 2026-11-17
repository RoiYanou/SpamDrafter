using System.Runtime.InteropServices;

namespace SpamDrafter.App.ViewModels;

public enum SoundKind { Click, Select, Tab, Success, Error }

/// <summary>
/// Effets sonores doux et discrets (phase 13, D-078). Les sons sont fabriqués par le programme (petites notes sinusoïdales qui
/// s'éteignent vite) : aucun fichier, aucun droit à gérer, jamais de son tiré du jeu (D-019). Windows seulement pour l'instant
/// (D-012) ; ailleurs, silence. Un son qui échoue ne gêne jamais l'application.
/// </summary>
public static class Sounds
{
    private const int Rate = 22050;
    private static readonly Dictionary<SoundKind, byte[]> Cache = new();
    private static double _volume = 0.5;

    public static bool Enabled { get; set; } = true;

    /// <summary>Volume de 0 à 1 (les sons déjà fabriqués sont refaits au prochain besoin).</summary>
    public static double Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0, 1); lock (Cache) Cache.Clear(); }
    }

    public static void Play(SoundKind kind)
    {
        if (!Enabled || _volume <= 0 || !OperatingSystem.IsWindows()) return;
        try
        {
            byte[] wav;
            lock (Cache)
            {
                if (!Cache.TryGetValue(kind, out wav!)) Cache[kind] = wav = Build(kind, _volume);
            }
            PlaySound(wav, IntPtr.Zero, 0x0004 | 0x0001 | 0x0002);   // SND_MEMORY | SND_ASYNC | SND_NODEFAULT
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException) { }
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    private static extern bool PlaySound(byte[] sound, IntPtr module, int flags);

    /// <summary>Notes (fréquence en Hz, début en ms, durée en ms) de chaque son.</summary>
    private static (double Hz, int Start, int Length)[] Notes(SoundKind kind) => kind switch
    {
        SoundKind.Click => new[] { (520.0, 0, 70) },
        SoundKind.Select => new[] { (700.0, 0, 45) },
        SoundKind.Tab => new[] { (600.0, 0, 70), (800.0, 60, 90) },
        SoundKind.Success => new[] { (660.0, 0, 180), (880.0, 90, 240) },
        _ => new[] { (180.0, 0, 200) },
    };

    /// <summary>Fichier WAV (16 bits, mono) du son : chaque note s'éteint de façon exponentielle.</summary>
    public static byte[] Build(SoundKind kind, double volume)
    {
        var notes = Notes(kind);
        var total = notes.Max(n => n.Start + n.Length) * Rate / 1000;
        var samples = new double[total];
        foreach (var (hz, start, length) in notes)
        {
            var from = start * Rate / 1000;
            var count = length * Rate / 1000;
            for (var i = 0; i < count && from + i < total; i++)
            {
                var t = (double)i / count;
                var attack = Math.Min(1, i / (Rate * 0.004));                 // 4 ms d'attaque : pas de « clic » parasite
                samples[from + i] += Math.Sin(2 * Math.PI * hz * i / Rate) * Math.Exp(-5 * t) * attack;
            }
        }
        var data = new byte[total * 2];
        for (var i = 0; i < total; i++)
        {
            var v = (short)(Math.Clamp(samples[i], -1, 1) * volume * 0.35 * short.MaxValue);   // 0,35 : sons discrets même à fond
            data[i * 2] = (byte)(v & 0xFF); data[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + data.Length); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(data.Length); w.Write(data);
        return ms.ToArray();
    }
}
