1. Proje Vizyonu

Kullanıcıların ekran kayıtlarını alırken, arka planda klavye ve mouse hareketlerini kaydedip, post-processing (düzenleme) aşamasında bu verilere dayanarak otomatik zoom, pan ve imleç efektleri uygulayan, tek seferlik ödeme modeline sahip profesyonel bir masaüstü uygulaması. (Yapay zeka özellikleri dahil DEĞİLDİR, tamamen algoritmik ve yerel çalışır).
projenin tasarımları C:\Users\burak\ScreenPowerPro\stitch_prostudio_screen_recorder dosyasının içindedir tekrardan tasarım yapmana gerek yoktur.
2. Teknoloji Yığını (Tech Stack)

Çatı (Framework): Electron.js (Windows ve macOS desteği)

Arayüz (Frontend): React.js + Tailwind CSS (Performanslı ve modern UI)

Durum Yönetimi (State): Zustand veya Redux (Editör içindeki karmaşık timeline verileri için)

Video İşleme: FFmpeg (Arka planda video kesme, zoom ve render işlemleri için yerleşik binary)

Sistem Dinleyicileri: uiohook-napi (Global mouse ve klavye takibi)

Ekran Kaydı: Electron desktopCapturer & WebRTC MediaRecorder API

3. Klasör ve Proje Yapısı (ScreenPowerPro Referanslı)

Bir kayıt alındığında uygulama diske özel bir proje dosyası (Örn: .myproj) ve bir kaynak klasörü oluşturur.

📁 Belgelerim/MyRecorder Projects/Kayıt_2026_06_26/
├── 📄 project.myproj            # Editörün okuyacağı ana proje manifest dosyası
├── 📁 recording/                # Ham kayıt dosyaları
│   ├── 🎥 display-0.mp4         # Ekranın yüksek kaliteli ham kaydı (Zoom'suz)
│   ├── 🎵 microphone-0.m4a      # Mikrofondan alınan ses kaydı
│   ├── 🎵 system_audio-0.m4a    # Bilgisayarın sistem sesi
│   ├── 📄 metadata.json         # Kayıt çözünürlüğü, FPS, süre gibi genel veriler
│   ├── 📄 mouseclicks-0.json    # Sadece tıklama olayları (X, Y ve Timestamp)
│   ├── 📄 mousemoves-0.json     # Fare hareketleri kordinatları
│   └── 📄 keystrokes-0.json     # Basılan tuşlar (Kısayol gösterimi için)
└── 📁 bundle/                   # Render edilmiş, dışa aktarılmış nihai MP4 dosyaları


4. Veri Modelleri (JSON Yapıları)

A. mouseclicks-0.json
Otomatik zoom motorunun okuyacağı ana dosya budur.

[
  { "timestamp": 1245.5, "type": "left_down", "x": 1920, "y": 1080 },
  { "timestamp": 1245.8, "type": "left_up", "x": 1920, "y": 1080 },
  { "timestamp": 4500.2, "type": "right_down", "x": 450, "y": 200 }
]


B. project.myproj (Editör Timeline Modeli)
Editör açıldığında videoya eklenecek efektler bu dosyada tutulur. Kullanıcı Zoom efektini silerse bu dosyadan silinir, ham veri (mouseclicks.json) bozulmaz.

{
  "version": "1.0",
  "videoPath": "./recording/display-0.mp4",
  "timeline": {
    "zoomEffects": [
      { "id": "z1", "startTime": 1.2, "duration": 2.0, "targetX": 1920, "targetY": 1080, "scale": 1.5, "easing": "ease-in-out" }
    ],
    "settings": {
      "cursorSmoothing": "medium",
      "motionBlur": true,
      "watermark": false
    }
  }
}


5. Uygulama Modülleri ve Ekranlar

5.1. Kayıt Öncesi Ekranı (Dashboard & Settings)

Kayıt Modları: Tam Ekran, Belirli Bir Pencere, Özel Alan (Custom Crop).

Aygıt Seçimi: Mikrofon seçimi, Kamera (Webcam - isteğe bağlı) ve Sistem Sesi toggle'ı.

Ayarlar Modülü:

General: Proje kayıt lokasyonu, Export lokasyonu.

Record: Otomatik Zoom efekti (Açık/Kapalı), Masaüstü simgelerini gizle (Windows API ile yapılabilir), Çözünürlük (1080p/4K), Geri sayım (3s, 5s).

Export: Format (MP4), FPS (30/60).

5.2. Video Editör Ekranı

Sol Menü (Araçlar): * Hareket Bulanıklığı (Motion Blur) ayarları.

İmleç Hareketi Yumuşatma (Slow, Medium, Fast).

Arkaplan stili (Özel pencereli kayıtlarda köşeleri yuvarlatma, arkaplan rengi/gölgesi).

Tuş Vuruşları Gösterimi (Display Shortcut Keys).

Merkez (Önizleme Canvas'ı): React üzerinde HTML <video> etiketi ve üzerine bindirilmiş CSS transform: scale() translate() ile çalışan sahte zoom motoru. Kullanıcıya videonun render edilmiş halini simüle eder.

Alt Alan (Timeline): * Video izi (Track).

Zoom izi (Tıklama noktalarından otomatik üretilen "Zoom 1", "Zoom 2" kutucukları). Kullanıcı bu kutuları sağa sola çekerek zoomun zamanlamasını değiştirebilir.

5.3. Render (Export) Motoru

Kullanıcı "Export" dediğinde, React tarafındaki .myproj JSON verisi alınır.

Bu veriler FFmpeg komutlarına dönüştürülür.

Örnek FFmpeg Filtresi (Zoom için): -vf "zoompan=z='if(between(t,1.2,3.2),1.5,1)':x='1920-(1920/1.5)/2':y='1080-(1080/1.5)/2':d=1"

İşlem arka planda yüzdelik bir progress bar ile kullanıcıya gösterilir.