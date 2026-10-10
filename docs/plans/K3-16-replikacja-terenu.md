# K3-16 · Replikacja terenu: wersje, cache, sumy kontrolne

| | |
| --- | --- |
| Prowadzi | Opus 5.5 |
| Testy akceptacyjne | Opus 5.5, przed implementacją; podzadania mechaniczne Gemini 3.8 Flash |
| Przegląd | GPT-6.1 Sol; nowe kontrakty protokołu i kodek także Opus 5.5 |
| Zależy od | K3-00 (szwy); K2-09, K2-10 (graf projektu). Konsumuje istniejące narzędzia K2-13 z bramki K2; połączenie widoku budowli K3-15 na bramce K3, bez dodatkowej zależności startu |
| Gałąź | `pakiet/k3-16-replikacja-terenu` |

**Cel:** zdalny klient ma własny teren i trwały cache; edycje serwera podmieniają bloki 16³ u pozostałych klientów w najwyżej 200 ms przy RTT 100 ms, a wersje i xxHash64 pozwalają wykryć i naprawić rozjazd bez podwójnego stosowania edycji u hosta.

## Wspólne ustalenia K3

Domyślnie start po małym PR [K3-00 szwy](K3-00-szwy.md) na bramce K2. Obowiązują [README: X1–X11](README.md#x1-szwy-przed-startem-pakietów): jeden dispatch S1, budżety S4, pełne migawki S5, readiness S6, viewers S7, narzędzia S8 i własne partial S9. Rekomendowane scalanie 15 → 16 → 14 → 17 → 18; integrację wspólnego punktu wykonuje pakiet scalany później, kolejność nie warunkuje startu. Bez odpowiedzi właściciela pakiet rusza z wariantem domyślnym.

## Kontekst

- `docs/ZASADY.md`: pkt 2 (główny wątek Godota, źródło strumienia dla workerów, autorytet, wspólny teren hosta), 3 (Core, wersje i zamrożony korpus), 4, 6, 7 (błędy i zamykanie), 8.
- `docs/spec/01-planeta.md:190–240`: niezawodny kanał danych (194), cache przed gotowością (212–213), generator i bloki 16³ (224–225), predykcja/paste/kolejka/checksum (229–232), zasięg widza (236), 50 KB/s (240).
- `docs/spec/01-planeta.md:294–314`: odmowa i cofnięcie (302), naprawa sum (304), timeout (306), awaria zapisu i utrata okna 100 ms (308–312).
- `docs/spec/01-planeta.md:316–355`: sieć i boty (327–330), 200 ms i zgodne skróty (340–341), ryzyko replikacji LOD i różnic generatora (352,354–355).
- `docs/plan/plan-projektu.md`, tabela Planety, wiersze K3-14..18; `docs/plans/README.md:78–80`, D1/D4/D8 i decyzja wyłączenia Linux CI 2026-10-09.
- `docs/plans/K2-09-adapter-voxel-tools.md:439,505–523`: P1 i rozpoznanie S2–S6 (blok do 12 337 B z kopertą w próbie najgorszego przypadku; `Paste`; LOD-y zapisują się zależnie od momentu wyładowania).
- `docs/plans/K2-10-tryby-gry.md:322,335,359–364,587` i `K2-13-narzedzia.md:59,178–182,302,433,469,483,499`: gotowość, lokalny gracz, źródło cache, narzędzia i predykcja zdalna.

### Kod, na którym pakiet buduje

Nazwy i linie sprawdzone w `pakiet/k2-bramka`; nie traktować dawnych fragmentów planów jako istniejących metod.

| Co | Gdzie |
| --- | --- |
| `ITerrainBlockSource.TryLoad(TerrainBlockKey, out uint version, out byte[] blob)`; `Found` / `NotEdited` / `Unavailable` | `src/Horyzont.Persistence/Terrain/ITerrainBlockSource.cs:23`; `TerrainBlockLookup.cs:4` |
| `ServerTerrainBlocks.Record`, `TryLoad`, `CloseAsync`; bieżące bloby przed checkpointem | `src/Horyzont.Persistence/Terrain/ServerTerrainBlocks.cs:105`, `:133`, `:299` |
| `TerrainStream._LoadVoxelBlock`, `_SaveVoxelBlock`, `Close()` | `game/src/Terrain/TerrainStream.cs:95`, `:151`, `:89` |
| `TerrainBlockCodec.Encode(VoxelBuffer)`, `Decode(ReadOnlySpan<byte>, VoxelBuffer)`, `Digest(VoxelBuffer)` | `game/src/Terrain/TerrainBlockCodec.cs:39`, `:59`, `:98` |
| `TerrainDigest.Compute(IEnumerable<(TerrainBlockKey Key, uint Version, ulong VoxelDigest)>)` | `game/src/Terrain/TerrainDigest.cs:20` |
| `DerivedLodCache.Put`, `TryGet`, `DefaultMaxBlocks = 8192`; obecnie tylko pamięć sesji | `game/src/Terrain/DerivedLodCache.cs:48`, `:89`, `:14` |
| `VoxelTerrainService.ApplyEdit(TerrainEditOp, IReadOnlyList<TerrainBlockVersion>)`, `TryCopyBlock(TerrainBlockKey, VoxelBuffer)` | `game/src/Terrain/VoxelTerrainService.cs:282`, `:367` |
| `VoxelServerTerrain.Voxel`, `Service`; `_blocks` jest prywatne, brak publicznego API eksportu | `game/src/Server/VoxelServerTerrain.cs:47`, `:50` |
| `ServerRuntime.IsLocalPlayer`, `AddTickParticipant`, `AddPlayerObserver`; obserwator gotowości/wyjścia | `src/Horyzont.Server/ServerRuntime.cs:143`, `:113`, `:121`; `IServerPlayerObserver.cs:12` |
| `GameServerHooks.OnClientMessage` tylko loguje; `OnPlayerReady` już powiadamia obserwatorów | `src/Horyzont.Server/GameServerHooks.cs:116`, `:93` |
| `ServerSimulation.Applied` (zdarzenie `Action<AppliedOperation>`) | `src/Horyzont.Core/Simulation/ServerSimulation.cs:61` |
| `OwnTerrainProvider.Prepare(ServerHello)` tworzy strumień z `GeneratorOnlyBlockSource`; `ClientNode.OnServerHello` woła `SendReady` | `game/src/Client/OwnTerrainProvider.cs:51`; `game/src/Client/ClientNode.cs:63` |
| `ClientRuntime.IsHostLocal`, `AddListener`; `ClientSession.Send(Message)` w Loading/InGame zależnie od typu | `src/Horyzont.Client/Session/ClientRuntime.cs:42`, `:49`; `src/Horyzont.Net/Session/ClientSession.cs:169` |
| `TerrainCacheReport(Planet, Entries)`, `TerrainBlockData(Key, Version, Data)`, `TerrainChecksums(Planet, Entries)`; checksum ma tylko `Key`, `Hash` | `src/Horyzont.Core/Protocol/Messages/TerrainCacheReport.cs:9`; `TerrainBlockData.cs:10`; `TerrainChecksums.cs:9`; `TerrainBlockChecksum.cs:8` |
| `StateCodec.EncodeTerrainBlockBlob`, `DecodeTerrainBlockBlob`; istniejąca koperta blobu | `src/Horyzont.Core/Serialization/StateCodec.cs:414`, `:443` |
| `ProtocolVersion = 3`; limity 4096 cache entries, 1024 checksums, 64 KiB danych bloku | `src/Horyzont.Core/Protocol/ProtocolConstants.cs:7`, `:64`, `:67`, `:70` |
| `IEditPrediction.Begin(uint, Intent, in ToolPreview)`, `Commit(uint)`, `Revert(uint)` | `src/Horyzont.Client/Tools/IEditPrediction.cs:11` |
| `PendingIntentTracker.MaxPending = 16`, `DeadlineTicks = 300`, `OnReply`; `IntentReply.AppliedOpId` | `src/Horyzont.Client/Tools/PendingIntentTracker.cs:16`, `:19`, `:75`; `src/Horyzont.Core/Protocol/Messages/IntentReply.cs:10` |
| `GodotToolWorldView` wymaga `WorldState` i serwerowego `ITerrainService`; nie jest widokiem klienta zdalnego | `game/src/Client/Tools/GodotToolWorldView.cs:23` |

## Zakres

- Pierwszy test akceptacyjny: serwer i dwa niezależne tereny klientów, edycja → dane → cache/paste → identyczny digest.
- Wymiana wersji przed wejściem w grę, zainteresowanie według widzów, kolejka i limit pasma, live bloby z bufora serwera.
- Cache SQLite `cache/<WorldId>/terrain.db`, predykcja własnych edycji i cofanie odmowy/timeoutu, zdalne celowanie i narzędzia terenowe.
- Kontrola xxHash64 wygenerowanych bloków i naprawa, diagnostyka, brak drugiej kopii u solo/hosta.
- Zamknięcie odroczenia odległych LOD-ów po restarcie: trwałe dane pochodne z unieważnianiem i odbudową; test oraz ocena wizualna.

## Poza zakresem

- Replikacja/predykcja budowli i lustro bytów K3-15; wygładzanie ruchu K3-14; Steam K3-17; menu/błędy świata/HUD K3-18.
- Zmiana generatora, semantyki `ApplyEdit`, operacji lub intencji; cache klienta nie jest zapisem autorytatywnego świata. Godot nie trafia do Core/Net/Persistence.
- Godzinny test całej gry i soak 2 h należą do K4-19; pakiet dostarcza fixture i mierniki, nie uznaje ich za wykonane.

## Projekt

### Pliki

Wszystkie wymienione niżej nowe typy/metody są **nowe API**.

| Pliki | Odpowiedzialność |
| --- | --- |
| `src/Horyzont.Core/Protocol/Messages/TerrainSync.cs`, `TerrainSyncEntry.cs`, `TerrainView.cs` (nowe); `Protocol/MessageType.cs`, `MessageChannels.cs`, `ProtocolConstants.cs`, `Serialization/MessageCodec.cs` | Manifest/bariera i dane widzów, kodek oraz D4 |
| `src/Horyzont.Server/Terrain/TerrainSendQueue.cs`, `TerrainReplicationPolicy.cs` (nowe) | Scalanie kluczy, priorytet odległości, wspólny budżet S4, ograniczenia kolejki i starzenie |
| `src/Horyzont.Persistence/Terrain/ClientTerrainCache.cs`, `ClientTerrainCacheSchema.cs` (nowe) | Osobna baza klienta, trwały cache i thread-safe `ITerrainBlockSource` |
| `game/src/Server/Terrain/ServerTerrainReplicator.cs` (nowy), `game/src/Server/VoxelServerTerrain.cs` | Eksport z bieżącego `_blocks` i generatora, zdarzenia operacji, manifesty i bariery |
| `game/src/Client/Terrain/ClientTerrainReplicator.cs`, `RemoteTerrainPrediction.cs`, `RemoteTerrainRaycast.cs` (nowe) | Odbiór, podmiana, checksum, predykcja i celowanie |
| `game/src/Client/OwnTerrainProvider.cs`, `ClientNode.cs`, `game/src/Boot/GameComposition.TerrainReplication.cs` | Przygotowanie cache, gotowość, dane widzów i narzędzia |
| `game/src/Terrain/DerivedLodCache.cs`, `TerrainStream.cs`; `game/src/Terrain/DerivedLodRebuilder.cs` i `src/Horyzont.Persistence/Terrain/DerivedLodStore.cs` (nowe) | Trwałość LOD, unieważnianie, odbudowa; osobna baza pochodna serwera |
| `src/Horyzont.Net/Session/MessageRouter.cs` | Dopuszczenie nowych typów do właściwych stanów, bez rozluźniania innych reguł |
| `tests/Horyzont.Server.Tests/Terrain/TerrainSendQueueTests.cs`, `tests/Horyzont.Persistence.Tests/Terrain/ClientTerrainCacheTests.cs`, `tests/Horyzont.Core.Tests/Serialization/TerrainSyncCodecTests.cs` (nowe) | Czyste testy i parser |
| `game/tests/Terrain/TerrainReplicationTests.cs`, `TerrainPredictionTests.cs`, `DerivedLodReplicationTests.cs` (nowe) | Dwa tereny, silnik, LOD, restart, predykcja |

### Kontrakty i numeracja

- K1-03:616–623 przydziela Planecie `MessageType` 21–29, `OperationKind` 8–19, `RejectReason` 21–29, intencje 4–9, bloby 5–9. W kodzie tej gałęzi komunikaty zajmują 1–20; `RejectReason.ServerReadOnly = 21`, `BlobKind.TerrainBlock = 5`; operacje 1–7. K3-15 ma rezerwację `ResyncRequest = 21`. `TerrainSync = 24`, `TerrainView = 25` — przydział ostateczny (X4); 22–23 dla K3-18. Nie wprowadzać nowych kodów operacji, odmów, intencji ani blobów.
- `TerrainSync(uint TransferId, byte Purpose, PlanetId Planet, OpId BarrierOpId, ushort PageIndex, bool IsLastPage, IReadOnlyList<TerrainSyncEntry> Entries)` (nowy rekord S→K, niezawodny), `TerrainSyncEntry(TerrainBlockKey Key, uint Version, ulong Hash)` (nowy). `Purpose`: 0 = odpowiedź na cache przy starcie, 1 = aktualizacja zainteresowania, 2 = bariera edycji. Do 1024 wpisów/stronę, strony od 0, ten sam transfer/purpose/planeta/bariera, wpisy unikalne i posortowane. Jednocześnie najwyżej jeden transfer manifestu na planetę/klienta. Licznik transferów monotoniczny w sesji; po rozłączeniu reset. Każda strona ma wersję formatu 1 w standardowej kopercie komunikatu.
- `TerrainView(PlanetId Planet, uint ViewSeq, Vector3 VisualPosition, float VisualDistance, Vector3 CollisionPosition, float CollisionDistance)` (nowy K→S, niezawodny). Wysyłka najwyżej 5 Hz, przy zmianie bloku widza/odległości; odległości wizualne 2048/4096/8192/16384 m z `ITerrainViewerSource.TryGetViewers` (S7; `ViewDistanceFor` jest prywatne), także pozycja i odległość widza kolizyjnego. Początkowy raport w Loading przed `ClientReady`; potem InGame. Serwer sprawdza skończoność, planetę, maks. 16384 m, monotoniczną sekwencję; `CollisionPosition` ≤ 64 m od stóp postaci serwera (`ServerCharacters.TryGetState`); `VisualPosition` ≤ 64 m od nich, chyba że gracz ma dozwoloną kamerę dev; naruszenie: zignorowanie raportu i log z limitem (nie rozłączenie). Nigdy nie wczytuje całej zadeklarowanej kuli na żądanie klienta.
- Dispatch z K3-00 (S1): `ServerTerrainReplicator` implementuje `IServerClientMessageHandler.Handle → bool` (true dla `TerrainCacheReport`, `TerrainChecksums`, `TerrainView`). `IServerPlayerObserver` i `IServerTickParticipant` pozostają.
- Nowa metoda `VoxelServerTerrain.TryExportBlock(TerrainBlockKey key, out uint version, out byte[] blob) → bool`: dla edytowanego LOD 0 czyta bieżące `ServerTerrainBlocks.TryLoad`, dla niezmienionego daje wersję 0 i kanoniczny blok generatora. Zwraca niemutowalną kopertę K2; brak danych edytowanego bloku nie powoduje generowania zamiennika. Odczyt terenu tylko na głównym wątku, worker dostać może skopiowany blob. Eksport generatora po jednym bloku jest zadaniem ograniczonym budżetem, nie synchronicznym generowaniem całej okolicy.
- Nowy `ClientTerrainCache : ITerrainBlockSource`: `Put(TerrainBlockKey key, uint version, ulong hash, byte[] blob)`, `Invalidate(TerrainBlockKey key)`, `CopyVersions(PlanetId planet, Span<TerrainBlockVersion> destination, int offset) → int`, `CloseAsync() → Task<bool>`. Wyłącznie zweryfikowane autorytatywne dane, nigdy kopia po predykcji. Wersja 0 z danymi oznacza poprawkę generatora, więc `Found`, a nie `NotEdited`. Nieznany i niepotwierdzony klucz może być generowany tymczasowo; znany zmieniony blok bez danych to `Unavailable` i blokada predykcji w obszarze.
- Nowy `ClientTerrainReplicator` implementuje `IClientSessionListener`; przygotowanie przez `Prepare(ServerHello hello)`, `OnMessage(Message)`, `OnDisconnected(DisconnectCode)`. `ClientNode` wysyła `TerrainCacheReport` i pierwszy `TerrainView` **po otwarciu cache i przed `SendReady`**; stan gameplay pozostaje zamrożony przez `IClientWorldReadiness` (S6): nowy `TerrainSyncReadiness` zwraca true po pustej stronie końcowej purpose 0 dla każdej planety z `ServerHello`; do tego czasu `LocalPlayer` nie nagrywa i nie wysyła wejść. Sesyjny `ClientReady` uruchamia dostawę serwera, nie oznacza jeszcze gotowości lokalnych kolizji.
- `RemoteTerrainPrediction : IEditPrediction` tylko dla osobnego terenu. `Commit` oznacza przyjęcie intencji, nie otrzymanie wszystkich bloków. Replikator wiąże `IntentReply.ClientSeq/AppliedOpId` z barierą; `RemoteTerrainPrediction` implementuje `IIntentReplyObserver` (S8); `ToolNode` woła je przed `Pending.OnReply` (przez `CompositeEditPrediction` po scaleniu K3-15). Nie zmieniać istniejącej sygnatury `Commit(uint)`.
- Nowy `RemoteTerrainRaycast : IRemoteTerrainRaycast` (S8) w `game/src/Client/Terrain/RemoteTerrainRaycast.cs` — raycast własnego terenu klienta (główny wątek, bez alokacji). `GameComposition.TerrainReplication.cs` przekazuje go do facade `RemoteToolWorldView` (S8) i `RemoteTerrainPrediction` do `CompositeEditPrediction` (K3-15; do scalenia K3-15 bezpośrednio do nowego przeciążenia `ToolNode.Initialize`). Solo/host bez zmian. Publiczny `IClientTerrainView` nadal nie ujawnia `VoxelTool` ani mutowalnego `VoxelLodTerrain`.

Format nowych komunikatów: standardowy typ + wersja 1; identyfikatory i liczniki kodowane istniejącą konwencją varuint, `Purpose`/bool jako pojedynczy bajt (bool tylko 0/1), hash jako UInt64 little-endian, pozycje jako trzy skończone Single. `TerrainSync`: pola w kolejności konstruktora, następnie count i wpisy klucz/wersja/hash; klucz identyczny jak w istniejących wiadomościach terenu. `TerrainView`: pola w kolejności konstruktora. Maks. 1024 wpisy/stronę sprawdzane przed alokacją; bariera 0 dla purpose 0/1, niezerowa dla 2, pusta strona końcowa dozwolona. Router według X5: 24 S→K Loading/InGame przez `IsGameMessageFromServer`; 25 K→S Loading i InGame, także `IsGameMessageFromClient`; jawne `IsKnownType`. Oba typy mapować wyłącznie na ReliableOrdered.

Nowy `TerrainSendQueue.Enqueue(PlayerId player, TerrainBlockKey key, uint version, byte[] immutableBlob, int priorityClass, float distanceSquared)`, `TryTake(PlayerId player, long timestamp, out TerrainBlockData? data) → bool`, `Remove(PlayerId player)`, `Clear()`. `timestamp` pochodzi z wstrzykniętego `TimeProvider.GetTimestamp()`, częstotliwość jest parametrem konstruktora; nie czytać zegara systemowego wewnątrz polityki. Nowy `TerrainReplicationPolicy` definiuje tylko kolejkę, priorytety i starzenie; korzysta ze wspólnego S4. Replikator serwera implementuje `IServerTickParticipant`, `IServerPlayerObserver` i `IServerClientMessageHandler` z S1; przy wyjściu usuwa raport, widzów, bariery i queue gracza. Replikator klienta tworzy `TerrainBlockChecksum` wyłącznie ze scratch bez nakładki, a odczyt metryk udostępnia nowym niemutowalnym `TerrainReplicationStatistics` (plik pod `src/Horyzont.Client/`, jeden typ na plik) dla K3-18.

### Decyzje: przepływ wersji i danych

1. **Start/cache:** baza klienta ma metadane schema=1, `WorldId`, build hash, profil i parametry każdej planety oraz wersję semantyki. Niepasująca metadana unieważnia cache, ale nie dotyka świata serwera. Raport wysyłany porcjami ≤4096 wpisów; `ClientReady` kończy raport. Serwer limituje łączny raport do 65 536 kluczy na planetę; nadmiar klient pomija deterministycznie (najbliższe najpierw), brak raportu oznacza zimny cache, a nie błąd.
2. **Manifest:** serwer odpowiada dla sumy zgłoszonych kluczy cache i edytowanych kluczy w zasięgu obu widzów. Najpierw manifest określający autorytatywną wersję/hash, potem brakujące `TerrainBlockData`, następnie pusta ostatnia strona transferu jako marker zakończenia. Strony z wpisami mają `IsLastPage=false`; jedyny finał jest pusty. Klient nie usuwa niezgłoszonych kluczy na podstawie ich nieobecności w stronie. Wpis o wersji 0 usuwa stary edytowany blob albo zastępuje go poprawką generatora; przy złym hash serwer dostarcza dane także dla wersji 0.
3. **Utrata nietrwałej partii:** cache może mieć wersję wyższą od serwera, a po restarcie ta sama liczba wersji może oznaczać inne bajty. Manifest wymusza autorytatywną wersję **i hash**, pozwala na downgrade i zastąpienie równej wersji o innym hash. Monotoniczne odrzucanie starszych danych obowiązuje tylko wewnątrz bieżącej sesji poza zadeklarowanym transferem. Dane z transferu przyporządkowane są kluczom jego manifestu i kolejności kanału; brak danych nie uprawnia do oznaczenia transferu ukończonym.
4. **Live edycja:** subskrypcja `ServerSimulation.Applied` po zastosowaniu `TerrainEditOp`; klucze i wersje liczyć z aktualnego stanu i tych samych `TerrainMath.AffectedBlocks`, nie z promienia klienta. Kolejka scala wielokrotne zmiany jednego klucza do najnowszej pary wersja/blob. Odbiorcy: zasięg widza, autor zawsze, z pominięciem `IsLocalPlayer`; klasa `Live` budżetu (X3) dla autora i widzów kolizyjnych ≤ 64 m; pozostali widzowie wizualni `Bulk`. Dla autora po danych wysłać `TerrainSync` purpose=2 z `BarrierOpId` i ostatecznymi parami klucz/wersja/hash (stronicowanie jak wyżej). Przy scaleniu edycji jeden najnowszy blob może zamknąć wiele barier; usunięcie predykcji wymaga wszystkich kluczy każdej bariery, nie tylko ACK intencji.
5. **Odbiór/paste:** przed cache i sceną sprawdzić kopertę `TerrainBlockCodec.Decode` do prywatnego scratch 16³; zły blob rozłącza nadawcę/loguje i nie psuje dobrego cache. `VoxelTool.Paste(Vector3I dst, VoxelBuffer src, int channelsMask)` jest zweryfikowane w K2-09 S6; `dst = (X,Y,Z)*16` w voxelach, także dla ujemnych współrzędnych, maska używanych kanałów. Załadowany/edytowalny blok podmienić na głównym wątku; niezaładowany trafia do cache, a strumień poda go przy odczycie. Odbiór w wyścigu z workerem musi pozostawić najnowszy blob w źródle oraz ponownie paste po załadowaniu starego odczytu.
6. **Pasmo:** kolejka per klient ≤4096 kluczy, nie przechowuje tysięcy duplikatów blobów. Przekroczenie usuwa najdalsze zadania i oznacza potrzebę ponownego manifestu, nie gubi informacji o wersjach. Priorytet: autor/kolizje w pobliżu, następnie odległość do widza, remis klucz; starzenie 1 s zapobiega głodzeniu. Teren nie ma własnego wiadra: `TerrainBlockData`/`TerrainSync` zużywają wspólne `IReplicationSendBudget` (S4, 27 000 B/s, 65 536 B) — `Live` dla danych i barier edycji na żywo, `Bulk` dla manifestów purpose 0/1 i napraw checksum. `TerrainReplicationPolicy` definiuje tylko kolejkę, priorytety i starzenie. Burst jednego standardowego narzędzia w promieniu 8 m może wykorzystać bucket; czas 200 ms nie dotyczy zimnego cache i nieograniczonej burzy edycji, które mierzyć osobno. Blok >64 KiB odrzuca kodek, nie dzielić go niejawnie na inny format. Test limitu liczy rzeczywiste ramki, nie tylko długości blobów.
7. **Predykcja:** najwyżej 16 oczekujących intencji zgodnie z trackerem. Przechowywać autorytatywne bazy dotkniętych bloków i listę lokalnych sfer; po nowych danych/odmowie odtworzyć bazę oraz nadal oczekujące edycje w kolejności `ClientSeq`. Nie cofać przez „odwrotną kulę”. Preview określa lokalną sferę, serwer nadal sam rozwiązuje raycast. Materiał z rejestru powitalnego; nie stosować `OperationApplier` na klientowym świecie ani nie bumpować autorytatywnych wersji. Duży zasięg niezaładowany → tylko wizualny preview, bez lokalnej mutacji. Timeout 300 ticków i disconnect cofają od najnowszej; zaakceptowana intencja czeka na barierę najwyżej 5 s, potem manifest i usunięcie spekulacji, nigdy zapis przewidzianych voxeli.
8. **Checksum:** co 5 s do 32 bloków LOD 0 wygenerowanych bez predykcji, najbliższych w promieniu 64 m widza kolizyjnego; rotacja deterministyczna, cały cykl obejmuje kandydatów. `TerrainBlockCodec.Digest` to kanoniczne wartości kanałów, nie hash skompresowanego blobu. `TerrainChecksums` ma już kod 7 i limit 1024; nie zmieniać jego formatu. Ponieważ nie zawiera wersji, porównywać tylko z blokami niezmienionymi; jeśli blok serwera już edytowano, dosłać aktualny bez zgłaszania błędu generatora. Edytowane bloki sprawdza manifest/hash. Przy różnicy generatora serwer wysyła swój blok wersji 0; licznik napraw i log `[LoggerMessage]` z graczem, kluczem, hash i buildem. Maks. 32 sprawdzenia/5 s/klienta; nie udostępniać oracle generacji całej planety.
9. **Cache IO:** WAL, tabela `(planet,lod,x,y,z,version,hash,data)` i metadane; zapis w osobnym writerze z kolejką ≤256 bloków, source najpierw widzi aktualne niemutowalne dane w pamięci. Cache na dysku ≤512 MiB, roboczy overlay ≤64 MiB; LRU usuwa tylko autorytatywne dane niepotrzebne aktywnej predykcji, aktywne klucze pinowane. Uszkodzona/pełna baza klienta: log, unieważnienie złej pozycji i przejście na cache pamięciowy/ponowny manifest, bez trybu bezpiecznego serwera. Testy SQLite w kolekcji `SqlitePools`, bez `ClearAllPools`.
10. **Gorące ścieżki:** `OnTick` pustej kolejki i wybór zadania bez alokacji; alokacja koperty/rekordu przy nowym bloku jest kosztem zdarzenia, nie „0 B dla całej transmisji”. Mierzyć ją i liczbę kopii, nie udawać że istniejący dekoder nie alokuje. Codec/hash wykonywać poza pętlą selekcji, do 2 bloków/klatkę na klienta; pomiar p95 frame <50 ms i średni tick serwera ≤8 ms przy 8 graczach raportować, pełny benchmark prowadzi K4-19.

### LOD-y: odroczenie K2-09 P1

- S5 potwierdza, że samo zapisywanie callbacków wyładowania nie gwarantuje zachowania odległego wykopu. Domyślnie implementować trwałe LOD-y pochodne oraz test po restarcie; nie uznawać `DerivedLodCache` za rozwiązanie trwałości.
- `DerivedLodStore` zapisuje na własnym wątku writera; główny wątek przekazuje skopiowane bloby kolejką.
- Nowy `DerivedLodStore` w osobnej bazie `terrain-derived.db` obok świata, schema=1; klient używa osobnych tabel pochodnych własnego cache. Nie potrzeba migracji autorytatywnego `world.db`. Każdy wpis: klucz LOD>0, fingerprint profilu/semantyki/wersji dzieci i blob. Te dane można usunąć i odbudować, nie są checkpointem ani dziennikiem.
- Nowy `DerivedLodRebuilder.Invalidate(TerrainBlockKey lod0Key)`, `Step(int maxBlocks)`, `TryLoad(TerrainBlockKey key, out byte[] blob) → bool`. Przodek na poziomie L ma współrzędne `floor(x/2^L)` itd., także dla ujemnych x. Każda nowa baza LOD0 unieważnia wszystkich przodków; znane zmienione przodki nie mogą podać starego blobu jako aktualnego.
- Krok rozpoznania przed kodem ustala dokładną, dostępną w forku procedurę mipmapowania i zgłaszania przeładowania; nie zakłada istnienia metody `Downscale`. Odbudowa jednego rodzica bierze 8 aktualnych dzieci (edytowane z blobów, pozostałe z generatora), stosuje **ten sam** filtr i format kanałów co Voxel Tools, zapisuje fingerprint i propaguje w górę. Wykonawca zapisuje nazwy/pliki forka oraz porównanie bajtów z mipmapą edycji w „Odstępstwach”. Jeśli brak dostępnego bindingu, dodać izolowany adapter w `game/src/Terrain/` po przeglądzie; nie zmieniać `voxel.ref` ani filtra edycji. Jeżeli to wymaga zmiany silnika/semantyki, zatrzymać wyłącznie ten krok i eskalować kontrakt, nie ukrywać deferral.
- Zadania odbudowy po skopiowaniu dzieci mogą działać na workerze, bez drzewa sceny; paste/reload tylko główny wątek. Max 2 pochodne bloki/klatkę i 8192 bloków w pamięci, dirty queue ograniczona i trwałe fingerprinty odtwarzane z wersji LOD0 po restarcie. Brak gotowego pochodnego bloku oznacza oczekiwanie/odbudowę; nie cichy fallback do nieedytowanej planety dla znanego wykopu.
- Serwer przechowuje pochodne lokalnie; przez sieć autorytetem są tylko LOD0. Klient liczy i trzyma własne LOD-y, bez nowych komunikatów LOD. Test musi obejmować wykop na granicy bloków, wyładowanie, restart i kamerę orbitalną bez ponownego zbliżenia do wykopu.

### Chronione kontrakty i zamykanie

- Zmiany `Protocol/` i `Serialization/` podbijają `ProtocolConstants.ProtocolVersion = (main w chwili scalenia) + 1`. K2 ma 3; 4 jest tylko kandydatem, K3-15 może scalić wcześniej. Aktualizacja istniejących `ProtocolVersion_MatchesDocumentedMergeValue` i `Constants_GoldenValues` (X4); bez nowego testu wersji; rebase aktualizuje stałą/test/plan. Nie zmieniać zamrożonego początku `ClientHello` i `HandshakeReject`.
- Operacje/intencje, `TerrainRules` i `TerrainEditSemantics` pozostają z K2. Gdy planowany adapter wymaga zmiany tych kontraktów, potrzebny format operacji/intencji z identycznym replay starej wersji oraz przegląd Opus. Sam bump protokołu tego nie załatwia. Zamrożony korpus terenu i `DeterminismTests` bez zmian (D1).
- D8: zamykanie sesji → opróżnienie → `OnShutdown` (replicator odpina zdarzenia i czyści zadania) → wyjścia graczy → checkpoint → writer → serwerowy teren/blocks → magazyn tylko gdy drain=true → krok 7a: `DerivedLodStore.CloseAsync()` zarejestrowane `ServerRuntime.AddAfterTerrainShutdown` (opróżnia własny writer, kolejka ≤ 256, termin 5 s z `TimeProvider`; przekroczenie: log, bez zamknięcia używanej bazy). Zdalny klient: cofnąć spekulację, zamknąć streams, przerwać zadania pochodne, drain odczytów i cache writer (termin 5 s przez `TimeProvider`), dopiero close SQLite. Timeout: log i zostawienie aktywnego readera procesowi, bez zamknięcia używanej bazy. Lokalny host nie ma client-cache writera.

### Odroczenia przejęte

| Źródło | Realizacja / granica |
| --- | --- |
| K2-08:61,339,444 | Checksum, licznik częstości rozjazdów, artefakt build/profile; mechanizm naprawy testowany przez sztuczny błąd. Rzeczywiste porównanie Windows–Linux niewykonane przy wyłączonym Linux CI, nie zastępuje go sztuczny test |
| K2-09:62,176,280,331–334,402,439,522–523 | Cache/source, koperta K2, digest, export live, paste S6 oraz trwała odbudowa LOD i wizualny odbiór |
| K2-10:65,184,322,335,359–364,587 | Dispatch komunikatów, raport przed ready, źródło klienta, `IsLocalPlayer` i brak danych/cache u hosta |
| K2-13:59,178–182,302,433/P5,469,499 | `IEditPrediction`, tracker, raycast terenowy i realny `ToolNode` na `--connect`; szkielet `RemoteToolWorldView` i `CompositeEditPrediction` daje K3-00 (S8), K3-15 wypełnia w nim metody budowli i graczy, którym K3-16 wstrzykuje teren oraz predykcję; `ToolNode` przyjmuje widok i predykcję przez przeciążenie `Initialize` z K3-00 (S8); nie tworzyć drugiego facade ani drugiego `ToolNode` |
| K2-13:483 | Test ręczny kopania/zasypywania na zdalnym kliencie przejmuje K3-16; budowlana część 5×5×3 wymaga K3-15 i odbywa się na bramce K3, bez rozszerzania tego pakietu o replikację budowli |

### Równoległość i mapa kolizji

Po K3-00 wszystkie K3-14…18 startują równolegle po swoich zależnościach K2. Mapa X10 w [README](README.md) i reguła „integruje pakiet scalany później” obowiązują także poza rekomendowaną kolejnością 15 → 16 → 14 → 17 → 18.

| Kolizja | Rozdział odpowiedzialności |
| --- | --- |
| 15/16: `GameServerHooks.cs`, `ServerRuntime.cs` | Dispatch z K3-00; 16 tylko rejestruje handler, dodatkowo rejestruje zamykanie LOD przez istniejący `AddAfterTerrainShutdown` |
| Protocol/Serialization/router i fixture | Każdy własne wiersze X4–X6, późniejszy pakiet dopisuje i aktualizuje D4 |
| 14/16: `LocalPlayer.cs` | 16 czyta S6/S7, nie edytuje `LocalPlayer.cs` |
| 15/16: `IToolWorldView`, `ToolNode`, predykcja | Facade z S8 w Client; 15 wypełnia budowle/graczy, 16 wstrzykuje raycast i predykcję przez własny partial; `ToolNode.cs` bez zmian |
| Kompozycja, `ClientNode.cs` | 16 edytuje `GameComposition.TerrainReplication.cs`, przygotowuje cache przed ready; pozostali własne partial S9 |
| 16/17: gotowość hosta | `IsHostLocal`/`IsLocalPlayer`, nie typ transportu, wykluczają drugą lokalną kopię |
| CI/gdUnit4 | Jeden Windows runner `horyzont`, fixed pipe, testy silnika szeregowo; bez Linux CI |

## Testy akceptacyjne (napisz najpierw)

**Najpierw** utworzyć pierwszy scenariusz dwóch klientów Godota; bazą `game/tests/Terrain/` i prawdziwy kodek/paste, nie dwa mocki tego samego bufora. Czyste testy czasu/kolejki: `FakeTimeProvider`, `InMemoryTransport`, latency 50 ms w każdą stronę (RTT 100), seedy 16/160/1600; 5% utraty dotyczy tylko kanału zawodnego, kanał danych terenu jest niezawodny. Test partycji osobno wstrzymuje ten kanał. gdUnit4 `Slow` z terminem 60 s/scenariusz (dłuższy test oznaczyć osobno), prawdziwy czas silnika raportowany; SQLite w `SqlitePools`.

| Test | Scenariusz → oczekiwany wynik |
| --- | --- |
| `Replicate_TwoRemoteClients_TerrainDigestsMatchWithin200ms` | Pierwszy test: edycja promień 1 m z 8 m, oba tereny załadowane; RTT 100 ms, czas od zastosowania serwera do podmiany danych/kolizji/render mesh ≤200 ms, wersje i `TerrainDigest` zgodne; pomiar według X11 w README: opóźnienie dostarczenia (`FakeTimeProvider`, 50 ms + jitter) + rzeczywisty czas odbiór→mesh/kolizja (`Time.GetTicksUsec`), suma ≤200 ms; raport obu składników osobno, `Slow`, termin 60 s, wyjątek od ZASADY pkt 8 dla czasu silnika |
| `Prepare_CachedVersions_AreReportedBeforeClientReady` | Capture ramek: report/view przed ready; gracz nie przewiduje na niezweryfikowanych kolizjach |
| `Synchronize_ColdCacheAndLateJoin_ReceivesAllEditedBlocksInView` | Nowy klient po wielu edycjach i ruch widza; kompletna lista, brak polegania na 500 m zainteresowania bytów |
| `Synchronize_ClientAheadOrSameVersionDifferentHash_ReplacesWithServerState` | Crash traci partię i ponowna edycja używa tej samej wersji; downgrade/hash naprawia, brak starej geometrii |
| `Receive_UnloadedBlock_IsPastedAfterLoadWithoutStaleRead` | Odbiór podczas worker read; cache aktualny i ponowny paste, ujemne współrzędne |
| `Receive_CorruptOrOversizedBlob_PreservesLastGoodCacheAndClosesPeer` | Truncated/zły format/dekompresja i >64 KiB; scena i dobra baza bez zmian |
| `Decode_TerrainSyncAndView_InvalidLimitsFailBeforeAllocation` | Roundtrip, złe purpose/page/planet/NaN, >1024 wpisów, duplikaty kluczy, fuzzing; wyjątek protokołu złapany na granicy |
| `Queue_RepeatedEdits_SendsLatestVersionAndClosesEveryBarrier` | 3 operacje tego samego bloku; najwyższy blob, 3 bariery poprawnie kończą predykcję |
| `Queue_DistantFlood_RespectsByteBudgetAndDoesNotStarveNearbyEdits` | 8 odbiorców i 4096 zadań; liczba ramek/bajty, limit i starzenie, overflow wymusza resync |
| `Host_LocalPlayer_ReceivesNoTerrainAndUsesOneTerrainInstance` | Host + klient Steam/UDP lub atrapa zdalna; zero cache/report/danych dla lokalnego, edycja dokładnie raz |
| `Predict_AcceptedReplyBeforeBlocks_RemovesOverlayOnlyAfterBarrier` | ACK najpierw, bloki później i odwrotna kolejność; wszystkie klucze bariery, zero podwójnej kuli |
| `Predict_OverlappingEditsAndRejection_RebuildsFromAuthoritativeBase` | Dwie nakładające się sfery i cudza zmiana; odmowa jednej zachowuje drugą i dane serwera |
| `Predict_TimeoutOrDisconnect_RevertsWithoutSavingSpeculation` | 300 ticków, 16 intencji, wyjście; rollback od końca, restart cache bez lokalnych edycji |
| `UseTools_RemoteClient_CanAimDigAndFillWithoutServerWorldState` | Rzeczywisty `ToolNode`, teren własny, promień oka; serwerowe limity i odmowy nadal działają |
| `Check_GeneratedBlockMismatch_RepairsVersionZeroAndCountsMismatch` | Jeden celowo zły voxel generatora; dosłany serwerowy blob version=0, kolejne checksum zgodne, log/licznik jeden |
| `Check_ServerEditedBlock_DoesNotReportGeneratorMismatch` | Checksum klienta opóźniony wobec edycji; aktualne dane, bez fałszywej metryki generatora |
| `Cache_RestartCorruptionOrDiskFull_RecoversWithoutServerMutation` | Ciepły start, zła metadana/blob, awaria writer; ponowna synchronizacja, autorytatywny świat bez zmian |
| `Close_ActiveStreamRead_DrainsBeforeClosingDatabase` | Trzymany reader i termin 5 s; brak `ObjectDisposedException`, timeout nie zamyka używanej bazy |
| `Rebuild_DerivedLodAfterRestart_PreservesDistantExcavation` | Wykop na granicy/ujemne klucze, nie zdążył unload callback; restart i widok orbitalny, poprawne fingerprinty i mipmapy |
| `Rebuild_NewerLod0Version_InvalidatesAllAncestors` | Zmiana bazy w czasie pracy; worker stary fingerprint nie nadpisuje nowego, porównanie z natywnym filtrem |
| `OnTick_IdleAndWarmedSelection_AllocatesZeroBytes` | 10 000 wywołań bez nowych danych + wybór z wcześniej przygotowanych zadań; 0 B; osobny raport kosztów transmisji |
| `Replicate_TwoClientsRandomEdits_FinalDigestsMatch` | `Slow`, 600 deterministycznych edycji w czasie wirtualnym; po drain wersje i hashe zgodne. Scenariusz rzeczywistych 10 min/godziny przekazany K4-19 |

## Kroki

- [ ] 0. Rebase na scalone K2 i K3-00; przydziały X4, dispatch S1, szwy S6–S9 i własność X10 są ustalone; sprawdzić kontrakty tej gałęzi.
- [ ] 1. Pierwszy test serwer + dwa tereny klientów (wymóg spec:352), potem testy cache/manifestu i kodeka. Czerwone testy przed implementacją.
- [ ] 2. Rozpoznanie adaptera mipmap/reload i pomiar paste/kolizji/render mesh; konkretne API oraz wyniki S5/S6 wpisać do „Odstępstw”. Nie zmieniać zamrożonego korpusu K2.
- [ ] 3. Nowe komunikaty, walidacja stanów, numeracja i D4; przegląd Core przez Opus przed integracją. Golden roundtrip/fuzzing zielone; fixture X6 i test routera (X5: 24 S→K Loading/InGame; 25 K→S Loading i InGame, `IsGameMessageFromClient`).
- [ ] 4. Cache schema/source/writer z testami restartu, błędów i drain; podłączyć source w `OwnTerrainProvider` i raport przed ready.
- [ ] 5. Export live, manifest, handler S1, queue/limity i bariery; test 200 ms z pomiarem danych, kolizji i obrazu.
- [ ] 6. Zdalne narzędzia i predykcja bazowa/rollback/bariera, bez server `WorldState`; host nadal bez kopii/predykcji.
- [ ] 7. Checksum i poprawki version=0, metryki dla K3-18; test symulowanego rozjazdu, bez twierdzenia o zgodności Linux.
- [ ] 8. Trwałe LOD/fingerprinty, odbudowa po restarcie, wizualny test orbitalny. Blokadę adaptera eskalować z dowodami; nie odbierać całego pakietu z tym krokiem ukrycie odroczonym. Krok LOD może trafić osobnym PR `K3-16B` na tej samej gałęzi; bramka K3 wymaga K3-16B albo jawnej decyzji właściciela (wariant alternatywny P1).
- [ ] 9. Po integracji K3-15 podłączyć budowle do facade/routera i test ręczny z K2-13:483; część terenowa oraz 5×5×3 budowla, 5 min TPP host+zdalny klient. Ryzyka/odstępstwa zapisać w PR.
- [ ] 10. Pełne `dotnet build Horyzont.sln`, `dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"`, `dotnet format Horyzont.sln --verify-no-changes`, `python tools/engine/build.py smoke`; seryjne gdUnit4 Windows. Wyniki do PR, zielone CI po merge. Małe zielone kroki kończyć Conventional Commits; po dwóch nieudanych podejściach eskalacja z testem/logiem (ZASADY pkt 12).

## Odbiór

- Dwa zdalne tereny otrzymują normalne edycje ≤200 ms przy RTT 100 ms, pomiar według X11; zgodne wersje i hashe po drain, ciepłym starcie i crash/downgrade.
- Zdalny klient rzeczywiście kopie/zasypuje i cofa odmowę; host ma jedną kopię oraz zero transmisji terenu do lokalnego gracza.
- Checksum naprawia generator version=0; stale cache i uszkodzony blob nie nadpisują poprawnej geometrii. LOD-y po restarcie pokazują odległy wykop.
- Raport actual bytes według X3 (migawki, wspólne Live/Bulk i sterowanie) i czasów X11, wyniki testów/build/format/smoke, przegląd Sol i Core Opus. Windows na jedynym runnerze; Linux `VALIDATION_NOT_PERFORMED`, bez przywracania wyłączonego CI.

## Odstępstwa i decyzje wykonawcy

## Pułapki

- Dysk serwera może zawierać starą wersję; wysyłka musi czytać bieżący `ServerTerrainBlocks`, nie sam `TerrainBlockStore`.
- Równa wersja cache po awarii nie dowodzi równych danych. Najwyższa wersja klienta nie jest autorytetem.
- `TerrainBlockData` nie zawiera `OpId`; dlatego ACK intencji nie wystarcza do zakończenia overlay, potrzebna bariera.
- `TerrainDigest.Compute` sortuje i alokuje, nadaje się do odbioru/testów, nie do każdego ticku.
- `TerrainBlockCodec` ma scratch; nie współdzielić jednej instancji równocześnie pomiędzy workerami a paste. Każdy worker własny kodek/bufor lub serializacja dostępu.
- Samo `Paste` nie znaczy jeszcze nowej siatki ani gotowych kolizji. Kryterium 200 ms mierzy wszystkie etapy na załadowanym obszarze.
- LOD-y zmieniają się asynchronicznie; brak fingerprintu i invalidacji po wersji powoduje trwałe zapisanie starego wykopu.

## Pytania do właściciela

Domyślne warianty obowiązują przy starcie bez odpowiedzi (README:42). Nie pozwalają zmieniać semantyki ani uznać niespełnionego kryterium specyfikacji za odebrane.

| ID | Pytanie | Domyślny wariant / alternatywa |
| --- | --- | --- |
| P1 | Czy odroczenie LOD z K2-09 zamykamy trwałym cache pochodnym i odbudową w K3-16? | Tak; alternatywa: jawna decyzja właściciela o późniejszym pakiecie z opisem ograniczenia widoku orbitalnego |
| P2 | Czy wspólne 27 000 B/s z K3-15 (X3), pojemność 65 536 B i cache klienta 512 MiB są dobrymi limitami na K3? | Tak; alternatywa: strojenie po raporcie actual bytes i pamięci, przy zachowaniu celu 50 KB/s |
| P3 | Czy nowe manifesty/bariera 24 i widzowie 25 przyjmujemy jako jawny kontrakt naprawy cache i końca predykcji? | Tak; alternatywa: równoważny kontrakt po przeglądzie Opus i koordynacji 15/18, nadal D4 oraz test downgrade/barier |
| X1 | Czy przyjmujemy K3-00 przed równoległym startem pakietów? | Domyślnie tak: S1–S9 na bramce K2 bez Core/protokołu; alternatywa po odrzuceniu: X10 „integruje pakiet scalany później”, K3-14 krok 6 i K3-16 krok 6 dopiero po scaleniu K3-15 |
| X3 | Czy przyjmujemy wspólny podział pasma? | Domyślnie migawki 20 480 B/s / 2 300 B, niezawodne wspólne 27 000 B/s / 65 536 B, sterowanie poza budżetem (~2,5 KB/s); alternatywa: strojenie podziału po raporcie actual bytes, limit ≤50 000 B/s, bez osobnego wiadra terenu |
