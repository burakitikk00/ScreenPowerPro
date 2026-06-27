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
          <section className="flex flex-col gap-6 text-sm text-[#a3a3a3] px-2 py-2">
            
            {/* Show Cursor */}
            <div className="flex items-center justify-between">
              <span className="text-[13px]">Show Cursor</span>
              <label className="relative inline-flex items-center cursor-pointer">
                <input
                  type="checkbox"
                  className="sr-only peer"
                  checked={settings.cursorVisible !== false}
                  onChange={(e) => updateSettings({ cursorVisible: e.target.checked })}
                />
                <div className="w-[34px] h-[20px] bg-[#3a3a3c] rounded-full peer peer-checked:bg-[#2e5cff] after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:rounded-full after:h-[16px] after:w-[16px] after:transition-all peer-checked:after:translate-x-[14px]" />
              </label>
            </div>

            {/* Cursor Size */}
            <div className="flex flex-col gap-3">
              <div className="flex justify-between items-center">
                <span className="text-[13px]">Cursor Size</span>
                <div className="flex items-center gap-2">
                  <button 
                    onClick={() => updateSettings({ cursorSize: 100 })}
                    className="text-[#2e5cff] hover:text-blue-400 font-medium text-[12px]"
                  >
                    Reset
                  </button>
                  <span className="font-medium text-white w-8 text-right text-[12px]">
                    {((settings.cursorSize ?? 100) / 100).toFixed(1)}x
                  </span>
                </div>
              </div>
              <input
                type="range"
                min={50}
                max={300}
                value={settings.cursorSize ?? 100}
                onChange={(e) => updateSettings({ cursorSize: Number(e.target.value) })}
                className="w-full h-1 bg-[#3a3a3c] rounded-lg appearance-none cursor-pointer accent-[#2e5cff]"
              />
            </div>

            {/* Cursor Style */}
            <div className="flex flex-col gap-3 mt-1">
              <div className="flex items-center gap-1">
                <span className="text-[13px]">Cursor Style</span>
                <span className="material-symbols-outlined text-[14px] text-[#6b6b6b]">info</span>
              </div>
              <div className="grid grid-cols-5 gap-2">
                {[
                  { id: 'style_1', icon: 'arrow_selector_tool', color: 'text-white' },
                  { id: 'style_2', icon: 'navigation', color: 'text-[#6b6b6b]' },
                  { id: 'style_3', icon: 'touch_app', color: 'text-[#6b6b6b]' },
                  { id: 'style_4', icon: 'more_horiz', color: 'text-[#6b6b6b]' },
                  { id: 'style_none', icon: 'block', color: 'text-[#6b6b6b]' },
                  { id: 'default', icon: 'arrow_selector_tool', color: 'text-white' },
                  { id: 'style_6', icon: 'arrow_selector_tool', outline: true, color: 'text-white' },
                  { id: 'style_7', icon: 'arrow_selector_tool', color: 'text-[#ff9800]' },
                  { id: 'style_8', icon: 'arrow_selector_tool', color: 'text-[#9c27b0]' },
                  { id: 'style_9', icon: 'arrow_selector_tool', color: 'text-[#03a9f4]' }
                ].map((style) => (
                  <button
                    key={style.id}
                    onClick={() => updateSettings({ cursorStyle: style.id })}
                    className={`flex items-center justify-center aspect-[5/4] rounded-lg transition-all border ${
                      settings.cursorStyle === style.id
                        ? 'bg-[#2e5cff]/20 border-[#2e5cff]'
                        : 'bg-transparent border-[#2c2c2e] hover:bg-[#2c2c2e]'
                    }`}
                  >
                    <span className={`material-symbols-outlined text-[20px] ${style.color} ${style.outline ? 'opacity-70' : ''}`}>
                      {style.icon}
                    </span>
                  </button>
                ))}
              </div>
            </div>

            {/* Click Effect */}
            <div className="flex flex-col gap-3 mt-3">
              <span className="text-[13px]">Click Effect</span>
              <div className="grid grid-cols-3 gap-2">
                {[
                  { id: 'none', label: 'None', icon: 'block' },
                  { id: 'default', label: 'Default', icon: 'arrow_selector_tool' },
                  { id: 'ripple', label: 'Ripple', icon: 'radio_button_unchecked' },
                  { id: 'ring', label: 'Ring', icon: 'trip_origin' },
                  { id: 'diffusion', label: 'Diffusion', icon: 'blur_on' },
                  { id: 'spotlight', label: 'Spotlight', icon: 'highlight' },
                  { id: 'sparkle', label: 'Sparkle', icon: 'auto_awesome' },
                  { id: 'firework', label: 'Firework', icon: 'celebration' },
                  { id: 'christmas', label: 'Christmas', icon: 'park' }
                ].map((effect) => (
                  <button
                    key={effect.id}
                    onClick={() => updateSettings({ clickEffect: effect.id })}
                    className={`flex flex-col items-center justify-center p-3 rounded-xl border transition-all gap-1.5 ${
                      (settings.clickEffect || 'default') === effect.id
                        ? 'bg-[#2e5cff]/20 border-[#2e5cff] text-white'
                        : 'bg-transparent border-[#2c2c2e] hover:bg-[#2c2c2e]'
                    }`}
                  >
                    <span className={`material-symbols-outlined text-[24px] ${(settings.clickEffect || 'default') === effect.id ? 'text-white' : 'text-[#8e8e93]'}`}>{effect.icon}</span>
                    <span className={`text-[11px] font-medium ${(settings.clickEffect || 'default') === effect.id ? 'text-white' : 'text-[#8e8e93]'}`}>{effect.label}</span>
                  </button>
                ))}
              </div>
            </div>

            {/* Cursor Click Sound */}
            <div className="flex items-center justify-between mt-3">
              <span className="text-[13px]">Cursor Click Sound</span>
              <label className="relative inline-flex items-center cursor-pointer">
                <input
                  type="checkbox"
                  className="sr-only peer"
                  checked={settings.cursorClickSound || false}
                  onChange={(e) => updateSettings({ cursorClickSound: e.target.checked })}
                />
                <div className="w-[34px] h-[20px] bg-[#3a3a3c] rounded-full peer peer-checked:bg-[#2e5cff] after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:rounded-full after:h-[16px] after:w-[16px] after:transition-all peer-checked:after:translate-x-[14px]" />
              </label>
            </div>

            {/* Hide Cursor When Idle */}
            <div className="flex items-center justify-between mt-1">
              <div className="flex items-center gap-1">
                <span className="text-[13px]">Hide Cursor When Idle</span>
                <span className="material-symbols-outlined text-[14px] text-[#6b6b6b]">info</span>
              </div>
              <label className="relative inline-flex items-center cursor-pointer">
                <input
                  type="checkbox"
                  className="sr-only peer"
                  checked={settings.hideCursorWhenIdle || false}
                  onChange={(e) => updateSettings({ hideCursorWhenIdle: e.target.checked })}
                />
                <div className="w-[34px] h-[20px] bg-[#3a3a3c] rounded-full peer peer-checked:bg-[#2e5cff] after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-white after:rounded-full after:h-[16px] after:w-[16px] after:transition-all peer-checked:after:translate-x-[14px]" />
              </label>
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
        {renderContent()}
      </div>
    </aside>
  );
}
