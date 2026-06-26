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
    microphoneEnabled: true,
    systemAudioEnabled: true,
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

export function resolveMediaPath(projectPath: string, relativePath: string): string {
  const cleaned = relativePath.replace(/^\.\//, '');
  return path.join(projectPath, cleaned);
}

export function listProjects(basePath: string) {
  if (!fs.existsSync(basePath)) return [];

  const entries = fs.readdirSync(basePath, { withFileTypes: true });
  const projects = [];

  for (const entry of entries) {
    if (!entry.isDirectory()) continue;
    const projectPath = path.join(basePath, entry.name);
    const manifestPath = path.join(projectPath, 'project.myproj');
    if (!fs.existsSync(manifestPath)) continue;

    try {
      const manifest = loadProjectManifest(projectPath);
      let metadata: RecordingMetadata = {
        width: 1920,
        height: 1080,
        fps: 30,
        duration: 0,
        recordedAt: '',
        mode: 'fullscreen',
      };
      const metaPath = path.join(projectPath, 'recording', 'metadata.json');
      if (fs.existsSync(metaPath)) {
        metadata = JSON.parse(fs.readFileSync(metaPath, 'utf-8'));
      }
      const videoPath = resolveMediaPath(projectPath, manifest.videoPath);
      projects.push({
        path: projectPath,
        name: manifest.name || entry.name,
        recordedAt: metadata.recordedAt || entry.name,
        duration: metadata.duration,
        mode: metadata.mode,
        hasVideo: fs.existsSync(videoPath),
      });
    } catch {
      /* skip invalid */
    }
  }

  return projects.sort(
    (a, b) => new Date(b.recordedAt).getTime() - new Date(a.recordedAt).getTime()
  );
}
