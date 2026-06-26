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

let cameraWindow: BrowserWindow | null = null;

export function createCameraOverlay() {
  if (cameraWindow) return cameraWindow;

  cameraWindow = new BrowserWindow({
    width: 200,
    height: 200,
    frame: false,
    transparent: true,
    backgroundColor: '#00000000',
    alwaysOnTop: true,
    resizable: true,
    skipTaskbar: true,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
    },
  });

  // Important: setAspectRatio to keep it square
  cameraWindow.setAspectRatio(1);

  if (!app.isPackaged) {
    cameraWindow.loadURL('http://localhost:5173/#/camera-overlay');
  } else {
    cameraWindow.loadFile(path.join(__dirname, '../../dist/index.html'), {
      hash: '/camera-overlay',
    });
  }

  cameraWindow.on('closed', () => {
    cameraWindow = null;
  });

  return cameraWindow;
}

export function closeCameraOverlay() {
  if (cameraWindow) {
    cameraWindow.close();
    cameraWindow = null;
  }
}

let maskWindow: BrowserWindow | null = null;

export function createMaskWindow(bounds: { x: number; y: number; width: number; height: number }) {
  if (maskWindow) return maskWindow;

  const { screen } = require('electron');
  const primaryDisplay = screen.getPrimaryDisplay();
  const { width, height } = primaryDisplay.bounds;

  maskWindow = new BrowserWindow({
    width,
    height,
    x: 0,
    y: 0,
    frame: false,
    transparent: true,
    backgroundColor: '#00000000',
    alwaysOnTop: true,
    resizable: false,
    skipTaskbar: true,
    hasShadow: false,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
    },
  });

  maskWindow.setIgnoreMouseEvents(true);

  if (isDev) {
    maskWindow.loadURL(`http://localhost:5173/#/mask-overlay?bounds=${JSON.stringify(bounds)}`);
  } else {
    maskWindow.loadFile(path.join(__dirname, '../../dist/index.html'), {
      hash: `/mask-overlay?bounds=${JSON.stringify(bounds)}`,
    });
  }

  return maskWindow;
}

export function closeMaskWindow() {
  if (maskWindow) {
    maskWindow.close();
    maskWindow = null;
  }
}

let countdownWindow: BrowserWindow | null = null;

export function createCountdownWindow(seconds: number) {
  if (countdownWindow) {
    countdownWindow.webContents.send('start-countdown', seconds);
    return countdownWindow;
  }

  const { screen } = require('electron');
  const primaryDisplay = screen.getPrimaryDisplay();
  const { width, height } = primaryDisplay.bounds;

  countdownWindow = new BrowserWindow({
    width,
    height,
    x: 0,
    y: 0,
    frame: false,
    transparent: true,
    backgroundColor: '#00000000',
    alwaysOnTop: true,
    resizable: false,
    skipTaskbar: true,
    hasShadow: false,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
    },
  });

  countdownWindow.setIgnoreMouseEvents(true);

  if (isDev) {
    countdownWindow.loadURL(`http://localhost:5173/#/countdown-overlay?seconds=${seconds}`);
  } else {
    countdownWindow.loadFile(path.join(__dirname, '../../dist/index.html'), {
      hash: `/countdown-overlay?seconds=${seconds}`,
    });
  }

  return countdownWindow;
}

export function closeCountdownWindow() {
  if (countdownWindow) {
    countdownWindow.close();
    countdownWindow = null;
  }
}

let cropperWindow: BrowserWindow | null = null;

export function createCropperWindow() {
  if (cropperWindow) return cropperWindow;

  const primaryDisplay = screen.getPrimaryDisplay();
  const { width, height } = primaryDisplay.bounds;

  cropperWindow = new BrowserWindow({
    x: 0,
    y: 0,
    width,
    height,
    frame: false,
    transparent: true,
    backgroundColor: '#00000000',
    alwaysOnTop: true,
    resizable: false,
    skipTaskbar: true,
    enableLargerThanScreen: true,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
    },
  });

  if (!app.isPackaged) {
    cropperWindow.loadURL('http://localhost:5173/#/cropper-overlay');
  } else {
    cropperWindow.loadFile(path.join(__dirname, '../../dist/index.html'), {
      hash: '/cropper-overlay',
    });
  }

  cropperWindow.on('closed', () => {
    cropperWindow = null;
  });

  return cropperWindow;
}

export function closeCropperWindow() {
  if (cropperWindow) {
    cropperWindow.close();
    cropperWindow = null;
  }
}
