# King of Nothing (BitirmeProjesi)

Tiny Swords asset'leriyle yapılan 2D krallık kurma + hafif RTS oyunu (bitirme projesi). Oyun detayları GDD'de.

## Kurulum

1. **Unity 6000.4.2f1** sürümünü Unity Hub'dan kur (farklı sürüm kullanmayın, proje dosyaları değişir).
2. Repoyu klonla ve Unity Hub → **Add** ile klasörü aç (ilk açılış birkaç dakika sürer).
3. Üst menü: **King of Nothing → Setup → Create Default Map Assets**
   `Assets/Data/World` altında zemin tile set'leri, haritadaki objeler, 7 biyom, harita ayarları, referans harita
   (`ReferenceMap`), developer düzenlemeleri (`MapEdits`); `Assets/Data/Characters` altında isim havuzu ve trait'ler,
   `Assets/Prefabs/Characters/Villager.prefab` oluşur. (Bu asset'ler küçük; commit'leyin, herkes aynı ayarlarla çalışır.)
   Asset'ler zaten varsa ve varsayılanlara dönmek istiyorsanız: **Reset Default Map Assets (overwrite)**.
4. Üst menü: **King of Nothing → Setup → Create World Scene** → `Assets/Scenes/World.unity` oluşur ve açılır.
5. **Play**'e bas. Harita oyun başlarken üretilir (birkaç saniye). Aynı seed = herkeste aynı harita.

> Harita sahneye kaydedilmiyor, bu yüzden `World.unity` küçük ve repoda tutulabilir.
> Harita, `Assets/Map Source/World Map.map` (Azgaar) referans haritasından üretilir — **bu dosyayı silmeyin**.
> Eski `Assets/Scenes/RTSMap.unity` (~750 MB) ve `Assets/Scripts/Editor/AzgaarMapReader.cs` artık kullanılmıyor, silebilirsiniz.

## Harita nasıl üretiliyor

- **Map Source = Reference Map** (varsayılan): kıta şekilleri, nehirler, göller ve biyom alanları referans haritadan gelir,
  sonra kurallara uyarlanır: kara %40'a tamamlanır (kıyılar gürültülü, düz kenar yok), nehirler denize kadar uzatılır
  ve kıvrılır, biyom sınırları dalgalandırılır. Çöl (%5, 2-4 büyük alan) ve kayalık (%3, 10-14 orta alan) seed'e göre
  yerleştirilir; diğer biyomlar referanstaki yerlerinde kalır. Referans dosyası / boyut / nehir ayarları değişince
  `ReferenceMap` asset'i editörde kendini otomatik yeniden import eder.
- **Map Source = Procedural**: tamamen seed'den rastgele harita (ileride kullanmak için duruyor).

## Haritayı ayarlamak

Neredeyse her şey Inspector'dan değiştirilebilir:

| Nerede | Ne var |
|---|---|
| `Assets/Data/World/MapGenerationSettings` | Boyut (1800x800), seed, kara oranı (%40), kıyı şekli, göller (sayı, boyut, gölden çıkan nehir sayısı), nehirler (kaynak sayısı, kollar, genişlik / daralma adımları, kıvrımlar), göletler, biyom yerleşimi, biyomlar arası objeler (altın madeni, kafatası + kemik, sudaki taşlar), başlangıç alanı |
| `Assets/Data/World/Biomes/*` | Her biyomun zemin tile'ları, hareket hızı (bataklık x0.25), haritadaki payı ve bölge sayısı, referans haritada nasıl yerleşeceği (`Reference Placement`), komşu kuralları (çölün etrafında kar yok), içindeki su, spawn kuralları |
| `Assets/Data/World/Spawnables/*` | Her objenin sprite'ları, kapladığı hücreler, **canı** (ağaç, taş, çalı, mantar, balkabağı, kemik, sudaki taş... hepsine vurulabilir), verdiği kaynak, ağacın kütüğü |
| `Assets/Data/World/MapEdits` | Developer araçlarıyla yapılan kalıcı harita düzenlemeleri |
| **World → MapData** (sahnede) | Su hareket kuralları: karadan itibaren ilk **5 su hücresinde** x0.5 hızla yürünür, ötesi için tekne gerekir (tekne ileride craft edilecek) |

Her spawn kuralının **Placement** bölümünde: yoğunluk (1000 hücre başına) veya sabit sayı, **toplam min-max sınırı**
(`Limit Count` / `Count Limit`), **hiç çıkmama ihtimali** (`Absent Chance`), haritadan haritaya miktar farkı
(`Amount Variation`), **küme boyutu** (`Cluster Size` min-max) ve küme yarıçapı. Örn. balkabağı: Grass2 + Grass3'te,
tüm haritada 150-300 adet, 1-4'lük kümeler.

Notlar:
- Bataklık ağaçları şimdilik mevcut ağaçlarla yoğun şekilde dolduruluyor; bataklık ağacı asset'i gelince `Swamp` biyomundaki
  "Swamp Trees" kuralının varyantları değiştirilecek.
- Kemiklerin yanında çıkan kafatası kuralı, kafatası asset'i gelene kadar kapalı. Eski "Skull" objesi aslında bir
  tabela/korkuluk (`Signpost`) — haritada doğmuyor, sadece developer araçlarıyla konabiliyor (ileride craft edilecek).

**World** objesindeki **MapGenerator** Inspector'ında:
- **Preview Layout**: sadece arazi + biyomları hızlıca hesaplar, resmini ve istatistikleri (kara %, her biyomun %'si ve bölge sayısı) gösterir.
- **Preview New Seed** → beğenince **Use Seed** ile seed'i ayarlara yaz (takım aynı haritayı görür).
- **Generate In Scene / Clear**: haritayı editörde tam olarak üretir / siler.

## Kontroller

- **WASD / ok tuşları**: kamera, **Shift**: hızlı, **fare tekerleği**: zoom (imlece doğru), **orta tuş sürükle**: kaydır
- **Sol tık / sürükle**: birim seç, **Shift**: seçime ekle, **sağ tık**: yürüt. Birimler engellerin ve nehir kıyılarının
  etrafından yol bulur; seçili birimin izlediği yol ve hedefi (RimWorld tarzı) gösterilir. Trait'ler hızı değiştirir.
- **F1**: Developer / Default mod (editörde ve development build'de)
- Developer modda **F2**: Developer Tools paneli — **Hit** (vur), **Destroy** (tek tıkla yok et), **Place** (listeden obje
  seç ve koy), **Move** (objeyi tut, yeni yerine tıkla). Kısayollar: **Ctrl + sol tık** vur, **Ctrl + sağ tık** yok et,
  **sağ tık / Esc** aracı bırak.
- Developer düzenlemeleri kalıcıdır: `Assets/Data/World/MapEdits` asset'ine kaydedilir, Play'den çıkınca kaybolmaz ve
  her üretimde aynı haritaya tekrar uygulanır (seed / boyut değişirse uygulanmaz). Panelde **Clear saved edits** ile sıfırlanır.

## Kod yapısı (`Assets/Scripts`)

- `World/Generation`: Unity'den bağımsız, deterministik harita üretim çekirdeği. Sıralı pass'ler:
  Referans harita: `ReferenceTerrainPass → LakePass → DrainagePass → RiverPass → BiomeLayoutPass → WetlandPass → LandBalancePass`
  (procedural modda ilk adım `LandmassPass`). `Reference/AzgaarMapParser` .map dosyasını okur.
  Yeni bir adım eklemek için `MapGenerationPass`'ten türetip pipeline'a ekleyin. `ObjectPlacementPlanner` objelerin yerini seçer.
- `World/Data`: ScriptableObject'ler (`MapGenerationSettings`, `BiomeDefinition`, `SpawnableDefinition`, `GroundTileSet`) ve spawn kuralları.
- `World/Runtime`: `MapGenerator` (her şeyi çalıştırır), `TilemapPainter`, `MapObjectSpawner`, `MapPreviewTexture`.
- `World/Objects`: `WorldObject → ResourceNode → TreeResource / StumpObject / GoldMineResource`, `DecorationObject`. Kesilen ağaç kütük bırakır; kütük kaldırılmadan oraya bina konamaz (`MapData.CanPlaceStructure`).
- `Map`: `MapData` (hücre → arazi / biyom / obje, yürünebilirlik, hız, kar kuralı), `MapSorting` (çizim sırası).
- `Characters`: `Villager` (kimlik, rol, statlar 0-20, ihtiyaçlar, trait'ler), `TraitDefinition` (trait şablonu), `StartingPartySpawner` (sürgün edilen 3 kişi).
- `Core`: `GameModeManager` (Developer / Default mod). `Interaction`: developer mod fare etkileşimi.
- `World/Navigation`: `GridPathfinder` (A*, hücre hızlarına göre). `Units`: hareket, seçim, yol göstergesi. `Camera`: RTS kamera. `Editor`: menüler, varsayılan asset kurucu, MapGenerator Inspector'ı.

Kodlar ve yorumlar İngilizce yazılır.

## Çalışma düzeni

- Çalışmaya başlamadan önce: `git pull`
- Her iş için ayrı branch açın, bitince GitHub'da Pull Request ile `main`'e birleştirin.
- **Aynı sahneyi (.unity) veya prefab'ı aynı anda iki kişi düzenlemesin**; Unity sahne dosyalarında
  çakışma çözmek çok zordur. Kim hangi sahnede çalışıyorsa önceden haber versin.
