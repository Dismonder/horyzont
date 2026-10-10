# K2-13 · Narzędzia: manipulator terenu i budowanie

| | |
| --- | --- |
| Prowadzi | Sonnet 5.5 (Claude Code, `claude --model sonnet`; `--model opusplan`, gdy krok 0 wykaże rozjazd kontraktów) |
| Testy akceptacyjne | Sonnet 5.5; testy zmian w Core (W1, W2) przegląda Opus 5.5 przed kodem |
| Przegląd | Opus 5.5 w PR (zmiany w `src/Horyzont.Core/`, katalog `Tools/` w projekcie `Horyzont.Client` z K2-10, wpięcie budowli w `GameComposition`), reszta na bramce K2 |
| Zależy od | K2-09, K2-10, K2-11, K2-12 (K2-10 i K2-12 wprost: krok 0 wpina budowle K2-12 w composition root K2-10, decyzja bramki D5); warunki wejścia niżej |
| Gałąź | `pakiet/k2-13-narzedzia` |

**Cel:** gracz w grze solo i u hosta kopie i zasypuje teren kulą o promieniu 0,5–3 m (wybór materiału przy zasypywaniu) oraz stawia, obraca i usuwa bloki z palety z podglądem, a każde działanie idzie jako intencja przez sesję do `ServerSimulation`, która egzekwuje limity ze specyfikacji: zasięg 8 m, do 5 edycji terenu na sekundę, nie poniżej rdzenia, blok bez kolizji z graczem.

## Kontekst

- `docs/ZASADY.md`, punkty 2, 3, 4, 6, 7, 8
- `docs/spec/01-planeta.md`: Model świata (tabela „Narzędzie (bez zasobów)”, „Budowle”, „Grawitacja i postać”), Stan, operacje i dziennik (kroki 1–6, tabela operacji), Sieć („Edycje terenu”, „Zainteresowanie i limity”), Obsługa błędów („Odrzucona intencja”), Testy
- `docs/plans/K1-03-core-stan-i-operacje.md`: Intencje, „Walidacja i rozwiązanie”, „Reguły gry”, tabela zakresów kodów, Odstępstwa (`IntersectsPlayer` dla wszystkich graczy online, `Construct.LocalToParent`)
- `docs/plans/K1-07-sesja-i-protokol.md`: `ClientSession.SubmitIntent`, `IntentReply` z `ClientSeq`, `IServerGameHooks.OnIntent`, `IClientGameHooks.OnMessage`/`OnDisconnected`
- Przegląd bramki K1: `C:/Users/Damia/AgentTools/logs/results/horyzont-k1-review.md` (B2, E1–E9, lekcje 1–6)
- Plany i kod scalonych K2-09, K2-10, K2-11, K2-12 (krok 0 tego planu mapuje ich rzeczywiste nazwy)

Kod, na którym pakiet buduje (stan `main` 884d40d):

| Co | Gdzie |
| --- | --- |
| Intencje `TerrainToolIntent`, `PlaceBlockIntent` (`ConstructFaceTarget`, `TerrainTarget`), `RemoveBlockIntent`, `RejectReason` 0–20 | `src/Horyzont.Core/Intents/` |
| Walidacja: kolejność sprawdzeń, `OrientOnSurface` (dziś `internal static`), `CellHitsPlayer`, `SphereHitsPlayer` | `src/Horyzont.Core/Simulation/IntentValidator.cs` |
| `GameRules` (zasięg 8 m, promień 0,5–3 m, 5 edycji terenu/s z zapasem 5, 10 bloków/s z zapasem 10, tolerancja początku promienia 2 m, kapsuła 0,4 × 1,8 m, oczy 1,62 m) | `src/Horyzont.Core/Simulation/GameRules.cs` |
| `ServerSimulation.Submit(PlayerId, Intent) → IntentResult`, `UpdatePlayerKinematics` | `src/Horyzont.Core/Simulation/ServerSimulation.cs` |
| `ConstructRaycast.Raycast(ConstructStore, FrameRef, origin, direction, maxDistance, out ConstructHit)`, `ConstructHit(Construct, Cell, Face, Position, Distance)` | `src/Horyzont.Core/World/Constructs/` |
| `ITerrainService` (`Raycast`, `IsAreaLoaded`, `ApplyEdit`), `TerrainHit`, `TerrainMath.UpAt`/`AffectedBlocks` | `src/Horyzont.Core/World/Terrain/` |
| `BlockTypeRegistry` (`MissingKey = "core:missing"`, `Entries`), `MaterialRegistry`, `BlockState.OrientationCount = 24` | `src/Horyzont.Core/World/Blocks/`, `World/Constructs/BlockState.cs` |
| `ServerHello.BlockTypes`/`Materials` (`RegistryEntry(Id, Key)`), `IntentSubmit`, `IntentReply(ClientSeq, Reason, AppliedOpId)` | `src/Horyzont.Core/Protocol/Messages/` |
| Haki serwera nad symulacją w teście bramki | `tests/Horyzont.Integration.Tests/Support/SimulationServerHooks.cs` (gałąź bramki K1) |
| `FakeTerrainService`, `WorldFactory`, `InMemoryJournal`, `WorldScript` | `tests/Horyzont.Testing/` |

## Warunki wejścia

Pakiet nie rusza, dopóki każdy punkt nie jest spełniony na `main` z zielonym CI (lekcja 4: zielone na `main`, nie tylko na gałęzi):

1. Bramka K1 (PR #9: B1–B3) scalona: B1 (kolekcja xUnit bez równoległości dla testów z pulą SQLite, zakaz `ClearAllPools`), B2 (`TerrainRules.EditMarginVoxels` i `TerrainRules.MaxEditRadiusMeters` w `src/Horyzont.Core/Simulation/TerrainRules.cs` oraz limit `|Center|` w `OperationApplier.CheckTerrainEdit`), B3 (skrót pozy graczy offline), B4 (`game/` z K1-02 etap B).
2. K2-10 scalony z E1–E9 zamkniętymi albo z wpisem „odroczone do X” w jego planie (lekcja 5).
3. K2-09, K2-11, K2-12 scalone, ich checkboxy odhaczone albo odroczone z nazwą pakietu.
4. Zielony przebieg `python tools/engine/build.py smoke` i joba `game` w CI na `main` (lekcja 5).

## Zakres

- **Krok 0: wpięcie budowli (decyzja bramki D5).** K2-12 dostarcza bibliotekę i własną scenę testową; ten pakiet wpina `BlockContentLoader`, `BlockTypeCatalog` (`BlockTypeKeys` dla `CreateNew`/`WorldLoadOptions`), `BlockRenderTable`, `ConstructWorld` (`Initialize`, `OnApplied` z miejsca rozsyłającego `AppliedOperation`, `SetViewers` z pozycji K2-11, `ShutdownAsync`) w `GameComposition` K2-10 i w widok klienta hosta (`WorldStateConstructSource` nad `ServerRuntime.State`). `ConstructWorld.ShutdownAsync` trafia do kanonicznej kolejności zamykania K2-10 jako krok 7a (po terenie, przed transportem; tabela w K2-10 i ZASADY pkt 7 aktualizowane w tym PR).
- Logika narzędzi klienta w katalogu `Tools/` projektu bez Godota `src/Horyzont.Client` z K2-10 i testy w `tests/Horyzont.Client.Tests` (oba zakłada K2-10).
- Zmiany w Core (W1, W2, W3), wszystkie poza ścieżkami formatu i poza `OperationApplier`: wydzielenie matematyki stawiania do publicznych typów, reguła nakładania budowli w walidatorze (dokładny test OBB–OBB) oraz twarde limity tempa i zasięgu edycji terenu w walidatorze (decyzja bramki D13).
- Węzły Godota narzędzi w `game/` pod `/Root/Client`: wejście, celowanie FPP i TPP, podgląd (kula, duch bloku), mały HUD narzędzia, wysyłka intencji, obsługa odpowiedzi.
- Testy integracyjne pełnej ścieżki klient → `InMemoryTransport` → `ServerSession` → `ServerSimulation` w `tests/Horyzont.Integration.Tests/Tools/`.
- Testy Godota bez grafiki dla prawdziwego terenu Voxel Tools i siatki budowli.

Poza zakresem:

| Co | Gdzie |
| --- | --- |
| Lokalna predykcja edycji terenu u zdalnego klienta i nadpisywanie jej danymi bloków | K3-16 (implementuje `IEditPrediction` z tego pakietu) |
| Przewidywane bloki u zdalnego klienta, widok budowli u zdalnego klienta | K3-15 |
| Kamera deweloperska (swobodny lot od orbity) | K2-11 |
| Implementacja `ITerrainService` na Voxel Tools (z `Raycast` bez alokacji, D16); mapa `MaterialId` → indeks materiału Voxel Tools: definicja `TerrainMaterialMap` w K2-08, zastosowanie przy edycji w K2-09 (mapa w konstruktorze `VoxelTerrainService`, D3) | K2-08 / K2-09 | `Horyzont.Game.Terrain.VoxelTerrainService : ITerrainService` (`game/src/Terrain/`; `Raycast` bez alokacji, `IsAreaLoaded`, `LoadAreaBlocking`, `ApplyEdit` przez `do_sphere`); jedna instancja serwera i klienta hosta: `VoxelServerTerrain.Voxel`/`.Service` z `VoxelServerTerrainFactory.Terrain` |
| Meshing, kolizje, tabela 24 orientacji, definicje typów bloków (bibliotecznie; wpięcie w grę robi krok 0 tego pakietu) | K2-12 |
| Docelowy HUD i menu | K3-18 |
| Boty kopiące i budujące | K4-19 (używa `ToolController` z tego pakietu) |
| Koszt bloków, zasoby, wytrzymałość | K6-36 |

## Projekt

### Zasada nadrzędna: zero nowej semantyki operacji

K2-13 nie dodaje operacji, pól stanu, komunikatów, intencji ani kodów odmów i nie zmienia `OperationApplier`, `Operations/`, `Intents/`, `Hashing/`, `Checkpoints/`, `Protocol/`, `Serialization/`. Wszystko, czego narzędzia potrzebują, istnieje od K1-03. Jeżeli wykonawca uzna, że potrzebuje któregokolwiek z nich, zatrzymuje się i eskaluje do Opus 5.5 (ZASADY 12). Taka zmiana wymagałaby w tym samym PR: kodu z zakresu K2–K4 (`OperationKind` 8–19, `RejectReason` 21–29, `IntentCode` 4–9, następny wolny numer w chwili scalenia, test „złoty”), podbicia wersji formatu (lekcja 1), hashowania w `StateHasher`, testu czułości i udziału w teście live = replay = checkpoint + ogon (lekcja 2).

Limity narzędzi (zasięg, promień, tempo, kolizja z graczem) to walidacja intencji, nie stosowanie operacji: można je stroić w `GameRules` bez zmiany formatu. Stałe, od których zależy `Apply`/`Replay` (margines bloków, górny limit promienia w applierze z B2), zostają `const` w ścieżce chronionej; ten pakiet tylko sprawdza testem, że `GameRules.MaxToolRadius` nie przekracza limitu appliera.

### Pliki

```
src/Horyzont.Core/World/Constructs/ConstructPlacement.cs     W1: publiczne OnTerrain (dawne OrientOnSurface + początek)
src/Horyzont.Core/Simulation/PlayerCapsule.cs                W1: publiczne testy kapsuła–kula i kapsuła–komórka
src/Horyzont.Core/Simulation/IntentValidator.cs              W1 (użycie nowych typów), W2 (nakładanie budowli), W3 (zasięg od oka serwera)
src/Horyzont.Core/Simulation/BlockOverlap.cs                 W2: dokładny test przecięcia OBB–OBB (SAT) komórek
src/Horyzont.Core/Simulation/SlidingWindowLimiter.cs         W3: okno przesuwne 1 s dla edycji terenu (bez alokacji)
src/Horyzont.Core/Simulation/PlayerBuckets.cs                W3: kubełek terenu + okno przesuwne

src/Horyzont.Client/Horyzont.Client.csproj                   z K2-10 (referencje: Core, Net; bez Godota); tu tylko referencja do Horyzont.Constructs, jeśli potrzebna
src/Horyzont.Client/Tools/
  ToolKind.cs, ToolSettings.cs, ToolInputFrame.cs
  AimRay.cs, AimRayBuilder.cs
  IToolWorldView.cs
  ToolTargetKind.cs, ToolTarget.cs, ToolTargeting.cs
  ToolPreviewKind.cs, ToolPreview.cs
  BlockPalette.cs, MaterialPalette.cs, OrientationPicker.cs
  IntentPacer.cs
  IIntentSender.cs, SessionIntentSender.cs
  IEditPrediction.cs, NoEditPrediction.cs
  PendingIntent.cs, PendingIntentTracker.cs, PendingIntentOutcome.cs
  ToolController.cs
  ToolsLog.cs                                                [LoggerMessage], wewnętrzny

tests/Horyzont.Client.Tests/                                 xUnit v3 mtp-off, FsCheck.Xunit.v3, <Using Include="Xunit" />
tests/Horyzont.Integration.Tests/Tools/                      pełna ścieżka przez sesję
tests/Horyzont.Core.Tests/World/Constructs/ConstructPlacementTests.cs
tests/Horyzont.Core.Tests/Simulation/IntentValidationTests.cs (dopisane przypadki W2)
tests/Horyzont.Architecture.Tests/LayeringTests.cs           Horyzont.Client na liście projektów bez Godota

game/src/Client/Tools/
  ToolNode.cs                 Node3D: wejście → ToolController, rysuje podgląd, przekazuje IntentReply
  ToolInputReader.cs          InputMap → ToolInputFrame (zbierane w _Process, zużywane w _PhysicsProcess)
  TerrainToolPreview.cs       półprzezroczysta kula
  BlockGhostPreview.cs        duch bloku (siatka typu z K2-12 albo sześcian) w orientacji
  ToolHud.cs                  etykieta: narzędzie, promień, materiał, typ, orientacja, ostatnia odmowa
  GodotToolWorldView.cs       IToolWorldView nad terenem z K2-09 (Raycast bez alokacji) i budowlami z K2-12 wpiętymi w kroku 0
game/src/Boot/GameComposition.cs   krok 0: budowle K2-12 (ładowanie treści, ConstructWorld, OnApplied, widzowie, zamknięcie)
game/tests/Tools/             scenariusze silnika (tabela „Godot bez grafiki”)
game/scenes/client/tools/Tools.tscn
game/project.godot            akcje InputMap (tabela Sterowanie)
game/locale/tools.csv         klucze REJECT_* i TOOL_*, kolumny pl, en
```

Katalogi `game/src/Client/` i `game/scenes/client/` przyjmij takie, jakie założył K2-10; jeśli są inne, dopasuj i wpisz w Odstępstwach.

### W1. Wspólna matematyka stawiania (Core, bez zmiany semantyki)

Duch bloku na terenie musi wyglądać dokładnie jak budowla, którą utworzy serwer, a podpowiedź „koliduje z graczem” musi liczyć to samo co walidator. Dlatego jedna implementacja w Core, używana przez walidator i klienta:

```csharp
namespace Horyzont.Core.World.Constructs;

/// <summary>Pose of a new construct placed on the terrain (docs/plans/K1-03, "PlaceBlockIntent na terenie").</summary>
public static class ConstructPlacement
{
    /// <summary>Local +Y along TerrainMath.UpAt(hit), local −Z along lookForward projected on the tangent plane
    /// (fallbacks: rayDirection, planet X, planet Z); origin = hit − rotation · (0.5, 0, 0.5).</summary>
    public static void OnTerrain(Vector3 hit, Vector3 lookForward, Vector3 rayDirection,
                                 out Vector3 origin, out Quaternion rotation);
}
```

```csharp
namespace Horyzont.Core.Simulation;

/// <summary>Player capsule tests shared by intent validation and client previews.</summary>
public static class PlayerCapsule
{
    public static void Axis(Vector3 feet, GameRules rules, out Vector3 bottom, out Vector3 top);
    public static bool IntersectsSphere(Vector3 feet, Vector3 center, float radius, GameRules rules);
    /// <summary>Cell box [cell, cell + 1) in a construct pose (origin, rotation in the planet frame).</summary>
    public static bool IntersectsCell(Vector3 feet, Vector3 origin, Quaternion rotation, CellPos cell, GameRules rules);
}
```

- `IntentValidator.OrientOnSurface`, `SphereHitsPlayer` i `CellHitsPlayer` wołają nowe typy; dla budowli zagnieżdżonych walidator dalej składa pozę łańcuchem rodziców (`PlanetToConstruct`) i przekazuje wynik do `IntersectsCell`.
- Test „złoty” powstaje **przed** refaktoryzacją: 64 ustalone przypadki (trafienie, `LookForward`, `RayDirection`, w tym zdegenerowane) z bitowym zapisem `origin` i `rotation` liczonym obecnym kodem; po zmianie te same bity. Bez tego refaktoryzacja nie jest dowiedzioną „zmianą bez semantyki”.
- `IntentValidator` nie jest na liście ścieżek formatu (ZASADY 3), ale jest w Core: przegląd Opus 5.5.

### W2. Blok nie nakłada się na inną budowlę (walidator, Core)

Dziś `ValidatePlaceOnTerrain` tworzy nową budowlę, nawet gdy jej komórka (0,0,0) wchodzi w zajętą komórkę innej budowli stojącej obok (obie pod innym obrotem). Specyfikacja mówi „blok przy istniejącej budowli ją rozbudowuje” i „bez łączenia”, więc nakładanie się dwóch budowli nie jest przewidziane. **Pytanie do właściciela P2** decyduje o wariancie; domyślnie (brak odpowiedzi przed krokiem 4) wariant A:

- **Wariant A (domyślny):** nowe sprawdzenie w walidatorze, kod istniejący `CellOccupied` (14), bez nowego kodu i bez zmiany formatu. Dotyczy obu celów `PlaceBlockIntent`. Kolejność: po `AreaNotLoaded`, przed `IntersectsPlayer`. Reguła (decyzja bramki D15: dokładny test, nie próbkowanie): dla każdej budowli na tej samej planecie (ta sama iteracja co `ConstructRaycast.Raycast`, z budowlami zagnieżdżonymi; z pominięciem budowli docelowej przy `ConstructFaceTarget`), której prostopadłościan `BoundsMin`/`BoundsMax` w jej układzie przecina kulę otaczającą nową komórkę (środek, promień √3/2 m), nową komórkę jako OBB (środek, obrót, półwymiary 0,5 m) sprawdza się testem osi rozdzielających (SAT, 15 osi: 3 + 3 normalne ścian i 9 iloczynów krawędzi, iloczyny bliskie zeru pominięte) z każdą zajętą komórką tej budowli, której komórka leży w AABB rzutu nowej komórki na układ budowli (≤ 27 komórek). Przecięcie z głębokością > `BlockOverlap.TouchToleranceMeters` (2e-3 m) → `CellOccupied`; styk ścianą, krawędzią albo wierzchołkiem (głębokość ≤ tolerancja) jest dozwolony, bo bloki stykające się ścianami muszą dać się postawić. `BlockOverlap.Intersects(in Obb a, in Obb b)` to czysta funkcja bez alokacji, wspólna dla walidatora i podpowiedzi klienta.
- **Wariant B:** przyciąganie do sąsiedniej budowli (rozbudowa zamiast nowej). Wymaga nowej reguły rozwiązania intencji i nowego celu albo pola w intencji, czyli zmiany formatu intencji: poza zakresem tego pakietu, osobny pakiet po decyzji.
- Test alokacji z K1-03 (`Simulation/AllocationTests`) obejmuje też ścieżkę z W2: brak alokacji poza rekordem operacji.

### W3. Twarde limity tempa i zasięgu edycji terenu (walidator, Core; decyzja bramki D13)

- **Tempo:** „do 5 operacji na sekundę” znaczy ≤ 5 przyjętych edycji terenu w **każdym** oknie 1 s (60 ticków serwera). Kubełek terenu z `PlayerBuckets` zostaje (pojemność `TerrainEditBurst` = 5, odnowienie `TerrainEditsPerSecond` = 5/s), a dodatkowo `SlidingWindowLimiter` (pierścień ticków ostatnich `TerrainEditsPerSecond` przyjętych edycji, bez alokacji) przepuszcza edycję tylko, gdy w przedziale `(tick − TickRate, tick]` jest mniej niż `TerrainEditsPerSecond` przyjętych. Przekroczenie → `RateLimited` (kod istniejący). Odmowa nie zużywa ani kubełka, ani okna. Bloki (`BlockEditsPerSecond`) bez zmian.
- **Zasięg:** walidator dalej rzuca promień terenu od zgłoszonego `RayOrigin` (tolerancja `MaxRayOriginErrorMeters` = 2 m zostaje dla opóźnienia), ale trafienie przyjmuje tylko, gdy `Vector3.Distance(oko serwera, hit.Position) ≤ ReachMeters`, gdzie oko serwera = `PlayerState.EyePosition(rules)` (stan autorytatywny, nie `RayOrigin`); inaczej `OutOfReach`. Dotyczy `TerrainToolIntent` i `PlaceBlockIntent` z `TerrainTarget`. Bloki na ścianach budowli już liczą zasięg od oka serwera (`ReachMeters + 0,87 m` do środka komórki).
- To walidacja intencji, nie stosowanie operacji: dziennik i powtórka bez zmian, format bez zmian; przegląd Opus 5.5 (Core).

### Kontrakty wystawiane

| Typ | Plik | Dla kogo |
| --- | --- | --- |
| `ConstructPlacement.OnTerrain` | `src/Horyzont.Core/World/Constructs/ConstructPlacement.cs` | podgląd klienta, K3-15 (przewidywane budowle) |
| `PlayerCapsule` | `src/Horyzont.Core/Simulation/PlayerCapsule.cs` | podpowiedzi klienta, K3-15 |
| `BlockOverlap.Intersects` (OBB–OBB, SAT) | `src/Horyzont.Core/Simulation/BlockOverlap.cs` | podpowiedzi klienta, K3-15 |
| `IToolWorldView` | `src/Horyzont.Client/Tools/IToolWorldView.cs` | K3-15/K3-16 (widok zdalnego klienta), K4-19 (boty) |
| `ToolController`, `ToolInputFrame`, `ToolSettings`, `AimRay`, `AimRayBuilder` | `src/Horyzont.Client/Tools/` | K4-19 (boty bez grafiki używają tej samej ścieżki co gracz) |
| `IIntentSender`, `SessionIntentSender` | `src/Horyzont.Client/Tools/` | K4-19 |
| `IEditPrediction`, `NoEditPrediction` | `src/Horyzont.Client/Tools/` | K3-16 (predykcja terenu), K3-15 (predykcja bloków) |
| `PendingIntentTracker`, `PendingIntentOutcome` | `src/Horyzont.Client/Tools/` | K3-15/K3-16 (koniec predykcji), K3-18 (HUD) |

```csharp
namespace Horyzont.Client.Tools;

public enum ToolKind : byte { TerrainManipulator = 1, Builder = 2 }

/// <summary>Current tool and its parameters. Radius in [rules.MinToolRadius, rules.MaxToolRadius], step RadiusStep.</summary>
public readonly record struct ToolSettings(ToolKind Tool, float Radius, byte FillMaterial, ushort BlockType, byte Orientation)
{
    public const float RadiusStep = 0.25f;
}

/// <summary>Input of one physics tick. Steps are signed counts (wheel notches, key presses) since the last tick.</summary>
public readonly record struct ToolInputFrame(
    bool PrimaryPressed, bool PrimaryHeld, bool SecondaryPressed, bool SecondaryHeld,
    ToolKind? SelectTool, int RadiusSteps, int PaletteSteps, int MaterialSteps, int YawSteps, int UpSteps);

/// <summary>Aim in the planet frame of the local player. Origin is always the eye, in FPP and in TPP.</summary>
public readonly record struct AimRay(PlanetId Planet, Vector3 Eye, Vector3 Direction, Vector3 LookForward);

public static class AimRayBuilder
{
    /// <summary>Eye = feet + TerrainMath.UpAt(feet) · rules.EyeHeight, the server formula (IntentValidator.TryGetEye).</summary>
    public static Vector3 EyeFromFeet(Vector3 feet, GameRules rules);

    /// <summary>
    /// FPP: the camera ray is the eye ray. TPP: the aim point is the first hit of the camera ray (terrain or construct)
    /// within |camera − eye| + rules.ReachMeters, otherwise the point at that distance; the ray goes from the eye to it.
    /// Returns false for non-finite input or a degenerate direction (nothing is sent then).
    /// </summary>
    public static bool TryBuild(PlanetId planet, Vector3 feet, Vector3 cameraOrigin, Vector3 cameraDirection,
                                Vector3 bodyForward, IToolWorldView world, GameRules rules, out AimRay ray);
}

public interface IToolWorldView
{
    bool TryGetPlanet(PlanetId planet, [NotNullWhen(true)] out PlanetParams? parameters);
    bool RaycastTerrain(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out TerrainHit hit);
    bool RaycastConstructs(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out ConstructHit hit);
    bool TryGetConstruct(EntityId construct, [NotNullWhen(true)] out Construct? value);   // tylko do odczytu
    /// <summary>Feet of other online players on the planet, for preview hints only (may be empty).</summary>
    int CopyOtherPlayerFeet(PlanetId planet, Span<Vector3> destination);
}

public enum ToolTargetKind : byte { None = 0, Terrain = 1, ConstructCell = 2 }
public readonly record struct ToolTarget(ToolTargetKind Kind, Vector3 Point, Vector3 Normal, float Distance,
                                         EntityId Construct, CellPos Cell, Face Face);

public enum ToolPreviewKind : byte { None = 0, Sphere = 1, NewConstructCell = 2, ConstructCell = 3 }
/// <summary>What to draw. Hint is the locally predicted RejectReason (None = green); it never replaces server validation.</summary>
public readonly record struct ToolPreview(ToolPreviewKind Kind, Vector3 Center, float Radius, Vector3 Origin,
                                          Quaternion Rotation, EntityId Construct, CellPos Cell, byte Orientation,
                                          RejectReason Hint);

public interface IIntentSender { bool CanSend { get; } uint Submit(Intent intent); }   // SessionIntentSender: ClientSession w InGame

public interface IEditPrediction
{
    void Begin(uint clientSeq, Intent intent, in ToolPreview preview);   // NoEditPrediction: nic
    void Commit(uint clientSeq);
    void Revert(uint clientSeq);                                         // wołane od najnowszej do najstarszej
}

public enum PendingIntentOutcome : byte { Accepted = 1, Rejected = 2, TimedOut = 3, Disconnected = 4 }

public sealed class PendingIntentTracker
{
    public const int MaxPending = 16;
    public const int DeadlineTicks = 300;                       // 5 s przy 60 Hz, połowa limitu bezczynności sesji
    public PendingIntentTracker(IEditPrediction prediction, ILogger<PendingIntentTracker> logger);
    public bool IsFull { get; }
    public int Count { get; }
    public void Add(uint clientSeq, Intent intent, in ToolPreview preview, long clientTick);
    /// <summary>Unknown or repeated ClientSeq: logged and ignored, never throws.</summary>
    public bool OnReply(IntentReply reply, out PendingIntentOutcome outcome);
    public void Tick(long clientTick);                          // wygasza wpisy starsze niż DeadlineTicks
    public void Clear();                                        // rozłączenie: Revert od najnowszego
    public RejectReason LastRejection { get; }
}

public sealed class ToolController
{
    public ToolController(GameRules rules, IToolWorldView world, IIntentSender sender, PendingIntentTracker pending,
                          BlockPalette blocks, MaterialPalette materials, ILogger<ToolController> logger);
    public ToolSettings Settings { get; }
    public ToolPreview Preview { get; }
    /// <summary>One physics tick: applies setting steps, targets, builds the preview, sends at most one intent.</summary>
    public void Update(in ToolInputFrame input, in AimRay aim, long clientTick);
}
```

Nazwy parametrów i drobne przeciążenia wykonawca może zmienić, jeśli opisze to w Odstępstwach; znaczenie, granice (`MaxPending`, `DeadlineTicks`) i reguły z sekcji niżej są kontraktem.

### Kontrakty konsumowane

Plany K2-09, K2-11 i K2-12 powstają równolegle z tym planem, więc nazwy poniżej są opisem potrzeby, nie gwarancją nazw. Krok 0 wypełnia kolumnę „Rzeczywisty typ” po przeczytaniu scalonego kodu; brak którejś zdolności zatrzymuje pakiet i wraca do pakietu, który ją miał dać.

| Potrzeba | Od | Rzeczywisty typ (krok 0) |
| --- | --- | --- |
| Implementacja `ITerrainService` na Voxel Tools (`Raycast` bez alokacji zarządzanej — celowanie co tick, D16; `IsAreaLoaded`, `ApplyEdit` przez `do_sphere`), wspólna dla serwera i klienta hosta | K2-09 | `Horyzont.Game.Terrain.VoxelTerrainService : ITerrainService` (`game/src/Terrain/`; `Raycast` bez alokacji, `IsAreaLoaded`, `LoadAreaBlocking`, `ApplyEdit` przez `do_sphere`); jedna instancja serwera i klienta hosta: `VoxelServerTerrain.Voxel`/`.Service` z `VoxelServerTerrainFactory.Terrain` |
| Odwzorowanie `MaterialId` z `MaterialRegistry` na indeks materiału Voxel Tools (`TerrainMaterialMap`: definicja K2-08, zastosowanie K2-09) i odczyt materiału voxela (do testu Godota) | K2-08 / K2-09 | `Horyzont.Game.Planet.TerrainMaterialMap` (`Build(MaterialRegistry)`, `ToTextureIndex`), `TerrainMaterials` (klucze i indeksy tekstur, `CoreMaterialKey`); odczyt voxela w teście: `PlanetTerrain.Tool` (`GetVoxelF`, kanały `TerrainChannels.Sdf`/`Material`) |
| Pozycja stóp lokalnego gracza w ramie planety (przewidziana), kierunek ciała, kamera (początek, kierunek, FPP/TPP) | K2-11 | `Horyzont.Game.Client.Character.LocalPlayer : ILocalPlayerView` (`Planet`, `PredictedFeet`, `BodyRotation`, `CameraOrigin`, `CameraDirection`, `CameraMode`, `IsActive`, `Eye`); `PlanetFrames` (Godot ↔ rama planety); fizyka `LocalPlayer` o priorytecie 10, `ToolNode` 20 |
| Serwer woła `ServerSimulation.UpdatePlayerKinematics` dla każdego gracza co tick fizyki | K2-11 | `ServerCharacters.OnTick` (`IServerTickParticipant`, `game/src/Server/Characters/`), rejestracja przez `ServerCharactersBinder.Register` |
| Źródło budowli widocznych dla klienta hosta (`WorldStateConstructSource`) i odświeżenie siatki chunku po `BlockSet`/`BlockClear`/`ConstructCreate` (`ConstructWorld.OnApplied` + `ChunkMeshApplied`, p99 ≤ 100 ms) | K2-12 (biblioteka); wpięcie w `GameComposition` K2-10: krok 0 tego pakietu (D5) | `Horyzont.Constructs.Sources.WorldStateConstructSource` nad `ServerRuntime.State`, `Horyzont.Game.Constructs.ConstructWorld` (`Initialize`, `OnApplied`, `RebuildAll`, `SetViewers`, `ShutdownAsync`, zdarzenie `ChunkMeshApplied`); wpięcie: `HostConstructs` (`game/src/Constructs/`) + `ServerSimulation.Applied` + `ServerRuntime.AddAfterTerrainShutdown` |
| Tabela 24 orientacji: indeks ↔ baza całkowita (oś „góra” i „przód” jako `Face`) | K2-12 | `Horyzont.Core.World.Constructs.BlockOrientation` (`WithYaw`, `Identity`, tabela 24 orientacji w Core), `OrientationPicker` w `Horyzont.Client.Tools` |
| Definicje typów bloków do palety i ducha (klucz, kształt, siatka) | K2-12 | `Horyzont.Constructs.Content.BlockTypeCatalog` (`BlockTypeKeys`), `Horyzont.Game.Constructs.BlockContentLoader.Load()`, `BlockRenderTable.Build`; paleta z `ServerHello.BlockTypes` (`BlockPalette.FromHello`); duch to sześcian 1,02 m (nie siatka typu) |
| Tryb procesu (solo, host, klient, serwer), rejestracja węzła w composition root `game/src/Boot/` (`GameComposition`), przekazanie `IntentReply` i `OnDisconnected` do narzędzi (`IClientSessionListener`), `IServerGameHooks.OnIntent` → `ServerSimulation.Submit`, kanoniczna kolejność zamykania | K2-10 | `GameComposition` (`game/src/Boot/`): `Create`/`StartAsync`; `ClientRuntime.AddListener(IClientSessionListener)` (`OnServerHello`, `OnIntentReply`, `OnDisconnected`); `GameServerHooks.OnIntent` → `ServerSimulation.Submit`; kolejność zamykania `ServerRuntime.BuildShutdown` |
| Wątek symulacji serwera w trybie host (ten sam co klient czy osobny) | K2-10 | | ten sam wątek główny: `ServerNode._PhysicsProcess` (priorytet −100) przed `ClientNode` (0), więc `GodotToolWorldView` czyta `ServerRuntime.State` bezpośrednio |

Jeśli K2-10 uruchamia symulację hosta na osobnym wątku, `GodotToolWorldView` nie może czytać `WorldState` serwera bezpośrednio: wtedy budowle bierze wyłącznie z migawki K2-12 (wpis w Odstępstwach, przegląd Opus).

### Decyzje

1. **Promień zawsze od oka.** W FPP i w TPP `RayOrigin` intencji to oko liczone wzorem serwera ze stóp gracza, a kierunek wskazuje punkt pod celownikiem kamery (`AimRayBuilder`). Promień z kamery TPP stoi kilka metrów za graczem i dostałby `RayOriginMismatch` (tolerancja 2 m). Podgląd liczy ten sam promień, więc to, co widać, jest tym, co zrobi serwer.
2. **Zasięg (decyzja bramki D13).** Walidator rzuca promień terenu na `ReachMeters` od `RayOrigin` (może odbiegać od oka serwera o `MaxRayOriginErrorMeters`), ale trafienie dalej niż `ReachMeters` od **autorytatywnego** oka serwera dostaje `OutOfReach` (W3); bloki mają zasięg `ReachMeters + 0,87 m` od oka serwera do środka komórki. Klient celuje na 8 m od swojego oka.
3. **Kolejność trafień jak u serwera.** Budowla wygrywa z terenem tylko wtedy, gdy jest ściśle bliżej (`blocker.Distance < hit.Distance`), tak jak `NoLineOfSight` w walidatorze.
4. **Akcje.** Manipulator: główna = kopanie (`Remove`, `MaterialId = 0`), dodatkowa = zasypywanie (`Add`, wybrany materiał). Budowanie: główna = usunięcie wskazanego bloku (`RemoveBlockIntent`), dodatkowa = postawienie (`PlaceBlockIntent` z `ConstructFaceTarget` przy trafieniu budowli albo `TerrainTarget` przy trafieniu terenu). Postawienie w powietrzu bez trafienia nie istnieje (K1-03); Pytanie do właściciela P3.
5. **Tempo po stronie klienta.** `IntentPacer` to lustro limitów serwera na tickach klienta: teren = kubełek `TerrainEditBurst`/`TerrainEditsPerSecond` **i** okno przesuwne 1 s z W3 (≤ 5 w każdym oknie), bloki `BlockEditBurst`/`BlockEditsPerSecond` z tych samych `GameRules`. Przytrzymanie przycisku powtarza co 13 ticków dla terenu (≈ 4,6/s, 5 edycji zajmuje 53 ticki, więc zapas na jitter ≥ 7 ticków) i co 15 ticków dla bloków (4/s). Klient nie wysyła intencji, której jego lustro by nie przepuściło; serwer i tak egzekwuje limit, klient tylko unika zbędnych odmów.
6. **Oczekujące intencje mają termin i limit** (lekcja 6). `PendingIntentTracker`: najwyżej 16 wpisów, termin 300 ticków; po terminie wpis jest cofany (`Revert`) i logowany jako ostrzeżenie; przy pełnym trackerze akcja jest odrzucana lokalnie (HUD „czekam na serwer”), bez wysyłki. Rozłączenie czyści wszystko od najnowszego. Odpowiedź z nieznanym albo powtórzonym `ClientSeq` (błąd albo złośliwy serwer) jest logowana i pomijana. Wpisy trzyma lista uporządkowana po `ClientSeq`, nie słownik (kolejność cofania).
7. **Brak predykcji na wspólnym terenie.** W trybie solo i host klient czyta autorytatywny teren serwera bez drugiej kopii (specyfikacja, Architektura), więc lokalne zastosowanie edycji byłoby podwójnym `do_sphere`. Tryby solo i host używają `NoEditPrediction`; „podgląd własnych edycji” to tu kula i duch, a skutek pojawia się po zastosowaniu przez serwer w tym samym procesie. Zdalny klient w K2 też dostaje `NoEditPrediction`: intencje działają i są walidowane, widoczny skutek dają K3-15 i K3-16, które dostarczą własne `IEditPrediction`. Pytanie do właściciela P5.
8. **Palety.** `BlockPalette` z `ServerHello.BlockTypes` w kolejności `Id`, bez `core:missing` (blok zastępczy nie jest do budowania; serwer go nie odrzuca, bo to tylko kosmetyka, a zmiana walidatora nie jest potrzebna). `MaterialPalette` z `ServerHello.Materials` w kolejności `Id`, bez materiałów z listy wykluczeń (domyślnie klucz materiału rdzenia z K2-08); Pytanie do właściciela P4.
9. **Orientacja bloku.** `OrientationPicker` obraca o 90° wokół lokalnej osi „góra” (`YawSteps`) i przełącza ścianę „góra” po sześciu ścianach (`UpSteps`), przez tabelę orientacji z K2-12; wszystkie 24 indeksy są osiągalne. Obrót samej budowli wynika z kierunku patrzenia przy pierwszym bloku (Core, `ConstructPlacement`).
10. **Kierunki w intencjach.** Wektory z Godota (`real_t = double`) konwertuje się do `System.Numerics.Vector3` i dopiero wtedy normalizuje; długość poza 1 ± 0,01 albo wartość nieskończona oznacza „nie wysyłaj”. Planeta domowa ma środek w początku świata Godota, więc w podprojekcie 1 rama planety to współrzędne świata; konwersja idzie przez jedną funkcję (z K2-11, jeśli jest), żeby od K5 dało się podmienić ramy.
11. **Logowanie.** Odmowy na serwerze logują się na poziomie `Debug` z `PlayerId` i powodem (`[LoggerMessage]`); po stronie klienta odmowa trafia do HUD (`tr("REJECT_<NAZWA>")`) i do logu `Information` z `ClientSeq`.

### Sterowanie (domyślne, akcje InputMap)

| Akcja | Klawisz | Działanie |
| --- | --- | --- |
| `tool_select_terrain` / `tool_select_build` | 1 / 2 | wybór narzędzia |
| `tool_primary` / `tool_secondary` | LPM / PPM | kop / zasyp; usuń blok / postaw blok |
| `tool_next` / `tool_prev` | kółko | promień ±0,25 m (manipulator) albo typ bloku (budowanie) |
| `tool_material_next` | M | następny materiał zasypywania |
| `tool_rotate_yaw` / `tool_rotate_up` | R / T | obrót bloku wokół „góry” / zmiana ściany „góra” |

Kółko daje tylko naciśnięcia; `ToolInputReader` zlicza je w `_Process` i oddaje sumę w `_PhysicsProcess`.

## Pytania do właściciela

| # | Pytanie | Domyślnie, jeśli brak odpowiedzi przed krokiem, który go dotyczy |
| --- | --- | --- |
| P1 | „Do 5 operacji na sekundę”. Rozstrzygnięte na bramce K1: twardo, ≤ 5 przyjętych edycji terenu w każdym oknie 1 s (kubełek + okno przesuwne, W3, D13) | — |
| P2 | Blok na terenie tuż przy budowli: odrzucić nakładanie (`CellOccupied`, wariant A) czy przyciągać i rozbudowywać sąsiednią budowlę (wariant B, zmiana formatu intencji, osobny pakiet)? | wariant A |
| P3 | „Blok w wolnym miejscu” w specyfikacji: czy stawianie w powietrzu bez trafienia (np. w punkcie zasięgu) jest wymagane? Wymaga nowego celu intencji (`IntentCode` 4–9, wersja formatu). | nie; tylko teren i ściany budowli |
| P4 | Które materiały są w palecie zasypywania (rdzeń, ruda)? | wszystkie z rejestru poza materiałem rdzenia; serwer przyjmuje każdy znany |
| P5 | Czy narzędzia u zdalnego klienta mają dawać widoczny skutek już w K2, czy dopiero z K3-15/K3-16? | dopiero K3; w K2 zdalny klient wysyła intencje bez predykcji |
| P6 | Projekt bez Godota `src/Horyzont.Client`: zakłada go K2-10 (tamto Pytanie 1); ten pakiet tylko dokłada `Tools/` | jak odpowiedź na K2-10 P1 |
| P7 | Zasięg 8 m. Rozstrzygnięte na bramce K1: odległość trafienia ≤ 8 m od autorytatywnego oka serwera (stan gracza), nie od zgłoszonego `RayOrigin` (W3, D13) | — |

## Testy akceptacyjne (napisz najpierw)

`Horyzont.Client.Tests` (bez sieci, `FakeToolWorldView` w projekcie testów nad `ConstructStore` z `WorldFactory` i `FakeTerrainService`):

| Test | Oczekiwanie |
| --- | --- |
| `AimRayBuilder_ThirdPerson_RayStartsAtEyeTowardCrosshairHit` | kamera 4 m za graczem: `Eye` równe wzorowi serwera, kierunek na trafienie promienia kamery; `Vector3.Distance(Eye, oko serwera) = 0` |
| `AimRayBuilder_NonFiniteOrDegenerate_ReturnsFalse` | NaN, nieskończoność, kierunek zerowy → `false` |
| `ToolTargeting_ConstructStrictlyCloser_TargetsConstructElseTerrain` | budowla bliżej → `ConstructCell` ze ścianą; remis albo teren bliżej → `Terrain` |
| `ToolTargeting_HitBeyondReach_NoTarget` | trafienie 8,01 m od oka → `None` |
| `ToolSettings_RadiusSteps_ClampedHalfToThreeInQuarterSteps` | od 0,5 do 3,0 krokami 0,25; poza zakresem przycięte |
| `BlockPalette_FromHello_ExcludesMissingOrderedById` | `core:missing` nieobecny, kolejność `Id` |
| `MaterialPalette_FromHello_AppliesExclusions` | materiały z listy wykluczeń nieobecne |
| `OrientationPicker_FourYawSteps_ReturnsToStart` i `_AllTwentyFourReachable` | właściwości (FsCheck) |
| `IntentPacer_HoldDigTenSeconds_AtMostFiveInEveryOneSecondWindow` | 600 ticków przytrzymania i serii kliknięć: w **każdym** oknie 60 ticków ≤ 5 wysyłek (sprawdzone dla każdego ticku początkowego), łącznie ≤ 50 |
| `ToolPreview_Hints_MatchValidatorReasons` | kula poniżej rdzenia → `BelowCore`; zasypywanie na sobie → `IntersectsPlayer`; duch w kapsule → `IntersectsPlayer`; komórka zajęta → `CellOccupied` |
| `PendingIntentTracker_Reply_CommitsOrRevertsAndReportsReason` | `Accepted` → `Commit`; odmowa → `Revert` i `LastRejection` |
| `PendingIntentTracker_NoReplyWithinDeadline_RevertsAndLogs` | po 300 tickach `TimedOut`, `Revert`, wpis w logu |
| `PendingIntentTracker_Full_RefusesLocally` | 17. akcja nie jest wysyłana, `IsFull` |
| `PendingIntentTracker_Clear_RevertsNewestFirst` | kolejność `Revert` malejąca po `ClientSeq` |
| `PendingIntentTracker_UnknownOrDuplicateSeq_IgnoredWithoutThrow` | log, brak wyjątku, stan bez zmian |
| `ToolController_Update_SteadyState_DoesNotAllocate` | 1000 ticków celowania i podglądu bez wysyłki na `FakeToolWorldView`: 0 B (`GC.GetAllocatedBytesForCurrentThread`); gorąca ścieżka w rozumieniu ZASADY 6 (wersję na prawdziwym adapterze ma tabela Godota, D16) |
| `ToolController_SenderNotInGame_SendsNothing` | `CanSend = false` → brak intencji i wpisów |

`Horyzont.Integration.Tests/Tools/` (klient `ToolController` → `SessionIntentSender` → `ClientSession` → `InMemoryTransport` → `ServerSession` → haki nad `ServerSimulation` z `FakeTerrainService`; `FakeTimeProvider`):

| Test | Oczekiwanie |
| --- | --- |
| `Dig_ValidRadius_AppliesTerrainEditAtRaycastHit` | `TerrainEditOp(Remove, MaterialId 0)` ze środkiem w trafieniu i promieniem z ustawień; odpowiedź `None` |
| `Dig_RadiusOutsideLimits_RejectedInvalidParameters` | surowe intencje (z pominięciem `ToolController`) 0,49 i 3,01 → `InvalidParameters`; 0,5 i 3,0 przyjęte |
| `Dig_TargetBeyondReach_RejectedNoHit` | teren 8,1 m od początku promienia → `NoHit` |
| `Dig_ShiftedRayOrigin_HitBeyondEyeReach_RejectedOutOfReach` | surowa intencja z `RayOrigin` przesuniętym o 1,9 m od oka serwera w stronę celu (w tolerancji), trafienie 7,5 m od `RayOrigin`, czyli 9,4 m od oka serwera → `OutOfReach`; to samo trafienie 7,9 m od oka przyjęte (D13) |
| `PlaceOnTerrain_ShiftedRayOrigin_RejectedOutOfReach` | jak wyżej dla `PlaceBlockIntent` z `TerrainTarget` |
| `Dig_BelowCore_RejectedBelowCore` | środek bliżej niż `R − CoreDepth + promień` → `BelowCore`, brak operacji |
| `Dig_HoldTenSeconds_NoRateLimitedReplies` | łącze 50 ms ± 10 ms: przyjęte ≤ 50, w każdym oknie 60 ticków serwera ≤ 5 przyjętych, zero odpowiedzi `RateLimited` |
| `Dig_RawFloodBypassingPacer_SixthWithinOneSecondRateLimited` | surowe intencje z pominięciem `IntentPacer`: 5 w ticku 0 przyjęte, 6. w tym samym ticku i każda następna przed tickiem 60 → `RateLimited`; po pełnym oknie przyjęta |
| `Dig_RawFloodTenSeconds_EveryWindowAtMostFive` | surowa intencja co tick przez 600 ticków: dla **każdego** okna 60 kolejnych ticków serwera ≤ 5 przyjętych (sprawdzone wszystkie 541 okien), reszta `RateLimited` (D13) |
| `Fill_SelectedMaterial_OpCarriesMaterial` / `Fill_UnknownMaterial_Rejected` | `Add` z materiałem z palety; nieznany → `UnknownMaterial` |
| `Fill_OverAnyOnlinePlayer_RejectedIntersectsPlayer` | kula przecina kapsułę drugiego gracza → `IntersectsPlayer` |
| `Build_OnTerrain_CreatesConstructMatchingGhost` | `ConstructCreateOp.Origin/Rotation` bitowo równe pozie ducha z podglądu (właściwość, FsCheck, 200 losowych trafień i kierunków) |
| `Build_OnFace_ExtendsConstruct` | `BlockSetOp` w komórce `Cell.Offset(Face)`, wersja +1 |
| `Build_BeyondReach_RejectedOutOfReach` | środek nowej komórki dalej niż 8,87 m od oka → `OutOfReach` |
| `Build_IntoOwnOrOtherCapsule_RejectedIntersectsPlayer` | własna kapsuła i kapsuła drugiego gracza |
| `Build_OverlappingOtherConstruct_RejectedCellOccupied` | wariant A (W2): nowa budowla obrócona o 45° obok istniejącej → `CellOccupied`; blok stykający się ścianą przyjęty |
| `Build_Rotated45Offset115_RejectedCellOccupied` | przypadek akceptacyjny D15: dwa sześciany jednostkowe, drugi obrócony o 45° wokół Y i przesunięty o 1,15 m w X względem środka pierwszego (przecinają się, choć żaden punkt próbkowy drugiego nie leży w pierwszym) → `CellOccupied`; przesunięcie 1,25 m → przyjęty |
| `Remove_LastBlock_RemovesConstruct` | `BlockClearOp`, `ConstructChange.Removed` |
| `Reject_ClientReportsReasonAndTrackerEmpty` | HUD-owy `LastRejection` równy kodowi serwera, `Count = 0` |
| `Disconnect_WithPendingIntents_TrackerClearedNewestFirst` | rozłączenie z 3 oczekującymi: `Disconnected`, puste |
| `ToolSession_RandomActions_LiveEqualsReplayEqualsCheckpointPlusTail` | 2 klientów, 2000 losowych akcji narzędzi (ziarno FsCheck w nazwie przypadku), `PersistenceWriter` na pliku tymczasowym, checkpoint w połowie: skrót stanu na żywo = pełna powtórka (świeży `WorldState.CreateNew` + `ServerSimulation.Replay` całego dziennika) = `WorldLoader.Load(plik)` (stan z checkpointu, `LoadedWorld.JournalTail`) + `ServerSimulation.Replay` ogona; kolekcja bez równoległości z poprawki B1. Stanu z checkpointu nie składa się ręcznie: jedyne publiczne drogi to `WorldLoader.Load` albo `WorldStateBuilder(WorldId, ulong galaxySeed)` z `SetProgress`/`AddPlanet`/`AddBlockType`/`AddMaterial`/`AddConstruct`/`Build()` (D16) |

`Horyzont.Core.Tests` (przegląd Opus 5.5 przed kodem):

| Test | Oczekiwanie |
| --- | --- |
| `ConstructPlacementTests.OnTerrain_GoldenCases_BitIdenticalToK1` | 64 przypadki zapisane przed refaktoryzacją, te same bity po niej |
| `ConstructPlacementTests.PlayerCapsule_GoldenDecisionsFromK1` | przed refaktoryzacją (krok 2) nagrane decyzje walidatora K1 (`CellHitsPlayer`/`SphereHitsPlayer` przez `IntentValidator` na kodzie z `main`) dla 256 ustalonych póz; po zmianie `IntersectsCell`/`IntersectsSphere` dają te same wyniki (D15) |
| `ConstructPlacementTests.PlayerCapsule_IndependentGeometry` | przypadki liczone ręcznie, niezależnie od pomocnika: kula styczna do boku kapsuły (odległość osi = r + 0,4 ± 1e-4), nad czubkiem i pod stopami, komórka obrócona o 45° muskająca kapsułę narożnikiem, komórka 1 mm od kapsuły; wartości wpisane w teście |
| `BlockOverlapTests` | SAT: przypadek 45° / 1,15 m → przecięcie; 1,25 m → brak; styk ścianą, krawędzią i wierzchołkiem → brak; obrót o 90° (osie równoległe, iloczyny krawędzi zerowe) bez NaN; symetria `Intersects(a, b) == Intersects(b, a)` (FsCheck); brak alokacji |
| `IntentValidationTests` (dopisane, W3) | okno przesuwne: 6. edycja w oknie 1 s → `RateLimited`, odmowa nie zużywa limitu; trafienie terenu > 8 m od oka serwera przy `RayOrigin` w tolerancji → `OutOfReach`; kolejność sprawdzeń bez zmian poza nowymi warunkami |
| `IntentValidationTests` (dopisane) | W2: `CellOccupied` przy nakładaniu dla obu celów; kolejność: `AreaNotLoaded` przed `CellOccupied` (nakładanie) przed `IntersectsPlayer` |
| `GameRulesTests.MaxToolRadius_NotAboveApplierRadiusLimit` | `new GameRules().MaxToolRadius ≤` stała limitu promienia appliera z B2 |
| `AllocationTests` (rozszerzone) | przyjęta `PlaceBlockIntent` na terenie obok innej budowli bez alokacji poza rekordami operacji |

Godot bez grafiki (infrastruktura testów gry z wcześniejszego pakietu K2; jeśli żaden jej nie założył, krok 10 dodaje scenariusz w trybie `--selftest tools` uruchamiany przez job `game` w CI, z kodem wyjścia):

| Scenariusz | Oczekiwanie |
| --- | --- |
| `SoloDigAndFill_RealTerrain` | prawdziwy `VoxelLodTerrain`: kopanie r = 2 m 4 m przed graczem → SDF w środku > 0; zasypanie materiałem `dirt` → SDF < 0 i indeks materiału zgodny z odwzorowaniem K2-09 |
| `SoloPlaceAndRemoveBlock_MeshUpdates` | postawienie bloku na terenie: test czeka na `ConstructWorld.ChunkMeshApplied` dla chunku nowej komórki z terminem 100 ms od odpowiedzi `IntentReply` (kontrakt K2-12: p99 ≤ 100 ms; jeden limit, nie „dwie klatki”, D14); usunięcie: węzeł budowli znika w tym samym terminie |
| `HostConstructs_WiredInComposition` | krok 0 (D5): u hosta `GameComposition` tworzy `ConstructWorld`, `OnApplied` przychodzi z tej samej ścieżki co `AppliedOperation`, klient hosta widzi budowlę bez drugiej kopii stanu; `ConstructWorld.ShutdownAsync` w kroku 7a kolejności K2-10 |
| `ToolAim_RealTerrainAdapter_DoesNotAllocate` | `ToolController.Update` z `GodotToolWorldView` nad prawdziwym `VoxelTerrainService.Raycast` (K2-09) i `ConstructWorld`: 1000 ticków celowania i podglądu po rozgrzaniu → 0 B zarządzanych (D16) |
| `SoloDigBelowCore_Refused` | gracz przeniesiony nad rdzeń (dev): odmowa `BelowCore`, teren bez zmian |

Ręczny (`Manual`, wynik i zrzuty w PR): 5 minut gry solo i 5 minut u hosta z drugim klientem lokalnym: kopanie z przytrzymaniem, zasypywanie trzema materiałami, budowla 5 × 5 × 3 z obrotami, usunięcie całej budowli, próba postawienia bloku w sobie, kopanie w TPP.

## Kroki

- [x] 0. (warunki wejścia sprawdzone na gałęzi pakietu, bo `main` nie ma jeszcze K2-08..K2-12; numery runów CI na `main` wpisać w PR po ich scaleniu, patrz Odstępstwa „Etap 2”) Warunki wejścia sprawdzone na `main` (lista wyżej, z numerami runów CI w PR). Kolumna „Rzeczywisty typ” w tabeli kontraktów konsumowanych wypełniona; brakująca zdolność → stop i zgłoszenie do pakietu, który ją miał dać. Odpowiedzi na otwarte pytania (P2–P5) wpisane albo przyjęte domyślne (zapis w Odstępstwach). **Wpięcie budowli K2-12 w `GameComposition` K2-10 i widok klienta hosta (D5)**: treść bloków, rejestr bloków dla nowego świata, `ConstructWorld` z `OnApplied`, widzami i zamknięciem (krok 7a w tabeli K2-10 i ZASADY pkt 7); test `HostConstructs_WiredInComposition` zielony.
- [x] 1. Katalog `Tools/` w `src/Horyzont.Client` z K2-10 i testy w `tests/Horyzont.Client.Tests`; `LayeringTests` bez zmian (projekt już na liście); referencja `Horyzont.Game.csproj` już jest.
- [x] 2. (testy napisane i zielone; osobny przegląd testów Core „przed kodem” się nie odbył — testy Core W1–W3 przejrzał Opus 5.5 w przeglądzie etapu 1, zob. „Decyzje po przeglądzie Opus”) Testy z tabel (czerwone), w tym testy „złote” W1 (`OnTerrain_GoldenCases_BitIdenticalToK1`, `PlayerCapsule_GoldenDecisionsFromK1`) nagrane na kodzie sprzed zmiany. Przegląd testów Core przez Opus 5.5.
- [x] 3. W1: `ConstructPlacement`, `PlayerCapsule`, walidator na nowych typach; testy Core zielone, skróty i test „złoty” protokołu bez zmian.
- [x] 4. W2 według odpowiedzi na P2 (domyślnie wariant A, `BlockOverlap` SAT); `IntentValidationTests`, `BlockOverlapTests` i `AllocationTests` zielone.
- [x] 4a. W3: `SlidingWindowLimiter`, `PlayerBuckets`, zasięg od oka serwera w `IntentValidator`; testy W3 i integracyjne tempa i zasięgu zielone (D13).
- [x] 5. `ToolSettings`, palety, `OrientationPicker`.
- [x] 6. `AimRayBuilder`, `ToolTargeting`, `ToolPreview` z podpowiedziami.
- [x] 7. `IntentPacer`, `PendingIntentTracker`, `IEditPrediction`/`NoEditPrediction`, `SessionIntentSender`.
- [x] 8. `ToolController` z testem alokacji.
- [x] 9. Testy integracyjne zielone, w tym live = replay = checkpoint + ogon w kolekcji bez równoległości.
- [x] 10. Godot: `Tools.tscn`, węzły z listy plików, akcje InputMap, `tools.csv`, rejestracja w composition root (`game/src/Boot/`), przekazanie `IntentReply` i `OnDisconnected` do trackera, wybór `NoEditPrediction` dla wszystkich trybów K2.
- [x] 11. (test ręczny odroczony do bramki K2, patrz Odstępstwa „Etap 2”) Scenariusze Godota bez grafiki zielone w jobie `game`; test ręczny opisany w PR.
- [x] 12. (lokalnie zielone 2026-10-09: sln 1720 testów, format, `--smoke`, `--engine-tests` 18, gdUnit 127, `server_smoke`; `build.py smoke` zastąpiony `--smoke` na gotowym silniku; CI po scaleniu: krok 13) Komentarze XML, `dotnet build`, `dotnet test --filter "Category!=Soak&Category!=Manual"`, `dotnet format --verify-no-changes`, `python tools/engine/build.py smoke` z wynikami w PR; nowe zasady dopisane do `docs/ZASADY.md` (kierunki z Godota normalizowane po konwersji do float; promień narzędzi zawsze od oka); każdy nieodhaczony krok ma wpis „odroczone do X”.
- [ ] 13. Po scaleniu: zielone CI na `main` (pełny przebieg, nie tylko na gałęzi).

## Odstępstwa i decyzje wykonawcy

(wypełnia wykonawca: odpowiedzi P2–P5, rzeczywiste nazwy kontraktów z kroku 0, każda zmiana sygnatur z tego planu)

### Etap 1: część niezależna od silnika (2026-10-09, Opus 5.5, gałąź `pakiet/k2-13-narzedzia`)

**Zakres etapu.** Kroki 1–9 (Core W1–W3, `Tools/` w `Horyzont.Client`, testy klienta i integracyjne). Poza etapem, odroczone do etapu Godota: krok 0 (wpięcie budowli K2-12 w `GameComposition`; tabela „Rzeczywisty typ” zostaje pusta), kroki 10–12 (węzły `game/`, scenariusze silnika, `python tools/engine/build.py smoke`, wiersze ZASADY dotyczące `game/`) i 13. Krok 2 nie jest odhaczony: testy są napisane i zielone, ale ich przegląd przez Opus 5.5 (testy Core: W1, W2, W3) jeszcze się nie odbył. Gałąź oparta na niescalonej gałęzi K2-11 (07ff1af: K2-08–K2-10, K2-12, K2-11 kroki 1–5, `ProtocolVersion` 3); `ProtocolVersion` nie został zmieniony.

**Odpowiedzi na pytania (domyślne, brak odpowiedzi właściciela).** P2: wariant A (`BlockOverlap`, SAT, `CellOccupied`). P3: nie, tylko teren i ściany budowli. P4: wszystkie materiały z rejestru poza `core:bedrock` (`MaterialPalette.CoreMaterialKey`, kopia `TerrainMaterials.CoreMaterialKey` z `game/`, bo `Horyzont.Client` nie widzi `game/`; wiring w `GameComposition` ma przekazać stałą z gry). P5: dopiero K3-15/K3-16; w K2 wszystkie tryby używają `NoEditPrediction`. P6: `src/Horyzont.Client` jak w K2-10.

**Dowód braku zmiany semantyki W1.** Złote pliki `tests/Horyzont.Core.Tests/Golden/ConstructPlacement/{on_terrain,player_capsule}.txt` (64 przypadki pozy i 256 decyzji kapsuły) nagrano kodem K1 w commicie `a37dce8`, który zawiera też rejestrator i odniesienie K1 (refleksja po `SphereHitsPlayer`/`CellHitsPlayer`), przed jakąkolwiek zmianą w `src/`. Po refaktoryzacji te same bity i decyzje; `DeterminismTests`, złoty test protokołu i zamrożony korpus dzienników terenu (`git diff cad1f70..HEAD` na tych ścieżkach) bez zmian.

**Zmiany sygnatur i dodatki względem planu.**
- Core: `Obb` (publiczny rekord) oraz `BlockOverlap.ForCell`, `BlockOverlap.OverlapsConstruct(store, frame, center, rotation, skip)` i `BlockOverlap.TryGetConstructPose` obok `Intersects`: ta sama reguła „nowa komórka nie nakłada się na inną budowlę” dla walidatora i podpowiedzi klienta (poza budowlą docelową, z budowlami zagnieżdżonymi). `PlayerCapsule.IntersectsLocalCell` (wewnętrzny) obsługuje budowle zagnieżdżone bez składania rotacji. `SlidingWindowLimiter` jest publiczny (lustro w kliencie). `OrientOnSurface`, `SphereHitsPlayer` i `CellHitsPlayer` w walidatorze wołają nowe typy; `Geometry.CapsuleAxis` przeniesiono do `PlayerCapsule.Axis`.
- W3: okno przesuwne dotyczy tylko `TerrainToolIntent` (bloki mają kubełek `Blocks`); przy `TerrainEditsPerSecond = 0` okno ma pojemność 1 (kubełek i tak nie przepuści). Zasięg od oka serwera sprawdzany zaraz po `NoHit`, przed `NoLineOfSight`.
- Test K1 `ServerSimulationTests.Submit_SixthTerrainEditInOneTick_...` zmieniony wprost zgodnie z D13: po 12 tickach szósta edycja nadal dostaje `RateLimited`, po 60 jest przyjęta (K1-03 mówiło „po 12 tickach”; zastąpione decyzją bramki).
- `AimRay` ma piąte pole `Feet` (kapsuła własnej postaci do podpowiedzi). `IToolWorldView` dostał `TryGetConstructPose` i `OverlapsConstruct` (adapter nie musi udostępniać `ConstructStore`; migawka K2-12 wystarczy). `ToolTargeting` ma `TryTarget` (budowanie) i `TryTargetTerrain` (manipulator, z informacją o budowli przed terenem). `ToolController`: `Pending`, `WaitingForServer`, `ClearAim`; intencja powstaje dopiero po przejściu `IntentPacer` i sprawdzeń (brak alokacji bez wysyłki). `IntentPacer.TryAcquire(PaceClass, repeat, tick)`. `PendingIntentTracker`: `LastOutcome`/`LastOutcomeSeq` (HUD i testy dla `TimedOut`/`Disconnected`); `Add` rzuca `InvalidOperationException` przy pełnym trackerze albo nierosnącym `ClientSeq` (błąd wywołującego; kontroler sprawdza `IsFull` przed wysyłką). Podpowiedzi liczy wewnętrzny `ToolPreviewBuilder`.
- Testy: `SimulationServerHooks.CreateHello` wysyła rejestry bloków i materiałów (potrzebne paletom); `K1GateTests` dostał `[Collection(SqlitePools.Name)]` (kolekcja bez równoległości także w `Horyzont.Integration.Tests`, B1). Testy alokacji używają „rundy bez alokacji” (stan ustalony po tier-upie JIT), bo pojedynczy pomiar bywa zaszumiony w pełnym przebiegu.
- Test `Build_OnTerrain_CreatesConstructMatchingGhost` (FsCheck, 200 przypadków) działa na `ServerSimulation` bez sieci; pozostałe testy integracyjne idą przez `ClientRuntime`, `InMemoryTransport` i `ServerSession`.

**Ryzyka i uwagi do przeglądu.**
- Klient celuje na `ReachMeters − ClientReachMarginMeters` (0,4 m), patrz „Decyzje po przeglądzie Opus”; limit serwera zostaje twardy.
- Gdyby `GameRules` serwera były konfigurowane inaczej niż domyślne, klient (te same domyślne wartości builda) rozjedzie się z limitem; skutkiem są tylko odmowy (zgłosić do K3-18).

**Decyzje po przeglądzie Opus (2026-10-09).**
- `BlockOverlap.TouchToleranceMeters` 1e-4 → 2e-3 m. Powód: współrzędne planety przy ~4000 m mają pół ziarna ≈ 1,2e-4 m, a błąd rzutowanej penetracji w `ValidatePlaceOnFace` sięga ≈ 4e-4 m, więc idealny styk ścianą mógł dać `CellOccupied`. Zmiana dotyczy tylko walidatora, nigdy niewydana, powtórka nie zależy od niej (nie wchodzi do dziennika) → bez podbicia wersji. Test: `BlockOverlapTests.OverlapsConstruct_FaceContactAtPlanetRadius_ToleratesFloatGrain` (styk liczony przez obrócone budowle przy ~3000–4000 m; z 1e-4 pada).
- Margines zasięgu klienta: stała `ToolTargeting.ClientReachMarginMeters = 0,4 m` (`ToolTargeting.ClientReach(rules)`) (≈ prędkość maks. × (RTT/2 + odstęp wysyłania)). Klient (`ToolTargeting`, `ToolPreviewBuilder`) używa `ReachMeters − margines`; cele na ścianach budowli zachowują `CellReachSlack` i odejmują ten sam margines. Serwer: bez zmian, twardo ≤ `ReachMeters` od oka serwera (D13). Testy: `ToolTargeting_HitBeyondReach_NoTarget` (granice 7,6 m), `ToolTargeting_TargetWithinMarginOfReach_NotOffered`.
- Budżet alokacji przyjętych budowli w `AllocationTests` zmierzony: 69 056 B na cztery budowle (stabilnie), próg 69 120 B (zaokrąglone w górę do 256 B); ścieżki odrzucone nadal „pętla do zera”.
- Budowla, której łańcuch rodziców nie sięga planety, jest odrzucana w `ValidatePlaceOnFace` kodem `WrongPlanet` (najbliższy istniejący; łańcuch rozwiązuje się do planety, ale jest głębszy niż śledzi test nakładania, 64) zamiast po cichu pomijać sprawdzenie nakładania. Test: `IntentValidationTests.Submit_PlaceOnFaceOfDeeplyNestedConstruct_RejectedWhenOverlapTestCannotFollowChain`.
- Składanie pozy zagnieżdżonych budowli w `BlockOverlap.TryGetPose` jest w `float`, a ZASADY pkt 2 wymagają `double`: odroczone do K5-20 (`Vector3D`). Złote przypadki kapsuły (`ConstructPlacement`) pokrywają tylko ścieżkę bez zagnieżdżenia.

### Etap 2: część Godota (2026-10-09, Opus 5.5, ta sama gałąź)

**Zakres etapu.** Krok 0 (warunki wejścia, kolumna „Rzeczywisty typ”, wpięcie budowli K2-12 w `GameComposition`, D5), krok 10 (węzły Godota, InputMap, `tools.csv`, rejestracja, `IntentReply` i `OnDisconnected`, podgląd, HUD), krok 11 (scenariusze silnika) i lokalna część kroku 12 (komentarze XML, ZASADY, SETUP).

**Warunki wejścia (krok 0).** `main` (3e85fad) jeszcze nie ma K2-08..K2-12, więc warunki 1–4 sprawdzono na gałęzi pakietu, która je zawiera (K2-08, K2-09, K2-10, K2-12, K2-11 kroki 1–5, bramka K1 B1–B3): `--smoke` i testy silnika zielone lokalnie; numery runów CI na `main` do wpisania w PR po scaleniu K2-08..K2-12 (krok 13). Odpowiedzi na P2–P5 bez zmian względem etapu 1 (domyślne).

**Wpięcie budowli (D5), nazwy i miejsca.**
- `HostConstructs` (`game/src/Constructs/HostConstructs.cs`, `IServerTickParticipant`) tworzy `ConstructWorld` pod `/Root/Server/Constructs` w każdym trybie z serwerem (solo, host, serwer dedykowany; bez siatek, `ConstructWorldMode.CollisionOnly`, gdy `env.Render` jest wyłączone), po starcie świata (`Attach`) inicjuje go nad `WorldStateConstructSource(ServerRuntime.State)` z `BlockRenderTable.Build(state.BlockTypes.Entries, katalog)` i `BlockVisuals`, wywołuje `RebuildAll` dla planety domowej, a co 30 ticków `SetViewers` z pozycji graczy online. Klient hosta nie ma drugiej kopii stanu ani drugiego `ConstructWorld`: jeden węzeł służy serwerowi (kolizje) i widokowi (siatki).
- `BlockContentLoader.Load()` jest wołany w `GameComposition.Create`; `catalog.BlockTypeKeys` trafia do `ServerRuntimeOptions.BlockTypeKeys` (dopisywanie brakujących kluczy przy wczytaniu) i do `WorldState.CreateNew` nowego świata.
- **Zmiana w Core (przegląd Opus):** `ServerSimulation.Applied` (`event Action<AppliedOperation>`), wołane po zapisie do dziennika w `Submit` i `ApplyServerOperation`, nigdy przy powtórce. Powód: w kodzie nie było „miejsca rozsyłającego `AppliedOperation`” (plan zakładał je z K2-10), a `ConstructWorld.OnApplied` potrzebuje `ConstructChanges`, których nie ma w `IJournalSink`. To nie jest ścieżka formatu (ZASADY 3), nie zmienia dziennika, powtórki ani hashowania; test `ServerSimulationTests.Applied_SubmitAndServerOperations_RaisedAfterJournalAndNeverForRejected`. Obsługa po stronie gry łapie własne wyjątki (operacja jest już zapisana).
- **Krok 7a zamykania:** `ServerRuntime.AddAfterTerrainShutdown(Func<Task>)` i krok `7a shut game systems down` między `7 shut terrain down` a `8 dispose transport` (pomijany, gdy nic nie zarejestrowano, więc istniejące sekwencje i testy kolejności bez zmian); `HostConstructs.Register` rejestruje `ConstructWorld.ShutdownAsync`. Test `ShutdownSequenceTests.Order_AfterTerrainTask_RunsAsStep7aBetweenTerrainAndTransport`; ZASADY pkt 7 i tabela K2-10 zaktualizowane. Kolejność kroków 1–8 K2-10 bez zmian.

**Węzły Godota (krok 10).** `game/src/Client/Tools/`: `ToolNode` (scena `game/scenes/client/tools/Tools.tscn`, `IClientSessionListener`, priorytet fizyki 20 po `LocalPlayer` 10; kontroler i palety powstają przy `OnServerHello`; `OnIntentReply` przekazuje odpowiedź do `PendingIntentTracker.OnReply`; `OnDisconnected` woła `Clear`), `ToolInputReader` (`IToolInputSource`; zdarzenia zliczane w `_Input`, oddawane w `Take`; bez wejścia przy zwolnionej myszy), `GodotToolWorldView` (stan i teren serwera w tym samym procesie, bez kopii), `TerrainToolPreview` (kula), `BlockGhostPreview` (sześcian 1,02 m w pozie podglądu; siatka typu z K2-12 odroczona do K8/UI, plan dopuszczał „albo sześcian”), `ToolHud` (etykieta w lewym dolnym rogu i celownik; teksty przez `Tr("TOOL_*")` i `Tr("REJECT_*")`). `GameComposition.CreateTools` składa je tylko dla lokalnego klienta procesu z serwerem (solo i host) i tylko przy `env.Characters`; wizualizacje tylko przy `env.Render`. `NoEditPrediction` we wszystkich trybach, w których narzędzia istnieją.
- **Odstępstwo: zdalny klient (`--connect`) nie dostaje narzędzi w K2.** `IToolWorldView` zdalnego klienta (teren własny, budowle bez widoku) to K3-15/K3-16 (plan: „Poza zakresem”); wysyłanie intencji bez celowania nie ma sensu. `ToolNode` przyjmie wtedy ich widok i `IEditPrediction`.
- Wejście: kółko trafia do `RadiusSteps`; `ToolNode` przekierowuje je do `PaletteSteps`, gdy aktywne jest budowanie (kontroler stosuje oba pola niezależnie). W kamerze deweloperskiej (`CameraMode.DevFree`) i bez aktywnego gracza kontroler dostaje zerowy promień: ustawienia się zmieniają, nic się nie wysyła, podgląd znika.
- InputMap: akcje z tabeli „Sterowanie” w `game/project.godot` (1, 2, LPM, PPM, kółko, M, R, T). Tłumaczenia `game/locale/tools.csv` (en, pl) zarejestrowane w `project.godot` (`internationalization/locale/translations`), bo HUD z nich korzysta (K2-12 zostawił `blocks.csv` niezarejestrowane; to zadanie K8/UI). Plik `.import` zatwierdzony.
- Katalogi: `game/src/Client/Tools/` i `game/scenes/client/tools/` zgodnie z planem (K2-10 nie założył `scenes/client/`, K2-11 użył `scenes/Character/`).
- Etykieta pomocy (`HelpOverlay`, F1) ma linię z klawiszami narzędzi; `docs/SETUP.md` ma je w „Uruchamianie trybów”.

**Scenariusze (krok 11).** Wszystkie w `game/tests/Tools/ToolScenarioTests.cs` (gdUnit4 pod `dotnet test game`, prawdziwy `VoxelLodTerrain`, prawdziwy `ConstructWorld`):
- `SoloDigAndFill_RealTerrain`: dołek r = 2 m pogłębia powierzchnię z 3,00 do 4,89 m, SDF w środku 1,42 > 0; zasyp dirt r = 1 m daje SDF −0,40 i indeks tekstury 1 (`DirtTextureIndex`).
- `SoloPlaceAndRemoveBlock_MeshUpdates`: siatka po odpowiedzi w 24,9 ms (≤ 100 ms); po usunięciu węzeł budowli znika w tym samym terminie.
- `HostConstructs_WiredInComposition`: tryb `--host`, jeden `ConstructWorld` w drzewie, kroki zamykania `7`, `7a`, `8` po kolei, 0 wątków mesherów po zamknięciu.
- `ToolAim_RealTerrainAdapter_DoesNotAllocate`: 0 B w 1000 ticków dla manipulatora i budowania (z `AimRayBuilder` w TPP).
- `SoloDigBelowCore_Refused`: `BelowCore`, brak nowej operacji. Gracz jest przestawiany jednym wywołaniem `UpdatePlayerKinematics` (serwerowa postać wróciłaby w następnym ticku), a obszar pod rdzeniem ładuje `VoxelViewer`.
- Własne: `Solo_HoldDig_AtMostFiveEditsInAnyOneSecondWindow` (przytrzymanie: 14 edycji, najgorsze okno 5, zero odmów), `Solo_NothingInReach_SendsNothing` (patrzenie w niebo: brak podglądu i intencji), `Solo_SaveAndRestart_KeepsTerrainAndConstructs` (kopanie, zasyp i postawienie bloku; po zamknięciu i ponownym starcie ten sam skrót terenu `0CD3C4538613B5ED`, te same budowle, węzeł budowli widoczny).
- Limity zasięgu, tempa i kolizji z graczem są pokryte testami integracyjnymi z etapu 1 (`Horyzont.Integration.Tests/Tools`); osobnego scenariusza `--selftest tools` nie dodano, bo infrastruktura gdUnit istnieje.
- Test ręczny (`Manual`: 5 minut solo i u hosta z drugim klientem, budowla 5 × 5 × 3, kopanie w TPP) nie został wykonany; odroczony do bramki K2 (drugi klient z narzędziami wymaga K3-15/K3-16). Zamiast niego zrzut ekranu z narzędziami w grze (`--screenshot`).

**Poprawki po przeglądzie Opus (Etap 2).**
- `ServerSimulation.Applied` jest wołane dopiero po rozliczeniu limitera i oknie terenu w `Submit` (i po zapisie do dziennika w `ApplyServerOperation`); wyjątek subskrybenta jest logowany (`SimulationLog.AppliedSubscriberFailed`) i połykany, symulacja nie przechodzi w stan błędu, a odpowiedź zostaje `Accepted`. Test: `Applied_ThrowingSubscriber_DoesNotFaultOrUndoTheOperation`; komentarz XML zdarzenia poprawiony.
- `HostConstructs`: po nieudanej aktualizacji widoku ustawiana jest flaga, a następny tick serwera robi `RebuildAll` (raz na serię błędów; błąd przebudowy jest tylko logowany). Dodane: `ViewUpdate` (szew testowy) i `ResyncCount`. Test silnika: `HostConstructs_FailedViewUpdate_ResyncsOnceOnTheNextTick`.
- Krok 7a zamykania: dokumentacja `AddAfterTerrainShutdown` zgodna z zachowaniem (`timeoutIsError: false`, błąd tylko w logu, kod wyjścia bez zmian); `RunAfterTerrainShutdownAsync` uruchamia wszystkie zadania mimo wyjątku wcześniejszego (każdy błąd logowany, na końcu `AggregateException`). Test: `AfterTerrainTasks_OneThrows_TheOthersStillRunAndTheStepIsLoggedOnly`.
- `ToolInputReader`: nazwy akcji jako statyczne `StringName` (bez alokacji napisu na zdarzenie).
- `ToolHud`: tekst budowany tylko przy zmianie wejść (narzędzie, blok, orientacja, materiał, promień, podpowiedź, oczekiwanie, ostatnia odmowa); tłumaczenia (`Tr`) w pamięci podręcznej czyszczonej przy `NotificationTranslationChanged`; wyszukiwanie nazw pętlą (bez LINQ i domknięć), `Label.Text` ustawiane tylko po zmianie.
- `--dev-tools-demo` wymaga `env.DevBuild`: `GameComposition.Create` odrzuca flagę w buildzie nie-deweloperskim z `ArgumentException` (komunikat „ARGUMENT ERROR” w `Boot`). Test: `DevToolsDemo_OnlyInADevelopmentBuild`; `docs/SETUP.md` uzupełniony.

## Odbiór

Testy z tabel przechodzą w CI na `main`; scenariusze Godota zielone; wynik testu ręcznego w PR. Opus 5.5 przegląda zmiany w Core (W1: dowód braku zmiany semantyki przez testy „złote”; W2: SAT, kolejność sprawdzeń, brak alokacji, brak wpływu na powtórkę; W3: okno przesuwne i zasięg od oka serwera), katalog `Tools/` i wpięcie budowli; resztę ocenia bramka K2. Kryterium planu projektu: kopanie, zasypywanie i budowanie działają w grze solo i u hosta, a każdy limit ze specyfikacji (zasięg 8 m, promień 0,5–3 m, 5 edycji terenu na sekundę, nie poniżej rdzenia, blok bez kolizji z graczem) ma test, który go łamie i dostaje odmowę z właściwym kodem.

## Pułapki

- U hosta teren jest wspólny: lokalne `ApplyEdit` po stronie klienta zastosowałoby edycję drugi raz. Predykcja terenu wolno włączyć tylko na osobnej instancji terenu zdalnego klienta (K3-16).
- Promień z kamery TPP dostanie `RayOriginMismatch`; zawsze buduj go od oka (`AimRayBuilder`).
- Oko klienta liczone z przewidzianej pozycji, a serwera z ostatniej pozycji z `UpdatePlayerKinematics`; przy sprincie i RTT 100 ms różnica to ułamek metra, tolerancja 2 m ją mieści. Nie zmniejszaj `MaxRayOriginErrorMeters` bez testu z opóźnieniem.
- Limity serwera (kubełek + okno 1 s) liczą się w tickach serwera, a lustro klienta w tickach klienta; jitter może przybliżyć intencje u serwera, stąd odstęp 13 i 15 ticków przy przytrzymaniu.
- `Vector3` z Godota w podwójnej precyzji: normalizuj po rzutowaniu na float, inaczej kodek albo walidator odrzuci kierunek (`InvalidParameters`).
- `Quaternion` w `System.Numerics` i Godocie ma kolejność (X, Y, Z, W), a „przód” to −Z w obu konwencjach budowli; duch bloku buduj z tej samej pozy, co `ConstructPlacement.OnTerrain`, a nie z bazy Godota liczonej osobno.
- Kółko myszy daje tylko zdarzenie naciśnięcia; zliczaj kroki w `_Process`, a zużywaj w `_PhysicsProcess`, inaczej przy wysokim FPS gubisz albo dublujesz kroki.
- Nie iteruj po `Dictionary` przy cofaniu oczekujących intencji; kolejność cofania jest kontraktem (`IEditPrediction.Revert` od najnowszej).
- `GameRules` klienta to wartości domyślne tego samego builda (uścisk dłoni sprawdza skrót builda). Jeśli K2-10 pozwala konfigurować `GameRules` na serwerze, klient może się rozjechać z limitem; skutkiem są tylko odmowy, ale zapisz to w Odstępstwach i zgłoś do K3-18.
- Kusi dodanie stałej albo warunku w `OperationApplier` „przy okazji”: to zmiana formatu dziennika (lekcja 1). Stop i eskalacja.
- Testy z SQLite (live = replay = checkpoint + ogon) muszą być w kolekcji bez równoległości z B1; nie wołaj `ClearAllPools`.
- `FakeTerrainService` nie zmienia powierzchni po edycji; testy kolejnych kopnięć w to samo miejsce sprawdzają operacje, a nie głębokość. Głębokość sprawdzają scenariusze Godota na prawdziwym terenie.
- Poza przypadkiem jednej budowli: dla budowli dzielących układ rodzica licz pozy względne (najpierw O_A − O_B), zamiast składać każdą w układzie planety i porównywać duże współrzędne float. Do zrobienia w K3/K5.
