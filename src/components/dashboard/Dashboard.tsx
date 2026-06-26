import React, { useState } from 'react';
import { useAppStore } from '../../stores/appStore';
import { useRecording } from '../../hooks/useRecording';
import { useMediaDevices } from '../../hooks/useMediaDevices';
import type { RecordingMode } from '../../types';
import WindowPickerModal from '../recording/WindowPickerModal';

const modes: { id: RecordingMode; icon: string; label: string }[] = [
  { id: 'fullscreen', icon: 'fullscreen', label: 'Full Screen' },
  { id: 'custom', icon: 'crop', label: 'Custom' },
  { id: 'window', icon: 'window', label: 'Window' },
];

export default function Dashboard() {
  const {
    recordingMode,
    setRecordingMode,
    countdown,
    settings,
    updateSettings,
    windowPickerOpen,
    setWindowPickerOpen,
    selectedSourceName,
    setSelectedSource,
    preRecordingBarOpen,
    setPreRecordingBarOpen,
  } = useAppStore();
  const { startRecording, isPreparing } = useRecording();
  const { mics, speakers } = useMediaDevices();
  const [openDropdown, setOpenDropdown] = useState<'mic' | 'speaker' | null>(null);

  const handleModeSelect = (modeId: RecordingMode) => {
    setRecordingMode(modeId);
    setPreRecordingBarOpen(true);
  };

  React.useEffect(() => {
    const handleClickOutside = () => setOpenDropdown(null);
    document.addEventListener('click', handleClickOutside);
    return () => document.removeEventListener('click', handleClickOutside);
  }, []);

  if (preRecordingBarOpen) return null;

  return (
    <div 
      className="h-screen w-screen relative flex flex-col bg-[#0f1015] text-white overflow-hidden"
      style={{ WebkitAppRegion: 'drag' } as React.CSSProperties}
    >
      {/* Custom Title Bar for Launcher */}
      <div className="flex items-center justify-between px-4 py-3 h-12 w-full">
        <div className="flex items-center gap-4">
          <div className="flex items-center gap-2">
            <div className="w-6 h-6 bg-gradient-to-tr from-indigo-500 to-purple-500 rounded-full flex items-center justify-center">
              <span className="material-symbols-outlined text-[14px] text-white">videocam</span>
            </div>
            <span className="font-headline-sm text-sm font-semibold tracking-wide">FocuSee</span>
          </div>
          <div className="w-px h-4 bg-white/10" />
          <button className="text-[13px] text-white/70 hover:text-white transition-colors flex items-center gap-1" style={{ WebkitAppRegion: 'no-drag' } as React.CSSProperties}>
            <span className="material-symbols-outlined text-[16px]">video_library</span> Library
          </button>
        </div>
        <div className="flex items-center gap-2 text-white/50" style={{ WebkitAppRegion: 'no-drag' } as React.CSSProperties}>
          <button onClick={() => window.electronAPI.windowMinimize()} className="w-7 h-7 flex items-center justify-center rounded-full hover:bg-white/10 transition-colors">
            <span className="material-symbols-outlined text-[16px]">remove</span>
          </button>
          <button onClick={() => window.electronAPI.windowMaximize()} className="w-7 h-7 flex items-center justify-center rounded-full hover:bg-white/10 transition-colors">
            <span className="material-symbols-outlined text-[14px]">crop_square</span>
          </button>
          <button onClick={() => window.electronAPI.windowClose()} className="w-7 h-7 flex items-center justify-center rounded-full hover:bg-red-500 hover:text-white transition-colors">
            <span className="material-symbols-outlined text-[16px]">close</span>
          </button>
        </div>
      </div>

      {windowPickerOpen && (
        <WindowPickerModal
          onSelect={(id) => {
            setWindowPickerOpen(false);
            setSelectedSource(id, id);
          }}
          onClose={() => setWindowPickerOpen(false)}
        />
      )}

      <main className="flex-1 flex px-8 py-4 gap-8 relative z-10" style={{ WebkitAppRegion: 'no-drag' } as React.CSSProperties}>
        {/* Left Side: Recording Modes */}
        <div className="flex-1 flex flex-col gap-4">
          <h2 className="text-on-surface-variant font-body-lg text-[15px]">Please select the recording mode</h2>
          <div className="grid grid-cols-3 gap-4 h-32">
            {modes.map((mode) => (
              <button
                key={mode.id}
                onClick={() => handleModeSelect(mode.id)}
                className={`group relative rounded-[16px] p-4 flex flex-col items-center justify-center gap-3 transition-all duration-300 border bg-[#1e1f27] hover:bg-[#252630] border-transparent ${
                  recordingMode === mode.id ? 'ring-2 ring-indigo-500 border-transparent' : ''
                }`}
              >
                <div className="w-full flex-1 rounded-xl bg-[#2a2b36] flex items-center justify-center overflow-hidden relative">
                  <span className="material-symbols-outlined text-4xl text-indigo-400 group-hover:scale-110 transition-transform">
                    {mode.icon}
                  </span>
                  {mode.id === 'custom' && (
                    <div className="absolute inset-3 border-2 border-dashed border-indigo-400/50 rounded-lg pointer-events-none" />
                  )}
                </div>
                <span className="font-headline-sm text-[13px] font-medium text-white/90">{mode.label}</span>
              </button>
            ))}
          </div>
        </div>

        {/* Right Side: Device & Tool */}
        <div className="w-[280px] flex flex-col gap-4">
          <h2 className="text-on-surface-variant font-body-lg text-[15px]">Device & Tool</h2>
          <div className="flex flex-col gap-2">
            
            <div className="flex flex-col bg-[#1e1f27] rounded-[16px] p-1.5">
              {/* Microphone */}
              <div className="relative group" onClick={(e) => e.stopPropagation()}>
                <button 
                  onClick={() => setOpenDropdown(openDropdown === 'mic' ? null : 'mic')}
                  className={`w-full flex items-center gap-3 px-3 py-2.5 rounded-xl transition-colors ${settings.microphoneEnabled ? 'bg-indigo-500/10' : 'hover:bg-white/5'}`}
                >
                  <div className={`w-7 h-7 rounded-full flex items-center justify-center ${settings.microphoneEnabled ? 'bg-indigo-500/20 text-indigo-400' : 'bg-white/5 text-white/50'}`}>
                    <span className="material-symbols-outlined text-[16px]">
                      {settings.microphoneEnabled ? 'mic' : 'mic_off'}
                    </span>
                  </div>
                  <span className="flex-1 text-left text-[13px] text-white/90 truncate pr-4">
                    {settings.microphoneEnabled ? (mics[0]?.label || 'Microphone Array...') : 'None'}
                  </span>
                  <span className="material-symbols-outlined text-white/30 text-[18px]">expand_more</span>
                </button>

                {openDropdown === 'mic' && (
                  <div className="absolute top-full left-0 mt-2 w-full bg-[#252530] border border-white/10 rounded-xl shadow-xl overflow-hidden z-50 py-1">
                    <button 
                      onClick={() => { updateSettings({ microphoneEnabled: false }); setOpenDropdown(null); }}
                      className="w-full text-left px-4 py-2 text-[13px] text-white/90 hover:bg-white/10 transition-colors flex items-center gap-2"
                    >
                      <span className="material-symbols-outlined text-[16px]">mic_off</span> None
                    </button>
                    <div className="h-px bg-white/10 my-1" />
                    {mics.map(mic => (
                      <button 
                        key={mic.deviceId}
                        onClick={() => { updateSettings({ microphoneEnabled: true }); setOpenDropdown(null); }}
                        className="w-full text-left px-4 py-2 text-[13px] text-white/90 hover:bg-white/10 transition-colors truncate"
                      >
                        {mic.label || 'Microphone'}
                      </button>
                    ))}
                  </div>
                )}
              </div>

              <div className="h-px w-full bg-white/5 my-0.5" />

              {/* Speaker */}
              <div className="relative group" onClick={(e) => e.stopPropagation()}>
                <button 
                  onClick={() => setOpenDropdown(openDropdown === 'speaker' ? null : 'speaker')}
                  className={`w-full flex items-center gap-3 px-3 py-2.5 rounded-xl transition-colors ${settings.systemAudioEnabled ? 'bg-indigo-500/10' : 'hover:bg-white/5'}`}
                >
                  <div className={`w-7 h-7 rounded-full flex items-center justify-center ${settings.systemAudioEnabled ? 'bg-indigo-500/20 text-indigo-400' : 'bg-white/5 text-white/50'}`}>
                    <span className="material-symbols-outlined text-[16px]">
                      {settings.systemAudioEnabled ? 'volume_up' : 'volume_off'}
                    </span>
                  </div>
                  <span className="flex-1 text-left text-[13px] text-white/90 truncate pr-4">
                    {settings.systemAudioEnabled ? (speakers[0]?.label || 'Speaker (Realtek...') : 'None'}
                  </span>
                  <span className="material-symbols-outlined text-white/30 text-[18px]">expand_more</span>
                </button>

                {openDropdown === 'speaker' && (
                  <div className="absolute top-full left-0 mt-2 w-full bg-[#252530] border border-white/10 rounded-xl shadow-xl overflow-hidden z-50 py-1">
                    <button 
                      onClick={() => { updateSettings({ systemAudioEnabled: false }); setOpenDropdown(null); }}
                      className="w-full text-left px-4 py-2 text-[13px] text-white/90 hover:bg-white/10 transition-colors flex items-center gap-2"
                    >
                      <span className="material-symbols-outlined text-[16px]">volume_off</span> None
                    </button>
                    <div className="h-px bg-white/10 my-1" />
                    {speakers.map(speaker => (
                      <button 
                        key={speaker.deviceId}
                        onClick={() => { updateSettings({ systemAudioEnabled: true }); setOpenDropdown(null); }}
                        className="w-full text-left px-4 py-2 text-[13px] text-white/90 hover:bg-white/10 transition-colors truncate"
                      >
                        {speaker.label || 'Speaker'}
                      </button>
                    ))}
                  </div>
                )}
              </div>
            </div>

            {/* Teleprompter Button */}
            <button className="flex items-center justify-center gap-2 bg-[#1e1f27] hover:bg-[#252630] rounded-[16px] py-3 transition-colors mt-1">
              <span className="material-symbols-outlined text-[18px] text-white/70">picture_in_picture</span>
              <span className="text-[13px] font-medium text-white/90">Teleprompter</span>
            </button>
          </div>
        </div>
      </main>
    </div>
  );
}
