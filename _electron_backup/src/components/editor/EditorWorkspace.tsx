import EditorTopBar from './EditorTopBar';
import EditorSidebar from './EditorSidebar';
import PropertiesPanel from './PropertiesPanel';
import VideoPreview from './VideoPreview';
import Timeline from './Timeline';

export default function EditorWorkspace() {
  return (
    <div className="h-screen w-screen overflow-hidden flex flex-col bg-[#111116] text-white">
      <EditorTopBar />
      <div className="flex-1 flex overflow-hidden">
        {/* Left Sidebar and Properties */}
        <EditorSidebar />
        <PropertiesPanel />
        
        {/* Center Canvas */}
        <div className="flex-1 flex flex-col relative bg-black/20">
          <div className="flex-1 overflow-hidden p-8 flex items-center justify-center">
            <VideoPreview />
          </div>
        </div>
      </div>
      {/* Bottom Timeline */}
      <Timeline />
    </div>
  );
}
