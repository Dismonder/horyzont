# K2-09 · Adapter Voxel Tools: strumienie, wersje bloków, zrzut do checkpointu

| | |
| --- | --- |
| Prowadzi | Opus 5.5 (Claude Code) |
| Testy akceptacyjne | Opus 5.5 |
| Przegląd | GPT-6.1 Sol (Codex), wysiłek `high`; zmiany w `src/Horyzont.Core/` dodatkowo Opus 5.5 w świeżej sesji (nie sesja wykonawcy) |
| Zależy od | K1-05, K2-08 (z etapem K2-08A); warunki startu niżej (bramka K1 (PR #9: B1–B3), K1-02B) |
| Gałąź | `pakiet/k2-09-adapter-voxel` |

**Cel:** prawdziwa implementacja `ITerrainService` nad `VoxelLodTerrain`, w której dane każdego edytowanego bloku 16³ mają zawsze dokładnie tę wersję, którą zna `WorldState`. Nasz strumień Voxel Tools podaje bloki z bufora, z bazy albo z generatora, a checkpoint zapisuje bloki z wersjami spójnie z granicą dziennika, więc stan na żywo, pełna powtórka i checkpoint z ogonem dają te same bajty voxeli.

## Kontekst

- `docs/ZASADY.md`, punkty 2, 3, 4, 6, 7, 8
- `docs/spec/01-planeta.md`: Model świata (Teren), Stan, operacje i dziennik (`TerrainEdit`, „Transakcje i determinizm”), Zapis i odtwarzanie (Checkpoint, kroki 1–4; Odtwarzanie przy starcie, kroki 3–5), Obsługa błędów, Ryzyka (widoczność edycji z daleka, Voxel Tools prowadzony przez jedną osobę)
- `docs/plans/K1-03-core-stan-i-operacje.md`: `ITerrainService`, `TerrainVersionTable`, `WorldState.CaptureCheckpoint`/`CompleteCheckpoint`/`RequeueAfterFailedCheckpoint`, `ServerSimulation.Replay`, tabela zakresów kodów (`BlobKind` 5–9 dla K2–K4)
- `docs/plans/K1-04-serializatory.md`: `StateCodec`, nagłówek blobu (rodzaj, wersja formatu), fuzzing
- `docs/plans/K1-05-persistence.md`: `PersistenceWriter`, `CheckpointRequest`, `TerrainBlockPayload`, `TerrainBlockStore`, odstępstwo o `ArgumentException` dla niespójnych payloadów (linia 240), zdarzenia `CheckpointCommitted`/`CheckpointFailed`
- plan K2-08 (generator, konfiguracja `VoxelLodTerrain`, kanały voxeli, uprząż testów silnika)
- przegląd bramki K1: `AgentTools/logs/results/horyzont-k1-review.md` (B1, B2, E1, E2, lekcje 1–6)

Kod, na którym budujesz (main 884d40d):

- `src/Horyzont.Core/World/Terrain/ITerrainService.cs` — `IsAreaLoaded`, `LoadAreaBlocking`, `Raycast`, `ApplyEdit(TerrainEditOp)`
- `src/Horyzont.Core/World/Terrain/TerrainMath.cs` — `BlockSize = 16`, `AffectedBlocks(planet, center, radius, voxelSize, marginVoxels, output)` (tylko LOD 0, klucze posortowane)
- `src/Horyzont.Core/World/Terrain/TerrainVersionTable.cs` — `Get`, `All`, wewnętrzne `Bump` (oznacza blok do checkpointu)
- `src/Horyzont.Core/Simulation/OperationApplier.cs:95-107` — `ApplyTerrainEdit`: najpierw `Bump` wszystkich kluczy, potem `terrain.ApplyEdit(op)`
- `src/Horyzont.Core/Simulation/ServerSimulation.cs:159-189` — `Replay`: warunki wstępne, potem `LoadAreaBlocking` (poza `try`), potem `Apply`
- `src/Horyzont.Core/Simulation/IntentValidator.cs:139-143` — `IsAreaLoaded(center, radius + margines)` → `RejectReason.AreaNotLoaded`
- `src/Horyzont.Persistence/Terrain/TerrainBlockStore.cs` — `TryLoad(key, out version, out data)` z puli połączeń tylko do odczytu
- `src/Horyzont.Persistence/Writing/PersistenceWriter.cs:117-123, 219-245` — `CheckTerrainPayloads`: payload dla każdej wersji z `TerrainVersions` z tą samą wersją, nic ponadto
- `src/Horyzont.Core/Serialization/BlobKind.cs`, `StateCodec.cs:403-419` — nagłówek blobu
- `src/Horyzont.Core/Protocol/ProtocolConstants.cs:67` — `MaxTerrainBlockDataBytes = 64 KiB`
- `tests/Horyzont.Testing/FakeTerrainService.cs` — dane bloków jako XxHash64, `BlockSource`
- `F:/dev/horyzont-k1-02b/game` (gałąź `pakiet/k1-02b-godot`): `Horyzont.Game.csproj` (Godot.NET.Sdk 4.7.2, `GodotFloat64`, referencje do Core, Persistence, Net), `src/Boot/Boot.cs`
- `engine/engine.json` — Godot `4.7.2-stable`, Voxel Tools `v1.7`, `precision=double`

## Warunki startu

Pakiet nie rusza, dopóki na `main` nie ma (sprawdź `git log` i kod, nie pamięć):

1. **Bramka K1 (PR #9: B1–B3) scalona:** B2 — margines edycji jako `const` w ścieżce chronionej (`TerrainRules.EditMarginVoxels = 4` i `TerrainRules.MaxEditRadiusMeters = 16` w `src/Horyzont.Core/Simulation/TerrainRules.cs`; sprawdź, że na `main` są te nazwy) oraz limit promienia i `|Center|` w `CheckTerrainEdit`; B1 — `TerrainBlockStore.Close()` i kolekcja xUnit bez równoległości dla testów z pulą SQLite; B3 — skrót pozy graczy offline. Ten plan wszędzie, gdzie pisze „stała marginesu”, ma na myśli tę stałą; `GameRules.TerrainEditMarginVoxels` nie może być już używane.
2. **K1-02B scalony** (`game/` na `main`, job `game` w CI zielony na Windows i Linux).
3. **K2-08 scalony.** Przed krokiem 1 zapisz w „Odstępstwach” faktyczne nazwy kontraktów K2-08, z których korzystasz (tabela „Kontrakty pobierane z K2-08”); nazwy w tym planie są robocze.

Brak któregokolwiek → zatrzymaj się i eskaluj (ZASADY pkt 1), nie obchodź go w tym pakiecie.

## Zakres

- Gra (`game/src/Terrain/`): `VoxelTerrainService : ITerrainService`, `PlanetTerrain` (węzeł z `VoxelLodTerrain` jednej planety), nasz strumień `TerrainStream : VoxelStreamScript`, kodek bloku, ładowanie obszaru i powtórka dziennika z ładowaniem terenu, pamięć podręczna LOD > 0, skrót danych voxeli.
- Persistence (`src/Horyzont.Persistence/Terrain/`): bufor bloków zmienionych od checkpointu z wersjami, odczyt „bufor → baza” dla strumienia, składanie `CheckpointRequest`, obsługa zdarzeń checkpointu. Część bez Godota, testowana w zwykłym CI.
- Core (zmiany chronione, wąskie): wersje przekazywane do `ITerrainService.ApplyEdit`, publiczna czysta walidacja edycji `TerrainRules.CheckEdit` (decyzja bramki D2), `BlobKind.TerrainBlock = 5` i koperta blobu bloku terenu w `StateCodec`; przed jakąkolwiek zmianą Core zamrożony korpus złoty dzienników (decyzja bramki D1, krok 1).
- Wersjonowanie semantyki edycji terenu: ogon dziennika zapisany inną wersją semantyki nie jest powtarzany nową implementacją (decyzja bramki D1, „Stałe semantyki edycji”).
- Warunki wejścia K2-10 z przeglądu bramki, które dotyczą kopii zapasowych: **E1** i **E2** (patrz „Kopie zapasowe”). Pozostałe E3–E9 zostają w K2-10.
- Testy: `tests/Horyzont.Core.Tests`, `tests/Horyzont.Persistence.Tests/Terrain/`, testy silnika w uprzęży z K2-08.

Poza zakresem:

- generator, parametry planety, kanały voxeli, definicja mapy materiałów `TerrainMaterialMap` i konfiguracja mesh/LOD (K2-08; ten pakiet tylko stosuje mapę przy edycji, decyzja bramki D3);
- widzowie (`VoxelViewer`) graczy, kolizje postaci, umieszczenie węzłów w drzewie `/Root/Server` i `/Root/Client`, pętla checkpointu co 60 s, kolejność startu i zamknięcia serwera (K2-10; ten plan podaje kontrakty, które K2-10 składa);
- strumień klienta z cache `cache/<WorldId>/terrain.db`, podmiana bloków (`paste`) u klienta, wysyłka danych terenu, sumy kontrolne (K3-16; ten plan dostarcza kodek, `ITerrainBlockSource` i skrót, a krok rozpoznania sprawdza API podmiany);
- manipulator terenu i limity narzędzia (K2-13);
- trwały zapis danych LOD > 0 (patrz „LOD > 0” i Pytanie do właściciela 1).

## Projekt

### Pliki

```
src/Horyzont.Core/
  World/Terrain/ITerrainService.cs           ApplyEdit dostaje wersje (zmiana sygnatury)
  Simulation/TerrainRules.cs                 + CheckEdit (czysta walidacja, wspólna z TerrainReplay)
  Simulation/OperationApplier.cs             przekazuje już policzone wersje, CheckTerrainEdit woła TerrainRules.CheckEdit (bez zmiany semantyki)
  Serialization/BlobKind.cs                  TerrainBlock = 5
  Serialization/StateCodec.cs                EncodeTerrainBlockBlob / DecodeTerrainBlockBlob
  Serialization/TerrainBlockBlob.cs          rekord zdekodowanej koperty

src/Horyzont.Persistence/Terrain/
  ITerrainBlockSource.cs                     źródło bloków dla strumienia (serwer tu, klient w K3-16)
  TerrainBlockLookup.cs                      NotEdited, Found, Unavailable
  ServerTerrainBlocks.cs                     bufor zmienionych bloków + TerrainBlockStore + checkpoint
  TerrainBufferOptions.cs                    progi bufora, limit czasu zamknięcia
  TerrainLog.cs                              [LoggerMessage]

src/Horyzont.Persistence/Writing/PersistenceWriter.cs, Backup/BackupService.cs   E1, E2

game/src/Terrain/
  VoxelTerrainService.cs                     ITerrainService; rejestr planet; tylko wątek główny
  PlanetTerrain.cs                           Node3D z VoxelLodTerrain i VoxelToolLodTerrain jednej planety
  TerrainStream.cs                           VoxelStreamScript: LOD 0 z ITerrainBlockSource, LOD > 0 z DerivedLodCache
  TerrainBlockCodec.cs                       VoxelBuffer ↔ blob (VoxelBlockSerializer + koperta z Core), skrót
  TerrainEditSemantics.cs                    stałe parametrów narzędzia Voxel Tools (semantyka edycji)
  TerrainAreaLoader.cs                       ładowanie obszaru tymczasowym VoxelViewer, z terminem
  TerrainReplay.cs                           powtórka ogona dziennika z ładowaniem obszarów
  DerivedLodCache.cs                         ograniczona pamięć bloków LOD > 0 (tylko w sesji)
  TerrainDigest.cs                           skrót danych voxeli edytowanych bloków
  TerrainGameLog.cs                          [LoggerMessage]

game/tests/ (uprząż z K2-08)                 testy silnika z tabeli niżej
tests/Horyzont.Persistence.Tests/Terrain/    testy bufora, współbieżności i wstrzykiwania awarii
tests/Horyzont.Core.Tests/Golden/TerrainJournals/   korpus złoty dzienników (krok 1, zamrożony)
src/Horyzont.Persistence/Terrain/TerrainSemanticsMeta.cs   odczyt/zapis wersji semantyki terenu w tabeli meta
```

### Kto trzyma które bajty (decyzja główna)

Spec (Checkpoint, krok 2) mówi, że przy checkpoincie „dane zmienionych bloków terenu kopiuje się z Voxel Tools do pamięci”, a krok 4, że bloki wyładowane wcześniej trafiają do bufora przez strumień. Robimy to samo, ale **kopiujemy w chwili edycji, nie w chwili checkpointu**:

1. `OperationApplier` podbija wersje bloków (jak dziś) i woła `ApplyEdit(op, versions)`.
2. `VoxelTerrainService.ApplyEdit` sprawdza, że cały obszar bloków z `versions` jest edytowalny (inaczej rzuca, zanim cokolwiek zmieni), wykonuje edycję narzędziem Voxel Tools, od razu kopiuje każdy blok z `versions` z `VoxelLodTerrain` do `VoxelBuffer` 16³, koduje go do blobu i woła `ServerTerrainBlocks.Record(key, version, blob)`.
3. `ServerTerrainBlocks` trzyma dla każdego klucza najnowszą parę (wersja, blob) aż do zatwierdzenia checkpointu, który ją zawiera.

Dlaczego tak:

- Para wersja–dane powstaje w jednym miejscu, na wątku serwera, w tym samym wywołaniu co podbicie wersji. Nie ma okna, w którym Voxel Tools wyładował blok, zapis do strumienia jeszcze nie przyszedł, a ponowne wczytanie bierze starą wersję z bazy albo z generatora (kolejność zadań zapisu i odczytu w wątkach Voxel Tools nie jest naszym kontraktem).
- Checkpoint nie musi niczego kopiować z silnika: payload to wpisy bufora o wersjach z `WorldCheckpoint.TerrainVersions`. Spójność z dziennikiem wynika z konstrukcji, bo i wersje, i bufor zmienia tylko wątek serwera między tickami.
- Wymóg spec z kroku 4 jest spełniony strukturalnie: blok wyładowany przed checkpointem jest już w buforze.
- Koszt: promień narzędzia ≤ 3 m plus margines 4 voxeli daje zasięg 14 m, czyli najwyżej 2 bloki na oś, więc ≤ 8 kopii 16³ na edycję. Budżet sprawdza test wydajności.

Zapisy LOD 0, które Voxel Tools zgłasza strumieniowi przy wyładowaniu, strumień serwera ignoruje (bufor ma te same albo nowsze dane; w testach silnika sprawdzamy równość). Odrzucona alternatywa: własna edycja SDF w C# na surowych kanałach (pełna niezależność od Voxel Tools, ale wymaga masowego dostępu do kanałów i podmiany bloku w `VoxelLodTerrain`, czego nie znamy) — zostaje planem awaryjnym, jeśli rozpoznanie (krok 2, S3) pokaże niedeterministyczne edycje.

### Zmiany w Core (ścieżki chronione)

```csharp
namespace Horyzont.Core.World.Terrain;

public interface ITerrainService
{
    // bez zmian: IsAreaLoaded, LoadAreaBlocking, Raycast

    /// <summary>Applies an edit; deterministic for the same inputs. Called after the versions were bumped.</summary>
    /// <param name="edit">The edit.</param>
    /// <param name="versions">New versions of every LOD 0 block the edit may change, sorted by key
    /// (exactly <c>TerrainMath.AffectedBlocks</c> with the margin constant).</param>
    void ApplyEdit(TerrainEditOp edit, IReadOnlyList<TerrainBlockVersion> versions);
}
```

- `OperationApplier.ApplyTerrainEdit` przekazuje tablicę `versions`, którą już buduje. Kolejność „najpierw `Bump`, potem `ApplyEdit`” zostaje. To nie zmienia semantyki stosowania, więc **nie podbija** wersji formatu operacji — ale tylko pod warunkiem decyzji bramki D1: krok 1 najpierw zatwierdza osobnym commitem zamrożony korpus złoty (dzienniki + oczekiwane skróty `StateHasher` i listy wersji bloków nagrane z obecnego `main`), a `TerrainJournalGoldenCorpusTests` przechodzi po zmianie **bez zmian** w plikach korpusu i w pliku testu (`git diff <commit korpusu>..HEAD -- tests/Horyzont.Core.Tests/Golden/TerrainJournals tests/Horyzont.Core.Tests/TerrainJournalGoldenCorpusTests.cs` pusty; sprawdza przegląd). Bez takiego korpusu albo gdy test wymaga zmiany: podbicie wersji formatu operacji z powtórką starej wersji (ZASADY pkt 3). Dodatkowo złote skróty `DeterminismTests` bez zmian.
- Wspólna walidacja edycji (decyzja bramki D2, właściciel: ten pakiet): `OperationApplier.CheckTerrainEdit` jest dziś prywatne, a `TerrainReplay` musi sprawdzić wpis z dysku tym samym warunkiem przed ładowaniem obszaru. Wyciągamy jego część zależną tylko od operacji i planety do publicznej czystej funkcji w `TerrainRules`; `OperationApplier` zachowuje sprawdzenie planety w stanie i materiału w rejestrze, a resztę deleguje (te same komunikaty błędów, ta sama kolejność warunków; korpus złoty i testy appliera bez zmian).
- `FakeTerrainService.ApplyEdit(edit, versions)` porównuje klucze `versions` z własnym `AffectedBlocks` (ze stałą marginesu) i rzuca `InvalidOperationException` przy różnicy. Konstruktorowy parametr `marginVoxels` znika (po B2 margines jest stałą).

```csharp
namespace Horyzont.Core.Simulation;

public static class TerrainRules
{
    // istniejące: EditMarginVoxels = 4, MaxEditRadiusMeters = 16

    /// <summary>Checks the part of a terrain edit that depends only on the operation and its planet:
    /// shape and mode, finite centre and radius, radius in (0, MaxEditRadiusMeters], centre inside the atmosphere bound.</summary>
    /// <returns>True when valid; otherwise <paramref name="failure"/> holds the same message OperationApplier reports.</returns>
    public static bool CheckEdit(TerrainEditOp op, PlanetParams planet, out string? failure);
}
```

```csharp
namespace Horyzont.Core.Serialization;

public enum BlobKind : byte { /* 1–4 bez zmian */ TerrainBlock = 5 }

/// <summary>Decoded terrain block blob: voxel payload format and the payload, opaque to Core.</summary>
public sealed record TerrainBlockBlob(byte VoxelFormat, byte[] Voxels);

public static class StateCodec
{
    /// [BlobKind.TerrainBlock][CurrentFormatVersion][voxelFormat][u32 length][voxels];
    /// całość ≤ ProtocolConstants.MaxTerrainBlockDataBytes, inaczej ArgumentException
    public static byte[] EncodeTerrainBlockBlob(byte voxelFormat, ReadOnlySpan<byte> voxels);
    /// zły rodzaj, wersja, długość, voxelFormat == 0 albo całość ponad limit → ProtocolViolationException
    public static TerrainBlockBlob DecodeTerrainBlockBlob(ReadOnlySpan<byte> blob);
}
```

- Koperta jest blobem z bajtem wersji formatu (spec: „Każdy zapisany blob ma bajt wersji formatu”) i mieści się w `TerrainBlockData` (K3-16 wyśle te same bajty). `terrain_blocks.data` i `TerrainBlockPayload.Data` to od teraz cała koperta.
- Nowa wartość enuma z zakresu K2–K4 nie zmienia żadnej istniejącej ramki, więc `ProtocolVersion` zostaje; opis w PR, test złoty wartości `BlobKind` z K1-04 dopisuje 5, fuzzing K1-04 dostaje `DecodeTerrainBlockBlob`.
- `StateHasher` bez zmian: pakiet nie dodaje pól do `WorldState`. Dane voxeli, które odtwarza powtórka, mają własny skrót `TerrainDigest` z testem czułości i udziałem w teście live = powtórka = checkpoint + ogon (lekcja 2 przeglądu).

### Persistence: `ServerTerrainBlocks`

```csharp
namespace Horyzont.Persistence.Terrain;

public enum TerrainBlockLookup { NotEdited, Found, Unavailable }

/// <summary>Saved and pending terrain blocks for a voxel stream. Thread-safe; called from Voxel Tools worker threads.</summary>
public interface ITerrainBlockSource
{
    TerrainBlockLookup TryLoad(TerrainBlockKey key, out uint version, out byte[] blob);
}

public sealed record TerrainBufferOptions
{
    public int EarlyCheckpointBlocks { get; init; } = 4096;   // NeedsEarlyCheckpoint
    public int MaxPendingBlocks { get; init; } = 16384;       // IsSaturated
    public TimeSpan CloseTimeout { get; init; } = TimeSpan.FromSeconds(5);
}

public sealed class ServerTerrainBlocks : ITerrainBlockSource
{
    public ServerTerrainBlocks(TerrainBlockStore store, IEnumerable<TerrainBlockVersion> savedVersions,
                               TerrainBufferOptions options, TimeProvider time, ILogger<ServerTerrainBlocks> logger);

    public void Record(TerrainBlockKey key, uint version, byte[] blob);              // wątek serwera, po każdej edycji
    public TerrainBlockLookup TryLoad(TerrainBlockKey key, out uint version, out byte[] blob);   // dowolny wątek
    public CheckpointRequest CreateCheckpointRequest(WorldCheckpoint core);           // wątek serwera
    public void OnCheckpointCommitted(CheckpointRequest request);                    // wątek serwera
    public void OnCheckpointFailed(CheckpointRequest request);                       // wątek serwera
    public int PendingCount { get; }
    public bool NeedsEarlyCheckpoint { get; }
    public bool IsSaturated { get; }
    public TerrainBlockKey? FirstCorruptKey { get; }                                 // dowolny wątek
    public Task<bool> CloseAsync();                                                  // false: nie doczekał końca odczytów
}
```

Zasady:

- `savedVersions` to `WorldState.TerrainVersions.All` po wczytaniu: zbiór kluczy kiedykolwiek edytowanych. Klucz spoza zbioru i spoza bufora → `NotEdited` bez zapytania do SQLite (strumień oddaje blok generatorowi). `Record` dopisuje klucz do zbioru.
- Kolejność odczytu: bufor (pod blokadą) → `TerrainBlockStore.TryLoad` → przy kluczu edytowanym bez wiersza w bazie: `Unavailable`, zapis `FirstCorruptKey`, log błędu (niezmiennik złamany: wersja bez danych). Nigdy nie oddaje generatorowi bloku, który ma wersję.
- `Record` z wersją niższą lub równą wersji w buforze → `InvalidOperationException` (błąd w naszym kodzie). Blob jest od tej chwili niezmienny; nowa edycja zastępuje wpis nową tablicą.
- `CreateCheckpointRequest`: dla każdej wersji z `core.TerrainVersions` wpis bufora musi mieć tę samą wersję, inaczej `InvalidOperationException` (wywołujący woła wtedy `RequeueAfterFailedCheckpoint`; to błąd w kodzie, nie w danych). Payload zawiera dokładnie te wpisy, więc `CheckTerrainPayloads` writera zawsze przechodzi.
- `OnCheckpointCommitted`: usuwa z bufora tylko wpisy, których wersja równa się wersji z payloadu; nowsza edycja po granicy zostaje. Usunięcie następuje po zatwierdzeniu, więc odczyt, który nie trafi w bufor, widzi już nowy wiersz w bazie.
- `OnCheckpointFailed`: nic nie usuwa (Core sam ponownie oznacza bloki w `RequeueAfterFailedCheckpoint`).
- Limity (lekcja 6): `NeedsEarlyCheckpoint` przy `EarlyCheckpointBlocks` (pętla serwera zleca checkpoint, gdy żaden nie jest w locie); `IsSaturated` przy `MaxPendingBlocks` — wtedy `VoxelTerrainService.IsAreaLoaded` zwraca `false`, więc walidator odrzuca nowe edycje kodem `AreaNotLoaded` bez zmiany Core, a log ostrzega raz na wejście w nasycenie. Najgorszy przypadek bez limitu: 8 graczy × 5 edycji/s × 60 s × 8 bloków ≈ 19 tys. wpisów.
- `CloseAsync`: od tej chwili `TryLoad` zwraca `Unavailable` bez dotykania SQLite; czeka (przez `TimeProvider`, najwyżej `CloseTimeout`) na koniec trwających odczytów. Dopiero potem K2-10 woła `TerrainBlockStore.Close()`.

Przepływ checkpointu na pętli serwera (kontrakt dla K2-10, w testach tego pakietu jako pomocnik testowy):

```
core = state.CaptureCheckpoint();                        // na granicy ticku
request = blocks.CreateCheckpointRequest(core);
writer.CheckpointAsync(request);
CheckpointCommitted(r) → state.CompleteCheckpoint(r.Core); blocks.OnCheckpointCommitted(r);
CheckpointFailed(r)    → state.RequeueAfterFailedCheckpoint(r.Core); blocks.OnCheckpointFailed(r);
blocks.NeedsEarlyCheckpoint && brak checkpointu w locie → checkpoint teraz
```

### Gra: `VoxelTerrainService`, `PlanetTerrain`, `TerrainStream`

```csharp
namespace Horyzont.Game.Terrain;

public sealed class VoxelTerrainService : ITerrainService
{
    public VoxelTerrainService(ServerTerrainBlocks blocks, TerrainBlockCodec codec, TerrainMaterialMap materials,
                               ILogger<VoxelTerrainService> logger);   // mapa materiałów z K2-08 (D3)
    public PlanetTerrain AddPlanet(PlanetParams planet, VoxelLodTerrain configured);   // węzeł z fabryki K2-08
    public bool TryGetPlanet(PlanetId planet, [NotNullWhen(true)] out PlanetTerrain? terrain);
    public Task ShutdownAsync();                                                         // strumienie przestają czytać
    // ITerrainService: tylko wątek główny Godota (inny wątek → InvalidOperationException)
}

public sealed partial class PlanetTerrain : Node3D
{
    public PlanetId Planet { get; }
    public VoxelLodTerrain Terrain { get; }
}

public sealed partial class TerrainStream : VoxelStreamScript   // wywołania z wątków roboczych Voxel Tools
```

- **Układ współrzędnych:** `PlanetTerrain` stoi w środku planety z jednostkową bazą (planety się nie obracają), więc współrzędne voxeli = pozycja w ramie planety w metrach. `AddPlanet` rzuca `NotSupportedException`, gdy `PlanetParams.VoxelSizeMeters != 1` albo rozmiar bloku danych `VoxelLodTerrain` ≠ `TerrainMath.BlockSize`, oraz `InvalidOperationException`, gdy włączony jest tryb pełnego ładowania zamiast strumieniowania. Konwersja `System.Numerics.Vector3` (float) ↔ `Godot.Vector3` (double przy `GodotFloat64`) zawsze jawna, w jednym pomocniku.
- **`IsAreaLoaded(planet, center, radius)`:** `false`, gdy planeta nieznana albo `ServerTerrainBlocks.IsSaturated`; inaczej `is_area_editable` dla AABB `[center − radius, center + radius]` (sześcian, nie kula: `AffectedBlocks` liczy pudełko, więc musi być załadowane całe).
- **`ApplyEdit(op, versions)`:** (1) AABB sumy bloków z `versions` musi być edytowalne, inaczej `InvalidOperationException` przed jakąkolwiek zmianą (Voxel Tools pomija nieedytowalne obszary po cichu, a to rozjechałoby dane z wersjami); (2) edycja narzędziem z parametrami wyłącznie z `TerrainEditSemantics` i operacji (tryb, promień, środek, materiał przez `TerrainMaterialMap.ToTextureIndex` z mapy przekazanej w konstruktorze; definicja mapy w K2-08, zastosowanie tutaj); (3) kopia każdego bloku z `versions` do `VoxelBuffer`, `TerrainBlockCodec.Encode`, `ServerTerrainBlocks.Record`. Wyjątek w (2) albo (3) przechodzi do `ServerSimulation`, która przechodzi w stan awarii (zamierzone: spec „Wyjątek w ticku symulacji”).
- **`Raycast`:** narzędzie Voxel Tools z liczbą iteracji wyszukiwania binarnego z `TerrainEditSemantics`; punkt trafienia = początek + kierunek × odległość, normalna według ustaleń rozpoznania (S7). Wynik idzie do operacji, więc nie jest częścią powtórki. **Bez alokacji zarządzanej na wywołanie** (decyzja bramki D16): K2-11 i K2-13 celują co tick przez `ITerrainService.Raycast(..., out TerrainHit)`, więc narzędzie Voxel Tools jest trzymane w polu, a jeśli rozpoznanie (S7) pokaże, że `raycast` Voxel Tools alokuje obiekt wyniku, metoda robi własny marsz promienia po SDF (`get_voxel_f`, wyszukiwanie binarne, parametry w `TerrainEditSemantics`). Test silnika `Raycast_NoManagedAllocation`.
- **`LoadAreaBlocking`:** Voxel Tools nie ładuje synchronicznie, więc metoda tylko sprawdza `is_area_editable` i rzuca `InvalidOperationException("area not preloaded")`, gdy obszar nie jest gotowy. Ładowanie robi wcześniej `TerrainReplay` (niżej). Błąd poza `try` w `Replay` nie przełącza symulacji w stan awarii, a start serwera kończy się błędem z numerem operacji.
- **`TerrainStream`:** `_GetUsedChannelsMask` według kanałów K2-08. Wczytanie LOD 0: `ITerrainBlockSource.TryLoad` → `Found`: `TerrainBlockCodec.Decode` do `out_buffer`, wynik „znaleziono”; `NotEdited`: „nie znaleziono” (generator); `Unavailable` albo błąd dekodowania (`ProtocolViolationException`, odmowa deserializera): log z kluczem, wynik „błąd”, zgłoszenie do `VoxelTerrainService` (flaga sprawdzana na wątku głównym; reakcja: Pytanie do właściciela 2). LOD > 0: `DerivedLodCache`. Zapis LOD 0: ignorowany (w kompilacji testowej porównanie z buforem); zapis LOD > 0: do `DerivedLodCache`. Strumień nie dotyka drzewa sceny ani `WorldState`; bufory robocze (np. `StreamPeerBuffer`) w polach `ThreadLocal<T>` instancji, nie w polach statycznych. Po `ShutdownAsync` każde wywołanie zwraca „błąd” bez odczytu.

### Format bloku

`TerrainBlockCodec`:

- `byte[] Encode(VoxelBuffer block)` → `StateCodec.EncodeTerrainBlockBlob(TerrainEditSemantics.VoxelFormat, VoxelBlockSerializer(block, kompresja))`.
- `void Decode(ReadOnlySpan<byte> blob, VoxelBuffer into)`: najpierw koperta z Core (limity, wersje), potem deserializer Voxel Tools; nieznany `VoxelFormat` → `ProtocolViolationException`.
- `ulong Digest(VoxelBuffer block)`: XxHash64 serializacji **bez** kompresji (kanoniczne bajty, niezależne od kompresora).
- `VoxelFormat = 1` oznacza: serializer bloków Voxel Tools `v1.7`, kanały z K2-08. Zmiana kanałów albo serializera = nowy numer i odczyt starego w `Decode` (spec: czytniki obsługują stare wersje).

`TerrainDigest.Compute(IEnumerable<(TerrainBlockKey Key, uint Version, ulong VoxelDigest)>)`: XxHash64 z kolejnych kluczy w porządku `TerrainBlockKey`, wersji i skrótów bloków. Używają go testy tego pakietu, K3-16 (sumy kontrolne) i K4-19 (identyczność u wszystkich).

### Wczytanie i powtórka

```csharp
public sealed class TerrainAreaLoader
{
    public TerrainAreaLoader(VoxelTerrainService terrain, TimeProvider time, TimeSpan deadline);   // domyślnie 30 s
    public Task EnsureLoadedAsync(PlanetId planet, Vector3 center, float radius, CancellationToken ct);
}

public static class TerrainReplay
{
    /// For each entry: a terrain edit first loads its area, then ServerSimulation.Replay.
    public static Task ReplayAsync(ServerSimulation simulation, IReadOnlyList<JournalEntry> tail,
                                   TerrainAreaLoader loader, CancellationToken ct);
}
```

- `EnsureLoadedAsync` stawia tymczasowy `VoxelViewer` (bez wizualizacji i kolizji) w środku obszaru, czeka klatkami silnika, aż AABB będzie edytowalne, i usuwa widza. Przekroczenie terminu → `TimeoutException` z planetą i środkiem (lekcja 6: każdy stan oczekiwania ma termin).
- `ReplayAsync` przed ładowaniem odrzuca dane wrogie (lekcja 6, dziennik z dysku jak wejście z sieci): planetę z `simulation.State.Planets`, potem `TerrainRules.CheckEdit(op, planet, out failure)` — ta sama funkcja, którą woła `OperationApplier.CheckTerrainEdit` (D2; bez kopii stałych i warunków). Zły wpis → `ReplayFailedException` z numerem operacji, zanim powstanie widz. Kolejne edycje w już załadowanym obszarze nie stawiają nowego widza.
- Kolejność startu (spec, Odtwarzanie przy starcie, kroki 3–5), którą składa K2-10: `WorldLoader.Load` → sprawdzenie wersji semantyki terenu (niżej) → `ServerTerrainBlocks(store, state.TerrainVersions.All, …)` → `VoxelTerrainService` i `AddPlanet` dla planet z `state.Planets` → `TerrainReplay.ReplayAsync` → `LeaveAllOnlinePlayers()` → natychmiastowy checkpoint przez `CreateCheckpointRequest` → zapis bieżącej wersji semantyki w `meta` → dopiero potem przyjmowanie operacji. Kanoniczna kolejność startu i zamknięcia jest w K2-10 („Kolejność startu”, „Kolejność zamykania”).

### LOD > 0

Wersje i trwały zapis mają tylko bloki LOD 0 (`TerrainMath.AffectedBlocks`, kontrakt writera). Dane LOD > 0 to mipmapy wyliczane przez Voxel Tools z LOD 0, czyli dane pochodne. W tym pakiecie: `DerivedLodCache` trzyma bloki LOD > 0 zgłoszone przez Voxel Tools do zapisu i oddaje je przy ponownym wczytaniu w tej samej sesji (ograniczenie: `MaxBlocks` = 8192, najstarsze wypadają; utrata wpisu = blok z generatora, czyli tylko efekt wizualny). Po restarcie odległe LOD-y pokazują teren z generatora, dopóki Voxel Tools nie przeliczy ich z edytowanych bloków. Rozpoznanie (S5) mierzy, co dokładnie widać; decyzja o trwałym zapisie: Pytanie do właściciela 1. Odrzucone: wersje dla LOD > 0 w Core (algorytm mipmap Voxel Tools stałby się formatem operacji, a tabela wersji rosłaby o każdy poziom).

### Stałe semantyki edycji (lekcja 1)

`TerrainEditSemantics` (`game/src/Terrain/TerrainEditSemantics.cs`) zawiera jako `const` każdy parametr narzędzia Voxel Tools, od którego zależą bajty po edycji (tryb SDF, siła i skala SDF, sposób zapisu materiału, liczba iteracji raycastu), oraz `VoxelFormat`. Nic z tego nie jest konfigurowalne w czasie działania. Test złoty `EditSemantics_GoldenDigest` (płaski generator Voxel Tools, stała lista edycji, stały skrót w teście) pada przy zmianie tych stałych albo `voxel.ref` w `engine/engine.json`.

**Semantyka edycji terenu jest wersjonowana (decyzja bramki D1).** ZASADY pkt 3 wymagają identycznej powtórki, więc ogona dziennika nie wolno powtarzać inną implementacją edycji niż ta, która go zapisała:

- `TerrainEditSemantics.SemanticsVersion` (`const ushort`, start: 1) podbija się przy każdej zmianie stałych `TerrainEditSemantics`, `voxel.ref` w `engine/engine.json` albo wyniku profilu generatora K2-08 używanego przez edycję (profil jest wersjonowany w K2-08: nowy wynik = nowa nazwa profilu, nie zmiana starej). Test złoty `EditSemantics_GoldenDigest` trzyma wersję, dla której nagrano skrót: zmiana skrótu bez podbicia wersji pada.
- Ciąg semantyki zapisywany w tabeli `meta` (klucz `terrain_semantics`, `TerrainSemanticsMeta` w Persistence) to `"{SemanticsVersion}|voxel={voxel.ref}"`. Serwer zapisuje go po natychmiastowym checkpoincie startowym, zanim przyjmie pierwszą operację, więc każdy wpis dziennika po checkpoincie ma semantykę równą zapisanej w `meta` (awaria między checkpointem a zapisem `meta` zostawia pusty ogon).
- Przy starcie: zapisany ciąg ≠ bieżący i ogon dziennika zawiera choć jedną `TerrainEditOp` → **brak powtórki**. Świat otwiera się w trybie bezpiecznym K2-10 (stan z checkpointu, `RejectReason.ServerReadOnly`, brak zapisu) z jasnym błędem: która wersja zapisała ogon, która jest bieżąca, ile operacji i od którego `OpId` nie zostało powtórzonych. Odzyskanie z samego checkpointu (porzucenie ogona) to jawna akcja właściciela (opcja startu w K2-10, z kopią zapasową przed porzuceniem), nigdy domyślna. Ogon bez edycji terenu albo pusty → zwykła powtórka i zapis nowego ciągu po checkpoincie.
- Świat bez klucza `terrain_semantics` (utworzony przed K2-09) jest traktowany jak wersja 1 z bieżącym `voxel.ref` (pierwszy start po scaleniu).

Do ZASADY pkt 3 dopisujesz wiersz: `game/src/Terrain/TerrainEditSemantics.cs` i `voxel.ref` w `engine/engine.json` → „podbicie `SemanticsVersion` i aktualizacja skrótu złotego z opisem w PR; ogon dziennika zapisany inną wersją semantyki nie jest powtarzany (tryb bezpieczny)”. Do tego samego punktu: zmiana `OperationApplier.cs` albo `ITerrainService.ApplyEdit` bez podbicia formatu operacji tylko przy zamrożonym korpusie złotym dzienników, który przechodzi bez zmian.

### Kopie zapasowe (E1, E2 z przeglądu bramki)

- **E1:** `BackupAsync` sprawdza `reason` synchronicznie (`ArgumentException` u wywołującego, ten sam warunek co `BackupService`), a każdy wyjątek z kopii poza `SimulatedCrashException` jest logowany i daje `false` bez przełączenia writera w stan awarii (zgodnie z decyzją K1-05, linia 238).
- **E2:** zmierz czas kopii świata o realistycznym rozmiarze terenu (np. 50 tys. bloków po ~3 KB). Jeśli kopia blokuje partie dziennika dłużej niż 100 ms (spodziewane), kopia przechodzi z wątku zapisu na osobne zadanie z własnym połączeniem tylko do odczytu (migawka WAL: `BackupDatabase` w jednym kroku albo `VACUUM INTO`, wybór według pomiaru), a wątek zapisu dalej zatwierdza partie. Rotacja i numeracja kopii z K1-05 bez zmian, najwyżej jedna kopia naraz. Wynik pomiaru i wybór zapisz w „Odstępstwach”.

### Kontrakty dla innych pakietów

| Kontrakt | Plik | Kto używa |
| --- | --- | --- |
| `VoxelTerrainService : ITerrainService`, `AddPlanet`, `ShutdownAsync`; ITerrainService tylko na wątku głównym Godota, więc `ServerSimulation` działa na wątku głównym | `game/src/Terrain/VoxelTerrainService.cs` | K2-10, K2-13 (przez `ServerSimulation`) |
| `PlanetTerrain` (węzeł; w trybie hosta jedna instancja dla serwera i lokalnego klienta, bez drugiej kopii terenu) | `game/src/Terrain/PlanetTerrain.cs` | K2-10, K2-11 (widzowie, kolizje) |
| `ServerTerrainBlocks`: przepływ checkpointu, `NeedsEarlyCheckpoint`, `TryLoad` jako bieżąca para wersja–blob | `src/Horyzont.Persistence/Terrain/ServerTerrainBlocks.cs` | K2-10, K3-16 |
| `TerrainAreaLoader`, `TerrainReplay.ReplayAsync` | `game/src/Terrain/` | K2-10 (start), K3-16 |
| `ITerrainBlockSource`, `TerrainStream` | Persistence / game | K3-16 (źródło z cache klienta) |
| `TerrainBlockCodec`, `StateCodec.Encode/DecodeTerrainBlockBlob`, `TerrainDigest` | game / Core | K3-16, K4-19 |
| Kolejność zamknięcia: jedna kanoniczna w K2-10 („Kolejność zamykania”, decyzja bramki D8). Ten pakiet wnosi do niej `VoxelTerrainService.ShutdownAsync` → `ServerTerrainBlocks.CloseAsync` przed `TerrainBlockStore.Close()`; gdy `CloseAsync` zwróci `false` (odczyty wciąż trwają), `TerrainBlockStore.Close()` **nie jest wołane** — log i zamknięcie zostaje końcowi procesu | K2-10 | K2-10 |
| `TerrainRules.CheckEdit` (czysta walidacja edycji) | `src/Horyzont.Core/Simulation/TerrainRules.cs` | `OperationApplier`, `TerrainReplay` |
| `TerrainEditSemantics.SemanticsVersion`, `TerrainSemanticsMeta`, wynik sprawdzenia ogona (niezgodność → tryb bezpieczny) | game / Persistence | K2-10 (start, tryb bezpieczny, opcja odzyskania z checkpointu) |
| `Raycast` bez alokacji zarządzanej (D16) | `game/src/Terrain/VoxelTerrainService.cs` | K2-11 (celowanie), K2-13 (`ToolAim`, test zero alokacji) |

Kontrakty pobierane z K2-08 (nazwy robocze, obowiązują nazwy ze scalonego K2-08):

| Co | Do czego |
| --- | --- |
| fabryka skonfigurowanego `VoxelLodTerrain` dla `PlanetParams` (generator, mesher Transvoxel, LOD, Clipbox, rozmiar bloku 16) | `AddPlanet` przypina `TerrainStream` i opakowuje węzeł |
| układ kanałów voxeli (SDF, kanał materiału) i `TerrainMaterialMap` (`MaterialId` → indeks tekstury Voxel Tools) | `_GetUsedChannelsMask`, edycja `Add` z materiałem (mapa w konstruktorze `VoxelTerrainService`), `VoxelFormat` |
| uprząż testów silnika bez grafiki (spec: gdUnit4Net) w jobie `game` na Windows i Linux | testy silnika z tabeli |

Brak uprzęży w K2-08 → krok 3 dodaje gdUnit4Net (paczki w `Directory.Packages.props`: opis w PR i zielony test dymny silnika, ZASADY pkt 3); jeśli gdUnit4Net nie działa z naszą kompilacją silnika, własny runner `-- --engine-tests <filtr>` z kodem wyjścia, opisany w „Odstępstwach”.

## Testy akceptacyjne (napisz najpierw)

Czysty .NET (zwykłe CI, `FakeTimeProvider`; testy z `TerrainBlockStore` w kolekcji bez równoległości z B1, bez `ClearAllPools`):

| Test | Co sprawdza |
| --- | --- |
| `TerrainBlockBlobTests` | koperta w obie strony; zły rodzaj, wersja, długość, `VoxelFormat = 0` i przekroczenie `MaxTerrainBlockDataBytes` → `ProtocolViolationException` przy dekodowaniu, `ArgumentException` przy kodowaniu; nowy przypadek w fuzzingu K1-04; test złoty `BlobKind` z wartością 5 |
| `TerrainJournalGoldenCorpusTests` | korpus nagrany z `main` przed zmianami (krok 1): kilka dzienników z edycjami terenu (dodaj, usuń, granice bloków, ujemne współrzędne, wpisy odrzucane przez warunki) powtórzonych przez `ServerSimulation.Replay` na `FakeTerrainService` daje zapisane skróty `StateHasher` i listy `TerrainVersions.All`; pliki korpusu i testu bez zmian w diffie po kroku 1 |
| `TerrainRulesCheckEditTests` | każdy warunek `CheckEdit` osobno (kształt, tryb, NaN/∞, promień 0, `MaxEditRadiusMeters` i tuż ponad, środek na granicy i tuż ponad); te same komunikaty co dotychczasowe odrzucenia `OperationApplier` |
| `TerrainSemanticsMetaTests` | brak klucza → wersja 1; zapis i odczyt; ogon z `TerrainEditOp` przy innym ciągu → wynik „niezgodność” z wersjami, liczbą i pierwszym `OpId`; ogon bez edycji terenu → powtórka dozwolona |
| `OperationApplierTerrainTests` | `ApplyEdit` dostaje dokładnie podbite wersje, posortowane, po `Bump`; złote skróty `DeterminismTests` bez zmian |
| `ServerTerrainBlocksTests` | kolejność odczytu (bufor wygrywa z bazą); klucz nieedytowany → `NotEdited` bez zapytania do SQLite (licznik w teście); klucz z wersją bez wiersza → `Unavailable` i `FirstCorruptKey`; `Record` z nie-rosnącą wersją rzuca; `CreateCheckpointRequest` = dokładnie `TerrainVersions` i writer go przyjmuje; brak wpisu o tej wersji → `InvalidOperationException`; zatwierdzenie usuwa tylko wpisy o tej samej wersji, edycja po granicy zostaje; nieudany checkpoint nic nie usuwa |
| `ServerTerrainBlocksLimitTests` | progi `NeedsEarlyCheckpoint` i `IsSaturated`, powrót poniżej progu po zatwierdzeniu; `CloseAsync` czeka na trwający odczyt (bramka w teście), po zamknięciu `Unavailable` bez SQLite; termin zamknięcia → `false` |
| `ServerTerrainBlocksConcurrencyTests` | 8 wątków czyta losowe klucze podczas edycji, checkpointów i zatwierdzeń: każda para (wersja, blob) równa wyroczni dla tej wersji, a wersja na klucz w wątku nigdy nie maleje |
| `TerrainFaultInjectionTests` (właściwość) | `WorldScript` + dane `FakeTerrainService` zapisywane przez `ServerTerrainBlocks`, checkpoint co 20 operacji, każdy `FaultPoint`: awaria, wczytanie, nowe `ServerTerrainBlocks`, `BlockSource` = jego `TryLoad`, powtórka; dla każdego edytowanego klucza dane i wersja równe wyroczni po `LastOpId` |
| `BackupTests` (rozszerzenie) | E1: zły `reason` → `ArgumentException` u wywołującego, writer nie w stanie awarii, dziennik dalej trwały; wyjątek w kopii → `false` bez awarii. E2: kopia wstrzymana bramką w teście nie wstrzymuje `JournalCommitted` (jeśli pomiar przesunął kopię z wątku zapisu) |
| `BackupDurationReport` (`Manual`) | pomiar czasu kopii świata z 50 tys. bloków; wynik do PR |

Testy silnika (Godot bez grafiki, Windows i Linux, job `game`):

| Test | Co sprawdza |
| --- | --- |
| `Edit_Footprint_WithinVersionedBlocks` | 1000 losowych edycji (promień 0,5–3 m, dodaj i usuń, środki na granicach i narożnikach bloków): każdy zmieniony voxel w pudełku o jeden blok większym leży w bloku z `versions` |
| `Edit_CopyRightAfterApply_EqualsLaterRead` | kopia bloku tuż po edycji równa odczytowi po kilku klatkach (edycja synchroniczna) |
| `Edit_SameOpsDifferentLoadHistory_IdenticalDigest` | te same operacje na dwóch świeżych terenach, raz z blokami z generatora, raz po wyładowaniu i wczytaniu przez strumień → ten sam `TerrainDigest` |
| `Stream_UnloadReloadBeforeCheckpoint_ReturnsPendingData` | edycja, widz odchodzi (blok wyładowany), wraca: dane z bufora, skrót równy sprzed wyładowania |
| `Stream_UnloadReloadAfterCommit_ReturnsStoreData` | to samo po zatwierdzonym checkpoincie: dane z bazy, bufor pusty |
| `Checkpoint_BlocksUnloadedBeforeCapture_AreSaved` | blok wyładowany przed checkpointem jest w payloadzie i w bazie z właściwą wersją |
| `Restart_CheckpointPlusTail_EqualsLive` (teoria) | warianty: zamknięcie z checkpointem; awaria po `FlushAsync` bez checkpointu; checkpoint i ogon; `SimulatedCrashException` w `CheckpointMidTransaction`. Po wczytaniu, `TerrainReplay` i porównaniu: `TerrainDigest` wszystkich edytowanych bloków, wersje i skrót `StateHasher` równe stanowi na żywo |
| `FullReplay_FromEmptyCheckpoint_EqualsLive` | bez checkpointów: pełna powtórka dziennika na świeżym świecie daje ten sam `TerrainDigest` co na żywo |
| `Replay_HostileEditFromDisk_RejectedBeforeLoading` | wpis z promieniem albo środkiem poza limitem → `ReplayFailedException` z numerem operacji, żaden widz nie powstaje |
| `AreaLoader_Deadline_ThrowsTimeout` | obszar, który nie zdąży się załadować → `TimeoutException` po terminie |
| `LoadAreaBlocking_NotPreloaded_ThrowsWithoutFault` | `Replay` bez wcześniejszego ładowania rzuca, `ServerSimulation.IsFaulted == false` |
| `ApplyEdit_AreaNotEditable_ThrowsBeforeChange` | edycja przy niezaładowanym bloku z `versions` rzuca, a żaden voxel się nie zmienia |
| `IsAreaLoaded_Saturated_ReturnsFalse` | po przekroczeniu `MaxPendingBlocks` intencja terenu dostaje `AreaNotLoaded` |
| `TerrainDigest_Sensitivity` | zmiana jednego voxela, wersji albo klucza zmienia skrót; kolejność wejścia nie zmienia |
| `EditSemantics_GoldenDigest` | płaski generator, stała lista 50 edycji: skrót równy stałej w teście na Windows i na Linux |
| `Shutdown_StreamCallsAfterClose_ReturnErrorWithoutStore` | po `ShutdownAsync` i `CloseAsync` wywołania strumienia zwracają błąd, a `TerrainBlockStore.Close()` nie powoduje wyjątku w wątkach Voxel Tools |
| `Raycast_NoManagedAllocation` | 1000 wywołań `Raycast` po rozgrzaniu: `GC.GetAllocatedBytesForCurrentThread` bez zmian |
| `Restart_DifferentSemanticsVersion_SafeModeWithoutReplay` | ogon z edycją zapisany przy innym `terrain_semantics` → żadnego wywołania `ApplyEdit`, stan równy checkpointowi, wynik „niezgodność” z czytelnym komunikatem |
| `ApplyEdit_Radius3_TimeBudget` (`Slow`) | mediana ≤ 1 ms, p99 ≤ 3 ms na edycję z kopią i kodowaniem 8 bloków; wynik liczbowy w logu testu i w PR |

## Kroki

- [ ] 0. Warunki startu sprawdzone na `main` (bramka K1 (PR #9: B1–B3), K1-02B, K2-08 z K2-08A); faktyczne nazwy kontraktów K2-08 i stałej marginesu zapisane w „Odstępstwach”.
- [ ] 1. **Korpus złoty (D1), osobny commit przed jakąkolwiek zmianą w `src/`:** na niezmienionym `main` nagraj `tests/Horyzont.Core.Tests/Golden/TerrainJournals/` (dzienniki zakodowane kodekiem K1-04 + oczekiwane skróty `StateHasher` i listy wersji bloków) i `TerrainJournalGoldenCorpusTests`; test zielony na `main`. Od tej chwili pliki korpusu i testu są zamrożone.
- [ ] 2. Rozpoznanie Voxel Tools `v1.7` na gałęzi (kod rozpoznania nie trafia do PR poza testami). Wyniki w „Odstępstwach”:
  - S1: `VoxelStreamScript` w C# dostaje wywołania wczytania i zapisu z wątków roboczych; zwrócony blok jest widoczny w terenie;
  - S2: nazwy API serializera bloków w C#; bajty w obie strony identyczne; typowy i największy rozmiar blobu z kanałami K2-08 (musi być ≤ 64 KiB z kopertą);
  - S3: edycja na `VoxelLodTerrain` jest synchroniczna, deterministyczna i nie wychodzi poza `AffectedBlocks` ze stałą marginesu; jak narzędzie zapisuje materiał przy `Add`;
  - S4: `is_area_editable` i czas ładowania obszaru 14 m tymczasowym widzem bez grafiki;
  - S5: czy i kiedy Voxel Tools zapisuje bloki LOD > 0 po edycjach; co widać po ponownym wczytaniu i po restarcie;
  - S6: czy istnieje podmiana bloku w `VoxelLodTerrain` (`paste` albo odpowiednik) — tylko raport dla K3-16;
  - S7: raycast po terenie gładkim: precyzja punktu i skąd normalna.
  - **Stop:** S1, S2 albo S3 nie przechodzi → zatrzymaj pakiet i eskaluj do Opus 5.5 z raportem. Ślad edycji większy niż margines oznacza zmianę stałej w Core (format operacji), a niedeterminizm — plan awaryjny z własną edycją SDF; żadnej z tych zmian nie robisz w tym pakiecie bez decyzji.
- [ ] 3. Uprząż testów silnika z K2-08 (albo gdUnit4Net według „Kontraktów pobieranych”); job `game` uruchamia ją na Windows i Linux.
- [ ] 4. Testy z obu tabel (czerwone).
- [ ] 5. Core: `TerrainRules.CheckEdit` i delegacja z `OperationApplier.CheckTerrainEdit`; `BlobKind.TerrainBlock`, koperta w `StateCodec`, `TerrainBlockBlob`; sygnatura `ApplyEdit` z wersjami, `OperationApplier`, `FakeTerrainService`; złote skróty i korpus złoty z kroku 1 bez zmian (inaczej podbicie formatu operacji, D1). Przegląd diffu Core przez Opus 5.5 w świeżej sesji przed dalszymi krokami.
- [ ] 6. Persistence: `ITerrainBlockSource`, `TerrainBlockLookup`, `TerrainBufferOptions`, `ServerTerrainBlocks`, `TerrainSemanticsMeta`, `TerrainLog`.
- [ ] 7. Gra: `TerrainBlockCodec`, `TerrainEditSemantics`, `TerrainStream`, `PlanetTerrain`, `VoxelTerrainService` (mapa materiałów w konstruktorze, edycja z kopią, `IsAreaLoaded`, `Raycast` bez alokacji, `LoadAreaBlocking`, sprawdzenie wątku).
- [ ] 8. `TerrainAreaLoader`, `TerrainReplay` z odrzucaniem wrogich wpisów przez `TerrainRules.CheckEdit` i sprawdzeniem wersji semantyki ogona.
- [ ] 9. `DerivedLodCache`; raport S5 z zachowaniem odległych LOD-ów po restarcie (zrzuty ekranu w PR).
- [ ] 10. Zamknięcie: `ShutdownAsync`, `CloseAsync` (wynik `false` → bez `TerrainBlockStore.Close()`), zgodnie z kanoniczną kolejnością K2-10.
- [ ] 11. E1 i E2 (pomiar, ewentualne przeniesienie kopii z wątku zapisu).
- [ ] 12. Test złoty semantyki edycji z `SemanticsVersion`, test wydajności; dopisanie wierszy do ZASADY pkt 3 (semantyka terenu, korpus złoty) i reguły „ITerrainService tylko na wątku głównym Godota” do ZASADY pkt 2.
- [ ] 13. Komentarze XML w Core i Persistence; lokalnie `dotnet build`, `dotnet test --filter "Category!=Soak&Category!=Manual"`, `dotnet format --verify-no-changes`, `python tools/engine/build.py smoke`; CI zielone na gałęzi (Windows i Linux, z jobem `game`).
- [ ] 14. Po scaleniu: CI zielone na `main` (nie tylko na gałęzi; lekcja 4) i jeden zielony przebieg nocny (`Soak`) przed bramką K2. Krok niezrobiony przed scaleniem dostaje wpis „odroczone do X” (lekcja 5).

## Odbiór

Testy z obu tabel przechodzą w CI na Windows i Linux, a CI na `main` po scaleniu jest zielone. Przegląd GPT-6.1 Sol potwierdza: nie ma ścieżki, w której dane bloku w buforze, w bazie albo w Voxel Tools mają inną wersję niż ta, którą zna `WorldState`; blok edytowany nigdy nie wraca z generatora (wyładowanie, wczytanie, awaria, restart); payload checkpointu odpowiada dokładnie granicy dziennika. Zmiany Core ma przejrzane Opus 5.5 w osobnej sesji, a korpus złoty z kroku 1 przechodzi bez zmian. Raport rozpoznania (S1–S7), pomiar E2 i liczby z testu wydajności są w „Odstępstwach”.

## Pułapki

- Strumień Voxel Tools działa na wątkach roboczych: nie dotykaj drzewa sceny, `WorldState`, `ServerSimulation` ani węzłów; nie trzymaj `out_buffer` po powrocie; nie używaj pól statycznych na bufory.
- Voxel Tools po cichu pomija edycję w obszarze nieedytowalnym. Bez sprawdzenia przed edycją wersje się podbiją, a dane nie — rozjazd widoczny dopiero po restarcie.
- Wersja bez danych to uszkodzenie, nie „blok z generatora”. Oddanie generatorowi bloku z wersją > 0 cofa cudze wykopy bez śladu w logu.
- Nie zmieniaj bajtów blobu, który już trafił do `Record`: `CheckpointRequest` trzyma do niego referencję, a writer zapisuje go na innym wątku.
- Usuwaj wpis z bufora dopiero po `CheckpointCommitted` i tylko przy równej wersji; edycja między przechwyceniem a zatwierdzeniem musi przetrwać.
- `GodotFloat64`: `Godot.Vector3` ma składowe `double`, Core używa `float`. Konwertuj w jednym miejscu; niejawne rzutowania w kilku plikach rozjadą zaokrąglenia granic bloków.
- Współrzędne bloków ujemne: zawsze `floor`, nigdy dzielenie całkowite (−1 m to blok −1). Licz przez `TerrainMath`, nie lokalną kopią.
- Pule SQLite: testy z `TerrainBlockStore` w kolekcji bez równoległości; zakaz `ClearAllPools` (B1). `TerrainBlockStore.Close()` dopiero po `CloseAsync() == true`; przy `false` nie zamykaj magazynu (D8), inaczej wątek Voxel Tools dostanie `ObjectDisposedException`.
- Zwalnianie węzła `VoxelLodTerrain` może wywołać zapisy przez strumień: strumień musi już być zamknięty albo je bezpiecznie ignorować.
- Podbicie `voxel.ref` albo zmiana `TerrainEditSemantics` bez podbicia `SemanticsVersion` dałoby inny wynik powtórki ogona dziennika; test złoty to wychwyci, a niezgodny ogon kończy się trybem bezpiecznym, nie cichą powtórką (D1).
- Nie poprawiaj korpusu złotego ani jego testu po kroku 1, żeby „przeszło”: to jest dokładnie przypadek, w którym trzeba podbić format operacji.
- `ITerrainService` tylko na wątku głównym: jeśli K2-10 przeniesie symulację na inny wątek, `VoxelTerrainService` rzuci przy pierwszym wywołaniu — to celowe.

## Pytania do właściciela

1. **Odległe LOD-y po restarcie.** Wersje i trwały zapis mają tylko bloki LOD 0; LOD > 0 trzymamy w pamięci sesji. Po restarcie wykopy mogą być niewidoczne z daleka, dopóki Voxel Tools ich nie przeliczy (spec, Ryzyka: „widoczność dużych edycji z daleka”). Czy trwały zapis danych pochodnych LOD > 0 (nowa tabela i migracja, poza kontraktem wersji) robimy w K2-09, czy łącznie z klientami w K3-16 („własne przeliczanie LOD”)? Domyślnie w planie: K3-16, po raporcie S5.
2. **Uszkodzony blob terenu w trakcie gry.** Spec opisuje tylko uszkodzenie wykryte przy starcie. Teren wczytuje się leniwie, więc uszkodzony wiersz `terrain_blocks` wyjdzie dopiero w grze. Propozycja: tryb bezpieczny jak przy błędzie zapisu (serwer przestaje przyjmować zmiany, ostrzega, proponuje przywrócenie kopii przy następnym starcie). Zgoda?
3. **E1 i E2 w K2-09.** Rozstrzygnięte na bramce K1: E1–E2 należą do K2-09, E3–E9 do K2-10 (README, „Warunki wejścia E1–E9”). Otwarte zostaje tylko: E2 może zmienić decyzję K1-05 (kopie na wątku zapisu) — wynik pomiaru idzie do przeglądu.
4. **Doprecyzowanie spec, Checkpoint krok 2.** Dane bloków kopiujemy z Voxel Tools przy każdej edycji, nie przy checkpoincie (powód w „Kto trzyma które bajty”). Wynik jest ten sam, ale tekst spec mówi inaczej. Zgoda na zmianę zdania w spec po przeglądzie?
5. **Aktualizacja silnika a ogon dziennika.** Rozstrzygnięte na bramce K1: semantyka edycji terenu jest wersjonowana; ogon zapisany inną wersją semantyki (`SemanticsVersion` + `voxel.ref`) nie jest powtarzany, świat otwiera się w trybie bezpiecznym z jasnym błędem, a odzyskanie z samego checkpointu to jawna akcja właściciela (D1, „Stałe semantyki edycji”).

## Odstępstwa i decyzje wykonawcy

(wypełnia wykonawca: nazwy kontraktów K2-08, wyniki S1–S7, pomiar E2, liczby z testu wydajności, każde odejście od planu z powodem)
