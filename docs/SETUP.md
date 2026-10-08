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

## Linux (szablon eksportu, CI)

Wymagania budowania Godota dla `linuxbsd` według dokumentacji „Compiling for Linux, *BSD” dla wersji 4.7, plus Python 3.12, SCons i .NET SDK 10.

## Kroki

1. `git clone` repozytorium, potem `git lfs pull`.
2. Zbuduj silnik: `python tools/engine/build.py all`. Pierwsze budowanie trwa 1–2 h; kolejne bez zmian w `engine/engine.json` i `tools/engine/build.py` nic nie przebudowują.
3. Sprawdź silnik: `python tools/engine/build.py smoke` musi wypisać `SMOKE OK`.
4. Zbuduj i przetestuj rozwiązanie: `dotnet build Horyzont.sln`, potem `dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"`.
5. Otwórz `game/project.godot` edytorem z `engine/out/<klucz>/` (plik `godot.windows.editor.double.x86_64.mono.exe`).

## Typowe problemy

- **Restore bierze oficjalne albo nieaktualne paczki Godota.** Lokalne paczki mają ten sam numer wersji co oficjalne, a NuGet nie rozpakowuje ponownie wersji, którą już ma. `python tools/engine/build.py activate` kopiuje właściwe paczki do `engine/nuget-local` i usuwa stare z `.nuget/packages`.
- **Build Godota przerywa się błędem o zależnościach Direct3D 12.** SCons uruchomiono ręcznie, bez flag z `engine/engine.json`. Buduj wyłącznie przez `tools/engine/build.py`.
- **Błędy długich ścieżek.** Włącz `core.longpaths` i przenieś repozytorium bliżej korzenia dysku.
- **Edytor nie widzi klas `Voxel*` w C#.** Glue .NET wygenerowano edytorem bez modułu. Uruchom `python tools/engine/build.py all --clean`.
- **`SMOKE FAIL: NuGet GodotSharp built with single precision` albo `MissingFieldException` w teście dymnym.** Aktywne są paczki zbudowane bez `--precision=double` (np. po teście 4 z K1-01). Uruchom `python tools/engine/build.py activate`.
- **`SMOKE FAIL: Editor GodotSharp built with single precision`.** Katalog `engine/out/<klucz>/GodotSharp/` obok edytora zbudowano z pojedynczą precyzją. Uruchom `python tools/engine/build.py assemblies`.
