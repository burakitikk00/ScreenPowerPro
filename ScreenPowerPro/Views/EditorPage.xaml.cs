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

    // Timeline Pan / Drag / Cut Point state
    private bool _isPanningTimeline = false;
    private Point _panStartPoint;
    private double _panStartOffset;
    private bool _isDraggingCutPoint = false;
    private ClipSegment? _cutLeftClip;
    private ClipSegment? _cutRightClip;
    private string? _cutTrackType;
    private double _cutInitialLeftEnd;
    private double _cutInitialRightStart;
    private double _cutInitialRightOffset;
    private Point _cutDragStartPoint;

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
            try
            {
                ViewModel.LoadProject(projectDir);
                UpdateFromViewModel();
                RenderTimeline();
                await LoadVideoAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EditorPage] Proje yükleme hatası: {ex}");
            }
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
        SetSidebarTab("cursor");

        if (TimelineScrollViewer != null)
        {
            TimelineScrollViewer.PointerWheelChanged += OnTimelineWheelChanged;
        }

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
            else if (args.PropertyName == nameof(EditorViewModel.WaveformPeaks))
            {
                RenderAudioTrack(Math.Max(_totalDurationSeconds * _timelineScale, 800));
            }
        };

        ViewModel.NavigateToExport += OnNavigateToExport;

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
            }

            // Mikrofon ses çalarını başlat
            if (!string.IsNullOrEmpty(ViewModel.MicAudioPath) && File.Exists(ViewModel.MicAudioPath))
            {
                try
                {
                    var micFile = await StorageFile.GetFileFromPathAsync(ViewModel.MicAudioPath);
                    _micPlayer = new Windows.Media.Playback.MediaPlayer
                    {
                        Source = MediaSource.CreateFromStorageFile(micFile),
                        AutoPlay = false,
                        Volume = ViewModel.MicMuted ? 0 : (ViewModel.MicVolume / 100.0)
                    };
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[EditorPage] Mic audio player hatası: {ex}");
                }
            }

            // Sistem sesi çalarını başlat
            if (!string.IsNullOrEmpty(ViewModel.SystemAudioPath) && File.Exists(ViewModel.SystemAudioPath))
            {
                try
                {
                    var sysFile = await StorageFile.GetFileFromPathAsync(ViewModel.SystemAudioPath);
                    _sysPlayer = new Windows.Media.Playback.MediaPlayer
                    {
                        Source = MediaSource.CreateFromStorageFile(sysFile),
                        AutoPlay = false,
                        Volume = ViewModel.SysVolume / 100.0
                    };
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[EditorPage] Sys audio player hatası: {ex}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EditorPage] LoadVideoAsync hatası: {ex}");
        }
    }

    private void OnMediaPlayerOpened(Windows.Media.Playback.MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var session = sender.PlaybackSession;
            if (session != null && session.NaturalDuration > TimeSpan.Zero)
            {
                double dur = session.NaturalDuration.TotalSeconds;
                if (dur > 0 && Math.Abs(_totalDurationSeconds - dur) > 0.5)
                {
                    _totalDurationSeconds = dur;
                    ViewModel.TotalDurationSec = dur;
                    ViewModel.InitClipsFromDuration(dur);
                    UpdateFromViewModel();
                    RenderTimeline();
                }

                uint w = session.NaturalVideoWidth;
                uint h = session.NaturalVideoHeight;
                if (w > 0 && h > 0 && TbResolution != null)
                {
                    TbResolution.Text = $"{w}x{h} • 60fps";
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
            System.Diagnostics.Debug.WriteLine($"[EditorPage] Medya oynatıcı hatası: {args.ErrorMessage}");
        });
    }

    private void UpdateFromViewModel()
    {
        _totalDurationSeconds = ViewModel.TotalDurationSec > 0 ? ViewModel.TotalDurationSec : 10;
        TbProjectName.Text = string.IsNullOrEmpty(ViewModel.ProjectName)
            ? "Untitled Recording.mp4"
            : ViewModel.ProjectName;

        TbTotalTime.Text = FormatTime(_totalDurationSeconds);
        TbTimelineTotal.Text = FormatTime(_totalDurationSeconds);
        TbCurrentTime.Text = FormatTime(0);
        TbTimelineCurrent.Text = FormatTime(0);

        double clipWidth = _totalDurationSeconds * _timelineScale;
        if (VideoClipBlock != null)
        {
            VideoClipBlock.Width = Math.Max(clipWidth, 200);
        }
    }

    // =========================================================================
    // SIDEBAR TABS MANAGEMENT (FOCUSEE STYLE)
    // =========================================================================

    private void OnSidebarTabClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tabTag)
        {
            SetSidebarTab(tabTag);
        }
    }

    private void SetSidebarTab(string tabTag)
    {
        ViewModel.ActiveSidebarTab = tabTag;

        // İkon butonlarının durumunu güncelle
        ResetTabButton(TabBtnCursor, tabTag == "cursor");
        ResetTabButton(TabBtnCanvas, tabTag == "canvas");
        ResetTabButton(TabBtnAudio, tabTag == "audio");
        ResetTabButton(TabBtnKeys, tabTag == "keys");
        ResetTabButton(TabBtnMotion, tabTag == "motion");
        ResetTabButton(TabBtnCamera, tabTag == "camera");
        ResetTabButton(TabBtnVolume, tabTag == "volume");
        ResetTabButton(TabBtnWatermark, tabTag == "watermark");
        ResetTabButton(TabBtnZoom, tabTag == "zoom");

        // Panellerin görünürlüğünü ayarla
        if (PanelTabCursor != null) PanelTabCursor.Visibility = (tabTag == "cursor") ? Visibility.Visible : Visibility.Collapsed;
        if (PanelTabCanvas != null) PanelTabCanvas.Visibility = (tabTag == "canvas") ? Visibility.Visible : Visibility.Collapsed;
        if (PanelTabAudio != null) PanelTabAudio.Visibility = (tabTag == "audio") ? Visibility.Visible : Visibility.Collapsed;
        if (PanelTabKeys != null) PanelTabKeys.Visibility = (tabTag == "keys") ? Visibility.Visible : Visibility.Collapsed;
        if (PanelTabMotion != null) PanelTabMotion.Visibility = (tabTag == "motion") ? Visibility.Visible : Visibility.Collapsed;
        if (PanelTabCamera != null) PanelTabCamera.Visibility = (tabTag == "camera") ? Visibility.Visible : Visibility.Collapsed;
        if (PanelTabVolume != null) PanelTabVolume.Visibility = (tabTag == "volume") ? Visibility.Visible : Visibility.Collapsed;
        if (PanelTabWatermark != null) PanelTabWatermark.Visibility = (tabTag == "watermark") ? Visibility.Visible : Visibility.Collapsed;
        if (PanelTabZoom != null) PanelTabZoom.Visibility = (tabTag == "zoom") ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResetTabButton(Button? btn, bool isActive)
    {
        if (btn == null) return;
        if (isActive)
        {
            btn.Background = new SolidColorBrush(Color.FromArgb(38, 192, 193, 255));
            if (btn.Content is FontIcon fi)
            {
                fi.Foreground = new SolidColorBrush(Color.FromArgb(255, 192, 193, 255));
            }
        }
        else
        {
            btn.Background = new SolidColorBrush(Colors.Transparent);
            if (btn.Content is FontIcon fi)
            {
                fi.Foreground = new SolidColorBrush(Color.FromArgb(144, 144, 160, 255));
            }
        }
    }

    // --- CURSOR TAB HANDLERS ---

    private void OnResetCursorSizeClicked(object sender, RoutedEventArgs e)
    {
        if (CursorSizeSlider != null) CursorSizeSlider.Value = 1.0;
        ViewModel.CursorSize = 100;
        if (TbCursorSizeVal != null) TbCursorSizeVal.Text = "1.0x";
    }

    private void OnCursorSizeSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (TbCursorSizeVal != null) TbCursorSizeVal.Text = $"{e.NewValue:F1}x";
        if (ViewModel != null) ViewModel.CursorSize = e.NewValue * 100.0;
    }

    private void OnCursorStyleSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        ViewModel.CursorStyle = tag;

        HighlightButtonChoice(new[] { BtnCursorStyleDefault, BtnCursorStyleHighlight, BtnCursorStyleSpotlight }, btn);
    }

    private void OnClickEffectSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        ViewModel.ClickEffect = tag;

        HighlightButtonChoice(new[]
        {
            BtnClickNone, BtnClickDefault, BtnClickRipple, BtnClickRing,
            BtnClickDiffusion, BtnClickSpotlight, BtnClickSparkle, BtnClickFirework, BtnClickChristmas
        }, btn);
    }

    // --- CANVAS TAB HANDLERS ---

    private void OnCanvasRatioSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        ViewModel.AspectRatio = tag;

        HighlightButtonChoice(new[] { BtnRatioOrig, BtnRatio169, BtnRatio11, BtnRatio43, BtnRatio916 }, btn);
    }

    private void OnPaddingValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (TbPaddingVal != null) TbPaddingVal.Text = $"{(int)e.NewValue}";
    }

    private void OnInsetValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (TbInsetVal != null) TbInsetVal.Text = $"{(int)e.NewValue}";
    }

    private void OnRoundnessValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (TbRoundnessVal != null) TbRoundnessVal.Text = $"{(int)e.NewValue}";
    }

    private void OnShadowValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (TbShadowVal != null) TbShadowVal.Text = $"{(int)e.NewValue}%";
    }

    private void OnCanvasPresetSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        ViewModel.CanvasPreset = tag;

        HighlightButtonChoice(new[] { BtnPresetDefault, BtnPresetSteady, BtnPresetGraceful, BtnPresetScenery }, btn);
    }

    // --- MIC AUDIO TAB HANDLERS ---

    private void OnResetVolumeEnhanceClicked(object sender, RoutedEventArgs e)
    {
        if (SliderVolEnhance != null) SliderVolEnhance.Value = 1.0;
        ViewModel.VolumeEnhancement = 1.0;
        if (TbVolEnhanceVal != null) TbVolEnhanceVal.Text = "1.0x";
    }

    private void OnVolumeEnhanceChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (TbVolEnhanceVal != null) TbVolEnhanceVal.Text = $"{e.NewValue:F1}x";
        if (ViewModel != null) ViewModel.VolumeEnhancement = e.NewValue;
    }

    // --- SHORTCUT KEYS HANDLERS ---

    private void OnKeyStyleSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        ViewModel.ShortcutKeyStyle = tag;
        HighlightButtonChoice(new[] { BtnKeyStyleFilled, BtnKeyStyleOutline, BtnKeyStyleMinimal }, btn);
    }

    private void OnKeySizeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (TbKeySizeVal != null) TbKeySizeVal.Text = $"{(int)e.NewValue}";
    }

    private void OnKeyPosSelected(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            ViewModel.ShortcutPosition = tag;
        }
    }

    // --- CAMERA HANDLERS ---

    private void OnCameraShapeSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        ViewModel.CameraShape = tag;
        HighlightButtonChoice(new[] { BtnCamCircle, BtnCamSquare, BtnCamRounded }, btn);
    }

    private void HighlightButtonChoice(IEnumerable<Button?> buttons, Button selected)
    {
        foreach (var b in buttons)
        {
            if (b == null) continue;
            if (b == selected)
            {
                b.Background = new SolidColorBrush(Color.FromArgb(38, 192, 193, 255));
                b.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 192, 193, 255));
                b.Foreground = new SolidColorBrush(Color.FromArgb(255, 192, 193, 255));
            }
            else
            {
                b.Background = new SolidColorBrush(Colors.Transparent);
                b.BorderBrush = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
                b.Foreground = new SolidColorBrush(Color.FromArgb(144, 144, 160, 255));
            }
        }
    }

    // =========================================================================
    // TIMELINE RENDERING (VIDEO + AUDIO WAVEFORM + ZOOM EFFECT TRACKS)
    // =========================================================================

    private void RenderTimeline()
    {
        if (TimeRuler == null || VideoTrack == null || AudioTrack == null || ZoomTrack == null) return;

        double totalWidth = Math.Max(_totalDurationSeconds * _timelineScale, 800);
        TimelineContentGrid.MinWidth = totalWidth + 120;

        RenderTimeRuler(totalWidth);
        RenderVideoClips(totalWidth);
        RenderAudioTrack(totalWidth);
        RenderZoomPills();
        UpdatePlayhead();
    }

    private void RenderTimeRuler(double totalWidth)
    {
        TimeRuler.Children.Clear();
        TimeRuler.Width = totalWidth + 120;

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
    }

    private void RenderVideoClips(double totalWidth)
    {
        VideoTrack.Children.Clear();
        VideoTrack.Width = totalWidth + 120;

        // Izgara çizgileri
        double step = 10.0;
        int tickCount = (int)((_totalDurationSeconds + step) / step);
        for (int i = 0; i <= tickCount; i++)
        {
            double x = 40 + i * step * _timelineScale;
            var gridLine = new Rectangle
            {
                Width = 1,
                Height = 72,
                Fill = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255))
            };
            Canvas.SetLeft(gridLine, x);
            VideoTrack.Children.Add(gridLine);
        }

        if (ViewModel == null) return;

        // Eğer hiç klip yoksa tam süreyi kapsayan ilk klibi oluştur
        if (ViewModel.VideoTrack.Clips.Count == 0 && _totalDurationSeconds > 0)
        {
            ViewModel.InitClipsFromDuration(_totalDurationSeconds);
        }

        // Klipleri tek tek çiz
        for (int idx = 0; idx < ViewModel.VideoTrack.Clips.Count; idx++)
        {
            var clip = ViewModel.VideoTrack.Clips[idx];
            AddClipVisualBlock(clip, idx, isAudioTrack: false);
        }
    }

    private void RenderAudioTrack(double totalWidth)
    {
        AudioTrack.Children.Clear();
        AudioTrack.Width = totalWidth + 120;

        // Izgara çizgileri
        double step = 10.0;
        int tickCount = (int)((_totalDurationSeconds + step) / step);
        for (int i = 0; i <= tickCount; i++)
        {
            double x = 40 + i * step * _timelineScale;
            var gridLine = new Rectangle
            {
                Width = 1,
                Height = 56,
                Fill = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255))
            };
            Canvas.SetLeft(gridLine, x);
            AudioTrack.Children.Add(gridLine);
        }

        if (ViewModel == null) return;

        // Waveform verisi hazır değilse yükle
        if (ViewModel.WaveformPeaks == null)
        {
            ViewModel.LoadWaveformData();
        }

        if (ViewModel.MicTrack.Clips.Count == 0 && _totalDurationSeconds > 0)
        {
            ViewModel.InitClipsFromDuration(_totalDurationSeconds);
        }

        // Ses kliplerini ve waveform grafiklerini çiz
        for (int idx = 0; idx < ViewModel.MicTrack.Clips.Count; idx++)
        {
            var clip = ViewModel.MicTrack.Clips[idx];
            AddClipVisualBlock(clip, idx, isAudioTrack: true);
        }
    }

    private void AddClipVisualBlock(ClipSegment clip, int clipIndex, bool isAudioTrack)
    {
        Canvas targetCanvas = isAudioTrack ? AudioTrack : VideoTrack;
        double duration = Math.Max(0.1, clip.SourceEnd - clip.SourceStart);
        double startX = 40 + clip.TrackOffset * _timelineScale;
        double width = Math.Max(duration * _timelineScale, 24);
        double height = isAudioTrack ? 46 : 56;
        double top = isAudioTrack ? 5 : 8;

        bool isSelected = clip.Id == ViewModel.SelectedClipId;

        var clipBlock = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(6),
            Background = isAudioTrack
                ? (isSelected ? new SolidColorBrush(Color.FromArgb(70, 34, 197, 94)) : new SolidColorBrush(Color.FromArgb(35, 34, 197, 94)))
                : (isSelected ? new SolidColorBrush(Color.FromArgb(90, 128, 131, 255)) : new SolidColorBrush(Color.FromArgb(50, 128, 131, 255))),
            BorderBrush = isSelected
                ? new SolidColorBrush(isAudioTrack ? Color.FromArgb(255, 74, 222, 128) : Color.FromArgb(255, 192, 193, 255))
                : new SolidColorBrush(isAudioTrack ? Color.FromArgb(120, 34, 197, 94) : Color.FromArgb(120, 192, 193, 255)),
            BorderThickness = isSelected ? new Thickness(2) : new Thickness(1),
            Tag = clip
        };

        var contentGrid = new Grid();

        // 1. Ses dalga formu grafiği (Eğer ses parçasıysa)
        if (isAudioTrack && ViewModel.WaveformPeaks != null && ViewModel.WaveformPeaks.Length > 0)
        {
            var waveformCanvas = new Canvas
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = false
            };

            int barCount = Math.Max(1, (int)(width / 3.0));
            var peaks = ViewModel.WaveformPeaks;

            for (int b = 0; b < barCount; b++)
            {
                double ratio = b / (double)barCount;
                double t = clip.SourceStart + ratio * duration;
                int peakIdx = (int)Math.Clamp((t / Math.Max(1.0, _totalDurationSeconds)) * peaks.Length, 0, peaks.Length - 1);
                float val = peaks[peakIdx];

                double barH = Math.Max(3, val * 34.0);
                var bar = new Rectangle
                {
                    Width = 2,
                    Height = barH,
                    RadiusX = 1,
                    RadiusY = 1,
                    Fill = new SolidColorBrush(isSelected ? Color.FromArgb(230, 220, 255, 220) : Color.FromArgb(160, 74, 222, 128))
                };
                Canvas.SetLeft(bar, b * 3);
                Canvas.SetTop(bar, (44 - barH) / 2.0);
                waveformCanvas.Children.Add(bar);
            }
            contentGrid.Children.Add(waveformCanvas);
        }

        // 2. Klip etiketi (Başlık ve Süre)
        var label = new TextBlock
        {
            Text = isAudioTrack
                ? $"Audio #{clipIndex + 1} ({duration:F1}s)"
                : $"Video #{clipIndex + 1} ({duration:F1}s)",
            FontSize = 10,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = isAudioTrack
                ? new SolidColorBrush(Color.FromArgb(255, 220, 255, 230))
                : new SolidColorBrush(Color.FromArgb(255, 192, 193, 255)),
            Margin = new Thickness(10, 4, 10, 0),
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false
        };
        contentGrid.Children.Add(label);

        // 3. Sol ve Sağ Trim Tutamaçları
        var leftHandle = new Border
        {
            Width = 8,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
            CornerRadius = new CornerRadius(4, 0, 0, 4)
        };
        var rightHandle = new Border
        {
            Width = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
            CornerRadius = new CornerRadius(0, 4, 4, 0)
        };
        contentGrid.Children.Add(leftHandle);
        contentGrid.Children.Add(rightHandle);

        clipBlock.Child = contentGrid;

        // ETKİLEŞİM: Sürükleme (Drag), Kırpma (Trim) ve Ctrl+Click ile Kesme Noktası Taşıma
        bool isMoving = false;
        bool isTrimmingLeft = false;
        bool isTrimmingRight = false;
        Point startPt = default;
        double origOffset = 0;
        double origStart = 0;
        double origEnd = 0;

        clipBlock.PointerPressed += (s, e) =>
        {
            e.Handled = true;
            clipBlock.CapturePointer(e.Pointer);

            ViewModel.SelectedClipId = clip.Id;
            ViewModel.SelectedTrackType = isAudioTrack ? "mic" : "video";
            RenderTimeline();

            var ptr = e.GetCurrentPoint(targetCanvas);
            startPt = ptr.Position;
            origOffset = clip.TrackOffset;
            origStart = clip.SourceStart;
            origEnd = clip.SourceEnd;

            var localPt = e.GetCurrentPoint(clipBlock).Position;

            var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            bool isCtrl = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

            // Ctrl + Click kesme noktası taşıma kontrolü
            if (isCtrl && localPt.X <= 16 && clipIndex > 0)
            {
                // Sol kesme noktası
                var trackClips = isAudioTrack ? ViewModel.MicTrack.Clips : ViewModel.VideoTrack.Clips;
                _isDraggingCutPoint = true;
                _cutTrackType = isAudioTrack ? "mic" : "video";
                _cutLeftClip = trackClips[clipIndex - 1];
                _cutRightClip = clip;
                _cutInitialLeftEnd = _cutLeftClip.SourceEnd;
                _cutInitialRightStart = _cutRightClip.SourceStart;
                _cutInitialRightOffset = _cutRightClip.TrackOffset;
                _cutDragStartPoint = startPt;
                return;
            }

            if (localPt.X <= 10)
            {
                isTrimmingLeft = true;
            }
            else if (localPt.X >= clipBlock.Width - 10)
            {
                isTrimmingRight = true;
            }
            else
            {
                isMoving = true;
            }
        };

        clipBlock.PointerMoved += (s, e) =>
        {
            if (_isDraggingCutPoint && _cutLeftClip != null && _cutRightClip != null)
            {
                var ptr = e.GetCurrentPoint(targetCanvas);
                double dt = (ptr.Position.X - _cutDragStartPoint.X) / _timelineScale;
                double maxLeftShift = _cutLeftClip.SourceEnd - _cutLeftClip.SourceStart - 0.2;
                double maxRightShift = _cutRightClip.SourceEnd - _cutRightClip.SourceStart - 0.2;

                dt = Math.Clamp(dt, -maxLeftShift, maxRightShift);

                _cutLeftClip.SourceEnd = Math.Round(_cutInitialLeftEnd + dt, 2);
                _cutRightClip.SourceStart = Math.Round(_cutInitialRightStart + dt, 2);
                _cutRightClip.TrackOffset = Math.Round(_cutInitialRightOffset + dt, 2);

                RenderTimeline();
                return;
            }

            if (isMoving)
            {
                var ptr = e.GetCurrentPoint(targetCanvas);
                double dt = (ptr.Position.X - startPt.X) / _timelineScale;
                clip.TrackOffset = Math.Max(0, Math.Round(origOffset + dt, 2));
                Canvas.SetLeft(clipBlock, 40 + clip.TrackOffset * _timelineScale);
            }
            else if (isTrimmingLeft)
            {
                var ptr = e.GetCurrentPoint(targetCanvas);
                double dt = (ptr.Position.X - startPt.X) / _timelineScale;
                double maxTrim = origEnd - origStart - 0.2;
                dt = Math.Clamp(dt, -origOffset, maxTrim);

                clip.SourceStart = Math.Round(origStart + dt, 2);
                clip.TrackOffset = Math.Round(origOffset + dt, 2);

                double newDur = clip.SourceEnd - clip.SourceStart;
                clipBlock.Width = Math.Max(newDur * _timelineScale, 20);
                Canvas.SetLeft(clipBlock, 40 + clip.TrackOffset * _timelineScale);
            }
            else if (isTrimmingRight)
            {
                var ptr = e.GetCurrentPoint(targetCanvas);
                double dt = (ptr.Position.X - startPt.X) / _timelineScale;
                double maxTrim = _totalDurationSeconds - origEnd;
                dt = Math.Clamp(dt, -(origEnd - origStart - 0.2), maxTrim);

                clip.SourceEnd = Math.Round(origEnd + dt, 2);
                double newDur = clip.SourceEnd - clip.SourceStart;
                clipBlock.Width = Math.Max(newDur * _timelineScale, 20);
            }
        };

        clipBlock.PointerReleased += (s, e) =>
        {
            clipBlock.ReleasePointerCapture(e.Pointer);
            if (isMoving || isTrimmingLeft || isTrimmingRight || _isDraggingCutPoint)
            {
                ViewModel.PushHistory();
                ViewModel.SaveProject();
                RenderTimeline();
            }
            isMoving = false;
            isTrimmingLeft = false;
            isTrimmingRight = false;
            _isDraggingCutPoint = false;
            _cutLeftClip = null;
            _cutRightClip = null;
        };

        Canvas.SetLeft(clipBlock, startX);
        Canvas.SetTop(clipBlock, top);
        targetCanvas.Children.Add(clipBlock);
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
        double width = Math.Max(zoom.Duration * _timelineScale, 20);

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

            var closeBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE711", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(200, 208, 188, 255)) },
                Background = new SolidColorBrush(Color.FromArgb(150, 208, 188, 255)),
                BorderThickness = new Thickness(0),
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -4, -4, 0),
                Tag = zoom
            };
            closeBtn.Click += (s, e) => { if (s is Button b && b.Tag is ZoomEffect z) DeleteZoom(z); };
            grid.Children.Add(closeBtn);

            var resizeHandle = new Border
            {
                Width = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(Colors.Transparent)
            };
            grid.Children.Add(resizeHandle);

            pill.Child = grid;

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
                    double newX = Math.Max(40, initialX + dx);
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
        if (PlayheadTriangle != null)
        {
            Canvas.SetLeft(PlayheadTriangle, x);
        }
        PlayheadLine.Height = 22 + 72 + 56 + 56;
    }

    private void SelectZoom(ZoomEffect zoom)
    {
        _selectedZoom = zoom;
        SetSidebarTab("zoom");
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

    // =========================================================================
    // PLAYBACK AND INTERACTION ENGINE
    // =========================================================================

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

        try { VideoPlayer?.MediaPlayer?.Play(); } catch { }

        try
        {
            if (_micPlayer != null && _micPlayer.PlaybackSession?.NaturalDuration > TimeSpan.Zero)
            {
                _micPlayer.Play();
            }
        }
        catch { }

        try
        {
            if (_sysPlayer != null && _sysPlayer.PlaybackSession?.NaturalDuration > TimeSpan.Zero)
            {
                _sysPlayer.Play();
            }
        }
        catch { }

        _playbackTimer?.Start();
    }

    private void PausePlayback()
    {
        _isPlaying = false;
        PlayPauseIcon.Glyph = "\uE768";
        if (PlayOverlay != null) PlayOverlay.Opacity = 1;

        try { VideoPlayer?.MediaPlayer?.Pause(); } catch { }
        try { _micPlayer?.Pause(); } catch { }
        try { _sysPlayer?.Pause(); } catch { }

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

        if (_isPlaying)
        {
            if (_micPlayer?.PlaybackSession != null && _micPlayer.PlaybackSession.NaturalDuration > TimeSpan.Zero)
            {
                if (pos <= _micPlayer.PlaybackSession.NaturalDuration)
                {
                    if (Math.Abs((_micPlayer.Position - pos).TotalMilliseconds) > 600)
                    {
                        try { _micPlayer.Position = pos; } catch { }
                    }
                }
            }

            if (_sysPlayer?.PlaybackSession != null && _sysPlayer.PlaybackSession.NaturalDuration > TimeSpan.Zero)
            {
                if (pos <= _sysPlayer.PlaybackSession.NaturalDuration)
                {
                    if (Math.Abs((_sysPlayer.Position - pos).TotalMilliseconds) > 600)
                    {
                        try { _sysPlayer.Position = pos; } catch { }
                    }
                }
            }
        }

        ViewModel.CurrentTimeSec = _currentTimeSeconds;
        TbCurrentTime.Text = FormatTime(_currentTimeSeconds);
        TbTimelineCurrent.Text = FormatTime(_currentTimeSeconds);
        UpdatePlayhead();
        UpdateZoomSimulation();
    }

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

    // =========================================================================
    // TIMELINE POINTER EVENTS (PANNING + PLAYHEAD SEEK + CTRL ZOOM)
    // =========================================================================

    private void OnTimelinePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            element.CapturePointer(e.Pointer);
            var ptr = e.GetCurrentPoint(element);

            // Eğer boş bir alana tıklandıysa veya cetvele tıklandıysa:
            // 1. Playhead seek
            double clickSec = Math.Max(0, (ptr.Position.X - 40) / _timelineScale);
            SeekToTime(clickSec);
            _isDraggingPlayhead = true;

            // 2. Drag-to-scroll pan hazırlığı
            _isPanningTimeline = true;
            _panStartPoint = e.GetCurrentPoint(TimelineScrollViewer).Position;
            _panStartOffset = TimelineScrollViewer.HorizontalOffset;
        }
    }

    private void OnAudioTrackPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        OnTimelinePointerPressed(sender, e);
    }

    private void OnTimelinePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isDraggingPlayhead && sender is UIElement element)
        {
            var ptr = e.GetCurrentPoint(element);
            double clickSec = Math.Max(0, (ptr.Position.X - 40) / _timelineScale);
            SeekToTime(clickSec);

            // Sürüklerken kenara gelindiğinde kaydır (drag-to-scroll)
            if (TimelineScrollViewer != null)
            {
                var scrollPt = e.GetCurrentPoint(TimelineScrollViewer).Position;
                if (scrollPt.X > TimelineScrollViewer.ActualWidth - 40)
                {
                    TimelineScrollViewer.ChangeView(TimelineScrollViewer.HorizontalOffset + 20, null, null, true);
                }
                else if (scrollPt.X < 40)
                {
                    TimelineScrollViewer.ChangeView(Math.Max(0, TimelineScrollViewer.HorizontalOffset - 20), null, null, true);
                }
                else if (_isPanningTimeline)
                {
                    double diff = scrollPt.X - _panStartPoint.X;
                    if (Math.Abs(diff) > 5)
                    {
                        TimelineScrollViewer.ChangeView(_panStartOffset - diff, null, null, true);
                    }
                }
            }
        }
    }

    private void OnTimelinePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            element.ReleasePointerCapture(e.Pointer);
            _isDraggingPlayhead = false;
            _isPanningTimeline = false;
        }
    }

    public void SeekToTime(double time)
    {
        _currentTimeSeconds = Math.Clamp(time, 0, _totalDurationSeconds);
        var ts = TimeSpan.FromSeconds(_currentTimeSeconds);

        try
        {
            if (VideoPlayer?.MediaPlayer != null)
            {
                VideoPlayer.MediaPlayer.Position = ts;
            }
        }
        catch { }

        try
        {
            if (_micPlayer?.PlaybackSession != null && ts <= _micPlayer.PlaybackSession.NaturalDuration)
            {
                _micPlayer.Position = ts;
            }
        }
        catch { }

        try
        {
            if (_sysPlayer?.PlaybackSession != null && ts <= _sysPlayer.PlaybackSession.NaturalDuration)
            {
                _sysPlayer.Position = ts;
            }
        }
        catch { }

        ViewModel.CurrentTimeSec = _currentTimeSeconds;
        TbCurrentTime.Text = FormatTime(_currentTimeSeconds);
        TbTimelineCurrent.Text = FormatTime(_currentTimeSeconds);
        UpdatePlayhead();
        UpdateZoomSimulation();
    }

    private void OnTimelineWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var ptr = e.GetCurrentPoint(TimelineScrollViewer);
        int delta = ptr.Properties.MouseWheelDelta;

        var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        bool isCtrl = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

        if (isCtrl)
        {
            e.Handled = true;
            // Ctrl + Mouse Wheel: Timeline Zoom in/out (aralığı genişlet / daralt)
            double factor = delta > 0 ? 1.2 : 0.833;
            double newScale = Math.Clamp(_timelineScale * factor, 15, 600);
            _timelineScale = newScale;

            if (TimelineZoomSlider != null)
            {
                TimelineZoomSlider.Value = Math.Clamp(((_timelineScale - 20) / 280.0) * 100.0, 1, 100);
            }
            RenderTimeline();
        }
        else
        {
            // Normal wheel: Yatay saniye saniye kaydırma
            e.Handled = true;
            double targetOffset = TimelineScrollViewer.HorizontalOffset - (delta * 0.7);
            TimelineScrollViewer.ChangeView(targetOffset, null, null, true);
        }
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
                e.Handled = true;
                if (_selectedZoom != null)
                {
                    DeleteZoom(_selectedZoom);
                }
                else if (!string.IsNullOrEmpty(ViewModel.SelectedClipId))
                {
                    ViewModel.DeleteSelected();
                    RenderTimeline();
                }
                break;

            case Windows.System.VirtualKey.S when !isCtrl:
                e.Handled = true;
                OnSplitClicked(this, new RoutedEventArgs());
                break;
        }
    }

    private void OnSkipPrevClicked(object sender, RoutedEventArgs e) => SeekToTime(0);

    private void OnSkipNextClicked(object sender, RoutedEventArgs e) => SeekToTime(_totalDurationSeconds);

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

    private void OnMotionBlurChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (TbZoomBlurVal != null) TbZoomBlurVal.Text = $"{(int)e.NewValue}%";
        if (ViewModel != null) ViewModel.MotionBlurAmount = e.NewValue;
    }

    private void OnOpacityChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (TbOpacity != null) TbOpacity.Text = $"{(int)e.NewValue}%";
        if (ViewModel != null) ViewModel.BackgroundOpacity = e.NewValue;
    }

    private void OnMicVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (TbMicVolVal != null) TbMicVolVal.Text = $"{(int)e.NewValue}%";
        if (TbMicVolVal2 != null) TbMicVolVal2.Text = $"{(int)e.NewValue}%";
        if (ViewModel != null) ViewModel.MicVolume = e.NewValue;
        if (_micPlayer != null && ViewModel != null && !ViewModel.MicMuted) _micPlayer.Volume = e.NewValue / 100.0;
    }

    private void OnSysVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (TbSysVolVal != null) TbSysVolVal.Text = $"{(int)e.NewValue}%";
        if (ViewModel != null) ViewModel.SysVolume = e.NewValue;
        if (_sysPlayer != null) _sysPlayer.Volume = e.NewValue / 100.0;
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
                    ViewModel.CanvasBackground = "#0D0E15";
                    ViewModel.BackgroundStyle = "dark";
                    break;
                case "DarkGray":
                    ViewModel.CanvasBackground = "#1E1F27";
                    ViewModel.BackgroundStyle = "gradient-1";
                    break;
                case "Purple":
                    ViewModel.CanvasBackground = "#1E1B4B";
                    ViewModel.BackgroundStyle = "gradient-2";
                    break;
                case "Navy":
                    ViewModel.CanvasBackground = "#0C4A6E";
                    break;
                case "Emerald":
                    ViewModel.CanvasBackground = "#064E3B";
                    break;
                case "Crimson":
                    ViewModel.CanvasBackground = "#4C0519";
                    break;
                case "Grad1":
                    ViewModel.CanvasBackground = "linear-gradient(135deg, #C0C1FF, #A078FF)";
                    break;
                case "Grad2":
                    ViewModel.CanvasBackground = "linear-gradient(135deg, #3B82F6, #9333EA)";
                    break;
                case "Grad3":
                    ViewModel.CanvasBackground = "linear-gradient(135deg, #EC4899, #F43F5E)";
                    break;
                case "Grad4":
                    ViewModel.CanvasBackground = "linear-gradient(135deg, #10B981, #06B6D4)";
                    break;
                case "Grad5":
                    ViewModel.CanvasBackground = "linear-gradient(135deg, #F59E0B, #EF4444)";
                    break;
                case "Grad6":
                    ViewModel.CanvasBackground = "linear-gradient(135deg, #6366F1, #D946EF)";
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
            Scale = ViewModel.DefaultZoomScale > 0 ? ViewModel.DefaultZoomScale : 1.5,
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
        {
            DeleteZoom(_selectedZoom);
        }
        else if (!string.IsNullOrEmpty(ViewModel.SelectedClipId))
        {
            ViewModel.DeleteSelected();
            RenderTimeline();
        }
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

    private void OnVideoTrackMuteClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleMute("video");
        bool isMuted = ViewModel.VideoTrack.Muted;
        if (IconVideoMute != null)
        {
            IconVideoMute.Glyph = isMuted ? "\uE74F" : "\uE767";
            IconVideoMute.Foreground = new SolidColorBrush(isMuted ? Color.FromArgb(255, 239, 68, 68) : Color.FromArgb(144, 144, 160, 255));
        }
    }

    private void OnAudioTrackMuteClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleMute("mic");
        bool isMuted = ViewModel.MicTrack.Muted;
        if (_micPlayer != null)
        {
            _micPlayer.Volume = isMuted ? 0 : (ViewModel.MicVolume / 100.0);
        }
        if (IconAudioMute != null)
        {
            IconAudioMute.Glyph = isMuted ? "\uE74F" : "\uE767";
            IconAudioMute.Foreground = new SolidColorBrush(isMuted ? Color.FromArgb(255, 239, 68, 68) : Color.FromArgb(144, 144, 160, 255));
        }
    }

    private void OnMuteClicked(object sender, RoutedEventArgs e)
    {
        bool allMuted = ViewModel.VideoTrack.Muted && ViewModel.MicTrack.Muted;
        ViewModel.VideoTrack.Muted = !allMuted;
        ViewModel.MicTrack.Muted = !allMuted;
        ViewModel.MicMuted = !allMuted;

        if (_micPlayer != null) _micPlayer.Volume = !allMuted ? 0 : (ViewModel.MicVolume / 100.0);
        if (_sysPlayer != null) _sysPlayer.Volume = !allMuted ? 0 : (ViewModel.SysVolume / 100.0);

        if (MuteIcon != null)
        {
            MuteIcon.Glyph = !allMuted ? "\uE74F" : "\uE767";
        }
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
