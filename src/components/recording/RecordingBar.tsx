import { useState, useEffect } from 'react';
import { useAppStore } from '../../stores/appStore';
import { useRecording } from '../../hooks/useRecording';
import { useMediaDevices } from '../../hooks/useMediaDevices';
import { useAudioLevel } from '../../hooks/useAudioLevel';
import { useSystemAudioLevel } from '../../hooks/useSystemAudioLevel';
import type { ScreenSource } from '../../shared/types';

export default function RecordingBar() {
  const isOverlay = window.location.hash === '#/recording-bar';
  const isRecording = useAppStore((s) => s.isRecording);
  const preRecordingBarOpen = useAppStore((s) => s.preRecordingBarOpen);
  const setPreRecordingBarOpen = useAppStore((s) => s.setPreRecordingBarOpen);
  const settings = useAppStore((s) => s.settings);
  const updateSettings = useAppStore((s) => s.updateSettings);
  const [elapsed, setElapsed] = useState(0);

  const { startRecording, isPreparing } = useRecording();
  const { mics, speakers, cameras } = useMediaDevices();
  const micLevel = useAudioLevel('default', settings.microphoneEnabled);
  const sysLevel = useSystemAudioLevel(settings.systemAudioEnabled, settings.selectedSpeakerId);

  const [openDropdown, setOpenDropdown] = useState<'camera' | 'mic' | 'speaker' | null>(null);
  const [windowSources, setWindowSources] = useState<ScreenSource[]>([]);

  useEffect(() => {
    window.electronAPI.getScreenSources().then(s => setWindowSources(s.filter(x => x.id.startsWith('window:'))));
  }, []);

  useEffect(() => {
    if (!isOverlay && !isRecording) return;
    const start = Date.now();
    const interval = setInterval(() => {
      setElapsed(Math.floor((Date.now() - start) / 1000));
    }, 1000);
    return () => clearInterval(interval);
  }, [isOverlay, isRecording]);

  const handleStop = () => {
    if (isOverlay) {
      window.electronAPI.requestStopRecording();
    } else {
      const stopFn = (window as unknown as { __stopRecording?: () => void }).__stopRecording;
      if (stopFn) stopFn();
    }
  };

  const handleClosePreRecording = () => {
    if (!isOverlay) {
      window.electronAPI.restoreDashboardSize();
    }
    setPreRecordingBarOpen(false);
  };

  const handleStartRecording = async () => {
    setPreRecordingBarOpen(false);
    await startRecording();
  };

  // Close dropdowns when clicking outside
  useEffect(() => {
    const handleClickOutside = () => setOpenDropdown(null);
    document.addEventListener('click', handleClickOutside);
    return () => document.removeEventListener('click', handleClickOutside);
  }, []);

  if (!isOverlay && !isRecording && !preRecordingBarOpen) return null;

  const mins = Math.floor(elapsed / 60).toString().padStart(2, '0');
  const secs = (elapsed % 60).toString().padStart(2, '0');

  // Active recording bar (Small top notification)
  if (isRecording || isOverlay) {
    if (!isOverlay) return null; // Sadece overlay penceresinde gösterilsin
    
    return (
      <div 
        className="fixed inset-0 flex items-center justify-between px-4 bg-[#1e1f27]/90 backdrop-blur-md border border-white/10 rounded-full shadow-[0_10px_30px_rgba(0,0,0,0.5)] select-none"
        style={{ WebkitAppRegion: 'drag' } as React.CSSProperties}
      >
        <div className="flex items-center gap-2">
          <div className="w-2.5 h-2.5 rounded-full bg-red-500 animate-pulse" />
          <span className="font-label-sm text-xs font-medium text-white/90">Kayıt Ediliyor</span>
        </div>
        <div className="flex items-center gap-3" style={{ WebkitAppRegion: 'no-drag' } as React.CSSProperties}>
          <span className="font-mono text-xs text-indigo-400 tabular-nums font-semibold">
            {mins}:{secs}
          </span>
          <div className="w-px h-3 bg-white/20" />
          <button
            onClick={handleStop}
            className="w-6 h-6 flex items-center justify-center rounded-full hover:bg-red-500/20 text-red-500 transition-colors"
            title="Kaydı Durdur"
          >
            <span className="material-symbols-outlined text-[16px]">stop</span>
          </button>
        </div>
      </div>
    );
  }

  // Pre-recording setup bar (Cropped dark purple design)
  if (preRecordingBarOpen) {
    return (
      <div 
        className={`fixed bottom-8 left-1/2 -translate-x-1/2 z-[200] flex items-center gap-1.5 bg-[#1b1b22] rounded-full p-1.5 shadow-[0_8px_32px_rgba(0,0,0,0.6)] border border-white/5`}
        style={{ WebkitAppRegion: 'drag' } as React.CSSProperties}
      >
        <button onClick={handleClosePreRecording} className="w-9 h-9 flex items-center justify-center rounded-full hover:bg-white/10 transition-colors text-white/50 hover:text-white/90" style={{ WebkitAppRegion: 'no-drag' } as React.CSSProperties}>
          <span className="material-symbols-outlined text-[20px]">close</span>
        </button>
        
        <div className="w-px h-5 bg-white/10 mx-1" />
        
        <button className="w-9 h-9 flex items-center justify-center rounded-xl bg-white/5 hover:bg-white/10 transition-colors text-white/90" style={{ WebkitAppRegion: 'no-drag' } as React.CSSProperties}>
          <span className="material-symbols-outlined text-[18px]">picture_in_picture</span>
        </button>
        
        <button className="w-9 h-9 flex items-center justify-center rounded-full hover:bg-white/10 transition-colors text-white/50 hover:text-white/90" style={{ WebkitAppRegion: 'no-drag' } as React.CSSProperties}>
          <span className="material-symbols-outlined text-[18px]">settings</span>
        </button>
        
        {/* Camera Dropdown */}
        <div className="relative" onClick={(e) => e.stopPropagation()} style={{ WebkitAppRegion: 'no-drag' } as React.CSSProperties}>
          <button 
            onClick={() => setOpenDropdown(openDropdown === 'camera' ? null : 'camera')}
            className={`flex items-center gap-2 px-3 py-1.5 rounded-xl transition-colors text-white/90 ${settings.cameraEnabled ? 'bg-[#2b2b40]' : 'hover:bg-white/10'}`}
          >
            <span className="material-symbols-outlined text-[18px]">{settings.cameraEnabled ? 'videocam' : 'videocam_off'}</span>
            <span className="text-[13px] max-w-[120px] truncate">{settings.cameraEnabled ? (cameras.find(c => c.deviceId === settings.selectedCameraId)?.label || cameras[0]?.label || 'Camera') : 'None'}</span>
            <span className="material-symbols-outlined text-[16px] text-white/50">expand_more</span>
          </button>

          {openDropdown === 'camera' && (
            <div className="absolute bottom-full left-0 mb-2 w-48 max-h-48 overflow-y-auto bg-[#252530] border border-white/10 rounded-xl shadow-xl z-50 py-1 scrollbar-thin scrollbar-thumb-white/10">
              <button 
                onClick={() => { updateSettings({ cameraEnabled: false }); setOpenDropdown(null); }}
                className="w-full text-left px-4 py-2 text-[13px] text-white/90 hover:bg-white/10 transition-colors flex items-center gap-2"
              >
                <span className="material-symbols-outlined text-[16px]">videocam_off</span> None
              </button>
              <div className="h-px bg-white/10 my-1" />
              {cameras.map(camera => (
                <button 
                  key={camera.deviceId}
                  onClick={() => { updateSettings({ cameraEnabled: true, selectedCameraId: camera.deviceId }); setOpenDropdown(null); }}
                  className="w-full text-left px-4 py-2 text-[13px] text-white/90 hover:bg-white/10 transition-colors truncate"
                >
                  {camera.label || 'Camera'}
                </button>
              ))}
            </div>
          )}
        </div>
        
        {/* Microphone Dropdown */}
        <div className="relative" onClick={(e) => e.stopPropagation()} style={{ WebkitAppRegion: 'no-drag' } as React.CSSProperties}>
          <button 
            onClick={() => setOpenDropdown(openDropdown === 'mic' ? null : 'mic')}
            className={`flex items-center gap-2 px-3 py-1.5 rounded-xl transition-colors text-white/90 ${settings.microphoneEnabled ? 'bg-[#2b2b40]' : 'hover:bg-white/10'}`}
          >
            <div className="relative flex items-center justify-center">
              <span className="material-symbols-outlined text-[18px] z-10 relative">{settings.microphoneEnabled ? 'mic' : 'mic_off'}</span>
              {settings.microphoneEnabled && micLevel > 0.05 && (
                <div 
                  className="absolute inset-[-4px] rounded-full bg-indigo-400 opacity-40 pointer-events-none transition-transform duration-75"
                  style={{ transform: `scale(${1 + micLevel})` }}
                />
              )}
            </div>
            <span className="text-[13px] max-w-[120px] truncate">{settings.microphoneEnabled ? (mics.find(m => m.deviceId === settings.selectedMicId)?.label || mics[0]?.label || 'Microphone') : 'None'}</span>
            <span className="material-symbols-outlined text-[16px] text-white/50">expand_more</span>
          </button>

          {openDropdown === 'mic' && (
            <div className="absolute bottom-full left-0 mb-2 w-48 max-h-48 overflow-y-auto bg-[#252530] border border-white/10 rounded-xl shadow-xl z-50 py-1 scrollbar-thin scrollbar-thumb-white/10">
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
                  onClick={() => { updateSettings({ microphoneEnabled: true, selectedMicId: mic.deviceId }); setOpenDropdown(null); }}
                  className="w-full text-left px-4 py-2 text-[13px] text-white/90 hover:bg-white/10 transition-colors truncate"
                >
                  {mic.label || 'Microphone'}
                </button>
              ))}
            </div>
          )}
        </div>

        {/* Speaker Dropdown */}
        <div className="relative" onClick={(e) => e.stopPropagation()} style={{ WebkitAppRegion: 'no-drag' } as React.CSSProperties}>
          <button 
            onClick={() => {
              setOpenDropdown(openDropdown === 'speaker' ? null : 'speaker');
              if (openDropdown !== 'speaker') {
                window.electronAPI.getScreenSources().then(s => setWindowSources(s.filter(x => x.id.startsWith('window:'))));
              }
            }}
            className={`flex items-center gap-2 px-3 py-1.5 rounded-xl transition-colors text-white/90 ${settings.systemAudioEnabled ? 'bg-[#2b2b40]' : 'hover:bg-white/10'}`}
          >
            <div className="relative flex items-center justify-center">
              <span className="material-symbols-outlined text-[18px] z-10 relative">{settings.systemAudioEnabled ? 'volume_up' : 'volume_off'}</span>
              {settings.systemAudioEnabled && sysLevel > 0.05 && (
                <div 
                  className="absolute inset-[-4px] rounded-full bg-indigo-400 opacity-40 pointer-events-none transition-transform duration-75"
                  style={{ transform: `scale(${1 + sysLevel})` }}
                />
              )}
            </div>
            <span className="text-[13px] max-w-[120px] truncate">{settings.systemAudioEnabled ? (settings.selectedSpeakerId?.startsWith('window:') ? windowSources.find(w => w.id === settings.selectedSpeakerId)?.name : speakers.find(s => s.deviceId === settings.selectedSpeakerId)?.label) || speakers[0]?.label || 'Speaker' : 'None'}</span>
            <span className="material-symbols-outlined text-[16px] text-white/50">expand_more</span>
          </button>

          {openDropdown === 'speaker' && (
            <div className="absolute bottom-full left-0 mb-2 w-48 max-h-48 overflow-y-auto bg-[#252530] border border-white/10 rounded-xl shadow-xl z-50 py-1 scrollbar-thin scrollbar-thumb-white/10">
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
                  onClick={() => { updateSettings({ systemAudioEnabled: true, selectedSpeakerId: speaker.deviceId }); setOpenDropdown(null); }}
                  className="w-full text-left px-4 py-2 text-[13px] text-white/90 hover:bg-white/10 transition-colors truncate"
                >
                  {speaker.label || 'Speaker'}
                </button>
              ))}
              
              {windowSources.length > 0 && (
                <>
                  <div className="px-4 pt-3 pb-1 text-[11px] font-semibold text-white/50 uppercase tracking-wider">Only app audio</div>
                  {windowSources.map(src => (
                    <button 
                      key={src.id}
                      onClick={() => { updateSettings({ systemAudioEnabled: true, selectedSpeakerId: src.id }); setOpenDropdown(null); }}
                      className="w-full text-left px-4 py-2 text-[13px] text-white/90 hover:bg-white/10 transition-colors truncate"
                    >
                      {src.name}
                    </button>
                  ))}
                </>
              )}
            </div>
          )}
        </div>

        {/* REC Button */}
        <button 
          onClick={handleStartRecording}
          disabled={isPreparing}
          className="ml-1 w-12 h-9 flex items-center justify-center rounded-full bg-[#5a67d8] hover:bg-[#667eea] transition-colors text-white font-bold text-[11px] tracking-wider disabled:opacity-50"
          style={{ WebkitAppRegion: 'no-drag' } as React.CSSProperties}
        >
          REC
        </button>
      </div>
    );
  }

  return null;
}
