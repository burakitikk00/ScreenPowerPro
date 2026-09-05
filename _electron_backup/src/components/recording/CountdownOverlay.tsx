import React, { useEffect, useState } from 'react';

export default function CountdownOverlay() {
  const [countdown, setCountdown] = useState<number | null>(null);

  useEffect(() => {
    const searchParams = new URLSearchParams(window.location.hash.split('?')[1]);
    const secParam = searchParams.get('seconds');
    if (secParam) {
      setCountdown(parseInt(secParam, 10));
    }
  }, []);

  useEffect(() => {
    if (countdown === null) return;
    if (countdown <= 0) {
      window.electronAPI.closeCountdown();
      return;
    }
    const timer = setTimeout(() => {
      setCountdown(c => (c ? c - 1 : 0));
    }, 1000);
    return () => clearTimeout(timer);
  }, [countdown]);

  if (countdown === null || countdown === 0) return null;

  return (
    <div className="fixed inset-0 z-[9999] flex items-center justify-center pointer-events-none bg-transparent">
      <div 
        key={countdown} 
        className="flex items-center justify-center w-32 h-32 rounded-full bg-black/40 backdrop-blur-lg border border-white/20 text-6xl font-bold text-white shadow-[0_0_40px_rgba(0,0,0,0.5)] animate-in zoom-in duration-300 fade-in"
      >
        {countdown}
      </div>
    </div>
  );
}
