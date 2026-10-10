# K3-17 · Steam: lobby i zaproszenia

| | |
| --- | --- |
| Prowadzi | Gemini 3.8 Flash (High) |
| Testy akceptacyjne | Sonnet 5.5, przed kodem; wykonawca nie zmienia ich bez zgody |
| Przegląd | Sonnet 5.5; Opus 5.5 przy zmianie kontraktu Core, drugi przegląd planu przez Opus na bramce K2; Opus 5.5 dla reguły tożsamości w `HandshakeValidator` (17.3) |
| Zależy od | K3-00 (szwy); K1-06, K2-10; niezależny od pozostałych K3, integracja menu przez K3-18 |
| Gałąź | `pakiet/k3-17-steam-lobby` |

**Cel:** host publikuje lobby Steam, a zaproszony znajomy przechodzi z nakładki lub listy znajomych do istniejącej sesji P2P bez przekierowywania portów.

## Wspólne ustalenia K3

Domyślnie start po małym PR [K3-00 szwy](K3-00-szwy.md) na bramce K2. Obowiązują [README: X1–X11](README.md#x1-szwy-przed-startem-pakietów): jeden dispatch S1, budżety S4, pełne migawki S5, readiness S6, viewers S7, narzędzia S8 i własne partial S9. Rekomendowane scalanie 15 → 16 → 14 → 17 → 18; integrację wspólnego punktu wykonuje pakiet scalany później, kolejność nie warunkuje startu. Bez odpowiedzi właściciela pakiet rusza z wariantem domyślnym.

## Kontekst do przeczytania

- `docs/ZASADY.md`: punkty 2, 3, 6–8; `docs/plans/README.md:78–80`: D1/D4/D8.
- `docs/plan/plan-projektu.md:98`: właściciel, zależności i odbiór K3-17.
- `docs/spec/01-planeta.md:194–218`: transport, handshake, sesja i wyjście hosta; `:233–245`: 8 graczy, cel pasma, lobby private/friends/public, App ID 480, potwierdzenie tożsamości P2P; `:299–309`: odmowy i disconnect; `:338`: 8 osób przez zaproszenie.
- `docs/plans/K2-10-tryby-gry.md:63,151,367,601`: transport Steam w hoście i podmiana Dev identity; `K1-06-transporty.md`: kontrakt transportu, wyłączność właściciela SteamAPI. K3-18 konsumuje kontrakt lobby poniżej.

### Kod, na którym pakiet buduje

Weryfikacja w bazie `pakiet/k2-bramka`; poniższe symbole istnieją.

| Typ / składowa | Plik:linia | Użycie |
| --- | --- | --- |
| `SteamTransport(TransportOptions)` | `src/Horyzont.Net/Transport/Steam/SteamTransport.cs:38` | istniejący Steam Networking Sockets, wymaga zewnętrznego Init/RunCallbacks |
| `SteamTransport.Listen`, `Poll`, `Dispose`, `GetRemoteEndpoint` | ten sam plik `:56`, `:146`, `:215`, `:209` | listen na virtual port 0, pompka, identyfikator z transportu |
| `SteamTransport.IsDrained` | ten sam plik `:47` | true; linger obsługuje Steam, nie zmieniaj kontraktu |
| `SteamEndpoint(ulong SteamId)` | `src/Horyzont.Net/Transport/SteamEndpoint.cs:5` | endpoint P2P, nie identyfikator lobby |
| `CompositeTransport(IReadOnlyList<ITransport>)` | `src/Horyzont.Net/Transport/CompositeTransport.cs:27` | właściciel wewnętrznych transportów |
| `ServerRuntimeOptions.Listen` | `src/Horyzont.Server/ServerRuntimeOptions.cs:25` | dodanie Steam obok UDP i InMemory |
| `ClientSession.Connect` | `src/Horyzont.Net/Session/ClientSession.cs:90` | endpoint + PlatformIdentity + nazwa + hasło |
| `ClientRuntime.Connect` | `src/Horyzont.Client/Session/ClientRuntime.cs:71` | adapter sesji |
| `GameComposition.Create`, `StartAsync`, `RequestShutdown`, `ReleaseStep` | `game/src/Boot/GameComposition.cs:162`, `:459`, `:525`, `:551` | composition root i lifecycle |
| `LaunchOptions.Mode`, `Password`, `Parse` | `game/src/Boot/LaunchOptions.cs:28`, `:40`, `:98` | tryby i istniejące hasło |
| `QuitCoordinator.RequestStop` | `game/src/Boot/QuitCoordinator.cs:68` | wspólne zakończenie procesu |
| `ServerSession.GetEndpoint` | `src/Horyzont.Net/Session/ServerSession.cs:201` | endpoint sesji; w handshake korzystaj z endpointu transportu przed dołączeniem |
| `HandshakeValidator.Validate` kontrola SteamID | `src/Horyzont.Net/Session/HandshakeValidator.cs:36` | porównanie identity z SteamEndpoint już istnieje; zachowaj i pokryj testem |
| `Steamworks.NET` 2024.8.0 | `Directory.Packages.props:12`, `src/Horyzont.Net/Horyzont.Net.csproj:4` | już wybrana biblioteka; bez wymiany SDK |

## Zakres

Właściciel SteamAPI, lobby trzech widoczności, metadane, zaproszenia i lista znajomych grających w Horyzont, obsługa zaproszenia przy działającej grze i startu `+connect_lobby`, SteamTransport w hoście/kliencie, właściwa tożsamość P2P, błędy i sprzątanie. Wszystkie odroczenia K2-10 wymienione w kontekście przejęte.

## Poza zakresem

K9-58: bilety UDP, autoryzacja dedicated Steam, achievementy i pełna integracja platformy. Brak migracji hosta, matchmakingu/rankingu, warsztatu, trwałych banów i browsera publicznych serwerów. UI przycisków/okien implementuje K3-18. Lobby publiczne można tworzyć i dołączać przez identyfikator/zaproszenie; globalny browser nie jest wymagany przez specyfikację. Replikacja świata pozostaje K3-15/16.

## Projekt

### Pliki i kontrakty

Każdy wymieniony poniżej typ/metoda jest **nowym API**. Logika testowalna bez Godota; Steamworks tylko w adapterze. Dodaj referencję Steamworks.NET do Client wyłącznie na potrzeby adaptera platformy; nie zmieniaj wersji pakietu.

| Nowy plik | Kontrakt |
| --- | --- |
| `src/Horyzont.Client/Steam/LobbyVisibility.cs` | enum `Private`, `FriendsOnly`, `Public` |
| `src/Horyzont.Client/Steam/LobbyState.cs` | enum `Unavailable`, `Idle`, `Creating`, `Hosting`, `Joining`, `Joined`, `Leaving`, `Failed` |
| `src/Horyzont.Client/Steam/LobbyInfo.cs` | `readonly record struct LobbyInfo(ulong LobbyId, ulong HostSteamId, LobbyVisibility Visibility, int Members, int Capacity, string BuildHash, ushort ProtocolVersion)` |
| `src/Horyzont.Client/Steam/ISteamLobbyApi.cs` | `bool TryInitialize(uint appId)`, `void Pump()`, `void Shutdown()`, `ulong LocalSteamId { get; }`, `string DisplayName { get; }`; `void CreateLobby(LobbyVisibility visibility, int capacity, uint requestId)`, `void JoinLobby(ulong lobbyId, uint requestId)`, `void LeaveLobby(ulong lobbyId)`, `bool SetData(ulong lobbyId, string key, string value)`, `string GetData(ulong lobbyId, string key)`, `ulong GetOwner(ulong lobbyId)`, `bool ShowInviteOverlay(ulong lobbyId)`; event `Action<LobbyApiResult> LobbyCreated`, `LobbyJoined`, event `Action<ulong> JoinRequested` |
| `src/Horyzont.Client/Steam/LobbyApiResult.cs` | `readonly record struct LobbyApiResult(uint RequestId, ulong LobbyId, LobbyError Error)`; requestId z closure konkretnego CallResult, również dla późnego callbacka |
| `src/Horyzont.Client/Steam/SteamworksLobbyApi.cs` | adapter Steamworks.NET; callbacki trzymane w polach do Dispose; tłumaczy wynik SDK na nowy `LobbyError`, bez wyjątków w controllerze |
| `src/Horyzont.Client/Steam/LobbyError.cs` | enum `None`, `Unavailable`, `Timeout`, `Full`, `AccessDenied`, `InvalidMetadata`, `IncompatibleBuild`, `OwnerChanged`, `Cancelled`, `PlatformFailure`; lokalne wyniki, nie kody wire |
| `src/Horyzont.Client/Steam/SteamLobbyController.cs` | `LobbyState State { get; }`, `LobbyInfo? Current { get; }`; `void Host(LobbyVisibility visibility, int capacity)`, `void Join(ulong lobbyId)`, `void Invite()`, `void Leave()`, `void Update()`; `event Action<SteamEndpoint, bool>? ConnectRequested` (drugi parametr: wymagane hasło), `event Action<LobbyError>? Failed`, `event Action<ulong>? JoinConfirmationRequested`, `event Action<LobbyState>? StateChanged`, `void ConfirmJoin(ulong lobbyId)`, `void DeclineJoin()`; konstruktor `(ISteamLobbyApi api, TimeProvider time, Func<bool> isSessionActive, ILogger<SteamLobbyController> logger)` |
| `src/Horyzont.Client/Steam/SteamFriend.cs` | `readonly record struct SteamFriend(ulong SteamId, string Name, ulong LobbyId)`; API `int CopyJoinableFriends(Span<SteamFriend> destination)` w ISteamLobbyApi |
| `game/src/Boot/SteamPlatformServices.cs` | Nowy obiekt procesowy: Api/Lobby/Lifetime, `TryShutdown()` (Leave lobby → Dispose callbacków → `SteamAPI.Shutdown()`, zwraca true); własność X8 |
| `game/src/Boot/SteamLifetime.cs` | Procesowy Node, dziecko `Boot`: `Initialize(ISteamLobbyApi api)`, `_Process` pompuje callbacki raz/klatkę; inicjalizacja i shutdown raz/proces |
| `game/src/Boot/SteamSessionBinder.cs` | składanie transportu/identity/lobby; działa tylko w Host i Client, eventy publikowane do UI; solo/dedicated nie wymagają Steam |
| `tests/Horyzont.Client.Tests/Steam/`, `tests/Horyzont.Testing/FakeSteamLobbyApi.cs`, `game/tests/Steam/` | testy deterministyczne i osobne Manual |

Nowe pola `LaunchOptions`: `ulong? ConnectLobbyId`, `bool EnableSteam`, `LobbyVisibility LobbyVisibility = FriendsOnly`, `uint SteamAppId = 480`. Nowe argumenty: `--steam`, `--steam-app-id <uint>`, `--lobby-visibility private|friends|public`, `+connect_lobby <ulong>` (ustawia Mode=Client i EnableSteam=true, nie wymaga --connect; konflikt z --solo/--host/--server jest błędem argumentów). Nie przechowuj sekretów w metadanych. `Boot` dołącza do argumentów użytkownika parę `+connect_lobby <id>` znalezioną w `OS.GetCmdlineArgs()` (tylko ten token i jedna wartość); `LaunchOptions.IsModeRequested` zwraca true przy `+connect_lobby` (także w trybie menu K3-18 — omija menu). Modyfikacje istniejących plików: `LaunchOptions.cs`, `GameEnvironment.cs` (nowe init `Platform: SteamPlatformServices?`, `ProcessCleanup: Func<bool>?`, domyślnie null), `GameComposition.Platform.cs` (S9), `Boot.cs`, `HandshakeValidator.cs` (17.3), `.gitignore` (`steam_appid.txt`). Walidacja P2P identity istnieje w HandshakeValidator i nie wymaga zmiany ServerSession. Kontrakty konsumowane: tabela istniejącego kodu; wystawiane dla K3-18: controller, friend list, visibility, status i błędy.

### Mechaniczna procedura i decyzje

1. Steam SDK nie uruchamia się w Core/Server/transportowym konstruktorze. `SteamPlatformServices` (`game/src/Boot/SteamPlatformServices.cs`) i węzeł `SteamLifetime` są procesowe: tworzy je `Boot` (CLI `--steam`/`+connect_lobby`) albo `GameUiCoordinator` (menu, przy pierwszym użyciu Steam) przez `GameComposition.CreatePlatformServices(env, appId)`; `SteamLifetime` jest dzieckiem `Boot`; kompozycja dostaje je przez nowe `GameEnvironment.Platform`; `TryShutdown()` podpięte jako `GameEnvironment.ProcessCleanup` (X8 pkt 1 i 4). Init raz/proces, RunCallbacks raz/klatkę na głównym wątku; kolejne sesje korzystają z tych samych usług. Dedykowany/solo/engine-tests/smoke bez Steam. Przed `SteamAPI.Init` ustaw w procesie zmienne środowiskowe `SteamAppId` i `SteamGameId` na `LaunchOptions.SteamAppId` (domyślnie 480); kod nie tworzy `steam_appid.txt`; dodać `steam_appid.txt` do `.gitignore`. Jeśli Manual pokaże, że zmienna nie wystarcza, zapisać to w Odstępstwach i użyć nieśledzonego pliku obok pliku wykonywalnego. Nie commituj prawdziwego App ID ani nie ustawiaj 480 w produkcyjnym eksporcie.
2. Adapter korzysta z rodzin SDK `SteamAPI`, `SteamMatchmaking`, `SteamFriends` w istniejącym Steamworks.NET 2024.8.0. Nazwy API poniżej potwierdzić kompilacją z SDK 2024.8.0; semantyki nie zmieniać. Mapowanie jest ustalone: Private→private, FriendsOnly→friends only, Public→public; create/join jako asynchroniczne CallResult, join-request jako Callback. Callback wyłącznie aktualizuje controller/kolejkę; nie buduje sceny i nie blokuje.
3. Host najpierw uruchamia świat i listener P2P na virtual port 0. CompositeTransport zawiera dokładnie jeden InMemory z istniejącej sieci hosta, UDP 27015 i SteamTransport. Dopiero po udanym starcie świata `Host(FriendsOnly, configuredMaxPlayers)`; capacity = `ServerOptions.MaxPlayers` hosta. Błąd lobby nie niszczy zapisu/hosta LAN; pokaż błąd i pozwól ponowić utworzenie lobby. Solo nie nasłuchuje Steam. Lobby nie jest transportem.
4. Po wyniku create ustaw klucze: `horyzont_schema=1`, `horyzont_host=<SteamId decimal>`, `horyzont_build=<ten sam build hash co handshake>`, `horyzont_protocol=<ProtocolVersion decimal>`, `horyzont_world=<WorldId D>`, `horyzont_capacity=<liczba decimal>`, `horyzont_password=<0|1>` (czy host ma hasło; nigdy samo hasło), `horyzont_ready=1`. Najpierw wszystkie pola, ready ostatni; maks. wartość 128 znaków, parse InvariantCulture/TryParse. Błąd SetData → leave i Failed. Nigdzie nie publikuj hasła ani lokalnej ścieżki świata. Lobby graczy jest informacyjne; limity i hasło egzekwuje sesja.
5. Join request i `+connect_lobby` korzystają z tego samego `Join`. Id musi być niezerowe. Controller przechodzi Idle/Failed→Joining, czeka na wynik JoinLobby maks. 10 s (TimeProvider), następnie sprawdza wszystkie metadane, owner==host, ready==1, schema==1 i zgodność build/protocol. Dopiero wtedy emituje `ConnectRequested(new SteamEndpoint(host), passwordRequired)`. Przy `horyzont_password=1` `ConnectRequested(host, true)`: K3-18 pyta o hasło przed połączeniem; CLI używa `--password`, a bez niego połączenie kończy się istniejącym `BadPassword`. WorldId informacyjny; endpoint z owner/data, nigdy z lobbyId. Metadane nie zastępują handshake serwera.
6. Invite wywołuje nakładkę tylko Hosting/Joined i przy aktywnym lobby, bez samodzielnego wysyłania wiadomości znajomym. Lista znajomych: maks. 256 wyników, tylko ten sam App ID i niezerowe lobbyId, posortowane `StringComparer.Ordinal` po `Name`, potem `SteamId` rosnąco; odświeżenie na otwarcie panelu i co 5 s przy widocznym panelu. Join z listy korzysta z punktu 5. Nazwy są zwykłym tekstem, bez BBCode.
7. Przy zaproszeniu podczas gry controller emituje nowy lokalny `JoinConfirmationRequested(ulong lobbyId)` zamiast od razu opuszczać świat. K3-18 wyświetla „Zakończyć obecną sesję i dołączyć?”; po akceptacji obecne GameComposition.RequestShutdown/ReleaseStep kończy sesję zgodnie z D8, a dopiero potem nowy join. Odmowa zostawia obecną sesję. Drugi request zastępuje oczekujący id; podwójny wynik callbacka nie tworzy drugiego klienta. W trybie CLI (brak UI) `JoinConfirmationRequested` nie ma subskrybenta: controller loguje Information i odrzuca żądanie.
8. Tożsamość Steam (`PlatformIdentity(IdentityPlatform.Steam, LocalSteamId)`) wyłącznie dla klienta łączącego się przez `SteamEndpoint`. Lokalny klient hosta (InMemory) i połączenia UDP zawsze używają profilu Dev (`LocalProfile`), więc postać hosta nie zmienia się po włączeniu Steam. `HandshakeValidator.Validate` dostaje regułę po linii 39: `hello.Identity.Platform == IdentityPlatform.Steam && context.RemoteEndpoint is not SteamEndpoint` → `HandshakeRejectCode.IdentityMismatch` (istniejący kod, format bez zmian). Zachowaj istniejące porównanie SteamID z endpointem P2P. Ta zmiana wymaga przeglądu Opus. Ta sama osoba przez Steam i przez IP to dwóch różnych graczy świata — akceptowane w K3; łączenie kont K9-58. Nie utożsamiaj SteamId z PlayerId/EntityId.
9. Controller w `Update()` co 1 s (`TimeProvider`) porównuje `GetOwner(lobby)` z zapamiętanym hostem. Owner zmieniony względem zapamiętanego hosta: leave, `OwnerChanged`, koniec sesji; żadnej migracji hosta. Host kończy sesję wszystkim istniejącym `Disconnect`, ustawia ready=0 i opuszcza lobby. Nie dopisuj kodu handshake. Wynik Full/AccessDenied/timeout pokazuje controllerowi lokalny LobbyError; UI tłumaczy w K3-18.
10. Anulowanie zwiększa generation token operacji. Późny successful join/create starej generacji od razu LeaveLobby; nie emituje ConnectRequested. Dla każdej operacji termin 10 s, jedno żądanie aktywne; możliwość retry dopiero po zakończeniu/anulowaniu. `Leave` idempotentne. Kontroler usuwa subskrypcje i CallResult przy Dispose.
11. D8: host ustawia `SetData(horyzont_ready, 0)` od razu przy `RequestShutdown`; wyjście sesji → drain → zapis/teren/ConstructWorld → Dispose composite i klienta → `LeaveLobby`. `SteamAPI.Shutdown` wyłącznie w `TryShutdown()` procesu (X8), nigdy przy wyjściu do menu. Callbacki działają do końca zamykania transportów. Przy przekroczeniu terminu zwolnienia sesji `SteamAPI.Shutdown` nie jest wołane (proces kończy się bez niego). CLI zachowuje `QuitCoordinator` kompozycji, cleanup = `() => composition.ReleaseStep() && (env.ProcessCleanup?.Invoke() ?? true)`. Menu ma procesowy koordynator X8; nie twórz koordynatora sesji.
12. Bez nowych kodów protokołu, operacji, intencji ani blobów i bez zmiany formatu. D4 stosuje się, gdy implementacja jednak wymaga Protocol/Serialization: wartość main przy scaleniu+1, Opus i aktualizacja istniejących `ProtocolVersion_MatchesDocumentedMergeValue` i `Constants_GoldenValues`, fixture X4–X6 i planu; nie rób tego dla lokalnego LobbyError. ClientHello prefix i HandshakeReject zamrożone. D1: semantyka terenu i korpus nietknięte. Logowanie generowane, bez authTicket/hasła, z lobbyId i peer/player tam, gdzie są znane.

### Mapowanie Steamworks.NET 2024.8.0 (17.4)

Nazwy projektowanego adaptera SDK sprawdzone wyszukiwaniem w tej gałęzi; poza używanym `k_EResultOK` gałąź nie zawiera jeszcze tych wywołań. Potwierdzenie nazw kompilacją należy do implementacji, mapowanie obowiązuje bez zmian.

| Operacja / wynik | Wywołanie / mapowanie |
| --- | --- |
| Create | `SteamMatchmaking.CreateLobby(ELobbyType, cMaxMembers)` + `CallResult<LobbyCreated_t>` |
| Widoczność | `Private→k_ELobbyTypePrivate`, `FriendsOnly→k_ELobbyTypeFriendsOnly`, `Public→k_ELobbyTypePublic` |
| `LobbyCreated_t.m_eResult` | `k_EResultOK→None`, `k_EResultTimeout→Timeout`, `k_EResultAccessDenied→AccessDenied`, inne → `PlatformFailure`; `bIOFailure→PlatformFailure` |
| Join | `SteamMatchmaking.JoinLobby` + `CallResult<LobbyEnter_t>` |
| `m_EChatRoomEnterResponse` | `Success→None`, `Full→Full`, `DoesntExist→Unavailable`, `NotAllowed/Banned/Limited/ClanDisabled/CommunityBan/MemberBlockedYou/YouBlockedMember→AccessDenied`, inne → `PlatformFailure` |
| Zaproszenie podczas gry | `Callback<GameLobbyJoinRequested_t>` (`m_steamIDLobby`) → `JoinRequested` |
| Znajomi | `GetFriendCount(EFriendFlags.k_EFriendFlagImmediate)`, `GetFriendByIndex`, `GetFriendGamePlayed(id, out FriendGameInfo_t)`; filtr `m_gameID.AppID() == appId && m_steamIDLobby != 0`; nazwa `GetFriendPersonaName` |
| Liczba / limit | `Members = GetNumLobbyMembers`, `Capacity` z metadanej |
| Nakładka | `SteamFriends.ActivateGameOverlayInviteDialog(lobby)` |

### Równoległość i mapa kolizji

Po K3-00 K3-17 rozpoczyna po K1-06/K2-10, równolegle z K3-14/15/16/18. Moduły Steam i fake nie współdzielą plików replikacji. K3-18 może używać ustalonych kontraktów controller/friends bez oczekiwania na działający Steam.

| Wspólna ścieżka | Ryzyko i rozdział |
| --- | --- |
| Kompozycja, `GameEnvironment.cs`, `Boot.cs` | 17 edytuje `GameComposition.Platform.cs` (S9), `GameEnvironment.cs` (`Platform`, `ProcessCleanup`), `Boot.cs` (obiekt procesowy, 17.2); rebase 17 → 18 |
| `LaunchOptions.cs`, `Boot.cs` | 17 argumenty Steam; 18 menu. Integruje pakiet scalany później (X10); kolejność scalania 15 → 16 → 14 → 17 → 18 |
| `ServerSession.cs` / `HandshakeValidator.cs` | 15 `SendEncoded`; 18 `HandleHello`/`GetTransportStats`; 17 zmienia `HandshakeValidator.cs` (jedna reguła 17.3) |
| `game/project.godot`, scena UI | właściciel 18, Steam nie dopisuje hotkeyów ani przycisków |
| testy silnika | pojedynczy Windows self-hosted `horyzont`, fixed pipe gdUnit4, brak równoległych procesów testów; Linux CI wyłączony 2026-10-09 |

## Testy akceptacyjne (napisz najpierw)

Sonnet dostarcza testy przed pracą Gemini. CI używa fake; realny Steam ma `Manual`, limit 120 s/scenariusz, raport w PR. Żadnego Thread.Sleep w testach fake.

| Nazwa | Scenariusz i wynik |
| --- | --- |
| `Host_ThreeVisibilityModes_WritesMetadataAndReadyLast` | private/friends/public, 8 miejsc, rzeczywisty WorldId/build/protocol; ready ostatni, hasło nieobecne |
| `Host_WorldStartFails_DoesNotCreateLobby` | corrupted/start failure; zero create/listen Steam po błędzie startu |
| `Host_LobbyCreateFails_KeepsLanWorldRunning` | błąd platformy, pokazany Failed, host i zapis nadal działają |
| `Join_ValidLobby_ConnectsToOwnerNotLobbyId` | różne host i lobby id; dokładnie jeden SteamEndpoint hosta |
| `Join_BadMetadataOrDifferentBuild_DoesNotConnect` | każda brakująca wartość, id 0, oversized, capacity, owner mismatch, schema/protocol/build; lokalny czytelny wynik |
| `Join_FullDeniedAndTimeout_ReturnsExpectedError` | mapowanie wyników SDK i 10 s z FakeTimeProvider |
| `Join_CancelThenLateSuccess_LeavesWithoutConnecting` | generation token, retry i podwójny callback |
| `Invite_InactiveLobby_DoesNotOpenOverlay` | Idle/Failed versus Hosting; tylko właściwy id |
| `Friends_OtherAppAndNoLobby_FiltersAndSorts` | limit 256, Name ordinal+SteamId, refresh 5 s |
| `Launch_SteamStyleArgsWithoutSeparator_DetectsConnectLobby` | `OS.GetCmdlineArgs()` z `+connect_lobby 123` bez `--`; tylko token i wartość dodane do user args, menu pominięte |
| `Handshake_SteamIdentityOverUdp_IsRejected` | Steam identity przez UDP → IdentityMismatch, zero operacji join |
| `Host_LocalClient_UsesDevIdentityWithSteamEnabled` | Włączenie Steam nie zmienia profilu Dev lokalnej postaci hosta |
| `Join_PasswordProtectedLobby_RequestsPassword` | `horyzont_password=1` → `ConnectRequested(host, true)`, brak publikacji hasła |
| `Shutdown_ReturnToMenu_KeepsSdkAlive` | Fake: wyjście do menu nie woła Shutdown, kolejny host używa tej samej instancji |
| `Owner_ChangedDuringSession_EndsWithOwnerChanged` | Polling co 1 s, FakeTimeProvider; leave i koniec sesji bez migracji |
| `Launch_ConnectLobbyArgument_UsesSameJoinFlow` | `+connect_lobby 123`, błąd braku/zera/przepełnienia; tryb Client |
| `JoinRequest_ActiveWorld_WaitsForConfirmationAndShutdown` | anulowanie zachowuje świat, zgoda uruchamia join po ReleaseStep, brak równoległych sesji |
| `Handshake_ForgedP2PIdentity_DoesNotApplyPlayerJoin` | transportowy SteamEndpoint inny niż claimed; zero zmian i zapisów świata |
| `Shutdown_HostLeaves_DisconnectsPeersAndShutsSdkLast` | kolejność D8, owner change, powtórzony Leave/Dispose; timeout Dispose nie wyłącza SDK spod transportu |
| `SteamLifetime_MultipleSessions_InitializesAndPumpsOnce` | host→menu→client, jeden Init i jedna pompka/klatkę |
| `SteamInvite_TwoAccounts_JoinsHostThroughRelay` | Manual: 2 konta i 2 PC/sieci bez port-forward, App ID 480; zaproszenie nakładki, lista znajomych, restart z +connect_lobby; zapis logów |
| `SteamLobby_EightPlayersThenHostExit_AllReceiveSessionEnd` | Manual: 8 kont/botów z osobnymi kontami Steam, timeout 120 s dla join/exit, brak migracji; jeśli brak kont raport pending, finalny scenariusz skoordynowany w K4-19 |

## Kroki

- [ ] 0. Sprawdź scalone zależności i K3-00; nazwy API SDK 2024.8.0 potwierdź kompilacją; Sonnet zatwierdza fake/API i tabelę testów. Zapisz różnice w Odstępstwach.
- [ ] 1. Sonnet pisze testy akceptacyjne; Gemini dodaje tylko wymagane typy kontraktów, aby testy się kompilowały i były czerwone.
- [ ] 2. Zaimplementuj controller z fake: stany, dane, limity, czas, generacje, friends i confirm.
- [ ] 3. Dodaj mechaniczny adapter SDK, rejestracje callbacków oraz SteamLifetime; smoke/engine-tests bez Steam.
- [ ] 4. Dodaj argumenty i SteamSessionBinder; host composite/identity, klient endpoint ownera, walidacja P2P.
- [ ] 5. Zintegruj start świata→lobby i lifecycle D8; K3-18 podłącza UI do wystawionych zdarzeń.
- [ ] 6. Wykonaj testy fake, testy lifecycle silnika szeregowo i Manual na dwóch kontach; wpisz dowody/dochodzące ograniczenia do PR.
- [ ] 7. Wykonawca uruchamia build/test/format/smoke z ZASADY pkt 10 na Windows, rebase wspólnych fragmentów, przegląd Sonnet (Opus dla Core), zielone CI.

## Odbiór

Udokumentowane dołączenie przez Steam invite i listę znajomych do hosta P2P, bez przekierowania portów; wszystkie trzy widoczności, czytelne odmowy, poprawny owner i tożsamość, brak wycieku callbacków po retry/wyjściu. Fake zielony w CI Windows; Manual nie jest zastępowany fake. Jeśli brak środowiska Steam, implementację można skierować do przeglądu, lecz odbiór pakietu pozostaje pending do dowodu Manual. Nie przywracaj Linux CI ani nie dodawaj drugiego runnera.

## Odstępstwa i decyzje wykonawcy

- wykonawca: GPT-6.1 Sol (luna; Antigravity niedostępny), przegląd Opus 5.5.
- 2026-10-10: krok 0 rozpoczęty na właściwej gałęzi, czysty worktree, baza zawiera S1–S9 K3-00. Blokada zakresu: S9 `ConfigurePlatform()` wywołuje się po skonstruowaniu ServerRuntime/ClientRuntime z transportami. Nie ma szwów wyboru transportu/listenerów, startu połączenia Steam ani lifecycle platformy. Wymagane przez pkt 3 i 11 procedury zmiany leżą w `GameComposition.cs`, którego edycję przez K3-17 zabrania X10. Szczegóły i pytanie do przeglądu: [K3-17-opis-pr.md](K3-17-opis-pr.md). Bez zmian implementacji do rozstrzygnięcia sprzeczności (ZASADY pkt 1 i krok 4 instrukcji agentów).
- Zgodnie z poleceniem użytkownika Godot, gdUnit i smoke silnika nie będą uruchamiane lokalnie; Manual oraz przegląd pozostają pending. Nie podbito ProtocolVersion, nie zmieniono kontraktów ani plików innych pakietów.

## Pułapki

- SteamEndpoint przyjmuje SteamID hosta, nie lobbyID.
- Nie inicjalizuj Steam dwa razy i nie wyłączaj SDK przed Dispose SteamTransport.
- Steamworks.NET używa własnych callbacków; nie zastępuj go przykładem z Facepunch.
- App ID 480 współdzielą inne gry; filtruj metadane, build i schema.
- Zmiana właściciela lobby nie daje prawa do migracji świata; pozostaje wyjście hosta.
- Steam identity przez UDP jest odrzucana. Ta sama osoba przez Steam i IP ma dwóch różnych graczy świata w K3; łączenie kont K9-58.

## Pytania do właściciela

Warianty domyślne są decyzjami planu; wykonawca implementuje je bez wybierania architektury.

| Nr | Pytanie | Wariant domyślny |
| --- | --- | --- |
| P1 | Jakie App ID i widoczność stosować podczas K3? | 480, FriendsOnly; prawdziwe App ID podmieniane konfiguracją przy otrzymaniu, bez zmiany kontraktu |
| P2 | Czy dostępne są dwa konta/PC do ręcznego odbioru i osiem kont do pełnego scenariusza? | odbiór zaproszeń na dwóch kontach; 8 osób koordynowane z K4-19; brak dowodu wpisany jako pending Manual |
| X1 | Czy przyjmujemy K3-00 przed równoległym startem pakietów? | Domyślnie tak: S1–S9 na bramce K2 bez Core/protokołu; alternatywa po odrzuceniu: X10 „integruje pakiet scalany później”, K3-14 krok 6 i K3-16 krok 6 dopiero po scaleniu K3-15 |
