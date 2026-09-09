# ScreenPowerPro — Çift Proje Tanıtımı ve Teknik Mimari Rehberi

> Bu doküman, **ScreenPowerPro** projesinin iki farklı versiyonunu (**Modern C# / .NET 9 WinUI 3** ve **Electron.js / React Backup**) bağımsız ve kapsamlı şekilde tanıtmak için hazırlanmıştır. İlgilendiğiniz projenin bölümüne doğrudan atlayarak detaylı bilgi edinebilirsiniz.

---

## 📑 Hızlı Gezinti & İçindekiler

* [🎯 Ortak Vizyon & Proje Amacı](#-ortak-vizyon--proje-amac)
* [⚡ Hızlı Karşılaştırma Tablosu](#-hzl-karlatrma-tablosu)
* [🔷 BÖLÜM 1: ScreenPowerPro — C# & .NET 9 (WinUI 3) [Aktif / Ana Proje]](#-bölüm-1-screenpowerpro--c--net-9-winui-3-aktif--ana-proje)
  * [1.1 Genel Bakış](#11-genel-bak)
  * [1.2 Teknoloji Yığını (Tech Stack)](#12-teknoloji-yn-tech-stack)
  * [1.3 Mimari Tasarım & Prensiple](#13-mimari-tasarm--prensipler)
  * [1.4 Çekirdek Servisler & Yenilikçi Özellikler](#14-ekirdek-servisler--yeniliki-özellikler)
  * [1.5 Özel Pencereler & Kullanıcı Arayüzü](#15-özel-pencereler--kullanc-arayz)
  * [1.6 Klasör Yapısı](#16-klasör-yaps)
  * [1.7 Kurulum & Çalıştırma Rehberi](#17-kurulum--altrma-rehberi)
* [🔶 BÖLÜM 2: ScreenPowerPro — Electron.js & React [Yedek / Backup Proje]](#-bölüm-2-screenpowerpro--electronjs--react-yedek--backup-proje)
  * [2.1 Genel Bakış](#21-genel-bak)
  * [2.2 Teknoloji Yığını (Tech Stack)](#22-teknoloji-yn-tech-stack-1)
  * [2.3 Çift Süreçli (IPC) Mimari](#23-ift-sreli-ipc-mimari)
  * [2.4 Çekirdek Modüller & Çalışma Mantığı](#24-ekirdek-modller--altrma-mant)
  * [2.5 Klasör Yapısı](#25-klasör-yaps)
  * [2.6 Kurulum & Çalıştırma Rehberi](#26-kurulum--altrma-rehberi-1)
* [⚖️ BÖLÜM 3: Kapsamlı Karşılaştırma — Neden C# Tercih Edildi?](#️-bölüm-3-kapsaml-karlatrma--neden-c-tercih-edildi)
* [📦 BÖLÜM 4: Ortak Proje Formatı & Veri Modelleri](#-bölüm-4-ortak-proje-format--veri-modelleri)

---

## 🎯 Ortak Vizyon & Proje Amacı

**ScreenPowerPro**, ekran kaydı alan içerik üreticileri, yazılımcılar ve eğitimciler için geliştirilmiş yeni nesil bir **otomasyonlu ekran kayıt ve düzenleme** aracıdır.

- **Otomatik Zoom & Pan**: Kullanıcı ekran kaydı yaparken fare tıklamaları ve klavye hareketleri arka planda yüksek hassasiyetle telemetri olarak kaydedilir.
- **Akıllı Düzenleme**: Kayıt bittiğinde, tıklama yoğunluğuna ve odak noktalarına göre yapay zekaya ihtiyaç duymadan **matematiksel ve algoritmik olarak** otomatik yakınlaşma (zoom) ve kaydırma (pan) efektleri uygulanır.
- **Kişiselleştirilebilir Timeline**: Kullanıcı entegre editörde bu zoom noktalarını, zaman aralıklarını, ses kanallarını ve geçiş yumuşatmalarını dilediği gibi düzenleyip tek tıkla yüksek kalitede MP4 olarak dışa aktarabilir.

---

## ⚡ Hızlı Karşılaştırma Tablosu

| Kriter | 🔷 C# / .NET 9 Versiyonu (`ScreenPowerPro`) | 🔶 Electron Backup (`_electron_backup`) |
| :--- | :--- | :--- |
| **Durum** | **Aktif Ana Geliştirme Dalı** | Arşiv / Referans Web Versiyonu |
| **Çatı / Framework** | .NET 9 (`net9.0-windows10.0.26100.0`) + WinUI 3 | Electron 33 + Vite |
| **Arayüz (UI)** | XAML + Windows App SDK 2.4 | React 18 + Tailwind CSS 3 |
| **Durum Yönetimi** | CommunityToolkit.Mvvm (ObservableObject) | Zustand (appStore, editorStore) |
| **Ekran Kaydı** | FFmpeg `gdigrab` (Yerel Process, 60 FPS) | WebRTC `desktopCapturer` + MediaRecorder |
| **Ses Kaydı** | NAudio (WASAPI Loopback + WaveIn) | WebRTC `getUserMedia` |
| **Girdi Takibi** | Win32 Düşük Seviyeli Kancalar (P/Invoke) | `uiohook-napi` (Node Native Addon) |
| **Şeffaf Çerçeve** | Donanım Hızlandırmalı `WS_EX_LAYERED` Win32 | HTML5 Canvas / Electron Frameless Window |
| **Kayıttan UI Gizleme** | `WDA_EXCLUDEFROMCAPTURE` (Sıfır ekrandan parazit) | Kısmi / CSS Tabanlı Gizleme |
| **Bellek & CPU** | Çok Düşük (~80-150 MB RAM, Düşük CPU) | Orta/Yüksek (~300-600 MB RAM, Chromium yükü) |
| **Platform** | Windows 10/11 (Derin API Entegrasyonu) | Cross-Platform (Windows & macOS hedeflenmiş) |

---

# 🔷 BÖLÜM 1: ScreenPowerPro — C# & .NET 9 (WinUI 3) [Aktif / Ana Proje]

Ana proje dizini: `ScreenPowerPro/`

### 1.1 Genel Bakış
C# sürümü, Windows işletim sisteminin tüm modern grafik ve API yeteneklerinden sonuna kadar faydalanmak üzere baştan inşa edilmiştir. Chromium/Electron katmanının getirdiği bellek şişmesini ortadan kaldırarak 60 FPS akıcı kayıt, piksel düzeyinde şeffaflık, sıfır gecikmeli ses kaydı ve profesyonel timeline düzenleme sunar.

### 1.2 Teknoloji Yığını (Tech Stack)
- **Çalışma Zamanı**: .NET 9 (Windows SDK 10.0.26100.0)
- **Kullanıcı Arayüzü**: WinUI 3 (Windows App SDK 2.4) & XAML
- **Mimari Desen**: MVVM (Model-View-ViewModel) + CommunityToolkit.Mvvm 8.4.2
- **Bağımlılık Enjeksiyonu**: Microsoft.Extensions.DependencyInjection 10.0
- **Ses Kütüphanesi**: NAudio 3.0.1 (WASAPI Loopback & WaveIn)
- **Grafik & Çizim**: System.Drawing.Common + Win32 GDI+ P/Invoke
- **Video Motoru**: Yerel FFmpeg CLI Entegrasyonu (gdigrab, libx264, aac, filter-complex zoompan)
- **İşletim Sistemi Entegrasyonu**: User32, Gdi32, DwmApi, Shell32 Win32 API çağrıları

### 1.3 Mimari Tasarım & Prensipler
```
┌─────────────────────────────────────────────────────────────┐
│                    WinUI 3 XAML Arayüzü                     │
│  DashboardPage | EditorPage | ExportPage | FloatingToolbar  │
└──────────────────────────────┬──────────────────────────────┘
                               │ Data Binding & Commands
┌──────────────────────────────▼──────────────────────────────┐
│                    MVVM ViewModels Layer                    │
│    DashboardViewModel, EditorViewModel, SettingsViewModel   │
└──────────────────────────────┬──────────────────────────────┘
                               │ DI / Service Injection
┌──────────────────────────────▼──────────────────────────────┐
│                       Servis Katmanı                        │
│ ┌──────────────────────────┐   ┌──────────────────────────┐ │
│ │  ScreenRecorderService   │   │     ZoomEngineService    │ │
│ │  (FFmpeg gdigrab 60fps)  │   │  (Keyframe/Cubic Spline) │ │
│ └──────────────────────────┘   └──────────────────────────┘ │
│ ┌──────────────────────────┐   ┌──────────────────────────┐ │
│ │  InputTrackerService     │   │   AudioLevelMonitor      │ │
│ │  (Win32 Low-Level Hooks) │   │  (NAudio WASAPI Audio)   │ │
│ └──────────────────────────┘   └──────────────────────────┘ │
│ ┌──────────────────────────┐   ┌──────────────────────────┐ │
│ │      ProjectService      │   │      ExportService       │ │
│ │ (.myproj JSON Yönetimi)  │   │   (FFmpeg Filtre Motoru) │ │
│ └──────────────────────────┘   └──────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### 1.4 Çekirdek Servisler & Yenilikçi Özellikler

#### 1. Ekran Kaydı (`ScreenRecorderService.cs`)
- Ekranın tamamını, belirli bir uygulama penceresini veya kullanıcının seçtiği özel bir koordinat aralığını (`Custom Crop`) FFmpeg `gdigrab` ile doğrudan yakalar.
- Donanım kodlama desteği (NVENC / Intel QSV / AMD AMF) ile minimum işlemci yüküyle kayıt alır.

#### 2. Düşük Seviyeli Girdi Takipçisi (`InputTrackerService.cs`)
- `SetWindowsHookEx` ile `WH_MOUSE_LL` ve `WH_KEYBOARD_LL` işletim sistemi kancaları kurar.
- Tıklamaların X, Y koordinatlarını, farenin hareket izlerini ve basılan tuşları milisaniye cinsinden kaydeder (`mouseclicks-0.json`, `mousemoves-0.json`, `keystrokes-0.json`).

#### 3. Matematiksel Zoom & Pan Motoru (`ZoomEngineService.cs`)
- **Kümeleme (Clustering) Algoritması**: Birbirine yakın zaman ve konumlarda gerçekleşen tıklamaları gruplayarak videonun gereksiz yere titremesini engeller.
- **Yumuşak Geçiş Eğrileri**: `easeInOutCubic` ve `smoothstep` enterpolasyonları ile sinematik yumuşaklıkta yakınlaşma ve uzaklaşma sağlar.
- **Fare Takip Modu**: Tıklamadan sonra farenin hareketini belirli bir süre boyunca dinamik olarak takip eder.
- **FFmpeg Filtre Üretimi**: Editördeki zoom parametrelerini doğrudan FFmpeg `zoompan` filtre ifadelerine dönüştürür.

#### 4. Ses Kaydı & Canlı Seviye Takibi (`AudioLevelMonitorService.cs`)
- NAudio kütüphanesini kullanarak hem bilgisayarın dahili sistem sesini (`WasapiLoopbackCapture`) hem de kullanıcının mikrofonunu (`WaveInEvent`) eşzamanlı kaydeder.
- Canlı ses desibel seviyesini ve tepe noktalarını (peak levels) arayüzde VU-metre olarak gösterir.

#### 5. Kapsamlı Video Editörü (`EditorPage.xaml / .xaml.cs`)
- Çok kanallı zaman çizelgesi (Video İzi, Mikrofon İzi, Sistem Sesi İzi, Zoom İzi).
- Tıklama noktalarından otomatik oluşturulan ayarlanabilir zoom blokları.
- Oynatma çubuğu (Playhead) konumunda klip bölme (`Split`), kırpma (`Trim`) ve silme.
- Önizleme ekranında sahte zoom simülasyonu ile anlık görsel denetim.

### 1.5 Özel Pencereler & Kullanıcı Arayüzü

1. **`FullScreenBorderWindow.cs`**:
   - Win32 `WS_EX_LAYERED` ve `UpdateLayeredWindow` mimarisiyle sıfırdan oluşturulmuş, arkasındaki masaüstünü %100 net gösteren, kesikli çizgili (`dashed`) canlı mavi kayıt alanı göstergesi.
   - `WS_EX_TRANSPARENT` özelliği sayesinde kullanıcı çerçeveye tıklasa bile altındaki masaüstü veya uygulamalarla kesintisiz etkileşime devam edebilir.
   - `WDA_EXCLUDEFROMCAPTURE` API çağrısı ile bu çerçevenin video kaydında çıkması engellenmiştir.
2. **`RecordingSetupToolbarWindow.xaml`**:
   - Ekranın alt-orta kısmında yüzen obsidyen tasarımlı kompakt hazırlık çubuğu.
   - Hızlı ayarlar (2D/3D Zoom, Geri sayım, Masaüstü simgelerini ve görev çubuğunu gizleme), kamera/mikrofon/hoparlör seçimi ve kırmızı kayıt butonu içerir.
3. **`RecordingBarWindow.xaml`**:
   - Kayıt anında beliren minimalist durdur/duraklat/süre kontrol paneli.
4. **`RegionSelectionWindow.xaml`**:
   - Kullanıcının ekranda serbestçe sürükleyip kayıt alanı seçebildiği yarı saydam maskeli alan seçici.
5. **Ek Araçlar**: `TeleprompterWindow` (sunum metni kaydırıcı), `CameraOverlayWindow` (yuvarlak/köşeli kamera balonu), `CountdownWindow` (kayıt öncesi 3-2-1 animasyonu), `MaskOverlayWindow`.

### 1.6 Klasör Yapısı (`ScreenPowerPro/`)
```
ScreenPowerPro/
├── App.xaml / App.xaml.cs          # Uygulama yaşam döngüsü ve DI tanımları
├── MainWindow.xaml / .cs           # Ana pencere kabuğu
├── ScreenPowerPro.csproj           # .NET 9 ve NuGet bağımlılıkları
├── app.manifest                    # DPI-Awareness ve Windows 11 kimliği
├── Helpers/                        # Win32, FFmpeg ve Sistem yardımcı sınıfları
│   ├── FFmpegHelper.cs             # FFmpeg process yönetimi ve komut oluşturucu
│   ├── Win32Helper.cs              # P/Invoke Windows API fonksiyonları
│   └── SystemHelper.cs             # Aygıt ve ekran tespiti
├── Models/                         # Veri modelleri
│   ├── AppSettings.cs              # Kullanıcı tercihleri modeli
│   ├── ProjectManifest.cs          # .myproj JSON veri yapısı
│   └── InputEvents.cs              # Fare ve klavye telemetri modelleri
├── Services/                       # Çekirdek iş mantığı servisleri
│   ├── ScreenRecorderService.cs    # FFmpeg kayıt yürütücüsü
│   ├── ZoomEngineService.cs        # Matematiksel zoom & keyframe motoru
│   ├── InputTrackerService.cs      # Win32 hook telemetri toplayıcısı
│   ├── AudioLevelMonitorService.cs # NAudio ses seviyesi ve yakalama
│   ├── AudioWaveformService.cs     # Ses dalga formu (waveform) üretici
│   ├── ExportService.cs            # Video render servisi
│   └── ProjectService.cs           # Proje kaydetme / yükleme servisi
├── ViewModels/                     # MVVM ViewModel sınıfları
└── Views/                          # XAML Sayfaları ve Özel Pencereler
    ├── DashboardPage.xaml          # Kayıt modu seçim ana sayfası
    ├── EditorPage.xaml             # Kapsamlı timeline video editörü
    ├── ExportPage.xaml             # Dışa aktarma ve format ayarları
    ├── LibraryPage.xaml            # Kayıt geçmişi ve proje kütüphanesi
    ├── SettingsPage.xaml           # Ayrıntılı ayarlar sekmesi
    ├── RecordingSetupToolbarWindow # Yüzen kayıt öncesi ayar çubuğu
    ├── FullScreenBorderWindow.cs   # Donanım hızlandırmalı şeffaf çerçeve
    ├── RegionSelectionWindow.xaml  # Özel alan kırpma aracı
    └── TeleprompterWindow.xaml     # Entegre prompter aracı
```

### 1.7 Kurulum & Çalıştırma Rehberi
#### Gereksinimler:
- Windows 10 (Sürüm 1809+) veya Windows 11
- .NET 9.0 SDK
- Visual Studio 2022 (v17.12 veya üzeri, ".NET Masaüstü Geliştirme" ve "Windows App SDK C#" iş yükleri yüklü olmalı)
- Sistem `PATH` yoluna eklenmiş `ffmpeg.exe` (veya proje dizinindeki yerel FFmpeg binary'si)

#### Derleme ve Çalıştırma:
```powershell
# 1. Proje dizinine gidin
cd c:\Users\burak\ScreenPowerPro\ScreenPowerPro

# 2. Bağımlılıkları geri yükleyin
dotnet restore

# 3. Projeyi derleyin
dotnet build

# 4. Uygulamayı çalıştırın
dotnet run
```
*Visual Studio ile çalıştırmak için kök dizindeki `ScreenPowerPro.sln` çözüm dosyasını açıp `F5` tuşuna basabilirsiniz.*

---

# 🔶 BÖLÜM 2: ScreenPowerPro — Electron.js & React [Yedek / Backup Proje]

Yedek proje dizini: `_electron_backup/`

### 2.1 Genel Bakış
ScreenPowerPro'nun ilk prototip aşamasında geliştirilmiş olan Electron versiyonudur. Web teknolojilerinin (React, Vite, Tailwind CSS) hızından faydalanılarak cross-platform (Windows & macOS) hedeflenerek inşa edilmiştir. WebRTC API'leri ve Node.js ekosistemi üzerine kuruludur.

### 2.2 Teknoloji Yığını (Tech Stack)
- **Çalışma Çatısı**: Electron 33.0.0
- **Frontend**: React 18.3.1 + TypeScript 5.6.2
- **Stil & Tasarım**: Tailwind CSS 3.4.13 + PostCSS
- **Derleme Aracı**: Vite 5.4.8
- **Durum Yönetimi (State)**: Zustand 4.5.5 (`appStore.ts`, `editorStore.ts`)
- **Girdi Takibi**: `uiohook-napi` 1.5.4 (Node.js C++ eklentisi)
- **Video & Medya**: WebRTC `desktopCapturer` & `MediaRecorder` API
- **Render / Export**: `fluent-ffmpeg` 2.1.3 + `ffmpeg-static` 5.2.0
- **Paketleme**: `electron-builder` 25.1.7 (NSIS installer / DMG)

### 2.3 Çift Süreçli (IPC) Mimari
Electron'un standart güvenlik ve süreç ayrımı prensiplerine göre yapılandırılmıştır:

```
┌──────────────────────────────────────────────────────────┐
│             Renderer Süreci (React + Vite)               │
│  Dashboard, EditorWorkspace, RecordingBar, SettingsModal │
└────────────────────────────┬─────────────────────────────┘
                             │ window.electronAPI (ContextBridge)
┌────────────────────────────▼─────────────────────────────┐
│                      Preload Script                      │
│      electron/preload.ts (IPC Güvenli Köprüleme)         │
└────────────────────────────┬─────────────────────────────┘
                             │ ipcRenderer.invoke / send
┌────────────────────────────▼─────────────────────────────┐
│               Ana Süreç (Electron Main Process)          │
│   electron/main.ts, windowManager.ts, ipcHandlers.ts     │
│   ├── inputTracker.ts (uiohook-napi)                     │
│   ├── exportService.ts (fluent-ffmpeg)                   │
│   └── projectService.ts (Diske okuma / yazma)            │
└──────────────────────────────────────────────────────────┘
```

### 2.4 Çekirdek Modüller & Çalışma Mantığı

#### 1. Ekran & Ses Yakalama (`useRecording.ts`)
- Electron'un `desktopCapturer.getSources()` fonksiyonu ile mevcut ekranlar ve pencereler listelenir.
- Tarayıcının standart `navigator.mediaDevices.getUserMedia()` ve `MediaRecorder` API'si üzerinden WebM video akışı kaydedilir.
- Kayıt tamamlandığında bu WebM dosyası diske kaydedilir ve editörün okuyabileceği proje yapısına dönüştürülür.

#### 2. Giriş Olayları Dinleyicisi (`inputTracker.ts`)
- Node.js native addon'u olan `uiohook-napi` kütüphanesi ana süreçte çalıştırılır.
- Fare tıklamaları ve tuş basımları dinlenerek anlık olarak `mouseclicks-0.json`, `mousemoves-0.json` ve `keystrokes-0.json` dosyalarına serialize edilir.

#### 3. Reaktif Durum Yönetimi (`editorStore.ts` - Zustand)
- 50 adıma kadar geri al/yinele (`Undo/Redo`) desteği sunan snapshot tabanlı geçmiş yığını.
- Çoklu track (Video, Mikrofon, Sistem Sesi) ve zoom bloklarının koordinat, süre ve ölçek değerlerini anlık olarak yönetir.
- `splitClipsAtTime`: Oynatma kafasının bulunduğu noktada video ve ses parçalarını ikiye bölme yeteneği.

#### 4. Zoom Hesaplama Motoru (`zoomEngine.ts`)
- Tıklama noktalarının zamanlamasına göre kümelenmiş zoom keyframe'leri üretir.
- Editör önizlemesinde React canvas'ı üzerinde CSS `transform: scale(...) translate(...)` kullanarak kullanıcının çıktıyı render almadan canlı simüle etmesini sağlar.

#### 5. Dışa Aktarma (`exportService.ts`)
- `fluent-ffmpeg` kütüphanesi ve paketle gelen `ffmpeg-static` ikilisi kullanılarak video ve ses kanalları birleştirilir; üretilen zoom filtreleri uygulanarak MP4 formatında dışa aktarılır.

### 2.5 Klasör Yapısı (`_electron_backup/`)
```
_electron_backup/
├── package.json              # NPM bağımlılıkları ve scriptler
├── vite.config.ts            # Vite yapılandırması
├── tailwind.config.js        # Obsidian/Flux tema renkleri ve stilleri
├── index.html                # SPA HTML giriş noktası
├── electron/                 # Electron Ana Süreç Kodları
│   ├── main.ts               # Electron lifecycle ve pencere başlatıcı
│   ├── preload.ts            # ContextIsolation ve Renderer köprüsü
│   ├── ipcHandlers.ts        # IPC isteklerini karşılayan yönlendirici
│   ├── windowManager.ts      # Pencerelerin boyut ve pozisyon yöneticisi
│   └── services/             # Node.js tarafındaki servisler
│       ├── inputTracker.ts   # uiohook-napi giriş dinleyici
│       ├── exportService.ts  # fluent-ffmpeg export servisi
│       └── projectService.ts # Dosya sistemi işlemleri
└── src/                      # React Renderer Kodları
    ├── main.tsx              # React bootstrap
    ├── App.tsx               # Ana router ve sayfa seçici
    ├── index.css             # Tailwind ve özel animasyon stilleri
    ├── components/           # UI Bileşenleri
    │   ├── dashboard/        # Kayıt modu kartları ve aygıt seçiciler
    │   ├── editor/           # Timeline, Canvas ve Zoom kontrolleri
    │   ├── recording/        # Kayıt overlay ve geri sayım bileşenleri
    │   ├── export/           # Export ilerleme ekranı
    │   ├── library/          # Proje geçmişi
    │   └── settings/         # Ayarlar modalı
    ├── stores/               # Zustand Store'ları
    │   ├── appStore.ts       # Genel uygulama durumu
    │   └── editorStore.ts    # Timeline, klip ve zoom durumları
    └── lib/                  # Yardımcı algoritmalar
        └── zoomEngine.ts     # Zoom keyframe hesaplama algoritması
```

### 2.6 Kurulum & Çalıştırma Rehberi
#### Gereksinimler:
- Node.js (v18 veya v20 LTS önerilir)
- npm veya yarn
- Windows için `windows-build-tools` (uiohook-napi yerel derlemesi için)

#### Kurulum ve Geliştirme Modunda Başlatma:
```bash
# 1. Electron yedek dizinine gidin
cd c:\Users\burak\ScreenPowerPro\_electron_backup

# 2. Bağımlılıkları yükleyin
npm install

# 3. Hem Vite dev sunucusunu hem Electron'u eşzamanlı başlatın
npm run electron:dev
```

*Alternatif olarak ayrı terminallerde çalıştırmak isterseniz:*
```bash
# Terminal 1: Vite Dev Server (http://localhost:5173)
npm run dev

# Terminal 2: Electron Ana Süreci
npm run build:electron && npx electron .
```

#### Paketleme / Kurulum Dosyası Üretme:
```bash
# Windows installer (NSIS) oluşturmak için:
npm run electron:build
```
*Çıktı dosyaları `_electron_backup/release/` dizinine oluşturulacaktır.*

---

# ⚖️ BÖLÜM 3: Kapsamlı Karşılaştırma — Neden C# Tercih Edildi?

ScreenPowerPro projesinin Electron tabanından modern C# .NET 9 WinUI 3 mimarisine taşınmasının temel teknik ve mimari nedenleri şunlardır:

### 1. Performans ve Sistem Kaynağı Tüketimi
- **Electron**: Arka planda tam bir Chromium tarayıcısı ve Node.js runtime'ı çalıştırır. Boşta dahi 300-500 MB RAM tüketir ve 60 FPS ekran kaydında işlemciyi ve GPU'yu zorlayabilir.
- **C# / .NET 9**: Saf yerel (native) kod olarak derlenir. Donanım hızlandırmalı WinUI 3 arayüzü yalnızca 80-150 MB RAM ile çalışır; ekran kaydı doğrudan FFmpeg ve DirectX/GDI yüzeylerinden sıfır aracı katmanla alınır.

### 2. Windows Arayüz ve Pencere Bütünleşmesi
- **Ekrandan Kayıt Hariç Tutma (`WDA_EXCLUDEFROMCAPTURE`)**: Electron pencerelerinde Windows'un "pencereyi ekran kaydından gizleme" API'sini kararlı çalıştırmak zordur. C# tarafında tek satırlık Win32 API çağrısı ile yüzen kayıt paneli ve çerçeve videodan tamamen gizlenir.
- **Piksel Düzeyinde Şeffaflık**: Electron'un frameless ve şeffaf pencereleri Windows üzerinde GPU kompozisyon hatalarına (arka planın siyah boyanması veya tıklama gecikmelerine) yol açabilmektedir. C# sürümünde Win32 `WS_EX_LAYERED` ve `WS_EX_TRANSPARENT` doğrudan kullanılarak pencereler altındaki masaüstünü %100 netlikte gösterir ve tıklamaları doğrudan alta iletir.

### 3. Düşük Seviyeli Sistem Kancaları (Low-Level Hooks)
- Electron'da `uiohook-napi` gibi harici C++ kütüphanelerine ve Node.js sürüm uyumluluklarına bağımlı kalınırken; C# sürümünde yerel `SetWindowsHookEx` Win32 API'leri hiçbir harici native kütüphane gerektirmeden, son derece stabil ve mikrosaniyelik hassasiyetle çalışır.

### 4. Ses Mimarisi (WASAPI Loopback)
- Electron'da sistem sesini (bilgisayardan çıkan hoparlör sesini) WebRTC üzerinden gecikmesiz ve yüksek bit hızında almak zordur.
- C# projesinde kullanılan `NAudio` kütüphanesi, doğrudan Windows Audio Session API (WASAPI) döngüsüne bağlanarak stüdyo kalitesinde, kayıpsız ve gecikmesiz sistem sesi kaydı alır.

---

# 📦 BÖLÜM 4: Ortak Proje Formatı & Veri Modelleri

Her iki uygulama da aynı temel dosya mimarisini ve JSON telemetri yapılarını destekleyecek şekilde tasarlanmıştır. Bu sayede bir sürümde alınan kayıt diğerinde incelenebilir.

### Klasör Yapısı
```
📁 Belgelerim/ScreenPowerPro Projects/Kayıt_YYYY_MM_DD_HHMMSS/
├── 📄 project.myproj            # Editörün okuduğu ana proje manifestosu
├── 📁 recording/                # Ham kayıt verileri
│   ├── 🎥 display-0.mp4         # Ham ekran videosu (Zoom'suz)
│   ├── 🎵 microphone-0.wav      # Mikrofon ses kaydı
│   ├── 🎵 system_audio-0.wav    # Bilgisayar sistem sesi
│   ├── 📄 metadata.json         # Çözünürlük, FPS, kayıt süresi
│   ├── 📄 mouseclicks-0.json    # Tıklama telemetrisi (X, Y, buton türü, timestamp)
│   ├── 📄 mousemoves-0.json     # Fare rotası koordinatları
│   └── 📄 keystrokes-0.json     # Basılan tuşlar ve kısayollar
└── 📁 bundle/                   # Render edilmiş nihai MP4 videoları
```

### Proje Manifestosu Örneği (`project.myproj`)
```json
{
  "version": "1.0",
  "projectName": "Visual Studio Code Tanıtımı",
  "createdAt": "2026-06-26T14:30:00Z",
  "videoPath": "./recording/display-0.mp4",
  "micAudioPath": "./recording/microphone-0.wav",
  "systemAudioPath": "./recording/system_audio-0.wav",
  "timeline": {
    "zoomEffects": [
      {
        "id": "zoom_101",
        "startTime": 2.4,
        "duration": 3.0,
        "targetX": 1280,
        "targetY": 720,
        "scale": 1.75,
        "easing": "easeInOutCubic"
      }
    ],
    "settings": {
      "cursorSmoothing": "medium",
      "motionBlur": true,
      "watermark": false,
      "backgroundColor": "#0F0F14",
      "windowPadding": 32,
      "borderRadius": 16
    }
  }
}
```

---

> 💡 **Özet Tavsiye**:
> - **Maksimum Performans, Windows Uyumluluğu ve Kararlı Çalışma için**: `ScreenPowerPro/` (.NET 9 / WinUI 3) projesini tercih ediniz.
> - **Web Teknolojileri, Hızlı React Prototiplemesi ve Cross-Platform Referansı için**: `_electron_backup/` projesini inceleyiniz.
