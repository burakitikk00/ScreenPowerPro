import { create } from 'zustand';
import type { ZoomEffect, TimelineSettings } from '../types';

interface EditorState {
  currentTime: number;
  duration: number;
  isPlaying: boolean;
  selectedZoomId: string | null;
  timelineZoom: number;
  timelineScroll: number;
  activeSidebarTab: string;
  settings: TimelineSettings;

  videoTrack: TrackState;
  micTrack: TrackState;
  sysTrack: TrackState;

  setActiveSidebarTab: (tab: string) => void;
  setCurrentTime: (t: number) => void;
  setTimelineScroll: (s: number) => void;
  setDuration: (d: number) => void;
  setIsPlaying: (p: boolean) => void;
  setSelectedZoomId: (id: string | null) => void;
  setTimelineZoom: (z: number) => void;
  setZoomEffects: (effects: ZoomEffect[]) => void;
  updateZoomEffect: (id: string, partial: Partial<ZoomEffect>) => void;
  removeZoomEffect: (id: string) => void;
  updateSettings: (partial: Partial<TimelineSettings>) => void;
  updateTrack: (type: 'video' | 'mic' | 'sys', data: Partial<TrackState>) => void;
  loadFromProject: (effects: ZoomEffect[], settings: TimelineSettings, duration: number) => void;
}

const defaultTimelineSettings: TimelineSettings = {
  cursorSmoothing: 'medium',
  motionBlur: false,
  watermark: false,
  showShortcutKeys: true,
  canvasBackground: '#1e1f27',
  backgroundOpacity: 100,
  defaultZoomScale: 1.5,
  motionBlurAmount: 50,
  videoSpeed: 1,
  cursorSize: 100,
  cursorVisible: true,
  cameraVisible: true,
  cameraShape: 'circle',
  cameraSize: 100,
  micVolume: 100,
  sysVolume: 100,
  watermarkText: 'ScreenPowerPro',
};

const defaultTrackState: TrackState = {
  offset: 0,
  trimStart: 0,
  trimEnd: 0,
};

export const useEditorStore = create<EditorState>((set) => ({
  currentTime: 0,
  duration: 0,
  isPlaying: false,
  selectedZoomId: null,
  timelineZoom: 50,
  timelineScroll: 0,
  zoomEffects: [],
  activeSidebarTab: 'motion',
  settings: defaultTimelineSettings,
  videoTrack: defaultTrackState,
  micTrack: defaultTrackState,
  sysTrack: defaultTrackState,

  setActiveSidebarTab: (tab) => set({ activeSidebarTab: tab }),
  setCurrentTime: (t) => set({ currentTime: t }),
  setTimelineScroll: (s) => set({ timelineScroll: s }),
  setDuration: (d) =>
    set((s) => ({
      duration: Number.isFinite(d) && d > 0 && d !== Infinity ? d : s.duration,
    })),
  setIsPlaying: (p) => set({ isPlaying: p }),
  setSelectedZoomId: (id) => set({ selectedZoomId: id }),
  setTimelineZoom: (z) => set({ timelineZoom: z }),
  setZoomEffects: (effects) => set({ zoomEffects: effects }),
  updateZoomEffect: (id, partial) =>
    set((s) => ({
      zoomEffects: s.zoomEffects.map((e) => (e.id === id ? { ...e, ...partial } : e)),
    })),
  removeZoomEffect: (id) =>
    set((s) => ({
      zoomEffects: s.zoomEffects.filter((e) => e.id !== id),
      selectedZoomId: s.selectedZoomId === id ? null : s.selectedZoomId,
    })),
  updateSettings: (s) => set((state) => ({ settings: { ...state.settings, ...s } })),
  updateTrack: (type, data) => set((state) => {
    if (type === 'video') return { videoTrack: { ...state.videoTrack, ...data } };
    if (type === 'mic') return { micTrack: { ...state.micTrack, ...data } };
    if (type === 'sys') return { sysTrack: { ...state.sysTrack, ...data } };
    return state;
  }),
  loadFromProject: (effects, settings, duration) =>
    set({
      zoomEffects: effects,
      settings,
      duration: Number.isFinite(duration) && duration > 0 ? duration : 0,
      currentTime: 0,
      isPlaying: false,
    }),
}));
