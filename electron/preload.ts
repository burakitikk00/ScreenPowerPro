import { contextBridge, ipcRenderer } from 'electron';
import type {
  AppSettings,
  ProjectManifest,
  ProjectSummary,
  ScreenSource,
  ExportProgress,
  MouseClickEvent,
  MouseMoveEvent,
  KeystrokeEvent,
  RecordingMetadata,
} from '../shared/types';

const api = {
  getScreenSources: (): Promise<ScreenSource[]> =>
    ipcRenderer.invoke('get-screen-sources'),

  getSettings: (): Promise<AppSettings> => ipcRenderer.invoke('get-settings'),

  saveSettings: (settings: AppSettings): Promise<void> =>
    ipcRenderer.invoke('save-settings', settings),

  selectFolder: (title: string): Promise<string | null> =>
    ipcRenderer.invoke('select-folder', title),

  createProject: (name: string): Promise<{ projectPath: string; recordingDir: string }> =>
    ipcRenderer.invoke('create-project', name),

  prepareCapture: (opts: { sourceId: string; includeSystemAudio: boolean }): Promise<void> =>
    ipcRenderer.invoke('prepare-capture', opts),

  minimizeForRecording: (): Promise<void> => ipcRenderer.invoke('minimize-for-recording'),

  restoreAfterRecording: (): Promise<void> => ipcRenderer.invoke('restore-after-recording'),

  startInputTracking: (): Promise<void> => ipcRenderer.invoke('start-input-tracking'),

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
    ipcRenderer.invoke('save-recording-file', projectPath, filename, new Uint8Array(data)),

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

  loadProjectMetadata: (projectPath: string): Promise<RecordingMetadata> =>
    ipcRenderer.invoke('load-project-metadata', projectPath),

  loadProjectInputData: (projectPath: string): Promise<{ moves: MouseMoveEvent[], clicks: MouseClickEvent[] }> =>
    ipcRenderer.invoke('load-project-input-data', projectPath),

  listProjects: (): Promise<ProjectSummary[]> => ipcRenderer.invoke('list-projects'),

  getMediaUrl: (projectPath: string, relativePath: string): Promise<string | null> =>
    ipcRenderer.invoke('get-media-url', projectPath, relativePath),

  getMediaDebugInfo: (
    projectPath: string,
    relativePath: string
  ): Promise<{ fullPath: string; exists: boolean; size: number; url: string | null }> =>
    ipcRenderer.invoke('get-media-debug-info', projectPath, relativePath),

  openProjectDialog: (): Promise<string | null> => ipcRenderer.invoke('open-project-dialog'),

  startExport: (
    projectPath: string,
    manifest: ProjectManifest,
    metadata: RecordingMetadata
  ): Promise<string> =>
    ipcRenderer.invoke('start-export', projectPath, manifest, metadata),

  cancelExport: (): Promise<void> => ipcRenderer.invoke('cancel-export'),

  onExportProgress: (callback: (progress: ExportProgress) => void) => {
    const handler = (_: unknown, progress: ExportProgress) => callback(progress);
    ipcRenderer.on('export-progress', handler);
    return () => ipcRenderer.removeListener('export-progress', handler);
  },

  onStopRecordingRequest: (callback: () => void) => {
    const handler = () => callback();
    ipcRenderer.on('stop-recording-request', handler);
    return () => ipcRenderer.removeListener('stop-recording-request', handler);
  },

  revealInFolder: (filePath: string): Promise<void> =>
    ipcRenderer.invoke('reveal-in-folder', filePath),

  getDefaultPaths: (): Promise<{ projects: string; exports: string }> =>
    ipcRenderer.invoke('get-default-paths'),

  requestStopRecording: (): Promise<void> => ipcRenderer.invoke('trigger-stop-recording'),
  
  showRecordingBar: (): Promise<void> => ipcRenderer.invoke('show-recording-bar'),
  resizeForEditor: (): Promise<void> => ipcRenderer.invoke('resize-for-editor'),
  createCameraOverlay: (): Promise<void> => ipcRenderer.invoke('create-camera-overlay'),
  closeCameraOverlay: (): Promise<void> => ipcRenderer.invoke('close-camera-overlay'),
  startCameraDrag: (offsetX: number, offsetY: number): Promise<void> => ipcRenderer.invoke('start-camera-drag', offsetX, offsetY),
  stopCameraDrag: (): Promise<void> => ipcRenderer.invoke('stop-camera-drag'),

  openCropper: (): Promise<void> => ipcRenderer.invoke('open-cropper'),
  closeCropper: (): Promise<void> => ipcRenderer.invoke('close-cropper'),
  openMask: (bounds: {x: number, y: number, width: number, height: number}): Promise<void> => ipcRenderer.invoke('open-mask', bounds),
  closeMask: (): Promise<void> => ipcRenderer.invoke('close-mask'),
  openCountdown: (seconds: number): Promise<void> => ipcRenderer.invoke('open-countdown', seconds),
  closeCountdown: (): Promise<void> => ipcRenderer.invoke('close-countdown'),
  onStartCountdown: (callback: (seconds: number) => void) => {
    const handler = (_: unknown, seconds: number) => callback(seconds);
    ipcRenderer.on('start-countdown', handler);
    return () => ipcRenderer.removeListener('start-countdown', handler);
  },
  onCropperAreaSelected: (callback: (bounds: {x: number, y: number, width: number, height: number}) => void) => {
    const handler = (_: unknown, bounds: {x: number, y: number, width: number, height: number}) => callback(bounds);
    ipcRenderer.on('cropper-area-selected', handler);
    return () => ipcRenderer.removeListener('cropper-area-selected', handler);
  },
  sendCropperArea: (bounds: {x: number, y: number, width: number, height: number}): Promise<void> => 
    ipcRenderer.invoke('cropper-area-selected', bounds),

  resizeForPrerecording: (): Promise<void> => ipcRenderer.invoke('resize-for-prerecording'),
  restoreAndResizeForPrerecording: (): Promise<void> => ipcRenderer.invoke('restore-and-resize-for-prerecording'),
  restoreDashboardSize: (): Promise<void> => ipcRenderer.invoke('restore-dashboard-size'),

  windowMinimize: (): Promise<void> => ipcRenderer.invoke('window-minimize'),
  windowMaximize: (): Promise<void> => ipcRenderer.invoke('window-maximize'),
  windowClose: (): Promise<void> => ipcRenderer.invoke('window-close'),
};

contextBridge.exposeInMainWorld('electronAPI', api);

export type ElectronAPI = typeof api;
