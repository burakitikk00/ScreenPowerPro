import { useRef, useCallback, useEffect } from 'react';
import { useEditorStore } from '../../stores/editorStore';
import { useAppStore } from '../../stores/appStore';
import { formatTimecode, normalizeDuration } from '../../lib/zoomEngine';
import { videoLog } from '../../lib/videoDebug';
import type { ClipSegment } from '../../types';

const PIXELS_PER_SECOND = 50;

export default function Timeline() {
  const {
    duration,
    currentTime,
    zoomEffects,
    timelineZoom,
    timelineScroll,
    selectedZoomId,
    selectedClipId,
    isPlaying,
    videoTrack,
    micTrack,
    sysTrack,
    settings,
    history,
    historyIndex,
    seekTo,
    setIsPlaying,
    setTimelineZoom,
    setTimelineScroll,
    setSelectedZoomId,
    setSelectedClipId,
    updateZoomEffect,
    removeZoomEffect,
    updateSettings,
    updateClip,
    pushHistory,
    undo,
    redo,
    cutAtPlayhead,
    deleteSelected,
    toggleMute,
  } = useEditorStore();

  const { currentProject } = useAppStore();

  const canUndo = historyIndex >= 0;
  const canRedo = historyIndex < history.length - 1;

  const dragRef = useRef<{
    id: string;
    type: 'move' | 'resize-left' | 'resize-right';
    startX: number;
    origStart: number;
    origDuration: number;
    origSourceStart?: number;
    origSourceEnd?: number;
    isClip?: boolean;
    trackType?: 'video' | 'mic' | 'sys';
  } | null>(null);

  const panRef = useRef<{ isPanning: boolean; startX: number; origScroll: number }>({ isPanning: false, startX: 0, origScroll: 0 });
  const containerRef = useRef<HTMLDivElement>(null);

  const videoSpeed = settings.videoSpeed ?? 1;
  const sourceSafeDuration = normalizeDuration(duration, 60);
  const safeDuration = sourceSafeDuration / videoSpeed; // Output safe duration
  const scale = timelineZoom / 50;
  const pps = PIXELS_PER_SECOND * scale;
  const totalWidth = Math.max(safeDuration * pps + 500, 1200);
  const playheadX = (currentTime / videoSpeed) * pps;

  // Keyboard shortcuts
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement)?.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA') return;

      // Undo/Redo
      if ((e.metaKey || e.ctrlKey) && e.key === 'z') {
        e.preventDefault();
        if (e.shiftKey) redo();
        else undo();
      }
      if ((e.metaKey || e.ctrlKey) && e.key === 'y') {
        e.preventDefault();
        redo();
      }

      // Delete selected
      if (e.key === 'Delete' || e.key === 'Backspace') {
        e.preventDefault();
        deleteSelected();
      }

      // Cut at playhead
      if (e.key === 'c' && !e.ctrlKey && !e.metaKey) {
        // Only cut when not in text input
        cutAtPlayhead(currentTime);
      }

      // Space to toggle play
      if (e.key === ' ') {
        e.preventDefault();
        setIsPlaying(!isPlaying);
      }

      // Arrow keys for frame navigation
      if (e.key === 'ArrowLeft') {
        e.preventDefault();
        const step = e.shiftKey ? 5 : 1 / 30; // 5s or 1 frame
        seekTo(Math.max(0, currentTime - step * videoSpeed));
      }
      if (e.key === 'ArrowRight') {
        e.preventDefault();
        const step = e.shiftKey ? 5 : 1 / 30;
        seekTo(Math.min(sourceSafeDuration, currentTime + step * videoSpeed));
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [undo, redo, deleteSelected, cutAtPlayhead, currentTime, isPlaying, setIsPlaying, seekTo, sourceSafeDuration, videoSpeed]);

  const handleWheel = (e: React.WheelEvent) => {
    if (e.ctrlKey) {
      e.preventDefault();
      const delta = e.deltaY > 0 ? -5 : 5;
      const newZoom = Math.max(10, Math.min(200, timelineZoom + delta));
      setTimelineZoom(newZoom);
    } else {
      const delta = Math.abs(e.deltaX) > Math.abs(e.deltaY) ? e.deltaX : e.deltaY;
      setTimelineScroll(Math.max(0, timelineScroll + delta));
    }
  };

  const handleTimelineMouseDown = (e: React.MouseEvent) => {
    if (e.button === 1 || e.button === 0) {
      if ((e.target as HTMLElement).closest('.track-item')) return;
      panRef.current = { isPanning: true, startX: e.clientX, origScroll: timelineScroll };
    }
  };

  const handleMouseDownClip = (
    e: React.MouseEvent,
    clip: ClipSegment,
    type: 'move' | 'resize-left' | 'resize-right',
    trackType: 'video' | 'mic' | 'sys'
  ) => {
    e.stopPropagation();
    setSelectedClipId(clip.id);
    pushHistory();
    const clipDuration = clip.sourceEnd - clip.sourceStart;
    dragRef.current = {
      id: clip.id,
      type,
      isClip: true,
      trackType,
      startX: e.clientX,
      origStart: clip.trackOffset,
      origDuration: clipDuration,
      origSourceStart: clip.sourceStart,
      origSourceEnd: clip.sourceEnd,
    };
  };

  const handleMouseDownZoom = (
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
      if (panRef.current.isPanning) {
        const dx = panRef.current.startX - e.clientX;
        setTimelineScroll(Math.max(0, panRef.current.origScroll + dx));
        return;
      }
      if (!dragRef.current) return;
      const dx = (e.clientX - dragRef.current.startX) / pps;
      const dxSource = dx * videoSpeed;
      const { id, type, origStart, origDuration, origSourceStart, origSourceEnd, isClip, trackType } = dragRef.current;

      if (isClip && trackType && origSourceStart !== undefined && origSourceEnd !== undefined) {
        const trackState = trackType === 'video' ? videoTrack : trackType === 'mic' ? micTrack : sysTrack;
        const clips = trackState.clips.slice().sort((a, b) => a.trackOffset - b.trackOffset);
        const idx = clips.findIndex(c => c.id === id);
        const prev = clips[idx - 1];
        const next = clips[idx + 1];

        if (type === 'move') {
          let rawOffset = origStart + dxSource;
          
          if (prev) {
            const prevDur = prev.sourceEnd - prev.sourceStart;
            const prevRightEdge = prev.trackOffset + prevDur;
            const swapThreshold = Math.min(origDuration / 2, prevDur / 2, 0.5);
            
            if (rawOffset < prevRightEdge - swapThreshold) {
              // SWAP
              const newCurrentOffset = prev.trackOffset;
              const newPrevOffset = origStart + origDuration - prevDur;
              
              updateClip(trackType, prev.id, { trackOffset: newPrevOffset });
              updateClip(trackType, id, { trackOffset: newCurrentOffset });
              
              dragRef.current.origStart = newCurrentOffset;
              dragRef.current.startX = e.clientX;
              return;
            } else if (rawOffset < prevRightEdge) {
              // SNAP
              rawOffset = prevRightEdge;
            }
          }
          
          if (next) {
            const nextDur = next.sourceEnd - next.sourceStart;
            const swapThreshold = Math.min(origDuration / 2, nextDur / 2, 0.5);
            
            if (rawOffset + origDuration > next.trackOffset + swapThreshold) {
              // SWAP
              const newNextOffset = origStart;
              const newCurrentOffset = next.trackOffset + nextDur - origDuration;
              
              updateClip(trackType, next.id, { trackOffset: newNextOffset });
              updateClip(trackType, id, { trackOffset: newCurrentOffset });
              
              dragRef.current.origStart = newCurrentOffset;
              dragRef.current.startX = e.clientX;
              return;
            } else if (rawOffset + origDuration > next.trackOffset) {
              // SNAP
              rawOffset = next.trackOffset - origDuration;
            }
          }

          if (rawOffset < 0) rawOffset = 0;
          updateClip(trackType, id, { trackOffset: rawOffset });
        } else if (type === 'resize-left') {
          const minOffset = prev ? prev.trackOffset + (prev.sourceEnd - prev.sourceStart) : 0;
          let newOffset = origStart + dxSource;
          if (newOffset < minOffset) newOffset = minOffset;
          
          let newSourceStart = origSourceStart + (newOffset - origStart);
          if (newSourceStart < 0) {
            newSourceStart = 0;
            newOffset = origStart - origSourceStart;
          }

          const maxLeftOffset = origStart + origDuration - 0.2;
          if (newOffset > maxLeftOffset) {
             newOffset = maxLeftOffset;
             newSourceStart = origSourceStart + (newOffset - origStart);
          }

          updateClip(trackType, id, { 
             trackOffset: newOffset, 
             sourceStart: newSourceStart 
          });
        } else if (type === 'resize-right') {
          const maxOffset = next ? next.trackOffset : Infinity;
          let newDuration = origDuration + dxSource;
          if (origStart + newDuration > maxOffset) {
            newDuration = maxOffset - origStart;
          }
          if (newDuration < 0.2) newDuration = 0.2;
          
          let newSourceEnd = origSourceStart + newDuration;
          
          updateClip(trackType, id, { sourceEnd: newSourceEnd });
        }
        return;
      }

      // Zoom effect dragging
      if (type === 'move') {
        updateZoomEffect(id, { startTime: Math.max(0, origStart + dxSource) });
      } else if (type === 'resize-left') {
        const newStart = Math.max(0, origStart + dxSource);
        const newDuration = origDuration - (newStart - origStart);
        if (newDuration > 0.2) {
          updateZoomEffect(id, { startTime: newStart, duration: newDuration });
        }
      } else if (type === 'resize-right') {
        updateZoomEffect(id, { duration: Math.max(0.2, origDuration + dxSource) });
      }
    },
    [pps, updateZoomEffect, setTimelineScroll, updateClip, videoTrack, micTrack, sysTrack, videoSpeed]
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

  const renderClipTrack = (
    track: { clips: ClipSegment[]; muted: boolean },
    type: 'video' | 'mic' | 'sys',
    colorClass: string,
    selectedColorClass: string,
    waveColor: string,
    showWaveform: boolean
  ) => {
    return (
      <div className="h-14 border-b border-white/5 relative flex items-center">
        {track.clips.map((clip) => {
          const clipDuration = clip.sourceEnd - clip.sourceStart;
          const isSelected = selectedClipId === clip.id;
          return (
            <div
              key={clip.id}
              className={`track-item absolute h-[38px] rounded-lg border shadow-md cursor-pointer group ${
                isSelected
                  ? `${selectedColorClass} border-white/60 ring-1 ring-white/30 z-10`
                  : `${colorClass} border-white/20 hover:border-white/40 z-0 transition-all duration-300 ease-out`
              } ${track.muted ? 'opacity-40' : ''}`}
              style={{
                left: (clip.trackOffset / videoSpeed) * pps,
                width: (clipDuration / videoSpeed) * pps,
              }}
              onMouseDown={(e) => handleMouseDownClip(e, clip, 'move', type)}
              onClick={(e) => { e.stopPropagation(); setSelectedClipId(clip.id); }}
            >
              {/* Waveform decoration — only for audio tracks */}
              {showWaveform && (
                <div className="absolute inset-0 flex items-center px-2 gap-px pointer-events-none overflow-hidden rounded-lg">
                  {Array.from({ length: Math.floor(clipDuration * pps / 3) }).map((_, i) => (
                    <div
                      key={i}
                      className={`w-px shrink-0 rounded-full opacity-40 ${waveColor}`}
                      style={{ height: `${20 + Math.sin(i * 0.7) * 14 + Math.cos(i * 1.3) * 8}%` }}
                    />
                  ))}
                </div>
              )}

              {/* Left resize handle */}
              <div
                className="absolute left-0 w-2 h-full cursor-ew-resize hover:bg-white/30 rounded-l-lg z-20 transition-colors"
                onMouseDown={(e) => handleMouseDownClip(e, clip, 'resize-left', type)}
              >
                <div className="absolute inset-y-0 left-0.5 w-px bg-white/50 my-2 rounded-full" />
              </div>
              {/* Right resize handle */}
              <div
                className="absolute right-0 w-2 h-full cursor-ew-resize hover:bg-white/30 rounded-r-lg z-20 transition-colors"
                onMouseDown={(e) => handleMouseDownClip(e, clip, 'resize-right', type)}
              >
                <div className="absolute inset-y-0 right-0.5 w-px bg-white/50 my-2 rounded-full" />
              </div>
            </div>
          );
        })}
      </div>
    );
  };

  const hasSelectedItem = !!selectedClipId || !!selectedZoomId;

  return (
    <div
      className="h-[310px] border-t border-white/10 flex flex-col z-20"
      style={{ background: 'linear-gradient(180deg, #16161e 0%, #12121a 100%)' }}
      onWheel={handleWheel}
    >
      {/* ─── Toolbar ─────────────────────────────────────────── */}
      <div
        className="h-11 border-b border-white/[0.06] flex items-center px-3 gap-1 shrink-0 select-none"
        style={{ background: 'rgba(255,255,255,0.02)' }}
      >
        {/* Left section: tools */}
        <div className="flex items-center gap-1">
          {/* Timeline label */}
          <div className="flex items-center gap-1.5 px-2.5 py-1 rounded-md bg-white/5 border border-white/8 mr-1">
            <span className="material-symbols-outlined text-[14px] text-white/60">view_timeline</span>
            <span className="text-[12px] font-semibold text-white/70 tracking-wide">Timeline</span>
          </div>

          <div className="w-px h-5 bg-white/10 mx-1" />

          {/* Cut at playhead */}
          <button
            onClick={() => cutAtPlayhead(currentTime)}
            title="Kes — Playhead'de Böl (C)"
            className="w-8 h-8 flex items-center justify-center rounded-md hover:bg-white/10 text-white/50 hover:text-white/90 transition-colors"
          >
            <span className="material-symbols-outlined text-[17px]">content_cut</span>
          </button>

          <div className="w-px h-5 bg-white/10 mx-0.5" />

          {/* Undo */}
          <button
            onClick={undo}
            disabled={!canUndo}
            title="Geri Al (Ctrl+Z)"
            className="w-8 h-8 flex items-center justify-center rounded-md hover:bg-white/10 text-white/50 hover:text-white/90 transition-colors disabled:opacity-30 disabled:cursor-not-allowed"
          >
            <span className="material-symbols-outlined text-[17px]">undo</span>
          </button>

          {/* Redo */}
          <button
            onClick={redo}
            disabled={!canRedo}
            title="İleri Al (Ctrl+Y / Ctrl+Shift+Z)"
            className="w-8 h-8 flex items-center justify-center rounded-md hover:bg-white/10 text-white/50 hover:text-white/90 transition-colors disabled:opacity-30 disabled:cursor-not-allowed"
          >
            <span className="material-symbols-outlined text-[17px]">redo</span>
          </button>

          <div className="w-px h-5 bg-white/10 mx-0.5" />

          {/* Delete selected */}
          <button
            onClick={deleteSelected}
            disabled={!hasSelectedItem}
            title="Seçili Öğeyi Sil (Delete)"
            className="w-8 h-8 flex items-center justify-center rounded-md hover:bg-red-500/20 text-white/50 hover:text-red-400 transition-colors disabled:opacity-30 disabled:cursor-not-allowed"
          >
            <span className="material-symbols-outlined text-[17px]">delete</span>
          </button>
        </div>

        {/* Center: Transport controls + timecode */}
        <div className="flex-1 flex items-center justify-center gap-2">
          {/* Skip backward 5s */}
          <button
            onClick={() => seekTo(Math.max(0, currentTime - 5 * videoSpeed))}
            title="5 Saniye Geri"
            className="w-7 h-7 flex items-center justify-center rounded-full hover:bg-white/10 text-white/40 hover:text-white/80 transition-colors"
          >
            <span className="material-symbols-outlined text-[16px]">fast_rewind</span>
          </button>

          {/* Frame backward */}
          <button
            onClick={() => seekTo(Math.max(0, currentTime - (1 / 30) * videoSpeed))}
            title="1 Frame Geri"
            className="w-7 h-7 flex items-center justify-center rounded-full hover:bg-white/10 text-white/40 hover:text-white/80 transition-colors"
          >
            <span className="material-symbols-outlined text-[16px]">skip_previous</span>
          </button>

          {/* Play/Pause */}
          <button
            onClick={() => {
              videoLog('Timeline', 'Timeline oynat butonu', { wasPlaying: isPlaying });
              setIsPlaying(!isPlaying);
            }}
            className="w-9 h-9 flex items-center justify-center rounded-full bg-white/10 hover:bg-white/20 text-white border border-white/15 transition-all hover:scale-105 active:scale-95"
          >
            <span className="material-symbols-outlined text-[20px] fill">
              {isPlaying ? 'pause' : 'play_arrow'}
            </span>
          </button>

          {/* Frame forward */}
          <button
            onClick={() => seekTo(Math.min(sourceSafeDuration, currentTime + (1 / 30) * videoSpeed))}
            title="1 Frame İleri"
            className="w-7 h-7 flex items-center justify-center rounded-full hover:bg-white/10 text-white/40 hover:text-white/80 transition-colors"
          >
            <span className="material-symbols-outlined text-[16px]">skip_next</span>
          </button>

          {/* Skip forward 5s */}
          <button
            onClick={() => seekTo(Math.min(sourceSafeDuration, currentTime + 5 * videoSpeed))}
            title="5 Saniye İleri"
            className="w-7 h-7 flex items-center justify-center rounded-full hover:bg-white/10 text-white/40 hover:text-white/80 transition-colors"
          >
            <span className="material-symbols-outlined text-[16px]">fast_forward</span>
          </button>

          <div className="font-mono text-[12px] tracking-wider flex items-center gap-1 bg-black/30 px-3 py-1 rounded-md border border-white/8 ml-2">
            <span className="text-white/90 font-semibold">{formatTimecode(currentTime / videoSpeed)}</span>
            <span className="text-white/25 mx-0.5">/</span>
            <span className="text-white/40">{formatTimecode(safeDuration)}</span>
          </div>
        </div>

        {/* Right section: video speed + zoom */}
        <div className="flex items-center gap-2">
          {/* Video Speed */}
          <div className="flex items-center gap-1.5 bg-black/20 border border-white/8 rounded-md px-2 py-1">
            <span className="material-symbols-outlined text-[13px] text-white/40">speed</span>
            <input
              type="range"
              min={25}
              max={400}
              step={5}
              value={(videoSpeed) * 100}
              onChange={(e) => updateSettings({ videoSpeed: Number(e.target.value) / 100 })}
              className="w-20 accent-indigo-500 cursor-pointer"
              title={`Video Hızı: ${videoSpeed}x`}
            />
            <span className="text-[11px] font-mono text-indigo-400 w-7 text-right">{videoSpeed.toFixed(2)}x</span>
          </div>

          <div className="w-px h-5 bg-white/10" />

          {/* Timeline zoom */}
          <div className="flex items-center gap-1">
            <button
              onClick={() => setTimelineZoom(Math.max(10, timelineZoom - 10))}
              className="w-6 h-6 flex items-center justify-center rounded hover:bg-white/10 text-white/40 hover:text-white/70 transition-colors"
            >
              <span className="material-symbols-outlined text-[16px]">remove</span>
            </button>
            <input
              type="range"
              min={10}
              max={200}
              value={timelineZoom}
              onChange={(e) => setTimelineZoom(Number(e.target.value))}
              className="w-20 accent-indigo-500 cursor-pointer"
              title="Timeline Zoom"
            />
            <button
              onClick={() => setTimelineZoom(Math.min(200, timelineZoom + 10))}
              className="w-6 h-6 flex items-center justify-center rounded hover:bg-white/10 text-white/40 hover:text-white/70 transition-colors"
            >
              <span className="material-symbols-outlined text-[16px]">add</span>
            </button>
          </div>
        </div>
      </div>

      {/* ─── Track area ───────────────────────────────────────── */}
      <div className="flex-1 flex overflow-hidden relative">
        {/* Left Sidebar - Track Labels with Mute */}
        <div className="w-[140px] border-r border-white/[0.05] flex flex-col shrink-0 z-30" style={{ background: 'rgba(30,30,40,0.9)' }}>
          <div className="h-6 border-b border-white/5 bg-black/20" />
          {/* Video track label */}
          <div className="h-14 border-b border-white/5 flex items-center px-3 gap-2">
            <span className="material-symbols-outlined text-[15px] text-indigo-400">videocam</span>
            <span className="font-medium text-[11px] text-white/80 flex-1">Ekran Kaydı</span>
          </div>
          {/* Mic track label */}
          {currentProject?.microphonePath && (
            <div className="h-14 border-b border-white/5 flex items-center px-3 gap-2">
              <span className="material-symbols-outlined text-[15px] text-green-400">mic</span>
              <span className="font-medium text-[11px] text-white/80 flex-1">Mikrofon</span>
              <button
                onClick={() => toggleMute('mic')}
                title={micTrack.muted ? 'Sesi Aç' : 'Sesi Kapat'}
                className={`w-6 h-6 flex items-center justify-center rounded-md transition-colors ${
                  micTrack.muted
                    ? 'bg-red-500/20 text-red-400'
                    : 'hover:bg-white/10 text-white/40 hover:text-white/80'
                }`}
              >
                <span className="material-symbols-outlined text-[14px]">
                  {micTrack.muted ? 'volume_off' : 'volume_up'}
                </span>
              </button>
            </div>
          )}
          {/* System audio track label */}
          {currentProject?.systemAudioPath && (
            <div className="h-14 border-b border-white/5 flex items-center px-3 gap-2">
              <span className="material-symbols-outlined text-[15px] text-blue-400">speaker</span>
              <span className="font-medium text-[11px] text-white/80 flex-1">Sistem Sesi</span>
              <button
                onClick={() => toggleMute('sys')}
                title={sysTrack.muted ? 'Sesi Aç' : 'Sesi Kapat'}
                className={`w-6 h-6 flex items-center justify-center rounded-md transition-colors ${
                  sysTrack.muted
                    ? 'bg-red-500/20 text-red-400'
                    : 'hover:bg-white/10 text-white/40 hover:text-white/80'
                }`}
              >
                <span className="material-symbols-outlined text-[14px]">
                  {sysTrack.muted ? 'volume_off' : 'volume_up'}
                </span>
              </button>
            </div>
          )}
          {/* Zoom effects label */}
          <div className="h-12 flex items-center px-3 gap-2">
            <span className="material-symbols-outlined text-[15px] text-purple-400">zoom_in_map</span>
            <span className="font-medium text-[11px] text-white/80">Zoom Efektleri</span>
          </div>
        </div>

        {/* Scrollable Timeline */}
        <div
          ref={containerRef}
          className="flex-1 overflow-x-hidden relative"
          style={{ background: '#0f0f16' }}
          onMouseDown={handleTimelineMouseDown}
        >
          <div
            className="relative"
            style={{ width: totalWidth, minHeight: '100%' }}
            onClick={(e) => {
              if (dragRef.current || panRef.current.isPanning) return;
              const rect = e.currentTarget.getBoundingClientRect();
              const x = e.clientX - rect.left;
              seekTo(Math.max(0, (x / pps) * videoSpeed));
            }}
          >
            {/* Playhead */}
            <div
              className="absolute top-0 bottom-0 z-40 pointer-events-none"
              style={{ left: playheadX }}
            >
              <div className="absolute -top-0 left-1/2 -translate-x-1/2 w-2.5 h-2.5 bg-indigo-400 rotate-45 shadow-[0_0_8px_rgba(129,140,248,0.8)]" />
              <div className="absolute top-2.5 bottom-0 left-1/2 -translate-x-1/2 w-px bg-indigo-400/70" />
            </div>

            {/* Ruler */}
            <div className="h-6 border-b border-white/5 flex items-end sticky top-0 z-20 text-[9px] text-white/30 select-none pointer-events-none"
              style={{ background: 'rgba(20,20,28,0.95)', backdropFilter: 'blur(4px)' }}>
              {rulerMarks.map((t) => (
                <div
                  key={t}
                  className="border-l border-white/8 pl-1 pb-0.5 h-3 shrink-0"
                  style={{ width: 5 * pps }}
                >
                  {formatTimecode(t)}
                </div>
              ))}
            </div>

            {/* Tracks */}
            <div className="flex flex-col">
              {/* Video track — no waveform */}
              {renderClipTrack(
                videoTrack, 'video',
                'bg-indigo-500/25 hover:bg-indigo-500/35',
                'bg-indigo-500/45',
                'bg-indigo-300',
                false
              )}
              {/* Microphone track — with waveform */}
              {currentProject?.microphonePath && renderClipTrack(
                micTrack, 'mic',
                'bg-emerald-500/25 hover:bg-emerald-500/35',
                'bg-emerald-500/45',
                'bg-emerald-300',
                true
              )}
              {/* System audio track — with waveform */}
              {currentProject?.systemAudioPath && renderClipTrack(
                sysTrack, 'sys',
                'bg-blue-500/25 hover:bg-blue-500/35',
                'bg-blue-500/45',
                'bg-blue-300',
                true
              )}

              {/* Zoom Effects Track */}
              <div className="h-12 relative flex items-center mt-0.5">
                {zoomEffects.map((effect, i) => (
                  <div
                    key={effect.id}
                    onMouseDown={(e) => handleMouseDownZoom(e, effect.id, 'move')}
                    className={`track-item absolute h-[24px] top-1/2 -translate-y-1/2 rounded-full flex items-center justify-center cursor-pointer ${
                      selectedZoomId === effect.id
                        ? 'bg-purple-500/50 border-2 border-purple-400 shadow-[0_0_12px_rgba(168,85,247,0.5)] z-10'
                        : 'bg-purple-500/20 border border-purple-500/40 hover:border-purple-400 z-0 transition-all duration-300 ease-out'
                    }`}
                    style={{
                      left: (effect.startTime / videoSpeed) * pps,
                      width: (effect.duration / videoSpeed) * pps,
                    }}
                    onClick={(e) => { e.stopPropagation(); setSelectedZoomId(effect.id); }}
                  >
                    <span className="text-[10px] font-medium text-purple-300 pointer-events-none">Zoom {i + 1}</span>
                    <div
                      className="absolute left-0 w-3 h-full cursor-ew-resize hover:bg-white/20 rounded-l-full"
                      onMouseDown={(e) => handleMouseDownZoom(e, effect.id, 'resize-left')}
                    />
                    <div
                      className="absolute right-0 w-3 h-full cursor-ew-resize hover:bg-white/20 rounded-r-full"
                      onMouseDown={(e) => handleMouseDownZoom(e, effect.id, 'resize-right')}
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
