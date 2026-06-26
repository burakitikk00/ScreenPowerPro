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
    <div className="fixed inset-0 z-[9999] flex items-center justify-center pointer-events-none bg-black/40 backdrop-blur-sm">
      <div 
        key={countdown} 
        className="text-[150px] font-black text-white tracking-tighter drop-shadow-[0_10px_30px_rgba(0,0,0,0.8)] animate-pulse"
      >
        {countdown}
      </div>
    </div>
  );
}
