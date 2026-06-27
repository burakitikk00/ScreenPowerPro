import { create } from 'zustand';
import type { ZoomEffect, TimelineSettings, TrackState, ClipSegment } from '../types';

// Snapshot of undoable state
interface HistorySnapshot {
  zoomEffects: ZoomEffect[];
  videoTrack: TrackState;
  micTrack: TrackState;
  sysTrack: TrackState;
}

interface EditorState {
  currentTime: number;
  duration: number;
  isPlaying: boolean;
  selectedZoomId: string | null;
  selectedClipId: string | null;
  timelineZoom: number;
  timelineScroll: number;
  activeSidebarTab: string;
  settings: TimelineSettings;
  zoomEffects: ZoomEffect[];

  videoTrack: TrackState;
  micTrack: TrackState;
  sysTrack: TrackState;

  // Seek request counter — VideoPreview watches this to know when to seek
  seekVersion: number;

  // History for undo/redo
  history: HistorySnapshot[];
  historyIndex: number;

  setActiveSidebarTab: (tab: string) => void;
  setCurrentTime: (t: number) => void;
  seekTo: (t: number) => void;
  setTimelineScroll: (s: number) => void;
  setDuration: (d: number) => void;
  setIsPlaying: (p: boolean) => void;
  setSelectedZoomId: (id: string | null) => void;
  setSelectedClipId: (id: string | null) => void;
  setTimelineZoom: (z: number) => void;
  setZoomEffects: (effects: ZoomEffect[]) => void;
  updateZoomEffect: (id: string, partial: Partial<ZoomEffect>) => void;
  removeZoomEffect: (id: string) => void;
  updateSettings: (partial: Partial<TimelineSettings>) => void;
  updateTrack: (type: 'video' | 'mic' | 'sys', data: Partial<TrackState>) => void;
  updateClip: (type: 'video' | 'mic' | 'sys', clipId: string, partial: Partial<ClipSegment>) => void;
  loadFromProject: (effects: ZoomEffect[], settings: TimelineSettings, duration: number) => void;

  // Undo / Redo
  undo: () => void;
  redo: () => void;
  pushHistory: () => void;

  // Cut at playhead — splits clips
  cutAtPlayhead: (time: number) => void;

  // Delete selected clip or zoom
  deleteSelected: () => void;

  // Toggle mute for a track
  toggleMute: (type: 'video' | 'mic' | 'sys') => void;

  // Initialize clips from duration (called after media loads)
  initClipsFromDuration: (duration: number) => void;
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
  cursorStyle: 'default',
  clickEffect: 'default',
  cursorClickSound: false,
  hideCursorWhenIdle: false,
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
  clips: [],
  muted: false,
};

let clipIdCounter = 0;
function generateClipId(): string {
  clipIdCounter += 1;
  return `clip-${Date.now()}-${clipIdCounter}`;
}

function snapshot(state: EditorState): HistorySnapshot {
  return {
    zoomEffects: state.zoomEffects.map((e) => ({ ...e })),
    videoTrack: { ...state.videoTrack, clips: state.videoTrack.clips.map(c => ({ ...c })) },
    micTrack: { ...state.micTrack, clips: state.micTrack.clips.map(c => ({ ...c })) },
    sysTrack: { ...state.sysTrack, clips: state.sysTrack.clips.map(c => ({ ...c })) },
  };
}

const MAX_HISTORY = 50;

// Helper: split a clip at a given time
function splitClipsAtTime(clips: ClipSegment[], time: number): ClipSegment[] {
  const result: ClipSegment[] = [];
  for (const clip of clips) {
    const clipDuration = clip.sourceEnd - clip.sourceStart;
    const clipEnd = clip.trackOffset + clipDuration;

    if (time > clip.trackOffset && time < clipEnd) {
      // Split this clip into two
      const splitPoint = clip.sourceStart + (time - clip.trackOffset);
      result.push({
        id: clip.id,
        sourceStart: clip.sourceStart,
        sourceEnd: splitPoint,
        trackOffset: clip.trackOffset,
      });
      result.push({
        id: generateClipId(),
        sourceStart: splitPoint,
        sourceEnd: clip.sourceEnd,
        trackOffset: time,
      });
    } else {
      result.push(clip);
    }
  }
  return result;
}

// Helper: remove a clip and shift subsequent clips
function removeClipById(clips: ClipSegment[], clipId: string): ClipSegment[] {
  const idx = clips.findIndex(c => c.id === clipId);
  if (idx === -1) return clips;

  const removed = clips[idx];
  const removedDuration = removed.sourceEnd - removed.sourceStart;
  const newClips: ClipSegment[] = [];

  for (let i = 0; i < clips.length; i++) {
    if (i === idx) continue;
    const clip = { ...clips[i] };
    // Shift clips that come after the removed one
    if (clip.trackOffset > removed.trackOffset) {
      clip.trackOffset = Math.max(0, clip.trackOffset - removedDuration);
    }
    newClips.push(clip);
  }

  return newClips;
}

export const useEditorStore = create<EditorState>((set, get) => ({
  currentTime: 0,
  duration: 0,
  isPlaying: false,
  selectedZoomId: null,
  selectedClipId: null,
  timelineZoom: 50,
  timelineScroll: 0,
  zoomEffects: [],
  activeSidebarTab: 'motion',
  settings: defaultTimelineSettings,
  videoTrack: defaultTrackState,
  micTrack: defaultTrackState,
  sysTrack: defaultTrackState,
  seekVersion: 0,
  history: [],
  historyIndex: -1,

  setActiveSidebarTab: (tab) => set({ activeSidebarTab: tab }),

  // setCurrentTime: called by video timeupdate — does NOT trigger seek
  setCurrentTime: (t) => set({ currentTime: t }),

  // seekTo: called by user clicking timeline — DOES trigger seek
  seekTo: (t) => set((s) => ({
    currentTime: t,
    seekVersion: s.seekVersion + 1,
  })),

  setTimelineScroll: (s) => set({ timelineScroll: s }),
  setDuration: (d) =>
    set((s) => ({
      duration: Number.isFinite(d) && d > 0 && d !== Infinity ? d : s.duration,
    })),
  setIsPlaying: (p) => set({ isPlaying: p }),
  setSelectedZoomId: (id) => set({ selectedZoomId: id, selectedClipId: null }),
  setSelectedClipId: (id) => set({ selectedClipId: id, selectedZoomId: null }),
  setTimelineZoom: (z) => set({ timelineZoom: z }),

  setZoomEffects: (effects) => {
    get().pushHistory();
    set({ zoomEffects: effects });
  },

  updateZoomEffect: (id, partial) => {
    get().pushHistory();
    set((s) => ({
      zoomEffects: s.zoomEffects.map((e) => (e.id === id ? { ...e, ...partial } : e)),
    }));
  },

  removeZoomEffect: (id) => {
    get().pushHistory();
    set((s) => ({
      zoomEffects: s.zoomEffects.filter((e) => e.id !== id),
      selectedZoomId: s.selectedZoomId === id ? null : s.selectedZoomId,
    }));
  },

  updateSettings: (s) => set((state) => ({ settings: { ...state.settings, ...s } })),

  updateTrack: (type, data) => {
    get().pushHistory();
    set((state) => {
      if (type === 'video') return { videoTrack: { ...state.videoTrack, ...data } };
      if (type === 'mic') return { micTrack: { ...state.micTrack, ...data } };
      if (type === 'sys') return { sysTrack: { ...state.sysTrack, ...data } };
      return state;
    });
  },

  updateClip: (type, clipId, partial) => {
    // Only push history if it's the start of a drag, or we could just skip it here
    // Wait, dragging calls updateClip continuously. We SHOULD NOT push history on every frame!
    // Instead, pushHistory should be called onMouseDown.
    set((state) => {
      let track = type === 'video' ? state.videoTrack : type === 'mic' ? state.micTrack : state.sysTrack;
      const newClips = track.clips.map(c => c.id === clipId ? { ...c, ...partial } : c);
      if (type === 'video') return { videoTrack: { ...state.videoTrack, clips: newClips } };
      if (type === 'mic') return { micTrack: { ...state.micTrack, clips: newClips } };
      if (type === 'sys') return { sysTrack: { ...state.sysTrack, clips: newClips } };
      return state;
    });
  },

  loadFromProject: (effects, settings, duration) =>
    set({
      zoomEffects: effects,
      settings: { ...defaultTimelineSettings, ...settings },
      duration: Number.isFinite(duration) && duration > 0 ? duration : 0,
      currentTime: 0,
      isPlaying: false,
      videoTrack: defaultTrackState,
      micTrack: defaultTrackState,
      sysTrack: defaultTrackState,
      history: [],
      historyIndex: -1,
      selectedClipId: null,
      selectedZoomId: null,
    }),

  // Initialize clips from duration — creates a single full-length clip for each track
  initClipsFromDuration: (duration: number) => {
    const state = get();
    const makeClip = (): ClipSegment[] => [{
      id: generateClipId(),
      sourceStart: 0,
      sourceEnd: duration,
      trackOffset: 0,
    }];

    // Only initialize if clips are empty
    const updates: Partial<EditorState> = {};
    if (state.videoTrack.clips.length === 0) {
      updates.videoTrack = { ...state.videoTrack, clips: makeClip() };
    }
    if (state.micTrack.clips.length === 0) {
      updates.micTrack = { ...state.micTrack, clips: makeClip() };
    }
    if (state.sysTrack.clips.length === 0) {
      updates.sysTrack = { ...state.sysTrack, clips: makeClip() };
    }
    if (Object.keys(updates).length > 0) {
      set(updates);
    }
  },

  // Push current state onto history stack (called before mutations)
  pushHistory: () =>
    set((state) => {
      const snap = snapshot(state);
      const newHistory = state.history.slice(0, state.historyIndex + 1);
      newHistory.push(snap);
      if (newHistory.length > MAX_HISTORY) newHistory.shift();
      return { history: newHistory, historyIndex: newHistory.length - 1 };
    }),

  undo: () =>
    set((state) => {
      if (state.historyIndex < 0) return state;
      const snap = state.history[state.historyIndex];
      return {
        ...snap,
        historyIndex: state.historyIndex - 1,
      };
    }),

  redo: () =>
    set((state) => {
      if (state.historyIndex >= state.history.length - 1) return state;
      const snap = state.history[state.historyIndex + 1];
      return {
        ...snap,
        historyIndex: state.historyIndex + 1,
      };
    }),

  // Cut all tracks at the playhead position — splits clips into two
  cutAtPlayhead: (time: number) => {
    get().pushHistory();

    set((s) => {
      const newVideoClips = splitClipsAtTime(s.videoTrack.clips, time);
      const newMicClips = splitClipsAtTime(s.micTrack.clips, time);
      const newSysClips = splitClipsAtTime(s.sysTrack.clips, time);

      // Also split zoom effects
      const newZoomEffects = s.zoomEffects.flatMap((z) => {
        if (time > z.startTime && time < z.startTime + z.duration) {
          return [
            { ...z, duration: time - z.startTime },
            {
              ...z,
              id: `${z.id}-split`,
              startTime: time,
              duration: z.startTime + z.duration - time,
            },
          ];
        }
        return [z];
      });

      return {
        videoTrack: { ...s.videoTrack, clips: newVideoClips },
        micTrack: { ...s.micTrack, clips: newMicClips },
        sysTrack: { ...s.sysTrack, clips: newSysClips },
        zoomEffects: newZoomEffects,
      };
    });
  },

  // Delete selected clip or zoom effect
  deleteSelected: () => {
    const state = get();

    if (state.selectedClipId) {
      get().pushHistory();
      const clipId = state.selectedClipId;

      set((s) => ({
        videoTrack: { ...s.videoTrack, clips: removeClipById(s.videoTrack.clips, clipId) },
        micTrack: { ...s.micTrack, clips: removeClipById(s.micTrack.clips, clipId) },
        sysTrack: { ...s.sysTrack, clips: removeClipById(s.sysTrack.clips, clipId) },
        selectedClipId: null,
      }));
    } else if (state.selectedZoomId) {
      get().removeZoomEffect(state.selectedZoomId);
    }
  },

  // Toggle mute for a track
  toggleMute: (type) => {
    set((state) => {
      if (type === 'video') return { videoTrack: { ...state.videoTrack, muted: !state.videoTrack.muted } };
      if (type === 'mic') return { micTrack: { ...state.micTrack, muted: !state.micTrack.muted } };
      if (type === 'sys') return { sysTrack: { ...state.sysTrack, muted: !state.sysTrack.muted } };
      return state;
    });
  },
}));
