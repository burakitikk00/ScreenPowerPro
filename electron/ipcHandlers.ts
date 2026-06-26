import { ipcMain, desktopCapturer, dialog, shell, app } from 'electron';
import * as path from 'path';
import * as fs from 'fs';
import {
  loadSettings,
  saveSettings,
  createProjectFolder,
  saveRecordingBuffer,
  saveRecordingData,
  saveProjectManifest,
  loadProjectManifest,
  loadMetadata,
  listProjects,
  resolveMediaPath,
} from './services/projectService';
import { startInputTracking, stopInputTracking } from './services/inputTracker';
import { startExport, cancelExport } from './services/exportService';
import {
  setCaptureOptions,
  minimizeForRecording,
  restoreAfterRecording,
  getMainWindow,
  getRecordingWindow,
  createCameraOverlay,
  closeCameraOverlay,
  createCropperWindow,
  closeCropperWindow,
} from './windowManager';
import type { AppSettings, ProjectManifest, RecordingMetadata } from '../shared/types';

export function resolveMediaFilePath(requestUrl: string): string | null {
  try {
    const url = new URL(requestUrl);
    const file = url.searchParams.get('file');
    if (!file) return null;
    return decodeURIComponent(file);
  } catch {
    return null;
  }
}

export function buildMediaUrl(fullPath: string): string {
  return `media://playback?file=${encodeURIComponent(fullPath)}`;
}

export function registerIpcHandlers() {
  ipcMain.handle('get-screen-sources', async () => {
    const sources = await desktopCapturer.getSources({
      types: ['screen', 'window'],
      thumbnailSize: { width: 320, height: 180 },
    });
    return sources.map((s) => ({
      id: s.id,
      name: s.name,
      thumbnail: s.thumbnail.toDataURL(),
    }));
  });

  ipcMain.handle('get-settings', () => loadSettings());
  ipcMain.handle('save-settings', (_e, settings: AppSettings) => saveSettings(settings));

  ipcMain.handle('get-default-paths', () => ({
    projects: path.join(app.getPath('documents'), 'ScreenPowerPro Projects'),
    exports: path.join(app.getPath('documents'), 'ScreenPowerPro Exports'),
  }));

  ipcMain.handle('select-folder', async (_e, title: string) => {
    const result = await dialog.showOpenDialog({
      title,
      properties: ['openDirectory', 'createDirectory'],
    });
    return result.canceled ? null : result.filePaths[0];
  });

  ipcMain.handle('create-project', (_e, name: string) => {
    const settings = loadSettings();
    const basePath =
      settings.projectLocation || path.join(app.getPath('documents'), 'ScreenPowerPro Projects');
    return createProjectFolder(basePath, name);
  });

  ipcMain.handle(
    'prepare-capture',
    (_e, opts: { sourceId: string; includeSystemAudio: boolean }) => {
      setCaptureOptions(opts);
    }
  );

  ipcMain.handle('minimize-for-recording', () => minimizeForRecording());
  ipcMain.handle('restore-after-recording', () => restoreAfterRecording());

  ipcMain.handle('start-input-tracking', () => startInputTracking());
  ipcMain.handle('stop-input-tracking', () => stopInputTracking());

  ipcMain.handle(
    'save-recording-file',
    (_e, projectPath: string, filename: string, data: Uint8Array) =>
      saveRecordingBuffer(projectPath, filename, Buffer.from(data))
  );

  ipcMain.handle(
    'save-metadata',
    (
      _e,
      projectPath: string,
      metadata: RecordingMetadata,
      clicks: unknown,
      moves: unknown,
      keystrokes: unknown
    ) => {
      saveRecordingData(
        projectPath,
        metadata,
        clicks as Parameters<typeof saveRecordingData>[2],
        moves as Parameters<typeof saveRecordingData>[3],
        keystrokes as Parameters<typeof saveRecordingData>[4]
      );
    }
  );

  ipcMain.handle('save-project-manifest', (_e, projectPath: string, manifest: ProjectManifest) =>
    saveProjectManifest(projectPath, manifest)
  );

  ipcMain.handle('load-project', (_e, projectPath: string) => loadProjectManifest(projectPath));

  ipcMain.handle('load-project-metadata', (_e, projectPath: string) =>
    loadMetadata(projectPath)
  );

  ipcMain.handle('list-projects', () => {
    const settings = loadSettings();
    const basePath =
      settings.projectLocation || path.join(app.getPath('documents'), 'ScreenPowerPro Projects');
    return listProjects(basePath);
  });

  ipcMain.handle('get-media-url', (_e, projectPath: string, relativePath: string) => {
    const fullPath = resolveMediaPath(projectPath, relativePath);
    if (!fs.existsSync(fullPath)) {
      console.error('[MediaProtocol] Dosya bulunamadı:', fullPath);
      return null;
    }
    const url = buildMediaUrl(fullPath);
    console.log('[MediaProtocol] URL oluşturuldu:', { fullPath, size: fs.statSync(fullPath).size, url });
    return url;
  });

  ipcMain.handle('get-media-debug-info', (_e, projectPath: string, relativePath: string) => {
    const fullPath = resolveMediaPath(projectPath, relativePath);
    const exists = fs.existsSync(fullPath);
    const size = exists ? fs.statSync(fullPath).size : 0;
    const url = exists ? buildMediaUrl(fullPath) : null;
    console.log('[MediaProtocol] Debug info:', { fullPath, exists, size, url });
    return { fullPath, exists, size, url };
  });

  ipcMain.handle('open-project-dialog', async () => {
    const result = await dialog.showOpenDialog({
      title: 'Proje Aç',
      properties: ['openDirectory'],
    });
    if (result.canceled || !result.filePaths[0]) return null;
    const projectPath = result.filePaths[0];
    try {
      loadProjectManifest(projectPath);
      return projectPath;
    } catch {
      return null;
    }
  });

  ipcMain.handle(
    'start-export',
    async (_e, projectPath: string, manifest: ProjectManifest, metadata: RecordingMetadata) => {
      const settings = loadSettings();
      const win = getMainWindow();
      const outputPath = await startExport(
        projectPath,
        manifest,
        metadata,
        settings.exportFps,
        (progress) => {
          win?.webContents.send('export-progress', progress);
        }
      );
      return outputPath;
    }
  );

  ipcMain.handle('cancel-export', () => cancelExport());
  ipcMain.handle('reveal-in-folder', (_e, filePath: string) => shell.showItemInFolder(filePath));

  ipcMain.handle('trigger-stop-recording', () => {
    const main = getMainWindow();
    main?.webContents.send('stop-recording-request');
  });

  ipcMain.handle('resize-for-editor', () => {
    const main = getMainWindow();
    if (main) {
      main.setResizable(true);
      main.setSize(1200, 800);
      main.center();
    }
  });

  ipcMain.handle('create-camera-overlay', () => {
    createCameraOverlay();
  });

  ipcMain.handle('close-camera-overlay', () => {
    closeCameraOverlay();
  });

  ipcMain.handle('open-cropper', () => {
    createCropperWindow();
  });

  ipcMain.handle('close-cropper', () => {
    closeCropperWindow();
  });

  ipcMain.handle('open-mask', (_e, bounds) => {
    const { createMaskWindow } = require('./windowManager');
    createMaskWindow(bounds);
  });

  ipcMain.handle('close-mask', () => {
    const { closeMaskWindow } = require('./windowManager');
    closeMaskWindow();
  });

  ipcMain.handle('open-countdown', (_e, seconds: number) => {
    const { createCountdownWindow } = require('./windowManager');
    createCountdownWindow(seconds);
  });

  ipcMain.handle('close-countdown', () => {
    const { closeCountdownWindow } = require('./windowManager');
    closeCountdownWindow();
  });

  ipcMain.handle('cropper-area-selected', (_e, bounds: {x: number, y: number, width: number, height: number}) => {
    const main = getMainWindow();
    main?.webContents.send('cropper-area-selected', bounds);
    closeCropperWindow();
  });

  ipcMain.handle('resize-for-prerecording', () => {
    const main = getMainWindow();
    if (!main) return;
    main.setSize(850, 300);
    // Position it near the bottom
    const { screen } = require('electron');
    const primaryDisplay = screen.getPrimaryDisplay();
    const { width, height } = primaryDisplay.workAreaSize;
    main.setPosition(Math.round(width / 2 - 425), Math.round(height - 350));
  });

  ipcMain.handle('restore-and-resize-for-prerecording', () => {
    const main = getMainWindow();
    if (!main) return;
    if (main.isMinimized()) {
      main.restore();
    }
    main.setSize(850, 300);
    const { screen } = require('electron');
    const primaryDisplay = screen.getPrimaryDisplay();
    const { width, height } = primaryDisplay.workAreaSize;
    main.setPosition(Math.round(width / 2 - 425), Math.round(height - 350));
  });

  ipcMain.handle('restore-dashboard-size', () => {
    const main = getMainWindow();
    if (!main) return;
    main.setSize(720, 350);
    main.center();
  });

  ipcMain.handle('window-minimize', () => {
    const main = getMainWindow();
    main?.minimize();
  });

  ipcMain.handle('window-maximize', () => {
    const main = getMainWindow();
    if (!main) return;
    if (main.isMaximized()) {
      main.unmaximize();
    } else {
      main.maximize();
    }
  });

  ipcMain.handle('window-close', () => {
    app.quit();
  });
}
