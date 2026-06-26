import { useAppStore } from '../../stores/appStore';
import type { AppScreen } from '../../types';

interface SideNavProps {
  active?: AppScreen | 'library';
}

const navItems: { id: AppScreen | 'library'; icon: string; label: string }[] = [
  { id: 'dashboard', icon: 'videocam', label: 'Kaydedici' },
  { id: 'library', icon: 'video_library', label: 'Kütüphane' },
  { id: 'editor', icon: 'edit_note', label: 'Düzenleyici' },
  { id: 'dashboard', icon: 'handyman', label: 'Araçlar' },
  { id: 'dashboard', icon: 'insights', label: 'Analizler' },
];

export default function SideNav({ active = 'dashboard' }: SideNavProps) {
  const setScreen = useAppStore((s) => s.setScreen);
  const setSettingsOpen = useAppStore((s) => s.setSettingsOpen);

  return (
    <aside className="fixed left-0 top-toolbar-height h-[calc(100vh-48px)] w-sidebar-width bg-white/5 backdrop-blur-md border-r border-white/10 shadow-xl flex flex-col py-panel-gap hidden md:flex z-40">
      <div className="flex-1 px-4 flex flex-col gap-2 mt-4">
        {navItems.map((item, i) => {
          const isActive =
            (item.id === 'dashboard' && active === 'dashboard' && i === 0) ||
            (item.id === 'editor' && active === 'editor');
          return (
            <button
              key={`${item.id}-${i}`}
              onClick={() => item.id !== 'library' && setScreen(item.id as AppScreen)}
              className={`w-full flex items-center gap-3 px-4 py-3 rounded-lg transition-all duration-300 group ${
                isActive
                  ? 'bg-primary/20 text-primary border-r-2 border-primary'
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
        <button className="w-full py-2 px-4 rounded-lg bg-white/5 hover:bg-white/10 text-on-surface font-label-md text-label-md border border-white/10 transition-colors duration-300 mb-4">
          Pro&apos;ya Yükselt
        </button>
        <div className="flex flex-col gap-2">
          <button
            onClick={() => setSettingsOpen(true)}
            className="w-full flex items-center gap-3 px-4 py-2 rounded-lg text-on-surface-variant hover:text-on-surface hover:bg-white/5 transition-all duration-300 group"
          >
            <span className="material-symbols-outlined text-[18px] group-hover:text-primary transition-colors">
              help
            </span>
            <span className="font-label-md text-label-md">Destek</span>
          </button>
          <button className="w-full flex items-center gap-3 px-4 py-2 rounded-lg text-on-surface-variant hover:text-on-surface hover:bg-white/5 transition-all duration-300 group">
            <span className="material-symbols-outlined text-[18px] group-hover:text-primary transition-colors">
              person
            </span>
            <span className="font-label-md text-label-md">Hesap</span>
          </button>
        </div>
      </div>
    </aside>
  );
}
