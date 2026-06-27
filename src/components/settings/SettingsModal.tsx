import React, { useState } from 'react';
import { useAppStore } from '../../stores/appStore';
import type { AutoZoomMode, Resolution, ExportFps } from '../../types';

type SettingsTab = 'general' | 'record' | 'export';

function CustomSelect({ value, options, onChange, placement = 'bottom' }: { value: any, options: {value: any, label: string}[], onChange: (v: any) => void, placement?: 'top' | 'bottom' }) {
  const [open, setOpen] = useState(false);
  
  React.useEffect(() => {
    const handleClickOutside = () => setOpen(false);
    if (open) {
      document.addEventListener('click', handleClickOutside);
      return () => document.removeEventListener('click', handleClickOutside);
    }
  }, [open]);

  const selectedOption = options.find(o => o.value === value) || options[0];

  return (
    <div className="relative" onClick={(e) => e.stopPropagation()}>
      <button 
        onClick={() => setOpen(!open)}
        className="w-full flex items-center justify-between glass-input text-on-surface rounded-lg py-2.5 px-4 font-body-sm text-[13px] bg-[#1e1f27] border border-white/5 hover:border-white/10 transition-colors"
      >
        <span>{selectedOption.label}</span>
        <span className={`material-symbols-outlined text-[18px] text-white/50 transition-transform ${open ? 'rotate-180' : ''}`}>expand_more</span>
      </button>

      {open && (
        <div className={`absolute ${placement === 'top' ? 'bottom-full mb-2' : 'top-full mt-2'} left-0 w-full bg-[#252530] border border-white/10 rounded-xl shadow-xl overflow-hidden z-[400] py-1`}>
          {options.map((opt) => (
            <button
              key={opt.value}
              onClick={() => { onChange(opt.value); setOpen(false); }}
              className={`w-full text-left px-4 py-2 text-[13px] transition-colors ${
                value === opt.value ? 'bg-indigo-500/20 text-indigo-400' : 'text-white/90 hover:bg-white/10'
              }`}
            >
              {opt.label}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

export default function SettingsModal() {
  const { settingsOpen, setSettingsOpen, settings, updateSettings } = useAppStore();
  const [tab, setTab] = useState<SettingsTab | null>('general');
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
    <div className="fixed inset-0 bg-black/60 backdrop-blur-sm z-[300] flex items-center justify-center p-2 sm:p-4">
      <div className="bg-[#0f1015]/95 backdrop-blur-xl border border-white/10 w-full max-w-[600px] rounded-2xl shadow-2xl shadow-black/80 flex flex-col h-[450px] max-h-[90vh]">
        <div className="px-4 py-2 border-b border-white/10 flex justify-between items-center bg-white/[0.02]">
          <h2 className="font-headline-sm text-[16px] text-white font-medium flex items-center gap-2">
            <span className="material-symbols-outlined text-indigo-400 text-[20px]">settings</span>
            Ayarlar
          </h2>
          <button
            onClick={() => setSettingsOpen(false)}
            className="text-white/50 hover:text-white transition-colors p-1.5 rounded-full hover:bg-white/10 flex items-center justify-center"
          >
            <span className="material-symbols-outlined text-[18px]">close</span>
          </button>
        </div>

        <div className="flex-1 overflow-y-auto px-4 py-3 flex flex-col gap-3 scroll-smooth scrollbar-thin scrollbar-thumb-white/10">
          {(
            [
              { id: 'general' as const, icon: 'display_settings', label: 'Genel Ayarlar' },
              { id: 'record' as const, icon: 'videocam', label: 'Kayıt Tercihleri' },
              { id: 'export' as const, icon: 'output', label: 'Varsayılan Dışa Aktarma' },
            ] as const
          ).map((t) => (
            <div key={t.id} className="flex flex-col border border-white/5 rounded-xl bg-[#1e1f27] transition-all duration-300">
              <button
                onClick={() => setTab(tab === t.id ? null : t.id as SettingsTab)}
                className={`w-full text-left px-4 py-2.5 flex items-center justify-between transition-colors ${
                  tab === t.id ? 'bg-white/5 rounded-t-xl' : 'hover:bg-white/5 rounded-xl'
                }`}
              >
                <div className="flex items-center gap-3">
                  <span className={`material-symbols-outlined text-[20px] ${tab === t.id ? 'text-indigo-400 fill' : 'text-white/60'}`}>
                    {t.icon}
                  </span>
                  <span className={`font-medium text-[14px] ${tab === t.id ? 'text-white' : 'text-white/80'}`}>
                    {t.label}
                  </span>
                </div>
                <span className={`material-symbols-outlined text-[18px] text-white/50 transition-transform duration-300 ${tab === t.id ? 'rotate-180' : ''}`}>
                  expand_more
                </span>
              </button>

              <div className={`grid transition-all duration-300 ease-in-out ${tab === t.id ? 'grid-rows-[1fr] opacity-100' : 'grid-rows-[0fr] opacity-0'}`}>
                <div className="overflow-hidden">
                  <div className="p-5 border-t border-white/5 bg-[#0f1015]/50 flex flex-col gap-4 rounded-b-xl">
                    {t.id === 'general' && (
                    <>
                      <div className="flex flex-col gap-2">
                        <label className="font-label-sm text-[11px] text-white/50 uppercase tracking-wider font-semibold">
                          Proje Kayıt Konumu
                        </label>
                        <div className="flex gap-2">
                          <input
                            readOnly
                            value={local.projectLocation}
                            className="flex-1 bg-[#1e1f27] border border-white/5 text-white/90 rounded-lg py-2.5 px-4 font-body-sm text-[13px] truncate outline-none focus:border-indigo-500/50"
                          />
                          <button
                            onClick={() => handleSelectFolder('projectLocation')}
                            className="px-4 py-2 rounded-lg bg-white/5 border border-white/10 hover:bg-white/10 text-white text-[13px] font-medium transition-colors"
                          >
                            Seç
                          </button>
                        </div>
                      </div>
                      <div className="flex flex-col gap-2">
                        <label className="font-label-sm text-[11px] text-white/50 uppercase tracking-wider font-semibold">
                          Dışa Aktarma Konumu
                        </label>
                        <div className="flex gap-2">
                          <input
                            readOnly
                            value={local.exportLocation}
                            className="flex-1 bg-[#1e1f27] border border-white/5 text-white/90 rounded-lg py-2.5 px-4 font-body-sm text-[13px] truncate outline-none focus:border-indigo-500/50"
                          />
                          <button
                            onClick={() => handleSelectFolder('exportLocation')}
                            className="px-4 py-2 rounded-lg bg-white/5 border border-white/10 hover:bg-white/10 text-white text-[13px] font-medium transition-colors"
                          >
                            Seç
                          </button>
                        </div>
                      </div>
                    </>
                  )}

                  {t.id === 'record' && (
                    <>
                      <div className="flex flex-col gap-2">
                        <label className="font-label-sm text-[11px] text-white/50 uppercase tracking-wider font-semibold">
                          Otomatik Yakınlaştırma Efekti
                        </label>
                        <CustomSelect
                          value={local.autoZoom}
                          onChange={(v) => setLocal((s) => ({ ...s, autoZoom: v as AutoZoomMode }))}
                          options={[
                            { value: 'none', label: 'Yok' },
                            { value: 'smooth', label: 'Yumuşak Yakınlaştırma (Önerilen)' },
                            { value: 'instant', label: 'Anında Geçiş' },
                          ]}
                        />
                      </div>
                      <div className="grid grid-cols-2 gap-4">
                        <div className="flex flex-col gap-2">
                          <label className="font-label-sm text-[11px] text-white/50 uppercase tracking-wider font-semibold">
                            Çözünürlük
                          </label>
                          <CustomSelect
                            value={local.resolution}
                            onChange={(v) => setLocal((s) => ({ ...s, resolution: v as Resolution }))}
                            options={[
                              { value: '1080p', label: '1080p' },
                              { value: '4k', label: '4K' },
                            ]}
                          />
                        </div>
                        <div className="flex flex-col gap-2">
                          <label className="font-label-sm text-[11px] text-white/50 uppercase tracking-wider font-semibold">
                            Geri Sayım
                          </label>
                          <CustomSelect
                            value={local.countdown}
                            onChange={(v) => setLocal((s) => ({ ...s, countdown: Number(v) as 0 | 3 | 5 | 10 }))}
                            options={[
                              { value: 0, label: 'Yok' },
                              { value: 3, label: '3 Saniye' },
                              { value: 5, label: '5 Saniye' },
                              { value: 10, label: '10 Saniye' },
                            ]}
                          />
                        </div>
                      </div>
                    </>
                  )}

                  {t.id === 'export' && (
                    <div className="grid grid-cols-2 gap-4">
                      <div className="flex flex-col gap-2">
                        <label className="font-label-sm text-[11px] text-white/50 uppercase tracking-wider font-semibold">
                          Format
                        </label>
                        <div className="font-body-sm text-[13px] bg-[#1e1f27] text-white/90 rounded-lg px-4 py-2.5 border border-white/5 flex items-center">MP4</div>
                      </div>
                      <div className="flex flex-col gap-2">
                        <label className="font-label-sm text-[11px] text-white/50 uppercase tracking-wider font-semibold">
                          Kare Hızı
                        </label>
                        <CustomSelect
                          value={local.exportFps}
                          onChange={(v) => setLocal((s) => ({ ...s, exportFps: Number(v) as ExportFps }))}
                          options={[
                            { value: 30, label: '30 FPS' },
                            { value: 60, label: '60 FPS' },
                          ]}
                        />
                      </div>
                      <div className="flex flex-col gap-2">
                        <label className="font-label-sm text-[11px] text-white/50 uppercase tracking-wider font-semibold">
                          Çözünürlük
                        </label>
                        <CustomSelect
                          value={local.exportResolution}
                          onChange={(v) => setLocal((s) => ({ ...s, exportResolution: v as Resolution }))}
                          placement="top"
                          options={[
                            { value: '1080p', label: '1080P' },
                            { value: '4k', label: '4K' },
                          ]}
                        />
                      </div>
                    </div>
                  )}
                </div>
              </div>
            </div>
          </div>
        ))}
        </div>

        <div className="px-4 py-2 border-t border-white/10 bg-white/[0.02] flex justify-end gap-3">
          <button
            onClick={() => setSettingsOpen(false)}
            className="px-6 py-2 rounded-lg font-body-sm text-[13px] text-white/70 hover:text-white border border-transparent hover:bg-white/5 transition-colors font-medium"
          >
            İptal
          </button>
          <button
            onClick={handleSave}
            className="px-6 py-2 rounded-lg font-body-sm text-[13px] bg-indigo-500 text-white hover:bg-indigo-600 transition-colors shadow-[0_0_15px_rgba(99,102,241,0.3)] font-medium"
          >
            Değişiklikleri Kaydet
          </button>
        </div>
      </div>
    </div>
  );
}
