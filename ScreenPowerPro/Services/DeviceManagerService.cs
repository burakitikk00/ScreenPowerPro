using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using Windows.Devices.Enumeration;

namespace ScreenPowerPro.Services;

public class DeviceItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
    public bool IsNone { get; set; }
}

public class AudioAppItem
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
}

public class DeviceManagerService
{
    private readonly SettingsService _settingsService;

    public ObservableCollection<DeviceItem> Cameras { get; } = new();
    public ObservableCollection<DeviceItem> Microphones { get; } = new();
    public ObservableCollection<DeviceItem> Speakers { get; } = new();
    public ObservableCollection<AudioAppItem> OpenAudioApps { get; } = new();

    public DeviceItem? SelectedCamera { get; private set; }
    public DeviceItem? SelectedMicrophone { get; private set; }
    public DeviceItem? SelectedSpeaker { get; private set; }
    public bool IsOnlyAppAudioSelected { get; set; }

    public event Action? DevicesUpdated;
    public event Action? AudioAppsUpdated;

    public DeviceManagerService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task RefreshAllDevicesAsync()
    {
        await RefreshCamerasAsync();
        RefreshAudioDevices();
        RefreshOpenAudioApps();
        DevicesUpdated?.Invoke();
    }

    public async Task RefreshCamerasAsync()
    {
        Cameras.Clear();
        try
        {
            var videoDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            foreach (var dev in videoDevices)
            {
                Cameras.Add(new DeviceItem
                {
                    Id = dev.Id,
                    Name = dev.Name,
                    IsSelected = false
                });
            }
        }
        catch
        {
            // DirectShow / WinRT fallback
        }

        // Add None option
        Cameras.Add(new DeviceItem
        {
            Id = "none",
            Name = "None",
            IsNone = true,
            IsSelected = false
        });

        // Set active selection
        string? savedCam = _settingsService.Current.SelectedCameraDevice;
        bool camEnabled = _settingsService.Current.CameraEnabled;

        if (!camEnabled)
        {
            SelectCamera("none");
        }
        else
        {
            var match = Cameras.FirstOrDefault(c => !c.IsNone && (c.Id == savedCam || c.Name == savedCam));
            if (match != null)
            {
                SelectCamera(match.Id);
            }
            else
            {
                var first = Cameras.FirstOrDefault(c => !c.IsNone);
                SelectCamera(first?.Id ?? "none");
            }
        }
    }

    public void RefreshAudioDevices()
    {
        Microphones.Clear();
        Speakers.Clear();

        try
        {
            using var enumerator = new MMDeviceEnumerator();

            // 1. Capture devices (Microphones)
            var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var dev in captureDevices)
            {
                Microphones.Add(new DeviceItem
                {
                    Id = dev.ID,
                    Name = dev.FriendlyName,
                    IsSelected = false
                });
            }

            // 2. Render devices (Speakers)
            var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var dev in renderDevices)
            {
                Speakers.Add(new DeviceItem
                {
                    Id = dev.ID,
                    Name = dev.FriendlyName,
                    IsSelected = false
                });
            }
        }
        catch
        {
        }

        // Add None option for Microphones
        Microphones.Add(new DeviceItem
        {
            Id = "none",
            Name = "None",
            IsNone = true,
            IsSelected = false
        });

        // Add None option for Speakers
        Speakers.Add(new DeviceItem
        {
            Id = "none",
            Name = "None",
            IsNone = true,
            IsSelected = false
        });

        // Set active selection for Mic
        string? savedMic = _settingsService.Current.SelectedMicDevice;
        bool micEnabled = _settingsService.Current.MicAudioEnabled;

        if (!micEnabled)
        {
            SelectMicrophone("none");
        }
        else
        {
            var match = Microphones.FirstOrDefault(m => !m.IsNone && (m.Id == savedMic || m.Name == savedMic));
            if (match != null)
            {
                SelectMicrophone(match.Id);
            }
            else
            {
                var first = Microphones.FirstOrDefault(m => !m.IsNone);
                SelectMicrophone(first?.Id ?? "none");
            }
        }

        // Set active selection for Speaker
        string? savedSpeaker = _settingsService.Current.SelectedSpeakerDevice;
        bool sysAudioEnabled = _settingsService.Current.SystemAudioEnabled;

        if (!sysAudioEnabled)
        {
            SelectSpeaker("none");
        }
        else
        {
            var match = Speakers.FirstOrDefault(s => !s.IsNone && (s.Id == savedSpeaker || s.Name == savedSpeaker));
            if (match != null)
            {
                SelectSpeaker(match.Id);
            }
            else
            {
                var first = Speakers.FirstOrDefault(s => !s.IsNone);
                SelectSpeaker(first?.Id ?? "none");
            }
        }
    }

    public void RefreshOpenAudioApps()
    {
        OpenAudioApps.Clear();
        var foundProcesses = new Dictionary<int, string>();

        try
        {
            // 1. Enumerate WASAPI Audio Sessions
            using var enumerator = new MMDeviceEnumerator();
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (defaultDevice?.AudioSessionManager != null)
            {
                var sessions = defaultDevice.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    uint pid = session.GetProcessID;
                    if (pid > 0 && !foundProcesses.ContainsKey((int)pid))
                    {
                        try
                        {
                            var proc = Process.GetProcessById((int)pid);
                            string name = !string.IsNullOrWhiteSpace(proc.MainWindowTitle)
                                ? proc.MainWindowTitle
                                : proc.ProcessName;
                            foundProcesses[(int)pid] = name;
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }

        // 2. Enumerate other running desktop windows (e.g. Chrome, Spotify, Edge, ScreenPowerPro, Media players)
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.MainWindowHandle != IntPtr.Zero &&
                        !string.IsNullOrWhiteSpace(proc.MainWindowTitle) &&
                        !foundProcesses.ContainsKey(proc.Id))
                    {
                        // Filter out system windows
                        string title = proc.MainWindowTitle.Trim();
                        string pName = proc.ProcessName.ToLowerInvariant();
                        if (pName != "shellexperiencehost" &&
                            pName != "applicationframehost" &&
                            pName != "systemsettings" &&
                            pName != "textinputhost")
                        {
                            foundProcesses[proc.Id] = title;
                        }
                    }
                }
                catch { }
            }
        }
        catch { }

        var savedSelectedProcesses = _settingsService.Current.SelectedAppAudioProcesses ?? new List<string>();

        foreach (var kvp in foundProcesses)
        {
            string pName = "";
            try { pName = Process.GetProcessById(kvp.Key).ProcessName; } catch { }

            bool isSelected = savedSelectedProcesses.Contains(kvp.Key.ToString()) ||
                              savedSelectedProcesses.Contains(pName);

            OpenAudioApps.Add(new AudioAppItem
            {
                ProcessId = kvp.Key,
                ProcessName = pName,
                DisplayName = kvp.Value.Length > 28 ? kvp.Value.Substring(0, 26) + "..." : kvp.Value,
                IsSelected = isSelected
            });
        }

        AudioAppsUpdated?.Invoke();
    }

    public void SelectCamera(string id)
    {
        foreach (var c in Cameras)
        {
            c.IsSelected = (c.Id == id);
        }
        SelectedCamera = Cameras.FirstOrDefault(c => c.Id == id);

        if (SelectedCamera != null)
        {
            if (SelectedCamera.IsNone)
            {
                _settingsService.Current.CameraEnabled = false;
                _settingsService.Current.SelectedCameraDevice = null;
            }
            else
            {
                _settingsService.Current.CameraEnabled = true;
                _settingsService.Current.SelectedCameraDevice = SelectedCamera.Id;
            }
            _settingsService.Save();
        }
        DevicesUpdated?.Invoke();
    }

    public void SelectMicrophone(string id)
    {
        foreach (var m in Microphones)
        {
            m.IsSelected = (m.Id == id);
        }
        SelectedMicrophone = Microphones.FirstOrDefault(m => m.Id == id);

        if (SelectedMicrophone != null)
        {
            if (SelectedMicrophone.IsNone)
            {
                _settingsService.Current.MicAudioEnabled = false;
                _settingsService.Current.SelectedMicDevice = null;
            }
            else
            {
                _settingsService.Current.MicAudioEnabled = true;
                _settingsService.Current.SelectedMicDevice = SelectedMicrophone.Id;
            }
            _settingsService.Save();
        }
        DevicesUpdated?.Invoke();
    }

    public void SelectSpeaker(string id)
    {
        IsOnlyAppAudioSelected = false;
        foreach (var s in Speakers)
        {
            s.IsSelected = (s.Id == id);
        }
        SelectedSpeaker = Speakers.FirstOrDefault(s => s.Id == id);

        if (SelectedSpeaker != null)
        {
            if (SelectedSpeaker.IsNone)
            {
                _settingsService.Current.SystemAudioEnabled = false;
                _settingsService.Current.OnlyAppAudioEnabled = false;
                _settingsService.Current.SelectedSpeakerDevice = null;
            }
            else
            {
                _settingsService.Current.SystemAudioEnabled = true;
                _settingsService.Current.OnlyAppAudioEnabled = false;
                _settingsService.Current.SelectedSpeakerDevice = SelectedSpeaker.Id;
            }
            _settingsService.Save();
        }
        DevicesUpdated?.Invoke();
    }

    public void ToggleAppAudioSelection(AudioAppItem app, bool isSelected)
    {
        app.IsSelected = isSelected;
        IsOnlyAppAudioSelected = true;

        // Uncheck regular speakers
        foreach (var s in Speakers)
        {
            s.IsSelected = false;
        }

        _settingsService.Current.SystemAudioEnabled = true;
        _settingsService.Current.OnlyAppAudioEnabled = true;

        var selectedPids = OpenAudioApps.Where(a => a.IsSelected).Select(a => a.ProcessId.ToString()).ToList();
        _settingsService.Current.SelectedAppAudioProcesses = selectedPids;
        _settingsService.Save();

        DevicesUpdated?.Invoke();
        AudioAppsUpdated?.Invoke();
    }

    public int GetSelectedAppCount()
    {
        return OpenAudioApps.Count(a => a.IsSelected);
    }

    public List<int> GetSelectedProcessIds()
    {
        return OpenAudioApps.Where(a => a.IsSelected).Select(a => a.ProcessId).ToList();
    }
}
