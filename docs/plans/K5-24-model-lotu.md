# K5-24 · Model lotu

| | |
| --- | --- |
| Prowadzi | Sonnet 5.5 (Claude Code, `claude --model sonnet`; `--model opusplan` w krokach 0–1, gdy nazwy API na `main` różnią się od tabeli „Kontekst”) |
| Testy akceptacyjne | Sonnet 5.5; kontrakty Core (op 21, intencja 13, odmowa 39, blob 12, `ShipStateRecord`, `ShipControl`, sygnatura modelu lotu, `ShipLaunchBlock`) i złote bajty zatwierdza Opus 5.5 **przed kodem** (krok 1) |
| Przegląd | Opus 5.5 w PR (ZASADY pkt 9: pakiet Sonnet zmienia kontrakty Core — operacja 21, intencja 13, odmowa 39, blob 12, `WorldState`, `WorldCheckpoint`) |
| Zależy od | K5-21 i K5-23 scalone na `main` (spec 02:249). Pośrednio K5-20 (`UpdateConstructKinematics`, fazy ticka, `FrameGraph`, `ServerFrameHistory`). **Nie** zależy od K5-22 (bramka terenu z wariantem domyślnym, decyzja 17) ani K5-25 (brak pilota = autolądowanie; sterowanie przez interfejs) |
| Gałąź | `pakiet/k5-24-lot` (worktree `F:/dev/horyzont-k5-24`) |
| Kamień milowy | K5 Kosmos; odbiór wg spec 02:249: „przeloty z napędem ok. 50, 70 i 100 s; autolądowanie z trzech wysokości” |
| Równolegle | K5-22, K5-27 (po K5-21); po K5-24 ruszają K5-25 i K5-26 — punkty styku w sekcji „Kody i punkty styku” |

**Cel:** dać statkom lot — czysty, deterministyczny model lotu w Core (ciąg, moment, wspomaganie, limit v = clamp(h/τ, v_min, v_cap), napęd pulsacyjny, paliwo z rezerwą, opór atmosfery, autolądowanie i postój), serwerowy przebieg lotu z kolizjami silnika, trwały `ShipState` (blob 12, tabela `ship_state`), tankowanie (`RefuelIntent` 13 → `ShipFuelAddOp` 21), odmowę `ShipNotParked` (39) i odstawianie statków po wczytaniu — tak, żeby przelot z napędem trwał ok. 50/70/100 s, a statek bez pilota lądował i parkował z 100 m, 1 km i 5 km.

## Kontekst

- `docs/ZASADY.md`: pkt 2 (każda zmiana świata to operacja — ale ruch statku i zużycie paliwa są poza dziennikiem jak ruch graczy), 3 (`Operations/`, `Intents/`, `OperationApplier.cs` = format; `World/`, `Checkpoints/` typy zapisywane; `Serialization/`, `Protocol/` → `ProtocolVersion`; zamrożony korpus i `DeterminismTests` bez zmian w plikach; kody tylko z zakresu Kosmosu), 4 (applier nie czyta treści ani terenu; zapis zmiennoprzecinkowy bitowo), 6 (tick bez alokacji, testy alokacji), 7, 8.
- `docs/spec/02-kosmos.md`: :14, :50 (τ = 8 s, v_min = 100 m/s, 250 m/s bez napędu, 2 km/s z napędem; czasy przelotu), :52 (grawitacja stref), :73–80 (lot: masa, ciąg ×6, wspomaganie i zawis, limit h/τ, paliwo i rezerwa 10 %, opór tylko w atmosferze, zderzenia zatrzymują bez niszczenia, darmowe tankowanie przy stacji do podprojektu 3), :82–88 (postój: podwozie + v < 1 m/s; bez pilota schodzi pionowo i parkuje, w próżni hamuje do zera), :138–140 (`ShipState`: paliwo, prędkość liniowa i kątowa, postój; model lotu w Core jako czysta funkcja wspólna z predykcją pilota; kolizje liczy silnik), :144 (lot i paliwo poza dziennikiem), :150 (op 21), :154 (`RefuelIntent` 13), :156 (`ShipNotParked` 39), :181–191 (blob 12, `ship_state`, statek w ruchu zawsze „zmieniony”, odstawienie po wczytaniu), :194 (skrót pomija `ShipState` i pozy statków), :198–211 (awarie: autolądowanie przy utracie pilota, rezerwa, teren niegotowy), :224, :228 (testy), :249 (odbiór).
- `docs/plans/README.md`: X4/D4, X6 (fixture kodeka), X10.
- `docs/plans/K5-23-bloki-statku.md` (autorytatywny): decyzje 1 (`BlockFunction`/`BlockFunctionKind` 0–9), 2 (`BlockContentTable` tylko w walidatorze), 3 (`ShipStatsCalculator`: masa, środek masy, tensor, `Thrust[face]` — siła przeciwna do `Forward` silnika, moment, paliwo, fotele), 4–5 (`ShipModeSetOp` 20, `ShipModeIntent` 10, P7: „`ShipNotParked` dokłada K5-24”), 9 (`ShipStatsCache`, komenda `ship`), `ShipFixtures.Thirty` (30 bloków: 4 silniki, 2 zbiorniki, 1 napęd pulsacyjny, 4 podwozia).
- `docs/plans/K5-21-uklad-gwiezdny.md`: decyzje 5 (`StarSystemMath.GravityInPlanet`, `StarSystemModel.GravityAt`, grawitacja ramy układu = 0), 12 (DDL `ship_state (construct_id INTEGER PRIMARY KEY, data BLOB NOT NULL)` — K5-24 nie zmienia DDL), 16 (`ServerZoneTransitions` w fazie `Frames`; „K5-24 rejestruje lot **przed** nim w `GameComposition.Space.cs`”), 6 (atmosfera księżyca 600 m, opór na księżycu = dana K5-24).
- `docs/plans/K5-20-ramy-odniesienia.md`: decyzje 2–3 (`Vector3D`, `QuaternionD`, `PoseD`, `FrameMath.TryToFloat`, `FrameLimits`: układ ≤ 500 km), 6 (`UpdateConstructKinematics` tylko `Ship`, poza dziennikiem, nagłówek „zmieniony”), 7 (`FrameTransitions` przelicza prędkość), 8 (`ServerTickPhase.Frames`), 9 (`ServerFrameHistory`), P7 (gracze chodzą po zaparkowanym statku w ramie planety).
- `docs/plans/K5-22-wiele-terenow.md`: decyzje 4 (dzierżawy terenu), 6 (`IPlanetTerrainReadiness`: `Inactive`/`Streaming`/`Ready`).
- `docs/plans/K5-27-mapa-i-nawigacja.md`: decyzja 10–11 (`NavigationCoordinator.TryGetGuidance` dla HUD lotu).

Kod, na którym pakiet buduje (zweryfikowany w `F:/dev/horyzont-k3-15` i `F:/dev/horyzont-k4-19`; nazwy K5-20/21/23 z ich planów; krok 0 potwierdza na `main`):

| Co | Gdzie |
| --- | --- |
| `OperationKind` (1–7 + 20 z K5-23), `OperationCodec`, `IntentCodec` (prywatny `enum IntentCode`), `RejectReason` (0–21 + 30, 31, 37) | `src/Horyzont.Core/Operations/`, `Serialization/`, `Intents/RejectReason.cs` |
| `OperationApplier`, `IntentValidator`, `IntentResolver`, `GameRules`, `ServerSimulation` (`ApplyServerOperation`, `Applied`; K5-20 `UpdateConstructKinematics`; K5-23 konstruktor z `BlockContentTable`) | `src/Horyzont.Core/Simulation/` |
| `WorldState` (`ChangeTracker`, `CaptureCheckpoint`, `RequeueAfterFailedCheckpoint`), `WorldCheckpoint`, `WorldStateBuilder` | `src/Horyzont.Core/World/WorldState.cs`, `src/Horyzont.Core/Checkpoints/WorldCheckpoint.cs` |
| `StateHasher` (pomija pozę `Ship`) | `src/Horyzont.Core/Hashing/StateHasher.cs` |
| `BlobKind` (1–5, 10 K5-21, 11 K5-27), `StateCodec` | `src/Horyzont.Core/Serialization/` |
| `PersistenceWriter` (transakcja checkpointu), `WorldLoader`, `WorldCreator`, `Sql` | `src/Horyzont.Persistence/Writing/PersistenceWriter.cs`, `src/Horyzont.Persistence/` |
| Kolizja budowli: `ConstructChunkNode` — `StaticBody3D` per chunk z `ConcavePolygonShape3D` (kształt wklęsły — nie nadaje się do testu ruchu ciała) | `game/src/Constructs/ConstructChunkNode.cs:12,72`, `ChunkMeshUploader.cs:116-119` |
| `ConstructWorld`, `HostConstructs`, `ConstructNode` | `game/src/Constructs/` |
| `IServerTickParticipant`, `ServerRuntime.AddTickParticipant` (K5-20: wstawianie wg fazy) | `src/Horyzont.Server/` |
| Komendy konsoli serwera (`ship` z K5-23, `frame` z K5-20, `stats`/`digest` z K4-19) | `src/Horyzont.Server/Console/ConsoleCommandProcessor.cs` |
| K5-23: `BlockTypeDef`, `BlockFunction`, `BlockFunctionKind`, `BlockContentTable`, `ShipStats`, `ShipStatsCalculator`, `ShipStatsCache`, `ShipModeIntent` (walidator), `ShipFixtures.Thirty`, `blocks.json` format 2 | `src/Horyzont.Core/World/Blocks/`, `src/Horyzont.Core/Ships/`, `src/Horyzont.Server/Ships/`, `tests/Horyzont.Testing/Ships/`, `game/content/blocks/blocks.json` |
| K5-21: `StarSystemModel`, `StarSystemMath`, `StarSystemRules`, `ServerZoneTransitions`, `GameComposition.Space.cs`, `M0002_Space` (`ship_state`) | `src/Horyzont.Core/Space/`, `src/Horyzont.Server/Space/`, `game/src/Boot/`, `src/Horyzont.Persistence/Migrations/` |
| K5-20: `FrameMath`, `FrameLimits`, `FrameGraph`, `ServerFrameHistory`, `FrameNodes`, `ClientFrameGraph`, `TestShips` | `src/Horyzont.Core/Frames/`, `src/Horyzont.Server/Frames/`, `game/src/Shared/Frames/`, `src/Horyzont.Client/Frames/`, `tests/Horyzont.Testing/Ships/` |

## Zakres

1. **Core — model lotu (`src/Horyzont.Core/Ships/Flight/`):** `ShipControl`, `ShipButtons`, `FlightRules`, `ShipMotion`, `ShipFlightFlags`, `FlightEnvironment`, `ShipTerrainGate`, `ShipFlightModel.Step`, `AutolandController`, `ShipHullBoxes`; rozszerzenie `ShipStats` o ramę sterowania (`ControlFrame`).
2. **Core — stan i format:** `ShipStateRecord`, `ShipStateFlags`, `ShipStateStore` w `WorldState`, `ServerSimulation.UpdateShipState` (poza dziennikiem), `ShipFuelAddOp` (21), `RefuelIntent` (13), `RejectReason.ShipNotParked` (39), blob 12, `WorldCheckpoint`/`WorldStateBuilder`, `ShipLaunchBlock`, `ShipFlightStatus`; `BlockFunctionKind.RefuelStation = 10` (treść).
3. **Walidator:** `RefuelIntent`, `ShipNotParked` przy powrocie do `Static` (K5-23 P7), blokada startu na rezerwie.
4. **Persistence:** `ship_state` w transakcji checkpointu, odczyt przy wczytaniu.
5. **Serwer:** `ShipFlightSystem` (faza `Frames`, przed `ServerZoneTransitions`), `IShipControlSource` (+ brak pilota), `IShipCollider`, `IShipTerrainGate` (+ `HomeOnlyGate`), `ShipLoadSettler`, `DevTransitPilot` i komendy `ship pilot`, `ship fuel`.
6. **Gra:** `GodotShipCollider` (pośrednik z prostopadłościanów kadłuba), blok stacji tankowania w treści, `FlightHud` z `IClientShipStatus` (wariant „nieznany” do K5-26), partial `GameComposition.Flight.cs`.

## Poza zakresem

- Fotele, wsiadanie, wysiadanie, wyskok, blokada startu przez osoby w kadłubie (`PersonInHull`), `ShipInputBatch` (30), `ShipCorrection` (34), predykcja pilota, `NotPilot` (K5-25 — implementuje `IShipControlSource` i serializuje `ShipControl`).
- `ShipStatus` (31), migawki i zasięg statków w ramie układu (K5-26 — konsumuje `ShipFlightSystem.TryGetStatus` i implementuje `IClientShipStatus`).
- Aktywacja terenów i gotowość kolizji (K5-22 — dostarcza `IPlanetTerrainReadiness`; adapter do `IShipTerrainGate` pisze scalany później). Koszt paliwa i tankowanie z przedmiotów (podprojekt 3), uszkodzenia od zderzeń, autopilot po trasie dla graczy (spec :35 — `DevTransitPilot` jest wyłącznie narzędziem testów i `--dev`).
- Chodzenie po lecącym statku, osobne przestrzenie fizyki, statki w statkach.

## Fazy i zależność od niescalonych pakietów

| Faza | Kroki | Wymaga scalonych | Co daje |
| --- | --- | --- | --- |
| A | 0–7 | K5-21, K5-23 | kontrakty, model lotu, autolądowanie, przeloty w Core, stan statku, op 21, blob 12, zapis, walidator |
| B | 8–10 | K5-21, K5-23 | kolizje silnika, przebieg lotu na serwerze, odstawienie po wczytaniu, komendy, testy silnika |
| C | 11–12 | — (K5-27 opcjonalnie) | HUD lotu, odbiór |

## Mapa kryteriów → dowód

| # | Kryterium (źródło) | Dowód | Kategoria / job | Faza |
| --- | --- | --- | --- | --- |
| 1 | Przeloty z napędem ok. 50, 70 i 100 s (02:46–50, :249) | `ShipTransit_WithPulse_Moon50_B70_C100s` (Core: model + `DevTransitPilot` + `SphereGroundCollider`, układ z ziarna 0) — okna ±20 %; raport czasów bez napędu (ok. 1, 3, 7 min) | domyślna | A |
| 2 | Autolądowanie z 100 m, 1 km i 5 km; bez pilota schodzi pionowo i parkuje (02:87, :202, :228, :249) | `Autoland_From100m1km5km_LandsVerticallyAndParks` (Core) + `Autoland_HomeTerrain_From100m_Parks` (silnik) | domyślna + silnik | A, B |
| 3 | v ≤ clamp(h/τ, v_min, v_cap); połowienie wysokości trwa τ·ln 2 (02:77) | `ShipFlightModel_SpeedNeverExceedsLimit` (FsCheck) + `…_DescentAtLimit_HalvingTakesTauLn2` | domyślna | A |
| 4 | Wspomaganie: tłumi dryf i obroty, zawis gdy ciąg w dół > ciężar (02:76) | `ShipFlightModel_Assist_*` (3 testy) | domyślna | A |
| 5 | Paliwo ∝ ciąg, napęd szybciej, rezerwa 10 % tylko na lądowanie (02:78, :209) | `ShipFlightModel_Fuel_*` (4 testy) | domyślna | A |
| 6 | Opór tylko w atmosferze; zderzenia zatrzymują bez niszczenia (02:79) | `ShipFlightModel_Drag_*` + `GodotShipCollider_HitStopsWithoutBounce_BlocksUnchanged` | domyślna + silnik | A, B |
| 7 | Tankowanie przy stacji bez kosztu: `RefuelIntent` 13 → `ShipFuelAddOp` 21 (02:80, :150, :154) | `RefuelIntent_*` (6 testów) + `ShipFuelAddOp_*` (4) | domyślna | A |
| 8 | `ShipNotParked` (39) przy powrocie do `Static` (02:156) | `ShipModeIntent_ToStaticNotParked_RejectedShipNotParked` | domyślna | A |
| 9 | `ShipState` (blob 12) trwały; statek w ruchu zawsze „zmieniony”; skrót pomija `ShipState` (02:181–194) | `ShipState_CheckpointRestart_Restored`, `ShipStateBlob_GoldenBytesAndRejectsDamaged`, `StateHasher_ShipStateIgnored`, `ShipFuelAddOp_JournalReplay_SameHash` | domyślna | A |
| 10 | Po wczytaniu: niezaparkowany w strefie → odstawiony na teren i zaparkowany; w przestrzeni stoi z v = 0 (02:191, :227) | `ShipLoadSettler_UnparkedInZone_PlacedOnGroundAndParked` / `…_InSystem_StopsInPlace` | domyślna | B |
| 11 | Model lotu wspólny z predykcją: deterministyczny, bez alokacji (02:140) | `ShipFlightModel_SameInputs_BitwiseIdentical` + `…_Step_AllocatesZeroBytes` | domyślna | A |
| 12 | Teren niegotowy: nie niżej niż 2 R; kolizja niegotowa: zawis (02:207–208) | `ShipFlightModel_GateNotLoaded_StaysAbove2R` / `…_CollisionPending_NoDescent` | domyślna | A |

## Projekt

### Decyzje

1. **Sterowanie jako dane** (`ShipControl`, `readonly record struct`, Core): `sbyte ThrustX, ThrustY, ThrustZ` (ruch w osiach ramy sterowania: +X prawo, +Y góra, −Z przód), `sbyte Pitch, Yaw, Roll`, `ShipButtons Buttons` (`[Flags] byte`: `Assist = 1`, `PulseDrive = 2`, `LandingGear = 4`). Przyciski to **stany** („podwozie wysunięte”), nie zbocza — odporne na zgubione i powtórzone wejścia (K5-25 wysyła je w `ShipInputBatch` 1:1). Oś = wartość / 127, obcięta do [−1; 1].
2. **Rama sterowania.** Kierunki pilota są w osiach kokpitu: `ShipStats.ControlFrame` (kwaternion budowla → kokpit z orientacji pierwszego kokpitu po `CellPos`, `BlockOrientation.ToQuaternion`) — rozszerzenie typu K5-23 w `ShipStatsCalculator` (P2). Ciąg dostępny w danym kierunku = suma `Thrust[face]` (K5-23) rzutowana na oś ramy sterowania (ściany zgodne z osią; ukośne nie występują, bo orientacje są kątami prostymi).
3. **Zasady jako dane** (`FlightRules`, `sealed record` z wartościami domyślnymi; nie format): `TauSeconds = 8`, `MinSpeedLimit = 100`, `SpeedCap = 250`, `PulseSpeedCap = 2000`, `PulseResponseSeconds = 1`, `FuelPerMegaNewtonSecond = 4`, `PulseFuelPerSecond = 4`, `ReserveFraction = 0.1`, `ParkSpeed = 1`, `MaxTurnRate = 1.5` rad/s, `AssistLinearGain = 2` 1/s, `AssistAngularGain = 4` 1/s, `AtmosphereDragPerSecond` indeksowany `BodyPlacement.BodyIndex` (K5-21) `[0.05, 0, 0.05, 0.06]` (księżyc 0 — K5-21 decyzja 6; świat bez układu → indeks 0), `SurfaceClearanceMeters = 300` (najwyższa rzeźba generatora V1, przekazywana z gry; wszystkie ciała V1 mają profil planety domowej — K5-21 decyzja 1; nowy profil generatora przenosi tę wartość do danych ciała), `ReadinessTimeoutSeconds = 20` (README Y11), `NotLoadedFloorFactor = 2`, `CollisionPendingAltitude = 400` m, `SystemBoundaryMeters = 400 000` (wewnątrz `FrameLimits` układu 500 km), `GearContactMaxTilt = 0.7` (cos). Strojenie bez zmian kodu (P5).
4. **Stan ruchu** (`ShipMotion`, `struct` modyfikowany przez `ref`): `PoseD Pose` (poza początku ramy budowli w ramie rodzica, `double`), `Vector3 LinearVelocity` (rama rodzica), `Vector3 AngularVelocity` (osie budowli), `float Fuel`, `ShipFlightFlags Flags` (`[Flags] byte`: `Assist = 1`, `PulseEngaged = 2`, `GearDown = 4`, `Parked = 8`, `Reserve = 16`, `Autoland = 32`, `TerrainHold = 64` — bramka `NotLoaded`/`CollisionPending` ogranicza ruch, `TerrainUnavailable = 128` — decyzja 17; ustawiane przez przebieg serwera, model tylko je czyta). Obrót liczony wokół środka masy z `ShipStats` (poza początku przeliczana: `p_com = p + R·com`, po obrocie `p' = p_com − R'·com`).
5. **Otoczenie** (`FlightEnvironment`, liczy przebieg serwera i predykcja): `Vector3D Gravity` (rama rodzica: `StarSystemMath.GravityInPlanet` albo zero w układzie), `Vector3D Up` (od środka najbliższego ciała), `double Altitude` = `r − R − SurfaceClearanceMeters` najbliższego ciała (w ramie układu: minimum po ciałach ze `StarSystemModel`; ≥ 0), `double RadiusOfBody` (R), `double DistanceFromBody` (r), `float DragPerSecond` (w ramie planety wewnątrz `R + AtmosphereHeight`, liniowe wygaszanie w ostatnich 20 % grubości; poza = 0), `ShipTerrainGate Gate` (`Ready = 0`, `NotLoaded = 1`, `CollisionPending = 2`, `Unavailable = 3` — decyzja 17), `ShipLaunchBlock LaunchBlock` (przegląd K5, N14: jedyne wejście `Step` dla blokady startu; serwer liczy z rezerwy i — od K5-25 — z osób w obrysie, predykcja pilota bierze je z `ShipStatus`), `bool InSystemFrame`, `double SystemDistance` (odległość od początku układu). Świat bez układu (`StarSystem == null`): tylko planeta domowa, `BodyIndex` 0.
6. **Krok modelu** (`ShipFlightModel.Step(ref ShipMotion m, in ShipControl c, in ShipStats s, in FlightEnvironment e, FlightRules r, float dt) → FlightStepOutcome`; czysty, deterministyczny, bez alokacji, stała kolejność działań):
   1. Postój: `Parked` → prędkości = 0; wyjście z postoju tylko gdy `LandingGear` zwolnione i `e.LaunchBlock == None` (rezerwa blokuje start — decyzja 12); inaczej koniec kroku.
   2. Obroty: docelowa prędkość kątowa = osie × `MaxTurnRate` (bez wejścia przy `Assist` → 0); przyspieszenie ograniczone momentem `s.Torque` przez przekątną tensora; całkowanie `QuaternionD` z normalizacją.
   3. Ciąg: żądanie osi × dostępny ciąg w tym kierunku; `Assist` dla osi bez wejścia: ciąg tłumiący prędkość w tej osi (`AssistLinearGain`) plus kompensacja grawitacji w osi (zawis), obcięty do dostępnego ciągu — zawis tylko, gdy ciąg w dół > ciężar.
   4. Napęd pulsacyjny: `PulseDrive` wciśnięty, `s.PulseDriveCount > 0`, bez rezerwy → prędkość wzdłuż osi przodu dąży do `v_lim` ze stałą `PulseResponseSeconds` (napęd „trzyma limit”; bez tego czasy spec są nieosiągalne, bo `dv/dt = v/τ` przy 2 km/s to 250 m/s²) i zużywa `PulseFuelPerSecond`.
   5. Przyspieszenie: ciąg / masa + grawitacja − `DragPerSecond · v`; całkowanie półjawne Eulera (`v` potem pozycja, `double`).
   6. Ogranicznik: `v_lim = clamp(Altitude / τ, MinSpeedLimit, PulseEngaged ? PulseSpeedCap : SpeedCap)`; `|v| > v_lim` → skalowanie do `v_lim` (twardy limit — hamuje bez paliwa, P1). Bramka: `NotLoaded` i `r < 2 R` → składowa radialna do środka = 0; `CollisionPending` i `Altitude < CollisionPendingAltitude` → składowa do środka = 0 (zawis); `Unavailable` → składowa do środka = 0 na każdej wysokości (bezpieczny zawis albo bezruch, ścieżka błędu z decyzji 17); rezerwa → składowa od środka = 0 (tylko lądowanie); poza `SystemBoundaryMeters` → składowa od początku układu = 0.
   7. Paliwo: `−= Σ|ciąg użyty| · FuelPerMegaNewtonSecond · 1e−6 · dt` (+ napęd); `Fuel ≤ 0` → brak ciągu i napędu od następnego kroku; `Reserve = Fuel ≤ ReserveFraction · FuelCapacity`.
   Wynik: proponowana poza, użyty ciąg, `v_lim`. Model nie zna kolizji — rozwiązuje je przebieg (decyzja 9).
7. **Autolądowanie** (`AutolandController.Compute(in ShipMotion, in FlightEnvironment, in ShipStats, FlightRules) → ShipControl`, czyste): z grawitacją — poziomowanie osi góry kokpitu do `Up`, `Assist` + `LandingGear`, prędkość radialna docelowa `−min(v_lim, max(2, Altitude/τ))`, poziomej brak; bez grawitacji — `Assist` hamuje do zera, bez podwozia. Używa go przebieg, gdy `IShipControlSource` nie zwraca sterowania (brak pilota, utrata połączenia — spec :202).
8. **Pośrednik kadłuba** (`ShipHullBoxes.Compute(Construct) → ShipHullBox[]`, Core, czyste): zachłanne łączenie zajętych komórek w prostopadłościany osi budowli (najpierw X, potem Z, potem Y), ≤ 128 pudełek (więcej → łączenie grubsze, pokrycie nadmiarowe); płyty i skosy jako pełna komórka. Liczone przy wejściu w `Ship` i po zmianie bloków statku (`ShipStatsCache`), nie co tick.
9. **Kolizje** (`IShipCollider`, `src/Horyzont.Server/Ships/`): `ShipSweepResult Sweep(EntityId ship, in PoseD from, in PoseD to)` → (trafienie, ułamek, normalna w ramie rodzica). Gra: `GodotShipCollider` — jedno ukryte ciało na statek (warstwa 0, maska teren | budowle) z `BoxShape3D` z `ShipHullBoxes`, `PhysicsServer3D.BodyTestMotion` z wykluczeniem własnych ciał chunków (`ConcavePolygonShape3D` budowli nie nadaje się do testu ruchu). Odpowiedź: przesunięcie do bezpiecznego ułamka, usunięcie składowej prędkości w normalną (niesprężyście), składowa styczna × 0,5; trafienie z normalną `· Up ≥ GearContactMaxTilt`, podwozie wysunięte i `|v| < ParkSpeed` → `Parked`. Gracze nie są w masce (blokadę startu daje K5-25). Bloki nigdy nie są niszczone. Atrapa testów: `SphereGroundCollider` (`tests/Horyzont.Testing/Ships/`) — kula promienia R (+ opcjonalna funkcja wysokości) na każde ciało.
10. **Przebieg na serwerze** (`ShipFlightSystem`, `src/Horyzont.Server/Ships/`, `IServerTickParticipant`, faza `Frames`, rejestrowany **przed** `ServerZoneTransitions`): co tick dla budowli `Mode == Ship` po id: `ShipStats` z `ShipStatsCache`; `ShipMotion` z pamięci przebiegu, a gdy rama, `ParentSeq` albo zapisana poza `float` budowli różni się od ostatnio zapisanej (przepięcie strefy, `ShipModeSetOp`, odstawienie) — odtwarzany z nagłówka budowli i `ShipStateRecord` (poza `double` żyje tylko w przebiegu; `float` w nagłówku przez `FrameMath.TryToFloat`); sterowanie z `IShipControlSource.TryGetControl(ship, tick, out control)` albo `AutolandController`; otoczenie ze `StarSystemModel`, rejestru planet i `IShipTerrainGate`; `Step`; `IShipCollider.Sweep`; `ServerSimulation.UpdateConstructKinematics` (gdy poza się zmieniła) i `ServerSimulation.UpdateShipState`. Bez alokacji po rozgrzaniu (test przy 8 statkach). Prędkość w ramie rodzica nie zmienia się przy przepięciu planeta ↔ układ (ramy w EA są nieruchome i mają te same osie — test).
11. **`ShipState` w Core** (`src/Horyzont.Core/World/Ships/`): `readonly record struct ShipStateRecord(EntityId Construct, float Fuel, Vector3 LinearVelocity, Vector3 AngularVelocity, ShipStateFlags Flags)`, `[Flags] enum ShipStateFlags : byte { Parked = 1, GearDown = 2, Assist = 4 }`; `ShipStateStore` (`SortedDictionary`, mutacje `internal`) w `WorldState.ShipStates`. `ServerSimulation.UpdateShipState(EntityId, in ShipStateRecord) → bool` poza dziennikiem: tylko `Mode == Ship`, wartości skończone, `Fuel ≥ 0`; oznacza `ChangeTracker`. **Nie** w skrócie (spec :194 — `StateHasher` bez zmian, test). Przejście `Ship → Static` zostawia rekord (paliwo w zbiornikach), przebieg zeruje prędkości. Rekordy budowli, które przestały istnieć, usuwa `CaptureCheckpoint` (`RemovedShipStates`) — bez zmiany appliera istniejących rodzajów operacji.
12. **Blokada startu** (`ShipLaunchBlock : byte { None = 0, FuelReserve = 1, PersonInHull = 2 }`, Core/Ships; wartość 2 nadaje K5-25): K5-24 ustawia `FuelReserve`, gdy statek stoi na postoju z paliwem w rezerwie. `ShipFlightStatus(float Fuel, float FuelCapacity, float SpeedLimit, ShipFlightFlags Flags, ShipLaunchBlock LaunchBlock)` (Core/Ships) i `ShipFlightSystem.TryGetStatus(EntityId, out ShipFlightStatus)` — konsument K5-26 (`ShipStatus` 31).
13. **`ShipFuelAddOp(EntityId Construct, float Units, float Capacity)`, kod 21.** Warunki wstępne appliera **tylko na stanie objętym skrótem**: budowla istnieje, `Mode == Ship`, `Units` skończone > 0, `Capacity` skończona ≥ 0 (nigdy „zbiornik niepełny” — paliwo nie jest w skrócie, więc warunek zależny od paliwa rozjechałby powtórkę). Skutek: rekord (gdy brak: `Parked | GearDown`, paliwo 0, prędkości 0), `Fuel = MathF.Min(Fuel + Units, Capacity)`, `ChangeTracker`. Kodek: rekord wersji 1 `[Construct u64][Units f32][Capacity f32]`; rodzaje 1–7, 20, 22, 23 bez zmian.
14. **`RefuelIntent(EntityId Ship, EntityId Station, CellPos StationCell, Vector3 RayOrigin, Vector3 RayDirection)`, kod 13.** Walidator (kolejność): budowla stacji istnieje i stoi na planecie gracza (`UnknownConstruct`, `WrongPlanet`); komórka w zakresie i zajęta (`CellOutOfBounds`, `CellEmpty`); funkcja bloku `RefuelStation` (`InvalidParameters`); oko serwera, zasięg i linia wzroku jak dla kokpitu w K5-23 (`RayOriginMismatch`, `OutOfReach`, `NoLineOfSight`); statek istnieje i `Mode == Ship` (`UnknownConstruct`/`InvalidParameters`); statek na postoju (**`ShipNotParked`**); odległość od środka komórki stacji do sfery statku ≤ `RefuelRangeMeters = 15` w ramie planety stacji (`FrameGraph`) (`OutOfReach`); paliwo < pojemność − 0,5 (`InvalidParameters`); kubełek bloków (`RateLimited`). Resolver: `Units = Capacity`, `Capacity = ShipStats.FuelCapacity` (przegląd K5, N16: skutek `min(Fuel + Capacity, Capacity) = Capacity` nie zależy od paliwa w chwili powtórki — po awarii ogon dziennika daje pełny zbiornik niezależnie od tego, z którego checkpointu pochodzi paliwo; format op bez zmian). Do podprojektu 3 bez kosztu (spec :80). Nowy blok treści `core:ship_refuel_station` (masa 1500 kg, `function: {"kind": "refuel_station"}`, `BlockFunctionKind.RefuelStation = 10` — treść, nie format; materiał `ship_engine`; nazwy pl/en).
15. **`ShipNotParked` (39).** Walidator `ShipModeIntent` z celem `Static` odrzuca statek bez flagi `Parked` (K5-23 P7); także `RefuelIntent`. Walidator nie jest ścieżką formatu.
16. **Blob 12 i zapis.** `[BlobKind=12][format=1][Construct u64][Fuel f32][lin 3×f32][ang 3×f32][Flags u8]` (39 B); dekodowanie odrzuca NaN/∞, `Fuel < 0`, nieznane bity flag (`ProtocolViolationException` → `WorldCorruptedException`). `WorldCheckpoint` + `UpsertedShipStates`, `RemovedShipStates` (parametry z domyślnymi pustymi wartościami na końcu rekordu); `PersistenceWriter` w transakcji checkpointu: `INSERT … ON CONFLICT DO UPDATE` i `DELETE` na `ship_state`; `WorldLoader` czyta tabelę po `construct_id` (wiersz budowli nieistniejącej → pominięty z Warning); `WorldStateBuilder.AddShipState`. Statek, który się poruszył od checkpointu, jest „zmieniony” (nagłówek z pozą i `ShipState` — spec :190).
17. **Bramka terenu** (`IShipTerrainGate`, `src/Horyzont.Server/Ships/`; przegląd K5, B3 — definicja zachowawcza, README Y11): `ShipTerrainGate Query(EntityId ship, PlanetId planet, in ShipMotion motion, in ShipStats stats)`. Geometria: bramka pyta o **kolumnę pod statkiem**, nie o jego środek — `d = min(Altitude, 2 s · |v| + 64 m)`, środek zapytania na promieniu pod statkiem w wysokości `max(R + MaxRelief, r − d/2)`, promień = promień sfery statku + `d/2`. Gotowość = **kolizja zbudowana** (K5-22 decyzja 6), nigdy samo wczytanie danych. Odwzorowanie `IPlanetTerrainReadiness` → bramka:
   - `Ready` → `Ready`;
   - `Inactive`/`Streaming` i `Altitude ≥ 2 s · v_lim + CollisionPendingAltitude` → `NotLoaded` (nie niżej niż 2 R);
   - `Streaming` niżej → `CollisionPending` (zawis);
   - `Faulted` od razu albo brak `Ready` przez `ReadinessTimeoutSeconds` (20 s, licznik per statek w ticki serwera, kasowany przy `Ready`, zmianie planety albo postoju) → `Unavailable`.
   **Ścieżka błędu (`Unavailable`):** statek nie schodzi na żadnej wysokości (zawis z `Assist`, w próżni bezruch), flaga `ShipFlightFlags.TerrainUnavailable` (w `ShipFlightStatus` → HUD „Teren niedostępny”, K5-26 przenosi ją w `ShipStatus`), wpis Warning `[LoggerMessage]` najwyżej raz na 10 s na statek (statek, planeta, stan gotowości, czas oczekiwania, statystyki terenu z K5-22), zapotrzebowanie na teren trwa (statek niezaparkowany — K5-22 decyzja 2b), a bramka co 5 s ponawia zapytanie i wraca do zwykłego odwzorowania po pierwszym `Ready`. Pilot może odlecieć w górę albo poza strefę. Domyślnie `HomeOnlyGate`: planeta domowa → `Ready`; inne → `NotLoaded` (przed K5-22 ciała 1–3 nie mają terenu — statek nie zejdzie poniżej 2 R). Adapter `PlanetTerrainShipGate` i podmianę `IShipDemandSource` na `UnparkedShipDemand` („`Mode == Ship` ∧ ¬`Parked`”) pisze pakiet scalany później z pary K5-22/K5-24. Lot nie bierze dzierżaw terenu.
18. **Odstawienie po wczytaniu** (`ShipLoadSettler`, Server, przed pierwszym krokiem lotu każdego statku): `ShipStateRecord` bez `Parked` w ramie planety → statek trzymany (v = 0, poza bez zmian; jako niezaparkowany sam żąda terenu — decyzja 17) do `Gate == Ready` dla kolumny od statku do gruntu (`d = Altitude + 10 m`; `Unavailable` → statek zostaje w miejscu z v = 0, flagą `TerrainUnavailable` i logiem jak w decyzji 17, ponowienie co 5 s), potem `Sweep` od pozy do pozy przesuniętej o `Altitude + 10 m` w dół → poza w miejscu trafienia, `Parked | GearDown`, v = 0; w ramie układu → v = 0, bez postoju. Zmiany poza dziennikiem (`UpdateConstructKinematics`, `UpdateShipState`).
19. **Narzędzia deweloperskie** (tylko `--dev`): `DevTransitPilot` (`src/Horyzont.Server/Ships/`, czysty sterownik: wznoszenie pionowe, obrót na cel, napęd, hamowanie limitem, autolądowanie; wejście: ciało docelowe; używają go testy przelotów, K5-26 i K5-28) wpięty jako `IShipControlSource` przez `DevControlSource`; komendy `ship pilot <id> <planet:id|stop>` i `ship fuel <id> <units>` (operacja serwera 21); linia `ship <id>` z K5-23 rozszerzona o `fuel=… v=… limit=… parked=… gate=…` (format stały).
20. **HUD lotu** (`game/src/Client/Ships/FlightHud.cs`): widoczny, gdy własny byt jest w ramie budowli `Ship`; wysokość (`ClientFrameGraph` + `ClientStarSystem`, najbliższe ciało), prędkość (poza statku z historii klienta), limit (`FlightRules`; napęd z `IClientShipStatus`), paliwo % z ostrzeżeniem przy 25 % i rezerwie, powód blokady startu, kierunek do celu z `NavigationCoordinator.TryGetGuidance` (K5-27; integruje scalany później). `IClientShipStatus` (`src/Horyzont.Client/Ships/`: `bool TryGet(EntityId ship, out ShipFlightStatus status)`) — domyślnie `UnknownShipStatus` (pola „—”); implementację z `ShipStatus` (31) daje K5-26. Tekst przez bufor `char[]`, bez alokacji w klatce.
21. **Protokół.** `OperationApplied`, `IntentSubmit`, `IntentReply` niosą nowe wartości → `ProtocolVersion` = (main przy scaleniu) + 1 (D4); fixture X6 (`CodecGen`, `MaxMessages`, `ProtocolContractTests`) dla op 21 i intencji 13; router bez zmian.

### Pliki

```
src/Horyzont.Core/Ships/Flight/      ShipControl, ShipButtons, FlightRules, ShipMotion, ShipFlightFlags, FlightEnvironment,
                                     ShipTerrainGate, ShipFlightModel, FlightStepOutcome, AutolandController, ShipHullBoxes, ShipHullBox
src/Horyzont.Core/Ships/             ShipStats (+ ControlFrame), ShipStatsCalculator, ShipLaunchBlock, ShipFlightStatus
src/Horyzont.Core/World/Ships/       ShipStateRecord, ShipStateFlags, ShipStateStore
src/Horyzont.Core/World/Blocks/BlockFunctionKind.cs       + RefuelStation = 10
src/Horyzont.Core/World/WorldState.cs, Checkpoints/WorldCheckpoint.cs, Checkpoints/WorldStateBuilder.cs
src/Horyzont.Core/Operations/        ShipFuelAddOp, OperationKind (+ ShipFuelAdd = 21)
src/Horyzont.Core/Intents/           RefuelIntent, RejectReason (+ ShipNotParked = 39)
src/Horyzont.Core/Simulation/        OperationApplier (21), IntentValidator, IntentResolver, ServerSimulation (+ UpdateShipState), GameRules
src/Horyzont.Core/Serialization/     OperationCodec, IntentCodec (13), BlobKind (+ ShipState = 12), StateCodec
src/Horyzont.Core/Protocol/ProtocolConstants.cs           ProtocolVersion (D4)
src/Horyzont.Constructs/Content/BlockTypeCatalog.cs       rodzaj funkcji "refuel_station"
src/Horyzont.Persistence/Writing/PersistenceWriter.cs, WorldLoader.cs    ship_state
src/Horyzont.Server/Ships/           ShipFlightSystem, IShipControlSource, NoPilotControlSource, DevControlSource, DevTransitPilot,
                                     IShipCollider, ShipSweepResult, IShipTerrainGate, HomeOnlyGate, ShipLoadSettler, ShipFlightLog
src/Horyzont.Server/Console/ConsoleCommandProcessor.cs    ship pilot / ship fuel / rozszerzona linia ship
src/Horyzont.Client/Ships/           IClientShipStatus, UnknownShipStatus
game/src/Ships/GodotShipCollider.cs, game/src/Client/Ships/FlightHud.cs
game/content/blocks/blocks.json, game/locale/blocks.csv, game/locale/ui.csv (HUD, odmowa 39)
game/src/Boot/GameComposition.Flight.cs (nowy partial), GameComposition.Space.cs (1 linia przed ServerZoneTransitions)
tests/Horyzont.Testing/Ships/        SphereGroundCollider, ScriptedControlSource; CodecGen (21, 13)
tests/Horyzont.Core.Tests/Ships/Flight/  ShipFlightModelTests, AutolandTests, ShipTransitTests, ShipHullBoxesTests
tests/Horyzont.Core.Tests/Ships/     ShipFuelTests, RefuelIntentTests, ShipStateHashTests
tests/Horyzont.Core.Tests/Serialization/ShipStateBlobTests.cs, …/FlightCodecGoldenTests.cs
tests/Horyzont.Persistence.Tests/ShipStatePersistenceTests.cs
tests/Horyzont.Server.Tests/Ships/   ShipFlightSystemTests, ShipLoadSettlerTests
game/tests/Ships/                    GodotShipColliderTests, AutolandEngineTests
```

## Testy akceptacyjne (napisz najpierw)

| Test | Plik | Kategoria | Faza | Co sprawdza |
| --- | --- | --- | --- | --- |
| `OperationCodec_ShipFuelAdd_Golden` / `IntentCodec_Refuel_Golden` / `RejectReason_ShipNotParked_Golden` / `ShipStateBlob_GoldenBytesAndRejectsDamaged` | `tests/Horyzont.Core.Tests/Serialization/FlightCodecGoldenTests.cs`, `ShipStateBlobTests.cs` | domyślna | A | bajty zatwierdzone przez Opus (krok 1); odrzucenia z decyzji 16 |
| `ShipFlightModel_SameInputs_BitwiseIdentical` / `…_Step_AllocatesZeroBytes` | `tests/Horyzont.Core.Tests/Ships/Flight/ShipFlightModelTests.cs` | domyślna | A | 10 000 kroków z losowym sterowaniem dwa razy = te same bity; 0 B |
| `ShipFlightModel_SpeedNeverExceedsLimit` (FsCheck) / `…_DescentAtLimit_HalvingTakesTauLn2` | jw. | domyślna | A | `|v| ≤ v_lim + 1e−3`; zejście 8 km → 1 km: czas = τ·ln 8 ± 5 % |
| `ShipFlightModel_Assist_DampsDriftAndRotation` / `…_HoversWhenDownThrustExceedsWeight` / `…_NoHoverWhenThrustInsufficient` | jw. | domyślna | A | decyzja 6.2–6.3 |
| `ShipFlightModel_Fuel_ProportionalToThrust` / `…_PulseConsumesFaster` / `…_ReserveOnlyLandingAndBlocksLaunch` / `…_EmptyNoThrust` | jw. | domyślna | A | decyzje 6.7, 12 |
| `ShipFlightModel_Drag_OnlyInsideAtmosphere_MoonNone` / `…_GateNotLoaded_StaysAbove2R` / `…_CollisionPending_NoDescent` / `…_GateUnavailable_NoDescentAnyAltitude` / `…_LaunchBlockInEnvironment_KeepsParked` / `…_SystemBoundary_NoOutwardMotion` | jw. | domyślna | A | decyzje 5–6, 17, N14 |
| `ShipTerrainGate_ColumnGeometry_QueriesGroundUnderShip` / `…_StreamingBeyondTimeout_Unavailable` / `…_Faulted_UnavailableImmediately` / `…_ReadyAgain_Recovers` / `…_Unavailable_LogsAtMostEvery10s` | `tests/Horyzont.Server.Tests/Ships/ShipTerrainGateTests.cs` | domyślna | B | decyzja 17 na atrapie `IPlanetTerrainReadiness` (gdy K5-22 nie jest na `main` — atrapa w `tests/Horyzont.Testing/Ships/`) |
| `ShipTransit_WithPulse_Moon50_B70_C100s` / `ShipTransit_WithoutPulse_Report` | `tests/Horyzont.Core.Tests/Ships/Flight/ShipTransitTests.cs` | domyślna | A | `ShipFixtures.Thirty` z paliwem, `DevTransitPilot`, `SphereGroundCollider`, `StarSystemGenesis` ziarno 0: czas od startu z powierzchni domowej do postoju na celu w [40; 60], [56; 84], [80; 120] s; bez napędu — liczby w raporcie |
| `Autoland_From100m1km5km_LandsVerticallyAndParks` / `…_ZeroGravity_StopsToZero` | `…/Flight/AutolandTests.cs` | domyślna | A | dryf poziomy ≤ 5 m, `Parked`, v = 0; w próżni |v| → 0 |
| `ShipHullBoxes_GreedyMerge_CoversCellsExactly` / `…_Max128Boxes` | `…/Flight/ShipHullBoxesTests.cs` | domyślna | A | pokrycie = zbiór komórek (bez nadmiaru do 128) |
| `ShipFuelAddOp_Preconditions_*` / `…_ClipsToCapacity` / `…_JournalReplay_SameHash` / `StateHasher_ShipStateIgnored` | `tests/Horyzont.Core.Tests/Ships/ShipFuelTests.cs`, `ShipStateHashTests.cs` | domyślna | A | decyzje 11, 13; korpus złoty i `DeterminismTests` bez zmian w plikach |
| `RefuelIntent_NotStation_Rejected` / `…_ShipNotParked_Rejected39` / `…_TooFar_OutOfReach` / `…_TankFull_Rejected` / `…_OutOfReachOrNoLineOfSight` / `…_Accepted_ResolvesFullTankOp` | `tests/Horyzont.Core.Tests/Ships/RefuelIntentTests.cs` | domyślna | A | decyzja 14 |
| `ShipModeIntent_ToStaticNotParked_RejectedShipNotParked` | jw. | domyślna | A | decyzja 15 |
| `ShipState_CheckpointRestart_FuelVelocityFlagsRestored` / `…_ConstructRemoved_RowDeletedAtCheckpoint` / `…_FailedCheckpoint_Requeued` | `tests/Horyzont.Persistence.Tests/ShipStatePersistenceTests.cs` | domyślna | A | decyzja 16 |
| `ShipFlightSystem_RegisteredBeforeZoneTransitions` / `…_NoPilot_Autolands` / `…_ReparentKeepsVelocity` / `…_EightShips_TickAllocatesZero` / `…_HomeOnlyGate_NoDescentBelow2RElsewhere` | `tests/Horyzont.Server.Tests/Ships/ShipFlightSystemTests.cs` | domyślna | B | decyzje 10, 17 (atrapa zderzacza) |
| `ShipLoadSettler_UnparkedInZone_PlacedOnGroundAndParked` / `…_InSystem_StopsInPlace` / `…_WaitsForGateReady` / `…_GateUnavailable_HeldInPlaceAndRetries` | `tests/Horyzont.Server.Tests/Ships/ShipLoadSettlerTests.cs` | domyślna | B | decyzja 18 |
| `GodotShipCollider_HitsTerrainAndConstructs_ExcludesOwnChunks` / `…_HitStopsWithoutBounce_BlocksUnchanged` | `game/tests/Ships/GodotShipColliderTests.cs` | silnik | B | decyzja 9 |
| `Autoland_HomeTerrain_From100m_Parks` | `game/tests/Ships/AutolandEngineTests.cs` | silnik | B | prawdziwy teren planety domowej, kolizje Jolt |
| `Manual_Flight_HomeToMoon_InGame` | procedura w PR | `Manual` | B | host `--dev`: statek z 30 bloków, `ship fuel`, `ship pilot <id> planet:<księżyc>`, obserwacja, `ship <id>`; HUD lotu; zrzuty |

## Kroki

Każdy krok: test czerwony → kod → zielone testy → mały commit (Conventional Commits). Wyszukiwanie kodu i uruchamianie testów zlecać subagentowi Haiku (CLAUDE.md projektu).

**Faza A — Core**

- [ ] 0. Warunki wejścia: `git status`, worktree `F:/dev/horyzont-k5-24` od `origin/main` po scaleniu K5-21 i K5-23; w „Odstępstwach” zapisać scalone pakiety i rzeczywiste nazwy (`ShipStats` i jego pola, `ShipStatsCache`, `BlockFunctionKind`, `UpdateConstructKinematics`, `ServerTickPhase`, `ServerZoneTransitions` i miejsce jego rejestracji, `StarSystemModel`, `WorldCheckpoint`, plik tekstów odmów HUD); sprawdzić, że op 21, intencja 13, odmowa 39 i blob 12 są wolne na `main` i w otwartych PR K5; czy K5-22 jest scalony (wtedy krok 9 pisze też adapter bramki). Commit `docs:`.
- [ ] 1. **Kontrakt do zatwierdzenia przez Opus przed kodem:** rekordy `ShipFuelAddOp`, `RefuelIntent`, `ShipStateRecord`, `ShipControl`, `ShipLaunchBlock`, `ShipFlightStatus`, sygnatura `ShipFlightModel.Step`, wartości enumów, układ bajtów (testy złote czerwone). Commit `test:`.
- [ ] 2. Po akceptacji: kodeki (op 21, intencja 13, blob 12), `RejectReason.ShipNotParked`, fixture X6, `ProtocolVersion` (D4) z aktualizacją `ProtocolVersion_MatchesDocumentedMergeValue` i `Constants_GoldenValues`. Commity `test:`, `feat:`.
- [ ] 3. `ShipControl`, `FlightRules`, `ShipMotion`, `FlightEnvironment`, `ShipFlightModel` (testy właściwości, determinizmu i alokacji najpierw), `ShipStats.ControlFrame`. Commity `test:`, `feat:`.
- [ ] 4. `AutolandController`, `DevTransitPilot`, `SphereGroundCollider`, `ScriptedControlSource`; testy autolądowania i przelotów (raport czasów w PR; nieosiągnięte okna → strojenie danych `FlightRules`, nie zmiana asercji). Commity `test:`, `feat:`.
- [ ] 5. `ShipStateRecord`/`Store`, `ServerSimulation.UpdateShipState`, applier `ShipFuelAddOp`, `StateHasher_ShipStateIgnored`; `git diff` pusty na korpusie złotym i `DeterminismTests`. Commity `test:`, `feat:`.
- [ ] 6. `WorldCheckpoint`/`CaptureCheckpoint`/`RequeueAfterFailedCheckpoint`, `WorldStateBuilder.AddShipState`, `PersistenceWriter` i `WorldLoader` dla `ship_state`; testy zapisu. Commity `test:`, `feat:`.
- [ ] 7. Walidator i resolver `RefuelIntent`, `ShipNotParked` w `ShipModeIntent`, `BlockFunctionKind.RefuelStation` + parser `blocks.json` + blok `core:ship_refuel_station` + nazwy i tekst odmowy 39; `ShipHullBoxes`. Testy. Commity `test:`, `feat:`.

**Faza B — serwer i silnik**

- [ ] 8. `IShipCollider`, `GodotShipCollider` (pośrednik z pudełek, `BodyTestMotion`, wykluczenia) + testy silnika. Commity `test:`, `feat:`.
- [ ] 9. `ShipFlightSystem`, `IShipControlSource` (+ `NoPilotControlSource`, `DevControlSource`), `IShipTerrainGate` (geometria kolumny, limit czasu, `Unavailable` — decyzja 17, kontrakt zatwierdza Opus razem z K5-22 wg README Y11) + `HomeOnlyGate` (+ `PlanetTerrainShipGate` i `UnparkedShipDemand`, jeśli K5-22 scalony), testy `ShipTerrainGate_*`, `ShipLoadSettler`, `TryGetStatus`, komendy `ship pilot`/`ship fuel`; `GameComposition.Flight.cs` i jedna linia w `GameComposition.Space.cs` przed rejestracją `ServerZoneTransitions` (także serwer dedykowany); testy `ShipFlightSystem_*`, `ShipLoadSettler_*`. Commity `test:`, `feat:`.
- [ ] 10. `Autoland_HomeTerrain_From100m_Parks` (job `game`), `Manual_Flight_HomeToMoon_InGame` (do K5-22 lądowanie na księżycu kończy się zawisem na 2 R — opisać w PR); `python tools/engine/build.py smoke`. Commit `test:`.

**Faza C — HUD i odbiór**

- [ ] 11. `IClientShipStatus` + `UnknownShipStatus`, `FlightHud` (wysokość, prędkość, limit, paliwo, blokada, kierunek do celu, jeśli K5-27 scalony); teksty `ui.csv`. Commity `test:`, `feat:`.
- [ ] 12. Rebase na `main`; pełne komendy odbioru; `docs/ZASADY.md` pkt 2: „lot, zużycie paliwa i pozy statków poza dziennikiem — wyłącznie `UpdateConstructKinematics`/`UpdateShipState` z `ShipFlightSystem`”, pkt 4: „warunki wstępne `ShipFuelAddOp` nie czytają `ShipState` (poza skrótem)”, pkt 3: wiersz `src/Horyzont.Core/Ships/Flight/` („model lotu wspólny z predykcją pilota; zmiana zachowania → opis w PR i `ProtocolVersion`, bo predykcja klienta i serwer muszą liczyć tak samo”); wiersz K5-24 w sekcji „Pakiety K5” `docs/plans/README.md`; PR z raportem czasów przelotów, odstępstwami i pytaniami. Commit `docs:`.

## Odbiór

```
dotnet build Horyzont.sln
dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"
dotnet format Horyzont.sln --verify-no-changes
python tools/engine/build.py smoke
```

Plus testy silnika `game/tests/Ships/` (job `game`), w PR: tabela czasów przelotów z napędem i bez (`ShipTransit_*`), wynik autolądowania z trzech wysokości, zrzuty `Manual_Flight_HomeToMoon_InGame` i `git diff origin/main -- tests/Horyzont.Core.Tests/Golden/TerrainJournals tests/Horyzont.Core.Tests/TerrainJournalGoldenCorpusTests.cs tests/Horyzont.Core.Tests/Hashing/DeterminismTests.cs` = pusty.

## Kody i punkty styku

Kody zajęte przez K5-24 (spec 02:150–156, :181): **`OperationKind.ShipFuelAdd = 21`**, **`IntentCode` 13 (`Refuel`)**, **`RejectReason.ShipNotParked = 39`**, **`BlobKind.ShipState = 12`**. Bez komunikatów i migracji (DDL `ship_state` z M0002 K5-21). Wartości poza formatem: `BlockFunctionKind.RefuelStation = 10` (treść), `ShipLaunchBlock` 0–1 (2 = `PersonInHull` nadaje K5-25).

| Rodzaj | Kod | Nazwa | Pakiet |
| --- | --- | --- | --- |
| `OperationKind` | 20 / 21 / 22 / 23 / 24–29 | `ShipModeSet` / `ShipFuelAdd` / `MarkerSet` / `MarkerRemove` / wolne | K5-23 / **K5-24** / K5-27 / K5-27 / — |
| `IntentCode` | 10 / 11 / 12 / 13 / 14 / 15 / 16 / 17–19 | `ShipMode` / `SeatEnter` / `SeatExit` / `Refuel` / `MarkerSet` / `MarkerRemove` / `SeatEvict` / wolne | K5-23 / K5-25 / K5-25 / **K5-24** / K5-27 / K5-27 / K5-25 / — |
| `RejectReason` | 30, 31, 37 / 32–35, 38 / 36 / 39 | `NoCockpit`, `ShipAnchored`, `NoEngine` / `SeatOccupied`, `SeatOutOfReach`, `EjectTooFast`, `NotPilot`, `ConstructNotEmpty` / `MarkerLimit` / `ShipNotParked` | K5-23 / K5-25 / K5-27 / **K5-24** |
| `MessageType` | 30 / 31 / 32 / 33 / 34 / 35 / 36–39 | `ShipInputBatch` / `ShipStatus` / `StarSystemState` / `MarkersState` / `ShipCorrection` / `MapPresence` (warunkowo, K5-27 P1) / wolne | K5-25 / K5-26 / K5-21 / K5-27 / K5-25 / K5-27 / — |
| `BlobKind` | 10 / 11 / 12 / 13–19 | `StarSystemParams` / `Marker` / `ShipState` / wolne | K5-21 / K5-27 / **K5-24** / — |
| Migracja | M0002 | `M0002_Space` | K5-21 |

Kolejność scalania K5: **(K5-23 ↔ K5-20) → K5-21 → {K5-22, K5-24, K5-27} → {K5-25, K5-26} → K5-28**; rekomendacja K5-22 → K5-24, gdy oba gotowe. Zasada X10.

| Plik / punkt | K5-24 | Inny pakiet | Rozwiązanie |
| --- | --- | --- | --- |
| `OperationKind`, `OperationCodec`, `OperationApplier.cs` | 21 | K5-23 (20), K5-27 (22, 23) | przypadki obok siebie w kolejności numerów; scalany później |
| `IntentCodec`, `Intents/`, `IntentValidator.cs`, `IntentResolver.cs`, `GameRules` | 13, `ShipNotParked` w `ShipModeIntent` | K5-23 (10), K5-25 (11, 12, 16), K5-27 (14, 15) | jw. |
| `RejectReason` | 39 | 30, 31, 37 / 32–35, 38 / 36 | jw. |
| `BlobKind`, `StateCodec` | 12 | 10 (K5-21), 11 (K5-27) | jw. |
| `WorldState.cs`, `WorldCheckpoint.cs`, `WorldStateBuilder.cs`, `PersistenceWriter.cs`, `WorldLoader.cs` | `ShipStates` | K5-27 znaczniki | sąsiednie pola i bloki; scalany później |
| `ShipStats`/`ShipStatsCalculator` (K5-23) | + `ControlFrame` | K5-25 (fotele) | dopisanie pola; scalany później |
| `GameComposition.Space.cs` (K5-21) | linia rejestracji lotu **przed** `ServerZoneTransitions` | K5-21 | K5-24 scalany po K5-21 |
| `IShipTerrainGate` ↔ `IPlanetTerrainReadiness` (K5-22) | bramka (kolumna, limit czasu, `Unavailable`) + `HomeOnlyGate` | K5-22 (`Ready` = kolizja zbudowana, `Faulted`) | scalany później pisze `PlanetTerrainShipGate` (README Y11) |
| `IShipDemandSource` (K5-22) | `UnparkedShipDemand` | K5-22 `MovingShipDemand` | scalany później podmienia implementację |
| `IShipControlSource`, `ShipControl` | interfejs i dane | K5-25 (`ShipInputBatch`, pilot, `NotPilot`) | K5-25 implementuje; zmiana `ShipControl` = zmiana kontraktu (Opus) |
| `ShipFlightSystem.TryGetStatus`, `IClientShipStatus` | API | K5-26 (`ShipStatus` 31, `ClientShipStatus`) | K5-26 scalany po K5-24 |
| `FlightHud` ↔ `NavigationCoordinator.TryGetGuidance` (K5-27) | konsument | K5-27 | scalany później dopina wywołanie |
| `ShipLaunchBlock` | 0, 1 | K5-25 (2 `PersonInHull`) | K5-25 dopisuje wartość i podbija `ProtocolVersion` |
| `ProtocolConstants.ProtocolVersion` + dwa testy | podbicie | wszystkie K5 z formatem | D4 |
| `docs/ZASADY.md`, `docs/plans/README.md` | wiersze K5-24 | wszyscy | scalany później dopisuje obok |

## Ryzyka

| Ryzyko | Skutek | Odpowiedź |
| --- | --- | --- |
| Wklęsłe kształty kolizji budowli w teście ruchu Jolt | brak kolizji statku | pośrednik z pudełek (decyzja 8–9) |
| Przyspieszenie potrzebne do śledzenia limitu (250 m/s² przy 2 km/s) | czasy przelotu poza spec | napęd pulsacyjny „trzyma limit” (decyzja 6.4); test czasów |
| Twardy limit hamuje bez paliwa | odczucie „magicznego hamulca” | zgodne ze spec („nie przekracza”); P1 |
| Dryf `float` przy zaokrąglaniu pozy co tick w ramie układu (~1 cm przy 115 km) | rozjazd pozy | poza `double` w przebiegu; `float` tylko do nagłówka (decyzja 10) |
| Przelot przez cienki teren przy 33 m/tick | statek pod powierzchnią | test ruchu ciągły (`BodyTestMotion` przemiata całą drogę) |
| Przed K5-22 brak terenu ciał 1–3 | statek spada przez księżyc | `HomeOnlyGate`: nie niżej niż 2 R |
| Determinizm z predykcją pilota (K5-25) | ciągłe korekty | stała kolejność działań, test bitowy, bez trygonometrii poza normalizacją; .NET nie kontraktuje FMA |
| Koszt 8 statków × 128 pudełek w teście ruchu | tick > 8 ms | pomiar w kroku 8; mniej pudełek przy dużych statkach |
| Statek opuszcza układ | `float` poza `FrameLimits` | granica 400 km (decyzja 6.6) |

## Pytania do właściciela (wariant domyślny obowiązuje bez odpowiedzi)

| # | Pytanie | Wariant domyślny |
| --- | --- | --- |
| P1 | Czy ogranicznik prędkości hamuje bez zużycia paliwa? | tak — twardy limit spec |
| P2 | Kierunek „przód” statku? | oś przodu pierwszego kokpitu (`ControlFrame`) |
| P3 | Tankowanie: cel i zasięg? | promień w blok stacji tankowania, statek na postoju ≤ 15 m od stacji, do pełna |
| P4 | Opór atmosfery? | liniowy, `[0,05; 0; 0,05; 0,06]` 1/s dla ciał 0–3, wygaszany w górnych 20 % atmosfery |
| P5 | Zużycie paliwa? | 4 j./MN·s ciągu + 4 j./s napędu; zbiornik 1000 j.; strojenie w danych |
| P6 | Do K5-22 statek przy ciałach 1–3? | zawis na 2 R (`HomeOnlyGate`) |
| P7 | Autopilot przelotu? | tylko narzędzie testów i `--dev` (`ship pilot`); w grze brak (spec :35) |
| P8 | Odpowiedź na zderzenie? | niesprężyste zatrzymanie w normalnej, tarcie 0,5 stycznej, bez uszkodzeń |
| P9 | Przyciski w `ShipControl`? | stany (nie zbocza) |

## Pułapki

- Applier `ShipFuelAddOp` nie czyta treści, terenu ani `ShipState` w warunkach wstępnych; pojemność niesie operacja.
- `ShipState` i pozy statków nie trafiają do skrótu; nie dopisywać sekcji w `StateHasher`.
- Ruch statku tylko przez `UpdateConstructKinematics`/`UpdateShipState` z `ShipFlightSystem`; żadnej operacji na tick.
- `ShipFlightModel` bez alokacji, LINQ i lambd; jeden porządek działań — zmiana kolejności to zmiana kontraktu z predykcją.
- `ConcavePolygonShape3D` chunków nie służy do `BodyTestMotion`; tylko pośrednik z pudełek.
- Lot rejestrowany w fazie `Frames` **przed** `ServerZoneTransitions` (test kolejności).
- Nie zmieniać bajtu wersji rekordów rodzajów 1–7, 20, 22, 23; nowy rodzaj zaczyna od wersji 1.

## Odstępstwa i decyzje wykonawcy

(wypełnia wykonawca: stan K5 na `main`, rzeczywiste nazwy API, akceptacja kontraktu przez Opus w kroku 1, czasy przelotów i wartości po strojeniu, odpowiedzi właściciela, różnice od planu)
