# K6-39 · Interfejs i ustawienia świata

| | |
| --- | --- |
| Prowadzi | Gemini 3.8 Flash, myślenie High (plan projektu l. 140); etap B — patrz DO USTALENIA-1 |
| Testy akceptacyjne | Sonnet 5.5, przed implementacją (wykonawca ich nie zmienia bez zgody, AGENTS.md pkt 2) |
| Przegląd | Opus 5.5: A1, B1, B5, C4 i każda zmiana `src/Horyzont.Core/`; Sonnet 5.5: końcowy PR (ZASADY §9: pakiet Gemini → Sonnet); wygląd ocenia właściciel |
| Zależy od | K6-32, K6-33, K6-34, K6-35, K6-36, K6-37, K6-38 (scalone do `main`) |
| Gałąź | `pakiet/k6-39-interfejs-i-ustawienia`, osobny worktree utworzony z `main` po scaleniu K6-38 |
| Protokół | K6-34 = 19, K6-35 = 20, K6-36 = 21, K6-37 = 22, K6-38 = 23 (uzgodnione 2026-10-17) → K6-39 = **24**. Reguła: `ProtocolVersion` bazy + 1 w chwili scalenia, nigdy < 22 |
| Kamień milowy | K6 Przetrwanie |
| Odbiór ze specyfikacji | Każda akcja z sekcji „Rozgrywka” dostępna z interfejsu (spec l. 441); ustawienia świata działają i przetrwają restart |

**Cel:** ekran ustawień świata (przy tworzeniu i w trakcie gry), intencja 33 z uprawnieniami administratora sprawdzanymi przez serwer, trzy brakujące konsumenty ustawień (mnożnik stosów, mnożnik surowców, natężenie zagrożeń) oraz domknięcie interfejsu istniejących mechanik Przetrwania.
Rekord ustawień, operacja 46, blob 23, komunikat 47 i tag skrótu `0x57` dostarcza K6-35 — K6-39 ich nie projektuje od nowa.
Dokument przygotowano wyłącznie z odczytu (worktree `k6-35`, bez gita, budowania i testów). Nazwy nowych plików i testów są propozycjami.

## Uzgodnienie numerów (Opus, 2026-10-17, `K6-37-40-reconciliation.md`)

- Kolizja PV 22 rozstrzygnięta: K6-34 = 19, K6-35 = 20, K6-36 = 21, K6-37 = 22, K6-38 = 23, **K6-39 = 24** (reguła „bazowe + 1” nadal nadrzędna; literał przypina B1).
- K6-39 bez zmian numerów: intencja 33 (format 1 zapisany jawnie — K6-36 wprowadza format 2 tylko dla intencji 1/3, wersja per kod, nie globalna), odmowa 56, ogon `CanEdit` w 47 bez bajtu wersji per komunikat (`MessageCodec` ma jedną wersję). Brak kolizji z 30–32/34 i 47/48/50–55/57/58.
- `BlockFunctionKind`: K6-37 `Teleport = 17`, K6-38 `Planter = 18` — katalog akcji K6-39 (D3/D4) odwołuje się do nazw, nie do wartości.
- Konsumenci ustawień bez duplikacji: K6-36 (podparcie, darmowe budowanie), K6-37 (`TeleportAll`, darmowe paliwo); K6-39 tylko testuje ich w C4. Plony poza mnożnikiem surowców — zgodnie z K6-38 DO USTALENIA-9.

## Kontekst i stan sprawdzenia

Źródła: `AGENTS.md`, `docs/ZASADY.md` (całość), `docs/spec/03-przetrwanie.md` l. 168–185 (ustawienia), 296–347 (intencje, odmowy, sieć), 349–365 (zapis), 438–442 (pakiety); `docs/plan/plan-projektu.md` l. 136–141; plany K6-34 i K6-35 w `docs/plans/`; plany Codexa K6-37/K6-38 (`AgentTools/logs/results/plan-k6-3{7,8}-codex.md`); ustalenia przeglądu Opus planu K6-36 (2026-10-16). Planu K6-36 nie ma w `docs/plans/` bazy — jego numeracja pochodzi z planu K6-35 l. 251 i przeglądu Opus (**niezweryfikowane w pliku planu K6-36**).

| Fakt (worktree `k6-35`) | Plik | Konsekwencja |
| --- | --- | --- |
| `ProtocolVersion = 18` | `Core/Protocol/ProtocolConstants.cs` l. 7 | Baza nie zawiera K6-34…K6-38; nie zaczynać pracy na tej gałęzi (D2) |
| `OperationKind`: 1–7, 20–23, 30–37, 40–45, 47–49; brak 38, 39, 46 | `Core/Operations/OperationKind.cs` | 46 przydzielone K6-35; K6-39 nie dodaje operacji |
| `IntentCode`: 1–3, 10, 13–15, 20–25, 27–29; jedna stała `CurrentFormatVersion = 1` | `Core/Serialization/IntentCodec.cs` l. 15–38 | 33 wolne; 26 = K6-34, 30 = K6-37, 31/32 = K6-38, 34 = K6-36 |
| `RejectReason`: …21, 30, 31, 36, 37, 39, 40–46, 49, 53, 54, 57 | `Core/Intents/RejectReason.cs` | 56 wolne i nazwane w spec (`NotAdmin`) |
| `MessageType` 40–46, 48, 49 zajęte; 47 = K6-35 | `Core/Protocol/MessageType.cs` | **Zakres 40–49 jest po K6-35 pełny** — K6-39 nie może dodać komunikatu Przetrwania (D12) |
| `MessageCodec`: jedna wersja formatu dla wszystkich typów (`CurrentFormatVersion = 1`, l. 25, 79) | `Core/Serialization/MessageCodec.cs` | Zmiana treści komunikatu 47 = podbicie `ProtocolVersion`, bez bajtu wersji per typ |
| `BlobKind`: 1–5, 10–12, 20–22, 25; 23/24 = K6-35 | `Core/Serialization/BlobKind.cs` | K6-39 nie dodaje blobu |
| `GameServerHooks.OnIntent`: tryb bezpieczny → `ServerReadOnly` przed `Submit` | `Server/GameServerHooks.cs` l. 133–151 | Tryb bezpieczny działa dla 33 bez zmian |
| `ServerSimulation.Submit`: walidator → łańcuch resolverów (`_crafting`/`_functional`/`_survival`/`IntentResolver`); token zużywany tylko przy przyjęciu (l. 238) | `Core/Simulation/ServerSimulation.cs` l. 151–254 | 33 potrzebuje własnej gałęzi przed łańcuchem (D11) |
| `IntentValidator.BucketFor` zwraca `null` dla nieznanej intencji; `HasValidParameters` wyznacza kubełek | `Core/Simulation/IntentValidator.cs` l. 96, 127–137 | Bez wpisu 33 w obu miejscach: `InvalidParameters` albo `NullReferenceException` (`!` w l. 238) |
| `ServerRuntime.IsLocalPlayer(player)` porównuje endpoint z `ServerRuntimeOptions.LocalClientEndpoint` | `Server/ServerRuntime.cs` l. 206–210 | Gospodarz solo/host rozpoznawalny po stronie serwera |
| `HandshakeValidator` l. 36–44: tożsamość Steam tylko przez endpoint Steam P2P; UDP i klient lokalny = `Dev` (ZASADY l. 48) | `Net/Session/HandshakeValidator.cs` | Tożsamość `Dev` jest deklaracją — nie może nadawać praw (D8) |
| `ServerConfig` bez listy administratorów; serwer dedykowany nasłuchuje wyłącznie UDP (`ToListenEndpoint`) | `Server/ServerConfig.cs` | Dziś żaden zdalny gracz serwera dedykowanego nie ma wiarygodnej tożsamości → lista działa dopiero ze Steam P2P (D9) |
| `ConsoleCommandProcessor.Execute`: `switch` poleceń, akcje przez `Request*` | `Server/Console/ConsoleCommandProcessor.cs` l. 57–106 | Polecenie `settings` jako lokalna droga administratora serwera dedykowanego (D10) |
| `WorldListPanel.CreateRequested : Action<ulong?>` (tylko ziarno); `GameUiCoordinator.LaunchLocal` buduje `LaunchOptions`; `GameComposition` l. 333 przekazuje `() => NewWorld(...)` | `game/src/UI/…`, `game/src/Boot/GameComposition.cs` l. 333, 750–754 | Droga formularz → `LaunchOptions` → fabryka nowego świata istnieje |
| Fabryka nowego świata wołana tylko przy braku pliku świata | `Server/ServerRuntime.cs` l. 60, `ServerRuntime.Start.cs` l. 86 | Ustawienia tworzenia nie nadpiszą istniejącego zapisu (D7) |
| `WorldCreator.Create(path, WorldState, …)` zapisuje przekazany stan | `Persistence/WorldCreator.cs` l. 23 | Ustawienia w `WorldState` przed `Create` trafiają do pierwszego zapisu, jeśli K6-35 zapisuje blob 23 w `WorldCreator` (A0) |
| `.MaxStack` czytane bezpośrednio w 11 miejscach: Core `CraftingRules` l. 293, `FunctionalBlockRules` l. 431, 507–510, `PropRules` l. 152, `StationSettlements` l. 43, `SurvivalRules` l. 158, 267; Server `InventoryService` l. 390; klient `ClientInventory` l. 201, `InventoryInteraction` l. 265; `ContentLoader` l. 462, 596 (walidacja treści) | jw. | Mnożnik stosów nie ma dziś żadnego konsumenta; potrzebny jeden punkt obliczania także po stronie klienta (D17) |
| Applier: `ItemTransferOp` i `ItemCreateOp` odrzucają `cel + liczba > limit` | `Core/Simulation/OperationApplier.cs` l. 561, 619 | Po obniżeniu mnożnika przeniesienie całego ponadlimitowego stosu do pustego slotu zostałoby odrzucone — reguła resolvera w D17 |
| `YieldLedger` (reszta urobku per gracz i materiał), `MiningYield` | `Core/Simulation/YieldLedger.cs`, `SurvivalRules.cs` l. 262–267 | Mnożnik surowców terenu przed wyznaczeniem całych sztuk (D18) |
| `HazardField.Rates(..., float intensity)` z obcinaniem 0,5–2; `GameRules.HazardIntensity` nie istnieje w tej bazie | `Core/Survival/HazardField.cs` l. 10–32 | Źródło natężenia: ustawienia świata zamiast `GameRules` (D19); stan K6-34 do potwierdzenia w A0 |
| Slot plecaka w wyposażeniu nie przyjmuje przedmiotu („until a package changes the size of the backpack”) | `Core/Items/EquipmentLayout.cs` l. 11–35 | Zmiana rozmiaru plecaka (spec intencja 20) nie ma mechaniki ani właściciela — DO USTALENIA-6 |
| Testy gry: gdUnit4Net w `game/tests/`, komenda `dotnet test game/Horyzont.Game.csproj --settings game/.runsettings` z `GODOT_BIN` | plan K2-08 l. 283 | Poprawione komendy testów scen (Codex używał `dotnet test game`) |
| Etykieta „wkrótce” w `InventoryScreen.cs` i `game/locale/items.csv` | grep | Do usunięcia tylko tam, gdzie mechanika działa (D4) |

## Zakres

1. Formularz ustawień nowego świata (solo i host) oraz przekazanie rekordu do pierwszego zapisu świata.
2. Ekran ustawień uruchomionego świata z pauzy: odczyt dla wszystkich, edycja dla administratora.
3. Intencja 33 `WorldSettingsIntent`, odmowa 56 `NotAdmin`, polityka administratorów (gospodarz + lista Steam serwera dedykowanego), polecenie konsoli `settings`.
4. Bajt `CanEdit` w komunikacie 47 (per odbiorca) i rozsyłanie 47 po każdej op 46 z jednego subskrybenta `Applied`.
5. Konsumenty: mnożnik stosów (serwer i predykcja klienta), mnożnik surowców (teren i obiekty generatora), natężenie zagrożeń.
6. Macierz akcji Przetrwania: każda intencja gracza 20–33 ma osiągalne wejście w interfejsie, odpowiedź i odświeżenie; ujednolicone odmowy, blokady wejścia i fokus.
7. Lokalizacja PL/EN.

Poza zakresem: ponowna implementacja śmierci, nagrobków, kosztów budowy, podparcia, teleportów, tankowania i upraw (właściciele K6-35…K6-38); mechanika zmiany rozmiaru plecaka (DO USTALENIA-6); presety trudności spoza spec; głód i najazdy (zarezerwowane); ustawienia klienta, sterowanie, dostępność (K9-61); serwer dedykowany ze Steam (K9-59); boty i testy kamienia milowego (K6-40); zarządzanie listą administratorów przez sieć.

## Decyzje

- **D1. Jeden właściciel rekordu.** `WorldSettings`, op 46 v1, blob 23 v1, komunikat 47, tag `0x57`, `WorldSettingsService` i odczyt/zapis w `meta.world_settings` należą do K6-35 (jego D4). K6-39 nie tworzy drugiego rekordu w `GameRules`, konfiguracji klienta ani JSON. Nazwy pól i wartości enumów biorę z zatwierdzonego K6-35: flagi (bit 0 `TeleportAll`, bit 1 darmowe paliwo, bit 2 budowanie bez kosztów, bity 3–4 głód/najazdy = 0), mnożniki `ushort` w setnych (surowce 50–300, stosy 100–300, zagrożenia 50–200), `BuildingIntegrityMode` (`On = 1`, `Relaxed = 2`, `Off = 3`), `DeathPenalty` (`Mild = 1`, `Normal = 2`, `Hard = 3`). Kosmetycznych zmian kontraktu nie wykonywać.

- **D2. Baza = `main` po K6-38.** A0 zapisuje commit bazy, `ProtocolVersion`, obecność 46/23/47/`0x57`, intencji 26/30/31/32/34 i odmów 47/48/50/51/52/55/58. Gdy K6-35 nie dostarczył pełnego rekordu (właściciel odrzucił jego DO USTALENIA-1): **stop**, eskalacja do Opus z przeplanowaniem (wariant: K6-39 przejmuje 46/23/47 w pełnym kształcie D4 K6-35) — nie implementować tego milcząco.

- **D3. Bez presetów.** Formularz grupuje pola spec, ma „Przywróć domyślne”; zestaw różny od domyślnego opisany jako „Własne”. Presety łatwy/normalny/trudny wymagają zmiany spec (DO USTALENIA-4).

- **D4. Pola zarezerwowane i odrodzenie.** Głód i najazdy: nieaktywne kontrolki „Zarezerwowane”, zawsze 0 w rekordzie. Punkt odrodzenia ustawia łóżko (K6-30/K6-35), nie formularz. Etykiety „wkrótce” usuwać tylko przy działającej mechanice.

- **D5. Jeden model edytora.** Czysty `WorldSettingsEditor` (Horyzont.Client, bez Godota): stan potwierdzony, szkic, walidacja tą samą funkcją co Core (`WorldSettings` K6-35 — walidacja zakresów i bitów). Przy tworzeniu zwraca rekord do `LaunchOptions`; w grze wysyła intencję 33. Zmiana kontrolki nie zmienia symulacji.

- **D6. Ustawienia w pierwszym zapisie.** `LaunchOptions.NewWorldSettings` (`WorldSettings?`, `null` = domyślne) → `GameComposition.NewWorld(..., settings)` ustawia rekord w `WorldState` przed `WorldCreator.Create`. To stan początkowy świata, nie operacja (bez wpisu w dzienniku), tak jak ziarno i układ gwiezdny. Ustawienie stanu użyje API K6-35 (np. `WorldStateBuilder`/właściwość inicjalizowana); brak takiego API → nowa publiczna ścieżka w Core tylko po Opus (C2). Serwer dedykowany tworzy nowy świat z wartościami domyślnymi; zmiana później poleceniem `settings` (D10).

- **D7. Istniejący świat zachowuje ustawienia.** Fabryka nie jest wołana, gdy plik istnieje; `LaunchOptions.NewWorldSettings` jest wtedy ignorowane (log informacyjny, nie błąd). Ponowne uruchomienie przez `_lastLaunch with {...}` (odzysk z kopii, checkpoint) nie może nadpisać zapisu — test.

- **D8. Administratora wyznacza wyłącznie serwer.** `IsAdministrator(player)` = `ServerRuntime.IsLocalPlayer(player)` **albo** (`PlayerState.Identity.Platform == Steam` **i** tożsamość jest na liście `ServerConfig.Administrators`). Tożsamość `Dev` nigdy nie nadaje praw z listy (ZASADY l. 48: deklaracja). `PlayerId`, nazwa, metadane lobby i flagi klienta nie nadają praw. Uprawnienia nie trafiają do dziennika, zapisu ani skrótu; powtórka op 46 ich nie sprawdza.

- **D9. Lista administratorów = konfiguracja procesu dedykowanego.** `ServerConfig.Administrators` (lista napisów `steam:<SteamID64>`), walidowana w `ServerConfigLoader.TryLoad`: tylko `steam:`, liczba dziesiętna `ulong` > 0, bez duplikatów, najwyżej 64 wpisy; wpis `dev:` lub inny = błąd konfiguracji z czytelnym komunikatem. Przekazywana przez `ServerRuntimeOptions` do polityki. Zmiana wymaga restartu. Dziś serwer dedykowany jest tylko UDP, więc lista zacznie działać ze Steam P2P (K9-59); test pokrywa ją przez `InMemoryNetwork.CreateTransport(..., fakeSteamId)`.

- **D10. Droga administratora serwera dedykowanego: konsola.** Polecenie `settings` (wypisanie rekordu) i `settings set <pole> <wartość>` (pola: `teleport-all`, `free-fuel`, `free-building`, `resources`, `stacks`, `hazards` w setnych albo dziesiętnie z kropką, `integrity on|relaxed|off`, `death mild|normal|hard`). Jeśli K6-35 dodał już polecenie — użyć go. Wykonanie na wątku głównym przez kolejkę jak `RequestCheckpoint` (ZASADY l. 23), dalej `WorldSettingsService.Apply` → op 46. Błędna wartość → komunikat, bez operacji.

- **D11. Intencja 33: pełny rekord, walidacja w Core, uprawnienie przez wstrzykniętą politykę.**
  - `WorldSettingsIntent(WorldSettings Settings)`; ciało = dokładnie kodowanie rekordu op 46 v1 (wspólna funkcja zapisu/odczytu z K6-35, bez kopii kodu).
  - Core dostaje interfejs `IWorldAdministrators { bool IsAdministrator(PlayerId player); }` przekazany do `ServerSimulation` (parametr opcjonalny konstruktora; `null` = nikt nie jest administratorem). Implementację `WorldAdministrators` (D8/D9) daje Server.
  - Kolejność w `IntentValidator.Validate`: gracz/online → parametry (zakresy, bity, enumy → `InvalidParameters` 4) → kubełek (`RateLimited` 3) → uprawnienie (`NotAdmin` 56). Tryb bezpieczny → `ServerReadOnly` 21 w `GameServerHooks` (bez zmian).
  - Kubełek: istniejący `Markers` (4/s, seria 8; `GameRules` l. 39–42) — rzadka akcja administracyjna; bez nowego pola `GameRules`. `BucketFor` i `HasValidParameters` muszą znać 33.
  - Rozstrzygnięcie w `ServerSimulation.Submit`: osobna gałąź **przed** `_crafting`/`_functional`/`_survival` (działa też bez treści Przetrwania). Rekord równy bieżącemu → `AcceptedWithoutOperation` (precedens `PropHitIntent`, token zużyty); inaczej `WorldSettingsSetOp` przez `TryApplyAndJournal`.
  - Odmowy nie zużywają tokenu (K1-03; odmowy 33 są tanie — bez skanów).

- **D12. `CanEdit` jako ogon komunikatu 47.** Zakres komunikatów 40–49 jest pełny, a ekran musi wiedzieć, czy pokazać edycję (zdalny administrator Steam nie jest `IsHostLocal`). Na końcu `WorldSettingsState` dopisać bajt `CanEdit` (0/1; inna wartość → `ProtocolViolationException`), wyliczany per odbiorca z D8. Rekord, op 46, blob 23 i skrót bez zmian. Zmiana treści 47 jest pokryta podbiciem `ProtocolVersion` (jedna wersja formatu komunikatów, `MessageCodec` l. 25). Klient nie wnioskuje uprawnień sam; serwer i tak sprawdza.

- **D13. Rozsyłanie 47 z jednego miejsca.** Subskrybent `ServerSimulation.Applied` (`WorldSettingsReplicator` w Server) wysyła 47 do wszystkich graczy w grze po każdej zastosowanej op 46 — niezależnie, czy przyszła z intencji, konsoli, czy API K6-35. Przy wejściu kolejność K6-35/spec l. 338 (kontenery → 47 → 48). Jeśli K6-35 wysyła 47 z `WorldSettingsService.Apply`, przenieść wysyłkę do subskrybenta (jedna droga, brak podwójnej wysyłki — test liczy komunikaty).

- **D14. Potwierdzenie serwera, bez optymizmu.** Klient trzyma stan potwierdzony (z 47), szkic i co najwyżej jedno oczekujące żądanie (`ClientSeq`). „Zastosuj” zablokowane w trakcie żądania; odmowa zachowuje szkic i pokazuje powód; rozłączenie czyści stan sesji; brak automatycznego ponawiania.

- **D15. Równoczesna edycja: ostatnia przyjęta wygrywa.** 47 przychodzące w trakcie edycji pokazuje baner konfliktu i wymaga „Wczytaj aktualne” przed wysłaniem. Bez rewizji rekordu i bez nowego kodu odmowy. Ryzyko nadpisania przy jednoczesnym wysłaniu przyjęte jawnie.

- **D16. Zmiana nie przepisuje historii.** Istniejące stosy, koszty zapisanych bloków, nagrobki, jedzenie i trwające przejścia nie są przeliczane. Zmiana trybu podparcia nie wywołuje zawaleń (K6-36). Applier op 46 nie dotyka innych części stanu.

- **D17. Mnożnik stosów — jedna funkcja, serwer i klient.**
  - `EffectiveStackLimit.Of(ItemDef def, in WorldSettings settings)` w Core (`Content/` albo `Items/`): `def.MaxStack == 1` → 1; inaczej `min(ushort.MaxValue, floor(MaxStack × StackMultiplier / 100))`, co najmniej `MaxStack`. Arytmetyka całkowita (`int`), bez `float`.
  - Wszystkie odczyty `.MaxStack` z tabeli Kontekstu (poza `ContentLoader` — walidacja treści używa limitu bazowego) przechodzą przez funkcję; dochodzą miejsca dodane przez K6-35…K6-38 (śmierć, zwroty, tankowanie, plony). Operacje nadal niosą gotowy limit; applier nie czyta ustawień.
  - Klient (`ClientInventory`, `InventoryInteraction`) liczy tę samą funkcję z potwierdzonego 47 — inaczej predykcja łączenia rozjedzie się z serwerem.
  - Ponadlimitowe stosy po obniżeniu mnożnika: łączenie do celu niepustego tylko do `max(0, limit − cel)`; przeniesienie **całego** stosu do pustego slotu dozwolone z `ToStackLimit = max(limit, liczba)` (to nie łączenie, spec l. 185); przeniesienie części do pustego slotu najwyżej `limit` sztuk. Zamiana (`ItemSwapOp`) bez zmian.
  - Test architektury: brak `.MaxStack` w `src/` poza `ContentLoader`, `ItemDef` i `EffectiveStackLimit` (skan źródeł; lista dozwolonych plików w teście).

- **D18. Mnożnik surowców tylko w źródłach „urobek z terenu i obiektów”** (spec l. 195).
  - Teren: `MiningYield.Amount(...) × ResourceMultiplier / 100` przed dodaniem reszty z `YieldLedger` — ułamki przenoszą się jak dziś.
  - Obiekty generatora (`PropRules` l. 152): ilość wpisu × mnożnik z resztą w osobnym rejestrze reszt per (gracz, `ItemTypeId`), z tym samym etapowaniem (`Stage…`/`Commit` po zastosowaniu operacji, jak `_props!.Commit()` w `Submit` l. 233–236). Reszta w pamięci, poza dziennikiem — jak reszta terenu. Alternatywa „floor, minimum 1” — DO USTALENIA-5.
  - Nie mnożyć: wyniku rzemiosła i stacji, zwrotów budowy, odzysku nagrobka, przenoszeń, łupów, plonów (do decyzji DO USTALENIA-5), przedmiotów startowych.
  - Wynik trafia do `ItemCreateOp` jako liczba — powtórka nie czyta ustawień.

- **D19. Natężenie zagrożeń z ustawień świata.** Wywołujący `HazardField.Rates` (krok kombinezonu w `VitalsService`) przekazuje `settings.HazardIntensity / 100f`; `GameRules.HazardIntensity` (jeśli K6-34 je dodał) usunąć — jedno źródło. Mnoży drenaż, nie obrażenia przy zerze ani pojemność (K6-34 D9, D16). **Jedyny pisarz stanu postaci pozostaje `VitalsService` przez `UpdatePlayerVitals`** (ZASADY l. 33, K6-34 l. 370 i D22): zmiana ustawienia nie modyfikuje vitals ani kombinezonu bezpośrednio, działa od następnego ticku.

- **D20. Interfejs składa istniejące mechaniki.** Katalog akcji `SurvivalUiActions` (Horyzont.Client) mapuje każdą intencję gracza 20–33 na wejście (klawisz, przycisk, interakcja) i panel. Brak mechaniki u właściciela = blokada zgłaszana w PR, nie dopisywanie mechaniki w K6-39.

- **D21. Blokada wejścia i fokus.** Panel ustawień jest modalny przez istniejący `IGameplayInputGate` (`GameUiCoordinator`, `MenuController`); śmierć (K6-35) i przejście (K6-37) zamykają panele kontenerów, ale nie panel ustawień (nie wykonuje intencji przedmiotów).

## Ustawienia i układ ekranu

| Grupa | Pole (wg K6-35) | Kontrolka | Zakres | Domyślnie |
| --- | --- | --- | --- | --- |
| Zasoby | mnożnik surowców | suwak + pole, krok 0,05 (zapis w setnych) | 0,50–3,00 | 1,00 |
| Zasoby | mnożnik stosów | suwak + pole, krok 0,05 | 1,00–3,00 | 1,00 |
| Zagrożenia | natężenie zagrożeń | suwak + pole, krok 0,05 | 0,50–2,00 | 1,00 |
| Śmierć | kara | Łagodna / Normalna / Twarda + opis utraty | — | Normalna |
| Budowanie | wytrzymałość | `On` / `Relaxed` / `Off` z podpisami PL/EN | — | `On` |
| Budowanie | budowanie bez kosztów | przełącznik | — | wył. |
| Transport | `TeleportAll` | przełącznik | — | wył. |
| Transport | darmowe paliwo | przełącznik | — | wył. |
| Zarezerwowane | głód, najazdy | nieaktywne, „Zarezerwowane” | — | wył. |

Pole liczbowe przyjmuje setne (np. 1,37); suwak ma krok 0,05. Parsowanie i formatowanie kulturą `InvariantGlobalization` + separator z lokalizacji UI; wartości poza zakresem są błędem walidacji (bez cichego obcinania). Pod karą: dokładny zakres utraty (D3 K6-35) i informacja, że twarda kara nie usuwa postaci. Pod wytrzymałością: informacja, że zmiana nie zawali budowli od razu. Pod stosami: istniejące stosy zostają całe.
Przyciski: „Utwórz świat” / „Zastosuj”, „Przywróć domyślne” (tylko szkic), „Anuluj”. Szablon wyglądu ocenia właściciel na zrzutach stanów przykładowych (D5 etapu D).

## Kontrakty chronione i numeracja

### Przydziały K6-39

| Rodzaj | Numer | Uwagi |
| --- | --- | --- |
| Intencja | **33 `WorldSettingsIntent`**, format 1 | Spec l. 317; kodek przez wspólną funkcję rekordu K6-35 |
| Odmowa | **56 `NotAdmin`** | Spec l. 319 |
| Komunikat | **47 `WorldSettingsState`: ogon `CanEdit` (1 B)** | Bez nowego numeru (zakres 40–49 pełny) |
| `ProtocolVersion` | **bazowe + 1; planowo 24** (K6-37 = 22, K6-38 = 23) | Nigdy < 22; B1 zapisuje dokładną wartość, test kontraktu przypina literał |
| Operacja / blob / tag skrótu / migracja | **brak nowych** — 46, 23, `0x57` z K6-35; bez migracji | Zmiana któregokolwiek = powrót do Opus i przeplanowanie |

### Zajęte przez innych (nie używać)

Operacje 38/39/46, bloby 23/24, komunikat 47 (treść v1), odmowa 48, tagi `0x54`/`0x57` — K6-35. Intencja 34, odmowy 51/52/55/58, `CompoundOp` v2 — K6-36 (wg K6-35 l. 251, **niezweryfikowane w planie K6-36**). Intencja 30, odmowa 47 — K6-37. Intencje 31/32, odmowa 50 — K6-38. Odmowa 59 — wolna, nie zużywać.

### Ścieżki i przeglądy

| Ścieżka | Zmiana | Wymaganie |
| --- | --- | --- |
| `Core/Intents/WorldSettingsIntent.cs`, `RejectReason.cs`, `Serialization/IntentCodec.cs` | 33 v1, 56 | Opus B1/B5; stare intencje (także rozszerzone przez K6-36) zachowują bajty |
| `Core/Protocol/ProtocolConstants.cs`, `Messages/WorldSettingsState.cs`, `Serialization/MessageCodec.cs` | PV, ogon `CanEdit` | Opus B1; złote bajty 47 przed i po |
| `Core/Simulation/IWorldAdministrators.cs`, `IntentValidator.cs`, `ServerSimulation.cs` | polityka, walidacja, gałąź 33 | Opus (nie format, ZASADY l. 74) |
| `Core/.../EffectiveStackLimit.cs`, resolverzy z D17/D18, `YieldLedger` lub nowy rejestr reszt | konsumenty | Opus B5; korpus złoty i `DeterminismTests` bez zmian |
| `Core/Operations/`, `OperationApplier.cs`, `Hashing/`, `Checkpoints/`, `Persistence/Migrations/` | **żadnych zmian** | Konieczność zmiany = stop i eskalacja |
| `Core/World/WorldState.cs` (ustawienie rekordu przy tworzeniu) | tylko jeśli K6-35 nie daje API | Opus C2 |
| `Server/` (`WorldAdministrators`, `ServerConfig*`, `ServerRuntimeOptions`, konsola, `WorldSettingsReplicator`) | | Sonnet; bezpieczeństwo uprawnień również w materiale Opus B5 |
| `Client/`, `game/` | | Sonnet (końcowy PR) |

Zamrożone: początek `ClientHello`, cały `HandshakeReject` (ZASADY l. 76).

### Skrót stanu

Bez zmian w `StateHasher`. Testy: dwa rekordy różniące się jednym polem → różne skróty (pokrycie K6-35 `0x57`); `CanEdit`, szkic, lista administratorów nie wpływają na skrót; świat utworzony z ustawieniami i wczytany ponownie ma ten sam skrót; checkpoint + ogon z op 46 = ciągłe wykonanie. Pin liczbowy tylko na własnej zamrożonej fiksturze z A0 (lekcja K6-31 `db779bd`).

## Projekt — pliki

| Obszar | Pliki (nowe = *) |
| --- | --- |
| Core | *`Intents/WorldSettingsIntent.cs`, `Intents/RejectReason.cs`, `Serialization/IntentCodec.cs`, `Protocol/ProtocolConstants.cs`, `Protocol/Messages/WorldSettingsState.cs`, `Serialization/MessageCodec.cs`, *`Simulation/IWorldAdministrators.cs`, `Simulation/IntentValidator.cs`, `Simulation/ServerSimulation.cs`, *`Content/EffectiveStackLimit.cs` (lub `Items/`), `Simulation/{CraftingRules,FunctionalBlockRules,PropRules,StationSettlements,SurvivalRules}.cs`, `Simulation/YieldLedger.cs` lub *`Simulation/PropYieldLedger.cs`, wywołujący `HazardField` |
| Server | *`Settings/WorldAdministrators.cs`, *`Settings/WorldSettingsReplicator.cs`, `ServerConfig.cs`, `ServerConfigLoader.cs`, `ServerConfigJsonContext.cs`, `ServerRuntimeOptions.cs`, `Console/ConsoleCommandProcessor.cs`, `Inventory/InventoryService.cs` l. 390, `WorldSettingsService.cs` z K6-35 (lokalizacja wg K6-35 l. 272 — A0) |
| Client | *`UI/WorldSettingsEditor.cs`, *`Settings/ClientWorldSettings.cs`, *`Settings/WorldSettingsSession.cs`, *`UI/SurvivalUiActions.cs`, `Inventory/ClientInventory.cs`, `Inventory/InventoryInteraction.cs`, `Replication/ClientReplication.cs` |
| game | `Boot/LaunchOptions.cs`, `Boot/GameComposition.cs` (l. 333, 750–754), *`Boot/GameComposition.Settings.cs`, `UI/WorldListPanel.cs` + scena, `UI/GameUiCoordinator.cs`, `UI/PauseMenu.cs` + scena, *`UI/WorldSettingsPanel.cs`, *`scenes/UI/WorldSettingsPanel.tscn` (lokalizacja scen wg istniejących — A0), panele `Client/Inventory/*`, `Client/Survival/VitalsHud.cs`, `Client/Tools/BlockGhostPreview.cs` |
| Teksty | *`game/locale/world_settings.csv`, `ui.csv`, rejestracja w `game/project.godot` (jak istniejące csv) |
| Testy | `tests/Horyzont.Core.Tests/Settings/`, `tests/Horyzont.Server.Tests/Settings/`, `tests/Horyzont.Client.Tests/Settings/`, `tests/Horyzont.Integration.Tests/Settings/`, `tests/Horyzont.Persistence.Tests/Settings/`, `tests/Horyzont.Architecture.Tests/`, `game/tests/UI/` |

**Przepływy.** Nowy świat: formularz → `WorldSettings` → `LaunchOptions.NewWorldSettings` → `NewWorld` → `WorldCreator.Create` (blob 23) → start → 47 przy wejściu. Zmiana w grze: szkic → 33 w `IntentSubmit` → `GameServerHooks` (tryb bezpieczny) → `Submit` (parametry, kubełek, `NotAdmin`) → op 46 → dziennik → `Applied` → 47 do wszystkich → `IntentReply`. Konsola: `settings set` → kolejka → `WorldSettingsService.Apply` → op 46 → `Applied` → 47.

## Testy akceptacyjne — przed kodem (Sonnet 5.5)

Zasady: każdy test odmowy zaczyna od przyjętego odpowiednika (kontrola dodatnia); żaden test nie może przejść na pustej kolekcji (asercja liczności przed pętlą); test „bez skutków” porównuje liczbę wpisów dziennika i skrót przed/po.

| Test | Kryterium |
| --- | --- |
| `WorldSettingsIntentCodecTests.Code33_GoldenBytes` | Złote bajty 33 v1 = nagłówek + bajty rekordu op 46 v1; round-trip |
| `WorldSettingsIntentCodecTests.ReservedBitsRangesEnums_Throw` | Bit 3/4, stosy 99/301, surowce 49/301, zagrożenia 49/201, enum 0/4 → odrzucone |
| `WorldSettingsStateCodecTests.CanEditTail_GoldenBytes_AndInvalidByteThrows` | 0/1 przyjęte, 2 → `ProtocolViolationException` |
| `ProtocolContractTests.ProtocolVersion_IsDecidedValue` | Literał z B1 (planowo 24) |
| `WorldSettingsValidationTests.Order_ParamsThenRateThenAdmin` | Nie-administrator z błędnym rekordem → 4; z poprawnym → 56; administrator bez tokenu → 3 |
| `WorldSettingsFlowTests.Admin_ChangesRecord_JournalsOp46` | Kontrola dodatnia: wpis 46, nowy skrót, `IntentReply` z `OpId` |
| `WorldSettingsFlowTests.NonAdmin_Refused56_NoJournalNoHashChange` | Po kontroli dodatniej innego gracza |
| `WorldSettingsFlowTests.SameRecord_AcceptedWithoutOperation` | Brak wpisu, token zużyty |
| `WorldSettingsFlowTests.ReadOnly_Refused21` | Tryb bezpieczny |
| `WorldSettingsFlowTests.NoPolicy_EveryoneRefused` | `ServerSimulation` bez `IWorldAdministrators` → 56 |
| `WorldAdministratorsTests.LocalHost_IsAdmin` / `RemoteDev_IsNot` | Endpoint lokalny vs UDP/`Dev` |
| `WorldAdministratorsTests.ListedSteamViaP2P_IsAdmin_DevWithSameValue_IsNot` | `fakeSteamId` w `InMemoryNetwork`; `Dev` o tej samej wartości → brak praw |
| `ServerConfigLoaderTests.Administrators_RejectsDevDuplicatesGarbage` | `dev:1`, duplikat, `steam:abc`, 65 wpisów → błąd; poprawna lista przyjęta |
| `ConsoleSettingsTests.Set_AppliesOp46OnMainThread_InvalidValueChangesNothing` | |
| `WorldSettingsReplicationTests.TwoClientsLateJoinReconnect_SeeCurrent_CanEditPerRecipient` | Host `CanEdit = 1`, gość 0; dokładnie jedno 47 na op 46 na odbiorcę |
| `WorldCreationSettingsTests.CustomSettings_InFirstSave_SameHashAfterLoad` | |
| `WorldCreationSettingsTests.ExistingWorld_IgnoresNewWorldSettings` | Utwórz z S1, uruchom z S2 → S1; także ścieżka odzysku z checkpointu |
| `WorldSettingsPersistenceTests.CheckpointAndTail_RestoreSameRecordAndHash` | |
| `EffectiveStackLimitTests.Table` | `MaxStack` 1 → 1 przy 300; 100×150 → 150; 999×300 → 2997; 64×101 → 64 (floor, nie mniej niż bazowy) |
| `StackMultiplierTests.Decrease_KeepsStacks_WholeMoveToEmptyAllowed_MergeCapped` | Stos 250 przy limicie 100: przeniesienie całości do pustego = OK; łączenie do 90 → przenosi 10; część 150 do pustego → 100 |
| `StackMultiplierTests.ClientPredictionUsesSameLimit` | `ClientInventory.IsFull` zgodne z serwerem przy mnożniku 300 |
| `StackLimitArchitectureTests.MaxStack_ReadOnlyThroughEffectiveStackLimit` | Skan `src/`: lista dozwolonych plików; test wykrywa celowo dodany odczyt w fiksturze |
| `ResourceMultiplierTests.TerrainFraction_CarriesRemainder` | 0,5 × 3 kopnięcia po 1 → 1 sztuka + reszta 0,5 |
| `ResourceMultiplierTests.Props_Remainder_And_NoMultiplyOnCraftRefundTombstone` | |
| `HazardIntensitySettingTests.DrainScalesDamageAtZeroDoesNot` | Drenaż 50 vs 200 różni się 4×; obrażenia przy zerze równe; vitals zmienia tylko `VitalsService` |
| `WorldSettingsEditorTests.DefaultsRangesReset_Cancel_DraftOnly` | |
| `WorldSettingsSessionTests.RefusalDisconnectRemoteChange_SinglePending` | |
| `SurvivalUiActionsTests.EveryPlayerIntent20To33_HasEntry` | Oczekiwana lista intencji wpisana w test (14 kodów; 26–32 wg scalonych pakietów); brak wpisu = porażka |
| `game/tests/UI/WorldSettingsSceneTests` (gdUnit) | Tworzenie solo/host, pauza, tylko odczyt gościa, modalność i fokus |
| `WorldSettingsLocalizationTests.AllKeys_PlAndEn` | Każdy klucz z `world_settings.csv` i nowych `ui.csv` ma obie kolumny niepuste |

## Kroki

Każdy krok: najpierw wskazany test (czerwony z oczekiwanym powodem), potem minimalny kod, potem zielony test i commit Conventional Commits. **[Opus]** = obowiązkowy przegląd przed kolejnym krokiem.

### Etap A — baza, testy, modele klienta

- [ ] **A0. Baza.** Z `main` po K6-38: zapisać commit, `ProtocolVersion`, obecność kontraktów z D2, lokalizację `WorldSettingsService`, sposób wysyłki 47, API ustawienia rekordu przy tworzeniu, zapis bloba 23 w `WorldCreator`, polecenie konsoli K6-35, wątek `ConsoleCommandProcessor.Execute`, istnienie `GameRules.HazardIntensity`, nowe odczyty `.MaxStack` z K6-35…K6-38.
  Test: `WorldSettingsBaselineTests.FrozenFixture_HashPin` (własna fikstura, pin przed zmianami).
  Komenda: `rg -n "ProtocolVersion =|WorldSettings|NotAdmin|\.MaxStack|HazardIntensity|IntentCode" src` oraz `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~WorldSettingsBaseline"`.
- [ ] **A1. Czerwone testy akceptacyjne [Opus].** Sonnet dodaje testy z tabeli i szkielety typów (bez logiki). Opus zatwierdza: podział z K6-35 (D1), politykę D8–D11, `CanEdit` (D12), D17–D19, wartość PV.
  Komenda: `dotnet build Horyzont.sln`; lista oczekiwanych porażek w opisie PR.
- [ ] **A2. Model edytora.** `WorldSettingsEditor` (D5, tabela ekranu).
  Komenda: `dotnet test tests/Horyzont.Client.Tests --filter "FullyQualifiedName~WorldSettingsEditor"`.
- [ ] **A3. Model sesji.** `ClientWorldSettings`, `WorldSettingsSession` (D14, D15).
  Komenda: `dotnet test tests/Horyzont.Client.Tests --filter "FullyQualifiedName~WorldSettingsSession"`.

### Etap B — kontrakty, uprawnienia, konsumenty

- [ ] **B1. Bajty i wersja [Opus].** Czerwone złote bajty 33, ogon 47, PV; Opus zatwierdza dokładne PV na scalonej bazie.
  Komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~WorldSettingsIntentCodec|FullyQualifiedName~WorldSettingsStateCodec|FullyQualifiedName~ProtocolContract"` (oczekiwane czerwone).
- [ ] **B2. Kodek.** `WorldSettingsIntent`, `IntentCodec` (33), `RejectReason.NotAdmin = 56`, ogon 47, `ProtocolVersion`. Aktualizacja wszystkich testów, które przypinają PV.
  Komenda: jak B1 (zielone) + `dotnet test tests/Horyzont.Net.Tests`.
- [ ] **B3. Walidacja i polityka (Core).** `IWorldAdministrators`, wpis 33 w `HasValidParameters` i `BucketFor`, kolejność D11, gałąź w `Submit`.
  Komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~WorldSettingsValidation|FullyQualifiedName~WorldSettingsFlow"`.
- [ ] **B4. Serwer.** `WorldAdministrators`, `ServerConfig.Administrators` + loader + kontekst JSON, `ServerRuntimeOptions`, `WorldSettingsReplicator` (D13), polecenie `settings` (D10).
  Komenda: `dotnet test tests/Horyzont.Server.Tests --filter "FullyQualifiedName~WorldAdministrators|FullyQualifiedName~ServerConfigLoader|FullyQualifiedName~ConsoleSettings"`.
- [ ] **B5. Konsumenty [Opus po kroku].** `EffectiveStackLimit` + wszystkie miejsca (serwer, klient), reguła ponadlimitowych stosów, mnożnik surowców (teren, obiekty), natężenie zagrożeń.
  Komenda: `dotnet test tests/Horyzont.Core.Tests --filter "FullyQualifiedName~StackMultiplier|FullyQualifiedName~EffectiveStackLimit|FullyQualifiedName~ResourceMultiplier|FullyQualifiedName~HazardIntensitySetting|FullyQualifiedName~Determinism|FullyQualifiedName~TerrainJournalGoldenCorpus"` oraz `dotnet test tests/Horyzont.Architecture.Tests` i `dotnet test tests/Horyzont.Client.Tests --filter "FullyQualifiedName~StackMultiplier"`.
  Opus sprawdza: brak odczytu ustawień w applierze, brak zmian korpusu i złotych skrótów (`git diff <A0>..HEAD -- tests/Horyzont.Core.Tests/Golden tests/Horyzont.Core.Tests/TerrainJournalGoldenCorpusTests.cs` pusty), kompletność walidacji, jednego pisarza vitals.

### Etap C — tworzenie, zapis, replikacja

- [ ] **C1. Czerwone testy tworzenia i trwałości.**
  Komenda: `dotnet test tests/Horyzont.Persistence.Tests --filter "FullyQualifiedName~WorldCreationSettings|FullyQualifiedName~WorldSettingsPersistence"` (oczekiwane czerwone).
- [ ] **C2. Pierwszy zapis [Opus dla Core].** `LaunchOptions.NewWorldSettings`, `NewWorld(..., settings)`, ustawienie rekordu w `WorldState` przez API K6-35.
  Komenda: jak C1 (zielone).
- [ ] **C3. Replikacja i stan klienta.** `ClientReplication` → `ClientWorldSettings`; `GameComposition.Settings.cs`.
  Komenda: `dotnet test tests/Horyzont.Integration.Tests --filter "FullyQualifiedName~WorldSettingsReplication|FullyQualifiedName~WorldSettingsFlow"`.
- [ ] **C4. Skutki w grze i bramka C [Opus].** Testy integracyjne: kara przy następnej śmierci (K6-35), `TeleportAll` i darmowe paliwo przy następnym teleporcie/tankowaniu (K6-37), tryb podparcia bez samoczynnego zawalenia (K6-36), stosy i surowce. Tylko weryfikacja konsumentów właścicieli; brak konsumenta = blokada w PR.
  Komenda: `dotnet test Horyzont.sln --filter "FullyQualifiedName~WorldSettings|FullyQualifiedName~WorldCreationSettings"` + sprawdzenie końca etapu.

### Etap D — ekrany i integracja Przetrwania

- [ ] **D1. Czerwone testy scen i katalogu akcji.**
  Komenda: `dotnet test tests/Horyzont.Client.Tests --filter "FullyQualifiedName~SurvivalUiActions"` oraz `dotnet test game/Horyzont.Game.csproj --settings game/.runsettings --filter "FullyQualifiedName~WorldSettingsScene"` (zmienna `GODOT_BIN`).
- [ ] **D2. Formularz nowego świata.** `WorldListPanel.CreateRequested` → `Action<ulong?, WorldSettings>`; `GameUiCoordinator.LaunchLocal(world, seed, settings)`; zachować blokadę `_busy` i pojedynczy start.
  Komenda: `dotnet test game/Horyzont.Game.csproj --settings game/.runsettings --filter "FullyQualifiedName~WorldSettingsScene|FullyQualifiedName~MenuScene"`.
- [ ] **D3. Ustawienia w grze.** `PauseMenu` → `WorldSettingsPanel`; tylko odczyt przy `CanEdit = 0`; konflikt (D15); Esc przywraca fokus (D21).
  Komenda: jak D2 + `dotnet test tests/Horyzont.Client.Tests --filter "FullyQualifiedName~WorldSettingsSession"`.
- [ ] **D4. Katalog akcji i domknięcie paneli.** `SurvivalUiActions` dla intencji 20–33; brakujące przyciski/skróty w istniejących panelach; usunięcie „wkrótce” tylko przy działających mechanikach.
  Komenda: `dotnet test tests/Horyzont.Client.Tests --filter "FullyQualifiedName~SurvivalUiActions"`.
- [ ] **D5. Lokalizacja i wygląd.** `world_settings.csv`, rejestracja w `project.godot`; zrzuty stanów (tworzenie, gość, administrator, konflikt, odmowa) do oceny właściciela — wykonawca nie uruchamia gry z oknem (zrzuty z testów scen albo przez właściciela).
  Komenda: `dotnet test tests/Horyzont.Client.Tests --filter "FullyQualifiedName~WorldSettingsLocalization"`.
- [ ] **D6. Bramka końcowa.** `WorldSettingsAcceptanceTests.CreateChangeRestart_AllFieldsPreserved`; przegląd Sonnet; Opus zamyka uwagi Core.
  Komenda: sprawdzenie końca etapu + test dymny.

## Sprawdzenie na końcu każdego etapu

```
dotnet build Horyzont.sln
dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"
dotnet format Horyzont.sln --verify-no-changes
```

Po etapach C i D dodatkowo: `dotnet test game/Horyzont.Game.csproj --settings game/.runsettings` oraz `python tools/engine/build.py smoke`. Testy silnika lokalnie tylko, gdy wspólny runner nie wykonuje joba `game` (lokalny gdUnit przerywał zadania CI) — inaczej zostawić CI.

## Reguły dla wykonawcy (Gemini 3.8 Flash)

1. Nie zmieniać numerów, nazw pól rekordu ani bajtów z tabeli „Przydziały”; nie dodawać operacji, blobów, tagów ani migracji.
2. Nie edytować testów akceptacyjnych Sonnet; nie aktualizować złotych bajtów, pinów skrótu, korpusu terenu ani `DeterminismTests`, aby uzyskać zielony wynik.
3. Każdy plik w `src/Horyzont.Core/` = zatrzymanie po kroku i przegląd Opus przed następnym.
4. Uprawnienia sprawdza tylko serwer; klient nigdy nie wnioskuje praw sam (używa `CanEdit` z 47).
5. Applier nie czyta ustawień ani treści; ilości i limity liczy resolver i niesie operacja.
6. Vitals zmienia wyłącznie `VitalsService` przez `UpdatePlayerVitals`; konsola i sieć tylko na wątku głównym.
7. Logowanie wyłącznie `[LoggerMessage]`; walidacja zwraca `RejectReason`, nie rzuca.
8. Bez LINQ i alokacji w ścieżkach co tick (krok kombinezonu, `EffectiveStackLimit`).
9. Po dwóch nieudanych podejściach do tego samego testu: eskalacja do Sonnet 5.5 z testem, logiem i opisem prób (ZASADY §12).
10. Nie uruchamiać gry z oknem; nie uruchamiać testów silnika równolegle z CI.
11. Każdy fakt „niezweryfikowane” sprawdzić w A0 i zapisać wynik w „Odstępstwach”.

## Koordynacja

| Pakiet | Granica |
| --- | --- |
| K6-32/K6-33 | Bez zmian rzemiosła, stacji, jedzenia; tylko `EffectiveStackLimit` w ich resolverach |
| K6-34 | Natężenie z ustawień (D19); panele kombinezonu i generatora bez zmian mechaniki |
| K6-35 | Dostarcza rekord, 46/23/47, `0x57`, usługę i konsumenta kary; K6-39 dodaje 33, 56, `CanEdit`, replikator (D13) i ekran; przejmuje docelowy układ ekranu śmierci/nagrobka bez zmian logiki |
| K6-36 | Konsumuje tryb podparcia i budowanie bez kosztów; K6-39 tylko test integracyjny i wpięcie istniejącego podglądu |
| K6-37 | Konsumuje `TeleportAll` i darmowe paliwo; K6-39 tylko wpis w katalogu akcji |
| K6-38 | Akcje grządki w katalogu; decyzja o plonach w mnożniku (DO USTALENIA-5) |
| K6-40 | Dostaje katalog akcji i scenariusze ustawień; nie przejmuje testu 8 botów |
| K9-59 | Serwer dedykowany ze Steam P2P aktywuje listę administratorów z D9 |
| K9-61 | Ustawienia klienta, sterowanie, dostępność |

## Ryzyka

| Ryzyko | Ograniczenie |
| --- | --- |
| K6-35 bez pełnego rekordu | A0 stop i przeplanowanie (D2) |
| Kolizja PV (rozstrzygnięta: K6-37 = 22, K6-38 = 23) | Reguła „bazowe + 1”, literał zatwierdza B1 |
| Podszycie pod administratora (`Dev` po UDP) | D8: `Dev` nigdy z listy; test z tą samą wartością |
| Brak wpisu 33 w `BucketFor` → wyjątek w `Submit` | Reguła D11, test kolejności walidacji |
| Mnożnik stosów tylko w części resolverów lub tylko na serwerze | Test architektury skanujący `.MaxStack`, test predykcji klienta |
| Utrata przedmiotów / zablokowany stos po obniżeniu limitu | D17: całość do pustego slotu dozwolona, łączenie przycięte |
| Mnożnik 0,5 zeruje pojedyncze łupy obiektów | Rejestr reszt (D18) albo decyzja właściciela |
| Podwójna wysyłka 47 (usługa + replikator) | Jedno źródło (D13), test liczby komunikatów |
| Zawalenia po zmianie trybu | Reguła K6-36, test C4 |
| Pozorna kompletność UI | Katalog akcji z listą kodów w teście |
| Gemini w ścieżkach bezpieczeństwa i Core | Opus po każdym kroku Core; DO USTALENIA-1 |

## DO USTALENIA dla właściciela

1. **Wykonawca etapu B.** Polityka narzędzi wyklucza Gemini z prac bezpieczeństwa i reguł Core. Propozycja: etapy B i C2 — Sonnet 5.5; Gemini: A2–A3, C3, D. Alternatywa: całość Gemini z przeglądem Opus po każdym kroku Core.
2. **Potwierdzenie K6-35 D4** (pełny rekord w K6-35). Propozycja: tak; inaczej przeplanowanie K6-39.
3. **`CanEdit` w 47** (D12). Propozycja: tak. Alternatywa bez zmiany 47: edycja tylko dla `IsHostLocal`, a zdalny administrator ze Steam dopiero w K9-59 (wtedy ten bajt).
4. **Presety trudności.** Propozycja: brak (spec ich nie definiuje), „Własne” + domyślne.
5. **Mnożnik surowców:** (a) obiekty generatora z resztą per (gracz, typ) — propozycja; alternatywa floor z minimum 1; (b) czy obejmuje plony K6-38 — propozycja: nie (plony to jedzenie, spec l. 195 wymienia „urobek z terenu i obiektów”).
6. **Większy plecak** (spec intencja 20: plecak w wyposażeniu zmienia rozmiar). Mechanika nie istnieje (`EquipmentLayout` l. 28–35), a K6-34 l. 84 odsyła ją do „K6-39/K8-54”. Propozycja: K8-54 (wymaga operacji zmiany rozmiaru kontenera — Core, nie Gemini); odbiór K6-39 wyłącza tę akcję jawnie.
7. **Serwer dedykowany:** nowy świat z domyślnymi ustawieniami, zmiana konsolą (D6, D10). Alternatywa: sekcja ustawień w `ServerConfig` czytana tylko przy tworzeniu.
8. **Kubełek intencji 33** = `Markers` (4/s). Alternatywa: osobny kubełek w `GameRules`.
9. **Wygląd** formularza i panelu — ocena właściciela na zrzutach.

## Czego nie sprawdzono (niezweryfikowane)

- Plan K6-36 (brak w `docs/plans/` bazy): intencja 34, odmowy 51/52/55/58 — z planu K6-35 i przeglądu Opus.
- Implementacje K6-34 (etapy B–D), K6-35…K6-38: API `WorldSettings`, `WorldSettingsService`, polecenie konsoli, wysyłka 47, zapis bloba 23 w `WorldCreator`, nowe odczyty `.MaxStack`.
- Ostateczne PV K6-37/K6-38: uzgodnione 22/23 (2026-10-17) — stąd „planowo 24”; literał i tak z bazy w B1.
- Czy `WorldCreator.Create` zapisuje świat w jednej transakcji SQLite.
- Wątek wykonania `ConsoleCommandProcessor.Execute`.
- Czy gdUnit4 adapter obsługuje `--filter "FullyQualifiedName~..."` (jeśli nie — uruchomić klasę testów przez filtr adaptera i zapisać w uwagach).
- Lokalizacja scen UI (`game/scenes/UI/` vs inny katalog) i proces importu `.csv` → `.translation`.
- Przebiegi komend, wydajność, wygląd — nic nie uruchamiano.

## Odbiór

- [ ] Rekord z formularza w pierwszym zapisie; istniejący świat (także po odzysku) zachowuje ustawienia.
- [ ] Administrator (host lokalny; Steam z listy w teście) zmienia ustawienia; gość dostaje 56 bez wpisu i zmiany skrótu; tryb bezpieczny 21.
- [ ] Konsola `settings` działa na serwerze dedykowanym.
- [ ] Dwaj klienci i późno dołączający widzą stan; `CanEdit` poprawne per odbiorca; jedno 47 na op 46.
- [ ] Każde aktywne pole ma sprawdzony konsument (stosy, surowce, zagrożenia — tu; kara, podparcie, darmowe budowanie, teleport, paliwo — test integracyjny właścicieli).
- [ ] Obniżenie mnożnika stosów nie dzieli ani nie blokuje stosów; predykcja klienta zgodna z serwerem.
- [ ] Restart, checkpoint i ogon dają ten sam rekord i skrót.
- [ ] Katalog akcji pokrywa intencje 20–33 (bez wyjętego plecaka, DO USTALENIA-6).
- [ ] PL/EN kompletne; modalność i fokus poprawne.
- [ ] Bez zmian w operacjach, applierze, skrócie, migracjach, korpusie złotym.
- [ ] Przeglądy Opus (A1, B1, B5, C4) i Sonnet zakończone; CI zielone; wygląd odebrany.

## Odstępstwa i uwagi z realizacji

Implementacja nie rozpoczęta. Wykonawca dopisuje po każdym etapie: commit, wyniki komend, werdykt przeglądu, wyniki A0 dla pozycji „niezweryfikowane”, rozstrzygnięcia DO USTALENIA.

## Przegląd planu (Opus 5.5)

Data: 2026-10-17. Podstawa: plan Codexa (`plan-k6-39-codex.md`), worktree `k6-35` (tylko odczyt). Werdykt: **GO z poprawkami** (12 poprawek; P1–P4 istotne dla poprawności).

| # | Co zmieniono | Dlaczego |
| --- | --- | --- |
| P1 | D11: admin w Core przez `IWorldAdministrators` (domyślnie odmowa), jawna kolejność walidacji, wpis 33 w `BucketFor`/`HasValidParameters`, gałąź przed resolverami | Codex: „jedna droga przez `WorldSettingsService`” zamiast `Submit`; bez wpisu kubełka `Submit` l. 238 rzuca NRE |
| P2 | D17: reguła ponadlimitowych stosów (całość do pustego slotu, łączenie przycięte) + predykcja klienta + test architektury | Applier l. 619 odrzuciłby przeniesienie stosu 250 przy limicie 100; klient l. 201/265 czyta `MaxStack` |
| P3 | D8/D9: lista tylko `steam:`, `Dev` nigdy; konsola jako droga dedykowana (D10) | ZASADY l. 48: `Dev` to deklaracja; serwer dedykowany jest tylko UDP |
| P4 | PV: „bazowe + 1, planowo 24” | K6-37 i K6-38 oba planują 22, a K6-39 zależy od obu |
| P5 | D12: `CanEdit` decyzją, nie opcją; zakres komunikatów 40–49 pełny; bez bajtu wersji per typ | `MessageType`, `MessageCodec` l. 25 |
| P6 | D13: wysyłka 47 z subskrybenta `Applied` | Konsola i intencja muszą rozsyłać tak samo, bez dubli |
| P7 | D18: reszta per (gracz, typ) dla obiektów | Floor przy 0,5 zeruje pojedyncze łupy |
| P8 | D19: jeden pisarz vitals, usunięcie `GameRules.HazardIntensity` | ZASADY l. 33, K6-34 D22 |
| P9 | Komendy gdUnit `dotnet test game/Horyzont.Game.csproj --settings game/.runsettings` | `dotnet test game --filter` nie odpowiada uprzęży K2-08 |
| P10 | Testy: kontrole dodatnie, złote bajty, liczności, katalog akcji z listą kodów | Ryzyko testów przechodzących pusto |
| P11 | Reguły dla wykonawcy Gemini; DO USTALENIA-1 (etap B → Sonnet) | Brak reguł w planie; polityka narzędzi |
| P12 | DO USTALENIA-6: plecak bez mechaniki i bez właściciela | `EquipmentLayout` l. 28–35, K6-34 l. 84 |

Sprawdzone bez uwag: 33 i 56 wolne i nazwane w spec; 46/23/47/`0x57` przydzielone K6-35; `IsLocalPlayer`, `ServerConfig*`, `ConsoleCommandProcessor`, `WorldListPanel.CreateRequested`, `LaunchLocal`, `NewWorld`, `WorldCreator` istnieją; fabryka wołana tylko przy braku pliku.
