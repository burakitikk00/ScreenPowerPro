export type RecordingMode = 'fullscreen' | 'window' | 'custom' | 'camera';

export type CursorSmoothing = 'slow' | 'medium' | 'fast';

export type AutoZoomMode = 'none' | 'smooth' | 'instant';

export type Resolution = '1080p' | '4k';

export type ExportFps = 30 | 60;

export interface MouseClickEvent {
  timestamp: number;
  type: 'left_down' | 'left_up' | 'right_down' | 'right_up';
  x: number;
  y: number;
}

export interface MouseMoveEvent {
  timestamp: number;
  x: number;
  y: number;
}

export interface KeystrokeEvent {
  timestamp: number;
  key: string;
  modifiers: string[];
}

export interface RecordingMetadata {
  width: number;
  height: number;
  fps: number;
  duration: number;
  recordedAt: string;
  mode: RecordingMode;
  customCropBounds?: {
    x: number;
    y: number;
    width: number;
    height: number;
  };
}

export interface ZoomEffect {
  id: string;
  startTime: number;
  duration: number;
  targetX: number;
  targetY: number;
  scale: number;
  easing: 'ease-in-out' | 'linear' | 'instant';
}

export interface TrackState {
  offset: number;     // Başlangıçtan ötelenme miktarı (timeline pozisyonu)
  trimStart: number;  // Başlangıçtan kırpma miktarı
  trimEnd: number;    // Sondan kırpma miktarı (veya orijinal uzunluk)
}

export interface TimelineSettings {
  cursorSmoothing: CursorSmoothing;
  motionBlur: boolean;
  watermark: boolean;
  showShortcutKeys: boolean;
  canvasBackground: string;
  backgroundOpacity: number;
  defaultZoomScale: number;
  motionBlurAmount: number;
  // Yeni özellikler
  videoSpeed: number;
  cursorSize: number;
  cursorVisible: boolean;
  cameraVisible: boolean;
  cameraShape: 'circle' | 'square';
  cameraSize: number;
  micVolume: number;
  sysVolume: number;
  watermarkText: string;
}

export interface ProjectManifest {
  version: string;
  name: string;
  videoPath: string;
  microphonePath?: string;
  systemAudioPath?: string;
  timeline: {
    zoomEffects: ZoomEffect[];
    settings: TimelineSettings;
    videoTrack?: TrackState;
    micTrack?: TrackState;
    sysTrack?: TrackState;
  };
}

export interface AppSettings {
  projectLocation: string;
  exportLocation: string;
  autoZoom: AutoZoomMode;
  hideDesktopIcons: boolean;
  hideTaskbar: boolean;
  resolution: Resolution;
  countdown: 0 | 3 | 5 | 10;
  exportFormat: 'mp4' | 'webm';
  exportFps: number;
  exportResolution: '1080p' | '720p' | '4k';
  microphoneEnabled?: boolean;
  systemAudioEnabled?: boolean;
  cameraEnabled?: boolean;
  selectedCameraId?: string;
  selectedMicId?: string;
  selectedSpeakerId?: string;
  customCropBounds?: {
    x: number;
    y: number;
    width: number;
    height: number;
  };
}

export interface ScreenSource {
  id: string;
  name: string;
  thumbnail: string;
}

export interface AudioDevice {
  deviceId: string;
  label: string;
}

export interface RecordingSession {
  projectPath: string;
  projectName: string;
  startTime: number;
}

export type AppScreen = 'dashboard' | 'library' | 'recording' | 'editor' | 'export';

export interface ProjectSummary {
  path: string;
  name: string;
  recordedAt: string;
  duration: number;
  mode: RecordingMode;
  hasVideo: boolean;
  videoUrl?: string;
}

export interface ExportProgress {
  percent: number;
  status: string;
  estimatedSeconds?: number;
}
