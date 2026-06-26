import { useRef, useCallback } from 'react';
import { useAppStore } from '../stores/appStore';
import { useEditorStore } from '../stores/editorStore';
import { generateZoomEffectsFromClicks } from '../lib/zoomEngine';
import type { ProjectManifest, RecordingMetadata } from '../types';

export function useRecording() {
  const {
    recordingMode,
    settings,
    setCountdown,
    setIsRecording,
    setScreen,
    setCurrentProject,
    updateSettings,
  } = useAppStore();
  const loadEditor = useEditorStore((s) => s.loadFromProject);

  const mediaRecorderRef = useRef<MediaRecorder | null>(null);
  const chunksRef = useRef<Blob[]>([]);
  const streamRef = useRef<MediaStream | null>(null);
  const projectPathRef = useRef<string>('');
  const startTimeRef = useRef<number>(0);
  const isPreparingRef = useRef(false);

  const getConstraints = useCallback(async () => {
    const sources = await window.electronAPI.getScreenSources();
    let sourceId = sources[0]?.id;

    if (recordingMode === 'window' && sources.length > 1) {
      sourceId = sources.find((s) => s.id.includes('window'))?.id || sourceId;
    }

    const is4k = settings.resolution === '4k';
    const width = is4k ? 3840 : 1920;
    const height = is4k ? 2160 : 1080;

    return {
      audio: false,
      video: {
        mandatory: {
          chromeMediaSource: 'desktop',
          chromeMediaSourceId: sourceId,
          maxWidth: width,
          maxHeight: height,
          maxFrameRate: 30,
        },
      } as unknown as MediaTrackConstraints,
    };
  }, [recordingMode, settings.resolution]);

  const stopRecording = useCallback(async () => {
    return new Promise<void>((resolve) => {
      const recorder = mediaRecorderRef.current;
      if (!recorder || recorder.state === 'inactive') {
        resolve();
        return;
      }

      recorder.onstop = async () => {
        try {
          const blob = new Blob(chunksRef.current, { type: 'video/webm' });
          const buffer = await blob.arrayBuffer();
          await window.electronAPI.saveRecordingFile(
            projectPathRef.current,
            'display-0.webm',
            buffer
          );

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
          setCurrentProject(manifest, projectPathRef.current);
          loadEditor(manifest.timeline.zoomEffects, manifest.timeline.settings, duration);
          setScreen('editor');
        } catch (err) {
          console.error('Kayıt kaydetme hatası:', err);
        } finally {
          streamRef.current?.getTracks().forEach((t) => t.stop());
          setIsRecording(false);
          resolve();
        }
      };

      recorder.stop();
    });
  }, [recordingMode, settings, setCurrentProject, setScreen, setIsRecording, loadEditor]);

  const startRecording = useCallback(async () => {
    if (isPreparingRef.current) return;
    isPreparingRef.current = true;

    try {
      const paths = await window.electronAPI.getDefaultPaths();
      if (!settings.projectLocation) {
        updateSettings({ projectLocation: paths.projects });
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

      await window.electronAPI.startInputTracking();
      startTimeRef.current = Date.now();

      const constraints = await getConstraints();
      const stream = await navigator.mediaDevices.getUserMedia(constraints);
      streamRef.current = stream;

      const recorder = new MediaRecorder(stream, {
        mimeType: 'video/webm;codecs=vp9',
      });
      chunksRef.current = [];
      recorder.ondataavailable = (e) => {
        if (e.data.size > 0) chunksRef.current.push(e.data);
      };
      recorder.start(1000);
      mediaRecorderRef.current = recorder;
      setIsRecording(true);

      (window as unknown as { __stopRecording?: () => void }).__stopRecording = stopRecording;
    } catch (err) {
      console.error('Kayıt başlatma hatası:', err);
      setCountdown(null);
      setIsRecording(false);
    } finally {
      isPreparingRef.current = false;
    }
  }, [settings, getConstraints, setCountdown, setIsRecording, stopRecording, updateSettings]);

  return { startRecording, stopRecording, isPreparing: isPreparingRef.current };
}
