# K1-03 · Core: stan, operacje, walidacja, kontrakty protokołu

| | |
| --- | --- |
| Prowadzi | Opus 5.5 (Claude Code) |
| Testy akceptacyjne | Opus 5.5 |
| Przegląd | GPT-6.1 Sol (Codex), wysiłek `high` |
| Zależy od | K1-02A |
| Gałąź | `pakiet/k1-03-core` |

**Cel:** czysty model świata w `Horyzont.Core`: identyfikatory, budowle, rejestry, wersje terenu, operacje z walidacją i rozwiązywaniem intencji, `ServerSimulation`, migawki do checkpointu, skrót stanu i kontrakty komunikatów protokołu; wszystko deterministyczne i testowane bez silnika.

## Kontekst

- `docs/ZASADY.md`, zwłaszcza punkty 2, 3, 4 i 6
- `docs/spec/01-planeta.md`: Model świata, Stan operacje i dziennik, Sieć (komunikaty), Zapis (co trafia do checkpointu)
- `docs/spec/02-kosmos.md`: Architektura i model danych (ramy odniesienia) oraz Operacje i dziennik; stąd pola ramy, `ParentSeq`, tryb budowli i `EntityReparentOp`

## Zakres

Wszystkie typy opisane niżej, testy akceptacyjne oraz `FakeTerrainService`, `InMemoryJournal` i `WorldFactory` w `tests/Horyzont.Testing`.

Poza zakresem: kodeki binarne (K1-04), SQLite (K1-05), transport i sesja (K1-06, K1-07), adapter Voxel Tools (pakiet 9). Interfejs `ITerrainService` definiujesz tu, a implementację w grze dostarcza pakiet 9.

## Projekt

### Przestrzenie nazw i pliki

| Przestrzeń nazw | Typy |
| --- | --- |
| `Horyzont.Core.Ids` | `WorldId`, `SystemId`, `PlanetId`, `EntityId`, `PlayerId`, `IdentityPlatform`, `PlatformIdentity`, `FrameKind`, `FrameRef`, `OpId`, `Tick`, `EntityIdAllocator`, `PlayerIdAllocator` |
| `Horyzont.Core.World` | `WorldState`, `PlanetParams`, `PlanetRegistry`, `PlayerState`, `PlayerStore` |
| `Horyzont.Core.World.Blocks` | `BlockTypeDef`, `BlockShape`, `BlockTypeRegistry`, `MaterialRegistry`, `RegistryEntry` |
| `Horyzont.Core.World.Constructs` | `BlockState`, `CellPos`, `ChunkPos`, `Face`, `ConstructMode`, `ConstructChunk`, `Construct`, `ConstructStore`, `ConstructRaycast`, `ConstructHit` |
| `Horyzont.Core.World.Terrain` | `TerrainBlockKey`, `TerrainBlockVersion`, `TerrainVersionTable`, `TerrainMath`, `ITerrainService`, `TerrainHit` |
| `Horyzont.Core.Operations` | `Operation` i rekordy operacji, `OperationKind`, `TerrainShape`, `TerrainEditMode`, `JournalEntry`, `AppliedOperation` |
| `Horyzont.Core.Intents` | `Intent` i rekordy intencji, `BlockTarget` i cele, `RejectReason`, `IntentResult` |
| `Horyzont.Core.Simulation` | `GameRules`, `IJournalSink`, `ServerSimulation`, `IntentValidator`, `IntentResolver`, `OperationApplier`, `TokenBucket`, `ReplayFailedException` |
| `Horyzont.Core.Checkpoints` | `WorldCheckpoint`, `ConstructHeaderRecord`, `ConstructChunkKey`, `ConstructChunkRecord`, `PlayerRecord`, `WorldStateBuilder` |
| `Horyzont.Core.Hashing` | `StateHasher` |
| `Horyzont.Core.Protocol` | `ProtocolConstants`, `MessageType`, `DeliveryChannel`, `MessageChannels`, `HandshakeRejectCode`, `DisconnectCode` |
| `Horyzont.Core.Protocol.Messages` | `Message` i rekordy komunikatów, `PlayerInput`, `InputButtons`, `SnapshotFrame`, `EntityState`, `EntityStateFields`, `EntityKind`, `TerrainBlockChecksum` |

Mutacje stanu są `internal` i wykonuje je wyłącznie `OperationApplier` (oraz `WorldStateBuilder` przy wczytywaniu). `InternalsVisibleTo("Horyzont.Core.Tests")`. Kod spoza Core (Testing, Persistence, gra) tworzy stan wyłącznie przez publiczne `WorldState.CreateNew` i `WorldStateBuilder`.

Każdy typ z `IComparable<T>` ma też operatory `<`, `<=`, `>`, `>=`; bez nich reguła CA1036 przy ostrzeżeniach jako błędach wywraca build.

### Identyfikatory

```csharp
public readonly record struct WorldId(Guid Value);
public readonly record struct SystemId(ulong Value);          // układ gwiezdny; w podprojektach 1–6 zawsze 1
public readonly record struct PlanetId(ulong Value);

/// Wewnętrzny identyfikator gracza w świecie, nadawany przez serwer od 1 (PlayerIdAllocator).
/// Nigdy nie jest SteamID: tożsamość platformy to osobny PlatformIdentity.
public readonly record struct PlayerId(ulong Value)
{
    public static PlayerId None => default;                  // 0 nigdy nie jest nadawane
    public bool IsNone => Value == 0;
}

public enum IdentityPlatform : byte { Dev = 1, Steam = 2 }   // kolejne platformy dopisuje się na końcu
/// Dev: losowa liczba zapisana w profilu klienta (testy, sieć lokalna). Steam: SteamID64.
public readonly record struct PlatformIdentity(IdentityPlatform Platform, ulong Value);

public readonly record struct EntityId(ulong Value)
{
    /// Byty trwałe (gracze, budowle, byty trwałe z podprojektu 3) mają numery 1 .. 2^32 − 1 z EntityIdAllocator.
    /// Byty nietrwałe (stwory, pociski, bossowie z podprojektu 4) mają numery od 2^32 z licznika serwera
    /// poza checkpointem i skrótem stanu, więc nie zmieniają NextEntityId.
    public const ulong TransientBase = 1UL << 32;
    public static EntityId None => default;                  // 0 nigdy nie jest nadawane
    public bool IsNone => Value == 0;
    public bool IsTransient => Value >= TransientBase;
}

/// Rama odniesienia: układ (początek w środku planety domowej; słońce to kierunek światła), planeta (początek w środku planety)
/// albo budowla (początek w narożniku komórki (0,0,0)). Id to wartość SystemId, PlanetId albo EntityId.
public enum FrameKind : byte { System = 1, Planet = 2, Construct = 3 }
public readonly record struct FrameRef(FrameKind Kind, ulong Id)
{
    public static FrameRef Of(SystemId system) => new(FrameKind.System, system.Value);
    public static FrameRef Of(PlanetId planet) => new(FrameKind.Planet, planet.Value);
    public static FrameRef Of(EntityId construct) => new(FrameKind.Construct, construct.Value);
}

public readonly record struct OpId(ulong Value) : IComparable<OpId>   // plus operatory porównania
{
    public static OpId None => default;
    public OpId Next => new(Value + 1);
    public int CompareTo(OpId other) => Value.CompareTo(other.Value);
}

public readonly record struct Tick(ulong Value) : IComparable<Tick>   // plus operatory porównania
{
    public Tick Next => new(Value + 1);
    public int CompareTo(Tick other) => Value.CompareTo(other.Value);
}

public sealed class EntityIdAllocator
{
    public EntityIdAllocator(ulong nextValue);   // nextValue >= 1
    public ulong NextValue { get; }              // zapisywane w checkpoincie
    public EntityId PeekNext();                  // używa resolver; nic nie zmienia
    internal void MarkUsed(EntityId id);         // applier: NextValue = max(NextValue, id + 1); rzuca, gdy id.IsTransient
}
// PlayerIdAllocator: to samo API dla PlayerId (NextValue zapisywane w checkpoincie jako NextPlayerId).
// PeekNext rzuca, gdy następny numer doszedłby do EntityId.TransientBase.
```

Resolver tylko podgląda następny identyfikator, a przesuwa go applier. Dzięki temu powtórka dziennika odtwarza oba alokatory bez osobnego zapisu.

Kanoniczny stan każdego bytu to rama rodzica (`FrameRef`), licznik przepięć `ParentSeq` i poza lokalna w tej ramie; pozycja w świecie jest zawsze wartością pochodną. W podprojekcie 1 gracze i budowle mają ramę swojej planety, a `ParentSeq` = 0. Złożenie ram w podwójnej precyzji (`FrameMath`, `Vector3D`, `QuaternionD`) dochodzi w K5-20.

### Budowle

```csharp
/// Bits 0-15: block type id (0 = empty). Bits 16-20: orientation 0-23.
/// Bits 21-24: cost revision 0-15 (refunds use the cost recorded at placement).
/// Bits 25-27: function state 0-7 (meaning defined by the block type in content, opaque to Core).
/// Bits 28-31: reserved, must be 0.
public readonly record struct BlockState(uint Packed)
{
    public const int OrientationCount = 24;
    public const int CostRevisionCount = 16;
    public const int FunctionStateCount = 8;
    public static BlockState Empty => default;
    public ushort TypeId => (ushort)(Packed & 0xFFFF);
    public byte Orientation => (byte)((Packed >> 16) & 0x1F);
    public byte CostRevision => (byte)((Packed >> 21) & 0xF);
    public byte FunctionState => (byte)((Packed >> 25) & 0x7);
    public bool IsEmpty => TypeId == 0;
    public bool IsWellFormed => Packed == 0 || (TypeId != 0 && Orientation < OrientationCount && (Packed >> 28) == 0);
    public static BlockState Create(ushort typeId, byte orientation, byte costRevision = 0, byte functionState = 0);
    // rzuca dla typeId == 0, orientation >= 24, costRevision >= 16 i functionState >= 8
    public BlockState WithFunctionState(byte functionState);   // rzuca dla pustego bloku i functionState >= 8
}

public enum ConstructMode : byte { Static = 0, Ship = 1 }   // tryb statku włącza podprojekt 2

public enum Face : byte { PosX = 0, NegX = 1, PosY = 2, NegY = 3, PosZ = 4, NegZ = 5 }

public readonly record struct CellPos(int X, int Y, int Z)
{
    public const int Limit = 2048;                               // poprawny zakres: -2048..2047 na oś
    public bool IsInBounds { get; }
    public ChunkPos Chunk => new(X >> 4, Y >> 4, Z >> 4);       // przesunięcie arytmetyczne = podłoga także dla ujemnych
    public int LocalIndex => ((X & 15) << 8) | ((Y & 15) << 4) | (Z & 15);   // 0..4095
    public CellPos Offset(Face face);
}

public readonly record struct ChunkPos(int X, int Y, int Z) : IComparable<ChunkPos>;   // porządek leksykograficzny X, Y, Z; plus operatory
```

- `BlockState.FunctionState`: trzy bity stanu bloku funkcyjnego, np. otwarte drzwi albo włączona lampa. Znaczenie podaje typ bloku w treści gry, a Core go nie interpretuje. W podprojekcie 1 każdy blok ma stan 0; zmienia go operacja `BlockFunctionStateSetOp` (kod 49) z podprojektu 3.
- `ConstructChunk`: 4096 wartości `uint`, licznik niepustych, `internal BlockState Set(int index, BlockState state)` zwraca poprzedni stan.
- `Construct`: `Id`, `Parent` (`FrameRef`; w podprojekcie 1 rama planety), `ParentSeq` (`uint`), `Mode` (`ConstructMode`, przy tworzeniu `Static`), `Origin` (`Vector3`, układ ramy rodzica, narożnik komórki (0,0,0)), `Rotation` (`Quaternion`, z układu budowli do układu ramy rodzica), `Version` (`uint`), `BlockCount`, chunki w `SortedDictionary<ChunkPos, ConstructChunk>`, prostopadłościan zajętych komórek (do szybkiego odrzucania w raycaście). Metody: `GetBlock`, `CellCenterToParent`, `ParentToCell`, `internal SetBlock` (podbija wersję, usuwa puste chunki, oznacza zmiany do checkpointu).
- `ConstructStore`: `TryGet`, `All` (porządek po `EntityId`), `InFrame(FrameRef)` (budowle, których rodzicem jest ta rama); `internal Create`, `internal Remove`.
- `ConstructRaycast.Raycast(store, frame, origin, direction, maxDistance, out ConstructHit hit)`: promień w układzie ramy `frame`, algorytm Amanatidesa–Woo w układzie każdej budowli z tej ramy, której sfera otaczająca przecina promień. `ConstructHit(EntityId Construct, CellPos Cell, Face Face, Vector3 Position, float Distance)`.

### Rejestry, planety, gracze

```csharp
public enum BlockShape : byte { Cube = 1 }      // kolejne kształty w pakiecie 12
public sealed record BlockTypeDef(string Key, BlockShape Shape, byte MaterialId, bool HasCollision);
public readonly record struct RegistryEntry(ushort Id, string Key);   // klucz do 64 bajtów UTF-8

public sealed class BlockTypeRegistry          // klucz tekstowy <-> numer, numery od 1, 0 = pusty
{
    public const string MissingKey = "core:missing";   // zastępczy typ dla nieznanych kluczy przy wczytaniu
    public bool TryGetId(string key, out ushort id);
    public bool TryGetKey(ushort id, [NotNullWhen(true)] out string? key);
    public bool IsKnown(ushort id);
    public IReadOnlyList<RegistryEntry> Entries { get; }   // posortowane po Id
    internal ushort GetOrAssign(string key);               // rzuca po przekroczeniu ProtocolConstants.MaxBlockTypes
}
// MaterialRegistry: to samo dla materiałów terenu, identyfikatory 1..255 (typ byte, 0 = powietrze),
// limit ProtocolConstants.MaxMaterials.

public sealed record PlanetParams(
    PlanetId Id, SystemId System, ulong Seed, string GeneratorProfile,   // GeneratorProfile do 64 bajtów UTF-8, z wersją
    float RadiusMeters = 4000f, float VoxelSizeMeters = 1f, float Gravity = 9.81f,
    float AtmosphereHeightMeters = 1000f, float CoreDepthMeters = 400f, float DayLengthSeconds = 1800f);
// PlanetRegistry: TryGet(PlanetId, out PlanetParams), All (po Id).
// DayLengthSeconds = 1800 to cykl słońca z podprojektu 2; od K5-21 porę dnia liczy układ gwiezdny.

public sealed class PlayerState
{
    public PlayerId Player { get; }
    public PlatformIdentity Identity { get; }
    public string DisplayName { get; }         // z ostatniego wejścia, do 32 bajtów UTF-8
    public EntityId Entity { get; }
    public FrameRef Frame { get; }             // rama rodzica; w podprojekcie 1 zawsze rama planety
    public uint ParentSeq { get; }             // +1 przy każdym przepięciu (EntityReparentOp)
    public CellPos? Seat { get; }              // komórka fotela, gdy Frame to budowla
    public Vector3 Position { get; }           // stopy, układ ramy rodzica
    public Quaternion BodyRotation { get; }    // układ ramy rodzica
    public float Pitch { get; }
    public bool IsOnline { get; }
    /// Oko gracza w ramie planety (podprojekt 1). Od K5-20 „górę” w każdej ramie daje FrameMath.
    public Vector3 EyePosition(GameRules rules) => Position + TerrainMath.UpAt(Position) * rules.EyeHeight;
}
// PlayerStore: TryGet, TryGetByIdentity(PlatformIdentity, out PlayerId), All (po PlayerId), Online (po PlayerId).
```

### Stan świata

```csharp
public sealed class WorldState
{
    /// Nowy świat. Typy bloków dostają numery od 1 w podanej kolejności; core:missing jest dopisywany na końcu,
    /// jeśli go brak. Materiały dostają numery od 1 w podanej kolejności.
    /// LastOpId = None, CurrentTick = 0, NextEntityId = 1, NextPlayerId = 1.
    public static WorldState CreateNew(WorldId id, ulong galaxySeed, IEnumerable<PlanetParams> planets,
                                       IEnumerable<string> blockTypeKeys, IEnumerable<string> materialKeys);

    public WorldId Id { get; }
    public ulong GalaxySeed { get; }
    public OpId LastOpId { get; }
    public Tick CurrentTick { get; }
    public EntityIdAllocator EntityIds { get; }
    public PlayerIdAllocator PlayerIds { get; }
    public PlanetRegistry Planets { get; }
    public BlockTypeRegistry BlockTypes { get; }
    public MaterialRegistry Materials { get; }
    public ConstructStore Constructs { get; }
    public PlayerStore Players { get; }
    public TerrainVersionTable TerrainVersions { get; }

    /// Rama istnieje: planeta w rejestrze, budowla w magazynie; układ tylko SystemId 1.
    public bool FrameExists(FrameRef frame);
    /// Planeta, w której leży rama: rama planety wprost, budowla przez łańcuch rodziców; false dla ramy układu.
    public bool TryResolvePlanet(FrameRef frame, out PlanetId planet);

    public bool IsCheckpointInFlight { get; }                            // true między CaptureCheckpoint a jego zamknięciem
    public WorldCheckpoint CaptureCheckpoint();                          // opis w sekcji Checkpoint
    public void CompleteCheckpoint(WorldCheckpoint committed);
    public void RequeueAfterFailedCheckpoint(WorldCheckpoint failed);
}

public sealed class WorldStateBuilder           // wczytywanie (K1-05); nic nie oznacza jako zmienione
{
    public WorldStateBuilder(WorldId id, ulong galaxySeed);
    public void SetProgress(OpId lastOpId, Tick tick, ulong nextEntityId, ulong nextPlayerId);   // granica i tick ostatniego checkpointu
    public void AddPlanet(PlanetParams planet);
    public void AddBlockType(RegistryEntry entry);
    public void AddMaterial(RegistryEntry entry);
    public void AddConstruct(ConstructHeaderRecord header);
    public void AddChunk(ConstructChunkRecord chunk);                         // budowla musi być już dodana
    public void AddPlayer(PlayerRecord player);
    public void AddTerrainVersion(TerrainBlockVersion version);
    public WorldState Build();   // niespójne dane (chunk bez budowli, budowla bez bloków, zdublowany klucz albo tożsamość,
                                 // rama rodzica, która nie istnieje) → InvalidDataException
}
```

`BlockCount` budowli i prostopadłościan zajętych komórek `Build` liczy z chunków. K1-05 zamienia `InvalidDataException` na `WorldCorruptedException`.

Rejestry zmieniają się tylko przy tworzeniu i wczytywaniu świata, nigdy w trakcie symulacji. Klucze nowych typów bloków i materiałów z treści gry dopisuje do istniejącego świata `WorldLoader` (K1-05), z kolejnymi numerami, zanim wywoła `Build`. Klucz zapisany w świecie, a nieznany treści gry, zostaje w rejestrze; gra rysuje taki blok jako `core:missing` (specyfikacja: „nowy typ nie psuje zapisów, a brakujący ładuje się jako blok zastępczy”).

### Teren (wersje i interfejs)

```csharp
public readonly record struct TerrainBlockKey(PlanetId Planet, byte Lod, int X, int Y, int Z) : IComparable<TerrainBlockKey>;
// porządek: Planet.Value, Lod, X, Y, Z; plus operatory
public readonly record struct TerrainBlockVersion(TerrainBlockKey Key, uint Version);

public sealed class TerrainVersionTable
{
    public uint Get(TerrainBlockKey key);                    // 0 = nigdy nieedytowany
    public IEnumerable<TerrainBlockVersion> All { get; }     // posortowane
    internal uint Bump(TerrainBlockKey key);                 // zwraca nową wersję, oznacza do checkpointu
}

public static class TerrainMath
{
    public const int BlockSize = 16;
    public static Vector3 UpAt(Vector3 planetLocalPosition);             // znormalizowany wektor od środka planety
    /// Klucze bloków LOD0, których voxele mogą się zmienić: prostopadłościan sfery powiększony o marginesVoxels,
    /// współrzędne bloku = floor(pozycja / (16 * voxelSize)); wynik posortowany, bez alokacji poza listą wyjściową.
    public static void AffectedBlocks(PlanetId planet, Vector3 center, float radius, float voxelSize,
                                      int marginVoxels, List<TerrainBlockKey> output);
}

public readonly record struct TerrainHit(Vector3 Position, Vector3 Normal, float Distance);

public interface ITerrainService
{
    bool IsAreaLoaded(PlanetId planet, Vector3 center, float radius);
    void LoadAreaBlocking(PlanetId planet, Vector3 center, float radius);   // tylko przy powtórce dziennika
    bool Raycast(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out TerrainHit hit);
    void ApplyEdit(TerrainEditOp edit);                                     // deterministyczne dla tych samych danych wejściowych
}
```

Środek planety to początek jej układu, więc `UpAt` nie potrzebuje parametru planety.

### Operacje

```csharp
public enum OperationKind : byte
{
    TerrainEdit = 1, ConstructCreate = 2, BlockSet = 3, BlockClear = 4, PlayerJoin = 5, PlayerLeave = 6, EntityReparent = 7,
    // zakresy kolejnych pakietów: 8–19 dalsze pakiety Planety (K2–K4), 20–29 Kosmos, 30–49 Przetrwanie, 50–59 Walka, 60–69 Treść EA
}
public enum TerrainShape : byte { Sphere = 1 }
public enum TerrainEditMode : byte { Remove = 1, Add = 2 }

public abstract record Operation
{
    private protected Operation() { }
    public abstract OperationKind Kind { get; }
}

public sealed record TerrainEditOp(PlanetId Planet, TerrainShape Shape, Vector3 Center, float Radius,
                                   TerrainEditMode Mode, byte MaterialId) : Operation;          // MaterialId = 0 dla Remove
public sealed record ConstructCreateOp(EntityId Construct, FrameRef Parent, Vector3 Origin, Quaternion Rotation,
                                       CellPos FirstCell, BlockState Block) : Operation;   // Origin i Rotation w ramie Parent
public sealed record BlockSetOp(EntityId Construct, CellPos Cell, BlockState Block) : Operation;
public sealed record BlockClearOp(EntityId Construct, CellPos Cell) : Operation;
public sealed record PlayerJoinOp(PlayerId Player, PlatformIdentity Identity, string DisplayName, EntityId Entity,
                                  FrameRef Frame, uint ParentSeq, Vector3 Position, Quaternion BodyRotation) : Operation;
public sealed record PlayerLeaveOp(PlayerId Player, FrameRef Frame, uint ParentSeq, Vector3 Position,
                                   Quaternion BodyRotation, float Pitch) : Operation;
/// Przepięcie bytu (gracza albo budowli) do nowej ramy. Używają go wsiadanie, wysiadanie, wyskok,
/// przejście statku między strefami wpływu i teleporty (podprojekty 2 i 3). W podprojekcie 1 nic go nie tworzy,
/// ale applier, kodek i testy powstają tutaj, żeby format dziennika był kompletny od początku.
public sealed record EntityReparentOp(EntityId Entity, FrameRef NewParent, uint NewParentSeq,
                                      Vector3 LocalPosition, Quaternion LocalRotation, Vector3 LocalVelocity,
                                      CellPos? SeatCell) : Operation;
// Każdy rekord nadpisuje Kind odpowiednią wartością.

public sealed record JournalEntry(OpId Id, Tick Tick, PlayerId? Actor, Operation Operation);

/// Wersja budowli po operacji. Operacja złożona z podprojektu 3 może zmienić kilka budowli naraz.
public readonly record struct ConstructChange(EntityId Construct, uint Version, bool Removed);

public sealed record AppliedOperation(
    OpId Id, Tick Tick, PlayerId? Actor, Operation Operation,
    IReadOnlyList<TerrainBlockVersion> TerrainVersions,   // niepuste dla TerrainEdit
    IReadOnlyList<ConstructChange> ConstructChanges);     // po jednym wpisie na zmienioną budowlę, po EntityId
```

Warunki wstępne i skutki (`OperationApplier`):

| Operacja | Warunki wstępne | Skutek |
| --- | --- | --- |
| `TerrainEdit` | planeta znana; wartości skończone; promień > 0; dla `Remove` `MaterialId = 0`; dla `Add` materiał znany | `AffectedBlocks` z marginesem `GameRules.TerrainEditMarginVoxels` → `Bump` każdej wersji → `ITerrainService.ApplyEdit` |
| `ConstructCreate` | identyfikator nieużyty (`Value ≥ EntityIds.NextValue`); rodzic to rama znanej planety; komórka w zakresie; blok poprawny i znanego typu | `MarkUsed`; nowa budowla z jednym blokiem, wersja 1, `ParentSeq` 0, tryb `Static` |
| `BlockSet` | budowla istnieje; komórka w zakresie i pusta; blok poprawny i znanego typu | wstawienie, wersja +1 |
| `BlockClear` | budowla istnieje; komórka zajęta; żaden gracz, także offline, nie siedzi w tej komórce; usunięcie ostatniego bloku wymaga, żeby żaden byt nie miał tej budowli za rodzica | usunięcie, wersja +1; przy 0 bloków usunięcie budowli |
| `PlayerJoin` | gracz offline. Gracz znany: ta sama tożsamość, jego dotychczasowy byt, jego rama i `ParentSeq`. Gracz nowy: `Player.Value ≥ PlayerIds.NextValue`, tożsamości nie ma inny gracz, byt nieużyty (`Value ≥ EntityIds.NextValue`), rama istnieje, `ParentSeq` 0 | gracz online z podaną pozycją i nazwą; `MarkUsed` obu alokatorów dla nowego |
| `PlayerLeave` | gracz online; rama i `ParentSeq` równe bieżącym | zapis pozycji, obrotu i pochylenia; gracz offline |
| `EntityReparent` | byt istnieje i ma ramę: gracz (online albo offline), budowla, a od podprojektu 3 także byt trwały; nowa rama istnieje i nie jest tym bytem ani jego potomkiem; `NewParentSeq` = bieżący + 1; `SeatCell` tylko dla gracza w ramie budowli, komórka zajęta i wolna od innych graczy, także offline | nowa rama, `ParentSeq`, poza lokalna i fotel; `LocalVelocity` przekazuje kontrolerowi gry prędkość początkową, Core jej nie przechowuje |

Po zastosowaniu: `LastOpId = LastOpId.Next`, a `ServerSimulation` dopisuje `JournalEntry` do `IJournalSink`. Wyjątek w trakcie stosowania przełącza symulację w stan `Faulted`: każde kolejne wywołanie rzuca `InvalidOperationException`, a gra zatrzymuje serwer bez checkpointu (specyfikacja, „Obsługa błędów”).

Semantyka stosowania operacji jest częścią formatu dziennika: stare dzienniki muszą odtwarzać się identycznie, więc jej zmiana wymaga nowej wersji formatu operacji (`docs/ZASADY.md`, punkt 3).

### Intencje

```csharp
public abstract record Intent { private protected Intent() { } }
public sealed record TerrainToolIntent(PlanetId Planet, Vector3 RayOrigin, Vector3 RayDirection, float Radius,
                                       TerrainEditMode Mode, byte MaterialId) : Intent;
public sealed record PlaceBlockIntent(BlockTarget Target, ushort BlockTypeId, byte Orientation) : Intent;
public sealed record RemoveBlockIntent(EntityId Construct, CellPos Cell) : Intent;

public abstract record BlockTarget { private protected BlockTarget() { } }
public sealed record ConstructFaceTarget(EntityId Construct, CellPos Cell, Face Face) : BlockTarget;
public sealed record TerrainTarget(PlanetId Planet, Vector3 RayOrigin, Vector3 RayDirection, Vector3 LookForward) : BlockTarget;

public enum RejectReason : byte
{
    None = 0, UnknownPlayer = 1, PlayerOffline = 2, RateLimited = 3, InvalidParameters = 4,
    RayOriginMismatch = 5, NoHit = 6, OutOfReach = 7, AreaNotLoaded = 8, BelowCore = 9,
    OutsideAtmosphere = 10, UnknownPlanet = 11, UnknownConstruct = 12, CellOutOfBounds = 13,
    CellOccupied = 14, CellEmpty = 15, UnknownBlockType = 16, UnknownMaterial = 17,
    IntersectsPlayer = 18, NoLineOfSight = 19, WrongPlanet = 20,
}

public readonly record struct IntentResult(RejectReason Reason, AppliedOperation? Applied)
{
    public bool IsAccepted => Reason == RejectReason.None;
    public static IntentResult Accepted(AppliedOperation applied) => new(RejectReason.None, applied);
    public static IntentResult Rejected(RejectReason reason) => new(reason, null);
}
```

Walidacja i rozwiązanie (kolejność sprawdzeń ma znaczenie, bo decyduje o zwracanym powodzie):

**Wspólne, w tej kolejności:** gracz istnieje (`UnknownPlayer`) i jest online (`PlayerOffline`); wszystkie liczby skończone, kierunki znormalizowane z tolerancją 0,01 (`InvalidParameters`); dla intencji z `RayOrigin`: `|RayOrigin − oko gracza| ≤ MaxRayOriginErrorMeters` (`RayOriginMismatch`); żeton w kubełku tempa danej klasy (`RateLimited`), zużywany dopiero po przyjęciu. Klasy tempa: teren (`TerrainToolIntent`) i bloki (`PlaceBlockIntent`, `RemoveBlockIntent`).

„Planeta gracza” to wynik `TryResolvePlanet(gracz.Frame)`, a „budowla na planecie gracza” to budowla, której rama rozwiązuje się do tej samej planety. Gracz w ramie układu nie ma planety, więc jego intencje terenu i bloków dostają `WrongPlanet`. Pozycje w intencjach są w ramie planety gracza; w podprojekcie 1 to jego własna rama.

**`TerrainToolIntent`:** planeta znana (`UnknownPlanet`) i równa planecie gracza (`WrongPlanet`); promień w [`MinToolRadius`, `MaxToolRadius`], tryb znany, dla `Remove` `MaterialId = 0` (`InvalidParameters`), dla `Add` materiał znany (`UnknownMaterial`); raycast terenu do `ReachMeters` (`NoHit`); bliższe trafienie budowli niż terenu (`NoLineOfSight`); środek edycji = punkt trafienia; `|środek| ≥ R − CoreDepth + promień` (`BelowCore`) i `|środek| ≤ R + Atmosphere` (`OutsideAtmosphere`); obszar z marginesem wczytany (`AreaNotLoaded`); dla `Add` sfera nie przecina kapsuły żadnego gracza online (`IntersectsPlayer`) → `TerrainEditOp`.

**`PlaceBlockIntent` na ścianie budowli:** typ bloku znany (`UnknownBlockType`), orientacja < 24 (`InvalidParameters`); budowla istnieje (`UnknownConstruct`) na planecie gracza (`WrongPlanet`); wskazana komórka zajęta (`CellEmpty`); nowa komórka = `Cell.Offset(Face)` w zakresie (`CellOutOfBounds`) i pusta (`CellOccupied`); odległość od oka do środka nowej komórki ≤ zasięg + 0,87 m (`OutOfReach`); promień od oka do środka wskazanej ściany nie trafia wcześniej w teren ani w inną komórkę (`NoLineOfSight`); obszar wczytany (`AreaNotLoaded`); prostopadłościan nowej komórki nie przecina kapsuły gracza (`IntersectsPlayer`) → `BlockSetOp`.

**`PlaceBlockIntent` na terenie:** typ i orientacja jak wyżej; planeta celu znana (`UnknownPlanet`) i równa planecie gracza (`WrongPlanet`); raycast terenu (`NoHit`) i budowli (`NoLineOfSight`); `up = UpAt(trafienie)`; `forward` = rzut `LookForward` na płaszczyznę styczną, przy wektorze zdegenerowanym rzut `RayDirection`, a gdy i ten jest zdegenerowany, rzut osi X układu planety; `Rotation` takie, że lokalne +Y budowli = `up`, a lokalne −Z = `forward` (konwencja Godota); `Origin = trafienie − Rotation · (0,5; 0; 0,5)`, więc komórka (0,0,0) stoi na terenie, wyśrodkowana na punkcie trafienia; kolizja z graczem i wczytany obszar jak wyżej → `ConstructCreateOp` z `EntityIds.PeekNext()`, rodzicem `FrameRef.Of(planeta)` i komórką (0,0,0).

**`RemoveBlockIntent`:** budowla istnieje (`UnknownConstruct`) na planecie gracza (`WrongPlanet`); komórka w zakresie (`CellOutOfBounds`) i zajęta (`CellEmpty`); odległość ≤ zasięg + 0,87 m (`OutOfReach`); pierwsza komórka trafiona promieniem od oka do jej środka to ta komórka, a teren nie jest bliżej (`NoLineOfSight`) → `BlockClearOp`.

Kodek (K1-04) sprawdza tylko poprawność typów (zakresy typów C#, enumy, `BlockState.IsWellFormed`, skończone liczby, znormalizowane kwaterniony). Reguły gry, w tym granice komórek, orientację w `PlaceBlockIntent` i `MaterialId` przy `Remove`, sprawdza walidator i applier.

### Reguły gry

```csharp
public sealed record GameRules
{
    public int TickRate { get; init; } = 60;
    public float ReachMeters { get; init; } = 8f;
    public float MinToolRadius { get; init; } = 0.5f;
    public float MaxToolRadius { get; init; } = 3f;
    public int TerrainEditsPerSecond { get; init; } = 5;
    public int TerrainEditBurst { get; init; } = 5;
    public int BlockEditsPerSecond { get; init; } = 10;
    public int BlockEditBurst { get; init; } = 10;
    public float MaxRayOriginErrorMeters { get; init; } = 2f;
    public float PlayerCapsuleRadius { get; init; } = 0.4f;
    public float PlayerCapsuleHeight { get; init; } = 1.8f;
    public float EyeHeight { get; init; } = 1.62f;
    public int TerrainEditMarginVoxels { get; init; } = 4;
}
```

Ze specyfikacji pochodzą: 60 Hz, zasięg 8 m, promień 0,5–3 m, 5 edycji terenu na sekundę. Założenia do potwierdzenia: tempo bloków, tolerancja początku promienia, wymiary kapsuły i wysokość oczu (pakiet 11), margines edycji (pakiet 9 zmierzy, ile bloków danych zmienia `do_sphere`).

`TokenBucket` liczy wyłącznie na liczbach całkowitych i w tickach, nie w czasie rzeczywistym: pojemność `Burst × TickRate` jednostek, start pełny, każde `ServerSimulation.AdvanceTick()` dolewa `EditsPerSecond` jednostek (do pojemności), przyjęta intencja kosztuje `TickRate` jednostek. Przy wartościach domyślnych pięć edycji terenu w jednym ticku opróżnia kubełek, a kolejna przechodzi dokładnie po 12 tickach.

### Symulacja serwera

```csharp
public interface IJournalSink { void Append(JournalEntry entry); }   // nie blokuje; zapis w tle (K1-05)

public sealed class ServerSimulation
{
    public ServerSimulation(WorldState state, ITerrainService terrain, IJournalSink journal,
                            GameRules rules, ILogger<ServerSimulation> logger);
    public WorldState State { get; }
    public GameRules Rules { get; }
    public bool IsFaulted { get; }

    public IntentResult Submit(PlayerId player, Intent intent);   // walidacja → rozwiązanie → zastosowanie → dziennik; zły input nigdy nie rzuca
    /// Znana tożsamość: jej PlayerId, byt, rama i ostatnia pozycja. Nowa: PlayerIds.PeekNext(), nowy byt i punkt startu.
    public PlayerJoinOp PreparePlayerJoin(PlatformIdentity identity, string displayName);
    public AppliedOperation ApplyServerOperation(Operation operation);   // operacje serwera (Join, Leave); złamany warunek wstępny = wyjątek
    public IReadOnlyList<AppliedOperation> LeaveAllOnlinePlayers();      // PlayerLeaveOp z bieżącą kinematyką dla każdego gracza online, po PlayerId
    public void Replay(JournalEntry entry);                       // odtwarzanie: wymaga Id == LastOpId.Next, ustawia CurrentTick = entry.Tick, nie zapisuje do dziennika
    public void AdvanceTick();                                    // CurrentTick + 1, dolewa żetony
    /// Ruch z fizyki, poza dziennikiem, w ramie gracza. false dla złych wartości albo gdy frame i parentSeq
    /// nie są bieżące (spóźniony ruch sprzed przepięcia).
    public bool UpdatePlayerKinematics(PlayerId player, FrameRef frame, uint parentSeq,
                                       Vector3 position, Quaternion bodyRotation, float pitch);
}
```

- Punkt startu nowego gracza: raycast terenu z punktu (0, R + atmosfera, 0) w stronę środka pierwszej planety, start 0,1 m nad trafieniem, w ramie tej planety. Jest deterministyczny, a i tak trafia do `PlayerJoinOp`.
- Rama zapisana u gracza zawsze istnieje: ostatniego bloku budowli nie da się usunąć, gdy w jej ramie jest jakikolwiek byt, a gracz offline zajmuje swój fotel. Fotel gracza offline zwalnia dopiero intencja z podprojektu 2 (`SeatEvictIntent`), która przepina go do punktu odrodzenia.
- `Replay` przed edycją terenu woła `LoadAreaBlocking`. Niespełniony warunek wstępny daje `ReplayFailedException` z `OpId` i rodzajem operacji: odtwarzanie się zatrzymuje, nigdy nie pomija operacji.
- `UpdatePlayerKinematics` odrzuca wartości nieskończone i obrót o `|długość − 1| > 1e-3`: zwraca `false`, loguje ostrzeżenie i zostawia poprzednią kinematykę. Dzięki temu błąd fizyki nie trafia do `PlayerLeaveOp` ani do checkpointu, gdzie zapis by go odrzucił. Gra normalizuje obrót przed wywołaniem.
- Start serwera po wczytaniu: `Replay` każdego wpisu z ogona dziennika, potem `LeaveAllOnlinePlayers()` (po awarii nikt nie jest połączony, a gracz zapisany jako online nie mógłby wejść ponownie), potem checkpoint.
- Zatrzymanie serwera: zaczekaj na wynik checkpointu w locie (`IsCheckpointInFlight`); `ServerSession.Shutdown` i `Poll`, aż `IsShutdownComplete` (hak `OnPlayerLeft` stosuje `PlayerLeaveOp` tylko dla gracza, który jest online); `LeaveAllOnlinePlayers()` dla pozostałych; ostatni checkpoint; zamknięcie zapisu.

### Checkpoint

```csharp
public sealed record ConstructHeaderRecord(EntityId Id, FrameRef Parent, uint ParentSeq, ConstructMode Mode,
                                           Vector3 Origin, Quaternion Rotation, uint Version);
public readonly record struct ConstructChunkKey(EntityId Construct, ChunkPos Chunk);
public sealed record ConstructChunkRecord(ConstructChunkKey Key, uint[] Blocks);   // 4096 wartości, kopia należąca do rekordu
public sealed record PlayerRecord(PlayerId Player, PlatformIdentity Identity, string DisplayName, EntityId Entity,
                                  FrameRef Frame, uint ParentSeq, CellPos? Seat, Vector3 Position,
                                  Quaternion BodyRotation, float Pitch, bool IsOnline);

public sealed record WorldCheckpoint(
    OpId BarrierOpId, Tick Tick, ulong NextEntityId, ulong NextPlayerId,
    IReadOnlyList<ConstructHeaderRecord> UpsertedConstructs,
    IReadOnlyList<EntityId> RemovedConstructs,
    IReadOnlyList<ConstructChunkRecord> UpsertedChunks,
    IReadOnlyList<ConstructChunkKey> RemovedChunks,
    IReadOnlyList<PlayerRecord> UpsertedPlayers,
    IReadOnlyList<TerrainBlockVersion> TerrainVersions);   // dane voxeli tych bloków dokłada adapter terenu (pakiet 9)
```

- `WorldState.CaptureCheckpoint()` wołane na granicy ticku: kopiuje tylko to, co zmieniło się od poprzedniego checkpointu (gracze: wszyscy online, bo ich kinematyka zmienia się poza dziennikiem, plus zmienieni), i czyści znaczniki zmian. Koszt proporcjonalny do liczby zmian.
- Znaczniki zmian to same klucze (budowla, chunk, gracz, blok terenu). Czy klucz trafia do listy zmienionych, czy usuniętych, rozstrzyga stan w chwili `CaptureCheckpoint`.
- Najwyżej jeden checkpoint w locie: po `CaptureCheckpoint` stan czeka na `CompleteCheckpoint(committed)` (zapis udany) albo `RequeueAfterFailedCheckpoint(failed)` (zapis nieudany). Kolejne `CaptureCheckpoint` przed jednym z nich rzuca `InvalidOperationException`, więc pętla serwera pomija okresowy checkpoint, gdy `IsCheckpointInFlight`. Bez tej reguły checkpoint N+1 bez zmian z nieudanego N mógłby obciąć dziennik, który je pokrywał.
- `RequeueAfterFailedCheckpoint(failed)` ponownie oznacza wszystkie klucze z `failed`. Następny `CaptureCheckpoint` zapisze ich bieżący stan albo ich usunięcie.
- `WorldStateBuilder` buduje `WorldState` przy wczytywaniu (K1-05): rejestry, planety, budowle z chunkami, gracze (z `IsOnline`), wersje terenu, granica, tick, `NextEntityId` i `NextPlayerId`; nic nie oznacza jako zmienione.

### Skrót stanu

`StateHasher.Hash(WorldState)` zwraca `ulong` (XxHash64) z kanonicznego strumienia bajtów: `WorldId`, ziarno, `NextEntityId`, `NextPlayerId`, `LastOpId`, rejestry (wszystkie wpisy, po `Id`), planety, budowle (po `EntityId`; nagłówek z ramą, `ParentSeq` i trybem, potem chunki po `ChunkPos` z surowymi wartościami), wersje terenu (posortowane), gracze (po `PlayerId`: tożsamość, nazwa, byt, rama, `ParentSeq`, fotel, `IsOnline`). Liczby zmiennoprzecinkowe idą bitowo. Pozę budowli skrót obejmuje tylko w trybie `Static`, bo budowle w trybie `Ship` poruszają się poza dziennikiem (podprojekt 2). Skrót celowo pomija `CurrentTick` i kinematykę graczy, bo nie trafiają do dziennika; obejmuje dokładnie to, co odtwarza checkpoint z powtórką. Treść voxeli terenu jest poza Core, więc skrót obejmuje tylko wersje bloków.

### Kontrakty protokołu

```csharp
public static class ProtocolConstants
{
    public const ushort ProtocolVersion = 1;
    public const int MaxReliableMessageBytes = 256 * 1024;      // cała ramka: typ, wersja i ładunek
    public const int MaxUnreliableMessageBytes = 1150;
    public const int MaxDisplayNameBytes = 32;
    public const int MaxPasswordBytes = 64;
    public const int MaxAuthTicketBytes = 1024;
    public const int MaxRejectDetailBytes = 256;
    public const int MaxRegistryKeyBytes = 64;                  // klucze rejestrów i GeneratorProfile
    public const int MaxBlockTypes = 2048;
    public const int MaxMaterials = 255;
    public const int MaxPlanetsInHello = 64;
    public const int MaxInputsPerBatch = 5;
    public const int MaxFramesPerSnapshot = 8;
    public const int MaxEntitiesPerSnapshot = 22;               // 22 pełne stany i 8 ram mieszczą się w 1150 B,
                                                                // z zapasem 3 B na byt dla pól walki (podprojekt 4)
    public const int MaxEntityKindDataBytes = 64;               // KindData w EntitySpawn
    public const int MaxConstructChangesPerOperation = 1024;
    public const int MaxChunksPerConstructStatePart = 12;       // 12 chunków w najgorszym razie RLE mieści się w 256 KiB
    public const int MaxTerrainCacheEntries = 4096;
    public const int MaxTerrainChecksumEntries = 1024;
    public const int MaxTerrainBlockDataBytes = 64 * 1024;
}

public enum DeliveryChannel : byte { ReliableOrdered = 0, Unreliable = 1 }
public enum HandshakeRejectCode : byte
{
    WrongProtocol = 1, WrongBuild = 2, ServerFull = 3, BadPassword = 4, Banned = 5, AlreadyConnected = 6, IdentityMismatch = 7,
}
public enum DisconnectCode : byte { Normal = 0, Timeout = 1, ProtocolViolation = 2, Kicked = 3, ServerShutdown = 4, HostLeft = 5, HandshakeTimeout = 6 }
```

Migawka to jeden komunikat `Snapshot` na klienta i tick wysyłki. Byty, które nie mieszczą się w `MaxEntitiesPerSnapshot`, których rama nie mieści się w `MaxFramesPerSnapshot` albo które przekraczają budżet bajtów klienta na tick, serwer pomija w tej migawce według priorytetu, a pominięty byt dostaje wyższy priorytet w następnej. Pominięcie nie jest zniknięciem: byt znika u klienta tylko po `EntityDespawn`. Stan bytu to delta względem stanu tego bytu w migawce bazowej (`BaselineSnapshotId`), a byt nieobecny w bazie idzie w pełnym stanie, więc pominięcie jednego bytu nie psuje delty pozostałych. Budżet bajtów i priorytet według odległości wprowadza pakiet 14 (K3-14), a priorytet walki dokłada K7-46.

| `MessageType` | Wartość | Kierunek | Kanał |
| --- | --- | --- | --- |
| `ClientHello` | 1 | K→S | niezawodny |
| `ServerHello` | 2 | S→K | niezawodny |
| `HandshakeReject` | 3 | S→K | niezawodny |
| `ClientReady` | 4 | K→S | niezawodny |
| `TerrainCacheReport` | 5 | K→S | niezawodny |
| `TerrainBlockData` | 6 | S→K | niezawodny |
| `TerrainChecksums` | 7 | K→S | niezawodny |
| `ConstructState` | 8 | S→K | niezawodny |
| `ConstructRemoved` | 9 | S→K | niezawodny |
| `OperationApplied` | 10 | S→K | niezawodny |
| `IntentSubmit` | 11 | K→S | niezawodny |
| `IntentReply` | 12 | S→K | niezawodny |
| `InputBatch` | 13 | K→S | zawodny |
| `Snapshot` | 14 | S→K | zawodny |
| `SnapshotAck` | 15 | K→S | zawodny |
| `EntitySpawn` | 16 | S→K | niezawodny |
| `EntityDespawn` | 17 | S→K | niezawodny |
| `Ping` | 18 | K→S | zawodny |
| `Pong` | 19 | S→K | zawodny |
| `Disconnect` | 20 | oba | niezawodny |

```csharp
public abstract record Message { private protected Message() { } public abstract MessageType Type { get; } }

public sealed record ClientHello(ushort ProtocolVersion, ulong BuildHash, PlatformIdentity Identity, string DisplayName,
                                 string Password, byte[] AuthTicket) : Message;            // AuthTicket może być pusty
public sealed record ServerHello(WorldId World, Tick ServerTick, byte TickRate, PlayerId YourPlayer, EntityId YourEntity,
                                 IReadOnlyList<PlanetParams> Planets, IReadOnlyList<RegistryEntry> BlockTypes,
                                 IReadOnlyList<RegistryEntry> Materials) : Message;
public sealed record HandshakeReject(HandshakeRejectCode Code, string Detail) : Message;
public sealed record ClientReady : Message;
public sealed record TerrainCacheReport(PlanetId Planet, IReadOnlyList<TerrainBlockVersion> Entries) : Message;
public sealed record TerrainBlockData(TerrainBlockKey Key, uint Version, byte[] Data) : Message;   // Data nieprzezroczyste dla Core
public sealed record TerrainChecksums(PlanetId Planet, IReadOnlyList<TerrainBlockChecksum> Entries) : Message;
public readonly record struct TerrainBlockChecksum(TerrainBlockKey Key, ulong Hash);
public sealed record ConstructState(ConstructHeaderRecord Header, ushort PartIndex, ushort PartCount,
                                    IReadOnlyList<ConstructChunkRecord> Chunks) : Message;  // chunki tej budowli; część 0 zastępuje kopię klienta, kolejne ją uzupełniają
public sealed record ConstructRemoved(EntityId Construct) : Message;
public sealed record OperationApplied(OpId Id, Tick Tick, Operation Operation,
                                      IReadOnlyList<ConstructChange> ConstructChanges) : Message;
public sealed record IntentSubmit(uint ClientSeq, Intent Intent) : Message;
public sealed record IntentReply(uint ClientSeq, RejectReason Reason, OpId AppliedOpId) : Message;   // OpId.None przy odrzuceniu
public sealed record InputBatch(uint FirstSeq, IReadOnlyList<PlayerInput> Inputs) : Message;          // 1–5 wejść, kolejne numery od FirstSeq
public sealed record Snapshot(uint SnapshotId, Tick ServerTick, uint BaselineSnapshotId, uint LastProcessedInputSeq,
                              IReadOnlyList<SnapshotFrame> Frames, IReadOnlyList<EntityState> Entities) : Message;
                              // BaselineSnapshotId 0 = pełna; 1–8 ram, każdy stan wskazuje ramę indeksem
public sealed record SnapshotAck(uint SnapshotId) : Message;
public sealed record EntitySpawn(EntityId Entity, EntityKind Kind, ushort Archetype, PlayerId? Owner, SnapshotFrame Frame,
                                 EntityState Initial, byte[] KindData) : Message;                     // Initial.FrameIndex = 0 wskazuje Frame
// Archetype i KindData (najwyżej MaxEntityKindDataBytes) znaczą to, co podaje specyfikacja rodzaju bytu;
// gracz i budowla mają Archetype = 0 i puste KindData. EntitySpawn znanego bytu zastępuje kopię klienta.
public sealed record EntityDespawn(EntityId Entity) : Message;
public sealed record Ping(ulong ClientTimeMicros) : Message;
public sealed record Pong(ulong ClientTimeMicros, Tick ServerTick) : Message;
public sealed record Disconnect(DisconnectCode Code) : Message;
// Każdy rekord nadpisuje Type odpowiednią wartością.

[Flags] public enum InputButtons : ushort { None = 0, Jump = 1, Sprint = 2, Crouch = 4, Primary = 8, Secondary = 16 }
public readonly record struct PlayerInput(uint ClientTick, float MoveX, float MoveY, float Yaw, float Pitch, InputButtons Buttons);

public enum EntityKind : byte { Player = 1, Construct = 2 }
// zarezerwowane: DroppedItem = 3, Creature = 4, Projectile = 5, Tombstone = 6, Boss = 7 (podprojekty 3 i 4)
/// Rama w migawce. ParentSeq to licznik przepięć samej ramy (budowli); dla planety i układu 0.
public readonly record struct SnapshotFrame(FrameRef Frame, uint ParentSeq);
[Flags] public enum EntityStateFields : byte { None = 0, Position = 1, Rotation = 2, Velocity = 4, MotionFlags = 8 }
/// Pozycja, obrót i prędkość są w układzie ramy Frames[FrameIndex]; ParentSeq to licznik przepięć bytu.
public readonly record struct EntityState(EntityId Entity, EntityStateFields Fields, byte FrameIndex, uint ParentSeq,
                                          Vector3 Position, Quaternion Rotation, Vector3 Velocity, byte MotionFlags);
// Pole nieobecne w Fields ma wartość domyślną, a Rotation wtedy Quaternion.Identity.
// Klient odrzuca stan ze starszym ParentSeq niż znany, a nowszy wstrzymuje do operacji przepięcia (podprojekt 2).
```

- Tablice bajtów i listy w komunikatach należą do rekordu. Dekoder zawsze je kopiuje i nigdy nie trzyma wycinka bufora transportu.
- `MessageChannels.For(MessageType)` mapuje typ na kanał według tabeli i rzuca dla wartości spoza enuma. `MessageChannels.MaxFrameBytes(DeliveryChannel)` zwraca 256 KiB albo 1150 B.
- Format pierwszych pól `ClientHello` (typ, wersja formatu, `ProtocolVersion`) i całego `HandshakeReject` jest zamrożony na zawsze: dzięki temu klient z inną wersją zawsze dostanie czytelną odmowę (K1-04, K1-07).
- Kodowanie bajtowe tych rekordów definiuje K1-04.

Zakresy kodów dla kolejnych pakietów są przydzielone z góry, żeby Kosmos i Przetrwanie mogły powstawać równolegle. Kolumna K2–K4 należy do dalszych pakietów Planety; pierwszy kod z niej, komunikat `ResyncRequest` (21), dodaje K3-15. Test „złoty” sprawdza wartości z K1, a zakresy pilnuje przegląd.

| Enum | K1 | K2–K4 | Kosmos | Przetrwanie | Walka | Treść EA | Wydanie |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `OperationKind` | 1–7 | 8–19 | 20–29 | 30–49 | 50–59 | 60–69 | — |
| `RejectReason` | 0–20 | 21–29 | 30–39 | 40–59 | 60–69 | 70–79 | — |
| `MessageType` | 1–20 | 21–29 | 30–39 | 40–49 | 50–59 | 60–69 | 70–79 |
| `IntentCode` (K1-04) | 1–3 | 4–9 | 10–19 | 20–39 | 40–49 | 50–59 | — |
| `BlobKind` (K1-04) | 1–4 | 5–9 | 10–19 | 20–29 | 30–39 | 40–49 | — |

## Testy akceptacyjne (napisz najpierw)

`tests/Horyzont.Testing` dostaje: `FakeTerrainService` (sfera o promieniu R jako „teren” do raycastów, lista zastosowanych edycji, sterowany zbiór wczytanych obszarów), `InMemoryJournal` (lista wpisów), `WorldFactory.CreateDefault()` (przez `WorldState.CreateNew`: świat z jedną planetą, typami `core:stone_block` i `core:missing`, materiałami `stone`, `dirt`).

| Plik testów | Co sprawdza |
| --- | --- |
| `Ids/EntityIdAllocatorTests` | dla `EntityIdAllocator` i `PlayerIdAllocator`: `PeekNext` nie zmienia stanu; `MarkUsed` przesuwa tylko w górę; start od 1 |
| `World/Constructs/BlockStateTests` | pakowanie z wersją kosztu 0–15 i stanem funkcji 0–7; `Create` odrzuca typ 0, orientację 24, wersję kosztu 16 i stan funkcji 8; `WithFunctionState` zmienia tylko bity 25–27 i rzuca dla pustego bloku; `IsWellFormed` przyjmuje stany funkcji 0–7 i odrzuca bity 28–31 |
| `World/Constructs/CellPosTests` | chunk i indeks lokalny dla ujemnych (−1 → chunk −1, indeks 15 na osi); −2048 i 2047 w zakresie, −2049 i 2048 poza; `Offset` dla 6 ścian |
| `World/Constructs/ConstructTests` | ustawianie i czyszczenie, wersje, usuwanie pustych chunków; właściwość: `ParentToCell(CellCenterToParent(c)) == c` dla losowych obrotów i komórek |
| `World/Constructs/ConstructRaycastTests` | trafienie właściwej komórki i ściany; promień mijający budowlę; najbliższa z dwóch budowli |
| `World/Terrain/TerrainMathTests` | `AffectedBlocks` na granicy bloku obejmuje sąsiadów; wynik posortowany; ujemne współrzędne |
| `World/RegistryTests` | `CreateNew` nadaje numery od 1 i dopisuje `core:missing`; przekroczenie `MaxBlockTypes` i `MaxMaterials` rzuca |
| `Simulation/IntentValidationTests` | każdy `RejectReason` poza `None` osiągalny przynajmniej jednym przypadkiem; kolejność sprawdzeń z tego planu |
| `Simulation/IntentResolutionTests` | kopanie trafia w punkt raycastu; budowa na terenie daje `ConstructCreateOp` z komórką (0,0,0) na powierzchni i osią +Y wzdłuż `UpAt`; budowa na ścianie daje sąsiednią komórkę |
| `Simulation/ServerSimulationTests` | kolejne `OpId` od 1; wpis w dzienniku po każdej przyjętej intencji, brak po odrzuconej; limit tempa (szósta edycja terenu w jednym ticku odrzucona, po 12 tickach przyjęta, po 11 nie); usunięcie ostatniego bloku usuwa budowlę; `LeaveAllOnlinePlayers` wylogowuje wszystkich w kolejności `PlayerId` i trafia do dziennika; `UpdatePlayerKinematics` z NaN albo nieznormalizowanym obrotem zwraca `false` i nie zmienia stanu; `Faulted` po wyjątku z `ITerrainService.ApplyEdit` |
| `Simulation/ReplayTests` | powtórka dziennika na świeżym stanie daje ten sam skrót, a `CurrentTick` równy tickowi ostatniego wpisu; luka w `OpId` i niespełniony warunek wstępny dają `ReplayFailedException` z numerem |
| `Simulation/ReparentTests` | `EntityReparentOp` gracza do budowli z fotelem i z powrotem do planety; `NewParentSeq` różny od bieżący + 1, rama nieistniejąca, budowla przepinana do siebie albo do potomka i zajęty fotel dają złamany warunek; `BlockClear` komórki z siedzącym graczem odrzucone; `UpdatePlayerKinematics` ze starą ramą albo `ParentSeq` zwraca `false` |
| `Simulation/PlayerIdentityTests` | `PreparePlayerJoin` dla znanej tożsamości zwraca jej `PlayerId` i byt; nowa tożsamość dostaje `PlayerIds.PeekNext()`; dwie tożsamości nigdy nie dzielą `PlayerId`; `PlayerJoinOp` z cudzą tożsamością dla znanego gracza łamie warunek |
| `Checkpoints/CheckpointTests` | drugi checkpoint bez zmian zawiera tylko graczy online; `IsCheckpointInFlight` jest `true` od `CaptureCheckpoint` do zamknięcia, a `CaptureCheckpoint` w tym czasie rzuca; `RequeueAfterFailedCheckpoint` przywraca zmiany, także usunięcia; chunk usunięty w nieudanym checkpoincie i ponownie zapełniony trafia do następnego jako zmieniony, nie usunięty; stan z `WorldStateBuilder` (checkpointy plus powtórka ogona dziennika) ma ten sam skrót co stan na żywo, także z graczem online w checkpoincie i jego `PlayerLeave` w ogonie |
| `Hashing/DeterminismTests` | właściwość (FsCheck): ta sama losowa sekwencja poprawnych intencji na dwóch świeżych światach daje ten sam skrót; zmiana jednej komórki budowli, wersji bloku terenu, `IsOnline` gracza, ramy albo `ParentSeq` bytu albo `LastOpId` zmienia skrót |
| `Protocol/ProtocolContractTests` | wartości liczbowe wszystkich enumów protokołu i `RejectReason` zgodne z tym planem (test „złoty”); `MessageChannels` mapuje każdą wartość `MessageType`; każdy rekord komunikatu zwraca swój `Type` |
| `Simulation/AllocationTests` | `Submit` przyjętej `RemoveBlockIntent` i `AdvanceTick` nie alokują w stanie ustalonym poza rekordem operacji, `AppliedOperation` i `JournalEntry` |

## Kroki

- [x] `Horyzont.Testing`: `FakeTerrainService`, `InMemoryJournal`, `WorldFactory`.
- [x] Identyfikatory, `FrameRef`, `PlatformIdentity` i oba alokatory z testami.
- [x] `BlockState`, `CellPos`, `ChunkPos`, `Face`, `ConstructChunk`, `Construct`, `ConstructStore` z testami.
- [x] Rejestry, planety, gracze, `WorldState.CreateNew`, `TerrainBlockKey`, `TerrainVersionTable`, `TerrainMath`, `ITerrainService`.
- [x] Operacje, w tym `EntityReparentOp`, i `OperationApplier` z warunkami wstępnymi.
- [x] Intencje, `IntentValidator`, `IntentResolver`, `ConstructRaycast`, `TokenBucket`.
- [x] `ServerSimulation` z `Submit`, `ApplyServerOperation`, `PreparePlayerJoin`, `LeaveAllOnlinePlayers`, `Replay`, stanem `Faulted`.
- [x] Checkpoint: znaczniki zmian, `CaptureCheckpoint`, `CompleteCheckpoint`, `RequeueAfterFailedCheckpoint`, `WorldStateBuilder`.
- [x] `StateHasher` i testy determinizmu.
- [x] Kontrakty protokołu: stałe, enumy, rekordy komunikatów, `MessageChannels`, test „złoty”.
- [ ] Komentarze XML dla całego publicznego API; zielone CI.

## Odstępstwa i decyzje wykonawcy (do przeglądu GPT-6.1 Sol)

- **Chunki budowli w `Dictionary`, nie w `SortedDictionary`.** `SortedDictionary.TryGetValue` alokuje (32 B na wywołanie), a `Construct.GetBlock` działa w każdym kroku raycastu, więc test alokacji `Submit(RemoveBlockIntent)` nie przechodził. `Construct.Chunks` sortuje klucze przy wyliczaniu (skrót, checkpoint). `TerrainVersionTable` zostaje na `SortedDictionary`, bo nie leży na gorącej ścieżce.
- **`OpId.ToString()` i `Tick.ToString()` zwracają samą liczbę.** Domyślny `ToString` rekordu drukuje właściwość `Next`, która znowu ma `Next`: nieskończona rekurencja i przepełnienie stosu przy każdym komunikacie z `OpId` (wysadzało proces testów).
- **Usunięcie fotela albo ostatniego bloku ramy z dziećmi** walidator odrzuca kodem `IntersectsPlayer` (zakres K1 nie ma osobnego kodu; applier dalej rzuca przy złamanym warunku).
- **`IntersectsPlayer` przy stawianiu bloku** sprawdza kapsuły wszystkich graczy online na planecie, nie tylko gracza działającego (jak przy edycji terenu).
- **Prostopadłościan zajętych komórek** tylko rośnie do ponownego wczytania świata (zachowawczy, wystarcza do odrzucania w raycaście; `Build` liczy go od nowa). Nie wchodzi do skrótu.
- **`ConstructChanges`:** pusty dla `EntityReparent` (wersja bloków się nie zmienia), `PlayerJoin` i `PlayerLeave`; przy usunięciu ostatniego bloku wpis ma wersję po usunięciu i `Removed = true`.
- **Gracz bez planety (rama układu):** sprawdzenie `RayOriginMismatch` jest pomijane, a intencja dostaje `WrongPlanet` na swoim etapie. Nieznana wartość `Face` w `ConstructFaceTarget` i nieznormalizowany `LookForward` w `TerrainTarget` to `InvalidParameters` (sprawdzenia wspólne). Rzut `LookForward` ma dodatkowy, ostatni zapasowy kierunek: oś Z planety.
- **Punkt startu**, gdy raycast terenu nie trafi: (0, R + 0,1, 0). `PlayerJoin` znanego gracza aktualizuje nazwę i pozę, a zachowuje pochylenie i fotel.
- **Dodatkowe publiczne API** (potrzebne walidacji, testom i K1-05/K1-07): `CellPos.FromChunk`, `Construct.LocalToParent`/`ParentToLocal`/`TryGetChunk`/`ChunkCount`/`BoundsMin`/`BoundsMax`, `WorldState.TryToPlanetFrame`, `PlayerStore.TryGetByEntity`/`Count`, `ConstructStore.Count`, `TerrainVersionTable.Count`, `ConstructHeaderRecord.From`, `PlayerRecord.From`, `ReplayFailedException.Kind`.
- **Kolejność pracy:** testy akceptacyjne powstały w tej samej sesji co kod, ale po nim (nie przed). Wszystkie z tabeli istnieją i przechodzą; test „złoty” sprawdza dodatkowo, że każdy `OperationKind` ma rekord operacji.
- `ConstructChunkRecord`, `ClientHello`, `TerrainBlockData`, `EntitySpawn`: tablice w rekordach z wyłączonym CA1819 (rekord jest właścicielem tablicy, zgodnie z kontraktem dekodera).

## Odbiór

Wszystkie testy z tabeli przechodzą w CI; GPT-6.1 Sol przegląda pod kątem determinizmu, kompletności walidacji i zgodności z tym planem; odstępstwa od kontraktów opisane w tym pliku przed scaleniem, bo K1-04, K1-05 i K1-07 budują na nich.

## Pułapki

- `record` z `Vector3` porównuje liczby zmiennoprzecinkowe dokładnie; testy porównują rekordy po przejściu przez te same obliczenia, a nie z ręcznie wpisanymi wartościami.
- Rekordy z listami i tablicami (`ConstructChunkRecord`, komunikaty) porównują je przez referencję; testy porównują zawartość przez `SequenceEqual`.
- `Quaternion` z `System.Numerics` ma konwencję (X, Y, Z, W); przy przejściu do Godota (pakiety 9 i 11) uważaj na kolejność i na to, że Godot używa −Z jako „do przodu”.
- `SortedDictionary` alokuje przy dodawaniu; to akceptowalne przy edycjach (rzadkie), ale nie wolno iterować po nim w gorącej pętli bez potrzeby.
- Nie dodawaj tu nic, co wymaga Godota: test warstw z K1-02 to wychwyci.
- Pozycje w stanie, operacjach, checkpoincie i komunikatach są zawsze lokalne w ramie rodzica. Nie zapisuj nigdzie pozycji w świecie, nawet jako pomocniczego pola (`docs/ZASADY.md`, punkt 2).
- `PlayerId` to numer w świecie, a nie SteamID. Do sesji, banów i listy administratorów służy `PlatformIdentity`.
