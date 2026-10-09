# K2-12 · Budowle: siatki, chunki, meshing, kolizje, rejestr bloków

| | |
| --- | --- |
| Prowadzi | GPT-6.1 Sol (Codex) |
| Testy akceptacyjne | GPT-6.1 Sol; test złoty tabeli orientacji zatwierdza Opus 5.5 w przeglądzie |
| Przegląd | Opus 5.5 (ZASADY pkt 9: pakiet GPT-6.1 Sol → Opus 5.5; każda zmiana w `src/Horyzont.Core/` i tak wymaga Opus 5.5, pkt 3) |
| Zależy od | K1-02 (etap B: projekt `game/`), K1-03, K2-08A (cel `editor-linux` w `engine/engine.json` i artefakt `engine-linux-editor` — bez niego testy silnika nie ruszą na Linuksie; decyzja bramki D6). Nie zależy od K2-08 etap B ani od K2-09 … K2-11; uprząż testów silnika patrz krok 2 |
| Gałąź | `pakiet/k2-12-budowle` |

**Cel:** budowle z kodu Core (K1-03) stają się widoczne i fizyczne: tabela 24 orientacji jako format, katalog typów bloków z treści gry, greedy meshing chunków 16³ na wątkach roboczych, kolizje `ConcavePolygonShape3D` per chunk i węzeł `ConstructWorld`, który odświeża tylko zmienione chunki; budowla 64³ bloków pojawia się i jest edytowana bez przycięć klatki dłuższych niż 50 ms.

## Kontekst

- `docs/ZASADY.md`: pkt 2 (granice, composition root), 3 (kontrakty chronione: `World/` z `BlockShape`, `Face`, `BlockState`), 4 (determinizm, tylko Core), 5, 6 (meshing to gorąca ścieżka: brak alokacji w stanie ustalonym, test alokacji), 7, 8 (kategorie testów, `FakeTimeProvider`).
- `docs/spec/01-planeta.md`: „Model świata → Budowle” (siatka 1 m, ±2048, chunki 16³, stan bloku 32 bity, 24 orientacje, typy bloków jako dane, mapa klucz → numer, blok zastępczy, render i kolizje per chunk, greedy meshing, gotowe siatki innych kształtów, wątki robocze, `ConcavePolygonShape3D`), „Architektura” (serwer liczy fizykę wszystkich graczy; zdalny klient buduje kolizje budowli tylko do predykcji; host ich nie dubluje), „Stan, operacje i dziennik” (`ConstructCreate`, `BlockSet`, `BlockClear`, wersja budowli), „Ryzyka” (wydajność meshingu budowli w C#: wątki robocze, bufory z puli, profilowanie), „Kryteria ukończenia” (przycięcia ≤ 50 ms).
- `docs/plans/K1-03-core-stan-i-operacje.md`: `Horyzont.Core.World.Blocks` (`BlockTypeDef`, `BlockShape`, `BlockTypeRegistry.MissingKey`), akapit „Rejestry zmieniają się tylko przy tworzeniu i wczytywaniu świata” (linia ~260), tabela zakresów kodów.
- `docs/plans/K1-05-persistence.md`: `WorldLoadOptions.BlockTypeKeys`, `WorldLoader.GrowRegistries`.
- Kod K1 (zweryfikowany na `main` 884d40d):
  - `src/Horyzont.Core/World/Constructs/`: `BlockState` (`OrientationCount = 24`, `IsWellFormed`, pola bitowe), `CellPos` (`Limit = 2048`, `Chunk`, `LocalIndex = (X&15)<<8 | (Y&15)<<4 | (Z&15)`, `Offset(Face)`, `FromChunk`), `ChunkPos` (`IComparable`), `ConstructChunk` (`Size = 16`, `CellCount`, `Blocks : ReadOnlySpan<uint>`, `NonEmptyCount`), `Construct` (`Id`, `Parent`, `ParentSeq`, `Mode`, `Origin`, `Rotation`, `Version`, `BlockCount`, `BoundsMin/Max`, `TryGetChunk`, `GetBlock`, `Chunks` — alokuje posortowaną tablicę przy każdym wyliczeniu), `ConstructStore` (`All` posortowane po `EntityId`, `TryGet`, `InFrame`), `Face` (`PosX=0, NegX=1, PosY=2, NegY=3, PosZ=4, NegZ=5`), `ConstructRaycast`, `ConstructMode`.
  - `src/Horyzont.Core/World/Blocks/`: `BlockShape { Cube = 1 }` („further shapes arrive in package 12”), `BlockTypeDef(string Key, BlockShape Shape, byte MaterialId, bool HasCollision)` (`MaterialId` = „Visual material”; nigdzie nie serializowany), `BlockTypeRegistry` (`MissingKey = "core:missing"`, `Entries`, `TryGetId`, `TryGetKey`, `IsKnown`), `RegistryEntry(ushort Id, string Key)`.
  - `src/Horyzont.Core/Operations/`: `AppliedOperation(OpId, Tick, PlayerId?, Operation, TerrainVersions, ConstructChanges)`, `ConstructChange(EntityId Construct, uint Version, bool Removed)`, `ConstructCreateOp`, `BlockSetOp`, `BlockClearOp`, `EntityReparentOp`.
  - `src/Horyzont.Core/Protocol/Messages/`: `OperationApplied(OpId, Tick, Operation, ConstructChanges)`, `ConstructState(Header, PartIndex, PartCount, Chunks)`, `ConstructRemoved`, `ServerHello(..., BlockTypes, Materials)` (rejestr to same pary `Id`–`Key`; definicje typów klient bierze z własnej treści po kluczu).
  - `src/Horyzont.Core/Checkpoints/WorldStateBuilder` (publiczny: `AddBlockType`, `AddConstruct(ConstructHeaderRecord)`, `AddChunk(ConstructChunkRecord)`, `Build`) — do fikstur testowych.
  - `src/Horyzont.Core/Simulation/IntentValidator.cs` (linia ~316: orientacja < 24, typ znany), `IntentResolver.cs` (`BlockState.Create(typeId, orientation)`), `ConstructRaycast` (każdy niepusty blok to pełny sześcian).
  - `tests/Horyzont.Testing/WorldFactory.cs` (`StoneBlockKey = "core:stone_block"`), `tests/Horyzont.Architecture.Tests/LayeringTests.cs`.
- `game/` z K1-02B (`F:/dev/horyzont-k1-02b/game`, gałąź `pakiet/k1-02b-godot`): `Horyzont.Game.csproj` (Godot.NET.Sdk 4.7.2, net10.0, referencje Core/Persistence/Net), `src/Boot/Boot.cs`, puste `src/Client`, `src/Server`, `src/Shared`; `physics_ticks_per_second=60`, `forward_plus`; brak ustawionego silnika fizyki i nazw warstw.
- Plany rodzeństwa: `K2-09-adapter-voxel-tools.md` (ServerSimulation i `ITerrainService` na wątku głównym Godota; uprząż testów silnika w jobie `game`), `K2-10-tryby-gry.md` (kanoniczna kolejność zamykania, `GameComposition`), `K2-13-narzedzia.md` (krok 0: wpięcie budowli; sekcja „Kontrakty konsumowane”: oczekuje od K2-12 źródła budowli dla klienta hosta, odświeżenia siatki po `BlockSet`/`BlockClear`/`ConstructCreate`, tabeli 24 orientacji z `UpSteps`/`YawSteps`, definicji typów do palety i ducha).

## Zakres

1. **Core (chroniony, przegląd Opus):** `BlockOrientation` — tabela 24 orientacji jako format zapisu; `BlockShape.Mesh = 2`. Bez nowych operacji, intencji, komunikatów, blobów i odmów: pakiet nie bierze kodów z zakresu 8–19.
2. **Nowy projekt `src/Horyzont.Constructs`** (czysty .NET, referencja wyłącznie do Core; bez Godota): katalog typów bloków z treści, tabela renderu (`BlockRenderTable`), wejście chunku z obwódką, greedy mesher renderu i kolizji, wbudowane siatki kształtów, kolejka przebudowy chunków, zbieranie brudnych chunków z operacji, źródło budowli `IConstructSource` z implementacją nad `WorldState`.
3. **Gra (`game/src/Constructs/`)**: `ConstructWorld` (węzeł), węzły budowli i chunków, pula wątków roboczych, przesyłanie siatek i kolizji z budżetem na klatkę, materiały bloków (`Texture2DArray` + shader), ładowanie treści z `res://content/blocks/blocks.json`, fabryka siatki pojedynczego bloku (duch dla K2-13), stałe warstw fizyki.
4. **Treść K2:** typy `core:stone_block`, `core:wood_planks`, `core:metal_plate`, `core:stone_slab` (kształt `Mesh`, `builtin:slab`), `core:stone_slope` (`Mesh`, `builtin:slope`) i wbudowany `core:missing`; materiały wizualne jako kolory (tekstury dopiero K8-50); nazwy w `game/locale/blocks.csv` (pl, en).
5. **Testy:** jednostkowe Core i `Horyzont.Constructs`, testy silnika bez grafiki (Godot headless), test przycięć 64³ (`Slow`, CI Windows i Linux) i pomiar ręczny z GPU (`Manual`, wynik w PR).

## Poza zakresem

- Replikacja budowli do zdalnego klienta (lustro po stronie klienta zasilane `ConstructState`/`OperationApplied`, `ResyncRequest` 21): K3-15. K2-12 daje interfejs `IConstructSource`, który K3-15 zaimplementuje nad lustrem.
- Narzędzie budowania, duch, paleta, wybór orientacji, intencje: K2-13 (z kontraktów tego pakietu).
- Spinanie z trybami procesu i composition root (`GameComposition` z K2-10), wołanie `ConstructWorld.OnApplied` z miejsca, które rozsyła `AppliedOperation`, przekazanie pozycji widzów, widok budowli u klienta hosta: **K2-13 krok 0** (decyzja bramki D5; K2-13 zależy od K2-10 i K2-12). Ten pakiet dostarcza bibliotekę (`Horyzont.Constructs`, `ConstructWorld`, mesher, kolizje) i własną scenę testową (`ConstructsDemo.tscn`, testy silnika wołające API ręcznie).
- Fizyka postaci (kapsuła, `up_direction`): K2-11; kolizje budowli tylko udostępnia warstwą `Constructs`.
- Zmiana walidatora/raycastu Core pod kształty inne niż sześcian albo bloki bez kolizji (Decyzja 6) i dowolna zmiana semantyki stosowania operacji.
- Wytrzymałość konstrukcji, łączenie i dzielenie budowli, bloki funkcyjne, przezroczystość, okluzja otoczenia (AO), LOD budowli, tekstury artystyczne: późniejsze podprojekty (K6-30, K6-36, K8-50).
- BenchmarkDotNet: odroczone do K4-19 (benchmarki); tu test `Slow` z pomiarem w wyjściu testu.

## Projekt

### Pliki

```
src/Horyzont.Core/World/Constructs/BlockOrientation.cs         tabela 24 orientacji (FORMAT)
src/Horyzont.Core/World/Blocks/BlockShape.cs                   + Mesh = 2

src/Horyzont.Constructs/Horyzont.Constructs.csproj             net10.0, ref: Horyzont.Core; IsAotCompatible jak Core
src/Horyzont.Constructs/AssemblyMarker.cs
src/Horyzont.Constructs/Content/BlockTypeCatalog.cs
src/Horyzont.Constructs/Content/BlockTypeContent.cs
src/Horyzont.Constructs/Content/BlockMaterialDef.cs
src/Horyzont.Constructs/Content/ShapeMesh.cs
src/Horyzont.Constructs/Content/IShapeMeshResolver.cs
src/Horyzont.Constructs/Content/BuiltInShapeMeshes.cs          builtin:slab, builtin:slope
src/Horyzont.Constructs/Meshing/BlockRenderInfo.cs
src/Horyzont.Constructs/Meshing/BlockRenderTable.cs
src/Horyzont.Constructs/Meshing/ChunkMeshInput.cs              18³ z obwódką 1 komórki
src/Horyzont.Constructs/Meshing/ChunkMeshData.cs               bufory wyjściowe z puli
src/Horyzont.Constructs/Meshing/ChunkMesher.cs                 greedy: render + kolizja
src/Horyzont.Constructs/Scheduling/ConstructChunkKeyComparer.cs  porządek (EntityId, ChunkPos) dla Core.Checkpoints.ConstructChunkKey
src/Horyzont.Constructs/Scheduling/ChunkRemeshQueue.cs         brudne → zadania → wyniki, generacje
src/Horyzont.Constructs/Scheduling/RemeshJob.cs
src/Horyzont.Constructs/Sources/IConstructSource.cs
src/Horyzont.Constructs/Sources/ConstructPose.cs
src/Horyzont.Constructs/Sources/WorldStateConstructSource.cs
src/Horyzont.Constructs/Sources/ConstructDirtyChunks.cs        operacja → brudne chunki

tests/Horyzont.Core.Tests/World/Constructs/BlockOrientationTests.cs
tests/Horyzont.Core.Tests/Protocol/ProtocolContractTests.cs    + BlockShape.Mesh = 2
tests/Horyzont.Constructs.Tests/                               xUnit v3 mtp-off, FsCheck.Xunit.v3, <Using Include="Xunit" />
  Content/BlockTypeCatalogTests.cs
  Meshing/BlockRenderTableTests.cs
  Meshing/ChunkMesherTests.cs
  Meshing/ChunkMesherPropertyTests.cs                          wyrocznia: NaiveFaceOracle
  Meshing/ChunkMesherAllocationTests.cs
  Meshing/NaiveFaceOracle.cs                                   tylko w testach
  Scheduling/ChunkRemeshQueueTests.cs
  Sources/ConstructDirtyChunksTests.cs
  Sources/WorldStateConstructSourceTests.cs
  Timing/MesherTimingTests.cs                                  [Slow], kolekcja nierównoległa
tests/Horyzont.Testing/ConstructFixtures.cs                    budowle 64³: Solid, Building, Random35, Checkerboard
tests/Horyzont.Architecture.Tests/LayeringTests.cs             + Horyzont.Constructs (bez Godota; dozwolone: Horyzont.Core)

game/Horyzont.Game.csproj                                      + ProjectReference Horyzont.Constructs
game/project.godot                                             nazwy warstw fizyki 3D (layer_names/3d_physics)
game/src/Shared/Physics/CollisionLayers.cs                     Terrain = 1, Constructs = 2, ServerBodies = 3, ClientBodies = 4
game/src/Constructs/ConstructWorld.cs                          Node3D: API pakietu
game/src/Constructs/ConstructWorldOptions.cs
game/src/Constructs/ConstructWorldMode.cs                      RenderAndCollision | CollisionOnly
game/src/Constructs/ConstructNode.cs                           Node3D: poza budowli w ramie planety
game/src/Constructs/ConstructChunkNode.cs                      MeshInstance3D? + StaticBody3D/CollisionShape3D/ConcavePolygonShape3D
game/src/Constructs/MeshWorkerPool.cs                          wątki robocze, kanał zadań i wyników
game/src/Constructs/ChunkMeshUploader.cs                       ChunkMeshData → ArrayMesh / faces
game/src/Constructs/BlockVisuals.cs                            Texture2DArray + ShaderMaterial z katalogu
game/src/Constructs/BlockContentLoader.cs                      res://content/blocks/blocks.json → BlockTypeCatalog
game/src/Constructs/GodotShapeMeshResolver.cs                  res://*.tres|.obj → ShapeMesh (wątek główny, przy starcie)
game/src/Constructs/BlockMeshFactory.cs                        siatka pojedynczego bloku (duch K2-13)
game/shaders/blocks.gdshader
game/content/blocks/blocks.json
game/locale/blocks.csv
game/tests/Constructs/…                                        testy silnika (tabela niżej)
docs/ZASADY.md                                                 pkt 3: BlockOrientation w liście typów; pkt 2: zasada migawek dla wątków meshingu
```

Katalogi testów silnika (`game/tests/`) i ich runner przyjmij z uprzęży, która będzie na `main` (K2-08 albo K2-09); krok 2 opisuje wariant, gdy jej jeszcze nie ma.

### Kontrakty udostępniane

| Kontrakt | Plik | Dla |
| --- | --- | --- |
| `BlockOrientation` (tabela 24 orientacji, `Up`, `Forward`, `Right`, `FromUpForward`, `WithYaw`, `WithUp`, `UpSlot`, `ToConstruct`, `ToBlock`, `ToQuaternion`) | `src/Horyzont.Core/World/Constructs/BlockOrientation.cs` | K2-13 (`OrientationPicker`, duch), K3-15, K6 |
| `BlockShape.Mesh = 2` | `src/Horyzont.Core/World/Blocks/BlockShape.cs` | treść, mesher |
| `BlockTypeCatalog`, `BlockTypeContent`, `BlockMaterialDef`, `ShapeMesh` | `src/Horyzont.Constructs/Content/` | K2-13 krok 0 (`BlockTypeKeys` dla `CreateNew`/`WorldLoadOptions` w `GameComposition` K2-10), K2-13 (paleta, nazwy, duch) |
| `BlockRenderTable.Build(IReadOnlyList<RegistryEntry>, BlockTypeCatalog)` | `src/Horyzont.Constructs/Meshing/BlockRenderTable.cs` | K2-13 krok 0 (po wczytaniu świata albo z `ServerHello.BlockTypes`), K3-15 |
| `IConstructSource`, `ConstructPose`, `WorldStateConstructSource` | `src/Horyzont.Constructs/Sources/` | K2-13 (`GodotToolWorldView` w trybie solo/host), K3-15 (lustro klienta) |
| `ConstructWorld` (`Initialize`, `OnApplied`, `RebuildAll`, `SetViewers`, `ShutdownAsync`, `ChunkMeshApplied`, `TryGetConstructNode`) | `game/src/Constructs/ConstructWorld.cs` | K2-13 krok 0 (skład w `GameComposition`, start, zamknięcie), K2-13 (odświeżenie po operacji), K3-15 |
| `BlockMeshFactory.CreateBlockMesh(ushort typeId, byte orientation)` i `BlockVisuals.Material` | `game/src/Constructs/` | K2-13 (`BlockGhostPreview`) |
| `BlockContentLoader.Load()` | `game/src/Constructs/BlockContentLoader.cs` | K2-13 krok 0 (composition root K2-10) |
| `CollisionLayers` | `game/src/Shared/Physics/CollisionLayers.cs` | K2-09 (teren), K2-11 (postać: `ServerBodies`, `ClientBodies`), K2-13 (promień podglądu) |

#### Core: tabela orientacji (FORMAT)

```csharp
namespace Horyzont.Core.World.Constructs;

/// <summary>
/// The 24 orientations of a block. FORMAT: the index is stored in <see cref="BlockState"/> (journal, checkpoints,
/// protocol), so the table never changes. Index = UpSlot * 4 + Yaw. Identity (0) = Up +Y, Forward −Z.
/// Right = Forward × Up. Yaw k = Forward of yaw 0 turned k quarter turns counter-clockwise about Up (right-hand rule).
/// </summary>
public static class BlockOrientation
{
    public const int Count = 24;                 // == BlockState.OrientationCount (test)
    public const byte Identity = 0;
    public static bool IsValid(byte orientation);            // < 24
    public static int UpSlot(byte orientation);              // orientation >> 2; slots: 0 PosY, 1 NegY, 2 PosX, 3 NegX, 4 PosZ, 5 NegZ
    public static int Yaw(byte orientation);                 // orientation & 3
    public static Face UpOfSlot(int slot);
    public static Face Up(byte orientation);                 // image of block-local +Y
    public static Face Forward(byte orientation);            // image of block-local −Z
    public static Face Right(byte orientation);              // image of block-local +X
    public static bool TryFromUpForward(Face up, Face forward, out byte orientation); // false when not perpendicular
    public static byte FromUpForward(Face up, Face forward);  // ArgumentException when not perpendicular
    public static byte WithYaw(byte orientation, int quarterTurns);  // any int, modulo 4, keeps Up
    public static byte WithUp(byte orientation, int upSlot);         // keeps Yaw index
    public static Face ToConstruct(byte orientation, Face blockFace); // block-local face → construct face
    public static Face ToBlock(byte orientation, Face constructFace);  // inverse
    public static Quaternion ToQuaternion(byte orientation);          // System.Numerics, exact for the 24 rotations
}
```

Metody publiczne rzucają `ArgumentOutOfRangeException` dla orientacji ≥ 24; kod, który czyta dane z sieci lub dysku (mesher), najpierw woła `IsValid`. Tabela jest literałem w kodzie (`private static ReadOnlySpan<byte>` z parami ścian), nie wynikiem obliczeń; test złoty powtarza ją niezależnie:

| Indeks | Up | Forward | | Indeks | Up | Forward | | Indeks | Up | Forward |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | PosY | NegZ | | 8 | PosX | NegZ | | 16 | PosZ | PosY |
| 1 | PosY | NegX | | 9 | PosX | PosY | | 17 | PosZ | NegX |
| 2 | PosY | PosZ | | 10 | PosX | PosZ | | 18 | PosZ | NegY |
| 3 | PosY | PosX | | 11 | PosX | NegY | | 19 | PosZ | PosX |
| 4 | NegY | PosZ | | 12 | NegX | NegZ | | 20 | NegZ | NegY |
| 5 | NegY | NegX | | 13 | NegX | NegY | | 21 | NegZ | NegX |
| 6 | NegY | NegZ | | 14 | NegX | PosZ | | 22 | NegZ | PosY |
| 7 | NegY | PosX | | 15 | NegX | PosY | | 23 | NegZ | PosX |

Yaw 0 dla danego Up to obraz −Z przy obróceniu +Y na Up wokół osi X (Up ∈ {NegY, PosZ, NegZ}) albo osi Z (Up ∈ {PosX, NegX}). Dla K2-13: `YawSteps` = `WithYaw(o, ±1)`, `UpSteps` = `WithUp(o, (UpSlot(o) + 1) % 6)`; obie drogi osiągają wszystkie 24 indeksy.

#### `Horyzont.Constructs`: treść i tabela renderu

```csharp
namespace Horyzont.Constructs.Content;

/// <summary>Block types and visual materials of the game content (not saved; rebuilt at every start).</summary>
public sealed class BlockTypeCatalog
{
    public const int FormatVersion = 1;
    /// <summary>Parses blocks.json. Throws InvalidDataException with a JSON path on any invalid entry.</summary>
    public static BlockTypeCatalog Parse(ReadOnlySpan<byte> utf8Json, IShapeMeshResolver meshes);
    /// <summary>Keys in content order without core:missing; pass to WorldState.CreateNew / WorldLoadOptions.BlockTypeKeys.</summary>
    public IReadOnlyList<string> BlockTypeKeys { get; }
    public IReadOnlyList<BlockTypeContent> Blocks { get; }        // content order, then core:missing
    public IReadOnlyList<BlockMaterialDef> Materials { get; }     // Id from 1, content order
    public BlockTypeContent Missing { get; }                      // built-in core:missing
    public bool TryGet(string key, [NotNullWhen(true)] out BlockTypeContent? block);
}

/// <param name="Def">Core definition; Def.MaterialId = BlockMaterialDef.Id (content-local, never persisted).</param>
/// <param name="NameKey">Translation key, e.g. BLOCK_STONE_BLOCK.</param>
/// <param name="Mesh">Prefab mesh for BlockShape.Mesh, null for Cube.</param>
public sealed record BlockTypeContent(BlockTypeDef Def, string NameKey, ShapeMesh? Mesh);

public readonly record struct BlockMaterialDef(byte Id, string Key, uint ColorRgba8);

/// <summary>Immutable triangle mesh in block space [0,1]³, Godot winding (clockwise front faces).</summary>
public sealed class ShapeMesh
{
    public ShapeMesh(Vector3[] positions, Vector3[] normals, Vector2[] uvs, int[] indices, bool occludesNothing = true);
    public ReadOnlySpan<Vector3> Positions { get; } public ReadOnlySpan<Vector3> Normals { get; }
    public ReadOnlySpan<Vector2> Uvs { get; }       public ReadOnlySpan<int> Indices { get; }
}

public interface IShapeMeshResolver { bool TryResolve(string meshRef, [NotNullWhen(true)] out ShapeMesh? mesh); }
public static class BuiltInShapeMeshes { public static bool TryResolve(string meshRef, [NotNullWhen(true)] out ShapeMesh? mesh); } // "builtin:slab", "builtin:slope"
```

```csharp
namespace Horyzont.Constructs.Meshing;

public readonly struct BlockRenderInfo
{
    public BlockShape Shape { get; }        // 0 = empty cell
    public byte MaterialId { get; }         // layer in BlockVisuals.Texture2DArray = MaterialId - 1
    public bool HasCollision { get; }
    public bool Occludes { get; }           // Cube only
    public bool IsMissing { get; }          // registry key unknown to content → rendered as core:missing
    public ShapeMesh? Mesh { get; }
}

/// <summary>Immutable lookup typeId → render info for one world session; safe to read from any thread.</summary>
public sealed class BlockRenderTable
{
    public static BlockRenderTable Build(IReadOnlyList<RegistryEntry> registry, BlockTypeCatalog catalog);
    public ref readonly BlockRenderInfo Get(ushort typeId);   // 0 → empty; unknown/out of range → Missing
    public int MissingKeyCount { get; }                       // logged once by the caller
}

public sealed class ChunkMeshInput   // reusable, one per job slot
{
    public const int Padded = ConstructChunk.Size + 2;        // 18
    public ConstructChunkKey Key { get; }
    public uint Generation { get; }
    public Span<uint> Cells { get; }                          // 18³, index (x+1)*324 + (y+1)*18 + (z+1), x,y,z ∈ [−1,16]
    /// <summary>Main thread: copies the chunk and the 6 face slabs of neighbours (edges and corners stay empty).</summary>
    public bool Fill(IConstructSource source, ConstructChunkKey key, uint generation);
}

public sealed class ChunkMeshData    // reusable, grows by ArrayPool, never shrinks below its high-water mark
{
    public ReadOnlySpan<Vector3> Positions { get; } public ReadOnlySpan<Vector3> Normals { get; }
    public ReadOnlySpan<Vector2> Uvs { get; }       public ReadOnlySpan<Vector2> Uv2 { get; } // Uv2.X = material layer
    public ReadOnlySpan<int> Indices { get; }
    public ReadOnlySpan<Vector3> CollisionFaces { get; }  // 3 vertices per triangle (ConcavePolygonShape3D.Data)
    public bool IsEmpty { get; }
    public void Clear();
}

/// <summary>Greedy mesher. One instance per worker thread; no allocation after warm-up (ZASADY pkt 6).</summary>
public sealed class ChunkMesher
{
    public void Build(ChunkMeshInput input, BlockRenderTable table, ChunkMeshData output);
}
```

Współrzędne wyjścia są w układzie budowli względem początku chunku (0 … 16), oś i skrętność jak w Core i Godocie (prawoskrętny, +Y w górę, przód −Z).

#### Źródło budowli i brudne chunki

Klucz chunku to istniejący `Horyzont.Core.Checkpoints.ConstructChunkKey(EntityId Construct, ChunkPos Chunk)` z K1-03 (bez nowego typu o tej samej nazwie); porządek daje `ConstructChunkKeyComparer` w `Horyzont.Constructs`.

```csharp
namespace Horyzont.Constructs.Sources;

public readonly record struct ConstructPose(EntityId Id, FrameRef Parent, Vector3 Origin, Quaternion Rotation, uint Version, CellPos BoundsMin, CellPos BoundsMax);

/// <summary>Read side of constructs for meshing and tools. Main thread only. Implemented over WorldState (solo, host,
/// dedicated server) here and over the client mirror in K3-15.</summary>
public interface IConstructSource
{
    void CollectConstructs(FrameRef frame, List<EntityId> into);                 // sorted by EntityId
    bool TryGetPose(EntityId id, out ConstructPose pose);
    void CollectChunks(EntityId id, List<ChunkPos> into);                        // sorted; allocation-free path
    bool TryGetChunkBlocks(EntityId id, ChunkPos chunk, out ReadOnlySpan<uint> blocks); // CellPos.LocalIndex order
    BlockState GetBlock(EntityId id, CellPos cell);                              // Empty when absent
}

public sealed class WorldStateConstructSource(WorldState state) : IConstructSource { … }

public static class ConstructDirtyChunks
{
    /// <summary>Chunks to remesh after an operation: the chunk of the cell plus each neighbour chunk the cell touches
    /// (cell on a chunk face); constructs from ConstructChange with Removed = true are reported separately.</summary>
    public static void Collect(Operation operation, IReadOnlyList<ConstructChange> changes,
                               List<ConstructChunkKey> dirty, List<EntityId> created, List<EntityId> removed);
}
```

`WorldStateConstructSource.CollectChunks` nie używa `Construct.Chunks` (alokuje tablicę przy każdym wyliczeniu); jeśli `Construct` nie daje drogi bez alokacji, wolno wołać `Chunks` tylko w `RebuildAll` i przy tworzeniu widoku budowli (nie co klatkę) — wpis w Odstępstwach. Zmiana `Construct` w Core poza zakresem.

#### Gra: `ConstructWorld`

```csharp
namespace Horyzont.Game.Constructs;

public enum ConstructWorldMode { RenderAndCollision, CollisionOnly }

public sealed record ConstructWorldOptions
{
    public ConstructWorldMode Mode { get; init; } = ConstructWorldMode.RenderAndCollision; // CollisionOnly: dedicated server
    public int WorkerCount { get; init; } = Math.Clamp(Environment.ProcessorCount / 4, 1, 3);
    public int MaxInFlightJobs { get; init; } = 8;            // bound of the job and result queues
    public double MaxApplyMillisecondsPerFrame { get; init; } = 4; // at least one result per frame
    public float RenderRadiusMeters { get; init; } = 1000;
    public float CollisionRadiusMeters { get; init; } = 128;
    public float HysteresisMeters { get; init; } = 64;
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(2);
}

public sealed partial class ConstructWorld : Node3D
{
    public void Initialize(IConstructSource source, BlockRenderTable table, BlockVisuals? visuals,
                           ConstructWorldOptions options, ILogger<ConstructWorld> logger);  // visuals null in CollisionOnly
    /// <summary>Main thread, after each applied operation (server: AppliedOperation; client: OperationApplied in K3-15).</summary>
    public void OnApplied(Operation operation, IReadOnlyList<ConstructChange> changes);
    /// <summary>After world load/replay or resync: rebuild every construct of the frame.</summary>
    public void RebuildAll(FrameRef frame);
    /// <summary>Viewer positions in the planet frame (players, cameras). Empty = everything in range (tests, tools).</summary>
    public void SetViewers(ReadOnlySpan<Vector3> planetFramePositions);
    public bool TryGetConstructNode(EntityId id, [NotNullWhen(true)] out Node3D? node);
    public event Action<EntityId, ChunkPos>? ChunkMeshApplied;   // main thread, after mesh+collision swap
    public Task ShutdownAsync();                                  // idempotent; joins workers within ShutdownTimeout
    public int PendingJobs { get; }                               // HUD/tests
}
```

### Kontrakty konsumowane

| Potrzeba | Od | Typ |
| --- | --- | --- |
| Stan budowli, chunki, wersje, `ConstructChange` | K1-03 | `ConstructStore`, `Construct`, `ConstructChunk`, `AppliedOperation`, `OperationApplied` |
| Rejestr typów (pary `Id`–`Key`), blok zastępczy | K1-03 | `BlockTypeRegistry.Entries`, `MissingKey`, `ServerHello.BlockTypes` |
| Dopisywanie nowych kluczy treści do istniejącego świata | K1-05 | `WorldLoadOptions.BlockTypeKeys`, `WorldLoader.GrowRegistries` |
| Projekt Godota, composition root | K1-02B | `game/Horyzont.Game.csproj`, `game/src/Boot/` |
| ServerSimulation na wątku głównym Godota (więc `WorldStateConstructSource` czyta stan bez blokad) | K2-09 (decyzja) / K2-10 (model wątków) | — |
| Uprząż testów silnika bez grafiki w jobie `game` | K2-08 albo K2-09 (krok 2) | — |
| Wołanie `OnApplied`, `RebuildAll`, `SetViewers`, `ShutdownAsync` w trybach procesu | K2-13 krok 0 (w `GameComposition` K2-10, pozycje widzów z K2-11; D5) | — |

### Decyzje

1. **Orientacja to format w Core.** Indeks orientacji jest w każdym `BlockState` w dzienniku, checkpointach i protokole, więc jego znaczenie należy do chronionej ścieżki `World/` (lekcja 1: semantyka zależna od danych zapisanych jest formatem). Tabela to literał z testem złotym; zmiana tabeli = nowy format bloku (podbicie wersji ramek z `BlockState` i migracja), czyli w praktyce nigdy. Applier i skrót stanu nie zależą od orientacji, więc wersje operacji i protokołu zostają bez zmian.
2. **`BlockShape.Mesh = 2`.** Spec: sześciany z greedy meshingiem, inne kształty z gotowych siatek. Zamiast enumerować kształty (skos, płyta) jeden kształt `Mesh` i siatka z treści. `BlockShape` nie występuje w żadnym kodeku ani skrócie (sprawdzone `rg BlockShape src/Horyzont.Core/Serialization src/Horyzont.Core/Hashing` — pusto), więc bez podbijania wersji; wykonawca powtarza to sprawdzenie w kroku 3 i przy trafieniu zatrzymuje się. Test kontraktu dostaje `(BlockShape.Mesh, 2)`.
3. **Osobny projekt `Horyzont.Constructs`.** Mesher jest potrzebny serwerowi dedykowanemu (kolizje do fizyki graczy) i klientowi, więc nie trafia do `Horyzont.Client` (K2-13, warstwa klienta) ani do Core (format; każda poprawka meshera wymagałaby przeglądu formatu). Czysty .NET pozwala testować greedy meshing wyrocznią i testem alokacji bez silnika. Pytanie P1.
4. **Materiał bloku jest lokalny dla treści.** `BlockTypeDef.MaterialId` to numer z `BlockTypeCatalog.Materials` (1 … 255), budowany przy każdym starcie i nigdy niezapisywany ani niewysyłany. To nie jest `MaterialRegistry` (materiały terenu zapisane w świecie). W K2 materiał to kolor; jedna `Texture2DArray` (warstwa = `MaterialId − 1`, warstwa 0 dla `core:missing` ma szachownicę magenta-czerń) i jeden `ShaderMaterial` na cały świat budowli: jedna powierzchnia sześcianów i jedna kształtów `Mesh` na chunk, czyli najwyżej 2 wywołania rysowania na chunk.
5. **Typ nieznany treści.** Klucz zapisany w świecie, a nieobecny w katalogu (K1-03), rysuje się i koliduje jak `core:missing` (sześcian, kolizja). `BlockRenderTable.Build` liczy takie klucze, a wołający loguje je raz przy starcie (`[LoggerMessage]`, poziom Warning, lista kluczy). Numer typu spoza rejestru (dane z sieci w K3-15) też daje `core:missing`, bez wyjątku.
6. **Walidator i raycast Core zostają przy pełnym sześcianie.** `ConstructRaycast` i sprawdzenie kolizji z graczem traktują każdy niepusty blok jako sześcian. Kształt i flaga kolizji z treści wpływają tylko na siatkę i kolizję fizyczną. Uzależnienie walidatora od treści dałoby rozjazd po aktualizacji treści, a appliera — zmianę formatu operacji (ZASADY pkt 4, lekcja 1). Skutek: płytę wskazuje się i blokuje jak pełną komórkę. Pytanie P2.
7. **Greedy meshing dwa razy.** Render: maska 16×16 na warstwę i kierunek, klucz łączenia = materiał (sześciany są jednolite, więc orientacja nie zmienia wyglądu sześcianu w K2; Pytanie P7). Kolizja: osobny przebieg z kluczem = „pełny sześcian z kolizją”, więc łączy więcej i daje mniej trójkątów. Ściana sześcianu jest ukryta, gdy sąsiad (także w sąsiednim chunku, przez obwódkę 18³) ma `Occludes`. Kształty `Mesh` niczego nie zasłaniają i nie są zasłaniane; ich trójkąty idą obrócone `BlockOrientation.ToQuaternion` wokół środka komórki do obu wyjść (do kolizji tylko z `HasCollision`). UV: współrzędne komórek w płaszczyźnie ściany (powtarzanie tekstury), więc scalone prostokąty nie rozciągają tekstury.
8. **Wątki i migawki.** Wątki robocze nigdy nie czytają `WorldState`, `Construct` ani węzłów. Wątek główny kopiuje chunk z obwódką do `ChunkMeshInput` (18³ × 4 B ≈ 23 KB, kopia mikrosekundowa), wątek roboczy buduje `ChunkMeshData`, wątek główny przesyła wynik do Godota w budżecie `MaxApplyMillisecondsPerFrame` (co najmniej jeden wynik na klatkę, żeby kolejka nie stała). Wejścia i wyjścia są z puli o rozmiarze `MaxInFlightJobs`; mesher i bufory są per wątek. Każdy klucz chunku ma generację: nowa zmiana podbija generację, a wynik ze starszą generacją albo dla usuniętej budowli jest odrzucany i wraca do puli.
9. **Kolejki mają górne granice i termin (lekcja 6).** Zbiór brudnych chunków jest zbiorem (deduplikacja), ograniczonym liczbą chunków budowli w zasięgu; zadania w locie ≤ `MaxInFlightJobs`; wyniki ≤ zadania w locie. Usunięcie budowli albo wyjście poza zasięg usuwa jej brudne wpisy i unieważnia generacje. `ShutdownAsync`: zamyka kanał, anuluje, czeka na wątki najwyżej `ShutdownTimeout`; po terminie loguje błąd i odłącza wyniki (żaden wynik nie dotknie węzłów po zamknięciu). Kolejność zamykania dla K2-10: `ConstructWorld.ShutdownAsync` po `Shutdown` sesji, przed zwolnieniem drzewa sceny; nie zależy od kolejności terenu z K2-09.
10. **Priorytet.** Zadania wybiera się po (odległość budowli do najbliższego widza, `EntityId`, `ChunkPos`), czyli deterministycznie przy remisach; chunki zmienione operacją gracza wyprzedzają przebudowę po wczytaniu (`RebuildAll`), żeby edycja przy dużej budowli nie czekała na całą kolejkę.
11. **Tryby.** Solo i host: jedna instancja `ConstructWorld` w trybie `RenderAndCollision` nad `WorldStateConstructSource` serwera; jej kolizje służą fizyce serwera i nie są dublowane (spec, Architektura). Serwer dedykowany: `CollisionOnly`, bez `MeshInstance3D` i bez `BlockVisuals`. Zdalny klient w K2: brak źródła (budowle zdalne pojawiają się w K3-15, które poda `IConstructSource` lustra i woła `OnApplied`).
12. **Zasięg.** Siatka renderu dla budowli, których prostopadłościan (`BoundsMin/Max` w ramie planety) jest w `RenderRadiusMeters` od widza; kolizja w `CollisionRadiusMeters` od gracza (fizyka serwera i predykcja); wyładowanie po przekroczeniu zasięgu + `HysteresisMeters`. Bez widzów (testy, pusty serwer) — wszystko. Pytanie P6.
13. **Poza w Godocie.** `ConstructNode.Transform = new Transform3D(new Basis(rotation), origin)` z ramy planety; w podprojekcie 1 planeta domowa ma środek w początku świata Godota (K2-13 decyzja 10). Konwersja przez jedną funkcję (K2-11, jeśli istnieje; inaczej `ConstructNode.ToGodot`), żeby od K5 dało się podmienić ramy. `ChunkNode.Position = chunk * 16`.
14. **Warstwy fizyki.** `CollisionLayers.Constructs` (warstwa 2) dla `StaticBody3D` chunków, `CollisionMask = 0` (statyczne). Plik `CollisionLayers` (`Terrain = 1`, `Constructs = 2`, `ServerBodies = 3`, `ClientBodies = 4`, maski jako `const uint`) zakłada ten pakiet, bo jest scalany przed K2-09 i K2-11 (rozstrzygnięte na bramce K1, dawne P5). `ConcavePolygonShape3D.BackfaceCollision = false`.
15. **Treść z dysku jest walidowana jak dane wejściowe (lekcja 6).** `BlockTypeCatalog.Parse`: `formatVersion` = 1; klucz `^[a-z0-9_]+:[a-z0-9_/]+$`, unikalny, różny od `core:missing`; kształt `cube`|`mesh`; `mesh` wymagane dokładnie dla kształtu `mesh` i rozwiązywalne; materiał istniejący; ≤ 255 materiałów; liczba typów + 1 ≤ `ProtocolConstants.MaxBlockTypes`; siatka `ShapeMesh` z indeksami w zakresie, liczbą indeksów podzielną przez 3, skończonymi wartościami w [0,1]³ ± 0,001. Błąd → `InvalidDataException` ze ścieżką JSON; gra nie startuje (treść jest nasza, błąd jest błędem builda).

## Testy akceptacyjne (napisz najpierw)

| Test | Projekt | Sprawdza |
| --- | --- | --- |
| `BlockOrientationTests.Table_MatchesGoldenUpForward` | Core.Tests | 24 pary (Up, Forward) z tabeli wyżej, literałem w teście |
| `BlockOrientationTests.AllOrientations_AreDistinctProperRotations` | Core.Tests | macierz z `Right/Up/−Forward` całkowita, ortonormalna, wyznacznik +1; 24 różne |
| `BlockOrientationTests.Identity_IsZero_AndCountMatchesBlockState` | Core.Tests | `Identity = 0` daje (PosY, NegZ); `Count == BlockState.OrientationCount` |
| `BlockOrientationTests.FromUpForward_RoundTripsAll_AndRejectsParallel` | Core.Tests | dla 24 indeksów; 12 par równoległych/przeciwnych → `TryFromUpForward` false |
| `BlockOrientationTests.WithYaw_FourTurns_IsIdentity_KeepsUp` / `WithUp_KeepsYaw_ReachesAll24` | Core.Tests | kontrakt dla `OrientationPicker` K2-13 |
| `BlockOrientationTests.ToConstruct_ToBlock_AreInverse_AndMatchQuaternion` | Core.Tests | `ToQuaternion` obraca normalną ściany na `ToConstruct` (tolerancja 1e-6) |
| `ProtocolContractTests` (rozszerzony) | Core.Tests | `(BlockShape.Cube, 1)`, `(BlockShape.Mesh, 2)` |
| `BlockTypeCatalogTests.*` | Constructs.Tests | poprawny plik; każdy błąd z Decyzji 15 osobno → `InvalidDataException` ze ścieżką; `BlockTypeKeys` bez `core:missing` w kolejności treści; plik `game/content/blocks/blocks.json` z repozytorium parsuje się (test czyta go z dysku) |
| `BlockRenderTableTests.*` | Constructs.Tests | id 0 → pusty; klucz rejestru nieznany treści → `IsMissing`; id spoza rejestru → `IsMissing`; `MissingKeyCount` |
| `ChunkMesherTests.SingleCube_SixQuads` / `SolidChunk_SixQuads` / `TwoCubes_CullSharedFace` | Constructs.Tests | liczby czworokątów i trójkątów kolizji |
| `ChunkMesherTests.Solid64Construct_96RenderQuads` | Constructs.Tests | budowla 64³ (4³ chunki): ściany wewnętrzne między chunkami ukryte przez obwódkę; razem 6 × 16 czworokątów |
| `ChunkMesherTests.MeshShape_EmitsOrientedPrefab_DoesNotOcclude` | Constructs.Tests | płyta w 24 orientacjach = `ToQuaternion` wokół środka komórki; sąsiedni sześcian zachowuje ścianę |
| `ChunkMesherTests.NoCollisionType_RenderedButNotInCollision` | Constructs.Tests | |
| `ChunkMesherPropertyTests.GreedyCoversExactlyExposedFaces` (FsCheck) | Constructs.Tests | losowe chunki z obwódką: rasteryzacja czworokątów renderu i kolizji do ścian jednostkowych = zbiór z `NaiveFaceOracle`; brak nakładania |
| `ChunkMesherPropertyTests.ArbitraryUints_NeverThrow` (FsCheck) | Constructs.Tests | dowolne `uint` w komórkach (orientacja ≥ 24, nieznany typ, bity 28–31) → bez wyjątku; źle uformowany blok rysowany jak `core:missing` w orientacji 0 |
| `ChunkMesherTests.SameInput_IdenticalOutput` | Constructs.Tests | determinizm wyjścia (powtarzalność testów i porównań) |
| `ChunkMesherAllocationTests.SteadyState_DoesNotAllocate` | Constructs.Tests | po rozgrzaniu `Build` + `Fill` dla losowego chunku: `GC.GetAllocatedBytesForCurrentThread` przyrost 0 (ZASADY pkt 6) |
| `ChunkRemeshQueueTests.*` | Constructs.Tests | deduplikacja; limit w locie; stara generacja odrzucona; usunięcie budowli czyści wpisy i odrzuca wyniki; priorytet edycji przed `RebuildAll`; remisy po (`EntityId`, `ChunkPos`) |
| `ConstructDirtyChunksTests.*` | Constructs.Tests | `BlockSet` w środku → 1 chunk; na ścianie/krawędzi/narożniku chunku → 2/3/4 chunki; `ConstructCreate` → `created`; `BlockClear` ostatniego bloku (`Removed = true`) → `removed`; `EntityReparent` budowli → przebudowa pozy bez siatek; operacje terenu i graczy → nic |
| `WorldStateConstructSourceTests.*` | Constructs.Tests | odczyt przez `WorldScript` po `ConstructCreate`/`BlockSet`/`BlockClear`; kolejność `CollectConstructs`/`CollectChunks` |
| `MesherTimingTests.Mesh64Patterns_ReportsTimes` `[Slow]` | Constructs.Tests | wypisuje czasy chunku (mediana, max) dla Solid, Building, Random35, Checkerboard; bramka tylko luźna: cała budowla Solid ≤ 2 s na jednym wątku |
| `ConstructWorldTests.CreateSetClear_MeshAndCollisionMatchMesher` | game (silnik) | po `OnApplied` i opróżnieniu kolejki: liczba powierzchni i trójkątów kolizji = wyjście `ChunkMesher` dla tych samych danych; `ChunkMeshApplied` dla każdego brudnego chunku |
| `ConstructWorldTests.CollisionOnly_NoMeshInstances` | game (silnik) | tryb serwera dedykowanego |
| `ConstructWorldTests.PhysicsRay_HitsSameCellAsConstructRaycast` | game (silnik) | 200 losowych promieni (ziarno stałe) na budowli Building: `PhysicsDirectSpaceState3D.IntersectRay` na warstwie `Constructs` i `ConstructRaycast` z Core trafiają tę samą komórkę i ścianę (tylko sześciany) |
| `ConstructWorldTests.RemovedDuringMeshing_DropsResult` / `Shutdown_JoinsWorkers_NoLateCallbacks` | game (silnik) | Decyzje 8–9 |
| `ConstructHitchTests.Construct64_LoadIn_MaxFrame50ms` `[Slow]` | game (silnik, CI Windows i Linux) | wzory Solid, Building, Random35: od `RebuildAll` do opróżnienia kolejki + 60 klatek; max odstęp między klatkami (`Time.GetTicksUsec` w `_Process`) ≤ 50 ms; zapis p50/p99/max do wyjścia |
| `ConstructHitchTests.Construct64_EditStream_MaxFrame50ms_RemeshP99Under100ms` `[Slow]` | game (silnik) | 10 s po 20 `BlockSet`/`BlockClear` na sekundę przez `ServerSimulation.ApplyServerOperation` (stałe ziarno) na Building: max klatka ≤ 50 ms; od `OnApplied` do `ChunkMeshApplied` p99 ≤ 100 ms (Pytanie P4) |
| `ConstructHitchManual` `[Manual]` | game z GPU (okno) | te same scenariusze z prawdziwym renderowaniem i Checkerboard; wynik (CPU, GPU, p50/p99/max) w opisie PR |

Fikstury `ConstructFixtures` (w `tests/Horyzont.Testing`, przez publiczny `WorldStateBuilder`, żeby wczytanie szło tą samą drogą co checkpoint): Solid (64³ pełne), Building (skorupa 64³ ze stropami co 4 m, oknami 2×2 co 6 m, ścianami działowymi; mieszane typy, w tym płyty i skosy), Random35 (35% wypełnienia, ziarno 12), Checkerboard (szachownica 3D — przypadek najgorszy, tylko raport). Wszystkie testy z czasem rzeczywistym i silnikiem w kolekcji xUnit `[CollectionDefinition(DisableParallelization = true)]` albo w osobnym przebiegu runnera silnika (lekcja 4).

## Kroki

- [x] 0. Gałąź `pakiet/k2-12-budowle` od `main`; sprawdź, czy K1-02B jest scalony (`game/Horyzont.Game.csproj`); jeśli nie, zatrzymaj się. Sprawdź, czy K2-08A jest scalony (`editor-linux` w `engine/engine.json`, zielony `engine.yml` z artefaktem `engine-linux-editor`); jeśli nie, zatrzymaj się (D6). Sprawdź, czy na `main` jest uprząż testów silnika (K2-08/K2-09) i jakie warstwy fizyki ustalono; wpisz wynik do Odstępstw.
- [x] 1. `src/Horyzont.Constructs` i `tests/Horyzont.Constructs.Tests` w `Horyzont.sln`; `LayeringTests`: nowy projekt na liście bez Godota z dozwoloną referencją `Horyzont.Core`; `game/Horyzont.Game.csproj` dostaje referencję. `dotnet build` zielony.
- [x] 2. Uprząż silnika: jeśli jest na `main`, użyj jej. Jeśli nie — dodaj ją tak, jak opisuje K2-09 krok 3 (edytor Linux z K2-08A; gdUnit4Net w `Directory.Packages.props`, opis w PR i zielony test dymny silnika, ZASADY pkt 3; job `game` na Windows i Linux); pakiet scalany jako drugi przechodzi na uprząż pierwszego przy rebase (Pytanie P8).
- [x] 3. Testy Core (czerwone): `BlockOrientationTests`, rozszerzenie `ProtocolContractTests`. Potem `BlockOrientation` i `BlockShape.Mesh = 2`. Powtórz `rg BlockShape src/Horyzont.Core/Serialization src/Horyzont.Core/Hashing` (oczekiwane: pusto; inaczej stop i pytanie). Zielone.
- [x] 4. `BlockTypeCatalogTests` → `BlockTypeCatalog`, `BlockTypeContent`, `BlockMaterialDef`, `ShapeMesh`, `IShapeMeshResolver`, `BuiltInShapeMeshes`; `game/content/blocks/blocks.json` z treścią K2 i `game/locale/blocks.csv`. Zielone.
- [x] 5. `BlockRenderTableTests` → `BlockRenderInfo`, `BlockRenderTable`. Zielone.
- [x] 6. `NaiveFaceOracle`, testy meshera (przykłady, właściwości, alokacje) → `ChunkMeshInput`, `ChunkMeshData`, `ChunkMesher` (render i kolizja). Zielone, w tym test alokacji.
- [x] 7. `ConstructDirtyChunksTests`, `WorldStateConstructSourceTests`, `ChunkRemeshQueueTests` → `Sources/*`, `Scheduling/*`. Zielone.
- [x] 8. `ConstructFixtures` i `MesherTimingTests` `[Slow]`; wpisz czasy do opisu PR.
- [x] 9. Gra: `CollisionLayers` i nazwy warstw w `project.godot`; `BlockContentLoader`, `GodotShapeMeshResolver`, `BlockVisuals` + `blocks.gdshader`, `ChunkMeshUploader`, `MeshWorkerPool`, `ConstructChunkNode`, `ConstructNode`, `ConstructWorld`, `BlockMeshFactory`. Logowanie wyłącznie `[LoggerMessage]`.
- [x] 10. Testy silnika `ConstructWorldTests` (najpierw czerwone na pustych metodach), potem implementacja do zielonego.
- [x] 11. (lokalnie Windows: 5 przebiegów z rzędu na spokojnej maszynie zaliczonych, patrz Odstępstwa; potwierdzenie na CI Windows i Linux: czeka) `ConstructHitchTests` `[Slow]` na Windows i Linux w CI, wymagany krok bez `continue-on-error`. Przy przekroczeniu: profiler Godota, zmniejszenie budżetu, podział przesyłania kolizji na osobne klatki od siatki, ewentualnie `RenderingServer.MeshAddSurface` z buforem bajtów (Pułapki); bez podnoszenia progu.
- [x] 12. Pomiar `[Manual]` z GPU (w tym Checkerboard) na maszynie właściciela; wynik w PR.
- [x] 13. Scena deweloperska `game/scenes/dev/ConstructsDemo.tscn` (budowla Building nad płaszczyzną, swobodna kamera z buildu deweloperskiego, jeśli jest; inaczej stała kamera) do oceny wzrokowej; zrzut ekranu w PR.
- [x] 14. `docs/ZASADY.md`: pkt 3 — dopisz `BlockOrientation` do listy typów zapisywanych w `World/` z adnotacją „tabela orientacji jest formatem `BlockState`”; pkt 2 — „wątki robocze (meshing, strumień terenu) czytają wyłącznie migawki skopiowane na wątku głównym”.
- [ ] 15. Pełne `dotnet build Horyzont.sln`, `dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"`, `dotnet format Horyzont.sln --verify-no-changes`, `python tools/engine/build.py smoke`; PR z wynikami testów akceptacyjnych, czasami z kroków 8, 11, 12, Odstępstwami i pytaniami; przegląd Opus 5.5.
- [ ] 16. Po scaleniu: CI na `main` zielone (lekcja 4); kroki nieodhaczone tylko z dopiskiem „odroczone do K…” (lekcja 5).

## Odbiór

- [ ] Wszystkie testy akceptacyjne z tabeli przechodzą w CI (Windows i Linux), w tym `[Slow]` przycięć 64³; wynik `[Manual]` z GPU w PR, max klatka ≤ 50 ms dla Solid, Building i Random35.
- [ ] Tabela orientacji w Core zgodna z tabelą tego planu; przegląd Opus 5.5 zatwierdził ją jako format.
- [ ] Brak nowych kodów operacji, intencji, komunikatów, odmów i blobów; brak zmian w `Operations/`, `Intents/`, `OperationApplier.cs`, `Serialization/`, `Protocol/`, `Hashing/`, `Checkpoints/` (diff to potwierdza). Pakiet nie dodaje stanu zapisywanego, więc lekcja 2 (skrót + test wrażliwości + live = replay = checkpoint+tail) nie ma zastosowania; gdyby wykonawca musiał dodać stan zapisywany, zatrzymuje się i pyta.
- [ ] Mesher nie alokuje w stanie ustalonym (test); kolejki ograniczone i czyszczone przy usunięciu budowli i zamknięciu (testy).
- [ ] K2-13 może wypełnić kolumnę „Rzeczywisty typ” w swoim kroku 0: źródło budowli (`IConstructSource`/`WorldStateConstructSource`), odświeżenie (`ConstructWorld.OnApplied` + `ChunkMeshApplied`), orientacje (`BlockOrientation`), definicje (`BlockTypeCatalog`, `BlockMeshFactory`, `BlockVisuals`).
- [ ] Definicja ukończenia z ZASADY pkt 11.

## Pułapki

- **Skrętność ścian w Godocie.** Przednia ściana trójkąta jest zgodna z ruchem wskazówek zegara (patrząc na nią). Odwrócona kolejność daje niewidoczne ściany przy domyślnym `CULL_BACK` i — przy `BackfaceCollision = false` — przenikanie przez kolizję od zewnątrz. Test `PhysicsRay_HitsSameCellAsConstructRaycast` łapie błąd kolizji; render sprawdza scena z kroku 13.
- **Obwódka chunku.** Bez niej każda ściana na granicy chunków jest rysowana podwójnie (z-fighting) i kolizja ma wewnętrzne ściany, o które zahacza kapsuła. Kopiuj tylko sześć płatów ścian (krawędzie i narożniki niepotrzebne bez AO), w kolejności `CellPos.LocalIndex` (X<<8 | Y<<4 | Z), nie X-Y-Z „po ludzku”.
- **Zmiana na granicy chunku brudzi sąsiada.** `BlockClear` na ścianie chunku odsłania ścianę w chunku obok; zapomniany sąsiad = dziura w siatce i kolizji.
- **`Construct.Chunks` alokuje** posortowaną tablicę przy każdym wyliczeniu — nie używać co klatkę.
- **Wątki robocze a stan.** Żadnego czytania `WorldState`/`Construct`/węzłów z wątku roboczego, żadnych pól statycznych na bufory (ZASADY pkt 2: zero mutowalnego stanu statycznego); `ArrayPool<T>.Shared` jest dozwolony. Wynik przesyła tylko wątek główny.
- **Przesyłanie do Godota alokuje.** `ArrayMesh.AddSurfaceFromArrays` i `ConcavePolygonShape3D.Data` wymagają tablic zarządzanych o dokładnej długości; to koszt per zmiana, nie w stanie ustalonym (bez edycji brak przesyłania), więc nie łamie ZASADY pkt 6 — opisz w Odstępstwach. Jeśli przesyłanie przekroczy budżet: jedna tablica bajtów przez `RenderingServer.MeshAddSurface` (format wierzchołków ręcznie) albo osobne klatki dla siatki i kolizji.
- **Koszt `ConcavePolygonShape3D`.** Budowa struktury przyspieszającej (BVH) idzie na wątku głównym przy ustawieniu danych/ciała; Checkerboard to ~24 tys. trójkątów na chunk. Mierz w teście przycięć; nie twórz kształtów z wątków roboczych przez `PhysicsServer3D` (nie jest bezpieczny wątkowo w domyślnej konfiguracji). Zapisz, który silnik fizyki jest aktywny (`ProjectSettings` `physics/3d/physics_engine`), w wyjściu testu.
- **Headless nie mierzy GPU.** Serwer renderowania w trybie bez grafiki jest atrapą: CI mierzy koszt CPU (meshing, przesyłanie danych, kolizje), a prawdziwe przesyłanie do GPU tylko pomiar `[Manual]`. Nie ogłaszaj spełnienia kryterium bez pomiaru z GPU.
- **Pomiar czasu tylko w `game/`.** `Stopwatch`/`Time.GetTicksUsec` są dozwolone w grze i testach silnika; w `Horyzont.Constructs` budżet czasu przekazuje wołający (bez zegara w kodzie czystym, łatwiejsze testy kolejki).
- **Materiał bloku ≠ materiał terenu.** `BlockTypeDef.MaterialId` nie jest numerem z `MaterialRegistry`; pomylenie da poprawnie wyglądające testy i złe kolory po dodaniu materiału terenu.
- **`core:missing` w treści.** Jest wbudowany i dopisywany przez `WorldState.CreateNew`; nie dodawaj go do `BlockTypeKeys` ani do `blocks.json` (katalog odrzuca), bo zmieniłoby to numerację nowych światów względem testów (`WorldFactory`).
- **Kolejność kluczy w `blocks.json` to numeracja nowych światów.** Nowe typy dopisuj na końcu; przestawienie nie psuje starych światów (mapa klucz → numer w zapisie), ale zmienia numery w nowych i rozjeżdża fikstury testów.
- **Precyzja.** Wierzchołki względem początku chunku (0 … 16 m), poza budowli w podwójnej precyzji Godota; nie licz wierzchołków w ramie planety (4 km × float = utrata milimetrów i drgania).
- **Sześcian w Core vs kształt w fizyce** (Decyzja 6): duch i podpowiedź kolizji z graczem w K2-13 muszą używać pełnej komórki, nie siatki kształtu.

## Pytania do właściciela

- **P1. Projekt `Horyzont.Constructs` zamiast kodu w `Horyzont.Client` (K2-13) albo w Core.** Domyślnie: osobny projekt czysty .NET, bo kolizji budowli potrzebuje serwer dedykowany. `Horyzont.Client` zakłada K2-10 (K2-11 i K2-13 dokładają katalogi) i może z niego korzystać.
- **P2. Kształty i bloki bez kolizji a walidacja.** Core (raycast, kolizja z graczem) traktuje każdy blok jako pełny sześcian; płyty i skosy fizycznie są mniejsze, a bloki bez kolizji i tak blokują stawianie w graczu. Domyślnie: zostaje tak w podprojekcie 1; zmiana to nowa wersja formatu intencji/operacji w osobnym pakiecie.
- **P3. Definicja „budowla 64³ bez przycięć > 50 ms”.** Domyślnie: wzory Solid, Building, Random35, scenariusze „pojawienie się” i „10 s edycji po 20/s”; bramka CI na wolnym przebiegu bez grafiki plus pomiar ręczny z GPU w PR; Checkerboard (najgorszy przypadek, 3,1 mln wierzchołków) tylko raport.
- **P4. Opóźnienie odświeżenia własnej edycji.** Rozstrzygnięte na bramce K1: p99 ≤ 100 ms od zastosowania operacji do podmiany siatki w trybie solo/host to kontrakt; K2-13 czeka na `ChunkMeshApplied` z tym terminem (D14).
- **P5. Numery warstw fizyki.** Rozstrzygnięte na bramce K1: `game/src/Shared/Physics/CollisionLayers.cs` z `Terrain = 1`, `Constructs = 2`, `ServerBodies = 3`, `ClientBodies = 4` (nazwy z K2-11), zakłada K2-12.
- **P6. Zasięg siatek i kolizji.** Domyślnie: render 1000 m od widza, kolizja 128 m od gracza, histereza 64 m; bez LOD budowli w podprojekcie 1.
- **P7. Wygląd sześcianu niezależny od orientacji w K2.** Jeden materiał na typ; tekstury per ściana (np. pień drzewa, strzałka) dopiero z kierunkiem artystycznym K8-50 — wtedy klucz łączenia w greedy dostanie materiał ściany po `ToBlock`. Domyślnie: tak.
- **P8. Uprząż testów silnika.** Jeśli K2-12 rusza przed K2-08/K2-09, dodaje gdUnit4Net tak, jak opisał K2-09 krok 3; drugi pakiet przechodzi na tę uprząż. Domyślnie: tak, bez osobnego pakietu.
- **P9. Materiały wizualne bloków poza zapisem świata.** Domyślnie: katalog materiałów bloków jest treścią (bez zapisu i bez wysyłania w `ServerHello`); różnica treści między klientem a serwerem zmienia tylko wygląd, nie stan.

## Odstępstwa

- **Wykonawca.** Pakiet realizuje Claude Sonnet 5.5 zamiast GPT-6.1 Sol (decyzja właściciela o oszczędzaniu limitu Codexa); przegląd Opus 5.5 pozostaje (ZASADY pkt 9 i 3). Commity niosą trailer `Co-Authored-By: Claude Opus 5.5` zgodnie z poleceniem zlecającego.
- **Krok 0.** Gałąź `pakiet/k2-12-budowle` jest od `pakiet/k2-08a-silnik-linux` (= `main` + K2-08A, PR #10 jeszcze niescalony), nie od `main`. K1-02B jest scalony (`game/Horyzont.Game.csproj` istnieje). Uprzęży testów silnika (K2-08/K2-09) nie ma na `main` ani w bazie. `game/project.godot` nie ustawia nazw warstw fizyki ani silnika fizyki; warstwy dodaje ten pakiet (P5).
- **Uprząż testów silnika (krok 2, P8).** K2-08/K2-09 nie są na `main`, a gdUnit4Net nie był sprawdzany z naszym silnikiem, więc zastosowano wariant zapasowy z K2-09 krok 3: własny runner `-- --engine-tests [--filter=tekst] [--slow] [--manual]` (`game/src/Testing/`: `EngineTestRunner`, `EngineTestBase`, `EngineTestAttribute`, `EngineAssert`; kod wyjścia 0 tylko gdy wszystkie testy przeszły i było ich co najmniej jeden), wołany z `Boot.cs` (jedna gałąź `if`). Testy leżą w `game/tests/Constructs/`; kompilują się tylko w konfiguracji `Debug` (`Compile Remove="tests/**"` poza nią), bo używają `tests/Horyzont.Testing` (fikstury `ConstructFixtures`), do którego `Horyzont.Game.csproj` ma w `Debug` referencję projektową. Pakiet scalany jako drugi (K2-09 albo ten) przechodzi na uprząż pierwszego przy rebase. Job `game` w `ci.yml` jest teraz macierzą Windows + Ubuntu 24.04 (artefakt `engine-linux-editor` z K2-08A) i uruchamia `--engine-tests --slow`; Linux i job w ogóle są **niezweryfikowane lokalnie (pending-CI)**, w szczególności lista bibliotek systemowych instalowanych przed uruchomieniem edytora Linux.
- **Core (krok 3).** Zmieniono tylko `BlockOrientation.cs` (nowy) i `BlockShape.cs` (`Mesh = 2`). `BlockShape` nie występuje w `Serialization/` ani `Hashing/` (sprawdzone, pusto), więc bez podbijania wersji. `git diff pakiet/k2-08a-silnik-linux..HEAD -- src/Horyzont.Core src/Horyzont.Persistence src/Horyzont.Net` obejmuje wyłącznie te dwa pliki. Dodatkowy test `WithYaw_QuarterTurn_IsCounterClockwiseAboutUp` potwierdza regułę „prawej ręki” z komentarza formatu.
- **Kontrakty `Horyzont.Constructs` różniące się od szkicu w planie.**
  - `BlockTypeCatalog.Materials` zawiera wbudowany materiał `core:missing` jako `Id = 1` (warstwa 0 tekstury, szachownica magenta-czerń), materiały z treści mają `Id` od 2 (stała `BlockTypeCatalog.MissingMaterialId`). W planie „Id od 1 w kolejności treści” kłóciło się z „warstwa 0 dla core:missing”; wybrano wariant spójny z Decyzją 4. `BlockTypeKeys` i numeracja nowych światów nie zależą od tego.
  - `ShapeMesh` nie ma parametru `occludesNothing` (kształty `Mesh` nigdy nie zasłaniają, parametr nic by nie robił); dodano `Validate()` (Decyzja 15).
  - `ChunkMeshData` wystawia dwie powierzchnie `Cubes` i `Shapes` (`MeshSurface`: `Positions`, `Normals`, `Uvs`, `Uv2`, `Indices`) zamiast płaskich tablic z szkicu, żeby chunk miał najwyżej dwa wywołania rysowania (Decyzja 4); `CollisionFaces` bez zmian. Tablice z puli `ArrayPool<T>.Shared`, bufory nie kurczą się; `ChunkMeshData` i `MeshSurface` są `IDisposable`.
  - `BlockRenderTable` ma dodatkowo `Missing` (info dla źle uformowanych komórek) i `UnknownKeys` (lista do logu, `MissingKeyCount` = jej długość; `core:missing` z rejestru nie liczy się jako nieznany).
  - Komórka źle uformowana (`!BlockState.IsWellFormed`: orientacja ≥ 24, bity 28–31, typ 0 z bitami) jest rysowana i koliduje jak `core:missing` w orientacji 0 (test `ArbitraryUints_NeverThrow` i `MalformedBlock_IsDrawnAsMissingCube`).
  - `ConstructDirtyChunks.Collect` ma opcjonalny ostatni parametr `reposed` (encje z `EntityReparentOp`: odświeżenie pozy bez pracy nad siatkami); `ConstructCreate` zwraca tylko `created` (świat sam buduje wszystkie chunki nowej budowli); `BlockSet`/`BlockClear` ostatniego bloku zwraca `removed` bez brudnych chunków.
  - `WorldStateConstructSource.CollectChunks` woła `Construct.Chunks` (alokuje posortowaną tablicę); `ConstructWorld` używa go tylko przy wejściu budowli w zasięg i w `RebuildAll`, nie co klatkę. Core nie zmieniono (poza zakresem).
  - `ChunkRemeshQueue` (bez zegara i wątków): `MarkDirty(key, RemeshPriority)`, `TryStartNext(out RemeshJob)`, `Complete(job) → bool` (czy wynik aktualny), `RemoveConstruct`, `Clear`, właściwość `Distance` (delegat `ConstructDistance`). Ten sam chunk nie jest uruchamiany dwa razy naraz (wynik trwającego zadania i tak byłby nieaktualny). Typ ma `[SuppressMessage]` dla CA1711 (nazwa narzucona planem).
  - Interfejs `IConstructSource` i `ConstructPose` bez zmian względem planu; `ChunkMeshInput` ma dodatkowo `Reset`, `Index`, `StrideX/Y` (testy i fabryka bloku).
- **Gra, różnice względem szkicu.**
  - `CollisionLayers` w przestrzeni nazw `Horyzont.Game.Physics` (plik zgodnie z planem w `game/src/Shared/Physics/`), bo CA1716 zabrania przestrzeni nazw `Shared`. Warstwy mają nazwy w `project.godot` (`layer_names`). Silnik fizyki: `ProjectSettings` `physics/3d/physics_engine` = `DEFAULT` (wypisywane na początku uruchomienia testów silnika).
  - `ConstructWorld.SetViewers` przyjmuje `ReadOnlySpan<System.Numerics.Vector3>` (ramę planety w Core opisuje `System.Numerics`); dodano `TryGetConstruct` (typowany węzeł), `IsShutdown`, `LoadedConstructCount`, `AliveWorkerThreads` i `Pump(double)` (jedna porcja pracy klatki, wołana też z `_Process`; testy napędzają świat bez drzewa).
  - `BlockMeshFactory` jest klasą z konstruktorem `(BlockRenderTable)` i metodą `CreateBlockMesh(ushort, byte)`; siatka bloku ma współrzędne 0 … 1 względem rogu komórki (duch K2-13 ustawia ją sam).
  - **Przesyłanie do Godota (Pułapki).** `CreateMesh` i `CopyCollisionPoints` alokują tablice zarządzane o dokładnej długości na każdą zmianę chunku (nie w stanie ustalonym); mesher i bufory pozostają bez alokacji. Zmierzone koszty kroków (Debug, wyjście testów): Random35 ~7,7 tys. trójkątów kolizji/chunk: siatka 3–4 ms, budowa kształtu 8–12 ms, przypisanie do ciała 3–5 ms; Checkerboard ~24,6 tys. trójkątów: siatka 9 ms, kształt 67 ms, przypisanie 20 ms.
  - **Dwa kroki na chunk (zastosowane zalecenie z kroku 11).** Siatka trafia do Godota w jednym kroku, kształt kolizji (dominujący koszt) w osobnym, zwykle następnej klatki; `ConstructWorld.ApplyResults` wykonuje takie kroki do wyczerpania budżetu (`MaxApplyMillisecondsPerFrame`), ale co najmniej jeden na klatkę, kształty przed nowymi siatkami. `ChunkMeshApplied` jest zgłaszany, gdy oba są na miejscu; `PendingJobs` obejmuje też oczekujące kształty.
  - Przy wyjściu poza zasięg kolizji ciała chunków są usuwane, a po powrocie budowla jest przebudowywana (dane kolizji nie są trzymane); render i kolizja mają osobną histerezę (`HysteresisMeters`). Zasięg liczony od sfery otaczającej `BoundsMin/Max` budowli (rama planety, `ConstructNode.ToGodot` to jedyne miejsce konwersji).
  - Dodano test `Viewers_LoadRenderAndCollisionByRange_WithHysteresis` (Decyzja 12 nie miała testu w tabeli) oraz `ConstructFixtures.Create(pattern, cubesOnly)`: test promieni używa Building bez płyt i skosów, bo Core traktuje każdy blok jako sześcian (Decyzja 6).
  - Treść: `blocks.json` z pięcioma typami K2 i kolorami (kamień, drewno, metal); `blocks.csv` ma też wiersz `BLOCK_MISSING`. Godot wygenerował `game/locale/blocks.csv.import` (zatwierdzony; tłumaczenia nie są jeszcze zarejestrowane w `project.godot`, to zadanie K8/UI).
- **Przyczyna przycięć 64³ usunięta; bramka w CI znów wymagana (poprawka po przeglądzie, zastępuje wpis „raportująca”).** Pomiar (`ApplyTimings`: czas przesyłania siatki i budowy kształtów kolizji na wątku głównym; wypisywany przez testy `[Slow]`): dominował koszt `ConcavePolygonShape3D` w domyślnym silniku fizyki (GodotPhysics3D): ok. 15–25 ms na kształt 7,8 tys. trójkątów (Random35, 64 chunki, suma ok. 1 s), a nie przesyłanie siatki (suma ok. 130–220 ms, max 7,5 ms) ani GC (gen2 ok. 9, pauzy łącznie 5–8 ms na spokojnej maszynie). Jedna jednostka kolizji na klatkę plus reszta klatki dawały 80–100 ms. Naprawa: (1) `project.godot` ma `physics/3d/physics_engine="Jolt Physics"` (silnik zakładany w specyfikacji, `02-kosmos.md`); kształt kolizji kosztuje tam ok. 4–18 ms; (2) tablice powierzchni i punkty kolizji kopiują wątki robocze (`ChunkMeshUploader.Prepare`), wątek główny woła tylko serwery; kształtów nie tworzymy na wątkach roboczych (Pułapki: serwery fizyki nie są bezpieczne wątkowo, a zresztą budowa jest leniwa i i tak kończy się na wątku głównym — zmierzone: konstruktor kształtu 0,0 ms). Budżet czasu (4 ms) był już liczony zegarem. `ci.yml` ma znów jeden wymagany krok `--engine-tests --slow` bez `continue-on-error`. Niezweryfikowane: CI Windows i Linux (Jolt na Linuxie, wall-clock na runnerze).
- **Bramka przycięć (kroki 11 i 12): pojedynczy przebieg.** Best-of-3 usunięte po naprawie przyczyny (poprzedni wpis): `Construct64_LoadIn_MaxFrame50ms` i `Construct64_EditStream_…` biegną raz, progi 50 ms i 100 ms bez zmian. Wynik lokalny, 5 przebiegów z rzędu po ostatnim buildzie na spokojnej maszynie (load < 35% przed każdym): max klatka LoadIn Solid 16,1–16,9; Building 16,3–19,4; Random35 18,3 / 23,9 / 34,6 / 30,6 / 41,1; EditStream 24,4 / 22,1 / 18,6 / 13,7 / 16,0 ms; Checkerboard (tylko raport) 42–77 ms. Zapas dla Random35 jest cienki (max 41 ms przy limicie 50); jeśli CI okaże się niestabilne, następny krok to podział przesyłania na mniejsze chunki albo kolizja budowana tylko w zasięgu graczy.
- **Wyniki pomiarów (krok 8, 11, 12).** Maszyna: Windows 11, AMD Radeon RX 9070 XT (Vulkan 1.4, Forward+), silnik z kroku 0 w tym worktree.
  - Mesher, jeden wątek, Debug, `MesherTimingTests` (mediana / max chunku w ms, 64 chunki): Solid 0,37 / 0,52; Building 0,56 / 0,87; Random35 4,06 / 7,11; Checkerboard 8,57 / 12,85. Cała budowla Solid ≈ 25 ms.
  - Headless (`--engine-tests --slow`, CPU, atrapa renderera), przykładowe przebiegi: load-in Solid max 7–19 ms, Building 11–26 ms, Random35 21–66 ms (przed rozdzieleniem kroków siatka/kolizja do 275 ms), Checkerboard 60–248 ms (tylko raport); strumień edycji (199 edycji w 10 s): max klatka 12–46 ms w dobrych przebiegach, p99 opóźnienia `OnApplied` → `ChunkMeshApplied` 10–40 ms. Przebiegi „złe” (obciążona maszyna) dawały pojedyncze klatki 70–630 ms, stąd best-of-3.
  - **Pomiar ręczny z GPU (`ConstructHitchManual`, okno 1280×720, po rozgrzewce shaderów).** Dobry przebieg po rozdzieleniu kroków: Solid max 4,4 ms; Building 8,6 ms; Random35 p50 7,3 / p99 20,0 / max 21,7 ms; Checkerboard max 95 ms (raport); strumień edycji: klatki p50 4,2 / p99 6,8 / max 10,1 ms, opóźnienie remesh p99 10,4 ms. Inne przebiegi tej samej sesji: Random35 max 32–62 ms, strumień edycji max do 184 ms (pojedyncze skoki systemu). Pierwsza klatka z shaderem bloków kosztuje 44–81 ms (kompilacja potoków) i jest płacona przed pomiarem; to koszt startu spoza tego pakietu (rozgrzewka potoków: K8). Kryterium „max klatka ≤ 50 ms dla Solid, Building i Random35” spełnione w dobrych przebiegach, **nie we wszystkich**; werdykt należy do przeglądu na podstawie CI i ponownego pomiaru na maszynie bez obciążenia.
  - Zrzut ekranu sceny `ConstructsDemo.tscn` (Building, widać ściany, stropy, płyty i skosy przy oknach, prawidłowa skrętność ścian) wykonany lokalnie (`--screenshot=`); plik nie jest w repozytorium, do dołączenia do PR.
- **Weryfikacja końcowa (krok 15, lokalnie).** `dotnet build Horyzont.sln`: 0 ostrzeżeń, 0 błędów. `dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"`: Architecture 8, Integration 1, Net 100, Constructs 83, Persistence 86, Core 906, razem 1184 zaliczonych, 0 niezaliczonych. `dotnet format Horyzont.sln --verify-no-changes` i `dotnet format game/Horyzont.Game.csproj --verify-no-changes --no-restore`: bez zmian. `python tools/engine/build.py smoke`: SMOKE OK. Testy silnika Windows lokalnie: 6 testów `ConstructWorldTests` zaliczone w każdym przebiegu; 2 testy `[Slow]` przycięć przeszły w kilku przebiegach z rzędu (część po 2–3 próbach), ale w ostatnim pełnym przebiegu na mocno obciążonej maszynie `Construct64_LoadIn_MaxFrame50ms` nie zmieścił się w 3 próbach (Building 63/73 ms, Random35 128/86/80 ms; w tym samym czasie `GC.GetTotalPauseDuration` dochodziło do 143 ms na load-in Random35, a p50 klatki było 2 razy wyższe niż w przebiegach z rana). **Bramka przycięć nie jest więc potwierdzona jako stabilna**; wymaga CI i pomiaru na spokojnej maszynie. Podejrzewany udział GC: każdy chunk Random35 to ok. 2,3 MB tablic zarządzanych (Godot w trybie double), czyli ciągłe alokacje w LOH i kolekcje gen2; próba `DOTNET_GCLOHThreshold=0x800000` nie dała jednoznacznej poprawy na tej maszynie. Do rozważenia po CI: mniej alokacji przy przesyłaniu (`RenderingServer.MeshAddSurface` z jednym buforem bajtów, jak podpowiada plan) albo `GCLatencyMode.SustainedLowLatency` w composition root. Niezweryfikowane: CI (job `game` Windows i Linux), przegląd Opus 5.5, PR. Aktualizacja po naprawie przyczyny przycięć: `dotnet build Horyzont.sln` 0/0; `dotnet test` 1187 zaliczonych (Architecture 8, Integration 1, Net 100, Persistence 86, Constructs 86, Core 906), 0 niezaliczonych; `dotnet format` bez zmian; testy silnika (bez `--slow`) 7 zaliczonych, z `--slow` 9 zaliczonych w 5 z 5 przebiegów; dodany test `TwoParentFrames_OnlyTheShownFrameIsLoaded_AndLaterCreatesDoNotSwitchIt` wykrył błąd: `ConstructWorld` ładował budowlę utworzoną pod inną ramą niż pokazywana (`TryEvaluate` nie sprawdzał `Parent`), naprawione.
