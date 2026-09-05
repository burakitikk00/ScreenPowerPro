export interface VideoDebugEntry {
  time: string;
  source: string;
  message: string;
  data?: unknown;
}

const MAX_LOGS = 100;
const logs: VideoDebugEntry[] = [];
const listeners = new Set<() => void>();

function timestamp() {
  return new Date().toISOString().slice(11, 23);
}

export function videoLog(source: string, message: string, data?: unknown) {
  const entry: VideoDebugEntry = { time: timestamp(), source, message, data };
  logs.push(entry);
  if (logs.length > MAX_LOGS) logs.shift();
  console.log(`[VideoDebug][${source}]`, message, data ?? '');
  listeners.forEach((fn) => fn());
}

export function getVideoLogs(): VideoDebugEntry[] {
  return [...logs];
}

export function clearVideoLogs() {
  logs.length = 0;
  listeners.forEach((fn) => fn());
}

export function subscribeVideoLogs(fn: () => void) {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

export function describeReadyState(state: number): string {
  const map: Record<number, string> = {
    0: 'HAVE_NOTHING',
    1: 'HAVE_METADATA',
    2: 'HAVE_CURRENT_DATA',
    3: 'HAVE_FUTURE_DATA',
    4: 'HAVE_ENOUGH_DATA',
  };
  return map[state] ?? `UNKNOWN(${state})`;
}

export function describeNetworkState(state: number): string {
  const map: Record<number, string> = {
    0: 'NETWORK_EMPTY',
    1: 'NETWORK_IDLE',
    2: 'NETWORK_LOADING',
    3: 'NETWORK_NO_SOURCE',
  };
  return map[state] ?? `UNKNOWN(${state})`;
}

export function describeMediaError(video: HTMLVideoElement): string | null {
  const err = video.error;
  if (!err) return null;
  const codes: Record<number, string> = {
    1: 'MEDIA_ERR_ABORTED',
    2: 'MEDIA_ERR_NETWORK',
    3: 'MEDIA_ERR_DECODE',
    4: 'MEDIA_ERR_SRC_NOT_SUPPORTED',
  };
  return `${codes[err.code] ?? err.code}: ${err.message}`;
}

export function snapshotVideo(video: HTMLVideoElement) {
  return {
    src: video.currentSrc || video.src,
    readyState: describeReadyState(video.readyState),
    networkState: describeNetworkState(video.networkState),
    paused: video.paused,
    ended: video.ended,
    currentTime: video.currentTime,
    duration: video.duration,
    videoWidth: video.videoWidth,
    videoHeight: video.videoHeight,
    error: describeMediaError(video),
  };
}
