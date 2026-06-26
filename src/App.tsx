import { useEffect } from 'react';
import { useAppStore } from './stores/appStore';
import Dashboard from './components/dashboard/Dashboard';
import Library from './components/library/Library';
import EditorWorkspace from './components/editor/EditorWorkspace';
import ExportScreen from './components/export/ExportScreen';
import SettingsModal from './components/settings/SettingsModal';
import RecordingBar from './components/recording/RecordingBar';
import CameraOverlay from './components/recording/CameraOverlay';

const isRecordingBarOnly = window.location.hash === '#/recording-bar';
const isCameraOverlay = window.location.hash === '#/camera-overlay';

export default function App() {
  const { screen, updateSettings } = useAppStore();

  useEffect(() => {
    if (isRecordingBarOnly) {
      document.documentElement.classList.add('recording-overlay');
      document.body.classList.add('recording-overlay');
      return () => {
        document.documentElement.classList.remove('recording-overlay');
        document.body.classList.remove('recording-overlay');
      };
    }
    const init = async () => {
      const settings = await window.electronAPI.getSettings();
      const paths = await window.electronAPI.getDefaultPaths();
      updateSettings({
        ...settings,
        projectLocation: settings.projectLocation || paths.projects,
        exportLocation: settings.exportLocation || paths.exports,
        microphoneEnabled: settings.microphoneEnabled ?? true,
        systemAudioEnabled: settings.systemAudioEnabled ?? true,
      });
    };
    init();
  }, [updateSettings, isRecordingBarOnly]);

  if (isRecordingBarOnly) {
    return (
      <div className="dark h-screen w-screen overflow-hidden bg-transparent">
        <RecordingBar />
      </div>
    );
  }

  if (isCameraOverlay) {
    return (
      <div className="dark h-screen w-screen overflow-hidden bg-transparent">
        <CameraOverlay />
      </div>
    );
  }

  return (
    <div className="dark h-screen w-screen overflow-hidden">
      {screen === 'dashboard' && <Dashboard />}
      {screen === 'library' && <Library />}
      {screen === 'editor' && <EditorWorkspace />}
      {screen === 'export' && <ExportScreen />}
      <RecordingBar />
      <SettingsModal />
    </div>
  );
}
