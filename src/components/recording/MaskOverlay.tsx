import React, { useEffect, useState } from 'react';

export default function MaskOverlay() {
  const [bounds, setBounds] = useState<{ x: number; y: number; width: number; height: number } | null>(null);

  useEffect(() => {
    // Parse bounds from URL parameters
    const searchParams = new URLSearchParams(window.location.hash.split('?')[1]);
    const boundsParam = searchParams.get('bounds');
    if (boundsParam) {
      try {
        setBounds(JSON.parse(boundsParam));
      } catch (e) {
        console.error('Failed to parse bounds:', e);
      }
    }
  }, []);

  if (!bounds) return null;

  return (
    <div className="fixed inset-0 w-screen h-screen overflow-hidden" style={{ pointerEvents: 'none' }}>
      {/* Siyah karartma arka planı */}
      <div 
        className="absolute inset-0 bg-black/60 pointer-events-none"
        style={{
          // Maske ile ortayı şeffaf bırakıyoruz
          clipPath: `polygon(
            0% 0%, 0% 100%, ${bounds.x}px 100%, ${bounds.x}px ${bounds.y}px, 
            ${bounds.x + bounds.width}px ${bounds.y}px, ${bounds.x + bounds.width}px ${bounds.y + bounds.height}px, 
            ${bounds.x}px ${bounds.y + bounds.height}px, ${bounds.x}px 100%, 
            100% 100%, 100% 0%
          )`
        }}
      />
      
      {/* İsteğe bağlı seçili alan kenarlığı */}
      <div 
        className="absolute border border-indigo-500/50 pointer-events-none"
        style={{
          left: bounds.x,
          top: bounds.y,
          width: bounds.width,
          height: bounds.height
        }}
      />
    </div>
  );
}
