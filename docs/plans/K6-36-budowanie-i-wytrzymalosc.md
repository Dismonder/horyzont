# K6-36 — Budowanie z zasobów i wytrzymałość

| Pole | Wartość |
| --- | --- |
| Gałąź implementacji | `pakiet/k6-36-budowanie-i-wytrzymalosc` |
| Wykonawca | Codex `gpt-6.1-sol`; wysiłek rozumowania `high` dla Core |
| Przeglądy | Opus 5.5: A1, A6, B1, B6, C6 i końcowy PR (pakiet GPT-6.1 Sol → Opus, ZASADY §9) |
| Zależności projektu | K6-29, K6-31, K2-12; w łańcuchu realizacji baza = scalone K6-35 |
| Odbiorcy kontraktów | K6-39, K9-61; następnie K6-40 |
| Protokół docelowy | `ProtocolConstants.ProtocolVersion = 21` (`main` + 1), po K6-34 = 19 i K6-35 = 20 |
| Etapy | A — Core; B — protokół i treść; C — zapis i serwer; D — klient |
| Stan dokumentu | Plan przygotowany przez odczyt repo; budowania, testów i gry nie uruchamiano |

## Kontekst

Źródła prawdy: `AGENTS.md`, `docs/ZASADY.md`, `docs/spec/03-przetrwanie.md` (l. 89–114, 283–284, 292, 298, 381, 387–391, 405–410) i `docs/plan/plan-projektu.md` (l. 137).
Wzorce wykonania: `docs/plans/K6-30-bloki-funkcyjne.md`, `K6-31-zbieranie.md`, `K6-32-rzemioslo-i-stacje.md`, `K6-34-zagrozenia-i-kombinezon.md`, plan K6-35.
Stan kodu poniżej sprawdzono w worktree `k6-34` (`ProtocolVersion` = 18; K6-34 częściowo). Kontrakty K6-35 (38/39, blob 24, `WorldSettings`) są **planowane**, nie w kodzie.

| Ustalenie z kodu | Miejsce i znaczenie |
| --- | --- |
| Postawienie daje `BlockSetOp` albo `ConstructCreateOp` | `Simulation/IntentResolver.cs`; obecnie `BlockState.Create` używa domyślnej rewizji kosztu 0 |
| Rozbiórka zwykłego bloku daje `BlockClearOp` | `IntentResolver.cs`; koszt i zwrot nie są tu rozwiązywane |
| Kolejność resolverów | `ServerSimulation.Submit` l. 176–221: `PropHit` → `_crafting.TryResolve` ‖ `_functional.TryResolve` → `_survival.Resolve` albo `IntentResolver`; koszt trzeba objąć na wszystkich gałęziach |
| Odmowy po kosztownym skanie | `Submit` l. 199–203 czyta współdzieloną flagę `_functional.RefusalCostsToken`; odmowy `_survival` (l. 216–219) żetonu nie pobierają; przyjęcie pobiera (l. 238) |
| Stan bloku jest już przygotowany na koszt | `World/Constructs/BlockState.cs` l. 17, 33–34: `CostRevisionCount = 16`, bity 21–24 |
| Bity funkcji są zajęte | Bity 25–27 (`FunctionStateMask`); bity 28–31 muszą być zerem (`IsWellFormed`) |
| Definicja bloku nie ma podparcia | `World/Blocks/BlockTypeDef.cs`: klucz, kształt, materiał wizualny, kolizja, masa, funkcja; treść, nie format (ZASADY §3) |
| Masa nie jest wytrzymałością | `MassKg` służy istniejącym mechanikom statków |
| Budowle są siatką rzadkich chunków | `World/Constructs/Construct.cs`; `Chunks` tworzy posortowaną kolekcję, więc nie nadaje się wprost do gorącej ścieżki |
| Tryby budowli są jawne | `ConstructMode.Static` i `Ship`; nie rozpoznawać statku po kokpicie |
| Usuwanie z budowli ma ograniczenia | `IntentValidator.cs` l. 453–464: ostatni kokpit, ostatni silnik, fotel, ostatni blok ramy z dziećmi (`state.HasChildren`) — applier też tego odmawia |
| Źródła składników mają precedens | `CraftingRules.BuildSources` l. 311–355: plecak, pasek, skrzynie `Shared` **oraz własne prywatne**, od oka gracza, `rules.CraftContainerRangeMeters`, sortowanie bez alokacji |
| Atomowość ma istotne ograniczenie | `OperationApplier.TryApplyCompound` l. 239–290: jedna końcowa edycja terenu albo budowli; zakaz łączenia stanu funkcji z edycją budowli (l. 265) |
| Jedna wersja formatu na cały kodek | `OperationCodec` l. 16/408 i `IntentCodec` l. 15: wspólne `CurrentFormatVersion = 1`; wersje per rodzaj trzeba dodać (K6-35 robi to dla operacji) |
| Limity | `ProtocolConstants`: `MaxCompoundComponents = 4096` (l. 136), `MaxCompoundBytes = 160 KiB` (l. 139), `MaxConstructChangesPerOperation = 1024` (l. 82) |
| Replikacja zakłada pojedynczą budowlę | `Client/Replication/ConstructMirror.cs` l. 150: odrzuca `ConstructChanges.Count != 1`; `ConstructChange` = (budowla, wersja, usunięta) |
| Teren udostępnia tylko znak SDF | `ITerrainService.TrySampleSolid(…, out bool solid)` (l. 38); nie wystarcza do progu −0,25 |
| `IntentReply` (komunikat 12) | `(ClientSeq, Reason, AppliedOpId)` — brak miejsca na podgląd |
| Przedmioty leżące | Plan K6-29W l. 57–58: 38/39, blob 24, rodzaje 3/6 → „reszta K6-29” i K6-35; plan K6-35 dostarcza 38/39, blob 24, gospodarza `PersistentEntity` i ogólną ścieżkę odzyskiwania (DO USTALENIA-9 K6-35) |
| Generator tlenu | Plan K6-34 l. 143: `core:oxygen_generator` „koszt zero do K6-36” |

Specyfikacja wymaga kosztów historycznych, pełnych zwrotów, podparcia, potwierdzania kaskad i spójności statków.
**Nie definiuje HP bloków, uderzeń ani napraw** — „wytrzymałość” w spec 03 i w tabeli projektu oznacza podparcie konstrukcji. Ten pakiet ich nie wprowadza (D12).
Nie wolno uznać podparcia za HP ani przenieść do bloków zasad obiektów generatora.

## Zakres

1. Koszt każdego stawialnego bloku, historia rewizji i atomowe pobieranie przedmiotów.
2. Rozbiórka ze zwrotem według zapisanej rewizji, również przy pełnym ekwipunku (nadmiar jako przedmiot leżący).
3. Deterministyczny `IntegrityEvaluator`, kaskady i tryby `On`, `Relaxed`, `Off`.
4. Spójność statków niezależna od ustawienia podparcia.
5. Kaskada po kopaniu terenu, obejmująca wszystkie dotknięte budowle w jednej operacji.
6. Zapis, odtwarzanie, replikacja wielu zmian oraz minimalny interfejs budowania (koszt, podparcie, potwierdzenie).
7. Przedmioty leżące jako zwrot: tworzenie (rodzaj 3), dostęp i prezentacja; bez podnoszenia 2 m i wygaszania.
8. Punkty integracji ustawień dla K6-39 oraz tekstów i dostępności dla K9-61.

Poza zakresem: HP bloków, uderzenia i naprawy (D12), walka, broń, eksplozje, najazdy, zużycie narzędzi, fizyczne odłamki i dzielenie statków.
Poza zakresem: automatyczne podnoszenie w promieniu 2 m i wygaszanie przedmiotów leżących („reszta K6-29”).
Pełny ekran ustawień należy do K6-39; końcowa ocena wyglądu należy do właściciela.

## Decyzje

- **D0. Baza i zależności.** Realizacja startuje z bazy zawierającej scalone K6-35 (a więc K6-29W…K6-34).
  K6-35 dostarcza: op 38/39, blob 24, `PersistentEntityStore` z rekordami rodzaju 3, gospodarza `PersistentEntity` w applierze, ogólną ścieżkę odzyskiwania kontenerów bytu
  (ostatnie przeniesienie + 36 + 39 w jednej złożonej) oraz `WorldSettings` (46/23/47). Etap C nie jest więc zablokowany brakiem K6-29.
  Jeśli K6-35 nie jest scalone w chwili startu, A–B mogą powstawać na K6-34, a C czeka; nie implementować zastępczej logistyki ani gubienia zwrotów.
  K6-30 jest zależnością przechodnią; obsługa stacji K6-32 i generatora K6-34 ma pozostać zachowana.

- **D1. Numery.** Nie przydzielać nowego kodu operacji: spec rezerwuje całe 30–49.
  Dodać intencję 34 `BuildInspectIntent` (zakres Przetrwania 20–39, K1-03 l. 621; 34 wolne w `IntentCodec`).
  Dodać odmowy ze spec: 51 `NoSupport`, 52 `WouldSplitShip`, 55 `CollapseNotConfirmed`, 58 `CollapseTooLarge`.
  Nie zajmować intencji 30–33, odmów 47, 50, 56, 59. Nie dodawać tagu skrótu ani bloba. Rezerwacje poprzedników w sekcji Koordynacja.

- **D2. Wersje.** Protokół K6-36 = `main` + 1 przy scaleniu, oczekiwane **21**; worktree `k6-34` ma dziś 18 — nie ustawiać na zapas.
  Konflikt z dodatkowym scalonym pakietem rozstrzyga rejestr protokołu (`docs/plans/README.md` D4), bez cichego obniżenia.
  Intencja 34 ma format 1; intencje 1 (`TerrainTool`) i 3 (`RemoveBlock`) dostają format 2 z odczytem formatu 1 (spec l. 298).
  `IntentCodec` dostaje tabelę dozwolonych wersji per kod (wzorem tabeli operacji z K6-35), zamiast jednej stałej.

- **D3. Rewizja 0 oznacza blok bez należnego zwrotu. [doprecyzowanie]**
  Wszystkie istniejące bloki zapisano z rewizją 0 i nie pobrano za nie kosztu; zwrot stworzyłby darmowe zasoby.
  Historia każdego bloku zawiera niezmienną pustą rewizję 0, a płatne rewizje zajmują 1–15 (razem 16 = `CostRevisionCount`, zgodnie ze spec l. 219).
  Darmowe stawianie zapisuje 0; płatne stawianie zapisuje bieżącą płatną rewizję. Nie wolno ponownie użyć wycofanej rewizji.

- **D4. Tryby świata z `WorldSettings`. [zmienione w przeglądzie]**
  Źródłem jest rekord ustawień świata z K6-35 (D4 K6-35): `BuildingIntegrityMode` (`On`/`Relaxed`/`Off`) i bit „budowanie bez kosztów”; trwały, replikowany (47) i haszowany (`0x57`).
  Zmiana przez op 46 nie wywołuje kaskady (D9). Presety nie znoszą spójności statków, uprawnień, zasięgu ani zabezpieczeń pasażerów.
  Bez treści budowania (format `blocks.json` 2) zachować dotychczasową ścieżkę K2, aby stare testy nie zmieniały zachowania.
  Fallback, jeśli właściciel odrzuci DO USTALENIA-1 K6-35: pola w `GameRules` (jak plan Codexa), przeniesienie do ustawień w K6-39.

- **D5. Źródła kosztu. [zmienione w przeglądzie]**
  Wspólny pomocnik wydzielony z `CraftingRules.BuildSources` bez zmiany jego zakresu: plecak, pasek, następnie skrzynie `Shared` i własne prywatne skrzynie gracza,
  na tej samej planecie, do 20 m od oka serwera, sortowane po odległości, przy remisie po `ContainerId`; sloty rosnąco; sortowanie bez alokacji.
  Nie wymagać otwarcia ani subskrypcji skrzyni; nie stosować limitu otwartego kontenera 8 m.
  Wykluczyć cudze prywatne skrzynie, wyposażenie, nagrobki, przedmioty leżące i kontenery stacji/generatora.
  Ładownie jako źródło automatyczne pozostają DO USTALENIA; domyślna propozycja ich nie obejmuje.

- **D6. Zwrot.** Płatna rozbiórka zwraca 100% historycznego kosztu (`ItemCreateOp`, źródło `Refund`) do plecaka, potem paska.
  Nadmiar tworzy trwały przedmiot leżący pod nogami gracza (spec l. 381): 38 (rodzaj 3, rama planety) + 35 (`DroppedItem`, gospodarz byt) + 33 (`Refund`).
  Zawalenie zwraca koszt jako przedmioty leżące „po jednym bycie na rodzaj przedmiotu w miejscu zawalenia” (spec l. 102) — DO USTALENIA-4.
  `FreeBuilding` wyłącza zarówno pobieranie, jak i zwrot; rewizja bloku nie jest przy tym przepisywana.
  Rozbiórka pełnej skrzyni nadal odmawia (`ContainerNotEmpty`); zawalenie skrzyni/stacji/generatora przenosi jej zawartość **transferami** (`ItemTransferOp`) do kontenerów przedmiotów leżących, nigdy przez zniszcz–utwórz.
  Rama bytu leżącego to zawsze rama planety (K6-35 D12), więc kolejne zawalenie nie usuwa jego ramy.

- **D7. Podparcie.** Liczyć na sześciu sąsiadach ściennych; nie łączyć różnych `Construct`.
  Uziemienie: SDF w środku dolnej ściany ≤ −0,25; dół to lokalna oś −Y budowli.
  Przy składaniu ram używać `FrameMath`; nie zakładać, że rodzicem jest bezpośrednio planeta.
  Parametry konstrukcyjne są niezależne od materiału wizualnego, kolizji, kształtu i masy.
  Proponuje się, że każdy zajęty blok przenosi podparcie, także szkło, drzwi i bloki siatkowe (DO USTALENIA-5).
  Podparcie dotyczy tylko budowli `Static`; statki podlegają wyłącznie D10.

- **D8. Punkt stały.** Start od uziemionych bloków; pozostałe mają 0.
  Dla bloku `b`: najlepsze podparcie sąsiada minus strata materiału `b`, ograniczone do jego maksimum i minimum 0.
  Dla pary sąsiadów wzdłuż Y używać straty pionowej; wzdłuż X/Z — poziomej.
  `Relaxed` stosuje `(loss + 1) / 2` (dzielenie całkowite = zaokrąglenie w górę); `Off` pomija podparcie, lecz nie spójność.
  Po usunięciu bloków poniżej minimum przeliczać aż do braku kolejnych zawaleń; wynik nie zależy od kolejności.

- **D9. Zmiany treści i ustawień.** Wczytanie, aktualizacja treści i zmiana trybu nie generują samoczynnej kaskady.
  Istniejące niestabilne obszary oznaczać (stan pochodny, nie zapisywany); następna ingerencja w ich składową wymaga potwierdzenia niezależnie od liczby bloków.
  Zwykła zmiana zawalająca najwyżej 10 bloków nie wymaga potwierdzenia; 11 i więcej wymaga.
  Nowego bloku bez podparcia nie stawiać nawet z potwierdzeniem (`NoSupport`).
  Samo oglądanie budowli nie zmienia świata ani nie zapisuje operacji.

- **D10. Statki.** Po planowanej zmianie wszystkie pozostałe bloki statku muszą tworzyć jedną składową ścienną.
  Sprawdzać także przejście `Static` → `Ship` (op 20, walidator K5-23): budowla statyczna rozdzielona na wyspy (dozwolone przy podparciu) dostaje `WouldSplitShip`.
  Dokładanie bloku na ścianie zawsze styka się z istniejącym blokiem, więc nie wymaga osobnego sprawdzenia spójności.
  Zachować wcześniejsze odmowy `NoCockpit`, `NoEngine`, `IntersectsPlayer`; nie zastępować ich nowym kodem.

- **D11. Atomowość: `CompoundOp` format 2.**
  Format 1 zachowuje bajty i semantykę, w tym jedną końcową edycję nieodwracalną.
  W formacie 2 edycje budowli (`BlockSetOp`, `BlockClearOp`, `ConstructCreateOp`) są **odwracalnymi** składowymi z cofaniem komórek, chunków, nagłówków,
  usuniętych budowli, indeksów i wersji; jedyną nieodwracalną pozostaje edycja terenu, zawsze ostatnia — reguła ZASADY l. 29 zostaje zachowana, zmienia się tylko lista składowych odwracalnych (dopisać do ZASADY).
  Przed edycją terenu muszą być sprawdzone wszystkie warunki; żadna zawodna składowa nie występuje po niej.
  Zakaz łączenia `BlockFunctionStateSetOp` z edycją tej samej budowli (l. 265) obowiązuje także w v2; zawalenie drzwi czyści blok, nie przełącza stanu.
  Usunięcie ostatniego bloku budowli z dziećmi ramy (l. 462–464) jest odmowane już w planie (D13), nie dopiero w applierze.
  Nadal brak zagnieżdżania, limit 4096 składowych, 160 KiB całej zakodowanej operacji i 1024 zmienionych budowli (`MaxConstructChangesPerOperation`); przekroczenie → 58.
  Wersja jest zachowywana w obiekcie `CompoundOp`, aby replay nie wybierał semantyki według bieżących ustawień.
  Po wycofaniu tracker zmian odpowiada stanowi (nadmiarowe oznaczenie „zmieniona” jest nieszkodliwe; pozostawione „usunięta” — błąd; test).

- **D12. HP bloków, uderzenia i naprawa — poza pakietem. [zmienione w przeglądzie] DO USTALENIA-1.**
  Spec 03 i tabela projektu nie przewidują HP bloków; plan Codexa dodawał intencje 34/35, `BlockDamageLedger` i formułę obrażeń jako rozszerzenie specyfikacji.
  Decyzja: bez HP w K6-36. Rozbiórka to istniejąca intencja 3 (natychmiastowa, ze zwrotem). Uszkadzanie budowli należy do projektu walki i najazdów (K7, najazdy zarezerwowane w ustawieniach).
  Jeśli właściciel zechce HP w EA, potrzebny osobny pakiet z kontraktami; numery intencji 35–39 pozostają wolne.

- **D13. Potwierdzenie bez stanu serwera. [zmienione w przeglądzie]**
  `AllowCollapse` nie może potwierdzać dowolnej przyszłej kaskady.
  Odmowa 55 niesie podgląd i `CollapseDigest` (u64, XxHash64): kod akcji i jej parametry, posortowany zbiór (budowla, komórka) zawaleń oraz wersje dotkniętych budowli.
  Ponowienie niesie `AllowCollapse = true` i ten skrót; serwer przelicza plan i porównuje — różnica daje ponownie 55 z nowym podglądem.
  Brak magazynu biletów, wygasania, limitu na gracza i stanu współdzielonego; działa po ponownym połączeniu. Nie zapisywać skrótu w świecie; replay zawiera już ostateczne skutki.
  Zmiana terenu poza zbiorem uziemień wpływa na wynik tylko przez zbiór zawaleń, więc skrót go pokrywa.

- **D14. Informacja dla klienta bez nowego komunikatu.**
  Dopisać opcjonalny ogon budowania do `IntentReply` 12, zachowując bajt wersji ramki `01` i podbijając protokół do 21.
  Ogon niesie podgląd kaskady (liczba, komórki do limitu, `CollapseDigest`) albo wynik inspekcji (koszt, dostępność, podparcie celu).
  Wszystkie odpowiedzi protokołu 21 zapisują bajt obecności ogona; brak danych to 0.
  `BuildInspectIntent` 34 służy wyłącznie odczytowi dostępnego celu, bez operacji, bez rezerwacji identyfikatorów i bez otwierania skrzyń.
  Nie wysyłać identyfikatorów ani zawartości skrzyń; pokazać jedynie łączne dostępne ilości składników.

- **D15. Nieznana treść.** Brak historycznej rewizji kosztu oznacza błąd walidacji świata/treści.
  Nie zastępować kosztu historycznego aktualnym ani kosztem placeholdera.
  Blok bez definicji pozostaje widoczny jako istniejący placeholder; płatne operacje wymagają poprawnej historii.
  Historia jest dopisywana, nie edytowana; kontrolować jej niezmienność testem względem treści bazowej.

- **D16. Odmowy kosztowne pobierają żeton — wynik wywołania, nie flaga. [dodane w przeglądzie]**
  Odmowy po ocenie podparcia, skanie źródeł albo podglądzie terenu (`NoSupport`, `WouldSplitShip`, `CollapseNotConfirmed`, `CollapseTooLarge`, `NotEnoughItems` po skanie skrzyń, `AreaNotLoaded` po próbie podglądu)
  pobierają żeton kubełka swojej klasy (bloki albo teren) tak jak przyjęcie; tanie odmowy walidatora — nie (K1-03).
  Informację „odmowa kosztowna” zwraca resolver w wyniku wywołania (`BuildResolution.CostsToken`), a nie we współdzielonym polu obiektu reguł —
  nie powielać wzorca `_functional.RefusalCostsToken` (`ServerSimulation` l. 199), w którym nieaktualna wartość może przeciec między gałęziami `||`.
  Inspekcja 34 zawsze pobiera żeton własnego kubełka (4/s, seria 4), także przy odmowie.

- **D17. Przedmioty leżące ze zwrotu. [dodane w przeglądzie]**
  Dostęp: kontener `DroppedItem` jest `Shared` dla każdego gracza w zasięgu 6 m (subskrypcja do 8 m), z ogólnej ścieżki K6-35; nagrobki pozostają prywatne.
  Odzyskanie ostatniej sztuki usuwa kontener i byt w tej samej złożonej (K6-35). Wkładanie i zamiana do kontenera leżącego są odmawiane.
  Prezentacja: `EntitySpawn` rodzaju 3 z `Archetype` = `ItemTypeId` pierwszego stosu i liczbą sztuk w `KindData` (spec l. 344), widoczność 64 m.
  Podnoszenie w promieniu 2 m i wygaszanie po 60 min nie są w tym pakiecie; przedmioty czekają do ręcznego odzyskania.

## Kontrakty chronione i wersje

| Kontrakt | Przyrost w K6-36 |
| --- | --- |
| `Operations/CompoundOp.cs`, `OperationCodec` | Format 2 tylko operacji 30; zachowany odczyt i stosowanie formatu 1 |
| `OperationApplier.cs`, `UndoLog.cs` | Ścieżka formatu 2 z odwracalnymi edycjami budowli; stare operacje bez zmiany semantyki |
| `TerrainToolIntent` 1, `RemoveBlockIntent` 3 | Format 2: dotychczasowe pola, `AllowCollapse`, `CollapseDigest` przy `AllowCollapse = 1` |
| Nowa intencja 34 | Format 1; parametry opisane w Projekcie |
| `IntentReply`, `IntentResult`, `MessageCodec` | Ogon budowania; protokół 21; rozszerzyć także ścieżkę lokalną hosta |
| `BlockTypeDef`, katalog treści | Koszty i parametry podparcia; treść, nie format zapisu |
| `BlockState`, bloby chunków | Bez zmiany układu bitów i wersji |
| Checkpointy i `StateHasher` | Bez nowego tagu; pełny zapis wszystkich skutków kaskady; byty leżące w `0x54` (K6-35) |
| `OperationConstructs`, repliki i dirty chunks | Projekcja wielu zmian i wielu budowli; bez danych ekwipunku |
| Migracje | Brak nowej migracji K6-36 |
| `ITerrainService` / nowy interfejs próbkowania | Przyrost API odczytu i podglądu; bez zmiany semantyki istniejącego `ApplyEdit` |

Każda zmiana Core wymaga Opus. Podniesienie protokołu nie zastępuje wersji operacji 30.
Nie zmieniać `ClientHello`, `HandshakeReject`, starych numerów, migracji ani zamrożonego korpusu.
Nie zmieniać `TerrainEditSemantics.SemanticsVersion`: podgląd ma odtwarzać istniejące działanie, nie definiować nowe.
Jeżeli zgodnego podglądu nie da się wykonać (spike A0b), zatrzymać C i przedstawić konkretną rozbieżność Opusowi.

## Projekt

### Pliki i odpowiedzialności

| Obszar | Pliki istniejące i nowe |
| --- | --- |
| Core — czyste reguły | Nowy katalog `src/Horyzont.Core/Building/`: `IntegrityEvaluator`, `IntegrityWorkspace`, `IntegrityMaterialDef`, `BuildCostDef`, `BuildCostRevision`, `ShipConnectivity`, `CollapseDigest` (`BuildingIntegrityMode` pochodzi z K6-35) |
| Core — rozwiązywanie | `Simulation/BuildingRules.cs`, `BuildingPlan.cs`, `BuildingSources.cs` (wydzielony z `CraftingRules`), `BuildResolution.cs`; zmiany `ServerSimulation`, `IntentValidator` |
| Atomowość | `Operations/CompoundOp.cs`, `OperationConstructs.cs`, `Simulation/OperationApplier.cs`, `UndoLog.cs`, `Serialization/OperationCodec.cs` |
| Intencje | Nowy rekord 34 w `Intents/`; zmiany dwóch istniejących rekordów i `IntentCodec` |
| Treść bloków | `World/Blocks/BlockTypeDef.cs`, `Constructs/Content/BlockTypeCatalog.cs`, `game/content/blocks/blocks.json` |
| Łączenie treści | `Content/ContentLoader.cs`, `SurvivalContent.cs`, `game/src/Content/SurvivalContentLoader.cs`, `game/src/Constructs/BlockContentLoader.cs` |
| Teren | Nowy interfejs `World/Terrain/ITerrainIntegrityProbe.cs`; implementacja w `game/src/Terrain/`, fake w `tests/Horyzont.Testing/` |
| Replikacja | `Server/Replication/EntityReplication.cs`, `Core/Replication/ConstructReplica.cs`, `Client/Replication/ConstructMirror.cs`, `Constructs/Sources/ConstructDirtyChunks.cs`, prezentacja rodzaju 3 |
| Zapis | `Checkpoints/ChangeTracker.cs`, `WorldCheckpoint.cs`, `WorldStateBuilder.cs`; w Persistence tylko niezbędne poprawki i testy |
| Klient | Nowy katalog `src/Horyzont.Client/Building/`; integracja `Tools/ToolController.cs`, `ToolPreview.cs`, `PendingIntentTracker.cs` |
| Godot | `game/src/Client/Tools/ToolHud.cs`, `BlockGhostPreview.cs`, nowy panel potwierdzenia; composition root w `game/src/Boot/` |
| Teksty | `game/content/locale/tools.csv`, `blocks.csv`; istniejące położenie potwierdzić przed edycją (niesprawdzone) |

Nazwy nowych plików są docelowe. Nie tworzyć drugiego resolvera przedmiotów ani drugiego composition root.

### Dane treści

`blocks.json` przechodzi na format 3; loader nadal czyta format 2 jako treść bez reguł budowania.
Format 3 wymaga dla każdego stawialnego bloku `build` i `integrity` (bez `hitPoints` i `requiredToolTier` — D12).
Przykład pojedynczego wpisu; pełny plik zachowuje istniejące materiały, funkcje i klucze:

```json
{
  "key": "core:wood_planks",
  "shape": "cube", "mass": 500, "material": "wood",
  "build": {
    "currentRevision": 1,
    "revisions": [
      { "revision": 0, "items": [] },
      { "revision": 1, "items": [{ "item": "core:wood", "count": 2 }] }
    ]
  },
  "integrity": "wood"
}
```

| Materiał konstrukcyjny | Maksimum | Minimum | Strata pionowa | Strata pozioma |
| --- | ---: | ---: | ---: | ---: |
| `wood` | 100 | 10 | 1 | 12 |
| `core_wood` | 140 | 10 | 1 | 10 |
| `stone` | 1000 | 100 | 1 | 450 |
| `metal` | 1500 | 20 | 1 | 40 |

Materiały konstrukcyjne są osobną tabelą; nie zmieniać indeksów materiałów renderowania.
Koszt: maksymalnie 16 rewizji (z rewizją 0), 16 składników rewizji, dodatnie ilości mieszczące się w `ushort`.
Zakazać powtórzonego składnika, nieznanego przedmiotu, pustej rewizji płatnej i nieistniejącej rewizji bieżącej.
Rewizja 0 musi być pusta; historyczne składniki mogą być `Retired`, jeśli nadal istnieją w rejestrze.
Tabela kosztów obejmuje **wszystkie** stawialne bloki, także stacje K6-32, łóżka/skrzynie/drzwi/lampy K6-30 i generator tlenu K6-34; zatwierdza ją B1.
Koszt bloku nie stanowi źródła surowca w walidacji „przedmiot bez źródła”; zwrot nie może tworzyć obiegu bez źródła początkowego.

### Algorytm, teren i plan zmiany

`IntegrityEvaluator` przyjmuje niemutowalny widok bloków, tabelę materiałów, uziemienia i tryb; nie zna Godota.
Używać kolejki propagacji maksimum i pamięci roboczej wielokrotnego użytku; iteracja wejścia nie wpływa na wynik.
Zmiana lokalna unieważnia zależny obszar, również przez granice chunków; usunięcie podpory musi obniżać stare wyniki.
Pełna ocena po wczytaniu działa na kopii danych; wynik instalować tylko przy zgodnej wersji budowli i ustawień.
Próbki terenu zbierać wyłącznie na wątku głównym; worker nie czyta `WorldState`, `Construct` ani terenu.

`ITerrainIntegrityProbe` udostępnia rzeczywisty SDF oraz ograniczony, niemutujący podgląd próbek po wskazanej edycji.
Podgląd jest potrzebny **tylko dla trybu `Remove`**: dodanie terenu nie zmniejsza uziemienia, więc `Add` nie wywołuje kaskady.
Podgląd kopiuje potrzebne woksele z marginesem interpolacji i stosuje te same reguły Voxel Tools co edycja;
próbkowane są tylko środki dolnych ścian w promieniu edycji + margines interpolacji.
Nie zastępować go testem „punkt w kuli” ani `max(sdf, radius-distance)` bez dowodu zgodności z adapterem.
Nieznana próbka lub niewczytany obszar daje `AreaNotLoaded`, nigdy automatyczne uziemienie albo zawalenie.
Kopanie wyszukuje budowle przestrzennie, ocenia zmienione uziemienia i propaguje skutki poza obszar samej kuli.

`BuildingPlan` przechowuje gotowe zmiany, koszty, zwroty, wysypane przedmioty i wersje oczekiwanych danych.
Nie mutuje świata podczas przygotowania; identyfikatory rezerwuje bez zużycia przy odmowie.
Przekroczenie limitów składowych, bajtów lub liczby budowli daje 58 przed pobraniem kosztu i edycją terenu.
Zawalenie usuwające ostatni blok budowli, której rama ma dzieci, daje odmowę (DO USTALENIA-7), zanim powstanie operacja.
Wszystkie zawalenia, opróżnienia, usunięcia stanów i zwroty są częścią jednego wpisu dziennika.

### Walidacja i przepływ

1. Znany gracz → online → nie martwy i nie w przejściu (bramka `InTransition` 48 z K6-35) → poprawne parametry → właściwy limit intencji.
2. Cel: budowla/planeta/komórka → zasięg od oka → linia widzenia → uprawnienia i ograniczenia istniejących funkcji.
3. Plan: geometria → koszt i źródła → podparcie/spójność → kaskada → limity → potwierdzenie (`CollapseDigest`).
4. Zastosowanie i dziennik → replikacja → odpowiedź dla inicjatora; odmowy kosztowne pobierają żeton (D16).

`BuildInspectIntent` 34: cel postawienia (typ bloku, budowla/teren, ściana) albo istniejący `BlockRef`; koszt oblicza serwer z typu.
Inspekcja nie stosuje zmian, nie rezerwuje identyfikatorów i nie zużywa materiałów; kończy się `AcceptedWithoutOperation` z ogonem D14.

Wspólny koordynator budowania obejmuje wyniki resolverów zwykłych, funkcyjnych, stacji i generatorów.
Nie wystarczy dopisać kosztu w `SurvivalRules.Resolve`, bo wcześniejsze resolvery (`_crafting`, `_functional`) omijają tę gałąź.
Przy kopaniu zachować urobek, `YieldLedger`, usuwanie obiektów K6-31 i jedną końcową edycję terenu.
Przy zawaleniu stacji/generatora rozliczyć proces według istniejących zasad przed wysypaniem jego rzeczywistego stanu.

### Replikacja, zapis i HUD

`OperationConstructs` ma zwracać pełną projekcję wszystkich zmian budowli, także wielu `BlockClearOp` (ZASADY l. 28).
`ConstructMirror` najpierw waliduje cały zestaw, potem stosuje go atomowo; nie odrzuca wielu `ConstructChanges`.
Przy luce wersji żąda pełnego stanu dotkniętych budowli; duplikat nie stosuje ponownie zwrotów ani usunięć.
Subskrybenci skrzyni zawalonej w kaskadzie dostają usunięcie kontenera, a nie deltę (wzorzec K6-30).
Wersje serwera rosną zgodnie z istniejącymi mutacjami; klient otrzymuje i ustawia końcowe wersje, bez zgadywania.
Dirty chunks obejmują wszystkie zmienione komórki i sąsiednie chunki potrzebne do przebudowy ścian.
Projekcja nie zawiera operacji przedmiotów; `OperationTerrain`, `OperationProps` i powtórka nadal widzą swoje części.

Checkpoint zapisuje końcowe chunki, usunięte budowle, kontenery, postęp i trwałe przedmioty leżące.
Podparcie i oznaczenia niestabilności nie są blobami; po restarcie podparcie się przelicza.
Skrót obejmuje zapisane skutki i rewizje w `BlockState`; nie obejmuje cache podparcia.
Stare bloby i fikstury muszą się wczytywać bez migracji K6-36.

HUD pokazuje koszt „masz/potrzeba”, rewizję kosztu, brakujące składniki i podparcie wskazanego bloku (kolor + liczba/tekst).
Lokalna prognoza (plecak, pasek, `IntegrityEvaluator` na danych lustra) jest pomocnicza; ilości ze skrzyń i ostateczny podgląd pochodzą z odpowiedzi serwera.
Odmowa 55 otwiera panel liczby zawaleń i podświetla ich komórki; anulowanie niczego nie wysyła.
Potwierdzenie ponawia dokładnie tę samą akcję z `CollapseDigest`; zmiana celu zamyka stary panel.
Solo i host korzystają z tej samej logiki odpowiedzi, bez oczekiwania na pomijaną replikację lokalnego gracza.

## Testy akceptacyjne — napisać przed kodem

W tabelach `Core.Tests` oznacza `tests/Horyzont.Core.Tests`; analogicznie dla pozostałych projektów (wszystkie istnieją w `tests/`).
Każdy wymieniony scenariusz ma osobny test lub jawny przypadek teorii z angielską nazwą.
Testy używają fake czasu, terenu, transportu i jawnych danych; żadnych okien gry. Żaden test nie przechodzi pusto: listy i zbiory sprawdzane na niepustość, właściwości mają progi pokrycia.

### Etap A — Core, bez Godota

| Plik testów | Nazwy i wymagany wynik |
| --- | --- |
| `Building/BuildCostTests.cs` | `Place_UsesCurrentRevision_RecordsIt`, `Refund_UsesRecordedRevision_AfterContentUpdate`, `LegacyRevisionZero_RefundsNothing`, `FreeBuilding_NeitherConsumesNorRefunds` |
| `Building/BuildingSourcesTests.cs` | `Sources_BagThenHotbarThenDistanceAndId`, `ChestAt20Meters_Included`, `ChestBeyond20Meters_Excluded`, `OwnPrivateChest_Included_ForeignPrivateExcluded`, `StationTombstoneDroppedContainers_Excluded`, `MissingOneItem_ChangesNothing`, `SameOrderAsCraftingSources` |
| `Building/IntegrityEvaluatorTests.cs` | `Grounded_UsesMaterialMaximum`, `SdfAtMinusQuarter_Grounded`, `SdfAboveMinusQuarter_NotGrounded`, `BottomFace_UsesConstructRotation` |
| `Building/IntegrityMaterialTests.cs` | `Wood_HorizontalReach7`, `CoreWood_HorizontalReach13`, `Stone_HorizontalReach2`, `Metal_HorizontalReach37`, `MixedMaterials_UseSupportedBlockLoss`, `Relaxed_OddLoss_RoundsUp` |
| `Building/IntegrityEvaluatorTests.cs` | `FloatingCycle_HasZeroSupport`, `BestOfTwoSupports_Wins`, `RemoveSupport_PropagatesAcrossChunks`, `Cascade_ReachesStableRemainder`, `Off_SkipsSupport` |
| `Building/IntegrityProperties.cs` | `InputPermutation_SameResult`, `Incremental_EqualsFullEvaluation`, `RemovingGrounding_NeverIncreasesSupport`; losowe materiały, granice chunków i odłączone wyspy; progi: ≥ 100 przypadków z kaskadą, ≥ 50 przez granicę chunku |
| `Building/ShipConnectivityTests.cs` | `BridgeRemoval_WouldSplitShip`, `LeafRemoval_Accepted`, `Off_StillChecksShip`, `ShipModeSet_OnSplitStatic_WouldSplitShip`, `BuilderShip_DoesNotNeedFlightRuntime` |
| `Building/CollapseRulesTests.cs` | `TenCollapsed_NoConfirmation`, `ElevenCollapsed_RequiresConfirmation`, `PreviouslyUnstableComponent_RequiresConfirmation`, `NewUnsupportedBlock_Rejected`, `LastBlockOfFrameWithChildren_Refused` |
| `Building/CollapseDigestTests.cs` | `SameSet_SameDigest`, `ChangedConstructVersion_ChangesDigest`, `ChangedCollapseSet_ChangesDigest`, `DifferentAction_ChangesDigest`, `WrongDigest_RefusedWithFreshPreview` |
| `Items/CompoundBuildingTests.cs` | `V1_MultipleConstructEdits_StillRejected`, `V2_MultipleClears_Atomic`, `V2_FailedComponent_RestoresWorldAndTrackers`, `V2_LastBlockRemoval_RollbackRestoresConstruct`, `V2_TerrainIsLast`, `V2_FunctionStateWithEditOfSameConstruct_Rejected` |
| `Building/BuildingConservationProperties.cs` | `RandomBuildDemolishCollapse_ConservesItems`: 10 000 sekwencji, do 8 graczy; kontrola po każdej operacji i odmowie |

Test właściwości obejmuje co najmniej 100 udanych budów, 100 zwrotów, 20 kaskad, 20 zwrotów z nadmiarem jako przedmiot leżący i 20 odmów braku kosztu.
Bilans liczy osobno `ItemCreateOp(Refund)`, `ItemDestroyOp(BuildCost)` i transfery zawartości skrzyń.
Nie wystarcza sprawdzenie końcowej sumy po całej sekwencji.

### Etap B — protokół i treść

| Plik testów | Nazwy i wymagany wynik |
| --- | --- |
| `Serialization/OperationCodecBuildingTests.cs` | `CompoundV1_GoldenBytesUnchanged`, `CompoundV2_RoundTripPreservesVersion`, `UnknownCompoundVersion_Throws`, `NestedCompound_Throws` |
| `Serialization/IntentCodecBuildingTests.cs` | `TerrainAndRemove_V1_DefaultWithoutConfirmation`, `TerrainAndRemove_V2_RoundTrip`, `Inspect_RoundTrip`, `InvalidBooleanOrDigestWithoutConfirm_Throws`, `FutureVersion_Throws` |
| `Serialization/MessageCodecBuildingTests.cs` | `IntentReply_BuildingTail_RoundTrip`, `ReplyWithoutBuildingData_WritesZeroFlag`, `PreviewOverLimit_Throws`, `MalformedCountsOrDuplicateCells_Throws` |
| `Protocol/ProtocolContractTests.cs` | `ProtocolVersion_MatchesDocumentedMergeValue` = 21; aktualizacja złotych enumów wyłącznie o 34 i 51/52/55/58 |
| `Constructs.Tests/Content/BuildContentTests.cs` | `Version2_LoadsWithoutBuilding`, `Version3_RequiresEveryPlaceableDefinition`, `SeventeenthRevision_Throws`, `UnknownIngredient_Throws`, `HistoricalRevisionChanged_Throws`, `PaidRevisionEmpty_Throws`, `OxygenGeneratorAndStations_HaveCost` |
| `Simulation/BuildingIntentTests.cs` | `FunctionalAndOrdinaryPlacement_ChargeExactlyOnce`, `StationPlacement_ChargesOnce`, `InsufficientCost_LeavesFunctionalStateAbsent`, `DeadPlayer_RefusedInTransition` |
| `Simulation/BuildingTokenTests.cs` | `ExpensiveRefusals_ConsumeToken`, `CheapValidatorRefusals_DoNotConsume`, `RefusalCostFlag_DoesNotLeakBetweenResolvers`, `InspectFlood_IsLimited` |
| `Core.Tests/Building/BuildingLimitsTests.cs` | `Components4096_Accepted`, `Components4097_Refused`, `EncodedBytesAtLimit_Accepted`, `EncodedBytesOverLimit_Refused`, `Constructs1025_Refused`; brak kosztu i zmian przy odmowie |
| `Net.Tests/MergedProtocolTests.cs` | `Protocol21_Connects`, `Protocol20_ReceivesReadableMismatch`; zamrożony handshake bez zmian |

### Etap C — zapis i serwer

| Plik testów | Nazwy i wymagany wynik |
| --- | --- |
| `Core.Tests/Checkpoints/BuildingCheckpointTests.cs` | `Capture_CascadeMarksEveryConstructAndContainer`, `CheckpointPlusTail_SameHash`, `RolledBackV2_TrackerMatchesState` |
| `Persistence.Tests/BuildingPersistenceTests.cs` | `Restart_PreservesCostRevisionsAndDrops`, `OldWorld_RevisionZero_NoRefund`, `CrashDuringCascade_AllOrNothing`, `V1Tail_ReplaysUnchanged`, `MissingCostHistory_RefusesUnsafeRefund` |
| `Server.Tests/Building/BuildingFlowTests.cs` | `TwoPlayers_SameLastResources_OnlyOneBuilds`, `FullInventory_RefundCreatesPersistentDrop`, `CollapsedChest_SpillsContentsByTransfers`, `CollapsedBed_ClearsRespawnKeepsOtherProgress`, `DroppedRefund_AnyPlayerCanTakeAll` |
| `Integration.Tests/BuildingFlowTests.cs` | `DigUnderTwoConstructs_OneJournalEntry`, `Dig_PreservesYieldPropsAndCollapse`, `RejectedCollapse_TerrainAndYieldLedgerUnchanged`, `RestartAfterCascade_SameHashAndItems` |
| `Server.Tests/Building/BuildingAuthorityTests.cs` | `Inspect_DoesNotExposePrivateContainers`, `ReadOnlyMode_RejectsBuild`, `SubscribersOfCollapsedChest_GetRemoval` |
| `game/tests/Terrain/IntegrityProbeTests.cs` | `Preview_EqualsSamplesAfterActualEdit`, `Preview_DoesNotMutateTerrain`, `UnloadedNeighborhood_Unknown`; realny adapter, różne rozdzielczości i granice bloków |
| `Core.Tests/Building/IntegrityPerformanceTests.cs` | `Full64Cube_Within50Milliseconds`, `SingleEdit_WithinTickBudget`, `WarmIncrementalEvaluation_NoAllocations`; kategoria `Slow`, pomiar Release na maszynie referencyjnej |

Test awarii obejmuje punkty przed zapisem, po zapisie dziennika i przed checkpointem; po otwarciu świat ma całość albo nic.
Budżet pojedynczej zmiany i maszyna referencyjna wymagają ustalenia; propozycja: p95 ≤ 4 ms po rozgrzaniu.
Pomiary czasu nie są testami logiki; w PR podać sprzęt, konfigurację Release, rozmiar i rozkład wyników.

### Etap D — klient

| Plik testów | Nazwy i wymagany wynik |
| --- | --- |
| `Client.Tests/Building/BuildPreviewTests.cs` | `Cost_ShowsOwnedAndRequired`, `UnknownChestCounts_AreNotInvented`, `Off_ShowsSupportDisabled`, `LocalSupport_MatchesServerEvaluatorOnSameData` |
| `Client.Tests/Building/CollapseConfirmationTests.cs` | `Reject55_OpensPreview`, `Cancel_SendsNothing`, `Confirm_ResendsSameActionAndDigest`, `TargetChanged_DiscardsDigest`, `NewPreview_ReplacesOld` |
| `Client.Tests/Replication/ConstructMirrorBuildingTests.cs` | `TwoConstructCascade_AppliesAll`, `Duplicate_Ignored`, `VersionGap_RequestsResync`, `InvalidSecondConstruct_DoesNotPartiallyApplyFirst` |
| `Constructs.Tests/Sources/ConstructDirtyChunksTests.cs` | `Cascade_MarksAllCellsAndChunkNeighbors`, `Projection_DropsItemOperations`, `Projection_IsIdempotent` |
| `Client.Tests/Replication/DroppedItemPresentationTests.cs` | `Spawn_Kind3_ArchetypeAndCount`, `LastTake_Despawns` |
| `game/tests/Building/BuildingUiTests.cs` | `CostsAndSupport_Translated`, `Confirmation_HasKeyboardCancelAndConfirm`, `Host_ReceivesBuildingFeedback`; gdUnit bez okna |
| Ręcznie — właściciel | Ocena kosztów, kolorów i tekstów podparcia, podglądu 11 zawaleń, anulowania, przedmiotów leżących i zachowania po restarcie |

## Kroki

Każdy krok implementacyjny: test czerwony → minimalny kod → test zielony → mały Conventional Commit.
Nie zmieniać zatwierdzonych oczekiwań testów w celu uzyskania zieleni; rozbieżność przedstawić w przeglądzie.

### Etap A — Core

- [ ] **A0. Ustalić bazę.** Pliki: wymienione zależności i kontrakty; zanotować commit bazowy, obecność kontraktów K6-35 (38/39, blob 24, `WorldSettings`, bramka 48, tabela wersji operacji) i wartość `ProtocolVersion`.
  Test kontrolny: `V1Tail_ReplaysUnchanged`; komenda odczytu: `rg -n 'PersistentEntityCreate|PersistentEntityRemove|WorldSettings|InTransition|ProtocolVersion' src`.
- [ ] **A0b. Spike podglądu terenu (bez commitu do `main`, raport w PR).** Na adapterze Voxel Tools sprawdzić, że kopia wokseli + ta sama operacja `Remove` daje identyczne próbki SDF w środkach dolnych ścian co prawdziwa edycja.
  Wynik negatywny → zatrzymać C2 i przedstawić Opusowi rozbieżność przed A1 (to największe ryzyko pakietu).
- [ ] **A1. Napisać testy A i szkielety. [Opus]** Pliki: wszystkie testy etapu A, nowe typy `Building/`.
  Test: `InputPermutation_SameResult`; komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~Building"`.
  Przegląd przed implementacją: D3, D8, D11, D13, D16, sposób cofania budowli i wynik A0b; zapisać rozstrzygnięcia.
- [ ] **A2. Koszty i źródła.** Pliki: `BuildCostDef`, `BuildCostRevision`, `BuildingSources`, wydzielony pomocnik źródeł (bez zmiany wyników `CraftingRules`).
  Test: `MissingOneItem_ChangesNothing`, `SameOrderAsCraftingSources`; komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~BuildCost|FullyQualifiedName~BuildingSources|FullyQualifiedName~Craft"`.
- [ ] **A3. Podparcie i spójność.** Pliki: `IntegrityEvaluator`, `IntegrityWorkspace`, `ShipConnectivity`.
  Test: `Incremental_EqualsFullEvaluation`; komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~Integrity|FullyQualifiedName~ShipConnectivity"`.
- [ ] **A4. Kaskady i potwierdzenie.** Pliki: czyste reguły kaskady, `CollapseDigest`.
  Test: `WrongDigest_RefusedWithFreshPreview`; komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~CollapseRules|FullyQualifiedName~CollapseDigest"`.
- [ ] **A5. Atomowość v2.** Pliki: `CompoundOp`, `OperationApplier`, `UndoLog`, odpowiednie mutatory budowli i trackera.
  Test: `V2_LastBlockRemoval_RollbackRestoresConstruct`; komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~CompoundBuilding"`.
- [ ] **A6. Przegląd Core i mutacje. [Opus]** Pliki: cały diff A; testy `BuildingConservationProperties`, `IntegrityProperties`.
  Komenda: `dotnet test tests/Horyzont.Core.Tests`; mutacje: strata z materiału sąsiada, próg `< −0,25`, brak cofnięcia wersji, pominięcie wersji budowli w skrócie — każda musi dać czerwień.

### Etap B — protokół i treść

- [ ] **B1. Testy B i kontrakty. [Opus]** Pliki: testy kodeków, treści, intencji i żetonów; tabela kosztów wszystkich bloków.
  Test: `CompoundV1_GoldenBytesUnchanged`; komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~Building"`.
  Zatwierdzić numery, format v2, ogon odpowiedzi, rewizję 0 oraz DO USTALENIA dotyczące treści i źródeł.
- [ ] **B2. Kodeki.** Pliki: `OperationCodec`, `IntentCodec` (tabela wersji per kod), `MessageCodec`, `ProtocolConstants`, generatory testów i fixture protokołu.
  Test: `CompoundV2_RoundTripPreservesVersion`; komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~Codec|FullyQualifiedName~ProtocolContract"`.
- [ ] **B3. JSON i walidacja.** Pliki: `BlockTypeDef`, `BlockTypeCatalog`, loadery, `blocks.json`, testy historii bazowej.
  Test: `HistoricalRevisionChanged_Throws`; komenda: `dotnet test tests/Horyzont.Constructs.Tests --filter "FullyQualifiedName~Content"`.
- [ ] **B4. Resolver i żetony.** Pliki: `BuildingRules`, `BuildingPlan`, `BuildResolution`, `IntentValidator`, `ServerSimulation`, integracja resolverów funkcji, stacji i generatora.
  Test: `FunctionalAndOrdinaryPlacement_ChargeExactlyOnce`, `ExpensiveRefusals_ConsumeToken`; komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~BuildingIntent|FullyQualifiedName~BuildingToken"`.
- [ ] **B5. Limity i połączenie.** Pliki: `BuildingLimitsTests`, `MergedProtocolTests`, `GameServerHooks`, `IntentResult`.
  Test: `EncodedBytesOverLimit_Refused`; komendy: `dotnet test tests/Horyzont.Core.Tests` oraz `dotnet test tests/Horyzont.Net.Tests`.
- [ ] **B6. Przegląd całego Core A+B. [Opus]** Pliki: diff od A0 i dokumentacja wersji.
  Test: `V1_MultipleConstructEdits_StillRejected`; komenda: `dotnet test tests/Horyzont.Core.Tests`; poprawki przed C.

### Etap C — zapis i serwer

- [ ] **C1. Testy C i gotowość zależności.** Pliki: testy checkpointów, Persistence, Server i Integration.
  Test: `FullInventory_RefundCreatesPersistentDrop`; komenda: `dotnet test tests/Horyzont.Server.Tests --filter "FullyQualifiedName~Building"`.
  Bez scalonego K6-35 zapisać blokadę; nie zastępować przedmiotów leżących gubieniem nadmiaru.
- [ ] **C2. Próbki i podgląd terenu.** Pliki: `ITerrainIntegrityProbe`, adapter Godota, fake, integracja `BuildingRules`.
  Test: `Preview_EqualsSamplesAfterActualEdit`; komendy: `dotnet test tests/Horyzont.Core.Tests` i bezokienkowe `dotnet test game`.
- [ ] **C3. Kaskada i trwałe skutki.** Pliki: koordynator budowania, wysypanie kontenerów transferami, przedmioty leżące (D17), tracker, checkpoint, ewentualne poprawki Persistence.
  Test: `DigUnderTwoConstructs_OneJournalEntry`; komenda: `dotnet test tests/Horyzont.Integration.Tests --filter "FullyQualifiedName~BuildingFlow"`.
- [ ] **C4. Odtwarzanie i awarie.** Pliki: testy Persistence, nowa fikstura świata z budowaniem według schematu 3.
  Test: `CrashDuringCascade_AllOrNothing`; komenda: `dotnet test tests/Horyzont.Persistence.Tests`; bez nowej migracji K6-36.
- [ ] **C5. Serwer i wydajność.** Pliki: composition root, limity, indeks przestrzenny, testy wydajności i alokacji.
  Test: `Full64Cube_Within50Milliseconds`; komenda: `dotnet test tests/Horyzont.Core.Tests -c Release --filter "FullyQualifiedName~IntegrityPerformance"`.
- [ ] **C6. Przegląd integracji. [Opus]** Pliki: diff C, raport trwałości i wydajności.
  Test: `RestartAfterCascade_SameHashAndItems`; komenda: `dotnet test tests/Horyzont.Integration.Tests`.

### Etap D — klient

- [ ] **D1. Testy D przed kodem.** Pliki: testy klienta, projekcji, dirty chunks, prezentacji rodzaju 3 i gdUnit.
  Test: `InvalidSecondConstruct_DoesNotPartiallyApplyFirst`; komenda: `dotnet test tests/Horyzont.Client.Tests`.
- [ ] **D2. Replikacja.** Pliki: `OperationConstructs`, `EntityReplication`, `ConstructReplica`, `ConstructMirror`, `ConstructDirtyChunks`, prezentacja rodzaju 3.
  Test: `TwoConstructCascade_AppliesAll`; komendy: `dotnet test tests/Horyzont.Client.Tests` i `dotnet test tests/Horyzont.Constructs.Tests`.
- [ ] **D3. Model interakcji.** Pliki: `Client/Building/`, `ToolController`, obsługa odpowiedzi i lokalnego hosta.
  Test: `Confirm_ResendsSameActionAndDigest`; komenda: `dotnet test tests/Horyzont.Client.Tests --filter "FullyQualifiedName~Building"`.
- [ ] **D4. HUD i teksty.** Pliki: `ToolHud`, `BlockGhostPreview`, panel potwierdzenia, CSV PL/EN i composition root.
  Test: `CostsAndSupport_Translated`; komenda: bezokienkowe `dotnet test game`; wykonawca nie uruchamia gry z oknem.
- [ ] **D5. Odbiór i dokumentacja.** Pliki: raport PR, ten plan, `docs/ZASADY.md` (lista odwracalnych składowych v2, zasada D16 o żetonach).
  Test: `Host_ReceivesBuildingFeedback`; komenda: pełne sprawdzenie poniżej; wygląd i scenariusz ręczny wykonuje właściciel; końcowy przegląd Opus.

### Sprawdzenie na końcu etapu

Poniższe komendy dotyczą przyszłej implementacji; podczas przygotowania planu nie były uruchamiane.

```powershell
dotnet build Horyzont.sln
dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"
dotnet format Horyzont.sln --verify-no-changes
python tools/engine/build.py smoke
```

W C i D dodatkowo `dotnet test game` bez okna, po sprawdzeniu dostępności wspólnego runnera.
Po każdym etapie porównać z commitem A0 zamrożony korpus terenu i `Hashing/DeterminismTests.cs`: diff pusty.
Sprawdzić brak nieplanowanych zmian migracji, generatorów, `Space/`, orientacji i semantyki edycji terenu.
Wyniki, liczbę testów, pomiary i niewykonane sprawdzenia wpisać do PR; nie deklarować zieleni bez przebiegu.

## Koordynacja

| Pakiet | Zarezerwowane kontrakty i przekazanie |
| --- | --- |
| K6-29 („reszta”) | Podnoszenie 2 m i wygaszanie przedmiotów leżących; brak pakietu w łańcuchu (DO USTALENIA-8) |
| K6-30 | Operacje 36/37/40/44/49, intencje 22/23/28/29, komunikat 46, bloby 21/22 |
| K6-31 | Operacje 47/48, intencja 27, odmowa 53, komunikat 45, tag `0x48`; zachować tiery, propsy i urobek |
| K6-32 | Operacje 43/45, intencja 24, odmowy 44–46, komunikat 48, blob 25, tagi `0x44`/`0x4B`; objąć stacje kosztem |
| K6-33 | Operacja 41, intencja 25, odmowa 49, komunikat 44, tag `0x46`, protokół 18 |
| K6-34 | Operacja 42, intencja 26, stan bloku 5, `BlockFunctionKind` 16, protokół 19; generator dostaje koszt |
| K6-35 | Operacje 38/39/46, bloby 23/24, komunikat 47, odmowa 48, rodzaje bytów 3/6, tagi `0x54`/`0x57`, tabela wersji operacji, protokół 20; bramka martwych graczy; ogólna ścieżka kontenerów bytu |
| K6-36 | Intencja 34, odmowy 51/52/55/58, `CompoundOp` v2, intencje 1/3 v2, ogon `IntentReply`, protokół 21 |
| K6-39 | Ekran ustawień nad polami `WorldSettings` (tryb podparcia, budowanie bez kosztów) i docelowy podgląd budowy; nie zmieniać znaczenia rewizji 0 |
| K9-61 | Zachować tekstowe odpowiedniki kolorów, sterowanie panelem, lokalizację i możliwość przypisania akcji |

Przed implementacją i przed scaleniem ponowić wąski grep numerów, bo inne worktree mogą wprowadzić nowe rezerwacje.
Nie zmieniać cudzych gałęzi ani nie „naprawiać” brakujących zależności w zakresie tego pakietu.

## Ryzyka

| Ryzyko | Zabezpieczenie |
| --- | --- |
| Darmowe zasoby ze starych bloków | Pusta rewizja 0 i test starego świata |
| Cofnięcie kaskady pozostawia usuniętą ramę | Snapshot nagłówka, chunków, indeksów i wersji; test rollbacku ostatniego bloku; odmowa przy dzieciach ramy |
| Podgląd wykopu różni się od Voxel Tools | Spike A0b i test porównujący próbki z rzeczywistą edycją |
| Zawalenie gubi skrzynię lub proces stacji | Jedna operacja, rozliczenie procesu i transfer zawartości przed usunięciem |
| Klient widzi tylko pierwszy blok kaskady | Projekcja wielu zmian, atomowa walidacja i test dwóch budowli |
| Aktualizacja treści zawala bazę przy starcie | Wyłącznie oznaczanie; potwierdzenie przy następnej ingerencji |
| Zalew kosztownych odmów | D16: żeton za odmowę po ocenie; wynik wywołania zamiast flagi |
| Pełne skanowanie dużej bazy blokuje tick | Cache, indeks przestrzenny, przyrostowa propagacja i pomiar Release |
| Zakres za duży dla jednego wykonawcy | HP usunięte (D12); etap D może zostać okrojony do minimum HUD po decyzji B6 |

## Odbiór

- [ ] Decyzje rewizji 0, `CompoundOp` v2, `CollapseDigest` i żetonów zatwierdzone przez Opus.
- [ ] Testy A–D zielone; historia kosztów zachowuje zwrot po aktualizacji treści.
- [ ] Kaskada wykopu, przedmioty leżące i zawartość skrzyń przetrwały test awarii i restartu.
- [ ] Potwierdzenie działa dla >10 bloków i istniejących niestabilnych obszarów.
- [ ] `Off` nie pozwala rozdzielić statku; dotychczasowe ograniczenia pasażerów pozostają skuteczne.
- [ ] Budowla 64³ oceniana do 50 ms na uzgodnionym sprzęcie; wynik i alokacje opisane w PR.
- [ ] Stare dzienniki i bloby działają; korpus i stare złote skróty niezmienione.
- [ ] Właściciel zaakceptował wygląd i scenariusz ręczny; wykonawca nie uruchamiał gry z oknem.
- [ ] Końcowy przegląd Opus zakończony; brak nieopisanych odstępstw i `TODO` bez zgłoszenia.

## DO USTALENIA — z propozycjami

1. **HP bloków i naprawa.** Spec ich nie przewiduje. Propozycja: poza K6-36 (D12); ewentualnie osobny pakiet razem z najazdami.
2. **Koszty wszystkich bloków.** Propozycja: osobna tabela w B1, oparta na istniejących surowcach i recepturach; nie zgadywać materiałów niedostępnych w treści.
3. **Automatyczne źródła z ładowni.** Propozycja: początkowo tylko skrzynie, zgodnie z precedensem `CraftingRules`; rozszerzenie o ładownie wymaga testów ruchomych ram i dostępu.
4. **Zwrot z zawalenia: jeden byt na rodzaj przedmiotu.** Propozycja: na każdą budowlę i rodzaj przedmiotu w jednym zawaleniu jeden byt leżący (w środku najniższego zawalonego bloku tego rodzaju), zamiast bytu na każdy blok; oszczędza składowe (limit 4096) i jest zgodne z dosłownym brzmieniem spec l. 102.
5. **Przewodzenie podparcia przez drzwi i siatki.** Propozycja: każdy zajęty blok, niezależnie od stanu drzwi i kolizji.
6. **Darmowy tryb a wcześniej opłacone bloki.** Propozycja zgodna ze spec: w trakcie darmowego trybu brak zwrotu również za stare bloki; nie zmieniać ich zapisanej rewizji.
7. **Zawalenie usuwające budowlę z dziećmi ramy.** Propozycja: odmowa akcji (`NoSupport`) z tekstem „konstrukcja niesie inne obiekty”; alternatywa — pozostawienie ostatniego bloku (sprzeczne z regułą podparcia).
8. **Podnoszenie i wygaszanie przedmiotów leżących.** Propozycja: osobny mały pakiet „reszta K6-29” przed K6-40; do tego czasu ręczne odzyskiwanie.
9. **Sprzęt i budżet zmiany lokalnej.** Propozycja: 50 ms dla pełnego 64³ i p95 4 ms dla pojedynczej zmiany; właściciel wskazuje maszynę referencyjną przed C5.
10. **Źródło ustawień.** Propozycja: `WorldSettings` z K6-35; jeśli właściciel odrzuci DO USTALENIA-1 K6-35 — `GameRules`.

## Pułapki

- `BlockState.CostRevision` już istnieje; nie dodawać nowych bitów ani nie używać bitów funkcji.
- Brak kodu w enumie nie oznacza wolnego numeru: spec rezerwuje wszystkie operacje Przetrwania.
- Nie rozszerzać semantyki `CompoundOp` v1; wersja 2 musi przetrwać dekodowanie, zapis i replay.
- Nie pobierać kosztu osobno od postawienia ani nie zapisywać zawalenia jako późniejszej, niezależnej operacji.
- Nie czytać kosztów ani terenu w applierze; operacja zawiera gotowe skutki i limity stosów.
- Nie traktować `TrySampleSolid` jako odpowiednika progu SDF −0,25.
- Nie przenosić skanowania `Construct.Chunks` z alokacjami do każdego ticku lub każdej klatki HUD.
- Nie otwierać skrzyń i nie ujawniać ich zawartości tylko po to, aby pokazać dostępny koszt.
- Nie przenosić odmowy kosztownej przez współdzieloną flagę obiektu reguł.
- Nie wysypywać zawartości skrzyni parą zniszcz–utwórz.
- Nie kończyć odbioru na testach klienta zdalnego: solo i host wymagają osobnego sprawdzenia.
- Po dwóch nieudanych podejściach do tego samego testu przekazać test, log i opis prób zgodnie z zasadami eskalacji.

## Odstępstwa i uwagi z realizacji

Wypełnia wykonawca: commit A0, wynik spike A0b, rzeczywista baza zależności, rozstrzygnięcia A1/B1, wyniki etapów i końcowego przeglądu.
Zapisać każdą zmianę istniejących testów, różnicę względem spec, wyniki pomiarów oraz oczekujące sprawdzenia właściciela.
Nie oznaczać propozycji DO USTALENIA jako przyjętych bez odnotowanego rozstrzygnięcia.

## Przegląd planu (Opus 5.5)

Data: 2026-10-16. Podstawa: plan Codexa (gpt-6.1-sol), worktree `k6-34` (tylko odczyt, bez gita, bez budowania i testów), poprawiony plan K6-35. Werdykt: **GO z poprawkami** (17 poprawek; P1–P4 zmieniają zakres lub kontrakt).

| # | Co zmieniono | Dlaczego |
| --- | --- | --- |
| P1 | D12, Zakres, testy, kroki: usunięte HP bloków, `BlockHitIntent`/`BlockRepairIntent`, `BlockDamageLedger`; inspekcja dostaje numer 34 | Spec 03 (l. 95–114) i plan projektu (l. 137) nie przewidują HP; „wytrzymałość” = podparcie. Rozszerzenie spec bez decyzji właściciela, duży dodatkowy zakres dla wykonawcy |
| P2 | D0, Kontekst, Koordynacja, C1: baza = scalone K6-35, które dostarcza 38/39, blob 24, gospodarza bytu i ścieżkę odzyskiwania | Plan opierał się na worktree `k6-32` (PV 17) i blokował etap C brakiem K6-29; w liniowym łańcuchu robi to K6-35 |
| P3 | D16, testy `BuildingTokenTests`: odmowy kosztowne pobierają żeton, informacja w wyniku wywołania | Lekcja K6-30/K6-31: kosztowna odmowa bez żetonu = zalew; `ServerSimulation` l. 199 używa współdzielonej flagi `_functional.RefusalCostsToken`, której nie wolno powielać |
| P4 | D13: bilet z magazynem, wygasaniem i limitem → bezstanowy `CollapseDigest` | Brak stanu współdzielonego i wygasania; to samo bezpieczeństwo (potwierdzenie dokładnie tego zbioru), mniej kodu i działa po ponownym połączeniu |
| P5 | D5, test `OwnPrivateChest_Included_ForeignPrivateExcluded`: źródła jak `CraftingRules.BuildSources` (l. 340: `Shared` albo właściciel) | Plan wykluczał własne prywatne skrzynie, wbrew własnemu zaleceniu „bez zmiany zakresu źródeł” |
| P6 | D4: tryb podparcia i budowanie bez kosztów z `WorldSettings` K6-35 | Spec l. 422: każde ustawienie przetrwa restart; `GameRules` tego nie spełnia |
| P7 | D11: v2 = edycje budowli odwracalne, teren jedyny nieodwracalny ostatni; zakaz stanu funkcji z edycją tej samej budowli; limit 1024 budowli | Zgodność z ZASADY l. 29 i applierem l. 265; `MaxConstructChangesPerOperation = 1024` (l. 82) nie było w planie |
| P8 | D6, D17, C3: zwroty i wysypanie jako przedmioty leżące (38+35+33/31), rama planety, dostęp wspólny, prezentacja rodzaju 3 | Spec l. 102, 344, 381; bez tego nadmiar przepadałby albo blokował pakiet |
| P9 | D11, test `LastBlockOfFrameWithChildren_Refused`, DO USTALENIA-7 | `IntentValidator` l. 462–464 i applier odmawiają usunięcia ostatniego bloku ramy z dziećmi; kaskada musi to przewidzieć |
| P10 | D10, test `ShipModeSet_OnSplitStatic_WouldSplitShip` zamiast `DisconnectedPlacement_Rejected` | Postawienie na ścianie zawsze styka się z blokiem — stary test przechodziłby pusto |
| P11 | Projekt terenu: podgląd tylko dla `Remove`, krok A0b (spike) przed A1 | Dodanie terenu nie zmniejsza uziemienia; podgląd Voxel Tools to największe ryzyko, sprawdzone przed kontraktami |
| P12 | D2, B2: tabela wersji per kod w `IntentCodec` (i wspólny mechanizm z K6-35 dla operacji) | Kodeki mają dziś jedną stałą wersji (`OperationCodec` l. 408, `IntentCodec` l. 15) |
| P13 | Dane treści, test `OxygenGeneratorAndStations_HaveCost` | Plan K6-34 l. 143 odkłada koszt generatora do K6-36 |
| P14 | Przeglądy: C6 zawsze Opus | ZASADY §9: pakiet GPT-6.1 Sol → przegląd Opus |
| P15 | Walidacja: bramka martwych/w przejściu (48) z K6-35, test `DeadPlayer_RefusedInTransition` | Spec l. 297 |
| P16 | Test wydajności w `Core.Tests` (typ jest w Core); progi pokrycia w właściwościach; `RolledBackV2_TrackerMatchesState` | Lekcja: testy przechodzące pusto; tracker po wycofaniu |
| P17 | Kontekst: fakty z numerami linii, stan `k6-34`, PV 18 | Plan cytował nieaktualny worktree `k6-32` |

Sprawdzone bez uwag: intencja 34 i odmowy 51/52/55/58 wolne i w zakresie Przetrwania (K1-03 l. 618–621); bity kosztu 21–24 (`BlockState` l. 33–34); `ConstructMirror` l. 150 odrzuca wiele zmian; `TrySampleSolid` zwraca tylko znak; limity 4096/160 KiB; `IntentReply` bez miejsca na podgląd; tabela materiałów zgodna ze spec l. 105–110.
**Niesprawdzone:** położenie plików CSV tekstów; walidator `ShipModeSet` (K5-23) jako miejsce odmowy 52; czy statyczne budowle bywają ramą graczy (wpływa na częstość P9); wartość `CraftContainerRangeMeters` (zakładane 20 m).

**DO USTALENIA dla właściciela:** (1) czy HP bloków w ogóle w EA (propozycja: nie w K6-36); (4) jeden byt leżący na rodzaj przedmiotu i budowlę w zawaleniu; (7) odmowa zawalenia budowli niosącej inne obiekty; (8) kto robi podnoszenie 2 m i wygaszanie przedmiotów leżących; (9) maszyna referencyjna i budżet; (10) źródło ustawień zależne od decyzji w K6-35.
