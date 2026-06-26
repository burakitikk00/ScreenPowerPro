import { useAppStore } from '../../stores/appStore';
import React from 'react';

interface ToolButtonProps {
  icon: string | React.ReactNode;
  label: string;
  isActive: boolean;
  onClick: () => void;
  className?: string;
}

const ToolButton = ({ icon, label, isActive, onClick, className = '' }: ToolButtonProps) => (
  <button
    onClick={onClick}
    title={label}
    className={`w-10 h-10 flex items-center justify-center rounded-lg transition-colors ${
      isActive
        ? 'bg-primary/20 text-primary ring-1 ring-primary/50'
        : 'text-on-surface-variant hover:bg-white/10 hover:text-on-surface'
    } ${className}`}
  >
    {typeof icon === 'string' ? (
      <span className="material-symbols-outlined text-[20px]">{icon}</span>
    ) : (
      icon
    )}
  </button>
);

export default function EditorToolbar() {
  const { activeEditorTool, activeEditorSubTool, setActiveEditorTool } = useAppStore();

  const isToolActive = (tool: string, subTool?: string) => {
    if (subTool) {
      return activeEditorTool === tool && activeEditorSubTool === subTool;
    }
    return activeEditorTool === tool;
  };

  return (
    <div className="w-[280px] h-full bg-surface-container-low border-r border-white/5 flex flex-col overflow-y-auto hidden md:flex z-30 shrink-0 select-none">
      <div className="p-4 space-y-6">
        
        {/* Selection Tool */}
        <div>
          <ToolButton
            icon="arrow_selector_tool"
            label="Seçici"
            isActive={isToolActive('cursor')}
            onClick={() => setActiveEditorTool('cursor')}
            className="w-full justify-start px-4 gap-3"
          />
        </div>

        {/* Text Tools */}
        <div>
          <h3 className="text-label-sm font-label-sm text-on-surface-variant/60 mb-3 px-1 uppercase tracking-wider">Text</h3>
          <div className="grid grid-cols-4 gap-2">
            <ToolButton
              icon={<span className="font-bold text-sm">Text</span>}
              label="Düz Metin"
              isActive={isToolActive('text', 'plain')}
              onClick={() => setActiveEditorTool('text', 'plain')}
            />
            <ToolButton
              icon={<span className="font-bold text-sm bg-on-surface/20 px-1 rounded">Text</span>}
              label="Arkaplanlı Metin"
              isActive={isToolActive('text', 'background')}
              onClick={() => setActiveEditorTool('text', 'background')}
            />
            <ToolButton
              icon={<span className="font-bold text-sm border border-on-surface/50 px-1 rounded">Text</span>}
              label="Çerçeveli Metin"
              isActive={isToolActive('text', 'outline')}
              onClick={() => setActiveEditorTool('text', 'outline')}
            />
            <ToolButton
              icon="looks_one"
              label="Numaralı Liste"
              isActive={isToolActive('text', 'badge')}
              onClick={() => setActiveEditorTool('text', 'badge')}
            />
          </div>
        </div>

        {/* Shape Tools */}
        <div>
          <h3 className="text-label-sm font-label-sm text-on-surface-variant/60 mb-3 px-1 uppercase tracking-wider">Shape</h3>
          <div className="grid grid-cols-4 gap-2">
            <ToolButton
              icon="horizontal_rule"
              label="Çizgi"
              isActive={isToolActive('shape', 'line')}
              onClick={() => setActiveEditorTool('shape', 'line')}
            />
            <ToolButton
              icon={<div className="w-5 h-[2px] bg-current border-dashed border-t-2 border-current"></div>}
              label="Kesikli Çizgi"
              isActive={isToolActive('shape', 'dashed-line')}
              onClick={() => setActiveEditorTool('shape', 'dashed-line')}
            />
            <ToolButton
              icon="arrow_right_alt"
              label="Ok"
              isActive={isToolActive('shape', 'arrow')}
              onClick={() => setActiveEditorTool('shape', 'arrow')}
            />
            <ToolButton
              icon="crop_square"
              label="Kare"
              isActive={isToolActive('shape', 'rect')}
              onClick={() => setActiveEditorTool('shape', 'rect')}
            />
            <ToolButton
              icon={<div className="w-4 h-4 border-2 border-current rounded-md"></div>}
              label="Yuvarlak Köşeli Kare"
              isActive={isToolActive('shape', 'rounded-rect')}
              onClick={() => setActiveEditorTool('shape', 'rounded-rect')}
            />
            <ToolButton
              icon="radio_button_unchecked"
              label="Daire"
              isActive={isToolActive('shape', 'circle')}
              onClick={() => setActiveEditorTool('shape', 'circle')}
            />
            <ToolButton
              icon={<div className="w-5 h-3 border-2 border-current rounded-[50%]"></div>}
              label="Oval"
              isActive={isToolActive('shape', 'oval')}
              onClick={() => setActiveEditorTool('shape', 'oval')}
            />
            <ToolButton
              icon="south"
              label="Aşağı Ok"
              isActive={isToolActive('shape', 'arrow-down')}
              onClick={() => setActiveEditorTool('shape', 'arrow-down')}
            />
            <ToolButton
              icon="pan_tool_alt"
              label="İşaretçi"
              isActive={isToolActive('shape', 'pointer')}
              onClick={() => setActiveEditorTool('shape', 'pointer')}
            />
          </div>
        </div>

        {/* Mask Tools */}
        <div>
          <h3 className="text-label-sm font-label-sm text-on-surface-variant/60 mb-3 px-1 uppercase tracking-wider">Mask</h3>
          <div className="grid grid-cols-4 gap-2">
            <ToolButton
              icon="highlight"
              label="Spot Işığı"
              isActive={isToolActive('mask', 'spotlight')}
              onClick={() => setActiveEditorTool('mask', 'spotlight')}
            />
            <ToolButton
              icon="blur_on"
              label="Bulanıklık"
              isActive={isToolActive('mask', 'blur')}
              onClick={() => setActiveEditorTool('mask', 'blur')}
            />
            <ToolButton
              icon="search"
              label="Büyüteç"
              isActive={isToolActive('mask', 'magnifier')}
              onClick={() => setActiveEditorTool('mask', 'magnifier')}
            />
          </div>
        </div>
        
      </div>
    </div>
  );
}
