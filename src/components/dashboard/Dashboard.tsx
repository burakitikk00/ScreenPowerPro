import { useState } from 'react';
import { useAppStore } from '../../stores/appStore';
import { useRecording } from '../../hooks/useRecording';
import type { RecordingMode } from '../../types';
import TopNav from '../layout/TopNav';
import SideNav from '../layout/SideNav';

const modes: { id: RecordingMode; icon: string; label: string }[] = [
  { id: 'fullscreen', icon: 'fullscreen', label: 'Tam Ekran' },
  { id: 'custom', icon: 'crop', label: 'Özel Alan' },
  { id: 'window', icon: 'window', label: 'Pencere' },
  { id: 'camera', icon: 'photo_camera', label: 'Sadece Kamera' },
];

export default function Dashboard() {
  const { recordingMode, setRecordingMode, countdown, settings } = useAppStore();
  const { startRecording, isPreparing } = useRecording();
  const [micLabel] = useState('Varsayılan Mikrofon');
  const [cameraLabel] = useState('Kapalı');

  return (
    <div className="min-h-screen relative flex flex-col">
      <TopNav />
      <SideNav active="dashboard" />

      <main className="flex-1 ml-0 md:ml-sidebar-width mt-toolbar-height h-[calc(100vh-48px)] flex items-center justify-center p-4 relative z-10">
        {countdown !== null && (
          <div className="fixed inset-0 z-[100] bg-black/80 flex items-center justify-center">
            <span className="font-display-lg text-display-lg text-white animate-pulse">
              {countdown}
            </span>
          </div>
        )}

        <div className="w-full max-w-[600px] bg-surface-container/60 backdrop-blur-2xl rounded-[24px] border border-white/10 shadow-[0_30px_60px_rgba(0,0,0,0.5)] p-8 flex flex-col gap-8 relative overflow-hidden">
          <div className="absolute -top-32 -left-32 w-64 h-64 bg-primary/20 rounded-full blur-[80px] pointer-events-none" />

          <div className="text-center relative z-10">
            <h1 className="font-headline-lg text-headline-lg text-on-surface mb-2">Kayda Hazır</h1>
            <p className="text-on-surface-variant font-body-lg text-body-lg">
              Kayıt modunuzu seçin ve cihazları yapılandırın.
            </p>
          </div>

          <div className="grid grid-cols-2 gap-4 relative z-10">
            {modes.map((mode) => (
              <button
                key={mode.id}
                onClick={() => setRecordingMode(mode.id)}
                className={`group relative rounded-2xl p-6 flex flex-col items-center justify-center gap-4 transition-all duration-300 border ${
                  recordingMode === mode.id
                    ? 'bg-primary/10 border-primary/50'
                    : 'bg-white/5 hover:bg-white/10 border-white/5 hover:border-primary/50'
                }`}
              >
                <div className="w-16 h-16 rounded-full bg-surface-container flex items-center justify-center border border-white/10 group-hover:border-primary/50 transition-colors">
                  <span className="material-symbols-outlined text-3xl text-on-surface-variant group-hover:text-primary transition-colors">
                    {mode.icon}
                  </span>
                </div>
                <span className="font-headline-md text-base text-on-surface">{mode.label}</span>
              </button>
            ))}
          </div>

          <div className="bg-surface-container-high rounded-full border border-white/10 p-2 flex items-center justify-between gap-4 w-full relative z-10 shadow-inner">
            <div className="flex-1 flex items-center gap-3 px-4 py-2 rounded-full">
              <span className="material-symbols-outlined text-on-surface-variant text-[20px]">videocam</span>
              <div className="flex flex-col">
                <span className="font-label-sm text-label-sm text-on-surface-variant uppercase tracking-wider">Kamera</span>
                <span className="font-body-md text-[13px] text-on-surface truncate">{cameraLabel}</span>
              </div>
            </div>
            <div className="w-px h-8 bg-white/10" />
            <div className="flex-1 flex items-center gap-3 px-4 py-2 rounded-full">
              <span className="material-symbols-outlined text-on-surface-variant text-[20px]">mic</span>
              <div className="flex flex-col">
                <span className="font-label-sm text-label-sm text-on-surface-variant uppercase tracking-wider">Mikrofon</span>
                <span className="font-body-md text-[13px] text-on-surface truncate">{micLabel}</span>
              </div>
            </div>
            <div className="w-px h-8 bg-white/10" />
            <div className="flex-1 flex items-center gap-3 px-4 py-2 rounded-full">
              <span className="material-symbols-outlined text-on-surface-variant text-[20px]">volume_up</span>
              <div className="flex flex-col">
                <span className="font-label-sm text-label-sm text-on-surface-variant uppercase tracking-wider">Sistem Sesi</span>
                <span className="font-body-md text-[13px] text-on-surface">Açık</span>
              </div>
            </div>
          </div>

          <div className="flex justify-center mt-4 relative z-10">
            <button
              onClick={startRecording}
              disabled={isPreparing}
              className="relative group h-14 px-12 rounded-full font-headline-md text-white bg-gradient-to-r from-red-600 to-orange-500 hover:from-red-500 hover:to-orange-400 border border-white/20 recording-pulse transition-all duration-300 active:scale-95 flex items-center justify-center gap-3 disabled:opacity-50"
            >
              <div className="w-3 h-3 rounded-full bg-white shadow-[0_0_10px_white] animate-pulse" />
              <span>{isPreparing ? 'Hazırlanıyor...' : 'Kaydı Başlat'}</span>
            </button>
          </div>

          {settings.autoZoom !== 'none' && (
            <p className="text-center text-on-surface-variant font-label-md text-label-md relative z-10">
              Otomatik zoom: {settings.autoZoom === 'smooth' ? 'Yumuşak' : 'Anında'}
            </p>
          )}
        </div>
      </main>
    </div>
  );
}
