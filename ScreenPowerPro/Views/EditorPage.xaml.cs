using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using ScreenPowerPro.Models;
using ScreenPowerPro.Services;
using ScreenPowerPro.ViewModels;
using Windows.Foundation;
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

    public EditorPage()
    {
        InitializeComponent();
        ViewModel = App.Current.Services.GetRequiredService<EditorViewModel>();
        DataContext = ViewModel;
        Loaded += OnPageLoaded;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string projectDir)
        {
            _projectDir = projectDir;
            ViewModel.LoadProject(projectDir);
        }
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
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
        if (MainPlayer.MediaPlayer != null)
        {
            MainPlayer.MediaPlayer.IsMuted = !MainPlayer.MediaPlayer.IsMuted;
            MuteIcon.Glyph = MainPlayer.MediaPlayer.IsMuted ? "\uE74F" : "\uE767"; // E74F is Mute, E767 is Volume
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
                Background = new SolidColorBrush(Colors.Transparent),
                Cursor = new Microsoft.UI.Input.InputCursor(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast)
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
        _isPlaying = true;
        PlayPauseIcon.Glyph = "\uE769";
        VideoPlayer?.MediaPlayer?.Play();
        _playbackTimer?.Start();
    }

    private void PausePlayback()
    {
        _isPlaying = false;
        PlayPauseIcon.Glyph = "\uE768";
        VideoPlayer?.MediaPlayer?.Pause();
        _playbackTimer?.Stop();
    }

    private void OnPlaybackTimerTick(object? sender, object e)
    {
        if (VideoPlayer?.MediaPlayer == null) return;
        var pos = VideoPlayer.MediaPlayer.Position;
        _currentTimeSeconds = pos.TotalSeconds;
        TbCurrentTime.Text = FormatTime(_currentTimeSeconds);
        TbTimelineCurrent.Text = FormatTime(_currentTimeSeconds);
        UpdatePlayhead();

        var activeZoom = ViewModel?.ZoomEffects?
            .FirstOrDefault(z => z.StartTime <= _currentTimeSeconds && (z.StartTime + z.Duration) >= _currentTimeSeconds);
        ZoomLevelBadge.Text = activeZoom != null ? $"{activeZoom.Scale:F1}x" : "1.0x";
    }

    private void OnSkipPrevClicked(object sender, RoutedEventArgs e)
    {
        _currentTimeSeconds = 0;
        VideoPlayer?.MediaPlayer?.PlaybackSession?.let(s => s.Position = TimeSpan.Zero);
        UpdatePlayhead();
        TbCurrentTime.Text = FormatTime(0);
        TbTimelineCurrent.Text = FormatTime(0);
    }

    private void OnSkipNextClicked(object sender, RoutedEventArgs e)
    {
        _currentTimeSeconds = _totalDurationSeconds;
        UpdatePlayhead();
        TbCurrentTime.Text = FormatTime(_currentTimeSeconds);
        TbTimelineCurrent.Text = FormatTime(_currentTimeSeconds);
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
    }

    private void OnMotionBlurChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (TbMotionBlur != null)
            TbMotionBlur.Text = $"{(int)e.NewValue}%";
    }

    private void OnOpacityChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (TbOpacity != null)
            TbOpacity.Text = $"{(int)e.NewValue}%";
    }

    private void OnSelectedZoomFactorChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_selectedZoom == null) return;
        _selectedZoom.Scale = e.NewValue;
        TbSelectedZoomFactor.Text = $"{e.NewValue:F1}x";
        RenderZoomPills();
    }

    private void OnSpeedSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        ResetSpeedButton(BtnSpeedSlow);
        ResetSpeedButton(BtnSpeedMedium);
        ResetSpeedButton(BtnSpeedFast);
        btn.Background = new SolidColorBrush(Color.FromArgb(255, 13, 14, 21));
        btn.Foreground = new SolidColorBrush(Color.FromArgb(255, 192, 193, 255));
    }

    private void ResetSpeedButton(Button btn)
    {
        btn.Background = new SolidColorBrush(Colors.Transparent);
        btn.Foreground = new SolidColorBrush(Color.FromArgb(130, 199, 196, 215));
    }

    private void OnBgColorSelected(object sender, TappedRoutedEventArgs e)
    {
    }

    private void OnAddZoomClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var newZoom = new ZoomEffect
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = $"Zoom {ViewModel.ZoomEffects.Count + 1}",
            StartTime = _currentTimeSeconds,
            Duration = 3.0,
            Scale = 1.5,
            TargetX = 1920 / 2,
            TargetY = 1080 / 2
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
    }

    private void OnRedoClicked(object sender, RoutedEventArgs e)
    {
    }

    private void OnSplitClicked(object sender, RoutedEventArgs e)
    {
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        MainWindow.CurrentInstance?.NavigateToDashboard();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.SaveProject();
    }

    private void OnExportClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.Export();
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
