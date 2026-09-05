import * as path from 'path';
import * as fs from 'fs';
import ffmpeg from 'fluent-ffmpeg';
import ffmpegPath from 'ffmpeg-static';
import type { ProjectManifest, RecordingMetadata, ExportProgress } from '../../shared/types';

ffmpeg.setFfmpegPath(ffmpegPath as string);

let currentCommand: ffmpeg.FfmpegCommand | null = null;

function buildZoompanFilter(
  manifest: ProjectManifest,
  metadata: RecordingMetadata,
  fps: number
): string {
  const { zoomEffects } = manifest.timeline;
  const w = metadata.width;
  const h = metadata.height;

  if (zoomEffects.length === 0) {
    return `scale=${w}:${h}`;
  }

  const parts = zoomEffects.map((e) => {
    const end = e.startTime + e.duration;
    const cx = Math.round(e.targetX - w / (2 * e.scale));
    const cy = Math.round(e.targetY - h / (2 * e.scale));
    return {
      start: e.startTime,
      end,
      scale: e.scale,
      x: cx,
      y: cy,
    };
  });

  let zExpr = '1';
  let xExpr = 'iw/2-(iw/zoom/2)';
  let yExpr = 'ih/2-(ih/zoom/2)';

  for (const p of parts.reverse()) {
    zExpr = `if(between(t,${p.start.toFixed(3)},${p.end.toFixed(3)}),${p.scale},${zExpr})`;
    xExpr = `if(between(t,${p.start.toFixed(3)},${p.end.toFixed(3)}),${p.x},${xExpr})`;
    yExpr = `if(between(t,${p.start.toFixed(3)},${p.end.toFixed(3)}),${p.y},${yExpr})`;
  }

  return `zoompan=z='${zExpr}':x='${xExpr}':y='${yExpr}':d=1:s=${w}x${h}:fps=${fps}`;
}

export function startExport(
  projectPath: string,
  manifest: ProjectManifest,
  metadata: RecordingMetadata,
  fps: number,
  onProgress: (p: ExportProgress) => void
): Promise<string> {
  return new Promise((resolve, reject) => {
    const videoInput = path.join(projectPath, manifest.videoPath.replace('./', ''));
    const bundleDir = path.join(projectPath, 'bundle');
    const outputName = `${manifest.name || 'export'}_${Date.now()}.mp4`;
    const outputPath = path.join(bundleDir, outputName);

    if (!fs.existsSync(videoInput)) {
      reject(new Error(`Video dosyası bulunamadı: ${videoInput}`));
      return;
    }

    fs.mkdirSync(bundleDir, { recursive: true });

    const filter = buildZoompanFilter(manifest, metadata, fps);
    const micPath = manifest.microphonePath
      ? path.join(projectPath, manifest.microphonePath.replace('./', ''))
      : null;

    let command = ffmpeg(videoInput);

    if (micPath && fs.existsSync(micPath)) {
      command = command.input(micPath);
    }

    const videoFilter = filter;
    command = command.videoFilters(videoFilter);

    if (micPath && fs.existsSync(micPath)) {
      command = command.outputOptions(['-map', '0:v', '-map', '1:a', '-c:a', 'aac', '-shortest']);
    }

    command
      .outputOptions(['-c:v', 'libx264', '-preset', 'medium', '-crf', '23', '-pix_fmt', 'yuv420p'])
      .output(outputPath)
      .on('progress', (progress) => {
        const percent = Math.min(99, Math.round(progress.percent || 0));
        onProgress({
          percent,
          status: 'Rendering video...',
          estimatedSeconds: undefined,
        });
      })
      .on('end', () => {
        currentCommand = null;
        onProgress({ percent: 100, status: 'Complete' });
        resolve(outputPath);
      })
      .on('error', (err) => {
        currentCommand = null;
        if ((err as Error).message?.includes('SIGKILL')) {
          reject(new Error('Export cancelled'));
        } else {
          reject(err);
        }
      });

    currentCommand = command;
    command.run();
  });
}

export function cancelExport(): void {
  if (currentCommand) {
    currentCommand.kill('SIGKILL');
    currentCommand = null;
  }
}
