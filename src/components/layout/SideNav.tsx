import { useAppStore } from '../../stores/appStore';
import type { AppScreen } from '../../types';

interface SideNavProps {
  active?: AppScreen;
}

const navItems: { id: AppScreen; icon: string; label: string }[] = [
  { id: 'dashboard', icon: 'videocam', label: 'Kaydedici' },
  { id: 'library', icon: 'video_library', label: 'Kütüphane' },
  { id: 'editor', icon: 'edit_note', label: 'Düzenleyici' },
];

export default function SideNav({ active = 'dashboard' }: SideNavProps) {
  const setScreen = useAppStore((s) => s.setScreen);
  const setSettingsOpen = useAppStore((s) => s.setSettingsOpen);
  const currentProject = useAppStore((s) => s.currentProject);

  return (
    <aside className="fixed left-0 top-toolbar-height h-[calc(100vh-48px)] w-sidebar-width bg-white/5 backdrop-blur-md border-r border-white/10 shadow-xl flex flex-col py-panel-gap hidden md:flex z-40">
      <div className="flex-1 px-4 flex flex-col gap-2 mt-4">
        {navItems.map((item) => {
          const isDisabled = item.id === 'editor' && !currentProject;
          const isActive = active === item.id;
          return (
            <button
              key={item.id}
              disabled={isDisabled}
              onClick={() => !isDisabled && setScreen(item.id)}
              className={`w-full flex items-center gap-3 px-4 py-3 rounded-lg transition-all duration-300 group ${
                isActive
                  ? 'bg-primary/20 text-primary border-r-2 border-primary'
                  : isDisabled
                    ? 'text-on-surface-variant/40 cursor-not-allowed'
                    : 'text-on-surface-variant hover:text-on-surface hover:bg-white/5'
              }`}
            >
              <span
                className={`material-symbols-outlined ${isActive ? 'fill' : ''} group-hover:text-primary transition-colors`}
              >
                {item.icon}
              </span>
              <span className="font-label-md text-label-md">{item.label}</span>
            </button>
          );
        })}
      </div>
      <div className="px-4 pb-4">
        <button
          onClick={() => setSettingsOpen(true)}
          className="w-full flex items-center gap-3 px-4 py-2 rounded-lg text-on-surface-variant hover:text-on-surface hover:bg-white/5 transition-all duration-300 group"
        >
          <span className="material-symbols-outlined text-[18px] group-hover:text-primary transition-colors">
            settings
          </span>
          <span className="font-label-md text-label-md">Ayarlar</span>
        </button>
      </div>
    </aside>
  );
}
