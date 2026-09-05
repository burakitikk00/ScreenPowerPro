import { useEffect, useRef, useState } from 'react';
import { useAppStore } from '../../stores/appStore';

export default function CameraOverlay() {
  const videoRef = useRef<HTMLVideoElement>(null);
  const settings = useAppStore(s => s.settings);
  type CameraShape = 'rounded' | 'circle' | 'square';
  const [shape, setShape] = useState<CameraShape>('rounded');

  useEffect(() => {
    async function startCamera() {
      try {
        const stream = await navigator.mediaDevices.getUserMedia({ 
          video: settings.selectedCameraId ? { deviceId: { exact: settings.selectedCameraId } } : true 
        });
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
  }, [settings.selectedCameraId]);

  const roundedClass = shape === 'circle' ? 'rounded-full' : shape === 'rounded' ? 'rounded-3xl' : 'rounded-none';

  return (
    <div
      className={`w-full h-full relative overflow-hidden transition-all duration-300 group ${roundedClass}`}
      style={{ WebkitAppRegion: 'drag', transform: 'translateZ(0)' } as React.CSSProperties}
    >
      <video
        ref={videoRef}
        autoPlay
        playsInline
        muted
        className={`w-full h-full object-cover pointer-events-none transition-all duration-300 ${roundedClass}`}
      />

      {/* Şekil Değiştirme Butonu */}
      <button 
        onClick={(e) => {
          e.stopPropagation();
          setShape(prev => prev === 'rounded' ? 'circle' : prev === 'circle' ? 'square' : 'rounded');
        }}
        className={`absolute ${shape === 'circle' ? 'top-9 left-9' : 'top-4 left-4'} w-6 h-6 flex items-center justify-center rounded text-white/40 hover:bg-white/10 hover:text-white transition-all z-30 opacity-0 group-hover:opacity-100`}
        style={{ WebkitAppRegion: 'no-drag' } as React.CSSProperties}
      >
        <span className="material-symbols-outlined text-[14px]">shape_line</span>
      </button>

      {/* Kapat Butonu */}
      <button 
        onClick={(e) => {
          e.stopPropagation();
          window.electronAPI.closeCameraOverlay();
        }}
        className={`absolute ${shape === 'circle' ? 'top-9 right-9' : 'top-4 right-4'} w-6 h-6 flex items-center justify-center rounded text-white/40 hover:bg-white/10 hover:text-red-400 transition-all z-30 opacity-0 group-hover:opacity-100`}
        style={{ WebkitAppRegion: 'no-drag' } as React.CSSProperties}
      >
        <span className="material-symbols-outlined text-[14px]">close</span>
      </button>
    </div>
  );
}
