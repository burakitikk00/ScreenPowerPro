import { useEditorStore } from '../../stores/editorStore';
import type { CursorSmoothing } from '../../types';

export default function PropertiesPanel() {
  const { activeSidebarTab, settings, updateSettings, zoomEffects, selectedZoomId, updateZoomEffect } = useEditorStore();

  const selectedZoom = zoomEffects.find((z) => z.id === selectedZoomId);

  const renderContent = () => {
    switch (activeSidebarTab) {
      case 'motion':
        return (
          <section className="flex flex-col gap-4">
            <h3 className="font-label-md text-label-md text-on-surface-variant uppercase tracking-wider">
              Hareket Bulanıklığı (Motion Blur)
            </h3>
            <div className="bg-surface-container-high rounded-lg p-4 border border-white/5 flex flex-col gap-4">
              <div className="flex items-center justify-between">
                <span className="text-on-surface text-sm">Aktif Et</span>
                <label className="relative inline-flex items-center cursor-pointer">
                  <input
                    type="checkbox"
                    className="sr-only peer"
                    checked={settings.motionBlur}
                    onChange={(e) => updateSettings({ motionBlur: e.target.checked })}
                  />
                  <div className="w-9 h-5 bg-surface-bright rounded-full peer peer-checked:bg-primary after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:after:translate-x-4" />
                </label>
              </div>
              <div className="flex flex-col gap-2">
                <div className="flex justify-between items-center">
                  <label className="text-on-surface text-sm">Miktar</label>
                  <span className="font-label-sm text-label-sm text-on-surface-variant bg-white/5 px-2 py-0.5 rounded">
                    {settings.motionBlurAmount ?? 50}%
                  </span>
                </div>
                <input
                  type="range"
                  min={0}
                  max={100}
                  value={settings.motionBlurAmount ?? 50}
                  onChange={(e) => updateSettings({ motionBlurAmount: Number(e.target.value) })}
                />
              </div>
            </div>
          </section>
        );

      case 'cursor':
        return (
          <section className="flex flex-col gap-4">
            <h3 className="font-label-md text-label-md text-on-surface-variant uppercase tracking-wider">
              İmleç Ayarları
            </h3>
            <div className="bg-surface-container-high rounded-lg p-4 border border-white/5 flex flex-col gap-4">
              <div className="flex items-center justify-between">
                <span className="text-on-surface text-sm">Görünürlük</span>
                <label className="relative inline-flex items-center cursor-pointer">
                  <input
                    type="checkbox"
                    className="sr-only peer"
                    checked={settings.cursorVisible !== false}
                    onChange={(e) => updateSettings({ cursorVisible: e.target.checked })}
                  />
                  <div className="w-9 h-5 bg-surface-bright rounded-full peer peer-checked:bg-primary after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:after:translate-x-4" />
                </label>
              </div>
              <div className="flex flex-col gap-2">
                <div className="flex justify-between items-center">
                  <label className="text-on-surface text-sm">Boyut</label>
                  <span className="font-label-sm text-label-sm text-on-surface-variant bg-white/5 px-2 py-0.5 rounded">
                    {settings.cursorSize ?? 100}%
                  </span>
                </div>
                <input
                  type="range"
                  min={50}
                  max={200}
                  value={settings.cursorSize ?? 100}
                  onChange={(e) => updateSettings({ cursorSize: Number(e.target.value) })}
                />
              </div>
              <div className="flex flex-col gap-2 mt-2">
                <label className="text-on-surface text-sm">Yumuşatma (Smoothing)</label>
                <div className="bg-surface-container-lowest rounded-lg p-1 border border-white/5 flex">
                  {(['slow', 'medium', 'fast'] as CursorSmoothing[]).map((speed) => (
                    <button
                      key={speed}
                      onClick={() => updateSettings({ cursorSmoothing: speed })}
                      className={`flex-1 py-1.5 font-label-sm text-label-sm rounded-md transition-colors capitalize ${
                        settings.cursorSmoothing === speed
                          ? 'bg-primary/20 text-primary border border-primary/30'
                          : 'text-on-surface-variant hover:text-on-surface'
                      }`}
                    >
                      {speed === 'slow' ? 'Yavaş' : speed === 'medium' ? 'Orta' : 'Hızlı'}
                    </button>
                  ))}
                </div>
              </div>
            </div>
          </section>
        );

      case 'zoom':
        return (
          <section className="flex flex-col gap-4">
            <h3 className="font-label-md text-label-md text-on-surface-variant uppercase tracking-wider">
              Zoom & Pan Ayarları
            </h3>
            <div className="bg-surface-container-high rounded-lg p-4 border border-white/5 flex flex-col gap-4">
              <div className="flex flex-col gap-2">
                <div className="flex justify-between items-center">
                  <label className="text-on-surface text-sm">Varsayılan Zoom Seviyesi</label>
                  <span className="font-label-sm text-label-sm text-primary bg-primary/10 px-2 py-0.5 rounded">
                    {Math.round((settings.defaultZoomScale ?? 1.5) * 100)}%
                  </span>
                </div>
                <input
                  type="range"
                  min={100}
                  max={300}
                  value={(settings.defaultZoomScale ?? 1.5) * 100}
                  onChange={(e) => {
                    const scale = Number(e.target.value) / 100;
                    updateSettings({ defaultZoomScale: scale });
                  }}
                />
              </div>
              {selectedZoom && (
                <div className="mt-4 p-3 border border-tertiary/30 bg-tertiary/5 rounded-lg flex flex-col gap-2">
                  <span className="text-tertiary text-xs font-semibold uppercase tracking-wider">Seçili Efekt</span>
                  <div className="flex justify-between items-center mt-2">
                    <label className="text-on-surface text-sm">Özel Scale</label>
                    <span className="font-label-sm text-label-sm text-tertiary bg-tertiary/10 px-2 py-0.5 rounded">
                      {Math.round(selectedZoom.scale * 100)}%
                    </span>
                  </div>
                  <input
                    type="range"
                    min={100}
                    max={300}
                    value={selectedZoom.scale * 100}
                    onChange={(e) => {
                      const scale = Number(e.target.value) / 100;
                      updateZoomEffect(selectedZoom.id, { scale });
                    }}
                  />
                </div>
              )}
            </div>
          </section>
        );

      case 'camera':
        return (
          <section className="flex flex-col gap-4">
            <h3 className="font-label-md text-label-md text-on-surface-variant uppercase tracking-wider">
              Kamera Özellikleri
            </h3>
            <div className="bg-surface-container-high rounded-lg p-4 border border-white/5 flex flex-col gap-4">
              <div className="flex items-center justify-between">
                <span className="text-on-surface text-sm">Görünürlük</span>
                <label className="relative inline-flex items-center cursor-pointer">
                  <input
                    type="checkbox"
                    className="sr-only peer"
                    checked={settings.cameraVisible ?? true}
                    onChange={(e) => updateSettings({ cameraVisible: e.target.checked })}
                  />
                  <div className="w-9 h-5 bg-surface-bright rounded-full peer peer-checked:bg-primary after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:after:translate-x-4" />
                </label>
              </div>
              <div className="flex flex-col gap-2 mt-2">
                <label className="text-on-surface text-sm">Şekil</label>
                <div className="bg-surface-container-lowest rounded-lg p-1 border border-white/5 flex">
                  {(['circle', 'square'] as const).map((shape) => (
                    <button
                      key={shape}
                      onClick={() => updateSettings({ cameraShape: shape })}
                      className={`flex-1 py-1.5 font-label-sm text-label-sm rounded-md transition-colors capitalize ${
                        settings.cameraShape === shape || (!settings.cameraShape && shape === 'circle')
                          ? 'bg-primary/20 text-primary border border-primary/30'
                          : 'text-on-surface-variant hover:text-on-surface'
                      }`}
                    >
                      {shape === 'circle' ? 'Daire' : 'Kare'}
                    </button>
                  ))}
                </div>
              </div>
              <div className="flex flex-col gap-2 mt-2">
                <div className="flex justify-between items-center">
                  <label className="text-on-surface text-sm">Boyut</label>
                  <span className="font-label-sm text-label-sm text-on-surface-variant bg-white/5 px-2 py-0.5 rounded">
                    {settings.cameraSize ?? 100}%
                  </span>
                </div>
                <input
                  type="range"
                  min={50}
                  max={200}
                  value={settings.cameraSize ?? 100}
                  onChange={(e) => updateSettings({ cameraSize: Number(e.target.value) })}
                />
              </div>
            </div>
          </section>
        );

      case 'audio':
        return (
          <section className="flex flex-col gap-4">
            <h3 className="font-label-md text-label-md text-on-surface-variant uppercase tracking-wider">
              Ses Ayarları
            </h3>
            <div className="bg-surface-container-high rounded-lg p-4 border border-white/5 flex flex-col gap-4">
              <div className="flex flex-col gap-2">
                <div className="flex justify-between items-center">
                  <label className="text-on-surface text-sm">Sistem Sesi</label>
                  <span className="font-label-sm text-label-sm text-on-surface-variant bg-white/5 px-2 py-0.5 rounded">
                    {settings.sysVolume ?? 100}%
                  </span>
                </div>
                <input
                  type="range"
                  min={0}
                  max={200}
                  value={settings.sysVolume ?? 100}
                  onChange={(e) => updateSettings({ sysVolume: Number(e.target.value) })}
                />
              </div>
              <hr className="border-white/5" />
              <div className="flex flex-col gap-2">
                <div className="flex justify-between items-center">
                  <label className="text-on-surface text-sm">Mikrofon</label>
                  <span className="font-label-sm text-label-sm text-on-surface-variant bg-white/5 px-2 py-0.5 rounded">
                    {settings.micVolume ?? 100}%
                  </span>
                </div>
                <input
                  type="range"
                  min={0}
                  max={200}
                  value={settings.micVolume ?? 100}
                  onChange={(e) => updateSettings({ micVolume: Number(e.target.value) })}
                />
              </div>
            </div>
          </section>
        );

      case 'keyboard':
        return (
          <section className="flex flex-col gap-4">
            <h3 className="font-label-md text-label-md text-on-surface-variant uppercase tracking-wider">
              Klavye Kısayolları
            </h3>
            <div className="bg-surface-container-high rounded-lg p-4 border border-white/5 flex flex-col gap-4">
              <div className="flex items-center justify-between">
                <span className="text-on-surface text-sm">Kısayolları Göster</span>
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
            </div>
          </section>
        );

      case 'bg':
        return (
          <section className="flex flex-col gap-4">
            <h3 className="font-label-md text-label-md text-on-surface-variant uppercase tracking-wider">
              Arkaplan (Background)
            </h3>
            <div className="bg-surface-container-high rounded-lg p-4 border border-white/5 flex flex-col gap-4">
              <div className="flex flex-col gap-3">
                <span className="text-on-surface text-sm">Renk Seçimi</span>
                <div className="flex gap-2">
                  {['#000000', '#1A1A1A', '#1e1f27', '#2a2b3d'].map((color) => (
                    <button
                      key={color}
                      onClick={() => updateSettings({ canvasBackground: color })}
                      className={`w-8 h-8 rounded-full transition-all ${
                        settings.canvasBackground === color
                          ? 'ring-2 ring-primary ring-offset-2 ring-offset-surface-container-high scale-110'
                          : 'hover:ring-2 ring-white/20 ring-offset-2 ring-offset-surface-container-high hover:scale-105'
                      }`}
                      style={{ backgroundColor: color }}
                    />
                  ))}
                </div>
              </div>
            </div>
          </section>
        );

      case 'watermark':
        return (
          <section className="flex flex-col gap-4">
            <h3 className="font-label-md text-label-md text-on-surface-variant uppercase tracking-wider">
              Filigran (Watermark)
            </h3>
            <div className="bg-surface-container-high rounded-lg p-4 border border-white/5 flex flex-col gap-4">
              <div className="flex items-center justify-between">
                <span className="text-on-surface text-sm">Göster</span>
                <label className="relative inline-flex items-center cursor-pointer">
                  <input
                    type="checkbox"
                    className="sr-only peer"
                    checked={settings.watermark}
                    onChange={(e) => updateSettings({ watermark: e.target.checked })}
                  />
                  <div className="w-9 h-5 bg-surface-bright rounded-full peer peer-checked:bg-primary after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:after:translate-x-4" />
                </label>
              </div>
              {settings.watermark && (
                <div className="flex flex-col gap-2 mt-2">
                  <label className="text-on-surface text-sm">Metin</label>
                  <input
                    type="text"
                    className="bg-surface-container-lowest border border-white/10 rounded-md px-3 py-1.5 text-sm text-on-surface focus:border-primary outline-none"
                    value={settings.watermarkText || 'ScreenPowerPro'}
                    onChange={(e) => updateSettings({ watermarkText: e.target.value })}
                    placeholder="Filigran metni..."
                  />
                </div>
              )}
            </div>
          </section>
        );

      default:
        return null;
    }
  };

  return (
    <aside className="w-[320px] bg-[#1e1e26] border-r border-white/5 flex flex-col overflow-y-auto z-10 shrink-0">
      <div className="flex flex-col gap-8 p-6">
        {/* Hız ayarını genel olarak en üste ekleyebiliriz veya ayrı bir panel yapabiliriz */}
        <section className="flex flex-col gap-4 border-b border-white/5 pb-6">
          <h3 className="font-label-md text-label-md text-on-surface-variant uppercase tracking-wider">
            Video Hızı
          </h3>
          <div className="bg-surface-container-high rounded-lg p-1 border border-white/5 flex">
            {([0.5, 1, 1.5, 2] as const).map((speed) => (
              <button
                key={speed}
                onClick={() => updateSettings({ videoSpeed: speed })}
                className={`flex-1 py-1.5 font-label-sm text-label-sm rounded-md transition-colors ${
                  (settings.videoSpeed ?? 1) === speed
                    ? 'bg-primary/20 text-primary border border-primary/30'
                    : 'text-on-surface-variant hover:text-on-surface'
                }`}
              >
                {speed}x
              </button>
            ))}
          </div>
        </section>

        {renderContent()}
      </div>
    </aside>
  );
}
