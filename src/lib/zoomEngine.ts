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

export interface Keyframe {
  time: number;
  scale: number;
  x: number;
  y: number;
}

export function buildKeyframes(effects: ZoomEffect[]): Keyframe[] {
  const kfs: Keyframe[] = [];
  if (effects.length === 0) return kfs;

  const PAN_THRESHOLD = 3.0; // seconds
  const ZOOM_IN_TIME = 0.8;
  const ZOOM_OUT_TIME = 0.8;
  const HOLD_TIME = 1.0;
  const PAN_TIME = 0.6;

  const clusters: ZoomEffect[][] = [];
  let currentCluster: ZoomEffect[] = [effects[0]];

  for (let i = 1; i < effects.length; i++) {
    const prev = effects[i - 1];
    const curr = effects[i];
    if (curr.startTime - prev.startTime <= PAN_THRESHOLD) {
      currentCluster.push(curr);
    } else {
      clusters.push(currentCluster);
      currentCluster = [curr];
    }
  }
  clusters.push(currentCluster);

  for (const cluster of clusters) {
    const first = cluster[0];
    
    kfs.push({
      time: Math.max(0, first.startTime - ZOOM_IN_TIME),
      scale: 1.0,
      x: first.targetX,
      y: first.targetY,
    });
    kfs.push({
      time: first.startTime,
      scale: first.scale,
      x: first.targetX,
      y: first.targetY,
    });

    for (let i = 1; i < cluster.length; i++) {
      const prev = cluster[i - 1];
      const curr = cluster[i];
      
      const panStartTime = Math.max(prev.startTime, curr.startTime - PAN_TIME);
      
      kfs.push({
        time: panStartTime,
        scale: prev.scale,
        x: prev.targetX,
        y: prev.targetY,
      });

      kfs.push({
        time: curr.startTime,
        scale: curr.scale,
        x: curr.targetX,
        y: curr.targetY,
      });
    }

    const last = cluster[cluster.length - 1];
    kfs.push({
      time: last.startTime + HOLD_TIME,
      scale: last.scale,
      x: last.targetX,
      y: last.targetY,
    });
    kfs.push({
      time: last.startTime + HOLD_TIME + ZOOM_OUT_TIME,
      scale: 1.0,
      x: last.targetX,
      y: last.targetY,
    });
  }

  kfs.sort((a, b) => a.time - b.time);
  const uniqueKfs: Keyframe[] = [];
  for (const kf of kfs) {
    if (uniqueKfs.length === 0 || uniqueKfs[uniqueKfs.length - 1].time < kf.time) {
      uniqueKfs.push(kf);
    } else if (uniqueKfs[uniqueKfs.length - 1].time === kf.time) {
      uniqueKfs[uniqueKfs.length - 1] = kf;
    }
  }

  return uniqueKfs;
}

function easeInOutCubic(t: number): number {
  return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
}

export function getActiveZoomAtTime(
  effects: ZoomEffect[],
  timeSec: number
): { scale: number; targetX: number; targetY: number } | null {
  if (effects.length === 0) return null;
  const kfs = buildKeyframes(effects);

  if (timeSec <= kfs[0].time) return null;
  if (timeSec >= kfs[kfs.length - 1].time) return null;

  for (let i = 0; i < kfs.length - 1; i++) {
    const k1 = kfs[i];
    const k2 = kfs[i + 1];
    if (timeSec >= k1.time && timeSec <= k2.time) {
      if (k1.time === k2.time) return { scale: k2.scale, targetX: k2.x, targetY: k2.y };
      
      const progress = (timeSec - k1.time) / (k2.time - k1.time);
      const t = easeInOutCubic(progress);
      
      const scale = k1.scale + (k2.scale - k1.scale) * t;
      const targetX = k1.x + (k2.x - k1.x) * t;
      const targetY = k1.y + (k2.y - k1.y) * t;
      
      return { scale, targetX, targetY };
    }
  }
  return null;
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
      // cx = targetX - width/(2*scale)
      // clamp to [0, videoWidth - width/scale]
      const vwExpr = `(iw/${e.scale})`;
      const rawCx = `(${e.targetX}-${vwExpr}/2)`;
      const clampX = `max(0, min(${rawCx}, iw-${vwExpr}))`;
      return `if(between(t,${e.startTime.toFixed(3)},${end.toFixed(3)}),${clampX},iw/2-(iw/zoom/2))`;
    })
    .join(':');

  const yExpr = effects
    .map((e) => {
      const end = e.startTime + e.duration;
      // cy = targetY - height/(2*scale)
      // clamp to [0, videoHeight - height/scale]
      const vhExpr = `(ih/${e.scale})`;
      const rawCy = `(${e.targetY}-${vhExpr}/2)`;
      const clampY = `max(0, min(${rawCy}, ih-${vhExpr}))`;
      return `if(between(t,${e.startTime.toFixed(3)},${end.toFixed(3)}),${clampY},ih/2-(ih/zoom/2))`;
    })
    .join(':');

  return `zoompan=z='${zExpr}':x='${xExpr}':y='${yExpr}':d=1:s=${videoWidth}x${videoHeight}:fps=${fps}`;
}
