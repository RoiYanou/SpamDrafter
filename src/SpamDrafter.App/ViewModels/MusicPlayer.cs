using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NVorbis;

namespace SpamDrafter.App.ViewModels;

/// <summary>
/// Musique de fond : un fichier .ogg du paquet de données, lu en continu (décodé au fil de l'eau, pas chargé en entier) et rejoué
/// en boucle. Désactivée par défaut. Windows seulement pour l'instant (D-012) ; ailleurs, ou sans carte son, silence :
/// un échec de lecture ne gêne jamais l'application.
/// </summary>
public static class MusicPlayer
{
    private static readonly object Gate = new();
    private static WaveOutEvent? _out;
    private static VolumeSampleProvider? _volumeProvider;
    private static string? _playingPath;
    private static bool _enabled;
    private static double _volume = 0.4;
    private static string? _path;

    /// <summary>Volume de 0 à 1 (la courbe est quadratique : le bas du curseur reste doux).</summary>
    public static void SetVolume(double volume)
    {
        lock (Gate)
        {
            _volume = Math.Clamp(volume, 0, 1);
            if (_volumeProvider is not null) _volumeProvider.Volume = (float)(_volume * _volume);
        }
        Refresh();
    }

    public static void SetEnabled(bool enabled) { lock (Gate) _enabled = enabled; Refresh(); }

    /// <summary>Choisit la musique (chemin du fichier .ogg ; null : aucune).</summary>
    public static void SetTrack(string? path) { lock (Gate) _path = path; Refresh(); }

    /// <summary>Arrête la lecture (fermeture de l'application).</summary>
    public static void Shutdown() { lock (Gate) { _enabled = false; Stop(); } }

    private static void Refresh()
    {
        lock (Gate)
        {
            if (!OperatingSystem.IsWindows() || !_enabled || _volume <= 0 || _path is null || !File.Exists(_path)) { Stop(); return; }
            if (_out is not null && _playingPath == _path) return;   // déjà en lecture
            Stop();
            try
            {
                var source = new LoopingVorbis(_path);
                _volumeProvider = new VolumeSampleProvider(source) { Volume = (float)(_volume * _volume) };
                _out = new WaveOutEvent { DesiredLatency = 200 };
                _out.Init(_volumeProvider);
                _out.Play();
                _playingPath = _path;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or NAudio.MmException or InvalidOperationException or DllNotFoundException)
            {
                Stop();   // pas de carte son, fichier illisible : silence
            }
        }
    }

    private static void Stop()
    {
        var o = _out;
        _out = null; _volumeProvider = null; _playingPath = null;
        try { o?.Stop(); o?.Dispose(); } catch (Exception) { /* rien à faire de plus */ }
    }

    /// <summary>Lecture d'un fichier Ogg Vorbis en boucle, en flottants stéréo.</summary>
    private sealed class LoopingVorbis : ISampleProvider, IDisposable
    {
        private readonly VorbisReader _reader;
        public WaveFormat WaveFormat { get; }

        public LoopingVorbis(string path)
        {
            _reader = new VorbisReader(path);
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_reader.SampleRate, _reader.Channels);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var total = 0;
            var emptyReads = 0;
            while (total < count)
            {
                var n = _reader.ReadSamples(buffer, offset + total, count - total);
                if (n <= 0)
                {
                    if (++emptyReads > 2) break;   // fichier vide : on ne boucle pas à vide
                    _reader.SeekTo(0);
                    continue;
                }
                emptyReads = 0;
                total += n;
            }
            if (total == 0) Array.Clear(buffer, offset, count);
            return total == 0 ? count : total;
        }

        public void Dispose() => _reader.Dispose();
    }
}
