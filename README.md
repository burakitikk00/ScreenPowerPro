# ScreenPowerPro

Profesyonel ekran kaydı ve otomatik zoom düzenleme masaüstü uygulaması.

> 📖 **Detaylı Çift Proje Rehberi**: Hem modern **.NET 9 / C# (WinUI 3)** ana sürümünü hem de **Electron.js / React** yedek sürümünü karşılaştırmalı incelemek için [PROJECTS_OVERVIEW.md](file:///c:/Users/burak/ScreenPowerPro/PROJECTS_OVERVIEW.md) dosyasını inceleyebilirsiniz.

---

## 📌 Proje Sürümleri

Bu depoda iki ayrı mimari bulunmaktadır:

1. **🔷 ScreenPowerPro (`ScreenPowerPro/`) [Ana / Aktif Versiyon]**:
   - **Teknoloji**: .NET 9, C# 13, Windows App SDK (WinUI 3), MVVM, NAudio, FFmpeg CLI, Win32 P/Invoke.
   - **Özellikler**: 60 FPS yerel ekran kaydı, donanım hızlandırmalı şeffaf kesikli mavi kayıt çerçevesi, düşük seviyeli fare/klavye kancaları, akıllı `ZoomEngineService` (cluster tabanlı keyframe zoom), çok kanallı video editörü ve sıfır gecikmeli WASAPI ses kaydı.
   - **Çalıştırma**: Visual Studio 2022 veya `dotnet run` (dizin: `ScreenPowerPro/`).

2. **🔶 ScreenPowerPro Backup (`_electron_backup/`) [Arşiv / Web Versiyonu]**:
   - **Teknoloji**: Electron 33, React 18, Vite, Tailwind CSS, Zustand, uiohook-napi, fluent-ffmpeg.
   - **Özellikler**: WebRTC tabanlı kayıt prototipi, React tabanlı timeline ve önizleme simülasyonu.
   - **Çalıştırma**: `npm run electron:dev` (dizin: `_electron_backup/`).

---

## 🚀 Hızlı Başlangıç

### 🔷 C# .NET 9 Sürümünü Başlatma (Önerilen)
```powershell
cd ScreenPowerPro
dotnet restore
dotnet run
```

### 🔶 Electron Sürümünü Başlatma
```bash
cd _electron_backup
npm install
npm run electron:dev
```

---

Daha fazla teknik detay, mimari şemalar ve modül açıklamaları için:
👉 **[PROJECTS_OVERVIEW.md](file:///c:/Users/burak/ScreenPowerPro/PROJECTS_OVERVIEW.md)**

