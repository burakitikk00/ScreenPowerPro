# ScreenPowerPro - Yeni Arayüz, Kurulum Araç Çubuğu ve Ayarlar Rehberi

Bu belge, **ScreenPowerPro** uygulamasına eklenen yeni kullanıcı deneyimi (UX), alt yüzen kayıt öncesi kurulum araç çubuğu, tam ekran kesikli kayıt alanı çerçevesi ve tümüyle çalışan yeni ayarlar penceresinin teknik ve işlevsel kullanım kılavuzudur.

---

## 1. Yeni Akış ve Ekran Kayıt Modu Seçimi (Dashboard)

### 1.1. Mod Kartları Üzerinde Mavi Hover Çerçevesi
- Dashboard ekranındaki 4 kayıt modu kartı (`Full Screen`, `Custom`, `Window`, `Device`) fare ile üzerine gelindiğinde dinamik olarak canlı mavi renkte (`#3B82F6`) 2px kalınlığında vurgulanır.
- Fare kart üzerinden ayrıldığında, eğer o kart aktif seçili değilse standart koyu temalı sınır rengine (`#1F202B`) geri döner; seçiliyse aktif mavi vurgusunu korur.

### 1.2. "Kayıta Başla" Butonunun Kaldırılması & Doğrudan Geçiş
- Eski "Start Recording" butonu ana sayfadan tamamen kaldırılmıştır.
- Kullanıcı herhangi bir kayıt moduna (özellikle **Full Screen** veya **Custom**) tıkladığı anda:
  1. Ana pencere (`MainWindow`) otomatik olarak arka plana / simge durumuna küçülür (`presenter.Minimize()`).
  2. **Tam Ekran (`FullScreen`)** seçildiyse: Ekranın sınırlarını gösteren **kesikli mavi çerçeve** (`FullScreenBorderWindow`) ekranda belirir.
  3. **Özel Alan (`CustomArea`)** seçildiyse: Kullanıcı ekranda kaydetmek istediği alanı fare ile seçer; ardından seçilen bu alanın etrafına kesikli çerçeve yerleşir.
  4. Ekranın alt orta kısmında modern, yüzen **2. Sayfa Kurulum Araç Çubuğu** (`RecordingSetupToolbarWindow`) açılır.

---

## 2. Kesikli Çizgili Kayıt Alanı Çerçevesi (`FullScreenBorderWindow`)

- **%100 Şeffaf Arka Plan (Arkası Tamamen Görünür)**:
  - WinUI 3 pencerelerinin tam ekranda varsayılan siyah arka plan boyama sorunu giderilerek yerel **Win32 Katmanlı Pencere (`WS_EX_LAYERED` + `UpdateLayeredWindow`)** mimarisine geçilmiştir.
  - İç kısımdaki tüm pikseller Alpha=0 (tamamen saydam) olarak işlenir; böylece masaüstü, açık pencereler, simgeler ve uygulamalar arkasında %100 netlikte görünür, kesinlikle siyah ekran oluşmaz.
- **Kesikli Çizgiler**:
  - Ekran sınırlarında 4px kalınlığında canlı mavi (`#38BDF8`) kesikli çizgiler (`dash: 12px`, `gap: 8px`) çizdirilir.
- **Tıklama Geçirgenliği (Click-Through)**:
  - Çerçeve `WS_EX_TRANSPARENT` stili sayesinde tıklama geçirgendir; altındaki masaüstü veya uygulamaları kullanmayı asla engellemez.
- **Kayıttan Gizleme**:
  - `Win32Helper.WDA_EXCLUDEFROMCAPTURE` API'si ile video kaydında görünmesi engellenir.
- **Kapatma / İptal**:
  - Kurulum araç çubuğundaki `[X]` kapatma butonuna basıldığında veya `Kayıta Başla` ile kayıt başlatıldığında pencere anında yok edilir (`DestroyWindow`).

---

## 3. Alt Yüzen Kurulum Araç Çubuğu (`RecordingSetupToolbarWindow`)

Ekranın alt kısmında yüzen obsidyen koyu tasarımlı kompakt çubuk, kayıt başlamadan önceki son kontrolleri ve ayarları sağlar:

### 3.1. Bileşenler ve İşlevleri:
1. **`[X]` Kapat Butonu**:
   - Kurulum çubuğunu ve kesikli ekran çerçevesini kapatır.
   - Ana Dashboard penceresini (`MainWindow`) tekrar ekrana geri getirir.
2. **`[🖥️]` Ekran Modu İkonu**:
   - Seçilen kayıt modunun görsel göstergesidir (Full Screen, Custom Area vb.).
3. **`[⚙ v]` Hızlı Ayarlar Dişli Menüsü**:
   - Tıklandığında yukarıya doğru açılan bağlam menüsü (Flyout) sunar:
     - **Zoom Effect >** (None, 2D Zoom, 3D Motion).
     - **Pre-Recording Countdown >** (3s, 5s, No countdown).
     - **Hide Desktop Icons**: Masaüstü simgelerini anında gizler / gösterir.
     - **Hide Taskbar**: Windows görev çubuğunu anında gizler / gösterir.
     - **More Settings**: Ayrıntılı 4 sekmeli Ayarlar Penceresini açar.
4. **`[📹 Kamera]` Açılır Menüsü**: Web kameraları ve "None" seçeneği.
5. **`[🎤 Mikrofon]` Açılır Menüsü**: Mikrofon aygıtları ve "None" seçeneği.
6. **`[🔊 Hoparlör]` Açılır Menüsü**: Ses çıkış aygıtları ve "None" seçeneği.
7. **`[● Kayıta Başla]` Butonu (En Sağda)**:
   - Araç çubuğunun en sağında yer alan belirgin, kırmızı degrade arka plana ve kayıt simgesine sahip eylem butonu.
   - Tıklandığında kurulum çubuğunu ve kesikli çerçeveyi kapatıp geri sayım eşliğinde (varsa) veya doğrudan ekran kaydını başlatır.

---

## 4. Yeni Ayrıntılı Ayarlar Penceresi (`SettingsWindow`)

Yüklenen ekran görüntülerine göre tasarlanmış, 4 sekmeli ve tüm ayarları anında uygulayan modern penceredir.

### 4.1. `General` (Genel) Sekmesi
- **Save Location (Kayıt Yeri)**:
  - Ham video, mikrofon, sistem sesi ve proje meta verilerinin kaydedileceği klasör.
  - Yanındaki klasör butonuna tıklandığında Windows `FolderPicker` açılır ve seçilen yeni dizin anında kaydedilir.
- **Export Location (Dışa Aktarım Yeri)**:
  - Editörden render alınan nihai videoların kaydedileceği klasör.
  - Klasör seçici ile değiştirilebilir.
- **Auto Start (Otomatik Başlatma)**:
  - Açıldığında Windows Kayıt Defteri'ne (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) ScreenPowerPro'nun tam yolunu yazar.
  - Kapatıldığında bu kaydı temizler.
- **Auto-play video when entering editor**:
  - Kayıt bitip Video Düzenleyici sayfası (`EditorPage`) açıldığında videonun otomatik olarak oynatılmaya başlamasını sağlar.
- **Current graphics card (Mevcut Ekran Kartı)**:
  - Bilgisayarda takılı ve ekrana bağlı olan GPU donanımını (örneğin `NVIDIA GeForce GTX 1650` veya `Intel(R) Iris Xe`) Windows `EnumDisplayDevices` API'si ile gerçek zamanlı tespit eder ve rozet şeklinde gösterir.

### 4.2. `Record` (Kayıt) Sekmesi
- **Auto-Add Zoom Effect**:
  - `2D Zoom`, `3D Motion` veya `None`. Fare tıklamalarına göre video timeline'ına otomatik zoom blokları eklenmesini kontrol eder.
- **Hide Desktop Icons**:
  - Kayıt esnasında masaüstü simgelerini otomatik olarak gizler, kayıt bittiğinde eski haline getirir.
- **Hide Taskbar**:
  - Kayıt esnasında görev çubuğunu otomatik olarak gizler, kayıt bittiğinde görünür yapar.
- **Recording Quality**:
  - Video kayıt kalitesi ve sıkıştırma oranı:
    - `Ultra`: CRF 15 (Kayıpsıza yakın en yüksek kalite)
    - `High`: CRF 18 (Önerilen yüksek kalite dengesi)
    - `Medium`: CRF 23 (Daha düşük dosya boyutu)
    - `Low`: CRF 28 (Minimum dosya boyutu)
- **Pre-Recording Countdown**:
  - Kayıt başlamadan önceki bekleme: `3s`, `5s`, `No countdown`.

### 4.3. `Shortcut Keys` (Kısayol Tuşları) Sekmesi
- **Start / Stop Recording**: Kaydı Başlatma ve Durdurma kısayolu (Varsayılan: `F9`).
- **Pause / Resume Recording**: Kaydı Duraklatma ve Devam Ettirme kısayolu (Varsayılan: `F10`).
- **Take Screenshot**: Anlık ekran görüntüsü yakalama kısayolu (Varsayılan: `F11`).

### 4.4. `Export` (Dışa Aktarma) Sekmesi
- **Video Format**: Nihai render dosya formatı (`MP4`, `MKV`, `WebM`, `GIF`).
- **Export Resolution**: Çıktı çözünürlüğü (`Original`, `4K`, `1080p`, `720p`).
- **Target Frame Rate**: Hedef kare hızı (`60 FPS`, `30 FPS`).

---

## 5. Değişiklik Özeti ve Kod Mimarisi

| Dosya | Yapılan Değişiklik |
|---|---|
| `DashboardPage.xaml` | Mod kartlarına hover pointer olayları eklendi. "Start Recording" butonu kaldırıldı. Teleprompter butonu genişletildi. |
| `DashboardPage.xaml.cs` | Mod tıklandığında Dashboard'u küçültüp `FullScreenBorderWindow` ve `RecordingSetupToolbarWindow` açan mantık eklendi. Ayarlar ikonu `SettingsWindow`'a bağlandı. |
| `RecordingSetupToolbarWindow.xaml/.cs` | Görsellerdeki modern alt yüzen araç çubuğu (Close, Mode, Gear Flyout, Cam/Mic/Speaker Dropdowns, Purple REC) oluşturuldu. |
| `FullScreenBorderWindow.xaml/.cs` | Ekran etrafında kesikli mavi çizgili (`StrokeDashArray="8,5"`), tıklama geçiren transparan çerçeve penceresi oluşturuldu. |
| `SettingsWindow.xaml/.cs` | Görsellerdeki 4 sekmeli (General, Record, Shortcut Keys, Export) obsidian temalı ayarlar penceresi ve işlevleri uygulandı. |
| `AppSettings.cs` | `AutoStart`, `AutoPlayVideo`, `ZoomEffect`, `RecordingQuality`, kısayol alanları modele eklendi. |
| `SystemHelper.cs` | GPU model adı algılama (`EnumDisplayDevices`) ve Windows Run Registry otomatik başlatma metotları yazıldı. |
| `ScreenRecorderService.cs` | `RecordingQuality` seçimine göre FFmpeg `-crf` dinamik hesaplaması eklendi. |
| `EditorPage.xaml.cs` | `AutoPlayVideo` ayarı aktif olduğunda editöre girildiğinde otomatik oynatma tetiklendi. |
