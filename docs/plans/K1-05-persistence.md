# K1-05 · Persistence: dziennik, checkpoint, odtwarzanie, migracje

| | |
| --- | --- |
| Prowadzi | Opus 5.5 (Claude Code) |
| Testy akceptacyjne | Opus 5.5 |
| Przegląd | GPT-6.1 Sol (Codex), wysiłek `high` |
| Zależy od | K1-03, K1-04 |
| Gałąź | `pakiet/k1-05-persistence` |

**Cel:** zapis świata w jednej bazie SQLite, w którym awaria procesu w dowolnym momencie kosztuje najwyżej ostatnią niezatwierdzoną partię dziennika, a odtworzenie daje dokładnie stan ostatniej trwałej operacji, bez duplikatów. Partia czeka najwyżej 100 ms, a gdy akurat trwa transakcja checkpointu, dodatkowo do jej końca.

## Kontekst

- `docs/ZASADY.md`, punkty 3, 4, 7
- `docs/spec/01-planeta.md`: Zapis i odtwarzanie, Obsługa błędów
- `docs/plans/K1-03-core-stan-i-operacje.md`: `IJournalSink`, `WorldState` (`CreateNew`, `CaptureCheckpoint`, `CompleteCheckpoint`, `RequeueAfterFailedCheckpoint`), `WorldStateBuilder`, `ServerSimulation.Replay` i `LeaveAllOnlinePlayers`
- `docs/plans/K1-04-serializatory.md`: ramki operacji, `StateCodec` i bloby

## Zakres

Projekt `Horyzont.Persistence`, testy w `tests/Horyzont.Persistence.Tests`, pomocniczy program `tests/Horyzont.Persistence.CrashHost` do testów zabijania procesu, rozszerzenie `FakeTerrainService` w `tests/Horyzont.Testing` o dane bloków.

Poza zakresem: implementacja strumienia Voxel Tools w grze (pakiet 9) i interfejs przywracania kopii w menu (pakiet 18). Tu powstaje API, z którego oba skorzystają.

## Projekt

### Pliki

```
src/Horyzont.Persistence/
  PersistenceOptions.cs           FlushInterval 100 ms, MaxBatch 256, BackupInterval 6 h, BackupsToKeep 10, GameVersion
  WorldDatabase.cs                otwieranie połączeń, pragmy, quick_check
  WorldCreator.cs                 nowy świat: plik, schemat, meta, rejestry, planety
  WorldLoader.cs                  wczytanie → LoadedWorld
  WorldLoadOptions.cs             lista migracji (testy podstawiają własną)
  WorldCorruptedException.cs, WorldFromNewerVersionException.cs
  Migrations/IMigration.cs
  Migrations/MigrationRunner.cs   MigrationRunner.All: migracje produkcyjne w kolejności numerów
  Migrations/M0001_Initial.cs
  Writing/PersistenceWriter.cs    wątek zapisu z kolejką poleceń
  Writing/JournalSink.cs          IJournalSink → PersistenceWriter
  Writing/CheckpointRequest.cs    WorldCheckpoint + dane bloków terenu od gry
  Writing/PersistenceEvent.cs     zdarzenia dla pętli serwera
  Terrain/TerrainBlockStore.cs    odczyt bloków terenu dla strumienia gry
  Backup/BackupService.cs
  Faults/IPersistenceFaults.cs    punkty awarii dla testów; w produkcji NoFaults
  Faults/SimulatedCrashException.cs
```

### Połączenie i schemat

Pragmy po otwarciu: `journal_mode=WAL`, `synchronous=FULL`, `foreign_keys=ON`, `busy_timeout=5000`, `temp_store=MEMORY`. Wersja schematu w `PRAGMA user_version`.

`M0001_Initial`:

```sql
CREATE TABLE meta (key TEXT PRIMARY KEY, value BLOB NOT NULL) WITHOUT ROWID;
CREATE TABLE block_types (id INTEGER PRIMARY KEY, key TEXT NOT NULL UNIQUE);
CREATE TABLE materials (id INTEGER PRIMARY KEY, key TEXT NOT NULL UNIQUE);
CREATE TABLE planets (planet_id INTEGER PRIMARY KEY, params BLOB NOT NULL);
CREATE TABLE terrain_blocks (
    planet_id INTEGER NOT NULL, lod INTEGER NOT NULL,
    block_x INTEGER NOT NULL, block_y INTEGER NOT NULL, block_z INTEGER NOT NULL,
    version INTEGER NOT NULL, data BLOB NOT NULL,
    PRIMARY KEY (planet_id, lod, block_x, block_y, block_z)) WITHOUT ROWID;
CREATE TABLE entities (entity_id INTEGER PRIMARY KEY, kind INTEGER NOT NULL, data BLOB NOT NULL);
CREATE TABLE construct_chunks (
    construct_id INTEGER NOT NULL, chunk_x INTEGER NOT NULL, chunk_y INTEGER NOT NULL, chunk_z INTEGER NOT NULL,
    data BLOB NOT NULL,
    PRIMARY KEY (construct_id, chunk_x, chunk_y, chunk_z)) WITHOUT ROWID;
CREATE TABLE players (
    player_id INTEGER PRIMARY KEY, platform INTEGER NOT NULL, platform_id INTEGER NOT NULL,
    entity_id INTEGER NOT NULL, data BLOB NOT NULL,
    UNIQUE (platform, platform_id));
CREATE TABLE journal (
    op_id INTEGER PRIMARY KEY, tick INTEGER NOT NULL, kind INTEGER NOT NULL,
    actor INTEGER, payload BLOB NOT NULL, crc INTEGER NOT NULL);
CREATE TABLE checkpoints (
    checkpoint_id INTEGER PRIMARY KEY AUTOINCREMENT, barrier_op_id INTEGER NOT NULL,
    tick INTEGER NOT NULL, created_utc TEXT NOT NULL);
```

- Klucze `meta`: `world_id` (16 bajtów), `galaxy_seed`, `next_entity_id`, `next_player_id`, `last_checkpoint_op_id`, `last_checkpoint_tick`, `game_version`, `created_utc`. Liczby jako INTEGER, napisy jako TEXT. Nowy świat ma `last_checkpoint_op_id = 0`, `last_checkpoint_tick = 0`, `next_entity_id = 1`, `next_player_id = 1`.
- `game_version` podaje wywołujący (`PersistenceOptions.GameVersion`, w grze `BuildInfo.Commit` z K1-07); zapisuje go `WorldCreator` i każdy checkpoint.
- `entities.kind` to wartość `EntityKind` z K1-03: `2` = budowla (blob `ConstructHeader`). Kolejne rodzaje bytów trwałych (3 i 6) dochodzą w podprojekcie 3.
- `players.platform` i `players.platform_id` to `PlatformIdentity` gracza; ograniczenie `UNIQUE` pilnuje, że jedna tożsamość ma jeden `PlayerId`.
- Bloby z K1-04: `entities.data` (`ConstructHeader`), `players.data` (`Player`, razem z ramą, `ParentSeq`, fotelem, nazwą i `IsOnline`), `construct_chunks.data` (`ConstructChunk`, samo RLE; klucz chunku jest w kolumnach), `planets.params` (`PlanetParams`).
- `journal.payload` to ramka operacji z K1-04; `crc` to CRC32 (`System.IO.Hashing.Crc32`) z `payload`.
- Wartości `ulong` (identyfikatory, SteamID64, ziarna) zapisujesz jako `unchecked((long)x)` i czytasz jako `unchecked((ulong)y)`.
- Numer migracji nadaje się przy scaleniu do `main` jako następny wolny, bo podprojekty 2 i 3 mogą powstawać równolegle. `MigrationRunner.All` wymaga numerów ciągłych od 1; luka albo powtórzenie to błąd przy starcie i w teście.

### Wątek zapisu

```csharp
public sealed class PersistenceWriter : IAsyncDisposable
{
    public PersistenceWriter(WorldDatabase db, PersistenceOptions options, TimeProvider time,
                             IPersistenceFaults faults, ILogger<PersistenceWriter> logger);
    public OpId DurableOpId { get; }                         // ostatnia operacja na dysku; odczyt z dowolnego wątku
    public bool IsFaulted { get; }
    public void EnqueueJournal(JournalEntry entry);          // używa JournalSink; nie blokuje
    public Task<bool> CheckpointAsync(CheckpointRequest request);   // wynik także jako zdarzenie; false = zapis się nie udał
    public Task<bool> FlushAsync();                          // true: wszystko wcześniejsze jest trwałe; false: zapis w stanie awarii
    public Task<bool> BackupAsync(string reason);
    public void ClearFault();                                // po usunięciu przyczyny awarii; najpierw ponawia zaległy dziennik
    public bool TryDequeueEvent(out PersistenceEvent evt);   // pętla serwera woła co tick
}

public sealed record CheckpointRequest(WorldCheckpoint Core, IReadOnlyList<TerrainBlockPayload> TerrainBlocks);
public sealed record TerrainBlockPayload(TerrainBlockKey Key, uint Version, byte[] Data);   // dane skompresowane przez adapter gry

public abstract record PersistenceEvent { private protected PersistenceEvent() { } }
public sealed record JournalCommitted(OpId UpTo) : PersistenceEvent;
public sealed record CheckpointCommitted(CheckpointRequest Request) : PersistenceEvent;
public sealed record CheckpointFailed(CheckpointRequest Request) : PersistenceEvent;
public sealed record WriterFaulted(Exception Error) : PersistenceEvent;
```

Zasady:

- Jeden dedykowany wątek, kolejka `Channel<WriteCommand>` (wielu piszących, jeden czytający); kolejność wykonania = kolejność wstawienia. Pętla serwera wstawia wpisy dziennika i żądanie checkpointu w kolejności operacji, więc każdy wpis z `OpId` ≤ granicy checkpointu jest w kolejce przed nim.
- Partia dziennika zatwierdza się, gdy minie `FlushInterval` od pierwszego wpisu w partii (mierzone przez `TimeProvider`), gdy uzbiera się `MaxBatch` wpisów, gdy przychodzi inne polecenie (najpierw partia, potem polecenie) albo przy zamykaniu. Jedna partia = jedna transakcja.
- Checkpoint to jedna transakcja: nagłówki budowli i usunięte budowle (razem z ich chunkami), chunki zmienione i usunięte, gracze (z kolumnami tożsamości), bloki terenu (wersja i dane), w `meta` `next_entity_id`, `next_player_id`, `last_checkpoint_op_id`, `last_checkpoint_tick` i `game_version`, wiersz w `checkpoints`, na końcu `DELETE FROM journal WHERE op_id <= barrier`.
- Dowolny błąd zapisu: wycofanie transakcji, `IsFaulted = true`, zdarzenie `WriterFaulted`, a przy checkpoincie także `CheckpointFailed`.
- W stanie awarii wątek zapisu nie zapisuje niczego. Wpisy dziennika czekają w pamięci w kolejności (później nie powstaje luka); każde żądanie checkpointu od razu kończy się `CheckpointFailed` bez dotykania bazy; `FlushAsync` i `BackupAsync` zwracają `false`.
- `ClearFault()` (pętla serwera albo komenda administratora po usunięciu przyczyny, np. zwolnieniu miejsca) tylko wstawia polecenie do kolejki. Wątek zapisu, gdy do niego dojdzie, zapisuje zaległe wpisy dziennika w kolejności i wraca do normalnej pracy; nikt poza nim nie używa jego połączenia. Ponowny błąd znów przełącza w stan awarii.
- Pętla serwera (na swoim wątku): `CheckpointCommitted` → `WorldState.CompleteCheckpoint(request.Core)`; `CheckpointFailed` → `WorldState.RequeueAfterFailedCheckpoint(request.Core)` i przekazanie adapterowi terenu kluczy z `request.TerrainBlocks` do ponownego oznaczenia; `WriterFaulted` → tryb bezpieczny ze specyfikacji (serwer przestaje przyjmować zmiany i ostrzega). Wątek zapisu nigdy nie dotyka `WorldState`.
- Najwyżej jeden checkpoint w locie pilnuje `WorldState` (K1-03): kolejny `CaptureCheckpoint` przed `CompleteCheckpoint` albo `RequeueAfterFailedCheckpoint` rzuca.
- `SimulatedCrashException` z punktu awarii (tylko testy) kończy wątek zapisu natychmiast, bez żadnego kolejnego zapisu, tak jak śmierć procesu; `DisposeAsync` po niej tylko zamyka połączenia.
- Zamknięcie: `DisposeAsync` przestaje przyjmować polecenia, wykonuje kolejkę do końca (z partią dziennika) i zamyka połączenie. Ostatni checkpoint przed zamknięciem zleca wywołujący, w kolejności zatrzymania serwera z K1-03 (checkpoint w locie, zamknięcie sesji, `LeaveAllOnlinePlayers()`, checkpoint, `DisposeAsync`).

### Wczytywanie

```csharp
public sealed record WorldLoadOptions
{
    public IReadOnlyList<IMigration> Migrations { get; init; } = MigrationRunner.All;
    public required string BackupsDirectory { get; init; }      // saves/<WorldId>/backups
    public IReadOnlyList<string> BlockTypeKeys { get; init; } = [];   // typy bloków z treści gry
    public IReadOnlyList<string> MaterialKeys { get; init; } = [];    // materiały z treści gry
}

public sealed record LoadedWorld(WorldState State, IReadOnlyList<JournalEntry> JournalTail, OpId LastCheckpointOpId, string GameVersion);

public static class WorldLoader
{
    public static LoadedWorld Load(string path, WorldLoadOptions options, TimeProvider time, ILogger logger);
}
```

1. `PRAGMA quick_check`; wynik inny niż `ok` → `WorldCorruptedException` (gra zaproponuje przywrócenie kopii).
2. `user_version` niższy niż najnowsza migracja z `options.Migrations` → `WorldLoader` sam robi kopię `pre-migration` (wątek zapisu jeszcze nie istnieje), potem migracje, każda w osobnej transakcji. Wyższy → `WorldFromNewerVersionException`.
3. Klucze z `BlockTypeKeys` i `MaterialKeys`, których nie ma w bazie, dostają kolejne numery (od największego istniejącego + 1) w jednej transakcji. Przekroczenie `MaxBlockTypes` albo `MaxMaterials` daje `InvalidOperationException`, bo to błąd treści gry. Klucze z bazy nieobecne w opcjach zostają.
4. Meta, rejestry, planety, budowle z chunkami, gracze i same wersje bloków terenu do `WorldStateBuilder`, z `SetProgress(last_checkpoint_op_id, last_checkpoint_tick, next_entity_id, next_player_id)`. Dane bloków terenu wczytuje leniwie `TerrainBlockStore`. `ProtocolViolationException` z dekodowania blobu i `InvalidDataException` z `Build()` zamieniasz na `WorldCorruptedException`.
5. Dziennik w kolejności `op_id`: sprawdzenie CRC i ciągłości od `last_checkpoint_op_id + 1`; dekodowanie `OperationCodec`. Błąd CRC albo luka → `WorldCorruptedException` z numerem operacji.

Gra po wczytaniu woła `ServerSimulation.Replay` dla każdego wpisu z `JournalTail`, potem `LeaveAllOnlinePlayers()`, i od razu zleca checkpoint (specyfikacja, „Odtwarzanie przy starcie”).

### Bloki terenu dla gry

```csharp
public sealed class TerrainBlockStore
{
    public TerrainBlockStore(string databasePath);
    public bool TryLoad(TerrainBlockKey key, out uint version, out byte[] data);   // bezpieczne wątkowo, czytelnicy WAL nie blokują zapisu
}
```

Każde wywołanie otwiera połączenie z puli `Microsoft.Data.Sqlite`. Bloki zmienione, ale jeszcze niezapisane, trzyma adapter gry i sprawdza je przed `TryLoad` (pakiet 9).

### Kopie

`BackupService(string backupsDirectory, int keep, TimeProvider time)`; `CreateBackup(SqliteConnection source, string reason)` używa `SqliteConnection.BackupDatabase` i zapisuje do `<backupsDirectory>/<yyyyMMdd-HHmmss>-<reason>.db` (czas UTC z `TimeProvider`), zostawiając `keep` najnowszych. W trakcie gry kopie wykonuje wątek zapisu (`BackupAsync`: przy starcie serwera i co 6 h). Kopię przed migracją robi `WorldLoader`, zanim powstanie wątek zapisu.

### Punkty awarii

```csharp
public enum FaultPoint { BeforeJournalCommit, AfterJournalCommit, CheckpointMidTransaction, BeforeCheckpointCommit, AfterCheckpointCommit }
public interface IPersistenceFaults { void Hit(FaultPoint point); }   // NoFaults w produkcji; w testach rzuca SimulatedCrashException albo IOException
```

`SimulatedCrashException` w transakcji kończy się jej wycofaniem przy zamknięciu połączenia, czyli tak samo jak śmierć procesu w trybie WAL. `IOException` z punktu awarii udaje błąd dysku i sprawdza ścieżkę stanu awarii.

### Dane bloków w `FakeTerrainService`

`FakeTerrainService` dostaje deterministyczne „dane voxeli” bloku: bajty wyliczane z listy edycji tego bloku (np. XxHash64 z poprzednich danych i ramki operacji). Testy przekazują je w `TerrainBlockPayload`, a przy `LoadAreaBlocking` odczytują przez delegata `Func<TerrainBlockKey, byte[]?> BlockSource`, którego testy Persistence ustawiają na `TerrainBlockStore.TryLoad`. `Horyzont.Testing` dalej zależy tylko od Core.

## Testy akceptacyjne (napisz najpierw)

Czas przez `FakeTimeProvider`; prawdziwy czas tylko w `CrashHostSoakTests`.

| Test | Co sprawdza |
| --- | --- |
| `CreateAndLoadTests` | nowy świat wczytuje się z tym samym `WorldId`, ziarnem, rejestrami, planetami i `GameVersion` |
| `JournalTests` | partia zatwierdza się po 100 ms (przesunięcie `FakeTimeProvider`) albo po 256 wpisach; `DurableOpId` rośnie monotonicznie; CRC i ciągłość sprawdzane przy wczytaniu |
| `CheckpointTests` | po checkpoincie dziennik ≤ granicy jest pusty; stan wczytany = stan na żywo (skrót z K1-03), a `CurrentTick` równy tickowi ostatniego wpisu albo, przy pustym ogonie, checkpointu; budowla usunięta po checkpoincie nie wraca; gracz online w checkpoincie z `PlayerLeave` w ogonie wczytuje się i powtarza bez błędu |
| `RegistryGrowthTests` | nowy klucz w `WorldLoadOptions.BlockTypeKeys` dostaje kolejny numer i zostaje po ponownym wczytaniu; klucz z bazy nieobecny w opcjach zostaje z tym samym numerem; przekroczenie `MaxBlockTypes` rzuca `InvalidOperationException` |
| `FaultInjectionTests` (właściwość) | dla każdego `FaultPoint` i losowej sekwencji intencji z checkpointem co 20 operacji (pomijanym, gdy poprzedni jest w locie): awaria, ponowne otwarcie, wczytanie, powtórka; skrót i dane bloków `FakeTerrainService` równe wyroczni po `LastOpId` odtworzonego stanu, a `LastOpId` ≥ ostatni `DurableOpId` sprzed awarii |
| `CheckpointFailureTests` | błąd checkpointu daje `CheckpointFailed` i `WriterFaulted`; w stanie awarii dziennik nie trafia na dysk, a kolejne żądania checkpointu kończą się `CheckpointFailed` bez dotykania bazy; po `RequeueAfterFailedCheckpoint`, `ClearFault` i udanym checkpoincie żadna zmiana nie ginie, a dziennik nie ma luk |
| `JournalFailureTests` | błąd partii dziennika: późniejsze wpisy nie trafiają na dysk; po `ClearFault` zaległe wpisy zapisują się w kolejności, a wczytanie widzi ciągły dziennik |
| `MigrationTests` | baza z `Fixtures/world-v1.db` (z commita, nie generowana w teście, skopiowana do katalogu tymczasowego przed otwarciem) wczytuje się; testowa migracja 2 podana w `WorldLoadOptions.Migrations` robi kopię i podnosi `user_version`; `user_version = 99` daje `WorldFromNewerVersionException`; `MigrationRunner.All` ma numery ciągłe od 1, a lista z luką albo powtórzeniem rzuca przy starcie |
| `CorruptionTests` | zmieniony bajt w `payload` dziennika daje `WorldCorruptedException` z numerem operacji; uszkodzony blob budowli daje `WorldCorruptedException`; uszkodzony plik nie przechodzi `quick_check` |
| `TerrainBlockStoreTests` | odczyt równoległy z 8 wątków podczas checkpointu zwraca spójne pary wersja–dane |
| `BackupTests` | kopia otwiera się jako poprawny świat; rotacja zostawia 10 plików |
| `CrashHostSoakTests` (`Category=Soak`) | 50 przebiegów: `CrashHost` wykonuje deterministyczne intencje z ziarna, co 100 operacji zleca checkpoint (gdy żaden nie jest w locie) i wypisuje `DURABLE <opId>` po każdym `JournalCommitted`; test zabija proces w losowej chwili (50–2000 ms), wczytuje bazę, powtarza dziennik i porównuje skrót z wyrocznią (te same intencje bez zapisu do odtworzonego `LastOpId`); `LastOpId` ≥ ostatnie wypisane `DURABLE` |

Wyrocznia: ta sama sekwencja intencji z tego samego ziarna na świeżym świecie w pamięci.

## Kroki

- [x] Testy z tabeli (czerwone), rozszerzenie `FakeTerrainService` i `CrashHost`.
- [x] `WorldDatabase`, `M0001_Initial`, `MigrationRunner`, `WorldCreator`.
- [x] `PersistenceWriter` z partiami dziennika, `JournalSink`, zdarzeniami.
- [x] Checkpoint w jednej transakcji; stan awarii, `ClearFault`, `CheckpointFailed`.
- [x] `WorldLoader` z weryfikacją CRC i ciągłości.
- [x] `TerrainBlockStore`, `BackupService`.
- [x] Punkty awarii i testy wstrzykiwania; `Fixtures/world-v1.db` zapisany w repo.
- [x] Test zabijania procesu w nocnym CI.
- [x] Komentarze XML; zielone CI (lokalnie: build bez ostrzeżeń, testy, `dotnet format`; CI do potwierdzenia w PR).

## Odbiór

Testy z tabeli przechodzą (Soak w nocnym przebiegu: 50 z 50), przegląd GPT-6.1 Sol potwierdza brak ścieżki, w której operacja zostaje zastosowana dwa razy albo znika po zgłoszeniu jako trwała.

## Pułapki

- Checkpoint musi zawierać wszystkie operacje do granicy, zanim usunie dziennik; dlatego wątek zapisu najpierw zatwierdza bieżącą partię dziennika.
- `Microsoft.Data.Sqlite` nie jest bezpieczny wątkowo na poziomie jednego połączenia: wątek zapisu ma własne, każdy czytelnik bierze swoje z puli.
- Nie wołaj `RequeueAfterFailedCheckpoint` ani `CompleteCheckpoint` z wątku zapisu; tylko przez zdarzenie na pętli serwera.
- `synchronous=FULL` w WAL jest wolniejsze od `NORMAL`, ale tylko ono gwarantuje trwałość zatwierdzonej partii po utracie zasilania.
- `Fixtures/world-v1.db` commituj po `PRAGMA journal_mode=DELETE`, inaczej część danych może zostać w pliku `-wal`, którego `.gitignore` nie wpuszcza do repozytorium.

## Odstępstwa i decyzje wykonawcy

- Core bez zmian. `Horyzont.Testing` dostał `WorldScript` (deterministyczne intencje z ziarna, najwyżej jedna operacja na krok, wspólne dla testów, `CrashHost` i wyroczni) i dane bloków w `FakeTerrainService` (XxHash64 poprzednich danych i pól edycji, bez kodeka; `BlockSource`, `GetBlockData`, `Blocks`).
- `PersistenceOptions.BackupsDirectory` (domyślnie `backups` obok pliku świata) i `WorldLoadOptions.BackupsToKeep` (10): plan nie mówił, skąd wątek zapisu i `WorldLoader` biorą katalog i limit rotacji.
- Kopie co `BackupInterval` zleca sam wątek zapisu timerem `TimeProvider`; kopię przy starcie zleca gra przez `BackupAsync("startup")`. Nieudana kopia zwraca `false` i loguje błąd, ale nie przełącza w stan awarii, bo nie dotyka pliku świata.
- `CheckpointAsync` rzuca `ArgumentException`, gdy wersja z `WorldCheckpoint.TerrainVersions` nie ma danych w `TerrainBlocks` z tą samą wersją (błąd wywołującego, para wersja–dane w bazie musi być spójna). Dodatkowe bloki (wyładowane przez Voxel Tools) są dozwolone.
- Wątek zapisu pilnuje ciągłości: wpis z `OpId` innym niż poprzedni + 1 nie jest zapisywany i przełącza w stan awarii. Granica checkpointu spoza przedziału [ostatni checkpoint, `DurableOpId`] też. `ClearFault` najpierw czyta z bazy faktyczny postęp (największy `op_id` dziennika albo `last_checkpoint_op_id`) i pomija wpisy już trwałe, więc commit, który zgłosił błąd po dotarciu na dysk, nie zapisze się drugi raz.
- Błąd `IOException` w `AfterJournalCommit`/`AfterCheckpointCommit`: dane są na dysku, więc writer zgłasza `JournalCommitted`/`CheckpointCommitted` i dodatkowo `WriterFaulted`. Zdarzenia trafiają do kolejki przed wynikiem zadania.
- `SimulatedCrashException`: polecenie w trakcie i zaległe w kolejce kończą się wynikiem `false` (checkpoint także `CheckpointFailed`), bez żadnego zapisu, żeby testy nie wisiały.
- `PersistenceWriter.ProbeAsync` (internal, dla testów): punkt synchronizacji, który nie zatwierdza partii; dzięki niemu testy partii są deterministyczne bez czekania w czasie rzeczywistym.
- `M0001_Initial` jest `internal` (nazwa z podkreśleniem; testy biorą ją z `MigrationRunner.All[0]`). `MigrationRunner` ma też publiczne `Validate`, `LatestVersion` i `Run`. `user_version = 0` przy wczytaniu daje `WorldCorruptedException`.
- `FaultInjectionTests`: checkpoint co 20 operacji jest pomijany, gdy poprzedni jest w locie, a zlecony czeka na wynik; bez tego wątek zapisu nie nadążał za pętlą testu i punkty awarii checkpointu prawie nie padały. Zmierzone: wszystkie 40 przypadków kończy się awarią.
- `Fixtures/world-v1.db` generuje test `FixtureGenerator` (`Category=Manual`, odmawia nadpisania istniejącego pliku); plik w repozytorium jest w trybie `DELETE`.
