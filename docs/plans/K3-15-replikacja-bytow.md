# K3-15 · Replikacja bytów i budowli

| | |
| --- | --- |
| Prowadzi | GPT-6.1 Sol (Codex), wysiłek `high` |
| Testy akceptacyjne | GPT-6.1 Sol, przed implementacją; przegląd scenariuszy protokołu przez Opus 5.5 |
| Przegląd | Opus 5.5; osobno kontrakty Core i integracja K3-14/K3-16 |
| Zależy od | K2-10, K2-12; kod K2-11/K2-13 konsumowany po scaleniu bramki K2, bez nowej krawędzi między pakietami K3 |
| Gałąź | `pakiet/k3-15-replikacja-bytow` |

**Cel:** dwóch zdalnych klientów widzi te same byty i budowle po 10 minutach losowych edycji, a wejście w zasięg, utrata migawek i luka wersji nie pozostawiają niepełnej kopii świata.

## Wspólne ustalenia K3

Domyślnie start po małym PR [K3-00 szwy](K3-00-szwy.md) na bramce K2. Obowiązują [README: X1–X11](README.md#x1-szwy-przed-startem-pakietów): jeden dispatch S1, budżety S4, pełne migawki S5, readiness S6, viewers S7, narzędzia S8 i własne partial S9. Rekomendowane scalanie 15 → 16 → 14 → 17 → 18; integrację wspólnego punktu wykonuje pakiet scalany później, kolejność nie warunkuje startu. Bez odpowiedzi właściciela pakiet rusza z wariantem domyślnym.

## Kontekst do przeczytania

- `docs/ZASADY.md`: punkty 2–4, 6–8; `docs/plans/README.md:78–80`: D1, D4, D8.
- `docs/plan/plan-projektu.md:95–99`: graf K3; K3-15 zależy od K2-10/K2-12, a nie od K3-14/K3-16.
- `docs/spec/01-planeta.md:190–235`: sesja, kanały, migawki 20 Hz, pełne budowle i zainteresowanie 500 m; `:299–309`: resync i błędy; `:326–344`: sieć, testy botów i odbiór.
- `docs/plans/K1-03-core-stan-i-operacje.md:614–622`: przydział numerów; `K2-11-postac-i-kamera.md:70–71,266,481`: zastąpienie nadawcy i ograniczenie walidatora; `K2-12-budowle.md:40,353,359,460`: lustro, missing, tryby, koszt kolejki.
- `docs/plans/K2-13-narzedzia.md:59–60,178–182,433,469,483`: predykcja bloków, widok zdalny, brak narzędzi w K2 i odroczony test ręczny; `K2-10-tryby-gry.md:65,322`: stan początkowy i gotowość klienta.

### Kod, na którym pakiet buduje

Odwołania sprawdzone na `pakiet/k2-bramka`; numery linii dotyczą bazy K2, po rebase sprawdź wskazany symbol.

| Typ / składowa istniejąca | Plik:linia | Znaczenie |
| --- | --- | --- |
| `OwnStateSender.Send(Tick)`, `SendEveryTicks = 3` | `game/src/Server/Characters/OwnStateSender.cs:49`, `:16` | most 20 Hz do zastąpienia |
| `ServerCharactersBinder.OnTick(ServerRuntime)` | `game/src/Server/Characters/ServerCharactersBinder.cs:74` | krok ruchu i dotychczasowe wysyłanie |
| `ServerCharacters.TryGetState`, `ServerCharacterState.LastProcessedInputSeq` | `game/src/Server/Characters/ServerCharacters.cs:197`, `ServerCharacterState.cs:21` | kinematyka dla nowego adaptera bez zależności Server→Godot |
| `ServerSimulation.Applied` | `src/Horyzont.Core/Simulation/ServerSimulation.cs:61` | skutki operacji do niezawodnej replikacji |
| `ServerSession.Send`, `SendToInterested`, `Interest`, `PlayersInGame` | `src/Horyzont.Net/Session/ServerSession.cs:132`, `:145`, `:80`, `:83` | sesja i widoczność |
| `InterestManager.Update`, `GetChanges`, `IsVisible` | `src/Horyzont.Net/Session/InterestManager.cs:47`, `:93`, `:131` | przejścia zainteresowania |
| `GameServerHooks.OnPlayerReady`, `OnClientMessage` | `src/Horyzont.Server/GameServerHooks.cs:93`, `:116` | observer już działa; komunikaty tylko logowane |
| `ServerRuntime.AddTickParticipant`, `AddPlayerObserver`, `AddAfterTerrainShutdown` | `src/Horyzont.Server/ServerRuntime.cs:113`, `:121`, `:134` | rejestracja i zamykanie |
| `IConstructSource.TryGetChunkBlocks` | `src/Horyzont.Constructs/Sources/IConstructSource.cs:33` | interfejs lustra, 4096 komórek/chunk |
| `ConstructWorld.Initialize`, `OnApplied`, `RebuildAll`, `ShutdownAsync` | `game/src/Constructs/ConstructWorld.cs:88`, `:121`, `:173`, `:241` | meshing i kolizje |
| `ConstructChange(EntityId Construct, uint Version, bool Removed)` | `src/Horyzont.Core/Operations/ConstructChange.cs:9` | wynikowa wersja operacji |
| `Construct` konstruktor, `SetBlock`, `LoadChunk` (internal) | `src/Horyzont.Core/World/Constructs/Construct.cs:18`, `:124`, `:171` | Client nie ma dostępu; potrzebny nowy ograniczony adapter w Core, bez upubliczniania mutatorów świata |
| `ConstructHeaderRecord`, `ConstructChunkRecord` | `src/Horyzont.Core/Checkpoints/ConstructHeaderRecord.cs:16`, `ConstructChunkRecord.cs:7` | nagłówek i dane pełnego stanu |
| `ClientRuntime.AddListener`, `IClientSessionListener.OnMessage` | `src/Horyzont.Client/Session/ClientRuntime.cs:49`, `IClientSessionListener.cs:30` | odbiór wiadomości |
| `ToolNode.Initialize`, `PendingIntentTracker.MaxPending` | `game/src/Client/Tools/ToolNode.cs:93`, `src/Horyzont.Client/Tools/PendingIntentTracker.cs:16` | narzędzia i limit 16 intencji |
| `MessageCodec` obsługa `Snapshot` | `src/Horyzont.Core/Serialization/MessageCodec.cs:316`, `:642` | maski pól już w formacie |
| `ProtocolConstants.ProtocolVersion = 3` | `src/Horyzont.Core/Protocol/ProtocolConstants.cs:7` | wersja bazy K2 |

## Zakres

- Spawn/despawn graczy i budowli, zainteresowanie, migawki/delty/ACK, resynchronizacja kodem 21.
- Pełny stan budowli w częściach, niezawodne operacje bloków, autorytatywne lustro i jego `IConstructSource`, render oraz kolizje zdalnego klienta.
- Zdalne `IToolWorldView` i predykcja wyłącznie bloków; podłączenie `ToolNode`, dotąd pomijanego dla `--connect`.
- Przejęcie wszystkich wymienionych wyżej odroczeń K2-10/11/12/13. Obserwacja czasu `ChunkRemeshQueue.TryStartNext` w teście dużej budowli, bez przebudowy algorytmu kolejki.

## Poza zakresem

K3-14: predykcja i wygładzanie ruchu, interpolacja 100 ms oraz ogólna polityka priorytetów/budżetu migawek. K3-16: cache, wersje i predykcja terenu oraz bramka `SendReady`. K3-18: HUD. K4-19: godzinny test całego świata, benchmarki i 2 h soak. Render 1000 m z K2-12 nie poszerza sieciowego zainteresowania 500 m: u klienta zdalnego ogranicza go dostępny stan. Nie zmieniaj operacji ani reguł walidacji i stosowania bloków.

## Projekt

### Pliki i nowe kontrakty

Wszystkie poniższe nazwy projektowane oznaczają **nowe API**, nie istniejące symbole.

| Pliki | Nowy kontrakt / zadanie |
| --- | --- |
| `src/Horyzont.Core/Protocol/Messages/ResyncRequest.cs` | `sealed record ResyncRequest(EntityId Entity) : Message`, `MessageType.ResyncRequest = 21`; wersja formatu 1, id jako varuint, id 0 odrzucany |
| `src/Horyzont.Core/Replication/ConstructReplica.cs` | `sealed class ConstructReplica`; `static ConstructReplica Create(ConstructHeaderRecord header, IReadOnlyList<ConstructChunkRecord> chunks)`, `Construct Value { get; }`, `bool TrySet(CellPos cell, BlockState state, uint version)`, `bool TryReparent(FrameRef parent, uint parentSeq, Vector3 origin, Quaternion rotation)`; isolated ChangeTracker, bez WorldState/journal/allocatora; jedyny most do internal konstruktora/LoadChunk/SetBlock |
| `src/Horyzont.Server/Replication/EntityReplication.cs` | `IServerTickParticipant`, `IServerPlayerObserver`; `bool Handle(PlayerId player, Message message)` przez `IServerClientMessageHandler` (S1); zainteresowanie i kolejki niezawodne |
| `src/Horyzont.Server/Replication/IEntityReplicationSource.cs`, `ReplicationEntity.cs` | `int CopyEntities(Span<ReplicationEntity> destination)`, `uint GetLastProcessedInputSeq(PlayerId player)`; `readonly record struct ReplicationEntity(EntityId Id, EntityKind Kind, PlayerId? Owner, SnapshotFrame Frame, EntityState State, float BoundsRadius)` |
| `game/src/Server/Replication/ServerReplicationSource.cs` | implementuje powyższy interfejs z WorldState i ServerCharacters.TryGetState; kopie na main thread, żadnej zależności Server→game |
| `src/Horyzont.Server/Replication/ConstructStateSender.cs` | `void Enqueue(PlayerId player, EntityId construct)`; kopia nagłówka/chunków z jednego stanu, wysyłka części |
| `src/Horyzont.Server/Replication/SnapshotBuilder.cs` | `int Build(PlayerId player, Tick tick, uint lastInputSeq, Span<byte> destination)`; zwraca rozmiar ramki; bufor 1150 B, używa `ISnapshotBudget` z S4 i woła `Debit` faktycznym rozmiarem; bez alokacji po rozgrzaniu |
| `src/Horyzont.Client/Replication/SnapshotDecoder.cs` | `bool TryReconstruct(Snapshot delta, out Snapshot full)`; wynik semantycznie pełny, baseline 0; publikacja do K3-14 i walidatora własnego stanu |
| `src/Horyzont.Client/Replication/EntityMirror.cs` | `void Apply(Message message)`, `void Clear()`; tylko kopia odczytowa dla klienta |
| `src/Horyzont.Client/Replication/ConstructMirror.cs` | `IConstructSource`; `bool Apply(ConstructState part)`, `bool Apply(OperationApplied message)`; `Construct` kopii dostępny dla `IToolWorldView` |
| `src/Horyzont.Client/Replication/ConstructPrediction.cs` | `IEditPrediction` dla bloków, osobna nakładka; autorytatywne lustro bez zmian |
| `src/Horyzont.Client/Replication/CompositeEditPrediction.cs` | delegacja intencji bloków do powyższego, terenu do implementacji K3-16; domyślnie teren do `NoEditPrediction` |
| `game/src/Client/Replication/RemoteConstructs.cs`, `RemotePlayers.cs` | listener, `ConstructWorld`; lifecycle węzłów innych graczy (`EntitySpawn`/`EntityDespawn`/respawn) z `RemotePlayerPresentation` K3-14; bez ciał fizycznych i bez warstw kolizji; do scalenia K3-14 prosty marker wizualny ustawiany najnowszą próbką |
| `tests/Horyzont.Server.Tests/Replication/`, `tests/Horyzont.Client.Tests/Replication/`, `tests/Horyzont.Core.Tests/ResyncRequestCodecTests.cs`, `game/tests/Replication/` | scenariusze tabeli poniżej |

Modyfikacje: `MessageType.cs`, `MessageChannels.cs` (21 reliable ordered), `MessageCodec.cs`, `ProtocolConstants.cs`, `MessageRouter.cs`, `ServerSession.cs`, `ServerCharactersBinder.cs`, `OwnStateValidator.cs`, `GameComposition.Replication.cs`; referencja Client → Constructs w `Horyzont.Client.csproj`. `tests/Horyzont.Architecture.Tests/LayeringTests.cs:31` → `"Horyzont.Core,Horyzont.Net,Horyzont.Constructs"`. Nowe `src/Horyzont.Core/Serialization/SnapshotWriter.cs` i uzupełnienie `src/Horyzont.Client/Replication/RemoteToolWorldView.cs` (S8). Nowy ConstructReplica w Core wymaga przeglądu Opus; istniejące typy World/operacje/applier bez zmian. Dispatch pochodzi z K3-00 (S1): `EntityReplication` implementuje `IServerClientMessageHandler.Handle → bool` (true dla `SnapshotAck`, `ResyncRequest`) i rejestruje się `ServerRuntime.AddClientMessageHandler` poza tickiem. EntityReplication subskrybuje istniejące ServerSimulation.Applied i odpina subskrypcję w shutdown; nigdy nie czyta prywatnej kolejki writera.

### Decyzje wykonawcze

1. Najpierw `ServerCharactersBinder` wykonuje ruch, potem `EntityReplication` wysyła stan co 3 ticki. Wyłącz stare `_sender.Send`; zero podwójnych migawek. Własny byt ma pierwszeństwo i pełne pola do korekty; zawsze jedna migawka na odbiorcę/tick wysyłki. K3-14 może podmienić politykę wyboru pozostałych bytów bez zmiany formatu.
2. Byty tej samej planety: zasięg 500 m i istniejąca histereza 50 m (`ServerOptions.InterestHysteresisMeters`), wyjście >550 m. Dystans do granicy sfery BoundsRadius budowli, dla gracza promień 0; wartości ViewRange/Region dostarczaj do istniejących InterestSubject/InterestViewer. Własny byt zawsze widoczny. Sortuj identyfikatory; przy równym priorytecie kolejność EntityId. Spawn niezawodny przed pierwszą migawką, opuszczenie zasięgu przez `EntityDespawn`; pominięcie w migawce nie usuwa bytu. Budowlę usuniętą operacją dodatkowo usuwa `ConstructRemoved`.
3. Serwer i klient utrzymują pierścień 64 odtworzonych migawek na połączenie. Bazą wyłącznie ACK wysłanej migawki zachowanej w pierścieniu. Nieznany/przyszły ACK ignoruj i loguj z limitem; brak bazy u klienta: odrzuć deltę bez ACK; serwer wysyła pełną co 20 migawek, również gdy baza wypadła. Każda pominięta encja jest nieobecna w danej bazie, więc przy powrocie dostaje pełne pola. Porównuj pola bitowo; numery uint według arytmetyki sekwencji z oknem <2^31, test zawijania.
4. Klient rekonstruuje deltę **przed** `OwnStateValidator.Validate`; nie podawaj surowej delty do kodu K2-11. Listener surowych wiadomości nie może równocześnie uruchamiać starej ścieżki korekty. Do K3-14 trafiają pełne stany z oryginalnym tickiem/seq, bez drugiej korekty. `SnapshotDecoder` zastępuje `PassThroughFullSnapshots` (S5) i rozsyła pełne migawki do `IFullSnapshotSink` (`LocalPlayer`, adapter K3-14). K3-15 nie edytuje `LocalPlayer.cs`; jest jedynym edytującym `OwnStateValidator.cs`. `ParentSeq` starszy od znanego odrzucaj; nowszy przechowuj maks. 32 próbki/bytu do nadejścia przepięcia/spawnu, potem resync.
5. `ConstructState`: utrwal w pamięci kopię jednego nagłówka i chunków, posortowanych po kluczu. Maks. 12 chunków/część i 256 KiB/ramkę, miękki cel 32 KiB zakodowanej ramki (min. 1 chunk), podział sprawdzony faktycznym rozmiarem kodeka; pusta budowla ma jedną część. Część 0 usuwa starą widoczną kopię i rozpoczyna staging; publikuj siatkę/kolizję dopiero po komplecie. Maks. 4096 chunków/budowlę, 16 MiB staging/klienta, czas składania 5 s przez `TimeProvider`; nadmiar lub zła kolejność oznacza resync, powtarzana niepoprawna ramka protokołu rozłącza nadawcę. Odrzuć powtórzony chunk, niezgodny nagłówek/wersję/count i część bez zera.
6. Pełne części i następujące operacje idą jednym kanałem reliable ordered. Wysyłaj części danego stanu przed jego kolejnymi deltami; buforuj do 256 operacji/odbiorcę podczas transferu, przy przepełnieniu porzuć transfer i rozpocznij nowszy pełny stan. Gdy konstrukcja znika, anuluj transfer i wyślij usunięcie. Nie blokuj ticku na wielkiej budowli. Budżet z K3-00 (S4): `IReplicationSendBudget.TryConsume(PlayerId, ReplicationTraffic, int)`, jedna instancja na klienta współdzielona z K3-16, 27 000 B/s, pojemność 65 536 B; `Live` dla `OperationApplied`/`ConstructRemoved`, `Bulk` dla części `ConstructState`. Migawki mają osobne 20 480 B/s (K3-14). Kolejka reliable do 8 MiB/klienta; po przekroczeniu zastąp delty budowli jednym nowszym stanem. Kontrola sesji/odmowy nie czeka za dużym transferem. K3-16 współdzieli tę samą instancję budżetu, bez drugiego niezależnego limitu 50 KB/s; duże transfery mogą chwilowo przekroczyć cel zwykłej gry i mają osobny raport.
7. `OperationApplied` niosą tylko skutki widoczne dla odbiorcy, nie cały dziennik; luki globalnego `OpId` są legalne. Dla budowli oczekuj następnej `ConstructChange.Version`; duplikat/stara wersja ignorowane, luka zatrzymuje delty tej budowli i wysyła jeden resync. Operacja wielobudowlowa nie może ujawnić poza-zasięgowej budowli: jeśli obejmuje choć jedną niewidoczną, zamiast niej wyślij pełne stany tylko widocznych budowli (usunięte przez ConstructRemoved), bez zmiany kanonicznej operacji w dzienniku. Nie uruchamiaj `OperationApplier` na niepełnym globalnym świecie klienta; użyj dedykowanego appliera lustra.

   Lustro trzyma ConstructReplica. Create kopiuje bloki i odtwarza nagłówek/wersję bez dziennika, odrzuca powtórzone chunki i źle uformowane BlockState; dobrze uformowany nieznany TypeId zostaje w danych i jest renderowany jako missing. TrySet najpierw sprawdza version==Value.Version+1, potem raz SetBlock (Empty dla clear), a false nie zmienia danych. TryReparent przyjmuje tylko następny ParentSeq i zachowuje Version. ConstructCreate daje nową replikę, usunięcie usuwa ją ze słownika. Przetestuj kopię tablic, brak wpływu na autorytatywny WorldState i identyczność wersji; nie dodawaj InternalsVisibleTo dla całego Client.
8. Resync tylko `InGame`, reliable; dodać 21 do `IsKnownType`, `IsAllowedFromClient(InGame)`, `IsGameMessageFromClient` i `MessageChannels.For` (ReliableOrdered) zgodnie z X5; bez tego `ClientSession.Send` rzuci `ArgumentException`. Nieznany byt/zły zakres nie ujawnia świata; limit 4 żądania/s/gracza i jedno aktywne żądanie/bytu, okno liczone `TimeProvider`. Znana widoczna budowla → części od 0, inny widoczny byt → `EntitySpawn`.
9. Zdalny widok czyta planety z `ServerHello`, budowle z lustra i innych graczy z `EntityMirror`; `RaycastTerrain` deleguje do `IRemoteTerrainRaycast` (S8); do scalenia K3-16 `NoRemoteTerrain` zwraca false (zdalny klient nie ma `ITerrainService`). K3-15 wypełnia w `RemoteToolWorldView` (S8) metody budowli i graczy: `RaycastConstructs`/`TryGetConstruct`/`TryGetConstructPose` z `ConstructMirror`, `OverlapsConstruct` przez `BlockOverlap.Intersects`, `CopyOtherPlayerFeet` z `EntityMirror` (K2-13:175–177). Przejścia Godot↔Numerics wyłącznie `PlanetFrames`. Nieznane typy bloków korzystają z `core:missing`, jak K2-12; nie zmieniaj Core raycastu pełnych sześcianów.
10. Predykcja bloków jest wizualną nakładką maks. 16 wpisów, bez autorytatywnych kolizji, trwałych EntityId i zapisu. `Commit` oznacza przyjęcie intencji, ale nakładkę zdejmij dopiero po operacji wskazanej przez `IntentReply.AppliedOpId` (`src/Horyzont.Core/Protocol/Messages/IntentReply.cs:10`); przetestuj obie kolejności reply/operation. Przy zastąpieniu operacji pełnym stanem zdejmij nakładkę po zgodnym stanie komórki; twardy termin nadal 300 ticków. Odmowa, timeout, disconnect → `Revert` od najnowszej. Nowa budowla pozostaje duchem do potwierdzenia; nigdy nie zajmuje allocatora serwera. Solo/host nadal `NoEditPrediction`. `CompositeEditPrediction` implementuje `IIntentReplyObserver` (S8): `ToolNode` woła `NoteReply` przed `Pending.OnReply`; kompozyt przekazuje reply do predykcji bloków i terenu (K3-16).
11. Gorące ścieżki: wybór zainteresowania, budowanie/wysyłka migawek i zapytania narzędzi: brak LINQ, stałe bufory, test 10 000 wywołań po warmup. Istniejące rekordy wiadomości/kodek alokują: dodaj w Net **nowy** `ServerSession.SendEncoded(PlayerId, TransportChannel, ReadOnlySpan<byte>)`. Walidacja jak `Send`: pierwszy bajt spełnia `MessageRouter.IsGameMessageFromServer`, `channel == MessageChannels.For(type)` (przetłumaczony), długość ≤ `MessageChannels.MaxFrameBytes`; inaczej `ArgumentException`. Zapis bezalokacyjny w nowym `src/Horyzont.Core/Serialization/SnapshotWriter.cs`: `static int Write(Span<byte> destination, uint snapshotId, Tick tick, uint baselineSnapshotId, uint lastProcessedInputSeq, ReadOnlySpan<SnapshotFrame> frames, ReadOnlySpan<EntityState> entities)`; test `SnapshotWriter_Write_MatchesMessageCodecEncode` (FsCheck na `CodecGen`) — bajty identyczne z `MessageCodec`, format bez zmian; nie udawaj, że `new Snapshot` spełnia punkt 6. Klient może alokować przy odebraniu/spawnie, ale nie podczas interpolacji/zapytań.
12. D4: zmiany Protocol/Serialization → `(ProtocolVersion na main przy scaleniu) + 1` (po samym K2: 4). Aktualizuj stałą, istniejący `ProtocolVersion_MatchesDocumentedMergeValue` i `Constants_GoldenValues` (X4) oraz plan po rebase; nie dodawaj nowego testu wersji. Operacje/intencje i stare blob-y bez zmian; jeśli okażą się konieczne, przegląd Opus, wersja formatu oraz powtórka starej wersji. Zakresy Planety: MessageType 21–29 (21 tutaj, 22–23 K3-18), OperationKind 8–19, RejectReason 21–29 (21 zajęte), IntentCode 4–9, BlobKind 5–9 (5 zajęte). Nie dodawaj innych kodów w tym pakiecie. D1: złoty korpus i skróty bez zmian. D8: odpięcie listenerów/kolejek, zamknięcie zdalnego ConstructWorld przed zwolnieniem klienta; host zachowuje krok 7a.

### Odroczenia przejęte

| Źródło | Realizacja / granica |
| --- | --- |
| K2-10:65,322 | Stan początkowy po `OnPlayerReady`, gotowość klienta |
| K2-11:70–71,266,327,481 | Zastąpienie `OwnStateSender`, pełne migawki do walidatora |
| K2-12:40,123,353,359,460 | Lustro i `ResyncRequest`, `BlockRenderTable.Build` z `ServerHello.BlockTypes` u zdalnego klienta, `core:missing`, tryby, obserwacja `ChunkRemeshQueue.TryStartNext` |
| K2-13:60,175–178,181–182,469,483 | Widok i predykcja bloków, podpowiedzi `ConstructPlacement.OnTerrain`/`PlayerCapsule`/`BlockOverlap`, `ToolNode` na `--connect`, test ręczny 5×5×3 |
| K2-13:511 | **Odrzucone do K5-20**: pozy względne budowli wspólnego rodzica wymagają ram statków |

### Równoległość i mapa kolizji

Po K3-00 wszystkie K3-14…18 ruszają równolegle po swoich zależnościach K2. Obowiązuje mapa X10 w [README](README.md): integrację wspólnego punktu wykonuje pakiet scalany później; kolejność 15 → 16 → 14 → 17 → 18 nie jest warunkiem startu.

| Wspólna ścieżka | Właściciel fragmentu / reguła |
| --- | --- |
| Kompozycja | 15 edytuje wyłącznie `GameComposition.Replication.cs` (S9) |
| `ServerCharactersBinder.cs`, `OwnStateValidator.cs`, `LocalPlayer.cs` | 15 wyłącza `OwnStateSender` i rekonstruuje delty; wyłącznie 15 edytuje walidator. `LocalPlayer.cs` wyłącznie 14, pełne migawki przez S5 |
| `GameServerHooks.cs`, `ServerRuntime.cs` | Dispatch z K3-00; 15 i 16 tylko rejestrują handler; 18 odmowa join i status |
| Protocol/Serialization/router i fixture | Każdy własne wiersze X4–X6; późniejszy pakiet integruje i robi D4 |
| `ToolNode.cs`, zdalne narzędzia | `ToolNode` z S8 bez zmian; 15 wypełnia facade w Client, 16 wstrzykuje teren; składanie w swoich partial |
| CI/gdUnit4 | Jeden Windows runner `horyzont`, fixed pipe, testy silnika szeregowo; Linux wyłączony 2026-10-09 |

## Testy akceptacyjne (napisz najpierw)

| Nazwa | Scenariusz i oczekiwany wynik |
| --- | --- |
| `Replicate_TwoClientsRandomEditsTenMinutes_ConstructDigestsEqual` | seed 1515, 36 000 ticków/60 Hz, dwóch klientów wykonuje legalne set/clear co 6 ticków, w tym granice chunków i usunięcie ostatniego bloku; porównaj co 60 ticków posortowane id/ramy/pozy/wersje/4096 uintów po opróżnieniu reliable; końcowo identyczne skróty zakresu wspólnego; `Slow`, czas symulowany |
| `Replicate_Rtt100Jitter10_BlockVisibleWithin200Milliseconds` | FakeTimeProvider, 50 ms w każdą stronę, jitter ±10 ms, 5% utraty kanału zawodnego (niezawodny bez retransmisji — semantyka `LinkConditions.LossRate`); pomiar według X11 w README: opóźnienie dostarczenia + rzeczywisty czas odbiór→`ChunkMeshApplied`/podmiana kolizji (`Time.GetTicksUsec`), suma ≤200 ms; raport obu składników, max i p99; `Slow`, termin 60 s, wyjątek od ZASADY pkt 8 dla czasu silnika |
| `Interest_EnterLeaveAndOtherPlanet_OnlyExpectedEvents` | 499/501/551 m, inna planeta, własny byt, duża budowla z granicą sfery w zasięgu; dokładnie jeden spawn/despawn |
| `Snapshot_AcknowledgedBaselineAndDroppedPackets_ReconstructsFullState` | zmienione/niezmienione pola, pominięty byt, utrata ACK, wypadnięcie bazy i zawijanie uint; własny stan poprawnie trafia do walidatora |
| `Snapshot_TooManyEntities_Respects1150BytesAndEventuallySendsAll` | ponad 22 byty i >8 ram; jeden pakiet/tick, własny pierwszy, starzenie priorytetu, brak fałszywego despawn |
| `Apply_MultipartConstructThenEdits_PublishesCompleteVersion` | >12 chunków, edycje w trakcie wysyłki, pusta budowla, usunięcie; bez częściowej kolizji |
| `Apply_MissingPartDuplicateChunkAndStagingLimit_ResyncsWithoutPartialState` | uszkodzone/celowo niekompletne części, 5 s deadline; brak wycieku puli |
| `Apply_VersionGapAndDuplicate_RequestsOneResync` | luka wersji, duplikat OpId, legalna luka globalnego OpId; pełny stan naprawia kopię |
| `Resync_InvisibleEntityAndFlood_DoesNotLeakOrExceedRate` | unknown id, byt innej planety, 100 żądań; max 4/s, izolacja nadawcy |
| `Codec_ResyncRequestRoundTripAndMalformed_EnforcesCode21` | format 1, zera, truncation, fuzz; kod i kierunek/stany sesji |
| `Prediction_ReplyBeforeAndAfterOperation_RemovesOverlayOnce` | accepted/rejected/timeout/disconnect, nowa budowla; stan autorytatywny i allocatory nietknięte |
| `RemoteTools_BuildAndClear_UsesMirrorAndServerValidation` | rzeczywisty ToolNode, `--connect`, blok istniejącej i nowej budowli; teren NoEditPrediction do integracji K3-16 |
| `RemoteConstructs_ResyncWhileMeshing_DiscardsOldMeshAndCollision` | resync podczas pracy workerów; sprawdź generację, missing block i kolizje, gdUnit4 |
| `Replicate_HostLocal_UsesOneConstructWorldAndTerrain` | host bez drugich kolizji i bez cache terenu |
| `BuildSnapshot_WarmedUp10000Calls_AllocatesZeroBytes` | GC.GetAllocatedBytesForCurrentThread dla wyboru/zapisu/SendEncoded; osobny analogiczny test zapytań RemoteToolWorldView |
| `Shutdown_ReplicationActive_DrainsAndReleasesOnce` | odłączenie w transferze i przy resyncu; D8 i brak późnego wysyłania/workerów |

## Kroki

- [ ] 0. Potwierdź zależności na main, rzeczywiste numery i pole reply, zapisz różnice w Odstępstwach; zastosuj szwy K3-00 i mapę X10. Potwierdź, że każda operacja 1–7 zmienia najwyżej jedną komórkę na budowlę (`Construct.SetBlock` podbija `Version` o 1 na komórkę, `Construct.cs:154`). Jeśli nie: stosuj `TrySet` kolejno z wersjami Version+1…, a końcową porównaj z `ConstructChange.Version`; rozbieżność = resync.
- [ ] 1. Napisz testy .NET i scenariusze silnika z tabeli; użyj InMemoryTransport i FakeTimeProvider.
- [ ] 2. Dodaj ResyncRequest/kodek/router, D4 i przegląd Opus przed integracją oraz fixture X6 (`ProtocolContractTests`, `MaxMessages.All`, `CodecGen.Message`) i test routera w `tests/Horyzont.Net.Tests` (X5).
- [ ] 3. Zarejestruj handler w dispatchu S1, dodaj zainteresowanie, lifecycle bytów i transfer pełnych budowli.
- [ ] 4. Dodaj baseline/ACK, bezalokacyjny builder/SendEncoded i rekonstrukcję; wyłącz OwnStateSender.
- [ ] 5. Dodaj lustro, wersje/resync, RemoteConstructs i RemotePlayers; sprawdź stare wyniki workerów.
- [ ] 6. Dodaj RemoteToolWorldView, nakładkę predykcji i zdalny ToolNode; przygotuj wstrzyknięcie K3-16.
- [ ] 7. Test 10 min, pasmo/latencja, duża budowla i zero alokacji; ręcznie 5 min host + zdalny klient, budowla 5×5×3 i po integracji K3-16 kopanie TPP (odroczenie K2-13).
- [ ] 8. Wykonawca uruchamia build/test/format/smoke z ZASADY pkt 10 i testy silnika szeregowo na Windows, wyniki do PR. Rebase, D4, przegląd Opus, zielone CI przed scaleniem.

## Odbiór

Dwa klienty z identycznym stanem budowli po 10 min, działający resync i zdalne narzędzia, jedna ścieżka korekty, brak dublowania świata hosta. Testy tabeli zielone; raport pasma według tabeli X3 (osobno migawki, niezawodne Live/Bulk, sterowanie), cel 50 000 B/s/klienta; raport latencji i kosztu kolejki dużej budowli. Złoty korpus bez zmian. Pełne godzina/8 botów/2 h to K4-19. CI tylko Windows; nie przywracaj jobów Linux w tym pakiecie.

## Odstępstwa i decyzje wykonawcy

## Pułapki

- ACK dotyczy odtworzonego stanu, nie samego odbioru bajtów; brak bazy nie może potwierdzić delty.
- Router używa jawnej listy (K3-00); dopisz 21 w trzech predykatach X5, sam enum/kodek nie wystarczy.
- `ConstructState` nie jest transakcją dziennika; klient nie zapisuje jego kopii jako świata serwera.
- `IntentReply` i operacja mogą dotrzeć w różnej kolejności. Przyjęcie nie oznacza jeszcze widocznego autorytatywnego bloku.
- Założenie, że wszystkie operacje globalnego dziennika dochodzą do każdego klienta, jest błędne przy zainteresowaniu.

## Pytania do właściciela

Pytania produktowe nie zostawiają wykonawcy decyzji projektowych; do odpowiedzi obowiązują wskazane warianty.

| Nr | Pytanie | Wariant domyślny |
| --- | --- | --- |
| P1 | Czy nakładka przewidzianych bloków ma wpływać na lokalne kolizje? | nie; wyłącznie wygląd, autorytatywne kolizje po potwierdzeniu |
| P2 | Czy 10 min odbioru ma być czasem rzeczywistym czy symulowanym? | 36 000 deterministycznych ticków w CI; dodatkowy ręczny przebieg czasu rzeczywistego, godzina w K4-19 |
| X1 | Czy przyjmujemy K3-00 przed równoległym startem pakietów? | Domyślnie tak: S1–S9 na bramce K2 bez Core/protokołu; alternatywa po odrzuceniu: X10 „integruje pakiet scalany później”, K3-14 krok 6 i K3-16 krok 6 dopiero po scaleniu K3-15 |
| X3 | Czy przyjmujemy wspólny podział pasma? | Domyślnie migawki 20 480 B/s / 2 300 B, niezawodne wspólne 27 000 B/s / 65 536 B, sterowanie poza budżetem (~2,5 KB/s); alternatywa: strojenie podziału po raporcie actual bytes, limit ≤50 000 B/s, bez osobnego wiadra terenu |
