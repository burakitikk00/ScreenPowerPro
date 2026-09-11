using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ScreenPowerPro.Models;

namespace ScreenPowerPro.Services;

/// <summary>
/// Projedeki fare tıklama seslerini (public/mouse_click) düşük gecikme (zero-latency)
/// ve polifonik (aynı anda birden fazla tık) destekle çalan ses servisidir.
/// </summary>
public class ClickSoundService : IDisposable
{
    private static ClickSoundService? _instance;
    public static ClickSoundService Instance => _instance ??= new ClickSoundService();

    private readonly List<ClickSoundItem> _availableSounds = new();
    public IReadOnlyList<ClickSoundItem> AvailableSounds => _availableSounds;

    private readonly Dictionary<string, CachedSound> _cachedSounds = new(StringComparer.OrdinalIgnoreCase);
    private WaveOut? _waveOut;
    private MixingSampleProvider? _mixer;
    private static readonly WaveFormat MixerFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

    private bool _isInitialized = false;
    private readonly object _lock = new();

    public ClickSoundService()
    {
        InitializeSoundCatalog();
        _ = Task.Run(InitializeAudioEngine);
    }

    private void InitializeSoundCatalog()
    {
        string clickDir = ResolveClickAudioFolder();

        _availableSounds.Clear();
        _availableSounds.AddRange(new[]
        {
            new ClickSoundItem
            {
                FileName = "universfield-computer-mouse-click-02-383961.mp3",
                DisplayName = "Universfield - Bilgisayar Tık 02 (Önerilen)",
                ShortName = "Bilgisayar Tık 02",
                Description = "Modern, temiz ve tok dijital tıklama sesi",
                FullPath = Path.Combine(clickDir, "universfield-computer-mouse-click-02-383961.mp3")
            },
            new ClickSoundItem
            {
                FileName = "freesound_community-mouse-click-104737.mp3",
                DisplayName = "Freesound - Standart Tık",
                ShortName = "Standart Tık",
                Description = "Dengeli, doğal klasik fare tıklaması",
                FullPath = Path.Combine(clickDir, "freesound_community-mouse-click-104737.mp3")
            },
            new ClickSoundItem
            {
                FileName = "matthewvakaliuk73627-mouse-click-290204.mp3",
                DisplayName = "Matthew - Yumuşak / Soft Tık",
                ShortName = "Yumuşak Tık",
                Description = "Sessiz ofis ortamları için hafif ve yumuşak tık",
                FullPath = Path.Combine(clickDir, "matthewvakaliuk73627-mouse-click-290204.mp3")
            },
            new ClickSoundItem
            {
                FileName = "universfield-computer-mouse-click-352734.mp3",
                DisplayName = "Universfield - Mekanik Tık",
                ShortName = "Mekanik Tık",
                Description = "Belirgin mekanik switch / tactile hissi veren tık",
                FullPath = Path.Combine(clickDir, "universfield-computer-mouse-click-352734.mp3")
            },
            new ClickSoundItem
            {
                FileName = "universfield-mouse-click-351398.mp3",
                DisplayName = "Universfield - Modern Fare Tık",
                ShortName = "Modern Fare Tık",
                Description = "Ergonomik modern optik fare tıklaması",
                FullPath = Path.Combine(clickDir, "universfield-mouse-click-351398.mp3")
            },
            new ClickSoundItem
            {
                FileName = "dragon-studio-mouse-click-4-393911.mp3",
                DisplayName = "Dragon Studio - Tıklama 4",
                ShortName = "Tıklama 4",
                Description = "Net, stüdyo kaydı parlak fare sesi",
                FullPath = Path.Combine(clickDir, "dragon-studio-mouse-click-4-393911.mp3")
            }
        });
    }

    private static string ResolveClickAudioFolder()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "public", "mouse_click"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "public", "mouse_click"),
            @"C:\Users\burak\ScreenPowerPro\ScreenPowerPro\public\mouse_click",
            Path.Combine(Directory.GetCurrentDirectory(), "public", "mouse_click")
        ];

        foreach (var dir in candidates)
        {
            try
            {
                if (Directory.Exists(dir) && Directory.GetFiles(dir, "*.mp3").Length > 0)
                {
                    return Path.GetFullPath(dir);
                }
            }
            catch { }
        }

        return @"C:\Users\burak\ScreenPowerPro\ScreenPowerPro\public\mouse_click";
    }

    private void InitializeAudioEngine()
    {
        lock (_lock)
        {
            if (_isInitialized) return;

            try
            {
                // NAudio Mixer motorunu başlat
                _mixer = new MixingSampleProvider(MixerFormat) { ReadFully = true };
                _waveOut = new WaveOut();
                _waveOut.Init(_mixer);
                _waveOut.Play();

                // Ses dosyalarını belleğe ön yükle (zero-latency)
                foreach (var item in _availableSounds)
                {
                    if (File.Exists(item.FullPath))
                    {
                        try
                        {
                            var cached = new CachedSound(item.FullPath, MixerFormat);
                            _cachedSounds[item.FileName] = cached;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[ClickSoundService] Ön yükleme hatası ({item.FileName}): {ex.Message}");
                        }
                    }
                }

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ClickSoundService] Ses motoru başlatma hatası: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// İsmi verilen fare tıklama sesini belirtilen ses seviyesinde (%0 - %100) çalar.
    /// </summary>
    public void PlaySound(string? fileName, double volume0to100 = 80)
    {
        if (volume0to100 <= 0) return;

        string targetName = string.IsNullOrWhiteSpace(fileName)
            ? "universfield-computer-mouse-click-02-383961.mp3"
            : Path.GetFileName(fileName);

        float vol = Math.Clamp((float)(volume0to100 / 100.0), 0f, 1f);

        lock (_lock)
        {
            if (_cachedSounds.TryGetValue(targetName, out var cached) && _mixer != null)
            {
                _mixer.AddMixerInput(new CachedSoundSampleProvider(cached, vol));
                return;
            }
        }

        // Eğer önbellek henüz hazır değilse veya dosya yüklenmediyse fallback oynatma
        PlayFallback(targetName, vol);
    }

    private void PlayFallback(string fileName, float volume)
    {
        try
        {
            var item = _availableSounds.FirstOrDefault(s => string.Equals(s.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                       ?? _availableSounds.FirstOrDefault();

            if (item != null && File.Exists(item.FullPath))
            {
                Task.Run(() =>
                {
                    try
                    {
                        using var reader = new AudioFileReader(item.FullPath) { Volume = volume };
                        using var output = new WaveOut();
                        output.Init(reader);
                        output.Play();
                        while (output.PlaybackState == PlaybackState.Playing)
                        {
                            System.Threading.Thread.Sleep(20);
                        }
                    }
                    catch { }
                });
            }
        }
        catch { }
    }

    public ClickSoundItem? GetSoundByFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return _availableSounds.FirstOrDefault();

        string nameOnly = Path.GetFileName(fileName);
        return _availableSounds.FirstOrDefault(s => string.Equals(s.FileName, nameOnly, StringComparison.OrdinalIgnoreCase))
               ?? _availableSounds.FirstOrDefault();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            try
            {
                _waveOut?.Stop();
                _waveOut?.Dispose();
                _waveOut = null;
                _mixer = null;
                _cachedSounds.Clear();
            }
            catch { }
        }
    }

    private sealed class CachedSound
    {
        public float[] AudioData { get; }
        public WaveFormat WaveFormat { get; }

        public CachedSound(string audioFileName, WaveFormat targetFormat)
        {
            using var audioFileReader = new AudioFileReader(audioFileName);
            
            ISampleProvider provider = audioFileReader;
            if (audioFileReader.WaveFormat.SampleRate != targetFormat.SampleRate ||
                audioFileReader.WaveFormat.Channels != targetFormat.Channels)
            {
                var resampler = new WdlResamplingSampleProvider(audioFileReader, targetFormat.SampleRate);
                provider = (resampler.WaveFormat.Channels == 1 && targetFormat.Channels == 2)
                    ? new MonoToStereoSampleProvider(resampler)
                    : resampler;
            }

            var wholeFile = new List<float>();
            var readBuffer = new float[targetFormat.SampleRate * targetFormat.Channels];
            int samplesRead;
            while ((samplesRead = provider.Read(readBuffer.AsSpan())) > 0)
            {
                wholeFile.AddRange(readBuffer.Take(samplesRead));
            }

            AudioData = wholeFile.ToArray();
            WaveFormat = targetFormat;
        }
    }

    private sealed class CachedSoundSampleProvider : ISampleProvider
    {
        private readonly CachedSound _cachedSound;
        private long _position;
        private readonly float _volume;

        public CachedSoundSampleProvider(CachedSound cachedSound, float volume)
        {
            _cachedSound = cachedSound;
            _volume = Math.Clamp(volume, 0f, 1f);
        }

        public int Read(Span<float> buffer)
        {
            var availableSamples = _cachedSound.AudioData.Length - _position;
            var samplesToCopy = (int)Math.Min(availableSamples, buffer.Length);
            for (int i = 0; i < samplesToCopy; i++)
            {
                buffer[i] = _cachedSound.AudioData[_position + i] * _volume;
            }
            _position += samplesToCopy;
            return samplesToCopy;
        }

        public WaveFormat WaveFormat => _cachedSound.WaveFormat;
    }
}
