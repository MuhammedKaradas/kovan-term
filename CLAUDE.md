# CLAUDE.md

Bu dosya, Claude Code'un bu repoda çalışırken uyması gereken bağlamı ve kuralları içerir.

## Proje

**Kovan Term**: Birden fazla AI kodlama ajanını (Claude Code, Codex, Gemini CLI…) sonsuz bir tuval
üzerinde gerçek terminallerde yan yana çalıştıran, Windows'a özel, hafif bir masaüstü uygulaması.
Electron tabanlı alternatiflere (OpenCove, TermCanvas) göre düşük RAM ve akıcılık ana hedeftir.

## Teknoloji

- .NET 10, WPF (`net10.0-windows`)
- Tuval: [Nodify](https://github.com/miroiu/nodify)
- Terminal: [EasyWindowsTerminalControl](https://github.com/mitchcapper/EasyWindowsTerminalControl)
  (Windows Terminal'in yerel kontrolü + ConPTY)

## Komutlar

```bash
dotnet build                      # derle
dotnet run --project src/KovanTerm  # çalıştır
dotnet build -c Release           # performans ölçerken Release kullan
dotnet test                       # birim testleri (tests/KovanTerm.Tests)
```

## Mimari notlar (önemli)

- Terminal kontrolü **HwndHost**'tur: kendi HWND'sinde DirectX ile çizer. Bu yüzden
  - WPF `ScaleTransform`/zoom ile **ölçeklenmez** → zoom'da yazı boyutu `SetTheme` ile değiştirilir.
  - WPF içeriği terminalin **üstüne çizilemez** (airspace). Terminal üstünde açılır menü,
    tooltip vb. tasarlarken bunu hesaba kat.
  - Kaydırma/zoom/sürükleme sırasında canlı terminaller gizlenir, yerine `TerminalSnapshot`
    ile alınan anlık görüntü gösterilir ("fotoğraf modu"). Hareket bitince 150 ms sonra
    canlı terminale dönülür. Bu davranışı bozacak değişikliklerde akıcılığı mutlaka elle test et.
- Ayrıntı: `docs/mimari.md`, kararların gerekçesi: `docs/kararlar/`.

## Kurallar

- **Commit mesajları**: Conventional Commits, açıklama **Türkçe ve Türkçe karakterlerle**.
  Ayrıntı ve örnekler `CONTRIBUTING.md` içinde.
  - Commit'lere **imza eklenmez**: `Co-Authored-By`, "Generated with Claude Code" vb.
    satırlar **kullanılmaz**.
  - Küçük değişiklikte tek satır yeterli; davranış değiştiren ya da gerekçe isteyen
    değişikliklerde gövdeye *neden*i yaz.
- Kod içi yorumlar ve kullanıcıya görünen metinler Türkçe; tanımlayıcılar (sınıf, metot,
  değişken adları) İngilizce.
- Mevcut kod stiline uy; `.editorconfig` geçerlidir.
- Yeni bağımlılık eklemeden önce lisansını kontrol et (MIT/Apache/BSD uyumlu olmalı).
- Performansı etkileyen değişikliklerde ölçümü (RAM, akıcılık) PR açıklamasına yaz.
- Yol haritası `docs/yol-haritasi.md` ve GitHub milestone'larında tutulur; iş bitince issue'yu
  commit/PR ile kapat (`Closes #12`).
