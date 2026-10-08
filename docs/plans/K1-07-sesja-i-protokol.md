# K1-07 · Net: sesja, uścisk dłoni, zainteresowanie, limity

| | |
| --- | --- |
| Prowadzi | Opus 5.5 (Claude Code) |
| Testy akceptacyjne | Opus 5.5 |
| Przegląd | GPT-6.1 Sol (Codex), wysiłek `high` |
| Zależy od | K1-04, K1-06 |
| Gałąź | `pakiet/k1-07-sesja` |

**Cel:** sesje serwera i klienta nad `ITransport`: uścisk dłoni z kontrolą wersji i builda, stany połączenia z dozwolonymi komunikatami, limity tempa i bufor wejścia, zarządzanie zainteresowaniem; złe dane nigdy nie wywracają serwera.

## Kontekst

- `docs/ZASADY.md`, punkty 6, 7, 8
- `docs/spec/01-planeta.md`: Sieć (Protokół, Sesja, Zainteresowanie i limity), Obsługa błędów
- `docs/plans/K1-03-core-stan-i-operacje.md`: kontrakty protokołu, rekordy komunikatów, `HandshakeRejectCode`
- `docs/plans/K1-04-serializatory.md`: `MessageCodec`, ramki zamrożone
- `docs/plans/K1-06-transporty.md`: `ITransport`, `GetRemoteEndpoint`, `InMemoryNetwork`

## Zakres

`Horyzont.Net.Session`, `CompositeTransport`, `BuildInfo` w Core, testy w `tests/Horyzont.Net.Tests/Session/`.

Poza zakresem: treść replikacji (migawki i predykcja: pakiet 14; byty i budowle: pakiet 15; teren: pakiet 16), lobby Steam (pakiet 17), sprawdzanie biletu Steama dla połączeń UDP, u hosta i na serwerze dedykowanym (K9-58). Sesja przekazuje odpowiednie komunikaty do haków gry, a pakiety K2–K3 je wypełniają.

## Projekt

### Pliki

```
src/Horyzont.Net/Session/
  ServerOptions.cs, ClientOptions.cs
  ServerSession.cs, ClientSession.cs
  Peer.cs, PeerState.cs, ClientState.cs
  HandshakeValidator.cs
  MessageRouter.cs
  InputBuffer.cs
  InterestManager.cs
  RttEstimator.cs
  IServerGameHooks.cs, IClientGameHooks.cs
src/Horyzont.Net/Transport/CompositeTransport.cs
src/Horyzont.Core/BuildInfo.cs (+ cel MSBuild generujący BuildInfo.g.cs)
```

### Wersja builda

Cel MSBuild w `Horyzont.Core.csproj` generuje przed kompilacją `$(IntermediateOutputPath)BuildInfo.g.cs` (`WriteLinesToFile` z `WriteOnlyWhenDifferent="true"`, plik dodany do `Compile`) ze stałą `Commit`: zmienna środowiskowa `HORYZONT_COMMIT` (CI), inaczej `git rev-parse HEAD`, inaczej `"dev"`. Dzięki `WriteOnlyWhenDifferent` niezmieniony commit nie przebudowuje Core i wszystkiego, co od niego zależy. `BuildInfo.ComputeBuildHash(string generatorProfileVersion)` zwraca XxHash64 z `Commit` i wersji profilu generatora, bo inny generator dałby inny teren (specyfikacja). Buildy `"dev"` na różnych maszynach dostają ten sam skrót; to akceptowalne w rozwoju, a CI zawsze ustawia commit: ten pakiet dopisuje `HORYZONT_COMMIT: ${{ github.sha }}` do `env` w `.github/workflows/ci.yml`.

### Opcje

```csharp
public sealed record ServerOptions
{
    public required ulong BuildHash { get; init; }
    public int MaxPlayers { get; init; } = 8;
    public int MaxPendingConnections { get; init; } = 16; // dodane po przeglądzie (Odstępstwa, F2)
    public string Password { get; init; } = "";
    public IReadOnlySet<PlatformIdentity> BannedIdentities { get; init; } = new HashSet<PlatformIdentity>();
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan CloseDelay { get; init; } = TimeSpan.FromMilliseconds(500);
    public float InterestRadiusMeters { get; init; } = 500f;
    public float InterestHysteresisMeters { get; init; } = 50f;
    public int InputBufferCapacity { get; init; } = 8;
    public int ReliableMessagesPerSecond { get; init; } = 200;
    public int UnreliableMessagesPerSecond { get; init; } = 120;
}

public sealed record ClientOptions
{
    public required ulong BuildHash { get; init; }
    public TimeSpan PingInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(15);
}
```

Wartości 8 graczy, 10 s i 500 m pochodzą ze specyfikacji; limity komunikatów, 15 s na uścisk dłoni, 500 ms na zamknięcie i histereza 50 m to założenia do weryfikacji w testach z botami (pakiet 19). Obie sesje dostają `TimeProvider` w konstruktorze.

### Stany serwera i dozwolone komunikaty

| Stan peera | Dozwolone niezawodne od klienta | Dozwolone zawodne od klienta | Wyjście ze stanu |
| --- | --- | --- | --- |
| `AwaitingHello` | `ClientHello`, `Disconnect` | — | poprawny `ClientHello` → `Loading`; brak w `HandshakeTimeout` → `HandshakeTimeout` |
| `Loading` | `TerrainCacheReport`, `ClientReady`, `Disconnect` | `Ping` | `ClientReady` → `InGame` |
| `InGame` | `IntentSubmit`, `TerrainCacheReport`, `TerrainChecksums`, `Disconnect` | `InputBatch`, `SnapshotAck`, `Ping` | rozłączenie |
| `Closing` | — | — | `transport.Disconnect` po `CloseDelay` |

- Niezawodny komunikat spoza listy, błąd dekodowania (`ProtocolViolationException`) albo ramka nadesłana kanałem innym niż przewiduje `MessageChannels` → rozłączenie z kodem `ProtocolViolation` i wpis w logu z identyfikatorem gracza.
- Zawodny komunikat spoza listy jest odrzucany po cichu i liczony do limitu tempa. Ramki zawodne mogą wyprzedzić niezawodną zmianę stanu (np. `InputBatch` przed `ClientReady`), więc nie są naruszeniem.
- W stanie `Closing` każda przychodząca ramka jest odrzucana po cichu.
- Żaden wyjątek nie wychodzi poza `Poll`.
- Kolejne pakiety dopisują swoje typy K→S do wiersza `InGame`: `ResyncRequest` (K3-15), `ShipInputBatch` (K5-25), `ContainerSubscribe` i `ContainerUnsubscribe` (K6-29), `AdminCommand` (K9-59). Podprojekt 6 dokłada też stan `AwaitingAuth` między `ClientHello` a `Loading` na asynchroniczne sprawdzenie biletu Steama (K9-58). Każda taka zmiana tej tabeli to zmiana kontraktu z przeglądem Opus 5.5.

### Stany klienta

```csharp
public enum ClientState : byte { Disconnected = 0, Connecting = 1, AwaitingServerHello = 2, Loading = 3, InGame = 4, Closing = 5 }
```

`Connect` → `Connecting`; po `OnConnected` transportu klient wysyła `ClientHello` → `AwaitingServerHello`; `ServerHello` → `Loading` (gra wczytuje teren i wysyła `TerrainCacheReport`; od tej chwili klient wysyła `Ping` co `PingInterval`); `SendReady()` → `InGame` (dopiero wtedy wolno wysyłać wejście i intencje); `HandshakeReject` albo `Disconnect` → `Closing` → `Disconnected`. Reguły odbioru po stronie klienta:

- `Disconnect` jest przyjmowany w każdym stanie (np. `Shutdown` serwera w trakcie uścisku dłoni daje `HostLeft`, nie błąd protokołu).
- `ServerHello` i `HandshakeReject` tylko w `AwaitingServerHello`; pozostałe komunikaty S→K tylko w `Loading` i `InGame`.
- Zawodny komunikat S→K poza dozwolonym stanem klient odrzuca po cichu, bo może wyprzedzić niezawodny `ServerHello`; w `Closing` wszystko po cichu.
- Niezawodny komunikat poza dozwolonym stanem albo błąd dekodowania kończy połączenie po stronie klienta.

### Uścisk dłoni

Najpierw sprawdzenie wersji na surowej ramce: w `AwaitingHello` serwer woła `MessageCodec.TryReadProtocolVersion`. Ramka `ClientHello` z inną `ProtocolVersion` daje `HandshakeReject(WrongProtocol)`, nawet jeśli reszty ramki nie da się zdekodować. Prefiks `ClientHello` i format `HandshakeReject` są zamrożone (K1-04), więc klient każdej wersji dostaje czytelną prośbę o aktualizację. Ta sama `ProtocolVersion` i nieznany `FormatVersion` ramki `ClientHello` również daje `HandshakeReject(WrongProtocol)`; ta sama `ProtocolVersion`, znany `FormatVersion`, ale ramka się nie dekoduje → `ProtocolViolation`.

Potem `HandshakeValidator.Validate(ClientHello hello, HandshakeContext context) → HandshakeRejectCode?`, czysta funkcja, sprawdza w tej kolejności:

1. `BuildHash` (`WrongBuild`);
2. hasło, jeśli serwer je ma: porównanie w stałym czasie skrótów SHA-256 obu haseł w UTF-8 (`CryptographicOperations.FixedTimeEquals`), żeby nie zdradzić długości (`BadPassword`);
3. gdy `transport.GetRemoteEndpoint(connection)` zwraca `SteamEndpoint`: `Identity` równe `(Steam, SteamId)` tego punktu (`IdentityMismatch`). Przez UDP serwer przyjmuje zgłoszoną tożsamość; bilet Steama z `AuthTicket` sprawdza od podprojektu 6 pakiet K9-58;
4. tożsamość spoza `BannedIdentities` (`Banned`);
5. ta tożsamość nie jest już połączona (`AlreadyConnected`);
6. liczba graczy w stanach `Loading` i `InGame` mniejsza od `MaxPlayers` (`ServerFull`).

Odmowa: `HandshakeReject` kanałem niezawodnym, stan `Closing`, rozłączenie po `CloseDelay`. Przyjęcie: `hooks.OnPlayerJoining` (gra woła `PreparePlayerJoin`, stosuje `PlayerJoinOp` i zwraca `PlayerId`), `ServerHello` z `hooks.CreateHello`, stan `Loading`. Od tej chwili sesja zna połączenie po `PlayerId`, a tożsamość platformy służy tylko do banów i wykrywania podwójnego połączenia.

### Rozłączenia

- Każde rozłączenie z inicjatywy serwera (`Kick`, naruszenie protokołu, limit czasu uścisku dłoni albo bezczynności, `Shutdown`): `Disconnect(code)` kanałem niezawodnym, stan `Closing`, `transport.Disconnect` po `CloseDelay`. Gracz, który przeszedł `OnPlayerJoining`, dostaje `hooks.OnPlayerLeft(code)` od razu przy wejściu w `Closing`.
- `Shutdown(code)` robi to dla wszystkich peerów; `IsShutdownComplete` staje się `true`, gdy wszystkie połączenia są zamknięte. Gra woła `Poll` dalej, aż do tej chwili.
- Rozłączenie zgłoszone przez transport mapuje się na `DisconnectCode` tak:

| `TransportDisconnectReason` | `DisconnectCode` w hakach |
| --- | --- |
| `Local` | kod, z którym sesja sama rozłączyła |
| `Remote` | kod z odebranego wcześniej `Disconnect`, inaczej `Normal` |
| `Timeout`, `TransportError`, `Refused` | `Timeout` |

### Haki gry

```csharp
public interface IServerGameHooks
{
    PlayerId OnPlayerJoining(PlatformIdentity identity, string displayName);   // PlayerJoinOp już zastosowany
    ServerHello CreateHello(PlayerId player);                                   // YourPlayer i YourEntity ze stanu świata
    void OnPlayerReady(PlayerId player);                               // stan początkowy wysyła gra (pakiety 15–16)
    IntentReply OnIntent(PlayerId player, uint clientSeq, Intent intent);
    void OnClientMessage(PlayerId player, Message message);            // TerrainCacheReport, TerrainChecksums, SnapshotAck
    void OnPlayerLeft(PlayerId player, DisconnectCode reason);         // gra stosuje PlayerLeaveOp, jeśli gracz jest online
}

public interface IClientGameHooks
{
    void OnServerHello(ServerHello hello);
    void OnRejected(HandshakeRejectCode code, string detail);
    void OnMessage(Message message);                                   // komunikaty S→K w stanach Loading i InGame
    void OnDisconnected(DisconnectCode code);
}
```

`ServerSession` daje grze: `Poll(Tick currentTick)` (tick trafia do `Pong.ServerTick`), `Send(PlayerId, Message)`, `SendToInterested(EntityId subject, Message)`, `Kick(PlayerId, DisconnectCode)`, `TryTakeInput(PlayerId, out PlayerInput, out uint seq)`, `PlayersInGame`, `GetRtt(PlayerId)`, `Shutdown(DisconnectCode)` (host wychodzi → `HostLeft` do wszystkich), `IsShutdownComplete`. `ClientSession` daje: `Connect`, `Poll`, `State`, `SendReady`, `SubmitIntent(Intent) → uint clientSeq`, `SendInputs`, `Send(Message)`, `Rtt`.

### Limity

- Kubełek żetonów komunikatów na peera, osobno dla kanałów (200 i 120 na sekundę, zapas = 2 sekundy). Nadmiarowy komunikat jest odrzucany i liczony; ponad 100 odrzuconych w 5 s → `Kick(Kicked)`.
- Bufor wejścia: wejścia z numerem większym niż ostatnio przetworzony, bez duplikatów (klient powtarza trzy ostatnie), najwyżej `InputBufferCapacity`; nadmiar najnowszych odrzucany, żeby tempo przetwarzania było stałe. `TryTakeInput` oddaje dokładnie jedno wejście na wywołanie (gra woła raz na tick fizyki).
- Limity intencji (tempo edycji) są w Core (`ServerSimulation`), nie tutaj.

### Zainteresowanie

```csharp
/// Region to rama, w której liczy się odległość: rama planety, a od podprojektu 2 także rama układu.
/// Position to pozycja w układzie regionu; dla bytów w ramie budowli gra składa ją z ram.
/// Radius to promień otaczający podmiotu, ViewRange to zasięg, z którego widać ten podmiot.
public readonly record struct InterestSubject(EntityId Entity, FrameRef Region, Vector3 Position, float Radius, float ViewRange);
public readonly record struct InterestViewer(PlayerId Player, FrameRef Region, Vector3 Position);

public sealed class InterestManager
{
    public InterestManager(float hysteresis);
    public void Update(ReadOnlySpan<InterestSubject> subjects, ReadOnlySpan<InterestViewer> viewers);
    public void GetChanges(PlayerId viewer, List<EntityId> entered, List<EntityId> left);
    public bool IsVisible(PlayerId viewer, EntityId subject);
}
```

Podmiot wchodzi w zasięg, gdy jest w tym samym regionie i `odległość − Radius ≤ ViewRange`; wychodzi powyżej `ViewRange + hysteresis` albo po zmianie regionu. W podprojekcie 1 każdy podmiot ma `ViewRange` = `ServerOptions.InterestRadiusMeters` (500 m); kolejne podprojekty podają własne zasięgi (stwory 120 m, przedmioty leżące 64 m, statki w układzie 10 km). Siatka mieszająca ma komórki 64 m w regionie planety i 2048 m w regionie układu; w stanie ustalonym `Update` nie alokuje. Gra woła `Update` z częstotliwością 10 Hz.

### RTT

Klient wysyła `Ping` co sekundę od wejścia w `Loading`, serwer odpowiada `Pong` z tickiem przekazanym do `Poll`. `RttEstimator` liczy średnią wykładniczą (α = 0,125) i odchylenie; pakiet 14 użyje tego do interpolacji.

## Testy akceptacyjne (napisz najpierw)

Wszystkie na `InMemoryTransport` z `FakeTimeProvider`, poza `CompositeTransport` (w pamięci + UDP na prawdziwym czasie).

| Test | Oczekiwanie |
| --- | --- |
| `Handshake_ValidHello_ReachesInGame` | klient `InGame`; haki serwera w kolejności `OnPlayerJoining`, `CreateHello`, `OnPlayerReady` |
| `Handshake_InvalidHello_RejectsWithCode` (teoria) | `WrongBuild`, `BadPassword`, `IdentityMismatch` (transport klienta z `fakeSteamId` różnym od `Identity.Value` albo tożsamość `Dev` przez transport Steam), `Banned`, `AlreadyConnected`, `ServerFull`: klient dostaje kod i rozłączenie, `OnPlayerJoining` nie jest wołany |
| `Handshake_NewerProtocolHello_RejectsWrongProtocol` | ramka `ClientHello` z `ProtocolVersion = 2` i nieznanym `FormatVersion` daje `HandshakeReject(WrongProtocol)`, nie `ProtocolViolation` |
| `Poll_NoHelloFor15s_DisconnectsHandshakeTimeout` | brak `ClientHello` przez 15 s → rozłączenie `HandshakeTimeout` |
| `Poll_SilenceFor10sInGame_ReportsPlayerLeftTimeout` | cisza 10 s w `InGame` → `OnPlayerLeft(Timeout)` |
| `Receive_ReliableNotAllowedInState_DisconnectsProtocolViolation` | `IntentSubmit` w stanie `Loading` → `ProtocolViolation` |
| `Receive_UnreliableBeforeReady_DroppedSilently` | łącze z jitterem: `InputBatch` dochodzi przed `ClientReady`; brak rozłączenia, klient kończy w `InGame` |
| `Receive_MalformedFrame_DisconnectsAndKeepsServingOthers` | losowe bajty: rozłączenie, `Poll` nie rzuca, wpis w logu; drugi klient działa dalej |
| `Receive_WrongChannel_DisconnectsProtocolViolation` | `InputBatch` kanałem niezawodnym albo `IntentSubmit` kanałem zawodnym → `ProtocolViolation` |
| `SubmitIntent_ServerReplies_ReplyCarriesClientSeq` | `SubmitIntent` → `OnIntent` → `IntentReply` z tym samym `ClientSeq` |
| `Receive_Flood_KicksOnlyFlooder` | 1000 komunikatów niezawodnych w 1 s → `Kicked`; inny klient obsługiwany bez zakłóceń |
| `Kick_Player_ClientReceivesCodeBeforeClose` | klient dostaje `OnDisconnected(Kicked)` z komunikatu `Disconnect`, zanim transport zamknie połączenie |
| `TryTakeInput_DuplicatesAndBurst_OnePerCallUpToCapacity` | paczki z powtórzeniami dają rosnące, unikalne numery, po jednym na `TryTakeInput`; nadmiar ponad 8 odrzucony |
| `InterestManager_Hysteresis_EntersAt500LeavesAbove550` | przy `ViewRange` 500 m: 499 m widoczny, 520 m nadal widoczny, 551 m opuszcza; podmiot z `ViewRange` 120 m wchodzi przy 119 m i nie jest widoczny przy 200 m; zmiana regionu opuszcza |
| `LossyLink_500Intents_RepliesInOrder` | łącze 5% strat, 50 ms ± 10 ms: 500 intencji → 500 odpowiedzi w kolejności |
| `Handshake_NinthClient_RejectsServerFull` | 8 klientów `InGame`, dziewiąty `ServerFull` |
| `Shutdown_HostLeft_NotifiesAllAndCompletes` | `Shutdown(HostLeft)` → każdy klient `OnDisconnected(HostLeft)`, potem `IsShutdownComplete` |
| `Shutdown_DuringHandshake_ClientReportsHostLeft` | `Shutdown(HostLeft)`, zanim klient dostanie `ServerHello` → `OnDisconnected(HostLeft)`, nie błąd protokołu |
| `ClientReceive_UnreliableBeforeServerHello_DroppedSilently` | zawodna ramka S→K (np. `Pong`) przed `ServerHello` nie rozłącza klienta, który kończy w `InGame` |
| `CompositeTransport_MemoryAndUdp_BothReachInGame` | jeden klient w pamięci, drugi przez UDP na loopbacku, obaj `InGame` |
| `Poll_SteadyState_DoesNotAllocate` | 8 klientów w `InGame`, 1000 ticków z wejściem i pingiem: `Poll` i `TryTakeInput` bez alokacji poza dekodowaniem komunikatów |

## Kroki

- [x] `BuildInfo` z celem MSBuild; `HORYZONT_COMMIT` w `ci.yml`.
- [x] Testy z tabeli (czerwone).
- [x] `HandshakeValidator`, sprawdzenie wersji na surowej ramce, `MessageRouter`, stany peera i klienta.
- [x] `ServerSession` i `ClientSession` z hakami i regułą rozłączeń.
- [x] Limity komunikatów, `InputBuffer`.
- [x] `InterestManager`.
- [x] `RttEstimator`, ping i pong.
- [x] `CompositeTransport`.
- [x] Komentarze XML; zielone CI.

## Odstępstwa i decyzje wykonawcy

- **Filtr typu przed dekodowaniem (przegląd K1-04, F2).** Serwer i klient sprawdzają bajt typu ramki przed `MessageCodec.Read`: nieznany typ, kanał inny niż w `MessageChannels` albo niezawodny typ spoza tabeli stanu kończą się `ProtocolViolation` bez dekodowania; zawodny typ spoza tabeli (także typ S→K od klienta) jest odrzucany po cichu, również bez dekodowania. Test `Receive_ServerToClientTypeFromClient_DisconnectsWithoutDecoding` i `ClientReceive_ClientToServerTypeReliable_EndsSessionWithoutDecoding` (wewnętrzne liczniki `FramesDecoded`).
- **Treść przyjęta przez kodek, odrzucona przez grę (przegląd K1-04, F3, wariant a).** Klient łapie `InvalidDataException` i `ProtocolViolationException` z haków `OnServerHello` i `OnMessage` (np. zduplikowane identyfikatory rejestrów albo planeta 0 w `ServerHello`) i kończy sesję kodem `ProtocolViolation` z `Disconnect` do serwera. Test `ClientReceive_HelloRejectedByGame_EndsSessionWithProtocolViolation`. Sesja nie woła `OperationCodec` bezpośrednio; `MessageCodec.Read` sprawdza koniec całej ramki.
- **Wyjątki w `Poll` serwera.** Oprócz `ProtocolViolationException` serwer łapie każdy wyjątek z obsługi ramki peera i z haków gry wołanych w `Poll` (np. błąd gry przy złośliwych danych), loguje go jako błąd z identyfikatorem gracza i rozłącza tylko tego peera kodem `ProtocolViolation`. Wyjątki z metod wołanych przez grę (`Send`, `Kick`, `Shutdown`) przechodzą do gry.
- **Nieznany `FormatVersion` `ClientHello`.** Wersję formatu, którą zna kodek, sesja odczytuje z ramki zakodowanej przez `MessageCodec` przy starcie (bez kopii stałej i bez zmiany Core).
- **`IntentReply`.** Sesja wysyła odpowiedź z `ClientSeq` zgłoszonej intencji niezależnie od wartości zwróconej przez hak.
- **Klient po odmowie i rozłączeniu.** Po `HandshakeReject` klient woła `OnRejected`, potem `OnDisconnected(Normal)`. Po `Disconnect` albo odmowie od serwera klient od razu woła `transport.Disconnect` (opóźnienie zamknięcia ma tylko serwer). `OnDisconnected` jest wołane dokładnie raz na `Connect`, przy wejściu w `Closing` albo przy zgłoszeniu transportu, cokolwiek nastąpi pierwsze. Klient ma własny limit uścisku dłoni (`ClientOptions.HandshakeTimeout`, kod `HandshakeTimeout`).
- **`Disconnect` od klienta.** Serwer przechodzi w `Closing` bez odpowiedzi, woła `OnPlayerLeft` z kodem klienta i zamyka transport po `CloseDelay`, jeśli klient nie zamknął go wcześniej.
- **Limity tempa.** Kubełki uzupełniane z monotonicznego zegara `TimeProvider` sesji (w tickach `TimeSpan`, liczby całkowite), zapas 2 s. Do reguły „ponad 100 w 5 s” liczą się tylko ramki odrzucone przez limit; okno 5 s jest stałe (zerowane po upływie), nie przesuwne. Ramki w stanie `Closing` nie liczą się.
- **`AlreadyConnected` i `ServerFull`** liczą tylko graczy w `Loading` i `InGame`; peer w `Closing` od razu zwalnia miejsce i tożsamość.
- **Bufor wejścia.** Pełny bufor odrzuca nowe wejście, gdy jest najnowsze; starsze wejście (przestawione w sieci) wypiera najnowsze z bufora. Numery nie zawijają się (przepełnienie `FirstSeq + i` jest ignorowane).
- **RTT.** `RttEstimator`: α = 1/8, odchylenie z β = 1/4 (RFC 6298), pierwsza próbka ustawia odchylenie na połowę. `ServerSession.GetRtt` zwraca RTT z `ITransport.GetStats`, bo serwer nie wysyła własnych pingów.
- **API ponad plan.** `ClientSession.Connect(endpoint, identity, displayName, password = "", authTicket = null)` i `ClientSession.Disconnect()`; `ClientSession.Send` przyjmuje tylko `TerrainCacheReport`, `TerrainChecksums` i `SnapshotAck` w stanach, w których serwer je przyjmuje; `SendInputs(uint firstSeq, IReadOnlyList<PlayerInput>)`. `ServerSession.Interest` to `InterestManager` sesji (histereza z opcji), którego `Update` woła gra; `Send` i `SendToInterested` przyjmują tylko komunikaty gry S→K (`ArgumentException` dla uścisku dłoni i `Disconnect`). `PlayersInGame` w kolejności wejścia do gry.
- **Dodatkowe pliki** (jeden typ publiczny na plik): `HandshakeContext.cs`, `InterestSubject.cs`, `InterestViewer.cs`, wewnętrzne `MessageBucket.cs` i `SessionLog.cs` (`[LoggerMessage]`). `Horyzont.Net` ma `InternalsVisibleTo` dla `Horyzont.Net.Tests`.
- **`InterestManager`.** Widzowie nieobecni w `Update` są zapominani; `GetChanges` czyści listy i podaje różnicę od poprzedniego wywołania dla tego widza; podmioty i widzowie z wartościami nieskończonymi są pomijani. Zapytanie przegląda sześcian komórek wokół widza albo, gdy zajętych komórek jest mniej, listę zajętych komórek.
- **`CompositeTransport`** kieruje punkty końcowe według rodzaju do pierwszego transportu tego typu (`InMemoryEndpoint` → `InMemoryTransport`, `UdpEndpoint` → `UdpTransport`, `SteamEndpoint` → `SteamTransport`), nadaje własne `ConnectionId` i jest właścicielem transportów wewnętrznych (`Dispose`).
- **`BuildInfo`.** Cel `GenerateBuildInfo` działa przed `BeforeCompile`/`CoreCompile`; commit jest oczyszczany do znaków `[0-9A-Za-z._/-]`; `ComputeBuildHash` liczy XxHash64 z UTF-8 `Commit`, bajtu zerowego i profilu.
- **Test alokacji.** `Poll_SteadyState_DoesNotAllocate` dopuszcza koszt dekodowania odebranych ramek, obiekt `Pong` na każdy `Ping` (rekord odpowiedzi) i stały zapas 1 KiB na szum środowiska przy równoległych testach (zaobserwowane 40 B jednorazowo); alokacja w każdym ticku (≥ 24 kB na 1000 ticków) nadal oblewa test. `Update` w `InterestManager` ma osobny test zerowej alokacji.
- **TDD.** Testy powstały przed kodem sesji, ale pierwszy przebieg odbył się już z implementacją (brak zapisanego osobnego przebiegu „na czerwono”).
- **Testy ponad tabelę:** odmowa przy znanym `ProtocolVersion` i nieznanym `FormatVersion`, `ProtocolViolation` dla nierozkodowalnego `ClientHello`, losowe ramki w każdym stanie, poprawne hasło i tożsamość Steam, lokalny limit uścisku dłoni klienta, normalne rozłączenie klienta, ping w `Loading`, testy `InputBuffer`, `RttEstimator`, `InterestManager` (promień, region układu, usunięcie podmiotu, zero alokacji) i mapowanie identyfikatorów w `CompositeTransport`.
- **Poprawki po przeglądzie GPT-6.1 Sol (F1–F5).**
  - F1: cała obsługa ramki peera (także limit tempa, wykrycie zalewu i ścieżka `ProtocolViolationException`) jest w jednym bloku `try`; wyjątek kończy się `Fail` tylko dla tego peera. `OnPlayerLeft` jest izolowany na każdej ścieżce zamknięcia (naruszenie, zalew, limity czasu, `Kick`, `Shutdown`, rozłączenie transportu): błąd haka jest logowany (EventId 7008) i sesja zamyka pozostałych graczy dalej. To zawęża wcześniejszą decyzję „wyjątki z `Kick` i `Shutdown` przechodzą do gry”: nie dotyczy już `OnPlayerLeft`. Testy `Receive_MalformedFrameAndThrowingLeftHook_KeepsServingOthers`, `Receive_FloodAndThrowingLeftHook_KeepsServingOthers`, `Shutdown_ThrowingLeftHook_ClosesEveryPlayer`.
  - F2: nowa opcja `ServerOptions.MaxPendingConnections` (domyślnie 16) ogranicza połączenia poza `Loading` i `InGame` (czekające na `ClientHello` oraz zamykane). Nadmiarowe połączenie dostaje gotową ramkę `HandshakeReject(ServerFull)` i rozłączenie transportu tuż po jego `Poll`, bez tworzenia `Peer`; serwer trzyma więc najwyżej `MaxPlayers + MaxPendingConnections` połączeń. Bufor wejścia powstaje dopiero po przyjęciu gracza, sprawdzenie `AlreadyConnected` używa zbioru tożsamości (bez przeglądania listy). Atak zajmujący wszystkie miejsca oczekujące blokuje nowych graczy do upływu `HandshakeTimeout`; limity na adres i uwierzytelnienie UDP zostają w K9-58. Test `Connect_StormOfUnauthenticatedConnections_BoundedAndEstablishedClientServed` (1000 połączeń, `MaxPlayers = 8`).
  - F3: obie sesje mierzą czas wyłącznie przez `TimeProvider.GetTimestamp`/`GetElapsedTime` (zegar monotoniczny od konstrukcji sesji); czas UTC nie jest używany. `FakeTimeProvider` nie cofa czasu UTC, a jego znacznik czasu podąża za UTC, więc testy używają `SkewableClock` (znacznik z `FakeTimeProvider`, UTC przesunięte o dowolny offset). Testy `Receive_WallClockJumpsBack_RateLimitKeepsRefilling`, `Poll_WallClockJumpsForward_SessionsAndHandshakeSurvive`.
  - F4: `Ping.ClientTimeMicros` niesie mikrosekundy zegara monotonicznego klienta (format bez zmian). Klient pamięta najwyżej 8 wysłanych pingów; próbką RTT jest tylko pierwszy `Pong` odpowiadający zapamiętanemu pingowi (nieproszony albo powtórzony `Pong` trafia do gry, ale nie do RTT). `Connect` tworzy nowy `RttEstimator` i czyści listę pingów. Testy `ClientReceive_UnsolicitedPong_NotAnRttSample`, `ClientReceive_DuplicatePong_CountsOnce`, `ClientConnect_AfterReconnect_RttStartsEmpty`.
  - F5: `CompositeTransport.Send` sprawdza ogłoszony (najmniejszy) limit kanału przed wyszukaniem połączenia, także dla znanych połączeń. Test `Send_KnownConnectionAboveCompositeLimit_Throws`.
- **Niezweryfikowane:** ścieżka Steam (`SteamTransport` w `CompositeTransport`) tylko się kompiluje; sprawdzenie tożsamości Steam testowane przez `fakeSteamId` transportu w pamięci.

## Odbiór

Testy z tabeli przechodzą w CI; przegląd GPT-6.1 Sol pod kątem odporności na złośliwego klienta (każda ścieżka danych z sieci kończy się walidacją albo rozłączeniem).

## Pułapki

- Odmowa wysłana tuż przed rozłączeniem może nie dojść, jeśli transport zamknie połączenie natychmiast; stąd stan `Closing` z opóźnieniem.
- Kolejność między kanałami nie jest zachowana: ramka zawodna wysłana po niezawodnej może dojść wcześniej.
- `CompositeTransport` musi mapować `ConnectionId` z różnych transportów na unikalne identyfikatory sesji i przekazywać `GetRemoteEndpoint` do właściwego transportu.
- Nie trzymaj referencji do bufora z `OnReceived` po powrocie z wywołania; dekoduj od razu.
