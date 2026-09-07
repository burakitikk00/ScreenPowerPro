using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using ScreenPowerPro.Models;
using ScreenPowerPro.Services;
using ScreenPowerPro.ViewModels;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.UI;

namespace ScreenPowerPro.Views;

public sealed partial class EditorPage : Page
{
    public EditorViewModel ViewModel { get; }

    private string? _projectDir;
    private bool _isPlaying = false;
    private double _timelineScale = 100.0;
    private double _totalDurationSeconds = 0;
    private double _currentTimeSeconds = 0;
    private ZoomEffect? _selectedZoom = null;
    private DispatcherTimer? _playbackTimer;

    private Windows.Media.Playback.MediaPlayer? _micPlayer;
    private Windows.Media.Playback.MediaPlayer? _sysPlayer;
    private bool _isDraggingPlayhead = false;

    public EditorPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<EditorViewModel>();
        DataContext = ViewModel;
        Loaded += OnPageLoaded;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        string? projectDir = e.Parameter as string;
        if (string.IsNullOrEmpty(projectDir))
        {
            var projectService = App.Current.Services.GetRequiredService<ProjectService>();
            var recents = projectService.GetRecentProjects();
            if (recents.Count > 0)
            {
                projectDir = recents[0].FolderPath;
            }
        }

        if (!string.IsNullOrEmpty(projectDir))
        {
            _projectDir = projectDir;
            ViewModel.LoadProject(projectDir);
            UpdateFromViewModel();
            RenderTimeline();
            await LoadVideoAsync();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        PausePlayback();
        _playbackTimer?.Stop();

        try
        {
            _micPlayer?.Dispose();
            _micPlayer = null;
            _sysPlayer?.Dispose();
            _sysPlayer = null;
        }
        catch { }
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _playbackTimer.Tick += OnPlaybackTimerTick;

        UpdateFromViewModel();
        RenderTimeline();

        if (TimelineScrollViewer != null)
        {
            TimelineScrollViewer.PointerWheelChanged += OnTimelineWheelChanged;
        }

        // ViewModel seek bildirimlerini dinle ve video oynatıcısını senkronize et
        ViewModel.PropertyChanged += (s, args) =>
        {
            if (args.PropertyName == nameof(EditorViewModel.SeekVersion))
            {
                SeekToTime(ViewModel.CurrentTimeSec);
            }
            else if (args.PropertyName == nameof(EditorViewModel.TotalDurationSec))
            {
                _totalDurationSeconds = ViewModel.TotalDurationSec;
                UpdateFromViewModel();
                RenderTimeline();
            }
        };

        ViewModel.NavigateToExport += OnNavigateToExport;

        // Video henüz yüklenmediyse yükle
        if (VideoPlayer.Source == null && !string.IsNullOrEmpty(ViewModel.VideoPath))
        {
            await LoadVideoAsync();
        }
    }

    private void OnNavigateToExport(string projectDir)
    {
        PausePlayback();
        MainWindow.CurrentInstance?.NavigateToExport(projectDir);
    }

    public async Task LoadVideoAsync()
    {
        if (string.IsNullOrEmpty(ViewModel.VideoPath)) return;

        string videoPath = ViewModel.VideoPath;

        if (!File.Exists(videoPath))
        {
            if (NoVideoMessage != null)
            {
                NoVideoMessage.Visibility = Visibility.Visible;
                TbMissingVideoPath.Text = videoPath;
            }
            return;
        }

        if (NoVideoMessage != null)
        {
            NoVideoMessage.Visibility = Visibility.Collapsed;
        }

        try
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(videoPath);
            var mediaSource = MediaSource.CreateFromStorageFile(storageFile);
            VideoPlayer.Source = mediaSource;

            var player = VideoPlayer.MediaPlayer;
            if (player != null)
            {
                player.AutoPlay = false;
                player.MediaEnded -= OnMediaPlayerEnded;
                player.MediaEnded += OnMediaPlayerEnded;
                player.MediaOpened -= OnMediaPlayerOpened;
                player.MediaOpened += OnMediaPlayerOpened;
                player.MediaFailed -= OnMediaPlayerFailed;
                player.MediaFailed += OnMediaPlayerFailed;

                if (player.PlaybackSession != null)
                {
                    player.PlaybackSession.PlaybackRate = ViewModel.VideoSpeed > 0 ? ViewModel.VideoSpeed : 1.0;
                }
            }

            await SetupAudioPlayersAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EditorPage] Video yüklenirken hata: {ex.Message}");
            if (NoVideoMessage != null)
            {
                NoVideoMessage.Visibility = Visibility.Visible;
                TbMissingVideoPath.Text = $"Hata: {ex.Message}\n{videoPath}";
            }
        }
    }

    private async Task SetupAudioPlayersAsync()
    {
        try
        {
            if (!string.IsNullOrEmpty(ViewModel.MicAudioPath) && File.Exists(ViewModel.MicAudioPath))
            {
                var micFile = await StorageFile.GetFileFromPathAsync(ViewModel.MicAudioPath);
                _micPlayer = new Windows.Media.Playback.MediaPlayer
                {
                    Source = MediaSource.CreateFromStorageFile(micFile),
                    AutoPlay = false,
                    Volume = ViewModel.MicVolume / 100.0
                };
            }
        }
        catch { }

        try
        {
            if (!string.IsNullOrEmpty(ViewModel.SystemAudioPath) && File.Exists(ViewModel.SystemAudioPath))
            {
                var sysFile = await StorageFile.GetFileFromPathAsync(ViewModel.SystemAudioPath);
                _sysPlayer = new Windows.Media.Playback.MediaPlayer
                {
                    Source = MediaSource.CreateFromStorageFile(sysFile),
                    AutoPlay = false,
                    Volume = ViewModel.SysVolume / 100.0
                };
            }
        }
        catch { }
    }

    private void OnMediaPlayerOpened(Windows.Media.Playback.MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (sender.PlaybackSession != null && sender.PlaybackSession.NaturalDuration > TimeSpan.Zero)
            {
                double realDuration = sender.PlaybackSession.NaturalDuration.TotalSeconds;
                if (realDuration > 0 && Math.Abs(_totalDurationSeconds - realDuration) > 0.2)
                {
                    _totalDurationSeconds = realDuration;
                    ViewModel.TotalDurationSec = realDuration;
                    UpdateFromViewModel();
                    RenderTimeline();
                }
            }
        });
    }

    private void OnMediaPlayerEnded(Windows.Media.Playback.MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            PausePlayback();
            SeekToTime(0);
        });
    }

    private void OnMediaPlayerFailed(Windows.Media.Playback.MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            PausePlayback();
            System.Diagnostics.Debug.WriteLine($"[EditorPage] Medya oynatılamadı: {args.ErrorMessage}");
        });
    }

    private void OnTimelineWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint(TimelineScrollViewer).Properties;
        
        // Check if Ctrl key is pressed
        var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        bool isCtrlPressed = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

        if (isCtrlPressed)
        {
            e.Handled = true;
            int delta = properties.MouseWheelDelta;
            
            // Adjust slider value based on scroll direction
            double newZoom = TimelineZoomSlider.Value + (delta > 0 ? 5 : -5);
            TimelineZoomSlider.Value = Math.Clamp(newZoom, TimelineZoomSlider.Minimum, TimelineZoomSlider.Maximum);
            
            // OnTimelineZoomChanged will be called automatically by the slider, 
            // which will update _timelineScale and call RenderTimeline()
        }
    }

    private void OnMuteClicked(object sender, RoutedEventArgs e)
    {
        if (VideoPlayer.MediaPlayer != null)
        {
            bool newMuted = !VideoPlayer.MediaPlayer.IsMuted;
            VideoPlayer.MediaPlayer.IsMuted = newMuted;
            if (_micPlayer != null) _micPlayer.IsMuted = newMuted;
            if (_sysPlayer != null) _sysPlayer.IsMuted = newMuted;
            MuteIcon.Glyph = newMuted ? "\uE74F" : "\uE767"; // E74F is Mute, E767 is Volume
        }
    }

    private void UpdateFromViewModel()
    {
        if (ViewModel == null) return;

        TbProjectName.Text = ViewModel.ProjectName + ".mp4";
        _totalDurationSeconds = ViewModel.TotalDurationSec;

        TbTotalTime.Text = FormatTime(_totalDurationSeconds);
        TbTimelineTotal.Text = FormatTime(_totalDurationSeconds);
        TbCurrentTime.Text = FormatTime(0);
        TbTimelineCurrent.Text = FormatTime(0);

        double clipWidth = _totalDurationSeconds * _timelineScale;
        VideoClipBlock.Width = Math.Max(clipWidth, 200);
    }

    private void RenderTimeline()
    {
        if (TimeRuler == null || VideoTrack == null || ZoomTrack == null) return;

        double totalWidth = Math.Max(_totalDurationSeconds * _timelineScale, 800);
        TimelineContentGrid.MinWidth = totalWidth + 80;

        RenderTimeRuler(totalWidth);
        RenderZoomPills();
        UpdatePlayhead();
    }

    private void RenderTimeRuler(double totalWidth)
    {
        TimeRuler.Children.Clear();
        TimeRuler.Width = totalWidth + 80;

        double step = 10.0;
        int tickCount = (int)((_totalDurationSeconds + step) / step);

        for (int i = 0; i <= tickCount; i++)
        {
            double sec = i * step;
            double x = 40 + sec * _timelineScale;

            var line = new Rectangle
            {
                Width = 1,
                Height = (i % 6 == 0) ? 10 : 5,
                Fill = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255))
            };
            Canvas.SetLeft(line, x);
            Canvas.SetTop(line, 12);
            TimeRuler.Children.Add(line);

            if (i % 6 == 0)
            {
                var label = new TextBlock
                {
                    Text = FormatTimeShort(sec),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(130, 199, 196, 215)),
                    FontFamily = new FontFamily("Consolas")
                };
                Canvas.SetLeft(label, x + 2);
                Canvas.SetTop(label, 4);
                TimeRuler.Children.Add(label);
            }
        }

        VideoTrack.Children.Clear();
        VideoTrack.Children.Add(VideoClipBlock);
        VideoClipBlock.Width = Math.Max(_totalDurationSeconds * _timelineScale, 200);
        Canvas.SetLeft(VideoClipBlock, 40);

        for (int i = 0; i <= tickCount; i++)
        {
            double x = 40 + i * step * _timelineScale;
            var gridLine = new Rectangle
            {
                Width = 1,
                Height = 72,
                Fill = new SolidColorBrush(Color.FromArgb(13, 255, 255, 255))
            };
            Canvas.SetLeft(gridLine, x);
            VideoTrack.Children.Add(gridLine);
        }
    }

    private void RenderZoomPills()
    {
        if (ZoomTrack == null || ViewModel == null) return;
        ZoomTrack.Children.Clear();

        foreach (var zoom in ViewModel.ZoomEffects)
        {
            AddZoomPill(zoom);
        }
    }

    private Border AddZoomPill(ZoomEffect zoom)
    {
        double x = 40 + zoom.StartTime * _timelineScale;
        double width = Math.Max(zoom.Duration * _timelineScale, 20); // Minimum 20px width

        bool isSelected = _selectedZoom == zoom;

        var pill = new Border
        {
            Width = width,
            Height = 34,
            CornerRadius = new CornerRadius(17),
            Background = isSelected
                ? new SolidColorBrush(Color.FromArgb(100, 208, 188, 255))
                : new SolidColorBrush(Color.FromArgb(50, 160, 120, 255)),
            BorderBrush = isSelected
                ? new SolidColorBrush(Color.FromArgb(255, 208, 188, 255))
                : new SolidColorBrush(Color.FromArgb(128, 208, 188, 255)),
            BorderThickness = isSelected ? new Thickness(2) : new Thickness(1),
            Tag = zoom,
            IsHitTestVisible = true
        };

        var label = new TextBlock
        {
            Text = $"{zoom.Scale:F1}x",
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 208, 188, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (isSelected)
        {
            var grid = new Grid();
            grid.Children.Add(label);
            
            // Delete button
            var closeBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE711", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(200, 208, 188, 255)) },
                Background = new SolidColorBrush(Color.FromArgb(150, 208, 188, 255)),
                BorderThickness = new Thickness(0),
                Width = 18, Height = 18,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -4, -4, 0),
                Tag = zoom
            };
            closeBtn.Click += (s, e) => { if (s is Button b && b.Tag is ZoomEffect z) DeleteZoom(z); };
            grid.Children.Add(closeBtn);

            // Resize handle (right edge)
            var resizeHandle = new Border
            {
                Width = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(Colors.Transparent)
            };
            grid.Children.Add(resizeHandle);
            
            pill.Child = grid;
            
            // Setup Drag and Resize logic
            bool isDragging = false;
            bool isResizing = false;
            Point startPoint = default;
            double initialX = 0;
            double initialWidth = 0;

            pill.PointerPressed += (s, e) =>
            {
                e.Handled = true;
                SelectZoom(zoom);
                var ptr = e.GetCurrentPoint(ZoomTrack);
                startPoint = ptr.Position;
                initialX = Canvas.GetLeft(pill);
                initialWidth = pill.Width;

                // Check if user clicked on the right edge (within 8 pixels)
                if (e.GetCurrentPoint(pill).Position.X >= pill.Width - 8)
                {
                    isResizing = true;
                }
                else
                {
                    isDragging = true;
                }
                pill.CapturePointer(e.Pointer);
            };

            pill.PointerMoved += (s, e) =>
            {
                if (isDragging)
                {
                    var ptr = e.GetCurrentPoint(ZoomTrack);
                    double dx = ptr.Position.X - startPoint.X;
                    double newX = Math.Max(40, initialX + dx); // 40 is track start offset
                    Canvas.SetLeft(pill, newX);
                    zoom.StartTime = (newX - 40) / _timelineScale;
                    NbZoomStart.Value = zoom.StartTime;
                }
                else if (isResizing)
                {
                    var ptr = e.GetCurrentPoint(ZoomTrack);
                    double dx = ptr.Position.X - startPoint.X;
                    double newWidth = Math.Max(20, initialWidth + dx);
                    pill.Width = newWidth;
                    zoom.Duration = newWidth / _timelineScale;
                    NbZoomEnd.Value = zoom.StartTime + zoom.Duration;
                }
            };

            pill.PointerReleased += (s, e) =>
            {
                isDragging = false;
                isResizing = false;
                pill.ReleasePointerCapture(e.Pointer);
            };
        }
        else
        {
            pill.Child = label;
            pill.PointerPressed += (s, e) => 
            {
                e.Handled = true;
                SelectZoom(zoom);
            };
        }

        Canvas.SetLeft(pill, x);
        Canvas.SetTop(pill, 11);
        ZoomTrack.Children.Add(pill);

        return pill;
    }

    private void UpdatePlayhead()
    {
        if (PlayheadLine == null) return;
        double x = 40 + _currentTimeSeconds * _timelineScale;
        Canvas.SetLeft(PlayheadLine, x - 1);
        Canvas.SetLeft(PlayheadTriangle, 0);
        PlayheadLine.Height = 72 + 56 + 22;

        double triX = x;
        PlayheadTriangle.Points = new PointCollection
        {
            new Point(triX - 7, 0),
            new Point(triX + 7, 0),
            new Point(triX, 12)
        };
    }

    private void SelectZoom(ZoomEffect zoom)
    {
        _selectedZoom = zoom;
        ZoomPropertiesPanel.Visibility = Visibility.Visible;
        TbSelectedZoomFactor.Text = $"{zoom.Scale:F1}x";
        SelectedZoomSlider.Value = zoom.Scale;
        NbZoomStart.Value = zoom.StartTime;
        NbZoomEnd.Value = zoom.StartTime + zoom.Duration;
        ZoomLevelBadge.Text = $"{zoom.Scale:F1}x";
        RenderZoomPills();
    }

    private void DeleteZoom(ZoomEffect zoom)
    {
        ViewModel?.ZoomEffects?.Remove(zoom);
        if (_selectedZoom == zoom)
        {
            _selectedZoom = null;
            ZoomPropertiesPanel.Visibility = Visibility.Collapsed;
            ZoomLevelBadge.Text = "1.0x";
        }
        RenderZoomPills();
    }

    private void OnZoomTrackDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel == null) return;

        var pos = e.GetPosition(ZoomTrack);
        double clickSec = Math.Max(0, (pos.X - 40) / _timelineScale);

        var newZoom = new ZoomEffect
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = $"Zoom {ViewModel.ZoomEffects.Count + 1}",
            StartTime = clickSec,
            Duration = 3.0,
            Scale = 1.5,
            TargetX = 1920 / 2,
            TargetY = 1080 / 2
        };

        ViewModel.ZoomEffects.Add(newZoom);
        SelectZoom(newZoom);
    }

    private void OnPlayPauseClicked(object sender, RoutedEventArgs e)
    {
        if (_isPlaying) PausePlayback();
        else StartPlayback();
    }

    private void StartPlayback()
    {
        if (_currentTimeSeconds >= _totalDurationSeconds - 0.1 && _totalDurationSeconds > 0)
        {
            SeekToTime(0);
        }

        _isPlaying = true;
        PlayPauseIcon.Glyph = "\uE769";
        if (PlayOverlay != null) PlayOverlay.Opacity = 0;

        VideoPlayer?.MediaPlayer?.Play();
        _micPlayer?.Play();
        _sysPlayer?.Play();

        _playbackTimer?.Start();
    }

    private void PausePlayback()
    {
        _isPlaying = false;
        PlayPauseIcon.Glyph = "\uE768";
        if (PlayOverlay != null) PlayOverlay.Opacity = 1;

        VideoPlayer?.MediaPlayer?.Pause();
        _micPlayer?.Pause();
        _sysPlayer?.Pause();

        _playbackTimer?.Stop();
    }

    private void OnPlaybackTimerTick(object? sender, object e)
    {
        if (VideoPlayer?.MediaPlayer == null) return;
        var pos = VideoPlayer.MediaPlayer.Position;
        _currentTimeSeconds = pos.TotalSeconds;

        if (_currentTimeSeconds >= _totalDurationSeconds && _totalDurationSeconds > 0)
        {
            PausePlayback();
            SeekToTime(0);
            return;
        }

        // Ses kanallarını video ile senkron tut
        if (_isPlaying)
        {
            if (_micPlayer != null && Math.Abs((_micPlayer.Position - pos).TotalMilliseconds) > 150)
            {
                _micPlayer.Position = pos;
            }
            if (_sysPlayer != null && Math.Abs((_sysPlayer.Position - pos).TotalMilliseconds) > 150)
            {
                _sysPlayer.Position = pos;
            }
        }

        ViewModel.CurrentTimeSec = _currentTimeSeconds;
        TbCurrentTime.Text = FormatTime(_currentTimeSeconds);
        TbTimelineCurrent.Text = FormatTime(_currentTimeSeconds);
        UpdatePlayhead();
        UpdateZoomSimulation();
    }

    /// <summary>
    /// Video oynatımı veya timeline üzerinde gezinirken aktif keyframe tabanlı
    /// zoom/pan durumunu hesaplar ve VideoPlayer üzerine canlı CompositeTransform olarak uygular.
    /// </summary>
    private void UpdateZoomSimulation()
    {
        var activeZoom = ViewModel?.GetCurrentZoom();
        if (activeZoom != null)
        {
            ZoomLevelBadge.Text = $"{activeZoom.Scale:F1}x";
            if (VideoTransform != null)
            {
                VideoTransform.ScaleX = activeZoom.Scale;
                VideoTransform.ScaleY = activeZoom.Scale;

                // Hedef tıklama koordinatını merkeze getiren pan ötelemesi
                double renderWidth = VideoPlayer?.ActualWidth > 0 ? VideoPlayer.ActualWidth : 880;
                double renderHeight = VideoPlayer?.ActualHeight > 0 ? VideoPlayer.ActualHeight : 495;

                double normCenterX = 1920.0 / 2.0;
                double normCenterY = 1080.0 / 2.0;

                double offsetX = (normCenterX - activeZoom.TargetX) * (renderWidth / 1920.0) * (activeZoom.Scale - 1.0);
                double offsetY = (normCenterY - activeZoom.TargetY) * (renderHeight / 1080.0) * (activeZoom.Scale - 1.0);

                VideoTransform.TranslateX = Math.Clamp(offsetX, -renderWidth / 2.0, renderWidth / 2.0);
                VideoTransform.TranslateY = Math.Clamp(offsetY, -renderHeight / 2.0, renderHeight / 2.0);
            }
        }
        else
        {
            ZoomLevelBadge.Text = "1.0x";
            if (VideoTransform != null)
            {
                VideoTransform.ScaleX = 1.0;
                VideoTransform.ScaleY = 1.0;
                VideoTransform.TranslateX = 0;
                VideoTransform.TranslateY = 0;
            }
        }
    }

    /// <summary>
    /// Zaman çizelgesi cetveline veya video parçasına tıklandığında oynatma kafasını belirtilen saniyeye sarar.
    /// </summary>
    private void OnTimelinePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            element.CapturePointer(e.Pointer);
            _isDraggingPlayhead = true;
            var ptr = e.GetCurrentPoint(element);
            double clickSec = Math.Max(0, (ptr.Position.X - 40) / _timelineScale);
            SeekToTime(clickSec);
        }
    }

    private void OnTimelinePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isDraggingPlayhead && sender is UIElement element)
        {
            var ptr = e.GetCurrentPoint(element);
            double clickSec = Math.Max(0, (ptr.Position.X - 40) / _timelineScale);
            SeekToTime(clickSec);
        }
    }

    private void OnTimelinePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isDraggingPlayhead && sender is UIElement element)
        {
            element.ReleasePointerCapture(e.Pointer);
            _isDraggingPlayhead = false;
        }
    }

    public void SeekToTime(double time)
    {
        _currentTimeSeconds = Math.Clamp(time, 0, _totalDurationSeconds);
        var ts = TimeSpan.FromSeconds(_currentTimeSeconds);

        if (VideoPlayer?.MediaPlayer != null)
        {
            VideoPlayer.MediaPlayer.Position = ts;
        }
        if (_micPlayer != null)
        {
            _micPlayer.Position = ts;
        }
        if (_sysPlayer != null)
        {
            _sysPlayer.Position = ts;
        }

        ViewModel.CurrentTimeSec = _currentTimeSeconds;
        TbCurrentTime.Text = FormatTime(_currentTimeSeconds);
        TbTimelineCurrent.Text = FormatTime(_currentTimeSeconds);
        UpdatePlayhead();
        UpdateZoomSimulation();
    }

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var focused = FocusManager.GetFocusedElement(this.XamlRoot);
        if (focused is TextBox || focused is NumberBox) return;

        var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        bool isCtrl = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

        switch (e.Key)
        {
            case Windows.System.VirtualKey.Space:
                e.Handled = true;
                if (_isPlaying) PausePlayback();
                else StartPlayback();
                break;

            case Windows.System.VirtualKey.Left:
                e.Handled = true;
                SeekToTime(_currentTimeSeconds - (isCtrl ? 5.0 : 1.0));
                break;

            case Windows.System.VirtualKey.Right:
                e.Handled = true;
                SeekToTime(_currentTimeSeconds + (isCtrl ? 5.0 : 1.0));
                break;

            case Windows.System.VirtualKey.Home:
                e.Handled = true;
                SeekToTime(0);
                break;

            case Windows.System.VirtualKey.End:
                e.Handled = true;
                SeekToTime(_totalDurationSeconds);
                break;

            case Windows.System.VirtualKey.Z when isCtrl:
                e.Handled = true;
                OnUndoClicked(this, new RoutedEventArgs());
                break;

            case Windows.System.VirtualKey.Y when isCtrl:
                e.Handled = true;
                OnRedoClicked(this, new RoutedEventArgs());
                break;

            case Windows.System.VirtualKey.Delete:
            case Windows.System.VirtualKey.Back:
                if (_selectedZoom != null)
                {
                    e.Handled = true;
                    DeleteZoom(_selectedZoom);
                }
                break;

            case Windows.System.VirtualKey.S when !isCtrl:
                e.Handled = true;
                OnSplitClicked(this, new RoutedEventArgs());
                break;
        }
    }

    private void OnSkipPrevClicked(object sender, RoutedEventArgs e)
    {
        SeekToTime(0);
    }

    private void OnSkipNextClicked(object sender, RoutedEventArgs e)
    {
        SeekToTime(_totalDurationSeconds);
    }

    private void OnVideoCanvasClicked(object sender, RoutedEventArgs e)
    {
        if (_isPlaying) PausePlayback();
        else StartPlayback();
    }

    private void OnTimelineZoomChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        _timelineScale = 20 + (e.NewValue / 100.0) * 280;
        RenderTimeline();
    }

    private void OnZoomLevelChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (TbZoomLevel != null)
            TbZoomLevel.Text = $"{(int)e.NewValue}%";
        if (ViewModel != null)
            ViewModel.DefaultZoomScale = e.NewValue / 100.0;
    }

    private void OnMotionBlurChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (TbMotionBlur != null)
            TbMotionBlur.Text = $"{(int)e.NewValue}%";
        if (ViewModel != null)
            ViewModel.MotionBlurAmount = e.NewValue;
    }

    private void OnOpacityChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (TbOpacity != null)
            TbOpacity.Text = $"{(int)e.NewValue}%";
        if (ViewModel != null)
            ViewModel.BackgroundOpacity = e.NewValue;
    }

    private void OnMicVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (TbMicVolVal != null)
            TbMicVolVal.Text = $"{(int)e.NewValue}%";
        if (ViewModel != null)
            ViewModel.MicVolume = e.NewValue;
        if (_micPlayer != null)
            _micPlayer.Volume = e.NewValue / 100.0;
    }

    private void OnSysVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (TbSysVolVal != null)
            TbSysVolVal.Text = $"{(int)e.NewValue}%";
        if (ViewModel != null)
            ViewModel.SysVolume = e.NewValue;
        if (_sysPlayer != null)
            _sysPlayer.Volume = e.NewValue / 100.0;
    }

    private void OnSelectedZoomFactorChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_selectedZoom == null) return;
        _selectedZoom.Scale = e.NewValue;
        TbSelectedZoomFactor.Text = $"{e.NewValue:F1}x";
        RenderZoomPills();
        UpdateZoomSimulation();
    }

    private void OnSpeedSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        ResetSpeedButton(BtnSpeedSlow);
        ResetSpeedButton(BtnSpeedMedium);
        ResetSpeedButton(BtnSpeedFast);
        btn.Background = new SolidColorBrush(Color.FromArgb(255, 13, 14, 21));
        btn.Foreground = new SolidColorBrush(Color.FromArgb(255, 192, 193, 255));

        if (ViewModel != null)
        {
            double speed = btn.Tag switch
            {
                "Slow" => 0.5,
                "Fast" => 1.5,
                _ => 1.0
            };
            ViewModel.VideoSpeed = speed;
            if (VideoPlayer?.MediaPlayer?.PlaybackSession != null)
            {
                VideoPlayer.MediaPlayer.PlaybackSession.PlaybackRate = speed;
            }
            if (_micPlayer?.PlaybackSession != null)
            {
                _micPlayer.PlaybackSession.PlaybackRate = speed;
            }
            if (_sysPlayer?.PlaybackSession != null)
            {
                _sysPlayer.PlaybackSession.PlaybackRate = speed;
            }
        }
    }

    private void ResetSpeedButton(Button btn)
    {
        btn.Background = new SolidColorBrush(Colors.Transparent);
        btn.Foreground = new SolidColorBrush(Color.FromArgb(130, 199, 196, 215));
    }

    private void OnBgColorSelected(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border border && border.Tag is string tag && ViewModel != null)
        {
            switch (tag)
            {
                case "Black":
                    ViewModel.CanvasBackground = "#000000";
                    ViewModel.BackgroundStyle = "dark";
                    break;
                case "DarkGray":
                    ViewModel.CanvasBackground = "#1A1A1A";
                    ViewModel.BackgroundStyle = "gradient-1";
                    break;
                case "Purple":
                    ViewModel.CanvasBackground = "#1E1B4B";
                    ViewModel.BackgroundStyle = "gradient-2";
                    break;
            }
            ViewModel.SaveProject();
        }
    }

    private void OnAddZoomClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        ViewModel.PushHistory();

        var newZoom = new ZoomEffect
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = $"Zoom {ViewModel.ZoomEffects.Count + 1}",
            StartTime = Math.Round(_currentTimeSeconds, 2),
            Duration = 3.0,
            Scale = ViewModel.DefaultZoomScale,
            TargetX = 1920 / 2.0,
            TargetY = 1080 / 2.0,
            Easing = "ease-in-out"
        };
        ViewModel.ZoomEffects.Add(newZoom);
        SelectZoom(newZoom);
    }

    private void OnDeleteZoomClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedZoom != null)
            DeleteZoom(_selectedZoom);
    }

    private void OnUndoClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.Undo();
        UpdateFromViewModel();
        RenderTimeline();
        UpdateZoomSimulation();
    }

    private void OnRedoClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.Redo();
        UpdateFromViewModel();
        RenderTimeline();
        UpdateZoomSimulation();
    }

    private void OnSplitClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.CutAtPlayhead();
        RenderTimeline();
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        PausePlayback();
        MainWindow.CurrentInstance?.NavigateToDashboard();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.SaveProject();
    }

    private void OnExportClicked(object sender, RoutedEventArgs e)
    {
        PausePlayback();
        ViewModel?.SaveProject();
        if (!string.IsNullOrEmpty(ViewModel?.ProjectDir))
        {
            MainWindow.CurrentInstance?.NavigateToExport(ViewModel.ProjectDir);
        }
    }

    private static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    private static string FormatTimeShort(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }
}

file static class MediaPlayerExtensions
{
    public static void let(this Windows.Media.Playback.MediaPlaybackSession? session, Action<Windows.Media.Playback.MediaPlaybackSession> action)
    {
        if (session != null) action(session);
    }
}
