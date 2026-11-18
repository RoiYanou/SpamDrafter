using System.Runtime.InteropServices;

namespace SpamDrafter.App.ViewModels;

public enum SoundKind { Click, Select, Tab, Success, Error, Pop, Page, Title1, Title2, Title3, Metal1, Metal2, Metal3 }

/// <summary>
/// Effets sonores doux et ludiques : des fichiers .wav ou .ogg embarqués (dossier Assets/Sounds), lus puis joués ;
/// un son dont le fichier manque retombe sur un son fabriqué par le programme. Jamais de son tiré du jeu (D-019). Windows seulement pour
/// l'instant (D-012) ; ailleurs, silence. Un son qui échoue ne gêne jamais l'application.
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

    /// <summary>Prépare tous les sons à l'avance (à appeler hors du fil de l'interface) : le premier son joué ne fait plus attendre.</summary>
    public static void Warm()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            foreach (var kind in Enum.GetValues<SoundKind>())
            {
                lock (Cache) if (Cache.ContainsKey(kind)) continue;
                var wav = Build(kind, _volume);
                lock (Cache) Cache[kind] = wav;
            }
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or ArgumentException) { }
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

    /// <summary>Un son à tirer au hasard parmi plusieurs qui se ressemblent (titre de l'application).</summary>
    public static void PlayAnyTitle() => Play((SoundKind)((int)SoundKind.Title1 + Random.Shared.Next(3)));

    private static SoundKind _lastMetal = SoundKind.Metal3;

    /// <summary>Un bruitage métallique tiré au hasard parmi trois qui se ressemblent (clic sur la carte de la fiche) ; jamais deux fois le même d'affilée.</summary>
    public static void PlayAnyMetal()
    {
        SoundKind kind;
        do kind = (SoundKind)((int)SoundKind.Metal1 + Random.Shared.Next(3)); while (kind == _lastMetal);
        _lastMetal = kind;
        Play(kind);
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    private static extern bool PlaySound(byte[] sound, IntPtr module, int flags);

    // ----- Banque de sons embarqués -----

    /// <summary>
    /// Fichier embarqué de chaque son et vitesse de
    /// lecture (1 = d'origine ; plus bas = plus grave). Choisis pour leur douceur : notes pincées, « bongs » graves, petits clics ronds.
    /// </summary>
    private static readonly Dictionary<SoundKind, (string File, double Rate)> Bank = new()
    {
        // Vitesses < 1 : les sons sont ralentis, donc plus graves et plus ronds (retour du joueur : « trop aigus »).
        [SoundKind.Click] = ("select_007", 0.62),
        [SoundKind.Select] = ("toggle_004", 0.65),
        [SoundKind.Tab] = ("toggle_002", 0.68),
        [SoundKind.Pop] = ("pluck_001", 0.68),
        [SoundKind.Success] = ("confirmation_001", 0.75),
        [SoundKind.Error] = ("error_008", 0.8),
        [SoundKind.Title1] = ("pluck_002", 0.66),
        [SoundKind.Title2] = ("pluck_002", 0.76),
        [SoundKind.Title3] = ("pluck_002", 0.88),
        // Page qui se tourne : .ogg lu avec NVorbis.
        [SoundKind.Page] = ("book_flip_2", 1.0),
        // Trois bruits métalliques (clic sur la carte de la fiche) : fonds de casseroles en cuivre (7 et 8 cm) et de poêle en aluminium (26 cm).
        [SoundKind.Metal1] = ("metal_cuivre_7cm", 1.0),
        [SoundKind.Metal2] = ("metal_cuivre_8cm", 1.0),
        [SoundKind.Metal3] = ("metal_alu_26cm", 1.0),
    };

    private const double MaxSeconds = 0.38;   // moins de 400 ms (directives §5) : un son plus long est coupé en douceur

    /// <summary>Lit un son embarqué (.wav PCM 16 bits ou .ogg) en mono flottant ; null s'il manque ou s'il est illisible.</summary>
    private static (float[] Samples, int Rate)? LoadMono(string file)
    {
        var asm = typeof(Sounds).Assembly;
        using var wavStream = asm.GetManifestResourceStream($"Sounds/{file}.wav");
        if (wavStream is not null)
        {
            using var ms = new MemoryStream();
            wavStream.CopyTo(ms);
            var wav = ms.ToArray();
            int channels = 0, rate = 0, bits = 0, dataAt = -1, dataLen = 0;
            for (var pos = 12; pos + 8 <= wav.Length;)
            {
                var id = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
                var len = BitConverter.ToInt32(wav, pos + 4);
                if (id == "fmt ") { channels = BitConverter.ToInt16(wav, pos + 10); rate = BitConverter.ToInt32(wav, pos + 12); bits = BitConverter.ToInt16(wav, pos + 22); }
                else if (id == "data") { dataAt = pos + 8; dataLen = Math.Min(len, wav.Length - dataAt); break; }
                pos += 8 + len + (len & 1);
            }
            if (dataAt < 0 || bits != 16 || channels is < 1 or > 2 || rate <= 0) return null;
            var frames = dataLen / (2 * channels);
            var mono = new float[frames];
            for (var i = 0; i < frames; i++)
            {
                float sum = 0;
                for (var c = 0; c < channels; c++) sum += BitConverter.ToInt16(wav, dataAt + (i * channels + c) * 2);
                mono[i] = sum / channels / 32768f;
            }
            return (mono, rate);
        }
        using var oggStream = asm.GetManifestResourceStream($"Sounds/{file}.ogg");
        if (oggStream is null) return null;
        using var reader = new NVorbis.VorbisReader(oggStream, false);
        var ch = reader.Channels;
        var all = new List<float>();
        var buffer = new float[4096 * ch];
        int read;
        while ((read = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
            for (var i = 0; i + ch <= read; i += ch)
            {
                float sum = 0;
                for (var c = 0; c < ch; c++) sum += buffer[i + c];
                all.Add(sum / ch);
            }
        return all.Count == 0 ? null : (all.ToArray(), reader.SampleRate);
    }

    /// <summary>Son de la banque libre : mono, adouci (filtre passe-bas), coupé à 380 ms avec une fin en fondu, au niveau discret, ralenti selon sa vitesse ; null s'il manque.</summary>
    private static byte[]? FromBank(SoundKind kind, double volume)
    {
        if (!Bank.TryGetValue(kind, out var entry)) return null;
        try
        {
            if (LoadMono(entry.File) is not var (mono, rate)) return null;
            // Durée entendue = longueur / (fréquence × vitesse) : on garde de quoi faire au plus 380 ms.
            var keep = Math.Min(mono.Length, (int)(MaxSeconds * rate * entry.Rate));
            if (keep <= 0) return null;
            // Passe-bas très doux : enlève l'éclat des aigus.
            float y = 0;
            // (Pas pour les bruits métalliques : leur éclat fait justement leur caractère.)
            if (kind < SoundKind.Metal1) for (var i = 0; i < keep; i++) { y += 0.32f * (mono[i] - y); mono[i] = y; }
            var fade = Math.Min(keep / 3, (int)(0.06 * rate * entry.Rate));
            for (var i = 0; i < fade; i++) mono[keep - 1 - i] *= (float)i / fade;
            var peak = Math.Max(1e-4f, mono.Take(keep).Max(Math.Abs));
            var data = new byte[keep * 2];
            for (var i = 0; i < keep; i++)
            {
                var v = (short)(Math.Clamp(mono[i] / peak, -1f, 1f) * volume * 0.35 * short.MaxValue);   // niveau discret, même à fond
                data[i * 2] = (byte)(v & 0xFF); data[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
            }
            return Wav(data, (int)(rate * entry.Rate));
        }
        catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException or IndexOutOfRangeException) { return null; }
    }

    private static byte[] Wav(byte[] data, int rate)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + data.Length); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(data.Length); w.Write(data);
        return ms.ToArray();
    }

    // ----- Sons fabriqués (bruit de page, et secours si un fichier manque) -----

    /// <summary>
    /// Notes (fréquence en Hz, début en ms, durée en ms, glissando, gain) : douces et rondes, attaque lente, petite montée de hauteur façon « bloup ».
    /// </summary>
    private static (double Hz, int Start, int Length, double Glide, double Gain)[] Notes(SoundKind kind) => kind switch
    {
        SoundKind.Click => new[] { (587.0, 0, 100, 0.06, 1.0) },
        SoundKind.Select => new[] { (784.0, 0, 80, 0.04, 0.8) },
        SoundKind.Tab => new[] { (523.0, 0, 100, 0.03, 0.9), (659.0, 70, 130, 0.03, 0.9) },
        SoundKind.Success => new[] { (523.0, 0, 160, 0.0, 0.9), (659.0, 90, 160, 0.0, 0.9), (784.0, 180, 200, 0.0, 0.9) },
        SoundKind.Pop => new[] { (880.0, 0, 120, 0.12, 1.0) },
        SoundKind.Title1 => new[] { (660.0, 0, 120, 0.08, 1.0), (990.0, 80, 170, 0.03, 0.9) },
        SoundKind.Title2 => new[] { (588.0, 0, 120, 0.08, 1.0), (880.0, 80, 170, 0.03, 0.9) },
        SoundKind.Title3 => new[] { (740.0, 0, 120, 0.08, 1.0), (1109.0, 80, 170, 0.03, 0.9) },
        SoundKind.Page => Array.Empty<(double, int, int, double, double)>(),
        _ => new[] { (294.0, 0, 170, -0.08, 1.0), (247.0, 110, 210, -0.08, 0.9) },
    };

    private const int PageMs = 380;

    /// <summary>Fichier WAV (16 bits, mono) du son : la banque libre si le fichier est là, sinon un son fabriqué.</summary>
    public static byte[] Build(SoundKind kind, double volume)
    {
        if (FromBank(kind, volume) is { } fromBank) return fromBank;
        var notes = Notes(kind);
        var total = (notes.Length == 0 ? PageMs : notes.Max(n => n.Start + n.Length)) * Rate / 1000;
        var samples = new double[total];
        foreach (var (hz, start, length, glide, gain) in notes)
        {
            var from = start * Rate / 1000;
            var count = length * Rate / 1000;
            double phase = 0;
            for (var i = 0; i < count && from + i < total; i++)
            {
                var t = (double)i / count;
                var attack = Math.Min(1, i / (Rate * 0.014));                  // 14 ms d'attaque : début tout en douceur
                attack *= attack;
                var f = hz * (1 + glide * Math.Exp(-t * 7));
                phase += 2 * Math.PI * f / Rate;
                var tone = Math.Sin(phase) + 0.22 * Math.Sin(2 * phase) * Math.Exp(-3 * t) + 0.06 * Math.Sin(3 * phase) * Math.Exp(-6 * t);
                samples[from + i] += tone * gain * Math.Exp(-4.5 * t) * attack * 0.85;
            }
        }
        if (kind == SoundKind.Page) AddPage(samples);
        var data = new byte[total * 2];
        for (var i = 0; i < total; i++)
        {
            var v = (short)(Math.Clamp(samples[i], -1, 1) * volume * 0.35 * short.MaxValue);   // 0,35 : sons discrets même à fond
            data[i * 2] = (byte)(v & 0xFF); data[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return Wav(data, Rate);
    }

    /// <summary>Page qui se tourne : souffle de bruit adouci (filtre passe-bas qui s'ouvre puis se referme) avec un « frottement » au milieu.</summary>
    private static void AddPage(double[] samples)
    {
        var rng = new Random(7);   // même bruit à chaque fois : le son est reproductible
        double low = 0, low2 = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var t = (double)i / samples.Length;
            var swell = Math.Sin(Math.PI * Math.Pow(t, 0.8));
            var open = 0.05 + 0.35 * Math.Sin(Math.PI * t);
            low += open * ((rng.NextDouble() * 2 - 1) - low);
            low2 += open * (low - low2);
            var flutter = 0.75 + 0.25 * Math.Sin(2 * Math.PI * 38 * t);
            samples[i] += low2 * swell * flutter * 2.2;
        }
    }
}
