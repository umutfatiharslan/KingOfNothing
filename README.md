# King of Nothing (BitirmeProjesi)

Tiny Swords asset'leriyle yapılan 2D krallık kurma + hafif RTS oyunu (bitirme projesi). Oyun detayları GDD'de.

## Kurulum

1. **Unity 6000.4.2f1** sürümünü Unity Hub'dan kur (farklı sürüm kullanmayın, proje dosyaları değişir).
2. Repoyu klonla ve Unity Hub → **Add** ile klasörü aç (ilk açılış birkaç dakika sürer).
3. Üst menü: **King of Nothing → Setup → Create Default Map Assets**
   `Assets/Data/World` altında zemin tile set'leri, haritadaki objeler, 7 biyom, harita ayarları, referans harita
   (`ReferenceMap`), orijinal harita (`OriginalMap`); `Assets/Data/Characters` altında isim havuzu ve trait'ler,
   `Assets/Prefabs/Characters/Villager.prefab` oluşur. (Bu asset'ler küçük; commit'leyin, herkes aynı ayarlarla çalışır.)
   Asset'ler zaten varsa ve varsayılanlara dönmek istiyorsanız: **Reset Default Map Assets (overwrite)**.
4. Üst menü: **King of Nothing → Setup → Create World Scene** → `Assets/Scenes/World.unity` oluşur ve açılır.
5. **Play**'e bas. İlk Play'de orijinal harita ayarlardan bir kez üretilip `Assets/Data/World/OriginalMap` dosyasına
   dondurulur (**bu dosyayı commit'leyin**, takımın orijinali aynı olsun). Sonraki her Play bu dosyadan yüklenir.

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

## Orijinal harita ve düzenlenmiş harita (save)

| Dosya | Nerede | Ne |
|---|---|---|
| **Orijinal harita** | `Assets/Data/World/OriginalMap` (projede, git'te) | Arazi, biyomlar ve tüm objeler donmuş halde. Oynamak, developer araçları, kod veya ayar değişiklikleri onu **hiç değiştirmez**. |
| **Düzenlenmiş harita** | Her kişinin kendi bilgisayarında: `<persistentDataPath>/Saves/OriginalMap_edited.json` (git'e girmez) | Orijinalden sonra yapılan **her değişiklik** (Developer ve Default mod): silinen / eklenen / taşınan objeler, kesilen ağaç + kalan kütük, vurulan objelerin kalan canı. Geri al / ileri al geçmişi de bu dosyada. |

- Harita her açılışta: orijinal yüklenir, üstüne düzenlenmiş harita dosyası uygulanır. Değişiklikler birkaç saniyede bir ve
  oyun kapanırken otomatik kaydedilir.
- **F2 paneli**: **Undo** / **Redo** (son işlemi geri al / yeniden yap; bir objeye art arda vurulan darbeler, devrilip kütük
  kalması dahil, tek adımdır), **Reset map to original** (iki kez onay ister; düzenlenmiş harita dosyasını siler, harita
  orijinale döner — karakterler ve arazi yerinde kalır). Reset'ten sonraki değişiklikler yeni bir düzenlenmiş dosyaya yazılır.
  **Open saves folder** dosyanın olduğu klasörü açar.
- **World → MapGenerator** Inspector'ında: orijinal haritanın bilgisi, **Bake Original Map** (mevcut ayarlar + seed ile yeni
  orijinal üretir; eski düzenlenmiş dosyalar uyumsuz kalır ve uygulanmaz, `_outdated_...` adıyla saklanır),
  **Delete Edited Map**, **Open Saves Folder**. `Use Original Map` kapatılırsa harita her seferinde ayarlardan yeniden
  üretilir (test modu, hiçbir şey kaydedilmez).
- Objelerin canı / verdiği kaynak orijinale dondurulmaz; her zaman `Spawnables` asset'lerindeki güncel değerler kullanılır.

## Hareket, çarpışma ve saydamlık

- **Geçilebilen objeler**: çalılar (hız **x0.7**), otlar, kemikler, en küçük mantar (`Deco/01.png`) ve en küçük taş
  (`Deco/04.png`). **Diğer tüm objeler katıdır**: karakterler içinden geçemez, yol bulma etraflarından dolaşır.
- Her objenin kuralı kendi asset'inde (`Assets/Data/World/Spawnables/*` → **Movement & collision**): `Walk Through`,
  `Walk Through Speed`, `Hitbox Size / Offset` (katı kısım, ör. ağacın sadece gövdesi), `Fade When Hiding Character`.
  Yeni asset eklediğimizde bu alanlar ayarlanır.
- Hitbox'lar ve fiziksel etkileşim **her karakter için ayrı ayrı, çevresindeki 5 hücre** içinde devreye girer (uzaktaki
  binlerce obje hiç hesaplanmaz).
- Bir karakter bir objenin içinden geçerken veya tam arkasındayken obje **%50 saydam** olur, sonra yavaşça geri gelir.
- Tüm bu sayılar `Assets/Data/World/MovementSettings` asset'inde: etkileşim yarıçapı, saydamlık, solma hızı, karakter
  ayak yarıçapı ve gövde boyutu, takılınca yeniden yol arama süresi, su kuralları (ilk 5 su hücresi x0.5), hitbox'ları
  gösterme (debug). Asset olduğu için **Play modunda yapılan değişiklikler de kalıcıdır**.

## Seçim, emirler ve etkileşim

- **Sol tık**: karakter veya obje seç (objeler görselinin kendisinden seçilir, boş çimen değil). Boş yere tık: seçimi temizler.
- **Shift + sol tık**: karakter / obje ekle veya çıkar (tek tek çoklu seçim).
- **Sol sürükle**: mavi kutu = karakter seç. Sürüklerken (önce ya da sonra) **Shift** basılırsa kutu turuncu olur = obje seç.
- Seçili objeler turuncu renk tonuyla gösterilir (`InteractionSettings → Selected Tint`), seçili kaldıkça can barları görünür.
- **Sağ tık (yere)**: seçili karakterler yürür. **Sağ tık basılı sürükle**: RimWorld gibi çizgi üzerinde dizilim
  (işaretler yerleri gösterir).
- **Bir objeye sağ tık**: eylem menüsü. Seçili obje grubunda her tür için ayrı satır çıkar (ağaçlar **Chop**,
  taş / altın **Mine**, çalı / mantar / ot **Cut**, diğerleri **Attack**) ve her satır sadece kendi objelerine uygulanır.
  Birden fazla karakter seçiliyken satırın üzerine gelince açılır (fare çıkınca kapanır): **... individually** (her karakter kendine en yakın boştaki objeyi alır, obje başına
  bir karakter) ve **... together** (seçili karakterlerin hepsi aynı objede, grup bitene kadar sırayla).
  **Collect** (canı -1 olanlar; envanter gelene kadar işlevsiz), **Move** (taşınabilir objeler, karakterler taşır; kırmızı =
  çakışma, konamaz). Developer modda ayrıca **Destroy (Developer Mode)** ve **Move (Developer Mode)** (anında, iç içe
  geçebilir). **Esc** iptal eder. Karakter eylemleri developer modda da çalışır.
- Karakter son yaptığı eylemin animasyonlarını (ör. elinde balta) bir sonraki emre kadar kullanır; sadece yürüme emri
  verilince düz Pawn_Run / Pawn_Idle'a döner.
- Köylüler şimdilik her vuruşta **10 hasar** verir; pawn'ın iş animasyonu oynar (ağaç / kütük: balta, taş / altın: kazma,
  mantar / çalı / ot: bıçak, diğerleri: çekiç — her objenin asset'inde `Interaction Tool` ile değiştirilebilir), vuruş anında
  obje sallanır. Odun / altın / et taşırken pawn'ın taşıma animasyonu kullanılır.
- **Can barı**: vurulan objenin üstünde belirir, son vuruştan 3 sn sonra 1 sn'de kaybolur; %60 üstü yeşil, %25–60 sarı,
  %25 altı kırmızı. (Karakterlerin canı eklenince onlarda da çıkacak.)
- **Düşen eşyalar**: sadece objenin etrafındaki 1 birimlik alana (3x3, merkez dahil) 10'luk yığınlar + kalan olarak dağılır;
  alan dolunca kalanlar oradaki yığınların üstüne rastgele eklenir (ör. 10, 11, 14, 16 ...).
- Değerler: her objenin asset'inde can / düşen eşyalar / yığın / taşınabilirlik / alet / saydamlaşma; vuruş hasarı ve aralığı,
  sallanma, can barı süreleri ve renkleri, seçim renkleri, dizilim aralığı `Assets/Data/World/InteractionSettings` içinde.
- %50 saydamlaşan objeler (karakter içinden / arkasından geçerken): ağaçlar, çalılar, kaktüsler, madenler, Gold Stone 5–6, tabelalar.

## Haritayı ayarlamak

Neredeyse her şey Inspector'dan değiştirilebilir:

| Nerede | Ne var |
|---|---|
| `Assets/Data/World/MapGenerationSettings` | Boyut (1800x800), seed, kara oranı (%40), kıyı şekli, göller (sayı, boyut, gölden çıkan nehir sayısı), nehirler (kaynak sayısı, kollar, genişlik / daralma adımları, kıvrımlar), göletler, biyom yerleşimi, biyomlar arası objeler (altın madeni, kafatası + kemik, sudaki taşlar), başlangıç alanı |
| `Assets/Data/World/Biomes/*` | Her biyomun zemin tile'ları, hareket hızı (bataklık x0.25), haritadaki payı ve bölge sayısı, referans haritada nasıl yerleşeceği (`Reference Placement`), komşu kuralları (çölün etrafında kar yok), içindeki su, spawn kuralları |
| `Assets/Data/World/Spawnables/*` | Her objenin sprite'ları, kapladığı hücreler, **canı** (ağaç, taş, çalı, mantar, balkabağı, kemik, sudaki taş... hepsine vurulabilir), verdiği kaynak, ağacın kütüğü |
| `Assets/Data/World/MovementSettings` | Su kuralları (karadan itibaren ilk **5 su hücresinde** x0.5, ötesi için tekne — ileride craft edilecek), etkileşim yarıçapı, saydamlık |

Her spawn kuralının **Placement** bölümünde: yoğunluk (1000 hücre başına) veya sabit sayı, **toplam min-max sınırı**
(`Limit Count` / `Count Limit`), **hiç çıkmama ihtimali** (`Absent Chance`), haritadan haritaya miktar farkı
(`Amount Variation`), **küme boyutu** (`Cluster Size` min-max) ve küme yarıçapı. Örn. balkabağı: Grass2 + Grass3'te,
tüm haritada 150-300 adet, 1-4'lük kümeler.

Notlar:
- Bataklık ağaçları şimdilik mevcut ağaçlarla yoğun şekilde dolduruluyor; bataklık ağacı asset'i gelince `Swamp` biyomundaki
  "Swamp Trees" kuralının varyantları değiştirilecek.
- Kemiklerin yanında çıkan kafatası kuralı, kafatası asset'i gelene kadar kapalı. Eski "Skull" objesi aslında bir
  tabela/korkuluk (`Signpost`) — haritada doğmuyor, sadece developer araçlarıyla konabiliyor (ileride craft edilecek).

Ayar değişiklikleri orijinal haritayı değiştirmez: yeni ayarları haritada görmek için **Bake Original Map** gerekir.

**World** objesindeki **MapGenerator** Inspector'ında:
- **Preview Layout**: sadece arazi + biyomları hızlıca hesaplar, resmini ve istatistikleri (kara %, her biyomun %'si ve bölge sayısı) gösterir.
- **Preview New Seed** → beğenince **Use Seed** ile seed'i ayarlara yaz, sonra **Bake Original Map**.
- **Build In Scene / Clear**: haritayı (orijinal + düzenlenmiş dosya) editörde gösterir / siler.

## Kontroller

- **WASD / ok tuşları**: kamera, **Shift**: hızlı, **fare tekerleği**: zoom (imlece doğru), **orta tuş sürükle**: kaydır
- **Sol tık / sürükle**: seç, **Shift**: seçime ekle, **sağ tık**: yürüt (objeye sağ tık: eylem menüsü) — ayrıntılar yukarıda. Birimler engellerin ve nehir kıyılarının
  etrafından yol bulur; seçili birimin izlediği yol ve hedefi (RimWorld tarzı) gösterilir. Trait'ler hızı değiştirir.
- **F1**: Developer / Default mod (editörde ve development build'de)
- Developer modda **F2**: Developer Tools paneli (developer modda her şey anında olur, objeler iç içe geçebilir; fare
  panelin üzerindeyken harita zoom olmaz):
  - **Select**: seçim Default moddaki gibi; **Delete** seçilenleri siler, sağ tık menüsünde Destroy / Move (Developer Mode).
  - **Hit** (vur), **Destroy** (tık veya **basılı tutup sürükle** = fırça ile sil), **Place** (listeden seç; tık veya
    **basılı tutup sürükle** = fırça ile boya, iç içe konabilir). Bir fırça darbesi tek geri alma adımıdır.
  - **Move**: objeye tık (üst üste olanlarda en üstteki / en son eklenen) veya **sürükleyerek kutu içine al**, sonra yeni
    yerine tıkla (grup düzeni korunur).
  - Kısayollar: **Ctrl + sol tık** vur, **Ctrl + sağ tık** yok et, **sağ tık / Esc** aracı bırak.
- Paneldeki **Undo / Redo / Reset map to original** için yukarıdaki "Orijinal harita ve düzenlenmiş harita" bölümüne bakın.

`Assets/Tiny Swords Old/Deco` görselleri gerçek adlarıyla: Mushroom_Red_Small/Medium/Big, Rock, Rock_old1/2,
Bush_Green_Small/Medium/Big, Grass_Green_Small/Big, Pumpkin_Small/Big, Bone1/2, Signpost1/2, Scarecrow
(**Create Default Map Assets** eski 01..18 adlarını otomatik değiştirir).

## Kod yapısı (`Assets/Scripts`)

- `World/Generation`: Unity'den bağımsız, deterministik harita üretim çekirdeği. Sıralı pass'ler:
  Referans harita: `ReferenceTerrainPass → LakePass → DrainagePass → RiverPass → BiomeLayoutPass → WetlandPass → LandBalancePass`
  (procedural modda ilk adım `LandmassPass`). `Reference/AzgaarMapParser` .map dosyasını okur.
  Yeni bir adım eklemek için `MapGenerationPass`'ten türetip pipeline'a ekleyin. `ObjectPlacementPlanner` objelerin yerini seçer.
- `World/Persistence`: `MapSnapshot` (donmuş harita verisi), `MapChangeHistory` (değişiklikleri kaydeder, undo / redo,
  düzenlenmiş haritayı yükler), `EditedMapFile` (JSON dosyası). Objeleri değiştiren her sistem (`WorldObject` /
  `ResourceNode` olayları üzerinden) otomatik kaydedilir; yeni bir sistemin ayrıca bir şey yapması gerekmez.
- `World/Data`: ScriptableObject'ler (`MapGenerationSettings`, `OriginalMapAsset`, `BiomeDefinition`, `SpawnableDefinition`, `GroundTileSet`) ve spawn kuralları.
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
