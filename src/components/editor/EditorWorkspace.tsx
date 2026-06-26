import TopNav from '../layout/TopNav';
import SideNav from '../layout/SideNav';
import VideoPreview from './VideoPreview';
import PropertiesPanel from './PropertiesPanel';
import Timeline from './Timeline';
import { useAppStore } from '../../stores/appStore';

export default function EditorWorkspace() {
  const { currentProject, setScreen } = useAppStore();

  const handleExport = () => setScreen('export');

  return (
    <div className="h-screen w-screen overflow-hidden flex flex-col">
      <TopNav
        title={currentProject?.name || 'Kayıt'}
        showExport
        onExport={handleExport}
      />
      <SideNav active="editor" />
      <main className="flex-1 flex flex-col md:ml-sidebar-width mt-toolbar-height h-[calc(100vh-48px)] relative bg-surface-container-lowest">
        <div className="flex-1 flex overflow-hidden">
          <VideoPreview />
          <PropertiesPanel />
        </div>
        <Timeline />
      </main>
    </div>
  );
}
