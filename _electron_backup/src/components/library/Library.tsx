import { useEffect, useState } from 'react';
import { useAppStore } from '../../stores/appStore';
import { useEditorStore } from '../../stores/editorStore';
import { formatTimecode } from '../../lib/zoomEngine';
import type { ProjectSummary } from '../../types';
import TopNav from '../layout/TopNav';

export default function Library() {
  const { setScreen, setCurrentProject } = useAppStore();
  const loadEditor = useEditorStore((s) => s.loadFromProject);
  const [projects, setProjects] = useState<ProjectSummary[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    window.electronAPI.listProjects().then((list) => {
      setProjects(list);
      setLoading(false);
    });
  }, []);

  const openProject = async (projectPath: string) => {
    try {
      const manifest = await window.electronAPI.loadProject(projectPath);
      const metadata = await window.electronAPI.loadProjectMetadata(projectPath);
      setCurrentProject(manifest, projectPath);
      loadEditor(manifest.timeline.zoomEffects, manifest.timeline.settings, metadata.duration);
      setScreen('editor');
    } catch {
      alert('Proje açılamadı');
    }
  };

  return (
    <div className="h-screen flex flex-col bg-[#0f1015] overflow-hidden">
      <TopNav title="Kütüphane" onBack={() => setScreen('dashboard')} />

      <main className="flex-1 mt-[48px] overflow-y-auto p-4 md:p-6 scrollbar-thin scrollbar-thumb-white/10">
        <div className="max-w-6xl mx-auto pb-16">
          <div className="mb-4">
            <h1 className="text-2xl font-semibold text-white mb-1">Kütüphane</h1>
            <p className="text-white/50 text-[13px]">
              Kayıtlı projelerinizi görüntüleyin ve düzenleyin.
            </p>
          </div>

          {loading && (
            <p className="text-on-surface-variant text-center py-16">Projeler yükleniyor...</p>
          )}

          {!loading && projects.length === 0 && (
            <div className="text-center py-16 glass-panel rounded-xl">
              <span className="material-symbols-outlined text-5xl text-on-surface-variant mb-4">video_library</span>
              <p className="text-on-surface-variant font-body-lg mb-4">Henüz kayıt yok</p>
              <button
                onClick={() => setScreen('dashboard')}
                className="px-6 py-2 rounded-lg bg-primary text-on-primary font-body-md"
              >
                İlk Kaydını Başlat
              </button>
            </div>
          )}

          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
            {projects.map((project) => (
              <div
                key={project.path}
                className="group relative glass-panel rounded-xl overflow-hidden text-left hover:border-primary/30 transition-all hover:shadow-[0_0_20px_rgba(192,193,255,0.1)]"
              >
                <div className="aspect-video bg-surface-container-high flex items-center justify-center border-b border-white/5 overflow-hidden">
                  {project.videoUrl ? (
                    <video src={project.videoUrl} className="w-full h-full object-cover pointer-events-none" preload="metadata" muted playsInline />
                  ) : (
                    <span className="material-symbols-outlined text-4xl text-on-surface-variant group-hover:text-primary transition-colors">
                      {project.hasVideo ? 'movie' : 'videocam_off'}
                    </span>
                  )}
                </div>
                <div className="p-4 flex flex-col gap-1">
                  <span className="font-headline-md text-base text-on-surface truncate group-hover:text-primary transition-colors">
                    {project.name}
                  </span>
                  <div className="flex items-center gap-3 text-on-surface-variant font-label-sm text-label-sm">
                    <span>{formatTimecode(project.duration)}</span>
                    <span>•</span>
                    <span className="capitalize">{project.mode}</span>
                  </div>
                  {project.recordedAt && (
                    <span className="text-on-surface-variant font-label-sm text-label-sm">
                      {new Date(project.recordedAt).toLocaleDateString('tr-TR', {
                        day: 'numeric',
                        month: 'long',
                        year: 'numeric',
                      })}
                    </span>
                  )}
                </div>
                
                {/* Hover overlay */}
                <div className="absolute inset-0 bg-black/60 opacity-0 group-hover:opacity-100 transition-opacity duration-200 flex flex-col items-center justify-center gap-3 backdrop-blur-sm z-10 pointer-events-none group-hover:pointer-events-auto">
                  <button
                    onClick={() => openProject(project.path)}
                    className="bg-indigo-500 text-white px-6 py-2 rounded-lg font-body-md hover:bg-indigo-600 shadow-lg flex items-center gap-2 transform translate-y-4 group-hover:translate-y-0 transition-transform duration-300"
                  >
                    <span className="material-symbols-outlined text-[18px]">edit</span>
                    Düzenle
                  </button>
                  <button
                    onClick={(e) => {
                      e.stopPropagation();
                      window.electronAPI.revealInFolder(project.path);
                    }}
                    className="bg-white/10 text-white border border-white/20 px-6 py-2 rounded-lg font-body-md hover:bg-white/20 shadow-lg flex items-center gap-2 transform translate-y-4 group-hover:translate-y-0 transition-transform duration-300 delay-75"
                  >
                    <span className="material-symbols-outlined text-[18px]">folder_open</span>
                    Dosya Konumuna Git
                  </button>
                </div>
              </div>
            ))}
          </div>
        </div>
      </main>
    </div>
  );
}
