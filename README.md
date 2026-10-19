# Kovan Term

AI kodlama ajanlarını (Claude Code, Codex, Gemini CLI…) **sonsuz bir tuval** üzerinde, her biri
kendi **gerçek terminalinde**, yan yana çalıştıran hafif bir Windows uygulaması.

> ⚠️ Erken geliştirme aşamasında (v0.1). Yol haritası: [docs/yol-haritasi.md](docs/yol-haritasi.md)

## Neden?

Birden fazla ajanı terminal sekmeleri arasında yönetmek zor. Electron tabanlı tuval uygulamaları
(OpenCove, TermCanvas) bu sorunu çözüyor ama ağır. Kovan Term aynı fikri yerel .NET/WPF ve Windows
Terminal'in GPU hızlandırmalı terminal bileşeniyle, düşük kaynak tüketimiyle hedefliyor.

## Şu an neler var

- Kaydırılıp yakınlaştırılabilen sonsuz tuval
- Tuval üzerinde sürüklenebilir gerçek terminaller
- Profiller: PowerShell, cmd, Claude Code, Codex, Gemini CLI + kendi profillerin ([docs/profiller.md](docs/profiller.md))
- Çalışma dizini seçimi, tuvalde sağ tıkla "Burada yeni terminal"
- Çalışma alanı otomatik kaydedilir ve açılışta geri gelir
- Zoom'a göre ölçeklenen terminal yazısı
- Kaydırma/zoom sırasında akıcılık için "fotoğraf modu"

## Gereksinimler

- Windows 10 1903+ / Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Ajanlar için ilgili CLI'lar (ör. `claude`) PATH'te olmalı

## Kurulum

[Releases](https://github.com/MuhammedKaradas/kovan-term/releases) sayfasından
`KovanTerm-<sürüm>-win-x64.zip` dosyasını indir, bir klasöre aç ve `KovanTerm.exe`'yi çalıştır.
.NET kurulumu gerekmez. İmzasız olduğu için SmartScreen uyarısı çıkarsa "Ek bilgi → Yine de çalıştır".

## Kaynaktan çalıştırma

```bash
git clone https://github.com/MuhammedKaradas/kovan-term.git
cd kovan-term
dotnet run --project src/KovanTerm -c Release
```

## Belgeler

- [Mimari](docs/mimari.md)
- [Profiller](docs/profiller.md)
- [Yol haritası](docs/yol-haritasi.md)
- [Mimari karar kayıtları](docs/kararlar/)
- [Katkı kuralları](CONTRIBUTING.md)

## Lisans

[MIT](LICENSE)
