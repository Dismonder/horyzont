# K5-28 · Testy Kosmosu

| | |
| --- | --- |
| Prowadzi | Sonnet 5.5 (Claude Code, `claude --model sonnet`; `--model opusplan` w kroku 0, gdy nazwy API na `main` różnią się od tabeli „Kontekst”) |
| Testy akceptacyjne | Sonnet 5.5; jedyna zmiana w Core (wydzielenie `TransitGuidance` z `DevTransitPilot` — kod czysty, nie format) przegląda Opus 5.5 przed scaleniem |
| Przegląd | Opus 5.5 w PR (ZASADY pkt 9: pakiet Sonnet zmienia `src/Horyzont.Core/`; poza tym przegląd na bramce K5) |
| Zależy od | K5-22, K5-25, K5-26 i K5-27 scalone na `main` (spec 02:253) **oraz K4-19 (PR #29) scalony** dla kroków 3–6 i 9 (`CoopRig`, `Horyzont.Bots`, `LatencyTransport`, `coop_soak.py`, `BenchmarkReport`, `budgets.json`, nocny `bench`); krok 9 także domknięte kroki 9–10 K5-22 (`descent8km`, `planets4` — wstrzymane do K4-19). Bez K4-19: tylko kroki 0–2 i faza C (S1). Zależy też od decyzji właściciela **K5-24 T1** (okna przelotów — S2) |
| Gałąź | `pakiet/k5-28-testy` (worktree `F:/dev/horyzont-k5-28`) |
| Kamień milowy | K5 Kosmos; odbiór wg spec 02:253: „wszystkie kryteria ukończenia spełnione” (spec :233–237) |
| Równolegle | brak — ostatni pakiet K5; po nim bramka K5 (decyzja właściciela) |

**Cel:** zamknąć K5 dowodami — scenariusze regresji dla każdego wiersza „Testy” (spec :217–229) i każdego kryterium ukończenia (spec :233–237), z podziałem na CI (`Slow`, job `game`) i nocny `bench`: boty-załogi budują statki, tankują i latają na trzy pozostałe ciała, Jolt na prawdziwej planecie C z postacią i statkiem, autolądowanie na terenie księżyca, B i C, odtwarzanie po awarii z lecącym statkiem i graczami w fotelach, płynny lot pasażerów przy RTT 100 ms i 5 % strat z prawdziwymi fotelami i pilotem, tick ≤ 8 ms przy 8 graczach na czterech planetach — plus katalog kryterium → test → job, sprawdzany w CI.

## Przegląd Opus 2026-10-11

**Werdykt: APPROVE WITH CHANGES** — poprawki S1–S9 wpisane niżej (ta sekcja jest nadrzędna przy sprzeczności). Kod odniesienia: `F:/dev/horyzont-k5-24` (`main` + K5-24) i `F:/dev/horyzont-k4-19` (K4-19, PR #29 — **nie** na `main`). Numeracja: K5-28 nie zajmuje kodów protokołu ani `ProtocolVersion` — zgodne z Y2/Y3; `BotRole.Crew = 3` i `BotAction` 9–14 są wolne w K4-19 (`BotRole` 0–2, `BotAction` 0–8) — **kolizji brak**.

| # | Waga | Problem (dowód) | Zmiana w planie |
| --- | --- | --- | --- |
| S1 | **BLOKER** | K4-19 (PR #29) nie jest na `main`, a K5-28 potrzebuje z niego prawie wszystkiego: `Horyzont.Bots`, `CoopRig` (z `IPersistenceFaults` — awaria), `LatencyTransport`, `coop_soak.py`, `BenchmarkReport`, `budgets.json`, `check_budgets.py`, nocny `bench`. K5-22 ma przez to niedokończone kroki 9 (wpis `descent8km`, budżet) i 10 (`planets4`) — „Odstępstwa” K5-22. README wymagał tylko K5-22/25/26/27. | Warunki startu per faza (tabela „Fazy”): bez K4-19 tylko kroki 1–2 i faza C; kroki 3–6 i 9 wymagają K4-19 na `main`; krok 9 także domknięcia K5-22 (kroki 9–10, PR uzupełniający właściciela K5-22). K5-28 **nie** przejmuje `planets4` (P7). README zaktualizowany. |
| S2 | **BLOKER** | Okna przelotów: K5-24 zmierzył 63 / 103 / 124 s przy τ = 8 s ze spec; `ShipTransit_WithPulse_Moon50_B70_C100s` ma `Skip` („K5-24 open question T1: … owner decides the tuning”; `tests/Horyzont.Server.Tests/Ships/ShipTransitTests.cs:38`). Twarde „każdy odcinek w [50; 100] s” w CI musi paść dla B i C. | Decyzja 5 (b): twardo „zaparkował” i ≤ 1,25 × górne okno K5-24 (jak strażnik `ShipTransit_WithPulse_ParksAndReportsTime`); [50; 100] s i okna ±20 % — raport; wpis K1 w katalogu `pending: "K5-24 T1"` = bloker bramki K5, nie scalenia K5-28. Po decyzji T1 — próg twardy (jedna liczba w katalogu i teście). Mediana do najbliższego sąsiada ≤ 90 s zostaje twarda. |
| S3 | WYSOKA | `DevTransitPilot` już jest czystą klasą z fazą, zależną tylko od Core (`Compute(in ShipMotion, ShipStats, in FlightEnvironment, in Vector3D targetCenter, double targetRadius, double groundDistance, FlightRules, ReadOnlySpan<BodySphere>)`); `BodySphere` leży w jego pliku w Server. Plan wymyślał nową sygnaturę bez `groundDistance` i przeszkód oraz test „zapis 3 przelotów”. | Decyzja 2: przeniesienie 1:1 do Core (`TransitGuidance`, `TransitPhase`, `BodySphere`), `DevTransitPilot` = cienka powłoka (publiczne API bez zmian dla testów K5-24/K5-26); dowód = niezmienione i zielone `ShipTransit_*` (`TransitSim`), bez osobnego testu zapisu. |
| S4 | WYSOKA | Bot nie ma `groundDistance`: serwer liczy go `ShipFlightSystem.ProbeGround` (zderzacz serwera), a bot bez silnika nie ma ani zderzacza, ani terenu. | Decyzja 3: `IBotGroundProbe` — CI (`CoopRig`): `SphereGroundCollider` jak `TransitSim`; noc (boty bez silnika): dolna granica `Altitude + clearance − TerrainMaxReliefMeters`, poniżej zera — `AutolandController.Compute` (Core, bez sondy) do postoju. Noc raportuje czas podejścia osobno. |
| S5 | ŚREDNIA | `ship spawn` „intencjami gracza `dev-op`” jest niewykonalny: `dev-op` dodaje gracza **offline** w punkcie startu (`ConsoleCommandProcessor.DevOp`, `PreparePlayerJoin`), a intencje bloków wymagają zasięgu 8 m i linii wzroku od oka. | Decyzja 4: operacje serwera **istniejących** rodzajów — `ConstructCreate` (2) + `BlockSet` (3) przez `ApplyServerOperation`, z `OperationApplier.CheckPreconditions` i sprawdzeniem gruntu (`ITerrainService.TrySampleSolid`) przed zastosowaniem; tylko `--dev`; bez nowych rodzajów. CI dalej buduje intencjami botów. |
| S6 | ŚREDNIA | `--check` uzna test z `[Theory(Skip = …)]` za dowód. | Decyzja 1: test z `Skip` = brak dowodu, chyba że wpis ma `"pending": "<pytanie właściciela>"` (wtedy raport „PENDING”, nie błąd). `--self-test` obejmuje przypadek. |
| S7 | ŚREDNIA | Przegląd K5-25 zmienił kontrakty, na których stoi bot-pilot: mapowanie slotów wejść (R1), `HorizontalSpeedCap` w `ShipCorrection` (R2), test 1000 foteli w `tests/Horyzont.Server.Tests/Ships/` (R10). | Decyzje 3 i 6: `BotShipPilot` wysyła wejście co tick bez przerw (sloty); raport `Substituted`/`Reanchors` z linii `stats` serwera; katalog — faktyczne ścieżki z kroku 0. |
| S8 | NISKA | Testy silnika w `bench` (`Soak`) i `LatencyTransport` w `game/tests` — mechanizm podziału 3/20 ma K5-22 (`Descent8km`); projekt gry może nie odwoływać się do `Horyzont.Bots`. | Krok 0 zapisuje mechanizm K5-22 i drogę opóźnienia w teście silnika (bez `Horyzont.Bots` — warunki łącza `InMemoryNetwork`). |
| S9 | NISKA | Kontekst: `ShipTransit_*` są w `tests/Horyzont.Server.Tests/Ships/ShipTransitTests.cs` (nie Core), fixture `FlightFixtures.Thirty()`/`TransitSim`; zderzacz statku `SphereShipCollider`, atrapa gotowości `FakeTerrainReadiness`. | Tabela „Kontekst” poprawiona. |

Pytania do właściciela: P1–P6 bez zmian; nowe P7 (kto domyka `planets4`/`descent8km` K5-22) i zależność od **K5-24 T1** (strojenie τ / v_min — S2).

## Kontekst

- `docs/ZASADY.md`: pkt 3 (Core = przegląd Opus), 6, 8 (kategorie `Slow`/`Soak`/`Manual`, `FakeTimeProvider`, `InMemoryTransport`), 9, 11 (definicja ukończenia).
- `docs/spec/02-kosmos.md`: :46–50 (czasy przelotu, τ), :131 (tereny serwera), :190–192 (statek w ruchu „zmieniony”, odstawienie po wczytaniu, gracz w fotelu po powrocie), :194 (skrót bez `ShipState`), :198–211 (awarie), **:213–229 (testy)**, **:231–237 (kryteria ukończenia)**, :253 (odbiór), :257–263 (ryzyka; plan awaryjny w danych :261).
- `docs/plans/README.md` „Pakiety K5”: Y3 (K5-28 nie podbija `ProtocolVersion`), Y5, Y11, Y12, Y14 (korpus złoty i skrót `WorldFactory.CreateDefault()` bez zmian), **Y15 (testy silnika szeregowo, X11, `Descent8km` 3/20)**; X11 (czas sieci wirtualny + czas silnika w raporcie).
- `docs/plans/K5-przeglad.md` „Pokrycie K5-25 i K5-28” — lista obowiązkowa (każdy punkt ma tu test).
- Plany K5 (testy, które K5-28 **reużywa**, a nie pisze od nowa): K5-20 (`FarFromOrigin_*` na terenie przesuniętym, `Reparent_ThousandRandom_*`), K5-22 (`Descent8km_TwentyTrials_*`, `coop_soak.py --profile planets4` z `place`, `Manual_ImpostorTransition_*`, `Manual_TriplanarPlanetC_*`, komenda `place`), K5-24 (`ShipTransit_WithPulse_Moon50_B70_C100s`, `Autoland_*` w Core i `Autoland_HomeTerrain_From100m_Parks`, `DevTransitPilot`, `ShipLoadSettler`, `SphereGroundCollider`), K5-25 (`Seats_ThousandRandomEnterExitEject_*`, `PilotPrediction_Rtt100_*`, `ShipLaunchGuard_*`), K5-26 (`ShipReplication_TwoClients_SeriesOfFlights_*`, definicja „identyczne” — decyzja 12), K5-27 (znaczniki po restarcie).

Kod, na którym pakiet buduje (zweryfikowany wyrywkowo w `F:/dev/horyzont-k4-19` i gałęziach K5 w toku; krok 0 potwierdza na `main`):

| Co | Gdzie |
| --- | --- |
| `Horyzont.Bots` (odwołuje się do Core, Net, Client): `BotClient`, `BotScript`/`BotSchedule`/`BotRole` (`Builder`, `Explorer`, `Abuser`), `BotAction` 0–8, `BotCrowd`, `BotReport`, `LatencyTransport` | `src/Horyzont.Bots/` (K4-19) |
| `coop_soak.py` (profile; K5-22 dodaje `planets4`), `BenchmarkReport`, `budgets.json`, `nightly.yml` (job `bench`) | `tools/coop/coop_soak.py`, `game/src/…/BenchmarkReport.cs`, `.github/workflows/nightly.yml` (K4-19, K5-22) |
| `CoopRig` (`ServerRuntime` + `InMemoryNetwork(LinkConditions)` + N klientów) | `tests/Horyzont.Integration.Tests/Coop/` (K4-19) |
| Komendy konsoli serwera (`stats`, `digest`, `frame`, `ship`, `ship pilot/fuel`, `place`, `seat`; gracz `dev-op` przez `PreparePlayerJoin`) | `src/Horyzont.Server/Console/ConsoleCommandProcessor.cs` (`:116` — `dev-op`) |
| `DevTransitPilot`, `DevControlSource`, `ShipFlightSystem`, `ShipLoadSettler`, `PilotControlSource`, `ShipLaunchGuard`, `ShipCorrectionSender`, `ShipStatusSender` | `src/Horyzont.Server/Ships/` (K5-24, K5-25, K5-26) |
| Sprawdzone 2026-10-11 (S3, S9): `DevTransitPilot(bool usePulse = true)` — klasa z `Phase` (`DevTransitPhase`), `Compute(in ShipMotion, ShipStats, in FlightEnvironment, in Vector3D targetCenter, double targetRadius, double groundDistance, FlightRules, ReadOnlySpan<BodySphere> obstacles)`, `BodySphere` w tym samym pliku; `DevControlSource` podaje `groundDistance = ShipFlightSystem.ProbeGround(ship, 5000)`; `AutolandController.Compute(motion, env, stats, rules)` (Core, bez sondy); testy przelotów `tests/Horyzont.Server.Tests/Ships/ShipTransitTests.cs` (`ShipTransit_WithPulse_ParksAndReportsTime` — strażnik 1,25 × okno; `ShipTransit_WithPulse_Moon50_B70_C100s` — `Skip`, pytanie K5-24 T1; zmierzone 63 / 103 / 124 s), `TransitSim`, `FlightFixtures.Thirty()`, `SphereShipCollider`, `FakeTerrainReadiness`, `ScriptedControlSource` (`tests/Horyzont.Testing/Ships/`) | `src/Horyzont.Server/Ships/DevTransitPilot.cs`, `src/Horyzont.Core/Ships/Flight/AutolandController.cs`, `tests/` |
| `OperationKind` 2 `ConstructCreate`, 3 `BlockSet` (S5); `dev-op` = gracz offline dodany `PreparePlayerJoin` | `src/Horyzont.Core/Operations/`, `src/Horyzont.Server/Console/ConsoleCommandProcessor.cs:110-123` |
| `ShipInputSequencer`, `ShipPrediction`, `ClientShipStatus`, `ClientStarSystem`, `ClientFrameGraph` | `src/Horyzont.Client/Ships/`, `Space/`, `Frames/` (K5-25, K5-26, K5-21, K5-20) |
| `ShipFixtures.Thirty`, `SeatFixtures`, `SphereGroundCollider`, `ScriptedControlSource`, `TestShips` | `tests/Horyzont.Testing/Ships/` (K5-20, K5-23, K5-24, K5-25) |
| `StarSystemGenesis` (ziarno 0: domowa, księżyc, B, C), `StarSystemModel` | `src/Horyzont.Core/Space/` (K5-21) |
| Testy silnika: `game/tests/Frames/FarFromOriginTests.cs`, `game/tests/Space/DescentTests.cs`, `game/tests/Ships/` | K5-20, K5-22, K5-24, K5-25 |
| `WorldLoader`, `PersistenceWriter`, `StateHasher`, `ServerSimulation` (powtórka ogona dziennika) | `src/Horyzont.Persistence/`, `src/Horyzont.Core/` |

## Zakres

1. **Katalog odbioru K5:** `tools/space/k5_acceptance.json` (kryterium/test spec → nazwy testów → kategoria/job → próg) i `tools/space/k5_acceptance.py` (`--check`: każda nazwa istnieje w repo i każdy job istnieje w workflow; `--report`: tabela wyników z artefaktów; `--self-test`) — w CI.
2. **Boty-załogi:** `BotRole.Crew` (pilot + pasażer/nawigator), akcje statku i foteli w `BotAction`, `BotShipPilot` (prawdziwe `ShipInputBatch` przez stos K5-25) nad wydzielonym `TransitGuidance` (Core).
3. **Dev:** `ship spawn <blueprint> <planet:id> <x> <y> <z>` (budowa z pliku wzorca przez intencje gracza `dev-op`), wzorzec `game/content/dev/ships/thirty.json` zgodny z `ShipFixtures.Thirty` (test).
4. **Scenariusze CI (`Slow`, bez silnika):** pełna pętla 8 botów (budowa, tankowanie, tryb statku, fotele, loty na trzy ciała), czasy przelotu i mediana do najbliższego sąsiada, płynny lot pasażerów z prawdziwymi fotelami i pilotem, odtwarzanie po awarii z lecącym statkiem i graczami w fotelach.
5. **Scenariusze silnika (job `game` i nocny `bench`):** Jolt na prawdziwej planecie C z postacią i statkiem, autolądowanie na terenie księżyca, B i C z trzech wysokości, lądowanie pilota z predykcją na C.
6. **Nocny `bench`:** profil `planets4-flight` w `coop_soak.py` (8 botów-załóg na serwerze dedykowanym, loty między czterema ciałami; tick, RAM, czas kolizji, mediana przelotu) i wariant z awarią procesu (`--crash-at`).
7. **Przebieg odbioru na `main`:** wszystkie testy K5 (CI, `game`, `bench`, `Manual` z K5-22/K5-24/K5-25/K5-26/K5-27) na jednym commicie, raport w tym planie i w PR.

## Poza zakresem

- Nowe zachowania gry, kody, komunikaty, migracje i podbicia `ProtocolVersion` (K5-28 nie zmienia formatu; potrzeba zmiany formatu = stop i pytanie do Opus i właściciela).
- Naprawy błędów w pakietach K5 większe niż poprawka z testem (> ~50 linii albo zmiana kontraktu) — zgłoszenie do pakietu-właściciela; K5-28 zapisuje je w „Odstępstwach” jako blokery bramki.
- Strojenie danych ponad strojenie `FlightRules`/`StarSystemLayout` opisane w planach K5-24 i K5-22 (plan awaryjny spec :261 = decyzja właściciela, P2).

## Fazy i zależność od niescalonych pakietów

| Faza | Kroki | Wymaga scalonych | Co daje |
| --- | --- | --- | --- |
| A | 0–2 | K5-22, K5-25, K5-26, K5-27 | katalog, `TransitGuidance` |
| A' | 3 | jw. + **K4-19** | boty-załogi, `ship spawn` |
| B | 4–6 | jw. + **K4-19** (`CoopRig`, `IPersistenceFaults`) | scenariusze CI: pętla 8 botów, pasażerowie, odtwarzanie po awarii |
| C | 7–8 | K5-22, K5-25, K5-26, K5-27 (krok 8 `bench` — K4-19) | scenariusze silnika (planeta C, autolądowanie na trzech ciałach) |
| D | 9–10 | jw. + **K4-19** + domknięte kroki 9–10 K5-22 (`planets4`, `descent8km`) | nocny `planets4-flight` + awaria procesu, przebieg odbioru na `main`, PR |

PR K5-28 otwiera się dopiero po fazie D (kryteria K1–K5 wymagają nocy); fazy bez K4-19 mogą iść na gałęzi wcześniej (S1).

## Mapa kryteriów → dowód

Kryteria ukończenia spec :233–237:

| # | Kryterium (źródło) | Dowód | Kategoria / job | Faza |
| --- | --- | --- | --- | --- |
| K1 | 8 graczy buduje statek z bloków i leci na każde z trzech pozostałych ciał bez ekranu ładowania; przelot z napędem 50–100 s (02:233) | `SpaceLoop_EightBots_BuildRefuelFlyToEachBody_NoLoadingScreen` (CI) + `planets4-flight` (noc, serwer dedykowany) | `Slow` / `ci.yml`; `bench` | B, D |
| K2 | Pasażerowie siedzą w fotelach i widzą płynny lot przy RTT 100 ms i 5 % strat (02:234) | `SpaceCoop_PilotAndSeatedPassengers_Rtt100Loss5_SmoothAndIdentical` (fotele przez intencje K5-25, pilot przez `ShipInputBatch`) + `ShipReplication_TwoClients_*` (K5-26) | `Slow` | B |
| K3 | Statki, paliwo, znaczniki i miejsca graczy przetrwają restart i awarię; skrót po powtórce = skrót przed awarią (02:235) | `SpaceRecovery_CrashMidFlight_ReplaySameHash_ShipsSettledSeatsAndMarkersKept` / `SpaceRecovery_CleanRestart_*` + `planets4-flight --crash-at` | `Slow`; `bench` | B, D |
| K4 | Serwer dedykowany z 8 graczami na czterech planetach: tick średnio ≤ 8 ms (02:236) | `planets4-flight` (twardy próg 8 ms, raport RAM, p99, czas kolizji na ciało) + `planets4` (K5-22) | `bench` | D |
| K5 | Wszystkie testy z sekcji „Testy” przechodzą w CI albo w opisanych przebiegach (02:237) | `k5_acceptance.py --check` (CI) + przebieg odbioru na `main` (krok 10) | domyślna (CI) + raport | A, D |

Tabela „Testy” spec :217–229 (właściciel testu → co dokłada K5-28):

| Test spec | Istniejący dowód (pakiet) | K5-28 dokłada | Job |
| --- | --- | --- | --- |
| Jolt daleko od początku (:219) | `FarFromOrigin_*` na terenie przesuniętym (K5-20) | `PlanetC_RealPlacement_CharacterAndParkedShip_JitterBelow1mm` | `game` |
| Zejście z 8 km (:220) | `Descent8km_TwentyTrials_*` (K5-22) | w katalogu; przebieg odbioru | `game` 3 / `bench` 20 |
| 8 botów na 4 planetach (:221) | `planets4` z `place` (K5-22) | `planets4-flight` (boty latają) | `bench` |
| Trójplanarne na C (:222) | `Manual_TriplanarPlanetC_*` (K5-22) | przebieg odbioru na `main` | `Manual` |
| Impostor → woksele (:223) | `ImpostorBlend_*` + `Manual_ImpostorTransition_*` (K5-22) | przebieg odbioru na `main` | domyślna + `Manual` |
| Czas przelotu, mediana ≤ 90 s (:224) | `ShipTransit_*` w `tests/Horyzont.Server.Tests/Ships/` (K5-24; okna — `pending` T1, S2) | mediana do najbliższego sąsiada w `SpaceLoop_*` i `planets4-flight` | `Slow`; `bench` |
| Przepinanie pod opóźnieniem (:225) | `Seats_ThousandRandom_*` (K5-25), `Reparent_ThousandRandom_*` (K5-20) | w katalogu | `Slow` |
| Predykcja pilota (:226) | `PilotPrediction_Rtt100_*` (K5-25) | `PilotLanding_PlanetC_Engine_CorrectionsBelow05mIn95Percent` (prawdziwy teren) | `bench` |
| Odtwarzanie (:227) | `ShipState_CheckpointRestart_*`, `ShipLoadSettler_*` (K5-24), `Checkpoint_SeatedOfflinePlayer_*` (K5-25) | `SpaceRecovery_*` (lecący statek + fotele + znaczniki + awaria) | `Slow`; `bench` |
| Autolądowanie 100 m, 1 km, 5 km (:228) | Core + teren domowy 100 m (K5-24) | `Autoland_RealTerrain_{Moon,B,C}_*` | `game` (100 m) / `bench` (3 wysokości) |
| Blokada startu (:229) | `ShipLaunchGuard_*`, `FlightHud_LaunchBlock*` (K5-25) | w katalogu | domyślna + silnik |

## Projekt

### Decyzje

1. **Katalog jako dane, sprawdzany w CI.** `tools/space/k5_acceptance.json`: lista wpisów `{ "id": "K1" | "T219" …, "spec": "02:233", "tests": ["Nazwa", …], "where": "ci|slow|game|bench|manual", "threshold": "…" }`. `k5_acceptance.py --check` (krok CI w `ci.yml`, job `build-and-test`): każda nazwa testu .NET występuje jako metoda z `[Fact]`/`[Theory]` w `tests/**/*.cs`, każda nazwa testu silnika w `game/tests/**/*.cs`, każda procedura `Manual_*` w planie pakietu-właściciela, każdy job (`game`, `bench`) w `.github/workflows/`; brak → kod wyjścia 1 z listą. Test z `Skip` (`[Fact(Skip = …)]`, `[Theory(Skip = …)]`, gdUnit `skip`) nie jest dowodem: błąd, chyba że wpis ma `"pending": "<id pytania właściciela>"` — wtedy status `PENDING` w raporcie (S6; dziś: K1 ↔ `ShipTransit_WithPulse_Moon50_B70_C100s`, `pending: "K5-24 T1"`). `--report <katalog artefaktów>` składa tabelę wyników (markdown) do PR i do sekcji „Raport odbioru” tego planu. `--self-test` na wbudowanych przykładach (jak skrypty K5-22). Python 3.12, tylko biblioteka standardowa.
2. **`TransitGuidance` w Core** (`src/Horyzont.Core/Ships/Guidance/`, poza `Ships/Flight/`, więc nie podlega regule „model lotu wspólny z predykcją”): **przeniesienie 1:1** ciała `DevTransitPilot` (S3 — już czysta klasa z fazą, zależna tylko od Core) jako `TransitGuidance(bool usePulse = true)` z `Phase` (`TransitPhase` = dzisiejsze `DevTransitPhase`) i tą samą sygnaturą `Compute(in ShipMotion, ShipStats, in FlightEnvironment, in Vector3D targetCenter, double targetRadius, double groundDistance, FlightRules, ReadOnlySpan<BodySphere> obstacles)`; `BodySphere` też do Core. `DevTransitPilot` (Server) = cienka powłoka z niezmienionym publicznym API (testy K5-24/K5-26 bez edycji). Dowód: `ShipTransit_*` (`tests/Horyzont.Server.Tests/Ships/ShipTransitTests.cs`, `TransitSim`) bez zmian w plikach i z tymi samymi czasami w wyjściu testu (porównanie liczb w PR) — bez osobnego testu „zapisu sterowania”. Powód: boty (`Horyzont.Bots` → Core/Net/Client) nie mogą odwołać się do `Horyzont.Server` (Y6).
3. **Boty-załogi** (`Horyzont.Bots`): `BotRole.Crew = 3`; akcje `BotAction` (enum botów, nie protokół): `SeatEnter = 9`, `SeatExit = 10`, `ShipMode = 11`, `Refuel = 12`, `Fly = 13`, `BuildBlueprint = 14`. Załoga = 2 boty (pilot w kokpicie, drugi na fotelu pasażera albo nawigatora). `BotShipPilot` (Bots/Client): stan własnego statku z `ShipPrediction` (K5-25; korekty `ShipCorrection`), układ z `ClientStarSystem`, sterowanie z `TransitGuidance`, wysyłka przez `ShipInputSequencer` co tick bez przerw (sloty K5-25 R1) — to ta sama ścieżka co gracz (wejście 30, korekty 34, `PilotControlSource`). Odległość do gruntu (S4): `IBotGroundProbe` — w CI (`CoopRig`, w procesie) przemiatanie `SphereGroundCollider` jak `TransitSim`; w nocy (boty bez silnika i bez terenu) dolna granica `Altitude + clearance − GameRules.TerrainMaxReliefMeters`, a gdy spadnie do 0 — sterowanie z `AutolandController.Compute` (Core) do `Parked`; czas podejścia raportowany osobno. Bot nie dostaje uprzywilejowanych danych serwera. `BotClient` obsługuje komunikaty 31–34 przez istniejące klasy klienta (bez nowych dekoderów). Statystyki załogi w `BotReport`: czasy odcinków (start = zdjęcie `Parked`, koniec = `Parked` na celu), odmowy wg kodu, korekty pilota (p95), `TerrainHold`/`TerrainUnavailable` w sekundach.
4. **Budowa statku z wzorca.** `ShipBlueprint` (Bots i serwer czytają ten sam JSON: `{ "format": 1, "blocks": [{ "cell": [x,y,z], "type": "core:ship_engine_t1", "orientation": n }] }`, kolejność = kolejność stawiania od bloku przy gruncie) — `game/content/dev/ships/thirty.json` i `refuel_pad.json`. Test `ShipBlueprint_Thirty_MatchesShipFixturesThirty` (te same komórki, typy, orientacje). Boty w CI budują wzorzec intencjami `PlaceBlockIntent` (ta sama ścieżka co gracz, z kubełkiem). Serwer dedykowany w nocnym profilu: `ship spawn <blueprint> <planet:id> <x> <y> <z>` (tylko `--dev`) stawia ten sam wzorzec **operacjami serwera istniejących rodzajów** (S5): `ConstructCreateOp` (2) + `BlockSetOp` (3) w kolejności wzorca przez `ServerSimulation.ApplyServerOperation`, każda po `OperationApplier.CheckPreconditions`, a cała budowla po sprawdzeniu gruntu (`ITerrainService.TrySampleSolid` w komórkach; kolizja → odmowa bez zmian w świecie); linia `SHIPSPAWN id=… blocks=… result=…` (format stały). Intencje `dev-op` odpadają: ten gracz jest offline w punkcie startu, a intencje bloków wymagają zasięgu i linii wzroku. Bez nowych rodzajów operacji.
5. **Pętla 8 botów w CI** (`SpaceLoop_EightBots_BuildRefuelFlyToEachBody_NoLoadingScreen`, `tests/Horyzont.Integration.Tests/Space/`, `Slow`): `CoopRig` z `ServerRuntime` (atrapa terenu dla czterech ciał z `SphereGroundCollider` i `FakeServerTerrain`, gotowość terenu z atrapy `IPlanetTerrainReadiness`: `Streaming` przez 3 s po aktywacji, potem `Ready`), układ z ziarna 0, RTT 100 ms, 1 % strat; 4 załogi × 2 boty. Przebieg: każda załoga buduje `thirty` i jedną stację tankowania intencjami → `ShipModeIntent` → `RefuelIntent` → fotele → loty z napędem w rotacji (załoga i leci na ciało `(i mod 3) + 1`, wraca, potem kolejne), aż każda załoga odwiedzi wszystkie trzy ciała. Asercje: (a) każda załoga dotarła na każde z trzech ciał i zaparkowała; (b) każdy odcinek z napędem zaparkował i trwał ≤ 1,25 × górne okno K5-24 dla ciała celu (60 / 84 / 120 s — ten sam strażnik co `ShipTransit_WithPulse_ParksAndReportsTime`); okno spec [50; 100] s (:233) i okna ±20 % — **raport**, a po decyzji właściciela K5-24 T1 — próg twardy (S2; dziś K5-24 mierzy 63 / 103 / 124 s przy τ = 8 s); (c) mediana odcinków „do najbliższego sąsiada” (wg `StarSystemModel` dla ciała startu) ≤ 90 s (spec :224); (d) „bez ekranu ładowania” = każdy klient przez cały przebieg w stanie `InGame` (zero przejść do `Loading` po pierwszym wejściu) i zero `ResyncRequest` statków; (e) zero `Unavailable` bramki. Czas symulacji ok. 12 min; budżet czasu testu w CI ≤ 6 min (jeśli przekroczony — wariant 2 załóg w CI, pełne 4 w `bench`, zapis w „Odstępstwach”).
6. **Pasażerowie przy RTT 100 ms i 5 % strat** (`SpaceCoop_PilotAndSeatedPassengers_Rtt100Loss5_SmoothAndIdentical`): uprząż i miary K5-26 (decyzja 12 — „identyczne”; miary K5-20 — skok ≤ 0,5 m ponad `v·Δt`, błąd ≤ 0,5 m, zero złej ramy), ale fotele przez `SeatEnterIntent` z klientów, pilot = bot z `BotShipPilot` (`ShipInputBatch`), a nie `DevTransitPilot` ani `FrameTransitions`. Seria: start, przejście strefy domowa → układ → księżyc, postój, przesiadka, powrót; dwa ziarna. Raport jak K5-26 + p95 korekt pilota.
7. **Odtwarzanie po awarii** (`SpaceRecovery_CrashMidFlight_ReplaySameHash_ShipsSettledSeatsAndMarkersKept`, `Slow`; SQLite w katalogu tymczasowym przez `PersistenceWriter`/`WorldLoader`): świat z ziarnem 0; statek A leci (pilot + pasażer w fotelach) w ramie planety domowej na 1 km, statek B w ramie układu między ciałami, statek C zaparkowany z graczem offline w fotelu; znaczniki wspólne i prywatne (K5-27); po checkpoincie dalsze operacje w dzienniku (przesiadka, `ShipFuelAddOp`, `MarkerSetOp`, `SeatEvictIntent` na C, `ShipModeSetOp` innej budowli). „Awaria” = porzucenie `ServerRuntime` bez końcowego checkpointu i bez `FlushAsync` ponad ostatni zatwierdzony zapis dziennika; potem `WorldLoader` + powtórka ogona. Asercje: skrót `StateHasher` po powtórce = skrót przed awarią (zapisany przy ostatniej operacji dziennika); A (niezaparkowany w strefie) po `ShipLoadSettler` stoi na gruncie z `Parked`; B w układzie z v = 0 bez postoju; gracze A w tych samych fotelach jako offline, po `PlayerJoin` siedzą (`KindData` u obserwatora = fotel); gracz z C po zwolnieniu w punkcie startu świata; paliwo = z checkpointu, a po `ShipFuelAddOp` z ogona = pojemność (K5-24 N16); znaczniki i `next_marker_id` jak przed awarią. Wariant `SpaceRecovery_CleanRestart_*`: zamknięcie wg ZASADY pkt 7 — to samo plus pozy statków z checkpointu bitowo. Korpus złoty i `DeterminismTests` bez zmian (Y14).
8. **Planeta C na prawdziwym miejscu** (`game/tests/Space/PlanetCTests.cs`, silnik): świat z ziarnem 0 (układ K5-21, teren C aktywny przez dzierżawę K5-22), `PlanetC_RealPlacement_CharacterAndParkedShip_JitterBelow1mm`: postać stoi 600 ticków na terenie C (ok. 110 km od początku układu — liczba z `StarSystemModel`, wpisana w raport): maks. przesunięcie ≤ 1 mm, ciągły kontakt; statek `thirty` zaparkowany na C: dryf pozy ≤ 1 mm przez 600 ticków, `ShipHullBoxes`/`GodotShipCollider` bez trafień poza gruntem; `ShipLanding_PlanetC_AutolandFrom100m_HullContactStable` — autolądowanie na C, po zaparkowaniu 300 ticków bez drgań > 1 mm. Wymaga jednego runnera (Y15).
9. **Autolądowanie na terenie trzech ciał** (`game/tests/Space/AutolandRealTerrainTests.cs`): `Autoland_RealTerrain_{Moon,B,C}_From100m_Parks` w jobie `game` (3 testy), `…_From1km_Parks` i `…_From5km_Parks` w nocnym `bench` (6 testów); statek bez pilota w ramie ciała nad losowym punktem (ziarno w nazwie), bramka K5-22/K5-24 prawdziwa (`PlanetTerrainShipGate`); asercje: `Parked`, v = 0, dryf poziomy ≤ 5 m (K5-24), zero ticków z bramką `Ready` przy braku kolizji pod statkiem (sonda kolumny K5-22), czas do postoju w raporcie.
10. **Lądowanie pilota z predykcją na prawdziwym terenie** (`PilotLanding_PlanetC_Engine_CorrectionsBelow05mIn95Percent`, `bench`): host z klientem przez `LatencyTransport` (RTT 100 ms), bot-pilot ląduje na C 5 razy; p95 błędu korekt < 0,5 m, skoki ≥ 5 m = 0 (ryzyko spec :265 — K5-25 mierzy to tylko na atrapie zderzacza).
11. **Nocny profil `planets4-flight`** (`tools/coop/coop_soak.py`, obok `planets4` z K5-22): serwer dedykowany (maska 6 rdzeni jak `planets4`), 8 botów-załóg (4 statki ze `ship spawn`, paliwo `ship fuel`), 15 min: załogi latają w rotacji między wszystkimi czterema ciałami (odcinki do najbliższego sąsiada i dalsze), boty-pasażerowie wysiadają na celu i wracają na fotel. Pomiary co 60 s (`stats`): średni tick (twardy próg **≤ 8 ms** z całego przebiegu), p99, RAM procesu, czas budowy kolizji na ciało, liczba aktywnych terenów; z `BotReport`: mediana odcinków do najbliższego sąsiada (twardy próg ≤ 90 s), wszystkie odcinki z napędem (raport; poza [50; 100] s → ostrzeżenie w raporcie), czas w `TerrainHold`. Wpis w `budgets.json` (`planets4_flight_tick_ms: 8`, `nearest_transit_median_s: 90`) i w `nightly.yml`. Niezaliczone 8 ms → regulator K5-22 i ponowny pomiar; dalej niezaliczone → plan awaryjny spec :261 (decyzja właściciela, P2).
12. **Awaria procesu w nocy** (`coop_soak.py --profile planets4-flight --crash-at 600`): w chwili `t` boty wstrzymują wysyłkę na 5 s, skrypt pobiera `digest` (skrót + `LastOpId`), zabija proces serwera (bez zamknięcia), uruchamia go ponownie na tym samym świecie i pobiera `digest` — równość skrótu i `LastOpId`; po wznowieniu boty łączą się, siedzący wracają na fotele, statki niezaparkowane stoją na gruncie (log `ShipLoadSettler`). Wynik w raporcie nocnym.
13. **Przebieg odbioru na `main`** (krok 10): po rebase jeden commit `main`: pełne komendy odbioru, job `game` (szeregowo, Y15), ręczne uruchomienie `nightly.yml` (`workflow_dispatch`) z numerem runu, procedury `Manual` z K5-22 (`Manual_ImpostorTransition_*`, `Manual_TriplanarPlanetC_*`), K5-24 (`Manual_Flight_HomeToMoon_InGame`), K5-25 (`Manual_Seats_*`), K5-26 (`Manual_TwoClients_*`), K5-27 (ręczny przebieg znaczników) — wyniki w „Raport odbioru” (tabela z `k5_acceptance.py --report`) i w PR. Każde „nie przechodzi” = bloker bramki K5 z odnośnikiem do pakietu-właściciela.

### Pliki

```
tools/space/k5_acceptance.json, tools/space/k5_acceptance.py (+ --self-test)
.github/workflows/ci.yml                 krok k5_acceptance.py --check
.github/workflows/nightly.yml            planets4-flight, --crash-at, testy From1km/From5km, PilotLanding
tools/coop/coop_soak.py                  profil planets4-flight, --crash-at; budgets.json
src/Horyzont.Core/Ships/Guidance/        TransitGuidance, TransitTarget, TransitPhase
src/Horyzont.Server/Ships/DevTransitPilot.cs              powłoka nad TransitGuidance
src/Horyzont.Server/Ships/               ShipBlueprint, ShipBlueprintSpawner
src/Horyzont.Server/Console/ConsoleCommandProcessor.cs    ship spawn
src/Horyzont.Bots/Scripting/             BotRole (+ Crew), BotAction (+ 9–14), CrewScript
src/Horyzont.Bots/Client/                BotShipPilot, BotClient (komunikaty 31–34 przez klasy klienta)
src/Horyzont.Bots/Runner/BotReport.cs    odcinki, korekty, TerrainHold
game/content/dev/ships/thirty.json, refuel_pad.json
tests/Horyzont.Core.Tests/Ships/         TransitGuidanceTests
tests/Horyzont.Bots.Tests/               CrewScriptTests, BotShipPilotTests, ShipBlueprintTests
tests/Horyzont.Integration.Tests/Space/  SpaceLoopTests, SpaceCoopPassengerTests, SpaceRecoveryTests
game/tests/Space/                        PlanetCTests, AutolandRealTerrainTests, PilotLandingTests
docs/plans/K5-28-testy-kosmosu.md        „Raport odbioru”
```

## Testy akceptacyjne (napisz najpierw)

| Test | Plik | Kategoria | Faza | Co sprawdza |
| --- | --- | --- | --- | --- |
| `k5_acceptance.py --check` / `--self-test` | `tools/space/k5_acceptance.py` | CI (`ci.yml`) | A | decyzja 1; czerwony, dopóki brak testów K5-28 |
| `ShipTransit_*` (K5-24, pliki bez zmian, te same czasy) / `TransitGuidance_PhasesFromLiftOffToArrived` (krótki przelot 100 m na `SphereGroundCollider`) | `tests/Horyzont.Server.Tests/Ships/ShipTransitTests.cs`, `tests/Horyzont.Core.Tests/Ships/TransitGuidanceTests.cs` | domyślna | A | decyzja 2 (S3) |
| `BotGroundProbe_Conservative_NeverAboveTrueDistance` / `BotShipPilot_NoProbe_HandsOverToAutolandAndParks` / `ShipSpawn_ServerOpsOfExistingKinds_RejectsOverlapWithoutChange` | `tests/Horyzont.Bots.Tests/`, `tests/Horyzont.Server.Tests/Ships/` | domyślna | A' | decyzje 3–4 (S4, S5) |
| `ShipBlueprint_Thirty_MatchesShipFixturesThirty` / `ShipBlueprint_RejectsUnknownTypeAndBadOrientation` / `ShipSpawn_DevOnly_BuildsViaDevOpIntents` | `tests/Horyzont.Bots.Tests/ShipBlueprintTests.cs`, `tests/Horyzont.Server.Tests/Ships/` | domyślna | A | decyzja 4 |
| `CrewScript_PilotAndPassenger_SeatsFliesParksReturns` / `BotShipPilot_UsesShipInputBatchAndCorrections_NoServerAccess` | `tests/Horyzont.Bots.Tests/` | domyślna | A | decyzja 3 (na `CoopRig` 1 załogi, krótki lot na 100 m) |
| `SpaceLoop_EightBots_BuildRefuelFlyToEachBody_NoLoadingScreen` | `tests/Horyzont.Integration.Tests/Space/SpaceLoopTests.cs` | `Slow` | B | decyzja 5 (K1, :224) |
| `SpaceCoop_PilotAndSeatedPassengers_Rtt100Loss5_SmoothAndIdentical` | `…/Space/SpaceCoopPassengerTests.cs` | `Slow` | B | decyzja 6 (K2) |
| `SpaceRecovery_CrashMidFlight_ReplaySameHash_ShipsSettledSeatsAndMarkersKept` / `SpaceRecovery_CleanRestart_PosesBitwiseSeatsAndMarkersKept` | `…/Space/SpaceRecoveryTests.cs` | `Slow` | B | decyzja 7 (K3, :227) |
| `PlanetC_RealPlacement_CharacterAndParkedShip_JitterBelow1mm` / `ShipLanding_PlanetC_AutolandFrom100m_HullContactStable` | `game/tests/Space/PlanetCTests.cs` | silnik (`game`) | C | decyzja 8 (:219) |
| `Autoland_RealTerrain_{Moon,B,C}_From100m_Parks` | `game/tests/Space/AutolandRealTerrainTests.cs` | silnik (`game`) | C | decyzja 9 (:228) |
| `Autoland_RealTerrain_{Moon,B,C}_From1km_Parks` / `…_From5km_Parks` | jw. | `Soak` (`bench`) | C | decyzja 9 |
| `PilotLanding_PlanetC_Engine_CorrectionsBelow05mIn95Percent` | `game/tests/Space/PilotLandingTests.cs` | `Soak` (`bench`) | C | decyzja 10 (:226, :265) |
| `coop_soak.py --profile planets4-flight` / `… --crash-at 600` | `tools/coop/coop_soak.py` | noc (`bench`) | D | decyzje 11–12 (K1, K3, K4, :221, :224) |

## Kroki

Każdy krok: test czerwony → kod → zielone testy → mały commit (Conventional Commits). Wyszukiwanie kodu i uruchamianie testów zlecać subagentowi Haiku (CLAUDE.md projektu).

**Faza A — katalog i narzędzia**

- [ ] 0. Warunki wejścia: `git status`, worktree `F:/dev/horyzont-k5-28` od `origin/main` po scaleniu K5-22, K5-25, K5-26, K5-27; w „Odstępstwach” zapisać faktyczne nazwy testów wszystkich pakietów K5 (grep — katalog musi mieć prawdziwe nazwy), API botów K4-19 (`BotClient`, `BotScript`, `BotReport`), profile `coop_soak.py`, joby `ci.yml`/`nightly.yml`, wynik ostatniego nocnego `planets4` i `Descent8km` (numery runów), czy K4-19 i domknięcie K5-22 (kroki 9–10) są na `main` (S1), stan K5-24 T1 (S2), mechanizm podziału testów silnika `game`/`bench` z K5-22 i droga opóźnienia łącza w teście silnika bez `Horyzont.Bots` (S8), faktyczne ścieżki testów K5-25 po przeglądzie (R10). Commit `docs:`.
- [ ] 1. `k5_acceptance.json` (wszystkie wiersze tabel „Mapa kryteriów”) + `k5_acceptance.py` (`--check`, `--report`, `--self-test`) + krok w `ci.yml`; `--check` czerwony tylko dla testów K5-28. Commity `test:`, `ci:`.
- [ ] 2. `TransitGuidance` (Core) — przeniesienie 1:1 z `DevTransitPilot` (S3); `ShipTransit_*` bez zmian w plikach i z tymi samymi czasami w wyjściu (liczby przed/po w PR). Commity `test:`, `refactor:`.
- [ ] 2a. **Bramka K4-19:** jeśli K4-19 nie jest na `main` — przejdź do fazy C (kroki 7–8 bez części `bench`), wróć do kroku 3 po scaleniu K4-19 (zapis w „Odstępstwach”).
- [ ] 3. `ShipBlueprint` + `thirty.json`/`refuel_pad.json` (test zgodności z `ShipFixtures.Thirty`), `ship spawn` (dev), `BotRole.Crew`, akcje 9–14, `CrewScript`, `BotShipPilot`, `BotReport` (odcinki); testy botów na `CoopRig` z jedną załogą. Commity `test:`, `feat:`.

**Faza B — scenariusze CI**

- [ ] 4. `SpaceLoop_EightBots_*` (decyzja 5); raport odcinków i mediany w PR; czas testu w CI zmierzony (> 6 min → wariant 2 załóg w CI, 4 w `bench`, zapis w „Odstępstwach”). Commit `test:` (+ `fix:` z testem, gdy znaleziony błąd mieści się w „Poza zakresem” pkt 2).
- [ ] 5. `SpaceCoop_PilotAndSeatedPassengers_*` (decyzja 6); raport miar. Commit `test:`.
- [ ] 6. `SpaceRecovery_*` (decyzja 7); `git diff origin/main` pusty na korpusie złotym, `TerrainJournalGoldenCorpusTests.cs`, `DeterminismTests.cs` (Y14). Commit `test:`.

**Faza C — silnik**

- [ ] 7. `PlanetC_*`, `ShipLanding_PlanetC_*` (job `game`, szeregowo — Y15; X11: czas silnika w raporcie). Niezaliczone drgania → stop i eskalacja do Opus (ryzyko spec :262; K5-20 P1 — plan B zmienia K5-21/22). Commit `test:`.
- [ ] 8. `Autoland_RealTerrain_*` (100 m w `game`, 1 km i 5 km w `bench`), `PilotLanding_PlanetC_*` (`bench`). Commit `test:`.

**Faza D — noc i odbiór**

- [ ] 9. `planets4-flight` i `--crash-at` w `coop_soak.py` (+ `--self-test`), `budgets.json`, `nightly.yml`; `workflow_dispatch` z `minutes=15`, numer runu i liczby (tick średni/p99, RAM, czas kolizji na ciało, mediana przelotu, wynik awarii) w PR. Niezaliczone 8 ms → regulator K5-22 i ponowny pomiar; dalej → P2 do właściciela. Commity `feat:`, `ci:`.
- [ ] 10. Rebase na `main`; pełne komendy odbioru; przebieg odbioru (decyzja 13) i `k5_acceptance.py --report` → sekcja „Raport odbioru”; `docs/ZASADY.md` pkt 8: „scenariusze regresji Kosmosu są w `tools/space/k5_acceptance.json`; nowy test K5 albo zmiana nazwy = wpis w katalogu (sprawdza CI)”; wiersz scalenia K5-28 w „Kolejność K5” `docs/plans/README.md` (bez `ProtocolVersion` — K5-28 go nie podbija); kryteria spec :233–237 odhaczone w PR z odnośnikami do dowodów (sam plik spec zmienia właściciel na bramce). Commit `docs:`.

## Odbiór

```
dotnet build Horyzont.sln
dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"
dotnet format Horyzont.sln --verify-no-changes
python tools/engine/build.py smoke
python tools/space/k5_acceptance.py --check
python tools/space/k5_acceptance.py --self-test
```

Plus job `game` (testy silnika `game/tests/Space/`, `Ships/`, `Frames/` — szeregowo), ręczny run `nightly.yml` (`planets4-flight`, `--crash-at`, `Autoland_RealTerrain_*_From1km/5km`, `PilotLanding_*`, `Descent8km` 20 prób, `planets4`) z numerami runów, wyniki procedur `Manual` z pakietów K5 i `git diff origin/main -- tests/Horyzont.Core.Tests/Golden/TerrainJournals tests/Horyzont.Core.Tests/TerrainJournalGoldenCorpusTests.cs tests/Horyzont.Core.Tests/Hashing/DeterminismTests.cs` = pusty.

## Kody i punkty styku

K5-28 **nie zajmuje** żadnych kodów (`OperationKind`, `IntentCode`, `RejectReason`, `MessageType`, `BlobKind`, migracje) i **nie podbija** `ProtocolVersion` (Y3 — nie ma go na liście). Wartości poza formatem: `BotRole.Crew = 3`, `BotAction` 9–14 (enumy botów, nie protokół); format wzorca statku `format: 1` (dane dev, nie zapis świata). Potrzeba zmiany formatu albo nowego kodu w trakcie = stop i pytanie (zakres `RejectReason` 30–39 jest wyczerpany — Y2).

Kolejność scalania K5: K5-28 ostatni (**K5-23 → K5-20 → K5-21 → K5-22 → K5-24 → K5-27 → K5-26 → K5-25 → K5-28**). Zasada X10 — K5-28 integruje wszystko, co jest na `main`.

| Plik / punkt | K5-28 | Inny pakiet | Rozwiązanie |
| --- | --- | --- | --- |
| `tools/coop/coop_soak.py`, `budgets.json`, `nightly.yml`, `BenchmarkReport.cs` | `planets4-flight`, `--crash-at`, progi | K4-19 (właściciel), K5-22 (`planets4`, `descent8km`) | dopisywanie profilu i wpisów obok (K5-22 wprost przewiduje, że K5-28 rozszerza) |
| `src/Horyzont.Bots/` (K4-19) | rola `Crew`, akcje 9–14, `BotShipPilot`, raport | — | dopisywanie; istniejące role bez zmian (testy K4-19 zielone) |
| `DevTransitPilot.cs` (K5-24) | powłoka nad `TransitGuidance` | K5-24, K5-26 (testy go używają) | test bitowy sterowania; publiczne API `DevTransitPilot` bez zmian |
| `ConsoleCommandProcessor.cs` | `ship spawn` | 20 `frame`, 23 `ship`, 24 `ship pilot/fuel`, 22 `place`, 25 `seat` | osobna podkomenda, stały format linii |
| `game/tests/Space/` | `PlanetCTests`, `AutolandRealTerrainTests`, `PilotLandingTests` | K5-22 (`DescentTests`) | osobne pliki; szeregowo (Y15) |
| `.github/workflows/ci.yml` | krok `k5_acceptance.py --check` | — | jeden krok w `build-and-test` |
| `docs/ZASADY.md`, `docs/plans/README.md` | wiersz K5-28, reguła katalogu | wszyscy | dopisywanie (Y1) |

## Ryzyka

| Ryzyko | Skutek | Odpowiedź |
| --- | --- | --- |
| Tick > 8 ms przy lotach 8 załóg (koszt terenów, kolizji statków i widzów) | kryterium K4 niespełnione | pomiar w `planets4-flight`; regulator K5-22; plan awaryjny w danych spec :261 = decyzja właściciela (P2) |
| Drgania Jolt na prawdziwej C (inna niż przesunięty teren K5-20) | kryterium :219 niespełnione | stop i eskalacja do Opus w kroku 7 (plan B K5-20 P1) |
| Czas `SpaceLoop_*` w CI (12 min symulacji, 8 klientów) | wolne CI | pomiar w kroku 4; wariant 2 załóg w CI, pełny w `bench` |
| Bramka terenu wydłuża przeloty (`TerrainHold`) | mediana > 90 s w nocy, choć Core mieści się w oknach | raport czasu w `TerrainHold` na odcinek; diagnoza w K5-22/K5-24, nie zmiana progu |
| Testy silnika na wspólnym runnerze zabijają joby gry (pamięć projektu) | fałszywe czerwone | Y15: szeregowo, jeden runner; lokalnie nie uruchamiać testów silnika równolegle z jobem `game` |
| Awaria procesu w nocy zależna od chwili (`t` w trakcie zapisu) | flaky | 5 s ciszy botów przed `digest`; 3 próby przy różnych `t` w raporcie |
| Błędy w pakietach K5 znalezione dopiero tu | K5-28 rośnie | „Poza zakresem” pkt 2: zgłoszenie do właściciela pakietu, bloker bramki |
| Bot-pilot gorszy niż gracz (przeloty dłuższe) | fałszywe niezaliczenie mediany | `TransitGuidance` = ta sama logika co `DevTransitPilot` (K5-24 spełnia okna w Core); raport porównuje oba |

## Pytania do właściciela (wariant domyślny obowiązuje bez odpowiedzi)

| # | Pytanie | Wariant domyślny |
| --- | --- | --- |
| P1 | Sprzęt referencyjny dla progów 8 ms i zejścia (spec :220, :236)? | runner `horyzont` (komputer właściciela, maska 6 rdzeni jak w `planets4`) |
| P2 | Tick > 8 ms po regulatorze — plan awaryjny spec :261 (mniejsze promienie i odległości)? | stop; decyzja właściciela przed zmianą `StarSystemLayout` (nowe światy), K5-28 raportuje liczby |
| P3 | „8 graczy buduje statek” — czy wystarczy budowa botami intencjami w CI, a w nocy `ship spawn`? | tak: CI dowodzi ścieżki budowy, noc — wydajności lotów |
| P4 | Okno 50–100 s dla każdego odcinka z napędem czy tylko dla najbliższych? | twardo w CI (`SpaceLoop_*`, deterministyczne), w nocy tylko mediana do najbliższego sąsiada twarda, reszta raportowana |
| P5 | Awaria procesu w nocy — prawdziwe zabicie serwera dedykowanego? | tak (`--crash-at`), 3 próby w raporcie |
| P6 | Kto odhacza kryteria w `docs/spec/02-kosmos.md`? | właściciel na bramce K5; K5-28 daje dowody w PR i „Raport odbioru” |
| P7 | Kto domyka K5-22 kroki 9–10 (`descent8km`, `planets4`) po scaleniu K4-19? | wykonawca K5-22 w PR uzupełniającym; K5-28 faza D czeka; przejęcie przez K5-28 tylko na decyzję właściciela (S1) |
| — | (zależność) K5-24 T1: strojenie τ / v_min dla okien 50–100 s | K5-28 raportuje, kryterium K1 = `PENDING` do decyzji (S2) |

## Pułapki

- Nie pisać od nowa testów, które mają pakiety K5 — katalog na nie wskazuje; K5-28 dokłada tylko brakujące scenariusze.
- Bot nie czyta stanu serwera: sterowanie wyłącznie z danych klienta (`ShipPrediction`, `ClientStarSystem`), wysyłka `ShipInputBatch`.
- `ship spawn` działa tylko z `--dev`, operacjami serwera istniejących rodzajów 2 i 3 (S5); żadnego nowego rodzaju operacji.
- Test z `Skip` nie jest dowodem; okna przelotów czekają na K5-24 T1 (S2, S6).
- Skrót po powtórce porównywać przy tym samym `LastOpId`; `ShipState` i pozy statków nie są w skrócie (spec :194).
- Testy silnika szeregowo (Y15); pomiary z czasem silnika i czasem sieci osobno (X11).
- Progi z spec nie są luzowane w testach; niezaliczenie = diagnoza i zgłoszenie, nie zmiana asercji.

## Raport odbioru

(wypełnia wykonawca w kroku 10: commit `main`, tabela z `k5_acceptance.py --report` — kryterium, test, job/run, wynik, liczby; numery runów `game` i `bench`; wyniki procedur `Manual`)

## Odstępstwa i decyzje wykonawcy

(wypełnia wykonawca: stan K5 na `main`, faktyczne nazwy testów pakietów K5, czas `SpaceLoop_*` w CI i ewentualny wariant 2 załóg, wyniki nocne, znalezione błędy i ich pakiety-właściciele, odpowiedzi właściciela, różnice od planu)
