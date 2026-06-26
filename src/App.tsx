import { useEffect } from 'react';
import { useAppStore } from './stores/appStore';
import Dashboard from './components/dashboard/Dashboard';
import EditorWorkspace from './components/editor/EditorWorkspace';
import ExportScreen from './components/export/ExportScreen';
import SettingsModal from './components/settings/SettingsModal';
import RecordingBar from './components/recording/RecordingBar';

export default function App() {
  const { screen, updateSettings } = useAppStore();

  useEffect(() => {
    const init = async () => {
      const settings = await window.electronAPI.getSettings();
      const paths = await window.electronAPI.getDefaultPaths();
      updateSettings({
        ...settings,
        projectLocation: settings.projectLocation || paths.projects,
        exportLocation: settings.exportLocation || paths.exports,
      });
    };
    init();
  }, [updateSettings]);

  return (
    <div className="dark h-screen w-screen overflow-hidden">
      {screen === 'dashboard' && <Dashboard />}
      {screen === 'editor' && <EditorWorkspace />}
      {screen === 'export' && <ExportScreen />}
      <RecordingBar />
      <SettingsModal />
    </div>
  );
}
