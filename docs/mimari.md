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
├─ Models/        Saf veri: NodeStatus, TerminalProfile, WorkspaceState
├─ ViewModels/    MainViewModel, TerminalNodeViewModel, NewTerminalViewModel, NodePlacement
├─ Views/         MainWindow, NewTerminalWindow, DialogService, TerminalCanvasController, Converters/
├─ Services/      WorkspaceStore, ProfileStore, CommandResolver, IDialogService
└─ Interop/       Win32 P/Invoke (Native) ve HWND görüntüsü alma (TerminalSnapshot)
tests/KovanTerm.Tests/   xUnit birim testleri
```

| Dosya | Görev |
|---|---|
| `ViewModels/MainViewModel.cs` | Düğüm listesi, görünüm durumu, komutlar (`NewTerminal`, `CloseNode`), kayıt |
| `ViewModels/NewTerminalViewModel.cs` | "Yeni terminal" diyaloğu: profil ve çalışma dizini seçimi, doğrulama |
| `ViewModels/NodePlacement.cs` | Yeni düğüm için çakışmayan, tercihen görünür boş yer bulma |
| `Services/CommandResolver.cs` | Programı `PATH`/`PATHEXT` ile bulma, ConPTY komut satırını kurma |
| `Services/ProfileStore.cs` | Varsayılan + `profiles.json` profilleri ([profiller.md](profiller.md)) |
| `ViewModels/TerminalNodeViewModel.cs` | Düğüm: başlık, komut, boyut, konum, durum |
| `Views/MainWindow.xaml(.cs)` | Tuval ve düğüm şablonu; code-behind yalnızca terminalleri kontrolcüye kaydeder |
| `Views/TerminalCanvasController.cs` | Fotoğraf modu ve zoom→yazı boyutu (HwndHost kısıtları) |
| `Interop/TerminalSnapshot.cs` | Terminal HWND'sinin anlık görüntüsünü alma |

Kural: ViewModel'ler WPF kontrollerine (`EasyTerminalControl`, `NodifyEditor`) dokunmaz. Terminal
kontrolüyle doğrudan çalışması gereken kod `Views/` altında yaşar. ViewModel'in açması gereken
pencereler `IDialogService` üzerinden açılır (görünümde `DialogService`), böylece ViewModel test
edilebilir kalır.

Yeni düğümler hiçbir düğümle çakışmayacak şekilde yerleştirilir (`NodePlacement`): üst üste binen
terminaller airspace nedeniyle birbirinin başlığını örter.

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
   - `ViewportUpdated` veya `ItemContainer.LocationChanged` geldiğinde `Freeze()`: terminaller
     gizlenir, yerlerine `Image` içinde anlık görüntüleri gösterilir.
   - Son hareketten 150 ms sonra `Unfreeze()`: yazı boyutu güncellenir, terminaller geri gelir;
     görüntüler terminal ilk karesini çizene kadar 100 ms daha arkada tutulur (boş kare titremesi olmasın).
   - Boyutlandırma gibi süresi belli olmayan etkileşimlerde `BeginInteraction()`/`EndInteraction()`
     fotoğraf modunu etkileşim bitene kadar tutar; görüntü yeni boyuta gerilir, bırakınca terminal
     gerçek boyutta yeniden çizilir.
   - **Görüntü önbelleği:** `PrintWindow(PW_RENDERFULLCONTENT)` bir DWM karesini beklediği için
     terminal başına ~12-20 ms sürer ve çağrılar paralelleştirilemiyor (sıraya giriyor). Hareket
     anında almak 10 terminalde ~100-170 ms donmaya yol açıyordu. Bunun yerine terminalin
     `TerminalOutput` olayı ve boyut değişimi "değişti" bayrağı kaldırır; 150 ms'de bir değişen
     terminallerin görüntüsü **arka plan iş parçacığında** alınır. `Freeze()` önbellekteki
     görüntüyü kullanır (<1 ms); yalnızca güncel olmayanları anında alır.
   - **Kırpılan terminaller:** pencere dışına taşan bir HWND için `PrintWindow` yalnızca görünür
     kısmı sol üst köşeye yaslanmış çizer. `TerminalSnapshot` görünür parçayı doğru konuma kaydırır,
     taşan kısmı terminal arka planıyla boyar.
   - Ölçüm: `KOVAN_PERF_SELFTEST` (CLAUDE.md). 10 terminalde ~58 fps, en kötü kare ~35 ms
     (önbellek öncesi ~200 ms).
   - Bilinen sınır: yazı boyutu tam sayı olduğu için (`SetTheme` `short` alıyor) hareket bitince
     terminal yazısı ölçeklenmiş görüntüden biraz farklı boyutta gelebilir (küçük bir "zıplama").
4. **Boyutlandırma tutamacı**: terminalin üstüne WPF çizilemediği için tutamaç terminalin altındaki
   ince şerittedir. `Thumb.DragDelta` değerleri düğümün içinde ölçülür, yani zaten tuval birimindedir.

## Kalıcılık

Çalışma alanı `%APPDATA%\KovanTerm\workspace.json` dosyasında tutulur (`WorkspaceStore`).

- **Ne kaydedilir:** düğümler (kimlik, başlık, komut, çalışma dizini, konum, boyut) ve görünüm
  (zoom, konum). Terminal içeriği ve ajan durumu kaydedilmez; açılışta terminaller aynı komutla
  yeniden başlar.
- **Ne zaman:** düğüm eklenince/kapanınca, düğüm taşınınca ve görünüm değişince 500 ms gecikmeyle;
  ayrıca pencere kapanırken.
- **Güvenlik:** önce `.tmp` dosyasına yazılıp yer değiştirilir (yarım dosya kalmaz). Okunamayan ya
  da daha yeni sürümle yazılmış dosya `workspace.bozuk-<tarih>.json` olarak yedeklenir ve uygulama
  varsayılan alanla açılır.
- **Sürüm:** `WorkspaceState.Version`. Alan eklemek geriye uyumludur; alan anlamını değiştiren
  değişiklikte sürüm artırılıp geçiş kodu yazılır.

## Bağımlılık riski

`EasyWindowsTerminalControl`, Microsoft'un resmi olarak yayımlamadığı WPF terminal kontrolünün
topluluk tarafından yeniden paketlenmiş sürümüne (`CI.Microsoft.Terminal.Wpf`) dayanır. Riskler,
WebView2 + xterm.js yedeğiyle bellek karşılaştırması ve alınan önlemler:
[0002 karar kaydı](kararlar/0002-terminal-bagimliligi-riski.md). Kısaca: paket sürümleri sabit
tutulur, güncellemeler elle test edilir.

## Planlanan bileşenler

- **Ajan durum köprüsü** (v0.2): Claude Code hook'ları → yerel named pipe → düğüm durumu.
- **Ses** (v0.4): Whisper.net ile yerel konuşma tanıma.
