# Instrukcje dla agentów (Codex, Antigravity i inne)

Najpierw przeczytaj `docs/ZASADY.md` i plan pakietu z `docs/plans/`, nad którym pracujesz. Te pliki są nadrzędne wobec twoich domyślnych nawyków.

## Sposób pracy

1. Ustal pakiet i jego plan. Pracuj tylko w jego zakresie i na gałęzi podanej w nagłówku planu.
2. Najpierw testy akceptacyjne z planu (jeśli plan mówi, że pisze je inny model, one już są w repo — nie zmieniaj ich bez zgody).
3. Mały krok: test, kod, zielone testy, commit w formacie Conventional Commits.
4. Gdy plan jest niejasny albo sprzeczny ze specyfikacją (`docs/spec/`), zatrzymaj się i zapisz pytanie w opisie PR.
5. Nie zmieniaj kontraktów chronionych (`docs/ZASADY.md`, punkt 3), jeśli plan pakietu wprost tego nie przewiduje.
6. Po dwóch nieudanych podejściach do tego samego testu przekaż zadanie wyżej z testem i logiem (`docs/ZASADY.md`, punkt 12).
7. Przed zakończeniem uruchom pełne budowanie i testy, a wynik wklej do opisu PR.

## Ustawienia modeli

- Codex: `codex --model gpt-6.1-sol`, wysiłek rozumowania `medium`, przy pakietach rdzenia `high`.
- Antigravity: Gemini 3.8 Flash z poziomem myślenia High. W ustawieniach reguł projektu wskaż ten plik i `docs/ZASADY.md`.

## Komendy

```
dotnet build Horyzont.sln
dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"
dotnet format Horyzont.sln --verify-no-changes
python tools/engine/build.py smoke
```
