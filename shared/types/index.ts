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

export interface TimelineSettings {
  cursorSmoothing: CursorSmoothing;
  motionBlur: boolean;
  watermark: boolean;
  showShortcutKeys: boolean;
  canvasBackground: string;
  backgroundOpacity: number;
  defaultZoomScale: number;
  motionBlurAmount: number;
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
  exportFormat: 'mp4';
  exportFps: ExportFps;
  exportResolution: Resolution;
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

export type AppScreen = 'dashboard' | 'recording' | 'editor' | 'export';

export interface ExportProgress {
  percent: number;
  status: string;
  estimatedSeconds?: number;
}
