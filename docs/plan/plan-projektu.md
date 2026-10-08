# Plan projektu i podział pracy modeli

> Kopia dokumentu z Claude Docs, stan na 2026-10-08. Wersja żywa: https://claude.ai/code/artifact/2e57067f-0a0b-45b5-a0a1-5a7f0d95757f
>
> Plany wykonawcze pakietów leżą w [docs/plans/](../plans/README.md). Plany K1 już tam są, a plany kolejnych pakietów Opus 5.5 pisze ze specyfikacji, zanim pakiet ruszy.

Cała gra to 6 podprojektów i 65 pakietów pracy przydzielonych według trudności: Opus 5.5 prowadzi 14 pakietów rdzenia, GPT-6.1 Sol 6, Sonnet 5.5 26, a Gemini 3.8 Flash 19 oraz wszystkie podzadania mechaniczne; przy dwóch pakietach Gemini pracuje też Dismond. Haiku 5.5 pomaga w każdym pakiecie. Plan nie ma dat, bo kolejność wyznaczają zależności pakietów, a na start gra wychodzi tylko na PC: Steam na Windows i Linuksa. Specyfikacje podprojektów: [1 Planeta](../spec/01-planeta.md), [2 Kosmos](../spec/02-kosmos.md), [3 Przetrwanie](../spec/03-przetrwanie.md), [4 Walka](../spec/04-walka.md), [5 Treść na EA](../spec/05-tresc-ea.md), [6 Wydanie](../spec/06-wydanie.md).

## Role modeli

Zasada: każde zadanie dostaje najtańszy model, który zrobi je w jakości produkcyjnej, a złożone zostają dla Opus 5.5 i GPT-6.1 Sol.

| Model | Narzędzie | Rola | Dostaje |
| --- | --- | --- | --- |
| Opus 5.5 | Claude Code | architekt i odbiorca | kontrakty Core, sieć, zapis, synchronizację terenu; plany wykonawcze wszystkich pakietów; przegląd na każdej bramce |
| GPT-6.1 Sol | Codex | drugi mocny model | złożone pakiety samodzielne (algorytmy, meshing, AI stworów); drugi przegląd pakietów Opus; zastępstwo, gdy kończą się limity Claude |
| Sonnet 5.5 | Claude Code | główny wykonawca | pakiety średniej trudności z gotowym planem: integracje, rozgrywka, narzędzia |
| Gemini 3.8 Flash (High) | Antigravity | wykonawca od wolumenu | szkielety, serializatory z gotowego formatu, UI, dane gry, skrypty, dokumentacja; podzadania mechaniczne w każdym pakiecie |
| Haiku 5.5 | Claude Code, subagent | pomocnik | przeszukiwanie kodu, uruchamianie testów i buildów, streszczanie logów i zmian, opisy commitów |

GPT-6.1 Sol to model OpenAI do złożonego kodowania, bliski ich flagowcowi ([DevX](https://www.devx.com/artificial-intelligence-ai/gpt-6-1-sol-openai-codex-cloud-explained/)). Gemini 3.8 Flash to najmocniejszy Flash, projektowany do długich zadań programistycznych, a u Ciebie ma największe i najtańsze limity ([Google](https://ai.google.dev/gemini-api/docs/latest-model)). Haiku 5.5 jest najszybszym i najtańszym modelem Claude ([Anthropic](https://platform.claude.com/docs/en/models/overview)).

Przegląd przed scaleniem:

- Pakiet Opus 5.5 → drugi przegląd GPT-6.1 Sol, bo inna rodzina modeli wyłapuje inne błędy.
- Pakiet GPT-6.1 Sol → przegląd Opus 5.5.
- Pakiet Sonnet 5.5 → przegląd Opus 5.5 od razu, gdy zmienia kontrakty Core, protokół albo schemat bazy; pozostałe na bramce kamienia milowego.
- Pakiet Gemini 3.8 Flash → przegląd Sonnet 5.5. Testy akceptacyjne pisze Sonnet, żeby tani model nie oceniał sam siebie.

Limity:

- Gemini 3.8 Flash zawsze bierze podzadania mechaniczne, także w pakietach innych modeli.
- Kończy się limit Opus → pakiety rdzenia i plany wykonawcze przejmuje GPT-6.1 Sol, a Opus robi tylko przeglądy na bramkach.
- Kończy się limit Sonnet → pakiet przejmuje GPT-6.1 Sol albo Gemini 3.8 Flash z dokładniejszym planem.
- Kończy się limit Codex → zadania Sol wracają do Opus 5.5.

Eskalacja: po dwóch nieudanych podejściach do tego samego testu model oddaje zadanie wyżej (Gemini → Sonnet → Sol albo Opus), razem z testem i logiem.

## Mapa podprojektów

```
Każdy podprojekt kończy się grywalną bramką (◇ = bramka; liczby = pakiety prowadzone przez każdy model)

1 · Planeta · 19 pakietów ◇
    co-op na kulistej planecie: kopanie, budowanie, trwały zapis
    Opus 6 · Sol 2 · Sonnet 7 · Gemini 4
          │                                             │
          ▼                                             ▼
2 · Kosmos · 9 pakietów ◇                     3 · Przetrwanie · 12 pakietów ◇
    lot między planetami                          zbieranie, rzemiosło
    bez ekranu ładowania                          i baza z zasobów
    Opus 4 · Sol 1 · Sonnet 3 · Gemini 1          Opus 1 · Sol 1 · Sonnet 5 · Gemini 5
          │                                             │
          └──────────────────────┬──────────────────────┘
                                 ▼
4 · Walka · 9 pakietów ◇
    walka wręcz i z łuku, stwory, bossowie, kompensacja opóźnień
    Opus 1 · Sol 1 · Sonnet 6 · Gemini 1
                                 ▼
5 · Treść na EA · 8 pakietów ◇
    cztery planety i czterech bossów, 20–30 h do finału EA
    Opus 1 · Sonnet 3 · Gemini 4 (jeden z Dismondem)
                                 ▼
6 · Wydanie · 8 pakietów ◇
    Early Access na Steamie: Windows i Linux, serwer dedykowany
    Opus 1 · Sol 1 · Sonnet 2 · Gemini 4 (jeden z Dismondem)

Po K4-19 Kosmos i Przetrwanie idą równolegle; Walka rusza po K5-20 i nie czeka na bramki K5 ani K6.
Przed bramką K4 mogą ruszyć K8-50, K8-56, K9-58, K9-62 i K9-64; warunki podają tabele niżej.
```

Opus 5.5 prowadzi 10 z 28 pakietów Planety i Kosmosu, gdzie powstaje rdzeń. W Przetrwaniu, Walce, Treści i Wydaniu większość pakietów prowadzą Sonnet 5.5 i Gemini 3.8 Flash na gotowym szkielecie.

## Podprojekt 1: Planeta

Planeta to 19 pakietów w czterech kamieniach milowych; Opus 5.5 prowadzi 6 pakietów rdzenia i odbiera każdą bramkę.

Kamienie milowe: K1 Fundament (K1-01 … K1-07), K2 Solo na planecie (K2-08 … K2-13), K3 Co-op (K3-14 … K3-18), K4 Odbiór (K4-19). Plany wykonawcze K1 są już w repozytorium w docs/plans/. K1-02 ma dwa etapy: A (rozwiązanie .NET bez silnika) rusza razem z K1-01, a B (projekt Godota) czeka na K1-01.

| ID | Pakiet | Prowadzi | Zależy od | Odbiór |
| --- | --- | --- | --- | --- |
| K1-01 | Pipeline silnika: Godot z Voxel Tools, podwójna precyzja, SDK C#, CI | Sonnet 5.5 | start | jedna komenda buduje edytor, SDK i szablony; CI zielone |
| K1-02 | Szkielet rozwiązania: projekty, styl kodu, analizatory, CI testów | Gemini 3.8 Flash | etap A: —; etap B: K1-01 | rozwiązanie buduje się w CI; Core, Persistence i Net bez Godota |
| K1-03 | Core: stan, operacje, walidacja, kontrakty protokołu | Opus 5.5 | K1-02A | testy operacji i determinizmu (skróty stanu) przechodzą |
| K1-04 | Core: serializatory binarne i fuzzing | Gemini 3.8 Flash | K1-03 | testy w obie strony; fuzzing bez awarii |
| K1-05 | Persistence: dziennik, checkpoint, odtwarzanie, migracje | Opus 5.5 | K1-03, K1-04 | wstrzykiwane awarie: stan równy ostatniemu trwałemu, bez duplikatów |
| K1-06 | Net: transporty w pamięci, UDP i Steam | Sonnet 5.5 | K1-02A | wspólne testy kontraktu przechodzą dla trzech transportów |
| K1-07 | Net: sesja, uścisk dłoni, zainteresowanie, limity | Opus 5.5 | K1-04, K1-06 | odmowa innego builda; limity; testy z utratą pakietów |
| K2-08 | Planeta: generator i parametry | Sonnet 5.5 | K1-01, K1-02 | powtarzalna planeta 4 km; raport zgodności Windows–Linux |
| K2-09 | Adapter Voxel Tools: strumienie, wersje bloków, zrzut do checkpointu | Opus 5.5 | K1-05, K2-08 | bloki zapisują się z wersjami; checkpoint spójny z dziennikiem |
| K2-10 | Tryby: solo i host, klient, serwer dedykowany z konsolą | Sonnet 5.5 | K1-07, K2-09 | solo, host i serwer (Windows, Linux) ładują i zapisują świat; host bez drugiej kopii terenu |
| K2-11 | Postać i kamera na kuli | Sonnet 5.5 | K2-08, K2-10 | obejście planety bez skoków kamery; lot od orbity do powierzchni |
| K2-12 | Budowle: siatki, chunki, meshing, kolizje, rejestr bloków | GPT-6.1 Sol | K1-02, K1-03 | budowla 64³ bloków bez przycięć dłuższych niż 50 ms |
| K2-13 | Narzędzia: manipulator terenu i budowanie | Sonnet 5.5 | K2-09, K2-11, K2-12 | kopanie, zasypywanie i budowanie z limitami ze specyfikacji |
| K3-14 | Predykcja, korekta i interpolacja ruchu | Opus 5.5 | K1-07, K2-11 | RTT 100 ms i 5% utraty: ruch bez widocznych przeskoków |
| K3-15 | Replikacja bytów i budowli | GPT-6.1 Sol | K2-10, K2-12 | 2 klientów: identyczne budowle po 10 min losowych edycji |
| K3-16 | Replikacja terenu: wersje, cache, sumy kontrolne | Opus 5.5 | K2-09, K2-10 | edycje u innych w najwyżej 200 ms przy RTT 100 ms; sumy zgodne |
| K3-17 | Steam: lobby i zaproszenia | Gemini 3.8 Flash | K1-06, K2-10 | dołączenie do hosta przez zaproszenie Steam |
| K3-18 | Interfejs: menu, lista światów, HUD deweloperski | Gemini 3.8 Flash | K2-10 | menu solo, hostuj, dołącz; lista światów; HUD z FPS, tickiem i pasmem |
| K4-19 | Boty, testy integracyjne i wytrzymałościowe, benchmarki | Sonnet 5.5 | K2-13 … K3-18 | wszystkie kryteria ukończenia ze specyfikacji spełnione |

W każdym pakiecie podzadania mechaniczne (szkielety klas, dane, dokumentacja) robi Gemini 3.8 Flash, a Haiku 5.5 uruchamia testy i przeszukuje kod.

## Podprojekty 2–6

Każdy podprojekt ma pełną specyfikację z zakresem, architekturą, operacjami, testami i kryteriami odbioru. Tabele pokazują, kto prowadzi każdy pakiet i na co czeka; zakres i odbiór każdego pakietu podaje specyfikacja.

### 2. Kosmos

Efekt: lot statkiem z bloków między czterema ciałami układu bez ekranu ładowania. Kamień milowy K5, specyfikacja: [docs/spec/02-kosmos.md](../spec/02-kosmos.md).

| ID | Pakiet | Prowadzi | Zależy od |
| --- | --- | --- | --- |
| K5-20 | Ramy odniesienia w ruchu | Opus 5.5 | K4-19 |
| K5-21 | Układ gwiezdny | Opus 5.5 | K5-20 |
| K5-22 | Wiele terenów i przejście orbita–powierzchnia | Opus 5.5 | K5-21 |
| K5-23 | Bloki statku i tryb statku | Sonnet 5.5 | K4-19 |
| K5-24 | Model lotu | Sonnet 5.5 | K5-21, K5-23 |
| K5-25 | Fotele i sterowanie | GPT-6.1 Sol | K5-24 |
| K5-26 | Replikacja statków i pasażerów | Opus 5.5 | K5-24 |
| K5-27 | Mapa układu i planety, znaczniki, nawigacja | Gemini 3.8 Flash | K5-21 |
| K5-28 | Testy Kosmosu | Sonnet 5.5 | K5-22, K5-25, K5-26, K5-27 |

### 3. Przetrwanie

Efekt: zbieranie, ekwipunek, rzemiosło, jedzenie, zagrożenia planet, śmierć i baza z zasobów z wytrzymałością. Kamień milowy K6, równolegle z Kosmosem, specyfikacja: [docs/spec/03-przetrwanie.md](../spec/03-przetrwanie.md).

| ID | Pakiet | Prowadzi | Zależy od |
| --- | --- | --- | --- |
| K6-29 | Przedmioty i kontenery | Opus 5.5 | K4-19 |
| K6-30 | Bloki funkcyjne | Sonnet 5.5 | K6-29, K2-12 |
| K6-31 | Zbieranie | Sonnet 5.5 | K6-29, K2-09, K3-16 |
| K6-32 | Rzemiosło i stacje | Gemini 3.8 Flash | K6-29, K6-30 |
| K6-33 | Stan postaci i jedzenie | Gemini 3.8 Flash | K6-29 |
| K6-34 | Zagrożenia i kombinezon | Gemini 3.8 Flash | K6-30, K6-33 |
| K6-35 | Śmierć i odrodzenie | Sonnet 5.5 | K6-29, K6-30, K6-33 |
| K6-36 | Budowanie z zasobów i wytrzymałość | GPT-6.1 Sol | K6-29, K6-31, K2-12 |
| K6-37 | Teleporty i logistyka | Sonnet 5.5 | K6-30, K6-34, K5-22, K5-25 |
| K6-38 | Uprawy | Gemini 3.8 Flash | K6-30, K6-32, K6-34 |
| K6-39 | Interfejs i ustawienia świata | Gemini 3.8 Flash | K6-32, K6-33, K6-34, K6-35, K6-36, K6-37, K6-38 |
| K6-40 | Testy Przetrwania | Sonnet 5.5 | K6-31, K6-34, K6-35, K6-37, K6-38, K6-39 |

### 4. Walka

Efekt: walka wręcz i dystansowa ze stworami i bossem 1, z kompensacją opóźnień na serwerze. Kamień milowy K7, specyfikacja: [docs/spec/04-walka.md](../spec/04-walka.md).

| ID | Pakiet | Prowadzi | Zależy od |
| --- | --- | --- | --- |
| K7-41 | Trafienia w sieci | Opus 5.5 | K5-20 |
| K7-42 | System walki | Sonnet 5.5 | K7-41, K6-33, K6-35 |
| K7-43 | Kamera i namierzanie | Sonnet 5.5 | K7-42, K5-22 |
| K7-44 | Broń dystansowa | Sonnet 5.5 | K7-41, K7-43, K6-32 |
| K7-45 | AI i nawigacja | GPT-6.1 Sol | K7-42, K5-22 |
| K7-46 | Replikacja i pojawianie się stworów | Sonnet 5.5 | K7-41, K7-45 |
| K7-47 | Framework bossów i boss 1 | Sonnet 5.5 | K7-46, K6-32, K6-35 |
| K7-48 | Animacje, efekty i dźwięk walki | Gemini 3.8 Flash | K7-42, K7-47 |
| K7-49 | Testy walki | Sonnet 5.5 | K7-44, K7-47, K7-48 |

### 5. Treść na Early Access

Efekt: cztery typy planet, czterech bossów, fauna, progresja i kierunek gry; 20–30 h do bossa 4. Kamień milowy K8, specyfikacja: [docs/spec/05-tresc-ea.md](../spec/05-tresc-ea.md).

| ID | Pakiet | Prowadzi | Zależy od |
| --- | --- | --- | --- |
| K8-50 | Kierunek artystyczny i potok zasobów | Sonnet 5.5 | K2-11 |
| K8-51 | Typy planet | Sonnet 5.5 | K5-22, K6-31, K6-34 |
| K8-52 | Fauna | Gemini 3.8 Flash | K7-46, K7-48, K8-51 |
| K8-53 | Bossowie 2–4 | Sonnet 5.5 | K7-47, K7-48, K8-51 |
| K8-54 | Progresja i przedmioty | Gemini 3.8 Flash | K5-23, K6-32, K6-34, K7-44 |
| K8-55 | Kierunek i wprowadzenie | Gemini 3.8 Flash | K5-27, K6-39, K7-47, K8-51, K8-53, K8-54, K9-58 |
| K8-56 | Zasoby graficzne i dźwiękowe | Dismond z Gemini 3.8 Flash | K8-50 |
| K8-57 | Balans i pomiar | Opus 5.5 | K5-28, K6-40, K7-49, K8-52, K8-53, K8-55, K8-56, K9-62, K9-64 |

### 6. Wydanie

Efekt: Early Access na Steamie, na start tylko PC (Windows i Linux, Steam Deck z oceną Playable), z serwerem dedykowanym. Kamień milowy K9, specyfikacja: [docs/spec/06-wydanie.md](../spec/06-wydanie.md).

| ID | Pakiet | Prowadzi | Zależy od |
| --- | --- | --- | --- |
| K9-58 | Integracja Steam | Sonnet 5.5 | K2-10, K3-17 |
| K9-59 | Serwer dedykowany do pobrania | Gemini 3.8 Flash | K9-58, K6-29, K6-32, K6-35 |
| K9-60 | Optymalizacja | Opus 5.5 | K8-51, K8-52, K8-53, K8-56 |
| K9-61 | Ustawienia, sterowanie, dostępność i lokalizacja | Gemini 3.8 Flash | K3-18, K5-27, K6-34, K6-36, K7-43, K7-44, K8-55 |
| K9-62 | Raporty awarii i telemetria | Gemini 3.8 Flash | K1-01, K3-18 |
| K9-63 | Audyt bezpieczeństwa | GPT-6.1 Sol | K5-28, K6-40, K7-49, K8-55, K9-59 |
| K9-64 | Wydanie na Steamie: sklep, Playtest, FAQ, ceny | Dismond z Gemini 3.8 Flash | K8-50, K9-58 |
| K9-65 | Testy wydania | Sonnet 5.5 | K5-28, K6-40, K8-57, K9-59, K9-60, K9-61, K9-62, K9-63, K9-64 |

Kody operacji, intencji, odmów, komunikatów i blobów mają zakresy rozdzielone między podprojekty, a numer migracji nadaje się przy scaleniu. Dzięki temu Kosmos i Przetrwanie powstają równolegle bez konfliktów.

## Jak prowadzić pakiet

Każdy pakiet idzie tą samą drogą: osobna gałąź i świeża sesja z minimalnym kontekstem, żeby nie zużywać limitów na historię rozmów.

1. Kontekst sesji: link do specyfikacji, ten plan, wiersz pakietu i interfejsy pakietów, od których zależy.
2. Plan szczegółowy: Opus 5.5 zapisuje w repo pliki, interfejsy, testy i kroki ze specyfikacji, zanim pakiet ruszy; plany K1 już tam są. W Claude Code służy do tego skill writing-plans.
3. Testy akceptacyjne powstają przed kodem; dla pakietów Gemini pisze je Sonnet 5.5.
4. Implementacja: test, kod, zielone testy, commit. Haiku 5.5 jako subagent przeszukuje kod, uruchamia testy i streszcza logi.
5. Przegląd według reguł z sekcji Role modeli; poprawki wracają do modelu prowadzącego.
6. Odbiór: kryterium z tabeli i zielone CI, potem scalenie. Na koniec kamienia milowego Opus 5.5 przegląda całą zmianę w świeżej sesji.

| Narzędzie | Jak uruchomić właściwy model |
| --- | --- |
| Claude Code | `claude --model opus` przy starcie albo `/model sonnet` w sesji; tryb `opusplan` (Opus planuje, Sonnet wykonuje); subagent pomocniczy z `model: haiku` |
| Codex | `codex --model gpt-6.1-sol`, wysiłek rozumowania medium albo high |
| Antigravity | Gemini 3.8 Flash z poziomem myślenia High |

Wspólne zasady: jeden plik zasad w repo (architektura, styl, komendy testów), do którego odsyłają `CLAUDE.md` dla Claude Code, `AGENTS.md` dla Codex i reguły projektu w Antigravity. Dzięki temu każdy model pracuje według tych samych reguł.

## Rozstrzygnięte decyzje

Siedem otwartych decyzji rozstrzygnął raport „Otwarte decyzje projektowe Horyzontu”, a specyfikacje 2–6 opisują je ze źródłami.

| Obszar | Rozstrzygnięcie | Gdzie |
| --- | --- | --- |
| Kosmos: chodzenie po statku | w locie wszyscy siedzą, do 8 osób; po zaparkowanych konstrukcjach chodzi się swobodnie; każda encja ma pozycję w ramie rodzica | specyfikacja 2 |
| Kosmos: układ i przeloty | jeden układ: planeta domowa i trzy nieruchome ciała 18–110 km od niej; limit prędkości zależny od wysokości, napęd pulsacyjny do 2 km/s | specyfikacja 2 |
| Przetrwanie: ekwipunek i jedzenie | jednolite sloty bez wagi (40 plus 10 na pasku); trzy miejsca na jedzenie bez śmierci z głodu; rzemiosło i budowanie ze skrzyń w promieniu bazy | specyfikacja 3 |
| Przetrwanie: wytrzymałość | całkowitoliczbowe podparcie budowli na gruncie, statki tylko spójność; ustawienie świata Off, On, Relaxed | specyfikacja 3 |
| Walka | kamera zza ramienia i pierwsza osoba; opcjonalne namierzanie; łuki i jedna broń energetyczna na pociskach; kompensacja opóźnień także dla stworów | specyfikacja 4 |
| Treść EA | cztery typy planet i czterech bossów; 20–30 h do bossa 4, minimum 15 h | specyfikacja 5 |
| Wydanie | na start tylko PC: Steam na Windows i Linuksa, Deck z oceną Playable; 19,99 USD z zapowiedzianą podwyżką przy 1.0; bez crossplayu i anti-cheatu w EA | specyfikacja 6 |

Źródła:

- [Claude: przegląd modeli](https://platform.claude.com/docs/en/models/overview)
- [Claude Code: konfiguracja modeli](https://code.claude.com/docs/en/model-config)
- [GPT-6.1 Sol: opis i Codex (DevX)](https://www.devx.com/artificial-intelligence-ai/gpt-6-1-sol-openai-codex-cloud-explained/)
- [GPT-6.1 Sol: użycie w Codex](https://flaviocopes.com/gpt-6-1-sol/)
- [Gemini 3.8 Flash: nowości](https://ai.google.dev/gemini-api/docs/latest-model)
- [Gemini 3.8 Flash: premiera i dostępność](https://tbreak.com/gemini-3-8-flash-launch/)
