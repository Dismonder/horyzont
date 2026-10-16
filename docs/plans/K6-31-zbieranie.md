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
- **D16. Zapis.** `HarvestedPropRecord(PlanetId Planet, PropId Prop, Tick Tick)`; `WorldCheckpoint.UpsertedHarvestedProps` / `RemovedHarvestedProps` (`init`, domyślnie puste, porządek po planecie i obiekcie); `ChangeTracker` śledzi klucze; `PersistenceWriter` w transakcji checkpointu: `INSERT OR REPLACE` / `DELETE`. `WorldLoader` czyta kolumny (bez bloba); `WorldStateBuilder.AddHarvestedProp`: planeta istnieje, klucz unikalny, bity zarezerwowane zero, komórka w siatce planety — inaczej `InvalidDataException` → `WorldCorruptedException`. Istnienia w generatorze builder nie sprawdza. `WorldCreator` odrzuca świat z zebranymi obiektami (jak z kontenerami). `tick` w SQLite to `INTEGER` ze znakiem: wartość > `long.MaxValue` → `WorldCorruptedException`.
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
| `Persistence.Tests/HarvestedPropPersistenceTests`: `Checkpoint_WritesHarvested_LoadRestores`, `Checkpoint_Regrown_DeletesRow`, `Row_NegativeTick_WorldCorrupted`, `Row_ReservedBits_WorldCorrupted`, `Row_UnknownPlanet_WorldCorrupted` | D16 |
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
- [ ] A2. `PropId`, `PropRegionKey`, `CubeSphereGrid`, `HarvestedPropStore` (posortowany po planecie i `PropId`, zakres regionu, zbiór zmienionych kluczy) w `WorldState`. Commit `feat(core): add prop ids and the harvested prop store`.
- [ ] A3. Operacje 47, 48 w applierze jako składowe z `UndoLog`; zbiór składowych złożonej (D13); `OperationProps`. Commit `feat(core): add prop harvest and regrow operations`.
- [ ] A4. Kodeki 47/48 (`PropId` w `CoreCodecPrimitives`), `HarvestedPropRecord`, `ChangeTracker`, `WorldCheckpoint`, `WorldStateBuilder`, `CaptureCheckpoint`/`Requeue`. Commit `feat(core): encode and checkpoint harvested props`.
- [ ] A5. `StateHasher` (D15) i test A0 zielony. Commit `feat(core): hash harvested props`.
- [ ] A6. `PropKindV1`, `PropLayoutV1`, `PropLayouts`; złoty skrót `PropLayoutV1_GoldenDigest` zapisany **po** zatwierdzeniu tabeli w A1. Commit `feat(core): add the frozen prop layout v1`.
- [ ] A7. Testy właściwości zielone; sprawdzenie mutacją (usunięty warunek „obiekt niezebrany” w 48 → `PropHarvest_AlreadyHarvested_Fails` i test właściwości czerwone); korpus i `DeterminismTests` bez zmian w plikach.

### Etap B — treść, intencje, protokół

- [ ] B1 (testy napisane, czerwone; **czeka na przegląd Opus 5.5 przed B2**). Testy z tabeli etapu B. Przegląd Opus: intencja 27, odmowa 53, komunikat 45 (bajty i limity), `ITerrainService.TrySampleGeneratedSurface` (D6), `PropField` (D7), D9–D12, D17, D24, `IntentResult.AcceptedWithoutOperation` oraz DO USTALENIA-2, 3, 4, 8, 9. Commit `test(core): add prop hit and tool tier tests`.
- [ ] B2. `RejectReason` 53; intencja 27 i `IntentCodec`; komunikat 45, `MessageCodec`, `MessageChannels`, `MessageRouter`; fixture X6; `ProtocolVersion` = 16 i jego testy. Commit `feat(protocol): add prop states and the prop hit intent`.
- [ ] B3. Treść: `PropDef`, `PropYield`, `ContentLoader` (`props/props.json`, przeciążenie `Parse`, `HasSource`), `SurvivalContent.Props`; pliki `game/content/props/props.json` i przedmioty w `items.json`; nazwy przedmiotów w `items.csv`. Commit `feat(content): add harvestable props and their items`.
- [ ] B4. `ITerrainService.TrySampleGeneratedSurface` (+ `FakeTerrainService.GeneratedSurfaceRadius`, `NoTerrain` w testach gry), `PropField`, `PropInstance`. `VoxelTerrainService` dostaje na razie zaślepkę `false` z kontrolą wątku (prawdziwa w C5, wzór K6-29W). Commit `feat(core): sample the generated surface for props`.
- [ ] B5. `PropDamageLedger`, `PropRules` (walidacja D24 i rozwiązanie D10–D11), `IntentResult.AcceptedWithoutOperation`, `PlayerBuckets.Props`, `GameRules` (D22), wpięcie w `ServerSimulation.Submit` i `IntentValidator`; `SurvivalRules`: poziom przy kopaniu (D9) i obiekty w kuli (D12). Commit `feat(core): hit and harvest props with tool tiers`.
- [ ] B6. Sprawdzenie (niżej) i **przegląd Core A+B przez Opus 5.5** (jak w K6-30: `TryApplyCompound` z 47/48, czyszczenie stanu na każdym wyjściu, `PropRules`, kodeki, `ProtocolVersion`); poprawki w jednym commicie `fix(core): apply the core A+B review` przed C2.

### Etap C — zapis, serwer, gra (serwerowa część)

- [ ] C1 (testy napisane, czerwone). Testy z tabeli etapu C. Commit `test: add harvested prop persistence, regrowth and replication tests`.
- [ ] C2. Persistence: `WorldLoader`, `PersistenceWriter`, `WorldCreator`; fikstura `world-v3-props.db` (`FixtureGenerator.Generate_WorldV3Props`, `Manual`). Commit `feat(persistence): store harvested props`.
- [ ] C3. `PropRegrowth` (D14) jako uczestnik ticku; rejestracja w serwerze. Commit `feat(server): regrow flora as server operations`.
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
