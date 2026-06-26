import React from 'react';

interface SidebarItemProps {
  icon: string | React.ReactNode;
  label: string;
  isActive: boolean;
  onClick: () => void;
}

const SidebarItem = ({ icon, label, isActive, onClick }: SidebarItemProps) => (
  <button
    onClick={onClick}
    title={label}
    className={`w-12 h-12 flex items-center justify-center rounded-xl mb-2 transition-colors ${
      isActive 
        ? 'bg-indigo-500/20 text-indigo-400' 
        : 'text-white/50 hover:bg-white/5 hover:text-white/90'
    }`}
  >
    {typeof icon === 'string' ? (
      <span className="material-symbols-outlined text-[24px]">{icon}</span>
    ) : (
      icon
    )}
  </button>
);

export default function EditorSidebar() {
  const [activeTab, setActiveTab] = React.useState('motion');

  return (
    <div className="w-[72px] h-full bg-[#1b1b22] border-r border-white/5 flex flex-col items-center py-4 flex-shrink-0 z-20">
      <SidebarItem 
        icon="blur_on" 
        label="Motion Blur" 
        isActive={activeTab === 'motion'} 
        onClick={() => setActiveTab('motion')} 
      />
      <SidebarItem 
        icon="arrow_selector_tool" 
        label="Cursor" 
        isActive={activeTab === 'cursor'} 
        onClick={() => setActiveTab('cursor')} 
      />
      <SidebarItem 
        icon="zoom_in" 
        label="Zoom & Pan" 
        isActive={activeTab === 'zoom'} 
        onClick={() => setActiveTab('zoom')} 
      />
      <div className="w-8 h-px bg-white/10 my-2" />
      <SidebarItem 
        icon="videocam" 
        label="Camera" 
        isActive={activeTab === 'camera'} 
        onClick={() => setActiveTab('camera')} 
      />
      <SidebarItem 
        icon="graphic_eq" 
        label="Audio" 
        isActive={activeTab === 'audio'} 
        onClick={() => setActiveTab('audio')} 
      />
      <SidebarItem 
        icon="keyboard" 
        label="Keyboard" 
        isActive={activeTab === 'keyboard'} 
        onClick={() => setActiveTab('keyboard')} 
      />
      <SidebarItem 
        icon="format_color_fill" 
        label="Background" 
        isActive={activeTab === 'bg'} 
        onClick={() => setActiveTab('bg')} 
      />
      <SidebarItem 
        icon="water_drop" 
        label="Watermark" 
        isActive={activeTab === 'watermark'} 
        onClick={() => setActiveTab('watermark')} 
      />
    </div>
  );
}
