import React, { useState, useRef, useEffect } from 'react';

export default function CropperOverlay() {
  const [isDrawing, setIsDrawing] = useState(false);
  const [startPos, setStartPos] = useState({ x: 0, y: 0 });
  const [currentPos, setCurrentPos] = useState({ x: 0, y: 0 });
  const [bounds, setBounds] = useState<{ x: number, y: number, width: number, height: number } | null>(null);

  const handleMouseDown = (e: React.MouseEvent) => {
    setIsDrawing(true);
    setStartPos({ x: e.clientX, y: e.clientY });
    setCurrentPos({ x: e.clientX, y: e.clientY });
    setBounds(null);
  };

  const handleMouseMove = (e: React.MouseEvent) => {
    if (!isDrawing) return;
    setCurrentPos({ x: e.clientX, y: e.clientY });
  };

  const handleMouseUp = () => {
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

  return (
    <div 
      className="fixed inset-0 cursor-crosshair z-[9999]" 
      onMouseDown={handleMouseDown}
      onMouseMove={handleMouseMove}
      onMouseUp={handleMouseUp}
      style={{ backgroundColor: 'rgba(0,0,0,0.4)' }} // Darken screen slightly
    >
      {/* Top text instructions */}
      {!bounds && !isDrawing && (
        <div className="absolute top-10 left-1/2 -translate-x-1/2 bg-black/70 text-white px-6 py-3 rounded-full pointer-events-none select-none font-medium">
          Kaydedilecek alanı çizmek için sürükleyin. Çıkmak için ESC'ye basın.
        </div>
      )}

      {/* The drawing box */}
      {(isDrawing || bounds) && (
        <div 
          className="absolute border-2 border-indigo-500 bg-indigo-500/10 pointer-events-none"
          style={{
            left: bounds ? bounds.x : drawX,
            top: bounds ? bounds.y : drawY,
            width: bounds ? bounds.width : drawW,
            height: bounds ? bounds.height : drawH,
            boxShadow: '0 0 0 9999px rgba(0,0,0,0.6)', // Creates a hollow cutout effect
          }}
        />
      )}

      {/* Action buttons (only when bounds exist and not drawing) */}
      {bounds && !isDrawing && (
        <div 
          className="absolute flex items-center gap-2 bg-[#1e1f27] border border-white/10 rounded-lg p-2 shadow-xl"
          style={{
            left: bounds.x + bounds.width - 120 > 0 ? bounds.x + bounds.width - 120 : bounds.x,
            top: bounds.y + bounds.height + 10,
          }}
          onMouseDown={(e) => e.stopPropagation()} // Prevent redrawing when clicking buttons
        >
          <button 
            onClick={cancelSelection}
            className="p-1.5 rounded-md hover:bg-white/10 text-white/70 hover:text-white transition-colors"
          >
            <span className="material-symbols-outlined text-[20px]">close</span>
          </button>
          <button 
            onClick={confirmSelection}
            className="px-3 py-1.5 rounded-md bg-indigo-500 hover:bg-indigo-600 text-white font-medium text-sm transition-colors flex items-center gap-1"
          >
            <span className="material-symbols-outlined text-[18px]">check</span> Kaydet
          </button>
        </div>
      )}
    </div>
  );
}
