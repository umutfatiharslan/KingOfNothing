# King of Nothing (BitirmeProjesi)

Tiny Swords asset'leriyle yapılan 2D krallık kurma + hafif RTS oyunu (bitirme projesi). Oyun detayları GDD'de.

## Kurulum

1. **Unity 6000.4.2f1** sürümünü Unity Hub'dan kur (farklı sürüm kullanmayın, proje dosyaları değişir).
2. Repoyu klonla ve Unity Hub → **Add** ile klasörü aç (ilk açılış birkaç dakika sürer).
3. Üst menü: **King of Nothing → Setup → Create Default Map Assets**
   `Assets/Data/World` altında zemin tile set'leri, haritadaki objeler, 7 biyom, harita ayarları, referans harita
   (`ReferenceMap`), orijinal harita (`OriginalMap`); `Assets/Data/Characters` altında isim havuzu ve trait'ler,
   `Assets/Prefabs/Characters/Villager.prefab` oluşur; ayrıca binalar (`Assets/Data/World/Buildings`), `GameplaySettings`,
   asker / rahip animasyon setleri ve Gold Coin. Bridge_All.png ve Arrow.png otomatik dilimlenir. (Bu asset'ler küçük; commit'leyin, herkes aynı ayarlarla çalışır.)
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
  taş / altın **Mine**, çalı / mantar / ot **Cut**) ve her satır sadece kendi objelerine uygulanır. Bu işleri sadece
  köylüler yapar; seçimde asker varsa **Attack** satırı da çıkar (aşağıda).
  Birden fazla karakter seçiliyken satırın üzerine gelince açılır (fare çıkınca kapanır): **... individually** (her karakter kendine en yakın boştaki objeyi alır, obje başına
  bir karakter) ve **... together** (seçili karakterlerin hepsi aynı objede, grup bitene kadar sırayla).
  **Carry loot** (yerdeki yığınlar: odun, altın, gold coin, taş, kemik... — karakter eline alır, aşağıda),
  **Collect** (canı -1 olan diğerleri; işlevsiz), **Move** (taşınabilir objeler, karakterler taşır; kırmızı =
  çakışma, konamaz). Developer modda ayrıca **Destroy (Developer Mode)** ve **Move (Developer Mode)** (anında, iç içe
  geçebilir). **Esc** iptal eder. Karakter eylemleri developer modda da çalışır.
- **Yeni emir eskisini hemen keser**: karakter bir yere yürürken (ör. sağ tıkla yürüt, hemen ardından Mine) yürümeyi yarıda
  bırakır ve doğrudan yeni hedefe gider.
- **Çalışma yeri**: karakter objenin hitbox'ının hemen yanında (sağ veya sol, hangisi yakınsa; ikisi de doluysa önü / arkası),
  objenin tabanıyla aynı hizada ve bir tık önünde durur, böylece aletin ucu objeye değer. Büyük objelerde (binalar) birden
  fazla karakter farklı yerlere dağılır. Ayarlar: `InteractionSettings → Work Spot` (mesafe 0.28, ön kayma 0.05, tolerans,
  karakterler arası aralık).
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
- %25 saydamlaşan objeler (karakter içinden / arkasından geçerken): ağaçlar, çalılar, kaktüsler, madenler, Gold Stone 5–6,
  tabelalar ve binalar.

## Binalar, askerler ve envanter

**İnşa etmek**: ekranın sol altındaki **Build (B)** düğmesi veya **B** tuşu → bina seç → yarı saydam kopya fareyi izler
(kırmızı = konamaz: kara değil / önünde obje var / kale sınırı / köprü kuralı / malzeme yok) → sol tık = **şablon** (template).
Şablon %25 saydam ve 1 canla durur; **köylüler kendiliğinden başlamaz**: köylü seç → şablona sağ tık → **Build (start
construction)**. Can 1'i geçince saydamlık kalkar. **Shift** basılıysa aynı binadan bir tane daha konur; sağ tık / Esc
iptal. Şablona sağ tık → menünün ilk satırı **Give up template** (yapmaktan vazgeç; getirilen malzeme yere bırakılır).
Başlamış bir şantiye anında silinemez: sağ tık → **Destroy (tear down)** ile karakterler (köylüler çekiçle, askerler
silahlarıyla) canı bitene kadar yıkar; yıkılınca yapım malzemesinin **%50'si** yere loot olarak düşer
(`GameplaySettings → Construction Salvage Share`).
İnşa / tamir eden köylü binanın yanında çekiçle çalışır ve bina her vuruşta sallanır; her vuruş
`GameplaySettings → Build Power Per Hit` (50) can ekler. Yanında gün sayacı durur.

**Malzeme**: Default modda şablon, yapım malzemesi (`Build Cost`) **tamamen getirilene kadar** inşa edilemez; malzeme
depodan kendiliğinden düşülmez, **köylüler taşır**. Şablona sağ tık → menüde getirilen malzeme görünür (ör.
`Materials: Wood 10/30, Rock 0/10`); **Supply materials**: seçili köylüler eksik malzemeyi en yakın kaynaktan (yerdeki
yığınlar veya kale / ev depoları) yük yük getirir. Elinde gerekli loot olan karakterler **Deliver carried loot** ile
doğrudan teslim eder (Stock loot da çalışır). Malzeme tamamlanınca **Build** açılır. Şablondan vazgeçilirse getirilen
malzeme yere loot olarak bırakılır. **Developer modda bedava** (malzeme gerekmez). **Sol üstteki kaynak çubuğu**
depolardaki odun, taş, altın, gold coin ve diğer eşyaları gösterir.

| Bina | Asset | Tam can | Yıkık hal canı | Maliyet (Default mod) | Kapasite | Envanter |
|---|---|---|---|---|---|---|
| Castle (ana kale, **tek**) | Castle.png | 10000 | 4000 (Castle_Destroyed) | 200 odun, 150 taş, 30 altın | sınırsız | 9 çeşit x 100 |
| School (okul) | Barracks.png | 5000 | 2000 (Castle_Destroyed x0.6) | 80 odun, 40 taş | 4 (sadece köylü) | - |
| Army House (orduevi) | Archery.png | 5000 | 2000 (Castle_Destroyed x0.6) | 80 odun, 40 taş | 2 (köylü, asker) | - |
| Church (kilise) | Monastery.png | 7000 | 2000 (Castle_Destroyed x0.6) | 100 odun, 60 taş, 10 altın | 4 | - |
| House 1/2/3 (kapı güney / güneybatı / yan) | House1-3.png | 3000 | 1000 (House_Destroyed) | 30 odun, 10 taş | 2 (her meslek) | 4 çeşit x 100 |
| Archer Tower | Tower.png | 6000 | 3000 (Tower_Destroyed) | 60 odun, 50 taş | 1 (üstünde) | - |
| Bridge (yatay / dikey) | Bridge_All.png | 2000 | 1500 (3 aşama, 500'er) | 20 odun | - | - |
| Furnace (fırın, geçici görsel) | Avatars_16.png | 1000 | 300 (House_Destroyed x0.75) | 20 odun, 30 taş | - | 3 çeşit x 30 |
| Gold Mine (altın madeni) | GoldMine_Inactive / Active | 2000 | 1000 (GoldMine_Destroyed) | 40 odun, 20 taş | 2 (sadece köylü) | - |

İnşaat görselleri tam canın içindeki eşiklerdir: Castle_Construction < 4000; House_Construction x1.5 < 2000 (okul,
orduevi, kilise); House_Construction < 1000 (evler); Tower_Construction < 3000; köprü < 500 / 1000 / 1500;
GoldMine_Destroyed < 1000 (maden). Can eşiği geçince bitmiş görsel görünür ama inşaat tam cana kadar sürer.

- **Üç ayrı can**: inşaat (şablon → tam can), **tam hal** ve **yıkık hal**. Tam halin canı bitince bina yıkık görsele geçer
  ve yıkık halin kendi canı **yeniden yeşilden** başlar (can barı yeşil → sarı → kırmızı). Yıkık halin canı da bitince bina
  kaybolur. **Bina tamamen yok olana kadar tamir edilebilir**: yıkık hal tamir edilip canı dolunca bina **1 canla** geri
  gelir, tamir tam cana kadar devam eder. Yıkılırken içerideki karakterler hemen dışarı çıkar ve canları **1–25** arası
  rastgele bir değere düşer (canı zaten 25 veya altındaysa mutlaka şimdikinden düşük bir değere; en az 1). **Kale tamamen yok olunca oyun biter** (Game Over; `Pause On Game Over`).
- **Hasar ve bakım**: askerler binalara saldırabilir; bitmiş / yıkık her binanın canı **her oyun gününde 1 azalır**
  (`Decay Per Day`, şablon ve şantiyeler hariç). Bir oyun günü **24 dakika** (`Day Length Minutes`). Hasarlı veya yıkık
  binaya köylülerle sağ tık → **Repair**.
- **%25 saydamlık**: karakter Barracks, Archery, Castle, Monastery, House1-3, Tower'ın (ve ağaç, çalı gibi objelerin)
  arkasına geçince. Artık sprite'ın görünen pikselleri esas alınıyor (resmin saydam kenarı değil), bu yüzden bina karakter
  hitbox'ın hemen arkasındayken saydamlaşır, çok uzaktayken değil. Değer `MovementSettings → Occluded Alpha` (0.75).
- **Köprüler**: karaya veya suya, karakterlerin ulaşabildiği başka bir yapıyla çakışmayan her yere konabilir (developer
  modda çakışabilir). Su üzerinde karakterler sadece köprülerin üstünden yürür: karşıya geçmek için köprüler birbirine
  (ve kıyıya) bitişik konmalıdır, aradaki su boşluğu boşluk olarak kalır. Uç uca bitişen köprülerin birleşen uçlarında
  kazıklar çizilmez, tek yol gibi görünür (köprü hücre hücre çizilir: `Tile Start / Middle / End`). Dik bir köprüye
  yandan yatay bir köprü eklenince (veya tersi) eklenen köprünün ucu kazıksız çizilir ve diğer köprünün kenarının
  üstüne kısa bir parça uzanır, aralarında boşluk kalmaz; köylüler yeni köprüyü dokunduğu bitmiş köprünün üstünde
  durarak inşa eder. Serbest uçlarda (ve sadece yandan dokunulan uçlarda) **dik destek kütükleri her zaman** durur.
  Birleşim parçası iki köprünün de altında çizilir, kütükler ve tahtalar üstte kalır. Köprüler hiçbir modda üst üste
  konamaz (birbirine değerek birleşir). Köprüler yıkılabilir: köprüye sağ tık → **Destroy (break the bridge)** (köylüler
  ve askerler; önce tam hal, sonra yıkık hal canı biter).
- **Sudan düşmeme**: karakterler yürünemeyen bir hücreye (kıyıdan 5 hücreden uzak derin su) asla adım atmaz; köprünün
  kenarından suya inmek yerine kenar boyunca kayar. Altındaki köprü yıkılırsa karakter oradan dışarı yürüyebilir. İnşa ve tamir eden köylüler suya girmez: köprünün ucundaki karada (veya
  bitmiş bir köprüde) durur; kuru yer yoksa iş yapılmaz. Son inşaat aşaması geçilince üzerinden yürünür.
- **Binaya sağ tık menüsü**: **Build / Repair**, **Cancel construction**, **Enter** (seçili karakterler kapıya yürüyüp içeri
  girer, kapasiteye ve kimin girebileceğine bakılır), **Character list**, **Inventory**, **Stock loot**, kulede **Climb
  tower** / **Come down from tower**, fırında **Collect Gold Coin**, madende **Mine** / **Destroy**, askerler seçiliyse
  **Attack**. Hiç karakter seçili değilken de binaya sağ tıklanabilir (liste, envanter).
- **Character list**: pencere daha uzun; her karakterin kendi düğmeleri var. Evler ve kale: **Exit**. Okul / orduevi:
  **Exit**, **Improve stat** (stat sistemi gelene kadar gri). Orduevi: **sınıf değiştirme** (Warrior / Lancer / Archer,
  geri dönmek için Villager; rahip hariç). Kilise: **Exit**, **Heal** (20 gold coin, depolardan — kale önce), **Pay
  indulgence** (10, gri — ruh sağlığı yok), rahip için **Skill development** (gri), **Random skill +1** (1000, gri),
  **Become priest** (rahip sadece kilisede açılır). Kule: **Come down**, **Face left / right**.
- **Okçu kulesi**: karakter seçiliyken kuleye sağ tık → **Climb tower** (kapasite 1). Karakter kulenin tepesinde durur,
  sağa / sola bakabilir; inmek için kuleye veya karaktere sağ tık → **Come down from tower** (veya başka yere yürüt).
  Üstünde okçu varken kule +500 can alır, okçunun menzili x2 olur, kulede kaldığı her dakika okçuluk +1 (en fazla +5,
  inince kaybolur, tekrar çıkınca sıfırdan). **Fear of Heights** trait'li okçu kulede işe yaramaz (trait kuleye çıkınca
  ortaya çıkar). Tümü kulenin asset'inde (`Tower` başlığı).
- **Altın madeni**: haritadaki madenler **yıkık** (GoldMine_Destroyed) başlar; köylülerle **Repair** edilince çalışır.
  **Mine**: köylüler (en fazla 2) madene girer, her biri 5 sn'de 1 Gold_Resource çıkarır; altınlar kapının önünde 10'luk
  yığınlar olur. Maden 500 altından sonra tükenir. İçeride biri çalışırken GoldMine_Active görünür. **Attack**: köylüler
  (kazmayla) ve askerler madene saldırıp kırabilir. Maden Build menüsünden de inşa edilebilir. Askerler madene giremez. Değerler madenin asset'inde
  (`Gold mine` başlığı; `Spawnables/GoldMine*` asset'leri artık bina asset'i, GUID'leri aynı kaldı).
- **Envanter / loot**: yerdeki bir yığına sağ tık → **Carry loot**: karakter yığının üstüne yürür, eline alır (odun / altın
  pawn'ın taşıma animasyonuyla) ve başka yere yürütülse de bırakmaz. Bir binaya sağ tık → **Stock loot**: binaya götürüp
  envanterine koyar (sadece bitmiş binalar). Karaktere sağ tık → **Drop loot** yere bırakır; başka bir iş emri de önce yükü
  bırakır. Elinde aynı loot varken yerdeki yığına **Carry loot** denirse yığın eldekine eklenir (elde 2 + yerde 4 odun =
  elde 6), yığın boyutuna kadar (odun 10; sığmayan yerde kalır). **Inventory** penceresinde eşyalar görünür; seçili bir karakter **Take out** ile dışarı taşıyabilir.
  Yerdeki küçük loot'ların (odun, en küçük altın, gold coin, en küçük taş, en küçük mantar) içinden / üstünden geçilir;
  item yığınları asset ne derse desin yolu kapatmaz.
- **Gold coin**: haritada küçültüldü (`Visual Scale` 0.35, köylünün eline sığar) ve odun gibi 10'luk yığınlar halinde taşınır.
- **Fırın**: girdiler Gold_Resource + Wood Resource (envanterine Stock loot ile), **1 altın + 3 odun → 1 gold coin**
  (Icon_03), 8 sn. Çalışırken üstünden 3 hücre yükseğe Dust_01 dumanı çıkar (dumanla etkileşim yok). Coin'ler
  **Collect Gold Coin** ile alınır, kaleye / eve stoklanabilir. Görsel geçici: asset gelince `Furnace` asset'inin sprite'ı
  değiştirilecek.
- **Askerler**: Warrior (kılıç, iki saldırı animasyonu sırayla), Lancer (mızrak, yöne göre saldırı sheet'i), Archer (ok,
  menzil **10 hücre**), Monk (rahip). Pawn'larla aynı mantık: idle / run, saldırırken silah animasyonu.
  **Köylü işlerini sadece köylüler yapar**: askerler ağaç kesemez, bina inşa / tamir edemez, taş / altın kıramaz, madene
  giremez (madene **Attack** ise köylüler de yapabilir). Askerler sadece **çalıları kesebilir** (Cut) ve en küçük mantarları
  toplayabilir (Carry loot); büyük mantarları kıramaz. Asker(ler) seçiliyken binaya sağ tık → **Attack**: bina yok olana veya oyuncu onları başka yere yürütene kadar
  saldırırlar.
- **Oklar**: daha küçük (`Arrow Scale` 0.55), uçarken arkasında hava izi (`Arrow Trail`) var. Okçular okçuluk statına göre
  ıskalayabilir (stat 0'da %40, stat 20'de %5; `Miss Chance`); ıskalanan ok hedefin yanına düşer ve Arrow.png'deki **ucu
  olmayan ok** olarak yere saplanır, **3 oyun saati** sonra kaybolur (`Stuck Arrow Game Hours`; 1 oyun saati = 1 dakika).
  Hasarlar ve aralıklar `GameplaySettings → Combat`.
- Değerler: genel kurallar `Assets/Data/World/GameplaySettings`, her bina `Assets/Data/World/Buildings/*` (tam / yıkık can,
  inşaat / yıkık görselleri ve eşikleri, kapasite, kimin girebileceği, kapı, envanter, **Build Cost**, kule / kilise / orduevi /
  fırın / köprü / maden kuralları), sınıf animasyonları `Assets/Data/Characters/*Animations_Blue`. Hepsi Play modunda da kalıcı.
- Binalar, evreleri (şablon / inşaat / tam / yıkık) ve canları düzenlenmiş haritaya kaydedilir ve Undo / Redo ile geri
  alınır. **Depolardaki eşyalar da kaydedilir ama Undo / Redo onları geri almaz** (envanter sistemi gelince bağlanacak).
  İçerideki karakterler kaydedilmez (karakterler her oyunda yeniden doğar).

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
- **B**: Build menüsü (inşa), **Esc / sağ tık**: yerleştirmeyi iptal
- **F1**: Developer / Default mod (editörde ve development build'de)
- Developer modda **F2**: Developer Tools paneli (developer modda her şey anında olur, objeler iç içe geçebilir; fare
  panelin üzerindeyken harita zoom olmaz). Panel F2 ile kapatılınca seçili sekmenin işlevi durur, tekrar açılınca geri
  gelir. Fare başka bir panelin / düğmenin (Build düğmesi, kaynak çubuğu...) üzerindeyken araçlar haritaya bir şey yapmaz:
  - **Select**: seçim Default moddaki gibi; **Delete** seçilenleri siler, sağ tık menüsünde Destroy / Move (Developer Mode).
  - **Hit** (vur), **Destroy** (tık veya **basılı tutup sürükle** = fırça ile sil), **Place** (liste **alfabetik**; seçilen
    objenin yarı saydam kopyası fareyi izler; tık veya **basılı tutup sürükle** = fırça ile boya, iç içe konabilir). Loot'lar
    (odun, altın, coin...) iç içe binmez, **yığınlanır**: aynı hücredeki aynı yığına eklenir (yığın boyutuna kadar), yığın
    doluysa yanındaki boş hücreye konur. Bir fırça darbesi tek geri alma adımıdır.
  - **Characters**: meslek seç (Villager, Warrior, Lancer, Archer, Priest, King), haritaya tıkla = o meslekte yeni karakter
    (prefab `StartingPartySpawner`'dan; karakterler kaydedilmez).
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
- `World/Buildings`: `Building` (ResourceNode'dan türer: şablon / inşaat / tam / yıkık evreleri, tamir, hasar, çürüme,
  içeridekiler, kule, envanter, fırın, maden), `BridgeRules` (köprülerin uç uca birleşmesi), `BuildingInventory`,
  `GameClock` (gün sayacı), `SmokeEmitter`. Bina verisi `World/Data/BuildingDefinition`,
  genel kurallar `World/Data/GameplaySettings`.
- `World/Objects`: `WorldObject → ResourceNode → TreeResource / StumpObject / GoldMineResource / Building`, `DecorationObject`. Kesilen ağaç kütük bırakır; kütük kaldırılmadan oraya bina konamaz (`MapData.CanPlaceStructure`).
- `Map`: `MapData` (hücre → arazi / biyom / obje, yürünebilirlik, hız, kar kuralı), `MapSorting` (çizim sırası).
- `Characters`: `Villager` (kimlik, rol, statlar 0-20, ihtiyaçlar, trait'ler), `TraitDefinition` (trait şablonu), `StartingPartySpawner` (sürgün edilen 3 kişi).
- `Core`: `GameModeManager` (Developer / Default mod). `Interaction`: developer mod fare etkileşimi.
- `World/Navigation`: `GridPathfinder` (A*, hücre hızlarına göre). `Units`: hareket, seçim, yol göstergesi, `LootCarrier`
  (eldeki loot), `Units/Jobs` (`HitObjectJob`, `BuildJob`, `AttackJob` + `Arrow`, `EnterBuildingJob`, `CarryLootJob`,
  `StockLootJob`, `WithdrawJob`, `Approach` + `WorkSpots`). `Interaction`: eylem menüsü, `BuildMenuController`, `BuildingWindows`. `Camera`: RTS kamera. `Editor`: menüler, varsayılan asset kurucu, MapGenerator Inspector'ı.

Kodlar ve yorumlar İngilizce yazılır.

## Çalışma düzeni

- Çalışmaya başlamadan önce: `git pull`
- Her iş için ayrı branch açın, bitince GitHub'da Pull Request ile `main`'e birleştirin.
- **Aynı sahneyi (.unity) veya prefab'ı aynı anda iki kişi düzenlemesin**; Unity sahne dosyalarında
  çakışma çözmek çok zordur. Kim hangi sahnede çalışıyorsa önceden haber versin.
