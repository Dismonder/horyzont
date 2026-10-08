# K1-06 · Net: transporty w pamięci, UDP i Steam

| | |
| --- | --- |
| Prowadzi | Sonnet 5.5 (Claude Code) |
| Testy akceptacyjne | Sonnet 5.5 |
| Przegląd | Opus 5.5 na bramce K1 |
| Zależy od | K1-02A |
| Gałąź | `pakiet/k1-06-transporty` |

**Cel:** jeden interfejs `ITransport` przenoszący bajty kanałem niezawodnym uporządkowanym i zawodnym, z trzema implementacjami (w pamięci z symulacją łącza, UDP przez LiteNetLib, Steam Networking Sockets): dwie pierwsze przechodzą wspólne testy kontraktu w CI, Steam ręczny scenariusz na dwóch maszynach.

## Kontekst

- `docs/ZASADY.md`, punkty 6, 7, 8
- `docs/spec/01-planeta.md`: Sieć → Transport, Steam w podprojekcie 1

## Zakres

`Horyzont.Net.Transport` (interfejs i typy), `Horyzont.Net.Transport.InMemory`, `Horyzont.Net.Transport.Udp`, `Horyzont.Net.Transport.Steam`; testy kontraktu w `tests/Horyzont.Net.Tests/Transport/`; narzędzie `tests/Horyzont.Net.SteamCheck` do ręcznego sprawdzenia Steama.

Poza zakresem: format komunikatów i sesja (K1-04, K1-07), inicjalizacja Steama i lobby (pakiet 17). Transport Steam zakłada, że gra już wywołała `SteamAPI.Init()` i co klatkę woła `SteamAPI.RunCallbacks()`.

## Projekt

```csharp
namespace Horyzont.Net.Transport;

public enum TransportChannel : byte { ReliableOrdered = 0, Unreliable = 1 }
public readonly record struct ConnectionId(ulong Value);
public enum TransportDisconnectReason : byte { Local = 0, Remote = 1, Timeout = 2, Refused = 3, TransportError = 4 }
public readonly record struct TransportStats(float RttMs, float PacketLoss, long BytesSent, long BytesReceived);

public abstract record TransportEndpoint;
public sealed record InMemoryEndpoint(string Name) : TransportEndpoint;
public sealed record UdpEndpoint(string Host, int Port) : TransportEndpoint;
public sealed record SteamEndpoint(ulong SteamId) : TransportEndpoint;      // klient: SteamID hosta; serwer: ignorowane

public interface ITransportEvents
{
    void OnConnected(ConnectionId connection);
    void OnDisconnected(ConnectionId connection, TransportDisconnectReason reason);
    void OnReceived(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload);   // bufor ważny tylko w trakcie wywołania
}

public interface ITransport : IDisposable
{
    int MaxReliablePayload { get; }              // co najmniej 256 KiB (limit komunikatu niezawodnego z K1-03)
    int MaxUnreliablePayload { get; }            // co najmniej 1150 bajtów (limit komunikatu zawodnego z K1-03)
    void Listen(TransportEndpoint endpoint);
    ConnectionId Connect(TransportEndpoint endpoint);
    void Send(ConnectionId connection, TransportChannel channel, ReadOnlySpan<byte> payload);
    void Disconnect(ConnectionId connection);    // po wysłaniu zaległych wiadomości niezawodnych, jeśli transport to umożliwia
    void Poll(ITransportEvents events);          // tylko z wątku gry; dostarcza wszystkie oczekujące zdarzenia
    TransportStats GetStats(ConnectionId connection);
    TransportEndpoint GetRemoteEndpoint(ConnectionId connection);   // Steam: SteamEndpoint z SteamID drugiej strony
}

/// Timeout: brak jakiegokolwiek ruchu od drugiej strony przez ten czas rozłącza z powodem Timeout.
/// KeepAlive: odstęp wiadomości podtrzymujących; konstruktor rzuca ArgumentException, gdy KeepAlive > Timeout / 4.
public sealed record TransportOptions(TimeSpan Timeout, TimeSpan KeepAlive)
{
    public static TransportOptions Default { get; } = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1));
}
```

Zasady wspólne:

- `Send` z ładunkiem większym niż limit kanału rzuca `ArgumentException`. `Send` do rozłączonego połączenia nic nie robi (wyścig z rozłączeniem jest normalny).
- Zdarzenia przychodzą wyłącznie w `Poll`. W stanie bez ruchu `Poll` nie alokuje.
- Kanał niezawodny dostarcza każdą wiadomość dokładnie raz i w kolejności wysłania; zawodny może gubić, duplikować i zmieniać kolejność.
- `ConnectionId` nadaje sam transport z własnego licznika: jest unikalny w obrębie instancji i nigdy nie jest używany ponownie, także gdy biblioteka pod spodem odzyskuje swoje identyfikatory.
- Czas przez `TimeProvider` przekazany w konstruktorze; implementacja w pamięci działa wyłącznie na nim, więc testy przesuwają `FakeTimeProvider`.

### W pamięci

```csharp
public sealed record LinkConditions(TimeSpan Latency, TimeSpan Jitter, double LossRate, double DuplicateRate, int Seed);

public sealed class InMemoryNetwork          // wspólny „ośrodek” dla wszystkich InMemoryTransport w teście albo procesie hosta
{
    public InMemoryNetwork(TimeProvider time, LinkConditions? defaultConditions = null);
    public InMemoryTransport CreateTransport(string name, TransportOptions options, ulong? fakeSteamId = null);
    public void SetConditions(string a, string b, LinkConditions conditions);
    public void Partition(string a, string b);   // cały ruch między a i b ginie, w obie strony
    public void Heal(string a, string b);
}
```

- Nazwa transportu to jego adres: `Listen(new InMemoryEndpoint(name))` przyjmuje tylko własną nazwę, a `Connect(new InMemoryEndpoint("server"))` łączy z transportem o nazwie `server`.
- Kanał niezawodny w pamięci ma opóźnienie i jitter, ale nie gubi i nie zmienia kolejności (gwarancja transportu). Kanał zawodny podlega stratom, duplikatom i jitterowi z ziarnem z `LinkConditions`, więc testy są powtarzalne.
- Keepalive: transport wysyła go w `Poll`, gdy od `KeepAlive` nic nie wysłał. Strona, która nie woła `Poll`, nie wysyła niczego, a po `Partition` nie dochodzi nic: w obu przypadkach druga strona dostaje `Timeout` po `Timeout`.
- `fakeSteamId` ustawia tożsamość widoczną przez `GetRemoteEndpoint` po drugiej stronie (`SteamEndpoint(fakeSteamId)` zamiast `InMemoryEndpoint`). Dzięki temu sesja (K1-07) testuje sprawdzenie tożsamości Steam bez Steama.
- Tryb hosta w grze używa tej implementacji z zerowym opóźnieniem.

### UDP (LiteNetLib)

Paczka LiteNetLib w najnowszej stabilnej wersji 1.3.x albo 2.0.x; nazwy pól niżej są takie same w obu.

- `NetManager` z `UnsyncedEvents = false`; `Poll` woła `PollEvents()`.
- `MtuOverride = 1164`, `MtuDiscovery = false`. Domyślne MTU LiteNetLib to 1024 bajty (1023 B ładunku zawodnego), za mało na 1150 B; 1164 daje 1163 B i nadal mieści się w bezpiecznych 1232 B ładunku UDP. `MaxUnreliablePayload` bierzesz z `NetPeer.GetMaxSinglePacketSize(DeliveryMethod.Unreliable)` i sprawdzasz, że wynosi co najmniej 1150.
- Kanały: `DeliveryMethod.ReliableOrdered` i `DeliveryMethod.Unreliable`; duże wiadomości niezawodne dzieli LiteNetLib (256 KiB to około 230 fragmentów przy limicie 65 535).
- Klucz połączenia stały (`"horyzont/1"`, `request.AcceptIfKey`) odrzuca obcy ruch; hasło serwera sprawdza dopiero sesja (K1-07).
- `DisconnectTimeout` i `PingInterval` z `TransportOptions` (w milisekundach).
- `NetPeer.Id` LiteNetLib wraca do puli po rozłączeniu, więc mapę `NetPeer` ↔ `ConnectionId` trzymasz sam.
- Statystyki: `EnableStatistics = true`; RTT to `NetPeer.RoundTripTime` (`NetPeer.Ping` to połowa RTT), straty z `NetPeer.Statistics`.
- Symulacja strat i opóźnień wbudowana w LiteNetLib (`SimulatePacketLoss`, `SimulateLatency`) działa tylko w buildach DEBUG albo z `SIMULATE_NETWORK`, a paczka NuGet jest w Release. Testy z utratą pakietów idą przez `UdpLossyRelay` (niżej).

### Steam (Steamworks.NET)

- Serwer: `SteamNetworkingSockets.CreateListenSocketP2P(0, 0, null)` i grupa odpytywania (`CreatePollGroup`). Wywołanie zwrotne `SteamNetConnectionStatusChangedCallback_t` jest wspólne dla całego procesu: serwer obsługuje tylko zdarzenia, w których `m_info.m_hListenSocket` to jego gniazdo; w stanie `Connecting` akceptuje (`AcceptConnection`) i dodaje do grupy (`SetConnectionPollGroup`).
- Klient: `ConnectP2P` z tożsamością SteamID hosta; obsługuje zdarzenia tylko swojego połączenia.
- Wysyłanie: `SendMessageToConnection` z flagą `k_nSteamNetworkingSend_Reliable` albo `k_nSteamNetworkingSend_UnreliableNoDelay`.
- Odbiór: `ReceiveMessagesOnPollGroup` (serwer) i `ReceiveMessagesOnConnection` (klient) do tablicy wskaźników z puli; po skopiowaniu ładunku każda wiadomość jest zwalniana (`SteamNetworkingMessage_t.Release`).
- `GetRemoteEndpoint` zwraca `SteamEndpoint` z `m_info.m_identityRemote`. `Disconnect` woła `CloseConnection` z `bEnableLinger: true`, żeby zaległe wiadomości niezawodne doszły przed zamknięciem.
- Limit czasu przez konfigurację połączenia `k_ESteamNetworkingConfig_TimeoutConnected` z `TransportOptions.Timeout`; podtrzymanie robi sam Steam.
- W trakcie rozwoju App ID 480: plik `steam_appid.txt` z treścią `480` obok pliku wykonywalnego, poza eksportem.
- Biblioteki natywne: sprawdź, czy paczka NuGet zawiera `steam_api64.dll` i `libsteam_api.so` (`runtimes/*/native`). Jeśli nie, weź je z `redistributable_bin` Steamworks SDK w wersji zgodnej z paczką i trzymaj w `third_party/steamworks/` (Git LFS według reguły już obecnej w `.gitattributes`).

## Testy akceptacyjne (napisz najpierw)

Abstrakcyjna klasa `TransportContractTests<TFactory>` z fabryką tworzącą w jednym procesie serwer i klientów. Konkretne klasy: `InMemoryTransportTests` i `UdpTransportTests` (CI; UDP na loopbacku przez `UdpLossyRelay`).

`UdpLossyRelay` (w `tests/Horyzont.Net.Tests/Transport/`): przekaźnik UDP między klientami a serwerem, który przekazuje datagramy z ziarnem losowania, stratą, duplikatami i opóźnieniem oraz `Partition()`, czyli porzucaniem całego ruchu. Klienci łączą się z jednym gniazdem przekaźnika, a przekaźnik trzyma osobne gniazdo w stronę serwera dla każdego adresu klienta (mapa adres klienta → gniazdo). Inaczej LiteNetLib, który rozróżnia peery po `IPEndPoint`, widziałby ośmiu klientów jako jednego.

| Test | Oczekiwanie |
| --- | --- |
| `Connect_ServerListening_BothSidesReportConnected` | `OnConnected` po obu stronach |
| `SendReliable_1000Messages_ArriveInOrderOnce` | wszystkie, w kolejności, bez duplikatów; UDP także przy 10% strat na przekaźniku |
| `SendReliable_256KiB_ArrivesIntact` | jeden ładunek 256 KiB, zgodny bajt w bajt |
| `SendUnreliable_OverLimit_Throws` | `ArgumentException`, nic nie wysłane |
| `SendUnreliable_1150Bytes_Arrives` | ładunek dokładnie 1150 B dochodzi nienaruszony |
| `SendUnreliable_UnderLoss_DeliversIntactSubset` | przy 20% strat dochodzi część, każda wiadomość nienaruszona |
| `Disconnect_Local_RemoteNotifiedWithin1s` | strona zdalna dostaje `Remote` w ciągu 1 s, lokalna `Local` |
| `RemoteVanishes_NoTraffic_TimesOut` | po `Partition` obie strony dostają `Timeout` po czasie z `TransportOptions` (z zapasem 1 s) |
| `Send_AfterDisconnect_IsIgnored` | bez wyjątku |
| `Connect_EightClients_DistinctConnectionIds` | 8 równoczesnych połączeń z odrębnymi `ConnectionId` |
| `Connect_AfterDisconnect_ConnectionIdNotReused` | ponowne połączenie dostaje nowy `ConnectionId` |
| `Poll_Idle_DoesNotAllocate` | `GC.GetAllocatedBytesForCurrentThread` bez zmian po 1000 pustych `Poll` |
| `GetRemoteEndpoint_FakeSteamId_ReturnsSteamEndpoint` | tylko w pamięci: druga strona widzi `SteamEndpoint(fakeSteamId)` |
| `TransportOptions_KeepAliveAboveQuarterTimeout_Throws` | `ArgumentException` |

Testy w pamięci przesuwają `FakeTimeProvider` zamiast czekać. Testy UDP działają na prawdziwym czasie z `TransportOptions(500 ms, 100 ms)` i limitem czasu testu 10 s.

### Steam: ręczny scenariusz

`tests/Horyzont.Net.SteamCheck` to program konsolowy poza CI. Sam woła `SteamAPI.Init()` i co 10 ms `SteamAPI.RunCallbacks()`, a do katalogu wyjściowego kopiuje `steam_appid.txt` (480) i biblioteki natywne. Potrzebne są dwie maszyny z dwoma kontami Steam, na obu uruchomiony Steam.

- Maszyna A: `SteamCheck --host` wypisuje swój SteamID i czeka.
- Maszyna B: `SteamCheck --join <SteamID A>` wykonuje scenariusz: połączenie; 1000 wiadomości niezawodnych w obie strony w kolejności; 256 KiB niezawodnie; 1000 wiadomości zawodnych po 1150 B (liczba doręczonych, wszystkie nienaruszone); `GetRemoteEndpoint` po obu stronach zwraca SteamID drugiej; rozłączenie z B, które A widzi jako `Remote`; ponowne połączenie; zabicie procesu A, które B widzi jako `Timeout`.

Każdy krok wypisuje `PASS` albo `FAIL`. Wynik obu maszyn (data, konta, wersja Steamworks.NET) trafia do opisu PR.

## Kroki

- [x] Typy i interfejs w `Horyzont.Net.Transport`; testy kontraktu i `UdpLossyRelay` (czerwone).
- [x] `InMemoryNetwork` i `InMemoryTransport` z symulacją łącza i `Partition`; testy zielone.
- [x] `UdpTransport`; testy zielone na Windows (lokalnie); Linux dopiero w CI.
- [ ] `SteamTransport` i `SteamCheck` napisane i budują się; ręczny scenariusz na dwóch maszynach i wynik w PR do zrobienia przez człowieka.
- [ ] Komentarze XML gotowe (build z `GenerateDocumentationFile` przechodzi); zielone CI po wypchnięciu gałęzi.

## Odstępstwa

- `LiteNetLib` 2.1.4 (wersja już przypięta w `Directory.Packages.props`); pole `IPv6Enabled = false`, serwer wiąże się na adresie IPv4 podanym w `UdpEndpoint`.
- `MaxUnreliablePayload` w UDP to stała `1164 - 1 = 1163` (nagłówek `PacketProperty` jest w LiteNetLib niepubliczny), a nie odczyt z `NetPeer.GetMaxSinglePacketSize`; pokrywa to test `SendUnreliable_1150Bytes_Arrives` i `MaxUnreliablePayload_Default_IsAtLeast1150`.
- Limity: `MaxReliablePayload` = 512 KiB we wszystkich trzech transportach (maksimum Steama, co najmniej 256 KiB z planu), `MaxUnreliablePayload` = 1200 B w pamięci i na Steamie.
- `UdpTransport` i `SteamTransport` nie przyjmują `TimeProvider`: nie mają logiki zależnej od czasu (limity czasu robią LiteNetLib i Steam). `UdpTransport` przyjmuje opcjonalny `ILogger<UdpTransport>` (błędy sieci przez `[LoggerMessage]`).
- `Disconnect` w UDP czeka z wysłaniem rozłączenia, aż kolejka niezawodna połączenia będzie pusta (`GetPacketsCountInReliableQueue`), a zdarzenie `Local` zgłasza od razu w następnym `Poll`.
- Transport w pamięci: `RttMs` w `GetStats` wynika ze skonfigurowanych warunków łącza, `PacketLoss` to zaobserwowany odsetek porzuconych wiadomości zawodnych. `GetRemoteEndpoint` dla nieznanego połączenia rzuca `ArgumentException`, a `GetStats` zwraca zera.
- Kontrakt: `Send` przed `OnConnected` jest ignorowany (połączenie jeszcze nie istnieje dla wysyłania).
- `TransportOptions` jest zwykłym rekordem z jawnym konstruktorem (walidacja), więc nie ma dekonstrukcji pozycyjnej.
- Testy z `Timeout` w xUnit v3 mają wyłączone ostrzeżenie xUnit1069 w `TransportContractTests`: pętle oczekiwania mają własny termin.
- Test `Poll_Idle_DoesNotAllocate` mierzy połączoną parę (a nie sam serwer bez połączeń).
- Biblioteki natywne Steama: paczka NuGet Steamworks.NET 2024.8.0 zawiera tylko zarządzaną bibliotekę (bez `steam_api64.dll` i `libsteam_api.so`). `third_party/steamworks/` nie istnieje: SDK trzeba pobrać ręcznie z konta partnera Steam i dodać pliki (LFS); `SteamCheck` kopiuje je do wyjścia, jeśli są.
- `SteamTransport` i `SteamCheck` kompilują się, ale nie były uruchamiane (brak dwóch maszyn i kont Steam); ścieżka Steam jest niezweryfikowana.
- `SteamCheck` dodany do `Horyzont.sln` (budowany w CI, bez testów).

## Odbiór

Testy kontraktu przechodzą dla transportu w pamięci i UDP w CI, a scenariusz `SteamCheck` kończy się `PASS` na obu maszynach (wynik w PR).

## Pułapki

- Bufor z `OnReceived` jest ważny tylko w trakcie wywołania; kod wyżej musi skopiować dane, jeśli je przechowuje.
- Steam nie połączy P2P procesu z samym sobą, a jedno konto Steam gra naraz tylko na jednej maszynie: stąd dwa konta i dwie maszyny.
- LiteNetLib wysyła pingi z własnego wątku także wtedy, gdy nikt nie woła `PollEvents()`, więc „cisza” w testach to zawsze `Partition` na przekaźniku, nie brak `Poll`.
