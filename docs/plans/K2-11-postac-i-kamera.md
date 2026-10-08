# K2-11 · Postać i kamera na kuli

| | |
| --- | --- |
| Prowadzi | Sonnet 5.5 (`claude --model sonnet`; `--model opusplan`, gdy krok 0 wykaże rozjazd kontraktów z K2-08/K2-09/K2-10) |
| Testy akceptacyjne | Sonnet 5.5; testy zmian w Core (`Motion/`, kodek `InputBatch`) przegląda Opus 5.5 przed kodem |
| Przegląd | Opus 5.5 w PR (nowe `src/Horyzont.Core/Motion/`, zmiana w `src/Horyzont.Core/Protocol/` i `Serialization/` z podbiciem `ProtocolVersion` do 3, dokładka do projektu `src/Horyzont.Client` z K2-10); reszta na bramce K2 |
| Zależy od | K2-08 (parametry i generator planety), K2-10 (tryby, pętla serwera, haki sesji); pośrednio K2-09 (`PlanetTerrain`, `VoxelTerrainService`) |
| Gałąź | `pakiet/k2-11-postac-i-kamera` |

**Cel:** gracz w trybie solo, u hosta i jako zdalny klient chodzi, biega, skacze i spada po kulistej planecie 4 km z grawitacją do środka, z kamerą TPP zza ramienia (domyślnie) i FPP, bez skoków kamery także nad biegunami; serwer liczy fizykę każdej postaci z jednego wejścia na tick i woła `ServerSimulation.UpdatePlayerKinematics`, klient od razu przewiduje własny ruch i wraca do stanu serwera przy rozjeździe, a w buildzie deweloperskim kamera i postać mogą swobodnie lecieć od orbity do powierzchni.

## Kontekst

- `docs/ZASADY.md`, punkty 2 (granice, rama rodzica, pozycja w świecie pochodna), 3 (Core, `Protocol/`, `Serialization/` → `ProtocolVersion`), 4, 6 (zero alokacji w ticku), 7, 8.
- `docs/spec/01-planeta.md`:
  - Cel i zakres (linie ~34–41: „postać: ruch po kuli, kamera FPP i TPP, predykcja ruchu po stronie klienta”);
  - Architektura (podział `/Root/Server` i `/Root/Client`; „Klient nie zmienia stanu świata… wyjątki to przewidywany ruch własnej postaci”; „Serwer liczy fizykę wszystkich graczy. Zdalny klient buduje kolizje terenu i budowli tylko na potrzeby predykcji; host ich nie dubluje”);
  - Model świata: tabela parametrów planety (grawitacja 9,81 w atmosferze, 0 poza nią; atmosfera 1000 m; rdzeń 400 m), „Współrzędne”, „Grawitacja i postać” (kapsuła `CharacterBody3D`, `up_direction` co tick fizyki, kamera wyrównuje się płynnie, FPP/TPP, stany: chodzenie, sprint, skok, spadanie, swobodny lot w buildzie deweloperskim), tabela narzędzi (wiersz „Kamera deweloperska: swobodny lot od orbity do powierzchni, tylko build deweloperski”);
  - Sieć: tabela „Ruch własnej postaci” i „Ruch innych graczy”, „Zainteresowanie i limity” (jedno wejście na tick z krótkiego bufora, nadmiar odrzucany);
  - Obsługa błędów: „Duży błąd predykcji ruchu: natychmiastowe przestawienie postaci; mały błąd jest wygładzany”;
  - Kryteria ukończenia: „Kamera przechodzi płynnie od orbity do powierzchni… bez przycięć dłuższych niż 50 ms”;
  - Ryzyka i otwarte kwestie: „Domyślna kamera: widok zza ramienia, pierwsza osoba zawsze dostępna”.
- `docs/plan/plan-projektu.md`, tabela K2 (K2-11: „obejście planety bez skoków kamery; lot od orbity do powierzchni”) i K3-14 (predykcja, korekta, interpolacja przy RTT 100 ms i 5% utraty).
- `docs/plans/K1-07-sesja-i-protokol.md`: „Haki gry”, „Limity” (bufor wejścia, `TryTakeInput` jedno wejście na wywołanie, klient powtarza trzy ostatnie wejścia), stany serwera (`InputBatch` w `InGame`).
- Przegląd bramki K1: `C:/Users/Damia/AgentTools/logs/results/horyzont-k1-review.md` (B3 skrót pozy offline, lekcje 1–6).
- Plany siostrzane: `K2-09-adapter-voxel-tools.md` (sekcja „Gra: `VoxelTerrainService`, `PlanetTerrain`, `TerrainStream`”, tabela „Kontrakty dla innych pakietów”), `K2-10-tryby-gry.md` (kanoniczna kolejność zamykania), `K2-13-narzedzia.md` (tabela „Kontrakty konsumowane”, decyzje 1 i 10, pytanie P6).
- Plany i kod scalonych K2-08 i K2-10 (krok 0 mapuje ich rzeczywiste nazwy).

Kod, na którym pakiet buduje (stan `main` 884d40d, sprawdzony):

| Co | Gdzie |
| --- | --- |
| `ServerSimulation.UpdatePlayerKinematics(PlayerId, FrameRef, uint parentSeq, Vector3 position, Quaternion bodyRotation, float pitch) → bool` (poza dziennikiem; odrzuca nieaktualną ramę/`ParentSeq` i złą pozę) | `src/Horyzont.Core/Simulation/ServerSimulation.cs:214` |
| `PlayerState` (`Frame`, `ParentSeq`, `Position` = stopy, `BodyRotation`, `Pitch`, `IsOnline`, `EyePosition(rules)` = stopy + `UpAt(stopy)` · `EyeHeight`) | `src/Horyzont.Core/World/PlayerState.cs` |
| `TerrainMath.UpAt(Vector3)` (+Y w środku) | `src/Horyzont.Core/World/Terrain/TerrainMath.cs:16` |
| `GameRules` (`TickRate` 60, `PlayerCapsuleRadius` 0,4, `PlayerCapsuleHeight` 1,8, `EyeHeight` 1,62 — „założenia do potwierdzenia w pakiecie 11”) | `src/Horyzont.Core/Simulation/GameRules.cs` |
| `Geometry.IsRotation` (tolerancja 1e-3), `OperationApplier.IsPose` (internal) | `src/Horyzont.Core/Simulation/Geometry.cs`, `OperationApplier.cs:397` |
| `PlanetParams` (`RadiusMeters`, `Gravity` 9,81, `AtmosphereHeightMeters` 1000, `CoreDepthMeters` 400) | `src/Horyzont.Core/World/PlanetParams.cs` |
| `PlayerInput(ClientTick, MoveX, MoveY, Yaw, Pitch, Buttons)`, `InputButtons` (Jump 1, Sprint 2, Crouch 4, Primary 8, Secondary 16), `InputBatch(FirstSeq, Inputs 1–5)` | `src/Horyzont.Core/Protocol/Messages/` |
| Kodek: osie `MoveX`/`MoveY` kwantyzowane do `sbyte` (`Quantization.QuantizeAxis`), `Yaw`/`Pitch` jako `float`, maska przycisków `0x1F` (koder `ArgumentException`, dekoder `ProtocolViolationException`) | `src/Horyzont.Core/Serialization/MessageCodec.cs:289–310, 610–640` |
| `Snapshot(SnapshotId, ServerTick, BaselineSnapshotId, LastProcessedInputSeq, Frames, Entities)`, `EntityState(Entity, Fields, FrameIndex, ParentSeq, Position, Rotation, Velocity, byte MotionFlags)`, `SnapshotFrame(FrameRef, ParentSeq)` | `src/Horyzont.Core/Protocol/Messages/` |
| `ProtocolConstants.ProtocolVersion = 1`, `MaxInputsPerBatch = 5` | `src/Horyzont.Core/Protocol/ProtocolConstants.cs` |
| `ServerSession.TryTakeInput(PlayerId, out PlayerInput, out uint seq)`, `Send(PlayerId, Message)`; `ClientSession.SendInputs(uint firstSeq, IReadOnlyList<PlayerInput>)`; `IServerGameHooks.OnPlayerReady`/`OnPlayerLeft`; `IClientGameHooks.OnServerHello`/`OnMessage`/`OnDisconnected` | `src/Horyzont.Net/Session/` |
| Projekt Godota: `GodotFloat64 = true` (`real_t = double`), `physics_ticks_per_second = 60`, `game/src/{Boot,Client,Server,Shared}` | `game/` (K1-02 etap B) |

## Warunki wejścia

Pakiet nie rusza, dopóki każdy punkt nie jest spełniony na `main` z zielonym CI (lekcja 4):

1. K2-08 i K2-10 scalone, ich checkboxy odhaczone albo z wpisem „odroczone do X” (lekcja 5); K2-10 z E3–E9 zamkniętymi albo odroczonymi.
2. Poprawka B3 z bramki K1 (skrót pozy graczy offline) scalona — ten pakiet zasila `Position`/`BodyRotation`/`Pitch` co tick i polega na tym, że poza po wyjściu trafia do skrótu.
3. Uprząż testów silnika bez grafiki (gdUnit4Net, `game/tests/`, z K2-08) działa w jobie `game` na Windows i Linux.
4. Zielony `python tools/engine/build.py smoke` na `main`.

## Zakres

- **Core, nowy katalog `src/Horyzont.Core/Motion/`** (poza ścieżkami formatu): rama powierzchni i konwencja kąta `Yaw` (`SurfaceFrame`), reguły ruchu (`MovementRules`), tryb ruchu (`MovementMode`), czysty krok silnika ruchu (`CharacterMotor`) wspólny dla serwera i predykcji klienta.
- **Protokół:** bit `InputButtons.DevFly = 32`, maska `0x3F` w kodeku, `ProtocolVersion` 2 → 3 (K2-10 podbija 1 → 2; reguła D4 w „Protokół”), dokumentacja znaczenia `PlayerInput.Yaw` i `EntityState.MotionFlags` (Pytanie P1).
- **Projekt bez Godota `src/Horyzont.Client`** + `tests/Horyzont.Client.Tests` (zakłada je K2-10; ten pakiet dokłada katalogi `Camera/`, `Movement/`, `Dev/`; K2-13 dokłada `Tools/`): śledzenie orientacji, matematyka kamery, wygładzanie „góry” kamery, numeracja i historia wejść, bufor predykcji z korektą, monitor ciągłości kamery, interfejs `ILocalPlayerView` dla K2-13.
- **Gra (`game/`):** wspólne `PlayerBody` (`CharacterBody3D`), konwersje ram, warstwy kolizji; serwerowe `ServerCharacters` (ciała wszystkich graczy, jedno wejście na tick, `UpdatePlayerKinematics`, widzowie `VoxelViewer` z kolizjami, oczekiwanie na grunt, punkt startu) i minimalny nadawca stanu własnej postaci; klienckie `LocalPlayer` (wejście, predykcja, korekta), `CameraRig` (TPP zza ramienia / FPP), `DevFlyCamera` z własnym widzem terenu.
- Wpięcie w kod K2-10: wywołanie `ServerCharacters.Step` w pętli serwera, haki dołączenia i wyjścia, `OnShutdown` w kanonicznej kolejności zamykania K2-10, węzły klienta w composition root.
- **Cykl dnia i nocy (decyzja bramki D12):** K2-08 dostarcza czystą funkcję `DayNightCycle.SunDirection` i węzeł `SunLight`; ten pakiet wpina je w scenę klienta i zasila tickiem serwera (sekcja „Cykl dnia i nocy”), z testem silnika.
- Testy: .NET (Core, Client, integracyjne pozy), silnika (gdUnit4Net), ręczne (obejście planety, lot z orbity).

Poza zakresem:

| Co | Gdzie |
| --- | --- |
| Wygładzanie małych błędów predykcji, interpolacja innych graczy z buforem 100 ms, strojenie progów przy RTT 100 ms i 5% utraty | K3-14 (dostaje `PredictionBuffer` i `CorrectionPolicy` z tego pakietu) |
| Pełne migawki wszystkich bytów, kompresja różnicowa, `SnapshotAck`, zainteresowanie 500 m, spawn/despawn innych graczy u klienta | K3-15 (zastępuje `OwnStateSender`) |
| Widok i kolizje budowli (warstwa `Constructs` jest tu tylko zarezerwowana) | K2-12 |
| Narzędzia, celowanie, `AimRayBuilder` | K2-13 (czyta `ILocalPlayerView`) |
| Generator, `VoxelLodTerrain`, LOD, „bez dziur w terenie i przycięć > 50 ms” przy strumieniowaniu | K2-08 / K2-09 (ten pakiet tylko przesuwa widza za kamerą i raportuje czasy klatek) |
| Docelowa postać, animacje, model | K8-50 (tu placeholder: kapsuła) |
| Przepięcia ram (siedzenia, statki), `FrameMath` w podwójnej precyzji | K5-20 i dalej (tu rama zawsze planety, `ParentSeq` przepisywany ze stanu) |
| Kucanie jako stan ruchu na ziemi | poza specyfikacją; `Crouch` służy tylko do opadania w locie deweloperskim |

## Projekt

### Pliki

```
src/Horyzont.Core/Motion/
  SurfaceFrame.cs            rama styczna, Yaw <-> obrót ciała, transport równoległy
  MovementRules.cs           stałe ruchu (record z wartościami domyślnymi)
  MovementMode.cs            enum: tryb ruchu = wartość bajtu EntityState.MotionFlags
  MotorInput.cs              readonly record struct: wejście po dekwantyzacji i walidacji
  MotorContext.cs            readonly record struct: stan ciała przed krokiem
  MotorOutput.cs             readonly record struct: żądana prędkość, góra, obrót, tryb
  CharacterMotor.cs          czysty krok ruchu (bez Godota)
src/Horyzont.Core/Protocol/Messages/InputButtons.cs      + DevFly = 32
src/Horyzont.Core/Protocol/Messages/PlayerInput.cs       dokumentacja Yaw (SurfaceFrame)
src/Horyzont.Core/Protocol/Messages/EntityState.cs       dokumentacja MotionFlags (MovementMode)
src/Horyzont.Core/Protocol/ProtocolConstants.cs          ProtocolVersion = 3, InputButtonsMask = 0x3F
src/Horyzont.Core/Serialization/MessageCodec.cs          maska z ProtocolConstants.InputButtonsMask

src/Horyzont.Client/                      (projekt z K2-10; referencje: Core, Net; bez Godota)
  ILocalPlayerView.cs                     kontrakt dla K2-13
  CameraMode.cs
  Camera/OrientationTracker.cs            ciągła orientacja ciała + pitch (mysz, transport po kuli)
  Camera/CameraRigMath.cs                 początek i kierunek kamery FPP/TPP
  Camera/CameraUpSmoother.cs              płynne wyrównanie „góry” kamery
  Camera/CameraContinuityMonitor.cs       wykrywanie skoków kamery (testy, HUD dev, log)
  Movement/InputSequencer.cs              numeracja wejść, paczka co 2 ticki: 2 nowe + 3 powtórzenia
  Movement/PredictionBuffer.cs            historia wejść i stanów, decyzja o korekcie
  Movement/PredictedState.cs              readonly record struct
  Movement/CorrectionPolicy.cs            progi korekty (K3-14 dokłada wygładzanie)
  Movement/OwnStateValidator.cs           walidacja migawki własnej postaci jak danych wrogich
  Dev/DevFlightSpeed.cs                   prędkość kamery dev zależna od wysokości
  ClientLog.cs                            [LoggerMessage]
tests/Horyzont.Client.Tests/              xUnit v3 (mtp-off), FsCheck
tests/Horyzont.Testing/Motion/IdealSphereBody.cs   integrator: idealna kula zamiast MoveAndSlide (testy .NET)

game/src/Shared/Frames/PlanetFrames.cs    jedyna konwersja Godot (double) <-> rama planety (System.Numerics)
game/src/Shared/Physics/CollisionLayers.cs
game/src/Shared/Character/PlayerBody.cs   CharacterBody3D + CapsuleShape3D z GameRules; Step(MotorInput)
game/src/Shared/Character/GroundProbe.cs  czy pod stopami jest już kolizja terenu
game/scenes/Character/PlayerBody.tscn     kapsuła-placeholder
game/src/Server/Characters/ServerCharacters.cs
game/src/Server/Characters/IInputSource.cs + SessionInputSource.cs
game/src/Server/Characters/SpawnLocator.cs
game/src/Server/Characters/OwnStateSender.cs
game/src/Server/Characters/ServerCharactersLog.cs
game/src/Client/Character/LocalPlayer.cs
game/src/Client/Character/LocalInputReader.cs
game/src/Client/Character/CameraRig.cs
game/src/Client/Character/DevFlyCamera.cs
game/src/Client/Sky/ClientDayNight.cs      zasila SunLight (K2-08) szacowanym tickiem serwera
game/tests/Character/                      testy silnika (gdUnit4Net)
tests/Horyzont.Core.Tests/Motion/          testy SurfaceFrame, CharacterMotor
tests/Horyzont.Integration.Tests/Movement/ (albo Core.Tests, jeśli projekt integracyjny nie istnieje) test pozy live = replay = checkpoint + ogon
```

### Rama powierzchni i konwencja `Yaw` (decyzja główna)

Na kuli nie istnieje ciągłe pole kierunków stycznych bez osobliwości, więc `PlayerInput.Yaw` (bezwzględny kąt w radianach, już w formacie) potrzebuje jawnej ramy odniesienia. Ustalamy ją raz, w Core, i obie strony liczą tą samą funkcją:

```csharp
namespace Horyzont.Core.Motion;

public static class SurfaceFrame
{
    /// <summary>Below this |projection| the +Y axis is too close to "up" and +Z is used instead (poles).</summary>
    public const float PoleFallbackThreshold = 1e-4f;

    /// <summary>"North" tangent at up: normalize(ProjectOnPlane(+Y, up)); near the poles normalize(ProjectOnPlane(+Z, up)).</summary>
    public static Vector3 Reference(Vector3 up);

    /// <summary>Body rotation: basis Y = up, forward (−Z) = Reference(up) rotated by yaw about up (right-handed). Normalized.</summary>
    public static Quaternion FromYaw(Vector3 up, float yaw);

    /// <summary>Yaw of the body's forward projected on the tangent plane, in (−π, π]; 0 when the projection degenerates.</summary>
    public static float YawOf(Quaternion bodyRotation, Vector3 up);

    /// <summary>Parallel transport: the minimal rotation taking the body's up to newUp, applied to the body. Normalized.</summary>
    public static Quaternion TransportToUp(Quaternion bodyRotation, Vector3 newUp);
}
```

- **Serwer** odtwarza obrót ciała co tick z `FromYaw(UpAt(stopy przed krokiem), input.Yaw)`.
- **Klient** nie trzyma kąta, tylko ciągły obrót ciała (`OrientationTracker`): co tick `TransportToUp` do nowej „góry”, potem obrót myszą wokół „góry”; `Yaw` do wejścia liczy `YawOf` z tej orientacji i z tej samej przewidzianej pozycji stóp, którą serwer będzie miał przed krokiem. Dzięki temu kamera nie kręci się przy biegunach (wartość `Yaw` skacze w pobliżu osi, orientacja nie).
- Osobliwość: w odległości ≲ 0,4 m od osi Y (`PoleFallbackThreshold` · 4000 m) rama przełącza się na +Z; rozjazd pozycji klient–serwer tuż przy biegunie może dać inny kierunek ruchu na serwerze → zwykła korekta. Punkt startu leży na równiku (Pytanie P5).

### Ruch (`CharacterMotor`, Core)

```csharp
namespace Horyzont.Core.Motion;

public enum MovementMode : byte { Frozen = 0, Walking = 1, Sprinting = 2, Jumping = 3, Falling = 4, DevFlying = 5 }

public readonly record struct MotorInput(float MoveX, float MoveY, float Yaw, float Pitch, InputButtons Buttons)
{
    /// Dequantizes axes exactly as the codec (Quantization), clamps |move| ≤ 1, wraps yaw to (−π, π],
    /// clamps pitch to ±MovementRules.MaxPitch; non-finite yaw/pitch → Neutral(previous) and returns false.
    public static bool TryFrom(in PlayerInput input, MovementRules rules, out MotorInput result);
    public static MotorInput Neutral(float yaw, float pitch);
}

public readonly record struct MotorContext(
    Vector3 Feet, Vector3 Velocity, bool OnFloor, MovementMode PreviousMode, PlanetParams Planet, bool DevFlightAllowed);

public readonly record struct MotorOutput(Vector3 DesiredVelocity, Vector3 Up, Quaternion BodyRotation, MovementMode Mode);

public static class CharacterMotor
{
    /// Pure, allocation-free. dt = 1 / GameRules.TickRate. The caller applies DesiredVelocity (MoveAndSlide or IdealSphereBody).
    public static MotorOutput Step(in MotorInput input, in MotorContext context, MovementRules rules, float dt);

    /// Gravity vector: −UpAt(feet) · Planet.Gravity when |feet| ≤ RadiusMeters + AtmosphereHeightMeters, else zero (spec, Model świata).
    public static Vector3 GravityAt(Vector3 feet, PlanetParams planet);
}
```

```csharp
public sealed record MovementRules
{
    public float WalkSpeed { get; init; } = 4.5f;               // m/s
    public float SprintSpeed { get; init; } = 7.5f;
    public float JumpSpeed { get; init; } = 5.0f;               // ≈ 1,27 m przy 9,81
    public float GroundAcceleration { get; init; } = 40f;       // m/s²
    public float GroundDeceleration { get; init; } = 50f;
    public float AirAcceleration { get; init; } = 8f;
    public float MaxFallSpeed { get; init; } = 60f;
    public float FloorMaxAngleDegrees { get; init; } = 46f;
    public float FloorSnapLength { get; init; } = 0.3f;
    public float MaxPitch { get; init; } = 1.5533f;             // 89°
    public float DevFlySpeed { get; init; } = 20f;
    public float DevFlySprintSpeed { get; init; } = 150f;
    public float MaxDevFlightRadiusFactor { get; init; } = 2.25f; // |stopy| ≤ 2,25 R (9 km przy 4 km; float VT do ~10 km)
    public int MaxStarvedTicks { get; init; } = 6;              // 100 ms bez wejścia → wejście neutralne
    public int GroundWaitDeadlineTicks { get; init; } = 1800;   // 30 s czekania na kolizję terenu
    public float CoreRescueMarginMeters { get; init; } = 2f;
}
```

Zasady kroku: „góra” = `UpAt(stopy)`; obrót ciała = `FromYaw`; kierunek ruchu z `MoveX`/`MoveY` w płaszczyźnie stycznej ciała; składowa pozioma prędkości dąży do celu z przyspieszeniem naziemnym albo powietrznym, składowa radialna z grawitacji (obcięta do `MaxFallSpeed`); `Jump` tylko przy `OnFloor` (zbocze, nie trzymanie: skok raz na przyziemienie); `DevFly` przy `DevFlightAllowed`: brak grawitacji, ruch w kierunku patrzenia (yaw + pitch), `Jump` w górę, `Crouch` w dół, prędkość `DevFlySpeed`/`DevFlySprintSpeed`, promień obcięty do `MaxDevFlightRadiusFactor` · R. Bez `DevFlightAllowed` bit jest ignorowany (nie naruszenie protokołu). `MovementRules` nie wpływa na `Apply`/`Replay` (ruch jest poza dziennikiem), więc może być rekordem `init` (lekcja 1 nie dotyczy); obie strony używają tych samych wartości, bo skrót builda w uścisku dłoni wymusza ten sam build.

### Protokół (Pytanie P1)

Reguła podbicia (decyzja bramki D4, wspólna z K2-10): K2-10 podbija `ProtocolVersion` z 1 do 2, ten pakiet z 2 do 3. Ustaw wartość na (wartość na `main` w chwili scalenia) + 1; test sprawdza udokumentowaną wartość (`ProtocolVersion_IsThree`); rebase na `main` z innym podbiciem aktualizuje i stałą, i test, i ten plan.

- `InputButtons.DevFly = 32` („trzymany” stan: klient ustawia bit w każdym wejściu, dopóki lot jest włączony).
- `ProtocolConstants.InputButtonsMask = 0x3F` (nowa stała), kodek używa jej w obu kierunkach; `ProtocolVersion = 3` (ZASADY pkt 3: zmiana w `Protocol/` i `Serialization/`). Stary klient dostaje czytelną odmowę uścisku dłoni (zamrożony `HandshakeReject`).
- `EntityState.MotionFlags`: wartość bajtu = `(byte)MovementMode`; wartości > 5 klient traktuje jak `Falling` i loguje (dokumentacja w XML, bez zmiany bajtów formatu).
- Brak nowych kodów komunikatów, operacji, intencji i odmów (tabela zakresów K1-03 bez zmian).

### Serwer (`game/src/Server/Characters/`)

```csharp
namespace Horyzont.Game.Server.Characters;

public interface IInputSource { bool TryTakeInput(PlayerId player, out PlayerInput input, out uint seq); }
public sealed class SessionInputSource(ServerSession session) : IInputSource;   // adapter

public sealed partial class ServerCharacters : Node3D   // pod /Root/Server; _PhysicsProcess wyłączony
{
    public ServerCharacters(ServerSimulation simulation, IInputSource inputs, VoxelTerrainService terrain,
                            GameRules rules, MovementRules movement, bool devFlightAllowed, ILogger<ServerCharacters> logger);
    public void Spawn(PlayerId player);              // OnPlayerReady: ciało w pozie z PlayerState, tryb Frozen do gruntu
    public void Despawn(PlayerId player);            // OnPlayerLeft, PRZED PlayerLeaveOp: ostatnie UpdatePlayerKinematics
    public void DespawnAll();                        // zamknięcie, przed LeaveAllOnlinePlayers
    public void Step(Tick tick);                     // raz na tick fizyki, wołane jawnie przez pętlę serwera K2-10
    public bool TryGetState(PlayerId player, out ServerCharacterState state);   // dla OwnStateSender i testów
}

public readonly record struct ServerCharacterState(
    PlanetId Planet, FrameRef Frame, uint ParentSeq, Vector3 Feet, Quaternion BodyRotation, float Pitch,
    Vector3 Velocity, MovementMode Mode, uint LastProcessedInputSeq);
```

`Step` dla każdego ciała w kolejności rosnącego `PlayerId` (lista posortowana przy `Spawn`, nie iteracja po słowniku):

1. `TryTakeInput` → `MotorInput.TryFrom`; zła wartość → wejście neutralne + log z ograniczeniem częstości; brak wejścia → licznik głodu; po `MaxStarvedTicks` kolejnych tickach bez wejścia krok z `MotorInput.Neutral(ostatni yaw, ostatni pitch)` (lekcja 6: termin), do tego czasu ciało stoi (stan jest funkcją ciągu wejść, co ułatwia predykcję).
2. W trybie `Frozen` (po spawnie i po ratunku spod rdzenia): wejścia są zużywane i potwierdzane, ale ciało nie rusza się, dopóki `GroundProbe` nie trafi kolizji terenu pod stopami; wtedy stopy na trafienie + 0,05 m wzdłuż „góry”, tryb `Walking`. Po `GroundWaitDeadlineTicks` bez trafienia: ostrzeżenie i odmrożenie (gracz w jaskini albo nad pustką nie wisi w nieskończoność).
3. `PlayerBody.Step(motorInput)`: `UpDirection = up`, `Velocity = DesiredVelocity`, `MoveAndSlide()`, odczyt stóp, prędkości, `IsOnFloor()`.
4. Ratunek: |stopy| < R − `CoreDepthMeters` − `CoreRescueMarginMeters` → przestawienie nad powierzchnię (`SpawnLocator`), `Frozen`, log.
5. `ServerSimulation.UpdatePlayerKinematics(player, state.Frame, state.ParentSeq, stopy(float), Quaternion.Normalize(obrót), pitch)` — obrót normalizowany przed wywołaniem (lekcja 3c, `WriteQuaternion` przy `PlayerLeaveOp` rzuca poza 1e-3). Wynik `false` (ramy/`ParentSeq` nieaktualne, gracz offline) → log, ciało przestawiane na pozę z `PlayerState` w następnym ticku.

Kolejność w ticku serwera (kontrakt dla pętli K2-10): `ServerSession.Poll(tick)` (intencje widzą kinematykę z poprzedniego ticku) → **`ServerCharacters.Step(tick)`** → tick `ServerSimulation` → co 3. tick (20 Hz) **`OwnStateSender.Send(tick)`**.

Widzowie: każde ciało serwera ma dziecko `VoxelViewer` (`requires_collisions = true`, `requires_visuals = false`, zasięg `ServerViewerDistance` = 64 m ≥ zasięg narzędzi 8 m + zapas na `IsAreaLoaded`). Rozpoznanie w kroku 0 potwierdza, że `VoxelLodTerrain` buduje kolizje przy takim widzu na serwerze bez grafiki (headless, Linux); inaczej Pytanie do właściciela przed krokiem 7.

`SpawnLocator` (czysta funkcja, bez wyjątków, więc nie łamie atomowości dołączenia z lekcji 3): poza dla nowego gracza = kierunek startu (domyślnie +X, równik) × (R + wysokość powierzchni z generatora CPU K2-08 + 2 m); bez funkcji wysokości w K2-08 → R + maksymalna rzeźba z profilu generatora + 2 m, a resztę robi oczekiwanie na grunt. K2-10 woła ją w części Prepare dołączenia nowej tożsamości; znana tożsamość dostaje pozę z zapisu.

`OwnStateSender`: co 3 ticki każdemu graczowi w grze pełna `Snapshot` (`BaselineSnapshotId = 0`, rosnący `SnapshotId` na gracza, `LastProcessedInputSeq`, jedna ramka planety, jeden `EntityState` własnej postaci z polami Position, Rotation, Velocity, MotionFlags) przez `ServerSession.Send`. To most do K3-15, który zastąpi go pełnym budowaniem migawek; nazwa i miejsce wywołania są kontraktem dla K3-15.

Kolejność zamknięcia: obowiązuje kanoniczna tabela K2-10 („Kolejność zamykania”, decyzja bramki D8). Ten pakiet wnosi do niej krok 3a: `ServerCharacters` jako `IServerTickParticipant` implementuje `OnShutdown` → **`DespawnAll()`** (ostatnia kinematyka, zdjęcie widzów) przed `LeaveAllOnlinePlayers`. Nic poza tym nie zmienia kolejności.

### Klient (`src/Horyzont.Client` + `game/src/Client/Character/`)

```csharp
namespace Horyzont.Client;

public enum CameraMode : byte { ThirdPerson = 0, FirstPerson = 1, DevFree = 2 }

/// <summary>State of the local player at the end of the latest client physics tick (not render-interpolated), planet frame.</summary>
public interface ILocalPlayerView
{
    bool IsActive { get; }                 // spawned, not Frozen, not disconnected
    PlanetId Planet { get; }
    Vector3 PredictedFeet { get; }
    Quaternion BodyRotation { get; }       // normalized
    float Pitch { get; }
    Vector3 Eye { get; }                   // feet + UpAt(feet) · GameRules.EyeHeight — exactly the server formula
    Vector3 CameraOrigin { get; }          // FPP: == Eye; TPP: behind the shoulder after the spring arm; DevFree: dev camera
    Vector3 CameraDirection { get; }       // unit, finite
    CameraMode CameraMode { get; }
}
```

- `OrientationTracker` (Client): `Advance(Vector3 newUp, float mouseYawDelta, float mousePitchDelta)`; pitch obcięty do `MaxPitch`; po teleporcie/korekcie `Reset(bodyRotation, pitch)`.
- `CameraRigMath.Compute(feet, bodyRotation, pitch, smoothedUp, mode, tpp offsets, GameRules) → (origin, direction)`; TPP: ramię 3,5 m za okiem, 0,5 m w prawo, 0,2 m w górę (Pytanie P2); kolizję ramienia robi `SpringArm3D` w `CameraRig` (maska: teren + budowle, z wykluczeniem własnego ciała), więc `CameraOrigin` w `ILocalPlayerView` to pozycja po kolizji.
- `CameraUpSmoother`: „góra” kamery dąży do „góry” ciała wykładniczo (τ = 0,12 s) z limitem 180°/s; po spawnie, korekcie z przestawieniem i wyjściu z kamery dev — natychmiast (`Snap`). Pozycja oka nigdy nie jest wygładzana (musi równać się wzorowi serwera, K2-13 decyzja 1).
- `InputSequencer` (spec „Tick i czas”, decyzja bramki D10): numery od 1, jedno wejście na tick zapisywane w historii **po** kwantyzacji osi (to samo, co zobaczy serwer); co 2 ticki jedna paczka `SendInputs(firstSeq, wejścia)` z dwoma nowymi wejściami i trzema poprzednimi jako powtórzenia, razem ≤ 5 (`MaxInputsPerBatch`); na początku sesji paczka ma tyle powtórzeń, ile jest wcześniejszych wejść. Rozłączenie albo korekta nie zmienia numeracji.
- `PredictionBuffer`: pierścień 128 wpisów (seq, `MotorInput`, stan po kroku); `Acknowledge(seq, serverState)`: brak wpisu albo rozjazd pozycji > `CorrectionPolicy.SnapDistance` (0,02 m) lub prędkości > 0,1 m/s → korekta: stan serwera + powtórzenie niepotwierdzonych wejść (najwyżej 128 `MoveAndSlide` w jednej klatce). Niepotwierdzonych > `MaxUnacknowledged` (120 = 2 s) → predykcja wstrzymana (ciało jedzie za migawkami), HUD dev „czekam na serwer”; rozłączenie czyści wszystko (lekcja 6).
- `OwnStateValidator`: migawka własnej postaci odrzucana i logowana, gdy byt ≠ `ServerHello.YourEntity`, `FrameIndex` poza `Frames`, rama nie jest planetą z `ServerHello.Planets`, wartości nieskończone, obrót poza tolerancją, `SnapshotId`/`ServerTick` niemonotoniczne (starsza migawka pomijana), `LastProcessedInputSeq` większy niż ostatnio wysłany.
- Bramka kolizji klienta: dopóki `GroundProbe` klienta nie widzi terenu pod przewidzianym ciałem (zdalny klient ładuje teren później niż serwer), klient nie przewiduje, tylko ustawia ciało ze stanu serwera; wejścia dalej wysyła i zapisuje.
- `LocalPlayer` (`/Root/Client`, `ProcessPhysicsPriority` niższy niż węzły narzędzi K2-13): w `_PhysicsProcess` czyta `LocalInputReader`, liczy wejście, krok predykcji, wysyła, aktualizuje `ILocalPlayerView`. Przewidziane ciało ma warstwę `ClientBodies` i maskę teren + budowle, więc u hosta nie zderza się z ciałami serwera w tym samym `World3D`.
- Widzowie klienta: kamera ma `VoxelViewer` z wizualizacją (zasięg = cała planeta, LOD z K2-08); zdalny klient dokłada widza z kolizjami przy własnym ciele, host nie (spec: host nie dubluje kolizji). W trybie dev widz wizualny jedzie z kamerą dev, widz kolizji zostaje przy ciele.
- Interpolacja renderu: ciało i kamera liczone w tickach 60 Hz; płynność przy innym FPS daje wbudowana interpolacja fizyki Godota (`physics/common/physics_interpolation = true`) z `ResetPhysicsInterpolation()` po teleporcie i korekcie z przestawieniem. Rozpoznanie w kroku 0 (zgodność z `SpringArm3D` i `Camera3D` w 4.7.2); inaczej własna interpolacja w `CameraRig` (wpis w Odstępstwach).

### Cykl dnia i nocy (decyzja bramki D12)

- K2-08 wystawia czystą funkcję `DayNightCycle.SunDirection(ulong tick, int tickRate, PlanetParams planet)` (doba z `planet.DayLengthSeconds`; bez zmian w Core, test jednostkowy) i węzeł sceny `SunLight.Apply(tick, tickRate, planet)`. Ten pakiet tylko je wpina.
- `ClientDayNight` (`/Root/Client`) w `_PhysicsProcess` ustawia `SunLight` z szacowanego ticku serwera: `ServerHello.CurrentTick` + ticki od odebrania, korygowany `Snapshot.ServerTick` z `OwnStateSender`; `DayLengthSeconds` z `PlanetParams` planety domowej z `ServerHello.Planets`, tick rate z `ServerHello.TickRate`. U hosta to ten sam tick co `ServerRuntime.State.CurrentTick`. Klient nie ma własnego zegara dnia.
- Bez stanu trwałego i bez wpływu na symulację (lekcja 2): kierunek słońca to funkcja ticku.

### Kamera deweloperska (`DevFlyCamera`)

- Tylko build deweloperski (`OS.IsDebugBuild()` albo flaga `--dev` z composition root K2-10). `F8` odłącza kamerę od ciała (`CameraMode.DevFree`), ciało stoi i dalej wysyła wejścia neutralne; `F9` przenosi kamerę „na orbitę”: 2R od środka (Pytanie P8), patrzy na środek planety.
- Lot 6 DoF bez kolizji, prędkość `DevFlightSpeed.At(wysokość)` = clamp(wysokość nad R · 0,5 s⁻¹, 5, 2000) m/s, przewijanie zmienia mnożnik; roll wyrównywany do lokalnej „góry” przez `CameraUpSmoother` (poza atmosferą też, kierunek od środka).
- Powrót (`F8`): kamera wraca do ciała z `Snap` (to zamierzone przestawienie, `CameraContinuityMonitor` go nie liczy).
- Lot postaci (`F` = `InputButtons.DevFly`) to osobna rzecz: postać leci z kolizjami, serwer to symuluje i pozwala na to tylko przy `devFlightAllowed` (solo i host w buildzie dev, serwer dedykowany z flagą `--dev`).

### Kontrakty wystawiane

| Kontrakt | Plik | Kto używa |
| --- | --- | --- |
| `SurfaceFrame` (`Reference`, `FromYaw`, `YawOf`, `TransportToUp`), znaczenie `PlayerInput.Yaw` | `src/Horyzont.Core/Motion/SurfaceFrame.cs` | K3-14, K5-20 (rama statku), K4-19 (boty) |
| `CharacterMotor.Step`, `CharacterMotor.GravityAt`, `MovementRules`, `MovementMode`, `MotorInput`/`MotorContext`/`MotorOutput` | `src/Horyzont.Core/Motion/` | K3-14 (predykcja i korekta), K4-19 (boty), Kosmos (grawitacja poza atmosferą) |
| `InputButtons.DevFly`, `ProtocolConstants.InputButtonsMask`, `ProtocolVersion = 3` | `src/Horyzont.Core/Protocol/` | wszyscy (uścisk dłoni) |
| `ILocalPlayerView` (przewidziane stopy, obrót ciała, pitch, oko wzorem serwera, kamera: początek, kierunek, `CameraMode`) | `src/Horyzont.Client/ILocalPlayerView.cs` (impl. `game/src/Client/Character/LocalPlayer.cs`) | K2-13 (`AimRayBuilder`, podgląd; przy `DevFree` narzędzia wyłączone), K3-18 (HUD) |
| `PlanetFrames.ToPlanetLocal(PlanetId, Godot.Vector3) → System.Numerics.Vector3`, `ToGodot(PlanetId, Vector3) → Godot.Vector3`, `ToNumerics`/`ToGodot` dla `Quaternion` (rzuca dla nieznanej planety) | `game/src/Shared/Frames/PlanetFrames.cs` | K2-13 (decyzja 10: „jedna funkcja konwersji z K2-11”), K2-12, K3-14 |
| `CollisionLayers` (`Terrain = 1`, `Constructs = 2`, `ServerBodies = 3`, `ClientBodies = 4`, numery warstw Godota 1–32; maski jako `const uint`) | `game/src/Shared/Physics/CollisionLayers.cs` | K2-09 (teren na warstwie `Terrain`), K2-12 (budowle na `Constructs`), K2-13 (promień podglądu) |
| `ServerCharacters.Step(Tick)` w pętli serwera i co tick `UpdatePlayerKinematics` dla każdego gracza online; `Spawn`/`Despawn`/`DespawnAll` (przez `IServerTickParticipant.OnShutdown`); `TryGetState` | `game/src/Server/Characters/ServerCharacters.cs` | K2-10 (pętla, haki, zamknięcie), K2-13 (oko serwera), K3-15 |
| `OwnStateSender` (20 Hz, pełna migawka własnej postaci) | `game/src/Server/Characters/OwnStateSender.cs` | K3-15 (zastępuje) |
| `PredictionBuffer`, `CorrectionPolicy`, `OwnStateValidator`, `InputSequencer` | `src/Horyzont.Client/Movement/` | K3-14 (wygładzanie, strojenie, testy łącza) |
| `SpawnLocator.ForNewPlayer(PlanetParams) → (Vector3 feet, Quaternion body)` | `game/src/Server/Characters/SpawnLocator.cs` | K2-10 (Prepare dołączenia) |
| Akcje `InputMap`: `move_forward` W, `move_back` S, `move_left` A, `move_right` D, `jump` Spacja, `sprint` Shift, `crouch` Ctrl, `camera_toggle` V, `dev_fly_toggle` F, `dev_camera_toggle` F8, `dev_camera_orbit` F9; mysz: obrót; Esc: zwolnienie kursora | `game/project.godot` | K2-13 (bez kolizji z 1, 2, LPM, PPM, kółkiem, M, R, T), K3-18 |

### Kontrakty konsumowane

Plany K2-08 i K2-10 nie są gotowe w chwili pisania tego planu, więc kolumna „Rzeczywisty typ” powstaje w kroku 0 po przeczytaniu scalonego kodu; brak zdolności zatrzymuje pakiet i wraca do pakietu, który ją miał dać (albo trafia do Odstępstw z przeglądem Opus).

| Potrzeba | Od | Rzeczywisty typ (krok 0) |
| --- | --- | --- |
| `PlanetTerrain` (węzeł w środku planety, jednostkowa baza; współrzędne voxeli = rama planety), `VoxelTerrainService.TryGetPlanet`, `IsAreaLoaded`; u hosta jedna instancja terenu dla serwera i klienta | K2-09 | `game/src/Terrain/PlanetTerrain.cs`, `VoxelTerrainService.cs` |
| Kolizje `VoxelLodTerrain` przy widzu `requires_collisions` na serwerze bez grafiki; teren na warstwie `CollisionLayers.Terrain` | K2-08 / K2-09 | |
| `PlanetParams` planety domowej, wysokość powierzchni w kierunku z generatora CPU (dla `SpawnLocator`; opcjonalnie), maksymalna rzeźba profilu; `DayNightCycle.SunDirection`, węzeł `SunLight` (D12) | K2-08 | |
| Pętla ticku serwera (miejsce na `ServerCharacters.Step` między `Poll` a tickiem symulacji), implementacja `IServerGameHooks` (`OnPlayerReady`, `OnPlayerLeft`, Prepare+Apply dołączenia), kanoniczna kolejność zamknięcia z `IServerTickParticipant.OnShutdown`, wątek symulacji = wątek główny Godota | K2-10 (+ K2-09) | |
| Tryb procesu (solo, host, klient, serwer dedykowany), flaga dev, composition root `game/src/Boot/`, `IClientGameHooks.OnMessage` → `LocalPlayer`, `ServerHello` (`YourPlayer`, `YourEntity`, `Planets`, `TickRate`) dla klienta | K2-10 | |
| Teren klienta zdalnego (osobny `PlanetTerrain` po stronie klienta) | K2-10 | |
| `ServerSimulation.UpdatePlayerKinematics`, `PlayerState` | K1-03 (sprawdzone) | `src/Horyzont.Core/Simulation/ServerSimulation.cs:214` |
| `ServerSession.TryTakeInput`/`Send`, `ClientSession.SendInputs` | K1-07 (sprawdzone) | `src/Horyzont.Net/Session/` |

### Decyzje

1. **Brak nowych pól stanu trwałego** (lekcja 2): prędkość, tryb ruchu, licznik głodu i stan predykcji żyją tylko w pamięci; po restarcie postać startuje z prędkością 0 w trybie `Frozen`. Zapisywane pozostają istniejące `Position`/`BodyRotation`/`Pitch` (skrót po B3). Test „live = replay = checkpoint + ogon” dla pozy po ruchu i wyjściu jest w tabeli testów, bo ten pakiet jest pierwszym, który zasila te pola z fizyki.
2. **Semantyka operacji bez zmian** (lekcja 1): `CharacterMotor` i `MovementRules` nie są czytane przez `OperationApplier`; walidator intencji dalej liczy oko z `PlayerState`.
3. **Normalizacja obrotu** przed każdym `UpdatePlayerKinematics` i w `EntityState` (lekcja 3c).
4. **Każda kolejka i każdy stan oczekiwania ma termin i limit** (lekcja 6): głód wejścia (`MaxStarvedTicks`), oczekiwanie na grunt (`GroundWaitDeadlineTicks`), niepotwierdzone wejścia (`MaxUnacknowledged`), powtórka korekty (≤ 128 kroków), log złych wejść z ograniczeniem częstości. Migawka od serwera i wejście od klienta walidowane jak dane wrogie.
5. **Gracze nie zderzają się ze sobą** w podprojekcie 1 (Pytanie P3): ciała serwera na `ServerBodies` z maską teren + budowle; przewidziane ciało klienta na `ClientBodies`. Blokada stawiania bloku w graczu zostaje w walidatorze (K1-03).
6. **Predykcja w K2-11 = natychmiastowa symulacja + korekta przez przestawienie i powtórkę**; wygładzanie małych błędów i strojenie pod opóźnienie robi K3-14 (Pytanie P4). Przy `InMemoryTransport` u hosta oczekiwana liczba korekt to 0.
7. **Zero alokacji w stanie ustalonym** (ZASADY pkt 6) dla `ServerCharacters.Step` (8 graczy), `CharacterMotor.Step`, `PredictionBuffer`, `InputSequencer`, `OrientationTracker`, `CameraRigMath`; listy wejść do `SendInputs` z bufora wielokrotnego użytku.
8. **Logowanie** przez `[LoggerMessage]` z `PlayerId` (serwer) i `SnapshotId`/seq (klient): odrzucone wejście, głód, odmrożenie po terminie, ratunek spod rdzenia, korekta (Debug), wstrzymanie predykcji (Warning), odrzucona migawka (Warning z ograniczeniem częstości).

## Pytania do właściciela

| # | Pytanie | Domyślnie, jeśli brak odpowiedzi przed krokiem, który go dotyczy |
| --- | --- | --- |
| P1 | Swobodny lot postaci w buildzie dev. Rozstrzygnięte na bramce K1: bit `InputButtons.DevFly`, podbicie `ProtocolVersion` do 3 po K2-10 (D4) | bit 32, maska `0x3F`, `ProtocolVersion = 3` (krok 3) |
| P2 | Stałe ruchu i kamery: chód 4,5 m/s, sprint 7,5 m/s, skok 5 m/s (~1,27 m), kapsuła 0,4 × 1,8 m, oczy 1,62 m (założenia K1-03 „do potwierdzenia w pakiecie 11”), ramię TPP 3,5 m za okiem, 0,5 m w prawo, 0,2 m w górę | wartości z tabeli; `GameRules` bez zmian |
| P3 | Czy gracze mają się zderzać ze sobą w podprojekcie 1? | nie |
| P4 | Podział predykcji: K2-11 przestawia i powtarza przy każdym rozjeździe > 2 cm, K3-14 dodaje wygładzanie i interpolację innych graczy. Zgoda? | zgoda |
| P5 | Rama odniesienia `Yaw`: bieguny na osi ±Y planety, punkt startu na równiku (+X); przy biegunie możliwe drobne korekty ruchu | zgoda |
| P6 | Punkt startu nowego gracza: kierunek stały (+X) czy losowy z ziarna świata? Wymaga od K2-08 wysokości powierzchni z generatora CPU | stały +X; bez funkcji wysokości K2-08 → start nad maksymalną rzeźbą i opadnięcie po załadowaniu kolizji |
| P7 | Brak wejścia od klienta: ciało stoi do 6 ticków (100 ms), potem serwer liczy wejście neutralne (grawitacja działa). Zgoda? | zgoda |
| P8 | „Orbita” dla kamery dev i testu lotu: 2R od środka (4000 m nad powierzchnią, powyżej atmosfery)? | 2R |
| P9 | Ręczny test „obejście planety” (25 km): mnożnik prędkości tylko w buildzie dev (np. ×8, ~7 min), czy obejście z normalnym sprintem (~56 min)? | mnożnik ×8 przez flagę `--dev-speed 8` (solo/host; serwer i klient w tym samym procesie biorą te same `MovementRules`) |

## Testy akceptacyjne (napisz najpierw)

.NET, `tests/Horyzont.Core.Tests/Motion/` (bez kategorii, chyba że podano):

| Test | Sprawdza |
| --- | --- |
| `SurfaceFrame_FromYawThenYawOf_RoundTrips` (FsCheck) | losowa „góra” z |up.Y| < 1 − 1e-6 i yaw ∈ (−π, π]: `YawOf(FromYaw(up, yaw), up)` = yaw ± 1e-4 (z zawinięciem) |
| `SurfaceFrame_FromYaw_BodyUpEqualsSurfaceUp` (FsCheck) | oś Y obrotu = up ± 1e-5, kwaternion jednostkowy ± 1e-6 |
| `SurfaceFrame_AtExactPoles_FiniteAndNormalized` | up = ±Y i ±Y ± 1e-7: `Reference`, `FromYaw`, `YawOf` skończone, wynik jednostkowy |
| `SurfaceFrame_TransportToUp_SmallStep_HeadingContinuous` | krok 0,01° po wielkim kole przez biegun: kąt między kolejnymi kierunkami „przód” ≤ krok + 1e-5 rad |
| `CharacterMotor_GravityInsideAtmosphere_TowardCenter` / `_OutsideAtmosphere_Zero` | `GravityAt` przy R + 999 m = −up · 9,81; przy R + 1001 m = 0 |
| `CharacterMotor_WalkDiagonal_SpeedCappedToWalk` / `_Sprint_CappedToSprint` | |v styczna| ≤ `WalkSpeed`/`SprintSpeed` + 1e-4 po ustaleniu |
| `CharacterMotor_Jump_OnlyOnFloor_OncePerLanding` | trzymany `Jump` w powietrzu nie dodaje prędkości; tryby `Jumping` → `Falling` → `Walking` |
| `CharacterMotor_DevFly_IgnoredWhenNotAllowed` / `_Allowed_NoGravityAndRadiusClamped` | bit bez zgody = chód; ze zgodą brak grawitacji, |stopy| ≤ 2,25 R |
| `MotorInput_NonFiniteOrOutOfRange_NeutralOrClamped` | NaN/∞ yaw/pitch → `false` i wejście neutralne; pitch obcięty do ±89°; osie jak w kodeku |
| `CharacterMotor_WalkGreatCircleThroughBothPoles_NoJumps` | `IdealSphereBody`, R = 4000, sprint bez myszy, ~201 tys. ticków: powrót do startu ± 0,5 % obwodu; „góra” ciała = up ± 1e-4 w każdym ticku; zmiana kierunku ciała ≤ 0,01°/tick; zmiana wysokości ≤ 1 cm/tick |
| `CharacterMotor_Step_DoesNotAllocate` | 10 000 kroków, `GC.GetAllocatedBytesForCurrentThread` = 0 |
| `InputBatch_DevFlyBit_RoundTrips` / `_BitsAbove0x3F_ProtocolViolation` / `ProtocolVersion_IsThree` | kodek i stała (zmiana w `Serialization/`, test w istniejącym projekcie testów kodeka) |
| `Kinematics_WalkThenLeave_LiveEqualsReplayEqualsCheckpointPlusTail` | `UpdatePlayerKinematics` z serią póz (także z obrotem o długości 1 ± 5e-4 po normalizacji), `PlayerLeaveOp`, checkpoint w środku: skrót stanu live = replay = checkpoint + ogon, poza bitowo równa (lekcja 2) |

.NET, `tests/Horyzont.Client.Tests/`:

| Test | Sprawdza |
| --- | --- |
| `OrientationTracker_MouseYaw_RotatesAboutLocalUp` / `_PitchClamped` | obrót wokół „góry”, pitch ±89° |
| `OrientationTracker_WalkOverPole_CameraForwardContinuous` | przejście przez biegun po ścieżce z `IdealSphereBody`: brak zmiany kierunku kamery > 0,05°/tick bez ruchu myszy, mimo skoku wartości `Yaw` |
| `CameraRigMath_FirstPerson_OriginEqualsServerEye` | `Vector3.Distance(origin, PlayerState.EyePosition(rules))` = 0 (ten sam wzór co `IntentValidator.TryGetEye`) |
| `CameraRigMath_ThirdPerson_BehindRightShoulder_LooksAlongView` | przesunięcie w bazie ciała zgodne z P2, kierunek = kierunek widoku (yaw + pitch) |
| `CameraUpSmoother_90DegreeChange_ConvergesMonotonically_RateLimited` / `_Snap_Immediate` | brak przeregulowania, krok ≤ 3°/tick przy 60 Hz |
| `CameraContinuityMonitor_FlagsJump_IgnoresMouseAndDeclaredSnaps` | skok kąta > 2°/tick bez myszy albo pozycji > v·dt + 1 cm → zgłoszenie; `Snap` nie jest zgłaszany |
| `InputSequencer_EveryTwoTicks_TwoNewPlusThreeRepeats` | paczka tylko co drugi tick; numery od 1; w ustalonym stanie 5 wejść (2 nowe + 3 powtórzenia), na starcie 2, 4, 5; `firstSeq` = najstarsze w paczce; nigdy > `MaxInputsPerBatch`; osie po kwantyzacji |
| `InputSequencer_LostPacket_RepeatsCoverGap` | utrata jednej paczki: następna zawiera oba zgubione wejścia w powtórzeniach |
| `PredictionBuffer_ServerMatches_NoCorrection` | zgodny stan → brak korekty, wpisy do seq usunięte |
| `PredictionBuffer_Mismatch_SnapsAndReplaysUnacknowledged` | rozjazd 5 cm → stan serwera + powtórka wejść seq+1..n w kolejności |
| `PredictionBuffer_OverMaxUnacknowledged_PausesPrediction` / `_Disconnect_Clears` | limit 120, wznowienie po potwierdzeniu |
| `OwnStateValidator_HostileSnapshots_Rejected` | NaN, zły byt, `FrameIndex` poza zakresem, nieznana planeta, obrót poza tolerancją, starszy `SnapshotId`, `LastProcessedInputSeq` > wysłanego → odrzucone i zalogowane |
| `PredictionBuffer_And_InputSequencer_SteadyState_DoNotAllocate` | 10 000 ticków, 0 bajtów |
| `Architecture_ClientDoesNotReferenceGodot` | w `tests/Horyzont.Architecture.Tests` |

Silnik (gdUnit4Net, `game/tests/Character/`, job `game`, kolekcja bez równoległości):

| Test | Kategoria | Sprawdza |
| --- | --- | --- |
| `PlayerBody_SpawnAboveVoxelSphere_FrozenUntilCollision_ThenStandsWithin5cm` | Slow | spawn przed kolizjami nie spada przez teren; po trafieniu `GroundProbe` stopy 0–5 cm nad powierzchnią, `IsOnFloor` |
| `PlayerBody_WalkAcrossPole_NoCameraJump` | Slow | planeta testowa bez szumu z własnego generatora kuli w fiksturze testu (`game/tests/Character/Fixtures/SphereGenerator`, SDF = |p| − R; nie profil K2-08, D11), ścieżka 300 m przez biegun: `CameraContinuityMonitor` = 0 zgłoszeń, `up_direction` = `UpAt` w każdym ticku |
| `PlayerBody_Walk2kmOnNoiseTerrain_CameraContinuity` | Slow | profil domyślny K2-08: 0 zgłoszeń skoków, brak przelotu przez teren (|stopy| ≥ wysokość terenu − 0,1 m) |
| `ServerCharacters_OneInputPerTick_ExcessDropped` | — | fałszywe `IInputSource`: 1 wejście na `Step`, `LastProcessedInputSeq` rośnie o 1 |
| `ServerCharacters_Starvation_NeutralAfterSixTicks` | — | 6 ticków bez ruchu, 7. z grawitacją |
| `ServerCharacters_EveryTick_UpdatesKinematicsWithNormalizedRotation` | — | `PlayerState` po każdym `Step`: |q| = 1 ± 1e-6, ramy i `ParentSeq` przepisane |
| `ServerCharacters_GroundWaitDeadline_UnfreezesAndLogs` | — | brak kolizji przez 1800 ticków → odmrożenie, wpis w logu |
| `ServerCharacters_BelowCore_RescuedToSurface` | — | stopy pod rdzeniem → przestawienie i `Frozen` |
| `ServerCharacters_Step_EightPlayers_DoesNotAllocate` | Slow | 600 ticków, 0 bajtów w kodzie C# (poza Godotem) |
| `HostLoop_InMemory_PredictionMatchesServer_ZeroCorrections` | Slow | solo/host, 600 ticków losowego wejścia (chód, sprint, skok): 0 korekt, |stopy klient − serwer| < 1 mm przy każdym potwierdzeniu |
| `RemoteClient_LinkLatency100ms_ConvergesAfterCorrection` | Slow | `InMemoryTransport` z opóźnieniem 100 ms i 5 % utraty: po zatrzymaniu się błąd < 1 cm w ≤ 2 migawki; liczba korekt raportowana (strojenie w K3-14) |
| `DevCamera_OrbitToSurface_ViewerFollows_TerrainReadyOnArrival` | Slow | kamera dev z 2R do 5 m nad powierzchnią w 60 s symulacji: widz wizualny przy kamerze w każdej klatce, obszar pod kamerą gotowy (`is_area_editable`, meshe LOD0) ≤ 5 s po dotarciu; raport maks. czasu klatki (bez progu; próg 50 ms należy do K2-08/K4-19) |
| `DayNight_SunFollowsServerTick` | — | solo: `SunLight` ma kierunek `DayNightCycle.SunDirection(tick serwera)` ± 1e-4 w kolejnych tickach; po `DayLengthSeconds` · tick rate ticków wraca do kierunku startowego; zdalny klient po `ServerHello` z `CurrentTick` ≠ 0 nie zaczyna od ticku 0 (D12) |
| `DevFly_Character_ServerAndClientAgree` | Slow | lot postaci z `DevFly` u hosta: 0 korekt; serwer bez zgody ignoruje bit |

Ręczne (`Manual`, wynik w PR):

| Test | Sprawdza |
| --- | --- |
| `Manual_WalkAroundPlanet` | solo, `--dev-speed 8` (P9), wielkie koło przez oba bieguny w TPP i FPP: `CameraContinuityMonitor` = 0 zgłoszeń, subiektywnie bez skoków, wrócić do punktu startu |
| `Manual_OrbitToSurface` | `F9` → lot do powierzchni i lądowanie kamery przy postaci (`F8`): brak dziur i widoczny teren; czasy klatek z HUD dev zapisane w PR |
| `Manual_HostAndRemoteClient` | host + zdalny klient przez UDP w LAN: oba chodzą, klient bez widocznych przeskoków przy RTT LAN |

## Kroki

- [ ] 0. Warunki wejścia sprawdzone; tabela „Kontrakty konsumowane” wypełniona rzeczywistymi typami K2-08/K2-09/K2-10. Rozpoznanie (wynik w Odstępstwach): (a) kolizje `VoxelLodTerrain` przy widzu bez wizualizacji na serwerze headless (Windows i Linux); (b) jak wykryć gotową kolizję pod stopami (`GroundProbe`: promień fizyki w dół na warstwie `Terrain`); (c) interpolacja fizyki Godota 4.7.2 z `CharacterBody3D`, `SpringArm3D`, `Camera3D`; (d) wielokrotne `MoveAndSlide` w jednym ticku (powtórka korekty) widzi nową pozycję ciała. Odpowiedzi na P1–P9 albo przyjęte wartości domyślne wpisane.
- [ ] 1. Testy `SurfaceFrame` (przegląd Opus przed kodem) → `SurfaceFrame` → zielone.
- [ ] 2. `IdealSphereBody` w `tests/Horyzont.Testing`; testy `CharacterMotor`/`MotorInput` (przegląd Opus) → `MovementRules`, `MovementMode`, `MotorInput`, `MotorContext`, `MotorOutput`, `CharacterMotor` → zielone, w tym obejście kuli i brak alokacji.
- [ ] 3. Testy kodeka → `InputButtons.DevFly`, `InputButtonsMask`, `ProtocolVersion = 3` (reguła D4), dokumentacja `PlayerInput.Yaw` i `EntityState.MotionFlags` → zielone (wszystkie istniejące testy uścisku dłoni i kodeka dalej zielone). Pominąć bez P1 = „tak” i wpisać „odroczone do X”.
- [ ] 4. Dokładka do `src/Horyzont.Client` i `tests/Horyzont.Client.Tests` z K2-10 (test architektury już jest); testy i kod `OrientationTracker`, `CameraRigMath`, `CameraUpSmoother`, `CameraContinuityMonitor`, `DevFlightSpeed`, `ILocalPlayerView`, `CameraMode`.
- [ ] 5. Testy i kod `InputSequencer`, `PredictionBuffer`, `PredictedState`, `CorrectionPolicy`, `OwnStateValidator`, `ClientLog` (z testami alokacji).
- [ ] 6. `PlanetFrames`, `CollisionLayers` (teren K2-09 przestawiony na warstwę `Terrain`, jeśli trzeba), `PlayerBody` + scena, `GroundProbe`; test silnika „spawn, zamrożenie, stanie na kuli”.
- [ ] 7. `IInputSource`/`SessionInputSource`, `ServerCharacters`, `SpawnLocator`, `OwnStateSender`, `ServerCharactersLog`; wpięcie w pętlę serwera K2-10 (kolejność ticku), haki `OnPlayerReady`/`OnPlayerLeft` (Despawn przed `PlayerLeaveOp`), Prepare dołączenia z `SpawnLocator`, `OnShutdown` → `DespawnAll` w kanonicznej kolejności K2-10; testy silnika serwera.
- [ ] 8. `LocalInputReader`, `LocalPlayer` (predykcja, korekta, bramka kolizji klienta, widzowie), `CameraRig` (TPP domyślnie, `V` przełącza FPP), akcje `InputMap`, rejestracja w composition root; testy `HostLoop_*`, `RemoteClient_*`.
- [ ] 9. `DevFlyCamera` (F8/F9, widz wizualny za kamerą), lot postaci `DevFly` po stronie klienta; testy `DevCamera_*`, `DevFly_*`.
- [ ] 9a. `ClientDayNight`: `SunLight` z K2-08 w scenie klienta, zasilany tickiem serwera; test `DayNight_SunFollowsServerTick` (D12).
- [ ] 10. Test pozy `Kinematics_WalkThenLeave_LiveEqualsReplayEqualsCheckpointPlusTail`.
- [ ] 11. Testy ręczne z tabeli, wyniki do opisu PR; dopisanie do `docs/ZASADY.md` pkt 2 reguły „konwersja Godot ↔ rama planety tylko przez `PlanetFrames`” i do pkt 3 informacji, że `SurfaceFrame` definiuje znaczenie `PlayerInput.Yaw` (zmiana = `ProtocolVersion`).
- [ ] 12. Komentarze XML w Core i Client; lokalnie `dotnet build Horyzont.sln`, `dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"`, `dotnet format Horyzont.sln --verify-no-changes`, `python tools/engine/build.py smoke`; CI zielone na gałęzi (Windows i Linux, z jobem `game`), po scaleniu zielone na `main` (lekcja 4).

## Odstępstwa i decyzje wykonawcy

(puste na start; wykonawca dopisuje wyniki rozpoznania z kroku 0, rzeczywiste nazwy z K2-08/K2-10 i każde odejście od planu)

## Odbiór

- Testy akceptacyjne z tabel przechodzą w CI (kategorie bez i `Slow`); testy `Manual` opisane w PR z liczbą zgłoszeń `CameraContinuityMonitor` i czasami klatek lotu z orbity.
- Kryterium planu projektu: obejście planety bez skoków kamery (`Manual_WalkAroundPlanet` + `CharacterMotor_WalkGreatCircleThroughBothPoles_NoJumps` + `PlayerBody_WalkAcrossPole_NoCameraJump`); lot od orbity do powierzchni (`DevCamera_OrbitToSurface_*` + `Manual_OrbitToSurface`).
- Przegląd Opus 5.5: `Motion/`, zmiana protokołu z `ProtocolVersion = 3`, `Horyzont.Client`, wpięcie w pętlę i haki K2-10.
- Każdy checkbox odhaczony albo z wpisem „odroczone do X” (lekcja 5); `TODO` tylko z odnośnikiem.
- K2-13 krok 0 może wypełnić swój wiersz „Pozycja stóp… kamera” typem `ILocalPlayerView`, a wiersz „UpdatePlayerKinematics co tick” — `ServerCharacters.Step`.
- Zielone CI na `main` po scaleniu.

## Pułapki

- **Kąt `Yaw` nie jest orientacją.** Klient nigdy nie odtwarza kamery z `Yaw` (skacze przy biegunach); trzyma ciągły kwaternion (`OrientationTracker`) i liczy `Yaw` tylko do wejścia. Serwer odwrotnie: zawsze `FromYaw` z własnej pozycji.
- **Kwantyzacja osi.** `MoveX`/`MoveY` idą jako `sbyte`; predykcja liczona z osiami przed kwantyzacją rozjeżdża się z serwerem o ułamki mm na tick i daje ciągłe korekty. Historia trzyma wejście po kwantyzacji.
- **`UpdatePlayerKinematics` zwraca `false`** dla nieaktualnej ramy/`ParentSeq` i złej pozy; nie ignorować — ciało musi wrócić do `PlayerState`. Nienormalizowany obrót z `Basis` Godota (double → float) potrafi przekroczyć 1e-3 po długim obracaniu; normalizacja zawsze przed wywołaniem.
- **Spawn przed kolizjami.** Voxel Tools buduje kolizje asynchronicznie po wczytaniu bloków; ciało bez trybu `Frozen` przelatuje przez planetę w pierwszych klatkach. To samo u zdalnego klienta (bramka kolizji klienta).
- **Dwa ciała jednego gracza u hosta** (serwer i predykcja) są w tym samym `World3D`; bez rozdzielnych warstw ciało predykcji odpycha ciało serwera i odwrotnie.
- **Widzowie a zamknięcie.** Widzowie ciał muszą zniknąć przed `VoxelTerrainService.ShutdownAsync`, inaczej strumień dostaje żądania po zamknięciu (K2-09: `Unavailable`, logi błędów).
- **Kolejność w ticku.** `ServerCharacters.Step` przed tickiem symulacji, po `Poll`; odwrotnie walidator intencji widziałby pozę starszą o dwa ticki, a tolerancja `MaxRayOriginErrorMeters` (2 m) zjada się przy sprincie z opóźnieniem (K2-13 pułapki).
- **`_PhysicsProcess` w dowolnej kolejności.** Nie polegać na kolejności węzłów Godota po stronie serwera; `ServerCharacters.Step` wołany jawnie. Po stronie klienta `LocalPlayer` ma `ProcessPhysicsPriority` niższy niż narzędzia K2-13, żeby czytały stan z tego samego ticku.
- **Kamera dev i widz.** Bez widza przy kamerze dev lot z orbity pokaże dziury (Clipbox ładuje wokół widzów); widz kolizji ciała nie może jechać za kamerą (serwer i predykcja straciłyby grunt pod stopami).
- **Float w ramie planety.** Godot jest w `double`, Core w `float`; konwersja tylko przez `PlanetFrames`, z kontrolą skończoności. Lot dev obcięty do 2,25 R, bo powyżej ~10 km precyzja `float` w Voxel Tools i w sieci przestaje wystarczać.
- **Nie zmieniać `GameRules.EyeHeight` ani kapsuły bez K2-13**: wzór oka serwera (`IntentValidator.TryGetEye`) i `CameraRigMath` muszą dawać identyczny wynik, a walidator stawiania bloków używa kapsuły.
