# Specyfikacja podprojektu 1: Planeta

> Kopia dokumentu z Claude Docs, stan na 2026-10-08. Wersja żywa: https://claude.ai/code/artifact/da1b6ac3-c214-4af6-af7a-fa177fe3a0a3

## Kontekst i decyzje

Podprojekt 1 buduje fundament całej gry: kulistą planetę, po której kilku graczy w co-opie chodzi, kopie i buduje, a świat przetrwa restart i awarię serwera.

- Gra łączy Minecrafta (kopanie i budowanie), Valheima (progresja, walka, co-op) i No Man's Sky (kuliste planety, przeloty, galaktyka).
- Cel: wydanie na Steamie we wczesnym dostępie, z co-opem od pierwszej wersji (gra u hosta i serwer dedykowany).
- Zasada pracy: każdy etap to kod docelowy, bez prototypów do wyrzucenia, a każdy podprojekt kończy się czymś grywalnym.

Kolejność podprojektów:

1. Planeta (ten dokument)
2. Kosmos: statek, start i lądowanie bez ekranu ładowania, układ gwiezdny, mapa galaktyki
3. Przetrwanie: zbieranie, ekwipunek, crafting, potrzeby postaci, dzień i noc
4. Walka: stwory, AI, walka ze staminą, pierwszy boss
5. Treść na Early Access: planety jako kolejne biomy, bossowie jako bramy, ulepszenia statku
6. Wydanie: Steam, serwer dedykowany do pobrania, optymalizacja

| Obszar | Decyzja | Dlaczego |
| --- | --- | --- |
| Silnik | Godot 4 z .NET (C#), własna kompilacja z podwójną precyzją | nowoczesny C#, licencja MIT, duże światy bez przesuwania początku układu |
| Teren | gładkie voxele: moduł Voxel Tools (VoxelLodTerrain, Transvoxel) | gotowe LOD, wątki, strumieniowanie i narzędzia edycji |
| Budowanie | bloki 1 m na własnych siatkach wyrównanych do grawitacji | brak zniekształceń na kuli, jak w Space Engineers |
| Mapa | kuliste planety, płynne przejście orbita–powierzchnia | mapa jak w No Man's Sky |
| Co-op | od pierwszego wydania: host i serwer dedykowany | jeden z głównych powodów zakupu survivali |
| Sieć | własny netcode w C#, serwer autorytatywny, Steam Networking Sockets + UDP | kontrola pasma, predykcja, spójność z dziennikiem |
| Zapis | SQLite na świat, checkpoint + dziennik operacji | odtworzenie po awarii bez utraty zmian i duplikatów |

## Cel i zakres

Efekt grywalny: do 8 graczy na jednej kulistej planecie chodzi z lokalną grawitacją, kopie, zasypuje i buduje z bloków, a zmiany przetrwają restart i awarię serwera.

W zakresie:

- budowanie silnika i SDK jednym skryptem, CI;
- tryby: gra solo, host, klient, serwer dedykowany (Windows i Linux);
- planeta: generacja, LOD od orbity do powierzchni, kolizje, grawitacja, wizualny cykl dnia i nocy;
- postać: ruch po kuli, kamera FPP i TPP, predykcja ruchu po stronie klienta;
- narzędzia: kopanie i zasypywanie terenu, stawianie i usuwanie bloków;
- sieć: połączenie, sesja, replikacja ruchu, budowli i terenu; Steam: lobby i zaproszenia;
- zapis: checkpointy, dziennik, odtwarzanie po awarii, kopie zapasowe;
- minimalny interfejs: menu główne (graj solo, hostuj, dołącz), lista światów, HUD deweloperski.

| Poza zakresem | Trafia do |
| --- | --- |
| statki, przeloty, wiele planet w grze | Kosmos |
| zasoby, ekwipunek, crafting, potrzeby postaci, wytrzymałość konstrukcji | Przetrwanie |
| stwory, walka, bossowie | Walka |
| biomy i typy planet | Treść na Early Access |
| osiągnięcia, chmura Steam, serwery na liście Steama, bilety Steama dla połączeń UDP | Wydanie |
| mody, konsole, gra między platformami | po wydaniu |

## Architektura

Jeden kod działa w trzech trybach, a serwer i klient rozmawiają wyłącznie przez transport, więc gra solo i co-op idą tą samą ścieżką.

```
Klient (Godot)  <->  Transport (Steam · UDP · pamięć)  <->  Serwer (Godot)  ->  Persistence (SQLite)
render, wejście,     do serwera: intencje                    symulacja, walidacja,  checkpoint + dziennik
interpolacja,        do klienta: operacje, teren, migawki    operacje, dziennik,
predykcja                                                     teren autorytatywny
            Core: identyfikatory, stan, operacje, serializacja, protokół (czysty .NET, bez Godota)
```

Gra solo i host idą tą samą ścieżką co co-op: transport w pamięci zastępuje sieć, a teren hosta jest współdzielony z serwerem.

| Tryb | Procesy | Uwagi |
| --- | --- | --- |
| Gra solo / host | serwer i klient w jednym procesie, transport w pamięci | zdalni gracze przez Steam lub UDP; teren współdzielony, klient czyta autorytatywny teren serwera bez drugiej kopii |
| Klient | sam klient | własna instancja terenu zasilana danymi z serwera |
| Serwer dedykowany | sam serwer, bez grafiki (Windows, Linux) | konfiguracja z pliku; konsola: save, stop, players, kick |

Rozdział serwera i klienta:

- Serwer i klient to osobne poddrzewa sceny: `/Root/Server` (symulacja, walidacja, dziennik, zapis) i `/Root/Client` (render, wejście, interpolacja, predykcja, UI, dźwięk).
- Klient nie zmienia stanu świata, tylko wysyła intencje. Wyjątki to przewidywany ruch własnej postaci i podgląd własnych edycji, które serwer potwierdza albo koryguje.
- Serwer liczy fizykę wszystkich graczy. Zdalny klient buduje kolizje terenu i budowli tylko na potrzeby predykcji; host ich nie dubluje.

| Projekt | Zależy od | Odpowiada za |
| --- | --- | --- |
| Core | nic | identyfikatory, model stanu, operacje i walidacja, dziennik, serializacja binarna, protokół |
| Persistence | Core | SQLite: checkpointy, dziennik, migracje schematu, kopie zapasowe |
| Net | Core | interfejs transportu; implementacje UDP (LiteNetLib), Steam (Steamworks.NET), w pamięci |
| Game | wszystkie | projekt Godota: sceny, render, wejście, fizyka postaci, adapter Voxel Tools, serwer i klient, skład zależności |
| Testy | testowane projekty | xUnit dla Core, Persistence i Net; testy integracyjne w Godocie bez grafiki |

Zasady kodu: Core, Persistence i Net nie znają Godota, więc testuje się je bez silnika. Stan gry nie żyje w statycznych singletonach; zależności składa jedno miejsce startowe (composition root). Włączone typy nullowalne, ostrzeżenia jako błędy, wspólny `.editorconfig`.

Budowanie silnika:

1. Plik `engine.json` przypina wersje: Godot (stabilna, zgodna z Voxel Tools 1.7), tag Voxel Tools i opcje (`precision=double`, .NET).
2. Skrypt pobiera źródła, dokłada moduł, buduje edytor, generuje glue .NET i buduje zestawy C# z podwójną precyzją.
3. Paczki SDK trafiają do lokalnego źródła NuGet, z którego korzysta projekt gry przez `nuget.config`.
4. Skrypt buduje szablony eksportu: Windows i Linux, dla gry i serwera.
5. CI (GitHub Actions) buduje silnik z cache według skrótu konfiguracji, uruchamia testy i eksportuje buildy.

## Model świata

Planeta to nieruchomy, kulisty teren z voxeli o promieniu 4 km, z grawitacją do środka i budowlami z bloków na własnych siatkach.

Hierarchia: świat (zapis) → galaktyka z ziarna → układy gwiezdne → planety. Podprojekt 1 ma jeden układ i jedną planetę, ale model od razu obsługuje wiele.

| Identyfikator | Postać | Skąd |
| --- | --- | --- |
| `WorldId` | GUID | nadany przy tworzeniu świata |
| `GalaxySeed` | 64 bity | losowany przy tworzeniu świata |
| `PlanetId` | 64 bity | wyprowadzony z ziarna i indeksu planety |
| `EntityId` | 64 bity | nadawany przez serwer, rosnący, zapisywany |
| `SystemId` | 64 bity | układ gwiezdny; w EA jeden układ |
| `PlayerId` | 64 bity | nadawany przez serwer przy pierwszym wejściu i powiązany z tożsamością platformy: SteamID64, a bez Steama identyfikatorem deweloperskim |

Planety stoją w miejscu: nie krążą i się nie obracają, a dzień i noc robi słońce obracające się względem planety. Voxel Tools traktuje teren jako statyczny ([changelog](https://voxel-tools.readthedocs.io/en/latest/changelog/)), a nieruchoma planeta upraszcza fizykę, sieć i zapis.

| Parametr planety | Domyślnie | Uwagi |
| --- | --- | --- |
| Promień | 4000 m | float wewnątrz Voxel Tools wystarcza do ok. 10 km |
| Voxel | 1 m |  |
| Grawitacja | 9,81 m/s² | stała w atmosferze, poza nią 0 (doprecyzuje Kosmos) |
| Atmosfera | 1000 m nad promieniem | granica grawitacji i nieba |
| Rdzeń | 400 m pod promieniem | głębiej nie da się kopać |
| Doba | 30 min | obrót słońca względem planety; od podprojektu 2 cykl słońca w układzie |

Współrzędne: świat jest w podwójnej precyzji, w metrach, ze środkiem układu w środku planety domowej; słońce to kierunek światła. Każdy byt ma pozycję lokalną w ramie rodzica: na planecie i w jej pobliżu to układ planety (środek planety, stałe osie), a od podprojektu 2 także układ budowli albo układ gwiezdny. Pozycja w świecie jest wartością pochodną. Przez sieć idą floaty, co przy 4–10 km daje precyzję około milimetra.

Teren:

- Każda planeta to osobny `VoxelLodTerrain` z mesherem Transvoxel i strumieniowaniem Clipbox, które obsługuje wielu widzów naraz.
- Bloki danych mają 16³ voxeli. Liczba LOD obejmuje całą planetę najdalszym poziomem, czyli około 10 przy promieniu 4 km.
- Generator `VoxelGeneratorGraph`: kula z szumem wysokości, jaskinie z szumu 3D, jeden indeks materiału na voxel (kamień, ziemia, piasek, ruda, rdzeń).
- Determinizm: wszystko, co ma kolizje, generuje CPU skalarnym szumem (`ZN_FastNoiseLite`). GPU liczy tylko wizualne detale dalekich LOD-ów, a serwer generuje wyłącznie na CPU.

Grawitacja i postać:

- Grawitacja ciągnie do środka planety, a „góra” to kierunek od środka.
- Postać to kapsuła `CharacterBody3D` z `up_direction` ustawianym co tick fizyki; kamera wyrównuje się płynnie i przełącza między FPP a TPP.
- Stany ruchu: chodzenie, sprint, skok, spadanie oraz swobodny lot w buildzie deweloperskim.

Budowle:

- Budowla to byt z własną siatką bloków 1 m: rama rodzica (planeta), początek siatki w jej układzie, obrót i tryb (`Static`; tryb statku dochodzi w podprojekcie 2). Oś „góra” wskazuje przeciwnie do grawitacji w miejscu pierwszego bloku, a obrót wokół niej odpowiada kierunkowi patrzenia gracza.
- Komórki to `Vector3I` w zasięgu ±2048 na oś, przechowywane w chunkach 16³. Stan bloku to 32 bity: typ (16 bitów), orientacja (jedna z 24), wersja kosztu (4 bity) i stan funkcji (3 bity, np. otwarte drzwi); dwa ostatnie pola są używane od podprojektu 3.
- Blok przy istniejącej budowli ją rozbudowuje, a blok w wolnym miejscu, także na terenie, zakłada nową. Usunięcie ostatniego bloku usuwa budowlę.
- W podprojekcie 1 budowle są statyczne: bez fizyki konstrukcji, bez łączenia i bez dzielenia po usunięciu bloku. Bloki mogą wchodzić w teren, a duże budowle na krzywiźnie potrzebują fundamentów.
- Typy bloków to dane: klucz tekstowy, kształt, materiał i kolizja. Świat zapisuje mapę klucz → numer, więc nowy typ nie psuje zapisów, a brakujący ładuje się jako blok zastępczy.
- Render i kolizje działają per chunk. Sześciany mają wycięte ukryte ściany i łączenie ścian (greedy meshing), inne kształty pochodzą z gotowych siatek. Siatki budują wątki robocze, a kolizją jest `ConcavePolygonShape3D`.

| Narzędzie (bez zasobów) | Działanie | Ograniczenia |
| --- | --- | --- |
| Manipulator terenu | kopanie i zasypywanie kulą o promieniu 0,5–3 m, wybór materiału przy zasypywaniu | zasięg 8 m, do 5 operacji na sekundę, nie poniżej rdzenia |
| Budowanie | stawianie i usuwanie bloków, obrót, paleta typów, podgląd | zasięg 8 m, blok nie może kolidować z graczem |
| Kamera deweloperska | swobodny lot od orbity do powierzchni | tylko build deweloperski |

## Stan, operacje i dziennik

Każda zmiana świata to operacja: serwer zamienia intencję klienta na w pełni określoną operację, stosuje ją transakcyjnie, dopisuje do dziennika i rozsyła skutki.

1. Klient wysyła intencję, np. „kop tu” z promieniem i kierunkiem patrzenia.
2. Serwer waliduje ją czystą funkcją: uprawnienia, zasięg, limit tempa, załadowany obszar i reguły świata (rdzeń, kolizja z graczem). Odrzucenie wraca do klienta z kodem przyczyny.
3. Serwer rozwiązuje intencję do operacji z kompletem parametrów: dokładny środek z raycastu i nadane identyfikatory. Operacja nie wymaga już raycastu ani losowania.
4. Serwer stosuje operację: najpierw sprawdza warunki wstępne, potem zmienia stan. Sama zmiana stanu nie może się nie udać.
5. Operacja dostaje kolejny `OpId` i trafia do kolejki dziennika.
6. Skutki idą do klientów w zasięgu (sekcja Sieć).

| Operacja | Pola | Skutek |
| --- | --- | --- |
| `TerrainEdit` | planeta, kształt (kula), środek, promień, tryb (usuń, dodaj), materiał | zmiana voxeli, podbicie wersji dotkniętych bloków danych |
| `ConstructCreate` | nowy `EntityId` budowli, rama rodzica (planeta), początek, obrót, pierwsza komórka, stan bloku | nowa budowla z jednym blokiem w trybie `Static` |
| `BlockSet` | budowla, komórka, stan bloku | postawienie bloku, wersja budowli +1 |
| `BlockClear` | budowla, komórka | usunięcie bloku, wersja +1; ostatni blok usuwa budowlę, jeśli nic nie ma jej za rodzica |
| `PlayerJoin`, `PlayerLeave` | gracz, tożsamość platformy, rama, `ParentSeq`, pozycja | wejście i wyjście z sesji, zapis ramy i pozycji |
| `EntityReparent` | byt, nowa rama, `ParentSeq`, poza lokalna, fotel | przepięcie bytu do innej ramy; w podprojekcie 1 nic jej nie tworzy, używa jej Kosmos |

Ruch graczy nie trafia do dziennika, bo jest za częsty. Pozycje zapisuje checkpoint i `PlayerLeave`.

Transakcje i determinizm:

- Operacja trafia do dziennika dopiero po zastosowaniu. Wyjątek w trakcie zatrzymuje symulację bez checkpointu, a restart odtwarza stan sprzed błędu.
- Operacje są deterministyczne: bez zegara ściennego, bez losowania przy stosowaniu, bez kolejności z nieuporządkowanych kolekcji. Powtórka dziennika na serwerze daje ten sam stan.
- Każdy blok danych terenu ma 32-bitowy licznik wersji, podbijany przy każdej edycji, która go dotyka. Każda budowla ma własny licznik wersji.

Tick i czas:

- Fizyka działa w 60 Hz na serwerze i kliencie, a serwer wysyła migawki w 20 Hz.
- Klient wysyła wejście co tick fizyki, w paczkach po dwa, z powtórzeniem trzech ostatnich na wypadek utraty pakietów.
- Czas świata (doba) wynika z ticka serwera i jest zapisywany.

## Sieć

Własny netcode w C# z serwerem autorytatywnym: operacje dyskretne idą jako operacje, teren jako wersjonowane dane bloków, a ruch jako migawki z predykcją.

Transport: interfejs `ITransport` ma kanał niezawodny uporządkowany (sterowanie, operacje, dane terenu i budowli) oraz zawodny (migawki i wejście; kolejność pilnuje protokół).

| Implementacja | Do czego | Szczegóły |
| --- | --- | --- |
| `InMemoryTransport` | host i testy | w testach opcjonalne opóźnienie, jitter i utrata pakietów |
| `SteamTransport` | co-op przez Steam | Steam Networking Sockets P2P z przekaźnikami Valve, bez przekierowania portów |
| `UdpTransport` | serwery dedykowane, LAN | LiteNetLib, połączenie po IP i porcie, hasło serwera |

Protokół:

- Binarny, little-endian, z ręcznie pisanymi serializatorami (bez refleksji). Każdy komunikat ma typ i wersję formatu.
- Każdy odczyt sprawdza granice i limity rozmiaru. Uszkodzony pakiet rozłącza nadawcę, nigdy nie wywraca serwera.
- Uścisk dłoni porównuje wersję protokołu i skrót builda gry. Inny build dostaje odmowę z prośbą o aktualizację, bo inny generator dałby inny teren.

Sesja:

1. Połączenie i uścisk dłoni.
2. Serwer wysyła `ServerHello`: `WorldId`, parametry planety, mapę typów bloków, profil generatora i tick serwera.
3. Klient przygotowuje teren lokalnie i zgłasza wersje bloków, które ma w cache.
4. Serwer tworzy postać, wysyła stan budowli i zmienione bloki terenu w zasięgu, a klient przechodzi do gry.
5. Brak danych przez 10 s oznacza rozłączenie; ponowne połączenie to nowa sesja ze stanem z serwera.

Wyjście hosta kończy sesję wszystkim, bez migracji hosta. Trwały świat zapewnia serwer dedykowany.

| Co | Jak | Kanał |
| --- | --- | --- |
| Byty | spawn i despawn jako zdarzenia; migawki 20 Hz z kompresją różnicową względem ostatniej potwierdzonej; jedna migawka na klienta i tick wysyłki w budżecie bajtów, a byty, które się nie mieszczą, idą z wyższym priorytetem w następnej | niezawodny, zawodny |
| Ruch własnej postaci | klient od razu symuluje swoje wejście; serwer odsyła stan i numer ostatniego przetworzonego wejścia; klient wraca do stanu serwera i powtarza niepotwierdzone wejścia | zawodny |
| Ruch innych graczy | interpolacja z buforem 100 ms | zawodny |
| Budowle | pełny stan przy wejściu w zasięg, potem `BlockSet` i `BlockClear` z numerem wersji; przy luce w wersjach klient wysyła `ResyncRequest` (21) i dostaje pełny stan budowli | niezawodny |
| Teren niezmieniony | klient generuje go sam z ziarna | brak |
| Teren zmieniony | serwer wysyła skompresowane dane bloków 16³ z wersją, klient trzyma je w cache | niezawodny |

Edycje terenu:

- Własną edycję klient stosuje lokalnie od razu, a autorytatywne dane bloków nadpisują ją po chwili.
- Cudze edycje przychodzą jako dane bloków. Załadowany blok klient podmienia (`paste`), a niezaładowany czeka w cache, skąd przy wczytaniu podaje go nasz strumień Voxel Tools.
- Kolejka wysyłki terenu ma priorytet według odległości od gracza i limit pasma.
- Kontrola determinizmu: klient co jakiś czas wysyła sumy kontrolne (xxHash64) bloków wygenerowanych u siebie wokół gracza. Przy różnicy serwer wysyła swój blok i loguje przypadek, co chroni przed różnicami między platformami (np. klient Windows, serwer Linux).

Zainteresowanie i limity:

- Klient dostaje byty tylko z tej samej planety, w promieniu 500 m; zasięg terenu wynika z widza Voxel Tools.
- Serwer ogranicza tempo intencji gracza, odrzuca intencje w niezaładowanych obszarach, sprawdza zasięg i linię wzroku.
- Serwer przetwarza jedno wejście gracza na tick fizyki z krótkiego bufora i odrzuca nadmiar, co blokuje przyspieszanie postaci.
- Domyślnie do 8 graczy na serwer (ustawialne).
- Cel pasma: do 50 KB/s na klienta w zwykłej grze; większe porcje terenu idą przez kolejkę.

Steam w podprojekcie 1:

- Host tworzy lobby (prywatne, tylko znajomi albo publiczne); znajomi dołączają przez zaproszenie w nakładce Steam albo z listy znajomych.
- Do czasu własnego App ID używamy testowego App ID 480.
- Tożsamość graczy w P2P potwierdza Steam; bilet Steama połączeń UDP, u hosta i na serwerze dedykowanym, sprawdza od podprojektu Wydanie pakiet K9-58.

## Zapis i odtwarzanie

Każdy świat to jedna baza SQLite: okresowy spójny checkpoint plus dziennik operacji od niego, więc awaria kosztuje najwyżej ostatnią partię dziennika (do 100 ms, a w trakcie transakcji checkpointu do jej końca) i nie tworzy duplikatów.

Pliki:

- Serwer: `saves/<WorldId>/world.db` (SQLite w trybie WAL) i katalog `saves/<WorldId>/backups/`.
- Klient: `cache/<WorldId>/terrain.db`, czyli cache bloków terenu z wersjami. Nie jest autorytatywny i można go skasować w każdej chwili.

| Tabela | Zawartość |
| --- | --- |
| `meta` | wersja schematu, `WorldId`, ziarno galaktyki, wersja gry, data utworzenia |
| `block_types`, `materials` | mapa klucz tekstowy → numer |
| `planets` | parametry planet |
| `terrain_blocks` | planeta, LOD, pozycja bloku, wersja, dane |
| `entities` | byty: typ i dane z bajtem wersji formatu |
| `construct_chunks` | budowla, pozycja chunku, dane |
| `players` | `PlayerId`, tożsamość platformy, byt, rama i ostatnia pozycja |
| `journal` | `OpId`, tick, typ, dane, suma kontrolna |
| `checkpoints` | `OpId` granicy, tick, czas |

Zapis bieżący: jeden wątek zapisu z kolejką obsługuje dziennik i checkpointy, więc kolejność zapisu odpowiada kolejności operacji. Dziennik zapisuje się partiami najpóźniej co 100 ms (WAL, `synchronous=FULL`).

Checkpoint (domyślnie co 60 s, przy zatrzymaniu serwera i na komendę `save`):

1. Na granicy ticku serwer zapamiętuje `OpId` granicy.
2. Dane zmienionych bloków terenu kopiuje z Voxel Tools do pamięci (to tylko kopiowanie), a stan bytów i chunków budowli zamraża jako niezmienne migawki.
3. Kompresja i zapis idą w tle, w jednej transakcji: bloki, byty, chunki, wiersz checkpointu i usunięcie dziennika do granicy.
4. Bloki, które Voxel Tools wyładował przed checkpointem, trafiają do bufora przez nasz strumień i zapisują się w tej samej transakcji.

Zrzut na granicy ticku gwarantuje, że powtórka dziennika po checkpoincie nie zastosuje żadnej edycji dwa razy.

Odtwarzanie przy starcie:

1. Kontrola integralności bazy (`PRAGMA quick_check`); błąd kończy się propozycją przywrócenia kopii.
2. Migracje schematu, jeśli zapis jest starszy, zawsze po wykonaniu kopii.
3. Wczytanie meta, map typów, planet, bytów i graczy. Teren wczytuje się leniwie przez nasz strumień: zmienione bloki z bazy, reszta z generatora.
4. Powtórka operacji z `OpId` większym niż granica ostatniego checkpointu; przed każdą edycją terenu serwer ładuje jej obszar.
5. Natychmiastowy nowy checkpoint.

Kopie i migracje:

- Kopia przez Online Backup API SQLite przy starcie serwera i co 6 h; trzymamy 10 ostatnich.
- Migracje schematu żyją w kodzie, są numerowane i każda działa w transakcji.
- Każdy zapisany blob ma bajt wersji formatu, a czytniki obsługują stare wersje albo migrują je przy wczytaniu.

## Obsługa błędów

Zasada: żadne dane od klienta nie wywracają serwera, a żaden błąd serwera nie zapisuje niespójnego stanu.

| Sytuacja | Reakcja |
| --- | --- |
| Inna wersja gry lub protokołu | odmowa połączenia z prośbą o aktualizację |
| Uszkodzony lub za duży pakiet | rozłączenie nadawcy, wpis w logu |
| Odrzucona intencja | kod przyczyny do klienta; lokalnie przewidziana edycja się cofa |
| Luka w wersjach budowli | pełna resynchronizacja: `ResyncRequest` (21) z identyfikatorem bytu od klienta; serwer odsyła budowlę jako `ConstructState` od części 0, a inny byt jako `EntitySpawn` |
| Niezgodna suma kontrolna terenu | serwer wysyła swój blok; zdarzenie w logu i metrykach |
| Duży błąd predykcji ruchu | natychmiastowe przestawienie postaci; mały błąd jest wygładzany |
| Utrata połączenia | rozłączenie po 10 s; ponowne połączenie jako nowa sesja |
| Wyjście hosta | koniec sesji u wszystkich z komunikatem |
| Błąd zapisu dziennika lub pełny dysk | tryb bezpieczny: serwer przestaje przyjmować zmiany i ostrzega graczy oraz administratora |
| Uszkodzona baza przy starcie | świat się nie uruchamia; propozycja przywrócenia ostatniej kopii |
| Operacja z dziennika nie przechodzi przy powtórce | zatrzymanie z numerem operacji, nigdy ciche pominięcie |
| Wyjątek w ticku symulacji | zatrzymanie symulacji bez checkpointu; restart odtwarza stan z checkpointu i dziennika |
| Awaria przed zapisem partii dziennika | znikają ostatnie zmiany z okna do 100 ms, które gracze już widzieli; akceptujemy to świadomie |

Logi: serwer i klient piszą strukturalne logi do pliku z rotacją, z identyfikatorami sesji, gracza i operacji. Przy awarii zapisują stos i stan ostatniego ticku.

## Testy

Logikę stanu, zapisu i sieci testujemy bez silnika, a pełna gra przechodzi w CI test z botami na serwerze dedykowanym.

| Poziom | Co sprawdza | Narzędzia |
| --- | --- | --- |
| Jednostkowe | walidacja i stosowanie operacji, nadawanie identyfikatorów, serializatory w obie strony, protokół | xUnit, testy właściwości (FsCheck) |
| Odporność parsera | losowe i uszkodzone pakiety nie wywracają serwera | fuzzing deserializatorów |
| Determinizm | stan po operacjach równa się stanowi po checkpoincie i powtórce dziennika (skróty stanu) | xUnit |
| Odporność zapisu | przerwanie zapisu w losowych miejscach, restart; stan równa się ostatniemu trwałemu, bez duplikatów | wstrzykiwanie awarii w Persistence |
| Migracje | stare bazy z repozytorium otwierają się i migrują | bazy testowe |
| Sieć | predykcja i korekta, zainteresowanie, resynchronizacja budowli, wersje terenu przy opóźnieniu, jitterze i utracie pakietów | `InMemoryTransport` z symulacją łącza |
| Integracyjne | serwer i 2 boty kopią i budują przez 10 min; teren i budowle identyczne u wszystkich | Godot bez grafiki (gdUnit4Net) |
| Wydajność | generacja i meshing, czas ticku serwera przy 8 graczach | BenchmarkDotNet, profiler Godota |
| Wytrzymałościowe | serwer dedykowany i 8 botów przez 2 h, co noc | CI |

## Kryteria ukończenia

Podprojekt 1 jest skończony, gdy wszystkie punkty poniżej są spełnione na buildzie z CI.

- [ ] Jedna komenda buduje silnik, SDK i szablony eksportu, a CI jest zielone.
- [ ] Działają gra solo, host, klient i serwer dedykowany (Windows i Linux).
- [ ] Kamera przechodzi płynnie od orbity do powierzchni planety o promieniu 4 km, bez dziur w terenie i bez przycięć dłuższych niż 50 ms przy strumieniowaniu.
- [ ] 8 graczy gra na jednej planecie przez zaproszenie Steam oraz przez serwer dedykowany po IP.
- [ ] Edycje terenu i bloków są widoczne u wszystkich w najwyżej 200 ms przy RTT 100 ms.
- [ ] Po godzinie testu z botami stan terenu i budowli jest identyczny u wszystkich (skróty stanu).
- [ ] Restart serwera zachowuje wszystkie zmiany, a zabicie procesu traci najwyżej ostatnią partię dziennika (do 100 ms, a w trakcie transakcji checkpointu do jej końca), bez duplikatów (test automatyczny).
- [ ] Na sprzęcie referencyjnym (6 rdzeni, GTX 1660 Super, 16 GB RAM) gra ma 60 FPS w 1080p, a tick serwera trwa średnio najwyżej 8 ms przy 8 graczach.
- [ ] Testy jednostkowe, integracyjne i odporności zapisu przechodzą w CI.

## Ryzyka i otwarte kwestie

Największe ryzyko to teren w sieci: Voxel Tools nie synchronizuje terenu z LOD, więc tę warstwę piszemy sami i sprawdzamy ją na samym początku.

| Ryzyko | Wpływ | Działanie |
| --- | --- | --- |
| Brak synchronizacji sieciowej dla `VoxelLodTerrain` ([dokumentacja](https://voxel-tools.readthedocs.io/en/latest/multiplayer/)) | cała sieć terenu jest po naszej stronie | projekt w sekcji Sieć; pierwszy test z dwoma klientami jako pierwszy test akceptacyjny K3-16 |
| Moduł w kompilacji z podwójną precyzją i .NET ([zgłoszenie #639](https://github.com/Zylann/godot_voxel/issues/639), [C# w Voxel Tools](https://voxel-tools.readthedocs.io/en/latest/getting_the_module/)) | problemy z budowaniem lub precyzją | pipeline silnika jako pierwsze zadanie; planety do ok. 10 km promienia |
| Widoczność dużych edycji z daleka (przenoszenie zmian do niższych LOD) | wykopy niewidoczne z orbity | sprawdzić zachowanie Voxel Tools na starcie; w razie potrzeby własne przeliczanie LOD |
| Różnice generacji między platformami | rozjazdy kolizji u klientów | sumy kontrolne bloków i dosyłanie danych z serwera; mierzyć częstość |
| Voxel Tools prowadzi głównie jedna osoba | wolniejsze poprawki i aktualizacje | licencja MIT i pełny kod; w razie potrzeby własny fork |
| Wydajność meshingu budowli w C# | przycięcia przy dużych budowlach | wątki robocze, bufory z puli, profilowanie; w ostateczności kod natywny |
| Brak własnego App ID na Steamie | blokada testów zewnętrznych | App ID 480 w rozwoju; własne przed testami zewnętrznymi |

Otwarte kwestie (nie blokują startu):

- [x] Domyślna kamera: widok zza ramienia, pierwsza osoba zawsze dostępna (specyfikacja 4).
- [x] Maksymalna liczba graczy: 8 (specyfikacje 2–4 i 6).
- [ ] Długość doby i promień planety po pierwszych testach.

## Następne kroki

Po akceptacji tej specyfikacji powstaje plan implementacji w tej kolejności; każdy krok kończy się działającym, przetestowanym kodem.

1. Pipeline silnika: Godot z Voxel Tools, podwójna precyzja, SDK C#, CI.
2. Szkielet rozwiązania: projekty Core, Persistence, Net i Game, testy, styl kodu.
3. Core: identyfikatory, model stanu, operacje, walidacja, serializacja, protokół.
4. Persistence: schemat, dziennik, checkpointy, odtwarzanie, kopie, migracje.
5. Net: transport w pamięci, UDP i Steam; sesja i uścisk dłoni.
6. Planeta: generator, `VoxelLodTerrain`, nasz strumień, kamera deweloperska.
7. Postać: ruch po kuli, grawitacja, kamera, predykcja i korekta.
8. Narzędzia i budowle: manipulator terenu, siatki bloków, meshing, kolizje.
9. Replikacja: byty, budowle, teren z wersjami i cache, sumy kontrolne.
10. Steam i serwer: lobby i zaproszenia, serwer dedykowany z konsolą.
11. Testy integracyjne z botami i sprawdzenie kryteriów ukończenia.

Źródła:

- [Voxel Tools: changelog](https://voxel-tools.readthedocs.io/en/latest/changelog/)
- [Voxel Tools: multiplayer](https://voxel-tools.readthedocs.io/en/latest/multiplayer/)
- [Voxel Tools: instalacja i C#](https://voxel-tools.readthedocs.io/en/latest/getting_the_module/)
- [godot\_voxel: zgłoszenie #639](https://github.com/Zylann/godot_voxel/issues/639)
