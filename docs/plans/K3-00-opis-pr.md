# K3-00: szwy przed równoległym startem Co-op

Pakiety K3-14…18 mogą korzystać z jednego dispatchu komunikatów, wspólnego budżetu pasma i neutralnych adapterów klienta. S1–S9 są zaimplementowane; domyślna ścieżka K2 nadal używa pełnych migawek, zawsze gotowego świata oraz współdzielonego terenu hosta z NoEditPrediction.

- S1: wszystkie handlery w kolejności rejestracji, bez short-circuit; Debug wyłącznie przy braku obsługi; rejestracja przed StartAsync.
- S2–S3: jawne typy routera 1–20 i jeden test udokumentowanej wersji protokołu. ProtocolVersion = 3; src/Horyzont.Core bez zmian.
- S4: FixedSnapshotBudget 1150 B; wspólne wiadro per PlayerId 27 000 B/s / 65 536 B, Live z długiem, Bulk z progiem min(bytes, capacity), rzeczywisty koszt odejmowany w całości.
- S5–S7: pojedynczy full snapshot sink, readiness zatrzymuje nagrywanie/wysyłanie wejść i prowadzi za serwerem; widzowie w ramie planety przez PlanetFrames.
- S8: neutralny RemoteToolWorldView, raycast i fabryki narzędzi; observer odpowiedzi przed trackerem.
- S9: puste instancyjne partial w kolejności Replication → TerrainReplication → Motion → Platform.

## Walidacja lokalna (2026-10-10)

Testy akceptacyjne powstały przed implementacją (`88d686b`); RED potwierdził brak projektowanych kontraktów. Regresja bazowego K2: 1757 testów .NET zielonych. Końcowe wyniki polecenia `dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"`:

| Projekt | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Horyzont.Architecture.Tests | 12 | 0 | 0 |
| Horyzont.Core.Tests | 1120 | 0 | 0 |
| Horyzont.Net.Tests | 113 | 0 | 0 |
| Horyzont.Persistence.Tests | 124 | 0 | 0 |
| Horyzont.Integration.Tests | 28 | 0 | 0 |
| Horyzont.Constructs.Tests | 95 | 0 | 0 |
| Horyzont.Server.Tests | 117 | 0 | 0 |
| Horyzont.Client.Tests | 161 | 0 | 0 |
| **Razem** | **1770** | **0** | **0** |

- `dotnet build Horyzont.sln`: PASS, 0 ostrzeżeń, 0 błędów.
- `dotnet build game/Horyzont.Game.csproj`: PASS, 0 ostrzeżeń, 0 błędów; nowe i istniejące testy gdUnit skompilowane.
- `dotnet format Horyzont.sln --verify-no-changes`: PASS (exit 0).
- `git diff --check`: PASS; diff src/Horyzont.Core pusty; zamrożone korpusy i golden values niezmienione.

## Odstępstwa i odbiór

Na polecenie użytkownika GPT-6.1 Sol implementuje i pisze akceptację; Opus 5.5 przegląda. Gałąź `pakiet/k3-00-szwy`, baza bramki K2 `91de59d`, bez pushowania. Szczegóły decyzji w planie K3-00.

Godot, gdUnit i smoke silnika **nie uruchomiono** na jedynym runnerze CI. Testy silnikowe do wykonania szeregowo w CI: nowe CoopSeamTests/ToolSeamTests oraz regresje ruchu, narzędzi i lifecycle (solo, host, --connect), w tym SnapshotProgressTests po przejściu na pełny sink. Po smoke, zielonym CI i przeglądzie Opus można zamknąć krok 5 i odbiór pakietu. Brak otwartych pytań wymagających decyzji; Core ani formatów nie zmieniono.
