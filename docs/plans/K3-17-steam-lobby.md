# K3-17 · Steam: lobby i zaproszenia

| | |
| --- | --- |
| Prowadzi | Gemini 3.8 Flash (High) |
| Testy akceptacyjne | Sonnet 5.5, przed kodem; wykonawca nie zmienia ich bez zgody |
| Przegląd | Sonnet 5.5; Opus 5.5 przy zmianie kontraktu Core, drugi przegląd planu przez Opus na bramce K2 |
| Zależy od | K1-06, K2-10; niezależny od pozostałych K3, integracja menu przez K3-18 |
| Gałąź | `pakiet/k3-17-steam-lobby` |

**Cel:** host publikuje lobby Steam, a zaproszony znajomy przechodzi z nakładki lub listy znajomych do istniejącej sesji P2P bez przekierowywania portów.

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
| `src/Horyzont.Client/Steam/ISteamLobbyApi.cs` | `bool TryInitialize(uint appId)`, `void Pump()`, `void Shutdown()`, `ulong LocalSteamId { get; }`, `string DisplayName { get; }`; `void CreateLobby(LobbyVisibility visibility, int capacity, uint requestId)`, `void JoinLobby(ulong lobbyId, uint requestId)`, `void LeaveLobby(ulong lobbyId)`, `bool SetData(ulong lobbyId, string key, string value)`, `string GetData(ulong lobbyId, string key)`, `ulong GetOwner(ulong lobbyId)`, `bool ShowInviteOverlay(ulong lobbyId)`; event `Action<LobbyApiResult> LobbyCreated`, `LobbyJoined`, event `Action<ulong> JoinRequested`, `OwnerChanged` |
| `src/Horyzont.Client/Steam/LobbyApiResult.cs` | `readonly record struct LobbyApiResult(uint RequestId, ulong LobbyId, LobbyError Error)`; requestId z closure konkretnego CallResult, również dla późnego callbacka |
| `src/Horyzont.Client/Steam/SteamworksLobbyApi.cs` | adapter Steamworks.NET; callbacki trzymane w polach do Dispose; tłumaczy wynik SDK na nowy `LobbyError`, bez wyjątków w controllerze |
| `src/Horyzont.Client/Steam/LobbyError.cs` | enum `None`, `Unavailable`, `Timeout`, `Full`, `AccessDenied`, `InvalidMetadata`, `IncompatibleBuild`, `OwnerChanged`, `Cancelled`, `PlatformFailure`; lokalne wyniki, nie kody wire |
| `src/Horyzont.Client/Steam/SteamLobbyController.cs` | `LobbyState State { get; }`, `LobbyInfo? Current { get; }`; `void Host(LobbyVisibility visibility, int capacity)`, `void Join(ulong lobbyId)`, `void Invite()`, `void Leave()`, `void Update()`; event `Action<SteamEndpoint>? ConnectRequested`, event `Action<LobbyError>? Failed`; zegar `TimeProvider` |
| `src/Horyzont.Client/Steam/SteamFriend.cs` | `readonly record struct SteamFriend(ulong SteamId, string Name, ulong LobbyId)`; API `int CopyJoinableFriends(Span<SteamFriend> destination)` w ISteamLobbyApi |
| `game/src/Boot/SteamLifetime.cs` | Node: `Initialize(ISteamLobbyApi api)`, `_Process` pompuje callbacki raz/klatkę; inicjalizacja i shutdown raz/proces |
| `game/src/Boot/SteamSessionBinder.cs` | składanie transportu/identity/lobby; działa tylko w Host i Client, eventy publikowane do UI; solo/dedicated nie wymagają Steam |
| `tests/Horyzont.Client.Tests/Steam/`, `tests/Horyzont.Testing/FakeSteamLobbyApi.cs`, `game/tests/Steam/` | testy deterministyczne i osobne Manual |

Nowe pola `LaunchOptions`: `ulong? ConnectLobbyId`, `bool EnableSteam`, `LobbyVisibility LobbyVisibility = FriendsOnly`, `uint SteamAppId = 480`. Nowe argumenty: `--steam`, `--steam-app-id <uint>`, `--lobby-visibility private|friends|public`, `+connect_lobby <ulong>` (ustawia Mode=Client i EnableSteam=true, nie wymaga --connect; konflikt z --solo/--host/--server jest błędem argumentów). Nie przechowuj sekretów w metadanych. Modyfikacje istniejących plików: `LaunchOptions.cs`, `GameEnvironment.cs`, `GameComposition.cs`, `Boot.cs`, `QuitCoordinator.cs`. Walidacja P2P identity istnieje w HandshakeValidator i nie wymaga zmiany ServerSession. Kontrakty konsumowane: tabela istniejącego kodu; wystawiane dla K3-18: controller, friend list, visibility, status i błędy.

### Mechaniczna procedura i decyzje

1. Steam SDK nie uruchamia się w Core/Server/transportowym konstruktorze. `SteamLifetime` powstaje w GameComposition przed jakimkolwiek SteamTransport; wywołuje Init raz, RunCallbacks raz na klatkę na głównym wątku. Nowe `GameComposition.CreatePlatformServices(GameEnvironment env, uint appId)` tworzy lifetime/controller bez uruchomienia świata: K3-18 wywołuje je przy otwarciu panelu Steam, CLI --steam przed składaniem transportów. Nowy obiekt `SteamPlatformServices` posiada Api/Lobby/Lifetime, jest wstrzykiwany w kolejne sesje i zwalniany dopiero przy wyjściu procesu; nie twórz drugiego composition root w UI. Dedykowany/solo/engine-tests/smoke bez Steam. App ID konfiguruj przed Init, domyślnie 480; plik `steam_appid.txt` tylko w lokalnym katalogu dev, nie commituj prawdziwego App ID ani nie ustawiaj 480 w produkcyjnym eksporcie.
2. Adapter korzysta z rodzin SDK `SteamAPI`, `SteamMatchmaking`, `SteamFriends` w istniejącym Steamworks.NET 2024.8.0. Przy implementacji potwierdź sygnatury create/join i callbacków w zainstalowanym SDK, nie kopiuj innej biblioteki. Mapowanie jest ustalone: Private→private, FriendsOnly→friends only, Public→public; create/join jako asynchroniczne CallResult, join-request jako Callback. Callback wyłącznie aktualizuje controller/kolejkę; nie buduje sceny i nie blokuje.
3. Host najpierw uruchamia świat i listener P2P na virtual port 0. CompositeTransport zawiera dokładnie jeden InMemory z istniejącej sieci hosta, UDP 27015 i SteamTransport. Dopiero po udanym starcie świata `Host(FriendsOnly, configuredMaxPlayers)`; domyślnie capacity 8, jak sesja. Błąd lobby nie niszczy zapisu/hosta LAN; pokaż błąd i pozwól ponowić utworzenie lobby. Solo nie nasłuchuje Steam. Lobby nie jest transportem.
4. Po wyniku create ustaw klucze: `horyzont_schema=1`, `horyzont_host=<SteamId decimal>`, `horyzont_build=<ten sam build hash co handshake>`, `horyzont_protocol=<ProtocolVersion decimal>`, `horyzont_world=<WorldId D>`, `horyzont_capacity=<liczba decimal>`, `horyzont_ready=1`. Najpierw wszystkie pola, ready ostatni; maks. wartość 128 znaków, parse InvariantCulture/TryParse. Błąd SetData → leave i Failed. Nigdzie nie publikuj hasła ani lokalnej ścieżki świata. Lobby graczy jest informacyjne; limity i hasło egzekwuje sesja.
5. Join request i `+connect_lobby` korzystają z tego samego `Join`. Id musi być niezerowe. Controller przechodzi Idle/Failed→Joining, czeka na wynik JoinLobby maks. 10 s (TimeProvider), następnie sprawdza wszystkie metadane, owner==host, ready==1, schema==1 i zgodność build/protocol. Dopiero wtedy emituje `ConnectRequested(new SteamEndpoint(host))`. WorldId informacyjny; endpoint z owner/data, nigdy z lobbyId. Metadane nie zastępują handshake serwera.
6. Invite wywołuje nakładkę tylko Hosting/Joined i przy aktywnym lobby, bez samodzielnego wysyłania wiadomości znajomym. Lista znajomych: maks. 256 wyników, tylko ten sam App ID i niezerowe lobbyId, posortowane Name ordinal, potem SteamId; odświeżenie na otwarcie panelu i co 5 s przy widocznym panelu. Join z listy korzysta z punktu 5. Nazwy są zwykłym tekstem, bez BBCode.
7. Przy zaproszeniu podczas gry controller emituje nowy lokalny `JoinConfirmationRequested(ulong lobbyId)` zamiast od razu opuszczać świat. K3-18 wyświetla „Zakończyć obecną sesję i dołączyć?”; po akceptacji obecne GameComposition.RequestShutdown/ReleaseStep kończy sesję zgodnie z D8, a dopiero potem nowy join. Odmowa zostawia obecną sesję. Drugi request zastępuje oczekujący id; podwójny wynik callbacka nie tworzy drugiego klienta.
8. Host/local client używają Steam PlatformIdentity wyłącznie gdy Steam aktywny (w pozostałych trybach trwały profil Dev). Dla połączenia P2P przed `OnPlayerJoining` ServerSession porównuje claimed SteamID z potwierdzonym `SteamEndpoint` z transportu; fałszywa platforma lub inny SteamID → istniejąca ścieżka odmowy/rozłączenia, bez operacji join. UDP nie zyskuje uwierzytelnienia z samego deklarowanego SteamID; bilet nadal K9-58. Nie utożsamiaj SteamId z PlayerId/EntityId.
9. Owner zmieniony względem zapamiętanego hosta: leave, `OwnerChanged`, koniec sesji; żadnej migracji hosta. Host kończy sesję wszystkim istniejącym `Disconnect`, ustawia ready=0 i opuszcza lobby. Nie dopisuj kodu handshake. Wynik Full/AccessDenied/timeout pokazuje controllerowi lokalny LobbyError; UI tłumaczy w K3-18.
10. Anulowanie zwiększa generation token operacji. Późny successful join/create starej generacji od razu LeaveLobby; nie emituje ConnectRequested. Dla każdej operacji termin 10 s, jedno żądanie aktywne; możliwość retry dopiero po zakończeniu/anulowaniu. `Leave` idempotentne. Kontroler usuwa subskrypcje i CallResult przy Dispose.
11. D8: wyjście sesji → drain → zapis/teren/ConstructWorld → Dispose composite i klienta → LeaveLobby (ready=0 dla hosta) → callback Dispose → SteamAPI.Shutdown ostatnie. Steam callbacks działają do końca zamykania transportów, także gdy Dispose transportu kończy się terminem; w takim przypadku shutdown SDK odłóż do wyjścia procesu, nie wywołuj równolegle z jeszcze działającym Dispose. QuitCoordinator nadal obsługuje zamknięcie okna/sygnały. Wyjście do menu nie wyłącza SDK procesu.
12. Bez nowych kodów protokołu, operacji, intencji ani blobów i bez zmiany formatu. D4 stosuje się, gdy implementacja jednak wymaga Protocol/Serialization: wartość main przy scaleniu+1, Opus i aktualizacja testu/planu; nie rób tego dla lokalnego LobbyError. ClientHello prefix i HandshakeReject zamrożone. D1: semantyka terenu i korpus nietknięte. Logowanie generowane, bez authTicket/hasła, z lobbyId i peer/player tam, gdzie są znane.

### Równoległość i mapa kolizji

K3-17 rozpoczyna po K1-06/K2-10, równolegle z K3-14/15/16/18. Moduły Steam i fake nie współdzielą plików replikacji. K3-18 może używać ustalonych kontraktów controller/friends bez oczekiwania na działający Steam.

| Wspólna ścieżka | Ryzyko i rozdział |
| --- | --- |
| `GameComposition.cs`, `GameEnvironment.cs` | 15: replikacja, 16: teren, 17: transport/SteamLifetime, 18: menu. SteamSessionBinder posiada całe składanie Steam; wywołania integracyjne scalaj po 15/16, przed integracją UI 18 |
| `LaunchOptions.cs`, `Boot.cs`, `QuitCoordinator.cs` | 17 posiada argumenty Steam i lifetime, 18 menu/wyjście do menu; oddzielne nowe bindery, rebase wspólnych plików 17→18 |
| `ServerSession.cs` / `HandshakeValidator.cs` | 15 dodaje bezalokacyjne send/resync; 18 odmowę read-only; 17 konsumuje istniejącą kontrolę tożsamości P2P bez zmian produkcyjnych w tych plikach |
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
| `Launch_ConnectLobbyArgument_UsesSameJoinFlow` | `+connect_lobby 123`, błąd braku/zera/przepełnienia; tryb Client |
| `JoinRequest_ActiveWorld_WaitsForConfirmationAndShutdown` | anulowanie zachowuje świat, zgoda uruchamia join po ReleaseStep, brak równoległych sesji |
| `Handshake_ForgedP2PIdentity_DoesNotApplyPlayerJoin` | transportowy SteamEndpoint inny niż claimed; zero zmian i zapisów świata |
| `Shutdown_HostLeaves_DisconnectsPeersAndShutsSdkLast` | kolejność D8, owner change, powtórzony Leave/Dispose; timeout Dispose nie wyłącza SDK spod transportu |
| `SteamLifetime_MultipleSessions_InitializesAndPumpsOnce` | host→menu→client, jeden Init i jedna pompka/klatkę |
| `SteamInvite_TwoAccounts_JoinsHostThroughRelay` | Manual: 2 konta i 2 PC/sieci bez port-forward, App ID 480; zaproszenie nakładki, lista znajomych, restart z +connect_lobby; zapis logów |
| `SteamLobby_EightPlayersThenHostExit_AllReceiveSessionEnd` | Manual: 8 kont/botów z osobnymi kontami Steam, timeout 120 s dla join/exit, brak migracji; jeśli brak kont raport pending, finalny scenariusz skoordynowany w K4-19 |

## Kroki

- [ ] 0. Potwierdź scalone zależności i SDK 2024.8.0; Sonnet zatwierdza fake/API i tabelę testów. Zapisz różnice w Odstępstwach.
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

## Pułapki

- SteamEndpoint przyjmuje SteamID hosta, nie lobbyID.
- Nie inicjalizuj Steam dwa razy i nie wyłączaj SDK przed Dispose SteamTransport.
- Steamworks.NET używa własnych callbacków; nie zastępuj go przykładem z Facepunch.
- App ID 480 współdzielą inne gry; filtruj metadane, build i schema.
- Zmiana właściciela lobby nie daje prawa do migracji świata; pozostaje wyjście hosta.
- Nigdy nie uznaj Steam identity deklarowanej w UDP za potwierdzoną.

## Pytania do właściciela

Warianty domyślne są decyzjami planu; wykonawca implementuje je bez wybierania architektury.

| Nr | Pytanie | Wariant domyślny |
| --- | --- | --- |
| P1 | Jakie App ID i widoczność stosować podczas K3? | 480, FriendsOnly; prawdziwe App ID podmieniane konfiguracją przy otrzymaniu, bez zmiany kontraktu |
| P2 | Czy dostępne są dwa konta/PC do ręcznego odbioru i osiem kont do pełnego scenariusza? | odbiór zaproszeń na dwóch kontach; 8 osób koordynowane z K4-19; brak dowodu wpisany jako pending Manual |
