# K6-35 · Śmierć i odrodzenie

| | |
| --- | --- |
| Prowadzi | Sonnet 5.5 |
| Testy akceptacyjne | Sonnet 5.5, przed implementacją; kontrakty i testy A/B zatwierdza Opus 5.5 |
| Przegląd | Opus 5.5: A1, A7, B1, B6, C6 oraz końcowy PR; wygląd ocenia właściciel |
| Zależy od | K6-29, K6-30, K6-33; integracja z kontraktami K6-34 zgodnie z D0 |
| Gałąź | `pakiet/k6-35-smierc-i-odrodzenie`, utworzona przez wykonawcę w osobnym worktree |
| Protokół | K6-33 = 18, K6-34 = 19, K6-35 = **20** (`main` + 1 w chwili scalenia) |
| Kamień milowy | K6 Przetrwanie |
| Odbiór ze specyfikacji | Śmierć w trzech trybach kary; nagrobek przetrwa restart |

**Cel:** zerowe zdrowie uruchamia serwerową, atomową operację śmierci.
Przedmioty trafiają do prywatnego nagrobka zgodnie z karą świata, jedzenie znika, a postać odradza się przy łóżku albo w miejscu startu świata.
Nagrobek można odzyskać bez duplikowania przedmiotów; opróżniony znika.
Wykonawca nie uruchamia gry z oknem. Ten dokument opisuje przyszłą realizację; podczas jego przygotowania nie budowano ani nie testowano repozytorium.

## Kontekst

Źródła: `AGENTS.md`, `docs/ZASADY.md`, `docs/spec/03-przetrwanie.md`, `docs/plan/plan-projektu.md`.
Wzorce: plany K6-30, K6-31, K6-32, K6-33 i K6-34 w `docs/plans/`.
Obowiązują zwłaszcza ZASADY §2–4: autorytet serwera, lokalne pozy w ramach, deterministyczna powtórka i przegląd każdego rozszerzenia Core.
Kryteria pakietu wynikają z sekcji „Śmierć i odrodzenie” (spec l. 142–153), tabel operacji (l. 252–290), sieci (l. 325–347), zapisu (l. 351–368) i obsługi błędów (l. 383–384).
Stan sprawdzono w worktree `k6-34` (`ProtocolVersion` = 18, K6-34 częściowo zaimplementowane: `SuitResources` istnieje, op 42 jeszcze nie); częściowych zmian nie traktować jako ukończonych kontraktów.

| Fakt sprawdzony w kodzie (worktree `k6-34`) | Konsekwencja |
| --- | --- |
| `Progress/PlayerProgress.cs`: łóżko, odkrycia, trzy miejsca jedzenia; `IsEmpty` uwzględnia wszystkie trzy | Śmierć czyści jedzenie, zachowuje odkrycia i punkt odrodzenia |
| `Serialization/StateCodec.cs`: blob 22 czyta wersje 1–3 | Lista nagrobków będzie pochodna; nie wymaga rozszerzania bloba 22 |
| `Operations/RespawnPointSetOp.cs`: operacja 44 z `BedRef` | Nie tworzyć drugiego kontraktu ustawiania łóżka |
| `Progress/BedRef.cs`: budowla, komórka, oczekiwany typ | Sam zapis referencji nie gwarantuje bezpiecznego miejsca odrodzenia |
| `World/PlayerVitals.cs`: `(Health, Stamina, RestedUntil, Exhausted, SuitResources? Suit = null)`; `PlayerState.Vitals` jest `PlayerVitals?` | Brak stanu (`null`) jest poprawną wartością zapisu (blob 2: bajt flag `00`) |
| `Server/Survival/VitalsService.Player.cs` l. 44: `Vitals ?? new PlayerVitals(NaN, NaN, …)`; `VitalsStep` l. 34–35: NaN → maksimum | `Vitals = null` oznacza „świeża postać”: pełne zdrowie i wytrzymałość w następnym ticku |
| Plan K6-34 D4/D22: `Suit = null` = świeży kombinezon, `VitalsService` ustawia pojemności z aktualnego wyposażenia | Reset kombinezonu nie musi nieść pojemności w operacji (D5) |
| `Survival/VitalsStep.cs` l. 55: `MathF.Max(…, rules.MinHealthWithoutDeath)`; `VitalsRules` l. 40: domyślnie 1 | K6-35 zdejmuje minimum 1 i dodaje obsługę stanu zerowego |
| `VitalsStep` l. 69 tworzy `new PlayerVitals(…)` bez `Suit` | Integracja musi zachować zasoby K6-34 (o ile K6-34 C3 tego nie naprawi); wymagany test regresji |
| `VitalsService.cs` l. 52: `Phase => ServerTickPhase.Compose` (nie `Characters`) | Obsługa śmierci w fazie `Compose`, zarejestrowana zaraz po `VitalsService` (D18) |
| `Simulation/OperationApplier.cs` l. 125–128: `IsComponent` bez operacji 7; l. 239–290: ostatnia składowa nieodwracalna sprawdzana przed pozostałymi | Przepięcie gracza dochodzi jako **jedyna nieodwracalna składowa, ostatnia** (D6) |
| `ApplyReparent` (l. 1225–1240): rama, `ParentSeq`, poza, obrót, fotel, `MarkChanged`; `CheckReparent` (l. 1403+): `ParentSeq + 1`, rama istnieje, poza skończona, fotel | Warunki przepięcia zależą tylko od stanu gracza i ram, nie od innych składowych śmierci |
| `OperationCodec` l. 408: jedna wspólna `CurrentFormatVersion = 1` dla wszystkich operacji | Format 2 operacji 7 wymaga tabeli dozwolonych wersji per rodzaj (K6-36 użyje jej dla op 30) |
| Operacje 38/39/46 nie występują w `OperationKind`; spec rezerwuje je (l. 260–268); plan K6-29W l. 57–58 odkłada je do „reszty K6-29” i K6-35 | Pakiet uzupełnia 38/39 i (D4) 46 |
| `ContainerHostKind.PersistentEntity = 3`, `ContainerKind.Tombstone = 8`, `DroppedItem = 9`; `CoreCodecPrimitives` koduje gospodarza 3 (sam id) | Kodek op 35 bez zmian; zachować numery |
| `ApplyContainerCreate` (l. 654–707) przyjmuje gospodarza gracza i bloku, inne → „host kind not supported” | Potrzebna obsługa gospodarza będącego bytem trwałym (przyrost semantyki, stare dzienniki go nie mają) |
| `WorldLoader` l. 289: każdy `kind` inny niż `Construct` → `InvalidDataException` | Zapis i odczyt rodzajów 3/6 w `entities` do dodania |
| `Protocol/Messages/EntityKind.cs` l. 3: zarezerwowane `DroppedItem = 3`, `Tombstone = 6` | Uaktywnić bez zmiany innych wartości |
| `SurvivalRules.ResolveDrop` niszczy przedmiot przez `ItemDestroyOp`; `IsBlockContainer` (l. 419–421) dopuszcza tylko skrzynie i stacje | Nie używać tej ścieżki do śmierci; dostęp do nagrobka to nowa gałąź (D13) |
| `ProgressReplication` (`Server/Crafting/`) wysyła 48; `OperationProgress` rozpoznaje odkrycia i jedzenie | Rozszerzyć o łóżko i własne nagrobki (składowe 38/39) |
| `SpawnLocator` jest w `game/` i używa generatora | Core przyjmuje przygotowane dane startu; nie zależy od Godota |
| `SurfaceFrame.FromYaw` określa orientację na sferze | Użyć istniejącej konwencji, bez jej zmiany |
| `StateHasher`: sekcja `0x53`, podsekcje `0x42`, `0x50`, `0x48`, `0x44`, `0x4B`, `0x46` (ostatnia, K6-33) | Wolne `0x54` (byty) i `0x57` (ustawienia), dopisane po `0x46` |
| `ProtocolConstants.MaxContainerSlots = 256` (l. 121) | D11 |
| W kodzie nie znaleziono `RespawnPoints` ani `SeatEvictIntent` | Integracja K5-25 zależy od faktycznej bazy realizacji |

**Niesprawdzone (do A0):** miejsce wysyłki migawek względem faz ticka; istnienie rejestru komend konsoli serwera; to, czy `QuickStackIntent` wybiera wyłącznie `ContainerKind.Chest`; czy encje mogą mieć ramę statycznej budowli.

## Zakres

1. A — czyste reguły śmierci, wyboru odrodzenia, przeniesień i odwracalności operacji, bez Godota.
2. B — kodeki, trwałe byty, ustawienia świata (D4), komunikaty właściciela i treść prezentacyjna.
3. C — checkpoint, powtórka, awarie, harmonogram śmierci i przygotowanie miejsca na serwerze.
4. D — ekran śmierci i odradzania, nagrobki w świecie, odzyskiwanie oraz oznaczenia właściciela.
5. Wspólna funkcja Core wyboru punktu odrodzenia dla śmierci i zwolnienia fotela K5-25.
6. Prywatność kontenera, bezpieczne wyniesienie nagrobka oraz zachowanie wszystkich sztuk przedmiotów.

Poza zakresem: teleporty K6-37, docelowy interfejs K6-39, walka i nietykalność K7-42, animacje K7-48.
Poza zakresem: automatyczne podnoszenie w promieniu 2 m i wygaszanie przedmiotów leżących, przebudowa wyrzucania z K6-29W.
Poza zakresem: utrata umiejętności, doświadczenia, trwałości, postaci albo świata; spec nie przewiduje takich kar.
Komenda administratora przenosząca nagrobek należy do podprojektu 6; model ma umożliwiać jej późniejsze dodanie.

## Decyzje

- **D0. Baza i kolejność.** Zależności logiczne pozostają zgodne z tabelą projektu.
  Realizacja startuje z bazy zawierającej scalone K6-33 i K6-34 (`SuitResources`, `UndoLog.SetVitals`, blob 2 v3, ogon 44, PV 19).
  Etap A można przygotować wcześniej na jawnych danych wejściowych; B–D wymagają scalonego K6-34.
  Nie pobierać częściowych zmian z aktywnego worktree innego agenta. A0 zapisuje commit bazy i faktycznie dostępne kontrakty.

- **D1. Śmierć jest `CompoundOp`, bez nowego kodu `PlayerDeathOp`.**
  Spec przewiduje złożoną akcję śmierci (l. 286), a wszystkie numery operacji 30–49 są przydzielone.
  `DeathOperationBuilder` tworzy pojedynczą operację 30; nazwa „operacja śmierci” oznacza tę akcję domenową.
  Dodajemy zarezerwowane 38/39; nie pożyczamy numeru z Walki ani nie zajmujemy 46 operacją śmierci.

- **D2. Natychmiastowe odrodzenie, bez intencji „Odrodź”.**
  Spec mówi „od razu”; klient nie decyduje o chwili, pozycji ani zawartości nagrobka.
  Ekran śmierci informuje o zdarzeniu i oczekiwaniu na bezpieczne pojawienie ciała.
  Przycisk może zamknąć podsumowanie po odrodzeniu, ale nie uruchamia operacji serwera.

- **D3. Trzy kary, bez hardcore.**
  `DeathPenalty`: `Mild = 1`, `Normal = 2`, `Hard = 3`; domyślnie `Normal`.
  Łagodna zachowuje wszystkie przedmioty; normalna przenosi plecak i pasek; twarda także wyposażenie.
  Twarda nie oznacza permanentnej śmierci. Nie dodawać trybu hardcore bez zmiany specyfikacji.

- **D4. Pełny rekord ustawień świata, jeden format. DO USTALENIA-1. [zmienione w przeglądzie]**
  Kod nie ma trwałego ustawienia kary; samo pole `GameRules` nie spełniłoby wymagania „przetrwa restart” (spec l. 422).
  Propozycja: K6-35 przejmuje z „reszty K6-29” op 46, blob 23 i komunikat 47 z **pełną tabelą spec l. 172–183** w wersji 1,
  aby K6-36 (podparcie, budowanie bez kosztów), K6-37 (`TeleportAll`, darmowe paliwo) i K6-39 nie podbijały formatu kolejno.
  Pola v1: bajt flag (bit 0 `TeleportAll`, bit 1 darmowe paliwo, bit 2 budowanie bez kosztów; bity 3–4 głód i najazdy — zarezerwowane, muszą być 0; inne bity → błąd),
  mnożnik surowców, mnożnik stosów i natężenie zagrożeń jako `ushort` w setnych (50–300, 100–300, 50–200),
  `BuildingIntegrityMode` (`On = 1`, `Relaxed = 2`, `Off = 3`; domyślnie `On`), `DeathPenalty` (D3).
  Brak klucza `world_settings` w `meta` = wartości domyślne. K6-35 konsumuje tylko `DeathPenalty`; pozostałe pola są zapisane, replikowane i haszowane, ale bez konsumenta do czasu właściwych pakietów (opis w PR).
  Zmianę podczas gry wykonuje `ServerSimulation.ApplyServerOperation(op 46)` przez `WorldSettingsService.Apply`; jeśli A0 znajdzie rejestr komend konsoli, dodać komendę administratora, inaczej tylko API i testy.
  Klient nie dostaje nowej intencji (intencja 33 i ekran należą do K6-39). W A1/B1 Opus zatwierdza podział; nie implementować równolegle drugiego właściciela 46/23/47.

- **D5. Reset postaci jest częścią dziennikowanej akcji. [uproszczone w przeglądzie]**
  Rozszerzyć operację 7 formatem 2 o jeden bajt `ResetVitals` (0/1; inna wartość → `ProtocolViolationException`), dozwolony wyłącznie dla bytu gracza.
  Applier przy `ResetVitals = 1` ustawia `PlayerState.Vitals = null` („świeża postać”): następny tick `VitalsService` daje zdrowie i wytrzymałość równe maksimum
  (jedzenie jest już puste, więc bazowe 100/100), brak odpoczynku i wyczerpania oraz `Suit = null`, czyli pojemności z wyposażenia **pozostałego po karze** (K6-34 D4).
  Operacja nie niesie liczb z treści ani pojemności kombinezonu (ZASADY §4); blob 2 już koduje brak stanu postaci.
  Zwykłe przepięcia nadal zapisują format 1; odczyt 1 zachowuje dokładnie dotychczasową semantykę i bajty.
  Uzasadnienie dziennikowania: po awarii między dziennikiem a checkpointem gracz z checkpointu miałby 0 zdrowia i zginąłby drugi raz.
  Jest to jawne rozszerzenie chronionego kontraktu, wymagające przeglądu Opus.

- **D6. Przepięcie gracza jako jedyna nieodwracalna składowa, ostatnia. [zmienione w przeglądzie]**
  Zgodnie z ZASADY §2 (l. 29) i wzorcem `TryApplyCompound` (l. 263–290): `EntityReparentOp` dla **gracza** może być składową złożonej wyłącznie jako ostatnia,
  a złożona z przepięciem nie zawiera edycji terenu ani budowli. Jej warunki (`CheckReparent` + `ResetVitals` tylko dla gracza) sprawdza się **przed** stosowaniem pozostałych składowych;
  żadna wcześniejsza składowa nie zmienia `ParentSeq`, ramy ani fotela gracza, więc kolejność jest bezpieczna.
  Dzięki temu `UndoLog` nie dostaje cofania pozy, ramy, fotela i vitals; nie rozszerzamy przy tym obsługi przepięć budowli w złożonej.
  Operacja śmierci kończy się przepięciem z `SeatCell = null`, prędkością zero i `ParentSeq + 1`.
  Błąd dowolnej wcześniejszej składowej cofa liczniki identyfikatorów (byty, kontenery), znaczniki checkpointu i wersje kontenerów.
  Zasadę dopisać do ZASADY §2: „nieodwracalna ostatnia: teren, budowla albo przepięcie gracza (K6-35)”.

- **D7. Zero zdrowia nie może się samo wyleczyć.**
  Krok żywej postaci zachowuje dotychczasową kolejność regeneracji i obrażeń, lecz obcina zdrowie do zera (bez wartości ujemnych).
  Postać już mająca zero nie regeneruje się, nie wydaje wytrzymałości i oczekuje na operację śmierci.
  Harmonogram przetwarza ją raz; przy oczekiwaniu na teren nie tworzy kolejnych nagrobków.
  Po zmianie `ParentSeq` `VitalsService` zeruje zaległe obrażenia i koszty gracza (`PendingDamage`, `PendingCost`), aby obrażenia sprzed śmierci nie trafiły w nowe ciało.
  `Vitals = null` jest stanem żywym (nie zerem). Po wczytaniu checkpointu z zerowym zdrowiem serwer wznawia obsługę przed dopuszczeniem gracza do działań.

- **D8. Warunki powtórki nie zależą od chwilowego zdrowia.**
  Zdrowie, wytrzymałość i kombinezon są poza dziennikiem i skrótem, zgodnie z K6-33/K6-34.
  Resolver sprawdza śmierć na żywym serwerze; applier sprawdza strukturalne warunki operacji i oczekiwane sloty.
  Powtórka śmierci nie wymaga zdrowia zero w starszym checkpoincie.
  `ApplyReparent` już woła `MarkChanged`, więc reset trafia do checkpointu także dla gracza offline.

- **D9. Nagrobek bez strat i bez tworzenia przedmiotów.**
  Przeniesienia idą kolejno: plecak, pasek, wyposażenie, a w każdym kontenerze rosnące sloty.
  Każdy niepusty stos trafia do osobnego pustego slotu nagrobka; śmierć nie scala stosów.
  `ToStackLimit` = max(limit z treści, przenoszona liczba), więc ponadlimitowy stary stos nie jest odrzucany ani dzielony.
  Nie używać `ItemCreateOp(Recovered)` ani pary zniszcz–utwórz do przenoszenia istniejących sztuk.

- **D10. Pusty nagrobek. DO USTALENIA-2.**
  Spec jednocześnie tworzy nagrobek przy śmierci i usuwa opróżniony.
  Propozycja: gdy kara nie przenosi żadnego stosu, nie tworzyć pustego bytu ani kontenera.
  Dotyczy to łagodnej kary i śmierci bez przedmiotów; komunikat śmierci nadal dociera do właściciela.

- **D11. Rozmiar większy niż 256 slotów. DO USTALENIA-3.**
  `MaxContainerSlots = 256`; obecne 40 + 10 + 7 mieści się w jednym nagrobku.
  Model dopuszcza jednak większe kontenery gracza, więc nie wolno zakładać stałych 57 slotów.
  Propozycja: przy ponad 256 niepustych stosach tworzyć kolejne nagrobki, każdy z jednym kontenerem do 256 slotów.
  Wszystkie powstają w tej samej operacji śmierci; interfejs pokazuje całą grupę. Nie podbijać globalnego limitu kontenera.

- **D12. Nagrobki są prywatne i trwałe.**
  `EntityKind.Tombstone = 6`, `ContainerKind.Tombstone = 8`, gospodarz `PersistentEntity`, dostęp zawsze `Private`.
  Właściciel bytu i kontenera musi być ten sam, znany światu `PlayerId`.
  **Rama bytu trwałego to zawsze rama planety** (D16, D17); builder odrzuca inny rodzaj ramy. Dzięki temu rozbiórka lub zawalenie budowli (K6-36) nigdy nie usuwa ramy nagrobka.
  Nagrobek nie wygasa po 60 minutach; reguła dotyczy wyłącznie przedmiotów leżących.
  Nie ograniczać liczby nagrobków przez usuwanie najstarszych z przedmiotami.

- **D13. Odzyskiwanie.**
  Otwieranie do 6 m, utrzymanie subskrypcji do 8 m, sprawdzane serwerowo we wspólnej ramie.
  `TakeAllIntent` przenosi tyle, ile mieści plecak i pasek; pozostałe stosy zostają w nagrobku.
  Wyposażenie z twardej kary trafia do plecaka/paska, bez automatycznego zakładania.
  Ostatnie przeniesienie, `ContainerRemoveOp` i op 39 są jedną złożoną, także przy zwykłym `ItemMoveIntent`.
  Nagrobek jest wyłącznie źródłem: `ItemMoveIntent` **do** nagrobka, `ItemSwapOp` z udziałem slotu nagrobka (zamiana też wkłada przedmiot — lekcja K6-32 o wyjściu stacji)
  i szybkie odkładanie do nagrobka dają `InvalidParameters`.
  Reguły dostępu i usuwania pisać ogólnie dla kontenerów z gospodarzem `PersistentEntity`: `Tombstone` → tylko właściciel; inne rodzaje (`DroppedItem`) → odmowa, dopóki K6-36 nie doda swojej reguły (DO USTALENIA-9).

- **D14. Jedna funkcja wyboru odrodzenia w Core.**
  `RespawnPoints.Resolve` otrzymuje świat, gracza oraz przygotowany zestaw kandydatów i pozę startową.
  Wybiera poprawne własne łóżko, następnie bezpieczne miejsce startu na `WorldState.HomePlanet` (ZASADY l. 40; nigdy `Planets.All[0]`).
  Sprawdza oczekiwany typ bloku, stan `BedBlockState` i właściciela; bezpieczeństwo kandydatów dostaje jako dane.
  Nie pyta Godota, zegara ani generatora. Kolejność kandydatów jest stała i testowana.
  Adapter przygotowuje dane terenu; ta sama funkcja służy śmierci i zwolnieniu fotela, bez resetu vitals przy samym zwolnieniu.

- **D15. Łóżko na statku, przechylona budowla i bezpieczny odstęp. DO USTALENIA-4.**
  Propozycja: odrodzenie obok łóżka tylko w budowli statycznej albo zaparkowanym statku na planecie.
  Łóżko w locie, w ramie układu, zablokowane albo niedostępne daje start świata; referencji nie kasować automatycznie.
  Kandydaci przy łóżku: cztery strony komórki w ustalonej kolejności, liczone w osiach budowli, potem przeliczone do ramy planety przez `FrameMath` (podwójna precyzja).
  Miejsce na kapsułę i podparcie sprawdzać wzdłuż **radialnego „w górę” planety**, nie osi Y budowli (lekcja K6-33: odpoczynek w przechylonej budowli).
  Budowla przechylona tak, że żaden kandydat nie stoi (np. odwrócona), daje start świata.
  Dokładne odstępy wyprowadzić z `PlayerCapsule` i geometrii łóżka, zatwierdzić w A1; nie wpisywać przypadkowych wymiarów.

- **D16. Wynoszenie nagrobka na powierzchnię.**
  Najpierw przeliczyć miejsce śmierci do ramy planety przez istniejące składanie ram w podwójnej precyzji.
  Na otwartej powierzchni zachować miejsce, jeśli bryła nagrobka jest wolna i ma dostępne podparcie.
  Dla miejsca w terenie lub pod powierzchnią wyznaczyć zewnętrzną powierzchnię w kierunku radialnym i wolną pozę ponad nią.
  Próbnik uwzględnia edycje świata; `TrySampleGeneratedSurface` może wyznaczyć początek poszukiwań, nie końcowy wynik.
  Brak załadowanych próbek oznacza oczekiwanie, nigdy „pusto”; gotowa poza trafia do op 38.
  **Limit oczekiwania [dodane w przeglądzie]:** po `DeathPlacementTimeoutTicks` (propozycja 60 s, jak przejście teleportu, spec l. 385) nagrobek trafia obok wybranego punktu odrodzenia,
  którego bezpieczeństwo jest już sprawdzone; przedmioty nigdy nie przepadają, a gracz nie zostaje martwy bez końca.

- **D17. Śmierć poza planetą. DO USTALENIA-5.**
  Propozycja: śmierć w ramie budowli związanej z planetą daje nagrobek na tej planecie, niezależnie od późniejszego ruchu statku.
  Gdy nie da się ustalić planety, nagrobek trafia do bezpiecznego miejsca startu na planecie domowej.
  Jest to odstępstwo od dosłownego „w miejscu śmierci”, potrzebne do odzyskiwania przedmiotów bez EVA.
  Wybór zapisuje operacja; powtórka nie oblicza ponownie orbity ani pozycji statku.

- **D18. Tick i oczekiwanie na teren. [poprawione w przeglądzie]**
  `VitalsService` działa w fazie `Compose` (`VitalsService.cs` l. 52), więc `DeathService` to uczestnik fazy `Compose` zarejestrowany **zaraz po** `VitalsService`
  (kolejność rejestracji w fazie, ZASADY l. 36). Śmierć powstaje w tym samym ticku, w którym zdrowie spadło do zera (lekcja K6-33: skutek w ticku kontaktu).
  A0 sprawdza, czy migawki wysyłane są po uczestnikach `Compose`; jeśli nie, zapisać rozbieżność i zatrzymać C3.
  Przygotowanie terenu korzysta z dzierżaw K5-22, działa na głównym wątku i ma budżet pracy na tick.
  Postać z zerem zdrowia oraz postać oczekująca na kolizję po odrodzeniu są zamrożone.
  Serwer odrzuca ich intencje zmieniające świat i kontenery; opóźnione wejścia nie mogą uruchomić akcji po odrodzeniu.
  Stan oczekiwania jest odtwarzalny ze zdrowia i gotowości ciała; nie dodawać ukrytego trwałego timera śmierci (licznik D16 żyje w pamięci serwera i po restarcie liczy od nowa).

- **D19. Odmowa przejścia i subskrypcje.**
  Używamy zarezerwowanego w spec `InTransition = 48` także dla oczekiwania na odrodzenie.
  Nie dodajemy odmowy 59; K6-37 współdzieli ten sam warunek i tekst.
  Bramkę sprawdzają `Submit`, subskrypcje kontenerów i przyjmowanie wejścia, zanim wykonają skutki uboczne; to tani warunek wstępny, więc odmowa nie pobiera żetonu (K1-03).
  Przed zastosowaniem operacji śmierci serwer kończy wszystkie subskrypcje kontenerów zmarłego (spec l. 383), także otwartego nagrobka i stacji.

- **D20. Replikacja właściciela.**
  Komunikat 48 dostaje ogon: punkt odrodzenia, stronicowaną listę własnych nagrobków i stan odradzania.
  Lista wynika z bytów trwałych przez indeks `PersistentEntityStore.OwnedBy(player)` utrzymywany przyrostowo; nie zapisujemy jej drugi raz w `PlayerProgress` ani blobie 22.
  `OperationProgress` rozpoznaje składowe 38/39 (także wewnątrz złożonej) jako zmianę postępu właściciela.
  Zdarzenie śmierci zawiera `OpId` oraz docelowy `ParentSeq`; klient deduplikuje podsumowanie.
  Przy ponownym połączeniu wysyłać bieżący stan (kolejność spec l. 338: kontenery, 47, 48), bez odtwarzania historycznego ekranu śmierci.

- **D21. Duże listy i zawodny kanał vitals.**
  Lista nagrobków ma rewizję, numer strony i liczbę stron; klient wymienia ją dopiero po kompletnym zestawie.
  Strona zawiera najwyżej 64 wpisy i mieści się w istniejącym limicie ramki; brak wpisów to jedna pusta strona.
  Komunikat 44 dostaje (po ogonie K6-34) `Tick` i `ParentSeq`, aby stary pakiet sprzed śmierci nie nadpisał resetu.
  Kontrakt nie zakłada kolejności pomiędzy kanałem niezawodnym a zawodnym; ekran znika po potwierdzeniu właściwej epoki ciała.

- **D22. Zgodność i skrót.**
  Nowe byty dostają blob 24 v1; blob 22 pozostaje v3, a blob 2 pozostaje w wersji z K6-34 (v3).
  Skrót otrzymuje podsekcję bytów `0x54` i ustawień `0x57`, dopisane po `0x46`; obie są też powodem pojawienia się sekcji `0x53`.
  Pusta kolekcja bytów i ustawienia domyślne nie dopisują bajtów; stare złote skróty muszą pozostać identyczne.
  Pin skrótu A0 liczyć na **własnej zamrożonej fiksturze** pakietu (`DeathHashFixture`), nie na współdzielonym `WorldFactory` (lekcja K6-31: zmiana fikstury sąsiada przestawiła pin); ponowne przypięcie tylko z opisem w PR.

## Kontrakty chronione i wersje

| Ścieżka lub kontrakt | Zmiana | Wersja i zgodność |
| --- | --- | --- |
| `Operations/`, `OperationCodec` | 38, 39, 46 | Nowe operacje w formacie 1; istniejące numery bez zmian |
| `EntityReparentOp`, `OperationCodec` | Bajt `ResetVitals` | Format 2 tylko dla nowego wariantu; pełny odczyt formatu 1; tabela dozwolonych wersji per rodzaj zamiast jednej stałej (l. 408) |
| `OperationApplier`, `UndoLog` | Trwałe byty (odwracalne), gospodarz `PersistentEntity`, przepięcie gracza jako ostatnia nieodwracalna | Przegląd Opus; stare poprawne dzienniki bez zmiany wyniku (nie zawierają 7 w złożonej ani gospodarza 3) |
| `World/`, `Checkpoints/` | Rekordy bytów, ustawienia i śledzenie zmian | Nowe bloby 24/23 v1; istniejące bloby bez zmiany |
| `Intents/RejectReason` | `InTransition = 48` | Istniejące intencje pozostają w formacie 1 |
| `Protocol/`, `MessageCodec` | Ogony 44/48 i komunikat 47 | `ProtocolVersion = 20`; bajt wersji komunikatów `01` według wzorca K6-32/K6-34 |
| `Protocol/Messages/EntityKind` | Uaktywnienie zarezerwowanych 3 i 6 | Pozostałe wartości bez zmiany; prezentacja nagrobka zgodna ze spec l. 344 |
| `Hashing/StateHasher` | Tagi `0x54`, `0x57` | Opis w PR; stare korpusy i stałe nietknięte |
| `Persistence/Migrations/` | Brak zmian | `entities`, `meta` i tabele K6 już istnieją; bez nowej migracji |
| `SurfaceFrame`, generator, semantyka terenu | Brak zmian | Dotychczasowe konwencje i złote skróty zachowane |

Rezerwacje sprawdzone w kodzie (worktree `k6-34`) i planach K6-31…K6-34:
operacja 42, intencja 26, rodzaj stanu bloku 5 i blob 2 v3 należą do K6-34; operacje 47/48, intencja 27, odmowa 53, komunikat 45 i tag `0x48` do K6-31.
Operacje 43/45, intencja 24, odmowy 44–46, komunikat 48, blob 25, tagi `0x44`/`0x4B` należą do K6-32; K6-33 zajmuje op 41, intencję 25, odmowę 49, komunikat 44 i tag `0x46`.
K6-35 bierze: op 38/39/46, bloby 23/24, komunikat 47, odmowę 48 (nazwaną w spec), rodzaje bytów 3/6, tagi `0x54`/`0x57`; nie bierze nowej intencji ani odmowy 59.
K6-36 (następny w łańcuchu) bierze intencję 34, odmowy 51/52/55/58 i `CompoundOp` v2 — bez kolizji z powyższym. Intencje 30–33 i odmowy 47, 50, 56 pozostają dla K6-37…K6-39.
Zamrożone `ClientHello` i `HandshakeReject` pozostają bez zmian.

## Projekt

### Pliki

Nowe nazwy są projektem; istniejące ścieżki poniżej odpowiadają odczytanemu repozytorium.

| Obszar | Pliki |
| --- | --- |
| Core — model | `World/PersistentEntity.cs`, `PersistentEntityStore.cs`, `WorldSettings.cs`, `DeathPenalty.cs`, `BuildingIntegrityMode.cs`; `WorldState.cs` |
| Core — reguły | `Survival/DeathRules.cs`, `DeathInventoryPlan.cs`, `Simulation/RespawnPoints.cs`, `RespawnContext.cs` |
| Core — akcja | `Simulation/DeathOperationBuilder.cs`, `Operations/EntityReparentOp.cs` (pole `ResetVitals`) |
| Core — operacje | `PersistentEntityCreateOp.cs`, `PersistentEntityRemoveOp.cs`, `WorldSettingsSetOp.cs`, `OperationKind.cs` |
| Core — stosowanie | `Simulation/OperationApplier.cs`, `UndoLog.cs`, `ServerSimulation.cs`, `SurvivalRules.cs`, `FunctionalBlockRules.cs` |
| Core — dostęp | `Simulation/ContainerReach.cs`, `IntentValidator.cs`, `Intents/RejectReason.cs`, `Survival/VitalsStep.cs` |
| Core — zapis | `Checkpoints/PersistentEntityRecord.cs`, `ChangeTracker.cs`, `WorldCheckpoint.cs`, `WorldStateBuilder.cs` |
| Core — formaty | `Serialization/OperationCodec.cs`, `StateCodec.cs`, `BlobKind.cs`, `MessageCodec.cs`, `CoreCodecPrimitives.cs` |
| Core — sieć i skrót | `Protocol/Messages/PlayerProgressState.cs`, `PlayerVitalsMessage.cs`, `WorldSettingsState.cs`, `Hashing/StateHasher.cs` |
| Persistence | `WorldLoader.cs`, `WorldCreator.cs`, `Writing/PersistenceWriter.cs`; nowe testowe fikstury schematu 3 |
| Server | `Survival/DeathService.cs`, `DeathPreparation.cs`, `DeathLog.cs`, `WorldSettingsService.cs`, `VitalsService.*.cs` |
| Server — replikacja | `Crafting/ProgressReplication.cs`, `Inventory/InventoryService.cs`, `Replication/EntityReplication*` |
| Client | `Survival/ClientDeathState.cs`, `Inventory/TombstoneSession.cs`, `Crafting/ClientProgress.cs`, `Replication/EntityMirror.cs` |
| Gra — serwer | `Server/Characters/ServerCharacters.cs`, adapter przygotowania pozycji; `Boot/GameComposition.Death.cs` |
| Gra — klient | `Client/Survival/DeathScreen.cs`, `Client/Inventory/TombstonePanel.cs`, prezentacja i znaczniki nagrobków |
| Treść i testy | `game/locale/death.csv`, wpis tłumaczeń; `tests/*/Survival/`, `game/tests/Survival/` |

### Kontrakty Core

| Kontrakt | Pola i warunki |
| --- | --- |
| `PersistentEntityCreateOp` 38 v1 | `EntityId`, rodzaj 3/6, `FrameRef` (rodzaj `Planet`), `ParentSeq`, lokalna pozycja i obrót, `Owner`; id wolne globalnie, rama istnieje, liczby skończone; odwracalna składowa z cofnięciem alokatora |
| `PersistentEntityRemoveOp` 39 v1 | `EntityId`; byt istnieje, nie ma już kontenera; odwracalna składowa |
| `WorldSettingsSetOp` 46 v1 | Pełny rekord D4; tylko zakresy i znane bity, bez uprawnień w applierze; odwracalna składowa (choć zwykle samodzielna) |
| `EntityReparentOp` 7 v2 | Pola v1 + bajt `ResetVitals`; `1` tylko dla gracza; w złożonej wyłącznie jako ostatnia składowa (D6) |
| Blob 24 v1 | Pola rekordu bytu jak w 38; `kind` i id w wierszu SQLite muszą zgadzać się z blobem |
| Blob 23 v1 | Rekord D4; nieobecność klucza `world_settings` oznacza ustawienia domyślne |
| Komunikat 47 | Rekord D4; po kontenerach gracza, przed 48; ponownie po każdej op 46 |
| Ogon 48 | Opcjonalny `BedRef`; rewizja i strony własnych nagrobków; faza odradzania; opcjonalne zdarzenie z `OpId` i docelowym `ParentSeq` |
| Wpis nagrobka w 48 | Byt, kontener, rama, `ParentSeq`, pozycja lokalna; bez zawartości kontenera |
| Ogon 44 | Po ogonie K6-34: tick próbki i `ParentSeq`; klient odrzuca starsze epoki oraz starsze próbki |
| `RespawnPoints.Resolve` | Deterministyczny wybór z przygotowanych kandydatów; wynik: rama, poza, źródło `Bed`/`WorldStart` |
| `DeathInventoryPlan` | Wskazania źródłowych slotów i docelowych nagrobków; kolejność stabilna, bez modyfikacji świata |

Kodeki: identyfikatory i ticki według istniejących prymitywów, wektory i obroty bitowo, ramy przez `CoreCodecPrimitives`.
Niepoprawne enumy, NaN, nieskończoności, duplikaty wpisów i przekroczone limity odrzucać przed kosztowną alokacją.
Blob 24 obsługuje także rodzaj 3, ale pakiet nie uruchamia mechaniki przedmiotów leżących.
Builder sprawdza globalną unikalność id, ramę planety oraz relację byt–kontener (dokładnie jeden kontener, ten sam właściciel) po odtworzeniu wszystkich rekordów.

### Przepływ

`VitalsStep → zdrowie 0 → (Compose, po VitalsService) DeathService → koniec subskrypcji → przygotowanie terenu → DeathOperationBuilder → ApplyServerOperation`.
Złożona: `[38 + 35 + transfery] dla każdego nagrobka → FoodSetOp(puste) → EntityReparentOp(v2, ResetVitals = 1)` — przepięcie ostatnie, sprawdzone przed pozostałymi.
Łagodna kara pomija grupy nagrobków; każda kara usuwa jedzenie.
Po powodzeniu: dziennik, delty kontenerów, aktualizacja 48, spawn nagrobka i przepięcie ciała.
Po przygotowaniu kolizji: odblokowanie ruchu; ponowna próbka vitals ma już nowy `ParentSeq`.
Odzyskanie: `ItemTransferOp… → ContainerRemoveOp → PersistentEntityRemoveOp`, wszystko w jednym wpisie.

## Testy akceptacyjne

Wszystkie nowe testy powstają przed kodem. Fixture tworzy wyposażenie i niepuste trzy miejsca jedzenia.
Każdy scenariusz śmierci sprawdza sumy sztuk, pozycję, jedzenie, vitals, łóżko i odkrycia.
Wspólne pomoce: `DeathWorldFactory`, `DeathHashFixture` (zamrożona), kandydaci odrodzenia, kontrolowane próbki terenu, `FakeTimeProvider`, `InMemoryTransport`.
Żaden test nie może przejść pusto: testy iterujące po listach najpierw sprawdzają ich niepustość, a właściwości mają progi pokrycia.

### Etap A — Core, bez Godota

| Klasa / test | Kryterium |
| --- | --- |
| `DeathRulesTests.Death_Mild_KeepsAllItems` | Żaden stos nie zmienia kontenera; brak pustego nagrobka |
| `DeathRulesTests.Death_Normal_MovesBagAndHotbar` | Wyposażenie pozostaje, pozostałe stosy są w nagrobku |
| `DeathRulesTests.Death_Hard_MovesAllEquipment` | Wszystkie trzy kontenery opróżnione bez utraty sztuk |
| `DeathRulesTests.Death_AllPenalties_ClearsFoodAndResetsVitals` | Jedzenie puste, `Vitals = null`; po jednym kroku `VitalsService`/`VitalsStep` 100/100 bez odpoczynku |
| `DeathRulesTests.Death_PreservesDiscoveriesAndBed` | Pozostały postęp bez zmiany |
| `DeathRulesTests.Death_Over256Stacks_SplitsWithoutLoss` | Wszystkie stosy mieszczą się w kilku nagrobkach |
| `DeathRulesTests.Death_StackAboveCurrentLimit_PreservesCount` | Brak utraty starego, ponadlimitowego stosu |
| `VitalsStepTests.Advance_LethalDamage_ReachesZero` | Zdjęte minimum 1, brak wartości ujemnych |
| `VitalsStepTests.Advance_AlreadyZero_DoesNotRevive` | Oczekiwanie nie regeneruje postaci |
| `VitalsStepTests.Advance_WithSuit_PreservesResources` | Zwykły krok nie gubi danych K6-34 |
| `RespawnPointsTests.Resolve_OwnBed_SelectsSafeCandidate` | Poprawny typ, właściciel i stała kolejność kandydatów |
| `RespawnPointsTests.Resolve_MissingBlockedOrFlyingBed_UsesHomeStart` | Bezpieczny fallback; domowa planeta z `WorldState.HomePlanet`, nie z sortowania id |
| `RespawnPointsTests.Resolve_TiltedConstruct_UsesPlanetUp` | Budowla obrócona o 30° i 90°: kandydat po stronie zgodnej z radialnym „w górę”; odwrócona → start świata |
| `DeathCompoundTests.Apply_FailingComponent_RollsBackEverything` | Sloty, byty, oba alokatory, wersje i tracker bez zmian; poza i vitals nietknięte, bo przepięcie nie zostało zastosowane |
| `DeathCompoundTests.Reparent_NotLast_RejectedStructurally` | Op 7 w złożonej nie jako ostatnia, przepięcie budowli i `ResetVitals` dla budowli → błąd struktury |
| `DeathCompoundTests.Reparent_FailingPrecondition_ChecksBeforeOtherComponents` | Zły `ParentSeq`: żadna składowa nie została zastosowana |
| `DeathCompoundTests.Replay_CheckpointHadPositiveHealth_AppliesReset` | Powtórka nie zależy od chwilowego zdrowia |
| `TombstoneRecoveryTests.TakeAll_Partial_KeepsRemainder` | Pełny plecak nie niszczy reszty |
| `TombstoneRecoveryTests.LastMove_RemovesContainerAndEntityAtomically` | Brak pustego nagrobka po ostatniej sztuce |
| `TombstoneRecoveryTests.MoveOrSwapIntoTombstone_Refused` | Przeniesienie do nagrobka, zamiana ze slotem nagrobka i szybkie odkładanie → `InvalidParameters`, stan bez zmian |
| `DeathConservationProperties.RandomDeathsAndRecovery_ConserveEveryItem` | FsCheck: trzy kary, wielu graczy, przeplatane odzyskiwanie; progi: ≥ 50 śmierci na karę, ≥ 20 częściowych `TakeAll`, ≥ 20 ostatnich odzyskań, ≥ 10 twardych z wyposażeniem |
| `DeathHashTests.NoNewState_PreservesPreviousHash` | Pin na `DeathHashFixture` z commitu A0; nowe byty i ustawienia inne niż domyślne zmieniają skrót |

### Etap B — protokół i treść

| Klasa / test | Kryterium |
| --- | --- |
| `DeathCodecTests.NewOperationsAndBlobs_RoundTrip` | 38/39/46, 23/24, 47; jawne złote bajty |
| `DeathCodecTests.ReparentV1_GoldenBytesUnchanged` | Stare operacje 7 identyczne binarnie |
| `DeathCodecTests.ReparentV2_Reset_RoundTrips` | Bajt `ResetVitals`; wartość inna niż 0/1 → `ProtocolViolationException` |
| `DeathCodecTests.WorldSettings_RangesAndReservedBits` | Mnożniki poza zakresem, zarezerwowane bity głodu/najazdów i nieznane enumy odrzucone |
| `DeathCodecTests.MalformedFrames_RejectBeforeAllocation` | Ucięcia, enumy, ramy, liczby i limity stron |
| `DeathProtocolTests.ProtocolVersion_Is20` | Aktualizacja kontraktowych testów wersji |
| `DeathProtocolTests.ProgressPages_Reordered_DoesNotPublishPartialList` | Atomowa wymiana listy; stara rewizja ignorowana |
| `DeathProtocolTests.OldVitalsEpoch_DoesNotOverwriteRespawn` | Odporność na kolejność między kanałami |
| `DeathAccessTests.OtherPlayer_CannotSubscribeOrTakeItems` | Brak zawartości i brak zmian świata |
| `DeathAccessTests.PendingRespawn_RejectsMutationsWithInTransition` | Intencje, subskrypcje i wejście przechodzą wspólną bramkę; żeton niepobrany |
| `DeathContentTests.Translations_HavePolishAndEnglishKeys` | Śmierć, odradzanie, trzy kary, odzyskiwanie i odmowy |

### Etap C — zapis i serwer

| Klasa / test | Kryterium |
| --- | --- |
| `DeathPersistenceTests.CheckpointAndTail_RestoreTombstones` | Identyczne byty, zawartość, łóżko i ustawienia po restarcie |
| `DeathPersistenceTests.ExistingSchema_LoadsWithoutMigration` | Schemat 3 bez zmian; stare bloby i brak ustawień poprawne |
| `DeathPersistenceTests.OrphanOrMismatchedEntity_RejectsWorld` | Błędny właściciel, gospodarz, id, rodzaj albo rama inna niż planeta nie jest pomijana |
| `DeathCrashTests.CrashAtJournalAndCheckpointBoundaries_NoDuplication` | Cały ostatni trwały skutek albo jego brak; spójne alokatory; brak drugiej śmierci po powtórce |
| `DeathServiceTests.ZeroAcrossManyTicks_CreatesOneDeath` | Brak wielokrotnej kary i nagrobków podczas oczekiwania |
| `DeathServiceTests.LethalTick_DeathInSameTick` | `DeathService` po `VitalsService` w `Compose`; operacja w ticku, w którym zdrowie spadło do 0 |
| `DeathServiceTests.PendingDamageBeforeDeath_NotAppliedAfterRespawn` | Zaległe obrażenia i koszty wyzerowane po zmianie `ParentSeq` |
| `DeathServiceTests.OpenSubscriptions_EndBeforeDeath` | Subskrypcje skrzyni, stacji i nagrobka zakończone przed operacją |
| `DeathServiceTests.DisconnectOrRestartWhilePreparing_ResumesSafely` | Zero zdrowia nie znika z obsługi po zmianie sesji |
| `DeathPlacementTests.BuriedOrEditedTerrain_UsesActualOuterSurface` | Aktualny teren, nie sama wysokość generatora |
| `DeathPlacementTests.UnknownTerrain_WaitsWithoutLosingItems` | Brak próbek nie oznacza wolnego miejsca |
| `DeathPlacementTests.TerrainNeverReady_FallsBackToRespawnPoint` | Po limicie D16 nagrobek obok punktu odrodzenia; wszystkie sztuki zachowane |
| `DeathServiceTests.SafeMode_DoesNotApplyDeathOrConsumeIds` | Brak mutacji i ukrytej zmiany stanu trwałego |
| `DeathFlowTests.TwoClients_DeathRecoverRestart_ConservesItems` | Pełny przepływ przez sesję, writer i replikację |
| `RespawnIntegrationTests.SeatEviction_UsesSameResolverWithoutPenalty` | Wspólny wybór pozycji; brak resetu ekwipunku i jedzenia |
| `DeathAllocationTests.IdleAliveTick_AllocatesZero` | Gorąca ścieżka sprawdzania żywych graczy bez alokacji |

### Etap D — klient

| Klasa / test | Kryterium |
| --- | --- |
| `ClientDeathStateTests.DeathNotice_Duplicate_ShowsOnce` | Deduplikacja po `OpId`; reconnect bez starego podsumowania |
| `ClientDeathStateTests.RespawnWait_BlocksInputUntilNewBodyReady` | Brak samodzielnego odradzania i lokalnych zmian przedmiotów |
| `TombstoneSessionTests.OwnerOpenAndTakeAll_UsesExistingMessages` | Subskrypcja i intencja 23; brak nowego protokołu odzyskiwania |
| `TombstoneSessionTests.LastDeltaAndRemoval_ClosePanel` | Usunięty kontener nie zostaje otwarty |
| `TombstoneMarkersTests.OwnOnly_AllPlanets_KeepFrameCoordinates` | Własne oznaczenia, lokalne pozy i informacja o innej planecie |
| `DeathScreenTests.SampleStates_ShowLabelsAndFocus` | gdUnit bez okna: ładowanie, gotowe odrodzenie, klawiatura i PL/EN |
| `TombstonePresentationTests.SpawnAndDespawn_UpdatesWorldAndMap` | Rodzaj 6, `Archetype = 0`, puste `KindData`, poprawny właściciel |

## Kroki

Polecenia poniżej wykonuje realizator pakietu w swoim worktree, nie autor tego planu.
Każdy krok implementacyjny: czerwony test z tabeli → minimalny kod → zielony test → Conventional Commit.
W kroku testowym błąd kompilacji brakującego kontraktu jest dopuszczalnym stanem czerwonym; log trafia do PR.
Przeglądy Opus są wymaganiami ZASADY §3 i §12; poprawki przeglądu zapisać przed dalszą implementacją.

### Etap A — Core

- [ ] **A0. Baza i rezerwacje.** Pliki: plan, enumy, kodeki, `StateHasher`; test `DeathHashTests.NoNewState_PreservesPreviousHash`.
  Komenda: `rg -n 'ProtocolVersion|PersistentEntity|InTransition|WorldSettings|Tag =|ServerTickPhase' src docs/plans/K6-34-zagrozenia-i-kombinezon.md`.
  Zanotować commit bazy, pin skrótu `DeathHashFixture` przed zmianami Core, obecność K6-34 (`UndoLog.SetVitals`, blob 2 v3) oraz fakty „Niesprawdzone” z Kontekstu.
- [ ] **A1. Czerwone testy i przegląd Opus przed kodem.** Pliki: `tests/Horyzont.Core.Tests/Survival/Death*`, `RespawnPointsTests`, szkielety nowych kontraktów.
  Test: `DeathRulesTests.Death_Normal_MovesBagAndHotbar`; komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~Death|FullyQualifiedName~RespawnPoints"`.
  Opus zatwierdza D1–D17, szczególnie format 7 v2, D4 (pełny rekord), D6 (przepięcie ostatnie), kilka nagrobków i zasady łóżka.
- [ ] **A2. Reguły i model.** Pliki: `PersistentEntity*`, `DeathRules`, `DeathInventoryPlan`, `RespawnPoints`, `RespawnContext`, `WorldState`, `WorldSettings`.
  Test: `RespawnPointsTests.Resolve_OwnBed_SelectsSafeCandidate`; komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~RespawnPoints|FullyQualifiedName~DeathRules"`.
- [ ] **A3. Atomowa śmierć.** Pliki: operacje 38/39/46, `EntityReparentOp`, `DeathOperationBuilder`, `OperationApplier` (przepięcie ostatnie, gospodarz `PersistentEntity`), `UndoLog` (byty).
  Test: `DeathCompoundTests.Apply_FailingComponent_RollsBackEverything`; komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~DeathCompound|FullyQualifiedName~DeathRules"`.
- [ ] **A4. Zero zdrowia i odzyskiwanie.** Pliki: `VitalsStep`, reguły intencji, `ContainerReach`, `FunctionalBlockRules`, `SurvivalRules`.
  Test: `VitalsStepTests.Advance_AlreadyZero_DoesNotRevive`, `TombstoneRecoveryTests.MoveOrSwapIntoTombstone_Refused`; komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~VitalsStep|FullyQualifiedName~TombstoneRecovery"`.
- [ ] **A5. Checkpoint i skrót w Core.** Pliki: rekordy, tracker, builder, `WorldCheckpoint`, `StateHasher`; bez SQLite.
  Test: `DeathHashTests.NoNewState_PreservesPreviousHash`; komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~DeathHash|FullyQualifiedName~DeathConservation"`.
- [ ] **A6. Właściwości i regresje.** Pliki: `DeathConservationProperties`, testy cofania, istniejące testy vitals wymagające minimum 1.
  Test: `DeathConservationProperties.RandomDeathsAndRecovery_ConserveEveryItem`; komenda: `dotnet test tests/Horyzont.Core.Tests`.
  Zmianę oczekiwania 1→0 opisać; nie zmieniać zamrożonych testów determinizmu.
- [ ] **A7. Przegląd Opus i bramka A.** Pliki: cały diff Core i testów; test `DeathCompoundTests.Replay_CheckpointHadPositiveHealth_AppliesReset`.
  Komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~DeathCompound|FullyQualifiedName~Determinism|FullyQualifiedName~TerrainJournalGoldenCorpus"`.
  Opus ocenia rollback, kolejność sprawdzania przepięcia, brak odczytu treści w applierze i zachowanie starych operacji; następnie sprawdzenie końca etapu.

### Etap B — protokół i treść

- [ ] **B1. Czerwone kontrakty binarne; Opus przed implementacją.** Pliki: `DeathCodecTests`, `DeathProtocolTests`, fixture generatora ramek.
  Test: `DeathCodecTests.ReparentV1_GoldenBytesUnchanged`; komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~DeathCodec|FullyQualifiedName~DeathProtocol"`.
  Zatwierdzić kolejność pól 7 v2, 23/24, 44/47/48, stronicowanie i przejęcie 46/23/47.
- [ ] **B2. Operacje i bloby.** Pliki: `OperationCodec` (tabela wersji per rodzaj), `StateCodec`, `BlobKind`, prymitywy; odczyt starych formatów bez zmian.
  Test: `DeathCodecTests.NewOperationsAndBlobs_RoundTrip`; komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~DeathCodec"`.
- [ ] **B3. Komunikaty i wersja.** Pliki: `MessageCodec`, rekordy 44/47/48, `MessageChannels`, `ProtocolConstants`, kontraktowe testy wersji.
  Test: `DeathProtocolTests.ProtocolVersion_Is20`; komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~DeathProtocol|FullyQualifiedName~ProtocolContract|FullyQualifiedName~Constants"`.
- [ ] **B4. Dostęp i routing.** Pliki: `RejectReason`, `IntentValidator`, `ServerSimulation`, `InventoryService`, `MessageRouter`, ich testy.
  Test: `DeathAccessTests.OtherPlayer_CannotSubscribeOrTakeItems`; komenda: `dotnet test Horyzont.sln --filter "FullyQualifiedName~DeathAccess|FullyQualifiedName~DeathRouter"`.
- [ ] **B5. Treść minimalna.** Pliki: `game/locale/death.csv`, rejestr tłumaczeń i test kluczy; nagrobek jako prezentacja bytu, nie przedmiot.
  Test: `DeathContentTests.Translations_HavePolishAndEnglishKeys`; komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~DeathContent"`.
- [ ] **B6. Przegląd Opus i bramka B.** Pliki: diff protokołu, wersji i testów; test `DeathCodecTests.MalformedFrames_RejectBeforeAllocation`.
  Komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~DeathCodec|FullyQualifiedName~DeathProtocol"`.
  Sprawdzić brak kolizji numerów, nadużyć rozmiaru list i przypadkowego podbicia istniejących formatów; wykonać sprawdzenie końca etapu.

### Etap C — zapis i serwer

- [ ] **C1. Czerwone testy trwałości i awarii.** Pliki: `DeathPersistenceTests`, `DeathCrashTests`, `DeathFlowTests`.
  Test: `DeathPersistenceTests.CheckpointAndTail_RestoreTombstones`; komenda: `dotnet test tests/Horyzont.Persistence.Tests --filter "FullyQualifiedName~Death"`.
- [ ] **C2. SQLite i powtórka.** Pliki: `WorldLoader`, `WorldCreator`, `PersistenceWriter`, fixture schematu 3; bez edycji migracji.
  Test: `DeathCrashTests.CrashAtJournalAndCheckpointBoundaries_NoDuplication`; komenda: `dotnet test tests/Horyzont.Persistence.Tests --filter "FullyQualifiedName~Death"`.
- [ ] **C3. Harmonogram śmierci.** Pliki: `DeathService` (faza `Compose`, po `VitalsService`), `VitalsService.*` (zerowanie zaległych obrażeń), bramka mutacji, koniec subskrypcji, `WorldSettingsService`, logi i rejestracja uczestników ticku.
  Test: `DeathServiceTests.ZeroAcrossManyTicks_CreatesOneDeath`, `DeathServiceTests.LethalTick_DeathInSameTick`; komenda: `dotnet test tests/Horyzont.Server.Tests --filter "FullyQualifiedName~DeathService|FullyQualifiedName~DeathAllocation"`.
- [ ] **C4. Przygotowanie pozycji i ciało.** Pliki: `DeathPreparation`, adapter terenu, `ServerCharacters`, `GameComposition.Death`, integracja resolvera K5-25, jeśli obecna.
  Test: `DeathPlacementTests.BuriedOrEditedTerrain_UsesActualOuterSurface`, `DeathPlacementTests.TerrainNeverReady_FallsBackToRespawnPoint`; komenda: `dotnet test tests/Horyzont.Server.Tests --filter "FullyQualifiedName~DeathPlacement|FullyQualifiedName~RespawnIntegration"`.
  Sceny adaptera sprawdzić bez okna przez `dotnet test game`; brak K5-25 opisać jako warunek integracji, nie pozorować gotowego podłączenia.
- [ ] **C5. Replikacja i pełny przepływ.** Pliki: `ProgressReplication`, `InventoryService`, źródło bytów replikacji, testy integracyjne.
  Test: `DeathFlowTests.TwoClients_DeathRecoverRestart_ConservesItems`; komenda: `dotnet test tests/Horyzont.Integration.Tests --filter "FullyQualifiedName~DeathFlow"`.
- [ ] **C6. Przegląd Opus i bramka C.** Pliki: zapis, harmonogram, adapter, testy awarii i opis PR.
  Test: `DeathServiceTests.DisconnectOrRestartWhilePreparing_ResumesSafely`; komenda: `dotnet test Horyzont.sln --filter "FullyQualifiedName~Death|FullyQualifiedName~RespawnIntegration"`.
  Opus sprawdza punkty awarii, śmierć offline, tryb bezpieczny i kolejność shutdown; wykonać sprawdzenie końca etapu.

### Etap D — klient

- [ ] **D1. Czerwone testy klienta.** Pliki: `ClientDeathStateTests`, `TombstoneSessionTests`, `TombstoneMarkersTests`.
  Test: `ClientDeathStateTests.DeathNotice_Duplicate_ShowsOnce`; komenda: `dotnet test tests/Horyzont.Client.Tests --filter "FullyQualifiedName~Death|FullyQualifiedName~Tombstone"`.
- [ ] **D2. Stan i interakcje.** Pliki: `ClientDeathState`, `ClientProgress`, `TombstoneSession`, `EntityMirror`, filtrowanie starych vitals.
  Test: `ClientDeathStateTests.RespawnWait_BlocksInputUntilNewBodyReady`; komenda: `dotnet test tests/Horyzont.Client.Tests --filter "FullyQualifiedName~Death|FullyQualifiedName~Tombstone"`.
- [ ] **D3. Ekran i odzyskiwanie.** Pliki: `DeathScreen`, `TombstonePanel`, prezentacja nagrobka, znaczniki HUD/mapy, composition root.
  Test: `DeathScreenTests.SampleStates_ShowLabelsAndFocus`; komenda: `dotnet test game`.
  Najpierw dane przykładowe, potem wpięcie; stan „Odradzanie…” nie zawiera przycisku wysyłającego intencję.
- [ ] **D4. Odbiór wyglądu i PR.** Pliki: plan, `docs/ZASADY.md` (reguła D6), opis PR; test `TombstonePresentationTests.SpawnAndDespawn_UpdatesWorldAndMap`.
  Komenda wykonawcy: `dotnet test game`; zrzuty i ocenę wyglądu wykonuje właściciel.
  Opus przegląda końcowy PR; wykonawca zapisuje oddzielnie wynik automatyczny i oczekującą ocenę wyglądu.

## Sprawdzenie na końcu etapu

Realizator wykonuje po każdym etapie i wkleja wynik do PR:

```powershell
dotnet build Horyzont.sln
dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"
dotnet format Horyzont.sln --verify-no-changes
```

Po C i D dodatkowo: `python tools/engine/build.py smoke` oraz `dotnet test game`, wyłącznie bez okna.
Przed testami silnika sprawdzić zajętość wspólnego runnera według zasad projektu.
Porównać zamrożony korpus terenu i `DeterminismTests.cs` z commitem A0; diff tych plików ma być pusty.
Nie aktualizować złotych skrótów generatora, obiektów ani starego świata, żeby ukryć regresję.
Po dwóch nieudanych podejściach do tego samego testu eskalować z testem, logiem i opisem prób.

## Koordynacja z sąsiednimi pakietami

| Pakiet | Uzgodnienie |
| --- | --- |
| K6-29/K6-30 | Uzupełnienie 38/39, 46/23/47 i gospodarza trwałego; zachowanie numerów kontenerów, łóżka i blobów 20/22 |
| K6-31/K6-32 | Zachowanie rozpoznawania składowych przez wspólne pomocniki; 48 nadal przenosi odkrycia i klucze |
| K6-33 | Usunięcie minimum 1; reset vitals przez operację (`Vitals = null`); zwykły stan nadal poza skrótem |
| K6-34 | Blob 2 v3, `Suit = null` po resecie, ogon 44 K6-35 po ogonie K6-34; reset próbek środowiska po zmianie `ParentSeq`; bez nadpisywania pól kombinezonu |
| K5-25 | Wspólne `RespawnPoints`; zwolnienie fotela nie jest śmiercią i nie zmienia przedmiotów ani jedzenia |
| K6-36 | Czyta `BuildingIntegrityMode` i budowanie bez kosztów z `WorldSettings` (D4); używa 38/39, bloba 24 i ogólnej ścieżki kontenerów `PersistentEntity` dla przedmiotów leżących; usunięcie łóżka nadal czyści punkt operacją 44; rama nagrobka to planeta |
| K6-37 | Wspólna bramka przejścia i odmowa 48; przepięcie teleportu bez `ResetVitals`; współdzielone przygotowanie terenu; czyta `TeleportAll` i darmowe paliwo z D4 |
| K6-39 | Intencja 33 i ekran ustawień nad rekordem D4 (bez zmiany formatu); ponowne użycie ekranu i panelu nagrobka |
| K7-42/K6-40 | Obrażenia uruchamiają tę samą ścieżkę śmierci; boty sprawdzają zachowanie sztuk i opóźnione intencje |

Tabela projektu nie wymienia K6-35 jako zależności K6-37; zależność współdzielonej bramki jest koordynacją implementacji, nie cichą zmianą tabeli.
Jeśli K6-37 powstaje wcześniej, uzgodnić wspólny kontrakt przed jego scaleniem; nie tworzyć dwóch niezależnych stanów przejścia.

## Ryzyka

| Ryzyko | Ograniczenie |
| --- | --- |
| Duplikacja przy śmierci i równoczesnym transferze | Jeden wątek, jedna operacja, oczekiwane sloty, bramka przejścia, koniec subskrypcji przed śmiercią |
| Powtórna kara po awarii | Atomowy reset w ogonie dziennika, odtwarzanie po `LastOpId`, testy granic trwałości |
| Nagrobek pod edytowanym terenem | Próbki aktualnego świata, kontrola załadowania, gotowa poza zapisana w operacji |
| Teren nigdy niegotowy | Limit D16 i nagrobek przy punkcie odrodzenia |
| Utrata modułów zmienia pojemność kombinezonu | `Suit = null` → pojemności z wyposażenia po karze (K6-34) |
| Stare pakiety odtwarzają stan sprzed śmierci | `ParentSeq`, tick, deduplikacja i czyszczenie predykcji |
| Rosnąca liczba prywatnych nagrobków | Stronicowanie i indeks właściciela; bez kasowania zawartości jako sposobu ograniczania pamięci |
| Rozjazd równoległych pakietów | Osobne worktree, commit bazy A0, jawne rezerwacje i przegląd B1 |

## Odbiór

- [ ] Trzy kary działają zgodnie ze specyfikacją; twarda nie usuwa postaci.
- [ ] Śmierć i ostatnie odzyskanie są atomowe; wszystkie sztuki mają dokładnie jedno miejsce.
- [ ] Nagrobek, zawartość i ustawienia świata przetrwają checkpoint, ogon dziennika oraz restart.
- [ ] Łóżko i start świata korzystają z jednej funkcji Core; `SurfaceFrame` zachowuje konwencję.
- [ ] Nagrobek pod terenem zostaje wyniesiony; nieznany teren nie powoduje utraty przedmiotów ani wiecznej śmierci.
- [ ] Właściciel widzi własne nagrobki w HUD i na mapie; inni nie odczytują ich zawartości.
- [ ] Ekran śmierci i odradzania działa bez klientowej decyzji o odrodzeniu.
- [ ] Reguła „przepięcie gracza jako ostatnia nieodwracalna” dopisana do `docs/ZASADY.md`.
- [ ] Przeglądy Opus zakończone; pełne sprawdzenie zielone w CI; wygląd oceniony przez właściciela.
- [ ] Brak zmian migracji i zamrożonych korpusów; odstępstwa i ograniczenia zapisane w PR.

## DO USTALENIA z propozycjami

1. **Ustawienia świata:** K6-35 przejmuje 46/23/47 z pełnym rekordem spec (D4); konsumuje tylko karę; zatwierdzić w A1/B1.
2. **Pusty nagrobek:** pomijać byt bez przenoszonych przedmiotów; nie zostawiać pustego znacznika.
3. **Ponad 256 stosów:** kilka nagrobków w jednej akcji, bez zmiany globalnego limitu slotów.
4. **Łóżko w locie, przechylenie i geometria:** fallback do startu; wymiary kandydatów z istniejącej kapsuły i modelu łóżka; „w górę” radialne.
5. **Śmierć poza planetą:** nagrobek przy starcie planety domowej; jawne odstępstwo do zatwierdzenia.
6. **Budżet przygotowania powierzchni i limit oczekiwania:** ograniczone skanowanie rozłożone na ticki; limit 60 s, potem nagrobek przy punkcie odrodzenia.
7. **K5-25 nieobecne w bazie:** dostarczyć wspólny resolver i test kontraktowy; podłączenie potwierdzić po udostępnieniu kodu foteli.
8. **Prezentacja:** prosta bryła nagrobka i oznaczenia PL/EN; właściciel zatwierdza wygląd, K6-39 przejmuje docelowy układ.
9. **Przedmioty leżące (rodzaj 3):** K6-35 pisze dostęp i usuwanie ogólnie dla gospodarza `PersistentEntity`, ale dopuszcza tylko nagrobki; regułę `DroppedItem` (dostęp wspólny, 6 m) dodaje K6-36; podnoszenie 2 m i wygaszanie 60 min pozostają „resztą K6-29”.

Niejasność zmieniająca specyfikację albo chroniony format zatrzymuje zależny krok i trafia do opisu PR.
Pozostałą niezależną pracę można kontynuować; nie zastępować brakującej decyzji ukrytą stałą.

## Pułapki

- Nie uzależniać appliera śmierci od vitals, treści, raycastu ani gotowości węzłów Godota.
- Nie resetować zdrowia dopiero w obsłudze `Applied`; reset musi odtworzyć się z dziennika.
- Nie stosować przepięcia przed innymi składowymi ani bez wcześniejszego `CheckReparent` — przepięcie nie ma cofania.
- Nie tworzyć operacji w reentrantnym handlerze replikacji; harmonogram wykonuje je w ustalonej fazie.
- Nie sprawdzać tylko typu zewnętrznej operacji; śmierć i odzyskanie są złożone.
- Nie stosować reguły wygasania przedmiotów leżących do nagrobków.
- Nie przyjmować, że prywatność jest wyłącznie właściwością panelu klienta; zamiana stosów też wkłada do nagrobka.
- Nie traktować wygenerowanej powierzchni jako aktualnego terenu po kopaniu i zasypywaniu.
- Nie podmieniać lokalnych pozycji na globalne ani używać pierwszej planety z posortowanego rejestru.
- Nie liczyć „w górę” z osi budowli przy łóżku na przechylonej konstrukcji.
- Nie zmieniać starych numerów, wersji migracji ani znaczenia zwykłej operacji 7.
- Nie przypinać skrótu na współdzielonej fiksturze.
- Nie poprawiać przy okazji wyrzucania przedmiotów, UI całego ekwipunku ani mechaniki teleportów.
- Nie uruchamiać gry z oknem; ocena wyglądu i zrzuty należą do właściciela.

## Odstępstwa i uwagi z realizacji

Plan przygotowany na podstawie odczytu repozytorium; implementacja nie rozpoczęta w ramach tego zadania.
Planowane rozszerzenia wymagające jawnego odbioru: `ResetVitals` w op 7 v2, przepięcie gracza jako ostatnia składowa złożonej i pełne ustawienia świata przejęte z „reszty K6-29”.
Proponowane odstępstwa rozgrywki: pomijanie pustego nagrobka, kilka nagrobków przy nadmiarze slotów, fallback dla śmierci poza planetą i po limicie oczekiwania.
Po każdym etapie wykonawca dopisuje commit, wyniki testów, werdykt Opus i rozstrzygnięcia DO USTALENIA.
Nie oznaczać testów, integracji foteli ani oceny wyglądu jako wykonanych wyłącznie na podstawie tego planu.

## Przegląd planu (Opus 5.5)

Data: 2026-10-16. Podstawa: plan Codexa (gpt-6.1-sol), worktree `k6-34` (tylko odczyt, bez gita, bez budowania i testów). Werdykt: **GO z poprawkami** (16 poprawek; 3 istotne dla poprawności: P1–P3).

| # | Co zmieniono | Dlaczego |
| --- | --- | --- |
| P1 | D18, C3, test `LethalTick_DeathInSameTick`: `DeathService` w fazie `Compose` po `VitalsService` | Plan twierdził, że vitals liczą się w `Characters`; kod: `VitalsService.cs` l. 52 = `Compose`. Rejestracja w złej fazie dawałaby śmierć tick później albo przed obrażeniami (lekcja „skutek w ticku kontaktu”) |
| P2 | D6, kontrakty, A3, testy kompozycji: przepięcie gracza jako **jedyna nieodwracalna składowa, ostatnia**, sprawdzana przed pozostałymi; bez cofania pozy, fotela i vitals | ZASADY l. 29 i istniejący wzorzec `TryApplyCompound` (l. 263–290); warunki `CheckReparent` nie zależą od innych składowych śmierci; mniej chronionego kodu w `UndoLog` |
| P3 | D5: `RespawnReset` (zdrowie, wytrzymałość, 5 pojemności) zastąpiony bajtem `ResetVitals` → `Vitals = null` | Kod: `null` → NaN → maksimum (`VitalsService.Player.cs` l. 44, `VitalsStep` l. 34–35); K6-34 D4: `Suit = null` = świeży kombinezon z pojemności aktualnego wyposażenia. Operacja nie niesie wartości treści (ZASADY §4), znika obliczanie „pojemności po karze” |
| P4 | D7, test: zerowanie `PendingDamage`/`PendingCost` po zmianie `ParentSeq` | Obrażenia zebrane przed śmiercią trafiłyby w nowe ciało |
| P5 | D13, test `MoveOrSwapIntoTombstone_Refused` | Lekcja K6-32: zamiana stosów też wkłada do kontenera tylko-do-wyjmowania |
| P6 | D16, test `TerrainNeverReady_FallsBackToRespawnPoint`: limit oczekiwania | Bez limitu niegotowy teren zostawia gracza martwego na zawsze |
| P7 | D19, test: koniec subskrypcji przed operacją śmierci | Spec l. 383; plan tego nie wymieniał |
| P8 | D15, test `Resolve_TiltedConstruct_UsesPlanetUp` | Lekcja K6-33 (odpoczynek w przechylonej budowli): kandydaci przy łóżku muszą stać wzdłuż radialnego „w górę” |
| P9 | D12, builder, test persystencji: rama bytu trwałego = planeta | Usunięcie/zawalenie budowli (K6-36) nie może usunąć ramy nagrobka |
| P10 | D4: pełny rekord ustawień spec w v1 (ushort setne, zarezerwowane bity) | Unika kolejnych podbić formatu 46/23/47 przez K6-36, K6-37, K6-39; K6-36 czyta z niego tryb podparcia |
| P11 | D22, A0: pin skrótu na własnej zamrożonej fiksturze | Lekcja K6-31 (`db779bd`): zmiana wspólnej fikstury przestawiła pin |
| P12 | Testy: progi pokrycia w `DeathConservationProperties`, zakaz pustych przejść | Lekcja: testy przechodzące pusto |
| P13 | D20: indeks `OwnedBy`, `OperationProgress` rozpoznaje 38/39 | Bez indeksu każda wysyłka 48 skanuje wszystkie byty |
| P14 | Kontekst: fakty z numerami linii; „Niesprawdzone” wypisane jawnie; tabela rezerwacji uzupełniona o K6-36 | Weryfikacja faktów i brak kolizji numerów z następnikiem |
| P15 | Kontrakty: tabela dozwolonych wersji per rodzaj w `OperationCodec` | Kod ma jedną stałą wersji (l. 408); K6-36 użyje tego samego mechanizmu dla op 30 v2 |
| P16 | DO USTALENIA-9 i Koordynacja K6-36: ogólna ścieżka kontenerów `PersistentEntity` | K6-36 potrzebuje przedmiotów leżących dla zwrotów; bez tego jego etap C byłby zablokowany |

Sprawdzone bez uwag: wolne numery (op 38/39/46, bloby 23/24, komunikat 47, tagi `0x54`/`0x57`, odmowa 48 nazwana w spec, rodzaje bytów 3/6); kodek gospodarza 3 już istnieje; `WorldLoader` odrzuca dziś byty inne niż budowle; `MaxContainerSlots = 256`; brak `RespawnPoints`/`SeatEvictIntent`.
**Niesprawdzone:** miejsce wysyłki migawek względem faz; rejestr komend konsoli; filtr `QuickStack`; czy encje mogą mieć ramę statycznej budowli (A0).

**DO USTALENIA dla właściciela:** (1) czy K6-35 przejmuje pełne ustawienia świata 46/23/47 (propozycja: tak); (2) pomijanie pustego nagrobka; (5) nagrobek przy starcie domowej planety przy śmierci poza planetą; (6) limit 60 s i nagrobek przy punkcie odrodzenia; (9) kto realizuje podnoszenie 2 m i wygaszanie przedmiotów leżących („reszta K6-29” nie ma dziś pakietu w łańcuchu).
