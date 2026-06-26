import { useState, useEffect } from 'react';
import { useAppStore } from '../../stores/appStore';

export default function RecordingBar() {
  const isRecording = useAppStore((s) => s.isRecording);
  const [elapsed, setElapsed] = useState(0);

  useEffect(() => {
    if (!isRecording) return;
    const start = Date.now();
    const interval = setInterval(() => {
      setElapsed(Math.floor((Date.now() - start) / 1000));
    }, 1000);
    return () => clearInterval(interval);
  }, [isRecording]);

  const handleStop = async () => {
    const stopFn = (window as unknown as { __stopRecording?: () => void }).__stopRecording;
    if (stopFn) await stopFn();
  };

  if (!isRecording) return null;

  const mins = Math.floor(elapsed / 60).toString().padStart(2, '0');
  const secs = (elapsed % 60).toString().padStart(2, '0');

  return (
    <div className="fixed bottom-8 left-1/2 -translate-x-1/2 z-[200] flex items-center gap-4 bg-white/5 backdrop-blur-xl border border-white/10 rounded-full px-6 py-3 shadow-[0_20px_40px_rgba(0,0,0,0.4)]">
      <div className="flex items-center gap-2">
        <div className="w-3 h-3 rounded-full bg-red-500 animate-pulse" />
        <span className="font-label-md text-label-md text-on-surface">Kayıt</span>
      </div>
      <span className="font-label-md text-label-md text-primary tabular-nums">
        {mins}:{secs}
      </span>
      <button
        onClick={handleStop}
        className="px-4 py-1.5 rounded-full bg-red-600 hover:bg-red-500 text-white font-label-md text-label-md transition-colors"
      >
        Durdur
      </button>
    </div>
  );
}
