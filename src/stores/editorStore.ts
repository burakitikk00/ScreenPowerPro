import { create } from 'zustand';
import type { ZoomEffect, TimelineSettings } from '../types';

interface EditorState {
  currentTime: number;
  duration: number;
  isPlaying: boolean;
  selectedZoomId: string | null;
  timelineZoom: number;
  zoomEffects: ZoomEffect[];
  settings: TimelineSettings;

  setCurrentTime: (t: number) => void;
  setDuration: (d: number) => void;
  setIsPlaying: (p: boolean) => void;
  setSelectedZoomId: (id: string | null) => void;
  setTimelineZoom: (z: number) => void;
  setZoomEffects: (effects: ZoomEffect[]) => void;
  updateZoomEffect: (id: string, partial: Partial<ZoomEffect>) => void;
  removeZoomEffect: (id: string) => void;
  updateSettings: (partial: Partial<TimelineSettings>) => void;
  loadFromProject: (effects: ZoomEffect[], settings: TimelineSettings, duration: number) => void;
}

const defaultTimelineSettings: TimelineSettings = {
  cursorSmoothing: 'medium',
  motionBlur: true,
  watermark: false,
  showShortcutKeys: true,
  canvasBackground: '#000000',
  backgroundOpacity: 100,
  defaultZoomScale: 1.5,
  motionBlurAmount: 30,
};

export const useEditorStore = create<EditorState>((set) => ({
  currentTime: 0,
  duration: 0,
  isPlaying: false,
  selectedZoomId: null,
  timelineZoom: 50,
  zoomEffects: [],
  settings: defaultTimelineSettings,

  setCurrentTime: (t) => set({ currentTime: t }),
  setDuration: (d) => set({ duration: d }),
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
  updateSettings: (partial) =>
    set((s) => ({ settings: { ...s.settings, ...partial } })),
  loadFromProject: (effects, settings, duration) =>
    set({ zoomEffects: effects, settings, duration, currentTime: 0, isPlaying: false }),
}));
