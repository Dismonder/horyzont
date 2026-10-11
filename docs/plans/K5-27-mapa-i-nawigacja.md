# K5-27 · Mapa układu i planety, znaczniki, nawigacja
Przegląd Opus: zatwierdzony (2026-10-10; szkic Gemini przepisany: znacznik zawsze w ramie planety, DDL `markers` z K5-21 (id + blob 11), znaczniki w checkpoincie, warunkowy skrót, upsert z niezmiennym właścicielem, własny nadawca `OperationApplied` znaczników, mapa w EA z bytów w zasięgu)

| | |
| --- | --- |
| Prowadzi | Gemini 3.8 Flash (spec 02:252) — wykonuje zamrożone tu kontrakty; kroki 1–6 (Core i zapis) każdorazowo przegląda Opus 5.5 |
| Testy akceptacyjne | Sonnet 5.5; kontrakty Core, układ bajtów (op 22/23, intencje 14/15, odmowa 36, blob 11, komunikat 33) i złote testy zatwierdza Opus 5.5 **przed** kodem appliera (krok 1) |
| Przegląd | Sonnet 5.5 (ZASADY pkt 9: pakiet Gemini → Sonnet); zmiany w `src/Horyzont.Core/` — Opus 5.5 (ZASADY pkt 3) |
| Zależy od | K5-21 scalony (spec 02:252: migracja M0002 z tabelą `markers`, `StarSystemState`, `ClientStarSystem`, `WorldState.HomePlanet`); przez K5-21 także K5-20 (`ClientFrameGraph`, `FrameLimits`) i K3-15 (`OperationApplied`, `EntityMirror`). Kroki 12 (UI) dodatkowo K3-18 (`IGameplayInputGate`, `ui.csv`) |
| Gałąź | `pakiet/k5-27-mapa-nawigacja` (worktree `F:/dev/horyzont-k5-27`) |
| Kamień milowy | K5 Kosmos; odbiór wg spec 02:252: „znaczniki wspólne i prywatne przetrwają restart; prowadzenie do znacznika działa w każdej ramie” |
| Równolegle | K5-22, K5-23, K5-24, K5-25, K5-26 — brak krawędzi startu; punkty styku w sekcji „Kody i punkty styku” |

**Cel:** znaczniki prywatne i wspólne jako operacje dziennika (22/23) z intencjami (14/15), odmową `MarkerLimit` (36), blobem 11 w tabeli `markers`, checkpointem i warunkowym skrótem stanu, synchronizacja `MarkersState` (33) z filtrem prywatności, oraz mapa układu i planety i wskaźnik celu w HUD liczony w `double` przez `ClientFrameGraph`, tak aby znaczniki przetrwały restart i awarię, a prowadzenie nie skakało przy żadnym przepięciu ramy.

## Kontekst

- `docs/ZASADY.md`: pkt 2 (każda zmiana świata to operacja; ramy tylko w `double`; konwersja Godot ↔ rama planety tylko przez `PlanetFrames`), 3 (Core = przegląd Opus; `Operations/`, `Intents/`, `OperationApplier.cs` = format; `Checkpoints/`, `World/` typy zapisywane; `Protocol/`, `Serialization/` → `ProtocolVersion`; zamrożony korpus złoty i `DeterminismTests` bez zmian w plikach; kody tylko z zakresu Kosmosu), 4 (applier bez treści, terenu i generatora; skrót bitowo), 6 (HUD bez alokacji), 7, 8.
- `docs/spec/02-kosmos.md`: :23 i :235 (znaczniki przetrwają restart i awarię; skrót po powtórce równy), :62 i :85 (stanowisko nawigatora: mapa, znaczniki, skaner), :92–95 (mapa układu, mapa planety bez mgły wojny, znacznik prowadzi strzałką i odległością, HUD lotu ma kierunek do celu), :144 (znaczniki w dzienniku), :151–152 (op 22 i 23), :154 (intencje 14, 15), :156 (odmowa 36), :176 (`MarkersState` 33; `OperationApplied` prywatnego tylko do właściciela), :181–186 (tabela `markers`, blob 11, `next_marker_id` w `meta`), :194 (skrót obejmuje znaczniki z licznikiem), :252 (odbiór).
- `docs/plans/README.md`: X3 (budżet `Live`), X4/D4, X5, X6, X10.
- `docs/plans/K5-21-uklad-gwiezdny.md`: decyzja 12 (DDL: `markers (marker_id INTEGER PRIMARY KEY, data BLOB NOT NULL)`), 14 (`ServerSpaceSync : IServerPlayerObserver` wysyła stan przy `OnPlayerReady`), 15 (`ClientStarSystem`, `StarSystemModel`), 7 (`StarSystemMath.SunDirection`), P5 (klucze nazw ciał), kolejność sekcji skrótu (znaczniki na końcu).
- `docs/plans/K5-20-ramy-odniesienia.md`: decyzje 3 (`FrameLimits`), 4 (`FrameGraph`), 12 (`ClientFrameGraph.TryGetWorldPose(FrameRef, double renderTick, out PoseD)`), 14 (własne przepięcie przez `Rebase`).
- `docs/plans/K3-15-replikacja-bytow.md:92-93` (`OperationApplied` niesie tylko skutki widoczne dla odbiorcy, luki `OpId` legalne, klient ma dedykowany applier lustra), `K3-18-interfejs.md:81` (`IGameplayInputGate.IsBlocked`, implementuje `MenuController`).

Kod, na którym pakiet buduje (zweryfikowane w `F:/dev/horyzont-k4-19`; nazwy K5-20/K5-21 z ich planów):

| Co | Gdzie | Rola |
| --- | --- | --- |
| `EntityIdAllocator` (`NextValue`, `PeekNext`, `IsUnused`, `MarkUsed`) | `src/Horyzont.Core/Ids/EntityIdAllocator.cs` | wzorzec `MarkerIdAllocator` |
| `OperationKind` 1–7, `OperationCodec` | `src/Horyzont.Core/Operations/OperationKind.cs`, `Serialization/OperationCodec.cs` | + 22, 23 |
| `IntentCodec` (prywatny `enum IntentCode`, 1–3) | `src/Horyzont.Core/Serialization/IntentCodec.cs:15` | + 14, 15 |
| `RejectReason` 0–21 | `src/Horyzont.Core/Intents/RejectReason.cs` | + `MarkerLimit = 36` |
| `OperationApplier` (`internal sealed`, nie partial), `IntentValidator` (kubełki limitu w `HasValidParameters`, `:43-57`), `IntentResolver` (`static`) | `src/Horyzont.Core/Simulation/` | obsługa nowych rodzajów |
| `ServerSimulation.Applied` (event), `Submit` | `src/Horyzont.Core/Simulation/ServerSimulation.cs:61,70` | źródło dla nadawcy znaczników |
| `WorldState` (`ChangeTracker`, `CaptureCheckpoint`, `RequeueAfterFailedCheckpoint`), `WorldCheckpoint`, `WorldStateBuilder` | `src/Horyzont.Core/World/WorldState.cs:213-323`, `Checkpoints/` | znaczniki w checkpoincie |
| `StateHasher` | `src/Horyzont.Core/Hashing/StateHasher.cs` | sekcja znaczników (warunkowa) |
| `BlobKind` 1–5 (+10 z K5-21), `StateCodec` | `src/Horyzont.Core/Serialization/` | + 11 |
| `MessageType` (K3: 21–25, K5-21: 32), `MessageChannels` | `src/Horyzont.Core/Protocol/` | + 33 |
| `MessageCodec` | `src/Horyzont.Core/Serialization/MessageCodec.cs` | typ 33 |
| `MessageRouter` | `src/Horyzont.Net/Session/MessageRouter.cs` | X5 |
| `ProtocolConstants` (`MaxReliableMessageBytes = 256 KiB`, `MaxDisplayNameBytes = 32`) | `src/Horyzont.Core/Protocol/ProtocolConstants.cs:13,19` | rozmiar `MarkersState` |
| `Sql` (stałe `meta`), `WorldLoader.ReadWorld`, `WorldCreator`, `PersistenceWriter` (zapis checkpointu, `:557`) | `src/Horyzont.Persistence/` | tabela `markers`, `next_marker_id` |
| `IServerPlayerObserver`, `GameServerHooks` | `src/Horyzont.Server/` | `OnPlayerReady` |
| K3-15: `EntityReplication` (rozsyłanie `OperationApplied` wg widoczności), `IReplicationSendBudget` | `src/Horyzont.Server/Replication/` | wyłączenie rodzajów 22/23, budżet `Live` |
| K5-21: `ServerSpaceSync`, `ClientStarSystem`, `StarSystemModel`, `StarSystemMath.SunDirection`, `WorldState.HomePlanet` | `src/Horyzont.Server/Space/`, `src/Horyzont.Client/Space/`, `src/Horyzont.Core/Space/` | stan przy wejściu, ciała na mapie |
| K5-20: `ClientFrameGraph`, `FrameLimits`, `GodotFrames`, `FrameNodes` | `src/Horyzont.Client/Frames/`, `src/Horyzont.Core/Frames/`, `game/src/Shared/Frames/` | nawigacja w każdej ramie |
| K3-15: `EntityMirror`, `ConstructMirror` | `src/Horyzont.Client/Replication/` | statki i gracze na mapie |
| K3-18: `IGameplayInputGate` | `src/Horyzont.Client/UI/IGameplayInputGate.cs` | blokada ruchu przy mapie |
| `PlanetGeneratorV1.Create(PlanetParams)` (graf `VoxelGeneratorGraph`), `MaxReliefMeters = 300` | `game/src/Planet/Generators/` | mapa planety |

## Zakres

1. **Core:** `MarkerId`, `MarkerIdAllocator`; `MarkerIcon`, `MarkerColor`, `MarkerRecord`, `MarkerRules` (limity i walidacja nazwy); `MarkerStore` w `WorldState`; `MarkerSetOp` (22), `MarkerRemoveOp` (23) w `OperationApplier.cs`; `MarkerSetIntent` (14), `MarkerRemoveIntent` (15), `RejectReason.MarkerLimit` (36); kodeki; blob 11; `MarkersState` (33); warunkowy `StateHasher`; znaczniki w `WorldCheckpoint` i `WorldStateBuilder`.
2. **Persistence:** zapis i odczyt `markers` (blob 11) oraz `meta.next_marker_id` w checkpoincie i przy wczytaniu.
3. **Serwer:** `MarkersState` przy `OnPlayerReady` (obok `StarSystemState`); `MarkerReplication` — `OperationApplied` znaczników: wspólnych do wszystkich gotowych graczy, prywatnych tylko do właściciela; wyłączenie 22/23 z rozsyłania K3-15.
4. **Klient (biblioteka):** `ClientMarkerStore` (lustro: `MarkersState` + applier lustra 22/23), `NavigationTarget`, `NavigationCoordinator` (wektor i odległość w `double` przez `ClientFrameGraph`, bez alokacji), `DistanceFormatter` (bez alokacji).
5. **Gra:** `NavigationHud` + `EdgeArrow`; `SystemMapPanel`; `PlanetMapSampler` + `PlanetMapPanel`; `MarkerEditDialog`; akcja `toggle_map` (M); bramka wejścia mapy; `ui.csv`; partial `GameComposition.Navigation.cs`.

## Poza zakresem

- Skaner złóż (podprojekt 3; stanowisko nawigatora dostaje tylko wywołanie otwarcia mapy — K5-25). HUD lotu (wysokość, prędkość, limit, paliwo — K5-24/K5-25); K5-27 daje mu `NavigationCoordinator.TryGetGuidance`.
- Znaczniki w ramie układu albo budowli (decyzja 1). Grupy/frakcje, więcej niż 256 + 256, autopilot po trasie (spec :35), mgła wojny (spec :93 jej nie ma).
- Pozycje graczy i statków spoza zasięgu zainteresowania na mapie (decyzja 9, pytanie P1).
- Zmiana DDL `markers` (ustalił K5-21) i nowe migracje.

## Fazy i zależność od niescalonych pakietów

| Faza | Kroki | Wymaga scalonych | Co daje |
| --- | --- | --- | --- |
| A | 0–4 | K5-21 | kontrakty i złote bajty, model, operacje, intencje, limity |
| B | 5–7 | K5-21 | skrót, checkpoint, zapis i awaria, komunikat 33, D4 |
| C | 8–9 | K5-21 (K3-15 przez K5-20) | serwer z filtrem prywatności; lustro klienta, nawigacja w każdej ramie |
| D | 10–12 | K5-21; krok 12 także K3-18 | HUD, mapy, dialog, wejście |
| E | 13 | — | odbiór, dokumentacja, PR |

## Mapa kryteriów → dowód

| # | Kryterium (źródło) | Dowód | Kategoria / job | Faza |
| --- | --- | --- | --- | --- |
| 1 | Znaczniki wspólne i prywatne przetrwają restart (02:252) i awarię; skrót po powtórce równy (02:235) | `Markers_CheckpointRestart_SameMarkersAndHash` + `Markers_CrashBeforeCheckpoint_JournalReplayRestores` | domyślna | B |
| 2 | Prowadzenie do znacznika działa w każdej ramie (02:252) | `NavigationCoordinator_TargetOnMoon_FromPlanetShipAndSystemFrames_Continuous` | domyślna | C |
| 3 | Limit 256 prywatnych na gracza i 256 wspólnych, odmowa 36 (02:151, :156) | `MarkerSetIntent_257thPrivate_MarkerLimit` / `…_257thShared_MarkerLimit` / `MarkerSetOp_OverLimit_PreconditionFails` | domyślna | A |
| 4 | Nowy numer ≥ `next_marker_id`: resolver podgląda, applier przesuwa (02:151) | `MarkerSetOp_NewId_BumpsAllocator` / `…_IdBelowNextAndUnknown_PreconditionFails` | domyślna | A |
| 5 | Prywatny usuwa (i zmienia) tylko właściciel (02:152) | `MarkerRemoveIntent_PrivateOfOther_Rejected` / `MarkerSetIntent_EditPrivateOfOther_Rejected` | domyślna | A |
| 6 | `MarkersState` po `ServerHello`: wspólne + własne prywatne; `OperationApplied` prywatnego tylko do właściciela (02:176) | `MarkersState_OnReady_SharedAndOwnPrivateOnly` / `MarkerReplication_PrivateToOwnerOnly_SharedToAll` (2 klientów, `InMemoryTransport`) | domyślna | C |
| 7 | Skrót obejmuje znaczniki z licznikiem (02:194) | `StateHasher_MarkerChange_ChangesHash` / `…_NextMarkerIdChange_ChangesHash` / `…_NoMarkersEver_HashUnchanged` | domyślna | B |
| 8 | Format: op 22/23, intencje 14/15, odmowa 36, blob 11, komunikat 33 | `MarkerCodecs_GoldenBytes` (5 złotych przypadków) + fixture X6 | domyślna | A/B |
| 9 | Mapa układu: ciała, kierunek słońca, statki i gracze z nazwami, znaczniki (02:92) | `SystemMapModel_ContainsBodiesSunEntitiesMarkers` (biblioteka) + `SystemMapPanel_RendersModel` (silnik) | domyślna + silnik | D |
| 10 | Mapa planety: ukształtowanie z generatora, gracze i znaczniki, bez mgły wojny (02:93) | `PlanetMapSampler_HeightsMatchGeneratorWithinVoxel` | silnik | D |
| 11 | Wybrany znacznik prowadzi strzałką i odległością w HUD (02:94); kierunek do celu dla HUD lotu (02:95) | `NavigationHud_TargetInView_Pin_Behind_EdgeArrow` + `NavigationCoordinator_TryGetGuidance_*` | silnik + domyślna | C/D |
| 12 | HUD bez alokacji (ZASADY pkt 6) | `NavigationCoordinator_Update_AllocatesZeroBytes` / `DistanceFormatter_AllocatesZeroBytes` | domyślna | C |

## Projekt

### Decyzje

1. **Znacznik zawsze w ramie planety.** Spec :151 daje polu „planeta”, a `markers` ma właściciela i planetę. `MarkerRecord(MarkerId Id, PlayerId? Owner, PlanetId Planet, Vector3 Position, string Name, MarkerIcon Icon, MarkerColor Color)` (`sealed record`, `src/Horyzont.Core/World/Markers/`): `Owner == null` → wspólny; `Planet` wymagana; `Position` w ramie planety (`float`, |p| ≤ `FrameLimits` ramy planety = 100 km). Znacznik leży na ciele albo nad nim, w ramie tego ciała; punkt dalej niż 100 km od każdego ciała (układ sięga 400 km — K5-24 decyzja 6.6) nie może być znacznikiem i jest odrzucany jako `InvalidParameters` (przegląd K5, N20). Szkic miał `PlanetId?` i osobne kolumny x/y/z — odrzucone (DDL ustalił K5-21).
2. **Identyfikatory i wartości.** `readonly record struct MarkerId(ulong Value)` (`Ids/`, 0 = `None`). `MarkerIdAllocator(ulong nextValue)` jak `EntityIdAllocator`: `NextValue`, `PeekNext()`, `internal IsUnused`, `internal MarkUsed` (NextValue = max(NextValue, id+1)). `enum MarkerIcon : byte { Default, Base, Outpost, Resource, Danger, Ship, Landing, Poi }` i `enum MarkerColor : byte { White, Red, Green, Blue, Yellow, Cyan, Magenta, Orange }` (0–7; wartość > 7 odrzucana na granicy). `MarkerRules` (`src/Horyzont.Core/Operations/` — stałe semantyki op, ścieżka formatu): `MaxPrivatePerPlayer = 256`, `MaxShared = 256`, `MaxNameBytes = 32`, `IsValidName(string)` (1..32 B UTF-8, bez znaków sterujących, bez samych spacji).
3. **`MarkerStore`** (`World/Markers/`, w `WorldState.Markers`): `SortedDictionary<ulong, MarkerRecord>` (deterministyczna iteracja), liczniki prywatnych per gracz i wspólnych, `TryGet`, `CountPrivate(PlayerId)`, `CountShared`, `All` (po id); mutacje `internal` (tylko applier i builder), każda oznacza `ChangeTracker` (zbiór `MarkerId`).
4. **Operacje** (`Operations/`, applier w `OperationApplier.cs` — jeden plik formatu, bez partiali):
   - `MarkerSetOp(MarkerId Marker, PlayerId? Owner, PlanetId Planet, Vector3 Position, string Name, MarkerIcon Icon, MarkerColor Color)` (22). Warunki (tylko stan): planeta w rejestrze; pozycja skończona i ≤ 100 km; `IsValidName`; ikona/kolor ≤ 7; gdy znacznik istnieje → `Owner` równy obecnemu (właściciel niezmienny); gdy nie istnieje → `MarkerIds.IsUnused(Marker)` i po wstawieniu limit 256 (prywatny dla `Owner`, wspólny) nie przekroczony. Skutek: wstaw albo zastąp; `MarkUsed`.
   - `MarkerRemoveOp(MarkerId Marker)` (23). Warunek: znacznik istnieje. Skutek: usuń (licznik nie wraca).
   - Uprawnienie „prywatny zmienia i usuwa tylko właściciel” sprawdza walidator (operacja nie niesie aktora; ZASADY pkt 4 — applier stosuje skutek). Zmiana appliera z nowymi rodzajami: korpus złoty i `DeterminismTests` bez zmian w plikach (ZASADY pkt 3, akapit pod tabelą) — wersja rekordu operacji bez zmian, bo istniejące rodzaje mają te same bajty.
5. **Intencje** (`Intents/`): `MarkerSetIntent(MarkerId Marker, bool Shared, PlanetId Planet, Vector3 Position, string Name, MarkerIcon Icon, MarkerColor Color)` (14; `Marker == None` → nowy), `MarkerRemoveIntent(MarkerId Marker)` (15). Walidator (osobna klasa `MarkerIntentRules` w `Simulation/`, wywoływana z `IntentValidator`; nie format): gracz online; kubełek limitu `GameRules.MarkerIntentsPerSecond = 4` (pojemność 8) w `HasValidParameters` → `RateLimited`; parametry jak w op → `InvalidParameters`; nowy: limit → **`MarkerLimit`**; edycja/usunięcie: znacznik nie istnieje **albo jest cudzy prywatny** → `InvalidParameters` (ten sam kod, by nie ujawniać istnienia cudzych prywatnych); edycja ze zmianą `Shared` → `InvalidParameters`. Wspólne może zmieniać i usuwać każdy (P3). Resolver: nowy → `MarkerIds.PeekNext()`, `Owner = Shared ? null : player`; edycja → obecny `Owner`.
6. **Format** (zatwierdza Opus w kroku 1; wszystkie liczby LE, `float` bitowo):
   - op 22 i ciało blobu: `[id varuint][hasOwner u8][owner varuint gdy 1][planet varuint][x,y,z f32][name: długość varuint + UTF-8 ≤ 32][icon u8][color u8]`; op 23: `[id varuint]` (zatwierdzone w przeglądzie Opus 2026-10-11, odstępstwo 1).
   - intencja 14: `[id varuint][Shared u8][planet varuint][x,y,z f32][name: długość varuint + UTF-8][icon u8][color u8]`; intencja 15: `[id varuint]`; id 0 i id > 2^32−1 odrzuca kodek.
   - blob 11: `[BlobKind=11][format=1]` + ciało jak op 22 (pełny `MarkerRecord`); `StateCodec.Write/ReadMarker` wspólne dla blobu i komunikatu.
   - `MarkersState(IReadOnlyList<MarkerRecord> Markers)` (33): `[count varuint ≤ 512]` + rekordy; najwyżej 512 (256 wspólnych + 256 własnych prywatnych), ≤ ~37 KB < `MaxReliableMessageBytes`; `MaxMessages` — 512 rekordów z 32-bajtowymi nazwami. `ReliableOrdered`, S→K w `Loading` i `InGame` (X5 jak typ 32). Szkic miał dwie listy — jedna wystarcza (`Owner` rozróżnia). Budżet (README Y5, przegląd K5 N21): wysyłka obciąża wspólny `IReplicationSendBudget` jako ruch `Bulk` (`TryConsume(player, Bulk, bytes)`); gdy kubełek nie ma środków, `ServerSpaceSync` ponawia w kolejnych tickach, a operacje 22/23 dla gracza czekają w kolejce `MarkerReplication` do wysłania stanu (gracz nie jest „zsynchronizowany” przed `MarkersState`).
   - `ProtocolVersion` = (main przy scaleniu) + 1 (D4).
7. **Skrót i checkpoint.** `StateHasher` dopisuje na końcu sekcję znaczników **tylko gdy `MarkerIds.NextValue > 1`** (`U64(NextValue)`, `U32(count)`, po id: id, `hasOwner`, owner, planeta, 3×`F32`, nazwa, ikona, kolor) — świat bez znaczników ma dzisiejszy skrót (korpus złoty). `WorldCheckpoint` dostaje `UpsertedMarkers`, `RemovedMarkers`, `NextMarkerId` (parametry z domyślnymi pustymi wartościami na końcu rekordu); `CaptureCheckpoint` i `RequeueAfterFailedCheckpoint` obsługują zbiór znaczników `ChangeTracker`; `WorldStateBuilder.AddMarker`, `SetNextMarkerId`. `WorldCheckpoint` nie jest serializowany jako ramka (przekazanie w procesie do writera), więc nie ma wersji do podbicia; nowy format zapisu to blob 11.
8. **Persistence.** `Sql.MetaNextMarkerId = "next_marker_id"`. `PersistenceWriter` w transakcji checkpointu: `INSERT … ON CONFLICT DO UPDATE` dla `UpsertedMarkers`, `DELETE` dla `RemovedMarkers`, `SetMeta(next_marker_id)`. `WorldLoader`: odczyt `markers` po `marker_id` (blob 11, uszkodzony → `WorldCorruptedException`), brak klucza `next_marker_id` → 1. `WorldCreator`: `next_marker_id = 1`. Odzysk po awarii = checkpoint + ogon dziennika z op 22/23 (bez dodatkowego kodu).
9. **Serwer.**
   - `MarkersState`: `ServerSpaceSync` (K5-21) w `OnPlayerReady` wysyła po `StarSystemState` komunikat `MarkersState` (wspólne + prywatne tego gracza) i oznacza gracza jako zsynchronizowanego w `MarkerReplication`.
   - `MarkerReplication` (`src/Horyzont.Server/Markers/`): subskrybuje `ServerSimulation.Applied`; trzyma mapę `MarkerId → Owner` (z `WorldState` przy starcie i z op 22), bo przy op 23 znacznik już zniknął ze stanu. Op 22/23 wspólnego → `OperationApplied` (bez `ConstructChanges`) do każdego zsynchronizowanego gracza; prywatnego → tylko do właściciela, jeśli online i zsynchronizowany. Budżet `IReplicationSendBudget` z ruchem `Live` (X3). Luki `OpId` u klientów są legalne (K3-15:93).
   - K3-15 `EntityReplication` nie rozsyła op 22/23 — potwierdzone w przeglądzie K5 (N22): `EntityReplication.OnApplied` (`EntityReplication.cs:452-456`) przekazuje tylko operacje z `ConstructChanges` albo `EntityReparentOp`. Krok 0 tylko sprawdza, że to się nie zmieniło; edycja `EntityReplication.cs` niepotrzebna (test `MarkerReplication_PrivateToOwnerOnly_SharedToAll` i tak liczy bajty u drugiego klienta).
10. **Klient.** `ClientMarkerStore` (`src/Horyzont.Client/Markers/`): zastępuje zawartość przy `MarkersState`, stosuje op 22/23 z `OperationApplied` własnym applierem lustra (nie `OperationApplier`), zdarzenie `Changed`. `NavigationTarget` (`readonly record struct`: rodzaj `Marker | Body | Entity`, id). `NavigationCoordinator` (`src/Horyzont.Client/Navigation/`): `Select(NavigationTarget)`, `Clear()`, `bool TryGetGuidance(double renderTick, out NavigationGuidance guidance)` z `NavigationGuidance(Vector3D WorldDelta, double DistanceMeters, NavigationTarget Target)`; pozycja celu: znacznik → `ClientFrameGraph.TryGetWorldPose(Planet)` ∘ pozycja; ciało → środek z `ClientStarSystem`; byt → jego rama i poza z `EntityMirror`; pozycja gracza → własna poza (predykcja K3-14, po `Rebase` K5-20) w świecie. Wszystko w `double`; przepięcie nie zmienia pozycji w świecie, więc nie zmienia wektora. Cel zniknął (usunięty znacznik, byt poza zasięgiem) → `false` i czyszczenie. `DistanceFormatter.TryFormat(double meters, Span<char> dest, out int written)`: `< 1000 m` → „850 m”, `< 100 km` → „12,3 km”, dalej „123 km” (kultura z `ui.csv`, bez alokacji). Te typy są w bibliotece klienta, więc testy bez silnika.
11. **HUD (`game/src/Client/Navigation/`).** `NavigationHud` (`CanvasLayer`): co klatkę `TryGetGuidance`; kierunek `GodotFrames` → przestrzeń kamery; przed kamerą i w kadrze → `Camera3D.UnprojectPosition` (punkt pomocniczy na kierunku w odległości ≤ 1 km, by nie rzutować punktów 100 km od kamery) z ikoną, nazwą i odległością; za kamerą albo poza kadrem → `EdgeArrow` na krawędzi z marginesem 32 px. Tekst przez bufor `char[]` i `Label.Text` ustawiane tylko przy zmianie wartości (co najwyżej 10 Hz). HUD lotu K5-24/K5-25 korzysta z tego samego `TryGetGuidance`.
12. **Mapy (`game/src/UI/Map/`).** Model mapy liczony w bibliotece klienta (`SystemMapModel`, testowalny bez silnika), panel tylko rysuje.
    - `SystemMapPanel` (`Control`, rzut ortograficzny na płaszczyznę XZ ramy układu, przesuwanie i zoom): ciała z `ClientStarSystem` + nazwy z kluczy `BODY_*` (K5-21 P5), okręgi stref 3 R, strzałka słońca z `StarSystemMath.SunDirection`, statki i gracze z nazwami z `EntityMirror` (tylko znani klientowi — decyzja 13), znaczniki z `ClientMarkerStore`. Klik ciała → mapa planety; klik znacznika → wybór celu.
    - `PlanetMapSampler`: siatka 128×64 (równoodległościowa długość/szerokość) wysokości powierzchni z generatora profilu planety (`PlanetGeneratorV1.Create(planet)`), wysokość = bisekcja SDF wzdłuż promienia w [R − 300; R + 300] m do 0,5 m (≤ 11 próbek na kolumnę), na `WorkerThreadPool`, wynik (tekstura hipsometryczna) w pamięci podręcznej per (profil, ziarno, R). Bez mgły wojny. Edycje graczy nie są widoczne (spec: „z generatora”). Jedynym próbnikiem powierzchni w grze jest `PlanetSurfaceSampler` (K5-22, przegląd K5 N10): gdy jest na `main`, `PlanetMapSampler` tylko składa siatkę i teksturę z jego wysokości.
    - `PlanetMapPanel`: tekstura + gracze i znaczniki tej planety; klik powierzchni → `MarkerEditDialog` z pozycją = punkt powierzchni (`R + h` w kierunku); klik znacznika → edycja/usunięcie/wybór celu.
    - `MarkerEditDialog`: nazwa (licznik bajtów, ≤ 32 B), 8 ikon, 8 kolorów, przełącznik „wspólny” (tylko przy tworzeniu); wysyła intencje przez `IIntentSender` (K2-13); `IntentReply` z `MarkerLimit` → komunikat `REJECT_MARKER_LIMIT`.
    - Akcja `toggle_map` (N) i `Esc`; bramka: `MapInputGate : IGameplayInputGate` + złożenie z bramką menu w `GameComposition.Navigation.cs` (bez edycji `MenuController`, K3-18 jest właścicielem). Stanowisko nawigatora (K5-25) otwiera mapę przez `IMapOpener.Open()` wystawione tutaj.
13. **Gracze i statki poza zasięgiem.** Klient zna tylko byty w zasięgu zainteresowania (K3-15: 500 m na planecie, K5-26: 10 km w układzie). Spec nie przydziela kodu komunikatu obecności, więc w EA mapa pokazuje byty znane klientowi, a listę wszystkich graczy online z nazwą ciała, na którym są (bez pozycji), z `ServerHello`/listy graczy, jeśli istnieje; wariant z komunikatem `MapPresence` (35, 1 Hz) — pytanie P1.

### Pliki

```
src/Horyzont.Core/Ids/                    MarkerId.cs, MarkerIdAllocator.cs
src/Horyzont.Core/World/Markers/          MarkerRecord.cs, MarkerIcon.cs, MarkerColor.cs, MarkerStore.cs
src/Horyzont.Core/World/WorldState.cs     + Markers, MarkerIds; checkpoint znaczników; ChangeTracker (+ zbiór MarkerId)
src/Horyzont.Core/Checkpoints/            WorldCheckpoint.cs (+3 pola), WorldStateBuilder.cs (+AddMarker, SetNextMarkerId)
src/Horyzont.Core/Operations/             MarkerSetOp.cs, MarkerRemoveOp.cs, MarkerRules.cs, OperationKind.cs (+22, 23)
src/Horyzont.Core/Intents/                MarkerSetIntent.cs, MarkerRemoveIntent.cs, RejectReason.cs (+MarkerLimit = 36)
src/Horyzont.Core/Simulation/             OperationApplier.cs (22, 23), IntentValidator.cs (kubełek, dispatch),
                                          MarkerIntentRules.cs, IntentResolver.cs, GameRules.cs (+MarkerIntentsPerSecond)
src/Horyzont.Core/Hashing/StateHasher.cs  sekcja znaczników (warunkowa, na końcu)
src/Horyzont.Core/Serialization/          BlobKind.cs (+11), StateCodec.cs (Write/ReadMarker, blob 11),
                                          OperationCodec.cs (22, 23), IntentCodec.cs (14, 15), MessageCodec.cs (33)
src/Horyzont.Core/Protocol/               MessageType.cs (+33), MessageChannels.cs, ProtocolConstants.cs (D4, MaxMarkersInState = 512),
                                          Messages/MarkersState.cs
src/Horyzont.Net/Session/MessageRouter.cs typ 33 (X5)
src/Horyzont.Persistence/                 Sql.cs (+MetaNextMarkerId), WorldCreator.cs, WorldLoader.cs, Writing/PersistenceWriter.cs
src/Horyzont.Server/Markers/              MarkerReplication.cs, MarkerLog.cs
src/Horyzont.Server/Space/ServerSpaceSync.cs (K5-21)   + MarkersState
src/Horyzont.Client/Markers/ClientMarkerStore.cs
src/Horyzont.Client/Navigation/           NavigationTarget.cs, NavigationGuidance.cs, NavigationCoordinator.cs,
                                          DistanceFormatter.cs, SystemMapModel.cs, IMapOpener.cs
game/src/Client/Navigation/               NavigationHud.cs, EdgeArrow.cs
game/src/UI/Map/                          SystemMapPanel.cs, PlanetMapPanel.cs, PlanetMapSampler.cs, MarkerEditDialog.cs, MapInputGate.cs
game/src/Boot/GameComposition.Navigation.cs   partial (X10)
game/project.godot                        akcja toggle_map (M)
game/locale/ui.csv                        MAP_*, MARKER_*, NAV_*, BODY_* (jeśli nie dodał K5-21), REJECT_MARKER_LIMIT
tests/Horyzont.Core.Tests/Markers/        MarkerStoreTests.cs, MarkerOperationTests.cs, MarkerIntentTests.cs
tests/Horyzont.Core.Tests/Serialization/  MarkerCodecTests.cs (złote), MaxMessages.cs; Protocol/ProtocolContractTests.cs
tests/Horyzont.Core.Tests/Hashing/MarkerHashTests.cs; Checkpoints/MarkerCheckpointTests.cs
tests/Horyzont.Testing/Generators/CodecGen.cs   op 22/23, intencje 14/15, komunikat 33
tests/Horyzont.Net.Tests/                 router typu 33
tests/Horyzont.Persistence.Tests/MarkersPersistenceTests.cs
tests/Horyzont.Server.Tests/Markers/MarkerReplicationTests.cs
tests/Horyzont.Client.Tests/Navigation/   NavigationCoordinatorTests.cs, DistanceFormatterTests.cs, SystemMapModelTests.cs, ClientMarkerStoreTests.cs
game/tests/Navigation/                    NavigationHudTests.cs, PlanetMapSamplerTests.cs, MapPanelsTests.cs
```

## Testy akceptacyjne (napisz najpierw)

| Test | Plik | Kategoria | Faza | Co sprawdza |
| --- | --- | --- | --- | --- |
| `MarkerCodecs_GoldenBytes` (op 22 wspólny i prywatny, op 23, intencje 14/15, blob 11, `RejectReason.MarkerLimit = 36`) | `tests/Horyzont.Core.Tests/Serialization/MarkerCodecTests.cs` | domyślna | A | bajty z decyzji 6, zatwierdzone przez Opus |
| `MarkerStore_UpsertRemove_CountsAndOrder` | `tests/Horyzont.Core.Tests/Markers/MarkerStoreTests.cs` | domyślna | A | liczniki, kolejność po id, oznaczanie zmian |
| `MarkerSetOp_NewId_BumpsAllocator` / `…_IdBelowNextAndUnknown_PreconditionFails` / `…_OwnerChange_PreconditionFails` / `…_OverLimit_PreconditionFails` / `…_InvalidNameOrPosition_PreconditionFails` / `MarkerRemoveOp_Unknown_PreconditionFails` | `tests/Horyzont.Core.Tests/Markers/MarkerOperationTests.cs` | domyślna | A | decyzja 4; odrzucenie = brak zmiany stanu |
| `MarkerSetIntent_257thPrivate_MarkerLimit` / `…_257thShared_MarkerLimit` / `…_EditPrivateOfOther_Rejected` / `…_ChangeShared_Rejected` / `MarkerRemoveIntent_PrivateOfOther_Rejected` / `…_SharedByAnyone_Accepted` / `MarkerIntents_Burst_RateLimited` | `tests/Horyzont.Core.Tests/Markers/MarkerIntentTests.cs` | domyślna | A | decyzja 5 |
| `StateHasher_MarkerChange_ChangesHash` / `…_NextMarkerIdChange_ChangesHash` / `…_NoMarkersEver_HashUnchanged` | `tests/Horyzont.Core.Tests/Hashing/MarkerHashTests.cs` | domyślna | B | decyzja 7 + `git diff` korpusu pusty |
| `Checkpoint_Markers_UpsertRemoveAndRequeue` | `tests/Horyzont.Core.Tests/Checkpoints/MarkerCheckpointTests.cs` | domyślna | B | checkpoint, ponowienie po błędzie zapisu |
| `Markers_CheckpointRestart_SameMarkersAndHash` / `Markers_CrashBeforeCheckpoint_JournalReplayRestores` / `Markers_OldWorldWithoutMetaKey_NextIdIsOne` | `tests/Horyzont.Persistence.Tests/MarkersPersistenceTests.cs` | domyślna | B | 10 znaczników (5 wspólnych, 5 prywatnych dwóch graczy), checkpoint, restart; awaria (`FaultPoint`, jak `FaultInjectionTests`) → ten sam skrót |
| `MessageCodec_MarkersState_RoundTrip` + X6 + `ProtocolVersion_MatchesDocumentedMergeValue`, `Constants_GoldenValues` | `tests/Horyzont.Core.Tests/Protocol/`, `Serialization/` | domyślna | B | kodek, 512 rekordów w `MaxMessages`, D4 |
| `MessageRouter_MarkersState_ServerOnlyLoadingAndInGame` | `tests/Horyzont.Net.Tests/` | domyślna | B | X5 |
| `MarkersState_OnReady_SharedAndOwnPrivateOnly` / `MarkerReplication_PrivateToOwnerOnly_SharedToAll` / `…_RemovePrivate_OnlyOwnerNotified` / `…_NotReady_NothingSent` | `tests/Horyzont.Server.Tests/Markers/MarkerReplicationTests.cs` | domyślna | C | decyzja 9; dwóch klientów na `InMemoryTransport`; żaden bajt cudzego prywatnego nie trafia do drugiego klienta |
| `ClientMarkerStore_StateThenOps_MatchesServer` | `tests/Horyzont.Client.Tests/Navigation/ClientMarkerStoreTests.cs` | domyślna | C | lustro = stan serwera po serii op |
| `NavigationCoordinator_TargetOnMoon_FromPlanetShipAndSystemFrames_Continuous` / `…_TargetRemoved_Clears` / `NavigationCoordinator_Update_AllocatesZeroBytes` | `tests/Horyzont.Client.Tests/Navigation/NavigationCoordinatorTests.cs` | domyślna | C | gracz przepinany planeta → statek → (statek w układzie) → planeta księżyca przy tej samej pozie w świecie: skok `WorldDelta` ≤ 1 mm, odległość = \|P_cel − P_gracz\| ± 1 mm; 0 B na 10 000 wywołań |
| `DistanceFormatter_Units_AndAllocatesZeroBytes` | `tests/Horyzont.Client.Tests/Navigation/DistanceFormatterTests.cs` | domyślna | C | progi jednostek, 0 B |
| `SystemMapModel_ContainsBodiesSunEntitiesMarkers` | `tests/Horyzont.Client.Tests/Navigation/SystemMapModelTests.cs` | domyślna | D | 4 ciała, kierunek słońca = `SunDirection`, byty z nazwami, znaczniki |
| `NavigationHud_TargetInView_Pin_Behind_EdgeArrow` | `game/tests/Navigation/NavigationHudTests.cs` | silnik | D | pin w kadrze, strzałka za kamerą i poza kadrem, margines 32 px |
| `PlanetMapSampler_HeightsMatchGeneratorWithinVoxel` | `game/tests/Navigation/PlanetMapSamplerTests.cs` | silnik | D | 32 losowe kolumny: wysokość z bisekcji vs gęsta próbka SDF ≤ 1 m; wynik ten sam przy drugim wywołaniu (pamięć podręczna) |
| `SystemMapPanel_RendersModel` / `MapInputGate_Open_BlocksGameplayInput` | `game/tests/Navigation/MapPanelsTests.cs` | silnik | D | panel rysuje elementy modelu; otwarta mapa blokuje ruch i narzędzia |

## Kroki

Każdy krok: test czerwony → kod → zielone testy → mały commit (Conventional Commits). Wyszukiwanie kodu i uruchamianie testów zlecać subagentowi Haiku (CLAUDE.md projektu). Commity kroków 1–7 do przeglądu Opus.

**Faza A — kontrakty i Core**

- [x] 0. Warunki wejścia: `git status`; worktree `F:/dev/horyzont-k5-27` od `origin/main` po scaleniu K5-21; zapisać w „Odstępstwach” scalone pakiety; potwierdzić DDL `markers (marker_id, data)` w migracji Kosmosu, `ServerSpaceSync`, `ClientStarSystem`, `WorldState.HomePlanet`; sprawdzić, że kody op 22/23, intencji 14/15, odmowy 36, blobu 11 i komunikatu 33 są wolne na `main` i w otwartych PR K5; ustalić, jak `EntityReplication` (K3-15) wybiera operacje do `OperationApplied` (czy op bez zmian budowli by wysłał) i czy K3-18 dostarczył `IGameplayInputGate` i `game/locale/ui.csv`. Commit `docs:`.
- [x] 1. **Kontrakt do zatwierdzenia przez Opus przed kodem appliera:** rekordy `MarkerId`, `MarkerRecord`, `MarkerSetOp`, `MarkerRemoveOp`, `MarkerSetIntent`, `MarkerRemoveIntent`, `MarkersState`, wartości enumów, `RejectReason.MarkerLimit = 36`, `MarkerRules`; czerwone `MarkerCodecs_GoldenBytes`. Po akceptacji: kodeki op/intencji/blobu 11. Commity `test:`, `feat:`.
- [x] 2. `MarkerIdAllocator`, `MarkerStore`, `WorldState.Markers/MarkerIds`, `ChangeTracker` (zbiór znaczników); testy magazynu. Commity `test:`, `feat:`.
- [x] 3. Applier op 22/23 w `OperationApplier.cs`; testy warunków; `git diff` pusty na korpusie złotym i `DeterminismTests`. Commity `test:`, `feat:`.
- [x] 4. Walidator (`MarkerIntentRules`, kubełek w `IntentValidator.HasValidParameters`, `GameRules.MarkerIntentsPerSecond`) i resolver; testy intencji. Commity `test:`, `feat:`.

**Faza B — skrót, zapis, protokół**

- [x] 5. Warunkowy `StateHasher` (przed zmianą zapisać stałą skrótu `WorldFactory.CreateDefault()`); `WorldCheckpoint` + `CaptureCheckpoint`/`RequeueAfterFailedCheckpoint` + `WorldStateBuilder`; testy skrótu i checkpointu. Commity `test:`, `feat:`.
- [x] 6. Persistence: `Sql.MetaNextMarkerId`, `PersistenceWriter`, `WorldLoader`, `WorldCreator`; testy restartu, awarii i starego świata. Commity `test:`, `feat:`.
- [x] 7. `MessageType.MarkersState = 33`, rekord, `MessageCodec`, `MessageChannels`, `MessageRouter` (X5), fixture X6 (`ProtocolContractTests`, `MaxMessages`, `CodecGen` także dla op/intencji), D4 z aktualizacją dwóch istniejących testów. Commity `test:`, `feat:`.

**Faza C — serwer i klient**

- [x] 8. `MarkerReplication` + `MarkersState` w `ServerSpaceSync` (budżet `Bulk`, kolejka op do synchronizacji); rejestracja w `GameComposition.Navigation.cs` (także serwer dedykowany); testy dwóch klientów. Commity `test:`, `feat:`.
- [x] 9. `ClientMarkerStore`, `NavigationTarget`, `NavigationGuidance`, `NavigationCoordinator`, `DistanceFormatter`, `SystemMapModel`; testy ciągłości i alokacji. Commity `test:`, `feat:`.

**Faza D — gra**

- [x] 10. `NavigationHud` + `EdgeArrow`; test silnika. Commity `test:`, `feat:`.
- [x] 11. `SystemMapPanel`, `PlanetMapSampler`, `PlanetMapPanel`; testy silnika (próbkowanie, render modelu). Commity `test:`, `feat:`.
- [ ] 12. **[K3-18]** (kod i testy gotowe; brak: ręczny przebieg `Manual` w silniku)  `MarkerEditDialog`, akcja `toggle_map`, `MapInputGate` + złożenie bramek, `IMapOpener`, klucze `ui.csv`, tekst odmowy 36; test bramki; ręczny przebieg (tworzenie, edycja, usunięcie, prywatny niewidoczny dla drugiego gracza) wpisany do PR (`Manual`). Commity `test:`, `feat:`.

**Faza E — odbiór**

- [ ] 13. (zrobione: ZASADY pkt 3, wiersz README, odstępstwa; zostaje: rebase, `ProtocolVersion`, smoke, zrzuty, PR) Rebase na `main`; pełne komendy odbioru; `docs/ZASADY.md` pkt 3: `MarkerRecord` i `MarkerId` jako typy zapisywane, `MarkerRules` jako stałe semantyki operacji; wiersz scalenia K5-27 w istniejącej sekcji „Pakiety K5” `docs/plans/README.md` z faktyczną wartością `ProtocolVersion`; PR ze zrzutami mapy i HUD, odstępstwami i odpowiedziami na pytania. Commit `docs:`.

## Odbiór

```
dotnet build Horyzont.sln
dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"
dotnet format Horyzont.sln --verify-no-changes
python tools/engine/build.py smoke
```

Plus job `game` (`game/tests/Navigation/`), ręczny przebieg z kroku 12 w PR i `git diff origin/main` pusty na `tests/Horyzont.Core.Tests/Golden/TerrainJournals/`, `TerrainJournalGoldenCorpusTests.cs`, `Hashing/DeterminismTests.cs`.

## Kody i punkty styku

Kody zajęte przez K5-27 (spec 02:151–176, zakres Kosmosu z K1-03): **`OperationKind.MarkerSet = 22`, `MarkerRemove = 23`; `IntentCode` 14 (`MarkerSet`), 15 (`MarkerRemove`); `RejectReason.MarkerLimit = 36`; `BlobKind.Marker = 11`; `MessageType.MarkersState = 33`**. Bez nowych migracji. Nie koliduje z: K5-21 (blob 10, komunikat 32), K5-23 (op 20, intencja 10, odmowy 30, 31, 37), K5-24 (op 21, intencja 13, odmowa 39, blob 12), K5-25 (intencje 11, 12, 16, odmowy 32–35, 38, komunikaty 30, 34), K5-26 (komunikat 31).

Kolejność scalania: **(K5-23 ↔ K5-20 dowolnie) → K5-21 → K5-27**; K5-27 nie wymaga K5-23 (K5-27 równolegle z K5-22/24/25/26; scalany później integruje — X10). Rekomendacja: K5-27 po K5-24, jeśli oba gotowe jednocześnie, bo K5-24 dodaje do HUD lotu wywołanie `TryGetGuidance` dopiero, gdy `NavigationCoordinator` istnieje — w przeciwnej kolejności K5-24 (scalany później) dopina wywołanie.

| Plik / punkt | K5-27 | Inny pakiet | Rozwiązanie |
| --- | --- | --- | --- |
| `OperationKind`, `OperationCodec`, `OperationApplier.cs` | 22, 23 | K5-23 (20), K5-24 (21) | przypadki obok siebie w kolejności numerów; scalany później |
| `IntentCodec`, `Intents/`, `IntentValidator.cs`, `IntentResolver.cs`, `GameRules` | 14, 15, kubełek | K5-23 (10), K5-24 (13), K5-25 (11, 12, 16) | jw. |
| `RejectReason` | 36 | 30, 31, 37 (K5-23), 39 (K5-24), 32–35, 38 (K5-25) | jw. |
| `BlobKind`, `StateCodec` | 11 | 10 (K5-21), 12 (K5-24) | jw. |
| `MessageType`/`Channels`/`Codec`/`Router`, fixture X6 | 33 | 32 (K5-21), 30/34 (K5-25), 31 (K5-26) | jw. |
| `ProtocolVersion` + dwa testy | podbicie | wszystkie pakiety K5 z formatem | D4 |
| `StateHasher.cs` | sekcja znaczników na końcu (warunkowa) | K5-21 sekcja układu | rozłączne |
| `WorldState.cs`, `WorldCheckpoint.cs`, `WorldStateBuilder.cs`, `PersistenceWriter.cs` | znaczniki | K5-24 `ShipState` (tabela `ship_state`) | sąsiednie pola i bloki; scalany później |
| `ServerSpaceSync.cs` (K5-21) | + `MarkersState` | — | po K5-21 |
| `EntityReplication.cs` (K3-15) | bez edycji (N22: op 22/23 i tak nie są przekazywane) | K5-20, K5-26 | — |
| `PlanetMapSampler` ↔ `PlanetSurfaceSampler` (K5-22) | mapa planety | K5-22 (jedyny próbnik powierzchni) | gdy K5-22 na `main`: `PlanetMapSampler` = adapter nad `PlanetSurfaceSampler`; gdy nie — bisekcja tutaj, K5-22 ją przenosi |
| HUD lotu | `NavigationCoordinator.TryGetGuidance` | K5-24/K5-25 | konsument dopina wywołanie |
| Stanowisko nawigatora | `IMapOpener` | K5-25 (fotel `SeatRole.Navigator` z K5-23) | K5-25 woła `Open()` |
| `GameComposition` | `GameComposition.Navigation.cs` | `.Frames` (K5-20), `.Ships` (K5-23), `.Space` (K5-21) | osobne partiale |
| `game/locale/ui.csv`, `project.godot` | klucze i akcja | K3-18, K5-23 (klawisz G), K5-25 | dopisywanie; klawisz M wolny (sprawdzić w kroku 0) |

## Ryzyka

| Ryzyko | Skutek | Odpowiedź |
| --- | --- | --- |
| Wyciek prywatnych znaczników (`MarkersState`, `OperationApplied`, odpowiedź na edycję cudzego) | utrata prywatności | jeden nadawca z mapą właścicieli; K3-15 nie wysyła 22/23; ten sam `InvalidParameters` dla nieistniejącego i cudzego; test bajtów u drugiego klienta |
| Znaczniki tylko w dzienniku, nie w checkpoincie | utrata po przycięciu dziennika | decyzja 7–8; test restartu i awarii |
| Sekcja skrótu zmienia korpus złoty | czerwony CI | warunek `NextValue > 1` |
| Skok wskaźnika przy przepięciu (własnym albo celu) | nieczytelna nawigacja | wszystko w świecie w `double`; `Rebase` K5-20; test ciągłości ≤ 1 mm |
| Rzutowanie punktu odległego o 100 km | błędy precyzji ekranu | rzutujemy punkt pomocniczy na kierunku ≤ 1 km od kamery |
| Próbkowanie generatora blokuje klatkę | przycięcie przy otwarciu mapy | `WorkerThreadPool`, siatka 128×64, pamięć podręczna; graf generatora tworzony na wątku głównym (`Create` — „Main thread”) |
| Mapa nie pokazuje dalekich graczy | niepełne kryterium :92 w odczuciu graczy | decyzja 13; P1 z gotowym wariantem `MapPresence` (35) |
| Spam intencjami | pasmo i dziennik | kubełek 4/s, limit 256 + 256 |

## Pytania do właściciela (wariant domyślny obowiązuje bez odpowiedzi)

| # | Pytanie | Wariant domyślny |
| --- | --- | --- |
| P1 | Pokazywać na mapie graczy i statki spoza zasięgu zainteresowania (nowy komunikat `MapPresence` 35, 1 Hz, ~20 B/gracz)? | nie w K5-27 — tylko byty znane klientowi; lista graczy z nazwą ciała bez pozycji |
| P2 | Paleta ikon i kolorów? | decyzja 2 (8 + 8) |
| P3 | Kto zmienia i usuwa znaczniki wspólne? | każdy gracz (co-op) |
| P4 | Znaczniki w kosmosie (rama układu)? | nie — punkt zapisywany w ramie najbliższego ciała (decyzja 1) |
| P5 | Czy wybrany cel nawigacji jest zapamiętywany po restarcie? | nie — stan klienta, czyszczony przy rozłączeniu |
| P6 | Klawisz mapy? | `M`, zamykanie `M` albo `Esc` |

## Pułapki

- Applier op 22/23 nie czyta generatora, terenu ani treści i nie zna aktora — uprawnienia są w walidatorze.
- Właściciel znacznika jest niezmienny; edycja niesie obecnego właściciela.
- Cudzy prywatny znacznik: każdy kanał (stan, operacje, kody odmów) zachowuje się jak dla nieistniejącego.
- Znaczniki muszą trafić do checkpointu — dziennik po checkpoincie jest obcinany.
- Skrót: sekcja znaczników tylko gdy `MarkerIds.NextValue > 1`, na samym końcu.
- Pozycje w HUD i na mapie składać w `double` przez `ClientFrameGraph`; do `float` tylko końcowy kierunek/rzut (`GodotFrames`).
- `NavigationHud._Process` bez alokacji: bez interpolacji stringów, LINQ i lambd; `Label.Text` tylko przy zmianie.
- `OperationApplier.cs`, `Operations/`, `Intents/` to ścieżki formatu — bez partiali w innych plikach.

## Odstępstwa i decyzje wykonawcy

Wykonawca: Claude (Sonnet 5.5, na zlecenie hosta), gałąź `pakiet/k5-27-mapa-i-nawigacja` od `origin/main` 37096d1.

**Stan wejścia (krok 0).** Na `main` są K3 (w tym K3-15, K3-18: `IGameplayInputGate`, `game/locale/ui.csv`), K5-23, K5-20, K5-21 (`StarSystemState` 32, blob 10, `M0002_Space` z tabelą `markers (marker_id, data)`, `ClientStarSystem`, `WorldState.HomePlanet`). Kody 22/23, 14/15, 36, blob 11, komunikat 33 były wolne. `EntityReplication.OnApplied` przekazuje tylko operacje z `ConstructChanges` albo `EntityReparentOp` (N22 potwierdzone, plik bez edycji). K5-22 i K5-24 nie są na `main`: `PlanetMapSampler` jest własną bisekcją (`PlanetMap` + `PlanetMapCache` + `SurfaceBisection`), do podmiany na `PlanetSurfaceSampler`, gdy K5-22 wejdzie.

**Odstępstwa od planu.**
1. **Format bajtów** (decyzja 6) zapisany konwencją istniejących kodeków (`ByteWriter`): identyfikatory (`MarkerId`, `PlayerId`, `PlanetId`) jako `varuint`, nazwa jako `WriteString` (długość `varuint` + UTF-8), liczba rekordów `MarkersState` jako `varuint`, a nie `u64`/`u8`/`u16` ze szkicu. Złote bajty w `MarkerCodecTests`. **Zatwierdzone przez przegląd Opus 2026-10-11.**
2. **Klawisz mapy: `N`, nie `M`.** `M` jest zajęty przez `tool_material_next` (`project.godot`). Pytanie P6 do właściciela. Zamykanie: ten sam klawisz albo `Esc` (kolejność `_Input` względem menu pauzy nie jest sprawdzona w silniku).
3. **`MarkerReplication` wysyła `MarkersState` sam** (jest `IServerPlayerObserver` zarejestrowanym po `ServerSpaceSync`), a nie przez edycję `ServerSpaceSync`; kolejność `StarSystemState` → `MarkersState` zachowana. Operacje podczas oczekiwania na stan (budżet `Bulk` bez środków) nie są kolejkowane: stan budowany przy wysyłce już je zawiera (ten sam efekt, mniej kodu). Brak `MarkerLog.cs` (nie ma nic do zalogowania).
4. **Pozycja własna w nawigacji** to pozycja bieżącej kamery Godota (świat Godota = rama układu, K5-20) w `double` przez `GodotFrames` (`CameraViewpoint`), a nie osobna ścieżka z `OwnState`. Rzutowanie na ekran: własny `NavigationScreenMath` (kierunek w przestrzeni kamery, bez `UnprojectPosition` i bez punktu pomocniczego).
5. **Bramka wejścia**: `MapInputGate` + `CompositeInputGate`, składane przez `GameComposition.ComposeInputGate`; wymagało dwóch jednolinijkowych zmian w `GameUiCoordinator.Attach` (K3-18), bo ten przypisuje `reader.Gate = this` po złożeniu sesji. `MenuController` bez zmian.
6. `GameComposition.cs`: `partial void ConfigureNavigation()` i wywołanie po `ConfigureMotion()` (X10).
7. Dodatkowe typy biblioteki klienta: `IWorldPoseSource`/`ClientFramePoses` (nić między `ClientFrameGraph` a koordynatorem, testowalne bez silnika), `INavigationViewpoint`, `PlanetMapProjection`, `SurfaceBisection`, `NavigationScreenMath`.
8. `WorldCheckpoint` ma dodatkowy konstruktor bez pól znaczników (stare wywołania w testach/pakietach równoległych kompilują się bez zmian).
9. Nazwy graczy na mapie układu: klient ich nie zna (`EntitySpawn` niesie tylko `Owner`); model przyjmuje funkcję nazw, gra podaje `#<id gracza>` dla cudzych bytów (P1 bez zmian).

**`ProtocolVersion` = 10** (`main` 9 + 1, D4); K5-22 bierze równolegle też 10: do przeliczenia przy scaleniu (`ProtocolContractTests`, `ProtocolConstants`, komentarz w planie). Odstępstwo 10 (przegląd Opus 2026-10-11): kolizję z K5-22 zostawiono bez zmian, wartość jest przeliczana przy scaleniu.

**Wyniki.** `dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"` zielone poza znanym kruchym `Replicate_LargeConstruct_DoesNotBlockTicksAndArrivesComplete` (zegar ścienny, nie dotyczy pakietu). Testy restartu (`Markers_CheckpointRestart_SameMarkersAndHash`), awarii (`Markers_CrashBeforeCheckpoint_JournalReplayRestores`), prywatności (`MarkerReplication_PrivateToOwnerOnly_SharedToAll`, dwóch klientów na `InMemoryTransport`, brak bajtów cudzego prywatnego) i ciągłości nawigacji (`NavigationCoordinator_TargetOnMoon_FromPlanetShipAndSystemFrames_Continuous`, ≤ 1 mm) przechodzą. Testy silnika w `game/tests/Navigation/` są napisane i się kompilują, **nie uruchamiane lokalnie** (zakaz gdUnit na tej maszynie): `NavigationHud_TargetInView_Pin_Behind_EdgeArrow`, `PlanetMapSampler_HeightsMatchGeneratorWithinVoxel`, `SystemMapPanel_RendersModel`, `MapInputGate_Open_BlocksGameplayInput`.

**Do zrobienia przed PR.** Ręczny przebieg `Manual` kroku 12 (tworzenie, edycja, usunięcie, prywatny niewidoczny dla drugiego gracza); krok 13 (rebase na `main`, wartość `ProtocolVersion`, `python tools/engine/build.py smoke`, zrzuty).

**Krok 1 (kontrakt Opus):** kontrakt bajtów zatwierdzony przez przegląd Opus 2026-10-11 (odstępstwo 1).

**Lista kontrolna ręcznego przebiegu `Manual` (krok 12, dla właściciela; dwa klienty, host + gość):**
1. Klawisz `N` otwiera mapę układu; `N`/`Esc` zamyka; podczas mapy ruch i narzędzia są zablokowane. `Esc` przy otwartej mapie zamyka mapę i nie otwiera menu pauzy. Wpisanie `N` w polu nazwy znacznika nie zamyka mapy.
2. Mapa planety: widoczna wysokościomierz-mapa, kursor pokazuje położenie; bieżąca pozycja gracza zaznaczona.
3. Utwórz znacznik (nazwa, ikona, kolor, prywatny/wspólny): pojawia się na mapie i jako pinezka w HUD.
4. Edytuj znacznik (nazwa, kolor): zmiana widoczna od razu; po restarcie serwera zostaje.
5. Ustaw znacznik jako cel nawigacji: pinezka w polu widzenia, strzałka na krawędzi, gdy cel za plecami; odległość z jednostką.
6. Usuń znacznik: znika z mapy i HUD; cel nawigacji czyszczony.
7. Znacznik prywatny jest niewidoczny na drugim kliencie; wspólny — widoczny u obu.
8. Przekroczenie limitu znaczników: komunikat odmowy 36 po polsku.
9. Nawigacja do znacznika na księżycu: odległość ciągła przy zmianie ramy (planeta → statek → układ).
