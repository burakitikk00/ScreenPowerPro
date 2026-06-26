import { BrowserWindow, screen, app } from 'electron';
import * as path from 'path';

const isDev = !app.isPackaged;

let mainWindow: BrowserWindow | null = null;
let recordingWindow: BrowserWindow | null = null;

export interface CaptureOptions {
  sourceId: string;
  includeSystemAudio: boolean;
}

let captureOptions: CaptureOptions = { sourceId: '', includeSystemAudio: true };

export function setMainWindow(win: BrowserWindow | null) {
  mainWindow = win;
}

export function getMainWindow() {
  return mainWindow;
}

export function getRecordingWindow() {
  return recordingWindow;
}

export function setCaptureOptions(opts: CaptureOptions) {
  captureOptions = opts;
}

export function getCaptureOptions() {
  return captureOptions;
}

export function createRecordingWindow() {
  if (recordingWindow) return recordingWindow;

  recordingWindow = new BrowserWindow({
    width: 320,
    height: 56,
    frame: false,
    transparent: true,
    backgroundColor: '#00000000',
    alwaysOnTop: true,
    resizable: false,
    skipTaskbar: true,
    show: false,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
    },
  });

  if (isDev) {
    recordingWindow.loadURL('http://localhost:5173/#/recording-bar');
  } else {
    recordingWindow.loadFile(path.join(__dirname, '../../dist/index.html'), {
      hash: '/recording-bar',
    });
  }

  recordingWindow.on('closed', () => {
    recordingWindow = null;
  });

  return recordingWindow;
}

export function showRecordingWindow() {
  const recWin = createRecordingWindow();
  const { width, height } = screen.getPrimaryDisplay().workAreaSize;
  recWin.setPosition(Math.round((width - 320) / 2), height - 80);
  recWin.show();
}

export function closeRecordingWindow() {
  recordingWindow?.close();
  recordingWindow = null;
}

export function minimizeForRecording() {
  mainWindow?.minimize();
  showRecordingWindow();
}

export function restoreAfterRecording() {
  closeRecordingWindow();
  mainWindow?.restore();
  mainWindow?.focus();
}
