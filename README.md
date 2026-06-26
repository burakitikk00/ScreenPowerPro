# ScreenPowerPro

Profesyonel ekran kaydı ve otomatik zoom düzenleme masaüstü uygulaması.

## Özellikler

- **Ekran Kaydı**: Tam ekran, pencere veya özel alan modları
- **Girdi Takibi**: Global mouse ve klavye hareketleri (uiohook-napi)
- **Otomatik Zoom**: Tıklama noktalarından algoritmik zoom efektleri
- **Video Editör**: Timeline üzerinde zoom efektlerini düzenleme
- **FFmpeg Export**: Zoom/pan efektleriyle MP4 dışa aktarma

## Kurulum

```bash
npm install
```

## Geliştirme

```bash
# Terminal 1: Vite dev server
npm run dev

# Terminal 2: Electron
npx electron .
```

Veya tek komutla:

```bash
npm run electron:dev
```

## Derleme

```bash
npm run build
npm run electron:build
```

## Proje Yapısı

Kayıt alındığında şu klasör yapısı oluşturulur:

```
Belgelerim/ScreenPowerPro Projects/Kayıt_2026_06_26/
├── project.myproj
├── recording/
│   ├── display-0.webm
│   ├── metadata.json
│   ├── mouseclicks-0.json
│   ├── mousemoves-0.json
│   └── keystrokes-0.json
└── bundle/
```

## Teknoloji

- Electron.js
- React + Tailwind CSS
- Zustand
- FFmpeg (ffmpeg-static)
- uiohook-napi
