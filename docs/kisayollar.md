# Klavye kısayolları

Kısayollar terminal odaktayken de çalışır. Terminal programlarının sık kullandığı tuşlardan kaçınmak
için çoğu `Ctrl+Shift` iledir (ör. `Ctrl+T` bash/readline'da karakter yer değiştirir, ona dokunulmaz).

| Kısayol | İş |
|---|---|
| `Ctrl+Tab` | Sonraki terminale geç: görünümü kaydırır ve terminale odaklanır |
| `Ctrl+Shift+Tab` | Önceki terminale geç |
| `Ctrl+Shift+F` | Tüm terminalleri sığdır |
| `Ctrl+Shift+Enter` | Şu anki terminale yakınlaş (görünür alanın ~%90'ı, en fazla %100 zoom) |
| `Ctrl+Shift+T` | Yeni terminal |

**Fareyle:**

| | |
|---|---|
| Başlığa çift tık | O terminale yakınlaş ve odaklan |
| Tuvalde sağ tık | "Burada yeni terminal…" |
| Sağ alt köşe tutamacı | Terminali boyutlandır |
| Tekerlek | Yakınlaş / uzaklaş |
| Sağ tuşla sürükle | Tuvali kaydır |

## Gezinme sırası

`Ctrl+Tab` terminalleri okuma sırasıyla dolaşır: satır satır, her satırda soldan sağa. Üst kenarları
120 birimden yakın olan terminaller aynı satırda sayılır. Sona gelince başa döner. "Şu anki" terminal,
en son tıklanan ya da kısayolla gidilen terminaldir; hiçbiri yoksa görünümün ortasına en yakın olandan
başlanır.

## Nasıl çalışıyor?

Terminal odaktayken tuşlar doğrudan terminalin yerel penceresine (HWND) gider, WPF `KeyBinding`'leri
onları görmez. Terminal penceresi arayüz iş parçacığında olduğu için tuş mesajı
`ComponentDispatcher.ThreadPreprocessMessage` ile terminalden önce yakalanır
(`Views/MainWindow.Shortcuts.cs`). Kısayol listesi `Views/Shortcuts.cs`'te; bu belgeyle aynı tutulmalı.
