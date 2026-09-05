export function getVideoMimeType(): string {
  const types = ['video/webm;codecs=vp9', 'video/webm;codecs=vp8', 'video/webm'];
  for (const type of types) {
    if (MediaRecorder.isTypeSupported(type)) return type;
  }
  return 'video/webm';
}

export function getAudioMimeType(): string {
  const types = ['audio/webm;codecs=opus', 'audio/webm'];
  for (const type of types) {
    if (MediaRecorder.isTypeSupported(type)) return type;
  }
  return 'audio/webm';
}

export async function recordStreamToBlob(
  stream: MediaStream,
  mimeType: string
): Promise<Blob> {
  return new Promise((resolve, reject) => {
    const chunks: Blob[] = [];
    const recorder = new MediaRecorder(stream, { mimeType });

    recorder.ondataavailable = (e) => {
      if (e.data.size > 0) chunks.push(e.data);
    };

    recorder.onerror = () => reject(new Error('MediaRecorder hatası'));

    recorder.onstop = () => {
      stream.getTracks().forEach((t) => t.stop());
      resolve(new Blob(chunks, { type: mimeType.split(';')[0] }));
    };

    recorder.start(250);

    (recorder as MediaRecorder & { _stop: () => void })._stop = () => {
      if (recorder.state === 'recording') {
        recorder.requestData();
        setTimeout(() => recorder.stop(), 150);
      } else {
        recorder.stop();
      }
    };

    (window as unknown as { __activeRecorders: MediaRecorder[] }).__activeRecorders =
      (window as unknown as { __activeRecorders?: MediaRecorder[] }).__activeRecorders || [];
    (window as unknown as { __activeRecorders: MediaRecorder[] }).__activeRecorders.push(recorder);
  });
}

export function stopAllRecorders(): Promise<void> {
  return new Promise((resolve) => {
    const recorders =
      (window as unknown as { __activeRecorders?: (MediaRecorder & { _stop?: () => void })[] })
        .__activeRecorders || [];
    if (recorders.length === 0) {
      resolve();
      return;
    }
    let remaining = recorders.length;
    const done = () => {
      remaining--;
      if (remaining <= 0) {
        (window as unknown as { __activeRecorders: MediaRecorder[] }).__activeRecorders = [];
        resolve();
      }
    };
    for (const r of recorders) {
      if (r._stop) r._stop();
      else if (r.state === 'recording') {
        r.onstop = done;
        r.stop();
      } else {
        done();
      }
    }
    setTimeout(resolve, 3000);
  });
}

export async function pickDefaultSource(
  mode: string,
  selectedSourceId: string | null
): Promise<string> {
  const sources = await window.electronAPI.getScreenSources();
  if (sources.length === 0) throw new Error('Kaynak bulunamadı');

  if (selectedSourceId) {
    const found = sources.find((s) => s.id === selectedSourceId);
    if (found) return found.id;
  }

  if (mode === 'window') {
    const win = sources.find((s) => s.id.startsWith('window:'));
    if (win) return win.id;
  }

  const screen = sources.find((s) => s.id.startsWith('screen:'));
  return screen?.id || sources[0].id;
}
