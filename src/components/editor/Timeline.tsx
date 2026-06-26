import { useRef, useCallback, useEffect } from 'react';
import { useEditorStore } from '../../stores/editorStore';
import { formatTimecode } from '../../lib/zoomEngine';

const PIXELS_PER_SECOND = 50;

export default function Timeline() {
  const {
    duration,
    currentTime,
    zoomEffects,
    timelineZoom,
    selectedZoomId,
    isPlaying,
    setCurrentTime,
    setIsPlaying,
    setTimelineZoom,
    setSelectedZoomId,
    updateZoomEffect,
    removeZoomEffect,
  } = useEditorStore();

  const dragRef = useRef<{ id: string; type: 'move' | 'resize-left' | 'resize-right'; startX: number; origStart: number; origDuration: number } | null>(null);

  const scale = timelineZoom / 50;
  const pps = PIXELS_PER_SECOND * scale;
  const totalWidth = Math.max(duration * pps + 200, 800);
  const playheadX = currentTime * pps;

  const handleMouseDown = (
    e: React.MouseEvent,
    id: string,
    type: 'move' | 'resize-left' | 'resize-right'
  ) => {
    e.stopPropagation();
    const effect = zoomEffects.find((z) => z.id === id);
    if (!effect) return;
    setSelectedZoomId(id);
    dragRef.current = {
      id,
      type,
      startX: e.clientX,
      origStart: effect.startTime,
      origDuration: effect.duration,
    };
  };

  const handleMouseMove = useCallback(
    (e: MouseEvent) => {
      if (!dragRef.current) return;
      const dx = (e.clientX - dragRef.current.startX) / pps;
      const { id, type, origStart, origDuration } = dragRef.current;

      if (type === 'move') {
        updateZoomEffect(id, { startTime: Math.max(0, origStart + dx) });
      } else if (type === 'resize-left') {
        const newStart = Math.max(0, origStart + dx);
        const newDuration = origDuration - (newStart - origStart);
        if (newDuration > 0.2) {
          updateZoomEffect(id, { startTime: newStart, duration: newDuration });
        }
      } else if (type === 'resize-right') {
        updateZoomEffect(id, { duration: Math.max(0.2, origDuration + dx) });
      }
    },
    [pps, updateZoomEffect]
  );

  const handleMouseUp = useCallback(() => {
    dragRef.current = null;
  }, []);

  useEffect(() => {
    window.addEventListener('mousemove', handleMouseMove);
    window.addEventListener('mouseup', handleMouseUp);
    return () => {
      window.removeEventListener('mousemove', handleMouseMove);
      window.removeEventListener('mouseup', handleMouseUp);
    };
  }, [handleMouseMove, handleMouseUp]);

  const rulerMarks = [];
  for (let t = 0; t <= duration + 10; t += 10) {
    rulerMarks.push(t);
  }

  return (
    <div className="h-[250px] bg-surface-container-low border-t border-white/10 flex flex-col z-20 shadow-[0_-10px_30px_rgba(0,0,0,0.3)]">
      <div className="h-10 border-b border-white/5 flex items-center justify-between px-4 bg-surface-container/50 backdrop-blur-md">
        <div className="flex items-center gap-2">
          <button
            onClick={() => selectedZoomId && removeZoomEffect(selectedZoomId)}
            className="w-8 h-8 flex items-center justify-center rounded hover:bg-error/20 text-error transition-colors"
            title="Sil"
          >
            <span className="material-symbols-outlined text-[18px]">delete</span>
          </button>
        </div>
        <div className="flex items-center gap-4">
          <div className="font-label-md text-label-md tracking-wider flex items-center gap-1 bg-black/40 px-3 py-1 rounded-md border border-white/5">
            <span className="text-primary">{formatTimecode(currentTime)}</span>
            <span className="text-on-surface-variant">/</span>
            <span className="text-on-surface-variant">{formatTimecode(duration)}</span>
          </div>
          <div className="flex items-center gap-1 bg-surface-container-high rounded-lg p-0.5 border border-white/5">
            <button
              onClick={() => setIsPlaying(!isPlaying)}
              className="w-8 h-8 flex items-center justify-center rounded bg-primary text-on-primary hover:bg-primary/90 transition-colors"
            >
              <span className="material-symbols-outlined text-[20px] fill">
                {isPlaying ? 'pause' : 'play_arrow'}
              </span>
            </button>
          </div>
        </div>
        <div className="flex items-center gap-2 w-32">
          <span className="material-symbols-outlined text-[16px] text-on-surface-variant">zoom_out</span>
          <input
            type="range"
            min={10}
            max={100}
            value={timelineZoom}
            onChange={(e) => setTimelineZoom(Number(e.target.value))}
            className="flex-1"
          />
          <span className="material-symbols-outlined text-[16px] text-on-surface-variant">zoom_in</span>
        </div>
      </div>

      <div className="flex-1 flex overflow-hidden relative">
        <div className="w-[200px] bg-surface-container border-r border-white/5 flex flex-col shrink-0 z-10">
          <div className="h-6 border-b border-white/5 bg-surface-container-lowest" />
          <div className="h-20 border-b border-white/5 flex items-center px-4 gap-2">
            <span className="material-symbols-outlined text-[18px] text-primary">videocam</span>
            <span className="font-label-md text-label-md text-on-surface">Ekran Kaydı</span>
          </div>
          <div className="h-16 border-b border-white/5 flex items-center px-4 gap-2">
            <span className="material-symbols-outlined text-[18px] text-tertiary">zoom_in_map</span>
            <span className="font-label-md text-label-md text-on-surface">Zoom Efektleri</span>
          </div>
        </div>

        <div
          className="flex-1 overflow-x-auto relative timeline-grid bg-surface-container-lowest cursor-pointer"
          onClick={(e) => {
            const rect = e.currentTarget.getBoundingClientRect();
            const x = e.clientX - rect.left + e.currentTarget.scrollLeft;
            setCurrentTime(Math.max(0, Math.min(duration, x / pps)));
          }}
        >
          <div
            className="absolute top-0 bottom-0 w-px bg-secondary z-30 pointer-events-none"
            style={{ left: playheadX }}
          >
            <div className="absolute -top-1 -left-2 w-4 h-4 bg-secondary rounded-sm rotate-45 shadow-[0_0_10px_rgba(255,178,183,0.5)]" />
          </div>

          <div className="h-6 border-b border-white/5 flex items-end sticky top-0 bg-surface-container-lowest/90 backdrop-blur z-20 text-[10px] text-on-surface-variant select-none">
            {rulerMarks.map((t) => (
              <div
                key={t}
                className="border-l border-white/20 pl-1 pb-0.5 h-3 shrink-0"
                style={{ width: 10 * pps }}
              >
                {formatTimecode(t)}
              </div>
            ))}
          </div>

          <div className="h-20 border-b border-white/5 relative flex items-center px-2">
            <div
              className="absolute left-0 h-[56px] bg-primary-container/20 border border-primary/50 rounded-md"
              style={{ width: duration * pps }}
            >
              <div className="absolute left-2 top-1 text-[11px] font-medium text-primary bg-black/40 px-1 rounded">
                display-0.webm
              </div>
            </div>
          </div>

          <div className="h-16 border-b border-white/5 relative" style={{ width: totalWidth }}>
            {zoomEffects.map((effect, i) => (
              <div
                key={effect.id}
                onMouseDown={(e) => handleMouseDown(e, effect.id, 'move')}
                className={`absolute h-[32px] top-1/2 -translate-y-1/2 rounded-full flex items-center justify-center cursor-pointer transition-colors ${
                  selectedZoomId === effect.id
                    ? 'bg-tertiary/20 border-2 border-tertiary shadow-[0_0_15px_rgba(208,188,255,0.2)]'
                    : 'bg-tertiary-container/30 border border-tertiary/50 hover:border-tertiary'
                }`}
                style={{
                  left: effect.startTime * pps,
                  width: effect.duration * pps,
                }}
              >
                <span className="text-[11px] font-medium text-tertiary">Zoom {i + 1}</span>
                <div
                  className="absolute left-0 w-3 h-full cursor-ew-resize"
                  onMouseDown={(e) => handleMouseDown(e, effect.id, 'resize-left')}
                />
                <div
                  className="absolute right-0 w-3 h-full cursor-ew-resize"
                  onMouseDown={(e) => handleMouseDown(e, effect.id, 'resize-right')}
                />
                {selectedZoomId === effect.id && (
                  <button
                    onClick={(e) => {
                      e.stopPropagation();
                      removeZoomEffect(effect.id);
                    }}
                    className="absolute -top-2 -right-2 w-4 h-4 bg-tertiary rounded-full border-2 border-surface flex items-center justify-center"
                  >
                    <span className="material-symbols-outlined text-[10px] text-on-tertiary font-bold">
                      close
                    </span>
                  </button>
                )}
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}
