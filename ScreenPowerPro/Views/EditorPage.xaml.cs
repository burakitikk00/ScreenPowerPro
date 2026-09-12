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
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using ScreenPowerPro.Helpers;
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
    private readonly List<ZoomEffect> _selectedZooms = new();
    private readonly HashSet<string> _selectedZoomIds = new();
    private bool _isUpdatingZoomInputs = false;
    private bool _isUpdatingZoomSlider = false;
    private enum TimelineTrackType { Video, Audio, Zoom, Click }
    private TimelineTrackType _activeTrack = TimelineTrackType.Video;
    private DispatcherTimer? _playbackTimer;
    private DispatcherTimer? _videoAudioAnimTimer;
    private int _videoAudioAnimTick = 0;

    // Bézier Curve Editor State
    private Point _bezierP1 = new Point(0.215, 0.61);
    private Point _bezierP2 = new Point(0.355, 1.0);
    private int _activeBezierHandle = 0; // 0 = none, 1 = P1, 2 = P2
    private bool _isUpdatingBezierUI = false;

    // Real-Time Zoom Preview State
    private DispatcherTimer? _zoomPreviewTimer;
    private readonly System.Diagnostics.Stopwatch _zoomPreviewStopwatch = new();
    private CompositeTransform? _previewTargetTransform;

    private Windows.Media.Playback.MediaPlayer? _micPlayer;
    private Windows.Media.Playback.MediaPlayer? _sysPlayer;

    // Prepared sources - set during OnNavigatedTo, attached to player after OnPageLoaded
    private MediaSource? _pendingVideoSource;
    private MediaSource? _pendingMicSource;
    private MediaSource? _pendingSysSource;
    private bool _videoAttachInProgress = false; // Guard against double DoAttachVideoToPlayer calls
    private bool _isDraggingPlayhead = false;

    // Timeline Pan / Drag / Cut Point state
    private bool _isPanningTimeline = false;
    private Point _panStartPoint;
    private double _panStartOffset;
    private readonly LocalizationService _loc;

    // Cursor overlay state & geometries
    private Grid? _cursorVisualRoot;
    private PathIcon? _cursorPathIcon;
    private FontIcon? _cursorFontIcon;
    private Border? _cursorGlowBorder;
    private Ellipse? _cursorDotEllipse;
    private CompositeTransform? _cursorTransform;
    private double _currentCursorX = 440;
    private double _currentCursorY = 240;
    private double _lastMouseMoveTimestamp = 0;
    private Point _lastKnownCursorPoint = new Point(440, 240);
    private readonly List<Border> _motionBlurGhosts = new();

    private double _naturalVideoWidth = 1920.0;
    private double _naturalVideoHeight = 1080.0;
    private double _lastTriggeredClickTimestamp = -1.0;
    private DateTime _lastPlaybackTick = DateTime.UtcNow;
    private double _savedMasterVolume = 100.0;


    private bool _isPageLoaded = false;

    public EditorPage()
    {
        AppLog.Info("[EditorPage] ctor başlatıldı.");
        try
        {
            ViewModel = App.Current.Services.GetRequiredService<EditorViewModel>();
            _loc = App.Current.Services.GetRequiredService<LocalizationService>();
            DataContext = ViewModel;

            InitializeComponent();
            Loaded += OnPageLoaded;
            AppLog.Success("[EditorPage] ctor ve InitializeComponent başarıyla tamamlandı.");
        }
        catch (Exception ex)
        {
            AppLog.Error("[EditorPage] ctor içinde KRİTİK HATA!", ex);
            throw;
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        string? projectDir = e.Parameter as string;
        AppLog.Event("NAV", $"[EditorPage] OnNavigatedTo çağrıldı. Parametre: '{projectDir}'");

        if (string.IsNullOrEmpty(projectDir))
        {
            var projectService = App.Current.Services.GetRequiredService<ProjectService>();
            var recents = projectService.GetRecentProjects();
            if (recents.Count > 0)
            {
                projectDir = recents[0].FolderPath;
                AppLog.Info($"[EditorPage] Boş parametre yerine son proje seçildi: '{projectDir}'");
            }
        }

        if (!string.IsNullOrEmpty(projectDir))
        {
            _projectDir = projectDir;
            try
            {
                AppLog.Info($"[EditorPage] ViewModel.LoadProject başlatılıyor: '{projectDir}'");
                ViewModel.LoadProject(projectDir);
                AppLog.Success($"[EditorPage] Proje başarıyla yüklendi: '{ViewModel.ProjectName}' (Süre: {ViewModel.TotalDurationSec:F1}s)");

                UpdateFromViewModel();
                RenderTimeline();

                AppLog.Info($"[EditorPage] LoadVideoAsync başlatılıyor: '{ViewModel.VideoPath}'");
                await LoadVideoAsync();
                AppLog.Success("[EditorPage] LoadVideoAsync başarıyla tamamlandı.");
            }
            catch (Exception ex)
            {
                AppLog.Error($"[EditorPage] Proje veya video yükleme hatası!", ex);
            }
        }
        else
        {
            AppLog.Warn("[EditorPage] Yüklenecek geçerli bir proje dizini bulunamadı!");
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _loc.LanguageChanged -= ApplyLocalization;
        PausePlayback();
        _playbackTimer?.Stop();
        _videoAudioAnimTimer?.Stop();
        _zoomPreviewTimer?.Stop();
        _zoomPreviewStopwatch.Stop();

        try
        {
            _micPlayer?.Dispose();
            _micPlayer = null;
            _sysPlayer?.Dispose();
            _sysPlayer = null;
        }
        catch { }
    }

    private void OnDismissGpuWarning(object sender, RoutedEventArgs e)
    {
        if (GpuWarningBanner != null)
        {
            GpuWarningBanner.Visibility = Visibility.Collapsed;
        }

        if (CbDontShowGpuWarning != null && CbDontShowGpuWarning.IsChecked == true)
        {
            try { Windows.Storage.ApplicationData.Current.LocalSettings.Values["HideGpuWarning"] = true; }
            catch { /* Unpackaged modda yoksay */ }
        }
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _isPageLoaded = true;
        AppLog.Info("[EditorPage] OnPageLoaded tetiklendi. Bileşenler bağlanıyor...");

        // GPU Uyarı kontrolü
        bool hideGpuWarning = false;
        try { hideGpuWarning = Windows.Storage.ApplicationData.Current.LocalSettings.Values["HideGpuWarning"] as bool? ?? false; }
        catch { /* Unpackaged modda ApplicationData.Current kullanılamaz */ }
        if (App.ShowIntegratedGpuWarning && GpuWarningBanner != null && !hideGpuWarning)
        {
            GpuWarningBanner.Visibility = Visibility.Visible;
        }

        _videoAudioAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _videoAudioAnimTimer.Tick += OnVideoAudioAnimTick;
        _videoAudioAnimTimer.Start();
        UpdateVideoAudioIconState();

        if (TimelineZoomSlider != null)
        {
            TimelineZoomSlider.ValueChanged += OnTimelineZoomChanged;
        }
        if (SliderZoomHoldDuration != null)
        {
            SliderZoomHoldDuration.ValueChanged += OnGlobalZoomDurationChanged;
        }
        if (SliderZoomMaxScale != null)
        {
            SliderZoomMaxScale.ValueChanged += OnGlobalZoomMaxScaleChanged;
        }
        if (SelectedZoomSlider != null)
        {
            SelectedZoomSlider.ValueChanged += OnSelectedZoomFactorChanged;
        }

        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(25)
        };
        _playbackTimer.Tick += OnPlaybackTimerTick;

        _loc.LanguageChanged += ApplyLocalization;
        ApplyLocalization();

        UpdateFromViewModel();
        RenderTimeline();
        SetSidebarTab("cursor");
        InitializeCursorOverlay();
        SyncCursorButtons();
        SyncMotionSettings();
        InitializeZoomPreview();
        UpdateBezierGraphVisuals();
        InitializeClickSoundUI();

        if (TimelineScrollViewer != null)
        {
            TimelineScrollViewer.PointerWheelChanged += OnTimelineWheelChanged;
        }

        AppLog.Success("[EditorPage] OnPageLoaded tamamlandı, sayfa hazır.");

        // Phase 2: Attach media to player now that visual tree + D3D surface are ready.
        // If LoadVideoAsync already ran and has a pending source, attach it now.
        if (_pendingVideoSource != null)
        {
            AppLog.Info("[EditorPage] OnPageLoaded: Bekleyen video kaynağı bulundu, player'a bağlanıyor.");
            AttachVideoToPlayer();
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
            else if (args.PropertyName == nameof(EditorViewModel.CursorStyle))
            {
                UpdateCursorVisual();
                SyncCursorButtons();
            }
            else if (args.PropertyName == nameof(EditorViewModel.CursorSize))
            {
                UpdateCursorScale(ViewModel.CursorSize > 0 ? ViewModel.CursorSize / 100.0 : 1.0);
            }
            else if (args.PropertyName == nameof(EditorViewModel.CursorVisible) ||
                     args.PropertyName == nameof(EditorViewModel.HideCursorWhenIdle))
            {
                UpdateCursorVisibility();
            }
            else if (args.PropertyName == nameof(EditorViewModel.ClickEffect) ||
                     args.PropertyName == nameof(EditorViewModel.CursorClickSound))
            {
                SyncCursorButtons();
                UpdateClickIconState();
            }
            else if (args.PropertyName == nameof(EditorViewModel.CursorClickSoundFile) ||
                     args.PropertyName == nameof(EditorViewModel.CursorClickVolume))
            {
                UpdateClickIconState();
            }
            else if (args.PropertyName == nameof(EditorViewModel.CursorMovementType))
            {
                SyncMotionSettings();
            }
        };

        ViewModel.NavigateToExport += OnNavigateToExport;

        // NOTE: If a video source was already prepared by OnNavigatedTo, it was already
        // attached above via AttachVideoToPlayer(). Do NOT call LoadVideoAsync() again here.
    }

    private void OnNavigateToExport(string projectDir)
    {
        PausePlayback();
        ShowExportSettingsDialog();
    }

    private void ApplyLocalization()
    {
        // Top Nav Bar
        if (TbBtnBackText != null) TbBtnBackText.Text = _loc["Editor_Nav_Back"];
        if (TbBtnSaveText != null) TbBtnSaveText.Text = _loc["Editor_Nav_Save"];
        if (TbBtnExportText != null) TbBtnExportText.Text = _loc["Editor_Nav_Export"];

        ApplyExportModalLocalization();

        // Sidebar tabs tooltips
        if (TabBtnCursor != null) ToolTipService.SetToolTip(TabBtnCursor, _loc["Editor_Sidebar_Cursor"]);
        if (TabBtnCanvas != null) ToolTipService.SetToolTip(TabBtnCanvas, _loc["Editor_Sidebar_Canvas"]);
        if (TabBtnAudio != null) ToolTipService.SetToolTip(TabBtnAudio, _loc["Editor_Sidebar_Audio"]);
        if (TabBtnKeys != null) ToolTipService.SetToolTip(TabBtnKeys, _loc["Editor_Sidebar_Keys"]);
        if (TabBtnMotion != null) ToolTipService.SetToolTip(TabBtnMotion, _loc["Editor_Sidebar_Motion"]);
        if (TabBtnCamera != null) ToolTipService.SetToolTip(TabBtnCamera, _loc["Editor_Sidebar_Camera"]);
        if (TabBtnVolume != null) ToolTipService.SetToolTip(TabBtnVolume, _loc["Editor_Sidebar_Volume"]);
        if (TabBtnWatermark != null) ToolTipService.SetToolTip(TabBtnWatermark, _loc["Editor_Sidebar_Watermark"]);
        if (TabBtnZoom != null) ToolTipService.SetToolTip(TabBtnZoom, _loc["Editor_Sidebar_Zoom"]);

        // TAB 1: CURSOR
        if (TbTitleCursor != null) TbTitleCursor.Text = _loc["Editor_Cursor_Title"];
        if (TbShowCursor != null) TbShowCursor.Text = _loc["Editor_Cursor_Show"];
        if (TbCursorSize != null) TbCursorSize.Text = _loc["Editor_Cursor_Size"];
        if (BtnResetCursorSize != null) BtnResetCursorSize.Content = _loc["Reset"];
        if (TbCursorStyle != null) TbCursorStyle.Text = _loc["Editor_Cursor_Style"];
        if (TbCursorStyleCount != null) TbCursorStyleCount.Text = _loc.CurrentLanguage == "en" ? "10 Shapes" : "10 Şekil";
        if (TbClickEffect != null) TbClickEffect.Text = _loc["Editor_Cursor_ClickEffect"];
        if (BtnClickNone != null) BtnClickNone.Content = _loc["Editor_Cursor_ClickEffect_None"];
        if (BtnClickDefault != null) BtnClickDefault.Content = _loc["Editor_Cursor_ClickEffect_Default"];
        if (BtnClickRipple != null) BtnClickRipple.Content = _loc["Editor_Cursor_ClickEffect_Ripple"];
        if (BtnClickRing != null) BtnClickRing.Content = _loc["Editor_Cursor_ClickEffect_Ring"];
        if (BtnClickDiffusion != null) BtnClickDiffusion.Content = _loc["Editor_Cursor_ClickEffect_Diffusion"];
        if (BtnClickSpotlight != null) BtnClickSpotlight.Content = _loc["Editor_Cursor_ClickEffect_Spotlight"];
        if (BtnClickSparkle != null) BtnClickSparkle.Content = _loc["Editor_Cursor_ClickEffect_Sparkle"];
        if (BtnClickFirework != null) BtnClickFirework.Content = _loc["Editor_Cursor_ClickEffect_Firework"];
        if (BtnClickChristmas != null) BtnClickChristmas.Content = _loc["Editor_Cursor_ClickEffect_Christmas"];
        if (TbCursorClickSound != null) TbCursorClickSound.Text = _loc["Editor_Cursor_ClickSound"];
        if (TbHideCursorIdle != null) TbHideCursorIdle.Text = _loc["Editor_Cursor_HideIdle"];
        if (TbRawCursorInfoTitle != null) TbRawCursorInfoTitle.Text = _loc["Editor_Cursor_RawInfoTitle"];
        if (TbRawCursorInfoDesc != null) TbRawCursorInfoDesc.Text = _loc["Editor_Cursor_RawInfoDesc"];

        // TAB 2: CANVAS
        if (TbTitleCanvas != null) TbTitleCanvas.Text = _loc["Editor_Canvas_Title"];
        if (TbAspectRatio != null) TbAspectRatio.Text = _loc["Editor_Canvas_AspectRatio"];
        if (BtnRatioOrig != null) BtnRatioOrig.Content = _loc["Editor_Canvas_Auto"];
        if (TbRoundnessLabel != null) TbRoundnessLabel.Text = _loc["Editor_Canvas_Roundness"];
        if (TbShadowLabel != null) TbShadowLabel.Text = _loc["Editor_Canvas_Shadow"];
        if (TbBgPresetLabel != null) TbBgPresetLabel.Text = _loc["Editor_Canvas_BgPreset"];

        // TAB 3: AUDIO
        if (TbTitleAudio != null) TbTitleAudio.Text = _loc["Editor_Audio_Title"];
        if (TbMuteMic != null) TbMuteMic.Text = _loc["Editor_Audio_MuteMic"];

        // TAB 4: KEYS
        if (TbTitleKeys != null) TbTitleKeys.Text = _loc["Editor_Keys_Title"];
        if (TbDisplayShortcutKeys != null) TbDisplayShortcutKeys.Text = _loc["Editor_Keys_DisplayKeys"];
        if (TbDisplaySingleKey != null) TbDisplaySingleKey.Text = _loc["Editor_Keys_DisplaySingleKey"];
        if (TbKeyStyle != null) TbKeyStyle.Text = _loc["Editor_Keys_KeyStyle"];
        if (BtnKeyStyleFilled != null) BtnKeyStyleFilled.Content = _loc["Editor_Keys_Filled"];
        if (BtnKeyStyleOutline != null) BtnKeyStyleOutline.Content = _loc["Editor_Keys_Outline"];
        if (BtnKeyStyleMinimal != null) BtnKeyStyleMinimal.Content = _loc["Editor_Keys_Minimal"];
        if (TbKeySize != null) TbKeySize.Text = _loc["Editor_Keys_KeySize"];
        if (TbScreenPos != null) TbScreenPos.Text = _loc["Editor_Keys_ScreenPos"];

        // TAB 5: MOTION
        if (TbTitleMotion != null) TbTitleMotion.Text = _loc["Editor_Motion_Title"];
        if (TbMotionBlur != null) TbMotionBlur.Text = _loc["Editor_Motion_MotionBlur"];
        if (TbZoomBlur != null) TbZoomBlur.Text = _loc["Editor_Motion_ZoomBlur"];
        if (TbScreenBlur != null) TbScreenBlur.Text = _loc["Editor_Motion_ScreenBlur"];
        if (TbCursorBlur != null) TbCursorBlur.Text = _loc["Editor_Motion_CursorBlur"];
        if (TbCursorMovement != null) TbCursorMovement.Text = _loc["Editor_Motion_CursorMovement"];
        if (BtnSpeedSlow != null) BtnSpeedSlow.Content = _loc["Editor_Motion_Slow"];
        if (BtnSpeedMedium != null) BtnSpeedMedium.Content = _loc["Editor_Motion_Medium"];
        if (BtnSpeedFast != null) BtnSpeedFast.Content = _loc["Editor_Motion_Fast"];

        // TAB 6: CAMERA
        if (TbTitleCamera != null) TbTitleCamera.Text = _loc["Editor_Camera_Title"];
        if (TbCameraLayer != null) TbCameraLayer.Text = _loc["Editor_Camera_Layer"];
        if (TbCameraShape != null) TbCameraShape.Text = _loc["Editor_Camera_Shape"];
        if (BtnCamCircle != null) BtnCamCircle.Content = _loc["Editor_Camera_Circle"];
        if (BtnCamSquare != null) BtnCamSquare.Content = _loc["Editor_Camera_Square"];
        if (BtnCamRounded != null) BtnCamRounded.Content = _loc["Editor_Camera_Rounded"];

        // TAB 7: VOLUME MIXER
        if (TbTitleVolume != null) TbTitleVolume.Text = _loc["Editor_Volume_Title"];
        if (TbMicLevel2 != null) TbMicLevel2.Text = _loc["Editor_Volume_MicLevel"];
        if (TbSysLevel != null) TbSysLevel.Text = _loc["Editor_Volume_SysLevel"];

        // TAB 8: WATERMARK
        if (TbTitleWatermark != null) TbTitleWatermark.Text = _loc["Editor_Watermark_Title"];
        if (TbWatermarkShow != null) TbWatermarkShow.Text = _loc["Editor_Watermark_Show"];
        if (TbWatermarkText != null) TbWatermarkText.PlaceholderText = _loc["Editor_Watermark_Placeholder"];

        // TAB 9: ZOOM PROPS
        if (TbTitleZoomProps != null) TbTitleZoomProps.Text = _loc["Editor_Zoom_Title"];
        if (TbZoomFactor != null) TbZoomFactor.Text = _loc["Editor_Zoom_Factor"];
        if (TbZoomStart != null) TbZoomStart.Text = _loc["Editor_Zoom_Start"];
        if (TbZoomEnd != null) TbZoomEnd.Text = _loc["Editor_Zoom_End"];

        // Video canvas
        if (TbNoVideoMessage != null) TbNoVideoMessage.Text = _loc["Editor_Canvas_NoVideoMessage"];

        // Timeline Toolbar
        if (BtnUndo != null) ToolTipService.SetToolTip(BtnUndo, _loc["Editor_Timeline_Undo"]);
        if (BtnRedo != null) ToolTipService.SetToolTip(BtnRedo, _loc["Editor_Timeline_Redo"]);
        if (BtnSplit != null) ToolTipService.SetToolTip(BtnSplit, _loc["Editor_Timeline_Split"]);
        if (BtnDeleteZoom != null) ToolTipService.SetToolTip(BtnDeleteZoom, _loc["Editor_Timeline_DeleteClip"]);
        if (BtnMuteAudio != null) ToolTipService.SetToolTip(BtnMuteAudio, _loc["Editor_Timeline_MuteAll"]);
        if (IconZoomOut != null) ToolTipService.SetToolTip(IconZoomOut, _loc["Editor_Timeline_ZoomOut"]);
        if (IconZoomIn != null) ToolTipService.SetToolTip(IconZoomIn, _loc["Editor_Timeline_ZoomIn"]);

        // Timeline Track Headers
        if (TbTrackVideo != null) TbTrackVideo.Text = _loc["Editor_Timeline_TrackVideo"];
        if (TbTrackVideoSub != null) TbTrackVideoSub.Text = _loc["Editor_Timeline_TrackVideoSub"];
        if (BtnVideoMute != null) ToolTipService.SetToolTip(BtnVideoMute, _loc["Editor_Timeline_MuteVideoTrack"]);
        if (TbTrackAudio != null) TbTrackAudio.Text = _loc["Editor_Timeline_TrackAudio"];
        if (TbTrackAudioSub != null) TbTrackAudioSub.Text = _loc["Editor_Timeline_TrackAudioSub"];
        if (BtnAudioMute != null) ToolTipService.SetToolTip(BtnAudioMute, _loc["Editor_Timeline_MuteAudioTrack"]);
        if (TbTrackZoom != null) TbTrackZoom.Text = _loc["Editor_Timeline_TrackZoom"];
    }

    private bool _isLoadingVideo = false;

    public async Task LoadVideoAsync()
    {
        if (_isLoadingVideo) return;
        _isLoadingVideo = true;

        try
        {
            await Task.Yield();
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
                NoVideoMessage.Visibility = Visibility.Collapsed;

            // Phase 1: Create MediaSources (can happen before visual tree is ready)
            _pendingVideoSource = await CreateMediaSourceSafeAsync(videoPath);
            if (_pendingVideoSource == null)
            {
                AppLog.Error($"[EditorPage] MediaSource oluşturulamadı: {videoPath}");
                return;
            }

            _pendingVideoSource.OpenOperationCompleted += (s, e) =>
            {
                if (e.Error != null)
                    DispatcherQueue.TryEnqueue(() => AppLog.Error($"[EditorPage] MediaSource OpenOperation hatası: {e.Error.ExtendedError?.Message} (0x{e.Error.ExtendedError?.HResult:X8})"));
                else
                    DispatcherQueue.TryEnqueue(() => AppLog.Success("[EditorPage] MediaSource OpenOperationCompleted başarıyla tetiklendi."));
            };

            // Audio sources (these don't need visual tree)
            if (ViewModel != null && !string.IsNullOrEmpty(ViewModel.MicAudioPath) && File.Exists(ViewModel.MicAudioPath))
                _pendingMicSource = await CreateMediaSourceSafeAsync(ViewModel.MicAudioPath);

            if (ViewModel != null && !string.IsNullOrEmpty(ViewModel.SystemAudioPath) && File.Exists(ViewModel.SystemAudioPath))
                _pendingSysSource = await CreateMediaSourceSafeAsync(ViewModel.SystemAudioPath);

            if (ViewModel != null && Math.Abs(ViewModel.VideoSpeed - 1.0) > 0.01)
                ViewModel.VideoSpeed = 1.0;

            // Phase 2: Attach to player - only if visual tree is already ready
            // If OnPageLoaded hasn't fired yet, it will call AttachVideoToPlayer itself.
            if (_isPageLoaded)
            {
                AppLog.Info("[EditorPage] Sayfa zaten hazır, player'a direkt bağlanılıyor.");
                AttachVideoToPlayer();
            }
            else
            {
                AppLog.Info("[EditorPage] Sayfa henüz hazır değil, OnPageLoaded bekleniyor.");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"[EditorPage] LoadVideoAsync hatası: {ex.Message}", ex);
        }
        finally
        {
            _isLoadingVideo = false;
        }
    }

    /// <summary>
    /// Phase 2: Attaches the prepared MediaSource to the MediaPlayer.
    /// Must be called AFTER OnPageLoaded so the MediaPlayerElement is in the
    /// visual tree and has a valid D3D11 render surface.
    /// We add an extra 150ms delay to let the DXVA2 hardware decoder context
    /// fully initialise before setting the source.
    /// </summary>
    private void AttachVideoToPlayer()
    {
        if (_pendingVideoSource == null)
        {
            AppLog.Warn("[EditorPage] AttachVideoToPlayer: pendingVideoSource null, atlanıyor.");
            return;
        }

        AppLog.Info("[EditorPage] AttachVideoToPlayer: 150ms gecikme başlatılıyor (DXVA2 init bekleniyor)...");

        // Give the D3D11/DXVA2 hardware decoder context a moment to fully initialise
        // after the visual tree is ready. Without this delay the MediaPlayer may fail
        // with MF_E_UNSUPPORTED_FORMAT (0xC00D36FA) even though the file is valid.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            DoAttachVideoToPlayer();
        };
        timer.Start();
    }

    private void DoAttachVideoToPlayer()
    {
        // Guard: prevent double execution (can be triggered from both OnPageLoaded and LoadVideoAsync)
        if (_videoAttachInProgress)
        {
            AppLog.Warn("[EditorPage] DoAttachVideoToPlayer: zaten çalışıyor, tekrar çağrı engellendi.");
            return;
        }
        _videoAttachInProgress = true;

        // Capture and clear the pending source so no other call can use it
        var videoSource = _pendingVideoSource;
        var micSource = _pendingMicSource;
        var sysSource = _pendingSysSource;
        _pendingVideoSource = null;
        _pendingMicSource = null;
        _pendingSysSource = null;

        if (videoSource == null)
        {
            AppLog.Warn("[EditorPage] DoAttachVideoToPlayer: videoSource null, atlanıyor.");
            _videoAttachInProgress = false;
            return;
        }

        AppLog.Info("[EditorPage] DoAttachVideoToPlayer başlatılıyor...");

        try
        {
            var player = VideoPlayer.MediaPlayer;
            if (player != null)
            {
                player.AutoPlay = false;
                if (player.PlaybackSession != null)
                    player.PlaybackSession.PlaybackRate = 1.0;

                player.MediaEnded -= OnMediaPlayerEnded;
                player.MediaEnded += OnMediaPlayerEnded;
                player.MediaOpened -= OnMediaPlayerOpened;
                player.MediaOpened += OnMediaPlayerOpened;
                player.MediaFailed -= OnMediaPlayerFailed;
                player.MediaFailed += OnMediaPlayerFailed;
            }

            AppLog.Info("[EditorPage] VideoPlayer.Source set ediliyor (DXVA2 hazır)...");
            VideoPlayer.Source = videoSource;

            if (player?.PlaybackSession?.NaturalDuration > TimeSpan.Zero)
                OnMediaPlayerOpened(player, null!);
        }
        catch (Exception ex)
        {
            AppLog.Error($"[EditorPage] DoAttachVideoToPlayer video player hatası: {ex.Message}", ex);
        }
        finally
        {
            _videoAttachInProgress = false;
        }

        // Audio players
        try
        {
            _micPlayer?.Dispose();
            _micPlayer = null;
            if (micSource != null)
            {
                _micPlayer = new Windows.Media.Playback.MediaPlayer
                {
                    Source = micSource,
                    AutoPlay = false,
                    Volume = ViewModel?.MicMuted == true ? 0 : ((ViewModel?.MicVolume ?? 100) / 100.0)
                };
                AppLog.Info("[EditorPage] Mikrofon player bağlandı.");
            }
        }
        catch (Exception ex) { AppLog.Warn($"[EditorPage] Mic audio player hatası: {ex.Message}"); }

        try
        {
            _sysPlayer?.Dispose();
            _sysPlayer = null;
            if (sysSource != null)
            {
                _sysPlayer = new Windows.Media.Playback.MediaPlayer
                {
                    Source = sysSource,
                    AutoPlay = false,
                    Volume = (ViewModel?.SysVolume ?? 100) / 100.0
                };
                AppLog.Info("[EditorPage] Sistem sesi player bağlandı.");
            }
        }
        catch (Exception ex) { AppLog.Warn($"[EditorPage] Sys audio player hatası: {ex.Message}"); }

        AppLog.Success("[EditorPage] DoAttachVideoToPlayer tamamlandı.");
    }

    private void OnMediaPlayerOpened(Windows.Media.Playback.MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            AppLog.Success("[EditorPage] MediaPlayer başarıyla yüklendi (MediaOpened).");
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
                    AutoZoomToFit();
                    RenderTimeline();
                }

                uint w = session.NaturalVideoWidth;
                uint h = session.NaturalVideoHeight;
                if (w > 0 && h > 0)
                {
                    _naturalVideoWidth = w;
                    _naturalVideoHeight = h;
                    if (ViewModel != null)
                    {
                        ViewModel.VideoWidth = (int)w;
                        ViewModel.VideoHeight = (int)h;
                    }
                    if (TbResolution != null)
                    {
                        TbResolution.Text = $"{w}x{h} • 60fps";
                    }
                    UpdateVideoContainerBounds();
                }

                // İlk frame'i göster (AutoPlay=false ile kara ekran olmaması için)
                try { sender.PlaybackSession.Position = TimeSpan.Zero; } catch { }
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

    private static async Task<MediaSource> CreateMediaSourceSafeAsync(string filePath)
    {
        AppLog.Info($"[EditorPage] CreateMediaSourceSafeAsync çağrıldı. Dosya: {filePath}");

        if (!System.IO.File.Exists(filePath))
        {
            AppLog.Warn($"[EditorPage] Uyarı: {filePath} dosyası bulunamadı.");
            return null!;
        }

        var fileInfo = new System.IO.FileInfo(filePath);
        AppLog.Info($"[EditorPage] Dosya boyutu: {fileInfo.Length} byte, Uzantı: {fileInfo.Extension}");

        string ext = fileInfo.Extension.ToLowerInvariant();

        // Primary: CreateFromStream with explicit MIME type.
        // This forces Windows MF to use the correct decoder without URI-based
        // format guessing, which can fail with MF_E_UNSUPPORTED_FORMAT (0xC00D36FA)
        // for H.264 in unpackaged WinUI3 apps.
        try
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
            var stream = await storageFile.OpenReadAsync();
            string mimeType = ext switch
            {
                ".mp4"  => "video/mp4",
                ".mov"  => "video/quicktime",
                ".mkv"  => "video/x-matroska",
                ".avi"  => "video/avi",
                ".wmv"  => "video/x-ms-wmv",
                ".wav"  => "audio/wav",
                ".mp3"  => "audio/mpeg",
                ".aac"  => "audio/aac",
                ".m4a"  => "audio/mp4",
                _       => "application/octet-stream"
            };
            var source = MediaSource.CreateFromStream(stream, mimeType);
            AppLog.Info($"[EditorPage] MediaSource.CreateFromStream başarılı (MIME: {mimeType}).");
            return source;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"[EditorPage] CreateFromStream başarısız, CreateFromUri deneniyor: {ex.Message}");
        }

        // Fallback: CreateFromUri
        try
        {
            var fileUri = new Uri(filePath);
            var source = MediaSource.CreateFromUri(fileUri);
            AppLog.Info($"[EditorPage] MediaSource.CreateFromUri başarılı.");
            return source;
        }
        catch (Exception ex)
        {
            AppLog.Error($"[EditorPage] Her iki yöntem de başarısız oldu: {ex.Message} \nStack Trace: {ex.StackTrace}");
            return null!;
        }
    }

    private void OnMediaPlayerFailed(Windows.Media.Playback.MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            string currentVideoPath = ViewModel?.VideoPath ?? "Bilinmiyor";
            string currentAudioPath = ViewModel?.SystemAudioPath ?? "Bilinmiyor";
            string currentMicPath = ViewModel?.MicAudioPath ?? "Bilinmiyor";

            AppLog.Error($"[EditorPage] Medya oynatıcı hatası! \n" +
                         $"Error: {args.Error}\n" +
                         $"HResult: 0x{args.ExtendedErrorCode?.HResult:X8}\n" +
                         $"Message: '{args.ErrorMessage}'\n" +
                         $"Exception: '{args.ExtendedErrorCode?.Message}'\n" +
                         $"Active VideoPath: {currentVideoPath}\n" +
                         $"Active SystemAudioPath: {currentAudioPath}\n" +
                         $"Active MicAudioPath: {currentMicPath}");
        });
    }

    private void AutoZoomToFit()
    {
        if (TimelineScrollViewer == null || _totalDurationSeconds <= 0) return;
        
        double w = TimelineScrollViewer.ActualWidth;
        if (w == 0)
        {
            // Eğer arayüz tam yüklenmediyse bekle
            Microsoft.UI.Xaml.SizeChangedEventHandler sizeChangedHandler = null;
            sizeChangedHandler = (s, e) =>
            {
                TimelineScrollViewer.SizeChanged -= sizeChangedHandler;
                AutoZoomToFit();
            };
            TimelineScrollViewer.SizeChanged += sizeChangedHandler;
            return;
        }

        // 40px margin sağdan soldan toplam pay
        double desiredScale = (w - 40) / _totalDurationSeconds; 
        desiredScale = Math.Clamp(desiredScale, 20.0, 300.0);
        
        _timelineScale = desiredScale;
        
        if (TimelineZoomSlider != null)
        {
            _isUpdatingZoomSlider = true;
            double sliderVal = Math.Clamp(((_timelineScale - 20) / 280.0) * 100.0, 1, 100);
            TimelineZoomSlider.Value = sliderVal;
            if (ViewModel != null) ViewModel.TimelineZoom = sliderVal;
            _isUpdatingZoomSlider = false;
        }
    }

    private void UpdateFromViewModel()
    {
        if (ViewModel == null) return;
        _totalDurationSeconds = ViewModel.TotalDurationSec > 0 ? ViewModel.TotalDurationSec : 10;
        if (TbProjectName != null)
        {
            TbProjectName.Text = string.IsNullOrEmpty(ViewModel.ProjectName)
                ? "Untitled Recording.mp4"
                : ViewModel.ProjectName;
        }

        if (TbTotalTime != null) TbTotalTime.Text = FormatTime(_totalDurationSeconds);
        if (TbTimelineTotal != null) TbTimelineTotal.Text = FormatTime(_totalDurationSeconds);
        if (TbCurrentTime != null) TbCurrentTime.Text = FormatTime(0);
        if (TbTimelineCurrent != null) TbTimelineCurrent.Text = FormatTime(0);

        double clipWidth = _totalDurationSeconds * _timelineScale;
        if (VideoClipBlock != null)
        {
            VideoClipBlock.Width = Math.Max(clipWidth, 200);
        }
        UpdateVideoAudioIconState();
        UpdateAudioIconState();
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
        if (PanelTabMotion != null)
        {
            PanelTabMotion.Visibility = (tabTag == "motion") ? Visibility.Visible : Visibility.Collapsed;
            if (tabTag == "motion")
            {
                SyncMotionSettings();
            }
        }
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
            if (btn.Content is IconElement icon)
            {
                icon.Foreground = new SolidColorBrush(Color.FromArgb(255, 192, 193, 255));
            }
        }
        else
        {
            btn.Background = new SolidColorBrush(Colors.Transparent);
            if (btn.Content is IconElement icon)
            {
                icon.Foreground = new SolidColorBrush(Color.FromArgb(144, 144, 160, 255));
            }
        }
    }

    // --- CURSOR TAB HANDLERS ---

    private void OnResetCursorSizeClicked(object sender, RoutedEventArgs e)
    {
        if (CursorSizeSlider != null) CursorSizeSlider.Value = 1.0;
        if (ViewModel != null) ViewModel.CursorSize = 100;
        if (TbCursorSizeVal != null) TbCursorSizeVal.Text = "1.0x";
        UpdateCursorScale(1.0);
    }

    private void OnCursorSizeSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_isPageLoaded) return;
        if (TbCursorSizeVal != null) TbCursorSizeVal.Text = $"{e.NewValue:F1}x";
        if (ViewModel != null)
        {
            ViewModel.CursorSize = e.NewValue * 100.0;
            UpdateCursorScale(e.NewValue);
        }
    }

    private void OnCursorStyleSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        ViewModel.CursorStyle = tag;

        var cursorButtons = new[]
        {
            BtnCursorDefault, BtnCursorBlack, BtnCursorHand, BtnCursorCrosshair, BtnCursorDot,
            BtnCursorIBeam, BtnCursorSpotlight, BtnCursorHighlightYellow, BtnCursorCyan, BtnCursorPurple
        };

        HighlightButtonChoice(cursorButtons, btn);
        UpdateCursorVisual();
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

        PlayClickEffectAt(_currentCursorX, _currentCursorY, tag);
    }

    // --- CANVAS TAB HANDLERS ---

    private void OnCanvasRatioSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        ViewModel.AspectRatio = tag;

        HighlightButtonChoice(new[] { BtnRatioOrig, BtnRatio169, BtnRatio11, BtnRatio43, BtnRatio916 }, btn);
        UpdateVideoContainerBounds();
    }



    private void OnRoundnessValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_isPageLoaded) return;
        if (TbRoundnessVal != null) TbRoundnessVal.Text = $"{(int)e.NewValue}";
        UpdateVideoWindowRoundness();
    }

    private void OnShadowValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_isPageLoaded) return;
        if (TbShadowVal != null) TbShadowVal.Text = $"{(int)e.NewValue}%";
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
        if (!_isPageLoaded || ViewModel == null) return;
        if (TimeRuler == null || VideoTrack == null || AudioTrack == null || ZoomTrack == null) return;
        
        ViewModel.RecalculateTotalDuration();
        _totalDurationSeconds = ViewModel.TotalDurationSec;

        double totalWidth = Math.Max(_totalDurationSeconds * _timelineScale, 800);
        TimelineContentGrid.MinWidth = totalWidth + 120;

        RenderTimeRuler(totalWidth);
        RenderVideoClips(totalWidth);
        RenderAudioTrack(totalWidth);
        RenderZoomPills();
        RenderClickTrack(totalWidth);
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
            double x = sec * _timelineScale;

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
                Canvas.SetLeft(label, x);
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
            double x = i * step * _timelineScale;
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
            double x = i * step * _timelineScale;
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

    private List<double> GetTimelineSnapPoints(object? excludeItem = null)
    {
        var snaps = new HashSet<double>
        {
            0.0,
            _currentTimeSeconds
        };
        if (_totalDurationSeconds > 0) snaps.Add(_totalDurationSeconds);

        if (ViewModel?.VideoTrack?.Clips != null)
        {
            foreach (var c in ViewModel.VideoTrack.Clips)
            {
                if (c == excludeItem) continue;
                snaps.Add(c.TrackOffset);
                snaps.Add(c.TrackOffset + Math.Max(0, c.SourceEnd - c.SourceStart));
            }
        }

        if (ViewModel?.MicTrack?.Clips != null)
        {
            foreach (var c in ViewModel.MicTrack.Clips)
            {
                if (c == excludeItem) continue;
                snaps.Add(c.TrackOffset);
                snaps.Add(c.TrackOffset + Math.Max(0, c.SourceEnd - c.SourceStart));
            }
        }

        if (ViewModel?.ZoomEffects != null)
        {
            foreach (var z in ViewModel.ZoomEffects)
            {
                if (z == excludeItem) continue;
                snaps.Add(z.StartTime);
                snaps.Add(z.StartTime + z.Duration);
            }
        }

        return snaps.ToList();
    }

    private void UpdateClipSelectionVisuals()
    {
        UpdateTrackClipSelectionVisuals(VideoTrack, isAudio: false);
        UpdateTrackClipSelectionVisuals(AudioTrack, isAudio: true);
    }

    private void UpdateTrackClipSelectionVisuals(Canvas? trackCanvas, bool isAudio)
    {
        if (trackCanvas == null || ViewModel == null) return;
        foreach (var child in trackCanvas.Children)
        {
            if (child is Border clipBlock && clipBlock.Tag is ClipSegment clip)
            {
                bool isSelected = ViewModel.IsClipSelected(clip.Id);
                clipBlock.Background = isAudio
                    ? (isSelected ? new SolidColorBrush(Color.FromArgb(95, 34, 197, 94)) : new SolidColorBrush(Color.FromArgb(45, 34, 197, 94)))
                    : (isSelected ? new SolidColorBrush(Color.FromArgb(115, 128, 131, 255)) : new SolidColorBrush(Color.FromArgb(55, 128, 131, 255)));
                clipBlock.BorderBrush = isSelected
                    ? new SolidColorBrush(isAudio ? Color.FromArgb(255, 74, 222, 128) : Color.FromArgb(255, 216, 218, 255))
                    : new SolidColorBrush(isAudio ? Color.FromArgb(120, 34, 197, 94) : Color.FromArgb(120, 192, 193, 255));
                clipBlock.BorderThickness = isSelected ? new Thickness(2.5) : new Thickness(1);
            }
        }
    }

    private void AddClipVisualBlock(ClipSegment clip, int clipIndex, bool isAudioTrack)
    {
        Canvas targetCanvas = isAudioTrack ? AudioTrack : VideoTrack;
        double duration = Math.Max(0.1, clip.SourceEnd - clip.SourceStart);
        double startX = TimelineMathService.TimeToPixel(clip.TrackOffset, _timelineScale);
        double width = TimelineMathService.DurationToWidth(duration, _timelineScale, 24);
        double height = isAudioTrack ? 46 : 56;
        double top = isAudioTrack ? 5 : 8;

        bool isSelected = ViewModel != null && ViewModel.IsClipSelected(clip.Id);

        var clipBlock = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(6),
            Opacity = clip.IsLocked ? 0.75 : 1.0,
            Background = isAudioTrack
                ? (isSelected ? new SolidColorBrush(Color.FromArgb(95, 34, 197, 94)) : new SolidColorBrush(Color.FromArgb(45, 34, 197, 94)))
                : (isSelected ? new SolidColorBrush(Color.FromArgb(115, 128, 131, 255)) : new SolidColorBrush(Color.FromArgb(55, 128, 131, 255))),
            BorderBrush = isSelected
                ? new SolidColorBrush(isAudioTrack ? Color.FromArgb(255, 74, 222, 128) : Color.FromArgb(255, 216, 218, 255))
                : new SolidColorBrush(isAudioTrack ? Color.FromArgb(120, 34, 197, 94) : Color.FromArgb(120, 192, 193, 255)),
            BorderThickness = isSelected ? new Thickness(2.5) : new Thickness(1),
            Tag = clip,
            IsHitTestVisible = true
        };

        var contentGrid = new Grid();

        // 1. Ses dalga formu grafiği (Eğer ses parçasıysa)
        if (isAudioTrack && ViewModel?.WaveformPeaks != null && ViewModel.WaveformPeaks.Length > 0)
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
                    Fill = new SolidColorBrush(isSelected ? Color.FromArgb(240, 220, 255, 220) : Color.FromArgb(160, 74, 222, 128))
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
                ? new SolidColorBrush(Color.FromArgb(255, 230, 255, 235))
                : new SolidColorBrush(Color.FromArgb(255, 220, 222, 255)),
            Margin = new Thickness(4, 4, 14, 0),
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false
        };
        contentGrid.Children.Add(label);

        // 3. Sol ve Sağ Kırpma (Trim Edge Handles) Tutamaçları
        var leftHandle = new Border
        {
            Width = 10,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = isSelected
                ? new SolidColorBrush(Color.FromArgb(90, 255, 255, 255))
                : new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
            CornerRadius = new CornerRadius(5, 0, 0, 5),
            IsHitTestVisible = false,
            Child = new Border
            {
                Width = 2,
                Height = 16,
                Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(1)
            }
        };

        var rightHandle = new Border
        {
            Width = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = isSelected
                ? new SolidColorBrush(Color.FromArgb(90, 255, 255, 255))
                : new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
            CornerRadius = new CornerRadius(0, 5, 5, 0),
            IsHitTestVisible = false,
            Child = new Border
            {
                Width = 2,
                Height = 16,
                Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(1)
            }
        };

        contentGrid.Children.Add(leftHandle);
        contentGrid.Children.Add(rightHandle);

        // 4. Kilit Butonu (Sol Alt Köşe) - Basılınca klibin hareketi kilitlenir
        var lockIcon = new FontIcon
        {
            Glyph = clip.IsLocked ? "\uE72E" : "\uE785",
            FontSize = 9,
            Foreground = clip.IsLocked
                ? new SolidColorBrush(Color.FromArgb(255, 239, 68, 68))
                : new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var lockButton = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(4),
            Background = clip.IsLocked
                ? new SolidColorBrush(Color.FromArgb(140, 239, 68, 68))
                : new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(14, 0, 0, 4),
            IsHitTestVisible = true,
            Child = lockIcon
        };
        ToolTipService.SetToolTip(lockButton, clip.IsLocked ? "Kilitli (Hareket ettirilemez)" : "Kilitle");

        lockButton.PointerPressed += (s, e) =>
        {
            e.Handled = true;
            clip.IsLocked = !clip.IsLocked;
            lockIcon.Glyph = clip.IsLocked ? "\uE72E" : "\uE785";
            lockIcon.Foreground = clip.IsLocked
                ? new SolidColorBrush(Color.FromArgb(255, 239, 68, 68))
                : new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));
            lockButton.Background = clip.IsLocked
                ? new SolidColorBrush(Color.FromArgb(140, 239, 68, 68))
                : new SolidColorBrush(Color.FromArgb(50, 0, 0, 0));
            ToolTipService.SetToolTip(lockButton, clip.IsLocked ? "Kilitli (Hareket ettirilemez)" : "Kilitle");
            clipBlock.Opacity = clip.IsLocked ? 0.75 : 1.0;
            ViewModel?.SaveProject();
        };

        contentGrid.Children.Add(lockButton);
        clipBlock.Child = contentGrid;

        // ETKİLEŞİM: Sürükle-Bırak (Drag & Drop), Manyetik Yapışma (Snapping), Kenar Kırpma (Trimming)
        bool isMoving = false;
        bool isTrimmingLeft = false;
        bool isTrimmingRight = false;
        bool hasActuallyMoved = false;
        Point startPt = default;
        double origOffset = 0;
        double origStart = 0;
        double origEnd = 0;
        List<ClipSegment>? movingClips = null;
        Dictionary<ClipSegment, double>? movingClipOffsets = null;
        Dictionary<ClipSegment, Border>? movingClipBorders = null;

        clipBlock.PointerPressed += (s, e) =>
        {
            e.Handled = true;
            clipBlock.CapturePointer(e.Pointer);

            var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            bool isCtrl = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

            if (ViewModel != null)
            {
                if (isCtrl)
                {
                    ViewModel.SelectClip(clip.Id, isMultiSelect: true);
                }
                else
                {
                    if (!ViewModel.IsClipSelected(clip.Id))
                    {
                        ViewModel.SelectClip(clip.Id, isMultiSelect: false);
                    }
                }
                ViewModel.SelectedTrackType = isAudioTrack ? "mic" : "video";
                _activeTrack = isAudioTrack ? TimelineTrackType.Audio : TimelineTrackType.Video;
            }

            _selectedZoom = null;
            UpdateClipSelectionVisuals();
            RenderZoomPills();

            var ptr = e.GetCurrentPoint(targetCanvas);
            startPt = ptr.Position;
            origOffset = clip.TrackOffset;
            origStart = clip.SourceStart;
            origEnd = clip.SourceEnd;
            hasActuallyMoved = false;

            // Kilitli klibi sürükleme veya kırpma
            if (clip.IsLocked)
            {
                isMoving = false;
                isTrimmingLeft = false;
                isTrimmingRight = false;
                return;
            }

            var localPt = e.GetCurrentPoint(clipBlock).Position;

            if (localPt.X <= 12)
            {
                isTrimmingLeft = true;
            }
            else if (localPt.X >= clipBlock.ActualWidth - 12 || (clipBlock.Width > 0 && localPt.X >= clipBlock.Width - 12))
            {
                isTrimmingRight = true;
            }
            else
            {
                isMoving = true;
                if (ViewModel != null)
                {
                    var trackClips = isAudioTrack ? ViewModel.MicTrack.Clips : ViewModel.VideoTrack.Clips;
                    movingClips = trackClips.Where(c => ViewModel.IsClipSelected(c.Id) && !c.IsLocked).ToList();
                    if (!movingClips.Contains(clip))
                    {
                        movingClips.Add(clip);
                    }
                    movingClipOffsets = movingClips.ToDictionary(c => c, c => c.TrackOffset);
                    movingClipBorders = targetCanvas.Children.OfType<Border>()
                        .Where(b => b.Tag is ClipSegment cs && movingClips.Contains(cs))
                        .ToDictionary(b => (ClipSegment)b.Tag, b => b);
                }
            }
        };

        clipBlock.PointerMoved += (s, e) =>
        {
            if (isMoving && movingClips != null && movingClipOffsets != null)
            {
                var ptr = e.GetCurrentPoint(targetCanvas);
                double rawDx = ptr.Position.X - startPt.X;
                if (Math.Abs(rawDx) > 3) hasActuallyMoved = true;

                double dt = rawDx / _timelineScale;
                double minOffset = movingClipOffsets.Values.Min();
                if (minOffset + dt < 0)
                {
                    dt = -minOffset;
                }

                // Manyetik yapışma (snapping)
                double candidatePrimaryOffset = Math.Max(0, origOffset + dt);
                double curDur = clip.SourceEnd - clip.SourceStart;
                var snapPoints = GetTimelineSnapPoints(clip);
                var (snapL, snapTimeL) = TimelineMathService.FindSnapTime(candidatePrimaryOffset, snapPoints, _timelineScale, 15.0);
                if (snapL)
                {
                    dt = snapTimeL - origOffset;
                }
                else
                {
                    var (snapR, snapTimeR) = TimelineMathService.FindSnapTime(candidatePrimaryOffset + curDur, snapPoints, _timelineScale, 15.0);
                    if (snapR)
                    {
                        dt = (snapTimeR - curDur) - origOffset;
                    }
                }

                // Sıkı çarpışma engelleme (Collision detection with stationary clips)
                if (ViewModel != null)
                {
                    var trackClips = isAudioTrack ? ViewModel.MicTrack.Clips : ViewModel.VideoTrack.Clips;
                    var nonMovingClips = trackClips.Where(c => !movingClips.Contains(c)).ToList();
                    double minCandidateDt = -minOffset;
                    double maxCandidateDt = double.MaxValue;
                    foreach (var c in movingClips)
                    {
                        double dur = c.SourceEnd - c.SourceStart;
                        double orig = movingClipOffsets[c];
                        double cLeft = nonMovingClips.Where(o => o.TrackOffset + (o.SourceEnd - o.SourceStart) <= orig).Select(o => (double?)(o.TrackOffset + (o.SourceEnd - o.SourceStart))).Max() ?? 0.0;
                        double cRight = nonMovingClips.Where(o => o.TrackOffset >= orig + dur).Select(o => (double?)o.TrackOffset).Min() ?? double.MaxValue;
                        minCandidateDt = Math.Max(minCandidateDt, cLeft - orig);
                        maxCandidateDt = Math.Min(maxCandidateDt, cRight - (orig + dur));
                    }
                    dt = Math.Clamp(dt, minCandidateDt, Math.Max(minCandidateDt, maxCandidateDt));
                }

                if (minOffset + dt < 0) dt = -minOffset;

                foreach (var c in movingClips)
                {
                    c.TrackOffset = Math.Max(0, Math.Round(movingClipOffsets[c] + dt, 2));
                    if (movingClipBorders != null && movingClipBorders.TryGetValue(c, out var b))
                    {
                        Canvas.SetLeft(b, TimelineMathService.TimeToPixel(c.TrackOffset, _timelineScale));
                    }
                }
            }
            else if (isTrimmingLeft)
            {
                var ptr = e.GetCurrentPoint(targetCanvas);
                double rawDx = ptr.Position.X - startPt.X;
                if (Math.Abs(rawDx) > 3) hasActuallyMoved = true;

                double candidateOffset = Math.Max(0, origOffset + (rawDx / _timelineScale));
                var snapPoints = GetTimelineSnapPoints(clip);
                var (snapped, snapTime) = TimelineMathService.FindSnapTime(candidateOffset, snapPoints, _timelineScale, 15.0);
                if (snapped) candidateOffset = snapTime;

                // Sol komşu klip ile çarpışma önleme
                double leftLimit = 0.0;
                if (ViewModel != null)
                {
                    var trackClips = isAudioTrack ? ViewModel.MicTrack.Clips : ViewModel.VideoTrack.Clips;
                    leftLimit = trackClips.Where(o => o != clip && o.TrackOffset + (o.SourceEnd - o.SourceStart) <= origOffset).Select(o => (double?)(o.TrackOffset + (o.SourceEnd - o.SourceStart))).Max() ?? 0.0;
                }
                double clipDur = origEnd - origStart;
                candidateOffset = Math.Clamp(candidateOffset, leftLimit, origOffset + clipDur - 0.2);

                double dt = candidateOffset - origOffset;
                clip.SourceStart = Math.Round(origStart + dt, 2);
                clip.TrackOffset = Math.Round(origOffset + dt, 2);

                double newDur = Math.Max(0.2, clip.SourceEnd - clip.SourceStart);
                clipBlock.Width = TimelineMathService.DurationToWidth(newDur, _timelineScale, 24);
                Canvas.SetLeft(clipBlock, TimelineMathService.TimeToPixel(clip.TrackOffset, _timelineScale));
                label.Text = isAudioTrack
                    ? $"Audio #{clipIndex + 1} ({newDur:F1}s)"
                    : $"Video #{clipIndex + 1} ({newDur:F1}s)";
            }
            else if (isTrimmingRight)
            {
                var ptr = e.GetCurrentPoint(targetCanvas);
                double rawDx = ptr.Position.X - startPt.X;
                if (Math.Abs(rawDx) > 3) hasActuallyMoved = true;

                double origDur = origEnd - origStart;
                double candidateEndOffset = origOffset + origDur + (rawDx / _timelineScale);
                var snapPoints = GetTimelineSnapPoints(clip);
                var (snapped, snapTime) = TimelineMathService.FindSnapTime(candidateEndOffset, snapPoints, _timelineScale, 15.0);
                if (snapped) candidateEndOffset = snapTime;

                // Sağ komşu klip ile çarpışma önleme
                double rightLimit = double.MaxValue;
                if (ViewModel != null)
                {
                    var trackClips = isAudioTrack ? ViewModel.MicTrack.Clips : ViewModel.VideoTrack.Clips;
                    rightLimit = trackClips.Where(o => o != clip && o.TrackOffset >= origOffset + origDur).Select(o => (double?)o.TrackOffset).Min() ?? double.MaxValue;
                }
                candidateEndOffset = Math.Clamp(candidateEndOffset, origOffset + 0.2, rightLimit);

                double dt = candidateEndOffset - (origOffset + origDur);
                clip.SourceEnd = Math.Round(origEnd + dt, 2);
                double newDur = Math.Max(0.2, clip.SourceEnd - clip.SourceStart);
                clipBlock.Width = TimelineMathService.DurationToWidth(newDur, _timelineScale, 24);
                label.Text = isAudioTrack
                    ? $"Audio #{clipIndex + 1} ({newDur:F1}s)"
                    : $"Video #{clipIndex + 1} ({newDur:F1}s)";
            }
        };

        clipBlock.PointerReleased += (s, e) =>
        {
            clipBlock.ReleasePointerCapture(e.Pointer);

            if (hasActuallyMoved)
            {
                if (isMoving && ViewModel != null)
                {
                    var trackClips = isAudioTrack ? ViewModel.MicTrack.Clips : ViewModel.VideoTrack.Clips;
                    trackClips.Sort((a, b) => a.TrackOffset.CompareTo(b.TrackOffset));
                }

                ViewModel?.PushHistory();
                ViewModel?.SaveProject();
                RenderTimeline();
                UpdateGapBlackScreen();
            }

            isMoving = false;
            isTrimmingLeft = false;
            isTrimmingRight = false;
            hasActuallyMoved = false;
            movingClips = null;
            movingClipOffsets = null;
            movingClipBorders = null;
        };

        Canvas.SetLeft(clipBlock, startX);
        Canvas.SetTop(clipBlock, top);
        targetCanvas.Children.Add(clipBlock);
    }

    private void RenderZoomPills()
    {
        if (!_isPageLoaded || ZoomTrack == null || ViewModel == null) return;
        ZoomTrack.Children.Clear();

        foreach (var zoom in ViewModel.ZoomEffects)
        {
            AddZoomPill(zoom);
        }
    }

    private Border AddZoomPill(ZoomEffect zoom)
    {
        double x = TimelineMathService.TimeToPixel(zoom.StartTime, _timelineScale);
        double exactWidth = TimelineMathService.DurationToWidth(zoom.Duration, _timelineScale, 0);
        double width = Math.Max(exactWidth, 10); // Ensure it's at least 10px to be clickable, but not 24px which breaks sync heavily.

        bool isSelected = _selectedZoomIds.Contains(zoom.Id) || _selectedZoom == zoom;

        var pill = new Border
        {
            Width = width,
            Height = 34,
            CornerRadius = new CornerRadius(17),
            Background = isSelected
                ? new SolidColorBrush(Color.FromArgb(120, 208, 188, 255))
                : new SolidColorBrush(Color.FromArgb(55, 160, 120, 255)),
            BorderBrush = isSelected
                ? new SolidColorBrush(Color.FromArgb(255, 208, 188, 255))
                : new SolidColorBrush(Color.FromArgb(130, 208, 188, 255)),
            BorderThickness = isSelected ? new Thickness(2.5) : new Thickness(1),
            Tag = zoom,
            IsHitTestVisible = true
        };

        var contentGrid = new Grid();

        // 1. Zoom Etiketi
        var label = new TextBlock
        {
            Text = $"{zoom.Name} ({zoom.Scale:F1}x)",
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 235, 225, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        contentGrid.Children.Add(label);

        // 2. Sol Kenar Tutamacı (In-Point / Start Time Trimming)
        var leftHandle = new Border
        {
            Width = 8,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = isSelected
                ? new SolidColorBrush(Color.FromArgb(90, 255, 255, 255))
                : new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            CornerRadius = new CornerRadius(17, 0, 0, 17),
            IsHitTestVisible = false,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new Border { Width = 1.5, Height = 14, Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), CornerRadius = new CornerRadius(1) },
                    new Border { Width = 1.5, Height = 14, Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), CornerRadius = new CornerRadius(1) }
                }
            }
        };

        // 3. Sağ Kenar Tutamacı (Out-Point / Duration Trimming)
        var rightHandle = new Border
        {
            Width = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = isSelected
                ? new SolidColorBrush(Color.FromArgb(90, 255, 255, 255))
                : new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            CornerRadius = new CornerRadius(0, 17, 17, 0),
            IsHitTestVisible = false,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new Border { Width = 1.5, Height = 14, Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), CornerRadius = new CornerRadius(1) },
                    new Border { Width = 1.5, Height = 14, Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), CornerRadius = new CornerRadius(1) }
                }
            }
        };

        contentGrid.Children.Add(leftHandle);
        contentGrid.Children.Add(rightHandle);
        pill.Child = contentGrid;

        bool isDragging = false;
        bool isTrimmingLeft = false;
        bool isTrimmingRight = false;
        bool hasActuallyMoved = false;
        Point startPoint = default;
        double origStart = 0;
        double origDur = 0;

        pill.PointerEntered += (s, e) =>
        {
            if (!isDragging && !isTrimmingLeft && !isTrimmingRight)
            {
                pill.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 220, 200, 255));
            }
        };

        pill.PointerExited += (s, e) =>
        {
            if (!isDragging && !isTrimmingLeft && !isTrimmingRight)
            {
                pill.BorderBrush = isSelected
                    ? new SolidColorBrush(Color.FromArgb(255, 208, 188, 255))
                    : new SolidColorBrush(Color.FromArgb(130, 208, 188, 255));
                this.ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
            }
        };

        pill.PointerPressed += (s, e) =>
        {
            e.Handled = true;

            var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            bool isCtrl = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

            if (isCtrl)
            {
                if (_selectedZoomIds.Contains(zoom.Id))
                {
                    _selectedZoomIds.Remove(zoom.Id);
                    _selectedZooms.Remove(zoom);
                    if (_selectedZoom == zoom)
                    {
                        _selectedZoom = _selectedZooms.LastOrDefault();
                    }
                }
                else
                {
                    _selectedZoomIds.Add(zoom.Id);
                    _selectedZooms.Add(zoom);
                    _selectedZoom = zoom;
                }

                if (_selectedZoom != null)
                {
                    SelectZoom(_selectedZoom, clearOthers: false);
                }
                else
                {
                    RenderZoomPills();
                }
            }
            else
            {
                _selectedZooms.Clear();
                _selectedZoomIds.Clear();
                _selectedZooms.Add(zoom);
                _selectedZoomIds.Add(zoom.Id);
                SelectZoom(zoom, clearOthers: false);
            }

            ViewModel.SelectedClipId = null;
            _activeTrack = TimelineTrackType.Zoom;

            var ptr = e.GetCurrentPoint(ZoomTrack);
            startPoint = ptr.Position;
            origStart = zoom.StartTime;
            origDur = zoom.Duration;
            hasActuallyMoved = false;

            var localPt = e.GetCurrentPoint(pill).Position;
            double edgeThreshold = Math.Clamp(pill.Width * 0.28, 12.0, 18.0);

            if (localPt.X <= edgeThreshold)
            {
                isTrimmingLeft = true;
                this.ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
            }
            else if (localPt.X >= pill.Width - edgeThreshold)
            {
                isTrimmingRight = true;
                this.ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
            }
            else
            {
                isDragging = true;
                this.ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeAll);
            }

            pill.CapturePointer(e.Pointer);
        };

        pill.PointerMoved += (s, e) =>
        {
            if (!isDragging && !isTrimmingLeft && !isTrimmingRight)
            {
                var localPt = e.GetCurrentPoint(pill).Position;
                double edgeThreshold = Math.Clamp(pill.Width * 0.28, 12.0, 18.0);
                if (localPt.X <= edgeThreshold || localPt.X >= pill.Width - edgeThreshold)
                {
                    this.ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
                }
                else
                {
                    this.ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
                }
                return;
            }

            var ptr = e.GetCurrentPoint(ZoomTrack);
            double rawDx = ptr.Position.X - startPoint.X;

            if (!hasActuallyMoved && Math.Abs(rawDx) > 3.0)
            {
                hasActuallyMoved = true;
            }

            if (!hasActuallyMoved) return;

            if (isDragging)
            {
                // Çarpışma / Üst üste binmeyi engelleme limitleri
                var otherZooms = ViewModel.ZoomEffects.Where(z => z != zoom).ToList();
                double leftLimit = otherZooms.Where(o => o.StartTime + o.Duration <= origStart).Select(o => (double?)(o.StartTime + o.Duration)).Max() ?? 0.0;
                double rightLimit = otherZooms.Where(o => o.StartTime >= origStart + origDur).Select(o => (double?)o.StartTime).Min() ?? double.MaxValue;

                double candidateStart = Math.Max(0, origStart + (rawDx / _timelineScale));
                var snapPoints = GetTimelineSnapPoints(zoom);

                // Sol kenar yapışması
                var (snapL, snapTimeL) = TimelineMathService.FindSnapTime(candidateStart, snapPoints, _timelineScale, 15.0);
                if (snapL)
                {
                    candidateStart = snapTimeL;
                }
                else
                {
                    // Sağ kenar yapışması
                    var (snapR, snapTimeR) = TimelineMathService.FindSnapTime(candidateStart + origDur, snapPoints, _timelineScale, 15.0);
                    if (snapR)
                    {
                        candidateStart = Math.Max(0, snapTimeR - origDur);
                    }
                }

                // Sıkı kesişim / çarpışma sınırlandırması (Collision Clamp)
                candidateStart = Math.Clamp(candidateStart, leftLimit, Math.Max(leftLimit, rightLimit - origDur));

                zoom.StartTime = Math.Max(0, Math.Round(candidateStart, 2));
                Canvas.SetLeft(pill, TimelineMathService.TimeToPixel(zoom.StartTime, _timelineScale));
                _isUpdatingZoomInputs = true;
                try
                {
                    if (NbZoomStart != null) NbZoomStart.Value = zoom.StartTime;
                    if (NbZoomEnd != null) NbZoomEnd.Value = zoom.StartTime + zoom.Duration;
                }
                finally { _isUpdatingZoomInputs = false; }
                UpdateZoomSimulation();
            }
            else if (isTrimmingLeft)
            {
                var otherZooms = ViewModel.ZoomEffects.Where(z => z != zoom).ToList();
                double leftLimit = otherZooms.Where(o => o.StartTime + o.Duration <= origStart).Select(o => (double?)(o.StartTime + o.Duration)).Max() ?? 0.0;

                double candidateStart = origStart + (rawDx / _timelineScale);
                var snapPoints = GetTimelineSnapPoints(zoom);
                var (snapped, snapTime) = TimelineMathService.FindSnapTime(candidateStart, snapPoints, _timelineScale, 15.0);
                if (snapped) candidateStart = snapTime;

                double fixedEnd = origStart + origDur;
                candidateStart = Math.Clamp(candidateStart, leftLimit, fixedEnd - 0.2);

                zoom.StartTime = Math.Round(candidateStart, 2);
                zoom.Duration = Math.Round(fixedEnd - zoom.StartTime, 2);

                double exactPillWidth = TimelineMathService.DurationToWidth(zoom.Duration, _timelineScale, 0);
                pill.Width = Math.Max(exactPillWidth, 10);
                Canvas.SetLeft(pill, TimelineMathService.TimeToPixel(zoom.StartTime, _timelineScale));
                _isUpdatingZoomInputs = true;
                try
                {
                    if (NbZoomStart != null) NbZoomStart.Value = zoom.StartTime;
                    if (NbZoomEnd != null) NbZoomEnd.Value = zoom.StartTime + zoom.Duration;
                }
                finally { _isUpdatingZoomInputs = false; }
                UpdateZoomSimulation();
            }
            else if (isTrimmingRight)
            {
                var otherZooms = ViewModel.ZoomEffects.Where(z => z != zoom).ToList();
                double rightLimit = otherZooms.Where(o => o.StartTime >= origStart + origDur).Select(o => (double?)o.StartTime).Min() ?? double.MaxValue;

                double candidateEnd = (origStart + origDur) + (rawDx / _timelineScale);
                var snapPoints = GetTimelineSnapPoints(zoom);
                var (snapped, snapTime) = TimelineMathService.FindSnapTime(candidateEnd, snapPoints, _timelineScale, 15.0);
                if (snapped) candidateEnd = snapTime;

                candidateEnd = Math.Clamp(candidateEnd, zoom.StartTime + 0.2, rightLimit);
                zoom.Duration = Math.Round(candidateEnd - zoom.StartTime, 2);

                double exactPillWidth = TimelineMathService.DurationToWidth(zoom.Duration, _timelineScale, 0);
                pill.Width = Math.Max(exactPillWidth, 10);
                _isUpdatingZoomInputs = true;
                try
                {
                    if (NbZoomEnd != null) NbZoomEnd.Value = zoom.StartTime + zoom.Duration;
                }
                finally { _isUpdatingZoomInputs = false; }
                UpdateZoomSimulation();
            }
        };

        pill.PointerReleased += (s, e) =>
        {
            pill.ReleasePointerCapture(e.Pointer);
            this.ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);

            if (hasActuallyMoved)
            {
                ViewModel.PushHistory();
                ViewModel.SaveProject();
                RenderTimeline();
                UpdateZoomSimulation();
            }

            isDragging = false;
            isTrimmingLeft = false;
            isTrimmingRight = false;
            hasActuallyMoved = false;
        };

        pill.DoubleTapped += (s, e) =>
        {
            e.Handled = true;
            SelectZoom(zoom);
        };

        Canvas.SetLeft(pill, x);
        Canvas.SetTop(pill, 11);
        ZoomTrack.Children.Add(pill);

        return pill;
    }

    private readonly HashSet<double> _selectedClickTimestamps = new();

    private void RenderClickTrack(double totalWidth)
    {
        if (ClickTrack == null || ViewModel == null) return;
        ClickTrack.Children.Clear();
        ClickTrack.Width = totalWidth + 120;

        var clicks = ViewModel.MouseClicks;
        if (clicks == null || clicks.Count == 0)
        {
            var emptyNotice = new TextBlock
            {
                Text = "Fare tıklamaları bu kanalda otomatik olarak gösterilir",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(48, 14, 0, 0)
            };
            ClickTrack.Children.Add(emptyNotice);
            return;
        }

        var validClicks = clicks.Where(c => c.Type == "left_down" || c.Type == "right_down").ToList();
        foreach (var c in validClicks)
        {
            double x = (c.Timestamp * _timelineScale);
            bool isRight = c.Type == "right_down";
            bool isSelected = _selectedClickTimestamps.Contains(c.Timestamp);

            var pill = new Border
            {
                Width = 14,
                Height = 32,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                BorderBrush = isSelected
                    ? new SolidColorBrush(Color.FromArgb(255, 255, 215, 0))
                    : new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
                Background = isRight
                    ? new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(0, 1),
                        GradientStops =
                        {
                            new GradientStop { Color = Color.FromArgb(255, 192, 132, 252), Offset = 0 },
                            new GradientStop { Color = Color.FromArgb(255, 124, 58, 237), Offset = 1 }
                        }
                    }
                    : new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(0, 1),
                        GradientStops =
                        {
                            new GradientStop { Color = Color.FromArgb(255, 0, 210, 255), Offset = 0 },
                            new GradientStop { Color = Color.FromArgb(255, 0, 120, 212), Offset = 1 }
                        }
                    },
                Child = new FontIcon
                {
                    Glyph = isRight ? "\uE964" : "\uE962",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            Canvas.SetLeft(pill, x - 7);
            Canvas.SetTop(pill, 8);

            ToolTipService.SetToolTip(pill, $"{(isRight ? "Sağ Fare Tıklaması" : "Sol Fare Tıklaması")} • {TimeSpan.FromSeconds(c.Timestamp):mm\\:ss\\.ff}");

            pill.PointerEntered += (s, e) =>
            {
                pill.Opacity = 1.0;
                pill.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.White);
            };

            pill.PointerExited += (s, e) =>
            {
                pill.Opacity = 0.9;
                pill.BorderBrush = _selectedClickTimestamps.Contains(c.Timestamp)
                    ? new SolidColorBrush(Color.FromArgb(255, 255, 215, 0))
                    : new SolidColorBrush(Color.FromArgb(100, 255, 255, 255));
            };

            pill.PointerPressed += (s, e) =>
            {
                e.Handled = true;
                _activeTrack = TimelineTrackType.Click;
                _selectedClickTimestamps.Clear();
                _selectedClickTimestamps.Add(c.Timestamp);
                RenderClickTrack(totalWidth);
                ViewModel.SeekTo(c.Timestamp);
                ClickSoundService.Instance.PlaySound(ViewModel.CursorClickSoundFile, ViewModel.CursorClickVolume);
            };

            ClickTrack.Children.Add(pill);
        }
    }

    private void HighlightAllClicks()
    {
        _selectedClickTimestamps.Clear();
        if (ViewModel?.MouseClicks != null)
        {
            foreach (var c in ViewModel.MouseClicks.Where(x => x.Type == "left_down" || x.Type == "right_down"))
            {
                _selectedClickTimestamps.Add(c.Timestamp);
            }
        }
        double totalWidth = Math.Max(_totalDurationSeconds * _timelineScale, 800);
        RenderClickTrack(totalWidth);
    }

    private void UpdatePlayhead()
    {
        if (PlayheadLine == null || PlayheadContainer == null) return;
        double x = _currentTimeSeconds * _timelineScale;
        Canvas.SetLeft(PlayheadContainer, x - 10);
        PlayheadContainer.Height = 22 + 72 + 56 + 56;
        PlayheadLine.Height = PlayheadContainer.Height;
    }

    private void SelectZoom(ZoomEffect zoom, bool clearOthers = true)
    {
        if (clearOthers)
        {
            _selectedZooms.Clear();
            _selectedZoomIds.Clear();
            _selectedZooms.Add(zoom);
            _selectedZoomIds.Add(zoom.Id);
        }
        else
        {
            if (!_selectedZoomIds.Contains(zoom.Id))
            {
                _selectedZooms.Add(zoom);
                _selectedZoomIds.Add(zoom.Id);
            }
        }

        _selectedZoom = zoom;
        zoom.Scale = Math.Clamp(zoom.Scale, 1.0, 2.2);
        SetSidebarTab("zoom");
        if (TbSelectedZoomFactor != null) TbSelectedZoomFactor.Text = $"{zoom.Scale:F1}x";
        if (SelectedZoomSlider != null) SelectedZoomSlider.Value = zoom.Scale;

        _isUpdatingZoomInputs = true;
        try
        {
            if (NbZoomStart != null) NbZoomStart.Value = zoom.StartTime;
            if (NbZoomEnd != null) NbZoomEnd.Value = zoom.StartTime + zoom.Duration;
        }
        finally
        {
            _isUpdatingZoomInputs = false;
        }

        if (ZoomLevelBadge != null) ZoomLevelBadge.Text = $"{zoom.Scale:F1}x";

        _isUpdatingBezierUI = true;
        try
        {
            string easingStr = zoom.Easing ?? "Cubic-Out";
            var (x1, y1, x2, y2) = ZoomEngineService.ParseCubicBezier(easingStr);
            _bezierP1 = new Point(x1, y1);
            _bezierP2 = new Point(x2, y2);

            string norm = easingStr.Trim().ToLowerInvariant().Replace(" ", "-");
            string comboItem = norm switch
            {
                "linear" => "Linear",
                "quad-out" or "quadout" => "Quad-Out",
                "cubic-out" or "cubicout" => "Cubic-Out",
                "quartic-out" or "quarticout" => "Quartic-Out",
                "ease-in-out" or "easeinout" => "Ease-In-Out",
                _ => "Özel (Custom Eğri)"
            };

            if (CbSelectedZoomEasing != null)
            {
                CbSelectedZoomEasing.SelectedItem = comboItem;
            }
            if (TbBezierPresetName != null)
            {
                TbBezierPresetName.Text = comboItem;
            }

            UpdateBezierGraphVisuals();
        }
        catch { }
        finally
        {
            _isUpdatingBezierUI = false;
        }

        RenderZoomPills();

        if (_currentTimeSeconds < zoom.StartTime || _currentTimeSeconds > zoom.StartTime + zoom.Duration)
        {
            SeekToTime(zoom.StartTime + Math.Min(0.2, zoom.Duration / 2));
        }
        else
        {
            UpdateZoomSimulation();
        }
    }

    private void SelectAllZooms()
    {
        if (ViewModel?.ZoomEffects == null || ViewModel.ZoomEffects.Count == 0) return;
        _selectedZooms.Clear();
        _selectedZoomIds.Clear();
        foreach (var z in ViewModel.ZoomEffects)
        {
            _selectedZooms.Add(z);
            _selectedZoomIds.Add(z.Id);
        }
        _selectedZoom = _selectedZooms.LastOrDefault();
        if (_selectedZoom != null)
        {
            SelectZoom(_selectedZoom, clearOthers: false);
        }
        RenderZoomPills();
    }

    private void ExecuteSelectAllForActiveTrack()
    {
        if (ViewModel == null) return;

        TimelineTrackType targetTrack = _activeTrack;
        if (_selectedZooms.Count > 0 || _selectedZoom != null)
        {
            targetTrack = TimelineTrackType.Zoom;
        }
        else if (ViewModel.SelectedClipIds.Count > 0 || !string.IsNullOrEmpty(ViewModel.SelectedClipId))
        {
            targetTrack = ViewModel.SelectedTrackType == "mic" ? TimelineTrackType.Audio : TimelineTrackType.Video;
        }

        switch (targetTrack)
        {
            case TimelineTrackType.Zoom:
                ViewModel.ClearClipSelection();
                UpdateClipSelectionVisuals();
                SelectAllZooms();
                break;

            case TimelineTrackType.Video:
                _selectedZooms.Clear();
                _selectedZoomIds.Clear();
                _selectedZoom = null;
                RenderZoomPills();
                ViewModel.SelectAllClipsInTrack("video");
                UpdateClipSelectionVisuals();
                break;

            case TimelineTrackType.Audio:
                _selectedZooms.Clear();
                _selectedZoomIds.Clear();
                _selectedZoom = null;
                RenderZoomPills();
                ViewModel.SelectAllClipsInTrack("mic");
                UpdateClipSelectionVisuals();
                break;

            case TimelineTrackType.Click:
                _selectedZooms.Clear();
                _selectedZoomIds.Clear();
                _selectedZoom = null;
                RenderZoomPills();
                ViewModel.ClearClipSelection();
                UpdateClipSelectionVisuals();
                HighlightAllClicks();
                break;
        }
    }

    private void DeleteSelectedZooms()
    {
        if (_selectedZooms.Count == 0 && _selectedZoom != null)
        {
            _selectedZooms.Add(_selectedZoom);
        }
        if (_selectedZooms.Count == 0) return;

        ViewModel?.PushHistory();
        foreach (var z in _selectedZooms.ToList())
        {
            ViewModel?.ZoomEffects.Remove(z);
        }
        _selectedZooms.Clear();
        _selectedZoomIds.Clear();
        _selectedZoom = null;
        if (ZoomLevelBadge != null) ZoomLevelBadge.Text = "1.0x";
        ViewModel?.SaveProject();
        RenderZoomPills();
        UpdateZoomSimulation();
    }

    private void DeleteZoom(ZoomEffect zoom)
    {
        if (ViewModel?.ZoomEffects == null) return;
        ViewModel.PushHistory();
        ViewModel.ZoomEffects.Remove(zoom);
        _selectedZooms.Remove(zoom);
        _selectedZoomIds.Remove(zoom.Id);
        if (_selectedZoom == zoom)
        {
            _selectedZoom = _selectedZooms.LastOrDefault();
            if (_selectedZoom == null && ZoomLevelBadge != null)
            {
                ZoomLevelBadge.Text = "1.0x";
            }
        }
        ViewModel.SaveProject();
        RenderZoomPills();
        UpdateZoomSimulation();
    }

    private bool IsCurrentTimeInVideoGap(double time)
    {
        if (ViewModel?.VideoTrack?.Clips == null || ViewModel.VideoTrack.Clips.Count == 0)
            return false;

        foreach (var clip in ViewModel.VideoTrack.Clips)
        {
            double start = clip.TrackOffset;
            double end = clip.TrackOffset + Math.Max(0.01, clip.SourceEnd - clip.SourceStart);
            if (time >= start && time < end)
            {
                return false;
            }
        }
        return true;
    }

    // Field declarations for Camera tracking
    private double _cameraCurrentX = 0.5, _cameraCurrentY = 0.5;
    private double _cameraTargetX  = 0.5, _cameraTargetY  = 0.5;
    private double _cameraCurrentScale = 1.0, _cameraTargetScale = 1.0;
    private const double CameraLerpSpeed = 0.025;

    private static double CubicEaseOut(double t)
        => 1.0 - Math.Pow(1.0 - Math.Clamp(t, 0.0, 1.0), 3.0);

    private static double LerpCubicOut(double from, double to, double t)
        => from + (to - from) * CubicEaseOut(t);

    private void UpdateCameraForZoomEffect(double currentTimeSec)
    {
        if (ViewModel == null || VideoTransform == null) return;

        var activeZoom = ViewModel.ZoomEffects?.FirstOrDefault(z =>
            currentTimeSec >= z.StartTime &&
            currentTimeSec <= z.StartTime + z.Duration);

        if (activeZoom == null)
        {
            _cameraTargetScale = 1.0;
            _cameraTargetX = 0.5;
            _cameraTargetY = 0.5;
        }
        else
        {
            _cameraTargetScale = activeZoom.Scale;
            _cameraTargetX = _naturalVideoWidth  > 0 ? activeZoom.TargetX / _naturalVideoWidth  : 0.5;
            _cameraTargetY = _naturalVideoHeight > 0 ? activeZoom.TargetY / _naturalVideoHeight : 0.5;
        }

        _cameraCurrentScale = LerpCubicOut(_cameraCurrentScale, _cameraTargetScale, CameraLerpSpeed);
        _cameraCurrentX     = LerpCubicOut(_cameraCurrentX,     _cameraTargetX,     CameraLerpSpeed);
        _cameraCurrentY     = LerpCubicOut(_cameraCurrentY,     _cameraTargetY,     CameraLerpSpeed);

        ApplyCameraTransform(_cameraCurrentScale, _cameraCurrentX, _cameraCurrentY);
    }

    private void ApplyCameraTransform(double scale, double normX, double normY)
    {
        if (VideoTransform == null || VideoWindowLayer == null) return;
        double W = VideoWindowLayer.ActualWidth;
        double H = VideoWindowLayer.ActualHeight;
        if (W <= 0 || H <= 0) return;

        scale = Math.Clamp(scale, 1.0, 2.2);

        // Gerçek video içeriğinin ekrandaki sınırlarını al (Letterbox hesabı)
        Windows.Foundation.Rect contentRect = GetVideoContentRect();

        // Odak noktasının konteyner üzerindeki gerçek piksel koordinatı
        double targetPx = contentRect.X + (normX * contentRect.Width);
        double targetPy = contentRect.Y + (normY * contentRect.Height);

        // Odak noktasını ekranın merkezine (W/2, H/2) hizalamak için gereken Translate
        double tx = (W * 0.5) - (targetPx * scale);
        double ty = (H * 0.5) - (targetPy * scale);

        tx = Math.Clamp(tx, W * (1.0 - scale), 0);
        ty = Math.Clamp(ty, H * (1.0 - scale), 0);

        VideoTransform.ScaleX     = scale;
        VideoTransform.ScaleY     = scale;
        VideoTransform.TranslateX = tx;
        VideoTransform.TranslateY = ty;
        
        if (BlackGapOverlay != null && BlackGapOverlay.RenderTransform is Microsoft.UI.Xaml.Media.CompositeTransform bgT)
        {
            bgT.ScaleX = scale;
            bgT.ScaleY = scale;
            bgT.TranslateX = tx;
            bgT.TranslateY = ty;
        }
    }

    private void UpdateGapBlackScreen()
    {
        if (BlackGapOverlay == null) return;
        bool isGap = IsCurrentTimeInVideoGap(_currentTimeSeconds);
        BlackGapOverlay.Visibility = isGap ? Visibility.Visible : Visibility.Collapsed;

        if (!isGap && ViewModel?.VideoTrack?.Clips != null)
        {
            var activeClip = ViewModel.VideoTrack.Clips.FirstOrDefault(c =>
                _currentTimeSeconds >= c.TrackOffset &&
                _currentTimeSeconds < c.TrackOffset + (c.SourceEnd - c.SourceStart));

            if (activeClip != null && VideoPlayer?.MediaPlayer != null)
            {
                double sourceTime = activeClip.SourceStart + (_currentTimeSeconds - activeClip.TrackOffset);
                var targetTs = TimeSpan.FromSeconds(Math.Max(0, sourceTime));
                if (Math.Abs((VideoPlayer.MediaPlayer.Position - targetTs).TotalMilliseconds) > 300)
                {
                    try { VideoPlayer.MediaPlayer.Position = targetTs; } catch { }
                }

                try
                {
                    if (_isPlaying && VideoPlayer.MediaPlayer.PlaybackSession.PlaybackState != Windows.Media.Playback.MediaPlaybackState.Playing)
                    {
                        VideoPlayer.MediaPlayer.Play();
                    }
                }
                catch { }
            }
        }
        else if (isGap)
        {
            if (VideoPlayer?.MediaPlayer != null)
            {
                try
                {
                    if (VideoPlayer.MediaPlayer.PlaybackSession.CanPause &&
                        VideoPlayer.MediaPlayer.PlaybackSession.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing)
                    {
                        VideoPlayer.MediaPlayer.Pause();
                    }
                }
                catch { }
            }
        }
    }

    private void OnZoomTrackDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel == null) return;

        var pos = e.GetPosition(ZoomTrack);
        double clickSec = Math.Max(0, pos.X / _timelineScale);

        // Eğer tıklanan konumda mevcut bir zoom efekti varsa onu seçip özellikler panelini aç.
        var existingZoom = ViewModel.ZoomEffects.FirstOrDefault(z => clickSec >= z.StartTime && clickSec <= z.StartTime + z.Duration);
        if (existingZoom != null)
        {
            SelectZoom(existingZoom);
        }
        else
        {
            // Boş alana çift tıklanırsa yeni zoom ekle.
            AddZoomEffectAt(clickSec);
        }
    }

    private void OnZoomTrackPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(ZoomTrack).Position;
        bool overPill = ZoomTrack.Children
            .OfType<Border>()
            .Any(b => b != ZoomHoverAddBadge &&
                      new Windows.Foundation.Rect(Canvas.GetLeft(b), Canvas.GetTop(b),
                               b.ActualWidth, b.ActualHeight).Contains(pt));
                               
        if (!overPill && ZoomHoverAddBadge != null)
        {
            ZoomHoverAddBadge.Visibility = Visibility.Visible;
            Canvas.SetLeft(ZoomHoverAddBadge, Math.Max(4, pt.X));
        }
        else if (overPill && ZoomHoverAddBadge != null)
        {
            ZoomHoverAddBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void OnZoomTrackPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (ZoomHoverAddBadge != null)
            ZoomHoverAddBadge.Visibility = Visibility.Collapsed;
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
        _lastTriggeredClickTimestamp = -1;
        _lastPlaybackTick = DateTime.UtcNow;
        PlayPauseIcon.Glyph = "\uE769";
        if (PlayOverlay != null) PlayOverlay.Opacity = 0;

        UpdateGapBlackScreen();

        try
        {
            if (!IsCurrentTimeInVideoGap(_currentTimeSeconds))
            {
                VideoPlayer?.MediaPlayer?.Play();
            }
        }
        catch { }

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
        _lastTriggeredClickTimestamp = -1;
        PlayPauseIcon.Glyph = "\uE768";
        if (PlayOverlay != null) PlayOverlay.Opacity = 1;

        try { VideoPlayer?.MediaPlayer?.Pause(); } catch { }
        try { _micPlayer?.Pause(); } catch { }
        try { _sysPlayer?.Pause(); } catch { }

        _playbackTimer?.Stop();
        UpdatePlaybackCursor(_currentTimeSeconds);
    }

    private void OnPlaybackTimerTick(object? sender, object e)
    {
        if (!_isPlaying) return;

        var now = DateTime.UtcNow;
        double dt = (now - _lastPlaybackTick).TotalSeconds;
        _lastPlaybackTick = now;
        if (dt <= 0 || dt > 0.5) dt = 0.025;

        _currentTimeSeconds += dt;

        if (_currentTimeSeconds >= _totalDurationSeconds && _totalDurationSeconds > 0)
        {
            PausePlayback();
            SeekToTime(0);
            return;
        }

        UpdateGapBlackScreen();
        UpdateCameraForZoomEffect(_currentTimeSeconds);

        var ts = TimeSpan.FromSeconds(_currentTimeSeconds);
        if (_isPlaying)
        {
            if (_micPlayer?.PlaybackSession != null && _micPlayer.PlaybackSession.NaturalDuration > TimeSpan.Zero)
            {
                if (ts <= _micPlayer.PlaybackSession.NaturalDuration)
                {
                    if (Math.Abs((_micPlayer.Position - ts).TotalMilliseconds) > 600)
                    {
                        try { _micPlayer.Position = ts; } catch { }
                    }
                }
            }

            if (_sysPlayer?.PlaybackSession != null && _sysPlayer.PlaybackSession.NaturalDuration > TimeSpan.Zero)
            {
                if (ts <= _sysPlayer.PlaybackSession.NaturalDuration)
                {
                    if (Math.Abs((_sysPlayer.Position - ts).TotalMilliseconds) > 600)
                    {
                        try { _sysPlayer.Position = ts; } catch { }
                    }
                }
            }
        }

        ViewModel.CurrentTimeSec = _currentTimeSeconds;
        TbCurrentTime.Text = FormatTime(_currentTimeSeconds);
        TbTimelineCurrent.Text = FormatTime(_currentTimeSeconds);
        UpdatePlayhead();
        UpdateZoomSimulation();
        UpdatePlaybackCursor(_currentTimeSeconds);
    }

    private void OnVideoCanvasHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateVideoContainerBounds();
    }

    private void UpdateVideoContainerBounds()
    {
        if (VideoContainer == null) return;

        UpdateVideoWindowRoundness();

        double hostW = VideoCanvasHost?.ActualWidth > 0 ? VideoCanvasHost.ActualWidth : 920;
        double hostH = VideoCanvasHost?.ActualHeight > 0 ? VideoCanvasHost.ActualHeight : 540;

        double availW = Math.Max(200, hostW - 32);
        double availH = Math.Max(150, hostH - 32);

        double natW = _naturalVideoWidth > 0 ? _naturalVideoWidth : (ViewModel?.VideoWidth > 0 ? ViewModel.VideoWidth : 1920.0);
        double natH = _naturalVideoHeight > 0 ? _naturalVideoHeight : (ViewModel?.VideoHeight > 0 ? ViewModel.VideoHeight : 1080.0);
        
        double canvasAspect = natW / natH;
        if (ViewModel != null && !string.IsNullOrEmpty(ViewModel.AspectRatio))
        {
            switch (ViewModel.AspectRatio)
            {
                case "16:9": canvasAspect = 16.0 / 9.0; break;
                case "4:3": canvasAspect = 4.0 / 3.0; break;
                case "1:1": canvasAspect = 1.0; break;
                case "9:16": canvasAspect = 9.0 / 16.0; break;
            }
        }

        // Arkaplan (Tuval) boyutunu hesapla - MAX SINIRI KALDIRILDI
        double targetW = availW;
        double targetH = targetW / canvasAspect;

        if (targetH > availH)
        {
            targetH = availH;
            targetW = targetH * canvasAspect;
        }

        VideoContainer.Width = Math.Round(targetW);
        VideoContainer.Height = Math.Round(targetH);

        // Videonun, tuval içindeki gerçek kaplayacağı alanı (kendi en-boy oranına göre) hesapla
        double videoAspect = natW / natH;
        double innerW = targetW;
        double innerH = innerW / videoAspect;
        if (innerH > targetH)
        {
            innerH = targetH;
            innerW = innerH * videoAspect;
        }

        // VideoWindowLayer'in boyutunu doğrudan videonun gerçek boyutuna eşitle ki sınırlarına (gölge, radius) tam otursun
        if (VideoWindowLayer != null)
        {
            VideoWindowLayer.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center;
            VideoWindowLayer.VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center;
            VideoWindowLayer.Width = Math.Round(innerW);
            VideoWindowLayer.Height = Math.Round(innerH);
        }

        if (BlackGapOverlay != null)
        {
            BlackGapOverlay.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center;
            BlackGapOverlay.VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center;
            BlackGapOverlay.Width = Math.Round(innerW);
            BlackGapOverlay.Height = Math.Round(innerH);
        }

        UpdatePlaybackCursor(_currentTimeSeconds);
    }

    private void UpdateVideoWindowRoundness()
    {
        if (VideoWindowLayer == null || ViewModel == null) return;
        
        VideoWindowLayer.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        if (BlackGapOverlay != null) BlackGapOverlay.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);

        if (ViewModel.IsVideoFrameEnabled)
        {
            // Margin yerine Scale kullanarak en-boy oranını (Aspect Ratio) koruyoruz.
            // Böylece yanlarda siyah boşluk (letterbox) oluşmaz.
            var scale = new Microsoft.UI.Xaml.Media.ScaleTransform { ScaleX = 0.92, ScaleY = 0.92 };
            
            VideoWindowLayer.Margin = new Thickness(0);
            VideoWindowLayer.RenderTransform = scale;
            VideoWindowLayer.CornerRadius = new CornerRadius(ViewModel.Roundness > 0 ? ViewModel.Roundness : 12);
            
            if (BlackGapOverlay != null) 
            {
                BlackGapOverlay.Margin = new Thickness(0);
                BlackGapOverlay.RenderTransform = scale;
                BlackGapOverlay.CornerRadius = new CornerRadius(ViewModel.Roundness > 0 ? ViewModel.Roundness : 12);
            }
        }
        else
        {
            VideoWindowLayer.Margin = new Thickness(0);
            VideoWindowLayer.RenderTransform = null;
            VideoWindowLayer.CornerRadius = new CornerRadius(0);
            
            if (BlackGapOverlay != null)
            {
                BlackGapOverlay.Margin = new Thickness(0);
                BlackGapOverlay.RenderTransform = null;
                BlackGapOverlay.CornerRadius = new CornerRadius(0);
            }
        }
    }

    private void OnVideoFrameToggled(object sender, RoutedEventArgs e)
    {
        UpdateVideoWindowRoundness();
    }

    private Rect GetVideoContentRect()
    {
        double containerW = VideoPlayer?.ActualWidth > 0 ? VideoPlayer.ActualWidth : 880;
        double containerH = VideoPlayer?.ActualHeight > 0 ? VideoPlayer.ActualHeight : 495;

        double natW = _naturalVideoWidth > 0 ? _naturalVideoWidth : (ViewModel?.VideoWidth > 0 ? ViewModel.VideoWidth : 1920.0);
        double natH = _naturalVideoHeight > 0 ? _naturalVideoHeight : (ViewModel?.VideoHeight > 0 ? ViewModel.VideoHeight : 1080.0);

        double videoAspect = natW / natH;
        double containerAspect = containerW / containerH;

        double renderW, renderH, offsetX, offsetY;

        if (containerAspect > videoAspect + 0.001)
        {
            // Genişlik fazla (sağda solda siyah şerit)
            renderH = containerH;
            renderW = containerH * videoAspect;
            offsetX = (containerW - renderW) / 2.0;
            offsetY = 0;
        }
        else if (containerAspect < videoAspect - 0.001)
        {
            // Yükseklik fazla (üstte altta siyah şerit)
            renderW = containerW;
            renderH = containerW / videoAspect;
            offsetX = 0;
            offsetY = (containerH - renderH) / 2.0;
        }
        else
        {
            renderW = containerW;
            renderH = containerH;
            offsetX = 0;
            offsetY = 0;
        }

        return new Rect(offsetX, offsetY, renderW, renderH);
    }

    private void UpdateZoomSimulation()
    {
        if (!_isPageLoaded || ZoomLevelBadge == null) return;

        // Mevcut fare konumunu telemetriden doğrudan kaynak video koordinatlarında al
        double cursorSrcX = -1, cursorSrcY = -1;
        if (ViewModel?.MouseMoves != null && ViewModel.MouseMoves.Count > 0)
        {
            var pt = ZoomEngineService.GetInterpolatedCursorPosition(ViewModel.MouseMoves, _currentTimeSeconds);
            if (pt.HasValue)
            {
                cursorSrcX = pt.Value.X;
                cursorSrcY = pt.Value.Y;
            }
        }

        var activeZoom = ViewModel?.GetCurrentZoom(cursorSrcX, cursorSrcY);
        if (activeZoom != null)
        {
            ZoomLevelBadge.Text = $"{activeZoom.Scale:F1}x";
            if (VideoTransform != null)
            {
                VideoTransform.ScaleX = activeZoom.Scale;
                VideoTransform.ScaleY = activeZoom.Scale;

                double natW = _naturalVideoWidth > 0 ? _naturalVideoWidth : (ViewModel?.VideoWidth > 0 ? ViewModel.VideoWidth : 1920.0);
                double natH = _naturalVideoHeight > 0 ? _naturalVideoHeight : (ViewModel?.VideoHeight > 0 ? ViewModel.VideoHeight : 1080.0);
                var rect = GetVideoContentRect();

                double normCenterX = natW / 2.0;
                double normCenterY = natH / 2.0;

                // Zoom ölçeğine göre kenar boşlukları ve tam merkezleme ofseti hesabı
                double maxOffsetX = (rect.Width * (activeZoom.Scale - 1.0)) / 2.0;
                double maxOffsetY = (rect.Height * (activeZoom.Scale - 1.0)) / 2.0;

                double offsetX = (normCenterX - activeZoom.TargetX) * (rect.Width / natW) * activeZoom.Scale;
                double offsetY = (normCenterY - activeZoom.TargetY) * (rect.Height / natH) * activeZoom.Scale;

                VideoTransform.TranslateX = Math.Clamp(offsetX, -maxOffsetX, maxOffsetX);
                VideoTransform.TranslateY = Math.Clamp(offsetY, -maxOffsetY, maxOffsetY);
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

    private double CalculateTimeFromPointerX(double pointerX, double timelineScale, double rulerOffset = 0.0)
    {
        double rawTime = (pointerX - rulerOffset) / timelineScale;
        return Math.Clamp(rawTime, 0.0, _totalDurationSeconds);
    }

    private void OnHoverScrubPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            element.CapturePointer(e.Pointer);
            var ptr = e.GetCurrentPoint(element);

            if (_selectedZoom != null || _selectedZooms.Count > 0 || !string.IsNullOrEmpty(ViewModel?.SelectedClipId) || (ViewModel?.SelectedClipIds.Count > 0))
            {
                _selectedZoom = null;
                _selectedZooms.Clear();
                _selectedZoomIds.Clear();
                if (ViewModel != null)
                {
                    ViewModel.ClearClipSelection();
                    ViewModel.SelectedTrackType = null;
                }
                UpdateClipSelectionVisuals();
                RenderZoomPills();
            }

            // 1. Playhead seek
            double clickSec = CalculateTimeFromPointerX(ptr.Position.X, _timelineScale);
            SeekToTime(clickSec);
            // Kırmızı playhead dışındaki bir yere tıklandığında _isDraggingPlayhead true olmasın,
            // sadece timeline panning yapılsın. Playhead'i sürüklemek için doğrudan playhead'e tıklanacak.

            // 2. Drag-to-scroll pan hazırlığı
            _isPanningTimeline = true;
            _panStartPoint = e.GetCurrentPoint(TimelineScrollViewer).Position;
            _panStartOffset = TimelineScrollViewer.HorizontalOffset;
        }
    }

    private void OnHoverScrubPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            var ptr = e.GetCurrentPoint(element);
            
            if (_isDraggingPlayhead)
            {
                double clickSec = CalculateTimeFromPointerX(ptr.Position.X, _timelineScale);
                SeekToTime(clickSec);

                // Drag-to-scroll
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
            else if (!_isPlaying)
            {
                // Hover Playhead
                double hoverSec = CalculateTimeFromPointerX(ptr.Position.X, _timelineScale);
                if (HoverPlayheadLine != null && HoverPlayheadTriangle != null)
                {
                    HoverPlayheadLine.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                    HoverPlayheadTriangle.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                    double x = hoverSec * _timelineScale;
                    Canvas.SetLeft(HoverPlayheadLine, x);
                    Canvas.SetLeft(HoverPlayheadTriangle, x);

                    // Hover (önizleme) anında videoyu da o frame'e getir
                    if (VideoPlayer?.MediaPlayer != null)
                    {
                        try { VideoPlayer.MediaPlayer.Position = TimeSpan.FromSeconds(hoverSec); } catch { }
                    }
                }
            }
        }
    }

    private void OnHoverScrubPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            element.ReleasePointerCapture(e.Pointer);
            _isDraggingPlayhead = false;
            _isPanningTimeline = false;
        }
    }

    private void OnHoverScrubPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (HoverPlayheadLine != null) HoverPlayheadLine.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        if (HoverPlayheadTriangle != null) HoverPlayheadTriangle.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

        // Fare timeline'dan çıkınca videoyu tekrar asıl zamana (kırmızı çizgi) döndür
        if (!_isPlaying && VideoPlayer?.MediaPlayer != null)
        {
            try { VideoPlayer.MediaPlayer.Position = TimeSpan.FromSeconds(_currentTimeSeconds); } catch { }
        }
    }

    private void OnPlayheadPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            e.Handled = true;
            element.CapturePointer(e.Pointer);
            
            var ptr = e.GetCurrentPoint(TimelineContentGrid);
            double clickSec = CalculateTimeFromPointerX(ptr.Position.X, _timelineScale);
            SeekToTime(clickSec);
            
            _isDraggingPlayhead = true;
            _isPanningTimeline = false; // Playhead sürüklerken timeline panning kapalı
        }
    }

    private void OnPlayheadPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isDraggingPlayhead && sender is UIElement element)
        {
            e.Handled = true;
            var ptr = e.GetCurrentPoint(TimelineContentGrid);
            double clickSec = CalculateTimeFromPointerX(ptr.Position.X, _timelineScale);
            SeekToTime(clickSec);

            // Drag-to-scroll (edge scrolling) for playhead dragging
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
            }
        }
    }

    private void OnPlayheadPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            e.Handled = true;
            element.ReleasePointerCapture(e.Pointer);
            _isDraggingPlayhead = false;
        }
    }

    private void OnPlayheadPointerExited(object sender, PointerRoutedEventArgs e)
    {
        // Optional
    }

    private void OnVideoTrackPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _activeTrack = TimelineTrackType.Video;
    }

    private void OnAudioTrackPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _activeTrack = TimelineTrackType.Audio;
    }

    private void OnZoomTrackPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _activeTrack = TimelineTrackType.Zoom;
    }

    private void OnZoomTrackPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _activeTrack = TimelineTrackType.Zoom;
        OnHoverScrubPointerPressed(sender, e);
    }

    private void OnClickTrackPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _activeTrack = TimelineTrackType.Click;
    }

    private void OnClickTrackPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _activeTrack = TimelineTrackType.Click;
        OnHoverScrubPointerPressed(sender, e);
    }

    private void OnToggleClickLaneClicked(object sender, RoutedEventArgs e)
    {
        if (ClickTrack != null)
        {
            if (ClickTrack.Visibility == Visibility.Visible)
            {
                ClickTrack.Visibility = Visibility.Collapsed;
                if (IconToggleClickLane != null) IconToggleClickLane.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 120, 120, 120)); // Muted
                if (TimelineContentGrid != null && TimelineContentGrid.RowDefinitions.Count > 4)
                {
                    TimelineContentGrid.RowDefinitions[4].Height = new GridLength(0);
                }
            }
            else
            {
                ClickTrack.Visibility = Visibility.Visible;
                if (IconToggleClickLane != null) IconToggleClickLane.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 210, 255)); // Accent
                if (TimelineContentGrid != null && TimelineContentGrid.RowDefinitions.Count > 4)
                {
                    TimelineContentGrid.RowDefinitions[4].Height = new GridLength(48);
                }
            }
        }
    }

    public void SeekToTime(double time)
    {
        _currentTimeSeconds = Math.Clamp(time, 0, _totalDurationSeconds);
        _lastTriggeredClickTimestamp = -1;
        _lastPlaybackTick = DateTime.UtcNow;

        UpdateGapBlackScreen();

        var ts = TimeSpan.FromSeconds(_currentTimeSeconds);
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

        try
        {
            if (VideoPlayer?.MediaPlayer?.PlaybackSession != null && ts <= VideoPlayer.MediaPlayer.PlaybackSession.NaturalDuration)
            {
                VideoPlayer.MediaPlayer.Position = ts;
            }
        }
        catch { }

        ViewModel.CurrentTimeSec = _currentTimeSeconds;
        TbCurrentTime.Text = FormatTime(_currentTimeSeconds);
        TbTimelineCurrent.Text = FormatTime(_currentTimeSeconds);
        UpdatePlayhead();
        UpdateZoomSimulation();
        UpdatePlaybackCursor(_currentTimeSeconds);
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
            // Ctrl + Mouse Wheel: Timeline Zoom in/out (hızlandırılmış & fare imleci pivotlu)
            double steps = Math.Max(1.0, Math.Abs(delta) / 120.0);
            double factor = delta > 0 ? Math.Pow(1.35, steps) : Math.Pow(1.0 / 1.35, steps);
            double oldScale = _timelineScale;
            double newScale = Math.Clamp(oldScale * factor, 15, 600);
            if (Math.Abs(newScale - oldScale) < 0.01) return;

            // Pivot hesaplama: Fare imlecinin altındaki saniye noktası zoom sonrası aynı ekran konumunda kalsın
            double currentOffset = TimelineScrollViewer.HorizontalOffset;
            double mouseViewportX = ptr.Position.X;
            double mouseCanvasX = currentOffset + mouseViewportX;
            double mouseSec = Math.Max(0, mouseCanvasX / oldScale);

            _timelineScale = newScale;

            if (TimelineZoomSlider != null)
            {
                _isUpdatingZoomSlider = true;
                try
                {
                    TimelineZoomSlider.Value = Math.Clamp(((_timelineScale - 20) / 280.0) * 100.0, 1, 100);
                }
                finally
                {
                    _isUpdatingZoomSlider = false;
                }
            }

            UpdateFromViewModel();
            RenderTimeline();

            // Yeni ofseti uygula (Pivot konumu koru)
            double newMouseCanvasX = (mouseSec * newScale);
            double targetOffset = Math.Max(0, newMouseCanvasX - mouseViewportX);
            TimelineScrollViewer.ChangeView(targetOffset, null, null, true);
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

            case Windows.System.VirtualKey.A when isCtrl:
                e.Handled = true;
                ExecuteSelectAllForActiveTrack();
                break;

            case Windows.System.VirtualKey.Delete:
            case Windows.System.VirtualKey.Back:
                e.Handled = true;
                if (_selectedZooms.Count > 0)
                {
                    DeleteSelectedZooms();
                }
                else if (_selectedZoom != null)
                {
                    DeleteZoom(_selectedZoom);
                }
                else if ((ViewModel?.SelectedClipIds.Count > 0) || !string.IsNullOrEmpty(ViewModel?.SelectedClipId))
                {
                    ViewModel?.DeleteSelected();
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
        if (_isUpdatingZoomSlider) return;
        // Convert slider value (0-100) to a scale factor: base 20% + up to 300%.
        double newScale = 20 + (e.NewValue / 100.0) * 280;
        _timelineScale = newScale;
        // Keep ViewModel in sync for any bindings that rely on timeline zoom value.
        if (ViewModel != null)
        {
            ViewModel.TimelineZoom = e.NewValue;
        }
        if (!_isPageLoaded || ViewModel == null) return;
        // Update derived UI elements (clip widths, ruler) before re‑rendering.
        UpdateFromViewModel();
        RenderTimeline();
    }

    private void OnMotionBlurChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_isPageLoaded) return;
        if (TbZoomBlurVal != null) TbZoomBlurVal.Text = $"{(int)e.NewValue}%";
        if (ViewModel != null) ViewModel.MotionBlurAmount = e.NewValue;
    }



    // Yeniden giriş kilidi: ses slider'larının birbirini döngüsel tetiklemesini önler
    private bool _isUpdatingVolume = false;

    private void OnMicVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_isPageLoaded || _isUpdatingVolume) return;
        _isUpdatingVolume = true;
        try
        {
            if (TbMicVolVal2 != null) TbMicVolVal2.Text = $"{(int)e.NewValue}%";
            if (ViewModel != null) ViewModel.MicVolume = e.NewValue;
            if (_micPlayer != null && ViewModel != null && !ViewModel.MicMuted) _micPlayer.Volume = e.NewValue / 100.0;
            // SliderAudioTrackVol (timeline sol panel) sync
            if (SliderAudioTrackVol != null && Math.Abs(SliderAudioTrackVol.Value - e.NewValue) > 0.5) SliderAudioTrackVol.Value = e.NewValue;
            UpdateAudioIconState();
        }
        finally
        {
            _isUpdatingVolume = false;
        }
    }

    private void OnAudioTrackVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        OnMicVolumeChanged(sender, e);
    }

    /// <summary>
    /// Flyout açıldığında içindeki slider ve label'ı güncel ses seviyesiyle doldurur.
    /// x:Bind/x:Name flyout içinde WinUI 3'te E_XAMLPARSEFAILED'a yol açar;
    /// bu nedenle Opened event + Tag tabanlı traversal kullanılır.
    /// </summary>
    private void OnAudioFlyoutOpened(object sender, object e)
    {
        if (ViewModel == null) return;
        try
        {
            double vol = ViewModel.MicVolume;
            // Flyout içeriği: Flyout → Border → StackPanel → Slider[Tag=FlyoutVolSlider], TextBlock[Tag=FlyoutVolLabel]
            if (sender is Flyout flyout && flyout.Content is Border border && border.Child is StackPanel sp)
            {
                foreach (var child in sp.Children)
                {
                    if (child is Slider sl && sl.Tag is string slTag && slTag == "FlyoutVolSlider")
                    {
                        _isUpdatingVolume = true;
                        try { sl.Value = vol; } finally { _isUpdatingVolume = false; }
                    }
                    if (child is Grid grid)
                    {
                        foreach (var gc in grid.Children)
                        {
                            if (gc is TextBlock tb && tb.Tag is string tbTag && tbTag == "FlyoutVolLabel")
                            {
                                tb.Text = $"{(int)vol}%";
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EditorPage] OnAudioFlyoutOpened hatası: {ex.Message}");
        }
    }

    /// <summary>
    /// Flyout içindeki ses slider'ı değiştiğinde çağrılır.
    /// </summary>
    private void OnFlyoutAudioVolSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingVolume || ViewModel == null) return;
        double newVol = e.NewValue;
        _isUpdatingVolume = true;
        try
        {
            ViewModel.MicVolume = newVol;
            if (_micPlayer != null && !ViewModel.MicMuted) _micPlayer.Volume = newVol / 100.0;
            if (SliderAudioTrackVol != null) SliderAudioTrackVol.Value = newVol;
            if (TbMicVolVal2 != null) TbMicVolVal2.Text = $"{(int)newVol}%";
            // Flyout label güncelle
            if (sender is Slider sl && sl.Parent is StackPanel sp)
            {
                foreach (var child in sp.Children)
                {
                    if (child is Grid grid)
                    {
                        foreach (var gc in grid.Children)
                        {
                            if (gc is TextBlock tb && tb.Tag is string tbTag && tbTag == "FlyoutVolLabel")
                                tb.Text = $"{(int)newVol}%";
                        }
                    }
                }
            }
            UpdateAudioIconState();
        }
        finally
        {
            _isUpdatingVolume = false;
        }
    }

    private void OnAudioTrackBtnWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (BtnAudioMute == null) return;
        var delta = e.GetCurrentPoint(BtnAudioMute).Properties.MouseWheelDelta;
        if (ViewModel != null)
        {
            double step = delta > 0 ? 5 : -5;
            double newVol = Math.Clamp(ViewModel.MicVolume + step, 0, 100);
            _isUpdatingVolume = true;
            try
            {
                ViewModel.MicVolume = newVol;
                if (SliderAudioTrackVol != null) SliderAudioTrackVol.Value = newVol;
                if (TbMicVolVal2 != null) TbMicVolVal2.Text = $"{(int)newVol}%";
                if (_micPlayer != null && !ViewModel.MicMuted) _micPlayer.Volume = newVol / 100.0;
            }
            finally
            {
                _isUpdatingVolume = false;
            }
            UpdateAudioIconState();
            e.Handled = true;
        }
    }

    private void UpdateAudioIconState()
    {
        if (IconAudioMute == null || ViewModel == null) return;
        // MicTrack null olabilir (proje henüz yüklenmemişse); null-safe erişim
        bool isMuted = (ViewModel.MicTrack?.Muted ?? false) || ViewModel.MicVolume <= 0;
        if (isMuted)
        {
            IconAudioMute.Glyph = "\uE74F";
            IconAudioMute.Foreground = new SolidColorBrush(Color.FromArgb(255, 239, 68, 68));
        }
        else
        {
            double vol = ViewModel.MicVolume;
            IconAudioMute.Glyph = vol switch
            {
                <= 33 => "\uE993",
                <= 66 => "\uE994",
                _ => "\uE995"
            };
            IconAudioMute.Foreground = new SolidColorBrush(Color.FromArgb(255, 34, 197, 94));
        }
    }

    private void UpdateClickIconState()
    {
        if (IconClickMute == null || ViewModel == null) return;
        bool isMuted = !ViewModel.CursorClickSound || ViewModel.CursorClickVolume <= 0;
        if (isMuted)
        {
            IconClickMute.Glyph = "\uE74F";
            IconClickMute.Foreground = new SolidColorBrush(Color.FromArgb(255, 239, 68, 68));
        }
        else
        {
            double vol = ViewModel.CursorClickVolume;
            IconClickMute.Glyph = vol switch
            {
                <= 33 => "\uE993",
                <= 66 => "\uE994",
                _ => "\uE995"
            };
            IconClickMute.Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 210, 255));
        }

        if (TbTrackClickSub != null)
        {
            var soundItem = ClickSoundService.Instance.GetSoundByFileName(ViewModel.CursorClickSoundFile);
            TbTrackClickSub.Text = isMuted ? "Sessiz" : (soundItem?.ShortName ?? "Bilgisayar Tık 02");
        }
    }

    private bool _isInitializingClickSound = false;

    private void InitializeClickSoundUI()
    {
        _isInitializingClickSound = true;
        try
        {
            if (ComboClickSounds != null)
            {
                ComboClickSounds.ItemsSource = ClickSoundService.Instance.AvailableSounds;
                var currentSound = ClickSoundService.Instance.GetSoundByFileName(ViewModel?.CursorClickSoundFile);
                ComboClickSounds.SelectedItem = currentSound ?? ClickSoundService.Instance.AvailableSounds.FirstOrDefault();
            }

            if (PanelClickSoundDetails != null && ViewModel != null)
            {
                PanelClickSoundDetails.Visibility = ViewModel.CursorClickSound ? Visibility.Visible : Visibility.Collapsed;
            }

            if (TbCursorSoundVolText != null && ViewModel != null)
            {
                TbCursorSoundVolText.Text = $"{(int)ViewModel.CursorClickVolume}%";
            }

            UpdateClickIconState();
        }
        finally
        {
            _isInitializingClickSound = false;
        }
    }

    private void OnClickTrackVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_isPageLoaded || ViewModel == null) return;
        ViewModel.CursorClickVolume = e.NewValue;
        UpdateClickIconState();
    }

    private void OnClickTrackBtnWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (BtnClickMute == null || ViewModel == null) return;
        var delta = e.GetCurrentPoint(BtnClickMute).Properties.MouseWheelDelta;
        if (delta != 0)
        {
            double step = delta > 0 ? 5 : -5;
            double newVol = Math.Clamp(ViewModel.CursorClickVolume + step, 0, 100);
            if (Math.Abs(ViewModel.CursorClickVolume - newVol) > 0.1)
            {
                ViewModel.CursorClickVolume = newVol;
                UpdateClickIconState();
            }
            e.Handled = true;
        }
    }

    private void OnClickTrackMuteClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        ViewModel.CursorClickSound = !ViewModel.CursorClickSound;
        UpdateClickIconState();
        if (TsCursorSound != null) TsCursorSound.IsOn = ViewModel.CursorClickSound;
        if (PanelClickSoundDetails != null) PanelClickSoundDetails.Visibility = ViewModel.CursorClickSound ? Visibility.Visible : Visibility.Collapsed;
        ViewModel.SaveProject();
    }

    private void OnClickFlyoutOpened(object sender, object e)
    {
        if (ViewModel == null) return;
        try
        {
            double vol = ViewModel.CursorClickVolume;
            if (sender is Flyout flyout && flyout.Content is Border border && border.Child is StackPanel sp)
            {
                foreach (var child in sp.Children)
                {
                    if (child is Slider sl && sl.Tag is string slTag && slTag == "FlyoutClickVolSlider")
                    {
                        sl.Value = vol;
                    }
                    if (child is ComboBox cb && cb.Tag is string cbTag && cbTag == "FlyoutClickSoundCombo")
                    {
                        cb.ItemsSource = ClickSoundService.Instance.AvailableSounds;
                        cb.SelectedItem = ClickSoundService.Instance.GetSoundByFileName(ViewModel.CursorClickSoundFile);
                    }
                    if (child is Grid grid)
                    {
                        foreach (var gc in grid.Children)
                        {
                            if (gc is TextBlock tb && tb.Tag is string tbTag && tbTag == "FlyoutClickVolLabel")
                            {
                                tb.Text = $"{(int)vol}%";
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EditorPage] OnClickFlyoutOpened hatası: {ex.Message}");
        }
    }

    private void OnFlyoutClickVolSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (ViewModel == null) return;
        double newVol = e.NewValue;
        ViewModel.CursorClickVolume = newVol;
        if (SliderClickTrackVol != null) SliderClickTrackVol.Value = newVol;
        if (SliderCursorClickVol != null) SliderCursorClickVol.Value = newVol;
        if (TbCursorSoundVolText != null) TbCursorSoundVolText.Text = $"{(int)newVol}%";

        if (sender is Slider sl && sl.Parent is StackPanel sp)
        {
            foreach (var child in sp.Children)
            {
                if (child is Grid grid)
                {
                    foreach (var gc in grid.Children)
                    {
                        if (gc is TextBlock tb && tb.Tag is string tbTag && tbTag == "FlyoutClickVolLabel")
                            tb.Text = $"{(int)newVol}%";
                    }
                }
            }
        }
        UpdateClickIconState();
    }

    private void OnFlyoutClickSoundSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedItem is ClickSoundItem sound && ViewModel != null)
        {
            ViewModel.CursorClickSoundFile = sound.FileName;
            UpdateClickIconState();
            if (ComboClickSounds != null) ComboClickSounds.SelectedItem = sound;
            ClickSoundService.Instance.PlaySound(sound.FileName, ViewModel.CursorClickVolume);
            ViewModel.SaveProject();
        }
    }

    private void OnCursorSoundToggled(object sender, RoutedEventArgs e)
    {
        if (!_isPageLoaded || ViewModel == null) return;
        bool isOn = TsCursorSound?.IsOn ?? false;
        ViewModel.CursorClickSound = isOn;
        if (PanelClickSoundDetails != null) PanelClickSoundDetails.Visibility = isOn ? Visibility.Visible : Visibility.Collapsed;
        
        if (isOn && ClickTrack != null && ClickTrack.Visibility == Visibility.Collapsed)
        {
            ClickTrack.Visibility = Visibility.Visible;
            if (IconToggleClickLane != null) IconToggleClickLane.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 210, 255)); // Accent
            if (TimelineContentGrid != null && TimelineContentGrid.RowDefinitions.Count > 4)
            {
                TimelineContentGrid.RowDefinitions[4].Height = new GridLength(48);
            }
        }

        UpdateClickIconState();
        ViewModel.SaveProject();
    }

    private void OnCursorClickVolSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_isPageLoaded || ViewModel == null) return;
        ViewModel.CursorClickVolume = e.NewValue;
        if (TbCursorSoundVolText != null) TbCursorSoundVolText.Text = $"{(int)e.NewValue}%";
        if (SliderClickTrackVol != null) SliderClickTrackVol.Value = e.NewValue;
        UpdateClickIconState();
    }

    private void OnComboClickSoundsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isPageLoaded || ViewModel == null || _isInitializingClickSound) return;
        if (ComboClickSounds?.SelectedItem is ClickSoundItem sound)
        {
            ViewModel.CursorClickSoundFile = sound.FileName;
            UpdateClickIconState();
            ClickSoundService.Instance.PlaySound(sound.FileName, ViewModel.CursorClickVolume);
            ViewModel.SaveProject();
        }
    }

    private void OnTestClickSoundClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        ClickSoundService.Instance.PlaySound(ViewModel.CursorClickSoundFile, ViewModel.CursorClickVolume);
    }

    private void OnVideoTrackVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        OnSysVolumeChanged(sender, e);
    }

    private void OnSysVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_isPageLoaded) return;
        if (TbSysVolVal != null) TbSysVolVal.Text = $"{(int)e.NewValue}%";
        if (ViewModel != null) ViewModel.SysVolume = e.NewValue;
        if (_sysPlayer != null) _sysPlayer.Volume = e.NewValue / 100.0;
        if (VideoPlayer?.MediaPlayer != null) VideoPlayer.MediaPlayer.Volume = e.NewValue / 100.0;
        if (SliderVideoTrackVol != null && Math.Abs(SliderVideoTrackVol.Value - e.NewValue) > 0.5)
        {
            SliderVideoTrackVol.Value = e.NewValue;
        }
        if (SysVolumeSlider != null && Math.Abs(SysVolumeSlider.Value - e.NewValue) > 0.5)
        {
            SysVolumeSlider.Value = e.NewValue;
        }
        UpdateVideoAudioIconState();
    }

    private void OnVideoAudioAnimTick(object? sender, object e)
    {
        if (!_isPlaying) return;
        if (VideoAudioBar1 == null || VideoAudioBar2 == null || VideoAudioBar3 == null || ViewModel == null) return;

        bool isMuted = (ViewModel.VideoTrack?.Muted ?? false) || ViewModel.SysVolume <= 0;
        if (isMuted)
        {
            VideoAudioBar1.Height = 1;
            VideoAudioBar2.Height = 1;
            VideoAudioBar3.Height = 1;
            VideoAudioBar1.Opacity = 0.25;
            VideoAudioBar2.Opacity = 0.25;
            VideoAudioBar3.Opacity = 0.25;
            return;
        }

        double volFactor = Math.Clamp(ViewModel.SysVolume / 100.0, 0.2, 1.0);
        if (_isPlaying)
        {
            _videoAudioAnimTick++;
            double t = _videoAudioAnimTick * 0.45;
            double h1 = Math.Clamp((3.5 + Math.Sin(t) * 3.5 + Math.Cos(t * 1.3) * 2.0) * volFactor, 2.0, 14.0);
            double h2 = Math.Clamp((6.0 + Math.Sin(t * 1.4 + 1.2) * 5.0 + Math.Cos(t * 0.7) * 3.0) * volFactor, 2.0, 16.0);
            double h3 = Math.Clamp((4.0 + Math.Cos(t * 1.1 + 2.0) * 4.0 + Math.Sin(t * 0.8) * 2.5) * volFactor, 2.0, 14.0);

            VideoAudioBar1.Height = h1;
            VideoAudioBar2.Height = h2;
            VideoAudioBar3.Height = h3;
            VideoAudioBar1.Opacity = 1.0;
            VideoAudioBar2.Opacity = 1.0;
            VideoAudioBar3.Opacity = 1.0;
        }
        else
        {
            VideoAudioBar1.Height = Math.Max(2.0, 3.0 * volFactor);
            VideoAudioBar2.Height = Math.Max(3.0, 6.0 * volFactor);
            VideoAudioBar3.Height = Math.Max(2.0, 4.0 * volFactor);
            VideoAudioBar1.Opacity = 0.7;
            VideoAudioBar2.Opacity = 0.7;
            VideoAudioBar3.Opacity = 0.7;
        }
    }

    private void UpdateVideoAudioIconState()
    {
        if (IconVideoMute == null || ViewModel == null) return;
        bool isMuted = (ViewModel.VideoTrack?.Muted ?? false) || ViewModel.SysVolume <= 0;
        if (isMuted)
        {
            IconVideoMute.Glyph = "\uE74F";
            IconVideoMute.Foreground = new SolidColorBrush(Color.FromArgb(255, 239, 68, 68));
        }
        else
        {
            double vol = ViewModel.SysVolume;
            IconVideoMute.Glyph = vol switch
            {
                <= 33 => "\uE993",
                <= 66 => "\uE994",
                _ => "\uE995"
            };
            IconVideoMute.Foreground = new SolidColorBrush(Color.FromArgb(255, 56, 189, 248));
        }
    }

    private void OnVideoFlyoutOpened(object sender, object e)
    {
        if (ViewModel == null) return;
        try
        {
            double vol = ViewModel.SysVolume;
            if (sender is Flyout flyout && flyout.Content is Border border && border.Child is StackPanel sp)
            {
                foreach (var child in sp.Children)
                {
                    if (child is Slider sl && sl.Tag is string slTag && slTag == "FlyoutVideoVolSlider")
                    {
                        _isUpdatingVolume = true;
                        try { sl.Value = vol; } finally { _isUpdatingVolume = false; }
                    }
                    if (child is Grid grid)
                    {
                        foreach (var gc in grid.Children)
                        {
                            if (gc is TextBlock tb && tb.Tag is string tbTag && tbTag == "FlyoutVideoVolLabel")
                            {
                                tb.Text = $"{(int)vol}%";
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EditorPage] OnVideoFlyoutOpened hatası: {ex.Message}");
        }
    }

    private void OnFlyoutVideoVolSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingVolume || ViewModel == null) return;
        double newVol = e.NewValue;
        _isUpdatingVolume = true;
        try
        {
            ViewModel.SysVolume = newVol;
            if (_sysPlayer != null && !ViewModel.VideoTrack.Muted) _sysPlayer.Volume = newVol / 100.0;
            if (VideoPlayer?.MediaPlayer != null && !ViewModel.VideoTrack.Muted) VideoPlayer.MediaPlayer.Volume = newVol / 100.0;
            if (SliderVideoTrackVol != null) SliderVideoTrackVol.Value = newVol;
            if (TbSysVolVal != null) TbSysVolVal.Text = $"{(int)newVol}%";

            if (sender is Slider sl && sl.Parent is StackPanel sp)
            {
                foreach (var child in sp.Children)
                {
                    if (child is Grid grid)
                    {
                        foreach (var gc in grid.Children)
                        {
                            if (gc is TextBlock tb && tb.Tag is string tbTag && tbTag == "FlyoutVideoVolLabel")
                            {
                                tb.Text = $"{(int)newVol}%";
                            }
                        }
                    }
                }
            }
            UpdateVideoAudioIconState();
        }
        finally
        {
            _isUpdatingVolume = false;
        }
    }

    private void OnVideoTrackBtnWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var ptr = e.GetCurrentPoint(sender as UIElement);
        int delta = ptr.Properties.MouseWheelDelta;
        e.Handled = true;
        double step = delta > 0 ? 5.0 : -5.0;
        double newVol = Math.Clamp(ViewModel.SysVolume + step, 0, 100);
        ViewModel.SysVolume = newVol;
        if (_sysPlayer != null && !ViewModel.VideoTrack.Muted) _sysPlayer.Volume = newVol / 100.0;
        if (VideoPlayer?.MediaPlayer != null && !ViewModel.VideoTrack.Muted) VideoPlayer.MediaPlayer.Volume = newVol / 100.0;
        if (SliderVideoTrackVol != null) SliderVideoTrackVol.Value = newVol;
        if (TbSysVolVal != null) TbSysVolVal.Text = $"{(int)newVol}%";
        UpdateVideoAudioIconState();
    }

    private void OnSelectedZoomFactorChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_isPageLoaded || _selectedZoom == null) return;
        double clamped = Math.Clamp(e.NewValue, 1.0, 2.2);
        _selectedZoom.Scale = clamped;

        if (_selectedZooms.Count > 1)
        {
            foreach (var z in _selectedZooms)
            {
                z.Scale = clamped;
            }
        }

        // Hedef koordinat varsayılan merkezde kalmışsa veya atanmamışsa gerçek fare tıklamasıyla eşle
        if (ViewModel?.MouseClicks != null && ViewModel.MouseClicks.Count > 0)
        {
            var matchedClick = ViewModel.MouseClicks
                .Where(c => c.Timestamp >= _selectedZoom.StartTime - 0.3 && c.Timestamp <= _selectedZoom.StartTime + _selectedZoom.Duration)
                .OrderBy(c => Math.Abs(c.Timestamp - (_selectedZoom.StartTime + 0.15)))
                .FirstOrDefault();
            if (matchedClick != null && (_selectedZoom.TargetX <= 0 || (Math.Abs(_selectedZoom.TargetX - 960) < 1 && Math.Abs(_selectedZoom.TargetY - 540) < 1)))
            {
                _selectedZoom.TargetX = Math.Round((double)matchedClick.X, 1);
                _selectedZoom.TargetY = Math.Round((double)matchedClick.Y, 1);
            }
        }
        else if (ViewModel?.MouseMoves != null && ViewModel.MouseMoves.Count > 0)
        {
            var pt = ZoomEngineService.GetInterpolatedCursorPosition(ViewModel.MouseMoves, _selectedZoom.StartTime + 0.15)
                     ?? ZoomEngineService.GetInterpolatedCursorPosition(ViewModel.MouseMoves, _selectedZoom.StartTime);
            if (pt.HasValue && (_selectedZoom.TargetX <= 0 || (Math.Abs(_selectedZoom.TargetX - 960) < 1 && Math.Abs(_selectedZoom.TargetY - 540) < 1)))
            {
                _selectedZoom.TargetX = Math.Round(pt.Value.X, 1);
                _selectedZoom.TargetY = Math.Round(pt.Value.Y, 1);
            }
        }

        if (TbSelectedZoomFactor != null) TbSelectedZoomFactor.Text = $"{clamped:F1}x";
        if (ZoomLevelBadge != null) ZoomLevelBadge.Text = $"{clamped:F1}x";
        RenderZoomPills();
        UpdateZoomSimulation();
        UpdatePlaybackCursor(_currentTimeSeconds);
    }

    private void OnZoomStartChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isPageLoaded || _isUpdatingZoomInputs || _selectedZoom == null || double.IsNaN(args.NewValue)) return;
        double newStart = Math.Max(0, args.NewValue);
        double currentEnd = _selectedZoom.StartTime + _selectedZoom.Duration;
        if (newStart >= currentEnd - 0.1)
        {
            newStart = Math.Max(0, currentEnd - 0.1);
        }
        _selectedZoom.StartTime = Math.Round(newStart, 2);
        _selectedZoom.Duration = Math.Max(0.2, Math.Round(currentEnd - _selectedZoom.StartTime, 2));

        RenderZoomPills();
        UpdateZoomSimulation();
        UpdatePlaybackCursor(_currentTimeSeconds);
        ViewModel?.SaveProject();
    }

    private void OnZoomEndChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_isPageLoaded || _isUpdatingZoomInputs || _selectedZoom == null || double.IsNaN(args.NewValue)) return;
        double newEnd = args.NewValue;
        if (newEnd <= _selectedZoom.StartTime + 0.1)
        {
            newEnd = _selectedZoom.StartTime + 0.1;
        }
        _selectedZoom.Duration = Math.Max(0.2, Math.Round(newEnd - _selectedZoom.StartTime, 2));

        RenderZoomPills();
        UpdateZoomSimulation();
        UpdatePlaybackCursor(_currentTimeSeconds);
        ViewModel?.SaveProject();
    }

    private void OnPreviewContainerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Border b && e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            b.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        }
    }

    private void InitializeZoomPreview()
    {
        if (PreviewTargetBox != null)
        {
            _previewTargetTransform = new CompositeTransform
            {
                CenterX = 0,
                CenterY = 0
            };
            PreviewTargetBox.RenderTransformOrigin = new Point(0.5, 0.5);
            PreviewTargetBox.RenderTransform = _previewTargetTransform;
        }

        _zoomPreviewTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60fps
        };
        _zoomPreviewTimer.Tick += OnZoomPreviewTimerTick;
        _zoomPreviewStopwatch.Restart();
        _zoomPreviewTimer.Start();
    }

    private void OnZoomPreviewTimerTick(object? sender, object e)
    {
        if (PanelTabZoom == null || PanelTabZoom.Visibility != Visibility.Visible) return;
        if (_previewTargetTransform == null || PreviewTargetBox == null) return;

        double totalMs = 2800.0;
        double elapsed = _zoomPreviewStopwatch.ElapsedMilliseconds % (long)totalMs;
        double t = elapsed / totalMs; // 0.0 - 1.0

        double rawScale = _selectedZoom != null ? _selectedZoom.Scale : 1.5;
        double targetScale = Math.Clamp(rawScale, 1.0, 2.2);
        double curScale = 1.0;

        if (t < 0.35)
        {
            // Zoom In
            double p = t / 0.35;
            double ease = ZoomEngineService.SolveCubicBezier(p, _bezierP1.X, _bezierP1.Y, _bezierP2.X, _bezierP2.Y);
            curScale = 1.0 + (targetScale - 1.0) * ease;
        }
        else if (t <= 0.70)
        {
            // Hold
            curScale = targetScale;
        }
        else
        {
            // Zoom Out
            double p = (t - 0.70) / 0.30;
            double ease = ZoomEngineService.SolveCubicBezier(p, _bezierP1.X, _bezierP1.Y, _bezierP2.X, _bezierP2.Y);
            curScale = targetScale - (targetScale - 1.0) * ease;
        }

        _previewTargetTransform.ScaleX = curScale;
        _previewTargetTransform.ScaleY = curScale;

        if (TbPreviewScaleIndicator != null)
        {
            TbPreviewScaleIndicator.Text = $"{curScale:F2}x";
        }
    }

    private const double BezierPadX = 14.0;
    private const double BezierPadY = 14.0;

    private (double w, double h) GetBezierGraphSize()
    {
        double totalW = CanvasBezierGraph != null && CanvasBezierGraph.ActualWidth > 40 ? CanvasBezierGraph.ActualWidth : 200.0;
        double totalH = CanvasBezierGraph != null && CanvasBezierGraph.ActualHeight > 40 ? CanvasBezierGraph.ActualHeight : 128.0;
        double w = totalW - (BezierPadX * 2);
        double h = totalH - (BezierPadY * 2);
        return (Math.Max(50, w), Math.Max(50, h));
    }

    private Point NormalizedToCanvasPoint(Point norm, double w, double h)
    {
        double x = BezierPadX + norm.X * w;
        double y = BezierPadY + (1.0 - norm.Y) * h;
        return new Point(x, y);
    }

    private Point CanvasToNormalizedPoint(Point pt, double w, double h)
    {
        double nx = Math.Clamp((pt.X - BezierPadX) / w, 0.0, 1.0);
        double ny = Math.Clamp(1.0 - ((pt.Y - BezierPadY) / h), 0.0, 1.0);
        return new Point(Math.Round(nx, 2), Math.Round(ny, 2));
    }

    private void UpdateBezierGraphVisuals()
    {
        if (CanvasBezierGraph == null || FigBezierCurve == null || SegBezierCurve == null) return;

        var (gw, gh) = GetBezierGraphSize();
        var p0 = NormalizedToCanvasPoint(new Point(0, 0), gw, gh);
        var p3 = NormalizedToCanvasPoint(new Point(1, 1), gw, gh);
        var cp1 = NormalizedToCanvasPoint(_bezierP1, gw, gh);
        var cp2 = NormalizedToCanvasPoint(_bezierP2, gw, gh);

        // Curve figure
        FigBezierCurve.StartPoint = p0;
        SegBezierCurve.Point1 = cp1;
        SegBezierCurve.Point2 = cp2;
        SegBezierCurve.Point3 = p3;

        // Lines to handles
        if (LineHandleP1 != null)
        {
            LineHandleP1.X1 = p0.X; LineHandleP1.Y1 = p0.Y;
            LineHandleP1.X2 = cp1.X; LineHandleP1.Y2 = cp1.Y;
        }
        if (LineHandleP2 != null)
        {
            LineHandleP2.X1 = p3.X; LineHandleP2.Y1 = p3.Y;
            LineHandleP2.X2 = cp2.X; LineHandleP2.Y2 = cp2.Y;
        }

        // Handle thumbs
        if (ThumbP1 != null)
        {
            Canvas.SetLeft(ThumbP1, cp1.X - 7);
            Canvas.SetTop(ThumbP1, cp1.Y - 7);
        }
        if (ThumbP2 != null)
        {
            Canvas.SetLeft(ThumbP2, cp2.X - 7);
            Canvas.SetTop(ThumbP2, cp2.Y - 7);
        }

        // Grid lines
        if (GridLineDiag != null)
        {
            GridLineDiag.X1 = p0.X; GridLineDiag.Y1 = p0.Y;
            GridLineDiag.X2 = p3.X; GridLineDiag.Y2 = p3.Y;
        }
        if (GridLineH1 != null) { GridLineH1.X1 = BezierPadX; GridLineH1.X2 = BezierPadX + gw; GridLineH1.Y1 = GridLineH1.Y2 = BezierPadY + gh * 0.25; }
        if (GridLineH2 != null) { GridLineH2.X1 = BezierPadX; GridLineH2.X2 = BezierPadX + gw; GridLineH2.Y1 = GridLineH2.Y2 = BezierPadY + gh * 0.50; }
        if (GridLineH3 != null) { GridLineH3.X1 = BezierPadX; GridLineH3.X2 = BezierPadX + gw; GridLineH3.Y1 = GridLineH3.Y2 = BezierPadY + gh * 0.75; }

        if (TbBezierCoords != null)
        {
            TbBezierCoords.Text = $"P1: {_bezierP1.X:F2}, {_bezierP1.Y:F2} | P2: {_bezierP2.X:F2}, {_bezierP2.Y:F2}";
        }
    }

    private void OnBezierGraphPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (CanvasBezierGraph == null) return;
        var (gw, gh) = GetBezierGraphSize();
        var cp1 = NormalizedToCanvasPoint(_bezierP1, gw, gh);
        var cp2 = NormalizedToCanvasPoint(_bezierP2, gw, gh);

        var pt = e.GetCurrentPoint(CanvasBezierGraph).Position;
        double dist1 = Math.Sqrt(Math.Pow(pt.X - cp1.X, 2) + Math.Pow(pt.Y - cp1.Y, 2));
        double dist2 = Math.Sqrt(Math.Pow(pt.X - cp2.X, 2) + Math.Pow(pt.Y - cp2.Y, 2));

        if (dist1 <= 22)
        {
            _activeBezierHandle = 1;
            CanvasBezierGraph.CapturePointer(e.Pointer);
            e.Handled = true;
        }
        else if (dist2 <= 22)
        {
            _activeBezierHandle = 2;
            CanvasBezierGraph.CapturePointer(e.Pointer);
            e.Handled = true;
        }
        else if (dist1 < dist2 && dist1 <= 36)
        {
            _activeBezierHandle = 1;
            CanvasBezierGraph.CapturePointer(e.Pointer);
            e.Handled = true;
        }
        else if (dist2 <= 36)
        {
            _activeBezierHandle = 2;
            CanvasBezierGraph.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    private void OnBezierGraphPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (CanvasBezierGraph == null) return;
        var (gw, gh) = GetBezierGraphSize();

        if (_activeBezierHandle != 0)
        {
            var pt = e.GetCurrentPoint(CanvasBezierGraph).Position;
            var normPt = CanvasToNormalizedPoint(pt, gw, gh);

            if (_activeBezierHandle == 1)
            {
                _bezierP1 = normPt;
            }
            else if (_activeBezierHandle == 2)
            {
                _bezierP2 = normPt;
            }

            _isUpdatingBezierUI = true;
            try
            {
                if (CbSelectedZoomEasing != null)
                {
                    CbSelectedZoomEasing.SelectedItem = "Özel (Custom Eğri)";
                }
                if (TbBezierPresetName != null)
                {
                    TbBezierPresetName.Text = "Özel";
                }
            }
            finally
            {
                _isUpdatingBezierUI = false;
            }

            UpdateBezierGraphVisuals();

            if (_selectedZoom != null)
            {
                _selectedZoom.Easing = FormattableString.Invariant($"cubic-bezier({_bezierP1.X:F2}, {_bezierP1.Y:F2}, {_bezierP2.X:F2}, {_bezierP2.Y:F2})");
                UpdateZoomSimulation();
            }
            e.Handled = true;
        }
        else
        {
            var cp1 = NormalizedToCanvasPoint(_bezierP1, gw, gh);
            var cp2 = NormalizedToCanvasPoint(_bezierP2, gw, gh);
            var pt = e.GetCurrentPoint(CanvasBezierGraph).Position;
            double dist1 = Math.Sqrt(Math.Pow(pt.X - cp1.X, 2) + Math.Pow(pt.Y - cp1.Y, 2));
            double dist2 = Math.Sqrt(Math.Pow(pt.X - cp2.X, 2) + Math.Pow(pt.Y - cp2.Y, 2));
            if (dist1 <= 20 || dist2 <= 20)
            {
                this.ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
            }
            else
            {
                this.ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
            }
        }
    }

    private void OnBezierGraphPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_activeBezierHandle != 0 && CanvasBezierGraph != null)
        {
            CanvasBezierGraph.ReleasePointerCapture(e.Pointer);
            _activeBezierHandle = 0;
            if (_selectedZoom != null)
            {
                _selectedZoom.Easing = FormattableString.Invariant($"cubic-bezier({_bezierP1.X:F2}, {_bezierP1.Y:F2}, {_bezierP2.X:F2}, {_bezierP2.Y:F2})");
                ViewModel?.PushHistory();
                ViewModel?.SaveProject();
                UpdateZoomSimulation();
            }
            e.Handled = true;
        }
    }

    private void OnBezierGraphPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_activeBezierHandle == 0)
        {
            this.ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        }
    }

    private void OnSelectedZoomEasingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingBezierUI || CbSelectedZoomEasing == null) return;
        string? selected = CbSelectedZoomEasing.SelectedItem as string;
        if (string.IsNullOrEmpty(selected)) return;

        if (TbBezierPresetName != null)
        {
            TbBezierPresetName.Text = selected;
        }

        switch (selected)
        {
            case "Linear":
                _bezierP1 = new Point(0.0, 0.0);
                _bezierP2 = new Point(1.0, 1.0);
                break;
            case "Quad-Out":
                _bezierP1 = new Point(0.25, 0.46);
                _bezierP2 = new Point(0.45, 0.94);
                break;
            case "Cubic-Out":
                _bezierP1 = new Point(0.215, 0.61);
                _bezierP2 = new Point(0.355, 1.0);
                break;
            case "Quartic-Out":
                _bezierP1 = new Point(0.165, 0.84);
                _bezierP2 = new Point(0.44, 1.0);
                break;
            case "Ease-In-Out":
                _bezierP1 = new Point(0.42, 0.0);
                _bezierP2 = new Point(0.58, 1.0);
                break;
            case "Özel (Custom Eğri)":
                break;
        }

        UpdateBezierGraphVisuals();
        
        // Data updating is now handled by TwoWay binding in SelectedZoomEasing pass-through property
        if (_selectedZoom != null)
        {
            UpdateZoomSimulation();
        }
    }

    private void OnApplyToSelectedZoomsClicked(object sender, RoutedEventArgs e)
    {
        var targets = _selectedZooms.Count > 0 
            ? _selectedZooms.ToList() 
            : (_selectedZoom != null ? new List<ZoomEffect> { _selectedZoom } : new List<ZoomEffect>());

        if (targets.Count == 0 || ViewModel == null) return;

        string targetEasing = _selectedZoom?.Easing ??
            (CbSelectedZoomEasing?.SelectedItem as string ?? FormattableString.Invariant($"cubic-bezier({_bezierP1.X:F2}, {_bezierP1.Y:F2}, {_bezierP2.X:F2}, {_bezierP2.Y:F2})"));
        double targetScale = SelectedZoomSlider != null 
            ? Math.Clamp(SelectedZoomSlider.Value, 1.0, 2.2) 
            : (_selectedZoom != null ? Math.Clamp(_selectedZoom.Scale, 1.0, 2.2) : 1.5);

        ViewModel.PushHistory();

        foreach (var z in targets)
        {
            z.Easing = targetEasing;
            z.Scale = targetScale;
        }

        ViewModel.SaveProject();
        RenderZoomPills();
        UpdateZoomSimulation();

        if (sender is Button btn)
        {
            if (btn.Content is StackPanel sp && sp.Children.OfType<TextBlock>().FirstOrDefault() is TextBlock tb)
            {
                string origText = tb.Text;
                tb.Text = $"Uygulandı! ({targets.Count} Seçili)";
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (s, ev) =>
                {
                    tb.Text = origText;
                    timer.Stop();
                };
                timer.Start();
            }
        }
    }

    private void OnApplyZoomToAllClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.ZoomEffects == null || ViewModel.ZoomEffects.Count == 0) return;

        string targetEasing = _selectedZoom?.Easing ??
            (CbSelectedZoomEasing?.SelectedItem as string ?? FormattableString.Invariant($"cubic-bezier({_bezierP1.X:F2}, {_bezierP1.Y:F2}, {_bezierP2.X:F2}, {_bezierP2.Y:F2})"));
        double targetScale = SelectedZoomSlider != null 
            ? Math.Clamp(SelectedZoomSlider.Value, 1.0, 2.2) 
            : (_selectedZoom != null ? Math.Clamp(_selectedZoom.Scale, 1.0, 2.2) : 1.5);

        ViewModel.PushHistory();

        foreach (var z in ViewModel.ZoomEffects)
        {
            z.Easing = targetEasing;
            z.Scale = targetScale;
        }

        ViewModel.SaveProject();
        RenderZoomPills();
        UpdateZoomSimulation();

        if (sender is Button btn)
        {
            if (btn.Content is StackPanel sp && sp.Children.OfType<TextBlock>().FirstOrDefault() is TextBlock tb)
            {
                string origText = tb.Text;
                tb.Text = "Uygulandı! (Tümüne Aktarıldı)";
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (s, ev) =>
                {
                    tb.Text = origText;
                    timer.Stop();
                };
                timer.Start();
            }
        }
    }

    private void OnGlobalZoomMaxScaleChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_isPageLoaded || ViewModel == null) return;
        ViewModel.ZoomMaxScale = e.NewValue;
        ViewModel.DefaultZoomScale = e.NewValue;

        if (ViewModel.ZoomEffects != null && ViewModel.ZoomEffects.Count > 0)
        {
            foreach (var z in ViewModel.ZoomEffects)
            {
                z.Scale = Math.Round(e.NewValue, 1);
            }
            if (_selectedZoom != null)
            {
                if (TbSelectedZoomFactor != null) TbSelectedZoomFactor.Text = $"{_selectedZoom.Scale:F1}x";
                if (SelectedZoomSlider != null) SelectedZoomSlider.Value = _selectedZoom.Scale;
            }
            RenderZoomPills();
            UpdateZoomSimulation();
            UpdatePlaybackCursor(_currentTimeSeconds);
            ViewModel.SaveProject();
        }
    }

    private void OnGlobalZoomDurationChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_isPageLoaded || ViewModel == null) return;
        ViewModel.ZoomHoldDurationSec = e.NewValue;
        RenderZoomPills();
        UpdateZoomSimulation();
    }

    private void OnSpeedSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        HighlightSpeedButton(btn);

        if (ViewModel != null)
        {
            ViewModel.CursorMovementType = btn.Tag?.ToString() ?? "Medium";
            ViewModel.SaveProject();
        }
    }

    private void HighlightSpeedButton(Button selectedBtn)
    {
        var speedButtons = new[] { BtnSpeedSlow, BtnSpeedMedium, BtnSpeedFast };
        var selectedBg = Application.Current.Resources.TryGetValue("SurfaceContainerLowestBrush", out var bg) && bg is Brush bBg
            ? bBg
            : new SolidColorBrush(Color.FromArgb(255, 13, 14, 21));
        var selectedFg = Application.Current.Resources.TryGetValue("PrimaryAccentBrush", out var fg) && fg is Brush bFg
            ? bFg
            : new SolidColorBrush(Color.FromArgb(255, 192, 193, 255));
        var mutedFg = Application.Current.Resources.TryGetValue("TextMutedBrush", out var mfg) && mfg is Brush bMfg
            ? bMfg
            : new SolidColorBrush(Color.FromArgb(130, 199, 196, 215));

        foreach (var b in speedButtons)
        {
            if (b == null) continue;
            bool isSelected = b == selectedBtn;
            b.Background = isSelected ? selectedBg : new SolidColorBrush(Colors.Transparent);
            b.Foreground = isSelected ? selectedFg : mutedFg;
        }
    }

    private void SyncMotionSettings()
    {
        if (ViewModel == null) return;
        var movementType = ViewModel.CursorMovementType ?? "Medium";
        Button? target = movementType.ToLowerInvariant() switch
        {
            "slow" => BtnSpeedSlow,
            "fast" => BtnSpeedFast,
            _ => BtnSpeedMedium
        };

        if (target != null)
        {
            HighlightSpeedButton(target);
        }
    }

    private void OnBgColorSelected(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border border && border.Tag is string tag && ViewModel != null)
        {
            switch (tag)
            {
                case "Black":
                    ViewModel.CanvasBackground = "#0D0E15";
                    ApplyBackgroundGradient("#0D0E15", "#05050A");
                    break;
                case "DarkGray":
                    ViewModel.CanvasBackground = "#1E1F27";
                    ApplyBackgroundGradient("#1E1F27", "#101015");
                    break;
                case "Purple":
                    ViewModel.CanvasBackground = "#1E1B4B";
                    ApplyBackgroundGradient("#1E1B4B", "#0F0D25");
                    break;
                case "Navy":
                    ViewModel.CanvasBackground = "#0C4A6E";
                    ApplyBackgroundGradient("#0C4A6E", "#062537");
                    break;
                case "Emerald":
                    ViewModel.CanvasBackground = "#064E3B";
                    ApplyBackgroundGradient("#064E3B", "#03271D");
                    break;
                case "Crimson":
                    ViewModel.CanvasBackground = "#4C0519";
                    ApplyBackgroundGradient("#4C0519", "#26020C");
                    break;
                case "Grad1":
                    ViewModel.CanvasBackground = "linear-gradient(135deg, #C0C1FF, #A078FF)";
                    ApplyBackgroundGradient("#C0C1FF", "#A078FF");
                    break;
                case "Grad2":
                    ViewModel.CanvasBackground = "linear-gradient(135deg, #3B82F6, #9333EA)";
                    ApplyBackgroundGradient("#3B82F6", "#9333EA");
                    break;
                case "Grad3":
                    ViewModel.CanvasBackground = "linear-gradient(135deg, #EC4899, #F43F5E)";
                    ApplyBackgroundGradient("#EC4899", "#F43F5E");
                    break;
                case "Grad4":
                    ViewModel.CanvasBackground = "linear-gradient(135deg, #10B981, #06B6D4)";
                    ApplyBackgroundGradient("#10B981", "#06B6D4");
                    break;
                case "Grad5":
                    ViewModel.CanvasBackground = "linear-gradient(135deg, #F59E0B, #EF4444)";
                    ApplyBackgroundGradient("#F59E0B", "#EF4444");
                    break;
                case "Grad6":
                    ViewModel.CanvasBackground = "linear-gradient(135deg, #6366F1, #D946EF)";
                    ApplyBackgroundGradient("#6366F1", "#D946EF");
                    break;
            }
            ViewModel.SaveProject();
        }
    }

    private void ApplyBackgroundGradient(string hex1, string hex2)
    {
        if (PreviewBackgroundLayer != null)
        {
            var brush = new Microsoft.UI.Xaml.Media.LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1)
            };
            brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = ParseColor(hex1), Offset = 0 });
            brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = ParseColor(hex2), Offset = 1 });
            PreviewBackgroundLayer.Background = brush;
        }
    }

    private Windows.UI.Color ParseColor(string hex)
    {
        hex = hex.Replace("#", "");
        byte a = 255, r = 255, g = 255, b = 255;
        if (hex.Length == 8)
        {
            a = Convert.ToByte(hex.Substring(0, 2), 16);
            r = Convert.ToByte(hex.Substring(2, 2), 16);
            g = Convert.ToByte(hex.Substring(4, 2), 16);
            b = Convert.ToByte(hex.Substring(6, 2), 16);
        }
        else if (hex.Length == 6)
        {
            r = Convert.ToByte(hex.Substring(0, 2), 16);
            g = Convert.ToByte(hex.Substring(2, 2), 16);
            b = Convert.ToByte(hex.Substring(4, 2), 16);
        }
        return Windows.UI.Color.FromArgb(a, r, g, b);
    }

    private void AddZoomEffectAt(double timeSec)
    {
        if (ViewModel == null) return;
        ViewModel.PushHistory();

        var curPt = ZoomEngineService.GetInterpolatedCursorPosition(ViewModel.MouseMoves, timeSec);
        double natW = _naturalVideoWidth > 0 ? _naturalVideoWidth : (ViewModel.VideoWidth > 0 ? ViewModel.VideoWidth : 1920.0);
        double natH = _naturalVideoHeight > 0 ? _naturalVideoHeight : (ViewModel.VideoHeight > 0 ? ViewModel.VideoHeight : 1080.0);

        double targetX = curPt.HasValue ? curPt.Value.X : (natW / 2.0);
        double targetY = curPt.HasValue ? curPt.Value.Y : (natH / 2.0);

        double defaultScale = ViewModel.DefaultZoomScale > 0 ? ViewModel.DefaultZoomScale : 1.5;
        double startCandidate = Math.Round(timeSec, 2);
        double durationCandidate = 2.5;

        // Çakışma önleme: Yeni zoom var olan bir zoom'un üzerine çakışmasın
        if (ViewModel.ZoomEffects != null && ViewModel.ZoomEffects.Count > 0)
        {
            var sorted = ViewModel.ZoomEffects.OrderBy(z => z.StartTime).ToList();
            var inside = sorted.FirstOrDefault(z => startCandidate >= z.StartTime && startCandidate < z.StartTime + z.Duration);
            if (inside != null)
            {
                startCandidate = Math.Round(inside.StartTime + inside.Duration + 0.05, 2);
            }
            var next = sorted.FirstOrDefault(z => z.StartTime > startCandidate);
            if (next != null && startCandidate + durationCandidate > next.StartTime)
            {
                durationCandidate = Math.Max(0.5, Math.Round(next.StartTime - startCandidate - 0.05, 2));
            }
        }

        var newZoom = new ZoomEffect
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = $"Zoom {(ViewModel?.ZoomEffects?.Count ?? 0) + 1}",
            StartTime = startCandidate,
            Duration = durationCandidate,
            Scale = Math.Clamp(defaultScale, 1.0, 2.2),
            TargetX = Math.Round(targetX, 1),
            TargetY = Math.Round(targetY, 1),
            Easing = !string.IsNullOrEmpty(ViewModel?.ZoomEasingFunction) ? ViewModel.ZoomEasingFunction : "Cubic-Out"
        };
        ViewModel?.ZoomEffects?.Add(newZoom);
        SelectZoom(newZoom);
    }

    private void OnDeleteZoomClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedZooms.Count > 0)
        {
            DeleteSelectedZooms();
        }
        else if (_selectedZoom != null)
        {
            DeleteZoom(_selectedZoom);
        }
        else if ((ViewModel?.SelectedClipIds.Count > 0) || !string.IsNullOrEmpty(ViewModel?.SelectedClipId))
        {
            ViewModel.DeleteSelected();
            RenderTimeline();
            UpdateGapBlackScreen();
        }
    }

    private void OnUndoClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.Undo();
        UpdateFromViewModel();
        RenderTimeline();
        UpdateZoomSimulation();
        UpdateGapBlackScreen();
    }

    private void OnRedoClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.Redo();
        UpdateFromViewModel();
        RenderTimeline();
        UpdateZoomSimulation();
        UpdateGapBlackScreen();
    }

    private void OnSplitClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.CutAtPlayhead();
        RenderTimeline();
        UpdateGapBlackScreen();
    }

    private void OnVideoTrackMuteClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleMute("video");
        bool isMuted = ViewModel.VideoTrack.Muted;
        if (_sysPlayer != null)
        {
            _sysPlayer.Volume = isMuted ? 0 : (ViewModel.SysVolume / 100.0);
        }
        if (VideoPlayer?.MediaPlayer != null)
        {
            VideoPlayer.MediaPlayer.Volume = isMuted ? 0 : (ViewModel.SysVolume / 100.0);
        }
        UpdateVideoAudioIconState();
    }

    private void OnAudioTrackMuteClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleMute("mic");
        bool isMuted = ViewModel.MicTrack.Muted;
        if (_micPlayer != null)
        {
            _micPlayer.Volume = isMuted ? 0 : (ViewModel.MicVolume / 100.0);
        }
        UpdateAudioIconState();
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

    private void OnMasterVolumeSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_isPageLoaded) return;
        double vol = e.NewValue;
        if (TbVolumePercent != null) TbVolumePercent.Text = $"{(int)vol}%";

        float ratio = (float)(vol / 100.0);
        string glyph;
        if (ratio >= 0.75f)
        {
            glyph = "\uE995"; // Speaker with 3 waves (High)
        }
        else if (ratio >= 0.30f)
        {
            glyph = "\uE994"; // Speaker with 2 waves (Medium)
        }
        else if (ratio > 0.0f)
        {
            glyph = "\uE993"; // Speaker with 1 wave (Low)
        }
        else
        {
            glyph = "\uE74F"; // Speaker Mute / X
        }

        if (MuteIcon != null)
        {
            MuteIcon.Glyph = glyph;
            MuteIcon.Foreground = new SolidColorBrush(ratio > 0
                ? Color.FromArgb(255, 230, 230, 245)
                : Color.FromArgb(255, 239, 68, 68));
        }

        if (FlyoutMuteIcon != null)
        {
            FlyoutMuteIcon.Glyph = glyph;
            FlyoutMuteIcon.Foreground = new SolidColorBrush(ratio > 0
                ? Color.FromArgb(255, 192, 193, 255)
                : Color.FromArgb(255, 239, 68, 68));
        }

        if (_micPlayer != null && ViewModel != null && !ViewModel.MicMuted)
        {
            _micPlayer.Volume = ratio * (ViewModel.MicVolume / 100.0);
        }
        if (_sysPlayer != null && ViewModel != null)
        {
            _sysPlayer.Volume = ratio * (ViewModel.SysVolume / 100.0);
        }
        if (VideoPlayer?.MediaPlayer != null && ViewModel != null && !ViewModel.VideoTrack.Muted)
        {
            VideoPlayer.MediaPlayer.Volume = ratio;
        }
    }

    private void OnFlyoutMuteToggleClicked(object sender, RoutedEventArgs e)
    {
        if (MasterVolumeSlider == null) return;
        if (MasterVolumeSlider.Value > 0)
        {
            _savedMasterVolume = MasterVolumeSlider.Value;
            MasterVolumeSlider.Value = 0;
        }
        else
        {
            MasterVolumeSlider.Value = _savedMasterVolume > 0 ? _savedMasterVolume : 100;
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
        ShowExportSettingsDialog();
    }

    private void ShowExportSettingsDialog()
    {
        PausePlayback();
        ViewModel?.SaveProject();

        ApplyExportModalLocalization();

        try
        {
            var settingsService = App.Current.Services.GetRequiredService<SettingsService>();
            string exportDir = settingsService.Current?.ExportLocation ?? string.Empty;
            if (string.IsNullOrWhiteSpace(exportDir))
            {
                exportDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ScreenPowerPro Exports");
            }
            if (!Directory.Exists(exportDir))
            {
                Directory.CreateDirectory(exportDir);
            }

            string projName = !string.IsNullOrEmpty(ViewModel?.ProjectName)
                ? ViewModel.ProjectName
                : (!string.IsNullOrEmpty(ViewModel?.ProjectDir) ? System.IO.Path.GetFileName(ViewModel.ProjectDir) : "Recording");

            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                projName = projName.Replace(c, '_');
            }

            TxtExportPath.Text = System.IO.Path.Combine(exportDir, $"{projName}.mp4");
        }
        catch
        {
            TxtExportPath.Text = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ScreenRecording.mp4");
        }

        if (_naturalVideoHeight > 0)
        {
            if (ExportRes4K != null) ExportRes4K.IsEnabled = _naturalVideoHeight >= 2000;
            if (ExportRes2K != null) ExportRes2K.IsEnabled = _naturalVideoHeight >= 1400;
            if (ExportRes1080p != null) ExportRes1080p.IsEnabled = _naturalVideoHeight >= 1000;
            if (ExportRes720p != null) ExportRes720p.IsEnabled = true;

            if (CmbExportResolution.SelectedItem is ComboBoxItem selectedItem && !selectedItem.IsEnabled)
            {
                if (ExportRes1080p != null && ExportRes1080p.IsEnabled) CmbExportResolution.SelectedItem = ExportRes1080p;
                else if (ExportRes720p != null) CmbExportResolution.SelectedItem = ExportRes720p;
            }
            
            bool hasDisabled = false;
            if (ExportRes4K != null && !ExportRes4K.IsEnabled) hasDisabled = true;
            if (ExportRes2K != null && !ExportRes2K.IsEnabled) hasDisabled = true;
            
            if (TbExportResInfo != null)
            {
                TbExportResInfo.Visibility = hasDisabled ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        UpdateExportSummaryBadge();
        ExportModalOverlay.Visibility = Visibility.Visible;
    }

    private void ApplyExportModalLocalization()
    {
        if (TbExportModalTitle != null) TbExportModalTitle.Text = _loc["Export_Dialog_Title"];
        if (TbExportModalSubtitle != null) TbExportModalSubtitle.Text = _loc["Export_Dialog_Subtitle"];
        if (TbExportResLabel != null) TbExportResLabel.Text = _loc["Export_Dialog_Resolution"];
        if (TbExportFpsLabel != null) TbExportFpsLabel.Text = _loc["Export_Dialog_Fps"];
        if (TbExportFormatLabel != null) TbExportFormatLabel.Text = _loc["Export_Dialog_Format"];
        if (TbExportLocationLabel != null) TbExportLocationLabel.Text = _loc["Export_Dialog_Location"];
        if (TbBtnBrowseText != null) TbBtnBrowseText.Text = _loc["Export_Dialog_Browse"];
        if (TbBtnCancelExportText != null) TbBtnCancelExportText.Text = _loc["Export_Dialog_Cancel"];
        if (TbBtnConfirmExportText != null) TbBtnConfirmExportText.Text = _loc["Export_Dialog_Start"];
    }

    private void OnCloseExportDialogClicked(object sender, RoutedEventArgs e)
    {
        ExportModalOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnExportBackdropTapped(object sender, TappedRoutedEventArgs e)
    {
        ExportModalOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnExportSettingChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateExportSummaryBadge();
    }

    private void UpdateExportSummaryBadge()
    {
        if (TbExportSummaryBadge == null || CmbExportResolution == null || CmbExportFps == null) return;

        string resTag = (CmbExportResolution.SelectedItem as ComboBoxItem)?.Tag as string ?? "1080p";
        string resText = resTag switch
        {
            "4K" => "3840×2160 (4K)",
            "2K" => "2560×1440 (2K)",
            "720p" => "1280×720 (HD)",
            _ => "1920×1080 (FHD)"
        };

        string fpsTag = (CmbExportFps.SelectedItem as ComboBoxItem)?.Tag as string ?? "60";
        TbExportSummaryBadge.Text = $"{resText} • {fpsTag} FPS • MP4 (H.264)";
    }

    private async void OnBrowseExportPathClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.VideosLibrary;
            picker.FileTypeChoices.Add("MP4 Video (*.mp4)", new List<string> { ".mp4" });

            string current = TxtExportPath.Text;
            if (!string.IsNullOrWhiteSpace(current))
            {
                picker.SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(current);
            }
            else
            {
                picker.SuggestedFileName = !string.IsNullOrEmpty(ViewModel?.ProjectName)
                    ? ViewModel.ProjectName
                    : "ScreenRecording";
            }

            IntPtr hwnd = MainWindow.CurrentInstance?.GetWindowHandle() ?? IntPtr.Zero;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file != null && !string.IsNullOrWhiteSpace(file.Path))
            {
                TxtExportPath.Text = file.Path;
                UpdateExportSummaryBadge();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Export] FileSavePicker error: {ex.Message}");
        }
    }

    private void OnConfirmExportClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.SaveProject();

        string outPath = TxtExportPath.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(outPath))
        {
            string exportDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "ScreenPowerPro Exports");
            string projName = !string.IsNullOrEmpty(ViewModel?.ProjectDir) ? System.IO.Path.GetFileName(ViewModel.ProjectDir) : "Recording";
            outPath = System.IO.Path.Combine(exportDir, $"{projName}.mp4");
        }

        if (!outPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            outPath += ".mp4";
        }

        int targetWidth = 1920;
        int targetHeight = 1080;
        string resTag = (CmbExportResolution.SelectedItem as ComboBoxItem)?.Tag as string ?? "1080p";
        switch (resTag)
        {
            case "4K":
                targetWidth = 3840;
                targetHeight = 2160;
                break;
            case "2K":
                targetWidth = 2560;
                targetHeight = 1440;
                break;
            case "1080p":
                targetWidth = 1920;
                targetHeight = 1080;
                break;
            case "720p":
                targetWidth = 1280;
                targetHeight = 720;
                break;
        }

        int fps = 60;
        string fpsTag = (CmbExportFps.SelectedItem as ComboBoxItem)?.Tag as string ?? "60";
        if (int.TryParse(fpsTag, out int parsedFps))
        {
            fps = parsedFps;
        }

        var options = new ScreenPowerPro.Models.ExportOptions
        {
            ProjectDir = ViewModel?.ProjectDir ?? string.Empty,
            OutputPath = outPath,
            TargetWidth = targetWidth,
            TargetHeight = targetHeight,
            TargetFps = fps,
            Format = "mp4",
            ResolutionLabel = $"{targetWidth}×{targetHeight}"
        };

        ExportModalOverlay.Visibility = Visibility.Collapsed;
        MainWindow.CurrentInstance?.NavigateToExport(options);
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

    // =========================================================================
    // DYNAMIC CURSOR ENGINE, 10 SHAPES & CLICK EFFECTS
    // =========================================================================

    private static Geometry ArrowGeometry => ParseGeometry("M 0,0 L 0,16 L 4.5,12.5 L 8.5,20 L 11,18.5 L 7,11.5 L 13,11.5 Z");
    private static Geometry CrosshairGeometry => ParseGeometry("M 10,0 L 10,6 M 10,14 L 10,20 M 0,10 L 6,10 M 14,10 L 20,10");
    private static Geometry IBeamGeometry => ParseGeometry("M 3,0 L 11,0 M 7,0 L 7,18 M 3,18 L 11,18");

    private static Geometry ParseGeometry(string pathData)
    {
        try
        {
            return (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), pathData);
        }
        catch
        {
            return (Geometry)Microsoft.UI.Xaml.Markup.XamlReader.Load($"<Geometry xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">{pathData}</Geometry>");
        }
    }

    private void InitializeCursorOverlay()
    {
        if (CursorOverlayCanvas == null) return;
        CursorOverlayCanvas.Children.Clear();

        // Motion blur ghost trails
        _motionBlurGhosts.Clear();
        for (int i = 0; i < 2; i++)
        {
            var ghost = new Border
            {
                Width = 24,
                Height = 24,
                Opacity = 0,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0, 0),
                RenderTransform = new CompositeTransform()
            };
            _motionBlurGhosts.Add(ghost);
            CursorOverlayCanvas.Children.Add(ghost);
        }

        _cursorVisualRoot = new Grid
        {
            Width = 60,
            Height = 60,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0, 0)
        };

        _cursorTransform = new CompositeTransform { ScaleX = 1.0, ScaleY = 1.0 };
        _cursorVisualRoot.RenderTransform = _cursorTransform;

        // Glow Border for spotlight & highlighter
        _cursorGlowBorder = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(-12, -12, 0, 0),
            Visibility = Visibility.Collapsed
        };
        _cursorVisualRoot.Children.Add(_cursorGlowBorder);

        // Path Icon for arrow, crosshair, ibeam shapes
        _cursorPathIcon = new PathIcon
        {
            Width = 24,
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Data = ArrowGeometry,
            Foreground = new SolidColorBrush(Colors.White)
        };
        _cursorVisualRoot.Children.Add(_cursorPathIcon);

        // Font Icon for hand / icons
        _cursorFontIcon = new FontIcon
        {
            FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed
        };
        _cursorVisualRoot.Children.Add(_cursorFontIcon);

        // Dot / circle for circle_dot shape
        _cursorDotEllipse = new Ellipse
        {
            Width = 18,
            Height = 18,
            Stroke = new SolidColorBrush(Colors.White),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed
        };
        _cursorVisualRoot.Children.Add(_cursorDotEllipse);

        CursorOverlayCanvas.Children.Add(_cursorVisualRoot);

        UpdateCursorVisual();
        UpdateCursorScale(ViewModel.CursorSize > 0 ? ViewModel.CursorSize / 100.0 : 1.0);
        UpdatePlaybackCursor(_currentTimeSeconds);
    }

    private void SyncCursorButtons()
    {
        var cursorButtons = new[]
        {
            BtnCursorDefault, BtnCursorBlack, BtnCursorHand, BtnCursorCrosshair, BtnCursorDot,
            BtnCursorIBeam, BtnCursorSpotlight, BtnCursorHighlightYellow, BtnCursorCyan, BtnCursorPurple
        };

        var selectedBtn = cursorButtons.FirstOrDefault(b => (string?)b?.Tag == ViewModel.CursorStyle) ?? BtnCursorDefault;
        if (selectedBtn != null)
        {
            HighlightButtonChoice(cursorButtons, selectedBtn);
        }

        var effectButtons = new[]
        {
            BtnClickNone, BtnClickDefault, BtnClickRipple, BtnClickRing,
            BtnClickDiffusion, BtnClickSpotlight, BtnClickSparkle, BtnClickFirework, BtnClickChristmas
        };
        var selectedEffect = effectButtons.FirstOrDefault(b => (string?)b?.Tag == ViewModel.ClickEffect) ?? BtnClickDefault;
        if (selectedEffect != null)
        {
            HighlightButtonChoice(effectButtons, selectedEffect);
        }

        if (CursorSizeSlider != null)
        {
            double val = ViewModel.CursorSize > 0 ? ViewModel.CursorSize / 100.0 : 1.0;
            CursorSizeSlider.Value = Math.Clamp(val, 0.2, 5.0);
            if (TbCursorSizeVal != null) TbCursorSizeVal.Text = $"{CursorSizeSlider.Value:F1}x";
        }
        if (TsShowCursor != null) TsShowCursor.IsOn = ViewModel.CursorVisible;
        if (TsCursorSound != null) TsCursorSound.IsOn = ViewModel.CursorClickSound;
        if (PanelClickSoundDetails != null) PanelClickSoundDetails.Visibility = ViewModel.CursorClickSound ? Visibility.Visible : Visibility.Collapsed;
        if (SliderCursorClickVol != null) SliderCursorClickVol.Value = ViewModel.CursorClickVolume;
        if (TbCursorSoundVolText != null) TbCursorSoundVolText.Text = $"{(int)ViewModel.CursorClickVolume}%";
        if (TsHideIdleCursor != null) TsHideIdleCursor.IsOn = ViewModel.HideCursorWhenIdle;

        UpdateCursorVisual();
        UpdateCursorScale(ViewModel.CursorSize > 0 ? ViewModel.CursorSize / 100.0 : 1.0);
    }

    private void UpdateCursorVisual()
    {
        if (_cursorVisualRoot == null || _cursorPathIcon == null || _cursorFontIcon == null || _cursorDotEllipse == null || _cursorGlowBorder == null)
            return;

        string style = ViewModel?.CursorStyle ?? "default";

        _cursorGlowBorder.Visibility = Visibility.Collapsed;
        _cursorDotEllipse.Visibility = Visibility.Collapsed;
        _cursorFontIcon.Visibility = Visibility.Collapsed;
        _cursorPathIcon.Visibility = Visibility.Visible;

        switch (style)
        {
            case "arrow_black":
                _cursorPathIcon.Data = ArrowGeometry;
                _cursorPathIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 30, 31, 39));
                break;

            case "pointer_hand":
                _cursorPathIcon.Visibility = Visibility.Collapsed;
                _cursorFontIcon.Visibility = Visibility.Visible;
                _cursorFontIcon.Glyph = "\uE962";
                _cursorFontIcon.Foreground = new SolidColorBrush(Colors.White);
                break;

            case "crosshair":
                _cursorPathIcon.Data = CrosshairGeometry;
                _cursorPathIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 240, 255));
                break;

            case "circle_dot":
                _cursorPathIcon.Visibility = Visibility.Collapsed;
                _cursorDotEllipse.Visibility = Visibility.Visible;
                break;

            case "ibeam":
                _cursorPathIcon.Data = IBeamGeometry;
                _cursorPathIcon.Foreground = new SolidColorBrush(Colors.White);
                break;

            case "spotlight":
                _cursorPathIcon.Data = ArrowGeometry;
                _cursorPathIcon.Foreground = new SolidColorBrush(Colors.White);
                _cursorGlowBorder.Visibility = Visibility.Visible;
                _cursorGlowBorder.Width = 60;
                _cursorGlowBorder.Height = 60;
                _cursorGlowBorder.CornerRadius = new CornerRadius(30);
                _cursorGlowBorder.Margin = new Thickness(-20, -20, 0, 0);
                _cursorGlowBorder.Background = new SolidColorBrush(Color.FromArgb(60, 192, 193, 255));
                _cursorGlowBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(160, 192, 193, 255));
                _cursorGlowBorder.BorderThickness = new Thickness(1.5);
                break;

            case "highlight_yellow":
                _cursorPathIcon.Data = ArrowGeometry;
                _cursorPathIcon.Foreground = new SolidColorBrush(Colors.White);
                _cursorGlowBorder.Visibility = Visibility.Visible;
                _cursorGlowBorder.Width = 44;
                _cursorGlowBorder.Height = 44;
                _cursorGlowBorder.CornerRadius = new CornerRadius(22);
                _cursorGlowBorder.Margin = new Thickness(-12, -12, 0, 0);
                _cursorGlowBorder.Background = new SolidColorBrush(Color.FromArgb(100, 255, 230, 0));
                _cursorGlowBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(200, 255, 230, 0));
                _cursorGlowBorder.BorderThickness = new Thickness(1.5);
                break;

            case "arrow_cyan":
                _cursorPathIcon.Data = ArrowGeometry;
                _cursorPathIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 229, 255));
                break;

            case "arrow_purple":
                _cursorPathIcon.Data = ArrowGeometry;
                _cursorPathIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 192, 193, 255));
                break;

            case "default":
            default:
                _cursorPathIcon.Data = ArrowGeometry;
                _cursorPathIcon.Foreground = new SolidColorBrush(Colors.White);
                break;
        }

        UpdateCursorVisibility();
    }

    private void UpdateCursorScale(double scale)
    {
        if (_cursorTransform != null)
        {
            double safeScale = Math.Clamp(scale, 0.2, 5.0);
            _cursorTransform.ScaleX = safeScale;
            _cursorTransform.ScaleY = safeScale;
        }
    }

    private void UpdateCursorPosition(double x, double y)
    {
        if (_cursorVisualRoot == null || CursorOverlayCanvas == null) return;

        // Motion blur ghost update
        if (ViewModel.MotionBlur && ViewModel.CursorMotionBlurAmount > 10 && _motionBlurGhosts.Count >= 2)
        {
            double blurFactor = ViewModel.CursorMotionBlurAmount / 100.0;
            Canvas.SetLeft(_motionBlurGhosts[1], _lastKnownCursorPoint.X);
            Canvas.SetTop(_motionBlurGhosts[1], _lastKnownCursorPoint.Y);
            _motionBlurGhosts[1].Opacity = blurFactor * 0.2;

            Canvas.SetLeft(_motionBlurGhosts[0], (_lastKnownCursorPoint.X + x) / 2.0);
            Canvas.SetTop(_motionBlurGhosts[0], (_lastKnownCursorPoint.Y + y) / 2.0);
            _motionBlurGhosts[0].Opacity = blurFactor * 0.4;
        }
        else
        {
            foreach (var ghost in _motionBlurGhosts) ghost.Opacity = 0;
        }

        _lastKnownCursorPoint = new Point(x, y);
        _currentCursorX = x;
        _currentCursorY = y;

        Canvas.SetLeft(_cursorVisualRoot, x);
        Canvas.SetTop(_cursorVisualRoot, y);

        UpdateCursorVisibility();
    }

    private void UpdateCursorVisibility()
    {
        if (_cursorVisualRoot == null) return;

        if (!ViewModel.CursorVisible)
        {
            _cursorVisualRoot.Visibility = Visibility.Collapsed;
            return;
        }

        _cursorVisualRoot.Visibility = Visibility.Visible;

        if (ViewModel.HideCursorWhenIdle)
        {
            if (_isPlaying && (_currentTimeSeconds - _lastMouseMoveTimestamp > 1.5) && _lastMouseMoveTimestamp > 0)
            {
                _cursorVisualRoot.Opacity = 0;
                return;
            }
        }

        _cursorVisualRoot.Opacity = 1.0;
    }

    /// <summary>
    /// Verilen saniye zamanına karşılık gelen fare konumunu ikili arama (binary search)
    /// ve ardışık örnekler arası doğrusal enterpolasyonla O(log N) hızında pürüzsüz hesaplar.
    /// </summary>
    private Point? GetCursorPositionAtTime(IReadOnlyList<MouseMoveEvent> moves, double currentSec)
    {
        if (moves == null || moves.Count == 0)
            return null;

        if (currentSec <= moves[0].Timestamp)
        {
            return new Point(moves[0].X, moves[0].Y);
        }

        if (currentSec >= moves[^1].Timestamp)
        {
            return new Point(moves[^1].X, moves[^1].Y);
        }

        int low = 0;
        int high = moves.Count - 1;
        int idx = 0;

        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (moves[mid].Timestamp <= currentSec)
            {
                idx = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        if (idx >= moves.Count - 1)
        {
            return new Point(moves[^1].X, moves[^1].Y);
        }

        var m1 = moves[idx];
        var m2 = moves[idx + 1];
        double dt = m2.Timestamp - m1.Timestamp;

        if (dt > 0.0001 && currentSec >= m1.Timestamp && currentSec <= m2.Timestamp)
        {
            double t = (currentSec - m1.Timestamp) / dt;
            double x = m1.X + (m2.X - m1.X) * t;
            double y = m1.Y + (m2.Y - m1.Y) * t;
            return new Point(x, y);
        }

        return new Point(m1.X, m1.Y);
    }

    private void UpdatePlaybackCursor(double currentSec)
    {
        if (CursorOverlayCanvas == null || ViewModel == null) return;

        // VideoTransform ile CursorCanvasTransform senkronizasyonu
        if (CursorCanvasTransform != null && VideoTransform != null)
        {
            CursorCanvasTransform.ScaleX = VideoTransform.ScaleX;
            CursorCanvasTransform.ScaleY = VideoTransform.ScaleY;
            CursorCanvasTransform.TranslateX = VideoTransform.TranslateX;
            CursorCanvasTransform.TranslateY = VideoTransform.TranslateY;

            if (_cursorTransform != null)
            {
                double baseScale = ViewModel.CursorSize > 0 ? ViewModel.CursorSize / 100.0 : 1.0;
                double invScale = VideoTransform.ScaleX > 0 ? (1.0 / VideoTransform.ScaleX) : 1.0;
                _cursorTransform.ScaleX = baseScale * invScale;
                _cursorTransform.ScaleY = baseScale * invScale;
            }
        }

        var moves = ViewModel.MouseMoves;
        var clicks = ViewModel.MouseClicks;

        double natW = _naturalVideoWidth > 0 ? _naturalVideoWidth : (ViewModel.VideoWidth > 0 ? ViewModel.VideoWidth : 1920.0);
        double natH = _naturalVideoHeight > 0 ? _naturalVideoHeight : (ViewModel.VideoHeight > 0 ? ViewModel.VideoHeight : 1080.0);
        var vRect = GetVideoContentRect();

        if (moves != null && moves.Count > 0)
        {
            var pt = GetCursorPositionAtTime(moves, currentSec);
            if (pt.HasValue)
            {
                double normX = Math.Clamp(pt.Value.X / natW, 0, 1);
                double normY = Math.Clamp(pt.Value.Y / natH, 0, 1);

                double canvasX = vRect.X + (normX * vRect.Width);
                double canvasY = vRect.Y + (normY * vRect.Height);

                UpdateCursorPosition(canvasX, canvasY);
                _lastMouseMoveTimestamp = currentSec;
            }
        }
        else
        {
            UpdateCursorVisibility();
        }

        if (_isPlaying && clicks != null && clicks.Count > 0)
        {
            // Saniye cinsinden tıklama penceresi (80ms aralık)
            double windowStart = currentSec - 0.07;
            double windowEnd = currentSec + 0.02;

            var recentClicks = clicks.Where(c =>
                c.Timestamp >= windowStart &&
                c.Timestamp <= windowEnd &&
                (c.Type == "left_down" || c.Type == "right_down") &&
                Math.Abs(c.Timestamp - _lastTriggeredClickTimestamp) > 0.05);

            foreach (var c in recentClicks)
            {
                _lastTriggeredClickTimestamp = c.Timestamp;
                double normX = Math.Clamp(c.X / natW, 0, 1);
                double normY = Math.Clamp(c.Y / natH, 0, 1);
                PlayClickEffectAt(vRect.X + (normX * vRect.Width), vRect.Y + (normY * vRect.Height), ViewModel.ClickEffect);
            }
        }
    }

    private void PlayClickEffectAt(double x, double y, string effect)
    {
        if (CursorOverlayCanvas == null || string.IsNullOrEmpty(effect) || effect == "none") return;

        if (ViewModel.CursorClickSound)
        {
            try
            {
                ClickSoundService.Instance.PlaySound(ViewModel.CursorClickSoundFile, ViewModel.CursorClickVolume);
            }
            catch { }
        }

        if (effect == "sparkle" || effect == "firework" || effect == "christmas")
        {
            SpawnParticles(x, y, effect);
        }
        else
        {
            SpawnRingEffect(x, y, effect);
        }
    }

    private void SpawnRingEffect(double x, double y, string effect)
    {
        if (CursorOverlayCanvas == null) return;

        var ring = new Ellipse
        {
            Width = 24,
            Height = 24,
            RenderTransformOrigin = new Point(0.5, 0.5),
            IsHitTestVisible = false
        };

        var transform = new CompositeTransform { ScaleX = 0.5, ScaleY = 0.5 };
        ring.RenderTransform = transform;

        Color color;
        double targetScale = 2.5;

        switch (effect)
        {
            case "ripple":
                color = Color.FromArgb(255, 192, 193, 255);
                ring.Fill = new SolidColorBrush(Color.FromArgb(40, 192, 193, 255));
                ring.Stroke = new SolidColorBrush(color);
                ring.StrokeThickness = 1.5;
                targetScale = 3.5;
                break;
            case "ring":
                color = Color.FromArgb(255, 0, 229, 255);
                ring.Stroke = new SolidColorBrush(color);
                ring.StrokeThickness = 2.5;
                targetScale = 3.0;
                break;
            case "diffusion":
                color = Color.FromArgb(180, 255, 230, 0);
                ring.Fill = new SolidColorBrush(Color.FromArgb(60, 255, 230, 0));
                ring.Stroke = new SolidColorBrush(color);
                ring.StrokeThickness = 1;
                targetScale = 4.0;
                break;
            case "spotlight":
                color = Color.FromArgb(220, 255, 255, 255);
                ring.Fill = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
                ring.Stroke = new SolidColorBrush(color);
                targetScale = 2.8;
                break;
            case "default":
            default:
                color = Color.FromArgb(255, 192, 193, 255);
                ring.Fill = new SolidColorBrush(Color.FromArgb(60, 192, 193, 255));
                ring.Stroke = new SolidColorBrush(color);
                ring.StrokeThickness = 1.5;
                targetScale = 2.2;
                break;
        }

        Canvas.SetLeft(ring, x - 12);
        Canvas.SetTop(ring, y - 12);
        CursorOverlayCanvas.Children.Add(ring);

        var sb = new Storyboard();
        var animScaleX = new DoubleAnimation { From = 0.5, To = targetScale, Duration = TimeSpan.FromMilliseconds(350) };
        var animScaleY = new DoubleAnimation { From = 0.5, To = targetScale, Duration = TimeSpan.FromMilliseconds(350) };
        var animOpacity = new DoubleAnimation { From = 1.0, To = 0.0, Duration = TimeSpan.FromMilliseconds(350) };

        Storyboard.SetTarget(animScaleX, transform);
        Storyboard.SetTargetProperty(animScaleX, nameof(CompositeTransform.ScaleX));

        Storyboard.SetTarget(animScaleY, transform);
        Storyboard.SetTargetProperty(animScaleY, nameof(CompositeTransform.ScaleY));

        Storyboard.SetTarget(animOpacity, ring);
        Storyboard.SetTargetProperty(animOpacity, nameof(UIElement.Opacity));

        sb.Children.Add(animScaleX);
        sb.Children.Add(animScaleY);
        sb.Children.Add(animOpacity);

        sb.Completed += (s, e) =>
        {
            CursorOverlayCanvas.Children.Remove(ring);
        };

        sb.Begin();
    }

    private void SpawnParticles(double x, double y, string effect)
    {
        if (CursorOverlayCanvas == null) return;

        int count = effect == "firework" ? 8 : 6;
        var colors = effect switch
        {
            "christmas" => new[] { Color.FromArgb(255, 239, 68, 68), Color.FromArgb(255, 34, 197, 94), Colors.Gold },
            "firework" => new[] { Color.FromArgb(255, 244, 63, 94), Color.FromArgb(255, 59, 130, 246), Color.FromArgb(255, 234, 179, 8), Color.FromArgb(255, 168, 85, 247) },
            _ => new[] { Color.FromArgb(255, 250, 204, 21), Colors.White, Color.FromArgb(255, 192, 193, 255) }
        };

        for (int i = 0; i < count; i++)
        {
            double angle = (2 * Math.PI / count) * i;
            double dist = 28 + (i % 2) * 10;
            double targetX = Math.Cos(angle) * dist;
            double targetY = Math.Sin(angle) * dist;

            var p = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = new SolidColorBrush(colors[i % colors.Length]),
                IsHitTestVisible = false
            };

            var trans = new CompositeTransform();
            p.RenderTransform = trans;

            Canvas.SetLeft(p, x - 3);
            Canvas.SetTop(p, y - 3);
            CursorOverlayCanvas.Children.Add(p);

            var sb = new Storyboard();
            var animX = new DoubleAnimation { From = 0, To = targetX, Duration = TimeSpan.FromMilliseconds(400) };
            var animY = new DoubleAnimation { From = 0, To = targetY, Duration = TimeSpan.FromMilliseconds(400) };
            var animOp = new DoubleAnimation { From = 1.0, To = 0.0, Duration = TimeSpan.FromMilliseconds(400) };

            Storyboard.SetTarget(animX, trans);
            Storyboard.SetTargetProperty(animX, nameof(CompositeTransform.TranslateX));

            Storyboard.SetTarget(animY, trans);
            Storyboard.SetTargetProperty(animY, nameof(CompositeTransform.TranslateY));

            Storyboard.SetTarget(animOp, p);
            Storyboard.SetTargetProperty(animOp, nameof(UIElement.Opacity));

            sb.Children.Add(animX);
            sb.Children.Add(animY);
            sb.Children.Add(animOp);

            sb.Completed += (s, e) =>
            {
                CursorOverlayCanvas.Children.Remove(p);
            };

            sb.Begin();
        }
    }
}
