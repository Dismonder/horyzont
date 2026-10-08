# Specyfikacja podprojektu 4: Walka

> Kopia dokumentu z Claude Docs, stan na 2026-10-08. Wersja żywa: https://claude.ai/code/artifact/10b4fee2-b82b-4909-94cf-724f08feadaf

## Kontekst i decyzje

Podprojekt 4 dodaje walkę wręcz i dystansową ze stworami i bossami w kooperacji do 8 graczy. Trafienia rozstrzyga serwer, a kompensacja opóźnień obejmuje też stwory, bo w PvE to one są głównymi celami. Decyzje pochodzą z raportu „Otwarte decyzje projektowe Horyzontu”.

| Decyzja | Rozstrzygnięcie | Podstawa |
| --- | --- | --- |
| Kamera | zza ramienia domyślnie, pierwsza osoba zawsze dostępna; balans EA pod widok zza ramienia | prośba o pierwszą osobę w Enshrouded zebrała ok. 3 tys. głosów, głównie z powodu budowania ([Enshrouded Feature Upvote](https://enshrouded.featureupvote.com/suggestions/523850/add-first-person-camera)); walka w pierwszej osobie wypada najsłabiej ([Funcom](https://forums.funcom.com/t/shield-block-in-first-person-still-not-working/138765), [MeinMMO](https://mein-mmo.de/steam-hit-valheim-in-first-person/)) |
| Namierzanie | opcjonalne i przełączane, nigdy samo; cel najbliższy środka ekranu, pierwszeństwo dla atakujących | Enshrouded dopracował opcjonalne namierzanie w kwietniu 2026 ([Enshrouded](https://enshrouded.com/en-US/news/enshrouded-forging-the-path-is-live)); skargi dotyczą wyboru celu ([Pro Game Guides](https://progameguides.com/soulmask/soulmask-combat-guide/), [Funcom](https://forums.funcom.com/t/improve-targeting-of-enemies/225385)) |
| Broń dystansowa | łuki, potem jedna broń energetyczna na pociskach, bez hitscanu; mniej obrażeń na sekundę niż wręcz | Valheim dodał kuszę i laski ok. 22 miesiące po starcie EA ([GameSpot](https://gamespot.com/articles/valheims-latest-update-adds-new-biome-and-magic-system/1100-6509780/)); wczesna broń palna spychała walkę wręcz ([Inverse](https://www.inverse.com/gaming/dune-awakening-developers-lore-timothee-chalamet)) albo rozczarowywała ([But Why Tho](https://butwhytho.net/2024/02/nightingale-early-access-review-survival-game/)) |
| Trafienia | serwer cofa kapsuły graczy i stworów do chwili, którą widział atakujący; historia 500 ms, cofnięcie najwyżej 250 ms | formuła Valve ([Valve](https://developer.valvesoftware.com/wiki/Source_Multiplayer_Networking)); Overwatch trzyma ok. 0,5 s historii ([Edgegap](https://edgegap.com/blog/game-backend-deep-dive-overwatch-2016-netcode-architecture-rollback)); bez cofania wilk biegnący 6 m/s daje ok. 0,9 m błędu |
| Obrona | zapowiedzi ataków długie i czytelne, minimalna zapowiedź liczona z okna W − 216 ms; wątpliwe przypadki na korzyść obrońcy | przy RTT 100 ms z zamachu 600 ms zostaje ok. 384 ms na reakcję (raport, decyzja 5) |
| Obrażenia | idempotentne zdarzenia serwera z kluczem (atakujący, cios albo pocisk, ofiara, tick); do dziennika trafiają tylko skutki trwałe | zdrowie zapisuje checkpoint (podprojekt 3), więc dziennik nie rośnie z każdym ciosem, a klucz wyklucza podwójne trafienie |
| Symulacja stworów | wyłącznie na serwerze; klient interpoluje i odtwarza ogłoszone ataki na swojej osi czasu | model Valheima z symulacją u jednego klienta dziedziczy jego ping i słabnie przy 5–6 graczach ([Game Host Bros](https://www.gamehostbros.com/guides/games/valheim/troubleshooting-network), [Edgegap](https://edgegap.com/blog/valheim-multiplayer-game-backend-deep-dive)) |
| Budowle | w EA stwory nie niszczą budowli; najazdy to zarezerwowane ustawienie świata | decyzja projektowa: baza jest bezpieczna, a presję dają zagrożenia planet i bossowie |
| PvP | brak w EA; gracze nie zadają sobie obrażeń | gra jest PvE; przy PvP kompromis między atakującym a obrońcą trzeba rozstrzygnąć od nowa |

Zależności: predykcja ruchu (K3-14), replikacja bytów (K3-15), historia transformacji ram (K5-20), wiele terenów (K5-22), przedmioty i rzemiosło (K6-29, K6-32), stan postaci (K6-33) oraz śmierć i odrodzenie (K6-35).

## Cel i zakres

Na bramce K7 grupa do 8 graczy walczy wręcz i z łuku ze stworami planety domowej i pokonuje bossa 1 przy RTT do 150 ms, bez poczucia niesprawiedliwych trafień. Pozostałą faunę i bossów 2–4 dokłada podprojekt 5 jako dane na tym samym szkielecie.

W zakresie:

- walka wręcz: ataki lekkie i ciężkie, kombinacje jako dane z fazami zamachu, trafienia i powrotu, wytrzymałość, typy obrażeń, blok, parowanie, unik, pancerz i równowaga;
- kamera zza ramienia i pierwsza osoba, przejście w pierwszą osobę w ciasnych miejscach, namierzanie i wspomaganie celowania na padzie;
- trafienia w sieci: maszyna stanów ataku na serwerze, predykcja zamachu u klienta, historia kapsuł 500 ms, cofanie do 250 ms, idempotentne obrażenia;
- broń dystansowa: łuki i strzały, broń energetyczna z zasobem ciepła, pociski kinematyczne;
- stwory: zachowania, zmysły, nawigacja po kulistym terenie, pojawianie się według danych planety, replikacja i zainteresowanie;
- framework bossów: przywołanie, fazy, zapowiedzi, skalowanie od 1 do 8 graczy, nagroda i klucz postępu, a na nim boss 1;
- łup, animacje, efekty i dźwięk walki;
- narzędzia od pierwszego pakietu: wizualizator kompensacji opóźnień i symulator opóźnień 100, 150 i 250 ms.

Poza zakresem EA: PvP, walka w kosmosie i wieżyczki, niszczenie budowli przez stwory, kusze, broń automatyczna, materiały wybuchowe, broń samonaprowadzająca, wiązki hitscan, oswajanie i wierzchowce oraz umiejętności rosnące z użyciem. Kapsuły i ich historia są zapisane w ramie rodzica, więc strzał z lecącego statku do celu na planecie użyje kiedyś tej samej maszynerii.

## Rozgrywka

### Walka wręcz

- Każda broń ma zestaw ataków z danych: lekki, ciężki i kombinację do trzech lekkich ciosów. Atak ma fazy zamachu, trafienia i powrotu w tickach, koszt wytrzymałości, obrażenia według typów, odrzut i obrażenia równowagi.
- Typy obrażeń to obuchowe, cięte, kłute i energetyczne. Stwory mają mnożniki odporności i słabości z danych.
- Obrażenia po pancerzu = obrażenia · 100 / (100 + pancerz).
- Blok tarczą albo bronią zmniejsza obrażenia o siłę bloku i kosztuje wytrzymałość proporcjonalnie do zatrzymanych obrażeń. Brak wytrzymałości łamie gardę i ogłusza.
- Parowanie: blok podniesiony najwyżej 250 ms przed trafieniem odbija cios, ogłusza atakującego i podwaja obrażenia następnego ciosu w ogłuszony cel.
- Unik to przewrót z 300 ms nietykalności, kosztem wytrzymałości.
- Obrażenia równowagi sumują się i zanikają z czasem. Przekroczenie progu ogłusza na czas z danych.
- Wspomaganie ciosu obraca postać najwyżej o 15° na atak w stronę celu w stożku 20°. Cel pochodzi z id celu w komendzie gracza, więc obrót liczy się deterministycznie i nie psuje predykcji.
- Kierunek ciosu wynika z orientacji postaci, a obszar trafienia z krzywej broni w ataku, niezależnie od kamery.
- Gracze nie zadają sobie obrażeń. Po odrodzeniu (podprojekt 3) postać ma 10 s nietykalności, która znika przy jej pierwszym ataku.

### Kamera

- Domyślna kamera stoi za ramieniem, przesunięta w bok. Klawisz zmienia ramię, a ramię kamery cofa się przy przeszkodach.
- Kamera oddala się, gdy atakujący albo namierzony przeciwnik jest przy krawędzi ekranu, oraz przy wielkich bossach.
- Pierwsza osoba jest zawsze dostępna. Opcja „pierwsza osoba w ciasnych miejscach”, domyślnie włączona, przełącza sama, gdy ramię kamery jest krótsze niż 0,6 m przez 0,5 s.
- Na planecie „góra” kamery to wygładzony kierunek od środka planety, a obrót i pochylenie liczą się kwaternionami w płaszczyźnie stycznej. Na budowli „górę” daje rama budowli.
- Broń dystansowa celuje wzdłuż promienia kamery z korekcją paralaksy: raycast z kamery daje punkt celowania, a pocisk leci z lufy do tego punktu.

### Namierzanie

- Namierzanie włącza i wyłącza klawisz; gra nigdy nie włącza go sama.
- Cel to przeciwnik najbliższy środka ekranu w zasięgu 30 m i z linią wzroku. Pierwszeństwo mają atakujący gracza, potem inni przeciwnicy, potem stwory neutralne; bierne zwierzęta tylko po wyraźnym wyborze.
- Zmiana celu to ruch myszy w dwóch osiach albo gałka pada w jednej osi. Cel jest wyraźnie oznaczony.
- Bossowie mają ręcznie ustawione punkty namierzania, a obrót do celu przy dużych przeciwnikach jest ograniczony, żeby gra nie wkręcała gracza w kapsuły trafień.
- Celowanie bronią dystansową zrywa namierzanie. Wspomaganie celowania, czyli spowolnienie przy celu, ma tylko pad.

### Broń dystansowa

- Łuk: naciąg i przytrzymanie kosztują wytrzymałość, a pełny naciąg daje pełną prędkość strzały. Strzały lecą balistycznie i opadają ku środkowi planety, bo grawitację pocisku daje rama.
- Strzały się wytwarza, więc ich produkcja jest bramką rzemiosła. Strzała wbita w teren albo budowlę zostaje przedmiotem leżącym z szansą z danych, domyślnie 50%.
- Broń energetyczna od środka gry strzela pociskami bez grawitacji, szybszymi od strzał, a każdy strzał ją grzeje. Przegrzanie blokuje strzelanie do schłodzenia.
- Broń energetyczna zadaje mniej obrażeń na sekundę niż walka wręcz na tym samym poziomie. Narzędzie do kopania nie ma znaczącej siły bojowej.
- Rzucany oszczep jest opcją treści podprojektu 5 na tym samym systemie pocisków.

### Stwory

- Każdy typ planety ma 3–5 typów stworów (podprojekt 5). Zachowanie to dane: bierne uciekają, neutralne się bronią, agresywne atakują w zasięgu wykrycia, a stadne walczą razem.
- Zmysły to wzrok w stożku z linią wzroku i słuch reagujący na bieg, walkę i kopanie.
- Stany zachowania: spoczynek, wędrówka, czujność, pościg, atak, ucieczka i powrót na swój teren.
- Stwory pojawiają się według reguł planety (biom, pora dnia, gęstość) w pierścieniu 40–80 m od graczy, poza ich wzrokiem. Limit to 10 stworów na gracza i 64 na serwer, bez bossów i ich przywołańców.
- Stwór, który nie walczy i jest dalej niż 150 m od graczy, znika. Stwory nie są trwałe, więc po restarcie serwera pojawiają się od nowa.
- Stwory nie niszczą budowli i nie atakują bloków. Do budowli wchodzą tylko przez otwarte drzwi.
- Łup ze stwora to przedmioty leżące z tabeli łupu. Losowanie jest deterministyczne, z ziarnem z identyfikatora bytu i ticku śmierci.

### Bossowie

- Bossa przywołuje się ofiarą z danych przy jego ołtarzu. Przywołanie zapisuje aktywne starcie w stanie świata, więc po restarcie serwera boss pojawia się znowu przy ołtarzu, a ofiara nie przepada.
- Boss ma fazy przy progach zdrowia, np. 66% i 33%, każdą z własnym zestawem ataków.
- Każdy atak bossa ma zapowiedź: animację, efekt i dźwięk. Zapowiedź ataku, którego trzeba uniknąć, trwa co najmniej 400 ms reakcji plus 216 ms, czyli 616 ms; obie wartości są danymi.
- Zdrowie i mechaniki skalują się od 1 do 8 graczy. Domyślnie zdrowie rośnie o 60% za każdego gracza ponad pierwszego w promieniu 100 m, liczonych przy przywołaniu i przy zmianie fazy.
- Gdy przez 60 s żaden gracz nie jest w promieniu 100 m, boss wraca do ołtarza z pełnym zdrowiem, a starcie trwa dalej.
- Pokonanie bossa daje klucz postępu świata i zwiększa licznik pokonań. Łup bossa pojawia się przy nim jako przedmioty leżące, osobny zestaw dla każdego gracza obecnego w walce.
- Pokonanego bossa można przywołać ponownie dla łupu, ale klucz postępu świat dostaje tylko raz.

## Architektura i model danych

Walka to czyste funkcje w Core na danych z treści, a obrażenia powstają wyłącznie na serwerze; klient przewiduje tylko własny zamach i efekty.

```
Obrażenia powstają tylko na serwerze, w chwili, którą widział atakujący

Klient atakującego        ->  Maszyna ataku                ->  Cofnięcie kapsuł
przewiduje własny zamach      zamach, trafienie, powrót        ofiary w chwili renderu
komenda: tick, renderowana    faza trafienia liczona           klienta, najwyżej 250 ms
para interpolacji, id celu    w ticku serwera                  z historii 500 ms
                                                                    │
                                                                    ▼
Skutki                    <-  DamageEvent z kluczem        <-  Reguły walki w Core
zdrowie: pamięć, checkpoint   atakujący, cios, ofiara, tick    pancerz, blok, parowanie
śmierć, łup, boss: operacja   stosowany najwyżej raz           unik, równowaga,
złożona w dzienniku                                            odporności
```

Cofnięcie dotyczy tylko sprawdzenia trafienia. Obrażenia, ogłuszenie i zabicie działają u wszystkich dopiero po zdarzeniu z serwera.

### Ataki i obrażenia

- `AttackDef` (dane) ma fazy w tickach, koszt, obrażenia według typów, równowagę, odrzut i krzywą broni: kapsułę broni w czasie ataku, próbkowaną z animacji przy imporcie.
- `CombatRules` w Core liczą pancerz, blok, parowanie, nietykalność uniku, równowagę i odporności. To czyste funkcje, wspólne dla serwera i klienta.
- Maszyna stanów ataku (gotowość, zamach, trafienie, powrót, ogłuszenie) działa autorytatywnie na serwerze dla graczy i stworów. Klient gracza uruchamia tę samą maszynę w predykcji.
- `DamageEvent` ma klucz (atakujący, id ciosu albo pocisku, ofiara, tick) i obrażenia według typów. Serwer stosuje zdarzenie o danym kluczu najwyżej raz i pamięta klucze przez 2 s.

### Kapsuły i historia

- Każdy byt bojowy ma kilka ręcznie ustawionych kapsuł na kluczowych kościach. Serwer bez grafiki nie liczy `AnimationTree`, tylko bierze pozy kapsuł z krzywych zapisanych przy imporcie animacji.
- `HitHistory` to bufor cykliczny 500 ms (30 ticków) na byt: poza korzenia w ramie rodzica, `ParentSeq` i stan animacji (klip i czas). Leży obok historii transformacji ram z K5-20, więc cofnięcie działa też między ramami.
- `PlayerInput` dostaje renderowaną parę interpolacji (tick „od”, tick „do”, współczynnik) i opcjonalne id celu. Zmiana formatu `InputBatch` podbija `ProtocolVersion`.
- Serwer cofa kapsuły do chwili, którą klient renderował według tej pary, najwyżej 250 ms wstecz. Komendę starszą rozstrzyga w najstarszej dozwolonej chwili.

### Pociski

- Pocisk (rodzaj bytu 5) nie jest trwały. Stwory, pociski i bossowie dostają `EntityId` z puli nietrwałej (od 2^32) z K1-03, poza checkpointem i skrótem stanu. Zdarzenie wystrzału niesie tick, punkt startu, prędkość, broń, ziarno losowości i ramę, a tor to czysta funkcja z grawitacją ramy, liczona tak samo u klienta i na serwerze.
- Serwer tworzy pocisk autorytatywny przesunięty do przodu o opóźnienie strzelca, najwyżej 250 ms. Odcinki toru z tego przesunięcia sprawdza na cofniętych kapsułach, a dalsze w czasie bieżącym.
- Strzelec od razu widzi pocisk kosmetyczny o tym samym identyfikatorze i dosuwa go płynnie do toru serwera. Pozostali klienci dostają zdarzenie wystrzału i liczą tor sami.

### Stwory

- `CreatureDef` (dane) ma zdrowie, odporności, kapsuły, ataki, zmysły, zachowanie, prędkości i łup. `CreatureBrain` to maszyna stanów na serwerze z decyzjami 10 razy na sekundę i ruchem w każdym ticku.
- Nawigacja jest własna, bo planeta jest kulą, a siatkę nawigacji Godota piecze się względem jednej osi pionowej. Siatka regionu 64 m ma komórki 1 m na powierzchni terenu i bloków i powstaje leniwie wokół graczy.
- A\* idzie po siatce z kosztem nachylenia, a lokalne omijanie rozwiązuje kolizje z innymi bytami. Edycja terenu albo budowli unieważnia tylko dotknięte komórki.
- Reguły pojawiania się (`SpawnRules`) są danymi planety. Spawner działa w ticku serwera i pilnuje limitów.

### Bossowie

- `BossDef` (dane) ma fazy z progami, ataki z zapowiedziami, wzór skalowania, ofiarę, ołtarz, łup i klucz postępu.
- Aktywne starcie (`BossEncounter`: boss, ołtarz, rama, liczba graczy przy przywołaniu) jest stanem świata z dziennika. Byt bossa nie jest trwały i powstaje ze starcia.
- Pokonanie bossa zwiększa licznik pokonań i ustawia klucz postępu (`ProgressKeyGrantOp` z podprojektu 3) w jednej operacji złożonej razem z łupem.

## Operacje i dziennik

Ciosy, obrażenia i ruch stworów nie trafiają do dziennika. Trafiają tam zużycie amunicji, łup, przywołanie i pokonanie bossa oraz śmierć gracza z podprojektu 3, bo od nich zależy dalszy stan świata.

| Kod | Operacja | Pola | Warunki wstępne i skutek |
| --- | --- | --- | --- |
| 50 | `BossEncounterStartOp` | starcie, boss, planeta, ołtarz (obiekt generatora), liczba graczy | boss nie ma aktywnego starcia; tworzy starcie |
| 51 | `BossDefeatedOp` | starcie, tick | starcie istnieje; kończy je i zwiększa licznik pokonań bossa |

| Akcja | Składowe operacji złożonej |
| --- | --- |
| Strzał z łuku | `ItemDestroyOp` na strzale (ujście `Ammo`) |
| Odzyskanie strzały | `PersistentEntityCreateOp`, `ContainerCreateOp`, `ItemCreateOp` (źródło `Recovered`) |
| Łup ze stwora | `PersistentEntityCreateOp`, `ContainerCreateOp`, `ItemCreateOp` (`Loot`) |
| Przywołanie bossa | `ItemDestroyOp` na ofierze (ujście `Offering`), `BossEncounterStartOp` |
| Pokonanie bossa | `BossDefeatedOp`, `ProgressKeyGrantOp` tylko wtedy, gdy świat nie ma jeszcze klucza, łup dla każdego gracza obecnego w walce |

- Wartości `Recovered` (źródło) oraz `Ammo` i `Offering` (ujścia) są w enumach operacji 33 i 34 od pierwszego formatu z K6-29, więc podprojekt nie zmienia ich formatu.
- Ataki, uniki i strzały idą przez `PlayerInput`, a nie przez intencje, bo są częste i przewidywane. Serwer sam tworzy operacje amunicji, łupu i bossów.
- Jedyna nowa intencja to `BossSummonIntent` (kod 40): planeta, ołtarz, kontener i slot ofiary.
- Nowe kody odmowy: `BossAlreadyActive` 60 i `WrongOffering` 61. Zbyt dużą odległość od ołtarza zgłasza `OutOfReach` z K1-03.
- Stwory, pociski i stan walki nie są w checkpoincie. Po awarii serwera stwory pojawiają się od nowa, a aktywne starcia bossów wracają z dziennika.

## Sieć

Klient przewiduje własny zamach i własny pocisk, a ataki stworów pokazuje na swojej przewidywanej osi czasu, żeby obrona miała jak najdłuższe okno.

| Typ | Komunikat | Kierunek i kanał | Zawartość |
| --- | --- | --- | --- |
| 50 | `CombatEvents` | S→K, zawodny, każde zdarzenie powtórzone w 3 kolejnych pakietach | ogłoszenia ataków (byt, atak, tick startu), zdarzenia wystrzału, trafienia (ofiara, obrażenia, punkt, efekt), ogłuszenia, parowania, śmierci |
| 51 | `BossStatus` | S→K, zawodny, 4 Hz, do graczy w promieniu 200 m | boss, faza, zdrowie, liczba graczy w skalowaniu |

- `PlayerInput` dostaje renderowaną parę interpolacji, id celu, aktywny slot paska i nowe przyciski: atak lekki, atak ciężki, blok, unik i naciąg. Każda z tych zmian podbija `ProtocolVersion`.
- `EntityState` dostaje pola zdrowia (ułamek w bajcie) i stanu walki (atak i faza). Nowe bity w `EntityStateFields` zmieniają wersję formatu ramki migawki, a trzy nowe bajty mieszczą się w zapasie z K1-03: 22 stany po 43 B, 8 ram i nagłówek to 1103 B.
- Stwory (rodzaj bytu 4), pociski (5) i bossowie (7) mają w `EntitySpawn` pole `Archetype` równe indeksowi swojej definicji (`CreatureDef`, typ pocisku, `BossDef`) w posortowanej liście kluczy treści i puste `KindData`. Klient ma tę samą listę, bo treść obejmuje `BuildHash`.
- Zdarzenia mają numery kolejne, a klient odrzuca powtórzenia. Obrażenia, ogłuszenie i śmierć pokazuje dopiero po zdarzeniu, a efekty trafienia już w predykcji.
- Klient pokazuje ogłoszony atak stwora od razu w swoim czasie przewidywanym i pomija początek zamachu, który już minął. Unik albo blok gracza trafia wtedy na serwer w tym samym ticku, który gracz widział, a okno reakcji rośnie do ok. W − 116 ms.
- Trafienie stwora w gracza serwer rozstrzyga z opóźnieniem równym buforowi wejść tego gracza, najwyżej 100 ms. Spóźniony pakiet z unikiem albo blokiem o wcześniejszym ticku wciąż się liczy, czyli wątpliwy przypadek wygrywa obrońca.
- Stwory mają promień zainteresowania 120 m. Gdy w zasięgu jest więcej bytów, niż mieści migawka, serwer wybiera według priorytetu: walka z odbiorcą, odległość, czas od ostatniej wysyłki.
- Walidacja strzału sprawdza szybkostrzelność, amunicję albo ciepło, zasięg, linię wzroku w chwili cofnięcia i limit cofnięcia. Serwer nigdy nie przyjmuje trafień zgłoszonych przez klienta ([GameDev.net](https://gamedev.net/forums/topic/683828-server-validation-for-client-side-weapon-hit-detection/)).
- Wizualizator kompensacji opóźnień rysuje kapsuły z chwili cofnięcia i z chwili bieżącej, a symulator łącza z K1-06 daje profile 100, 150 i 250 ms z wahaniami i stratami.

## Zapis

Podprojekt dokłada jedną tabelę i jeden klucz `meta` w migracji planowo M0004; numer nadaje się przy scaleniu, jak w podprojekcie 3.

| Tabela | Zawartość | Blob (kod) |
| --- | --- | --- |
| `boss_encounters` | aktywne starcia bossów | `BossEncounter` (30) |
| `meta` (istniejąca) | klucz `boss_stats`: liczba pokonań każdego bossa | `BossStats` (31) |

- Stwory, pociski, historia kapsuł i stan walki nie są zapisywane.
- Po wczytaniu każde aktywne starcie tworzy bossa przy ołtarzu z pełnym zdrowiem.
- Skrót stanu obejmuje starcia i liczniki pokonań, a pomija wszystko, czego dziennik nie zapisuje.

## Obsługa błędów

Przy każdej rozbieżności wygrywa stan serwera, a gracz traci najwyżej efekt kosmetyczny, nigdy zdrowie bez potwierdzenia.

| Sytuacja | Reakcja |
| --- | --- |
| Komenda ataku spóźniona ponad limit cofnięcia | serwer rozstrzyga ją w najstarszej dozwolonej chwili (250 ms wstecz) i liczy spóźnienie w metrykach |
| Klient przewidział trafienie, którego serwer nie potwierdził | efekt był tylko kosmetyczny; zdrowie, ogłuszenie i śmierć nie zmieniły się u nikogo |
| Powtórzone zdarzenie obrażeń | klucz zdarzenia odrzuca drugie zastosowanie |
| Pocisk kosmetyczny rozjeżdża się z torem serwera | dosuwanie przez 100–200 ms; przy różnicy ponad 2 m pocisk kosmetyczny znika i zostaje serwerowy |
| Gracz rozłącza się w walce z bossem | starcie trwa; gdy przez 60 s nikt nie jest w promieniu 100 m, boss wraca do ołtarza |
| Awaria serwera w walce z bossem | starcie wraca z dziennika, boss pojawia się przy ołtarzu z pełnym zdrowiem, a ofiara nie przepada |
| Stwór nie ma ścieżki przez 5 s | wraca do wędrówki; po 30 s bez ruchu znika, jeśli nikt go nie widzi |
| Boss nie ma ścieżki do celu przez 10 s | wraca na ołtarz z zachowaniem zdrowia |
| Region bez gotowej siatki nawigacji | spawner nie tworzy w nim stworów, a stwory czekają w miejscu |
| W zasięgu więcej bytów, niż mieści migawka | priorytet migawek; pominięte byty dostają wyższy priorytet w następnej |
| Próba strzału bez amunicji albo z przegrzaną bronią | serwer nie tworzy pocisku; klient pokazuje brak strzały już w predykcji |

## Testy

Wizualizator kompensacji i symulator opóźnień powstają w pierwszym pakiecie, bo bez powtarzalnego śladu błędów czasowych nie da się diagnozować.

| Test | Kryterium zaliczenia |
| --- | --- |
| Kompensacja opóźnień | przy RTT 100, 150 i 250 ms cios, który atakujący widział jako trafiony w wilka biegnącego 6 m/s, trafia w co najmniej 95% prób; cofnięcie nigdy nie przekracza 250 ms |
| Bez podwójnych trafień | 10 000 ciosów i strzałów przy 5% strat i powtórzeniach pakietów: każde zdarzenie obrażeń zastosowane dokładnie raz |
| Okno obrony | bot z RTT 100 ms robi unik 400 ms po pojawieniu się zapowiedzi i unika ataku z zapowiedzią 616 ms w co najmniej 95% prób |
| Obrońca wygrywa wątpliwe | unik z tickiem wcześniejszym niż trafienie, dostarczony z opóźnieniem do 100 ms, chroni przed trafieniem |
| Kapsuły bez grafiki | pozy kapsuł z zapisanych krzywych na serwerze zgadzają się z pozami z `AnimationTree` u klienta z dokładnością 5 cm |
| Tor pocisku | tor u klienta i na serwerze identyczny dla tych samych danych wystrzału; strzała opada ku środkowi każdej z czterech planet |
| Namierzanie | atakujący mają pierwszeństwo, potem najbliższy środka ekranu; bierne zwierzęta nigdy nie są wybierane same |
| Nawigacja | stwór dochodzi do gracza po stoku 30°, przez jaskinię i wokół budowli na każdej planecie |
| Wydajność | 8 graczy i 64 stwory w jednym miejscu: tick średnio do 8 ms, pasmo do 50 KB/s na klienta |
| Boss 1 | 1, 4 i 8 graczy pokonuje bossa; skalowanie zgodne z danymi; restart w trakcie starcia przywraca bossa przy ołtarzu |
| Łup | to samo ziarno daje ten sam łup; łup zachowuje niezmiennik przedmiotów z podprojektu 3 |
| Odtwarzanie | checkpoint plus powtórka dziennika z przywołaniami, pokonaniami i amunicją daje ten sam skrót stanu |

## Kryteria ukończenia

- [ ] 8 graczy walczy ze stworami planety domowej i pokonuje bossa 1 przy RTT do 150 ms.
- [ ] Przy RTT 100–250 ms trafienia zgadzają się z tym, co widział atakujący, w co najmniej 95% prób, a żadne zdarzenie obrażeń nie działa dwa razy.
- [ ] Okno reakcji na zapowiedziane ataki bossa wynosi co najmniej 400 ms przy RTT 100 ms.
- [ ] Serwer z 8 graczami i 64 stworami utrzymuje tick średnio do 8 ms, a pasmo na klienta nie przekracza 50 KB/s.
- [ ] Starcie z bossem przetrwa restart i awarię serwera bez utraty ofiary.
- [ ] Wszystkie testy z sekcji Testy przechodzą w CI albo w opisanych przebiegach na sprzęcie referencyjnym.

## Pakiety pracy

Walka to 9 pakietów kamienia milowego K7: Opus 5.5 prowadzi 1, Sonnet 5.5 6, GPT-6.1 Sol 1, a Gemini 3.8 Flash 1. Plany wykonawcze pisze Opus 5.5 z tej specyfikacji przed startem każdego pakietu, gdy jego zależności są scalone.

| Pakiet | Zakres | Prowadzi | Zależy od | Odbiór |
| --- | --- | --- | --- | --- |
| K7-41 | Trafienia w sieci: kapsuły z krzywych animacji, `HitHistory`, cofanie do 250 ms, `DamageEvent` z kluczem, zmiany `PlayerInput` i `EntityState`, `CombatEvents`, rozstrzyganie na korzyść obrońcy, pula `EntityId` nietrwałych, wizualizator kompensacji, profile symulatora łącza | Opus 5.5 | K5-20 | test kompensacji, test bez podwójnych trafień, test obrońcy i test kapsuł bez grafiki na atakach i celach testowych; pełne testy w K7-49 |
| K7-42 | System walki: `AttackDef`, `CombatRules`, maszyna stanów ataku, kombinacje, blok, parowanie, unik, równowaga, pancerz, wspomaganie ciosu, nietykalność po odrodzeniu | Sonnet 5.5 | K7-41, K6-33, K6-35 | testy reguł walki; ataki z danych zgodne w predykcji i na serwerze |
| K7-43 | Kamera i namierzanie: kamera zza ramienia, zmiana ramienia, oddalanie, pierwsza osoba w ciasnych miejscach, „góra” kamery na kuli i na budowli, namierzanie, wspomaganie celowania na padzie | Sonnet 5.5 | K7-42, K5-22 | test namierzania; kamera działa na czterech planetach i na budowli |
| K7-44 | Broń dystansowa: łuki, strzały i odzyskiwanie, broń energetyczna z ciepłem, pociski kinematyczne, pocisk kosmetyczny, korekcja paralaksy, amunicja w dzienniku | Sonnet 5.5 | K7-41, K7-43, K6-32 | test toru pocisku; strzały zużywają amunicję zgodnie z niezmiennikiem |
| K7-45 | AI i nawigacja: `CreatureDef`, `CreatureBrain`, zmysły, siatka nawigacji regionu, A\*, omijanie, unieważnianie siatki | GPT-6.1 Sol | K7-42, K5-22 | test nawigacji na czterech planetach; 64 stwory w budżecie ticku |
| K7-46 | Replikacja i pojawianie się stworów: `SpawnRules`, limity, znikanie, zainteresowanie 120 m, priorytet migawek, ataki w czasie przewidywanym, łup | Sonnet 5.5 | K7-41, K7-45 | test okna obrony; pasmo do 50 KB/s przy 64 stworach |
| K7-47 | Framework bossów i boss 1: `BossDef`, ołtarze, przywołanie, starcia w dzienniku, fazy, zapowiedzi, skalowanie, powrót na ołtarz, łup i klucz postępu, `BossStatus`, migracja | Sonnet 5.5 | K7-46, K6-32, K6-35 | test bossa 1 dla 1, 4 i 8 graczy; restart w trakcie starcia |
| K7-48 | Animacje, efekty i dźwięk walki: animacje klienta, eksport krzywych kapsuł i broni, efekty trafień, dźwięki, pasek zdrowia bossa | Gemini 3.8 Flash | K7-42, K7-47 | test kapsuł bez grafiki na docelowych animacjach; każdy atak ma efekt i dźwięk |
| K7-49 | Testy walki: boty walczące przy RTT 100, 150 i 250 ms, test okna obrony, scenariusze regresji | Sonnet 5.5 | K7-44, K7-47, K7-48 | wszystkie kryteria ukończenia spełnione |

## Ryzyka i otwarte kwestie

Największa niewiadoma to pozy kapsuł z zapisanych krzywych na bezgłowym serwerze Godota, bo nie znaleźliśmy źródła, które opisuje to podejście.

| Ryzyko | Skutek | Odpowiedź |
| --- | --- | --- |
| Kapsuły z krzywych rozjeżdżają się z animacją klienta | trafienia niezgodne z obrazem | test kapsuł bez grafiki w K7-41; w razie rozjazdu kapsuły tylko na korzeniu i głównych kościach |
| Krótkie okno obrony przy dużym RTT | walka wydaje się niesprawiedliwa | ataki stworów w czasie przewidywanym, rozstrzyganie na korzyść obrońcy, minimalne zapowiedzi w danych |
| Koszt nawigacji na kuli | tick ponad budżet przy wielu stworach | siatka leniwa wokół graczy, decyzje 10 razy na sekundę, limit 64 stworów |
| Błędy czasowe trudne do diagnozy | modele nie znajdują przyczyny bez śladu | wizualizator, symulator łącza, deterministyczna pętla serwera z logiem wejść, nakładki z id ramy i `ParentSeq` |
| Pasmo przy wielu stworach | ponad 50 KB/s na klienta | priorytet migawek, zainteresowanie 120 m, stan walki w kilku bitach |
| Broń energetyczna dominuje | walka wręcz traci sens | mniej obrażeń na sekundę niż wręcz, zasób ciepła, balans w podprojekcie 5 |

Źródła: [Enshrouded Feature Upvote: pierwsza osoba](https://enshrouded.featureupvote.com/suggestions/523850/add-first-person-camera), [Funcom: blok w pierwszej osobie](https://forums.funcom.com/t/shield-block-in-first-person-still-not-working/138765), [MeinMMO: Valheim w pierwszej osobie](https://mein-mmo.de/steam-hit-valheim-in-first-person/), [Enshrouded: Forging the Path](https://enshrouded.com/en-US/news/enshrouded-forging-the-path-is-live), [Pro Game Guides: Soulmask](https://progameguides.com/soulmask/soulmask-combat-guide/), [Funcom: namierzanie](https://forums.funcom.com/t/improve-targeting-of-enemies/225385), [GameSpot: Mistlands](https://gamespot.com/articles/valheims-latest-update-adds-new-biome-and-magic-system/1100-6509780/), [Inverse: Dune Awakening](https://www.inverse.com/gaming/dune-awakening-developers-lore-timothee-chalamet), [But Why Tho: Nightingale](https://butwhytho.net/2024/02/nightingale-early-access-review-survival-game/), [Valve: Source Multiplayer Networking](https://developer.valvesoftware.com/wiki/Source_Multiplayer_Networking), [Valve: Lag Compensation](https://developer.valvesoftware.com/wiki/Lag_Compensation), [Edgegap: Overwatch](https://edgegap.com/blog/game-backend-deep-dive-overwatch-2016-netcode-architecture-rollback), [Steve Streeting: pociski w Unreal](https://www.stevestreeting.com/2024/12/12/unreal-network-prediction-for-projectiles/), [Game Host Bros: sieć Valheima](https://www.gamehostbros.com/guides/games/valheim/troubleshooting-network), [Edgegap: Valheim](https://edgegap.com/blog/valheim-multiplayer-game-backend-deep-dive), [GameDev.net: walidacja trafień](https://gamedev.net/forums/topic/683828-server-validation-for-client-side-weapon-hit-detection/).
