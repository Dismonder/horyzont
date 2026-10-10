# K5-25 · Fotele i sterowanie

| | |
| --- | --- |
| Prowadzi | GPT-6.1 Sol (Codex, effort medium; high w krokach 1 i 9); wariant zapasowy Opus 5.5 (`claude --model opus`), gdy Sol jest niedostępny albo po dwóch nieudanych podejściach do tego samego testu (ZASADY pkt 12) |
| Testy akceptacyjne | GPT-6.1 Sol; kontrakty Core (intencje 11, 12, 16, odmowy 32–35 i 38, komunikaty 30 i 34, `ShipLaunchBlock.PersonInHull = 2`, `MovementMode.Gliding = 6`, `SeatPose`, `SeatExitPlanner`, pomocnik Y9) i złote bajty zatwierdza Opus 5.5 **przed kodem** (krok 1) |
| Przegląd | Opus 5.5 w PR (ZASADY pkt 9: pakiet Sol → Opus; zmiany w `src/Horyzont.Core/` — Opus w świeżej sesji, ZASADY pkt 3) |
| Zależy od | K5-24 scalony na `main` (spec 02:250). Pośrednio K5-20 (`FrameTransitions`, `FrameGraph`, `ServerCharacters` Y10, `Frozen` w ramie budowli), K5-21 (`StarSystemModel`, `ClientStarSystem`), K5-23 (`SeatRole`, `ShipSeat`, `ShipStats.Seats`, `ShipModeIntent`), K5-24 (`ShipControl`, `IShipControlSource`, `ShipFlightModel.Step`, `FlightEnvironment.LaunchBlock`, `ShipHullBoxes`, `ShipFlightSystem`). **Nie** zależy od K5-26 (brak krawędzi startu; rekomendowane scalanie K5-26 → K5-25 — sekcja „Kody i punkty styku”) ani od K5-27 (stanowisko nawigatora woła `IMapOpener`, jeśli scalony) |
| Gałąź | `pakiet/k5-25-fotele` (worktree `F:/dev/horyzont-k5-25`) |
| Kamień milowy | K5 Kosmos; odbiór wg spec 02:250: „1000 wsiadań i wyskoków bez skoków ponad 0,5 m; korekty pilota poniżej 0,5 m w 95%” |
| Równolegle | K5-26 (po K5-24), K5-22, K5-27; po K5-25 rusza K5-28 |

**Cel:** dać graczom miejsca w statku i stery — wsiadanie, wysiadanie, wyskok i zwalnianie fotela gracza offline jako intencje (11, 12, 16) zamieniane na `EntityReparentOp`, blokadę startu przez osoby w obrysie kadłuba (`PersonInHull`), wejście pilota `ShipInputBatch` (30) przetwarzane jedno na tick jako implementację `IShipControlSource`, predykcję pilota tym samym `ShipFlightModel.Step` z korektą `ShipCorrection` (34) oraz walidację intencji gracza siedzącego w ramie budowli (Y9) — tak, żeby 1000 losowych wsiadań, wysiadań i wyskoków przy RTT 100–250 ms i 5 % strat nie dało skoku ponad 0,5 m ani stanu w złej ramie, a korekty pilota przy RTT 100 ms były poniżej 0,5 m w 95 % przypadków.

## Kontekst

- `docs/ZASADY.md`: pkt 2 (każda zmiana świata to operacja; ruch statku poza dziennikiem — K5-24), 3 (`Operations/`, `Intents/`, `Protocol/`, `Serialization/`, `Motion/` → `ProtocolVersion`; walidator i `GameRules` nie są ścieżką formatu; kody tylko z zakresu Kosmosu), 4 (applier nie czyta treści ani terenu — pozę fotela i punkt wyjścia liczy resolver i niesie operacja), 6 (tick i runda replikacji bez alokacji), 7 (`[LoggerMessage]`), 8 (`FakeTimeProvider`, `InMemoryTransport`), 12.
- `docs/spec/02-kosmos.md`: :60–61 (kokpit, fotel, stanowisko nawigatora), :82–88 (start tylko bez osób w obrysie — gra sadza je albo blokuje start z komunikatem; role foteli; wyskok poniżej 30 m/s i 200 m, dziedziczy prędkość, kombinezon do 6 m/s; bez pilota autolądowanie — K5-24), :108–111 (postać w fotelu = rama statku + komórka fotela), :139 (poza siedzącego wynika z bloku), :148 (`EntityReparentOp` 7: fotel wolny i w tej budowli), :154 (intencje 11, 12, 16; `SeatEvictIntent` → punkt odrodzenia = miejsce startu świata do K6-35; walidacja odległości od fotela), :156 (odmowy 32–35, 38; `ConstructNotEmpty` także dla gracza offline w fotelu), :162–167 (`ShipInputBatch` 30: do 5 wejść z powtórzeniem trzech ostatnich, osie `sbyte`, przyciski; jedno wejście na tick; tylko od gracza w kokpicie, inaczej `NotPilot`; `ShipCorrection` 34 z każdą migawką tylko do pilota; korekty < 5 m wygładzane 100–200 ms, większe skokiem; kamera pasażera), :177 (`ShipInputBatch` w `InGame`), :192 (gracz zapisany w fotelu wraca w nim), :202–204 (utrata pilota, pasażera, zwalnianie fotela), :210 (rozjazd > 5 m → skok i log z ostatnimi wejściami), :225–226, :229 (testy), :250 (odbiór), :265 (predykcja przy lądowaniu).
- `docs/plans/README.md` „Pakiety K5”: Y2 (kody), Y3 (D4: K5-25 → planowo 14), Y4 (router 30, 34), Y5 (`ShipCorrection` w `ISnapshotBudget`; K→S ≤ 4 KB/s), Y6, Y8, **Y9 (właściciel pomocnika: K5-25)**, Y10, Y12; X5, X6, X10, X11.
- `docs/plans/K5-przeglad.md` „Pokrycie K5-25 i K5-28” — lista obowiązkowa (każdy punkt ma w tym planie decyzję i test).
- `docs/plans/K5-20-ramy-odniesienia.md`: decyzje 7 (`FrameTransitions.TryReparent(…, seat, …)` — zachowuje pozę w świecie), 11, 13–15 (rebase, `Frozen` w ramie budowli, ciało przyczepione, Y10), P6 (gracz w ramie układu nieobsługiwany), „Pomiar w teście akceptacyjnym” (miary skoku i złej ramy).
- `docs/plans/K5-23-bloki-statku.md`: decyzje 1 (`BlockFunctionKind.Cockpit`/`Seat`, `SeatRole`), 3 (`ShipStats.Seats` posortowane po `CellPos`), 5 + N11 (walidator `ShipModeIntent`, Y9), 11 (klawisz G).
- `docs/plans/K5-24-model-lotu.md`: decyzje 1 (`ShipControl`, przyciski = stany), 2 (`ControlFrame` = pierwszy kokpit), 4–7 (`ShipMotion`, `FlightEnvironment` z `LaunchBlock`, `Step`, autolądowanie), 8 (`ShipHullBoxes`), 10 (`ShipFlightSystem`, `IShipControlSource.TryGetControl`), 12 (`ShipLaunchBlock`, `ShipFlightStatus`), 17 (bramka), 19 (`DevControlSource`), 20 (`FlightHud`, `IClientShipStatus`), P9.
- `docs/plans/K5-26-replikacja-statkow.md`: decyzje 4 (migawki dopiero na żywo), 6 (własny łańcuch w każdej migawce), 7 (`KindData` gracza = fotel, `EntityMirror.TryGetSeat`), 9–10 (`ShipStatus`, `ClientShipStatus`).
- `docs/plans/K5-27-mapa-i-nawigacja.md`: `IMapOpener.Open()` dla stanowiska nawigatora.

Kod, na którym pakiet buduje (zweryfikowany wyrywkowo w `F:/dev/horyzont-k5-20` i `F:/dev/horyzont-k5-23` — gałęzie w toku; nazwy K5-21/K5-24/K5-26 z ich planów; krok 0 potwierdza na `main`):

| Co | Gdzie |
| --- | --- |
| `EntityReparentOp(Entity, NewParent, NewParentSeq, LocalPosition, LocalRotation, LocalVelocity, SeatCell?)` — `LocalVelocity` „początkowa prędkość dla kontrolera gry, Core jej nie przechowuje”; applier ustawia `PlayerState.Frame/ParentSeq/Position/BodyRotation/Seat`; `CheckReparent`: seq + 1, rama istnieje, fotel tylko gracz → budowla, komórka niepusta, fotel nie zajęty przez innego gracza | `src/Horyzont.Core/Operations/EntityReparentOp.cs`, `src/Horyzont.Core/Simulation/OperationApplier.cs:127-142, 294-352` |
| `PlayerLeaveOp(Player, Frame, ParentSeq, Position, BodyRotation, Pitch)` — nie czyści `Seat`; `PreparePlayerJoin` zwraca znaną ramę, `ParentSeq` i pozę; nowy gracz → `SpawnPoint(State.Planets.All[0])` (prywatne) | `src/Horyzont.Core/Operations/PlayerLeaveOp.cs`, `Simulation/OperationApplier.cs:76-83`, `Simulation/ServerSimulation.cs:110-133, 338-348` |
| `WorldState.TryGetSeated(construct, cell, out PlayerState)`, `HasChildren(FrameRef)` (internal) | `src/Horyzont.Core/World/WorldState.cs:326, 350` |
| Walidator usuwania bloku: siedzisko zajęte albo ostatni blok ramy z dziećmi → dziś `IntersectsPlayer` | `src/Horyzont.Core/Simulation/IntentValidator.cs:330-335`; testy `tests/Horyzont.Core.Tests/Simulation/ReparentTests.cs:82, 95` |
| `FrameTransitions.TryReparent(EntityId, FrameRef, CellPos? seat, [Vector3 velocityInFrame,] out AppliedOperation?, out FrameTransitionFailure)` — operacja serwera, zachowuje pozę w świecie; `FrameTransitionFailure` 0–9 | `src/Horyzont.Server/Frames/FrameTransitions.cs:46-60`, `FrameTransitionFailure.cs` (gałąź K5-20) |
| `SeatRole : byte { None, Pilot, Passenger, Navigator }`, `ShipSeat(CellPos Cell, SeatRole Role)`, `ShipStats.Seats` (kokpity jako `Pilot` + fotele, sortowane po komórce) | `src/Horyzont.Core/World/Blocks/SeatRole.cs`, `src/Horyzont.Core/Ships/ShipSeat.cs`, `ShipStats.cs:19-23` (gałąź K5-23) |
| `InputBatch(FirstSeq, Inputs)`, `PlayerInput`, `InputBuffer` (internal, posortowany, jedno wejście na tick), `ServerSession.TryTakeInput`, obsługa `case InputBatch` | `src/Horyzont.Core/Protocol/Messages/InputBatch.cs`, `src/Horyzont.Net/Session/InputBuffer.cs`, `ServerSession.cs:230, 513-515` |
| `InputSequencer`, `PredictionBuffer`, `CorrectionPolicy`, `OwnStateValidator` (wzorzec predykcji postaci K3-14) | `src/Horyzont.Client/Movement/` |
| `CharacterMotor.Step` (`MovementRules.MaxFallSpeed`, `MovementMode` 0–5, `Frozen` = bez ruchu) | `src/Horyzont.Core/Motion/CharacterMotor.cs:21-90`, `MovementMode.cs` |
| `MessageType` 1–21 (+ 31–33 z K5-21/26/27), `MessageChannels` (lista zawodnych), `MessageCodec`, `MessageRouter.IsGameMessageFromClient` | `src/Horyzont.Core/Protocol/`, `src/Horyzont.Core/Serialization/MessageCodec.cs`, `src/Horyzont.Net/Session/MessageRouter.cs:40` |
| `ISnapshotBudget.Debit(PlayerId, int)`, `EntityReplication`, `SnapshotBuilder` | `src/Horyzont.Server/Replication/` |
| K5-24: `ShipControl`, `ShipButtons`, `FlightRules`, `ShipMotion`, `FlightEnvironment`, `ShipFlightModel`, `AutolandController`, `ShipHullBoxes`, `ShipLaunchBlock`, `ShipFlightStatus`, `ShipFlightSystem`, `IShipControlSource`, `NoPilotControlSource`, `DevControlSource`, `IShipCollider`, `GodotShipCollider`, `FlightHud`, `IClientShipStatus` | `src/Horyzont.Core/Ships/Flight/`, `src/Horyzont.Core/Ships/`, `src/Horyzont.Server/Ships/`, `src/Horyzont.Client/Ships/`, `game/src/Ships/`, `game/src/Client/Ships/` |

## Zakres

1. **Core — fotele:** `SeatEnterIntent` (11), `SeatExitIntent` (12, w locie = wyskok), `SeatEvictIntent` (16); odmowy `SeatOccupied` (32), `SeatOutOfReach` (33), `EjectTooFast` (34), `NotPilot` (35), `ConstructNotEmpty` (38); `SeatPose`, `SeatExitPlanner`, `SeatRules` (dane w `GameRules`), `RespawnPoints`; pomocnik Y9 `IntentActorFrames`.
2. **Core — protokół:** `ShipInputBatch` (30) z `ShipInput`, `ShipCorrection` (34); `ShipLaunchBlock.PersonInHull = 2`; `MovementMode.Gliding = 6` i opadanie kombinezonu w `CharacterMotor`; `ProtocolVersion` (D4).
3. **Net:** bufor wejść statku w sesji serwera (`TryTakeShipInput`), router (Y4), kanały zawodne.
4. **Serwer:** `PilotControlSource : IShipControlSource` (jedno wejście na tick, `NotPilot`), `ShipLaunchGuard` (osoby w obrysie: posadzenie albo `PersonInHull`), `ShipCorrectionSender` (z migawką pilota, w `ISnapshotBudget`), rozszerzenia `ShipFlightSystem` (`TryGetPilotState`), `ServerCharacters` (szybowanie po wyskoku), komendy `seat`.
5. **Klient:** `ShipInputSequencer`, `ShipPrediction` (bufor wejść, rewind, wygładzanie), `ClientFlightEnvironment`, kamera fotela, sterowanie pilota, interakcja z fotelem, stanowisko nawigatora (`IMapOpener`), teksty odmów i blokady startu.
6. **Testy:** akceptacyjne 1000 przepięć foteli pod opóźnieniem i predykcji pilota przy RTT 100 ms (`Slow`), jednostkowe Core/Net/Server/Client, silnika, ręczny.

## Poza zakresem

- Zasięg statków, kolejność rodzic–dziecko, `KindData` z fotelem, `ShipStatus` (K5-26; K5-25 używa ich, a gdy K5-26 nie jest scalony — patrz „Kody i punkty styku”).
- Chodzenie po lecącym statku, EVA w ramie układu (K5-20 P6), obrażenia od upadku (podprojekt 3), łóżko jako punkt odrodzenia (K6-35 — podmienia `RespawnPoints`), skaner złóż (podprojekt 3).
- Kompensacja opóźnień (K7-41), zmiana modelu lotu (K5-24 — K5-25 tylko go woła).

## Fazy i zależność od niescalonych pakietów

| Faza | Kroki | Wymaga scalonych | Co daje |
| --- | --- | --- | --- |
| A | 0–5 | K5-24 | kontrakty, kodeki, D4, intencje foteli, Y9, `ConstructNotEmpty`, szybowanie |
| B | 6–9 | K5-24 | wejście pilota na serwerze, blokada startu, korekty, predykcja klienta, test predykcji |
| C | 10–12 | K5-24 (K5-26 i K5-27 opcjonalnie) | gra (sterowanie, kamera, HUD, nawigator), test 1000 przepięć, silnik, odbiór |

## Mapa kryteriów → dowód

| # | Kryterium (źródło) | Dowód | Kategoria / job | Faza |
| --- | --- | --- | --- | --- |
| 1 | 1000 losowych wsiadań, wysiadań i wyskoków przy RTT 100–250 ms i 5 % strat: żaden skok > 0,5 m, żaden stan w złej ramie (02:225, :250) | `Seats_ThousandRandomEnterExitEject_UnderLatency_NoJumpOver05m_NoWrongFrame` (patrz „Pomiar 1”) | `Slow` / `ci.yml` | C |
| 2 | Predykcja pilota przy RTT 100 ms: stery odpowiadają od razu, korekty < 0,5 m w 95 % (02:226, :250) | `PilotPrediction_Rtt100_RespondsSameTick_CorrectionsBelow05mIn95Percent` (patrz „Pomiar 2”) | `Slow` / `ci.yml` | B |
| 3 | Osoba w obrysie kadłuba: posadzona na wolnym miejscu albo start zablokowany z komunikatem (02:84, :229) | `ShipLaunchGuard_PersonInHull_SeatedOnFreeSeat` / `…_NoFreeSeat_BlocksPersonInHull` / `…_PersonLeaves_LaunchAllowed` + `FlightHud_LaunchBlockPersonInHull_ShowsMessage` | domyślna | B, C |
| 4 | Wyskok tylko < 30 m/s i < 200 m nad terenem, dziedziczy prędkość, opadanie ≤ 6 m/s (02:86) | `SeatExitIntent_InFlight_*` (5 testów) + `CharacterMotor_Gliding_FallSpeedClampedTo6` + `Eject_HomeTerrain_LandsGliding` (silnik) | domyślna + silnik | A, C |
| 5 | Fotel wolny i w tej budowli; odległość od fotela; role foteli (02:85, :148, :154) | `SeatEnterIntent_*` (8 testów) | domyślna | A |
| 6 | Gracz offline w fotelu: zostaje po restarcie, blokuje rozbiórkę (`ConstructNotEmpty`), zwolnienie → punkt odrodzenia (02:156, :192, :203–204) | `SeatEvictIntent_*` (5) + `ConstructNotEmpty_*` (3) + `Rejoin_SeatedPlayer_SameSeat` / `Rejoin_Evicted_AtWorldStart` | domyślna | A |
| 7 | Wejście statku tylko od gracza w kokpicie, jedno na tick; inne → `NotPilot` (02:165) | `PilotControlSource_*` (6 testów) + `ShipModeIntent_SeatedNonPilot_RejectedNotPilot` | domyślna | B |
| 8 | Utrata pilota → autolądowanie; pasażerowie zostają w fotelach (02:202–203) | `PilotDisconnect_ShipAutolandsAndParks_PassengersStaySeated` | domyślna | B |
| 9 | Rozjazd > 5 m → skok i log z ostatnimi wejściami; < 5 m wygładzanie 100–200 ms (02:166, :210) | `ShipPrediction_ErrorBelow5m_SmoothedOver150ms` / `…_ErrorOver5m_SnapsAndLogsLastInputs` | domyślna | B |
| 10 | Pilot w kokpicie przełącza tryb i tankuje; intencje w ramie budowli (Y9, N11) | `IntentActorFrames_*` (4) + `ShipModeIntent_PilotInCockpit_ToStaticAfterLanding_Accepted` + `RefuelIntent_PilotInCockpit_Accepted` | domyślna | A |
| 11 | Ruch K→S ≤ 4 KB/s; `ShipCorrection` w budżecie migawek (Y5) | `ShipInputBatch_SteadyState_Below4KBps` + `ShipCorrectionSender_DebitsSnapshotBudget` | domyślna | B |
| 12 | Przetwarzanie wejść i runda korekt bez alokacji (ZASADY pkt 6) | `PilotControlSource_TickAllocatesZero` / `ShipPrediction_StepAllocatesZero` / `ShipCorrectionSender_RoundAllocatesZero` | domyślna | B |

## Projekt

### Decyzje

1. **Intencje foteli (Core, `Intents/`).** Rekordy w wersji 1 kodeka intencji:
   - `SeatEnterIntent(EntityId Construct, CellPos SeatCell, Vector3 RayOrigin, Vector3 RayDirection)`, kod 11;
   - `SeatExitIntent(EntityId Construct)`, kod 12 (budowla, w której gracz siedzi — chroni przed opóźnioną intencją po przesiadce);
   - `SeatEvictIntent(EntityId Construct, CellPos SeatCell, Vector3 RayOrigin, Vector3 RayDirection)`, kod 16.
   Układ bajtów: `[Construct u64][Cell 3×i16 jak w CellPos istniejących intencji][RayOrigin 3×f32][RayDirection 3×f32]` dla 11 i 16, `[Construct u64]` dla 12; dekodowanie odrzuca NaN/∞ i komórki spoza zakresu (`ProtocolViolationException`). Każda intencja daje dokładnie jedną `EntityReparentOp` (kod 7, format bez zmian).
2. **Poza fotela** (`SeatPose`, `src/Horyzont.Core/Ships/`, czyste): dla komórki fotela i jej `BlockOrientation`: stopy = środek dolnej ściany komórki w osiach bloku (`cell + 0,5 − 0,5·Up_bloku`) + `SeatRules.FeetLift = 0,05` m wzdłuż `Up_bloku`; obrót = `BlockOrientation.ToQuaternion` (przód postaci = `Forward` bloku). Oko siedzącego = stopy + `Up_bloku · SeatRules.SeatedEyeHeight` (1,2 m). Ten sam kod liczy serwer (resolver), posadzenie automatyczne (decyzja 9) i klient (kamera, decyzja 18). Wynik niesie `EntityReparentOp` — applier niczego nie przelicza (ZASADY pkt 4).
3. **Pomocnik Y9** (`IntentActorFrames`, `src/Horyzont.Core/Simulation/`, czysty nad `WorldState` + `FrameGraph`): `RejectReason TryResolve(PlayerState actor, FrameRef target, out ActorView view)`, gdzie `ActorView(PlanetId Region, Vector3 EyeInTarget, bool Seated, EntityId SeatConstruct, CellPos SeatCell)`. Region aktora = `FrameGraph.TryGetRegion(actor.Frame)`, region celu tak samo — różne → `WrongPlanet`; region `System` → `WrongPlanet` (P6). Oko: gracz w ramie planety — dzisiejsze `PlayerState.EyePosition`; siedzący — oko z decyzji 2; wyrażone w ramie celu przez `FrameGraph.TryExpress`. Brak pozy ramy → `AreaNotLoaded`. Używają go: `ShipModeIntent` (K5-23), `RefuelIntent` (K5-24), intencje foteli i istniejące intencje bloków na budowlach (zastępuje warunek „gracz w ramie planety budowli”). Siedzący gracz **nie** edytuje terenu ani bloków (`InvalidParameters`, P5) — intencje 1–3 sprawdzają `view.Seated` na początku. Testy pilota w kokpicie (`ShipModeIntent` na `Static` po lądowaniu, `RefuelIntent`) są w tym pakiecie (README Y9). Dla siedzącego w kokpicie, którego `SeatCell == CockpitCell` celu, zasięg i linia wzroku do kokpitu są spełnione z definicji (siedzi w nim); do stacji tankowania — liczone od oka siedzącego.
4. **`NotPilot` (35) w walidatorze.** Gracz siedzący w budowli `B` może wysłać `ShipModeIntent`/`RefuelIntent` dotyczące `B` tylko jako pilot (decyzja 10); inny siedzący w `B` → `NotPilot`. Gracze w ramie planety — bez zmian względem K5-23/K5-24.
5. **Walidator `SeatEnterIntent`** (kolejność, każda odmowa bez zmiany stanu): budowla istnieje (`UnknownConstruct`); pomocnik Y9 (`WrongPlanet`, `AreaNotLoaded`); aktor nie siedzi albo siedzi w tej samej budowli (przesiadka; siedzący w innej → `InvalidParameters`); komórka w zakresie i zajęta (`CellOutOfBounds`, `CellEmpty`); blok ma funkcję `Cockpit` albo `Seat` (`InvalidParameters`); fotel wolny — także od gracza offline (`SeatOccupied`); początek promienia = oko serwera (`RayOriginMismatch`); odległość oka od środka komórki fotela ≤ `SeatRules.ReachMeters = 3` (`SeatOutOfReach`); linia wzroku do komórki (`NoLineOfSight`, wykluczona sama komórka fotela); budowla `Static` albo `Ship` z `Parked` albo `|v| ≤ FlightRules.ParkSpeed` z `ShipStateRecord` (`ShipNotParked`, 39 z K5-24 — wsiadanie do lecącego statku jest niemożliwe); kubełek bloków (`RateLimited`). Przesiadka w obrębie budowli: te same warunki, ale zasięg liczony od oka siedzącego. Resolver: `EntityReparentOp(actor.Entity, Construct(B), ParentSeq + 1, SeatPose…, LocalVelocity = 0, SeatCell)`.
6. **Walidator `SeatExitIntent`**: aktor siedzi w `Construct` z intencji (`InvalidParameters`); region budowli = planeta (rama układu → `EjectTooFast`, P6); stan budowli:
   - **wysiadanie** — `Static` albo `Ship` z `Parked`: punkt wyjścia z `SeatExitPlanner` (decyzja 7); brak punktu → `CellOccupied`;
   - **wyskok** — `Ship` bez `Parked`: `|v_statku| ≤ SeatRules.EjectMaxSpeed = 30` m/s i wysokość nad terenem ≤ `SeatRules.EjectMaxHeight = 200` m (promień terenu w dół od środka statku w ramie planety, `ITerrainService.Raycast` z zasięgiem 200 m; brak trafienia albo teren niewczytany → za wysoko), inaczej `EjectTooFast`; punkt z `SeatExitPlanner` w trybie wyskoku; prędkość = `v + ω × r` statku w punkcie wyjścia, wyrażona w ramie planety (`ShipStateRecord`, osie budowli → rama rodzica).
   Resolver: `EntityReparentOp(actor.Entity, Planet(region), ParentSeq + 1, punkt, obrót „pionowo” wg `SurfaceFrame` z kierunkiem przodu fotela, LocalVelocity = 0 przy wysiadaniu albo prędkość wyskoku, SeatCell = null)`. Planeta celu może różnić się od planety ciała — Y10 (K5-20) robi respawn ciała.
7. **Punkt wyjścia** (`SeatExitPlanner`, Core/Ships, czysty nad `Construct` + funkcją „grunt w punkcie”): kandydaci w osiach fotela: komórka przed fotelem (`Forward`), za, w lewo, w prawo, nad; pierwszy, którego komórka i komórka nad nią (wzdłuż `Up` regionu zaokrąglonego do osi budowli) są puste w budowli i którego stopy nie leżą w gruncie (`ITerrainService.TrySampleSolid`, K5-23); stopy = środek dolnej ściany tej komórki + 0,05 m. Wyskok bez wolnej komórki: punkt nad prostopadłościanem obejmującym budowlę + 1 m wzdłuż `Up` regionu, nad fotelem. Wynik w ramie budowli, potem `FrameGraph.TryExpress` do ramy planety i `FrameMath.TryToFloat` (zakres `FrameLimits`).
8. **Walidator `SeatEvictIntent`**: budowla istnieje, Y9, komórka zajęta, funkcja fotela, zasięg i linia wzroku jak w decyzji 5; fotel zajęty przez **gracza offline** (`IsOnline == false`; online albo pusty → `InvalidParameters`); aktor nie jest tym graczem; budowla `Static` albo zaparkowana (`ShipNotParked`). Resolver: `EntityReparentOp(offline.Entity, Planet(spawn), ParentSeq + 1, RespawnPoints.WorldStart, Identity, 0, null)`. `RespawnPoints.WorldStart` (Core/Simulation) = dzisiejszy prywatny `ServerSimulation.SpawnPoint(State.Planets.All[0])` wydzielony bez zmiany wyniku (test bitowy); K6-35 podmienia na łóżko. Gracz offline po powrocie (`PreparePlayerJoin` — rama i poza z `PlayerState`) staje w punkcie odrodzenia.
9. **Blokada startu i posadzenie** (`ShipLaunchGuard`, `src/Horyzont.Server/Ships/`, wołany przez `ShipFlightSystem` przed `Step`, gdy statek ma `Parked`, a sterowanie zwalnia `LandingGear` — „próba startu”): osoby w obrysie = gracze online i offline w ramie planety regionu statku (nie w ramie statku), których kapsuła (`PlayerCapsule`) przecina prostopadłościany `ShipHullBoxes` powiększone o `SeatRules.HullMarginMeters = 0,5` (pozycja wyrażona w ramie budowli przez `FrameGraph`). Dla każdej osoby online (posortowanej po `PlayerId`) guard szuka wolnego fotela `Passenger`, potem `Navigator` (`ShipStats.Seats`, kolejność komórek) i stosuje `EntityReparentOp` jako operację serwera (`ServerSimulation.ApplyServerOperation`, poza z `SeatPose` — **nie** `FrameTransitions.TryReparent`, bo ta zachowuje pozę w świecie). Gdy zostaje ktoś bez miejsca (albo ktoś offline) → `ShipLaunchBlock.PersonInHull`, statek zostaje na postoju; `FlightEnvironment.LaunchBlock` = `max` z blokady K5-24 (`FuelReserve`) i tej (`FuelReserve` ma pierwszeństwo przy obu — stała kolejność). Guard działa najwyżej raz na `SeatRules.LaunchRetryTicks = 30` na statek (bez burzy operacji przy trzymanym przycisku). Blokada trafia do `ShipFlightStatus.LaunchBlock` → `ShipStatus` (K5-26) → HUD. Domyślnie posadzenie włączone (`SeatRules.AutoSeatOnLaunch = true`, P2).
10. **Pilot** = gracz siedzący w komórce **pierwszego kokpitu** (`ShipStats.ControlFrame`, K5-24 decyzja 2); siedzący w innych kokpitach to pasażerowie bez sterów (P3). `PilotRegistry` (Server/Ships): dla statku → `PlayerId?` pilota, odświeżany z `ServerSimulation.Applied` (`EntityReparentOp`, `PlayerLeaveOp`, `PlayerJoinOp`, `ShipModeSetOp`, bloki statku) — bez przeszukiwania graczy co tick.
11. **`ShipInputBatch` (30)** (`src/Horyzont.Core/Protocol/Messages/`): `sealed record ShipInputBatch(uint FirstSeq, IReadOnlyList<ShipInput> Inputs) : Message`, `readonly record struct ShipInput(uint ClientTick, ShipControl Control)`. Kodek: `[FirstSeq u32][Count u8 = 1..5]` + na wejście `[ClientTick u32][ThrustX ThrustY ThrustZ Pitch Yaw Roll i8×6][Buttons u8]` (11 B; batch ≤ 60 B); odrzuca `Count` spoza 1..5, oś = −128 i bity przycisków spoza `ShipButtons` (`ProtocolViolationException`). Kanał zawodny; X5: od klienta w `InGame` (`IsGameMessageFromClient`), od serwera — nie. Klient wysyła batch co tick, gdy jest pilotem: nowe wejście + do 3 poprzednich (spec :164) → typowo 49 B × 60 Hz ≈ 2,9 KB/s + nagłówek transportu < 4 KB/s (Y5; test).
12. **Bufor wejść statku (Net).** `InputBuffer` staje się `SequencedInputBuffer<TInput> where TInput : struct` (ta sama semantyka: numery większe od ostatnio pobranego, bez duplikatów, pojemność, przy przepełnieniu odrzucane najnowsze); `InputBuffer` = jego zamknięta specjalizacja dla `PlayerInput` (istniejące testy bez zmian). `ServerSession`: `peer.ShipInputs`, `case ShipInputBatch` → `Add`, `TryTakeShipInput(PlayerId, out ShipInput, out uint seq)`, `ClearShipInputs(PlayerId)` (przy zmianie fotela i rozłączeniu).
13. **`PilotControlSource : IShipControlSource`** (Server/Ships; złożony przed `NoPilotControlSource`, po `DevControlSource` — dev ma pierwszeństwo tylko z `--dev` i aktywnym `ship pilot`): `TryGetControl(ship, tick, out control)`: brak pilota albo pilot offline → `false` (K5-24: autolądowanie; spec :202); pilot online: jedno wejście z `TryTakeShipInput` na tick (zapamiętuje `LastProcessedSeq`); pusty bufor → ostatnie sterowanie przez najwyżej `SeatRules.HoldLastInputTicks = 15`, potem osie 0 z ostatnimi przyciskami (wspomaganie trzyma zawis — nie autolądowanie). Wejścia graczy, którzy nie są pilotem żadnego statku, są zdejmowane z bufora co tick i liczone (`ShipInputStats.NotPilotDropped`, log `[LoggerMessage]` najwyżej raz na 10 s na gracza, kod `NotPilot` w treści logu) — bez odpowiedzi (kanał zawodny, spec :165). Bez alokacji.
14. **`ShipCorrection` (34)** (`src/Horyzont.Core/Protocol/Messages/`): `sealed record ShipCorrection(EntityId Ship, uint LastProcessedSeq, uint ParentSeq, Vector3 Position, Quaternion Rotation, Vector3 LinearVelocity, Vector3 AngularVelocity, float Fuel, ShipFlightFlags Flags, ShipTerrainGate Gate, ShipLaunchBlock LaunchBlock) : Message`. Kodek: `[Ship u64][Seq u32][ParentSeq u32][Pos 3×f32][Rot 4×f32][Lin 3×f32][Ang 3×Half][Fuel f32][Flags u8][Gate u8][LaunchBlock u8]` = 69 B (Y5 podaje „~64 B”; 69 B × 20 Hz = 1,4 KB/s w budżecie 20 480 B/s — zapisać w PR); odrzuca NaN/∞, obrót nieznormalizowany (tolerancja jak `OwnStateValidator`), `Fuel < 0`, wartości enumów spoza zakresu. Bramka i blokada startu są w korekcie, bo klient nie zna gotowości terenu ani osób w obrysie (wejścia `Step`, które klient musi dostać od serwera). Kanał zawodny; X5: od serwera w `InGame`, od klienta — nie.
15. **`ShipCorrectionSender`** (Server/Ships, wołany przez `EntityReplication` w rundzie migawek — tylko dla odbiorcy, który jest pilotem): stan z `ShipFlightSystem.TryGetPilotState(ship, out ShipMotion motion, out ShipTerrainGate gate, out ShipLaunchBlock block)` (nowe API w pliku K5-24: ruch **po** kroku ticku, w którym pobrano `LastProcessedSeq`; poza `double` → `float` przez `FrameMath.TryToFloat`). Koszt bajtów wliczany `ISnapshotBudget.Debit` w tej samej rundzie (Y5). Korekta jedzie wyłącznie z migawką pilota (spec :166): gdy budżet pominie migawkę, pomija też korektę, więc obie mają tę samą częstotliwość (K5-26 decyzja 6: własny łańcuch w każdej migawce).
16. **Predykcja pilota** (`ShipPrediction`, `src/Horyzont.Client/Ships/`): pierścień 128 wpisów `(seq, ShipControl, ShipMotion po kroku, FlightEnvironment)`; co tick klienta: sterowanie z wejścia → `ShipInputSequencer` → `ShipFlightModel.Step` z `ClientFlightEnvironment` (grawitacja i wysokość z `ClientStarSystem` + `ClientFrameGraph`, opór wg ciała, bramka i blokada startu z ostatniej korekty) → `IShipCollider` klienta (gra: ta sama klasa `GodotShipCollider` na przestrzeni fizyki klienta z wykluczeniem własnych chunków; testy: `SphereGroundCollider`). `ShipStats` liczone u klienta z lustra tym samym `ShipStatsCalculator` (test bitowej równości z serwerem). Korekta: wpis o `seq == LastProcessedSeq` porównany ze stanem serwera; błąd ponad tolerancję (`ShipCorrectionPolicy`: pozycja `max(0,05 m; 4 ulp |p|)`, obrót 0,01 rad, prędkość 0,1 m/s) → stan = serwer, ponowna symulacja kolejnych wpisów; `ParentSeq` starszy → odrzucona; nowszy → wstrzymana do nadejścia przepięcia statku (≤ 1 s, potem odrzucona i licznik). Przepięcie strefy (`OperationApplied { EntityReparentOp }` statku) → cały pierścień wyrażony w nowej ramie (`ClientFrameGraph`; w EA ramy mają te same osie — przesunięcie). Bez alokacji po rozgrzaniu.
17. **Wygładzanie i skok.** Różnica między pozą pokazywaną a nową przewidzianą po korekcie: < `SnapDistance = 5` m → przesunięcie wizualne wygaszane liniowo przez `SmoothSeconds = 0,15` (obrót slerp w tym czasie); ≥ 5 m → natychmiastowy skok i wpis Warning `[LoggerMessage]` z błędem i ostatnimi 10 wejściami (spec :210). Wygładzanie dotyczy tylko renderu — stan predykcji jest zawsze dokładny.
18. **Kamera i widok.** Siedzący: kamera = poza statku ∘ `SeatPose` ∘ oko; u pilota poza statku z predykcji (z wygładzeniem), u pasażera i nawigatora — interpolowana z migawek (`ClientFrameGraph`, spec :167). Rozglądanie się myszą w granicach ±100° yaw, ±80° pitch względem fotela (bez wpływu na stan). `LocalPlayer` w ramie budowli pozostaje `Frozen` (K5-20 decyzja 14) i nie wysyła `InputBatch`.
19. **Szybowanie po wyskoku** (`MovementMode.Gliding = 6`, Core/Motion): wchodzi w nie ciało gracza po `EntityReparentOp` z ramy `Construct` do ramy `Planet` z `|LocalVelocity| > 0,5` m/s (reguła `EjectRules.StartsGlide(op)` — ta sama po stronie serwera w `ServerCharacters` i klienta w `LocalPlayer`); `CharacterMotor.Step` w `Gliding`: składowa radialna ≥ `−MovementRules.SuitGlideFallSpeed` (6 m/s), składowa pozioma wygaszana `GlideHorizontalDamping = 0,5` 1/s plus sterowanie do prędkości chodu, dziedziczona prędkość jako stan początkowy (`EntityReparentOp.LocalVelocity`); kontakt z podłożem → `Walking`. Zmiana semantyki ruchu i nowa wartość enuma w migawce → D4. Bez obrażeń (podprojekt 3).
20. **Rozłączenia i powrót.** Pilot: `PlayerLeaveOp` zostawia `Frame`/`Seat` → `PilotRegistry` zwraca brak pilota → autolądowanie (K5-24); pasażer: zostaje w fotelu (offline, fotel zajęty). Powrót: `PreparePlayerJoin` daje ramę statku i pozę fotela, `ServerCharacters` przyczepia ciało (K5-20), klient dostaje fotel z `KindData` (K5-26) albo — gdy K5-26 nie jest scalony — z własnego `OperationApplied`/stanu (P7). Statek zaparkowany po autolądowaniu z graczem offline w kokpicie: kto inny może go zwolnić (decyzja 8).
21. **`ConstructNotEmpty` (38) i `SeatOccupied` przy rozbiórce.** Walidator `RemoveBlockIntent` (`IntentValidator.cs:330-335`): zajęty fotel (także offline) → `SeatOccupied` (zamiast `IntersectsPlayer`); ostatni blok budowli, w której ramie jest jakikolwiek byt → `ConstructNotEmpty`. Applier bez zmian (już odmawia). Zmiana odpowiedzi walidatora — nie formatu; testy `ReparentTests.cs:82, 95` zmieniają oczekiwany kod (opis w PR).
22. **Komendy konsoli (dev):** `seat <player> enter <construct> <x> <y> <z>`, `seat <player> exit`, `seat <player> evict` (te same walidatory przez `ServerSimulation.Submit` w imieniu gracza; linia wyniku `SEAT player=… result=… code=…`, format stały).
23. **Klient — gra.** Akcje (krok 0 sprawdza wolne klawisze w `project.godot`): `seat_use` (F) — wsiadanie do fotela w celowniku / wysiadanie (w locie wyskok); `seat_evict` (Shift+F, tylko fotel gracza offline); pilot: `ship_thrust_*` (W/S przód–tył, A/D, Spacja/C), `ship_roll_*` (Q/E), mysz pitch/yaw, `ship_pulse_toggle` (P), `ship_gear_toggle` (L), `ship_assist_toggle` (Z); przyciski to stany (K5-24 P9). Stanowisko nawigatora: po wejściu na fotel `Navigator` mapa otwiera się raz przez `IMapOpener.Open()` (K5-27, jeśli scalony; inaczej podpowiedź w HUD), `M` przełącza. Teksty pl/en odmów 32–35, 38 i blokady `PersonInHull` w `game/locale/ui.csv`.
24. **Protokół.** `IntentSubmit`/`IntentReply`/`OperationApplied`, migawka (`Gliding`), nowe komunikaty 30 i 34, wartość `ShipLaunchBlock` 2 w `ShipStatus` → `ProtocolVersion` = (main przy scaleniu) + 1 (planowo 14 — Y3), aktualizacja `ProtocolVersion_MatchesDocumentedMergeValue` i `Constants_GoldenValues`; fixture X6 (`CodecGen`, `MaxMessages.All()`, `ProtocolContractTests`) dla 30, 34 i intencji 11, 12, 16; `MessageChannels_MapsEveryType` z 30 i 34 na liście zawodnych.

### Pliki

```
src/Horyzont.Core/Intents/               SeatEnterIntent, SeatExitIntent, SeatEvictIntent, RejectReason (+ 32, 33, 34, 35, 38)
src/Horyzont.Core/Ships/                 SeatPose, SeatExitPlanner, SeatRules, EjectRules, ShipLaunchBlock (+ PersonInHull = 2)
src/Horyzont.Core/Simulation/            IntentActorFrames, ActorView, RespawnPoints, IntentValidator, IntentResolver, ServerSimulation, GameRules
src/Horyzont.Core/Motion/                MovementMode (+ Gliding = 6), CharacterMotor, MovementRules (+ SuitGlideFallSpeed)
src/Horyzont.Core/Protocol/              MessageType (+ 30, 34), MessageChannels, Messages/ShipInputBatch, ShipInput, ShipCorrection, ProtocolConstants
src/Horyzont.Core/Serialization/         IntentCodec (11, 12, 16), MessageCodec (30, 34)
src/Horyzont.Net/Session/                SequencedInputBuffer, InputBuffer, ServerSession (ShipInputs), MessageRouter (30, 34)
src/Horyzont.Server/Ships/               PilotControlSource, PilotRegistry, ShipInputStats, ShipLaunchGuard, ShipCorrectionSender, SeatLog;
                                         ShipFlightSystem (+ TryGetPilotState, wywołanie guardu)
src/Horyzont.Server/Replication/         EntityReplication (wywołanie ShipCorrectionSender przy migawce pilota)
src/Horyzont.Server/Console/ConsoleCommandProcessor.cs    seat
src/Horyzont.Client/Ships/               ShipInputSequencer, ShipPrediction, ShipCorrectionPolicy, ClientFlightEnvironment, SeatView
game/src/Client/Ships/                   ShipPilotController, SeatInteraction, SeatCamera, FlightHud (+ blokada PersonInHull)
game/src/Client/Player/LocalPlayer.cs    Gliding po własnym wyskoku (edycja ograniczona — X10)
game/src/Server/Characters/ServerCharacters.cs           Gliding po wyskoku (Y10 ścieżka K5-20)
game/src/Boot/GameComposition.Seats.cs   (nowy partial)
game/locale/ui.csv, project.godot        akcje i teksty
tests/Horyzont.Testing/Ships/            SeatFixtures (statek z kokpitem, 2 fotelami, nawigatorem), CodecGen (11, 12, 16, 30, 34)
tests/Horyzont.Core.Tests/Ships/         SeatIntentTests, SeatPoseTests, SeatExitPlannerTests, IntentActorFramesTests, ConstructNotEmptyTests
tests/Horyzont.Core.Tests/Motion/        CharacterMotorGlideTests
tests/Horyzont.Core.Tests/Serialization/ SeatCodecGoldenTests, ShipInputCodecTests
tests/Horyzont.Net.Tests/                SequencedInputBufferTests, ShipMessageRouterTests
tests/Horyzont.Server.Tests/Ships/       PilotControlSourceTests, ShipLaunchGuardTests, ShipCorrectionSenderTests, PilotDisconnectTests
tests/Horyzont.Client.Tests/Ships/       ShipPredictionTests, ShipInputSequencerTests
tests/Horyzont.Integration.Tests/Ships/  SeatsUnderLatencyTests, PilotPredictionTests, SeatRejoinTests
game/tests/Ships/                        EjectEngineTests, SeatCameraTests
```

## Testy akceptacyjne (napisz najpierw)

| Test | Plik | Kategoria | Faza | Co sprawdza |
| --- | --- | --- | --- | --- |
| `IntentCodec_SeatEnterExitEvict_Golden` / `MessageCodec_ShipInputBatch_GoldenAndRejects` / `MessageCodec_ShipCorrection_GoldenAndRejects` / `RejectReason_SeatCodes_Golden` / `MovementMode_Gliding_Golden` | `tests/Horyzont.Core.Tests/Serialization/SeatCodecGoldenTests.cs`, `ShipInputCodecTests.cs` | domyślna | A | bajty zatwierdzone przez Opus (krok 1); odrzucenia z decyzji 1, 11, 14 |
| `SeatEnterIntent_FreeSeatInReach_ResolvesReparentWithSeatPose` / `…_Occupied_RejectedSeatOccupied` / `…_OfflineOccupant_RejectedSeatOccupied` / `…_TooFar_RejectedSeatOutOfReach` / `…_NotSeatBlock_InvalidParameters` / `…_NoLineOfSight` / `…_ShipFlying_RejectedShipNotParked` / `…_SwitchSeatSameShip_Accepted` | `tests/Horyzont.Core.Tests/Ships/SeatIntentTests.cs` | domyślna | A | decyzje 2, 5; brak zmiany stanu przy odmowie |
| `SeatExitIntent_Parked_ExitPointFreeAndOutsideTerrain` / `…_NoFreeCell_RejectedCellOccupied` / `…_InFlight_Under30And200_EjectsWithShipVelocity` / `…_InFlight_Over30ms_RejectedEjectTooFast` / `…_InFlight_Over200m_RejectedEjectTooFast` / `…_ShipInSystemFrame_RejectedEjectTooFast` / `…_NotSeated_InvalidParameters` | jw. | domyślna | A | decyzje 6–7; prędkość `v + ω × r` w ramie planety ± 1e−4 |
| `SeatEvictIntent_OfflineOccupant_MovedToWorldStart` / `…_OnlineOccupant_InvalidParameters` / `…_Self_InvalidParameters` / `…_ShipNotParked_Rejected39` / `RespawnPoints_WorldStart_BitwiseEqualsOldSpawnPoint` | jw. | domyślna | A | decyzja 8 |
| `ConstructNotEmpty_RemoveLastBlockWithSeatedOffline_Rejected38` / `…_WithStandingChild_Rejected38` / `RemoveBlock_OccupiedSeat_RejectedSeatOccupied` | `…/Ships/ConstructNotEmptyTests.cs` | domyślna | A | decyzja 21 |
| `IntentActorFrames_SeatedEyeExpressedInTarget` / `…_DifferentRegion_WrongPlanet` / `…_SystemRegion_WrongPlanet` / `…_SeatedBlockEdit_InvalidParameters` + `ShipModeIntent_PilotInCockpit_ToStaticAfterLanding_Accepted` / `ShipModeIntent_SeatedNonPilot_RejectedNotPilot` / `RefuelIntent_PilotInCockpit_Accepted` | `…/Ships/IntentActorFramesTests.cs` | domyślna | A | decyzje 3–4 (Y9, N11) |
| `CharacterMotor_Gliding_FallSpeedClampedTo6` / `…_InheritsVelocityThenDamps` / `…_FloorContact_Walking` / `EjectRules_StartsGlide_OnlyConstructToPlanetWithVelocity` | `tests/Horyzont.Core.Tests/Motion/CharacterMotorGlideTests.cs` | domyślna | A | decyzja 19 |
| `SequencedInputBuffer_*` (istniejące przypadki `InputBuffer` dla `ShipInput`) / `MessageRouter_ShipInputBatch_ClientInGameOnly` / `…_ShipCorrection_ServerInGameOnly` | `tests/Horyzont.Net.Tests/` | domyślna | B | decyzja 12, X5 |
| `PilotControlSource_OneInputPerTick` / `…_NoPilot_ReturnsFalseAutoland` / `…_PilotOffline_ReturnsFalse` / `…_EmptyBuffer_HoldsLast15TicksThenNeutral` / `…_NonPilotInputs_DroppedCountedNotPilot` / `…_TickAllocatesZero` | `tests/Horyzont.Server.Tests/Ships/PilotControlSourceTests.cs` | domyślna | B | decyzje 10, 13 |
| `ShipLaunchGuard_PersonInHull_SeatedOnFreeSeat` / `…_NoFreeSeat_BlocksPersonInHull` / `…_OfflinePersonInHull_Blocks` / `…_PersonLeaves_LaunchAllowed` / `…_FuelReserveTakesPrecedence` / `…_AtMostOncePer30Ticks` | `…/Ships/ShipLaunchGuardTests.cs` | domyślna | B | decyzja 9 |
| `ShipCorrectionSender_OnlyToPilot_WithPilotSnapshot` / `…_DebitsSnapshotBudget` / `…_StateAfterStepOfLastProcessedSeq` / `…_RoundAllocatesZero` | `…/Ships/ShipCorrectionSenderTests.cs` | domyślna | B | decyzje 14–15, Y5 |
| `PilotDisconnect_ShipAutolandsAndParks_PassengersStaySeated` | `…/Ships/PilotDisconnectTests.cs` | domyślna | B | decyzja 20, spec :202–203 |
| `ShipPrediction_SameInputsAsServer_ZeroCorrections` / `…_ErrorBelow5m_SmoothedOver150ms` / `…_ErrorOver5m_SnapsAndLogsLastInputs` / `…_NewerParentSeq_HeldUntilReparent` / `…_ZoneTransition_RingRebased` / `…_StepAllocatesZero` / `ShipStats_ClientMirrorEqualsServer_Bitwise` / `ShipInputBatch_SteadyState_Below4KBps` | `tests/Horyzont.Client.Tests/Ships/ShipPredictionTests.cs`, `ShipInputSequencerTests.cs` | domyślna | B | decyzje 11, 16–17 |
| `PilotPrediction_Rtt100_RespondsSameTick_CorrectionsBelow05mIn95Percent` | `tests/Horyzont.Integration.Tests/Ships/PilotPredictionTests.cs` | `Slow` | B | „Pomiar 2” |
| `Seats_ThousandRandomEnterExitEject_UnderLatency_NoJumpOver05m_NoWrongFrame` | `…/Ships/SeatsUnderLatencyTests.cs` | `Slow` | C | „Pomiar 1” |
| `Rejoin_SeatedPlayer_SameSeat` / `Rejoin_Evicted_AtWorldStart` / `Checkpoint_SeatedOfflinePlayer_SeatSurvivesRestart` | `…/Ships/SeatRejoinTests.cs` | domyślna | C | decyzje 8, 20, spec :192 |
| `Eject_HomeTerrain_LandsGliding_FallSpeedAtMost6` / `SeatCamera_PassengerUsesInterpolatedShip_PilotUsesPrediction` | `game/tests/Ships/EjectEngineTests.cs`, `SeatCameraTests.cs` | silnik | C | decyzje 18–19 na prawdziwym terenie |
| `FlightHud_LaunchBlockPersonInHull_ShowsMessage` | `game/tests/Ships/` | silnik | C | decyzja 9, spec :229 |
| `Manual_Seats_PilotAndPassengers_InGame` | procedura w PR | `Manual` | C | host + zdalny klient (`--dev`, `LatencyTransport` 100 ms): wsiadanie, start, lot na księżyc, wyskok przy 150 m, nawigator otwiera mapę, rozłączenie pilota → autolądowanie; zrzuty/nagranie |

### Pomiar 1 (kryterium 1)

- Uprząż K5-20 („Pomiar w teście akceptacyjnym”): `ServerRuntime` bez silnika (`FakeServerTerrain`, `SphereGroundCollider`), klienci przez `InMemoryNetwork` (`FakeTimeProvider`), RTT z {100, 150, 200, 250} ms ± 10 ms, 5 % strat zawodnego (X11). Ziarno w nazwie przypadku; 4 przebiegi × 250 zdarzeń = 1000.
- Świat: `SeatFixtures` — statek A zaparkowany (kokpit, 2 fotele, nawigator) i statek B w zawisie 120 m nad terenem, sterowany skryptem `ScriptedControlSource` po okręgu 20 m/s (poniżej progów wyskoku); 4 graczy (G1–G3 + obserwator G4 stojący 50 m od A).
- Zdarzenia losowe co 0,5–2 s przez **intencje z klienta** (`SeatEnterIntent`, `SeatExitIntent`, przesiadka, `SeatEvictIntent` po rozłączeniu gracza), w tym wyskoki z B. Ruch po wyjściu: skryptowy integrator `CharacterMotor` (bez fizyki; `Gliding` po wyskoku).
- Miary (te same co K5-20): skok w klatce renderu 60 Hz `|p(t) − p(t−1)| − v_max·Δt ≤ 0,5 m` dla obserwatora i dla własnego bytu (z kamerą fotela); błąd względem serwera przy tym samym ticku ≤ 0,5 m; `WrongFrameApplied = 0`. Raport: maks., p99, liczba odmów wg kodu, `ReparentSnaps`, `ResyncRequest`.

### Pomiar 2 (kryterium 2)

- Serwer z `ShipFlightSystem` (`SphereGroundCollider`), klient-pilot z `ShipPrediction` (ten sam zderzacz), RTT 100 ms ± 10 ms, 5 % strat (wariant 0 % strat raportowany obok).
- Skrypt pilota 180 s: start z postoju, wznoszenie, obroty we wszystkich osiach, ruch boczny, napęd pulsacyjny 30 s, hamowanie limitem, zawis, lądowanie na postój; dwa ziarna.
- Miary: (a) „od razu” — w ticku klienta, w którym zmienia się wejście, przewidziana prędkość zmienia się w tym samym kroku (opóźnienie 0 ticków; 100 % zmian); (b) błąd pozycji `|przewidziana(seq) − serwer(seq)|` dla każdej odebranej korekty: p95 < 0,5 m (twardy próg), raport p50/p95/p99/maks.; (c) skoki ≥ 5 m = 0.

## Kroki

Każdy krok: test czerwony → kod → zielone testy → mały commit (Conventional Commits). Wyszukiwanie kodu i uruchamianie testów zlecać tańszemu subagentowi (CLAUDE.md projektu).

**Faza A — Core**

- [ ] 0. Warunki wejścia: `git status`, worktree `F:/dev/horyzont-k5-25` od `origin/main` po scaleniu K5-24; w „Odstępstwach” zapisać scalone pakiety K5 i rzeczywiste nazwy (`FrameTransitions`, `IntentActorFrames`-podobne pomocniki z K5-23/24, `ShipStats.Seats`/`ControlFrame`, `IShipControlSource`, `ShipFlightSystem` i miejsce kroku, `ShipLaunchBlock`, `FlightEnvironment.LaunchBlock`, `ReplicationEntity.Seat`/`KindData` — czy K5-26 scalony, `IMapOpener` — czy K5-27 scalony, `ServerCharacters` Y10); sprawdzić, że intencje 11, 12, 16, odmowy 32–35, 38, komunikaty 30, 34 i `MovementMode` 6 są wolne na `main` i w otwartych PR K5; wolne klawisze w `project.godot`. Commit `docs:`.
- [ ] 1. **Kontrakt do zatwierdzenia przez Opus przed kodem:** rekordy intencji 11, 12, 16, `ShipInputBatch`/`ShipInput`, `ShipCorrection`, wartości `RejectReason` 32–35, 38, `ShipLaunchBlock.PersonInHull`, `MovementMode.Gliding`, `SeatPose` i `SeatExitPlanner` (sygnatury), `IntentActorFrames`, układ bajtów (testy złote czerwone). Commit `test:`.
- [ ] 2. Po akceptacji: kodeki (11, 12, 16, 30, 34), `RejectReason`, `MessageType`, `MessageChannels`, fixture X6, `ProtocolVersion` (D4) z aktualizacją `ProtocolVersion_MatchesDocumentedMergeValue` i `Constants_GoldenValues`. Commity `test:`, `feat:`.
- [ ] 3. `IntentActorFrames` + przepięcie walidatorów `ShipModeIntent`, `RefuelIntent` i intencji bloków na pomocnik (Y9); `NotPilot` dla siedzących; testy pilota w kokpicie. `git diff origin/main` pusty na korpusie złotym i `DeterminismTests` (Y14). Commity `test:`, `feat:`.
- [ ] 4. `SeatPose`, `SeatExitPlanner`, `SeatRules`, `RespawnPoints` (test bitowy względem starego `SpawnPoint`), walidatory i resolvery 11, 12, 16; `ConstructNotEmpty`/`SeatOccupied` przy rozbiórce (zmiana `ReparentTests.cs:82, 95` opisana w PR). Commity `test:`, `feat:`.
- [ ] 5. `MovementMode.Gliding`, `CharacterMotor` (szybowanie), `EjectRules.StartsGlide`; testy ruchu istniejące bez zmian (opadanie poza `Gliding` jak dziś). Commity `test:`, `feat:`.

**Faza B — sterowanie, korekty, predykcja**

- [ ] 6. `SequencedInputBuffer<T>`, `ServerSession` (`ShipInputs`, `TryTakeShipInput`, `ClearShipInputs`), router (Y4) + testy routera obu stron. Commity `test:`, `feat:`.
- [ ] 7. `PilotRegistry`, `PilotControlSource` (+ złożenie w `GameComposition.Seats.cs`: dev → pilot → brak pilota), `ShipInputStats`, `PilotDisconnect_*`; `ShipLaunchGuard` wpięty w `ShipFlightSystem` (jedno wywołanie przed `Step`; `PersonInHull` w `FlightEnvironment.LaunchBlock` i `ShipFlightStatus`). Commity `test:`, `feat:`.
- [ ] 8. `ShipFlightSystem.TryGetPilotState`, `ShipCorrectionSender` w rundzie `EntityReplication` (rozłączna metoda — X10; przy K5-26 na `main` — po jego łańcuchu własnym), budżet migawek. Commity `test:`, `feat:`.
- [ ] 9. `ShipInputSequencer`, `ShipPrediction`, `ShipCorrectionPolicy`, `ClientFlightEnvironment`; testy klienta; `PilotPrediction_Rtt100_*` zielony z raportem (p50/p95/p99/maks.). Niezaliczone 95 % → najpierw diagnoza rozjazdu wejść `Step` (bramka, blokada, `ShipStats`, kolejność kroków), nie poluzowanie progu. Commity `test:`, `feat:`.

**Faza C — gra i odbiór**

- [ ] 10. **[K3-14, K5-24]** Gra: `ShipPilotController`, `SeatInteraction`, `SeatCamera`, akcje i teksty, `FlightHud` (blokada `PersonInHull`, odmowy 32–35, 38), stanowisko nawigatora (`IMapOpener`, jeśli K5-27 scalony), `LocalPlayer` — `Gliding` po własnym wyskoku (edycja ograniczona — X10), `ServerCharacters` — `Gliding` po wyskoku; komendy `seat`. Commity `test:`, `feat:`.
- [ ] 11. `Seats_ThousandRandomEnterExitEject_*` zielony z raportem; `SeatRejoinTests`; testy silnika `game/tests/Ships/` (job `game`, szeregowo — Y15); `python tools/engine/build.py smoke`; `Manual_Seats_PilotAndPassengers_InGame` (zrzuty w PR). Commit `test:`.
- [ ] 12. Rebase na `main`; pełne komendy odbioru; `docs/ZASADY.md` pkt 2: „wsiadanie, wysiadanie, wyskok i zwolnienie fotela to wyłącznie `EntityReparentOp` z intencji 11/12/16 albo z `ShipLaunchGuard` (operacja serwera); pozę fotela i punkt wyjścia liczy resolver”, pkt 3: wiersz `src/Horyzont.Core/Motion/` („`CharacterMotor` wspólny z predykcją — zmiana zachowania = `ProtocolVersion`”, jeśli wiersza brak); wiersz scalenia K5-25 w „Kolejność K5” w `docs/plans/README.md` z faktyczną wartością `ProtocolVersion`; PR z raportami pomiarów 1 i 2, odstępstwami i odpowiedziami właściciela. Commit `docs:`.

## Odbiór

```
dotnet build Horyzont.sln
dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"
dotnet format Horyzont.sln --verify-no-changes
python tools/engine/build.py smoke
```

Plus testy silnika `game/tests/Ships/` (job `game`), w PR: raport „Pomiar 1” (maks., p99, odmowy wg kodu, `ReparentSnaps`, `ResyncRequest`) i „Pomiar 2” (p50/p95/p99/maks. korekt, opóźnienie odpowiedzi, skoki ≥ 5 m), ruch K→S pilota w B/s, wynik `Manual_Seats_*` i `git diff origin/main -- tests/Horyzont.Core.Tests/Golden/TerrainJournals tests/Horyzont.Core.Tests/TerrainJournalGoldenCorpusTests.cs tests/Horyzont.Core.Tests/Hashing/DeterminismTests.cs` = pusty.

## Kody i punkty styku

Kody zajęte przez K5-25 (README Y2): **`IntentCode` 11 `SeatEnter`, 12 `SeatExit`, 16 `SeatEvict`**; **`RejectReason` 32 `SeatOccupied`, 33 `SeatOutOfReach`, 34 `EjectTooFast`, 35 `NotPilot`, 38 `ConstructNotEmpty`** (po K5-25 zakres 30–39 jest wyczerpany); **`MessageType` 30 `ShipInputBatch`, 34 `ShipCorrection`** (oba zawodne); **`ShipLaunchBlock.PersonInHull = 2`**; **`MovementMode.Gliding = 6`** (wartość w migawce). Bez nowych `OperationKind` (używa 7 `EntityReparent`), `BlobKind` ani migracji. `ProtocolVersion`: (main przy scaleniu) + 1, planowo **14** (Y3).

| Rodzaj | Kod | Nazwa | Pakiet |
| --- | --- | --- | --- |
| `IntentCode` | 10 / 11 / 12 / 13 / 14 / 15 / 16 / 17–19 | `ShipMode` / `SeatEnter` / `SeatExit` / `Refuel` / `MarkerSet` / `MarkerRemove` / `SeatEvict` / wolne | K5-23 / **K5-25** / **K5-25** / K5-24 / K5-27 / K5-27 / **K5-25** / — |
| `RejectReason` | 30, 31, 37 / 32–35, 38 / 36 / 39 | `NoCockpit`, `ShipAnchored`, `NoEngine` / `SeatOccupied`, `SeatOutOfReach`, `EjectTooFast`, `NotPilot`, `ConstructNotEmpty` / `MarkerLimit` / `ShipNotParked` | K5-23 / **K5-25** / K5-27 / K5-24 |
| `MessageType` | 30 / 31 / 32 / 33 / 34 / 35 / 36–39 | `ShipInputBatch` / `ShipStatus` / `StarSystemState` / `MarkersState` / `ShipCorrection` / `MapPresence` (warunkowo) / wolne | **K5-25** / K5-26 / K5-21 / K5-27 / **K5-25** / K5-27 / — |
| `ShipLaunchBlock` | 0 / 1 / 2 | `None` / `FuelReserve` / `PersonInHull` | K5-24 / K5-24 / **K5-25** |
| `MovementMode` | 0–5 / 6 | istniejące / `Gliding` | K1–K3 / **K5-25** |

Kolejność scalania K5: **K5-23 → K5-20 → K5-21 → K5-22 → K5-24 → K5-27 → K5-26 → K5-25 → K5-28** (README); rekomendacja **K5-26 → K5-25** — gdy K5-25 scala się pierwszy, K5-26 robi rebase na jego zmianach w `EntityReplication`. Zasada X10.

| Plik / punkt | K5-25 | Inny pakiet | Rozwiązanie |
| --- | --- | --- | --- |
| `IntentCodec`, `Intents/`, `IntentValidator.cs`, `IntentResolver.cs`, `GameRules` | 11, 12, 16, pomocnik Y9, `NotPilot`, rozbiórka (decyzja 21) | K5-23 (10), K5-24 (13), K5-27 (14, 15) | przypadki w kolejności numerów; pomocnik Y9 zastępuje warunki ramy w 10 i 13 — scalany później dostosowuje |
| `RejectReason.cs` | 32–35, 38 | 30, 31, 37 / 36 / 39 | wartości wg Y2 |
| `Message*`, `MessageRouter`, fixture X6 | 30, 34 | 31 (K5-26), 32 (K5-21), 33 (K5-27) | dopisywanie w kolejności numerów; lista zawodnych rośnie o 30 i 34 |
| `EntityReplication.cs`, `SnapshotBuilder.cs` | wywołanie `ShipCorrectionSender` przy migawce pilota | K5-20, K5-26 (zasięgi, bramka, łańcuch własny) | rozłączna metoda; rekomendacja K5-26 → K5-25 |
| `ReplicationEntity.Seat`, `EntitySpawn.KindData` gracza | konsument (fotel po powrocie i u obserwatora) | K5-26 (właściciel definicji `[1][x][y][z]`) | K5-26 pierwszy — K5-25 używa; K5-25 pierwszy — K5-25 dodaje dokładnie definicję K5-26 (decyzja 7 K5-26), K5-26 tylko testy |
| `ShipFlightSystem` (K5-24) | `TryGetPilotState`, wywołanie `ShipLaunchGuard` | K5-24 (właściciel), K5-26 (`TryGetStatus`) | dopisanie metod; jedna linia w kroku |
| `ShipLaunchBlock`, `ShipFlightStatus`, `ShipStatus` (31) | wartość 2 | K5-24, K5-26 | kodek K5-26 przyjmuje wartości enuma — bez edycji |
| `ServerCharacters.cs` | `Gliding` po wyskoku | K5-20 (właściciel Y10) | edycja ograniczona do wejścia w `Gliding` |
| `LocalPlayer.cs` (K3-14) | `Gliding`, pilot `Frozen` bez `InputBatch` | K5-20 (rebase), K5-22 (widze) | edycje ograniczone; scalany później |
| `CharacterMotor.cs`, `MovementMode.cs`, `MovementRules` | `Gliding` | — | Core, przegląd Opus |
| `InputBuffer.cs`, `ServerSession.cs` (Net) | generyczny bufor, `ShipInputs` | — | istniejące testy bez zmian |
| `ShipStats`/`ShipStatsCalculator` | konsument `Seats`, `ControlFrame` | K5-23, K5-24 | bez edycji |
| `GameComposition.Seats.cs` | nowy partial | `.Flight` (K5-24 — linia złożenia `IShipControlSource`), `.ShipReplication` (K5-26) | podmiana jednej linii w `.Flight` |
| `game/locale/ui.csv`, `project.godot` | akcje F, Shift+F, P, L, Z, Q/E | K5-23 (G), K5-27 (M), K5-24 (HUD) | dopisywanie; klawisze w kroku 0 |
| `ProtocolConstants.ProtocolVersion` + dwa testy | podbicie | wszystkie K5 z formatem | D4 |
| `docs/ZASADY.md`, `docs/plans/README.md` | wiersze K5-25 | wszyscy | dopisywanie (Y1) |

## Ryzyka

| Ryzyko | Skutek | Odpowiedź |
| --- | --- | --- |
| Rozjazd wejść `Step` klient–serwer (bramka, blokada startu, `ShipStats`, zderzacz) | ciągłe korekty, kryterium 2 nie przechodzi | bramka i blokada w korekcie (decyzja 14); test bitowy `ShipStats`; ten sam zderzacz w teście; kolejność działań `Step` z K5-24 |
| Kwantyzacja `f32` pozy w ramie układu (do 3,1 cm przy 400 km) | fałszywe korekty daleko od ciał | tolerancja `max(0,05 m; 4 ulp)` (decyzja 16) |
| Lądowanie: kontakt z terenem u klienta inny niż na serwerze (spec :265) | widoczna korekta przy przyziemieniu | wygładzanie < 5 m; pomiar 2 obejmuje lądowanie; ręczny na prawdziwym terenie |
| Posadzenie automatyczne zaskakuje gracza | odczucie „teleportu” | tylko przy próbie startu, komunikat w HUD, P2 (wyłącznik w `SeatRules`) |
| Wyskok z zamkniętego kokpitu „przez kadłub” | postać w bloku | `SeatExitPlanner`: wolne komórki, potem punkt nad budowlą; test bez kolizji z blokami |
| Wejście w `Gliding` tylko po stronie serwera | korekta postaci po wyskoku | ta sama reguła `EjectRules.StartsGlide` po obu stronach (test) |
| Konflikty z K5-26 w replikacji | rebase | rozłączne metody, rekomendacja K5-26 → K5-25 |
| Kody odmów wyczerpane | nowa odmowa niemożliwa bez decyzji właściciela | odmowy ogólne (`InvalidParameters`, `CellOccupied`, `ShipNotParked`) tam, gdzie spec nie daje kodu |

## Pytania do właściciela (wariant domyślny obowiązuje bez odpowiedzi)

| # | Pytanie | Wariant domyślny |
| --- | --- | --- |
| P1 | Zasięg wsiadania i zwalniania fotela? | 3 m od oka do środka komórki fotela, z linią wzroku |
| P2 | Osoba w obrysie przy starcie: posadzić czy blokować? | najpierw posadzić na wolnym fotelu pasażera/nawigatora, reszta → blokada `PersonInHull` z komunikatem; wyłącznik `AutoSeatOnLaunch` |
| P3 | Kilka kokpitów — kto pilotuje? | tylko pierwszy kokpit (`ControlFrame`); inne kokpity działają jak fotele pasażera |
| P4 | Przesiadka między fotelami? | dozwolona w tej samej budowli na postoju, bez wysiadania |
| P5 | Czy siedzący może budować/kopać? | nie (`InvalidParameters`); tylko tryb statku, tankowanie, wysiadanie |
| P6 | Wysokość wyskoku liczona od czego? | od terenu pod statkiem (promień ≤ 200 m), nie od `R` |
| P7 | Fotel u obserwatora, gdy K5-26 nie jest scalony? | K5-25 dodaje `KindData` wg definicji K5-26 (jedna definicja) |
| P8 | Klawisze pilota? | WSAD + Spacja/C, Q/E roll, mysz pitch/yaw, P napęd, L podwozie, Z wspomaganie, F fotel |

## Pułapki

- Poza fotela i punkt wyjścia liczy resolver; applier `EntityReparentOp` niczego nie przelicza i nie czyta treści ani terenu.
- `FrameTransitions.TryReparent` zachowuje pozę w świecie — nie nadaje się do sadzania na fotelu (decyzja 9).
- Wejście statku przetwarzane jedno na tick; brak wejścia ≠ brak pilota (autolądowanie tylko bez pilota online).
- `ShipCorrection` wyłącznie z migawką pilota i w `ISnapshotBudget`; nigdy poza budżetem.
- Siedzący nie wysyła `InputBatch`; `LocalPlayer` w ramie budowli zostaje `Frozen`.
- Nie zmieniać bajtu wersji rekordów istniejących rodzajów intencji i operacji; nowe intencje zaczynają od wersji 1.
- `NotPilot` dla `ShipInputBatch` to licznik i log, nie odpowiedź (kanał zawodny).

## Odstępstwa i decyzje wykonawcy

(wypełnia wykonawca: stan K5 na `main` (zwłaszcza K5-26, K5-27), rzeczywiste nazwy API, akceptacja kontraktu przez Opus w kroku 1, wyniki pomiarów 1 i 2, odpowiedzi właściciela, różnice od planu)
