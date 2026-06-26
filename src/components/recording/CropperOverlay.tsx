import React, { useState, useEffect } from 'react';

const PRESETS = [
  { label: '1920 x 1080 (1080p)', width: 1920, height: 1080 },
  { label: '1280 x 720 (720p)', width: 1280, height: 720 },
  { label: '854 x 480 (480p)', width: 854, height: 480 },
];

export default function CropperOverlay() {
  const [isDrawing, setIsDrawing] = useState(false);
  const [startPos, setStartPos] = useState({ x: 0, y: 0 });
  const [currentPos, setCurrentPos] = useState({ x: 0, y: 0 });
  
  const [bounds, setBounds] = useState<{ x: number, y: number, width: number, height: number } | null>(null);
  
  const [isDragging, setIsDragging] = useState(false);
  const [dragOffset, setDragOffset] = useState({ x: 0, y: 0 });

  const handleMouseDown = (e: React.MouseEvent) => {
    if (bounds) {
      // Check if clicking inside bounds to drag
      if (
        e.clientX >= bounds.x &&
        e.clientX <= bounds.x + bounds.width &&
        e.clientY >= bounds.y &&
        e.clientY <= bounds.y + bounds.height
      ) {
        setIsDragging(true);
        setDragOffset({
          x: e.clientX - bounds.x,
          y: e.clientY - bounds.y,
        });
        return;
      }
    }

    // Start drawing new bounds
    setIsDrawing(true);
    setStartPos({ x: e.clientX, y: e.clientY });
    setCurrentPos({ x: e.clientX, y: e.clientY });
    setBounds(null);
  };

  const handleMouseMove = (e: React.MouseEvent) => {
    if (isDragging && bounds) {
      setBounds({
        ...bounds,
        x: e.clientX - dragOffset.x,
        y: e.clientY - dragOffset.y,
      });
      return;
    }

    if (!isDrawing) return;
    setCurrentPos({ x: e.clientX, y: e.clientY });
  };

  const handleMouseUp = () => {
    if (isDragging) {
      setIsDragging(false);
      return;
    }

    if (!isDrawing) return;
    setIsDrawing(false);

    const x = Math.min(startPos.x, currentPos.x);
    const y = Math.min(startPos.y, currentPos.y);
    const width = Math.abs(currentPos.x - startPos.x);
    const height = Math.abs(currentPos.y - startPos.y);

    if (width > 50 && height > 50) {
      setBounds({ x, y, width, height });
    } else {
      setBounds(null); // Too small
    }
  };

  const confirmSelection = () => {
    if (bounds) {
      window.electronAPI.sendCropperArea(bounds);
    }
  };

  const cancelSelection = () => {
    window.electronAPI.closeCropper();
  };
  
  const setPreset = (w: number, h: number) => {
    const screenW = window.innerWidth;
    const screenH = window.innerHeight;
    const cw = Math.min(w, screenW);
    const ch = Math.min(h, screenH);
    const cx = Math.max(0, (screenW - cw) / 2);
    const cy = Math.max(0, (screenH - ch) / 2);
    setBounds({ x: cx, y: cy, width: cw, height: ch });
  };

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        cancelSelection();
      } else if (e.key === 'Enter' && bounds) {
        confirmSelection();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [bounds]);

  const drawX = Math.min(startPos.x, currentPos.x);
  const drawY = Math.min(startPos.y, currentPos.y);
  const drawW = Math.abs(currentPos.x - startPos.x);
  const drawH = Math.abs(currentPos.y - startPos.y);
  
  const currentBounds = bounds || (isDrawing ? { x: drawX, y: drawY, width: drawW, height: drawH } : null);

  return (
    <div 
      className="fixed inset-0 w-screen h-screen overflow-hidden select-none"
      onMouseDown={handleMouseDown}
      onMouseMove={handleMouseMove}
      onMouseUp={handleMouseUp}
      style={{
        cursor: isDragging ? 'grabbing' : (bounds ? 'crosshair' : 'crosshair'),
      }}
    >
      {/* Siyah karartma arka planı (clipPath ile ortası delik) */}
      <div 
        className="absolute inset-0 bg-black/60 pointer-events-none transition-all duration-75"
        style={{
          clipPath: currentBounds ? `polygon(
            0% 0%, 0% 100%, ${currentBounds.x}px 100%, ${currentBounds.x}px ${currentBounds.y}px, 
            ${currentBounds.x + currentBounds.width}px ${currentBounds.y}px, ${currentBounds.x + currentBounds.width}px ${currentBounds.y + currentBounds.height}px, 
            ${currentBounds.x}px ${currentBounds.y + currentBounds.height}px, ${currentBounds.x}px 100%, 
            100% 100%, 100% 0%
          )` : 'none'
        }}
      />

      {/* The drawing box border and inner area */}
      {currentBounds && (
        <div 
          className="absolute border border-indigo-500 bg-transparent transition-all duration-75"
          style={{
            left: currentBounds.x,
            top: currentBounds.y,
            width: currentBounds.width,
            height: currentBounds.height,
            cursor: bounds && !isDrawing ? (isDragging ? 'grabbing' : 'grab') : 'crosshair',
          }}
        >
          {/* Çizgiler/Kılavuzlar (İsteğe bağlı, şimdilik temiz bırakalım) */}
          {bounds && !isDrawing && (
            <div className="absolute inset-0 pointer-events-none opacity-50 flex items-center justify-center">
              <span className="text-white/50 font-mono text-sm bg-black/50 px-2 rounded">
                {Math.round(currentBounds.width)} x {Math.round(currentBounds.height)}
              </span>
            </div>
          )}
        </div>
      )}

      {/* Top text instructions / Preset picker */}
      <div className="absolute top-4 right-4 z-50 flex gap-2" onMouseDown={(e) => e.stopPropagation()}>
        <div className="group relative">
          <button className="bg-[#1e1f27]/90 backdrop-blur-sm border border-white/10 text-white px-4 py-2 rounded-lg font-medium text-sm flex items-center gap-2 hover:bg-[#252630] transition-colors shadow-xl">
            <span className="material-symbols-outlined text-[18px]">aspect_ratio</span>
            Boyut Seç
            <span className="material-symbols-outlined text-[16px]">expand_more</span>
          </button>
          <div className="absolute right-0 mt-2 w-48 bg-[#252530] border border-white/10 rounded-xl shadow-xl overflow-hidden hidden group-hover:block">
            {PRESETS.map((p) => (
              <button
                key={p.label}
                onClick={() => setPreset(p.width, p.height)}
                className="w-full text-left px-4 py-2 text-sm text-white/90 hover:bg-white/10 transition-colors"
              >
                {p.label}
              </button>
            ))}
          </div>
        </div>
        <button 
          onClick={cancelSelection}
          className="bg-[#1e1f27]/90 backdrop-blur-sm border border-white/10 text-white/70 hover:text-white px-3 py-2 rounded-lg transition-colors shadow-xl"
        >
          <span className="material-symbols-outlined text-[18px]">close</span>
        </button>
      </div>

      {/* Action buttons (only when bounds exist and not drawing) */}
      {bounds && !isDrawing && (
        <div 
          className="absolute flex items-center gap-2 bg-[#1e1f27] border border-white/10 rounded-lg p-2 shadow-xl z-50"
          style={{
            left: bounds.x + bounds.width / 2 - 60, // Center horizontally
            top: bounds.y + bounds.height + 16,     // Below the box
          }}
          onMouseDown={(e) => e.stopPropagation()} // Prevent redrawing when clicking buttons
        >
          <button 
            onClick={confirmSelection}
            className="px-6 py-2 rounded-md bg-indigo-500 hover:bg-indigo-600 text-white font-medium text-sm transition-colors flex items-center justify-center shadow-[0_0_15px_rgba(99,102,241,0.4)]"
          >
            <span className="material-symbols-outlined text-[20px]">check</span>
          </button>
        </div>
      )}
    </div>
  );
}
