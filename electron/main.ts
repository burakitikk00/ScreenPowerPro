import { app, BrowserWindow, protocol, session, desktopCapturer } from 'electron';
import * as path from 'path';
import * as fs from 'fs';
import { registerIpcHandlers, resolveMediaFilePath } from './ipcHandlers';
import { setMainWindow, getMainWindow, getCaptureOptions } from './windowManager';
import { ipcMain } from 'electron';

const isDev = !app.isPackaged;

function getMimeType(filePath: string): string {
  const ext = path.extname(filePath).toLowerCase();
  if (ext === '.webm') return 'video/webm';
  if (ext === '.mp4') return 'video/mp4';
  if (ext === '.m4a') return 'audio/mp4';
  return 'application/octet-stream';
}

async function serveMediaFile(request: Request): Promise<Response> {
  const filePath = resolveMediaFilePath(request.url);
  const rangeHeader = request.headers.get('Range');
  console.log('[MediaProtocol] İstek:', {
    url: request.url,
    filePath,
    range: rangeHeader,
  });

  if (!filePath || !fs.existsSync(filePath)) {
    console.error('[MediaProtocol] 404 — dosya yok:', filePath);
    return new Response('Not Found', { status: 404 });
  }

  const stat = await fs.promises.stat(filePath);
  const fileSize = stat.size;
  const mime = getMimeType(filePath);

  if (rangeHeader) {
    const match = /bytes=(\d*)-(\d*)/.exec(rangeHeader);
    if (!match) {
      console.error('[MediaProtocol] 416 — geçersiz range:', rangeHeader);
      return new Response('Invalid Range', { status: 416 });
    }
    const start = match[1] ? parseInt(match[1], 10) : 0;
    const end = match[2] ? parseInt(match[2], 10) : fileSize - 1;
    if (start >= fileSize || end >= fileSize) {
      console.error('[MediaProtocol] 416 — range sınır dışı:', { start, end, fileSize });
      return new Response('Range Not Satisfiable', { status: 416 });
    }
    const chunkSize = end - start + 1;
    const buffer = Buffer.alloc(chunkSize);
    const fd = await fs.promises.open(filePath, 'r');
    try {
      await fd.read(buffer, 0, chunkSize, start);
    } finally {
      await fd.close();
    }
    console.log('[MediaProtocol] 206 — parça gönderildi:', { start, end, chunkSize, fileSize });
    return new Response(buffer, {
      status: 206,
      headers: {
        'Content-Type': mime,
        'Content-Length': String(chunkSize),
        'Content-Range': `bytes ${start}-${end}/${fileSize}`,
        'Accept-Ranges': 'bytes',
      },
    });
  }

  console.log('[MediaProtocol] 200 — tam dosya:', { fileSize, mime });
  const data = await fs.promises.readFile(filePath);
  return new Response(data, {
    headers: {
      'Content-Type': mime,
      'Content-Length': String(fileSize),
      'Accept-Ranges': 'bytes',
    },
  });
}

let cameraWindow: BrowserWindow | null = null;

function createWindow() {
  const mainWindow = new BrowserWindow({
    width: 720,
    height: 310,
    minWidth: 720,
    minHeight: 350,
    resizable: false,
    frame: false, // We'll need a custom title bar or just a plain borderless window
    transparent: true, // FocuSee often has rounded corners, transparent helps
    backgroundColor: '#00000000',
    titleBarStyle: 'hidden',
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false,
    },
  });

  if (isDev) {
    mainWindow.loadURL('http://localhost:5173');
    // We don't want devtools opening in detach mode for a small launcher window initially, 
    // but it's okay for debugging. We'll leave it out for compactness.
  } else {
    mainWindow.loadFile(path.join(__dirname, '../../dist/index.html'));
  }

  mainWindow.on('closed', () => {
    setMainWindow(null);
    if (cameraWindow) {
      cameraWindow.close();
    }
  });

  setMainWindow(mainWindow);
  return mainWindow;
}

protocol.registerSchemesAsPrivileged([
  {
    scheme: 'media',
    privileges: {
      bypassCSP: true,
      stream: true,
      supportFetchAPI: true,
      corsEnabled: true,
    },
  },
]);

app.whenReady().then(() => {
  protocol.handle('media', (request) => serveMediaFile(request));

  session.defaultSession.setDisplayMediaRequestHandler(async (_request, callback) => {
    const opts = getCaptureOptions();
    const sources = await desktopCapturer.getSources({
      types: ['screen', 'window'],
      thumbnailSize: { width: 0, height: 0 },
    });

    let source = sources.find((s) => s.id === opts.sourceId);
    if (!source) {
      source = sources.find((s) => s.id.startsWith('screen:')) ?? sources[0];
    }

    if (!source) {
      callback({});
      return;
    }

    callback({
      video: source,
      audio: opts.includeSystemAudio ? 'loopback' : undefined,
    });
  });

  registerIpcHandlers();
  createWindow();

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit();
});
