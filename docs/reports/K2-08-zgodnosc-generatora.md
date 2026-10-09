# K2-08: zgodność generatora `core:planet/1` między runtime'ami

Status: **pełny, IDENTICAL**. Kolumny Windows zmierzone lokalnie (2026-10-09); CI 2026-10-09: `ci.yml` przebieg 37904314754 (edytor Windows kontra Linux) i `nightly.yml` przebieg 37931289532 (cztery runtime'y, commit `3069aa3`), wynik ogólny IDENTICAL.

Zestaw próbek: 2334 bloków (2275 LOD 0 i 59 LOD > 0), planeta domowa, ziarno galaktyki `0x484F52595A4F4E54`, profil `core:planet/1`,
silnik 4.7.2 `double` (klucz `cec7d1354ea3`), Voxel Tools 1.7.0 (`2ac9f5f8`). Porównanie: `tools/generator/compare_reports.py`.

| Runtime | Źródło | Bloki LOD 0 zgodne z windows-editor | Bloki LOD > 0 zgodne | Mediana czasu bloku | Wynik |
| --- | --- | --- | --- | --- | --- |
| windows-editor | lokalnie | wzorzec | wzorzec | 2034,2 us | wzorzec |
| windows-template_release | lokalnie (eksport z edytora Windows) | 2275/2275 | 59/59 | 1341,8 us | IDENTICAL |
| linux-editor | CI (`nightly.yml` 37931289532; też `generator-consistency` 37904314754) | 2275/2275 | 59/59 | 2534,1 us | IDENTICAL |
| linux-template_release | CI (`nightly.yml` 37931289532, `release-linux`) | 2275/2275 | 59/59 | 1602,6 us | IDENTICAL |

## Jak odtworzono Windows

- Edytor: `godot.windows.editor.double.x86_64.mono.console.exe --headless --path game -- --generator-report <katalog> --dump-keys <plik kluczy>`.
- Release: `--export-release windows-release ../build/windows-release/Horyzont.exe` z szablonem
  `godot.windows.template_release.double.x86_64.mono.exe` (preset `windows-release`, `custom_template/release` ustawiony tymczasowo na ścieżkę szablonu),
  potem `Horyzont.exe --headless -- --generator-report <katalog>`. Eksport zawiera zestaw `Horyzont.Game.dll` bez testów i bez adaptera gdUnit4
  (`Compile Remove` dla konfiguracji `ExportRelease`).
- `--dump-keys` (zrzut surowych bajtów dla wskazanych bloków) sprawdzony w edytorze Windows.

## Kontrola w CI

1. Job `game (ubuntu-latest)` i `generator-consistency` w `ci.yml`: edytor Windows kontra edytor Linux (przy każdym PR).
2. `nightly.yml` (ręcznie `workflow_dispatch`, raz w tym PR): `release-windows`, `release-linux`, `release-compare` porównują cztery raporty.
3. Różnica na LOD 0 blokuje scalenie: diagnoza przez `--dump-keys` i eskalacja do właściciela (Pytanie 2), nie scalanie.
