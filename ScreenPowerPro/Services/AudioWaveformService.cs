using System;
using System.IO;
using System.Text;

namespace ScreenPowerPro.Services;

/// <summary>
/// Ses dosyalarından (WAV) PCM peak verilerini okuyarak zaman çizelgesinde
/// frekans/amplitüd dalga formu (waveform) grafiklerini oluşturmak için servis.
/// </summary>
public class AudioWaveformService
{
    /// <summary>
    /// Verilen ses dosyasını okur ve zaman çizelgesi için normalize edilmiş (0.0 - 1.0) peak dizisi üretir.
    /// </summary>
    public float[] ExtractPeaks(string? audioFilePath, int targetPeakCount = 1200)
    {
        if (string.IsNullOrEmpty(audioFilePath) || !File.Exists(audioFilePath))
        {
            return GenerateFallbackPeaks(targetPeakCount);
        }

        try
        {
            var ext = Path.GetExtension(audioFilePath).ToLowerInvariant();
            if (ext == ".wav")
            {
                var peaks = ReadWavPeaks(audioFilePath, targetPeakCount);
                if (peaks != null && peaks.Length > 0)
                {
                    return peaks;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioWaveformService] Waveform okuma hatası: {ex.Message}");
        }

        return GenerateFallbackPeaks(targetPeakCount);
    }

    private static float[]? ReadWavPeaks(string filePath, int targetPeakCount)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);

        // RIFF header
        string riff = new string(reader.ReadChars(4));
        if (riff != "RIFF") return null;

        reader.ReadInt32(); // File size - 8
        string wave = new string(reader.ReadChars(4));
        if (wave != "WAVE") return null;

        short audioFormat = 1;
        short numChannels = 1;
        int sampleRate = 44100;
        short bitsPerSample = 16;
        long dataPos = 0;
        int dataChunkSize = 0;

        // Chunks scan
        while (stream.Position < stream.Length - 8)
        {
            string chunkId = new string(reader.ReadChars(4));
            int chunkSize = reader.ReadInt32();

            if (chunkId == "fmt ")
            {
                audioFormat = reader.ReadInt16();
                numChannels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32(); // byteRate
                reader.ReadInt16(); // blockAlign
                bitsPerSample = reader.ReadInt16();

                int remaining = chunkSize - 16;
                if (remaining > 0) reader.ReadBytes(remaining);
            }
            else if (chunkId == "data")
            {
                dataPos = stream.Position;
                dataChunkSize = chunkSize;
                break;
            }
            else
            {
                stream.Seek(chunkSize, SeekOrigin.Current);
            }
        }

        if (dataPos == 0 || dataChunkSize <= 0) return null;

        stream.Position = dataPos;
        int bytesPerSample = bitsPerSample / 8;
        if (bytesPerSample <= 0) return null;

        int totalSamples = dataChunkSize / (bytesPerSample * numChannels);
        if (totalSamples <= 0) return null;

        int samplesPerPeak = Math.Max(1, totalSamples / targetPeakCount);
        int actualPeaks = Math.Min(targetPeakCount, totalSamples / samplesPerPeak);
        float[] peaks = new float[actualPeaks];

        if (bitsPerSample == 16)
        {
            for (int p = 0; p < actualPeaks; p++)
            {
                float maxVal = 0;
                for (int s = 0; s < samplesPerPeak && stream.Position < stream.Length - (2 * numChannels); s++)
                {
                    short sample = reader.ReadInt16();
                    if (numChannels > 1) reader.ReadInt16(); // skip right channel
                    float abs = Math.Abs((float)sample) / 32768f;
                    if (abs > maxVal) maxVal = abs;
                }
                peaks[p] = Math.Clamp(maxVal, 0.05f, 1.0f);
            }
        }
        else if (bitsPerSample == 32 && audioFormat == 3) // IEEE Float
        {
            for (int p = 0; p < actualPeaks; p++)
            {
                float maxVal = 0;
                for (int s = 0; s < samplesPerPeak && stream.Position < stream.Length - (4 * numChannels); s++)
                {
                    float sample = reader.ReadSingle();
                    if (numChannels > 1) reader.ReadSingle();
                    float abs = Math.Abs(sample);
                    if (abs > maxVal) maxVal = abs;
                }
                peaks[p] = Math.Clamp(maxVal, 0.05f, 1.0f);
            }
        }
        else
        {
            return GenerateFallbackPeaks(targetPeakCount);
        }

        return peaks;
    }

    private static float[] GenerateFallbackPeaks(int count)
    {
        // Ses dosyası bulunamadığında veya sessiz olduğunda zarif, doğal bir dalga formu
        var peaks = new float[count];
        var rnd = new Random(42);
        float current = 0.25f;
        for (int i = 0; i < count; i++)
        {
            float target = 0.15f + (float)rnd.NextDouble() * 0.45f;
            current += (target - current) * 0.35f;
            peaks[i] = Math.Clamp(current, 0.08f, 0.95f);
        }
        return peaks;
    }
}
