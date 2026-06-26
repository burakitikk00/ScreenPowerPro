import { useState } from 'react';
import { useAppStore } from '../../stores/appStore';
import type { AutoZoomMode, Resolution, ExportFps } from '../../types';

type SettingsTab = 'general' | 'record' | 'export';

export default function SettingsModal() {
  const { settingsOpen, setSettingsOpen, settings, updateSettings } = useAppStore();
  const [tab, setTab] = useState<SettingsTab>('record');
  const [local, setLocal] = useState(settings);

  if (!settingsOpen) return null;

  const handleSave = async () => {
    await window.electronAPI.saveSettings(local);
    updateSettings(local);
    setSettingsOpen(false);
  };

  const handleSelectFolder = async (field: 'projectLocation' | 'exportLocation') => {
    const path = await window.electronAPI.selectFolder(
      field === 'projectLocation' ? 'Proje Klasörü Seç' : 'Dışa Aktarma Klasörü Seç'
    );
    if (path) setLocal((s) => ({ ...s, [field]: path }));
  };

  return (
    <div className="fixed inset-0 bg-black/60 backdrop-blur-sm z-[300] flex items-center justify-center p-4">
      <div className="glass-panel w-full max-w-2xl rounded-xl shadow-2xl shadow-black/50 overflow-hidden flex flex-col max-h-[90vh]">
        <div className="px-container-margin py-4 border-b border-white/10 flex justify-between items-center bg-white/[0.02]">
          <h2 className="font-headline-md text-headline-md text-on-surface tracking-tight flex items-center gap-2">
            <span className="material-symbols-outlined text-primary fill">settings</span>
            Ayarlar
          </h2>
          <button
            onClick={() => setSettingsOpen(false)}
            className="text-on-surface-variant hover:text-on-surface transition-colors p-1 rounded-md hover:bg-white/10"
          >
            <span className="material-symbols-outlined">close</span>
          </button>
        </div>

        <div className="flex-1 overflow-y-auto p-container-margin grid grid-cols-1 md:grid-cols-[200px_1fr] gap-8">
          <div className="flex flex-col gap-2">
            {(
              [
                { id: 'general' as const, icon: 'display_settings', label: 'Genel' },
                { id: 'record' as const, icon: 'videocam', label: 'Kayıt' },
                { id: 'export' as const, icon: 'output', label: 'Dışa Aktar' },
              ] as const
            ).map((t) => (
              <button
                key={t.id}
                onClick={() => setTab(t.id)}
                className={`text-left px-4 py-2.5 rounded-lg font-body-md text-body-md flex items-center gap-3 transition-colors ${
                  tab === t.id
                    ? 'bg-primary/10 text-primary border border-primary/20 shadow-[0_0_15px_rgba(192,193,255,0.1)]'
                    : 'text-on-surface-variant hover:bg-white/5'
                }`}
              >
                <span className={`material-symbols-outlined text-[18px] ${tab === t.id ? 'fill' : ''}`}>
                  {t.icon}
                </span>
                {t.label}
              </button>
            ))}
          </div>

          <div className="flex flex-col gap-6">
            {tab === 'general' && (
              <div className="flex flex-col gap-6">
                <h3 className="font-headline-md text-[18px] text-on-surface border-b border-white/10 pb-2">
                  Genel Ayarlar
                </h3>
                <div className="flex flex-col gap-2">
                  <label className="font-label-md text-label-md text-on-surface-variant uppercase tracking-wider">
                    Proje Kayıt Konumu
                  </label>
                  <div className="flex gap-2">
                    <input
                      readOnly
                      value={local.projectLocation}
                      className="flex-1 glass-input text-on-surface rounded-lg py-2.5 px-4 font-body-md"
                    />
                    <button
                      onClick={() => handleSelectFolder('projectLocation')}
                      className="px-4 py-2 rounded-lg border border-white/10 hover:bg-white/5 text-on-surface"
                    >
                      Seç
                    </button>
                  </div>
                </div>
                <div className="flex flex-col gap-2">
                  <label className="font-label-md text-label-md text-on-surface-variant uppercase tracking-wider">
                    Dışa Aktarma Konumu
                  </label>
                  <div className="flex gap-2">
                    <input
                      readOnly
                      value={local.exportLocation}
                      className="flex-1 glass-input text-on-surface rounded-lg py-2.5 px-4 font-body-md"
                    />
                    <button
                      onClick={() => handleSelectFolder('exportLocation')}
                      className="px-4 py-2 rounded-lg border border-white/10 hover:bg-white/5 text-on-surface"
                    >
                      Seç
                    </button>
                  </div>
                </div>
              </div>
            )}

            {tab === 'record' && (
              <div className="flex flex-col gap-6">
                <h3 className="font-headline-md text-[18px] text-on-surface border-b border-white/10 pb-2">
                  Kayıt Tercihleri
                </h3>
                <div className="flex flex-col gap-2">
                  <label className="font-label-md text-label-md text-on-surface-variant uppercase tracking-wider">
                    Otomatik Yakınlaştırma Efekti
                  </label>
                  <select
                    value={local.autoZoom}
                    onChange={(e) => setLocal((s) => ({ ...s, autoZoom: e.target.value as AutoZoomMode }))}
                    className="w-full glass-input text-on-surface rounded-lg py-2.5 px-4 appearance-none cursor-pointer"
                  >
                    <option value="none">Yok</option>
                    <option value="smooth">Yumuşak Yakınlaştırma (Önerilen)</option>
                    <option value="instant">Anında Geçiş</option>
                  </select>
                </div>
                <div className="grid grid-cols-2 gap-4">
                  <div className="flex flex-col gap-2">
                    <label className="font-label-md text-label-md text-on-surface-variant uppercase tracking-wider">
                      Çözünürlük
                    </label>
                    <select
                      value={local.resolution}
                      onChange={(e) => setLocal((s) => ({ ...s, resolution: e.target.value as Resolution }))}
                      className="w-full glass-input text-on-surface rounded-lg py-2.5 px-4"
                    >
                      <option value="1080p">1080p</option>
                      <option value="4k">4K</option>
                    </select>
                  </div>
                  <div className="flex flex-col gap-2">
                    <label className="font-label-md text-label-md text-on-surface-variant uppercase tracking-wider">
                      Geri Sayım
                    </label>
                    <select
                      value={local.countdown}
                      onChange={(e) =>
                        setLocal((s) => ({
                          ...s,
                          countdown: Number(e.target.value) as 0 | 3 | 5 | 10,
                        }))
                      }
                      className="w-full glass-input text-on-surface rounded-lg py-2.5 px-4"
                    >
                      <option value={0}>Yok</option>
                      <option value={3}>3 Saniye</option>
                      <option value={5}>5 Saniye</option>
                      <option value={10}>10 Saniye</option>
                    </select>
                  </div>
                </div>
              </div>
            )}

            {tab === 'export' && (
              <div className="flex flex-col gap-6">
                <h3 className="font-headline-md text-[18px] text-on-surface border-b border-white/10 pb-2">
                  Varsayılan Dışa Aktarma
                </h3>
                <div className="grid grid-cols-2 gap-4">
                  <div className="flex flex-col gap-2">
                    <label className="font-label-md text-label-md text-on-surface-variant uppercase tracking-wider">
                      Format
                    </label>
                    <div className="font-body-md bg-white/5 rounded-lg px-3 py-2.5 border border-white/10">MP4</div>
                  </div>
                  <div className="flex flex-col gap-2">
                    <label className="font-label-md text-label-md text-on-surface-variant uppercase tracking-wider">
                      Kare Hızı
                    </label>
                    <select
                      value={local.exportFps}
                      onChange={(e) =>
                        setLocal((s) => ({ ...s, exportFps: Number(e.target.value) as ExportFps }))
                      }
                      className="w-full glass-input text-on-surface rounded-lg py-2.5 px-4"
                    >
                      <option value={30}>30 FPS</option>
                      <option value={60}>60 FPS</option>
                    </select>
                  </div>
                  <div className="flex flex-col gap-2">
                    <label className="font-label-md text-label-md text-on-surface-variant uppercase tracking-wider">
                      Çözünürlük
                    </label>
                    <select
                      value={local.exportResolution}
                      onChange={(e) =>
                        setLocal((s) => ({ ...s, exportResolution: e.target.value as Resolution }))
                      }
                      className="w-full glass-input text-on-surface rounded-lg py-2.5 px-4"
                    >
                      <option value="1080p">1080P</option>
                      <option value="4k">4K</option>
                    </select>
                  </div>
                </div>
              </div>
            )}
          </div>
        </div>

        <div className="px-container-margin py-4 border-t border-white/10 bg-white/[0.02] flex justify-end gap-3">
          <button
            onClick={() => setSettingsOpen(false)}
            className="px-6 py-2 rounded-lg font-body-md text-on-surface border border-white/10 hover:bg-white/5 transition-colors"
          >
            İptal
          </button>
          <button
            onClick={handleSave}
            className="px-6 py-2 rounded-lg font-body-md bg-primary text-on-primary hover:bg-primary-container transition-colors shadow-[0_0_15px_rgba(192,193,255,0.3)]"
          >
            Değişiklikleri Kaydet
          </button>
        </div>
      </div>
    </div>
  );
}
