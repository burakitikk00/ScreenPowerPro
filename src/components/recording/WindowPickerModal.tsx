import { useEffect, useState } from 'react';
import { useAppStore } from '../../stores/appStore';
import type { ScreenSource } from '../../types';

interface WindowPickerModalProps {
  onSelect: (sourceId: string) => void;
  onClose: () => void;
}

export default function WindowPickerModal({ onSelect, onClose }: WindowPickerModalProps) {
  const [sources, setSources] = useState<ScreenSource[]>([]);
  const [loading, setLoading] = useState(true);
  const [filter, setFilter] = useState('');
  const { recordingMode } = useAppStore();

  useEffect(() => {
    window.electronAPI.getScreenSources().then((s) => {
      const filtered =
        recordingMode === 'window'
          ? s.filter((src) => src.id.startsWith('window:'))
          : s;
      setSources(filtered);
      setLoading(false);
    });
  }, [recordingMode]);

  const displayed = sources.filter((s) =>
    s.name.toLowerCase().includes(filter.toLowerCase())
  );

  return (
    <div className="fixed inset-0 bg-black/70 backdrop-blur-sm z-[250] flex items-center justify-center p-4">
      <div className="glass-panel w-full max-w-3xl rounded-xl shadow-2xl overflow-hidden flex flex-col max-h-[80vh]">
        <div className="px-6 py-4 border-b border-white/10 flex justify-between items-center">
          <h2 className="font-headline-md text-headline-md text-on-surface flex items-center gap-2">
            <span className="material-symbols-outlined text-primary">window</span>
            {recordingMode === 'window' ? 'Pencere Seç' : 'Kaynak Seç'}
          </h2>
          <button onClick={onClose} className="text-on-surface-variant hover:text-on-surface p-1 rounded-md hover:bg-white/10">
            <span className="material-symbols-outlined">close</span>
          </button>
        </div>

        <div className="px-6 py-3 border-b border-white/5">
          <input
            type="text"
            placeholder="Ara..."
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            className="w-full glass-input text-on-surface rounded-lg py-2 px-4 font-body-md"
          />
        </div>

        <div className="flex-1 overflow-y-auto p-4 grid grid-cols-2 md:grid-cols-3 gap-3">
          {loading && (
            <p className="col-span-full text-center text-on-surface-variant py-8">Yükleniyor...</p>
          )}
          {!loading && displayed.length === 0 && (
            <p className="col-span-full text-center text-on-surface-variant py-8">Kaynak bulunamadı</p>
          )}
          {displayed.map((src) => (
            <button
              key={src.id}
              onClick={() => onSelect(src.id)}
              className="group flex flex-col gap-2 p-3 rounded-lg border border-white/10 hover:border-primary/50 hover:bg-white/5 transition-all text-left"
            >
              <div className="aspect-video rounded-md overflow-hidden bg-black border border-white/10">
                {src.thumbnail ? (
                  <img src={src.thumbnail} alt={src.name} className="w-full h-full object-cover" />
                ) : (
                  <div className="w-full h-full flex items-center justify-center text-on-surface-variant">
                    <span className="material-symbols-outlined">desktop_windows</span>
                  </div>
                )}
              </div>
              <span className="font-body-md text-[13px] text-on-surface truncate group-hover:text-primary transition-colors">
                {src.name}
              </span>
            </button>
          ))}
        </div>
      </div>
    </div>
  );
}
