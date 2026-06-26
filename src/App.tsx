import { useEffect } from 'react';
import { useAppStore } from './stores/appStore';
import Dashboard from './components/dashboard/Dashboard';
import Library from './components/library/Library';
import EditorWorkspace from './components/editor/EditorWorkspace';
import ExportScreen from './components/export/ExportScreen';
import SettingsModal from './components/settings/SettingsModal';
import RecordingBar from './components/recording/RecordingBar';
import CameraOverlay from './components/recording/CameraOverlay';

import CropperOverlay from './components/recording/CropperOverlay';
import MaskOverlay from './components/recording/MaskOverlay';
import CountdownOverlay from './components/recording/CountdownOverlay';

const isRecordingBarOnly = window.location.hash === '#/recording-bar';
const isCameraOverlay = window.location.hash === '#/camera-overlay';
const isCropperOverlay = window.location.hash === '#/cropper-overlay';
const isMaskOverlay = window.location.hash.startsWith('#/mask-overlay');
const isCountdownOverlay = window.location.hash.startsWith('#/countdown-overlay');

export default function App() {
  const { screen, updateSettings, preRecordingBarOpen } = useAppStore();

  useEffect(() => {
    if (isRecordingBarOnly || isCropperOverlay || isMaskOverlay || (screen === 'dashboard' && preRecordingBarOpen)) {
      document.documentElement.classList.add('recording-overlay');
      document.body.classList.add('recording-overlay');
      document.body.style.backgroundColor = 'transparent';
      return () => {
        document.documentElement.classList.remove('recording-overlay');
        document.body.classList.remove('recording-overlay');
        document.body.style.backgroundColor = '';
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
  }, [updateSettings, isRecordingBarOnly, isCropperOverlay, preRecordingBarOpen, screen]);

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

  if (isCropperOverlay) {
    return (
      <div className="dark h-screen w-screen overflow-hidden bg-transparent">
        <CropperOverlay />
      </div>
    );
  }

  if (isMaskOverlay) {
    return <MaskOverlay />;
  }

  if (isCountdownOverlay) {
    return <CountdownOverlay />;
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
