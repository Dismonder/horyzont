# K3-17 — opis PR (Steam: lobby i zaproszenia)

Status: blokada kroku 0 ROZSTRZYGNIĘTA przez szew S10 w K3-00 (ICompositionPlatform, PlatformSession, ReleaseTrackedTransport, GameEnvironment.ProcessCleanup, wczesny `static partial ConfigurePlatform` oraz instancyjny `partial ConfigurePlatform()`). K3-17 nie edytuje `GameComposition.cs`; Steam jest składany w `GameComposition.Platform.cs` i plikach pakietu. Kroki 0–5 wykonane; kroki 6–7 częściowo (patrz Weryfikacja).

## Co zrobiono

- `src/Horyzont.Client/Steam/`: kontrakty (`LobbyVisibility`, `LobbyState`, `LobbyError`, `LobbyInfo`, `LobbyApiResult`, `SteamFriend`, `ISteamLobbyApi`), `SteamLobbyController` (stany, metadane z `ready` na końcu, generacje i terminy 10 s, polling właściciela co 1 s, lista znajomych, potwierdzenie zaproszenia), adapter `SteamworksLobbyApi` (Steamworks.NET 2024.8.0, mapowanie wyników), `SteamPlatformServices` (jedna inicjalizacja, `TryShutdown` jako ostatni krok procesu) i `SteamSessionBinder : ICompositionPlatform` (transport P2P hosta i klienta, `OnWorldStarted` → lobby FriendsOnly z pojemnością hosta, `OnShutdownRequested` → ready=0, `OnTransportsReleased` → LeaveLobby).
- `game/src/Boot/`: `GameComposition.Platform.cs` (oba overloady S10 i `CreatePlatformServices`), `SteamLifetime` (pompka raz na klatkę), `GameEnvironment.Platform`, argumenty `--steam`, `--steam-app-id`, `--lobby-visibility`, `+connect_lobby <id>` (także poza `--`, tylko token i wartość), `Boot.cs`.
- `HandshakeValidator`: tożsamość Steam przez endpoint inny niż `SteamEndpoint` → `IdentityMismatch` (reguła 17.3, wymaga przeglądu Opus). Zasada dopisana do `docs/ZASADY.md`.
- Bez zmian Core i protokołu; `ProtocolVersion` = 3.

## Odstępstwa i pytania

Pełna lista w sekcji „Odstępstwa i decyzje wykonawcy” planu. Najważniejsze: logika bindera i usług w `Horyzont.Client` (testowalna bez Godota), kontrakty uzupełnione o `GetMemberCount`/`Configure`/`SetSession`/`PrepareShutdown`/`SetFriendsVisible`; po `ConfirmJoin` UI (K3-18) musi sam zażądać zakończenia sesji. Otwarte pytania do właściciela: brak (warianty domyślne P1/P2/X1).

## Weryfikacja

- `dotnet build Horyzont.sln`: 0 błędów, 0 ostrzeżeń. `dotnet format Horyzont.sln --verify-no-changes`: czysto. `dotnet build game/Horyzont.Game.csproj`: 0 błędów, 0 ostrzeżeń.
- `dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"`: Client 261, Architecture 12, Net 117, Constructs 95, Integration 28, Persistence 124, Server 117 zielone, Core 1119/1120; jedyny błąd to `CharacterMotor_Step_DoesNotAllocate` pod obciążeniem współdzielonego PC (Core nietknięty), osobno zielony.
- NOT RUN (polecenie użytkownika, wspólny runner): gdUnit/silnik (`game/tests/Steam/*`, `PlatformSeamTests`), smoke `python tools/engine/build.py smoke`, realny Steam.
- Manual pending: `SteamInvite_TwoAccounts_JoinsHostThroughRelay`, `SteamLobby_EightPlayersThenHostExit_AllReceiveSessionEnd` (K4-19). Przegląd Opus reguły tożsamości w `HandshakeValidator` pending. Odbiór pakietu pozostaje pending do dowodu Manual.
