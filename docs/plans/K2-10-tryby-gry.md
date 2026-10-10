# K2-10 · Tryby gry: solo i host, klient, serwer dedykowany z konsolą

| | |
| --- | --- |
| Prowadzi | Sonnet 5.5 (`claude --model sonnet`; `--model opusplan`, gdy krok wymaga doprecyzowania) |
| Testy akceptacyjne | Sonnet 5.5; tabela „Zamykanie i E3–E9” sprawdzona przez Opus 5.5 przed kodem |
| Przegląd | Opus 5.5 w świeżej sesji (zmiany w `src/Horyzont.Core/`: `RejectReason`, `ProtocolVersion`); GPT-6.1 Sol (Codex), wysiłek `medium`, tylko model wątków, kolejność zamykania i E6/E7 |
| Zależy od | K1-07, K2-09 (pośrednio K2-08 z K2-08A, bramka K1 (PR #9: B1–B3), K1-02 etap B) |
| Gałąź | `pakiet/k2-10-tryby` |

**Cel:** jeden kod uruchamia grę solo, hosta, samego klienta i serwer dedykowany bez grafiki (Windows, Linux) z konsolą: każdy tryb serwera wczytuje albo tworzy świat, odtwarza dziennik, zapisuje checkpointy i zamyka się w ustalonej kolejności bez utraty danych, a host ma jedną kopię terenu wspólną dla serwera i lokalnego klienta.

## Kontekst do przeczytania

- `docs/ZASADY.md`, punkty 2 (composition root, serwer autorytatywny), 3 (ścieżki chronione, zakresy kodów), 4, 6, 7, 8, 11
- `docs/spec/01-planeta.md`: Architektura (tabela trybów, rozdział `/Root/Server` i `/Root/Client`), Sieć (Transport, Sesja kroki 1–5, „Wyjście hosta”), Zapis i odtwarzanie (Pliki, Checkpoint, Odtwarzanie przy starcie kroki 1–5, Kopie i migracje), Obsługa błędów (cała tabela), Testy, Kryteria ukończenia (tryby Windows i Linux)
- `docs/plans/K1-05-persistence.md`: `PersistenceWriter` (zdarzenia, `FlushAsync`, `DisposeAsync`, kopie), `WorldLoader`, `WorldCreator`
- `docs/plans/K1-07-sesja-i-protokol.md`: „Haki gry”, „Rozłączenia”, `Shutdown`/`IsShutdownComplete`
- plan K2-09 (`docs/plans/K2-09-adapter-voxel-tools.md`): „Kontrakty dla innych pakietów”, „Wczytanie i powtórka”, „Stałe semantyki edycji” (wersja semantyki ogona, D1), przepływ checkpointu
- plan K2-13: tabela „Kontrakty konsumowane” (wiersze K2-10) — ten plan je wystawia
- przegląd bramki K1 (`AgentTools/logs/results/horyzont-k1-review.md`): E3–E9 i „Lekcje do zakodowania w planach K2” (1–6)

Kod, na którym budujesz (main 884d40d, zweryfikowane):

- `src/Horyzont.Net/Session/IServerGameHooks.cs` — sześć haków; `ServerSession.cs:99` `Poll(Tick)`, `:172` `Kick`, `:211` `Shutdown(DisconnectCode)`, `:86` `IsShutdownComplete`; wyjątki z haków łapie `Fail(peer, ex)` (`:120`, `:358`) i zamyka tylko tego peera
- `ServerSession.cs:297-310` (E8: przyjęcie liczone przed `_shuttingDown`), `:468-470` (E9: `peer.RemoteCode = disconnect.Code`), `:490-494` (E4: kontrola id po `OnPlayerJoining`), `:503` (`CreateHello` bez kontroli `YourPlayer`)
- `src/Horyzont.Net/Session/ClientSession.cs` — `Connect`, `Poll`, `SubmitIntent`, `Disconnect`; `IClientGameHooks` (`OnServerHello`, `OnRejected`, `OnMessage`, `OnDisconnected`)
- `src/Horyzont.Net/Transport/ITransport.cs`, `CompositeTransport.cs`, `InMemory/InMemoryNetwork.cs` (`CreateTransport(name, options, fakeSteamId)`), `Udp/UdpTransport.cs:293-312` (E7: `FinishClosing` bez terminu), `TransportOptions.cs` (`Timeout`, `KeepAlive`)
- `src/Horyzont.Core/Simulation/ServerSimulation.cs` — `Submit`, `PreparePlayerJoin` (`:96`), `ApplyServerOperation` (`:125`, warunki wstępne przed zmianą), `LeaveAllOnlinePlayers` (`:139`), `Replay`, `AdvanceTick`, `UpdatePlayerKinematics`, `IsFaulted`
- `src/Horyzont.Core/World/WorldState.cs` — `CreateNew`, `CaptureCheckpoint`, `CompleteCheckpoint`, `IsCheckpointInFlight` (`:80`)
- `src/Horyzont.Persistence/Writing/PersistenceWriter.cs` — `EnqueueJournal` (`:101`, E3: wynik `Post` ignorowany), `CheckpointAsync`, `FlushAsync`, `BackupAsync`, `ClearFault`, `TryDequeueEvent`, `DisposeAsync`; zdarzenia `JournalCommitted`, `CheckpointCommitted`, `CheckpointFailed`, `WriterFaulted`
- `src/Horyzont.Persistence/WorldLoader.cs` (`Load` → `LoadedWorld(State, JournalTail, LastCheckpointOpId, GameVersion)`, `WorldCorruptedException`, `WorldFromNewerVersionException`), `WorldCreator.Create`, `Backup/BackupService.cs` (powód: ASCII litery, cyfry, `-`)
- `src/Horyzont.Core/BuildInfo.cs` — `ComputeBuildHash(generatorProfileVersion)`
- haki z testu bramki K1: `F:/dev/horyzont-k1-bramka/tests/Horyzont.Integration.Tests/Support/SimulationServerHooks.cs` (wzór; `TickRate = 30` tylko w teście)
- `F:/dev/horyzont-k1-02b/game` (K1-02 etap B): `src/Boot/Boot.cs` (tylko `--smoke`, reszta „to do in K2”), `scenes/Boot.tscn`, `project.godot` (`physics_ticks_per_second=60`), brak `export_presets.cfg`; `engine/engine.json` ma szablony `template-windows-debug`, `template-windows-release`, `template-linux-release`

## Warunki wejścia

Pakiet nie rusza, dopóki na `main` z zielonym CI (lekcja 4) nie ma:

1. Bramka K1 (PR #9: B1–B3) scalona (B1: `TerrainBlockStore.Close()`, testy z pulą SQLite bez równoległości, zakaz `ClearAllPools`; B2; B3) i K1-02 etap B.
2. K2-09 scalony z E1 i E2 zamkniętymi (decyzja K2-09: kopie zapasowe przeszły do K2-09) oraz z kontraktami: `VoxelTerrainService` (`AddPlanet`, `ShutdownAsync`, mapa materiałów w konstruktorze), `ServerTerrainBlocks` (`CloseAsync`, przepływ checkpointu), `TerrainAreaLoader`, `TerrainReplay.ReplayAsync`, `PlanetTerrain`, `TerrainSemanticsMeta` i wynik sprawdzenia wersji semantyki ogona.
3. Zielony `python tools/engine/build.py smoke` i job `game` na `main` (lekcja 5).

Krok 0 wpisuje do „Odstępstw” rzeczywiste nazwy z K2-08/K2-09 (fabryka `VoxelLodTerrain`, rejestry bloków i materiałów, nazwa kolekcji xUnit z B1); ten plan używa nazw z planu K2-09.

## Zakres

- Warunki wejścia E3–E9 z przeglądu bramki K1 (kroki 1–3), każdy z testem.
- Nowy projekt bez Godota `src/Horyzont.Server`: produkcyjne `IServerGameHooks`, pętla serwera, start, checkpointy, tryb bezpieczny, kolejność zamykania, konfiguracja serwera, polecenia konsoli. Testy w `tests/Horyzont.Server.Tests`.
- Nowy projekt bez Godota `src/Horyzont.Client` (ta sama nazwa, której oczekuje K2-13): tylko warstwa sesji klienta (`ClientRuntime`, słuchacze). K2-13 dokłada do niego `Tools/`.
- `RejectReason.ServerReadOnly = 21` (tryb bezpieczny) z podbiciem `ProtocolConstants.ProtocolVersion` z 1 do 2 (decyzja bramki D4; K2-11 podbija potem do 3).
- Inicjalizacja terenu serwera z wczytanego świata przez fabrykę `IServerTerrainFactory` (decyzja bramki D7).
- Tryb bezpieczny bez zapisu przy niezgodnej wersji semantyki terenu w ogonie dziennika (K2-09, D1) i jawna opcja właściciela `--recover-from-checkpoint`.
- Jedna kanoniczna kolejność zamykania (decyzja bramki D8), do której odwołują się K2-09 i K2-11.
- `ITransport.IsDrained` (E6) i termin zamykania UDP (E7).
- `WorldBackups.RestoreLatest` w Persistence (spec: „propozycja przywrócenia ostatniej kopii”) i flaga serwera `--restore-latest-backup`.
- Godot (`game/`): wybór trybu z argumentów, composition root w `game/src/Boot/`, scena `Root` z poddrzewami `/Root/Server` i `/Root/Client`, węzły serwera i klienta, adapter terenu serwera nad K2-09, dostawcy terenu klienta (wspólny u hosta, własny u klienta), adapter logów (plik z rotacją + konsola Godota), obsługa zamknięcia okna i sygnałów, presety eksportu serwera `server-windows` i `server-linux` dopisane do `windows-release`/`linux-release` z K2-08 (te zostają bez zmian, D9), test dymny serwera dedykowanego na Windows i Linux w CI.

## Poza zakresem

- Menu (solo / hostuj / dołącz), lista światów, okno propozycji przywrócenia kopii dla solo i hosta, HUD → K3-18. Tu tryb wybiera się argumentem wiersza poleceń, a wynik startu (`WorldStartResult`) jest gotowy dla UI.
- Lobby i zaproszenia Steam, `SteamTransport` w hoście → K3-17; bilet Steama dla UDP → K9-58. Host w K2 nasłuchuje na UDP (LAN) obok transportu w pamięci.
- Postać, widzowie `VoxelViewer`, kolizje, `UpdatePlayerKinematics` co tick → K2-11 (ten pakiet daje miejsce wpięcia: `IServerTickParticipant`).
- Replikacja bytów, budowli i terenu (`OnPlayerReady` wysyła stan początkowy, cache `cache/<WorldId>/terrain.db`, `OnClientMessage`) → K3-15, K3-16. Tu `OnPlayerReady` i `OnClientMessage` tylko logują; klient zdalny widzi teren z generatora.
- Narzędzia → K2-13. Bany, lista administratorów, RCON → po K2 (wpis w „Pytaniach”).
- Budowle: wpięcie `ConstructWorld`, meshera i kolizji K2-12 w `GameComposition` i w widok klienta hosta → K2-13 krok 0 (decyzja bramki D5). Ten pakiet nie zależy od K2-12; `GameComposition` ma być rozszerzalny o kolejne usługi bez zmiany kolejności startu i zamykania.
- Komunikat ostrzegający graczy o trybie bezpiecznym (nowy `MessageType`) → K3-18; tu gracze dostają `ServerReadOnly` przy każdej odrzuconej intencji.

## Projekt

### Pliki

```
src/Horyzont.Server/                       (nowy; referencje: Core, Persistence, Net; bez Godota)
  Horyzont.Server.csproj
  ProcessMode.cs                           enum ProcessMode { Solo, Host, Client, DedicatedServer }
  ServerConfig.cs                          rekord konfiguracji + ServerConfigLoader (JSON, walidacja jak dane wrogie)
  ServerRuntime.cs                         start, Tick, checkpointy, zdarzenia writera, tryb bezpieczny, stan
  ServerRuntimeState.cs                    enum Created, Starting, Running, SafeMode, ShuttingDown, Stopped, Faulted
  GameServerHooks.cs                       produkcyjne IServerGameHooks nad ServerSimulation
  IServerTerrain.cs                        to, czego ServerRuntime potrzebuje od terenu (Godot: VoxelServerTerrain)
  IServerTerrainFactory.cs                 tworzy IServerTerrain z wczytanego świata (D7)
  IServerTickParticipant.cs                miejsce wpięcia K2-11/K3 w tick serwera
  ShutdownSequence.cs                      kolejność zamykania jako jawne kroki z terminami
  WorldStartResult.cs                      wynik startu: Started / StartedReadOnly(reason) / Corrupted(backups) / FromNewerVersion / ReplayFailed
  ServerBackupReasons.cs                   stałe powody kopii (lekcja 3d)
  Console/ConsoleCommandProcessor.cs       save, stop, players, kick, help
  Console/ConsoleInputPump.cs              wątek czytający stdin → ograniczona kolejka
  ServerLog.cs                             [LoggerMessage]
src/Horyzont.Client/                       (nowy; referencje: Core, Net; bez Godota; K2-13 dokłada Tools/)
  Horyzont.Client.csproj
  Session/ClientRuntime.cs                 IClientGameHooks → słuchacze
  Session/IClientSessionListener.cs
  Session/ClientLog.cs
src/Horyzont.Persistence/Backup/WorldBackups.cs     RestoreLatest, DiscardJournalTail (nowe)
src/Horyzont.Persistence/Writing/PersistenceWriter.cs   E3
src/Horyzont.Persistence/Terrain/ServerTerrainBlocks.cs bez zmian (z K2-09)
src/Horyzont.Net/Session/ServerSession.cs           E4, E8, E9
src/Horyzont.Net/Session/IServerGameHooks.cs        kontrakt E4 w XML
src/Horyzont.Net/Transport/ITransport.cs            IsDrained (E6) + wszystkie implementacje
src/Horyzont.Net/Transport/TransportOptions.cs      CloseTimeout, MaxClosingConnections (E7)
src/Horyzont.Net/Transport/Udp/UdpTransport.cs      E7
src/Horyzont.Core/Intents/RejectReason.cs           ServerReadOnly = 21 (ścieżka chroniona)
src/Horyzont.Core/Protocol/ProtocolConstants.cs     ProtocolVersion 1 → 2 (ścieżka chroniona; reguła D4 niżej)
tests/Horyzont.Server.Tests/                        xUnit v3 mtp-off, <Using Include="Xunit" />
tests/Horyzont.Client.Tests/                        (zakłada ten pakiet; K2-13 dopisuje swoje testy)
tests/Horyzont.Testing/FakeServerTerrain.cs         IServerTerrain i IServerTerrainFactory nad FakeTerrainService
tests/Horyzont.Architecture.Tests/LayeringTests.cs  Horyzont.Server i Horyzont.Client na liście projektów bez Godota
game/Horyzont.Game.csproj                           referencje do Horyzont.Server i Horyzont.Client
game/src/Boot/Boot.cs                               parsowanie argumentów → ProcessMode → GameComposition
game/src/Boot/LaunchOptions.cs                      --solo|--host|--connect <host:port>|--server, --world, --config, --data-dir, --restore-latest-backup, --recover-from-checkpoint, --smoke
game/src/Boot/GameComposition.cs                    composition root: jedyne miejsce, które tworzy usługi
game/src/Boot/QuitCoordinator.cs                    AutoAcceptQuit=false, WM close, PosixSignalRegistration → ShutdownSequence → Quit(kod)
game/src/Boot/LocalProfile.cs                       tożsamość Dev i nazwa gracza w user://profile.json
game/src/Logging/GodotLoggerProvider.cs             ILoggerProvider: plik z rotacją + GD.Print/PushError (jeśli K2-08/09 już go dodały — użyj)
game/src/Server/ServerNode.cs                       /Root/Server: _PhysicsProcess → ServerRuntime.Tick
game/src/Server/VoxelServerTerrain.cs               IServerTerrain nad VoxelTerrainService + ServerTerrainBlocks + TerrainReplay (K2-09)
game/src/Server/VoxelServerTerrainFactory.cs        IServerTerrainFactory: TerrainBlockStore, ServerTerrainBlocks ze state.TerrainVersions.All, VoxelTerrainService z TerrainMaterialMap, AddPlanet
game/src/Server/DedicatedConsole.cs                 ConsoleInputPump → ConsoleCommandProcessor na wątku głównym
game/src/Client/ClientNode.cs                       /Root/Client: _PhysicsProcess → ClientSession.Poll
game/src/Client/IClientTerrainProvider.cs           teren widziany przez klienta
game/src/Client/SharedTerrainProvider.cs            host/solo: PlanetTerrain serwera (bez drugiej kopii)
game/src/Client/OwnTerrainProvider.cs               klient: PlanetTerrain z fabryki K2-08 + TerrainStream(GeneratorOnlyBlockSource)
game/scenes/Root.tscn                               Root z dziećmi Server i Client (puste kontenery)
game/export_presets.cfg                             windows-release, linux-release z K2-08 bez zmian; dopisane server-windows, server-linux (tryb eksportu dedicated server)
game/tests/ (uprząż z K2-08/K2-09)                  testy Godota z tabeli niżej
tools/server_smoke.py                               test dymny serwera dedykowanego (Windows i Linux)
.github/workflows/ci.yml                            krok serwera dedykowanego w jobie game na Windows i Linux
docs/ZASADY.md                                      reguły z sekcji „Model wątków” i „Kolejność zamykania”
```

### Tryby procesu i composition root

```csharp
namespace Horyzont.Server;

public enum ProcessMode : byte { Solo = 1, Host = 2, Client = 3, DedicatedServer = 4 }
```

| Tryb | Argumenty (po `--`) | `/Root/Server` | `/Root/Client` | Transport serwera | Transport klienta | Teren |
| --- | --- | --- | --- | --- | --- | --- |
| `Solo` | `--solo [--world <WorldId>]` | tak | tak | `InMemoryTransport` („server”) | `InMemoryTransport` („local”) | jeden `PlanetTerrain` serwera; klient przez `SharedTerrainProvider` |
| `Host` | `--host [--world <WorldId>] [--port 27015] [--password …]` | tak | tak | `CompositeTransport([InMemory, Udp])` | `InMemoryTransport` („local”) | jak `Solo` |
| `Client` | `--connect <host:port> [--password …]` | nie | tak | — | `UdpTransport` | własny `PlanetTerrain` z `ServerHello.Planets` (generator; dane bloków od K3-16) |
| `DedicatedServer` | `--server --config <server.json> [--data-dir <dir>] [--restore-latest-backup]` | tak | nie | `UdpTransport` | — | jeden `PlanetTerrain` serwera, bez widoku |

- Solo i host to ta sama ścieżka (spec: Architektura); różnica: solo nie nasłuchuje na UDP i przy wyjściu wysyła `HostLeft` do nikogo.
- `GameComposition` (`game/src/Boot/`) to jedyne miejsce tworzące usługi: `TimeProvider.System`, `ILoggerFactory` z `GodotLoggerProvider`, transporty, `ServerRuntime`, `ClientRuntime`, `VoxelServerTerrainFactory`, dostawcę terenu klienta (budowle K2-12 dopina K2-13 krok 0, D5). Zero pól statycznych ze stanem (ZASADY pkt 2). Węzły dostają zależności metodą `Initialize(...)` wywołaną przez `Boot` przed dodaniem do drzewa; węzeł bez `Initialize` rzuca w `_Ready`.
- Ścieżki: solo/host `user://saves/<WorldId>/world.db`, serwer dedykowany `<data-dir>/saves/<WorldId>/world.db` (domyślnie katalog pliku wykonywalnego), kopie w `saves/<WorldId>/backups/` (spec: Pliki). `--world` pominięty → nowy świat (`WorldId(Guid.NewGuid())`, planety z fabryki K2-08, rejestr materiałów K2-08; rejestr bloków K2-12 dopina K2-13 krok 0, do tego czasu pusty); w `DedicatedServer` brak `worldId` w konfiguracji → nowy świat, a jego id serwer dopisuje do logu i do pliku `<data-dir>/saves/last-world.txt` (konfiguracji nie nadpisuje).
- Tożsamość lokalna: `PlatformIdentity(IdentityPlatform.Dev, value)`, `value` losowane raz (`RandomNumberGenerator`) i trzymane z nazwą w `user://profile.json` (`LocalProfile`); ta sama postać w kolejnych sesjach solo. Steam podmienia to w K3-17.
- `ServerOptions.BuildHash = ClientOptions.BuildHash = BuildInfo.ComputeBuildHash(<wersja profilu generatora z K2-08>)`; `ServerHello.TickRate = 60` (spec: fizyka 60 Hz, `project.godot`).
- `GameRules` nie są konfigurowalne: serwer i klient używają `new GameRules()` tego samego builda (K2-13, „Decyzje”; lekcja 1).

### Model wątków

Decyzja (wymuszona przez K2-09: `ITerrainService` tylko na wątku głównym Godota):

| Wątek | Co na nim działa | Czego nie wolno |
| --- | --- | --- |
| główny Godota | `ServerRuntime.Tick` (w `_PhysicsProcess` węzła `/Root/Server`, 60 Hz), `ServerSession.Poll`, wszystkie haki, `ServerSimulation`, `ITerrainService`, odbiór zdarzeń writera, `ClientSession.Poll` i słuchacze klienta (w `_PhysicsProcess` węzła `/Root/Client`), polecenia konsoli, cała `ShutdownSequence` | blokujące czekanie na zadania (`.Wait()`, `.Result`) — kroki asynchroniczne idą przez `await` z kontekstem synchronizacji Godota albo przez sprawdzanie `Task.IsCompleted` co klatkę |
| zapisu `PersistenceWriter` | dziennik, checkpointy, kopie (wątek wewnętrzny writera; kopia według E2 z K2-09) | dotykanie `WorldState` |
| robocze Voxel Tools | `TerrainStream` (K2-09) → `ServerTerrainBlocks.TryLoad` | drzewo sceny, `WorldState` |
| `ConsoleInputPump` | tylko `Console.In.ReadLine()` → `BoundedChannel<string>` (pojemność 64, linia ≤ 256 znaków; nadmiar odrzucany z logiem) | cokolwiek poza kolejką |
| sygnały (`PosixSignalRegistration`) | tylko `QuitCoordinator.RequestStop()` (flaga `Volatile`) | cokolwiek poza flagą |

- U hosta serwer i klient działają na tym samym wątku głównym, w tej samej klatce fizyki: `/Root/Server` ma `ProcessPhysicsPriority = -100`, `/Root/Client` `0`, więc tick serwera zawsze poprzedza `Poll` klienta. To odpowiedź dla K2-13: `GodotToolWorldView` u hosta może czytać `WorldState` serwera bezpośrednio, tylko na wątku głównym i tylko do odczytu (przez `ServerRuntime.State` — nie przez `ClientRuntime`).
- Kolejność w `ServerRuntime.Tick()`: (1) zdarzenia writera (`TryDequeueEvent` do wyczerpania); (2) `session.Poll(state.CurrentTick)` (haki: dołączenia, intencje, wyjścia); (3) kontrola `simulation.IsFaulted`; (4) uczestnicy `IServerTickParticipant` w kolejności rejestracji (K2-11: kinematyka); (5) `simulation.AdvanceTick()`; (6) harmonogram checkpointu i kopii; (7) w trybie `ShuttingDown` zamiast (2)–(6) krok `ShutdownSequence`.
- Do `ZASADY.md` pkt 2 dopisujesz: „Symulacja serwera, sesje i `ITerrainService` działają wyłącznie na wątku głównym Godota; inne wątki (writer, Voxel Tools, konsola, sygnały) komunikują się z nim tylko kolejkami albo flagami.”

### Produkcyjne `IServerGameHooks`

```csharp
namespace Horyzont.Server;

/// <summary>Production session hooks over a ServerSimulation; main thread only.</summary>
public sealed class GameServerHooks : IServerGameHooks
{
    public GameServerHooks(ServerSimulation simulation, Func<bool> acceptsChanges, ILogger<GameServerHooks> logger);
    public PlayerId OnPlayerJoining(PlatformIdentity identity, string displayName);
    public ServerHello CreateHello(PlayerId player);
    public void OnPlayerReady(PlayerId player);                       // K2: log; K3-15/16 wysyłają stan
    public IntentReply OnIntent(PlayerId player, uint clientSeq, Intent intent);
    public void OnClientMessage(PlayerId player, Message message);    // K2: log na poziomie Debug; K3-16
    public void OnPlayerLeft(PlayerId player, DisconnectCode reason);
}
```

- **`OnPlayerJoining` (E5, lekcja 3a):** Prepare i Apply zawsze razem w jednym wywołaniu: `op = PreparePlayerJoin(identity, displayName)`; jeśli `op.Player` jest już online w `State.Players` → `InvalidOperationException` przed jakąkolwiek zmianą; potem `ApplyServerOperation(op)` (warunki wstępne sprawdzane przed zmianą stanu, `ServerSimulation.cs:125`). Rzut = nic nie zastosowano. Nigdy nie trzymaj przygotowanej operacji między wywołaniami (dwa przygotowania dostałyby ten sam `PeekNext()`). Gdy `acceptsChanges()` zwraca `false` (tryb bezpieczny) → `InvalidOperationException("server is read-only")`, sesja zamyka peera (wpis w „Pytaniach” o kod odmowy).
- **`CreateHello`:** `new ServerHello(State.Id, State.CurrentTick, 60, player, entity, State.Planets.All, blockTypes, materials)` z rejestrów stanu; pola, których jeszcze nie ma (profil generatora z K2-08), według scalonego K2-08.
- **`OnIntent`:** `acceptsChanges()` fałsz → `IntentReply(clientSeq, RejectReason.ServerReadOnly, OpId.None)` bez wołania `Submit`. Inaczej `result = simulation.Submit(player, intent)` → `IntentReply(clientSeq, result.Reason, result.Applied?.Id ?? OpId.None)`. To jest hak, którego oczekuje K2-13 (`IServerGameHooks.OnIntent` → `ServerSimulation.Submit`).
- **`OnPlayerLeft` (lekcja 3c):** jeśli gracz online i symulacja nie jest w awarii: `PlayerLeaveOp` z kinematyki stanu, rotacja przez `Quaternion.Normalize` przed budową operacji. Kod `reason` tylko do logu. W trybie bezpiecznym wyjście nadal się stosuje (inaczej po restarcie gracz zostałby online; `LeaveAllOnlinePlayers` przy starcie i tak to naprawia, ale dziennik w pamięci writera i tak czeka na `ClearFault`).
- **Awaria w haku:** `ServerSession` łapie wyjątek haka i zamyka tylko peera (`Fail`). Dlatego `ServerRuntime.Tick` po `Poll` sprawdza `simulation.IsFaulted` i przechodzi w stan `Faulted` (spec: „Wyjątek w ticku symulacji: zatrzymanie symulacji bez checkpointu”): `session.Shutdown(ServerShutdown)` albo `HostLeft`, opróżnienie transportu, `FlushAsync` (wpisy sprzed awarii są poprawne), `DisposeAsync`, zamknięcie terenu — bez `LeaveAllOnlinePlayers` i bez checkpointu; kod wyjścia procesu 70.

### Kolejność startu

`ServerRuntime.StartAsync(CancellationToken)` (wątek główny; `WorldStartResult` zamiast wyjątku dla przewidzianych błędów danych):

1. `worldDir` istnieje? Nie → `WorldCreator.Create(path, WorldState.CreateNew(id, seed, planets, blockKeys, materialKeys), options, time)`.
2. `WorldLoader.Load(path, new WorldLoadOptions { BackupsDirectory, BlockTypeKeys, MaterialKeys }, time, logger)` (spec: Odtwarzanie kroki 1–3; kopia przed migracją robi loader). `WorldCorruptedException` → `WorldStartResult.Corrupted(backups)` z listą kopii (`BackupService.List()`); `WorldFromNewerVersionException` → `FromNewerVersion`. Świat się nie uruchamia.
3. Wersja semantyki terenu (K2-09, D1): `TerrainSemanticsMeta` porównuje zapisany ciąg z bieżącym; niezgodność przy ogonie z `TerrainEditOp` → **tryb bezpieczny bez zapisu** (niżej) zamiast kroków 5–8.
   Potem `terrain = terrainFactory.Create(path, loaded.State)` (D7): fabryka otwiera `TerrainBlockStore(path)`, tworzy `ServerTerrainBlocks(store, state.TerrainVersions.All, options, time, logger)`, `VoxelTerrainService` z `TerrainMaterialMap` (K2-08/K2-09) i woła `AddPlanet` dla każdej planety z `state.Planets` (K2-09 „Kolejność startu”). Teren nigdy nie powstaje przed wczytaniem stanu.
4. `PersistenceWriter(WorldDatabase.Open(path), options, time, faults, logger)`; `ServerSimulation(state, terrain.Service, new JournalSink(writer), new GameRules(), logger)`.
5. `terrain.ReplayAsync(simulation, loaded.JournalTail, ct)` (K2-09 `TerrainReplay`; dziennik z dysku walidowany jak dane wrogie — lekcja 6, robi to K2-09). `ReplayFailedException`/`TimeoutException` → `WorldStartResult.ReplayFailed(opId, message)`, writer i teren zamykane, świat się nie uruchamia (spec: „zatrzymanie z numerem operacji”).
6. `simulation.LeaveAllOnlinePlayers()` (gracze online z chwili awarii).
7. Natychmiastowy checkpoint (spec: krok 5) i czekanie na `CheckpointCommitted` (termin `StartupCheckpointTimeout` = 60 s → `Faulted`); potem `TerrainSemanticsMeta` zapisuje bieżący ciąg semantyki (K2-09) — przed pierwszą przyjętą operacją.
8. `writer.BackupAsync(ServerBackupReasons.Startup)` (spec: kopia przy starcie; wynik `false` tylko log, nie blokuje startu — E1 w K2-09).
9. `session = new ServerSession(transport, serverOptions, hooks, time, logger)`, `transport.Listen(...)`; stan `Running`. Dopiero teraz `ServerNode` zaczyna wołać `Tick`.

U hosta klient łączy się (`ClientSession.Connect(new InMemoryEndpoint("server"), identity, name)`) dopiero po kroku 9.

**Tryb bezpieczny bez zapisu (niezgodna semantyka terenu, D1).** Stan = checkpoint z `loaded.State` bez powtórki ogona. Nie powstaje writer, nie ma `LeaveAllOnlinePlayers`, checkpointu ani kopii startowej (każdy zapis nadałby `OpId` kolidujące z niepowtórzonym ogonem). Krok 9 normalnie (gracze mogą zobaczyć błąd, dołączenia i intencje są odrzucane jak w `SafeMode`), stan `SafeMode`, `WorldStartResult.StartedReadOnly(TerrainSemanticsMismatch)` z komunikatem K2-09 (wersje, liczba operacji, pierwszy `OpId`) w logu i konsoli. Wyjście tylko przez restart: poprzednią wersją gry albo z `--recover-from-checkpoint` — jawna akcja właściciela: `WorldBackups.DiscardJournalTail(worldDir, loaded.LastCheckpointOpId)` najpierw robi kopię `pre-recover`, potem usuwa wpisy dziennika po checkpoincie, a start idzie dalej zwykłą ścieżką. Zamknięcie w tym trybie pomija kroki 3–6 kolejności zamykania.

### Checkpointy, kopie, tryb bezpieczny

- Checkpoint (spec: co 60 s, przy zatrzymaniu, na `save`; K2-09 przepływ): `ServerRuntime` zleca, gdy `!state.IsCheckpointInFlight` i (minęło `CheckpointInterval` od ostatniego zlecenia według `TimeProvider`, albo `terrain.NeedsEarlyCheckpoint`, albo czeka `save`). Zlecenie na granicy ticku (krok 6 w `Tick`, po `AdvanceTick`): `core = state.CaptureCheckpoint(); request = terrain.CreateCheckpointRequest(core); _ = writer.CheckpointAsync(request)`. Wynik tylko przez zdarzenia: `CheckpointCommitted` → `state.CompleteCheckpoint(r.Core); terrain.OnCheckpointCommitted(r)`; `CheckpointFailed` → `state.RequeueAfterFailedCheckpoint(r.Core); terrain.OnCheckpointFailed(r)`. `InvalidOperationException` z `CreateCheckpointRequest` (błąd w kodzie) → `RequeueAfterFailedCheckpoint` i `Faulted`.
- Kopie okresowe co 6 h robi timer writera (`PersistenceOptions.BackupInterval`); `ServerRuntime` woła `BackupAsync` tylko ze stałych `ServerBackupReasons` (`Startup = "startup"`; nowe powody tylko jako kolejne stałe; lekcja 3d).
- `WriterFaulted` → stan `SafeMode` (spec: „Błąd zapisu dziennika lub pełny dysk”): `acceptsChanges` = fałsz (intencje → `ServerReadOnly`, nowe dołączenia odrzucane), log błędu, ostrzeżenie w konsoli serwera dedykowanego, checkpointy wstrzymane. Wyjście z `SafeMode`: polecenie `save` w konsoli woła `writer.ClearFault()`, a następne `JournalCommitted` bez `WriterFaulted` przywraca `Running`. U hosta bez konsoli: `SafeMode` trwa do zamknięcia (UI → K3-18).
- Nowy kod odmowy: `RejectReason.ServerReadOnly = 21` (pierwszy z zakresu K2–K4 dla `RejectReason`, K1-03 tabela zakresów). Ścieżka chroniona `src/Horyzont.Core/Intents/` → kod nie trafia do operacji ani dziennika, tylko do `IntentReply`, więc podbijasz `ProtocolConstants.ProtocolVersion` z 1 do 2 (stary klient dostałby nieznaną wartość enuma). Reguła podbicia (decyzja bramki D4, wspólna dla K2-10 i K2-11): ustaw `ProtocolVersion` na (wartość na `main` w chwili scalenia) + 1; test sprawdza udokumentowaną wartość; rebase na `main` z nowszym podbiciem aktualizuje i stałą, i test, i ten plan. Kolejność K2: K2-10 → 2, K2-11 → 3. Lekcja 1 nie dotyczy (applier bez zmian). Test złoty wartości enumów K1 bez zmian; nowy wiersz dla 21.

### Kolejność zamykania

**To jest jedyna kanoniczna kolejność zamykania (decyzja bramki D8);** K2-09 i K2-11 odwołują się do niej i jej nie powtarzają. Uzgodniona z K2-09 (teren zamyka się po writerze) i z lekcją 3b. `ShutdownSequence` to jawne kroki wykonywane na wątku głównym, po jednym na klatkę, z terminami przez `TimeProvider` (lekcja 6):

| # | Krok | Kończy się, gdy | Termin | Po terminie |
| --- | --- | --- | --- | --- |
| 1 | `simulation` przestaje przyjmować intencje (`acceptsChanges` = fałsz), `session.Shutdown(code)`; `code` = `HostLeft` (solo/host), `ServerShutdown` (dedykowany) | — | — | — |
| 2 | pompuj `session.Poll(tick)` | `session.IsShutdownComplete && transport.IsDrained` (E6) | `DrainTimeout` 5 s | log ostrzeżenia, dalej |
| 3a | `IServerTickParticipant.OnShutdown(runtime)` w kolejności rejestracji (K2-11: `ServerCharacters.DespawnAll()` — ostatnia kinematyka, zdjęcie widzów) | — | — | wyjątek → `Faulted`, dalej od kroku 5 bez checkpointu |
| 3b | `simulation.LeaveAllOnlinePlayers()` (gracze, których `OnPlayerLeft` nie zastosował) | — | — | wyjątek → `Faulted`, dalej od kroku 5 bez checkpointu |
| 4 | checkpoint (jak wyżej; poprzedni w locie najpierw się kończy) | `CheckpointCommitted` albo `CheckpointFailed` | `ShutdownCheckpointTimeout` 60 s | log błędu, dalej (dziennik i tak jest trwały) |
| 5 | `writer.FlushAsync()` | zadanie ukończone | 10 s | log błędu, dalej |
| 6 | `writer.DisposeAsync()` | zadanie ukończone | 10 s | log błędu, dalej (proces i tak kończy wątek zapisu) |
| 7 | `terrain.ShutdownAsync()` = `VoxelTerrainService.ShutdownAsync()` → `ServerTerrainBlocks.CloseAsync()` → tylko gdy zwróciło `true`: `TerrainBlockStore.Close()` | zadanie ukończone | `CloseTimeout` z K2-09 | `CloseAsync` zwróciło `false` (odczyty Voxel Tools wciąż trwają): **bez `Close()`**, log ostrzeżenia, magazyn zamyka koniec procesu (krok 9 i tak kończy proces) |
| 8 | `transport.Dispose()`; u hosta potem klient: `clientSession.Disconnect()`, `Poll`, `Dispose` transportu klienta | — | — | — |
| 9 | `Stopped`; `QuitCoordinator` woła `GetTree().Quit(exitCode)` | — | — | — |

- Kod wyjścia: 0 normalnie, 2 błąd konfiguracji, 3 świat uszkodzony lub z nowszej wersji, 4 powtórka dziennika nie przeszła, 70 `Faulted` albo krok po terminie z błędem.
- Klient lokalny hosta zamyka się po serwerze (krok 8), więc dostaje `HostLeft` przez transport w pamięci jak każdy inny klient.
- Wejścia do sekwencji: polecenie `stop`, zamknięcie okna (`NotificationWMCloseRequest` przy `GetTree().AutoAcceptQuit = false`), `SIGINT`/`SIGTERM`/`SIGQUIT` (`PosixSignalRegistration`, `context.Cancel = true`, tylko flaga), wyjście z gry u hosta. Drugie żądanie podczas zamykania jest ignorowane z logiem; trzecie `SIGINT` kończy proces natychmiast (`Environment.Exit(130)`; dziennik jest trwały co 100 ms).
- Do `ZASADY.md` pkt 7 dopisujesz kolejność zamykania (tabela wyżej, jedna linia) jako regułę dla każdego, kto dokłada usługę z zasobami. Jedna linia: `Shutdown` sesji → `Poll` do opróżnienia → `OnShutdown` uczestników (K2-11 `DespawnAll`) → `LeaveAllOnlinePlayers` → checkpoint → `FlushAsync` → `DisposeAsync` writera → `VoxelTerrainService.ShutdownAsync` → `ServerTerrainBlocks.CloseAsync` → (`true`) `TerrainBlockStore.Close()` → `Dispose` transportu → klient lokalny.

### Serwer dedykowany: konfiguracja i konsola

```csharp
namespace Horyzont.Server;

public sealed record ServerConfig
{
    public Guid? WorldId { get; init; }                 // null → nowy świat
    public string BindAddress { get; init; } = "0.0.0.0";
    public int Port { get; init; } = 27015;             // 1024..65535
    public string Password { get; init; } = "";         // ≤ 64 bajty UTF-8
    public int MaxPlayers { get; init; } = 8;           // 1..8 (spec: do 8 graczy)
    public int CheckpointIntervalSeconds { get; init; } = 60;   // 10..3600
    public int BackupIntervalHours { get; init; } = 6;  // 1..168
    public int BackupsToKeep { get; init; } = 10;       // 1..100
    public string LogDirectory { get; init; } = "logs";
}

public static class ServerConfigLoader
{
    public static bool TryLoad(string path, out ServerConfig config, out string error);   // nigdy nie rzuca dla złych danych
}
```

- JSON przez `System.Text.Json` ze źródłowym generatorem (`JsonSerializerContext`, bez refleksji), `UnmappedMemberHandling = Disallow`, plik ≤ 64 KiB, każda wartość poza zakresem → błąd z nazwą pola; brak pliku → błąd (serwer nie tworzy konfiguracji sam; `--write-default-config <path>` zapisuje przykładowy plik i kończy). Kod wyjścia 2.
- Pola mapują się na `ServerOptions` (`MaxPlayers`, `Password`), `PersistenceOptions` (`BackupInterval`, `BackupsToKeep`), `UdpEndpoint(BindAddress, Port)` i `ServerRuntimeOptions.CheckpointInterval`. Żadne pole nie zmienia semantyki operacji ani `GameRules` (lekcja 1).

```csharp
namespace Horyzont.Server.Console;

public sealed class ConsoleCommandProcessor
{
    public ConsoleCommandProcessor(ServerRuntime runtime, ILogger<ConsoleCommandProcessor> logger);
    public string Execute(string line);    // wątek główny; zwraca tekst odpowiedzi; nieznane polecenie → pomoc
}
```

| Polecenie | Działanie | Odpowiedź |
| --- | --- | --- |
| `save` | checkpoint teraz (albo po bieżącym w locie); w `SafeMode` najpierw `writer.ClearFault()`; bez kopii (kopie tylko przy starcie i co 6 h, spec) | `save queued`, potem w logu `checkpoint committed op=<OpId>` albo błąd |
| `stop` | `ShutdownSequence` z `ServerShutdown` | `stopping` |
| `players` | `PlayersInGame`: `PlayerId`, nazwa, tożsamość (platforma i wartość), RTT w ms | tabela, `0 players` |
| `kick <PlayerId>` | `session.Kick(player, DisconnectCode.Kicked)` | `kicked <id>` albo `unknown player <id>` |
| `help` | lista poleceń | — |

- Argumenty parsowane bez wyjątków (`int.TryParse` z `CultureInfo.InvariantCulture`), linia ≤ 256 znaków, nadmiar kolejki (64) odrzucany z ostrzeżeniem (lekcja 6).
- `ConsoleInputPump`: wątek tła; `ReadLine()` zwraca `null` (EOF, np. systemd bez stdin) → wątek kończy się z logiem, serwer działa dalej (zatrzymanie sygnałem). Godot w trybie `--headless` nie czyta stdin, więc konflikt nie występuje (sprawdź w rozpoznaniu R2).
- Uszkodzona baza: serwer wypisuje listę kopii i polecenie restartu z `--restore-latest-backup`; ta flaga woła `WorldBackups.RestoreLatest(worldDir)`: przenosi `world.db` (+ `-wal`, `-shm`) do `world.db.corrupt-<yyyyMMdd-HHmmss>`, kopiuje najnowszą opublikowaną kopię (`BackupService.List()`, bez plików `.partial`) jako `world.db`, loguje ścieżki; potem zwykły start. Brak kopii → kod 3.

### Klient: `ClientRuntime` i słuchacze

```csharp
namespace Horyzont.Client.Session;

/// <summary>Receives client session events on the main thread, in registration order.</summary>
public interface IClientSessionListener
{
    void OnServerHello(ServerHello hello) { }
    void OnRejected(HandshakeRejectCode code, string detail) { }
    void OnIntentReply(IntentReply reply) { }          // IntentReply nie trafia już do OnMessage
    void OnMessage(Message message) { }                // pozostałe komunikaty S→K
    void OnDisconnected(DisconnectCode code) { }
}

public sealed class ClientRuntime : IClientGameHooks
{
    public ClientRuntime(ITransport transport, ClientOptions options, bool isHostLocal, TimeProvider time, ILoggerFactory loggers);
    public ClientSession Session { get; }
    public bool IsHostLocal { get; }                   // true w Solo/Host: teren współdzielony, bez cache terenu
    public ServerHello? Hello { get; }
    public void AddListener(IClientSessionListener listener);
    public void RemoveListener(IClientSessionListener listener);
    public void Connect(TransportEndpoint server, PlatformIdentity identity, string displayName, string password = "");
    public void Poll();                                // wątek główny, w _PhysicsProcess węzła /Root/Client
}
```

- To jest kontrakt dla K2-13: `ToolNode` rejestruje słuchacza i dostaje `OnIntentReply` (→ `PendingIntentTracker.OnReply`) i `OnDisconnected`; `SessionIntentSender` używa `ClientRuntime.Session.SubmitIntent`. Wyjątek słuchacza to błąd w naszym kodzie: przechodzi przez `Poll` (nie jest łapany), chyba że jest to `ProtocolViolationException`/`InvalidDataException`, które `ClientSession` zamienia na `ProtocolViolation`.
- `OnServerHello`: `ClientRuntime` zapamiętuje `Hello`, powiadamia słuchaczy, a węzeł `/Root/Client` przygotowuje teren przez `IClientTerrainProvider` i woła `Session.SendReady()` (K3-16 dołoży `TerrainCacheReport` przed `SendReady` u zdalnego klienta; u hosta `IsHostLocal` go pomija).

```csharp
namespace Horyzont.Game.Client;

public interface IClientTerrainProvider
{
    void Prepare(ServerHello hello);                                                      // wątek główny
    bool TryGetPlanet(PlanetId planet, [NotNullWhen(true)] out PlanetTerrain? terrain);   // ten sam węzeł u hosta co serwer
}
```

- `SharedTerrainProvider(VoxelTerrainService)`: `Prepare` nic nie tworzy; `TryGetPlanet` deleguje do `VoxelTerrainService.TryGetPlanet`. Host bez drugiej kopii terenu i bez drugich kolizji (spec: „host ich nie dubluje”).
- `OwnTerrainProvider`: `Prepare` tworzy `PlanetTerrain` pod `/Root/Client/Terrain` z fabryki K2-08 dla każdej planety z `hello.Planets` i `TerrainStream` ze źródłem `GeneratorOnlyBlockSource : ITerrainBlockSource` (zawsze `NotEdited`); K3-16 podmienia źródło na cache.
- K2-11 (widzowie, kolizje postaci) i K2-13 (`GodotToolWorldView`) biorą teren wyłącznie z `IClientTerrainProvider`.

### Warunki wejścia E3–E9

| # | Poprawka | Test (napisz najpierw) |
| --- | --- | --- |
| E3 | `PersistenceWriter.EnqueueJournal`: `Post` zwraca `false` i writer nie jest po symulowanej awarii (`_crashed`) → `InvalidOperationException("journal entry lost: writer is closing")`. `ShutdownSequence` gwarantuje kolejność: koniec symulacji → `FlushAsync` → `DisposeAsync`. | `PersistenceWriterTests.EnqueueJournal_AfterChannelCompletedWithoutCrash_Throws`; `ShutdownSequenceTests.Shutdown_NoJournalEnqueueAfterFlush` (writer-szpieg) |
| E4 | Kontrakt w XML `IServerGameHooks.OnPlayerJoining`: „rzut albo zwrot `PlayerId.None` = gra niczego nie zastosowała; zwrócony gracz nie jest online w sesji”. `GameServerHooks` spełnia go przez kontrolę przed `Apply`. `ServerSession.HandleHello`: po `CreateHello` sprawdza `hello.YourPlayer == player`, inaczej `InvalidOperationException` (peer ma już `Player`, więc zamknięcie woła `OnPlayerLeft` i gracz nie zostaje duchem). | `ServerSessionTests.HandleHello_HelloForOtherPlayer_ClosesPeerAndCallsOnPlayerLeft`; `GameServerHooksTests.OnPlayerJoining_IdentityAlreadyOnline_ThrowsWithoutApplying` (skrót stanu bez zmian) |
| E5 | Prepare+Apply w jednym wywołaniu `OnPlayerJoining` (wyżej). | `GameServerHooksTests.TwoNewIdentitiesInOnePoll_GetDistinctPlayers`; `ServerRuntimeTests.Join_ThrowInApply_NothingApplied` |
| E6 | `ITransport.IsDrained { get; }`: `true`, gdy transport nie ma połączeń w trakcie zamykania z niewysłanymi danymi niezawodnymi. InMemory: brak pakietów w drodze od tego transportu; UDP: `_closing.Count == 0`; Steam: `true` (linger Steama, opis w XML); Composite: wszystkie wewnętrzne. `ShutdownSequence` krok 2 pompuje `Poll` do `IsShutdownComplete && IsDrained` albo terminu. | `InMemoryTransportTests.IsDrained_AfterDisconnectWithQueuedReliable_FalseUntilDelivered`; `ShutdownSequenceTests.HostShutdown_RemoteClientSeesHostLeft` (InMemory z opóźnieniem 100 ms, klient dostaje `HostLeft`, nie `Normal`); `UdpTransportTests.HostShutdown_ClientSeesHostLeft` (prawdziwy czas, limit 10 s) |
| E7 | `TransportOptions.CloseTimeout` (domyślnie 5 s) i `MaxClosingConnections` (domyślnie 64). `UdpTransport.FinishClosing`: połączenie dłużej niż `CloseTimeout` w `_closing` → `NetManager.DisconnectPeerForce(peer)` i usunięcie; przekroczenie limitu → wymuszone zamknięcie najstarszego. Czas przez `TimeProvider` przekazany do konstruktora (nowy parametr opcjonalny, domyślnie `TimeProvider.System`). | `UdpTransportTests.Closing_PeerNeverAcks_ForcedAfterCloseTimeout` (klient LiteNetLib bez potwierdzeń; prawdziwy czas, limit 15 s); `UdpTransportTests.Closing_OverLimit_OldestForced` |
| E8 | `ServerSession.OnConnected`: `_shuttingDown` sprawdzane przed przyjęciem; nowe połączenie dostaje ramkę `Disconnect(_shutdownCode)` i trafia do `_refused` (ograniczone `MaxPendingConnections`, jak dziś), nigdy `HandshakeReject(ServerFull)`. | `ServerSessionTests.Connect_DuringShutdownWithFullServer_GetsShutdownCodeNotServerFull` |
| E9 | `case Disconnect`: `peer.RemoteCode = DisconnectCode.Normal` bez względu na kod klienta (log przy kodzie innym niż `Normal`); martwa gałąź `Local` i `RemoteCode` w `OnDisconnected` uproszczone. | `ServerSessionTests.ClientDisconnectWithKickedCode_OnPlayerLeftGetsNormal` (teoria: `Kicked`, `HostLeft`, `ServerShutdown`) |

E1 i E2 są w K2-09 (decyzja K2-09, Pytanie 3 tamtego planu); ten pakiet tylko sprawdza w kroku 0, że są zamknięte.

### Kontrakty wystawiane

| Kontrakt | Plik | Kto używa |
| --- | --- | --- |
| `ProcessMode` | `src/Horyzont.Server/ProcessMode.cs` | K2-11, K2-13, K3-17, K3-18 |
| Composition root `GameComposition` + `Initialize(...)` węzłów; poddrzewa `/Root/Server`, `/Root/Client`; katalogi `game/src/Server/`, `game/src/Client/`, `game/scenes/client/` | `game/src/Boot/GameComposition.cs`, `game/scenes/Root.tscn` | K2-11, K2-13, K3-15…K3-18 |
| Model wątków: wszystko na wątku głównym; serwer przed klientem w klatce fizyki | ZASADY pkt 2, ten plan | K2-11, K2-13 (`GodotToolWorldView` czyta `ServerRuntime.State` u hosta) |
| `ServerRuntime` (`StartAsync`, `Tick`, `State`, `Simulation`, `Session`, `RequestCheckpoint`, `RequestShutdown`, `IsLocalPlayer(PlayerId)`, `RuntimeState`) | `src/Horyzont.Server/ServerRuntime.cs` | K2-11, K3-15, K3-16 (lokalny gracz hosta bez danych terenu), K4-19 |
| `IServerTickParticipant { void OnTick(ServerRuntime runtime); void OnShutdown(ServerRuntime runtime) { } }` (`OnShutdown` w kroku 3a zamykania) | `src/Horyzont.Server/IServerTickParticipant.cs` | K2-11 (kinematyka, `DespawnAll`), K3-14…K3-16 |
| `GameServerHooks` (`OnIntent` → `ServerSimulation.Submit`) | `src/Horyzont.Server/GameServerHooks.cs` | K2-13, K3-15, K3-16 (dopisują `OnPlayerReady`/`OnClientMessage`) |
| `IServerTerrain`, `IServerTerrainFactory` | `src/Horyzont.Server/`; Godot: `game/src/Server/VoxelServerTerrain.cs`, `VoxelServerTerrainFactory.cs`; testy: `tests/Horyzont.Testing/FakeServerTerrain.cs` | K2-13 i K4-19 (testy bez silnika) |
| `ClientRuntime`, `IClientSessionListener` (`OnIntentReply`, `OnDisconnected`) | `src/Horyzont.Client/Session/` | K2-13, K3-14, K3-18 |
| `IClientTerrainProvider`, `SharedTerrainProvider`, `OwnTerrainProvider` | `game/src/Client/` | K2-11, K2-13, K3-16 |
| `ShutdownSequence` i kolejność zamykania | `src/Horyzont.Server/ShutdownSequence.cs`, ZASADY pkt 7 | wszyscy dokładający usługi z zasobami |
| `ServerConfig`, `ConsoleCommandProcessor` | `src/Horyzont.Server/` | K9 (wydanie serwera) |
| `ITransport.IsDrained`, `TransportOptions.CloseTimeout`/`MaxClosingConnections` | `src/Horyzont.Net/Transport/` | K3-17 (`SteamTransport`) |
| `RejectReason.ServerReadOnly = 21` | `src/Horyzont.Core/Intents/RejectReason.cs` | K2-13 (klucz `REJECT_SERVER_READ_ONLY` w `tools.csv`), K3-18 |
| `WorldStartResult` (z `StartedReadOnly`), `WorldBackups.RestoreLatest`, `WorldBackups.DiscardJournalTail` | `src/Horyzont.Server/`, `src/Horyzont.Persistence/Backup/` | K3-18 (propozycja przywrócenia w UI) |
| Composition root otwarty na budowle (`GameComposition`) | `game/src/Boot/GameComposition.cs` | K2-13 krok 0 (wpięcie `ConstructWorld` K2-12, D5) |

```csharp
namespace Horyzont.Server;

public interface IServerTerrain
{
    ITerrainService Service { get; }                                   // wątek główny
    bool NeedsEarlyCheckpoint { get; }
    CheckpointRequest CreateCheckpointRequest(WorldCheckpoint core);
    void OnCheckpointCommitted(CheckpointRequest request);
    void OnCheckpointFailed(CheckpointRequest request);
    Task ReplayAsync(ServerSimulation simulation, IReadOnlyList<JournalEntry> tail, CancellationToken ct);
    /// VoxelTerrainService.ShutdownAsync → ServerTerrainBlocks.CloseAsync → TerrainBlockStore.Close() only when CloseAsync returned true.
    /// Returns false when the store was left open because reads were still running (D8).
    Task<bool> ShutdownAsync();
}

/// <summary>Creates the server terrain from the loaded world (D7); called once by StartAsync, after WorldLoader.Load.</summary>
public interface IServerTerrainFactory
{
    /// Opens TerrainBlockStore(worldPath), creates ServerTerrainBlocks from state.TerrainVersions.All,
    /// the terrain service and one planet per state.Planets entry.
    IServerTerrain Create(string worldPath, WorldState state);
}

public sealed class ServerRuntime
{
    public ServerRuntime(ProcessMode mode, ServerRuntimeOptions options, ITransport transport, IServerTerrainFactory terrainFactory,
                         Func<WorldState> createNewWorld,   // wołane tylko, gdy pliku świata nie ma
                         TimeProvider time, ILoggerFactory loggers);
    public ServerRuntimeState RuntimeState { get; }
    public WorldState State { get; }                 // po StartAsync; wątek główny, tylko do odczytu poza hakami
    public ServerSimulation Simulation { get; }
    public ServerSession Session { get; }
    public Task<WorldStartResult> StartAsync(CancellationToken ct);
    public void AddTickParticipant(IServerTickParticipant participant);
    public void Tick();
    public void RequestCheckpoint();                 // save
    public void RequestShutdown();                   // stop / zamknięcie okna / sygnał
    public bool IsLocalPlayer(PlayerId player);      // gracz z połączenia InMemory hosta
    public int ExitCode { get; }
}
```

`ServerRuntimeOptions`: `WorldPath`, `BackupsDirectory`, `ServerOptions`, `PersistenceOptions`, `Listen` (`TransportEndpoint`), `CheckpointInterval`, `DrainTimeout`, `StartupCheckpointTimeout`, `ShutdownCheckpointTimeout`, `FlushTimeout`, `DisposeTimeout`, `BlockTypeKeys`, `MaterialKeys`.

### Kontrakty konsumowane

| Co | Od | Plik |
| --- | --- | --- |
| `IServerGameHooks`, `ServerSession`, `ClientSession`, `IClientGameHooks`, `ServerOptions`, `ClientOptions` | K1-07 | `src/Horyzont.Net/Session/` |
| `ITransport`, `InMemoryNetwork`/`InMemoryTransport`, `UdpTransport`, `CompositeTransport`, `InMemoryEndpoint`, `UdpEndpoint` | K1-06/K1-07 | `src/Horyzont.Net/Transport/` |
| `PersistenceWriter`, `JournalSink`, `WorldLoader`, `WorldCreator`, `WorldDatabase`, `BackupService`, zdarzenia writera | K1-05 (+E1/E2 z K2-09) | `src/Horyzont.Persistence/` |
| `TerrainBlockStore.Close()`, kolekcja xUnit bez równoległości | bramka K1 (PR #9: B1–B3) | `src/Horyzont.Persistence/Terrain/` |
| `ServerSimulation` (`Submit`, `PreparePlayerJoin`, `ApplyServerOperation`, `LeaveAllOnlinePlayers`, `AdvanceTick`, `IsFaulted`), `WorldState` (`CreateNew`, `CaptureCheckpoint`, `CompleteCheckpoint`, `RequeueAfterFailedCheckpoint`, `IsCheckpointInFlight`), `BuildInfo` | K1-03/K1-07 | `src/Horyzont.Core/` |
| `VoxelTerrainService` (konstruktor z `TerrainMaterialMap`, `AddPlanet`, `TryGetPlanet`, `ShutdownAsync`), `TerrainSemanticsMeta`, `PlanetTerrain`, `ServerTerrainBlocks` (`CreateCheckpointRequest`, `OnCheckpointCommitted/Failed`, `NeedsEarlyCheckpoint`, `CloseAsync`), `TerrainAreaLoader`, `TerrainReplay.ReplayAsync`, `ITerrainBlockSource`, `TerrainStream` | K2-09 | `game/src/Terrain/`, `src/Horyzont.Persistence/Terrain/` |
| fabryka `VoxelLodTerrain` dla `PlanetParams`, parametry planety startowej, wersja profilu generatora, rejestry bloków i materiałów, uprząż testów silnika | K2-08 (nazwy robocze; obowiązują ze scalonego K2-08) | `game/src/…` |
| `FakeTerrainService`, `WorldFactory`, `FakeTimeProvider` | K1-03 | `tests/Horyzont.Testing/` |

## Testy akceptacyjne (napisz najpierw)

Czysty .NET (zwykłe CI; `FakeTimeProvider`, `InMemoryNetwork`, `FakeServerTerrain` nad `FakeTerrainService`, SQLite w katalogu tymczasowym; testy z SQLite w kolekcji bez równoległości z B1, bez `ClearAllPools` — lekcja 4). Tabela E3–E9 jest wyżej i też należy do akceptacji.

| Test | Oczekiwanie |
| --- | --- |
| `ServerRuntimeTests.Start_NewWorld_CreatesDatabaseCheckpointAndStartupBackup` | brak pliku → świat utworzony, jeden checkpoint, kopia `*-startup.db`, stan `Running` |
| `ServerRuntimeTests.Restart_AfterEdits_SameStateHash` | solo przez sesję: dołączenie, 50 intencji, `RequestShutdown` do `Stopped`; nowy `ServerRuntime` na tym samym pliku → `StateHasher` równy (lekcja 2: live = checkpoint + ogon) |
| `ServerRuntimeTests.Restart_AfterCrashWithoutShutdown_ReplaysTailAndLeavesPlayers` | proces „pada” po 30 intencjach (writer zamknięty bez checkpointu, `IPersistenceFaults`); restart: ogon odtworzony, gracze offline, skrót = skrót stanu na żywo po wyjściu wszystkich |
| `ServerRuntimeTests.Start_CorruptedDatabase_ReturnsCorruptedWithBackups` | zepsuty plik → `WorldStartResult.Corrupted` z listą kopii; nic nie nasłuchuje |
| `ServerRuntimeTests.Start_JournalOpIdGap_ReturnsCorrupted` | dziennik z dziurą w `OpId` → `WorldLoader.ReadJournal` rzuca `WorldCorruptedException` → `WorldStartResult.Corrupted` z listą kopii; nic nie nasłuchuje (D9) |
| `ServerRuntimeTests.Start_ReplayFails_ReturnsReplayFailedWithOpId` | ciągły dziennik z wpisem łamiącym warunek wstępny operacji (np. `PlayerLeaveOp` gracza offline) → `ReplayFailed` z numerem tej operacji; writer i teren zamknięte (D9) |
| `ServerRuntimeTests.Start_TerrainSemanticsMismatch_StartsReadOnlyWithoutWrites` | `FakeServerTerrain` zgłasza niezgodną semantykę przy ogonie z edycją → `StartedReadOnly`, brak powtórki, brak nowych wpisów dziennika i checkpointu, intencje → `ServerReadOnly`; restart z `--recover-from-checkpoint` → kopia `pre-recover`, ogon porzucony, `Running` |
| `ServerRuntimeTests.Start_TerrainCreatedFromLoadedState` | fabryka dostaje `WorldState` po `WorldLoader.Load` z `TerrainVersions` z pliku (D7); nie jest wołana przed wczytaniem |
| `ServerRuntimeTests.Tick_Every60s_CheckpointsOnTickBoundary` | `FakeTimeProvider` +60 s → dokładnie jeden checkpoint; drugi nie startuje, gdy pierwszy w locie |
| `ServerRuntimeTests.Tick_NeedsEarlyCheckpoint_CheckpointsBeforeInterval` | `FakeServerTerrain.NeedsEarlyCheckpoint = true` → checkpoint w najbliższym ticku |
| `ServerRuntimeTests.CheckpointFailed_RequeuesAndRetries` | `CheckpointFailed` → `RequeueAfterFailedCheckpoint`; następny checkpoint zapisuje te same bloki |
| `ServerRuntimeTests.WriterFaulted_EntersSafeModeAndRejectsIntents` | `WriterFaulted` → `SafeMode`; intencja → `IntentReply(ServerReadOnly)`; skrót stanu bez zmian; `save` → `ClearFault` → `Running` |
| `ServerRuntimeTests.SimulationFaultInHook_StopsWithoutCheckpoint` | `FakeTerrainService` rzuca w `ApplyEdit` → `Faulted`, brak nowego checkpointu, `ExitCode = 70`, dziennik sprzed awarii trwały |
| `GameServerHooksTests.OnIntent_SubmitsAndRepliesWithClientSeq` | `IntentReply(clientSeq, None, appliedOpId)`; odrzucenie z kodem walidatora |
| `GameServerHooksTests.OnPlayerLeft_DenormalizedRotation_SerializesLeaveOp` | rotacja z błędem normy 5e-3 → `PlayerLeaveOp` przechodzi przez `OperationCodec` bez wyjątku (lekcja 3c) |
| `GameServerHooksTests.LeaveAllOnlinePlayers_RotationAtPoseTolerance_Serializes` | rotacja na granicy tolerancji `OperationApplier.IsPose` → zapis bez wyjątku; jeśli pada, normalizacja w `UpdatePlayerKinematics` (zmiana w Core, przegląd Opus, wpis w „Odstępstwach”) |
| `ShutdownSequenceTests.Order_IsSessionDrainLeaveCheckpointFlushDisposeTerrainTransport` | szpiedzy rejestrują kolejność kroków 1–8 (z 3a `OnShutdown` przed 3b `LeaveAllOnlinePlayers`) dokładnie jak w tabeli |
| `ShutdownSequenceTests.TerrainCloseFalse_StoreNotClosed` | `CloseAsync` zwraca `false` → `TerrainBlockStore.Close()` nie wołane, ostrzeżenie w logu, sekwencja idzie do kroku 8 (D8) |
| `ShutdownSequenceTests.StepTimeouts_AdvanceWithLog` | wiszący `FlushAsync`/`CloseAsync` → po terminie kolejny krok, kod 70 tylko dla błędów zapisu |
| `ShutdownSequenceTests.SecondRequest_Ignored` | drugie `RequestShutdown` w trakcie nie restartuje sekwencji |
| `HostTests.LocalClientAndRemoteClient_BothReachInGame_LocalIsLocalPlayer` | `CompositeTransport([InMemory, InMemory-jako-zdalny])`; `IsLocalPlayer` prawda tylko dla lokalnego |
| `HostTests.HostLeaves_RemoteClientGetsHostLeft_WorldSaved` | wyjście hosta → zdalny `OnDisconnected(HostLeft)`, plik ma checkpoint z graczami offline |
| `ServerConfigLoaderTests.TryLoad_InvalidValues_ReturnsFieldError` (teoria) | nieznane pole, port 80, `MaxPlayers` 9, plik 65 KiB, zły JSON → `false` z nazwą pola, bez wyjątku |
| `ServerConfigLoaderTests.TryLoad_Defaults_MatchSpec` | 8 graczy, checkpoint 60 s, kopie co 6 h, 10 kopii |
| `ConsoleCommandProcessorTests.Commands` (teoria) | `save`, `stop`, `players`, `kick 2`, `kick x`, `kick 999`, `help`, pusta linia, 300 znaków → oczekiwane odpowiedzi i skutki |
| `ConsoleInputPumpTests.Eof_EndsPumpServerKeepsRunning` / `.Overflow_DropsWithWarning` | `StringReader` jako stdin |
| `WorldBackupsTests.RestoreLatest_MovesCorruptAndCopiesNewestPublished` | `.partial` pominięty; uszkodzony plik przeniesiony z `-wal`/`-shm`; wczytanie po przywróceniu działa |
| `ClientRuntimeTests.Listeners_GetReplyAndDisconnectInOrder` | `IntentReply` tylko w `OnIntentReply`, nie w `OnMessage`; kolejność rejestracji |
| `ProtocolTests.RejectReason_ServerReadOnly_RoundTripsAndProtocolVersionIsTwo` | kodek `IntentReply` z 21; `ProtocolVersion == 2` (wartość udokumentowana w tym planie; reguła D4 przy rebase); wartości K1 bez zmian |
| `LayeringTests` | `Horyzont.Server` i `Horyzont.Client` bez referencji do Godota |
| Wiersz lekcji 2 | ten pakiet nie dodaje pól stanu zapisywanych w checkpoincie ani w dzienniku; równość live = replay = checkpoint + ogon sprawdzają `Restart_AfterEdits_SameStateHash` i `Restart_AfterCrashWithoutShutdown_…` |

Godot bez grafiki (uprząż K2-08/K2-09, job `game`, Windows i Linux):

| Test | Oczekiwanie |
| --- | --- |
| `HostModeTests.Host_HasSingleTerrainInstance` | w trybie `Host` liczba węzłów `VoxelLodTerrain` w drzewie = liczba planet; `SharedTerrainProvider.TryGetPlanet` zwraca ten sam obiekt co `VoxelTerrainService.TryGetPlanet`; brak `/Root/Client/Terrain` |
| `SoloModeTests.Solo_EditSaveRestart_SameTerrainDigest` | edycja terenu przez intencję lokalnego klienta, `stop`, ponowny start z tym samym `WorldId` → `TerrainDigest` (K2-09) i `StateHasher` równe |
| `ClientModeTests.Client_ConnectsToDedicatedInProcess_ReachesInGameWithOwnTerrain` | serwer w tym samym procesie na UDP `127.0.0.1` + klient `--connect`: `InGame`, własny `PlanetTerrain` pod `/Root/Client/Terrain` |
| `ModeTests.Threading_AllSimulationCallsOnMainThread` | strażnik w `VoxelTerrainService` (K2-09) nie rzuca przez 300 ticków z edycjami |

Test dymny serwera dedykowanego (`tools/server_smoke.py`, krok w jobie `game` na Windows i Linux; prawdziwy czas, limit 180 s):

1. `--write-default-config` → plik; start `--headless -- --server --config … --data-dir <tmp>`; czeka na linię `SERVER READY world=<id>`.
2. Polecenia na stdin: `players` (`0 players`), `save` (czeka na `checkpoint committed`), `stop`; kod wyjścia 0; `world.db` i `backups/*-startup.db` istnieją.
3. Drugi start z `worldId` z `last-world.txt` → `SERVER READY` z tym samym `WorldId` i `state hash=<…>` równym hashowi z pierwszego zamknięcia (serwer wypisuje go przy `Stopped`).
4. Trzeci start po `SIGTERM` (Linux) / zamknięciu konsoli (`CTRL_CLOSE`, Windows) w trakcie pracy → kod 0 i checkpoint.
5. Uszkodzony plik → kod 3 i lista kopii; `--restore-latest-backup` → `SERVER READY`.

Na Linuksie uruchamia wyeksportowany build z presetu `server-linux` (`template-linux-release`); na Windows edytor `--headless` albo eksport z `template-windows-release` (oba dozwolone; wybór w „Odstępstwach”).

## Rozpoznanie (przed kodem Godota, wyniki w „Odstępstwach”)

- R1. Voxel Tools na serwerze bez grafiki (`--headless`, eksport w trybie dedicated server): czy `VoxelLodTerrain` generuje kolizje i czy `is_area_editable`/edycje działają bez `RenderingServer`. Serwer liczy fizykę graczy (spec), więc kolizje są potrzebne; jeśli tryb dedicated server wycina zasoby lub mesher, preset eksportuje zasoby terenu jako „keep”.
- R2. Godot 4.7.2 `--headless`: czy stdin jest wolny dla `Console.In` na Windows i Linux; czy Godot sam obsługuje `SIGTERM` (jeśli tak, `PosixSignalRegistration` ma pierwszeństwo i ustawia `Cancel = true`).
- R3. Kontekst synchronizacji Godota dla `await` w `_PhysicsProcess` (kontynuacje na wątku głównym) — od tego zależy, czy `ShutdownSequence` używa `await`, czy sprawdzania `IsCompleted` co klatkę (domyślnie: sprawdzanie co klatkę, bez zależności od kontekstu).
- R4. Szablon `template-linux-release` z K1-01 uruchamia `--headless` na runnerze CI bez serwera X.

## Kroki

- [x] 0. Warunki wejścia sprawdzone; rzeczywiste nazwy z K2-08/K2-09 i nazwa kolekcji B1 wpisane do „Odstępstw”; E1/E2 zamknięte w K2-09.
- [x] 1. E3 (writer) i E9, E8, E4 (sesja): testy z tabeli E3–E9 czerwone → poprawki → zielone.
- [x] 2. E6 (`ITransport.IsDrained` we wszystkich implementacjach) i E7 (termin i limit zamykania UDP): testy → kod → zielone.
- [x] 3. Projekty `Horyzont.Server`, `Horyzont.Client`, `Horyzont.Server.Tests`, `Horyzont.Client.Tests` w `Horyzont.sln`; `LayeringTests`; `FakeServerTerrain`.
- [x] 4. `RejectReason.ServerReadOnly`, `ProtocolVersion` 1 → 2 według reguły D4, test kodeka (przegląd Opus 5.5 w świeżej sesji).
- [x] 5. `GameServerHooks` (E5, lekcja 3c, tryb bezpieczny) z testami.
- [x] 6. `IServerTerrainFactory`, `ServerRuntime`: start (teren z wczytanego stanu, wersja semantyki, tryb bezpieczny bez zapisu, `DiscardJournalTail`), `Tick`, checkpointy, zdarzenia writera, `SafeMode`, `Faulted`; testy restartu i awarii (lekcja 2).
- [x] 7. `ShutdownSequence` z terminami i krokiem 3a (`OnShutdown`); `CloseAsync() == false` → bez `Close()`; testy kolejności, terminów i hosta (`HostLeft`).
- [x] 8. `ServerConfig`/`ServerConfigLoader`, `ConsoleCommandProcessor`, `ConsoleInputPump`, `WorldBackups.RestoreLatest`; testy.
- [x] 9. `ClientRuntime`, `IClientSessionListener`; testy.
- [x] 10. Rozpoznanie R1–R4; wyniki w „Odstępstwach”.
- [x] 11. Godot: `LaunchOptions`, `GameComposition`, `Root.tscn`, `ServerNode`, `ClientNode`, `VoxelServerTerrain`, `VoxelServerTerrainFactory`, `SharedTerrainProvider`, `OwnTerrainProvider`, `GeneratorOnlyBlockSource`, `LocalProfile`, `GodotLoggerProvider`, `QuitCoordinator`, `DedicatedConsole`; `Boot.cs` zachowuje `--smoke`.
- [x] 12. Testy Godota z tabeli (host jedna kopia terenu, solo zapis/odczyt, klient, wątki).
- [x] 13. `export_presets.cfg` (dopisane `server-windows`, `server-linux`; `windows-release`/`linux-release` z K2-08 bez zmian), `tools/server_smoke.py`, krok w jobie `game` (Windows i Linux).
- [x] 14. `ZASADY.md`: reguła wątków (pkt 2), kolejności zamykania (pkt 7), composition root i `QuitCoordinator` (część Godot).
- [ ] 15. (lokalnie wykonane i zielone, zob. „Odstępstwa”; zostaje zielone CI na gałęzi i po scaleniu) Pełne `dotnet build Horyzont.sln`, `dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"`, `dotnet format Horyzont.sln --verify-no-changes`, `python tools/engine/build.py smoke`; zielone CI na gałęzi i po scaleniu na `main` (lekcja 4). Każdy nieodhaczony krok ma wpis „odroczone do X” (lekcja 5).

## Odbiór

- Solo, host i serwer dedykowany (Windows i Linux) tworzą, wczytują i zapisują świat; restart daje ten sam skrót stanu i terenu (testy `Restart_*`, `Solo_EditSaveRestart_SameTerrainDigest`, `server_smoke.py` na obu systemach w CI).
- Host ma jedną instancję terenu (`Host_HasSingleTerrainInstance`).
- Klient łączy się po UDP i dochodzi do `InGame`.
- E3–E9 zamknięte testami albo z wpisem „odroczone do X”.
- Polecenia `save`, `stop`, `players`, `kick` działają w teście dymnym.
- Zielone CI na `main` po scaleniu; w PR wynik `dotnet build`, `dotnet test` i testu dymnego serwera.

## Odstępstwa i decyzje wykonawcy


### Wykonanie bez Godota (kroki 1–9, 14 częściowo)

Nazwy rzeczywiste z K2-08/K2-09: kolekcja B1 to `SqlitePools` (`tests/Horyzont.Persistence.Tests/Support/SqlitePools.cs`); `TerrainSemanticsMeta` (`Format`, `Read`, `Write`, `CheckTail` -> `TerrainTailCheck`); `ServerTerrainBlocks` (`CreateCheckpointRequest`, `OnCheckpointCommitted/Failed`, `NeedsEarlyCheckpoint`, `CloseAsync`). `TerrainReplay` i `VoxelTerrainService` jeszcze nie istnieją (baza to K2-09 etap 1; K2-08 nie jest na `main`), więc `IServerTerrainFactory` ukrywa teren za `IServerTerrain`.

Odstępstwa:
- `IServerTerrainFactory` ma dodatkowo `SemanticsVersion` i `VoxelRef`, a `Create(worldPath, state)`.
- `WorldStartResult` ma dodatkowy wariant `StartFailed(Message)`; kody wyjścia 0/3/4/70.
- `ServerRuntimeOptions.Listen` to lista `TransportEndpoint`; dodatkowe pola `LocalClientEndpoint`, `TerrainShutdownTimeout`, `RecoverFromCheckpoint`, `Rules`, `Faults`, `JournalSinkDecorator`.
- `IsStopped` oznacza zakończoną sekwencję zamykania; `Faulted` pozostaje `Faulted`.
- `WorldBackups.RestoreLatest` zwraca `null`, gdy brak kopii.
- `ServerConfig` ma settery (nie `init`): deserializator ze źródeł zeruje brakujące składowe `init`.
- Dodane `ServerSession.GetEndpoint(PlayerId)` (do `IsLocalPlayer`).
- E9: `OnPlayerLeft` zawsze słyszy `Normal` przy kodzie `Disconnect` od klienta; dwa istniejące testy `ProtocolRulesTests` zmienione; `HandshakeTests` używają `ProtocolVersion + 1`.
- `InMemoryTransport.IsDrained` nie liczy keep-alive ani pakietów wstrzymanych partycją.
- Test E3 wymaga haka `PersistenceWriter.CompleteChannelForTest`.
- Pierwszy commit testów E3-E9 zawiera zalążki API tylko do kompilacji; testy `ShutdownSequenceTests` i `ServerRuntimeTests` zatwierdzono razem z implementacją.
- Błąd IO checkpointu psuje writer, więc test `CheckpointFailed` idzie przez tryb bezpieczny i `save`.
- `HostTests` używają jednego serwerowego `InMemoryTransport` i różnie nazwanych klientów zamiast `CompositeTransport`.
- Krok 4 (kontrakt chroniony) czeka na przegląd Opus.
- Poprawki po przeglądzie (1) Krok 8 zamykania (`Dispose` transportu) działa w `Task.Run` i ma termin `TransportDisposeTimeout` (5 s) liczony `TimeProvider`em co klatkę; po terminie ostrzeżenie i zostawienie procesowi (LiteNetLib `Stop(true)` łączy wątek bez limitu). Test: `Shutdown_TransportDisposeBlocks_MainThreadNeverBlocksAndStopCompletesAfterDeadline`.
- (2) Sprzątanie po nieudanym starcie ma termin `StartCleanupTimeout` (10 s) na każde oczekiwanie (writer, teren); kopia startowa ma termin jak checkpoint startowy. `RequestShutdown` w stanie `Starting` anuluje start (`CancellationTokenSource` powiązany z `ct`), a `StartAsync` zwraca nowy wariant `WorldStartResult.Aborted` (kod 0) po ograniczonym sprzątaniu; wybrano sprzątanie startu zamiast kanonicznej sekwencji, bo nie ma jeszcze sesji ani wpisów do opróżnienia. Testy: `StartAbortTests.*`.
- (3) Krok 3a (`OnShutdown` uczestników) jest pomijany w trybie bezpiecznym bez writera, zgodnie z planem; poprzedni wyjątek wykonawcy usunięty, oczekiwanie w `Shutdown_ReadOnlyStart_SkipsLeaveCheckpointAndWriter` poprawione.
- (4) Kod 70 tylko dla kroków zapisu: nowa flaga `ShutdownStep.FailureIsError` (ustawiona dla 3a, 3b, 4, 5, 6; 3a, bo jego wyjątek pomija leave i checkpoint). Wyjątek w kroku 7 (teren) i 8 jest logowany (Error), a kod wyjścia zostaje 0 (plan nie definiuje innego). Test: `Shutdown_TerrainShutdownThrows_IsLoggedButExitCodeStaysZero`.
- (5) `Shutdown_NoJournalEnqueueAfterFlush` wysyła intencje po kroku 1 i w trakcie opróżniania (łącze 200 ms) i sprawdza, że po zatrzymaniu do dziennika trafiły wyłącznie `PlayerLeaveOp`, a odpowiedzi to `ServerReadOnly`.

### Wykonanie części Godot (kroki 10–14, lokalnie)

Rozpoznanie:
- R1: w `--headless` `VoxelLodTerrain` z `PlanetTerrainOptions.Server` generuje kolizje (raycast fizyki trafia w teren) i `IsAreaEditable` działa bez renderera (`ModeTests.HeadlessServerTerrain_GeneratesCollisionsWithoutRenderer`). Eksport presetu `server-windows` (`dedicated_server=true`, `export_filter="all_resources"`, `template_release`) uruchamia się i przechodzi `server_smoke.py --export`; zasoby terenu nie wymagają wpisu „keep” (generator jest w kodzie, shader tylko przy renderze).
- R2: stdin jest wolny dla `Console.In` w Godocie 4.7.2 `--headless` (Windows, edytor i eksport). Godot nie obsługuje SIGTERM/Ctrl+C sam (jego `HandlerRoutine` zwraca FALSE poza debuggerem), więc `PosixSignalRegistration` działa; CTRL_BREAK trafia jako SIGQUIT. Uwaga: `*.console.exe` edytora to nakładka, która ginie od sygnału z kodem 0xC000013A, a kod wyjścia dziecka jest zgubiony, więc test dymny używa binarki bez `.console` (sam podmienia). Linux: VALIDATION_NOT_PERFORMED (brak Linuksa; CI Linux wyłączone decyzją właściciela 2026-10-09).
- R3: kontynuacje `await` po zadaniu w tle wracają na wątek główny (`ModeTests.Await_AfterWorkerTask_ContinuesOnMainThread`); `ShutdownSequence` i tak sprawdza `IsCompleted` co klatkę.
- R4: Linux `template-linux-release` bez serwera X: VALIDATION_NOT_PERFORMED (brak Linuksa/runnera; Linux CI wyłączone).

Decyzje i odstępstwa:
- `Root.tscn` jest dzieckiem sceny `Boot` (nie zastępuje jej): ścieżki to `/root/Boot/Root/Server` i `/root/Boot/Root/Client`; w trybie klienta węzeł `Server` jest zwalniany, w dedykowanym `Client`.
- `GameEnvironment` (nowy rekord) odcina od `GameComposition` wszystko, co dotyka maszyny (czas, logi, katalogi, stdin, `Print`, `QuitCoordinator`); testy składają tryby tym samym kodem co wiersz poleceń (`LaunchOptions.Parse` + `GameComposition.Create`). `StartAsync(ct, connectClient)` ma parametr dla testów restartu.
- Dodane opcje wiersza poleceń: `--bind` (host), `--profile`, `--seed`, `--write-default-config`; nieznana opcja kończy proces kodem 2. `ServerConfig.CheckpointInterval` dostał `[JsonIgnore]` (domyślny plik zawierał pole wyliczane).
- Linia gotowości to `SERVER READY world=<id> state hash=<hex>` (hash stanu po wczytaniu, porównywany ze `Stopped; state hash=` poprzedniego zamknięcia). Stan tylko do odczytu drukuje `SERVER READ-ONLY …`, nie `READY`.
- `VoxelServerTerrain.ReplayAsync` woła `TerrainReplay` z bieżącą semantyką, bo niezgodność ogona wykrywa wcześniej `ServerRuntime`; wynik niezgodności tam to błąd w kodzie.
- Test Solo ustawia kinematykę gracza przez `UpdatePlayerKinematics` i dodaje `VoxelViewer` w teście (postać i widzowie dopiero w K2-11); intencja `TerrainToolIntent` idzie przez lokalnego klienta. Testy czekają realnym czasem (limit na oczekiwanie), bo tick węzłów pochodzi z zegara silnika.
- Test dymny na Windows używa edytora (`--godot`), a także eksportu (`--export`, sprawdzone lokalnie); krok CI uruchamia wariant z edytorem, linia Linux (eksport `server-linux`) jest zakomentowana jak reszta CI Linux. Krok 4 testu (zamknięcie konsoli) na Windows to CTRL_BREAK zamiast CTRL_CLOSE.
- Katalog `build/` z lokalnego eksportu pozostał (ignorowany przez git; usunięcie czeka na zgodę).
- Nie ma jeszcze obrazu ani kamery dla solo/host: uruchomienie `--solo` tworzy świat i łączy klienta, ale bez postaci i HUD (K2-11, K3-18).


### Poprawki po przeglądzie GPT-6.1 Sol (Godot)

- P1 widok terenu klienta: `IClientTerrainProvider.TryGetPlanet` zwraca teraz `IClientTerrainView` (`Planet`, `Anchor` jako `Node3D` pod węzłem planety, `CollisionLayer`, `AddViewer`/`RemoveViewer`), a nie `PlanetTerrain`; ani `VoxelLodTerrain`, ani `VoxelTool` nie wychodzą poza serwer. `SharedTerrainProvider` i `OwnTerrainProvider` oddają ten sam typ widoku (`ClientTerrainView`, jeden na planetę). Konstruktor `SharedTerrainProvider` jest wewnętrzny. Test: `ClientTerrainApiTests.ClientFacingTerrainApi_ExposesNoMutableTerrain` (refleksja po publicznym API typów klienta). Raycast przez `ITerrainService` pominięty: żaden konsument go jeszcze nie potrzebuje (dodać w K2-11, gdy będzie potrzebny).
- P1 błąd po starcie serwera: `GameComposition.StartAsync` łapie każdy wyjątek, gdy serwer jest już `Running`/`SafeMode`, loguje go i uruchamia kanoniczne zamknięcie (przez `QuitCoordinator`, a bez niego wprost); proces kończy się kodem 70 po zamknięciu, a `Release()` nie zastępuje już zamknięcia. Zapis `last-world.txt` nie jest krytyczny: błąd IO jest logowany, serwer działa dalej. Testy: `LifecycleTests.FailureAfterRunning_GoesThroughCanonicalShutdown_WorldReloadsWithSameHash`, `LastWorldNotWritable_ServerKeepsRunning_AndStopsCleanly`.
- P1 sygnały: wymuszone wyjście (`Environment.Exit(130)`) tylko przy trzecim `SIGINT` (Ctrl+C); `SIGTERM`, `SIGQUIT` (CTRL_BREAK) i zamknięcie okna nigdy nie wymuszają wyjścia, kolejne takie żądania są logowane i ignorowane. Logika siedzi w `QuitCoordinator.HandleSignal` (wyjście wstrzykiwalne w testach). Testy: `QuitCoordinatorTests` (trzy testy sygnałów).
- P2 wyjście hosta: koniec sesji lokalnego klienta (solo i host) przy działającym serwerze prosi o kanoniczne zamknięcie z `HostLeft`; w trakcie zamykania żądanie jest pomijane, bo to serwer kończy tę sesję. Test: `LifecycleTests.LocalClientLeaves_ServerShutsDownWithCheckpoint` (solo i host).
- P2 zamykanie transportu zdalnego klienta: `QuitCoordinator` wywołuje `GameComposition.ReleaseStep()` co klatkę; transport UDP jest zwalniany w `Task.Run`, a koordynator czeka według `TimeProvider` do terminu 5 s i wychodzi mimo wiszącego transportu (log). `Release()` (synchroniczne) zostaje dla ścieżek błędu i testów. Test: `QuitCoordinatorTests.SlowCleanup_IsWaitedForPerFrameUntilTheDeadline_ThenQuits` (atrapa czyszczenia + `FakeTimeProvider`); `GameComposition` nie przyjmuje atrapy transportu, więc ścieżkę UDP pokrywa `ClientModeTests` (fixture czeka na `ReleaseStep`).
- P2 test dymny: wybrano polecenie konsoli `dev-op` włączane flagą `--dev` (tylko serwer dedykowany; bez flagi polecenie nie istnieje). Dodaje gracza offline przez `ServerSimulation.ApplyServerOperation` (join i leave wymyślonej tożsamości), więc zmiana idzie przez operację i dziennik. `tools/server_smoke.py` zmienia stan przed zapisem, wymaga, by skrót stanu po zatrzymaniu różnił się od początkowego, a po restarcie był równy skrótowi z zatrzymania; sprawdzenie sygnału liczy tylko wiersze po sygnale i wymaga zmiany skrótu względem startu. Testy: `ConsoleCommandProcessorTests.DevOp_*`, `LaunchOptionsTests.Parse_Dev_OnlyForTheDedicatedServer`.

## Pułapki

- `ServerSession` połyka wyjątki haków (zamyka peera). Bez kontroli `simulation.IsFaulted` w `Tick` serwer w awarii udawałby, że działa, a `EnsureNotFaulted` odrzucałby każde kolejne wywołanie wyjątkiem w każdym peerze.
- Nie wołaj `PreparePlayerJoin` poza `OnPlayerJoining` i nie przechowuj jego wyniku (E5). Nie buduj `PlayerLeaveOp` z nienormalizowanej rotacji z fizyki Godota (`WriteQuaternion` rzuca poza 1e-3).
- `LeaveAllOnlinePlayers` po opróżnieniu sesji, nie przed: najpierw `OnPlayerLeft` stosuje wyjścia z kinematyką z chwili rozłączenia, a `LeaveAll` łapie tylko resztę (np. peerów zamkniętych po terminie). Odwrotna kolejność zostawiłaby połączonych graczy offline w świecie, a ich ostatnie wejście trafiłoby w `PlayerOffline`.
- `TerrainBlockStore.Close()` dopiero po `ServerTerrainBlocks.CloseAsync() == true`; przy `false` wcale (D8), inaczej wątek Voxel Tools dostanie `ObjectDisposedException` (K2-09). Writer zamyka się przed terenem, bo checkpoint w kroku 4 czyta bufor `ServerTerrainBlocks`.
- `EnqueueJournal` po `DisposeAsync` rzuca (`ObjectDisposedException`), a po E3 także w oknie zamykania: każda intencja po kroku 1 sekwencji jest odrzucana przez `acceptsChanges`, nie przez wyjątek writera.
- Żadnego `.Wait()`/`.Result` na wątku głównym: `FlushAsync`/`CloseAsync` kończą się na innych wątkach, a `TerrainReplay` czeka klatkami silnika — blokada wątku głównego to zakleszczenie.
- U hosta `InMemoryTransport` serwera i klienta pochodzą z jednego `InMemoryNetwork(TimeProvider.System)`; nie twórz drugiej sieci. Lokalny klient nie dostaje cache terenu ani danych bloków (K3-16 sprawdza `IsLocalPlayer`).
- `UdpTransport` nie ma uwierzytelnienia do K9-58: tożsamość `Dev` z UDP to deklaracja klienta. Serwer publiczny bez hasła pozwala podszyć się pod innego gracza i przejąć jego postać — przykładowa konfiguracja ma pole `password` z komentarzem w dokumentacji serwera; domyślnie puste tylko dla LAN.
- `PosixSignalRegistration` na Windows obsługuje `SIGINT`/`SIGQUIT`/`SIGTERM` przez zdarzenia konsoli; `CTRL_CLOSE_EVENT` daje systemowi ~5 s, więc krok 4 (checkpoint) może nie zdążyć — dziennik i tak jest trwały co 100 ms, a restart odtwarza ogon.
- Testy z SQLite (`ServerRuntimeTests`, `WorldBackupsTests`) w kolekcji bez równoległości z B1; bez `SqliteConnection.ClearAllPools`.
- `ConsoleInputPump` to wątek tła (`IsBackground = true`): `ReadLine` blokuje bez możliwości przerwania, więc nie czekaj na jego koniec przy zamykaniu.
- Eksport „dedicated server” Godota może wyciąć zasoby wizualne potrzebne generatorowi (R1); test dymny na Linuksie musi używać prawdziwego eksportu, nie edytora.

## Pytania do właściciela

1. **Nowe projekty `Horyzont.Server` i `Horyzont.Client` (bez Godota).** Logika trybów, pętli serwera, zamykania i konsoli jest testowalna bez silnika; composition root zostaje w `game/src/Boot/`. `Horyzont.Client` ma nazwę, której oczekuje K2-13 (krok 0 tam dopasuje się do tego pakietu). Domyślnie: tak.
2. **`RejectReason.ServerReadOnly = 21` z podbiciem `ProtocolVersion`.** Rozstrzygnięte na bramce K1: nowy kod i `ProtocolVersion` 1 → 2 (D4; K2-11 podbija do 3); komunikat ostrzegający graczy (`MessageType` z zakresu 22–29, bo 21 bierze K3-15) w K3-18.
3. **Dołączenie w trybie bezpiecznym** kończy się zamknięciem połączenia przez `Fail` (klient widzi `ProtocolViolation`/zerwanie zamiast czytelnej odmowy), bo `HandshakeRejectCode` jest zamrożony (ZASADY pkt 3). Domyślnie: akceptujemy w K2; czytelny kod w K3-18 przez nowy komunikat.
4. **Bany i administratorzy:** konsola ma tylko `kick` (spec). `ServerOptions.BannedIdentities` istnieje, ale bez trwałej listy i polecenia `ban`. Domyślnie: po K2 (K9).
5. **Domyślny port 27015 i katalog danych obok pliku wykonywalnego** dla serwera dedykowanego. Domyślnie: tak; `--data-dir` dla instalacji systemowych (Linux: `/var/lib/horyzont`).
6. **Host w K2 nasłuchuje na UDP** (LAN, bez Steam do K3-17), domyślnie port 27015, hasło z `--password`. Solo nie nasłuchuje wcale. Domyślnie: tak; K3-17 dokłada `SteamTransport` do `CompositeTransport` hosta.
7. **Przywracanie kopii w solo/hoście** tylko jako `WorldStartResult.Corrupted` dla UI z K3-18 (odroczone do K3-18); serwer dedykowany ma `--restore-latest-backup`. Domyślnie: tak.
