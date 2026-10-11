# K4-19 · Raport odbioru Planety

Stan: szkielet (faza A, krok 0). Status wiersza: `PENDING`, `PASS` (z dowodem), `FAIL`, `EXCEPTION` (z decyzją właściciela i datą).

## Mapa kryteriów ukończenia

| # | Kryterium (spec 01) | Dowód | Status |
| --- | --- | --- | --- |
| 1 | Jedna komenda buduje silnik, SDK i szablony; CI zielone | `build.py all`, `engine.yml`, trzy kolejne zielone `ci.yml` i `nightly.yml` | PENDING |
| 2 | Solo, host, klient i serwer dedykowany (Windows i Linux) | `tools/modes_smoke.py`, `server_smoke.py`, `Manual_ModesSmoke_Linux` | PENDING |
| 3 | Kamera orbita → powierzchnia płynnie, bez dziur, bez przycięć > 50 ms | `--benchmark-report` (`OrbitToSurface`), `Manual_OrbitToSurface_1080p` | PENDING |
| 4 | 8 graczy przez zaproszenie Steam i przez serwer dedykowany po IP | `coop_soak.py` (8 botów UDP), `Manual_Steam_EightPlayers` | PENDING |
| 5 | Edycje widoczne u wszystkich w ≤ 200 ms przy RTT 100 ms | test `Coop_Rtt100Jitter10_*` (150 ms sieć) + boty w silniku (50 ms) | PENDING |
| 6 | Po godzinie testu z botami stan identyczny u wszystkich | soak 2 h, przerwy kontrolne co 15 min | PENDING |
| 7 | Restart zachowuje zmiany; zabicie procesu traci najwyżej ostatnią partię | testy trwałości, soak (restart w 40. min, zabicie w 80. min) | PENDING |
| 8 | 60 FPS w 1080p, tick serwera średnio ≤ 8 ms przy 8 graczach | `coop_soak.py --profile tick`, `Manual_Fps1080p_EightPlayers` | PENDING |
| 9 | Testy jednostkowe, integracyjne i odporności zapisu przechodzą w CI | numery runów `ci.yml` i `nightly.yml` | PENDING |

## Dowody

(uzupełniane w fazie E)

## Procedury Manual

Wynik każdej procedury (data, maszyna, wynik, zrzut ekranu albo log) wpisać w „Dowody”. Kategoria testu `Manual`, czyli nie uruchamia się w CI.

### Manual_Steam_EightPlayers (kryterium 4, P3)

1. Host: gra z `--dev` w trybie host przez Steam; znajomy dołącza zaproszeniem z nakładki.
2. Reszta do 8: `dotnet run --project tests/Horyzont.BotRunner -- --connect host:port --bots N` albo `coop_soak.py` z botami UDP wskazującymi UDP hosta (jeśli host słucha jednocześnie Steam i UDP).
3. Po 10 min: `digest` na hoście i u klienta, skróty zgodne; brak odmów poza rolą Abuser.
4. Bez drugiego konta: zaproszenie na 2 kontach + 8 po IP, EXCEPTION do K9-58/K9-65.

### Manual_Fps1080p_EightPlayers (kryterium 8, P2)

1. Serwer dedykowany z maską 6 rdzeni, `coop_soak.py --bots 8 --engine-bots 0 --profile tick`.
2. Klient gry 1080p z HUD deweloperskim (FPS, tick, pasmo) dołączony do tego serwera, 5 min ruchu i budowania.
3. Wpisać: średnie i minimalne FPS, średni tick z `stats` (cel <= 8 ms), specyfikację maszyny.

### Manual_OrbitToSurface_1080p (kryterium 3, P2)

1. `--dev`, kamera deweloperska, lot z 2R do 5 m w 60 s w 1080p.
2. Wpisać maks. czas klatki (cel <= 50 ms) i potwierdzić wzrokowo brak dziur w terenie; porównać z `--benchmark-report` (`OrbitToSurface`).

### Manual_ModesSmoke_Linux (kryterium 2, P1)

1. WSL2: zbudować szablon Linux `build.py`, wyeksportować `server-linux`.
2. `server_smoke.py --export` oraz `coop_soak.py` z botami Windows po UDP do serwera w WSL.
3. Bez WSL2: EXCEPTION z przeniesieniem do K9-65 (decyzja właściciela z datą).

## Wyjątki i odstępstwa (do decyzji właściciela przy odbiorze)

| Wyjątek | Opis | Stan |
| --- | --- | --- |
| E1 · wyjście silnika z naruszeniem dostępu | Serwer dedykowany kończy czasem proces kodem `0xC0000005` po własnej linii `Stopped` (zamknięcie silnika, nie logika serwera). `coop_soak.py` toleruje to tylko wtedy, gdy `Stopped` pochodzi z tego samego procesu, liczy takie wyjścia (`engine_exit_av` w `summary.json`) i oblewa przebieg przy więcej niż 1 na przebieg. Przyczyna: prawdopodobnie `GD.Print` z wątków pobocznych i zamykanie logów po `Quit()`; wprowadzono flagę zamknięcia w `GodotLoggerProvider`, zatrzymanie pompy konsoli i dołączanie wątku zapisu. CI zbiera zrzuty WER (`wer-dumps-*`). | otwarty do czasu zielonych nocnych przebiegów bez AV |
| E2 · alokacja ticku serwera | `ServerTickBench.Tick` alokuje ok. 120 B na tick (budżet 256 B zamiast 0 B). Pomiar (2026-10-10, `GC.GetAllocatedBytesForCurrentThread` wokół fragmentów ticku): cała alokacja jest w `ServerSession.Poll` > `transport.Poll(sink)`, czyli w odbiorze i dekodowaniu przychodzących wiadomości (najpewniej obiekty komunikatów wejścia i zamiarów; nie rozbito dalej); `PumpWriterEvents`, uczestnicy ticku, `AdvanceTick` i `ScheduleCheckpoint` nie alokują. Usunięcie wymaga zmiany ścieżki dekodowania w `Horyzont.Net` (kontrakt chroniony), więc poza zakresem poprawek odbioru. | EXCEPTION, do decyzji właściciela |

