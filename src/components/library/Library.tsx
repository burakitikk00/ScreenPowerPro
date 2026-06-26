import { useEffect, useState } from 'react';
import { useAppStore } from '../../stores/appStore';
import { useEditorStore } from '../../stores/editorStore';
import { formatTimecode } from '../../lib/zoomEngine';
import type { ProjectSummary } from '../../types';
import TopNav from '../layout/TopNav';
import SideNav from '../layout/SideNav';

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
    <div className="min-h-screen flex flex-col">
      <TopNav title="Kütüphane" />
      <SideNav active="library" />

      <main className="flex-1 ml-0 md:ml-sidebar-width mt-toolbar-height h-[calc(100vh-48px)] overflow-y-auto p-container-margin">
        <div className="max-w-6xl mx-auto">
          <div className="mb-8">
            <h1 className="font-headline-lg text-headline-lg text-on-surface mb-2">Kütüphane</h1>
            <p className="text-on-surface-variant font-body-lg">
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
              <button
                key={project.path}
                onClick={() => openProject(project.path)}
                className="group glass-panel rounded-xl overflow-hidden text-left hover:border-primary/30 transition-all hover:shadow-[0_0_20px_rgba(192,193,255,0.1)]"
              >
                <div className="aspect-video bg-surface-container-high flex items-center justify-center border-b border-white/5">
                  <span className="material-symbols-outlined text-4xl text-on-surface-variant group-hover:text-primary transition-colors">
                    {project.hasVideo ? 'movie' : 'videocam_off'}
                  </span>
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
              </button>
            ))}
          </div>
        </div>
      </main>
    </div>
  );
}
