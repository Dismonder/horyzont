# K3-18 · Interfejs: menu, lista światów, HUD deweloperski

| | |
| --- | --- |
| Prowadzi | Gemini 3.8 Flash (High) |
| Testy akceptacyjne | Sonnet 5.5, przed kodem; wykonawca nie zmienia testów bez zgody |
| Przegląd | Sonnet 5.5; Opus 5.5 dla nowych komunikatów Core i sesji, przegląd planu na bramce K2 |
| Zależy od | K3-00 (szwy); K2-10; pozostałe K3 niezależne, integracja Steam i metryk replikacji przez opcjonalne adaptery |
| Gałąź | `pakiet/k3-18-interfejs` |

**Cel:** gracz wybiera solo, hosta albo dołączenie, uruchamia istniejący/nowy świat i otrzymuje czytelny błąd lub ostrzeżenie; HUD pokazuje FPS, tick, pasmo oraz stan narzędzi bez ingerencji w symulację.

## Wspólne ustalenia K3

Domyślnie start po małym PR [K3-00 szwy](K3-00-szwy.md) na bramce K2. Obowiązują [README: X1–X11](README.md#x1-szwy-przed-startem-pakietów): jeden dispatch S1, budżety S4, pełne migawki S5, readiness S6, viewers S7, narzędzia S8 i własne partial S9. Rekomendowane scalanie 15 → 16 → 14 → 17 → 18; integrację wspólnego punktu wykonuje pakiet scalany później, kolejność nie warunkuje startu. Bez odpowiedzi właściciela pakiet rusza z wariantem domyślnym.

## Kontekst do przeczytania

- `docs/ZASADY.md`: punkty 2–4, 6–8; `docs/plans/README.md:78–80`: D1/D4/D8.
- `docs/plan/plan-projektu.md:99`: zakres, właściciel i zależność K3-18.
- `docs/spec/01-planeta.md:45`: minimalny interfejs; `:56–98`: tryby i rozdział UI od serwera; `:209–218`: start i wyjście hosta; `:237–245`: lobby Steam; `:294–314`: błędy, warning zapisu, kopie i logowanie; `:332–344`: kryteria ukończenia.
- `docs/plans/K2-10-tryby-gry.md:62,68,200–218,597–602`: menu, Corrupted/backups, safe-mode warning i czytelna odmowa join; `K2-11-postac-i-kamera.md:323,330`: widok gracza i Esc; `K2-13-narzedzia.md:64,182,447,507`: HUD, pending i limity GameRules klienta.
- `docs/plans/K3-17-steam-lobby.md`: nowe kontrakty controller/friends/confirmation; `K1-03-core-stan-i-operacje.md:614–622`: numery komunikatów.

### Kod, na którym pakiet buduje

Symbole zweryfikowane na `pakiet/k2-bramka`; tabela opisuje kod istniejący, nie projektowane API.

| Typ / składowa | Plik:linia | Konsekwencja dla UI |
| --- | --- | --- |
| `LaunchOptions.IsModeRequested`, `Parse` | `game/src/Boot/LaunchOptions.cs:87`, `:98` | bez argumentu trybu pokaż menu, CLI nadal omija menu |
| `Boot._Ready` | `game/src/Boot/Boot.cs:24` | istniejąca ścieżka smoke/engine-tests musi zostać wcześniejsza |
| `GameComposition.StartAsync` | `game/src/Boot/GameComposition.cs:459` | zwraca int?, dziś nie wystawia wyniku startu UI |
| `GameComposition` switch `WorldStartResult.Corrupted` | `game/src/Boot/GameComposition.cs:646` | obecnie tylko log i kod zakończenia; potrzebna publikacja wyniku |
| `GameComposition.WorldPath`, `IsFinished`, `RequestShutdown`, `ReleaseStep` | ten sam plik `:146`, `:152`, `:525`, `:551` | aktywny świat i lifecycle |
| `WorldStartResult.Corrupted`, `StartedReadOnly`, `FromNewerVersion`, `ReplayFailed`, `StartFailed` | `src/Horyzont.Server/WorldStartResult.cs:36`, `:31`, `:41`, `:46`, `:53` | komplet wyników do mapowania |
| `ServerRuntime.StartAsync` | `src/Horyzont.Server/ServerRuntime.Start.cs:23` | źródło wyniku startu |
| `WorldBackups.RestoreLatest`, `DiscardJournalTail` | `src/Horyzont.Persistence/Backup/WorldBackups.cs:44`, `:23` | kopia przed odzyskaniem i usunięcie ogona tylko jawnie |
| `ServerRuntime.RuntimeState`, `RequestCheckpoint` | `src/Horyzont.Server/ServerRuntime.cs:85`, `:147` | warning i przycisk zapisu; ponowienie writera wymaga osobnej komendy |
| `GameServerHooks.OnPlayerJoining`, `OnIntent` | `src/Horyzont.Server/GameServerHooks.cs:53`, `:103` | safe-mode join dziś rzuca; intencja zwraca ServerReadOnly |
| `ServerSession.HandleHello` | `src/Horyzont.Net/Session/ServerSession.cs:491` | nowa odmowa przed operacją join |
| `ClientRuntime.AddListener`, `IClientSessionListener.OnRejected`, `OnDisconnected` | `src/Horyzont.Client/Session/ClientRuntime.cs:49`, `IClientSessionListener.cs:18`, `:48` | UI jest listenerem, nie właścicielem symulacji |
| `ToolHud.RejectKey`, `ToolNode.Pending`, `LastReply`, `IntentAnswered` | `game/src/Client/Tools/ToolHud.cs:43`, `ToolNode.cs:54`, `:81`, `:45` | istniejące tłumaczenie i liczniki, bez duplikowania narzędzi |
| `TransportStats(float RttMs, float PacketLoss, long BytesSent, long BytesReceived)` | `src/Horyzont.Net/Transport/TransportStats.cs:8` | pasmo licz z przyrostów, nie z liczby wiadomości |
| `QuitCoordinator.RequestStop` | `game/src/Boot/QuitCoordinator.cs:68` | jedyna ścieżka wyjścia procesu |

## Zakres

Menu solo/host/join/exit, lista światów, tworzenie świata, dołączanie IP/port/hasło, adapter UI Steam, ekran ładowania/anulowania, powrót do menu i HUD. Wszystkie wyniki WorldStartResult oraz disconnect/reject. Propozycja RestoreLatest dla Corrupted; jawne recovery przy D1; ostrzeżenia safe mode lokalne i zdalne oraz czytelny join reject nowym komunikatem. HUD przejmuje ToolHud i zgłoszenie rozbieżności GameRules klient/serwer jako wyjaśnienie odmowy, bez zmiany reguł.

## Poza zakresem

K9-61: remapping, dostępność i pełna lokalizacja/ustawienia grafiki. K6-39: ekwipunek i ustawienia przetrwania. K3-17: SDK i transport Steam. K3-14/15/16: symulacja/replikacja/predykcja. Brak kasowania świata, wyboru dowolnej kopii, migracji bazy przez listę światów, chmury Steam i telemetrycznego uploadu. Prawdziwe GameRules nie są wysyłane w ServerHello w tym pakiecie: rozbieżność klienta daje czytelną odmowę, a synchronizacja kontraktu wymaga osobnego zatwierdzonego planu.

## Projekt

### Pliki, kontrakty wystawiane i konsumowane

Poniżej wszystkie typy/pola/metody opisane jako projektowane są **nowe API**.

| Nowe pliki | Kontrakt / rola |
| --- | --- |
| `src/Horyzont.Client/UI/MenuState.cs` | enum `Main`, `Worlds`, `Join`, `Loading`, `Playing`, `Pause`, `Error`, `Stopping` |
| `src/Horyzont.Client/UI/MenuController.cs` | `MenuState State { get; }`; `void OpenWorlds(UiLaunchMode mode)`, `void OpenJoin()`, `void BeginLaunch()`, `void Cancel()`, `void CompleteStart(UiStartOutcome outcome)`, `void SessionEnded(DisconnectCode code)`; modele UI bez zależności Client→Server |
| `src/Horyzont.Client/UI/UiLaunchMode.cs` | Nowy enum `Solo`, `Host` |
| `src/Horyzont.Client/UI/UiStartKind.cs` | Nowy enum `Started`, `StartedReadOnly`, `Corrupted`, `FromNewerVersion`, `ReplayFailed`, `Aborted`, `StartFailed` |
| `src/Horyzont.Client/UI/UiStartOutcome.cs` | `sealed record UiStartOutcome(UiStartKind Kind, string Message, int BackupCount, ulong? OpId, int? SchemaVersion, int? SupportedVersion, bool TerrainSemanticsMismatch)` |
| `game/src/UI/WorldStartOutcomeMapper.cs` | Nowe `static UiStartOutcome Map(WorldStartResult result)`; Server pozostaje w grze |
| `src/Horyzont.Client/UI/WorldListEntry.cs` | `readonly record struct WorldListEntry(Guid WorldId, DateTimeOffset LastWriteUtc, bool DatabasePresent, string? Error)` |
| `src/Horyzont.Client/UI/WorldCatalog.cs` | `Task<IReadOnlyList<WorldListEntry>> ListAsync(string savesDirectory, CancellationToken ct)`; tylko pliki/metadane systemu plików, nie WorldLoader |
| `src/Horyzont.Client/UI/JoinAddressParser.cs` | `bool TryParse(string address, string portText, out string host, out int port)`; IPv4, DNS i IPv6 bez nawiasów w oddzielnym polu |
| `src/Horyzont.Client/UI/UiErrorMapper.cs` | `string KeyFor(UiStartKind kind)`, `string KeyFor(DisconnectCode code)`; osobne formatowanie detali bez BBCode |
| `src/Horyzont.Client/Diagnostics/HudSample.cs` | `readonly record struct HudSample(HudFields Present, double Fps, double ServerTickMilliseconds, ulong Tick, double RttMilliseconds, double SentBytesPerSecond, double ReceivedBytesPerSecond, int PendingIntents, int Rejections, bool ReadOnly)`; dostępność wartości przez `HudFields Present` |
| `src/Horyzont.Client/Diagnostics/HudFields.cs` | Nowe `[Flags] enum HudFields : ushort { None = 0, Fps = 1, ServerTick = 2, ServerTickMilliseconds = 4, Rtt = 8, Bandwidth = 16, Tools = 32, ReadOnly = 64 }` |
| `src/Horyzont.Client/Diagnostics/IHudMetricsSource.cs` | Nowe `bool TryGetServerTick(out ulong tick)`, `bool TryGetServerTickMilliseconds(out double milliseconds)`, `bool TryGetRtt(out double milliseconds)`, `bool TryGetTransportBytes(out long sent, out long received)`, `bool TryGetTools(out int pending, out int rejections)`, `bool IsReadOnly { get; }`, `int SessionGeneration { get; }` (zmiana resetuje liczniki pasma) |
| `src/Horyzont.Client/Diagnostics/HudSampler.cs` | `HudSample Sample()`; wstrzyknięty nowy `IHudMetricsSource` i TimeProvider, stały ring 60 próbek |
| `game/src/UI/MainMenu.cs`, `WorldListPanel.cs`, `JoinPanel.cs`, `LoadingPanel.cs`, `ErrorDialog.cs`, `PauseMenu.cs`, `DeveloperHud.cs`, `GameUiCoordinator.cs` | Control/CanvasLayer, Main-thread; sygnały do controllera i coordinatora, żadnych operacji świata bez serwera; `GameUiCoordinator` ma procesowy `QuitCoordinator` i pełny cykl życia X8 pkt 2–3 opisany w sekcji poniżej |
| `game/scenes/UI/MainMenu.tscn`, `WorldListPanel.tscn`, `JoinPanel.tscn`, `LoadingPanel.tscn`, `ErrorDialog.tscn`, `PauseMenu.tscn`, `DeveloperHud.tscn` | ustalone drzewa i przyciski poniżej |
| `game/src/UI/GameHudMetricsSource.cs` | adapter runtime/session/transport/tools; nie dotyka prywatnych pól przez refleksję |
| `src/Horyzont.Client/UI/IUiLobbyService.cs` | `bool Available { get; }`, `void Host(UiLobbyVisibility visibility, int capacity)`, `void Join(ulong lobbyId)`, `void Invite()`, `void Leave()`, `int CopyFriends(Span<UiLobbyFriend> into)`; event `Action<ulong> JoinConfirmationRequested`, event `Action<UiLobbyError> Failed`; `UiLobbyState State { get; }`, `event Action<UiLobbyState>? StateChanged`, `void ConfirmJoin(ulong lobbyId)`, `void DeclineJoin()`, `event Action<ulong, bool>? ConnectRequested`; adapter `SteamUiLobbyService` w game/src/UI/, implementacja `UnavailableUiLobbyService` jako domyślna; nowe lokalne typy UiLobbyVisibility (Private/FriendsOnly/Public), UiLobbyError (wartości lokalnie odpowiadające LobbyError), readonly record struct UiLobbyFriend(ulong SteamId, string Name, ulong LobbyId), po jednym pliku w UI/; nowy `src/Horyzont.Client/UI/UiLobbyState.cs` (Unavailable/Idle/Creating/Hosting/Joining/Joined/Leaving/Failed jak `LobbyState` K3-17), mapowanie 1:1 w `SteamUiLobbyService` po scaleniu 17 |
| `src/Horyzont.Client/UI/IGameplayInputGate.cs` | `bool IsBlocked { get; }`; MenuController implementuje, input ruchu/narzędzi czyta przed pobraniem klawiszy; nie zmienia silnika CharacterMotor |
| `src/Horyzont.Core/Protocol/Messages/ServerStatus.cs` | `sealed record ServerStatus(bool ReadOnly, byte Reason, string Detail) : Message`, kod 22 |
| `src/Horyzont.Core/Protocol/Messages/JoinUnavailable.cs` | `sealed record JoinUnavailable(byte Reason, string Detail) : Message`, kod 23 |
| `src/Horyzont.Net/Session/JoinUnavailableException.cs` | kontrolowana odmowa gry; `byte Reason`, `string Detail`; obsługiwana tylko wokół OnPlayerJoining |
| `tests/Horyzont.Client.Tests/UI/`, `Diagnostics/`, `tests/Horyzont.Core.Tests/ServerStatusCodecTests.cs`, `tests/Horyzont.Net.Tests/JoinUnavailableTests.cs`, `game/tests/UI/` | testy Sonnet z tabeli; `Start_AllWorldStartResults_ShowsExpectedScreen` w `game/tests/UI/` (mapper + controller), pozostałe testy modeli bez Server w Client |

Modyfikacje: `game/src/Client/Character/LocalInputReader.cs`, `game/src/Client/Tools/ToolInputReader.cs`, `Boot.cs`, `GameComposition.cs`, `game/project.godot`, `ToolHud.cs` tylko wyłączenie starej instancji, `ServerNode.cs`, `ServerRuntime.cs`/`GameServerHooks.cs`, Core `MessageType.cs`/`MessageChannels.cs`/`MessageCodec.cs`/`ProtocolConstants.cs`, Net `MessageRouter.cs`/`ServerSession.cs`/`ClientSession.cs`. Tłumaczenia dodaj w **nowym** `game/locale/ui.csv` (kolumny `keys,en,pl`, jak `game/locale/tools.csv:1`); nie nadpisuj tools.csv.

Nowe pola composition: `WorldStartResult? LastWorldStartResult { get; }` (zamiast eventu `WorldStartCompleted`, patrz Odstępstwa). Pole ustawiane na głównym wątku po `Server.StartAsync` i przed obecnym switchem; mapowanie na `UiStartOutcome` robi `GameUiCoordinator` (18.1); pozostaw StartAsync int? dla CLI. UI nigdy nie odtwarza wyniku przez parsowanie logu. Procesowy `QuitCoordinator` w `GameUiCoordinator` według X8; sesje z `ManageQuit = false`, `RegisterSignals = false`. Nowe metody: `ClientSession.GetTransportStats() → TransportStats`, `ServerSession.GetTransportStats(PlayerId) → TransportStats`, odczyt statystyk własnego połączenia/peera; `ServerRuntime.RequestCheckpoint()` już realizuje konsolowe save i ClearFault (`ServerRuntime.cs:147–158`, `Console/ConsoleCommandProcessor.cs:85`); UI korzysta bez nowej metody retry. Nie odblokowuje SafeMode przed potwierdzeniem writera. Wystawienie lokalnych metryk poza Core nie zmienia wire.

### Procesowy cykl życia i bramka wejścia (18.2, 18.6, X8)

`Boot` dodaje `GameUiCoordinator` jako dziecko. Koordynator UI tworzy jeden procesowy `QuitCoordinator`, `Initialize` z delegatami: `requestShutdown = () => { _quitRequested = true; _current?.RequestShutdown(); }`, `isFinished = () => _quitRequested && (_current is null || _current.IsFinished)`, `exitCode = () => 0`, `cleanup = () => (_current?.ReleaseStep() ?? true) && (_platform?.TryShutdown() ?? true)`, `registerSignals: true`. Kompozycje sesji dostają `GameEnvironment { ManageQuit = false, RegisterSignals = false }`. Bez sesji i bez prośby o quit proces nadal działa.

Koniec sesji (host zakończył, Cancel, ReturnToMenu, zgoda na zaproszenie): koordynator UI woła `RequestShutdown()`, w `_Process` pompuje `ReleaseStep()` do true albo do tego samego terminu release co `GameComposition` (obecnie 5 s). Sukces → `_current = null`, `Root.QueueFree()`, menu; przekroczenie → `ErrorDialog` „Nie udało się zamknąć poprzedniej sesji. Uruchom grę ponownie.”, wszystkie przyciski startu zablokowane (X8 pkt 3). `SteamPlatformServices` i `SteamLifetime` pozostają procesowe, dziecko `Boot`; SDK zamykane dopiero po zwolnieniu transportów przy quit. Przy timeout zwolnienia sesji `SteamAPI.Shutdown` nie jest wołane.

`LocalInputReader` dostaje nową właściwość `IGameplayInputGate? Gate`; przy `Gate?.IsBlocked == true`: `Take()` zwraca `LocalInputSample.None`, `_Input` ignoruje ruch myszy i kółko, `_UnhandledInput` nie przechwytuje myszy LPM; nowa publiczna `Recapture()` woła prywatne `Capture()`. `ToolInputReader` traktuje `IsBlocked` jak `active = false`. Esc w trybie menu: `GameUiCoordinator._Input` przełącza `PauseMenu` na akcji zwalniania kursora używanej przez `LocalInputReader` i woła `GetViewport().SetInputAsHandled()`; Resume woła `Recapture()`. K3-18 nie edytuje `LocalPlayer.cs`.

### Ustalony układ i przepływy

1. Root UI: `MainMenu` z VBox `Title`, `SoloButton`, `HostButton`, `JoinButton`, `QuitButton`. `WorldListPanel`: `WorldList` (ItemList), `NewWorldButton`, `StartButton`, `BackButton`; tworzenie w oknie `SeedInput`, `CreateButton`, `CancelButton`. `JoinPanel`: `HostInput`, `PortInput` (domyślnie 27015), `PasswordInput` z secret=true, `ConnectButton`, `FriendsList`, `SteamJoinButton`, `BackButton`. Host ma OptionButton Private/Friends/Public, domyślnie FriendsOnly, oraz `EnableSteamCheck`; brak SDK → opcja nieaktywna i tekst, LAN działa.
2. Bez argumentu trybu: menu; żaden świat, transport ani server runtime nie powstaje. `--solo/--host/--connect/--server`, smoke, generator-report, engine-tests i pozostałe istniejące tryby diagnostyczne pozostają przed menu. Dedicated/headless nigdy nie tworzy Control ani Steam SDK. GameUiCoordinator składa zależności tylko przez GameComposition, nie staje się drugim composition root. `+connect_lobby` (17.2) omija menu i uruchamia join K3-17.
3. Lista: katalog jak GameComposition (`<dataDir>/saves/<GUID-D>/world.db`, domyślnie user data w solo/host), enumeracja jednego poziomu, tylko GUID-D. Sortowanie LastWriteUtc malejąco, potem Guid; wyświetl krótkie id i lokalną datę. Katalog bez world.db pokaż jako niedostępny. Błąd dostępu osobnego wpisu nie zrywa całej listy. Scan async z CancellationToken, wynik wraca na main thread; nie otwieraj SQLite, nie migruj i nie dotykaj dziennika. Limit 1024 wpisów, nadmiar sygnalizowany. Lista nie obiecuje zdrowia bazy: Corrupted wykrywa właściwy start.
4. New: nowe Guid generowane tylko po Create, seed parsowany ulong InvariantCulture (puste pole: kryptograficzne losowanie poza symulacją); przekazuj przez LaunchOptions.WorldId/GalaxySeed. Utworzenie DB wyłącznie istniejącym startem serwera. Zwykły wybór istniejącego świata nie zmienia seeda ani WorldId. Podwójny click blokuje wszystkie Start/Create/Connect do końca operacji.
5. IP join: osobne host/port/hasło; hostname/IPv4/IPv6, port 1–65535, host trim długość ≤253, bez URI/userinfo/ścieżki, hasło ≤64 bajty UTF-8 (istniejący limit); walidacja przed tworzeniem transportu. Hasło żyje tylko w pamięci bieżącego formularza, nie trafia do logu/dysku. Po Connect Loading pokazuje etap i Cancel. Timeout sieci 10 s pozostaje w sesji. Cancel nie wywołuje GetTree.Quit: anuluje start, RequestShutdown, pompuje zamknięcie/release, wraca do Join/Worlds.
6. Started → Playing; `StartedReadOnly` (solo/host) → stan `Error`, okno `ErrorDialog` z treścią `Message`, przyciski `BackButton` i — tylko przy `TerrainSemanticsMismatch` — `RecoverCheckpointButton` (punkt 8); bez stanu `Playing`. Lokalny klient nie łączy się (`StartAsync(ct, connectClient: false)`). Zdalny klient takiego serwera dostaje `JoinUnavailable` (reason 1). Corrupted → ErrorDialog z Message, liczbą kopii, `RestoreLatestButton` (tylko Backups.Count>0), `BackButton`. FromNewerVersion → „Świat wymaga nowszej wersji gry”, wersje schema; ReplayFailed → numer OpId i message, bez pomijania operacji; StartFailed → message; Aborted → menu bez alarmu.
7. RestoreLatest: dialog „Zastąpić bazę ostatnią kopią? Uszkodzony plik zostanie zachowany.” Zgoda dopiero po całkowitym shutdown/release nieudanego startu; wznowienie tego samego mode/WorldId przez LaunchOptions.RestoreLatestBackup=true, istniejąca ścieżka WorldBackups.RestoreLatest. Nie wywołuj RestoreLatest przy aktywnym writerze. Zero kopii/błąd restore → ErrorDialog, bez tworzenia pustego świata pod tym samym id. Nie wykonuj restore automatycznie.
8. D1: `StartedReadOnly(TerrainSemanticsMismatch, Message)` wyświetla wersje/liczbę operacji/OpId zawarte w Message oraz dwa wybory: Back i `RecoverCheckpointButton`. Ten drugi wymaga osobnego potwierdzenia „Odrzucić zmiany po checkpoincie? Najpierw powstanie kopia pre-recover.” Dopiero po release restart z RecoverFromCheckpoint=true; DiscardJournalTail pozostaje w istniejącym ServerRuntime. Nie uruchamiaj recovery klientem zdalnym. Zwykły pełny dysk nie pokazuje tej opcji.
9. PauseMenu: Resume, Save (solo/host), Invite (aktywne Steam lobby), ReturnToMenu, Quit. Escape otwiera Pause, zwalnia mysz; ponowne Escape/Resume zamyka, przywraca capture. Serwer sieciowy nadal tyka; wszystkie gameplay inputs (ruch i narzędzia) zablokowane, gdy modal/menu ma focus, nie tylko ukryte. Quit idzie QuitCoordinator.RequestStop; ReturnToMenu RequestShutdown/ReleaseStep bez Quit procesu. Na timeout/fault release wątku: pokaż błąd i wymagaj wyjścia procesu, nie uruchamiaj następnego świata obok żywego workera. Dopiero skuteczne release pozwala ponownie startować; dokładna procedura i komunikat według X8 pkt 3 poniżej.
10. Steam: konsumuj controller K3-17 przez nowy lokalny `IUiLobbyService` adapter (Host/Join/Invite/Leave/lista/status/confirm); domyślna implementacja unavailable umożliwia samodzielny odbiór IP/menu przed K3-17. Typy UiLobbyVisibility/UiLobbyError/UiLobbyFriend należą do 18 i nie referują jeszcze nieistniejących typów Steam z 17; adapter mapuje je jawnie po scaleniu 17. Lista znajomych i join request podłączone po scaleniu 17; sam moduł UI buduje się wcześniej z unavailable/fake. Zaproszenie przy aktywnym świecie: osobny modal zgody, potem D8 i join; Back/Cancel bez zmiany świata. Błąd SDK/lobby używa lokalnego mapowania LobbyError, a zakończenie hosta pokazuje „Host zakończył sesję” i ReturnToMenu, bez auto-reconnect. `ConnectRequested(host, passwordRequired: true)` → `JoinPanel` w trybie Steam pokazuje tylko `PasswordInput` i `ConnectButton`, potem `ClientRuntime.Connect(new SteamEndpoint(host), …, password)`.

### Ostrzeżenia i czytelna odmowa dołączenia

1. Rezerwacja: K3-15 MessageType 21, tutaj 22 ServerStatus i 23 JoinUnavailable, oba ReliableOrdered, format 1. Payload ServerStatus: bool jako bajt 0/1, reason byte, string z limitem `ProtocolConstants.MaxRejectDetailBytes` (256 B UTF-8); JoinUnavailable: reason byte + ten sam string. Reason: 0=Running (tylko ServerStatus.ReadOnly=false), 1=TerrainSemanticsMismatch, 2=WriterFault. Nie dodawaj tych lokalnych powodów do zamrożonego HandshakeRejectCode. Nieznany reason przy prawidłowej ramce wyświetla ogólny błąd; zły bool/truncated/oversized to ProtocolViolation.
2. `GameServerHooks` przy `_acceptsChanges()==false` rzuca **nowy** JoinUnavailableException przed PreparePlayerJoin; Nowy `src/Horyzont.Server/UI/ServerStatusInfo.cs`: `readonly record struct ServerStatusInfo(bool ReadOnly, byte Reason, string Detail)`; `ServerRuntime.CurrentStatus` (Reason 1 przy starcie `StartedReadOnly(TerrainSemanticsMismatch)`, 2 przy `RuntimeState == SafeMode`, inaczej 0); `GameServerHooks` dostaje nowy parametr konstruktora `Func<ServerStatusInfo> status`. `Detail` krótki tekst techniczny po angielsku; UI pokazuje tekst z `ui.csv` według `Reason`. ServerSession.HandleHello najpierw dotychczasowa walidacja build/password/identity, potem try/catch tylko wokół OnPlayerJoining: ten konkretny wyjątek → SendMessage(peer, JoinUnavailable), Closing i dotychczasowy CloseDelay; bez PlayerId, join/leave, CreateHello lub checkpointu. Inne wyjątki nadal istniejący Fail. Tryb zamykania sesji nadal obsługuje dotychczasową odmowę, nie podszywa się pod WriterFault.
3. MessageRouter według X5 w README: 22 w `IsKnownType` i `IsGameMessageFromServer`, S→K Loading/InGame; 23 w `IsKnownType`, S→K wyłącznie `AwaitingServerHello` (jawna gałąź tego stanu), **nie** w `IsGameMessageFromServer` (prywatne `ServerSession.SendMessage(peer, …)`); żadna nowa ścieżka K→S. MessageChannels musi zawierać oba. ClientSession.Receive obsługuje JoinUnavailable przed ServerHello: przekaż do `IClientGameHooks.OnMessage`, zapamiętaj lokalny wynik i rozpocznij Close; ClientRuntime przekazuje do listenera UI. Nie wywołuj OnServerHello/SendReady. OnDisconnected nie nadpisuje wcześniej wyświetlonej przyczyny generic Timeout/Normal.
4. Nowy `ServerStatusBroadcaster` w `src/Horyzont.Server/UI/` implementuje istniejące tick/player observer: snapshot aktualnego statusu na OnPlayerReady, następnie komunikat tylko przy zmianie Running↔SafeMode; rejestracja przez istniejące AddTickParticipant/AddPlayerObserver, stan przed wysłaniem. Detail ogólny, bez ścieżek dysku/stacktrace/sekretów; techniczny błąd w lokalnym logu ownera.
5. SafeMode przy WriterFault: trwały banner „Zapis nie działa. Zmiany są wstrzymane.”, intencje blokowane w UI, ale serwer nadal waliduje każdy request. Save/Retry tylko owner solo/host przez RequestCheckpoint; warning znika dopiero po potwierdzonym Running, nie od naciśnięcia przycisku. Pending predictions kończą się przez istniejące odpowiedzi/timeout, nie przez dopisywanie operacji w UI.
6. K2-13 deferral GameRules: `ToolHud.RejectKey` dalej źródłem nazw odmów; dla limitów/zasięgu/cooldownu dodaj tekst „Serwer stosuje własne limity; podgląd jest orientacyjny.” Nie pokazuj zgadywanej liczby skonfigurowanego limitu. HUD prezentuje reason i pending; nie próbuj obejść serwerowego limitera. Ostrzeżenie nie jest przesyłaniem GameRules.
7. D4: każde scalenie zmian Core Protocol/Serialization ustawia ProtocolVersion=(main przy scaleniu)+1; po samym K2 4, po wcześniejszym K3-15/16 odpowiednio następna wartość. Aktualizuj istniejący `ProtocolVersion_MatchesDocumentedMergeValue` i `Constants_GoldenValues` (X4) oraz plan po rebase; bez nowego testu wersji. K3-18 nie zmienia operacji/intencji ani blobów; potrzeba takiej zmiany wymaga wersji i replay starego formatu z Opus. Zakres Planety MessageType 21–29, RejectReason 21–29 (21 ServerReadOnly zajęte), OperationKind 8–19, IntentCode 4–9, BlobKind 5–9 (5 TerrainBlock zajęte). D1 i złoty korpus pozostają bez zmian.

### HUD i koszty

DeveloperHud jest jednym CanvasLayer z Label `MetricsLabel` i Label `ToolsLabel`, niewidoczny domyślnie, nowa akcja `dev_hud_toggle` F3 (F/F8/F9 już zajęte). Odświeżaj tekst 4 Hz, nigdy w ticku symulacji. FPS = 1/średnia ostatnich 60 dodatnich delta render; tick = bieżący numer serwera albo ostatni ServerHello/Snapshot u zdalnego klienta. Tick ms: nowa właściwość `ServerNode.LastTickMilliseconds` (`double`, `double.NaN` przed pierwszym tickiem), mierzona `TimeProvider.GetTimestamp/GetElapsedTime` z `GameEnvironment.Time` wokół `Runtime.Tick()`; poza symulacją. Zdalny tick ms „—”, nie RTT. Pasmo = różnica TransportStats.BytesSent/Received podzielona przez czas między próbami; jednostka KiB/s (1024), cel spec 50 KB/s opisany w raporcie w jednostkach dziesiętnych. Pierwsza próba i zmiana sesji: „—”, reset liczników; `GameHudMetricsSource`: host/serwer z UI — suma `ServerSession.GetTransportStats(p)` po `PlayersInGame` z pominięciem `ServerRuntime.IsLocalPlayer(p)`; zdalny klient — `ClientSession.GetTransportStats()`; solo — brak `Bandwidth`. RTT tylko zdalny klient (`ClientSession.Rtt`); solo/host „—”. Pending/reject/reason z ToolNode, orientacja/camera z ILocalPlayerView. Brak narzędzi/replikacji pokazuje „—”. UI nie alokuje w pobieraniu metryk co tick; formatowanie stringów 4 Hz dozwolone poza tickiem. Adaptery odczytu i ring testowane zero alokacji po warmup.

### Równoległość i mapa kolizji

Po K3-00 K3-18 może ruszyć po K2-10 równolegle z 14/15/16/17. UI/WorldCatalog/HudSampler i testy .NET są własne; adapter Steam zaczyna od fake, adapter replikacji od brakujących wartości. Integracja wspólnych plików przez rebase, bez zmiany grafu.

| Wspólne pliki | Rozdział i ryzyko |
| --- | --- |
| `GameComposition.cs`, `Boot.cs`, `GameEnvironment.cs`, `LaunchOptions.cs` | S9: 14/15/16/17 własne partial; 18 wyłącznie publikacja `WorldStartResult` w composition i lifecycle w UI/Boot. Rebase 17 → 18; reguła X10: integruje pakiet scalany później, kolejność 15 → 16 → 14 → 17 → 18 |
| `MessageType.cs`, `MessageChannels.cs`, `MessageCodec.cs`, `MessageRouter.cs`, `ProtocolConstants.cs` | 15 rezerwuje 21, 18 22–23, 16 24/25; rebase po każdym scaleniu i nowy D4 |
| `ServerSession.HandleHello`, `GameServerHooks`, `ServerRuntime` | 18 kontrolowana odmowa i status, dispatch K3-00, 15/16 tylko rejestrują handlery; nie rozbudowuj drugiego switcha OnClientMessage |
| `LocalInputReader.cs`, `ToolInputReader.cs`, `ToolNode` | 18 bramka wejścia i `Recapture`, odczyt `Pending/LastReply/IntentAnswered`; nie edytuje `LocalPlayer.cs` ani `ToolNode.cs`. Ruch wyłącznie 14 |
| `game/project.godot`, tłumaczenia, UI sceny | 18 posiada F3, import ui.csv i sceny; 17 nie dodaje własnego menu/invite overlay button |
| gdUnit4 / fixed pipe | wspólny jeden Windows runner `horyzont`, testy silnika wyłącznie szeregowo; Linux CI wyłączony decyzją 2026-10-09 |

## Testy akceptacyjne (napisz najpierw)

Sonnet pisze testy controller/catalog/parser/protocol i scenariusze UI przed kodem Gemini. FakeTimeProvider, fake launcher/lobby/metrics; bez realnego Steam w CI.

| Nazwa | Scenariusz i wynik |
| --- | --- |
| `Boot_NoMode_ShowsMenuWithoutStartingWorld` | brak argumentu, zero transportów/writera; --solo/--server/smoke/engine-tests zachowują swoją ścieżkę |
| `List_MixedGuidDirectories_SortsWithoutOpeningDatabase` | zdrowa i zepsuta DB, brak DB, nie-GUID, błąd uprawnień, limit 1024; zero migracji/zmiany hash plików |
| `Create_DoubleClick_UsesOneWorldIdAndLaunch` | nowy seed, existing world, cancel; jedna operacja i brak nowego świata po cancel |
| `Parse_Ipv4DnsIpv6AndInvalidPort_ValidatesBeforeConnect` | port 0/65536, hostname, IPv6, URI, host za długi, hasło 64 i 65 B UTF-8 |
| `Start_AllWorldStartResults_ShowsExpectedScreen` | `game/tests/UI/`: mapper + controller, każdy wariant z realnego WorldStartResult, versions/OpId widoczne; StartedReadOnly → Error bez lokalnego join i Playing; błąd nie kończy procesu UI |
| `Restore_CorruptedWithBackups_RequiresConfirmationAndRelease` | zgoda, odmowa, brak kopii i IO failure; oryginał zachowany, restore nigdy przy aktywnym runtime |
| `Recover_TerrainSemanticsMismatch_BackupsBeforeDiscard` | warning D1, osobna zgoda i restart; bez zgody cały ogon/hash niezmieniony |
| `Join_ReadOnlyServer_ReceivesJoinUnavailableWithoutPlayerJoin` | reason 1/2 przed ServerHello, zero join/leave/writer operations; inne build nadal frozen HandshakeReject |
| `Codec_StatusAndJoinUnavailable_RoundTripsAndRejectsOversize` | kody 22/23, format 1, max256 UTF-8, bool, truncation/fuzz, nieznany reason i router states/channels |
| `Status_WriterFaultThenCommit_BroadcastsTransitionsOnce` | join podczas SafeMode, 2 klienci w grze; warning raz, retry bez przedwczesnego Running |
| `Disconnect_AfterSpecificError_PreservesReadableReason` | read-only/host exit/update/wrong password/timeout/kick; późny generic disconnect nie zaciera detalu |
| `Pause_FocusedModal_BlocksMovementAndToolsButServerTicks` | Esc, tekst formularza, LPM nad przyciskiem; brak input/intencji gry, serwer dalej 60 Hz |
| `Menu_ProcessQuitWithoutSession_ExitsOnlyOnRequest` | Procesowy koordynator nie kończy procesu, gdy brak sesji i brak prośby |
| `ReturnToMenu_ReleaseTimeout_BlocksNewStarts` | Po przekroczeniu terminu ErrorDialog i wszystkie starty zablokowane; brak drugiej sesji i shutdown SDK spod transportu |
| `ReturnToMenu_ActiveHost_DrainsReleasesAndAllowsNewWorld` | kolejność D8, save, listen sockets, workers/listeners; po nieudanym release nowe starty zablokowane |
| `Steam_InviteDuringGame_WaitsForConfirmationAndOldWorldStop` | fake lobby, odmowa/zgoda, missing SDK, friends i join; realne invite Manual K3-17 |
| `Sample_KnownCountersAndSessionReset_ComputesActualBandwidth` | różnice bajtów/czasu, pierwszy sample, reset, brak lokalnych metryk; tick ms nie RTT |
| `Sample_WarmedUp10000Calls_AllocatesZeroBytes` | odczyt metryk/ring poza formatowaniem; zero GC allocation |
| `Hud_ToolsRejectAndConfiguredLimits_ShowsReasonWithoutGuessing` | Pending, ServerReadOnly i odmowa limitu; tekst orientacyjności, brak zgadywanego limitu |
| `Ui_SoloHostJoinBackAndRestore_ButtonsFollowState` | gdUnit4 szeregowo: faktyczne sceny, click/focus/Esc, jeden HUD, brak niewidocznych aktywnych przycisków |

## Kroki

- [x] 0. Sprawdź scalone K2-10 i K3-00 oraz realne nazwy; kody X4, szwy S9 i cykl życia X8 są ustalone. Sonnet dostarcza testy.
- [x] 1. Dodaj nowe modele/controller/parser/catalog/error mapper; czerwone→zielone testy .NET, bez Godota.
- [x] 2. Dodaj ServerStatus/JoinUnavailable, kodek/channels/router, kontrolowaną odmowę i D4; Opus przegląda przed UI integracją; fixture X6 (`ProtocolContractTests`, `MaxMessages.All`, `CodecGen.Message`), test routera X5 i aktualizacja istniejących testów wersji (X4).
- [x] 3. Wystaw WorldStartResult oraz status broadcaster bez parsowania logu; przetestuj safe mode i D1.
- [x] 4. Utwórz dokładne sceny i ui.csv, podłącz MainMenu/WorldList/Join/Loading/Error; zostaw CLI bez menu.
- [x] 5. Dodaj cancel/restore/recovery i lifecycle powrotu do menu, input gate i Pause; żadnych lokalnych zmian świata.
- [ ] 6. Dodaj HUD 4 Hz/F3 i zastąp starą instancję ToolHud; podłącz fake Steam, następnie adapter K3-17 po rebase. (HUD i fake Steam gotowe; `ToolHud` zostaje, adapter `SteamUiLobbyService` czeka na scalenie K3-17, zob. odstępstwa.)
- [ ] 7. Testy scen szeregowo, ręcznie solo→menu→host→menu→join, modal i save warning; raport i zrzuty w PR.
- [ ] 8. Wykonawca uruchamia build/test/format/smoke z ZASADY pkt 10, Windows CI, przegląd Sonnet/Opus, aktualizacja D4 po rebase i zielone CI przed scaleniem.

## Odbiór

Menu realizuje wszystkie trzy tryby i listę istniejących światów, błędy startu nie zamykają aplikacji UI, restore/recovery wyłącznie po zgodzie, warning i odmowa read-only czytelne również u klienta zdalnego. HUD pokazuje FPS/tick/pasmo z poprawnymi jednostkami i stan narzędzi; modal blokuje gameplay. CLI/dedicated/diagnostyka bez regresji. CI wyłącznie jeden self-hosted Windows, testy silnika bez równoległości. Nie przywracaj Linux CI; kryteria platformowe całego podprojektu pozostają do dalszego odbioru właściciela.

## Odstępstwa i decyzje wykonawcy

Wykonawcy: GPT-6.1 Sol (kroki 0–1 i szkic kroku 2, przerwane limitem), potem Claude Sonnet 5.5 (reszta). Przegląd: Opus 5.5.

1. **ProtocolVersion = 6** (README D4/X4, kolejność X10: K3-15 → 4, K3-16 → 5, K3-18 → 6). `main` ma dziś 3; ostateczna wartość to (main przy scaleniu)+1, więc po rebase trzeba zaktualizować `ProtocolConstants.ProtocolVersion` oraz istniejące `ProtocolVersion_MatchesDocumentedMergeValue` i `Constants_GoldenValues` (bez nowego testu wersji). Wiersz „Kolejność K3” w README do dopisania przy scalaniu.
2. **`ToolHud` zostaje.** Plan każe go wyłączyć, ale DeveloperHud jest domyślnie ukryty (P3), więc wyłączenie `ToolHud` usunęłoby celownik i podpowiedzi narzędzi z normalnej gry. `DeveloperHud.ToolsLabel` dodaje oczekujące/odrzucone intencje, ostatnią odmowę i uwagę „Serwer stosuje własne limity…” (`HudRejectNotice`). Zastąpienie `ToolHud` zostaje dla K9 (HUD gracza).
3. **Adapter `SteamUiLobbyService` nie powstał**: K3-17 nie jest scalone (brak `SteamLobbyController`/`SteamEndpoint`), a plan każe mapować 1:1 dopiero po scaleniu. Gotowe: `IUiLobbyService`, `UnavailableUiLobbyService` (domyślny), typy `UiLobby*`, zaproszenie z potwierdzeniem w `MenuController.OfferInvite`, lista znajomych i przycisk Steam w `JoinPanel`, opcje lobby hosta. `GameUiCoordinator.OnLobbyConnectRequested` loguje i pokazuje błąd do czasu podłączenia `SteamEndpoint` przez pakiet scalany później (X10).
4. **Wynik startu a klient lokalny:** `GameComposition.StartAsync(ct, connectClient: false)` + nowa publiczna `ConnectLocalClient()`; UI łączy lokalnego klienta dopiero po `Started`, więc `StartedReadOnly` nigdy nie dołącza lokalnie. Ścieżka CLI (`connectClient: true`) bez zmian. Wyjątek od reguły X10 (S9: 18 wyłącznie publikacja `WorldStartResult` i lifecycle w UI/Boot): `ConnectLocalClient()` i parametr `connectClient` w `StartAsync`/`ConnectClient` to dodatkowa zmiana `GameComposition.cs` poza samą publikacją wyniku; przy rebase 17 → 18 integruje ją pakiet 18. Event `WorldStartCompleted` usunięty (menu czyta `LastWorldStartResult`); reason 3 ServerStatus = serwer nie działa (poza Running/SafeMode).
5. **Błąd sesji/start a zwolnienie:** po nieudanym starcie (kod wyjścia z `StartAsync`) kompozycja jest już zwolniona (`Release()`), więc UI nie czeka na `IsFinished` takiego serwera (jego stan po błędzie nie jest `Stopped`), tylko na `ReleaseStep()`. Ogólny limit zamykania serwera przed „release” to 120 s (kroki serwera mają własne terminy), release po `IsFinished` — 5 s jak w `GameComposition`.
6. `MenuController` ma kilka metod ponad tabelę planu (`TryBeginLaunch`, `CompleteStop`, `Pause/Resume`, `RequestRestore/RequestRecover/Confirm`, `OfferInvite`, `TakeRelaunch`, `ApplyServerStatus`, `JoinRefused`, `Rejected`); modale (restore, recover, invite) to `UiConfirmation`, a to, co startuje po zwolnieniu sesji, to `UiRelaunchKind`. `IsBlocked` kontrolera dotyczy stanu menu; bramka wejścia w grze (`GameUiCoordinator.IsBlocked`) dodatkowo uwzględnia otwarte pytanie.
7. Nowe pliki pomocnicze poza tabelą: `UiNodes.cs`, `HudRejectNotice.cs`, `UiConfirmation.cs`, `UiRelaunchKind.cs`. Panel „okno nowego świata” to `PanelContainer` (`NewWorldWindow`), nie osobne okno systemowe. Banner „Zapis nie działa” i przycisk ponowienia zapisu to małe `CanvasLayer` budowane w `GameUiCoordinator`, widoczne niezależnie od F3.
8. `ServerNode.Initialize` dostał opcjonalny `TimeProvider` (czas ticku mierzony poza symulacją). Pliki `.uid` nowych skryptów C# nie są dołączone: Godot generuje je przy pierwszym imporcie (sceny odwołują się do skryptów po ścieżce).
9. Pliki zapisane przez poprzedniego wykonawcę miały zakończenia CRLF w kopii roboczej (indeks był LF); znormalizowano je i uruchomiono `dotnet format` (zmiany tylko białych znaków w testach Sonneta).
10. **Nie uruchomiono** (zasada współdzielonego runnera): testy silnika gdUnit (`game/tests/UI/`), ręczny scenariusz solo→menu→host→menu→join, zrzuty ekranu i smoke silnika. Sceny `.tscn` są pisane ręcznie i nie były ładowane w Godocie: pierwszy import/test silnika na runnerze jest też ich pierwszą weryfikacją.

## Pułapki

- `GameComposition.StartAsync` zwraca kod, a nie WorldStartResult: potrzeba nowego pola/eventu; nie parsuj konsoli.
- Corrupted nie daje prawa do automatycznego przywrócenia ani stworzenia pustego świata.
- `StartedReadOnly` może nie mieć writera; RequestCheckpoint nie jest recovery D1.
- UI nie przesyła lokalnych reguł na serwer i nie wykonuje BlockSet/TerrainEdit samodzielnie.
- Wyjście do menu musi zwolnić stare worker threads i sockety przed kolejnym GameComposition.
- Prefix ClientHello i cały HandshakeReject są zamrożone; nowy join reject ma własny kod i router.

## Pytania do właściciela

Wykonawca stosuje poniższe warianty do czasu odpowiedzi; nie ma otwartych decyzji implementacyjnych.

| Nr | Pytanie | Wariant domyślny |
| --- | --- | --- |
| P1 | Czy lista światów wymaga nazw użytkownika już w K3? | nie; krótkie WorldId i data, nazwy/metadane użytkownika później bez zmiany zapisu Core |
| P2 | Czy przycisk odzyskania z checkpointu ma być dostępny ownerowi w UI? | tak, tylko D1, dwa jawne kroki (wybór i potwierdzenie), kopia pre-recover przed usunięciem ogona |
| P3 | Czy HUD deweloperski ma być widoczny od startu? | nie; F3, metryki 4 Hz, ostrzeżenie safe mode widoczne niezależnie od HUD |
| X1 | Czy przyjmujemy K3-00 przed równoległym startem pakietów? | Domyślnie tak: S1–S9 na bramce K2 bez Core/protokołu; alternatywa po odrzuceniu: X10 „integruje pakiet scalany później”, K3-14 krok 6 i K3-16 krok 6 dopiero po scaleniu K3-15 |
