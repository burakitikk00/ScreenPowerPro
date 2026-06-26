import { useAppStore } from '../../stores/appStore';

interface TopNavProps {
  title?: string;
  showExport?: boolean;
  onExport?: () => void;
}

export default function TopNav({ title, showExport, onExport }: TopNavProps) {
  const setSettingsOpen = useAppStore((s) => s.setSettingsOpen);

  return (
    <nav className="fixed top-0 w-full z-50 flex justify-between items-center px-container-margin h-toolbar-height border-b border-white/10 bg-white/5 backdrop-blur-xl shadow-2xl shadow-black/40">
      <div className="flex items-center gap-2">
        <div className="w-6 h-6 rounded-full bg-primary flex items-center justify-center text-on-primary">
          <span className="material-symbols-outlined text-[16px] fill">videocam</span>
        </div>
        <span className="font-headline-md text-headline-md font-bold text-on-surface tracking-tight">
          ScreenPowerPro
        </span>
        {title && (
          <>
            <div className="h-4 w-px bg-white/10 mx-2" />
            <span className="text-on-surface-variant font-body-md text-body-md flex items-center gap-2">
              <span className="material-symbols-outlined text-sm">edit</span>
              {title}
            </span>
          </>
        )}
      </div>
      <div className="flex items-center gap-4">
        <button
          onClick={() => setSettingsOpen(true)}
          className="text-on-surface-variant hover:bg-white/10 transition-colors duration-200 p-2 rounded-full active:scale-95 group"
        >
          <span className="material-symbols-outlined group-hover:text-primary transition-colors">
            settings
          </span>
        </button>
        {showExport && (
          <button
            onClick={onExport}
            className="bg-primary text-on-primary px-4 py-1.5 rounded font-label-md text-label-md hover:bg-primary/90 transition-colors shadow-[0_0_15px_rgba(192,193,255,0.3)]"
          >
            Dışa Aktar
          </button>
        )}
      </div>
    </nav>
  );
}
