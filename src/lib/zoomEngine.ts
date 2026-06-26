import { v4 as uuidv4 } from 'uuid';
import type { MouseClickEvent, ZoomEffect, AutoZoomMode } from '../types';

const DEFAULT_ZOOM_DURATION = 2.0;
const DEFAULT_SCALE = 1.5;
const MIN_CLICK_GAP_MS = 800;

export function generateZoomEffectsFromClicks(
  clicks: MouseClickEvent[],
  autoZoom: AutoZoomMode,
  defaultScale = DEFAULT_SCALE
): ZoomEffect[] {
  if (autoZoom === 'none') return [];

  const downClicks = clicks.filter((c) => c.type === 'left_down' || c.type === 'right_down');
  const effects: ZoomEffect[] = [];
  let lastTime = -MIN_CLICK_GAP_MS;

  for (const click of downClicks) {
    if (click.timestamp - lastTime < MIN_CLICK_GAP_MS) continue;
    lastTime = click.timestamp;

    const startTimeSec = click.timestamp / 1000;
    const easing = autoZoom === 'instant' ? 'instant' : 'ease-in-out';
    const duration = autoZoom === 'instant' ? 0.3 : DEFAULT_ZOOM_DURATION;

    effects.push({
      id: uuidv4(),
      startTime: Math.max(0, startTimeSec - 0.1),
      duration,
      targetX: click.x,
      targetY: click.y,
      scale: defaultScale,
      easing,
    });
  }

  return effects;
}

export function getActiveZoomAtTime(
  effects: ZoomEffect[],
  timeSec: number
): { scale: number; translateX: number; translateY: number } | null {
  const active = effects.find(
    (e) => timeSec >= e.startTime && timeSec <= e.startTime + e.duration
  );
  if (!active) return null;

  const progress = (timeSec - active.startTime) / active.duration;
  let t = progress;
  if (active.easing === 'ease-in-out') {
    t = progress < 0.5 ? 2 * progress * progress : 1 - Math.pow(-2 * progress + 2, 2) / 2;
  } else if (active.easing === 'instant') {
    t = progress < 0.1 ? 0 : progress > 0.9 ? 1 : 1;
  }

  const scale = 1 + (active.scale - 1) * t;
  const translateX = -(active.targetX * (scale - 1));
  const translateY = -(active.targetY * (scale - 1));

  return { scale, translateX, translateY };
}

export function normalizeDuration(value: number, fallback = 0): number {
  if (Number.isFinite(value) && value > 0) return value;
  if (Number.isFinite(fallback) && fallback > 0) return fallback;
  return 0;
}

export async function resolveVideoDuration(
  video: HTMLVideoElement,
  projectPath: string
): Promise<number> {
  if (Number.isFinite(video.duration) && video.duration > 0 && video.duration !== Infinity) {
    return video.duration;
  }

  if (video.seekable.length > 0) {
    const end = video.seekable.end(video.seekable.length - 1);
    if (Number.isFinite(end) && end > 0) {
      return end;
    }
  }

  try {
    const meta = await window.electronAPI.loadProjectMetadata(projectPath);
    if (meta.duration > 0) return meta.duration;
  } catch {
    /* metadata yok */
  }

  return 0;
}

export function formatTimecode(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) return '--:--';
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = Math.floor(seconds % 60);
  if (h > 0) {
    return `${h.toString().padStart(2, '0')}:${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
  }
  return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
}

export function buildZoompanFilter(
  effects: ZoomEffect[],
  videoWidth: number,
  videoHeight: number,
  fps: number
): string {
  if (effects.length === 0) {
    return `scale=${videoWidth}:${videoHeight}`;
  }

  const zExpr = effects
    .map((e) => {
      const end = e.startTime + e.duration;
      return `if(between(t,${e.startTime.toFixed(3)},${end.toFixed(3)}),${e.scale},1)`;
    })
    .reduce((acc, cur) => (acc === '1' ? cur : `if(eq(${acc},1),${cur},${acc})`));

  const xExpr = effects
    .map((e) => {
      const end = e.startTime + e.duration;
      const cx = e.targetX - videoWidth / (2 * e.scale);
      return `if(between(t,${e.startTime.toFixed(3)},${end.toFixed(3)}),${cx.toFixed(0)},iw/2-(iw/zoom/2))`;
    })
    .join(':');

  const yExpr = effects
    .map((e) => {
      const end = e.startTime + e.duration;
      const cy = e.targetY - videoHeight / (2 * e.scale);
      return `if(between(t,${e.startTime.toFixed(3)},${end.toFixed(3)}),${cy.toFixed(0)},ih/2-(ih/zoom/2))`;
    })
    .join(':');

  return `zoompan=z='${zExpr}':x='${xExpr}':y='${yExpr}':d=1:s=${videoWidth}x${videoHeight}:fps=${fps}`;
}
