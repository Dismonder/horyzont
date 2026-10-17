# K6-37 · Teleporty i logistyka

| | |
| --- | --- |
| Prowadzi | Sonnet 5.5 (`docs/plan/plan-projektu.md` l. 138) |
| Testy akceptacyjne | Sonnet 5.5, przed implementacją; kontrakty i czerwone testy etapów A i B zatwierdza Opus 5.5 |
| Przegląd | Opus 5.5: A1, A6, B1, B6, C1, C7 i końcowy PR (ZASADY §3, §9); wygląd ocenia właściciel |
| Zależy od | K6-30, K6-34, K5-22, K5-25 (tabela projektu); baza realizacji = scalone K6-35 i K6-36 (D1) |
| Gałąź | `pakiet/k6-37-teleporty-i-logistyka`, w osobnym worktree |
| Docelowy plik planu | `docs/plans/K6-37-teleporty-i-logistyka.md` |
| Protokół | K6-34 = 19, K6-35 = 20, K6-36 = 21, **K6-37 = 22** (`main` + 1 w chwili scalenia; D2) |
| Kamień milowy | K6 Przetrwanie |
| Odbiór ze specyfikacji | Testy teleportów; tankowanie zużywa ogniwa, a darmowe paliwo je pomija (spec 03 l. 439) |

**Cel:** teleporty łączą się w pary po nazwie, także między planetami, a serwer przenosi postać dopiero po potwierdzeniu kolizji celu (najwyżej 60 s, potem anulowanie przy źródle).
Przedmioty masowe blokują teleport, chyba że świat ma `TeleportAll`. Bloki ładowni dostają kontener `CargoHold` (32/64/96 miejsc), tankowanie zużywa ogniwa atomowo w jednym wpisie dziennika, a podtrzymanie życia działa na rzeczywistych fotelach statku.
Wykonawca nie uruchamia gry z oknem. Plan przygotowano wyłącznie przez odczyt repozytorium; nie budowano ani nie testowano.

## Uzgodnienie numerów (Opus, 2026-10-17, `K6-37-40-reconciliation.md`)

- Kolejność scalania: K6-34 (19) → K6-35 (20) → K6-36 (21) → **K6-37 (22)** → K6-38 (23) → K6-39 (24) → K6-40 (bez PV). Przy innej kolejności: PV = `main` + 1, `BlockFunctionKind` = następna wolna w chwili scalenia.
- K6-37: intencja 30, odmowa 47 (używa 48 z K6-35), rodzaje stanu 4/7, `BlockFunctionKind.Teleport = 17` (K6-38 = 18). Bez kolizji z 26/31–34/50–58.
- **Komunikat 48 (zmiana):** `MessageCodec` ma jedną wersję formatu (`CurrentFormatVersion = 1`, odrzuca inne) — bez bajtu wersji per komunikat. Zmiana układu 48 jest pokryta wyłącznie przez PV 22 (D20).
- **Przygotowanie celu (zmiana):** rozszerzenie współdzielonego komponentu K6-35, nie drugi adapter; jeden budżet na tick (D10).
- **ZASADY l. 29 (zmiana):** dopisać tylko op 21 do brzmienia po K6-36 (D17).
- Intencja 30 pisze i czyta format 1 jawnie, niezależnie od formatu 2 intencji 1/3 z K6-36.

## Kontekst i stan weryfikacji

Źródła: `AGENTS.md`, `docs/ZASADY.md`, `docs/spec/03-przetrwanie.md` (l. 155–160, 172–183, 224, 230, 250–300, 302–319, 325–345, 385, 409, 439), `docs/spec/02-kosmos.md` (l. 66, 80, 248), plany K6-30…K6-36.
Kod odczytano w worktree `k6-35` (`ProtocolVersion = 18`; K6-34 częściowo: op 42 jest, intencja 26 i treść generatora — nie; K6-35/K6-36 tylko jako plany). Plan K6-36 odczytano z poprawionej wersji Opus (scratchpad sesji przeglądu, 2026-10-16), nie z repozytorium.

| Fakt (sprawdzony w kodzie) | Znaczenie |
| --- | --- |
| `OperationKind`: zajęte 1–7, 20–23, 30–37, 40–45, 47–49; wolne w Przetrwaniu tylko 38/39/46, które bierze K6-35 | K6-37 **nie dodaje operacji** |
| `IntentCodec.IntentCode` (l. 20–38): 1–3, 10, 13–15, 20–25, 27–29; 26 = K6-34, 34 = K6-36; spec l. 314: 30 = `TeleportIntent` | Nowa tylko intencja 30 |
| `RejectReason`: 40–46, 49, 53, 54, 57 w kodzie; 48 bierze K6-35, 51/52/55/58 K6-36; spec l. 319: 47 = `CannotTeleportBulk` | K6-37 dodaje 47, używa 48 |
| `MessageType`: 40–46, 48 (`PlayerProgressState`), 49; 47 bierze K6-35 | Brak nowego komunikatu; **48 to `PlayerProgressState`, nie „komunikat przejścia”** (D20) |
| `BlobKind`: 1–5, 10–12, 20–22, 25; 23/24 = K6-35 | Brak nowego bloba |
| `BlockEntityKind.Teleport = 4`, `CargoHold = 7` zarezerwowane; `ContainerKind.CargoHold = 5` istnieje | Uaktywnić warianty 4 i 7 |
| `BlockFunctionKind` kończy się na `OxygenGenerator = 16`; komentarz pliku: „Game content, not a save or wire format” | Nowa funkcja `Teleport = 17` to treść, nie kontrakt sieci (D2) |
| `OperationApplier.IsComponent` (l. 126–129) nie obejmuje `ShipFuelAddOp`; op 21 stosuje ścieżka K1 `Apply` (l. 188) bez dziennika cofania | Tankowanie z przedmiotów wymaga reguły złożonej (D17) |
| `CheckShipFuelAdd` (l. 1521–1534) czyta tylko tryb budowli i wartości; `ApplyShipFuelAdd` obcina do `op.Capacity` | Warunek niezależny od innych składowych → ostatnia nieodwracalna |
| `TryApplyCompound` (l. 240–355): teren/budowla tylko jako ostatnia, warunki ostatniej przed pozostałymi | Wzorzec dla op 21 (D17); ZASADY l. 29 |
| `ValidateRefuel` (`IntentValidator.cs` l. 706–777): stacja, zasięg + LOS, statek zaparkowany w `RefuelRangeMeters = 15`, zbiornik nie pełny (żywe `ShipStates`), fakty = pojemność | Zachować; dodać rozliczenie ogniw w resolverze |
| `IntentResolver` l. 24: `RefuelIntent → new ShipFuelAddOp(ship, cap, cap)` | Dziś darmowe tankowanie do pełna |
| `ServerSimulation.Submit` l. 176–189: `PropHitIntent` przyjęty **bez operacji** (`IntentResult.AcceptedWithoutOperation`, żeton przy przyjęciu) | Wzorzec dla `TeleportIntent` (D9) |
| `Submit` l. 194–206: kosztowna odmowa pobiera żeton przez współdzieloną flagę `refusalCostsToken` | K6-36 D16 zmienia to na wynik wywołania; K6-37 korzysta z jego mechanizmu |
| `CraftingRules.BuildSources` (l. 339–407) i `ContainerStore.HasChests/ChestsInChunk` indeksują **tylko skrzynie** | Ładownia nie jest dziś źródłem; K6-32 l. 79 przekazał „`CargoHold` jako źródło składników” do K6-37 |
| `ContainerReach.TryCellCenter/TryGetEye` liczą w ramie **planety** | Statek w ramie układu: ładowni nie da się otworzyć (D14) |
| `ServerCharacters.FollowReparent` (l. 478–) przez `CharacterFrames.Decide`: ta sama planeta → `Walk` (ciało `Frozen` w nowej pozie), inna → `Respawn` (nowe ciało przez ścieżkę pojawienia); `StepFrozen` czeka na teren i kolizję, po `GroundWaitDeadlineTicks` przechodzi w `Falling` | Po op 7 istnieje gotowa ścieżka osiadania; teleport musi ją poprzedzić przygotowaniem (D10) |
| `ServerPlanetTerrains.Acquire(planet, PlanetLeaseReason)` aktywuje od razu; `GetReadiness` wymaga widza LOD 0; `PlanetLeaseReason` (`Horyzont.Server/Space`) = 0–3 | Nowy powód `Teleport` (enum serwera, nie format) + widz celu |
| `HazardProtection.Apply(..., lifeSupport)` istnieje (K6-34 D14) | K6-37 sprawdza podłączenie na rzeczywistych fotelach, nie pisze drugiej reguły |
| `StateHasher` `0x42` haszuje warianty 1/2/3/5 w `switch` (l. 228–265) | Warianty 4 i 7 dopisać w tej podsekcji |
| Treść: `core:ship_cargo` `{kind: cargo, tier: 1}` (jedyny poziom), `core:ship_fuel_tank` `capacity: 1000`, `core:fuel_cell` (component), `core:iron_ingot` **bez** `bulk` | Poziomy 2/3 ładowni i flaga sztab to treść K6-37 |
| `ItemSink.Fuel = 4`, `ItemFlags.Bulk = 1` istnieją | Bez zmian formatu |
| Plany K6-38 i K6-39 (Codex) **też deklarują `ProtocolVersion = 22`** | Konflikt rozstrzyga D2 |

## Zakres

1. Teleport: blok funkcyjny ze stanem (nazwa, tick nazwania), konfiguracja przez istniejącą intencję 29, deterministyczne parowanie.
2. Przejście: `TeleportIntent` (30), wspólna bramka K6-35, przygotowanie celu (dzierżawa, widz, kolizja, kapsuła), limit 60 s, op 7.
3. Kontrola `Bulk` w plecaku, pasku i wyposażeniu; `TeleportAll` z ustawień świata K6-35.
4. Ładownia: stan bloku 7 + kontener `CargoHold`, poziomy 32/64/96, uzupełnienie istniejących bloków `core:ship_cargo`.
5. Ładownia jako źródło: rzemiosło (przekazane z K6-32) i tankowanie.
6. Tankowanie ogniwami z plecaka i dostępnych kontenerów do 20 m od stacji; darmowe paliwo z ustawień K6-35.
7. Podtrzymanie życia: weryfikacja reguły K6-34 D14 na fotelach K5-25.
8. Kodeki, skrót, zapis, replikacja, minimalny interfejs, teksty PL/EN.

Poza zakresem: śmierć i bramka jako mechanizm (K6-35), koszty budowy i zawalenia (K6-36), ekran ustawień (K6-39), dostęp do ładowni w ramie układu (D14), generator tlenu na statku w przestrzeni i podtrzymanie zależne od paliwa (DO USTALENIA-9), koszt użycia teleportu (D7), dobrowolne anulowanie przejścia (DO USTALENIA-6).

## Decyzje

- **D1. Wykonawca i baza.** Sonnet 5.5; każda zmiana w `src/Horyzont.Core/` przez bramkę Opus.
  Realizacja startuje z bazy zawierającej scalone K6-34, K6-35 (bramka przejścia, `InTransition = 48`, op 7 v2, ustawienia 46/23/47, ogon 48) i K6-36 (`CompoundOp` v2, wynik „odmowa kosztuje żeton”).
  Etap A można zacząć wcześniej na jawnych danych wejściowych; B–D wymagają scalonego K6-35. A0 zapisuje commit bazy i faktycznie dostępne kontrakty. Nie pobierać częściowych zmian z cudzych worktree.

- **D2. Numeracja. [zmienione w przeglądzie]**
  Nowe: intencja 30 `TeleportIntent`, odmowa 47 `CannotTeleportBulk` (obie nazwane w spec), `ProtocolVersion = 22`.
  Uaktywnione: `BlockEntityKind` 4 i 7 w op 40 / blobie 21 / komunikacie 46 (nowe wartości dyskryminatora, istniejące bajty bez zmian; wzorzec K6-32/K6-34).
  Treść i serwer (bez wpływu na format): `BlockFunctionKind.Teleport = 17`, `PlanetLeaseReason.Teleport` = następna wolna wartość.
  Bez nowych operacji, komunikatów, blobów, tagów skrótu, migracji i bez formatu 2 `IntentSubmit`.
  `ProtocolVersion` = `main` + 1 w chwili scalenia: przy kolejności K6-37 → K6-38 → K6-39 wychodzi 22/23/24; plany K6-38 i K6-39 trzeba odpowiednio poprawić (DO USTALENIA-12). K6-38 bierze `BlockFunctionKind` 18.

- **D3. Nazwa.** 1–32 B poprawnego UTF-8 (`MaxTeleportNameBytes = 32`, istniejący kodek `TeleportNameConfig`), porównanie `Ordinal`, bez przycinania i normalizacji.
  Nowy teleport jest nienazwany (`Name = ""`) i nie ma pary. Konfiguracja pustą nazwą → `InvalidParameters`. Nazwę zmienia każdy gracz w zasięgu (jak dziś konfiguracja skrzyni — sprawdzić w A0), DO USTALENIA-2.

- **D4. Trwała kolejność nazwania. [zmienione w przeglądzie]**
  `TeleportBlockState(string Name, Tick NamedTick)`: resolver intencji 29 ustawia `NamedTick = state.CurrentTick`, gdy nazwa się zmienia; ta sama nazwa zachowuje dotychczasowy tick.
  Para nazwy = dwa wpisy o najmniejszym `(NamedTick, BlockRef)`; trzeci i kolejne nie mają pary; usunięcie jednego z pary awansuje następnego.
  Zamiast `OpId` z `LastOpId.Next` (Codex): tick nie wiąże operacji z numerem, który dostanie dopiero w `TryApplyAndJournal`, a remis w tym samym ticku rozstrzyga deterministycznie `BlockRef`; istniejącej pary nie da się odebrać, bo nowe nazwanie ma tick ≥ każdego istniejącego.

- **D5. Para jest pochodna i nie dotyka appliera. [zmienione w przeglądzie]**
  Trwały jest tylko stan bloku. `TeleportPairs` (serwer, Core `Simulation/`) wylicza pary leniwie ze `state.BlockEntities` i unieważnia pamięć podręczną w zdarzeniu `Applied`, gdy operacja (także składowa złożonej) dotyka stanu rodzaju 4 albo usuwa blok ze stanem 4.
  Odrzucona operacja nie wywołuje `Applied`, więc `UndoLog` nie dostaje kroków indeksu. Odbudowa z checkpointu = to samo wyliczenie.

- **D6. Zakaz `Bulk`.** Walidator sprawdza wszystkie niepuste sloty `PlayerBag`, `Hotbar` i `Equipment` gracza (flaga z `ItemDef`); `TeleportAll` z ustawień świata znosi zakaz.
  Kontrola powtarzana przed op 7 (ustawienie mogło się zmienić). Gracz w przejściu nie może dołożyć przedmiotu (bramka, D8), więc powtórna kontrola chroni głównie przed zmianą ustawień.

- **D7. Koszt użycia.** Spec nie przewiduje kosztu przejścia — teleport jest darmowy; koszt budowy obsługuje K6-36. Płatny teleport = zmiana spec (DO USTALENIA-1).

- **D8. Jedna bramka przejścia.** Rejestr przejść K6-35 (nazwa klasy do potwierdzenia w A0, **niezweryfikowane**) dostaje rodzaj `Teleport`; odmowa `InTransition = 48`, tania (bez żetonu, K1-03).
  W przejściu: intencje mutujące świat i przedmioty odrzucone, subskrypcje kontenerów zamknięte na starcie przejścia, wejście ruchu ignorowane (ciało stoi), zmiana fotela niemożliwa.
  Śmierć ma pierwszeństwo: `DeathService` anuluje przygotowanie teleportu w tym samym ticku (jedna ścieżka sprzątania). Oczekiwanie nie daje nietykalności (zagrożenia K6-34 działają dalej).

- **D9. Przyjęcie bez operacji. [dodane w przeglądzie]**
  `TeleportIntent` przyjęty → `IntentResult.AcceptedWithoutOperation()` (wzorzec `PropHitIntent`, K6-31 D10), żeton kubełka `Blocks` pobrany przy przyjęciu.
  Core zapisuje ulotny wpis przygotowania (gracz, źródło, cel, oczekiwane typy bloków i `NamedTick` obu stron, `ParentSeq` gracza i obu budowli, tick startu). Wpis nie jest haszowany ani zapisywany (D22).

- **D10. Przygotowanie celu. [zmienione w uzgodnieniu]**
  K6-35 dostarcza współdzielone przygotowanie terenu dla odrodzenia (plan K6-35 C4 `DeathPreparation` + adapter, l. 496 „współdzielone przygotowanie terenu”). K6-37 **rozszerza ten komponent** o rodzaj `Teleport` (kandydaci przy teleporcie, kapsuła) zamiast pisać drugi adapter; śmierć i teleport dzielą **jeden** budżet sprawdzeń na tick, a limity 4/tick i 32 jednocześnie są podziałem tego budżetu. Jeśli scalony K6-35 nie ma komponentu nadającego się do rozszerzenia — stop i bramka Opus w A0, nie kopiować.
  Zakres rozszerzenia (`TeleportTerrainPreparation` = rodzaj w komponencie K6-35): `ServerPlanetTerrains.Acquire(planet, PlanetLeaseReason.Teleport)` (osobny powód tylko gdy K6-35 nie ma ogólnego) + widz LOD 0 w punkcie wyjścia; gotowość = `GetReadiness` gotowe **i** kolizja (ta sama sonda co `StepFrozen`: `TryFindGround` terenu i budowli) **i** wolna kapsuła `PlayerCapsule` na kandydacie.
  Kandydaci: cztery boki komórki teleportu w osiach budowli, w stałej kolejności, przeliczeni do ramy planety przez `FrameMath`; kapsuła i podparcie wzdłuż radialnego „w górę” planety (lekcja K6-33/K6-35 D15). Odstępy z `PlayerCapsule` — zatwierdzić w A1.
  Budżet: najwyżej 4 przygotowania sprawdzane na tick, rotacyjnie; najwyżej 32 jednocześnie na serwer (nadmiar → `RateLimited`, bez kolejki). Wartości w `GameRules`, DO USTALENIA-10.
  Dzierżawę i widza zwolnić po anulowaniu albo dopiero, gdy `ServerCharacters` ma ciało gracza na planecie celu (najpóźniej po `GroundWaitDeadlineTicks`), żeby planeta nie wygasła między op 7 a zapotrzebowaniem gracza.

- **D11. Wykonanie = op 7 w formacie 1.** Gotowa poza w ramie planety celu (`FrameRef` planety), prędkość zero, `SeatCell = null`, `NewParentSeq = ParentSeq + 1` także na tej samej planecie, bez `ResetVitals` (op 7 v1).
  Samodzielna operacja (nie złożona) — applier stosuje ją już dziś; **bez zmiany appliera dla teleportu**. Nie używać `FrameTransitions.TryReparent` ani rebase (zachowują pozycję świata).
  Po op 7 osiadanie robi istniejące `FollowReparent` (`Walk`/`Respawn`); test wymaga, by ciało przeszło w `Walking` bez `GroundWaitExpired`.
  Wyjście wewnątrz zaparkowanego statku: gracz ląduje w ramie planety; czy istniejąca reguła wejścia na pokład przepina go do ramy statku — **niezweryfikowane**, A0.

- **D12. Gdzie teleport działa.** Źródło i cel: budowla statyczna albo statek zaparkowany, w ramie planety. Statek w locie, w ramie układu, gracz w fotelu → odmowa (`InvalidParameters`; fotel: K6-35 zwolnienie fotela nie dotyczy).
  Postawienie dozwolone wszędzie; panel pokazuje przyczynę nieaktywności. DO USTALENIA-4.

- **D13. Unieważnienie.** Przed op 7 ponowna walidacja: oba bloki istnieją z oczekiwanym typem i stanem (ta sama nazwa i `NamedTick`), para niezmieniona, `ParentSeq` gracza i obu budowli bez zmian, statki nadal zaparkowane, `Bulk` (D6). Każda różnica → anulowanie przy źródle (nie przekierowanie). Bez przepinania statku i bez zmiany progów stref K5-22.

- **D14. Ładownia. [uzupełnione w przeglądzie]**
  `CargoHoldBlockState(ContainerId Container)` (rodzaj 7) + kontener `CargoHold` z gospodarzem `ConstructBlock`; postawienie/rozbiórka przez kompozycje K6-36 (stan i `ContainerCreateOp` przed blokiem, rozbiórka niepustej → 57, zawalenie → wysypanie K6-36).
  Rozmiar z treści: `tier` funkcji `cargo` → sloty z tabeli treści (1→32, 2→64, 3→96); `ContainerCreateOp` niesie rozmiar (applier nie czyta treści). Treść ma dziś tylko poziom 1 — bloki `core:ship_cargo_t2/_t3` dodać jako treść z kosztem K6-36; odblokowanie bossami K8-54 (DO USTALENIA-8).
  Dostęp jak skrzynia: otwieranie 6 m, utrzymanie 8 m, `Shared`/`Private`. **Ograniczenie v1:** `ContainerReach` liczy w ramie planety, więc ładownia statku w ramie układu jest niedostępna (subskrypcja odrzucona, czytelny tekst); dostęp w przestrzeni to DO USTALENIA-7.
  Spójność stanu i kontenera: istniejący `CheckChestConsistency` uogólnić na rodzaj 7 (stan ↔ kontener `CargoHold` z tym samym gospodarzem).

- **D15. Ładownia jako źródło. [dodane w przeglądzie]**
  Indeks przestrzenny `ContainerStore` (`HasChests`, `ChestsInChunk`) uogólnić na kontenery bloków rodzajów `Chest` i `CargoHold` (z filtrem rodzaju w zapytaniu); `CraftingRules.BuildSources` przyjmuje `CargoHold` zgodnie z K6-32 l. 79 (wynik dotychczasowych testów rzemiosła bez ładowni bez zmian).
  Źródła budowania K6-36 i `QuickStack` (spec: „do skrzyń”) pozostają przy skrzyniach, chyba że właściciel zdecyduje inaczej (DO USTALENIA-8).

- **D16. Tankowanie z ogniw.** `RefuelIntent = 13` bez zmiany formatu; walidator bez zmian (statek do 15 m od stacji).
  `RefuelRules` (Core, wzorzec `CraftingRules`): `need = capacity − fuel` (żywe `ShipStates`, jak walidator), `cells = ceil(need / unitsPerCell)`; źródła: plecak, potem kontenery `Chest`/`CargoHold` dostępne graczowi (`Shared` albo własne `Private`) w promieniu `RefuelContainerRangeMeters = 20` od środka komórki stacji, ta sama planeta, najbliższe pierwsze, remis po `ContainerId` i slocie.
  Bierze `min(cells, dostępne)`; częściowe tankowanie dozwolone, nadwyżka ostatniego ogniwa obcinana przez `Capacity`. Brak ogniw → `NotEnoughItems` (41). Ogniwo i jednostki na ogniwo z treści (`logistics.json`: lista `{item, units}`; propozycja `core:fuel_cell` = 100 przy zbiorniku 1000, DO USTALENIA-7').
  Wynik: `CompoundOp[ItemDestroyOp(sink Fuel)…, ShipFuelAddOp(units, capacity)]`; więcej niż 4095 składowych zniszczenia → obciąć liczbę ogniw (nie odmawiać), test granicy.
  Darmowe paliwo (bit 1 ustawień K6-35): samodzielna op 21 do pełna jak dziś. Hotbar jako źródło — DO USTALENIA-7 (spec mówi „z plecaka”).
  Odmowa po skanie kontenerów pobiera żeton mechanizmem wyniku K6-36 D16; tania odmowa (walidator) nie.

- **D17. Op 21 jako ostatnia nieodwracalna składowa. [zmienione w przeglądzie]**
  Zamiast rozszerzać `UndoLog` o `ShipStateRecord` (Codex): `ShipFuelAddOp` dozwolony w `CompoundOp` **tylko jako ostatnia składowa**, bez edycji terenu, budowli ani przepięcia w tej samej złożonej; `CheckShipFuelAdd` sprawdzany przed pozostałymi (nie zależy od nich — żadna składowa nie zmienia trybu budowli), stosowany po `CheckChestConsistency`/`CheckStations`.
  Rozszerzyć ZASADY l. 29 w brzmieniu po K6-35 i K6-36 (K6-36: edycje budowli odwracalne w `CompoundOp` v2, teren jedyną nieodwracalną ostatnią; K6-35: przepięcie gracza op 7 v2): dopisać wyłącznie „albo dolanie paliwa (op 21, K6-37)”; nie przywracać budowli jako nieodwracalnej. [uzgodnienie 2026-10-17] Stare dzienniki nie zawierają op 21 w złożonej → wyjątek ZASADY §3 l. 72 (korpus i `DeterminismTests` bez zmian).
  Paliwo pozostaje poza skrótem (ZASADY l. 92); po awarii powtórka daje to samo zużycie ogniw, a poziom paliwa może różnić się o lot niezapisany od checkpointu (cecha K5-24, opis w PR).

- **D18. Podtrzymanie życia.** Reguła K6-34 D14 (`Seat != null` i rama = budowla `Ship`) sprawdzona na kokpicie, fotelu i stanowisku nawigatora K5-25, z wejściem, wyjściem, przepięciem i zmianą strefy (natychmiastowa zmiana próbki, K6-34 B1 fix). Bez zależności od paliwa (DO USTALENIA-9). Brak integracji K5-25 lub K6-34 po stronie serwera blokuje C5 — nie zastępować atrapą.

- **D19. Ustawienia.** `TeleportAll` (bit 0) i darmowe paliwo (bit 1) czytać z rekordu ustawień K6-35 (op 46, blob 23, komunikat 47); bez równoległych flag w `GameRules`. Zmiana w trakcie przejścia obowiązuje przy D13.

- **D20. Stan przejścia u właściciela. [poprawione w przeglądzie]**
  Komunikat 48 to `PlayerProgressState`; K6-35 D20 dopisuje do niego „stan odradzania”. K6-37 uogólnia to pole do `Transition { Kind (0 brak, 1 odrodzenie, 2 teleport), Revision: uint, TargetParentSeq: uint, Outcome (0 w toku, 1 wykonano, 2 limit czasu, 3 cel nieważny, 4 brak miejsca, 5 śmierć, 6 rozłączenie/zamknięcie) }`.
  **[zmienione w uzgodnieniu]** `MessageCodec` ma jedną wersję formatu dla wszystkich komunikatów (`CurrentFormatVersion = 1`, l. 84–87 odrzuca inną) i komunikaty nie są zapisywane, więc **bez bajtu wersji 48**. Zalecenie dla bramki A1 K6-35: od razu zakodować stan odradzania jako ogólne `Transition` (`Kind = 1`, `Kind = 2` zarezerwowane dla teleportu) — wtedy K6-37 nie zmienia układu. Jeśli K6-35 scali inny układ, K6-37 zmienia go wyłącznie pod PV 22 (bajt wersji zostaje `01`); B1 zatwierdza złote bajty przed i po. Komunikat wysyłany tylko przy zmianie stanu przejścia.
  Klient kończy oczekiwanie po migawce z `TargetParentSeq` i gotowości lokalnego terenu; nie zakłada kolejności kanału niezawodnego i zawodnego.

- **D21. Bez bariery epoki w `IntentSubmit`. [zmienione w przeglądzie]**
  Codex proponował format 2 komunikatu 11 z `ExpectedParentSeq`. Odrzucone: intencje idą kanałem niezawodnym uporządkowanym, więc wysłane przed `TeleportIntent` są przetworzone przed nim; wysłane w trakcie przejścia odrzuca bramka; wysłane po op 7 są walidowane względem bieżącej pozy serwera (promień z oka → `RayOriginMismatch`, zasięg, subskrypcje zamknięte).
  Klient blokuje wysyłanie intencji mutujących i wysyła neutralne wejście od chwili wysłania `TeleportIntent` do zakończenia przejścia. Test potwierdza, że spóźniona intencja jest oceniana w nowym kontekście i nie mutuje niczego przy źródle.

- **D22. Trwałość.** Nazwy, `NamedTick`, kontenery ładowni i koszt tankowania: istniejące checkpointy i dziennik. Przygotowanie ulotne: rozłączenie, zamknięcie, restart lub tryb bezpieczny przed op 7 → anulowanie przy źródle; po zapisanej op 7 gracz odtwarza się w celu. Bez trwałego timera.

- **D23. Skrót.** Warianty 4 (`Name` jako tekst, `NamedTick` u64) i 7 (`Container` u64) w `0x42`; bez nowego tagu. Świat bez nowych stanów ma identyczny skrót. Pin na własnej zamrożonej fiksturze `LogisticsHashFixture` (lekcja K6-31 `db779bd`).

- **D24. Istniejące ładownie. [doprecyzowane w przeglądzie]**
  Bloki `core:ship_cargo` sprzed K6-37 nie mają stanu ani kontenera. Przy starcie, po powtórce ogona i przed przyjęciem graczy, serwer tworzy dla każdej takiej komórki (posortowane po budowli i komórce) operację serwera `CompoundOp[ContainerCreateOp, BlockEntitySetOp]`, najwyżej 1024 komórki na operację.
  Idempotentne (pomija komórki ze stanem), dziennikowane, w trybie bezpiecznym pominięte. Bez migracji i bez mutacji podczas odczytu. Alternatywa „leniwie przy pierwszym otwarciu” — DO USTALENIA-11.

## Kontrakty chronione i wersje

| Kontrakt | Zmiana | Wersja i bramka |
| --- | --- | --- |
| `Intents/`, `IntentCodec` | Nowa intencja 30 `TeleportIntent(EntityId Construct, CellPos Cell)` format 1; obsługa istniejącego `TeleportNameConfig` (bajty bez zmian) | PV 22; Opus A1/B1 |
| `Intents/RejectReason` | `CannotTeleportBulk = 47` | PV 22 |
| `BlockEntities/`, `StateCodec`, op 40, blob 21, komunikat 46 | Warianty 4 i 7 | Nowe wartości dyskryminatora, stare bajty bez zmian; PV 22 |
| `OperationApplier` | op 21 jako ostatnia składowa złożonej (D17); spójność stanu 7 ↔ kontener (D14) | Bez podbicia formatu operacji pod warunkiem ZASADY §3 l. 72; Opus |
| `UndoLog` | **Bez zmian** (D5, D17) | — |
| op 7 | Użycie formatu 1 | Bez zmian |
| Komunikat 48 | Pole `Transition` (D20) | Tylko PV 22; bajt wersji komunikatów bez zmian (`01`, jedna wersja w `MessageCodec`) |
| `Items/ContainerStore` | Indeks przestrzenny kontenerów bloków (Chest + CargoHold) | Struktura w pamięci; Opus |
| `Hashing/StateHasher` | Warianty 4 i 7 w `0x42` | Opis w PR; stare złote skróty bez zmian |
| `Protocol/ProtocolConstants` | `ProtocolVersion = 22` | `ProtocolContractTests` aktualizowane wyłącznie o 30, 47 i 22 |
| `GameRules`, `IntentValidator` | `RefuelContainerRangeMeters`, limity przygotowań, walidacja 29/30 | Nie format (ZASADY l. 74); Opus |
| `Persistence/Migrations/` | Brak | — |
| `BlockTypeDef`, `blocks.json`, `items.json`, `logistics.json` | Treść | Nie format (ZASADY l. 58) |

Zamrożone `ClientHello` i `HandshakeReject` bez zmian. Korpus `TerrainJournalGoldenCorpusTests` i `DeterminismTests`: `git diff <commit A0>..HEAD` na tych ścieżkach pusty.

### Rezerwacje sąsiadów

| Pakiet | Numery |
| --- | --- |
| K6-30 | op 36/37/40/44/49, intencje 22/23/28/29, komunikat 46, bloby 21/22, rodzaje stanu 1/2 |
| K6-31 | op 47/48, intencja 27, odmowa 53, komunikat 45, tag `0x48` |
| K6-32 | op 43/45, intencja 24, odmowy 44–46, komunikat 48, blob 25, tagi `0x44`/`0x4B`, rodzaj stanu 3 |
| K6-33 | op 41, intencja 25, odmowa 49, komunikat 44, tag `0x46` |
| K6-34 | op 42, intencja 26, rodzaj stanu 5, `BlockFunctionKind` 16, blob 2 v3, PV 19 |
| K6-35 | op 38/39/46, bloby 23/24, komunikat 47, odmowa 48, rodzaje bytów 3/6, tagi `0x54`/`0x57`, op 7 v2, PV 20 |
| K6-36 | intencja 34, odmowy 51/52/55/58, `CompoundOp` v2, intencje 1/3 v2, ogon `IntentReply`, PV 21 |
| **K6-37** | **intencja 30, odmowa 47, rodzaje stanu 4/7, `BlockFunctionKind` 17 (treść), PV 22** |
| K6-38 | intencje 31/32, odmowa 50, rodzaj stanu 6, `BlockFunctionKind` 18 (treść), PV 23 (po korekcie) |
| K6-39 | intencja 33, odmowa 56, PV 24 (po korekcie, jeśli podbija) |

Odmowa 59 pozostaje wolna (DO USTALENIA-3).

## Projekt

Nowe nazwy są projektem; ścieżki istniejących plików sprawdzone w `k6-35`, chyba że oznaczono inaczej.

| Obszar | Pliki |
| --- | --- |
| Model Core | Nowe: `src/Horyzont.Core/BlockEntities/TeleportBlockState.cs`, `CargoHoldBlockState.cs`; istniejące: `BlockEntityKind.cs`, `BlockEntityStore.cs`, `World/Blocks/BlockFunctionKind.cs`, `Items/ContainerStore.cs` |
| Reguły Core | Nowe: `Simulation/TeleportRules.cs` (walidacja 30, `Bulk`, kandydaci wyjścia), `Simulation/TeleportPairs.cs`, `Simulation/TeleportTransitions.cs` (ulotny rejestr — albo rozszerzenie rejestru K6-35), `Simulation/RefuelRules.cs`; istniejące: `IntentValidator.cs`, `ServerSimulation.cs`, `FunctionalBlockRules.cs`, `CraftingRules.cs`, `ContainerReach.cs`, `OperationApplier.cs`, `GameRules.cs` |
| Kontrakty | `Intents/TeleportIntent.cs`, `RejectReason.cs`, `Serialization/IntentCodec.cs`, `Serialization/StateCodec.cs`, kodek komunikatu 48, `Protocol/ProtocolConstants.cs` |
| Skrót | `Hashing/StateHasher.cs` |
| Treść | Nowe: `src/Horyzont.Core/Content/LogisticsDef.cs`, `game/content/logistics/logistics.json`; istniejące: `game/content/blocks/blocks.json`, `items/items.json`, `recipes/recipes.json`, loader treści (`ContentLoader`), `BlockTypeCatalog` (`src/Horyzont.Constructs/Content/`) |
| Serwer | Nowe: `src/Horyzont.Server/Logistics/TeleportService.cs`, `CargoBackfill.cs`, `LogisticsLog.cs`; istniejące: `Space/PlanetLeaseReason.cs`, `Inventory/InventoryService.cs`, rejestr bramki K6-35 |
| Adapter Godota | Nowe: `game/src/Server/Logistics/TeleportTerrainPreparation.cs`, `game/src/Boot/GameComposition.Logistics.cs`; istniejące: `game/src/Server/Terrain/ServerPlanetTerrains.cs`, `game/src/Server/Characters/ServerCharacters.cs` |
| Klient | Nowe: `src/Horyzont.Client/Logistics/ClientTeleportState.cs`, `TeleportInteraction.cs`; istniejące: obsługa komunikatu 48, interakcja z kontenerami (`ChestSession`/`InventoryInteraction` — ścieżki **niezweryfikowane**) |
| Interfejs | Nowe: `game/src/Client/Logistics/TeleportPanel.cs`, `TeleportTransitionView.cs`; istniejące: panel kontenera (`game/src/Client/Inventory/ChestPanel.cs`), panel stacji tankowania |
| Teksty | Nowe klucze w plikach `game/locale/` (położenie CSV potwierdzić w A0) |

### Kolejność walidacji `TeleportIntent`

1. Gracz znany, online; świat zapisywalny (`ServerReadOnly`).
2. Bramka przejścia (`InTransition`, bez żetonu); gracz w fotelu → `InvalidParameters`.
3. Budowla i komórka (`UnknownConstruct`, `CellOutOfBounds`, `CellEmpty`), funkcja `Teleport` i stan rodzaju 4 (`InvalidParameters`).
4. Zasięg i LOS jak `ValidateBlockInteract` (`OutOfReach`, `NoLineOfSight`, `WrongPlanet`).
5. Rama źródła dozwolona (D12), para istnieje i cel dozwolony (D12) → inaczej `InvalidParameters`.
6. `Bulk` bez `TeleportAll` → `CannotTeleportBulk` (47).
7. Kubełek `Blocks` i limity przygotowań (`RateLimited`).
8. Przyjęcie bez operacji (D9), żeton pobrany.

Wszystkie odmowy 1–7 są tanie (bez żetonu). Brak pary, zła rama i brak miejsca nie dostają własnych kodów (DO USTALENIA-3).

### Przepływ teleportu i harmonogram

`Submit(TeleportIntent)` → wpis przygotowania + rejestr bramki + zamknięcie subskrypcji → (tick, faza `Compose`, po `VitalsService` i `DeathService`) `TeleportService.Step`: rotacyjnie do 4 wpisów → adapter: dzierżawa, widz, gotowość, kandydat → D13 → `ApplyServerOperation(EntityReparentOp v1)` → komunikat 48 (`Outcome = 1`) → zwolnienie bramki → dzierżawa do potwierdzenia ciała.
Limit: `StartTick + 60 × TickRate` → anulowanie (`Outcome = 2`), zwolnienie wszystkiego. Kolejność rejestracji uczestników zatwierdza C1 (A0 sprawdza, czy migawki wychodzą po `Compose`).

### Tankowanie

`Submit(RefuelIntent)` → walidator (bez zmian) → `RefuelRules.Resolve` → złożona [34×N, 21] albo samodzielna 21 (darmowe paliwo) → applier (D17) → `Applied` → delty kontenerów i `ShipStatus`.

## Testy akceptacyjne — napisz najpierw

Każdy test odmowy ma poprawny odpowiednik różniący się jednym warunkiem (lekcja „testy przechodzące pusto”). Testy właściwości mierzą liczbę faktycznie wykonanych transferów/zniszczeń/odmów i mają progi pokrycia.

| Test | Projekt | Wymagany wynik |
| --- | --- | --- |
| `TeleportPairsTests.ThirdNamed_HasNoPartner_FirstPairKept` | Core | Pierwsza para zachowana |
| `TeleportPairsTests.RemoveOfPaired_PromotesNext` | Core | Następny po `(NamedTick, BlockRef)` |
| `TeleportPairsTests.SameTickNaming_TieBreakByBlockRef` | Core | Deterministyczny remis |
| `TeleportPairsTests.RenameSameName_KeepsNamedTick` | Core | Tick bez zmiany |
| `TeleportPairsTests.ReplayAndCheckpoint_SamePairs` | Persistence | Te same pary po checkpoincie + ogonie |
| `TeleportPairsTests.RefusedCompound_DoesNotInvalidateCache` | Core | Brak `Applied` → pamięć podręczna aktualna |
| `TeleportNameTests.Utf8Boundary` | Core | 32 B przyjęte; 0 B, 33 B, zły UTF-8 odrzucone |
| `TeleportValidationTests.EachRefusal_HasAcceptedTwin` | Core | Kody z listy walidacji; tanie odmowy bez żetonu |
| `TeleportValidationTests.Accept_ConsumesToken_NoOperation` | Core | `AcceptedWithoutOperation`, żeton −1 |
| `TeleportInventoryTests.BulkInBagHotbarEquipment_Rejected47` | Core | Trzy osobne przypadki + odpowiednik bez `Bulk` |
| `TeleportInventoryTests.TeleportAllToggledDuringWait_RecheckedAtCommit` | Server | Bieżące ustawienie |
| `TeleportGateTests.ItemIntentDuringTransition_InTransitionNoToken` | Server | 48, żeton nietknięty |
| `TeleportGateTests.SubscriptionsClosedOnStart` | Server | Brak otwartych kontenerów |
| `TeleportGateTests.DelayedIntentAfterCommit_ValidatedAtNewPose` | Server | Nic nie mutuje przy źródle (D21) |
| `TeleportTransitionTests.LoadedWithoutCollision_DoesNotCommit` | Server | Gotowość danych ≠ kolizja |
| `TeleportTransitionTests.Timeout60s_StaysAtSource_AllReleased` | Server | Brak op 7, dzierżawa i widz zwolnione, `Outcome = 2` |
| `TeleportTransitionTests.TargetRenamedRemovedOrUnparked_Cancels` | Server | `Outcome = 3`, brak op 7 |
| `TeleportTransitionTests.NoFreeCandidate_CancelsAtTimeout` | Server | Brak wejścia w kolizję |
| `TeleportTransitionTests.DeathDuringWait_DeathWins` | Server | Jedna ścieżka sprzątania |
| `TeleportTransitionTests.DisconnectShutdownSafeMode_Cancel` | Server | Bez op 7, bez blokady po ponownym wejściu |
| `TeleportTransitionTests.GlobalLimit_RateLimited` | Server | 33. przygotowanie odrzucone |
| `TeleportFrameTests.SamePlanet_ParentSeqPlusOne_V1Format` | Core | Op 7 v1, `SeatCell = null`, prędkość 0 |
| `TeleportFrameTests.TiltedConstruct_CandidatesAlongPlanetUp` | Core | Kapsuła wzdłuż radialnego „w górę” |
| `TeleportFrameTests.LargeCoordinates_FrameMathPrecision` | Core | Błąd < 1 mm przy dalekiej planecie |
| `TeleportFrameTests.ZoneOrParentSeqChangeDuringWait_Cancels` | Server | Brak użycia starej pozy |
| `TeleportFlowTests.OtherPlanet_BodyGroundsWithoutDeadline` | Integration | `Walking` bez `GroundWaitExpired`; planeta nie wygasa przed ciałem |
| `CargoTests.Tiers_32_64_96_HostAndKind` | Core | Rozmiar z operacji, gospodarz `ConstructBlock`, rodzaj 5 |
| `CargoTests.StateWithoutContainer_RefusedByApplier` | Core | Spójność 7 ↔ kontener, także w złożonej w obu kolejnościach |
| `CargoTests.PrivateAndNonEmptyRemoval_54_57` | Core | Bez zmiany zawartości |
| `CargoTests.ShipInSystemFrame_SubscribeRefused` | Server | Ograniczenie v1 (D14) |
| `CargoBackfillTests.OldCargoBlocks_ExactlyOneContainer_Idempotent` | Server/Persistence | Drugi start nic nie dodaje; tryb bezpieczny pomija |
| `CraftingSourceTests.CargoHoldWithin20m_IsSource_ChestResultsUnchanged` | Core | K6-32 D13 + regresja |
| `RefuelTests.CellsFromBagThenContainersWithin20m_NearestFirst` | Core | Granica 20 m (19,9 / 20,1), ta sama planeta, dostęp |
| `RefuelTests.PartialFill_LastCellOverflowClamped` | Core | Jednostki = ogniwa × wydajność, obcięcie do pojemności |
| `RefuelTests.NoCells_NotEnoughItems_ScanCostsToken` | Core | 41; żeton po skanie, nie po tanim warunku |
| `RefuelTests.FreeFuel_StandaloneOp21_NoDestroy` | Core | Brak `ItemDestroyOp` |
| `RefuelTests.ManyOneCountStacks_CapsAt4095Destroys` | Core | Brak przekroczenia limitu złożonej |
| `RefuelAtomicityTests.Op21NotLast_StructureRefused` | Core | „must be the last component” |
| `RefuelAtomicityTests.DestroyFails_NoFuelAdded_SlotsRestored` | Core | Paliwo, sloty, wersje kontenerów bez zmian |
| `RefuelAtomicityTests.Op21WithConstructEdit_Refused` | Core | Jedna nieodwracalna |
| `RefuelAtomicityTests.StandaloneOp21_ResultUnchanged` | Core | Stara semantyka |
| `RefuelPersistenceTests.CrashBeforeAfterJournal_NoDoubleCost` | Persistence | Koszt raz albo wcale |
| `ShipLifeSupportTests.CockpitSeatNavigator_EnterExitZone_Immediate` | Server | Ochrona zgodna z faktycznym fotelem |
| `LogisticsHashTests.WorldWithoutNewStates_HashUnchanged` | Core | Pin na `LogisticsHashFixture` |
| `LogisticsHashTests.RenameChangesHash_PairCacheDoesNot` | Core | Indeks poza skrótem |
| `LogisticsCodecTests.GoldenBytes_Intent30_Kinds4And7_Msg48` | Core | Złote bajty ręcznie wyliczone w planie A1 |
| `LogisticsContentTests.IngotsBulk_FuelUnitsPositive_CargoTiers` | Core/Constructs | Treść zgodna ze spec l. 158 |
| `LogisticsConservationProperties.TransfersRefuelCargo_ConserveItems` | Core | Niezmiennik spec l. 292, próg pokrycia |
| `LogisticsAllocationTests.TeleportStepAndPairsLookup_SteadyState_NoAlloc` | Server | Gorące ścieżki: `TeleportService.Step` bez wpisów i z wpisami czekającymi, `TeleportPairs.TryGetPartner` przy ciepłej pamięci |
| `ClientTeleportStateTests.*` | Client | Starsza rewizja ignorowana; odblokowanie po `TargetParentSeq` i terenie; blokada intencji mutujących |
| `TeleportPanelTests` (gdUnit, bez okna) | game | Stany: bez nazwy, oczekiwanie, anulowano, przedmioty masowe; siatki 32/64/96 |

## Kroki

W każdym kroku: najpierw test ujawniający brak zachowania (uruchomiony, czerwony), potem kod, potem komenda akceptacji. Commit w Conventional Commits.

### Etap A — model i atomowość Core

- [ ] **A0. Baza.** Zapisać commit, `ProtocolVersion`, obecność kontraktów K6-34 (intencja 26, `HazardTracker`, `lifeSupport` po stronie serwera), K6-35 (rejestr bramki, ogon 48, op 7 v2, ustawienia 46/23/47, kolejność `DeathService`), K6-36 (`CompoundOp` v2, wynik żetonu), K5-25 (role foteli).
  Sprawdzić fakty oznaczone „niezweryfikowane” (reguła wejścia na pokład, ścieżki klienta, położenie CSV, kto może zmieniać konfigurację bloku, trwałość `CurrentTick`, wysyłka migawek po `Compose`).
  Komenda: `rg -n "ProtocolVersion|InTransition|WorldSettings|IsComponent|ChestsInChunk|PlanetLeaseReason|AcceptedWithoutOperation" src game/src`.
  Test: `LogisticsHashTests.WorldWithoutNewStates_HashUnchanged` na `LogisticsHashFixture` (pin sprzed zmian). Akceptacja: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~LogisticsHash"`.
  Brak scalonego K6-35 blokuje B i C, nie A1–A5.

- [ ] **A1. Czerwone kontrakty i bramka Opus.** Szkielety rekordów (`TeleportBlockState`, `CargoHoldBlockState`, `TeleportIntent`), testy A z tabeli, złote bajty wyliczone ręcznie w opisie PR.
  Akceptacja: `dotnet build Horyzont.sln`; lista oczekiwanych czerwonych testów w PR. Opus zatwierdza D2–D5, D10 (odstępy kandydatów), D12, D16–D17, D24 i limity.

- [ ] **A2. Stany i pary.** `TeleportBlockState`, `CargoHoldBlockState`, `BlockEntityStore`, `TeleportPairs`. Akceptacja: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~TeleportPairs|FullyQualifiedName~TeleportName"`.

- [ ] **A3. Applier: ładownia.** Uogólnienie `CheckChestConsistency` na rodzaj 7; rollback nieudanego postawienia. Akceptacja: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~Cargo|FullyQualifiedName~FunctionalBlock|FullyQualifiedName~Chest"`.

- [ ] **A4. Applier: op 21 ostatnia (D17).** Struktura w `TryApplyCompound`, sprawdzenie przed pozostałymi, stosowanie po kontrolach spójności. Akceptacja: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~RefuelAtomicity|FullyQualifiedName~ShipFuel|FullyQualifiedName~Compound"`.

- [ ] **A5. Reguły czyste i skrót.** `TeleportRules`, `RefuelRules`, indeks `ContainerStore`, `CraftingRules` z ładownią, `StateHasher`. Akceptacja: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~TeleportInventory|FullyQualifiedName~TeleportFrame|FullyQualifiedName~Refuel|FullyQualifiedName~CraftingSource|FullyQualifiedName~Crafting|FullyQualifiedName~LogisticsHash"`.

- [ ] **A6. Bramka Opus A.** Diff Core; applier nie czyta treści ani `ShipStates` w warunkach; `UndoLog` nietknięty.
  Akceptacja: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~Determinism|FullyQualifiedName~TerrainJournalGoldenCorpus|FullyQualifiedName~Logistics|FullyQualifiedName~Refuel|FullyQualifiedName~Cargo|FullyQualifiedName~Teleport"` + sprawdzenie końca etapu.

### Etap B — protokół, treść, walidacja

- [ ] **B1. Czerwone testy binarne i bramka Opus.** Złote bajty intencji 30, wariantów 4/7 (op 40, blob 21, komunikat 46), pola `Transition` w 48, odrzucanie nieznanego rodzaju, zbyt długiej nazwy i złego UTF-8. Opus zatwierdza format 48 względem scalonego K6-35 i PV 22. Akceptacja: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~LogisticsCodec|FullyQualifiedName~ProtocolContract"`.
- [ ] **B2. Kodeki i wersja.** `IntentCodec`, `StateCodec`, kodek 48, `RejectReason`, `ProtocolConstants`. Akceptacja jak B1 + `FullyQualifiedName~GeneratorSanity`.
- [ ] **B3. Treść.** `logistics.json` (ogniwa i jednostki, sloty poziomów), bloki `core:teleport`, `core:ship_cargo_t2/_t3` (koszt przez K6-36), flaga `bulk` dla sztab (`core:iron_ingot` i każdej innej sztaby z `items.json`), receptury z istniejącymi źródłami. Akceptacja: `dotnet test Horyzont.sln --filter "FullyQualifiedName~LogisticsContent|FullyQualifiedName~BlockTypeCatalog|FullyQualifiedName~Crafting"`. Po zmianie flag sprawdzić testy rzemiosła i kombinezonu K6-32/K6-34 (lekcja K6-34 B5: `CraftingUiTests`).
- [ ] **B4. Walidacja i rozstrzyganie.** `IntentValidator` (30 i 29 dla teleportu), `FunctionalBlockRules` (postawienie/rozbiórka teleportu i ładowni w kompozycjach K6-36), `RefuelRules` w `Submit` z wynikiem żetonu K6-36. Akceptacja: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~TeleportValidation|FullyQualifiedName~TeleportInventory|FullyQualifiedName~Refuel|FullyQualifiedName~Cargo"`.
- [ ] **B5. Dostęp i odległości.** 6/8 m dla ładowni, 20 m źródeł paliwa, obrócone budowle, inna planeta, statek w ramie układu. Akceptacja: `dotnet test Horyzont.sln --filter "FullyQualifiedName~ContainerReach|FullyQualifiedName~CargoAccess|FullyQualifiedName~RefuelRange"`.
- [ ] **B6. Bramka Opus B.** Akceptacja: `dotnet test tests/Horyzont.Core.Tests` i `dotnet test tests/Horyzont.Net.Tests` + sprawdzenie końca etapu.

### Etap C — serwer, przejście, zapis

- [ ] **C1. Czerwone testy przepływu i harmonogram (bramka Opus).** Rejestracja `TeleportService` w `Compose` po `DeathService`; granica z rejestrem K6-35. Akceptacja: `dotnet test tests/Horyzont.Server.Tests --filter "FullyQualifiedName~Teleport"` (czerwone zgodnie z listą).
- [ ] **C2. Zapis i uzupełnienie ładowni (D24).** `CargoBackfill`, checkpoint + ogon. Akceptacja: `dotnet test tests/Horyzont.Persistence.Tests --filter "FullyQualifiedName~Logistics|FullyQualifiedName~Cargo|FullyQualifiedName~Refuel|FullyQualifiedName~TeleportPairs"`.
- [ ] **C3. Bramka, przygotowanie, sprzątanie.** `TeleportService`, rejestr K6-35, zamykanie subskrypcji w `InventoryService`, komunikat 48. Akceptacja: `dotnet test tests/Horyzont.Server.Tests --filter "FullyQualifiedName~TeleportTransition|FullyQualifiedName~TeleportGate|FullyQualifiedName~TeleportInventory"`.
- [ ] **C4. Teren, kapsuła, ciało.** Rozszerzenie komponentu przygotowania K6-35 o rodzaj `Teleport` (D10; wspólny budżet), `PlanetLeaseReason.Teleport` tylko jeśli potrzebny, `GameComposition.Logistics.cs`, zwolnienie dzierżawy po potwierdzeniu ciała. Akceptacja: `dotnet test tests/Horyzont.Integration.Tests --filter "FullyQualifiedName~TeleportFlow"` oraz `dotnet test game` (reguła wspólnego runnera).
- [ ] **C5. Tankowanie i fotele.** Podłączenie `RefuelRules`, test dwóch kolejnych tankowań z jednego stosu, `ShipLifeSupportTests` na fotelach K5-25. Akceptacja: `dotnet test Horyzont.sln --filter "FullyQualifiedName~Refuel|FullyQualifiedName~ShipLifeSupport"`. Brak serwerowego `lifeSupport` z K6-34 → zatrzymać krok, nie dublować.
- [ ] **C6. Awarie, właściwości, alokacje.** Akceptacja: `dotnet test Horyzont.sln --filter "FullyQualifiedName~TeleportFlow|FullyQualifiedName~RefuelPersistence|FullyQualifiedName~LogisticsConservation|FullyQualifiedName~LogisticsAllocation"`.
- [ ] **C7. Bramka Opus C.** Test `TeleportTransitionTests.AllTerminalPaths_ReleaseLeasesViewersGate`. Akceptacja: `dotnet test Horyzont.sln --filter "FullyQualifiedName~Teleport|FullyQualifiedName~Cargo|FullyQualifiedName~Refuel|FullyQualifiedName~ShipLifeSupport"` + sprawdzenie końca etapu.

### Etap D — klient i minimalny interfejs (najpierw wygląd statyczny)

- [ ] **D1. Czerwone testy klienta.** `ClientTeleportStateTests`, `TeleportInteractionTests`. Akceptacja: `dotnet test tests/Horyzont.Client.Tests --filter "FullyQualifiedName~Teleport|FullyQualifiedName~Cargo"`.
- [ ] **D2. Stan i interakcje.** Brak predykcji skoku; neutralne wejście i blokada intencji w przejściu; zamknięcie paneli kontenerów. Akceptacja jak D1.
- [ ] **D3. Panele (dane przykładowe, potem podłączenie).** Panel nazwy, widok przejścia, siatka ładowni 32/64/96, wynik tankowania z serwera; teksty PL/EN. Akceptacja: `dotnet test game` + `dotnet test tests/Horyzont.Client.Tests --filter "FullyQualifiedName~LogisticsText|FullyQualifiedName~Teleport"`. Zrzuty bez okna do oceny właściciela; bez akceptacji wyglądu nie podłączać animacji.
- [ ] **D4. Odbiór i przegląd końcowy Opus.** Test `TeleportFlowTests.TwoClients_TeleportCargoRefuelRestart_ConservesItems`. Pełne sprawdzenie końca etapu; uzupełnić ZASADY l. 29 (D17) i dopisać regułę „przejście teleportu = przygotowanie przed op 7” do §2.

## Sprawdzenie na końcu etapu

```powershell
dotnet build Horyzont.sln
dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"
dotnet format Horyzont.sln --verify-no-changes
```

Po C i D dodatkowo (bez okna; najpierw sprawdzić, czy wspólny runner nie wykonuje zadania gry):

```powershell
python tools/engine/build.py smoke
dotnet test game
```

Diff `tests/Horyzont.Core.Tests/Golden/TerrainJournals/`, `TerrainJournalGoldenCorpusTests.cs` i `Hashing/DeterminismTests.cs` od commitu A0 ma być pusty.

## Zasady dla wykonawcy (Sonnet 5.5)

- Nie dodawać numerów spoza D2; brak numeru w planie = zatrzymanie i pytanie w PR.
- Nie zmieniać `UndoLog` ani formatu op 7/op 21/op 30; zmiana appliera tylko D14/D17.
- Applier nie czyta `ShipStates`, treści ani terenu (ZASADY §4); wartości z treści niesie operacja.
- Rekordy zmieniać przez `with`, nie przez konstruktor (lekcja K6-34 `VitalsStep`).
- Żeton: przyjęcie zawsze; odmowa tylko po skanie (wynik wywołania K6-36); bramka i walidator nigdy.
- Nie uruchamiać gry z oknem; po dwóch nieudanych podejściach do tego samego testu eskalacja z testem i logiem (ZASADY §12).
- Każdy test odmowy z poprawnym bliźniakiem; testy właściwości z progiem pokrycia.
- Nie aktualizować złotych skrótów ani pinów, by ukryć regresję; ponowne przypięcie tylko z opisem w PR.

## Koordynacja

| Pakiet | Granica |
| --- | --- |
| K6-30 | Stan bloku, intencja 29, kontenery bloków, `CheckChestConsistency` (uogólniane) |
| K6-32 | Ładownia jako źródło składników (przekazane, D15) |
| K6-34 | `Bulk`, `lifeSupport` (D18); generator w przestrzeni i podtrzymanie zależne od paliwa — DO USTALENIA-9 |
| K6-35 | Rejestr bramki, `InTransition`, pole przejścia w 48, ustawienia 46/23/47, kolejność `DeathService` |
| K6-36 | Kompozycje postawienia/rozbiórki z kosztem, wysypanie ładowni przy zawaleniu, wynik „odmowa kosztuje żeton”, źródła budowania |
| K5-22 | Dzierżawy, widzowie, gotowość LOD 0 |
| K5-25 | Role foteli; teleport z fotela odrzucony |
| K6-38 | Intencje 31/32, odmowa 50, rodzaj stanu 6, `BlockFunctionKind` 18, PV 23 |
| K6-39 | Ekran ustawień (`TeleportAll`, darmowe paliwo) i docelowe panele; PV 24 |
| K6-40 | Boty: teleport i tankowanie w pętli przetrwania |

## Ryzyka

| Ryzyko | Ograniczenie |
| --- | --- |
| Trzy plany deklarują PV 22 | D2: `main` + 1 przy scaleniu; korekta K6-38/K6-39 |
| Planeta celu wygasa między op 7 a ciałem | Dzierżawa do potwierdzenia ciała (D10), test integracyjny |
| Dane bez kolizji → spadanie | Gotowość = LOD 0 + sonda + kapsuła; test `LoadedWithoutCollision` |
| Stara poza po zmianie strefy/statku | D13, anulowanie |
| Paliwo dodane bez kosztu | D17: op 21 ostatnia, sprawdzona przed pozostałymi; test awarii |
| Pamięć podręczna par nieaktualna | Unieważnianie w `Applied`, test odrzuconej złożonej |
| Sztaby przechodzą przez teleport | Test treści dla każdej sztaby |
| Ładownia niedostępna w kosmosie zaskoczy graczy | Jawny tekst + DO USTALENIA-7 |
| Skan źródeł paliwa kosztowny | Indeks przestrzenny, żeton po skanie |
| Uzupełnienie ładowni przy starcie duże | ≤ 1024 komórki na operację, raz, idempotentnie |

## DO USTALENIA dla właściciela

1. **Koszt użycia teleportu.** Propozycja: brak (spec).
2. **Kto nadaje nazwę.** Propozycja: jak konfiguracja skrzyni (sprawdzić w A0); bez prywatnych teleportów w v1.
3. **Odmowa „brak pary”.** Propozycja: `InvalidParameters` z czytelnym tekstem klienta; alternatywa: dopisać do spec `TeleportNotPaired = 59`.
4. **Teleport na statku.** Propozycja: tylko statek zaparkowany w ramie planety; z fotela odrzucony.
5. **Trzeci teleport.** Propozycja: para = dwa najwcześniej nazwane (`NamedTick`, `BlockRef`); usunięcie awansuje następnego.
6. **Dobrowolne anulowanie przejścia.** Propozycja: brak w v1 (czekanie ≤ 60 s); przycisk wymagałby rozszerzenia intencji 30.
7. **Paliwo i ładownia.** (a) Wydajność ogniwa: propozycja 100 jednostek (zbiornik 1000); (b) nadwyżka ostatniego ogniwa przepada; (c) źródła: plecak + skrzynie + ładownie (pasek? spec mówi „plecak”); (d) dostęp do ładowni w ramie układu — v1 nie, osobne zadanie.
8. **Poziomy ładowni i źródła.** Dodać bloki t2/t3 teraz (odblokowanie K8-54)? Ładownia jako źródło budowania K6-36 i cel `QuickStack`? Propozycja: rzemiosło i paliwo tak, budowanie i `QuickStack` nie.
9. **Przekazane z K6-34.** Generator tlenu na statku w przestrzeni i podtrzymanie życia zależne od paliwa. Propozycja: nie w K6-37.
10. **Budżet przygotowań.** 32 globalnie, 4 na tick — zatwierdzić albo zmierzyć.
11. **Istniejące ładownie.** Uzupełnienie przy starcie (propozycja) czy leniwie przy pierwszym otwarciu.
12. **Kolejność PV.** K6-37 = 22, K6-38 = 23, K6-39 = 24 przy tej kolejności scalania; poprawić plany K6-38/K6-39.

## Czego nie sprawdzono (niezweryfikowane)

- Implementacja K6-35 (rejestr bramki, układ pola w 48, kolejność `DeathService`) i K6-36 (kompozycje, wynik żetonu) — tylko plany.
- Serwerowe podłączenie `lifeSupport`/`HazardTracker` (K6-34 C) i role foteli K5-25.
- Reguła wejścia gracza na pokład zaparkowanego statku (rama po wyjściu z teleportu wewnątrz statku).
- Kto może konfigurować blok (właściciel czy każdy) w `ResolveConfig`.
- Trwałość `WorldState.CurrentTick` między restartami (zakładana dla `NamedTick`).
- Ścieżki klienta (`ChestSession`, `InventoryInteraction`), położenie plików CSV tekstów.
- Wysyłka migawek po fazie `Compose`.
- Czy `ContainerReach`/subskrypcje obsługują już gospodarza `ConstructBlock` dowolnego rodzaju (zakładane, sprawdzane dla skrzyń).
- Wyniki testów, wydajność i wygląd — nic nie uruchamiano.

## Odbiór

- [ ] Para działa na jednej planecie i między planetami; trzeci teleport bez pary; pary przetrwają restart.
- [ ] `Bulk` w plecaku, pasku lub wyposażeniu blokuje (47); `TeleportAll` znosi.
- [ ] Przejście czeka na kolizję; po 60 s anulowanie przy źródle; wszystkie ścieżki zwalniają dzierżawy, widzów i bramkę.
- [ ] Teleport nie resetuje zdrowia, jedzenia ani kombinezonu (op 7 v1).
- [ ] Ładownie 32/64/96, poprawny dostęp, trwała zawartość; stare bloki dostały kontener raz.
- [ ] Tankowanie zużywa ogniwa atomowo; darmowe paliwo je pomija.
- [ ] Podtrzymanie życia na rzeczywistych fotelach.
- [ ] Stare dzienniki, korpus i złote skróty bez zmian.
- [ ] Bramki Opus zamknięte, CI zielone, wygląd oceniony przez właściciela.

## Odstępstwa i uwagi z realizacji

Plan przygotowany przez odczyt; implementacja nie rozpoczęta. Po każdym etapie wykonawca dopisuje commit, wyniki komend, werdykt Opus i rozstrzygnięcia DO USTALENIA.

## Przegląd planu (Opus 5.5)

Data: 2026-10-17. Podstawa: plan Codexa (`plan-k6-37-codex.md`), worktree `k6-35` (odczyt, bez gita, bez budowania), poprawione plany K6-35/K6-36. Werdykt: **GO z poprawkami** (14 poprawek; P1–P5 istotne dla poprawności).

| # | Co zmieniono | Dlaczego |
| --- | --- | --- |
| P1 | D17: op 21 jako ostatnia nieodwracalna składowa sprawdzana przed pozostałymi; `UndoLog` bez zmian | `CheckShipFuelAdd` (l. 1521) nie zależy od innych składowych; wzorzec `TryApplyCompound` i ZASADY l. 29; mniej chronionego kodu niż cofanie `ShipStateRecord` i znaczników |
| P2 | D20: komunikat 48 = `PlayerProgressState`; pole `Transition` uogólnia „stan odradzania” K6-35 | Codex mylił komunikat 48 z odmową 48 (`InTransition`) |
| P3 | D21: usunięty format 2 `IntentSubmit` z `ExpectedParentSeq` | Kanał niezawodny uporządkowany + bramka + walidacja względem bieżącej pozy wystarczają; mniej zmian protokołu |
| P4 | D2, nagłówek: konflikt PV 22 z planami K6-38/K6-39; `BlockFunctionKind` to treść | `BlockFunctionKind.cs` l. 3; trzy plany deklarują 22 |
| P5 | D10, D11: po op 7 osiadanie przez `FollowReparent`; dzierżawa `PlanetLeaseReason.Teleport` do potwierdzenia ciała | `ServerCharacters` l. 429–494; bez tego planeta mogła wygasnąć przed ciałem, a `GroundWaitDeadline` zrzucić gracza |
| P6 | D9: przyjęcie bez operacji (wzorzec `PropHitIntent`) | `ServerSimulation.Submit` l. 176–189; Codex nie opisał, co zwraca przyjęta intencja |
| P7 | D4: `NamedTick` zamiast `OpId` z `LastOpId.Next` | Brak sprzężenia z numerem nadawanym dopiero w `TryApplyAndJournal` |
| P8 | D5: pary liczone leniwie, unieważniane w `Applied` | Brak kroków indeksu w `UndoLog` |
| P9 | D15: ładownia jako źródło rzemiosła; uogólniony indeks przestrzenny | K6-32 l. 79 przekazał to do K6-37; `BuildSources`/`ChestsInChunk` znają tylko skrzynie |
| P10 | D14: dostęp do ładowni tylko w ramie planety (v1) | `ContainerReach` l. 51–75 liczy w ramie planety |
| P11 | D16: 15 m statek–stacja (K5-24) vs 20 m kontenery–stacja; żeton po skanie przez wynik K6-36 | `GameRules.RefuelRangeMeters = 15`; spec l. 160 |
| P12 | D14, B3: tylko poziom 1 ładowni w treści; sloty z treści w op 35 | `blocks.json` l. 27 |
| P13 | Testy: bliźniaki odmów, progi pokrycia, pin na własnej fiksturze; usunięty test „zewnętrznej mutacji” kontenerów gracza (pusty: kontenery gracza są prywatne) zastąpiony testami bramki | Lekcje K6-31/K6-34 |
| P14 | Komendy: `tests/Horyzont.Integration.Tests` dla przepływu; sekcja zasad wykonawcy | Projekty testów sprawdzone w `tests/` |

Sprawdzone bez uwag: intencja 30 i odmowa 47 wolne i nazwane w spec; rodzaje stanu 4/7 i kontener 5 zarezerwowane; `ItemSink.Fuel`, `ItemFlags.Bulk`, `MaxTeleportNameBytes = 32`, kodek `TeleportNameConfig`; brak wolnych operacji 30–49 po K6-35.
