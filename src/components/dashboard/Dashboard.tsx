import React, { useState, useRef, useEffect, useCallback } from 'react';
import { useAppStore } from '../../stores/appStore';
import { useRecording } from '../../hooks/useRecording';
import { useMediaDevices } from '../../hooks/useMediaDevices';
import { useAudioLevel } from '../../hooks/useAudioLevel';
import { useSystemAudioLevel } from '../../hooks/useSystemAudioLevel';
import type { RecordingMode } from '../../types';
import WindowPickerModal from '../recording/WindowPickerModal';
import type { ScreenSource } from '../../../shared/types';
import ReactDOM from 'react-dom';

const modes: { id: RecordingMode; icon: string; label: string }[] = [
  { id: 'fullscreen', icon: 'fullscreen', label: 'Full Screen' },
  { id: 'custom', icon: 'crop', label: 'Custom' },
  { id: 'window', icon: 'window', label: 'Window' },
];

export default function Dashboard() {
  const {
    recordingMode,
    setRecordingMode,
    settings,
    updateSettings,
    windowPickerOpen,
    setWindowPickerOpen,
    selectedSourceName,
    setSelectedSource,
    preRecordingBarOpen,
    setPreRecordingBarOpen,
    setScreen,
  } = useAppStore();
  const { startRecording, isPreparing } = useRecording();
  const { mics, speakers, cameras } = useMediaDevices();
  const [openDropdown, setOpenDropdown] = useState<'mic' | 'speaker' | 'camera' | null>(null);
  const [openSubMenu, setOpenSubMenu] = useState<string | null>(null);
  const [windowSources, setWindowSources] = useState<ScreenSource[]>([]);
  const [dropdownPos, setDropdownPos] = useState<{ top?: number; bottom?: number; right: number; left?: number; isUp: boolean; maxHeight?: number }>({ right: 0, isUp: false });
  const [subMenuPos, setSubMenuPos] = useState<{ top?: number; bottom?: number; right: number; maxHeight?: number }>({ right: 0 });
  const micBtnRef = useRef<HTMLButtonElement>(null);
  const speakerBtnRef = useRef<HTMLButtonElement>(null);
  const cameraBtnRef = useRef<HTMLButtonElement>(null);
  const appAudioRef = useRef<HTMLDivElement>(null);

  const openDropdownAt = useCallback((type: 'mic' | 'speaker' | 'camera', ref: React.RefObject<HTMLButtonElement | null>) => {
    if (openDropdown === type) { setOpenDropdown(null); return; }
    const rect = ref.current?.getBoundingClientRect();
    if (rect) {
      const spaceBelow = window.innerHeight - rect.bottom;
      const spaceAbove = rect.top - 16; // 16px safe margin to allow dropdowns to stretch further up
      
      let isUp = spaceBelow < 180 && spaceAbove > spaceBelow;
      if (type === 'speaker') isUp = true;

      const right = window.innerWidth - rect.right;
      
      let maxHeight = 192;
      if (isUp) {
        maxHeight = Math.max(150, spaceAbove);
        const bottom = window.innerHeight - rect.top + 4;
        setDropdownPos({ bottom, right, isUp: true, maxHeight });
      } else {
        maxHeight = Math.max(150, spaceBelow - 8);
        setDropdownPos({ top: rect.bottom + 4, right, isUp: false, maxHeight });
      }
    }
    setOpenDropdown(type);
  }, [openDropdown]);

  const openSubMenuAt = (ref: React.RefObject<HTMLDivElement | null>) => {
    const rect = ref.current?.getBoundingClientRect();
    if (rect) {
      const spaceBelow = window.innerHeight - rect.top - 8;
      setSubMenuPos({ top: rect.top, right: window.innerWidth - rect.left + 4, maxHeight: Math.max(100, spaceBelow) });
    }
    setOpenSubMenu('appAudio');
  };

  const selectedMicId = settings.selectedMicId || mics[0]?.deviceId;
  const micVolume = useAudioLevel(selectedMicId, settings.microphoneEnabled);
  const sysVolume = useSystemAudioLevel(settings.systemAudioEnabled, settings.selectedSpeakerId);
  
  // Calculate dynamic scale based on volume (1 to 1.3)
  const micScale = 1 + micVolume * 0.3;
  const sysScale = 1 + sysVolume * 0.3;

  const handleModeSelect = (modeId: RecordingMode) => {
    setRecordingMode(modeId);
    if (modeId === 'custom') {
      window.electronAPI.windowMinimize();
      window.electronAPI.openCropper();
      const unsub = window.electronAPI.onCropperAreaSelected((bounds) => {
        updateSettings({ customCropBounds: bounds });
        window.electronAPI.restoreAndResizeForPrerecording();
        setPreRecordingBarOpen(true);
        unsub();
      });
    } else {
      window.electronAPI.resizeForPrerecording();
      setPreRecordingBarOpen(true);
    }
  };

  React.useEffect(() => {
    const handleClickOutside = () => { setOpenDropdown(null); setOpenSubMenu(null); };
    document.addEventListener('click', handleClickOutside);
    window.electronAPI.getScreenSources().then(s => setWindowSources(s.filter(x => x.id.startsWith('window:'))));
    return () => document.removeEventListener('click', handleClickOutside);
  }, []);

  if (preRecordingBarOpen) return null;

  return (
    <div 
      className="min-h-screen w-screen relative flex flex-col bg-[#0f1015] text-white"
      style={{ WebkitAppRegion: 'drag' } as React.CSSProperties}
    >
      {/* Custom Title Bar for Launcher */}
      <div className="flex items-center justify-between px-4 py-3 h-12 w-full">
        <div className="flex items-center gap-4">
          <div className="flex items-center gap-2">
            <div className="w-6 h-6 bg-gradient-to-tr from-indigo-500 to-purple-500 rounded-full flex items-center justify-center">
              <span className="material-symbols-outlined text-[14px] text-white">videocam</span>
            </div>
            <span className="font-headline-sm text-sm font-semibold tracking-wide">ScreenPowerPro</span>
          </div>
          <div className="w-px h-4 bg-white/10" />
          <button onClick={() => setScreen('library')} className="text-[13px] text-white/70 hover:text-white transition-colors flex items-center gap-1" style={{ WebkitAppRegion: 'no-drag' } as React.CSSProperties}>
            <span className="material-symbols-outlined text-[16px]">video_library</span> Library
          </button>
        </div>
        <div className="flex items-center gap-1 text-white/40" style={{ WebkitAppRegion: 'no-drag' } as React.CSSProperties}>
          <button onClick={() => window.electronAPI.windowMinimize()} className="w-6 h-6 flex items-center justify-center rounded hover:bg-white/10 transition-colors">
            <span className="material-symbols-outlined text-[14px]">remove</span>
          </button>
          <button onClick={() => window.electronAPI.windowClose()} className="w-6 h-6 flex items-center justify-center rounded hover:bg-white/10 hover:text-red-400 transition-colors">
            <span className="material-symbols-outlined text-[14px]">close</span>
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
                className={`group relative rounded-[16px] p-4 flex flex-col items-center justify-center gap-3 transition-all duration-300 border bg-[#1e1f27] hover:bg-[#252630] ${
                  recordingMode === mode.id 
                    ? 'ring-2 ring-indigo-500 border-transparent' 
                    : 'border-transparent hover:ring-2 hover:ring-white/20'
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
        <div className="w-[220px] flex flex-col gap-4">
          <h2 className="text-on-surface-variant font-body-lg text-[15px]">Device & Tool</h2>
          <div className="flex flex-col gap-2">
            
            <div className="flex flex-col bg-[#1e1f27] rounded-[16px] p-1.5">
              {/* Microphone */}
              <div className="relative group" onClick={(e) => e.stopPropagation()}>
                <button 
                  ref={micBtnRef}
                  onClick={() => openDropdownAt('mic', micBtnRef)}
                  className={`w-full flex items-center gap-3 px-3 py-2.5 rounded-xl transition-colors ${settings.microphoneEnabled ? 'bg-indigo-500/10' : 'hover:bg-white/5'}`}
                >
                  <div 
                    className={`w-7 h-7 rounded-full flex items-center justify-center transition-transform duration-75 ${settings.microphoneEnabled ? 'bg-indigo-500/20 text-indigo-400' : 'bg-white/5 text-white/50'}`}
                    style={{ transform: settings.microphoneEnabled ? `scale(${micScale})` : 'scale(1)' }}
                  >
                    <span className="material-symbols-outlined text-[16px]">
                      {settings.microphoneEnabled ? 'mic' : 'mic_off'}
                    </span>
                  </div>
                  <span className="flex-1 text-left text-[12px] text-white/90 truncate pr-4">
                    {settings.microphoneEnabled ? (mics.find(m => m.deviceId === settings.selectedMicId)?.label || mics[0]?.label || 'Microphone') : 'None'}
                  </span>
                  <span className="material-symbols-outlined text-white/30 text-[18px]">expand_more</span>
                </button>

                {openDropdown === 'mic' && ReactDOM.createPortal(
                  <div 
                    className="fixed w-max min-w-[200px] overflow-y-auto scrollbar-thin scrollbar-thumb-white/10 bg-[#1e1f2e] border border-white/10 rounded-xl shadow-2xl z-[9999] py-1 whitespace-nowrap"
                    style={{ ...(dropdownPos.isUp ? { bottom: dropdownPos.bottom } : { top: dropdownPos.top }), right: dropdownPos.right, maxHeight: dropdownPos.maxHeight || 192, WebkitAppRegion: 'no-drag' } as unknown as React.CSSProperties}
                    onClick={(e) => e.stopPropagation()}
                  >
                    <button 
                      onClick={() => { updateSettings({ microphoneEnabled: false }); setOpenDropdown(null); }}
                      className="w-full text-left px-4 py-2 text-[12px] text-white/90 hover:bg-white/10 transition-colors flex items-center gap-2"
                    >
                      <span className="material-symbols-outlined text-[14px]">
                        {!settings.microphoneEnabled ? 'check' : ''}
                      </span>
                      <span className="material-symbols-outlined text-[16px]">mic_off</span> None
                    </button>
                    <div className="h-px bg-white/10 my-1" />
                    {mics.map(mic => (
                      <button 
                        key={mic.deviceId}
                        onClick={() => { updateSettings({ microphoneEnabled: true, selectedMicId: mic.deviceId }); setOpenDropdown(null); }}
                        className="w-full text-left px-4 py-2 text-[12px] text-white/90 hover:bg-white/10 transition-colors flex items-center gap-2"
                      >
                        <span className="material-symbols-outlined text-[14px]">
                          {settings.microphoneEnabled && settings.selectedMicId === mic.deviceId ? 'check' : ''}
                        </span>
                        {mic.label || 'Microphone'}
                      </button>
                    ))}
                  </div>,
                  document.body
                )}
              </div>

              <div className="h-px w-full bg-white/5 my-0.5" />

              {/* Speaker */}
              <div className="relative group" onClick={(e) => e.stopPropagation()}>
                <button 
                  ref={speakerBtnRef}
                  onClick={() => {
                    openDropdownAt('speaker', speakerBtnRef);
                    if (openDropdown !== 'speaker') {
                      window.electronAPI.getScreenSources().then(s => setWindowSources(s.filter(x => x.id.startsWith('window:'))));
                    }
                  }}
                  className={`w-full flex items-center gap-3 px-3 py-2.5 rounded-xl transition-colors ${settings.systemAudioEnabled ? 'bg-indigo-500/10' : 'hover:bg-white/5'}`}
                >
                  <div 
                    className={`w-7 h-7 rounded-full flex items-center justify-center transition-transform duration-75 ${settings.systemAudioEnabled ? 'bg-indigo-500/20 text-indigo-400' : 'bg-white/5 text-white/50'}`}
                    style={{ transform: settings.systemAudioEnabled ? `scale(${sysScale})` : 'scale(1)' }}
                  >
                    <span className="material-symbols-outlined text-[16px]">
                      {settings.systemAudioEnabled ? 'volume_up' : 'volume_off'}
                    </span>
                  </div>
                  <span className="flex-1 text-left text-[12px] text-white/90 truncate pr-4">
                    {settings.systemAudioEnabled ? (settings.selectedSpeakerId?.startsWith('window:') ? windowSources.find(w => w.id === settings.selectedSpeakerId)?.name : speakers.find(s => s.deviceId === settings.selectedSpeakerId)?.label) || speakers[0]?.label || 'Speaker' : 'None'}
                  </span>
                  <span className="material-symbols-outlined text-white/30 text-[18px]">expand_more</span>
                </button>

                {openDropdown === 'speaker' && ReactDOM.createPortal(
                  <div 
                    className="fixed w-max min-w-[200px] overflow-y-auto scrollbar-thin scrollbar-thumb-white/10 bg-[#1e1f2e] border border-white/10 rounded-xl shadow-2xl z-[9999] py-1 whitespace-nowrap"
                    style={{ ...(dropdownPos.isUp ? { bottom: dropdownPos.bottom } : { top: dropdownPos.top }), right: dropdownPos.right, maxHeight: dropdownPos.maxHeight || 192, WebkitAppRegion: 'no-drag' } as unknown as React.CSSProperties}
                    onClick={(e) => e.stopPropagation()}
                  >
                    {speakers.map(speaker => (
                      <button 
                        key={speaker.deviceId}
                        onClick={() => { updateSettings({ systemAudioEnabled: true, selectedSpeakerId: speaker.deviceId }); setOpenDropdown(null); }}
                        className="w-full text-left px-4 py-2 text-[12px] text-white/90 hover:bg-white/10 transition-colors flex items-center gap-2"
                      >
                        <span className="material-symbols-outlined text-[14px]">
                          {settings.systemAudioEnabled && settings.selectedSpeakerId === speaker.deviceId ? 'check' : ''}
                        </span>
                        {speaker.label || 'Speaker'}
                      </button>
                    ))}
                    
                    <div 
                      ref={appAudioRef}
                      className="relative"
                      onMouseEnter={() => openSubMenuAt(appAudioRef)}
                      onMouseLeave={() => setOpenSubMenu(null)}
                    >
                      <button className="w-full text-left px-4 py-2 text-[12px] text-white/90 hover:bg-white/10 transition-colors flex items-center justify-between gap-4">
                        <div className="flex items-center gap-2">
                          <span className="material-symbols-outlined text-[14px]">
                            {settings.systemAudioEnabled && settings.selectedSpeakerId?.startsWith('window:') ? 'check' : ''}
                          </span>
                          <span>Only App Audio ({windowSources.length})</span>
                        </div>
                        <span className="material-symbols-outlined text-[14px] text-white/50">chevron_right</span>
                      </button>

                      {openSubMenu === 'appAudio' && windowSources.length > 0 && ReactDOM.createPortal(
                        <div 
                          className="fixed w-max min-w-[150px] overflow-y-auto scrollbar-thin scrollbar-thumb-white/10 bg-[#1e1f2e] border border-white/10 rounded-xl shadow-2xl z-[9999] py-1 whitespace-nowrap"
                          style={{ top: subMenuPos.top, right: subMenuPos.right, maxHeight: subMenuPos.maxHeight || 192, WebkitAppRegion: 'no-drag' } as React.CSSProperties}
                          onMouseEnter={() => setOpenSubMenu('appAudio')}
                          onMouseLeave={() => setOpenSubMenu(null)}
                          onClick={(e) => e.stopPropagation()}
                        >
                          {windowSources.map(src => (
                            <button 
                              key={src.id}
                              onClick={() => { updateSettings({ systemAudioEnabled: true, selectedSpeakerId: src.id }); setOpenDropdown(null); setOpenSubMenu(null); }}
                              className="w-full text-left px-4 py-2 text-[12px] text-white/90 hover:bg-white/10 transition-colors flex items-center gap-2"
                            >
                              <span className="material-symbols-outlined text-[14px]">
                                {settings.systemAudioEnabled && settings.selectedSpeakerId === src.id ? 'check' : ''}
                              </span>
                              {src.name}
                            </button>
                          ))}
                        </div>,
                        document.body
                      )}
                    </div>

                    <button 
                      onClick={() => { updateSettings({ systemAudioEnabled: false }); setOpenDropdown(null); }}
                      className="w-full text-left px-4 py-2 text-[12px] text-white/90 hover:bg-white/10 transition-colors flex items-center gap-2"
                    >
                      <span className="material-symbols-outlined text-[14px]">
                        {!settings.systemAudioEnabled ? 'check' : ''}
                      </span>
                      <span className="material-symbols-outlined text-[16px]">volume_off</span> None
                    </button>
                  </div>,
                  document.body
                )}
              </div>

              <div className="h-px w-full bg-white/5 my-0.5" />

              {/* Camera */}
              <div className="relative group" onClick={(e) => e.stopPropagation()}>
                <button 
                  ref={cameraBtnRef}
                  onClick={() => openDropdownAt('camera', cameraBtnRef)}
                  className={`w-full flex items-center gap-3 px-3 py-2.5 rounded-xl transition-colors ${settings.cameraEnabled ? 'bg-indigo-500/10' : 'hover:bg-white/5'}`}
                >
                  <div className={`w-7 h-7 rounded-full flex items-center justify-center ${settings.cameraEnabled ? 'bg-indigo-500/20 text-indigo-400' : 'bg-white/5 text-white/50'}`}>
                    <span className="material-symbols-outlined text-[16px]">
                      {settings.cameraEnabled ? 'videocam' : 'videocam_off'}
                    </span>
                  </div>
                  <span className="flex-1 text-left text-[12px] text-white/90 truncate pr-4">
                    {settings.cameraEnabled ? (cameras.find(c => c.deviceId === settings.selectedCameraId)?.label || cameras[0]?.label || 'Camera') : 'None'}
                  </span>
                  <span className="material-symbols-outlined text-white/30 text-[18px]">expand_more</span>
                </button>

                {openDropdown === 'camera' && ReactDOM.createPortal(
                  <div 
                    className="fixed w-max min-w-[200px] overflow-y-auto scrollbar-thin scrollbar-thumb-white/10 bg-[#1e1f2e] border border-white/10 rounded-xl shadow-2xl z-[9999] py-1 whitespace-nowrap"
                    style={{ ...(dropdownPos.isUp ? { bottom: dropdownPos.bottom } : { top: dropdownPos.top }), right: dropdownPos.right, maxHeight: dropdownPos.maxHeight || 192, WebkitAppRegion: 'no-drag' } as unknown as React.CSSProperties}
                    onClick={(e) => e.stopPropagation()}
                  >
                    <button 
                      onClick={() => { updateSettings({ cameraEnabled: false }); setOpenDropdown(null); }}
                      className="w-full text-left px-4 py-2 text-[12px] text-white/90 hover:bg-white/10 transition-colors flex items-center gap-2"
                    >
                      <span className="material-symbols-outlined text-[14px]">
                        {!settings.cameraEnabled ? 'check' : ''}
                      </span>
                      <span className="material-symbols-outlined text-[16px]">videocam_off</span> None
                    </button>
                    <div className="h-px bg-white/10 my-1" />
                    {cameras.map(camera => (
                      <button 
                        key={camera.deviceId}
                        onClick={() => { updateSettings({ cameraEnabled: true, selectedCameraId: camera.deviceId }); setOpenDropdown(null); }}
                        className="w-full text-left px-4 py-2 text-[12px] text-white/90 hover:bg-white/10 transition-colors flex items-center gap-2"
                      >
                        <span className="material-symbols-outlined text-[14px]">
                          {settings.cameraEnabled && settings.selectedCameraId === camera.deviceId ? 'check' : ''}
                        </span>
                        {camera.label || 'Camera'}
                      </button>
                    ))}
                  </div>,
                  document.body
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
