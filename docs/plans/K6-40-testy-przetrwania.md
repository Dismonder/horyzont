# K6-40 · Testy Przetrwania

| | |
| --- | --- |
| Prowadzi | Sonnet 5.5 (`docs/plan/plan-projektu.md` l. 141) |
| Testy akceptacyjne | Sonnet 5.5; wyrocznie, generatory i straże „nie pusto” powstają przed infrastrukturą; zatwierdza Opus 5.5 na A4 |
| Przegląd | Opus 5.5 na bramkach A4, B5, C5, D4 i przed scaleniem; każda zmiana w `src/` (także „minimalna poprawka”) według ZASADY §3 i §9 |
| Zależy od | K6-31, K6-34, K6-35, K6-37, K6-38, K6-39 (przechodnio K6-29…K6-36) |
| Gałąź | `pakiet/k6-40-testy-przetrwania`, z `main` po scaleniu wszystkich zależności |
| Protokół | **bez podbicia**; K6-40 nie zajmuje żadnego numeru (szczegóły: „Kontrakty chronione i wersje”) |
| Kamień milowy | K6 Przetrwanie (bramka) |
| Odbiór ze specyfikacji | „wszystkie kryteria ukończenia spełnione” (spec 03 l. 442, kryteria l. 416–423) |
| Plik planu w repo | `docs/plans/K6-40-testy-przetrwania.md` |

**Cel:** dowieść kryteriów ukończenia Przetrwania testami przekrojowymi: właściwość zachowania przedmiotów na 10 000 sekwencji intencji 8 graczy, 8 botów przy jednej skrzyni pod RTT 250 ms i 5% strat, pełna pętla 8 graczy, determinizm, awarie i restart, ustawienia świata, scenariusze regresji między pakietami oraz jeden test składania gry w silniku. Pakiet nie dodaje rozgrywki. Wynik to testy, raport pokrycia i — tylko po teście czerwonym i przeglądzie Opus — minimalne poprawki błędów znalezionych przez testy.

Wykonawca nie uruchamia gry z oknem. Plan przygotowano z odczytu repozytorium; nie budowano, nie testowano i nie commitowano.

## Uzgodnienie numerów (Opus, 2026-10-17, `K6-37-40-reconciliation.md`)

- K6-40 nadal nie zajmuje żadnego numeru i nie przypina PV. Kolizja PV 22 w szkicach K6-37…K6-39 rozstrzygnięta: K6-37 = 22, K6-38 = 23, K6-39 = 24 (planowo; literał z bazy).
- Tabela kanoniczna do macierzy pokrycia: intencje 26 (K6-34), 30 (K6-37), 31/32 (K6-38), 33 (K6-39), 34 (K6-36, tylko podgląd — bez mutacji, nie wchodzi do generatora bilansu); odmowy 47 (K6-37), 48 (K6-35), 50 (K6-38), 51/52/55/58 (K6-36), 56 (K6-39); 59 wolna. `BlockFunctionKind`: 17 `Teleport`, 18 `Planter` (treść).
- Scenariusz ustawień (`SurvivalSettingsTests`) zmienia rekord drogą produkcyjną: intencja 33 administratora (K6-39) albo polecenie konsoli K6-35 — nie wstrzykiwać op 46.
- Scenariusz regresji „śmierć w trakcie przygotowania teleportu” (jeden komponent przygotowania K6-35/K6-37, jeden budżet) — kandydat do listy D14 (A0 dopisuje, jeśli K6-35/K6-37 nie mają takiego testu); nazwa testu właścicieli **niezweryfikowana**.

## Kontekst

Źródła prawdy: `docs/ZASADY.md` (całość, zwłaszcza §2–4, §6, §8–12), `docs/spec/03-przetrwanie.md` (Rozgrywka l. 42–186, Operacje l. 246–319, Sieć l. 321–347, Zapis l. 349–369, Obsługa błędów l. 371–391, Testy l. 393–414, Kryteria l. 416–423, Pakiety l. 425–442), plany K6-30…K6-35 w `docs/plans/`, plan K6-36 (patrz niżej).

Stan sprawdzono w worktree `k6-35` (tylko odczyt). Ta gałąź zawiera K6-30…K6-34 (K6-34 częściowo) i plan K6-35; **kod K6-35…K6-39 nie istnieje**, więc fakty o nich pochodzą z planów.

### Fakty sprawdzone w kodzie (worktree `k6-35`)

| Fakt | Konsekwencja dla K6-40 |
| --- | --- |
| `ProtocolConstants.cs` l. 7: `ProtocolVersion = 18` | Baza nie ma K6-34 = 19; nie przypinać testów do żadnej wartości (D17) |
| `OperationKind.cs`: 30–37, 40–45, 47–49; `BlobKind.cs`: 20, 21, 22, 25; `MessageType.cs`: 40–46, 48, 49 | Zgodne z planami (K6-35 dokłada 38/39/46, bloby 23/24, komunikat 47); K6-40 nic nie dokłada |
| `ItemConservationProperties.cs` (Core.Tests/Items) l. 20–21: 2000 sekwencji × 50 **operacji** | To test na poziomie operacji z K6-29; spec wymaga **intencji** 8 graczy (l. 399) |
| `GameRules.cs` l. 15: `TickRate = 60`; l. 44/47: `ItemIntentsPerSecond = 20`, `ItemIntentBurst = 40`; `IntentValidator.cs` l. 98 zwraca `RateLimited` (3) | Boty muszą tempować intencje; odmowy `RateLimited` liczone osobno (D8). 8 h świata = 1 728 000 ticków |
| `LinkConditions.cs` l. 6–7: `LossRate`, `DuplicateRate` dotyczą tylko wiadomości zawodnych; `InMemoryTransport.cs` l. 359–381: strata tylko dla `Unreliable`; l. 337–352: `Partition` trzyma dane `ReliableOrdered` i dostarcza je po `Heal` w kolejności, gubi zawodne i keep-alive; l. 420–425: kanał uporządkowany nie wyprzedza poprzednich (`due = max`) | Strata „niezawodna” = opóźnienie retransmisji z blokadą czoła kolejki, nie zgubienie (D7) |
| `InMemoryNetwork.cs`: `TimeProvider`, `SetConditions` (oba kierunki), `Partition`/`Heal` | Deterministyczne sterowanie siecią bez zmian w `Horyzont.Net` |
| `InventoryRig.cs` l. 99–110 (tak samo `FoodRig` l. 110–119, `PropsRig` l. 175–184, `FunctionalBlocksRig` l. 166–175): pętla `Stopwatch` 30 s + `await Task.Delay(1)` na klatkę | Na Windows `Task.Delay(1)` trwa ~15 ms; 1,7 mln ticków Soak trwałoby godziny. Nowy rig nie kopiuje tego wzorca (D10) |
| `InventoryRig.cs` l. 60, `FoodRig.cs` l. 62–64, `FunctionalBlocksRig.cs` l. 107–115: usługi serwera rejestrowane ręcznie, każdy rig inny podzbiór; `GameComposition.cs` l. 335–350: kolejność produkcyjna (`HostConstructs`, `InventoryService`, `StationService`, `ProgressReplication`, ramy, postacie, `VitalsService`) | Testy integracyjne mogą przejść przy brakującej rejestracji w grze; potrzebny test zgodności składania (D13) |
| `HashingJournalSink.cs`: dekorator `IJournalSink` zapisuje skrót i bajty terenu po każdej operacji (symulacja dziennikuje po zastosowaniu) | Gotowy punkt obserwacji każdej operacji, także serwerowej; wyrocznia K6-40 to taki sam dekorator (D2) |
| `FaultPoint.cs`: `BeforeJournalCommit`, `AfterJournalCommit`, `CheckpointMidTransaction`, `BeforeCheckpointCommit`, `AfterCheckpointCommit`; `ArmedFaults` (Integration Support) | Awaria „między składowymi” nie jest punktem zapisu: operacja złożona to jeden wpis; atomowość w pamięci zapewnia dziennik cofania appliera (D11) |
| `StateHasher.cs` l. 17–27: pomija `CurrentTick`, kinematykę graczy online, pozy statków, woksele terenu, vitals i zasoby kombinezonu | Równy skrót nie dowodzi równości tych danych; osobne porównania (D9) |
| Pięć testów alokacji istnieje: `AllocationTests.cs` l. 135, `PropHitAllocationTests.cs` l. 96, `PlanetActivityPolicyTests.cs` l. 203, `PropReplicatorTests.cs` l. 281, `SnapshotBuilderTests.cs` l. 47 | Etap E (opcjonalny zakres, DO USTALENIA-6) |
| `ci.yml` l. 32: zadanie testów .NET `timeout-minutes: 30` dla całego `dotnet test Horyzont.sln` (l. 49, bez Soak/Manual); l. 72: zadanie silnika 45 min; l. 149–160: oczekiwanie na wolny potok gdUnit | Testy `Slow` K6-40 mają twardy budżet (D6) |
| `nightly.yml` l. 22: 120 min; l. 40: `Category=Soak` dla całego rozwiązania | Soak K6-40 w osobnym zadaniu nocnym (D12) |
| Istniejące `Soak`: `CrashHostSoakTests`, `CodecSoakTests`; brak klas botów w `src/`, `tests/`, `tools/` | Boty K6-40 to kod testowy, bez ramy z K4-19 w tej bazie (**niezweryfikowane** na `main`) |
| `game/tests/Support/ModeFixture.cs` używa `GameComposition`; są `game/Horyzont.Game.csproj`, `game/Horyzont.Game.sln`, `game/.runsettings` | gdUnit przez istniejący adapter i fiksturę trybu |
| Istnieją `ProtocolContractTests.cs` (Core.Tests/Protocol), `Hashing/DeterminismTests.cs`, `TerrainJournalGoldenCorpusTests.cs`, `Survival/VitalsStepTests.cs`; `CraftingFlowTests`, `InventoryFlowTests`, `FoodFlowTests`, `PropsFlowTests`, `FunctionalBlocksFlowTests` używają `FaultPoint` | Macierz pokrycia wskazuje te testy zamiast je powielać |

### Plan K6-36

Codex nie znalazł planu K6-36 w worktree. Poprawiony plan Opus istnieje poza repo (`…/scratchpad/K6-36-budowanie-i-wytrzymalosc.md`, przegląd 2026-10-16). Potwierdza: intencja 34 (`BuildInspectIntent`), odmowy 51/52/55/58, `CompoundOp` v2, intencje 1/3 w formacie 2 (`AllowCollapse`), `ProtocolVersion = 21`, brak HP bloków, test `Core.Tests/Building/IntegrityPerformanceTests.Full64Cube_Within50Milliseconds` (`Slow`, Release, maszyna referencyjna), `BuildCostTests.FreeBuilding_NeitherConsumesNorRefunds`. Wersja w `main` w chwili startu K6-40: **niezweryfikowane** (A0).

### Plany K6-37…K6-39 (szkice Codexa, bez przeglądu Opus w chwili pisania)

Wszystkie trzy deklarują `ProtocolVersion = 22` (`plan-k6-37-codex.md` l. 12, `plan-k6-38-codex.md` l. 12, `plan-k6-39-codex.md` l. 169). To kolizja między nimi, nie sprawa K6-40; K6-40 czyta wartość z bazy. K6-38 i K6-39 zostawiają pełną pętlę botów K6-40 (`plan-k6-38` l. 551, `plan-k6-39` l. 430). K6-39 ma własne testy UI i lokalizacji ustawień (`WorldSettingsLocalizationTests`, `WorldSettingsSceneTests`). Ostateczne nazwy klas i testów K6-37…K6-39: **niezweryfikowane**.

## Zakres

1. Macierz pokrycia: każde wymaganie spec 03 (Testy l. 397–414, Kryteria l. 418–423) → konkretny test, kategoria, komenda, wynik.
2. Właściwość zachowania przedmiotów: 10 000 sekwencji intencji, 8 graczy w każdej.
3. Test 8 botów przy jednej skrzyni: 10 000 intencji, RTT 250 ms, 5% strat.
4. Pełna pętla 8 graczy (Kryterium 1) przez sesje, sieć w pamięci i zapis.
5. Determinizm, checkpoint z ogonem, awarie i restart całego łańcucha.
6. Ustawienia świata: każde działa u swojego konsumenta i przetrwa restart (matryca nad testami pakietów właścicieli).
7. Scenariusze regresji między pakietami (D14).
8. Soak: długi horyzont czasu świata.
9. gdUnit: zgodność składania serwera w grze z rigiem i jeden scenariusz hosta bez okna.
10. (Opcjonalnie, DO USTALENIA-6) stabilizacja pięciu testów alokacji.

Poza zakresem:

| Co | Właściciel |
| --- | --- |
| Implementacja śmierci, nagrobków, ustawień świata (rekord 46/23/47) | K6-35 |
| Koszty, wytrzymałość, zawalenia, `CompoundOp` v2, test wydajności 64³ | K6-36 |
| Teleporty, ładownia, tankowanie, uprawy | K6-37, K6-38 |
| Ekrany, testy UI każdej akcji, lokalizacja ustawień, uprawnienia administratora | K6-39 |
| Podnoszenie w 2 m i wygaszanie przedmiotów leżących | brak pakietu (DO USTALENIA-5) |
| Rzeczywiste retransmisje UDP/Steam, Linux | poza K6-40 |
| Balans, walka, bossowie | K7/K8 |

## Decyzje

### D1. Pakiet tylko testowy

Zmiany domyślnie w `tests/`, `game/tests/`, `docs/reports/`, `docs/plans/`, `.github/workflows/`. Zakaz zmian w `src/` i `game/src/` „dla wygody testów” (publiczne haki, flagi testowe, nowe konstruktory). Błąd znaleziony testem: test czerwony → zgłoszenie w raporcie → minimalna poprawka **tylko** po zgodzie Opus; poprawka w ścieżce formatu (ZASADY §3) wymaga decyzji o wersji (D17). Brakującej funkcji nie implementuje K6-40 (status `Blocked`).

### D2. Wyrocznia zachowania = dekorator dziennika + suma wszystkich kontenerów

- Obserwator: `SurvivalLedgerSink : IJournalSink` (wzór `HashingJournalSink`), owija prawdziwy ujście dziennika. Widzi każdą zastosowaną operację: intencje graczy i operacje serwera (rozliczenia stacji i generatora, odrastanie, śmierć, zawalenia, upływ upraw).
- Liczniki: dla każdego typu `Created[type]` i `Destroyed[type]` z `ItemCreateOp`/`ItemDestroyOp`, także składowych `CompoundOp` (rozkład operacji złożonej przez publiczny typ `CompoundOp`, nie przez kod appliera). Osobno źródło (`Harvest`, `Craft`, `Station`, `Crop`, `Refund`, `Loot`, `Admin`, `Recovered`, `Starter`) i ujście.
- Stan faktyczny: suma po **wszystkich** kontenerach `WorldState` (iteracja po rejestrze kontenerów, nie po liście rodzajów gospodarzy). Dodatkowa straż: zbiór napotkanych rodzajów gospodarzy jest raportowany; nowy rodzaj nie może być pominięty, bo sumujemy wszystko.
- Niezmiennik po każdym `Append`: `Sum(type) == Initial(type) + Created(type) − Destroyed(type)` i brak ujemnych stosów.
- Bloki, paliwo statków, vitals i zasoby kombinezonu nie są przedmiotami: nie wchodzą do sumy (koszt budowy = ujście `BuildCost`, zwrot = źródło `Refund`, tankowanie = ujście `Fuel`).
- Wyrocznia nie woła appliera ani resolvera i nie liczy ponownie po replikacji, powtórce ani restarcie (stan bazowy po wczytaniu = nowe `Initial`, test `CheckpointBaseline_IsCountedOnce`).

### D3. Bilans nie wystarcza: oczekiwania źródeł i ujść

Podwójny `ItemCreateOp` przechodzi bilans. Dlatego scenariusz liczy niezależnie oczekiwane ilości z treści i akcji: urobek z danych (K6-31), wynik receptury, wyjście pieca po N tickach (spec l. 402), plon (K6-38), zwrot według zapisanej wersji kosztu (K6-36), koszt tankowania (K6-37), kara śmierci (spec l. 149–153). Transfer do nagrobka i odzyskanie to przeniesienia (bez create/destroy); `Recovered` tylko tam, gdzie plan K6-35 tak definiuje (**niezweryfikowane**, A0).

### D4. Właściwość: intencje przez prawdziwy walidator i resolver, bez sieci i SQLite na sekwencję

- Akcje generatora to intencje (`ItemMoveIntent` 20, `ItemDropIntent` 21, `QuickStackIntent` 22, `TakeAllIntent` 23, `CraftIntent` 24, `EatIntent` 25, `SuitRefillIntent` 26, `PropHitIntent` 27, `BlockInteractIntent` 28, `BlockConfigIntent` 29, budowa i rozbiórka z K1-03/K6-36, `TeleportIntent` 30, `PlantIntent` 31, `CropHarvestIntent` 32) oraz zdarzenia serwera (tiki, śmierć przez ścieżkę produkcyjną, D5). Nigdy operacje wstrzykiwane wprost.
- 10 000 sekwencji × 8 graczy × domyślnie 50 akcji (parametr; DO USTALENIA-3). Pojedyncza sekwencja nie może tworzyć `ServerRuntime` z plikiem SQLite: 10 000 × koszt startu przekroczy budżet D6. A0 mierzy dwa warianty: (a) `ServerSimulation` + uczestnicy ticka z `Horyzont.Server` bez transportu; (b) jeden `ServerRuntime` na sekwencję z bazą w pamięci (**czy `ServerRuntime` to umożliwia — niezweryfikowane**). Wybór zatwierdza Opus na A4.
- Pokrycie obowiązkowe (spec l. 399): przeniesienia, rzemiosło, jedzenie, budowa, rozbiórka, śmierć; dodatkowo stacje z czasem, kombinezon, uprawy, teleport, tankowanie. Test kończy się porażką, gdy którakolwiek rodzina ma zero **przyjęć** albo zero **odmów** w całym przebiegu, gdy któryś z trzech trybów kary nie wystąpił, albo gdy udział przyjęć < 30% (próg do kalibracji na A3, nie obniżać bez Opus).
- FsCheck (`FsCheck.Xunit.v3`): stałe ziarno w CI + zapis ziarna i rozmiaru przy porażce; zmniejszanie zachowuje przygotowanie świata (generator buduje sekwencję akcji, a nie stany pośrednie).

### D5. Śmierć tylko ścieżką produkcyjną; jeden pisarz stanu postaci

Zakaz w nowych testach: przypisywania `PlayerState.Vitals`, wołania `ServerSimulation.UpdatePlayerVitals` spoza `VitalsService`, tworzenia `PlayerVitals` jako wejścia do symulacji (ZASADY l. 33; K6-34 D22; K6-35 D5). Śmierć wywołuje zagrożenie (próżnia bez kombinezonu, profil testowy z dużym natężeniem) albo upadek — tą samą drogą, której używa `DeathFlowTests` K6-35 (A0 wskazuje dokładny mechanizm; **niezweryfikowane**). Reguły testowe (`VitalsRules`, `HazardRules`) wolno podać przez istniejące parametry konstrukcji, jedną instancją (lekcja K6-33/K6-34). Straż w B5/D4: `rg -n "\.Vitals = |UpdatePlayerVitals\(" tests/ game/tests/` — brak trafień w plikach K6-40.

### D6. Budżet czasu i kategorie

- `ci.yml` daje 30 min na **całe** `dotnet test Horyzont.sln`. Właściwość 10 000 sekwencji ≤ 5 min i test 8 botów ≤ 3 min (Release, runner CI) — wtedy `Slow` w CI.
- Jeśli A3/B3 zmierzą więcej: pełne przebiegi przechodzą do nocnego zadania K6-40 (spec l. 423 dopuszcza „opisane przebiegi”), a w CI zostaje wariant `Slow` 1000 sekwencji / 2000 intencji z tymi samymi strażami. Zmiana wymaga zgody właściciela (DO USTALENIA-3); niedozwolone jest ciche zmniejszenie liczby w kodzie.
- Każdy test `Slow`/`Soak` K6-40 ma jawny limit (`CancellationTokenSource` z czasem rzeczywistym tylko jako bezpiecznik; semantyka testu na `FakeTimeProvider`).

### D7. Model sieci: RTT 250 ms i 5% strat

- Opóźnienie: `LinkConditions(Latency = 125 ms, Jitter = 0, …)` w obu kierunkach (`SetConditions`), osobny klient na bota.
- Strata zawodna: `LossRate = 0.05`, `DuplicateRate = 0` (migawki, vitals).
- Strata niezawodna: prawdziwy transport niezawodny nie gubi ramek aplikacji, tylko je retransmituje. Model: testowy dekorator `ITransport` w `tests/` (`LossyReliableTransport`), który z prawdopodobieństwem 0,05 (ziarno) zatrzymuje ramkę `ReliableOrdered` na czas retransmisji (domyślnie 2 × RTT = 500 ms) i **wszystkie** kolejne ramki tego połączenia do jej wydania (blokada czoła kolejki). Nie gubi, nie duplikuje, nie zmienia kolejności. Zakładany po stronie klienta i serwera (czy `ServerRuntime`/`ClientSession` przyjmują dowolny `ITransport` — **niezweryfikowane**, A0; wariant zapasowy: impulsy `Partition`/`Heal` z ziarna, krótsze niż `TransportOptions.Timeout`).
- Testy kontraktu dekoratora przed użyciem (B1): kolejność, jednokrotność, odsetek zatrzymań 5% ± 1 p.p. na 10 000 ramek, determinizm ziarna.
- Raport nie twierdzi, że sprawdzono retransmisję UDP/Steam.

### D8. Test 8 botów: wyrocznia slotowa, tempo i zbieżność

- Każdy bot ma własny typ przedmiotu (8 typów) i tylko przeniesienia/zamiany/`QuickStack`/`TakeAll` między swoim plecakiem i wspólną skrzynią (stoi ≤ 6 m, spec l. 339). W tym teście nie ma create/destroy, więc liczba sztuk każdego typu jest stała; duplikat i zgubienie są widoczne osobno dla każdego bota.
- Dodatkowo niezależny **model slotów**: przyjęte `ItemTransferOp`/`ItemSwapOp` z dziennika stosowane do prostego słownika `(kontener, slot) → (typ, liczba)`; na końcu model == stan serwera slot w slot.
- Tempo: każdy bot ≤ 20 intencji/s, start z pełnym kubełkiem 40 (`GameRules` l. 44/47); w wariancie bazowym `RateLimited` = 0, w osobnym wariancie burst > 40 sprawdzamy, że odmowa `RateLimited` nie ma skutków. Liczymy dokładnie 10 000 **wysłanych** intencji; raport: przyjęte, `ContainerChanged`, `NotEnoughItems`, inne odmowy, odpowiedzi brakujące = 0.
- Konflikty muszą wystąpić: ≥ 1% intencji kończy się `ContainerChanged` albo `NotEnoughItems` (inaczej test nic nie sprawdza).
- Po wyciszeniu (brak intencji, opróżnione kolejki dekoratora): lustro kontenerów każdego klienta == serwer, a przewidywania klienta cofnięte przy odmowie (spec l. 341).

### D9. Determinizm i dane poza skrótem

1. Ten sam ślad (stan, intencje, harmonogram dostarczeń, ziarna) w dwóch świeżych `ServerRuntime` → identyczne bajty dziennika i skróty po każdej operacji. W jednym procesie nie wykryje to kolejności `Dictionary<string,…>` (losowe ziarno skrótu łańcuchów jest per proces), więc nocny wariant porównuje skrót strumienia dziennika z dwóch procesów (wzór `Horyzont.Persistence.CrashHost`; przydatność — **niezweryfikowane**, A0; brak możliwości = `Blocked` w raporcie, nie pominięcie).
2. Checkpoint + trwały ogon → ten sam skrót co `HashingJournalSink.HashAt[ostatnia trwała OpId]` (spec l. 412, kryterium l. 420).
3. Inny harmonogram może zmienić zwycięzcę konfliktu; wtedy wymagamy tylko D2/D8, nie identycznej historii.
4. Poza skrótem porównujemy osobno: vitals i zasoby kombinezonu po restarcie z checkpointu (równe wartości z checkpointu, nie z chwili awarii), bajty terenu (`TerrainAt`), pozy offline, lustra klientów.

### D10. Rig bez czasu rzeczywistego

`SurvivalRig` steruje wyłącznie `FakeTimeProvider.Advance` + `Runtime.Tick()` + `Poll` klientów i dekoratora. Zakaz `Task.Delay`, `Thread.Sleep`, `Stopwatch` w logice. Oczekiwanie na zapis = jawna bariera writera (flush/oczekiwanie na zadanie zapisu), nie odpytywanie. A0 ustala, po co istniejące rigi mają `Task.Delay(1)` (hipoteza: postęp wątku writera; **niezweryfikowane**) i jaki publiczny mechanizm go zastępuje; brak takiego mechanizmu = pytanie do Opus, nie nowy hak w `src/`.

### D11. Awarie na prawdziwych granicach

- Punkty: pięć wartości `FaultPoint` przez `ArmedFaults`. Wyrocznia po awarii = trwały prefiks dziennika; operacja potwierdzona klientowi, ale nietrwała, nie jest uznawana za zapisaną.
- „Awaria między składowymi” (spec l. 401) realizujemy dwojako: (a) awaria przed/po zatwierdzeniu wpisu z operacją złożoną (całość albo nic po powtórce); (b) niespełniony warunek późnej składowej w żywym stanie → cofnięcie z dziennika cofania, zero skutków (np. rzemiosło z 5 skrzyń, gdy inny gracz w tym samym ticku zabierze składnik z piątej).
- Brak potrzebnego punktu awarii zapisujemy w raporcie; nie dodajemy punktu do `src/`.

### D12. Soak

- Domyślnie 8 h czasu świata (1 728 000 ticków przy 60 Hz), 8 botów w cyklach: zbieranie, stacje, uprawy, tankowanie, zagrożenia, śmierć, odzyskanie, ≥ 3 restarty z checkpointu i ≥ 1 awaria `AfterJournalCommit`.
- A0 mierzy koszt ticka 8 graczy; limit 90 min wymaga ≤ ~3 ms/tick. Gdy więcej: krótszy horyzont do decyzji właściciela (DO USTALENIA-3), bez przyspieszania reguł gry.
- Osobne zadanie w `nightly.yml` (własny `timeout-minutes`), aby nie wypierać istniejących `Soak`; filtr istniejącego zadania wyklucza testy K6-40 (`Category=Soak&FullyQualifiedName!~Survival`).

### D13. gdUnit: składanie gry, nie ekrany

- `SurvivalCompositionTests` (gdUnit, `ModeFixture`): lista i kolejność uczestników ticka serwera w trybie Solo/Host po `GameComposition` == lista, którą rejestruje `SurvivalRig` (jedno źródło listy w teście; jeśli `ServerRuntime` nie ujawnia uczestników — porównanie przez obserwowalne skutki: każda usługa reaguje na swoją intencję w jednym przejściu; **niezweryfikowane**).
- `SurvivalHostLoopTests` (gdUnit, bez okna): jeden scenariusz hosta: zbiór, rzemiosło, skrzynia, transfer, jedzenie, śmierć, odzyskanie nagrobka, restart. Asercje na stanie serwera i lustrach klienta, nie na wyglądzie.
- Testy ekranów, każdej akcji UI i tłumaczeń należą do K6-39; K6-40 tylko wskazuje je w macierzy i dopisuje test brakującego tekstu odmowy 40–58, jeśli K6-39 go nie ma.

### D14. Scenariusze regresji między pakietami

Każdy jako osobny test `Survival*RegressionTests` (Integration), z oczekiwaniem z planu właściciela; rozbieżność = zgłoszenie, nie poprawka bez Opus:

1. Intencje przedmiotów martwego gracza i gracza w przejściu: odmowa `InTransition` (48), kontenery zamrożone (spec l. 297); opóźnione intencje sprzed śmierci dochodzą po odrodzeniu.
2. Śmierć z otwartym nagrobkiem/stacją: subskrypcje kończą się przed operacją śmierci (spec l. 383).
3. `ItemMove`/`Swap`/`QuickStack` do nagrobka: odmowa (K6-35 D13).
4. Śmierć podczas teleportu między planetami i restart w trakcie przejścia (60 s, spec l. 385).
5. Rzemiosło z 5 skrzyń, gdy w tym samym ticku stacja się rozlicza i inny gracz zabiera składnik.
6. Tankowanie z ogniw w skrzyni w 20 m, gdy inny gracz je zabiera; darmowe paliwo nie zużywa ogniw (spec l. 160).
7. Budowanie bez kosztów: włącz → postaw → wyłącz → rozbierz. Oczekiwanie K6-36: brak zwrotu za blok postawiony za darmo? Plan K6-36 mówi tylko, że tryb wyłącza pobieranie i zwrot **w trakcie** trybu i nie przepisuje rewizji — możliwa pętla „przedmioty z niczego” (DO USTALENIA-7).
8. Zmniejszenie mnożnika stosów: istniejące stosy się nie dzielą, nowe łączenie respektuje nowy limit, bilans bez zmian (spec l. 185).
9. Zmiana mnożnika surowców w trakcie gry: urobek zgodny z ustawieniem w chwili akcji.
10. Teleport z przedmiotem masowym w plecaku/pasku/wyposażeniu bez `TeleportAll` i z nim; przełączenie ustawienia w trakcie przejścia.
11. Nadmiar urobku/zwrotu/plonu: przedmiot leżący pod nogami (spec l. 381) wchodzi do sumy D2.
12. Kara Łagodna/Normalna/Twarda: zawartość nagrobka i wyposażenie po odrodzeniu (spec l. 149–153); pusty nagrobek znika.

### D15. Raport pokrycia: dowód, nie procent linii

`docs/reports/K6-40-pokrycie-przetrwania.md`: każde wymaganie → test (ścieżka, nazwa), kategoria, komenda, commit, wynik CI/nocny, liczby (sekwencje, intencje, gracze, ticki, przyjęcia/odmowy), ograniczenia, status `Covered`/`Partial`/`Missing`/`Blocked`/`Unverified`, właściciel luki. Bramka K6 wymaga zero `Missing`; `Blocked` tylko z decyzją właściciela.

### D16. Stabilizacja alokacji (etap E, warunkowo)

Pomiar po jawnej rozgrzewce, na tym samym wątku, bez asercji i formatowania w mierzonym fragmencie. Zakaz: podnoszenia progu, „najlepszy z N”, przenoszenia do `Manual`/`Soak`, automatycznych ponowień. Pierwsza zmiana to test kontrolny: celowa alokacja w mierzonym fragmencie musi dać porażkę. Rzeczywista alokacja produkcyjna = osobna diagnoza i przegląd Opus (zmiana w `src/`).

### D17. Wersje

K6-40 nie podbija `ProtocolVersion` ani wersji formatów i nie zmienia `ProtocolContractTests`. Jeśli poprawka błędu wymaga podbicia, to decyzja Opus; wartość = `main` + 1 w chwili scalenia, nie mniej niż 22 (K6-34 = 19, K6-35 = 20, K6-36 = 21; 22+ prawdopodobnie zajmą K6-37…K6-39).

## Kontrakty chronione i wersje

| Obszar | K6-40 |
| --- | --- |
| `src/Horyzont.Core/` (operacje, intencje, applier, serializacja, protokół, `Checkpoints/`) | brak zmian planowanych; wyjątek tylko przez D1 + Opus |
| `Hashing/StateHasher.cs`, tagi `0x53`/`0x42`/`0x50`/`0x48`/`0x44`/`0x4B`/`0x46` (+ `0x54`/`0x57` z K6-35) | bez zmian |
| Migracje `src/Horyzont.Persistence/Migrations/` | bez nowych; testy migracji używają istniejących fikstur |
| Złoty korpus `Golden/TerrainJournals/`, `TerrainJournalGoldenCorpusTests.cs`, `Hashing/DeterminismTests.cs` | `git diff <A0>..HEAD` pusty |
| `game/src/`, `game/content/`, `game/locale/` | bez zmian (błędne warianty treści = kopie testowe) |
| `src/Horyzont.Net/` | bez zmian (model strat w `tests/`, D7) |

### Numeracja

K6-40 **nie zajmuje** żadnego kodu: operacji, intencji, odmowy, komunikatu, bloba, rodzaju bytu, tagu skrótu ani wersji protokołu.

| Rodzaj | W kodzie (`k6-35`) | Zaplanowane przez zależności |
| --- | --- | --- |
| Operacje 30–49 | 30–37, 40–45, 47–49 | K6-35: 38, 39, 46 (zakres pełny) |
| Intencje 20–39 | 20–25, 27–29 | K6-34: 26; K6-36: 34; spec: 30–33 (K6-37/38/39); 35–39 wolne według planu K6-36 |
| Odmowy 40–59 | 40–46, 49, 53, 54, 57 | K6-35: 48; K6-36: 51, 52, 55, 58; spec: 47, 50, 56; 59 wolna |
| Komunikaty 40–49 | 40–46, 48, 49 | K6-35: 47 (zakres pełny) |
| Bloby 20–29 | 20, 21, 22, 25 | K6-35: 23, 24; 26–29 wolne |
| `ProtocolVersion` | 18 | K6-34 = 19, K6-35 = 20, K6-36 = 21, K6-37 = 22, K6-38 = 23, K6-39 = 24 (uzgodnione 2026-10-17) |

## Projekt

### Pliki (propozycje; nazwy zależności potwierdza A0)

| Obszar | Pliki |
| --- | --- |
| Wyrocznia | `tests/Horyzont.Testing/Survival/SurvivalLedger.cs`, `SurvivalLedgerSink.cs`, `SlotModel.cs`, `SurvivalTrace.cs` |
| Generator | `tests/Horyzont.Testing/Survival/SurvivalActionGenerator.cs`, `SurvivalWorldSetup.cs` |
| Rig i sieć | `tests/Horyzont.Integration.Tests/Support/SurvivalRig.cs`, `SurvivalBot.cs`, `LossyReliableTransport.cs` |
| Właściwość | `tests/Horyzont.Integration.Tests/Survival/SurvivalConservationProperties.cs`, `SurvivalOracleTests.cs`, `SurvivalGeneratorTests.cs` (projekt docelowy — Integration albo Server.Tests — po pomiarze A0) |
| Przekrojowe | `…/Survival/SurvivalLatencyTests.cs`, `LossyReliableTransportTests.cs`, `SurvivalLoopTests.cs`, `SurvivalReplayTests.cs`, `SurvivalCrashTests.cs`, `SurvivalSettingsTests.cs`, `SurvivalRegressionTests.cs`, `SurvivalContentTests.cs` |
| Soak | `…/Survival/SurvivalSoakTests.cs` |
| gdUnit | `game/tests/Survival/SurvivalCompositionTests.cs`, `SurvivalHostLoopTests.cs` |
| Raporty | `docs/reports/K6-40-pokrycie-przetrwania.md`, (E) `docs/reports/K6-40-stabilnosc-alokacji.md` |
| CI | `.github/workflows/ci.yml`, `.github/workflows/nightly.yml` |

Jeden publiczny typ na plik (ZASADY §5); typy testowe `internal sealed` tam, gdzie wystarczy.

### Przepływ

```text
ziarno + SurvivalWorldSetup (treść produkcyjna K6, ustawienia)
  → 8 × ClientSession ⇄ LossyReliableTransport ⇄ InMemoryNetwork(125 ms) ⇄ ServerRuntime
  → ServerSimulation → SurvivalLedgerSink(IJournalSink) → HashingJournalSink → prawdziwy writer
  → niezmiennik po każdym Append; model slotów; oczekiwania źródeł
  → checkpoint / awaria / restart / powtórka → porównania D9
```

Obserwator nie wywołuje operacji (brak reentrancji); działania botów planuje harmonogram testu.

## Testy akceptacyjne — napisz najpierw

| Test | Kryterium |
| --- | --- |
| `SurvivalOracleTests.InjectedDuplicateCreate_IsDetected`, `…LostItem_IsDetected`, `…DoubleCountedCompound_IsDetected`, `…WrongSourceQuantity_IsDetected` | Wyrocznia łapie celowe uszkodzenia (podmieniony sink/stan testowy, nie kod produkcyjny) |
| `SurvivalOracleTests.CheckpointBaseline_IsCountedOnce`, `RefusedCompound_DoesNotChangeLedger`, `AcceptedCompound_AccountsComponentsOnce` | Brak podwójnego liczenia |
| `SurvivalGeneratorTests.FullRun_CoversEveryFamily_AcceptedAndRefused` | Każda rodzina D4 ma przyjęcia i odmowy; trzy tryby kary; udział przyjęć ≥ próg |
| `SurvivalGeneratorTests.SameSeed_SameActions`, `Shrunk_KeepsValidSetup` | Odtwarzalność i zmniejszanie |
| `SurvivalConservationProperties.RandomIntentSequences_EightPlayers_ConserveEveryItem` | 10 000 sekwencji; niezmiennik po każdej operacji (`Slow` albo nocny wg D6) |
| `LossyReliableTransportTests.*` | Kolejność, jednokrotność, 5% ± 1 p.p., determinizm |
| `SurvivalLatencyTests.SharedChest_EightBots_TenThousandIntents_NoDuplicationNoLoss` | D8: stałe liczby typów, model slotów == serwer, ≥ 1% konfliktów, `RateLimited` = 0, lustra == serwer |
| `SurvivalLatencyTests.BurstAboveBucket_RateLimitedHasNoEffect` | Odmowa `RateLimited` bez skutków |
| `SurvivalLoopTests.EightPlayers_CompleteSurvivalLoop_AndReturnHome` | Każdy z 8: zbiór, rzemiosło, baza z zasobów, kombinezon, księżyc, śmierć, odzyskanie, powrót; liczniki etapów per gracz == 8 |
| `SurvivalReplayTests.SameTrace_TwoRuntimes_SameJournalBytesAndHashes` | D9.1 |
| `SurvivalReplayTests.CheckpointAndTail_HashEqualsLiveHashAtLastDurableOp` | D9.2, kryterium l. 420 |
| `SurvivalReplayTests.Restart_PreservesOffHashData` | D9.4 |
| `SurvivalCrashTests.<akcja>_<FaultPoint>_AllOrNothing` | Rzemiosło z 5 skrzyń, budowa, śmierć, odzyskanie, tankowanie × 5 punktów |
| `SurvivalCrashTests.LateComponentRefused_RollsBackEarlierComponents` | D11(b) |
| `SurvivalSettingsTests.<ustawienie>_ChangesConsumer_AndSurvivesRestart` | 8 aktywnych ustawień (spec l. 174–181); zarezerwowane bez skutku |
| `SurvivalRegressionTests.*` | 12 scenariuszy D14 |
| `SurvivalContentTests.ProductionContent_Valid`, `RemovedType_…`, `UnknownIngredient_…`, `SeventeenthCostRevision_…` | Spec l. 414 — tylko jeśli właściciele nie mają; inaczej wskazanie w macierzy |
| `SurvivalSoakTests.EightPlayers_LongHorizon_PreserveInvariants` | D12; straże minimalnej liczby ticków, aktywności każdego bota, restartów |
| `SurvivalCompositionTests.HostRegistersSameServerParticipantsAsRig` | D13 |
| `SurvivalHostLoopTests.SoloHost_HarvestCraftChestEatDieRecoverRestart` | D13 |

Każdy test „bez skutku” ma bliźniaka przyjętego (lekcja K6-34 A1: test przechodził przypadkiem).

### Macierz pokrycia (punkt wyjścia; A0 uzupełnia nazwy)

| Wymaganie spec 03 | Istniejące / właściciel | Praca K6-40 |
| --- | --- | --- |
| Zachowanie przedmiotów (l. 399) | `ItemConservationProperties` (operacje, 2 graczy) | D4 |
| Duplikacja pod opóźnieniem (l. 400) | brak | D7, D8 |
| Atomowość rzemiosła (l. 401) | `CraftingFlowTests` (FaultPoint) | D11 w pętli wieloosobowej |
| Piec według ticków (l. 402) | `CraftingFlowTests` | Soak: różne harmonogramy otwierania |
| Krzywa jedzenia (l. 403) | `VitalsStepTests` | wskazanie |
| Zagrożenia (l. 404) | K6-34 (sceny referencyjne) | wskazanie + pętla księżycowa |
| Wytrzymałość: wydajność (l. 405) | K6-36 `IntegrityPerformanceTests.Full64Cube_Within50Milliseconds` (plan) | wynik z maszyny referencyjnej w raporcie |
| Wytrzymałość: poprawność, statek (l. 406–407) | K6-36 (plan) | wskazanie; kaskada przy kopaniu w D2 |
| Śmierć (l. 408) | K6-35 `DeathFlowTests`, `DeathConservationProperties` (plan) | D14.12, pętla |
| Teleporty (l. 409) | K6-37 (szkic) | D14.4, D14.10 |
| Zwrot kosztów (l. 410) | K6-36 `BuildCostTests` (plan) | D14.7 |
| Uprawy (l. 411) | K6-38 (szkic) | Soak, D3 |
| Odtwarzanie (l. 412) | `K1GateTests`, flow testy | D9 |
| Migracja (l. 413) | testy Persistence + fikstury | wskazanie; kontenery przy wejściu w `SurvivalReplayTests` |
| Walidacja treści (l. 414) | testy Content | `SurvivalContentTests` dla braków |
| Kryterium l. 418 (pętla 8 graczy) | — | `SurvivalLoopTests` |
| Kryterium l. 421 (zawalenie > 10 wymaga potwierdzenia) | K6-36 (plan) | wskazanie |
| Kryterium l. 422 (ustawienia) | K6-35/K6-39 | `SurvivalSettingsTests` |

## Kroki

Każdy krok: test (czerwony) → kod testowy → zielone → commit Conventional Commits (`test:`, `ci:`, `docs:`; `fix:` tylko po zgodzie Opus). Nazwy testów po angielsku, kategorie przez `[Trait("Category", …)]`.

### Etap A — baza, wyrocznia, generator

- [ ] **A0. Baza i pomiary.** Zapisać commit bazy, `ProtocolVersion`, obecność kontraktów K6-35…K6-39 (`rg -n "PersistentEntityCreate|WorldSettingsSet|TeleportIntent|PlantIntent|CropHarvestIntent|BuildInspectIntent|ProtocolVersion =" src`). Ustalić: drogę śmierci w `DeathFlowTests` (D5), powód `Task.Delay(1)` w rigach i mechanizm bariery writera (D10), czy `ServerRuntime`/`ClientSession` przyjmują dowolny `ITransport` (D7), czy `ServerRuntime` działa z bazą w pamięci, koszt startu runtime i koszt ticka 8 graczy (D4, D12), przydatność `CrashHost` do D9.1. Szkic macierzy pokrycia.
  Akceptacja: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~ProtocolContract|FullyQualifiedName~DeterminismTests|FullyQualifiedName~TerrainJournalGoldenCorpus"` zielone; pomiary w raporcie.
  Brak scalonej zależności = stop i pytanie do właściciela; nie pisać testów na atrapach K6-35…K6-39.
- [ ] **A1. Czerwone testy wyroczni.** `SurvivalOracleTests` (celowe uszkodzenia, podwójne liczenie, bazowy stan po wczytaniu).
  Akceptacja: `dotnet test tests/Horyzont.Integration.Tests --filter "FullyQualifiedName~SurvivalOracle"` — czerwone z powodu braku implementacji, nie błędnej fikstury.
- [ ] **A2. Wyrocznia.** `SurvivalLedger`, `SurvivalLedgerSink`, `SlotModel`, `SurvivalTrace` (pierwsza rozbieżność: OpId, składowa, typ, oczekiwane/faktyczne).
  Akceptacja: jak A1, zielone.
- [ ] **A3. Generator i właściwość.** Najpierw `SurvivalGeneratorTests`; potem generator i `SurvivalConservationProperties` na wariancie wybranym z A0. Zmierzyć czas 10 000 sekwencji w Release.
  Akceptacja: `dotnet test tests/Horyzont.Integration.Tests -c Release --filter "FullyQualifiedName~SurvivalGenerator|FullyQualifiedName~SurvivalConservation"`.
- [ ] **A4. Bramka A — Opus.** Wyrocznia niezależna od appliera; celowe uszkodzenie w generatorze (np. podwójne `ItemCreate` w sinku testowym) obala właściwość; wybór wariantu D4 i budżet D6; brak zapisów `Vitals` w testach.
  Akceptacja: komendy A1–A3 zielone + raport czasu.

### Etap B — sieć, boty, pętla, ustawienia

- [ ] **B1. Kontrakt modelu strat.** `LossyReliableTransportTests` przed dekoratorem.
  Akceptacja: `dotnet test tests/Horyzont.Integration.Tests --filter "FullyQualifiedName~LossyReliableTransport"`.
- [ ] **B2. Rig i boty.** Najpierw `EightClients_HaveIndependentContainersAndMirrors`, `SameSeed_SameDeliverySchedule`, `Rig_UsesNoWallClock` (rig działa przy `FakeTimeProvider` bez postępu czasu rzeczywistego). Rejestracja usług w kolejności `GameComposition` (D13), jedna lista w kodzie testu.
  Akceptacja: `dotnet test tests/Horyzont.Integration.Tests --filter "FullyQualifiedName~SurvivalRig"`.
- [ ] **B3. Wspólna skrzynia.** `SurvivalLatencyTests` (D8), pomiar czasu.
  Akceptacja: `dotnet test tests/Horyzont.Integration.Tests -c Release --filter "FullyQualifiedName~SurvivalLatency"`.
- [ ] **B4. Pętla, ustawienia, regresje, treść.** `SurvivalLoopTests`, `SurvivalSettingsTests` (8 ustawień + zarezerwowane; odmowa nie-administratora tylko wskazana w K6-39), `SurvivalRegressionTests` (D14), `SurvivalContentTests` (tylko braki).
  Akceptacja: `dotnet test tests/Horyzont.Integration.Tests --filter "FullyQualifiedName~SurvivalLoop|FullyQualifiedName~SurvivalSettings|FullyQualifiedName~SurvivalRegression|FullyQualifiedName~SurvivalContent"`.
- [ ] **B5. Bramka B — Opus.** Pominięcie śmierci, wyprawy albo jednego klienta wywraca `SurvivalLoopTests`; brak bezpośrednich zapisów stanu świata (`rg -n "State\.(Containers|Players)\b.*=|\.Vitals = |UpdatePlayerVitals\(" tests/Horyzont.Integration.Tests/Survival tests/Horyzont.Testing/Survival`); uczciwy opis strat; wyniki D14 z rozbieżnościami zgłoszonymi właścicielom.
  Akceptacja: `dotnet test tests/Horyzont.Integration.Tests -c Release --filter "FullyQualifiedName~Survival&Category!=Soak&Category!=Manual"`.

### Etap C — powtórka, awarie, Soak

- [ ] **C1. Czerwone testy determinizmu.** `SurvivalReplayTests` (D9.1, D9.2, D9.4).
  Akceptacja: `dotnet test tests/Horyzont.Integration.Tests --filter "FullyQualifiedName~SurvivalReplay"`.
- [ ] **C2. Macierz awarii.** `SurvivalCrashTests` (5 akcji × 5 `FaultPoint` + D11(b)).
  Akceptacja: `dotnet test tests/Horyzont.Integration.Tests --filter "FullyQualifiedName~SurvivalCrash"`.
- [ ] **C3. Migracja i wejście.** Stara fikstura → migracja → gracze dostają kontenery przy wejściu, bez podwójnego zestawu startowego (spec l. 367, 413). Nie edytować migracji ani fikstur.
  Akceptacja: `dotnet test Horyzont.sln --filter "FullyQualifiedName~SurvivalReplay|FullyQualifiedName~Migration"`.
- [ ] **C4. Soak.** Najpierw straże (minimalna liczba ticków, aktywność każdego bota, ≥ 3 restarty, ≥ 1 awaria); potem przebieg D12; wariant dwuprocesowy D9.1, jeśli A0 potwierdził `CrashHost`.
  Akceptacja: `dotnet test tests/Horyzont.Integration.Tests -c Release --filter "Category=Soak&FullyQualifiedName~SurvivalSoak"`.
- [ ] **C5. Bramka C — Opus.** Wyrocznia po awarii = trwały prefiks; dane poza skrótem porównane; wynik `IntegrityPerformanceTests` K6-36 z maszyny referencyjnej wpisany (komenda: `dotnet test tests/Horyzont.Core.Tests -c Release --filter "FullyQualifiedName~IntegrityPerformanceTests"` — nazwa z planu K6-36, **niezweryfikowane** w kodzie).

### Etap D — gdUnit, CI, odbiór

- [ ] **D1. Składanie gry.** `SurvivalCompositionTests`, `SurvivalHostLoopTests` (D13). Przed uruchomieniem sprawdzić wolny potok `\\.\pipe\gdunit4-message-pipe` i zadania CI na wspólnym runnerze (`ci.yml` l. 149–160).
  Akceptacja: `dotnet test game/Horyzont.Game.csproj --settings game/.runsettings --filter "FullyQualifiedName~Survival"`.
- [ ] **D2. CI.** `ci.yml`: testy K6-40 `Slow` w istniejącym zadaniu (gdy mieszczą się w D6) i publikacja TRX przy porażce; `nightly.yml`: osobne zadanie Soak K6-40 i pełne przebiegi z D6, filtr starego zadania wyklucza K6-40. Wynik tylko z prawdziwego przebiegu CI.
  Akceptacja: `dotnet test Horyzont.sln -c Release --filter "Category!=Soak&Category!=Manual" --logger trx` mieści się w 30 min na runnerze; nocne: `dotnet test tests/Horyzont.Integration.Tests -c Release --filter "Category=Soak&FullyQualifiedName~Survival" --logger trx`.
- [ ] **D3. Raport.** Macierz D15 kompletna; zero `Missing`.
- [ ] **D4. Bramka D — Opus (końcowa).** Brak osłabionych asercji, pominiętych testów (`Skip`), nowych kategorii `Manual` dla wymagań spec, zmian w `src/` bez zgody, zmian w złotych plikach.

### Etap E — stabilizacja alokacji (warunkowo, DO USTALENIA-6; może iść równolegle od A0)

- [ ] **E1. Reprodukcja.** Dla każdego z pięciu testów: 10 uruchomień w osobnych procesach, zapis bajtów i środowiska.
- [ ] **E2. Test czułości.** Kontrolowana alokacja w mierzonym fragmencie daje porażkę.
- [ ] **E3. Poprawka pomiaru** zgodnie z D16; rzeczywista alokacja w `src/` → Opus.
  Akceptacja: `dotnet test Horyzont.sln -c Release --filter "FullyQualifiedName~SubmitPlaceOnFaceOverlappingOtherConstruct_Rejected_DoesNotAllocate|FullyQualifiedName~FarUnknownHit_OutOfReach_DoesNotAllocateNorSample|FullyQualifiedName~PlanetActivityPolicy_Update_AllocatesZeroBytes|FullyQualifiedName~PropReplicatorTests.Idle_DoesNotAllocate|FullyQualifiedName~BuildSnapshot_WarmedUp10000Calls_AllocatesZeroBytes"` × 10 niezależnych uruchomień + jeden pełny przebieg rozwiązania; raport `K6-40-stabilnosc-alokacji.md`.

## Sprawdzenie na końcu etapu

```powershell
dotnet build Horyzont.sln
dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"
dotnet format Horyzont.sln --verify-no-changes
```

Po D dodatkowo (silnik tylko przy wolnym runnerze i potoku gdUnit):

```powershell
dotnet build game/Horyzont.Game.sln
dotnet format game/Horyzont.Game.sln --verify-no-changes
python tools/engine/build.py smoke
dotnet test game/Horyzont.Game.csproj --settings game/.runsettings
```

`git diff <A0>..HEAD -- tests/Horyzont.Core.Tests/Golden/TerrainJournals tests/Horyzont.Core.Tests/TerrainJournalGoldenCorpusTests.cs tests/Horyzont.Core.Tests/Hashing/DeterminismTests.cs src/Horyzont.Persistence/Migrations src/Horyzont.Core` — pusty (wyjątek: poprawka zatwierdzona przez Opus, opisana w PR).

## Reguły dla wykonawcy (Sonnet 5.5)

1. Nie zmieniaj `src/`, `game/src/`, `game/content/`, `game/locale/`. Czerwony test produkcji = raport + pytanie do Opus.
2. Nie zapisuj stanu świata ani `Vitals` z testu; każda zmiana przez intencję albo ścieżkę serwera.
3. Nie używaj `Task.Delay`, `Thread.Sleep`, `Stopwatch`, `DateTime.Now` w logice testów K6-40 (wyjątek: bezpiecznik limitu Soak).
4. Nie zmniejszaj liczb ze spec (10 000, 8, 250 ms, 5%) ani progów pokrycia bez zapisanej decyzji właściciela.
5. Każdy test „brak skutku” ma bliźniaka „jest skutek”; każdy przebieg losowy ma straże „nie pusto”.
6. Nie powielaj testów pakietów właścicieli; wskaż je w macierzy.
7. Nie uruchamiaj gry z oknem ani gdUnit, gdy potok jest zajęty.
8. Po dwóch nieudanych próbach tego samego testu: eskalacja z testem, logiem, ziarnem (ZASADY §12).
9. Nie aktualizuj złotych skrótów ani `ProtocolContractTests`.

## Koordynacja

| Pakiet | Uzgodnienie |
| --- | --- |
| K6-29/K6-29W | Wyrocznia rozszerza `ItemConservationProperties`, nie zastępuje; podnoszenie/wygaszanie bez właściciela (DO USTALENIA-5) |
| K6-30 | Skrzynie, dostęp, `QuickStack` 10 m, łóżka |
| K6-31 | Urobek, nadmiar pod nogami, odrastanie jako operacja serwera |
| K6-32 | `CraftingFlowTests` zostaje; K6-40 dokłada wieloosobowe konflikty |
| K6-33 | `VitalsStepTests`, `FoodFlowTests` zostają |
| K6-34 | Zagrożenia i generator; zawieszenie kombinezonu przy `Frozen` (przegląd C-D K6-34) sprawdzić w pętli |
| K6-35 | Śmierć, kary, nagrobki, ustawienia 46/23/47; `DeathFlowTests` = wzór wywołania śmierci |
| K6-36 | Koszty, zwroty, wytrzymałość, `IntegrityPerformanceTests`; D14.7 |
| K6-37/K6-38 | Teleporty, tankowanie, uprawy; brak atrap w rigu |
| K6-39 | Testy UI i lokalizacji; K6-40 nie przeprojektowuje ekranów |

## Ryzyka

| Ryzyko | Ograniczenie |
| --- | --- |
| Wyrocznia powiela błąd appliera | Sink + suma wszystkich kontenerów + model slotów + oczekiwania źródeł (D2, D3, D8) |
| Test przechodzi pusto | Straże pokrycia, ≥ 1% konfliktów, liczniki etapów pętli |
| Przekroczenie 30 min CI | Pomiar A3/B3, D6 |
| Boty dławione limitem 20/s | Tempo, `RateLimited` = 0 w bazowym wariancie |
| Model strat nieadekwatny | Kontrakt B1, zatwierdzenie Opus, uczciwy opis |
| Rig różny od gry | `SurvivalCompositionTests` (D13) |
| Determinizm sprawdzony tylko w jednym procesie | Wariant dwuprocesowy (D9.1) |
| Awaria porównana z nietrwałym stanem | Trwały prefiks (D11) |
| Niedokończone zależności | A0 stop, status `Blocked` |
| Soak za wolny | Pomiar kosztu ticka, decyzja o horyzoncie |
| Stabilizacja maskuje regresję | Test czułości (D16) |

## Odbiór

- [ ] 10 000 sekwencji intencji × 8 graczy: niezmiennik po każdej operacji (CI albo nocny wg D6).
- [ ] 8 botów, 10 000 intencji, RTT 250 ms, 5% strat: zero duplikatów, zero zgubień, model slotów == serwer, lustra == serwer.
- [ ] 8 graczy przechodzi pełną pętlę (kryterium l. 418).
- [ ] Restart i awaria zachowują każdy przedmiot; skrót po powtórce == skrót przed awarią (l. 420).
- [ ] Każde aktywne ustawienie działa i przetrwa restart (l. 422).
- [ ] 12 scenariuszy regresji wykonanych; rozbieżności zgłoszone właścicielom.
- [ ] Soak przechodzi w nocnym CI.
- [ ] gdUnit: zgodność składania i scenariusz hosta.
- [ ] Macierz bez `Missing`; wynik 64³/50 ms z maszyny referencyjnej wpisany.
- [ ] Złote pliki, migracje i `src/Horyzont.Core` bez zmian (poza zatwierdzonymi poprawkami).
- [ ] (E) pięć testów alokacji: 10/10 zielone, test czułości.

## DO USTALENIA dla właściciela (z propozycjami)

1. **Start przed scaleniem K6-37…K6-39.** Propozycja: etap A (wyrocznia, generator na rodzinach K6-29…K6-36) i E mogą ruszyć po scaleniu K6-36; B–D po K6-39.
2. **Model strat 5%.** Propozycja: D7 (zawodne gubione, niezawodne opóźnione retransmisją z blokadą czoła kolejki) jako odbiór spec l. 400; test na prawdziwym UDP — osobny `Manual`, poza K6-40.
3. **Budżet.** Propozycja: 50 akcji/sekwencję; pełne 10 000/10 000 w CI, jeśli ≤ 5/3 min, inaczej nocnie + wariant 1000/2000 w CI; Soak 8 h świata z limitem 90 min albo krótszy horyzont po pomiarze A0.
4. **Maszyna referencyjna** dla 64³/50 ms (wspólne z DO USTALENIA-9 K6-36).
5. **Przedmioty leżące:** podnoszenie w 2 m i wygaszanie po 60 min bez pakietu. Propozycja: mały pakiet „reszta K6-29” przed K6-40; do tego czasu K6-40 liczy je w sumie, a wymaganie oznacza `Blocked`.
6. **Etap E w K6-40?** Propozycja: tak, bo niestabilne testy blokują zielone CI wymagane do odbioru; alternatywnie osobne zadanie.
7. **Budowanie bez kosztów a późniejszy zwrot:** czy blok postawiony za darmo zwraca koszt po wyłączeniu trybu? Propozycja: nie (rewizja „bez kosztu”), decyzja dla K6-36/K6-39, K6-40 tylko testuje wybrane zachowanie.
8. ~~**Kolizja `ProtocolVersion = 22`**~~ — rozstrzygnięte 2026-10-17: 22/23/24; K6-40 nie zależy od wartości.

## Pułapki

- Nie liczyć tylko kontenerów graczy; sumować wszystkie kontenery świata.
- Nie księgować operacji złożonej przed `Append`; nie liczyć ponownie po restarcie.
- Bilans ≠ poprawna ilość ze źródła (D3).
- Brak odpowiedzi ≠ odmowa; `RateLimited` ≠ konflikt.
- Inny harmonogram = inny zwycięzca; porównywać niezmienniki, nie historię.
- Skrót nie obejmuje vitals, terenu, póz online.
- Nie kopiować `Stopwatch`/`Task.Delay(1)` z istniejących rigów.
- Nie rejestrować usług w rigu w innej kolejności niż `GameComposition`.
- Nie wywoływać śmierci zapisem zdrowia.
- Nie stabilizować testów ponowieniami ani progami.

## Odstępstwa i uwagi z realizacji

Plan przygotowany z odczytu repozytorium (`k6-35`) i planów zależności; implementacja nie rozpoczęta. Po każdym etapie wykonawca dopisuje commit, wyniki, werdykt Opus i rozstrzygnięcia DO USTALENIA.

## Przegląd planu (Opus 5.5)

Data: 2026-10-17. Podstawa: plan Codexa (`plan-k6-40-codex.md`), worktree `k6-35` (tylko odczyt, bez budowania i testów), poprawione plany K6-35/K6-36, szkice K6-37…K6-39. Werdykt: **GO z poprawkami** (14).

| # | Zmiana | Dlaczego |
| --- | --- | --- |
| P1 | D4/D6: właściwość bez sieci i SQLite na sekwencję; budżet 30 min CI (`ci.yml` l. 32); dopuszczony przebieg nocny (spec l. 423) | 10 000 × start `ServerRuntime` nie zmieści się w CI; Codex nie określił poziomu ani budżetu |
| P2 | D2: obserwator = dekorator `IJournalSink` (wzór `HashingJournalSink`), suma po wszystkich kontenerach zamiast listy gospodarzy | Istniejący punkt obserwacji każdej operacji; lista gospodarzy mogła pominąć rodzaj |
| P3 | D8: osobny typ na bota, model slotów, ≥ 1% konfliktów, zbieżność luster | Sam bilans typu nie wykrywa duplikatu zniesionego zgubieniem; test bez konfliktów byłby pusty |
| P4 | D8: tempo 20/s, burst 40, `RateLimited` liczone osobno | `GameRules` l. 44/47; boty bez tempa testowałyby głównie limiter |
| P5 | D7: precyzyjny model strat niezawodnych (opóźnienie retransmisji z blokadą czoła, bez gubienia) w `tests/`, bez zmian w `Horyzont.Net` | Kod: strata tylko dla `Unreliable` (l. 359–381); Codex nie określił semantyki adaptera |
| P6 | D10: zakaz `Task.Delay(1)`/`Stopwatch`, zamiast nich bariera writera | Wzorzec rigów (l. 99–110) — ~15 ms na klatkę, Soak 1,7 mln ticków niewykonalny |
| P7 | D13: test zgodności składania `GameComposition` ↔ rig; gdUnit zredukowany do składania + scenariusz hosta | Rigi rejestrują usługi ręcznie (różne podzbiory); testy UI/lokalizacji należą do K6-39 (duplikacja w planie Codexa) |
| P8 | D5: śmierć tylko ścieżką produkcyjną, straż `rg` | ZASADY l. 33, K6-34 D22 — jeden pisarz stanu postaci |
| P9 | Kontekst: plan K6-36 odnaleziony (poprawiony przez Opus); numeracja i test 64³ potwierdzone | Codex oznaczył K6-36 jako brakujący |
| P10 | D9: wariant dwuprocesowy determinizmu | W jednym procesie kolejność `Dictionary<string>` jest stała; test byłby zawsze zielony dla tej klasy błędów |
| P11 | D11: „awaria między składowymi” = granice `FaultPoint` + cofnięcie późnej składowej | `FaultPoint` ma 5 wartości, brak punktu wewnątrz operacji złożonej |
| P12 | D14: 12 scenariuszy regresji między pakietami (w tym pętla „budowanie bez kosztów → zwrot”) | Wiersz K6-40 w spec: „scenariusze regresji”; Codex ich nie wymienił |
| P13 | Etap E wydzielony, warunkowy, z testem czułości | Poza literą spec; nie może blokować A–D |
| P14 | Gałąź/plik po polsku, numeracja „nic nie zajmuje”, kolizja PV 22 w szkicach K6-37/38/39 zgłoszona | Spójność z K6-30…K6-36; K6-40 nie może przypinać wersji |

Sprawdzone bez uwag: numery w kodzie (operacje, bloby, komunikaty), nazwy pięciu testów alokacji, `LinkConditions`, `StateHasher`, `ArmedFaults`, ścieżki `game/.runsettings`, `Horyzont.Game.csproj/.sln`, `ModeFixture`.

**Niezweryfikowane:** stan `main` i scalonych K6-34…K6-39; nazwy klas/testów K6-35…K6-39 (z planów); czy `ServerRuntime` działa z bazą w pamięci i przyjmuje dekorowany `ITransport`; powód `Task.Delay(1)` w rigach; dokładna droga śmierci w testach K6-35; przydatność `CrashHost` do porównania dwuprocesowego; czy `ServerRuntime` ujawnia listę uczestników ticka; koszt ticka 8 graczy i czas 10 000 sekwencji; maszyna referencyjna; obecność ramy botów K4-19 na `main`; niestabilność pięciu testów alokacji (nie odtworzona).
