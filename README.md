# Horyzont

Gra łącząca Minecrafta, Valheima i No Man's Sky: kuliste planety z gładkim terenem voxelowym, budowle z bloków, przeloty między planetami i co-op od pierwszego wydania (Steam, Early Access, na start tylko PC: Windows i Linux).

**Horyzont to nazwa robocza.** Przestrzenie nazw w kodzie to `Horyzont.*`; zmiana nazwy to jeden mechaniczny refaktor.

## Od czego zacząć

1. Przeczytaj [docs/ZASADY.md](docs/ZASADY.md). Obowiązują każdego wykonawcę: człowieka i każdy model.
2. Przygotuj maszynę według [docs/SETUP.md](docs/SETUP.md).
3. Bierz pakiety w kolejności z [docs/plans/README.md](docs/plans/README.md). Jako pierwsze ruszają równolegle K1-01 (pipeline silnika) i K1-02A (szkielet rozwiązania).

## Dokumenty

| Dokument | Po co |
| --- | --- |
| [docs/spec/01-planeta.md](docs/spec/01-planeta.md) | Specyfikacja podprojektu 1 (Planeta): architektura, model świata, sieć, zapis, kryteria ukończenia |
| [docs/spec/02-kosmos.md](docs/spec/02-kosmos.md) | Specyfikacja podprojektu 2 (Kosmos): układ gwiezdny, statki z bloków, lot, ramy odniesienia |
| [docs/spec/03-przetrwanie.md](docs/spec/03-przetrwanie.md) | Specyfikacja podprojektu 3 (Przetrwanie): przedmioty, rzemiosło, jedzenie, zagrożenia, śmierć, wytrzymałość |
| [docs/spec/04-walka.md](docs/spec/04-walka.md) | Specyfikacja podprojektu 4 (Walka): walka, kamera, kompensacja opóźnień, stwory, bossowie |
| [docs/spec/05-tresc-ea.md](docs/spec/05-tresc-ea.md) | Specyfikacja podprojektu 5 (Treść na EA): planety, fauna, progresja, kierunek gry, zasoby |
| [docs/spec/06-wydanie.md](docs/spec/06-wydanie.md) | Specyfikacja podprojektu 6 (Wydanie): Steam, serwer dedykowany, optymalizacja, sklep |
| [docs/plan/plan-projektu.md](docs/plan/plan-projektu.md) | Plan całej gry: 6 podprojektów, 65 pakietów, podział pracy między modele, rozstrzygnięte decyzje |
| [docs/plans/](docs/plans/README.md) | Plany wykonawcze pakietów K1, gotowe do implementacji |
| [docs/badania/otwarte-decyzje.md](docs/badania/otwarte-decyzje.md) | Raport z badań, z którego pochodzą decyzje podprojektów 2–6 |
| [docs/ZASADY.md](docs/ZASADY.md) | Wspólne zasady: architektura, styl, testy, git, kontrakty chronione |
| [docs/SETUP.md](docs/SETUP.md) | Przygotowanie maszyny deweloperskiej |
| [CLAUDE.md](CLAUDE.md), [AGENTS.md](AGENTS.md) | Instrukcje startowe dla Claude Code, Codex i Antigravity |

Specyfikacje i plan projektu mają wersje żywe w Claude Docs. Kopie w `docs/` są dla agentów, które nie czytają Claude Docs. Po zmianie dokumentu w Claude Docs zaktualizuj kopię w tym samym PR co kod, którego zmiana dotyczy.

## Stos

Godot 4.7.2 z modułem Voxel Tools 1.7, kompilowany z podwójną precyzją i C# (.NET 10). Własny netcode (LiteNetLib, Steam Networking Sockets), zapis w SQLite z dziennikiem operacji.

## Docelowa struktura repozytorium (po pakietach K1)

```
engine/            engine.json (przypięte wersje silnika), smoke/ (test dymny); src/ i out/ są ignorowane
tools/engine/      build.py: budowanie silnika, SDK C# i szablonów eksportu
src/
  Horyzont.Core/         stan świata, operacje, walidacja, protokół, serializacja (bez Godota)
  Horyzont.Persistence/  SQLite: dziennik, checkpointy, migracje, kopie (bez Godota)
  Horyzont.Net/          transporty i sesja sieciowa (bez Godota)
game/              projekt Godota: sceny, render, adapter Voxel Tools, serwer i klient
tests/             testy xUnit i biblioteka pomocnicza Horyzont.Testing
docs/              specyfikacja, plan, plany pakietów, zasady
```
