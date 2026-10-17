# K6-38 · Uprawy

| | |
| --- | --- |
| Prowadzi | Gemini 3.8 Flash (Antigravity, myślenie High; reguły: `AGENTS.md`, `docs/ZASADY.md`, ten plan). Kroki z osądem (A0, B4, ocena geometrii środowiska) — Sonnet 5.5 |
| Testy akceptacyjne | Sonnet 5.5, przed kodem (`plan-projektu.md` l. 28 i 197: dla pakietów Gemini testy pisze Sonnet). Testy i kontrakty etapów A i B zatwierdza Opus 5.5 **przed** implementacją |
| Przegląd | Bramki Opus 5.5: A1, A4, B1, B6, C5 oraz końcowy diff Core (ZASADY §3). PR: Sonnet 5.5 (pakiet Gemini, ZASADY §9). Wygląd grządki i rośliny ocenia właściciel; **wykonawca nie uruchamia gry z oknem** |
| Zależy od | K6-30, K6-32, K6-34 (tabela projektu, `plan-projektu.md` l. 139). Baza realizacji: `main` z scalonym K6-34 (warunek twardy, D1); K6-35/K6-36 — miękko, D1 |
| Gałąź | `pakiet/k6-38-uprawy`, utworzona przez wykonawcę w osobnym worktree |
| Protokół | K6-34 = 19, K6-35 = 20, K6-36 = 21, K6-37 = 22; K6-38 = **`main` + 1 w chwili scalenia, planowo 23** (uzgodnienie 2026-10-17) |
| Kamień milowy | K6 Przetrwanie |
| Odbiór ze specyfikacji | „test upraw”: wzrost jako funkcja ticków; zbiór niedojrzałej rośliny daje `NotGrown` (spec 03 l. 411) |

**Cel:** gracz zdobywa pierwsze nasiona z dzikiego krzewu, stawia grządkę, sadzi nasiono, a po czasie z danych zbiera plony i nasiona.
Roślina rośnie tylko w świetle (słońce albo lampa uprawna) i w atmosferze albo w zasięgu działającego generatora tlenu.
Sadzenie i zbiór to jedna operacja złożona w jednym wpisie dziennika. Wzrost jest czystą funkcją stanu grządki i ticka, a warunki środowiska zapisuje dziennik.

Dokument opisuje przyszłą realizację. Przy jego przygotowaniu nie edytowano repozytorium, nie budowano i nie uruchamiano testów.

## Uzgodnienie numerów (Opus, 2026-10-17, `K6-37-40-reconciliation.md`)

- Planowa kolejność scalania: K6-34 (19) → K6-35 (20) → K6-36 (21) → K6-37 (22) → **K6-38 (23)** → K6-39 (24) → K6-40 (bez PV).
- **`BlockFunctionKind.Planter = 18`** (17 = `Teleport` z K6-37). **`ProtocolVersion` = 23.** Obie wartości to `main` + 1 / następna wolna w chwili scalenia: gdyby K6-38 scalił się przed K6-37, dostaje 22 i 17, a K6-37 robi rebase.
- Bez zmian: intencje 31/32 (`0x1F`/`0x20`, format 1 zapisany jawnie — niezależnie od formatu 2 intencji 1/3 z K6-36), odmowa 50, rodzaj stanu 6, brak nowych operacji, komunikatów, blobów i tagów. Brak kolizji z 30/33/34, 47/48/51–58.
- Przy planowanej kolejności K6-35 i K6-36 są w bazie scalenia: bramka „martwy/w przejściu” (48) i koszty grządki/lampy w katalogu K6-36 są **obowiązkowe** przy scaleniu (D1). Etapy A–B mogą ruszyć wcześniej na `main` + K6-34.
- `StateHasher` `0x42`: K6-37 dopisuje warianty 4/7 w tym samym `switch`; `default: throw` dodać dopiero, gdy obsłużone są wszystkie rodzaje 1–7.

## Kontekst

Źródła: `AGENTS.md`, `docs/ZASADY.md` (całość), `docs/spec/03-przetrwanie.md` (l. 30, 162–166, 228–230, 240–244, 246–319, 321–347, 349–368, 411, 440), `docs/plan/plan-projektu.md` (l. 28, 136–141, 197).
Wzorce formatu: `docs/plans/K6-32-rzemioslo-i-stacje.md` (pakiet Flash), `K6-34-zagrozenia-i-kombinezon.md`, `K6-35-smierc-i-odrodzenie.md`.
Wejście: plan Codex `AgentTools/logs/results/plan-k6-38-codex.md`; ten dokument go zastępuje (zmiany w sekcji „Przegląd Opus planu Codex”).

### Stan kodu (sprawdzony odczytem 2026-10-17)

Worktree `k6-35` ma `ProtocolVersion = 18` i **nie zawiera** pełnego K6-34 (brak `OxygenCoverage`, `ShelterEvaluator`, `GeneratorSettlements`). Pełne API K6-34 jest w worktree `k6-34` (`ProtocolVersion = 19`). Fakty poniżej pochodzą z `k6-34`, chyba że zaznaczono inaczej.

| Fakt (plik:linia) | Konsekwencja |
| --- | --- |
| `BlockEntityKind.Planter = 6` „reserved for K6-38” (`BlockEntities/BlockEntityKind.cs:22`) | Rodzaj stanu 6 bez nowej rezerwacji |
| `BlockFunctionKind` kończy się na `OxygenGenerator = 16`; komentarz: „Game content, not a save or wire format” (`World/Blocks/BlockFunctionKind.cs:3,55`) | `Planter` = następna wolna wartość w chwili scalenia (planowo 18, bo K6-37 bierze 17); to treść, nie format |
| `IntentCode`: 1–3, 10, 13–15, 20–29 (`Serialization/IntentCodec.cs:22–38`); `CurrentFormatVersion = 1` | Wolne 31 i 32, przypisane w spec l. 315–316. Bajt kodu na drucie: **`0x1F`** i **`0x20`** (kody dziesiętne) |
| `RejectReason`: brak 50 (`Intents/RejectReason.cs`) | `NotGrown = 50` ze spec l. 319 |
| `ItemSource.Crop = 4` (`Items/ItemSource.cs:16`), `ItemSink.Plant = 7` (`Items/ItemSink.cs:25`) | Bez zmian enumów |
| `WriteBlockEntityState`/`ReadBlockEntityState` bez wersjonowania per rodzaj; rodzaj 5 dopisany w K6-34 bez podbicia wersji (`Serialization/CoreCodecPrimitives.cs:300–396`) | Rodzaj 6 dopisywany tak samo; op 40, blob 21 i komunikat 46 korzystają z jednego prymitywu |
| `ApplyBlockEntitySet`: `default: return "block state kind not supported"`; limit `MaxBlockEntitiesPerConstruct = 4096` (`Simulation/OperationApplier.cs:837–929`) | Nowy `case PlanterBlockState`; stare dzienniki nie mają rodzaju 6 |
| `StateHasher` podsekcja `0x42`: `switch` bez gałęzi `default` (`Hashing/StateHasher.cs:217–267`) | Bez nowego `case` grządka haszowałaby się tylko kluczem i rodzajem — **cicha dziura**; test każdego pola obowiązkowy (D15) |
| `BlockEntityStore.Generators`: posortowany indeks pochodny, utrzymywany w `Set`/`Load`/`Remove` (`BlockEntities/BlockEntityStore.cs:16,43,112–130`) | Indeks `Planters` tym samym wzorcem; nie trzeba osobnego indeksu po stronie serwera |
| `OxygenCoverage.IsCovered(state, planet, point, fuel, ticksPerCell, now, rules)` — „Public for the plants of K6-38” (`Survival/OxygenCoverage.cs:10,25`) | Pokrycie generatorem bez ponownej implementacji |
| `HazardField.Daylight`, `HazardField.HasAtmosphere`, `HazardProfileResolver.Resolve`, `StarSystemMath.SunDirection` używane razem w `HazardTracker.Evaluate` (`Server/Survival/HazardTracker.cs:99–120`) | Te same funkcje i ta sama kolejność dla grządki (D7) |
| `ShelterEvaluator.Evaluate`: promienie przez `ConstructRaycast.Raycast` + `ITerrainService.Raycast`; teren niewczytany „misses” (`Survival/ShelterEvaluator.cs:18–20,41–42`) | Promień nieba grządki: te same dwa wywołania; przy niewczytanym terenie wynik nieba jest **zachowany**, nie liczony (D7) |
| `ITerrainService.IsAreaLoaded(planet, center, radius)` (`World/Terrain/ITerrainService.cs:16`) | Sprawdzenie przed promieniem nieba |
| `GeneratorRules`: postawienie `[ContainerCreateOp, BlockEntitySetOp, blok]`, rozbiórka `[ContainerRemoveOp, BlockEntitySetOp(null), BlockClearOp]` (`Simulation/GeneratorRules.cs:57–115`) | Wzorzec `PlanterRules` (bez kontenera) |
| `PropRegrowth : IServerTickParticipant` — kursor, partia, `isReadOnly`, `ApplyServerOperation`, `CompoundOp` z limitem (`Server/Props/PropRegrowth.cs:17–60, 287–322`) | Wzorzec `CropMonitor` (D6) |
| `IntentValidator`: listy intencji przedmiotów w l. 81, 118 i `BucketFor` l. 132; `ValidateBlockInteract` używa `CheckBlockTarget` (l. 522–572) | Plant/Harvest dopisać do tych trzech list; cel sprawdzać `CheckBlockTarget` |
| `SurvivalRules`: źródło jedzenia i uzupełnienia = własny plecak/pasek (`IsOwnBagOrHotbarSlot`, l. 87–93) | To samo źródło dla nasiona |
| `PropRules.Resolve`: kopie slotów paska i plecaka, `ItemPlacement.Create`, nadmiar ginie (l. 139–153) | Zbiór grządki używa kopii, ale **odmawia** przy nadmiarze (D11) |
| `ContentLoader.HasSource(key, yields, startKit, props, recipes)` (`Content/ContentLoader.cs:213–222`) | Dopisać plony upraw jako źródło |
| `core:bush` daje `core:fiber` ×2 i `core:berries` ×2 (`game/content/props/props.json:8`); `core:lamp` ma funkcję `light` (`blocks.json:37`); parametry światła to treść wizualna, nie Core (`BlockFunction.cs:37`) | Nasiono w urobku krzewu; lampa uprawna rozpoznawana z danych upraw (D13) |
| `GameServerHooks.TickRate = 60` (`Server/GameServerHooks.cs:21`) | Przeliczenia czasu w przykładach |
| Lokalizacja: `game/locale/{items,blocks,tools,ui}.csv` | Bez nowych plików CSV |
| Projekty testów: `tests/Horyzont.{Core,Server,Client,Persistence,Integration,Constructs,Architecture}.Tests`; gdUnit przez `dotnet test game` (plan K6-34 l. 355) | Komendy akceptacji poniżej |

Plan K6-36 nie jest w `docs/plans/` żadnego sprawdzonego worktree. Istnieje szkic Codex `AgentTools/logs/results/plan-k636-codex.md` (intencje 34–36, odmowy 51/52/55/58, PV 21) i jego poprawiona wersja z przeglądu Opus (intencja 34, odmowy 51/52/55/58, `CompoundOp` v2, PV 21). Numery 31/32/50 nie kolidują z żadną z nich.

## Przegląd Opus planu Codex (werdykt: GO z poprawkami — ten plan je zawiera)

1. **Model wzrostu zmieniony.** Codex: akumulator + rozliczanie „przed każdą zmianą środowiska” + przedziałowa ekspozycja dnia i nocy (`CropExposure`, `CropEnvironmentIndex`, punkty wejścia mutacji). Środowisko grządki zmienia się przez lampy (op 49), dowolne bloki i teren nad grządką, paliwo generatora, ruch budowli i porę dnia — lista punktów wejścia jest otwarta, a K6-34 B1 pokazało, że nawet dla jednego rodzaju bloku „settle-before na każdej ścieżce” łatwo przeoczyć. Tego nie da się powierzyć Flash. Zamiast tego: **warunki próbkowane przez serwer i zapisane w stanie grządki operacją 40** (D4–D7). Wzrost pozostaje czystą funkcją stanu i ticka.
2. **Baza:** worktree `k6-35` ma PV 18 i brak API K6-34; plan musi startować z `main` po K6-34 (D1). Codex poprawnie wykrył braki, ale nie wskazał, gdzie API istnieje.
3. **Bajty intencji:** „`31 01 BlockRef SlotRef`” było błędne — kody są dziesiętne (`0x1F`, `0x20`), a typów `BlockRef`/`SlotRef` w kodeku intencji nie ma; pola jak w `BlockInteractIntent` i `EatIntent`.
4. **Format stanu:** usunięty `CropKey` (64 B): 4096 stanów × ~90 B przekracza 256 KiB komunikatu 46. Gatunek identyfikuje typ nasiona (D3).
5. **PV:** `main` + 1 przy scaleniu, nie sztywne 22 — K6-37 może scalić się wcześniej.
6. **Usunięto pauzę wzrostu w trybie `Ship`** (D15 Codex): próbkowanie bierze bieżącą pozę, historia trajektorii niepotrzebna.
7. **Lampa uprawna:** obszar działania w komórkach tej samej budowli (D8), bez zmian `BlockFunction` dla światła.
8. **Usunięto edycję `recipes.json`:** grządka i lampa to bloki, a koszt bloków należy do K6-36 (D14).
9. **Walidator:** wskazane dokładne listy w `IntentValidator` (l. 81, 118, 132) i źródło nasiona = własny plecak/pasek.
10. **Skrót:** `switch` w `StateHasher` nie ma `default` — test każdego pola i propozycja `default: throw` (D15).
11. **Testy:** usunięto testy zależne od odrzuconego modelu (orakl przedziałów, „brak skanowania co tick” bez definicji budżetu); dodano zasadę „odmowa zaczyna od przyjętej kontroli”, właściwość odniesienia tick po ticku dla monitora i jawne liczniki w teście zachowania przedmiotów.
12. **Treść:** `HasSource` musi znać plony upraw; dane demonstracyjne i reguły lampy przeniesione do `crops.json` i `CropRules`.

## Zakres

1. Stan bloku `Planter` (6): pusta i obsadzona grządka.
2. Czyste funkcje wzrostu i warunków; ocena środowiska grządki w Core.
3. `PlantIntent` (31), `CropHarvestIntent` (32), odmowa `NotGrown` (50).
4. Serwerowy monitor warunków (`CropMonitor`) jako źródło operacji 40 rozliczenia.
5. Treść: `crops.json`, nasiono, grządka, lampa uprawna, nasiono w urobku krzewu.
6. Zapis, powtórka, skrót, replikacja przez istniejące ścieżki.
7. Minimalna interakcja klienta i prezentacja etapów rośliny.

Poza zakresem: kary śmierci i bramka przejścia (K6-35), koszty bloków, podparcie i zawalenia (K6-36), teleporty (K6-37), docelowy interfejs i ekran grządki (K6-39), pętla botów (K6-40), nawadnianie (D9), energia, automatyzacja, mnożnik surowców dla plonów (DO USTALENIA-9).

## Decyzje

### D1. Baza i zależności

Baza realizacji: `main` ze scalonym K6-34 (wymagane: `OxygenCoverage`, `HazardProfileResolver`, `HazardField`, `GeneratorRules`, `ProtocolVersion = 19`). K6-35 i K6-36 nie są formalnymi zależnościami (tabela projektu); jeśli są już w `main`, K6-38 korzysta z ich bramki „martwy/w przejściu” (K6-35) i ścieżki kosztów postawienia (K6-36). Jeśli nie — A0 zapisuje to w „Odstępstwach”, a odpowiednie miejsca dostają komentarz z numerem pakietu do uzupełnienia przy rebase.

Nie kopiować kodu z aktywnych worktree innych pakietów.

**Uzasadnienie:** `plan-projektu.md` l. 139; stan kodu z Kontekstu.

### D2. Bez nowych operacji, komunikatów, blobów, tagów i migracji

Uprawy używają: `CompoundOp` 30, `ItemCreateOp` 33 (`ItemSource.Crop`), `ItemDestroyOp` 34 (`ItemSink.Plant`), `BlockEntitySetOp` 40, automatycznego odkrywania przez operację 43 (istniejące `ServerSimulation.DiscoverAfter`, K6-32 D9b), komunikatu 46, bloba 21, tabeli `block_entities`, podsekcji skrótu `0x42`.

**Uzasadnienie:** spec l. 246–319, 321–347, 349–368; kody 30–49 zajęte w całości (spec l. 273).

### D3. Stan grządki

```text
PlanterBlockState(CropPlanting? Planting)            // Kind = Planter (6)
CropPlanting(ItemTypeId Seed, Tick PlantedAt, Tick SettledAt,
             ulong GrowthTicks, ulong RequiredTicks, PlanterConditions Conditions)
```

| Pole | Znaczenie |
| --- | --- |
| `Seed` | Typ zużytego nasiona; identyfikuje gatunek (`CropDef` wybierany po kluczu nasiona) |
| `PlantedAt` | Tick posadzenia (spec l. 164) |
| `SettledAt` | Tick, do którego wzrost jest już wliczony w `GrowthTicks` |
| `GrowthTicks` | Ticki wzrostu zgromadzone do `SettledAt` |
| `RequiredTicks` | Czas wzrostu z danych, zamrożony przy sadzeniu: `growSeconds × TickRate` |
| `Conditions` | Warunki środowiska obowiązujące od `SettledAt` (D4) |

Niezmienniki (sprawdzane w kodeku → `ProtocolViolationException`, w applierze → komunikat błędu, w konstruktorze `CropPlanting` → `ArgumentException`):
`Seed` w 1..`MaxItemTypes`; `RequiredTicks` w 1..`ProtocolConstants.MaxCropGrowthTicks`; `GrowthTicks ≤ RequiredTicks`; `PlantedAt ≤ SettledAt`; `Conditions & ~0x1F == 0`.

Gatunek nie jest osobnym kluczem tekstowym: spec mówi „nasiono, tick posadzenia i gatunek”, a w treści EA każdy gatunek ma dokładnie jedno nasiono (walidacja treści, D13). `ItemTypeId` jest stały w świecie (rejestr rośnie tylko przy wczytaniu, spec l. 215). Klucz 64 B na grządkę rozsadzałby komunikat 46 (4096 stanów na budowlę × ~90 B > 256 KiB).

**Uzasadnienie:** spec l. 164, 230; `ProtocolConstants.MaxReliableMessageBytes = 256 KiB`, `MaxBlockEntitiesPerConstruct = 4096`.

### D4. Warunki jako flagi

```text
[Flags] enum PlanterConditions : byte
    SkyOpen = 1, Daylight = 2, GrowLight = 4, Atmosphere = 8, Generator = 16   // maska 0x1F

IsLit(c)     = (SkyOpen && Daylight) || GrowLight
HasAir(c)    = Atmosphere || Generator
IsGrowing(c) = IsLit(c) && HasAir(c)
```

Osobne bity (zamiast jednego „rośnie”) pozwalają: zachować `SkyOpen`, gdy teren nie jest wczytany (D7), pokazać graczowi przyczynę zatrzymania (K6-39) i testować każdą regułę osobno.

**Uzasadnienie:** spec l. 165.

### D5. Czysta funkcja wzrostu

`Crops/CropGrowth.cs` (statyczna, bez alokacji, bez treści, terenu i zegara):

```text
Elapsed(p, now)  = now > p.SettledAt ? now - p.SettledAt : 0
Progress(p, now) = p.GrowthTicks + (IsGrowing(p.Conditions) ? min(Elapsed(p, now), p.RequiredTicks - p.GrowthTicks) : 0)
IsRipe(p, now)   = Progress(p, now) == p.RequiredTicks
Settle(p, now, next) = p with { GrowthTicks = Progress(p, now), SettledAt = max(now, p.SettledAt), Conditions = next }
Stage(p, now)    = IsRipe ? 3 : (Progress * 3) / RequiredTicks      // 0, 1, 2 rośnie; 3 dojrzała; liczone w ulong bez przepełnienia
```

`min` przed dodawaniem wyklucza przepełnienie. Brak `float`. `Settle` z tymi samymi warunkami i tym samym tickiem nic nie zmienia.

**Uzasadnienie:** spec l. 164 i 242 („czysta funkcja ticków i danych, wspólna dla serwera i klienta”), ZASADY §4.

### D6. Warunki próbkuje serwer; zmianę zapisuje operacja 40

`src/Horyzont.Server/Crops/CropMonitor.cs : IServerTickParticipant`, kopia schematu `PropRegrowth`:

1. Co tick bierze do `k = clamp(ceil(N / PlanterSampleEveryTicks), 1, MaxPlanterSamplesPerTick)` grządek z `BlockEntityStore.Planters` (N = liczba grządek), od kursora po `BlockRef`, z zawinięciem.
2. Pomija pustą grządkę i dojrzałą (`IsRipe(now)`): ich warunki nie zmieniają wyniku.
3. Dla pozostałych: `next = PlanterEnvironment.Evaluate(..., previous: p.Conditions)`. Gdy `next != p.Conditions`, dodaje `BlockEntitySetOp(construct, cell, typBloku, new PlanterBlockState(CropGrowth.Settle(p, now, next)))`.
4. Zebrane zmiany jednego ticka grupuje po budowli, sortuje po `BlockRef` i stosuje przez `ServerSimulation.ApplyServerOperation` po najwyżej `MaxPlanterSettlementsPerOperation` składowych (`CompoundOp`, gdy więcej niż jedna). Jedna budowla = jeden wpis = jeden komunikat 46.
5. W trybie tylko do odczytu (`isReadOnly`) nic nie robi. `InvalidOperationException` z `ApplyServerOperation` loguje i przerywa bieżący tick (wzorzec `PropRegrowth.Regrow`).

Opóźnienie wykrycia zmiany: najwyżej `max(PlanterSampleEveryTicks, ceil(N / MaxPlanterSamplesPerTick))` ticków. Przy wartościach domyślnych (120 ticków, 8 próbek) to 2 s do 960 grządek. Spec nie wymaga dokładności co do ticka; przybliżenie trafia do `docs/ZASADY.md` jako nowa zasada w kroku D4.

Determinizm: powtórka stosuje zapisane operacje 40 i nigdy nie uruchamia `PlanterEnvironment`. Kolejność próbek zależy od kursora, ale wynik świata wyznacza wyłącznie dziennik — jak przy odrastaniu (ZASADY l. 30) i rozliczeniu stacji (ZASADY §4).

Odrzucone: „rozliczenie przed każdą zmianą środowiska” (otwarta lista mutacji, patrz przegląd pkt 1); analityczne liczenie dnia i nocy w funkcji wzrostu (nie rozwiązuje zasłonięcia nieba, lamp ani paliwa); zapisywanie wzrostu co próbkę (dziennik rośnie z liczbą grządek, nie ze zmianami).

**Uzasadnienie:** ZASADY l. 30 i §4 („zdarzenia czasowe są operacjami serwera”), wzorzec `PropRegrowth` i `HazardTracker` (próbkowanie co N ticków).

### D7. Ocena środowiska grządki

`src/Horyzont.Core/Crops/PlanterEnvironment.cs`, statyczna, bez alokacji, tylko wątek główny (teren):

```text
Evaluate(WorldState state, ITerrainService terrain, SurvivalContent survival, CropRules crops, HazardRules hazards,
         int tickRate, BlockRef planter, Tick now, PlanterConditions previous) -> PlanterConditions
```

1. `GrowLight` (D8) liczony zawsze.
2. `ContainerReach.TryCellCenter(state, planter.Construct, planter.Cell, out planet, out centre)`; `false` albo brak `PlanetParams` → zwraca tylko `GrowLight` (poza planetą roślina nie rośnie; DO USTALENIA-6).
3. `up = TerrainMath.UpAt(centre)`; `origin = centre + up × crops.SkyOriginOffsetMeters` (0,9 m > √3/2, więc poza własną komórką przy dowolnym obrocie budowli).
4. `Daylight`: `cycle = state.StarSystem?.SunCycleSeconds ?? planetParams.DayLengthSeconds`; `sun = StarSystemMath.SunDirection(now.Value, tickRate, cycle)`; `d = HazardField.Daylight(up, (Vector3)sun, hazards.TwilightBand)`; bit, gdy `d >= crops.MinDaylight`. Dokładnie te wywołania co `HazardTracker.cs:113–115`.
5. `SkyOpen`: gdy `!terrain.IsAreaLoaded(planet, origin, crops.SkyRayMeters)` — **bit z `previous`**. W przeciwnym razie bit, gdy ani `ConstructRaycast.Raycast(state.Constructs, FrameRef.Of(planet), origin, up, crops.SkyRayMeters, out _)`, ani `terrain.Raycast(planet, origin, up, crops.SkyRayMeters, out _)` nie trafia.
6. `Atmosphere`: bez treści zagrożeń (`survival.Hazards is null`) — zawsze ustawiony. Inaczej `height = |centre| − promień planety`; `profile = HazardProfileResolver.Resolve(state, survival.Hazards, planet, height)`; bit, gdy `HazardField.HasAtmosphere(HazardField.Rates(profile, height, d, 1f))`.
7. `Generator`: z treścią zagrożeń `OxygenCoverage.IsCovered(state, planet, centre, paliwo, ticksPerCell, now, hazards)` (paliwo i `ticksPerCell` jak w `HazardTracker` l. 42–43); bez treści — nieustawiony.

Jedno źródło reguł: tę funkcję wołają `PlanterRules` (sadzenie, warunki początkowe) i `CropMonitor`. Żadna inna klasa nie liczy warunków grządki.

**Uzasadnienie:** spec l. 165 i 139; ZASADY l. 22 (teren tylko na wątku głównym), §6.

### D8. Lampa uprawna

Lampa uprawna to blok z funkcją `Light` wymieniony w `crops.json → growLights`. Oświetla grządkę P, gdy leży **w tej samej budowli** w komórce L: `|L.x − P.x| ≤ 1`, `|L.z − P.z| ≤ 1`, `1 ≤ L.y − P.y ≤ crops.GrowLightReachCells` (osie budowli), i ma `FunctionState == 1` (włączona, op 49). Przegląd komórek w stałej kolejności (y, potem x, potem z), wyjście przy pierwszym trafieniu. Zwykła `core:lamp` nie jest uprawna. Bez kosztu energii (EA nie ma energii).

**Uzasadnienie:** spec l. 165; parametry światła w Core są treścią wizualną (`BlockFunction.cs:37`); 3×3×4 = 36 odczytów komórki na próbkę.

### D9. Nawadnianie — brak w EA (DO USTALENIA-3)

Spec nie definiuje wody ani podlewania. Grządka nie wymaga nawadniania; brak zasobu, intencji i wysychania. Jeśli właściciel chce podlewania, wymaga to zmiany spec i nowych numerów — przed A1.

### D10. `PlantIntent` (31)

Pola (spec l. 315): budowla, komórka grządki, kontener i slot nasiona.
Kolejność sprawdzeń (każda odmowa przed pobraniem tokenu, K1-03):

1. Gracz istnieje i jest online (istniejące).
2. Parametry: kontener i slot to własny plecak albo pasek (`IsOwnBagOrHotbarSlot`, jak `EatIntent`), inaczej `InvalidParameters`; bez treści przetrwania/upraw `InvalidParameters`.
3. Bramka K6-35 „martwy albo w przejściu” (`InTransition` 48), jeśli istnieje w bazie (D1).
4. Kubełek `Items` (spec l. 345): brak tokenu → `RateLimited`.
5. `CheckBlockTarget` (planeta, zasięg, linia wzroku — te same odmowy co `BlockInteractIntent`).
6. Resolver `PlanterRules`: funkcja bloku ≠ `Planter` albo stan ≠ `PlanterBlockState` → `InvalidParameters`; `Planting != null` → `CellOccupied` (14); slot pusty → `NotEnoughItems` (41); typ w slocie nie jest nasionem żadnego `CropDef` → `InvalidParameters`.
7. Wynik:

```text
CompoundOp[
  ItemDestroyOp(kontener, slot, seed, 1, ItemSink.Plant),
  BlockEntitySetOp(budowla, komórka, typBloku,
      PlanterBlockState(CropPlanting(seed, now, now, 0, required, PlanterEnvironment.Evaluate(..., previous: 0))))]
```

`required = clamp(growSeconds × TickRate, 1, MaxCropGrowthTicks)` liczy resolver z treści; applier jej nie czyta. Sadzenie w złych warunkach jest dozwolone — roślina czeka.

**Uzasadnienie:** spec l. 297, 315, 345; ZASADY l. 74, §4; wzorzec rozwiązywania `EatIntent`.

### D11. `CropHarvestIntent` (32)

Sprawdzenia 1, 3, 4, 5 jak w D10 (bez kontenera). Resolver:

- stan ≠ grządka → `InvalidParameters`; `Planting == null` albo `!IsRipe(now)` → `NotGrown` (50);
- `CropDef` po kluczu nasiona; brak definicji (wycofany gatunek) → wynik zastępczy: 1 sztuka nasiona, wpis do logu;
- kopie slotów paska i plecaka (jak `PropRules.cs:141–142`), `ItemPlacement.Create` dla każdego wpisu `yield` w kolejności treści; **jakakolwiek reszta > 0 → `InventoryFull` (40), bez operacji** (inaczej niż przy obiektach generatora, gdzie nadmiar ginie);
- wynik: `CompoundOp[ItemCreateOp… (ItemSource.Crop), BlockEntitySetOp(PlanterBlockState(null))]`.

Odkrycie nowych typów robi istniejące `DiscoverAfter`. Drugi zbiór tej samej rośliny w tym samym ticku widzi pustą grządkę → `NotGrown` (symulacja jednowątkowa).

**Uzasadnienie:** spec l. 166, 292, 316; niezmiennik zachowania przedmiotów.

### D12. Pierwsze nasiona z dzikiego krzewu

`game/content/props/props.json`: do `yield` `core:bush` dopisać `{ "item": "core:berry_seed", "count": 1 }`. `PropLayoutV1`, rodzaje, kolejność i identyfikatory obiektów bez zmian (ZASADY §3, `Props/`).

**Uzasadnienie:** spec l. 166.

### D13. Treść upraw

Nowy `game/content/crops/crops.json`, `formatVersion: 1`:

```json
{
  "formatVersion": 1,
  "crops": [
    { "key": "core:berry", "seed": "core:berry_seed", "growSeconds": 1200,
      "yield": [ { "item": "core:berries", "count": 3 }, { "item": "core:berry_seed", "count": 2 } ] }
  ],
  "growLights": [ { "block": "core:grow_light" } ]
}
```

Walidacja w `ContentLoader` (błąd = wyjątek ładowania jak dla innych plików): duplikat klucza; nasiono użyte przez dwa gatunki; nieznane nasiono, plon albo blok; `growSeconds` poza 1..86400; `yield` pusty albo > 8 wpisów; `count` poza 1..999; blok lampy bez funkcji `light`. Brak pliku = brak upraw (starsze fikstury działają, intencje dają `InvalidParameters`). `HasSource` uwzględnia plony upraw.

Nowe wpisy: `items.json` — `core:berry_seed` (kategoria jak `core:fiber`, bez flag); `blocks.json` — `core:planter` (`"function": { "kind": "planter" }`) i `core:grow_light` (`"function": { "kind": "light", ... }` jak `core:lamp`). `BlockFunctionKind.Planter`, `BlockFunction.Planter`, parsowanie `"planter"` w `BlockTypeCatalog`.

`CropRules` (Core, rekord z wartościami domyślnymi i walidacją, wzorzec `HazardRules`; jedna instancja przez konstruktor `ServerSimulation`, lekcja K6-33 B6): `SkyRayMeters = 32`, `SkyOriginOffsetMeters = 0.9`, `MinDaylight = 0.5`, `GrowLightReachCells = 4`.
`GameRules`: `PlanterSampleEveryTicks = 120`, `MaxPlanterSamplesPerTick = 8`, `MaxPlanterSettlementsPerOperation = 256`.

**Uzasadnienie:** spec l. 218–219 (treść w JSON, walidacja przy starcie), K6-32 (receptury w JSON).

### D14. Postawienie i rozbiórka grządki

`Simulation/PlanterRules.cs` według `GeneratorRules`:

- postawienie: `CompoundOp[BlockEntitySetOp(..., PlanterBlockState(null)), blok]`;
- rozbiórka: `CompoundOp[BlockEntitySetOp(..., null), BlockClearOp]`; **posadzona roślina przepada** (nasiono już zeszło ujściem `Plant`, więc bilans przedmiotów się zgadza; DO USTALENIA-5);
- wywołanie w `ServerSimulation` w tym samym miejscu i kolejności co `GeneratorRules` (A0 zapisuje linię).

Jeśli K6-36 jest w bazie, grządka i lampa przechodzą jego ścieżką kosztu i `CompoundOp` v2 tak samo jak generator; wpisy kosztów obu bloków w katalogu K6-36 (DO USTALENIA-7). Bez K6-36 — bez kosztów, notatka w „Odstępstwach”. Zawalenie (K6-36) usuwa stan grządki jak każdy stan bloku funkcyjnego.

Applier: `case PlanterBlockState` — tylko niezmienniki strukturalne D3; zgodność z funkcją bloku sprawdza walidator (spec l. 262). Istniejący limit `MaxBlockEntitiesPerConstruct` obowiązuje.

**Uzasadnienie:** spec l. 283–284; ZASADY l. 29, §4 (l. 89).

### D15. Zapis i skrót

- Bez migracji; `block_entities` + blob 21 przez wspólny prymityw.
- `StateHasher` `0x42`: `case PlanterBlockState` — `U32(0|1)`, a przy obsadzie kolejno `Seed`, `PlantedAt`, `SettledAt`, `GrowthTicks`, `RequiredTicks` (U64) i `Conditions` (U32). Światy bez grządek haszują się jak dotąd.
- Propozycja do bramki A1: dodać `default: throw new InvalidOperationException(...)` w tym `switch`, aby nowy rodzaj nie mógł się cicho nie haszować. Dla istniejących rodzajów nic nie zmienia; opis w PR (ZASADY §3, `Hashing/`).
- Czas wyłączonego serwera nie daje wzrostu (ticki świata stoją).

**Uzasadnienie:** spec l. 357, 365, 368; ZASADY §3–4.

### D16. Sieć i klient

Klient wysyła wyłącznie intencje 31/32; niczego nie zmienia lokalnie. Stan przychodzi w 46 (host: `SharedBlockEntities`, zdalny: `ClientBlockEntities`). Etap i pasek klient liczy sam: `CropGrowth.Stage/Progress` z tickiem serwera (jak pasek stacji, spec l. 343). Gdy `!IsGrowing`, podpowiedź pokazuje przyczynę z flag D4.

`BlockInteraction`: `BlockFunctionKind.Planter` → podpowiedzi `Plant` (wybrany slot paska ma nasiono), `NeedSeed`, `Growing` (etap, przyczyna), `Harvest` (dojrzała). Klawisz interakcji wysyła odpowiednią intencję przez `IIntentSender` z `PendingIntentTracker`. Jedno naciśnięcie = jedna intencja.

### D17. Stan postaci i zagrożenia

K6-38 nie zapisuje `PlayerVitals` ani `PlayerState.Vitals` (K6-34 D22): jedzenie plonów to istniejący `EatIntent`. Zimno, żar, toksyny i promieniowanie nie wpływają na rośliny (spec ich nie wiąże z uprawami).

### D18. Limity i koszt serwera

Intencje 31/32 w kubełku `Items` (20/s, seria 40). Ocena środowiska przy sadzeniu wykonuje się dopiero po wszystkich tanich sprawdzeniach, więc tylko przy przyjęciu. `CropMonitor` ma twardy budżet `MaxPlanterSamplesPerTick` na tick; `OxygenCoverage` jest O(liczba generatorów) na próbkę. Gorące ścieżki bez alokacji (ZASADY §6): `PlanterEnvironment.Evaluate` i pętla próbkowania `CropMonitor` (budowanie operacji przy zmianie może alokować).

## Numery, formaty i wersje

| Rodzaj | Przydział K6-38 | Stan |
| --- | --- | --- |
| Operacje | brak nowych; używa 30, 33, 34, 40 (43 automatycznie) | zweryfikowane |
| Intencje | **31 `PlantIntent`** (bajt `0x1F`), **32 `CropHarvestIntent`** (bajt `0x20`), wersja formatu 1 | wolne w kodzie; przypisane w spec |
| Odmowy | **50 `NotGrown`**; ponownie: 4, 7, 14, 19, 20, 40, 41, (48 z K6-35) | 50 wolne w kodzie; spec l. 319 |
| Komunikaty, bloby, tagi skrótu, migracje | brak nowych (46, 21, `0x42`) | zweryfikowane |
| Rodzaj stanu bloku | **6 `Planter`** | rezerwacja w kodzie |
| `BlockFunctionKind` | **`Planter` = następna wolna przy scaleniu, planowo 18** (17 = `Teleport`, K6-37); treść, nie format | zweryfikowane 16 jako ostatnie |
| Stałe protokołu | **`ProtocolConstants.MaxCropGrowthTicks = 5_184_000`** (24 h przy 60 Hz) | nowe |
| `ProtocolVersion` | **`main` + 1 przy scaleniu**; planowo **23** (po K6-37 = 22) | test kontraktu ustawiany przy scaleniu |

Zajęte przez sąsiadów (nie ruszać): K6-31 op 47/48, intencja 27, odmowa 53, komunikat 45, tag `0x48`; K6-32 op 43/45, intencja 24, odmowy 44–46, komunikat 48, blob 25; K6-33 op 41, intencja 25, odmowa 49, komunikat 44; K6-34 op 42, intencja 26, rodzaj 5; K6-35 op 38/39/46, bloby 23/24, komunikat 47, odmowa 48, tagi `0x54`/`0x57`; K6-36 intencja 34, odmowy 51/52/55/58. Intencja 30, odmowa 47 i rodzaje 4/7 należą do K6-37; intencja 33 i odmowa 56 do K6-39.

### Format stanu rodzaju 6 (zatwierdza Opus w A1)

```text
06                      // BlockEntityKind.Planter
u8 flags                // 0 = pusta grządka, 1 = obsadzona; inne -> ProtocolViolationException
if flags == 1:
  varuint Seed          // 1..MaxItemTypes
  varuint PlantedAt
  varuint SettledAt     // >= PlantedAt
  varuint GrowthTicks   // <= RequiredTicks
  varuint RequiredTicks // 1..MaxCropGrowthTicks
  u8 Conditions         // bity 0-4; bity 5-7 = 0
```

Złote bajty (varuint = LEB128, `ByteWriter.WriteVarUInt:85–97`):

- pusta: `06 00`;
- obsadzona `Seed 5, PlantedAt 100, SettledAt 160, GrowthTicks 60, RequiredTicks 72000, Conditions 0x0B`: `06 01 05 64 A0 01 3C C0 B2 04 0B`.

Wersje op 40, bloba 21 i komunikatu 46 bez zmian — precedens K6-34 (rodzaj 5). Stary odczyt odrzuca rodzaj 6 (`Unknown or reserved block state kind`), więc nowy dziennik nie odtworzy się na starej kompilacji; zgodność sieciową zapewnia podbicie `ProtocolVersion`. Bajty rodzajów 1, 2, 3, 5 bez zmian (test).

Rozmiar 46: najgorszy przypadek grządki ≈ 2 + 2 + 4×10 + 1 = 45 B + klucz komórki; A1 liczy dokładny najgorszy rozmiar dla 4096 stanów. Jeśli przekracza 256 KiB minus nagłówek — dodać limit grządek na budowlę w applierze, jak `MaxTimedStationsPerConstruct`.

### Format intencji

```text
1F 01 varuint(Construct) varint(X) varint(Y) varint(Z) varuint(Container) varuint16(Slot)   // PlantIntent
20 01 varuint(Construct) varint(X) varint(Y) varint(Z)                                      // CropHarvestIntent
```

Komórka przez `CoreCodecPrimitives.WriteCellPos`, jak `BlockInteractIntent` (`IntentCodec.cs:252–256`); kontener i slot jak `EatIntent` (`IntentCodec.cs:420–422`).
Złote: `PlantIntent(7, (0,0,0), 9, 4)` = `1F 01 07 00 00 00 09 04`; `CropHarvestIntent(7, (0,0,0))` = `20 01 07 00 00 00`. Drugi przykład z ujemną komórką ręcznie sprawdza Opus w B1 (kodowanie `WriteVarInt` niezweryfikowane).

## Kontrakty chronione

| Ścieżka | Zmiana | Wymaganie |
| --- | --- | --- |
| cały `src/Horyzont.Core/` | model, funkcje, ocena środowiska, reguły | przegląd Opus (ZASADY §3) |
| `BlockEntities/`, `Serialization/CoreCodecPrimitives.cs` | rodzaj 6, indeks `Planters` | złote bajty nowego i starych rodzajów |
| `Simulation/OperationApplier.cs` | `case PlanterBlockState` | korpus `TerrainJournalGoldenCorpusTests` i `DeterminismTests` bez zmian w plikach (ZASADY l. 72) |
| `Intents/`, `Serialization/IntentCodec.cs` | intencje 31/32 v1, odmowa 50 | `ProtocolVersion` + 1 |
| `Protocol/ProtocolConstants.cs` | `MaxCropGrowthTicks`, `ProtocolVersion` | test kontraktu |
| `Hashing/StateHasher.cs` | `case` grządki, ewentualnie `default: throw` | opis w PR |
| `World/Blocks/BlockFunction*.cs` | `Planter` | treść, nic do podbicia (wyjątek `BlockTypeDef`) |
| `Simulation/IntentValidator.cs`, `GameRules.cs` | listy intencji, wartości monitora | Opus; bez podbicia (ZASADY l. 74) |
| `Props/`, generator, migracje, korpus złoty | **bez zmian** | pusty diff względem bazy A0 |

## Projekt (pliki)

| Obszar | Nowe | Zmieniane |
| --- | --- | --- |
| Core model | `BlockEntities/PlanterBlockState.cs`, `Crops/CropPlanting.cs`, `Crops/PlanterConditions.cs`, `Crops/CropGrowth.cs`, `Crops/CropRules.cs` | `BlockEntities/BlockEntityStore.cs` (`Planters`) |
| Core środowisko | `Crops/PlanterEnvironment.cs` | — |
| Core treść | `Content/CropDef.cs`, `Content/CropContent.cs` | `Content/ContentLoader.cs`, `Content/SurvivalContent.cs` |
| Core intencje i reguły | `Intents/PlantIntent.cs`, `Intents/CropHarvestIntent.cs`, `Simulation/PlanterRules.cs` | `Intents/RejectReason.cs`, `Simulation/IntentValidator.cs`, `Simulation/ServerSimulation.cs`, `Simulation/GameRules.cs` |
| Core format | — | `Serialization/CoreCodecPrimitives.cs`, `Serialization/IntentCodec.cs`, `Protocol/ProtocolConstants.cs`, `Simulation/OperationApplier.cs`, `Hashing/StateHasher.cs` |
| Bloki | — | `World/Blocks/BlockFunctionKind.cs`, `World/Blocks/BlockFunction.cs`, `src/Horyzont.Constructs/Content/BlockTypeCatalog.cs` (ścieżka niezweryfikowana, A0) |
| Serwer | `src/Horyzont.Server/Crops/CropMonitor.cs`, `CropLog.cs` (`[LoggerMessage]`) | — |
| Klient | `src/Horyzont.Client/Crops/CropView.cs` | `src/Horyzont.Client/Blocks/BlockInteraction.cs`, `BlockPromptKind` |
| Godot | `game/src/Boot/GameComposition.Crops.cs`, `game/src/Client/Crops/CropPresentation.cs` | `game/src/Client/Blocks/BlockPromptHud.cs`, ładowarka treści przetrwania w `game/` (nazwa niezweryfikowana, A0) |
| Dane | `game/content/crops/crops.json` | `items/items.json`, `blocks/blocks.json`, `props/props.json` |
| Lokalizacja | — | `game/locale/items.csv`, `blocks.csv`, `ui.csv` |

## Zasady dla wykonawcy (Gemini 3.8 Flash)

1. Rób wyłącznie krok, który masz przydzielony, i tylko w plikach z jego listy. Nowy plik spoza listy = pytanie w PR.
2. Każdy krok ma wzorzec. Kopiuj strukturę wzorca, zmieniaj tylko nazwy i reguły z tego planu: `GeneratorRules` → `PlanterRules`; `PropRegrowth` → `CropMonitor`; `HazardRules` → `CropRules`; rodzaj 5 w kodeku, applierze, skrócie i `BlockEntityStore.Generators` → rodzaj 6 i `Planters`; `EatIntent` w kodeku i `SurvivalRules` → `PlantIntent`.
3. Nie zmieniaj: złotych bajtów, przypiętych skrótów, `tests/Horyzont.Core.Tests/Golden/`, `TerrainJournalGoldenCorpusTests.cs`, `DeterminismTests.cs`, `Props/`, migracji, numerów spoza tabeli „Numery”. Test, który wymaga takiej zmiany, zatrzymuje krok — eskalacja.
4. W Core: bez `Dictionary`/`HashSet` przy iteracji wpływającej na wynik, bez LINQ i lambd w `PlanterEnvironment` i `CropMonitor`, bez `float` w akumulacji wzrostu, bez `DateTime`/`Stopwatch`.
5. Test odmowy zaczyna od przyjęcia poprawnego odpowiednika (kontrola), potem zmienia jeden parametr. Test kodeka: najpierw poprawny round-trip, potem błąd.
6. Po dwóch nieudanych podejściach do tego samego testu: zatrzymaj się, przekaż Sonnet 5.5 test, log i opis prób (ZASADY §12).
7. Nie uruchamiaj gry z oknem. `dotnet test game` i `build.py smoke` tylko, gdy samohostowany runner CI jest wolny.
8. Commity: Conventional Commits po angielsku, jeden krok = co najmniej jeden commit.

## Testy akceptacyjne (napisz najpierw)

Pisze Sonnet 5.5. Każdy test odmowy ma kontrolę przyjęcia; każdy test właściwości liczy wykonane akcje i wymaga ich niezerowej liczby.

| Test | Projekt | Kryterium |
| --- | --- | --- |
| `CropGrowthTests.Progress_OneTickBeforeRequired_NotRipe` / `_AtRequired_Ripe` | Core | granica dojrzałości |
| `CropGrowthTests.Progress_NotGrowing_StaysAtSettled` | Core | brak światła albo powietrza zatrzymuje wzrost, zachowuje postęp |
| `CropGrowthTests.Progress_NowBeforeSettled_AddsNothing`, `_MaxValues_NoOverflow` | Core | brak ujemnego czasu i przepełnienia |
| `CropGrowthProperties.SettleAtAnyPoint_SameProgress` (FsCheck) | Core | `Progress(Settle(p, t, p.Conditions), u) == Progress(p, u)` dla `t ≤ u` |
| `PlanterConditionsTests.IsGrowing_AllThirtyTwoCombinations` | Core | tablica prawdy D4 |
| `PlanterEnvironmentTests.OpenSkyAtNoon_SkyAndDaylight` + `BlockAbove_NoSky` (ta sama scena z blokiem) + `TerrainOverhang_NoSky` | Core | promień nieba (budowla i teren) |
| `PlanterEnvironmentTests.Midnight_NoDaylight` | Core | pora dnia ze `StarSystemMath` |
| `PlanterEnvironmentTests.GrowLightOn_InFootprint` / `_Off` / `_FiveAbove` / `_DiagonalTwoAway` / `_OtherConstruct` / `_PlainLamp` | Core | D8 (kontrola: pierwszy przypadek ustawia bit) |
| `PlanterEnvironmentTests.VacuumProfile_NoAtmosphere`, `RunningGenerator_Covers`, `BurnedOutGenerator_DoesNot` | Core | D7 pkt 6–7 przez `OxygenCoverage` |
| `PlanterEnvironmentTests.TerrainNotLoaded_KeepsPreviousSky` (oba stany `previous`) | Core | D7 pkt 5 |
| `PlanterEnvironmentTests.TiltedConstruct45_OwnCellNotHit` | Core | przesunięcie 0,9 m |
| `PlanterEnvironmentTests.Evaluate_DoesNotAllocate` | Core | ZASADY §6 |
| `PlanterCodecTests.Empty_GoldenBytes`, `Planted_GoldenBytes`, `RoundTrip_Op40_Blob21_Msg46` | Core | format A1 |
| `PlanterCodecTests.Rejects_Flags2_Conditions0x20_Required0_RequiredOverMax_GrowthOverRequired_PlantedAfterSettled_Seed0` | Core | `ProtocolViolationException` |
| `PlanterCodecTests.ExistingKinds_GoldenBytesUnchanged` | Core | rodzaje 1, 2, 3, 5 |
| `CropIntentCodecTests.Plant_GoldenBytes`, `Harvest_GoldenBytes`, `RoundTrip`, `UnknownVersion_Rejected`, `Truncated_Rejected` | Core | format intencji |
| `ProtocolContractTests` — wartość PV i `NotGrown = 50` | Core | aktualizacja przy scaleniu |
| `PlanterHashTests.EachField_ChangesHash` (6 pól + pusta/obsadzona) | Core | D15 |
| `PlanterHashTests.WorldWithoutPlanters_HashUnchanged` | Core | pin skrótu fikstury z chest/station/generator zapisany w A0 |
| `PlanterApplierTests.InvalidInvariant_Fails`, `KindChange_Fails`, `Remove_RestoresOnUndo` | Core | D14 applier |
| `PlantIntentTests.Success_ConsumesOneSeedAndPlants` | Core | 1 nasiono mniej, stan z `PlantedAt = now`, warunki z oceny |
| `PlantIntentTests.Occupied_CellOccupied`, `EmptySlot_NotEnoughItems`, `NotASeed_InvalidParameters`, `ChestSlot_InvalidParameters`, `NotAPlanter_InvalidParameters`, `TooFar_OutOfReach`, `OtherPlanet_WrongPlanet` | Core | każda z kontrolą; po odmowie przedmioty, stan i token bez zmian |
| `PlantIntentTests.Burst_RateLimited` | Core | 41. intencja w jednym ticku |
| `CropHarvestTests.Immature_NotGrown` (kontrola: ta sama grządka dojrzała → przyjęta), `Empty_NotGrown` | Core | odmowa 50 |
| `CropHarvestTests.Ripe_YieldsAndSeeds_EmptiesPlanter`, `TwiceSameTick_SecondNotGrown` | Core | D11 |
| `CropHarvestTests.FullInventory_InventoryFull_PlantKept`, `PartialFit_InventoryFull_NothingCreated` | Core | brak częściowego wyniku |
| `CropHarvestTests.NewItemType_DiscoveredAfter` | Core | operacja 43 po zbiorze |
| `CropHarvestTests.RetiredCrop_YieldsOneSeed` | Core | wynik zastępczy D11 |
| `CropCompoundTests.FailingLaterComponent_RestoresSlotsStateVersionsMarks` | Core | cofanie całości |
| `CropConservationProperties.RandomPlantHarvestSettleRemove_ConservesItems` | Core | bilans po każdej operacji; liczniki: ≥1 sadzenie, zbiór, odmowa, rozliczenie, rozbiórka |
| `CropReplayTests.ChangedContentAndThrowingTerrain_ReplayIdentical` | Core | powtórka z inną treścią i terenem rzucającym przy każdym wywołaniu |
| `PlanterRulesTests.Place_StateBeforeBlock`, `Remove_StateThenClear`, `RemovePlanted_PlantLost_Conserved` | Core | D14 |
| `CropContentTests.*` | Core | każda reguła walidacji D13 z kontrolą; `HasSource` przez plon |
| `PropContentTests` — krzew daje nasiono | Core | D12 |
| `CropMonitorTests.SamplesPerTick_AtMostBudget`, `EmptyAndRipe_NotSampled`, `NoChange_NoOperation`, `Change_OneOperationPerConstruct`, `ReadOnly_NoOperation`, `Cursor_Wraps` | Server | D6 |
| `CropMonitorTests.DayNightAndLamp_MatchTickReferenceWithinLag` | Server | wzrost z monitora vs model tick po ticku; różnica ≤ opóźnienie × liczba przejść; scenariusz musi mieć ≥ 4 przejścia |
| `CropMonitorTests.Sampling_DoesNotAllocate` (bez zmian warunków) | Server | ZASADY §6 |
| `CropPersistenceTests.CheckpointAndTail_SameStateAndHash`, `CrashBetweenPlantAndCheckpoint_Replays` | Persistence | zapis |
| `CropFlowTests.BushSeedPlantGrowHarvestReplant`, `TwoClients_SeeSameState`, `LateJoin_SeesPlant` | Integration | pełna pętla |
| `BlockInteractionPlanterTests.*` (podpowiedzi, wysyłana intencja, brak zmian lokalnych, odmowa 50) | Client | D16 |
| `CropViewTests.Stage_FromServerTick` | Client | etapy 0–3 |
| `CropPresentationTests` (gdUnit, bez okna) | game | grządka pusta, 3 etapy, usunięcie |

## Kroki

W każdym kroku: najpierw uruchom wskazane testy i zapisz wynik czerwony, potem kod, potem zielone testy i commit.

### Etap A — model, format stanu, skrót (Core)

- [ ] **A0. Baza i fakty (Sonnet 5.5).** Zapisz commit bazy; potwierdź: PV na `main`, obecność API z Kontekstu, bramkę K6-35, ścieżkę kosztów K6-36, ostatnią wartość `BlockFunctionKind`, miejsce wywołania `GeneratorRules` w `ServerSimulation`, ścieżkę `BlockTypeCatalog`, ładowarkę treści w `game/`, źródło wybranego slotu paska u klienta, kodowanie `WriteVarInt`. Przypnij skrót fikstury bez grządek. Komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~ProtocolContract|FullyQualifiedName~Determinism|FullyQualifiedName~TerrainJournalGoldenCorpus"`. Brak K6-34 w `main` = stop pakietu.
- [ ] **A1. Testy A i format — bramka Opus (Sonnet pisze).** Pliki: `CropGrowthTests`, `CropGrowthProperties`, `PlanterConditionsTests`, `PlanterCodecTests`, `PlanterHashTests`, `PlanterApplierTests` + szkielety typów (bez logiki), aby testy się kompilowały. Opus zatwierdza bajty, niezmienniki, rozmiar 46, `default: throw` w skrócie. Komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~CropGrowth|FullyQualifiedName~PlanterConditions|FullyQualifiedName~PlanterCodec|FullyQualifiedName~PlanterHash|FullyQualifiedName~PlanterApplier"` — oczekiwane czerwone.
- [ ] **A2. Model i wzrost (Flash).** `CropPlanting`, `PlanterConditions`, `PlanterBlockState`, `CropGrowth` według D3–D5. Komenda: filtr `CropGrowth|PlanterConditions`. Commit `feat(core): add the planter state and the pure crop growth`.
- [ ] **A3. Kodek, applier, skrót, indeks (Flash).** Wzorzec: rodzaj 5 w `CoreCodecPrimitives.cs:337–350, 384–392`, `OperationApplier.cs:909–917`, `StateHasher.cs:251–264`, `BlockEntityStore.IndexGenerator`. Dodaj `MaxCropGrowthTicks`. Komenda: filtr `PlanterCodec|PlanterHash|PlanterApplier`, potem `Determinism|TerrainJournalGoldenCorpus` (bez zmian w plikach). Commit `feat(core): encode, apply and hash planter states`.
- [ ] **A4. Bramka Opus A.** Diff Core etapu A; pusty diff korpusu i `DeterminismTests`. Komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~Crop|FullyQualifiedName~Planter|FullyQualifiedName~Determinism|FullyQualifiedName~TerrainJournalGoldenCorpus"`.

### Etap B — treść, intencje, środowisko, reguły

- [ ] **B1. Testy B — bramka Opus (Sonnet pisze).** `CropIntentCodecTests`, `PlantIntentTests`, `CropHarvestTests`, `CropCompoundTests`, `CropConservationProperties`, `CropReplayTests`, `PlanterRulesTests`, `CropContentTests`, `PlanterEnvironmentTests`; szkielety. Opus sprawdza bajty intencji (w tym ujemną komórkę), kolejność odmów, kontrole, liczniki właściwości. Oczekiwane czerwone.
- [ ] **B2. Intencje i protokół (Flash).** `PlantIntent`, `CropHarvestIntent`, `IntentCode.Plant = 31`, `CropHarvest = 32`, `RejectReason.NotGrown = 50`, `ProtocolVersion` + 1; dopisz obie intencje do list w `IntentValidator` l. 81, 118 i `BucketFor` l. 132. Wzorzec: `EatIntent`, `BlockInteractIntent`. Komenda: filtr `CropIntentCodec|ProtocolContract`. Commit `feat(core): add the plant and crop harvest intents`.
- [ ] **B3. Treść (Flash).** `CropDef`, `CropContent`, `ContentLoader` (walidacja D13, `HasSource`), `SurvivalContent.Crops`, `BlockFunctionKind.Planter`, `BlockFunction.Planter`, `BlockTypeCatalog`, pliki JSON D12–D13, `CropRules`, wartości `GameRules`. Komenda: `dotnet test Horyzont.sln --filter "FullyQualifiedName~CropContent|FullyQualifiedName~PropContent|FullyQualifiedName~BlockTypeCatalog"`. Commit `feat(content): add crops, seeds, the planter and the grow light`.
- [ ] **B4. Ocena środowiska (Sonnet 5.5 — osąd geometrii).** `PlanterEnvironment` według D7–D8. Komenda: filtr `PlanterEnvironment`. Commit `feat(core): evaluate the light and the air of a planter`.
- [ ] **B5. Reguły grządki (Flash).** `PlanterRules` (D10, D11, D14) wzorem `GeneratorRules` i `PropRules.cs:139–153`; wywołanie w `ServerSimulation` obok `GeneratorRules`; `CheckBlockTarget` dla celu; ocena przy sadzeniu przez `PlanterEnvironment`. Komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~PlantIntent|FullyQualifiedName~CropHarvest|FullyQualifiedName~CropCompound|FullyQualifiedName~CropConservation|FullyQualifiedName~CropReplay|FullyQualifiedName~PlanterRules"`. Commit `feat(core): plant, harvest, place and remove planters`.
- [ ] **B6. Bramka Opus B.** Cały diff Core B; `rg -n "\.Vitals = " src` — liczba wystąpień jak w bazie (D17); brak kolizji numerów z `main`.

### Etap C — serwer, zapis, replikacja

- [ ] **C1. Testy C (Sonnet).** `CropMonitorTests`, `CropPersistenceTests`, `CropFlowTests`. Oczekiwane czerwone.
- [ ] **C2. Monitor (Flash).** `CropMonitor` + `CropLog` wzorem `PropRegrowth` (D6); rejestracja w `GameComposition.Crops.cs` tak jak `PropRegrowth` w `GameComposition.Props.cs` (faza i kolejność zapisuje A0). Komenda: `dotnet test tests/Horyzont.Server.Tests --filter "FullyQualifiedName~CropMonitor"`. Commit `feat(server): sample planter conditions and settle growth`.
- [ ] **C3. Zapis (Flash).** Oczekiwane: bez zmian kodu (wspólny prymityw i `block_entities`). Zmieniaj tylko, gdy test wykaże brak. Komenda: `dotnet test tests/Horyzont.Persistence.Tests --filter "FullyQualifiedName~Crop|FullyQualifiedName~BlockEntit"`.
- [ ] **C4. Replikacja (Flash).** `BlockEntityReplicator` tylko, gdy test wykaże brak. Komenda: `dotnet test tests/Horyzont.Integration.Tests --filter "FullyQualifiedName~CropFlow"`.
- [ ] **C5. Bramka Opus C.** Budżet monitora, brak alokacji, tryb tylko do odczytu, właściwość odniesienia; sprawdzenie końca etapu (z `dotnet test game` i `smoke` według reguły runnera).

### Etap D — klient, prezentacja, odbiór

- [ ] **D1. Testy klienta (Sonnet).** `BlockInteractionPlanterTests`, `CropViewTests`, `CropPresentationTests`. Oczekiwane czerwone.
- [ ] **D2. Interakcja (Flash).** `BlockInteraction` (przypadek `Planter`, D16), `BlockPromptKind`, `CropView`. Wzorzec: przypadki `OxygenGenerator` i `Light`. Komenda: `dotnet test tests/Horyzont.Client.Tests --filter "FullyQualifiedName~Planter|FullyQualifiedName~CropView|FullyQualifiedName~BlockInteraction"`. Commit `feat(client): plant and harvest from the interaction key`.
- [ ] **D3. Prezentacja i tłumaczenia (Flash).** `CropPresentation` (pusta grządka, etapy 1–3, dojrzała), `BlockPromptHud`, klucze PL/EN w `items.csv`, `blocks.csv`, `ui.csv`. Komenda: `dotnet test game` (runner wolny). Wygląd ocenia właściciel na zrzutach z testu gdUnit, nie z uruchomionej gry.
- [ ] **D4. Odbiór.** Pełne sprawdzenie; dopisz do `docs/ZASADY.md` (po zatwierdzeniu Opus) zasadę: „Warunki środowiska grządki ocenia wyłącznie `PlanterEnvironment`; zmianę zapisuje serwer operacją 40 (`CropMonitor`), a wzrost to czysta funkcja `CropGrowth` stanu i ticka (K6-38)”. Opis PR z wynikami testów i odstępstwami; przegląd Sonnet; przegląd Opus końcowego diffu Core.

## Sprawdzenie końca etapu

```powershell
dotnet build Horyzont.sln
dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"
dotnet format Horyzont.sln --verify-no-changes
# etapy C i D, gdy samohostowany runner jest wolny:
dotnet test game
python tools/engine/build.py smoke
```

Plus `git diff <baza A0>..HEAD -- tests/Horyzont.Core.Tests/Golden/ tests/Horyzont.Core.Tests/TerrainJournalGoldenCorpusTests.cs src/Horyzont.Core/Props/ src/Horyzont.Persistence/Migrations/` — pusty; `DeterminismTests` bez zmian w pliku.

## Koordynacja z sąsiednimi pakietami

| Pakiet | Granica |
| --- | --- |
| K6-30 | rodzaj 6 w istniejącym mechanizmie stanów; kolejność stan/blok przy postawieniu i rozbiórce |
| K6-31 | nasiono w urobku krzewu; `PropLayoutV1` bez zmian |
| K6-32 | `ItemPlacement`, `DiscoverAfter`; bez receptur dla grządki |
| K6-34 | konsumpcja `OxygenCoverage`, `HazardField`, `HazardProfileResolver`; żadnych zmian w generatorze ani w `HazardTracker` |
| K6-35 | bramka „martwy/w przejściu” dla 31/32; brak zmian kar |
| K6-36 | koszty grządki i lampy w jego katalogu; zawalenie usuwa stan grządki; `CompoundOp` v2 bez zmian |
| K6-37 | kolejność scalenia decyduje o PV i wartości `BlockFunctionKind.Planter`; rebase przestawia oba numery |
| K6-39 | ekran grządki i ikony przyczyn na danych D4/D16 |
| K6-40 | boty w pętli upraw korzystają z intencji 31/32 |

## Ryzyka

| Ryzyko | Ograniczenie |
| --- | --- |
| Opóźnienie monitora daje roślinie do ~2 s wzrostu w złych warunkach (albo tyle straty) | Ograniczenie opisane w D6; właściwość odniesienia z ograniczeniem błędu |
| Wiele grządek wydłuża okres próbkowania | Budżet na tick; licznik ewaluacji w teście; wartości w `GameRules` |
| Teren niewczytany — fałszywe „niebo otwarte” | `IsAreaLoaded` + zachowanie bitu `SkyOpen` |
| Grządka haszowana tylko kluczem | Test każdego pola; `default: throw` |
| Komunikat 46 za duży | Rachunek w A1; limit na budowlę, jeśli potrzebny |
| Rozjazd numerów z K6-37 | PV i `BlockFunctionKind` ustalane przy scaleniu; test kontraktu aktualizowany wtedy |
| Flash projektuje geometrię | B4 robi Sonnet; Flash kopiuje wzorce |
| Utrata dojrzałej rośliny przy rozbiórce | DO USTALENIA-5 |

## Pułapki

- Kody intencji są dziesiętne: 31 to bajt `0x1F`, nie `0x31`.
- `OperationConstructs.TryGetConstructPart` i `CompoundOp`: w składowych grządki nie ma operacji budowli poza postawieniem/rozbiórką; nie sprawdzaj typu operacji wprost (ZASADY l. 28).
- `BlockEntityStore.TryGet` przez `_lookup` (bez alokacji); nie iteruj `All` w monitorze — tylko `Planters`.
- `HazardField.Daylight` rzuca dla `band ≤ 0`; `HazardRules` to waliduje — używaj instancji z `ServerSimulation`, nie `new HazardRules()`.
- Po zbiorze nie twórz operacji 43 ręcznie — robi to `DiscoverAfter`.
- Monitor nie może wołać `PlanterEnvironment` dla grządki dojrzałej albo pustej (budżet i zbędne operacje).

## DO USTALENIA (właściciel; propozycja w nawiasie)

1. **Słońce:** promień w górę 32 m + pora dnia (noc zatrzymuje wzrost). Alternatywa jak Valheim: tylko otwarte niebo, noc nie przeszkadza (prościej, mniej operacji). *(propozycja: z porą dnia — dosłownie spec l. 165)*
2. **Lampa uprawna:** obszar 3×3, do 4 komórek nad grządką, ta sama budowla, bez energii. *(tak)*
3. **Nawadnianie:** brak w EA. *(tak)*
4. **Pełny plecak przy zbiorze:** odmowa `InventoryFull`, roślina zostaje. *(tak)*
5. **Rozbiórka/zawalenie z rośliną:** roślina przepada. Alternatywa: odmowa rozbiórki obsadzonej grządki. *(przepada)*
6. **Uprawy poza ramą planety (statek w kosmosie):** nie rosną w EA. *(tak; generator działa tylko na planecie — K6-34)*
7. **Wartości demonstracyjne:** `growSeconds` 1200, plon 3 jagody + 2 nasiona, krzew +1 nasiono; koszty `core:planter` i `core:grow_light` w katalogu K6-36. *(do akceptacji)*
8. **Monitor:** okres 120 ticków (2 s), 8 próbek na tick. *(tak)*
9. **Mnożnik surowców świata dla plonów:** nie stosować w K6-38 (ustawienia należą do K6-35/K6-39). *(nie stosować)*
10. **Źródło nasiona:** tylko własny plecak i pasek. *(tak)*
11. **Zajęta grządka:** odmowa `CellOccupied` (14). *(tak)*

## Odbiór (ZASADY §11)

- [ ] Krzew daje nasiono; nasiono sadzi się w grządce (1 sztuka znika atomowo).
- [ ] Wzrost: czysta funkcja `CropGrowth` stanu i ticka; ciemność albo brak powietrza go zatrzymują; lampa uprawna i generator działają.
- [ ] Zbiór niedojrzałej rośliny daje `NotGrown` (50); dojrzałej — plony i nasiona dokładnie raz.
- [ ] Checkpoint + ogon odtwarzają ten sam stan i skrót; powtórka nie pyta terenu ani treści.
- [ ] Monitor mieści się w budżecie; brak alokacji w gorących ścieżkach.
- [ ] Host, klient zdalny i późne dołączenie widzą to samo.
- [ ] Stare formaty, korpus, skróty bez grządek i `PropLayoutV1` bez zmian.
- [ ] Bramki Opus A1, A4, B1, B6, C5 i przegląd końcowy; DO USTALENIA rozstrzygnięte; CI zielone.

## Odstępstwa i uwagi z realizacji

Implementacja nie rozpoczęta; kroki nieodhaczone. Wykonawca dopisuje tu po każdym etapie: commit, wyniki testów, werdykt bramki, rozstrzygnięcia DO USTALENIA.

Odstępstwa od spec proponowane przez plan: gatunek identyfikowany typem nasiona (D3); opóźnienie wykrycia zmiany warunków (D6); brak wzrostu poza planetą (DO USTALENIA-6).

**Niezweryfikowane (sprawdza A0):**

- Ścieżki `src/Horyzont.Constructs/Content/BlockTypeCatalog.cs` i ładowarki treści przetrwania w `game/` (nazwy z planu Codex).
- Kodowanie `ByteWriter.WriteVarInt` (zigzag?) — złote bajty z ujemną komórką.
- Zachowanie `ContainerReach.TryCellCenter` dla budowli w ramie układu gwiezdnego (zakładane `false`).
- Zachowanie `StarSystemMath.SunDirection` przy `cycle = 0` (HazardTracker nie ma ochrony; `SystemMapModel` ma).
- Nazwa i API źródła wybranego slotu paska u klienta (`BlockInteraction` go dziś nie zna).
- Faza ticka i miejsce rejestracji `PropRegrowth` w `GameComposition.Props.cs`.
- Nazwa bramki „martwy/w przejściu” po K6-35 oraz ścieżka kosztów po K6-36 (oba plany poza `main`).
- Dokładny najgorszy rozmiar komunikatu 46 z 4096 grządkami (narzut klucza komórki).
- ~~Zgodność numerów z planem K6-37~~ — uzgodnione 2026-10-17 (sekcja „Uzgodnienie numerów”).
- Wyniki kompilacji i testów — nic nie uruchamiano.
