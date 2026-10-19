# Mimari

## Genel görünüm

```
MainWindow
 └─ NodifyEditor (sonsuz tuval: kaydırma, zoom, seçim, sürükleme)
     └─ ItemContainer (her düğüm için)
         └─ TerminalNode şablonu
             ├─ Başlık (durum noktası, ad, kapat)
             └─ Grid
                 ├─ Image                (fotoğraf modu için anlık görüntü)
                 └─ EasyTerminalControl  (HwndHost → Windows Terminal kontrolü → ConPTY → süreç)
```

| Dosya | Görev |
|---|---|
| `MainWindow.xaml(.cs)` | Tuval, düğüm şablonu, fotoğraf modu, zoom→yazı boyutu |
| `TerminalNode.cs` | Düğüm modeli (başlık, komut, konum, durum) |
| `TerminalSnapshot.cs` | Terminal HWND'sinin anlık görüntüsünü alma |
| `Native.cs` | Win32 P/Invoke tanımları |

## Terminal katmanı ve kısıtları

`EasyTerminalControl`, Windows Terminal'in yerel C++/DirectX kontrolünü bir **HwndHost** içinde
barındırır. Süreçler ConPTY ile başlatılır. Bunun sonuçları:

1. **Ölçekleme yok**: HwndHost, WPF'in `RenderTransform`'undan etkilenmez; Nodify zoom yaptığında
   HWND'nin boyutu küçülür ama yazı küçülmez. Çözüm: zoom değişince `Terminal.SetTheme(..., fontSize)`
   ile yazı boyutunu `13 × zoom` olarak ayarlamak.
2. **Airspace**: WPF öğeleri terminalin üstüne çizilemez. Üst üste binen düğümler, terminal üstü
   açılır pencereler vb. bu yüzden sınırlıdır.
3. **Hareket maliyeti**: Her kaydırma karesinde tüm HWND'ler yeniden konumlanıp boyutlandığı için
   takılma olur. Çözüm "fotoğraf modu":
   - `ViewportUpdated` veya `ItemContainer.LocationChanged` geldiğinde `Freeze()`: her terminalin
     `PrintWindow(PW_RENDERFULLCONTENT)` ile görüntüsü alınır, `Image`'a konur, terminal gizlenir.
   - Son hareketten 150 ms sonra `Unfreeze()`: yazı boyutu güncellenir, terminaller geri gelir.
   - Bilinen sorun: pencere dışında kalan terminal parçaları görüntüde gri çıkar.

## Bağımlılık riski

`EasyWindowsTerminalControl`, Microsoft'un resmi olarak yayımlamadığı WPF terminal kontrolünün
topluluk tarafından yeniden paketlenmiş sürümüne (`CI.Microsoft.Terminal.Wpf`) dayanır. API
değişebilir. Alternatifler `docs/kararlar/0001-dotnet-wpf-ve-yerel-terminal.md` içinde.

## Planlanan bileşenler

- **Ajan durum köprüsü** (v0.2): Claude Code hook'ları → yerel named pipe → düğüm durumu.
- **Kalıcılık** (v0.1): çalışma alanı JSON olarak `%APPDATA%\KovanTerm` altında.
- **Ses** (v0.4): Whisper.net ile yerel konuşma tanıma.
