# Specyfikacja podprojektu 2: Kosmos

> Kopia dokumentu z Claude Docs, stan na 2026-10-08. Wersja żywa: https://claude.ai/code/artifact/19c12ee3-629d-4ac4-9fc6-810902a2692f

## Kontekst i decyzje

Podprojekt 2 dodaje lot statkiem zbudowanym z bloków między czterema ciałami jednego układu, bez ekranu ładowania. W locie wszyscy siedzą, a każda encja od pierwszego dnia ma pozycję w ramie swojego rodzica. Decyzje pochodzą z raportu „Otwarte decyzje projektowe Horyzontu”; plan nie ma ram czasowych, kolejność wyznaczają zależności pakietów.

| Decyzja | Rozstrzygnięcie | Podstawa |
| --- | --- | --- |
| Chodzenie po statku | w locie wszyscy siedzą: pilot, nawigator i pasażerowie, do 8 osób; po zaparkowanych konstrukcjach chodzi się swobodnie | No Man's Sky miało jednoosobowe statki przez 9 lat, a chodzenie po korwetach dodało razem z wieloma światami fizyki ([Shacknews](https://www.shacknews.com/article/145686/no-mans-sky-voyagers-update-6-patch-notes)); Empyrion odmówił chodzenia po lecących statkach, bo wymagałoby to przepisania systemów ([forum Empyrion](https://empyriononline.com/threads/technical-idea-to-let-the-player-move-freely-on-its-ship-while-the-ship-is-moving.102989/)) |
| Ramy odniesienia | kanoniczny stan każdej encji to rama rodzica i pozycja lokalna; pozycja w świecie jest wartością pochodną | ten sam wzorzec stosują Unreal ([Epic](https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/Engine/GameFramework/FBasedMovementInfo?application_version=4.27)) i Unity ([Unity](https://docs.unity3d.com/Packages/com.unity.charactercontroller@1.0/manual/networking.html)) |
| Układ gwiezdny | jedno słońce (kierunek światła) i cztery nieruchome ciała: planeta domowa w początku układu i trzy ciała 18–110 km od niej | gry z trwałym budowaniem trzymają planety w miejscu ([SE Wiki](https://spaceengineers.fandom.com/wiki/Planets), [NMS Wiki](https://nomanssky.fandom.com/wiki/Planet)) |
| Szybki przelot | jeden model lotu z limitem prędkości zależnym od wysokości; napęd pulsacyjny podnosi limit | szybki przelot jest wszędzie wyłączany przy ciałach ([Twinfinite](https://twinfinite.net/guides/no-mans-sky-how-to-fly-faster/), [SE Wiki](https://spaceengineers.wiki.gg/wiki/Jump_Drive)) |
| Sterowanie pilota | statek przewidywany u pilota z korektą od serwera | bez predykcji pilot czuje na sterach RTT plus opóźnienie interpolacji |
| Zderzenia | w EA zderzenia zatrzymują statek, ale nie niszczą bloków | utrata statku przy lądowaniu frustruje; uszkodzenia mogą przyjść jako opcja świata |
| Platforma | na start tylko PC przez Steam | decyzja z podprojektu 6 |

Zależności od podprojektu 1: typy ram i operacja przepięcia w Core (K1-03), adapter Voxel Tools (K2-09), kontroler postaci niezależny od ramy (K2-11), budowle (K2-12), predykcja ruchu (K3-14), replikacja bytów i budowli (K3-15) oraz terenu (K3-16).

## Cel i zakres

Na bramce K5 grupa do 8 graczy buduje statek z bloków, startuje z planety domowej i bez ekranu ładowania lata na księżyc, planetę B i planetę C. Po wylądowaniu wysiada, buduje i wraca, a po restarcie albo awarii serwera statki, paliwo i znaczniki są na swoich miejscach.

W zakresie:

- układ gwiezdny: model, generacja z ziarna, rozmieszczenie ciał, kierunek słońca, grawitacja i strefy wpływu;
- wiele planet naraz: tereny na serwerze dla zajętych planet, impostory na kliencie, przejście impostor–woksele;
- statki z bloków: bloki funkcyjne, tryb statku, masa, ciąg i paliwo;
- lot: wspomaganie lotu, limit prędkości, napęd pulsacyjny, autolądowanie, postój;
- fotele i role, wsiadanie, wysiadanie, wyskok, blokada startu;
- ramy odniesienia w ruchu, predykcja statku u pilota, replikacja statków i pasażerów;
- mapa układu i planety, znaczniki prywatne i wspólne, nawigacja w HUD.

Poza zakresem EA: chodzenie po lecącym statku i EVA w locie, statki w statkach (hangary, dokowanie), orbity i obrót planet, kolejne układy, walka w kosmosie i wieżyczki, uszkodzenia od zderzeń, autopilot po trasie, windy i ruchome części budowli. Ramy, identyfikator układu i tryb budowli są w modelu danych od początku, więc te rozszerzenia nie wymagają migracji zapisów ani przepisywania systemów.

## Rozgrywka

### Układ gwiezdny

Układ ma jedno słońce i cztery nieruchome ciała. Dzień i noc wynikają z kierunku słońca, który obraca się wokół osi układu w cyklu 30 minut, a czas lokalny wynika z ticku serwera i pozycji na planecie.

| Ciało | Rola w progresji | Promień | Odległość od planety domowej | Przelot z napędem pulsacyjnym | Przelot bez napędu |
| --- | --- | --- | --- | --- | --- |
| Planeta domowa | start, boss 1 | 4,0 km | — | — | — |
| Księżyc | pierwszy lot, niska grawitacja, boss 2 | ok. 1,5 km | ok. 18 km | ok. 50 s | ok. 1 min |
| Planeta B | boss 3 | ok. 3,0–3,5 km | ok. 50 km | ok. 70 s | ok. 3 min |
| Planeta C | boss 4, finał EA | ok. 4,5–5,0 km | ok. 110 km | ok. 100 s | ok. 7 min |

Czasy przelotu wynikają z τ = 8 s, v\_min = 100 m/s i limitu 250 m/s bez napędu albo 2 km/s z napędem. Wszystkie te liczby są danymi, więc stroi się je po testach bez zmian w kodzie.

Grawitacja działa w strefie wpływu ciała o promieniu 3 R. Ma wartość g₀ · (R/r)² i wygasa liniowo między 2,5 R a 3 R; poza strefami grawitacji nie ma.

### Statki z bloków

Statek to budowla z kokpitem przełączona w tryb statku. Dostęp do kolejnych bloków statku otwierają bossowie (podprojekt 5).

| Blok | Działanie |
| --- | --- |
| Kokpit | miejsce pilota; bez niego budowla nie przejdzie w tryb statku |
| Fotel | miejsce pasażera |
| Stanowisko nawigatora | fotel z mapą układu, znacznikami i skanerem złóż planety |
| Silnik | ciąg wzdłuż osi bloku; kolejne poziomy z kolejnych bossów |
| Stabilizator | moment obrotowy i wspomaganie lotu |
| Zbiornik | pojemność paliwa |
| Ładownia | kontener (podprojekt 3) o pojemności zależnej od poziomu |
| Podwozie | blokada na gruncie, czyli postój |
| Napęd pulsacyjny | podnosi limit prędkości z 250 m/s do 2 km/s; nagroda za bossa 2 |
| Kadłub, okna, drzwi | struktura; drzwi to stan bloku |

Tryb statku włącza się w kokpicie. Wymaga kokpitu, co najmniej jednego silnika i braku bloków wbitych w teren. Statek nie podlega regule podparcia z podprojektu 3, tylko kontroli spójności.

### Lot

- Masa statku to suma mas bloków, a ciąg w każdym z sześciu kierunków to suma silników skierowanych w tę stronę.
- Wspomaganie lotu jest domyślnie włączone: tłumi dryf i obroty oraz utrzymuje zawis, jeśli silniki skierowane w dół przeważają ciężar.
- Prędkość nie przekracza v = clamp(h/τ, v\_min, v\_cap), gdzie h to wysokość nad powierzchnią najbliższego ciała. Każde zmniejszenie wysokości o połowę trwa τ · ln 2 ≈ 5,5 s, więc teren zawsze zdąży się wczytać.
- Paliwo zużywa się proporcjonalnie do ciągu, a napęd pulsacyjny zużywa je szybciej. Ostatnie 10% to rezerwa, która pozwala już tylko wylądować.
- W atmosferze działa prosty opór, poza nią nie. Zderzenia zatrzymują statek, ale nie niszczą bloków.
- Do czasu podprojektu 3 paliwo dolewa się przy bloku stacji tankowania bez kosztu. Potem tankowanie zużywa przedmioty, a darmowe paliwo zostaje jako ustawienie świata.

### Fotele, start i postój

- Statek startuje tylko wtedy, gdy nikt nie stoi w obrysie kadłuba. Gra sadza takie osoby na wolnych miejscach albo blokuje start z komunikatem.
- Fotel daje rolę: pilot steruje, nawigator obsługuje mapę i skaner, pasażer patrzy z kamery fotela.
- Wyjście z fotela w locie to wyskok: działa poniżej 30 m/s i 200 m wysokości, a postać dziedziczy prędkość statku. Powyżej tych progów jest zablokowane. Po wyskoku kombinezon spowalnia opadanie do 6 m/s, więc lądowanie nie zadaje obrażeń od upadku.
- Statek bez pilota schodzi pionowo i parkuje, a w przestrzeni bez grawitacji wyhamowuje do zera.
- Zaparkowany statek (podwozie zablokowane, prędkość poniżej 1 m/s) jest nieruchomy, a gracze chodzą po nim jak po budowli.

### Mapa i nawigacja

- Mapa układu pokazuje ciała, kierunek słońca, statki i graczy z nazwami oraz znaczniki.
- Mapa planety pokazuje ukształtowanie z generatora, graczy i znaczniki, bez mgły wojny.
- Znaczniki są prywatne albo wspólne; wybrany znacznik prowadzi strzałką i odległością w HUD.
- HUD lotu pokazuje wysokość, prędkość, bieżący limit prędkości, paliwo i kierunek do celu.

## Architektura i model danych

Pozycje w świecie powstają tylko przez złożenie ram w podwójnej precyzji silnika; zapisy, dziennik i sieć znają wyłącznie pozycje lokalne w ramie rodzica.

```
Każda encja żyje w ramie rodzica, a pozycja w świecie jest pochodna

Rama układu: środek w planecie domowej, osie stałe
├── Rama planety ×4: środek planety, grawitacja do środka
│   ├── Budowla statyczna: rama konstrukcji, podparcie w terenie
│   ├── Statek na planecie: rama konstrukcji, w strefie wpływu
│   │   └── Postać w fotelu: rama statku plus komórka fotela
│   └── Postać i inne byty: pozycja lokalna w ramie planety
└── Statek w kosmosie: rama konstrukcji, poza strefami wpływu
    └── Postać w fotelu: rama statku plus komórka fotela
```

Statek zmienia rodzica przy granicy strefy wpływu, a siedzący w nim gracze zostają w jego ramie, więc przechodzą granicę bez żadnej operacji.

### Ramy

- `FrameRef(FrameKind Kind, ulong Id)` z `FrameKind { System = 1, Planet = 2, Construct = 3 }` jest w Core od K1-03. Rama układu ma początek w środku planety domowej i stałe osie, a słońce to kierunek światła, rama planety początek w środku planety, a rama budowli początek w narożniku komórki (0,0,0) i jej obrót.
- `FrameMath` w Core składa ramy w podwójnej precyzji (`Vector3D`, `QuaternionD`); grawitację i strefy wpływu liczy układ gwiezdny z K5-21. Rama planety ma pole obrotu, w EA zawsze zerowe, więc ten sam kod obsłuży kiedyś obracające się ciała.
- Kolejność ticka na serwerze i u klienta: najpierw ramy i budowle (statki z wejściem pilota), potem postacie w każdej ramie, potem złożenie pozycji w świecie, na końcu interpolacja renderu.
- Serwer trzyma 500 ms historii transformacji ram; korzysta z niej kompensacja opóźnień w podprojekcie 4.

### Scena i fizyka

- Drzewo sceny odpowiada ramom: węzeł układu, pod nim węzły planet w swoich pozycjach, pod nimi teren, budowle i postacie z transformacjami lokalnymi. Przepięcie przenosi węzeł pod nowego rodzica z zachowaniem transformacji globalnej.
- W EA wszystko żyje w jednej przestrzeni fizyki, bo nikt nie chodzi po statku w locie. Interfejs „przestrzeń ramy” istnieje od początku, żeby osobne przestrzenie dla statków dało się włączyć później.
- Kontroler postaci dostaje z ramy przestrzeń kolizji i wektor grawitacji. Nie korzysta z wbudowanej obsługi ruchomych platform Godota, która nie obraca postaci razem z platformą ([GitHub](https://github.com/godotengine/godot/issues/102763)).

### Wiele planet

- Serwer trzyma teren tylko dla planet, w których strefie wpływu jest gracz: CLIPBOX, wyłącznie kolizja, jeden widz na gracza ([Voxel Tools](https://voxel-tools.readthedocs.io/en/latest/api/VoxelLodTerrain/)). Teren gaśnie 60 s po wyjściu ostatniego gracza ze strefy, więc przy czterech ciałach aktywne są najwyżej cztery tereny.
- Klient trzyma aktywny teren tylko planety, w której strefie jest gracz, i zaczyna go wczytywać przy 4 R. Pozostałe ciała to impostory: kule z siatką z tego samego generatora (64 × 64 na ścianę sześcianu) i materiałem liczonym w przestrzeni planety.
- Impostor przechodzi w teren wokselowy na wysokości 1–2 R przez przenikanie. Teksturowanie trójplanarne liczy się zawsze w przestrzeni planety, bo daleko od początku układu drga ([Godot](https://docs.godotengine.org/en/stable/tutorials/physics/large_world_coordinates.html)).

### Dane statku

- `BlockTypeDef` dostaje masę i funkcję bloku (`BlockFunction`) z parametrami: ciąg, moment, pojemność paliwa, rola fotela. To dane treści, a nie zapis świata.
- Nagłówek budowli ma tryb (`Static` albo `Ship`), ramę rodzica i `ParentSeq` (K1-03). Stan statku (`ShipState`) to paliwo w jednostkach, prędkość liniowa i kątowa oraz postój.
- Fotel to komórka bloku z funkcją fotela. Siedzący gracz ma ramę budowli i komórkę fotela, a jego poza wynika z bloku.
- Model lotu żyje w Core jako czysta funkcja (siła, moment, limit prędkości, paliwo), wspólna dla serwera i predykcji pilota; kolizje liczy fizyka silnika.

## Operacje i dziennik

Lot, zużycie paliwa i pozy statków nie trafiają do dziennika, tak jak ruch graczy; zapisuje je checkpoint. Do dziennika trafiają zmiany, od których zależy dalszy stan: tryb statku, przepięcia ram, dolanie paliwa i znaczniki.

| Kod | Operacja | Pola | Warunki wstępne i skutek |
| --- | --- | --- | --- |
| 7 | `EntityReparentOp` (K1-03) | byt, nowa rama, `ParentSeq`, poza i prędkość lokalna, komórka fotela (opcjonalna) | rama istnieje; `ParentSeq` = bieżący + 1; fotel wolny i w tej budowli; ustawia ramę i pozę. Używają jej wsiadanie, wysiadanie, wyskok i przejście statku między strefami wpływu |
| 20 | `ShipModeSetOp` | budowla, tryb, rama, `ParentSeq`, początek i obrót | budowla istnieje; tryb inny niż obecny; rama i `ParentSeq` równe obecnym; liczby skończone; ustawia tryb i pozę budowli z chwili przełączenia. Kokpit, silnik, grunt i postój sprawdza walidator (`NoCockpit`, `NoEngine`, `ShipAnchored`, `ShipNotParked`), bo applier nie czyta treści ani terenu |
| 21 | `ShipFuelAddOp` | budowla, jednostki, pojemność zbiorników | budowla w trybie statku; dolewa z obcięciem do pojemności niesionej w operacji, którą wylicza walidator |
| 22 | `MarkerSetOp` | znacznik, właściciel (pusty = wspólny), planeta, pozycja, nazwa do 32 B, ikona, kolor | nowy znacznik ma numer co najmniej `next_marker_id` z `meta`: resolver podgląda licznik, a applier go przesuwa, jak `EntityId` w K1-03; najwyżej 256 znaczników prywatnych na gracza i 256 wspólnych |
| 23 | `MarkerRemoveOp` | znacznik | znacznik istnieje; prywatny usuwa tylko właściciel |

Intencje: `ShipModeIntent` 10, `SeatEnterIntent` 11, `SeatExitIntent` 12 (w locie to wyskok), `RefuelIntent` 13, `MarkerSetIntent` 14, `MarkerRemoveIntent` 15 i `SeatEvictIntent` 16. Ta ostatnia zwalnia fotel gracza offline, który trafia wtedy do swojego punktu odrodzenia: do pakietu K6-35 jest nim miejsce startu świata, potem łóżko gracza, jeśli je ma. Walidacja sprawdza teren (brak bloków wbitych w grunt) i odległość od fotela, zanim powstanie operacja; applier przy powtórce dziennika nie pyta już terenu.

Nowe kody odmowy (30–39): `NoCockpit` 30, `ShipAnchored` 31, `SeatOccupied` 32, `SeatOutOfReach` 33, `EjectTooFast` 34, `NotPilot` 35, `MarkerLimit` 36, `NoEngine` 37, `ConstructNotEmpty` 38, `ShipNotParked` 39. `ConstructNotEmpty` dostaje rozbiórka ostatniego bloku budowli, w której ramie jest jakikolwiek byt, także gracz offline w fotelu (K1-03). `ShipNotParked` dostaje powrót do `Static` statku, który nie stoi na postoju.

Przejście między strefami wpływu to operacja serwera: statek wchodzi w ramę planety przy 3,0 R i wychodzi do ramy układu przy 3,2 R. Przepina się tylko statek, bo pasażerowie mają pozycje w jego ramie.

## Sieć

Pilot przewiduje własny statek, pasażerowie niczego nie przewidują, a wszystkie stany bytów podróżują we współrzędnych ramy rodzica.

- `ShipInputBatch` (typ 30, kanał zawodny): numer pierwszego wejścia i do 5 wejść, z powtórzeniem trzech ostatnich. Wejście to tick klienta, ciąg w trzech osiach i obrót w trzech osiach (każda oś jako `sbyte`) oraz przyciski: wspomaganie lotu, napęd pulsacyjny, podwozie.
- Serwer przyjmuje wejście statku tylko od gracza w kokpicie i przetwarza jedno wejście na tick, tak jak wejście postaci. Wejście od innego gracza jest odrzucane i liczone jako `NotPilot`.
- Pilot przewiduje statek tym samym kodem lotu co serwer i koryguje się według `ShipCorrection` (typ 34, kanał zawodny, z każdą migawką, tylko do pilota): numer ostatniego przetworzonego wejścia statku, poza w ramie rodzica, prędkość liniowa i kątowa. `EntityState` się nie zmienia. Korekty poniżej 5 m są wygładzane przez 100–200 ms, większe są skokiem.
- Pasażer ma pozycję fotela w ramie statku. Kamera składa interpolowaną pozę statku z pozą fotela, więc opóźnienie interpolacji jest wewnątrz statku niewidoczne.
- Migawki niosą statki jako byty rodzaju `Construct` (2) z pozą w ramie rodzica. Tabela ram w migawce i `ParentSeq` w stanie bytu pochodzą z K1-04: stan ze starszym `ParentSeq` od znanego klient odrzuca, a z nowszym wstrzymuje do nadejścia operacji przepięcia, najwyżej na 1 s.
- Zainteresowanie liczy się w ramie regionu: planety (w strefie wpływu) albo układu (poza strefami). Na planecie promień to 500 m; w układzie statki widać z 10 km, a ich pasażerów razem ze statkiem.
- Wejście statku w zasięg wysyła pełny stan budowli (`ConstructState`), potem migawki. Statek poza zasięgiem nie zużywa pasma.
- `ShipStatus` (typ 31, kanał zawodny, 2 Hz, tylko do siedzących w statku): paliwo, bieżący limit prędkości, stan napędu, powód blokady startu.

Przy wejściu do gry klient dostaje stan układu i znaczników:

- `StarSystemState` (typ 32, kanał niezawodny): parametry układu, wysyłane po `ServerHello`.
- `MarkersState` (typ 33, kanał niezawodny): znaczniki wspólne i własne prywatne, po `ServerHello`. `OperationApplied` ze znacznikiem prywatnym idzie tylko do jego właściciela.
- `ShipInputBatch` dopisuje do komunikatów dozwolonych w stanie `InGame` z K1-07 pakiet K5-25.

## Zapis

Podprojekt dokłada trzy tabele w jednej migracji (numer nadawany przy scaleniu, planowo M0002), którą tworzy pakiet K5-21 od razu ze wszystkimi trzema tabelami. Formatu istniejących tabel nie zmienia, bo rama rodzica, `ParentSeq` i tryb budowli są w nagłówku budowli od K1-03. Blob `StarSystemParams` (10) dodaje K5-21, `ShipState` (12) K5-24, a `Marker` (11) K5-27.

| Tabela | Zawartość | Blob (kod) |
| --- | --- | --- |
| `star_systems` | parametry układu: ziarno, słońce, rozmieszczenie ciał | `StarSystemParams` (10) |
| `markers` | znaczniki: numer, właściciel, planeta, pozycja, nazwa, ikona, kolor; licznik `next_marker_id` leży w `meta` | `Marker` (11) |
| `ship_state` | dla statków: paliwo, prędkość liniowa i kątowa w ramie rodzica, postój | `ShipState` (12) |

- Świat bez wpisu w `star_systems` dostaje układ wygenerowany z ziarna galaktyki, z istniejącą planetą jako planetą domową.
- Statek w ruchu jest zawsze „zmieniony”, tak jak gracz online, więc każdy checkpoint zapisuje jego pozę w nagłówku i jego `ShipState`.
- Po wczytaniu statek niezaparkowany w strefie wpływu zostaje odstawiony na teren pod sobą i zaparkowany; w przestrzeni stoi w miejscu z prędkością zero.
- Gracz zapisany w fotelu po powrocie siedzi w tym samym fotelu: fotel gracza offline jest zajęty, a statku z kimkolwiek w środku nie da się rozebrać. Wyjątkiem jest zwolnienie fotela, po którym gracz wraca w swoim punkcie odrodzenia.

Skrót stanu obejmuje układ, znaczniki z licznikiem `next_marker_id` i tryb budowli, a pomija `ShipState` i pozy statków, bo lot jest poza dziennikiem.

## Obsługa błędów

Każda awaria w locie kończy się bezpiecznym, przewidywalnym stanem statku: postój na gruncie albo bezruch w przestrzeni.

| Sytuacja | Reakcja |
| --- | --- |
| Pilot traci połączenie w locie | statek przechodzi w autolądowanie i parkuje; pasażerowie zostają w fotelach |
| Pasażer traci połączenie | `PlayerLeaveOp` zapisuje ramę statku i fotel; po powrocie gracz siedzi w tym samym fotelu, chyba że ktoś go zwolnił; wtedy wraca w punkcie odrodzenia |
| Fotel gracza offline blokuje rozbiórkę statku | każdy gracz przy statku może zwolnić fotel; gracz offline trafia do swojego punktu odrodzenia |
| Migawka ze starszym `ParentSeq` | odrzucona |
| Migawka z nowszym `ParentSeq` bez operacji przepięcia | wstrzymana najwyżej 1 s, potem klient wysyła `ResyncRequest` (21) i dostaje pełny stan bytu: statek jako `ConstructState` od części 0, inny byt jako `EntitySpawn` |
| Teren planety nie jest jeszcze gotowy na serwerze, a statek wchodzi w strefę | wejście w strefę uruchamia wczytywanie; do gotowości kolizji statek nie zejdzie poniżej 2 R |
| Kolizja terenu nie zdążyła się zbudować przy schodzeniu | serwer obniża limit prędkości aż do zawisu, dopóki kolizja nie będzie gotowa |
| Paliwo spada do rezerwy | działa już tylko lądowanie; HUD ostrzega przy 25% i przy rezerwie |
| Rozjazd predykcji pilota powyżej 5 m | natychmiastowy skok do stanu serwera i wpis w logu z ostatnimi wejściami |
| Serwer przekracza budżet ticku przy wielu planetach | rzadsze aktualizacje terenu dla graczy daleko od innych; wpis w logu z czasem każdego terenu |

## Testy

Niewiadome z badań rozstrzygają testy w kodzie produkcyjnym, które zostają potem w CI jako scenariusze regresji.

| Test | Kryterium zaliczenia |
| --- | --- |
| Jolt w podwójnej precyzji daleko od początku układu | postać i statek ok. 115 km od początku układu, na planecie C: stojąca postać nie drga o więcej niż 1 mm, kolizje są stabilne |
| Zejście z 8 km | przy τ = 8 s na komputerze referencyjnym: brak dziur w terenie i luk w kolizji przy przyziemieniu w 20 próbach |
| 8 botów na 4 planetach | serwer dedykowany: RAM, CPU na tick i czas budowy kolizji zmierzone; tick średnio do 8 ms |
| Teksturowanie trójplanarne na planecie C | brak migotania przy staniu i chodzeniu |
| Zamiana impostora na woksele | przejście bez widocznego skoku obrazu |
| Czas przelotu | mediana przelotu do najbliższego sąsiada w testach kooperacji do 90 s |
| Przepinanie pod opóźnieniem | 1000 losowych wsiadań, wysiadań i wyskoków przy RTT 100–250 ms i 5% strat: żadnego skoku ponad 0,5 m, żadnego stanu zastosowanego w złej ramie |
| Predykcja pilota | RTT 100 ms: stery odpowiadają lokalnie od razu, korekty poniżej 0,5 m w 95% przypadków |
| Odtwarzanie | checkpoint z lecącym statkiem i graczami w fotelach plus powtórka dziennika: ten sam skrót stanu; statki niezaparkowane po wczytaniu stoją na gruncie |
| Autolądowanie | rozłączenie pilota na wysokości 100 m, 1 km i 5 km: statek ląduje i parkuje |
| Blokada startu | osoba stojąca w obrysie kadłuba blokuje start z komunikatem |

## Kryteria ukończenia

- [ ] 8 graczy buduje statek z bloków i leci na każde z trzech pozostałych ciał bez ekranu ładowania; przelot z napędem pulsacyjnym trwa 50–100 s.
- [ ] Pasażerowie siedzą w fotelach i widzą płynny lot przy RTT 100 ms i 5% strat.
- [ ] Statki, paliwo, znaczniki i miejsca graczy przetrwają restart i awarię serwera, a skrót stanu po powtórce jest równy skrótowi przed awarią.
- [ ] Serwer dedykowany z 8 graczami na czterech planetach utrzymuje tick średnio do 8 ms.
- [ ] Wszystkie testy z sekcji Testy przechodzą w CI albo w opisanych przebiegach na sprzęcie referencyjnym.

## Pakiety pracy

Kosmos to 9 pakietów kamienia milowego K5; Opus 5.5 prowadzi 4 z nich. Plany wykonawcze pisze Opus 5.5 z tej specyfikacji przed startem każdego pakietu, gdy jego zależności są scalone.

| Pakiet | Zakres | Prowadzi | Zależy od | Odbiór |
| --- | --- | --- | --- | --- |
| K5-20 | Ramy odniesienia w ruchu: `FrameMath`, `Vector3D` i `QuaternionD` w podwójnej precyzji, kolejność ticka, przenoszenie węzłów sceny, interfejs przestrzeni ramy, historia transformacji ram | Opus 5.5 | K4-19 | przepinanie bytów testowych pod opóźnieniem bez skoków ponad 0,5 m i stanów w złej ramie; test Jolt daleko od początku układu |
| K5-21 | Układ gwiezdny: `StarSystemParams`, generacja z ziarna, rozmieszczenie ciał, słońce, grawitacja i strefy wpływu, `StarSystemState`, migracja Kosmosu z trzema tabelami i blob 10 | Opus 5.5 | K5-20 | powtarzalny układ z ziarna; strefy wpływu się nie nakładają; grawitacja bez skoków |
| K5-22 | Wiele terenów i przejście orbita–powierzchnia: CLIPBOX na serwerze dla zajętych planet, aktywacja i wygaszanie, impostory, przejście impostor–woksele | Opus 5.5 | K5-21 | zejście z 8 km bez dziur; cztery tereny na serwerze w budżecie ticku |
| K5-23 | Bloki statku i tryb statku: kokpit, fotele, stanowisko nawigatora, silniki, stabilizatory, zbiornik, ładownia (blok bez kontenera; kontener dodaje K6-37), podwozie, napęd pulsacyjny; masa i ciąg; `ShipModeSetOp` z odmowami `NoCockpit`, `NoEngine` i `ShipAnchored` (do K5-24 każdy statek stoi na postoju) | Sonnet 5.5 | K4-19 | budowla z 30 bloków przechodzi w tryb statku i z powrotem; masa i ciąg zgodne z danymi |
| K5-24 | Model lotu: wspomaganie, limit v = h/τ, napęd pulsacyjny, paliwo i tankowanie, opór atmosfery, autolądowanie, postój i odmowa `ShipNotParked` (39) przy powrocie do `Static`, zapis `ShipState` (blob 12) | Sonnet 5.5 | K5-21, K5-23 | przeloty z napędem ok. 50, 70 i 100 s; autolądowanie z trzech wysokości |
| K5-25 | Fotele i sterowanie: wsiadanie, wysiadanie, wyskok, zwalnianie fotela, blokada startu, `ShipInputBatch`, predykcja pilota z korektą z `ShipCorrection` | GPT-6.1 Sol | K5-24 | 1000 wsiadań i wyskoków bez skoków ponad 0,5 m; korekty pilota poniżej 0,5 m w 95% |
| K5-26 | Replikacja statków i pasażerów: migawki w ramach, zainteresowanie w ramie układu, pełny stan statku przy wejściu w zasięg, `ShipStatus` | Opus 5.5 | K5-24 | 2 klientów ma identyczne statki i pozycje pasażerów po serii lotów |
| K5-27 | Mapa układu i planety, znaczniki z `MarkersState` i blobem 11, nawigacja w HUD | Gemini 3.8 Flash | K5-21 | znaczniki wspólne i prywatne przetrwają restart; prowadzenie do znacznika działa w każdej ramie |
| K5-28 | Testy Kosmosu: boty w locie na czterech planetach, pomiary, scenariusze regresji | Sonnet 5.5 | K5-22, K5-25, K5-26, K5-27 | wszystkie kryteria ukończenia spełnione |

## Ryzyka i otwarte kwestie

Największa niewiadoma to koszt kilku terenów CLIPBOX na jednym serwerze, bo nikt go nie opublikował.

| Ryzyko | Skutek | Odpowiedź |
| --- | --- | --- |
| Pamięć i CPU czterech terenów na serwerze | tick ponad budżet przy 8 graczach | test 8 botów na 4 planetach; plan awaryjny w danych: mniejsze promienie i odległości, np. 15, 30 i 50 km |
| Jolt w podwójnej precyzji ok. 110 km od początku układu | drgania postaci i kolizji | Jolt kompiluje się z `JPH_DOUBLE_PRECISION` przy `precision=double` ([Godot](https://raw.githubusercontent.com/godotengine/godot/master/modules/jolt_physics/SCsub)); pierwszy test pakietu K5-20 to potwierdza w naszej kompilacji |
| Dziury w terenie przy szybkim zejściu | statek wpada w teren | twardy limit v = h/τ i zawis do gotowości kolizji; τ jest daną |
| Gracze chcą chodzić po lecącym statku | gorsze odczucie kooperacji | krótkie przeloty, role w fotelach; chodzenie w locie to osobny etap po EA: osobna przestrzeń fizyki dla statku za flagą funkcji i pomiar kosztu ośmiu przestrzeni Jolt |
| Predykcja pilota przy lądowaniu | widoczne korekty przy kontakcie z terenem | wygładzanie poniżej 5 m; test predykcji przy RTT 100 ms |

Źródła: [No Man's Sky Voyagers](https://www.shacknews.com/article/145686/no-mans-sky-voyagers-update-6-patch-notes), [Empyrion: chodzenie po statku](https://empyriononline.com/threads/technical-idea-to-let-the-player-move-freely-on-its-ship-while-the-ship-is-moving.102989/), [Keen na GDC 2023](https://gdcvault.com/play/1028839/Online-Game-Technology-Summit-Predicted), [Godot #102763](https://github.com/godotengine/godot/issues/102763), [Godot: duże współrzędne](https://docs.godotengine.org/en/stable/tutorials/physics/large_world_coordinates.html), [Voxel Tools: VoxelLodTerrain](https://voxel-tools.readthedocs.io/en/latest/api/VoxelLodTerrain/), [Voxel Tools: gra sieciowa](https://voxel-tools.readthedocs.io/en/latest/multiplayer/), [Space Engineers: czas lotu](https://support.keenswh.com/spaceengineers/pc/topic/24907-potential-increases-to-top-speed-limits-to-them), [Outer Wilds: ruchome ciała](https://www.mobiusdigitalgames.com/news/breaking-the-laws-of-physics), [Gaffer: kompresja migawek](https://gafferongames.com/post/snapshot_compression/).
