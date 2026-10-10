# K5-20 · Ramy odniesienia w ruchu

| | |
| --- | --- |
| Prowadzi | Opus 5.5 (Claude Code, `claude --model opus`) |
| Testy akceptacyjne | Opus 5.5 (krok 1 — test Jolt daleko od początku — przed jakimkolwiek innym kodem) |
| Przegląd | GPT-6.1 Sol (ZASADY pkt 9: pakiet Opus → drugi przegląd Sol); zmiany w `src/Horyzont.Core/` prowadzi i przegląda Opus 5.5 (ZASADY pkt 3) |
| Zależy od | K4-19 (plan projektu). Decyzja właściciela: start od razu. Faza A rusza na `main` (K2 + K3-00 + K3-15); fazy B–D czekają na scalenie wskazanych pakietów K3 (tabela „Fazy”); faza E (odbiór) po scaleniu K3-14 i K3-15. **Wariant domyślny (przegląd K5, P11): K5-20 nie czeka na K4-19** — żaden test pakietu nie używa kodu K4-19, a K4-19 jest pakietem odbiorczym; alternatywa: scalenie dopiero po K4-19 (właściciel może ją wybrać) |
| Gałąź | `pakiet/k5-20-ramy` (worktree `F:/dev/horyzont-k5-20`) |
| Kamień milowy | K5 Kosmos; odbiór pakietu wg spec 02: „przepinanie bytów testowych pod opóźnieniem bez skoków ponad 0,5 m i stanów w złej ramie; test Jolt daleko od początku układu” |
| Równolegle | K5-23 (Sonnet) — bez krawędzi startu; mapa kolizji i kolejność scalania w sekcji „Równoległość z K5-23” |

**Cel:** dać całej grze ruchome ramy odniesienia — złożenie ram w podwójnej precyzji (`Vector3D`, `QuaternionD`, `PoseD`, `FrameMath`, `FrameGraph`), ruchome ramy budowli w trybie statku poza dziennikiem, przepięcie bytu z zachowaniem pozy w świecie, fazy ticka, historię transformacji ram (500 ms), drzewo sceny odpowiadające ramom i interfejs przestrzeni ramy — tak, żeby 1000 przepięć bytów testowych pod opóźnieniem nie dało skoku ponad 0,5 m ani stanu zastosowanego w złej ramie, a Jolt w podwójnej precyzji był stabilny ok. 115 km od początku układu.

## Kontekst

- `docs/ZASADY.md`: pkt 2 (rama rodzica + `ParentSeq` + poza lokalna jako stan kanoniczny; ramy składa się tylko w `double` — `Vector3D`, `QuaternionD`, `FrameMath` w Core „od K5-20”; konwersja Godot ↔ rama planety tylko przez `PlanetFrames`; zero mutowalnego stanu statycznego), 3 (Core = przegląd Opus; `Protocol/`, `Serialization/` → `ProtocolVersion`; `SurfaceFrame` chroniony), 4 (determinizm), 6 (gorące ścieżki: zero alokacji, testy alokacji), 8 (czas wirtualny, kategorie).
- `docs/spec/02-kosmos.md`: „Architektura i model danych” (drzewo ram), „Ramy”, „Scena i fizyka”, „Sieć” (migawki w ramie rodzica, `ParentSeq`: starszy odrzucony, nowszy wstrzymany do 1 s → `ResyncRequest`), „Obsługa błędów”, „Testy” (wiersze „Jolt w podwójnej precyzji…” i „Przepinanie pod opóźnieniem”), „Ryzyka” (Jolt ok. 110 km), „Pakiety pracy” (wiersz K5-20).
- `docs/spec/01-planeta.md:126` (świat w `double`, środek układu w środku planety domowej, przez sieć floaty), `:143`, `:174`.
- `docs/plans/README.md` K3: X3 (budżety migawek), X4/D4 (podbijanie `ProtocolVersion`), X9 (inni gracze tylko wizualni), X10 (integruje pakiet scalany później), X11 (pomiary z silnikiem).
- Plany: K2-11 (`PlanetFrames`, `SurfaceFrame`, `UpdatePlayerKinematics`, wiersz „Przepięcia ram … K5-20”), K2-12 (decyzja 13: poza budowli przez jedną funkcję, „żeby od K5 dało się podmienić ramy”), K2-13:454 (składanie póz zagnieżdżonych budowli w `BlockOverlap.TryGetPose` jest w `float` — odroczone do K5-20), K3-14 (`RemoteMotionBuffer` resetuje się przy zmianie rodzica/`ParentSeq`, `Reconcile_LargeErrorOrFrameChange_SnapsImmediately`), K3-15 (wiersz K2-13:511 „pozy względne budowli wspólnego rodzica — odrzucone do K5-20”), K4-19 (boty, `CoopRig`, `LatencyTransport`).

Kod, na którym pakiet buduje (sprawdzony na `F:/dev/horyzont-k3-15`, gałąź K3-15, PR #20; krok 0 potwierdza na `main`):

| Co | Gdzie |
| --- | --- |
| `FrameRef(FrameKind Kind, ulong Id)`, `FrameKind { System = 1, Planet = 2, Construct = 3 }` | `src/Horyzont.Core/Ids/FrameRef.cs`, `FrameKind.cs` |
| `EntityReparentOp(Entity, NewParent, NewParentSeq, LocalPosition, LocalRotation, LocalVelocity, SeatCell?)` — „nic jej nie tworzy w podprojekcie 1”; applier: `ApplyReparent` (gracz: rama, seq, poza, fotel; budowla: `Construct.Reparent`), `CheckReparent` (seq = bieżący + 1, rama istnieje, budowla nie do siebie/potomka, poza skończona, fotel tylko gracz → budowla, fotel niepusty i wolny) | `src/Horyzont.Core/Operations/EntityReparentOp.cs`, `Simulation/OperationApplier.cs:127-142`, `:294-377` |
| `ServerSimulation.ApplyServerOperation(Operation)` (operacja serwera z dziennikiem), `UpdatePlayerKinematics(player, frame, parentSeq, …) → bool` (poza dziennikiem, odrzuca nieaktualną ramę/seq) | `src/Horyzont.Core/Simulation/ServerSimulation.cs:138`, `:235` |
| `Construct` (`Parent`, `ParentSeq`, `Mode` tylko do odczytu, `Origin`, `Rotation`, `Reparent(...)`), `ConstructMode { Static = 0, Ship = 1 }`, `ConstructHeaderRecord` (z `Mode`) | `src/Horyzont.Core/World/Constructs/`, `Checkpoints/ConstructHeaderRecord.cs` |
| `StateHasher` pomija pozę budowli `Ship` (hashuje ramę, `ParentSeq`, tryb) | `src/Horyzont.Core/Hashing/StateHasher.cs:16`, `:58-61` |
| `WorldState.FrameExists`, `HasChildren`, `TryGetSeated`, `ConstructStore.InFrame` | `src/Horyzont.Core/World/WorldState.cs` |
| `Snapshot.Frames` (1–8 `SnapshotFrame(FrameRef, ParentSeq)`), `EntityState(…, FrameIndex, ParentSeq, Position, Rotation, Velocity, …)`, `EntityKind { Player = 1, Construct = 2 }` | `src/Horyzont.Core/Protocol/Messages/` |
| `EntityMirror` (K3-15): parkuje próbki z nowszym `ParentSeq` (≤ 32 na byt), `ResyncTracker`, przepięcie z `OperationApplied { EntityReparentOp }`, `MaxFrameDepth = 8` | `src/Horyzont.Client/Replication/EntityMirror.cs:14-100` |
| `EntityReplication` (K3-15): budowle tylko przez `ConstructState` — nie są w `Mobile`, więc nie trafiają do migawek | `src/Horyzont.Server/Replication/EntityReplication.cs:320-344` |
| `InterestSubject(Entity, Region, Position, …)`, `InterestViewer(Player, Region, Position)` — odległości w ramie regionu (planeta albo układ) | `src/Horyzont.Net/Session/` |
| `IServerTickParticipant.OnTick` wołane w kolejności rejestracji (`_participants`) | `src/Horyzont.Server/IServerTickParticipant.cs`, `ServerRuntime.cs:34`, `:114`, `:234` |
| `ServerCharacters` tworzy ciało tylko dla gracza w ramie planety (`SpawnRefused "the player is not on a planet"`) | `game/src/Server/Characters/ServerCharacters.cs:130` |
| `PlanetFrames` (statyczna, czysta): rama planety = świat Godota, `MaxCoordinate = 100 000 m` | `game/src/Shared/Frames/PlanetFrames.cs` |
| Silnik w `double`: `engine/engine.json:11` `"precision": "double"`, `engine/smoke/SmokeTest.cs` sprawdza `real_t = double` | `engine/` |

Brak dziś: typów `double` w Core, składania ram, historii transformacji, faz ticka, węzłów ram w scenie, jakiegokolwiek producenta `EntityReparentOp`, ruchu budowli.

## Zakres

1. **Core, nowy katalog `src/Horyzont.Core/Frames/`** (poza ścieżkami formatu; przegląd Opus): `Vector3D`, `QuaternionD`, `PoseD`, `FrameMath`, `FrameLimits`, `IPlanetPlacement` + `HomeOnlyPlacement`, `IFramePoseSource`, `FrameGraph`, `WorldStateFramePoses`, `FramePoseHistory`.
2. **Core, symulacja:** `ServerSimulation.UpdateConstructKinematics` (ruch budowli `Ship` poza dziennikiem) i `Construct.SetPose` (internal).
3. **Serwer:** fazy ticka (`ServerTickPhase`), `FrameTransitions` (przepięcie z zachowaniem pozy w świecie), `ServerFrameHistory` (500 ms), budowle `Ship` w migawkach, zainteresowanie bytów w ramach budowli, komenda deweloperska `frame`.
4. **Klient (biblioteka):** `ClientFrameGraph` i kliencka historia ram z migawek; przepięcie bez skoku w interpolacji innych bytów (`Rebase` zamiast resetu) i we własnej predykcji; gracz w ramie budowli = przyczepiony (bez własnego ruchu).
5. **Gra:** `GodotFrames` (jedyna konwersja `PoseD`/`Vector3D` ↔ Godot), `FrameNodes` (drzewo: układ → planety → budowle i postacie; przepięcie z zachowaniem transformacji globalnej), `IFrameSpace` (przestrzeń fizyki + grawitacja z ramy), przyczepione ciała graczy w ramach budowli, audyt wywołań `PlanetFrames` z pozycjami globalnymi.
6. **Testy:** jednostkowe i właściwości (FsCheck), alokacji, integracyjny test akceptacyjny 1000 przepięć pod opóźnieniem (`Slow`), testy silnika daleko od początku układu.

## Poza zakresem

- Układ gwiezdny, rozmieszczenie ciał, strefy wpływu, grawitacja stref, przepinanie przy granicy stref (K5-21; K5-20 daje mu `FrameTransitions` i `IPlanetPlacement`).
- Wiele terenów, impostory (K5-22). K5-20 nie przesuwa węzła terenu planety domowej (stoi w początku, jak dziś); test silnika daleko od początku używa osobnej, testowej instancji terenu.
- Tryb statku i bloki (K5-23), model lotu i fizyka lecącego statku (K5-24), fotele, wsiadanie, `ShipInputBatch`, predykcja pilota (K5-25), priorytety i zasięg statków w ramie układu, `ShipStatus` (K5-26). K5-20 porusza ramami budowli wyłącznie skryptem testowym przez `UpdateConstructKinematics`.
- Chodzenie po lecącej budowli i osobne przestrzenie fizyki (po EA; interfejs `IFrameSpace` je umożliwia).
- Kompensacja opóźnień (K7-41; K5-20 daje jej `FramePoseHistory`).
- Nowe kody operacji, intencji, odmów, komunikatów i blobów: **żadnych**. Jedyna zmiana formatu sieci to semantyka (budowle `Ship` w migawkach) → podbicie `ProtocolVersion` (D4).

## Fazy i zależność od niescalonych pakietów

Po każdym scaleniu pakietu K3/K4 do `main` wykonawca robi rebase `pakiet/k5-20-ramy` i dopiero wtedy rusza z odblokowaną fazą; krok 0 fazy mapuje rzeczywiste nazwy API (K3-14/K3-15 cytowane z planów) i wpisuje różnice do „Odstępstw”.

| Faza | Kroki | Wymaga scalonych | Co daje |
| --- | --- | --- | --- |
| A | 0–7 | K2, K3-00 (`main` dziś) | test Jolt daleko od początku (najpierw), matematyka ram, graf ram, historia, ruch budowli `Ship`, fazy ticka, przepięcie z zachowaniem pozy, historia serwera |
| B | 8–9 | K3-15 | budowle `Ship` w migawkach, zainteresowanie w ramach budowli, `ClientFrameGraph` |
| C | 10–11 | K3-14 (+ K3-15) | interpolacja i predykcja bez skoku przy przepięciu, test akceptacyjny 1000 przepięć |
| D | 12–13 | K3-14, K3-15 (węzły zdalnych bytów) | `GodotFrames`, `FrameNodes`, `IFrameSpace`, przyczepione ciała, przepięcie węzłów |
| E | 14 | K3-14, K3-15 (K4-19 nie jest warunkiem — P11) | odbiór, ZASADY, README, PR |

## Mapa kryteriów → dowód

| # | Kryterium | Dowód | Kategoria / job | Faza |
| --- | --- | --- | --- | --- |
| 1 | Jolt w `double` ok. 115 km od początku układu: stojąca postać nie drga o więcej niż 1 mm, kolizje stabilne (spec „Testy”; tu na terenie planety domowej przesuniętym testowo o 110 km — wariant na prawdziwej planecie C powtarza K5-28) | `FarFromOrigin_*` (tabela testów) | testy silnika, job `game` (raport + próg) | A |
| 2 | 1000 losowych przepięć (planeta ↔ budowla z fotelem i bez ↔ układ dla budowli) przy RTT 100–250 ms i 5 % strat: żaden skok ponad 0,5 m, żaden stan zastosowany w złej ramie (spec „Testy”, „Przepinanie pod opóźnieniem”, wersja dla bytów testowych) | `Reparent_ThousandRandom_UnderLatency_*` (zdalny obserwator i własny byt) | `Slow` / `ci.yml` build-and-test | C |
| 3 | Kolejność ticka: ramy i budowle → postacie w ramach → złożenie pozy → interpolacja renderu (spec „Ramy”) | `ServerRuntime_Participants_RunInPhaseOrder`, `ClientTick_*` | domyślna | A, D |
| 4 | Serwer trzyma 500 ms historii transformacji ram | `ServerFrameHistory_*` | domyślna | A |
| 5 | Przepięcie przenosi węzeł pod nowego rodzica z zachowaniem transformacji globalnej (spec „Scena i fizyka”) | `FrameNodes_Reparent_KeepsGlobalTransform` | testy silnika | D |
| 6 | Interfejs przestrzeni ramy (przestrzeń kolizji + grawitacja dla kontrolera postaci) | `FrameSpace_*` | testy silnika | D |

## Projekt

### Decyzje

1. **Brak pływającego początku.** Świat Godota = rama układu (środek w planecie domowej, stałe osie). Węzły planet stoją w swoich pozach (K5-20: tylko planeta domowa w tożsamości), fizyka to jedna przestrzeń Jolt w `double`. Krok 1 to potwierdza; jeśli nie przejdzie, praca staje i eskaluje do Opus + właściciela (P1), bo plan B (pływający początek per region) zmienia K5-21/22.
2. **Typy `double` w Core** (`src/Horyzont.Core/Frames/`): `readonly record struct Vector3D(double X, double Y, double Z)`, `QuaternionD(double X, double Y, double Z, double W)`, `PoseD(Vector3D Position, QuaternionD Rotation)`; operatory i metody bez alokacji. `FrameMath` (statyczne, czyste): `Compose(in PoseD parent, in PoseD local)`, `Inverse`, `TransformPoint`, `TransformDirection`, `Relative(in PoseD from, in PoseD to)`, `FromFloat(Vector3, Quaternion)`, `TryToFloat(in PoseD, FrameKind, out Vector3, out Quaternion)` (kontrola skończoności, zakresu z `FrameLimits` i normalizacji; jedyne miejsce rzutowania `double` → `float` dla pozy w ramie). Rotacje normalizowane po każdym złożeniu.
3. **`FrameLimits`**: zakres współrzędnych lokalnych `float` — rama planety i budowli ≤ 100 000 m (jak `PlanetFrames.MaxCoordinate`), rama układu ≤ 500 000 m (rozdzielczość `float` ≤ 3,1 cm, wystarcza dla statku poza strefami, gdzie nic nie styka się z terenem; P2).
4. **Graf ram.** `IFramePoseSource.TryGetLocalPose(FrameRef frame, out FrameRef parent, out PoseD poseInParent)`: planeta → rodzic `System`, poza z `IPlanetPlacement`; budowla → `Construct.Parent`, poza `(Origin, Rotation)`; układ → korzeń. `FrameGraph` (klasa bez stanu poza źródłem): `TryGetWorldPose(FrameRef, out PoseD)`, `TryRelative(FrameRef from, FrameRef to, out PoseD)`, `TryExpress(FrameRef from, Vector3 local, FrameRef to, out Vector3D)`, `TryGetRegion(FrameRef, out FrameRef region)` (najbliższy przodek `Planet` albo `System`). Głębokość ≤ 8 (jak `EntityMirror.MaxFrameDepth`), cykl albo brak ramy → `false`. Zero alokacji. `WorldStateFramePoses` (Core) = źródło nad `WorldState` + `IPlanetPlacement`. Pozy zagnieżdżonych budowli w `BlockOverlap.TryGetPose` przechodzą na `FrameGraph` (dług z K2-13:454) — wynik dla przypadków bez zagnieżdżenia bitowo ten sam (złote przypadki `ConstructPlacement` bez zmian): ścieżka głębokości 1 (budowla bezpośrednio w ramie planety) zwraca `Origin`/`Rotation` budowli bez przejścia przez `double`; `FrameGraph` dotyczy tylko głębokości ≥ 2.
5. **`IPlanetPlacement`** (`bool TryGetPose(PlanetId, out PoseD)`): K5-20 dostarcza `HomeOnlyPlacement` (planeta domowa = tożsamość, inne → `false`); K5-21 podmienia na układ gwiezdny. Obrót planety jest polem pozy (w EA tożsamość), więc obracające się ciała nie wymagają zmiany grafu.
6. **Ruch ramy budowli poza dziennikiem.** `ServerSimulation.UpdateConstructKinematics(EntityId construct, FrameRef frame, uint parentSeq, Vector3 origin, Quaternion rotation) → bool`: tylko `Mode == Ship`, rama i `ParentSeq` bieżące, poza poprawna (`OperationApplier.IsPose`), inaczej `false` (zła poza logowana jak w `UpdatePlayerKinematics`). Ustawia `Construct.SetPose` (internal) i oznacza nagłówek jako zmieniony (checkpoint zapisze pozę — spec „Zapis”: statek w ruchu zawsze „zmieniony”). Skrót stanu bez zmian (`StateHasher` już pomija pozę `Ship`), format checkpointu bez zmian. Budowli `Static` nie da się ruszyć poza operacją.
7. **Przepięcie z zachowaniem pozy w świecie.** `FrameTransitions` (`src/Horyzont.Server/Frames/`): `TryReparent(EntityId entity, FrameRef newParent, CellPos? seat, out AppliedOperation? applied, out FrameTransitionFailure failure)`: liczy bieżącą pozę w świecie (`FrameGraph`), wyraża ją w nowej ramie (`FrameMath.Relative`), prędkość lokalną z prędkości w świecie (prędkość punktu ramy starej i nowej z `ServerFrameHistory`, różnica skończona z dwóch ostatnich ticków), konwertuje do `float` (`TryToFloat`, zakres z `FrameLimits`) i stosuje `EntityReparentOp` przez `ApplyServerOperation`. Reguły EA (walidacja przed operacją, bo applier ich nie zna): gracz → planeta albo budowla (nie układ — P6); budowla tylko w trybie `Ship` → planeta albo układ (nie budowla — statki w statkach poza EA). Fotel przekazywany bez zmian (K5-25 sprawdza fotel w walidatorze intencji). Konsumenci: K5-21 (granica strefy), K5-25 (wsiadanie, wysiadanie, wyskok), K6-37 (teleporty).
8. **Fazy ticka.** `enum ServerTickPhase : byte { Frames = 0, Characters = 1, Compose = 2 }` i domyślna składowa interfejsu `ServerTickPhase IServerTickParticipant.Phase => Characters` (istniejący kod bez zmian). `ServerRuntime.AddTickParticipant` wstawia stabilnie według fazy (ten sam porządek co dziś w obrębie fazy). Ramy i budowle (`Frames`: od K5-24 lot statków, potem K5-21 `ServerZoneTransitions`) → postacie (`Characters`: `ServerCharacters`, `HostConstructs`) → złożenie (`Compose`: `ServerFrameHistory` **pierwszy** w fazie, potem K5-22 `ServerPlanetTerrains`/`ServerShipViewers`, K5-26 `ShipStatusSender` — README „Pakiety K5”, Y7). U klienta ta sama kolejność w `ClientNode`/`LocalPlayer` (krok 13), na końcu interpolacja renderu.
9. **Historia ram.** `FramePoseHistory` (Core/Frames, czysta struktura): stała pojemność ramek (domyślnie 64) × pierścień `HistoryTicks = 31` (500 ms przy 60 Hz + 1), `Record(Tick, FrameRef, in PoseD worldPose)`, `TryGetWorldPose(FrameRef, double tick, out PoseD)` (lerp pozycji, slerp obrotu między tickami; poza oknem → `false`), `Forget(FrameRef)`. Zero alokacji po rozgrzaniu. Serwer: `ServerFrameHistory` (uczestnik fazy `Compose`) zapisuje co tick pozę w świecie każdej budowli `Ship` i usuwa budowle, które zniknęły albo wróciły do `Static`; ramy planet są stałe i idą z grafu. Klient: ta sama struktura karmiona pozami ram z migawek (krok 9).
10. **Budowle `Ship` w migawkach.** `EntityReplication` traktuje budowlę jako ruchomą (kandydat migawki), gdy w chwili budowania rundy `Construct.Mode == Ship`; stan czytany co rundę, więc przełączenie trybu (K5-23) nie wymaga żadnego punktu integracji. `Known` się nie zmienia (pełny stan dalej przez `ConstructState`); przejście `Ship → Static` nie wysyła `EntityDespawn`. Format migawki bez zmian (`EntityKind.Construct` już jest), ale semantyka strumienia się zmienia → podbicie `ProtocolVersion` (D4). Priorytety i zasięg statków w ramie układu — K5-26.
    **Przepięcie budowli na żywo (przegląd K5, B1; README Y8).** Dziś `EntityReplication.ForwardReparent` (`EntityReplication.cs:498-501`) dla budowli woła `ConstructStateSender.Enqueue`, co przestawia łącze w `Pending` i wysyła cały `ConstructState` od nowa (do 256 KiB `Bulk`) przy każdym przepięciu statku. K5-20 zmienia to: łącze odbiorcy `IsLive` → `SendLive(player, OperationApplied { EntityReparentOp })` (klient już to obsługuje: `ConstructMirror.cs:137-140` → `ConstructReplica.TryReparent`); `Enqueue` tylko, gdy łącze nie jest `Live` (transfer w toku, brak kopii). Retransfer pozostaje dla `ResyncRequest`.
11. **Zainteresowanie w ramach budowli.** Pozycja podmiotu i widza liczona w ramie regionu: `FrameGraph.TryGetRegion` + `TryExpress`; byt w ramie budowli dziedziczy region budowli. Wartości zasięgu bez zmian (K3-15).
12. **`ClientFrameGraph`** (`src/Horyzont.Client/Frames/`): `IFramePoseSource` nad `ConstructMirror` (budowle `Static`: poza z nagłówka) i `EntityMirror` (budowle `Ship`: poza interpolowana z migawek na czas renderu) + `IPlanetPlacement` (K5-20: `HomeOnlyPlacement`; K5-21 z `StarSystemState`). `TryGetWorldPose(FrameRef, double renderTick, out PoseD)`. Kliencka `FramePoseHistory` zapisuje pozy ram w świecie po każdej migawce (tick serwera), z niej korzysta `Rebase`.
13. **Przepięcie bez skoku u obserwatora.** W K3-14 `RemoteMotionBuffer` resetuje pierścień przy zmianie rodzica/`ParentSeq` (natychmiastowy skok o błąd interpolacji). K5-20 zastępuje reset wywołaniem `Rebase(FrameRef newFrame, uint newSeq, IFrameHistoryView history)`: każda starsza próbka jest wyrażana w nowej ramie przy swoim ticku (poza ramy starej i nowej z historii klienta), prędkość tak samo; gdy dla którejś próbki brak historii, starsze próbki są usuwane i licznik `ReparentSnaps` rośnie (test akceptacyjny wymaga 0 skoków > 0,5 m, nie 0 snapów). Serwer stosuje przepięcie z dokładną pozą w świecie (decyzja 7), więc złożenie nowej ramy z nową pozą lokalną przy tym samym ticku daje ten sam punkt.
14. **Przepięcie własnego bytu.** `LocalPlayer`/predykcja (K3-14): zmiana własnego `ParentSeq` z `OperationApplied` → przewidziana poza wyrażona w nowej ramie przy ticku operacji (`ClientFrameGraph`), wpisy predykcji starszej ramy odrzucone, dalej zwykła korekta; skok tylko gdy błąd po `Rebase` ≥ 1 m (próg K3-14). Gracz w ramie budowli jest **przyczepiony**: tryb ruchu `Frozen`, bez symulacji własnego wejścia, poza = rama ∘ poza lokalna (fotele K5-25 korzystają z tej samej ścieżki).
15. **Przyczepione ciała na serwerze.** `ServerCharacters` przyjmuje gracza w ramie budowli: ciało kinematyczne, kolizja wyłączona (warstwa i maska 0 — ciało w kadłubie nie może kolidować z lecącym statkiem), co tick w fazie `Characters` ustawiane z `FrameGraph` (rama ∘ poza lokalna); `UpdatePlayerKinematics` nie jest wołane (poza lokalna stała). Gracz w ramie układu — nieobsługiwany (P6).
    **Każde przepięcie gracza (przegląd K5, B2; README Y10).** Dziś `ServerCharacters` ustala planetę ciała raz przy tworzeniu (`ServerCharacters.cs:130-138`) i dalej podaje ją do `UpdatePlayerKinematics` (`:211`, `:337`), nie reagując na `EntityReparentOp`. K5-20 dodaje obsługę zdarzenia `ServerSimulation.Applied` z `EntityReparentOp` własnego gracza, wykonywaną w fazie `Characters` następnego ticka: rama `Construct` → ciało przyczepione (jak wyżej); rama `Planet(p)` i `p` = planeta ciała → powrót do chodzenia z pozą z operacji; rama `Planet(p)` i `p` ≠ planeta ciała → despawn ciała i jego widza, spawn na `p` z pozą z operacji w trybie `Frozen` do `IsAreaLoaded` (ta sama ścieżka co dzisiejszy spawn, w tym odmowa „the planet has no terrain” do czasu aktywacji terenu — K5-22). Konsumenci: K5-22 (`place`), K5-25 (wysiadanie i wyskok na innym ciele niż start).
16. **Scena.** `FrameNodes` (`game/src/Shared/Frames/FrameNodes.cs`, instancja per strona: serwer `/Root/Server/Frames`, klient `/Root/Client/Frames`): `System` → `Planet_<id>` (transformacja = `IPlanetPlacement`) → węzły budowli (`Construct_<id>`) i postaci. `Attach(Node3D node, FrameRef frame)` i `Reparent(Node3D node, FrameRef newFrame)` = `node.Reparent(target, keepGlobalTransform: true)`, odkładane do fazy złożenia (nigdy w trakcie wywołań zwrotnych fizyki). Węzeł terenu planety domowej zostaje tam, gdzie jest (K5-22 przeniesie tereny pod węzły planet); ponieważ planeta domowa stoi w tożsamości, wynik jest bitowo ten sam.
17. **Konwersje w grze.** `GodotFrames` (`game/src/Shared/Frames/GodotFrames.cs`, statyczna, czysta): `PoseD`/`Vector3D`/`QuaternionD` ↔ `Transform3D`/`Vector3`/`Quaternion` Godota (oba w `double`, bez utraty). `PlanetFrames` zostaje jedyną konwersją Godot ↔ `float` ramy planety, ale jej wejście to przestrzeń **lokalna węzła planety**; wywołania z pozycjami globalnymi (`GlobalPosition`, `GlobalTransform`) przechodzą przez `FrameNodes.ToPlanetLocal(planet, Vector3 global)` (krok 12 audytuje wszystkie wywołania; test z planetą przesuniętą o 115 km je łapie). Zero mutowalnego stanu statycznego — stan siedzi w instancji `FrameNodes`.
18. **Przestrzeń ramy.** `IFrameSpace` (`game/src/Shared/Frames/`): `Rid PhysicsSpace`, `Vector3D GravityAt(Vector3D localPosition)`, `bool SupportsWalking`. EA: `SharedWorldFrameSpaces` — dla każdej ramy ta sama przestrzeń świata; planeta: grawitacja jak dziś (radialna, z obecnych reguł); budowla: grawitacja regionu wyrażona w ramie budowli, `SupportsWalking = Mode == Static`; układ: zero (strefy wpływu w K5-21). `PlayerBody.Configure` dostaje `IFrameSpace` zamiast pośredniego założenia „planeta”; `CharacterMotor` i `SurfaceFrame` (chroniony, `PlayerInput.Yaw`) bez zmian.
19. **Protokół.** Brak nowych kodów; `ProtocolVersion` = (main przy scaleniu) + 1 z aktualizacją istniejącego `ProtocolVersion_MatchesDocumentedMergeValue` i `Constants_GoldenValues` (D4). Dziennik, checkpoint, bloby — bez zmian.

### Pliki

```
src/Horyzont.Core/Frames/                 Vector3D, QuaternionD, PoseD, FrameMath, FrameLimits, IPlanetPlacement,
                                          HomeOnlyPlacement, IFramePoseSource, FrameGraph, WorldStateFramePoses,
                                          FramePoseHistory, IFrameHistoryView
src/Horyzont.Core/World/Constructs/Construct.cs          + internal SetPose(Vector3, Quaternion)
src/Horyzont.Core/Simulation/ServerSimulation.cs         + UpdateConstructKinematics
src/Horyzont.Core/Simulation/BlockOverlap.cs             TryGetPose przez FrameGraph (double)
src/Horyzont.Server/ServerTickPhase.cs, IServerTickParticipant.cs (+ Phase), ServerRuntime.cs (wstawianie wg fazy)
src/Horyzont.Server/Frames/               FrameTransitions, FrameTransitionFailure, ServerFrameHistory
src/Horyzont.Server/Replication/EntityReplication.cs     budowle Ship jako kandydaci migawek; region przez FrameGraph
src/Horyzont.Server/Console/ConsoleCommandProcessor.cs   `frame` (tylko --dev)
src/Horyzont.Core/Protocol/ProtocolConstants.cs          ProtocolVersion (D4)
src/Horyzont.Client/Frames/               ClientFrameGraph, ClientFrameHistory
src/Horyzont.Client/... (K3-14)           RemoteMotionBuffer.Rebase; predykcja: rebase własnego bytu, Frozen w ramie budowli
game/src/Shared/Frames/                   GodotFrames, FrameNodes, IFrameSpace, SharedWorldFrameSpaces
game/src/Server/Characters/ServerCharacters.cs           przyczepione ciała, węzły pod FrameNodes
game/src/Client/Replication/RemotePlayers.cs, RemoteConstructs.cs, game/src/Constructs/ConstructNode.cs  węzły pod FrameNodes
game/src/Boot/GameComposition.Frames.cs   własny partial (X10)
game/tests/Frames/                        FarFromOriginTests, FrameNodesTests, FrameSpaceTests
tests/Horyzont.Core.Tests/Frames/, tests/Horyzont.Server.Tests/Frames/, tests/Horyzont.Client.Tests/Frames/
tests/Horyzont.Integration.Tests/Frames/ReparentUnderLatencyTests.cs
tests/Horyzont.Testing/Ships/TestShips.cs  budowla Ship przez WorldStateBuilder (nagłówek Mode = Ship) i skrypt ruchu ramy
```

## Testy akceptacyjne (napisz najpierw)

| Test | Plik | Kategoria | Faza | Co sprawdza |
| --- | --- | --- | --- | --- |
| `FarFromOrigin_CharacterOnBox115km_JitterBelow1mm` | `game/tests/Frames/FarFromOriginTests.cs` | silnik | A | `PlayerBody` na `StaticBody3D` w (115 000, 0, 0), grawitacja radialna wzgl. testowego środka; 600 ticków po ustaleniu: maks. przesunięcie ≤ 1 mm, ciągły kontakt z podłożem |
| `FarFromOrigin_VoxelTerrainAt110km_CharacterStableAndColliding` | jw. | silnik | A | testowa instancja terenu planety domowej (generator v1) z węzłem w (110 000, 0, 0), postać na powierzchni ok. 114 km od początku: drgania ≤ 1 mm, 20 skoków i lądowań bez przenikania, promień w dół zawsze trafia |
| `FarFromOrigin_ConstructCollisionAt115km_CharacterStands` | jw. | silnik | A | budowla 8×1×8 z kolizją chunków K2-12 w 115 km, postać stoi 600 ticków: ≤ 1 mm |
| `FrameMath_ComposeInverse_RoundTripWithin1e9` (FsCheck) / `FrameMath_ToFloat_RejectsNonFiniteAndOutOfRange` | `tests/Horyzont.Core.Tests/Frames/FrameMathTests.cs` | domyślna | A | łączność złożenia, odwrotność, normalizacja obrotu, granice `FrameLimits` |
| `FrameGraph_NestedConstructs_ComposeInDouble` / `FrameGraph_CycleOrMissingFrame_ReturnsFalse` / `FrameGraph_WorldPose_AllocatesZeroBytes` | `…/Frames/FrameGraphTests.cs` | domyślna | A | głębokość 8, cykl, brak ramy, region, 0 B |
| `BlockOverlap_NonNestedPlacement_GoldenCasesUnchanged` | istniejące złote przypadki `ConstructPlacement` | domyślna | A | przejście na `FrameGraph` nie zmienia wyników bez zagnieżdżenia |
| `FramePoseHistory_InterpolatesBetweenTicks_And500msWindow` / `FramePoseHistory_SteadyState_AllocatesZeroBytes` | `…/Frames/FramePoseHistoryTests.cs` | domyślna | A | lerp/slerp, okno 31 ticków, zapomnienie ramy, 10 000 zapisów 0 B |
| `UpdateConstructKinematics_ShipConstruct_StoresPoseOutsideJournal` / `…_StaticOrStaleSeq_ReturnsFalse` / `…_StateHashUnchanged_CheckpointHasPose` | `tests/Horyzont.Core.Tests/Simulation/ConstructKinematicsTests.cs` | domyślna | A | brak wpisu w dzienniku, skrót bez zmian, nagłówek w checkpoincie z nową pozą |
| `ServerRuntime_Participants_RunInPhaseOrder_StableWithinPhase` | `tests/Horyzont.Server.Tests/TickPhaseTests.cs` | domyślna | A | kolejność Frames → Characters → Compose, rejestracja w obrębie fazy |
| `FrameTransitions_PlanetToMovingConstructAndBack_WorldPoseWithin1mm` / `…_ConstructToSystem_…` / `…_RejectsPlayerToSystemAndStaticConstruct` | `tests/Horyzont.Server.Tests/Frames/FrameTransitionsTests.cs` | domyślna | A | poza w świecie przed i po przepięciu ≤ 1 mm, prędkość w świecie ≤ 1 cm/s, reguły EA |
| `ServerFrameHistory_ShipConstructs_Recorded500ms_ZeroAlloc` | `…/Frames/ServerFrameHistoryTests.cs` | domyślna | A | zapis co tick, `Static` pomijana, 0 B |
| `Snapshot_ShipConstruct_IsMobile_StaticIsNot` / `Snapshot_ShipToStatic_NoDespawn` | `tests/Horyzont.Server.Tests/Replication/` | domyślna | B | kandydaci migawek wg trybu czytanego co rundę |
| `Interest_EntityInConstructFrame_UsesRegionPosition` | jw. | domyślna | B | byt w ramie budowli 600 m od widza → poza zasięgiem, 400 m → w zasięgu |
| `Reparent_ConstructLiveLink_SentAsOperation_NoTransfer` / `Reparent_ConstructPendingLink_Enqueued` | jw. | domyślna | B | B1: łącze `Live` → jedna `OperationApplied { EntityReparentOp }`, zero części `ConstructState`, łącze dalej `Live`; łącze w transferze → `Enqueue` |
| `ClientFrameGraph_ShipPoseFromSnapshots_InterpolatedAtRenderTick` | `tests/Horyzont.Client.Tests/Frames/` | domyślna | B | poza ramy z `EntityMirror`, `Static` z `ConstructMirror` |
| `RemoteMotionBuffer_ReparentWithHistory_RebasesWithoutJump` / `…_WithoutHistory_DropsOlderAndCountsSnap` | jw. (obok testów K3-14) | domyślna | C | zastępuje oczekiwanie resetu z K3-14 `Push_StaleTickOrParentChange_DropsOrResetsSamples` (zmiana uzasadniona w PR) |
| `OwnPrediction_Reparent_RebasesIntoNewFrame_FrozenInConstruct` | jw. | domyślna | C | brak skoku ≥ 0,5 m, wejście nie jest symulowane w ramie budowli |
| `Reparent_ThousandRandom_UnderLatency_RemoteObserver_NoJumpOver05m_NoWrongFrame` | `tests/Horyzont.Integration.Tests/Frames/ReparentUnderLatencyTests.cs` | `Slow` | C | patrz „Pomiar” |
| `Reparent_ThousandRandom_UnderLatency_OwnEntity_NoJumpOver05m_NoWrongFrame` | jw. | `Slow` | C | to samo z perspektywy właściciela bytu |
| `FrameNodes_Reparent_KeepsGlobalTransform` / `FrameNodes_PlanetOffset115km_PlanetFramesRoundTrip` | `game/tests/Frames/FrameNodesTests.cs` | silnik | D | transformacja globalna ≤ 1e-9 m; audyt wywołań `PlanetFrames` |
| `FrameSpace_ConstructFrame_GravityOfRegionInConstructAxes` / `ServerCharacters_PlayerInConstructFrame_AttachedAndFollowsFrame` | `game/tests/Frames/FrameSpaceTests.cs` | silnik | D | grawitacja, ciało kinematyczne bez kolizji, poza = rama ∘ lokalna co tick |
| `ServerCharacters_ReparentToOtherPlanet_RespawnsFrozenUntilAreaLoaded` / `…_ConstructToSamePlanet_WalksFromOpPose` | `game/tests/Frames/FrameSpaceTests.cs` | silnik | D | B2: atrapa drugiego terenu (`FakeTerrainService` z dwiema planetami albo testowa instancja terenu); ciało na nowej planecie, `UpdatePlayerKinematics` z nową ramą zwraca `true`; prawdziwy drugi teren testuje K5-22 |

### Pomiar w teście akceptacyjnym (kryterium 2)

- Uprząż: `ServerRuntime` bez silnika (`FakeServerTerrain`), dwóch klientów przez `InMemoryNetwork` (`FakeTimeProvider`), RTT losowane per przebieg z {100, 150, 200, 250} ms ± jitter 10 ms, 5 % strat kanału zawodnego (X11: niezawodny bez strat). Ziarno w nazwie przypadku; 4 przebiegi × 250 przepięć = 1000.
- Ramy testowe: budowla `Ship` (`TestShips`) jadąca po okręgu 30 m/s z obrotem 0,3 rad/s (`UpdateConstructKinematics` z uczestnika fazy `Frames`), druga stoi; planeta domowa; budowla przepinana też planeta ↔ układ.
- Byty testowe: 4 graczy (ruch skryptowy przez `UpdatePlayerKinematics`, bez fizyki) przepinanych losowo co 0,5–2 s przez `FrameTransitions` (planeta ↔ budowla, z fotelem w komórce bloku i bez).
- Skok: dla każdej klatki renderu klienta (60 Hz) `|p_świat(t) − p_świat(t−1)| − v_max·Δt` ≤ 0,5 m, gdzie `v_max` = maks. prędkość punktu z historii serwera; dodatkowo błąd względem pozy serwera przy tym samym ticku renderu ≤ 0,5 m. Raport: maksimum, p99, liczba `ReparentSnaps`, liczba `ResyncRequest`.
- Zła rama: licznik `WrongFrameApplied` w `EntityMirror`/`ClientFrameGraph` (próbka zastosowana, gdy jej `(Frame, ParentSeq)` ≠ znanej) = 0, oraz porównanie z historią serwera: rama i `ParentSeq` bytu u klienta przy ticku t należą do przedziału, w którym serwer miał tę ramę.

## Kroki

Każdy krok: test czerwony → kod → zielone testy → mały commit (Conventional Commits). Wyszukiwanie kodu i uruchamianie testów zlecać subagentowi Haiku (CLAUDE.md projektu). Kroki w Core: przegląd Opus jest wbudowany (prowadzi Opus), ale każdy commit Core ma opis „Core: …” do drugiego przeglądu Sol.

**Faza A (od razu)**

- [ ] 0. Warunki wejścia: `git status`, worktree `F:/dev/horyzont-k5-20` od `origin/main`; zapisać w „Odstępstwach” scalone pakiety (K3/K4, numery PR) i rzeczywiste nazwy z tabeli „Kontekst”; potwierdzić, że `ProtocolVersion`, `OperationApplier`, `EntityMirror` są takie jak w tabeli (różnice → plan aktualizuje wykonawca w „Odstępstwach”, sprzeczność ze specyfikacją → stop i pytanie). Commit `docs:`.
- [ ] 1. **Najpierw ryzyko:** `game/tests/Frames/FarFromOriginTests.cs` — trzy testy `FarFromOrigin_*` (bez nowego kodu produkcyjnego poza testową instancją terenu w scenie testu; grawitacja testowa wzgl. środka testowej planety). Uruchomić w jobie `game` i lokalnie (`python tools/engine/build.py smoke` + runner testów silnika). Niezaliczony → stop, raport (drgania, kontakty) i eskalacja P1 przed krokiem 2. Commit `test:`.
- [ ] 2. `Vector3D`, `QuaternionD`, `PoseD`, `FrameMath`, `FrameLimits` (testy właściwości najpierw). Commity `test:`, `feat:`.
- [ ] 3. `IPlanetPlacement`, `HomeOnlyPlacement`, `IFramePoseSource`, `FrameGraph`, `WorldStateFramePoses`; przepięcie `BlockOverlap.TryGetPose` na `FrameGraph` (złote przypadki bez zmian, zamrożony korpus i `DeterminismTests` bez zmian w plikach). Testy alokacji.
- [ ] 4. `FramePoseHistory` + `IFrameHistoryView` (test alokacji).
- [ ] 5. `Construct.SetPose`, `ServerSimulation.UpdateConstructKinematics`; `tests/Horyzont.Testing/Ships/TestShips.cs` (budowla `Ship` z `WorldStateBuilder`, skrypt ruchu po okręgu). Testy: dziennik pusty, skrót bez zmian, checkpoint z pozą, `Static`/stara rama → `false`.
- [ ] 6. `ServerTickPhase`, `IServerTickParticipant.Phase` (domyślnie `Characters`), wstawianie w `ServerRuntime.AddTickParticipant`; test kolejności. Istniejący uczestnicy bez zmian zachowania.
- [ ] 7. `ServerFrameHistory` (faza `Compose`), `FrameTransitions` (+ `FrameTransitionFailure`), komenda konsoli `frame list|reparent <entity> <system|planet:id|construct:id> [seat x y z]` tylko z `--dev` (linia `FRAME …` w formacie stałym). Testy z tabeli.

**Faza B — po scaleniu K3-15**

- [ ] 8. **[K3-15 — już na `main`]** Rebase; `EntityReplication`: budowle `Ship` jako kandydaci migawek (tryb czytany co rundę), region i pozycja przez `FrameGraph`; `ForwardReparent` budowli na żywo dla łącza `Live` (B1, testy `Reparent_ConstructLiveLink_*`); podbicie `ProtocolVersion` (D4) z aktualizacją istniejących testów; testy replikacji i zainteresowania; test alokacji rundy migawki (jak K3-15) bez regresji.
- [ ] 9. **[K3-15]** `ClientFrameGraph`, `ClientFrameHistory` (karmione po każdej pełnej migawce przez `IFullSnapshotSink`), licznik `WrongFrameApplied` w `EntityMirror`; testy.

**Faza C — po scaleniu K3-14**

- [ ] 10. **[K3-14]** `RemoteMotionBuffer.Rebase` zamiast resetu przy zmianie ramy (zmiana testu K3-14 opisana w PR); predykcja własnego bytu: rebase przy własnym przepięciu, tryb przyczepiony (`Frozen`) w ramie budowli. Właściciel `LocalPlayer.cs` to K3-14 (X10) — po scaleniu K3-14 edycja dozwolona, ograniczona do wstrzyknięcia `ClientFrameGraph` i obsługi przepięcia.
- [ ] 11. **[K3-14, K3-15]** `ReparentUnderLatencyTests` (dwa testy `Slow`, „Pomiar”); raport liczb w PR. Niezaliczony próg = poprawka, nie osłabienie asercji (ZASADY pkt 12 po dwóch próbach).

**Faza D — silnik (po scaleniu K3-14 i K3-15)**

- [ ] 12. `GodotFrames`, `FrameNodes` (serwer i klient), `GameComposition.Frames.cs`; audyt: każde wywołanie `PlanetFrames.*` z wartością globalną przechodzi przez `FrameNodes.ToPlanetLocal` (lista wywołań w PR); testy `FrameNodes_*` z planetą przesuniętą o 115 km.
- [ ] 13. `IFrameSpace` + `SharedWorldFrameSpaces`, `PlayerBody.Configure(IFrameSpace …)`, `ServerCharacters` (przyczepione ciała, obsługa każdego przepięcia gracza wg decyzji 15 — także zmiana planety, testy `ServerCharacters_ReparentToOtherPlanet_*`; przepięcie węzła ciała w fazie złożenia), `RemotePlayers`/`RemoteConstructs`/`ConstructNode` jako dzieci `FrameNodes` (poza `Ship` z `ClientFrameGraph` co klatkę), kolejność klatki klienta (ramy → postacie → złożenie → interpolacja renderu); testy `FrameSpace_*`, `ServerCharacters_PlayerInConstructFrame_*`. Ręcznie (`--dev`): `frame reparent` na sobie do budowli testowej i z powrotem w trybie host + klient, zrzuty do PR.

**Faza E — odbiór (po scaleniu K3-14; K4-19 nie jest warunkiem — P11)**

- [ ] 14. Rebase na `main`; pełne komendy akceptacyjne; dopisać do `docs/ZASADY.md` pkt 2: „świat Godota = rama układu, bez pływającego początku; `Vector3D`/`QuaternionD`/`PoseD` ↔ Godot wyłącznie przez `GodotFrames`; wejście `PlanetFrames` to przestrzeń lokalna węzła planety”, „fazy ticka Frames → Characters → Compose”, „ruch budowli tylko `UpdateConstructKinematics` i tylko w trybie `Ship`”; dopisać wiersz scalenia K5-20 (numer PR, wartość `ProtocolVersion`) w istniejącej sekcji „Pakiety K5” `docs/plans/README.md` (sekcję i reguły Y1–Y15 dodał przegląd K5); PR z odstępstwami i odpowiedziami na pytania.

## Odbiór

```
dotnet build Horyzont.sln
dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"
dotnet format Horyzont.sln --verify-no-changes
python tools/engine/build.py smoke
```

Plus przebieg testów silnika `game/tests/Frames/` (job `game`, wynik i liczby drgań w PR) i wynik `Reparent_ThousandRandom_*` (maks., p99, `ReparentSnaps`, `ResyncRequest`) w opisie PR.

## Równoległość z K5-23

Brak krawędzi startu: oba pakiety ruszają od razu. Zasada X10: **integruje pakiet scalany później**. Rekomendowana kolejność scalania: **K5-23 → K5-20** (K5-23 jest mniejszy i nie czeka na K3-14; K5-20 ma fazy C–D zależne od K3-14). Jeśli K5-20 będzie gotowy pierwszy, kolejność się odwraca bez zmian w planach.

| Plik / punkt wspólny | K5-20 | K5-23 | Konflikt i kto rozwiązuje |
| --- | --- | --- | --- |
| `src/Horyzont.Core/World/Constructs/Construct.cs` | `internal SetPose` | `internal SetMode` | sąsiednie składowe; scalany później dopisuje obok |
| `src/Horyzont.Core/Simulation/ServerSimulation.cs` | nowa metoda `UpdateConstructKinematics` | nowy parametr konstruktora (tabela treści bloków) | rozłączne; scalany później rebase |
| `ProtocolConstants.ProtocolVersion` + `ProtocolVersion_MatchesDocumentedMergeValue`, `Constants_GoldenValues` | podbicie (krok 8) | podbicie | D4: scalany później = (main) + 1 i aktualizuje ten sam test |
| `EntityReplication.cs` | budowle `Ship` w migawkach (tryb co rundę) | nie edytuje | brak; po scaleniu obu `ShipModeSetOp` automatycznie przełącza migawki |
| `ConstructReplica`/`ConstructMirror` | czyta `Mode` | stosuje `ShipModeSetOp` | brak edycji wspólnych linii |
| `tests/Horyzont.Testing/Ships/` | `TestShips` (nagłówek `Mode = Ship`) | `ShipFixtures` (30 bloków, treść) | różne pliki; po scaleniu obu można przejść na `ShipModeSetOp` (opcjonalnie) |
| `docs/ZASADY.md`, `docs/plans/README.md` | wiersze K5-20 | wiersze K5-23 | scalany później dopisuje obok |
| `GameComposition` | `GameComposition.Frames.cs` | `GameComposition.Ships.cs` | osobne partiale |

## Ryzyka

| Ryzyko | Skutek | Odpowiedź |
| --- | --- | --- |
| Jolt/Voxel Tools niestabilne ok. 110 km mimo `double` | cała architektura K5 bez pływającego początku | krok 1 jako pierwszy; stop i eskalacja P1 |
| `Node.Reparent` ciała fizycznego usuwa i dodaje je do przestrzeni | jednoklatkowa utrata kontaktu | przepięcie tylko w fazie złożenia; przyczepione ciała bez kolizji; test `FrameNodes_*` z ciałem na podłożu |
| Historia klienta nie pokrywa starszych próbek przy dużym RTT | snapy > 0,5 m | historia z pozycją ram na ticki serwera z migawek (nie z renderu); okno klienta = bufor interpolacji + 250 ms; licznik `ReparentSnaps` w raporcie |
| Wywołania `PlanetFrames` z pozycjami globalnymi rozsiane po K2/K3 | błędy dopiero przy K5-21, gdy planety się przesuną | audyt w kroku 12 i test z planetą w 115 km |
| `float` w ramie układu (≤ 3,1 cm przy 500 km) | drgania statku w kosmosie | render i fizyka w `double` (Godot); `float` tylko na drutach i w zapisie; P2 |
| Zmiana testu K3-14 (reset → rebase) | spór w przeglądzie | uzasadnienie w PR: spec wymaga braku skoków > 0,5 m przy przepięciu |

## Pytania do właściciela (wariant domyślny obowiązuje bez odpowiedzi)

| # | Pytanie | Wariant domyślny |
| --- | --- | --- |
| P1 | Co, jeśli test Jolt/terenu daleko od początku nie przejdzie? | stop pakietu, raport, decyzja Opus + właściciel; plan B: pływający początek per region (świat Godota = rama aktywnej planety), co przesuwa K5-21/22 |
| P2 | Zakres `float` w ramie układu? | 500 km (rozdzielczość ≤ 3,1 cm); planeta i budowla 100 km |
| P3 | Długość historii ram? | 500 ms = 31 ticków przy 60 Hz, pojemność 64 ramy (ok. 110 KB) |
| P4 | Zasięg zainteresowania dla bytów w ramach budowli? | region i pozycja budowli; wartości K3-15 bez zmian; zasięg 10 km statków w układzie — K5-26 |
| P5 | Definicja „skoku” w teście? | przyrost pozy w świecie między klatkami renderu ponad `v_max·Δt` > 0,5 m albo błąd względem serwera > 0,5 m |
| P6 | Gracz w ramie układu (EVA)? | nieobsługiwany w EA: `FrameTransitions` odmawia; wyskok (K5-25) działa tylko w strefie planety |
| P7 | Chodzenie po zaparkowanym statku? | gracz zostaje w ramie planety (statek nieruchomy); przyczepienie do ramy budowli tylko przez przepięcie (fotel, testy) |
| P8 | Źródło bytów testowych? | skrypt serwera w testach + komenda `frame` tylko z `--dev` |
| P9 | Podbijać `ProtocolVersion` bez nowych kodów? | tak (budowle `Ship` w migawkach zmieniają semantykę strumienia) |
| P10 | Obrót planet? | pole pozy w `IPlanetPlacement`, w EA tożsamość |
| P11 | Czy scalenie K5-20 czeka na K4-19? | nie — K4-19 jest pakietem odbiorczym, a testy K5-20 go nie używają; czeka tylko na K3-14 i K3-15. Alternatywa: scalenie po K4-19 (dłuższa ścieżka krytyczna całego K5) |

## Pułapki

- `FrameMath.TryToFloat` to jedyne rzutowanie pozy `double` → `float`; nigdzie indziej nie skracać `Vector3D` do `Vector3`.
- Nie używać `GlobalPosition` z `PlanetFrames` bez `FrameNodes.ToPlanetLocal` — dziś działa, bo planeta stoi w tożsamości, po K5-21 przestanie.
- `UpdateConstructKinematics` nie może ruszać budowli `Static` (poza `Static` jest w skrócie stanu i w dzienniku).
- Przejście `Ship → Static` nie despawnuje budowli u klienta — znika tylko z migawek.
- Przepięcie węzłów Godota wyłącznie w fazie złożenia, nigdy w `_PhysicsProcess` innego węzła ani w sygnale kolizji.
- Testy domyślne i `Slow` w .NET tylko na `FakeTimeProvider`; testy silnika mierzą klatki (X11).
- `SurfaceFrame` (konwencja `Yaw`) jest chroniony: rama budowli nie zmienia znaczenia `PlayerInput.Yaw`; przyczepiony gracz nie wysyła ruchu.

## Odstępstwa i decyzje wykonawcy

(wypełnia wykonawca: stan K3/K4 przy każdej fazie, rzeczywiste nazwy API, wyniki kroku 1 i testu 1000 przepięć, odpowiedzi właściciela, różnice od planu)

**Krok 0 (2026-10-10, fazy A–B).**

- Gałąź `pakiet/k5-20-ramy-odniesienia` (nie `pakiet/k5-20-ramy`), worktree `F:/dev/horyzont-k5-20`, od `plany/k5` (44dad6e) nad `main` a711512. Scalone: K2 (#11–#18), K3-00 (#19), K3-15 (#20). K3-14 **nie** jest scalony → fazy C–D czekają. K4-19 nie scalony (nie jest warunkiem — P11).
- `ProtocolConstants.ProtocolVersion = 4` (K3-15), zgodnie z Y3.
- `OperationApplier`: `ApplyReparent` (:127), `CheckReparent` (:294), `IsPose` (:392, `internal static`) — jak w tabeli.
- `ServerSimulation.ApplyServerOperation` (:138), `UpdatePlayerKinematics` (:235) — jak w tabeli.
- `EntityMirror.MaxFrameDepth = 8` (`private const`, :19); `EntityReplication.ForwardReparent` jest w :491 (plan: :498-501), lista ruchomych bytów to `PlayerState.Mobile`.
- `BlockOverlap`: nie ma publicznego `TryGetPose`; jest prywatne rekurencyjne `TryGetPose(store, construct, frame, depth, …)` nad `ConstructStore` (głębokość `MaxNestingDepth = 64`) i publiczne `TryGetConstructPose`. Krok 3 zostawia ścieżkę głębokości 1 bez zmian, a głębsze składa w `double` przez `FrameGraph` z zachowaniem limitu 64 (bez zmiany zachowania walidatora dla głębokich zagnieżdżeń).
- Krok 1: `PlayerBody` działa w ramie planety przez `PlanetFrames` (planeta w tożsamości, `MaxCoordinate = 100 km`, stan zaokrąglany do `float` co tick), więc nie może stać 115 km od początku przed fazą D (wejście `PlanetFrames` = przestrzeń lokalna węzła planety). Testy `FarFromOrigin_*` używają testowego `CharacterBody3D` z tą samą konfiguracją co `PlayerBody` (kapsuła `GameRules`, `SafeMargin`, przyciąganie do podłoża, docisk 0,05 m/s), ruch w `double` z grawitacją względem testowego środka. Pełna ścieżka `PlayerBody` na planecie przesuniętej o 115 km — testy `FrameNodes_*` (krok 12).
- Krok 1 — walidacja: testów silnika nie uruchamia się lokalnie (wspólny runner, jeden potok gdUnit); `ci.yml` rusza tylko dla PR i `main`, więc wynik kroku 1 przyjdzie z jobu `game` po otwarciu PR. Do tego czasu kryterium 1 jest **niezweryfikowane**; fazy A–B idą dalej na polecenie prowadzącego (P1 obowiązuje, gdy job `game` pokaże porażkę).
