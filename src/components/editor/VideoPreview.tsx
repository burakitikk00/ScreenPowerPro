import { useRef, useEffect, useCallback, useState } from 'react';
import { useEditorStore } from '../../stores/editorStore';
import { useAppStore } from '../../stores/appStore';
import { getActiveZoomAtTime, resolveVideoDuration } from '../../lib/zoomEngine';
import type { MouseMoveEvent, RecordingMetadata } from '../../types';
import {
  videoLog,
  snapshotVideo,
  describeMediaError,
} from '../../lib/videoDebug';

export default function VideoPreview() {
  const videoContainerRef = useRef<HTMLDivElement>(null);
  const videoRef = useRef<HTMLVideoElement>(null);
  const cursorRef = useRef<HTMLDivElement>(null);
  const mouseMovesRef = useRef<MouseMoveEvent[]>([]);
  const metadataRef = useRef<RecordingMetadata | null>(null);
  const micAudioRef = useRef<HTMLAudioElement>(null);
  const sysAudioRef = useRef<HTMLAudioElement>(null);

  const { currentProjectPath, currentProject } = useAppStore();
  const {
    currentTime,
    isPlaying,
    zoomEffects,
    settings,
    seekVersion,
    videoTrack,
    micTrack,
    sysTrack,
    setCurrentTime,
    setDuration,
    setIsPlaying,
    initClipsFromDuration,
  } = useEditorStore();
  const [loadError, setLoadError] = useState<string | null>(null);
  const [fileInfo, setFileInfo] = useState<any>(null);
  const [videoState, setVideoState] = useState<Record<string, unknown> | null>(null);

  const refreshVideoState = useCallback(() => {
    const video = videoRef.current;
    if (!video) return;
    setVideoState(snapshotVideo(video));
  }, []);

  // Sync media to timeline time based on clips
  const syncMediaToTimeline = useCallback((time: number) => {
    const state = useEditorStore.getState();

    const syncElement = (
      el: HTMLMediaElement | null,
      track: { clips: { trackOffset: number; sourceStart: number; sourceEnd: number }[] }
    ) => {
      if (!el) return;
      const activeClip = track.clips.find(
        (c) => time >= c.trackOffset && time < c.trackOffset + (c.sourceEnd - c.sourceStart)
      );

      if (activeClip) {
        const targetMediaTime = activeClip.sourceStart + (time - activeClip.trackOffset);
        if (Math.abs(el.currentTime - targetMediaTime) > 0.3) {
          el.currentTime = targetMediaTime;
        }
      }
    };

    syncElement(videoRef.current, state.videoTrack);
    syncElement(micAudioRef.current, state.micTrack);
    syncElement(sysAudioRef.current, state.sysTrack);
  }, []);

  // Load video + audio files when project changes
  useEffect(() => {
    if (!currentProjectPath || !currentProject) return;

    const loadMedia = async () => {
      videoLog('Load', 'Video yükleme başladı', {
        projectPath: currentProjectPath,
        videoPath: currentProject.videoPath,
      });

      setLoadError(null);

      const debugInfo = await window.electronAPI.getMediaDebugInfo(
        currentProjectPath,
        currentProject.videoPath
      );
      setFileInfo(debugInfo);
      videoLog('Load', 'Dosya bilgisi', debugInfo);

      if (!debugInfo.exists) {
        const msg = `Dosya bulunamadı: ${debugInfo.fullPath}`;
        videoLog('Load', 'HATA', msg);
        setLoadError(msg);
        return;
      }

      if (debugInfo.size < 1000) {
        const msg = `Dosya çok küçük (${debugInfo.size} byte) — kayıt boş olabilir`;
        videoLog('Load', 'UYARI', msg);
        setLoadError(msg);
        return;
      }

      const url = debugInfo.url;
      const video = videoRef.current;
      if (!url || !video) {
        const msg = 'Media URL oluşturulamadı';
        videoLog('Load', 'HATA', msg);
        setLoadError(msg);
        return;
      }

      videoLog('Load', 'src atanıyor', { url });
      video.src = url;
      video.load();
      refreshVideoState();

      // Load microphone audio if available
      if (currentProject.microphonePath && micAudioRef.current) {
        const micInfo = await window.electronAPI.getMediaDebugInfo(
          currentProjectPath,
          currentProject.microphonePath
        );
        if (micInfo.exists && micInfo.url) {
          videoLog('Load', 'Mikrofon sesi yükleniyor', { url: micInfo.url });
          micAudioRef.current.src = micInfo.url;
          micAudioRef.current.load();
        }
      }

      // Load system audio if available
      if (currentProject.systemAudioPath && sysAudioRef.current) {
        const sysInfo = await window.electronAPI.getMediaDebugInfo(
          currentProjectPath,
          currentProject.systemAudioPath
        );
        if (sysInfo.exists && sysInfo.url) {
          videoLog('Load', 'Sistem sesi yükleniyor', { url: sysInfo.url });
          sysAudioRef.current.src = sysInfo.url;
          sysAudioRef.current.load();
        }
      }

      // Load mouse input data
      try {
        const { moves } = await window.electronAPI.loadProjectInputData(currentProjectPath);
        mouseMovesRef.current = moves || [];
      } catch (err) {
        videoLog('Load', 'Fare verisi yüklenemedi', { err });
      }

      // Load metadata to get crop bounds
      try {
        metadataRef.current = await window.electronAPI.loadProjectMetadata(currentProjectPath);
      } catch (err) {
        videoLog('Load', 'Metadata yüklenemedi', { err });
      }
    };

    loadMedia();
  }, [currentProjectPath, currentProject, refreshVideoState]);

  // Video event listeners
  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;

    const events: Array<[string, EventListener]> = [
      ['loadstart', () => videoLog('Event', 'loadstart')],
      ['loadedmetadata', () => {
        void (async () => {
          const raw = video.duration;
          const resolved = currentProjectPath
            ? await resolveVideoDuration(video, currentProjectPath)
            : raw;
          videoLog('Event', 'loadedmetadata', {
            rawDuration: raw,
            resolvedDuration: resolved,
          });
          if (resolved > 0) {
            setDuration(resolved);
            initClipsFromDuration(resolved);
          }
          refreshVideoState();
        })();
      }],
      ['loadeddata', () => videoLog('Event', 'loadeddata')],
      ['canplay', () => videoLog('Event', 'canplay')],
      ['canplaythrough', () => videoLog('Event', 'canplaythrough')],
      ['playing', () => videoLog('Event', 'playing')],
      ['waiting', () => videoLog('Event', 'waiting — buffer bekleniyor (donma?)')],
      ['stalled', () => videoLog('Event', 'stalled — veri gelmiyor')],
      ['suspend', () => videoLog('Event', 'suspend')],
      ['ended', () => {
        videoLog('Event', 'ended');
      }],
      ['error', () => {
        const err = describeMediaError(video);
        videoLog('Event', 'error', { error: err, src: video.currentSrc });
        if (video.currentTime < 1 && !useEditorStore.getState().isPlaying) {
          setLoadError(err ?? 'Video decode/network hatası');
        }
        refreshVideoState();
      }],
      ['timeupdate', () => {
        // We no longer sync timeline from video.timeupdate to allow timeline independence
      }],
      ['seeked', () => {
        // We no longer sync from video seeked
      }],
    ];

    for (const [name, handler] of events) {
      video.addEventListener(name, handler);
    }
    return () => {
      for (const [name, handler] of events) {
        video.removeEventListener(name, handler);
      }
    };
  }, [currentProjectPath, setCurrentTime, setDuration, setIsPlaying, refreshVideoState, syncMediaToTimeline, initClipsFromDuration]);

  // Update duration if audio tracks load and are longer than video
  const updateMaxDuration = useCallback(() => {
    const vDur = videoRef.current?.duration || 0;
    const mDur = micAudioRef.current?.duration || 0;
    const sDur = sysAudioRef.current?.duration || 0;
    
    const currentDur = useEditorStore.getState().duration;
    const newMax = Math.max(currentDur, mDur !== Infinity ? mDur : 0, sDur !== Infinity ? sDur : 0);
    
    if (newMax > currentDur) {
       setDuration(newMax);
       initClipsFromDuration(newMax);
    }
  }, [setDuration, initClipsFromDuration]);

  useEffect(() => {
    const mic = micAudioRef.current;
    const sys = sysAudioRef.current;
    if (mic) mic.addEventListener('loadedmetadata', updateMaxDuration);
    if (sys) sys.addEventListener('loadedmetadata', updateMaxDuration);
    return () => {
      if (mic) mic.removeEventListener('loadedmetadata', updateMaxDuration);
      if (sys) sys.removeEventListener('loadedmetadata', updateMaxDuration);
    };
  }, [updateMaxDuration]);

  // Seek sync — when seekVersion changes (user clicked timeline)
  useEffect(() => {
    if (seekVersion === 0) return;
    const targetTime = useEditorStore.getState().currentTime;
    videoLog('Seek', 'Timeline seek', { targetTime, seekVersion });
    syncMediaToTimeline(targetTime);
  }, [seekVersion, syncMediaToTimeline]);

  // Play/pause control
  useEffect(() => {
    const video = videoRef.current;
    const micAudio = micAudioRef.current;
    const sysAudio = sysAudioRef.current;

    if (isPlaying) {
      videoLog('Play', 'isPlaying=true → playing media elements');
      syncMediaToTimeline(useEditorStore.getState().currentTime);
      video?.play().catch(() => {});
      if (!micTrack.muted) micAudio?.play().catch(() => {});
      if (!sysTrack.muted) sysAudio?.play().catch(() => {});
    } else {
      videoLog('Play', 'isPlaying=false → pausing media elements');
      video?.pause();
      micAudio?.pause();
      sysAudio?.pause();
      refreshVideoState();
    }
  }, [isPlaying, syncMediaToTimeline, micTrack.muted, sysTrack.muted, refreshVideoState]);

  // Apply mic/sys mute & volume
  useEffect(() => {
    if (micAudioRef.current) {
      micAudioRef.current.volume = micTrack.muted ? 0 : Math.min(1, Math.max(0, (settings.micVolume ?? 100) / 100));
      if (micTrack.muted && !micAudioRef.current.paused) {
        micAudioRef.current.pause();
      }
    }
  }, [settings.micVolume, micTrack.muted]);

  useEffect(() => {
    if (sysAudioRef.current) {
      sysAudioRef.current.volume = sysTrack.muted ? 0 : Math.min(1, Math.max(0, (settings.sysVolume ?? 100) / 100));
      if (sysTrack.muted && !sysAudioRef.current.paused) {
        sysAudioRef.current.pause();
      }
    }
  }, [settings.sysVolume, sysTrack.muted]);

  // Apply video speed (playbackRate)
  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    const rate = settings.videoSpeed ?? 1;
    video.playbackRate = rate;
    // Also sync audio playback rate
    if (micAudioRef.current) micAudioRef.current.playbackRate = rate;
    if (sysAudioRef.current) sysAudioRef.current.playbackRate = rate;
  }, [settings.videoSpeed]);

  const togglePlay = useCallback(() => {
    const video = videoRef.current;
    videoLog('UI', 'Oynat butonuna tıklandı', {
      wasPlaying: isPlaying,
      willBePlaying: !isPlaying,
      video: video ? snapshotVideo(video) : null,
    });
    setIsPlaying(!isPlaying);
  }, [isPlaying, setIsPlaying]);

  // Zoom render loop and Master Clock
  useEffect(() => {
    let animationFrameId: number;
    let lastTime = performance.now();

    const renderLoop = (now: number) => {
      const dt = Math.min((now - lastTime) / 1000, 0.1);
      lastTime = now;

      const video = videoRef.current;
      const state = useEditorStore.getState();
      let currentT = state.currentTime;

      // 1. Advance timeline manually when playing
      if (state.isPlaying) {
        currentT += dt * (state.settings.videoSpeed ?? 1);
        if (currentT >= state.duration && state.duration > 0) {
          currentT = state.duration;
          state.setIsPlaying(false);
          state.seekTo(0);
        } else {
          state.setCurrentTime(currentT);
        }
      }

      // 2. Sync and control media elements based on active clips
      const syncTrack = (
        el: HTMLMediaElement | null,
        track: typeof state.videoTrack,
        isMuted: boolean,
        volume: number
      ) => {
        if (!el) return false;
        const activeClip = track.clips.find(
          (c) => currentT >= c.trackOffset && currentT < c.trackOffset + (c.sourceEnd - c.sourceStart)
        );

        if (activeClip) {
          const targetMediaTime = activeClip.sourceStart + (currentT - activeClip.trackOffset);
          if (Math.abs(el.currentTime - targetMediaTime) > 0.3) {
            el.currentTime = targetMediaTime;
          }
          if (state.isPlaying && el.paused && !el.ended && !isMuted) {
            el.play().catch(() => {});
          }
          el.muted = isMuted;
          el.volume = volume;
          return true;
        } else {
          if (!el.paused) {
            el.pause();
          }
          return false;
        }
      };

      const isVideoVisible = syncTrack(video, state.videoTrack, false, 1);
      syncTrack(micAudioRef.current, state.micTrack, state.micTrack.muted, (state.settings.micVolume ?? 100) / 100);
      syncTrack(sysAudioRef.current, state.sysTrack, state.sysTrack.muted, (state.settings.sysVolume ?? 100) / 100);

      // 3. Render video transformations
      if (video) {
        if (!isVideoVisible || video.ended || video.error) {
          video.style.opacity = '0';
        } else {
          video.style.opacity = '1';

          const videoWidth = video.videoWidth || 1920;
          const videoHeight = video.videoHeight || 1080;
          
          const container = videoContainerRef.current;
          let transformStyle = 'scale(1)';
          let transformOrigin = 'center';

          if (container && container.parentElement) {
            const videoRatio = videoWidth / videoHeight;
            const parentWidth = container.parentElement.clientWidth;
            const parentHeight = container.parentElement.clientHeight;
            const parentRatio = parentWidth / parentHeight;
            
            let actualWidth, actualHeight;
            if (videoRatio > parentRatio) {
              actualWidth = parentWidth;
              actualHeight = actualWidth / videoRatio;
            } else {
              actualHeight = parentHeight;
              actualWidth = actualHeight * videoRatio;
            }
            
            container.style.width = `${actualWidth}px`;
            container.style.height = `${actualHeight}px`;
          }
          
          const zoom = getActiveZoomAtTime(state.zoomEffects, currentT);
          if (zoom) {
            const vw = videoWidth / zoom.scale;
            const vh = videoHeight / zoom.scale;
            
            let cx = zoom.targetX - vw / 2;
            let cy = zoom.targetY - vh / 2;
            
            if (cx < 0) cx = 0;
            if (cx + vw > videoWidth) cx = videoWidth - vw;
            if (cy < 0) cy = 0;
            if (cy + vh > videoHeight) cy = videoHeight - vh;

            const percentX = (cx / videoWidth) * 100;
            const percentY = (cy / videoHeight) * 100;

            transformOrigin = '0 0';
            transformStyle = `scale(${zoom.scale}) translate(-${percentX}%, -${percentY}%)`;
          }

          if (container) {
            container.style.transformOrigin = transformOrigin;
            container.style.transform = transformStyle;
          }

          // Cursor processing
          const cursor = cursorRef.current;
          if (cursor) {
              if (state.settings.cursorVisible !== false) {
                const moves = mouseMovesRef.current;
                const MOUSE_SYNC_OFFSET = 150; // ms offset to fix recording delay
                const currentMs = currentT * 1000 + MOUSE_SYNC_OFFSET;
                let x = -1000, y = -1000;
              
              if (moves.length > 0) {
                let idx = 0;
                while (idx < moves.length - 1 && moves[idx + 1].timestamp <= currentMs) {
                  idx++;
                }
                
                if (idx < moves.length - 1 && currentMs >= moves[idx].timestamp) {
                  const m1 = moves[idx];
                  const m2 = moves[idx + 1];
                  const t = (currentMs - m1.timestamp) / (m2.timestamp - m1.timestamp);
                  x = m1.x + (m2.x - m1.x) * t;
                  y = m1.y + (m2.y - m1.y) * t;
                } else if (idx === moves.length - 1) {
                  x = moves[idx].x;
                  y = moves[idx].y;
                } else if (moves.length > 0) {
                  x = moves[0].x;
                  y = moves[0].y;
                }
              }

              if (x !== -1000) {
                let cropOffsetX = 0;
                let cropOffsetY = 0;
                
                if (metadataRef.current?.customCropBounds) {
                  cropOffsetX = metadataRef.current.customCropBounds.x || 0;
                  cropOffsetY = metadataRef.current.customCropBounds.y || 0;
                }

                const px = ((x - cropOffsetX) / videoWidth) * 100;
                const py = ((y - cropOffsetY) / videoHeight) * 100;
                
                cursor.style.left = `${px}%`;
                cursor.style.top = `${py}%`;
                cursor.style.opacity = '1';
                
                const scaleVal = state.settings.cursorSize ? state.settings.cursorSize / 100 : 1;
                cursor.style.transform = `translate(calc(-1 * var(--tip-x, 50%)), calc(-1 * var(--tip-y, 50%))) scale(${scaleVal})`;
                
                const styleClass = `cursor-${state.settings.cursorStyle || 'default'}`;
                if (cursor.className.indexOf(styleClass) === -1) {
                  cursor.className = `absolute pointer-events-none z-20 ${styleClass}`;
                }
              } else {
                cursor.style.opacity = '0';
              }
            } else {
              cursor.style.opacity = '0';
            }
          }
        }
      }
      
      animationFrameId = requestAnimationFrame(renderLoop);
    };

    animationFrameId = requestAnimationFrame(renderLoop);

    return () => {
      cancelAnimationFrame(animationFrameId);
    };
  }, [zoomEffects]);

  return (
    <div
      className="flex-1 flex items-center justify-center p-8 relative overflow-hidden"
      style={{ backgroundColor: settings.canvasBackground }}
    >
      {/* Hidden audio elements for mic and system audio playback */}
      <audio ref={micAudioRef} preload="auto" style={{ display: 'none' }} />
      <audio ref={sysAudioRef} preload="auto" style={{ display: 'none' }} />

      <div
        className="absolute inset-0 opacity-20 pointer-events-none"
        style={{
          backgroundImage:
            'radial-gradient(circle at 50% 50%, rgba(192, 193, 255, 0.1) 0%, transparent 50%)',
        }}
      />
      <div className="relative w-full max-w-4xl aspect-video bg-black rounded-xl overflow-hidden shadow-[0_20px_50px_rgba(0,0,0,0.5)] border border-white/10 ring-1 ring-white/5 group flex items-center justify-center">
        <div ref={videoContainerRef} className="relative transform-gpu overflow-hidden">
          <video
            ref={videoRef}
            className="w-full h-full object-fill"
            preload="auto"
            playsInline
          />
          <div ref={cursorRef} className="absolute pointer-events-none z-20 opacity-0 transition-opacity duration-150"></div>
        </div>

        {loadError && (
          <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 bg-black/80 text-on-surface-variant p-4 text-center">
            <span className="material-symbols-outlined text-4xl text-error">error</span>
            <p className="font-body-md text-error">{loadError}</p>
            <p className="font-label-sm text-label-sm text-on-surface-variant">
              Alttaki debug panelini kontrol edin
            </p>
          </div>
        )}

        <div
          className="absolute inset-0 bg-transparent flex items-center justify-center cursor-pointer z-10"
          onClick={togglePlay}
        >
        </div>

      </div>
    </div>
  );
}
