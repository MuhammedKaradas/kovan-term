# Kovan Term

[English](README.md) · **Türkçe**

AI kodlama ajanlarını (Claude Code, Codex, Gemini CLI…) **sonsuz bir tuval** üzerinde, her biri
kendi **gerçek terminalinde**, yan yana çalıştıran hafif bir Windows uygulaması.

> ⚠️ Erken geliştirme aşamasında (v0.1). Yol haritası: [docs/roadmap.md](docs/roadmap.md) (İngilizce)

## Neden?

Birden fazla ajanı terminal sekmeleri arasında yönetmek zor. Electron tabanlı tuval uygulamaları
(OpenCove, TermCanvas) bu sorunu çözüyor ama ağır. Kovan Term aynı fikri yerel .NET/WPF ve Windows
Terminal'in GPU hızlandırmalı terminal bileşeniyle, düşük kaynak tüketimiyle hedefliyor: 4 terminal
~190 MB RAM kullanıyor, WebView2 içinde xterm.js kullanan bir karşılığından ~3,5 kat az
([ölçüm](docs/decisions/0002-terminal-dependency-risk.md)).

## Özellikler

- Kaydırılıp yakınlaştırılabilen sonsuz tuval
- Tuval üzerinde sürüklenip boyutlandırılabilen gerçek terminaller
- Profiller: PowerShell, cmd, Claude Code, Codex, Gemini CLI + kendi profillerin ([docs/profiles.md](docs/profiles.md))
- Çalışma dizini seçimi, tuvalde sağ tıkla "Burada yeni terminal"
- Çalışma alanı otomatik kaydedilir ve açılışta geri gelir
- Klavye kısayolları: terminaller arası geçiş, tümünü sığdır, yakınlaş ([docs/shortcuts.md](docs/shortcuts.md))
- "Fotoğraf modu" ile akıcı kaydırma ve zoom; ekran dışındaki terminaller uyur
- Windows 11 görünümü (Mica, Fluent kontroller); açık, koyu ya da Windows'a uyan tema

## Gereksinimler

- Windows 10 1903+ / Windows 11
- Kullanmak istediğin ajanların CLI'ları (ör. `claude`) `PATH`'te olmalı

## Kurulum

[Releases](https://github.com/MuhammedKaradas/kovan-term/releases) sayfasından
`KovanTerm-<sürüm>-win-x64.zip` dosyasını indir, bir klasöre aç ve `KovanTerm.exe`'yi çalıştır.
.NET kurulumu gerekmez. Exe henüz imzalı olmadığı için SmartScreen uyarısı çıkarsa
"Ek bilgi → Yine de çalıştır".

## Kaynaktan çalıştırma

[.NET 10 SDK](https://dotnet.microsoft.com/download) gerekir.

```bash
git clone https://github.com/MuhammedKaradas/kovan-term.git
cd kovan-term
dotnet run --project src/KovanTerm -c Release
```

## Belgeler (İngilizce)

- [Mimari](docs/architecture.md)
- [Profiller](docs/profiles.md)
- [Klavye kısayolları](docs/shortcuts.md)
- [Yol haritası](docs/roadmap.md)
- [Mimari karar kayıtları](docs/decisions/)
- [Katkı kuralları](CONTRIBUTING.md)

## Lisans

[MIT](LICENSE)
