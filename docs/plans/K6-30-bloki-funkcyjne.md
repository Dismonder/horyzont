# K6-30 · Bloki funkcyjne

| | |
| --- | --- |
| Prowadzi | Sonnet 5.5 (Claude Code, `claude --model sonnet`); krok A1 (zatwierdzenie kontraktów Core) i przegląd PR — Opus 5.5 |
| Testy akceptacyjne | Sonnet 5.5, przed kodem; testy i kontrakty etapów A i B (operacje 36, 37, 40, 44, 49, reguły `CompoundOp`, nowy warunek `BlockClearOp`, intencje, bloby 21/22, komunikat 46) zatwierdza Opus 5.5 **przed** kodem (ZASADY §3, §8) |
| Przegląd | Opus 5.5 w PR (pakiet Sonnet zmienia kontrakty Core — ZASADY §9); wygląd (ekran skrzyni, podpowiedzi, otwarte drzwi, lampa) ogląda Dismond na zrzutach przed scaleniem |
| Zależy od | K6-29 — na gałęzi jest wycinek K6-29W (kontenery, operacje 30–35, `M0003_Survival`, `ProtocolVersion` 14); K2-12 (budowle). Brakujące elementy „reszty K6-29”, których K6-30 potrzebuje, bierze ten pakiet (D0, **DO USTALENIA-1**) |
| Gałąź | `pakiet/k6-30-bloki-funkcyjne` |
| Kamień milowy | K6 Przetrwanie; odbiór wg spec 03: „skrzynie, łóżka, drzwi i światła działają i przetrwają restart” |

**Cel:** skrzynie (wspólne albo prywatne, z kontenerem z K6-29W), łóżka ustawiające punkt odrodzenia, drzwi i lampy przełączane klawiszem interakcji oraz szybkie odkładanie do skrzyń w promieniu 10 m — każda zmiana jest jedną operacją dziennika (złożoną, gdy łączy blok, stan bloku i kontener), stany bloków funkcyjnych (`BlockEntity`, blob 21) i punkt odrodzenia przechodzą przez checkpoint i powtórkę, a klienci widzą je przez `BlockEntityStates` (46) i `OperationApplied`. Formaty są te ze spec 03, więc K6-32, K6-34, K6-35, K6-37 i K6-38 je rozszerzają (nowe rodzaje stanu bloku), a nie zastępują.

## Kontekst

- `docs/ZASADY.md`: §2 (każda zmiana świata to operacja; `OperationTerrain.TryGetTerrainEdit`), §3 (kontrakty chronione, zakresy kodów, reguła z linii 66 o zmianie appliera bez podbicia formatu), §4 (applier nie czyta treści ani terenu), §5, §6, §7, §8, §9, §11.
- `docs/spec/03-przetrwanie.md`: Ekwipunek i kontenery (wspólny/prywatny, szybkie odkładanie 10 m, blok z niepustym kontenerem nie do rozebrania), Śmierć i odrodzenie (łóżko ustawia punkt odrodzenia snem albo z menu), Architektura — Kontenery, Bloki ze stanem i byty trwałe, Stan gracza; Operacje i dziennik (operacje 36, 37, 40, 44, 49; tabela akcji: „Postawienie bloku”, „Rozbiórka”; intencje 22, 23, 28, 29; odmowy 40–58); Sieć (subskrypcja skrzyni do 6 m, koniec powyżej 8 m, komunikat 46, rzut złożonej w `OperationApplied`); Zapis (tabela `block_entities`, blob 21; `player_progress`, blob 22; skrót stanu); Obsługa błędów; Pakiety pracy (wiersz K6-30 i zależne K6-32/34/35/37/38).
- `docs/plan/plan-projektu.md:131` (K6-30: Sonnet 5.5; zależy od K6-29, K2-12) i wiersze K6-32…K6-38 (zależą od K6-30).
- `docs/plans/K6-29W-ekwipunek-i-wydobycie.md` — baza: D4 (dozwolone składowe złożonej, teren ostatni), D5 (`TryApply` z `UndoLog`), D7 (warunkowa sekcja skrótu), D8 (reguły Przetrwania tylko z `SurvivalContent`), D20 (subskrypcje), odstępstwa etapów A–D.
- `docs/plans/K2-12-budowle.md` (katalog bloków, `ConstructWorld`, siatki wbudowane), `docs/plans/K5-23-bloki-statku.md` (D1: `BlockFunction` jako treść w Core, `BlockContentTable` dla walidatora; drzwi `Door = 9` z `FunctionState` 0 = zamknięte; wzorzec akcji na bloku pod celownikiem — `ShipModeSwitch`).
- `docs/plans/K1-03-core-stan-i-operacje.md:614-622` (zakresy Przetrwania: operacje 30–49, odmowy 40–59, komunikaty 40–49, intencje 20–39, bloby 20–29).
- `docs/plans/README.md`: D4 (`ProtocolVersion` = `main` + 1 przy scaleniu), X5 (reguły routera), X6 (wspólne fixture protokołu).

Kod, na którym pakiet buduje (stan gałęzi przy pisaniu planu, 2026-10-16):

| Co | Gdzie |
| --- | --- |
| `OperationKind`: 1–7, 20–23, 30–35 (wolne w zakresie Przetrwania: 36–49) | `src/Horyzont.Core/Operations/OperationKind.cs` |
| `OperationApplier.TryApply` / `TryApplyCompound` (składowe przedmiotów z `UndoLog`, potem warunek i zastosowanie `TerrainEditOp` jako ostatniej), `CheckBlockClear`, `ApplyContainerCreate` („host kind not supported” dla gospodarza innego niż `Player`), `Finish` (wersje kontenerów raz na operację) | `src/Horyzont.Core/Simulation/OperationApplier.cs` |
| `OperationCodec` (format 1; złożona przyjmuje w kodeku dowolne składowe poza 30 — ograniczenia są w applierze) | `src/Horyzont.Core/Serialization/OperationCodec.cs` |
| `IntentCodec` (prywatny `IntentCode`: 1–3, 10, 13–15, 20, 21) | `src/Horyzont.Core/Serialization/IntentCodec.cs` |
| `RejectReason`: 0–21, 30, 31, 36, 37, 39, 40–43 | `src/Horyzont.Core/Intents/RejectReason.cs` |
| `MessageType`: 1–25, 31–33, 40–43, 49; `MessageChannels`; `BlobKind`: 1–5, 10–12, 20 | `src/Horyzont.Core/Protocol/`, `src/Horyzont.Core/Serialization/BlobKind.cs` |
| `ProtocolConstants.ProtocolVersion = 14`, `MaxContainerSlots = 256`, `MaxCompoundComponents = 4096`, `MaxCompoundBytes = 160 KiB`, `MaxReliableMessageBytes = 256 KiB` | `src/Horyzont.Core/Protocol/ProtocolConstants.cs` |
| `IntentValidator` (`Validate`, `BucketFor`, `ValidateRemove`: zasięg od oka, linia wzroku, `IntersectsPlayer`; `CellHitsPlayer`, `ToPlanet`), `SurvivalRules` (`CheckItemParameters`, `Resolve`; tylko własne `PlayerBag`/`Hotbar`), `ItemPlacement`, `IntentResolver` (statyczny), `ServerSimulation.Submit` (walidacja → `SurvivalRules.Resolve` → `TryApplyAndJournal`), `GameRules` (`ReachMeters` 8, kapsuła 1,8 m) | `src/Horyzont.Core/Simulation/` |
| `Container`, `ContainerStore` (`TryGet`, `TryGetOfPlayer`), `ContainerHost.ConstructBlock(construct, cell)`, `ContainerHostKind`, `ContainerKind.Chest = 4`, `ContainerAccess` (`Shared` 1, `Private` 2), `ContainerIdAllocator` | `src/Horyzont.Core/Items/`, `src/Horyzont.Core/Ids/` |
| `BlockState.FunctionState` / `WithFunctionState` (bity 25–27, `FunctionStateCount = 8`) | `src/Horyzont.Core/World/Constructs/BlockState.cs` |
| `BlockTypeDef(Key, Shape, MaterialId, HasCollision, MassKg, Function)`, `BlockFunction` (rodzaj + parametry), `BlockFunctionKind` (`Door = 9`, `RefuelStation = 10`), `BlockContentTable` | `src/Horyzont.Core/World/Blocks/` |
| `StateHasher`: sekcja Przetrwania `0x53` dopisywana warunkowo (`StateHasher.cs:159`) | `src/Horyzont.Core/Hashing/StateHasher.cs` |
| `WorldState.TryToPlanetFrame` (punkt ramy → planeta), `ChangeTracker`, `WorldCheckpoint`, `WorldStateBuilder`, `ContainerRecord` | `src/Horyzont.Core/World/WorldState.cs`, `src/Horyzont.Core/Checkpoints/` |
| `ConstructReplica` / `ConstructReplicaSet` (lustro budowli u klienta) | `src/Horyzont.Core/Replication/` |
| `BlockTypeCatalog` (`FormatVersion = 2`, `ParseFunction`: `cockpit`, `seat`, `engine`, `stabilizer`, `fuel_tank`, `cargo`, `landing_gear`, `pulse_drive`, `door`, `refuel_station`), `BlockTypeContent`, `BuiltInShapeMeshes` | `src/Horyzont.Constructs/Content/` |
| `ChunkMesher` (`_solid` z `HasCollision`), `BlockRenderTable`, `BlockRenderInfo`; `ConstructDirtyChunks.Collect` (rozpoznaje `ConstructCreateOp`/`BlockSetOp`/`BlockClearOp` po typie — złożonej nie zna) | `src/Horyzont.Constructs/Meshing/`, `src/Horyzont.Constructs/Sources/ConstructDirtyChunks.cs` |
| Treść bloków (18 typów plus wbudowany `core:missing`, w tym `core:ship_door`), nazwy bloków | `game/content/blocks/blocks.json`, `game/locale/blocks.csv` |
| `M0003_Survival`: tabele `block_entities (construct_id, cell_x, cell_y, cell_z, data)` i `player_progress (player_id, data)` już istnieją (puste); migracje `M0001_Initial`, `M0002_Space`, `M0003_Survival`; fikstury `world-v1.db`, `world-v3.db` (`FixtureGenerator.Generate_WorldV3`) | `src/Horyzont.Persistence/Migrations/`, `tests/Horyzont.Persistence.Tests/` |
| `WorldLoader`, `WorldCreator`, `WorldLoadOptions`, `Sql`, `Writing/PersistenceWriter.cs` | `src/Horyzont.Persistence/` |
| `InventoryService` (własne kontenery; `ContainerSubscribe` na cudzy → `ContainerState.Rejected(…, ContainerLocked)`; delty tylko do właściciela), `IServerClientMessageHandler`, `GameServerHooks` | `src/Horyzont.Server/Inventory/InventoryService.cs`, `src/Horyzont.Server/` |
| `ConstructStateSender` (`IsLive`, `RouteOperation`, `Complete`, `SendLive`), `EntityReplication.OnApplied` (wysyła `applied.Operation` bez rzutu), `ShipStatsCache` (po `ConstructChanges`) | `src/Horyzont.Server/Replication/`, `src/Horyzont.Server/Ships/ShipStatsCache.cs` |
| `ConstructMirror.ApplyOperation` (`ConstructCreateOp`, `BlockSetOp`, `BlockClearOp`, `ShipModeSetOp`, `EntityReparentOp`; inne → `RequestResync`), `ConstructPrediction` | `src/Horyzont.Client/Replication/` |
| `ShipModeSwitch` (wzór: blok pod celownikiem w zasięgu → intencja przez `IIntentSender` i `PendingIntentTracker`), `IToolWorldView`, `AimRay` | `src/Horyzont.Client/Ships/ShipModeSwitch.cs`, `src/Horyzont.Client/Tools/` |
| `ClientInventory` (stany i delty kontenerów, luka = ponowna subskrypcja), `InventoryInteraction`, `HeldStack` | `src/Horyzont.Client/Inventory/` |
| `ConstructWorld.OnApplied` → `ConstructDirtyChunks.Collect`, `HostConstructs`, `RemoteConstructs`, `BlockContentLoader` | `game/src/Constructs/`, `game/src/Client/Replication/RemoteConstructs.cs` |
| `InventoryNode`, `InventoryScreen`, `HotbarHud`, `HudTheme`, `SlotView`; `ToolInputReader` (`ship_mode_toggle` = G); akcje w `game/project.godot` (zajęte: W A S D, spacja, Shift, Ctrl, V, F, M, R, N, G, T, 1–0, Tab, I, Esc; **E wolne**) | `game/src/Client/Inventory/`, `game/src/Client/Tools/`, `game/project.godot` |
| Lokalizacja: `blocks.csv`, `items.csv` (`ITEM_*`, `INV_*`), `tools.csv` (`REJECT_*`, `TOOL_*`), `ui.csv` | `game/locale/` |

## Zakres

1. **Core: model i operacje.** `BlockEntity` (klucz budowla + komórka, stan zależny od funkcji: w tym pakiecie `Chest` i `Bed`, pozostałe rodzaje zarezerwowane), `BlockEntityStore` w `WorldState`, minimalny `PlayerProgress` z punktem odrodzenia (D11), operacje 36 `ContainerRemoveOp`, 37 `ContainerConfigureOp`, 40 `BlockEntitySetOp`, 44 `RespawnPointSetOp`, 49 `BlockFunctionStateSetOp`, rozszerzenie reguł `CompoundOp` (D12), gospodarz `ConstructBlock` w `ContainerCreateOp`, nowy warunek `BlockClearOp` (D13), kodeki, bloby 21 i 22, checkpoint, skrót stanu, rzut operacji dla klientów (`OperationConstructs`, D14).
2. **Core: intencje i protokół.** `QuickStackIntent` (22), `TakeAllIntent` (23, **DO USTALENIA-2**), `BlockInteractIntent` (28), `BlockConfigIntent` (29), odmowy `NotOwner` (54) i `ContainerNotEmpty` (57), postawienie i rozbiórka skrzyń i łóżek jako operacje złożone, przenoszenie do skrzyń w zasięgu, komunikat `BlockEntityStates` (46), `ProtocolVersion` 15.
3. **Treść.** Rodzaje funkcji `Chest`, `Bed`, `Light` w `BlockFunctionKind` i `BlockTypeCatalog` (format 2, dopisane rodzaje); bloki `core:chest_wood`, `core:chest_metal`, `core:bed`, `core:lamp`, `core:wood_door`.
4. **Zapis i serwer.** Zapis i odczyt `block_entities` i `player_progress` (tabele z M0003, bez nowej migracji), subskrypcje skrzyń (6 m / 8 m, dostęp), delty do wszystkich subskrybentów, replikacja stanów bloków (46), rzut złożonej w `OperationApplied`.
5. **Klient i gra (tylko to, co pakiet musi pokazać).** Klawisz interakcji E z podpowiedzią, ekran skrzyni obok plecaka, przycisk szybkiego odkładania, menu dostępu skrzyni dla właściciela, otwarte drzwi bez kolizji, światło lampy, etykiety PL/EN.

Poza zakresem:

| Co | Gdzie |
| --- | --- |
| Operacje 38, 39 (byty trwałe), przedmioty leżące, wysypanie zawartości zawalonego/zniszczonego bloku jako przedmiotów leżących, blob 24 | reszta K6-29 (przedmioty leżące), K6-36 (zawalenia) |
| Operacje 41–43, 45–48, bloby 23 i 25, komunikaty 47 i 48, pozostałe pola `PlayerProgress` (zdrowie, jedzenie, odkrycia, nagrobki, odpoczynek) | reszta K6-29, K6-32, K6-33 |
| Stacje (warsztat, piec, rafineria, kuchnia, fabrykator), kontenery `StationInput`/`StationOutput`, rozliczenie z ticków, rodzaj stanu `Station` | K6-32 |
| Generator tlenu (rodzaj stanu `OxygenGenerator`), schronienie, ciepło i komfort z bloków | K6-34, K6-33 |
| Odrodzenie przy łóżku po śmierci, nagrobki, `TakeAllIntent` na nagrobku (znikanie pustego), punkt odrodzenia jako jedna funkcja Core | K6-35 |
| Koszt postawienia skrzyni/łóżka/lampy/drzwi z ekwipunku, zwrot przy rozbiórce, wytrzymałość, `AllowCollapse`, `CollapseNotConfirmed` | K6-36 |
| Teleporty (rodzaj stanu `Teleport`, wariant `TeleportName` w `BlockConfigIntent` — format ustalony tu, obsługa `InvalidParameters`), kontener ładowni `CargoHold` w blokach `Cargo` | K6-37 |
| Grządki (rodzaj stanu `Planter`), `PlantIntent`, `CropHarvestIntent`, lampa uprawna jako źródło światła dla roślin | K6-38 |
| Pełny interfejs (podgląd przeniesienia przed odpowiedzią serwera, sortowanie, nazwy skrzyń, ikony bloków) | K6-39 |
| Animacja drzwi, grafika bloków, emisyjny materiał lampy | K8-50 (grafika) |

## Decyzje

Każda decyzja rozstrzyga miejsce, w którym specyfikacja milczy albo pakiet świadomie bierze jej wycinek. Odstępstwa od spec 03 są oznaczone **[odstępstwo]** i trafiają do opisu PR. Rzeczy, których nie da się ustalić z kodu ani specyfikacji, są oznaczone **DO USTALENIA-n** z proponowaną decyzją; wykonawca stosuje propozycję, dopóki Opus 5.5 albo właściciel nie zdecyduje inaczej w kroku A1.

- **D0. Baza i wycinek „reszty K6-29”. DO USTALENIA-1.** K6-29W zrealizował tylko część K6-29 (operacje 30–35, blob 20, komunikaty 40–43 i 49). K6-30 potrzebuje operacji 36 (rozbiórka skrzyni usuwa pusty kontener), 37 (zmiana dostępu skrzyni) i miejsca na punkt odrodzenia (`PlayerProgress`, blob 22). **Propozycja:** K6-30 dodaje 36, 37 i blob 22 w wersji 1 tylko z punktem odrodzenia (D11); osobny pakiet „reszta K6-29” nie jest warunkiem startu. Alternatywa (odrzucona w propozycji): czekać na pakiet „reszta K6-29” — blokuje cztery pakiety K6 zależne od K6-30.
- **D1. Kody w tym pakiecie (zakres Przetrwania, K1-03).** Operacje 36, 37, 40, 44, 49; intencje 22, 23 (DO USTALENIA-2), 28, 29; odmowy `NotOwner` 54, `ContainerNotEmpty` 57; komunikat 46; bloby 21 (`BlockEntity`), 22 (`PlayerProgress`). Pozostałe numery ze spec 03 zostają zarezerwowane (kodeki odrzucają je jako nieznane).
- **D2. `ProtocolVersion` = 15** (`main` ma 14 po K6-29W; README D4). Aktualizowane istniejące testy `ProtocolContractTests.ProtocolVersion_MatchesDocumentedMergeValue` i `Constants_GoldenValues`; przy rebase wartość = (`main` w chwili scalenia) + 1.
- **D3. Bez nowej migracji.** Tabele `block_entities` i `player_progress` utworzyła `M0003_Survival` (spec: „kolejne pakiety dodają tylko bloby i zapis”). Następny wolny numer to **M0004**; K6-30 go nie zajmuje (spec 04:197 planuje M0004 dla Walki; numer nadaje się przy scaleniu). Gdyby przegląd zażądał zmiany schematu, migracja dostaje następny wolny numer w chwili scalenia, a `M0003` nie jest edytowana. Nowa fikstura `world-v3-blocks.db` (D25) zabezpiecza dane tego pakietu przed przyszłymi migracjami.
- **D4. Model stanu bloku.** Nowy katalog `src/Horyzont.Core/BlockEntities/`:
  - `BlockRef(EntityId Construct, CellPos Cell)` — klucz (`readonly record struct`, porządek: budowla, potem `CellPos` jak w checkpointach chunków);
  - `BlockEntityKind : byte` — `Chest = 1`, `Bed = 2`; zarezerwowane z nazwą i numerem, bez formatu: `Station = 3` (K6-32), `Teleport = 4` (K6-37), `OxygenGenerator = 5` (K6-34), `Planter = 6` (K6-38), `CargoHold = 7` (K6-37). Kodek formatu 1 przyjmuje tylko 1 i 2; kolejne pakiety dopisują rodzaj (nowa wartość dyskryminatora, istniejące bajty bez zmian, `ProtocolVersion` w tamtym pakiecie);
  - `BlockEntityState` — `abstract record` z konstruktorem `private protected`; `ChestBlockState(ContainerId Container)`, `BedBlockState(PlayerId Owner)` (`PlayerId.None` = łóżko wolne);
  - `BlockEntityStore` — magazyn po `BlockRef`, iteracja posortowana (ZASADY §4), liczba stanów na budowlę (limit D23), zbiór zmienionych kluczy dla checkpointu.
  Drzwi i lampy nie mają `BlockEntity`: ich stan to bity funkcji bloku (spec), zmieniane operacją 49.
- **D5. Treść bloków.** `BlockFunctionKind` dostaje `Chest = 11`, `Bed = 12`, `Light = 13` (treść, nie format — wiersz `BlockTypeDef.cs` w ZASADY §3), `BlockFunction` nowy parametr `ContainerSlots` (`ushort`, 1…256, tylko `Chest`) i fabryki `CreateChest(slots)`, `Bed`, `Light`. `BlockTypeCatalog` przyjmuje `{"kind":"chest","slots":20}`, `{"kind":"bed"}`, `{"kind":"light","range":8,"color":"#FFD9A0","energy":1.5}`; parametry wizualne światła (zasięg, kolor, moc) trafiają do `BlockTypeContent` (Constructs), nie do Core. `FormatVersion` zostaje 2: rodzaje są dopisane, a istniejące pliki parsują się bez zmian (precedens: `refuel_station` z K5-24 bez podbicia). Nowe typy (liczby to dane do strojenia, **DO USTALENIA-11**):

  | Klucz | Kształt | Masa (kg) | Funkcja | Materiał |
  | --- | --- | --- | --- | --- |
  | `core:chest_wood` | `cube` | 80 | `Chest`, 20 miejsc | `wood` |
  | `core:chest_metal` | `cube` | 200 | `Chest`, 40 miejsc | `metal` |
  | `core:bed` | `mesh` `builtin:slab` | 120 | `Bed` | `ship_fabric` |
  | `core:lamp` | `cube` | 20 | `Light` (8 m, `#FFD9A0`, 1,5) | nowy `lamp` (`#F2E3B3`) |
  | `core:wood_door` | `cube` | 100 | `Door` | `wood` |

- **D6. Drzwi wysokie na kilka bloków. DO USTALENIA-3.** Kapsuła postaci ma 1,8 m (`GameRules.PlayerCapsuleHeight`), komórka 1 m, więc pojedynczy blok drzwi nie przepuszcza gracza. **Propozycja:** `Toggle` na komórce drzwi przełącza „kolumnę drzwi”: komórkę wskazaną i przylegające do niej wzdłuż osi Y budowli komórki tego samego typu drzwi, łącznie najwyżej `GameRules.MaxDoorColumnCells = 3` (najpierw w dół, potem w górę, od wskazanej). Nowy stan = przeciwny do stanu komórki wskazanej (0 ↔ 1) dla wszystkich komórek kolumny. Jedna komórka → `BlockFunctionStateSetOp`; więcej → `CompoundOp` z samych składowych 49 (D12), wersja budowli +1 raz. Zamknięcie, przy którym którakolwiek komórka przecina kapsułę gracza online → `IntersectsPlayer` (18). Otwarte drzwi nie kolidują i rysują się jako cienki panel przy jednej ścianie (nowa siatka wbudowana `builtin:door_open`, panel 1 × 1 × 0,1 m przy ścianie −X w orientacji bloku); wygląd zatwierdza Dismond na zrzucie (ZASADY: wygląd przed animacją). Alternatywy: dwa niezależne bloki przełączane osobno (połowa drzwi otwarta przy odmowie drugiej intencji); blok wielokomórkowy (nie istnieje w K2-12).
- **D7. Lampy. DO USTALENIA-4.** `Light`: `FunctionState` 1 = włączona, 0 = wyłączona. Resolver `PlaceBlockIntent` stawia lampę włączoną (`BlockState` z `FunctionState` 1 — operacja niesie wartość, applier nie czyta treści), inne bloki jak dotąd z 0. `Toggle` → jedna operacja 49. Gra: `OmniLight3D` bez cieni na lampę włączoną, najwyżej `MaxLitLampsRendered = 32` najbliższych kamerze (stała gry, nie Core); reszta bez światła. Propozycja do zatwierdzenia: limit 32 i brak cieni.
- **D8. Skrzynie: postawienie i rozbiórka.** Postawienie typu z funkcją `Chest` (resolver w `SurvivalRules`): `CompoundOp[ContainerCreateOp(nowy id, Chest, ConstructBlock(budowla, komórka), właściciel = stawiający, dostęp = Shared, rozmiar = ContainerSlots z treści), BlockEntitySetOp(budowla, komórka, typ, ChestBlockState(id)), BlockSetOp | ConstructCreateOp]` — składowa budowli jest ostatnia (D12). Rozbiórka (`RemoveBlockIntent`): kontener niepusty → `ContainerNotEmpty` (57); skrzynia prywatna i gracz nie jest właścicielem → `NotOwner` (54, **DO USTALENIA-5**); inaczej `CompoundOp[ContainerRemoveOp, BlockEntitySetOp(pusty stan), BlockClearOp]`. Bez kosztu i zwrotu (K6-36). Domyślny dostęp nowej skrzyni: `Shared` (**DO USTALENIA-5**; spec nie podaje).
- **D9. Dostęp i zasięg skrzyń. DO USTALENIA-8.** Odległość = od oka serwera (`TryGetEye` jak w walidatorze) do środka komórki skrzyni w ramie planety (`WorldState.TryToPlanetFrame`), ten sam region planety. `GameRules.ContainerOpenRangeMeters = 6` (subskrypcja), `ContainerKeepRangeMeters = 8` (koniec subskrypcji i zasięg intencji przedmiotów na skrzyni), `QuickStackRangeMeters = 10`. Dostęp: `Shared` albo właściciel. Intencje przedmiotów (`ItemMoveIntent`, „dowolny” cel, 22, 23) mogą używać kontenerów: własny `PlayerBag`, własny `Hotbar`, skrzynia z dostępem w zasięgu 8 m. Nieistniejący kontener albo brak dostępu → `ContainerLocked` (43, jak K6-29W); skrzynia poza zasięgiem → `OutOfReach` (7). `ItemDropIntent` (21) tylko z własnych kontenerów (jak K6-29W D13).
- **D10. Łóżka. DO USTALENIA-6.** `BedBlockState(Owner)`; postawienie: `CompoundOp[BlockEntitySetOp(…, BedBlockState(None)), BlockSetOp | ConstructCreateOp]`. `Sleep` i `SetRespawn` (w tym pakiecie skutek ten sam; odpoczynek dokłada K6-33): łóżko wolne albo własne → `CompoundOp[BlockEntitySetOp(stare łóżko, Bed(None))` gdy gracz ma inne istniejące łóżko ze swoim właścicielem`, BlockEntitySetOp(łóżko, Bed(gracz))` gdy właściciel inny niż gracz`, RespawnPointSetOp(gracz, łóżko)]`; łóżko innego gracza → `NotOwner` (54). Rozbiórka łóżka: `CompoundOp[RespawnPointSetOp(właściciel, brak)` gdy jego punkt wskazuje to łóżko`, BlockEntitySetOp(pusty), BlockClearOp]` (rozebrać może każdy — spec nie ma uprawnień do bloków). Niezmiennik pilnowany przez resolver i test właściwości: punkt odrodzenia gracza P = łóżko B ⇔ właściciel B = P. Samo odrodzenie przy łóżku robi K6-35.
- **D11. `PlayerProgress` w wersji 1 tylko z punktem odrodzenia. [odstępstwo]** `PlayerProgress(PlayerId Player, BedRef? RespawnBed)`, `BedRef(EntityId Construct, CellPos Cell, ushort ExpectedType)`; `PlayerProgressStore` w `WorldState`. Blob 22 wersja 1: varuint gracz, bajt flag (bit 0 = jest punkt), opcjonalnie varuint budowla, `CellPos`, varuint typ. K6-33 dokłada pola jako wersję 2 z odczytem wersji 1 (ZASADY §3, wiersz `Checkpoints/`). Rekord istnieje tylko dla gracza z punktem odrodzenia (wpis bez punktu jest usuwany).
- **D12. `CompoundOp` w formacie 1 — rozszerzenie zbioru składowych.** Dozwolone teraz: 31–37, 40, 44, 49 oraz co najwyżej **jedna składowa świata nieodwracalna**: `TerrainEditOp` (1), `ConstructCreateOp` (2), `BlockSetOp` (3) albo `BlockClearOp` (4) — zawsze **ostatnia**. Składowe 49 tylko w złożonej bez składowej budowli (2–4). Kolejność stosowania (rozszerza K6-29W D5, jedno przejście z `UndoLog`):
  1. struktura (jak dziś + nowe reguły);
  2. warunki wstępne składowej ostatniej na stanie wyjściowym (żadna inna składowa nie zmienia budowli ani terenu, więc wynik się nie zmienia);
  3. pozostałe składowe po kolei z `UndoLog` (36, 37, 40, 44, 49 dostają wpisy cofania); ich warunki dotyczące bloku (gospodarz `ConstructBlock` w 35, niepusty stan w 40, łóżko w 44) czytają **widok efektywny**: komórkę, którą zapisuje składowa ostatnia (`BlockSetOp.Block`, `ConstructCreateOp.Block` w `FirstCell` nowej budowli), widzą z jej nowym blokiem; pozostałe komórki — ze stanu żywego. Usunięcie stanu (40 z pustym stanem) i usunięcie kontenera sprawdzają stan żywy;
  4. dla `BlockClearOp` ostatniej: warunek D13 po składowych z kroku 3; niespełniony → cofnięcie i błąd z numerem ostatniej składowej;
  5. zastosowanie składowej ostatniej; `Finish` podbija wersję kontenerów i — dla składowych 49 — wersję każdej dotkniętej budowli raz, i składa `ConstructChanges` (składowa budowli albo budowle z 49).
  Stare dzienniki nie zawierają nowych rodzajów składowych, więc powtórka się nie zmienia (jak K6-29W D4). Wersja formatu złożonej zostaje 1.
- **D13. Nowe warunki istniejących operacji.** `BlockClearOp` (4): komórka nie ma `BlockEntity` i nie jest gospodarzem żadnego kontenera (inaczej zostałyby osierocone przedmioty). `ContainerCreateOp` (35): gospodarz `ConstructBlock` obsługiwany — blok istnieje (widok efektywny D12) i nie ma jeszcze kontenera tego samego rodzaju z tym gospodarzem (indeks gospodarz × rodzaj w `ContainerStore`; stacje z K6-32 będą mieć dwa rodzaje na jednym bloku). Format obu operacji zostaje 1: stare dzienniki nie mają stanów bloków ani gospodarzy blokowych, więc ich powtórka daje identyczny wynik; spełnia to warunek ZASADY §3 (linia 66: korpus złoty i `DeterminismTests` bez zmian w plikach), tak jak K6-29W dla gospodarzy kontenerów. **Do przeglądu Opus.**
- **D14. Rzut operacji dla budowli i klientów: `OperationConstructs`.** Nowa klasa w `src/Horyzont.Core/Operations/` (wzór `OperationTerrain`): `TryGetConstructPart(Operation op, out Operation part)` — dla operacji niezłożonej zwraca ją samą, gdy dotyczy budowli (2, 3, 4, 20, 49); dla złożonej: jej ostatnią składową budowli (2–4) albo nową `CompoundOp` z samych składowych 49; inaczej `false`. Używają jej: `EntityReplication.OnApplied` (`OperationApplied.Operation` = rzut, więc składowe przedmiotów i stanów bloków nie trafiają do obcych klientów — spec Sieć), `ConstructWorld.OnApplied`/`ConstructDirtyChunks` (przebudowa chunków po złożonej i po 49), `ConstructMirror` (obsługa `BlockFunctionStateSetOp` i złożonej z 49). Do ZASAD jako reguła obok `OperationTerrain` (Pułapki).
- **D15. `BlockEntityStates` (46).** `BlockEntityStates(EntityId Construct, IReadOnlyList<BlockEntityEntry> Entries)`, `BlockEntityEntry(CellPos Cell, BlockEntityState State)`; pełna lista stanów jednej budowli (także pusta — czyści mapę klienta). Kanał niezawodny uporządkowany, ten sam co `ConstructState` i `OperationApplied`. Serwer wysyła: (a) gdy łącze budowli u gracza staje się `Live` (nowe zdarzenie `ConstructStateSender.LinkLive`, po przekazaniu buforowanych operacji) — bieżący stan, jeśli budowla ma choć jeden stan; (b) po każdej zastosowanej operacji, która zmieniła stany bloków budowli (składowe 40), do każdego gracza z `IsLive(gracz, budowla)` — po `OperationApplied` tej operacji. Gracz z łączem w transferze dostaje stan w (a). Klient czyści mapę budowli przy nowym pełnym stanie (`ConstructState`) i przy jej usunięciu. Router: jak `ConstructState` (X5), fixture X6.
- **D16. Szybkie odkładanie (22).** Źródło: tylko własny `PlayerBag` (spec: „z plecaka”; pasek nietknięty). Cele: skrzynie z dostępem w promieniu `QuickStackRangeMeters` (D9), które już zawierają dany typ; kolejność skrzyń: rosnąca odległość, remis — rosnący `ContainerId`. Dla każdego slotu plecaka rosnąco: dopełnienie istniejących stosów tego typu w kolejnych skrzyniach (sloty rosnąco), potem puste sloty tych skrzyń; limit stosu z `ItemDef` w każdej `ItemTransferOp` (ZASADY §4). Wynik: jedna `CompoundOp` przeniesień. Nic pasującego → `NotEnoughItems` (41); pasujące, ale skrzynie pełne → `InventoryFull` (40) (**DO USTALENIA-10**). Kubełek `Items` (jeden żeton).
- **D17. `TakeAllIntent` (23) dla skrzyń. DO USTALENIA-2.** Spec wymienia go w tabeli intencji, a wiersz K6-30 go nie zawiera; ekran skrzyni bez „Weź wszystko” jest niepełny. **Propozycja:** w K6-30 dla kontenerów `Chest` (format intencji ze spec: kontener); rozmieszczenie jak urobek z K6-29W D10 (`ItemPlacement`: dopełnienie stosów paska, potem plecaka, puste sloty plecaka, potem paska); częściowo → przenosi, ile wejdzie; nic → `InventoryFull`. Gałąź nagrobka (znikanie pustego) dokłada K6-35.
- **D18. `BlockInteractIntent` (28) i `BlockConfigIntent` (29).** Pola ze spec: `BlockInteractIntent(EntityId Construct, CellPos Cell, BlockInteractAction Action)` (`Toggle` 1, `Sleep` 2, `SetRespawn` 3); `BlockConfigIntent(EntityId Construct, CellPos Cell, BlockConfig Config)` — wariant bajtem: 1 `TeleportName(string Name)` (UTF-8 1–32 B; w K6-30 → `InvalidParameters`, obsługa K6-37), 2 `ContainerAccess(ContainerAccess Access)`. Walidacja jak `ValidateRemove`: `UnknownConstruct`, `WrongPlanet`, `CellOutOfBounds`, `CellEmpty`, `OutOfReach` (zasięg `ReachMeters` + luz komórki), `NoLineOfSight`; potem funkcja bloku zgodna z akcją (`Toggle`: `Door`, `Light`; `Sleep`/`SetRespawn`: `Bed`; `ContainerAccess`: blok ze stanem `Chest`), inaczej `InvalidParameters`. Zmiana dostępu tylko przez właściciela kontenera (`NotOwner`) → `ContainerConfigureOp(kontener, ten sam rozmiar, nowy dostęp)`; ten sam dostęp → `InvalidParameters`. Kubełek `Blocks`. Otwarcie skrzyni nie jest intencją, tylko subskrypcją (D9, D20).
- **D19. Applier 37 nie sprawdza właściciela.** Spec: „dostęp zmienia tylko właściciel”. Warunek właściciela jest w walidatorze (`NotOwner`), bo operacja nie niesie aktora, a operacje serwera (administrator, K6-39) mogą zmieniać dostęp; applier sprawdza tylko, że usuwane sloty są puste i rozmiar 1…256. **Do przeglądu Opus.**
- **D20. Subskrypcje cudzych kontenerów (serwer).** `ContainerSubscribe` na skrzynię: dostęp (`ContainerLocked`), zasięg 6 m (`OutOfReach`) → `ContainerState` z pełnym stanem i zapis subskrypcji. `InventoryService` co 10 ticków (uczestnik ticku serwera) sprawdza subskrypcje skrzyń: odległość > 8 m, utrata dostępu (zmiana na prywatną) albo usunięcie kontenera → `ContainerState.Rejected(id, OutOfReach | ContainerLocked)` i koniec subskrypcji. `ContainerUnsubscribe` na skrzynię kończy subskrypcję (na własny kontener dalej ignorowany, K6-29W D20). Delty idą do wszystkich subskrybentów kontenera. Rozłączenie gracza usuwa jego subskrypcje.
- **D21. Bez `SurvivalContent` (testy K1/K2/K5).** `Toggle` drzwi i lamp działa (wymaga tylko `BlockContentTable`). Postawienie typu `Chest`/`Bed`, rozbiórka komórki ze stanem bloku albo kontenerem, `Sleep`, `SetRespawn`, 22, 23, 29 → `InvalidParameters` (zachowanie K6-29W D8; inaczej applier odrzuciłby zwykły `BlockClearOp` i `Submit` rzuciłby wyjątek).
- **D22. Skrót stanu.** Sekcja Przetrwania (`0x53`) obejmuje teraz także stany bloków i punkty odrodzenia: warunek sekcji dostaje `BlockEntities.Count > 0 || Progress.Count > 0`, a w sekcji, po kontenerach, podsekcje dopisywane tylko gdy niepuste: `0x42` + stany bloków po `BlockRef` (rodzaj, pola), `0x50` + punkty odrodzenia po graczu. Skrót świata z przedmiotami bez stanów bloków (K6-29W) się nie zmienia. Bity funkcji bloku są już w skrócie (stan bloku w chunkach).
- **D23. Limity.** `ProtocolConstants.MaxBlockEntitiesPerConstruct = 4096` (walidator odrzuca postawienie bloku funkcyjnego ponad limit `InvalidParameters`; komunikat 46 ≤ 4096 wpisów, ok. 100 KiB < 256 KiB); `MaxTeleportNameBytes = 32`; `GameRules.MaxDoorColumnCells = 3`, `ContainerOpenRangeMeters = 6`, `ContainerKeepRangeMeters = 8`, `QuickStackRangeMeters = 10`.
- **D24. Klient bez przewidywania przełączeń.** Drzwi i lampa zmieniają się po `OperationApplied` (host i solo — w następnym ticku). Spec nie wymaga predykcji stanu bloku; postawienie skrzyni korzysta z istniejącej predykcji bloku (`ConstructPrediction`), bo rzut złożonej to zwykły `BlockSetOp`/`ConstructCreateOp`.
- **D25. Fikstura. DO USTALENIA-9.** Nowa `tests/Horyzont.Persistence.Tests/Fixtures/world-v3-blocks.db` z `FixtureGenerator.Generate_WorldV3Blocks` (kategoria `Manual`): świat ze skrzynią z przedmiotami, łóżkiem z właścicielem i punktem odrodzenia, otwartymi drzwiami i lampą w checkpoincie oraz przełączeniem drzwi i przeniesieniem do skrzyni w ogonie dziennika. `world-v3.db` bez zmian. Następna migracja (M0004) testuje się też na tej fiksturze.

## Kontrakty chronione i wersje

| Ścieżka (ZASADY §3) | Zmiana w K6-30 | Co podbić / jak udowodnić |
| --- | --- | --- |
| `src/Horyzont.Core/Operations/`, `Simulation/OperationApplier.cs` | nowe rodzaje 36, 37, 40, 44, 49 (format 1); `OperationKind`; reguły złożonej (D12); nowy warunek `BlockClearOp` i gospodarz `ConstructBlock` w 35 (D13); `ContainerChange.Removed`; `OperationConstructs` | nowe rodzaje mają rekord w wersji 1; rodzaje 1–35 bez zmiany bajtów i bez zmiany wyniku powtórki starych dzienników — dowód: `git diff <commit startu A>..HEAD` na korpusie złotym i `DeterminismTests` pusty (ZASADY §3, linia 66); przegląd Opus |
| `src/Horyzont.Core/Intents/` | intencje 22, 23, 28, 29; `RejectReason` 54, 57 | kody intencji z wersją rekordu 1 w `IntentCodec`; `ProtocolVersion` 15 |
| `src/Horyzont.Core/Checkpoints/`, nowe typy zapisywane (`BlockEntities/`, `Progress/`) | `BlockEntityRecord`, `PlayerProgressRecord`, pola `WorldCheckpoint` (`init`, domyślnie puste), `ChangeTracker`, `WorldStateBuilder` | nowe bloby 21 i 22 w wersji 1; Persistence czyta wersję 1 (przyszła wersja 2 bloba 22 musi czytać 1) |
| `src/Horyzont.Core/World/` | `WorldState` (magazyny stanów bloków i postępu); `BlockFunction`/`BlockFunctionKind` | `WorldState` nie jest formatem; `BlockFunction*` to treść — nic do podbicia (wiersz `BlockTypeDef.cs`) |
| `src/Horyzont.Core/Hashing/` | podsekcje `0x42`, `0x50` w sekcji Przetrwania (D22) | opis w PR: skróty światów ze stanami bloków albo punktem odrodzenia są nowe; skróty świata domyślnego i świata K6-29W bez zmian (testy z A0) |
| `src/Horyzont.Core/Protocol/`, `Serialization/` | `MessageType.BlockEntityStates = 46`, `BlobKind` 21, 22, kodeki, `ProtocolConstants` (limity D23) | `ProtocolVersion` = 15 (D2); fixture X6; router X5 |
| `src/Horyzont.Core/Replication/ConstructReplica.cs` | `TrySetFunctionState` | przegląd Opus (cały Core) |
| `src/Horyzont.Persistence/Migrations/` | **brak zmian** (D3) | następny wolny numer M0004 nieużyty |
| `game/content/blocks/blocks.json` | nowe typy i rodzaje funkcji | `BlockTypeCatalog.FormatVersion` zostaje 2 (D5) |
| `tests/Horyzont.Core.Tests/Golden/TerrainJournals/`, `TerrainJournalGoldenCorpusTests.cs`, `DeterminismTests.cs` | **bez zmian** | `git diff` pusty |

## Projekt

### Pliki

```
src/Horyzont.Core/
  BlockEntities/BlockRef.cs, BlockEntityKind.cs, BlockEntityState.cs, ChestBlockState.cs, BedBlockState.cs, BlockEntityStore.cs
  Progress/PlayerProgress.cs, BedRef.cs, PlayerProgressStore.cs
  Operations/ContainerRemoveOp.cs, ContainerConfigureOp.cs, BlockEntitySetOp.cs, RespawnPointSetOp.cs, BlockFunctionStateSetOp.cs,
             OperationConstructs.cs (D14)
  Operations/OperationKind.cs (36, 37, 40, 44, 49), ContainerChange.cs (Removed)
  Intents/QuickStackIntent.cs, TakeAllIntent.cs, BlockInteractIntent.cs, BlockInteractAction.cs, BlockConfigIntent.cs, BlockConfig.cs,
          BlockConfigKind.cs, RejectReason.cs (54, 57)
  Simulation/OperationApplier.cs (36, 37, 40, 44, 49, D12, D13), UndoLog.cs (wpisy nowych składowych), IntentValidator.cs (28, 29, 22, 23,
             BucketFor), SurvivalRules.cs (BlockContentTable w konstruktorze; postawienie/rozbiórka skrzyń i łóżek, zasięg skrzyń),
             FunctionalBlockRules.cs (wewnętrzna: Toggle z kolumną drzwi, Sleep/SetRespawn, config, quick stack, take all),
             ContainerReach.cs (publiczna, statyczna: odległość oko → środek komórki, wspólna z serwerem), GameRules.cs (D23),
             ServerSimulation.cs (przekazanie BlockContentTable do SurvivalRules)
  Items/ContainerStore.cs (indeks gospodarz × rodzaj, Remove), Container.cs (Configure)
  World/WorldState.cs (BlockEntities, Progress), World/Blocks/BlockFunction.cs (ContainerSlots, fabryki), BlockFunctionKind.cs (11–13)
  Checkpoints/BlockEntityRecord.cs, PlayerProgressRecord.cs, ChangeTracker.cs, WorldCheckpoint.cs, WorldStateBuilder.cs
  Hashing/StateHasher.cs (D22)
  Serialization/OperationCodec.cs, IntentCodec.cs (22, 23, 28, 29), MessageCodec.cs (46), StateCodec.cs (bloby 21, 22), BlobKind.cs,
                CoreCodecPrimitives.cs (BlockEntityState, BedRef)
  Protocol/MessageType.cs (46), MessageChannels.cs, ProtocolConstants.cs (15, limity), Messages/BlockEntityStates.cs, Messages/BlockEntityEntry.cs
  Replication/ConstructReplica.cs (TrySetFunctionState)
src/Horyzont.Constructs/
  Content/BlockTypeCatalog.cs (chest, bed, light), BlockTypeContent.cs (parametry światła), BuiltInShapeMeshes.cs (builtin:door_open)
  Meshing/BlockRenderInfo.cs, BlockRenderTable.cs (rodzaj funkcji), ChunkMesher.cs (otwarte drzwi, komórki lamp), ChunkMeshData.cs (LightCells)
  Sources/ConstructDirtyChunks.cs (OperationConstructs, operacja 49)
src/Horyzont.Persistence/
  Sql.cs, WorldLoader.cs (block_entities, player_progress), WorldCreator.cs (odrzuca świat ze stanami bloków i postępem, jak z kontenerami),
  Writing/PersistenceWriter.cs (upsert/usuwanie wierszy w transakcji checkpointu)
src/Horyzont.Net/Session/MessageRouter.cs (46 od serwera w tych samych stanach co ConstructState)
src/Horyzont.Server/
  Inventory/InventoryService.cs (subskrypcje skrzyń, sprawdzanie co 10 ticków, delty do subskrybentów, ContainerState po 37, odmowa po 36),
            InventoryLog.cs
  Replication/BlockEntityReplicator.cs (nowy, D15), ConstructStateSender.cs (zdarzenie LinkLive), EntityReplication.cs (rzut D14),
              EntityReplicationBinder.cs (rejestracja replikatora), ReplicationLog.cs
src/Horyzont.Client/
  Blocks/ClientBlockEntities.cs, BlockInteraction.cs, BlockInteractionResult.cs, BlockPrompt.cs
  Inventory/ClientInventory.cs (otwieranie i zamykanie cudzych kontenerów, zdarzenie odmowy), InventoryInteraction.cs (skrzynia jako źródło/cel),
            ChestSession.cs
  Replication/ConstructMirror.cs (operacja 49 i złożona z 49), ClientReplication.cs (46 → ClientBlockEntities)
game/
  content/blocks/blocks.json (D5), locale/blocks.csv (nazwy), locale/items.csv (INV_CHEST_*, INV_QUICK_STACK, INV_TAKE_ALL, INV_ACCESS_*),
  locale/tools.csv (REJECT_54, REJECT_57, PROMPT_*), project.godot (akcja block_interact = E)
  src/Constructs/ConstructWorld.cs (światła chunków), BlockMeshFactory.cs (siatka builtin:door_open), BlockContentLoader.cs
  src/Client/Blocks/BlockLights.cs (limit 32, D7), BlockPromptHud.cs
  src/Client/Tools/ToolInputReader.cs (block_interact obok ship_mode_toggle), ToolNode.cs (wywołanie BlockInteraction)
  src/Client/Inventory/InventoryScreen.cs (panel skrzyni, przyciski), ChestPanel.cs, InventoryNode.cs
  src/Boot/GameComposition.Blocks.cs (nowa część: ClientBlockEntities, BlockInteraction, BlockEntityReplicator, BlockLights)
tests/  (nazwy w „Testy akceptacyjne”)
```

Nazwy nowych plików są propozycją; istniejące ścieżki sprawdzono w repozytorium. Pomocnicze typy (`BlockInteractionResult`, `ChestSession`) wykonawca może scalić z sąsiednimi, jeśli plik miałby kilka linii.

### Kontrakty Core (format 1)

| Kod | Typ | Pola w kolejności kodeka | Warunki wstępne appliera |
| --- | --- | --- | --- |
| op 36 | `ContainerRemoveOp(ContainerId Container)` | varuint | kontener istnieje; wszystkie sloty puste; gospodarz nie jest `Player` (kontenery gracza nie znikają; spec milczy — warunek ostrzejszy, do przeglądu) → usunięcie, zwolnienie indeksu gospodarza, `ContainerChange(…, Removed: true)` |
| op 37 | `ContainerConfigureOp(ContainerId Container, ushort Size, ContainerAccess Access)` | varuint, varuint, bajt | kontener istnieje; rozmiar 1…256; sloty o indeksie ≥ `Size` puste; rozmiar albo dostęp różny od obecnego (D19: bez sprawdzania aktora) → wersja +1 |
| op 40 | `BlockEntitySetOp(EntityId Construct, CellPos Cell, ushort ExpectedType, BlockEntityState? State)` | varuint, `CellPos`, varuint, bajt rodzaju (0 = brak stanu), pola rodzaju: `Chest` varuint kontener; `Bed` varuint właściciel (0 = brak) | niepusty stan: w widoku efektywnym (D12) budowla istnieje, komórka ma blok typu `ExpectedType`; brak stanu w tej komórce albo stan tego samego rodzaju; `Chest`: kontener istnieje, rodzaj `Chest`, gospodarz = ta komórka; `Bed`: właściciel `None` albo znany gracz; liczba stanów budowli ≤ 4096. Pusty stan: na stanie żywym blok typu `ExpectedType` i stan istnieje → usunięcie |
| op 44 | `RespawnPointSetOp(PlayerId Player, BedRef? Bed)` | varuint, bajt (0/1), [varuint budowla, `CellPos`, varuint typ] | gracz znany; z łóżkiem: w widoku efektywnym blok typu `ExpectedType` ze stanem `Bed`; brak łóżka czyści punkt |
| op 49 | `BlockFunctionStateSetOp(EntityId Construct, CellPos Cell, ushort ExpectedType, byte State)` | varuint, `CellPos`, varuint, bajt | budowla istnieje; komórka ma blok typu `ExpectedType`; `State` < 8 → `WithFunctionState`, wersja budowli +1 (w złożonej: raz na budowlę, D12), `ConstructChange` |

- Zapis z wartością spoza zakresu (rodzaj stanu 3–7, `State` ≥ 8, zarezerwowany wariant) → `ArgumentException`; odczyt → `ProtocolViolationException` (wzór K6-29W).
- Blob `BlockEntity` (21), wersja 1: budowla, komórka, rodzaj, pola rodzaju (jak w operacji 40). Wiersz `block_entities` o kluczu innym niż w blobie → `WorldCorruptedException`.
- Blob `PlayerProgress` (22), wersja 1: D11. Wiersz `player_progress` o `player_id` innym niż w blobie → `WorldCorruptedException`.
- Intencja 22 `QuickStackIntent()` — bez pól. Intencja 23 `TakeAllIntent(ContainerId Container)`. Intencja 28 `BlockInteractIntent(EntityId Construct, CellPos Cell, BlockInteractAction Action)` — bajt akcji 1–3. Intencja 29 `BlockConfigIntent(EntityId Construct, CellPos Cell, BlockConfig Config)` — bajt wariantu (1: napis UTF-8 z długością varuint, 1–32 B; 2: bajt dostępu).
- Komunikat 46 `BlockEntityStates(EntityId Construct, IReadOnlyList<BlockEntityEntry> Entries)` — varuint budowla, varuint liczba (0…4096), wpisy (`CellPos`, bajt rodzaju, pola rodzaju), niezawodny uporządkowany.
- `ContainerChange` dostaje pozycyjny `bool Removed` na końcu (jedyne miejsce tworzenia: applier). `InventoryService` przy `Removed` kończy subskrypcje kontenera (`ContainerLocked`), a po operacji 37 wysyła subskrybentom pełny `ContainerState` zamiast delty (delta nie niesie rozmiaru ani dostępu).

### Walidacja i rozwiązanie intencji

Kolejność ogólna z `IntentValidator.Validate` bez zmian (gracz → online → parametry → początek promienia → kubełek → walidacja rodzaju); reguły Przetrwania w `SurvivalRules`/`FunctionalBlockRules` po walidacji ogólnej, jak w K6-29W.

| Intencja | Sprawdzenia (kolejność = kod) | Wynik |
| --- | --- | --- |
| `PlaceBlockIntent` z typem `Chest`/`Bed`/`Light` | walidacja K2/K5 bez zmian; bez treści Przetrwania i typ `Chest`/`Bed` → `InvalidParameters` (D21); limit stanów budowli (D23) → `InvalidParameters` | `Chest`: złożona D8; `Bed`: złożona D10; `Light`: zwykły `BlockSetOp`/`ConstructCreateOp` z `FunctionState` 1 (D7) |
| `RemoveBlockIntent` komórki ze stanem albo kontenerem | walidacja K2/K5; bez treści → `InvalidParameters`; skrzynia prywatna i nie właściciel → `NotOwner`; kontener niepusty → `ContainerNotEmpty` | złożona D8 albo D10 |
| `BlockInteractIntent` `Toggle` | D18; funkcja `Door`/`Light`; drzwi: kolumna (D6), zamknięcie na graczu → `IntersectsPlayer` | `BlockFunctionStateSetOp` albo złożona z 49 |
| `BlockInteractIntent` `Sleep`/`SetRespawn` | D18; bez treści → `InvalidParameters`; funkcja `Bed`, stan `Bed`; właściciel inny → `NotOwner` | złożona D10 |
| `BlockConfigIntent` | D18; `TeleportName` → `InvalidParameters`; nie właściciel → `NotOwner`; ten sam dostęp → `InvalidParameters` | `ContainerConfigureOp` |
| `ItemMoveIntent` z kontenerem skrzyni | jak K6-29W, a dostęp do kontenera wg D9 (`ContainerLocked`, `OutOfReach`) | `ItemTransferOp` / `ItemSwapOp` / złożona przeniesień |
| `QuickStackIntent` | bez treści → `InvalidParameters`; kubełek `Items` | złożona przeniesień (D16) albo `NotEnoughItems` / `InventoryFull` |
| `TakeAllIntent` | bez treści, kontener nie `Chest` → `InvalidParameters`; dostęp D9; kubełek `Items` | złożona przeniesień (D17) albo `InventoryFull` |

Zasoby treści i terenu czyta tylko walidator i resolver; każda wartość (rozmiar skrzyni, limit stosu, stan początkowy lampy, identyfikatory) trafia do operacji (ZASADY §4). Identyfikatory nowych kontenerów i budowli przydziela resolver z bieżących alokatorów, jak `InventoryService.CreateContainers` i `IntentResolver` dla `ConstructCreateOp`.

### Przepływ intencja → walidator → operacja → replikacja → klient

```
Postawienie skrzyni
  klient: narzędzie budowy, typ core:chest_wood → PlaceBlockIntent (bez zmian formatu)
  serwer: IntentValidator (K2/K5) → SurvivalRules: funkcja Chest
          → CompoundOp[ContainerCreateOp, BlockEntitySetOp(Chest), BlockSetOp|ConstructCreateOp]
          → OperationApplier.TryApply (D12) → dziennik → Applied:
             EntityReplication: OperationApplied{ rzut = BlockSetOp|ConstructCreateOp } (D14)
             BlockEntityReplicator: BlockEntityStates(budowla) do graczy z łączem Live (D15)
             ConstructWorld (host): przebudowa chunku przez OperationConstructs
  klient: ConstructMirror (blok, ConstructPrediction potwierdza) → ClientBlockEntities (stan Chest z ContainerId)

Otwarcie skrzyni i przeniesienie
  klient: E na skrzyni → BlockInteraction: stan Chest → ContainerSubscribe(id)
  serwer: InventoryService: dostęp, ≤ 6 m → ContainerState (albo odmowa ContainerLocked / OutOfReach)
  klient: ekran skrzyni obok plecaka; przeciągnięcie → ItemMoveIntent (oczekiwana zawartość źródła)
  serwer: walidacja D9 → ItemTransferOp → ContainerDelta do wszystkich subskrybentów; > 8 m → odmowa OutOfReach, koniec subskrypcji
  klient: zamknięcie ekranu → ContainerUnsubscribe(id)

Drzwi i lampa
  klient: E → BlockInteractIntent(Toggle)
  serwer: walidacja D18 → kolumna drzwi (D6) → BlockFunctionStateSetOp albo CompoundOp[49…]
          → OperationApplied (rzut = ta sama operacja) → wszyscy z łączem Live
  klient/host: ConstructMirror.TrySetFunctionState → ConstructDirtyChunks → ChunkMesher: kolizja i panel drzwi, komórki lamp → światła

Łóżko
  klient: E na łóżku → BlockInteractIntent(Sleep)
  serwer: CompoundOp[BlockEntitySetOp(stare łóżko → wolne)?, BlockEntitySetOp(łóżko → gracz)?, RespawnPointSetOp] → BlockEntityStates
  klient: podpowiedź „Twoje łóżko” z właściciela w ClientBlockEntities (bez komunikatu 48, DO USTALENIA-7)

Szybkie odkładanie
  klient: przycisk w ekranie ekwipunku/skrzyni → QuickStackIntent
  serwer: D16 → CompoundOp[ItemTransferOp…] → ContainerDelta do subskrybentów (właściciel plecaka zawsze; skrzynie — kto je otworzył)
```

### Interfejs użytkownika (tylko to, co pakiet musi pokazać)

Styl i komponenty z K6-29W (`HudTheme`, `SlotView`, `ItemTooltip`); wygląd zatwierdza Dismond na zrzutach przed scaleniem.

- **Podpowiedź interakcji** (środek ekranu pod celownikiem, `BlockPromptHud`): „[E] Otwórz skrzynię” / „Skrzynia prywatna” (zablokowana, bez akcji, gdy klient zna odmowę), „[E] Otwórz drzwi” / „[E] Zamknij drzwi”, „[E] Włącz światło” / „[E] Wyłącz światło”, „[E] Śpij (ustaw punkt odrodzenia)” / „Twoje łóżko” / „Łóżko zajęte”. Tylko dla bloku w zasięgu `ReachMeters` pod celownikiem (ten sam promień z oka co narzędzia, `AimRayBuilder.EyeFromFeet`).
- **Ekran skrzyni**: otwarcie skrzyni otwiera ekran ekwipunku z panelem skrzyni nad siatką plecaka (siatka 8 kolumn: 20 miejsc = 3 rzędy z niepełnym ostatnim, 40 = 5 rzędów), nagłówek z nazwą typu bloku i stanem dostępu; przyciski „Weź wszystko” (23), „Odłóż pasujące” (22) i — tylko dla właściciela — przełącznik „Wspólna / Prywatna” (29). Przenoszenie myszą jak w K6-29W (LPM, PPM, Shift+klik do „dowolnego” w drugim kontenerze). Esc / Tab / I / oddalenie (odmowa `OutOfReach`) zamyka panel i wysyła `ContainerUnsubscribe`.
- **Ekran ekwipunku bez skrzyni**: przycisk „Odłóż pasujące do skrzyń w pobliżu” (22).
- **Odmowy**: istniejący komunikat odmowy w HUD z tekstami PL/EN dla 54 („To nie twoje”) i 57 („Skrzynia nie jest pusta”).
- **Świat**: otwarte drzwi jako panel bez kolizji, światło lampy. Bez animacji (K8-50).

## Testy akceptacyjne (napisz najpierw)

Pisze Sonnet 5.5 przed kodem (ZASADY §8); testy etapów A i B zatwierdza Opus 5.5 przed implementacją (krok A1, B1). Pomocnicze w `tests/Horyzont.Testing`: `SurvivalContentFactory.CreateTest()` dostaje typy bloków testowych (skrzynia 4 miejsca, skrzynia 8 miejsc, łóżko, lampa, drzwi) w tabeli `BlockContentTable` testów; `WorldFactory.CreateWithFunctionalBlocks()` (budowla z kolumną 2 drzwi, lampą, skrzynią i łóżkiem); `FunctionalBlockFixture` (stawianie przez intencje); generatory `CodecGen` dla nowych operacji, intencji i komunikatu (X6).

### Etap A — model, operacje, kodeki, skrót (Core)

| Test | Kryterium |
| --- | --- |
| `BlockEntities/BlockEntityOpsApplierTests`: `BlockEntitySet_OnBlockOfExpectedType_StoresState`, `BlockEntitySet_WrongExpectedType_Fails`, `BlockEntitySet_EmptyCell_Fails`, `BlockEntitySet_KindChange_Fails`, `BlockEntitySet_ChestWithForeignContainer_Fails`, `BlockEntitySet_EmptyStateWithoutEntity_Fails`, `BlockEntitySet_EmptyState_RemovesEntity`, `BlockEntitySet_Over4096PerConstruct_Fails` | tabela kontraktów; błąd nie zmienia skrótu |
| `BlockEntities/RespawnPointOpsTests`: `RespawnPointSet_OnBed_SetsProgress`, `RespawnPointSet_CellWithoutBedState_Fails`, `RespawnPointSet_None_ClearsPoint`, `RespawnPointSet_UnknownPlayer_Fails` | |
| `BlockEntities/FunctionStateOpsTests`: `FunctionStateSet_SetsBits25To27_VersionPlusOne`, `FunctionStateSet_WrongExpectedType_Fails`, `FunctionStateSet_UnknownConstruct_Fails` | |
| `Items/ContainerLifecycleTests`: `ContainerCreate_ConstructBlockHost_Succeeds`, `ContainerCreate_ConstructBlockHostEmptyCell_Fails`, `ContainerCreate_SecondOfKindOnSameBlock_Fails`, `ContainerRemove_Empty_RemovedAndHostFreed`, `ContainerRemove_NotEmpty_Fails`, `ContainerRemove_PlayerContainer_Fails`, `ContainerConfigure_Access_VersionPlusOne`, `ContainerConfigure_ShrinkOverItems_Fails`, `ContainerConfigure_NoChange_Fails` | |
| `Items/CompoundConstructTests`: `PlaceChestOnTerrain_Compound_CreatesConstructContainerAndEntity`, `PlaceChestOnFace_Compound_ConstructVersionPlusOneOnce`, `ConstructComponentNotLast_Fails`, `TerrainAndBlockInOneCompound_Fails`, `FunctionStateWithBlockComponent_Fails`, `RemoveChest_Compound_ClearsContainerEntityAndBlock`, `RemoveLastBlockWithEntity_Compound_RemovesConstruct`, `BlockClear_WithBlockEntity_Fails`, `BlockClear_WithHostedContainer_Fails`, `FailingItemComponent_ConstructAndEntitiesUntouched`, `DoorColumnCompound_TwoCells_OneVersionBump` | D12, D13; przy błędzie skrót przed = po |
| `BlockEntities/FunctionalBlockConservationProperties` (FsCheck, 2000 sekwencji po 50 operacji: postawienie/rozbiórka skrzyń, przeniesienia do i ze skrzyń, złożone z losowym błędem) | po każdej operacji: suma sztuk każdego typu = Σ create − Σ destroy; brak kontenera z gospodarzem `ConstructBlock` bez bloku; brak stanu bloku bez bloku |
| `Operations/OperationConstructsTests`: `ConstructPart_PlaceChestCompound_ReturnsBlockSetOnly`, `ConstructPart_ItemOnlyCompound_False`, `ConstructPart_DoorColumn_CompoundOfFunctionStates`, `ConstructPart_TerrainCompound_False` | D14 |
| `Serialization/OperationCodecBlockTests`: round-trip 36, 37, 40 (`Chest`, `Bed`, brak stanu), 44 (z łóżkiem i bez), 49; `ReservedBlockEntityKind_Read_Throws`, `FunctionStateAbove7_Write_Throws`, `CompoundWithNewComponents_RoundTrip` | zapis `ArgumentException`, odczyt `ProtocolViolationException` |
| `Serialization/StateCodecBlockEntityTests`: blob 21 round-trip obu rodzajów; blob 22 v1 round-trip; nieznana wersja bloba → wyjątek | |
| `Hashing/SurvivalHashTests` (dopisane): `WorldWithItemsWithoutBlockEntities_HashUnchanged` (stała zapisana w A0), `BlockEntityChange_ChangesHash`, `RespawnPointChange_ChangesHash`, `FunctionStateChange_ChangesHash` | D22; istniejący `WorldWithoutItems_HashUnchanged` bez zmian |
| `Checkpoints/BlockEntityCheckpointTests`: `Capture_ChangedBlockEntity_Included`, `Capture_RemovedBlockEntity_ListedAsRemoved`, `Capture_ChangedProgress_Included`, `Requeue_MarksBlockEntitiesAndProgressAgain`, `Builder_RestoresBlockEntitiesAndProgress`, `Builder_EntityOnEmptyCell_Throws`, `Builder_ContainerHostOnEmptyCell_Throws`, `CheckpointPlusJournal_SameHash` | |
| `Replication/ConstructReplicaTests` (dopisane): `TrySetFunctionState_NextVersion_Applies`, `TrySetFunctionState_StaleVersion_Ignored` | |
| Bez zmian w plikach: `TerrainJournalGoldenCorpusTests`, korpus `Golden/TerrainJournals`, `DeterminismTests` i wszystkie dotychczasowe testy (poza fixture X6 i złotymi wartościami enumów) | `git diff <commit startu A>..HEAD` na ścieżkach korpusu i tych testów pusty |

### Etap B — intencje, treść, protokół (Core, Constructs, Net)

| Test | Kryterium |
| --- | --- |
| `Simulation/BlockInteractIntentTests`: `Toggle_ClosedDoor_FunctionStateOpOpens`, `Toggle_DoorColumnOfTwo_CompoundOfTwo`, `Toggle_DoorColumnOfFour_TogglesThreeFromAimed`, `Toggle_DoorClosingOnPlayer_IntersectsPlayer`, `Toggle_Lamp_SwitchesOff`, `Toggle_PlainBlock_InvalidParameters`, `Toggle_OutOfReach_OutOfReach`, `Toggle_BehindWall_NoLineOfSight`, `Toggle_WithoutSurvivalContent_Applies`, `Sleep_FreeBed_ClaimsAndSetsRespawn`, `Sleep_OwnBedAgain_RespawnOnly`, `Sleep_NewBed_ReleasesOldBed`, `SetRespawn_BedOfOther_NotOwner`, `Sleep_OnDoor_InvalidParameters`, `Sleep_WithoutSurvivalContent_InvalidParameters`, `Burst_BlocksBucket_RateLimited` | D6, D7, D10, D18, D21 |
| `Simulation/ChestIntentTests`: `PlaceChest_OnTerrain_CompoundWithContainerAndEntity`, `PlaceChest_OnConstructFace_CompoundBlockSetLast`, `PlaceChest_SizeFromContent_CarriedInOp`, `PlaceLamp_InitialStateOn`, `PlaceChest_WithoutSurvivalContent_InvalidParameters`, `RemoveChest_Empty_Compound`, `RemoveChest_NotEmpty_ContainerNotEmpty`, `RemovePrivateChest_NotOwner_NotOwner`, `RemoveBed_OwnerRespawnCleared`, `MoveBagToChest_InRange_Transfer`, `MoveToChest_Beyond8m_OutOfReach`, `MoveToPrivateChestOfOther_ContainerLocked`, `ConfigureAccess_Owner_ContainerConfigureOp`, `ConfigureAccess_NotOwner_NotOwner`, `ConfigureAccess_Same_InvalidParameters`, `ConfigureTeleportName_InvalidParameters` | D8, D9, D18 |
| `Simulation/QuickStackIntentTests`: `QuickStack_MatchingTypes_NearestChestFirst`, `QuickStack_TopsUpStacksThenEmptySlots`, `QuickStack_HotbarUntouched`, `QuickStack_ChestBeyond10m_Ignored`, `QuickStack_PrivateChestOfOther_Ignored`, `QuickStack_NoMatchingItems_NotEnoughItems`, `QuickStack_ChestsFull_InventoryFull`, `QuickStack_SameState_SameOperation` | D16 |
| `Simulation/TakeAllIntentTests` (gdy DO USTALENIA-2 = tak): `TakeAll_Chest_MovesToHotbarStacksThenBag`, `TakeAll_PartialRoom_MovesWhatFits`, `TakeAll_NoRoom_InventoryFull`, `TakeAll_PrivateChestOfOther_ContainerLocked` | D17 |
| `Simulation/BedOwnershipProperties` (FsCheck, 1000 sekwencji Sleep/SetRespawn/rozbiórek łóżek przez 4 graczy) | po każdej operacji: punkt odrodzenia P = łóżko B ⇔ właściciel B = P |
| `Simulation/IntentValidationTests.AllReasons` (dopisane 54, 57) | każdy nowy kod ma test walidacji |
| `Serialization/IntentCodecBlockTests`: round-trip 22, 23, 28 (trzy akcje), 29 (oba warianty); `TeleportNameOver32Bytes_Write_Throws`, `UnknownAction_Read_Throws`, `UnknownConfigVariant_Read_Throws` | |
| `Serialization/MessageCodecBlockEntityTests`: round-trip 46 (pusty, oba rodzaje); 4097 wpisów → wyjątek | |
| `Protocol/ProtocolContractTests` (istniejące): `ProtocolVersion_MatchesDocumentedMergeValue` = 15, `Constants_GoldenValues` (limity D23), `Enums_GoldenValues` (op 36, 37, 40, 44, 49; odmowy 54, 57; komunikat 46; bloby 21, 22; `BlockEntityKind`, `BlockInteractAction`, `BlockConfigKind`), lista rekordów komunikatów; `MaxMessages.All()`, `CodecGen` | X6 |
| `Net.Tests/MessageRouterTests` (dopisane) | 46 dozwolony od serwera w tych samych stanach co `ConstructState` (sprawdzić w `MessageRouter` przy B2), niedozwolony od klienta |
| `Constructs.Tests/Content/BlockTypeCatalogTests` (dopisane): `Parse_RepoContent_HasChestBedLampDoor`, `Parse_ChestWithoutSlots_Throws`, `Parse_ChestSlotsOver256_Throws`, `Parse_LightWithInvalidColor_Throws` | `InvalidDataException` ze ścieżką JSON |
| `Constructs.Tests/Sources/ConstructDirtyChunksTests` (dopisane): `Collect_FunctionStateSet_DirtiesCell`, `Collect_CompoundWithBlockSet_DirtiesCell`, `Collect_ItemOnlyCompound_Nothing` | D14 |
| `Constructs.Tests/Meshing/ChunkMesherTests` (dopisane): `OpenDoor_NoCollision_DrawsOpenPanel`, `ClosedDoor_Collides`, `LitLamp_ListedInLightCells`, `UnlitLamp_NotListed` | D6, D7 |

### Etap C — zapis i serwer

| Test | Kryterium |
| --- | --- |
| `Persistence.Tests/BlockEntityPersistenceTests`: `Checkpoint_WritesBlockEntitiesAndProgress_LoadRestores`, `Checkpoint_RemovedBlockEntity_DeletesRow`, `Checkpoint_ClearedRespawn_DeletesRow`, `CorruptBlockEntityBlob_WorldCorrupted`, `BlockEntityRowKeyMismatch_WorldCorrupted`, `ProgressRowPlayerMismatch_WorldCorrupted` | |
| `Persistence.Tests/MigrationTests`: istniejące `FixtureWorldV1_MigratesToLatest_CreatesSurvivalTables` i `FixtureWorldV3_CopiedToTemp_LoadsAndReplays` bez zmian i zielone; nowy `FixtureWorldV3Blocks_CopiedToTemp_LoadsAndReplays` | `user_version` nadal 3 (D3) |
| `Server.Tests/Inventory/ChestSubscriptionTests`: `Subscribe_SharedChestWithin6m_SendsState`, `Subscribe_Beyond6m_OutOfReach`, `Subscribe_PrivateChestOfOther_ContainerLocked`, `WalkAwayBeyond8m_ServerEndsSubscription`, `Delta_SentToAllSubscribers`, `ChestRemoved_SubscribersGetLocked`, `SwitchToPrivate_NonOwnerSubscriptionsEnded`, `Configure_SubscribersGetFullState`, `Disconnect_SubscriptionDropped`, `UnsubscribeChest_StopsDeltas` | D20 |
| `Server.Tests/Replication/BlockEntityReplicationTests`: `LinkLive_SendsStatesAfterConstructState`, `BlockEntitySet_SendsStatesToLiveRecipientsOnly`, `TransferringRecipient_GetsStatesOnLinkLive`, `PlaceChest_OperationAppliedCarriesBlockSetOnly`, `DoorToggle_OperationAppliedForwarded_NoResync` | D14, D15 |
| `Integration.Tests/Blocks/FunctionalBlocksFlowTests` (klient → `InMemoryTransport` → sesja → symulacja → writer): `PlaceChest_MoveItems_Restart_ItemsAndChestIdentical`, `BedSleep_Restart_RespawnPointKept`, `DoorAndLamp_Toggle_Restart_StateKept`, `TwoClients_DoorToggle_BothSeeOpen`, `TwoClientsSameChest_RandomMoves_NoDuplication` (2 × 500 intencji, `FakeTimeProvider`), `CrashMidChestPlacement_WholeOrNothing` (oba punkty awarii, wzór K6-29W), `QuickStack_EndToEnd_DeltasToSubscribers` | odbiór spec: „działają i przetrwają restart”; skrót po powtórce = skrót przed |
| `game/tests/Blocks/FunctionalBlocksTests` (gdUnit4, prawdziwy `ConstructWorld`) | otwarte drzwi nie mają kształtu kolizji w komórce, zamknięte mają; włączona lampa ma `OmniLight3D`, wyłączona nie; ponad 32 lampy → 32 światła najbliżej kamery |

### Etap D — klient

| Test | Kryterium |
| --- | --- |
| `Client.Tests/Blocks/ClientBlockEntitiesTests`: `States_ReplaceConstructMap`, `EmptyStates_ClearsMap`, `ConstructRemoved_ClearsMap`, `NewConstructState_ClearsMap` | D15 |
| `Client.Tests/Blocks/BlockInteractionTests`: `AimDoor_PromptOpen_SendsToggle`, `AimLamp_PromptSwitchOff`, `AimFreeBed_SendsSleep`, `AimBedOfOther_PromptOccupied_SendsNothing`, `AimChest_SendsSubscribe`, `AimPlainBlock_NoPrompt`, `PendingFull_Waiting`, `OutOfReach_NoPrompt` | wzór `ShipModeSwitch` |
| `Client.Tests/Inventory/ChestSessionTests`: `Open_StateStored_Opened`, `RejectedLocked_PromptLocked`, `ServerOutOfReach_ClosesPanel`, `Close_SendsUnsubscribe`, `MoveBagToChest_IntentCarriesExpectedSource`, `ShiftClickInChest_MovesAnyToBag`, `QuickStackButton_SendsIntent`, `TakeAllButton_SendsIntent`, `AccessToggle_OwnerOnly` | |
| `Client.Tests/Replication/ConstructMirrorFunctionStateTests`: `FunctionStateOp_AppliesToReplica`, `DoorColumnCompound_Applies`, `FunctionStateGap_RequestsResync` | D14 |
| `game/tests/Inventory/InventoryUiTests` (dopisane, gdUnit4, bez grafiki) | E na skrzyni otwiera ekran z panelem skrzyni i zwalnia mysz; Esc zamyka i wysyła `ContainerUnsubscribe`; podpowiedź pokazuje tekst z CSV; przycisk dostępu widoczny tylko dla właściciela |
| Ręcznie (kategoria `Manual`, wynik i zrzuty do PR, `docs/reports/k6-30/`) | solo: postawienie obu skrzyń, przeniesienie, „Weź wszystko”, „Odłóż pasujące”, prywatna/wspólna; drzwi z dwóch bloków otwierają się i przepuszczają postać; lampa świeci i gaśnie; sen na łóżku daje „Twoje łóżko”; restart zachowuje wszystko. Zrzuty: ekran skrzyni, podpowiedzi, otwarte i zamknięte drzwi, lampa w nocy — do oceny Dismonda przed scaleniem |

## Kroki

Każdy krok kończy się zielonym budowaniem i testami; commity w formacie Conventional Commits, małe.

### Etap A — model, operacje, kodeki, skrót (Core)

- [x] A0. Zapisz `StateHasher.Hash` świata K6-29W z przedmiotami (`WorldFactory.CreateWithPlayerContainers()` + zestaw startowy) z bieżącego commita jako stałą testu `WorldWithItemsWithoutBlockEntities_HashUnchanged`; zanotuj commit startowy etapu (do `git diff` korpusu).
- [x] A1 (testy napisane, czerwone; **czeka na przegląd Opus 5.5 przed A2**). Testy z tabeli etapu A (czerwone, budowa może nie przechodzić do A2). **Przegląd Opus 5.5 przed kodem:** kontrakty operacji 36, 37, 40, 44, 49, D12, D13, D19, bloby 21/22, D22 oraz rozstrzygnięcie DO USTALENIA-1, 3, 5, 6, 7, 9. Commit `test(core): add functional block contract tests`.
- [x] A2. Typy: `BlockRef`, `BlockEntityKind`, `BlockEntityState` z rodzajami, `BlockEntityStore`, `PlayerProgress`, `BedRef`, `PlayerProgressStore`; magazyny w `WorldState`; indeks gospodarz × rodzaj i `Remove` w `ContainerStore`. Commit `feat(core): add block entity and player progress state`.
- [x] A3. Operacje 36, 37, 40, 44, 49 w applierze z `UndoLog`; reguły złożonej D12 (widok efektywny, składowa ostatnia, wersja budowli raz w `Finish`); D13; `ContainerChange.Removed`. Testy applier i złożonej zielone. Commit `feat(core): add block entity, respawn and function state operations`.
- [x] A4. Kodeki operacji, bloby 21 i 22, rekordy checkpointu, `ChangeTracker`, `WorldStateBuilder`, `CaptureCheckpoint`/`Requeue`. Commit `feat(core): encode and checkpoint block entities`.
- [x] A5. `StateHasher` (D22) i test A0 zielony. Commit `feat(core): hash block entities and respawn points`.
- [x] A6. `OperationConstructs` (D14), `ConstructReplica.TrySetFunctionState`. Commit `feat(core): project compound operations for construct views`.
- [x] A7. Testy właściwości zielone; sprawdzenie mutacją (usunięty warunek D13 → `BlockClear_WithHostedContainer_Fails` i test właściwości czerwone); korpus i `DeterminismTests` bez zmian w plikach.

### Etap B — intencje, treść, protokół

- [x] B1 (testy napisane, czerwone; **czeka na przegląd Opus 5.5 przed B2**). Testy z tabeli etapu B (czerwone); przegląd Opus kontraktów intencji i komunikatu 46 oraz DO USTALENIA-2, 4, 8, 10, 11. Commit `test(core): add functional block intent tests`.
- [x] B2. `RejectReason` 54, 57; intencje 22, 23, 28, 29; `IntentCodec`; komunikat 46, `MessageCodec`, `MessageChannels`, `MessageRouter`; fixture X6; `ProtocolVersion` = 15 i jego testy. Commit `feat(protocol): add block entity states and functional block intents`.
- [x] B3. Treść: `BlockFunctionKind` 11–13, `BlockFunction.ContainerSlots`, `BlockTypeCatalog`, `BlockTypeContent` (światło), nowe typy w `blocks.json`, materiał `lamp`, nazwy w `blocks.csv`. Commit `feat(content): add chests, a bed, a lamp and a wooden door`.
- [x] B4. `SurvivalRules` z `BlockContentTable`; `FunctionalBlockRules` (postawienie, rozbiórka, Toggle z kolumną, Sleep/SetRespawn, config, quick stack, take all); `ContainerReach`; walidator (28, 29, 22, 23; `BucketFor`; D21; D23); `GameRules`. Commit `feat(core): resolve chests, beds, doors, lamps and quick stacking`.
- [x] B5. `ConstructDirtyChunks` przez `OperationConstructs` i 49; `ChunkMesher` (otwarte drzwi, komórki lamp), `BuiltInShapeMeshes` (`builtin:door_open`). Commit `feat(constructs): mesh open doors and report lamp cells`.
- [x] B6. Sprawdzenie (niżej).

### Etap C — zapis i serwer

- [x] C1 (testy napisane, czerwone). Testy z tabeli etapu C (czerwone). Commit `test: add functional block persistence and server tests`.
- [x] C2. Persistence: `WorldLoader`, `PersistenceWriter`, `WorldCreator`, `Sql`; fikstura `world-v3-blocks.db` (`FixtureGenerator.Generate_WorldV3Blocks`, `Manual`). Commit `feat(persistence): store block entities and player progress`.
- [x] C3. `InventoryService`: subskrypcje skrzyń (D20), sprawdzanie co 10 ticków jako uczestnik ticku, delty do subskrybentów, `ContainerState` po 37, odmowa po 36. Commit `feat(server): subscribe to chests within reach`.
- [ ] C4. `BlockEntityReplicator`, `ConstructStateSender.LinkLive`, rzut w `EntityReplication`, rejestracja w `EntityReplicationBinder`. Commit `feat(server): replicate block entity states`.
- [ ] C5. Gra: `ConstructWorld` (przez `OperationConstructs`; światła chunków), `BlockLights` (limit 32), `BlockMeshFactory` (`builtin:door_open`), `GameComposition.Blocks.cs`. Commit `feat(game): show open doors and lamp lights`.
- [ ] C6. Sprawdzenie (z `dotnet test game` i `smoke` zgodnie z regułą wspólnego runnera).

### Etap D — klient (wygląd ogląda Dismond)

- [ ] D1. Testy z tabeli etapu D (czerwone). Commit `test(client): add block interaction and chest tests`.
- [ ] D2. `src/Horyzont.Client/Blocks/*`, `ClientInventory` (cudze kontenery), `ChestSession`, `InventoryInteraction` (skrzynia), `ConstructMirror` (49), `ClientReplication` (46). Commit `feat(client): open chests and interact with blocks`.
- [ ] D3. Akcja `block_interact` (E) w `project.godot`, `ToolInputReader`, `ToolNode`, `BlockPromptHud`. Commit `feat(game): add the interact key and block prompts`.
- [ ] D4. **Najpierw wygląd statyczny:** `ChestPanel`, przyciski i przełącznik dostępu w `InventoryScreen` — zrzuty ekranu skrzyni i podpowiedzi oceniane krytycznie, potem akceptacja Dismonda; dopiero potem dopracowanie zachowania. Commit `feat(game): add the chest panel to the inventory screen`.
- [ ] D5. Teksty PL/EN (`items.csv`, `tools.csv`, `blocks.csv`), import tłumaczeń (`--import`). Commit `feat(game): translate functional block texts`.
- [ ] D6. Test ręczny solo ze zrzutami w `docs/reports/k6-30/`, sprawdzenie, opis PR z odstępstwami i DO USTALENIA.

### Sprawdzenie na końcu każdego etapu

```
dotnet build Horyzont.sln
dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"
dotnet format Horyzont.sln --verify-no-changes
python tools/engine/build.py smoke          # etapy C i D
dotnet test game                            # etapy C i D (gdUnit4; reguła wspólnego runnera jak w K6-29W)
git diff <commit startu A>..HEAD -- tests/Horyzont.Core.Tests/Golden/TerrainJournals tests/Horyzont.Core.Tests/TerrainJournalGoldenCorpusTests.cs tests/Horyzont.Core.Tests/Hashing/DeterminismTests.cs   # pusty
git diff <commit startu A>..HEAD -- src/Horyzont.Persistence/Migrations   # pusty (D3)
```

## Ryzyka i odstępstwa

| Ryzyko | Skutek | Odpowiedź |
| --- | --- | --- |
| Nowa droga duplikacji lub zgubienia przy skrzyniach (dwóch graczy, rozbiórka, zmiana dostępu) | przedmiot w dwóch miejscach albo w żadnym | wszystko przez `TryApply`; warunek D13 jako obrona w applierze; test właściwości z `ConstructBlock`; test integracyjny dwóch klientów przy jednej skrzyni |
| Konsument rozpoznaje operacje budowli po typie i nie zna złożonej | host nie przebudowuje chunku po postawieniu skrzyni; klient dostaje złożoną z przedmiotami, robi resync; `ShipStatsCache` pomija zmianę | `OperationConstructs` w każdym konsumencie (`EntityReplication`, `ConstructDirtyChunks`, `ConstructMirror`), testy D14; reguła do ZASAD; przegląd grepem `BlockSetOp\|BlockClearOp\|ConstructCreateOp` w kroku C4 |
| Widok efektywny D12 rozjedzie się z kolejnością stosowania | składowa przyjęta przy powtórce inaczej niż na żywo | jedna ścieżka (`TryApply`) w `Submit`, `ApplyServerOperation` i `Replay`; test `CheckpointPlusJournal_SameHash` i test integracyjny z restartem |
| Zmiana skrótu światów K6-29W | fałszywy alarm w testach i raportach | podsekcje tylko gdy niepuste (D22), test A0 |
| `BlockEntityStates` przed `ConstructState` albo stan nieaktualny po transferze | klient bez stanu skrzyni/łóżka | wysyłka przy `LinkLive` i tylko do łącz `Live`; klient czyści mapę przy nowym pełnym stanie |
| Kolumna drzwi (D6) nie spodoba się właścicielowi | przeróbka resolvera i siatki | DO USTALENIA-3 rozstrzygane w A1; format operacji 49 i złożonej niezależny od decyzji |
| Wydajność świateł | spadek FPS w bazie z wieloma lampami | limit 32, bez cieni (D7); test gdUnit z 40 lampami |
| Równoległe pakiety K6 (K6-31, K6-33) zmieniają `SurvivalRules`, enumy i `StateHasher` | konflikty przy scaleniu | zakresy kodów rozdzielone; integrację robi pakiet scalany później (README); `ProtocolVersion` wg D4 |
| Ruchomy statek ze skrzynią | odległość liczona w innej ramie | `ContainerReach` przez `WorldState.TryToPlanetFrame` (ten sam region planety), test subskrypcji na budowli w trybie `Ship` |

Odstępstwa od spec 03 (do opisu PR):

- **[odstępstwo] D11:** `PlayerProgress` (blob 22) w wersji 1 ma tylko punkt odrodzenia; reszta pól w K6-33/K6-35 jako wersja 2.
- **[odstępstwo] DO USTALENIA-7:** komunikat 48 `PlayerProgressState` nie powstaje; klient zna swoje łóżko z właściciela w 46.
- **[odstępstwo] D10:** `Sleep` = `SetRespawn` (odpoczynek z K6-33).
- **[odstępstwo]** Postawienie skrzyni, łóżka, lampy i drzwi nic nie kosztuje, a rozbiórka nic nie zwraca (K6-36); zawartość niszczonego bloku nie wysypuje się (rozbiórka niepustej skrzyni jest odrzucana, zawalenia — K6-36).
- **Warunek ostrzejszy niż spec:** `ContainerRemoveOp` nie usuwa kontenera gracza; `BlockEntitySetOp` nie zmienia rodzaju istniejącego stanu i nie usuwa nieistniejącego.

## Odbiór (ZASADY §11)

- [ ] Testy akceptacyjne z tego planu przechodzą w CI (w tym `dotnet test game` i `smoke`); korpus złoty, `DeterminismTests` i migracje bez zmian w plikach.
- [ ] Przegląd Opus 5.5 w PR (zmiana kontraktów Core: operacje 36, 37, 40, 44, 49, D12, D13, intencje, bloby 21/22, komunikat 46, `ProtocolVersion`), uwagi rozwiązane; wygląd zaakceptowany przez Dismonda.
- [ ] Brak `TODO` bez odnośnika do zgłoszenia.
- [ ] Checklista kroków odhaczona; odstępstwa i rozstrzygnięcia DO USTALENIA opisane w sekcji „Odstępstwa i uwagi z realizacji” tego planu i w PR.
- [ ] Nowe zasady dopisane do `docs/ZASADY.md` (z Pułapek oznaczonych „do ZASAD”).
- [ ] Kryterium spec: skrzynie, łóżka, drzwi i światła działają i przetrwają restart (test integracyjny + test ręczny ze zrzutami).

## DO USTALENIA (zbiorczo, z propozycją)

| # | Kwestia | Propozycja |
| --- | --- | --- |
| 1 | Operacje 36, 37 i blob 22 z „reszty K6-29” | bierze K6-30 (D0, D11) |
| 2 | `TakeAllIntent` (23) w K6-30 | tak, tylko dla skrzyń (D17) |
| 3 | Drzwi wyższe niż blok i wygląd otwartych | kolumna do 3 bloków, panel `builtin:door_open` (D6) |
| 4 | Lampy: stan początkowy, liczba świateł | włączona po postawieniu; 32 światła bez cieni (D7) |
| 5 | Domyślny dostęp skrzyni, rozbiórka cudzej prywatnej | `Shared`; prywatną rozbiera tylko właściciel (D8) |
| 6 | Zasady łóżek | zajęcie snem lub wyborem, jedno łóżko na gracza, cudze → `NotOwner`, rozebrać może każdy (D10) |
| 7 | Komunikat 48 | pominięty w K6-30 |
| 8 | Zasięgi skrzyń i punkt pomiaru | 6 / 8 / 10 m od oka do środka komórki (D9) |
| 9 | Fikstura ze stanami bloków | nowa `world-v3-blocks.db`, `world-v3.db` bez zmian (D25) |
| 10 | Kody odmów szybkiego odkładania | brak pasujących → 41, pełne skrzynie → 40 (D16) |
| 11 | Liczby treści (masy, sloty, światło) | tabela D5, strojone bez zmiany kodu |

## Pułapki

- **Rozpoznawanie operacji budowli po typie.** Każde miejsce, które patrzy na `BlockSetOp`/`BlockClearOp`/`ConstructCreateOp`/`BlockFunctionStateSetOp`, używa `OperationConstructs.TryGetConstructPart`, bo operacje bywają składową `CompoundOp` (postawienie i rozbiórka bloków funkcyjnych, kolumna drzwi). (do ZASAD, §2, obok reguły `OperationTerrain`)
- **Składowa nieodwracalna ostatnia.** W `CompoundOp` co najwyżej jedna składowa terenu albo budowli, zawsze ostatnia; jej warunki sprawdza się przed pozostałymi, a warunki odwołujące się do jej komórki — w widoku efektywnym. (do ZASAD, §2)
- **Brak sierot.** `BlockClearOp` wymaga komórki bez stanu bloku i bez kontenera; stany bloków zmienia tylko operacja 40. (do ZASAD, §4: przykład obok limitów stosu)
- **Treść w applierze.** Rozmiar skrzyni, stan początkowy lampy i komórki kolumny drzwi wylicza resolver; applier ich nie liczy z `BlockContentTable`.
- **Rzut w `OperationApplied`.** Obcy klienci nie dostają składowych przedmiotów, stanów bloków ani punktu odrodzenia; stany bloków idą w 46.
- **Subskrypcje.** Koniec subskrypcji (oddalenie, prywatna, usunięcie) zawsze wysyła `ContainerState` z odmową, inaczej klient zostaje z otwartym panelem i odrzucanymi intencjami.
- **Kolejność w ticku.** `BlockEntityStates` po `OperationApplied` tej samej operacji; przy `LinkLive` po buforowanych operacjach.
- **Mysz i wejście.** Otwarty panel skrzyni to otwarty ekran ekwipunku: wejście ruchu, kamery, narzędzi i E zablokowane (K6-29W D18).
- **Tłumaczenia.** Nowe klucze w istniejących CSV wymagają importu (`--import`); bez tego etykiety pokażą klucze.
- **Rebase.** `ProtocolVersion` (D2), kolizje kodów z równoległymi pakietami K6 (tylko pliki enumów), numer migracji nie dotyczy (D3).

## Odstępstwa i uwagi z realizacji

### Przegląd A1 (Opus 5.5, 2026-10-16) — werdykt: GO z poprawkami

Przejrzane commity c3e6230 i 8ac8e0b (od b5e427e): testy w `tests/Horyzont.Core.Tests/{BlockEntities,Items,Checkpoints,Serialization,Hashing,Replication}/` i szkielety w `src/Horyzont.Core/{BlockEntities,Progress,Operations,Checkpoints,Serialization}/`. Przegląd był tylko odczytem plików (bez uruchamiania testów i bez `git diff`); czerwień testów zgłosił wykonawca. Poprawki z listy (4) wykonawca wprowadza w osobnym commicie `test(core): apply the A1 review` **przed** A2; nie wymagają ponownego przeglądu, chyba że zmienią kontrakt.

**(1) Zgodność z D0–D25 i kontraktami.** Testy odpowiadają kontraktom operacji 36, 37, 40, 44, 49 (warunki z tabeli, w tym ostrzejsze: `ContainerRemove_PlayerContainer_Fails`, `BlockEntitySet_KindChange_Fails`, `BlockEntitySet_EmptyStateWithoutEntity_Fails`, `ContainerConfigure_NoChange_Fails`), D12 (składowa nieodwracalna ostatnia, terenu i budowli razem nie wolno, 49 bez składowej budowli, cofanie przy błędzie z zachowaniem `ContainerIds.NextValue`, widok efektywny w `PlaceChestOnTerrain` i `PlaceChestOnFace`), D13 (`BlockClear_WithBlockEntity_Fails`, `BlockClear_WithHostedContainer_Fails`, ostatni blok budowli), D22 (stała `WorldWithItemsHashBeforeBlockEntities` i trzy testy zmiany skrótu) oraz blobom 21 i 22. D19 jest spełnione pośrednio: żaden test appliera nie oczekuje odmowy 37 z powodu aktora — zostaje tak. Braki są drobne i wymienione w (4): właściciel łóżka nieznany graczowi, `State` ≥ 8 w applierze, złożona z 49 na dwóch budowlach, łóżko ustawione w tej samej złożonej co punkt odrodzenia (widok efektywny dla 44), integralność stanu `Chest` przy wczytaniu, wsadowe przełączenie kolumny drzwi w lustrze.

**(2) Sygnatury dobrane przez wykonawcę — zatwierdzone, z doprecyzowaniem:**

- `WorldCheckpoint`: `UpsertedBlockEntities` (`BlockEntityRecord`), `RemovedBlockEntities` (`BlockRef`), `UpsertedProgress` (`PlayerProgressRecord`), `RemovedProgress` (`PlayerId`), wszystkie `init` z pustą wartością domyślną — **zatwierdzone**. Kolejność list: po `BlockRef` (budowla, potem `CellPos`) i po graczu, jak pozostałe listy checkpointu.
- `WorldStateBuilder.AddBlockEntity(BlockEntityRecord)` i `AddPlayerProgress(PlayerProgressRecord)`, błędy jako `InvalidDataException` — **zatwierdzone**. Doprecyzowanie walidacji: `AddBlockEntity` — budowla istnieje, komórka niepusta, klucz unikalny; w `Build()` (po wszystkich kontenerach) stan `Chest` wskazuje istniejący kontener rodzaju `Chest` z gospodarzem tej komórki, a liczba stanów budowli ≤ 4096. `AddPlayerProgress` — gracz istnieje, `RespawnBed` **nie jest** `null` (rekord bez punktu nie istnieje, D11), gracz unikalny; łóżka, na które wskazuje punkt, builder **nie** sprawdza (zerwany punkt nie może blokować startu świata; K6-35 przy braku łóżka odradza w miejscu startu).
- `StateCodec.EncodeBlockEntityBlob` / `DecodeBlockEntityBlob` i `EncodePlayerProgressBlob` / `DecodePlayerProgressBlob` — **zatwierdzone**; błędy dekodowania jako `ProtocolViolationException` (Persistence zamienia je na `WorldCorruptedException`, jak dla bloba 20). Kodek bloba 22 przyjmuje bit „brak punktu” (format na przyszłe pola wersji 2), ale zapis checkpointu nigdy go nie tworzy.
- `ConstructReplica.TrySetFunctionState(CellPos cell, ushort expectedType, byte state, uint version)` — **zatwierdzone** dla pojedynczej komórki (wersja dokładnie +1, zły typ, pusta komórka i `state` ≥ 8 → `false`). **Poprawka:** kolumna drzwi to jedna wersja dla kilku komórek, więc potrzebny jest też `TrySetFunctionStates(IReadOnlyList<BlockFunctionStateSetOp> ops, uint version)` — wszystko albo nic, wersja +1 raz; `ConstructMirror` używa go dla złożonej z 49.
- Bajty złote — **zatwierdzone** (zgodne z `WriteCellPos` = trzy varinty zygzak i z nagłówkiem blobów „rodzaj, wersja” jak blob 20): op 36 `24 01 03`; op 37 `25 01 03 08 02`; op 40 `28 01 05 02 01 00 02 01 07` (`Chest`), `… 03 02 09` (`Bed`), `… 03 00` (brak stanu); op 44 `2C 01 04 01 05 02 01 00 03` i `2C 01 04 00`; op 49 `31 01 05 02 01 00 09 01`; blob 21 `15 01 05 02 01 00 01 07` i `15 01 05 02 01 00 02 09`; blob 22 `16 01 04 01 05 02 01 00 03` i `16 01 04 00`. Uwaga zapisana w formacie: blob 21 nie niesie typu bloku (niesie go tylko operacja 40) — tak ma zostać.

**(3) Rozstrzygnięcia (DECYZJA, implementacja rusza autonomicznie):**

- **DO USTALENIA-1 → DECYZJA:** propozycja zatwierdzona — K6-30 dodaje operacje 36, 37 i blob 22 w wersji 1 tylko z punktem odrodzenia (D0, D11).
- **DO USTALENIA-3 → DECYZJA:** propozycja zatwierdzona — kolumna drzwi do 3 komórek wzdłuż osi Y budowli, jedna wersja budowli; otwarte drzwi bez kolizji. Wygląd panelu `builtin:door_open` przechodzi bramkę wyglądu (zrzut, akceptacja Dismonda w D4/C5); zmiana wyglądu nie zmienia formatu ani testów Core.
- **DO USTALENIA-5 → DECYZJA:** propozycja zatwierdzona — nowa skrzynia `Shared`; skrzynię prywatną rozbiera tylko właściciel (`NotOwner`).
- **DO USTALENIA-6 → DECYZJA:** propozycja zatwierdzona — łóżko zajmuje `Sleep` albo `SetRespawn`, jedno łóżko na gracza (zajęcie nowego zwalnia stare), łóżko innego gracza → `NotOwner`, rozebrać może każdy (czyści punkt właściciela), `Sleep` = `SetRespawn` do K6-33.
- **DO USTALENIA-7 → DECYZJA:** propozycja zatwierdzona — komunikat 48 nie powstaje w K6-30; klient zna swoje łóżko z właściciela w komunikacie 46.
- **DO USTALENIA-9 → DECYZJA:** propozycja zatwierdzona — nowa fikstura `world-v3-blocks.db` (`FixtureGenerator.Generate_WorldV3Blocks`, `Manual`), `world-v3.db` bez zmian.

DO USTALENIA-2, 4, 8, 10, 11 rozstrzyga przegląd B1 (dotyczą intencji, treści i gry, nie kontraktów etapu A).

**(4) Poprawki testów (przed A2):**

| Plik | Test | Zmiana |
| --- | --- | --- |
| `tests/Horyzont.Core.Tests/BlockEntities/BlockEntityOpsApplierTests.cs` | nowy `BlockEntitySet_BedWithUnknownOwner_Fails` | `BedBlockState(new PlayerId(99))` na łóżku → odmowa; `PlayerId.None` przechodzi |
| `tests/Horyzont.Core.Tests/BlockEntities/FunctionStateOpsTests.cs` | nowy `FunctionStateSet_StateAbove7_Fails` | rekord z `State = 8` zbudowany wprost → `AssertFails` (applier, nie tylko kodek) |
| `tests/Horyzont.Core.Tests/BlockEntities/RespawnPointOpsTests.cs` | nowy `RespawnPointSet_WrongExpectedType_Fails` | `BedRef` z typem drzwi na komórce łóżka ze stanem → odmowa |
| `tests/Horyzont.Core.Tests/Items/CompoundConstructTests.cs` | nowy `PlaceBedWithOwnerAndRespawn_OneCompound_UsesEffectiveView` | `[BlockEntitySetOp(FreeCell, Bed(Player1)), RespawnPointSetOp(Player1, BedRef(FreeCell)), BlockSetOp(FreeCell, BedType)]` → przyjęte; punkt i stan istnieją (D12: 40 i 44 czytają komórkę składowej ostatniej) |
| `tests/Horyzont.Core.Tests/Items/CompoundConstructTests.cs` | nowy `FunctionStatesOnTwoConstructs_OneChangePerConstruct` | złożona z 49 na dwóch budowlach → dwa `ConstructChange`, każda wersja +1 (wymaga drugiej budowli w `WorldFactory.CreateWithFunctionalBlocks` albo budowli utworzonej w teście) |
| `tests/Horyzont.Core.Tests/Checkpoints/BlockEntityCheckpointTests.cs` | nowe `Builder_ChestStateWithoutContainer_Throws`, `Builder_ProgressWithoutRespawn_Throws`, `Builder_ProgressOfUnknownPlayer_Throws` | doprecyzowanie z (2); każdy → `InvalidDataException` |
| `tests/Horyzont.Core.Tests/Replication/ConstructReplicaFunctionStateTests.cs` | nowy `TrySetFunctionStates_DoorColumn_OneVersion_AllOrNothing` | dwie komórki drzwi z wersją +1 → `true`, obie zmienione; jedna z nich z błędnym typem → `false`, żadna niezmieniona, wersja bez zmian |
| `tests/Horyzont.Core.Tests/Items/CompoundConstructTests.cs` | `RemoveChest_Compound_ClearsContainerEntityAndBlock` | bez zmian; potwierdzone: `ContainerChange.Removed` jest jedyną zmianą kontenera, `ConstructChange.Removed = false` |

Bez zmian w plikach: korpus złoty, `TerrainJournalGoldenCorpusTests`, `DeterminismTests` — do sprawdzenia `git diff b5e427e..HEAD` na tych ścieżkach przy każdym kroku A.

### Etap A, kroki A0 i A1 (2026-10-16)

- **Commit startowy etapu A: `b5e427e`** (do `git diff b5e427e..HEAD` na korpusie złotym, `DeterminismTests` i migracjach).
- **A0.** `SurvivalHashTests.WorldWithItemsWithoutBlockEntities_HashUnchanged` = `15528921043589756334` (świat `ItemFixture(players: 2)` z zestawem startowym, kamieniem i ziemią w plecakach, przeniesieniem i skrzynią gracza; policzone na kodzie bez zmian skrótu). Test jest zielony i ma pozostać zielony po A5.
- **A1, szkielety (`// K6-30 A2…A6`, bez logiki).** Core: `BlockEntities/` (`BlockRef`, `BlockEntityKind`, `BlockEntityState`, `ChestBlockState`, `BedBlockState`, `BlockEntityStore` tylko do odczytu), `Progress/` (`BedRef`, `PlayerProgress`, `PlayerProgressStore`), operacje 36, 37, 40, 44, 49, `OperationConstructs`, `ContainerChange.Removed` (domyślnie `false`), `WorldCheckpoint.{UpsertedBlockEntities, RemovedBlockEntities, UpsertedProgress, RemovedProgress}`, `BlockEntityRecord`, `PlayerProgressRecord`, `WorldStateBuilder.AddBlockEntity/AddPlayerProgress`, `StateCodec.Encode/Decode{BlockEntity,PlayerProgress}Blob` (rzucają `NotImplementedException`), `ConstructReplica.TrySetFunctionState(cell, expectedType, state, version)`, `WorldState.BlockEntities/Progress`. Nazwy i sygnatury pól checkpointu, buildera i kodeka plan zostawiał wykonawcy: **do zatwierdzenia przez Opus w A1**.
- **Odstępstwo od planu: `OperationKind` i `BlobKind` bez nowych wartości w A1.** Rekordy operacji zwracają `(OperationKind)36/37/40/44/49`. Dopisanie wartości do enumów czerwieniłoby istniejące testy (`Enums_GoldenValues`, `GeneratorSanityTests`, lista rekordów w `ProtocolContractTests`) już w A1; wartości `OperationKind` i `BlobKind` 21/22 dochodzą w A3/A4 razem z aktualizacją tych złotych testów (dopuszcza to tabela etapu A: „poza fixture X6 i złotymi wartościami enumów”).
- **Jedyny istniejący test czerwony po A1:** `ProtocolContractTests.Operations_EachRecordReturnsItsKind_AndEveryKindHasARecord` (porównuje nazwy rekordów `Operation` z nazwami `OperationKind`; same nowe rekordy wystarczą, żeby się wyłożył). Naprawia go A3 (dopisanie `ContainerRemove`, `ContainerConfigure`, `BlockEntitySet`, `RespawnPointSet`, `BlockFunctionStateSet` do enumu).
- **Pomocnicze:** `Horyzont.Testing`: `WorldFactory.CreateWithFunctionalBlocks(players, withConstruct)` (budowla `FunctionalBlocks.Construct` = id 100, bo gracze zajmują najniższe id; podłoga, skrzynia, łóżko, lampa i dwa bloki drzwi jako zwykłe bloki), `FunctionalBlocks` (typy 1–5 i komórki). `Core.Tests`: `BlockEntities/FunctionalBlockFixture`. `CreateWithPlayerContainers` bez zmiany zachowania (wydzielone `WithPlayers`).
- **Różnice względem tabeli testów:** `OperationConstructsTests` leży w `BlockEntities/` (przestrzeń `Horyzont.Core.Tests.Operations` przesłaniałaby `Operations.…` w `EntityIdAllocatorTests`); testy repliki w nowym pliku `Replication/ConstructReplicaFunctionStateTests.cs` (istniejącego `ConstructReplicaTests` nie ruszano); dodatkowe testy: `BlockEntitySet_SameKindOtherFields_Replaces`, `GoldenBytes_NewOperations`, `BlockEntityBlob_GoldenBytes` (bajty kontraktu: 36 `24 01 03`, 37 `25 01 03 08 02`, 40 `28 01 05 02 01 00 02 01 07`, 44 `2C 01 04 01 05 02 01 00 03`, 49 `31 01 05 02 01 00 09 01`, blob 21 `15 01 …`, blob 22 `16 01 …`), `Generator_ProducesAcceptedPlacementsAndRemovalsAndRefusedOperations` (bez niego test właściwości jest pusty, dopóki wszystkie postawienia są odrzucane).
- **Testy odmów** zaczynają od wykonania poprawnego odpowiednika (kontrola), więc są czerwone z powodu braku zachowania, a nie dlatego, że szkielet odrzuca wszystko.
- **Uwaga:** `AllocationTests.SubmitPlaceOnFaceOverlappingOtherConstruct_Rejected_DoesNotAllocate` wyłożył się raz w pełnym przebiegu (480 B, zimny JIT; osobno i w dwóch kolejnych pełnych przebiegach zielony) — znana klasa flaków, nie związana z pakietem.

### Etap A, kroki A1 (poprawki przeglądu), A2–A7 (2026-10-16)

- **Commity:** `d4312b0` (poprawki przeglądu A1), `0ed81f3` (A2), `6d10de5` (A3), `1edc07a` (A4), `40e43da` (A5), `dba39b1` (A6). A7 to sprawdzenia bez zmian w kodzie. Korpus złoty, `TerrainJournalGoldenCorpusTests`, `DeterminismTests` i `Migrations/`: `git diff b5e427e..HEAD` pusty.
- **Poprawki przeglądu** dodane dokładnie z listy (4); w teście `BlockEntitySet_Over4096PerConstruct_Fails` poprawiono generator komórek (4097. komórka zderzała się z pierwszą, `BlockSet` na zajętą komórkę przerywał test przed sprawdzeniem limitu): trzecia współrzędna `2 + i / 4096`. Dodany szkielet `ConstructReplica.TrySetFunctionStates`.
- **Kolejność porównania `BlockRef`:** budowla, potem kolumna `CellPos.Chunk` i `LocalIndex` (jak chunki w checkpoincie). Porządek zapisany w `BlockRef.CompareTo` i używany przez `BlockEntityStore` (`SortedDictionary`) oraz przez skrót.
- **Undo (D12):** `UndoLog` zapisuje kroki w jednej liście i cofa je w odwrotnej kolejności (sloty, utworzone, zmienione ustawienia, usunięte kontenery, stany bloków, punkty odrodzenia); usunięty kontener wraca ten sam obiekt (`ContainerStore.Restore`), więc wcześniejsze wpisy slotów pozostają ważne. Składowe 49 nie mają undo: warunki sprawdza się przy składowej, a zmiany stosuje na końcu (`ApplyPendingFunctionStates`), z jedną podwyżką wersji na budowlę. Usunięcie kontenera utworzonego w tej samej operacji jest odmawiane.
- **`ContainerChange` dla zmian bez slotu:** kontener po 37 i usunięty (36) trafiają do `ContainerChange` ze znacznikiem `NoSlot` w liście dotkniętych; zmieniony ma pustą listę slotów i wersję +1, usunięty ma `Removed = true`, pustą listę i wersję sprzed usunięcia (bez podwyżki).
- **Kolejność błędów złożonej:** warunki składowej budowli (2–4) sprawdza się przed pozostałymi (widok efektywny), warunki terenu po przedmiotach (jak w K6-29W, więc stare dzienniki bez zmian). Składowa 49 w złożonej ze składową 2–4 jest odrzucana ze wskazaniem ostatniej składowej.
- **Odstępstwo od A3 w planie:** `ProtocolConstants.MaxBlockEntitiesPerConstruct = 4096` dodane już w A3 (applier go potrzebuje); reszta limitów D23 w B2.
- **Skrót (D22):** podsekcje `0x42` i `0x50` dopisane po kontenerach tylko gdy niepuste; sekcja Przetrwania powstaje też dla świata ze stanem bloku albo punktem odrodzenia. `WorldWithItemsWithoutBlockEntities_HashUnchanged` zielony.
- **Builder:** `AddBlockEntity`/`AddPlayerProgress` tylko zbierają rekordy; wszystkie kontrole (budowla, niepusta komórka, unikalność, `Chest` ↔ kontener gospodarza, właściciel łóżka, limit 4096, gracz, brak rekordu bez punktu) są w `Build()` po wczytaniu kontenerów. `LoadContainer` przyjmuje teraz gospodarza `ConstructBlock` (budowla i niepusta komórka, jeden kontener danego rodzaju na blok).
- **`CodecGen` i enumy:** `OperationKind` 36/37/40/44/49 i `BlobKind` 21/22 dopisane razem z wpisami w `Enums_GoldenValues` (A3 i A4); `CodecGen.Operation` losuje nowe operacje (rozmiar `ContainerConfigureOp` w 1…256).
- **A7, mutacja:** usunięcie całego warunku D13 (`CheckBlockClearOrphans` zwraca zawsze `null`) czerwieni `BlockClear_WithBlockEntity_Fails`, `BlockClear_WithHostedContainer_Fails` i test właściwości. Usunięcie samej części „gospodarz kontenera” czerwieni tylko `BlockClear_WithHostedContainer_Fails`: generator właściwości zawsze zakłada stan `Chest` razem z kontenerem, więc stan zasłania brak drugiego warunku (do rozważenia w B1: akcja generatora, która zdejmuje stan skrzyni przed `BlockClearOp`).
- **Znane czerwone/flaki:** `AllocationTests.SubmitPlaceOnFaceOverlappingOtherConstruct_Rejected_DoesNotAllocate` (480 B) pada w pełnym przebiegu `Core.Tests` (zimny JIT, jak w notatce z A1), osobno zielony.

### Etap B, krok B1 (2026-10-16) — do przeglądu Opus 5.5 przed B2

**Stan.** Testy z tabeli etapu B napisane i czerwone, szkielety bez logiki (`// K6-30 B2` … `B5`). `dotnet build Horyzont.sln` 0/0, `dotnet format --verify-no-changes` czysto. `git diff b5e427e..HEAD` (i drzewo robocze) na korpusie złotym, `TerrainJournalGoldenCorpusTests`, `DeterminismTests` i `Migrations/` pusty. Wynik `dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"`: **96 czerwonych** (Core 80, Constructs 12, Net 4), **3191 zielonych**, 1 pominięty (istniejący, Server.Tests). Z 96 czerwonych 94 to nowe testy (każdy czerwony z braku zachowania: nieznana intencja daje `InvalidParameters`, kodek rzuca na nieznanym typie, katalog nie zna rodzaju `chest`/`light`, mesher ignoruje stan funkcji); **2 to istniejące testy** wywrócone samym szkieletem rekordu komunikatu 46 (jak `Operations_EachRecord…` w A1): `ProtocolContractTests.Messages_EachRecordReturnsItsType_AndEveryTypeHasARecord` (Core) i `MergedProtocolTests.MessageTypes_AllConcreteMessages_HaveUniqueIdsAndOnlyDocumentedGaps` (Net) porównują rekordy `Message` z enumem `MessageType`. Naprawia je B2 (wartość 46 w enumie razem ze złotymi testami).

**Zasada testów odmów.** Test, który oczekuje `InvalidParameters`, `ContainerLocked` albo innej odmowy, którą dziś daje „nieznana intencja", zaczyna od przyjęcia poprawnego odpowiednika (kontrola), inaczej przechodziłby bez żadnego zachowania. Tak samo w kodekach (kontrola poprawnego round-tripu przed `ArgumentException`/`ProtocolViolationException`) i w routerze.

**Odstępstwa od tabeli etapu B (świadome, jak w A1).**

- Bez wartości w enumach: `RejectReason` 54 i 57 (`NotOwner`, `ContainerNotEmpty`), `MessageType` 46 i `ProtocolVersion` 15 dochodzą w B2 razem z aktualizacją złotych testów (`Enums_GoldenValues`, `Constants_GoldenValues`, `ProtocolVersion_MatchesDocumentedMergeValue`, `MessageChannels_MapsEveryType`, `MaxMessages`, `CodecGen`, `GeneratorSanityTests`, `MergedProtocolTests`, `MessageRouterTests.TryGetType_UnassignedValues`). Testy nazywają kody liczbą: `FunctionalReasons` (`(RejectReason)54`, `57`) w `tests/Horyzont.Core.Tests/BlockEntities/`, `(MessageType)46` w testach kodeka i routera; rekord `BlockEntityStates.Type` zwraca `(MessageType)46`. Do B2: zamienić na członków enumów i usunąć `FunctionalReasons`.
- `IntentValidationTests.AllReasons` iteruje tylko kody poniżej 30, więc 54 i 57 mają osobny `Submit_FunctionalBlockScenario_ReturnsNewReason` (teoria na numerach).
- Generator `CodecGen` (X6) nie dostał nowych intencji i komunikatu: wpadłyby do istniejących testów właściwości kodeka i wywróciły je. Dochodzi w B2 z kodekiem.
- `BlockFunctionKind` 11–13 i `BlockFunction.ContainerSlots`/`CreateChest`/`Bed`/`Light` dopisane od razu (treść, nie format; żaden test złoty nie wylicza `BlockFunctionKind`).
- Generator testu właściwości z A7 ma nową akcję `DropChestState` (samo usunięcie stanu skrzyni, kontener zostaje): `BlockClearOp` na takiej komórce odmawia już tylko część "gospodarz kontenera" D13. Sprawdzone mutacją (ta część w `CheckBlockClearOrphans` wycięta): czerwienią się `BlockClear_WithHostedContainer_Fails` i test właściwości; mutacja cofnięta. Test pokrycia generatora wymaga co najmniej 10 samotnych usunięć stanu.
- `Constructs.Tests`: tabela renderowania drzwi i lamp budowana osobno w `FunctionalMeshContent` (nie w `TestContent`), bo parsowanie funkcji `light` zadziała dopiero po B3; testy lamp w `ChunkMesherTests` są więc czerwone przez katalog (do B3), testy drzwi przez mesher (do B5).
- `ProtocolContractTests`, `MessageRouterTests` i `MaxMessages` zostały bez zmian; router ma osobny `BlockEntityStatesRouterTests` (Net.Tests).

**Nowe sygnatury (szkielety) do zatwierdzenia.**

| Co | Sygnatura / kontrakt |
| --- | --- |
| Intencja 22 | `QuickStackIntent()` — rekord bez pól; bajty `16 01` |
| Intencja 23 | `TakeAllIntent(ContainerId Container)`; `17 01 <varuint>` |
| Intencja 28 | `BlockInteractIntent(EntityId Construct, CellPos Cell, BlockInteractAction Action)`, `BlockInteractAction : byte` `Toggle` 1, `Sleep` 2, `SetRespawn` 3; `1C 01 <budowla> <komórka> <akcja>` |
| Intencja 29 | `BlockConfigIntent(EntityId Construct, CellPos Cell, BlockConfig Config)`; `BlockConfig` = `abstract record` (konstruktor `private protected`, `BlockConfigKind Kind`) z wariantami `TeleportNameConfig(string Name)` (wariant 1: długość varuint + UTF-8, 1–32 B; zapis spoza zakresu → `ArgumentException`, odczyt → `ProtocolViolationException`) i `ContainerAccessConfig(ContainerAccess Access)` (wariant 2: bajt 1/2); `1D 01 <budowla> <komórka> <wariant> …`. Nazwy wariantów inne niż w planie (`TeleportName`/`ContainerAccess` kolidowałyby z enumami) |
| Komunikat 46 | `BlockEntityStates(EntityId Construct, IReadOnlyList<BlockEntityEntry> Entries)`, `BlockEntityEntry(CellPos Cell, BlockEntityState State)`; `2E 01 <budowla> <liczba varuint ≤ 4096> { <komórka> <rodzaj> <pola rodzaju> }`, pola jak w operacji 40 (`Chest` varuint kontener, `Bed` varuint właściciel, 0 = wolne); niezawodny uporządkowany. Kodek nie wymusza kolejności wpisów (do decyzji: rosnąco po komórce jak `BlockEntityStore`?) |
| Odmowy | `NotOwner` 54 i `ContainerNotEmpty` 57 (w testach jako rzutowanie liczby do B2) |
| Stałe | `ProtocolConstants.MaxTeleportNameBytes = 32`; `GameRules.MaxDoorColumnCells` 3, `ContainerOpenRangeMeters` 6, `ContainerKeepRangeMeters` 8, `QuickStackRangeMeters` 10 jako właściwości `init` (plan D23 mówił "stałe"; właściwości pozwalają testom je zmieniać) |
| Zasięg | `ContainerReach.TryDistanceFromEye(WorldState, GameRules, PlayerId, EntityId construct, CellPos, out float meters)` — publiczna statyczna; oko gracza (rama planety) → środek komórki; `false` dla nieznanego gracza, budowli albo innej planety |
| Treść | `BlockFunction(…, ushort ContainerSlots = 0)`, `BlockFunction.CreateChest(ushort)`, `.Bed`, `.Light`; `BlockFunctionKind.Chest` 11, `Bed` 12, `Light` 13 |
| Constructs | `BlockLightDef(float RangeMeters, uint Color /*0xRRGGBBAA*/, float Energy)`, `BlockTypeContent.Light` (opcjonalny czwarty parametr), `ChunkMeshData.LightCells` (`ReadOnlySpan<CellPos>`, komórki budowli, tylko lampy włączone) |
| Testing | `FunctionalBlocks.ContentFor(WorldState, chestSlots)` (kamień, skrzynia, łóżko, lampa, drzwi), `FunctionalBlockFixture.ForIntents(players, withSurvival, chestSlots)`, `StandFor(n)` (gracz n stoi 3 m przed rzędem bloków, w zasięgu wszystkich) |

**Interpretacje przypięte testami (do potwierdzenia albo korekty w przeglądzie).**

- **DO USTALENIA-2 (`TakeAllIntent`):** cztery testy przypinają D17: kolejność jak urobek (stosy paska, stosy plecaka, puste sloty plecaka, puste sloty paska), częściowe przeniesienie bierze sloty skrzyni rosnąco (pierwszy slot zajmuje jedyne wolne miejsce, drugi zostaje), brak miejsca → `InventoryFull`, cudza prywatna → `ContainerLocked`.
- **DO USTALENIA-4 (lampy):** `PlaceLamp_InitialStateOn` (włączona po postawieniu, zwykły `BlockSetOp` z `FunctionState` 1), `Toggle_Lamp_SwitchesOff`; `LightCells` zawiera tylko włączone. Limit 32 świateł i brak cieni to gra (etap C, gdUnit), bez testu w B1.
- **DO USTALENIA-8 (zasięgi):** testy mierzą od oka do środka komórki: skrzynia 3,1 m — w zasięgu; ruch do skrzyni z 14,6 m → `OutOfReach` (też ze skrzyni do plecaka); szybkie odkładanie z 13,6 m ignoruje skrzynię (`NotEnoughItems`), z 3,1 i 3,7 m bierze obie, najbliższą pierwszą (testowane tylko "bliższa wygrywa mimo wyższego id"; remis odległości rozstrzyga rosnący `ContainerId` bez testu). Progi 6 m (subskrypcja) oraz granice 8 m i 10 m sprawdzi etap C; `ContainerReachTests` pilnują wzoru (budowla obrócona o 90° i przesunięta).
- **DO USTALENIA-10 (kody odkładania):** brak pasujących typów albo skrzynie poza zasięgiem/cudze prywatne → `NotEnoughItems` (41); skrzynia z danym typem, ale bez miejsca → `InventoryFull` (40).
- **DO USTALENIA-11 (liczby treści):** `Parse_RepoContent_HasChestBedLampDoor` przypina tabelę D5 (masy 80/200/120/20/100, sloty 20/40, światło 8 m `#FFD9A0` 1,5, materiał `lamp` `#F2E3B3`, łóżko jako `mesh` `builtin:slab`); strojenie tych liczb (D5: "bez zmiany kodu") wymaga więc zmiany tego testu — jeśli Opus chce tuningu bez dotykania testów, trzeba poluzować asercje mas i slotów. B3 musi też zaktualizować `ShippedContent_AllShipBlocksPresentWithMass` (liczba kluczy) i `ShippedContent_FixtureDefinitionsMatchBlocksJson` (`ShipFixtures.BlockKeys` kontra `blocks.json`), bo nowe typy w `blocks.json` je wywrócą.
- Pozostałe wybory w testach (zgodne z D6, D10, D16, D18, ale nie wynikające z nich jednoznacznie) — patrz przegląd B1 niżej: kolumna drzwi rośnie najpierw w dół, potem w górę (wskazane drugie z czterech drzwi → przełączone 1–3, czwarte nietknięte); `Sleep` we własnym, już zajętym łóżku → sama `RespawnPointSetOp` bez `BlockEntitySetOp`; rozbiórka cudzego łóżka przez innego gracza zaczyna się od `RespawnPointSetOp(właściciel, null)`; ustawienie dostępu na bloku bez skrzyni i `TeleportName` na skrzyni → `InvalidParameters`; bucket `Blocks` ma 10 przełączeń, jedenaste → `RateLimited`; zamknięcie drzwi na graczu sprawdzone przy kapsule zachodzącej na komórkę o 0,2 m (celowanie w górne drzwi, bo dolne zasłania górny blok).

### Przegląd B1 (Opus 5.5, 2026-10-16) — werdykt: GO z poprawkami

Przegląd tylko odczytem planu i opisu B1 (commit e214887; bez uruchamiania testów). Poprawki z (4) wykonawca wprowadza w commicie `test(core): apply the B1 review` przed B2; nie wymagają ponownego przeglądu, o ile nie zmieniają kontraktu.

**(1) Zgodność.** Testy B1 odpowiadają D6 (kolumna), D7, D8, D9, D10, D16, D17, D18, D21, D23 i zasadzie „kontrola przed odmową”. Wybory przypięte testami w punkcie „Pozostałe wybory” są **zatwierdzone** (kolumna najpierw w dół, potem w górę; sen we własnym zajętym łóżku → sama `RespawnPointSetOp`; rozbiórka cudzego łóżka zaczyna od `RespawnPointSetOp(właściciel, null)`; dostęp na bloku bez skrzyni i `TeleportName` na skrzyni → `InvalidParameters`; 11. przełączenie → `RateLimited`). Odstępstwa „bez wartości w enumach do B2” i `FunctionalReasons` — **zatwierdzone** jako tymczasowe (usuwa je B2). Braki: granice zasięgów 8 m i 10 m oraz remis odległości w Core (nie dopiero w etapie C), kolejność i unikalność wpisów komunikatu 46, zarezerwowany rodzaj stanu w 46, `TakeAll`/`QuickStack` bez treści Przetrwania.

**(2) Sygnatury i bajty — zatwierdzone, z jedną poprawką kontraktu:**

- Intencje 22 `16 01`, 23 `17 01 <varuint>`, 28 `1C 01 …`, 29 `1D 01 …`; `BlockInteractAction` 1–3; `BlockConfig` jako `abstract record` z `TeleportNameConfig` (wariant 1) i `ContainerAccessConfig` (wariant 2) — **zatwierdzone** (zmiana nazw wariantów względem planu przyjęta).
- Komunikat 46 `2E 01 <budowla> <liczba ≤ 4096> {komórka, rodzaj, pola}` — **zatwierdzony z poprawką:** wpisy **ściśle rosnąco** w porządku komórek `BlockRef` (kolumna `CellPos.Chunk`, potem `LocalIndex` — ten sam co `BlockEntityStore`), bez powtórzeń. Zapis nieposortowany albo z powtórzeniem → `ArgumentException`, odczyt → `ProtocolViolationException`. Rodzaj 0 i 3–255 w 46 → `ProtocolViolationException` (stan pusty nie jest wpisem; usunięcie wyraża brak komórki na liście).
- `ProtocolConstants.MaxTeleportNameBytes = 32`; zasięgi i `MaxDoorColumnCells` w `GameRules` jako właściwości `init` — **zatwierdzone** (D23 zmienione: właściwości zamiast stałych).
- `ContainerReach.TryDistanceFromEye(WorldState, GameRules, PlayerId, EntityId, CellPos, out float)` — **zatwierdzone**; oko liczone tym samym wzorem co `IntentValidator.TryGetEye` (stopy + `UpAt` × `EyeHeight`) — walidator i serwer używają tej jednej funkcji, żadnej kopii wzoru.
- `BlockFunction(…, ushort ContainerSlots = 0)`, `CreateChest/Bed/Light`, `BlockFunctionKind` 11–13, `BlockLightDef(RangeMeters, Color 0xRRGGBBAA, Energy)`, `BlockTypeContent.Light`, `ChunkMeshData.LightCells` — **zatwierdzone**.

**(3) Rozstrzygnięcia (DECYZJA):**

- **DO USTALENIA-2 → DECYZJA:** zatwierdzone — `TakeAllIntent` (23) w K6-30 tylko dla skrzyń, kolejność jak urobek, częściowe przeniesienie, `InventoryFull`, `ContainerLocked`.
- **DO USTALENIA-4 → DECYZJA:** zatwierdzone — lampa włączona po postawieniu (`FunctionState` 1 w operacji), najwyżej 32 światła najbliżej kamery, bez cieni (test gdUnit w etapie C).
- **DO USTALENIA-8 → DECYZJA:** zatwierdzone — 6 / 8 / 10 m od oka serwera do środka komórki, ten sam region planety; granice włącznie (`≤`).
- **DO USTALENIA-10 → DECYZJA:** zatwierdzone — brak pasujących (także skrzynie poza zasięgiem lub cudze prywatne) → `NotEnoughItems` (41); pasujące, ale bez miejsca → `InventoryFull` (40).
- **DO USTALENIA-11 → DECYZJA:** liczby z D5 zostają w `blocks.json`, ale **testy nie przypinają liczb** (strojenie bez zmiany testów): `Parse_RepoContent_HasChestBedLampDoor` sprawdza obecność pięciu kluczy, rodzaj funkcji, kształt (`bed` = `mesh` `builtin:slab`), masy dodatnie i skończone, sloty skrzyń w 1…256 i `chest_metal` > `chest_wood`, światło z zasięgiem > 0 i energią > 0, materiał `lamp` istnieje. Dokładne liczby pilnuje tylko tabela D5 w planie.

**(4) Poprawki testów (przed B2):**

| Plik | Test | Zmiana |
| --- | --- | --- |
| `tests/Horyzont.Constructs.Tests/Content/BlockTypeCatalogTests.cs` | `Parse_RepoContent_HasChestBedLampDoor` | asercje poluzowane wg DECYZJI-11 |
| `tests/Horyzont.Core.Tests/Serialization/MessageCodecBlockEntityTests.cs` | nowe `Write_UnsortedOrDuplicateEntries_Throws`, `Read_UnsortedOrDuplicateEntries_Throws`, `Read_ReservedOrEmptyKind_Throws` | poprawka kontraktu 46 z (2) |
| `tests/Horyzont.Core.Tests/Simulation/ChestIntentTests.cs` (albo plik B1 z testami ruchu do skrzyni) | nowe `MoveToChest_AtKeepRange_Accepted_JustBeyond_OutOfReach` | 7,99 m przyjęte, 8,01 m `OutOfReach` (granica włącznie) |
| `tests/Horyzont.Core.Tests/Simulation/QuickStackIntentTests.cs` | nowe `QuickStack_ChestAtRangeBoundary_IncludedJustBeyondIgnored`, `QuickStack_EqualDistance_LowerContainerIdFirst` | 9,99 / 10,01 m; remis odległości → rosnący `ContainerId` |
| `tests/Horyzont.Core.Tests/Simulation/QuickStackIntentTests.cs`, `TakeAllIntentTests.cs` | nowe `QuickStack_WithoutSurvivalContent_InvalidParameters`, `TakeAll_WithoutSurvivalContent_InvalidParameters` | D21 |
| `tests/Horyzont.Core.Tests/Simulation/ContainerReachTests.cs` | nowy `TryDistanceFromEye_MatchesValidatorEye` | ta sama odległość co zasięg `RemoveBlockIntent` na tej samej komórce (np. odmowa `OutOfReach` przy `ReachMeters` i `ContainerReach` > `ReachMeters + luz` w tym samym ustawieniu) |

**Uzupełnienie kroków (przeniesione z B1):**

- **B2** dodatkowo: wartości `RejectReason.NotOwner = 54`, `ContainerNotEmpty = 57`, `MessageType.BlockEntityStates = 46`, `ProtocolVersion = 15`; zamiana `(RejectReason)54/57` i `(MessageType)46` w testach na członków enumów i usunięcie `FunctionalReasons`; złote: `ProtocolContractTests.Enums_GoldenValues` (54, 57, 46, `BlockInteractAction`, `BlockConfigKind`), `Constants_GoldenValues` (`MaxTeleportNameBytes`), `ProtocolVersion_MatchesDocumentedMergeValue` (15), `Messages_EachRecordReturnsItsType_AndEveryTypeHasARecord`, `MessageChannels_MapsEveryType` (46 niezawodny); `MaxMessages.All()` (46 z 4096 wpisami `Bed` o maksymalnych varintach — musi zmieścić się w 256 KiB); `CodecGen` (intencje 22, 23, 28, 29 i komunikat 46 z posortowanymi wpisami); `GeneratorSanityTests`; `MergedProtocolTests.MessageTypes_AllConcreteMessages_HaveUniqueIdsAndOnlyDocumentedGaps`; `MessageRouterTests.TryGetType_UnassignedValues` (46 przestaje być nieprzypisany); `IntentValidationTests` — jeśli `AllReasons` ma pokrywać zakres Przetrwania, dopisać 54 i 57 tam zamiast osobnej teorii (dopuszczalne zostawić `Submit_FunctionalBlockScenario_ReturnsNewReason`).
- **B3** dodatkowo: `ShippedContent_AllShipBlocksPresentWithMass` (liczba kluczy — najlepiej zamienić na „co najmniej te klucze statku”, żeby kolejne treści nie łamały testu) i `ShippedContent_FixtureDefinitionsMatchBlocksJson` (`ShipFixtures.BlockKeys` kontra `blocks.json` — porównanie ograniczyć do kluczy statku albo dopisać nowe typy do fixture); poluzowany `Parse_RepoContent_HasChestBedLampDoor` z (4) musi być zielony po B3.

### Etap B, kroki B1 (poprawki przeglądu) i B2 (2026-10-16)

- **Commity:** `5c751b8` (poprawki przeglądu B1, `test(core): apply the B1 review`), B2 w kolejnym commicie `feat(protocol): …`. Korpus złoty, `TerrainJournalGoldenCorpusTests`, `DeterminismTests` i `Migrations/`: `git diff b5e427e..HEAD` pusty.
- **Poprawki B1** dokładnie z listy (4); dodatkowo pomocnik `BlockEntities/ReachPlacement` (ustawia gracza w zadanej odległości oka od środka komórki, mierząc okiem symulacji, niezależnie od `ContainerReach`) i poprawka generatora `Write_4097Entries_Throws` (wpisy muszą być posortowane po `BlockRef`). `QuickStack_EqualDistance_LowerContainerIdFirst`: dwie skrzynie w komórkach (0,1,0) i (−1,1,0) przy graczu na x = 0, więc odległości są identyczne co do bitu; skrzynia o niższym id leży w komórce sortowanej później, więc remis nie rozstrzyga się ani kolejnością komórek, ani kolejnością zapisu.
- **B2:** `RejectReason.NotOwner` 54, `ContainerNotEmpty` 57; `MessageType.BlockEntityStates` 46 (kanał niezawodny uporządkowany); `ProtocolVersion` = 15; `ProtocolConstants.MaxBlockEntitiesPerConstruct`/`MaxTeleportNameBytes` bez zmian względem B1. `IntentCodec` 22, 23, 28, 29 (rozpoznanie kodu przez `Enum.IsDefined` zamiast ręcznej listy; nazwa teleportu 1–32 B UTF-8, pusta odrzucana przy zapisie i odczycie). `MessageCodec` 46: wpisy ściśle rosnąco po `BlockRef` (budowla, chunk, indeks w chunku), bez powtórzeń; rodzaj 0 i 3–255 → `ProtocolViolationException`. `MessageRouter`: 46 znany i dozwolony od serwera jak `ConstructState`, od klienta nigdy.
- **Złote testy:** `ProtocolContractTests` (`Enums_GoldenValues` z 54, 57, 46, `BlockEntityKind`, `BlockInteractAction`, `BlockConfigKind`; `Constants_GoldenValues`; wersja 15; lista rekordów komunikatów), `MaxMessages.MaxBlockEntityStates` (4096 łóżek z maksymalnymi varintami, ok. 68 KiB), `CodecGen` (intencje 22, 23, 28, 29 i komunikat 46 z posortowanymi wpisami), `GeneratorSanityTests`, `MergedProtocolTests` (wersja 15, 46 poza zarezerwowanymi, scenariusz 10), `MessageRouterTests.TryGetType_UnassignedValues`. Usunięto `FunctionalReasons` i rzutowania `(MessageType)46`.

### Etap B, krok B3 (2026-10-16)

- `BlockTypeCatalog`: rodzaje `chest` (`slots` 1…256, całkowita), `bed`, `light` (`range`, `energy` dodatnie skończone, `color` `#RRGGBB[AA]`; parametry światła w `BlockTypeContent.Light`, nie w Core); `FormatVersion` zostaje 2. `blocks.json`: materiał `lamp` (`#F2E3B3`) dopisany na końcu listy (istniejące id materiałów bez zmian) i pięć typów z tabeli D5; `blocks.csv`: pięć nazw PL/EN.
- `ShippedContent_AllShipBlocksPresentWithMass` sprawdza teraz „co najmniej klucze statku”, a `ShippedContent_FixtureDefinitionsMatchBlocksJson` porównuje fixture z początkiem listy kluczy i tylko z blokami statku (zgodnie z uzupełnieniem B3 z przeglądu B1).
- Nie uruchamiano importu tłumaczeń Godota (`.translation`): brak uruchamiania Godota w tym zadaniu; import dochodzi z D5 (`--import`).

### Etap B, krok B4 (2026-10-16)

- **Podział odpowiedzialności (odstępstwo od listy plików):** `SurvivalRules` dostaje `GameRules` (zasięg skrzyń przy przenoszeniu przedmiotów), a nie `BlockContentTable`; treść bloków czyta nowa `FunctionalBlockRules` (wewnętrzna, tworzona w `ServerSimulation` zawsze, także bez treści Przetrwania, bo przełączanie drzwi i lamp jej nie wymaga). `ServerSimulation.Submit` pyta najpierw `FunctionalBlockRules.TryResolve` (postawienie skrzyni, łóżka i lampy, rozbiórka komórki ze stanem, 28, 29, 22, 23), potem `SurvivalRules`/`IntentResolver`; `Commit` rejestru urobku tylko po rozwiązaniu przez `SurvivalRules` (inaczej zatwierdziłby cudze, nieskasowane wpisy). Walidacja 28 i 29 oraz bramki D21/D23 są w `IntentValidator`; `ContainerReach` (publiczna) ma wewnętrzne `TryGetEye` używane przez walidator, więc wzór oka jest jeden.
- **Kolumna drzwi:** `FunctionalBlockRules.DoorColumn` (najpierw w dół, potem w górę, ten sam typ bloku, najwyżej `MaxDoorColumnCells`, rosnąco po Y); nowy stan = przeciwny do stanu wskazanej komórki; zamknięcie sprawdza kapsuły wszystkich graczy online dla każdej komórki kolumny (`IntersectsPlayer`).
- **Szybkie odkładanie (D16):** najpierw dopełnienie stosów danego typu we wszystkich skrzyniach po kolei (najbliższa pierwsza, remis: niższy `ContainerId`), dopiero potem puste sloty tych skrzyń (czytanie dosłowne D16); jedna składowa → sama `ItemTransferOp`, więcej → `CompoundOp`. **Uwaga:** `TakeAllIntent` na pustej skrzyni daje `NotEnoughItems` (41), a `InventoryFull` (40) tylko gdy skrzynia ma przedmioty, które się nie mieszczą (D17 mówi "nic → InventoryFull"; pusta skrzynia nie jest brakiem miejsca).
- **Zasięg przedmiotów:** `ItemMoveIntent` na kontenerze skrzyni: nieistniejący albo cudza prywatna → `ContainerLocked`, dalej niż `ContainerKeepRangeMeters` → `OutOfReach`; `ItemDropIntent` nadal tylko z własnych kontenerów.
- **Alokacje:** `BlockEntityStore.TryGet` wychodzi wcześniej dla budowli bez stanów (zapytanie do `SortedDictionary` alokuje 40 B, a walidator pyta przy każdej rozbiórce); `ContainerStore` indeksuje gospodarzy blokowych kluczem `BlockRef` zamiast krotki. `AllocationTests` zielone.
- Poza listą: bez treści Przetrwania rozbiórka komórki ze stanem albo kontenerem (D21) i postawienie skrzyni/łóżka dają `InvalidParameters`; limit `MaxBlockEntitiesPerConstruct` sprawdza walidator przy postawieniu skrzyni/łóżka (D23).

### Etap B, krok B5 (2026-10-16)

- `ConstructDirtyChunks.Collect` bierze część budowli złożonej z `OperationConstructs.TryGetConstructPart` (składowa 2–4 albo same składowe 49), a operację 49 traktuje jak `BlockSetOp` (komórka i sąsiednie chunki, bo otwarte drzwi odsłaniają ściany sąsiadów). Złożona bez części budowli (same przedmioty) niczego nie brudzi.
- `BlockRenderInfo` ma `Function` i `OpenMesh`; `BlockRenderTable.Build` daje każdemu blokowi `Door` jednorazowo zbudowaną siatkę `builtin:door_open` (panel 0,1 m przy ścianie −X, nie jest wymieniona w treści). `ChunkMesher`: drzwi z `FunctionState` ≠ 0 są kształtem prefabrykowanym bez kolizji i nie zasłaniają ścian sąsiadów; lampa z `FunctionState` ≠ 0 trafia do `ChunkMeshData.LightCells` (komórki budowli, rosnąco po x, y, z; tylko wnętrze chunka). Zamknięte drzwi i wyłączona lampa rysują się jak zwykły sześcian.
- **Poprawka testu B1:** `Collect_CompoundWithBlockSet_DirtiesCell` (kolumna drzwi) używał komórek z `z = 0`, których sąsiedni chunk `Z − 1` też jest brudny; komórki przesunięte na `z = 8`, żeby test sprawdzał to, co opisuje (dwa chunki wzdłuż osi Y).

### Etap B, krok B6 — sprawdzenie (2026-10-16)

- `dotnet build Horyzont.sln`: 0 ostrzeżeń, 0 błędów. `dotnet format Horyzont.sln --verify-no-changes`: czysto.
- `dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"`: Architecture 12, Client 591, Integration 34, Net 166, Constructs 131, Persistence 203, Core 1802, Server 360 (+1 pominięty, istniejący) zielone, razem 3299. W jednym pełnym przebiegu `AllocationTests.SubmitPlaceOnFaceOverlappingOtherConstruct_Rejected_DoesNotAllocate` padł (zimny JIT, znany flak z A1/A2); osobno i w kolejnym przebiegu `Core.Tests` zielony.
- `git diff b5e427e..HEAD` na `Golden/TerrainJournals`, `TerrainJournalGoldenCorpusTests.cs`, `DeterminismTests.cs` i `src/Horyzont.Persistence/Migrations`: pusty.
- Zostaje etap C (zapis i serwer): testy z tabeli etapu C (C1), bez zmian w tym kroku. Przeglądu Opus wymaga całość zmian Core (`FunctionalBlockRules`, `IntentValidator`, `SurvivalRules`, kodeki 22/23/28/29/46, `ProtocolVersion` 15).

### Przegląd Core A+B (Opus 5.5, 2026-10-16) — werdykt: GO z poprawkami

Zakres i metoda: odczyt `OperationApplier.TryApplyCompound` (linie 202–312), `CheckPreconditions` (`BlockClearOp` → `CheckBlockClear ?? CheckBlockClearOrphans`, linia 97) oraz notatek realizacji A1–B6. Pełnego `git diff 9366b82..HEAD -- src/Horyzont.Core` przegląd nie objął (brak dostępu do gita w tej sesji); wersje formatów, bajty i skrót przyjmuję na podstawie zatwierdzonych w A1/B1 testów złotych, które są zielone (B6), i pustego `git diff` korpusu, `DeterminismTests` i `Migrations/`.

- **`CompoundOp` (D12) — zgodne.** Struktura: teren i każda składowa 2–4 tylko jako ostatnia, więc dwie nieodwracalne wykluczają się same; 49 ze składową budowli odrzucana. Warunki składowej budowli na stanie wyjściowym przed pozostałymi, widok efektywny (`_viewSet`, `_viewCreate`), D13 dla `BlockClearOp` po wcześniejszych składowych z cofnięciem, teren jak w K6-29W (stare dzienniki bez zmian). Składowe 49 bez wpisów cofania, stosowane w `Finish` — przyjęte (warunki sprawdzane przy składowej, zmiana dopiero po sukcesie całości).
- **D13 bez podbicia formatu — zgodne** z ZASADY §3 l. 66 (korpus i `DeterminismTests` bez zmian w plikach); warunek działa też dla samodzielnego `BlockClearOp`.
- **37 bez aktora (D19)** — zgodne; `actor` trafia tylko do `AppliedOperation`.
- **Wersje** (op 36/37/40/44/49, bloby 21/22, intencje 22/23/28/29, odmowy 54/57, komunikat 46, `ProtocolVersion` 15), **D22**, **`ContainerChange.Removed`** — zgodne z kontraktami (testy złote A/B).
- **Odstępstwa — akceptuję wszystkie:** `SurvivalRules` z `GameRules` i osobna `FunctionalBlockRules` zawsze obecna (lepszy podział niż w planie; D21 zachowane); `TakeAllIntent` na pustej skrzyni → `NotEnoughItems` (spójne z DECYZJĄ-10); szybkie odkładanie: najpierw dopełnienie stosów we wszystkich skrzyniach, potem puste sloty (dosłowne D16); jedna składowa bez `CompoundOp`; `IntentCodec` z `Enum.IsDefined`; `BlockEntityStore.TryGet` z wczesnym wyjściem; indeks gospodarzy po `BlockRef`; panel drzwi bez wpisu w treści.

**Poprawki (przed C2, jeden commit `fix(core): apply the core A+B review`):**

| Plik | Zmiana |
| --- | --- |
| `src/Horyzont.Core/Simulation/OperationApplier.cs` | upewnić się, że `_viewSet`, `_viewCreate` i lista oczekujących 49 są zerowane na **każdym** wyjściu z `TryApplyCompound` (także wczesny `return false` w liniach 258–262 i po `Rollback`) albo w `BeginOperation` dla każdej operacji, nie tylko złożonej; inaczej nieudana złożona zostawia widok efektywny następnej operacji |
| `tests/Horyzont.Core.Tests/Items/CompoundConstructTests.cs` | nowy `FailedCompound_ThenStandaloneOps_SeeNoStaleView`: złożona z `BlockSetOp` odrzucona na warunku ostatniej składowej, potem samodzielne `ContainerCreateOp` z gospodarzem tej komórki i `BlockEntitySetOp` → obie odrzucone (komórka pusta); tak samo po odrzuceniu w kroku cofania |
| `src/Horyzont.Core/Serialization/IntentCodec.cs` | gałąź `default` przełącznika odczytu rzuca `ProtocolViolationException` (wartość zdefiniowana w `IntentCode`, ale bez obsługi, nie może przejść jako `InvalidOperationException`); test `Read_DefinedButUnhandledCode_Throws` tylko, jeśli taki stan da się zbudować bez refleksji — inaczej wystarczy przegląd |
| `docs/ZASADY.md` (krok odbioru, nie teraz) | reguły z Pułapek: `OperationConstructs`, składowa nieodwracalna ostatnia z widokiem efektywnym, brak sierot przy `BlockClearOp` |

### Poprawki przeglądu Core A+B (2026-10-16, `fix(core): apply the core A+B review`)

- **Widok efektywny i oczekujące 49:** `BeginOperation` zerowało już `_viewSet`, `_viewCreate` i `_pendingStates` przed każdą operacją przedmiotową, więc następna operacja nie widziała śladów odrzuconej złożonej; mimo to czyszczenie jest teraz także na każdym wyjściu (`EndOperation` po `TryApplyCompound` i po składowej samodzielnej), żeby stan nie przeżywał operacji. Test `FailedCompound_ThenStandaloneOps_SeeNoStaleView` (jeden `OperationApplier` na kilka prób: odrzucenie na składowej po widoku, po stanie 49 i na warunku ostatniej składowej).
- **`IntentCodec`:** gałąź `default` odczytu już rzucała `ProtocolViolationException` (`Unhandled intent code`); kod bez zmian. Testu `Read_DefinedButUnhandledCode_Throws` nie ma: każda wartość `IntentCode` ma obsługę, więc stanu nie da się zbudować bez refleksji.
- **Szybkie odkładanie i „Weź wszystko” (przegląd niezależny):** `ContainerStore.OfKind(kind)` (indeks po rodzaju, posortowany po id); `ResolveQuickStack` wychodzi `NotEnoughItems` bez alokacji, gdy plecak jest pusty albo żadna skrzynia nie jest w zasięgu, i odwiedza tylko skrzynie z indeksu, nie `Containers.All`; `ResolveTakeAll` wychodzi `NotEnoughItems` przed kopiowaniem slotów, gdy skrzynia jest pusta. Testy `QuickStackRejectionTests` (1200 skrzyń, `GC.GetAllocatedBytesForCurrentThread` = 0 na ścieżce odrzucenia). Pasek gracza nie jest źródłem odkładania (D16), więc pusty pasek nie wpływa na wyjście.
- **`WorldStateBuilder.Build`:** kontener `Chest` z gospodarzem blokowym bez `ChestBlockState` (wskazującego ten kontener) jest odrzucany (`InvalidDataException`), test `Builder_ChestContainerWithoutState_Throws`; tolerancja zerwanego `BedRef` bez zmian.
