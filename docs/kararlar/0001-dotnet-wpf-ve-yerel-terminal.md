# 0001: .NET/WPF ve yerel terminal kontrolü

- **Durum:** Kabul edildi
- **Tarih:** 2026-10-20

## Bağlam

Benzer açık kaynak uygulamalar (OpenCove, TermCanvas) Electron + React + xterm.js kullanıyor.
Bunlar özellik olarak güçlü ama ağır ve zaman zaman takılıyor. Hedefimiz Windows'ta hafif ve akıcı
bir uygulama.

## Değerlendirilen seçenekler

1. **OpenCove'a eklenti yazmak**: En hızlı yol ama Electron'un ağırlığı devam eder.
2. **Tauri + xterm.js**: Electron'dan hafif, ama terminal çizimi yine web içinde.
3. **WPF + WebView2 içinde xterm.js**: Zoom sorunu yok, ağırlık geri gelir.
4. **WPF + Windows Terminal yerel kontrolü (HwndHost)**: En hafif ve hızlı terminal; zoom ve
   airspace kısıtları var.

## Karar

Seçenek 4. Bir günlük prototipte 4 terminal + Claude Code ile uygulama ~150-170 MB RAM kullandı.
Zoom kısıtı yazı boyutunu ölçekleyerek, kaydırma takılması "fotoğraf modu" ile çözüldü ve
akıcılık kabul edilebilir bulundu.

## Sonuçlar

- Yalnızca Windows desteklenir.
- Terminal üstüne WPF içeriği çizilemez; arayüz tasarımı buna göre yapılmalı.
- Resmi olmayan `CI.Microsoft.Terminal.Wpf` paketine bağımlıyız; kırılırsa seçenek 3'e geçiş planı
  hazır tutulmalı.
