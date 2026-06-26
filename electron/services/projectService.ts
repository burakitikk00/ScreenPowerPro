import * as fs from 'fs';
import * as path from 'path';
import { app } from 'electron';
import type {
  AppSettings,
  ProjectManifest,
  MouseClickEvent,
  MouseMoveEvent,
  KeystrokeEvent,
  RecordingMetadata,
} from '../../shared/types';

const SETTINGS_FILE = 'settings.json';

export function getSettingsPath(): string {
  return path.join(app.getPath('userData'), SETTINGS_FILE);
}

export function loadSettings(): AppSettings {
  const defaults: AppSettings = {
    projectLocation: path.join(app.getPath('documents'), 'ScreenPowerPro Projects'),
    exportLocation: path.join(app.getPath('documents'), 'ScreenPowerPro Exports'),
    autoZoom: 'smooth',
    hideDesktopIcons: false,
    hideTaskbar: false,
    resolution: '1080p',
    countdown: 3,
    exportFormat: 'mp4',
    exportFps: 30,
    exportResolution: '1080p',
  };

  try {
    const raw = fs.readFileSync(getSettingsPath(), 'utf-8');
    return { ...defaults, ...JSON.parse(raw) };
  } catch {
    return defaults;
  }
}

export function saveSettings(settings: AppSettings): void {
  fs.mkdirSync(path.dirname(getSettingsPath()), { recursive: true });
  fs.writeFileSync(getSettingsPath(), JSON.stringify(settings, null, 2));
}

export function createProjectFolder(basePath: string, name: string) {
  fs.mkdirSync(basePath, { recursive: true });
  const projectPath = path.join(basePath, name);
  const recordingDir = path.join(projectPath, 'recording');
  const bundleDir = path.join(projectPath, 'bundle');

  fs.mkdirSync(recordingDir, { recursive: true });
  fs.mkdirSync(bundleDir, { recursive: true });

  return { projectPath, recordingDir, bundleDir };
}

export function saveRecordingBuffer(
  projectPath: string,
  filename: string,
  buffer: Buffer
): string {
  const filePath = path.join(projectPath, 'recording', filename);
  fs.writeFileSync(filePath, buffer);
  return filePath;
}

export function saveRecordingData(
  projectPath: string,
  metadata: RecordingMetadata,
  clicks: MouseClickEvent[],
  moves: MouseMoveEvent[],
  keystrokes: KeystrokeEvent[]
): void {
  const recordingDir = path.join(projectPath, 'recording');
  fs.writeFileSync(path.join(recordingDir, 'metadata.json'), JSON.stringify(metadata, null, 2));
  fs.writeFileSync(path.join(recordingDir, 'mouseclicks-0.json'), JSON.stringify(clicks, null, 2));
  fs.writeFileSync(path.join(recordingDir, 'mousemoves-0.json'), JSON.stringify(moves, null, 2));
  fs.writeFileSync(path.join(recordingDir, 'keystrokes-0.json'), JSON.stringify(keystrokes, null, 2));
}

export function saveProjectManifest(projectPath: string, manifest: ProjectManifest): void {
  fs.writeFileSync(
    path.join(projectPath, 'project.myproj'),
    JSON.stringify(manifest, null, 2)
  );
}

export function loadProjectManifest(projectPath: string): ProjectManifest {
  const raw = fs.readFileSync(path.join(projectPath, 'project.myproj'), 'utf-8');
  return JSON.parse(raw);
}

export function loadMetadata(projectPath: string): RecordingMetadata {
  const raw = fs.readFileSync(
    path.join(projectPath, 'recording', 'metadata.json'),
    'utf-8'
  );
  return JSON.parse(raw);
}

export function getVideoPath(projectPath: string): string {
  return path.join(projectPath, 'recording', 'display-0.webm');
}
