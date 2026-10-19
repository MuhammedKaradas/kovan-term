# Profiller

Profil, yeni bir terminalin hangi programla başlayacağını tanımlar. "+ Yeni terminal" düğmesi ya da
tuvalde sağ tık → "Burada yeni terminal…" ile açılan pencerede profil ve çalışma dizini seçilir.

## Varsayılan profiller

| Id | Ad | Çalıştırılan |
|---|---|---|
| `powershell` | PowerShell | `powershell.exe -NoLogo` |
| `pwsh` | PowerShell 7 | `pwsh.exe -NoLogo` |
| `cmd` | cmd | `cmd.exe` |
| `claude` | Claude Code | `claude` |
| `codex` | Codex | `codex` |
| `gemini` | Gemini CLI | `gemini` |

Çalıştırılabilir dosya `PATH` ve `PATHEXT` kullanılarak aranır. Bulunamayan profiller pencerede soluk
görünür ve seçilemez. `.cmd`/`.bat` dosyaları (npm ile kurulan CLI'lar böyledir) `cmd.exe /k` ile
başlatılır; ajan kapansa da terminal kullanılabilir kalır.

## Kendi profillerini eklemek

`%APPDATA%\KovanTerm\profiles.json` dosyasını oluştur. Aynı `id` varsayılan profili ezer, yeni `id`
listeye eklenir. Yorum satırları ve sondaki virgül desteklenir.

```jsonc
[
  // Claude Code'u plan modunda başlat
  { "id": "claude", "name": "Claude Code (plan)", "executable": "claude", "arguments": "--permission-mode plan", "color": "#D97757" },
  // Yeni bir ajan
  { "id": "aider", "name": "Aider", "executable": "aider", "color": "#14B014" },
  // Tam yol da verilebilir
  { "id": "git-bash", "name": "Git Bash", "executable": "C:\\Program Files\\Git\\bin\\bash.exe", "arguments": "--login -i" }
]
```

| Alan | Zorunlu | Açıklama |
|---|---|---|
| `id` | ✔ | Benzersiz kimlik; çalışma alanında düğümle birlikte saklanır |
| `name` | ✔ | Listede ve düğüm başlığında görünen ad |
| `executable` | ✔ | Program adı (`PATH`'te aranır) ya da tam yolu |
| `arguments` | | Komut satırı argümanları |
| `color` | | Düğüm başlığının altındaki vurgu rengi (`#RRGGBB`) |

Dosya okunamazsa uygulama yalnızca varsayılan profillerle açılır. Değişiklikler uygulama yeniden
başlatılınca geçerli olur.

## Çalışma alanı açılırken

Profili bilinen düğümlerin komutu güncel profilden yeniden kurulur; böylece CLI başka bir yere
taşınmışsa da doğru yol kullanılır. Program bulunamazsa terminal başlatılmaz, düğümde
"Terminal başlatılamadı" mesajı gösterilir.
