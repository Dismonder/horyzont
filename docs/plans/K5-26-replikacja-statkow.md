# K5-26 · Replikacja statków i pasażerów

| | |
| --- | --- |
| Prowadzi | Opus 5.5 (Claude Code, `claude --model opus`) |
| Testy akceptacyjne | Opus 5.5 (krok 1 — szkielet testu akceptacyjnego dwóch klientów i uprzęży lotów, czerwony, przed kodem replikacji) |
| Przegląd | GPT-6.1 Sol (ZASADY pkt 9: pakiet Opus → drugi przegląd Sol); zmiany w `src/Horyzont.Core/` (komunikat 31, `KindData` gracza) przegląda Opus 5.5 w świeżej sesji (ZASADY pkt 3) |
| Zależy od | K5-24 scalony na `main` (spec 02:251). Pośrednio K5-20 (statki `Ship` w migawkach, region przez `FrameGraph`, `Rebase`), K5-21 (ramy ciał, strefy), K5-23 (statki z bloków, fotele w `ShipStats`), K3-15 (replikacja bytów), K4-19 (`CoopRig`, `LatencyTransport`). **Nie** zależy od K5-25: pasażerowie w testach siadają przez `FrameTransitions.TryReparent(…, seat, …)` (K5-20), loty prowadzi `DevTransitPilot` (K5-24) |
| Gałąź | `pakiet/k5-26-replikacja-statkow` (worktree `F:/dev/horyzont-k5-26`) |
| Kamień milowy | K5 Kosmos; odbiór wg spec 02:251: „2 klientów ma identyczne statki i pozycje pasażerów po serii lotów” |
| Równolegle | K5-25 (po K5-24; brak krawędzi startu), K5-22, K5-27 — punkty styku w sekcji „Kody i punkty styku” |

**Cel:** domknąć replikację statków i pasażerów — zasięg statków w ramie układu 10 km, pasażerowie widoczni razem ze statkiem, kolejność „rodzic przed dzieckiem” w pojawianiu się i znikaniu, pełny stan statku (`ConstructState`) przed jego migawkami z pierwszeństwem transferów statków, własny statek odbiorcy w każdej migawce, komórka fotela w pojawieniu się gracza i `ShipStatus` (31) 2 Hz do siedzących — tak, żeby dwóch klientów po serii lotów (RTT 100 ms, 5 % strat) miało identyczne statki i pozycje pasażerów.

## Kontekst

- `docs/ZASADY.md`: pkt 2 (stan kanoniczny: rama rodzica + `ParentSeq` + poza lokalna; sieć zna tylko pozy lokalne), 3 (`Protocol/`, `Serialization/` → `ProtocolVersion`; Core = przegląd Opus), 6 (runda replikacji bez alokacji, testy alokacji), 8 (`FakeTimeProvider`, `InMemoryTransport`, X11).
- `docs/spec/02-kosmos.md`: :114 (przepina się tylko statek; pasażerowie w jego ramie), :139 (fotel = komórka bloku; poza siedzącego wynika z bloku), :162–171 (sieć: pasażerowie nie przewidują; migawki niosą statki jako `Construct` (2) z pozą w ramie rodzica; `ParentSeq`: starszy odrzucony, nowszy wstrzymany do 1 s → `ResyncRequest`; zainteresowanie w ramie regionu: planeta 500 m, układ — statki z 10 km, pasażerowie razem ze statkiem; wejście statku w zasięg: najpierw `ConstructState`, potem migawki; poza zasięgiem — zero pasma; `ShipStatus` 31, zawodny, 2 Hz, tylko do siedzących: paliwo, limit, stan napędu, powód blokady startu), :206 (resync statku = `ConstructState` od części 0), :234 (pasażerowie widzą płynny lot przy RTT 100 ms i 5 % strat), :251 (odbiór).
- `docs/plans/README.md`: X3 (migawki 20 480 B/s, ramka ≤ 1 150 B; wspólny niezawodny 27 000 B/s; sterowanie poza budżetem), X4/D4, X5 (router), X6 (fixture), X10, X11.
- `docs/plans/K5-20-ramy-odniesienia.md`: decyzje 10 (budowla `Ship` kandydatem migawki wg trybu czytanego co rundę; `Ship → Static` bez despawnu), 11 (pozycja podmiotu i widza w ramie regionu przez `FrameGraph.TryGetRegion` + `TryExpress`), 12–14 (`ClientFrameGraph`, `Rebase`, gracz w ramie budowli przyczepiony), P4 („zasięg 10 km statków w układzie — K5-26”), test `Reparent_ThousandRandom_*` (miary skoku i złej ramy).
- `docs/plans/K5-24-model-lotu.md`: decyzje 10 (`ShipFlightSystem`), 12 (`ShipFlightStatus`, `ShipLaunchBlock`, `TryGetStatus`), 19 (`DevTransitPilot`, `DevControlSource`), 20 (`IClientShipStatus` — implementuje K5-26), `ShipFixtures.Thirty`, `SphereGroundCollider`.
- `docs/plans/K3-15-replikacja-bytow.md` (przegląd X3, priorytet migawek z przeglądu K3: wiek/odległość + wymuszenie po 10 pominięciach).

Kod, na którym pakiet buduje (zweryfikowany w `F:/dev/horyzont-k3-15` — K3-15 scalony na `main`; nazwy K5-20/K5-24 z ich planów; krok 0 potwierdza na `main`):

| Co | Gdzie |
| --- | --- |
| `EntityReplication`: jeden `_viewRange` (500 m) dla wszystkich podmiotów; runda co 3 ticki (20 Hz): `CopyEntities` → `UpdateLifecycle` (podmioty `InterestSubject(Id, Frame, Position, BoundsRadius, _viewRange)`, widz = własny byt) → `Enter`/`Leave` w kolejności z `GetChanges` → `SendSnapshots` | `src/Horyzont.Server/Replication/EntityReplication.cs:33,64,114-135,250-296` |
| `Enter`: budowla → `EntitySpawn` + `ConstructStateSender.Enqueue`, bez migawek (K5-20 dodaje `Ship`); inne → `EntitySpawn` + `Mobile` | `…/EntityReplication.cs:320-344` |
| `Leave` (`EntityDespawn`, anulowanie transferu), `ResetKnown`, `HandleResync`, `OnApplied`/`ForwardReparent` (statek przepięty → transfer albo `OperationApplied` na żywo) | `…/EntityReplication.cs:298-373,408-523` |
| `MakeSpawn`: `EntitySpawn(Id, Kind, 0, Owner, Frame, State, [])` — gracze z pustym `KindData` | `…/EntityReplication.cs:375-376`, `src/Horyzont.Core/Protocol/Messages/EntitySpawn.cs:5-18` (`KindData` ≤ 64 B) |
| `ReplicationEntity(Id, Kind, Owner, Frame, State, BoundsRadius)` | `src/Horyzont.Server/Replication/ReplicationEntity.cs` |
| `SnapshotBuilder.Build`: najpierw własny byt, potem najstarszy wysłany; ≤ 22 byty, ≤ 8 ram w tabeli, ≤ 1 150 B; budżet `ISnapshotBudget` | `src/Horyzont.Server/Replication/SnapshotBuilder.cs:93-240` |
| `SnapshotWriter`: pozycja 3 × `f32`, obrót 32 bity, prędkość 3 × `Half` | `src/Horyzont.Core/Serialization/SnapshotWriter.cs:77-86` |
| `ConstructStateSender`: `Enqueue` (kolejka FIFO na odbiorcę), `IsLive`, `IsTransferring`, części ≤ 32 KiB, ruch `Bulk` | `src/Horyzont.Server/Replication/ConstructStateSender.cs:34-160,228-251,394` |
| `InterestManager`: zasięg per podmiot, komórki 64 m (planeta) / 2 048 m (układ), jedna histereza (`ServerOptions.InterestHysteresisMeters = 50`), wyjście przy innym regionie | `src/Horyzont.Net/Session/InterestManager.cs:7-47,238`, `ServerOptions.cs:40` |
| `EntityMirror`: parkowanie nowszego `ParentSeq` (≤ 32), `TryGetPlanetPose` idzie łańcuchem budowli (brak budowli → `false`) | `src/Horyzont.Client/Replication/EntityMirror.cs:11-29,153-191` |
| `ConstructMirror`, `ConstructReplica`, `ResyncTracker` | `src/Horyzont.Client/Replication/`, `src/Horyzont.Core/Replication/` |
| `MessageType`, `MessageChannels` (lista zawodnych), `MessageCodec`, `MessageRouter` | `src/Horyzont.Core/Protocol/`, `Serialization/MessageCodec.cs`, `src/Horyzont.Net/Session/MessageRouter.cs` |
| K4-19: `CoopRig` (`ServerRuntime` + `InMemoryNetwork(LinkConditions)` + N klientów), `LatencyTransport` | `tests/Horyzont.Integration.Tests/Coop/`, `src/Horyzont.Bots/Transport/LatencyTransport.cs` |
| K5-20/K5-21/K5-24: `FrameGraph`, `FrameTransitions`, `ServerFrameHistory`, `ClientFrameGraph`, `StarSystemModel`, `ShipFlightSystem`, `DevTransitPilot`, `IClientShipStatus` | `src/Horyzont.Core/Frames/`, `src/Horyzont.Server/Frames/`, `src/Horyzont.Client/Frames/`, `src/Horyzont.Core/Space/`, `src/Horyzont.Server/Ships/`, `src/Horyzont.Client/Ships/` |

## Zakres

1. **Serwer — zainteresowanie:** `ReplicationRanges` (zasięg wg rodzaju i regionu: planeta 500 m, statek w układzie 10 km), próg wyjścia `ViewRange + max(histereza, 5 % ViewRange)`, pasażerowie dziedziczą podmiot statku.
2. **Serwer — cykl życia i migawki:** kolejność „rodzic przed dzieckiem” przy pojawianiu się i odwrotna przy znikaniu; migawki bytu dopiero, gdy każda budowla w jego łańcuchu ram jest u odbiorcy na żywo; pierwszeństwo transferów (własny statek → inne statki wg odległości → budowle statyczne); własny łańcuch ram odbiorcy w każdej migawce z rezerwacją miejsc w tabeli ram.
3. **Fotel w pojawieniu się:** `ReplicationEntity.Seat`, `KindData` gracza z komórką fotela, `EntityMirror.TryGetSeat`.
4. **`ShipStatus` (31):** rekord, kodek, kanał zawodny, router, fixture; `ShipStatusSender` (2 Hz, tylko do bytów w ramie statku); `ClientShipStatus : IClientShipStatus` (K5-24) — HUD lotu dostaje paliwo, napęd i blokadę startu.
5. **Testy:** test akceptacyjny dwóch klientów po serii lotów (`Slow`), testy jednostkowe zasięgu, cyklu życia, migawek, transferów, kodeka, alokacji; ręczny host + zdalny klient.

## Poza zakresem

- Fotele i intencje (wsiadanie, wysiadanie, wyskok, `SeatEvictIntent`), `ShipInputBatch` (30), predykcja pilota i `ShipCorrection` (34), blokada startu przez osoby w kadłubie (K5-25). K5-26 replikuje to, co jest w stanie (rama, `ParentSeq`, poza, fotel), niezależnie od tego, kto przepiął byt.
- Model lotu, `ShipState`, HUD lotu (K5-24; K5-26 tylko zasila `IClientShipStatus`). Mapa i obecność graczy spoza zasięgu (K5-27, `MapPresence` 35 — P1 K5-27).
- Zmiana formatu migawki (liczba ram w tabeli ≤ 8, precyzja pól) i kompensacja opóźnień (K7-41).

## Fazy i zależność od niescalonych pakietów

| Faza | Kroki | Wymaga scalonych | Co daje |
| --- | --- | --- | --- |
| A | 0–1 | K5-24 | warunki wejścia, czerwony test akceptacyjny i uprząż lotów |
| B | 2–6 | K5-24 | zasięg, pasażerowie, cykl życia, migawki, transfery, fotel w pojawieniu się |
| C | 7–8 | K5-24 | `ShipStatus` (31), D4, `ClientShipStatus` |
| D | 9–10 | K5-24 (K5-25 opcjonalnie dla testu ręcznego) | zielony test akceptacyjny z raportem, testy silnika i ręczny |
| E | 11 | — | odbiór, dokumentacja, PR |

## Mapa kryteriów → dowód

| # | Kryterium (źródło) | Dowód | Kategoria / job | Faza |
| --- | --- | --- | --- | --- |
| 1 | 2 klientów ma identyczne statki i pozycje pasażerów po serii lotów (02:251) | `ShipReplication_TwoClients_SeriesOfFlights_IdenticalShipsAndPassengers` (patrz „Pomiar”) | `Slow` / `ci.yml` | D |
| 2 | Pasażerowie widzą płynny lot przy RTT 100 ms i 5 % strat (02:234) | ten sam test: błąd pozy statku i pasażera u klienta względem serwera przy ticku renderu ≤ 0,5 m; skok między klatkami ≤ 0,5 m ponad `v·Δt` (miary K5-20) | `Slow` | D |
| 3 | Zainteresowanie w ramie regionu: planeta 500 m, statki w układzie z 10 km, pasażerowie razem ze statkiem (02:169) | `Interest_ShipInSystem_EntersAt10km_LeavesBeyond10_5km` / `Interest_ShipOnPlanet_500m` / `Interest_PassengersEnterAndLeaveWithShip` | domyślna | B |
| 4 | Wejście statku w zasięg: najpierw `ConstructState`, potem migawki; poza zasięgiem zero pasma (02:170) | `Snapshot_ShipNotLive_NotSent_ThenSentAfterConstructState` / `ShipOutOfRange_NoTraffic` | domyślna | B |
| 5 | Migawki niosą statki z pozą w ramie rodzica; `ParentSeq` (02:168, :205–206) | `ShipReplication_ZoneTransition_ObserverRebasesWithoutJump` + istniejące testy K3-15/K5-20 bez zmian | domyślna | B |
| 6 | `ShipStatus` 2 Hz, zawodny, tylko do siedzących (02:171) | `ShipStatusSender_2Hz_OnlyToPlayersInShipFrame` + `MessageCodec_ShipStatus_RoundTripAndRejects` + router X5 | domyślna | C |
| 7 | Fotel = komórka bloku; poza siedzącego wynika z bloku (02:139) | `EntitySpawn_SeatedPlayer_KindDataCarriesSeat` / `EntityMirror_Seat_FromSpawnAndReparent` | domyślna | B |
| 8 | Runda replikacji bez alokacji przy 8 statkach i 8 graczach (ZASADY pkt 6) | `ShipReplication_EightShipsEightPlayers_RoundAllocatesZeroBytes` | domyślna | B |

## Projekt

### Decyzje

1. **Zasięg wg rodzaju i regionu** (`ReplicationRanges`, `src/Horyzont.Server/Replication/`, wstrzykiwane do `EntityReplication` zamiast pojedynczego `viewRange`; konstruktor ze starym parametrem zostaje i tworzy `ReplicationRanges.Uniform(viewRange)`): region planety — wszystkie byty 500 m (spec :169; statki na planecie też, P1); region układu — budowle `Ship` 10 000 m; inne byty w ramie układu nie występują (K5-20 P6). Region i pozycja z K5-20 (`FrameGraph`). `InterestManager`: próg wyjścia = `ViewRange + max(histereza, 0,05 · ViewRange)` — przy 500 m bez zmian (50 m > 25 m), przy 10 km 500 m (zbliżanie 4 km/s nie powoduje migotania na granicy). Zmiana w `Horyzont.Net` (nie Core).
2. **Pasażerowie razem ze statkiem.** Byt, którego łańcuch ram przechodzi przez budowlę `Ship`, dostaje podmiot zainteresowania **tego statku** (najbliższego `Ship` w łańcuchu): region, pozycja w regionie, promień sfery i zasięg statku. Te same dane wejściowe → ta sama decyzja `InterestManager`, więc pasażer wchodzi i wychodzi razem ze statkiem (test). Obliczenie przy budowaniu podmiotów bez alokacji (indeks bytów z rundy, `FrameGraph` głębokość ≤ 8).
3. **Rodzic przed dzieckiem.** W jednej aktualizacji: wejścia sortowane wg głębokości ramy (budowle w ramie planety/układu przed bytami w ich ramach, potem po id), wyjścia odwrotnie (najpierw byty w ramie statku, potem statek). Dotyczy też `ResetKnown`. Klient nigdy nie dostaje pasażera przed jego statkiem w tej samej rundzie; `EntityMirror` i tak toleruje brak ramy (`TryGetPlanetPose` → `false`), ale pasażer bez statku byłby niewidoczny przez czas transferu.
4. **Migawki dopiero na żywo.** Kandydat migawki (statek albo byt w jego ramie) trafia do zbioru odbiorcy dopiero, gdy każda budowla w jego łańcuchu ram jest dla tego odbiorcy `ConstructStateSender.IsLive`; do tego czasu odbiorca ma tylko `EntitySpawn` (sterowanie, poza budżetem) i części `ConstructState`. Spec :170: „najpierw pełny stan, potem migawki”. Bramka jest bezpieczna tylko dlatego, że przepięcia i zmiany trybu nie restartują transferu u odbiorcy z kopią na żywo (README Y8: K5-20 — `ForwardReparent` budowli na żywo, B1; K5-23 — `ShipModeSetOp` podbija wersję, B4); krok 0 potwierdza oba na `main`, a test `ShipReplication_ZoneTransition_ObserverRebasesWithoutJump` dodatkowo sprawdza, że przy przejściu strefy żaden odbiorca nie dostaje części `ConstructState` statku.
5. **Pierwszeństwo transferów.** `ConstructStateSender.Enqueue(PlayerId, EntityId, ConstructTransferPriority priority)` (`Own = 0` — statek, w którego ramie jest odbiorca; `Ship = 1` — inne statki, w obrębie wg odległości w regionie przy wstawieniu; `Static = 2` — FIFO jak dziś); stara sygnatura = `Static`. Kolejność dotyczy rozpoczynania transferów; rozpoczęty transfer nie jest wywłaszczany (miękki cel 32 KiB/część zostaje). Budżet `Bulk` bez zmian (X3).
6. **Własny łańcuch w każdej migawce.** `SnapshotBuilder.Build`: kolejność wyboru = własny byt → budowle łańcucha ram własnego bytu (statek, w którym siedzi odbiorca — kamera pasażera składa interpolowaną pozę statku z fotelem, spec :167) → reszta wg dotychczasowej reguły wieku. Tabela ram (≤ 8): ramy łańcucha odbiorcy rezerwowane jako pierwsze; byty w ramach, które się nie zmieściły, czekają do następnej migawki (wiek rośnie, więc nie głodują). Format migawki bez zmian.
7. **Fotel w pojawieniu się gracza.** `ReplicationEntity` + `CellPos? Seat` (z `PlayerState`); `MakeSpawn` dla gracza: `KindData` puste = nie siedzi, `[1][x i32][y i32][z i32]` (LE, 13 B) = siedzi w komórce ramy budowli (`Frame`). Dekodowanie po stronie klienta (`EntityMirror`) odrzuca inne długości i wersję ≠ 1 (`ProtocolViolationException`). Zmienia się dokumentacja `EntitySpawn` („gracze: `KindData` = fotel”) → `ProtocolVersion` (D4). Przepięcia na żywo niosą fotel w `EntityReparentOp.SeatCell` (bez zmian); `EntityMirror.TryGetSeat(EntityId, out CellPos)` łączy oba źródła. Dzięki temu obserwator, który wszedł w zasięg po wsiadaniu, zna fotel.
8. **`ShipStatus` (31)** (`src/Horyzont.Core/Protocol/Messages/ShipStatus.cs`): `sealed record ShipStatus(EntityId Ship, Tick Tick, float Fuel, float FuelCapacity, float SpeedLimit, ShipFlightFlags Flags, ShipLaunchBlock LaunchBlock) : Message` — typy K5-24. Kodek: `[Ship u64][Tick u64][Fuel f32][Capacity f32][Limit f32][Flags u8][LaunchBlock u8]` (34 B); odrzuca NaN/∞, wartości ujemne, nieznane bity flag i wartość `LaunchBlock` spoza enuma. Kanał zawodny (dopisany do listy zawodnych w `MessageChannels` i w teście `MessageChannels_MapsEveryType`); X5: `IsKnownType` tak, serwer → klient w `InGame` (przez `IsGameMessageFromServer`), od klienta — nie. Poza budżetem replikacji (sterowanie; ≤ 8 × 2 Hz × ~40 B na serwer).
9. **`ShipStatusSender`** (`src/Horyzont.Server/Ships/`, uczestnik ticka fazy `Compose`): co 30 ticków (2 Hz) dla każdego gracza w grze, którego własny byt ma ramę `Construct(ship)` z `Mode == Ship` (siedzący i przyczepieni — spec „siedzący w statku”), `ShipFlightSystem.TryGetStatus(ship)` → `ShipStatus` przez `ServerSession.Send` na kanale zawodnym. Bez alokacji po rozgrzaniu (rekord z puli albo kodowanie bezpośrednie — wg wzorca `SendEncoded` K3-15).
10. **`ClientShipStatus`** (`src/Horyzont.Client/Ships/`, implementuje `IClientShipStatus` z K5-24): trzyma najnowszy stan wg `Tick` na statek; starszy niż 2 s → `TryGet` zwraca `false` (HUD „—”); czyszczony przy `EntityDespawn` statku. Podmiana `UnknownShipStatus` w `GameComposition.Flight.cs` (K5-24) — jedna linia, X10.
11. **Precyzja na drucie** (bez zmian formatu, potwierdzone testem): pozycja `f32` w ramie układu do 400 km (granica K5-24) ma krok ≤ 3,1 cm; prędkość `Half` przy 2 000 m/s ma krok 1 m/s → błąd ekstrapolacji ≤ 10 cm przy 100 ms; ramy w tabeli ≤ 8 (decyzja 6).
12. **Definicja „identyczne”** (przegląd K5, B5). Statek zaparkowany ma dalej `Mode == Ship`, jego poza u klienta pochodzi z migawek, a migawka pakuje obrót do 32 bitów (`SnapshotWriter.cs:79`, `Quantization.PackRotation`), więc porównanie bitowe pozy z migawki jest niemożliwe. Po każdym etapie serii (wszystkie statki na postoju, brak transferów w toku, ≥ 1 s ciszy), dla każdego bytu znanego klientowi, u serwera i u każdego klienta, który go zna:
    - **bitowo** — pola z operacji i `ConstructState`: `Mode`, `Parent`, `ParentSeq`, wersja bloków, zawartość chunków; u pasażera rama, `ParentSeq` i fotel;
    - **pozycja z ostatniej migawki** (statku i pasażera, `f32` w ramie rodzica) — bitowo równa pozycji serwera z ticku tej migawki;
    - **obrót z ostatniej migawki** — kąt między obrotem klienta i serwera ≤ 2 × kwant `Quantization.PackRotation` (stała z `Quantization`, nie liczba wpisana w test);
    - **etap końcowy serii** przełącza oba statki na `Static` (`ShipModeIntent` → `ShipModeSetOp`, który niesie pozę i dociera na żywo dzięki B4): wtedy nagłówek lustra `Origin`/`Rotation` — bitowo równy serwerowi.
    Oba klienty znające ten sam byt mają te same wartości w tych samych granicach. W locie — miary K5-20 (błąd ≤ 0,5 m, skok ≤ 0,5 m ponad `v·Δt`, zero stanów w złej ramie).

### Pliki

```
src/Horyzont.Server/Replication/      ReplicationRanges, EntityReplication (podmioty, kolejność, bramka migawek),
                                      ReplicationEntity (+ Seat), SnapshotBuilder (łańcuch własny, rezerwacja ram),
                                      ConstructStateSender (+ priorytet), ConstructTransferPriority, IEntityReplicationSource (fotel)
src/Horyzont.Net/Session/InterestManager.cs          próg wyjścia
src/Horyzont.Core/Protocol/           MessageType (+ ShipStatus = 31), MessageChannels, Messages/ShipStatus.cs,
                                      Messages/EntitySpawn.cs (XML-doc KindData gracza), ProtocolConstants (D4)
src/Horyzont.Core/Serialization/MessageCodec.cs       typ 31
src/Horyzont.Net/Session/MessageRouter.cs             typ 31 (X5)
src/Horyzont.Server/Ships/ShipStatusSender.cs
src/Horyzont.Client/Replication/EntityMirror.cs       + TryGetSeat, dekodowanie KindData gracza
src/Horyzont.Client/Ships/ClientShipStatus.cs
game/src/Server/Replication/ServerReplicationSource.cs  fotel w ReplicationEntity
game/src/Boot/GameComposition.ShipReplication.cs (nowy partial), GameComposition.Flight.cs (1 linia: ClientShipStatus)
tests/Horyzont.Testing/Generators/CodecGen.cs; tests/Horyzont.Core.Tests/Protocol/ProtocolContractTests.cs; Serialization/MaxMessages.cs
tests/Horyzont.Server.Tests/Replication/  ShipInterestTests, ShipLifecycleTests, ShipSnapshotTests, ConstructTransferPriorityTests
tests/Horyzont.Server.Tests/Ships/ShipStatusSenderTests.cs
tests/Horyzont.Net.Tests/              InterestLeaveThresholdTests, router typu 31
tests/Horyzont.Client.Tests/           EntityMirrorSeatTests, ClientShipStatusTests
tests/Horyzont.Integration.Tests/Ships/ShipCoopRig.cs, ShipReplicationTests.cs
```

## Testy akceptacyjne (napisz najpierw)

| Test | Plik | Kategoria | Faza | Co sprawdza |
| --- | --- | --- | --- | --- |
| `ShipReplication_TwoClients_SeriesOfFlights_IdenticalShipsAndPassengers` | `tests/Horyzont.Integration.Tests/Ships/ShipReplicationTests.cs` | `Slow` | A (czerwony) → D | patrz „Pomiar” |
| `ShipReplication_ObserverLeavesAndReturns_StateRebuilt` | jw. | `Slow` | D | statek odlatuje poza 500 m od obserwatora na planecie, wraca: `EntityDespawn`, nowy `ConstructState`, nagłówek i pasażerowie zgodni |
| `ShipReplication_ZoneTransition_ObserverRebasesWithoutJump` | jw. | domyślna | B | statek przechodzi planeta → układ przy obserwatorze-pasażerze innego statku: brak skoku > 0,5 m, `WrongFrameApplied = 0` |
| `Interest_ShipInSystem_EntersAt10km_LeavesBeyond10_5km` / `Interest_ShipOnPlanet_500m` / `Interest_PassengersEnterAndLeaveWithShip` / `InterestManager_LeaveThreshold_MaxOfHysteresisAnd5Percent` | `tests/Horyzont.Server.Tests/Replication/ShipInterestTests.cs`, `tests/Horyzont.Net.Tests/InterestLeaveThresholdTests.cs` | domyślna | B | decyzje 1–2 |
| `Lifecycle_ShipSpawnBeforePassengers_DespawnAfter` / `…_ResetKnown_ChildrenFirst` | `…/Replication/ShipLifecycleTests.cs` | domyślna | B | decyzja 3 (kolejność komunikatów u klienta w pamięci) |
| `Snapshot_ShipNotLive_NotSent_ThenSentAfterConstructState` / `ShipOutOfRange_NoTraffic` / `Snapshot_OwnShipEverySnapshot_With22Others` / `Snapshot_FrameTableFull_ChainFramesReserved` | `…/Replication/ShipSnapshotTests.cs` | domyślna | B | decyzje 4, 6 |
| `ConstructTransfers_OwnShipFirst_ThenShipsByDistance_ThenStatic` / `…_StartedTransferNotPreempted` | `…/Replication/ConstructTransferPriorityTests.cs` | domyślna | B | decyzja 5 |
| `EntitySpawn_SeatedPlayer_KindDataCarriesSeat` / `EntityMirror_Seat_FromSpawnAndReparent` / `EntityMirror_PlayerKindData_RejectsMalformed` | `tests/Horyzont.Server.Tests/Replication/`, `tests/Horyzont.Client.Tests/EntityMirrorSeatTests.cs` | domyślna | B | decyzja 7 |
| `ShipReplication_EightShipsEightPlayers_RoundAllocatesZeroBytes` | `…/Replication/ShipSnapshotTests.cs` | domyślna | B | 8 statków w układzie, 8 graczy (po jednym na statek), 1 000 rund po rozgrzaniu = 0 B |
| `MessageCodec_ShipStatus_RoundTripAndRejects` + X6 (`ProtocolContractTests`, `MaxMessages`, `CodecGen`, `MessageChannels_MapsEveryType`) + `ProtocolVersion_MatchesDocumentedMergeValue`, `Constants_GoldenValues` | `tests/Horyzont.Core.Tests/Protocol/`, `Serialization/` | domyślna | C | decyzja 8 |
| `MessageRouter_ShipStatus_ServerOnlyInGame` | `tests/Horyzont.Net.Tests/` | domyślna | C | X5 obu kierunków |
| `ShipStatusSender_2Hz_OnlyToPlayersInShipFrame` / `…_AllocatesZeroBytes` | `tests/Horyzont.Server.Tests/Ships/ShipStatusSenderTests.cs` | domyślna | C | decyzja 9 |
| `ClientShipStatus_NewestByTick_StaleAfter2s_ClearedOnDespawn` | `tests/Horyzont.Client.Tests/ClientShipStatusTests.cs` | domyślna | C | decyzja 10 |
| `Manual_TwoClients_ShipFlight_PassengerView` | procedura w PR | `Manual` | D | host + zdalny klient (`--dev`), pasażer przez `frame reparent … seat` (albo intencję K5-25, jeśli scalony), `ship pilot` do księżyca i z powrotem; widok pasażera płynny, HUD z paliwem; zrzuty/nagranie |

### Pomiar w teście akceptacyjnym (kryteria 1–2)

- Uprząż `ShipCoopRig` (na wzór `CoopRig` K4-19): `ServerRuntime` bez silnika (`FakeServerTerrain`), `ShipFlightSystem` z `SphereGroundCollider` i `DevControlSource`, `ServerZoneTransitions`, układ `WorldFactory.CreateWithStarSystem(0)`; trzech klientów przez `InMemoryNetwork(LinkConditions)` z RTT 100 ms ± 10 ms i 5 % strat kanału zawodnego (X11: niezawodny bez strat), `FakeTimeProvider`, klatki 16 ms jak `ServerRig`.
- Świat: dwa statki `ShipFixtures.Thirty` (A, B) na planecie domowej z paliwem; gracze: G1 pasażer A (fotel), G2 pilot-pasażer B (kokpit — przez `FrameTransitions` z fotelem w komórce kokpitu), G3 obserwator stojący na planecie domowej 200 m od statków; klienci K1 (G1) i K2 (G3) porównywani, K3 (G2) dodatkowo.
- Seria (ziarno w nazwie przypadku, 2 przebiegi): A i B lecą `ship pilot` na księżyc (przejścia planeta → układ → księżyc), stoją 5 s, B leci na planetę B, A wraca na domową; w połowie drogi B do planety B oba statki są w ramie układu ≤ 10 km od siebie (widzą się nawzajem), potem > 10,5 km (znikają); G1 przesiada się w A z fotela do fotela (`FrameTransitions`) w trakcie postoju.
- Na koniec serii: oba statki wracają na domową i przechodzą w `Static` intencją `ShipModeIntent` (gracz stojący obok — G3 albo pasażer po przepięciu na planetę), potem porównanie nagłówków bitowo (decyzja 12, ostatni punkt).
- Sprawdzenie po każdym etapie: decyzja 12 (bitowo dla pól z operacji, pozycja migawki bitowo, obrót w granicy kwantu) dla wspólnych bytów K1/K2/K3 i serwera; statki poza zasięgiem klienta — nieobecne u niego (i bez pasma: liczniki bajtów 0 dla tych bytów).
- W locie (co klatkę renderu klientów pasażerów): błąd pozy statku i pasażera w świecie względem historii serwera przy tym samym ticku renderu ≤ 0,5 m; skok ≤ 0,5 m ponad `v_max·Δt`; `WrongFrameApplied = 0`. Raport w PR: maks., p99, liczba `ResyncRequest`, `ReparentSnaps`, bajty migawek i `Bulk` na klienta, liczba migawek bez własnego statku (oczekiwane 0).

## Kroki

Każdy krok: test czerwony → kod → zielone testy → mały commit (Conventional Commits). Wyszukiwanie kodu i uruchamianie testów zlecać subagentowi Haiku (CLAUDE.md projektu).

**Faza A — wejście i test akceptacyjny**

- [ ] 0. Warunki wejścia: `git status`; worktree `F:/dev/horyzont-k5-26` od `origin/main` po scaleniu K5-24; w „Odstępstwach” zapisać scalone pakiety (K5-20…K5-25, K5-27) i rzeczywiste nazwy: zmiany K5-20 w `EntityReplication`/`SnapshotBuilder` (region, `Ship` w `Mobile`), `ShipFlightSystem.TryGetStatus`, `ShipFlightStatus`, `ShipFlightFlags`, `ShipLaunchBlock`, `IClientShipStatus`, `DevControlSource`, pole fotela w `PlayerState`; sprawdzić, że `MessageType` 31 jest wolny na `main` i w otwartych PR K5 (K5-25 ma 30 i 34). Jeśli K5-25 scalony — zapisać, czy już wypełnia `KindData` gracza albo dodaje fotel do `ReplicationEntity` (wtedy decyzja 7 tylko dopina brakujące). Commit `docs:`.
- [ ] 1. `ShipCoopRig` + szkielet `ShipReplication_TwoClients_SeriesOfFlights_*` (czerwony: brak zasięgu 10 km, pasażerów razem ze statkiem, statusu) i `ShipReplication_ObserverLeavesAndReturns_*`. Commit `test:`.

**Faza B — replikacja**

- [ ] 2. `ReplicationRanges` + próg wyjścia `InterestManager` + pasażerowie z podmiotem statku; testy `Interest_*`, `InterestManager_LeaveThreshold_*`; istniejące testy zainteresowania K3-15/K5-20 bez zmian. Commity `test:`, `feat:`.
- [ ] 3. Kolejność „rodzic przed dzieckiem” w `UpdateLifecycle` i `ResetKnown`; testy `Lifecycle_*`. Commity `test:`, `feat:`.
- [ ] 4. Bramka migawek „łańcuch na żywo”, własny łańcuch w każdej migawce, rezerwacja ram; testy `Snapshot_*`, `ShipOutOfRange_NoTraffic`, `ShipReplication_ZoneTransition_*`, test alokacji rundy. Commity `test:`, `feat:`.
- [ ] 5. `ConstructTransferPriority` w `ConstructStateSender.Enqueue` (+ wywołania z `Enter`, `HandleResync`, `ForwardReparent`); testy `ConstructTransfers_*`. Commity `test:`, `feat:`.
- [ ] 6. Fotel: `ReplicationEntity.Seat` (źródło w `ServerReplicationSource`), `KindData` gracza, `EntityMirror.TryGetSeat` z dekodowaniem i odrzuceniami; testy decyzji 7. Commity `test:`, `feat:`.

**Faza C — status**

- [ ] 7. `MessageType.ShipStatus = 31`, rekord, `MessageCodec`, `MessageChannels` (zawodny), `MessageRouter` (X5), fixture X6, D4 (`ProtocolVersion` = main + 1 — jedna podbitka razem z decyzją 7). Kontrakt i złote bajty do akceptacji Opus przed kodem nadawcy. Commity `test:`, `feat:`.
- [ ] 8. `ShipStatusSender` (rejestracja w `GameComposition.ShipReplication.cs`, także serwer dedykowany), `ClientShipStatus` + podmiana w `GameComposition.Flight.cs`; testy. Commity `test:`, `feat:`.

**Faza D — dowody**

- [ ] 9. Test akceptacyjny zielony w obu przebiegach; raport liczb (sekcja „Pomiar”) w PR. Niezaliczony próg = poprawka, nie osłabienie asercji (ZASADY pkt 12 po dwóch próbach). Commit `test:`.
- [ ] 10. Job `game` (istniejące testy silnika replikacji i ram zielone), `python tools/engine/build.py smoke`, `Manual_TwoClients_ShipFlight_PassengerView` (zrzuty, opis RTT z `LatencyTransport`/`--dev`). Commit `test:`.

**Faza E — odbiór**

- [ ] 11. Rebase na `main`; pełne komendy odbioru; `docs/ZASADY.md` pkt 2: „pasażer jest widoczny wtedy i tylko wtedy, gdy jego statek”, „migawki bytu w ramie budowli dopiero po pełnym stanie tej budowli u odbiorcy”, „`KindData` gracza = komórka fotela”; wiersz scalenia K5-26 w istniejącej sekcji „Pakiety K5” `docs/plans/README.md` z wartością `ProtocolVersion`; PR z raportem, odstępstwami i odpowiedziami. Commit `docs:`.

## Odbiór

```
dotnet build Horyzont.sln
dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"
dotnet format Horyzont.sln --verify-no-changes
python tools/engine/build.py smoke
```

Plus job `game` (testy silnika ram i replikacji bez regresji), w PR: raport `ShipReplication_TwoClients_*` (maks., p99, `ResyncRequest`, `ReparentSnaps`, bajty) i zrzuty testu ręcznego.

## Kody i punkty styku

Kod zajęty przez K5-26 (spec 02:171): **`MessageType.ShipStatus = 31`** (kanał zawodny). Żadnych `OperationKind`, `IntentCode`, `RejectReason`, `BlobKind` ani migracji. Zmiana semantyki bez nowego kodu: `EntitySpawn.KindData` gracza = fotel (decyzja 7) — ta sama podbitka `ProtocolVersion`.

| Rodzaj | Kod | Nazwa | Pakiet |
| --- | --- | --- | --- |
| `OperationKind` | 20 / 21 / 22 / 23 / 24–29 | `ShipModeSet` / `ShipFuelAdd` / `MarkerSet` / `MarkerRemove` / wolne | K5-23 / K5-24 / K5-27 / K5-27 / — |
| `IntentCode` | 10 / 11 / 12 / 13 / 14 / 15 / 16 / 17–19 | `ShipMode` / `SeatEnter` / `SeatExit` / `Refuel` / `MarkerSet` / `MarkerRemove` / `SeatEvict` / wolne | K5-23 / K5-25 / K5-25 / K5-24 / K5-27 / K5-27 / K5-25 / — |
| `RejectReason` | 30, 31, 37 / 32–35, 38 / 36 / 39 | `NoCockpit`, `ShipAnchored`, `NoEngine` / `SeatOccupied`, `SeatOutOfReach`, `EjectTooFast`, `NotPilot`, `ConstructNotEmpty` / `MarkerLimit` / `ShipNotParked` | K5-23 / K5-25 / K5-27 / K5-24 |
| `MessageType` | 30 / 31 / 32 / 33 / 34 / 35 / 36–39 | `ShipInputBatch` / `ShipStatus` / `StarSystemState` / `MarkersState` / `ShipCorrection` / `MapPresence` (warunkowo, K5-27 P1) / wolne | K5-25 / **K5-26** / K5-21 / K5-27 / K5-25 / K5-27 / — |
| `BlobKind` | 10 / 11 / 12 / 13–19 | `StarSystemParams` / `Marker` / `ShipState` / wolne | K5-21 / K5-27 / K5-24 / — |
| Migracja | M0002 | `M0002_Space` | K5-21 |

Kolejność scalania K5: **(K5-23 ↔ K5-20) → K5-21 → {K5-22, K5-24, K5-27} → {K5-25, K5-26} → K5-28**. K5-25 i K5-26 nie mają krawędzi startu między sobą; rekomendacja **K5-26 → K5-25** (K5-25 dokłada `ShipCorrection` do każdej migawki pilota i korzysta z gwarancji „własny statek w każdej migawce”); w odwrotnej kolejności K5-26 robi rebase na zmianach K5-25 w `SnapshotBuilder`/`EntityReplication`. Zasada X10.

| Plik / punkt | K5-26 | Inny pakiet | Rozwiązanie |
| --- | --- | --- | --- |
| `EntityReplication.cs`, `SnapshotBuilder.cs` | zasięgi, kolejność, bramka, łańcuch własny | K5-20 (region, `Ship` w migawkach — scalony), K5-25 (`ShipCorrection` przy migawce pilota), K5-27 (pominięcie op 22/23) | rozłączne metody; scalany później |
| `ReplicationEntity`, `ServerReplicationSource` | `Seat` | K5-25 (fotele) | jeśli K5-25 pierwszy dodał fotel — K5-26 używa jego pola |
| `EntitySpawn.KindData` gracza | fotel (13 B) | K5-25 | jedna definicja; scalany później dostosowuje się |
| `ConstructStateSender.cs` | priorytet | — | — |
| `InterestManager.cs` (Net) | próg wyjścia | — | — |
| `MessageType`/`Channels`/`Codec`/`Router`, fixture X6 | 31 (zawodny) | K5-25 (30, 34 — zawodne), K5-27 (33), K5-21 (32) | dopisywanie w kolejności numerów; lista zawodnych w `MessageChannels_MapsEveryType` rośnie o 31 |
| `IClientShipStatus` (K5-24) | implementacja `ClientShipStatus` | K5-24 `FlightHud` | podmiana jednej linii w `GameComposition.Flight.cs` |
| `ShipFlightSystem.TryGetStatus`, `ShipLaunchBlock` (K5-24) | konsument | K5-25 (`PersonInHull = 2`) | nowa wartość enuma przechodzi przez kodek bez zmian w K5-26 (odrzucenie tylko spoza enuma) |
| `ProtocolConstants.ProtocolVersion` + dwa testy | podbicie | wszystkie K5 z formatem | D4 |
| `GameComposition` | `GameComposition.ShipReplication.cs` | `.Flight` (K5-24), `.Seats` (K5-25), `.Space`, `.Frames`, `.Planets`, `.Navigation` | osobne partiale |
| `docs/ZASADY.md`, `docs/plans/README.md` | wiersze K5-26 | wszyscy | scalany później dopisuje obok |

## Ryzyka

| Ryzyko | Skutek | Odpowiedź |
| --- | --- | --- |
| Duży statek (do 256 KiB stanu) wchodzi w zasięg 10 km przy zbliżaniu 4 km/s, a `Bulk` 27 KB/s potrzebuje ~10 s | statek pojawia się z opóźnieniem albo dopiero blisko | pierwszeństwo statków (decyzja 5); statki EA małe; pomiar bajtów w raporcie; P2 |
| Tabela ram ≤ 8 przy 8 statkach z pasażerami w układzie (9+ ram) | część pasażerów co drugą migawkę | rezerwacja łańcucha odbiorcy; reszta rotuje wg wieku; zmiana formatu poza zakresem |
| Prędkość `Half` przy 2 km/s (krok 1 m/s) | drobny błąd ekstrapolacji | ≤ 10 cm przy 100 ms; test w pomiarze |
| Pasażer pojawia się przed pełnym stanem statku | niewidoczny pasażer albo skok | kolejność rodzic → dziecko i bramka „na żywo” |
| Zmiany K5-25 w tych samych plikach | konflikty rebase | X10, rozłączne metody, rekomendowana kolejność K5-26 → K5-25 |
| Migotanie na granicy 10 km | zalew spawn/despawn i transferów | próg wyjścia 10,5 km (decyzja 1) |

## Pytania do właściciela (wariant domyślny obowiązuje bez odpowiedzi)

| # | Pytanie | Wariant domyślny |
| --- | --- | --- |
| P1 | Zasięg statków w regionie planety? | 500 m jak wszystkie byty (spec :169); wariant: 2 km dla statków, żeby widać było start i lądowanie kolegi |
| P2 | Limit rozmiaru statku dla płynnego wejścia w zasięg? | brak nowego limitu (256 KiB z K3-15); raport bajtów w PR |
| P3 | Komu `ShipStatus`? | każdemu, czyj byt ma ramę statku `Ship` (siedzący i przyczepieni), 2 Hz |
| P4 | Fotel w `KindData` gracza? | tak (13 B), żeby obserwator wchodzący w zasięg znał fotel |
| P5 | Kryterium „identyczne”? | decyzja 12: bitowo pola z operacji i pozycja z migawki, obrót w granicy kwantu, nagłówek bitowo po przejściu w `Static`; w locie miary K5-20 (≤ 0,5 m) |

## Pułapki

- Pasażer nie ma własnego zasięgu: zawsze podmiot statku; inaczej znika na 500 m od statku w kosmosie.
- Nie wysyłać migawek bytu, którego łańcuch zawiera budowlę niebędącą u odbiorcy na żywo.
- Kolejność wejść i wyjść liczona bez alokacji (bufory z rundy), sortowanie stabilne po (głębokość, id).
- `ShipStatus` jest zawodny i poza budżetem; nie liczyć go do `ISnapshotBudget` ani `IReplicationSendBudget`.
- `KindData` gracza dekoduje tylko klient (lustro), z pełną walidacją długości i wersji — to dane z sieci.
- Lista zawodnych typów w `MessageChannels` i jej test rosną o 31 (inaczej `Send` wybierze kanał niezawodny).
- Testy .NET wyłącznie na `FakeTimeProvider` i `InMemoryNetwork`; straty tylko na kanale zawodnym (X11).

## Odstępstwa i decyzje wykonawcy

(wypełnia wykonawca: stan K5 na `main` (zwłaszcza K5-25), rzeczywiste nazwy API, wynik testu akceptacyjnego z liczbami, odpowiedzi właściciela, różnice od planu)
