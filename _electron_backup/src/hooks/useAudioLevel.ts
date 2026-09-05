import { useState, useEffect, useRef } from 'react';

export function useAudioLevel(deviceId?: string, enabled: boolean = true) {
  const [level, setLevel] = useState(0);
  const streamRef = useRef<MediaStream | null>(null);
  const audioContextRef = useRef<AudioContext | null>(null);
  const analyserRef = useRef<AnalyserNode | null>(null);
  const sourceRef = useRef<MediaStreamAudioSourceNode | null>(null);
  const animationRef = useRef<number>();

  useEffect(() => {
    if (!enabled) {
      setLevel(0);
      return;
    }

    let isMounted = true;

    async function startListening() {
      try {
        const constraints = {
          audio: deviceId && deviceId !== 'default' && deviceId !== 'none'
            ? { deviceId: { exact: deviceId } }
            : true,
        };

        const stream = await navigator.mediaDevices.getUserMedia(constraints);
        if (!isMounted) {
          stream.getTracks().forEach(t => t.stop());
          return;
        }

        streamRef.current = stream;
        const audioCtx = new AudioContext();
        audioContextRef.current = audioCtx;

        const analyser = audioCtx.createAnalyser();
        analyser.fftSize = 256;
        analyserRef.current = analyser;

        const source = audioCtx.createMediaStreamSource(stream);
        source.connect(analyser);
        sourceRef.current = source;

        const dataArray = new Uint8Array(analyser.frequencyBinCount);

        const updateLevel = () => {
          if (!isMounted) return;
          analyser.getByteFrequencyData(dataArray);
          let sum = 0;
          for (let i = 0; i < dataArray.length; i++) {
            sum += dataArray[i];
          }
          const avg = sum / dataArray.length;
          // Normalize to 0-1 (approx max is 255)
          setLevel(Math.min(1, avg / 128));
          animationRef.current = requestAnimationFrame(updateLevel);
        };

        updateLevel();
      } catch (err) {
        console.warn('Could not start audio level monitoring:', err);
        setLevel(0);
      }
    }

    startListening();

    return () => {
      isMounted = false;
      if (animationRef.current) cancelAnimationFrame(animationRef.current);
      if (streamRef.current) streamRef.current.getTracks().forEach(t => t.stop());
      if (audioContextRef.current) audioContextRef.current.close();
    };
  }, [deviceId, enabled]);

  return level;
}
