# K5-23 · Bloki statku i tryb statku

| | |
| --- | --- |
| Prowadzi | Sonnet 5.5 (Claude Code, `claude --model sonnet`; `--model opusplan` przy kroku 0, gdy nazwy API na `main` różnią się od tabeli „Kontekst”) |
| Testy akceptacyjne | Sonnet 5.5; kontrakt `ShipModeSetOp`/`ShipModeIntent`, kody i test złoty kodeka zatwierdza Opus 5.5 przed kodem (krok 2) |
| Przegląd | Opus 5.5 w PR (ZASADY pkt 9: pakiet Sonnet zmienia kontrakty Core — operacja 20, intencja 10, odmowy 30/31/37, `BlockTypeDef`, `ITerrainService`) |
| Zależy od | K4-19 (plan projektu). Decyzja właściciela: start od razu. Faza A rusza na `main` (K2 + K3-00); faza B czeka na K3-15 (lustro budowli u klienta); faza C (odbiór) po scaleniu K4-19 |
| Gałąź | `pakiet/k5-23-bloki-statku` (worktree `F:/dev/horyzont-k5-23`) |
| Kamień milowy | K5 Kosmos; odbiór wg spec 02: „budowla z 30 bloków przechodzi w tryb statku i z powrotem; masa i ciąg zgodne z danymi” |
| Równolegle | K5-20 (Opus) — bez krawędzi startu; mapa kolizji w sekcji „Równoległość z K5-20” |

**Cel:** dodać bloki statku jako dane treści (masa i funkcja bloku), czyste obliczenie masy, środka masy, bezwładności, ciągu w sześciu kierunkach, momentu i pojemności paliwa oraz przełączanie budowli między `Static` a `Ship` intencją z kokpitu (`ShipModeIntent` 10 → `ShipModeSetOp` 20) z odmowami `NoCockpit` (30), `ShipAnchored` (31) i `NoEngine` (37); w K5-23 każdy statek stoi na postoju.

## Kontekst

- `docs/ZASADY.md`: pkt 2 (autorytet serwera, każda zmiana świata to operacja), 3 (kontrakty chronione: `Operations/`, `Intents/`, `OperationApplier.cs`, `Serialization/`, `Protocol/`; zakresy kodów podprojektów), 4 (applier nie czyta treści ani terenu — operacja niesie wszystko), 5, 6, 7 (walidacja zwraca `RejectReason`), 8.
- `docs/spec/02-kosmos.md`: „Statki z bloków” (tabela bloków, warunki trybu statku), „Dane statku” (`BlockTypeDef` + masa + `BlockFunction`; nagłówek z trybem; fotel = komórka bloku), „Operacje i dziennik” (wiersz `ShipModeSetOp` 20; intencja `ShipModeIntent` 10; kody odmów 30–39), „Zapis” (skrót obejmuje tryb budowli, pomija pozę statku), „Pakiety pracy” (wiersz K5-23: ładownia bez kontenera, „do K5-24 każdy statek stoi na postoju”).
- `docs/plans/K1-03-core-stan-i-operacje.md:614-622` (zakresy: `OperationKind` 20–29, `RejectReason` 30–39, `IntentCode` 10–19, `MessageType` 30–39, `BlobKind` 10–19 dla Kosmosu), `:666` (usunięcie fotela/ostatniego bloku ramy z dziećmi → `IntersectsPlayer`).
- `docs/plans/K2-12-budowle.md` (katalog `blocks.json`, `BlockTypeCatalog`, materiały, wbudowane siatki `builtin:slab`/`builtin:slope`, `ConstructWorld`), `K2-13-narzedzia.md` (promień intencji z oka, `ToolController`, `IIntentSender`, `IntentReply` w HUD), `docs/plans/README.md` X4–X6 (D4, router, wspólne fixture kodeka).

Kod, na którym pakiet buduje (sprawdzony na `F:/dev/horyzont-k3-15`; krok 0 potwierdza na `main`):

| Co | Gdzie |
| --- | --- |
| `BlockTypeDef(string Key, BlockShape Shape, byte MaterialId, bool HasCollision)` — nigdzie nie serializowany; używają go tylko `BlockTypeCatalog` i `BlockTypeContent` | `src/Horyzont.Core/World/Blocks/BlockTypeDef.cs` |
| `BlockTypeCatalog.Parse` (`FormatVersion = 1`, `materials`, `blocks`: `key`, `shape`, `material`, `collision`, `mesh`), `BlockTypeContent(Def, NameKey, Mesh)`; treść `game/content/blocks/blocks.json` (5 typów + `core:missing`) | `src/Horyzont.Constructs/Content/`, `game/content/blocks/blocks.json`, `game/src/Constructs/BlockContentLoader.cs` |
| `BlockTypeRegistry` (klucz ↔ `TypeId`, `MissingKey`), rejestr stały od utworzenia/wczytania świata | `src/Horyzont.Core/World/Blocks/BlockTypeRegistry.cs` |
| `BlockState` (`TypeId`, `Orientation` < 24, `FunctionState` 3 bity), `BlockOrientation.Forward/Up/ToConstruct/ToQuaternion`, `Face` (`PosX=0 … NegZ=5`) | `src/Horyzont.Core/World/Constructs/` |
| `Construct` (`Mode` tylko do odczytu, `Origin`, `Rotation`, `Parent`, `ParentSeq`, `Chunks`, `BoundsMin/Max`), `ConstructMode { Static = 0, Ship = 1 }`, `ConstructHeaderRecord` z `Mode` (checkpoint i `ConstructState` już niosą tryb) | `src/Horyzont.Core/World/Constructs/`, `Checkpoints/` |
| `StateHasher`: hashuje tryb, pozę tylko dla `Static` | `src/Horyzont.Core/Hashing/StateHasher.cs:58-61` |
| `OperationKind` 1–7, `OperationCodec` (bajt wersji rekordu = 1, `switch` po rodzaju), `IntentCodec` (prywatny `enum IntentCode`, wersja 1), `RejectReason` 0–21 | `src/Horyzont.Core/Operations/`, `Serialization/`, `Intents/RejectReason.cs` |
| `IntentValidator(WorldState, ITerrainService, GameRules)` — bez dostępu do treści; zasięg i linia wzroku dla bloków (`RemoveBlockIntent`, okolice `:260-372`) | `src/Horyzont.Core/Simulation/IntentValidator.cs` |
| `ITerrainService`: `IsAreaLoaded`, `LoadAreaBlocking`, `Raycast`, `ApplyEdit` — brak próbkowania gęstości | `src/Horyzont.Core/World/Terrain/ITerrainService.cs` |
| `ConstructReplica` (Core, K3-15): `TryReparent`; brak zmiany trybu | `src/Horyzont.Core/Replication/ConstructReplica.cs` |

## Zakres

1. **Core (przegląd Opus):** `BlockFunction` + rozszerzony `BlockTypeDef` (masa, funkcja); `BlockContentTable` (tabela `TypeId` → definicja, budowana raz przy starcie świata); `ShipStats` + `ShipStatsCalculator` (masa, środek masy, tensor bezwładności, ciąg ×6, moment, paliwo, liczniki, fotele); `ShipModeSetOp` (20) z applierem, `ShipModeIntent` (10) z walidatorem i resolverem; `RejectReason.NoCockpit` (30), `ShipAnchored` (31), `NoEngine` (37); kodeki i fixture; `ITerrainService.TrySampleSolid`; `Construct.SetMode` (internal); `ConstructReplica.TrySetMode`.
2. **Treść:** `blocks.json` w formacie 2 (`mass` wymagane, `function` opcjonalne), 12 nowych typów bloków statku, materiały zastępcze (kolory), nazwy pl/en.
3. **Serwer:** tabela treści w `ServerSimulation`, `ShipStatsCache` (przeliczanie przy zmianie trybu i bloków statku), komenda konsoli `ship <id>`.
4. **Gra:** `VoxelTerrainService.TrySampleSolid`, akcja wejścia „Tryb statku” na kokpicie (wysyła `ShipModeIntent`), teksty odmów 30/31/37, wskaźnik trybu w HUD deweloperskim; lustro budowli u klienta stosuje `ShipModeSetOp` (faza B).
5. **Testy:** jednostkowe Core, kodeka (złote), integracyjne serwera (30 bloków tam i z powrotem, odmowy, powtórka i skrót), treści, silnika (próbkowanie gęstości), ręczny w grze.

## Poza zakresem

- Lot, ruch statku, `ShipState` (blob 12), paliwo w zbiornikach, tankowanie (`ShipFuelAddOp` 21), odmowa `ShipNotParked` (39), autolądowanie, zmiana fizyki budowli w trybie statku (K5-24). W K5-23 budowla `Ship` ma tę samą kolizję statyczną co `Static`.
- Fotele: wsiadanie, wysiadanie, wyskok, `SeatOccupied`, `SeatOutOfReach`, `EjectTooFast`, `NotPilot`, `ConstructNotEmpty`, `SeatEvictIntent`, blokada startu (K5-25). K5-23 daje tylko bloki z rolą fotela i listę foteli w `ShipStats`.
- Kontener ładowni (K6-37), otwieranie drzwi (brak intencji w spec; drzwi mają `FunctionState` = 0, „zamknięte”), odblokowywanie bloków przez bossów (K8-54), grafika bloków (K8-50), reguła podparcia i kontrola spójności (K6).
- Ramy, ruch ram, migawki statków (K5-20, K5-26).

## Mapa kryteriów → dowód

| # | Kryterium | Dowód | Kategoria | Faza |
| --- | --- | --- | --- | --- |
| 1 | Budowla z 30 bloków przechodzi w tryb statku i z powrotem | `ShipMode_ThirtyBlockConstruct_ToShipAndBack_ViaIntents` (serwer, intencje od gracza, `OperationApplied` u klienta) + ręczny w grze | domyślna; Manual (zrzut w PR) | A, B |
| 2 | Masa i ciąg zgodne z danymi | `ShipStats_ThirtyBlocks_MatchHandComputedFromContent` (wartości liczone ręcznie w teście z liczb `blocks.json`) + `ship <id>` w konsoli | domyślna | A |
| 3 | Odmowy `NoCockpit`, `NoEngine`, `ShipAnchored` | `ShipModeIntent_*_Rejected*` | domyślna | A |
| 4 | Tryb budowli w skrócie stanu i w powtórce dziennika | `ShipModeSetOp_JournalReplay_SameStateHash`, `…_CheckpointRoundTrip` | domyślna | A |

## Projekt

### Decyzje

1. **Funkcja bloku jako dane w Core.** `BlockTypeDef(string Key, BlockShape Shape, byte MaterialId, bool HasCollision, float MassKg, BlockFunction Function)`. `BlockFunction` to `readonly record struct` (rodzaj + parametry; `None` dla zwykłych bloków):

   | `BlockFunctionKind` | Parametry | Bloki |
   | --- | --- | --- |
   | `None = 0` | — | kadłub, okno, istniejące bloki |
   | `Cockpit = 1` | `SeatRole.Pilot` | kokpit |
   | `Seat = 2` | `SeatRole.Passenger` albo `Navigator` | fotel, stanowisko nawigatora |
   | `Engine = 3` | `ThrustNewtons`, `Tier` | silnik |
   | `Stabilizer = 4` | `TorqueNewtonMeters` | stabilizator |
   | `FuelTank = 5` | `FuelCapacity` (jednostki) | zbiornik |
   | `Cargo = 6` | `Tier` | ładownia (bez kontenera) |
   | `LandingGear = 7` | — | podwozie |
   | `PulseDrive = 8` | `SpeedCapMetersPerSecond` (2000) | napęd pulsacyjny |
   | `Door = 9` | — | drzwi (`FunctionState` 0 = zamknięte) |

   `BlockTypeDef` nie jest serializowany (klient bierze definicje z własnej treści po kluczu), więc to nie zmienia formatu; to treść, nie zapis świata (spec „Dane statku”).
2. **Tabela treści dla walidatora.** `BlockContentTable` (Core, `World/Blocks/`): budowana raz z `BlockTypeRegistry` i słownika definicji (`BlockTypeCatalog`), `ref readonly BlockTypeDef Get(ushort typeId)`; klucz nieznany treści → definicja `core:missing` (masa 100 kg, `None`). `ServerSimulation` dostaje ją nowym przeciążeniem konstruktora; stary konstruktor = pusta tabela (każda `ShipModeIntent` → `NoCockpit`), więc istniejące testy i K4-19 bez zmian. Applier **nie** dostaje tabeli (ZASADY pkt 4).
3. **Obliczenia statku.** `ShipStatsCalculator.Compute(Construct, BlockContentTable) → ShipStats` (Core, nowy katalog `src/Horyzont.Core/Ships/`): masa = suma; środek masy w przestrzeni budowli (środki komórek = komórka + 0,5); tensor bezwładności względem środka masy (każdy blok: sześcian 1 m o masie m, `I = m/6`, plus twierdzenie Steinera); ciąg w sześciu kierunkach przestrzeni budowli: silnik ma dyszę na swojej ścianie `Forward`, siła działa w kierunku przeciwnym — `Face d = Opposite(BlockOrientation.ToConstruct(o, Forward))`, `Thrust[d] += ThrustNewtons` (P1); moment = suma stabilizatorów; paliwo = suma zbiorników; liczniki kokpitów, silników, napędów, podwozi; fotele (komórka, rola) posortowane po `CellPos`. Alokuje tylko wynik (wywoływana przy zdarzeniu, nie co tick). Konsument: K5-24 (model lotu w Core), K5-25 (fotele).
4. **Operacja `ShipModeSetOp(EntityId Construct, ConstructMode Mode, FrameRef Parent, uint ParentSeq, Vector3 Origin, Quaternion Rotation)`, kod 20.** Warunki wstępne appliera (spec): budowla istnieje; `Mode` znany i różny od obecnego; `Parent` i `ParentSeq` równe obecnym; poza skończona i obrót znormalizowany (`OperationApplier.IsPose`); dla `Static` rodzic to planeta (budowla `Static` istnieje tylko w ramie planety, jak `CheckConstructCreate`). Skutek: `Construct.SetMode(mode, origin, rotation)` (internal; oznacza nagłówek jako zmieniony), wersja bloków bez zmian, `ConstructChanges` z bieżącą wersją (lustra odświeżają nagłówek). Applier nie czyta treści ani terenu. Kodek: nowy rodzaj z bajtem wersji rekordu 1; rekordy rodzajów 1–7 bez zmian, więc stare dzienniki odtwarzają się identycznie (zamrożony korpus i `DeterminismTests` bez zmian w plikach).
5. **Intencja `ShipModeIntent(EntityId Construct, CellPos CockpitCell, ConstructMode TargetMode, Vector3 RayOrigin, Vector3 RayDirection)`, kod 10.** Walidator w tej kolejności: budowla istnieje (`UnknownConstruct`) i stoi na planecie gracza (`WrongPlanet`); komórka w zakresie i zajęta (`CellOutOfBounds`, `CellEmpty`); blok w komórce ma funkcję `Cockpit` (`NoCockpit`); początek promienia = oko serwera, zasięg i linia wzroku jak dla `RemoveBlockIntent` (`RayOriginMismatch`, `OutOfReach`, `NoLineOfSight`); `TargetMode` ≠ obecny i rodzic = planeta (`InvalidParameters`); limit z kubełka bloków (`RateLimited`). Dla `Ship`: co najmniej jeden silnik (`NoEngine`); teren wokół budowli wczytany (`AreaNotLoaded`); brak bloków wbitych w teren (`ShipAnchored`, decyzja 6). Dla `Static`: w K5-23 bez dalszych warunków (każdy statek stoi; `ShipNotParked` dokłada K5-24). Resolver: `ShipModeSetOp` z bieżącą ramą, `ParentSeq` i pozą budowli.
6. **„Wbity w teren”.** `ITerrainService.TrySampleSolid(PlanetId planet, Vector3 position, out bool solid) → bool` (`false` = obszar niewczytany). Wszyscy implementujący dostają metodę (`VoxelTerrainService`: gęstość SDF z Voxel Tools `< 0` w punkcie, na wątku głównym; `FakeTerrainService` i atrapy serwera: z ich modelu terenu; krok 3 wylicza implementacje grepem). Sprawdzane są tylko bloki, których środek leży bliżej środka planety niż maks. promień terenu + 2 m; dla każdego: środek i 8 narożników wsuniętych o 0,1 m do wnętrza komórki (dotyk gruntu ≠ wbicie). Którykolwiek punkt w gruncie → `ShipAnchored`.
7. **Niezmienniki statku.** Dla budowli `Ship` walidator odrzuca: usunięcie ostatniego kokpitu (`NoCockpit`) i ostatniego silnika (`NoEngine`) — model lotu K5-24 może zakładać, że statek je ma; postawienie bloku wbitego w teren (`ShipAnchored`, ta sama procedura co w decyzji 6, tylko dla nowej komórki). Walidator nie jest ścieżką formatu (ZASADY pkt 3), więc bez podbijania wersji.
8. **Odmowy.** `RejectReason.NoCockpit = 30`, `ShipAnchored = 31`, `NoEngine = 37` (numery ze spec, zakres Kosmosu); `RejectReason` jest w `Intents/` → kodek odpowiedzi bez zmiany układu bajtów (bajt), nowe wartości w teście złotym. Pozostałe kody 32–36, 38, 39 dodają K5-24/K5-25/K5-27.
9. **Pamięć podręczna statystyk.** `ShipStatsCache` (`src/Horyzont.Server/Ships/`): słuchacz `OperationApplied` (po stronie serwera) przelicza `ShipStats` dla budowli, która weszła w `Ship`, i dla `BlockSet`/`BlockClear` w budowli `Ship`; usuwa przy `Static`/usunięciu. `TryGet(EntityId, out ShipStats)`. Komenda konsoli `ship <id>` drukuje linię `SHIP id=… mode=… mass=…kg com=(…) thrust=+X:…,−X:…,… torque=… fuel=… cockpits=… engines=… seats=…` (format stały).
10. **Treść (`blocks.json` format 2).** `mass` (kg, > 0, skończona) wymagane dla każdego typu, `function` opcjonalne (`{"kind": "engine", "thrust": 60000, "tier": 1}` itd.); parser odrzuca nieznany rodzaj, ujemne i nieskończone parametry, `function` z parametrami innego rodzaju (`InvalidDataException` ze ścieżką JSON, jak dziś). Format 1 nieobsługiwany (jedyny plik treści w repo aktualizowany w tym samym commicie). Nowe typy (kształt `cube`, chyba że wskazano):

   | Klucz | Masa (kg) | Funkcja | Materiał |
   | --- | --- | --- | --- |
   | `core:ship_cockpit` | 400 | `Cockpit` | `ship_glass` |
   | `core:ship_seat` | 80 | `Seat` Passenger (`mesh`: `builtin:slab`) | `ship_fabric` |
   | `core:ship_nav_station` | 250 | `Seat` Navigator | `ship_console` |
   | `core:ship_engine_t1` | 600 | `Engine` 60 000 N, tier 1 | `ship_engine` |
   | `core:ship_stabilizer` | 300 | `Stabilizer` 40 000 N·m | `ship_engine` |
   | `core:ship_fuel_tank` | 250 | `FuelTank` 1000 j. | `ship_tank` |
   | `core:ship_cargo` | 200 | `Cargo` tier 1 | `metal` |
   | `core:ship_landing_gear` | 150 | `LandingGear` | `ship_dark` |
   | `core:ship_pulse_drive` | 800 | `PulseDrive` 2000 m/s | `ship_engine` |
   | `core:ship_hull` | 200 | — | `metal` |
   | `core:ship_window` | 150 | — | `ship_glass` |
   | `core:ship_door` | 200 | `Door` | `ship_dark` |

   Istniejące typy dostają masy: `core:stone_block` 2400, `core:wood_planks` 500, `core:metal_plate` 200, `core:stone_slab` 1200, `core:stone_slope` 1200; `core:missing` 100 (wbudowany). Wszystkie liczby to dane do strojenia w K5-24/K8-57 (P2). Nazwy pl/en w `game/locale/blocks.csv`.
11. **Klient.** Akcja wejścia `ship_mode_toggle` (domyślnie klawisz `G`, P3): gdy promień narzędzia trafia w komórkę bloku z funkcją `Cockpit` budowli z lustra, wysyła `ShipModeIntent` (cel = przeciwny tryb) przez istniejący `IIntentSender`; promień z `AimRayBuilder.EyeFromFeet` (ZASADY pkt 2). Odpowiedź: istniejący komunikat odmowy w HUD z tekstami pl/en dla 30/31/37. HUD deweloperski pokazuje tryb i `ShipStats` budowli w celowniku (statystyki liczone u klienta z lustra i własnej treści tym samym `ShipStatsCalculator`). Lustro (`ConstructReplica.TrySetMode`) stosuje `ShipModeSetOp` z `OperationApplied` (faza B).
12. **Protokół.** `ProtocolVersion` = (main przy scaleniu) + 1 z aktualizacją istniejących testów (D4), bo `OperationApplied`/`IntentSubmit`/`IntentReply` niosą nowe wartości. Fixture kodeka (X6): próbki `ShipModeSetOp` i `ShipModeIntent` w `CodecGen`, `MaxMessages.All()`, testach kontraktu. Router bez zmian (brak nowego `MessageType`).

### Pliki

```
src/Horyzont.Core/World/Blocks/      BlockTypeDef (+ MassKg, Function), BlockFunction, BlockFunctionKind, SeatRole, BlockContentTable
src/Horyzont.Core/Ships/             ShipStats, ShipStatsCalculator, ShipSeat
src/Horyzont.Core/Operations/        ShipModeSetOp, OperationKind (+ ShipModeSet = 20)
src/Horyzont.Core/Intents/           ShipModeIntent, RejectReason (+ 30, 31, 37)
src/Horyzont.Core/Simulation/        OperationApplier (ShipModeSet), IntentValidator, IntentResolver, ServerSimulation (przeciążenie konstruktora)
src/Horyzont.Core/World/Constructs/Construct.cs        + internal SetMode
src/Horyzont.Core/World/Terrain/ITerrainService.cs     + TrySampleSolid
src/Horyzont.Core/Replication/ConstructReplica.cs      + TrySetMode (faza B)
src/Horyzont.Core/Serialization/     OperationCodec, IntentCodec (kod 10)
src/Horyzont.Core/Protocol/ProtocolConstants.cs        ProtocolVersion (D4)
src/Horyzont.Constructs/Content/     BlockTypeCatalog (format 2), BlockContentTableBuilder
src/Horyzont.Server/Ships/           ShipStatsCache; Console: komenda `ship`
game/content/blocks/blocks.json, game/locale/blocks.csv, teksty odmów (plik tekstów HUD z K2-13/K3-18)
game/src/Terrain/VoxelTerrainService.cs                TrySampleSolid
game/src/Boot/GameComposition.Ships.cs                 tabela treści do ServerSimulation, cache, akcja wejścia
game/src/Client/... (narzędzia/HUD)                    ship_mode_toggle, linia HUD dev
tests/Horyzont.Testing/              FakeTerrainService.TrySampleSolid, Ships/ShipFixtures (budowla 30 bloków), CodecGen
tests/Horyzont.Core.Tests/Ships/, …/Simulation/ShipModeTests.cs, …/Serialization/ (złote), tests/Horyzont.Constructs.Tests/Content/
tests/Horyzont.Server.Tests/Ships/, game/tests/Terrain/TrySampleSolidTests.cs
```

## Testy akceptacyjne (napisz najpierw)

`ShipFixtures.Thirty`: budowla 30 bloków na terenie płaskim testowym — 1 kokpit, 4 silniki (2 w dół, po 1 w tył i w przód), 2 stabilizatory, 2 zbiorniki, 2 fotele, 1 stanowisko nawigatora, 4 podwozia (najniższa warstwa, nad gruntem), 1 napęd pulsacyjny, 1 ładownia, 12 bloków kadłuba (w tym 2 okna i 1 drzwi); układ zapisany jawnie w pliku fixture.

| Test | Plik | Kategoria | Faza | Co sprawdza |
| --- | --- | --- | --- | --- |
| `ShipStats_ThirtyBlocks_MatchHandComputedFromContent` | `tests/Horyzont.Core.Tests/Ships/ShipStatsCalculatorTests.cs` | domyślna | A | masa, środek masy, ciąg ×6, moment, paliwo, liczniki i fotele = wartości policzone w teście z liczb treści |
| `ShipStats_EngineOrientations_ThrustOppositeToForward` (24 orientacje) / `ShipStats_Inertia_SingleBlockAndParallelAxis` | jw. | domyślna | A | konwencja P1, tensor |
| `ShipModeSetOp_Preconditions_*` / `ShipModeSetOp_Apply_SetsModeAndPose_VersionUnchanged` | `tests/Horyzont.Core.Tests/Simulation/ShipModeTests.cs` | domyślna | A | tryb ≠ obecny, rama/seq równe, poza skończona, `Static` tylko w planecie |
| `ShipModeIntent_NoCockpitAtCell_RejectedNoCockpit` / `…_NoEngine_RejectedNoEngine` / `…_BlockInTerrain_RejectedShipAnchored` / `…_TouchingGround_Accepted` / `…_AreaNotLoaded` / `…_OutOfReachOrNoLineOfSight` | jw. | domyślna | A | kolejność i kody odmów; brak zmiany stanu przy odmowie |
| `ShipConstruct_RemoveLastCockpitOrEngine_Rejected` / `ShipConstruct_PlaceBlockIntoTerrain_RejectedShipAnchored` | jw. | domyślna | A | niezmienniki decyzji 7 |
| `ShipModeSetOp_JournalReplay_SameStateHash` / `ShipModeSetOp_CheckpointRoundTrip_ModePreserved` | `tests/Horyzont.Core.Tests/Simulation/` | domyślna | A | `Static → Ship → Static` z różnymi pozami; skrót po powtórce = skrót na żywo; `TerrainJournalGoldenCorpusTests` i `DeterminismTests` bez zmian w plikach |
| `OperationCodec_ShipModeSet_Golden` / `IntentCodec_ShipModeIntent_Golden` / `RejectReason_KosmosValues_Golden` | `tests/Horyzont.Core.Tests/Serialization/` | domyślna | A | bajty zatwierdzone przez Opus (krok 2) |
| `BlockCatalog_Format2_ParsesMassAndFunctions` / `…_InvalidFunction_ThrowsWithJsonPath` / `ShippedContent_AllShipBlocksPresentWithMass` | `tests/Horyzont.Constructs.Tests/Content/` | domyślna | A | parser i plik treści z repo |
| `ShipMode_ThirtyBlockConstruct_ToShipAndBack_ViaIntents` | `tests/Horyzont.Server.Tests/Ships/ShipModeIntegrationTests.cs` | domyślna | A | `ServerRuntime` + klient w pamięci: gracz buduje 30 bloków intencjami (albo fixture + intencje trybu), `ShipModeIntent` → `Ship`, `ship <id>` drukuje zgodne liczby, `ShipModeIntent` → `Static`; dwie odpowiedzi bez odmowy, dwie `OperationApplied` |
| `ShipStatsCache_RecomputesOnModeAndBlockChanges` | `tests/Horyzont.Server.Tests/Ships/` | domyślna | A | przeliczenie po `BlockSet`/`BlockClear` w `Ship`, usunięcie przy `Static` |
| `ConstructReplica_ShipModeSetOp_UpdatesModeAndPose` / `ConstructMirror_OperationApplied_ShipMode_RefreshesHeader` | `tests/Horyzont.Core.Tests/Replication/`, `tests/Horyzont.Client.Tests/` | domyślna | B | lustro klienta po operacji = serwer |
| `TrySampleSolid_InsideAboveAndUnloaded` | `game/tests/Terrain/TrySampleSolidTests.cs` | silnik | A | punkt w gruncie, nad gruntem, obszar niewczytany → `false` |
| `Manual_ShipMode_ThirtyBlocks_InGame` | procedura w PR | `Manual` | B | host: zbudowanie statku z palety, `G` na kokpicie → HUD „Tryb statku”, `ship <id>` w konsoli, powrót do `Static`; zrzuty |

## Kroki

Każdy krok: test czerwony → kod → zielone testy → mały commit (Conventional Commits). Wyszukiwanie kodu i uruchamianie testów zlecać subagentowi Haiku (CLAUDE.md projektu).

**Faza A (od razu, `main` z K2 + K3-00)**

- [ ] 0. Warunki wejścia: `git status`, worktree `F:/dev/horyzont-k5-23` od `origin/main`; zapisać w „Odstępstwach” scalone pakiety i rzeczywiste nazwy z tabeli „Kontekst” (zwłaszcza `IntentCodec`, kubełek bloków, plik tekstów odmów w HUD); sprawdzić, że kody 20 (`OperationKind`), 10 (`IntentCode`), 30/31/37 (`RejectReason`) są wolne na `main` i w otwartych PR K5. Commit `docs:`.
- [ ] 1. `BlockFunction`, `BlockFunctionKind`, `SeatRole`, rozszerzony `BlockTypeDef`, `BlockContentTable`; `BlockTypeCatalog` format 2 + `BlockContentTableBuilder`; `blocks.json` (typy i masy z decyzji 10), `blocks.csv`. Testy treści najpierw.
- [ ] 2. Kontrakt do zatwierdzenia przez Opus **przed kodem appliera**: rekordy `ShipModeSetOp`, `ShipModeIntent`, wartości enumów, układ bajtów (testy złote czerwone). Po akceptacji: kodeki, fixture X6 (`CodecGen`, `MaxMessages`, kontrakt), podbicie `ProtocolVersion` (D4).
- [ ] 3. `ITerrainService.TrySampleSolid` we wszystkich implementacjach (lista z grepa w PR), `FakeTerrainService` z modelem terenu do testów; `VoxelTerrainService` + test silnika.
- [ ] 4. `ShipStats`, `ShipStatsCalculator` (testy 30 bloków, 24 orientacje, tensor) i `ShipFixtures.Thirty`.
- [ ] 5. `Construct.SetMode`, applier `ShipModeSetOp` (warunki wstępne, skutek, `ConstructChanges`); testy powtórki, skrótu, checkpointu; korpus złoty i `DeterminismTests` bez zmian w plikach.
- [ ] 6. Walidator i resolver `ShipModeIntent`, niezmienniki decyzji 7, przeciążenie konstruktora `ServerSimulation` z `BlockContentTable`; testy odmów.
- [ ] 7. `ShipStatsCache`, komenda `ship`, `GameComposition.Ships.cs` (tabela treści z `BlockContentLoader` + rejestr świata → `ServerSimulation`; także serwer dedykowany); test integracyjny `ShipMode_ThirtyBlockConstruct_ToShipAndBack_ViaIntents`.
- [ ] 8. Klient w grze: akcja `ship_mode_toggle` (`G`), wysyłka intencji przez `IIntentSender` z promieniem z oka, teksty odmów 30/31/37 (pl/en), linia HUD dev; `python tools/engine/build.py smoke`.

**Faza B — po scaleniu K3-15**

- [ ] 9. **[K3-15]** `ConstructReplica.TrySetMode`, `ConstructMirror` stosuje `ShipModeSetOp` z `OperationApplied`; testy lustra; `Manual_ShipMode_ThirtyBlocks_InGame` w trybie host + klient (zdalny klient widzi zmianę trybu w HUD dev).

**Faza C — odbiór (po scaleniu K4-19)**

- [ ] 10. Rebase na `main`; pełne komendy akceptacyjne; dopisać do `docs/ZASADY.md` pkt 4: „walidator może czytać treść (`BlockContentTable`) i teren; applier `ShipModeSetOp` — nigdy”, a w pkt 3 wiersz `src/Horyzont.Core/World/Blocks/BlockTypeDef.cs` „treść, nie format — bez podbicia”; wiersz K5-23 w sekcji „Pakiety K5” `docs/plans/README.md`; PR z odstępstwami i pytaniami.

## Odbiór

```
dotnet build Horyzont.sln
dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"
dotnet format Horyzont.sln --verify-no-changes
python tools/engine/build.py smoke
```

Plus testy silnika `game/tests/Terrain/TrySampleSolidTests.cs` (job `game`), linia `SHIP …` dla `ShipFixtures.Thirty` i zrzuty `Manual_ShipMode_ThirtyBlocks_InGame` w opisie PR.

## Równoległość z K5-20

Brak krawędzi startu. Zasada X10: integruje pakiet scalany później. Rekomendowana kolejność scalania: **K5-23 → K5-20** (K5-23 nie czeka na K3-14). Gdy K5-20 będzie gotowy pierwszy — odwrotnie, bez zmian w planach.

| Plik / punkt wspólny | K5-23 | K5-20 | Konflikt i kto rozwiązuje |
| --- | --- | --- | --- |
| `Construct.cs` | `internal SetMode` | `internal SetPose` | sąsiednie składowe; scalany później dopisuje obok |
| `ServerSimulation.cs` | przeciążenie konstruktora | metoda `UpdateConstructKinematics` | rozłączne; rebase |
| `ProtocolVersion` i jego dwa testy | podbicie | podbicie | D4: scalany później = (main) + 1 |
| `EntityReplication.cs` | nie edytuje | budowle `Ship` w migawkach (tryb czytany co rundę) | brak: po scaleniu obu przełączenie trybu automatycznie włącza i wyłącza migawki statku |
| `ConstructReplica.cs` / `ConstructMirror` | `TrySetMode`, obsługa `ShipModeSetOp` | tylko odczyt `Mode` | brak |
| `tests/Horyzont.Testing/Ships/` | `ShipFixtures` | `TestShips` | różne pliki |
| `GameComposition` | `GameComposition.Ships.cs` | `GameComposition.Frames.cs` | osobne partiale |
| `docs/ZASADY.md`, `docs/plans/README.md` | wiersze K5-23 | wiersze K5-20 | scalany później dopisuje obok |

## Ryzyka

| Ryzyko | Skutek | Odpowiedź |
| --- | --- | --- |
| Próbkowanie SDF dla dużych statków w jednym ticku | skok ticku przy przełączaniu trybu | tylko bloki blisko gruntu (decyzja 6), 9 próbek na blok; pomiar dla 2000 bloków w teście silnika (raport, próg 4 ms) |
| Nowa metoda `ITerrainService` łamie implementacje w pakietach K3 w locie | czerwony build po rebase | scalany później dopisuje implementację (X10); lista implementacji w PR |
| Format 2 `blocks.json` a mody w przyszłości | brak zgodności wstecz | brak modów przed EA; format wersjonowany, parser jasno odrzuca 1 |
| Konwencja kierunku ciągu niezgodna z intuicją graczy | statki budowane „odwrotnie” | P1; konwencja w jednym miejscu (`ShipStatsCalculator`) i w opisie bloku w HUD |
| Masy/ciąg nie pozwalają na zawis 30-blokowego statku | K5-24 musi stroić dane | dane, nie kod (P2); K5-24 stroi na `ShipFixtures.Thirty` |

## Pytania do właściciela (wariant domyślny obowiązuje bez odpowiedzi)

| # | Pytanie | Wariant domyślny |
| --- | --- | --- |
| P1 | Kierunek ciągu silnika? | dysza na ścianie `Forward` bloku, siła w przeciwną stronę |
| P2 | Masy i parametry bloków? | tabela decyzji 10; strojenie w K5-24 i K8-57 bez zmian kodu |
| P3 | Klawisz przełączania trybu? | `G` przy celowaniu w kokpit w zasięgu; przemapowanie w K9-61 |
| P4 | Czy statek w trybie `Ship` może stracić ostatni kokpit/silnik? | nie: `NoCockpit`/`NoEngine` przy usuwaniu |
| P5 | Definicja „wbity w teren”? | środek albo narożnik wsunięty o 0,1 m w gruncie (SDF < 0); dotyk ≠ wbicie |
| P6 | Rozszerzyć `ITerrainService` czy osobny interfejs? | metoda `TrySampleSolid` w `ITerrainService` (każda implementacja musi odpowiedzieć; brak cichego „nie wbity”) |
| P7 | Powrót do `Static` w K5-23? | zawsze dozwolony w ramie planety (każdy statek stoi); `ShipNotParked` w K5-24 |
| P8 | Bezwładność w K5-23? | tak, liczona razem z masą (K5-24 jej potrzebuje do momentu) |
| P9 | Kontrola spójności statku? | brak w K5-23 (budowla jest jedną siatką); reguła podparcia i spójności w K6 |
| P10 | Drzwi? | blok z `FunctionState` 0; otwieranie bez intencji w spec — poza zakresem |

## Pułapki

- Applier `ShipModeSetOp` nie czyta `BlockContentTable` ani terenu — wszystko, od czego zależy skutek, jest w operacji (ZASADY pkt 4).
- Poza budowli `Ship` nie jest w skrócie stanu, więc `ShipModeSetOp` **musi** nieść pozę: powrót do `Static` ustawia pozę z operacji, nie z checkpointu.
- Nie zmieniać bajtu wersji rekordów rodzajów 1–7; nowy rodzaj zaczyna od wersji 1.
- `RejectReason` 30/31/37 — dokładnie numery ze spec; 32–36, 38, 39 zostawić wolne.
- Treść klienta i serwera musi być ta sama (`BlockContentTable` z tego samego `blocks.json`); statystyki HUD to podgląd, decyduje serwer.
- `FunctionState` drzwi = 0 przy stawianiu; resolver `PlaceBlockIntent` już tworzy `BlockState.Create(typeId, orientation)` z zerem.

## Odstępstwa i decyzje wykonawcy

(wypełnia wykonawca: stan K3/K4 przy każdej fazie, rzeczywiste nazwy API, akceptacja kontraktu przez Opus w kroku 2, wyniki pomiarów, różnice od planu)
