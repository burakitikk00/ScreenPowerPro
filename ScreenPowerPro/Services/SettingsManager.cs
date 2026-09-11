using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace ScreenPowerPro.Services;

public class SettingsManager : INotifyPropertyChanged
{
    private static SettingsManager? _instance;
    public static SettingsManager Instance => _instance ??= new SettingsManager();

    private readonly SettingsService _settingsService;

    public SettingsManager() : this(App.Current?.Services?.GetService<SettingsService>() ?? new SettingsService())
    {
    }

    public SettingsManager(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _instance ??= this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public double ZoomSpeed
    {
        get => _settingsService.Current.ZoomSpeed;
        set
        {
            if (_settingsService.Current.ZoomSpeed != value)
            {
                _settingsService.Current.ZoomSpeed = value;
                _settingsService.Save();
                OnPropertyChanged();
            }
        }
    }

    public double MaxZoomRatio
    {
        get => _settingsService.Current.MaxZoomRatio;
        set
        {
            if (_settingsService.Current.MaxZoomRatio != value)
            {
                _settingsService.Current.MaxZoomRatio = value;
                _settingsService.Save();
                OnPropertyChanged();
            }
        }
    }

    public double ZoomDuration
    {
        get => _settingsService.Current.ZoomDuration;
        set
        {
            if (_settingsService.Current.ZoomDuration != value)
            {
                _settingsService.Current.ZoomDuration = value;
                _settingsService.Save();
                OnPropertyChanged();
            }
        }
    }

    public bool CancelOnOutOfBounds
    {
        get => _settingsService.Current.CancelOnOutOfBounds;
        set
        {
            if (_settingsService.Current.CancelOnOutOfBounds != value)
            {
                _settingsService.Current.CancelOnOutOfBounds = value;
                _settingsService.Save();
                OnPropertyChanged();
            }
        }
    }
}
