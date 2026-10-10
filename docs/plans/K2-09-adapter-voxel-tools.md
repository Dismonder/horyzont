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

- [x] 0. Warunki startu sprawdzone na `main` (bramka K1 (PR #9: B1–B3), K1-02B, K2-08 z K2-08A); faktyczne nazwy kontraktów K2-08 i stałej marginesu zapisane w „Odstępstwach”.
- [x] 1. **Korpus złoty (D1), osobny commit przed jakąkolwiek zmianą w `src/`:** na niezmienionym `main` nagraj `tests/Horyzont.Core.Tests/Golden/TerrainJournals/` (dzienniki zakodowane kodekiem K1-04 + oczekiwane skróty `StateHasher` i listy wersji bloków) i `TerrainJournalGoldenCorpusTests`; test zielony na `main`. Od tej chwili pliki korpusu i testu są zamrożone.
- [x] 2. Rozpoznanie Voxel Tools `v1.7` na gałęzi (kod rozpoznania nie trafia do PR poza testami). Wyniki w „Odstępstwach”:
  - S1: `VoxelStreamScript` w C# dostaje wywołania wczytania i zapisu z wątków roboczych; zwrócony blok jest widoczny w terenie;
  - S2: nazwy API serializera bloków w C#; bajty w obie strony identyczne; typowy i największy rozmiar blobu z kanałami K2-08 (musi być ≤ 64 KiB z kopertą);
  - S3: edycja na `VoxelLodTerrain` jest synchroniczna, deterministyczna i nie wychodzi poza `AffectedBlocks` ze stałą marginesu; jak narzędzie zapisuje materiał przy `Add`;
  - S4: `is_area_editable` i czas ładowania obszaru 14 m tymczasowym widzem bez grafiki;
  - S5: czy i kiedy Voxel Tools zapisuje bloki LOD > 0 po edycjach; co widać po ponownym wczytaniu i po restarcie;
  - S6: czy istnieje podmiana bloku w `VoxelLodTerrain` (`paste` albo odpowiednik) — tylko raport dla K3-16;
  - S7: raycast po terenie gładkim: precyzja punktu i skąd normalna.
  - **Stop:** S1, S2 albo S3 nie przechodzi → zatrzymaj pakiet i eskaluj do Opus 5.5 z raportem. Ślad edycji większy niż margines oznacza zmianę stałej w Core (format operacji), a niedeterminizm — plan awaryjny z własną edycją SDF; żadnej z tych zmian nie robisz w tym pakiecie bez decyzji.
- [x] 3. Uprząż testów silnika z K2-08 (albo gdUnit4Net według „Kontraktów pobieranych”); job `game` uruchamia ją na Windows i Linux.
- [x] 4. Testy z obu tabel (czerwone).
- [ ] 5. Core: `TerrainRules.CheckEdit` i delegacja z `OperationApplier.CheckTerrainEdit`; `BlobKind.TerrainBlock`, koperta w `StateCodec`, `TerrainBlockBlob`; sygnatura `ApplyEdit` z wersjami, `OperationApplier`, `FakeTerrainService`; złote skróty i korpus złoty z kroku 1 bez zmian (inaczej podbicie formatu operacji, D1). Przegląd diffu Core przez Opus 5.5 w świeżej sesji przed dalszymi krokami.
- [x] 6. Persistence: `ITerrainBlockSource`, `TerrainBlockLookup`, `TerrainBufferOptions`, `ServerTerrainBlocks`, `TerrainSemanticsMeta`, `TerrainLog`.
- [x] 7. Gra: `TerrainBlockCodec`, `TerrainEditSemantics`, `TerrainStream`, `PlanetTerrain`, `VoxelTerrainService` (mapa materiałów w konstruktorze, edycja z kopią, `IsAreaLoaded`, `Raycast` bez alokacji, `LoadAreaBlocking`, sprawdzenie wątku).
- [x] 8. `TerrainAreaLoader`, `TerrainReplay` z odrzucaniem wrogich wpisów przez `TerrainRules.CheckEdit` i sprawdzeniem wersji semantyki ogona.
- [x] 9. `DerivedLodCache`; raport S5 z zachowaniem odległych LOD-ów po restarcie (zrzuty ekranu w PR).
- [x] 10. Zamknięcie: `ShutdownAsync`, `CloseAsync` (wynik `false` → bez `TerrainBlockStore.Close()`), zgodnie z kanoniczną kolejnością K2-10.
- [x] 11. E1 i E2 (pomiar, ewentualne przeniesienie kopii z wątku zapisu).
- [x] 12. Test złoty semantyki edycji z `SemanticsVersion`, test wydajności; dopisanie wierszy do ZASADY pkt 3 (semantyka terenu, korpus złoty) i reguły „ITerrainService tylko na wątku głównym Godota” do ZASADY pkt 2.
- [ ] 13. Komentarze XML w Core i Persistence; lokalnie `dotnet build`, `dotnet test --filter "Category!=Soak&Category!=Manual"`, `dotnet format --verify-no-changes`, `python tools/engine/build.py smoke`; CI zielone na gałęzi (Windows i Linux, z jobem `game`).
- [ ] 14. (CI na `main` zielone 2026-10-10: przebieg 38011077413 na `bcef9df`, po scaleniu całego K2; Engine na `main` i przebieg nocny — w toku bramki K2) Po scaleniu: CI zielone na `main` (nie tylko na gałęzi; lekcja 4) i jeden zielony przebieg nocny (`Soak`) przed bramką K2. Krok niezrobiony przed scaleniem dostaje wpis „odroczone do X” (lekcja 5).

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

### Etap 1: część niezależna od silnika (2026-10-09, Opus 5.5, gałąź `pakiet/k2-09-adapter-voxel`)

**Zakres etapu.** Na polecenie prowadzącego zrobione zostały kroki 1, 5, 6 i 11, czysto .NET-owa część kroku 4 (pierwsza tabela testów) oraz z kroku 12 wiersze ZASADY. Wszystko pod `game/src/Terrain/` i testy silnika (kroki 2, 3, 7–10, reszta 12, test `EditSemantics_GoldenDigest`, `ApplyEdit_Radius3_TimeBudget`) czekają na scalenie K2-08. Krok 0 nie jest odhaczony: K2-08 nie jest jeszcze na `main`, więc nazwy kontraktów K2-08 nie są zapisane. Na `main` są już `TerrainRules.EditMarginVoxels = 4` i `TerrainRules.MaxEditRadiusMeters = 16` (B2) w `src/Horyzont.Core/Simulation/TerrainRules.cs`. Krok 5 ma kod i zielone testy, ale nie jest odhaczony: czeka na przegląd diffu Core przez Opus 5.5 w świeżej sesji.

**Korpus złoty (krok 1, D1).** Commit `cad1f70` powstał przed jakąkolwiek zmianą w `src/`. Zawiera pięć dzienników w `tests/Horyzont.Core.Tests/Golden/TerrainJournals/`: `add_remove`, `block_boundaries`, `negative_coordinates`, `rejected_entries` i `mixed_long` (200 edycji). Każdy plik `*.journal` to kontener testowy (bajt wersji kontenera 1, liczba wpisów, a dla każdego wpisu id, tick, opcjonalny aktor i ramka `OperationCodec` z K1-04). Każdy plik `*.expected.txt` to raport powtórki przez `ServerSimulation.Replay` na `FakeTerrainService`: skrót `StateHasher` po każdym wpisie, odrzucenia z pełnym komunikatem `ReplayFailedException` (nieznana planeta, promień 0, ujemny i tuż ponad 16 m, środek tuż ponad granicą, zły materiał, tick wstecz, luka w numeracji), końcowe `LastOpId`/tick/skrót, liczba edycji i bloków, skrót XxHash64 danych bloków `FakeTerrainService` oraz pełna lista `TerrainVersions.All`. NaN i nieskończoności nie trafiły do korpusu, bo kodek K1-04 odmawia ich zapisu (`ByteWriter.WriteSingle`). Rejestrator `TerrainJournalGoldenCorpusRecorder` (`Manual`) zapisuje pliki tylko wtedy, gdy ustawiona jest zmienna `HORYZONT_GOLDEN_TERRAIN_OUT`, i służy wyłącznie do nagrania nowego korpusu przy przyszłym podbiciu formatu. `.gitattributes` oznacza `tests/**/Golden/**` jako binarne (bez konwersji końców linii). Po zmianach Core polecenie `git diff cad1f70..HEAD -- tests/Horyzont.Core.Tests/Golden/TerrainJournals tests/Horyzont.Core.Tests/TerrainJournalGoldenCorpusTests.cs` daje pusty wynik, a złote skróty `DeterminismTests` się nie zmieniły. Wersja formatu operacji nie została więc podbita.

**Core (krok 5).**
- `TerrainRules.CheckEdit(op, planet, out string? failure)` zwraca `failure == null`, gdy edycja jest poprawna. `OperationApplier.CheckTerrainEdit` sprawdza kolejno: planetę w stanie, `CheckEdit`, materiał. Komunikaty i ich kolejność się nie zmieniły (pilnują tego korpus i `TerrainRulesCheckEditTests`).
- Koperta bloku ma 7 bajtów nagłówka, więc ładunek voxeli może mieć najwyżej 65 529 B. Dekoder kopiuje voxele do nowej tablicy, a nadmiarowe i brakujące bajty odrzuca przez porównanie z polem długości.
- K1-04 nie miało testu złotego wartości `BlobKind`. Dopisałem go do `ProtocolContractTests.Enums_GoldenValues` (wartości 1–5).
- `FakeTerrainService` dostał `AppliedVersions`, `OnApplyEdit` (wołane przed edycją, pozwala sprawdzić stan w chwili wywołania) i `AfterApplyEdit` (wołane po edycji, służy do zapisu danych do `ServerTerrainBlocks` w testach). Nikt poza nim samym nie używał parametru `marginVoxels`.
- W `game/` nie ma żadnej implementacji `ITerrainService` (sprawdzone grepem), więc zmiana sygnatury nie wymagała tam poprawek.

**Persistence (krok 6).**
- `ServerTerrainBlocks.Record` porównuje nową wersję z najnowszą znaną wersją klucza (zapisaną albo nagraną), a nie tylko z wpisem bufora. To silniejszy warunek niż w planie: łapie też zapis wersji ≤ zapisanej w bazie.
- Wyjątek SQLite przy odczycie daje `Unavailable` i log błędu (`TerrainLog.ReadFailed`), ale nie ustawia `FirstCorruptKey`, bo to błąd odczytu, a nie dowód uszkodzenia. `FirstCorruptKey` dostaje tylko klucz z wersją bez wiersza.
- `TerrainSemanticsMeta` ma API oparte na ścieżce pliku: `Format`, `Read(path, currentVoxelRef)` (brak klucza → `"1|voxel={currentVoxelRef}"`), `Write(path, semantics)` we własnej transakcji na własnym połączeniu bez puli (writer może być wtedy otwarty; WAL i `busy_timeout` szeregują zapisy; obejmuje to test `Write_WhileTheWriterIsOpen_...`) oraz `CheckTail`. Wynik `TerrainTailCheck` podaje obie wersje semantyki, liczbę i pierwszy `OpId` niepowtórzonego ogona, a także liczbę i pierwszy `OpId` edycji terenu, z komunikatem z `Describe()`.
- Kolekcji xUnit bez równoległości z B1 nie ma na `main` (jest tylko zakaz `ClearAllPools` w `TempDirectory`). Dodałem kolekcję `SqlitePools` (`DisableParallelization = true`) w `tests/Horyzont.Persistence.Tests/Support/SqlitePools.cs`. Są w niej nowe testy z `TerrainBlockStore` i `BackupDurationReport`. Istniejących `TerrainBlockStoreTests` nie przenosiłem.
- `TerrainBlockStore` ma wewnętrzny licznik zapytań `QueryCount` i hak `BeforeQuery` (bramka w testach `CloseAsync`). Publiczne API się nie zmieniło.
- Testy z tabeli: `ServerTerrainBlocksTests`, `ServerTerrainBlocksLimitTests`, `ServerTerrainBlocksConcurrencyTests` (8 wątków, wyrocznia par wersja–blob, monotoniczność na wątek) i `TerrainFaultInjectionTests` (właściwość, 25 przypadków, każdy `FaultPoint`, nowe `ServerTerrainBlocks` po awarii). Wszystkie używają pomocnika `Support/TerrainServer.cs` z przepływem checkpointu z planu.
- `CheckpointTests` odwoływał się do `Terrain.TerrainBlockStore` przestrzenią względną, która po dodaniu `Horyzont.Persistence.Tests.Terrain` przestała się rozwiązywać. Zmieniłem na `Persistence.Terrain.TerrainBlockStore` i nie ruszałem nic więcej w tym teście.

**Kopie zapasowe (krok 11).**
- E1: `BackupAsync` sprawdza `reason` synchronicznie przez `BackupService.ValidateReason`, tę samą regułę co `CreateBackup`. Dotąd zły znak w `reason` rzucał wyjątek na wątku zapisu i przełączał writer w stan awarii. Każdy wyjątek kopii poza `SimulatedCrashException` jest logowany i daje `false`; `finally` gwarantuje, że wywołujący zawsze dostanie wynik.
- E2, pomiar (`BackupDurationReport`, `Manual`, Windows 11, dysk lokalny): świat z 50 000 bloków po 2,8–3,3 KB losowych danych zajmuje 222,7 MiB. Trzy przebiegi dały:
  - `BackupService` na połączeniu zapisu: 1364, 1222 i 1218 ms;
  - `BackupDatabase` z osobnego połączenia tylko do odczytu: 1339, 1253 i 1175 ms;
  - `VACUUM INTO` z osobnego połączenia tylko do odczytu: 1794, 1696 i 1823 ms.
- E2, decyzja: kopia blokowała partie dziennika ponad 1 s, czyli znacznie dłużej niż próg 100 ms. Przeniosłem ją z wątku zapisu do puli wątków. Używa osobnego połączenia tylko do odczytu bez puli i `BackupDatabase` w jednym kroku (migawka WAL). Wybrałem `BackupDatabase`, bo jest szybszy od `VACUUM INTO` i zostawia bez zmian kod `BackupService` (nazwy, numeracja, rotacja, `journal_mode = DELETE`). Wątek zapisu przed startem kopii zatwierdza bieżącą partię, więc kopia zawiera dziennik co najmniej do chwili żądania. Kopie są łańcuchowane, więc w danej chwili trwa najwyżej jedna. `DisposeAsync` czeka na trwającą kopię, a wewnętrzne `ProbeAsync` (tylko testy) czeka też na wcześniej zlecone kopie. Test `BackupAsync_CopyInProgress_JournalKeepsCommitting` pokazuje, że `JournalCommitted` przychodzi, gdy kopia stoi na bramce. Ta zmiana odwraca decyzję K1-05 o kopiach na wątku zapisu i idzie do przeglądu (Pytanie 3).

**Poza tym etapem.** `python tools/engine/build.py smoke` nie był uruchamiany, bo etap nie dotyka silnika ani `engine/`. Krok 13 (CI na gałęzi) i krok 14 czekają na resztę pakietu.

**Poprawki po przeglądzie (2026-10-09).**
- Korpus złoty nie może zawierać wpisu „nieznany kształt lub tryb”, bo `OperationCodec` nie potrafi go zakodować. Ta gałąź jest pokryta tylko przez `TerrainRulesCheckEditTests`.
- Uwaga przeglądu „Core podbija wersje, zanim bufor je zna”: przyjęta, to nie jest wada. `Bump`, `ApplyEdit` i `Record` wykonują się w jednym wywołaniu wątku serwera, gdy edytowane bloki są wczytane i edytowalne (`ApplyEdit` to sprawdza), więc równoległe wczytanie tych kluczy przez strumień nie może być w toku dla wczytanego bloku. Wczytanie w tym oknie zwraca poprzednią parę, spójną z danymi voxeli w tej chwili.
- Reguła `TryLoad` (ponowne sprawdzenie): `ServerTerrainBlocks` trzyma najnowszą znaną wersję każdego klucza (z `savedVersions`, podnoszoną przez `Record`). Po chybieniu bufora i zapytaniu do SQLite odczyt pod blokadą sprawdza ponownie: wpis w buforze wygrywa; zmiana znanej wersji od początku próby powtarza całe wyszukiwanie (najwyżej 3 próby, potem `Unavailable` i log błędu); wiersz o wersji innej niż znana (albo brak wiersza) daje `Unavailable`, `FirstCorruptKey` i log błędu. `TryLoad` nigdy nie zwraca pary o wersji niższej od znanej w chwili powrotu.

### Etap 2: adapter Voxel Tools (2026-10-09, Opus 5.5, gałąź `pakiet/k2-09-adapter-voxel` po scaleniu K2-08 i K2-12)

**Krok 0, kontrakty K2-08 (faktyczne nazwy).** K2-08 nie jest jeszcze na `main`; ten etap korzysta z gałęzi K2-08 scalonej do gałęzi pakietu (`7a0866d`). Stała marginesu: `TerrainRules.EditMarginVoxels = 4`, limit promienia `TerrainRules.MaxEditRadiusMeters = 16`.

| Nazwa robocza w planie | Nazwa w kodzie K2-08 |
| --- | --- |
| fabryka skonfigurowanego `VoxelLodTerrain` | `PlanetTerrainFactory.Create(PlanetParams, PlanetTerrainOptions, Material? = null)` (`game/src/Planet/PlanetTerrainFactory.cs`); serwer: `PlanetTerrainOptions.Server`; układ: `PlanetTerrainLayout` (`DataBlockSize = 16`, granice `BoundsHalfExtentVoxels`) |
| układ kanałów | `TerrainChannels.Sdf` (`ChannelSdf`, 16 bitów), `TerrainChannels.Material` (`ChannelIndices`, 8 bitów), `TerrainChannels.UsedChannelsMask`, `TerrainChannels.CreateFormat()` |
| mapa materiałów | `TerrainMaterialMap.Build(MaterialRegistry)`, `ToTextureIndex(byte)`, `TryGetMaterialId(byte, out byte)`; klucze `TerrainMaterials.Keys` (`core:stone`, `core:dirt`, `core:sand`, `core:ore`, `core:bedrock`) |
| uprząż testów silnika | gdUnit4Net (`dotnet test game/Horyzont.Game.csproj --settings game/.runsettings` z `GODOT_BIN`), `game/tests/Support/EngineTestScene` (`Create`, `Add`, `WaitFrames`, `WaitFramesUntil`) |

K2-12 wnosi też własny runner `-- --engine-tests` (`game/src/Testing/EngineTestRunner.cs`). Testy K2-09 są w uprzęży gdUnit4Net, a runner K2-12 dalej przechodzi (18 przeszło, 0 nie, 4 pominięte).

**Krok 2, rozpoznanie S1–S7.** Kod rozpoznania nie trafił do PR. Wyniki pochodzą z przebiegu na edytorze `double` Windows: planeta `core:planet/1`, ziarno galaktyki `0x484F52595A4F4E54`, powierzchnia nad +X przy x = 3628.

- **S1 (przechodzi).**
  - API `VoxelStreamScript` w C#: `_LoadVoxelBlock(VoxelBuffer outBuffer, Vector3I positionInBlocks, int lod) → int`, `_SaveVoxelBlock(VoxelBuffer, Vector3I, int)`, `_GetUsedChannelsMask() → int`. Wynik wczytania to `VoxelStream.ResultCode`: `Error = 0`, `BlockNotFound = 1`, `BlockFound = 2`. Pozycja jest w blokach danego LOD-u, nie w voxelach.
  - Wątki: przy wejściu do drzewa przyszło 2639 wywołań wczytania z 8 wątków roboczych, żadne z wątku głównego (LOD 0: 2318, LOD 1–10: 321).
  - Blok zwrócony jako „znaleziony” jest widoczny w terenie (`GetVoxelF`, materiał). `outBuffer` to tymczasowe opakowanie; Voxel Tools przenosi jego dane do terenu po powrocie.
  - Po `ResultCode.Error` Voxel Tools 1.7 drukuje błąd i zostawia w terenie pusty bufor, który staje się edytowalny (patrz niżej, krok 7, błąd strumienia).
- **S2 (przechodzi).**
  - API: `VoxelBlockSerializer.SerializeToByteArray(VoxelBuffer, Compression)` i `DeserializeFromByteArray(byte[] | ReadOnlySpan<byte>, VoxelBuffer, bool decompress)`.
  - Kodowanie, dekodowanie i ponowne kodowanie dają identyczne bajty, z zstd i bez kompresji (64 bloki przy powierzchni po edycjach).
  - Rozmiar z zstd: mediana 6418 B, maksimum 7011 B. Bez kompresji 12 315 B. Najgorszy przypadek (losowe bajty obu kanałów): 12 330 B z zstd. Z kopertą (7 B) to daleko poniżej 64 KiB.
  - Odmowa deserializera nie rzuca wyjątku ani nie zwraca wyniku: tylko drukuje błąd i zostawia bufor bez zmian. Dlatego `TerrainBlockCodec.Decode` zmniejsza bufor do 1³ przed dekodowaniem, a potem sprawdza rozmiar 16³ i głębie kanałów (`ProtocolViolationException`).
- **S3 (przechodzi).**
  - Synchroniczność: `VoxelTool.DoSphere` na `VoxelLodTerrain` zmienia dane od razu, kopia zaraz po wywołaniu widzi zmianę. Jedna z 300 edycji niczego nie zmieniła (usuwanie w powietrzu).
  - Determinizm: te same 300 edycji na dwóch świeżych terenach i w dwóch procesach daje ten sam skrót obszaru 96³.
  - Ślad: 300 edycji (promień 0,5–3 m, środki na ścianach, krawędziach i narożnikach bloków) zmieniło 43 261 voxeli, z czego 0 poza blokami z `AffectedBlocks` ze stałą marginesu. Z kodu Voxel Tools: pudełko edycji to `[floor(c − r) − 2, ceil(c + r) + 2)`, czyli najwyżej r + 3 voxeli od środka na każdej osi. To mniej niż margines 4.
  - Materiał: tryb SDF `Add` zapisuje wyłącznie kanał SDF; `TextureIndex` nic nie zmienia. Tryb `TexturePaint` pisze wagi 4 indeksów, czyli inny układ niż K2-08. Dlatego edycja `Add` to dwa wywołania: kula SDF, potem ta sama kula na kanale `ChannelIndices` z `Value` = indeks tekstury. Voxel Tools ustawia tę wartość dla voxeli ściśle wewnątrz kuli.
- **S4.** `VoxelTool.IsAreaEditable(Aabb)` jest prawdziwe, gdy wczytane są wszystkie bloki LOD 0 pudełka (`VoxelData.is_area_loaded`). Obszar 14 m wokół świeżego widza (bez grafiki, odległość 128) był edytowalny po 29 klatkach, 234 ms. Uwaga: pudełko poza granicami terenu jest przycinane i wtedy jest „edytowalne” z definicji. Nie dotyczy to planet K2-08 z granicami ±16384.
- **S5.**
  - Przy wyładowaniu Voxel Tools zapisuje przez strumień bloki LOD 0 zmienione edycją oraz te bloki LOD > 0, których mipmapy przeliczył po edycjach.
  - Liczba zapisów LOD > 0 zależy od tego, czy przeliczenie mipmap zdążyło przed wyładowaniem. Pierwszy przebieg po odejściu widza: LOD 0: 116, LOD 1: 23, LOD 2: 8, LOD 3: 4, LOD 4: 4. Drugi przebieg (inny moment wyładowania): LOD 0: 116, LOD 1: 1.
  - Po powrocie widza bloki LOD 0 z zapisu dają identyczny skrót obszaru jak przed wyładowaniem.
  - Wniosek dla `DerivedLodCache`: w sesji odległe LOD-y wracają z pamięci, o ile Voxel Tools je zapisał. Po restarcie pochodzą z generatora, dopóki Voxel Tools nie przeliczy ich z wczytanych bloków LOD 0.
  - Zrzutów ekranu brak: rozpoznanie i testy działają bez grafiki (`--headless`). Wizualna ocena odległych LOD-ów zostaje przy Pytaniu do właściciela 1 (K3-16).
- **S6.** Podmiana bloku istnieje: `VoxelTool.Paste(Vector3I dst, VoxelBuffer src, int channelsMask)` na `VoxelToolLodTerrain`. Sprawdza edytowalność, wkleja i zgłasza edycję (przeliczenie siatki i mipmap); sprawdzone wklejeniem bloku 16³. Jest też `PasteMasked`. To raport dla K3-16.
- **S7.**
  - `VoxelTool.Raycast` po terenie gładkim robi przejście DDA po voxelach, potem wyszukiwanie binarne po interpolowanym SDF (`SetRaycastBinarySearchIterations`). Normalna pochodzi z gradientu SDF (`SetRaycastNormalEnabled`).
  - Metoda zwraca obiekt `VoxelRaycastResult` (RefCounted), czyli 408 B alokacji zarządzanej na wywołanie. Dlatego `VoxelTerrainService.Raycast` robi własny marsz (D16).
  - Własny marsz: krok `RaycastStepMeters = 0,5 m`. Próbka to trilinearna interpolacja 8 wartości `VoxelTool.GetVoxelF(Vector3I)` (0 B alokacji na wywołanie). Po zmianie znaku 10 iteracji wyszukiwania binarnego (`RaycastBinarySearchIterations`, ok. 0,5 mm). Normalna z różnic centralnych tej samej próbki (±1 m); przy zerowym gradiencie normalna jest przeciwna do kierunku promienia. Punkt startowy w materiale daje trafienie w odległości 0.
  - Na płaskim gruncie błąd punktu i odległości jest poniżej 1 cm, a normalna ma `y > 0,999` (test `Raycast_FlatGround_PreciseHitAndNormal`).

**Kroki 3–4 (testy).**
- Uprząż to gdUnit4Net z K2-08, bez zmian w paczkach.
- Testy silnika są w `game/tests/Terrain/`: `TerrainCodecTests`, `TerrainEditTests`, `TerrainStreamTests`, `TerrainReplayTests`, `TerrainSemanticsTests` (30 przypadków).
- Pomocnik `TerrainTestWorld` składa plik świata, `ServerTerrainBlocks`, prawdziwy `VoxelTerrainService`, teren z fabryki K2-08 z płaskim generatorem `VoxelGeneratorFlat` na wysokości 4000 m (promień planety), writer, symulację i przepływ checkpointu z planu. Do tego `CrashFaults`.
- Testy i kod powstały w jednym kroku; najpierw testy nie kompilowały się, bo brakowało typów. Że testy łapią błędy, sprawdziłem mutacją: pominięcie `Record` dla jednego bloku na edycję dało 10 czerwonych testów. Zielone zostały tylko te, które nie czytają bufora, np. pełna powtórka bez checkpointu.
- `Edit_Footprint_WithinVersionedBlocks` liczy zmienione voxele w pudełku o blok większym niż bloki z wersjami: 1000 edycji, 0 zmian poza wersjami.
- `IsAreaLoaded_Saturated_ReturnsFalse` przechodzi przez prawdziwy walidator intencji (`AreaNotLoaded`) i sprawdza powrót po zatwierdzonym checkpoincie.
- `Restart_CheckpointPlusTail_EqualsLive` ma cztery warianty z planu: zamknięcie z checkpointem; awaria po `FlushAsync` bez checkpointu; checkpoint i ogon; `SimulatedCrashException` w `CheckpointMidTransaction` drugiego checkpointu. Porównuje wersje, `StateHasher` i `TerrainDigest`.

**Odstępstwo: `LoadAreaBlocking_NotPreloaded_ThrowsWithoutFault`.**
- Plan zakłada, że `ServerSimulation.Replay` woła `LoadAreaBlocking` poza `try`. W Core (od K1-03, bez zmian w tym pakiecie) wywołanie jest wewnątrz `try`. Test Core `TerrainEditLimitsTests.Replay_TerrainLoadFails_FaultsSimulation` wprost wymaga stanu awarii po błędzie ładowania.
- Plan i kod są sprzeczne, więc Core nie zmieniłem (ZASADY pkt 1 i 3). Test silnika nazywa się `LoadAreaBlocking_NotPreloaded_Throws` i sprawdza tylko wyjątek `InvalidOperationException` („not preloaded”) bez zmiany stanu.
- W praktyce `TerrainReplay` zawsze ładuje obszar przed `Replay`, więc ta ścieżka oznacza błąd w kodzie.
- **Decyzja prowadzącego (Opus 5.5, 2026-10-09):** zostaje jak w Core — awaria symulacji po błędzie ładowania jest zamierzona (decyzja z K1, test `Replay_TerrainLoadFails_FaultsSimulation`). `TerrainReplay` ładuje obszar przed `Replay`, więc ta ścieżka oznacza błąd w kodzie startu, a start serwera i tak kończy się błędem z numerem operacji. Bez zmiany Core.

**Krok 7 (gra), decyzje.**
- `TerrainEditSemantics`: `SemanticsVersion = 1`, `VoxelRef = "v1.7"`, `VoxelFormat = 1`, `SdfScale = 1`, `SdfStrength = 1`, `MaterialSphereRadiusOffset = 0`, `RaycastStepMeters = 0,5`, `RaycastBinarySearchIterations = 10`, `BlobCompression = Zstd`, `Current` = `"1|voxel=v1.7"`.
- `VoxelRef` jest stałą, bo `engine/engine.json` nie trafia do buildów gry. Test `Semantics_VoxelRef_MatchesEngineJsonAndRuntime` porównuje ją z `engine.json` i z wersją modułu w działającym silniku (`VoxelEngine.GetVersionMajor/Minor`), więc podbicie `voxel.ref` bez zmiany stałej pada.
- Odstępstwo: `TerrainBlockCodec.Digest` liczy XxHash64 z rozpakowanych kanałów (`GetChannelAsByteArray`: SDF, potem materiał), a nie z serializacji bez kompresji jak w planie. Powód: serializator zapisuje kanał jednorodny jako jedną wartość, a ten sam blok po edycji (rozpakowany) inaczej, więc jego bajty nie są kanoniczne. Pilnuje tego test `Codec_Digest_IgnoresUniformCompression`.
- `TerrainBlockCodec` ma metody instancyjne, bo plan przekazuje obiekt w konstruktorze `VoxelTerrainService`; CA1822 jest wyłączone z uzasadnieniem. `TerrainStream` ma wyłączone CA1711 (sufiks `Stream` pochodzi z nazwy w planie).
- `TerrainVectors.ToGodot/ToCore` to jedyny pomocnik konwersji `System.Numerics.Vector3` ↔ `Godot.Vector3`.
- Odstępstwo: wszystkie sprawdzenia obszaru (`IsAreaLoaded`, `LoadAreaBlocking`, ładowanie przy powtórce) używają pudełka całych bloków LOD 0, a nie samego AABB `[c − r, c + r]`. Pudełko biegnie od bloku `floor((c − e)/16)` do `floor((c + e)/16)` i jest liczone przez `TerrainMath.AffectedBlocks` z promieniem 0 w narożnikach (te same operacje float co `AffectedBlocks` z marginesem). To ostrzejsze niż plan i równe zbiorowi bloków, który `ApplyEdit` sprawdza przed edycją. Dzięki temu intencja przyjęta przez walidator nie może paść w `ApplyEdit` na edytowalności.
- `ApplyEdit` przed pierwszą zmianą sprawdza kolejno: planetę, kształt i tryb, materiał w mapie (`ToTextureIndex`), równość `versions` z `AffectedBlocks` (liczba i klucze po kolei) oraz edytowalność pudełka bloków. Potem wykonuje edycję, kopiuje każdy blok z `versions` (`VoxelTool.Copy` do jednego bufora 16³ trzymanego w polu), koduje go (`Encode`) i woła `ServerTerrainBlocks.Record`.
- Odstępstwo: `IsAreaLoaded` zwraca `false` także wtedy, gdy strumień planety zgłosił błąd (`TerrainStream.FirstError`; log raz na planetę). Powód (S1): po `ResultCode.Error` Voxel Tools wstawia pusty, edytowalny blok, a edycja na nim zapisałaby śmieci z nową wersją. Pełna reakcja zostaje przy Pytaniu do właściciela 2. `VoxelTerrainService.FirstStreamError` udostępnia klucz dla K2-10.
- Wątek: każda metoda `ITerrainService` rzuca `InvalidOperationException`, gdy `OS.GetThreadCallerId() != OS.GetMainThreadId()` (test `TerrainService_OtherThread_Throws`).
- `TerrainStream` nie ma pól statycznych. Każdy wyjątek w wywołaniu strumienia jest logowany i zamieniany na `Error`, bo wyjątek nie może przejść do wątku Voxel Tools. Konstruktor bez parametrów (wymóg Godota) tworzy strumień zamknięty.
- W kompilacji `DEBUG` zapis LOD 0 porównuje skrót danych z parą z bufora: `ThreadLocal<VoxelBuffer>` w polu instancji, licznik `SaveMismatches`, log błędu. W testach wyładowania licznik jest 0.
- `VoxelTerrainService` implementuje `IDisposable` (zwalnia bufor kopii). Ma też `TryCopyBlock` (kopia wczytanego bloku LOD 0 dla testów i diagnostyki) i wewnętrzne `IsAreaEditable` do ładowania, które pomija nasycenie i błąd strumienia.
- `AddPlanet` odrzuca też węzeł, który ma już strumień albo jest w drzewie, oraz powtórzoną planetę (`InvalidOperationException`).

**Krok 8.**
- Odstępstwo: `TerrainAreaLoader` trzyma jednego widza na planetę i przesuwa go do kolejnego obszaru. Wszystkich widzów usuwa dopiero `ReleaseViewers()`, a plan mówi „usuwa widza” po każdym obszarze. Powód: po usunięciu widza Voxel Tools może wyładować bloki w następnej klatce, a kontynuacja `await` z powtórką może przyjść po tej klatce. Trzymany widz gwarantuje, że obszar jest wczytany w chwili `Replay`.
- `TerrainReplay.ReplayAsync` woła `ReleaseViewers()` w `finally`; przekroczenie terminu też usuwa widza. Odległość widza to `ceil(r) + 32`, a termin liczy `TimeProvider`.
- Odstępstwo w sygnaturze: `TerrainReplay.ReplayAsync(simulation, tail, loader, savedSemantics, ct)` zwraca `Task<TerrainTailCheck>` i ma dodatkowy parametr `savedSemantics` (ciąg z `TerrainSemanticsMeta.Read`). Najpierw woła `TerrainSemanticsMeta.CheckTail` z `TerrainEditSemantics.Current`. Przy niezgodności nic nie jest powtarzane (żadnego `ApplyEdit`), a wynik trafia do K2-10 (tryb bezpieczny).
- Wpis z dysku z nieznaną planetą (komunikat `unknown planet`, jak w `OperationApplier`) albo odrzucony przez `TerrainRules.CheckEdit` daje `ReplayFailedException` z numerem operacji, zanim powstanie widz.

**Krok 9.** `DerivedLodCache` ma `MaxBlocks = 8192`; najstarsze wpisy wypadają, a zastąpienie wpisu odświeża jego miejsce w kolejce. Klasa jest bezpieczna wątkowo i odrzuca klucze LOD 0. Raport S5 jest wyżej; zrzutów ekranu brak.

**Krok 10.**
- Odstępstwo w sygnaturze: `VoxelTerrainService.ShutdownAsync()` zwraca `Task<bool>`, a plan podaje `Task`. Metoda zamyka strumienie wszystkich planet (od tej chwili każde wywołanie zwraca `Error` bez odczytu), potem czeka na `ServerTerrainBlocks.CloseAsync()` i zwraca jego wynik.
- Wywołujący (K2-10) woła `TerrainBlockStore.Close()` tylko przy `true`. Przy `false` magazyn zostaje otwarty do końca procesu. Tak robi pomocnik testów (`TerrainTestWorld.DisposeAsync`).
- Test `Shutdown_StreamCallsAfterClose_ReturnErrorWithoutStore` sprawdza też zwolnienie węzła terenu po zamknięciu magazynu.

**Krok 12.**
- `EditSemantics_GoldenDigest`: płaski generator i 50 stałych edycji (dodaj i usuń, wszystkie materiały, promienie 0,5–3 m, ściany, krawędzie i narożniki bloków, ujemne współrzędne). `RecordedSemanticsVersion = 1`, skrót `0x915455B1D651C6C8` (Windows; dwa przebiegi dały ten sam wynik). Linux nie jest sprawdzony lokalnie; wynik da dopiero job `game` w CI.
- `ApplyEdit_Radius3_TimeBudget` ma `[TestCategory("Slow")]`, bo gdUnit4 nie zna `Trait`. Mierzy 500 edycji promienia 3 m tuż przy narożniku bloku (8 bloków kopiowanych i kodowanych na edycję) po 20 rozgrzewkowych. AMD Radeon RX 9070 XT, Windows 11, edytor `double` w Debug: mediana 0,359 ms, p99 0,645 ms, maksimum 19,5 ms (pojedynczy skok, prawdopodobnie GC albo wątek Voxel Tools). Limit planu: mediana ≤ 1 ms, p99 ≤ 3 ms.
- Wiersze ZASADY pkt 2 i 3 dopisał etap 1.

**Krok 13 (część lokalna).** Lokalnie zielone:
- `dotnet build Horyzont.sln`;
- `dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"` (1307 testów);
- `dotnet format Horyzont.sln --verify-no-changes`;
- `dotnet build` i `dotnet format --verify-no-changes` projektu gry;
- test dymny `--smoke` (BOOT OK);
- testy silnika gdUnit4Net (72, w tym 30 z K2-09);
- runner K2-12 `--engine-tests` (18 przeszło, 0 nie);
- pusty `git diff cad1f70..HEAD` na korpusie złotym.

CI na gałęzi (Windows i Linux) nie jest sprawdzone, bo gałąź nie była wypychana. Dlatego krok 13 zostaje otwarty.

**Poprawka po scaleniu K2-12.** Zaślepka `NoTerrain` w `game/tests/Constructs/ConstructTestWorld.cs` miała starą sygnaturę `ApplyEdit`, więc po scaleniu gra się nie budowała. Poprawiłem ją osobnym commitem.

### Poprawki po przeglądzie GPT-6.1 Sol (gra) (2026-10-09, Opus 5.5)

- **[P1] Błąd strumienia blokował tylko `IsAreaLoaded`.**
  - Scenariusz: zapisany blok z wersją jest uszkodzony albo niedostępny. Voxel Tools wstawia wtedy pusty, edytowalny blok (S1). Edycja z ogona dziennika na tym bloku przechodziła sprawdzenie edytowalności, wersje rosły, a dane z pustego bloku trafiały do `Record`, potem do checkpointu. Teren ginął trwale.
  - Poprawka: nowa właściwość `VoxelTerrainService.FirstFault` (`FirstStreamError`, a gdy jego brak, `ServerTerrainBlocks.FirstCorruptKey`). Gdy jest ustawiona, `ApplyEdit` i `LoadAreaBlocking` rzucają `InvalidOperationException` z kluczem bloku, zanim cokolwiek zmienią. `IsAreaLoaded` zwraca `false`. `TerrainAreaLoader.EnsureLoadedAsync` sprawdza flagę przed czekaniem, w każdej klatce i po załadowaniu obszaru (blok z błędem staje się edytowalny dopiero po zgłoszeniu błędu, więc flaga już stoi). Blokada działa do końca sesji; reakcja K2-10 zostaje przy Pytaniu do właściciela 2.
  - Testy: `StreamFault_ApplyEditAndLoadAreaBlocking_ThrowWithoutChange` (dwa warianty: usunięty wiersz w trakcie sesji, czyli `Unavailable` i `FirstCorruptKey`, oraz dane nie do zdekodowania). Sprawdza wyjątki z kluczem, brak wpisu w buforze i niezmieniony skrót terenu. `Replay_EditOverCorruptBlock_FailsFastWithoutRecord`: uszkodzony blok z checkpointu i edycja ogona nad nim; powtórka kończy się wyjątkiem z kluczem, bez `Record`, bez awarii symulacji, ze stanem z checkpointu.
  - Uwaga do testu: usunięcie wiersza między sesjami nie daje uszkodzenia, bo `WorldLoader` czyta wersje z tych samych wierszy `terrain_blocks` (blok wraca wtedy do generatora razem z wersją). Dlatego test powtórki nadpisuje dane bajtami nie do zdekodowania.
- **[P1] Deserializer Voxel Tools zmienia bufor przed walidacją.**
  - Voxel Tools v1.7 ustawia rozmiar i głębie kanałów, zanim sprawdzi resztę danych. Ładunek z uciętym kanałem materiału i poprawnym znacznikiem końca przechodził więc sprawdzenie rozmiaru i głębi, a strumień zwracał „znaleziono”. API w C# nie zwraca wyniku deserializacji (`void`).
  - Poprawka: `TerrainBlockCodec.Decode` dekoduje do bufora roboczego (`ThreadLocal<VoxelBuffer>` w instancji, z `trackAllValues`; kodek jest teraz `IDisposable`). Potem serializuje ten bufor ponownie i wymaga równości bajtów, a dopiero wtedy kopiuje kanały do bufora wywołującego (`SetChannelDepth` i `CopyChannelFrom`). Każdy błąd daje `ProtocolViolationException`, czyli wynik „błąd” strumienia i flagę.
  - Odstępstwo od zalecenia „ta sama kompresja”: porównuję serializację bez kompresji z rozpakowanym ładunkiem (nagłówek `CompressedData`: 0 = brak kompresji, 3 = zstd przez `GD.Decompress`; inne nagłówki i rozmiar ponad 262 167 B są odrzucane). Siła sprawdzenia jest ta sama, bo S2 pokazał bajtową identyczność także bez kompresji. Porównanie bajtów zstd zamieniłoby wszystkie zapisane bloki w „uszkodzone”, gdyby nowsza wersja zstd w silniku kompresowała inaczej.
  - Testy: `Codec_Decode_TruncatedChannelWithTrailingMagic_ThrowsProtocolViolation`, `Stream_TruncatedChannelWithTrailingMagic_ReportsErrorAndFlag` (wynik `Error`, `FirstError` = klucz, `ErrorCount` = 1). Przed poprawką oba były czerwone.
- **[P2] Marsz promienia o stałym kroku przeskakiwał cienkie bryły.**
  - Kontrprzykład z przeglądu: voxel (0, 0, 0) z SDF −1, sąsiedzi +1, promień wzdłuż +X przy y = z = 0,25 od x = −0,25. Bryła to −1/9 < x < 1/9, a obie próbki co 0,5 m dawały +0,156.
  - Poprawka: sphere tracing. Krok = SDF × `RaycastSdfStepFactor` (0,5) / `SdfScale`, najmniej `RaycastMinStepMeters` (0,1 m, czyli 0,1 voxela), najwyżej `RaycastMaxSteps` (1024) kroków. Dalej wyszukiwanie binarne jak dotąd (`RaycastBinarySearchIterations = 10`). Kierunek jest normalizowany. `RaycastStepMeters` usunięty. Stałe są w `TerrainEditSemantics` z dopiskiem, że `Raycast` nie należy do powtórki, więc `SemanticsVersion` zostaje 1.
  - Testy: `Raycast_ThinSolidBetweenSamples_Hits` (trafienie przy x ≈ −1/9 z tolerancją 1 cm; przed poprawką czerwony). `Raycast_NoManagedAllocation` i `Raycast_FlatGround_PreciseHitAndNormal` zostają zielone, `EditSemantics_GoldenDigest` też (bez zmian w teście).
- **[P2] Skrót z bajtów w kolejności hosta.**
  - Poprawka: `Digest` zamienia wartości kanałów szerszych niż 1 B na little-endian (`ToLittleEndian`, zamiana tylko przy `!BitConverter.IsLittleEndian`). Dotyczy to także kanału jednorodnego rozwiniętego przez `GetChannelAsByteArray`.
  - Testy: `Digest_ChannelBytes_LittleEndianOnEveryHost` (logika zamiany dla obu kolejności). Złoty skrót na x86_64 dalej wynosi `0x915455B1D651C6C8`. Ścieżki big-endian nie da się uruchomić lokalnie.
- Starsze wpisy wyżej (S2: „zmniejsza bufor do 1³”, S7 i krok 7: `RaycastStepMeters = 0,5`) opisują stan przed tymi poprawkami.
- Walidacja lokalna: `dotnet build` i `dotnet test` rozwiązania (1307), `dotnet format` rozwiązania i gry, `--smoke` (BOOT OK), testy silnika gdUnit4Net (79, w tym 37 z K2-09), runner K2-12 `--engine-tests` (18 przeszło, 0 nie), pusty `git diff cad1f70..HEAD` na korpusie złotym. `ApplyEdit_Radius3_TimeBudget` raz przekroczył p99 (3,3 ms przy limicie 3 ms) w pełnym przebiegu pod obciążeniem maszyny. Trzy kolejne osobne przebiegi: mediana 0,31–0,37 ms, p99 0,70–1,00 ms.
