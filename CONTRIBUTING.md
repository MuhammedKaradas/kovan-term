# Katkı Kuralları

## Dal (branch) düzeni

- `main` her zaman derlenebilir ve çalışır durumda olmalı.
- Her iş bir issue'ya bağlı kendi dalında yapılır:
  - `ozellik/<issue-no>-kisa-ad` → `ozellik/12-calisma-alani-kaydet`
  - `duzeltme/<issue-no>-kisa-ad` → `duzeltme/31-zoom-titreme`
- Dallar `main`'e PR ile, **squash merge** yapılarak alınır. PR başlığı commit kuralına uyar.

## Commit mesajları

[Conventional Commits](https://www.conventionalcommits.org/tr/v1.0.0/) kullanılır.
Tür ve kapsam İngilizce/ASCII, **açıklama Türkçe ve Türkçe karakterlerle** yazılır.

```
<tür>(<kapsam>): <açıklama>

[isteğe bağlı gövde: neden ve ne]

[isteğe bağlı alt bilgi: Closes #12]
```

### Türler

| Tür | Ne zaman |
|---|---|
| `feat` | Yeni özellik |
| `fix` | Hata düzeltme |
| `perf` | Performans iyileştirmesi |
| `refactor` | Davranışı değiştirmeyen kod düzenlemesi |
| `docs` | Yalnızca belge |
| `test` | Test ekleme/düzeltme |
| `build` | Derleme, bağımlılık, paketleme |
| `ci` | GitHub Actions vb. |
| `chore` | Diğer bakım işleri |

### Kapsamlar

`tuval`, `terminal`, `ajan`, `ses`, `alan` (çalışma alanı), `ui`, `ayar`. Gerekmiyorsa kapsam yazılmaz.

### Kurallar

- Açıklama küçük harfle başlar, emir kipiyle yazılır ("ekle", "düzelt"), sonunda nokta olmaz,
  72 karakteri geçmez.
- **Basit değişiklik → tek satır.** Davranış değiştiren, gerekçesi açık olmayan ya da birden
  fazla parçayı etkileyen değişiklik → gövdede *neden*i ve önemli ayrıntıları yaz.
- Geriye uyumsuz değişiklikte türden sonra `!` koy ve alt bilgiye `BREAKING CHANGE:` ekle.
- Commit'lere **imza satırı eklenmez** (`Co-Authored-By`, "Generated with …" vb.).

### Örnekler

Basit:

```
fix(tuval): zoom sınırında yazı boyutunun 4'ün altına inmesini engelle
```

```
docs: kurulum adımlarını güncelle
```

Detaylı:

```
perf(tuval): kaydırma sırasında terminalleri anlık görüntüyle değiştir

Terminal kontrolü kendi HWND'sinde çizildiği için her karede yeniden
konumlanıp boyutlanıyor, bu da kaydırma ve zoom'da takılmaya yol
açıyordu. Hareket başlarken her terminalin görüntüsü alınıp canlı
terminal gizleniyor; hareket bittikten 150 ms sonra geri getiriliyor.

Closes #5
```

## Issue'lar

- Her issue bir milestone'a (sürüm) ve en az bir `tür:` ve `alan:` etiketine bağlanır.
- Özellik issue'larında **Kabul kriterleri** bölümü olmalı.
