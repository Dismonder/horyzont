# Przygotowanie maszyny

Główna maszyna deweloperska to Windows (edytor, gra, serwer). Linux służy do CI i do szablonu eksportu, z którego powstają klient na Linuksa i Steam Decka oraz serwer dedykowany. Komendy z kroków 2–5 zaczynają działać po ukończeniu pakietów K1-01 i K1-02.

## Windows: wymagania

| Narzędzie | Wersja | Uwagi |
| --- | --- | --- |
| Git i Git LFS | aktualne | `git lfs install`; `git config --global core.longpaths true` |
| Python | 3.12 lub nowszy | z `pip` w PATH |
| SCons | aktualny z pip | `pip install scons` |
| Visual Studio Build Tools | 2022 lub nowsze | zestaw „Programowanie aplikacji klasycznych w C++” (MSVC, Windows SDK) |
| .NET SDK | 10.x | wersję przypina `global.json` |
| Klient Steam | aktualny | tylko do ręcznego sprawdzenia transportu Steam (K1-06): dwie maszyny, dwa konta |
| Wolne miejsce | około 40 GB | źródła i wyniki budowania silnika |

Zależności sterownika Direct3D 12, ANGLE i AccessKit nie są potrzebne: `engine/engine.json` buduje silnik bez nich (K1-01). D3D12 włącza dopiero K9-60, a AccessKit K9-61.

Sklonuj repozytorium do krótkiej ścieżki, np. `C:\dev\horyzont`, i dodaj ten katalog do wykluczeń antywirusa: przyspiesza to kompilację silnika kilkukrotnie.

## Linux (edytor do testów silnika, szablon eksportu, CI)

Wymagania budowania Godota dla `linuxbsd` według dokumentacji „Compiling for Linux, *BSD” dla wersji 4.7, plus Python 3.12, SCons i .NET SDK 10. Na Linuksie `python tools/engine/build.py all` buduje edytor `editor-linux` (z glue, paczkami i testem dymnym) oraz szablon `template-linux-release`; edytor służy testom silnika i raportowi zgodności generatora w CI (artefakt `engine-linux-editor`).

## Kroki

1. `git clone` repozytorium, potem `git lfs pull`.
2. Zbuduj silnik: `python tools/engine/build.py all`. Pierwsze budowanie trwa 1–2 h; kolejne bez zmian w `engine/engine.json` i `tools/engine/build.py` nic nie przebudowują.
3. Sprawdź silnik: `python tools/engine/build.py smoke` musi wypisać `SMOKE OK`.
4. Zbuduj i przetestuj rozwiązanie: `dotnet build Horyzont.sln`, potem `dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"`.
5. Otwórz `game/project.godot` edytorem z `engine/out/<klucz>/` (plik `godot.windows.editor.double.x86_64.mono.exe`).

## Uruchamianie trybów

Tryb wybiera się argumentem po `--` (K2-10; menu przyjdzie w K3-18). `E` to edytor z `engine/out/<klucz>/` (`godot.windows.editor.double.x86_64.mono.exe`; wariant `.console.exe` jest nakładką, która nie przekazuje sygnałów konsoli), projekt to `game`. Przed pierwszym uruchomieniem: `E --headless --path game --import --quit-after 1000`.

| Tryb | Komenda |
| --- | --- |
| Solo | `E --path game -- --solo [--world <WorldId>] [--seed <hex>]` |
| Host (LAN, UDP) | `E --path game -- --host [--world <WorldId>] [--port 27015] [--bind 0.0.0.0] [--password <tekst>]` |
| Klient | `E --path game -- --connect <host>:<port> [--password <tekst>] [--profile <plik>]` |
| Serwer dedykowany | `E --headless --path game -- --server --config <server.json> [--data-dir <katalog>] [--restore-latest-backup] [--recover-from-checkpoint]` |
| Przykładowa konfiguracja | `E --headless --path game -- --write-default-config <server.json>` |

- Sterowanie w `--solo`, `--host` i `--connect` (K2-11): `W A S D` ruch, mysz obrót (kursor przechwycony; `Esc` zwalnia, klik przechwytuje), `Spacja` skok, `Shift` bieg, `Ctrl` kucanie, `V` przełącza kamerę TPP/FPP (domyślnie TPP), `F` lot deweloperski postaci (tylko kompilacja dev), `F8` kamera deweloperska, `F9` orbita (lot do powierzchni), `F1` etykieta pomocy. Narzędzia (K2-13): `1` manipulator terenu, `2` budowanie, LPM kopie (manipulator) albo usuwa blok (budowanie), PPM zasypuje albo stawia blok, kółko zmienia promień kuli 0,5–3 m co 0,25 m albo typ bloku, `M` następny materiał zasypywania, `R` obrót bloku wokół „góry”, `T` zmiana ściany „góra”; przytrzymanie przycisku powtarza akcję (teren co 13 ticków, bloki co 15), a limity serwera to zasięg 8 m, najwyżej 5 edycji terenu w każdej sekundzie, nie poniżej rdzenia planety i brak bloku w graczu. Narzędzia działają w `--solo` i `--host` (zdalny klient dostaje je z K3-15/K3-16). Dodatkowe flagi: `--dev-speed <1-64>` (mnożnik prędkości, solo/host), `--dev-tools-demo` (solo/host: po wycelowaniu w ziemię narzędzia same kopią, zasypują i stawiają trzy bloki; do zrzutów), `--screenshot <plik.png> [--screenshot-after <s>]` (zapisuje zrzut i zamyka grę kanoniczną kolejnością).
- Bez `--world` powstaje nowy świat; zapisy są w `user://saves/<WorldId>/world.db` (solo i host) albo `<data-dir>/saves/<WorldId>/world.db` (serwer; domyślnie katalog pliku wykonywalnego, id nowego świata trafia do `saves/last-world.txt`). `--data-dir` działa też dla solo i hosta. Tożsamość i nazwa gracza: `user://profile.json` (`--profile` pozwala uruchomić dwie instancje na jednej maszynie).
- Serwer dedykowany czyta polecenia ze standardowego wejścia: `save`, `stop`, `players`, `kick <id>`, `help`. `Ctrl+C`/`SIGTERM` kończy go tą samą kolejnością zamykania; trzeci sygnał kończy proces natychmiast.
- Eksport serwera: presety `server-windows` i `server-linux` (`godot --headless --path game --export-release server-windows ../build/server-windows/HoryzontServer.exe`; wymaga szablonów eksportu, np. wskazanych w `custom_template/release` presetu). Uruchomienie: `HoryzontServer.exe --headless -- --server --config server.json`.
- Test dymny serwera (uruchamia serwer z `--dev`; polecenie konsoli `dev-op` zmienia świat): `python tools/server_smoke.py --godot <edytor>` albo `--export <wyeksportowany serwer>`.

## Zmierzone czasy i rozmiary (K1-01, 2026-10-08)

Windows 11, 16 wątków, Visual Studio 2026 Community (MSVC działa tak samo jak z Build Tools 2022), repozytorium na osobnym dysku SSD:

| Krok | Czas | Uwagi |
| --- | --- | --- |
| `editor` (zimny cache SCons) | około 30 min | |
| `glue` + `assemblies` | około 1,5 min | |
| `templates` Windows debug / release | około 17 / 16 min | release z `production=yes`, `lto=none` |
| `smoke` | około 30 s | pełny `dotnet build` projektu dymnego i import w edytorze |
| całe `all` od zera | około 70 min | |
| drugie `all` bez zmian | 8 s | niczego nie kompiluje |
| `all` po zmianie `build.py` (nowy klucz, ciepły cache SCons) | około 5 min | SCons bierze obiekty z `cache/scons` |

Wyniki jednego klucza (`engine/out/<klucz>/`: edytor, szablony, GodotSharp, paczki) zajmują około 0,5 GB; źródła z obiektami kompilacji (`engine/src`) około 7,5 GB, cache SCons (`cache/scons`, limit 8 GiB, `--cache-limit`) około 3 GB po trzech celach Windows. Razem trzeba liczyć 12–20 GB.

## Typowe problemy

- **Restore bierze oficjalne albo nieaktualne paczki Godota.** Lokalne paczki mają ten sam numer wersji co oficjalne, a NuGet nie rozpakowuje ponownie wersji, którą już ma. `python tools/engine/build.py activate` kopiuje właściwe paczki do `engine/nuget-local` i usuwa stare z `.nuget/packages`.
- **Build Godota przerywa się błędem o zależnościach Direct3D 12.** SCons uruchomiono ręcznie, bez flag z `engine/engine.json`. Buduj wyłącznie przez `tools/engine/build.py`.
- **Błędy długich ścieżek.** Włącz `core.longpaths` i przenieś repozytorium bliżej korzenia dysku.
- **Edytor nie widzi klas `Voxel*` w C#.** Glue .NET wygenerowano edytorem bez modułu. Uruchom `python tools/engine/build.py all --clean`.
- **`SMOKE FAIL: NuGet GodotSharp built with single precision` albo `MissingFieldException` w teście dymnym.** Aktywne są paczki zbudowane bez `--precision=double` (np. po teście 4 z K1-01). Uruchom `python tools/engine/build.py activate`.
- **`SMOKE FAIL: Editor GodotSharp built with single precision`.** Katalog `engine/out/<klucz>/GodotSharp/` obok edytora zbudowano z pojedynczą precyzją. Uruchom `python tools/engine/build.py assemblies`.
- **SCons: `Failed to create cache directory`.** SCons tworzy katalog cache atomowo i wymaga istniejącego katalogu nadrzędnego. `build.py` zakłada go sam; przy ręcznym `--scons-cache` sprawdź, czy ścieżka jest poprawna.
- **`CS0104: 'Environment' is an ambiguous reference` w kodzie C# projektu Godota.** Przy `using Godot;` i `using System;` nazwa `Environment` wskazuje i na `Godot.Environment`, i na `System.Environment`. Pisz pełną nazwę `System.Environment`.
- **`smoke` po przełączeniu paczek dalej zgłasza `MissingFieldException`.** Paczki wszystkich wariantów mają ten sam numer wersji i stare znaczniki czasu z archiwum, więc przyrostowy build zostawiłby `Smoke.dll` skompilowany pod poprzednie paczki. `build.py smoke` buduje projekt dymny z `--no-incremental`; ręcznie użyj tej samej flagi.
