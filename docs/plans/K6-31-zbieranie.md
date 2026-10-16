# K6-31 · Zbieranie

| | |
| --- | --- |
| Prowadzi | Sonnet 5.5 (Claude Code, `claude --model sonnet`); kroki A1 i B1 (zatwierdzenie kontraktów Core przed kodem), przegląd Core A+B i przegląd PR — Opus 5.5 |
| Testy akceptacyjne | Sonnet 5.5, przed kodem; testy i kontrakty etapów A i B (operacje 47, 48, `PropId` i układ obiektów V1, `PropStates` 45, `PropHitIntent` 27, odmowa 53, `ITerrainService.TrySampleGeneratedSurface`, reguły `CompoundOp`, skrót) zatwierdza Opus 5.5 **przed** kodem (ZASADY §3, §8) |
| Przegląd | Opus 5.5 w PR (pakiet Sonnet zmienia kontrakty Core — ZASADY §9); wygląd (drzewa, krzewy, skały i kryształy z generatora, podpowiedź uderzenia) ocenia Dismond — **bez uruchamiania gry z oknem przez wykonawcę** (zakaz właściciela): weryfikacja to xUnit i gdUnit bez okna, zrzuty i ocena wyglądu to krok właściciela |
| Zależy od | K6-29 (na gałęzi wycinek K6-29W: przedmioty, kontenery, urobek z kopania, `M0003_Survival` z tabelą `harvested_props`), K2-09 (edycje terenu i ich wersje), K3-16 (replikacja terenu, widzowie); baza: gałąź K6-30 (`ProtocolVersion` 15, reguły `CompoundOp` D12, `OperationConstructs`) |
| Gałąź | `pakiet/k6-31-zbieranie` (od `pakiet/k6-30-bloki-funkcyjne`) |
| Kamień milowy | K6 Przetrwanie; odbiór wg spec 03 (wiersz K6-31): „urobek zgodny z danymi; zebrane i odrośnięte obiekty identyczne u 2 klientów i po restarcie” |

**Cel:** drzewa, krzewy, skały, głazy i kryształy na planetach stają się obiektami rozgrywki: deterministycznymi z ziarna planety (`PropId`, układ V1 w Core), z punktami wytrzymałości w pamięci serwera i wymaganym poziomem narzędzia. Ostatnie uderzenie (`PropHitIntent`, 27) to jedna operacja złożona `PropHarvestOp` (48) + `ItemCreateOp` z urobkiem; flora odrasta operacją serwera `PropRegrowOp` (47), gdy minie czas z danych i w promieniu 20 m nie ma gracza ani budowli. Zbiór zebranych obiektów jest stanem świata (tabela `harvested_props`, checkpoint, skrót, powtórka), a klienci dostają go w `PropStates` (45). Kopanie terenu sprawdza poziom narzędzia (`ToolTierTooLow`, 53) i usuwa obiekty stojące w kuli edycji. Formaty są te ze spec 03, więc K6-36 (zawalenia przy kopaniu), K6-38 (uprawy) i K8-51 (typy planet, kolejne układy obiektów) je rozszerzają, a nie zastępują.

## Kontekst

- `docs/ZASADY.md`: §2 (każda zmiana świata to operacja; `OperationTerrain.TryGetTerrainEdit`, `OperationConstructs.TryGetConstructPart`, składowa nieodwracalna ostatnia z widokiem efektywnym — K6-30; `ITerrainService` tylko na wątku głównym), §3 (kontrakty chronione, linia 68 o zmianie appliera bez podbicia formatu, linia 74 o zakresach kodów, wiersz `game/src/Planet/Generators/` — profil generatora zamrożony złotym skrótem), §4 (applier nie pyta terenu ani generatora; „istnienie obiektów generatora i trafienia w teren sprawdza walidator”; zdarzenia czasowe — odrastanie — są operacjami serwera), §6 (gorące ścieżki), §8 (TDD, FsCheck, `FakeTimeProvider`, `FakeTerrainService`), §9, §11.
- `docs/spec/03-przetrwanie.md`: Zbieranie (l. 63–70: urobek z objętości, reszta ułamkowa w pamięci serwera, obiekty generatora z punktami wytrzymałości, odrastanie flory w tickach od zebrania przy braku budowli i gracza w promieniu 20 m, skały i złoża nie odrastają, poziom narzędzia 0 = ręka, nadmiar jako przedmiot leżący); Architektura (l. 244: resolver wylicza urobek); Operacje (l. 269–270: 47 `PropRegrowOp`, 48 `PropHarvestOp`; l. 277–279 tabela akcji; l. 311: intencja 27 `PropHitIntent`; l. 319: odmowa `ToolTierTooLow` 53); Sieć (l. 332: komunikat 45 `PropStates`; l. 342: „przychodzi razem z terenem regionu i jako delta po każdym zebraniu, bo obiekty generator buduje u klienta z ziarna”; l. 347: rzut złożonej w `OperationApplied` bez przedmiotów); Zapis (l. 359: `harvested_props`; l. 365–366: checkpoint zapisuje nowe zebrane obiekty, `PropRegrowOp` usuwa odrośnięte; l. 368: skrót obejmuje zebrane obiekty); Pakiety pracy (l. 433).
- `docs/plan/plan-projektu.md:132` (K6-31: Sonnet 5.5; zależy od K6-29, K2-09, K3-16), `:137` (K6-36 zależy od K6-31), `:141` (K6-40), `:166` (K8-51 Typy planet zależy od K6-31 — nowe układy obiektów).
- `docs/plans/K6-29W-ekwipunek-i-wydobycie.md` — D8 (reguły Przetrwania włącza treść), D9 (`MeasureSphere` jako precedens metody `ITerrainService` bez podbicia wersji), D10 (urobek i `YieldLedger`), D11 (przepełnienie przepada), D14 (bez bramkowania narzędzi — „walidator może to dodać z poziomami narzędzi (K6-31) bez zmiany formatu”), D16 (`ContentLoader`), D17 (przedmioty i wydajności startowe).
- `docs/plans/K6-30-bloki-funkcyjne.md` — D12 (zbiór składowych `CompoundOp`, `UndoLog`), D14 (`OperationConstructs`), D22 (warunkowe podsekcje skrótu), D25 (fikstura pakietu), uwagi z realizacji: przeglądy A1/B1 przed kodem, przegląd Core A+B, **odstępstwo „stany bloków lokalnego klienta solo i hosta”** (gracz hosta nie dostaje komunikatów replikacji — tu od początku `SharedPropStates`, D18).
- `docs/plans/K2-09-adapter-voxel-tools.md` (edycje terenu, wersje bloków LOD 0, `TerrainVersionTable`), `docs/plans/K3-16-replikacja-terenu.md` (blok terenu 16³ wokseli, widzowie `TerrainView` 2048–16384 m, wspólny budżet wysyłki, `IsLocalPlayer` pomijany).
- `docs/plans/K1-03-core-stan-i-operacje.md:614-622` (zakresy Przetrwania: operacje 30–49, odmowy 40–59, komunikaty 40–49, intencje 20–39, bloby 20–29); `docs/plans/README.md` D4 (`ProtocolVersion` = `main` + 1 przy scaleniu), X5 (router), X6 (fixture protokołu).

**Obiekty generatora dziś nie istnieją.** `game/src/Planet/Rendering/PlanetFlora.cs` to `VoxelInstancer` rozsiewający siatki `flora.glb` po siatkach terenu (`EmitMode.FromFaces`, `Persistent = false`): czysto wizualny, bez identyfikatorów i kolizji, zależny od siatek LOD klienta (serwer dedykowany ich nie ma), więc nie da się z niego zrobić obiektów rozgrywki. Komentarz klasy wprost zapowiada zastąpienie drzew i skał przez K6-31. Pakiet definiuje więc nowy, czysty i zamrożony **układ obiektów V1** w Core (D4, D5), wspólny dla serwera i klienta; wysokość kotwicy bierze z generatora terenu przez istniejący `PlanetSurfaceSampler` (K5-22).

Kod, na którym pakiet buduje (stan gałęzi przy pisaniu planu, 2026-10-16):

| Co | Gdzie |
| --- | --- |
| `OperationKind`: 1–7, 20–23, 30–37, 40, 44, 49 (**wolne 47, 48** — kody ze spec) | `src/Horyzont.Core/Operations/OperationKind.cs` |
| `OperationApplier.TryApply` / `TryApplyCompound`, `IsComponent` (31–37, 40, 44, 49 z `UndoLog`), `BeginOperation`/`EndOperation`, `Finish`, `CheckPreconditions` | `src/Horyzont.Core/Simulation/OperationApplier.cs` (l. 57–131) |
| `OperationTerrain`, `OperationConstructs` (rzuty złożonej) | `src/Horyzont.Core/Operations/` |
| `IntentCodec` (prywatny `IntentCode`: 1–3, 10, 13–15, 20–23, 28, 29 — **wolne 27**) | `src/Horyzont.Core/Serialization/IntentCodec.cs:16` |
| `RejectReason`: 0–21, 30, 31, 36, 37, 39, 40–43, 54, 57 (**wolne 53**) | `src/Horyzont.Core/Intents/RejectReason.cs` |
| `MessageType`: 1–25, 31–33, 40–43, 46, 49 (**wolne 45**); `BlobKind`: 1–5, 10–12, 20–22 | `src/Horyzont.Core/Protocol/MessageType.cs`, `src/Horyzont.Core/Serialization/BlobKind.cs` |
| `ProtocolConstants.ProtocolVersion = 15`, `MaxContainerSlots = 256`, `MaxCompoundComponents = 4096`, `MaxReliableMessageBytes = 256 KiB` | `src/Horyzont.Core/Protocol/ProtocolConstants.cs` |
| `IntentResult(Reason, AppliedOperation? Applied)`; `GameServerHooks` odpowiada `IntentReply(seq, result.Reason, result.Applied?.Id ?? OpId.None)` (przyjęcie bez operacji już się koduje) | `src/Horyzont.Core/Intents/IntentResult.cs`, `src/Horyzont.Server/GameServerHooks.cs:142` |
| `ServerSimulation.Submit` (walidacja → `FunctionalBlockRules.TryResolve` → `SurvivalRules.Resolve` → `TryApplyAndJournal` → `Commit` → kubełek), `ApplyServerOperation`, `Applied`, `AdvanceTick`; konstruktor z `BlockContentTable` i `SurvivalContent?` | `src/Horyzont.Core/Simulation/ServerSimulation.cs` |
| `SurvivalRules.ResolveTerrainEdit` (urobek przez `MeasureSphere`, `MiningYield`, `YieldLedger`, `ItemPlacement.Create(..., ItemSource.Harvest, ...)`, `Compose` z `TerrainEditOp` ostatnim) | `src/Horyzont.Core/Simulation/SurvivalRules.cs:198-294` |
| `IntentValidator`, `IntentFacts`, `PlayerBuckets` (`Terrain`, `TerrainWindow`, `Blocks`, `Markers`, `Items`), `GameRules` (`TickRate` 60, `ReachMeters` 8), `ContainerReach` | `src/Horyzont.Core/Simulation/` |
| `ItemDef(..., byte ToolTier, ...)`, `TerrainYieldDef(MaterialKey, ItemKey, ItemsPerCubicMeter, byte ToolTier)` („checked from K6-31”), `SurvivalContent` (`Items`, `Yields`, `StartKit`, `TryGetYield`), `ContentLoader` (`items/items.json`, `terrain/yields.json`, `start.json`, `HasSource`) | `src/Horyzont.Core/Content/` |
| Treść: `core:multitool` (`toolTier` 1), `core:build_tool` (1), surowce `core:stone`, `core:soil`, `core:sand`, `core:ore`; wydajności z `toolTier` 1 | `game/content/items/items.json`, `game/content/terrain/yields.json` |
| `ITerrainService` (`IsAreaLoaded`, `LoadAreaBlocking`, `Raycast`, `TrySampleSolid`, `MeasureSphere`, `ApplyEdit`); implementacje `VoxelTerrainService`, `FakeTerrainService(surfaceRadius, voxelSizeMeters)`, `NoTerrain` w `game/tests/Constructs/ConstructTestWorld.cs` | `src/Horyzont.Core/World/Terrain/ITerrainService.cs`, `game/src/Terrain/VoxelTerrainService.cs`, `tests/Horyzont.Testing/FakeTerrainService.cs` |
| `WorldState` (`CurrentTick` zapisywany w checkpoincie, `Planets`, `TerrainVersions`, `Containers`, `BlockEntities`, `Progress`, `ChangeTracker`), `PlanetParams(Id, System, Seed, GeneratorProfile, RadiusMeters, …)`, `Tick(ulong)` | `src/Horyzont.Core/World/`, `src/Horyzont.Core/Ids/Tick.cs` |
| `StateHasher` (sekcja Przetrwania `0x53` warunkowa; podsekcje `0x42`, `0x50` z K6-30) | `src/Horyzont.Core/Hashing/StateHasher.cs` |
| `WorldCheckpoint`, `ChangeTracker`, `WorldStateBuilder`, rekordy `ContainerRecord`, `BlockEntityRecord`, `PlayerProgressRecord` | `src/Horyzont.Core/Checkpoints/` |
| `M0003_Survival`: **tabela `harvested_props (planet_id, prop_id, tick, PRIMARY KEY (planet_id, prop_id)) WITHOUT ROWID` już istnieje (pusta)**; migracje M0001–M0003; fikstury `world-v1.db`, `world-v3.db`, `world-v3-blocks.db` (`FixtureGenerator`) | `src/Horyzont.Persistence/Migrations/M0003_Survival.cs:19`, `tests/Horyzont.Persistence.Tests/` |
| `WorldLoader`, `WorldCreator`, `Writing/PersistenceWriter.cs` | `src/Horyzont.Persistence/` |
| `IServerTickParticipant`, `IServerPlayerObserver`, `IServerClientMessageHandler`; `InventoryService` (wzór uczestnika ticku co N ticków), `ServerTerrainReplicator`, `EntityReplication` (rzut `OperationConstructs`), `ConstructStateSender` | `src/Horyzont.Server/` |
| `ServerCharacters` (pozycje postaci na serwerze) | `game/src/Server/Characters/ServerCharacters.cs` |
| `PlanetSurfaceSampler(planet, graph)` (`SurfaceRadius(direction)` z krokami 2 m / 0,125 m; graf wielowątkowy), `GeneratorProfiles.CreateGenerator`, `PlanetLook` (`SeaLevelMeters` −42, `BeachHeightMeters` 3,5, `TreeLineMeters` 110, `SnowLineMeters` 135), `PlanetFlora` (rodzaje `Pine`, `PineTall`, `Broadleaf`, `BroadleafAutumn`, `Bush`, `Grass*`, `Rock`, `Boulder`, `Crystals`), `flora.glb`, `foliage.gdshader` | `game/src/Planet/` |
| Narzędzia klienta: `ToolKind` (`None`, `TerrainManipulator`, `Builder`), `ToolTargeting`, `ToolTargetKind`, `PaceClass` (`Terrain`, `Block`), `IntentPacer`, `PendingIntentTracker`, `AimRayBuilder`, `IToolWorldView`; `IBlockEntityView` (wzór widoku z wariantem współdzielonym) | `src/Horyzont.Client/Tools/`, `src/Horyzont.Client/Blocks/` |
| `ClientReplication`, `ToolNode`, `ToolInputReader`, `GameComposition.*.cs` (części: `Blocks`, `Planets`, …) | `src/Horyzont.Client/Replication/`, `game/src/Client/Tools/`, `game/src/Boot/` |
| Lokalizacja: `items.csv` (`ITEM_*`), `tools.csv` (`REJECT_*`, `PROMPT_*`) | `game/locale/` |

## Zakres

1. **Core: model i operacje.** `PropId`, `PropRegionKey`, `HarvestedPropStore` w `WorldState`, operacje 47 `PropRegrowOp` i 48 `PropHarvestOp` (format 1, składowe `CompoundOp` z `UndoLog`), `OperationProps` (rzut zmian obiektów ze złożonej), kodeki, rekordy checkpointu, skrót stanu.
2. **Core: układ obiektów V1.** `PropLayoutV1` (czysta, zamrożona funkcja ziarna: siatka sześcianu na sferze, rodzaj, przesunięcie, obrót, skala, pasma wysokości), `PropLayouts` (profil generatora → układ), `PropField` (układ + powierzchnia generatora), złoty skrót układu.
3. **Core: treść, intencje, protokół.** `PropDef` i `content/props/props.json`, nowe przedmioty urobku; `PropHitIntent` (27), odmowa `ToolTierTooLow` (53), wytrzymałość obiektów w pamięci serwera (`PropDamageLedger`), zebranie jako operacja złożona, poziomy narzędzi przy obiektach i przy kopaniu terenu, usuwanie obiektów przez kopanie i zasypywanie, `ITerrainService.TrySampleGeneratedSurface`, komunikat `PropStates` (45), `ProtocolVersion` 16.
4. **Zapis i serwer.** Zapis i odczyt `harvested_props` (tabela z M0003, bez migracji), odrastanie (`PropRegrowth`, operacje serwera), replikacja `PropStates` z zainteresowaniem regionów (`PropReplicator`), wpięcie w `VoxelTerrainService`.
5. **Klient i gra (tylko to, co pakiet musi pokazać).** `IPropStateView` (zdalny i współdzielony), wybór obiektu promieniem, uderzenie ręką i manipulatorem, rysowanie obiektów z układu V1 bez zebranych, usunięcie odpowiadających rodzajów z `PlanetFlora`, podpowiedź, etykiety PL/EN.

Poza zakresem:

| Co | Gdzie |
| --- | --- |
| Przedmioty leżące (nadmiar urobku pod nogami), operacje 38/39, blob 24 | reszta K6-29 (przedmioty leżące) — do tego czasu nadmiar przepada (D11, jak K6-29W D11) |
| Zawalenia bloków nad wykopem, `AllowCollapse` w `TerrainToolIntent` | K6-36 |
| Poziomy narzędzi wyższe niż 1 (narzędzia z rzemiosła), bossowie otwierający poziomy, `ProgressKeys` | K6-32 (rzemiosło), podprojekt 5 |
| Uprawy, sadzonki, `PlantIntent`, `CropHarvestIntent` | K6-38 |
| Kolizje obiektów z postacią i statkiem (**DO USTALENIA-7**) | K8-50/K8-51 |
| Nowe profile planet i ich układy obiektów (`PropLayoutV2`, biomy) | K8-51 |
| Animacja ścinania, cząsteczki, dźwięk, padające drzewo, sadzonka w trakcie odrastania | K8-50 (grafika), K9 (dźwięk) |
| Pełny interfejs (pasek wytrzymałości obiektu z serwera, dziennik zdobyczy) | K6-39 |
| Trawa i kwiaty (`Grass*` w `PlanetFlora`) jako obiekty rozgrywki | zostają wizualne |

## Decyzje

Każda decyzja rozstrzyga miejsce, w którym specyfikacja milczy albo pakiet świadomie bierze jej wycinek. Odstępstwa od spec 03 są oznaczone **[odstępstwo]** i trafiają do opisu PR. Rzeczy, których nie da się ustalić z kodu ani specyfikacji, są oznaczone **DO USTALENIA-n** z proponowaną decyzją; wykonawca stosuje propozycję, dopóki Opus 5.5 albo właściciel nie zdecyduje inaczej w kroku A1 albo B1.

- **D0. Baza.** Gałąź od `pakiet/k6-30-bloki-funkcyjne`: `CompoundOp` z regułą „co najwyżej jedna składowa nieodwracalna, ostatnia” (K6-30 D12), `OperationConstructs`, `ProtocolVersion` 15, tabele `M0003_Survival`. Przedmioty leżące nadal nie istnieją, więc przepełnienie urobku przepada (D11). Jeśli K6-30 zostanie scalony do `main` przed K6-31, rebase na `main` bez zmian planu; jeśli nie — PR K6-31 czeka na K6-30.
- **D1. Kody (zakres Przetrwania, K1-03; wolne w repo — sprawdzone).** Operacje 47 `PropRegrowOp`, 48 `PropHarvestOp`; intencja 27 `PropHitIntent`; odmowa `ToolTierTooLow` 53; komunikat 45 `PropStates`. Bloby: **żaden** — `harvested_props` przechowuje kolumny, nie blob (spec 03, Zapis: „—”).
- **D2. `ProtocolVersion` = 16** (gałąź bazowa ma 15; README D4: przy scaleniu = `main` + 1). Aktualizowane istniejące testy `ProtocolContractTests.ProtocolVersion_MatchesDocumentedMergeValue` i `Constants_GoldenValues`.
- **D3. Bez nowej migracji.** Tabelę `harvested_props` utworzyła `M0003_Survival` (spec: „kolejne pakiety dodają tylko bloby i zapis”). Następny wolny numer to **M0004** (zarezerwowany planowo dla Walki, spec 04); K6-31 go nie zajmuje. Gdyby przegląd zażądał zmiany schematu (np. kolumny przyczyny usunięcia — odrzucone w D14), migracja dostaje następny wolny numer w chwili scalenia, a `M0003` nie jest edytowana. Nowa fikstura `world-v3-props.db` (D23).
- **D4. Identyfikator obiektu: `PropId` i regiony. DO USTALENIA-10** (rozstrzygnąć w A1 — układ jest zamrażany). Siatka „sześcian na sferze” o równych kątach (equal-angle cube map): 6 ścian, na ścianę `N × N` komórek, `N = 8 · max(1, round(R · π/2 / 64 m))` (dla R = 4000 m: N = 784, komórka ≈ 8,0 m); region = 8 × 8 komórek (≈ 64 m); w komórce sloty 0–3 (V1 używa 0 = drzewa i krzewy, 1 = skały, głazy, kryształy). `PropId(ulong Value)` w `src/Horyzont.Core/Ids/`, pakowanie **region najpierw**, żeby obiekty regionu tworzyły ciągły przedział w posortowanym magazynie:

  | Bity | Pole |
  | --- | --- |
  | 63–61 | 0 (zarezerwowane; wartość < 2⁶¹ mieści się w `INTEGER` SQLite bez znaku) |
  | 60–58 | ściana 0–5 |
  | 57–41 | region U (17 bitów) |
  | 40–24 | region V (17 bitów) |
  | 23–21 | komórka w regionie U (0–7) |
  | 20–18 | komórka w regionie V (0–7) |
  | 17–16 | slot 0–3 |
  | 15–0 | 0 (zarezerwowane) |

  `PropRegionKey(ulong Value)` = `PropId.Value >> 24` (ściana, region U, region V). `PropId.Create(face, u, v, slot)`, `Face`, `CellU`, `CellV`, `Slot`, `Region`. Kodeki i loader odrzucają wartość z niezerowymi bitami zarezerwowanymi, ścianą ≥ 6 albo komórką/regionem poza `N` planety (to ostatnie sprawdza walidator i `WorldStateBuilder`, kodek nie zna planety). Identyfikator **nie zależy od terenu** (tylko od siatki), więc serwer i klient zgadzają się co do tożsamości obiektu nawet przy różnicach zmiennoprzecinkowych wysokości.
- **D5. Układ obiektów V1 (`PropLayoutV1`, Core, zamrożony).** Czysta funkcja `(seed, PropId) → PropCandidate?` (`Kind`, przesunięcie w komórce w 1/1024 komórki, obrót w 1/65536 obrotu, skala w 1/1000), plus `Accepts(kind, heightOverRadius)` (pasma wysokości). Losowość: `XxHash64` (`System.IO.Hashing`, dozwolone w Core) z ziarna planety, `PropId` i stałej soli rodzaju; „łaty lasu” to szum wartości na siatce regionów z interpolacją w liczbach całkowitych (bez `Math.Sin`, `Math.Tan` w decyzjach). Kierunek kotwicy (`Vector3D` przez `Math.Tan` mapy równokątnej) nie wpływa na istnienie ani tożsamość. Tabela V1 (gęstości i pasma przeniesione z `PlanetFlora.Create`, wysokości względem R, stałe `PlanetLook` zapisane liczbowo, żeby zmiana wyglądu nieba nie przesuwała obiektów):

  | Rodzaj (`PropKindV1`) | Slot | Gęstość / komórkę (≈ 64 m²) | Pasmo wysokości nad R (m) | Szum łat (okres regionów, próg) |
  | --- | --- | --- | --- | --- |
  | `Pine = 1` | 0 | 0,224 | −34,5 … 125 | 3,5 / 0,15 |
  | `PineTall = 2` | 0 | 0,096 | −13,5 … 110 | 3,5 / 0,05 |
  | `Broadleaf = 3` | 0 | 0,160 | −35,5 … 70 | 4,7 / −0,05 |
  | `BroadleafAutumn = 4` | 0 | 0,077 | −28,5 … 90 | 2,8 / −0,25 |
  | `Bush = 5` | 0 | 0,300 (po drzewach) | −37,5 … 120 | 1,4 / 0,10 |
  | `Rock = 6` | 1 | 0,384 | −300 … 300 | — |
  | `Boulder = 7` | 1 | 0,077 | −300 … 300 | 2,5 / 0,0 |
  | `Crystals = 8` | 1 | 0,051 | 1,5 … 300 | 1,9 / −0,45 |

  Liczby to propozycja do strojenia w A1 (**DO USTALENIA-10**); po zatwierdzeniu złoty skrót `PropLayoutV1_GoldenDigest` (A6) je zamraża. Bez nachylenia i bez filtra materiału (wymagałyby dodatkowych próbek; V2 w K8-51). Zmiana V1 po scaleniu jest zabroniona — nowy wygląd = `PropLayoutV2` dla nowego profilu (reguła do ZASAD §3, obok wiersza `game/src/Planet/Generators/`). `PropLayouts.TryGet(string generatorProfile, out IPropLayout layout)`: `core:planet/1` → V1; nieznany profil → brak obiektów.
- **D6. Powierzchnia generatora: `ITerrainService.TrySampleGeneratedSurface`.** Nowa metoda `bool TrySampleGeneratedSurface(PlanetId planet, Vector3 direction, out float radius)` — promień powierzchni **generatora** (bez edycji gracza), tylko wątek główny, `false` dla nieznanej planety albo braku generatora. `VoxelTerrainService` liczy ją `PlanetSurfaceSampler.SurfaceRadius(direction)` (domyślne kroki 2 m / 0,125 m) z grafem planety tworzonym leniwie także dla planety bez aktywnego terenu (bez węzła terenu i bez strumienia). `FakeTerrainService` dostaje opcjonalne `GeneratedSurfaceRadius` (domyślnie `null` → `false`, więc istniejące testy nie widzą obiektów). Zmiana interfejsu Core bez podbicia wersji (nie jest typem zapisywanym; precedens `MeasureSphere`, K6-29W D9) — **do przeglądu Opus**. Klient rysuje obiekty z **tym samym** próbnikiem i tymi samymi krokami (D20), więc serwer i klient liczą kotwice tą samą ścieżką kodu.
- **D7. `PropField` (Core).** `PropField(WorldState state, ITerrainService terrain)`: `TryGetProp(PlanetId, PropId, out PropInstance)` (rodzaj, kotwica w ramie planety, kierunek „w górę”, skala; `false`, gdy układ nie przewiduje obiektu, pasmo wysokości go odrzuca albo nie da się spróbkować powierzchni) i `CollectInSphere(PlanetId, Vector3 center, float radius, List<PropInstance> result)` (komórki przecinające kulę, posortowane po `PropId`). Pamięć podręczna kotwic LRU `GameRules.PropAnchorCacheSize = 4096` (wątek główny). Używają go wyłącznie walidator, resolver i harmonogram odrastania — nigdy applier (ZASADY §4).
- **D8. Treść obiektów. DO USTALENIA-4.** Nowy plik `game/content/props/props.json` (`formatVersion` 1), `PropDef(string Key, ushort HitPoints, byte ToolTier, PropYield[] Yield, uint RegrowSeconds)`, `PropYield(string ItemKey, ushort Count)`; `ContentLoader.PropsFile = "props/props.json"`, `SurvivalContent.Props` + `TryGetProp(PropKindV1, out PropDef)`. Walidacja: każdy rodzaj V1 ma wpis, klucz znany (`core:pine` …), `hitPoints` 1–1000, `count` 1–999, 1–8 pozycji urobku, przedmiot znany; `HasSource` uwzględnia urobek obiektów. `ContentLoader.Parse` dostaje przeciążenie z `propsJson`; stare (trzy pliki) przekazuje pustą listę → reguły obiektów wyłączone (D21), więc testy K6-29W/K6-30 bez zmian. Nowe przedmioty: `core:wood` (Drewno), `core:fiber` (Włókno), `core:crystal` (Kryształ) — `resource`, `bulk`, stos 500. Propozycja danych:

  | Klucz | HP | Poziom | Urobek | Odrasta (s) |
  | --- | --- | --- | --- | --- |
  | `core:pine` | 6 | 1 | 4 × `core:wood` | 1200 |
  | `core:pine_tall` | 8 | 1 | 6 × `core:wood` | 1800 |
  | `core:broadleaf` | 6 | 1 | 4 × `core:wood` | 1200 |
  | `core:broadleaf_autumn` | 6 | 1 | 4 × `core:wood` | 1200 |
  | `core:bush` | 1 | 0 | 2 × `core:fiber` | 300 |
  | `core:rock` | 3 | 0 | 2 × `core:stone` | 0 (nie odrasta) |
  | `core:boulder` | 10 | 1 | 10 × `core:stone` | 0 |
  | `core:crystals` | 6 | 1 | 3 × `core:crystal` | 0 |

  Stałe ilości (bez losowania; losowość byłaby dozwolona w resolverze, ale niczego nie wnosi w EA). Czas w sekundach przelicza harmonogram przez `GameRules.TickRate` (nie applier).
- **D9. Poziomy narzędzi. DO USTALENIA-2, DO USTALENIA-9.** Poziom uderzenia w obiekt = przedmiot w slocie `ToolSlot` własnego `Hotbar`: z flagą `Tool` → `ItemDef.ToolTier`; pusty slot albo przedmiot bez `Tool` → 0 (ręka). Poziom < `PropDef.ToolTier` → `ToolTierTooLow` (53). Kopanie terenu (`TerrainToolIntent`, `Remove`, z treścią Przetrwania): poziom gracza = największy `ToolTier` przedmiotów `Tool` we własnym `Hotbar` (bez zmiany formatu `TerrainToolIntent`, zgodnie z K6-29W D14; `AllowCollapse` i tak zmieni ten format w K6-36); jeśli `MeasureSphere` zmierzył stałe woksele materiału, którego `TerrainYieldDef.ToolTier` > poziom gracza → `ToolTierTooLow`, teren bez zmian. Materiał bez wpisu wydajności (np. `core:bedrock`) nie ma wymagań (jak dziś). Pomiar niemożliwy (`MeasureSphere` = `false`) → zwykła edycja jak w K6-29W (bez sprawdzenia; serwer i tak nie trafi promieniem w niewczytany teren). Zasypywanie bez sprawdzenia poziomu (spec: poziom „otwiera materiały” do wydobycia). Przy obecnej treści (wszystkie wydajności poziom 1, manipulator poziom 1) zachowanie gracza z manipulatorem się nie zmienia; gracz bez narzędzia na pasku nie kopie.
- **D10. Wytrzymałość w pamięci serwera. DO USTALENIA-3.** `PropDamageLedger` (Core, `internal`, w `SurvivalRules`/`PropRules`): klucz (planeta, `PropId`) → (pozostałe HP, tick ostatniego uderzenia); obrażenia uderzenia = `1 + (poziom narzędzia − wymagany poziom)`; wpis wygasa (pełne HP) po `GameRules.PropDamageResetTicks = 1800` (30 s) bez uderzeń; najwyżej `GameRules.MaxDamagedProps = 4096` wpisów (najstarszy wypada). Gubiony przy restarcie (spec: „w pamięci serwera”). Uderzenie nieostatnie **nie jest operacją** (stan rozgrywki, nie świata): `IntentResult.AcceptedWithoutOperation()` (nowa fabryka: `Reason = None`, `Applied = null`), odpowiedź `IntentReply` z `AppliedOpId = OpId.None` (kodowanie już istnieje, `GameServerHooks.cs:142`); zmiana w księdze od razu. Ostatnie uderzenie: księga usuwa wpis dopiero po udanym `TryApply` (wzór `YieldLedger.Commit`).
- **D11. Zebranie.** Ostatnie uderzenie → `CompoundOp[PropHarvestOp(planeta, obiekt, CurrentTick), ItemCreateOp(Harvest)…]` z rozmieszczeniem `ItemPlacement.Create` jak urobek (pasek, potem plecak; limit stosu z `ItemDef` w każdej `ItemCreateOp`). Nic się nie mieści → sama `PropHarvestOp` (składowa bez złożonej). Nadmiar przepada, zbieranie nie jest blokowane **[odstępstwo, jak K6-29W D11; DO USTALENIA-8]**; klient pokazuje „Ekwipunek pełny”. Gracz bez kontenerów (`Hotbar`/`PlayerBag`) → sama `PropHarvestOp`.
- **D12. Kopanie i zasypywanie usuwa obiekty. DO USTALENIA-1.** Resolver `TerrainToolIntent` (z treścią obiektów) dokłada `PropHarvestOp` (bez urobku, tick = `CurrentTick`) dla każdego niezebranego obiektu z `PropField.CollectInSphere(środek, promień + GameRules.PropAnchorMarginMeters 0,5)` — kotwica w kuli edycji oznacza wykopane albo zasypane podłoże. Składowe obiektów stoją przed `TerrainEditOp` (ostatnią, K6-30 D12). Bez urobku, bo inaczej kopanie pod drzewem dawałoby drewno bez narzędzia o wymaganym poziomie. Takie obiekty nie odrastają (D14). Postawienie bloku na obiekcie niczego nie zmienia (obiekty nie mają kolizji, D19).
- **D13. `CompoundOp` — rozszerzenie zbioru składowych.** Dozwolone teraz: 31–37, 40, 44, **47, 48**, 49 oraz co najwyżej jedna składowa nieodwracalna ostatnia (bez zmian K6-30 D12). 47 i 48 są odwracalne (wpisy `UndoLog`), stosowane samodzielnie ścieżką składowej (`IsComponent`). Warunki appliera czytają tylko stan świata: 48 — planeta znana, obiekt niezebrany; 47 — planeta znana, obiekt zebrany. Istnienia w generatorze applier nie sprawdza (ZASADY §4, spec l. 270). `Tick` w 48 jest polem operacji (nie tickiem wpisu dziennika); applier go nie porównuje. Stare dzienniki nie mają 47/48, więc ich powtórka się nie zmienia (ZASADY §3 l. 68: korpus złoty i `DeterminismTests` bez zmian w plikach). Format złożonej zostaje 1.
- **D14. Odrastanie jako operacje serwera.** `PropRegrowth` (`src/Horyzont.Server/Props/`, `IServerTickParticipant`, co `GameRules.PropRegrowScanEveryTicks = 60` ticków) przegląda najwyżej `PropRegrowScanBatch = 256` wpisów zbioru zebranych od kursora (porządek magazynu, kursor zawija). Obiekt odrasta, gdy: `PropDef.RegrowSeconds > 0`; `CurrentTick ≥ tick + RegrowSeconds · TickRate`; blok terenu LOD 0 kotwicy nie ma wersji w `TerrainVersions` (teren nigdy nieedytowany — tak obiekty wykopane i zasypane w D12 nie odrastają nad dołem, bez nowej kolumny i bez migracji; skutek uboczny: flora zebrana w bloku 16 m, w którym ktoś kopał, też nie odrasta — **DO USTALENIA-1**); w promieniu `PropRegrowClearanceMeters = 20` od kotwicy nie ma gracza online ani bloku budowli (ta sama planeta, rama planety przez `WorldState.TryToPlanetFrame`). Zebrane w jednym przebiegu (najwyżej `MaxRegrowPerOperation = 64`) idą jako `PropRegrowOp` albo `CompoundOp` z samych 47 przez `ServerSimulation.ApplyServerOperation`. Obiekt, którego `PropField.TryGetProp` nie zna (zmiana treści), jest pomijany i logowany (`[LoggerMessage]`). Harmonogram tylko tworzy operacje; powtórka i checkpoint widzą ich skutki (ZASADY §4). Ticki nie płyną, gdy serwer stoi.
- **D15. Skrót stanu.** Warunek sekcji Przetrwania (`0x53`) dostaje `|| HarvestedProps.Count > 0`; w sekcji, po podsekcjach K6-30, podsekcja `0x48` dopisywana tylko gdy niepusta: wpisy po (planeta, `PropId`): varuint planeta, `UInt64` LE obiekt, `UInt64` LE tick. Skróty światów bez zebranych obiektów (K6-29W, K6-30) bez zmian (test A0).
- **D16. Zapis.** `HarvestedPropRecord(PlanetId Planet, PropId Prop, Tick Tick)`; `WorldCheckpoint.UpsertedHarvestedProps` / `RemovedHarvestedProps` (`init`, domyślnie puste, porządek po planecie i obiekcie); `ChangeTracker` śledzi klucze; `PersistenceWriter` w transakcji checkpointu: `INSERT OR REPLACE` / `DELETE`. `WorldLoader` czyta kolumny (bez bloba); `WorldStateBuilder.AddHarvestedProp`: planeta istnieje, klucz unikalny, bity zarezerwowane zero, komórka w siatce planety — inaczej `InvalidDataException` → `WorldCorruptedException`. Istnienia w generatorze builder nie sprawdza. `WorldCreator` odrzuca świat z zebranymi obiektami (jak z kontenerami). `tick` w SQLite to `INTEGER` ze znakiem: zapis i odczyt bit w bit (`unchecked((long)tick)` ↔ `unchecked((ulong)value)`), bez odrzucania (przegląd A1).
- **D17. `PropStates` (45) i zainteresowanie regionów. DO USTALENIA-5. [odstępstwo]** Spec każe wysyłać stan „razem z terenem regionu (K3-16)”; replikacja terenu wysyła tylko bloki edytowane i nie zna regionów obiektów, więc pakiet ma własne, prostsze zainteresowanie. `PropStates(PlanetId Planet, IReadOnlyList<PropRegionState> Regions)`, `PropRegionState(PropRegionKey Region, IReadOnlyList<HarvestedPropEntry> Props)`, `HarvestedPropEntry(PropId Prop, Tick Tick)` — **pełny** stan każdego wymienionego regionu (także pusty: czyści region u klienta). `PropReplicator` (`src/Horyzont.Server/Props/`, uczestnik ticku i obserwator graczy): co 30 ticków liczy regiony, których środek jest ≤ `PropSyncEnterMeters = 512` od postaci gracza (rama planety jego planety); wyjście z zainteresowania > `PropSyncLeaveMeters = 576`. Region wchodzący → wysyłka jego stanu (pusty też, partie ≤ `MaxPropRegionsPerMessage = 64` regionów i ≤ `MaxPropEntriesPerMessage = 4096` wpisów; najwyżej 256 regionów na gracza na przebieg, reszta w następnym). Po operacji z 47/48 (`OperationProps`) — pełny stan zmienionych regionów do graczy, którzy mają je w zainteresowaniu, po `OperationApplied` tej samej operacji. Klient zapomina region dalej niż `PropClientDropMeters = 768` (zawsze dalej niż wyjście serwera, więc ponowne wejście zawsze przynosi świeży stan). Kanał niezawodny uporządkowany (jak `BlockEntityStates`). Gracz lokalny hosta i solo nie dostaje komunikatów (świat współdzielony) — D18.
- **D18. Widok zebranych u klienta: `IPropStateView`.** `ClientPropStates` (mapa regionów z 45, klient zdalny) i `SharedPropStates` (czyta `WorldState.HarvestedProps` świata serwera w tym samym procesie, zdarzenie zmiany z `ServerSimulation.Applied` przez `OperationProps`) — od początku, wg lekcji K6-30 (odstępstwo „stany bloków lokalnego klienta solo i hosta”). Zdarzenie `RegionChanged(PlanetId, PropRegionKey)` dla rysowania.
- **D19. Uderzanie u klienta. DO USTALENIA-9, DO USTALENIA-7.** Promień z oka (`AimRayBuilder.EyeFromFeet`) w zasięgu `ReachMeters`: najbliższe trafienie w bryłę zastępczą obiektu (pień jako kapsuła: wysokość i promień z tabeli rodzajów V1 × skala; skała jako kula) przed trafieniem w teren → przy `ToolKind.None` (ręka) i `ToolKind.TerrainManipulator` lewy przycisk wysyła `PropHitIntent(planeta, obiekt, wybrany slot paska)` zamiast kopania; `Builder` bez zmian. Tempo: nowa `PaceClass.Prop` (4/s, jak kubełek serwera). Podpowiedź: „[LPM] Zetnij drzewo” / „[LPM] Rozbij skałę” / „Wymaga narzędzia poziomu N” (klient zna treść). Postęp uderzeń liczy klient lokalnie z treści (HP i obrażenia D10) — tylko informacyjnie, prawdą jest zebranie z serwera. Obiekty nie mają kolizji (jak dziś flora) — **DO USTALENIA-7**.
- **D20. Rysowanie obiektów. DO USTALENIA-5, DO USTALENIA-6.** `PropRenderer` (`game/src/Planet/Props/`): `MultiMeshInstance3D` na region i rodzaj z siatkami `flora.glb` (ten sam shader wiatru co `PlanetFlora`), regiony w promieniu `PropRenderRadiusMeters = 384` od kamery; kotwice liczone na wątku roboczym `PlanetSurfaceSampler` (wielowątkowy, K5-22) z budżetem regionów na klatkę; zebrane (z `IPropStateView`) pomijane; zmiana regionu przebudowuje jego siatki. `PlanetFlora` traci rodzaje rozgrywki (`Pine`, `PineTall`, `Broadleaf`, `BroadleafAutumn`, `Bush`, `Rock`, `Boulder`, `Crystals`) z wyjątkiem ewentualnych dalekich drzew (DO USTALENIA-6, rozstrzyga spike D3); trawa i kwiaty zostają. **Najpierw wygląd statyczny** (krok D4), ocena właściciela, potem interakcja.
- **D21. Bez treści obiektów.** Bez `SurvivalContent` albo z pustą listą obiektów: `PropHitIntent` → `InvalidParameters`, kopanie bez sprawdzania obiektów (zachowanie K6-29W), harmonogram i replikator nie działają, poziom narzędzia przy kopaniu sprawdzany tylko z treścią Przetrwania (bez niej K2). Testy K1/K2/K5/K6-29W/K6-30 bez zmian.
- **D22. Limity i stałe.** `GameRules`: `PropHitsPerSecond = 4`, `PropHitBurst = 4` (nowy kubełek `PlayerBuckets.Props`), `PropDamageResetTicks = 1800`, `MaxDamagedProps = 4096`, `PropAnchorCacheSize = 4096`, `PropAnchorMarginMeters = 0.5`, `PropRegrowClearanceMeters = 20`, `PropRegrowScanEveryTicks = 60`, `PropRegrowScanBatch = 256`, `MaxRegrowPerOperation = 64`, `PropSyncEnterMeters = 512`, `PropSyncLeaveMeters = 576`. `ProtocolConstants`: `MaxPropRegionsPerMessage = 64`, `MaxPropEntriesPerMessage = 4096` (wpis ≤ 20 B → ≤ 80 KiB < 256 KiB). Klient/gra: `PropClientDropMeters = 768`, `PropRenderRadiusMeters = 384`.
- **D23. Fikstura. DO USTALENIA-11.** `tests/Horyzont.Persistence.Tests/Fixtures/world-v3-props.db` z `FixtureGenerator.Generate_WorldV3Props` (kategoria `Manual`, uruchomiona raz): świat z treścią obiektów, kilkoma zebranymi obiektami w checkpoincie (flora i skała), zebraniem i odrośnięciem w ogonie dziennika. `world-v3.db` i `world-v3-blocks.db` bez zmian. Następna migracja testuje się też na tej fiksturze.
- **D24. Walidacja `PropHitIntent`.** Kolejność: ogólna z `IntentValidator.Validate` (gracz → online → parametry → kubełek `Props`), potem: bez treści obiektów → `InvalidParameters`; planeta ≠ planeta gracza → `WrongPlanet`; `ToolSlot` ≥ rozmiar `Hotbar` → `InvalidParameters`; `PropField.TryGetProp` = `false` → `InvalidParameters`; obiekt zebrany → `NoHit` (wyścig dwóch graczy przy jednym drzewie); odległość od oka serwera (`TryGetEye`) do odcinka pnia (kotwica → kotwica + wysokość × skala wzdłuż „w górę”) > `ReachMeters` + promień bryły → `OutOfReach`; teren między okiem a najbliższym punktem bryły (`ITerrainService.Raycast`) → `NoLineOfSight`; poziom → `ToolTierTooLow`. Kubełek zużywa także uderzenie nieostatnie.

## Kontrakty chronione i wersje

| Ścieżka (ZASADY §3) | Zmiana w K6-31 | Co podbić / jak udowodnić |
| --- | --- | --- |
| `src/Horyzont.Core/Operations/`, `Simulation/OperationApplier.cs` | nowe rodzaje 47, 48 (format 1); `OperationKind`; zbiór składowych złożonej (D13); `OperationProps` | nowe rodzaje mają rekord w wersji 1; rodzaje 1–37, 40, 44, 49 bez zmiany bajtów i wyniku powtórki starych dzienników — dowód: `git diff <commit startu A>..HEAD` na korpusie złotym i `DeterminismTests` pusty (ZASADY §3 l. 68); przegląd Opus |
| `src/Horyzont.Core/Intents/` | intencja 27; `RejectReason.ToolTierTooLow = 53`; `IntentResult.AcceptedWithoutOperation` | kod intencji z wersją rekordu 1 w `IntentCodec`; `ProtocolVersion` 16 |
| `src/Horyzont.Core/Ids/` | nowy `PropId`, `PropRegionKey` (typy zapisywane: dziennik, `harvested_props`, sieć) | nowe typy — format ustalany tu (D4), zamrożony w A1; zmiana później = nowa wersja operacji 47/48, komunikatu 45 i odczyt starej w Persistence |
| `src/Horyzont.Core/Props/` (nowy: `PropLayoutV1`, `PropLayouts`, `PropKindV1`) | układ obiektów V1 | **zamrożony jak `PlanetGenesis`/`StarSystemLayout.V1`**: złoty skrót `PropLayoutV1_GoldenDigest`; zmiana = `PropLayoutV2` dla nowego profilu; wiersz do ZASAD §3 |
| `src/Horyzont.Core/Checkpoints/` | `HarvestedPropRecord`, pola `WorldCheckpoint` (`init`, puste), `ChangeTracker`, `WorldStateBuilder.AddHarvestedProp` | bez bloba (kolumny tabeli z M0003); odczyt pustej tabeli ze świata K6-30 bez zmian |
| `src/Horyzont.Core/World/` | `WorldState.HarvestedProps`; `ITerrainService.TrySampleGeneratedSurface` | `WorldState` i `ITerrainService` nie są formatem — przegląd Opus (precedens `MeasureSphere`) |
| `src/Horyzont.Core/Content/` | `PropDef`, `PropYield`, `ContentLoader` (`props/props.json` format 1), `SurvivalContent.Props` | treść, nie format dziennika; plik ma własny `formatVersion` 1; obejmuje go `BuildHash` |
| `src/Horyzont.Core/Hashing/` | podsekcja `0x48` w sekcji Przetrwania (D15) | opis w PR: skróty światów z zebranymi obiektami są nowe; skróty światów K6-29W i K6-30 bez zmian (test A0) |
| `src/Horyzont.Core/Protocol/`, `Serialization/` | `MessageType.PropStates = 45`, kodeki 27/47/48/45, `MessageChannels`, `ProtocolConstants` (D22) | `ProtocolVersion` = 16 (D2); fixture X6; router X5 |
| `src/Horyzont.Core/Simulation/IntentValidator.cs`, `GameRules` | walidacja 27, poziom przy kopaniu, kubełek `Props`, stałe D22 | nie są ścieżką formatu (ZASADY §3 l. 70); przegląd Opus |
| `src/Horyzont.Persistence/Migrations/` | **brak zmian** (D3) | następny wolny numer M0004 nieużyty |
| `game/src/Planet/Generators/`, `TerrainChannels.cs`, `TerrainMaterials.cs` | **bez zmian** | `GeneratorV1_GoldenDigest` bez zmian |
| `game/content/items/items.json`, `game/content/props/props.json` (nowy) | nowe przedmioty i obiekty | `formatVersion` 1 bez zmian (przedmioty dopisane) |
| `tests/Horyzont.Core.Tests/Golden/TerrainJournals/`, `TerrainJournalGoldenCorpusTests.cs`, `DeterminismTests.cs` | **bez zmian** | `git diff` pusty |

## Projekt

### Pliki

```
src/Horyzont.Core/
  Ids/PropId.cs, PropRegionKey.cs (D4)
  Props/PropKindV1.cs, PropCandidate.cs, IPropLayout.cs, PropLayoutV1.cs (D5), PropLayouts.cs, CubeSphereGrid.cs (siatka równokątna:
        PropId ↔ kierunek, komórki przecinające kulę), PropInstance.cs, PropField.cs (D7), HarvestedPropStore.cs
  Operations/PropRegrowOp.cs, PropHarvestOp.cs, OperationKind.cs (47, 48), OperationProps.cs (rzut zmian obiektów ze złożonej, D18)
  Intents/PropHitIntent.cs, RejectReason.cs (53), IntentResult.cs (AcceptedWithoutOperation)
  Content/PropDef.cs, PropYield.cs, ContentLoader.cs (props/props.json, HasSource), SurvivalContent.cs (Props, TryGetProp)
  Simulation/OperationApplier.cs (47, 48 jako składowe, D13), UndoLog.cs (wpisy zbioru zebranych), PropRules.cs (wewnętrzna: walidacja
             i rozwiązanie 27, D9–D11, D24), PropDamageLedger.cs (D10), SurvivalRules.cs (poziom przy kopaniu, obiekty w kuli edycji, D9, D12),
             IntentValidator.cs (27, BucketFor), PlayerBuckets.cs (Props), GameRules.cs (D22), ServerSimulation.cs (PropRules, ścieżka
             przyjęcia bez operacji)
  World/WorldState.cs (HarvestedProps), World/Terrain/ITerrainService.cs (TrySampleGeneratedSurface, D6)
  Checkpoints/HarvestedPropRecord.cs, ChangeTracker.cs, WorldCheckpoint.cs, WorldStateBuilder.cs
  Hashing/StateHasher.cs (D15)
  Serialization/OperationCodec.cs (47, 48), IntentCodec.cs (27), MessageCodec.cs (45), CoreCodecPrimitives.cs (PropId)
  Protocol/MessageType.cs (45), MessageChannels.cs, ProtocolConstants.cs (16, limity), Messages/PropStates.cs, PropRegionState.cs,
           HarvestedPropEntry.cs
src/Horyzont.Persistence/
  WorldLoader.cs (harvested_props), WorldCreator.cs (odrzuca świat z zebranymi), Writing/PersistenceWriter.cs (upsert/usuwanie w transakcji
  checkpointu), Sql.cs (jeśli potrzebne stałe)
src/Horyzont.Net/Session/MessageRouter.cs (45 od serwera w stanach InGame jak BlockEntityStates)
src/Horyzont.Server/
  Props/PropRegrowth.cs (D14), PropReplicator.cs (D17), PropInterest.cs (regiony w zasięgu gracza, histereza), PropsLog.cs
  GameServerHooks.cs / ServerRuntime (rejestracja uczestników, jeśli nie przez binder)
src/Horyzont.Client/
  Props/IPropStateView.cs, ClientPropStates.cs, PropPicker.cs (promień → obiekt, bryły zastępcze z PropLayoutV1), PropInteraction.cs
        (uderzenie, podpowiedź, lokalny postęp), PropPrompt.cs
  Tools/PaceClass.cs (Prop), ToolController.cs albo ToolTargeting.cs (pierwszeństwo obiektu przed terenem dla ręki i manipulatora)
  Replication/ClientReplication.cs (45 → ClientPropStates)
game/
  content/props/props.json (D8), content/items/items.json (core:wood, core:fiber, core:crystal), locale/items.csv (ITEM_WOOD, …),
  locale/tools.csv (REJECT_TOOL_TIER_TOO_LOW, PROMPT_PROP_*)
  src/Terrain/VoxelTerrainService.cs (TrySampleGeneratedSurface: próbnik planety leniwie, także bez aktywnego terenu)
  src/Planet/Props/PropRenderer.cs (D20), PropRegionMesh.cs (zadanie robocze: kotwice regionu), SharedPropStates.cs (D18; w grze, bo
                   czyta świat serwera tego procesu — albo w Horyzont.Client, jeśli nie potrzebuje Godota)
  src/Planet/Rendering/PlanetFlora.cs (bez rodzajów rozgrywki; D20, DO USTALENIA-6)
  src/Client/Tools/ToolNode.cs (PropInteraction), src/Client/Props/PropPromptHud.cs
  src/Boot/GameComposition.Props.cs (nowa część: PropRegrowth, PropReplicator, IPropStateView, PropRenderer, PropInteraction),
       SurvivalContentLoader (props.json)
tests/  (nazwy w „Testy akceptacyjne”)
```

Nazwy nowych plików są propozycją; istniejące ścieżki sprawdzono w repozytorium. `SurvivalContentLoader` — sprawdzić dokładną nazwę i miejsce w `game/src/` przy C5 (K6-29W C5 go dodał).

### Kontrakty Core (format 1)

| Kod | Typ | Pola w kolejności kodeka | Warunki wstępne appliera |
| --- | --- | --- | --- |
| op 47 | `PropRegrowOp(PlanetId Planet, PropId Prop)` | varuint, varuint (`PropId`) | planeta istnieje w `WorldState.Planets`; obiekt jest w zbiorze zebranych → usunięcie wpisu (odrasta) |
| op 48 | `PropHarvestOp(PlanetId Planet, PropId Prop, Tick Tick)` | varuint, varuint, varuint | planeta istnieje; obiekt **nie** jest w zbiorze zebranych → wpis (planeta, obiekt, tick); istnienia w generatorze **nie** sprawdza (walidator, ZASADY §4) |
| intencja 27 | `PropHitIntent(PlanetId Planet, PropId Prop, ushort ToolSlot)` | varuint, varuint, varuint (`ToolSlot` < 256) | — (walidacja D24) |
| komunikat 45 | `PropStates(PlanetId Planet, IReadOnlyList<PropRegionState> Regions)` | varuint planeta, varuint liczba regionów (1…64), dla każdego: varuint `PropRegionKey`, varuint liczba wpisów, wpisy (varuint `PropId`, varuint tick) | S→K, niezawodny uporządkowany; suma wpisów ≤ 4096; regiony unikalne i rosnące; wpisy regionu rosnące, unikalne i należące do regionu (`PropId.Region == Region`) — inaczej `ProtocolViolationException` przed alokacją list |
| odmowa 53 | `ToolTierTooLow` | bajt | — |

- `PropId` w kodeku: varuint; odczyt wartości z niezerowymi bitami 63–61 albo 15–0 lub ścianą ≥ 6 → `ProtocolViolationException`; zapis takiej wartości → `ArgumentException` (wzór K6-29W/K6-30).
- Składowe 47/48 w `CompoundOp` dostają wpisy `UndoLog` (dodanie/usunięcie wpisu zbioru z poprzednią wartością tick). `AppliedOperation` nie niesie nowych pól; konsumenci biorą zmiany z operacji przez `OperationProps.CollectChanges(Operation, List<PropChange>)` (`PropChange(PlanetId, PropId, bool Harvested)`).
- `OperationConstructs.TryGetConstructPart` dla złożonej z samych 47/48 (i przedmiotów) zwraca `false` — `OperationApplied` nie niesie obiektów (spec Sieć l. 347; obiekty idą w 45). Złożona kopania z 48 przed `TerrainEditOp`: teren rozpoznaje `OperationTerrain.TryGetTerrainEdit` jak dotąd.
- Układ V1: dla slotu jeden rzut `XxHash64(seed, PropId, sól slotu) → [0, 1)` w liczbach całkowitych dzieli przedział między rodzaje slotu, których szum łat przechodzi próg (kolejność rodzajów jak w tabeli D5); nietrafienie = brak obiektu. Osobne skróty z innymi solami dają przesunięcie, obrót i skalę (zakres skali jak w `PlanetFlora`).

### Walidacja i rozwiązanie intencji

| Intencja | Sprawdzenia (kolejność = kod) | Wynik |
| --- | --- | --- |
| `PropHitIntent`, uderzenie nieostatnie | D24 | `IntentResult.AcceptedWithoutOperation()`; księga obrażeń −`1 + (poziom − wymagany)` |
| `PropHitIntent`, ostatnie uderzenie | D24 | `CompoundOp[PropHarvestOp, ItemCreateOp(Harvest)…]` albo sama `PropHarvestOp` (D11); po `TryApply` księga usuwa wpis |
| `TerrainToolIntent` `Remove` z treścią Przetrwania | walidacja K2-13 bez zmian; `MeasureSphere`; poziom narzędzia (D9) → `ToolTierTooLow`; urobek (K6-29W D10); obiekty w kuli (D12, tylko z treścią obiektów) | `CompoundOp[ItemCreateOp…, PropHarvestOp…, TerrainEditOp]` albo zwykła `TerrainEditOp`, gdy nic nie doszło |
| `TerrainToolIntent` `Add` z treścią Przetrwania | jak K6-29W D12 (koszt); obiekty w kuli (D12) | `CompoundOp[ItemDestroyOp…, PropHarvestOp…, TerrainEditOp]` |

Zasoby treści, terenu i generatora czyta tylko walidator, resolver i harmonogram; każda wartość (tick zebrania, liczba i limit stosu przedmiotów, które obiekty usunąć) trafia do operacji (ZASADY §4). Resolver obiektów działa w `ServerSimulation.Submit` przed `SurvivalRules.Resolve` (wzór `FunctionalBlockRules.TryResolve`); kopanie zostaje w `SurvivalRules.ResolveTerrainEdit`, które dostaje `PropField`.

### Przepływ intencja → walidator → operacja → replikacja → klient

```
Uderzenie i zebranie
  klient: promień z oka → PropPicker (bryły zastępcze z układu V1 i kotwic PropRenderer) → obiekt przed terenem, ≤ 8 m
          → LPM: PropHitIntent(planeta, obiekt, slot paska) przez IIntentSender (PaceClass.Prop, 4/s)
  serwer: IntentValidator (kubełek Props) → PropRules: D24 (PropField.TryGetProp, zasięg, linia wzroku, poziom)
          → nieostatnie: księga HP, IntentReply(None, OpId.None)
          → ostatnie: CompoundOp[PropHarvestOp, ItemCreateOp…] → OperationApplier.TryApply (D13) → dziennik → Applied:
             InventoryService: ContainerDelta do właściciela pasków
             PropReplicator: PropStates(planeta, [region z pełnym zbiorem]) do graczy z regionem w zainteresowaniu (D17)
             EntityReplication: brak OperationApplied (rzut pusty)
  klient zdalny: ClientPropStates.RegionChanged → PropRenderer przebudowuje region (obiekt znika)
  host/solo: SharedPropStates (z Applied przez OperationProps) → PropRenderer

Kopanie pod obiektem
  serwer: SurvivalRules: poziom (D9) → urobek → PropField.CollectInSphere → CompoundOp[ItemCreateOp…, PropHarvestOp…, TerrainEditOp]
          → ServerTerrainReplicator (OperationTerrain) + PropReplicator (OperationProps)

Odrastanie
  serwer: PropRegrowth co 60 ticków: tick ≥ zebranie + czas, blok kotwicy nieedytowany, nikogo i niczego w 20 m
          → ApplyServerOperation(PropRegrowOp | CompoundOp[47…]) → PropReplicator: PropStates z nowym zbiorem regionu
  klient: obiekt wraca w PropRenderer

Wejście w region / restart
  serwer: PropReplicator co 30 ticków: regiony ≤ 512 m od postaci → PropStates (także puste); > 576 m → wyjście
  restart: WorldLoader → harvested_props → HarvestedPropStore → klient po połączeniu dostaje te same PropStates
```

### Interfejs użytkownika (tylko to, co pakiet musi pokazać)

Styl i komponenty z K6-29W/K6-30 (`HudTheme`, etykiety w stylu `ToolHud`/`BlockPromptHud`); wygląd ocenia Dismond.

- **Obiekty w świecie**: drzewa, krzewy, skały, głazy i kryształy z układu V1 (siatki `flora.glb`, wiatr jak dziś), zebrane znikają, odrośnięte wracają. Bez animacji ścinania (K8-50).
- **Podpowiedź** (pod celownikiem, `PropPromptHud`): „[LPM] Zetnij drzewo” / „[LPM] Zetnij krzew” / „[LPM] Rozbij skałę” / „[LPM] Wydobądź kryształy”, poziom za niski: „Wymaga narzędzia poziomu N” (bez akcji); po uderzeniu krótki postęp „3/6” liczony lokalnie (D19).
- **Odmowy**: istniejący komunikat odmowy w HUD z tekstem PL/EN dla 53 („Za słabe narzędzie”) — także przy kopaniu terenu.
- **Urobek**: istniejąca lista zdobyczy z K6-29W (`+4 Drewno`) i ostrzeżenie „Ekwipunek pełny” (D11).

## Testy akceptacyjne (napisz najpierw)

Pisze Sonnet 5.5 przed kodem (ZASADY §8); testy etapów A i B zatwierdza Opus 5.5 przed implementacją (kroki A1, B1). Pomocnicze w `tests/Horyzont.Testing`: `FakeTerrainService.GeneratedSurfaceRadius` (płaska sfera; `null` = brak powierzchni), `SurvivalContentFactory.CreateTestWithProps()` (rodzaje V1 z małymi HP: drzewo 2, krzew 1, skała 1; poziomy 0/1/2; urobek do przedmiotów testowych; odrastanie 10 s i 0), `PropFixtures.FindProp(field, planet, near, kind)` (pierwszy obiekt danego rodzaju w pobliżu punktu, deterministycznie po `PropId`), generatory `CodecGen` dla 27, 47, 48 i 45 (X6).

### Etap A — model, operacje, kodeki, skrót, układ (Core)

| Test | Kryterium |
| --- | --- |
| `Ids/PropIdTests`: `Create_RoundTripsFaceCellsSlot`, `Region_IsHighBits_ContiguousInOrder`, `ReservedBitsSet_Invalid`, `FaceAbove5_Invalid`, `MaxGrid_FitsBelowTwoPow61` | D4 |
| `Props/CubeSphereGridTests`: `CellsPerFace_MultipleOf8_ApproxEightMeters`, `DirectionOfCell_InsideFace`, `CellOfDirection_InverseOfDirectionOfCell`, `CellsInSphere_ContainsAnchorCell_SortedById`, `FaceSeams_NoGapNoDuplicate` | D4, D7 |
| `Props/PropLayoutV1Tests`: `SameSeedAndId_SameCandidate`, `DifferentSeed_DifferentLayout`, `KindsOnlyInOwnSlot`, `HeightBand_RejectsOutside`, `Density_WithinTolerancePerKind` (10⁵ komórek, ± 3σ), `NoTranscendentalInDecision` (test: decyzja nie zmienia się przy zaburzeniu kierunku), `PropLayoutV1_GoldenDigest` (stała z A6, ziarno 42 i 1234, 2 regiony na ścianę) | D5; złoty skrót zamraża V1 |
| `Props/PropOpsApplierTests`: `PropHarvest_NotHarvested_Adds`, `PropHarvest_AlreadyHarvested_Fails`, `PropHarvest_UnknownPlanet_Fails`, `PropRegrow_Harvested_Removes`, `PropRegrow_NotHarvested_Fails`, `PropHarvest_DoesNotCallTerrainOrGenerator` (`FakeTerrainService` z licznikiem wywołań = 0) | tabela kontraktów; błąd nie zmienia skrótu |
| `Items/CompoundPropTests`: `HarvestWithItems_Compound_AddsPropAndItems`, `FailingItemComponent_PropNotHarvested`, `DigCompound_PropsBeforeTerrainLast_Applies`, `PropAfterTerrain_StructureFails`, `TwoHarvestsSameProp_SecondComponentFails_RolledBack`, `RegrowCompound_Of64_AllRemoved`, `FailedCompound_ThenStandalone_NoStaleUndo` | D13; przy błędzie skrót przed = po |
| `Props/PropConservationProperties` (FsCheck, 2000 sekwencji po 50 operacji: zebrania, odrośnięcia, złożone z losowym błędem) | po każdej operacji: zbiór zebranych = zbiór modelowy; suma sztuk przedmiotów = Σ create − Σ destroy |
| `Operations/OperationPropsTests`: `Changes_HarvestCompound_ListsProp`, `Changes_DigCompound_ListsProps`, `Changes_ItemOnly_Empty`, `ConstructPart_PropCompound_False` | D18; `OperationConstructs` bez zmian zachowania |
| `Serialization/OperationCodecPropTests`: round-trip 47, 48, złożona z 47/48; `ReservedBitsPropId_Read_Throws`, `ReservedBitsPropId_Write_Throws` | zapis `ArgumentException`, odczyt `ProtocolViolationException` |
| `Hashing/SurvivalHashTests` (dopisane): `WorldWithBlockEntitiesWithoutProps_HashUnchanged` (stała z A0), `HarvestProp_ChangesHash`, `HarvestThenRegrow_HashEqualsBefore` | D15; istniejące testy skrótu bez zmian |
| `Checkpoints/HarvestedPropCheckpointTests`: `Capture_Harvested_Included`, `Capture_Regrown_ListedAsRemoved`, `Requeue_MarksAgain`, `Builder_RestoresHarvested`, `Builder_UnknownPlanet_Throws`, `Builder_DuplicateKey_Throws`, `Builder_CellOutsidePlanetGrid_Throws`, `CheckpointPlusJournal_SameHash` | D16 |
| Bez zmian w plikach: `TerrainJournalGoldenCorpusTests`, korpus `Golden/TerrainJournals`, `DeterminismTests` i dotychczasowe testy (poza fixture X6 i złotymi wartościami enumów) | `git diff <commit startu A>..HEAD` na tych ścieżkach pusty |

### Etap B — treść, intencje, protokół (Core, Net)

| Test | Kryterium |
| --- | --- |
| `Content/PropContentTests`: `Parse_RepoContent_AllV1KindsHaveDefs`, `Parse_MissingKind_Throws`, `Parse_UnknownItem_Throws`, `Parse_HitPointsZero_Throws`, `Parse_YieldOver8_Throws`, `Parse_ThreeFileOverload_NoProps`, `HasSource_WoodFromPropsOnly_Accepted` | `InvalidDataException` ze ścieżką JSON |
| `Content/YieldDataTests`: `Dig_EveryRepoMaterial_YieldMatchesItemsPerCubicMeter` (treść z repo, `FakeTerrainService.MeasureResult` dla każdego materiału z `yields.json`), `HarvestEveryRepoKind_YieldMatchesPropsJson` | **kryterium spec: urobek zgodny z danymi** |
| `Props/PropFieldTests`: `TryGetProp_FlatSurface_AnchorOnSurface`, `TryGetProp_NoSurface_False`, `TryGetProp_UnknownProfile_False`, `CollectInSphere_ReturnsOnlyAnchorsInside`, `AnchorCache_SecondLookup_NoSurfaceCall` | D6, D7 |
| `Simulation/PropHitIntentTests`: `Hit_FirstOfThree_AcceptedWithoutOperation`, `Hit_Last_CompoundHarvestAndItems`, `Hit_HandOnTier1Tree_ToolTierTooLow`, `Hit_HandOnRock_Accepted`, `Hit_HigherTier_MoreDamage`, `Hit_EmptySlotIsHand`, `Hit_NonToolItemIsHand`, `Hit_SlotOutOfHotbar_InvalidParameters`, `Hit_UnknownProp_InvalidParameters`, `Hit_HarvestedProp_NoHit`, `Hit_OtherPlanet_WrongPlanet`, `Hit_Beyond8mFromTrunk_OutOfReach`, `Hit_TrunkTopWithinReach_Accepted`, `Hit_BehindHill_NoLineOfSight`, `Hit_WithoutPropContent_InvalidParameters`, `Hits_After30sPause_HitPointsReset`, `Hit_InventoryFull_HarvestWithoutItems`, `Burst_PropsBucket_RateLimited`, `TwoPlayersSameTree_SecondFinalHit_NoHit` | D9–D11, D21, D24 |
| `Simulation/DigToolTierTests`: `Dig_NoToolInHotbar_ToolTierTooLow`, `Dig_MultitoolTier1_Yields`, `Dig_MaterialTier2_Tier1Tool_ToolTierTooLow_TerrainUnchanged`, `Dig_MaterialWithoutYieldEntry_NoTierCheck`, `Dig_NotMeasurable_PlainEdit`, `Fill_NoTierCheck`, `Dig_WithoutSurvivalContent_K2Behaviour` | D9, D21; istniejące testy K6-29W bez zmian w plikach |
| `Simulation/DigRemovesPropsTests`: `Dig_TreeAnchorInSphere_PropHarvestWithoutItems`, `Fill_RockAnchorInSphere_PropRemoved`, `Dig_AnchorOutsideSphere_Untouched`, `Dig_AlreadyHarvested_NotRepeated`, `Dig_WithoutPropContent_NoPropComponents` | D12 |
| `Simulation/IntentValidationTests.AllReasons` (dopisane 53) | każdy nowy kod ma test walidacji |
| `Simulation/PropHitAllocationTests`: `RejectedHit_DoesNotAllocate` | ścieżka odmowy bez alokacji (ZASADY §6) |
| `Serialization/IntentCodecPropTests`: round-trip 27; `ToolSlotOver255_Read_Throws` | |
| `Serialization/MessageCodecPropStatesTests`: round-trip 45 (pusty region, kilka regionów); `Over64Regions_Throws`, `Over4096Entries_ThrowsBeforeAllocation`, `EntryOutsideRegion_Throws`, `UnsortedEntries_Throws`, `DuplicateRegion_Throws` | |
| `Protocol/ProtocolContractTests` (istniejące): `ProtocolVersion_MatchesDocumentedMergeValue` = 16, `Constants_GoldenValues` (D22), `Enums_GoldenValues` (op 47, 48; odmowa 53; komunikat 45; `PropKindV1`), lista rekordów komunikatów; `MaxMessages.All()`, `CodecGen` | X6 |
| `Net.Tests/MessageRouterTests` (dopisane) | 45 dozwolony od serwera w tych samych stanach co `BlockEntityStates`, niedozwolony od klienta |

### Etap C — zapis, serwer, gra (serwerowa część)

| Test | Kryterium |
| --- | --- |
| `Persistence.Tests/HarvestedPropPersistenceTests`: `Checkpoint_WritesHarvested_LoadRestores`, `Checkpoint_Regrown_DeletesRow`, `Tick_AboveLongMax_RoundTrips` (zapis i odczyt bit w bit, przegląd A1), `Row_ReservedBits_WorldCorrupted`, `Row_UnknownPlanet_WorldCorrupted` | D16 |
| `Persistence.Tests/MigrationTests`: istniejące testy fikstur bez zmian i zielone; nowy `FixtureWorldV3Props_CopiedToTemp_LoadsAndReplays` | `user_version` nadal 3 (D3) |
| `Server.Tests/Props/PropRegrowthTests`: `Regrow_AfterTime_NobodyNear_RegrowOp`, `Regrow_BeforeTime_Nothing`, `Regrow_PlayerWithin20m_Waits`, `Regrow_ConstructWithin20m_Waits`, `Regrow_EditedAnchorBlock_Never`, `Regrow_RockNeverRegrows`, `Regrow_ManyDue_BatchedMax64`, `Regrow_CursorWraps_AllVisited`, `Regrow_IdleScan_DoesNotAllocate` | D14; czas przez ticki (`AdvanceTick`), bez zegara |
| `Server.Tests/Props/PropReplicatorTests`: `EnterRegion_SendsStateEvenEmpty`, `Harvest_SendsRegionToInterestedOnly`, `Harvest_AfterOperationApplied`, `LeaveAndReenter_SendsFreshState`, `RegrowWhileAway_ReenterSeesRegrown`, `Batching_Max64RegionsPerMessage`, `LocalHostPlayer_NoMessages`, `Disconnect_InterestDropped`, `Idle_DoesNotAllocate` | D17 |
| `Integration.Tests/Props/PropsFlowTests` (2 klientów zdalnych → `InMemoryTransport` → sesja → symulacja → writer, `FakeTerrainService.GeneratedSurfaceRadius`, `FakeTimeProvider`): `TwoClients_HarvestTree_BothSeeHarvested`, `TwoClients_Regrow_BothSeeRegrown`, `Harvest_Restart_HarvestedIdentical_ClientsGetSameStates`, `Regrow_Restart_StateIdentical`, `CrashMidHarvest_WholeOrNothing` (oba punkty awarii, wzór K6-29W), `DigUnderTree_TreeGoneForBoth_NeverRegrows`, `HarvestYield_InHotbar_ConservationHolds` | **kryterium spec: zebrane i odrośnięte obiekty identyczne u 2 klientów i po restarcie**; skrót po powtórce = skrót przed |
| `game/tests/Terrain/GeneratedSurfaceTests` (gdUnit4, prawdziwy `VoxelTerrainService`, bez okna) | `TrySampleGeneratedSurface` = `PlanetSurfaceSampler.SurfaceRadius` bit w bit; działa dla planety bez aktywnego terenu; poza wątkiem głównym → wyjątek |
| `game/tests/Planet/PropFieldDigestTests` (gdUnit4) | `PlanetPropField_GoldenDigest`: kotwice i rodzaje obiektów z prawdziwym generatorem `core:planet/1` w dwóch regionach (stała zapisana w C5; zmiana = zmiana generatora albo V1) |

### Etap D — klient i gra

| Test | Kryterium |
| --- | --- |
| `Client.Tests/Props/ClientPropStatesTests`: `States_ReplaceRegion`, `EmptyRegion_Clears`, `RegionBeyond768m_Forgotten`, `RegionChanged_Raised`, `Disconnect_Clears` | D17, D18 |
| `Client.Tests/Props/PropPickerTests`: `RayHitsTrunkBeforeTerrain_PicksProp`, `TerrainBeforeTrunk_NoProp`, `HarvestedProp_NotPicked`, `Beyond8m_NoProp`, `RockSphere_Picked` | D19 |
| `Client.Tests/Props/PropInteractionTests`: `HandOnRock_PromptAndSendsHit`, `MultitoolOnTree_SendsHitNotDig`, `BuilderOnTree_NoHit`, `TierTooLow_PromptRequiresTier_SendsNothing`, `Pacing_Max4PerSecond`, `LocalProgress_CountsHits_ResetsOnHarvest`, `Rejected53_ShowsReason` | D19 |
| `game/tests/Planet/PropRendererTests` (gdUnit4, bez okna) | regiony w 384 m mają `MultiMeshInstance3D` z liczbą instancji = obiekty układu − zebrane; zmiana zbioru przebudowuje tylko ten region; `PlanetFlora` nie ma już rodzajów rozgrywki (z wyjątkiem decyzji DO USTALENIA-6) |
| `game/tests/Props/PropsSoloTests` (gdUnit4, prawdziwy serwer solo, bez okna) | LPM ręką na skale → po 3 uderzeniach skała znika z `PropRenderer`, kamień w pasku; host widzi to przez `SharedPropStates` |
| Spike D3 (gdUnit, `Manual`, wynik do planu) | czas liczenia kotwic regionu (p50/p95) na wątku roboczym; czy instancje `VoxelInstancer` z `LodIndex` 3 pojawiają się w promieniu 384 m |
| Do oceny przez właściciela (kategoria `Manual`, wynik w PR, `docs/reports/k6-31/`) | Dismond uruchamia grę i robi zrzuty: las i skały blisko i na horyzoncie, wygląd przed i po zebraniu, podpowiedź, odrośnięcie po czasie (z ustawionym krótkim czasem w treści testowej), 2 klientów — **wykonawca nie uruchamia gry z oknem** |

## Kroki

Każdy krok kończy się zielonym budowaniem i testami; commity w formacie Conventional Commits, małe. Kroki z „Przegląd Opus 5.5 przed kodem” kończą się commitem testów i **zatrzymaniem** do werdyktu; poprawki z przeglądu idą osobnym commitem `test(...): apply the A1 review` przed kodem (lekcja K6-30).

### Etap A — model, operacje, kodeki, skrót, układ (Core)

- [x] A0 (commit `646f130`, stała `17678442467951482955UL`). Zapisz `StateHasher.Hash` świata K6-30 ze stanami bloków i przedmiotami (`WorldFactory.CreateWithFunctionalBlocks()` + zestaw startowy) jako stałą testu `WorldWithBlockEntitiesWithoutProps_HashUnchanged`; zanotuj commit startowy etapu (do `git diff` korpusu i migracji).
- [x] A1 (testy napisane, czerwone: 55 z 57 nowych testów; **czeka na przegląd Opus 5.5 przed A2**). Testy z tabeli etapu A ze szkieletami typów (budowa może nie przechodzić). **Przegląd Opus 5.5 przed kodem:** kontrakty 47, 48, `PropId`/`PropRegionKey` (D4), tabela układu V1 i sposób losowania (D5), D13, D15, D16 oraz rozstrzygnięcie DO USTALENIA-1, 10, 11. Commit `test(core): add harvested prop contract tests`.
- [x] A2 (commit `feat(core): add prop ids and the harvested prop store`). `PropId`, `PropRegionKey`, `CubeSphereGrid`, `HarvestedPropStore` (posortowany po planecie i `PropId`, zakres regionu, zbiór zmienionych kluczy) w `WorldState`. Commit `feat(core): add prop ids and the harvested prop store`.
- [x] A3 (commit `feat(core): add prop harvest and regrow operations`). Operacje 47, 48 w applierze jako składowe z `UndoLog`; zbiór składowych złożonej (D13); `OperationProps`. Commit `feat(core): add prop harvest and regrow operations`.
- [x] A4 (commit `feat(core): encode and checkpoint harvested props`). Kodeki 47/48 (`PropId` w `CoreCodecPrimitives`), `HarvestedPropRecord`, `ChangeTracker`, `WorldCheckpoint`, `WorldStateBuilder`, `CaptureCheckpoint`/`Requeue`. Commit `feat(core): encode and checkpoint harvested props`.
- [x] A5 (commit `feat(core): hash harvested props`). `StateHasher` (D15) i test A0 zielony. Commit `feat(core): hash harvested props`.
- [x] A6 (commit `feat(core): add the frozen prop layout v1`, `PropLayoutV1_GoldenDigest` = 3687299226734270200). `PropKindV1`, `PropLayoutV1`, `PropLayouts`; złoty skrót `PropLayoutV1_GoldenDigest` zapisany **po** zatwierdzeniu tabeli w A1. Commit `feat(core): add the frozen prop layout v1`.
- [x] A7 (commit `docs(plans): record K6-31 stage A`). Testy właściwości zielone; sprawdzenie mutacją (usunięty warunek „obiekt niezebrany” w 48 → `PropHarvest_AlreadyHarvested_Fails` i test właściwości czerwone); korpus i `DeterminismTests` bez zmian w plikach.

### Etap B — treść, intencje, protokół

- [x] B1 (testy napisane, czerwone: 78 z 81 nowych w Core.Tests i 3 z 3 w Net.Tests; **czeka na przegląd Opus 5.5 przed B2**; commit `test(core): add harvesting intent tests`). Testy z tabeli etapu B. Przegląd Opus: intencja 27, odmowa 53, komunikat 45 (bajty i limity), `ITerrainService.TrySampleGeneratedSurface` (D6), `PropField` (D7), D9–D12, D17, D24, `IntentResult.AcceptedWithoutOperation` oraz DO USTALENIA-2, 3, 4, 8, 9. Commit `test(core): add prop hit and tool tier tests`.
- [x] B2. `RejectReason` 53; intencja 27 i `IntentCodec`; komunikat 45, `MessageCodec`, `MessageChannels`, `MessageRouter`; fixture X6; `ProtocolVersion` = 16 i jego testy. Commit `feat(protocol): add prop states and the prop hit intent`.
- [x] B3. Treść: `PropDef`, `PropYield`, `ContentLoader` (`props/props.json`, przeciążenie `Parse`, `HasSource`), `SurvivalContent.Props`; pliki `game/content/props/props.json` i przedmioty w `items.json`; nazwy przedmiotów w `items.csv`. Commit `feat(content): add harvestable props and their items`.
- [x] B4. `ITerrainService.TrySampleGeneratedSurface` (+ `FakeTerrainService.GeneratedSurfaceRadius`, `NoTerrain` w testach gry), `PropField`, `PropInstance`. `VoxelTerrainService` dostaje na razie zaślepkę `false` z kontrolą wątku (prawdziwa w C5, wzór K6-29W). Commit `feat(core): sample the generated surface for props`.
- [x] B5. `PropDamageLedger`, `PropRules` (walidacja D24 i rozwiązanie D10–D11), `IntentResult.AcceptedWithoutOperation`, `PlayerBuckets.Props`, `GameRules` (D22), wpięcie w `ServerSimulation.Submit` i `IntentValidator`; `SurvivalRules`: poziom przy kopaniu (D9) i obiekty w kuli (D12). Commit `feat(core): hit and harvest props with tool tiers`.
- [x] B6 (przegląd Core A+B wykonany, poprawki w commicie `fix(core): apply the core A+B review`; patrz „Etap B, krok B6" niżej). Sprawdzenie (niżej) i **przegląd Core A+B przez Opus 5.5** (jak w K6-30: `TryApplyCompound` z 47/48, czyszczenie stanu na każdym wyjściu, `PropRules`, kodeki, `ProtocolVersion`); poprawki w jednym commicie `fix(core): apply the core A+B review` przed C2.

### Etap C — zapis, serwer, gra (serwerowa część)

- [x] C1 (testy napisane, czerwone; patrz „Etap C, krok C1" niżej). Testy z tabeli etapu C. Commit `test: add harvested prop persistence, regrowth and replication tests`.
- [x] C2 (patrz „Etap C, krok C2" niżej). Persistence: `WorldLoader`, `PersistenceWriter`, `WorldCreator`; fikstura `world-v3-props.db` (`FixtureGenerator.Generate_WorldV3Props`, `Manual`). Commit `feat(persistence): store harvested props`.
- [x] C3 (patrz „Etap C, krok C3" niżej). `PropRegrowth` (D14) jako uczestnik ticku; rejestracja w serwerze. Commit `feat(server): regrow flora as server operations`.
- [ ] C4. `PropInterest`, `PropReplicator` (D17); przegląd grepem `PropHarvestOp|PropRegrowOp` — każdy konsument przez `OperationProps`; sprawdzenie, że `EntityReplication` nie wysyła złożonej z samych przedmiotów i obiektów. Commit `feat(server): replicate harvested props by region`.
- [ ] C5. Gra: `VoxelTerrainService.TrySampleGeneratedSurface` (próbnik planety leniwie, wątek główny, te same kroki co `PlanetSurfaceSampler.SurfaceRadius(direction)`), wczytanie `props.json`, `GameComposition.Props.cs` (część serwerowa); gdUnit `GeneratedSurfaceTests`, `PropFieldDigestTests` (stała zapisana teraz). Commit `feat(game): sample the generator surface for props on the server`.
- [ ] C6. Sprawdzenie (z `dotnet test game` i `smoke` — reguła wspólnego runnera: przed przebiegiem brak procesów Godota i potoku gdUnit).

### Etap D — klient (wygląd ocenia Dismond)

- [ ] D1 (testy napisane, czerwone). Testy z tabeli etapu D. Commit `test(client): add prop state, picking and hit tests`.
- [ ] D2. `src/Horyzont.Client/Props/*` (`IPropStateView`, `ClientPropStates`, `PropPicker`, `PropInteraction`, `PropPrompt`), `ClientReplication` (45), `PaceClass.Prop`; `SharedPropStates` dla hosta i solo (D18). Commit `feat(client): track harvested props and hit them`.
- [ ] D3 (spike, kategoria `Manual`, bez okna). Pomiar czasu kotwic regionu na wątku roboczym i zachowania `VoxelInstancer` z `LodIndex` 3 w pobliżu; wynik i rozstrzygnięcie DO USTALENIA-5 i 6 w „Odstępstwa i uwagi z realizacji”. Commit `test(game): measure prop anchor sampling` (sam test i notatka).
- [ ] D4. **Najpierw wygląd statyczny:** `PropRenderer` (bez interakcji), zmiana `PlanetFlora` (D20); gdUnit `PropRendererTests` (struktura, liczby instancji). **Wygląd do oceny przez Dismonda** — zrzuty lasu, skał i horyzontu robi właściciel (wykonawca nie uruchamia gry z oknem); interakcja (D5) czeka na akceptację wyglądu albo idzie dalej z adnotacją „wygląd nieoceniony” w PR, jeśli właściciel tak zdecyduje. Commit `feat(game): render generator props from layout v1`.
- [ ] D5. Wejście: LPM ręką i manipulatorem na obiekcie (`ToolNode`, `ToolInputReader` bez nowej akcji), `PropPromptHud`, lokalny postęp; gdUnit `PropsSoloTests`. Commit `feat(game): hit props with the hand and the multitool`.
- [ ] D6. Teksty PL/EN (`items.csv`, `tools.csv`), import tłumaczeń bez okna (`tools/ci/godot_import.sh`). Commit `feat(game): translate harvesting texts`.
- [ ] D7. Sprawdzenie, opis PR z odstępstwami i DO USTALENIA; lista „do oceny przez właściciela” (zrzuty i test ręczny 2 klientów).

### Sprawdzenie na końcu każdego etapu

```
dotnet build Horyzont.sln
dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"
dotnet format Horyzont.sln --verify-no-changes
dotnet build game/Horyzont.Game.sln && dotnet format game/Horyzont.Game.sln --verify-no-changes   # etapy C i D
dotnet test game/Horyzont.Game.csproj --settings game/.runsettings                                  # etapy C i D (gdUnit4, --headless, bez okna)
python tools/engine/build.py smoke                                                                  # etapy C i D
git diff <commit startu A>..HEAD -- tests/Horyzont.Core.Tests/Golden/TerrainJournals tests/Horyzont.Core.Tests/TerrainJournalGoldenCorpusTests.cs tests/Horyzont.Core.Tests/Hashing/DeterminismTests.cs   # pusty
git diff <commit startu A>..HEAD -- src/Horyzont.Persistence/Migrations game/src/Planet/Generators   # pusty (D3, ZASADY §3)
```

Nie uruchamiać gry z oknem, `--engine-tests` ani `tools/server_smoke.py` bez zgody właściciela; pliki `.uid` i `game/locale/ui.csv.import` nie należą do pakietu (zostawić).

## Ryzyka i odstępstwa

| Ryzyko | Skutek | Odpowiedź |
| --- | --- | --- |
| Różnica zmiennoprzecinkowa próbnika generatora między maszynami (serwer dedykowany, inny procesor) | obiekt na granicy pasma wysokości istnieje tylko u klienta albo tylko na serwerze | tożsamość niezależna od wysokości (D4), decyzje układu w liczbach całkowitych (D5); skutek nieszkodliwy (uderzenie → `InvalidParameters`, obiekt niewidoczny); złote skróty `PropLayoutV1_GoldenDigest` i `PlanetPropField_GoldenDigest`; ta sama ścieżka próbnika po obu stronach (D6) |
| Koszt próbkowania powierzchni (≈ 2 promienie SDF na kotwicę) | przycięcia przy wczytywaniu regionów u klienta, opóźnienie walidacji na serwerze | wątek roboczy z budżetem u klienta, LRU kotwic na serwerze (D7), spike D3 z pomiarem i decyzją promieni (DO USTALENIA-5) |
| Usunięcie drzew z `VoxelInstancer` łysi horyzont | gorszy wygląd z daleka | DO USTALENIA-6 (spike D3); daleki las jako impostor w K8-50 |
| Rozbieżność zbioru zebranych u klienta (zgubiony region, histereza) | drzewo widoczne mimo zebrania | pełny stan regionu przy każdym wejściu i zmianie (D17); klient zapomina dalej niż serwer wychodzi; test `RegrowWhileAway_ReenterSeesRegrown` |
| Konsument rozpoznaje 47/48 po typie i nie zna złożonej | obiekt wykopany razem z terenem nie znika u klientów | `OperationProps` w każdym konsumencie (D18), reguła do ZASAD, grep w C4 |
| Gracz hosta i solo bez komunikatów replikacji | brak aktualizacji obiektów lokalnie | `SharedPropStates` od początku (lekcja K6-30), test `PropsSoloTests` |
| Zmiana `ServerSimulation.Submit` (przyjęcie bez operacji) | wyjątek `Applied!` w konsumentach, liczniki | `GameServerHooks` już mapuje `null`; grep `\.Applied!` i `result.Applied` przy B5; test `Hit_FirstOfThree_AcceptedWithoutOperation` |
| Zmiana zachowania kopania (poziom narzędzia) | gracz bez manipulatora na pasku nie kopie | tylko z treścią Przetrwania (D9, D21); istniejące testy K6-29W bez zmian w plikach; opis w PR |
| Duży zbiór zebranych (kopanie w lesie, nieodrastające skały) | rośnie `harvested_props` i skrót | wpis ≈ 24 B; obiekty nieodrastające to rzadkie skały i wykopane obszary; pomiar w teście `Soak` K4-19, gdy będzie potrzebny |
| Równoległe pakiety K6 (K6-32, K6-33, K6-36) zmieniają `SurvivalRules`, enumy, `StateHasher`, `ContentLoader` | konflikty przy scaleniu | zakresy kodów rozdzielone; integruje pakiet scalany później (README); `ProtocolVersion` wg D2 |

Odstępstwa od spec 03 (do opisu PR):

- **[odstępstwo] D11:** nadmiar urobku z obiektu przepada (przedmioty leżące nie istnieją), zbieranie nie jest blokowane — jak K6-29W D11.
- **[odstępstwo] D17:** `PropStates` ma własne zainteresowanie regionów (512/576 m od postaci), nie idzie „razem z terenem regionu”; format komunikatu jak w spec (zebrane obiekty regionu z tickiem).
- **[uzupełnienie spec] D12, D14:** kopanie i zasypywanie usuwa obiekty z kotwicą w kuli edycji (bez urobku), a flora nie odrasta w edytowanym bloku terenu — spec milczy.
- **[uzupełnienie spec] D10:** uderzenie nieostatnie jest przyjęte bez operacji (`AppliedOpId = 0`).
- **Bez kolizji obiektów** (DO USTALENIA-7) — spec milczy, dziś flora też ich nie ma.

## Odbiór (ZASADY §11)

- [ ] Testy akceptacyjne z tego planu przechodzą w CI (w tym `dotnet test game` i `smoke`); korpus złoty, `DeterminismTests`, migracje i generatory bez zmian w plikach.
- [ ] Przegląd Opus 5.5 w PR (zmiana kontraktów Core: operacje 47, 48, `PropId`, układ V1, intencja 27, odmowa 53, komunikat 45, `ITerrainService`, `ProtocolVersion` 16), uwagi rozwiązane; wygląd oceniony przez Dismonda.
- [ ] Brak `TODO` bez odnośnika do zgłoszenia.
- [ ] Checklista kroków odhaczona; odstępstwa i rozstrzygnięcia DO USTALENIA opisane w sekcji „Odstępstwa i uwagi z realizacji” tego planu i w PR.
- [ ] Nowe zasady dopisane do `docs/ZASADY.md` (z Pułapek oznaczonych „do ZASAD”).
- [ ] Kryterium spec: urobek zgodny z danymi (`YieldDataTests`); zebrane i odrośnięte obiekty identyczne u 2 klientów i po restarcie (`PropsFlowTests`).

## DO USTALENIA (zbiorczo, z propozycją)

| # | Kwestia | Propozycja | Kiedy |
| --- | --- | --- | --- |
| 1 | Obiekty a edycje terenu | kopanie/zasypywanie usuwa obiekty z kotwicą w kuli, bez urobku; flora nie odrasta w edytowanym bloku 16 m (D12, D14) | A1 |
| 2 | Poziom narzędzia przy kopaniu | najlepsze narzędzie z paska, bez zmiany formatu `TerrainToolIntent`; niemierzalne jak dotąd; zasypywanie bez poziomu (D9) | B1 |
| 3 | Obrażenia i reset HP | `1 + (poziom − wymagany)`, reset po 30 s, 4096 wpisów (D10) | B1 |
| 4 | Dane obiektów i nowe przedmioty | tabela D8 (`core:wood`, `core:fiber`, `core:crystal`), strojone bez zmiany kodu | B1 |
| 5 | Promienie synchronizacji i rysowania | 512 / 576 / 768 m, rysowanie 384 m — potwierdzić pomiarem w D3 (D17, D20) | B1, D3 |
| 6 | Daleki las z `VoxelInstancer` | zostawić drzewa `LodIndex` 3 tylko, jeśli spike D3 pokaże, że nie pojawiają się w 384 m; inaczej usunąć (łysy horyzont do K8-50) | D3 |
| 7 | Kolizje obiektów | brak w K6-31 (jak dziś flora); kolizje w K8-50/K8-51 | A1 |
| 8 | Przepełnienie urobku z obiektu | przepada, zbieranie nieblokowane (D11) — pytanie do właściciela w PR, jak K6-29W | B1 |
| 9 | Czym się uderza | ręka i manipulator terenu (LPM na obiekcie zamiast kopania), narzędzie budowy nie (D19) | B1 |
| 10 | Siatka i układ V1 | komórka ≈ 8 m, region 8 × 8, sloty 0/1, tabela D5 z gęstościami `PlanetFlora` (krzew zmniejszony do 0,3) — **zamrażane**, więc decyzja przed A6 | A1 |
| 11 | Fikstura | nowa `world-v3-props.db`, starsze bez zmian (D23) | A1 |

## Pułapki

- **Rozpoznawanie zmian obiektów po typie.** Każde miejsce, które patrzy na `PropHarvestOp`/`PropRegrowOp`, używa `OperationProps.CollectChanges`, bo bywają składowymi `CompoundOp` (zebranie z urobkiem, kopanie pod drzewem, odrastanie wsadowe). (do ZASAD, §2, obok `OperationTerrain` i `OperationConstructs`)
- **Układ obiektów jest zamrożony.** `PropLayoutV1` i siatka `PropId` nie zmieniają się po scaleniu (złoty skrót); inny wygląd lub gęstość = `PropLayoutV2` dla nowego profilu generatora. (do ZASAD, §3, nowy wiersz obok `game/src/Planet/Generators/`)
- **Applier nie pyta generatora.** Istnienie obiektu, kotwicę, zasięg i poziom sprawdza walidator; operacje niosą tick i przedmioty. `PropField` nie może trafić do `OperationApplier`.
- **Ta sama ścieżka próbnika.** Serwer (`TrySampleGeneratedSurface`) i klient (`PropRenderer`, `PropPicker`) liczą kotwice tym samym `PlanetSurfaceSampler.SurfaceRadius(direction)` z domyślnymi krokami; inny krok = inne kotwice. (do ZASAD, §2)
- **Wytrzymałość to nie stan świata.** HP obiektu żyje w pamięci serwera (`PropDamageLedger`), nie w checkpoincie, skrócie ani dzienniku; tylko zebranie jest operacją. (do ZASAD, §4: przykład obok „zdarzenia czasowe są operacjami”)
- **Gracz lokalny hosta.** Replikatory pomijają lokalnego gracza; widok po stronie hosta i solo czyta świat serwera (`SharedPropStates`) — tak jak `SharedBlockEntities` w K6-30.
- **Pełny stan regionu.** `PropStates` zawsze niesie cały zbiór regionu (także pusty); delta bez wersji gubi się przy histerezie.
- **Kolejność w ticku.** `PropStates` po `OperationApplied` tej samej operacji; uczestnik replikatora rejestrowany po `EntityReplication`.
- **Wątki.** `ITerrainService.TrySampleGeneratedSurface` tylko na wątku głównym; klient próbkuje na wątku roboczym własnym próbnikiem (graf jest wielowątkowy), nigdy przez `ITerrainService`.
- **Tłumaczenia.** Nowe klucze w CSV wymagają importu (`--import`, bez okna); bez tego etykiety pokażą klucze.
- **Łaty lasu V1 urywają się na krawędziach ścian.** Sieć szumu łat jest osobna dla każdej ściany siatki; cecha zamrożona złotym skrótem (zmiana = `PropLayoutV2`).
- **Linia wzroku do obiektu sprawdza tylko teren.** Budowle nie zasłaniają drzewa ani skały przy uderzeniu (D24); ewentualne zasłanianie przez budowle to osobna zmiana walidatora.
- **Odmowa nie zużywa tokenu kubełka, więc musi być tania.** `PropField` pamięta także „kandydat istnieje, ale pasmo wysokości go odrzuca”; nowa ścieżka odmowy, która próbkuje powierzchnię albo alokuje, otwiera serwer na zalewanie intencjami.
- **Rebase.** `ProtocolVersion` (D2), kolizje kodów z równoległymi pakietami K6 (tylko pliki enumów), numer migracji nie dotyczy (D3).

## Odstępstwa i uwagi z realizacji

(Wypełnia wykonawca po każdym etapie: commity, werdykty przeglądów A1/B1/Core A+B, rozstrzygnięcia DO USTALENIA, wyniki sprawdzeń z liczbą testów, czego nie uruchomiono i dlaczego.)

### Etap A, kroki A0 i A1 (do przeglądu Opus 5.5 przed A2)

**Commity.** Kod bazowy etapu: `a1781ee` (tylko dokumentacja ponad K6-30); `646f130` = A0, od niego liczy się `git diff <commit startu A>..HEAD` korpusu złotego, `DeterminismTests`, migracji i generatorów (w A0 i A1 w tych ścieżkach nic się nie zmieniło). A1: `test(core): add harvested prop contract tests`.

**A0.** `WorldWithBlockEntitiesWithoutProps_HashUnchanged` w `SurvivalHashTests`: stała `17678442467951482955UL` = `StateHasher.Hash` świata `WorldWithBlockEntities()` policzona na czystym kodzie `a1781ee` (dwa uruchomienia, ta sama wartość). Świat: `FunctionalBlockFixture(players: 2)` (`WorldFactory.CreateWithFunctionalBlocks`), zestaw startowy (narzędzie 1 szt. i klejnoty 3 szt. w pasku gracza 1, 40 kamienia w plecaku), skrzynia z kontenerem 8 slotów i 10 kamienia (slot 2), łóżko gracza 1 z punktem odrodzenia, otwarte drzwi (stan funkcji 1).

**Sygnatury szkieletów A1 (puste, `// K6-31 A2…A6`, do przeglądu).**

| Typ | Sygnatura / uwaga |
| --- | --- |
| `Ids/PropId`, `PropRegionKey` | `readonly record struct PropId(ulong Value)`: `Create(int face, int cellU, int cellV, int slot)` (`cellU`/`cellV` to indeks komórki na ścianie, nie regionu), `Face`, `CellU`, `CellV`, `Slot`, `Region`, `IsValid`, stała `MaxCellsPerFace = 1 << 20`; `PropRegionKey(ulong Value)` z `Face`, `RegionU`, `RegionV`. Układ bitów jak D4; testy pakują wartość ręcznie (`PropFixture.Raw`), więc testy operacji nie zależą od `Create` |
| `Props/CubeSphereGrid` (statyczna) | `CellsPerFace(float planetRadius)`, `DirectionOfCell(face, u, v, cellsPerFace)` (jednostkowy `Vector3`), `CellOfDirection(Vector3, cellsPerFace, out face, out u, out v)`, `CellsInSphere(Vector3 center, float sphereRadius, float planetRadius, List<PropId> cells)` (dopisuje identyfikatory slotu 0, rosnąco, bez czyszczenia). **Kolejność ścian zamrażana testem: 0 +X, 1 -X, 2 +Y, 3 -Y, 4 +Z, 5 -Z** (propozycja, do zatwierdzenia razem z D4) |
| `Props/IPropLayout`, `PropCandidate`, `PropKindV1`, `PropLayoutV1`, `PropLayouts` | `TryGetCandidate(ulong seed, PropId id, out PropCandidate)`, `Accepts(PropKindV1, float heightOverRadiusMeters)`; `PropCandidate(Kind, ushort OffsetU, ushort OffsetV, ushort Rotation, ushort Scale)`; `PropKindV1` `None = 0`, `Pine = 1` … `Crystals = 8`; `PropLayouts.TryGet(string profile, out IPropLayout?)`. Układ nie dostaje kierunku (tylko ziarno i `PropId`), wysokość wchodzi dopiero w `Accepts` |
| `Props/HarvestedPropStore`, `WorldState.HarvestedProps` | `Count`, `All` (posortowane po planecie i `PropId`), `Contains`, `TryGetTick(planet, prop, out Tick)`, `CollectRegion(planet, region, List<HarvestedPropRecord>)`; mutatory `internal` dochodzą w A2 |
| `Operations/PropRegrowOp`, `PropHarvestOp`, `PropChange`, `OperationProps` | `PropRegrowOp(PlanetId, PropId)`, `PropHarvestOp(PlanetId, PropId, Tick)`, `Kind` to rzutowanie `(OperationKind)47/48` (enum dopisuje A3); `PropChange(PlanetId, PropId, bool Harvested)`; `OperationProps.CollectChanges(Operation, List<PropChange>)` dopisuje zmiany w kolejności składowych |
| `Checkpoints/` | `HarvestedPropRecord(PlanetId, PropId, Tick)`, **`HarvestedPropKey(PlanetId, PropId)` (nowy typ, nieobecny w planie: klucz listy usuniętych)**, `WorldCheckpoint.UpsertedHarvestedProps` / `RemovedHarvestedProps`, `WorldStateBuilder.AddHarvestedProp(HarvestedPropRecord)` |

**Bajty złote w testach (`OperationCodecPropTests.GoldenBytes_PropOperations`, `ReservedBitsPropId_Read_Throws`).** Rodzaj 48 = `0x30`, 47 = `0x2F`, wersja formatu 1, potem varuint-y (LEB128):

- `PropHarvestOp(1, face 1/u 0/v 0/slot 1, tick 300)` = `30 01 01 80 80 84 80 80 80 80 80 04 AC 02`;
- `PropHarvestOp(1, face 0/u 9/v 2/slot 0, tick 0)` = `30 01 01 80 80 A0 81 80 40 00`;
- `PropRegrowOp(1, face 0/u 9/v 2/slot 0)` = `2F 01 01 80 80 A0 81 80 40`; `PropRegrowOp(7, face 1/u 0/v 0/slot 1)` = `2F 01 07 80 80 84 80 80 80 80 80 04`.

Odczyt `PropId` z bitem 61 (ostatnia grupa `24`), bitem 0 (pierwszy bajt `81`) albo ścianą 6 (ostatnia grupa `18`) → `ProtocolViolationException`; zapis takiej wartości → `ArgumentException`.

**Złoty skrót układu V1.** `PropLayoutV1_GoldenDigest` ma na razie stałą `0` (czerwony do A6, zgodnie z krokiem A6): `XxHash64` po kandydatach ziaren 42 i 1234, regiony (0,0) i (3,5) każdej ściany, sloty 0-3, rekord 17 B na slot (id LE, rodzaj, przesunięcia, obrót, skala). Test gęstości: 317 × 317 komórek ściany 0; skały w ± 3σ od 0,384 (brak szumu łat), pozostałe rodzaje najwyżej gęstość + 3σ i co najmniej jedna sztuka (szum łat tnie gęstość, więc dokładnej gęstości nie da się twierdzić bez wartości szumu) — **interpretacja tabeli D5 do zatwierdzenia**. Zakresy skali per rodzaj w `CandidateFields_WithinRanges` to zakresy `PlanetFlora` (Pine 0,7–1,3 … Boulder 0,6–2,2).

**Odstępstwa od tabeli testów.**

- `OperationPropsTests` leży w `tests/Horyzont.Core.Tests/Props/` (przestrzeń `Horyzont.Core.Tests.Props`), nie w `Operations/`: przestrzeń `Horyzont.Core.Tests.Operations` przesłania `Operations.…` w istniejącym `EntityIdAllocatorTests` (nie wolno go zmieniać).
- `HarvestThenRegrow_HashEqualsBefore` porównuje świat po zebraniu i odroście z innym światem po tej samej liczbie operacji (zebranie i odrost innego obiektu), bo skrót zawiera `LastOpId`; dodatkowo `Count == 0`.
- Dodane testy poza tabelą (kontrakt): `HarvestedPropsOnly_InWorldWithoutItems_ChangeHash` (D15: sekcja Przetrwania także dla świata tylko z zebranymi), `CellsInSphere_SmallPlanet_MatchesBruteForce`, `CandidateFields_WithinRanges`, `Layouts_CoreProfile_ReturnsV1_UnknownProfileNone`, `HarvestedProps_SortedByPlanetThenProp_RegionRangeIsContiguous`, `Generator_ProducesAcceptedAndRefusedOperationsOfEveryKind`.
- `PropHarvest_DoesNotCallTerrainOrGenerator` używa istniejących liczników `FakeTerrainService` (`MeasureCalls`, `SolidSamples`, `AppliedEdits`, `BlockingLoads`, `LoadedAreas`); `Raycast` nie ma licznika. Generator wejdzie w etapie B (`TrySampleGeneratedSurface`).
- Testy odmów zaczynają od poprawnego odpowiednika (zebranie i odrost zwykłe oraz w złożonej), więc nie przechodzą trywialnie na braku operacji.

**Wynik.** `dotnet build Horyzont.sln` 0 błędów / 0 ostrzeżeń; `dotnet format --verify-no-changes` czysto. `dotnet test --filter "Category!=Soak&Category!=Manual"`: wszystkie projekty poza `Horyzont.Core.Tests` zielone; `Horyzont.Core.Tests` 1865 testów, 1809 zielonych, 56 czerwonych = 55 nowych (brak zachowania: operacje 47/48 jako nieznane, szkielety `PropId`, siatki i układu, magazynu, checkpointu) + 1 istniejący `ProtocolContractTests.Operations_EachRecordReturnsItsKind_AndEveryKindHasARecord` (wymaga wartości 47 i 48 w `OperationKind`, dopisanych w A3 — ten sam wzorzec co K6-30). Nowych testów w A1: 57, z czego 55 czerwonych i 2 zielone z założenia (straże regresji, mają pozostać zielone): `Changes_ItemOnly_Empty`, `ConstructPart_PropCompound_False`; test A0 (`WorldWithBlockEntitiesWithoutProps_HashUnchanged`) jest zielony od początku. Znany niestabilny `AllocationTests.SubmitPlaceOnFaceOverlappingOtherConstruct_Rejected_DoesNotAllocate` tym razem zielony. Nie uruchamiano gry ani Godota.

### Przegląd A1 (Opus 5.5, 2026-10-16) — werdykt: GO z poprawkami

Metoda: tylko odczyt plików (bez gita i bez uruchamiania testów; czerwień zgłosił wykonawca). Przeczytane w całości: `tests/Horyzont.Core.Tests/Ids/PropIdTests.cs`, `Props/PropFixture.cs`, `Props/CubeSphereGridTests.cs`, `Props/PropLayoutV1Tests.cs`, `Props/PropOpsApplierTests.cs`, `Props/OperationPropsTests.cs`, `Props/PropConservationProperties.cs`, `Items/CompoundPropTests.cs`, `Checkpoints/HarvestedPropCheckpointTests.cs`, `Serialization/OperationCodecPropTests.cs`; z `Hashing/SurvivalHashTests.cs` wiersze K6-31 (Grep); szkielety `src/Horyzont.Core/Ids/PropId.cs`, `Props/CubeSphereGrid.cs`, `Props/PropLayoutV1.cs`, `Props/HarvestedPropStore.cs`. Pozostałe szkielety (`PropRegionKey`, `IPropLayout`, `PropCandidate`, `PropKindV1`, `PropLayouts`, `PropRegrowOp`, `PropHarvestOp`, `PropChange`, `OperationProps`, `HarvestedPropRecord`, `HarvestedPropKey`) przyjmuję z opisu w tabeli sygnatur — **nieprzeczytane**. Poprawki z (4) wykonawca wprowadza jednym commitem `test(core): apply the A1 review` **przed** A2; ponowny przegląd tylko, jeśli zmieni się kontrakt.

**(1) Zgodność z planem.** Testy odpowiadają D4 (pakowanie, region w wysokich bitach i ciągłość przedziału, bity zarezerwowane, ściana ≥ 6, maksimum < 2⁶¹), D13 (47/48 samodzielnie i w złożonej, teren ostatni, cofanie bez wycieku `UndoLog`, odmowy od poprawnego odpowiednika), D15 (stała A0, zmiana skrótu od obiektu i od ticku, sekcja dla świata tylko z zebranymi), D16 (checkpoint, requeue, builder z kontrolą siatki planety, powtórka ogona) i D18 (`OperationProps` w kolejności składowych, `OperationConstructs` bez zmian). **Bajty złote 47/48 sprawdziłem ręcznie** (LEB128 bitów 58+16 → `80 80 84 80 80 80 80 80 04`; bitów 41+21+19 → `80 80 A0 81 80 40`; tick 300 → `AC 02`; mutacje `24`, `81`, `18`) — poprawne. Braki i błędy:

- **Orientacja siatki na ścianie nie jest zamrożona (błąd kontraktu).** Testy sprawdzają tylko, że kierunek leży na właściwej ścianie i że `CellOfDirection` odwraca `DirectionOfCell`; każda orientacja osi u/v przejdzie, a złoty skrót V1 jej nie widzi (układ nie dostaje kierunku). Bez tego obiekty mogą „przeskoczyć” po zmianie implementacji. Poprawka (4a) i DECYZJA w (2).
- **Applier 48 przyjąłby identyfikator, który builder odrzuca** (komórka poza siatką planety, bity zarezerwowane — operacja zbudowana w kodzie z pominięciem kodeka): wpis trafiłby do dziennika, a świat nie wstałby po restarcie (`WorldCorruptedException`). DECYZJA: warunek wstępny 48 = `Prop.IsValid` i `CellU`, `CellV` < `CubeSphereGrid.CellsPerFace(planeta.RadiusMeters)` (czyta `PlanetParams` ze stanu świata, nie treść — zgodne z ZASADY §4); 47 bez zmian (usuwa tylko istniejący wpis). Test (4b).
- **Tick > `long.MaxValue`**: kodek i applier przyjmują `Tick(ulong.MaxValue)` (test round-trip), a D16 każe loaderowi odrzucać ujemny `tick` z SQLite — ta sama niespójność co wyżej. DECYZJA: `harvested_props.tick` zapisywany i czytany bit w bit (`unchecked((long)tick)` ↔ `unchecked((ulong)value)`), bez odrzucania; test etapu C `Row_NegativeTick_WorldCorrupted` zastępuje `Tick_AboveLongMax_RoundTrips`. Jeśli w C2 okaże się, że istniejące kolumny `ulong` Persistence sprawdzają zakres, zamiast tego applier 48 odrzuca `Tick > long.MaxValue` — wykonawca zapisuje wybór tutaj.
- **Szum łat bez testu.** Test gęstości z „co najmniej 1 sztuka” przejdzie także bez łat (zwykły rozrzut). Poprawka (4c).
- Drobne: brak testu wyjątków `PropId.Create` dla złych argumentów; brak testu „zebrany i odrośnięty w jednym oknie checkpointu” (4d, 4e).

**(2) Sygnatury wykonawcy — zatwierdzone, z doprecyzowaniem:**

- `HarvestedPropKey(PlanetId, PropId)` jako klucz listy usuniętych — **zatwierdzone** (odpowiednik `BlockRef`/`PlayerId` w K6-30); porządek listy po planecie, potem `PropId`.
- **Kolejność ścian 0 +X, 1 −X, 2 +Y, 3 −Y, 4 +Z, 5 −Z — zatwierdzona.** DECYZJA (dopełnienie D4, zamrażane): osie styczne ściany cyklicznie, bez odbicia dla ścian ujemnych — X → (U = Y, V = Z), Y → (U = Z, V = X), Z → (U = X, V = Y). Mapa równokątna w `double`: `α = ((c + 0,5) / N − 0,5) · π/2`, `t = tan α`, kierunek = `normalize(znak · e_osi + t_u · e_U + t_v · e_V)`, rzutowany na `float` na końcu. `CellOfDirection`: ściana = oś o największym `|składowa|` (remis: niższy indeks osi X < Y < Z), znak ze składowej; `c = clamp(floor((atan(d_U / |d_osi|) · 4/π + 1) / 2 · N), 0, N − 1)`. `CellsPerFace`: `8 · max(1, (int)Math.Round(R · Math.PI / 2 / 64, MidpointRounding.AwayFromZero))` w `double`; wynik > `MaxCellsPerFace` → `ArgumentOutOfRangeException`.
- `PropId(0)` jest poprawnym identyfikatorem (ściana 0, komórka 0,0, slot 0) — nie używać `default` jako „brak”; wszędzie, gdzie potrzebny brak, `bool`/`TryGet`.
- `HarvestedPropStore` na posortowanej liście z wyszukiwaniem binarnym — zatwierdzone (wstawianie O(n) wystarcza do ~10⁵ wpisów); `CollectRegion` przez dwa wyszukiwania binarne granic przedziału `[region << 24, (region + 1) << 24)`.
- **Reguła gęstości — zatwierdzona i wzmocniona.** DECYZJA (dopełnienie D5): jeden rzut na slot, przedziały rodzajów na **stałych pozycjach** skumulowanych gęstości w kolejności tabeli D5; rodzaj, którego szum łat nie przechodzi progu, zostawia **lukę** (brak obiektu), przedziały się nie przesuwają — dlatego „każdy rodzaj najwyżej swoja gęstość” jest prawdą. Skały (bez łat) w ± 3σ od 0,384; pozostałe rodzaje: górna granica gęstość + 3σ **i** dolna granica ≥ 5 % `gęstość × komórki` (zamiast „≥ 1”), plus test skupienia (4c).

**(3) Rozstrzygnięcia (DECYZJA, implementacja rusza autonomicznie):**

- **DO USTALENIA-1 — tak, jak w propozycji:** kopanie i zasypywanie usuwa obiekty z kotwicą w kuli edycji + 0,5 m, bez urobku; flora nie odrasta, gdy blok terenu LOD 0 kotwicy ma wersję w `TerrainVersions` (D12, D14). Bez kolumny przyczyny i bez migracji.
- **DO USTALENIA-3 — tak:** obrażenia `1 + (poziom narzędzia − wymagany)`, reset HP po 1800 tickach bez uderzeń, najwyżej 4096 wpisów (D10).
- **DO USTALENIA-8 — tak:** nadmiar urobku z obiektu przepada, zbieranie nieblokowane (D11, jak K6-29W D11); pytanie do właściciela zostaje w opisie PR.
- **DO USTALENIA-10 — tak, zamrażamy:** komórka ≈ 8 m (`N` jak wyżej), region 8 × 8, sloty 0 (drzewa, krzew) i 1 (skała, głaz, kryształy), tabela D5 bez zmian (krzew 0,300), plus orientacja osi i reguła przedziałów z (2). Złoty skrót zapisuje A6 dopiero po wprowadzeniu (4a) i (4c).
- **DO USTALENIA-11 — tak:** nowa fikstura `world-v3-props.db` (`Manual`), `world-v3.db` i `world-v3-blocks.db` bez zmian (D23).

**(4) Poprawki testów (przed A2):**

| # | Plik | Test | Zmiana |
| --- | --- | --- | --- |
| a | `Props/CubeSphereGridTests.cs` | nowy `DirectionOfCell_GoldenValues` | stałe wektory z formuły (2), tolerancja 1e-5, co najmniej po jednej komórce na ścianę dla N = 8 i dwie dla N = 784; kontrolnie: N = 8, ściana 0, (0, 0) ≈ (0,65274; −0,53569; −0,53569); ściana 3, (u 7, v 0) ≈ (−0,53569; −0,65274; 0,53569) |
| a | `Props/CubeSphereGridTests.cs` | nowy `CellOfDirection_Ties_LowerAxisWins`, `CellsPerFace_HugeRadius_Throws` | `(1, 1, 0)` → ściana 0; `(0, 1, 1)` → ściana 2; promień dający `N` > 2²⁰ → `ArgumentOutOfRangeException` |
| b | `Props/PropOpsApplierTests.cs` | nowy `PropHarvest_InvalidOrOutsideGridId_Fails` | od poprawnego zebrania `Raw(2, 783, 783, 0)`; odmowy (`AssertFails`): `Raw(2, 784, 0, 0)`, `Raw(2, 0, 800, 1)`, bit 0 ustawiony, ściana 6 |
| c | `Props/PropLayoutV1Tests.cs` | `Density_WithinTolerancePerKind` | dla rodzajów z łatami dolna granica `0,05 × gęstość × komórki` zamiast 1 |
| c | `Props/PropLayoutV1Tests.cs` | nowy `Trees_ClusterInPatches` | liczba `Pine` na region (8 × 8 komórek, ≥ 1000 regionów): wariancja / średnia ≥ 1,5 (rozrzut dwumianowy daje < 1); skały: wariancja / średnia < 1,3 |
| c | `Props/PropLayoutV1Tests.cs` | `PropLayoutV1_GoldenDigest` (`Digest`) | dodać region (97, 97) i (50, 12) każdej ściany oraz ziarna `0` i `ulong.MaxValue` (przepełnienia sieci szumu i ziarna) |
| d | `Ids/PropIdTests.cs` | nowy `Create_OutOfRange_Throws` | ściana −1/6, komórka −1/`MaxCellsPerFace`, slot −1/4 → `ArgumentOutOfRangeException` |
| e | `Checkpoints/HarvestedPropCheckpointTests.cs` | nowy `Capture_HarvestThenRegrowInOneWindow_NotUpserted`, `Capture_RegrowThenHarvestInOneWindow_UpsertedWithNewTick` | po pełnym checkpoincie: zebrany i odrośnięty przed następnym → brak w `Upserted` (w `Removed` dopuszczalny, usunięcie nieistniejącego wiersza jest bezpieczne); odrośnięty i zebrany ponownie → `Upserted` z nowym tickiem |
| f | `docs/plans/K6-31-zbieranie.md` (krok C1) | `Row_NegativeTick_WorldCorrupted` | zastąpić `Tick_AboveLongMax_RoundTrips` (decyzja o ticku w (1)) |

### Etap A, kroki A2 do A7 (po przeglądzie A1)

**Commity.** `16e0bcf` poprawki testów z przeglądu (9 pozycji tabeli), `bad93d6` A2, `d5cf3e2` A3, `70169d5` A4, `9d25545` A5, `612d38b` A6, A7 = ten wpis. `git diff 646f130..HEAD` na `Golden/TerrainJournals`, `TerrainJournalGoldenCorpusTests.cs`, `Hashing/DeterminismTests.cs`, `src/Horyzont.Persistence/Migrations` i `game/src/Planet/Generators`: pusty.

**Wynik końcowy etapu A.** `dotnet build Horyzont.sln` 0 błędów / 0 ostrzeżeń; `dotnet format --verify-no-changes` czysto; `dotnet test --filter "Category!=Soak&Category!=Manual"`: Architecture 12, Client 644, Net 166, Persistence 210, Constructs 131, Integration 42, Core 1873 (wszystkie zielone, w tym 57 testów A1 i 9 dopisanych z przeglądu), Server 375 (1 pominięty z założenia). `AllocationTests.SubmitPlaceOnFaceOverlappingOtherConstruct_Rejected_DoesNotAllocate` w jednym z przebiegów pośrednich był czerwony (znany niestabilny), w końcowym zielony. Nie uruchamiano gry ani Godota.

**Złoty skrót.** `PropLayoutV1_GoldenDigest` = `3687299226734270200` (ziarna 0, 42, 1234, `ulong.MaxValue`; regiony (0,0), (3,5), (97,97), (50,12) każdej ściany; sloty 0-3). Zapisany po zatwierdzonej tabeli D5; skróty `StateHasher` światów K6-29W i K6-30 bez zmian (`WorldWithBlockEntitiesWithoutProps_HashUnchanged` zielony).

**Uwagi i odstępstwa wykonawcy.**

- **Poprawka (f) przeglądu była sformułowana odwrotnie** (tabela: „`Row_NegativeTick_WorldCorrupted` zastąpić `Tick_AboveLongMax_RoundTrips`", decyzja (1): „`Row_NegativeTick_WorldCorrupted` zastępuje `Tick_AboveLongMax_RoundTrips`"). Zastosowano decyzję merytoryczną (tick bit w bit, bez odrzucania): w tabeli testów etapu C test `Row_NegativeTick_WorldCorrupted` zastąpiono przez `Tick_AboveLongMax_RoundTrips`, a zdanie o `WorldCorruptedException` dla ticku w D16 zastąpiono opisem zapisu bit w bit. Do potwierdzenia przy C1.
- **Test A1 `HarvestWithItems_Compound_AddsPropAndItems`** zakładał wersję pojemnika 1 po operacji; fikstura zaczyna od wersji 1 (nowy pojemnik ma 1 po operacji, która go utworzyła), więc oczekiwanie to `wersja przed + 1` (zmiana testu A1 w commicie A3, nie testu istniejącego przed K6-31).
- **`CubeSphereGrid.TryGetCellsPerFace`** (nowa metoda, nieobecna w planie): applier 48 i builder nie mogą rzucać dla planety o promieniu ponad 5,3 · 10⁶ m, więc sprawdzają siatkę wersją bez wyjątku; `CellsPerFace` rzuca zgodnie z przeglądem.
- **Warunek 48 w applierze** (decyzja przeglądu): planeta znana, `Prop.IsValid`, `CellU`/`CellV` < `CellsPerFace(promień planety)`, obiekt niezebrany; 47: planeta znana i obiekt zebrany. Applier nie czyta treści ani terenu.
- **`CellsInSphere`**: próbkowanie dysku w płaszczyźnie stycznej (krok 0,35 komórki, zasięg = promień kuli + pół komórki), dla kuli obejmującej środek planety pełny przegląd komórek; wynik posortowany i bez duplikatów, bez alokacji poza listą wynikową (poza `Sort` na przedziale). Koszt ~400 wywołań `CellOfDirection` dla kuli 20 m (kopanie, nie gorąca ścieżka tyknięcia).
- **Skrót D15**: podsekcja `0x48` zapisuje planetę jako `UInt64` (jak reszta skrótu), nie varuint; opis w D15 mówi o varuint, ale skrót nie jest formatem wymiany.
- **`CodecGen`** (`tests/Horyzont.Testing`) dostał generatory `PropId`, `PropHarvestOp`, `PropRegrowOp` w A4 (dotychczasowy `GeneratorSanityTests.Operations_CoverAllEightKinds` wymaga wszystkich rodzajów); w A3 ten test był czerwony do A4 (wartości enuma 47/48 dopisane w A3, kodeki w A4).
- Pierwszy przebieg filtra `FullyQualifiedName~Prop` w A3 trwał kilka minut (dopasowuje wiele nietrywialnych klas); testy etapu uruchamiano po nazwach klas.

**Sprawdzenie mutacją (A7).** Każda mutacja zastosowana na kopii, testy uruchomione, plik przywrócony: (1) warunek „obiekt niezebrany" w 48 usunięty → `PropHarvest_AlreadyHarvested_Fails`, `TwoHarvestsSameProp_SecondComponentFails_RolledBack`, `FailedCompound_ThenStandalone_NoStaleUndo` i oba testy właściwości czerwone; (2) brak sprawdzenia `CellU` → `PropHarvest_InvalidOrOutsideGridId_Fails` czerwony; (3) brak warunku „zebrany" w 47 → `PropRegrow_NotHarvested_Fails`, test właściwości czerwone; (4) brak odtworzenia wpisu przy cofaniu 47 → `FailedCompound_ThenStandalone_NoStaleUndo`, test właściwości; (5) brak cofania dodania wpisu przy cofaniu 48 → pięć testów czerwonych; (6) skrót bez ticku → `HarvestProp_ChangesHash`; (7) usunięcie bez oznaczenia w trackerze → `Capture_Regrown_ListedAsRemoved`; (8) lustrzana oś U siatki → `DirectionOfCell_GoldenValues` i trzy testy siatki. **Słabe punkty:** (a) zmiana wartości w tabeli układu (gęstość, próg szumu, pasmo, skala) wykrywa tylko `PropLayoutV1_GoldenDigest` i przedziały testów statystycznych (celowo: skrót jest zamrożeniem); (b) test szumu łat mierzy tylko `Pine` i `Rock`, pozostałe progi chroni skrót; (c) `Capture_HarvestThenRegrowInOneWindow_NotUpserted` dopuszcza wpis w `Removed`, więc nie wykryje zbędnych usunięć; (d) `CellsInSphere` ma dowód tylko na planecie 40 m (brute force) i własności na 4000 m, bez porównania z siatką pełną dla dużej planety; (e) rollback złożonej z 64 składowymi 47/48 sprawdza test zebrania/odrastania, ale nie błąd w środku takiej złożonej.

### Etap B, krok B1 (do przeglądu Opus 5.5 przed B2)

**Commit.** `test(core): add harvesting intent tests` (nazwa zamiast `test(core): add prop hit and tool tier tests` z kroku B1, na polecenie). Stan bazowy ee46dc0; `git diff 646f130..HEAD` na `Golden/TerrainJournals`, `TerrainJournalGoldenCorpusTests.cs`, `DeterminismTests.cs`, `Migrations` i `game/src/Planet/Generators` pusty.

**Sygnatury szkieletów B1 (puste, `// K6-31 B2…B4`, do przeglądu).**

| Typ | Sygnatura / uwaga |
| --- | --- |
| `Intents/PropHitIntent` | `sealed record PropHitIntent(PlanetId Planet, PropId Prop, ushort ToolSlot) : Intent` (kod 27 dopisuje B2) |
| `Protocol/Messages/PropStates`, `PropRegionState`, `HarvestedPropEntry` | `PropStates(PlanetId Planet, IReadOnlyList<PropRegionState> Regions) : Message` z `Type => (MessageType)45`; `PropRegionState(PropRegionKey Region, IReadOnlyList<HarvestedPropEntry> Props)`; `readonly record struct HarvestedPropEntry(PropId Prop, Tick Tick)`. Wartości `RejectReason` 53 i `MessageType` 45 **nie** są w enumach (testy nazywają je liczbą: `PropProtocol.ToolTierTooLow`, `PropProtocol.PropStatesType`) |
| `ProtocolConstants`, `GameRules` | `MaxPropRegionsPerMessage = 64`, `MaxPropEntriesPerMessage = 4096`; właściwości D22 z wartościami z planu (`PropHitsPerSecond` … `PropSyncLeaveMeters`); `ProtocolVersion` zostaje 15 |
| `Content/PropDef`, `PropYield` | `PropDef(string Key, ushort HitPoints, byte ToolTier, IReadOnlyList<PropYield> Yield, uint RegrowSeconds)`, `PropYield(string ItemKey, ushort Count)`; `ContentLoader.PropsFile = "props/props.json"`, przeciążenie `Parse(items, yields, start, propsJson)` (szkielet ignoruje `propsJson`), `SurvivalContent.Props` (puste) i `TryGetProp(PropKindV1, out PropDef)` (zawsze `false`) |
| `Props/PropInstance`, `PropField` | `PropInstance(PropId Id, PropKindV1 Kind, Vector3 Anchor, Vector3 Up, float Scale)` + **nowe, spoza planu:** `TrunkHeightMeters` i `BodyRadiusMeters` (szkielet 0); `PropField(WorldState, ITerrainService)`: `TryGetProp(PlanetId, PropId, out PropInstance)` (szkielet `false`), `CollectInSphere(PlanetId, Vector3 center, float radius, List<PropInstance>)` (szkielet nic nie dopisuje) |
| `Intents/IntentResult` | `static IntentResult AcceptedWithoutOperation()` = `new(RejectReason.None, null)` (jedyna „logika": fabryka) |
| `tests/Horyzont.Testing` | `FakeTerrainService.GeneratedSurfaceRadius` (`float?`), `GeneratedSurfaceCalls`, `TrySampleGeneratedSurface(...)` jako metoda **klasy**, nie `ITerrainService` (interfejs, `VoxelTerrainService` i `NoTerrain` w grze dostają ją w B4 z zaślepką); `SurvivalContentFactory.CreateTestWithProps()` i stałe; `WorldFactory.CreateWithProps/CreateForContent/PropItem`; `PropFixtures.FindProp/PointBeside` |

**Bajty złote w testach.** Intencja 27 = `1B 01 <planeta> <PropId> <slot>`, np. `PropHitIntent(1, face 0/u 9/v 2/slot 0, 3)` = `1B 01 01 80 80 A0 81 80 40 03`, slot 255 = `FF 01`; slot 256 przy odczycie to `ProtocolViolationException`, przy zapisie `ArgumentException`; bity zarezerwowane `PropId` jak w operacjach 47/48. Komunikat 45 = `2D 01 <planeta> <liczba regionów 1…64> {<PropRegionKey> <liczba wpisów> {<PropId> <tick>}…}…`: np. `2D 01 01 01 80 80 08 01 80 80 A0 81 80 40 AC 02` (region (1, 0) ściany 0, jeden wpis, tick 300); region ściany 1 = klucz `80 80 80 80 40`. Odmowa 53 w `IntentReply`: `0C 01 03 35 00`; 52 i 54+ nadal nieznane (54 i 57 z K6-30). Granice: 0 regionów, 65 regionów, 4097 wpisów, regiony nieuporządkowane lub powtórzone, wpis spoza regionu, wpisy nieposortowane → odczyt `ProtocolViolationException`, zapis `ArgumentException`; region ma najwyżej 256 identyfikatorów, więc 4096 wpisów to co najmniej 16 regionów (test: 16 pełnych przechodzi, 17. region z jednym wpisem nie).

**Odstępstwa od tabeli testów i dopiski.**

- **Enumy złote:** nie dopisano wartości do `RejectReason`, `MessageType` ani `ProtocolVersion`. `ProtocolContractTests` dostało `ProtocolVersion` 16 (dwa testy, czerwone do B2) i dwie stałe limitów (`Constants_GoldenValues`); `Enums_GoldenValues` zostaje do B2 (liczy wszystkie człony enuma), jego odpowiednikiem do tego czasu jest `PropProtocolContractTests.Enums_PropValues_GoldenNumbersAndNames` (czerwony).
- **Istniejące testy zmienione w B1 (zielone przed i po B2):** `IntentCodecBlockTests.Read_VersionTrailingBytesAndNeighbourCodes_Throws` (kod `1B` wypada z listy nieprzydzielonych), `BlockEntityStatesRouterTests.MessageRouter_BlockEntityStates_TypeByteIsKnownAndReliable` (sąsiedzi 44, 47, 48; 45 jest `PropStates`). `IntentValidationTests` dostało `Submit_PropScenario_ReturnsToolTierTooLow` (kod 53 poza zakresem `AllReasons`, jak 54 i 57 w K6-30). `MergedProtocolTests` (lista zarezerwowanych numerów komunikatów) zostaje do B2 i jest czerwony od dodania rekordu `PropStates`.
- **Dodane poza tabelą:** `PropContentTests` (`Parse_TestContent_ReadsEveryField`, `Parse_UnknownKey_Throws`, `Parse_DuplicateKey_Throws`, `Parse_HitPointsOver1000_Throws`, `Parse_CountZero_Throws`, `Parse_EmptyYield_Throws`), `PropFieldTests` (`TryGetProp_HeightOutsideEveryBand_False`, `TryGetProp_UnknownPlanet_False`), `DigRemovesPropsTests.Dig_WithYield_ItemsThenPropsThenEdit` (kolejność składowych: przedmioty, obiekty, edycja), `DigToolTierTests.Dig_SurvivalContentWithoutProps_NoToolInHotbar_StillDigs` (patrz DO USTALENIA-12), `MessageCodecPropStatesTests` (zero regionów, wersja, ogon, `IntentReply` 53), `PropProtocolContractTests.GameRules_PropDefaults_FollowD22`, `Net.Tests/PropStatesRouterTests` (3 testy, zamiast dopisku do `MessageRouterTests`).
- **Poza zakresem testów B1:** kopanie z `MeasureSphere` = `false` (plan: zwykła edycja) nie ma testu usuwania obiektów — testy kopania mierzą kulę (`AirOf` albo `SolidOf`); do rozstrzygnięcia w B5, czy obiekty znikają także wtedy. Nie pokryte: wypieranie najstarszego wpisu księgi (`MaxDamagedProps`) i pamięci kotwic (`PropAnchorCacheSize`), alokacje ścieżki przyjęcia, `VoxelTerrainService.TrySampleGeneratedSurface` (C5), `harvested_props`, odrastanie, replikacja (etap C).

**Rozstrzygnięcia DO USTALENIA w testach (do potwierdzenia w przeglądzie).**

- **DO USTALENIA-2 (poziom przy kopaniu):** największy `ToolTier` przedmiotów z flagą `Tool` w własnym `Hotbar` (pasek, nie plecak: test z narzędziem poziomu 2 w plecaku nadal odmawia); materiał bez wpisu wydajności (`ash`), kula niemierzalna i zasypywanie nie sprawdzają poziomu.
- **DO USTALENIA-3:** uderzenie nieostatnie to `IntentResult.AcceptedWithoutOperation()` (bez operacji, bez wpisu w dzienniku, `LastOpId` bez zmiany, zużywa token kubełka `Props`: 4 uderzenia naraz, piąte `RateLimited`); obrażenia `1 + (poziom − wymagany)` (narzędzie 2 na sośnie poziomu 1 z 3 HP: dwa uderzenia); reset po 1800 tickach przerwy; HP wspólne dla graczy (dwóch graczy na jednym drzewie, drugi po zebraniu dostaje `NoHit`).
- **DO USTALENIA-4 (dane):** format `props/props.json`: `{"formatVersion":1,"props":[{"key":"core:pine","hitPoints":3,"toolTier":1,"regrowSeconds":10,"yield":[{"item":"test:wood","count":3}]}]}`; klucze `core:pine`, `core:pine_tall`, `core:broadleaf`, `core:broadleaf_autumn`, `core:bush`, `core:rock`, `core:boulder`, `core:crystals` w kolejności `PropKindV1`; błąd to `InvalidDataException` z `props/props.json` i ścieżką `$.props[i]…` (brakujący rodzaj, nieznany lub powtórzony klucz: ścieżka `$.props`). Test na treści z repo (`Parse_RepoContent_AllV1KindsHaveDefs`) wymaga pliku `game/content/props/props.json` i przedmiotów `core:wood`, `core:fiber`, `core:crystal` (zasób, `bulk`, stos 500); wartości HP i urobku z repo nie są przypięte, tylko zgodne z plikiem (`YieldDataTests`). Zestaw startowy treści testowej zawiera drewno i włókno w plecaku wyłącznie po to, by treść przechodziła `HasSource` przed B3 (testy go nie stosują).
- **DO USTALENIA-8:** pełny ekwipunek → sama `PropHarvestOp` (nie złożona), obiekt znika, przedmioty przepadają, uderzenie przyjęte.
- **DO USTALENIA-9 (czym się uderza, rdzeń):** każdy przedmiot z flagą `Tool` w slocie paska daje swój poziom (nie tylko manipulator); pusty slot i przedmiot bez `Tool` (kamienie, części) to ręka; wybór „tylko ręka i manipulator" jest po stronie klienta (D19).
- **DO USTALENIA-12 (nowe, sprzeczność D9/D21 z „testy K6-29W bez zmian"):** D21 mówi, że poziom przy kopaniu sprawdza się „z treścią Przetrwania", a istniejące `MiningYieldTests` kopią z treścią Przetrwania **bez żadnego narzędzia na pasku** i muszą zostać zielone. Testy B1 przyjmują, że sprawdzenie poziomu włącza **treść obiektów** (`SurvivalContent.Props` niepuste), nie sama treść Przetrwania; strażnik `Dig_SurvivalContentWithoutProps_NoToolInHotbar_StillDigs` jest zielony od początku. Alternatywa (sprawdzać z każdą treścią Przetrwania) wymaga zmiany `MiningYieldTests` i testów zasypywania z K6-29W.
- **DO USTALENIA-13 (nowe, wymiary brył zastępczych):** plan D19/D24 mówi „wysokość i promień z tabeli rodzajów V1 × skala" i nie ma tej tabeli. Szkielet `PropInstance` dostaje `TrunkHeightMeters` i `BodyRadiusMeters`; test `Hit_TrunkTopWithinReach_Accepted` wymaga, by wysokość pnia wysokiej sosny mieściła się w 4–40 m, a oko 90 % wysokości wyżej i 2 m obok pnia było dalej niż 8 m + promień od kotwicy, ale w zasięgu pnia. Do ustalenia w B4: tabela (propozycja: sosna 9 m / 0,35, wysoka sosna 14 m / 0,45, liściaste 8 m / 0,4, krzew 1,2 m / 0,5, skała i głaz kula 0,5·skala / 1,2·skala, kryształy 1,0 m / 0,6) i zamrożenie jej testem.
- **Geometria testów:** brak linii wzroku w testach to gracz pod powierzchnią (oko w terenie: `FakeTerrainService.Raycast` zwraca trafienie w początku promienia), bo sfera bez wzgórz nie zasłania pnia; zasięg liczony do odcinka pnia (nie do kotwicy); w testach kopania kula 1 m + margines 0,5 m, a oczekiwane obiekty wylicza niezależnie `PropField` z kuli zastosowanej edycji (granica ± 0,02 m).

**Wynik.** `dotnet build Horyzont.sln` 0 błędów / 0 ostrzeżeń; `dotnet format --verify-no-changes` czysto. `dotnet test --filter "Category!=Soak&Category!=Manual"`: Architecture 12, Client 644, Constructs 131, Integration 42, Persistence 210 (zielone); Core 1873 zielonych / 81 czerwonych = 78 nowych (brak zachowania: `PropField`, treść, kodeki 27 i 45, intencja 27, poziomy narzędzi, obiekty w kuli, enumy) + 3 istniejące (`ProtocolContractTests.ProtocolVersion_MatchesDocumentedMergeValue` i `Constants_GoldenValues` — wersja 16; `Messages_EachRecordReturnsItsType_AndEveryTypeHasARecord` — nowy rekord `PropStates` bez członu enuma); Net 165 zielonych / 4 czerwone = 3 nowe (`PropStatesRouterTests`) + 1 istniejący (`MergedProtocolTests.MessageTypes_AllConcreteMessages_HaveUniqueIdsAndOnlyDocumentedGaps`, ten sam powód); Server 373 zielone + 1 pominięty, 2 czerwone w jednym przebiegu (`PlanetActivityPolicyTests.PlanetActivityPolicy_Update_AllocatesZeroBytes`, `ServerStatusIntegrationTests.Status_WriterFaultThenCommit_TwoClientsGetEachTransitionOnce`), zielone przy ponownym uruchomieniu (znane niestabilne przy obciążeniu). Zielone z założenia (strażnicy, 3 nowe w Core): `GameRules_PropDefaults_FollowD22`, `YieldDataTests.Dig_EveryRepoMaterial_YieldMatchesItemsPerCubicMeter`, `DigToolTierTests.Dig_MultitoolTier1_Yields`. Przyczyny czerwieni: testy trafień i kopania pod obiektem kończą na `FindProp` (szkielet `PropField` nic nie zwraca, B4) albo na pierwszym przyjęciu `InvalidParameters` (intencja 27 nieobsługiwana, B5), testy poziomów na oczekiwanym `ToolTierTooLow` przy zwróconym `None` (B5). Kontrola logiki testów: tymczasowy prototyp `PropField` (nieskomitowany, usunięty) potwierdził, że `FindProp` znajduje wszystkie 8 rodzajów w 512 m od bieguna i że testy trafień kończą dokładnie na `InvalidParameters`. Nie uruchamiano gry ani Godota; `.uid` i `game/locale/ui.csv.import` nietknięte.

### Przegląd B1 (Opus 5.5, 2026-10-16) — werdykt: GO z poprawkami

Metoda: tylko odczyt plików (bez gita i bez uruchamiania testów). Przeczytane w całości: `tests/Horyzont.Core.Tests/Simulation/PropHitIntentTests.cs`, `Simulation/DigToolTierTests.cs`, `Simulation/DigRemovesPropsTests.cs`, `Simulation/PropHitAllocationTests.cs`, `Props/PropWorldFixture.cs`, `Props/PropFieldTests.cs`; częściowo: `Serialization/MessageCodecPropStatesTests.cs` (l. 110–190), `tests/Horyzont.Testing/SurvivalContentFactory.cs` (l. 1–80), `WorldFactory.CreateWithProps/CreateForContent` (Grep); nazwy testów (Grep) z `Content/PropContentTests.cs`, `Content/YieldDataTests.cs`, `Serialization/IntentCodecPropTests.cs`, `Protocol/PropProtocolContractTests.cs`, `Net.Tests/PropStatesRouterTests.cs`; kolejność `UnknownPlanet`/`WrongPlanet` w `src/Horyzont.Core/Simulation/IntentValidator.cs` (Grep). **Nieprzeczytane:** `MiningYieldTests.cs`, treść `YieldDataTests`, `PropContentTests`, `IntentCodecPropTests`, router, szkielety `src/` z B1 (przyjmuję z tabeli sygnatur). Bajty złote przeliczyłem ręcznie z opisu i testu: intencja 27 (`1B 01 01 80 80 A0 81 80 40 03`), komunikat 45 (klucz regionu (1, 0) ściany 0 = bit 17 → `80 80 08`; ściana 1 = bit 34 → `80 80 80 80 40`; 4097 = `81 20`), odmowa 53 w `IntentReply` (`0C 01 03 35 00`) — poprawne.

**Uwaga do przeglądu A1:** poprawka (f) była sformułowana odwrotnie w zdaniu decyzji; wykonawca zastosował intencję (tick bit w bit, test `Tick_AboveLongMax_RoundTrips`) — **potwierdzam**.

**(1) Zgodność z planem.** Testy odzwierciedlają D8–D12, D21, D22, D24: przyjęcie bez operacji (bez wpisu w dzienniku, `LastOpId` bez zmiany), obrażenia `1 + różnica`, reset po 1800 tickach, wspólne HP, `NoHit` w wyścigu, zasięg do odcinka pnia, linia wzroku, ręka = pusty slot albo przedmiot bez `Tool`, pełny ekwipunek = sama `PropHarvestOp`, kubełek `Props` 4/s, poziom przy kopaniu z paska (nie z plecaka), materiał bez wydajności i zasypywanie bez poziomu, obiekty w kuli + 0,5 m w porządku `PropId` i z tickiem kopania, kolejność składowych przedmioty → obiekty → edycja. Kontrakt 45 (1…64 regionów, ≤ 4096 wpisów, regiony i wpisy rosnące i unikalne, wpis w swoim regionie, odmowa przed alokacją) pokryty. Błędy i braki:

- **`Hit_OtherPlanet_WrongPlanet` i `PropHitAllocationTests` używają planety 2, której w świecie nie ma.** Walidator ma ustaloną kolejność: planeta nieznana → `UnknownPlanet` (11), znana, ale nie gracza → `WrongPlanet` (20) (`IntentValidator.cs:162-169`, `:330-337`). DECYZJA: `PropHitIntent` tak samo; D24 uzupełnione o `UnknownPlanet` przed `WrongPlanet`. Poprawka (4a).
- **Kopanie bez pomiaru a obiekty** (luka wykonawcy). DECYZJA: usuwanie obiektów z kuli (D12) **nie zależy** od `MeasureSphere` — `PropField` korzysta z powierzchni generatora, a zwykła edycja i tak zmienia teren pod obiektem. Kula niemierzalna: bez poziomu i bez urobku, ale z `PropHarvestOp` (gdy jest treść obiektów). Test (4b).
- **Brak testów księgi obrażeń:** wypieranie najstarszego wpisu (`MaxDamagedProps`), brak zużycia przy odmowie walidatora, pełne HP po odroście (wpis usunięty po zebraniu). Testy (4c).
- **Komunikat 45:** brak odmowy dla klucza regionu z niezerowymi bitami ponad 36 albo ścianą ≥ 6 i dla liczby wpisów regionu > 256 (region ma najwyżej 256 identyfikatorów — sprawdzać przed alokacją listy regionu). Testy (4d).
- **Treść:** `regrowSeconds` bez górnej granicy. DECYZJA: 0…2 592 000 (30 dni); test (4e).

**(2) Sygnatury, bajty i format — zatwierdzone:**

- `PropHitIntent(PlanetId, PropId, ushort ToolSlot)`, kod 27, `ToolSlot` varuint ≤ 255 (256 → odczyt `ProtocolViolationException`, zapis `ArgumentException`); `PropStates`/`PropRegionState`/`HarvestedPropEntry` i ich bajty; `IntentResult.AcceptedWithoutOperation()`; stałe D22 w `GameRules` i `ProtocolConstants`; `ProtocolVersion` 16 w B2 — **zatwierdzone**.
- `PropInstance` z `TrunkHeightMeters` i `BodyRadiusMeters` (poza planem) — **zatwierdzone**, wartości z tabeli (3).
- `FakeTerrainService.TrySampleGeneratedSurface` jako metoda klasy do B4 — zatwierdzone; w B4 trafia do `ITerrainService` z zaślepkami w `VoxelTerrainService` i `NoTerrain` (`game/tests/Constructs/ConstructTestWorld.cs`).
- **Format `props/props.json` — zatwierdzony:** `{"formatVersion":1,"props":[{"key","hitPoints","toolTier","regrowSeconds","yield":[{"item","count"}]}]}`, klucze `core:pine` … `core:crystals` w kolejności `PropKindV1`, każdy rodzaj dokładnie raz, błędy `InvalidDataException` ze ścieżką JSON; plus granica `regrowSeconds` z (1). Dane repo wg tabeli D8 (B3).

**(3) Rozstrzygnięcia (DECYZJA):**

- **DO USTALENIA-12 — wariant wykonawcy:** reguły K6-31 przy kopaniu (poziom narzędzia i usuwanie obiektów) włącza **niepusta treść obiektów** (`SurvivalContent.Props`), nie sama treść Przetrwania. `MiningYieldTests` i testy zasypywania K6-29W zostają bez zmian; gra zawsze przekazuje pełną treść, więc zachowanie gracza jest takie jak w D9. D21 czytać odtąd: „bez treści obiektów kopanie i zasypywanie działają jak w K6-29W (bez poziomu, bez obiektów)”.
- **DO USTALENIA-13 — zatwierdzone z ujednoliceniem:** każda bryła zastępcza to kapsuła wzdłuż „w górę” od kotwicy, wymiary × `Scale`: sosna 9 m / 0,35 m; wysoka sosna 14 m / 0,45 m; liściaste (oba) 8 m / 0,4 m; krzew 1,2 m / 0,5 m; skała 0,6 m / 0,5 m; głaz 1,4 m / 1,2 m; kryształy 1,0 m / 0,6 m. Tabela `PropShapesV1` w `src/Horyzont.Core/Props/` (wspólna dla walidatora i `PropPicker`), przypięta testem wartości złotych; nie wchodzi do skrótu układu (nie zmienia stanu), ale zmiana wymaga przeglądu, bo serwer i klient muszą liczyć tak samo.
- **DO USTALENIA-2 — tak:** największy `ToolTier` przedmiotów `Tool` we własnym pasku; plecak się nie liczy; materiał bez wpisu, kula niemierzalna i zasypywanie bez sprawdzenia.
- **DO USTALENIA-4 — tak:** format jak w (2), dane repo z tabeli D8; treść testowa może mieć własne wartości.
- **DO USTALENIA-9 — tak:** po stronie serwera każdy przedmiot `Tool` w slocie daje swój poziom; ograniczenie do ręki i manipulatora jest tylko w kliencie (D19).
- **DO USTALENIA-3, 8** — bez zmian względem A1 (potwierdzone testami). **DO USTALENIA-5** — zostaje propozycja do pomiaru w D3.

**(4) Poprawki testów (przed B2, commit `test(core): apply the B1 review`):**

| # | Plik | Test | Zmiana |
| --- | --- | --- | --- |
| a | `Simulation/PropHitIntentTests.cs` | `Hit_OtherPlanet_WrongPlanet` | świat z dwiema planetami (druga znana, gracz na pierwszej) → `WrongPlanet`; nowy `Hit_UnknownPlanet_UnknownPlanet` (planeta 99 → `UnknownPlanet`) |
| a | `Simulation/PropHitAllocationTests.cs` | `RejectedHit_DoesNotAllocate` | `new PlanetId(2)` → oczekiwane `UnknownPlanet` (albo świat z dwiema planetami i `WrongPlanet`); dodać odmowę `ToolTierTooLow` (ręka na sośnie z 3 m) |
| b | `Simulation/DigRemovesPropsTests.cs` | nowy `Dig_NotMeasurable_PropsStillRemoved` | `MeasureResult = null`, kopanie przy sośnie bez narzędzia: `CompoundOp[PropHarvestOp…, TerrainEditOp]`, bez przedmiotów i bez `ToolTierTooLow` |
| c | `Simulation/PropHitIntentTests.cs` | nowe `Hit_Refused_DoesNotWear`, `Hit_AfterRegrow_FullHitPoints` | `OutOfReach` między uderzeniami nie zmniejsza HP (trzecie przyjęte uderzenie dalej kończy drzewo dopiero po trzech przyjętych); po zebraniu i `PropRegrowOp` drzewo znów potrzebuje 3 uderzeń |
| c | nowy `Simulation/PropDamageLedgerTests.cs` (internal) | `Over4096Props_OldestResets`, `ResetAfter1800Ticks`, `RemoveOnHarvest` | jednostkowo na `PropDamageLedger` (wzór `YieldLedger`) |
| d | `Serialization/MessageCodecPropStatesTests.cs` | nowe `InvalidRegionKey_Throws`, `Read_RegionOver256Entries_ThrowsBeforeAllocation` | klucz z bitem 37 albo ścianą 6 → odczyt `ProtocolViolationException`, zapis `ArgumentException`; liczba wpisów regionu 257 → odmowa na liczbie |
| e | `Content/PropContentTests.cs` | nowy `Parse_RegrowSecondsOverMax_Throws` | `regrowSeconds` 2 592 001 → `InvalidDataException` ze ścieżką |
| f | nowy `Props/PropShapesV1Tests.cs` | `Shapes_GoldenValues` | wartości z DO USTALENIA-13; `Hit_TrunkTopWithinReach_Accepted` zostaje (wysoka sosna 14 m × skala) |

**Uzupełnienia kroków B2–B6 (przeniesione z B1):**

- **B2:** dopisać `RejectReason.ToolTierTooLow = 53`, `MessageType.PropStates = 45` i zastąpić liczby w `PropProtocol`; zaktualizować `Enums_GoldenValues` i `MergedProtocolTests` (lista numerów); `ProtocolVersion` = 16; router 45 jak `BlockEntityStates`.
- **B3:** granica `regrowSeconds`; `game/content/props/props.json` (tabela D8) i przedmioty `core:wood`, `core:fiber`, `core:crystal` w `items.json`; nazwy w `items.csv`.
- **B4:** `ITerrainService.TrySampleGeneratedSurface` w interfejsie + zaślepki gry; `PropShapesV1` (DO USTALENIA-13) i wypełnienie `TrunkHeightMeters`/`BodyRadiusMeters` w `PropField`; wypieranie pamięci kotwic `PropAnchorCacheSize` (test w `PropFieldTests`: 4097 różnych obiektów, pierwszy znów próbkuje).
- **B5:** kolejność walidacji D24 z `UnknownPlanet`; księga obrażeń zmieniana tylko po przyjęciu (nieostatnie) albo po `TryApply` (ostatnie); kopanie: treść obiektów włącza poziom i obiekty (DO USTALENIA-12), obiekty także przy kuli niemierzalnej (1); grep `\.Applied!` i `result.Applied` w `src/` (przyjęcie bez operacji).
- **B6:** przegląd Core A+B przez Opus jak w planie; w nim także `PropRules` (ścieżka bez operacji w `ServerSimulation.Submit`).

### Etap B, poprawki B1 oraz kroki B2 do B5 (po przeglądzie B1)

**Commity.** `8d68846` poprawki testów z przeglądu B1 (8 wierszy tabeli (4): a×2, b, c×2, d, e, f, razem z tekstem przeglądu w planie), `c57b4fb` B2, `58fb899` B3, `9d911ed` B4, `43a299f` B5; B6 = commit z tym wpisem. `git diff 646f130..HEAD` na `Golden/TerrainJournals`, `TerrainJournalGoldenCorpusTests.cs`, `Hashing/DeterminismTests.cs`, `src/Horyzont.Persistence/Migrations` i `game/src/Planet/Generators`: pusty.

**Wynik końcowy etapu B.** `dotnet build Horyzont.sln` 0 błędów / 0 ostrzeżeń; `dotnet format --verify-no-changes` czysto; `dotnet test --filter "Category!=Soak&Category!=Manual"`: Architecture 12, Client 644, Net 170, Persistence 210, Integration 42, Constructs 131, Core 1976, Server 375 (1 pominięty z założenia), wszystko zielone. Znane niestabilne w przebiegach pośrednich: `AllocationTests.SubmitPlaceOnFaceOverlappingOtherConstruct_Rejected_DoesNotAllocate` (pojedynczo zielony), raz `PlanetActivityPolicyTests.PlanetActivityPolicy_Update_AllocatesZeroBytes` (po ponownym uruchomieniu zielony). Gra i Godot nie były uruchamiane; `dotnet build game/Horyzont.Game.csproj` przeszedł (0/0) po B3 i B4 (bez `dotnet test game`, to etap C).

**Odstępstwa od przeglądu B1 (Opus 5.5) i błędy w testach z przeglądu.**

- **Poprawka (4a) `PropHitAllocationTests`:** zrobiona jako wariant z `UnknownPlanet` (planeta 2 nie istnieje) plus odmowa `ToolTierTooLow` drugiego gracza przy sośnie (3 m, ręka); `Hit_OtherPlanet_WrongPlanet` dostaje drugą planetę w `State.Planets`.
- **Test z przeglądu `IntentReply_ToolTierTooLow_RoundTrips` (B1) miał błąd:** oczekiwał, że kod 54 (`0x36`) jest nieznany, a `NotOwner = 54` jest zdefiniowany od K6-30. Zmiana: nieznane 52 (`0x34`) i 55 (`0x37`), 54 czyta się jako `NotOwner`.
- **Test B1 `Hit_SlotOutOfHotbar_InvalidParameters` miał niespójny bliźniak:** slot 9 jest pusty (ręka), a sosna wymaga poziomu 1, więc bliźniak „przyjęty bez operacji" nie mógł przejść. Zmiana: bliźniak ze slotem 0, slot 9 dowodzi się odmową `ToolTierTooLow` (przeszedł walidację slotu), 10 i więcej to `InvalidParameters`.
- **Test B1 `Dig_WithYield_ItemsThenPropsThenEdit` przekazywał do `AssertHarvestsOfEdit` także składowe przedmiotów;** teraz tylko wycinek od pierwszego `PropHarvestOp`.
- **`MessageRouterTests.TryGetType_UnassignedValues_ReturnsFalse`** dostaje 45 na liście znanych; `MergedProtocolTests.Handshake_MergedProtocolVersion_*` przyjmuje teraz tylko 16 (nazwa `AcceptsOnlySixteen`, dane 15, 16, 17), do listy zarezerwowanych numerów dopisano scenariusz 11 (`PropStates`).
- **`PropHitAllocationTests`:** pętla pomiaru powtarza te same odmowy bez czekania, bo odmowa nie zużywa tokenu kubełka (zużywa go dopiero przyjęcie); każde wywołanie jest sprawdzane na swoją przyczynę (wyjątek przy innej).

**Odstępstwa od planu (kod).**

- **B2:** `PropRegionKey.IsValid` (nowe, 37 bitów i ściana < 6) służy kodekowi 45; odczyt sprawdza liczbę regionów (1…64), klucz, rosnącą kolejność, liczbę wpisów regionu (≤ 256, przed alokacją listy), sumę (≤ 4096), przynależność wpisu do regionu i rosnącą kolejność wpisów. `ToolSlot` > 255: odczyt `ProtocolViolationException`, zapis `ArgumentException`. `CodecGen`/`MaxMessages` dostały generatory 27 i 45 (X6). Tymczasowe rzutowania liczbowe i `PropProtocol.cs` usunięte.
- **B3:** `ContentLoader.LoadDirectory` czyta `props/props.json` tylko gdy plik istnieje; `formatVersion` jest wymagany i musi być 1; `toolTier`, `regrowSeconds`, `hitPoints` i `yield` są wymagane. **`game/src/Content/SurvivalContentLoader.cs` czyta już teraz czwarty plik** (plan: C5), bo bez tego `core:wood` nie miałoby źródła i treść gry by się nie wczytała. Skutek: treść gry ma obiekty, więc reguły poziomu narzędzia przy kopaniu i obiektów w kuli włączą się w grze od razu po B5, ale `VoxelTerrainService.TrySampleGeneratedSurface` zwraca `false` do C5, więc żaden obiekt nie istnieje.
- **B4:** `PropField(state, terrain, GameRules? rules = null)` (trzeci parametr spoza planu: rozmiar pamięci kotwic z `PropAnchorCacheSize`); `CubeSphereGrid.DirectionOfPoint` (nowa metoda publiczna dla przesunięć w komórce), `DirectionOfCell` jej używa; `TryGetProp` pamięta tylko istniejące obiekty (brak powierzchni i odrzucenie pasmem wysokości nie są pamiętane); `CollectInSphere` bierze komórki z marginesem 1,5 szerokości komórki wokół kuli i filtruje po odległości kotwicy. `PropInstance.TrunkHeightMeters` i `BodyRadiusMeters` liczą się z `PropShapesV1` × `Scale` (bez nowych pól rekordu).
- **B5:** `ToolTiers` (nowy, wewnętrzny) wspólny dla `PropRules` i `SurvivalRules`; kubełek `Props` doładowuje `AdvanceTick`; uderzenie nieostatnie zużywa token po przyjęciu; gracz bez paska to ręka (slot niesprawdzany); przedmiot urobku nieznany światu (brak w rejestrze) albo treści jest pomijany, a zebranie i tak następuje — **ryzyko dla istniejących światów bez `core:wood`/`core:fiber`/`core:crystal` w rejestrze przedmiotów (do rozstrzygnięcia w C2: czy rejestr świata rośnie o nowe klucze treści)**; wpisy księgi obrażeń po 1800 ticków zostają do wyparcia albo nadpisania (odczyt traktuje je jako pełne HP).

**Słabe punkty testów (do przeglądu).**

- `Over4096Entries_ThrowsBeforeAllocation` i `Read_RegionOver256Entries_ThrowsBeforeAllocation` używają ramek bez bajtów wpisów, więc nie odróżniają odmowy na liczbie od końca danych; „przed alokacją" wynika z kolejności w kodzie (liczba sprawdzana przed `new HarvestedPropEntry[]`).
- Linia wzroku do obiektu sprawdza tylko teren (D24), budowle nie zasłaniają drzewa; test `Hit_BehindHill_NoLineOfSight` używa gracza pod powierzchnią, nie prawdziwego wzgórza.
- `PropHitAllocationTests` obejmuje ścieżkę odmowy po pamięci kotwic (zimny start zlicza się do rozgrzewki); przyjęcie uderzenia (księga obrażeń, `LinkedList`) alokuje i nie jest testowane pod tym kątem.
- Mutacje sprawdzone na B5 (każda przywrócona): stałe obrażenia (`Hit_HigherTier_MoreDamage`), brak linii wzroku (`Hit_BehindHill_NoLineOfSight`, `Hit_Refused_DoesNotWear`), brak sprawdzenia poziomu przy kopaniu (7 testów `DigToolTierTests`), brak zapisu zużycia (6 testów), brak warunku „zebrany" (`Hit_HarvestedProp_NoHit`, `TwoPlayersSameTree_SecondFinalHit_NoHit`), brak obiektów przy kopaniu (6 testów `DigRemovesPropsTests`).

### Przegląd Core A+B (Opus 5.5, 2026-10-16) — werdykt: GO z poprawkami

Metoda: tylko odczyt plików (bez gita i bez uruchamiania testów; zielone wyniki zgłosił koordynator, HEAD 21b4920). Przeczytane w całości: `src/Horyzont.Core/Simulation/PropRules.cs`, `PropDamageLedger.cs`, `ToolTiers.cs`, `Props/PropLayoutV1.cs`, `Props/PropField.cs`, `Props/PropShapesV1.cs`; fragmenty: `Simulation/ServerSimulation.cs` (l. 88–210, 335–353), `Simulation/SurvivalRules.cs` (l. 200–350), `Simulation/OperationApplier.cs` (l. 218–241 struktura złożonej, l. 834–872 `ApplyPropHarvest`/`ApplyPropRegrow`, Grep `IsComponent`), `Simulation/UndoLog.cs` (wpisy obiektów, Grep), `Simulation/IntentValidator.cs` (gałąź 27 i kubełek, Grep), `Hashing/StateHasher.cs` (podsekcja `0x48`, Grep), `Serialization/MessageCodec.cs` (limity 45, Grep), `Props/CubeSphereGrid.cs` (sygnatury i matematyka, Grep), `World/Terrain/ITerrainService.cs:62`, `game/src/Terrain/VoxelTerrainService.cs:399-404` (zaślepka), `game/tests/Constructs/ConstructTestWorld.cs:151`, `game/src/Content/SurvivalContentLoader.cs` (Grep), `src/Horyzont.Persistence/WorldLoader.cs:83` i `ServerRuntimeOptions.cs:71` (rejestr przedmiotów). **Nieprzeczytane:** `PropId`, `PropRegionKey`, `IntentCodec` (27), kodek 47/48 w `OperationCodec`, rekordy i builder checkpointu, `ProtocolConstants` — przyjmuję na podstawie zatwierdzonych testów złotych z A1/B1 (zielonych) i pustego `git diff` korpusu, `DeterminismTests`, `Migrations` i `Generators` zgłoszonego przez wykonawcę.

**Kontrakty chronione — zgodne:**

- **47/48 jako odwracalne składowe:** `IsComponent` obejmuje oba; struktura złożonej dopuszcza je przed jedną składową nieodwracalną; `UndoLog` zapisuje poprzedni tick (cofnięcie 47 przywraca wpis z tym samym tickiem, cofnięcie 48 usuwa). Warunki appliera czytają tylko stan świata: planeta, `IsValid`, siatka planety (`TryGetCellsPerFace` bez wyjątku), zbiór zebranych — zgodne z ZASADY §4 i decyzją A1.
- **`PropLayoutV1`:** decyzja w liczbach całkowitych (XxHash64, przedziały na stałych pozycjach w skali 2³², szum wartości ze smoothstep w stałym przecinku), bez `float` i trygonometrii; pasma wysokości tylko w `Accepts`. Złoty skrót z A6 to zamraża. Uwaga (bez zmiany, bo zamrożone): sieć szumu łat jest osobna dla każdej ściany, więc łaty lasu urywają się na krawędziach ścian — wpisać do Pułapek jako znaną cechę V1.
- **`ITerrainService.TrySampleGeneratedSurface`:** dopisana metoda interfejsu bez podbicia wersji, zaślepka `false` z kontrolą wątku w `VoxelTerrainService`, `NoTerrain` w testach gry — zgodne z D6 (precedens `MeasureSphere`).
- **Intencja 27 bez operacji:** `ServerSimulation.Submit` zwraca `AcceptedWithoutOperation()` po `Resolve` z `harvest == null` i zużywa token dopiero po przyjęciu; ostatnie uderzenie idzie przez `TryApplyAndJournal`, księga zapomina wpis w `Commit` po zastosowaniu. Kolejność walidacji D24 z `UnknownPlanet` przed `WrongPlanet` — zgodna z przeglądem B1.
- **Odmowa 53, komunikat 45, `0x48`:** `ToolTierTooLow` przy obiekcie i przy kopaniu (tylko z treścią obiektów, DO USTALENIA-12); kodek 45 sprawdza liczby przed alokacją (regiony 1…64, region ≤ 256, suma ≤ 4096); podsekcja skrótu dopisywana tylko gdy niepusta i dołączona do warunku sekcji Przetrwania.

**Odstępstwa wykonawcy — akceptuję wszystkie:** trzeci parametr `PropField(…, GameRules? rules)`; `CubeSphereGrid.DirectionOfPoint` i `TryGetCellsPerFace`; `PropRegionKey.IsValid`; `SurvivalContentLoader` czyta `props.json` już w B3 (skutek: w grze poziom narzędzia przy kopaniu działa od B5, a obiektów nie ma do C5 — zgodne z zamierzeniem, zestaw startowy daje manipulator poziomu 1); `ToolTiers` wspólny; gracz bez paska = ręka; wpisy księgi po resecie zostają do wyparcia; planeta w skrócie jako `UInt64`.

**Pominięcie urobku nieznanego w rejestrze świata — DECYZJA: zostaje jako obrona, bez zmiany kodu.** Istniejące światy dostają nowe klucze treści przy wczytaniu: `WorldLoader` wywołuje `GrowRegistries(connection, "item_types", options.ItemTypeKeys, …)` (`WorldLoader.cs:83`), a `ServerRuntimeOptions.ItemTypeKeys` to klucze treści gry („missing keys are appended”). W grze więc `core:wood`/`core:fiber`/`core:crystal` zawsze są w rejestrze; pominięcie dotyczy tylko testów ze światem bez tych kluczy. Wymagany test w C2 (poprawka 3).

**Poprawki (przed C2, jeden commit `fix(core): apply the core A+B review`):**

| # | Plik | Zmiana |
| --- | --- | --- |
| 1 | `src/Horyzont.Core/Props/PropField.cs` | **Pamięć wyników ujemnych.** Dziś `TryGetProp` pamięta tylko istniejące obiekty, a odmowa walidatora nie zużywa tokenu kubełka — klient może zalewać serwer intencjami 27 z identyfikatorem, którego kandydat istnieje, ale pasmo wysokości go odrzuca; każda taka intencja to nowe próbkowanie powierzchni (z prawdziwym próbnikiem ~2 promienie SDF na wątku głównym). Pamiętać także wynik „brak obiektu po próbkowaniu” (ta sama LRU z flagą albo osobny ograniczony zbiór); test `TryGetProp_AbsentAfterSampling_SecondLookupNoSurfaceCall` (`GeneratedSurfaceRadius` poza pasmami, drugie wywołanie nie zwiększa `GeneratedSurfaceCalls`) |
| 2 | `src/Horyzont.Core/Props/PropField.cs` (`CollectInSphere`) | **Filtr przed próbkowaniem.** Komórki brane są z marginesem 1,5 komórki, więc kopanie kulą 1 m próbkuje kilkanaście–kilkadziesiąt kandydatów na pierwszym kopnięciu w okolicy. Przed `TryGetProp` odrzucać kandydata, którego prosta od środka planety wzdłuż kierunku kotwicy (`DirectionOfPoint` z przesunięciem — liczone bez próbkowania) przechodzi dalej niż `radius` od środka kuli (kotwica leży na tej prostej). Wynik bez zmian; test `CollectInSphere_SamplesOnlyCandidatesWhoseRayMeetsSphere` (licznik `GeneratedSurfaceCalls` ≤ liczba takich kandydatów) i istniejący `CollectInSphere_ReturnsOnlyAnchorsInside` zielony. Koszt zmierzyć ponownie w spike'u D3 z prawdziwym próbnikiem |
| 3 | `tests/Horyzont.Persistence.Tests` (krok C2) | nowy `FixtureWorldV3_LoadedWithRepoContent_RegistersPropItems`: `world-v3.db` wczytany z kluczami treści repo ma `core:wood`, `core:fiber`, `core:crystal` w rejestrze (dowód decyzji o pominięciu urobku) |
| 4 | `docs/plans/K6-31-zbieranie.md` (Pułapki, przy odbiorze) | dopisać: szum łat V1 jest osobny na ścianę (łaty urywają się na krawędziach ścian, cecha zamrożona); linia wzroku do obiektu sprawdza tylko teren (budowle nie zasłaniają) |

Poprawki 1–2 nie zmieniają kontraktów (skrót układu, formaty i wyniki walidacji bez zmian), więc nie wymagają ponownego przeglądu; wystarczy zielony komplet testów i wpis wykonawcy.

### Etap B, krok B6 (poprawki z przeglądu Core A+B)

**Commit.** `fix(core): apply the core A+B review`. Poprawki 1 i 2 z przeglądu, poprawka 4 (Pułapki), uwaga z niezależnego przeglądu o `SurvivalRules`; poprawka 3 jest w kroku C2.

- **Poprawka 1.** `PropField` pamięta wynik „kandydat istnieje, pasmo wysokości (albo nieskończony promień) go odrzuca” w tej samej pamięci LRU (wpis z flagą `Present`). Brak powierzchni (`TrySampleGeneratedSurface` = `false`) nadal nie jest pamiętany, bo nic nie próbkowano i brak powierzchni może ustąpić. Testy: `TryGetProp_AbsentAfterSampling_SecondLookupNoSurfaceCall`, `TryGetProp_NoSurface_IsNotRemembered`, `PropHitAllocationTests.RepeatedBandRefusal_SamplesSurfaceOnce_AndDoesNotAllocate` (2000 powtórzonych intencji z odrzuconym pasmem: jedno próbkowanie, 0 bajtów).
- **Poprawka 2.** `CollectInSphere` odrzuca kandydata przed próbkowaniem, gdy prosta od środka planety wzdłuż jego kierunku mija środek kuli o więcej niż promień (margines 1 cm na błąd pojedynczej precyzji). Wynik bez zmian (istniejące testy zielone). Test `CollectInSphere_SamplesOnlyCandidatesWhoseRayMeetsSphere` liczy niezależnie w innej ścieżce kodu kandydatów, których promień trafia w kulę, i wymaga, by liczba próbkowań ich nie przekroczyła (a kandydatów w otoczeniu było wyraźnie więcej). Pomiar kosztu z prawdziwym próbnikiem zostaje w spike'u D3.
- **Uwaga z niezależnego przeglądu (`SurvivalRules`, gałąź `!measured || !hasContainers`).** `OperationApplier.CheckTerrainEdit` **nie** odrzuca edycji niezaładowanego terenu (sprawdza planetę, geometrię `TerrainRules.CheckEdit` i materiał); odrzuca ją walidator (`IntentValidator`: `AreaNotLoaded`, obszar `promień + margines edycji`, a więc nie mniejszy niż kula pomiaru), zanim intencja trafi do resolvera. Dlatego `measured = false` po przejściu walidatora oznacza teren, którego usługa w ogóle nie czyta (`NoTerrain`, test), i tam poziomu nie da się sprawdzić (nie wiadomo, jaki materiał) — zgodne z D9 i przeglądem B1. Prawdziwa luka była inna: **gracz zmierzony, ale bez kontenerów** omijał sprawdzenie poziomu. Poprawione kolejnością: poziom jest sprawdzany, gdy `measured`, przed gałęzią zwykłej edycji; testy `Dig_PlayerWithoutContainers_StillNeedsTheTier` (czerwony przed poprawką) i `Dig_AreaNotLoaded_RejectedByValidator_NothingResolved` (dokumentuje odmowę walidatora).
- **Odstępstwo od polecenia:** polecenie zakładało alternatywę „applier odrzuca → tylko test, nie zmieniaj kodu / nie odrzuca → dodaj kontrolę”; wynik jest pośredni (applier nie odrzuca, walidator tak), więc dodano test dokumentujący walidator i zmieniono kod tylko dla przypadku bez kontenerów.

### Etap C, krok C1 (testy czerwone)

**Commit.** `test: add harvested prop persistence, regrowth and replication tests`. Szkielety (puste) w `src/Horyzont.Server/Props/`: `PropInterest`, `PropRegrowth`, `PropReplicator`. Poprawka 3 z przeglądu Core A+B (`FixtureWorldV3_LoadedWithRepoContent_RegistersPropItems`) jest zielona od początku: `WorldLoader` już dopisuje klucze treści do rejestru, test dokumentuje decyzję o pominięciu urobku nieznanego w rejestrze.

**Sygnatury szkieletów (do wiadomości, bez osobnego przeglądu).**

| Typ | Sygnatura / uwaga |
| --- | --- |
| `PropRegrowth : IServerTickParticipant` | `PropRegrowth(Func<ServerSimulation> simulation, Func<ITerrainService> terrain, Func<bool> isReadOnly, ILogger<PropRegrowth>)`, `Tick()` (skanuje, gdy `CurrentTick % PropRegrowScanEveryTicks == 0`), `static Register(ServerRuntime, Func<ITerrainService>, ILoggerFactory)`. Teren jako `Func<>`, bo rejestracja następuje przed startem świata (wzór `InventoryService`); kompozycja gry poda `VoxelTerrainService` w C5 |
| `PropReplicator : IServerPlayerObserver, IServerTickParticipant` | `PropReplicator(Func<ServerSimulation>, Action<PlayerId, Message> send, Func<PlayerId, bool> isLocalPlayer, ILogger<PropReplicator>)`, `Tick()`, `OnPlayerReady/Leaving`, `static Register(ServerRuntime, ILoggerFactory)`; stałe `ScanEveryTicks = 30`, `MaxRegionsPerPlayerPerScan = 256` (spoza `GameRules`: D17 nie nadaje im nazw) |
| `PropInterest` | `CollectRegions(PlanetParams, Vector3 feet, float meters, List<PropRegionKey>)` (rosnąco, bez powtórzeń, lista czyszczona) i `static DistanceToRegion(PlanetParams, Vector3, PropRegionKey)` (odległość do środka regionu na promieniu planety) — spoza listy plików planu, bo zainteresowanie regionów (D17) potrzebuje jednej wspólnej definicji „region w zasięgu” dla wejścia, wyjścia i testów |

**Odstępstwa od tabeli testów etapu C.**

- **Interpretacja `MaxRegrowPerOperation = 64` (D14):** zdanie planu („zebrane w jednym przebiegu (najwyżej 64) idą jako `PropRegrowOp` albo `CompoundOp` z samych 47") jest dwuznaczne; testy przyjmują, że wszystkie wymagalne obiekty z przeglądanej partii (≤ 256) odrastają w tym samym przebiegu, po najwyżej 64 w jednej operacji (150 wymagalnych = złożone po 64, 64 i 22), jedna wymagalna = zwykła `PropRegrowOp`. Dowód: `Regrow_ManyDue_BatchedMax64`, `Regrow_TwoDue_OneCompound`, `Regrow_AfterTime_NobodyNear_RegrowOp`.
- **`Tick_AboveLongMax_RoundTrips`** zostaje (decyzja A1: tick bit w bit), `Row_NegativeTick_WorldCorrupted` nie powstaje.
- **Dodane poza tabelą:** `HarvestedPropPersistenceTests.Checkpoint_HarvestedAgainAfterRegrow_UpdatesTheTick`, `Row_CellOutsideThePlanetGrid_WorldCorrupted`; `PropRegrowthTests.Regrow_NotWhileReadOnly`, `Regrow_TwoDue_OneCompound`; `PropReplicatorTests.Harvest_DigCompound_SendsEachChangedRegionOnce`; `PropInterestTests` (5 testów: zestaw regionów zgodny z pełnym przeglądem 57 624 regionów planety dla czterech punktów, w tym przy styku trzech ścian; nadzbiór dla 576 m; brak alokacji).
- **Nie powstał test odrzucenia świata z zebranymi obiektami przez `WorldCreator`:** każda operacja zbierająca ustawia `LastOpId`, który `WorldCreator` odrzuca od K1, więc test przechodziłby bez nowego warunku; sam warunek (`HarvestedProps.Count != 0`) dochodzi w C2 jako obrona zgodna z D16.
- **`Batching_Max64RegionsPerMessage`** sprawdza 64 regiony na komunikat i 256 regionów na gracza i przebieg (zasięg 1500 m w `GameRules`), ale **nie** limit 4096 wpisów na komunikat: region ma najwyżej 128 obiektów, więc przekroczenie wymaga ≥ 32 pełnych regionów zebranych w całości (słaby punkt; limit pilnuje kodek 45, test w B2).
- **`Harvest_AfterOperationApplied`** dowodzi kolejności tak, jak można ją sprawdzić w jednostce: komunikat wychodzi w trakcie `ServerSimulation.Applied`, gdy stan zawiera już zebrany obiekt (licznik w chwili wysyłki), po jednym komunikacie na operację i z pełnym stanem regionu. Kolejność względem `OperationApplied` z `EntityReplication` zależy od kolejności rejestracji uczestników (Pułapki) i nie ma testu w tym kroku.

**Wynik (czerwień oczekiwana).** `dotnet build Horyzont.sln` 0 błędów / 0 ostrzeżeń; `dotnet format --verify-no-changes` czysto. `dotnet test --filter "Category!=Soak&Category!=Manual"` (bez `PropsFlowTests`, patrz niżej): Architecture 12, Client 644, Net 170, Integration 42, Constructs 131, Core 1982 zielone; Persistence 211 zielonych / 8 czerwonych (7 nowych `HarvestedPropPersistenceTests` oraz `FixtureWorldV3Props_CopiedToTemp_LoadsAndReplays` — brak kodu zapisu do C2 i brak fikstury); Server 378 zielonych (w tym 1 pominięty z założenia) / 26 czerwonych (wszystkie nowe w `Server.Tests/Props`: `PropRegrowthTests` 10, `PropReplicatorTests` 10 z 11, `PropInterestTests` 8 z 8 przypadków; zielone z założenia: `Regrow_IdleScan_DoesNotAllocate`, `Idle_DoesNotAllocate`). `Integration.Tests/Props/PropsFlowTests`: 8 z 8 czerwonych (wszystkie czekają 30 s na stany obiektów; uruchomione osobno, ~4 minuty w czerwieni). `FixtureWorldV3_LoadedWithRepoContent_RegistersPropItems` zielony od początku (patrz wyżej). Nie uruchamiano gry ani Godota.

### Etap C, krok C2 (Persistence)

**Commit.** `feat(persistence): store harvested props`. `WorldLoader.ReadHarvestedProps` (kolumny `planet_id`, `prop_id`, `tick` bez bloba; planeta, bity zarezerwowane i siatka planety sprawdza `WorldStateBuilder`, więc naruszenie to `InvalidDataException` -> `WorldCorruptedException`), `PersistenceWriter.WriteHarvestedProps` (w transakcji checkpointu: najpierw `DELETE` odrośniętych, potem upsert zebranych, tick bit w bit), `WorldCreator` odrzuca świat z zebranymi obiektami (obrona z D16, bez własnego testu, patrz C1). **Bez żadnej zmiany migracji** (`git diff 646f130..HEAD -- src/Horyzont.Persistence/Migrations` pusty): `user_version` fikstury to nadal 3.

**Fikstura.** `tests/Horyzont.Persistence.Tests/Fixtures/world-v3-props.db` (96 KB) z `FixtureGenerator.Generate_WorldV3Props` (kategoria `Manual`, uruchomiona raz, `PropWorldScript.BuildAsync`): checkpoint z drzewem, skałą i krzewem (ściany 0 i 2), ogon z zebraniem drzewa ze ściany 3 i odroście pierwszego drzewa. `world-v1.db`, `world-v3.db`, `world-v3-blocks.db` bez zmian. Uruchomienie wszystkich testów `Manual` osobno kończy się czerwienią generatorów (cel już istnieje, generatory odmawiają nadpisania) — to zachowanie z K1, nie błąd.

**Poprawka w moich testach C1.** `HarvestedPropPersistenceTests.Checkpoint_WritesHarvested_LoadRestores` i `FixtureWorldV3Props...` w C1 odczytywały stan po wczytaniu jako „sprzed powtórki", a `LoadAndReplay` zwraca ten sam obiekt stanu, który symulacja już powtórzyła; asercje zmienione na stan po powtórce (liczba wierszy checkpointu nadal sprawdzana w SQL).

### Etap C, krok C3 (odrastanie)

**Commit.** `feat(server): regrow flora as server operations`. `PropRegrowth` (`src/Horyzont.Server/Props/`) z `PropsLog` (zdarzenia 8501-8512 dla odrastania i replikacji obiektów). Kolejność sprawdzeń obiektu z wpisu zbioru (tania pierwsza, powierzchnia generatora dopiero dla wymagalnych): kandydat układu i rodzaj z samego ziarna i `PropId` -> czas odrastania z treści (rodzaj bez czasu pomijany) -> upłynął czas (`tick + sekundy * TickRate`, z nasyceniem) -> `PropField.TryGetProp` -> blok LOD 0 kotwicy bez wersji w `TerrainVersions` -> brak gracza online w 20 m -> brak bloku budowli w 20 m. Skan co 60 ticków (`CurrentTick % PropRegrowScanEveryTicks`, nie dwa razy w tym samym ticku), kursor to klucz pierwszego niesprawdzonego wpisu (wyszukiwanie binarne, więc usuwanie wpisów w trakcie nie przesuwa go), partia najwyżej 256 wpisów z zawinięciem, wymagalne sortowane po (planeta, `PropId`) i wysyłane po najwyżej 64 w operacji (jedna = `PropRegrowOp`, więcej = `CompoundOp`). Czasu rzeczywistego nie czyta (ticki), w trybie tylko do odczytu (bezpieczny, zamykanie) nie robi nic.

**Odstępstwa i uwagi.**

- **Rejestracja w grze to C5.** `PropRegrowth.Register(runtime, terrain, loggers)` jest gotowe (jak `InventoryService.Register`); dopisanie jej w `GameComposition` (z `VoxelTerrainService` jako terenem) należy do C5, bo `game/` jest poza zakresem tego zlecenia. Do tego czasu w grze nic nie odrasta (i tak żaden obiekt nie istnieje do C5).
- **Wyjątek w uczestniku ticku zatrzymuje serwer** (`ServerRuntime.TickRunning` -> `EnterFaulted`), więc odrastanie łapie `InvalidOperationException` z `ApplyServerOperation` (niespełniony warunek mimo sprawdzenia stanu), loguje i kończy skan; skan powtarza się za 60 ticków.
- **Obiekt nieznany układowi albo odrzucony pasmem** (zmiana treści/układu) zostaje zebrany, a log (`Warning`, raz na obiekt na proces) to mówi.
- **Przybliżenie budowli:** odległość do bloku liczona od środka komórki (kostka 1 m), zgrubnie bryłą graniczną budowli i bryłą chunku, dokładnie dopiero w pobliskich chunkach; budowla w ramie innej budowli przechodzi przez `TryToPlanetFrame`.
- **Koszt:** skan bez wymagalnych nie próbkuje powierzchni i nie alokuje (`Regrow_IdleScan_DoesNotAllocate`); wymagalny obiekt kosztuje próbkowanie raz (pamięć kotwic ma teraz też wyniki ujemne, poprawka 1 przeglądu).

**Sprawdzenie mutacją (C3).** Kopia pliku przywracana po każdej: warunek bloku z wersją usunięty -> `Regrow_EditedAnchorBlock_Never`; sprawdzenie gracza -> `Regrow_PlayerWithin20m_Waits`; sprawdzenie budowli -> `Regrow_ConstructWithin20m_Waits`; warunek czasu -> `Regrow_BeforeTime_Nothing`, `Regrow_CursorWraps_AllVisited`; partia 64 -> `Regrow_ManyDue_BatchedMax64`; partia skanu 10 zamiast 256 -> `Regrow_ManyDue_BatchedMax64`, `Regrow_CursorWraps_AllVisited` (wszystkie czerwone). **Słabe punkty:** tryb tylko do odczytu pilnuje jeden test (`Regrow_NotWhileReadOnly`), a zawinięcie kursora w obrębie jednego skanu (partia zaczynająca się w środku) pokrywa tylko pośrednio `Regrow_CursorWraps_AllVisited` (600 wpisów, 3 skany).

**Wynik.** `Server.Tests.Props.PropRegrowthTests` 11/11 zielone. Pełny przebieg w opisie kroku C4.
