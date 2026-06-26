import { useEffect, useRef } from 'react';

export default function CameraOverlay() {
  const videoRef = useRef<HTMLVideoElement>(null);

  useEffect(() => {
    async function startCamera() {
      try {
        const stream = await navigator.mediaDevices.getUserMedia({ video: true });
        if (videoRef.current) {
          videoRef.current.srcObject = stream;
        }
      } catch (error) {
        console.error('Kamera başlatılamadı:', error);
      }
    }
    startCamera();

    return () => {
      if (videoRef.current && videoRef.current.srcObject) {
        const stream = videoRef.current.srcObject as MediaStream;
        stream.getTracks().forEach((track) => track.stop());
      }
    };
  }, []);

  return (
    <div
      className="w-full h-full relative rounded-xl overflow-hidden border-2 border-white/20 shadow-2xl bg-black"
      style={{ WebkitAppRegion: 'drag' } as React.CSSProperties}
    >
      <video
        ref={videoRef}
        autoPlay
        playsInline
        muted
        className="w-full h-full object-cover"
        style={{ WebkitAppRegion: 'no-drag' } as React.CSSProperties}
      />
      {/* Kapat Butonu */}
      <button 
        onClick={() => window.electronAPI.closeCameraOverlay()}
        className="absolute top-2 right-2 w-6 h-6 rounded-full bg-black/50 text-white flex items-center justify-center hover:bg-red-500/80 transition-colors z-10"
        style={{ WebkitAppRegion: 'no-drag' } as React.CSSProperties}
      >
        <span className="material-symbols-outlined text-[14px]">close</span>
      </button>
    </div>
  );
}
