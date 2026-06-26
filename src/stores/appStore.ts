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
  isRecording: boolean;
  countdown: number | null;
  currentProject: ProjectManifest | null;
  currentProjectPath: string | null;
  exportProgress: ExportProgress | null;
  settingsOpen: boolean;

  setScreen: (screen: AppScreen) => void;
  setRecordingMode: (mode: RecordingMode) => void;
  setIsRecording: (v: boolean) => void;
  setCountdown: (v: number | null) => void;
  setCurrentProject: (project: ProjectManifest | null, path: string | null) => void;
  updateSettings: (partial: Partial<AppSettings>) => void;
  setExportProgress: (progress: ExportProgress | null) => void;
  setSettingsOpen: (open: boolean) => void;
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
};

export const useAppStore = create<AppState>((set) => ({
  screen: 'dashboard',
  settings: defaultSettings,
  recordingMode: 'fullscreen',
  isRecording: false,
  countdown: null,
  currentProject: null,
  currentProjectPath: null,
  exportProgress: null,
  settingsOpen: false,

  setScreen: (screen) => set({ screen }),
  setRecordingMode: (mode) => set({ recordingMode: mode }),
  setIsRecording: (v) => set({ isRecording: v }),
  setCountdown: (v) => set({ countdown: v }),
  setCurrentProject: (project, path) =>
    set({ currentProject: project, currentProjectPath: path }),
  updateSettings: (partial) =>
    set((s) => ({ settings: { ...s.settings, ...partial } })),
  setExportProgress: (progress) => set({ exportProgress: progress }),
  setSettingsOpen: (open) => set({ settingsOpen: open }),
}));
