# Plany wykonawcze: K1 Fundament, K2 Planeta i K3 Co-op

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

Kolejność K3: planowana K3-15 → 4, K3-16 → 5, K3-14 bez zmiany, K3-17 bez zmiany, K3-18 → 6; wykonawca po każdym scaleniu dopisuje rzeczywisty wiersz wersji (D4). K3-00 pozostawia 3.

## Szablon planu pakietu

Każdy plan ma te same części: nagłówek (kto, zależności, gałąź), cel w jednym zdaniu, kontekst do przeczytania, zakres i poza zakresem, projekt (pliki, kontrakty, decyzje), testy akceptacyjne, kroki z polami wyboru, odbiór i pułapki. Wykonawca odhacza kroki w tym pliku w ramach swojego PR.

## CI na własnym runnerze (Windows)

- Po co: workflow'y CI chodzą na własnym komputerze z Windows (z .NET, Godotem i resztą narzędzi), więc nie zużywają minut hostowanych runnerów ani nie wymagają odtwarzania środowiska w chmurze. Runner ma etykietę `horyzont`.
- Instalacja (właściciel, PowerShell 7, zalogowany `gh` z prawami admina repo): `pwsh tools/ci/setup-windows-runner.ps1 -Sha256 <skrót zip z notatek wydania actions/runner> -StartAtLogon`. Skrypt nie wypisuje tokenu rejestracyjnego i jest idempotentny (przy istniejącym `.runner` tylko odświeża `.path`).
- `.path` stawia Git Bash przed `C:\Windows\System32\bash.exe`, żeby `shell: bash` nie trafiał do WSL. Usługa Windows nie jest instalowana (działałaby bez narzędzi użytkownika); runner startuje przy logowaniu albo ręcznie przez `run.cmd`.
- Wyłączenie Linuksa: w workflow'ach zmień `runs-on` na `[self-hosted, horyzont]` i usuń zadania na `ubuntu-latest`.
- Przywrócenie Linuksa później: wróć do `runs-on: ubuntu-latest` (albo dodaj macierz z oboma systemami); runner może zostać zarejestrowany równolegle.
