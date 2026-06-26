import { useRef, useEffect, useCallback } from 'react';
import { useEditorStore } from '../../stores/editorStore';
import { useAppStore } from '../../stores/appStore';
import { getActiveZoomAtTime } from '../../lib/zoomEngine';

export default function VideoPreview() {
  const videoRef = useRef<HTMLVideoElement>(null);
  const { currentProjectPath, currentProject } = useAppStore();
  const {
    currentTime,
    duration,
    isPlaying,
    zoomEffects,
    settings,
    setCurrentTime,
    setDuration,
    setIsPlaying,
  } = useEditorStore();

  useEffect(() => {
    if (!videoRef.current || !currentProjectPath || !currentProject) return;
    const videoPath = `${currentProjectPath}/${currentProject.videoPath.replace('./', '')}`.replace(/\\/g, '/');
    videoRef.current.src = `file:///${videoPath}`;
    videoRef.current.load();
  }, [currentProjectPath, currentProject]);

  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;

    const onTimeUpdate = () => setCurrentTime(video.currentTime);
    const onLoaded = () => setDuration(video.duration || 0);
    const onEnded = () => setIsPlaying(false);

    video.addEventListener('timeupdate', onTimeUpdate);
    video.addEventListener('loadedmetadata', onLoaded);
    video.addEventListener('ended', onEnded);
    return () => {
      video.removeEventListener('timeupdate', onTimeUpdate);
      video.removeEventListener('loadedmetadata', onLoaded);
      video.removeEventListener('ended', onEnded);
    };
  }, [setCurrentTime, setDuration, setIsPlaying]);

  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    if (isPlaying) video.play().catch(() => setIsPlaying(false));
    else video.pause();
  }, [isPlaying, setIsPlaying]);

  const togglePlay = useCallback(() => setIsPlaying(!isPlaying), [isPlaying, setIsPlaying]);

  const zoom = getActiveZoomAtTime(zoomEffects, currentTime);
  const transform = zoom
    ? `scale(${zoom.scale}) translate(${zoom.translateX / zoom.scale}px, ${zoom.translateY / zoom.scale}px)`
    : 'scale(1)';

  return (
    <div
      className="flex-1 flex items-center justify-center p-8 relative overflow-hidden"
      style={{ backgroundColor: settings.canvasBackground }}
    >
      <div
        className="absolute inset-0 opacity-20 pointer-events-none"
        style={{
          backgroundImage: 'radial-gradient(circle at 50% 50%, rgba(192, 193, 255, 0.1) 0%, transparent 50%)',
        }}
      />
      <div className="relative w-full max-w-4xl aspect-video bg-black rounded-xl overflow-hidden shadow-[0_20px_50px_rgba(0,0,0,0.5)] border border-white/10 ring-1 ring-white/5 group">
        <div className="w-full h-full overflow-hidden">
          <video
            ref={videoRef}
            className="w-full h-full object-cover transition-transform duration-150 ease-out origin-center"
            style={{ transform }}
          />
        </div>
        <div
          className="absolute inset-0 bg-black/40 opacity-0 group-hover:opacity-100 transition-opacity duration-300 flex items-center justify-center backdrop-blur-sm cursor-pointer"
          onClick={togglePlay}
        >
          <button className="w-16 h-16 bg-primary/20 backdrop-blur-md border border-primary/50 rounded-full flex items-center justify-center text-primary shadow-[0_0_30px_rgba(192,193,255,0.3)] hover:scale-110 transition-transform">
            <span className="material-symbols-outlined text-4xl ml-1 fill">
              {isPlaying ? 'pause' : 'play_arrow'}
            </span>
          </button>
        </div>
        <div className="absolute top-4 right-4 bg-black/60 backdrop-blur-md px-2 py-1 rounded border border-white/10 font-label-sm text-label-sm text-on-surface-variant flex items-center gap-1">
          <span className="w-2 h-2 rounded-full bg-green-500" />
          1080p • 30fps
        </div>
      </div>
    </div>
  );
}
