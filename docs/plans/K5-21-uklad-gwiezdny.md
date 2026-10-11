# K5-21 · Układ gwiezdny
Przegląd Opus: zatwierdzony (2026-10-10; szkic Gemini przepisany: ciała jako planety w `PlanetRegistry`, separacja stref z konstrukcji, backfill w migracji, warunkowy skrót, strażnicy „jeden teren do K5-22”, słońce zgodne z `DayNightCycle`)

| | |
| --- | --- |
| Prowadzi | Opus 5.5 (Claude Code, `claude --model opus`) |
| Testy akceptacyjne | Opus 5.5 (krok 1 — determinizm generacji, separacja stref, ciągłość grawitacji — przed jakimkolwiek kodem serwera i zapisu) |
| Przegląd | GPT-6.1 Sol (ZASADY pkt 9: pakiet Opus → drugi przegląd Sol); całość zmian w `src/Horyzont.Core/` przegląda Opus 5.5 (ZASADY pkt 3) |
| Zależy od | K5-20 scalony na `main` (spec 02:246). Pośrednio K3-14/K3-15 (przez K5-20). K5-23 — **nie** (statki w testach budowane przez `WorldStateBuilder`). **Start na stosie (README „Pakiety K5”, Y13, wariant domyślny):** fazy A–B mogą ruszyć na gałęzi od `pakiet/k5-20-ramy` po zakończeniu fazy A K5-20 (Core ram stabilny); PR do `main` dopiero po scaleniu K5-20 i rebase. Alternatywa: start po scaleniu K5-20 |
| Gałąź | `pakiet/k5-21-uklad` (worktree `F:/dev/horyzont-k5-21`) |
| Kamień milowy | K5 Kosmos; odbiór wg spec 02:246: „powtarzalny układ z ziarna; strefy wpływu się nie nakładają; grawitacja bez skoków” |
| Równolegle | K5-23 (brak krawędzi startu); K5-22, K5-24, K5-27 startują dopiero po K5-21 |

**Cel:** jeden zapisany, powtarzalny z ziarna układ gwiezdny — cztery nieruchome ciała zarejestrowane jako planety świata, kierunek słońca z ticku, grawitacja stref wpływu bez skoków i serwerowe przepinanie statków na granicy stref z histerezą 3,0 R / 3,2 R — z migracją Kosmosu (trzy tabele, blob 10) i komunikatem `StarSystemState` (32), bez zmiany zachowania istniejących światów jednoplanetowych w testach złotych.

## Kontekst

- `docs/ZASADY.md`: pkt 2 (świat Godota = rama układu; ramy tylko w `double`), 3 (Core = przegląd Opus; `PlanetParams` jest typem zapisywanym; `Protocol/`, `Serialization/` → `ProtocolVersion`; migracje tylko nowe, numer = następny wolny przy scaleniu; zamrożony korpus `TerrainJournalGoldenCorpusTests` i złote skróty `DeterminismTests` bez zmian w plikach), 4 (determinizm; zapis zmiennoprzecinkowy bitowy), 6 (gorące ścieżki bez alokacji), 7 (`[LoggerMessage]`), 8.
- `docs/spec/02-kosmos.md`: :13 i :39–52 (cztery nieruchome ciała, tabela promieni i odległości, słońce 30 min, grawitacja g₀·(R/r)² w 3 R, wygaszanie 2,5–3 R), :104–119 (drzewo ram, obrót planet w EA zerowy), :158 (przejście stref = operacja serwera, 3,0 R wejście / 3,2 R wyjście, przepina się tylko statek), :175 (`StarSystemState` 32 po `ServerHello`), :181–189 (jedna migracja z trzema tabelami tworzona przez K5-21; blob 10; świat bez wpisu dostaje układ z ziarna z istniejącą planetą jako domową), :194 (skrót obejmuje układ), :246 (odbiór), :261 (plan awaryjny w danych: mniejsze promienie i odległości).
- `docs/plans/README.md`: X4/D4, X5 (router), X6 (fixture), X10 (integruje scalany później).
- `docs/plans/K5-20-ramy-odniesienia.md` (autorytatywny dla interfejsów): decyzja 3 (`FrameLimits`: rama planety ≤ 100 km, układu ≤ 500 km), 4 (`IFramePoseSource`, `FrameGraph`, `WorldStateFramePoses` = źródło nad `WorldState` + `IPlanetPlacement`), 5 (`IPlanetPlacement.TryGetPose(PlanetId, out PoseD)`, `HomeOnlyPlacement`), 7 (`FrameTransitions.TryReparent(EntityId, FrameRef, CellPos?, out AppliedOperation?, out FrameTransitionFailure)`; budowle tylko `Ship` → planeta albo układ), 8 (`ServerTickPhase { Frames, Characters, Compose }`, stabilne wstawianie wg fazy), 12 (`ClientFrameGraph` z `IPlanetPlacement`), 16 (`FrameNodes`: `System` → `Planet_<id>` z transformacją z `IPlanetPlacement`; teren planety domowej zostaje w miejscu), 18 (`IFrameSpace`; układ: zero).
- `docs/plans/K5-23-bloki-statku.md`: brak wspólnych kodów; oba podbijają `ProtocolVersion`.

Kod, na którym pakiet buduje (zweryfikowane w `F:/dev/horyzont-k4-19`, stan K2 + K3-00; nazwy K5-20 z jego planu):

| Co | Gdzie |
| --- | --- |
| `PlanetParams(Id, System, Seed, GeneratorProfile, RadiusMeters=4000, VoxelSizeMeters=1, Gravity=9.81, AtmosphereHeightMeters=1000, CoreDepthMeters=400, DayLengthSeconds=1800)` + `Validate()` | `src/Horyzont.Core/World/PlanetParams.cs:19` |
| `PlanetGenesis` (`HomePlanetIndex = 0`, `DerivePlanetId`, `DerivePlanetSeed`, `CreatePlanet`; funkcja zamrożona) | `src/Horyzont.Core/World/PlanetGenesis.cs` |
| `PlanetRegistry.All` (**posortowane po id**), `TryGet` | `src/Horyzont.Core/World/PlanetRegistry.cs:11-24` |
| `WorldState` (`CreateNew`, `FrameExists` — rama planety istnieje tylko dla planety z rejestru, `TryResolvePlanet`) | `src/Horyzont.Core/World/WorldState.cs:95,139` |
| `WorldStateBuilder` (wczytanie) | `src/Horyzont.Core/Checkpoints/WorldStateBuilder.cs` |
| `StateHasher` (planety `:39-52`) | `src/Horyzont.Core/Hashing/StateHasher.cs` |
| Miejsca zakładające „planeta = `All[0]`” | `ServerSimulation.cs:128` (spawn w `PreparePlayerJoin`), `game/src/Constructs/HostConstructs.cs:111,190`, `game/src/Client/Sky/ClientDayNight.cs:94` (`hello.Planets[0]`) |
| Teren tworzony **dla każdej** planety (wszystkie w początku układu) | `game/src/Server/VoxelServerTerrainFactory.cs:87`, `game/src/Client/OwnTerrainProvider.cs:53` |
| `ServerHello` niesie `state.Planets.All` (≤ `MaxPlanetsInHello = 64`) | `src/Horyzont.Server/GameServerHooks.cs:91`, `src/Horyzont.Core/Protocol/ProtocolConstants.cs:40` |
| `BlobKind` 1–5, `StateCodec.Encode/DecodePlanetBlob`, `WritePlanet/ReadPlanet` | `src/Horyzont.Core/Serialization/BlobKind.cs`, `StateCodec.cs:106,385` |
| `MessageType` 1–20 (K3 dodaje 21–25), `MessageChannels` | `src/Horyzont.Core/Protocol/MessageType.cs`, `MessageChannels.cs` |
| `MessageCodec` | `src/Horyzont.Core/Serialization/MessageCodec.cs` |
| `MessageRouter`, `ServerSession`, `ClientSession` | `src/Horyzont.Net/Session/` |
| `ProtocolVersion = 3` (przed K3) | `src/Horyzont.Core/Protocol/ProtocolConstants.cs:7` |
| `M0001_Initial` (tabele `meta`, `planets`, …), `MigrationRunner.All = [M0001]` | `src/Horyzont.Persistence/Migrations/` |
| `WorldCreator` (migracje na pustym pliku, potem meta i `planets`), `WorldLoader` (backup `pre-migration` + `MigrationRunner.Run`, potem `ReadPlanets`) | `src/Horyzont.Persistence/WorldCreator.cs:37,72`, `WorldLoader.cs:63-67,138` |
| Tworzenie nowego świata | `game/src/Boot/GameComposition.cs:672-675` |
| Słońce dziś: `DayNightCycle.SunDirection(tick, tickRate, planet)` (tick 0 = +X, obrót wokół +Y, okres `DayLengthSeconds`), `SunLight`, `ClientDayNight` | `game/src/Planet/Sky/`, `game/src/Client/Sky/ClientDayNight.cs` |
| Grawitacja postaci: stała g₀ do `R + AtmosphereHeight`, potem 0 | `src/Horyzont.Core/Motion/CharacterMotor.cs:47` |
| `PlanetGeneratorV1.MaxReliefMeters = 300` | `game/src/Planet/Generators/PlanetGeneratorV1.cs:16` |
| `RejectReason.OutsideAtmosphere = 10` (atmosfera = sufit budowania) | `src/Horyzont.Core/Intents/RejectReason.cs:39` |
| `ServerSimulation.Applied`, `ApplyServerOperation` | `src/Horyzont.Core/Simulation/ServerSimulation.cs:61,138` |
| `WorldFactory.CreateDefault()` (świat korpusu złotego) | `tests/Horyzont.Testing/WorldFactory.cs` |
| K5-20: `Vector3D`, `QuaternionD`, `PoseD`, `FrameMath`, `FrameLimits`, `IPlanetPlacement`, `HomeOnlyPlacement`, `FrameGraph`, `WorldStateFramePoses` | `src/Horyzont.Core/Frames/` |
| K5-20: `FrameTransitions`, `ServerTickPhase` | `src/Horyzont.Server/Frames/`, `src/Horyzont.Server/ServerTickPhase.cs` |
| K5-20: `ClientFrameGraph`; `FrameNodes`, `IFrameSpace`, `SharedWorldFrameSpaces`; `GameComposition.Frames.cs` | `src/Horyzont.Client/Frames/`, `game/src/Shared/Frames/`, `game/src/Boot/` |

## Zakres

1. **Core `src/Horyzont.Core/Space/`:** `StarSystemLayout` (wersja 1, zamrożona), `BodyPlacement`, `StarSystemParams`, `StarSystemGenesis`, `StarSystemRules` (stałe stref), `StarSystemModel` (gorące zapytania bez alokacji), `StarSystemMath` (grawitacja, słońce), `WorldStatePlanetPlacement : IPlanetPlacement`.
2. **Core `World/`, `Hashing/`, `Checkpoints/`, `Simulation/`:** `WorldState.StarSystem` (nullable) i `WorldState.HomePlanet`; przeciążenie `CreateNew` z układem; `WorldStateBuilder.SetStarSystem`; warunkowe rozszerzenie `StateHasher`; spawn nowego gracza na planecie domowej (`ServerSimulation.PreparePlayerJoin`).
3. **Serializacja i protokół:** `BlobKind.StarSystemParams = 10`, `StateCodec.Encode/DecodeStarSystemBlob`; `MessageType.StarSystemState = 32`, rekord, kodek, router, kanał, fixture X6, D4.
4. **Persistence:** `M0002_Space` (trzy tabele + uzupełnienie układu istniejącego świata w tej samej transakcji), zapis w `WorldCreator`, odczyt w `WorldLoader`.
5. **Serwer:** planeta domowa pierwsza w `ServerHello.Planets`; `ServerSpaceSync` (wysyłka `StarSystemState`); `ServerZoneTransitions` (faza `Frames`, przepinanie statków z histerezą); `WorldStatePlanetPlacement` w źródle ram serwera.
6. **Klient (biblioteka):** `ClientStarSystem : IPlanetPlacement` zasilany `StarSystemState`, wpięty w `ClientFrameGraph`.
7. **Gra:** strażnicy „teren tylko planety domowej do K5-22”; `HostConstructs`, `ClientDayNight` na planecie domowej; `DayNightCycle` deleguje do `StarSystemMath.SunDirection` (bitowo ten sam wynik); węzły planet w `FrameNodes` stoją w pozach z układu; `SharedWorldFrameSpaces` liczy grawitację budowli ze `StarSystemModel`; partial `GameComposition.Space.cs`; jedna linia tworzenia świata w `GameComposition.cs`.

## Poza zakresem

- Tereny ciał 1–3 na serwerze i kliencie, impostory, aktywacja i wygaszanie (K5-22). Do K5-22 ciała 1–3 istnieją w rejestrze i w grafie ram, ale nie mają terenu (intencje terenu na nich → `AreaNotLoaded`, i tak nikt tam nie doleci przed K5-24).
- Lot, ciąg, paliwo, limit v = h/τ, opór atmosfery (K5-24; gęstość oporu per ciało to dane K5-24, księżyc = 0). Bloki i tryb statku (K5-23). Fotele (K5-25). Replikacja statków w ramie układu (K5-26). Znaczniki i mapa (K5-27; K5-21 tylko tworzy pustą tabelę `markers` o ustalonym tu układzie kolumn).
- Zmiana grawitacji postaci w `CharacterMotor` (decyzja 6). Orbity, obrót planet, kolejne układy (spec 02:35).

## Fazy i zależność od niescalonych pakietów

| Faza | Kroki | Wymaga scalonych | Co daje |
| --- | --- | --- | --- |
| A | 0–3 | K5-20 | model i generacja układu, grawitacja, słońce, `WorldState.StarSystem/HomePlanet`, warunkowy skrót, blob 10 |
| B | 4–5 | K5-20 | migracja M0002 z uzupełnieniem, zapis i odczyt, strażnicy jednego terenu, planeta domowa pierwsza w `ServerHello` |
| C | 6–7 | K5-20 | `StarSystemState` (32), D4, `ServerSpaceSync`, `ClientStarSystem` w `ClientFrameGraph` |
| D | 8–9 | K5-20 | `ServerZoneTransitions` z histerezą; test integracyjny lotu przez trzy ramy z powtórką i restartem |
| E | 10–11 | K5-20 | słońce, węzły planet, przestrzenie ram w grze; dokumentacja i PR |

## Mapa kryteriów → dowód

| # | Kryterium (źródło) | Dowód | Kategoria / job | Faza |
| --- | --- | --- | --- | --- |
| 1 | Powtarzalny układ z ziarna (02:246) | `StarSystemGenesis_SameSeed_BitwiseIdentical` + `StarSystemGenesis_GoldenSeeds_MatchFrozenValues` (3 ziarna, bity pozycji i parametrów) | domyślna | A |
| 2 | Strefy wpływu się nie nakładają, także z histerezą (02:52, :158, :246) | `StarSystemGenesis_ZonesSeparated_Across10000Seeds` (∀ i≠j: d ≥ 3,2(Rᵢ+Rⱼ) + 250 m) + `StarSystemLayout_V1_WorstCaseSeparated` (analitycznie dla skrajnych zakresów) | domyślna | A |
| 3 | Grawitacja bez skoków; 0 poza 3 R (02:52, :246) | `StarSystemMath_Gravity_NoJumpPerMillimetre` (FsCheck, r ∈ [0; 3,3 R], \|g(r+1 mm) − g(r)\| ≤ 1e-5 m/s²) + `…_ExactAtBoundaries` (g(2,5 R) = 0,16 g₀, g(3 R) = 0, g(r > 3 R) = 0) | domyślna | A |
| 4 | Cztery nieruchome ciała, odległości 18 / 50 / 110 km, promienie z tabeli (02:13, :43–48) | `StarSystemGenesis_V1_BodiesWithinSpecRanges` | domyślna | A |
| 5 | Słońce: cykl 30 min, kierunek z ticku (02:41) | `StarSystemMath_SunDirection_MatchesDayNightCycleBitwise` (1000 ticków, stary i nowy wzór) + `…_PeriodIs1800s` | domyślna | A |
| 6 | Skrót obejmuje układ (02:194); świat bez układu — skrót bez zmian | `StateHasher_StarSystemChange_ChangesHash` + niezmienione pliki `TerrainJournalGoldenCorpusTests`/`DeterminismTests` (`git diff` pusty) | domyślna | A |
| 7 | Migracja z trzema tabelami, blob 10 (02:181–187) | `M0002_FreshFile_CreatesThreeTables` + `StarSystemBlob_GoldenBytesAndRejectsDamaged` | domyślna | B |
| 8 | Świat bez wpisu dostaje układ z ziarna z istniejącą planetą jako domową (02:189) | `M0002_ExistingWorld_BackfillsSystemAndThreeBodies` + `Backfill_EqualsFreshWorldOfSameSeed` | domyślna | B |
| 9 | `StarSystemState` (32) po `ServerHello`, kanał niezawodny (02:175) | `MessageCodec_StarSystemState_RoundTrip`, testy routera X5, `ServerSpaceSync_PlayerReady_SendsStarSystemStateOnce` | domyślna | C |
| 10 | Przejście stref: wejście 3,0 R, wyjście 3,2 R, tylko statek, pasażerowie bez operacji (02:114, :158) | `ZoneTransitions_*` (5 testów, krok 8) | domyślna | D |
| 11 | Ciągłość pozy przy przepięciach i trwałość (02:158, :235) | `SpaceFlight_HomeToMoon_ThreeFramesContinuousAndReplayable` | domyślna | D |
| 12 | Drzewo sceny: planety w swoich pozach (02:104, :125) | `FrameNodes_StarSystem_PlanetNodesAtPlacement` | silnik (job `game`) | E |

## Projekt

### Decyzje

1. **Ciała są planetami świata.** Każde z czterech ciał ma `PlanetParams` w `PlanetRegistry` (tabela `planets`, blob 4, `ServerHello`), bo `WorldState.FrameExists` i `FrameTransitions` uznają ramę planety tylko dla planety z rejestru, a K5-22 potrzebuje ziarna i profilu generatora każdego ciała. `StarSystemParams` **nie powiela** promienia, grawitacji ani atmosfery — trzyma tylko rozmieszczenie i słońce. Ciało 0 to istniejąca planeta domowa (`PlanetGenesis.CreatePlanet(seed, 0, …)` w nowych światach, jedyna planeta w starych). Ciała 1–3: `Id = PlanetGenesis.DerivePlanetId(galaxySeed, k)`, `Seed = DerivePlanetSeed(galaxySeed, k)`, `GeneratorProfile` = profil planety domowej, `System = 1`, `DayLengthSeconds` = jak domowa (pole przestarzałe od K5-21, decyzja 7). Kolizja id (2⁻⁶⁴) → `InvalidDataException` przy generacji.
2. **`StarSystemParams`** (`sealed record`, `src/Horyzont.Core/Space/`): `SystemId System` (=1), `byte LayoutVersion` (=1), `PlanetId HomePlanet`, `float SunCycleSeconds`, `IReadOnlyList<BodyPlacement> Bodies` (posortowane po `BodyIndex`, 1..8 ciał, ciało 0 = `HomePlanet` w tożsamości). `BodyPlacement(PlanetId Planet, byte BodyIndex, PoseD Pose)` — poza w ramie układu w `double`; w EA obrót = tożsamość (walidacja: znormalizowany; generator pisze tożsamość). `Validate()` (wewnętrzne): skończoność, unikalność id i indeksów, ciało 0 w (0,0,0).
3. **Generacja (`StarSystemGenesis`, `StarSystemLayout.V1` — zamrożona jak `PlanetGenesis`; zmiana = `V2` obok).** Wejście: `galaxySeed`, `PlanetParams home`. Każda wartość z `XxHash64(LE galaxySeed, LE bodyIndex, LE fieldIndex)` z ziarnem 3 (pola: promień, grawitacja, odległość, azymut, elewacja). Parametry kwantowane całkowitoliczbowo (promień co 10 m, grawitacja co 0,01 m/s², odległość co 1 m), pozycja liczona w `double` i **zaokrąglana do pełnego metra** (różnice ostatniego bitu `Math.Sin/Cos` między platformami znikają). V1:

   | Indeks | Ciało | R [m] | g₀ [m/s²] | Atmosfera [m] | Rdzeń [m] | Odległość [m] |
   | --- | --- | --- | --- | --- | --- | --- |
   | 0 | Planeta domowa | z `home` (4000) | z `home` (9,81) | z `home` (1000) | z `home` (400) | 0 |
   | 1 | Księżyc | 1400–1600 | 1,62 | 600 | 150 | 17 500–18 500 |
   | 2 | Planeta B | 3000–3500 | 7,50–9,00 | 1000 | 300 | 48 000–52 000 |
   | 3 | Planeta C | 4500–5000 | 10,50–12,00 | 1500 | 500 | 108 000–110 000 |

   Kierunek: azymut θ ∈ [0; 2π) w płaszczyźnie XZ, elewacja ∈ [−5°; 5°]. **Separacja z konstrukcji, nie przez ponawianie:** odległość ciała k od domowej jest podnoszona do `max(d, 3,2·(R₀ + R_k) + MinZoneGapMeters)` (`MinZoneGapMeters = 250`); np. księżyc R = 1600 → d ≥ 18 170 m. Pary ciał 1–3 są rozdzielone geometrycznie przez różnice odległości (≥ 30 km ≫ 3,2·(3500+5000) = 27,2 km — sprawdza test analityczny skrajnych zakresów); generator na końcu woła `Validate` z warunkiem separacji ∀ par i rzuca przy naruszeniu (nigdy dla V1). Planeta C ≤ 110 km + 5 km = 115 km od początku — zgodnie z testem Jolt K5-20. Plan awaryjny spec :261 (mniejsze odległości) = nowy `StarSystemLayout` z mniejszymi promieniami; separacja nadal wymuszana.
4. **Strefy (`StarSystemRules`, stałe Core):** `ZoneRadiusFactor = 3.0`, `ZoneExitFactor = 3.2`, `GravityFadeStartFactor = 2.5`, `MinZoneGapMeters = 250`. Strefa ciała = kula 3 R. Z separacji wynika, że punkt leży w strefie co najwyżej jednego ciała, a statek w ramie planety (r ≤ 3,2 R) nie leży w strefie innego ciała.
5. **Grawitacja (`StarSystemMath`, czyste, `double`, zero alokacji):** dla ciała (R, g₀) i odległości r od środka: r ≤ R → g₀; R < r ≤ 2,5 R → g₀(R/r)²; 2,5 R < r < 3 R → g₀(R/r)²·(3R − r)/(0,5 R); r ≥ 3 R → 0. Kierunek do środka; r = 0 → wektor zerowy. `GravityInPlanet(in Vector3D localPosition, PlanetParams planet)` (rama planety — używa K5-24) i `StarSystemModel.GravityAt(in Vector3D systemPosition)` (ciało, w którego strefie leży punkt). Wniosek z histerezy: statek w ramie układu jest zawsze poza 3,0 R każdego ciała, więc grawitacja ramy układu wynosi dokładnie 0 — `IFrameSpace` układu z K5-20 zostaje „zero”.
6. **Grawitacja postaci bez zmian.** `CharacterMotor.GravityAt` (stała g₀ do `R + atmosfera`) zostaje — zmiana dotknęłaby predykcji K3-14 i testów K2-11, a postać bez statku nie opuszcza powłoki atmosfery (wyskok K5-25 < 200 m). Dlatego **każde ciało ma atmosferę > `MaxReliefMeters` (300 m)**: księżyc 600 m (inaczej na wzgórzach księżyca postać nie miałaby grawitacji, a `OutsideAtmosphere` blokowałoby budowanie). Brak oporu powietrza na księżycu to dana K5-24. Pytanie P3.
7. **Słońce.** `StarSystemMath.SunDirection(ulong tick, int tickRate, float cycleSeconds) → Vector3D` — **ten sam wzór co `DayNightCycle.SunDirection`** (okres w całych tickach, faza całkowitoliczbowa, tick 0 = +X, obrót wokół +Y, ćwierć cyklu później −Z); bez nachylenia (szkic proponował 15° — zmieniłby wygląd i `DayNightTests`). `SunCycleSeconds` = `home.DayLengthSeconds` (1800), więc stare światy świecą identycznie. `DayNightCycle` deleguje do Core (test bitowej zgodności). Planety się nie obracają, więc kierunek w ramie układu = w ramie każdej planety. Funkcja tylko dla renderu i mapy (może używać trygonometrii, nie trafia do skrótu).
8. **`WorldState`.** `StarSystemParams? StarSystem { get; }` — `null` dla świata utworzonego starym `CreateNew` (testy, korpus złoty) albo bez możliwości uzupełnienia; `PlanetId HomePlanet => StarSystem?.HomePlanet ?? Planets.All[0].Id`. Nowe przeciążenie `CreateNew(id, galaxySeed, StarSystemParams system, IEnumerable<PlanetParams> planets, blockTypeKeys, materialKeys)` sprawdza, że każde ciało układu jest w `planets`. `WorldStateBuilder.SetStarSystem`. Układ jest niezmienny po utworzeniu (żadna operacja go nie zmienia).
9. **Skrót warunkowy.** `StateHasher` dopisuje sekcję układu **tylko gdy `StarSystem != null`** (po sekcji planet: `U8(LayoutVersion)`, `U64(HomePlanet)`, `F32(SunCycleSeconds)`, `U32(count)`, dla każdego ciała `U64(Planet)`, `U8(BodyIndex)`, 3×`F64` pozycji, 4×`F64` obrotu, bitowo). Świat bez układu ma bajtowo ten sam skrót co dziś, więc korpus złoty i `DeterminismTests` się nie zmieniają (`WorldFactory.CreateDefault()` bez zmian). Nowy `WorldFactory.CreateWithStarSystem(ulong seed)` dla testów K5.
10. **Planeta domowa zamiast `All[0]`.** `PlanetRegistry` sortuje po id, więc po dodaniu ciał `All[0]` może być księżycem. `ServerSimulation.PreparePlayerJoin` (resolver, poza applierem — dziennik bez zmian) bierze `State.HomePlanet`; `HostConstructs.cs:111,190` → `state.HomePlanet`; `GameServerHooks.CreateHello` układa `Planets` z **planetą domową na pozycji 0**, reszta po id (kolejność listy nie jest częścią formatu; XML-doc `ServerHello.Planets` dostaje zdanie „element 0 = planeta domowa”), więc `ClientDayNight.cs:94` i klient przed `StarSystemState` znają domową.
11. **Jeden teren do K5-22.** `VoxelServerTerrainFactory` i `OwnTerrainProvider` tworzą teren tylko dla planety domowej (`state.HomePlanet` / `hello.Planets[0]`) — inaczej cztery `VoxelLodTerrain` nałożyłyby się w początku układu. K5-22 zastępuje strażników aktywacją terenów (komentarz `// K5-22` przy obu).
12. **Migracja `M0002_Space`** (numer = następny wolny przy scaleniu; dziś 2). W jednej transakcji: (a) `CREATE TABLE star_systems (system_id INTEGER PRIMARY KEY, params BLOB NOT NULL)`, `CREATE TABLE markers (marker_id INTEGER PRIMARY KEY, data BLOB NOT NULL)`, `CREATE TABLE ship_state (construct_id INTEGER PRIMARY KEY, data BLOB NOT NULL)` — wszystkie pola znacznika i stanu statku żyją w blobach 11 i 12 (K5-27, K5-24 nie zmieniają DDL); (b) **uzupełnienie istniejącego świata:** gdy `meta.galaxy_seed` istnieje i `planets` ma dokładnie jeden wiersz → `StarSystemGenesis.Create(seed, home, StarSystemLayout.V1)` i zapis trzech wierszy `planets` (blob 4) oraz wiersza `star_systems` (blob 10). Pusty plik (`WorldCreator` uruchamia migracje przed zapisem meta) i świat z ≠ 1 planetą → bez uzupełnienia (log Warning; `StarSystem = null`, świat działa jak dziś). Uzupełnienie w migracji jest atomowe z `user_version = 2` (awaria w połowie → wycofanie, backup `pre-migration` już istnieje), a jawne `V1` zamraża jej zachowanie (ZASADY: migracji się nie edytuje).
13. **Blob 10** (`StateCodec.EncodeStarSystemBlob/DecodeStarSystemBlob`): `[BlobKind=10][format=1][System u64][LayoutVersion u8][HomePlanet u64][SunCycleSeconds f32][count u8]{[Planet u64][BodyIndex u8][pos 3×f64][rot 4×f64]}`; dekodowanie odrzuca NaN/∞, count 0 lub > 8, duplikaty, brak `HomePlanet`, ciało 0 poza początkiem (`ProtocolViolationException` → `WorldCorruptedException` w loaderze). Wspólne `WriteStarSystem/ReadStarSystem` używa też komunikat 32.
14. **`StarSystemState` (32):** `sealed record StarSystemState(StarSystemParams System) : Message`, `ReliableOrdered`, S→K w `Loading` i `InGame` (X5: `IsKnownType` tak, `IsAllowedFromServer` przez `IsGameMessageFromServer`, od klienta — nie). Wysyła `ServerSpaceSync : IServerPlayerObserver` w `OnPlayerReady` (bez zmian w `IServerGameHooks` i `ServerSession`), raz na sesję gracza, tylko gdy `StarSystem != null` (świat bez układu → klient zostaje przy `HomeOnlyPlacement`). Poza budżetem replikacji (sterowanie, jednorazowo: 4 ciała ≈ 290 B, maks. 8 ciał ≈ 560 B — README Y5). `ProtocolVersion` = (main przy scaleniu) + 1 (D4).
15. **Rozmieszczenie w grafie ram.** Serwer: `WorldStatePlanetPlacement(WorldState)` — `StarSystem` → poza ciała, `null` → tożsamość dla `HomePlanet`, inne `false`; zastępuje `HomeOnlyPlacement` w `WorldStateFramePoses` (K5-20). Klient: `ClientStarSystem : IPlanetPlacement` — przed `StarSystemState` tożsamość dla `hello.Planets[0]`, po nim wszystkie ciała; zastępuje `HomeOnlyPlacement` w `GameComposition.Frames.cs` (jedna linia, X10). `StarSystemModel` (Core) = niezmienny widok `(StarSystemParams, PlanetRegistry)` z tablicą `BodyInfo(PlanetId, Vector3D Position, double R, double G0)`; `TryFindZone(in Vector3D systemPosition, out int bodyIndex, out double distance)`, `GravityAt`, zero alokacji.
16. **Przepinanie na granicy (`ServerZoneTransitions`, `src/Horyzont.Server/Space/`, `IServerTickParticipant`, faza `Frames`).** Co tick dla każdej budowli `Mode == Ship` (kolejność po id): rodzic `System` → poza w świecie z `FrameGraph`, `TryFindZone` z r ≤ 3,0 R → `FrameTransitions.TryReparent(ship, FrameRef.Of(planet), null, out _, out failure)`; rodzic `Planet` → r = |Origin| (początek ramy budowli w ramie planety; statki ≪ pasmo 0,2 R ≥ 280 m) > 3,2 R → `TryReparent(ship, FrameRef.Of(new SystemId(1)), …)`; rodzic `Construct` → pomijany. Błąd przepięcia → log `[LoggerMessage]` najwyżej raz na 5 s na statek i ponowienie w następnym ticku. Gracze nie są przeglądani (pasażerowie mają ramę statku, spec :114). Świat bez układu → uczestnik nic nie robi. Kolejność w fazie `Frames`: ruch statków (K5-24) musi być zarejestrowany **przed** tym uczestnikiem — K5-24 jest scalany później i wstawia rejestrację lotu przed `ServerZoneTransitions` w `GameComposition.Space.cs` (X10). Operacja `EntityReparentOp` (7) trafia do dziennika; powtórka nie wymaga układu (applier sprawdza tylko istnienie ramy). Obserwatorzy z kopią statku na żywo dostają przepięcie jako `OperationApplied`, nie retransfer (K5-20 decyzja 10, README Y8) — krok 0 to potwierdza, a test integracyjny kroku 9 liczy części `ConstructState` (oczekiwane 0 przy przejściach).
17. **Gra.** `FrameNodes` (K5-20) pozycjonuje `Planet_<id>` z `IPlanetPlacement`, więc po decyzji 15 węzły ciał stoją w swoich pozach bez nowego kodu poza testem; teren planety domowej zostaje w miejscu (domowa w tożsamości). `SharedWorldFrameSpaces`: rama budowli w regionie planety → `StarSystemMath.GravityInPlanet` wyrażone w osiach budowli; planeta → jak dziś; układ → zero (decyzja 5). `ClientDayNight` bierze cykl z `ClientStarSystem` (gdy jest) albo z `hello.Planets[0]`. `GameComposition.Space.cs`: `ServerSpaceSync`, `ServerZoneTransitions`, `ClientStarSystem`. Tworzenie nowego świata (`GameComposition.cs:672`) woła `StarSystemGenesis.CreateWorldBodies(seed, GeneratorProfiles.Current)` → przeciążenie `CreateNew` z układem (jedyna zmiana w `GameComposition.cs`, opis w PR).

### Pliki

```
src/Horyzont.Core/Space/                 StarSystemParams, BodyPlacement, StarSystemLayout, StarSystemGenesis,
                                         StarSystemRules, StarSystemModel, StarSystemMath, WorldStatePlanetPlacement
src/Horyzont.Core/World/WorldState.cs    + StarSystem, HomePlanet, przeciążenie CreateNew
src/Horyzont.Core/Checkpoints/WorldStateBuilder.cs        + SetStarSystem
src/Horyzont.Core/Simulation/ServerSimulation.cs          PreparePlayerJoin → HomePlanet
src/Horyzont.Core/Hashing/StateHasher.cs                  sekcja układu warunkowo
src/Horyzont.Core/Serialization/BlobKind.cs, StateCodec.cs              blob 10
src/Horyzont.Core/Protocol/MessageType.cs, MessageChannels.cs, ProtocolConstants.cs, Messages/StarSystemState.cs
src/Horyzont.Core/Protocol/Messages/ServerHello.cs        tylko XML-doc (element 0 = planeta domowa)
src/Horyzont.Core/Serialization/MessageCodec.cs           typ 32
src/Horyzont.Net/Session/MessageRouter.cs                 typ 32 (X5)
src/Horyzont.Persistence/Migrations/M0002_Space.cs, MigrationRunner.cs (All)
src/Horyzont.Persistence/WorldCreator.cs, WorldLoader.cs  star_systems
src/Horyzont.Server/GameServerHooks.cs                    CreateHello: planeta domowa pierwsza
src/Horyzont.Server/Space/                                ServerSpaceSync, ServerZoneTransitions, SpaceLog
src/Horyzont.Server/Frames/ (K5-20)                       źródło ram z WorldStatePlanetPlacement
src/Horyzont.Client/Space/ClientStarSystem.cs
game/src/Server/VoxelServerTerrainFactory.cs, game/src/Client/OwnTerrainProvider.cs   strażnik jednego terenu
game/src/Constructs/HostConstructs.cs, game/src/Client/Sky/ClientDayNight.cs           planeta domowa / cykl z układu
game/src/Planet/Sky/DayNightCycle.cs                      deleguje do StarSystemMath
game/src/Shared/Frames/SharedWorldFrameSpaces.cs          grawitacja budowli ze StarSystemModel
game/src/Boot/GameComposition.Space.cs (nowy partial), GameComposition.Frames.cs (1 linia), GameComposition.cs (1 wywołanie)
tests/Horyzont.Testing/WorldFactory.cs                    + CreateWithStarSystem; CodecGen (typ 32)
tests/Horyzont.Core.Tests/Space/                          StarSystemGenesisTests, StarSystemMathTests, StarSystemModelTests
tests/Horyzont.Core.Tests/Hashing/StarSystemHashTests.cs
tests/Horyzont.Core.Tests/Serialization/StarSystemBlobTests.cs, MaxMessages.cs; Protocol/ProtocolContractTests.cs
tests/Horyzont.Net.Tests/                                 router typu 32
tests/Horyzont.Persistence.Tests/M0002SpaceTests.cs (+ plik świata M0001 jako fixture tworzony w teście przez M0001 + insert)
tests/Horyzont.Server.Tests/Space/                        ServerSpaceSyncTests, ZoneTransitionsTests, CreateHelloTests
tests/Horyzont.Client.Tests/Space/ClientStarSystemTests.cs
tests/Horyzont.Integration.Tests/Space/SpaceFlightTests.cs
game/tests/Space/                                         StarSystemSceneTests (FrameNodes, słońce)
```

## Testy akceptacyjne (napisz najpierw)

| Test | Plik | Kategoria | Faza | Co sprawdza |
| --- | --- | --- | --- | --- |
| `StarSystemGenesis_SameSeed_BitwiseIdentical` / `…_GoldenSeeds_MatchFrozenValues` | `tests/Horyzont.Core.Tests/Space/StarSystemGenesisTests.cs` | domyślna | A | dwa wywołania = bity; ziarna 0, 1, 0xDEADBEEF → zapisane wartości (id, R, g₀, pozycje) |
| `StarSystemGenesis_ZonesSeparated_Across10000Seeds` / `StarSystemLayout_V1_WorstCaseSeparated` | jw. | domyślna | A | d ≥ 3,2(Rᵢ+Rⱼ)+250 m dla wszystkich par; analitycznie dla skrajów zakresów |
| `StarSystemGenesis_V1_BodiesWithinSpecRanges` / `…_HomeIsGivenPlanetAtOrigin` / `…_BodyIdsFromPlanetGenesis` | jw. | domyślna | A | tabela decyzji 3; ciało 0 = podany `home` w (0,0,0); id = `DerivePlanetId(seed, k)`; atmosfera > 300 m |
| `StarSystemMath_Gravity_NoJumpPerMillimetre` (FsCheck) / `…_ExactAtBoundaries` / `…_ZeroAtCentreAndOutside` | `tests/Horyzont.Core.Tests/Space/StarSystemMathTests.cs` | domyślna | A | decyzja 5 |
| `StarSystemMath_SunDirection_MatchesDayNightCycleBitwise` / `…_PeriodIs1800s` | jw. | domyślna | A | decyzja 7 (kopia starego wzoru w teście jako wyrocznia) |
| `StarSystemModel_HotPaths_AllocateZeroBytes` | `tests/Horyzont.Core.Tests/Space/StarSystemModelTests.cs` | domyślna | A | 10 000 × `TryFindZone`, `GravityAt`, `GravityInPlanet`, `SunDirection` = 0 B |
| `StarSystemModel_TryFindZone_AtMostOneBody` | jw. | domyślna | A | punkty losowe: najwyżej jedno ciało z r ≤ 3 R |
| `StateHasher_StarSystemChange_ChangesHash` / `StateHasher_NoStarSystem_HashUnchanged` | `tests/Horyzont.Core.Tests/Hashing/StarSystemHashTests.cs` | domyślna | A | decyzja 9; drugi test: skrót `WorldFactory.CreateDefault()` = stała zapisana przed zmianą |
| `PreparePlayerJoin_FourBodies_SpawnsOnHomePlanet` | `tests/Horyzont.Core.Tests/Space/StarSystemGenesisTests.cs` | domyślna | A | id ciała < id domowej → spawn i tak na domowej |
| `StarSystemBlob_GoldenBytesAndRejectsDamaged` | `tests/Horyzont.Core.Tests/Serialization/StarSystemBlobTests.cs` | domyślna | A | złote bajty (zatwierdza Opus), round-trip, odrzucenia z decyzji 13 |
| `M0002_FreshFile_CreatesThreeTables` / `M0002_ExistingWorld_BackfillsSystemAndThreeBodies` / `Backfill_EqualsFreshWorldOfSameSeed` / `M0002_WorldWithTwoPlanets_NoBackfillLogged` | `tests/Horyzont.Persistence.Tests/M0002SpaceTests.cs` | domyślna | B | decyzja 12; uzupełnienie = bity świata utworzonego od razu z układem; backup `pre-migration` istnieje |
| `CreateAndLoad_WithStarSystem_HashEqual` | jw. | domyślna | B | zapis → wczytanie → ten sam skrót |
| `CreateHello_HomePlanetFirst` | `tests/Horyzont.Server.Tests/Space/CreateHelloTests.cs` | domyślna | B | decyzja 10 |
| `MessageCodec_StarSystemState_RoundTrip` + fixture X6 + `ProtocolVersion_MatchesDocumentedMergeValue`, `Constants_GoldenValues` (aktualizacja) | `tests/Horyzont.Core.Tests/Protocol/`, `Serialization/` | domyślna | C | kodek, `MaxMessages`, kanał |
| `MessageRouter_StarSystemState_ServerOnlyLoadingAndInGame` | `tests/Horyzont.Net.Tests/` | domyślna | C | X5 obu kierunków |
| `ServerSpaceSync_PlayerReady_SendsStarSystemStateOnce` / `…_NoStarSystem_SendsNothing` | `tests/Horyzont.Server.Tests/Space/ServerSpaceSyncTests.cs` | domyślna | C | decyzja 14 |
| `ClientStarSystem_BeforeState_HomeOnly_AfterState_AllBodies` | `tests/Horyzont.Client.Tests/Space/ClientStarSystemTests.cs` | domyślna | C | decyzja 15; `ClientFrameGraph.TryGetWorldPose(Planet(moon))` = poza z układu |
| `ZoneTransitions_ShipInSystemReaches3_0R_ReparentsToPlanet` / `…_ShipInPlanetBeyond3_2R_ReparentsToSystem` / `…_ShipOscillatingInBand_NoOperations` / `…_PassengersNotTouched` / `…_StaticConstructIgnored` | `tests/Horyzont.Server.Tests/Space/ZoneTransitionsTests.cs` | domyślna | D | decyzja 16; liczba `EntityReparentOp` w dzienniku; poza w świecie przed = po (≤ 1 mm) |
| `SpaceFlight_HomeToMoon_ThreeFramesContinuousAndReplayable` | `tests/Horyzont.Integration.Tests/Space/SpaceFlightTests.cs` | domyślna | D | statek (fixture `Ship`) przesuwany `UpdateConstructKinematics` po prostej domowa → księżyc: rodzice Planet(dom) → System → Planet(księżyc), skok pozy w świecie ≤ 1 mm, powtórka dziennika i restart zapisu → ten sam skrót |
| `FrameNodes_StarSystem_PlanetNodesAtPlacement` / `SunLight_FollowsStarSystemCycle` | `game/tests/Space/StarSystemSceneTests.cs` | silnik | E | węzły ciał w pozach z układu (≤ 1e-9 m), światło = `SunDirection`; istniejące `game/tests/Planet/DayNightTests.cs` bez zmian |

## Kroki

Każdy krok: test czerwony → kod → zielone testy → mały commit (Conventional Commits). Wyszukiwanie kodu i uruchamianie testów zlecać subagentowi Haiku (CLAUDE.md projektu).

**Faza A — Core**

- [x] 0. Warunki wejścia: `git status`; worktree `F:/dev/horyzont-k5-21` od `origin/main` po scaleniu K5-20; w „Odstępstwach” zapisać scalone pakiety i rzeczywiste nazwy K5-20 (`IPlanetPlacement`, `WorldStateFramePoses`, sygnatura `FrameTransitions.TryReparent`, `ServerTickPhase`, konstrukcja `ClientFrameGraph`, `SharedWorldFrameSpaces`); sprawdzić, że `BlobKind` 10 i `MessageType` 32 są wolne na `main` i w otwartych PR K5, że na `main` jest tylko `M0001` (inaczej numer migracji = następny wolny), oraz wypisać wszystkie miejsca `Planets.All[0]` / `hello.Planets[0]` / pętle tworzące teren po `Planets` (lista z „Kontekstu” + nowe z K3). Commit `docs:`.
- [x] 1. **Ryzyko najpierw:** czerwone `StarSystemGenesisTests`, `StarSystemMathTests`, `StarSystemModelTests` (bez złotych wartości — te dopisać po pierwszym zielonym przebiegu i zatwierdzić w PR). Potem `BodyPlacement`, `StarSystemParams`, `StarSystemLayout.V1`, `StarSystemRules`, `StarSystemGenesis` (`Create(galaxySeed, home, layout)` i `CreateWorldBodies(galaxySeed, profile)`), `StarSystemMath`, `StarSystemModel`. Commity `test:`, `feat:`.
- [x] 2. `WorldState.StarSystem/HomePlanet`, przeciążenie `CreateNew`, `WorldStateBuilder.SetStarSystem`, `WorldStatePlanetPlacement` (podmiana w `WorldStateFramePoses`), `PreparePlayerJoin` → `HomePlanet`, warunkowy `StateHasher`; `WorldFactory.CreateWithStarSystem`. Przed zmianą hashera zapisać stałą skrótu `CreateDefault()` w `StateHasher_NoStarSystem_HashUnchanged`. Sprawdzić `git diff` pusty na `tests/Horyzont.Core.Tests/Golden/TerrainJournals/`, `TerrainJournalGoldenCorpusTests.cs`, `Hashing/DeterminismTests.cs`. Commity `test:`, `feat:`.
- [x] 3. Blob 10: `BlobKind.StarSystemParams = 10`, `StateCodec.Write/ReadStarSystem`, `Encode/DecodeStarSystemBlob`; złote bajty do akceptacji Opus (czerwone do zatwierdzenia). Commity `test:`, `feat:`.

**Faza B — zapis i jeden teren**

- [x] 4. `M0002_Space` (decyzja 12) + `MigrationRunner.All`; `WorldCreator` zapisuje `star_systems` i wszystkie planety, gdy `StarSystem != null`; `WorldLoader` czyta `star_systems` (0 wierszy → `null`; > 1 → `WorldCorruptedException`); testy M0002 na pliku utworzonym przez samą `M0001` + wiersze `meta`/`planets`. Commity `test:`, `feat:`.
- [x] 5. `CreateHello` z planetą domową na pozycji 0 (+ XML-doc `ServerHello.Planets`); strażnicy jednego terenu w `VoxelServerTerrainFactory` i `OwnTerrainProvider`; `HostConstructs` → `HomePlanet`; tworzenie świata w `GameComposition.cs:672` z układem. Testy: `CreateHello_HomePlanetFirst`, istniejące testy silnika terenu zielone (job `game`). Commity `test:`, `feat:`.

**Faza C — protokół i klient**

- [x] 6. `MessageType.StarSystemState = 32`, rekord, `MessageCodec`, `MessageChannels` (`ReliableOrdered`), `MessageRouter` (X5), fixture X6 (`ProtocolContractTests`, `MaxMessages.All()` z 8 ciałami, `CodecGen`), D4 z aktualizacją `ProtocolVersion_MatchesDocumentedMergeValue` i `Constants_GoldenValues`. Commity `test:`, `feat:`.
- [x] 7. `ServerSpaceSync` (rejestracja w `GameComposition.Space.cs`, także serwer dedykowany); `ClientStarSystem` + podmiana placementu w `GameComposition.Frames.cs`; testy serwera i klienta. Commity `test:`, `feat:`.

**Faza D — strefy**

- [x] 8. `ServerZoneTransitions` (decyzja 16) + `SpaceLog`; pięć testów `ZoneTransitions_*` na `ServerRig` z budowlą `Ship` wczytaną przez `WorldStateBuilder`; test alokacji ticku uczestnika (0 B po rozgrzaniu przy 8 statkach). Commity `test:`, `feat:`.
- [x] 9. `SpaceFlight_HomeToMoon_ThreeFramesContinuousAndReplayable` (integracja: przepięcia, powtórka dziennika, checkpoint + restart). Commit `test:`.

**Faza E — gra i odbiór**

- [x] 10. `DayNightCycle` → `StarSystemMath` (istniejące `DayNightTests` bez zmian), `ClientDayNight` z cyklu układu, `SharedWorldFrameSpaces` z `StarSystemModel`, `StarSystemSceneTests` (job `game`). Commity `test:`, `feat:`.
- [x] 11. Rebase na `main`; pełne komendy odbioru; `docs/ZASADY.md` pkt 3: wiersze `src/Horyzont.Core/Space/` (typy zapisywane: `StarSystemParams`, `BodyPlacement`; `StarSystemLayout.V1` i `StarSystemGenesis` zamrożone jak `PlanetGenesis` — zmiana = nowa wersja układu) oraz „`M0002_Space` uzupełnia układ przez `StarSystemLayout.V1`”; pkt 2: „planeta domowa = `WorldState.HomePlanet`, nigdy `Planets.All[0]`”; wiersz scalenia K5-21 w istniejącej sekcji „Pakiety K5” `docs/plans/README.md` z faktyczną wartością `ProtocolVersion`; PR z raportem testu 10 000 ziaren, odstępstwami i pytaniami. Commit `docs:`.

## Odbiór

```
dotnet build Horyzont.sln
dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"
dotnet format Horyzont.sln --verify-no-changes
python tools/engine/build.py smoke
```

Plus job `game` (testy silnika `game/tests/Space/` i istniejące `Planet/DayNightTests`, testy terenu) oraz w PR: `git diff origin/main -- tests/Horyzont.Core.Tests/Golden/TerrainJournals tests/Horyzont.Core.Tests/TerrainJournalGoldenCorpusTests.cs tests/Horyzont.Core.Tests/Hashing/DeterminismTests.cs` = pusty.

## Kody i punkty styku z K5-20 / K5-23

Kody zajęte przez K5-21: **`BlobKind.StarSystemParams = 10`**, **`MessageType.StarSystemState = 32`**, migracja **M0002** (numer przy scaleniu). Żadnych `OperationKind`, `IntentCode`, `RejectReason`. Pozostałe kody Kosmosu (spec 02:146–176): K5-23 op 20 / intencja 10 / odmowy 30, 31, 37; K5-24 op 21 / intencja 13 / odmowa 39 / blob 12; K5-25 intencje 11, 12, 16 / odmowy 32–35, 38 / komunikaty 30, 34; K5-26 komunikat 31; K5-27 op 22, 23 / intencje 14, 15 / odmowa 36 / blob 11 / komunikat 33. Kolizji brak.

Kolejność scalania: **K5-23 ↔ K5-20 (dowolnie; rekomendacja K5-23 → K5-20) → K5-21 → (K5-22, K5-24, K5-27)**. K5-21 nie czeka na K5-23; zasada X10 — integruje scalany później.

| Plik / punkt | K5-21 | Inny pakiet | Rozwiązanie |
| --- | --- | --- | --- |
| `ProtocolConstants.ProtocolVersion` + dwa testy | podbicie (krok 6) | K5-20, K5-23, K5-27, K5-24/25/26 | D4: scalany później = (main) + 1 |
| `MessageType`/`MessageChannels`/`MessageCodec`/`MessageRouter`, fixture X6 | typ 32 | K5-27 (33), K5-25 (30, 34), K5-26 (31) | dopisywanie obok w kolejności numerów |
| `BlobKind`, `StateCodec` | 10 | K5-27 (11), K5-24 (12) | jw. |
| `StateHasher.cs` | sekcja układu po planetach (warunkowa) | K5-27 sekcja znaczników (warunkowa), K5-23 bez zmian | rozłączne bloki; kolejność sekcji: planety → układ → budowle → … → znaczniki na końcu |
| `WorldState.cs`, `WorldStateBuilder.cs` | `StarSystem`, `HomePlanet` | K5-27 `Markers`; K5-23 nie | sąsiednie składowe |
| `ServerSimulation.cs` | `PreparePlayerJoin` | K5-20 `UpdateConstructKinematics`, K5-23 konstruktor z `BlockContentTable` | rozłączne metody |
| `Migrations/` | właściciel M0002 (3 tabele) | K5-24, K5-27 używają tabel; **nie** dodają DDL | — |
| `WorldStateFramePoses` / `GameComposition.Frames.cs` (K5-20) | podmiana placementu | — | K5-21 scalany po K5-20 |
| `GameComposition` | `GameComposition.Space.cs`; 1 wywołanie w `GameComposition.cs` | K5-20 `.Frames.cs`, K5-23 `.Ships.cs` | osobne partiale |
| Faza `Frames` | `ServerZoneTransitions` | K5-24 lot statków | K5-24 rejestruje lot przed uczestnikiem stref |
| `VoxelServerTerrainFactory`, `OwnTerrainProvider` | strażnik jednego terenu | K5-22 | K5-22 zastępuje strażnika |
| `docs/ZASADY.md`, `docs/plans/README.md` | wiersze K5-21 | wszyscy | dopisuje scalany później |

## Ryzyka

| Ryzyko | Skutek | Odpowiedź |
| --- | --- | --- |
| Dodanie ciał do rejestru: kod zakładający `All[0]` = domowa albo „teren dla każdej planety” | spawn na księżycu; cztery nakładające się tereny | decyzje 10–11; lista miejsc w kroku 0; testy `PreparePlayerJoin_*`, `CreateHello_*` |
| Zmiana skrótu łamie korpus złoty | czerwony CI, zakaz zmiany korpusu | skrót warunkowy (decyzja 9); `CreateDefault` bez układu; test stałej |
| `Math.Sin/Cos` różne między platformami | inny układ z tego samego ziarna | parametry całkowitoliczbowe, pozycje zaokrąglone do 1 m, złote wartości w teście |
| Zakresy spec nie spełniają separacji z histerezą (szkic: 3,2·5500+1000 = 18,6 km > 18 km) | nakładające się pasma, oscylacje między ciałami | odległość podnoszona do minimum z konstrukcji (decyzja 3); test 10 000 ziaren + analityczny |
| Uzupełnienie w migracji przerwane | świat bez układu albo w połowie | jedna transakcja z `user_version`; backup `pre-migration` (`WorldLoader.cs:65`) |
| Postać bez grawitacji na wzgórzach księżyca | utknięcie, brak budowania | atmosfera ≥ 600 m (decyzja 6), test zakresów |
| Kolejność w fazie `Frames` (lot vs strefy) | przepięcie liczone z pozy sprzed ruchu (o 1 tick) | nieszkodliwe (pasmo 0,2 R); K5-24 rejestruje lot wcześniej |
| Klient przed `StarSystemState` | ciała 1–3 bez pozy w grafie | tożsamość tylko dla domowej; `TryGetWorldPose` → `false` dla reszty (K5-20 obsługuje brak ramy) |

## Pytania do właściciela (wariant domyślny obowiązuje bez odpowiedzi)

| # | Pytanie | Wariant domyślny |
| --- | --- | --- |
| P1 | Zakresy promieni, g₀ i odległości ciał? | tabela decyzji 3 (spec :43–48; C ≤ 110 km, by zmieścić się w teście Jolt 115 km) |
| P2 | Nachylenie osi słońca? | brak — ten sam wzór co dziś (oś +Y), bez zmian wyglądu |
| P3 | Grawitacja postaci g₀(R/r)² zamiast stałej w atmosferze? | nie w K5-21 (decyzja 6); ewentualnie osobny pakiet z wersją `SurfaceFrame`/predykcji |
| P4 | Świat bez układu (≠ 1 planeta) po migracji? | działa jak dziś (tylko planeta domowa), Warning w logu |
| P5 | Nazwy ciał na mapie? | klucze lokalizacji wg `BodyIndex` (`BODY_HOME`, `BODY_MOON`, `BODY_B`, `BODY_C`) po stronie klienta, nie w blobie |
| P6 | Punkt odniesienia statku przy granicy strefy? | początek ramy budowli (`Origin`); różnica wobec środka masy ≪ pasmo histerezy |

## Pułapki

- Nigdy `Planets.All[0]` jako planeta domowa — `WorldState.HomePlanet` (serwer) albo `hello.Planets[0]` (klient, kolejność gwarantuje `CreateHello`).
- `StarSystemParams` nie przechowuje R ani g₀ — źródłem jest `PlanetParams`; grawitacja liczy się z rejestru.
- Pozycje ciał tylko w `double` (`Vector3D`); `float` wyłącznie w ramie planety przez `FrameMath.TryToFloat`.
- `StarSystemLayout.V1` i `StarSystemGenesis` są zamrożone od scalenia (migracja ich używa) — każda zmiana to `V2`.
- Skrót bez układu musi być bajtowo identyczny z dzisiejszym; nie dopisywać licznika „0 ciał”.
- `M0001_Initial.cs` nie edytować; DDL `markers` i `ship_state` ustala K5-21 raz na zawsze (id + blob).
- `ServerZoneTransitions` nie dotyka graczy; przepina tylko `Mode == Ship` i tylko przez `FrameTransitions`.
- `GravityAt` / `TryFindZone` bez LINQ, lambd i alokacji (test w kroku 1).

## Odstępstwa i decyzje wykonawcy

(wypełnia wykonawca: stan K5-20 i K3 na `main`, rzeczywiste nazwy API, numer migracji, wartość `ProtocolVersion`, wynik testu 10 000 ziaren, złote wartości, odpowiedzi właściciela)

**Krok 0 (2026-10-11).**

- Start na stosie (Y13): gałąź `pakiet/k5-21-uklad-gwiezdny` (nie `pakiet/k5-21-uklad`), worktree `F:/dev/horyzont-k5-21`, od `pakiet/k5-20-ramy-odniesienia` fa4899f (PR #26, w przeglądzie). `main` = fb80590 (K2, K3-00, K3-15, K3-16, K3-14, plany K5). K5-23 (PR #25) i K3-17 (PR #24) otwarte, nie scalone. Poprawki K5-20 z przeglądu scalane do tej gałęzi na polecenie; PR do `main` dopiero po scaleniu K5-20 i rebase.
- `ProtocolVersion` na K5-20 = 6, więc K5-21 na stosie = **7**; wartość przeliczana przy scaleniu (D4: main + 1).
- Kody wolne: `BlobKind` 1–5 (10 wolny), `MessageType` 1–21, 24, 25 (32 wolny) na `main`, K5-20, K5-23 i K3-17; migracje: tylko `M0001` wszędzie → `M0002_Space`.
- Nazwy K5-20 (rzeczywiste): `IPlanetPlacement.TryGetPose(PlanetId, out PoseD)`, `HomeOnlyPlacement(PlanetId)`, prywatne `EntityReplication.NoPlanetPlacement` (domyślny graf replikacji), `WorldStateFramePoses(WorldState, IPlanetPlacement)`, `FrameGraph(IFramePoseSource)` z `TryExpressInRegion`, `FrameTransitions(ServerSimulation, ServerFrameHistory).TryReparent(EntityId, FrameRef, CellPos?, out AppliedOperation?, out FrameTransitionFailure)` (+ przeciążenie z prędkością), `ServerTickPhase { Frames, Characters, Compose }`, `ServerFramesBinder` (graf serwera; `placement` jako fabryka `Func<WorldState, IPlanetPlacement>?`, domyślnie `HomeFirst` = `Planets.All[0]`), `ClientFrameGraph(EntityMirror, ConstructMirror?, IFrameHistoryView, IPlanetPlacement, SystemId)`, `ClientFrames` (placement z `hello.Planets[0]`), `FrameNodes`, `PlanetSpace`, `IFrameSpace`, `SharedWorldFrameSpaces`, `FrameGravity` (Core).
- Miejsca „planeta = pierwsza” (produkcja): `ServerSimulation.cs:129` (`PreparePlayerJoin`), `ServerFramesBinder.cs:63` (`HomeFirst`), `game/src/Server/Characters/ServerCharacters.cs:108` (własny graf), `HostConstructs.cs:111,190`, `RemoteConstructs.cs:120` (`hello.Planets[0]` — poprawne po decyzji 10), `ClientDayNight.cs:97`, `ClientFrames.cs:35` (`hello.Planets[0]`). Pętle po wszystkich planetach: `VoxelServerTerrainFactory.cs:93`, `OwnTerrainProvider.cs:117,138,176`, `ClientTerrainReplicator.cs:234`, `RemoteTerrainPrediction.cs:73`, `ServerTerrainReplicator.cs:162` (K3-16; te trzy dotyczą replikacji terenu — teren bez węzła planety jest nieaktywny, zostają bez zmian, chyba że testy pokażą inaczej), `StateHasher.cs:40`, `WorldCreator.cs:72` (zamierzone).

**Kroki 8–11 (2026-10-11).**

- Krok 8: `ServerZoneTransitions.Step(WorldState)` publiczne (testy i integracja wołają je bez `ServerRuntime`), dostęp do `FrameTransitions` przez `Func<FrameTransitions?>` (w grze `ServerFramesBinder.EnsureWorld`, bo faza `Frames` biegnie przed pierwszym `Compose`). Liczniki `Reparented`/`Refused`. Log: `SpaceLog` 8401 (Information, przepięcie) i 8402 (Warning, odmowa, najwyżej raz na 5 s na statek). Poza pięcioma testami planu: `ZoneTransitions_WorldWithoutStarSystem_DoesNothing` i `ZoneTransitions_Step_EightShipsAllocatesNothing` (0 B na 1000 ticków po rozgrzaniu).
- Uwaga z przeglądu K5-20 (węzły ram serwera składane dopiero w `Compose`, po `Characters`): `GameComposition.Space.cs` rejestruje drugi `FrameNodesComposer` jako **ostatni uczestnik fazy `Frames`** (po `ServerZoneTransitions`; K5-24 wstawia lot przed oboma), więc postacie ticka widzą ramy przesunięte i przepięte w tym ticku. Złożenie w `Compose` zostaje (historia ram, ruch statków z fazy `Characters`). Planety w K5-21 stoją, więc dziś to tylko przepięcia statków.
- Krok 9: integracja w `tests/Horyzont.Integration.Tests/Space/SpaceFlightTests.cs` (projekt dostał referencję `Horyzont.Server` dla `FrameTransitions` i `ServerZoneTransitions`). `WorldCreator.Create` przyjmuje tylko świat bez budowli, więc statek trafia do pliku pierwszym checkpointem (jedna operacja `BlockSetOp` oznacza jego chunk). Lot co 10 m kończy się na granicy księżyca, bo ruch statku (`UpdateConstructKinematics`) nie trafia do dziennika — porównanie skrótu po powtórce ma sens tylko w ticku ostatniej operacji (od K5-24 lot to operacje). Liczenia części `ConstructState` u obserwatorów (decyzja 16) nie ma w tym teście: wymagałoby replikacji serwera; pokrywa to K5-20 (decyzja 10, przepięcie = `OperationApplied`).
- Krok 10: grawitacja budowli przez nowe `FrameGravity.TryAtZone` (Core, obok `TryAt`): region planety → `StarSystemMath.GravityInPlanet` w osiach budowli, region układu → 0; rama planety (postacie) zostaje przy `TryAt` (decyzja 6). Istniejący `game/tests/Frames/FrameSpaceTests.cs` (K5-20) oczekiwał stałego g₀ dla statku na 4100 m — teraz oczekuje g₀(R/r)² (decyzja 17). `DayNightCycle` ma przeciążenie z długością cyklu, `SunLight.Apply` też; `ClientDayNight.StarSystem` ustawia `GameComposition.Space.cs`. `DayNightTests` bez zmian. Testy silnika (`StarSystemSceneTests`, `FrameSpaceTests`, `DayNightTests`) nie były uruchamiane lokalnie (zakaz gdUnit na wspólnym runnerze) — job `game` w CI.
- Krok 11: `ProtocolVersion` = 7 na stosie K5-20 (przeliczyć przy scaleniu: main + 1, D4). Rebase na `main` i PR dopiero po scaleniu K5-20.

**Scalenie K5-20 i `origin/main` (2026-10-11).** K5-20 scalony do `main` jako cac8a1e (PR #26, `ProtocolVersion` 8). Najpierw scalenie `origin/pakiet/k5-20-ramy-odniesienia` (30be480 z K3-17, K3-18, K5-23 i dd0ddf7); konflikty: `ProtocolConstants` (**9** = main 8 + 1, D4; lista wersji uzupełniona o 9: `StarSystemState` 32, blob 10), `ProtocolContractTests` (tylko `ProtocolVersion_MatchesDocumentedMergeValue` i `Constants_GoldenValues` + oba zestawy typów: `ServerStatus` 22, `JoinUnavailable` 23 obok `StarSystemState` 32), `MessageChannels`, `MessageRouter` (+`MessageRouterTests`), `MaxMessages`, `CodecGen` (wiadomości K3-18 i K5-21 obok siebie), `TestShips` (dyrektywy `using`; `ForceMode` usunięty w K5-20 — K5-21 go nie używa, `ModeOp`). Drzewo głowy K5-20 = drzewo `main`, więc `main` dołączony przez `git merge -s ours origin/main`.
