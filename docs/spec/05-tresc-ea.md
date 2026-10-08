# Specyfikacja podprojektu 5: Treść na Early Access

> Kopia dokumentu z Claude Docs, stan na 2026-10-08. Wersja żywa: https://claude.ai/code/artifact/dcc602d8-92e0-472b-9e5a-bfd42fad93e4

## Kontekst i decyzje

Podprojekt 5 wypełnia systemy z podprojektów 1–4 treścią na Early Access: cztery typy planet, czterech bossów, faunę, progresję, kierunek gry i grafikę. Prawie wszystko jest danymi, więc pakiety treści rzadko dotykają kodu rdzenia. Decyzje pochodzą z raportu „Otwarte decyzje projektowe Horyzontu”.

| Decyzja | Rozstrzygnięcie | Podstawa |
| --- | --- | --- |
| Zakres EA | cztery typy planet i czterech bossów; grupa 2–4 osób pokonuje bossa 4 po 20–30 h, a twarde minimum to 15 h | Valheim wystartował z ok. 50% treści ([GamingBolt](https://gamingbolt.com/valheims-future-updates-detailed-more-bosses-recipes-and-mistlands-biome)); próg gotowej treści to 10–20 h ([How To Market A Game](https://howtomarketagame.com/2024/04/22/what-deep-rock-galactic-survivor-can-teach-us-about-early-access/)) |
| Pełne planety | każda planeta ma własny poziom surowców, 3–5 typów stworów, zagrożenie, jedzenie i bossa | Astroneera krytykowano za „brak rzeczy do zrobienia na każdej planecie” ([Wikipedia](https://en.wikipedia.org/wiki/Astroneer)) |
| Kierunek gry | dziennik celów, obeliski wskazujące ołtarze bossów i zapisy fabularne | zarzut „braku kierunku” wobec Hytale ([Wikipedia](https://en.wikipedia.org/wiki/Hytale)) |
| Bossowie jako bramy | pokonanie bossa daje klucz postępu świata, który otwiera następny etap | Valheim miał na starcie EA pięciu bossów ([Wikipedia](https://en.wikipedia.org/wiki/Valheim)); w Core Keeperze trzech bossów otwierało tylko wczesną fazę gry ([Indiecator](https://indiecator.org/2022/03/21/indietail-core-keeper/)) |
| Stabilność to też treść | żaden pakiet treści nie obniża kryteriów sieci i zapisu z podprojektów 1–4 | rozłączenia były największym problemem Nightingale ([GamingBolt](https://gamingbolt.com/nightingale-developer-not-happy-with-current-state-of-the-game-reveals-update-plans)) |
| Grafika | stylizowane low-poly z nowoczesnym światłem; licencjonowane paczki, modele wspomagane AI z obróbką w Blenderze, rejestr pochodzenia każdego zasobu | mały zespół; treść wygenerowaną przez AI przed wydaniem trzeba ujawnić w ankiecie treści Steamworks ([Steamworks](https://partner.steamgames.com/doc/gettingstarted/contentsurvey)) |
| Budżety wydajności | PC i Steam Deck (800p, 30 kl./s) | decyzja z podprojektu 6 |
| Długość gry | mierzona medianą w zamkniętych testach z telemetrią za zgodą graczy | budowanie dokłada godziny bez końca, ale grindu nie liczymy jako treści (raport, decyzja 6) |

Zależności: postać i kamera (K2-11), wiele terenów i mapa (K5-22, K5-27), bloki statku (K5-23), zbieranie, rzemiosło i zagrożenia (K6-31, K6-32, K6-34), interfejs (K6-39), broń dystansowa (K7-44), stwory, bossowie i animacje walki (K7-46, K7-47, K7-48), bramki podprojektów 2–4 (K5-28, K6-40, K7-49) oraz integracja Steam, telemetria i Steam Playtest (K9-58, K9-62, K9-64).

## Cel i zakres

Na bramce K8 debiutująca grupa 2–4 osób przechodzi grę od lądowania kapsuły na planecie domowej do pokonania bossa 4 na planecie C, w medianie 20–30 h i nie krócej niż 15 h. Na każdej planecie jest co robić, a gracz zawsze wie, jaki jest następny krok.

W zakresie:

- cztery typy planet: biomy, materiały terenu, flora i obiekty, profile zagrożeń, miejsca do odkrycia;
- fauna: 3–5 typów stworów na planetę i jeden silniejszy przeciwnik na planetę;
- bossowie 2–4 na frameworku z podprojektu 4, z ołtarzami, ofiarami i nagrodami;
- progresja: poziomy materiałów, narzędzia, broń, pancerz, moduły kombinezonu, bloki statku i jedzenie według kluczy postępu;
- kierunek gry: start przy kapsule ratunkowej, dziennik celów, wskazówki, zapisy fabularne, obeliski, finał EA;
- grafika i dźwięk: kierunek artystyczny, potok zasobów, rejestr pochodzenia, modele, animacje, efekty, dźwięki i muzyka;
- balans i pomiar długości gry.

Poza zakresem EA: piąta i kolejne planety, placówki i statki-bazy, finałowy boss wersji 1.0, ruchome ciała, osadnicy i handel, wydarzenia sezonowe. Typ planety i układ są danymi z `SystemId` i `PlanetId`, więc piąta planeta nie wymaga migracji zapisów.

## Rozgrywka

Nazwy stworów, bossów i surowców poniżej są robocze; zmiana nazwy to zmiana tekstu w danych, a nie w kodzie.

### Łańcuch postępu

| Etap | Planeta | Zagrożenie | Nowe surowce | Boss i ofiara | Klucz otwiera |
| --- | --- | --- | --- | --- | --- |
| 1 | Planeta domowa | zimno nocą w górach | drewno, żywica, kamień, włókno, krzemień, miedź, cyna, drewno rdzeniowe | Strażnik Gaju; rogi kolcorożca | `boss1`: warsztat 2, a na nim bloki pierwszego statku (kokpit, silnik I, zbiornik, podwozie, stabilizator, fotele, stanowisko nawigatora, ładownia I); rafineria, generator tlenu |
| 2 | Księżyc | próżnia, niska grawitacja | tytan, lód, hel-3 | Czerw Regolitu; rdzeń kryształowca | `boss2`: napęd pulsacyjny, silnik II, ładownia II, warsztat 3, fabrykator, pistolet energetyczny, moduł filtra |
| 3 | Planeta B | toksyny | kobalt, biopolimer, zarodniki | Matka Roju; jajo roju | `boss3`: silnik III, ładownia III, warsztat 4, moduły chłodzenia i osłony |
| 4 | Planeta C | żar i promieniowanie | iryd, kryształ energii | Strażnik Kuźni; klucz kuźni z irydu i kryształu | `boss4`: finał EA, trofeum, zapowiedź kolejnych planet |

- Każdy etap ma poziom narzędzi i broni: kamień i drewno, brąz, tytan, kobalt, iryd. Ruda następnej planety wymaga narzędzi z poprzedniej.
- Bez modułu na zagrożenie planety zasoby kombinezonu starczają na krótką wyprawę, więc bramką jest wytrzymałość wyprawy, a nie ściana.
- Pierwsze ogniwa paliwowe rafineria robi z żywicy, a wydajniejsze z helu-3. Przeloty bez napędu pulsacyjnego są możliwe, tylko dłuższe (podprojekt 2).
- Pierwsze pokonanie bossa daje klucz całemu światu, a każdy obecny gracz dostaje własny łup.

### Planety

| Planeta | Biomy | Stwory | Jedzenie |
| --- | --- | --- | --- |
| Domowa | łąki, las, stara puszcza, góry, jaskinie | Trawiak (bierny), Kolcorożec (neutralny, szarżuje), Szczerbacz (agresywny, stadny, nocą), Pełzacz jaskiniowy (agresywny) | jagody, grzyby, pieczone mięso, chleb z upraw |
| Księżyc | równiny regolitu, kratery z lodem, kryształowe szczeliny | Skoczek (neutralny), Kryształowiec (agresywny), Pyłowiec (zasadzka w regolicie) | porosty kraterowe, grzyby szczelinowe, uprawy w szklarni z generatorem tlenu |
| Planeta B | grzybowa dżungla, bagna, pylące wzgórza | Rojowiec (stadny), Plujak (dystansowy, trucizna), Bagiennik (duży, neutralny), Zarodnik (chmura toksyn) | owoce dżungli, mięso bagiennika, przyprawy |
| Planeta C | pola lawy, popielne pustkowia, kryształowe iglice | Żarowiec (agresywny), Pancernik magmowy (słaby na obuchowe), Wartownik (mechaniczny, strzela), Popielnik (nocny) | żarowe bulwy, mięso pancernika, potrawy z przyprawami |

Na każdej planecie żyje też jeden silniejszy przeciwnik, wersja alfa jednego ze stworów, z lepszym łupem. Każda planeta ma 3–4 potrawy, a potrawy z późniejszych planet są silniejsze.

### Miejsca do odkrycia

- Kapsuła ratunkowa to punkt startu świata, z terminalem, który daje pierwsze cele.
- Ruiny Budowniczych mają zapisy fabularne i skrzynie z łupem.
- Obelisk po aktywacji dodaje wspólny znacznik ołtarza bossa swojej planety i wpis dziennika.
- Ołtarz bossa jest jeden na planetę, na powierzchni i w zasięgu pieszo od miejsca lądowania.
- Wraki dają łup, a jaskinie surowce i stwory.
- Wszystkie miejsca rozstawia generator planety deterministycznie z ziarna; każda planeta ma co najmniej 3 obeliski i 6 ruin.

### Kierunek i wprowadzenie

- Nowy gracz dostaje zestaw startowy z danych: pochodnię, racje żywnościowe i ogniwo grzewcze.
- Pierwsze cele prowadzą przez zbieranie, warsztat, narzędzia, schronienie na noc i jedzenie.
- Dziennik celów pokazuje następny krok etapu. Cele wynikają ze stanu (odkryte przedmioty, klucze postępu, odwiedzone planety, odkryte miejsca), więc nie potrzebują własnych liczników.
- Wskazówka pojawia się przy pierwszym kontakcie z czymś nowym (przedmiot, stacja, zagrożenie) i klient ją zapamiętuje.
- Zapisy z ruin składają się w historię Budowniczych. Boss 4 zamyka łuk EA i odsłania Bramę, czyli zapowiedź kolejnych planet.

### Długość gry

- Cel to mediana 20–30 h do bossa 4 dla debiutującej grupy 2–4 osób w zamierzonym tempie; próg to 15 h.
- Model czasu postępu szacuje czas etapów z kosztów receptur i wydajności zbierania, zanim zaczną się testy z ludźmi.

## Architektura i model danych

Treść to pliki danych w `content/` i zasoby w `game/assets/`. Kod czyta dane przez `ContentLoader` z podprojektu 3, a walidacja w CI pilnuje spójności całego łańcucha postępu.

### Dane treści

| Folder albo plik | Typ | Zawartość |
| --- | --- | --- |
| `content/planets/` | `PlanetTypeDef` | profil generatora, biomy, materiały terenu, flora i obiekty, profil zagrożeń, reguły pojawiania się, miejsca do odkrycia, boss |
| `content/creatures/`, `content/bosses/` | `CreatureDef`, `BossDef` | definicje z podprojektu 4 |
| `content/items/`, `content/recipes/` | `ItemDef`, `RecipeDef` | definicje z podprojektu 3 |
| `content/lore/` | `LoreEntryDef` | klucz tekstu, miejsce, warunek odblokowania |
| `content/objectives/` | `ObjectiveDef` | etap, warunek ukończenia jako wyrażenie na stanie, wskazówka |
| `content/start.json` | `StartKitDef` | zestaw startowy |

- Teksty w danych to klucze lokalizacji; tłumaczenia PL i EN dostarcza podprojekt 6.
- Profil generatora zawiera wersję, np. `domowa/1`. Istniejące planety zostają przy swoim profilu, więc zmiana generatora nie przebudowuje terenów w starych światach.

### Miejsca do odkrycia

- `PoiDef` w danych planety ma rodzaj (kapsuła, ruiny, obelisk, ołtarz, wrak, jaskinia), reguły rozstawienia (biom, odległości, nachylenie) i zawartość (łup, wpis, znacznik).
- Generator planety z K2-08 rozstawia miejsca deterministycznie z ziarna, a ich identyfikatory są stabilne jak identyfikatory obiektów flory.
- Miejsce to obiekt generatora z modelem i kolizją, a nie budowla, więc nie da się go rozebrać. Generator wyrównuje pod nim teren, a w promieniu ochronnym serwer odrzuca kopanie i budowanie.
- Skrzynia w ruinach albo wraku to kontener z gospodarzem `Poi`, tworzony przy pierwszym otwarciu z łupem z tabeli. Jego istnienie oznacza, że skrzynię już otwarto.

### Postęp gracza i świata

- Cele dziennika to czyste funkcje stanu: warunek czyta odkryte przedmioty, klucze postępu, odwiedzone planety, odblokowane wpisy i odkryte miejsca.
- Nowe dane trwałe to odblokowane wpisy i odwiedzone planety gracza oraz odkryte miejsca świata.
- Wskazówki kontekstowe są tylko u klienta, w ustawieniach użytkownika.

### Grafika i zasoby

- Zasoby leżą w `game/assets/` (planety, stwory, bossowie, interfejs, dźwięk) w Git LFS, a ustawienia importu (`.import`) są w repozytorium.
- Modele to glTF binarny (`.glb`) z LOD z importu Godota i ręcznymi kolizjami z prostych kształtów. Każda planeta ma własną paletę i atlas tekstur.
- Rejestr pochodzenia `game/assets/provenance.json` ma dla każdego pliku źródło, licencję, autora, udział AI (narzędzie i zakres) i zmiany. Test CI odrzuca plik bez wpisu, a rejestr zasila ankietę treści Steamworks.
- Krzywe kapsuł i broni eksportuje skrypt z K7-48 przy imporcie każdej animacji.

| Zasób | Budżet startowy |
| --- | --- |
| Postać gracza | do 20 tys. trójkątów |
| Stwór | do 12 tys. trójkątów |
| Boss | do 60 tys. trójkątów |
| Obiekt flory albo miejsca | do 3 tys. trójkątów |
| Tekstury | 2048² dla bossów, 1024² dla reszty |
| Dźwięk | OGG Vorbis; muzyka strumieniowana |

Budżety startowe potwierdza albo zmienia test wydajności na Steam Decku w K8-50, zanim powstanie większość zasobów.

## Operacje i dziennik

Podprojekt dokłada trzy operacje postępu; reszta treści używa operacji z podprojektów 2–4.

| Kod | Operacja | Pola | Warunki wstępne i skutek |
| --- | --- | --- | --- |
| 60 | `LoreUnlockOp` | gracz, wpis | wpis jeszcze nieodblokowany u gracza |
| 61 | `PlanetVisitOp` | gracz, planeta | pierwsza wizyta gracza na planecie |
| 62 | `PoiDiscoverOp` | planeta, miejsce | miejsce nie jest jeszcze odkryte; istnienie miejsca w generatorze sprawdza walidator |

| Akcja | Składowe operacji złożonej |
| --- | --- |
| Pierwsze wejście gracza | `ItemCreateOp` z zestawem startowym (źródło `Starter`) obok kontenerów z podprojektu 3 |
| Aktywacja obelisku | `PoiDiscoverOp` (tylko przy pierwszej aktywacji), `MarkerSetOp` (wspólny znacznik ołtarza), `LoreUnlockOp` dla graczy w promieniu 20 m, którzy nie mają jeszcze tego wpisu |
| Odczyt zapisu w ruinach | `LoreUnlockOp` |
| Pierwsze otwarcie skrzyni w ruinach | `ContainerCreateOp` z gospodarzem `Poi`, `ItemCreateOp` (`Loot`) |
| Pierwsze wejście w ramę planety | `PlanetVisitOp`; serwer tworzy ją sam |

- Nowa intencja `PoiInteractIntent` (kod 50) ma planetę, miejsce i akcję: `Activate`, `Read` albo `Open`.
- Nowy kod odmowy `ProtectedArea` (70) dostaje kopanie i budowanie w promieniu ochronnym miejsca.
- Zestaw startowy, łup i nagrody zachowują niezmiennik przedmiotów, bo powstają wyłącznie przez `ItemCreateOp`.
- Źródło `Starter` w `ItemCreateOp` i gospodarz `Poi` w `ContainerCreateOp` są w formacie operacji 33 i 35 od K6-29, więc podprojekt nie zmienia ich formatu. Gospodarz `Poi` to planeta, miejsce i numer skrzyni: istnienie skrzyni w generatorze sprawdza walidator, a applier tylko to, że skrzynia nie ma jeszcze kontenera.

## Sieć

Treść nie podróżuje przez sieć: klient i serwer mają te same dane, bo obejmuje je `BuildHash`. Przez sieć idą tylko stany postępu.

| Typ | Komunikat | Kierunek i kanał | Zawartość |
| --- | --- | --- | --- |
| 60 | `PlayerJournalState` | S→K, niezawodny, tylko do właściciela | odblokowane wpisy i odwiedzone planety; przy wejściu i po zmianie |
| 61 | `PoiStates` | S→K, niezawodny | odkryte miejsca regionu terenu; razem z terenem regionu i jako delta po odkryciu |

- Dziennik celów klient liczy sam z odebranych stanów, więc cele nie mają własnego komunikatu.
- Teksty wpisów są lokalne i wybierane według języka klienta.

## Zapis

Podprojekt dokłada trzy tabele w migracji planowo M0005, tworzonej w K8-55; numer nadaje się przy scaleniu, jak w podprojekcie 3.

| Tabela | Zawartość |
| --- | --- |
| `lore_unlocks` | gracz i klucz wpisu |
| `visited_planets` | gracz i planeta |
| `discovered_pois` | planeta, miejsce i tick odkrycia |

- Skrzynie miejsc są zwykłymi wierszami `containers` z gospodarzem `Poi`.
- Skrót stanu obejmuje wszystkie trzy tabele.
- Treść nie jest zapisywana w świecie. Świat pamięta tylko klucze typów (rejestry) i profile generatorów planet.

## Obsługa błędów

Błędy treści mają wychodzić w CI, zanim trafią do graczy, a serwer nigdy nie startuje z niespójną treścią.

| Sytuacja | Reakcja |
| --- | --- |
| Walidacja treści nie przechodzi | CI odrzuca zmianę z listą błędów; serwer z niespójną treścią nie startuje |
| Aktualizacja zmienia generator planety | nowy profil generatora z nową wersją; istniejące planety zostają przy starym |
| Reguły miejsca nie dają się spełnić w danym terenie | generator przesuwa miejsce do najbliższego punktu, który je spełnia; ołtarz i obeliski są obowiązkowe |
| Gracz usunął znacznik ołtarza | ponowna aktywacja obelisku dodaje znacznik od nowa |
| Wpis albo cel wycofany z treści | klucz zostaje w danych jako wycofany, a zapisy przechowują go bez błędu |
| Zasób bez wpisu w rejestrze pochodzenia | CI odrzuca zmianę |
| Zasób ponad budżet | ostrzeżenie w CI; przekroczenie o ponad 50% blokuje scalenie |
| Gracz nie zgodził się na telemetrię | pomiar długości gry korzysta tylko z danych graczy, którzy się zgodzili |

## Testy

Najważniejszy test treści to solwer osiągalności postępu, który sprawdza cały łańcuch od zestawu startowego do bossa 4 bez grania.

| Test | Kryterium zaliczenia |
| --- | --- |
| Osiągalność postępu | solwer idzie od zestawu startowego przez źródła, receptury i klucze: każdy boss jest osiągalny, każdy przedmiot ma źródło, a części statku na każdą planetę są dostępne przed nią |
| Walidacja treści | każda planeta ma 3–5 typów stworów, bossa z kluczem i 3–4 potrawy; każdy atak bossa do uniknięcia ma zapowiedź co najmniej 616 ms |
| Generator miejsc | dla 1000 ziaren każda planeta ma ołtarz na powierzchni osiągalny pieszo, co najmniej 3 obeliski i 6 ruin; miejsca są powtarzalne z ziarna |
| Bossowie 2–4 | 1, 4 i 8 graczy pokonuje każdego bossa; skalowanie zgodne z danymi |
| Model czasu postępu | szacunek czasu każdego etapu z danych mieści się w celu, zanim zaczną się testy z ludźmi |
| Długość gry | w zamkniętych testach mediana do bossa 4 dla grup 2–4 osób wynosi 20–30 h i nie mniej niż 15 h |
| Budżety zasobów | każdy model i tekstura w budżecie; scena referencyjna każdej planety mieści się w budżecie wydajności na Decku i na PC referencyjnym |
| Rejestr pochodzenia | każdy plik w `game/assets/` ma wpis z licencją i opisem udziału AI |
| Teksty | każdy tekst w danych jest kluczem lokalizacji; kompletność tłumaczeń PL i EN sprawdza K9-61 |

## Kryteria ukończenia

- [ ] Debiutujące grupy 2–4 osób pokonują bossa 4 w medianie 20–30 h, a mediana nie spada poniżej 15 h.
- [ ] Każda z czterech planet ma własne surowce, 3–5 typów stworów, zagrożenie, jedzenie, bossa i miejsca do odkrycia.
- [ ] Solwer osiągalności postępu i walidacja treści przechodzą w CI.
- [ ] Każdy zasób ma wpis w rejestrze pochodzenia, a udział AI jest opisany do ankiety treści Steamworks.
- [ ] Sceny referencyjne planet mieszczą się w budżetach wydajności z podprojektu 6.
- [ ] Kryteria sieci i zapisu z podprojektów 1–4 przechodzą z pełną treścią.

## Pakiety pracy

Treść na EA to 8 pakietów kamienia milowego K8: Opus 5.5 prowadzi 1, Sonnet 5.5 3, a Gemini 3.8 Flash 4, w tym jeden razem z Dismondem. K8-50 czeka tylko na postać i kamerę z podprojektu 1, więc kierunek artystyczny i potok zasobów powstają równolegle z rdzeniem.

Pakiety K8-51…K8-56 zaczynają od planety domowej z bossem 1 jako pionowego wycinka. Zmierzony koszt tego wycinka wyznacza plan planet 2–4.

| Pakiet | Zakres | Prowadzi | Zależy od | Odbiór |
| --- | --- | --- | --- | --- |
| K8-50 | Kierunek artystyczny i potok zasobów: przewodnik stylu i palety, budżety, import glTF i ustawienia importu, LOD i kolizje, rejestr pochodzenia z testem CI, test wydajności scen na Decku | Sonnet 5.5 | K2-11 | scena próbna każdego rodzaju zasobu przechodzi import i budżet; rejestr pochodzenia działa w CI |
| K8-51 | Typy planet: `PlanetTypeDef`, cztery profile generatora z biomami, materiałami, florą i obiektami, profile zagrożeń, `PoiDef` i rozstawianie miejsc, strefy ochronne | Sonnet 5.5 | K5-22, K6-31, K6-34 | test generatora miejsc dla 1000 ziaren; zagrożenia działają na każdej planecie |
| K8-52 | Fauna: stwory czterech planet w danych, zachowania, łup, reguły pojawiania się, silniejsi przeciwnicy | Gemini 3.8 Flash | K7-46, K7-48, K8-51 | walidacja treści fauny; stwory pojawiają się według reguł na każdej planecie |
| K8-53 | Bossowie 2–4: dane, mechaniki, fazy, zapowiedzi, ołtarze, ofiary, nagrody | Sonnet 5.5 | K7-47, K7-48, K8-51 | test bossów 2–4 dla 1, 4 i 8 graczy |
| K8-54 | Progresja i przedmioty: drzewo receptur, poziomy materiałów, narzędzia, broń, pancerz, moduły, bloki statku według kluczy, jedzenie planet, solwer osiągalności postępu, `StartKitDef` z zestawem startowym | Gemini 3.8 Flash | K5-23, K6-32, K6-34, K7-44 | solwer i walidacja treści przechodzą na treści dostępnej przy scaleniu; pełny łańcuch sprawdza K8-57 |
| K8-55 | Kierunek i wprowadzenie: kapsuła startowa z zestawem z K8-54, dziennik celów, wskazówki, zapisy fabularne, obeliski, finał EA, operacje 60–62, `PoiInteractIntent`, osiągnięcia, statystyki i rich presence podłączone do zdarzeń gry | Gemini 3.8 Flash | K5-27, K6-39, K7-47, K8-51, K8-53, K8-54, K9-58 | w testach nowy gracz bez pomocy dochodzi do bossa 1; obeliski dodają znaczniki; osiągnięcia odblokowują się na koncie testowym |
| K8-56 | Zasoby graficzne i dźwiękowe: modele, animacje, tekstury, efekty, dźwięki i muzyka planet, stworów i bossów, import według K8-50 | Dismond z Gemini 3.8 Flash | K8-50 | każdy zasób z listy treści przechodzi import według K8-50, mieści się w budżecie i ma wpis w rejestrze pochodzenia |
| K8-57 | Balans i pomiar: arkusz balansu z danych, model czasu postępu, zamknięte testy z telemetrią, strojenie | Opus 5.5 | K5-28, K6-40, K7-49, K8-52, K8-53, K8-55, K8-56, K9-62, K9-64 | solwer potwierdza osiągalność pełnego łańcucha do finału EA; mediana do bossa 4 w zamkniętych testach 20–30 h i nie mniej niż 15 h |

## Ryzyka i otwarte kwestie

Największą niewiadomą jest koszt jednej planety z bossem przy pracy z AI, bo żadne źródło go nie mierzy; dlatego pierwsza planeta jest pionowym wycinkiem z pomiarem.

| Ryzyko | Skutek | Odpowiedź |
| --- | --- | --- |
| Planeta-zapchajdziura | gracze odbierają ją tak jak planety Astroneera | każda planeta z własnym surowcem, zagrożeniem, stworami i bossem; walidacja treści to wymusza |
| Za mało godzin gry | mediana poniżej 15 h | model czasu postępu przed testami; strojenie kosztów i wydajności w danych, bez zmian w kodzie |
| Grind zamiast treści | złe oceny mimo długiej gry | telemetria liczy także nowe rzeczy na godzinę: przedmioty, miejsca, stwory |
| Niespójny wygląd albo ryzyko licencyjne | gorszy odbiór, problem prawny | przewodnik stylu, budżety, rejestr pochodzenia z testem CI, akceptacja Dismonda |
| Wydajność na Decku | ocena gorsza niż Playable | test wydajności w K8-50 przed produkcją zasobów; budżety w danych |
| Koszt planety z bossem | planety 2–4 trwają dłużej niż zakłada plan | pionowy wycinek planety domowej z bossem 1 i pomiar, zanim ruszy reszta |

Źródła: [GamingBolt: plany Valheima](https://gamingbolt.com/valheims-future-updates-detailed-more-bosses-recipes-and-mistlands-biome), [How To Market A Game: Deep Rock Galactic Survivor](https://howtomarketagame.com/2024/04/22/what-deep-rock-galactic-survivor-can-teach-us-about-early-access/), [Wikipedia: Astroneer](https://en.wikipedia.org/wiki/Astroneer), [Wikipedia: Hytale](https://en.wikipedia.org/wiki/Hytale), [Wikipedia: Valheim](https://en.wikipedia.org/wiki/Valheim), [Indiecator: Core Keeper](https://indiecator.org/2022/03/21/indietail-core-keeper/), [GamingBolt: Nightingale](https://gamingbolt.com/nightingale-developer-not-happy-with-current-state-of-the-game-reveals-update-plans), [PCGamesN: długość Palworld](https://www.pcgamesn.com/palworld/how-long-is-length), [Steamworks: ankieta treści](https://partner.steamgames.com/doc/gettingstarted/contentsurvey).
