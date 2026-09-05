import { useEffect, useState } from 'react';
import { useAppStore } from '../../stores/appStore';
import { useEditorStore } from '../../stores/editorStore';

export default function ExportScreen() {
  const {
    currentProject,
    currentProjectPath,
    exportProgress,
    setExportProgress,
    setScreen,
    settings,
  } = useAppStore();
  const { zoomEffects, settings: timelineSettings } = useEditorStore();
  const [outputPath, setOutputPath] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!currentProject || !currentProjectPath) return;

    const runExport = async () => {
      setExportProgress({ percent: 0, status: 'Başlatılıyor...' });

      const unsubscribe = window.electronAPI.onExportProgress((p) => {
        setExportProgress(p);
      });

      try {
        const metadata = await window.electronAPI.loadProjectMetadata(currentProjectPath);

        const manifest = {
          ...currentProject,
          timeline: {
            zoomEffects,
            settings: timelineSettings,
          },
        };

        const path = await window.electronAPI.startExport(
          currentProjectPath,
          manifest,
          metadata
        );
        setOutputPath(path);
      } catch (err) {
        setError((err as Error).message || 'Dışa aktarma başarısız');
      } finally {
        unsubscribe();
      }
    };

    runExport();
  }, []);

  const percent = exportProgress?.percent ?? 0;
  const circumference = 2 * Math.PI * 120;
  const offset = circumference * (1 - percent / 100);

  const handleCancel = async () => {
    await window.electronAPI.cancelExport();
    setScreen('editor');
  };

  const handleDone = () => {
    if (outputPath) window.electronAPI.revealInFolder(outputPath);
    setScreen('dashboard');
    setExportProgress(null);
  };

  if (error) {
    return (
      <div className="min-h-screen flex flex-col items-center justify-center gap-6">
        <span className="material-symbols-outlined text-error text-5xl">error</span>
        <h1 className="font-headline-lg text-headline-lg text-on-surface">Dışa Aktarma Hatası</h1>
        <p className="text-on-surface-variant">{error}</p>
        <button
          onClick={() => setScreen('editor')}
          className="px-6 py-2 rounded-lg bg-primary text-on-primary font-body-md"
        >
          Editöre Dön
        </button>
      </div>
    );
  }

  if (outputPath && percent >= 100) {
    return (
      <div className="min-h-screen flex flex-col items-center justify-center gap-6">
        <span className="material-symbols-outlined text-primary text-5xl fill">check_circle</span>
        <h1 className="font-headline-lg text-headline-lg text-on-surface">Dışa Aktarma Tamamlandı!</h1>
        <p className="text-on-surface-variant font-body-lg">Videonuz başarıyla oluşturuldu.</p>
        <div className="flex gap-4">
          <button
            onClick={handleDone}
            className="px-6 py-2 rounded-lg bg-primary text-on-primary font-body-md shadow-[0_0_15px_rgba(192,193,255,0.3)]"
          >
            Dosyayı Göster
          </button>
          <button
            onClick={() => {
              setScreen('dashboard');
              setExportProgress(null);
            }}
            className="px-6 py-2 rounded-lg border border-white/10 text-on-surface font-body-md hover:bg-white/5"
          >
            Ana Sayfa
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="bg-background text-on-surface min-h-screen flex flex-col justify-center items-center overflow-hidden">
      <div className="fixed inset-0 z-0 pointer-events-none">
        <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[800px] h-[800px] bg-primary/5 rounded-full blur-[120px]" />
      </div>

      <main className="relative z-10 flex flex-col items-center justify-center w-full max-w-2xl px-container-margin text-center">
        <div className="relative w-64 h-64 mb-12 flex items-center justify-center rounded-full bg-surface-container-lowest border border-white/5 shadow-2xl shadow-black/50"
          style={{ boxShadow: '0 0 40px rgba(192, 193, 255, 0.15)' }}
        >
          <svg className="absolute inset-0 w-full h-full" viewBox="0 0 256 256">
            <circle
              className="text-surface-container-high stroke-current"
              cx="128"
              cy="128"
              fill="transparent"
              r="120"
              strokeWidth="8"
            />
            <circle
              className="progress-ring__circle text-primary stroke-current"
              cx="128"
              cy="128"
              fill="transparent"
              r="120"
              strokeWidth="8"
              strokeDasharray={circumference}
              strokeDashoffset={offset}
              strokeLinecap="round"
            />
          </svg>
          <span className="font-display-lg text-display-lg text-on-surface">{percent}%</span>
        </div>

        <div className="flex flex-col gap-2 items-center mb-16">
          <h1 className="font-headline-lg text-headline-lg text-on-surface">Video dışa aktarılıyor...</h1>
          <p className="font-body-lg text-body-lg text-on-surface-variant max-w-md">
            {exportProgress?.status || 'Lütfen uygulamayı kapatmayın.'}
          </p>
        </div>
      </main>

      <footer className="fixed bottom-12 w-full flex justify-center z-10">
        <button
          onClick={handleCancel}
          className="font-label-md text-label-md text-on-surface-variant hover:text-error transition-colors px-6 py-3 rounded-full border border-white/10 hover:border-error/50 bg-white/5 hover:bg-error/10 backdrop-blur-md flex items-center gap-2"
        >
          <span className="material-symbols-outlined text-[18px]">cancel</span>
          İptal Et
        </button>
      </footer>
    </div>
  );
}
