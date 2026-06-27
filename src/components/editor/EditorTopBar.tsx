import { useAppStore } from '../../stores/appStore';

export default function EditorTopBar() {
  const currentProject = useAppStore(s => s.currentProject);

  const handleMinimize = () => window.electronAPI.windowMinimize();
  const handleMaximize = () => window.electronAPI.windowMaximize();
  const handleClose = () => window.electronAPI.windowClose();

  return (
    <div 
      className="h-10 bg-[#16161e] border-b border-white/5 flex items-center justify-between px-4 select-none"
      style={{ WebkitAppRegion: 'drag' } as React.CSSProperties}
    >
      <div className="flex items-center gap-4" style={{ WebkitAppRegion: 'no-drag' } as React.CSSProperties}>
        <div className="flex items-center gap-2 text-white/90">
          <span className="material-symbols-outlined text-[18px] text-indigo-500">video_camera_front</span>
          <span className="font-semibold text-sm tracking-wide">ScreenPowerPro</span>
        </div>
        <div className="w-px h-4 bg-white/10" />
        <button className="text-[13px] text-white/70 hover:text-white transition-colors flex items-center gap-1">
          <span className="material-symbols-outlined text-[16px]">folder</span> File
        </button>
      </div>

      <div className="absolute left-1/2 -translate-x-1/2 text-[13px] text-white/50 flex items-center gap-2">
        <span className="material-symbols-outlined text-[16px]">edit</span>
        {currentProject?.name || 'Kayıt'}
      </div>

      <div className="flex items-center gap-4" style={{ WebkitAppRegion: 'no-drag' } as React.CSSProperties}>
        <button className="text-[12px] bg-indigo-500/20 text-indigo-400 px-3 py-1 rounded-md hover:bg-indigo-500/30 transition-colors font-medium flex items-center gap-1">
          <span className="material-symbols-outlined text-[14px]">stars</span> Referral
        </button>
        <div className="w-px h-4 bg-white/10" />
        <div className="flex items-center gap-2 text-white/50">
          <button onClick={handleMinimize} className="w-6 h-6 flex items-center justify-center hover:bg-white/10 rounded transition-colors">
            <span className="material-symbols-outlined text-[16px]">remove</span>
          </button>
          <button onClick={handleMaximize} className="w-6 h-6 flex items-center justify-center hover:bg-white/10 rounded transition-colors">
            <span className="material-symbols-outlined text-[14px]">crop_square</span>
          </button>
          <button onClick={handleClose} className="w-6 h-6 flex items-center justify-center hover:bg-red-500 hover:text-white rounded transition-colors">
            <span className="material-symbols-outlined text-[16px]">close</span>
          </button>
        </div>
      </div>
    </div>
  );
}
