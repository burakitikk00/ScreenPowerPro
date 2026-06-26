import { useState, useEffect } from 'react';
import {
  getVideoLogs,
  subscribeVideoLogs,
  clearVideoLogs,
  videoLog,
} from '../../lib/videoDebug';

export interface VideoDebugPanelProps {
  videoState: Record<string, unknown> | null;
  fileInfo: {
    fullPath: string;
    exists: boolean;
    size: number;
    url: string | null;
  } | null;
}

export default function VideoDebugPanel({ videoState, fileInfo }: VideoDebugPanelProps) {
  const [open, setOpen] = useState(true);
  const [, tick] = useState(0);

  useEffect(() => {
    return subscribeVideoLogs(() => tick((n) => n + 1));
  }, []);

  const logs = getVideoLogs();

  return (
    <div className="absolute bottom-2 left-2 right-2 z-30">
      <button
        onClick={() => setOpen((o) => !o)}
        className="mb-1 px-2 py-1 rounded bg-black/70 text-[10px] text-primary border border-white/10 font-mono"
      >
        {open ? '▼' : '▶'} Video Debug ({logs.length} log)
      </button>
      {open && (
        <div className="bg-black/85 border border-white/10 rounded-lg p-3 max-h-48 overflow-y-auto font-mono text-[10px] text-on-surface-variant space-y-2">
          <div className="flex gap-2">
            <button
              onClick={() => {
                clearVideoLogs();
                videoLog('UI', 'Loglar temizlendi');
              }}
              className="px-2 py-0.5 rounded bg-white/10 hover:bg-white/20 text-on-surface"
            >
              Temizle
            </button>
            <span className="text-on-surface-variant">
              DevTools: Ctrl+Shift+I | Terminal: [MediaProtocol] logları
            </span>
          </div>

          {fileInfo && (
            <div className="text-yellow-300/90 space-y-0.5 border-b border-white/10 pb-2">
              <div>dosya: {fileInfo.fullPath}</div>
              <div>var: {String(fileInfo.exists)} | boyut: {(fileInfo.size / 1024 / 1024).toFixed(2)} MB</div>
              <div className="break-all">url: {fileInfo.url ?? 'null'}</div>
            </div>
          )}

          {videoState && (
            <div className="text-cyan-300/90 space-y-0.5 border-b border-white/10 pb-2">
              {Object.entries(videoState).map(([k, v]) => (
                <div key={k}>
                  {k}: {typeof v === 'object' ? JSON.stringify(v) : String(v)}
                </div>
              ))}
            </div>
          )}

          <div className="space-y-0.5">
            {logs.length === 0 && <div className="text-on-surface-variant">Henüz log yok...</div>}
            {logs.map((l, i) => (
              <div key={i} className="break-all">
                <span className="text-white/40">{l.time}</span>{' '}
                <span className="text-primary">[{l.source}]</span> {l.message}
                {l.data !== undefined && (
                  <span className="text-white/50"> {JSON.stringify(l.data)}</span>
                )}
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
