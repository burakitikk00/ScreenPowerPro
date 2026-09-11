using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenPowerPro.Models;
using ScreenPowerPro.Services;
using Windows.Storage.Pickers;

namespace ScreenPowerPro.ViewModels;

/// <summary>
/// Uygulama ayarları sayfasının ViewModel sınıfı.
/// Genel tercihler, kayıt parametreleri, dışa aktarım seçenekleri ve
/// donanım aygıtlarını (Mikrofon, Kamera, Hoparlör) yönetir.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;

    // --- Genel Ayarlar ---
    [ObservableProperty]
    private string _language = "tr";

    [ObservableProperty]
    private string _projectSaveLocation = string.Empty;

    [ObservableProperty]
    private string _exportLocation = string.Empty;

    // --- Kayıt Ayarları ---
    [ObservableProperty]
    private string _autoZoomMode = "smooth"; // none, smooth, instant

    [ObservableProperty]
    private bool _autoZoom;

    // --- Dinamik Zoom Parametreleri ---
    [ObservableProperty]
    private double _zoomSpeed = 0.35;

    [ObservableProperty]
    private double _maxZoomRatio = 1.5;

    [ObservableProperty]
    private double _zoomDuration = 2.5;

    [ObservableProperty]
    private bool _cancelOnOutOfBounds = true;

    [ObservableProperty]
    private bool _hideDesktopIcons;

    [ObservableProperty]
    private bool _hideTaskbar;

    [ObservableProperty]
    private bool _hideMouseCursor;

    [ObservableProperty]
    private bool _excludeAppFromRecording;

    [ObservableProperty]
    private string _resolution = "1080p";

    [ObservableProperty]
    private int _countdownSeconds = 3;

    // --- Dışa Aktarım Ayarları ---
    [ObservableProperty]
    private int _fps = 60;

    [ObservableProperty]
    private string _exportFormat = "mp4";

    [ObservableProperty]
    private string _exportResolution = "1080p";

    // --- Cihaz ve Donanım Tercihleri ---
    [ObservableProperty]
    private bool _micAudioEnabled = true;

    [ObservableProperty]
    private bool _systemAudioEnabled = true;

    [ObservableProperty]
    private bool _cameraEnabled = false;

    [ObservableProperty]
    private string? _selectedMicDevice;

    [ObservableProperty]
    private string? _selectedSpeakerDevice;

    [ObservableProperty]
    private string? _selectedCameraDevice;

    [ObservableProperty]
    private ObservableCollection<string> _availableMics = new();

    [ObservableProperty]
    private ObservableCollection<string> _availableSpeakers = new();

    [ObservableProperty]
    private ObservableCollection<string> _availableCameras = new();

    [ObservableProperty]
    private string _saveStatusMessage = string.Empty;

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        Load();
        DetectHardwareDevices();
    }

    /// <summary>
    /// Kayıtlı ayarları okur ve ViewModel özelliklerini günceller.
    /// </summary>
    public void Load()
    {
        var s = _settingsService.Current;
        ProjectSaveLocation = s.ProjectSaveLocation;
        ExportLocation = s.ExportLocation;
        Language = s.Language ?? "tr";
        AutoZoomMode = s.AutoZoomMode ?? "smooth";
        AutoZoom = s.AutoZoom;
        ZoomSpeed = s.ZoomSpeed;
        MaxZoomRatio = s.MaxZoomRatio;
        ZoomDuration = s.ZoomDuration;
        CancelOnOutOfBounds = s.CancelOnOutOfBounds;
        HideDesktopIcons = s.HideDesktopIcons;
        HideTaskbar = s.HideTaskbar;
        HideMouseCursor = s.HideMouseCursor;
        ExcludeAppFromRecording = s.ExcludeAppFromRecording;
        Resolution = s.Resolution;
        CountdownSeconds = s.CountdownSeconds;
        Fps = s.Fps;
        ExportFormat = s.ExportFormat;
        ExportResolution = s.ExportResolution ?? "1080p";
        MicAudioEnabled = s.MicAudioEnabled;
        SystemAudioEnabled = s.SystemAudioEnabled;
        CameraEnabled = s.CameraEnabled;
        SelectedMicDevice = s.SelectedMicDevice ?? "Varsayılan Mikrofon";
        SelectedSpeakerDevice = s.SelectedSpeakerDevice ?? "Varsayılan Hoparlör";
        SelectedCameraDevice = s.SelectedCameraDevice ?? "Varsayılan Kamera";
    }

    /// <summary>
    /// Sistemde takılı ses ve kamera aygıtlarını tespit eder.
    /// </summary>
    public void DetectHardwareDevices()
    {
        AvailableMics.Clear();
        AvailableMics.Add("Varsayılan Mikrofon (Realtek High Definition)");
        AvailableMics.Add("Dahili Mikrofon Dizisi");

        AvailableSpeakers.Clear();
        AvailableSpeakers.Add("Varsayılan Hoparlör (Realtek)");
        AvailableSpeakers.Add("Kulaklık / Dijital Ses Çıkışı");

        AvailableCameras.Clear();
        AvailableCameras.Add("Entegre Web Kamerası");
        AvailableCameras.Add("Harici USB Kamera");
    }

    /// <summary>
    /// Kullanıcının yaptığı tüm ayar değişikliklerini diske kaydeder.
    /// </summary>
    [RelayCommand]
    public void Save()
    {
        var s = _settingsService.Current;
        s.Language = Language;
        s.ProjectSaveLocation = ProjectSaveLocation;
        s.ExportLocation = ExportLocation;
        s.AutoZoomMode = AutoZoomMode;
        s.AutoZoom = !string.Equals(AutoZoomMode, "none", StringComparison.OrdinalIgnoreCase);
        s.ZoomSpeed = ZoomSpeed;
        s.MaxZoomRatio = MaxZoomRatio;
        s.ZoomDuration = ZoomDuration;
        s.CancelOnOutOfBounds = CancelOnOutOfBounds;
        s.HideDesktopIcons = HideDesktopIcons;
        s.HideTaskbar = HideTaskbar;
        s.HideMouseCursor = HideMouseCursor;
        s.ExcludeAppFromRecording = ExcludeAppFromRecording;
        s.Resolution = Resolution;
        s.CountdownSeconds = CountdownSeconds;
        s.Fps = Fps;
        s.ExportFormat = ExportFormat;
        s.ExportResolution = ExportResolution;
        s.MicAudioEnabled = MicAudioEnabled;
        s.SystemAudioEnabled = SystemAudioEnabled;
        s.CameraEnabled = CameraEnabled;
        s.SelectedMicDevice = SelectedMicDevice;
        s.SelectedSpeakerDevice = SelectedSpeakerDevice;
        s.SelectedCameraDevice = SelectedCameraDevice;

        _settingsService.Save();

        // Anlık dinamik motor senkronizasyonu
        SettingsManager.Instance.ZoomSpeed = ZoomSpeed;
        SettingsManager.Instance.MaxZoomRatio = MaxZoomRatio;
        SettingsManager.Instance.ZoomDuration = ZoomDuration;
        SettingsManager.Instance.CancelOnOutOfBounds = CancelOnOutOfBounds;

        SaveStatusMessage = "Ayarlar başarıyla kaydedildi.";
    }

    /// <summary>
    /// Proje kayıt klasörü için Windows Klasör Seçim Diyaloğunu açar.
    /// </summary>
    [RelayCommand]
    public async Task PickProjectSaveLocationAsync()
    {
        try
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            if (MainWindow.CurrentInstance != null)
            {
                var hwnd = MainWindow.CurrentInstance.GetWindowHandle();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                ProjectSaveLocation = folder.Path;
                Save();
            }
        }
        catch { }
    }

    /// <summary>
    /// Dışa aktarım klasörü için Windows Klasör Seçim Diyaloğunu açar.
    /// </summary>
    [RelayCommand]
    public async Task PickExportLocationAsync()
    {
        try
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
            picker.FileTypeFilter.Add("*");

            if (MainWindow.CurrentInstance != null)
            {
                var hwnd = MainWindow.CurrentInstance.GetWindowHandle();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                ExportLocation = folder.Path;
                Save();
            }
        }
        catch { }
    }
}
