import { create } from 'zustand';
import type {
  AppScreen,
  AppSettings,
  RecordingMode,
  ProjectManifest,
  ExportProgress,
} from '../types';

interface AppState {
  screen: AppScreen;
  settings: AppSettings;
  recordingMode: RecordingMode;
  selectedSourceId: string | null;
  selectedSourceName: string | null;
  isRecording: boolean;
  countdown: number | null;
  currentProject: ProjectManifest | null;
  currentProjectPath: string | null;
  exportProgress: ExportProgress | null;
  settingsOpen: boolean;
  windowPickerOpen: boolean;

  setScreen: (screen: AppScreen) => void;
  setRecordingMode: (mode: RecordingMode) => void;
  setSelectedSource: (id: string | null, name?: string | null) => void;
  setIsRecording: (v: boolean) => void;
  setCountdown: (v: number | null) => void;
  setCurrentProject: (project: ProjectManifest | null, path: string | null) => void;
  updateSettings: (partial: Partial<AppSettings>) => void;
  setExportProgress: (progress: ExportProgress | null) => void;
  setSettingsOpen: (open: boolean) => void;
  setWindowPickerOpen: (open: boolean) => void;
}

const defaultSettings: AppSettings = {
  projectLocation: '',
  exportLocation: '',
  autoZoom: 'smooth',
  hideDesktopIcons: false,
  hideTaskbar: false,
  resolution: '1080p',
  countdown: 3,
  exportFormat: 'mp4',
  exportFps: 30,
  exportResolution: '1080p',
  microphoneEnabled: true,
  systemAudioEnabled: true,
};

export const useAppStore = create<AppState>((set) => ({
  screen: 'dashboard',
  settings: defaultSettings,
  recordingMode: 'fullscreen',
  selectedSourceId: null,
  selectedSourceName: null,
  isRecording: false,
  countdown: null,
  currentProject: null,
  currentProjectPath: null,
  exportProgress: null,
  settingsOpen: false,
  windowPickerOpen: false,

  setScreen: (screen) => set({ screen }),
  setRecordingMode: (mode) => set({ recordingMode: mode, selectedSourceId: null, selectedSourceName: null }),
  setSelectedSource: (id, name = null) => set({ selectedSourceId: id, selectedSourceName: name }),
  setIsRecording: (v) => set({ isRecording: v }),
  setCountdown: (v) => set({ countdown: v }),
  setCurrentProject: (project, path) =>
    set({ currentProject: project, currentProjectPath: path }),
  updateSettings: (partial) =>
    set((s) => ({ settings: { ...s.settings, ...partial } })),
  setExportProgress: (progress) => set({ exportProgress: progress }),
  setSettingsOpen: (open) => set({ settingsOpen: open }),
  setWindowPickerOpen: (open) => set({ windowPickerOpen: open }),
}));
