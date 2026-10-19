# Mimari

## Genel görünüm

Uygulama MVVM ile yazılır ([CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)).
Durum ve komutlar ViewModel'lerde; HwndHost'a özgü işler görünüm katmanında kalır.

```
MainWindow  (DataContext: MainViewModel)
 └─ NodifyEditor (sonsuz tuval; ViewportZoom/ViewportLocation ↔ MainViewModel)
     └─ ItemContainer (her düğüm için; Location ↔ TerminalNodeViewModel)
         └─ TerminalNodeViewModel şablonu
             ├─ Başlık (durum noktası, ad, kapat → CloseNodeCommand)
             └─ Grid
                 ├─ Image                (fotoğraf modu için anlık görüntü)
                 └─ EasyTerminalControl  (HwndHost → Windows Terminal kontrolü → ConPTY → süreç)
```

```
src/KovanTerm/
├─ Models/        Saf veri: NodeStatus, TerminalLaunch
├─ ViewModels/    MainViewModel, TerminalNodeViewModel
├─ Views/         MainWindow, TerminalCanvasController, TerminalThemes, Converters/
└─ Interop/       Win32 P/Invoke (Native) ve HWND görüntüsü alma (TerminalSnapshot)
```

| Dosya | Görev |
|---|---|
| `ViewModels/MainViewModel.cs` | Düğüm listesi, hızlı başlatma, görünüm durumu, komutlar (`AddTerminal`, `CloseNode`) |
| `ViewModels/TerminalNodeViewModel.cs` | Düğüm: başlık, komut, boyut, konum, durum |
| `Views/MainWindow.xaml(.cs)` | Tuval ve düğüm şablonu; code-behind yalnızca terminalleri kontrolcüye kaydeder |
| `Views/TerminalCanvasController.cs` | Fotoğraf modu ve zoom→yazı boyutu (HwndHost kısıtları) |
| `Interop/TerminalSnapshot.cs` | Terminal HWND'sinin anlık görüntüsünü alma |

Kural: ViewModel'ler WPF kontrollerine (`EasyTerminalControl`, `NodifyEditor`) dokunmaz. Terminal
kontrolüyle doğrudan çalışması gereken kod `Views/` altında yaşar.

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
