# K1-04 · Serializatory binarne i fuzzing

| | |
| --- | --- |
| Prowadzi | Gemini 3.8 Flash (Antigravity, myślenie High) |
| Testy akceptacyjne | Sonnet 5.5 (pisze je przed implementacją) |
| Przegląd | Sonnet 5.5; format bajtowy dodatkowo Opus 5.5 |
| Zależy od | K1-03 |
| Gałąź | `pakiet/k1-04-serializatory` |

**Cel:** ręcznie pisane kodeki binarne dla operacji, intencji, komunikatów protokołu i rekordów checkpointu, zgodne z formatem z tego planu, odporne na dowolne bajty wejściowe.

## Kontekst

- `docs/ZASADY.md`, punkty 3, 4, 6, 7
- `docs/plans/K1-03-core-stan-i-operacje.md`: rekordy operacji, intencji, checkpointu, komunikatów i `ProtocolConstants`
- `docs/spec/01-planeta.md`, sekcja Sieć → Protokół

## Zakres

Przestrzeń nazw `Horyzont.Core.Serialization`: `ByteWriter`, `ByteReader`, `ProtocolViolationException`, `Quantization`, `OperationCodec`, `IntentCodec`, `MessageCodec`, `StateCodec`, `BlobKind`. Testy w `tests/Horyzont.Core.Tests/Serialization/` i generatory FsCheck w `tests/Horyzont.Testing/Generators/`.

Poza zakresem: kompresja danych terenu (to bajty nieprzezroczyste dla Core), logika sesji i różnicowania migawek (K1-07, pakiet 14).

## Projekt

### API

```csharp
public sealed class ProtocolViolationException(string reason) : Exception(reason);

/// Growable buffer reused across messages; never shrinks. One instance per connection or per writer thread.
public sealed class ByteWriter
{
    public ByteWriter(int initialCapacity = 1024);
    public ReadOnlySpan<byte> WrittenSpan { get; }
    public int Length { get; }
    public void Reset();
    public void WriteByte(byte v); public void WriteSByte(sbyte v); public void WriteBool(bool v);
    public void WriteUInt16(ushort v); public void WriteUInt32(uint v); public void WriteUInt64(ulong v);
    public void WriteVarUInt(ulong v); public void WriteVarInt(long v);
    public void WriteSingle(float v);                    // rzuca ArgumentException dla NaN i nieskończoności
    public void WriteHalf(float v);                      // rzuca ArgumentException dla NaN, nieskończoności i |v| > 65504
    public void WriteVector3(Vector3 v); public void WriteQuaternion(Quaternion q);   // rzuca, gdy |długość − 1| > 1e-3
    public void WriteGuid(Guid v); public void WriteString(string v, int maxBytes); public void WriteBytes(ReadOnlySpan<byte> v, int maxBytes);
}

public ref struct ByteReader(ReadOnlySpan<byte> data)
{
    public int Position { get; } public int Remaining { get; }
    // Odpowiedniki Write*; każda metoda rzuca ProtocolViolationException przy braku danych, złej wartości albo przekroczeniu limitu.
    public uint ReadVarUInt32(); public ushort ReadVarUInt16(); public int ReadVarInt32();   // varuint/varint z kontrolą zakresu typu
    public int ReadCount(int max);                       // varuint ≤ max, używane przed każdą listą
    public void EnsureEnd();                             // nadmiarowe bajty na końcu to błąd
}

public static class Quantization
{
    public static sbyte QuantizeAxis(float v);   public static float DequantizeAxis(sbyte q);       // −128 przy odczycie to błąd
    public static uint PackRotation(Quaternion q); public static Quaternion UnpackRotation(uint packed);
}

public static class OperationCodec { public static void Write(ByteWriter w, Operation op); public static Operation Read(ref ByteReader r); }
public static class IntentCodec    { public static void Write(ByteWriter w, Intent intent); public static Intent Read(ref ByteReader r); }

public static class MessageCodec
{
    public static void Write(ByteWriter w, Message message);     // ArgumentException, gdy ramka > MessageChannels.MaxFrameBytes(kanał typu)
    public static Message Read(ReadOnlySpan<byte> frame);        // limit kanału sprawdzany przed dekodowaniem; na końcu EnsureEnd
    public static bool TryReadProtocolVersion(ReadOnlySpan<byte> frame, out ushort version);   // tylko ramka ClientHello, bajty 2–3
}

public enum BlobKind : byte { ConstructHeader = 1, Player = 2, ConstructChunk = 3, PlanetParams = 4 }

public static class StateCodec
{
    // Rekordy bez prefiksu (w komunikatach i wewnątrz blobów).
    public static void WriteConstructHeader(ByteWriter w, ConstructHeaderRecord h); public static ConstructHeaderRecord ReadConstructHeader(ref ByteReader r);
    public static void WritePlayer(ByteWriter w, PlayerRecord p);                   public static PlayerRecord ReadPlayer(ref ByteReader r);
    public static void WritePlanet(ByteWriter w, PlanetParams p);                   public static PlanetParams ReadPlanet(ref ByteReader r);
    public static void WriteRegistryEntry(ByteWriter w, RegistryEntry e, int maxId); public static RegistryEntry ReadRegistryEntry(ref ByteReader r, int maxId);
    public static void WriteChunkBlocks(ByteWriter w, ReadOnlySpan<uint> blocks);   public static uint[] ReadChunkBlocks(ref ByteReader r);   // samo RLE
    public static void WriteChunk(ByteWriter w, ConstructChunkRecord c);            public static ConstructChunkRecord ReadChunk(ref ByteReader r, EntityId construct);   // ChunkPos + RLE

    // Bloby SQLite: [BlobKind][FormatVersion] + rekord; Decode sprawdza rodzaj, wersję i koniec danych.
    public static byte[] EncodeConstructHeaderBlob(ConstructHeaderRecord h); public static ConstructHeaderRecord DecodeConstructHeaderBlob(ReadOnlySpan<byte> blob);
    public static byte[] EncodePlayerBlob(PlayerRecord p);                   public static PlayerRecord DecodePlayerBlob(ReadOnlySpan<byte> blob);
    public static byte[] EncodeChunkBlob(ReadOnlySpan<uint> blocks);         public static uint[] DecodeChunkBlob(ReadOnlySpan<byte> blob);   // klucz chunku jest w kolumnach tabeli
    public static byte[] EncodePlanetBlob(PlanetParams p);                   public static PlanetParams DecodePlanetBlob(ReadOnlySpan<byte> blob);
}
```

`ByteWriter` i kodeki wywołane z niepoprawnymi danymi (NaN, za długi napis, za duża ramka) rzucają `ArgumentException`, bo to błąd w naszym kodzie. Odczyt z niepoprawnymi danymi rzuca wyłącznie `ProtocolViolationException`, bo to dane z zewnątrz; dotyczy to też `OverflowException`, `DecoderFallbackException` i każdego innego wyjątku z biblioteki, który trzeba zamienić.

### Typy podstawowe (little-endian)

| Typ | Kodowanie |
| --- | --- |
| `bool` | 1 bajt, 0 albo 1; inna wartość to błąd |
| `byte` | 1 bajt |
| `sbyte` (tylko osie ruchu) | 1 bajt; −128 to błąd |
| `ushort`, `uint`, `ulong` stałe | 2, 4, 8 bajtów; stosowane tylko tam, gdzie mówi o tym ten plan |
| varuint | LEB128, do 10 bajtów; błąd przy przepełnieniu 64 bitów i przy kodowaniu nadmiarowym (ostatni bajt równy 0 przy długości > 1); przy odczycie do `ushort` lub `uint` wartość poza zakresem typu to błąd |
| varint | zigzag, potem varuint; przy odczycie do `int` wartość poza zakresem to błąd |
| `float` | 4 bajty IEEE 754; przy odczycie NaN i nieskończoności to błąd |
| `Half` | 2 bajty (`BitConverter.HalfToUInt16Bits`); przy odczycie NaN i nieskończoności to błąd |
| `Vector3` | 3 × `float` |
| `Quaternion` (pełny) | 4 × `float` w kolejności X, Y, Z, W; przy odczycie \|długość − 1\| ≤ 1e-3, inaczej błąd |
| `Guid` | 16 bajtów, `Guid.TryWriteBytes` w układzie domyślnym, odczyt `new Guid(span)` |
| napis | varuint długość w bajtach + UTF-8; limit na pole; niepoprawny UTF-8 to błąd (`new UTF8Encoding(false, true)`) |
| bajty | varuint długość + bajty; limit na pole; wynik to nowa tablica |
| lista | varuint liczba elementów (limit na pole) + elementy |
| `T?` | `bool` obecności, potem wartość, gdy `true` |

Domyślna zasada: liczby całkowite bez znaku w rekordach K1-03 (`ushort`, `uint`, `ulong`) koduje się jako varuint z kontrolą zakresu typu. Wyjątki, kodowane stałą szerokością: `PlatformIdentity.Value`, `BuildHash`, `PlanetParams.Seed`, `TerrainBlockChecksum.Hash`, `ClientTimeMicros` (`ulong`); `ClientHello.ProtocolVersion` i `InputButtons` (`ushort`); `BlockState` (`uint`).

### Identyfikatory i typy Core

| Typ | Kodowanie |
| --- | --- |
| `WorldId` | `Guid` |
| `PlayerId`, `SystemId`, `PlanetId`, `EntityId`, `OpId`, `Tick` | varuint (`PlayerId` to kolejny numer w świecie) |
| `PlatformIdentity` | `Platform` (byte, wartość z enuma), `Value` (`ulong` stały, bo SteamID64 ma 17 cyfr) |
| `FrameRef` | `Kind` (byte, wartość z enuma), `Id` (varuint) |
| `SnapshotFrame` | `Frame` (`FrameRef`), `ParentSeq` (varuint) |
| `CellPos`, `ChunkPos` | 3 × varint; `CellPos?` jak każde `T?` |
| `BlockState` | `uint` stały; przy odczycie wymagane `IsWellFormed` (bity 28–31 zerowe; bity 25–27 to stan funkcji, dowolny 0–7) |
| `TerrainBlockKey` | `PlanetId`, `Lod` (byte), 3 × varint |
| `TerrainBlockVersion` | `TerrainBlockKey`, varuint wersja |
| `RegistryEntry` | varuint `Id` (1..`maxId`: 2048 dla typów bloków, 255 dla materiałów), klucz do 64 bajtów |
| `PlanetParams` | `Id`, `System`, `Seed` (`ulong` stały), `GeneratorProfile` (do 64 bajtów), sześć `float` w kolejności z rekordu |
| `ConstructHeaderRecord` | `Id`, `Parent`, `ParentSeq`, `Mode` (byte), `Origin`, `Rotation` (pełny), `Version` |
| `PlayerRecord` | `Player`, `Identity`, `DisplayName` (≤ 32 B), `Entity`, `Frame`, `ParentSeq`, `Seat` (`CellPos?`), `Position`, `BodyRotation` (pełny), `Pitch`, `IsOnline` |
| enumy (`Face`, `TerrainEditMode`, `RejectReason` i inne) | 1 bajt (`InputButtons`: 2 bajty); wartość spoza zdefiniowanych to błąd |
| enumy `[Flags]` | błąd, gdy ustawiony jest bit spoza maski: `InputButtons` 0x1F, `EntityStateFields` 0x0F |

Wyjątek od reguły enumów: `HandshakeReject.Code` przyjmuje przy odczycie dowolny bajt, bo starszy klient musi umieć pokazać odmowę z kodem dodanym później.

Kodek sprawdza wyłącznie poprawność typów. Reguły gry (granice komórek, orientacja w `PlaceBlockIntent`, promień edycji, `MaterialId` przy `Remove`) sprawdza walidator i applier z K1-03.

### Ramki

- Komunikat: `[MessageType: u8][FormatVersion: u8][payload]`. W wersji 1 każdy typ ma `FormatVersion = 1`; nieznany typ albo wersja to błąd.
- Operacja (w dzienniku i wewnątrz `OperationApplied`): `[OperationKind: u8][FormatVersion: u8][payload]`.
- Intencja (wewnątrz `IntentSubmit`): `[IntentCode: u8][FormatVersion: u8][payload]`, gdzie `IntentCode` to wewnętrzny enum kodeka: `TerrainTool = 1`, `PlaceBlock = 2`, `RemoveBlock = 3`. Cel bloku: `BlockTargetCode` `ConstructFace = 1`, `Terrain = 2`, potem jego pola.
- Blob SQLite: `[BlobKind: u8][FormatVersion: u8][rekord]`. Prefiks jest wyłącznie w blobach; w komunikatach rekordy stanu idą bez niego, bo wersję niesie ramka komunikatu.
- Ramka zamrożona na zawsze: `ClientHello` zaczyna się od `[01][FormatVersion][ProtocolVersion: u16]` w każdej wersji protokołu, a `HandshakeReject` ma zawsze postać `[03][01][Code: u8][Detail: napis do 256 bajtów]`. `TryReadProtocolVersion` czyta bajty 2–3 bez względu na `FormatVersion`.

### Pola komunikatów (wersja 1)

Pola idą w kolejności z rekordów w K1-03, z kodowaniem z tabel wyżej. Uściślenia:

| Komunikat | Ładunek |
| --- | --- |
| `ClientHello` | `ProtocolVersion` (`ushort` stały), `BuildHash` (`ulong` stały), `Identity` (`PlatformIdentity`), `DisplayName` (≤ 32 B), `Password` (≤ 64 B), `AuthTicket` (bajty ≤ 1024) |
| `ServerHello` | `World`, `ServerTick`, `TickRate` (byte), `YourPlayer`, `YourEntity`, `Planets` (≤ 64 × `PlanetParams`), `BlockTypes` (≤ 2048 × `RegistryEntry`), `Materials` (≤ 255 × `RegistryEntry`) |
| `HandshakeReject` | `Code` (byte, dowolna wartość), `Detail` (≤ 256 B) |
| `ClientReady` | pusty |
| `TerrainCacheReport` | `Planet`, `Entries` (≤ 4096 × `TerrainBlockVersion`) |
| `TerrainBlockData` | `Key`, `Version` (varuint), `Data` (bajty ≤ 64 KiB) |
| `TerrainChecksums` | `Planet`, `Entries` (≤ 1024 × `Key` + `Hash` jako `ulong` stały) |
| `ConstructState` | `Header` (`ConstructHeaderRecord`), `PartIndex`, `PartCount` (varuint w zakresie `ushort`; `PartCount ≥ 1`, `PartIndex < PartCount`), `Chunks` (≤ 12 × `ChunkPos` + RLE); każdy odczytany chunk dostaje `Key.Construct = Header.Id`; powtórzony `ChunkPos` to błąd; zapis chunku innej budowli rzuca `ArgumentException` |
| `ConstructRemoved` | `Construct` |
| `OperationApplied` | `Id`, `Tick`, ramka operacji, `ConstructChanges` (≤ 1024 × `Construct`, `Version` jako varuint, `Removed` jako `bool`; po `EntityId`, bez powtórzeń) |
| `IntentSubmit` | `ClientSeq` (varuint), ramka intencji |
| `IntentReply` | `ClientSeq` (varuint), `Reason`, `AppliedOpId` |
| `InputBatch` | `FirstSeq` (varuint), liczba wejść jako `byte` 1–5, wejścia: `ClientTick` (varuint), `MoveX`, `MoveY` (`sbyte`, niżej), `Yaw`, `Pitch` (`float`), `Buttons` (`ushort` stały) |
| `Snapshot` | `SnapshotId`, `ServerTick`, `BaselineSnapshotId`, `LastProcessedInputSeq` (varuint), `Frames` (1–8 × `SnapshotFrame`, bez powtórzeń), `Entities` (≤ 22 × `EntityState`); `FrameIndex` spoza tabeli ram to błąd |
| `SnapshotAck` | `SnapshotId` (varuint) |
| `EntitySpawn` | `Entity`, `Kind`, `Archetype` (varuint w zakresie `ushort`), `Owner` (`PlayerId?`), `Frame` (`SnapshotFrame`), `Initial` (`EntityState` z `FrameIndex` = 0), `KindData` (bajty ≤ 64) |
| `EntityDespawn` | `Entity` |
| `Ping` | `ClientTimeMicros` (`ulong` stały) |
| `Pong` | `ClientTimeMicros` (`ulong` stały), `ServerTick` |
| `Disconnect` | `Code` |

`EntityState`: `Entity`, `Fields` (byte), `FrameIndex` (byte), `ParentSeq` (varuint), potem tylko obecne pola w kolejności `Position` (`Vector3`, w ramie `Frames[FrameIndex]`), `Rotation` (skompresowany, `uint` stały), `Velocity` (3 × `Half`), `MotionFlags` (byte). Pole nieobecne dekoduje się jako wartość domyślna, a `Rotation` jako `Quaternion.Identity`.

`EntityReparentOp`: `Entity`, `NewParent` (`FrameRef`), `NewParentSeq` (varuint), `LocalPosition` (`Vector3`), `LocalRotation` (pełny), `LocalVelocity` (`Vector3`), `SeatCell` (`CellPos?`).

Chunk w RLE: varuint liczba przebiegów (1–4096), każdy przebieg to varuint długość ≥ 1 i `uint` wartość z `IsWellFormed`; suma długości dokładnie 4096, w kolejności `LocalIndex`. Postać kanoniczna: przebiegi maksymalne, więc dwa sąsiednie przebiegi o tej samej wartości to błąd.

Operacje i intencje: pola w kolejności z rekordów K1-03; `Quaternion` w operacjach i nagłówkach budowli jest pełny (4 × `float`), skompresowany jest tylko w `EntityState`.

Limity z `ProtocolConstants` gwarantują, że każdy poprawny komunikat mieści się w limicie swojego kanału. Migawka w najgorszym razie: nagłówek z liczbami elementów 29 B, 8 ram po najwyżej 16 B i 22 pełne `EntityState` po najwyżej 40 B, razem 1037 B. Zapas 113 B to miejsce na 3 B pól walki na byt z podprojektu 4 (22 × 43 B daje 1103 B). 12 chunków w najgorszym RLE (4096 przebiegów po 5 B) z nagłówkiem to około 246 KB.

### Kwantyzacja (`Quantization`)

- Oś ruchu: `sbyte q = (sbyte)Math.Clamp(MathF.Round(v * 127f), -127, 127)`, odczyt `q / 127f`. Klient kwantyzuje wejście przed własną predykcją, żeby symulował dokładnie te liczby, które dostanie serwer.
- Obrót „smallest three” w 32 bitach:
  - zapis sprawdza `|długość − 1| ≤ 1e-3` (inaczej `ArgumentException`) i normalizuje kwaternion; `idx` = indeks składowej o największej wartości bezwzględnej w kolejności X, Y, Z, W (przy remisie najniższy); gdy ta składowa jest ujemna, zmienia znak całego kwaternionu;
  - trzy pozostałe składowe `a`, `b`, `c` w kolejności X, Y, Z, W z pominięciem `idx`; każda: `n = (s + 0.70710678f) / 1.41421356f`, `k = Math.Clamp((int)MathF.Round(n * 1023f), 0, 1023)` (`MathF.Round` z domyślnym zaokrągleniem do parzystej);
  - `packed = (uint)idx << 30 | (uint)ka << 20 | (uint)kb << 10 | (uint)kc`, zapisane jako `uint` little-endian;
  - odczyt: `s = k / 1023f * 1.41421356f − 0.70710678f`, składowa `idx` = `MathF.Sqrt(MathF.Max(0, 1 − a² − b² − c²))`; gdy `a² + b² + c² > 1`, to błąd (poprawne kodowanie daje najwyżej około 0,76);
  - błąd kąta `2·acos(|q·q′|)` wynosi najwyżej około 0,28° (analitycznie), w symulacji 200 000 losowych obrotów do 0,22°.
- Prędkość: `Half` na każdą składową.

### Przykłady bajtów (testy „złote”)

`BlockSetOp(Construct: EntityId(5), Cell: (1, −1, 0), Block: BlockState.Create(1, 3))` jako operacja:

```
03 01 05 02 01 00 01 00 03 00
```

(`BlockSet` = 3, wersja 1, varuint 5, zigzag 1 → 2, zigzag −1 → 1, zigzag 0 → 0, `0x00030001` little-endian.)

`Ping(ClientTimeMicros: 1 000 000)` jako komunikat:

```
12 01 40 42 0F 00 00 00 00 00
```

## Testy akceptacyjne (pisze Sonnet 5.5, przed implementacją)

| Test | Co sprawdza |
| --- | --- |
| Generatory FsCheck | `Arbitrary` dla każdej operacji, intencji, komunikatu i rekordu checkpointu, generujące tylko wartości poprawne według K1-03 i w limitach z `ProtocolConstants`; kwaterniony znormalizowane (cztery składowe z N(0,1), potem normalizacja); pola nieobecne w `EntityState` mają wartości domyślne, a `Rotation` = `Quaternion.Identity`; `FrameIndex` wskazuje istniejącą ramę migawki |
| Round-trip (właściwość) | dla każdego typu `decode(encode(x))` równe `x` po kwantyzacji pól stratnych: osie ruchu dokładnie po `Quantization`, `Velocity` dokładnie po konwersji na `Half`, skompresowany obrót w granicy 0,3°; listy i tablice porównywane przez `SequenceEqual` |
| Stabilność bajtów (właściwość) | `encode(decode(encode(x))) == encode(x)` bajt w bajt dla wszystkich typów poza zawierającymi skompresowany obrót (`EntityState`, `Snapshot`, `EntitySpawn`): przy remisie dwóch największych składowych ponowne zakodowanie może wybrać inny indeks |
| Testy złote | dwa przykłady z tego planu plus co najmniej po jednym dla `TerrainEditOp`, `ClientHello`, `HandshakeReject`, `Snapshot` z jedną encją, `ConstructState` z jednym chunkiem; wartości bajtów policzone ręcznie i opisane w komentarzu |
| Ramki zamrożone | `TryReadProtocolVersion` czyta wersję z `ClientHello` o dowolnym `FormatVersion`; `HandshakeReject` z nieznanym kodem dekoduje się bez błędu |
| Limity | największy możliwy komunikat każdego typu (pełne listy, najdłuższe napisy, najgorszy RLE) mieści się w limicie kanału; `Write` za dużej ramki rzuca `ArgumentException`; `Read` ramki dłuższej niż limit kanału rzuca `ProtocolViolationException` |
| Ucięte dane (właściwość) | każdy właściwy prefiks poprawnego kodowania daje `ProtocolViolationException` |
| Losowe dane (właściwość) | tablice 0–2048 losowych bajtów i poprawne kodowania z losowo zmienionymi bajtami: dekoder zwraca obiekt albo rzuca `ProtocolViolationException`, nigdy inny wyjątek, nigdy nie zawiesza się ani nie alokuje więcej niż 4 × długość wejścia + 64 KiB + 16 KiB na każdy zdekodowany chunk |
| Przypadki brzegowe | varuint nadmiarowy i przepełniony; varuint ponad zakres `uint` i `ushort`; varint ponad zakres `int`; `sbyte` −128; liczba elementów powyżej limitu; niepoprawny UTF-8; `bool` = 2; `BlockState` z bitami zarezerwowanymi 28–31; bit spoza maski w enumach `[Flags]`; NaN i nieskończoność w każdym polu `float` i `Half`; nieznormalizowany kwaternion; RLE z sumą ≠ 4096 i z dwoma sąsiednimi przebiegami o tej samej wartości; powtórzony chunk w `ConstructState`; blob z innym `BlobKind`; nadmiarowe bajty na końcu ramki |
| Alokacje | kodowanie `Snapshot` z 8 ramami i 22 encjami do istniejącego `ByteWriter` nie alokuje; jego dekodowanie alokuje najwyżej 8 KiB |
| Najgorszy rozmiar migawki | migawka z 8 ramami o maksymalnych identyfikatorach i 22 pełnymi stanami z maksymalnymi wartościami varuint ma najwyżej 1150 B |
| Kwantyzacja | błąd kąta `2·acos(\|q·q′\|)` < 0,3° dla 10 000 losowych kwaternionów; oś ruchu: −1, 0, 1 kodowane dokładnie; `WriteHalf(70000)` rzuca `ArgumentException` |

W CI każda właściwość ma 2 000 przypadków; w testach nocnych (`Category=Soak`) 200 000.

## Kroki

- [ ] (Sonnet) Generatory i wszystkie testy z tabeli, czerwone.
- [ ] `ProtocolViolationException`, `ByteWriter`, `ByteReader` z typami podstawowymi.
- [ ] Typy Core i `Quantization`.
- [ ] `OperationCodec`, `IntentCodec`.
- [ ] `StateCodec` (rekordy, RLE chunków, bloby).
- [ ] `MessageCodec` dla wszystkich 20 typów, z limitami kanałów i `TryReadProtocolVersion`.
- [ ] Wszystkie testy zielone; komentarze XML; zielone CI.

## Odbiór

Testy z tabeli przechodzą w CI, przegląd Sonnet 5.5 bez uwag krytycznych, Opus 5.5 zatwierdza zgodność formatu z tym planem.

## Pułapki

- Nigdy nie alokuj tablicy o rozmiarze odczytanym z sieci przed sprawdzeniem limitu i liczby pozostałych bajtów.
- `Encoding.UTF8` domyślnie zastępuje błędne bajty znakiem zastępczym; użyj kodera, który rzuca, i zamień jego wyjątek na `ProtocolViolationException`.
- `BinaryPrimitives` zamiast `BitConverter` dla liczb całkowitych, żeby jawnie wymusić little-endian.
- Rzutowanie `(uint)` i `(int)` z wartości odczytanej z sieci bez sprawdzenia zakresu cicho obcina liczbę; `checked` rzuca `OverflowException` zamiast `ProtocolViolationException`. Sprawdzaj zakres jawnie.
- Zmiana formatu po scaleniu wymaga podbicia `FormatVersion` i zachowania odczytu starej wersji w Persistence.
