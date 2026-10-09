# Plany wykonawcze: K1 Fundament i K2 Planeta

K1 to siedem pakietów, po których rozwiązanie ma zbudowany silnik, kontrakty Core, zapis odporny na awarie i działającą warstwę sieciową; dopiero na tym fundamencie powstaje planeta (K2).

## Pakiety

| Pakiet | Plan | Prowadzi | Testy akceptacyjne pisze | Przegląd | Zależy od |
| --- | --- | --- | --- | --- | --- |
| K1-01 | [Pipeline silnika](K1-01-pipeline-silnika.md) | Sonnet 5.5 | Sonnet 5.5 | Opus 5.5 na bramce | — |
| K1-02 | [Szkielet rozwiązania](K1-02-szkielet-rozwiazania.md) | Gemini 3.8 Flash | Sonnet 5.5 | Sonnet 5.5 | etap A: —; etap B: K1-01 |
| K1-03 | [Core: stan, operacje, protokół](K1-03-core-stan-i-operacje.md) | Opus 5.5 | Opus 5.5 | GPT-6.1 Sol | K1-02A |
| K1-04 | [Serializatory i fuzzing](K1-04-serializatory.md) | Gemini 3.8 Flash | Sonnet 5.5 | Sonnet 5.5 + Opus 5.5 (format) | K1-03 |
| K1-05 | [Persistence](K1-05-persistence.md) | Opus 5.5 | Opus 5.5 | GPT-6.1 Sol | K1-03, K1-04 |
| K1-06 | [Transporty](K1-06-transporty.md) | Sonnet 5.5 | Sonnet 5.5 | Opus 5.5 na bramce | K1-02A |
| K1-07 | [Sesja i protokół](K1-07-sesja-i-protokol.md) | Opus 5.5 | Opus 5.5 | GPT-6.1 Sol | K1-04, K1-06 |

K1-02 dzieli się na etap A (rozwiązanie .NET bez silnika) i etap B (projekt Godota po K1-01), a K1-06 przenosi same bajty, więc nie czeka na Core; plan projektu ma te same zależności. Dzięki temu Core i transporty ruszają, zanim skończy się wielogodzinne budowanie silnika.

Identyfikator pakietu to kamień milowy i numer z [planu projektu](../plan/plan-projektu.md): K1-01 … K1-07, dalej K2-08 … K2-13, K3-14 … K3-18 i K4-19 w Planecie, K5-20 … K5-28 w Kosmosie, K6-29 … K6-40 w Przetrwaniu, K7-41 … K7-49 w Walce, K8-50 … K8-57 w Treści na EA i K9-58 … K9-65 w Wydaniu. Etap pakietu dostaje literę (K1-02A, K1-02B), a gałąź nazywa się od identyfikatora (`pakiet/k2-08-generator`).

Plany K1 i K2 leżą w tym katalogu. Plany kolejnych pakietów Opus 5.5 pisze ze specyfikacji podprojektu (`docs/spec/`), zanim pakiet ruszy, bo zależą od kodu, który jeszcze nie istnieje. Specyfikacje podają już zakres, kontrakty, kody, testy i odbiór każdego pakietu.

## Kolejność startu

| Fala | Pakiety (mogą iść równolegle) | Warunek startu |
| --- | --- | --- |
| 1 | K1-01 (Sonnet), K1-02A (Gemini) | brak |
| 2 | K1-03 (Opus), K1-06 (Sonnet), K1-02B (Gemini) | K1-02A scalony; K1-02B także po K1-01 |
| 3 | K1-04 (Gemini) | K1-03 scalony |
| 4 | K1-05 (Opus), K1-07 (Opus) | K1-04 scalony; K1-07 także po K1-06 |
| Bramka K1 | test end-to-end i przegląd całości przez Opus 5.5 | wszystkie pakiety scalone |

## Bramka K1

Bramkę prowadzi Opus 5.5 na gałęzi `pakiet/k1-bramka`. Test end-to-end powstaje dopiero tu, bo jako jedyny potrzebuje jednocześnie Persistence (K1-05) i sesji (K1-07), które idą równolegle.

- [x] CI zielone na Windows i Linux, testy akceptacyjne wszystkich siedmiu pakietów przechodzą.
  Testy UDP z K1-06 dostały na bramce zapas na przestoje planisty runnera (timeout połączenia 2 s, limit testu 30 s); pierwszy nightly z testami Soak wykrył na Windows przejściowe `SQLITE_IOERR_TRUNCATE` (1546) przy otwieraniu świata po zabiciu procesu (11/50 przebiegów); `WorldDatabase` ponawia teraz przejściowe błędy I/O przy otwieraniu (ok. 1,6 s), a nightly na gałęzi bramki jest zielony na Windows i Linux.
- [x] `python tools/engine/build.py smoke` wypisuje `SMOKE OK`, a projekt `game/` uruchamia się bez grafiki z `--smoke`.
- [x] Nowy projekt `tests/Horyzont.Integration.Tests` (referencje: Core, Persistence, Net, Testing; dodany do `Horyzont.sln`) z testem end-to-end w pamięci. Serwer z `ServerSimulation`, `PersistenceWriter` na pliku tymczasowym i dwóch klientów przez `InMemoryTransport` przyjmuje intencje i zapisuje dziennik, a po symulowanej awarii wczytanie z powtórką daje ten sam skrót stanu. Haki gry dla testu (`IServerGameHooks` nad `ServerSimulation`) żyją w tym projekcie.
- [x] Opus 5.5 przegląda całość w świeżej sesji i pisze plany K2 (pakiety K2-08 … K2-13).
  Przegląd: 6 recenzentów Sonnet 5.5, 2 opinie GPT-6.1 Sol, synteza i weryfikacja Opus 5.5; blokujące B1–B3 poprawione w PR #9, B4 to scalenie K1-02B (#4). Plany K2 sprawdził krzyżowo GPT-6.1 Sol, a rozstrzygnięcia D1–D17 są wpisane w plany. Pytania do właściciela mają w planach warianty domyślne; pakiet rusza z domyślnym wariantem, jeśli właściciel nie odpowie przed jego startem.

## Pakiety K2

| Pakiet | Plan | Prowadzi | Testy akceptacyjne pisze | Przegląd | Zależy od |
| --- | --- | --- | --- | --- | --- |
| K2-08 | [Planeta: generator i parametry](K2-08-generator-planety.md) | Sonnet 5.5 | Sonnet 5.5 | Opus 5.5 | etap A (K2-08A, `editor-linux`): K1-02B; etap B: K2-08A |
| K2-09 | [Adapter Voxel Tools](K2-09-adapter-voxel-tools.md) | Opus 5.5 | Opus 5.5 | GPT-6.1 Sol (`high`) + Opus 5.5 w świeżej sesji (Core) | bramka K1 (PR #9: B1–B3), K1-05, K1-02B, K2-08 |
| K2-10 | [Tryby gry](K2-10-tryby-gry.md) | Sonnet 5.5 | Sonnet 5.5 (tabelę zamykania i E3–E9 sprawdza Opus 5.5) | Opus 5.5 w świeżej sesji + GPT-6.1 Sol (`medium`: wątki, zamykanie, E6/E7) | K1-07, K2-09 |
| K2-11 | [Postać i kamera](K2-11-postac-i-kamera.md) | Sonnet 5.5 | Sonnet 5.5 (testy Core przegląda Opus 5.5) | Opus 5.5 | K2-08, K2-10 (pośrednio K2-09) |
| K2-12 | [Budowle](K2-12-budowle.md) | GPT-6.1 Sol | GPT-6.1 Sol | Opus 5.5 | K1-02B, K1-03, K2-08A |
| K2-13 | [Narzędzia](K2-13-narzedzia.md) | Sonnet 5.5 | Sonnet 5.5 (testy Core przegląda Opus 5.5) | Opus 5.5 | K2-09, K2-10, K2-11, K2-12 |

K2-08A to osobny mały PR z celem `editor-linux` w `engine/engine.json`; bez niego testy silnika nie ruszą na Linuksie, więc czekają na niego K2-08B i K2-12. K2-12 nie czeka na resztę K2: wpięcie budowli w `GameComposition` K2-10 robi K2-13 w kroku 0.

### Kolejność startu K2

| Fala | Pakiety (mogą iść równolegle) | Warunek startu |
| --- | --- | --- |
| 1 | K2-08A (Sonnet) | bramka K1 zakończona, K1-02B scalony |
| 2 | K2-08B (Sonnet), K2-12 (Sol) | K2-08A scalony i `engine.yml` na `main` zielony |
| 3 | K2-09 (Opus) | K2-08 scalony; bramka K1 (PR #9: B1–B3) scalona |
| 4 | K2-10 (Sonnet) | K2-09 scalony z E1–E2 |
| 5 | K2-11 (Sonnet) | K2-10 scalony (`ProtocolVersion` = 2 na `main`) |
| 6 | K2-13 (Sonnet) | K2-11 i K2-12 scalone |
| Bramka K2 | przegląd całości i plany K3 | wszystkie pakiety K2 scalone, zielone CI i przebieg nocny na `main` |

### Warunki wejścia E1–E9 z przeglądu bramki K1

| Punkty | Właściciel |
| --- | --- |
| E1–E2 (kopie zapasowe: walidacja `reason`, kopia poza wątkiem zapisu według pomiaru) | K2-09 |
| E3–E9 (writer przy zamykaniu, kontrakt `OnPlayerJoining`, Prepare+Apply, `ITransport.IsDrained`, termin zamykania UDP, połączenia podczas zamykania, kod rozłączenia klienta) | K2-10 |

### Reguły wspólne K2 (decyzje bramki K1)

- **Semantyka edycji terenu jest wersjonowana (D1).** Ogon dziennika zapisany inną wersją semantyki terenu (`TerrainEditSemantics.SemanticsVersion` + `voxel.ref`, K2-09) nie jest powtarzany nową implementacją: świat otwiera się w trybie bezpiecznym z jasnym błędem, a odzyskanie z samego checkpointu to jawna akcja właściciela (K2-10, `--recover-from-checkpoint`). Zmiana `OperationApplier.cs` albo `ITerrainService.ApplyEdit` bez podbicia formatu operacji jest dozwolona tylko wtedy, gdy pierwszy krok pakietu zatwierdza zamrożony korpus złoty dzienników (oczekiwane skróty `StateHasher` i listy wersji bloków nagrane z `main`), a jego test przechodzi po zmianie bez zmian; inaczej podbicie formatu operacji z powtórką starej wersji.
- **`ProtocolVersion` (D4).** Pakiet, który podbija wersję protokołu, ustawia ją na (wartość na `main` w chwili scalenia) + 1; test sprawdza udokumentowaną wartość; rebase aktualizuje i stałą, i test. Kolejność K2: K2-10 → 2, K2-11 → 3.
- **Jedna kolejność zamykania (D8).** Kanoniczna tabela jest w K2-10 („Kolejność zamykania”); K2-09, K2-11 i K2-13 tylko dokładają do niej swoje kroki. `TerrainBlockStore.Close()` tylko po `ServerTerrainBlocks.CloseAsync() == true`.
- **Wspólna walidacja edycji terenu (D2).** `TerrainRules.CheckEdit` w Core (właściciel K2-09) używają i `OperationApplier`, i powtórka dziennika przed ładowaniem terenu.

## Szablon planu pakietu

Każdy plan ma te same części: nagłówek (kto, zależności, gałąź), cel w jednym zdaniu, kontekst do przeczytania, zakres i poza zakresem, projekt (pliki, kontrakty, decyzje), testy akceptacyjne, kroki z polami wyboru, odbiór i pułapki. Wykonawca odhacza kroki w tym pliku w ramach swojego PR.
