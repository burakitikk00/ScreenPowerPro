import { contextBridge, ipcRenderer } from 'electron';
import type {
  AppSettings,
  ProjectManifest,
  ScreenSource,
  AudioDevice,
  ExportProgress,
  MouseClickEvent,
  MouseMoveEvent,
  KeystrokeEvent,
  RecordingMetadata,
} from '../shared/types';

const api = {
  getScreenSources: (): Promise<ScreenSource[]> =>
    ipcRenderer.invoke('get-screen-sources'),

  getAudioDevices: (): Promise<AudioDevice[]> =>
    ipcRenderer.invoke('get-audio-devices'),

  getSettings: (): Promise<AppSettings> =>
    ipcRenderer.invoke('get-settings'),

  saveSettings: (settings: AppSettings): Promise<void> =>
    ipcRenderer.invoke('save-settings', settings),

  selectFolder: (title: string): Promise<string | null> =>
    ipcRenderer.invoke('select-folder', title),

  createProject: (name: string): Promise<{ projectPath: string; recordingDir: string }> =>
    ipcRenderer.invoke('create-project', name),

  startInputTracking: (): Promise<void> =>
    ipcRenderer.invoke('start-input-tracking'),

  stopInputTracking: (): Promise<{
    clicks: MouseClickEvent[];
    moves: MouseMoveEvent[];
    keystrokes: KeystrokeEvent[];
  }> => ipcRenderer.invoke('stop-input-tracking'),

  saveRecordingFile: (
    projectPath: string,
    filename: string,
    data: ArrayBuffer
  ): Promise<string> =>
    ipcRenderer.invoke('save-recording-file', projectPath, filename, Buffer.from(data)),

  saveMetadata: (
    projectPath: string,
    metadata: RecordingMetadata,
    clicks: MouseClickEvent[],
    moves: MouseMoveEvent[],
    keystrokes: KeystrokeEvent[]
  ): Promise<void> =>
    ipcRenderer.invoke('save-metadata', projectPath, metadata, clicks, moves, keystrokes),

  saveProjectManifest: (projectPath: string, manifest: ProjectManifest): Promise<void> =>
    ipcRenderer.invoke('save-project-manifest', projectPath, manifest),

  loadProject: (projectPath: string): Promise<ProjectManifest> =>
    ipcRenderer.invoke('load-project', projectPath),

  openProjectDialog: (): Promise<string | null> =>
    ipcRenderer.invoke('open-project-dialog'),

  startExport: (
    projectPath: string,
    manifest: ProjectManifest,
    metadata: RecordingMetadata
  ): Promise<string> =>
    ipcRenderer.invoke('start-export', projectPath, manifest, metadata),

  cancelExport: (): Promise<void> =>
    ipcRenderer.invoke('cancel-export'),

  onExportProgress: (callback: (progress: ExportProgress) => void) => {
    const handler = (_: unknown, progress: ExportProgress) => callback(progress);
    ipcRenderer.on('export-progress', handler);
    return () => ipcRenderer.removeListener('export-progress', handler);
  },

  revealInFolder: (filePath: string): Promise<void> =>
    ipcRenderer.invoke('reveal-in-folder', filePath),

  getDefaultPaths: (): Promise<{ projects: string; exports: string }> =>
    ipcRenderer.invoke('get-default-paths'),
};

contextBridge.exposeInMainWorld('electronAPI', api);

export type ElectronAPI = typeof api;
