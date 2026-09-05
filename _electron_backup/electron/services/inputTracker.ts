import type { MouseClickEvent, MouseMoveEvent, KeystrokeEvent } from '../../shared/types';

let uIOhook: typeof import('uiohook-napi').uIOhook | null = null;
let recordingStartTime = 0;
const clicks: MouseClickEvent[] = [];
const moves: MouseMoveEvent[] = [];
const keystrokes: KeystrokeEvent[] = [];
let moveThrottle = 0;

const KEY_MAP: Record<number, string> = {
  1: 'Mouse Left',
  2: 'Mouse Right',
  3: 'Mouse Middle',
};

async function loadHook() {
  if (uIOhook) return uIOhook;
  try {
    const mod = await import('uiohook-napi');
    uIOhook = mod.uIOhook;
    return uIOhook;
  } catch (err) {
    console.warn('uiohook-napi yüklenemedi:', err);
    return null;
  }
}

export async function startInputTracking(): Promise<void> {
  clicks.length = 0;
  moves.length = 0;
  keystrokes.length = 0;
  recordingStartTime = Date.now();
  moveThrottle = 0;

  const hook = await loadHook();
  if (!hook) return;

  hook.on('mousedown', (e) => {
    const type = e.button === 1 ? 'left_down' : e.button === 2 ? 'right_down' : 'left_down';
    clicks.push({
      timestamp: Date.now() - recordingStartTime,
      type: type as MouseClickEvent['type'],
      x: e.x,
      y: e.y,
    });
  });

  hook.on('mouseup', (e) => {
    const type = e.button === 1 ? 'left_up' : e.button === 2 ? 'right_up' : 'left_up';
    clicks.push({
      timestamp: Date.now() - recordingStartTime,
      type: type as MouseClickEvent['type'],
      x: e.x,
      y: e.y,
    });
  });

  hook.on('mousemove', (e) => {
    const now = Date.now();
    if (now - moveThrottle < 50) return;
    moveThrottle = now;
    moves.push({
      timestamp: now - recordingStartTime,
      x: e.x,
      y: e.y,
    });
  });

  hook.on('keydown', (e) => {
    const key = KEY_MAP[e.keycode] || `Key${e.keycode}`;
    const modifiers: string[] = [];
    if (e.altKey) modifiers.push('Alt');
    if (e.ctrlKey) modifiers.push('Ctrl');
    if (e.shiftKey) modifiers.push('Shift');
    if (e.metaKey) modifiers.push('Meta');
    keystrokes.push({
      timestamp: Date.now() - recordingStartTime,
      key,
      modifiers,
    });
  });

  hook.start();
}

export async function stopInputTracking(): Promise<{
  clicks: MouseClickEvent[];
  moves: MouseMoveEvent[];
  keystrokes: KeystrokeEvent[];
}> {
  if (uIOhook) {
    try {
      uIOhook.stop();
      uIOhook.removeAllListeners();
    } catch {
      /* ignore */
    }
  }

  return {
    clicks: [...clicks],
    moves: [...moves],
    keystrokes: [...keystrokes],
  };
}
