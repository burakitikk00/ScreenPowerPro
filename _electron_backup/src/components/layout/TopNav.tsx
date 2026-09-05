import { useAppStore } from '../../stores/appStore';

interface TopNavProps {
  title?: string;
  showExport?: boolean;
  onExport?: () => void;
  onBack?: () => void;
}

export default function TopNav({ title, showExport, onExport, onBack }: TopNavProps) {
  const setSettingsOpen = useAppStore((s) => s.setSettingsOpen);

  return (
    <nav 
      className="fixed top-0 w-full z-50 flex justify-between items-center px-container-margin h-12 border-b border-white/10 bg-[#0f1015]/90 backdrop-blur-xl shadow-2xl shadow-black/40"
      style={{ WebkitAppRegion: 'drag' } as React.CSSProperties}
    >
      <div className="flex items-center gap-2" style={{ WebkitAppRegion: 'no-drag' } as React.CSSProperties}>
        {onBack ? (
          <button
            onClick={onBack}
            className="w-8 h-8 flex items-center justify-center rounded-full hover:bg-white/10 transition-colors text-white/70 hover:text-white mr-2"
          >
            <span className="material-symbols-outlined text-[20px]">arrow_back</span>
          </button>
        ) : (
          <div className="w-6 h-6 rounded-full bg-gradient-to-tr from-indigo-500 to-purple-500 flex items-center justify-center text-on-primary">
            <span className="material-symbols-outlined text-[14px] text-white">videocam</span>
          </div>
        )}
        <span className="font-headline-sm text-sm font-semibold tracking-wide text-white">
          ScreenPowerPro
        </span>
        {title && (
          <>
            <div className="h-4 w-px bg-white/10 mx-2" />
            <span className="text-white/70 font-body-sm text-[13px] flex items-center gap-2">
              <span className="material-symbols-outlined text-[16px]">edit</span>
              {title}
            </span>
          </>
        )}
      </div>
      
      <div className="flex items-center gap-4" style={{ WebkitAppRegion: 'no-drag' } as React.CSSProperties}>
        <div className="flex items-center gap-2 mr-2">
          {showExport && (
            <button
              onClick={onExport}
              className="bg-indigo-500 text-white px-4 py-1.5 rounded-lg text-[13px] font-medium hover:bg-indigo-600 transition-colors shadow-lg"
            >
              Dışa Aktar
            </button>
          )}
          <button
            onClick={() => setSettingsOpen(true)}
            className="text-white/70 hover:text-white hover:bg-white/10 transition-colors duration-200 w-8 h-8 flex items-center justify-center rounded-full"
          >
            <span className="material-symbols-outlined text-[18px]">
              settings
            </span>
          </button>
        </div>

        {/* Window Controls */}
        <div className="flex items-center gap-2 text-white/50 border-l border-white/10 pl-4">
          <button onClick={() => window.electronAPI.windowMinimize()} className="w-7 h-7 flex items-center justify-center rounded-full hover:bg-white/10 hover:text-white transition-colors">
            <span className="material-symbols-outlined text-[16px]">remove</span>
          </button>
          <button onClick={() => window.electronAPI.windowClose()} className="w-7 h-7 flex items-center justify-center rounded-full hover:bg-red-500 hover:text-white transition-colors">
            <span className="material-symbols-outlined text-[16px]">close</span>
          </button>
        </div>
      </div>
    </nav>
  );
}
