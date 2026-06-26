import { useRef, useCallback, useEffect } from 'react';
import { useEditorStore } from '../../stores/editorStore';
import { useAppStore } from '../../stores/appStore';
import { formatTimecode, normalizeDuration } from '../../lib/zoomEngine';
import { videoLog } from '../../lib/videoDebug';
import type { TrackState } from '../../types';

const PIXELS_PER_SECOND = 50;

export default function Timeline() {
  const {
    duration,
    currentTime,
    zoomEffects,
    timelineZoom,
    timelineScroll,
    selectedZoomId,
    isPlaying,
    videoTrack,
    micTrack,
    sysTrack,
    setCurrentTime,
    setIsPlaying,
    setTimelineZoom,
    setTimelineScroll,
    setSelectedZoomId,
    updateZoomEffect,
    removeZoomEffect,
    updateTrack,
  } = useEditorStore();

  const { currentProject } = useAppStore();

  const dragRef = useRef<{
    id: string;
    type: 'move' | 'resize-left' | 'resize-right';
    startX: number;
    origStart: number;
    origDuration: number;
    trackType?: 'video' | 'mic' | 'sys';
  } | null>(null);

  const panRef = useRef<{ isPanning: boolean; startX: number; origScroll: number }>({ isPanning: false, startX: 0, origScroll: 0 });
  const containerRef = useRef<HTMLDivElement>(null);

  const safeDuration = normalizeDuration(duration, 60);
  const scale = timelineZoom / 50;
  const pps = PIXELS_PER_SECOND * scale;
  const totalWidth = Math.max(safeDuration * pps + 500, 1200);
  const playheadX = currentTime * pps;

  // Handle Ctrl+Scroll for zoom, Shift+Scroll or Normal Scroll for Pan
  const handleWheel = (e: React.WheelEvent) => {
    if (e.ctrlKey) {
      e.preventDefault();
      // Zoom in/out
      const delta = e.deltaY > 0 ? -5 : 5;
      const newZoom = Math.max(10, Math.min(200, timelineZoom + delta));
      setTimelineZoom(newZoom);
    } else {
      // Pan
      const delta = Math.abs(e.deltaX) > Math.abs(e.deltaY) ? e.deltaX : e.deltaY;
      setTimelineScroll(Math.max(0, timelineScroll + delta));
    }
  };

  // Middle mouse click pan
  const handleTimelineMouseDown = (e: React.MouseEvent) => {
    if (e.button === 1 || e.button === 0) { // Middle click or left click on empty space
      if ((e.target as HTMLElement).closest('.track-item')) return; // Ignore if clicking an item
      panRef.current = { isPanning: true, startX: e.clientX, origScroll: timelineScroll };
    }
  };

  const handleMouseDownItem = (
    e: React.MouseEvent,
    id: string,
    type: 'move' | 'resize-left' | 'resize-right',
    trackType?: 'video' | 'mic' | 'sys'
  ) => {
    e.stopPropagation();
    
    if (trackType) {
      const track = trackType === 'video' ? videoTrack : trackType === 'mic' ? micTrack : sysTrack;
      dragRef.current = {
        id,
        type,
        trackType,
        startX: e.clientX,
        origStart: track.offset,
        origDuration: safeDuration - track.trimStart - track.trimEnd, // effective duration
      };
    } else {
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
    }
  };

  const handleMouseMove = useCallback(
    (e: MouseEvent) => {
      if (panRef.current.isPanning) {
        const dx = panRef.current.startX - e.clientX;
        setTimelineScroll(Math.max(0, panRef.current.origScroll + dx));
        return;
      }

      if (!dragRef.current) return;
      const dx = (e.clientX - dragRef.current.startX) / pps;
      const { id, type, trackType, origStart, origDuration } = dragRef.current;

      if (trackType) {
        const track = trackType === 'video' ? videoTrack : trackType === 'mic' ? micTrack : sysTrack;
        
        if (type === 'move') {
          updateTrack(trackType, { offset: Math.max(0, origStart + dx) });
        } else if (type === 'resize-left') {
          // Adjust trimStart and offset
          const diff = Math.max(-track.trimStart, dx);
          // ensure we don't trim more than total duration
          if (track.trimStart + diff + track.trimEnd < safeDuration - 0.5) {
            updateTrack(trackType, { 
              trimStart: Math.max(0, track.trimStart + diff),
              offset: Math.max(0, origStart + diff)
            });
          }
        } else if (type === 'resize-right') {
          // Adjust trimEnd
          const diff = -dx;
          if (track.trimStart + track.trimEnd + diff < safeDuration - 0.5) {
            updateTrack(trackType, {
              trimEnd: Math.max(0, track.trimEnd + diff)
            });
          }
        }
        return;
      }

      // Zoom effect dragging
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
    [pps, updateZoomEffect, updateTrack, videoTrack, micTrack, sysTrack, timelineScroll]
  );

  const handleMouseUp = useCallback(() => {
    dragRef.current = null;
    panRef.current.isPanning = false;
  }, []);

  useEffect(() => {
    window.addEventListener('mousemove', handleMouseMove);
    window.addEventListener('mouseup', handleMouseUp);
    return () => {
      window.removeEventListener('mousemove', handleMouseMove);
      window.removeEventListener('mouseup', handleMouseUp);
    };
  }, [handleMouseMove, handleMouseUp]);

  useEffect(() => {
    if (containerRef.current) {
      containerRef.current.scrollLeft = timelineScroll;
    }
  }, [timelineScroll]);

  const rulerMarks: number[] = [];
  const maxMarks = 120;
  for (let t = 0; t <= safeDuration + 60 && rulerMarks.length < maxMarks; t += 5) {
    rulerMarks.push(t);
  }

  const renderTrack = (
    label: string, 
    icon: string, 
    track: TrackState, 
    type: 'video' | 'mic' | 'sys',
    colorClass: string
  ) => {
    const effectiveDur = Math.max(0.1, safeDuration - track.trimStart - track.trimEnd);
    return (
      <div className="h-16 border-b border-white/5 relative flex items-center px-2">
        <div
          className={`track-item absolute left-0 h-[40px] ${colorClass} rounded-md border border-white/20 shadow-sm cursor-pointer hover:border-white/40 transition-colors`}
          style={{ 
            left: track.offset * pps,
            width: effectiveDur * pps 
          }}
          onMouseDown={(e) => handleMouseDownItem(e, type, 'move', type)}
        >
          <div className="absolute left-2 top-1 text-[11px] font-medium text-white/90 bg-black/40 px-1.5 py-0.5 rounded truncate max-w-[90%] pointer-events-none">
            {icon} {label}
          </div>
          <div
            className="absolute left-0 w-3 h-full cursor-ew-resize hover:bg-white/20 rounded-l-md"
            onMouseDown={(e) => handleMouseDownItem(e, type, 'resize-left', type)}
          />
          <div
            className="absolute right-0 w-3 h-full cursor-ew-resize hover:bg-white/20 rounded-r-md"
            onMouseDown={(e) => handleMouseDownItem(e, type, 'resize-right', type)}
          />
        </div>
      </div>
    );
  };

  return (
    <div 
      className="h-[300px] bg-[#16161c] border-t border-white/10 flex flex-col z-20 shadow-[0_-10px_30px_rgba(0,0,0,0.4)]"
      onWheel={handleWheel}
    >
      <div className="h-10 border-b border-white/5 flex items-center justify-between px-4 bg-[#1e1e26] shrink-0">
        <div className="flex items-center gap-2">
          <button
            onClick={() => selectedZoomId && removeZoomEffect(selectedZoomId)}
            className="w-8 h-8 flex items-center justify-center rounded hover:bg-red-500/20 text-red-500 transition-colors disabled:opacity-50"
            disabled={!selectedZoomId}
            title="Seçili Efekti Sil"
          >
            <span className="material-symbols-outlined text-[18px]">delete</span>
          </button>
        </div>
        <div className="flex items-center gap-4">
          <div className="font-mono text-xs tracking-wider flex items-center gap-1 bg-black/40 px-3 py-1 rounded-md border border-white/5">
            <span className="text-indigo-400">{formatTimecode(currentTime)}</span>
            <span className="text-white/30">/</span>
            <span className="text-white/50">{formatTimecode(safeDuration)}</span>
          </div>
          <button
            onClick={() => {
              videoLog('Timeline', 'Timeline oynat butonu', { wasPlaying: isPlaying });
              setIsPlaying(!isPlaying);
            }}
            className="w-8 h-8 flex items-center justify-center rounded bg-indigo-500 text-white hover:bg-indigo-600 transition-colors shadow-lg"
          >
            <span className="material-symbols-outlined text-[20px] fill">
              {isPlaying ? 'pause' : 'play_arrow'}
            </span>
          </button>
        </div>
        <div className="flex items-center gap-2 w-32">
          <span className="material-symbols-outlined text-[16px] text-white/50">zoom_out</span>
          <input
            type="range"
            min={10}
            max={200}
            value={timelineZoom}
            onChange={(e) => setTimelineZoom(Number(e.target.value))}
            className="flex-1 accent-indigo-500"
          />
          <span className="material-symbols-outlined text-[16px] text-white/50">zoom_in</span>
        </div>
      </div>

      <div className="flex-1 flex overflow-hidden relative">
        {/* Sol Sidebar - Track İsimleri */}
        <div className="w-[180px] bg-[#1e1e26] border-r border-white/5 flex flex-col shrink-0 z-30">
          <div className="h-6 border-b border-white/5 bg-black/20" />
          <div className="h-16 border-b border-white/5 flex items-center px-4 gap-2">
            <span className="material-symbols-outlined text-[16px] text-indigo-400">videocam</span>
            <span className="font-medium text-xs text-white/90">Ekran Kaydı</span>
          </div>
          {currentProject?.microphonePath && (
            <div className="h-16 border-b border-white/5 flex items-center px-4 gap-2">
              <span className="material-symbols-outlined text-[16px] text-green-400">mic</span>
              <span className="font-medium text-xs text-white/90">Mikrofon</span>
            </div>
          )}
          {currentProject?.systemAudioPath && (
            <div className="h-16 border-b border-white/5 flex items-center px-4 gap-2">
              <span className="material-symbols-outlined text-[16px] text-blue-400">speaker</span>
              <span className="font-medium text-xs text-white/90">Sistem Sesi</span>
            </div>
          )}
          <div className="h-12 flex items-center px-4 gap-2">
            <span className="material-symbols-outlined text-[16px] text-purple-400">zoom_in_map</span>
            <span className="font-medium text-xs text-white/90">Zoom Efektleri</span>
          </div>
        </div>

        {/* Scrollable Timeline Alanı */}
        <div
          ref={containerRef}
          className="flex-1 overflow-x-hidden relative bg-[#121217]"
          onMouseDown={handleTimelineMouseDown}
        >
          {/* Timeline Grid (İçerik) */}
          <div 
            className="relative"
            style={{ width: totalWidth, minHeight: '100%' }}
            onClick={(e) => {
              if (dragRef.current || panRef.current.isPanning) return;
              const rect = e.currentTarget.getBoundingClientRect();
              const x = e.clientX - rect.left;
              setCurrentTime(Math.max(0, x / pps));
            }}
          >
            {/* Playhead */}
            <div
              className="absolute top-0 bottom-0 w-px bg-indigo-500 z-40 pointer-events-none"
              style={{ left: playheadX }}
            >
              <div className="absolute -top-1 -left-2 w-4 h-4 bg-indigo-500 rounded-sm rotate-45 shadow-[0_0_10px_rgba(99,102,241,0.5)]" />
            </div>

            {/* Ruler */}
            <div className="h-6 border-b border-white/5 flex items-end sticky top-0 bg-[#1e1e26]/90 backdrop-blur z-20 text-[10px] text-white/40 select-none pointer-events-none">
              {rulerMarks.map((t) => (
                <div
                  key={t}
                  className="border-l border-white/10 pl-1 pb-0.5 h-3 shrink-0"
                  style={{ width: 5 * pps }}
                >
                  {formatTimecode(t)}
                </div>
              ))}
            </div>

            {/* Tracks */}
            <div className="flex flex-col">
              {renderTrack('Video', '🎥', videoTrack, 'video', 'bg-indigo-500/30')}
              {currentProject?.microphonePath && renderTrack('Mikrofon', '🎤', micTrack, 'mic', 'bg-green-500/30')}
              {currentProject?.systemAudioPath && renderTrack('Sistem Sesi', '🔊', sysTrack, 'sys', 'bg-blue-500/30')}
              
              {/* Zoom Effects Track */}
              <div className="h-12 relative flex items-center mt-1">
                {zoomEffects.map((effect, i) => (
                  <div
                    key={effect.id}
                    onMouseDown={(e) => handleMouseDownItem(e, effect.id, 'move')}
                    className={`track-item absolute h-[24px] top-1/2 -translate-y-1/2 rounded-full flex items-center justify-center cursor-pointer transition-colors ${
                      selectedZoomId === effect.id
                        ? 'bg-purple-500/40 border-2 border-purple-400 shadow-[0_0_15px_rgba(168,85,247,0.4)] z-10'
                        : 'bg-purple-500/20 border border-purple-500/50 hover:border-purple-400 z-0'
                    }`}
                    style={{
                      left: effect.startTime * pps,
                      width: effect.duration * pps,
                    }}
                  >
                    <span className="text-[10px] font-medium text-purple-300 pointer-events-none">Zoom {i + 1}</span>
                    <div
                      className="absolute left-0 w-3 h-full cursor-ew-resize hover:bg-white/20 rounded-l-full"
                      onMouseDown={(e) => handleMouseDownItem(e, effect.id, 'resize-left')}
                    />
                    <div
                      className="absolute right-0 w-3 h-full cursor-ew-resize hover:bg-white/20 rounded-r-full"
                      onMouseDown={(e) => handleMouseDownItem(e, effect.id, 'resize-right')}
                    />
                  </div>
                ))}
              </div>
            </div>

          </div>
        </div>
      </div>
    </div>
  );
}
