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
