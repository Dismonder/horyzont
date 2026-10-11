# Plany wykonawcze: K1 Fundament, K2 Planeta, K3 Co-op i K5 Kosmos

K1 to siedem pakietów, po których rozwiązanie ma zbudowany silnik, kontrakty Core, zapis odporny na awarie i działającą warstwę sieciową; dopiero na tym fundamencie powstaje planeta (K2).

## Pakiety

| Pakiet | Plan | Prowadzi | Testy akceptacyjne pisze | Przegląd | Zależy od |
| --- | --- | --- | --- | --- | --- |
| K1-01 | [Pipeline silnika](K1-01-pipeline-silnika.md) | Sonnet 5.5 | Sonnet 5.5 | Opus 5.5 na bramce | — |
| K1-02 | [Szkielet rozwiązania](K1-02-szkielet-rozwiazania.md) | Gemini 3.8 Flash | Sonnet 5.5 | Sonnet 5.5 | etap A: —; etap B: K1-01 |
| K1-03 | [Core: stan, operacje, protokół](K1-03-core-stan-i-operacje.md) | Opus 5.5 | Opus 5.5 | GPT-6.1 Sol | K1-02A |
| K1-04 | [Serializatory i fuzzing](K1-04-serializatory.md) | Gemini 3.8 Flash | Sonnet 5.5 | Sonnet 5.5 + Opus 5.5 (format) | K1-03 |
| K1-05 | [Persistence](K1-05-persistence.md) | Opus 5.5 | Opus 5.5 | GPT-6.1 Sol | K1-03, K1-04 |
| K1-06 | [Transporty](K1-06-transporty.md) | Sonnet 5.5 | Sonnet 5.5 | Opus 5.5 na bramce | K1-02A |
| K1-07 | [Sesja i protokół](K1-07-sesja-i-protokol.md) | Opus 5.5 | Opus 5.5 | GPT-6.1 Sol | K1-04, K1-06 |

K1-02 dzieli się na etap A (rozwiązanie .NET bez silnika) i etap B (projekt Godota po K1-01), a K1-06 przenosi same bajty, więc nie czeka na Core; plan projektu ma te same zależności. Dzięki temu Core i transporty ruszają, zanim skończy się wielogodzinne budowanie silnika.

Identyfikator pakietu to kamień milowy i numer z [planu projektu](../plan/plan-projektu.md): K1-01 … K1-07, dalej K2-08 … K2-13, K3-14 … K3-18 i K4-19 w Planecie, K5-20 … K5-28 w Kosmosie, K6-29 … K6-40 w Przetrwaniu, K7-41 … K7-49 w Walce, K8-50 … K8-57 w Treści na EA i K9-58 … K9-65 w Wydaniu. Etap pakietu dostaje literę (K1-02A, K1-02B), a gałąź nazywa się od identyfikatora (`pakiet/k2-08-generator`).

Plany K1, K2 i K3 leżą w tym katalogu; K3 uwzględniają przegląd Opus 5.5 z 2026-10-10. Plany kolejnych pakietów Opus 5.5 pisze ze specyfikacji podprojektu (`docs/spec/`), zanim pakiet ruszy, bo zależą od kodu, który jeszcze nie istnieje. Specyfikacje podają już zakres, kontrakty, kody, testy i odbiór każdego pakietu.

## Kolejność startu

| Fala | Pakiety (mogą iść równolegle) | Warunek startu |
| --- | --- | --- |
| 1 | K1-01 (Sonnet), K1-02A (Gemini) | brak |
| 2 | K1-03 (Opus), K1-06 (Sonnet), K1-02B (Gemini) | K1-02A scalony; K1-02B także po K1-01 |
| 3 | K1-04 (Gemini) | K1-03 scalony |
| 4 | K1-05 (Opus), K1-07 (Opus) | K1-04 scalony; K1-07 także po K1-06 |
| Bramka K1 | test end-to-end i przegląd całości przez Opus 5.5 | wszystkie pakiety scalone |

## Bramka K1

Bramkę prowadzi Opus 5.5 na gałęzi `pakiet/k1-bramka`. Test end-to-end powstaje dopiero tu, bo jako jedyny potrzebuje jednocześnie Persistence (K1-05) i sesji (K1-07), które idą równolegle.

- [x] CI zielone na Windows i Linux, testy akceptacyjne wszystkich siedmiu pakietów przechodzą.
  Testy UDP z K1-06 dostały na bramce zapas na przestoje planisty runnera (timeout połączenia 2 s, limit testu 30 s); pierwszy nightly z testami Soak wykrył na Windows przejściowe `SQLITE_IOERR_TRUNCATE` (1546) przy otwieraniu świata po zabiciu procesu (11/50 przebiegów); `WorldDatabase` ponawia teraz przejściowe błędy I/O przy otwieraniu (ok. 1,6 s), a nightly na gałęzi bramki jest zielony na Windows i Linux.
- [x] `python tools/engine/build.py smoke` wypisuje `SMOKE OK`, a projekt `game/` uruchamia się bez grafiki z `--smoke`.
- [x] Nowy projekt `tests/Horyzont.Integration.Tests` (referencje: Core, Persistence, Net, Testing; dodany do `Horyzont.sln`) z testem end-to-end w pamięci. Serwer z `ServerSimulation`, `PersistenceWriter` na pliku tymczasowym i dwóch klientów przez `InMemoryTransport` przyjmuje intencje i zapisuje dziennik, a po symulowanej awarii wczytanie z powtórką daje ten sam skrót stanu. Haki gry dla testu (`IServerGameHooks` nad `ServerSimulation`) żyją w tym projekcie.
- [x] Opus 5.5 przegląda całość w świeżej sesji i pisze plany K2 (pakiety K2-08 … K2-13).
  Przegląd: 6 recenzentów Sonnet 5.5, 2 opinie GPT-6.1 Sol, synteza i weryfikacja Opus 5.5; blokujące B1–B3 poprawione w PR #9, B4 to scalenie K1-02B (#4). Plany K2 sprawdził krzyżowo GPT-6.1 Sol, a rozstrzygnięcia D1–D17 są wpisane w plany. Pytania do właściciela mają w planach warianty domyślne; pakiet rusza z domyślnym wariantem, jeśli właściciel nie odpowie przed jego startem.

## Pakiety K2

| Pakiet | Plan | Prowadzi | Testy akceptacyjne pisze | Przegląd | Zależy od |
| --- | --- | --- | --- | --- | --- |
| K2-08 | [Planeta: generator i parametry](K2-08-generator-planety.md) | Sonnet 5.5 | Sonnet 5.5 | Opus 5.5 | etap A (K2-08A, `editor-linux`): K1-02B; etap B: K2-08A |
| K2-09 | [Adapter Voxel Tools](K2-09-adapter-voxel-tools.md) | Opus 5.5 | Opus 5.5 | GPT-6.1 Sol (`high`) + Opus 5.5 w świeżej sesji (Core) | bramka K1 (PR #9: B1–B3), K1-05, K1-02B, K2-08 |
| K2-10 | [Tryby gry](K2-10-tryby-gry.md) | Sonnet 5.5 | Sonnet 5.5 (tabelę zamykania i E3–E9 sprawdza Opus 5.5) | Opus 5.5 w świeżej sesji + GPT-6.1 Sol (`medium`: wątki, zamykanie, E6/E7) | K1-07, K2-09 |
| K2-11 | [Postać i kamera](K2-11-postac-i-kamera.md) | Sonnet 5.5 | Sonnet 5.5 (testy Core przegląda Opus 5.5) | Opus 5.5 | K2-08, K2-10 (pośrednio K2-09) |
| K2-12 | [Budowle](K2-12-budowle.md) | GPT-6.1 Sol | GPT-6.1 Sol | Opus 5.5 | K1-02B, K1-03, K2-08A |
| K2-13 | [Narzędzia](K2-13-narzedzia.md) | Sonnet 5.5 | Sonnet 5.5 (testy Core przegląda Opus 5.5) | Opus 5.5 | K2-09, K2-10, K2-11, K2-12 |

K2-08A to osobny mały PR z celem `editor-linux` w `engine/engine.json`; bez niego testy silnika nie ruszą na Linuksie, więc czekają na niego K2-08B i K2-12. K2-12 nie czeka na resztę K2: wpięcie budowli w `GameComposition` K2-10 robi K2-13 w kroku 0.

### Kolejność startu K2

| Fala | Pakiety (mogą iść równolegle) | Warunek startu |
| --- | --- | --- |
| 1 | K2-08A (Sonnet) | bramka K1 zakończona, K1-02B scalony |
| 2 | K2-08B (Sonnet), K2-12 (Sol) | K2-08A scalony i `engine.yml` na `main` zielony |
| 3 | K2-09 (Opus) | K2-08 scalony; bramka K1 (PR #9: B1–B3) scalona |
| 4 | K2-10 (Sonnet) | K2-09 scalony z E1–E2 |
| 5 | K2-11 (Sonnet) | K2-10 scalony (`ProtocolVersion` = 2 na `main`) |
| 6 | K2-13 (Sonnet) | K2-11 i K2-12 scalone |
| Bramka K2 | przegląd całości i plany K3 | wszystkie pakiety K2 scalone, zielone CI i przebieg nocny na `main` |

### Warunki wejścia E1–E9 z przeglądu bramki K1

| Punkty | Właściciel |
| --- | --- |
| E1–E2 (kopie zapasowe: walidacja `reason`, kopia poza wątkiem zapisu według pomiaru) | K2-09 |
| E3–E9 (writer przy zamykaniu, kontrakt `OnPlayerJoining`, Prepare+Apply, `ITransport.IsDrained`, termin zamykania UDP, połączenia podczas zamykania, kod rozłączenia klienta) | K2-10 |

### Reguły wspólne K2 (decyzje bramki K1)

- **Semantyka edycji terenu jest wersjonowana (D1).** Ogon dziennika zapisany inną wersją semantyki terenu (`TerrainEditSemantics.SemanticsVersion` + `voxel.ref`, K2-09) nie jest powtarzany nową implementacją: świat otwiera się w trybie bezpiecznym z jasnym błędem, a odzyskanie z samego checkpointu to jawna akcja właściciela (K2-10, `--recover-from-checkpoint`). Zmiana `OperationApplier.cs` albo `ITerrainService.ApplyEdit` bez podbicia formatu operacji jest dozwolona tylko wtedy, gdy pierwszy krok pakietu zatwierdza zamrożony korpus złoty dzienników (oczekiwane skróty `StateHasher` i listy wersji bloków nagrane z `main`), a jego test przechodzi po zmianie bez zmian; inaczej podbicie formatu operacji z powtórką starej wersji.
- **`ProtocolVersion` (D4).** Pakiet, który podbija wersję protokołu, ustawia ją na (wartość na `main` w chwili scalenia) + 1; test sprawdza udokumentowaną wartość; rebase aktualizuje i stałą, i test. Kolejność K2: K2-10 → 2, K2-11 → 3.
- **Jedna kolejność zamykania (D8).** Kanoniczna tabela jest w K2-10 („Kolejność zamykania”); K2-09, K2-11 i K2-13 tylko dokładają do niej swoje kroki. `TerrainBlockStore.Close()` tylko po `ServerTerrainBlocks.CloseAsync() == true`.
- **Wspólna walidacja edycji terenu (D2).** `TerrainRules.CheckEdit` w Core (właściciel K2-09) używają i `OperationApplier`, i powtórka dziennika przed ładowaniem terenu.

## Pakiety K3

| Pakiet | Plan | Prowadzi | Testy akceptacyjne pisze | Przegląd | Zależy od |
| --- | --- | --- | --- | --- | --- |
| K3-00 | [Szwy](K3-00-szwy.md) | Opus 5.5 | Opus 5.5 | GPT-6.1 Sol + Opus 5.5 (odbiór szwów) | bramka K2; mały PR na `pakiet/k2-bramka` |
| K3-14 | [Predykcja ruchu](K3-14-predykcja-ruchu.md) | Opus 5.5 | Opus 5.5 | GPT-6.1 Sol; Opus dla ewentualnego Core | K1-07, K2-11; szwy K3-00 |
| K3-15 | [Replikacja bytów](K3-15-replikacja-bytow.md) | GPT-6.1 Sol (`high`) | GPT-6.1 Sol | Opus 5.5 | K2-10, K2-12; szwy K3-00 |
| K3-16 | [Replikacja terenu](K3-16-replikacja-terenu.md) | Opus 5.5 | Opus 5.5 | GPT-6.1 Sol + Opus 5.5 (Core) | K2-09, K2-10; szwy K3-00 |
| K3-17 | [Steam lobby](K3-17-steam-lobby.md) | Gemini 3.8 Flash (High) | Sonnet 5.5 | Sonnet 5.5 + Opus 5.5 (tożsamość) | K1-06, K2-10; szwy K3-00 |
| K3-18 | [Interfejs](K3-18-interfejs.md) | Gemini 3.8 Flash (High) | Sonnet 5.5 | Sonnet 5.5 + Opus 5.5 (Core/sesje) | K2-10; szwy K3-00 |

### Kolejność startu K3

| Fala | Pakiety (mogą iść równolegle) | Warunek startu |
| --- | --- | --- |
| 0 | K3-00 (Opus) | bramka K2 gotowa; PR szwów na `pakiet/k2-bramka`, bez zmian Core/protokołu |
| 1 | K3-14 (Opus), K3-15 (Sol), K3-16 (Opus), K3-17 (Gemini), K3-18 (Gemini) | K3-00 scalony i własne zależności K2 spełnione; brak dodatkowych krawędzi startu między 14…18 |
| 1B | K3-16B (Opus), jeśli LOD wydzielone do osobnego PR na gałęzi K3-16 | szwy i baza implementacji K3-16; pozostałe pakiety nadal równoległe |
| Integracja K3 | rekomendowane scalanie 15 → 16 → 14 → 17 → 18 | kolejność scalania nie warunkuje startu; integruje pakiet scalany później (X10) |
| Bramka K3 | odbiór co-op, zdalnych narzędzi, Steam i UI | wszystkie pakiety scalone, zielone CI; K3-16B wymagane albo jawna decyzja właściciela P1 K3-16; Manual Steam jawnie udokumentowane |

### X1. Szwy przed startem pakietów

Domyślnie mały PR [K3-00](K3-00-szwy.md), S1–S10, bez zmian Core i protokołu (`ProtocolVersion = 3`), prowadzi Opus 5.5, przegląd GPT-6.1 Sol. Pakiet rusza z wariantem domyślnym, gdy właściciel nie odpowie. Alternatywa po odrzuceniu K3-00: reguła X10 „integruje pakiet scalany później”; K3-16 krok 6 (zdalne narzędzia) i K3-14 krok 6 startują dopiero po scaleniu K3-15. Wydłuża to ścieżkę krytyczną.

### X2. Jeden dispatch komunikatów klienta

Decyzja (S1): `public interface IServerClientMessageHandler { bool Handle(PlayerId player, Message message); }` w `src/Horyzont.Server/`; `ServerRuntime.AddClientMessageHandler(IServerClientMessageHandler handler)` (tylko przed `StartAsync`/poza tickiem, jak `AddTickParticipant`); `GameServerHooks.OnClientMessage` woła **wszystkie** handlery w kolejności rejestracji (bez przerywania), a gdy żaden nie zwrócił `true`, loguje Debug jak dziś. Handler zwraca `true` tylko dla typów, które posiada: K3-15 `SnapshotAck`, `ResyncRequest`; K3-16 `TerrainCacheReport`, `TerrainChecksums`, `TerrainView`. Usunąć `IServerClientMessageObserver` z K3-16.

### X3. Jeden budżet pasma

Decyzja (wartości obowiązujące we wszystkich planach):

| Ruch | Mechanizm | Wartość |
| --- | --- | --- |
| Migawki (zawodny) | `ISnapshotBudget` → `MotionSnapshotBudget` (K3-14) | 20 480 B/s, pojemność 2 300 B; ramka zwykła ≤ 1 024 B, wymuszona pełna ≤ 1 150 B |
| Niezawodne dane gry: `ConstructState`, `OperationApplied`, `ConstructRemoved`, `TerrainBlockData`, `TerrainSync` | jedna instancja `IReplicationSendBudget` na klienta (`TokenBucketReplicationBudget`, S4), współdzielona przez K3-15 i K3-16 | 27 000 B/s, pojemność 65 536 B |
| Sterowanie: `IntentReply`, `EntitySpawn/Despawn`, `Ping/Pong`, `ServerStatus`, `Disconnect` | poza budżetem | zapas ok. 2,5 KB/s |

Suma 20 480 + 27 000 = 47 480 B/s ≤ 50 000 B/s (cel spec w jednostkach dziesiętnych).

Kontrakt S4: `bool TryConsume(PlayerId player, ReplicationTraffic traffic, int bytes)`; `enum ReplicationTraffic : byte { Live = 0, Bulk = 1 }`. `Live` (operacje budowli, `ConstructRemoved`, dane terenu i `TerrainSync` purpose 2 wynikające z edycji na żywo) zawsze zwraca `true` i odejmuje (stan może zejść poniżej zera — tempo ogranicza serwerowy limiter intencji). `Bulk` (części `ConstructState`, manifesty purpose 0/1, naprawy checksum) zwraca `true` tylko przy stanie ≥ `min(bytes, 65 536)`. Czas wyłącznie z wstrzykniętego `TimeProvider`. `ISnapshotBudget`: `int GetByteBudget(PlayerId player, Tick tick)` + `void Debit(PlayerId player, int encodedBytes)`. Dodatkowo, aby duży transfer nie blokował kanału uporządkowanego (head-of-line), część `ConstructState` ma miękki cel ≤ 32 KiB zakodowanej ramki (min. 1 chunk, nadal maks. 12 chunków i 256 KiB).

### X4. Numeracja i D4

| Kod | Typ | Pakiet | Kierunek | Kanał |
| --- | --- | --- | --- | --- |
| MessageType 21 | `ResyncRequest` | K3-15 | K→S | ReliableOrdered |
| MessageType 22 | `ServerStatus` | K3-18 | S→K | ReliableOrdered |
| MessageType 23 | `JoinUnavailable` | K3-18 | S→K (przed `ServerHello`) | ReliableOrdered |
| MessageType 24 | `TerrainSync` | K3-16 | S→K | ReliableOrdered |
| MessageType 25 | `TerrainView` | K3-16 | K→S | ReliableOrdered |

Wszystkie w zakresie Planety 21–29 (K1-03:620). Żaden plan nie dodaje `OperationKind`, `RejectReason`, `IntentCode` ani `BlobKind` — zgodnie z ZASADY pkt 3. Nazwy rekordów równe nazwom enuma (wymóg `ProtocolContractTests.cs:157`) — spełnione.

D4: K3-15, K3-16, K3-18 podbijają `ProtocolVersion` do (main przy scaleniu)+1 — przy kolejności X10 będzie to 4, 5, 6; K3-14 i K3-17 nie podbijają (o ile nie dotkną Protocol/Serialization). Każdy z trzech aktualizuje **istniejący** test `ProtocolVersion_MatchesDocumentedMergeValue` (S3) i `Constants_GoldenValues` (`ProtocolContractTests.cs:99`) — nie dodaje własnego testu o innej nazwie (dwa testy z różnymi wartościami zablokowałyby rebase). Po każdym scaleniu dopisać wiersz do `docs/plans/README.md` „Kolejność K3: …”.

### X5. Reguły routera dla nowych typów (BLOKUJĄCE — inaczej `ClientSession.Send`/`ServerSession.Send` rzucą)

| Typ | `IsKnownType` | `IsAllowedFromClient` | `IsAllowedFromServer` | `IsGameMessageFromClient` | `IsGameMessageFromServer` |
| --- | --- | --- | --- | --- | --- |
| 21 `ResyncRequest` | tak | `InGame` | — | tak | — |
| 22 `ServerStatus` | tak | — | `Loading`/`InGame` (przez `IsGameMessageFromServer`) | — | tak |
| 23 `JoinUnavailable` | tak | — | wyłącznie `AwaitingServerHello` (jawnie w gałęzi tego stanu) | — | **nie** (wysyła prywatne `ServerSession.SendMessage(peer, …)`) |
| 24 `TerrainSync` | tak | — | `Loading`/`InGame` | — | tak |
| 25 `TerrainView` | tak | `Loading` i `InGame` | — | tak | — |

Każdy pakiet dopisuje swoje wiersze w `MessageRouter.cs` i `MessageChannels.For` oraz test routera w `tests/Horyzont.Net.Tests` (dozwolone/zabronione stany obu kierunków).

### X6. Wspólne fixture testów protokołu (każdy pakiet z nowym komunikatem)

Dopisać próbkę nowego typu w: `tests/Horyzont.Core.Tests/Protocol/ProtocolContractTests.cs` (tablica `messages` w `Messages_EachRecordReturnsItsType_AndEveryTypeHasARecord`; `MessageChannels_MapsEveryType` — lista zawodnych bez zmian), `tests/Horyzont.Core.Tests/Serialization/MaxMessages.cs` (`All()`: największa poprawna instancja mieszcząca się w kanale), `tests/Horyzont.Testing/Generators/CodecGen.cs` (generator `Message`). Bez tego istniejące testy `ChannelLimitTests`, `GeneratorSanityTests` i kontraktu padną po dodaniu wartości enuma. Konflikty w tych plikach rozwiązuje pakiet scalany później (dopisuje obok, w kolejności numerów).

### X7. Test warstw projektów (BLOKUJĄCE)

`tests/Horyzont.Architecture.Tests/LayeringTests.cs:31` dopuszcza dla `Horyzont.Client` wyłącznie `Horyzont.Core,Horyzont.Net`. K3-15 dodaje Client→Constructs (dopuszczalne: Constructs zależy tylko od Core) — K3-15 musi zmienić tę linię na `"Horyzont.Core,Horyzont.Net,Horyzont.Constructs"`. Client→Server — **odrzucone** (18.1): biblioteka klienta nie może zależeć od runtime serwera.

### X8. Procesowy cykl życia menu i Steam

Decyzja:
1. **Tryb CLI** (`--solo/--host/--connect/--server`, także `+connect_lobby`): bez zmian — kompozycja tworzy swój `QuitCoordinator`. Nowe pole `GameEnvironment.ProcessCleanup` (`Func<bool>?`, domyślnie null); cleanup koordynatora = `() => composition.ReleaseStep() && (env.ProcessCleanup?.Invoke() ?? true)`.
2. **Tryb menu** (K3-18, brak argumentu trybu): `Boot` dodaje `GameUiCoordinator` (dziecko `Boot`). Koordynator UI tworzy **jeden** procesowy `QuitCoordinator` (`Initialize` z delegatami): `requestShutdown = () => { _quitRequested = true; _current?.RequestShutdown(); }`, `isFinished = () => _quitRequested && (_current is null || _current.IsFinished)`, `exitCode = () => 0`, `cleanup = () => (_current?.ReleaseStep() ?? true) && (_platform?.TryShutdown() ?? true)`, `registerSignals: true`. Kompozycje sesji dostają `GameEnvironment { ManageQuit = false, RegisterSignals = false }`.
3. Koniec sesji bez wyjścia (host zakończył, Cancel, ReturnToMenu, zgoda na zaproszenie): koordynator UI woła `RequestShutdown()`, w `_Process` pompuje `ReleaseStep()` do `true` albo do terminu `GameComposition` release timeout (ten sam co dziś); sukces → `_current = null`, `Root.QueueFree()`, menu; przekroczenie → `ErrorDialog` „Nie udało się zamknąć poprzedniej sesji. Uruchom grę ponownie.”, wszystkie przyciski startu zablokowane. Usunąć `QuitCoordinator.ExitOnSessionEnd` z K3-18.
4. **Steam** (K3-17): `SteamPlatformServices` (`game/src/Boot/SteamPlatformServices.cs`) jest obiektem **procesowym**: tworzy go `Boot` (CLI z `--steam`/`+connect_lobby`) albo `GameUiCoordinator` (menu, przy pierwszym użyciu Steam) przez statyczne `GameComposition.CreatePlatformServices(GameEnvironment env, uint appId)`. Węzeł `SteamLifetime` jest dzieckiem `Boot`, nie `composition.Root`. Kompozycja dostaje usługi przez nowe `GameEnvironment.Platform` (`SteamPlatformServices?`, init). `SteamPlatformServices.TryShutdown()` (Leave lobby → Dispose callbacków → `SteamAPI.Shutdown()`, zwraca true) jest podpinane jako `ProcessCleanup` (CLI) albo w cleanup koordynatora UI (menu); nigdy przed zwolnieniem transportu. Przy przekroczeniu terminu zwolnienia sesji `SteamAPI.Shutdown` nie jest wołane (proces kończy się bez niego).

### X9. Inni gracze wyłącznie wizualni

Fakt: `PlayerBody.cs:81` — `CollisionMask = TerrainMask | ConstructsMask`; serwer nie koliduje graczy ze sobą. Proxy kolizyjne innych graczy w maskach własnego ciała dałyby rozjazd z serwerem i stałe korekty. Decyzja: inni gracze u klienta to wyłącznie węzły wizualne (`RemotePlayerPresentation`, K3-14) zarządzane przez `RemotePlayers` (K3-15, lifecycle). Podpowiedzi narzędzi (K2-13: `PlayerCapsule`, `BlockOverlap`) liczą się z danych `EntityMirror` przez `IToolWorldView.CopyOtherPlayerFeet`, nie z ciał fizycznych.

### X10. Mapa kolizji i reguła scalania

Reguła ogólna: kolejność scalania nie jest warunkiem startu; **integrację wspólnego punktu wykonuje pakiet scalany później** (rebase, dopisanie swojego wiersza/wywołania, aktualizacja D4). Rekomendowana kolejność scalania: 15 → 16 → 14 → 17 → 18.

| Plik wspólny | Właściciel treści | Pozostali |
| --- | --- | --- |
| `GameComposition.cs` | K3-00 (partial S9 i neutralne punkty platformy S10); K3-18 wyłącznie publikacja `WorldStartResult` | 14/15/16/17 tylko w swoim `GameComposition.<Część>.cs` |
| `LocalPlayer.cs` | K3-14 (korekta, replay, kamera) | po S5–S7 nikt inny go nie edytuje; K3-18 edytuje `LocalInputReader.cs` |
| `OwnStateValidator.cs` | K3-15 | K3-14 nie edytuje |
| `ToolNode.cs` | K3-00 (S8) | K3-15/16 tylko przez `GameComposition.<Część>.cs`; K3-18 tylko odczyt `Pending/LastReply/IntentAnswered` |
| `GameServerHooks.cs`, `ServerRuntime.cs` | K3-00 (S1); K3-18 odmowa join i status | 15/16 tylko rejestrują handler |
| `MessageType/Channels/Codec/Router`, `ProtocolConstants` | każdy swój wiersz (X4–X6) | pakiet scalany później dopisuje i robi D4 |
| `ServerSession.cs` | K3-15 `SendEncoded`; K3-18 `HandleHello` + `GetTransportStats` | rozłączne metody |
| `HandshakeValidator.cs` | K3-17 (17.3) | — |
| `LaunchOptions.cs`, `Boot.cs`, `GameEnvironment.cs` | K3-00 neutralne `CompositionPlatform`/`ProcessCleanup`; K3-17 argumenty Steam i typed `Platform`; K3-18 tryb menu w `Boot` | rebase 17 → 18 |
| `LayeringTests.cs` | K3-15 | — |
| CI/gdUnit4 | jeden runner `horyzont`, testy silnika szeregowo | bez zmian w planach |

S10 usuwa blokadę K3-17: K3-00 posiada punkty w GameComposition.cs i neutralne kontrakty `Client/Platform`. K3-17 wybiera binder we wczesnym `static ConfigurePlatform(LaunchOptions, GameEnvironment, ref ICompositionPlatform)` i wiąże węzły w dotychczasowym instancyjnym `ConfigurePlatform()`, oba w swoim partial. Binder konsumuje ConfigureServerTransports/CreateClientTransport przed runtime, TryConnectClient dla joinu bez --connect, OnWorldStarted po udanym starcie hosta i OnShutdownRequested/OnTransportsReleased. Procesowy ProcessCleanup uruchamia się dopiero po potwierdzeniu pomyślnego Dispose; failure/timeout nie uruchamia SteamAPI.Shutdown. Wspólny GameComposition.cs pozostaje poza zakresem K3-17.

### X11. Pomiar 200 ms w testach z silnikiem

Testy `Replicate_*Within200ms*` (K3-15, K3-16) mieszają czas sieci (wirtualny) i czas meshingu/kolizji Voxel Tools (rzeczywisty, wątki robocze). Reguła: czas = opóźnienie dostarczenia z `LinkConditions` (deterministyczne, `FakeTimeProvider`: 50 ms w jedną stronę + jitter) + zmierzony rzeczywisty czas od odbioru komunikatu do `ChunkMeshApplied`/podmiany kolizji (`Time.GetTicksUsec` silnika), suma ≤ 200 ms; raport obu składników osobno. Test `Slow`, termin 60 s, opisany wyjątek od ZASADY pkt 8 (czas silnika). `LinkConditions.LossRate` dotyczy **wyłącznie** kanału zawodnego (`LinkConditions.cs:6`, `InMemoryTransport.cs:361`); kanał niezawodny w testach nie ma retransmisji.

Kolejność K3: planowana K3-15 → 4, K3-16 → 5, K3-14 bez zmiany, K3-17 bez zmiany, K3-18 → 6; wykonawca po każdym scaleniu dopisuje rzeczywisty wiersz wersji (D4). K3-00 pozostawia 3. Stan faktyczny: main po K3-17 ma 5 (K3-16), K3-18 podbija do 6 (typy 22 `ServerStatus`, 23 `JoinUnavailable`).

## Pakiety K5

Źródło reguł: [przegląd K5](K5-przeglad.md) (Opus 5.5, 2026-10-10; blokery B1–B5 wprowadzone do planów, B6 = ta sekcja). Plany K5-25 i K5-28 napisane 2026-10-10 według reguł Y1–Y15 i listy „Pokrycie K5-25 i K5-28” z przeglądu; przegląd Opus 2026-10-11 (fala 3a): oba **APPROVE WITH CHANGES**, poprawki w sekcjach „Przegląd Opus 2026-10-11” planów (K5-25 R1–R14: sloty wejść pilota, `HorizontalSpeedCap` w `ShipCorrection` 34 — 73 B, kolejność guardu startu, szybowanie przez `CharacterFrames`; K5-28 S1–S9: zależność od K4-19 i K5-24 T1).

| Pakiet | Plan | Prowadzi | Przegląd | Zależy od (start) | Warunek scalenia |
| --- | --- | --- | --- | --- | --- |
| K5-20 | [Ramy odniesienia](K5-20-ramy-odniesienia.md) | Opus 5.5 | GPT-6.1 Sol; Core — Opus | `main` (faza A), K3-15 (B), K3-14 (C–D) | K3-14, K3-15 scalone; **K4-19 nie jest warunkiem** (Y13) |
| K5-21 | [Układ gwiezdny](K5-21-uklad-gwiezdny.md) | Opus 5.5 | GPT-6.1 Sol; Core — Opus | K5-20 (albo na stosie po fazie A K5-20 — Y13) | K5-20 scalony |
| K5-22 | [Wiele terenów](K5-22-wiele-terenow.md) | Opus 5.5 | GPT-6.1 Sol | K5-21; kroki 5–6 K3-16, krok 10 K4-19 | K5-21 scalony; kontrakt Y11 zatwierdzony |
| K5-23 | [Bloki statku](K5-23-bloki-statku.md) | Sonnet 5.5 | Opus 5.5 (kontrakty Core) | `main` | brak krawędzi |
| K5-24 | [Model lotu](K5-24-model-lotu.md) | Sonnet 5.5 | Opus 5.5 (kontrakty Core) | K5-21, K5-23 | oba scalone; kontrakt Y11 zatwierdzony |
| K5-25 | [Fotele i sterowanie](K5-25-fotele-i-sterowanie.md) | GPT-6.1 Sol | Opus 5.5 | K5-24 | plan przejrzany (Opus 2026-10-11, R1–R14 w planie); K5-24 scalony |
| K5-26 | [Replikacja statków](K5-26-replikacja-statkow.md) | Opus 5.5 | GPT-6.1 Sol; Core — Opus | K5-24 | K5-24 scalony |
| K5-27 | [Mapa i nawigacja](K5-27-mapa-i-nawigacja.md) | Gemini 3.8 Flash | Sonnet 5.5; Core — Opus | K5-21; krok 12 K3-18 | K5-21 scalony |
| K5-28 | [Testy Kosmosu](K5-28-testy-kosmosu.md) | Sonnet 5.5 | Opus 5.5 | K5-22, K5-25, K5-26, K5-27 (kroki 0–2 i faza C); **K4-19** (kroki 3–6, 9); domknięte kroki 9–10 K5-22 (krok 9) | wszystkie scalone; okna przelotów po decyzji K5-24 T1 (do tego czasu K1 = `PENDING`) |

### Kolejność startu K5

| Fala | Pakiety (równolegle) | Warunek startu |
| --- | --- | --- |
| 0 | PR planów (ten przegląd, sekcja K5, poprawki B1–B5) | — |
| 1 | K5-23 (Sonnet), K5-20 fazy A–B (Opus) | po fali 0 (K3-15 jest na `main`) |
| 1C | K5-20 fazy C–D | po scaleniu K3-14 |
| 2 | K5-21 (Opus) | K5-20 scalony albo na stosie po fazie A (Y13) |
| 3 | K5-22 (Opus), K5-24 (Sonnet; także K5-23), K5-27 (Gemini Flash) | K5-21 scalony |
| 3a | plany K5-25 (Sol) i K5-28 (Sonnet) z przeglądem Opus | przed scaleniem K5-24 |
| 4 | K5-26 (Opus), K5-25 (Sol) | K5-24 scalony |
| 5 | K5-28 (Sonnet) | K5-22, K5-25, K5-26, K5-27 scalone; K4-19 (PR #29) scalony dla botów, `CoopRig` i nocy (przegląd K5-28 S1) |

Rekomendowana kolejność scalania: **K5-23 → K5-20 → K5-21 → K5-22 → K5-24 → K5-27 → K5-26 → K5-25 → K5-28**. Kolejność scalania nie warunkuje startu; integruje pakiet scalany później (X10).

### Y1. Ta sekcja przed startem
Sekcję i reguły Y1–Y15 dodał PR planów K5. Pakiety jej nie tworzą ani nie przepisują — po scaleniu dopisują tylko wiersz w „Kolejność K5” (numer PR, faktyczna wartość `ProtocolVersion`).

### Y2. Numeracja (zamrożona; zakresy K1-03:616-622)

| Rodzaj | Kod → typ (pakiet) | Wolne |
| --- | --- | --- |
| `OperationKind` 20–29 | 20 `ShipModeSet` (K5-23), 21 `ShipFuelAdd` (K5-24), 22 `MarkerSet`, 23 `MarkerRemove` (K5-27) | 24–29 |
| `IntentCode` 10–19 | 10 `ShipMode` (K5-23), 11 `SeatEnter`, 12 `SeatExit`, 16 `SeatEvict` (K5-25), 13 `Refuel` (K5-24), 14 `MarkerSet`, 15 `MarkerRemove` (K5-27) | 17–19 |
| `RejectReason` 30–39 | 30 `NoCockpit`, 31 `ShipAnchored`, 37 `NoEngine` (K5-23); 32 `SeatOccupied`, 33 `SeatOutOfReach`, 34 `EjectTooFast`, 35 `NotPilot`, 38 `ConstructNotEmpty` (K5-25); 36 `MarkerLimit` (K5-27); 39 `ShipNotParked` (K5-24) | **brak** — zakres wyczerpany; kolejna odmowa Kosmosu = decyzja właściciela |
| `MessageType` 30–39 | 30 `ShipInputBatch`, 34 `ShipCorrection` (K5-25); 31 `ShipStatus` (K5-26); 32 `StarSystemState` (K5-21); 33 `MarkersState` (K5-27); 35 `MapPresence` (rezerwacja warunkowa, K5-27 P1) | 36–39 |
| `BlobKind` 10–19 | 10 `StarSystemParams` (K5-21), 11 `Marker` (K5-27), 12 `ShipState` (K5-24) | 13–19 |
| Migracja | `M0002_Space` (K5-21; trzy tabele; numer = następny wolny przy scaleniu) | K5 nie dodaje innych |
| Treść (nie format) | `BlockFunctionKind` 0–9 (K5-23), 10 `RefuelStation` (K5-24) | — |
| Wartości w komunikatach | `ShipLaunchBlock` 0–1 (K5-24), 2 `PersonInHull` (K5-25, podbicie D4); `ShipFlightFlags` bity 0–7 (K5-24, w tym `TerrainHold`, `TerrainUnavailable`) | — |
| Semantyka bez kodu | `EntitySpawn.KindData` gracza = `[1][x i32][y i32][z i32]` (fotel, K5-26; K5-25 używa tej definicji) | — |

Nazwy rekordów = nazwy enuma (`ProtocolContractTests`). Bajt wersji rekordów istniejących rodzajów bez zmian; nowe zaczynają od 1.

### Y3. D4 dla K5
Podbijają `ProtocolVersion` do (main przy scaleniu) + 1: K5-20, K5-21, K5-22, K5-23, K5-24, K5-25, K5-26, K5-27 — każdy aktualizuje **istniejące** `ProtocolVersion_MatchesDocumentedMergeValue` i `Constants_GoldenValues`. Plan przy kolejności z X10 i K3 (4 = K3-15, 5 = K3-16, 6 = K3-18): K5-23 → 7, K5-20 → 8, K5-21 → 9, K5-22 → 10, K5-24 → 11, K5-27 → 12, K5-26 → 13, K5-25 → 14. Faktyczną wartość wpisuje wykonawca po scaleniu.

### Y4. Router i kanały

| Typ | Kanał | Od klienta | Od serwera | Właściciel |
| --- | --- | --- | --- | --- |
| 30 `ShipInputBatch` | zawodny | `InGame` (`IsGameMessageFromClient`) | — | K5-25 |
| 31 `ShipStatus` | zawodny | — | `Loading`, `InGame` | K5-26 |
| 32 `StarSystemState` | `ReliableOrdered` | — | `Loading`, `InGame` | K5-21 |
| 33 `MarkersState` | `ReliableOrdered` | — | `Loading`, `InGame` | K5-27 |
| 34 `ShipCorrection` | zawodny | — | `InGame` | K5-25 |

Typy 30, 31, 34 dopisać do listy zawodnych w `MessageChannels` i w `MessageChannels_MapsEveryType`. Każdy nowy typ: fixture X6 (`ProtocolContractTests`, `MaxMessages.All()`, `CodecGen`) i test routera w `tests/Horyzont.Net.Tests` (obie strony, stany dozwolone i zabronione).

### Y5. Budżet pasma K5 (rozszerzenie X3, S→K na klienta)

| Ruch | Mechanizm | Wartość |
| --- | --- | --- |
| Migawki (statki i pasażerowie jako zwykłe byty) | `ISnapshotBudget` | 20 480 B/s bez zmian |
| `ShipCorrection` (K5-25, przy migawce pilota) | wliczona do `ISnapshotBudget` tej samej rundy | ~75 B ładunku z typem i wersją (73 B treści; ≤ 83 B; przegląd K5-25 R2/R12/R13) × 20 Hz ≈ 1,5 KB/s |
| `ConstructState` statków (priorytety K5-26), `OperationApplied` (op 20, 22, 23, przepięcia na żywo — Y8), `MarkersState` (`Bulk`, jednorazowo ≤ ~37 KB) | wspólny `IReplicationSendBudget` | 27 000 B/s bez zmian |
| `StarSystemState` (jednorazowo ≤ ~560 B), `ShipStatus` (2 Hz, ~45 B), `IntentReply`, `EntitySpawn/Despawn` | poza budżetem (sterowanie) | razem w zapasie ~2,5 KB/s |

K→S: `ShipInputBatch` jak `InputBatch` (K5-25 podaje rozmiar i częstotliwość; cel ≤ 4 KB/s). Retransfer statku przy przepięciu albo zmianie trybu jest zakazany (Y8).

### Y6. Warstwy projektów (BLOKUJĄCE)
Bez nowych referencji projektów; `LayeringTests.cs:26-31` bez zmian w K5. `Horyzont.Server` nie zależy od `Horyzont.Constructs` (`BlockContentTable` składa `game/` i przekazuje do `ServerSimulation`); `Horyzont.Client` nie zależy od `Horyzont.Server`. Logika testowalna bez silnika: Core (`Frames/`, `Space/`, `Ships/`), Server (`Frames/`, `Space/`, `Ships/`, `Markers/`), Client (`Frames/`, `Space/`, `Ships/`, `Markers/`, `Navigation/`).

### Y7. Fazy ticka i kolejność uczestników
- `Frames`: `ShipFlightSystem` (K5-24) → `ServerZoneTransitions` (K5-21).
- `Characters` (domyślna): istniejący uczestnicy w dzisiejszej kolejności.
- `Compose`: `ServerFrameHistory` (K5-20) → `ServerPlanetTerrains` + `ServerShipViewers` (K5-22) → `ShipStatusSender` (K5-26).
Każdy pakiet rozszerza test K5-20 `ServerRuntime_Participants_RunInPhaseOrder_StableWithinPhase` o swojego uczestnika; rejestracja w swoim partialu.

### Y8. Zmiany nagłówka budowli docierają na żywo (B1, B4)
Operacja zmieniająca nagłówek budowli bez zmiany bloków trafia do odbiorcy z łączem `Live` jako `OperationApplied`, nigdy jako retransfer: `EntityReparentOp` budowli — `ForwardReparent` → `SendLive` dla łącza `Live` (K5-20 decyzja 10); `ShipModeSetOp` — podbicie `Construct.Version` o 1 (K5-23 decyzja 4). Retransfer tylko dla łączy nie-`Live` i `ResyncRequest`.

### Y9. Intencje gracza w ramie budowli
„Planeta gracza” = region jego ramy (`FrameGraph.TryGetRegion`); oko serwera wyrażane w ramie celu (`FrameGraph.TryExpress`) przed sprawdzeniem zasięgu i linii wzroku. Dotyczy `ShipModeIntent` (K5-23), `RefuelIntent` (K5-24), intencji foteli (K5-25) i istniejących intencji bloków. Wspólny pomocnik i testy pilota w kokpicie — K5-25 (najpóźniej); K5-23/K5-24 używają go od razu, jeśli K5-20 jest na `main`.

### Y10. Ciało postaci przy każdym przepięciu (B2)
`ServerCharacters` obsługuje: planeta → budowla (przyczepione), budowla → planeta (ta sama albo inna), planeta → inna planeta (respawn na terenie celu w `Frozen` do gotowości). Właściciel: K5-20 decyzja 15; konsumenci: K5-22 (`place`; klient wymusza teren planety własnego bytu), K5-25 (wysiadanie, wyskok).

### Y11. Gotowość terenu dla lotu (B3, definicja zachowawcza)
- `IPlanetTerrainReadiness` (K5-22): `Inactive` / `Streaming` / `Ready` / `Faulted`; `Faulted` = błąd strumienia albo nasycony sklep bloków. **Reguła `Ready` (przegląd Opus K5-22, 2026-10-11; zastępuje regułę sześcianu 40 m przy gruncie):** węzeł aktywny i kolizja LOD 0 zbudowana (nigdy same wczytane dane) — (a) w sześcianie gruntu (`TerrainGround`: grunt z generatora pod środkiem kolumny w zakresie promieniowym kolumny poszerzonym o `min(promień, 40 m)`, sześcian o tej połowie boku), gdy kolumna sięga gruntu, **oraz** (b) gdy `r − hull − d < R + MaxRelief + 64` m: w sześcianie przemiatania (`TerrainSweep`) o środku `statek + v̂ · min(2 s · |v|, 40 m)` i połowie boku `hull + 8` m — także ściany doliny na wysokości kadłuba i edycje graczy (wykop, budowle terenu). Kula zapytania o promieniu ≤ `TerrainSweep.FullBoxMaxRadius = 56` m jest sprawdzana jako cały sześcian, większa (kolumna) — przy gruncie (a); kula w całości nad `R + MaxRelief + 64` m → `Ready` przy aktywnym węźle. **K5-24 pyta przez `PlanetTerrainReadinessQueries.GetShipReadiness(planet, ShipTerrainQuery)`** (`ShipTerrainQuery.Of(shipCenter, hull, velocity, altitude, R, MaxRelief)` = kolumna `TerrainColumn.UnderShip` + `TerrainSweep` + `HasSweep`; wynik = gorszy z (a) i (b): `Faulted` > `Inactive` > `Streaming` > `Ready`); sygnatury `GetReadiness` i `TerrainColumn.UnderShip` bez zmian. **Limit prędkości poziomej w paśmie (b):** `TerrainSweep.MaxHorizontalSpeed(hull) = (96 − 40 − hull) / (10/60 s)` (widze odświeżane co 10 ticków; np. 276 m/s przy hull = 10 m, 0 przy hull ≥ 56 m) — egzekwuje model lotu K5-24.
- Zapytanie (K5-24) o **kolumnę pod statkiem**: `d = min(Altitude, 2 s · |v| + 64 m)`, statek daleko (`r − d/2 > R + MaxRelief`): środek na promieniu pod statkiem w wysokości `r − d/2`, promień = promień statku + `d/2`; inaczej (decyzja właściciela 2026-10-11: kolumna zawsze zawiera grunt pod statkiem, także w dolinie `r < R + MaxRelief`) kolumna obejmuje promień od `R − MaxRelief` do `r` — środek w połowie, promień = połowa rozpiętości + promień statku. Jedna implementacja: `TerrainColumn.UnderShip` (`src/Horyzont.Server/Space/`); widz statku (K5-22) stoi w tej samej kolumnie.
- Odwzorowanie: `Ready` → `Ready`; `Inactive`/`Streaming` wysoko → `NotLoaded` (nie niżej niż 2 R); `Streaming` nisko → `CollisionPending` (zawis); `Faulted` albo brak `Ready` przez `ReadinessTimeoutSeconds = 20` → `Unavailable`.
- Wykonanie (K5-22): Voxel Tools buduje LOD 0 (jedyny z kolizją) tylko do ok. 96 wokseli od widza, więc kolumny nie sprawdza się w całości. Widze statku (`ServerShipViewers`): widz gruntu w punkcie `TerrainGround.ViewerPoint` (ten sam test „grunt w kolumnie” co gotowość (a)); w paśmie (b) drugi widz w środku przemiatania, chyba że widz gruntu stoi ≤ 50 m od niego (wysokość nad gruntem ≤ 50 m — wystarcza jeden).
- Ścieżka błędu `Unavailable`: brak ruchu w dół na każdej wysokości (zawis albo bezruch), flaga `TerrainUnavailable` w `ShipFlightStatus`/`ShipStatus` (HUD), Warning `[LoggerMessage]` najwyżej co 10 s na statek, ponowienie co 5 s, automatyczny powrót po `Ready`; odstawianie po wczytaniu trzyma statek w miejscu z v = 0.
- Zapotrzebowanie na teren: `IShipDemandSource` (K5-22, domyślnie ruch > 0,5 m) podmieniane przez K5-24 na „`Mode == Ship` ∧ ¬`Parked`”; lot nie bierze dzierżaw.
- Adapter `PlanetTerrainShipGate` pisze pakiet scalany później z pary K5-22/K5-24; kontrakt zatwierdza Opus przed kodem obu. Bez mechanizmu „kolizja zbudowana” w Voxel Tools — stop i eskalacja (K5-22 P9), nigdy samo `IsAreaLoaded`.

### Y12. Mapa kolizji plików (integruje scalany później — X10)

| Plik / punkt | Pakiety (treść) | Reguła |
| --- | --- | --- |
| `ServerSimulation.cs` | 20 `UpdateConstructKinematics`; 21 `PreparePlayerJoin`; 23 konstruktor z `BlockContentTable`; 24 `UpdateShipState` | rozłączne metody |
| `OperationApplier.cs`, `OperationCodec`, `OperationKind` | 23 (20), 24 (21), 27 (22, 23) | przypadki w kolejności numerów; bez partiali |
| `IntentValidator.cs`, `IntentResolver.cs`, `IntentCodec`, `GameRules` | 23 (10, `TerrainMaxReliefMeters`), 24 (13), 27 (14, 15, kubełek), 25 (11, 12, 16), pomocnik Y9 | jw. |
| `RejectReason.cs` | 23, 24, 25, 27 | wartości wg Y2 |
| `WorldState.cs`, `WorldCheckpoint.cs`, `WorldStateBuilder.cs` | 21 (`StarSystem`, `HomePlanet`), 24 (`ShipStates`), 27 (`Markers`, `MarkerIds`) | sąsiednie pola; nowe parametry rekordów z wartościami domyślnymi na końcu |
| `StateHasher.cs` | 21 (sekcja układu po planetach, warunkowa), 27 (znaczniki na końcu, warunkowa) | kolejność: planety → układ → budowle → … → znaczniki; `ShipState` nigdy |
| `BlobKind`, `StateCodec` | 21 (10), 27 (11), 24 (12) | jw. |
| `PersistenceWriter`, `WorldLoader`, `WorldCreator`, `Sql` | 21, 24, 27 | rozłączne bloki w tej samej transakcji |
| `Message*`, `MessageRouter`, fixture X6 | 21 (32), 25 (30, 34), 26 (31), 27 (33) | Y4 |
| `EntityReplication.cs`, `SnapshotBuilder.cs`, `ConstructStateSender.cs` | 20 (`Ship` w migawkach, region, Y8), 26 (zasięgi, kolejność, bramka, priorytety), 25 (`ShipCorrection`) | rozłączne metody; rekomendacja 26 → 25; K5-27 bez edycji |
| `EntityMirror.cs` | 20 (`WrongFrameApplied`), 26 (`TryGetSeat`) | jw. |
| `ServerCharacters.cs` | 20 (Y10), 25 | 20 właścicielem; K5-22 tylko przez `ServerShipViewers` |
| `LocalPlayer.cs` (właściciel K3-14) | 20 (rebase, `Frozen`), 22 (widze → `ClientPlanetTerrains`), 25 (pilot) | edycje ograniczone; scalany później rebase |
| `VoxelTerrainService.cs`, `ITerrainService.cs`, `FakeTerrainService` | 23 (`TrySampleSolid`), 22 (`RemovePlanet`, gotowość kolizji) | rozłączne |
| `VoxelServerTerrainFactory.cs`, `OwnTerrainProvider.cs` | 21 (strażnik), 22 (usuwa strażnika) | 22 po 21 |
| `PlanetSurfaceSampler` / `PlanetMapSampler` | 22 (jedyny próbnik), 27 (adapter mapy) | scalany później usuwa duplikat |
| `ShipStats`/`ShipStatsCalculator` | 23, 24 (`ControlFrame`), 25 (fotele) | dopisywanie pól |
| `ConsoleCommandProcessor.cs` | 20 `frame`, 23 `ship`, 24 `ship pilot/fuel`, 22 `place` | osobne podkomendy, stały format linii |
| `blocks.json`, `game/locale/*.csv`, `project.godot` | 23 (G, bloki), 24 (stacja, HUD), 27 (M, mapa), 25 | dopisywanie; klawisze w kroku 0 |
| `GameComposition.*.cs` | 20 `.Frames`, 21 `.Space` (+1 wywołanie w `GameComposition.cs`), 22 `.Planets`, 23 `.Ships`, 24 `.Flight`, 25 `.Seats`, 26 `.ShipReplication`, 27 `.Navigation` | osobne partiale; podmiany jednej linii wg planów |
| `tests/Horyzont.Testing/Ships/` | 20 `TestShips`, 23 `ShipFixtures`, 24 `SphereGroundCollider`, `ScriptedControlSource`, atrapa gotowości | różne pliki |
| `docs/ZASADY.md`, `docs/plans/README.md` | wszyscy | dopisywanie wierszy (Y1) |

### Y13. Ścieżka krytyczna (wariant domyślny; właściciel może zmienić)
- K5-20 czeka na K3-14 i K3-15, **nie** na K4-19 (K4-19 jest pakietem odbiorczym, a testy K5-20 nie używają jego kodu). Alternatywa: scalenie K5-20 po K4-19 — wydłuża ścieżkę całego K5.
- K5-21 może ruszyć na gałęzi od `pakiet/k5-20-ramy` po zakończeniu fazy A K5-20; PR do `main` dopiero po scaleniu K5-20 i rebase. Alternatywa: start po scaleniu K5-20.
- Pakiety, których pojedyncze kroki wymagają K3-16, K3-18 albo K4-19 (K5-22 kroki 5, 6, 10; K5-27 krok 12), wstrzymują tylko te kroki.

### Y14. Korpus złoty i determinizm
Każdy pakiet K5: `git diff origin/main` pusty na `tests/Horyzont.Core.Tests/Golden/TerrainJournals/`, `TerrainJournalGoldenCorpusTests.cs`, `Hashing/DeterminismTests.cs`; nowe sekcje skrótu tylko warunkowe; skrót `WorldFactory.CreateDefault()` niezmieniony (K5-21 i K5-27 zapisują stałą przed zmianą hashera; scalany później ją zachowuje).

### Y15. Testy silnika na wspólnym runnerze
Testy `game/tests/Frames|Space|Ships|Terrain|Planet|Navigation` szeregowo na jednym runnerze `horyzont`; pomiary z silnikiem wg X11 (czas sieci wirtualny + zmierzony czas silnika, oba w raporcie); `Descent8km`: 3 próby w jobie `game`, 20 w nocnym `bench`.

Kolejność K5: (wykonawcy dopisują tu po scaleniu: pakiet, PR, `ProtocolVersion`).

- K5-20 — PR #26, `ProtocolVersion` = 8 (main 7 po K5-23 + 1, D4; scalenie `origin/main` z K3-17, K3-18 i K5-23).
- K5-21 — PR #30, `ProtocolVersion` = 9 (main 8 po K5-20 + 1, D4; scalenie K5-20 i `origin/main`).
- K5-22 — PR #31, `ProtocolVersion` = 10 (main 9 po K5-21 + 1, D4; scalenie K5-21 i `origin/main`). Kroki 9 (część benchmarku) i 10 wstrzymane do scalenia K4-19.
- K5-24 — PR: (numer przy otwarciu), `ProtocolVersion` = 11 (main 10 po K5-22 + 1, D4; scalenie gałęzi K5-22 i `origin/main`; op 21 `ShipFuelAdd`, intencja 13 `Refuel`, odmowa 39 `ShipNotParked`, blob 12 `ShipState`).
- K5-26 — PR: (numer przy otwarciu), `ProtocolVersion` = 13 (main 12 po K5-27 + 1, D4; scalenie `origin/main` z K5-27; komunikat 31 `ShipStatus`, fotel w `EntitySpawn.KindData` gracza).
- K5-27 — PR: (numer przy otwarciu), `ProtocolVersion` = 12 (main 11 po K5-24 + 1, D4; scalenie `origin/main` z K5-22 i K5-24; wiadomość 33, op 22/23, intencje 14/15, odmowa 36, blob 11).
- K5-25 — PR: (numer przy otwarciu), `ProtocolVersion` = 14 (main 13 po K5-26 + 1, D4; scalenie `origin/main` z K5-26 i K5-27; intencje 11 `SeatEnter`, 12 `SeatExit`, 16 `SeatEvict`, odmowy 32–35 i 38, komunikaty 30 `ShipInputBatch` i 34 `ShipCorrection`, `MovementMode` 6 `Gliding`, `ShipLaunchBlock` 2 `PersonInHull`). Scalone z K5-26 i K5-27 (fotel w `KindData` i `ShipStatus` 31 z main); `ShipPoseOverride` zob. odstępstwa w PR.

## Szablon planu pakietu

Każdy plan ma te same części: nagłówek (kto, zależności, gałąź), cel w jednym zdaniu, kontekst do przeczytania, zakres i poza zakresem, projekt (pliki, kontrakty, decyzje), testy akceptacyjne, kroki z polami wyboru, odbiór i pułapki. Wykonawca odhacza kroki w tym pliku w ramach swojego PR.

## CI na własnym runnerze (Windows)

- Po co: workflow'y CI chodzą na własnym komputerze z Windows (z .NET, Godotem i resztą narzędzi), więc nie zużywają minut hostowanych runnerów ani nie wymagają odtwarzania środowiska w chmurze. Runner ma etykietę `horyzont`.
- Instalacja (właściciel, PowerShell 7, zalogowany `gh` z prawami admina repo): `pwsh tools/ci/setup-windows-runner.ps1 -Sha256 <skrót zip z notatek wydania actions/runner> -StartAtLogon`. Skrypt nie wypisuje tokenu rejestracyjnego i jest idempotentny (przy istniejącym `.runner` tylko odświeża `.path`).
- `.path` stawia Git Bash przed `C:\Windows\System32\bash.exe`, żeby `shell: bash` nie trafiał do WSL. Usługa Windows nie jest instalowana (działałaby bez narzędzi użytkownika); runner startuje przy logowaniu albo ręcznie przez `run.cmd`.
- Wyłączenie Linuksa: w workflow'ach zmień `runs-on` na `[self-hosted, horyzont]` i usuń zadania na `ubuntu-latest`.
- Przywrócenie Linuksa później: wróć do `runs-on: ubuntu-latest` (albo dodaj macierz z oboma systemami); runner może zostać zarejestrowany równolegle.
