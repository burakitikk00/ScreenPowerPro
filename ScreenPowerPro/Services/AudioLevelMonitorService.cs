using System;
using System.Diagnostics;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ScreenPowerPro.Services;

/// <summary>
/// Mikrofon ve Hoparlör ses seviyelerini gerçek zamanlı olarak izleyen ve
/// arayüz için balistik (hızlı tepki, yumuşak sönümleme) ile normalize edilmiş
/// seviyeler (0.0 - 1.0) üreten servis.
/// </summary>
public class AudioLevelMonitorService : IDisposable
{
    private readonly DeviceManagerService _deviceManager;
    private readonly System.Timers.Timer _meterTimer;

    private WasapiRecorder? _micRecorder;
    private MMDevice? _micDevice;
    private MMDevice? _speakerDevice;

    private float _rawMicPeak = 0f;
    private float _smoothMicLevel = 0f;
    private float _smoothSpeakerLevel = 0f;

    private bool _isMonitoring = false;
    private readonly object _lock = new();

    /// <summary>
    /// Mikrofon ve Hoparlör seviyeleri güncellendiğinde tetiklenir: (micLevel [0..1], speakerLevel [0..1]).
    /// </summary>
    public event Action<float, float>? AudioLevelsChanged;

    public float CurrentMicLevel => _smoothMicLevel;
    public float CurrentSpeakerLevel => _smoothSpeakerLevel;

    public AudioLevelMonitorService(DeviceManagerService deviceManager)
    {
        _deviceManager = deviceManager;

        // ~33 FPS güncelleme hızı (30ms aralık)
        _meterTimer = new System.Timers.Timer(30);
        _meterTimer.AutoReset = true;
        _meterTimer.Elapsed += OnMeterTimerElapsed;

        _deviceManager.DevicesUpdated += OnDevicesUpdated;
    }

    public void StartMonitoring()
    {
        lock (_lock)
        {
            if (_isMonitoring) return;
            _isMonitoring = true;

            BindDevices();
            _meterTimer.Start();
        }
    }

    public void StopMonitoring()
    {
        lock (_lock)
        {
            if (!_isMonitoring) return;
            _isMonitoring = false;

            _meterTimer.Stop();
            ReleaseMicCapture();
            ReleaseSpeaker();

            _smoothMicLevel = 0f;
            _smoothSpeakerLevel = 0f;
            AudioLevelsChanged?.Invoke(0f, 0f);
        }
    }

    private void OnDevicesUpdated()
    {
        if (!_isMonitoring) return;
        lock (_lock)
        {
            BindDevices();
        }
    }

    private void BindDevices()
    {
        BindMicrophone();
        BindSpeaker();
    }

    private void BindMicrophone()
    {
        ReleaseMicCapture();

        var selectedMic = _deviceManager.SelectedMicrophone;
        if (selectedMic == null || selectedMic.IsNone)
        {
            _smoothMicLevel = 0f;
            return;
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            MMDevice? dev = null;

            if (!string.IsNullOrEmpty(selectedMic.Id))
            {
                try { dev = enumerator.GetDevice(selectedMic.Id); } catch { }
            }

            dev ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);

            if (dev != null)
            {
                _micDevice = dev;
                var micBuilder = new WasapiRecorderBuilder();
                micBuilder.WithDevice(dev);
                _micRecorder = micBuilder.Build();

                _micRecorder.DataAvailable += (buffer, flags, device, qpc) =>
                {
                    float max = 0f;
                    try
                    {
                        var span = buffer;
                        if (_micRecorder.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                        {
                            for (int i = 0; i <= span.Length - 4; i += 4)
                            {
                                float sample = Math.Abs(System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(span.Slice(i, 4)));
                                if (sample > max) max = sample;
                            }
                        }
                        else if (_micRecorder.WaveFormat.BitsPerSample == 16)
                        {
                            for (int i = 0; i <= span.Length - 2; i += 2)
                            {
                                short sample = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(span.Slice(i, 2));
                                float sample32 = Math.Abs(sample / 32768f);
                                if (sample32 > max) max = sample32;
                            }
                        }
                    }
                    catch { }

                    _rawMicPeak = Math.Max(_rawMicPeak, max);
                };

                _micRecorder.StartRecording();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioLevelMonitor] Mic capture init error: {ex.Message}");
        }
    }

    private void BindSpeaker()
    {
        ReleaseSpeaker();

        var selectedSpeaker = _deviceManager.SelectedSpeaker;
        if (selectedSpeaker == null || selectedSpeaker.IsNone)
        {
            _smoothSpeakerLevel = 0f;
            return;
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            MMDevice? dev = null;

            if (!string.IsNullOrEmpty(selectedSpeaker.Id))
            {
                try { dev = enumerator.GetDevice(selectedSpeaker.Id); } catch { }
            }

            dev ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _speakerDevice = dev;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioLevelMonitor] Speaker bind error: {ex.Message}");
        }
    }

    private void OnMeterTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (!_isMonitoring) return;

        // 1. Mikrofon Seviyesi
        float micInput = _rawMicPeak;
        _rawMicPeak = 0f; // reset peak for next interval

        if (_micDevice != null)
        {
            try
            {
                float hwPeak = _micDevice.AudioMeterInformation.MasterPeakValue;
                if (hwPeak > micInput) micInput = hwPeak;
            }
            catch { }
        }

        // Algısal skala: Math.Sqrt normal konuşmayı belirgin kılar
        float targetMic = Math.Clamp((float)Math.Sqrt(micInput), 0f, 1f);
        if (targetMic > _smoothMicLevel)
        {
            // Instant attack
            _smoothMicLevel = targetMic;
        }
        else
        {
            // Smooth decay
            _smoothMicLevel = Math.Max(0f, _smoothMicLevel * 0.82f - 0.015f);
        }

        // 2. Hoparlör Seviyesi
        float speakerInput = 0f;
        if (_speakerDevice != null)
        {
            try
            {
                speakerInput = _speakerDevice.AudioMeterInformation.MasterPeakValue;
            }
            catch { }
        }

        float targetSpeaker = Math.Clamp((float)Math.Sqrt(speakerInput), 0f, 1f);
        if (targetSpeaker > _smoothSpeakerLevel)
        {
            _smoothSpeakerLevel = targetSpeaker;
        }
        else
        {
            _smoothSpeakerLevel = Math.Max(0f, _smoothSpeakerLevel * 0.82f - 0.015f);
        }

        AudioLevelsChanged?.Invoke(_smoothMicLevel, _smoothSpeakerLevel);
    }

    private void ReleaseMicCapture()
    {
        try
        {
            if (_micRecorder != null)
            {
                _micRecorder.StopRecording();
                _micRecorder.Dispose();
                _micRecorder = null;
            }
            _micDevice?.Dispose();
            _micDevice = null;
        }
        catch { }
        _rawMicPeak = 0f;
    }

    private void ReleaseSpeaker()
    {
        try
        {
            _speakerDevice?.Dispose();
            _speakerDevice = null;
        }
        catch { }
    }

    public void Dispose()
    {
        StopMonitoring();
        _meterTimer.Dispose();
        _deviceManager.DevicesUpdated -= OnDevicesUpdated;
    }
}
