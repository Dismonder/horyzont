# K3-18 · Interfejs: menu, lista światów, HUD deweloperski

| | |
| --- | --- |
| Prowadzi | Gemini 3.8 Flash (High) |
| Testy akceptacyjne | Sonnet 5.5, przed kodem; wykonawca nie zmienia testów bez zgody |
| Przegląd | Sonnet 5.5; Opus 5.5 dla nowych komunikatów Core i sesji, przegląd planu na bramce K2 |
| Zależy od | K2-10; pozostałe K3 niezależne, integracja Steam i metryk replikacji przez opcjonalne adaptery |
| Gałąź | `pakiet/k3-18-interfejs` |

**Cel:** gracz wybiera solo, hosta albo dołączenie, uruchamia istniejący/nowy świat i otrzymuje czytelny błąd lub ostrzeżenie; HUD pokazuje FPS, tick, pasmo oraz stan narzędzi bez ingerencji w symulację.

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
| `GameComposition` switch `WorldStartResult.Corrupted` | `game/src/Boot/GameComposition.cs:642` | obecnie tylko log i kod zakończenia; potrzebna publikacja wyniku |
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
| `src/Horyzont.Client/UI/MenuController.cs` | `MenuState State { get; }`; `void OpenWorlds(ProcessMode mode)`, `void OpenJoin()`, `void BeginLaunch()`, `void Cancel()`, `void CompleteStart(WorldStartResult result)`, `void SessionEnded(DisconnectCode code)`; referencje Client→Server bez Godota, bez odwrotnej zależności |
| `src/Horyzont.Client/UI/WorldListEntry.cs` | `readonly record struct WorldListEntry(Guid WorldId, DateTimeOffset LastWriteUtc, bool DatabasePresent, string? Error)` |
| `src/Horyzont.Client/UI/WorldCatalog.cs` | `Task<IReadOnlyList<WorldListEntry>> ListAsync(string savesDirectory, CancellationToken ct)`; tylko pliki/metadane systemu plików, nie WorldLoader |
| `src/Horyzont.Client/UI/JoinAddressParser.cs` | `bool TryParse(string address, string portText, out string host, out int port)`; IPv4, DNS i IPv6 bez nawiasów w oddzielnym polu |
| `src/Horyzont.Client/UI/UiErrorMapper.cs` | `string KeyFor(WorldStartResult result)`, `string KeyFor(DisconnectCode code)`; osobne formatowanie detali bez BBCode |
| `src/Horyzont.Client/Diagnostics/HudSample.cs` | `readonly record struct HudSample(double Fps, double ServerTickMilliseconds, ulong Tick, double RttMilliseconds, double SentBytesPerSecond, double ReceivedBytesPerSecond, int PendingIntents, int Rejections, bool ReadOnly)`; brak wartości oznaczany przez osobny mask/flags |
| `src/Horyzont.Client/Diagnostics/HudSampler.cs` | `HudSample Sample()`; wstrzyknięty nowy `IHudMetricsSource` i TimeProvider, stały ring 60 próbek |
| `game/src/UI/MainMenu.cs`, `WorldListPanel.cs`, `JoinPanel.cs`, `LoadingPanel.cs`, `ErrorDialog.cs`, `PauseMenu.cs`, `DeveloperHud.cs`, `GameUiCoordinator.cs` | Control/CanvasLayer, Main-thread; sygnały do controllera i coordinatora, żadnych operacji świata bez serwera |
| `game/scenes/UI/MainMenu.tscn`, `WorldListPanel.tscn`, `JoinPanel.tscn`, `LoadingPanel.tscn`, `ErrorDialog.tscn`, `PauseMenu.tscn`, `DeveloperHud.tscn` | ustalone drzewa i przyciski poniżej |
| `game/src/UI/GameHudMetricsSource.cs` | adapter runtime/session/transport/tools; nie dotyka prywatnych pól przez refleksję |
| `src/Horyzont.Client/UI/IUiLobbyService.cs` | `bool Available { get; }`, `void Host(UiLobbyVisibility visibility, int capacity)`, `void Join(ulong lobbyId)`, `void Invite()`, `void Leave()`, `int CopyFriends(Span<UiLobbyFriend> into)`; event `Action<ulong> JoinConfirmationRequested`, event `Action<UiLobbyError> Failed`; adapter `SteamUiLobbyService` w game/src/UI/, implementacja `UnavailableUiLobbyService` jako domyślna; nowe lokalne typy UiLobbyVisibility (Private/FriendsOnly/Public), UiLobbyError (wartości lokalnie odpowiadające LobbyError), readonly record struct UiLobbyFriend(ulong SteamId, string Name, ulong LobbyId), po jednym pliku w UI/ |
| `src/Horyzont.Client/UI/IGameplayInputGate.cs` | `bool IsBlocked { get; }`; MenuController implementuje, input ruchu/narzędzi czyta przed pobraniem klawiszy; nie zmienia silnika CharacterMotor |
| `src/Horyzont.Core/Protocol/Messages/ServerStatus.cs` | `sealed record ServerStatus(bool ReadOnly, byte Reason, string Detail) : Message`, kod 22 |
| `src/Horyzont.Core/Protocol/Messages/JoinUnavailable.cs` | `sealed record JoinUnavailable(byte Reason, string Detail) : Message`, kod 23 |
| `src/Horyzont.Net/Session/JoinUnavailableException.cs` | kontrolowana odmowa gry; `byte Reason`, `string Detail`; obsługiwana tylko wokół OnPlayerJoining |
| `tests/Horyzont.Client.Tests/UI/`, `Diagnostics/`, `tests/Horyzont.Core.Tests/ServerStatusCodecTests.cs`, `tests/Horyzont.Net.Tests/JoinUnavailableTests.cs`, `game/tests/UI/` | testy Sonnet z tabeli |

Modyfikacje: Client csproj (referencja Server), `Boot.cs`, `GameComposition.cs`, `QuitCoordinator.cs`, `game/project.godot`, `ToolHud.cs` tylko wyłączenie starej instancji, `ServerNode.cs`, `ServerRuntime.cs`/`GameServerHooks.cs`, Core `MessageType.cs`/`MessageChannels.cs`/`MessageCodec.cs`/`ProtocolConstants.cs`, Net `MessageRouter.cs`/`ServerSession.cs`/`ClientSession.cs`. Tłumaczenia dodaj w **nowym** `game/locale/ui.csv` (kolumny `keys,en,pl`, jak `game/locale/tools.csv:1`); nie nadpisuj tools.csv.

Nowe pola composition: `WorldStartResult? LastWorldStartResult { get; }`, event `Action<WorldStartResult>? WorldStartCompleted`. Ustaw/publikuj bezpośrednio po Server.StartAsync i przed dotychczasowym switchem; pozostaw StartAsync int? dla CLI. UI nigdy nie odtwarza wyniku przez parsowanie logu. Nowe pole QuitCoordinator `bool ExitOnSessionEnd { get; set; } = true`: CLI zachowuje wyjście po zakończeniu sesji, tryb menu ustawia false i obsługuje zakończenie przez GameUiCoordinator; RequestStop nadal zawsze zamyka proces. Nowe metody: `ClientSession.GetTransportStats() → TransportStats`, `ServerSession.GetTransportStats(PlayerId) → TransportStats`, odczyt statystyk własnego połączenia/peera; `ServerRuntime.RequestCheckpoint()` już realizuje konsolowe save i ClearFault (`ServerRuntime.cs:147–158`, `Console/ConsoleCommandProcessor.cs:85`); UI korzysta bez nowej metody retry. Nie odblokowuje SafeMode przed potwierdzeniem writera. Wystawienie lokalnych metryk poza Core nie zmienia wire.

### Ustalony układ i przepływy

1. Root UI: `MainMenu` z VBox `Title`, `SoloButton`, `HostButton`, `JoinButton`, `QuitButton`. `WorldListPanel`: `WorldList` (ItemList), `NewWorldButton`, `StartButton`, `BackButton`; tworzenie w oknie `SeedInput`, `CreateButton`, `CancelButton`. `JoinPanel`: `HostInput`, `PortInput` (domyślnie 27015), `PasswordInput` z secret=true, `ConnectButton`, `FriendsList`, `SteamJoinButton`, `BackButton`. Host ma OptionButton Private/Friends/Public, domyślnie FriendsOnly, oraz `EnableSteamCheck`; brak SDK → opcja nieaktywna i tekst, LAN działa.
2. Bez argumentu trybu: menu; żaden świat, transport ani server runtime nie powstaje. `--solo/--host/--connect/--server`, smoke, generator-report, engine-tests i pozostałe istniejące tryby diagnostyczne pozostają przed menu. Dedicated/headless nigdy nie tworzy Control ani Steam SDK. GameUiCoordinator składa zależności tylko przez GameComposition, nie staje się drugim composition root.
3. Lista: katalog jak GameComposition (`<dataDir>/saves/<GUID-D>/world.db`, domyślnie user data w solo/host), enumeracja jednego poziomu, tylko GUID-D. Sortowanie LastWriteUtc malejąco, potem Guid; wyświetl krótkie id i lokalną datę. Katalog bez world.db pokaż jako niedostępny. Błąd dostępu osobnego wpisu nie zrywa całej listy. Scan async z CancellationToken, wynik wraca na main thread; nie otwieraj SQLite, nie migruj i nie dotykaj dziennika. Limit 1024 wpisów, nadmiar sygnalizowany. Lista nie obiecuje zdrowia bazy: Corrupted wykrywa właściwy start.
4. New: nowe Guid generowane tylko po Create, seed parsowany ulong InvariantCulture (puste pole: kryptograficzne losowanie poza symulacją); przekazuj przez LaunchOptions.WorldId/GalaxySeed. Utworzenie DB wyłącznie istniejącym startem serwera. Zwykły wybór istniejącego świata nie zmienia seeda ani WorldId. Podwójny click blokuje wszystkie Start/Create/Connect do końca operacji.
5. IP join: osobne host/port/hasło; hostname/IPv4/IPv6, port 1–65535, host trim długość ≤253, bez URI/userinfo/ścieżki, hasło ≤64 bajty UTF-8 (istniejący limit); walidacja przed tworzeniem transportu. Hasło żyje tylko w pamięci bieżącego formularza, nie trafia do logu/dysku. Po Connect Loading pokazuje etap i Cancel. Timeout sieci 10 s pozostaje w sesji. Cancel nie wywołuje GetTree.Quit: anuluje start, RequestShutdown, pompuje zamknięcie/release, wraca do Join/Worlds.
6. Started → Playing; StartedReadOnly → trwały warning plus informacja o D1, nie sugeruj, że lokalny gracz może zmieniać świat. Corrupted → ErrorDialog z Message, liczbą kopii, `RestoreLatestButton` (tylko Backups.Count>0), `BackButton`. FromNewerVersion → „Świat wymaga nowszej wersji gry”, wersje schema; ReplayFailed → numer OpId i message, bez pomijania operacji; StartFailed → message; Aborted → menu bez alarmu.
7. RestoreLatest: dialog „Zastąpić bazę ostatnią kopią? Uszkodzony plik zostanie zachowany.” Zgoda dopiero po całkowitym shutdown/release nieudanego startu; wznowienie tego samego mode/WorldId przez LaunchOptions.RestoreLatestBackup=true, istniejąca ścieżka WorldBackups.RestoreLatest. Nie wywołuj RestoreLatest przy aktywnym writerze. Zero kopii/błąd restore → ErrorDialog, bez tworzenia pustego świata pod tym samym id. Nie wykonuj restore automatycznie.
8. D1: `StartedReadOnly(TerrainSemanticsMismatch, Message)` wyświetla wersje/liczbę operacji/OpId zawarte w Message oraz dwa wybory: Back i `RecoverCheckpointButton`. Ten drugi wymaga osobnego potwierdzenia „Odrzucić zmiany po checkpoincie? Najpierw powstanie kopia pre-recover.” Dopiero po release restart z RecoverFromCheckpoint=true; DiscardJournalTail pozostaje w istniejącym ServerRuntime. Nie uruchamiaj recovery klientem zdalnym. Zwykły pełny dysk nie pokazuje tej opcji.
9. PauseMenu: Resume, Save (solo/host), Invite (aktywne Steam lobby), ReturnToMenu, Quit. Escape otwiera Pause, zwalnia mysz; ponowne Escape/Resume zamyka, przywraca capture. Serwer sieciowy nadal tyka; wszystkie gameplay inputs (ruch i narzędzia) zablokowane, gdy modal/menu ma focus, nie tylko ukryte. Quit idzie QuitCoordinator.RequestStop; ReturnToMenu RequestShutdown/ReleaseStep bez Quit procesu. Na timeout/fault release wątku: pokaż błąd i wymagaj wyjścia procesu, nie uruchamiaj następnego świata obok żywego workera. Dopiero skuteczne release pozwala ponownie startować.
10. Steam: konsumuj controller K3-17 przez nowy lokalny `IUiLobbyService` adapter (Host/Join/Invite/Leave/lista/status/confirm); domyślna implementacja unavailable umożliwia samodzielny odbiór IP/menu przed K3-17. Typy UiLobbyVisibility/UiLobbyError/UiLobbyFriend należą do 18 i nie referują jeszcze nieistniejących typów Steam z 17; adapter mapuje je jawnie po scaleniu 17. Lista znajomych i join request podłączone po scaleniu 17; sam moduł UI buduje się wcześniej z unavailable/fake. Zaproszenie przy aktywnym świecie: osobny modal zgody, potem D8 i join; Back/Cancel bez zmiany świata. Błąd SDK/lobby używa lokalnego mapowania LobbyError, a zakończenie hosta pokazuje „Host zakończył sesję” i ReturnToMenu, bez auto-reconnect.

### Ostrzeżenia i czytelna odmowa dołączenia

1. Rezerwacja: K3-15 MessageType 21, tutaj 22 ServerStatus i 23 JoinUnavailable, oba ReliableOrdered, format 1. Payload ServerStatus: bool jako bajt 0/1, reason byte, string z limitem **256 bajtów UTF-8**; JoinUnavailable: reason byte + ten sam string. Reason: 0=Running (tylko ServerStatus.ReadOnly=false), 1=TerrainSemanticsMismatch, 2=WriterFault. Nie dodawaj tych lokalnych powodów do zamrożonego HandshakeRejectCode. Nieznany reason przy prawidłowej ramce wyświetla ogólny błąd; zły bool/truncated/oversized to ProtocolViolation.
2. `GameServerHooks` przy `_acceptsChanges()==false` rzuca **nowy** JoinUnavailableException przed PreparePlayerJoin; źródło reason/detail wstrzyknij z ServerRuntime. ServerSession.HandleHello najpierw dotychczasowa walidacja build/password/identity, potem try/catch tylko wokół OnPlayerJoining: ten konkretny wyjątek → SendMessage(peer, JoinUnavailable), Closing i dotychczasowy CloseDelay; bez PlayerId, join/leave, CreateHello lub checkpointu. Inne wyjątki nadal istniejący Fail. Tryb zamykania sesji nadal obsługuje dotychczasową odmowę, nie podszywa się pod WriterFault.
3. MessageRouter: jawna lista nowych kodów w TryGetType (nie zakres, który przepuści nieprzydzielone 24–29); JoinUnavailable dozwolone S→K tylko AwaitingServerHello, ServerStatus tylko Loading/InGame; żadna nowa ścieżka K→S. MessageChannels musi zawierać oba. ClientSession.Receive obsługuje JoinUnavailable przed ServerHello: przekaż do `IClientGameHooks.OnMessage`, zapamiętaj lokalny wynik i rozpocznij Close; ClientRuntime przekazuje do listenera UI. Nie wywołuj OnServerHello/SendReady. OnDisconnected nie nadpisuje wcześniej wyświetlonej przyczyny generic Timeout/Normal.
4. Nowy `ServerStatusBroadcaster` w `src/Horyzont.Server/UI/` implementuje istniejące tick/player observer: snapshot aktualnego statusu na OnPlayerReady, następnie komunikat tylko przy zmianie Running↔SafeMode; rejestracja przez istniejące AddTickParticipant/AddPlayerObserver, stan przed wysłaniem. Lokalny owner widzi także bezpośredni runtime status, gdy join odrzucony w read-only start. Detail ogólny, bez ścieżek dysku/stacktrace/sekretów; techniczny błąd w lokalnym logu ownera.
5. SafeMode przy WriterFault: trwały banner „Zapis nie działa. Zmiany są wstrzymane.”, intencje blokowane w UI, ale serwer nadal waliduje każdy request. Save/Retry tylko owner solo/host przez RequestCheckpoint; warning znika dopiero po potwierdzonym Running, nie od naciśnięcia przycisku. Pending predictions kończą się przez istniejące odpowiedzi/timeout, nie przez dopisywanie operacji w UI.
6. K2-13 deferral GameRules: `ToolHud.RejectKey` dalej źródłem nazw odmów; dla limitów/zasięgu/cooldownu dodaj tekst „Serwer stosuje własne limity; podgląd jest orientacyjny.” Nie pokazuj zgadywanej liczby skonfigurowanego limitu. HUD prezentuje reason i pending; nie próbuj obejść serwerowego limitera. Ostrzeżenie nie jest przesyłaniem GameRules.
7. D4: każde scalenie zmian Core Protocol/Serialization ustawia ProtocolVersion=(main przy scaleniu)+1; po samym K2 4, po wcześniejszym K3-15/16 odpowiednio następna wartość. Test udokumentowanej wartości i plan zaktualizuj po rebase. K3-18 nie zmienia operacji/intencji ani blobów; potrzeba takiej zmiany wymaga wersji i replay starego formatu z Opus. Zakres Planety MessageType 21–29, RejectReason 21–29 (21 ServerReadOnly zajęte), OperationKind 8–19, IntentCode 4–9, BlobKind 5–9 (5 TerrainBlock zajęte). D1 i złoty korpus pozostają bez zmian.

### HUD i koszty

DeveloperHud jest jednym CanvasLayer z Label `MetricsLabel` i Label `ToolsLabel`, niewidoczny domyślnie, nowa akcja `dev_hud_toggle` F3 (F/F8/F9 już zajęte). Odświeżaj tekst 4 Hz, nigdy w ticku symulacji. FPS = 1/średnia ostatnich 60 dodatnich delta render; tick = bieżący numer serwera albo ostatni ServerHello/Snapshot u zdalnego klienta. Tick ms tylko lokalny pomiar w ServerNode wokół Runtime.Tick, przez TimeProvider.GetTimestamp/GetElapsedTime, **poza** deterministyczną symulacją. Zdalny tick ms „—”, nie RTT. Pasmo = różnica TransportStats.BytesSent/Received podzielona przez czas między próbami; jednostka KiB/s (1024), cel spec 50 KB/s opisany w raporcie w jednostkach dziesiętnych. Pierwsza próba i zmiana sesji: „—”, reset liczników; sumuj połączenia serwera przy host stats, osobno lokalny klient, bez podwójnego dodania do jednego licznika. RTT z ClientSession.Rtt; solo „—”. Pending/reject/reason z ToolNode, orientacja/camera z ILocalPlayerView. Brak narzędzi/replikacji pokazuje „—”. UI nie alokuje w pobieraniu metryk co tick; formatowanie stringów 4 Hz dozwolone poza tickiem. Adaptery odczytu i ring testowane zero alokacji po warmup.

### Równoległość i mapa kolizji

K3-18 może ruszyć po K2-10 równolegle z 14/15/16/17. UI/WorldCatalog/HudSampler i testy .NET są własne; adapter Steam zaczyna od fake, adapter replikacji od brakujących wartości. Integracja wspólnych plików przez rebase, bez zmiany grafu.

| Wspólne pliki | Rozdział i ryzyko |
| --- | --- |
| `GameComposition.cs`, `Boot.cs`, `QuitCoordinator.cs`, `LaunchOptions.cs` | 15/16 replikacja, 17 Steam, 18 lifecycle menu; każdy własny binder, integracja 15→16→17→18, 18 właściciel wystawienia WorldStartResult i powrotu do menu |
| `MessageType.cs`, `MessageChannels.cs`, `MessageCodec.cs`, `MessageRouter.cs`, `ProtocolConstants.cs` | 15 rezerwuje 21, 18 22–23, 16 istniejące wiadomości terenu; rebase po każdym scaleniu i nowy D4 |
| `ServerSession.HandleHello`, `GameServerHooks`, `ServerRuntime` | 18 kontrolowana odmowa i status, 15 registry handlerów, 16 terrain; nie rozbudowuj drugiego switcha OnClientMessage |
| `LocalPlayer` / input / `ToolNode` | 14 ruch, 15/16 zdalne narzędzia, 18 jedynie nowa wstrzyknięta bramka `IGameplayInputGate.IsBlocked`; ownership matematyki ruchu pozostaje 14 |
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
| `Start_AllWorldStartResults_ShowsExpectedScreen` | każdy wariant z realnego WorldStartResult, versions/OpId widoczne; błąd nie kończy procesu UI |
| `Restore_CorruptedWithBackups_RequiresConfirmationAndRelease` | zgoda, odmowa, brak kopii i IO failure; oryginał zachowany, restore nigdy przy aktywnym runtime |
| `Recover_TerrainSemanticsMismatch_BackupsBeforeDiscard` | warning D1, osobna zgoda i restart; bez zgody cały ogon/hash niezmieniony |
| `Join_ReadOnlyServer_ReceivesJoinUnavailableWithoutPlayerJoin` | reason 1/2 przed ServerHello, zero join/leave/writer operations; inne build nadal frozen HandshakeReject |
| `Codec_StatusAndJoinUnavailable_RoundTripsAndRejectsOversize` | kody 22/23, format 1, max256 UTF-8, bool, truncation/fuzz, nieznany reason i router states/channels |
| `Status_WriterFaultThenCommit_BroadcastsTransitionsOnce` | join podczas SafeMode, 2 klienci w grze; warning raz, retry bez przedwczesnego Running |
| `Disconnect_AfterSpecificError_PreservesReadableReason` | read-only/host exit/update/wrong password/timeout/kick; późny generic disconnect nie zaciera detalu |
| `Pause_FocusedModal_BlocksMovementAndToolsButServerTicks` | Esc, tekst formularza, LPM nad przyciskiem; brak input/intencji gry, serwer dalej 60 Hz |
| `ReturnToMenu_ActiveHost_DrainsReleasesAndAllowsNewWorld` | kolejność D8, save, listen sockets, workers/listeners; po nieudanym release nowe starty zablokowane |
| `Steam_InviteDuringGame_WaitsForConfirmationAndOldWorldStop` | fake lobby, odmowa/zgoda, missing SDK, friends i join; realne invite Manual K3-17 |
| `Sample_KnownCountersAndSessionReset_ComputesActualBandwidth` | różnice bajtów/czasu, pierwszy sample, reset, brak lokalnych metryk; tick ms nie RTT |
| `Sample_WarmedUp10000Calls_AllocatesZeroBytes` | odczyt metryk/ring poza formatowaniem; zero GC allocation |
| `Hud_ToolsRejectAndConfiguredLimits_ShowsReasonWithoutGuessing` | Pending, ServerReadOnly i odmowa limitu; tekst orientacyjności, brak zgadywanego limitu |
| `Ui_SoloHostJoinBackAndRestore_ButtonsFollowState` | gdUnit4 szeregowo: faktyczne sceny, click/focus/Esc, jeden HUD, brak niewidocznych aktywnych przycisków |

## Kroki

- [ ] 0. Potwierdź K2-10 na main i realne nazwy; uzgodnij kody 22/23 z K3-15/16 i bindery z 17. Sonnet dostarcza testy.
- [ ] 1. Dodaj nowe modele/controller/parser/catalog/error mapper; czerwone→zielone testy .NET, bez Godota.
- [ ] 2. Dodaj ServerStatus/JoinUnavailable, kodek/channels/router, kontrolowaną odmowę i D4; Opus przegląda przed UI integracją.
- [ ] 3. Wystaw WorldStartResult oraz status broadcaster bez parsowania logu; przetestuj safe mode i D1.
- [ ] 4. Utwórz dokładne sceny i ui.csv, podłącz MainMenu/WorldList/Join/Loading/Error; zostaw CLI bez menu.
- [ ] 5. Dodaj cancel/restore/recovery i lifecycle powrotu do menu, input gate i Pause; żadnych lokalnych zmian świata.
- [ ] 6. Dodaj HUD 4 Hz/F3 i zastąp starą instancję ToolHud; podłącz fake Steam, następnie adapter K3-17 po rebase.
- [ ] 7. Testy scen szeregowo, ręcznie solo→menu→host→menu→join, modal i save warning; raport i zrzuty w PR.
- [ ] 8. Wykonawca uruchamia build/test/format/smoke z ZASADY pkt 10, Windows CI, przegląd Sonnet/Opus, aktualizacja D4 po rebase i zielone CI przed scaleniem.

## Odbiór

Menu realizuje wszystkie trzy tryby i listę istniejących światów, błędy startu nie zamykają aplikacji UI, restore/recovery wyłącznie po zgodzie, warning i odmowa read-only czytelne również u klienta zdalnego. HUD pokazuje FPS/tick/pasmo z poprawnymi jednostkami i stan narzędzi; modal blokuje gameplay. CLI/dedicated/diagnostyka bez regresji. CI wyłącznie jeden self-hosted Windows, testy silnika bez równoległości. Nie przywracaj Linux CI; kryteria platformowe całego podprojektu pozostają do dalszego odbioru właściciela.

## Odstępstwa i decyzje wykonawcy

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
