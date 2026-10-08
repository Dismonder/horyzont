# Plany wykonawcze: K1 Fundament

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

Plany K1 leżą w tym katalogu. Plany kolejnych pakietów Opus 5.5 pisze ze specyfikacji podprojektu (`docs/spec/`), zanim pakiet ruszy, bo zależą od kodu, który jeszcze nie istnieje. Specyfikacje podają już zakres, kontrakty, kody, testy i odbiór każdego pakietu.

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

- [ ] CI zielone na Windows i Linux, testy akceptacyjne wszystkich siedmiu pakietów przechodzą.
- [ ] `python tools/engine/build.py smoke` wypisuje `SMOKE OK`, a projekt `game/` uruchamia się bez grafiki z `--smoke`.
- [ ] Nowy projekt `tests/Horyzont.Integration.Tests` (referencje: Core, Persistence, Net, Testing; dodany do `Horyzont.sln`) z testem end-to-end w pamięci. Serwer z `ServerSimulation`, `PersistenceWriter` na pliku tymczasowym i dwóch klientów przez `InMemoryTransport` przyjmuje intencje i zapisuje dziennik, a po symulowanej awarii wczytanie z powtórką daje ten sam skrót stanu. Haki gry dla testu (`IServerGameHooks` nad `ServerSimulation`) żyją w tym projekcie.
- [ ] Opus 5.5 przegląda całość w świeżej sesji i pisze plany K2 (pakiety K2-08 … K2-13).

## Szablon planu pakietu

Każdy plan ma te same części: nagłówek (kto, zależności, gałąź), cel w jednym zdaniu, kontekst do przeczytania, zakres i poza zakresem, projekt (pliki, kontrakty, decyzje), testy akceptacyjne, kroki z polami wyboru, odbiór i pułapki. Wykonawca odhacza kroki w tym pliku w ramach swojego PR.
