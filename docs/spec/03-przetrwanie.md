# Specyfikacja podprojektu 3: Przetrwanie

> Kopia dokumentu z Claude Docs, stan na 2026-10-08. Wersja żywa: https://claude.ai/code/artifact/e7dd8a7c-e3c0-4cf3-8d8e-e4c8fcd02c03

## Kontekst i decyzje

Podprojekt 3 zamienia budowanie i lot w grę o przetrwanie: zbieranie, ekwipunek, rzemiosło, jedzenie, zagrożenia planet, śmierć i budowanie z zasobów z wytrzymałością. Każda zmiana przedmiotów jest atomową operacją dziennika, więc żaden przedmiot nie ginie ani się nie duplikuje. Może iść równolegle z podprojektem 2, bo z Kosmosu potrzebuje dopiero pakiet K6-37.

| Decyzja | Rozstrzygnięcie | Podstawa |
| --- | --- | --- |
| Ekwipunek | jednolite sloty ze stosami, bez wagi i bez siatki kształtowej; 40 slotów plus 10 na pasku, większe plecaki z bossów | Enshrouded ma same sloty, 24 bazowe i do 56 z plecakiem ([Hack The Minotaur](https://hacktheminotaur.com/enshrouded/enshrouded-best-backpacks-guide/)); waga w Palworld wymagała ponownego strojenia ([Palworld Wiki](https://palworld.wiki.gg/wiki/Carrying_capacity)) |
| Rzemiosło i budowanie | z ekwipunku i ze skrzyń w promieniu bazy od pierwszego dnia | „I think we all want crafting from containers” to jeden z najgłośniejszych wątków Valheima po 1.0 ([Steam](https://steamcommunity.com/app/892970/discussions/0/4327475551416122758)); mod z tą funkcją ma 30,1 tys. pobrań ([Thunderstore](https://thunderstore.io/c/valheim/p/LukaWolf/CraftingWithContainersTempFix/)) |
| Logistyka | surowce masowe nie przechodzą przez teleporty, wozi je ładownia statku; ustawienie świata `TeleportAll` | zakaz rudy w portalach to najczęstsza skarga na Valheima ([PCGamesN](https://www.pcgamesn.com/valheim/ore-portal)), a jego preset Relaxed go znosi ([XGamingServer](https://xgamingserver.com/blog/valheim-world-modifiers-server-settings/)) |
| Jedzenie | 3 sloty bufów, bez śmierci z głodu, efekt słabnie z czasem | Valheim nie pozwala umrzeć z głodu ([Valheim Wiki](https://valheim.fandom.com/wiki/Food)), w Enshrouded jedzenie jest „generalnie niewymagane” ([Mein-MMO](https://mein-mmo.de/en/2-of-the-best-survival-games-of-recent-years-are-fully-released-which-one-suits-me-valheim-or-enshrouded,1587673/)) |
| Presja przetrwania | zagrożenia planet zużywają zasoby kombinezonu | wzór systemu podtrzymywania życia w No Man's Sky ([NMS Wiki](https://nomanssky.fandom.com/wiki/Life_Support)) |
| Wytrzymałość | całkowitoliczbowy limit podparcia dla budowli na gruncie, statki tylko spójność, ustawienie świata Off, On, Relaxed | Valheim wiąże wytrzymałość z progresją materiałów ([Valheim Wiki](https://valheim.fandom.com/wiki/Building)); zawalenia z zaskoczenia to najostrzejsze skargi w gatunku ([Funcom](https://forums.funcom.com/t/entire-base-lost-stability-for-no-reason-hours-of-effort-lost/126712)) |
| Postać | stan gracza należy do świata, bez postaci przenoszonych między światami | serwer autorytatywny z dziennikiem nie przyjmuje stanu z pliku klienta, więc kopia postaci nie może duplikować przedmiotów |
| Śmierć | nagrobek z zawartością plecaka i paska, wyposażenie zostaje na postaci; kara jako ustawienie świata | napięcie powrotu po nagrobek bez utraty całego postępu |
| Trwałość przedmiotów | brak w EA; pole na dane instancji czeka w modelu | mniej tarcia, zgodnie z zasadą przenoszenia utrudnień do ustawień świata |

Zależności: operacje i dziennik (K1-03, K1-05), sesja (K1-07), adapter terenu (K2-09), budowle (K2-12), replikacja (K3-15, K3-16); z Kosmosu wiele terenów, lot i fotele dla K6-37 (K5-22, K5-25).

## Cel i zakres

Na bramce K6 grupa do 8 graczy przechodzi pełną pętlę przetrwania na czterech ciałach: zbiera, wytwarza, buduje bazę z zasobów, je, chroni się przed zagrożeniami, ginie i odzyskuje nagrobek. Po restarcie albo awarii serwera każdy przedmiot jest dokładnie w jednym miejscu.

W zakresie:

- przedmioty, stosy i kontenery: plecak, pasek, wyposażenie, skrzynie, ładownie, wejścia i wyjścia stacji, przedmioty leżące, nagrobki;
- bloki funkcyjne ze stanem: skrzynie, stacje, łóżka, teleporty, generator tlenu, grządki; drzwi i światła jako stan bloku;
- zbieranie: urobek z terenu, flora i obiekty z generatora, odrastanie, poziomy narzędzi;
- rzemiosło: receptury jako dane, odkrywanie, klucze postępu, poziomy warsztatu, piec, rafineria, kuchnia, fabrykator;
- stan postaci: zdrowie, wytrzymałość, trzy miejsca na jedzenie, odpoczynek, obrażenia od upadku;
- zagrożenia planet, zasoby kombinezonu, schronienie i generator tlenu;
- śmierć, nagrobek i odrodzenie;
- budowanie z zasobów, zwrot kosztów i wytrzymałość budowli;
- teleporty w parach, tankowanie z przedmiotów, szybkie odkładanie i uprawy;
- ustawienia świata oraz interfejs ekwipunku, rzemiosła i ustawień.

Poza zakresem EA: waga i siatki kształtów w ekwipunku, automatyzacja i prąd, handel z postaciami niezależnymi, zużycie narzędzi i pancerza, głód jako kara, postacie przenoszone między światami, uprawnienia gildii i najazdy na bazy. Głód i najazdy mają zarezerwowane ustawienia świata, a stos ma pole na dane instancji, więc te rozszerzenia nie wymagają migracji formatu przedmiotów.

## Rozgrywka

### Ekwipunek i kontenery

- Gracz ma plecak (40 miejsc), pasek szybkiego wyboru (10 miejsc) i wyposażenie: głowa, tors, nogi, plecak i 3 moduły kombinezonu.
- Większy plecak z wyposażenia dokłada miejsca do plecaka. Zdjęcie go wymaga, żeby te miejsca były puste.
- Skrzynie, ładownie statku, wejścia i wyjścia stacji, nagrobki i przedmioty leżące to też kontenery. Rozmiary są danymi, np. skrzynia drewniana 20 miejsc, skrzynia metalowa 40, ładownia 32, 64 albo 96 według poziomu.
- Kontener jest wspólny albo prywatny. Prywatny otwiera tylko właściciel, a nagrobek zawsze tylko właściciel.
- Szybkie odkładanie przenosi z plecaka do skrzyń w promieniu 10 m te rodzaje przedmiotów, które skrzynia już zawiera.
- Blok z niepustym kontenerem nie da się rozebrać. Zawalenie albo zniszczenie takiego bloku wysypuje zawartość jako przedmioty leżące.
- Przedmiot leżący podnosi się sam w promieniu 2 m, gdy jest miejsce. Znika po 60 minutach czasu serwera, chyba że leży w promieniu 20 m od budowli.

| Rodzaj przedmiotu | Domyślny maksymalny stos |
| --- | --- |
| Bloki budowlane | 999 |
| Surowce | 500 |
| Części i półprodukty | 200 |
| Amunicja | 200 |
| Jedzenie | 50 |
| Narzędzia, broń, pancerz, moduły | 1 |

### Zbieranie

- Kopanie terenu daje urobek z objętości usuniętego materiału i wydajności materiału z danych. Serwer liczy urobek przy rozstrzygnięciu intencji, a operacja niesie gotowe przedmioty, więc powtórka dziennika nie pyta terenu.
- Część ułamkowa urobku zostaje w pamięci serwera dla gracza i materiału, więc drobne kopanie też się sumuje.
- Drzewa, krzewy, skały i złoża powierzchniowe to obiekty generatora planety, deterministyczne z ziarna. Każdy ma punkty wytrzymałości; ostatnie uderzenie usuwa go i daje przedmioty.
- Flora odrasta po czasie z danych, liczonym w tickach serwera od zebrania, jeśli w promieniu 20 m nie ma budowli ani gracza; odrośnięcie zapisuje operacja serwera `PropRegrowOp`. Skały i złoża nie odrastają, a złoża w głębi terenu wystarczają na całą rozgrywkę.
- Każdy materiał terenu i obiekt ma wymagany poziom narzędzia: 0 to ręka, kolejne poziomy otwierają materiały i bossowie (podprojekt 5).
- Brak miejsca w plecaku nie blokuje zbierania: nadmiar ląduje jako przedmiot leżący pod nogami.

### Rzemiosło i stacje

- Receptura to dane: składniki, wynik, stacja i jej poziom, czas dla stacji z czasem oraz klucz postępu.
- Gracz odkrywa recepturę, gdy pierwszy raz trzyma któryś składnik i świat ma jej klucz postępu. Klucze postępu są wspólne dla świata i dają je bossowie (podprojekty 4 i 5).
- Składniki pochodzą z plecaka, paska i wspólnych kontenerów w promieniu 20 m od stacji: najpierw plecak, potem kontenery od najbliższego. Wynik trafia do plecaka, a gdy nie ma miejsca, rzemiosło jest odrzucane przed zużyciem składników.

| Stacja | Działanie |
| --- | --- |
| Ręce | proste przedmioty bez stacji |
| Warsztat | narzędzia, broń, pancerz i bloki, w tym bloki statku poziomu I; poziomy 1–4 przez ulepszenia postawione w promieniu 5 m |
| Piec | przetapianie rud na sztaby; czas i paliwo |
| Rafineria | ogniwa paliwowe dla statków i generatorów; czas i paliwo |
| Kuchnia | posiłki od razu, bez czasu |
| Fabrykator | moduły kombinezonu, części statku od poziomu II i broń energetyczna; odblokowuje go boss 2 |

Stacja z czasem przetwarza wejście jako czystą funkcję ticków: liczba gotowych sztuk wynika z ticku startu, czasu receptury, wejścia i paliwa. Nic nie tyka co klatkę; rozliczenie powstaje przy każdej interakcji ze stacją, a klient liczy pasek postępu tą samą funkcją. Tak samo generator tlenu: zużyte ogniwa wynikają z ticku startu i zawartości wejścia, a rozliczenie powstaje przy każdej interakcji z generatorem.

### Budowanie z zasobów

- Każdy blok ma koszt w przedmiotach i wersję kosztu. Postawienie bloku pobiera koszt z plecaka, paska i wspólnych kontenerów w promieniu 20 m od gracza.
- Budowanie nie wymaga stacji w pobliżu. Ustawienie świata „budowanie bez kosztów” wyłącza pobieranie i zwrot.
- Blok zapisuje wersję kosztu w bitach 21–24 stanu bloku. Rozbiórka zwraca 100% kosztu według tej wersji, więc zmiana kosztu w aktualizacji nie tworzy ani nie niszczy przedmiotów.

### Wytrzymałość budowli

Budowle na gruncie podlegają regule podparcia, a statki tylko regule spójności. Usunięcie bloku, które rozdzieliłoby statek na dwie części, jest odrzucane.

- Blok jest uziemiony, gdy SDF terenu w środku jego dolnej ściany wynosi najwyżej −0,25, czyli ściana jest zagłębiona w gruncie. Kierunek „w dół” to oś −Y budowli.
- Uziemiony blok ma podparcie równe maksimum swojego materiału. Pozostałe bloki biorą najlepsze podparcie od sąsiada minus stratę: w pionie od sąsiadów wzdłuż Y, w poziomie od sąsiadów wzdłuż X i Z. Strata zależy od materiału bloku podpieranego.
- Blok z podparciem poniżej minimum swojego materiału się zawala. Zawalenia kaskadowe są częścią tej samej operacji co zmiana, która je wywołała, także przy kopaniu terenu pod budowlą.
- Zawalony blok zwraca 100% kosztu jako przedmioty leżące, po jednym bycie na rodzaj przedmiotu w miejscu zawalenia.
- Podgląd budowania koloruje bloki według podparcia. Postawienie bloku bez podparcia jest odrzucane, a zmiana zawalająca więcej niż 10 bloków wymaga potwierdzenia.

| Materiał | Podparcie maks. | Minimum | Strata w pionie | Strata w poziomie | Wysięg w poziomie |
| --- | --- | --- | --- | --- | --- |
| Drewno | 100 | 10 | 1 | 12 | 7 bloków |
| Drewno rdzeniowe | 140 | 10 | 1 | 10 | 13 bloków |
| Kamień | 1000 | 100 | 1 | 450 | 2 bloki |
| Metal | 1500 | 20 | 1 | 40 | 37 bloków |

Ustawienie świata `Relaxed` dzieli straty przez dwa (zaokrąglenie w górę), a `Off` wyłącza regułę podparcia. Reguła spójności statków działa w każdym trybie.

Bloki, które straciły podparcie przez zmianę ustawienia albo treści, nie spadają same. Podgląd je oznacza, a pierwsza zmiana budowli, która ich dotyczy, pokazuje podgląd zawalenia i wymaga potwierdzenia.

### Stan postaci i jedzenie

- Bazowo postać ma 100 zdrowia i 100 wytrzymałości. Wytrzymałość zużywają bieg, skok, kopanie i ataki (podprojekt 4).
- Trzy miejsca na jedzenie dodają do maksimum zdrowia i wytrzymałości oraz do regeneracji. Głód nie zabija: bez jedzenia zostają wartości bazowe.
- Siła jedzenia słabnie z czasem jak w Valheimie: efekt = pełny efekt · r^0,3, gdzie r to ułamek pozostałego czasu. Daje to 92% przy 75% czasu, 81% przy 50%, 66% przy 25% i 50% przy 10%.
- Tego samego jedzenia nie da się zjeść drugi raz, dopóki zostało mu więcej niż 20% czasu.
- Odpoczynek: 20 s siedzenia albo leżenia pod dachem przy źródle ciepła daje +50% regeneracji wytrzymałości na 8 minut plus minutę za każdy poziom komfortu. Komfort dają bloki w promieniu 10 m według danych.
- Obrażenia od upadku liczą się z prędkości uderzenia, bo grawitacja ciał jest różna: 8 punktów za każdy m/s powyżej 11 m/s.

### Zagrożenia planet i kombinezon

Każdy rodzaj planety ma profil zagrożeń z danych, zależny od biomu i pory dnia. Każde zagrożenie zużywa jeden zasób kombinezonu w skali 0–100, a ustawienie świata mnoży jego natężenie od 0,5 do 2.

| Zagrożenie | Zasób kombinezonu | Przy zerze |
| --- | --- | --- |
| Zimno | Ogrzewanie | 2 zdrowia na sekundę |
| Żar | Chłodzenie | 2 zdrowia na sekundę |
| Toksyny | Filtr | 2 zdrowia na sekundę |
| Promieniowanie | Osłona | 3 zdrowia na sekundę |
| Próżnia | Tlen | 10 zdrowia na sekundę |

- Zasoby uzupełniają przedmioty, np. ogniwo grzewcze, a moduły kombinezonu zmniejszają zużycie wybranego zasobu albo zwiększają jego pojemność.
- Schronienie chroni przed zimnem, żarem i toksynami. Postać jest w schronieniu, gdy co najmniej 21 z 26 promieni o długości 16 m trafia w budowlę albo teren, w tym promień pionowo w górę; jaskinia też się liczy.
- Generator tlenu chroni przed próżnią w promieniu 10 m i zużywa ogniwa paliwowe. W schronieniu i przy generatorze zasoby kombinezonu się odnawiają.
- Siedzenie w statku daje pełne podtrzymywanie życia: zagrożenia nie działają, a zasoby się odnawiają.

### Śmierć i odrodzenie

- Przy zerowym zdrowiu w miejscu śmierci powstaje nagrobek. Jeśli wypadłby w terenie albo pod nim, serwer wynosi go na powierzchnię.
- Postać od razu odradza się przy swoim łóżku, a gdy go nie ma, w miejscu startu świata. Jedzenie znika, zdrowie wraca do bazowego, a zasoby kombinezonu są pełne.
- Łóżko ustawia punkt odrodzenia, gdy gracz na nim śpi albo wybierze je w menu bloku.
- HUD i mapa pokazują właścicielowi jego nagrobki. Opróżniony nagrobek znika.

| Kara za śmierć (ustawienie świata) | Co trafia do nagrobka |
| --- | --- |
| Łagodna | nic; przedmioty zostają przy postaci, ginie tylko jedzenie |
| Normalna (domyślna) | plecak i pasek; wyposażenie zostaje na postaci |
| Twarda | plecak, pasek i wyposażenie |

### Teleporty i logistyka

- Teleporty łączą się w pary po nazwie do 32 B, także między planetami. Trzeci teleport o tej samej nazwie nie ma pary.
- Przedmioty masowe (rudy, sztaby, surowce) nie przechodzą przez teleport, chyba że świat ma `TeleportAll`. Wozi je ładownia statku.
- Teleport między planetami trzyma gracza w stanie przejścia, aż serwer wczyta kolizję terenu docelowego. W przejściu gracz nie wykonuje intencji przedmiotów.
- Tankowanie przy stacji tankowania zużywa ogniwa paliwowe z plecaka i kontenerów w promieniu 20 m od stacji. Ustawienie „darmowe paliwo” z podprojektu 2 pomija ten koszt.

### Uprawy

- Grządka to blok ze stanem: nasiono, tick posadzenia i gatunek. Wzrost to czysta funkcja ticków z czasem z danych.
- Roślina rośnie tylko w świetle (słońce albo lampa uprawna) i w atmosferze; na próżni potrzebuje generatora tlenu.
- Zbiór dojrzałej rośliny daje plony i nasiona, a dzikie rośliny dają pierwsze nasiona.

### Ustawienia świata

Ustawienia zmienia administrator: gospodarz gry albo tożsamość z listy administratorów serwera dedykowanego. Każdą wartość można zmienić w trakcie gry.

| Ustawienie | Wartości | Domyślnie |
| --- | --- | --- |
| `TeleportAll` | wyłączone, włączone | wyłączone |
| Mnożnik surowców | 0,5–3 | 1 |
| Mnożnik stosów | 1–3 | 1 |
| Wytrzymałość budowli | `Off`, `On`, `Relaxed` | `On` |
| Kara za śmierć | Łagodna, Normalna, Twarda | Normalna |
| Natężenie zagrożeń | 0,5–2 | 1 |
| Darmowe paliwo | wyłączone, włączone | wyłączone |
| Budowanie bez kosztów | wyłączone, włączone | wyłączone |
| Głód | zarezerwowane | wyłączone |
| Najazdy na bazy | zarezerwowane | wyłączone |

Zmniejszenie mnożnika stosów nie dzieli istniejących stosów; nowy limit obowiązuje przy kolejnym łączeniu.

## Architektura i model danych

Przedmioty, kontenery i stany bloków żyją w Core jako stan świata, a każdą ich zmianę wykonuje wyłącznie `OperationApplier`; gra w Godocie tylko je pokazuje.

```
Przedmiot powstaje tylko w źródle i znika tylko w ujściu

Źródła: ItemCreateOp
  urobek z terenu i obiektów · wynik rzemiosła i stacji · plony z grządek
  zwrot kosztu bloku · łup ze stworów i bossów
      │
      ▼
Kontenery: ItemTransferOp · ItemSwapOp
  plecak, pasek, wyposażenie · skrzynie i ładownie · wejścia i wyjścia stacji
  przedmioty leżące · nagrobki
      │
      ▼
Ujścia: ItemDestroyOp
  składniki rzemiosła · zjedzone jedzenie · paliwo i ogniwa
  koszt postawionego bloku · uzupełnienie kombinezonu · zniknięcie leżących

CompoundOp: składowe jednej akcji to jeden wpis dziennika, całość albo nic
```

Operacja złożona wiąże źródło, przeniesienia i ujście jednej akcji, więc rzemiosło z pięciu skrzyń albo śmierć gracza to jeden wpis dziennika.

### Przedmioty i treść

- `ItemTypeId` (`ushort`) pochodzi z rejestru świata `item_types`, który rośnie tylko przy wczytaniu, jak rejestr bloków z K1-03. Klucz tekstowy wiąże go z treścią.
- `ItemDef` to dane treści: klucz, kategoria, maksymalny stos, flagi (`Bulk`, `Food`, `Tool`, `Weapon`, `Armor`, `Ammo`, `Placeable`), poziom narzędzia, statystyki jedzenia, zasób kombinezonu do uzupełnienia i miejsce wyposażenia.
- `ItemStack(ItemTypeId Type, ushort Count, ItemInstanceData? Instance)`. `Instance` jest w EA zawsze puste i czeka na trwałość albo ulepszenia.
- Treść gry (przedmioty, receptury, koszty bloków, stacje, profile zagrożeń, flora) to pliki JSON w `content/`. Wczytuje je `ContentLoader` w Core, a treść należy do kompilacji, więc obejmuje ją `BuildHash`.
- Walidacja treści przy starcie odrzuca usunięcie typu przedmiotu (wycofany typ dostaje flagę `Retired`), ponad 16 wersji kosztu bloku, nieznany składnik i przedmiot bez źródła.

### Kontenery

- `ContainerId` (`ulong`) przydziela alokator jak `EntityIdAllocator`, z licznikiem `next_container_id` w `meta`.
- `Container` ma identyfikator, rodzaj (`PlayerBag`, `Hotbar`, `Equipment`, `Chest`, `CargoHold`, `StationInput`, `StationOutput`, `Tombstone`, `DroppedItem`), gospodarza (gracz, blok budowli, byt trwały albo skrzynia miejsca `Poi` z podprojektu 5: planeta, miejsce i numer skrzyni), właściciela, dostęp (`Shared`, `Private`), sloty i wersję.
- Wersja rośnie o 1 przy każdej operacji dziennika, która zmienia kontener, więc jedna operacja to jedna delta dla klientów.
- Kontenery gracza powstają przy jego pierwszym wejściu do świata z podprojektem 3, także w świecie podniesionym migracją ze starszej wersji.

### Bloki ze stanem i byty trwałe

- Stan bloku funkcyjnego (`BlockEntity`) ma klucz (budowla, komórka) i dane zależne od funkcji: postęp stacji (tick startu, paliwo), właściciel łóżka, nazwa teleportu, stan generatora tlenu, roślina w grządce. Drzwi i światła to stan funkcji w bitach 25–27 stanu bloku z K1-03, który zmienia `BlockFunctionStateSetOp` (49).
- Przedmioty leżące (`DroppedItem`, 3) i nagrobki (`Tombstone`, 6) to byty trwałe z ramą rodzica, `ParentSeq`, pozą lokalną; ich kontener wskazuje je jako gospodarza. Stwory i pociski z podprojektu 4 nie są trwałe.

### Stan gracza

- `PlayerProgress` ma zdrowie, wytrzymałość, trzy miejsca na jedzenie (typ i tick zjedzenia), zasoby kombinezonu, punkt odrodzenia, odkryte przedmioty (zbiór bitów po `ItemTypeId`), własne nagrobki i koniec odpoczynku.
- Zdrowie, wytrzymałość i zasoby kombinezonu zmieniają się co tick bez dziennika, tak jak ruch, a zapisuje je checkpoint. Jedzenie, odkrycia, punkt odrodzenia i uzupełnienia kombinezonu idą przez dziennik.

Klucze postępu świata (`ProgressKeys`) to zbiór kluczy tekstowych zapisany w `meta`. Dodaje je `ProgressKeyGrantOp` (45) przy pokonaniu bossa (podprojekt 4) albo z komendy administratora.

### Reguły w Core

- Receptury (`RecipeDef`), krzywa jedzenia, przetwarzanie stacji, wzrost roślin i drenaż zagrożeń to czyste funkcje ticków i danych, wspólne dla serwera i klienta.
- `IntegrityEvaluator` liczy podparcie jako punkt stały propagacji maksimum, więc wynik nie zależy od kolejności. Uziemienie dostaje od adaptera terenu jako funkcję, a zmiana przelicza tylko obszar, którego dotyczy.
- Resolver ma dostęp do terenu i wylicza urobek, zbiór zawalonych bloków i miejsce nagrobka. Operacja niesie gotowe wyniki, więc applier nigdy nie pyta terenu.

## Operacje i dziennik

Każda akcja gracza, która dotyka przedmiotów, to jedna operacja złożona w jednym wpisie dziennika, więc awaria serwera nie zostawia jej w połowie.

| Kod | Operacja | Pola | Warunki wstępne i skutek |
| --- | --- | --- | --- |
| 30 | `CompoundOp` | do 4096 składowych i 160 KiB po zakodowaniu | bez zagnieżdżania; składowe stosowane po kolei, a niespełniony warunek którejkolwiek cofa wcześniejsze z dziennika cofania |
| 31 | `ItemTransferOp` | źródło (kontener, slot), cel (kontener, slot), typ, liczba, limit stosu celu | źródło ma ten typ w co najmniej tej liczbie; cel jest pusty albo ma ten typ, a suma nie przekracza limitu z operacji |
| 32 | `ItemSwapOp` | dwa sloty, oczekiwane typy | zamienia zawartość dwóch slotów |
| 33 | `ItemCreateOp` | kontener, slot, typ, liczba, limit stosu, źródło: `Harvest`, `Craft`, `Station`, `Crop`, `Refund`, `Loot`, `Admin`, `Recovered`, `Starter` | slot pusty albo z tym typem; suma nie przekracza limitu z operacji |
| 34 | `ItemDestroyOp` | kontener, slot, typ, liczba, ujście: `Craft`, `Station`, `Eat`, `Fuel`, `BuildCost`, `SuitRefill`, `Plant`, `Despawn`, `Admin`, `Ammo`, `Offering` | slot ma ten typ w co najmniej tej liczbie |
| 35 | `ContainerCreateOp` | kontener, rodzaj, gospodarz, właściciel, dostęp, rozmiar | identyfikator nieużyty; gospodarz istnieje, także utworzony wcześniej w tej samej operacji złożonej; gospodarz `Poi` nie ma jeszcze kontenera |
| 36 | `ContainerRemoveOp` | kontener | kontener jest pusty |
| 37 | `ContainerConfigureOp` | kontener, rozmiar, dostęp | usuwane sloty są puste; dostęp zmienia tylko właściciel |
| 38 | `PersistentEntityCreateOp` | byt, rodzaj (3 albo 6), rama, `ParentSeq`, poza lokalna, właściciel | identyfikator nieużyty; rama istnieje |
| 39 | `PersistentEntityRemoveOp` | byt | jego kontener został już usunięty |
| 40 | `BlockEntitySetOp` | budowla, komórka, oczekiwany typ bloku, stan (pusty = usuń) | komórka ma blok oczekiwanego typu; zgodność stanu z funkcją bloku sprawdza walidator |
| 41 | `FoodSetOp` | gracz, trzy miejsca (typ i tick) | ustawia wszystkie trzy miejsca naraz |
| 42 | `SuitRefillOp` | gracz, zasób, ilość, pojemność | dodaje z obcięciem do pojemności z operacji, którą walidator wylicza z modułów kombinezonu |
| 43 | `ItemDiscoverOp` | gracz, typ | typ jeszcze nieodkryty przez gracza |
| 44 | `RespawnPointSetOp` | gracz, łóżko (budowla, komórka i oczekiwany typ bloku) albo brak | komórka ma blok oczekiwanego typu ze stanem łóżka (`BlockEntity`); brak czyści punkt odrodzenia |
| 45 | `ProgressKeyGrantOp` | klucz postępu | świat nie ma jeszcze tego klucza; dodaje go do kluczy postępu świata |
| 46 | `WorldSettingsSetOp` | pełne ustawienia świata | wartości w dozwolonych zakresach |
| 47 | `PropRegrowOp` | planeta, obiekt | obiekt jest zebrany; usuwa go ze zbioru zebranych, czyli obiekt odrasta |
| 48 | `PropHarvestOp` | planeta, obiekt, tick zebrania | obiekt nie jest zebrany; istnienie w generatorze sprawdza walidator |
| 49 | `BlockFunctionStateSetOp` | budowla, komórka, oczekiwany typ bloku, nowy stan funkcji 0–7 | komórka ma blok oczekiwanego typu; ustawia bity 25–27 stanu bloku, wersja budowli +1; czy typ ma stan funkcji, sprawdza walidator |

Kody 30–49 są zajęte w całości. Składowymi mogą być też operacje z K1-03 i z Kosmosu, np. `BlockSetOp`, `TerrainEditOp`, `EntityReparentOp` i `ShipFuelAddOp`. Enumy źródeł, ujść i gospodarzy mają od pierwszego formatu (K6-29) także wartości dla podprojektów 4 i 5 (`Recovered`, `Ammo`, `Offering`, `Starter`, `Poi`), więc tamte nie zmieniają formatu operacji 33–35.

| Akcja | Składowe operacji złożonej |
| --- | --- |
| Kopanie terenu | `TerrainEditOp`, `ItemCreateOp` (urobek), zawalenia bloków nad wykopem |
| Zebranie obiektu | `PropHarvestOp`, `ItemCreateOp` |
| Odrośnięcie obiektu | `PropRegrowOp`; serwer tworzy ją sam |
| Rzemiosło | `ItemDestroyOp` na składnikach, `ItemCreateOp` z wynikiem, `ItemDiscoverOp` dla nowych przedmiotów |
| Rozliczenie stacji z czasem | `ItemDestroyOp` (wejście, paliwo), `ItemCreateOp` (wyjście), `BlockEntitySetOp` (nowy tick startu) |
| Rozliczenie generatora tlenu | `ItemDestroyOp` (zużyte ogniwa), `BlockEntitySetOp` (nowy tick startu) |
| Postawienie bloku | `ConstructCreateOp` albo `BlockSetOp`, `ItemDestroyOp` na koszcie; blok funkcyjny dostaje w tej samej operacji `BlockEntitySetOp` ze stanem początkowym, a blok z kontenerem (skrzynia, stacja, ładownia) także `ContainerCreateOp` z blokiem jako gospodarzem |
| Rozbiórka | przy bloku funkcyjnym najpierw `ContainerRemoveOp` (pusty kontener) i `BlockEntitySetOp` z pustym stanem, potem `BlockClearOp`, `ItemCreateOp` ze zwrotem i zawalenia; zawalony blok funkcyjny tak samo, po wysypaniu zawartości jako przedmiotów leżących |
| Jedzenie | `ItemDestroyOp`, `FoodSetOp` |
| Śmierć | `PersistentEntityCreateOp` (nagrobek), `ContainerCreateOp` z nagrobkiem jako gospodarzem, `ItemTransferOp` dla każdego slotu, `FoodSetOp` (puste), `EntityReparentOp` do punktu odrodzenia |
| Wyrzucenie | `PersistentEntityCreateOp`, `ContainerCreateOp`, `ItemTransferOp` |
| Podniesienie | `ItemTransferOp`, `ContainerRemoveOp`, `PersistentEntityRemoveOp` |
| Tankowanie | `ItemDestroyOp` na ogniwach, `ShipFuelAddOp` |
| Teleport | `EntityReparentOp` po gotowości terenu docelowego |

Niezmiennik zachowania: dla każdego typu suma sztuk we wszystkich kontenerach równa się sumie `ItemCreateOp` minus sumie `ItemDestroyOp`. Przeniesienia i zamiany go nie zmieniają.

- Walidator i resolver sprawdzają warunki, zanim powstanie operacja. Applier stosuje składowe na żywym stanie z dziennikiem cofania, a błąd składowej przy powtórce daje `ReplayFailedException` z numerem operacji i składowej.
- Serwer sam tworzy operacje rozliczenia stacji i generatorów tlenu, podniesienia w promieniu 2 m, zniknięcia leżących, zawaleń i śmierci.
- Intencje przeniesienia niosą oczekiwaną zawartość slotu źródłowego, a nie wersję kontenera. Ruch w innych slotach tej samej skrzyni ich nie unieważnia.
- Gracz martwy albo w przejściu (teleport między planetami) nie wykonuje intencji przedmiotów, a jego kontenery są zamrożone.
- `TerrainToolIntent` i `RemoveBlockIntent` z K1-03 dostają pole `AllowCollapse`. Zmiana formatu podbija wersję formatu ich ramek i `ProtocolVersion`, zgodnie z ZASADY.md §3.

Operacja złożona może zmienić kilka budowli, więc `AppliedOperation` i `OperationApplied` niosą listę zmienionych budowli z K1-03. Operacje niosą też limit stosu, z którego korzystały, więc applier nie czyta treści gry (ZASADY.md §4).

| Kod intencji | Intencja | Pola | Wynik |
| --- | --- | --- | --- |
| 20 | `ItemMoveIntent` | źródło, cel (slot albo dowolny), liczba, oczekiwany typ i liczba w źródle | `ItemTransferOp` albo `ItemSwapOp`; plecak w wyposażeniu zmienia rozmiar plecaka |
| 21 | `ItemDropIntent` | kontener, slot, liczba | przedmiot leżący przed graczem |
| 22 | `QuickStackIntent` | — | przeniesienia do skrzyń w promieniu 10 m |
| 23 | `TakeAllIntent` | kontener | przeniesienia do plecaka i paska; pusty nagrobek znika |
| 24 | `CraftIntent` | receptura, liczba, stacja albo brak | rzemiosło |
| 25 | `EatIntent` | kontener, slot | jedzenie |
| 26 | `SuitRefillIntent` | kontener, slot | `ItemDestroyOp` i `SuitRefillOp` |
| 27 | `PropHitIntent` | planeta, obiekt, slot narzędzia | ubytek wytrzymałości obiektu w pamięci serwera; ostatnie uderzenie daje zebranie |
| 28 | `BlockInteractIntent` | budowla, komórka, akcja: `Toggle`, `Sleep`, `SetRespawn` | `Toggle`: `BlockFunctionStateSetOp` (drzwi, światła); `Sleep` i `SetRespawn`: `RespawnPointSetOp` |
| 29 | `BlockConfigIntent` | budowla, komórka, nazwa teleportu albo dostęp kontenera | `BlockEntitySetOp` albo `ContainerConfigureOp` |
| 30 | `TeleportIntent` | budowla, komórka | przejście, potem teleport |
| 31 | `PlantIntent` | grządka, kontener i slot nasiona | `ItemDestroyOp` i `BlockEntitySetOp` |
| 32 | `CropHarvestIntent` | grządka | `BlockEntitySetOp` i `ItemCreateOp` |
| 33 | `WorldSettingsIntent` | ustawienia | `WorldSettingsSetOp`; tylko administrator |

Nowe kody odmowy (40–58): `InventoryFull` 40, `NotEnoughItems` 41, `ContainerChanged` 42, `ContainerLocked` 43, `StationRequired` 44, `RecipeLocked` 45, `TooFarFromStation` 46, `CannotTeleportBulk` 47, `InTransition` 48, `FoodAlreadyActive` 49, `NotGrown` 50, `NoSupport` 51, `WouldSplitShip` 52, `ToolTierTooLow` 53, `NotOwner` 54, `CollapseNotConfirmed` 55, `NotAdmin` 56, `ContainerNotEmpty` 57, `CollapseTooLarge` 58. Każdy kod ma co najmniej jeden test walidacji, jak w K1-03.

## Sieć

Klient zna tylko te kontenery, które subskrybuje, a prawdą o ich zawartości są wyłącznie delty z serwera.

| Typ | Komunikat | Kierunek i kanał | Zawartość |
| --- | --- | --- | --- |
| 40 | `ContainerState` | S→K, niezawodny | pełny kontener: identyfikator, rodzaj, rozmiar, dostęp, wersja, sloty; albo kod odmowy subskrypcji bez slotów |
| 41 | `ContainerDelta` | S→K, niezawodny | kontener, nowa wersja, zmienione sloty |
| 42 | `ContainerSubscribe` | K→S, niezawodny | kontener do otwarcia |
| 43 | `ContainerUnsubscribe` | K→S, niezawodny | kontener do zamknięcia |
| 44 | `PlayerVitals` | S→K, zawodny, 10 Hz, tylko do właściciela | zdrowie i wytrzymałość z maksimami, zasoby kombinezonu, aktywne zagrożenia, schronienie, odpoczynek |
| 45 | `PropStates` | S→K, niezawodny | zebrane obiekty regionu terenu z tickiem zebrania |
| 46 | `BlockEntityStates` | S→K, niezawodny | stany bloków funkcyjnych jednej budowli |
| 47 | `WorldSettingsState` | S→K, niezawodny | ustawienia świata |
| 48 | `PlayerProgressState` | S→K, niezawodny, tylko do właściciela | jedzenie, odkrycia, punkt odrodzenia, własne nagrobki |
| 49 | `ItemRegistry` | S→K, niezawodny | rejestr przedmiotów świata; po `ServerHello`, zanim przyjdą kontenery |

- Plecak, pasek i wyposażenie gracz subskrybuje zawsze. Przy wejściu do gry po `ServerHello` dostaje ich `ContainerState`, potem `WorldSettingsState` i `PlayerProgressState`.
- Skrzynię, stację albo nagrobek można subskrybować z odległości do 6 m i z prawem dostępu. Serwer kończy subskrypcję sam, gdy gracz odejdzie dalej niż 8 m.
- Klient stosuje deltę tylko do wersji o 1 wyższej od znanej. Luka w wersjach oznacza ponowną subskrypcję i pełny stan.
- Klient pokazuje przeniesienie od razu i cofa je przy odmowie albo przy delcie, która mu przeczy.
- `PropStates` przychodzi razem z terenem regionu (K3-16) i jako delta po każdym zebraniu, bo obiekty generator buduje u klienta z ziarna.
- `BlockEntityStates` przychodzi razem z `ConstructState` i po każdym `BlockEntitySetOp`; stan funkcji (drzwi, światła) dociera w `OperationApplied` z `BlockFunctionStateSetOp`. Pasek postępu stacji klient liczy sam z ticku startu.
- Przedmioty leżące i nagrobki to byty w migawkach (rodzaje 3 i 6). `EntitySpawn` niesie ich wygląd: przedmiot leżący ma `Archetype` równy `ItemTypeId` pierwszego stosu i liczbę sztuk w `KindData` (varuint), a nagrobek `Archetype` 0, puste `KindData` i właściciela w `Owner`. Przedmioty leżące widać z 64 m, a własne nagrobki gracz zna z `PlayerProgressState`.
- Intencje przedmiotów mają osobny kubełek tokenów: 20 na sekundę i serię 40. Wartości są w `GameRules`.

Operację złożoną serwer wysyła w `OperationApplied` jako jej rzut: tylko składowe świata (teren, bloki, byty, stany bloków). Składowe przedmiotów docierają jako `ContainerDelta` do subskrybentów, a postęp gracza jako `PlayerProgressState` do właściciela. `ContainerSubscribe` i `ContainerUnsubscribe` dopisuje do komunikatów dozwolonych w stanie `InGame` z K1-07 pakiet K6-29.

## Zapis

Podprojekt dokłada pięć tabel w jednej migracji i używa istniejących tabel `entities` oraz `meta`.

| Tabela | Zawartość | Blob (kod) |
| --- | --- | --- |
| `item_types` | rejestr przedmiotów świata: identyfikator i klucz | — |
| `containers` | kontenery | `Container` (20) |
| `block_entities` | stany bloków funkcyjnych z kluczem (budowla, komórka) | `BlockEntity` (21) |
| `player_progress` | stan gracza z tego podprojektu | `PlayerProgress` (22) |
| `harvested_props` | zebrane obiekty: planeta, obiekt, tick zebrania | — |
| `entities` (istniejąca) | byty trwałe rodzajów 3 i 6 | `PersistentEntity` (24) |
| `meta` (istniejąca) | klucze `world_settings` i `next_container_id` | `WorldSettings` (23) |
| `meta` (istniejąca) | klucz `progress_keys`: klucze postępu świata | `ProgressKeys` (25) |

- Migracja ma planowo numer M0003 i powstaje w K6-29 ze wszystkimi tabelami podprojektu; kolejne pakiety dodają tylko bloby i zapis. Kosmos i Przetrwanie mogą powstawać równolegle, więc numer nadaje się przy scaleniu do `main` jako następny wolny, a `MigrationRunner` wymaga ciągłych numerów.
- Checkpoint zapisuje zmienione kontenery, stany bloków, stan graczy i nowe zebrane obiekty. Zmiany śledzi zbiór zmienionych kluczy, jak w K1-05.
- `PropRegrowOp` usuwa z `harvested_props` obiekty, które już odrosły, więc tabela trzyma tylko obiekty naprawdę nieobecne.
- Świat z wcześniejszej wersji po migracji nie ma kontenerów graczy. Każdy gracz dostaje je przy następnym wejściu.
- Skrót stanu obejmuje kontenery, byty trwałe, stany bloków, zebrane obiekty, ustawienia, klucze postępu, next\_container\_id i dziennikowane pola `PlayerProgress`. Pomija zdrowie, wytrzymałość i zasoby kombinezonu, które zapisuje tylko checkpoint.
- Baza testowa z poprzedniej wersji schematu migruje w teście, a baza po tej migracji trafia do `Fixtures/` dla następnych migracji.

## Obsługa błędów

Każdy błąd kończy się stanem, w którym każdy przedmiot jest dokładnie w jednym miejscu, nawet kosztem odrzuconej akcji gracza.

| Sytuacja | Reakcja |
| --- | --- |
| Dwa przeniesienia tego samego stosu przy dużym RTT | drugie dostaje `ContainerChanged` albo `NotEnoughItems`; klient cofa swój podgląd |
| Awaria serwera w trakcie akcji | operacja złożona jest w dzienniku cała albo wcale; powtórka daje ten sam stan |
| Składowa nie spełnia warunku przy powtórce | `ReplayFailedException` z numerem operacji i składowej; świat nie startuje, a kopia sprzed startu zostaje (K1-05) |
| Brak miejsca na wynik rzemiosła | `InventoryFull` przed zużyciem składników |
| Brak miejsca na urobek albo zwrot | nadmiar ląduje jako przedmiot leżący pod nogami |
| Gracz rozłącza się z otwartą skrzynią | subskrypcja znika, stan skrzyni bez zmian |
| Gracz ginie z otwartym nagrobkiem albo stacją | subskrypcje kończą się przed operacją śmierci |
| Nagrobek wypadłby w terenie | resolver wynosi go na powierzchnię nad miejscem śmierci |
| Teren celu teleportu nie jest gotowy | gracz czeka w przejściu; po 60 s przejście się anuluje, a gracz zostaje przy teleporcie źródłowym |
| Stacja wczytana po długim postępie serwera | rozliczenie z ticków przy pierwszej interakcji; ticki nie płyną, gdy serwer stoi |
| Aktualizacja zmienia koszt bloku | stare bloki zwracają koszt według zapisanej wersji kosztu |
| Aktualizacja wycofuje typ przedmiotu | typ zostaje w rejestrze z flagą `Retired`; receptury go nie tworzą, a istniejące sztuki można wyrzucić |
| Zawalenie ponad 10 bloków bez potwierdzenia | `CollapseNotConfirmed`; klient pokazuje podgląd i pyta |
| Uszkodzony blob kontenera przy wczytaniu | `WorldCorruptedException` i odmowa startu, jak w K1-05 |
| Zawalenie za duże na jedną operację złożoną (ponad 4096 składowych albo 160 KiB) | odmowa `CollapseTooLarge`; gracz rozbiera budowlę po kawałku |

## Testy

Najważniejszy test podprojektu to właściwość zachowania przedmiotów, sprawdzana po każdej operacji na losowych sekwencjach.

| Test | Kryterium zaliczenia |
| --- | --- |
| Zachowanie przedmiotów | 10 000 losowych sekwencji intencji (przeniesienia, rzemiosło, jedzenie, budowa, rozbiórka, śmierć) dla 8 graczy: po każdej operacji niezmiennik zachowania jest spełniony |
| Duplikacja pod opóźnieniem | 8 botów przy jednej skrzyni, 10 000 intencji przy RTT 250 ms i 5% strat: zero duplikatów i zero zgubień |
| Atomowość rzemiosła | rzemiosło ze składnikami w 5 skrzyniach i awaria wstrzyknięta między składowymi: po powtórce całość albo nic |
| Piec według ticków | liczba sztuk po N tickach nie zależy od tego, kiedy i jak często ktoś otwiera piec; po restarcie wynik jest ten sam |
| Krzywa jedzenia | efekt 92%, 81%, 66% i 50% przy 75%, 50%, 25% i 10% pozostałego czasu, z dokładnością 0,5 punktu |
| Zagrożenia | profil planety zużywa właściwy zasób; jaskinia i zamknięta chata dają schronienie, wiata bez ścian nie; generator tlenu chroni w promieniu 10 m |
| Wytrzymałość: wydajność | pełna ocena budowli 64³ bloków do 50 ms na komputerze referencyjnym; zmiana jednego bloku w dużej bazie mieści się w budżecie ticku |
| Wytrzymałość: poprawność | wynik niezależny od kolejności przetwarzania (losowe permutacje); wysięgi z tabeli materiałów; kaskada przy kopaniu w jednej operacji |
| Spójność statku | usunięcie bloku dzielącego statek daje `WouldSplitShip` |
| Śmierć | nagrobek zawiera dokładnie to, co mówi tryb kary; wyposażenie zostaje w trybie Normalna; gracz odradza się przy łóżku |
| Teleporty | przedmiot masowy blokuje teleport bez `TeleportAll` i przechodzi z nim; teleport między planetami czeka na teren |
| Zwrot kosztów | zmiana wersji kosztu bloku nie zmienia zwrotu bloków postawionych wcześniej |
| Uprawy | wzrost jako funkcja ticków; zbiór niedojrzałej rośliny daje `NotGrown` |
| Odtwarzanie | checkpoint plus powtórka dziennika z operacjami złożonymi daje ten sam skrót stanu |
| Migracja | baza z poprzedniej wersji schematu migruje, a gracze dostają kontenery przy następnym wejściu |
| Walidacja treści | treść z usuniętym typem, nieznanym składnikiem albo 17. wersją kosztu nie przechodzi walidacji |

## Kryteria ukończenia

- [ ] 8 graczy przechodzi pętlę: zbieranie, rzemiosło, baza z zasobów, wyprawa na księżyc w kombinezonie, śmierć, odzyskanie nagrobka i powrót.
- [ ] Test właściwości i test 8 botów pod opóźnieniem nie znajdują ani jednego duplikatu ani zgubionego przedmiotu.
- [ ] Restart i awaria serwera zachowują każdy przedmiot, a skrót stanu po powtórce jest równy skrótowi przed awarią.
- [ ] Wytrzymałość ocenia budowlę 64³ do 50 ms, a zawalenie ponad 10 bloków zawsze wymaga potwierdzenia.
- [ ] Każde ustawienie świata z tabeli działa i przetrwa restart.
- [ ] Wszystkie testy z sekcji Testy przechodzą w CI albo w opisanych przebiegach na sprzęcie referencyjnym.

## Pakiety pracy

Przetrwanie to 12 pakietów kamienia milowego K6: Opus 5.5 prowadzi 1, Sonnet 5.5 5, Gemini 3.8 Flash 5, a GPT-6.1 Sol 1. Plany wykonawcze pisze Opus 5.5 z tej specyfikacji przed startem każdego pakietu, gdy jego zależności są scalone, i przegląda każdy pakiet, który zmienia Core (ZASADY.md §3).

| Pakiet | Zakres | Prowadzi | Zależy od | Odbiór |
| --- | --- | --- | --- | --- |
| K6-29 | Przedmioty i kontenery: `ItemDef`, `ItemStack`, rejestr przedmiotów, `ContentLoader` z walidacją, `Container`, `CompoundOp` z dziennikiem cofania, operacje 31–39 i 46, intencje przenoszenia, `PlayerProgress` z blobem 22, komunikaty 40–43 i 47–49, migracja ze wszystkimi tabelami podprojektu, bloby 20, 23, 24 i 25 | Opus 5.5 | K4-19 | test właściwości zachowania na kontenerach testowych (przeniesienia, tworzenie, niszczenie); pełny test w K6-40 |
| K6-30 | Bloki funkcyjne: `BlockEntity` z blobem 21, `BlockEntitySetOp`, `BlockEntityStates`, skrzynie i ich dostęp, łóżka z `RespawnPointSetOp` (44), drzwi i światła jako stan funkcji z `BlockFunctionStateSetOp` (49), `BlockInteractIntent`, `BlockConfigIntent`, szybkie odkładanie | Sonnet 5.5 | K6-29, K2-12 | skrzynie, łóżka, drzwi i światła działają i przetrwają restart |
| K6-31 | Zbieranie: urobek w operacji kopania, obiekty generatora, `PropHitIntent`, `PropHarvestOp`, `PropRegrowOp`, `PropStates`, odrastanie, poziomy narzędzi | Sonnet 5.5 | K6-29, K2-09, K3-16 | urobek zgodny z danymi; zebrane i odrośnięte obiekty identyczne u 2 klientów i po restarcie |
| K6-32 | Rzemiosło i stacje: `RecipeDef`, odkrywanie z operacją 43, klucze postępu z operacją 45, warsztat z ulepszeniami, piec i rafineria z czasem, kuchnia, fabrykator, składniki z kontenerów w promieniu 20 m | Gemini 3.8 Flash | K6-29, K6-30 | rzemiosło z 5 skrzyń atomowe; piec według ticków |
| K6-33 | Stan postaci i jedzenie: zdrowie, wytrzymałość, `FoodSetOp`, krzywa r^0,3, odpoczynek, obrażenia od upadku, `PlayerVitals` | Gemini 3.8 Flash | K6-29 | krzywa jedzenia z dokładnością 0,5 punktu; upadek według prędkości uderzenia |
| K6-34 | Zagrożenia i kombinezon: profile planet, zasoby, moduły, `SuitRefillOp`, schronienie z 26 promieni, generator tlenu z rozliczeniem ogniw | Gemini 3.8 Flash | K6-30, K6-33 | testy zagrożeń i schronienia na scenach referencyjnych |
| K6-35 | Śmierć i odrodzenie: operacja śmierci, nagrobki, tryby kary, punkt odrodzenia jako jedna funkcja Core (używa jej też zwolnienie fotela z K5-25), wynoszenie nagrobka na powierzchnię | Sonnet 5.5 | K6-29, K6-30, K6-33 | test śmierci w trzech trybach kary; nagrobek przetrwa restart |
| K6-36 | Budowanie z zasobów i wytrzymałość: koszty i wersje kosztu, zwrot, `IntegrityEvaluator`, podgląd podparcia, potwierdzenie zawalenia, oznaczanie bloków bez podparcia, kaskada przy kopaniu, spójność statków rozpoznawanych po `ConstructMode.Ship` z K1-03 (testy budują statki przez `WorldStateBuilder`), `AllowCollapse` w intencjach z K1-03 | GPT-6.1 Sol | K6-29, K6-31, K2-12 | testy wydajności i poprawności wytrzymałości; zwrot według wersji kosztu |
| K6-37 | Teleporty i logistyka: teleporty w parach, przejście między planetami, przedmioty masowe i `TeleportAll`, kontener ładowni `CargoHold` (32, 64 albo 96 miejsc) w blokach ładowni z K5-23, tankowanie z ogniw, podtrzymanie życia w fotelu statku | Sonnet 5.5 | K6-30, K6-34, K5-22, K5-25 | testy teleportów; tankowanie zużywa ogniwa, a darmowe paliwo je pomija |
| K6-38 | Uprawy: grządki, nasiona, wzrost z ticków, światło i atmosfera, zbiór | Gemini 3.8 Flash | K6-30, K6-32, K6-34 | test upraw |
| K6-39 | Interfejs: ekwipunek, skrzynie, rzemiosło, podgląd budowy, HUD postaci i kombinezonu, ekran ustawień świata, administratorzy | Gemini 3.8 Flash | K6-32, K6-33, K6-34, K6-35, K6-36, K6-37, K6-38 | każda akcja z sekcji Rozgrywka jest dostępna z interfejsu |
| K6-40 | Testy Przetrwania: boty w pętli przetrwania, test duplikacji pod opóźnieniem, scenariusze regresji | Sonnet 5.5 | K6-31, K6-34, K6-35, K6-37, K6-38, K6-39 | wszystkie kryteria ukończenia spełnione |

## Ryzyka i otwarte kwestie

Największe ryzyko to nowa droga duplikacji przedmiotów, bo każda kolejna operacja dokłada miejsce, w którym niezmiennik może pęknąć.

| Ryzyko | Skutek | Odpowiedź |
| --- | --- | --- |
| Nowa ścieżka duplikacji | gracze kopiują przedmioty, gospodarka się sypie | jeden applier, operacje złożone z dziennikiem cofania, test właściwości rozszerzany o każdą nową operację, przegląd Opus 5.5 |
| Koszt wytrzymałości w dużych bazach | skoki ticku przy budowie | ocena przyrostowa w obszarze zmiany; pełna ocena tylko przy wczytaniu i zmianie trybu, poza wątkiem ticku |
| Heurystyka schronienia myli się | kara mimo dachu albo ochrona pod wiatą | 26 promieni i próg w danych; testy na scenach referencyjnych: jaskinia, chata, wiata |
| Nagrobek w niedostępnym miejscu | utrata przedmiotów | wynoszenie na powierzchnię; komenda administratora przenosząca nagrobek do łóżka (podprojekt 6) |
| Złożoność interfejsu ekwipunku | pakiet K6-39 rośnie ponad plan | makiety ekranów w planie wykonawczym; przegląd Dismonda przed scaleniem |
| Przyrost `harvested_props` | wolniejsze wczytywanie | tylko nieodrosłe obiekty w tabeli; skały i złoża to jeden wiersz na obiekt |
| Kosmos i Przetrwanie równolegle | konflikt numerów migracji | numer migracji nadawany przy scaleniu; zakresy kodów operacji, intencji, odmów, komunikatów i blobów rozdzielone między podprojekty |

Źródła: [Valheim Wiki: jedzenie](https://valheim.fandom.com/wiki/Food), [Valheim Wiki: budowanie](https://valheim.fandom.com/wiki/Building), [Steam: rzemiosło z kontenerów w Valheimie](https://steamcommunity.com/app/892970/discussions/0/4327475551416122758), [Thunderstore: CraftingWithContainers](https://thunderstore.io/c/valheim/p/LukaWolf/CraftingWithContainersTempFix/), [PCGamesN: rudy w portalach](https://www.pcgamesn.com/valheim/ore-portal), [XGamingServer: modyfikatory świata Valheima](https://xgamingserver.com/blog/valheim-world-modifiers-server-settings/), [Hack The Minotaur: plecaki w Enshrouded](https://hacktheminotaur.com/enshrouded/enshrouded-best-backpacks-guide/), [Palworld Wiki: udźwig](https://palworld.wiki.gg/wiki/Carrying_capacity), [Mein-MMO: Valheim i Enshrouded](https://mein-mmo.de/en/2-of-the-best-survival-games-of-recent-years-are-fully-released-which-one-suits-me-valheim-or-enshrouded,1587673/), [NMS Wiki: podtrzymywanie życia](https://nomanssky.fandom.com/wiki/Life_Support), [Funcom: utrata stabilności bazy](https://forums.funcom.com/t/entire-base-lost-stability-for-no-reason-hours-of-effort-lost/126712).
