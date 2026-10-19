# Yol Haritası

Güncel durum için GitHub'daki [milestone'lara](https://github.com/MuhammedKaradas/kovan-term/milestones)
bak. Bu belge büyük resmi ve sıralamanın gerekçesini anlatır.

## v0.1 Temel: günlük kullanılabilir tuval

Hedef: Kovan Term'i her gün açıp ajanlarla çalışabilmek.

| # | İş | Not |
|---|---|---|
| #1 | MVVM yapısına geçiş ve proje düzeni | **İlk yapılacak**, diğer her şeyin zemini |
| #2 | Çalışma alanını kaydet ve geri yükle | Öncelikli |
| #3 | Ajan profilleri ve yeni terminal ekleme menüsü | |
| #4 | Terminal düğümlerini yeniden boyutlandırma | |
| #5 | Fotoğraf modu iyileştirmeleri | Akıcılık |
| #6 | Ekran dışındaki terminalleri uyut | Akıcılık |
| #7 | Klavye kısayolları ve odak gezinme | İyi ilk iş |
| #8 | Terminal bağımlılığı riskinin araştırılması | Karar kaydı çıkacak |
| #9 | Kurulum paketi ve sürüm yayınlama | Sürümün son adımı |

## v0.2 Ajan farkındalığı

Hedef: Hangi ajanın çalıştığını, hangisinin beni beklediğini bir bakışta görmek.

| # | İş |
|---|---|
| #10 | Claude Code hook köprüsü ile ajan durumunu algıla |
| #11 | Bekleyen ajan bildirimleri ve kontrol paneli |
| #12 | Codex ve Gemini CLI durum desteği |
| #13 | Ajan oturumlarını sürdürme |
| #14 | Kullanım ve kota takibi |

## v0.3 Çalışma alanı

Hedef: Plan, görev, önizleme ve kod aynı tuvalde.

| # | İş |
|---|---|
| #15 | Not düğümü (markdown) |
| #16 | Görev panosu (kanban) düğümü |
| #17 | Web önizleme düğümü (WebView2) |
| #18 | Git worktree entegrasyonu |
| #19 | Gruplar (alanlar) ve proje ayrımı |

## v0.4 Ses

Hedef: Konuşarak yazmak ve tuvali sesle yönetmek. Bu, benzer projelerden ayrıştığımız özellik.

| # | İş |
|---|---|
| #20 | Yerel dikte (Whisper.net) |
| #21 | Sesli komut planlayıcı |

## Sıralamanın gerekçesi

1. **Önce temel, sonra özellik:** Kalıcılık ve MVVM olmadan eklenen her özellik sonradan yeniden
   yazılır.
2. **Ajan farkındalığı sesten önce:** Sesli komutların çoğu ("bekleyen ajana git") ajan durumunu
   bilmeyi gerektirir.
3. **Akıcılık her sürümde ölçülür:** Projenin var olma sebebi hafiflik. Yeni düğüm türleri
   (WebView2 dahil) fotoğraf moduna uymalı.
