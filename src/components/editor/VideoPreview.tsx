import { useRef, useEffect, useCallback, useState } from 'react';
import { useEditorStore } from '../../stores/editorStore';
import { useAppStore } from '../../stores/appStore';
import { getActiveZoomAtTime, resolveVideoDuration } from '../../lib/zoomEngine';
import {
  videoLog,
  snapshotVideo,
  describeMediaError,
} from '../../lib/videoDebug';
import VideoDebugPanel from './VideoDebugPanel';
import type { VideoDebugPanelProps } from './VideoDebugPanel';

export default function VideoPreview() {
  const videoRef = useRef<HTMLVideoElement>(null);
  const { currentProjectPath, currentProject } = useAppStore();
  const {
    currentTime,
    isPlaying,
    zoomEffects,
    settings,
    setCurrentTime,
    setDuration,
    setIsPlaying,
  } = useEditorStore();
  const [loadError, setLoadError] = useState<string | null>(null);
  const [fileInfo, setFileInfo] = useState<VideoDebugPanelProps['fileInfo']>(null);
  const [videoState, setVideoState] = useState<Record<string, unknown> | null>(null);

  const refreshVideoState = useCallback(() => {
    const video = videoRef.current;
    if (!video) return;
    setVideoState(snapshotVideo(video));
  }, []);

  useEffect(() => {
    if (!currentProjectPath || !currentProject) return;

    const loadVideo = async () => {
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
    };

    loadVideo();
  }, [currentProjectPath, currentProject, refreshVideoState]);

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
          if (resolved > 0) setDuration(resolved);
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
        setIsPlaying(false);
      }],
      ['error', () => {
        const err = describeMediaError(video);
        videoLog('Event', 'error', { error: err, src: video.currentSrc });
        setLoadError(err ?? 'Video decode/network hatası');
        refreshVideoState();
      }],
      ['timeupdate', () => setCurrentTime(video.currentTime)],
    ];

    for (const [name, handler] of events) {
      video.addEventListener(name, handler);
    }
    return () => {
      for (const [name, handler] of events) {
        video.removeEventListener(name, handler);
      }
    };
  }, [currentProjectPath, setCurrentTime, setDuration, setIsPlaying, refreshVideoState]);

  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;

    if (isPlaying) {
      videoLog('Play', 'isPlaying=true → video.play() çağrılıyor', snapshotVideo(video));
      const playPromise = video.play();
      if (playPromise) {
        playPromise
          .then(() => {
            videoLog('Play', 'play() başarılı');
            refreshVideoState();
          })
          .catch((err: Error) => {
            videoLog('Play', 'play() REDDEDİLDİ', { message: err.message });
            setIsPlaying(false);
            setLoadError(`Oynatma reddedildi: ${err.message}`);
            refreshVideoState();
          });
      }
    } else {
      videoLog('Play', 'isPlaying=false → video.pause()');
      video.pause();
      refreshVideoState();
    }
  }, [isPlaying, setIsPlaying, refreshVideoState]);

  const togglePlay = useCallback(() => {
    const video = videoRef.current;
    videoLog('UI', 'Oynat butonuna tıklandı', {
      wasPlaying: isPlaying,
      willBePlaying: !isPlaying,
      video: video ? snapshotVideo(video) : null,
    });
    setIsPlaying(!isPlaying);
  }, [isPlaying, setIsPlaying]);

  useEffect(() => {
    let animationFrameId: number;

    const renderLoop = () => {
      const video = videoRef.current;
      if (video) {
        const timeSec = video.currentTime;
        const videoWidth = video.videoWidth || 1920;
        const videoHeight = video.videoHeight || 1080;
        
        const zoom = getActiveZoomAtTime(zoomEffects, timeSec);
        const transform = zoom ? `scale(${zoom.scale})` : 'scale(1)';
        const transformOrigin = zoom 
          ? `${(zoom.targetX / videoWidth) * 100}% ${(zoom.targetY / videoHeight) * 100}%` 
          : 'center';

        video.style.transform = transform;
        video.style.transformOrigin = transformOrigin;
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
      <div
        className="absolute inset-0 opacity-20 pointer-events-none"
        style={{
          backgroundImage:
            'radial-gradient(circle at 50% 50%, rgba(192, 193, 255, 0.1) 0%, transparent 50%)',
        }}
      />
      <div className="relative w-full max-w-4xl aspect-video bg-black rounded-xl overflow-hidden shadow-[0_20px_50px_rgba(0,0,0,0.5)] border border-white/10 ring-1 ring-white/5 group">
        <video
          ref={videoRef}
          className="w-full h-full object-contain"
          preload="auto"
          playsInline
        />

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
          className="absolute inset-0 bg-black/40 opacity-0 group-hover:opacity-100 transition-opacity duration-300 flex items-center justify-center backdrop-blur-sm cursor-pointer z-10"
          onClick={togglePlay}
        >
          <button className="w-16 h-16 bg-primary/20 backdrop-blur-md border border-primary/50 rounded-full flex items-center justify-center text-primary shadow-[0_0_30px_rgba(192,193,255,0.3)] hover:scale-110 transition-transform">
            <span className="material-symbols-outlined text-4xl ml-1 fill">
              {isPlaying ? 'pause' : 'play_arrow'}
            </span>
          </button>
        </div>

        <div className="absolute top-4 right-4 bg-black/60 backdrop-blur-md px-2 py-1 rounded border border-white/10 font-label-sm text-label-sm text-on-surface-variant flex items-center gap-1 z-10">
          <span className={`w-2 h-2 rounded-full ${loadError ? 'bg-red-500' : isPlaying ? 'bg-yellow-500' : 'bg-green-500'}`} />
          {isPlaying ? 'Oynatılıyor' : loadError ? 'Hata' : 'Hazır'}
        </div>

        <VideoDebugPanel videoState={videoState} fileInfo={fileInfo} />
      </div>
    </div>
  );
}
