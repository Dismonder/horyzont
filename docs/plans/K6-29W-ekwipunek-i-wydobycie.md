# K6-29W · Ekwipunek, wydobycie i narzędzia jako przedmioty

| | |
| --- | --- |
| Prowadzi | Opus 5.5 (Claude Code, `claude --model opus`) dla etapów A i B (Core); Sonnet 5.5 dla etapów C i D |
| Testy akceptacyjne | piszący etap, przed kodem; testy etapów A i B przegląda Opus 5.5 przed kodem (ZASADY §3) |
| Przegląd | Opus 5.5 w PR (cały `src/Horyzont.Core/`, migracja, `ITerrainService`), drugi przegląd GPT-6.1 Sol (pakiet Opus, ZASADY §9); interfejs ogląda Dismond przed scaleniem (ryzyko „Złożoność interfejsu ekwipunku” ze spec 03) |
| Zależy od | K2-08…K2-13 i W1 (na gałęzi); spec 03 wymienia K4-19 jako zależność K6-29 — ten wycinek jej nie potrzebuje (patrz Decyzje, D0) |
| Gałąź | `pakiet/k6-29-ekwipunek` (worktree `F:/dev/horyzont-k6-29`) |
| Liczy się do | K6-29 (podzbiór), K6-31 (urobek z kopania), K6-39 (pasek i ekran ekwipunku) |

**Cel:** gracz zaczyna z manipulatorem terenu i narzędziem budowy na pasku szybkiego wyboru, kopanie daje urobek (kamień, ziemia, piasek, ruda) z objętości i danych materiału, zasypywanie zużywa noszony surowiec, a plecak 40 miejsc i pasek 10 miejsc obsługuje się myszą w ciemnym, półprzezroczystym interfejsie w duchu No Man's Sky i Astroneer — a każda zmiana przedmiotów jest atomową operacją dziennika zgodną z formatami spec 03, więc późniejsze pakiety K6 ją rozszerzają, a nie zastępują.

## Kontekst

- `docs/ZASADY.md`, punkty 2, 3, 4, 5, 6, 7, 8, 9.
- `docs/spec/03-przetrwanie.md`: Kontekst i decyzje; Ekwipunek i kontenery; Zbieranie; Architektura i model danych (Przedmioty i treść, Kontenery, Reguły w Core); Operacje i dziennik (obie tabele, tabela akcji, niezmiennik, kody odmów); Sieć; Zapis; Obsługa błędów; Testy; Pakiety pracy (K6-29, K6-31, K6-39).
- `docs/spec/05-tresc-ea.md`: Dane treści (`content/items/`, `content/start.json` ze `StartKitDef`), tabela „Pierwsze wejście gracza” (`ItemCreateOp` ze źródłem `Starter`).
- `docs/plans/K1-03-core-stan-i-operacje.md`: tabela zakresów kodów (operacje 30–49, odmowy 40–59, komunikaty 40–49, intencje 20–39, bloby 20–29 dla Przetrwania).
- `docs/plans/README.md`: reguła D4 (`ProtocolVersion` = wartość na `main` + 1 przy scaleniu).
- `docs/plans/K2-13-narzedzia.md` (ścieżka intencji narzędzi, `ToolController`, `PendingIntentTracker`, sterowanie), `docs/plans/K2-11-postac-i-kamera.md`, `docs/plans/K2-09-adapter-voxel-tools.md` (semantyka edycji, `TerrainReplay`, `TerrainSemanticsMeta`).

Kod, na którym pakiet buduje (stan gałęzi przy pisaniu planu, 2026-10-10):

| Co | Gdzie |
| --- | --- |
| `Operation` (`abstract record`, `private protected`), `OperationKind` 1–7, `AppliedOperation(Id, Tick, Actor, Operation, TerrainVersions, ConstructChanges)` | `src/Horyzont.Core/Operations/` |
| `OperationApplier.CheckPreconditions` / `Apply` (bez treści i terenu przy warunkach) | `src/Horyzont.Core/Simulation/OperationApplier.cs` |
| `ServerSimulation.Submit` / `ApplyServerOperation` / `Replay` (wstępne `LoadAreaBlocking` tylko dla `TerrainEditOp` na najwyższym poziomie), zdarzenie `Applied` | `src/Horyzont.Core/Simulation/ServerSimulation.cs` |
| `IntentValidator` (kolejność sprawdzeń, `BucketFor`), `IntentResolver` (statyczny), `IntentFacts`, `PlayerBuckets`, `GameRules` | `src/Horyzont.Core/Simulation/` |
| `OperationCodec` (bajt rodzaju + wersja formatu 1 na operację), `IntentCodec` (prywatny `IntentCode` 1–3), `MessageCodec`, `StateCodec`, `BlobKind` 1–5 | `src/Horyzont.Core/Serialization/` |
| `ProtocolConstants.ProtocolVersion = 3` (K2-11), `MessageType` 1–20, `RejectReason` 0–21 | `src/Horyzont.Core/Protocol/`, `src/Horyzont.Core/Intents/` |
| `StateHasher` (XxHash64, sekcje: rejestry, planety, budowle, wersje terenu, gracze) | `src/Horyzont.Core/Hashing/StateHasher.cs` |
| `WorldState` (rejestry `BlockTypes`, `Materials`; `ChangeTracker`; `CaptureCheckpoint` / `RequeueAfterFailedCheckpoint`), `WorldStateBuilder`, `WorldCheckpoint` | `src/Horyzont.Core/World/`, `src/Horyzont.Core/Checkpoints/` |
| `ITerrainService` (`IsAreaLoaded`, `LoadAreaBlocking`, `Raycast`, `ApplyEdit`); implementacje: `VoxelTerrainService` (gra), `FakeTerrainService` (testy), `NoTerrain` (`game/tests/Constructs/ConstructTestWorld.cs`) | `src/Horyzont.Core/World/Terrain/ITerrainService.cs` |
| Materiały terenu `core:stone` 0, `core:dirt` 1, `core:sand` 2, `core:ore` 3, `core:bedrock` 4 (indeksy tekstur), `TerrainMaterialMap.TryGetMaterialId` | `game/src/Planet/TerrainMaterials.cs`, `game/src/Planet/TerrainMaterialMap.cs` |
| Migracje: tylko `M0001_Initial`; `MigrationRunner.All`; fikstura `world-v1.db`, `FixtureGenerator` | `src/Horyzont.Persistence/Migrations/`, `tests/Horyzont.Persistence.Tests/` |
| Rejestry rosnące przy wczytaniu (`GrowRegistries`, `WorldLoadOptions.BlockTypeKeys/MaterialKeys`), `Sql.MetaNextEntityId` | `src/Horyzont.Persistence/WorldLoader.cs`, `Sql.cs` |
| Haki sesji `GameServerHooks` (`OnPlayerReady` → obserwatorzy, `OnClientMessage` tylko loguje), `IServerPlayerObserver`, `ServerRuntime.AddPlayerObserver`, `ServerRuntimeOptions` | `src/Horyzont.Server/` |
| `MessageRouter.IsAllowedFromClient` / `IsGameMessageFromServer` / `IsGameMessageFromClient`, `MessageChannels` | `src/Horyzont.Net/Session/`, `src/Horyzont.Core/Protocol/MessageChannels.cs` |
| Narzędzia klienta: `ToolController`, `ToolKind` (1 teren, 2 budowa), `ToolInputFrame.SelectTool`, `MaterialPalette`, `IClientSessionListener` | `src/Horyzont.Client/Tools/`, `src/Horyzont.Client/Session/` |
| Węzły narzędzi, `ToolHud`, `ToolInputReader`; akcje `tool_select_terrain` (1), `tool_select_build` (2), `tool_next`/`tool_prev` (kółko), `tool_material_next` (M) | `game/src/Client/Tools/`, `game/project.godot` |
| Treść bloków `res://content/blocks/blocks.json` (`BlockContentLoader`), lokalizacja CSV `game/locale/*.csv` (`keys,en,pl`) | `game/content/`, `game/locale/` |
| `TerrainReplay` (wczytuje obszar przed `TerrainEditOp`), `TerrainSemanticsMeta.CheckTail` (liczy `TerrainEditOp` w ogonie) | `game/src/Terrain/TerrainReplay.cs`, `src/Horyzont.Persistence/Terrain/TerrainSemanticsMeta.cs` |

## Zakres

1. **Core: przedmioty i kontenery (podzbiór K6-29).** `ItemTypeId`, rejestr `item_types` rosnący przy wczytaniu, `ItemDef`, `ItemStack`, `ItemCatalog` z `ContentLoader` i walidacją, `ContainerId` z alokatorem i `next_container_id`, `Container`, `ContainerStore`, operacje 30–35 (`CompoundOp` z dziennikiem cofania, `ItemTransferOp`, `ItemSwapOp`, `ItemCreateOp`, `ItemDestroyOp`, `ContainerCreateOp`), ich kodeki w wersji formatu 1, skrót stanu, rekordy checkpointu i blob `Container` (20).
2. **Core: intencje, urobek, protokół.** `ItemMoveIntent` (20) i `ItemDropIntent` (21) z walidacją i odmowami 40–43, kubełek intencji przedmiotów 20/s z serią 40, urobek z kopania i koszt zasypywania w operacji złożonej (wycinek K6-31), `ITerrainService.MeasureSphere`, komunikaty 40–43 i 49, `ProtocolVersion` 4.
3. **Zapis i serwer.** Migracja `M0002_Survival` ze wszystkimi pięcioma tabelami podprojektu, zapis i odczyt kontenerów oraz rejestru przedmiotów, kontenery i zestaw startowy przy pierwszym wejściu, subskrypcje własnych kontenerów, delty do właściciela, operacje złożone w `TerrainReplay` i `TerrainSemanticsMeta`, pomiar objętości w `VoxelTerrainService`.
4. **Klient (wycinek K6-39).** Logika bez Godota w `src/Horyzont.Client/Inventory/`; w `game/`: pasek 10 miejsc, ekran ekwipunku (Tab albo I), przenoszenie i dzielenie stosów myszą, podpowiedzi, powiadomienia o urobku, narzędzia wybierane slotem paska, styl sci-fi, etykiety PL i EN w CSV.

Poza zakresem:

| Co | Gdzie |
| --- | --- |
| Operacje 36–39 (`ContainerRemoveOp`, `ContainerConfigureOp`, `PersistentEntityCreateOp`, `PersistentEntityRemoveOp`), przedmioty leżące, nagrobki, blob 24, `EntitySpawn` rodzajów 3 i 6, podnoszenie w promieniu 2 m | reszta K6-29 (przedmioty leżące) i K6-35 (nagrobki) |
| Operacja 46 `WorldSettingsSetOp`, komunikat 47, blob 23 | reszta K6-29 |
| `PlayerProgress` (blob 22), komunikat 48, odkrycia (43), klucze postępu (45, blob 25) | reszta K6-29, K6-32, K6-33 |
| Wyposażenie w działaniu (zakładanie, większy plecak), moduły kombinezonu | K6-33, K6-34 (kontener `Equipment` powstaje już teraz, patrz D6) |
| Skrzynie, subskrypcja cudzych kontenerów z odległości 6/8 m, szybkie odkładanie | K6-30 |
| Obiekty generatora, `PropHitIntent`, poziomy narzędzi, `ToolTierTooLow` | K6-31 (reszta) |
| Rzemiosło, receptury, koszt bloków budowli z ekwipunku | K6-32, K6-36 |
| Podgląd przeniesienia u klienta przed odpowiedzią serwera | K6-39 (D15) |
| Rzut operacji złożonej w `OperationApplied` dla zdalnych klientów | K3-15 (dziś `OperationApplied` nie jest rozsyłany; reguła zapisana w Pułapkach) |
| `AllowCollapse` w `TerrainToolIntent` i `RemoveBlockIntent` | K6-36 |
| Ulepszenie modelu postaci w stylu No Man's Sky | osobny pakiet wizualny (W2), poza tym planem |

## Decyzje

Każda decyzja rozstrzyga miejsce, w którym specyfikacja milczy albo pakiet świadomie bierze jej wycinek. Odstępstwa od spec 03 są oznaczone **[odstępstwo]** i trafiają do opisu PR.

- **D0. Kolejność pakietów.** Właściciel zlecił pakiet 2026-10-10 przed K3–K5 i K4-19. Wycinek nie korzysta z niczego z K4-19 (ani z K3-15/K3-16): dotyczy wyłącznie własnych kontenerów gracza na hoście i u klienta przez sesję K1-07.
- **D1. Numer migracji.** Spec 03 planuje M0003 (M0002 dla Kosmosu). Na tej gałęzi jedyną migracją jest `M0001_Initial`, a `MigrationRunner` wymaga numerów ciągłych, więc migracja ma numer **2** (`M0002_Survival`). Przy scaleniu do `main` dostaje następny wolny numer (ZASADY §3); fikstura `world-v2.db` powstaje z tej migracji i przy zmianie numeru generuje się ją ponownie.
- **D2. `ProtocolVersion` = 4** (`main` ma 3 z K2-11; reguła D4). Test wartości aktualizowany; rebase aktualizuje stałą i test.
- **D3. Kody w tym pakiecie.** Operacje 30–35; intencje 20 i 21; odmowy `InventoryFull` 40, `NotEnoughItems` 41, `ContainerChanged` 42, `ContainerLocked` 43; komunikaty 40, 41, 42, 43, 49; blob 20. Pozostałe numery z tabel spec 03 zostają zarezerwowane i niezdefiniowane (kodeki odrzucają je jako nieznane).
- **D4. `CompoundOp` w formacie 1.** Do 4096 składowych i 160 KiB po zakodowaniu (`ProtocolConstants.MaxCompoundComponents`, `MaxCompoundBytes`), bez zagnieżdżania. Dozwolone składowe w tym formacie: 31–35 oraz co najwyżej jeden `TerrainEditOp`, który musi być **ostatni** (edycji terenu nie da się cofnąć dziennikiem cofania, a jej warunki wstępne nie zależą od przedmiotów). Kolejne pakiety dopuszczają kolejne rodzaje składowych; stare dzienniki ich nie zawierają, więc powtórka się nie zmienia. Kolejność w spec („`TerrainEditOp`, `ItemCreateOp`”) jest listą składowych akcji, nie kolejnością stosowania.
- **D5. Stosowanie złożonej: jedno przejście z cofaniem.** `OperationApplier` dostaje `TryApply(id, tick, actor, op, out applied, out failure, out componentIndex)`: składowe przedmiotów stosuje po kolei na żywym stanie z `UndoLog`; przy niespełnionym warunku cofa wszystko i zwraca błąd z numerem składowej; składowa terenu sprawdza warunek przed zastosowaniem i jest stosowana na końcu. Odrzucone: „próbne zastosowanie w `CheckPreconditions` i drugie w `Apply`” (podwójna praca i ryzyko rozjazdu dwóch ścieżek). `ServerSimulation` używa `TryApply` we wszystkich trzech ścieżkach; nieudana złożona w `Submit` po walidacji to `InvalidOperationException` bez przejścia w stan `Faulted` (stan cofnięty), w `Replay` to `ReplayFailedException` z nowym polem `ComponentIndex`.
- **D6. Kontenery gracza nie powstają w `PlayerJoinOp`.** Zmiana semantyki `PlayerJoinOp` zmieniłaby powtórkę starych dzienników i korpus złoty. Serwer (`InventoryService`, etap C) przy `OnPlayerReady` gracza bez kontenerów stosuje jedną operację złożoną: `ContainerCreateOp` × 3 (`PlayerBag` 40, `Hotbar` 10, `Equipment` 7, gospodarz `Player`, właściciel gracz, dostęp `Private`) i `ItemCreateOp` ze źródłem `Starter` dla każdej pozycji zestawu startowego. To spełnia spec („przy pierwszym wejściu, także w świecie po migracji”). `Equipment` istnieje, ale intencje do niego zwracają `InvalidParameters` do K6-33/K6-34.
- **D7. Skrót stanu bez zmian dla świata bez przedmiotów.** Sekcja Przetrwania (znacznik bajtowy `0x53`, rejestr `item_types`, `next_container_id`, kontenery po identyfikatorze ze slotami) jest dopisywana do strumienia tylko wtedy, gdy rejestr przedmiotów lub magazyn kontenerów nie jest pusty albo `next_container_id ≠ 1`. Złote skróty `DeterminismTests` i korpus `TerrainJournalGoldenCorpusTests` (światy z `WorldFactory` bez przedmiotów) zostają bez żadnej zmiany w plikach; w PR opis „skróty światów z przedmiotami są nowe” (ZASADY §3, Hashing).
- **D8. Reguły Przetrwania włącza treść.** `ServerSimulation` dostaje opcjonalny `SurvivalContent` (katalog przedmiotów, wydajności, zestaw startowy). Bez niego zachowanie jest dokładnie jak w K2 (kopanie bez urobku, zasypywanie bez kosztu, intencje przedmiotów `InvalidParameters`), więc istniejące testy K1/K2 i `ToolRig` działają bez zmian. Gra zawsze przekazuje treść. Odrzucone: przełącznik w `GameRules` (reguły nie są treścią) i ustawienie świata (operacja 46 poza zakresem).
- **D9. Pomiar objętości: `ITerrainService.MeasureSphere`.** Nowa metoda: `bool MeasureSphere(PlanetId planet, Vector3 center, float radius, Span<int> solidVoxelsByMaterial, out int airVoxels)` (rozmiar `Span` = 256, indeks = `MaterialId` świata, 0 nieużywane). Liczy woksele LOD 0, których środek leży w kuli: stałe (SDF < 0) według materiału i puste. Woła ją wyłącznie resolver na wątku głównym; applier i powtórka nigdy (ZASADY §4). Wynik nie musi być deterministyczny między uruchomieniami, bo trafia do operacji. Zmiana interfejsu w Core bez podbicia wersji (nie jest typem zapisywanym), z przeglądem Opus; implementują ją `VoxelTerrainService`, `FakeTerrainService` i `NoTerrain`.
- **D10. Urobek.** Dla każdego materiału z `terrain/yields.json`: `ilość = stałe_woksele × objętość_woksela × itemsPerCubicMeter + reszta[gracz, materiał]`; operacja dostaje część całkowitą, reszta ułamkowa zostaje w `YieldLedger` (pamięć serwera, gubiona przy restarcie — zgodnie ze spec). Materiał bez wpisu (`core:bedrock`) nic nie daje. Rozmieszczenie: najpierw dopełnienie istniejących stosów (pasek, potem plecak, sloty rosnąco), potem puste sloty plecaka, potem puste sloty paska; limit stosu z `ItemDef` zapisany w każdej `ItemCreateOp` (ZASADY §4). Brak całkowitego urobku = zwykły `TerrainEditOp` jak w K2 (bez złożonej).
- **D11. Przepełnienie urobku przepada. [odstępstwo]** Spec 03 każe nadmiarowi lądować jako przedmiot leżący, a przedmioty leżące (38, 39, blob 24) są poza zakresem. Kopanie nie jest blokowane (zgodnie ze spec i z No Man's Sky), nadmiar nie powstaje (niezmiennik zachowania spełniony, bo nic nie zostało utworzone), a klient pokazuje ostrzeżenie „Ekwipunek pełny”. Pakiet z przedmiotami leżącymi zmienia tylko resolver (nowe składowe 35/38), bez zmiany formatu. Pytanie do właściciela w PR.
- **D12. Zasypywanie zużywa surowiec (styl Astroneer).** Spec nie wyklucza kosztu, a ujście `BuildCost` istnieje w formacie 34. Koszt: `potrzeba = (puste_woksele + stałe_woksele_innego_materiału) × objętość_woksela × itemsPerCubicMeter(materiał) − kredyt[gracz, materiał]`, zużywane `ceil(max(0, potrzeba))` sztuk przedmiotu urobku tego materiału, nadpłata wraca do kredytu w `YieldLedger`. Zapis materiału obejmuje całą sferę edycji, więc płatne jest też przemalowanie stałych wokseli innego materiału (inaczej zasypanie wnętrza skały za darmo zmieniałoby kamień w bogatszy materiał); woksele już z tego materiału są darmowe. Pobranie: plecak, potem pasek, sloty rosnąco. Za mało sztuk albo materiał bez przedmiotu → `NotEnoughItems` (41), teren bez zmian. Operacja: `CompoundOp[ItemDestroyOp(BuildCost)…, TerrainEditOp]`.
- **D13. `ItemDropIntent` (21) tymczasowo niszczy. [odstępstwo]** Format intencji jak w spec (kontener, slot, liczba); rozwiązanie w tym pakiecie: `ItemDestroyOp` z ujściem `Despawn` (UI: „Wyrzuć — przepada”, z potwierdzeniem). Przedmiotów z flagą `Tool` nie da się wyrzucić (`InvalidParameters`), bo bez rzemiosła i przedmiotów leżących gracz nie odzyskałby narzędzia. Pakiet przedmiotów leżących zmienia tylko rozwiązanie intencji; operacje zapisują skutek, więc dziennik się nie zmienia.
- **D14. Bez bramkowania narzędzi na serwerze.** `TerrainToolIntent`, `PlaceBlockIntent` i `RemoveBlockIntent` nie wymagają posiadania narzędzia; wybór narzędzia slotem paska jest logiką klienta. Walidator może to dodać z poziomami narzędzi (K6-31, `ToolTierTooLow`) bez zmiany formatu.
- **D15. Bez podglądu przeniesienia u klienta. [odstępstwo]** Spec (Sieć) każe pokazywać przeniesienie od razu i cofać je przy odmowie. Na hoście i w grze solo odpowiedź przychodzi w następnym ticku; podgląd dla zdalnych klientów dodaje K6-39. Ekran pokazuje slot źródłowy jako „w drodze” do odpowiedzi.
- **D16. Treść.** `ContentLoader` i `ItemCatalog` w Core (`src/Horyzont.Core/Content/`, `System.Text.Json` z BCL). Pliki: `game/content/items/items.json` (`ItemDef`), `game/content/terrain/yields.json` (`TerrainYieldDef`: klucz materiału, klucz przedmiotu, `itemsPerCubicMeter`, `toolTier`), `game/content/start.json` (`StartKitDef`: rozmiary kontenerów gracza i pozycje zestawu: przedmiot, liczba, kontener, slot). Pola wizualne (`visual`: glif, kolor, ikona) parsuje tylko gra; Core je pomija. Walidacja przy starcie: zdublowany klucz, `maxStack` poza 1–999 albo niezgodny z kategorią bez jawnego nadpisania, nieznany przedmiot w wydajnościach lub zestawie, slot poza rozmiarem, przedmiot bez źródła (urobek albo zestaw), usunięty typ zapisany w świecie bez flagi `Retired` (sprawdzane przy wczytaniu, jak bloki).
- **D17. Przedmioty startowe i surowce.** `core:multitool` (Manipulator terenu, `Tool`, stos 1, poziom 1), `core:build_tool` (Narzędzie budowy, `Tool`, stos 1), `core:stone` (Kamień), `core:soil` (Ziemia), `core:sand` (Piasek), `core:ore` (Ruda) — surowce `Bulk`, stos 500. Wydajności startowe: kamień 2/m³, ziemia 3/m³, piasek 3/m³, ruda 1/m³ (dane, strojone bez zmiany kodu). Zestaw: pasek slot 0 manipulator, slot 1 narzędzie budowy; plecak pusty.
- **D18. Sterowanie.** Kółko myszy przewija pasek; `Shift` + kółko zmienia promień (teren) albo typ bloku (budowa) — akcje `tool_next`/`tool_prev` dostają `shift_pressed`; klawisze 1…9, 0 wybierają sloty 1–10 (akcje `hotbar_1`…`hotbar_10`; akcje `tool_select_terrain`/`tool_select_build` znikają); Tab i I otwierają ekran ekwipunku (`inventory_toggle`), który zwalnia mysz i blokuje wejście ruchu, kamery i narzędzi. Slot z przedmiotem `Tool` wybiera narzędzie (`core:multitool` → `ToolKind.TerrainManipulator`, `core:build_tool` → `ToolKind.Builder`, mapowanie w `visual.clientTool`); slot bez narzędzia → nowe `ToolKind.None` (bez podglądu i bez intencji). Wybrany slot to stan klienta, nie świata.
- **D19. Limity i stałe.** `ItemTypeId` = `ushort`, `ProtocolConstants.MaxItemTypes = 4096`; `ContainerId` = `ulong` (varuint); `MaxContainerSlots = 256`; `ItemStack.Count` = `ushort` (1…limit); kubełek przedmiotów `GameRules.ItemIntentsPerSecond = 20`, `ItemIntentBurst = 40`; okno powiadomień o urobku 1,5 s.
- **D20. Subskrypcje.** Własne `PlayerBag`, `Hotbar`, `Equipment` serwer subskrybuje sam przy `OnPlayerReady` i wysyła `ItemRegistry` (49), potem trzy `ContainerState` (40). `ContainerSubscribe` (42) na własny kontener ponownie wysyła pełny stan (ścieżka luki w wersjach); na każdy inny — `ContainerState` z odmową `ContainerLocked` (43) i bez slotów. `ContainerUnsubscribe` (43) na własny kontener jest ignorowany (spec: zawsze subskrybowane).

## Projekt

### Pliki

```
src/Horyzont.Core/
  Ids/ItemTypeId.cs, ContainerId.cs, ContainerIdAllocator.cs
  Content/ItemDef.cs, ItemCategory.cs, ItemFlags.cs, EquipmentSlot.cs, TerrainYieldDef.cs, StartKitDef.cs,
          StartKitEntry.cs, ItemCatalog.cs, SurvivalContent.cs, ContentLoader.cs
  Items/ItemStack.cs, ItemInstanceData.cs, Container.cs, ContainerKind.cs, ContainerAccess.cs, ContainerHost.cs,
        ContainerHostKind.cs, ContainerStore.cs, ItemTypeRegistry.cs, ItemSource.cs, ItemSink.cs, SlotRef.cs
  Operations/CompoundOp.cs, ItemTransferOp.cs, ItemSwapOp.cs, ItemCreateOp.cs, ItemDestroyOp.cs, ContainerCreateOp.cs,
             ContainerChange.cs, OperationTerrain.cs (TryGetTerrainEdit: najwyższy poziom albo ostatnia składowa)
  Operations/OperationKind.cs, AppliedOperation.cs            (zmiana: kody 30–35; pole ContainerChanges)
  Intents/ItemMoveIntent.cs, ItemDropIntent.cs, ItemMoveTarget.cs, RejectReason.cs (40–43)
  Simulation/UndoLog.cs, ItemPlacement.cs (plan rozmieszczenia i pobrania), YieldLedger.cs, MiningYield.cs,
             OperationApplier.cs (TryApply, operacje 30–35), IntentValidator.cs, IntentResolver.cs, IntentFacts.cs,
             PlayerBuckets.cs (Items), GameRules.cs (ItemIntentsPerSecond, ItemIntentBurst), ServerSimulation.cs,
             ReplayFailedException.cs (ComponentIndex)
  World/WorldState.cs (ItemTypes, Containers, ContainerIds), World/Terrain/ITerrainService.cs (MeasureSphere)
  Checkpoints/ContainerRecord.cs, ChangeTracker.cs, WorldCheckpoint.cs, WorldStateBuilder.cs
  Hashing/StateHasher.cs (sekcja Przetrwania, D7)
  Serialization/OperationCodec.cs, IntentCodec.cs, MessageCodec.cs, StateCodec.cs (blob 20), BlobKind.cs (Container = 20),
                CoreCodecPrimitives.cs (ItemStack, SlotRef, ContainerHost)
  Protocol/MessageType.cs (40–43, 49), MessageChannels.cs, ProtocolConstants.cs (4, limity D4/D19),
           Messages/ContainerState.cs, ContainerDelta.cs, ContainerSubscribe.cs, ContainerUnsubscribe.cs, ItemRegistry.cs,
           Messages/SlotContent.cs
src/Horyzont.Persistence/
  Migrations/M0002_Survival.cs, MigrationRunner.cs; Sql.cs (MetaNextContainerId); WorldCreator.cs; WorldLoader.cs
  (item_types, containers, next_container_id); WorldLoadOptions.cs (ItemTypeKeys); Writing/PersistenceWriter.cs
  (upsert/delete containers, next_container_id); Terrain/TerrainSemanticsMeta.cs (OperationTerrain)
src/Horyzont.Net/Session/MessageRouter.cs (42, 43 od klienta w InGame; 40, 41, 49 od serwera)
src/Horyzont.Server/
  Inventory/InventoryService.cs (IServerPlayerObserver + IServerClientMessageHandler), InventoryLog.cs
  IServerClientMessageHandler.cs, ServerRuntime.cs (AddClientMessageHandler), GameServerHooks.cs (przekazanie 42/43),
  ServerRuntimeOptions.cs (Content, ItemTypeKeys), ServerRuntime.Start.cs (SurvivalContent do ServerSimulation)
src/Horyzont.Client/
  Inventory/ClientInventory.cs, ClientContainer.cs, HotbarSelection.cs, InventoryInteraction.cs, HeldStack.cs,
            PickupToastFeed.cs, PickupToast.cs, ItemToolMap.cs, InventoryLog.cs
  Tools/ToolKind.cs (None = 0), Tools/ToolController.cs (None: bez podglądu i intencji)
game/
  content/items/items.json, content/terrain/yields.json, content/start.json
  locale/items.csv (+ .import, .en/.pl.translation), locale/tools.csv (nowe podpowiedzi), project.godot (akcje, tłumaczenia)
  src/Content/SurvivalContentLoader.cs (res:// → ContentLoader), src/Client/Inventory/ItemVisuals.cs, HudTheme.cs,
  HotbarHud.cs, InventoryScreen.cs, SlotView.cs, ItemTooltip.cs, PickupToasts.cs, InventoryInputReader.cs, InventoryNode.cs
  src/Client/Tools/ToolInputReader.cs (Shift+kółko, bez 1/2), ToolNode.cs (narzędzie z paska), ToolHud.cs (licznik surowca)
  src/Terrain/VoxelTerrainService.cs (MeasureSphere), src/Terrain/TerrainReplay.cs (OperationTerrain)
  src/Boot/GameComposition.cs (treść, InventoryService, InventoryNode)
tests/  (nazwy plików w „Testy akceptacyjne”)
```

### Kontrakty Core (format 1)

| Kod | Typ | Pola w kolejności kodeka | Warunki wstępne appliera |
| --- | --- | --- | --- |
| op 30 | `CompoundOp(IReadOnlyList<Operation> Components)` | varuint liczba (1…4096), ramki składowych (`OperationCodec` bez 30) | D4: dozwolone rodzaje, ≤ 1 `TerrainEditOp` i tylko ostatni, bez zagnieżdżenia |
| op 31 | `ItemTransferOp(SlotRef From, SlotRef To, ItemTypeId Type, ushort Count, ushort ToStackLimit)` | `SlotRef` = varuint kontener + varuint slot | oba kontenery istnieją, sloty w rozmiarze, `From ≠ To`; źródło ma `Type` w ≥ `Count`; cel pusty albo `Type`, suma ≤ `ToStackLimit`; `Count ≥ 1` |
| op 32 | `ItemSwapOp(SlotRef A, SlotRef B, ItemTypeId ExpectedA, ItemTypeId ExpectedB)` | `ItemTypeId` 0 = slot pusty | sloty istnieją, różne; typy zgodne z oczekiwanymi; nie oba puste |
| op 33 | `ItemCreateOp(SlotRef Slot, ItemTypeId Type, ushort Count, ushort StackLimit, ItemSource Source)` | `ItemSource`: `Harvest` 1, `Craft` 2, `Station` 3, `Crop` 4, `Refund` 5, `Loot` 6, `Admin` 7, `Recovered` 8, `Starter` 9 | typ w rejestrze; slot pusty albo `Type`; suma ≤ `StackLimit` |
| op 34 | `ItemDestroyOp(SlotRef Slot, ItemTypeId Type, ushort Count, ItemSink Sink)` | `ItemSink`: `Craft` 1, `Station` 2, `Eat` 3, `Fuel` 4, `BuildCost` 5, `SuitRefill` 6, `Plant` 7, `Despawn` 8, `Admin` 9, `Ammo` 10, `Offering` 11 | slot ma `Type` w ≥ `Count` |
| op 35 | `ContainerCreateOp(ContainerId Id, ContainerKind Kind, ContainerHost Host, PlayerId Owner, ContainerAccess Access, ushort Size)` | `ContainerKind`: `PlayerBag` 1, `Hotbar` 2, `Equipment` 3, `Chest` 4, `CargoHold` 5, `StationInput` 6, `StationOutput` 7, `Tombstone` 8, `DroppedItem` 9; `ContainerHost`: bajt rodzaju (`Player` 1: varuint; `ConstructBlock` 2: varuint + `CellPos`; `PersistentEntity` 3: varuint; `Poi` 4: planeta, miejsce, numer — varuinty) | identyfikator nieużyty; rozmiar 1…256; gospodarz `Player` istnieje i nie ma jeszcze kontenera tego rodzaju; inne rodzaje gospodarza w tym pakiecie: błąd „host kind not supported” (stare dzienniki ich nie mają, kolejne pakiety dodają obsługę) |

- Wersja kontenera: rośnie o 1 raz na operację dziennika, która go zmieniła (także na złożoną z wieloma składowymi), na końcu `TryApply`; nowy kontener ma wersję 0 przed tym podbiciem, więc po swojej operacji ma 1.
- `AppliedOperation` dostaje `IReadOnlyList<ContainerChange> ContainerChanges` (`ContainerId`, nowa wersja, posortowane indeksy zmienionych slotów; nowy kontener: wszystkie sloty). `TerrainVersions` złożonej = wersje jej składowej terenu.
- `ItemStack` w kodekach: varuint typ, varuint liczba, bajt instancji (0; inna wartość w formacie 1 = `ProtocolViolationException`). Slot pusty w delcie i stanie: typ 0.
- Blob `Container` (20), wersja 1: id, rodzaj, gospodarz, właściciel, dostęp, rozmiar, wersja, liczba niepustych slotów, (slot, stos)…
- Intencja 20 `ItemMoveIntent(SlotRef From, ItemMoveTarget To, ushort Count, ItemTypeId ExpectedType, ushort ExpectedCount)`; `ItemMoveTarget` = bajt (1 slot: `SlotRef`; 2 dowolny w kontenerze: varuint kontener). Intencja 21 `ItemDropIntent(SlotRef Slot, ushort Count, ItemTypeId ExpectedType, ushort ExpectedCount)` (niezgodna zawartość slotu: `ContainerChanged`).
- Komunikaty: 40 `ContainerState(ContainerId, RejectReason, ContainerKind, ContainerAccess, ushort Size, uint Version, IReadOnlyList<SlotContent> Slots)` (przy odmowie bez pól po `RejectReason`); 41 `ContainerDelta(ContainerId, uint Version, IReadOnlyList<SlotContent> Changed)`; 42 `ContainerSubscribe(ContainerId)`; 43 `ContainerUnsubscribe(ContainerId)`; 49 `ItemRegistry(IReadOnlyList<RegistryEntry> Entries)`. Wszystkie niezawodne. Limit `ContainerState`/`ContainerDelta` ≤ 256 slotów.

### Walidacja i rozwiązanie intencji przedmiotów

Kolejność sprawdzeń (decyduje o zwróconym kodzie): gracz znany (`UnknownPlayer`) → online (`PlayerOffline`) → parametry (`InvalidParameters`: liczby 0, sloty poza rozmiarem, `Equipment`, kontener nie gracza = `ContainerLocked`, brak treści D8) → kubełek `Items` (`RateLimited`) → zawartość źródła równa oczekiwanej (`ContainerChanged`) → `Count ≤ ExpectedCount` (`NotEnoughItems`) → reguła celu:

| Cel | Wynik |
| --- | --- |
| slot pusty | `ItemTransferOp` (`Count` < całość = podział) |
| slot z tym samym typem | `ItemTransferOp` z `min(Count, limit − cel)`; 0 → `InventoryFull` |
| slot z innym typem, `Count` = całe źródło | `ItemSwapOp` |
| slot z innym typem, część źródła | `InvalidParameters` |
| „dowolny” w innym kontenerze gracza | `CompoundOp` przeniesień: najpierw dopełnienie stosów, potem puste sloty rosnąco; nic się nie mieści → `InventoryFull`; częściowo → przenosi, ile wejdzie |
| `ItemDropIntent` | `ItemDestroyOp(Despawn)`; `Tool` → `InvalidParameters` (D13) |

Kopanie i zasypywanie: walidacja K2-13 bez zmian, potem (gdy jest treść) `MeasureSphere`; przy zasypywaniu brak przedmiotów → `NotEnoughItems`. Resolver składa operację wg D10–D12; `YieldLedger` zmienia się dopiero po udanym `TryApply`.

## Testy akceptacyjne (napisz najpierw)

Pomocnicze w `tests/Horyzont.Testing`: `SurvivalContentFactory.CreateTest()` (przedmioty testowe ze stosami 1/5/500, wydajność materiału 1 = 2/m³, zestaw startowy), `WorldFactory.CreateWithPlayerContainers()`, `FakeTerrainService.MeasureResult` (ustawiany wynik pomiaru) i `MeasureCalls` (licznik), `NoTerrain.MeasureSphere` zwraca false.

### Etap A — model, operacje, kodeki, skrót (Core)

| Test | Kryterium |
| --- | --- |
| `Content/ItemCatalogTests`: `Parse_RepoContent_IsValid` (czyta `game/content/items/items.json`, `terrain/yields.json`, `start.json`), `Parse_DuplicateKey_Throws`, `Parse_MaxStackOutOfRange_Throws`, `Parse_YieldUnknownItem_Throws`, `Parse_StartKitSlotOutOfRange_Throws`, `Parse_ItemWithoutSource_Throws` | `InvalidDataException` ze ścieżką JSON |
| `Items/ItemOpsApplierTests`: `Create_EmptySlot_SetsStack_VersionPlusOne`, `Create_OverStackLimit_Fails`, `Create_UnknownType_Fails`, `Transfer_Partial_SplitsStack`, `Transfer_SameType_MergesUpToLimit`, `Transfer_ToOtherType_Fails`, `Swap_UnexpectedType_Fails`, `Destroy_NotEnough_Fails`, `ContainerCreate_UsedId_Fails`, `ContainerCreate_SecondBagForPlayer_Fails`, `ContainerCreate_NonPlayerHost_Fails` | warunki z tabeli kontraktów; błąd nie zmienia stanu (skrót przed = po) |
| `Items/CompoundOpTests`: `FailingLastComponent_RollsBackAll`, `TerrainNotLast_Fails`, `TwoTerrainEdits_Fails`, `Nested_Fails`, `VersionBumpsOncePerContainer`, `TerrainAppliedOnlyAfterItemComponentsHold` (`FakeTerrainService.AppliedEdits` puste przy błędzie składowej), `ContainerCreateThenCreateItems_InOneCompound` | D4, D5 |
| `Items/ItemConservationProperties` (FsCheck, 2000 sekwencji po 50 operacji na 4 kontenerach testowych, także złożone z losowym błędem) | po każdej operacji: dla każdego typu suma sztuk = Σ create − Σ destroy; odrzucona operacja nie zmienia skrótu |
| `Serialization/OperationCodecItemTests`: round-trip 30–35 (wszystkie wartości enumów), `Compound4097Components_Throws`, `CompoundOver160KiB_Throws`, `NestedCompound_Read_Throws`, `InstanceByteNonZero_Throws`, `UnknownSourceSinkKindHost_Throws` | zapis rzuca `ArgumentException`, odczyt `ProtocolViolationException` |
| `Serialization/StateCodecContainerTests`: blob 20 round-trip; zły bajt rodzaju bloba → wyjątek | |
| `Hashing/SurvivalHashTests`: `WorldWithoutItems_HashUnchanged` (skrót `WorldFactory.CreateDefault()` równy wartości zapisanej z commita przed etapem A), `SlotChange_ChangesHash`, `NextContainerId_ChangesHash` | D7 |
| `Checkpoints/ContainerCheckpointTests`: `Capture_ChangedContainer_Included`, `Requeue_MarksContainersAgain`, `Builder_RestoresContainersAndNextContainerId`, `CheckpointPlusCompoundJournal_SameHash` | |
| `Simulation/ReplayCompoundTests`: `Replay_CompoundWithTerrain_LoadsAreaFirst` (`BlockingLoads`), `Replay_BrokenComponent_ReplayFailedWithComponentIndex` | |
| Bez zmian w plikach: `TerrainJournalGoldenCorpusTests`, `DeterminismTests`, wszystkie dotychczasowe testy | `git diff <start etapu>..HEAD` na ścieżkach korpusu i tych testów pusty |

### Etap B — intencje, urobek, protokół (Core)

| Test | Kryterium |
| --- | --- |
| `Simulation/ItemMoveIntentTests`: po jednym teście na każdy wiersz tabeli reguł celu i na każdy kod 40–43 oraz `InvalidParameters` dla `Equipment`; `Burst41_RateLimited` | wynik i kod zgodne z tabelą |
| `Simulation/ItemDropIntentTests`: `Drop_Resource_DestroysWithDespawn`, `Drop_Tool_InvalidParameters` | D13 |
| `Simulation/MiningYieldTests`: `Dig_Stone_CompoundOfHarvestCreatesThenTerrainEdit` (pomiar 3 woksele × 1 m³ × 2 = 6), `Dig_Fraction_AccumulatesAcrossDigs` (0,4 + 0,4 + 0,4 → 0, 0, 1), `Dig_Bedrock_PlainTerrainEdit`, `Dig_MergesIntoHotbarStackFirst`, `Dig_FullInventory_EditAppliedOverflowNotCreated`, `Fill_ConsumesSoilBuildCost`, `Fill_WithoutSoil_NotEnoughItems_TerrainUntouched`, `Fill_Credit_CarriesOverpayment`, `NoContent_BehavesAsK2`, `Replay_DigJournal_NeverMeasures` (`MeasureCalls` = 0 w powtórce) | D8–D12 |
| `Serialization/IntentCodecItemTests`: round-trip 20 i 21, oba warianty celu; nieznany wariant celu → `ProtocolViolationException` | |
| `Serialization/MessageCodecContainerTests`: round-trip 40 (ze slotami i z odmową), 41, 42, 43, 49; 257 slotów → wyjątek | |
| `Protocol/ProtocolVersionTests` | `ProtocolVersion == 4` |
| `Intents/RejectReasonGoldenTests` | wartości 40–43 |
| `Net.Tests/MessageRouterTests` (dopisane przypadki) | 42/43 dozwolone od klienta w `InGame`, nie w `Loading`; 40/41/49 dozwolone od serwera |

### Etap C — zapis i serwer

| Test | Kryterium |
| --- | --- |
| `Persistence.Tests/MigrationTests.FixtureWorldV1_MigratesToV2_CreatesSurvivalTables` | pięć tabel, `user_version` 2, kopia przed migracją |
| `Persistence.Tests/MigrationTests.FixtureWorldV2_CopiedToTemp_LoadsAndReplays` (nowa fikstura z `FixtureGenerator`) | |
| `Persistence.Tests/ContainerPersistenceTests`: `Checkpoint_WritesContainersAndNextId_LoadRestores`, `ItemTypes_GrowOnLoad_KeepIds`, `CorruptContainerBlob_WorldCorrupted` | |
| `Persistence.Tests/TerrainSemanticsMetaTests.CheckTail_CompoundWithTerrain_CountsEdit` | |
| `Server.Tests/Inventory/InventoryServiceTests`: `FirstReady_CreatesThreeContainersAndStarterKit_OneJournalEntry`, `SecondReady_NoNewContainers`, `Ready_SendsItemRegistryThenThreeStates`, `AppliedDig_SendsDeltaToOwnerOnly`, `SubscribeOwn_ResendsState`, `SubscribeForeign_ContainerLocked`, `ReadOnly_CreatesNothing_SendsExisting` | D6, D20 |
| `Integration.Tests/Inventory/InventoryFlowTests` (klient → `InMemoryTransport` → sesja → symulacja): `Dig_ClientSeesStoneInDelta`, `MoveSplitMerge_EndToEnd`, `Restart_ItemsIdenticalAndHashEqual`, `CrashMidCompound_WholeOrNothing` (wstrzyknięty błąd zapisu, wzór K1-05) | |
| `game/tests/Terrain/MeasureSphereTests` (gdUnit4, prawdziwy `VoxelLodTerrain`) | płaski kamień: liczba stałych wokseli w kuli 1 m = liczba środków w kuli pod powierzchnią ± 0; powietrze nad powierzchnią liczone jako puste |
| `game/tests/Terrain/TerrainReplayCompoundTests` | obszar złożonej z kopaniem wczytany przed powtórką |

### Etap D — klient

| Test | Kryterium |
| --- | --- |
| `Client.Tests/Inventory/ClientInventoryTests`: `State_ReplacesContainer`, `Delta_NextVersion_Applies`, `Delta_Gap_RequestsResubscribe_IgnoresDelta`, `Delta_Old_Ignored`, `RejectedState_NotStored` | spec Sieć: delta tylko do wersji +1 |
| `Client.Tests/Inventory/HotbarSelectionTests`: `Wheel_WrapsAround`, `Keys1To0_SelectSlots`, `SelectedItem_ChangesWithDelta` | |
| `Client.Tests/Inventory/InventoryInteractionTests`: `LeftClick_PickWhole_PlaceEmpty_Transfer`, `RightClick_PickHalf_Split`, `Place_OnSameType_Merge`, `Place_OnOtherType_Swap`, `ShiftClick_MoveAny`, `Discard_ResourceAsksConfirm_ThenDropIntent`, `Discard_Tool_Disabled`, `PendingSlot_BlocksSecondPick` | każda intencja niesie oczekiwany typ i liczbę źródła |
| `Client.Tests/Inventory/PickupToastFeedTests`: `IncreasesWithin1500ms_Aggregate` („+12 Kamień”), `InventoryFullAfterAcceptedDig_Warns` | |
| `Client.Tests/Tools/ToolFromHotbarTests`: `MultitoolSlot_SelectsTerrain`, `BuildToolSlot_SelectsBuilder`, `ResourceSlot_SelectsNone_SendsNothing` | D18 |
| `game/tests/Inventory/InventoryUiTests` (gdUnit4, bez grafiki): pasek ma 10 slotów i podświetla wybrany; Tab otwiera ekran, zwalnia mysz i blokuje `ToolInputReader`; podpowiedź pokazuje nazwę z CSV | |
| Ręcznie (wynik do PR, kategoria `Manual`) | solo: kopanie daje powiadomienie „+N Kamień”, zasypywanie zużywa ziemię i odmawia bez niej, przenoszenie/dzielenie/zamiana myszą, restart zachowuje przedmioty, zrzuty ekranu paska i ekranu ekwipunku dla Dismonda |

## Kroki

### Etap A — model, operacje, kodeki, skrót (Opus 5.5)

- [x] A0. Zapisz skrót `StateHasher.Hash(WorldFactory.CreateDefault())` z bieżącego commita jako stałą testu `WorldWithoutItems_HashUnchanged`; zanotuj commit startowy etapu do sprawdzenia korpusu. **Zrobione:** commit startowy `eee8f17`, skrót `17523897430694564880` (`SurvivalHashTests.DefaultWorldHashBeforeItems`).
- [x] A1. Testy z tabeli etapu A (czerwone); przegląd testów przez Opus przed kodem.
- [x] A2. Treść: `Content/*`, `ContentLoader`, pliki `game/content/items/items.json`, `terrain/yields.json`, `start.json` (D16, D17).
- [x] A3. Typy: `ItemTypeId`, `ContainerId`, `ContainerIdAllocator`, `ItemStack`, `Container`, `ContainerStore` (indeks gracz × rodzaj), `ItemTypeRegistry`, enumy z pełnymi wartościami spec.
- [x] A4. Operacje 30–35, `UndoLog`, `OperationApplier.TryApply`, `ContainerChanges`, wersje kontenerów; `ServerSimulation` na `TryApply`; `OperationTerrain` i wczytanie obszaru w `Replay`; `ReplayFailedException.ComponentIndex`.
- [x] A5. Kodeki operacji 30–35, blob 20, rekordy checkpointu, `ChangeTracker`, `WorldStateBuilder`, `CaptureCheckpoint`/`Requeue`.
- [x] A6. `StateHasher`: sekcja Przetrwania warunkowo (D7).
- [x] A7. Test właściwości zachowania zielony; korpus i `DeterminismTests` bez zmian w plikach.
- [x] A8. Sprawdzenie (niżej), commit(y): `feat(core): add items, containers and compound operations`.

### Etap B — intencje, urobek, protokół (Opus 5.5)

- [x] B1. Testy z tabeli etapu B (czerwone), przegląd Opus.
- [x] B2. `RejectReason` 40–43, `ItemMoveIntent`, `ItemDropIntent`, `IntentCodec` (kody 20, 21), kubełek `Items` w `PlayerBuckets` i `GameRules`, `AdvanceTick` uzupełnia go.
- [x] B3. Walidacja i rozwiązanie intencji przedmiotów; `ItemPlacement` (rozmieszczenie i pobranie, deterministyczne).
- [x] B4. `ITerrainService.MeasureSphere` (+ `FakeTerrainService`, `NoTerrain`); `MiningYield`, `YieldLedger`; resolver kopania i zasypywania (D9–D12); `SurvivalContent` w `ServerSimulation` (nowy konstruktor, stary deleguje z `null`).
- [x] B5. Komunikaty 40–43, 49, `MessageCodec`, `MessageChannels`, `MessageRouter`; `ProtocolVersion = 4` i jego test.
- [x] B6. Sprawdzenie, commit(y): `feat(core): add item intents and mining yield`, `feat(protocol): add container messages and bump the protocol to 4`.

### Etap C — zapis i serwer (Sonnet 5.5; przegląd Opus migracji i `VoxelTerrainService.MeasureSphere`)

- [x] C1. Testy z tabeli etapu C (czerwone).
- [x] C2. `M0002_Survival` (D1): `item_types (id INTEGER PRIMARY KEY, key TEXT NOT NULL UNIQUE)`, `containers (container_id INTEGER PRIMARY KEY, data BLOB NOT NULL)`, `block_entities (construct_id, cell_x, cell_y, cell_z, data, PRIMARY KEY (construct_id, cell_x, cell_y, cell_z)) WITHOUT ROWID`, `player_progress (player_id INTEGER PRIMARY KEY, data BLOB NOT NULL)`, `harvested_props (planet_id, prop_id, tick, PRIMARY KEY (planet_id, prop_id)) WITHOUT ROWID`; `MigrationRunner.All` += 2; `FixtureGenerator` → `world-v2.db`.
- [x] C3. `WorldCreator`, `WorldLoader` (`item_types` z `GrowRegistries`, kontenery, `next_container_id`), `WorldLoadOptions.ItemTypeKeys`, `PersistenceWriter` (upsert/usuwanie kontenerów, meta), `TerrainSemanticsMeta` przez `OperationTerrain`.
- [x] C4. `Horyzont.Server`: `ServerRuntimeOptions.Content`/`ItemTypeKeys`, `ServerSimulation` z treścią, `IServerClientMessageHandler` + `ServerRuntime.AddClientMessageHandler`, `GameServerHooks.OnClientMessage` przekazuje 42/43, `InventoryService` (D6, D20; delty z `ServerSimulation.Applied`, logi `[LoggerMessage]` z `PlayerId` i `OpId`).
- [x] C5. `game/`: `VoxelTerrainService.MeasureSphere` (bez alokacji, wątek główny), `TerrainReplay` przez `OperationTerrain`, `SurvivalContentLoader`, wpięcie w `GameComposition` (treść do `ServerRuntimeOptions`, `InventoryService.Register(server)`).
- [ ] C6. Sprawdzenie (z `dotnet test game` i `smoke`), commit(y): `feat(persistence): add the survival migration and container storage`, `feat(server): create player containers and replicate them`.

### Etap D — klient (Sonnet 5.5; wygląd ogląda Dismond)

- [x] D1. Testy z tabeli etapu D (czerwone). **Zrobione:** testy Client.Tests przed kodem (budowa czerwona), `InventoryUiTests` w `game/tests/Inventory`.
- [x] D2. `src/Horyzont.Client/Inventory/*`; `ToolKind.None` i jego obsługa w `ToolController`.
- [x] D3. Akcje w `project.godot` (D18), `ToolInputReader` (Shift+kółko), `InventoryInputReader`.
- [x] D4. `HudTheme` w kodzie (bez binarnych zasobów): panele `StyleBoxFlat` tło `#0B1016` przy alfie 0,78, obrys 1 px `#2A3A48`, zaokrąglenie 6 px, akcent `#F2A531` (wybrany slot, paski), drugi akcent `#4FD1C5` (narzędzia), tekst `#E6EDF3`, cień; `ItemVisuals` z `visual` w `items.json` (glif 1–2 litery na kolorowym heksagonie dla surowców, prosty rysunek `_Draw` dla narzędzi), licznik w prawym dolnym rogu slotu.
- [x] D5. `HotbarHud` (dół ekranu, 10 slotów 56 px, numery 1–0, nazwa wybranego przedmiotu nad paskiem), `InventoryScreen` (środek: siatka plecaka 8 × 5, pod nią pasek, panel wyposażenia wyszarzony z napisem „wkrótce”, podpowiedź z nazwą, kategorią, liczbą i limitem stosu), `PickupToasts` (prawa strona, „+12 Kamień”, znikanie po 3 s), ostrzeżenie „Ekwipunek pełny”; `ToolHud` pokazuje licznik surowca zasypywania.
- [x] D6. `game/locale/items.csv` (`ITEM_CORE_STONE,Stone,Kamień` itd., `INV_*` dla etykiet), import tłumaczeń i wpis w `internationalization/locale/translations`; podpowiedzi narzędzi w `tools.csv` (nowe skróty).
- [x] D7. `InventoryNode` w `/Root/Client` dla każdego klienta z postacią; narzędzia (tylko host i solo, jak K2-13) czytają wybrany slot.
- [ ] D8. Test ręczny solo z zrzutami ekranu, sprawdzenie, commit(y): `feat(client): add the hotbar, the inventory screen and pickup toasts`. **Częściowo:** test ręczny solo i zrzuty zrobione (`docs/reports/k6-29w/`), commity zrobione; zostają testy Godot (GODOT TESTS PENDING, patrz Etap D w uwagach).

### Sprawdzenie na końcu każdego etapu

```
dotnet build Horyzont.sln
dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"
dotnet format Horyzont.sln --verify-no-changes
python tools/engine/build.py smoke          # etapy C i D
dotnet test game                            # etapy C i D (gdUnit4)
git diff <commit startu A>..HEAD -- tests/Horyzont.Core.Tests/Golden/TerrainJournals tests/Horyzont.Core.Tests/TerrainJournalGoldenCorpusTests.cs tests/Horyzont.Core.Tests/Hashing/DeterminismTests.cs   # pusty
```

## Odbiór

- [ ] Wszystkie testy akceptacyjne zielone w CI; korpus złoty i `DeterminismTests` bez zmian w plikach.
- [ ] Nowy gracz w grze solo ma manipulator i narzędzie budowy na pasku; kopanie kamienia, ziemi, piasku i rudy daje urobek zgodny z `yields.json`; zasypywanie zużywa surowiec i odmawia bez niego.
- [ ] Plecak 40 i pasek 10 obsługiwane myszą i klawiaturą; restart serwera zachowuje każdy przedmiot, a skrót po powtórce równa się skrótowi przed.
- [ ] Odstępstwa D11, D13, D15 i numer migracji D1 opisane w PR; pytania do właściciela w PR.
- [ ] ZASADY: dopisane reguły z Pułapek oznaczonych „do ZASAD”.

## Pułapki

- **Nowa ścieżka duplikacji.** Każda operacja przedmiotów przechodzi przez `TryApply`; nigdzie indziej nie zmienia się `Container`. Test właściwości obejmuje złożone z błędem w losowej składowej.
- **Treść w applierze.** Applier nie czyta `ItemCatalog` (limity stosu niesie operacja). Walidator i resolver mogą. (do ZASAD: przykład z operacji 31 i 33).
- **Teren w złożonej.** Każde miejsce, które rozpoznaje edycję terenu po typie (`ServerSimulation.Replay`, `TerrainReplay`, `TerrainSemanticsMeta.CheckTail`), używa `OperationTerrain.TryGetTerrainEdit`, inaczej powtórka kopania z urobkiem rzuci „area not preloaded” albo ogon z inną semantyką terenu przejdzie niezauważony. (do ZASAD)
- **`PlayerJoinOp` bez zmian.** Kontenery i zestaw startowy tylko przez osobną operację złożoną serwera (D6); inaczej korpus i stare dzienniki się rozjadą.
- **Skrót.** Sekcja Przetrwania warunkowa (D7); nie dopisuj jej bezwarunkowo „dla porządku”.
- **`ITerrainService` na wątku głównym.** `MeasureSphere` też; tylko resolver; zero alokacji (woksele w AABB kuli, `Span<int>` od wołającego).
- **Kolejność w `OnPlayerReady`.** `ItemRegistry` przed `ContainerState`; w trybie tylko do odczytu nic nie jest tworzone, a gracz bez kontenerów dostaje pusty pasek.
- **Rzut złożonej w `OperationApplied` (K3-15).** Gdy K3-15 zacznie rozsyłać `OperationApplied`, wysyła tylko składowe świata; składowe przedmiotów idą jako `ContainerDelta` (spec Sieć). Zapisz w planie K3-15.
- **Podwójne kółko myszy.** Kółko bez modyfikatora należy do paska; promień i paleta wymagają `Shift`. Ekran ekwipunku otwarty = brak wejścia ruchu, kamery i narzędzi.
- **Tłumaczenia CSV.** Nowy `items.csv` wymaga importu przez edytor (`.import` i `.translation`) i wpisu w `project.godot`; bez tego etykiety pokażą klucze.
- **Rebase.** Przy scaleniu: numer migracji (D1), `ProtocolVersion` (D2), ewentualne kolizje kodów z pakietami Kosmosu (zakresy rozdzielone, więc tylko pliki enumów).

## Odstępstwa i uwagi z realizacji

### Etap A (Opus 5.5, 2026-10-10)

- **`ContainerCreateOp.ContainerKind` zamiast `Kind`.** Pole `Kind` z tabeli kontraktów koliduje z `Operation.Kind` (kod operacji), więc rodzaj kontenera nazywa się `ContainerKind`. Kolejność i format pól w kodeku bez zmian.
- **Wartości enumów, których spec nie numeruje:** `ContainerAccess` `Shared` 1, `Private` 2; `ItemCategory` `Resource` 1 … `Component` 8 (domyślne stosy: surowiec 500, narzędzie, broń i pancerz 1, jedzenie 20, amunicja i komponent 100, obiekt 50); `ItemFlags` bitowo (`Bulk` 1 … `Retired` 128); `EquipmentSlot` `None` 0 … `Module` 4. Złote wartości w `ProtocolContractTests.Enums_GoldenValues`.
- **Format plików treści** (spec go nie podaje): `items.json` `{ "items": [ { key, category, flags[], maxStack?, maxStackOverride?, toolTier?, equipmentSlot?, visual? } ] }`, `terrain/yields.json` `{ "yields": [ { material, item, itemsPerCubicMeter, toolTier? } ] }`, `start.json` `{ "containers": { playerBag, hotbar, equipment }, "items": [ { item, count, container, slot } ] }`; nazwy enumów bez rozróżniania wielkości liter, liczby jako nazwy odrzucane. `maxStack` różny od domyślnego dla kategorii wymaga `"maxStackOverride": true`. Kontrola „usunięty typ zapisany w świecie” jako `ItemCatalog.CheckWorldItemTypes` (wołana przy wczytaniu w etapie C).
- **API stanu bez zmiany istniejących sygnatur:** `WorldState.CreateNew` dostał przeciążenie z kluczami przedmiotów (stare deleguje z pustą listą), `WorldCheckpoint` nowe pola jako właściwości `init` (`NextContainerId` = 1, `UpsertedContainers`, `RemovedContainers` = puste), więc istniejące konstruktory w Persistence działają bez zmian; `WorldStateBuilder` dostał `SetNextContainerId`, `AddItemType`, `AddContainer` (walidacja: identyfikator poniżej licznika i unikalny, gospodarz `Player` istnieje, jeden kontener gracza danego rodzaju, znane typy przedmiotów, liczba slotów = rozmiar).
- **`AppliedOperation`** dostał pozycyjny parametr `ContainerChanges` na końcu (jedyne miejsce tworzenia to applier).
- **Właściciel w `ContainerCreateOp`:** musi być `PlayerId.None` albo znanym graczem (spec milczy).
- **Powtórka:** warunek składowej terenu (ostatniej) jest sprawdzany przed wczytaniem obszaru, jak dotąd dla `TerrainEditOp`; odrzucona operacja w powtórce (także złożona) nie przełącza symulacji w `Faulted` i nie zmienia ticku — tak jak niespełniony warunek w K2. `ReplayFailedException.ComponentIndex` = `null` dla operacji niezłożonych.
- **`InstanceByteNonZero_Throws`** sprawdza blob 20 (`ItemStack` w formacie 1 występuje tylko w blobie; operacje 30–35 nie niosą stosów), komunikaty 40/41 dojdą w etapie B.
- **Zmienione istniejące pliki testów** (poza korpusem i `DeterminismTests`, które są bez zmian): `ProtocolContractTests` (złote wartości nowych enumów), `CodecGen` i `CodecCases` (generatory operacji 30–35 i przypadek `ContainerBlob`, więc tabelaryczne właściwości kodeków obejmują nowe ramki), `WorldFactory` (nowe pomocnicze). Poprawione `cref` do przeciążonego `CreateNew` w `PlanetGenesis`, `WorldCreator` i `WorldFactory`.
- **CA1711** wyciszone atrybutem dla `ItemStack` i `ItemFlags` (nazwy ze spec 03).
- **Przegląd testów przed kodem** zrobił ten sam wykonawca (Opus 5.5) w tej sesji; osobny przegląd Opus zostaje w PR. Test właściwości sprawdzono mutacją (usunięte cofanie w złożonej → trzy testy czerwone).

### Etap B (Opus 5.5, 2026-10-10)

- **Gdzie są testy z tabeli.** `ProtocolVersionTests` i `RejectReasonGoldenTests` nie powstały jako osobne pliki: te same kryteria sprawdzają istniejące `ProtocolContractTests` (`ProtocolVersion_IsFour`, `Constants_GoldenValues`, `Enums_GoldenValues` z kodami 40–43 odmów i 40–43, 49 komunikatów, lista rekordów komunikatów). `MessageRouterTests` w `Horyzont.Net.Tests` nie istniał, więc powstał jako nowy plik. Kubełek przedmiotów ma złoty test w `GameRulesTests`, scenariusze kodów 40–43 dopisano też do `IntentValidationTests.AllReasons`.
- **Rozwiązanie Przetrwania w osobnej klasie.** Walidacja parametrów i rozwiązanie intencji przedmiotów, urobek i koszt zasypywania są w wewnętrznym `SurvivalRules` (z `ItemPlacement`, `MiningYield`, `YieldLedger`); `IntentResolver` zostaje statyczny dla reguł K2, `IntentFacts` bez zmian. Kolejność kodów jak w planie: krok parametrów (z `ContainerLocked`) w walidatorze przed kubełkiem, a `ContainerChanged`, `NotEnoughItems` i reguły celu w rozwiązaniu po kubełku.
- **Doprecyzowania walidacji (plan milczy).** Nieistniejący kontener = `ContainerLocked` (jak cudzy; klient nie odróżnia). Dozwolone są tylko własne `PlayerBag` i `Hotbar` (gospodarz `Player` i właściciel = gracz); `Equipment` i inne rodzaje → `InvalidParameters`. `ExpectedType` 0, cel równy źródłu, „dowolny” w kontenerze źródła i „dowolny” ze slotem ≠ 0 → `InvalidParameters` (kodek zapisu odrzuca taki cel `ArgumentException`). Przeniesienie do pustego slotu też jest przycinane do limitu stosu z treści (limit mógł zmaleć po aktualizacji treści), 0 → `InventoryFull`. Cel „dowolny” daje zawsze `CompoundOp`, także z jednym przeniesieniem. Wyrzucenie z pustego slotu albo więcej niż w slocie → `NotEnoughItems`; typ nieobecny w treści → `InvalidParameters`.
- **Pomiar niemożliwy.** `MeasureSphere` = false (teren niewczytany, `NoTerrain`): kopanie to zwykły `TerrainEditOp` bez zmiany reszty, zasypywanie → `AreaNotLoaded`. Zasypywanie materiałem bez przedmiotu urobku → `NotEnoughItems` także przy zerowej objętości; gracz bez paska lub plecaka: kopanie zwykłe, zasypywanie `NotEnoughItems`.
- **Arytmetyka urobku.** Zaokrąglenia z tolerancją 1e-9 (`floor(x + ε)`, `ceil(x − ε)`), żeby sumy typu 0,1 + 0,2 nie gubiły sztuki; górna granica 1 000 000 sztuk na rozwiązanie. Kilka materiałów w jednym kopaniu: składowe `ItemCreateOp` w kolejności rosnącego `MaterialId`, każdy materiał rozmieszczany na kopii slotów uwzględniającej poprzednie.
- **`VoxelTerrainService.MeasureSphere`** dostał zaślepkę zwracającą false (wątek główny sprawdzany); prawdziwy pomiar to krok C5. Gra w etapie B nie przekazuje treści, więc zachowanie gry się nie zmienia.
- **Rozmiar `ItemRegistry`.** 4096 wpisów z kluczami po 64 bajty to ok. 268 KiB, więcej niż limit kanału niezawodnego (256 KiB). Rejestr mieści się przy kluczach do 60 bajtów (tak jest w `MaxMessages`); większą ramkę zapis odrzuca. Pytanie do przeglądu: limit 60 bajtów na klucz przedmiotu w `ContentLoader` albo dzielenie komunikatu na części (jak `ConstructState`).
- **Zmienione istniejące pliki testów i pomocnicze:** `ProtocolContractTests`, `IntentValidationTests`, `GameRulesTests`, `CodecGen` (generatory intencji 20–21 i komunikatów 40–43, 49; `IntentReply` z każdym kodem odmowy), `MaxMessages`, `SimFixture` i `ItemFixture` (parametr treści), `WorldFactory` (materiał `bedrock` o id 3 w `CreateWithPlayerContainers`), `FakeTerrainService` (`MeasureResult`, `MeasureCalls`; typ `MeasuredSphere`). Korpus złoty i `DeterminismTests` bez zmian (`git diff eee8f17..HEAD` na ich ścieżkach pusty).
- **TDD.** Testy etapu powstały przed kodem; czerwień widziana dla kodeków i protokołu (21 testów), testy symulacji pierwszy raz uruchomione po napisaniu resolvera — sprawdzone mutacją (bez `Commit` księgi urobku trzy testy `MiningYieldTests` czerwone). Przegląd testów przed kodem zrobił ten sam wykonawca; osobny przegląd Opus zostaje w PR.
- **Niestabilny test etapu A.** `ItemConservationProperties.Generator_ProducesAcceptedAndRefusedOperationsOfEveryKind` losuje bez ziarna i przy 100 sekwencjach liczba przyjętych `ItemSwapOp` krążyła wokół progu 10 (czerwony mniej więcej co drugi przebieg); próbka zwiększona do 400 sekwencji (osobny commit `test`).
- **Commity.** Najpierw protokół (`feat(protocol)`, z tymczasowym wyłączeniem kodów ≥ 40 w `IntentValidationTests.AllReasons`), potem symulacja (`feat(core)`), która dodaje ich scenariusze; każdy commit buduje się i przechodzi testy.

### Etap C (Sonnet 5.5, 2026-10-10)

- **Migracja `M0002_Survival`** tworzy pięć tabel z kroku C2 i wstawia `meta.next_container_id = 1` (świat po migracji z v1 ma więc klucz; `WorldLoader` toleruje jego brak jako 1). `WorldCreator` zapisuje `item_types` i `next_container_id` nowego świata i odrzuca świat z kontenerami (tak jak odrzuca graczy). Istniejące testy migracji, które używały numeru 2 do własnej migracji testowej (`TestMigration2`), dostały numer 3 i listę `[.. MigrationRunner.All, test]`, bo świat tworzony przez `WorldCreator` ma już `user_version` 2; `CreateAndLoadTests` oczekuje teraz 2.
- **Fikstura `world-v2.db`** (`FixtureGenerator.Generate_WorldV2`, kategoria Manual): świat z typami przedmiotów, trzema kontenerami gracza, zestawem w checkpoincie i przeniesieniem przedmiotu w ogonie dziennika. `world-v1.db` bez zmian (test `FixtureWorldV1_MigratesToV2_CreatesSurvivalTables` migruje jej kopię).
- **Zapis kontenerów** idzie w tej samej transakcji checkpointu co gracze (`WriteContainers`); uszkodzony blob albo wiersz o innym `container_id` niż w blobie → `WorldCorruptedException` (przez `StateCodec.DecodeContainerBlob` i `WorldStateBuilder`).
- **`ServerRuntime`**: `Content` (`SurvivalContent?`) i `ItemTypeKeys` w `ServerRuntimeOptions`; po wczytaniu świata z treścią `ItemCatalog.CheckWorldItemTypes` (usunięty typ bez flagi `Retired` → `WorldStartResult.Corrupted` z komunikatem; plan milczał o wyniku, wybrano `Corrupted`, bo to samo robi się dla uszkodzonych danych). Treść trafia do obu konstrukcji `ServerSimulation` (zwykłej i tylko do odczytu). `GameServerHooks` dostał opcjonalny parametr `messageHandlers` na końcu i przekazuje tylko 42/43; wyjątek obsługi jest logowany i wiadomość przepada (zły komunikat jednego klienta nie zatrzymuje ticku).
- **`InventoryService`**: tworzy kontenery tylko, gdy serwer jest w stanie `Running` (`RuntimeState != Running` = tylko do odczytu, także tryb bezpieczny po błędzie zapisu); delty idą od momentu, w którym gracz ma subskrypcję (po wysłaniu stanów), a zmiany z wersją 1 (kontener właśnie utworzony) nie są wysyłane jako delta. `ContainerUnsubscribe` jest ignorowany (D20). Fabryka `InventoryService.Register(runtime, loggers)`.
- **`VoxelTerrainService.MeasureSphere`**: woksele LOD 0 leżą na siatce całkowitej (punkty próbkowania SDF, jak w `Raycast`), liczy się woksel w odległości `<= radius`; stałe = SDF `< 0` (SDF 0, czyli sama powierzchnia, to powietrze); materiał z kanału indeksów przez `TerrainMaterialMap.TryGetMaterialId` (tekstura nieznana światu nie jest liczona). Zwraca `false` dla niewczytanych bloków, nieznanej planety, promienia nieskończonego/nieujemnego/powyżej `MaxMeasureRadiusMeters` (32) albo `Span` krótszego niż 256. Test (`MeasureSphereTests`) maluje materiał kamienia w płaskim generatorze, bo `VoxelGeneratorFlat` nie pisze kanału materiału (surowa wartość domyślna Voxel Tools to nie tekstura); prawdziwy generator planety pisze go zawsze.
- **Testy poza tabelą:** `ContainerPersistenceTests.Checkpoint_ChangedContainerAgain_UpsertsTheRow` i `ContainerRowForAnotherId_WorldCorrupted`; w `MeasureSphereTests` przypadki graniczne i brak alokacji; `InventoryFlowTests.CrashMidCompound_WholeOrNothing` obejmuje oba punkty awarii (`BeforeJournalCommit` → nic, `AfterJournalCommit` → całość). `Integration.Tests` dostał referencję do `Horyzont.Server` (rig `InventoryRig` z `ServerRuntime`, bo cała ścieżka klient → sesja → symulacja → writer wymaga uruchomionego serwera).
- **Mutacja:** `TerrainReplayCompoundTests` czerwony po cofnięciu `OperationTerrain` w `TerrainReplay`.
- **Do przeglądu Opus:** migracja (`M0002_Survival`, brak edycji `M0001`) i `VoxelTerrainService.MeasureSphere` (konwencja siatki i progu SDF).
- **Testy Godot nie uruchomione (GODOT TESTS PENDING).** Zasada wspólnego runnera: w chwili sprawdzenia było 00:35 UTC (< 03:30). Do uruchomienia: `dotnet test game`, `--engine-tests`, `--smoke`, `tools/server_smoke.py` oraz `--import` po dodaniu plików gry (`MeasureSphereTests`, `TerrainReplayCompoundTests`, `SurvivalContentLoader`). Build gry, format i testy rozwiązania zielone. C6 zostaje nieodhaczone do czasu tych przebiegów.

### Etap D (Sonnet 5.5, 2026-10-10)

- **Podział logiki.** `src/Horyzont.Client/Inventory` (bez Godota): `ClientInventory` (także `IClientSessionListener`; delta tylko do wersji +1, luka = jedno `ContainerSubscribe` do czasu nowego stanu, `OwnedCountChanged` tylko dla delty), `ClientContainer`, `HotbarSelection`, `InventoryInteraction` (sama implementuje `IClientSessionListener` dla odpowiedzi), `HeldStack`, `InteractionOutcome`, `PickupToastFeed`/`PickupToast`/`PickupToastKind`, `ItemToolMap`, `InventoryLog`. Dodatki ponad plan: `ClientInventory.CountOf`, `TryGetTypeId`, `HasRoomForResources` (dla licznika zasypywania i ostrzeżenia).
- **Gra.** `InventoryNode` (stan klienta, wejście, otwieranie ekranu) tworzy `HotbarHud`, `InventoryScreen`, `PickupToasts` tylko przy renderowaniu; ma drugi `Initialize` bez sesji (testy). `SlotView`, `ItemTooltip`, `ItemIconPainter`, `ItemVisuals`/`ItemVisual`, `InventoryInputReader` (+ `IInventoryInputSource`, `InventoryInputFrame`). Nowe pole `GameComposition.Inventory`; `ToolNode.Inventory` i zdarzenie `ToolNode.DigAccepted` (zapamiętuje intencje po numerze, żeby wiedzieć, że odpowiedź dotyczy kopania).
- **Narzędzie z paska zmienia się przy zmianie slotu, nie co tick.** `ToolNode` wysyła `SelectTool` z wybranego slotu tylko wtedy, gdy wynik `ItemToolMap` różni się od poprzedniego, i tylko jeśli ramka wejścia nie niesie własnego wyboru. Dla gracza jest to to samo, a skryptowane testy K2-13 i `ToolsDemo` (wybór `Builder` w ramce) działają bez zmian. Dopóki pasek nie dotarł, narzędzie zostaje domyślne (manipulator).
- **Sterowanie.** Kółko bez Shift obsługuje tylko `InventoryInputReader` (nigdy przy zwolnionej myszy, więc nie kręci paskiem za ekranem), z Shift tylko `ToolInputReader`; kierunek: kółko od siebie = poprzedni slot. `tool_next`/`tool_prev` mają w `project.godot` `shift_pressed=true`, a Shift sprawdza kod (dopasowanie akcji jest niedokładne, więc samo ustawienie nie wystarcza). `inventory_toggle` ma dwa zdarzenia: Tab i I. Esc na otwartym ekranie: najpierw wycofuje pytanie o wyrzucenie, potem rękę, potem zamyka. `LocalInputReader` i `ToolInputReader` dostały `Blocked` (ekran otwarty) oraz `CaptureMouse()`; `ToolInputReader.Active` jest teraz właściwością instancji.
- **Ręka i przeniesienia (D15).** Zabrany stos zostaje w slocie źródłowym; po wysłaniu intencji źródło i cel są „w drodze” (przyciemnione) do odpowiedzi. Położenie jedną sztuką (PPM z ręką) i zamiana całego stosu są dodatkiem; część stosu na slot z innym typem nie jest wysyłana (serwer odmówiłby `InvalidParameters`). Zmiana źródła pod ręką (delta) odkłada rękę z powrotem. Wyrzucenie bierze liczbę z ręki (połowa po PPM).
- **Powiadomienia.** Wzrost sumy typu w oknie 1,5 s od pierwszego wzrostu łączy się w jeden toast na 3 s. Przeniesienie między plecakiem a paskiem to dwie delty (dwa kontenery): strata i zysk tego samego typu w jednym przebiegu (100 ms) znoszą się w obu kolejnościach. „Ekwipunek pełny” pojawia się, gdy serwer przyjął kopanie, 250 ms nie przyszedł żaden wzrost, a w plecaku i na pasku nie ma wolnego slotu ani stosu surowca poniżej limitu (ułamkowe urobki nie dają fałszywych ostrzeżeń, dopóki jest miejsce).
- **Wygląd.** `HudTheme` w kodzie, ikony rysowane w `_Draw` (heksagon z glifem dla surowców, urządzenie i sześcian dla narzędzi z `visual.clientTool`); `HotbarHud` chowa się, gdy ekran jest otwarty (ekran ma własny rząd paska); `ToolHud` przesunięty nad pasek, jego linia „1 … 2 …” zastąpiona podpowiedzią `INV_HOTBAR_HINT`, dodany licznik surowca zasypywania (`TOOL_STOCK`). `HelpOverlay` zaktualizowany.
- **Tłumaczenia.** `items.csv` (klucze `ITEM_<KLUCZ_PRZEDMIOTU>` i `INV_*`), nowe `REJECT_*` (40–43) i `TOOL_NONE`/`TOOL_STOCK` w `tools.csv`, wpis w `project.godot`. Pliki `.translation` są w `.gitignore` (generuje je import), w repozytorium są `items.csv` i `items.csv.import`.
- **`ToolsDemo`** otwiera ekran ekwipunku 400 ticków po starcie skryptu (do zrzutów ekranu, tylko z `--dev-tools-demo`).
- **Test ręczny solo (Manual).** Uruchomione `godot --path game -- --solo --dev-tools-demo --screenshot …` (nie jest to żaden z przebiegów objętych regułą wspólnego runnera). Wynik: start bez błędów w logu; nowy gracz ma na pasku manipulator (slot 1) i narzędzie budowy (slot 2); kopanie ziemi dało „+60 Ziemia” (zrzut `docs/reports/k6-29w/powiadomienie-o-urobku.png`), zasypywanie zużyło 12 (licznik „W zapasie: Ziemia 48”), ekran ekwipunku z siatką 8 × 5, paskiem i szarym panelem „Wkrótce” (`ekran-ekwipunku.png`). Nie sprawdzono ręcznie: przenoszenie, dzielenie i zamiana myszą, Tab/I/kółko z prawdziwego sprzętu, restart z zachowaniem przedmiotów, odmowa zasypywania bez ziemi (pokrywają je testy Core/Client/Integration, nie ręczny przebieg).
- **Testy Godot nie uruchomione (GODOT TESTS PENDING).** Reguła wspólnego runnera: w chwili sprawdzenia było 00:55 UTC (< 03:30). Do uruchomienia: `dotnet test game` (w tym `InventoryUiTests`, `MeasureSphereTests`, `TerrainReplayCompoundTests`, wszystkie dotychczasowe), `--engine-tests`, `--smoke`, `tools/server_smoke.py`. Wykonano: build gry, `dotnet format` gry i rozwiązania, import (`--import`), testy rozwiązania. C6 i D8 zostają nieodhaczone do tych przebiegów.
- **Ryzyko.** Na Windows Shift+kółko dociera jako zwykłe kółko z `ShiftPressed`; na innych platformach (macOS) może być zamienione na kółko poziome i wymaga osobnych zdarzeń w `project.godot`.
