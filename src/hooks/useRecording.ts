import { useRef, useCallback, useState, useEffect } from 'react';
import { useAppStore } from '../stores/appStore';
import { useEditorStore } from '../stores/editorStore';
import { generateZoomEffectsFromClicks } from '../lib/zoomEngine';
import { getVideoMimeType, getAudioMimeType, pickDefaultSource } from '../lib/recordingUtils';
import type { ProjectManifest, RecordingMetadata } from '../types';

interface ActiveRecorder {
  recorder: MediaRecorder;
  chunks: Blob[];
  stream: MediaStream;
}

function createRecorder(stream: MediaStream, mimeType: string): ActiveRecorder {
  const chunks: Blob[] = [];
  const recorder = new MediaRecorder(stream, { mimeType });
  recorder.ondataavailable = (e) => {
    if (e.data.size > 0) chunks.push(e.data);
  };
  recorder.start(250);
  return { recorder, chunks, stream };
}

async function finalizeRecorder(active: ActiveRecorder): Promise<Blob> {
  return new Promise((resolve, reject) => {
    const { recorder, chunks, stream } = active;
    if (recorder.state === 'inactive') {
      stream.getTracks().forEach((t) => t.stop());
      resolve(new Blob(chunks, { type: recorder.mimeType.split(';')[0] }));
      return;
    }
    recorder.onstop = () => {
      stream.getTracks().forEach((t) => t.stop());
      resolve(new Blob(chunks, { type: recorder.mimeType.split(';')[0] }));
    };
    recorder.onerror = () => reject(new Error('Kayıt hatası'));
    recorder.requestData();
    setTimeout(() => {
      if (recorder.state !== 'inactive') recorder.stop();
    }, 200);
  });
}

export function useRecording() {
  const {
    recordingMode,
    settings,
    selectedSourceId,
    setCountdown,
    setIsRecording,
    setScreen,
    setCurrentProject,
    updateSettings,
  } = useAppStore();
  const loadEditor = useEditorStore((s) => s.loadFromProject);

  const videoRecorderRef = useRef<ActiveRecorder | null>(null);
  const micRecorderRef = useRef<ActiveRecorder | null>(null);
  const systemAudioRecorderRef = useRef<ActiveRecorder | null>(null);
  const projectPathRef = useRef('');
  const startTimeRef = useRef(0);
  const [isPreparing, setIsPreparing] = useState(false);

  const stopRecording = useCallback(async () => {
    try {
      const videoActive = videoRecorderRef.current;
      if (!videoActive) return;

      const [videoBlob, micBlob, systemBlob] = await Promise.all([
        finalizeRecorder(videoActive),
        micRecorderRef.current ? finalizeRecorder(micRecorderRef.current) : null,
        systemAudioRecorderRef.current
          ? finalizeRecorder(systemAudioRecorderRef.current)
          : null,
      ]);

      videoRecorderRef.current = null;
      micRecorderRef.current = null;
      systemAudioRecorderRef.current = null;

      if (videoBlob.size < 1000) {
        throw new Error('Video kaydı boş — lütfen tekrar deneyin');
      }

      await window.electronAPI.saveRecordingFile(
        projectPathRef.current,
        'display-0.webm',
        await videoBlob.arrayBuffer()
      );

      if (micBlob && micBlob.size > 0) {
        await window.electronAPI.saveRecordingFile(
          projectPathRef.current,
          'microphone-0.webm',
          await micBlob.arrayBuffer()
        );
      }

      if (systemBlob && systemBlob.size > 0) {
        await window.electronAPI.saveRecordingFile(
          projectPathRef.current,
          'system_audio-0.webm',
          await systemBlob.arrayBuffer()
        );
      }

      const inputData = await window.electronAPI.stopInputTracking();
      const duration = (Date.now() - startTimeRef.current) / 1000;
      const is4k = settings.resolution === '4k';

      const metadata: RecordingMetadata = {
        width: is4k ? 3840 : 1920,
        height: is4k ? 2160 : 1080,
        fps: 30,
        duration,
        recordedAt: new Date().toISOString(),
        mode: recordingMode,
      };

      await window.electronAPI.saveMetadata(
        projectPathRef.current,
        metadata,
        inputData.clicks,
        inputData.moves,
        inputData.keystrokes
      );

      const zoomEffects = generateZoomEffectsFromClicks(
        inputData.clicks,
        settings.autoZoom,
        1.5
      );

      const manifest: ProjectManifest = {
        version: '1.0',
        name: `Kayıt_${new Date().toISOString().slice(0, 10)}`,
        videoPath: './recording/display-0.webm',
        microphonePath: micBlob ? './recording/microphone-0.webm' : undefined,
        systemAudioPath: systemBlob ? './recording/system_audio-0.webm' : undefined,
        timeline: {
          zoomEffects,
          settings: {
            cursorSmoothing: 'medium',
            motionBlur: true,
            watermark: false,
            showShortcutKeys: true,
            canvasBackground: '#000000',
            backgroundOpacity: 100,
            defaultZoomScale: 1.5,
            motionBlurAmount: 30,
          },
        },
      };

      await window.electronAPI.saveProjectManifest(projectPathRef.current, manifest);
      await window.electronAPI.restoreAfterRecording();
      await window.electronAPI.closeCameraOverlay();

      setCurrentProject(manifest, projectPathRef.current);
      loadEditor(manifest.timeline.zoomEffects, manifest.timeline.settings, duration);
      
      // Resize window for the editor
      await window.electronAPI.resizeForEditor();
      setScreen('editor');
    } catch (err) {
      console.error('Kayıt kaydetme hatası:', err);
      await window.electronAPI.restoreAfterRecording();
      await window.electronAPI.closeCameraOverlay();
      alert(err instanceof Error ? err.message : 'Kayıt kaydedilemedi');
    } finally {
      setIsRecording(false);
    }
  }, [recordingMode, settings, setCurrentProject, setScreen, setIsRecording, loadEditor]);

  useEffect(() => {
    const unsub = window.electronAPI.onStopRecordingRequest(() => {
      stopRecording();
    });
    (window as unknown as { __stopRecording?: () => void }).__stopRecording = stopRecording;
    return unsub;
  }, [stopRecording]);

  const startRecording = useCallback(
    async (sourceIdOverride?: string) => {
      if (isPreparing) return;
      setIsPreparing(true);

      try {
        const paths = await window.electronAPI.getDefaultPaths();
        if (!settings.projectLocation) {
          updateSettings({ projectLocation: paths.projects });
        }

        const sourceId = await pickDefaultSource(
          recordingMode,
          sourceIdOverride || selectedSourceId
        );

        if (recordingMode === 'window' && !sourceIdOverride && !selectedSourceId) {
          setIsPreparing(false);
          return { needsWindowPicker: true as const };
        }

        const projectName = `Kayıt_${new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19)}`;
        const { projectPath } = await window.electronAPI.createProject(projectName);
        projectPathRef.current = projectPath;

        const runCountdown = settings.countdown;
        if (runCountdown > 0) {
          for (let i = runCountdown; i > 0; i--) {
            setCountdown(i);
            await new Promise((r) => setTimeout(r, 1000));
          }
          setCountdown(null);
        }

        await window.electronAPI.prepareCapture({
          sourceId,
          includeSystemAudio: settings.systemAudioEnabled,
        });

        await window.electronAPI.minimizeForRecording();
        await new Promise((r) => setTimeout(r, 400));

        await window.electronAPI.startInputTracking();
        startTimeRef.current = Date.now();

        const displayStream = await navigator.mediaDevices.getDisplayMedia({
          video: true,
          audio: settings.systemAudioEnabled,
        });

        const videoTracks = displayStream.getVideoTracks();
        const videoOnlyStream = new MediaStream(videoTracks);
        videoRecorderRef.current = createRecorder(videoOnlyStream, getVideoMimeType());

        if (settings.systemAudioEnabled) {
          const audioTracks = displayStream.getAudioTracks();
          if (audioTracks.length > 0) {
            const systemStream = new MediaStream(audioTracks);
            systemAudioRecorderRef.current = createRecorder(systemStream, getAudioMimeType());
          }
        }

        if (settings.microphoneEnabled) {
          try {
            const micStream = await navigator.mediaDevices.getUserMedia({
              audio: true,
              video: false,
            });
            micRecorderRef.current = createRecorder(micStream, getAudioMimeType());
          } catch {
            console.warn('Mikrofon erişimi reddedildi');
          }
        }

        if (settings.cameraEnabled) {
          await window.electronAPI.createCameraOverlay();
        }

        setIsRecording(true);
        return { needsWindowPicker: false as const };
      } catch (err) {
        console.error('Kayıt başlatma hatası:', err);
        await window.electronAPI.restoreAfterRecording();
        setCountdown(null);
        setIsRecording(false);
        alert(err instanceof Error ? err.message : 'Kayıt başlatılamadı');
        return { needsWindowPicker: false as const };
      } finally {
        setIsPreparing(false);
      }
    },
    [
      isPreparing,
      recordingMode,
      selectedSourceId,
      settings,
      setCountdown,
      setIsRecording,
      updateSettings,
    ]
  );

  return { startRecording, stopRecording, isPreparing };
}
