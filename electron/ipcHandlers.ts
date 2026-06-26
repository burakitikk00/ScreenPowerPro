import { ipcMain, desktopCapturer, dialog, shell, app, BrowserWindow } from 'electron';
import * as path from 'path';
import { v4 as uuidv4 } from 'uuid';
import {
  loadSettings,
  saveSettings,
  createProjectFolder,
  saveRecordingBuffer,
  saveRecordingData,
  saveProjectManifest,
  loadProjectManifest,
  loadMetadata,
} from './services/projectService';
import { startInputTracking, stopInputTracking } from './services/inputTracker';
import { startExport, cancelExport } from './services/exportService';
import type { AppSettings, ProjectManifest, RecordingMetadata } from '../shared/types';

export function registerIpcHandlers(getWindow: () => BrowserWindow | null) {
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

  ipcMain.handle('get-audio-devices', async () => {
    return [{ deviceId: 'default', label: 'Varsayılan Mikrofon' }];
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
    const basePath = settings.projectLocation || path.join(app.getPath('documents'), 'ScreenPowerPro Projects');
    return createProjectFolder(basePath, name);
  });

  ipcMain.handle('start-input-tracking', () => startInputTracking());
  ipcMain.handle('stop-input-tracking', () => stopInputTracking());

  ipcMain.handle(
    'save-recording-file',
    (_e, projectPath: string, filename: string, buffer: Buffer) =>
      saveRecordingBuffer(projectPath, filename, buffer)
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

  ipcMain.handle('load-project', (_e, projectPath: string) =>
    loadProjectManifest(projectPath)
  );

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
      const win = getWindow();
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
}
