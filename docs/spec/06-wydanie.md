# Specyfikacja podprojektu 6: Wydanie

> Kopia dokumentu z Claude Docs, stan na 2026-10-08. Wersja żywa: https://claude.ai/code/artifact/d67f2619-0a4e-467e-98e6-b4ab015c70e4

## Kontekst i decyzje

Podprojekt 6 wydaje grę w Early Access na Steamie: integracja ze Steamem, serwer dedykowany do pobrania, optymalizacja, ustawienia i dostępność, raporty awarii, audyt bezpieczeństwa i strona sklepu. Na start gra wychodzi tylko na PC. Decyzje pochodzą z raportu „Otwarte decyzje projektowe Horyzontu”, a plan nie ma dat.

| Decyzja | Rozstrzygnięcie | Podstawa |
| --- | --- | --- |
| Platformy | Steam: Windows x86\_64 i natywny Linux x86\_64; Steam Deck z oceną Playable przy starcie EA, Verified jako cel wersji 1.0 | Windows to 95,03% Steama, Linux 3,05%, macOS 1,92% ([GamingOnLinux](https://www.gamingonlinux.com/2026/10/steam-survey-for-september-2026-shows-a-drop-in-linux-with-simplified-chinese-rising/)); Verified na Decku daje Verified na Steam Machine ([Steamworks](https://partner.steamgames.com/doc/steamdeck/compat)) |
| macOS i konsole | nie na start; osobna decyzja po EA | macOS wymaga certyfikatu Apple i notaryzacji ([Godot](https://raw.githubusercontent.com/godotengine/godot-docs/master/tutorials/export/exporting_for_macos.rst)); W4 obsługuje C# na konsolach tylko w becie ([W4 Games](https://www.w4games.com/w4consoles)) |
| Cena | 19,99 USD, 19,99 EUR, 69,99–74,99 zł; podwyżka do 29,99 USD przy 1.0 zapowiedziana od pierwszego dnia | Valheim podniósł cenę z 19,99 do 29,99 USD ([dlcompare](https://www.dlcompare.com/gaming-news/valheim-10-is-coming-with-a-bigger-price-tag)); gry z lat 2025–2026 liczą 3,33–3,75 zł za dolara ([Spider's Web](https://spidersweb.pl/2026/03/steam-wyceny-polska.html)) |
| Rabat premierowy | 10–15% przez 7–14 dni | rabat premierowy to najwyżej 40%, zwykle 10–15%; po podwyżce 30 dni bez rabatów ([Steamworks](https://partner.steamgames.com/doc/marketing/discounts)) |
| Serwer dedykowany | osobna aplikacja typu TOOL w anonimowym SteamCMD, Windows i Linux, z tej samej kompilacji podwójnej precyzji | [Steamworks](https://partner.steamgames.com/doc/sdk/uploading/distributing_gs) |
| Tryb hosta | Steam Networking Sockets przez sieć przekaźników, która ukrywa adres IP | [Steamworks](https://partner.steamgames.com/doc/features/multiplayer/steamdatagramrelay) |
| Crossplay i anti-cheat | nie w EA; transport i tożsamość gracza są za abstrakcjami od K1 | gra PvE z autorytatywnym serwerem i prywatnymi sesjami; Easy Anti-Cheat działa też na Linuksie, gdyby kiedyś był potrzebny ([Wikipedia](https://en.wikipedia.org/wiki/Easy_Anti-Cheat)) |
| Plan rozwoju | lista tematów ze słowem „planowane”, bez dat | Valve zabrania konkretnych obietnic co do przyszłości gry w EA ([Steamworks](https://partner.steamgames.com/doc/store/earlyaccess)) |
| Raporty awarii | Sentry: natywne zrzuty i błędy C# na Windows i Linuksie | dodatek Sentry dla Godota ([Sentry](https://docs.sentry.io/platforms/godot)) |

Zależności: potok silnika (K1-01), tryby gry i serwer z konsolą (K2-10), lobby Steam (K3-17), menu (K3-18), mapa (K5-27), przedmioty, rzemiosło, zagrożenia, śmierć i budowanie (K6-29, K6-32, K6-34, K6-35, K6-36), kamera i broń dystansowa (K7-43, K7-44), treść i zasoby (K8-50…K8-56) oraz bramki podprojektów 2–5 (K5-28, K6-40, K7-49, K8-57).

## Cel i zakres

Na bramce K9 gra jest gotowa do startu EA: przechodzi przegląd Steama na Windows i Linuksie, ma serwer dedykowany do pobrania, mieści się w budżetach wydajności, zbiera raporty awarii i ma stronę sklepu z FAQ. Sam start EA następuje dopiero po spełnieniu warunków z sekcji Kryteria.

W zakresie:

- Steam: osiągnięcia, statystyki, Steam Cloud dla ustawień, rich presence, lista serwerów, bilety uwierzytelniające, gałęzie wydań;
- serwer dedykowany: aplikacja TOOL w SteamCMD, Windows i Linux, obraz Docker, plik konfiguracji, komendy konsoli, administratorzy, dokumentacja;
- optymalizacja: profilowanie, sceny wzorcowe, budżety klatki, pamięci i ticku, LOD, czas wczytywania, zestaw ustawień dla Decka;
- ustawienia, sterowanie z SteamInput, dostępność i lokalizacja PL oraz EN;
- raporty awarii i telemetria za zgodą, zgodnie z RODO;
- audyt bezpieczeństwa serwera i odporności na oszustwa;
- sklep: strona, zwiastun, zrzuty, FAQ EA, plan rozwoju, ceny regionalne, Steam Playtest, demo albo otwarte testy, ankieta treści;
- testy wydania na wszystkich platformach i w trybach gry.

Poza zakresem EA: macOS, konsole, crossplay, inne sklepy, anti-cheat, publiczne serwery z PvP, mody i Steam Workshop. Transport, tożsamość platformy i usługi platformy są za interfejsami, więc kolejna platforma to nowa implementacja, a nie przepisanie sesji.

## Funkcje dla gracza

### Steam

- Osiągnięcia są danymi w `content/achievements.json`: pierwsze kroki, statek i lądowania, każdy boss, budowanie i kooperacja. Mechanizm osiągnięć, statystyk i obecności buduje K9-58, a do zdarzeń gry podłącza je K8-55, gdy treść jest gotowa.
- Statystyki Steama liczą przelecone kilometry, postawione bloki i pokonanych bossów.
- Steam Cloud przechowuje ustawienia i przypisania klawiszy. Światy zostają lokalnie, z kopiami z K1-05.
- Rich presence pokazuje planetę albo lot, liczbę graczy i walkę z bossem, a znajomi dołączają do hosta z listy znajomych.
- Przeglądarka serwerów w grze pokazuje zarejestrowane serwery dedykowane: nazwę, graczy, hasło i ping.
- Gałęzie wydań: domyślna, publiczna `beta` do testów i prywatna `dev`.

### Ustawienia i sterowanie

- Grafika: zestawy Deck, Niskie, Średnie, Wysokie i Ultra; rozdzielczość, skalowanie FSR, zasięg terenu i LOD, cienie, mgła, limit klatek, synchronizacja pionowa.
- Dźwięk: głośność ogólna, muzyki, efektów, otoczenia i interfejsu.
- Sterowanie: przypisanie każdej akcji na klawiaturze i myszy, pad przez SteamInput z właściwymi ikonami i klawiaturą ekranową, czułość i odwrócenie osi.
- Kamera: zza ramienia albo pierwsza osoba, pierwsza osoba w ciasnych miejscach, pole widzenia, kołysanie.
- Interfejs: skala, rozmiar i tło napisów, język.

### Dostępność

- Każde przytrzymanie ma wariant przełączany: bieg, celowanie, blok i naciąg.
- Zapowiedzi ataków mają zawsze sygnał obrazu i dźwięku, więc nie wymagają słuchu.
- Palety dla daltonistów obejmują znaczniki, zagrożenia i podgląd podparcia budowli.
- Kołysanie kamery, błyski i drgania ekranu można wyłączyć.
- Menu i ustawienia obsługują czytniki ekranu przez AccessKit z Godota, który K9-61 włącza w potoku silnika.

### Lokalizacja

- Polski i angielski od startu EA; cała gra korzysta z kluczy tekstów.
- Pseudolokalizacja w CI wykrywa brakujące klucze i teksty, które nie mieszczą się w interfejsie.

### Serwer dedykowany

- Instalacja anonimowo przez SteamCMD albo z obrazu Docker. Jeden plik `server.json` ma nazwę, hasło, port, liczbę miejsc (do 8), świat, ustawienia świata, administratorów i rejestrację na liście serwerów.
- Komendy konsoli: `help`, `players`, `kick`, `ban`, `unban`, `save`, `backup`, `settings`, `give`, `tp`, `tombstone`, `progress`, `notice`, `stop`.
- Administrator wydaje te same komendy z konsoli w grze (komunikat `AdminCommand`).
- Dokumentacja hostowania jest po polsku i angielsku.

### Prywatność

- Przy pierwszym uruchomieniu gra pyta o zgodę na raporty awarii i na telemetrię, osobno. Domyślnie obie są wyłączone, a zgodę można zmienić w ustawieniach.
- Telemetria używa losowego identyfikatora instalacji zamiast SteamID i zbiera tylko zdarzenia postępu potrzebne do balansu.

## Architektura i model danych

Usługi platformy są za interfejsem w grze, a serwer dedykowany to ten sam program co w K2-10, tylko spakowany i opisany dla graczy.

### Warstwa platformy

- `IPlatformServices` w grze obsługuje osiągnięcia, statystyki, chmurę, obecność, zaproszenia i bilety. `SteamPlatform` używa tej samej biblioteki Steamworks co transport z K1-06, a `NullPlatform` służy testom i serwerowi bez Steama.
- Osiągnięcia należą do konta gracza, więc serwer wysyła `AchievementUnlocked`, a klient zapisuje je przez API Steama.
- Na serwerze dedykowanym `SteamGameServer` rejestruje serwer na liście i sprawdza bilet sesji z `ClientHello` przez `BeginAuthSession`. Wynikiem jest zweryfikowana `PlatformIdentity` (Steam, SteamID) z K1.
- `BeginAuthSession` odpowiada asynchronicznie, więc sesja czeka na wynik w stanie `AwaitingAuth`, który K9-58 dokłada do maszyny stanów z K1-07, najwyżej 10 s. Odmowa, SteamID inny niż w `Identity` albo przekroczony czas kończą się `HandshakeReject` z kodem `IdentityMismatch`.
- W trybie hosta połączenie przez Steam Networking Sockets potwierdza tożsamość samo, a połączenie UDP host sprawdza biletem przez `ISteamUser::BeginAuthSession` w tym samym stanie `AwaitingAuth`.
- Opcja `RequireSteamAuth` w `server.json` jest domyślnie włączona. Bez działającego Steama serwer wtedy nie startuje; administrator może ją wyłączyć w sieci lokalnej.

### Serwer dedykowany

- Serwer dedykowany z K2-10, czyli eksport gry uruchamiany z `--headless` w trybie `--server`, jest pakowany jako aplikacja TOOL dla Windows i Linuksa oraz jako obraz Docker dla Linuksa z woluminem na zapisy.
- `server.json` jest walidowany przy starcie, a błąd konfiguracji kończy start czytelnym komunikatem.
- Lista zbanowanych tożsamości leży w `bans.json` obok konfiguracji, a nie w świecie.

### Optymalizacja

Sceny wzorcowe to stałe przeloty kamery nad każdą planetą, baza z 10 tys. bloków i walka z 64 stworami. Skrypt pomiaru zapisuje czasy klatki i ticku na sprzęcie referencyjnym.

| Cel | Budżet |
| --- | --- |
| PC referencyjny: Ryzen 5 3600, GeForce GTX 1660 Super, 16 GB RAM | 60 kl./s w 1080p na zestawie Średnie |
| Steam Deck | 30 kl./s w 800p na zestawie Deck |
| Serwer z 8 graczami | tick średnio do 8 ms |
| Wczytanie świata z dysku SSD | do 30 s na PC referencyjnym |

- Klienta profiluje profiler Godota, a serwer liczniki ticku i narzędzia `dotnet-counters` oraz `dotnet-trace`.
- Impostory planet i LOD z podprojektu 2 są też budżetem wydajności na Decku.

### Ustawienia i lokalizacja

- Ustawienia są w `user://settings.json`, synchronizowanym przez Steam Cloud. Przypisania klawiszy pochodzą z mapy wejść Godota.
- Tłumaczenia są w `game/locale/pl.po` i `en.po`. Skrypt zbiera klucze z kodu i z danych treści do pliku POT.

### Awarie i telemetria

- Dodatek Sentry ma część natywną, więc K9-62 buduje go ze źródeł z `precision=double` w potoku silnika z K1-01. Test dymny sprawdza, że dodatek się ładuje.
- Raport awarii ma wersję kompilacji, `BuildHash`, platformę i ostatnie wpisy logu, bez ścieżek z nazwą użytkownika.
- `TelemetryClient` wysyła paczki zdarzeń po HTTPS do lekkiego punktu zbierania, np. Cloudflare Worker z bazą D1, i tylko po zgodzie gracza.

## Operacje i dziennik

Podprojekt nie dodaje operacji dziennika. Komendy administratora tworzą operacje z wcześniejszych podprojektów, więc przechodzą tę samą walidację i trafiają do dziennika jak każda zmiana.

| Komenda | Operacje |
| --- | --- |
| `give` | `ItemCreateOp` ze źródłem `Admin` |
| `tp` | `EntityReparentOp` gracza |
| `tombstone` | `EntityReparentOp` nagrobka do łóżka właściciela |
| `progress` | `ProgressKeyGrantOp` |
| `settings` | `WorldSettingsSetOp` |
| `kick`, `ban`, `unban`, `notice`, `save`, `backup`, `stop` | bez operacji: sesja, plik `bans.json`, checkpoint, kopia zapasowa, zamknięcie |

## Sieć

Protokół gry się nie zmienia; dochodzą cztery komunikaty usługowe i uwierzytelnianie biletem Steama w istniejącym polu `ClientHello`.

| Typ | Komunikat | Kierunek i kanał | Zawartość |
| --- | --- | --- | --- |
| 70 | `AchievementUnlocked` | S→K, niezawodny | identyfikator osiągnięcia |
| 71 | `ServerNotice` | S→K, niezawodny | komunikat administratora albo zapowiedź restartu |
| 72 | `AdminCommand` | K→S, niezawodny | tekst komendy |
| 73 | `AdminCommandResult` | S→K, niezawodny | status (`Ok`, `NotAdmin`, `BadCommand` albo `Rejected` z kodem odmowy operacji) i tekst wyniku; to pole komunikatu, a nie nowy kod `RejectReason` |

- Klient wkłada bilet sesji Steama do `AuthTicket` w `ClientHello`; pole istnieje od K1-03. Zły albo wygasły bilet kończy się `HandshakeReject` z kodem `IdentityMismatch`.
- Serwer rejestruje się na liście przez `SteamGameServer` z portem gry i portem zapytań, a przeglądarka w grze pyta o listę przez API Steama.
- Serwer dedykowany wymaga otwartego portu UDP. Tryb hosta przechodzi przez sieć przekaźników Steama bez przekierowania portów.

## Zapis

Podprojekt nie zmienia schematu świata. Dokłada pliki obok serwera i w profilu gracza.

| Plik | Miejsce | Zawartość |
| --- | --- | --- |
| `server.json` | katalog serwera | konfiguracja serwera |
| `bans.json` | katalog serwera | zbanowane tożsamości z powodem |
| `user://settings.json` | profil gracza, Steam Cloud | ustawienia i przypisania klawiszy |
| `user://consent.json` | profil gracza | zgody na raporty i telemetrię oraz identyfikator instalacji |

- Kopie zapasowe i migracje z K1-05 działają bez zmian. Testy wydania sprawdzają, że świat z każdej wcześniejszej wersji EA się wczytuje.
- Uszkodzony plik ustawień dostaje kopię z rozszerzeniem `.broken`, a gra wraca do wartości domyślnych.

## Obsługa błędów

Każdy błąd wydania kończy się czytelnym komunikatem dla gracza albo administratora, a nigdy cichą awarią.

| Sytuacja | Reakcja |
| --- | --- |
| Klient uruchomiony poza Steamem | ponowne uruchomienie przez Steama (`SteamAPI_RestartAppIfNecessary`) |
| Brak sieci | gra solo działa w trybie offline Steama; hostowanie i dołączanie pokazują komunikat |
| Zły albo wygasły bilet sesji | `HandshakeReject` z kodem `IdentityMismatch` |
| Steam niedostępny na serwerze z `RequireSteamAuth` | serwer nie startuje i wypisuje powód |
| Błąd w `server.json` | start przerwany z numerem linii i opisem pola |
| Klient i serwer z różnych kompilacji | `HandshakeReject` z kodem `WrongBuild` (K1-07) i komunikat o aktualizacji |
| Brak tłumaczenia | tekst angielski; CI nie dopuszcza braków w wydaniu |
| Awaria klienta bez zgody na raporty | zrzut zostaje tylko lokalnie w `user://crashes/` |
| Dodatek natywny się nie ładuje | gra działa bez niego z wpisem w logu; test dymny łapie to przed wydaniem |

## Testy

Testy wydania sprawdzają grę tak, jak dostanie ją gracz: z czystej instalacji, na każdej platformie i z zapisami z poprzednich wersji.

| Test | Kryterium zaliczenia |
| --- | --- |
| Czyste instalacje | Windows 10 i 11, Ubuntu LTS i SteamOS: pierwsze uruchomienie, nowy świat solo, hostowanie i dołączanie |
| Steam Deck | lista Playable: pad i ikony, klawiatura ekranowa, czytelność tekstu, zestaw Deck z 30 kl./s w 800p |
| Proton | wersja na Windows pod Protonem na Decku działa jako punkt odniesienia |
| Serwer dedykowany | instalacja przez SteamCMD na Windows i Linuksie oraz z obrazu Docker; 8 graczy, restart, kopia i każda komenda konsoli |
| Aktualizacja zapisów | świat z każdej wcześniejszej wersji EA z zestawu baz testowych wczytuje się i działa |
| Wydajność | sceny wzorcowe w budżetach na PC referencyjnym i na Decku |
| Długi test | 8 botów przez 6 h na serwerze dedykowanym: pamięć rośnie o mniej niż 5%, skrót stanu bez rozjazdów |
| Bezpieczeństwo | fuzzing wszystkich kodeków w CI; zalew uścisków dłoni; blokada zgadywania hasła po 5 próbach z jednego adresu na 60 s |
| Lokalizacja | brak brakujących kluczy; polskie znaki; pseudolokalizacja z tekstem dłuższym o 30% bez przycięć |
| Prywatność | przechwycony ruch pokazuje, że bez zgody gra nie wysyła raportów ani telemetrii; wycofanie zgody zatrzymuje wysyłkę |
| Steam | osiągnięcia, statystyki, rich presence, zaproszenia i lista serwerów działają na koncie testowym |

## Kryteria ukończenia

- [ ] Kompilacje na Windows i Linuksa przechodzą przegląd Steama, a Deck spełnia listę Playable.
- [ ] Serwer dedykowany instaluje się anonimowo przez SteamCMD na Windows i Linuksie i działa z 8 graczami.
- [ ] Sceny wzorcowe mieszczą się w budżetach: 60 kl./s w 1080p na PC referencyjnym, 30 kl./s w 800p na Decku, tick średnio do 8 ms.
- [ ] Audyt bezpieczeństwa nie ma otwartych ustaleń krytycznych ani wysokich.
- [ ] Raporty awarii działają, a telemetria wysyła dane tylko po zgodzie.
- [ ] Strona sklepu, FAQ EA z zapowiedzią podwyżki i plan rozwoju bez dat są opublikowane.
- [ ] Warunki startu EA są spełnione: ok. 85% pozytywnych ocen w testach, mediana do bossa 4 co najmniej 15 h, stabilna sieć dla 8 graczy, ponad 7000 osób z grą na liście życzeń oraz wcześniejsze demo albo otwarte testy.

## Pakiety pracy

Wydanie to 8 pakietów kamienia milowego K9: Opus 5.5 prowadzi 1, GPT-6.1 Sol 1, Sonnet 5.5 2, a Gemini 3.8 Flash 4, w tym jeden razem z Dismondem. K9-62 i K9-64 zależą tylko od wcześniejszych pakietów, więc raporty awarii, strona sklepu i testy w Steam Playtest ruszają przed końcem treści.

| Pakiet | Zakres | Prowadzi | Zależy od | Odbiór |
| --- | --- | --- | --- | --- |
| K9-58 | Integracja Steam: `IPlatformServices`, mechanizm osiągnięć i statystyk, Steam Cloud dla ustawień, rich presence, rejestracja i lista serwerów, bilety sesji ze stanem `AwaitingAuth` w K1-07 i `PlatformIdentity` ze Steama, gałęzie wydań, komunikat 70 | Sonnet 5.5 | K2-10, K3-17 | osiągnięcie i statystyka testowa, obecność, lista serwerów i uwierzytelnianie działają na koncie testowym |
| K9-59 | Serwer dedykowany do pobrania: aplikacja TOOL w SteamCMD, `server.json`, `bans.json`, komendy konsoli i administratorzy (`give`, `settings`, `progress` i `tombstone` na operacjach z K6-29, K6-32 i K6-35), komunikaty 71–73 z `AdminCommand` w stanie `InGame` w K1-07, obraz Docker, dokumentacja PL i EN | Gemini 3.8 Flash | K9-58, K6-29, K6-32, K6-35 | test serwera dedykowanego z sekcji Testy |
| K9-60 | Optymalizacja: sceny wzorcowe, skrypt pomiaru, profilowanie klienta i serwera, LOD, pamięć, czas wczytywania, zestaw Deck, D3D12, LTO i szablon debug dla Linuksa w potoku silnika | Opus 5.5 | K8-51, K8-52, K8-53, K8-56 | sceny wzorcowe w budżetach na PC referencyjnym i na Decku |
| K9-61 | Ustawienia, sterowanie, dostępność i lokalizacja: menu ustawień, przypisania klawiszy, SteamInput z ikonami i klawiaturą ekranową, opcje dostępności, AccessKit dla czytników ekranu, pliki PO, pseudolokalizacja w CI | Gemini 3.8 Flash | K3-18, K5-27, K6-34, K6-36, K7-43, K7-44, K8-55 | testy lokalizacji; każda opcja dostępności działa |
| K9-62 | Raporty awarii i telemetria: dodatek Sentry w podwójnej precyzji w potoku silnika, zgody, `TelemetryClient`, punkt zbierania zdarzeń, zdarzenia postępu dla K8-57, polityka prywatności | Gemini 3.8 Flash | K1-01, K3-18 | test prywatności; awaria testowa daje raport z symbolami |
| K9-63 | Audyt bezpieczeństwa: model zagrożeń, fuzzing kodeków z podprojektów 2–6, limity tempa i rozmiarów, odporność uścisku dłoni, blokada zgadywania hasła, próby oszustw, poprawki | GPT-6.1 Sol | K5-28, K6-40, K7-49, K8-55, K9-59 | brak otwartych ustaleń krytycznych i wysokich |
| K9-64 | Wydanie na Steamie: strona sklepu, zwiastun i zrzuty, FAQ EA, plan rozwoju bez dat, ceny regionalne, rabat premierowy, Steam Playtest, demo albo otwarte testy, ankieta treści z udziałem AI, wymagania sprzętowe | Dismond z Gemini 3.8 Flash | K8-50, K9-58 | strona sklepu i Playtest opublikowane; ceny sprawdzone w kalkulatorze Steamworks |
| K9-65 | Testy wydania: macierz platform, czyste instalacje, Deck i Proton, serwer przez SteamCMD i Docker, aktualizacje zapisów, długi test, sprawdzenie warunków startu | Sonnet 5.5 | K5-28, K6-40, K8-57, K9-59, K9-60, K9-61, K9-62, K9-63, K9-64 | wszystkie kryteria ukończenia spełnione |

FAQ EA od pierwszego dnia zapowiada podwyżkę przy 1.0 i opisuje wybrany wzorzec: podwyżka co najmniej 30 dni przed 1.0 z rabatem na premierę albo podwyżka w chwili przejścia do 1.0 bez rabatu. Plan rozwoju wymienia tematy „Planeta 5”, „Statki i placówki”, „Planeta 6” i „Finał i historia” ze słowem „planowane”.

## Ryzyka i otwarte kwestie

Największym ryzykiem wydania jest wydajność na Steam Decku, bo wokselowy Enshrouded dostał tam ocenę Unsupported właśnie przez grafikę.

| Ryzyko | Skutek | Odpowiedź |
| --- | --- | --- |
| Wydajność na Decku | ocena Unsupported jak w Enshrouded ([Steam](https://store.steampowered.com/saleaction/ajaxgetdeckappcompatibilityreport?nAppID=1203620)) | budżety od K8-50, zestaw Deck, impostory i LOD, pomiar na sprzęcie |
| Dodatki natywne w podwójnej precyzji | dodatek nie ładuje się w naszej kompilacji | budowa ze źródeł w potoku z K1-01 i test dymny ładowania |
| Odrzucenie w przeglądzie Steama | start się przesuwa | lista kontrolna przed zgłoszeniem; wcześniejsze przeglądy Playtestu i dema |
| Jawny adres IP serwera dedykowanego | ataki na serwery graczy | norma przy serwerach wynajmowanych; host przez sieć przekaźników ukrywa adres; limity uścisku dłoni |
| RODO | kara albo utrata zaufania | zgoda przed wysyłką, losowy identyfikator, minimalny zakres danych, polityka prywatności, usuwanie na żądanie |
| Podwyżka ceny | negatywne recenzje | zapowiedź w FAQ od pierwszego dnia; po podwyżce 30 dni bez rabatów |
| Za mało list życzeń | słaby start | strona sklepu i Playtest wcześnie; start dopiero po progu 7000 |

Źródła: [GamingOnLinux: ankieta Steama, wrzesień 2026](https://www.gamingonlinux.com/2026/10/steam-survey-for-september-2026-shows-a-drop-in-linux-with-simplified-chinese-rising/), [Steamworks: zgodność z Deckiem](https://partner.steamgames.com/doc/steamdeck/compat), [Steam: raport Decka dla Valheima](https://store.steampowered.com/saleaction/ajaxgetdeckappcompatibilityreport?nAppID=892970), [Steam: raport Decka dla Enshrouded](https://store.steampowered.com/saleaction/ajaxgetdeckappcompatibilityreport?nAppID=1203620), [Godot: eksport na macOS](https://raw.githubusercontent.com/godotengine/godot-docs/master/tutorials/export/exporting_for_macos.rst), [W4 Consoles](https://www.w4games.com/w4consoles), [dlcompare: cena Valheima](https://www.dlcompare.com/gaming-news/valheim-10-is-coming-with-a-bigger-price-tag), [Spider's Web: wyceny w Polsce](https://spidersweb.pl/2026/03/steam-wyceny-polska.html), [Steamworks: rabaty](https://partner.steamgames.com/doc/marketing/discounts), [Steamworks: serwery dedykowane](https://partner.steamgames.com/doc/sdk/uploading/distributing_gs), [Steamworks: Steam Datagram Relay](https://partner.steamgames.com/doc/features/multiplayer/steamdatagramrelay), [Wikipedia: Easy Anti-Cheat](https://en.wikipedia.org/wiki/Easy_Anti-Cheat), [Steamworks: Early Access](https://partner.steamgames.com/doc/store/earlyaccess), [Sentry: Godot](https://docs.sentry.io/platforms/godot), [Steamworks: ankieta treści](https://partner.steamgames.com/doc/gettingstarted/contentsurvey).
