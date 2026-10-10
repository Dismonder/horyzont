# K4-19 · Boty, testy integracyjne i wytrzymałościowe, benchmarki (odbiór Planety)

| | |
| --- | --- |
| Prowadzi | Sonnet 5.5 (Claude Code, `claude --model sonnet`; `--model opusplan` w kroku 0, gdy mapowanie nazw K3 wykaże rozjazd z tym planem) |
| Testy akceptacyjne | Sonnet 5.5; scenariusze trwałości (krok 4) i definicję pomiaru 200 ms (P5) przegląda Opus 5.5 przed kodem |
| Przegląd | na bramce K4 (Opus 5.5, świeża sesja, cała zmiana); Opus 5.5 w PR, gdy pakiet musi dotknąć `src/Horyzont.Core/` (poprawka błędu znalezionego przez boty) albo `Directory.Packages.props` (BenchmarkDotNet) |
| Zależy od | K2-13 … K3-18 (plan projektu). Faza A rusza od razu na `main` z K2 i szwami K3-00; fazy B–D czekają na scalenie wskazanych pakietów K3 (tabela „Fazy”) |
| Gałąź | `pakiet/k4-19-odbior` (worktree `F:/dev/horyzont-k4-19`) |
| Kamień milowy | K4 Odbiór; odbiór pakietu = wszystkie kryteria ukończenia z `docs/spec/01-planeta.md` spełnione albo jawnie przeniesione decyzją właściciela (P1–P3) |

**Cel:** zbudować boty (lekkie .NET i pełne w silniku), testy integracyjne, test wytrzymałościowy 2 h z 8 botami na serwerze dedykowanym i benchmarki, a potem przeprowadzić nimi odbiór wszystkich kryteriów ukończenia Planety i zapisać dowody w `docs/reports/K4-19-odbior.md`.

## Kontekst

- `docs/ZASADY.md`, punkty 2 (wątki, autorytatywny serwer, oko ze stóp), 3 (kontrakty chronione), 6 (alokacje), 7 (kolejność zamykania, `QuitCoordinator`), 8 (kategorie testów, prawdziwy czas tylko w `Soak` i transportach), 10–11.
- `docs/spec/01-planeta.md`: Sieć (tick 60 Hz, migawki 20 Hz, wejście w paczkach, zainteresowanie 500 m, limity, 8 graczy, 50 KB/s), Zapis i odtwarzanie (partia dziennika 100 ms, checkpoint), Obsługa błędów, Testy (tabela poziomów), **Kryteria ukończenia** (9 punktów).
- `docs/plan/plan-projektu.md`, wiersz K4-19 i kamienie milowe; `docs/plans/README.md`: K3 (X1–X11), „CI na własnym runnerze”.
- Plany K3 (jeszcze nie na `main`; czytać z `F:/dev/horyzont-k3-15/docs/plans/`): K3-14 (`MotionStatistics`, `LinkConditions` 50 ms + 5 %), K3-15 (`EntityMirror`, `ConstructMirror`, `RemotePlayers`, test 10 min konstrukcji, P2 „godzina w K4-19”), K3-16 (`ClientTerrainCache`, `TerrainSync`/`TerrainView`, `TerrainDigest`, test 600 edycji, „godzina i soak 2 h należą do K4-19”), K3-17 (Manual 8 kont Steam „koordynowane z K4-19”), K3-18 (`ServerNode.LastTickMilliseconds`, `TransportStats`, `GetTransportStats`).
- Kontrakty z K2 przeznaczone dla K4-19: `ToolController`, `ToolInputFrame`, `AimRay`, `AimRayBuilder`, `IIntentSender`, `SessionIntentSender`, `IntentPacer` (K2-13); `SurfaceFrame`, `CharacterMotor` (K2-11); `ServerRuntime`, `IServerTerrainFactory`, `FakeServerTerrain(Factory)` (K2-10); `TerrainDigest`, `TerrainBlockCodec` (K2-09); uprząż `game/tests/`, `--generator-report` (K2-08).

Kod, na którym pakiet buduje (gałąź pakietu przy starcie):

| Co | Gdzie |
| --- | --- |
| Klient bez silnika: `ClientRuntime` (sesja, słuchacze), `InputSequencer`, `PredictionBuffer`, narzędzia, `AlwaysReadyWorld` | `src/Horyzont.Client/` |
| Serwer bez silnika: `ServerRuntime` (`StartAsync`, `Tick`, `State`, `RequestCheckpoint`, `RequestShutdown`), konsola `ConsoleCommandProcessor`, budżety replikacji | `src/Horyzont.Server/` |
| Transporty: `InMemoryNetwork` + `LinkConditions` (opóźnienie, jitter, utrata tylko kanału zawodnego), `UdpTransport` (LiteNetLib), `SteamTransport`, `CompositeTransport` | `src/Horyzont.Net/Transport/` |
| `IntentReply(ClientSeq, Reason, AppliedOpId)`, `OperationApplied(Id, Tick, Operation, ConstructChanges)` — korelacja edycji po `OpId` | `src/Horyzont.Core/Protocol/Messages/` |
| Uprząż testów serwera: `ServerRig`, `RigClient` (internal, wzór dla `CoopRig`) | `tests/Horyzont.Server.Tests/Support/` |
| Brama K1 (serwer + 2 klienty + awaria, skrót stanu) | `tests/Horyzont.Integration.Tests/K1GateTests.cs` |
| Soak: `CrashHostSoakTests` (K1-05), `CodecSoakTests` | `tests/Horyzont.Persistence.Tests/`, `tests/Horyzont.Core.Tests/Serialization/` |
| Test dymny serwera dedykowanego (start, konsola, restart, sygnał, uszkodzona baza) | `tools/server_smoke.py` |
| Testy klatek w silniku (próg 50 ms dla budowli), lot kamery orbita→powierzchnia (raport bez progu) | `game/tests/Constructs/ConstructHitch*.cs`, `game/tests/Character/DevCameraTests.cs` |
| CI: `ci.yml` (build-and-test, game: smoke, gdUnit, in-engine, server smoke), `nightly.yml` (Soak, raport generatora), `engine.yml`; jeden runner Windows, Linux wyłączony (decyzja właściciela 2026-10-09) | `.github/workflows/` |

Brak dziś: BenchmarkDotNet (żadnego projektu ani wpisu w `Directory.Packages.props`), pomiaru czasu ticku serwera, botów, testu z wieloma procesami.

## Zakres

- Biblioteka botów bez silnika `src/Horyzont.Bots` (skrypt zachowania, klient bota, skróty replik, dekorator opóźnienia) i jej testy.
- Proces `tests/Horyzont.BotRunner` (N botów .NET w jednym procesie, UDP, czas rzeczywisty).
- Tryb bota w grze `--bot` (tylko z `--dev`, tryb `--connect`): ten sam skrypt steruje prawdziwym klientem Godota (teren Voxel Tools, meshing, predykcja).
- Pomiar czasu ticku serwera (`TickTimeRecorder`), komendy konsoli `stats` i `digest`, linie logu z `OpId` przy starcie, checkpoincie i zatrzymaniu.
- Testy integracyjne .NET (czas wirtualny), scenariusze w silniku, orkiestrator `tools/coop/coop_soak.py` (serwer dedykowany + boty, przerwy kontrolne, restart, zabicie procesu), `tools/modes_smoke.py` (solo, host, klient, serwer).
- Benchmarki BenchmarkDotNet (`tests/Horyzont.Benchmarks`) i tryb raportu w silniku `--benchmark-report`; budżety i skrypt `tools/bench/check_budgets.py`.
- Zmiany CI: kroki w jobie `game`, nocne joby `coop-soak` i `bench`.
- Raport odbioru `docs/reports/K4-19-odbior.md` i surowe podsumowania w `docs/reports/K4-19/`.
- Decyzja `MeshBlockSize` 16/32 (K2-08 pytanie 8) na podstawie pomiaru.

## Poza zakresem

- Zmiany semantyki operacji, intencji, protokołu i formatów (żadnych nowych kodów, `ProtocolVersion` bez zmian). Błąd znaleziony przez boty w Core naprawia się zgodnie z ZASADY (test → poprawka → przegląd Opus), osobnym commitem `fix:`; zmiana kontraktu chronionego = eskalacja, nie obejście.
- Przywracanie Linuksa w CI (decyzja właściciela); K4-19 przygotowuje tylko procedurę Manual (P1).
- Optymalizacje wydajności poza tym, co wymaga niespełnione kryterium; gdy budżet nie przechodzi, pakiet raportuje i eskaluje (osobny pakiet `perf:` po decyzji), nie przepisuje systemów K2/K3.
- Testy Kosmosu, Przetrwania, Walki (K5-28, K6-40, K7-49), testy wydania (K9-65), telemetria (K9-62).

## Fazy i zależność od niescalonego K3

Kroki zależne od niescalonego kodu K3 są na końcu i oznaczone **[K3-xx]**. Po każdym scaleniu pakietu K3 do `main` wykonawca robi rebase `pakiet/k4-19-odbior` i dopiero wtedy rusza z odblokowaną fazą. API K3 cytowane niżej pochodzi z planów; krok 0 każdej fazy mapuje rzeczywiste nazwy i wpisuje różnice do „Odstępstw”.

| Faza | Kroki | Wymaga scalonych | Co daje |
| --- | --- | --- | --- |
| A | 0–8 | K2, K3-00 | boty na poziomie K2 (ruch, kopanie, budowanie na terenie, odmowy), tick serwera, trwałość, restart, benchmarki, smoke trybów, CI |
| B | 9–11 | K3-15, K3-16 | repliki budowli i terenu w botach .NET, skróty u wszystkich, opóźnienie edycji, soak z porównaniem stanu |
| C | 12 | K3-14 (+ K3-15, K3-16) | boty w silniku: skrót wokseli, czas do meshu, statystyki ruchu |
| D | 13 | K3-17, K3-18 | metryki HUD/pasma, procedury Manual Steam, FPS 1080p, Linux |
| E | 14 | wszystkie | przebiegi odbioru, raport, PR |

## Mapa kryteriów ukończenia → dowód

Kategorie: domyślna (zawsze w CI), `Slow` (CI), `Soak` (nocne), `Manual` (wynik w raporcie i PR). „Job” to miejsce uruchomienia.

| # | Kryterium (spec 01, „Kryteria ukończenia”) | Dowód w K4-19 | Kategoria / job | Faza |
| --- | --- | --- | --- | --- |
| 1 | Jedna komenda buduje silnik, SDK i szablony eksportu; CI zielone | `python tools/engine/build.py all` (K1-01) i run `engine.yml` na `main`; trzy kolejne zielone przebiegi `ci.yml` i `nightly.yml` z numerami runów w raporcie | dowód CI / `engine.yml`, `ci.yml`, `nightly.yml` | E |
| 2 | Działają solo, host, klient i serwer dedykowany (Windows i Linux) | `tools/modes_smoke.py`: `--solo`, `--host` + `--connect` (dwa procesy), `--server` + `--connect`, każdy dochodzi do linii `CLIENT IN GAME`/`SERVER READY` i zamyka się kodem 0; serwer dodatkowo `server_smoke.py` (K2-10). Linux: `Manual_ModesSmoke_Linux` według P1 | gating / `ci.yml` job `game` (Windows); Manual (Linux) | A (+D dla Linuksa) |
| 3 | Kamera orbita → powierzchnia (R = 4 km) płynnie, bez dziur, bez przycięć > 50 ms przy strumieniowaniu | `--benchmark-report`: scenariusz `OrbitToSurface` (lot z 2R do 5 m w 60 s, jak `DevCamera_OrbitToSurface_*`): maks. czas klatki głównego wątku ≤ 50 ms i promienie do środka planety z kamery co klatkę trafiają w teren (brak dziur); `Manual_OrbitToSurface_1080p` z grafiką: zrzuty co 5 s i ocena dziur przez właściciela | raport w PR (`ci.yml` `game`, bez blokowania), próg w nocy (`nightly.yml` `bench`); Manual | A |
| 4 | 8 graczy na jednej planecie przez zaproszenie Steam oraz przez serwer dedykowany po IP | IP: `coop_soak.py` z 8 botami przez UDP (smoke 3 min w CI, 2 h w nocy). Steam: `Manual_Steam_EightPlayers` według P3 | gating / `ci.yml` `game` + `nightly.yml` `coop-soak`; Manual | A (IP), D (Steam) |
| 5 | Edycje terenu i bloków widoczne u wszystkich w ≤ 200 ms przy RTT 100 ms | definicja P5: od wysłania intencji przez bota A do odbioru u B (część sieciowa ≤ 150 ms, p99) + zastosowanie w silniku do `ChunkMeshApplied`/podmiany kolizji (≤ 50 ms, p99); test `Coop_Rtt100Jitter10_EditVisibleAtOthers_NetworkPartWithin150ms` (czas wirtualny), boty w silniku w soak (pełna suma, p99 ≤ 200 ms); raport obok liczby X11 z K3-15/K3-16 | `Slow` / `ci.yml` build-and-test; `nightly.yml` `coop-soak` | B, C |
| 6 | Po godzinie testu z botami stan terenu i budowli identyczny u wszystkich (skróty stanu) | soak 2 h: przerwy kontrolne co 15 min (wstrzymanie edycji, opróżnienie, zrzut skrótów serwera i każdego bota); wynik minuty 60 i 120 jest dowodem; test wirtualny `Coop_EightBots_TwoHoursVirtual_DigestsEqualAtEveryQuarter` | `Soak` / `nightly.yml` `soak` i `coop-soak` | B (+C dla wokseli) |
| 7 | Restart zachowuje wszystkie zmiany; zabicie procesu traci najwyżej ostatnią partię (≤ 100 ms, w checkpoincie do jego końca), bez duplikatów (test automatyczny) | testy wirtualne kroku 4; w soak: łagodny restart (`stop`) w 40. min i zabicie procesu (`TerminateProcess`) w 80. min; asercje według „Trwałość” niżej; istniejące `K1GateTests`, `CrashHostSoakTests`, `server_smoke.py` | domyślna / `ci.yml`; `Soak` / `nightly.yml` | A |
| 8 | Na sprzęcie referencyjnym 60 FPS w 1080p, a tick serwera średnio ≤ 8 ms przy 8 graczach | tick: przebieg `coop_soak.py --profile tick` (8 botów .NET, 10 min, serwer z maską 6 rdzeni), średnia ≤ 8 ms z `stats`; FPS: `Manual_Fps1080p_EightPlayers` (klient z grafiką 1080p + 7 botów, trasa kamery, CSV klatek: średnio ≥ 60 FPS i p95 ≤ 16,7 ms) według P2 | `nightly.yml` `bench` (tick, próg); Manual (FPS) | A (tick), D (FPS) |
| 9 | Testy jednostkowe, integracyjne i odporności zapisu przechodzą w CI | istniejące projekty testów + nowe z tego planu; numery runów w raporcie | domyślna, `Slow` / `ci.yml`; `Soak` / `nightly.yml` | E |

Poziomy z tabeli „Testy” specyfikacji, które K4-19 domyka: „Integracyjne: serwer i 2 boty kopią i budują przez 10 min, teren i budowle identyczne u wszystkich, Godot bez grafiki” → scenariusz w silniku `CoopTwoEngineClients_TenMinutes_DigestsEqual` (krok 12); „Wydajność: generacja i meshing, czas ticku przy 8 graczach (BenchmarkDotNet, profiler Godota)” → krok 7; „Wytrzymałościowe: serwer dedykowany i 8 botów przez 2 h, co noc” → `coop-soak`.

Poza kryteriami, raportowane bez blokowania: pasmo na klienta (cel 50 000 B/s, X3), częstość niezgodnych sum kontrolnych terenu (K3-16), liczniki korekt ruchu (`MotionStatistics`, K3-14), liczba odmów według `RejectReason`.

## Projekt

### Zasada nadrzędna: boty idą ścieżką gracza

Bot nie ma tylnych drzwi do serwera. Wysyła wyłącznie to, co wysyła klient gracza: wejście ruchu przez `InputSequencer` (paczki po dwa, powtórzenie trzech ostatnich), intencje narzędzi przez `ToolController`/`SessionIntentSender` z tempem `IntentPacer`, promień zawsze z oka liczonego `AimRayBuilder.EyeFromFeet` ze stóp z ostatniego stanu własnego od serwera (ZASADY pkt 2). Serwer nie wie, że rozmawia z botem. Losowość bota (ziarno) jest w kodzie testowym po stronie klienta, nigdy przy stosowaniu operacji.

### Pliki

| Plik | Co |
| --- | --- |
| `src/Horyzont.Bots/Horyzont.Bots.csproj` (nowy) | biblioteka bez silnika; referencje: Core, Net, Client; po K3-15 także Constructs, po K3-16 Persistence (`ClientTerrainCache`). Komentarze XML jak w Core/Net |
| `src/Horyzont.Bots/Scripting/BotScript.cs`, `BotRole.cs`, `BotCommand.cs`, `BotSchedule.cs` | deterministyczny skrypt: z ziarna, roli i numeru ticku daje polecenie (ruch: yaw, wektor, skok, sprint; narzędzie: kop, zasyp, postaw, usuń, nic); harmonogram przerw kontrolnych i burstów |
| `src/Horyzont.Bots/Client/BotClient.cs`, `BotToolWorldView.cs`, `BotStats.cs`, `IBotReplica.cs`, `NoReplica.cs` | klient bota na `ClientRuntime`: połączenie, ready, wejście, narzędzia, śledzenie odpowiedzi (`ClientSeq` → `AppliedOpId`), ponowne łączenie z odczekaniem 2 s; widok narzędzi: teren jako kula o promieniu `|stopy|` (serwer i tak liczy trafienie), budowle z repliki |
| `src/Horyzont.Bots/Replicas/ConstructReplica.cs` **[K3-15]**, `TerrainReplica.cs` **[K3-16]** | `EntityMirror` + `ConstructMirror` (z ACK migawek według K3-15); protokół terenu klienta bez silnika: `TerrainCacheReport` przed ready, `TerrainView` z pozycji bota, odbiór `TerrainBlockData`/`TerrainSync`, zapis do `ClientTerrainCache` w katalogu tymczasowym bota |
| `src/Horyzont.Bots/Digests/ConstructDigest.cs` **[K3-15]**, `TerrainVersionDigest.cs`, `DigestDump.cs`, `DigestEntry.cs` | skróty XxHash64 po posortowanych wpisach: budowla (id, rama, `ParentSeq`, poza bitowo, wersja, chunki w porządku `ChunkPos`), teren (klucz, wersja); zrzut JSON Lines wpisów do porównania zakresu |
| `src/Horyzont.Bots/Transport/LatencyTransport.cs` | dekorator `ITransport`: stałe opóźnienie w jedną stronę + jitter z ziarna dla obu kanałów, czas z `TimeProvider`; kanał niezawodny zachowuje kolejność; do UDP w biegaczu (RTT 100 ms na loopbacku) |
| `src/Horyzont.Server/Diagnostics/TickTimeRecorder.cs`, `TickTimeWindow.cs` | pomiar `ServerRuntime.Tick()` przez wstrzyknięty `TimeProvider.GetTimestamp`: okno (średnia, p95, p99, max, liczba) na stałym histogramie, zero alokacji w ticku |
| `src/Horyzont.Server/Diagnostics/IStateDigestSource.cs` **[K3-15/16]** | `void WriteDump(TextWriter writer)` + `(ulong Constructs, ulong TerrainVersions, ulong? TerrainVoxels) Compute()`; implementacja w grze |
| `src/Horyzont.Server/ServerRuntime.cs` (mała zmiana) | właściwość `TickTimes` (recorder) mierzy wokół całego `Tick()`; nie zmienia kolejności zamykania ani ticku |
| `src/Horyzont.Server/Console/ConsoleCommandProcessor.cs` | komendy `stats` (`STATS tick_ms mean=… p95=… p99=… max=… n=… players=… last_op=… checkpoint=idle|running`, okno od poprzedniego `stats`) i `digest [--dump <plik>]` (`DIGEST constructs=… terrain_versions=… terrain_voxels=…|-`) |
| `src/Horyzont.Server/ServerLog.cs` | linie `SERVER READY … last op=<OpId>`, `Checkpoint started boundary=<OpId>`, `Checkpoint committed boundary=<OpId>`, `Stopped; … last op=<OpId>`; istniejące wzorce w `server_smoke.py` zostają dopasowane (dopisanie pola na końcu) |
| `game/src/Server/Diagnostics/ServerStateDigestSource.cs` **[K3-15/16]** | `IStateDigestSource` w grze: `ConstructDigest`, wersje z `WorldState`, `TerrainDigest.Compute` z `TerrainBlockCodec.Digest` po zmienionych blokach (główny wątek) |
| `game/src/Client/Bots/BotDriver.cs`, `BotInputSource.cs`, `EngineBotReport.cs` **[K3-14]** | tryb `--bot`: węzeł dziecko klienta, co tick fizyki pobiera `BotCommand` i podaje go przez szew wejścia zamiast `LocalInputReader`; zrzut skrótów (budowle z lustra, woksele `TerrainDigest`), czasy odbiór→`ChunkMeshApplied`, `MotionStatistics` |
| `game/src/Boot/LaunchOptions.cs` | `--bot <seed>`, `--bot-role`, `--bot-out <dir>`, `--bot-epoch <unix ms>`; tylko `ProcessMode.Client` i wymagane `--dev` |
| `game/src/Boot/DevModes.cs`, `game/src/Planet/Diagnostics/BenchmarkReport.cs` | `--benchmark-report <dir>`: generacja (bloki/s na LOD), meshing (ms/blok p50/p95), lot `OrbitToSurface` (czasy klatek, promienie „bez dziur”), budowla 64³ (z K2-12) |
| `tests/Horyzont.Bots.Tests/` (nowy projekt testów) | testy skryptu, skrótów, dekoratora |
| `tests/Horyzont.Integration.Tests/Coop/CoopRig.cs`, `CoopBotTests.cs`, `CoopDurabilityTests.cs`, `CoopReplicationTests.cs` **[K3-15/16]** | uprząż: `ServerRuntime` + `InMemoryNetwork(LinkConditions)` + `FakeServerTerrainFactory` + N × `BotClient`, kroki klatek 16 ms jak `ServerRig` (serwer, potem klienci) |
| `tests/Horyzont.BotRunner/` (nowy, exe) | `--connect host:port --bots N --roles … --seed S --epoch ms --latency-ms 50 --jitter-ms 10 --out dir`; pętla 60 Hz na czasie rzeczywistym, zapisuje `bots.json` (statystyki, czasy edycji, zrzuty w przerwach) |
| `tests/Horyzont.Benchmarks/` (nowy, exe, BenchmarkDotNet), `tests/Horyzont.Benchmarks/budgets.json` | benchmarki i budżety (niżej) |
| `tools/coop/coop_soak.py`, `tools/coop/compare_dumps.py` | orkiestrator procesów i porównanie zrzutów; `--self-test` na syntetycznych logach |
| `tools/modes_smoke.py` | smoke trybów (kryterium 2) |
| `tools/bench/check_budgets.py` | porównanie wyników BenchmarkDotNet i raportu silnika z budżetami; `--self-test` |
| `tests/Horyzont.Architecture.Tests/LayeringTests.cs` | wiersz `src/Horyzont.Bots/Horyzont.Bots.csproj` z dozwolonymi referencjami; Bots w teście „bez Godota”; `Horyzont.Client` nie może zależeć od Bots |
| `Directory.Packages.props` (kontrakt chroniony) | `BenchmarkDotNet` (wersja z Context7/NuGet w dniu kroku 7); opis w PR + zielony test dymny silnika |
| `.github/workflows/ci.yml`, `.github/workflows/nightly.yml` | kroki i joby z sekcji „CI” |
| `docs/reports/K4-19-odbior.md`, `docs/reports/K4-19/*.json` | raport odbioru i migawki wyników |

### Klient bota bez silnika

- `BotClient` posiada `ClientRuntime` (transport wstrzyknięty: `InMemoryTransport` w testach, `UdpTransport` opakowany `LatencyTransport` w biegaczu) i jest słuchaczem sesji (`IClientSessionListener`). Stan własny (stopy, rama, `ParentSeq`) bierze z odpowiedzi serwera; bot nie przewiduje ruchu (predykcję sprawdza K3-14 i bot w silniku).
- Wejście: `InputSequencer` z Client; yaw w konwencji `SurfaceFrame` (kontrakt chroniony, tylko odczyt).
- Narzędzia: `ToolController` z `BotToolWorldView`. Przed K3-15 bot stawia bloki tylko na terenie (`TerrainTarget`); po K3-15 także na ścianach budowli (`ConstructFaceTarget`) i usuwa bloki, celując w lustro (`ConstructRaycast` na `ConstructStore` repliki).
- Gotowość: przed K3-16 `AlwaysReadyWorld`; po K3-16 ready dopiero po wysłaniu raportu cache i widoku, jak klient gry.
- Role (P6): `Builder` (zostaje w strefie wspólnej 100 m, ok. 1 edycja terenu/s i 2 operacje bloków/s, burst do limitu na 10 s co 5 min), `Explorer` (pętla do 1,5 km i z powrotem: wejście/wyjście z zasięgu 500 m, zimny teren), `Abuser` (co 10 s seria intencji poza zasięgiem, za szybkich, pod rdzeniem, NaN w kierunku odrzuconym już u klienta; oczekiwane odmowy, zero zmian stanu). Domyślny skład 8 botów: 5 × Builder, 2 × Explorer, 1 × Abuser.
- Repliki za interfejsem `IBotReplica` (`OnMessage`, `WriteDump`, `Digest`), żeby faza A działała bez kodu K3, a fazy B/C dopinały repliki bez zmiany pętli bota.
- Jeśli K3-16 zostawi logikę protokołu terenu klienta wyłącznie w `game/` (`ClientTerrainReplicator`), `TerrainReplica` implementuje minimalną część (raport, widok, odbiór, cache) i ma test kontraktowy przeciw serwerowemu replikatorowi w `CoopReplicationTests`. Nie przenosić kodu K3-16 bez zgody prowadzącego K3-16 (Opus).

### Skróty stanu i porównanie „u wszystkich”

Klient ma tylko część świata (byty w 500 m, teren w widoku), więc porównanie idzie po wpisach, nie po jednym skrócie całego świata:

1. W przerwie kontrolnej (harmonogram `BotSchedule`, wspólny `--bot-epoch`, co 15 min: 60 s bez intencji, potem 20 s opróżnienia) każdy bot zapisuje zrzut wpisów (budowle, teren: klucz, wersja, skrót treści), a orkiestrator w tej samej chwili wysyła serwerowi `digest --dump`.
2. `compare_dumps.py`: (a) każdy wpis klienta równa się wpisowi serwera (wersja i skrót); (b) pokrycie: każdy wpis serwera w strefie wspólnej (budowle w 100 m od punktu strefy, bloki terenu przecięte strefą) jest u każdego bota roli `Builder` i u botów w silniku; (c) boty .NET nie mają wokseli, więc dla nich teren = (klucz, wersja) plus zgodność skrótu bloba między botami dla tej samej pary; woksele (`TerrainDigest`) porównują serwer i boty w silniku.
3. Wynik zapisuje się w `summary.json` (`checks[].name`, `passed`, liczby wpisów, pierwsza różnica).

### Trwałość (kryterium 7)

- Serwer wypisuje `last op` przy starcie, checkpoincie i zatrzymaniu; orkiestrator czyta jego stdout (sam go uruchomił) i zna znaczniki czasu linii (`TimeProvider.System.GetTimestamp`, na Windows QPC wspólny dla procesów).
- Biegacz zapisuje dla każdej przyjętej intencji `AppliedOpId` i chwilę odbioru `IntentReply` (ten sam zegar).
- Łagodny restart (`stop` → start): `last op` i skróty po starcie = przed zatrzymaniem; boty łączą się ponownie i przerwa kontrolna po 2 min daje zgodne zrzuty.
- Zabicie (`TerminateProcess`, chwila K): po starcie `last op = L`. Asercje: każda operacja z odbiorem ≤ K − 150 ms (100 ms partii + 50 ms zapasu) ma `OpId ≤ L`; jeśli między `Checkpoint started` a K nie było `Checkpoint committed`, granica cofa się do chwili `Checkpoint started` − 150 ms. Brak duplikatów: `OpId` w dzienniku po starcie są ciągłe i unikalne (`stats`), wersje bloków i budowli u botów po resynchronizacji równe serwerowym (żadna edycja nie została zastosowana dwa razy, bo wersja rośnie przy każdym zastosowaniu), drugi start bez ruchu daje ten sam skrót stanu (powtórka deterministyczna).
- Wersja wirtualna w `CoopDurabilityTests` używa `TestFaults`/`ArmedFaults` (awaria zapisu partii) zamiast zabijania procesu.

### Pomiar 200 ms (kryterium 5, P5)

Liczymy surowiej niż X11: od wysłania intencji przez bota A (`TimeProvider.GetTimestamp` w chwili `SubmitIntent`) do odbioru zmiany u bota B. Korelacja: budowle po `OpId` z `OperationApplied`; teren po kluczach i wersji bloków w strefie znacznikowej (A edytuje punkty z harmonogramu znane wszystkim botom) albo po barierze z `OpId`, jeśli K3-16 ją przesyła. Budżet: część sieciowa ≤ 150 ms (p99) w testach .NET (wirtualnie: 50 + 50 ms, czekanie na tick ≤ 16,7 ms, jitter), część silnika ≤ 50 ms (p99, odbiór → `ChunkMeshApplied`/podmiana kolizji, bot w silniku). W soak boty w silniku mierzą sumę bezpośrednio; raport podaje też liczbę według X11 z K3-15/K3-16.

### Benchmarki

| Benchmark (`tests/Horyzont.Benchmarks`) | Co | Budżet (`budgets.json`) |
| --- | --- | --- |
| `ServerTickBench` | `ServerRuntime.Tick()` z `FakeServerTerrain`, 8 klientów w pamięci, wejście co tick, 1 edycja/s/gracza | alokacja 0 B/tick w stanie ustalonym (twardy); czas: raport, część logiczna ticku bez fizyki Godota |
| `OperationApplierBench` | edycja terenu, `BlockSet`/`BlockClear` w chunku | alokacja według K1-03 (twardy), czas raport |
| `CodecBench` | `Snapshot` 8 bytów, `ConstructState` 32 KiB, `TerrainBlockData`, `IntentReply` (kodowanie i dekodowanie) | alokacja kodowania 0 B (twardy, gdzie kod z puli), czas raport |
| `StateHasherBench` | skrót świata po 10 000 operacji | czas raport |
| `ConstructMesherBench` | chunk 16³ losowy/pełny/szachownica (jak `MesherTimingTests`) | czas p95 ≤ wartości z K2-12 (miękki) |
| `JournalBatchBench` | partia 100 operacji do SQLite (WAL, `synchronous=FULL`) w katalogu tymczasowym | czas partii ≤ 50 ms (miękki: połowa okna 100 ms) |
| `TerrainSendQueueBench` **[K3-16]**, `SnapshotPriorityBench` **[K3-14/15]** | wybór zadań/bytów dla 8 odbiorców | alokacja 0 B (twardy) |
| Silnik `--benchmark-report` | generacja bloków/s na LOD, meshing ms/blok, lot `OrbitToSurface`, budowla 64³ | max klatki ≤ 50 ms i 100 % promieni trafia w teren (twardy w nocy), reszta raport |
| `coop_soak.py --profile tick` | 8 botów .NET, 10 min, serwer dedykowany z maską 6 rdzeni (`--affinity 0x3F`) | średni tick ≤ 8 ms (twardy w nocy, kryterium 8); p99 raport |

Zasady: budżety alokacji są twarde wszędzie (deterministyczne). Budżety czasu są twarde tylko tam, gdzie wynikają ze specyfikacji (tick 8 ms, klatka 50 ms) i tylko w nocy, kiedy runner nie dzieli maszyny z lokalnymi testami; regresja czasu > 25 % względem `docs/reports/K4-19/bench-baseline.json` daje ostrzeżenie (`::warning::`), nie błąd (P9). BenchmarkDotNet w trybie `--job short` w nocy, `--job default` w przebiegu odbioru. Wyniki: artefakty nocne (JSON, Markdown, 30 dni) i migawka odbioru w `docs/reports/K4-19/bench-<sha>.md` + `bench-baseline.json`, zatwierdzone w PR.

### CI

| Workflow / job | Zmiana | Limit |
| --- | --- | --- |
| `ci.yml` / `build-and-test` | bez zmian komend; nowe projekty testów wchodzą automatycznie (`Category!=Soak&Category!=Manual`, więc `Slow` też) | 30 min (zostaje; jeśli `Slow` K4-19 przekroczą, podnieść do 40 z opisem) |
| `ci.yml` / `game` | po „Dedicated server smoke”: krok `Modes smoke` (`python tools/modes_smoke.py --godot …`), krok `Co-op smoke` (`python tools/coop/coop_soak.py --godot … --minutes 3 --bots 8 --engine-bots 0 --out coop-smoke`), od fazy C `--engine-bots 1`; upload `coop-smoke/summary.*` (`if: always()`); krok raportu `--benchmark-report` z `continue-on-error: true` | 45 → 60 min |
| `nightly.yml` / `soak` | bez zmian; obejmuje nowe testy `Soak` (wirtualne 2 h) | 120 min |
| `nightly.yml` / `coop-soak` (nowy) | pobranie silnika jak w `game`, build gry, `coop_soak.py --minutes 120 --bots 8 --engine-bots 2` (od fazy C; wcześniej 0), restart w 40., zabicie w 80. min; in-engine `--engine-tests --soak` (scenariusz 10 min, faza C); artefakty `coop-soak/**`; `workflow_dispatch` z wejściem `minutes` | 200 min |
| `nightly.yml` / `bench` (nowy) | `dotnet run -c Release --project tests/Horyzont.Benchmarks -- --filter * --job short --exporters json markdown`, silnik `--benchmark-report`, `coop_soak.py --profile tick`, `check_budgets.py`; podsumowanie do `$GITHUB_STEP_SUMMARY` | 60 min |

Wszystkie nowe joby: `runs-on: [self-hosted, Windows, X64]`, warunek `setup-dotnet`/`setup-python` tylko na hostowanych, `MSBUILDDISABLENODEREUSE: 1` i `DOTNET_CLI_USE_MSBUILD_SERVER: 0` jak w `ci.yml`. Scenariusze nie używają potoku gdUnit (`gdunit4-message-pipe`); in-engine `--soak` używa runnera w silniku z K2-12. Linux: wiersze macierzy zostają zakomentowane z odnośnikiem do decyzji 2026-10-09.

### Decyzje

1. Dwa rodzaje botów: .NET (tanie, 8 naraz, obciążenie serwera identyczne jak od gracza) i w silniku (pełna wierność terenu i meshu, 1–2 naraz). Tick serwera mierzy się z 8 botami .NET, bo boty w silniku konkurują o CPU tej samej maszyny.
2. Biblioteka botów w `src/` (a nie w `tests/`), bo tryb `--bot` gry jej używa; `--bot` wymaga `--dev` i nie zmienia ścieżki gracza.
3. Pomiar ticku wewnątrz `ServerRuntime.Tick()` przez wstrzyknięty `TimeProvider` (nie w `ServerNode`), żeby nie kolidować z K3-18 (`ServerNode.LastTickMilliseconds`); K3-18 scalony później może czytać `Runtime.TickTimes` (reguła X10: integruje pakiet scalany później; jeśli K3-18 jest już scalony, K4-19 podpina jego właściwość do recordera bez zmiany zachowania HUD).
4. Przerwy kontrolne według harmonogramu czasu (wspólna epoka), nie przez IPC z botami: działa jednakowo dla botów .NET i w silniku.
5. Żadnych zmian w `src/Horyzont.Core/`, protokole i formatach; skróty botów nie trafiają do Core (`Hashing/` chroniony).

## Testy akceptacyjne (napisz najpierw)

| Test | Plik | Kategoria | Faza | Co sprawdza |
| --- | --- | --- | --- | --- |
| `Script_SameSeedAndRole_ProducesSameCommandStream` | `tests/Horyzont.Bots.Tests/BotScriptTests.cs` | domyślna | A | 36 000 ticków, dwa przebiegi → identyczny strumień; inna rola → inny |
| `Script_BuilderNormalMode_StaysWithinGameRulesPacing` | jw. | domyślna | A | tempo edycji ≤ `TerrainEditsPerSecond` i limit bloków poza burstami |
| `AimRay_FromServerFeet_StartsAtServerEye` | `tests/Horyzont.Bots.Tests/BotClientTests.cs` | domyślna | A | początek promienia = `AimRayBuilder.EyeFromFeet(stopy z serwera)`, kierunek znormalizowany |
| `LatencyTransport_OneWay50Jitter10_DeliversInOrderWithinBounds` | `tests/Horyzont.Bots.Tests/LatencyTransportTests.cs` | domyślna | A | `FakeTimeProvider`; niezawodny w kolejności, opóźnienie 40–60 ms |
| `Record_Window_ReportsMeanP95P99Max` / `Record_SteadyState_AllocatesZeroBytes` | `tests/Horyzont.Server.Tests/Diagnostics/TickTimeRecorderTests.cs` | domyślna | A | statystyki okna; 10 000 próbek 0 B po rozgrzaniu |
| `Stats_AfterTicks_PrintsTickWindowLastOpAndCheckpointState` | `tests/Horyzont.Server.Tests/ConsoleCommandProcessorTests.cs` | domyślna | A | format linii `STATS …` stały (parsuje go orkiestrator) |
| `Bots_ProjectReferences_AreAllowed` (wiersz) / `Bots_DoesNotReferenceGodot` | `tests/Horyzont.Architecture.Tests/LayeringTests.cs` | domyślna | A | warstwy |
| `Coop_EightBots_OneMinuteVirtual_AllInGameAndEditsAccepted` | `tests/Horyzont.Integration.Tests/Coop/CoopBotTests.cs` | domyślna | A | 8 botów w grze, ≥ 95 % intencji Builderów przyjętych, zero rozłączeń |
| `Coop_AbuserBot_RejectedWithReasonsAndNoStateChange` | jw. | domyślna | A | odmowy z oczekiwanymi `RejectReason`, skrót stanu = przebieg bez Abusera |
| `Coop_JournalBatchLostAtCrash_RecoveredStateLosesOnlyLastBatch_NoDuplicates` | `tests/Horyzont.Integration.Tests/Coop/CoopDurabilityTests.cs` | domyślna | A | wirtualnie: awaria zapisu partii; po wczytaniu brak tylko operacji z ostatniej partii, `OpId` ciągłe, skrót po powtórce = skrót na żywo bez utraconych |
| `Coop_GracefulRestart_BotsReconnect_StateHashAndLastOpUnchanged` | jw. | domyślna | A | `RequestShutdown` → nowy `ServerRuntime` na tym samym pliku |
| `Coop_EightBots_TenMinutesVirtual_ConstructAndTerrainVersionDigestsEqual` | `tests/Horyzont.Integration.Tests/Coop/CoopReplicationTests.cs` | `Slow` | B | 36 000 ticków, przerwy co 2 min, porównanie wpisów i pokrycia |
| `Coop_Rtt100Jitter10_EditVisibleAtOthers_NetworkPartWithin150ms` | jw. | `Slow` | B | `LinkConditions` 50 ms + jitter, 5 % utraty zawodnego; p99 ≤ 150 ms, max raport |
| `Coop_ExplorerLeavesAndReentersInterest_ReplicaMatchesAfterReturn` | jw. | `Slow` | B | wyjście poza 500 m i powrót; lustro budowli zgodne |
| `TerrainReplica_ProtocolContract_MatchesServerReplicator` | jw. | domyślna | B | raport przed ready, widok, odbiór, cache po restarcie bota |
| `Coop_EightBots_TwoHoursVirtual_DigestsEqualAtEveryQuarter` | jw. | `Soak` | B | 432 000 ticków, 8 przerw kontrolnych |
| `CoopTwoEngineClients_OneMinute_VoxelAndConstructDigestsEqual` | `game/tests/Coop/CoopEngineScenarios.cs` (runner w silniku, `--slow`) | in-engine slow (raport w PR) | C | serwer + 2 klienty w silniku sterowane `BotScript`; `TerrainDigest` i `ConstructDigest` równe; czas odbiór→mesh p99 ≤ 50 ms |
| `CoopTwoEngineClients_TenMinutes_DigestsEqual` | jw. (`--soak`) | in-engine soak (noc) | C | poziom „Integracyjne” spec: 2 boty kopią i budują 10 min |
| `Boot_BotWithoutDev_RejectedWithUsage` | testy `LaunchOptions` w `game/tests` (tam, gdzie są dziś) | domyślna (gdUnit) | C | `--bot` tylko z `--connect` i `--dev` |
| `OrbitToSurface_R4000_MaxFrame50ms_RaysAlwaysHitTerrain` | `--benchmark-report` + `check_budgets.py` | noc (`bench`), raport w PR | A | kryterium 3 bez grafiki |
| `coop_soak.py --self-test`, `compare_dumps.py --self-test`, `check_budgets.py --self-test`, `modes_smoke.py --self-test` | `tools/` | krok CI przed użyciem | A/B | parsery logów, granica trwałości (także checkpoint w trakcie), porównanie zrzutów, budżety |
| `Manual_OrbitToSurface_1080p`, `Manual_Fps1080p_EightPlayers`, `Manual_Steam_EightPlayers`, `Manual_ModesSmoke_Linux` | procedury w raporcie odbioru | `Manual` | D | kryteria 2 (Linux), 3 (dziury), 4 (Steam), 8 (FPS) |

## Kroki

Każdy krok: test czerwony → kod → zielone testy → mały commit (Conventional Commits). Wyszukiwanie kodu i uruchamianie testów zlecać subagentowi Haiku (CLAUDE.md projektu).

**Faza A (od razu)**

- [x] 0. Warunki wejścia: `git status`, rebase na `main`; zapisać w „Odstępstwach”, które pakiety K3 są scalone (numery PR) i rzeczywiste nazwy API z tabeli „Kontekst”; utworzyć szkielet `docs/reports/K4-19-odbior.md` z mapą kryteriów (status `PENDING` w każdym wierszu). Commit `docs:`.
- [x] 1. `TickTimeRecorder` + `TickTimeWindow` (testy najpierw, w tym alokacja), `ServerRuntime.TickTimes`, komenda `stats`, linie logu z `last op` i checkpointem; `server_smoke.py` nadal zielony (wzorce dopasowane do dopisanych pól). Commity `test:`, `feat:`.
- [x] 2. Projekt `src/Horyzont.Bots` + `tests/Horyzont.Bots.Tests`, wiersz w `LayeringTests`, test „bez Godota”; `BotScript`, `BotRole`, `BotSchedule` (testy determinizmu i tempa najpierw).
- [x] 3. `BotClient` (poziom K2: `NoReplica`, `AlwaysReadyWorld`, `InputSequencer`, `ToolController` z `BotToolWorldView`, ponowne łączenie), `LatencyTransport`; `CoopRig` i testy `Coop_EightBots_OneMinuteVirtual_*`, `Coop_AbuserBot_*`.
- [x] 4. `CoopDurabilityTests` (dwa testy kroku 4 tabeli). Przed kodem: przegląd scenariusza przez Opus (nagłówek). Błąd trwałości znaleziony tutaj = poprawka według ZASADY pkt 8 i 12, nie osłabienie asercji.
- [x] 5. `tests/Horyzont.BotRunner` (UDP + `LatencyTransport`, pętla 60 Hz z jawnym terminem, `bots.json`); test dymny biegacza: w pamięci przez ten sam kod pętli z `FakeTimeProvider`.
- [x] 6. `tools/coop/coop_soak.py` (start serwera `--server --dev` z losowym wolnym portem, biegacz, oś czasu: przerwy kontrolne, `stop`/start w 40. min, `TerminateProcess` w 80. min, `stats` co minutę, maska rdzeni dla `--profile tick`; `summary.json`/`summary.md`; kod wyjścia ≠ 0 przy niezaliczonym sprawdzeniu) z `--self-test`; `tools/modes_smoke.py` z `--self-test` (solo, host + klient, serwer + klient; linie `CLIENT IN GAME` dodać w logu klienta, jeśli nie istnieją). Lokalnie: `--minutes 3 --bots 8 --engine-bots 0` zielone.
- [x] 7. Benchmarki: wpis `BenchmarkDotNet` w `Directory.Packages.props` (opis w PR + `python tools/engine/build.py smoke`), `tests/Horyzont.Benchmarks` z benchmarkami fazy A, `budgets.json`, `tools/bench/check_budgets.py --self-test`; tryb silnika `--benchmark-report` (generacja, meshing, `OrbitToSurface` z promieniami do środka, budowla 64³); pomiar `MeshBlockSize` 16 vs 32 i decyzja według P7 (zmiana tylko wizualna; złoty skrót generatora bez zmian). Pierwszy `bench-baseline.json` z maszyny pomiarowej.
- [x] 8. CI: `ci.yml` (`game`: modes smoke, co-op smoke 3 min, raport benchmarku; limit 60 min), `nightly.yml` (`coop-soak` z `--engine-bots 0`, `bench`). Ręczne `workflow_dispatch` obu nowych jobów z wejściem `minutes=10`; numery runów do raportu. Commit `ci:`.

**Faza B — po scaleniu K3-15 i K3-16**

- [ ] 9. **[K3-15, K3-16]** Rebase; mapowanie nazw (`ConstructMirror`, `EntityMirror`, `ClientTerrainCache`, komunikaty 21/24/25, budżety X3). `ConstructDigest`, `TerrainVersionDigest`, `DigestDump` (testy kolejności i zgodności najpierw); `IStateDigestSource` + komenda `digest`; `ServerStateDigestSource` w grze (woksele przez `TerrainDigest`, główny wątek).
- [ ] 10. **[K3-15, K3-16]** `ConstructReplica`, `TerrainReplica`, budowanie na ścianach i usuwanie z lustra; testy `CoopReplicationTests` (`Slow`, `Soak`, kontrakt protokołu terenu).
- [ ] 11. **[K3-15, K3-16]** Orkiestrator: przerwy kontrolne ze zrzutami i `compare_dumps.py` (`--self-test`), pomiar opóźnienia edycji w biegaczu (RTT 100 ms przez `LatencyTransport`), benchmarki `TerrainSendQueueBench`/`SnapshotPriorityBench`; co-op smoke w CI porównuje zrzuty.

**Faza C — po scaleniu K3-14 (oraz 15, 16)**

- [ ] 12. **[K3-14]** Tryb `--bot` w grze: szew wejścia (jeśli K3-14/K3-18 nie zostawiły interfejsu źródła wejścia, dodać `IPlayerInputSource` z `LocalInputReader` jako domyślną implementacją, w pliku nowym, bez edycji `LocalPlayer.cs` poza wstrzyknięciem — właściciel `LocalPlayer.cs` to K3-14, X10), `BotDriver`, `EngineBotReport` (woksele, lustro, odbiór→mesh, `MotionStatistics`); scenariusze w silniku (1 min `--slow`, 10 min `--soak` — flaga `--soak` w runnerze silnika z K2-12); co-op smoke `--engine-bots 1`, `coop-soak` `--engine-bots 2`.

**Faza D — po scaleniu K3-17 i K3-18**

- [ ] 13. **[K3-17, K3-18]** Podpięcie `ServerNode.LastTickMilliseconds` do recordera (decyzja 3), pasmo botów z `GetTransportStats` (raport X3); procedury i przebiegi Manual: `Manual_Steam_EightPlayers` (P3), `Manual_Fps1080p_EightPlayers` i `Manual_OrbitToSurface_1080p` (P2), `Manual_ModesSmoke_Linux` (P1). Wyniki i zrzuty do raportu.

**Faza E — odbiór**

- [ ] 14. Trzy kolejne zielone noce (`soak`, `coop-soak`, `bench`) na `main` z całym K3 + gałęzią pakietu; przebieg BenchmarkDotNet `--job default` i migawka `bench-<sha>.md`; uzupełnić raport (każdy wiersz mapy: PASS/FAIL/EXCEPTION z numerem runu albo ścieżką artefaktu); dopisać do `docs/ZASADY.md` regułę „boty idą ścieżką gracza” i nowe komendy z punktu 10; wiersz K4 w `docs/plans/README.md`; pełne komendy akceptacyjne; PR z opisem odstępstw i pytań.

## Odbiór

Komendy (wyniki w PR):

```
dotnet build Horyzont.sln
dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"
dotnet test Horyzont.sln -c Release --filter "Category=Soak"
dotnet format Horyzont.sln --verify-no-changes
python tools/engine/build.py smoke
python -I tools/coop/coop_soak.py --self-test
python -I tools/coop/compare_dumps.py --self-test
python -I tools/bench/check_budgets.py --self-test
python tools/modes_smoke.py --godot <edytor>
python tools/coop/coop_soak.py --godot <edytor> --minutes 3 --bots 8 --engine-bots 1 --out coop-smoke
python tools/coop/coop_soak.py --godot <edytor> --profile tick --minutes 10 --bots 8 --engine-bots 0 --out coop-tick
dotnet run -c Release --project tests/Horyzont.Benchmarks -- --filter * --exporters json markdown
python -I tools/bench/check_budgets.py --bench BenchmarkDotNet.Artifacts/results --engine bench-report --budgets tests/Horyzont.Benchmarks/budgets.json
```

Pakiet jest odebrany, gdy:

- każdy wiersz mapy kryteriów w `docs/reports/K4-19-odbior.md` ma PASS z dowodem albo EXCEPTION z decyzją właściciela (P1–P3) wpisaną z datą;
- trzy kolejne noce `coop-soak` (2 h, 8 botów, restart i zabicie) i `bench` są zielone; minuta 60 i 120 mają zgodne zrzuty u wszystkich;
- średni tick serwera ≤ 8 ms przy 8 botach (`--profile tick`), maks. klatka lotu orbita→powierzchnia ≤ 50 ms bez dziur, p99 widoczności edycji ≤ 200 ms (150 + 50);
- złoty korpus dzienników, `DeterminismTests`, `GeneratorV1_GoldenDigest` i `EditSemantics_GoldenDigest` bez zmian (`git diff main -- tests/Horyzont.Core.Tests/Golden` pusty); `ProtocolVersion` bez zmian.

## Ryzyka

| Ryzyko | Wpływ | Działanie |
| --- | --- | --- |
| K3 scala się z opóźnieniem albo z innym API niż w planach | fazy B–D stoją, mapowanie nazw się rozjeżdża | fazy A niezależne; krok 0 i krok 9/12/13 zaczynają od mapowania; `IBotReplica` izoluje różnice |
| Jedyny runner Windows dzielony z pracą właściciela (lokalne gdUnit, budowanie) | flaki pomiarów czasu, zabite joby gry (potok gdUnit) | twarde progi czasu tylko w nocy; scenariusze bez potoku gdUnit; krok „Wait for a free gdUnit pipe” zostaje; noc to 3+ h pracy PC właściciela (P4) |
| Biegacz i 2–3 procesy Godota na jednej maszynie | zawyżony tick, brak pamięci | tick mierzony w osobnym profilu z botami .NET i maską rdzeni; pamięć procesów w `summary.json`; przy braku RAM `--engine-bots 1` |
| Duplikacja logiki protokołu terenu klienta w `TerrainReplica` | rozjazd z klientem gry | test kontraktowy B; boty w silniku używają prawdziwego klienta; zgłosić prowadzącemu K3-16 wydzielenie wspólnej części |
| Zegar między procesami | błędna granica trwałości/opóźnienia | `TimeProvider.System.GetTimestamp` (QPC, wspólny w Windows), zapas 50 ms; w raporcie oba znaczniki |
| Boty znajdą błąd w Core (determinizm, walidator) | potrzebna zmiana chroniona | test pokazujący błąd, eskalacja do Opus, osobny PR `fix:` z przeglądem; K4-19 czeka, nie omija |
| Kryteria nie do spełnienia bez zasobów (Linux, Steam × 8, sprzęt referencyjny) | odbiór niepełny | P1–P3: domyślne procedury i jawne wyjątki z decyzją właściciela w raporcie; bramka K4 widzi je osobno |
| `Directory.Packages.props` to kontrakt chroniony | wymóg opisu i smoke | jedna zmiana w kroku 7, opis w PR, `build.py smoke` zielony |

## Pytania do właściciela (wariant domyślny obowiązuje bez odpowiedzi)

| # | Pytanie | Domyślnie |
| --- | --- | --- |
| P1 | Linux (kryterium 2) przy wyłączonym CI Linux? | `Manual_ModesSmoke_Linux` w WSL2: szablon Linux budowany `build.py` w WSL, eksport `server-linux`, `server_smoke.py --export` i `coop_soak.py` z botami Windows po UDP do serwera w WSL; bez WSL2 → EXCEPTION z przeniesieniem do K9-65 |
| P2 | Brak sprzętu referencyjnego (6 rdzeni, GTX 1660 Super, 16 GB)? | maszyna właściciela jako sprzęt pomiarowy K4: serwer z maską 6 rdzeni, FPS na jego GPU; specyfikacja maszyny w raporcie; potwierdzenie na sprzęcie referencyjnym w K9-65 |
| P3 | 8 graczy przez zaproszenie Steam bez 8 kont? | Manual: host przez Steam + co najmniej jeden znajomy przez zaproszenie w nakładce, reszta do 8 jako boty UDP do tego samego hosta (jeśli host z K3-17 słucha jednocześnie Steam i UDP); inaczej zaproszenie na 2 kontach + 8 po IP i EXCEPTION do K9-58/K9-65 |
| P4 | Noc 3+ h (soak 2 h + bench + Soak .NET) na PC właściciela od 02:00? | tak; `workflow_dispatch` z `minutes` do krótszych przebiegów |
| P5 | Od kiedy liczyć 200 ms? | od wysłania intencji przez edytującego (surowiej niż X11); budżet 150 ms sieć + 50 ms silnik, p99; X11 raportowany obok |
| P6 | Skład botów w soak? | 8 = 6 .NET (5 Builder, 1 Abuser) + 2 w silniku (Builder, Explorer) od fazy C; przed C 8 .NET (5 Builder, 2 Explorer, 1 Abuser) |
| P7 | `MeshBlockSize` 16 czy 32? | 16, chyba że 32 daje ≥ 10 % lepsze p95 klatki w locie orbitalnym bez wzrostu maks. klatki |
| P8 | Gdzie biblioteka botów? | `src/Horyzont.Bots`, `--bot` tylko z `--dev` |
| P9 | Budżety czasu benchmarków twarde? | nie: twarde tylko alokacje oraz progi ze specyfikacji (tick 8 ms, klatka 50 ms) w nocy; regresja > 25 % = ostrzeżenie |
| P10 | Pasmo 50 KB/s na klienta? | raport (X3), nie bramka K4 |

## Pułapki

- `ServerRig` i `RigClient` są `internal` w `Horyzont.Server.Tests`; `CoopRig` to osobna uprząż w `Horyzont.Integration.Tests` (bez przenoszenia istniejących klas).
- `LinkConditions.LossRate` dotyczy tylko kanału zawodnego; niezawodny w pamięci nie ma retransmisji (X11). `LatencyTransport` nie gubi pakietów.
- Czas rzeczywisty tylko w biegaczu, orkiestratorze, scenariuszach silnika i `Soak`; każdy z jawnym terminem (ZASADY pkt 8). Testy domyślne i `Slow` w .NET tylko na `FakeTimeProvider`.
- Na Windows zabijanie: `TerminateProcess` (`Popen.kill()`), łagodne zatrzymanie: `stop` na stdin albo CTRL_BREAK (jak `server_smoke.py`); nigdy nie zabijać grupy procesów runnera.
- Wolny port wybierać dynamicznie; po zabiciu serwera port UDP bywa zajęty kilka sekund — orkiestrator ponawia start z nowym portem i podaje go biegaczowi.
- Stare procesy MSBuild po przerwanym jobie blokują checkout (`MSBUILDDISABLENODEREUSE`); orkiestrator zabija tylko procesy, które sam uruchomił (PID).
- Bot nie może wysyłać promieni spoza oka ani niepełnych wektorów; jedyny wyjątek to rola `Abuser`, której intencje mają kończyć się odmową, a nie zmianą stanu.
- Porównanie stanu zawsze po opróżnieniu kanałów niezawodnych; zrzut w trakcie edycji daje fałszywe różnice.
- `TerrainDigest.Compute` sortuje i alokuje: tylko w przerwach kontrolnych, nigdy co tick.

## Odstępstwa i decyzje wykonawcy

(wypełnia wykonawca: stan K3 przy każdej fazie, rzeczywiste nazwy API, odpowiedzi właściciela, wyniki pomiarów, różnice od planu)

### Faza A, krok 0 (2026-10-10)

- Baza: `origin/main` = `97e4b83` (K3-00, PR #19). Scalone: K2 (PR #11, #12, #15, #16, #17, #18 bramka K2), K3-00 (#19). Niescalone: K3-14 … K3-18, więc fazy B–E czekają.
- Nazwy API z tabeli „Kontekst” istnieją na `main` bez zmian (`ClientRuntime`, `InputSequencer`, `ToolController`, `ServerRuntime`, `InMemoryNetwork`, `LinkConditions`, `ServerRig`, `K1GateTests`, `server_smoke.py`). Brak BenchmarkDotNet, jak w planie.
- `ServerLog`: istniejące wpisy `checkpoint committed op=` i `Stopped; state hash=` dopasowane do formatu z planu (`Checkpoint committed boundary=`, dopisek `last op=` na końcu); `server_smoke.py` i istniejące testy zaktualizowane.

### Faza A, krok 3 (2026-10-10)

- `BotClient` idzie ścieżką gracza (InputSequencer, ToolController, oko ze stóp serwera). Stan własny bierze ze `Snapshot`; w `CoopRig` snapshoty i ruch ciała daje `CoopCharacters` (uproszczona wersja `ServerCharacters`, ten sam `OwnStateSender` z `game/` dołączony plikiem).
- Odstępstwo (rola Abuser): `AbuseUnderCore` zastąpiono `AbuseBadRadius` (promień 100 m, odmowa `InvalidParameters`). `BelowCore` i `OutsideAtmosphere` są nieosiągalne z oka w zasięgu 8 m, a serwer mierzy zasięg promieniem terenu (`NoHit`). Seria `AbuseTooFast` składa się z prawidłowych kopań: pierwsze `TerrainEditBurst` są przyjęte, reszta `RateLimited` (limiter nie odmawia bez wcześniejszych przyjęć), więc skrót stanu z Abuserem różni się od przebiegu bez niego dokładnie o te przyjęte kopania; test sprawdza księgowość dziennika.
- Odstępstwo (rola Builder, faza A): bez lustra budowli (K3-15) bot nie zna identyfikatora postawionego bloku, więc nie umie go usunąć. Stawia 1 blok na 2 s (nie 2 operacje bloków na s) i 1 edycję terenu na s; wędruje po meandrze w strefie własnego stanowiska (25 m od startu, promień 35 m), żeby nie wchodzić we własne bloki (`NoLineOfSight`, `CellOccupied`). Dopiero usuwanie przez lustro (faza B) domknie plan „2 operacje bloków na s” i długie przebiegi bez nasycenia strefy. `BotScript.At` jest tylko w kolejności rosnących ticków (zatrzaskuje decyzję o powrocie w segmencie).
- Próg testu: 1 minuta, 8 botów: Builderzy ≥ 95 % przyjętych intencji (ziarna 100+i, deterministycznie).


### Faza A, krok 4 (2026-10-10)

- Przegląd scenariusza trwałości przez Opus 5.5 przed kodem (wymóg kroku 4) nie odbył się w tej sesji: scenariusz jest do przeglądu na bramce K4 razem z całą zmianą.
- Awaria zapisu partii (`SimulatedCrashException` w `AfterJournalCommit`) jest dla pisarza cicha (jak śmierć procesu): serwer nie przechodzi w tryb bezpieczny, więc test czeka na `ArmedFaults.Crashed` i dalej stawia klatki, a operacje po awarii nie trafiają na dysk. Asercje: dziennik na dysku ciągły od checkpointu (bez dziur i duplikatów), skrót po powtórce równy skrótowi na żywo przy ostatniej trwałej operacji, nic sprzed uzbrojenia nie zginęło, coś po awarii zginęło.
- Restart łagodny: boty łączą się ponownie (liczba `Reconnects` ≥ 1: podczas zamykania odmowa lub rozłączenie może zdarzyć się dwa razy).

### Faza A, krok 5 (2026-10-10)

- Pętla biegacza jest w bibliotece (`BotCrowd.Pump`, `RunnerOptions`, `BotReport` w `src/Horyzont.Bots/Runner/`); `tests/Horyzont.BotRunner` to tylko linia poleceń, UDP i zegar systemowy. Test dymny pętli: `Crowd_PumpedOnVirtualTime_*` (czas wirtualny, ten sam `Pump`). Hash budowy: `BuildInfo.ComputeBuildHash(--profiles)` (domyślnie `core:planet/1`); commit budowy biegacza musi być commitem serwera.
- `bots.json`: liczniki i tablica odpowiedzianych intencji `[client_seq, kind, sent_at, replied_at, reason, applied_op_id]` z surowymi znacznikami zegara (`frequency` w ticks/s).
