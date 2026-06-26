import { useEditorStore } from '../../stores/editorStore';
import type { CursorSmoothing } from '../../types';

export default function PropertiesPanel() {
  const { settings, updateSettings, zoomEffects, selectedZoomId, updateZoomEffect } =
    useEditorStore();

  const selectedZoom = zoomEffects.find((z) => z.id === selectedZoomId);

  return (
    <aside className="w-[320px] bg-[#1e1e26] border-r border-white/5 flex flex-col overflow-y-auto z-10 shrink-0">
      <div className="flex flex-col gap-8 p-6">
        <section className="flex flex-col gap-4">
          <h3 className="font-label-md text-label-md text-on-surface-variant uppercase tracking-wider">
            İmleç & Bulanıklık
          </h3>
          <div className="bg-surface-container-high rounded-lg p-4 border border-white/5 flex flex-col gap-4">
            <div className="flex flex-col gap-2">
              <div className="flex justify-between items-center">
                <label className="text-on-surface text-sm">Zoom Seviyesi</label>
                <span className="font-label-sm text-label-sm text-primary bg-primary/10 px-2 py-0.5 rounded">
                  {Math.round((selectedZoom?.scale ?? settings.defaultZoomScale) * 100)}%
                </span>
              </div>
              <input
                type="range"
                min={100}
                max={300}
                value={(selectedZoom?.scale ?? settings.defaultZoomScale) * 100}
                onChange={(e) => {
                  const scale = Number(e.target.value) / 100;
                  if (selectedZoom) {
                    updateZoomEffect(selectedZoom.id, { scale });
                  } else {
                    updateSettings({ defaultZoomScale: scale });
                  }
                }}
              />
            </div>
            <div className="flex flex-col gap-2">
              <div className="flex justify-between items-center">
                <label className="text-on-surface text-sm">Hareket Bulanıklığı</label>
                <span className="font-label-sm text-label-sm text-on-surface-variant bg-white/5 px-2 py-0.5 rounded">
                  {settings.motionBlurAmount}%
                </span>
              </div>
              <input
                type="range"
                min={0}
                max={100}
                value={settings.motionBlurAmount}
                onChange={(e) =>
                  updateSettings({ motionBlurAmount: Number(e.target.value), motionBlur: true })
                }
              />
            </div>
          </div>
        </section>

        <section className="flex flex-col gap-4">
          <h3 className="font-label-md text-label-md text-on-surface-variant uppercase tracking-wider">
            İmleç Hareketi
          </h3>
          <div className="bg-surface-container-high rounded-lg p-1 border border-white/5 flex">
            {(['slow', 'medium', 'fast'] as CursorSmoothing[]).map((speed) => (
              <button
                key={speed}
                onClick={() => updateSettings({ cursorSmoothing: speed })}
                className={`flex-1 py-1.5 font-label-sm text-label-sm rounded-md transition-colors capitalize ${
                  settings.cursorSmoothing === speed
                    ? 'bg-surface-container-lowest text-primary shadow-sm border border-white/5'
                    : 'text-on-surface-variant hover:text-on-surface'
                }`}
              >
                {speed === 'slow' ? 'Yavaş' : speed === 'medium' ? 'Orta' : 'Hızlı'}
              </button>
            ))}
          </div>
        </section>

        <section className="flex flex-col gap-4">
          <h3 className="font-label-md text-label-md text-on-surface-variant uppercase tracking-wider">
            Stil
          </h3>
          <div className="bg-surface-container-high rounded-lg p-4 border border-white/5 flex flex-col gap-4">
            <div className="flex items-center justify-between">
              <span className="text-on-surface text-sm">Kısayol Tuşlarını Göster</span>
              <label className="relative inline-flex items-center cursor-pointer">
                <input
                  type="checkbox"
                  className="sr-only peer"
                  checked={settings.showShortcutKeys}
                  onChange={(e) => updateSettings({ showShortcutKeys: e.target.checked })}
                />
                <div className="w-9 h-5 bg-surface-bright rounded-full peer peer-checked:bg-primary after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:after:translate-x-4" />
              </label>
            </div>
            <hr className="border-white/5" />
            <div className="flex flex-col gap-3">
              <span className="text-on-surface text-sm">Arkaplan Rengi</span>
              <div className="flex gap-2">
                {['#000000', '#1A1A1A', '#1e1f27'].map((color) => (
                  <button
                    key={color}
                    onClick={() => updateSettings({ canvasBackground: color })}
                    className={`w-6 h-6 rounded-full transition-all ${
                      settings.canvasBackground === color
                        ? 'ring-2 ring-primary ring-offset-2 ring-offset-surface-container-high'
                        : 'hover:ring-2 ring-white/20 ring-offset-2 ring-offset-surface-container-high'
                    }`}
                    style={{ backgroundColor: color }}
                  />
                ))}
              </div>
            </div>
          </div>
        </section>
      </div>
    </aside>
  );
}
