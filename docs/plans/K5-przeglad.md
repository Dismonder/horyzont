# K5 · Przegląd planów przed implementacją (Opus 5.5, 2026-10-10)

> **Stan (2026-10-10, ten sam PR planów):** B1–B5 i uwagi N1–N22 wprowadzone do plików planów; B6 rozwiązany sekcją „Pakiety K5” w `docs/plans/README.md`. Obowiązującą wersją reguł Y1–Y15 jest README (dla B3 przyjęto definicję zachowawczą: gotowość = zbudowana kolizja, limit czasu 20 s i stan `Unavailable` z bezpiecznym zawisem). Domyślnie K5-20 czeka na K3-14, ale nie na K4-19 (alternatywa w Y13, decyzja właściciela). Po poprawkach wszystkie siedem planów: **APPROVE** do startu wg README; plany K5-25 i K5-28 — do napisania.

Zakres: K5-20, K5-21, K5-22, K5-23, K5-24, K5-26, K5-27 (plany w `docs/plans/`), sekcja Kosmosu w `docs/plan/plan-projektu.md:108-122`, `docs/spec/02-kosmos.md` (wybrane sekcje), `docs/ZASADY.md` pkt 2–4. Wyrywkowa weryfikacja kodu na tym drzewie (`F:/dev/horyzont-plany-k5`, stan: K2 + K3-00 + K3-15, `ProtocolVersion = 4`, `MessageType` 1–21, `RejectReason` 0–21; K3-14, K3-16, K3-18, K4-19 nie są scalone).

## Werdykty

| Pakiet | Werdykt | Blokery | Uzasadnienie w jednym zdaniu |
| --- | --- | --- | --- |
| K5-20 Ramy | **REQUEST CHANGES** | B1, B2, B6 | dobra architektura (bez pływającego początku, test Jolt najpierw), ale przepięcie statku dziś wymusza pełny retransfer `ConstructState`, a ciało postaci nie umie zmienić planety |
| K5-21 Układ | **APPROVE WITH NITS** | (B6) | kompletny i spójny; jedyny problem z jego przepięciami stref naprawia K5-20 (B1) |
| K5-22 Tereny | **REQUEST CHANGES** | B2, B3, B6 | „gotowość” = wczytane dane, a nie zbudowana kolizja; zapytanie wokół statku daje fałszywe `Ready` albo zakleszczenie na 2 R |
| K5-23 Bloki | **REQUEST CHANGES** | B4, B6 | `ShipModeSetOp` z niezmienioną wersją budowli nigdy nie dotrze na żywo do klienta (własny test akceptacyjny padnie) |
| K5-24 Lot | **REQUEST CHANGES** | B3, B6 | bramka terenu ma niezdefiniowaną geometrię zapytania i semantykę dzierżaw; reszta bardzo dobra |
| K5-26 Replikacja | **REQUEST CHANGES** | B1, B5, B6 | definicja „identyczne” (bitowo nagłówek) jest niespełnialna przy kwantyzacji obrotu w migawce; bramka „łańcuch na żywo” + dzisiejszy retransfer przy przepięciu = znikanie statku |
| K5-27 Mapa | **APPROVE WITH NITS** | (B6) | kontrakty poprawne; `EntityReplication` już nie przekazuje op 22/23 (sprawdzone) |

Razem: **6 blokerów** (B1–B6), z czego B6 jest procesowy (wspólne reguły K5 w README). Po poprawkach B1–B6 wszystkie plany nadają się do startu wg sekcji „Kolejność startu”.

Pewność: **WYSOKA** dla B1, B4, B5 (potwierdzone w kodzie), **WYSOKA** dla B2 (kod `ServerCharacters`), **ŚREDNIA** dla B3 (zachowanie `IsAreaEditable` Voxel Tools dla pustej przestrzeni nad terenem nie zostało uruchomione — krok 1 K5-22 to rozstrzyga). Zmieniłoby zdanie: test silnika pokazujący, że `IsAreaLoaded` jest `true` dopiero po zbudowaniu kolizji LOD 0 (wtedy B3 sprowadza się do geometrii zapytania).

## Problemy blokujące

### B1. Przepięcie statku wysyła pełny `ConstructState` i (z K5-26) wyłącza jego migawki

- Gdzie: `K5-20-ramy-odniesienia.md:98` (decyzja 10), `:185` (krok 8); `K5-21-uklad-gwiezdny.md:126` (decyzja 16 — pierwszy produkcyjny producent `EntityReparentOp` na budowli); `K5-26-replikacja-statkow.md:87` (decyzja 4), `:127` (test `ShipReplication_ZoneTransition_ObserverRebasesWithoutJump`).
- Fakt w kodzie: `src/Horyzont.Server/Replication/EntityReplication.cs:498-501` — `ForwardReparent` dla budowli woła `_constructs.Enqueue(player, op.Entity)`; `ConstructStateSender.cs:92-104` — `Enqueue` na istniejącym łączu ustawia `LinkState.Pending` (już nie `IsLive`). Klient umie przepiąć budowlę na żywo: `ConstructMirror.cs:137-140` → `ConstructReplica.TryReparent` (`src/Horyzont.Core/Replication/ConstructReplica.cs:116`).
- Skutek: każde przejście strefy (3,0 R / 3,2 R) i każde `frame reparent` statku = retransfer całego statku do każdego obserwatora (`Bulk`, do 256 KiB). Z bramką K5-26 decyzja 4 („migawki dopiero, gdy łańcuch na żywo”) statek i jego pasażerowie znikają z migawek na czas transferu — także własny statek pasażera, wbrew decyzji 6 K5-26 i kryterium „płynny lot” (spec 02:234). Test 1000 przepięć K5-20 (wariant „budowla planeta ↔ układ”) ma szansę to wykryć dopiero w fazie C.
- Poprawka (K5-20, krok 8, decyzja 10 dopisać zdanie): „`ForwardReparent`: budowla, której łącze u odbiorcy jest `Live` → `_constructs.SendLive(player, message)` (`OperationApplied { EntityReparentOp }`); `Enqueue` tylko gdy łącze nie jest `Live`”. Test `Reparent_ConstructLive_SentAsOperation_NoTransfer` w `tests/Horyzont.Server.Tests/Replication/`. K5-26 decyzja 4 dostaje odwołanie do tej reguły (Y8). Jeśli K5-21 scali się przed tą poprawką — nie scalać (kolejność K5-20 → K5-21 i tak to gwarantuje).

### B2. Ciało postaci nie obsługuje zmiany planety (wysiadanie na księżycu, `place`)

- Gdzie: `K5-20-ramy-odniesienia.md:103` (decyzja 15: „po przepięciu do planety ciało wraca do chodzenia z pozą z operacji”), `K5-22-wiele-terenow.md:109` (decyzja 15, komenda `place` — `EntityReparentOp` gracza na inną planetę) i kryterium 3 K5-22 (`planets4`: boty rozstawiane `place` po 2 na ciało).
- Fakt w kodzie: `game/src/Server/Characters/ServerCharacters.cs:130-138` ustala `character.Planet` raz przy tworzeniu ciała (odmowa „the planet has no terrain”, gdy brak węzła), `:211` i `:337` przekazują dalej starą planetę i ramę do `UpdatePlayerKinematics`; nigdzie nie ma reakcji na `EntityReparentOp`. Po przepięciu na inną planetę `UpdatePlayerKinematics` zwróci `false` (stara rama) i postać utknie.
- Poprawka (K5-20, decyzja 15 + krok 13): „`ServerCharacters` reaguje na każde przepięcie własnego gracza (zdarzenie `ServerSimulation.Applied` z `EntityReparentOp` dla gracza): rama `Construct` → ciało przyczepione; rama `Planet(p)`: gdy `p` = planeta ciała — powrót do chodzenia; gdy `p` ≠ planeta ciała — despawn ciała i spawn na `p` z pozą z operacji, w trybie `Frozen` do `IsAreaLoaded` (jak dzisiejszy spawn), z widzem na nowym terenie”. Test silnika `ServerCharacters_ReparentToOtherPlanet_RespawnsOnTarget` (na scenie z dwoma terenami — można w K5-22, wtedy K5-20 daje test na atrapie terenu). K5-22 decyzja 15 dopisuje: „`place` wymaga reguły Y10”. Kliencka strona (`LocalPlayer`) — K5-20 decyzja 14 (rebase własnego bytu) + K5-22 decyzja 10 (widze); dopisać do K5-22 kroku 6 przypadek „własny byt zmienia planetę → `ClientPlanetTerrains` aktywuje teren nowej planety niezależnie od 4 R kamery”.

### B3. Semantyka „gotowości terenu” dla lotu jest niespójna między K5-22 i K5-24

- Gdzie: `K5-22-wiele-terenow.md:100` (decyzja 6: `Ready` = `IsAreaLoaded` wokół `localCenter, radius`), `:99` (decyzja 5: widz statku ≤ promień + 512 m); `K5-24-model-lotu.md:100` (decyzja 6.6: `NotLoaded` i `r < 2 R` → brak ruchu w dół), `:113` (decyzja 17: `Query(planet, localCenter, radius)`), `:114` (decyzja 18: odstawienie czeka na `Ready`); spec 02:207-208 („do gotowości **kolizji**”, „kolizja nie zdążyła się zbudować → zawis”).
- Fakt w kodzie: `game/src/Terrain/VoxelTerrainService.cs:145-164, 444-458` — `IsAreaLoaded` = `VoxelTool.IsAreaEditable` (dane wokseli wczytane), nie „kolizja zbudowana”.
- Skutek: (a) zapytanie w środku statku wysoko nad terenem dotyczy powietrza: albo daje `Ready` od razu (dane powietrza wczytane przez widza statku), choć grunt pod statkiem nie ma kolizji → przelot przez teren przy `v_min = 100 m/s`; albo nigdy nie daje `Ready` → statek wisi na 2 R na zawsze (`Altitude` na 2 R = R − 300 ≥ 1100 m > `CollisionPendingAltitude` 400 m, więc gałąź `CollisionPending` nie zadziała). (b) Odstawienie po wczytaniu (`ShipLoadSettler`) trzyma statek nieruchomo, a polityka K5-22 traktuje jako zapotrzebowanie tylko statki „poruszające się o > 0,5 m” — teren się nie aktywuje, jeśli nie ma dzierżawy, a K5-24 nie mówi, kto i kiedy ją bierze („przez adapter”).
- Poprawka (wspólny kontrakt, Y11; zatwierdza Opus przed kodem K5-22 kroku 4 i K5-24 kroku 9):
  1. `IPlanetTerrainReadiness.Get(PlanetId, Vector3 localCenter, float radius)` → `Ready` wtedy i tylko wtedy, gdy węzeł aktywny **i** dla bloków LOD 0 przecinających kulę jest zbudowana kolizja (krok 1 K5-22 sprawdza API Voxel Tools — `VoxelLodTerrain.is_area_meshed` albo licznik zadań meshingu; w razie braku: `IsAreaLoaded` + brak oczekujących siatek LOD 0 w kuli z `ChunkMeshApplied`); kula w całości powyżej `R + MaxRelief + 64` m (poza `PlanetTerrainLayout.HalfExtentMeters`) → `Ready` przy aktywnym węźle (nie ma tam czego budować).
  2. K5-24 (`ShipFlightSystem`/adapter) pyta o **kolumnę pod statkiem**: środek = punkt na promieniu pod statkiem w wysokości `max(R + MaxRelief, r − d/2)`, promień = `promień statku + d/2`, gdzie `d = min(Altitude, 2 s · |v| + 64)`; `Streaming` w tej kolumnie przy `Altitude < 2 s · v_lim + CollisionPendingAltitude` → `CollisionPending` (zawis), wyżej → `NotLoaded` (nie niżej niż 2 R).
  3. Widz statku (K5-22 decyzja 5) sięga kolumny: `ViewDistance = promień + clamp(2 s · prędkość, 64, 512)` liczone **w dół do gruntu**, tj. widz stawiany w punkcie kolumny z ust. 2, nie w środku statku.
  4. Zapotrzebowanie: polityka K5-22 traktuje statek w ramie planety jako zapotrzebowanie także gdy stoi, o ile nie jest zaparkowany — K5-22 dostaje to przez `IShipDemandSource` (domyślnie „ruch o > 0,5 m”); K5-24 (scalany później albo wcześniej — X10) podmienia na „`Mode == Ship` ∧ ¬`Parked` w `ShipStateRecord`”. Wtedy odstawianie i autolądowanie bez graczy nie potrzebują dzierżaw; dzierżawy zostają dla powtórki i `place`.
  5. Test wspólny w pakiecie scalanym później: `ShipGate_DescentFrom2R_NeverCollisionlessBelow50m` (silnik, sonda z K5-22 sterowana `ShipFlightModel`).

### B4. `ShipModeSetOp` z niezmienioną wersją budowli nie dociera na żywo do luster

- Gdzie: `K5-23-bloki-statku.md:83` (decyzja 4: „wersja bloków bez zmian, `ConstructChanges` z bieżącą wersją”), `:148` (test `ShipMode_ThirtyBlockConstruct_ToShipAndBack_ViaIntents`: „dwie `OperationApplied`” u klienta), `:150` (lustro w fazie B).
- Fakt w kodzie: `src/Horyzont.Server/Replication/ConstructStateSender.cs:194-204` — dla łącza `Live` zmiana z `Version != SentVersion + 1` kończy się `Enqueue` (pełny retransfer, `Restarted`), a w trakcie transferu (`:210-213`) — `Dropped`. Klient nigdy nie dostanie `OperationApplied { ShipModeSetOp }`; dostanie nowy `ConstructState`. Test akceptacyjny K5-23 w obecnym brzmieniu padnie, a każdy przełącznik trybu kosztuje retransfer u wszystkich obserwatorów.
- Poprawka (K5-23 decyzja 4, wariant domyślny): applier `ShipModeSetOp` **podbija `Construct.Version` o 1** (jak operacja bloków; wersja jest w skrócie — `StateHasher.cs:67` — więc skutek deterministyczny i odtwarzalny), `ConstructChanges` niesie nową wersję; `ConstructReplica.TrySetMode` przyjmuje ją jak każdą zmianę (`Version == known + 1`). Alternatywa (odrzucona, większa): osobna ścieżka „zmiana nagłówka bez wersji” w `ConstructStateSender`. Dodać test serwera `ShipModeSetOp_LiveObserver_ReceivesOperationNotTransfer`.

### B5. Kryterium „identyczne” w K5-26 jest niespełnialne przy obecnym formacie migawki

- Gdzie: `K5-26-replikacja-statkow.md:95` (decyzja 12: „nagłówek lustra (`Mode`, `Parent`, `ParentSeq`, `Origin`, `Rotation` — bitowo) = serwer” po każdym etapie, gdy statki stoją „na postoju”), `:145`, `:71` (kryterium 1).
- Fakt: statek zaparkowany po locie ma dalej `Mode == Ship`; jego poza u klienta pochodzi z migawek (K5-20 decyzja 12), a migawka pakuje obrót do 32 bitów (`src/Horyzont.Core/Serialization/SnapshotWriter.cs:79`, `Quantization.PackRotation`). Nagłówek lustra `Ship` nie jest odświeżany ruchem (lot jest poza dziennikiem), więc porównanie bitowe nagłówka albo obrotu zawsze padnie, niezależnie od poprawności implementacji.
- Poprawka (K5-26 decyzja 12): „po każdym etapie: (a) pola z operacji i `ConstructState` (`Mode`, `Parent`, `ParentSeq`, wersja bloków, fotel, rama pasażera, `ParentSeq` pasażera) — bitowo; (b) poza statku `Ship` — z ostatniej migawki: pozycja `f32` bitowo, obrót w granicy kwantu `PackRotation` (kąt ≤ 2·kwant, stała z `Quantization`); (c) etap kończący serię przełącza statki na `Static` (`ShipModeIntent` → `ShipModeSetOp` niesie pozę) i wtedy nagłówek `Origin`/`Rotation` — bitowo (wymaga B4)”. Pomiar w locie (≤ 0,5 m) bez zmian.

### B6. Brak wspólnych reguł K5 i sprzeczne miejsce ich zapisu

- Gdzie: `docs/plans/README.md:83-196` ma tylko K3 (X1–X11); `K5-20-ramy-odniesienia.md:200` (krok 14: „dopisać sekcję »Pakiety K5«”), `K5-23-bloki-statku.md:176`, `K5-21-uklad-gwiezdny.md:220`, `K5-24-model-lotu.md:198`, `K5-22-wiele-terenow.md:203`, `K5-26-replikacja-statkow.md:177`, `K5-27-mapa-i-nawigacja.md:220` — każdy „dopisuje wiersz” do sekcji, którą tworzy dopiero ostatni krok K5-20 (a K5-23 ma być scalony wcześniej).
- Poprawka: sekcję „Pakiety K5” z regułami Y1–Y15 (poniżej) wpisuje do `docs/plans/README.md` mały PR planów przed startem pierwszego pakietu; pakiety tylko dopisują wiersz scalenia i wartość `ProtocolVersion` (Y3). W planach zamienić „dopisać sekcję” na „dopisać wiersz w sekcji »Pakiety K5«”.

## Uwagi nieblokujące

### K5-20
- N1 (`:92`, decyzja 4): „wynik bez zagnieżdżenia bitowo ten sam” — zagwarantować, że ścieżka głębokości 1 w `BlockOverlap.TryGetPose` nie przechodzi przez `double` (inaczej zaokrąglenia mogą się różnić); zapisać to w decyzji.
- N2 (`:8`, `:198`): faza E czeka na K4-19, ale żaden test K5-20 nie używa kodu K4-19 (test 1000 przepięć jest na `InMemoryNetwork`). To wydłuża ścieżkę krytyczną całego K5 (K5-21…K5-28 czekają na scalenie K5-20). Rekomendacja: pytanie do właściciela — scalić K5-20 po K3-14 + K3-15 bez czekania na K4-19 (Y13).
- N3 (`:97`): `ServerFrameHistory` i `ServerPlanetTerrains` (K5-22) oba w fazie `Compose` — kolejność w obrębie fazy ustala Y7.
- N4: `UpdateConstructKinematics` i „stan zmienia się tylko operacjami” (ZASADY pkt 4) — krok 14 dopisuje wyjątek; dobrze, zostawić.

### K5-21
- N5 (`:124`, decyzja 14): `StarSystemState` to do ~560 B (8 ciał × 65 B), nie ~120 B; poza budżetem jednorazowo — dopisać do Y5.
- N6 (`:9`): start dopiero po scaleniu K5-20 — dopuścić start „na stosie” (Y13).
- N7: świat z `StarSystem == null` (≠ 1 planeta) — K5-24 `FlightEnvironment` i K5-22 polityka muszą to obsłużyć (tylko planeta domowa); dopisać jedno zdanie w obu planach (konsumenci), nie w K5-21.

### K5-22
- N8 (`:8`, „Fazy”): faktyczne zależności kroku 5 to K3-16 (`ServerTerrainReplicator`), kroku 6 — K3-14 (`LocalPlayer`), fazy D — K4-19; są spełnione przechodnio przez K5-20, ale wpisać je jawnie w tabelę faz.
- N9 (`:142`): test w 108 km używa `TrySampleSolid` z K5-23 — dopisać „jeśli K5-23 scalony”.
- N10 (`:239`): duplikat `PlanetSurfaceSampler`/`PlanetMapSampler` — ustalić jedno miejsce: `game/src/Planet/Surface/PlanetSurfaceSampler.cs` (K5-22), K5-27 używa go albo (gdy scalony wcześniej) K5-22 przenosi `PlanetMapSampler` tam i K5-27 zostaje z cienkim adapterem.

### K5-23
- N11 (`:84`, decyzja 5): walidator `ShipModeIntent` zakłada gracza w ramie planety (`WrongPlanet`, oko w ramie planety). Pilot siedzący w kokpicie (K5-25, rama budowli) nie przełączy statku na `Static` po lądowaniu. Reguła Y9 (walidacja niezależna od ramy) — wdraża K5-25; jeśli K5-20 jest już na `main`, K5-23 od razu liczy „planetę gracza” przez `FrameGraph.TryGetRegion` i oko w ramie budowli przez `FrameGraph.TryExpress`.
- N12 (`:85`, decyzja 6): „maks. promień terenu + 2 m” — wskazać źródło (`R + PlanetGeneratorV1.MaxReliefMeters` przekazane z gry przez `GameRules`/opcje; Core nie zna generatora).
- N13: dane `ShipFixtures.Thirty` (4 silniki × 60 kN, masa ok. 8–9 t) — sprawdzić w kroku 4, że ciąg w dół > ciężar przy g = 12 m/s² (planeta C); inaczej K5-24 nie wyląduje na C z zawisem. Dane, nie kod.

### K5-24
- N14 (`:94-95`, decyzja 6.1): `LaunchBlock` steruje wyjściem z postoju, a nie ma go w żadnym wejściu `Step` (`ShipMotion`, `ShipControl`, `ShipStats`, `FlightEnvironment`). Dodać `ShipLaunchBlock LaunchBlock` do `FlightEnvironment` (serwer liczy, predykcja K5-25 bierze z `ShipStatus`) — w kontrakcie kroku 1.
- N15 (`:91`): `AtmosphereDragPerSecond` indeksowany „indeksem ciała” — jawnie `BodyPlacement.BodyIndex`, a dla świata bez układu indeks 0.
- N16 (`:109`, decyzja 13): `ShipFuelAddOp` stosuje `min(Fuel + Units, Capacity)` na paliwie spoza skrótu; po awarii powtórka liczy od paliwa z checkpointu, więc wynik zależy od chwili checkpointu. Rozważyć semantykę absolutną (`Fuel = Capacity`, op niesie tylko `Capacity`) — prostsza i idempotentna; wymaga zgody Opus w kroku 1 (spec nazywa op „Add”, ale nie definiuje układu bajtów).
- N17: `SurfaceClearanceMeters = 300` poprawne dla V1 (wszystkie ciała mają profil domowej — K5-21 decyzja 1); przy nowym profilu przenieść do danych ciała.

### K5-26
- N18 (`:91`): `ShipStatus` ~ 34 B + nagłówek przy 2 Hz — w Y5. 
- N19 (`:90`): `KindData` gracza = fotel — K5-25 musi użyć tej samej definicji (Y2/Y12).

### K5-27
- N20 (`:97`, decyzja 1): twierdzenie „z każdego miejsca układu najbliższe ciało jest bliżej niż 100 km” jest fałszywe (granica układu 400 km, K5-24 decyzja 6.6). Skutek nieszkodliwy (op odrzuca |p| > 100 km), ale poprawić tekst: „znaczniki tylko na/nad ciałem; punkt dalej niż 100 km od każdego ciała nie jest znacznikiem”.
- N21 (`:109`): `MarkersState` do ~37 KB jednorazowo przy wejściu — zaliczyć jako `Bulk` w `IReplicationSendBudget` albo podzielić; reguła Y5.
- N22 (`:115-116`): sprawdzone — `EntityReplication.OnApplied` (`EntityReplication.cs:452-456`) przekazuje tylko operacje z `ConstructChanges` albo `EntityReparentOp`, więc op 22/23 nie wyciekną; krok 0 może to zapisać jako potwierdzone, „pominięcie 22/23” nie jest potrzebne.

## Reguły wspólne K5 (Y1–Y15)

### Y1. Sekcja „Pakiety K5” w README przed startem
Tworzy ją PR planów (Opus) z tym dokumentem jako źródłem: tabela pakietów, Y1–Y15, kolejność startu i scalania. Pakiety dopisują tylko wiersz scalenia (numer PR, wartość `ProtocolVersion`).

### Y2. Numeracja (zamrożona; zakresy K1-03:616-622)

| Rodzaj | Kod → typ (pakiet) | Wolne |
| --- | --- | --- |
| `OperationKind` 20–29 | 20 `ShipModeSet` (K5-23), 21 `ShipFuelAdd` (K5-24), 22 `MarkerSet`, 23 `MarkerRemove` (K5-27) | 24–29 |
| `IntentCode` 10–19 | 10 `ShipMode` (K5-23), 11 `SeatEnter`, 12 `SeatExit`, 16 `SeatEvict` (K5-25), 13 `Refuel` (K5-24), 14 `MarkerSet`, 15 `MarkerRemove` (K5-27) | 17–19 |
| `RejectReason` 30–39 | 30 `NoCockpit`, 31 `ShipAnchored`, 37 `NoEngine` (K5-23); 32 `SeatOccupied`, 33 `SeatOutOfReach`, 34 `EjectTooFast`, 35 `NotPilot`, 38 `ConstructNotEmpty` (K5-25); 36 `MarkerLimit` (K5-27); 39 `ShipNotParked` (K5-24) | **brak** — zakres wyczerpany; kolejna odmowa Kosmosu = decyzja właściciela (np. ogólny `InvalidParameters`) |
| `MessageType` 30–39 | 30 `ShipInputBatch`, 34 `ShipCorrection` (K5-25); 31 `ShipStatus` (K5-26); 32 `StarSystemState` (K5-21); 33 `MarkersState` (K5-27); 35 `MapPresence` (rezerwacja warunkowa K5-27 P1) | 36–39 |
| `BlobKind` 10–19 | 10 `StarSystemParams` (K5-21), 11 `Marker` (K5-27), 12 `ShipState` (K5-24) | 13–19 |
| Migracja | `M0002_Space` (K5-21; trzy tabele, numer = następny wolny przy scaleniu) | K5 nie dodaje innych |
| Treść (nie format) | `BlockFunctionKind` 0–9 (K5-23), 10 `RefuelStation` (K5-24); `ShipLaunchBlock` 0–1 (K5-24), 2 `PersonInHull` (K5-25, w `ShipStatus` → podbicie D4) | — |
| Semantyka bez kodu | `EntitySpawn.KindData` gracza = `[1][x i32][y i32][z i32]` fotel (K5-26; K5-25 używa tej definicji) | — |

Nazwy rekordów = nazwy enuma (`ProtocolContractTests`). Bajt wersji rekordów istniejących rodzajów bez zmian; nowe zaczynają od 1.

### Y3. D4 — `ProtocolVersion`
Podbijają (main przy scaleniu + 1): K5-20, K5-21, K5-22, K5-23, K5-24, K5-25, K5-26, K5-27 — każdy aktualizuje **istniejące** `ProtocolVersion_MatchesDocumentedMergeValue` i `Constants_GoldenValues`, bez własnych testów wersji. Planowo (po K3: 4 = K3-15, 5 = K3-16, 6 = K3-18): K5-23 → 7, K5-20 → 8, K5-21 → 9, K5-22 → 10, K5-24 → 11, K5-27 → 12, K5-26 → 13, K5-25 → 14. Faktyczną wartość wpisuje wykonawca w README po scaleniu.

### Y4. Router i kanały (X5/X6 dla K5)

| Typ | Kanał | `IsAllowedFromClient` | `IsAllowedFromServer` | Właściciel |
| --- | --- | --- | --- | --- |
| 30 `ShipInputBatch` | zawodny | `InGame` (`IsGameMessageFromClient`) | — | K5-25 |
| 31 `ShipStatus` | zawodny | — | `InGame` | K5-26 |
| 32 `StarSystemState` | `ReliableOrdered` | — | `Loading`, `InGame` | K5-21 |
| 33 `MarkersState` | `ReliableOrdered` | — | `Loading`, `InGame` | K5-27 |
| 34 `ShipCorrection` | zawodny | — | `InGame` | K5-25 |

Typy 30, 31, 34 dopisać do listy zawodnych w `MessageChannels` i w `MessageChannels_MapsEveryType`. Każdy typ: `ProtocolContractTests`, `MaxMessages.All()`, `CodecGen`, test routera w `tests/Horyzont.Net.Tests` (obie strony, dozwolone i zabronione stany).

### Y5. Budżet pasma (rozszerzenie X3, na klienta, S→K)

| Ruch | Mechanizm | Wartość |
| --- | --- | --- |
| Migawki (statki i pasażerowie jako zwykłe byty, K5-20/26) | `ISnapshotBudget` | 20 480 B/s bez zmian |
| `ShipCorrection` (K5-25, przy migawce pilota) | **wliczona do `ISnapshotBudget`** tej samej rundy | ≤ ~64 B × 20 Hz |
| `ConstructState` statków (K5-26 priorytety), `OperationApplied` (w tym op 20, 22, 23 i przepięcia na żywo z Y8), `MarkersState` (jako `Bulk`, jednorazowo ≤ 37 KB) | `IReplicationSendBudget` (wspólny) | 27 000 B/s bez zmian |
| `StarSystemState` (jednorazowo ≤ ~600 B), `ShipStatus` (2 Hz, ~45 B), `IntentReply`, `EntitySpawn/Despawn` | poza budżetem (sterowanie) | razem ≤ 2,5 KB/s zapasu |

K→S: `ShipInputBatch` jak `InputBatch` (K5-25 podaje rozmiar i częstotliwość; cel ≤ 4 KB/s). Retransfer statku przy przepięciu jest zakazany (Y8).

### Y6. Warstwy projektów (X7)
Bez nowych referencji projektów; `LayeringTests.cs:26-31` bez zmian w K5. `Horyzont.Server` nie zależy od `Horyzont.Constructs` (tabelę treści `BlockContentTable` składa `game/` i przekazuje do `ServerSimulation`); `Horyzont.Client` nie zależy od `Horyzont.Server`. Logika testowalna bez silnika: Core (`Frames/`, `Space/`, `Ships/`), Server (`Frames/`, `Space/`, `Ships/`, `Markers/`), Client (`Frames/`, `Space/`, `Ships/`, `Markers/`, `Navigation/`).

### Y7. Fazy ticka i kolejność uczestników
- `Frames`: `ShipFlightSystem` (K5-24) → `ServerZoneTransitions` (K5-21).
- `Characters` (domyślna): istniejący uczestnicy (`ServerCharacters`, `HostConstructs`, replikacja) w dzisiejszej kolejności.
- `Compose`: `ServerFrameHistory` (K5-20) → `ServerPlanetTerrains` + `ServerShipViewers` (K5-22) → `ShipStatusSender` (K5-26).
Każdy pakiet rozszerza test kolejności K5-20 (`ServerRuntime_Participants_RunInPhaseOrder_StableWithinPhase`) o swojego uczestnika; rejestracja w swoim partialu, kolejność międzypakietowa wg tej listy (integruje scalany później).

### Y8. Zmiany nagłówka budowli docierają na żywo (B1, B4)
Każda operacja zmieniająca nagłówek budowli bez zmiany bloków (`EntityReparentOp` budowli, `ShipModeSetOp`) trafia do odbiorcy z łączem `Live` jako `OperationApplied`, nigdy jako retransfer. `EntityReparentOp` — przez `ForwardReparent` → `SendLive` (K5-20); `ShipModeSetOp` — przez podbicie wersji (K5-23). Retransfer tylko dla łączy nie-`Live` i `ResyncRequest`.

### Y9. Intencje gracza w ramie budowli
„Planeta gracza” = `FrameGraph.TryGetRegion(ramy gracza)`; oko serwera wyrażane w ramie celu (`FrameGraph.TryExpress`) przed sprawdzeniem zasięgu i linii wzroku. Dotyczy `ShipModeIntent` (K5-23), `RefuelIntent` (K5-24), intencji foteli (K5-25) i istniejących intencji bloków. Właściciel wspólnego pomocnika i testów pilota w kokpicie: K5-25 (najpóźniej); K5-23/K5-24 piszą swoje walidatory przez ten pomocnik, jeśli K5-20 jest już na `main`.

### Y10. Ciało postaci przy każdym przepięciu (B2)
`ServerCharacters` obsługuje: planeta → budowla (przyczepione), budowla → planeta (ta sama albo inna), planeta → inna planeta (respawn na terenie celu w `Frozen` do gotowości). Właściciel: K5-20; konsumenci: K5-22 (`place`), K5-25 (wysiadanie, wyskok).

### Y11. Gotowość terenu dla lotu (B3)
Kontrakt z B3 ust. 1–5; właściciel interfejsu: K5-22; adapter `PlanetTerrainShipGate` i geometria kolumny: pakiet scalany później z pary K5-22/K5-24; przegląd kontraktu: Opus przed kodem.

### Y12. Mapa kolizji plików (integruje scalany później — X10)

| Plik / punkt | Pakiety (treść) | Reguła |
| --- | --- | --- |
| `ServerSimulation.cs` | 20 `UpdateConstructKinematics`; 21 `PreparePlayerJoin`; 23 konstruktor z `BlockContentTable`; 24 `UpdateShipState` | rozłączne metody |
| `OperationApplier.cs`, `OperationCodec`, `OperationKind` | 23 (20), 24 (21), 27 (22, 23) | przypadki w kolejności numerów; bez partiali |
| `IntentValidator.cs`, `IntentResolver.cs`, `IntentCodec`, `GameRules` | 23 (10), 24 (13), 27 (14, 15, kubełek), 25 (11, 12, 16), pomocnik Y9 | jw. |
| `RejectReason.cs` | 23, 24, 25, 27 | wartości wg Y2 |
| `WorldState.cs`, `WorldCheckpoint.cs`, `WorldStateBuilder.cs` | 21 (`StarSystem`, `HomePlanet`), 24 (`ShipStates`), 27 (`Markers`, `MarkerIds`) | sąsiednie pola; parametry rekordów checkpointu z wartościami domyślnymi na końcu |
| `StateHasher.cs` | 21 (sekcja układu po planetach, warunkowa), 27 (znaczniki na końcu, warunkowa) | kolejność: planety → układ → budowle → … → znaczniki; `ShipState` nigdy |
| `BlobKind`, `StateCodec` | 21 (10), 27 (11), 24 (12) | jw. |
| `PersistenceWriter`, `WorldLoader`, `WorldCreator`, `Sql` | 21, 24, 27 | rozłączne bloki w tej samej transakcji |
| `Message*`, `MessageRouter`, fixture X6 | 21 (32), 25 (30, 34), 26 (31), 27 (33) | Y4 |
| `EntityReplication.cs`, `SnapshotBuilder.cs`, `ConstructStateSender.cs` | 20 (`Ship` w migawkach, region, Y8), 26 (zasięgi, kolejność, bramka, priorytety), 25 (`ShipCorrection`) | rozłączne metody; rekomendacja 26 → 25 |
| `EntityMirror.cs` | 20 (`WrongFrameApplied`), 26 (`TryGetSeat`) | jw. |
| `ServerCharacters.cs` | 20 (Y10), 22 (widze — tylko przez `ServerShipViewers`), 25 | 20 właścicielem |
| `LocalPlayer.cs` (właściciel K3-14) | 20 (rebase, `Frozen`), 22 (widze → `ClientPlanetTerrains`), 25 (pilot) | edycje ograniczone; scalany później rebase |
| `VoxelTerrainService.cs`, `ITerrainService.cs`, `FakeTerrainService` | 23 (`TrySampleSolid`), 22 (`RemovePlanet`) | rozłączne |
| `VoxelServerTerrainFactory.cs`, `OwnTerrainProvider.cs` | 21 (strażnik), 22 (usuwa strażnika) | 22 po 21 |
| `ShipStats`/`ShipStatsCalculator` | 23, 24 (`ControlFrame`), 25 (fotele) | dopisywanie pól |
| `ConsoleCommandProcessor.cs` | 20 `frame`, 23 `ship`, 24 `ship pilot/fuel`, 22 `place` | osobne podkomendy, format linii stały |
| `game/content/blocks/blocks.json`, `game/locale/*.csv`, `project.godot` | 23 (G, bloki), 24 (stacja, HUD), 27 (M, mapa), 25 | dopisywanie; klawisze sprawdzić w kroku 0 |
| `GameComposition.*.cs` | 20 `.Frames`, 21 `.Space` (+1 wywołanie w `GameComposition.cs`), 22 `.Planets`, 23 `.Ships`, 24 `.Flight`, 25 `.Seats`, 26 `.ShipReplication`, 27 `.Navigation` | osobne partiale; podmiany jednej linii wg planów |
| `tests/Horyzont.Testing/Ships/` | 20 `TestShips`, 23 `ShipFixtures`, 24 `SphereGroundCollider`, `ScriptedControlSource` | różne pliki |
| `docs/ZASADY.md`, `docs/plans/README.md` | wszyscy | dopisywanie wierszy (Y1) |

### Y13. Ścieżka krytyczna i start „na stosie”
K5-21 (i przez niego 22/24/27) czeka na scalenie K5-20, a K5-20 — na K3-14 (fazy C–D) i K4-19 (faza E). Decyzja domyślna: K5-21 może ruszyć na gałęzi od `pakiet/k5-20-ramy` po zakończeniu fazy A K5-20 (Core ram jest wtedy stabilny), z PR do `main` dopiero po scaleniu K5-20 i rebase. Pytanie do właściciela: czy K5-20 może się scalić bez K4-19 (N2).

### Y14. Korpus złoty i determinizm
Każdy pakiet K5: `git diff origin/main` pusty na `tests/Horyzont.Core.Tests/Golden/TerrainJournals/`, `TerrainJournalGoldenCorpusTests.cs`, `Hashing/DeterminismTests.cs`; nowe sekcje skrótu tylko warunkowe; świat z `WorldFactory.CreateDefault()` ma niezmieniony skrót (test stałej przed zmianą hashera — K5-21 i K5-27 robią to oba, scalany później zachowuje stałą).

### Y15. Testy silnika na wspólnym runnerze
Testy `game/tests/Frames|Space|Ships|Terrain|Planet|Navigation` uruchamiane szeregowo (jeden runner `horyzont`); pomiary z silnikiem wg X11 (czas sieci wirtualny + zmierzony czas silnika, raport obu); `Descent8km` 3 próby w `game`, 20 w nocnym `bench`.

## Pokrycie K5-25 i K5-28 (planów brak — co muszą przyjąć)

K5-25 (Sol, po K5-24): intencje 11/12/16, odmowy 32–35/38, komunikaty 30/34 (Y2, Y4); implementuje `IShipControlSource` (K5-24) z `ShipControl` 1:1 na tick i `NotPilot`; predykcja pilota tym samym `ShipFlightModel.Step` i `FlightRules` (z `LaunchBlock` w `FlightEnvironment`, N14); fotele przez `FrameTransitions.TryReparent(…, seat, …)` i `KindData` z K5-26; `ShipLaunchBlock.PersonInHull = 2` z obrysu `ShipHullBoxes`; `PlayerLeaveOp` z ramą statku i fotelem, powrót do fotela, zwolnienie fotela → punkt odrodzenia (spec 02:192, :203-204); Y9 (pilot w kokpicie przełącza tryb i tankuje); `IMapOpener.Open` na stanowisku nawigatora (K5-27); `ShipCorrection` w budżecie migawek (Y5); kryteria: 1000 wsiadań/wysiadań/wyskoków bez skoków > 0,5 m (spec :225), korekty < 0,5 m w 95 % przy RTT 100 ms (spec :226), blokada startu z komunikatem (spec :229). Plan K5-25 trzeba napisać i przejrzeć (Opus) przed scaleniem K5-24.

K5-28 (Sonnet, po 22/25/26/27): Jolt na prawdziwej planecie C z postacią **i statkiem** (spec :219, powtórzenie kryterium 1 K5-20); 8 botów lata na 4 ciała (rozszerzenie `planets4` K5-22 o loty `DevTransitPilot`/pilota K5-25), mediana przelotu do najbliższego sąsiada ≤ 90 s (spec :224); odtwarzanie z lecącym statkiem i graczami w fotelach + statki odstawione po wczytaniu (spec :227); autolądowanie na terenie księżyca, B i C (K5-24 testuje tylko teren domowy); kryteria ukończenia spec :233-237 jako scenariusze regresji (CI vs nocny `bench`).

## Kolejność startu i scalania

| Fala | Pakiety (równolegle) | Warunek startu |
| --- | --- | --- |
| 0 | PR planów: ten przegląd + sekcja „Pakiety K5” (Y1–Y15) + poprawki B1–B6 w planach | — (Opus) |
| 1 | K5-23 (Sonnet), K5-20 fazy A–B (Opus) | po fali 0; K3-15 jest już na tym drzewie, więc faza B K5-23 i K5-20 też może ruszyć |
| 1C | K5-20 fazy C–D | po scaleniu K3-14 |
| 2 | K5-21 (Opus) | K5-20 scalony (albo na stosie po fazie A — Y13) |
| 3 | K5-22 (Opus), K5-24 (Sonnet; wymaga też K5-23), K5-27 (Gemini Flash + przegląd Opus Core) | K5-21 scalony |
| 3a | napisanie i przegląd planów K5-25 (Sol) i K5-28 | przed scaleniem K5-24 |
| 4 | K5-26 (Opus), K5-25 (Sol) | K5-24 scalony |
| 5 | K5-28 (Sonnet) | K5-22, K5-25, K5-26, K5-27 scalone |

Rekomendowane scalanie: **K5-23 → K5-20 → K5-21 → K5-22 → K5-24 → K5-27 → K5-26 → K5-25 → K5-28** (kolejność nie warunkuje startu; integruje scalany później — X10; po każdym scaleniu wiersz w README z `ProtocolVersion`).

## Następne kroki dla wykonawcy planów

1. Wprowadzić B1–B6 do plików planów (K5-20 decyzje 10 i 15 + kroki 8, 13; K5-22 decyzje 5, 6, 15 + krok 4; K5-23 decyzja 4 + test; K5-24 decyzje 17, 18 + N14; K5-26 decyzje 4, 12) i sekcję „Pakiety K5” w `docs/plans/README.md`.
2. Uzyskać odpowiedź właściciela na N2/Y13 (scalanie K5-20 bez K4-19, start K5-21 na stosie).
3. Zlecić napisanie planu K5-25 według listy „Pokrycie K5-25”.
