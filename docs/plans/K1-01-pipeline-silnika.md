# K1-01 · Pipeline silnika

| | |
| --- | --- |
| Prowadzi | Sonnet 5.5 (Claude Code) |
| Testy akceptacyjne | Sonnet 5.5 |
| Przegląd | Opus 5.5 na bramce K1 |
| Zależy od | — |
| Gałąź | `pakiet/k1-01-pipeline-silnika` |

**Cel:** jedna komenda buduje Godota 4.7.2 z modułem Voxel Tools 1.7, z podwójną precyzją i C#: edytor Windows, szablony eksportu (Windows debug i release, Linux release) oraz paczki SDK C# w lokalnym źródle NuGet; CI robi to samo z cache.

## Kontekst

- `docs/ZASADY.md`
- `docs/spec/01-planeta.md`, sekcja Architektura → „Budowanie silnika”
- `engine/engine.json` (już w repo: przypięte wersje, flagi SCons i cele)
- Dokumentacja Godota 4.7: „Compiling for Windows”, „Compiling for Linux, *BSD”, „Compiling with .NET”, „Large world coordinates”

## Zakres

- `tools/engine/build.py`: Python 3.12+, tylko biblioteka standardowa plus SCons.
- `engine/smoke/`: minimalny projekt Godota w C#, test dymny.
- `nuget.config` w korzeniu repo (lokalne źródło paczek Godota, własny folder paczek, mapowanie źródeł).
- `.github/workflows/engine.yml`.
- Aktualizacja `docs/SETUP.md` o realne czasy i komendy.

Poza zakresem: macOS, Android, wersje z Tracy, podpisywanie plików wykonywalnych, sterowniki D3D12 i ANGLE oraz AccessKit (decyzja niżej).

## Projekt

### Układ katalogów

```
engine/
  engine.json        przypięte wersje, flagi SCons i cele (jedyne źródło prawdy, w repo)
  smoke/             projekt testu dymnego (w repo)
  src/               źródła (ignorowane): godot/ z modułem w godot/modules/voxel, STAMP.json
                     oraz bariery Directory.Build.props i Directory.Packages.props, które pisze fetch
  out/<klucz>/       binaria, GodotSharp/, nupkgs/, manifest.json, logs/ (ignorowane)
  nuget-local/       aktywne paczki Godot.NET.Sdk, GodotSharp, GodotSharpEditor, Godot.SourceGenerators
                     i ACTIVE.json (ignorowane)
tools/engine/build.py
```

`<klucz>` to pierwsze 12 znaków SHA-256 z zawartości `engine.json` i `build.py`. Zmiana któregokolwiek pliku daje nowy katalog wyjściowy; brak zmian oznacza brak przebudowy. Opcje testowe (`--assemblies-precision`) nie wchodzą do klucza.

### Flagi SCons

Każde wywołanie SCons dostaje `precision=double module_mono_enabled=yes`, flagi z `build.sconsFlags.all`, flagi dla danego `target` (np. `build.sconsFlags.template_release`) oraz `cache_path=<cache poza engine/src> cache_limit=<GiB>`. W `engine.json`:

- `d3d12=no`: w 4.7.2 sterownik D3D12 jest na Windows domyślnie włączony, a bez bibliotek Mesa/NIR `platform/windows/detect.py` przerywa build (`sys.exit(255)`).
- `accesskit=no`, `angle=no`: bez swoich SDK oba wyłączają się po cichu, więc binaria różniłyby się między maszynami.
- `production=yes lto=none` dla `template_release`: ustawienia produkcyjne bez LTO, które na runnerach CI z 7 GB RAM grozi brakiem pamięci.

Decyzja: w K1 silnik ma tylko Vulkan (Forward+) i nie wymaga zewnętrznych zależności. D3D12 (zapasowy sterownik na Windows), LTO i szablon debug dla Linuksa włącza pakiet K9-60 (optymalizacja), a AccessKit (czytniki ekranu) pakiet K9-61 (dostępność). Wymaga to wtedy kroku `deps` z `misc/scripts/install_d3d12_sdk_windows.py` i `install_accesskit.py` ze źródeł Godota oraz mocniejszego runnera.

### Komendy `build.py`

| Komenda | Działanie |
| --- | --- |
| `fetch` | płytki klon Godota na `godot.ref` do `engine/src/godot`, płytki klon Voxel Tools na `voxel.ref` do `engine/src/godot/modules/voxel`; zapis `engine/src/STAMP.json` z hashami commitów; zapis barier (niżej) |
| `editor` | `scons platform=windows target=editor` z flagami (tylko Windows) |
| `glue` | `bin/godot.windows.editor.double.x86_64.mono.exe --headless --generate-mono-glue modules/mono/glue` |
| `assemblies` | `python modules/mono/build_scripts/build_assemblies.py --godot-output-dir=./bin --precision=double --push-nupkgs-local <bezwzględna ścieżka engine/out/<klucz>/nupkgs>`, kopia `bin/GodotSharp/` do `engine/out/<klucz>/`, potem `activate` |
| `activate` | aktywuje paczki: czyści `engine/nuget-local`, kopiuje do niego `.nupkg` z `engine/out/<klucz>/nupkgs` (albo z `--variant <nazwa>` lub `--from <katalog>`); gdy zestaw różni się od zapisanego w `ACTIVE.json`, usuwa `.nuget/packages/{godot.net.sdk,godotsharp,godotsharpeditor,godot.sourcegenerators}/4.7.2`; zapisuje `ACTIVE.json` (klucz, wariant, SHA-256 paczek) |
| `templates` | dla każdego celu z `engine.json` pasującego do bieżącego systemu (albo tylko `--target`): `scons platform=<p> target=<t>` z flagami |
| `smoke` | `activate`, jeśli aktywny zestaw nie pasuje do klucza; `dotnet build engine/smoke/Smoke.csproj`; sprawdzenie źródła paczek; uruchomienie testu dymnego (niżej) |
| `all` | Windows: fetch → editor → glue → assemblies → templates → smoke. Linux: fetch → templates. Z `--target <nazwa>` tylko kroki tego celu: `editor-windows` to fetch → editor → glue → assemblies → smoke, a szablon to fetch → templates |

Opcje wspólne: `--jobs N` (domyślnie liczba rdzeni), `--clean` (usuwa `engine/src` i bieżący `engine/out/<klucz>`), `--dry-run` (wypisuje komendy bez uruchamiania), `--target <nazwa celu z engine.json>`. Opcja tylko do testu 4: `assemblies --assemblies-precision single`.

Wymagania wobec skryptu:

- Kod wyjścia różny od zera przy każdym błędzie; log każdego kroku w `engine/out/<klucz>/logs/<krok>.log`.
- Po każdym kroku zapis do `engine/out/<klucz>/manifest.json`: nazwy plików, rozmiary, SHA-256, hash commitu Godota i Voxel Tools, użyte flagi SCons.
- Kroki są idempotentne: drugi `all` bez zmian kończy się w mniej niż minutę.
- Nazwy binariów zgodne z konwencją Godota dla podwójnej precyzji i .NET: `godot.windows.editor.double.x86_64.mono.exe`, `godot.windows.template_debug.double.x86_64.mono.exe`, `godot.windows.template_release.double.x86_64.mono.exe`, `godot.linuxbsd.template_release.double.x86_64.mono`. Skrypt kopiuje je razem z katalogiem `GodotSharp/` do `engine/out/<klucz>/`.
- Cache kompilacji SCons (opcje `cache_path` i `cache_limit` z `SConstruct` 4.7.2) leży poza `engine/src`, żeby `--clean` go nie kasował.

### Bariery MSBuild w `engine/src`

Źródła Godota leżą wewnątrz repozytorium, więc MSBuild budujący GodotSharp, GodotTools i Godot.NET.Sdk szuka plików `Directory.*.props` w górę katalogów i trafiłby na nasze. `modules/mono/Directory.Build.props` Godota zatrzymuje nasz `Directory.Build.props`, ale w drzewie 4.7.2 nie ma żadnego `Directory.Packages.props`. Bez bariery nasze centralne wersje paczek (K1-02) włączyłyby się w projektach Godota i ich `PackageReference … Version="…"` dałyby błąd NU1008. `fetch` zapisuje więc:

- `engine/src/Directory.Packages.props`: `<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>`
- `engine/src/Directory.Build.props`: `<Project />`

`nuget.config` z korzenia repozytorium obowiązuje także build Godota. To nie przeszkadza: zależności Godota przychodzą z nuget.org, a przy `--push-nupkgs-local` Godot sam czyści swoje paczki z folderu `.nuget/packages`, który ten plik wskazuje.

### Wersja silnika

`engine.json` przypina tag `4.7.2-stable` i Voxel Tools `v1.7`. Oficjalne wydanie Voxel Tools 1.7 zbudowano z gałęzi 4.7 (wersja `4.7.2.stable`, commit `ed1daf0bf`). W kroku `fetch` zapisz hash commitu tagu `4.7.2-stable` i porównaj z `ed1daf0bf`; jeśli się różnią, a moduł się kompiluje, zostaw tag i odnotuj różnicę w PR.

### Kolizja z oficjalnymi paczkami NuGet

Lokalne paczki mają ten sam numer wersji (4.7.2) co oficjalne na nuget.org, więc restore mógłby wziąć oficjalne bez modułu i z pojedynczą precyzją. `nuget.config` w korzeniu repo:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config>
    <add key="globalPackagesFolder" value=".nuget/packages" />
  </config>
  <packageSources>
    <clear />
    <add key="godot-local" value="engine/nuget-local" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="godot-local">
      <package pattern="Godot.*" />
      <package pattern="GodotSharp*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

Najbardziej szczegółowy wzorzec wygrywa, więc `Godot.*` i `GodotSharp*` biorą się wyłącznie z lokalnego źródła. Ten sam numer wersji oznacza też, że NuGet nie rozpakuje nowej paczki, jeśli w `.nuget/packages` leży już stara o tej wersji; dlatego `activate` usuwa je przy każdej zmianie aktywnego zestawu.

### Test dymny (`engine/smoke/`)

Pliki:

- `project.godot`: nazwa `HoryzontSmoke`, główna scena `res://Main.tscn`, `dotnet/project/assembly_name="Smoke"`, `rendering/rendering_device/driver.windows="vulkan"`.
- `Directory.Build.props` z samym `<Project />` i `Directory.Packages.props` z `ManagePackageVersionsCentrally=false`, żeby izolować projekt od ustawień rozwiązania.
- `Smoke.csproj`: `<Project Sdk="Godot.NET.Sdk/4.7.2">`, `TargetFramework` z `engine.json` (`net10.0`), `Nullable` włączone, `<GodotFloat64>true</GodotFloat64>` (Godot.NET.Sdk definiuje `GODOT_REAL_T_IS_DOUBLE` tylko z tą właściwością; domyślnie jest `false`).
- `Main.tscn` z węzłem `Node` i skryptem `SmokeTest.cs`.

```csharp
using System;
using System.Collections.Generic;
using Godot;

public partial class SmokeTest : Node
{
    // Wybór przeciążenia zapada przy kompilacji, więc sprawdza paczkę GodotSharp z NuGet.
    private static bool IsDouble(double _) => true;
    private static bool IsDouble(float _) => false;

    public override void _Ready()
    {
        var failures = new List<string>();

        // Podwójna precyzja w paczce NuGet, z którą kompiluje się projekt (i która trafia do eksportu).
        if (!IsDouble(default(Vector3).X))
            failures.Add("NuGet GodotSharp built with single precision");

        // Podwójna precyzja w GodotSharp ładowanym w czasie działania: projekt uruchomiony edytorem
        // dostaje GodotSharp z engine/out/<klucz>/GodotSharp/, nie z paczki.
        if (typeof(Vector3).GetField(nameof(Vector3.X))!.FieldType != typeof(double))
            failures.Add("Editor GodotSharp built with single precision");

        // Moduł Voxel Tools musi być w silniku i w glue C# (kompilacja tej linii to też test).
        var terrain = new VoxelLodTerrain();
        terrain.Free();
        if (!ClassDB.ClassExists("VoxelLodTerrain"))
            failures.Add("Voxel Tools module missing in engine");

        if (Environment.Version.Major < 10)
            failures.Add($".NET runtime {Environment.Version} is older than 10");

        GD.Print(failures.Count == 0 ? "SMOKE OK" : "SMOKE FAIL: " + string.Join("; ", failures));
        GetTree().Quit(failures.Count == 0 ? 0 : 1);
    }
}
```

Gdy precyzja paczki i silnika się różni, JIT rzuca już przy kompilacji `_Ready` wyjątek `MissingFieldException` (pole `Vector3.X` ma w czasie działania inny typ niż przy kompilacji), więc `SMOKE OK` się nie pojawi. To też jest wynik negatywny.

`build.py smoke`:

1. `activate`, jeśli `ACTIVE.json` wskazuje inny klucz niż bieżący. Aktywny wariant bieżącego klucza (także testowy) zostaje bez zmian.
2. `dotnet build engine/smoke/Smoke.csproj`.
3. Sprawdzenie źródła: dla każdej z czterech paczek pole `"source"` w `.nuget/packages/<id>/4.7.2/.nupkg.metadata` musi wskazywać `engine/nuget-local`. Sam wynik testu nie mówi, skąd przyszło `Godot.NET.Sdk`.
4. `<edytor> --headless --path engine/smoke --quit-after 600` z limitem czasu procesu 120 s (wyjątek w `_Ready` nie kończy Godota sam z siebie, a CI czekałoby do limitu jobu) i sprawdzenie kodu wyjścia oraz linii `SMOKE OK` na wyjściu.

Jeśli Godot 4.7.2 nie uruchomi projektu w `net10.0` (oficjalny GodotSharp 4.7.0 ma zestawy `net8.0` zgodne z `net10.0`, ale ładowanie runtime’u sprawdza dopiero ten test): zmień `dotnetTargetFramework` na `net8.0` w `engine.json`, odnotuj to w `docs/ZASADY.md` i daj znać wykonawcy K1-02.

### CI (`.github/workflows/engine.yml`)

- Wyzwalacze: zmiany w `engine/**`, `tools/engine/**`, `nuget.config`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.github/workflows/engine.yml`; ręczne uruchomienie.
- Jeden job na cel z `engine.json` (`python tools/engine/build.py all --target <nazwa>`), równolegle:

| Job | Runner | Artefakty |
| --- | --- | --- |
| `editor-windows` | `windows-2022` | `engine-windows-editor` (edytor z `GodotSharp/`), `engine-nupkgs` (`engine/out/<klucz>/nupkgs`) |
| `template-windows-debug` | `windows-2022` | `engine-windows-template-debug` |
| `template-windows-release` | `windows-2022` | `engine-windows-template-release` |
| `template-linux-release` | `ubuntu-24.04` | `engine-linux-template` |

- Szablon `template-linux-release` służy klientowi na Linuksa i Steam Decka oraz serwerowi dedykowanemu, który uruchamia ten sam eksport z `--headless`.
- Job `template-linux-release` instaluje najpierw zależności Godota z „Compiling for Linux”: `sudo apt-get install build-essential pkg-config libx11-dev libxcursor-dev libxinerama-dev libgl1-mesa-dev libglu1-mesa-dev libasound2-dev libpulse-dev libudev-dev libxi-dev libxrandr-dev libwayland-dev`; bez nich `detect.py` dla `linuxbsd` przerywa build.
- Każdy job: Python, SCons, .NET SDK z `global.json`; cache SCons z kluczem `scons-<cel>-<klucz>` i `restore-keys: scons-<cel>-`, `cache_limit` tak, by wszystkie cele zmieściły się w 10 GB cache GitHuba (około 2 GiB na cel); cache `engine/out/<klucz>` z kluczem `engine-out-<cel>-<klucz>` (trafienie pomija budowanie, a paczki są w środku).
- Limit czasu jobu 300 minut.

## Testy akceptacyjne

1. Na czystej maszynie Windows przygotowanej według `docs/SETUP.md` `python tools/engine/build.py all` kończy się kodem 0, a `manifest.json` zawiera edytor, dwa szablony Windows i cztery paczki `.nupkg` w `engine/out/<klucz>/nupkgs`.
2. `python tools/engine/build.py smoke` wypisuje `SMOKE OK`, kończy się kodem 0, a sprawdzenie źródła potwierdza cztery paczki z `engine/nuget-local`.
3. Drugie `all` bez zmian w plikach wejściowych trwa poniżej minuty i niczego nie kompiluje.
4. `build.py assemblies --assemblies-precision single` buduje paczki pojedynczej precyzji do `engine/out/<klucz>/test-single/` (z osobnym `--godot-output-dir`, bez dotykania właściwych wyników i bez zmiany klucza). Po `build.py activate --variant test-single` `smoke` nie wypisuje `SMOKE OK` i kończy się kodem różnym od zera (komunikat o pojedynczej precyzji paczki albo `MissingFieldException`), a po `build.py activate` znowu daje `SMOKE OK`.
5. Wszystkie cztery joby CI są zielone, artefakty da się pobrać.

## Kroki

- [x] Szkielet `build.py`: parser komend, klucz konfiguracji, flagi z `engine.json`, logowanie, `--dry-run`, `--target`.
- [x] `fetch` z zapisem STAMP, porównaniem commitu z `ed1daf0bf` i barierami MSBuild.
- [x] `editor`, potem `glue`, potem `assemblies` z `--precision=double` i paczkami w `engine/out/<klucz>/nupkgs`.
- [x] `nuget.config` w korzeniu repo i `activate`.
- [x] Projekt `engine/smoke/` i komenda `smoke` ze sprawdzeniem źródła paczek; uruchomienie testów 2 i 4.
- [x] `templates` dla Windows; `all` na Windows.
- [x] Ścieżka Linux (`templates` dla `linuxbsd`), uruchomiona w CI.
- [x] `engine.yml` z jobami na cel, cache i artefaktami; zielone CI.
- [x] Aktualizacja `docs/SETUP.md` (czasy, rozmiary, napotkane problemy).

## Odstępstwa i decyzje wykonawcy

- `SmokeTest.cs` używa `System.Environment.Version` zamiast `Environment.Version` z planu: przy `using Godot;` nazwa jest niejednoznaczna z `Godot.Environment` (CS0104).
- `smoke` buduje projekt dymny z `--no-incremental`. Paczki wszystkich wariantów mają ten sam numer wersji i znaczniki czasu z archiwum, więc przyrostowy build po `activate` zostawiał `Smoke.dll` skompilowany pod poprzednie paczki i test 4 nie wracał do `SMOKE OK`.
- `scons()` zakłada katalog nadrzędny cache SCons przed uruchomieniem, bo SCons tworzy katalog cache atomowo i bez rodzica przerywa build.
- Dodatkowe opcje `--scons-cache` (zmienna `HORYZONT_SCONS_CACHE`) i `--cache-limit` (`HORYZONT_SCONS_CACHE_LIMIT`), żeby CI mogło zmieścić cache wszystkich celów w limicie GitHuba.
- Commit `engine/smoke/SmokeTest.cs.uid` generowanego przez Godota 4.7 (zalecenie Godota: pliki `.uid` trzymać w repozytorium).
- Wyniki lokalne (Windows, VS 2026 Community): test 1 (`all` od zera, około 70 min), 2, 3 (drugie `all` 8,6 s) i 4 zaliczone 2026-10-08. Test 5 zaliczony w PR #3: wszystkie cztery joby `engine.yml` zielone, w tym `template-linux-release`; artefakty edytora, paczek i trzech szablonów są do pobrania.

## Odbiór

Testy akceptacyjne 1–5 zaliczone, wynik testu 1 i 2 (fragment logu) wklejony do PR.

## Pułapki

- Glue C# trzeba wygenerować edytorem zbudowanym z modułem, inaczej w C# nie będzie klas `Voxel*`.
- `build_assemblies.py` domyślnie buduje pojedynczą precyzję; bez `--precision=double` C# i silnik nie zgadzają się co do rozmiaru `real_t`, a test dymny kończy się `SMOKE FAIL`.
- Wydania Voxel Tools zawierają edytory z podwójną precyzją, ale bez szablonów eksportu i bez .NET, więc gotowych binariów nie da się użyć.
- Bez `d3d12=no` build na czystym Windows przerywa się błędem o brakujących zależnościach D3D12; nie instaluj ich ręcznie, tylko trzymaj flagi z `engine.json`.
- Długie ścieżki i antywirus na Windows potrafią wielokrotnie wydłużyć budowanie (`docs/SETUP.md`).
