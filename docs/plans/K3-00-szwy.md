# K3-00 · Szwy przed równoległym startem Co-op

| | |
| --- | --- |
| Prowadzi | Opus 5.5 |
| Testy akceptacyjne | Opus 5.5, przed implementacją |
| Przegląd | GPT-6.1 Sol; Opus 5.5 przegląda odbiór szwów |
| Zależy od | bramka K2; przed startem K3-14…18 |
| Gałąź | `pakiet/k2-bramka` (mały PR „K3-00 szwy”) |

**Cel:** wystawić wspólne interfejsy i punkty kompozycji K3, aby pięć pakietów ruszyło równolegle bez konkurencyjnych kontraktów i bez zmian zachowania K2.

## Kontekst do przeczytania

- `docs/ZASADY.md`: pkt 2, 3, 6–10; [README](README.md), X1–X11.
- Plany K3-14…18: konsumenci szwów; niniejszy pakiet nie implementuje ich funkcji.
- Kod bazowy `ServerRuntime`, `GameServerHooks`, `MessageRouter`, `GameComposition`, `LocalPlayer`, `ToolNode`, `ProtocolContractTests`: nazwy sprawdzone wyszukiwaniem w `pakiet/k2-bramka`; nowe nazwy poniżej to projektowane API, nie symbole istniejące w K2.

## Zakres

S1–S9: interfejsy, neutralne implementacje domyślne, pojedynczy dispatch, jawna lista routera, uporządkowanie testu istniejącej wersji i podział kompozycji na partial. Bez zmian `src/Horyzont.Core/`, formatu protokołu i `ProtocolVersion` (pozostaje 3).

## Poza zakresem

Predykcja/interpolacja, delty i lustra, cache/replikacja terenu, Steam i UI; bez nowych komunikatów, operacji, intencji, odmów i blobów. Bez zmiany protected contracts i zachowania host/solo/`--connect`.

## Projekt

### Pliki, kontrakty wystawiane i konsumowane

| # | Szew | Plik(i) | Treść |
| --- | --- | --- | --- |
| S1 | Dispatch komunikatów klienta | `src/Horyzont.Server/IServerClientMessageHandler.cs` (nowy), `ServerRuntime.cs`, `GameServerHooks.cs` | patrz X2 |
| S2 | Router z jawną listą typów | `src/Horyzont.Net/Session/MessageRouter.cs` | `TryGetType` sprawdza `IsKnownType(byte)`: `switch` z jawnymi wartościami 1–20 (bez zakresu); test `TryGetType_UnassignedValues_ReturnsFalse` dla 0, 21–255 |
| S3 | Test wersji protokołu | `tests/Horyzont.Core.Tests/Protocol/ProtocolContractTests.cs` | `ProtocolVersion_IsThree` → `ProtocolVersion_MatchesDocumentedMergeValue` (wartość 3 bez zmian), komentarz XML: „K3: każdy pakiet zmieniający Protocol/Serialization ustawia main+1 i aktualizuje ten test oraz `Constants_GoldenValues`” |
| S4 | Budżety | `src/Horyzont.Server/Replication/ISnapshotBudget.cs`, `FixedSnapshotBudget.cs`, `IReplicationSendBudget.cs`, `ReplicationTraffic.cs`, `TokenBucketReplicationBudget.cs` | patrz X3; `FixedSnapshotBudget` zwraca 1150 i ignoruje `Debit` |
| S5 | Pełne migawki u klienta | `src/Horyzont.Client/Replication/IFullSnapshotSink.cs`, `PassThroughFullSnapshots.cs` (nowe); `game/src/Client/Character/LocalPlayer.cs` | `IFullSnapshotSink { void OnFullSnapshot(Snapshot full); }`. `LocalPlayer.OnMessage` przestaje przyjmować `Snapshot`; `LocalPlayer` implementuje `IFullSnapshotSink` (ustawia `_pending`). `PassThroughFullSnapshots : IClientSessionListener` przekazuje do zarejestrowanych sinków wyłącznie `Snapshot` z `BaselineSnapshotId == 0`; rejestrowany w kompozycji zamiast bezpośredniego odbioru. K3-15 podmienia go na `SnapshotDecoder` |
| S6 | Gotowość świata klienta | `src/Horyzont.Client/IClientWorldReadiness.cs`, `AlwaysReadyWorld.cs` (nowe); `LocalPlayer.cs` | `bool IsReady { get; }`. `LocalPlayer` przy `IsReady == false` zachowuje się jak przy zamkniętej bramce terenu (ścieżka `!open` w `ProcessSnapshot`: podąża za serwerem, nie nagrywa i nie wysyła wejść). Domyślnie `AlwaysReadyWorld` |
| S7 | Widzowie terenu | `src/Horyzont.Client/TerrainViewers.cs`, `ITerrainViewerSource.cs` (nowe); `LocalPlayer.cs` | `readonly record struct TerrainViewers(PlanetId Planet, Vector3 VisualPosition, uint VisualDistance, Vector3 CollisionPosition, uint CollisionDistance)`; `bool TryGetViewers(out TerrainViewers viewers)`; `LocalPlayer` implementuje z `_visualViewer`/`_collisionViewer` po konwersji `PlanetFrames` |
| S8 | Narzędzia zdalne | `src/Horyzont.Client/Tools/IIntentReplyObserver.cs`, `IRemoteTerrainRaycast.cs`, `NoRemoteTerrain.cs` (nowe); `src/Horyzont.Client/Replication/RemoteToolWorldView.cs` (nowy szkielet); `game/src/Client/Tools/ToolNode.cs` | `IIntentReplyObserver { void NoteReply(IntentReply reply); }` — `ToolNode.OnIntentReply` woła je **przed** `Pending.OnReply`, gdy predykcja je implementuje. `IRemoteTerrainRaycast { bool RaycastTerrain(PlanetId, Vector3, Vector3, float, out TerrainHit); }`. `RemoteToolWorldView(ServerHello hello, IRemoteTerrainRaycast terrain)` implementuje `IToolWorldView`: planety z hello, teren delegowany, metody budowli/graczy zwracają false/0 (wypełnia K3-15). Nowe przeciążenie `ToolNode.Initialize(ClientRuntime client, Func<ServerHello, IToolWorldView> views, Func<IEditPrediction> prediction, ILocalPlayerView player, GameRules rules, ILoggerFactory loggers)`; istniejące `Initialize(..., ServerRuntime server, Func<ITerrainService> terrain, ...)` deleguje z `GodotToolWorldView`/`NoEditPrediction.Instance` |
| S9 | Kompozycja w częściach | `game/src/Boot/GameComposition.Motion.cs`, `.Replication.cs`, `.TerrainReplication.cs`, `.Platform.cs` (nowe, `partial`) | każdy plik ma pustą metodę `partial`/prywatną wywoływaną w ustalonym miejscu `Create`: kolejno Replication (15) → TerrainReplication (16) → Motion (14) → Platform (17). UI (18) nie jest częścią kompozycji (X8). Pakiet edytuje wyłącznie swój plik |

### Decyzje wykonawcze

- S1 według X2: `public interface IServerClientMessageHandler { bool Handle(PlayerId player, Message message); }`. Rejestracja `ServerRuntime.AddClientMessageHandler` wyłącznie przed `StartAsync`/poza tickiem. `GameServerHooks.OnClientMessage` woła wszystkie handlery w kolejności rejestracji, bez przerywania; Debug tylko gdy żaden nie zwrócił true. True wyłącznie dla własnych typów (15: `SnapshotAck`/`ResyncRequest`; 16: `TerrainCacheReport`/`TerrainChecksums`/`TerrainView`).
- S2: `IsKnownType(byte)` to jawny switch 1–20, nie zakres; stanowe predykaty bez zmian. Typy 21–25 dopisują ich pakiety według X4–X5.
- S3 zmienia tylko nazwę istniejącego testu (i odwołania do niej) na `ProtocolVersion_MatchesDocumentedMergeValue`, nie wartość. Treść komentarza XML ustalona w tabeli S3: „K3: każdy pakiet zmieniający Protocol/Serialization ustawia main+1 i aktualizuje ten test oraz Constants_GoldenValues”.
- S4 według X3: `ISnapshotBudget` ma `int GetByteBudget(PlayerId player, Tick tick)` i `void Debit(PlayerId player, int encodedBytes)`; `FixedSnapshotBudget` zwraca 1150 i ignoruje Debit. `IReplicationSendBudget` ma `bool TryConsume(PlayerId player, ReplicationTraffic traffic, int bytes)`; `enum ReplicationTraffic : byte { Live = 0, Bulk = 1 }`. Jedna wspólna instancja na klienta dla 15/16: 27 000 B/s, 65 536 B, czas wyłącznie z wstrzykniętego `TimeProvider`. Live zawsze true i odejmuje, może zejść poniżej zera; Bulk tylko przy stanie ≥ `min(bytes, 65 536)`. Klasy ruchu, wydatki sterowania i docelowe 20 480 B/s migawek definiuje tabela X3 w README; `MotionSnapshotBudget` implementuje K3-14.
- S5–S7: neutralne domyślne przepuszczanie pełnych migawek i `AlwaysReadyWorld` zachowują K2; `LocalPlayer` udostępnia pełny sink oraz widzów przez `PlanetFrames`, nie publiczną metodę `ViewDistanceFor`. K3-15 zastępuje `PassThroughFullSnapshots` dekoderem bez edycji `LocalPlayer`; K3-16 czyta S6/S7 bez jego edycji.
- S8: `RemoteToolWorldView` w Client deleguje teren, a budowle/graczy zwraca false/0 do K3-15. `NoRemoteTerrain` zwraca false. `ToolNode.OnIntentReply` wywołuje `IIntentReplyObserver.NoteReply` przed `Pending.OnReply`. Istniejące przeciążenie Initialize deleguje z `GodotToolWorldView`/`NoEditPrediction.Instance`; host nie dostaje drugiego terenu ani predykcji.
- S9: `GameComposition` jest partial; w `Create` wywołania pustych partial/prywatnych metod w kolejności Replication (15) → TerrainReplication (16) → Motion (14) → Platform (17). UI (18) poza kompozycją. Każdy pakiet edytuje tylko swój partial, z wyjątkiem publikacji `WorldStartResult` przez 18. Własność i późniejszą integrację reguluje X10.

## Testy akceptacyjne (napisz najpierw)

| Test / sprawdzenie | Scenariusz → wynik |
| --- | --- |
| `TryGetType_UnassignedValues_ReturnsFalse` (nowy, Net.Tests) | 0 i 21–255 odrzucane; przypisane 1–20 pozostają rozpoznawane |
| Dispatch S1 | Wszystkie handlery wołane w kolejności, także po true; Debug tylko przy braku obsługi; rejestracja przed startem |
| Budżet S4 | FakeTimeProvider, 27 000 B/s, pojemność 65 536 B, Live z długiem i próg Bulk, jeden stan zużycia dla 15/16 |
| S5–S8 | Snapshot baseline 0 przekazany raz, delta nie trafia do sinka; AlwaysReady zachowuje K2, false zamyka nagrywanie/wysyłanie wejść; reply observer przed trackerem |
| K2 bez regresji | Host/solo/`--connect` jak w K2, istniejące testy ruchu/narzędzi/lifecycle zielone, bez drugiej korekty ani kopii terenu |
| Pełna walidacja | Build/test/format/smoke zielone, `ProtocolVersion_MatchesDocumentedMergeValue` i `Constants_GoldenValues` nadal 3 |

## Kroki

- [x] 0. Sprawdź bazę bramki K2 oraz X1–X11; domyślne decyzje właściciela obowiązują bez odpowiedzi.
- [x] 1. Napisz akceptację szwów i regresji przed kodem; nie zmieniaj testów pakietów 14…18 bez zgody. Akceptacja w commicie `88d686b`; RED: brak projektowanych API; istniejąca regresja K2 w .NET zielona.
- [x] 2. S1–S4: dispatch, lista routera, nazwa testu i wspólny budżet, bez Core i bumpu protokołu. Wszystkie handlery bez short-circuit; budżet współdzielony per PlayerId, FakeTimeProvider i test braku alokacji.
- [x] 3. S5–S8: pełny sink, readiness, viewers i neutralne narzędzia; zachować zachowanie K2. Pełny sink w kompozycji; false readiness zatrzymuje Record/Send; host bez drugiej kopii terenu. Testy gdUnit napisane, do wykonania w CI.
- [x] 4. S9: partial i puste miejsca wywołań; sprawdzić zakres diffu i własność X10. Cztery wywołania w Create: 15 → 16 → 14 → 17; partial mają dostęp do zależności instancji.
- [ ] 5. `dotnet build Horyzont.sln`, `dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"`, `dotnet format Horyzont.sln --verify-no-changes`, `python tools/engine/build.py smoke`; gdUnit4 szeregowo na runnerze Windows `horyzont`. Wyniki do PR; przegląd Sol i odbiór Opus, zielone CI przed startem 14…18.

  Walidacja lokalna: rozwiązanie i `game/Horyzont.Game.csproj` zbudowane (0 ostrzeżeń/błędów), format OK, 1770/1770 testów .NET. Szczegóły w [opisie PR](K3-00-opis-pr.md). Krok 5 pozostaje otwarty do smoke/gdUnit i zielonego CI oraz przeglądu Opus 5.5.

## Odbiór

S1–S9 gotowe, pełne build/test/format/smoke zielone, zero zmian zachowania host/solo/`--connect`, brak zmian Core/protokołu, wersja 3. Opus przegląda; pakiety mogą ruszyć równolegle.

## Odstępstwa i decyzje wykonawcy

- Decyzja użytkownika: GPT-6.1 Sol pisze testy i implementuje; Opus 5.5 przegląda. Praca na `pakiet/k3-00-szwy`, baza `91de59d` = bramka K2. Bez pushowania.
- Na polecenie użytkownika nie uruchamiamy Godota, gdUnit ani smoke silnika na jedynym runnerze CI. Testy silnika zostaną napisane i skompilowane; wykonanie i odbiór pozostają dla CI i Opus.

- Cache SDK w worktree uzupełniono kopiami czterech istniejących paczek forka 4.7.2 z `C:/actions-runner/horyzont/_work/horyzont/horyzont/engine/nuget-local`; bez uruchamiania silnika i bez zmian nuget.config/engine.json.
- Puste instancyjne metody partial S9 mają lokalne wyłączenie CA1822: przyszły właściciel komponuje zależności sesji przez `this`; metody pozostają neutralne.
- S7: nieobecny viewer ma odległość 0; TryGetViewers zwraca false przed utworzeniem widzów i po zwolnieniu świata. Pozycje wyłącznie przez PlanetFrames.
- Istniejący test SnapshotProgressTests przeniesiono z OnMessage do OnFullSnapshot, zgodnie z pojedynczą ścieżką S5; asercje regresji bez zmian.

## Pułapki

- Nie implementować funkcji pakietów K3 w neutralnych adapterach.
- Handler true nie przerywa dispatchu; nie tworzyć drugiego kontraktu observerów.
- Nowe typy routera dodają właściciele, nie rozszerzenie zakresu liczbowego.
- Późniejszy pakiet integruje wspólne punkty; kolejność scalania nie blokuje startu (X10).

## Pytania do właściciela

Wariant domyślny obowiązuje, jeśli właściciel nie odpowie przed startem.

| ID | Pytanie | Domyślny wariant / alternatywa |
| --- | --- | --- |
| P1 | Czy przyjmujemy mały PR K3-00 na bramce K2? | Tak, S1–S9 bez Core/protokołu; alternatywa po odrzuceniu: X10 oraz K3-14 krok 6 i K3-16 krok 6 dopiero po scaleniu K3-15 |
| P2 | Czy przyjmujemy wspólny podział pasma X3? | Tak: migawki 20 480 B/s / 2 300 B, niezawodne 27 000 B/s / 65 536 B, sterowanie poza budżetem (~2,5 KB/s); alternatywa: strojenie podziału po raporcie actual bytes, wspólny limit ≤50 000 B/s, bez osobnego wiadra terenu |
