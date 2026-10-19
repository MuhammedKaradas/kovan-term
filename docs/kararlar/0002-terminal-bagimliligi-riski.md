# 0002: Terminal kontrolü bağımlılığının riski

- **Durum:** Kabul edildi
- **Tarih:** 2026-10-20
- **İlgili:** #8, [0001](0001-dotnet-wpf-ve-yerel-terminal.md)

## Bağlam

Terminal katmanı iki topluluk paketine dayanıyor:

| Paket | Kim | Sürüm geçmişi | Not |
|---|---|---|---|
| `EasyWindowsTerminalControl` | Tek geliştirici ([mitchcapper](https://github.com/mitchcapper/EasyWindowsTerminalControl)) | 5 sürüm; son: 1.0.38 (2026-07-16) | MIT (lisans dosyası 2026-07-15'te eklendi), 37 yıldız, 1 açık issue. Son sürümde Microsoft'un resmi `Microsoft.Windows.Console.ConPTY` paketine geçti |
| `CI.Microsoft.Terminal.Wpf` | "CI2Nuget Repackage Team" | 2 sürüm; son: 1.25.260303002 (2026-07-16) | Windows Terminal'in WPF kontrolünün **resmi olmayan** yeniden paketi |

Microsoft, WPF terminal kontrolünü hâlâ resmi paket olarak yayımlamıyor:
[microsoft/terminal#6999](https://github.com/microsoft/terminal/issues/6999) ("Productize the WPF, UWP Terminal
Controls") 2020'den beri açık. Kontrolün kaynağı ise Windows Terminal reposunda MIT lisansıyla duruyor.

### Riskler

1. **Sahipsiz kalma:** İki paketin de arkasında tek kişi ya da küçük bir ekip var. Güncellemeler durabilir.
2. **API değişikliği:** Alttaki kontrol resmi değil; yeni sürümlerde arayüzü değişebilir.
3. **Windows uyumu:** Yeni bir Windows sürümü kontrolü bozarsa düzeltme gecikebilir.

Bu riskler **anında kırılma** riski değil: sürümler sabitlendiği sürece mevcut ikili dosyalar çalışmaya
devam eder. Asıl risk, ileride bir hata ya da güvenlik düzeltmesi gerektiğinde beklemek zorunda kalmak.

## Yedek planın ölçümü

Yedek plan (0001'deki 3. seçenek): her terminal için WebView2 içinde xterm.js. Aynı makinede, kabuk
işlemleri hariç tutularak (yalnızca arayüz maliyeti) ölçüldü:

| | 4 terminal | 10 terminal |
|---|---|---|
| **Yerel kontrol (KovanTerm, Release)** | 190 MB çalışma kümesi / 177 MB özel | 282 MB / 291 MB, 11 işlem |
| WebView2 + xterm.js (ortak ortam) | 626 MB / 326 MB, 10 işlem | 1.029 MB / 530 MB, 16 işlem |

- Ölçüm: açılıştan 15 sn sonra uygulama + tüm alt işlemleri; `cmd.exe` vb. kabuklar hariç.
- WebView2 prototipindeki xterm.js'ler kabuğa **bağlı değildi**; gerçek kullanımda değerler daha yüksek olur.
  Yani bu karşılaştırma WebView2 için iyimser.
- Yerel kontrol çalışma kümesinde ~3,5 kat, özel bellekte ~1,8 kat daha hafif.

## Karar

Yerel kontrolle devam ediyoruz. Riski şu önlemlerle yönetiyoruz:

1. **Sürümleri sabitle:** Paket sürümleri `csproj`'da tam sürümle duruyor; güncelleme bilinçli yapılır
   ve akıcılık/temizlik testleri (fotoğraf modu, işlem sonlandırma) elle tekrarlanır.
2. **Kaynaktan derleme yedeği:** Yeniden paketleyen ekip ortadan kalkarsa `Microsoft.Terminal.Wpf`
   kontrolü Windows Terminal kaynağından (`src/cascadia/WpfTerminalControl`, MIT) derlenebilir.
   `EasyWindowsTerminalControl` de MIT; gerekirse repoya alınıp sürdürülebilir.
3. **Terminal kontrolünü soyutla:** Terminale dokunan kod zaten görünüm katmanında, büyük kısmı
   `TerminalCanvasController`'da. Kontrolün yerine başka bir uygulama koyabilmek için bir arayüz
   arkasına alınması ayrı bir iş olarak planlandı (#30). Bu, WebView2'ye geçişi gerekirse günlerle sınırlar.
4. **Tetikleyiciler:** Aşağıdakilerden biri olursa bu karar yeniden değerlendirilir:
   - Paketler 12 aydan uzun güncellenmez **ve** çözülmeyen kritik bir hata çıkar
   - Microsoft resmi bir WPF/WinUI terminal kontrolü yayımlar (#6999 kapanır)
   - Desteklenen bir Windows sürümünde kontrol çalışmaz hale gelir

## Sonuçlar

- Bellek ve akıcılık avantajı korunur. Bu, projenin var olma sebebi.
- Paket güncellemelerinde elle test maliyeti var.
- Soyutlama işi yapılana kadar geçiş maliyeti yüksek kalır.
