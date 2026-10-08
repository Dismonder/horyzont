# K1-02 · Szkielet rozwiązania

| | |
| --- | --- |
| Prowadzi | Gemini 3.8 Flash (Antigravity, myślenie High) |
| Testy akceptacyjne | Sonnet 5.5 (pisze je przed implementacją) |
| Przegląd | Sonnet 5.5 |
| Zależy od | etap A: —; etap B: K1-01 |
| Gałęzie | `pakiet/k1-02a-szkielet`, `pakiet/k1-02b-projekt-godota` |

**Cel:** rozwiązanie .NET z projektami Core, Persistence, Net, testami i CI, które buduje się bez silnika (etap A), plus projekt Godota korzystający z tych bibliotek (etap B).

## Kontekst

- `docs/ZASADY.md` (punkty 2, 5, 8, 10)
- `docs/spec/01-planeta.md`, sekcja Architektura (tabela projektów)

## Zakres

Etap A: pliki konfiguracyjne rozwiązania, projekty bibliotek z jednym typem-znacznikiem, biblioteka testowa, projekty testów, test warstw, CI. Etap B: projekt Godota `game/` ze sceną startową i testem dymnym.

Poza zakresem: jakakolwiek logika gry. Jedyny kod produkcyjny w tym pakiecie to `AssemblyMarker` i `Boot`.

## Etap A: projekt

### Struktura

```
global.json
Directory.Build.props
Directory.Packages.props
.editorconfig
Horyzont.sln
src/Horyzont.Core/Horyzont.Core.csproj                 (+ AssemblyMarker.cs)
src/Horyzont.Persistence/Horyzont.Persistence.csproj   (+ AssemblyMarker.cs)
src/Horyzont.Net/Horyzont.Net.csproj                   (+ AssemblyMarker.cs)
tests/Directory.Build.props
tests/Horyzont.Testing/Horyzont.Testing.csproj
tests/Horyzont.Core.Tests/Horyzont.Core.Tests.csproj
tests/Horyzont.Persistence.Tests/Horyzont.Persistence.Tests.csproj
tests/Horyzont.Net.Tests/Horyzont.Net.Tests.csproj
tests/Horyzont.Architecture.Tests/Horyzont.Architecture.Tests.csproj
.github/workflows/ci.yml
.github/workflows/nightly.yml
```

Rozwiązanie tworzysz komendą `dotnet new sln -n Horyzont --format sln`: SDK .NET 10 domyślnie tworzy `.slnx`, a CI, testy i dokumentacja zakładają `Horyzont.sln`.

`nuget.config` tworzy K1-01. Jeśli etap A kończy się przed K1-01, utwórz ten plik dokładnie według planu K1-01.

### `global.json`

```json
{ "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }
```

### `Directory.Build.props`

```xml
<Project>
  <PropertyGroup>
    <HoryzontTargetFramework>net10.0</HoryzontTargetFramework>
    <TargetFramework>$(HoryzontTargetFramework)</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <Deterministic>true</Deterministic>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

Jeśli test dymny K1-01 wymusi `net8.0`, zmieniasz tylko `HoryzontTargetFramework`.

### `tests/Directory.Build.props`

```xml
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
  <PropertyGroup>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
</Project>
```

### `Directory.Packages.props`

Centralne wersje (`ManagePackageVersionsCentrally` i `CentralPackageTransitivePinningEnabled` na `true`). Wersje przypinasz na najnowsze stabilne w dniu startu i wpisujesz je do PR.

| Paczka | Używa | Po co |
| --- | --- | --- |
| `Microsoft.Extensions.Logging.Abstractions` | Core, Persistence, Net | `ILogger<T>`, `[LoggerMessage]` |
| `System.IO.Hashing` | Core | `XxHash64` (skróty stanu, sumy kontrolne), `Crc32` |
| `Microsoft.Data.Sqlite` | Persistence | SQLite |
| `LiteNetLib` | Net | transport UDP |
| `Steamworks.NET` | Net | transport Steam |
| `Microsoft.Extensions.TimeProvider.Testing` | Testing | `FakeTimeProvider` |
| `FsCheck` | Testing | generatory (K1-04) |
| `Microsoft.NET.Test.Sdk` | testy | uruchamianie testów |
| `xunit.v3`, `xunit.runner.visualstudio` | testy | xUnit v3 |
| `FsCheck.Xunit.v3` | testy | testy właściwości |
| `Microsoft.Extensions.Logging` | testy | logger w testach |

Jeśli `FsCheck.Xunit.v3` nie istnieje albo nie wspiera bieżącego xUnit v3, użyj xUnit v2 (`xunit`, `xunit.runner.visualstudio`) z `FsCheck.Xunit` i wpisz tę decyzję do `docs/ZASADY.md`, punkt 8. W etapie A projekty mają tylko te paczki, których potrzebują ich testy; resztę dodają pakiety K1-03 do K1-06, ale wpisy wersji wszystkich paczek z tabeli powstają już teraz.

### `.editorconfig`

Oprzyj się na domyślnym `.editorconfig` dla .NET (`dotnet new editorconfig`) i ustaw:

```ini
root = true

[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true
indent_style = space
indent_size = 4
trim_trailing_whitespace = true

[*.{json,yml,yaml,xml,csproj,props,targets}]
indent_size = 2

[*.cs]
csharp_style_namespace_declarations = file_scoped:error
csharp_prefer_braces = true:warning
dotnet_style_qualification_for_field = false:warning
dotnet_style_qualification_for_property = false:warning
dotnet_style_qualification_for_method = false:warning
csharp_style_var_when_type_is_apparent = true:suggestion
dotnet_diagnostic.IDE0005.severity = warning          # zbędne using
dotnet_diagnostic.CA2007.severity = none              # ConfigureAwait: aplikacja, nie biblioteka do NuGet
dotnet_diagnostic.CA1062.severity = none              # walidacja null: dublowałaby Nullable

# Stałe: PascalCase (reguła bardziej szczegółowa, wygrywa z regułą pól prywatnych)
dotnet_naming_rule.constants.symbols = constants
dotnet_naming_rule.constants.style = pascal_case
dotnet_naming_rule.constants.severity = warning
dotnet_naming_symbols.constants.applicable_kinds = field
dotnet_naming_symbols.constants.required_modifiers = const
dotnet_naming_style.pascal_case.capitalization = pascal_case

# Prywatne pola: _camelCase
dotnet_naming_rule.private_fields.symbols = private_fields
dotnet_naming_rule.private_fields.style = underscore_camel
dotnet_naming_rule.private_fields.severity = warning
dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private
dotnet_naming_style.underscore_camel.required_prefix = _
dotnet_naming_style.underscore_camel.capitalization = camel_case

[tests/**.cs]
dotnet_diagnostic.CA1707.severity = none              # podkreślenia w nazwach testów
```

Wyłączenie każdej innej reguły analizatora wymaga uzasadnienia w PR.

### Kod produkcyjny etapu A

```csharp
namespace Horyzont.Core;

/// <summary>Type used to locate the Horyzont.Core assembly (tests, reflection).</summary>
public static class AssemblyMarker
{
}
```

Analogiczne `AssemblyMarker` w `Horyzont.Persistence` i `Horyzont.Net`. Czas w kodzie gry to `TimeProvider` z biblioteki standardowej (`docs/ZASADY.md`, punkt 4), więc etap A nie definiuje własnego zegara; testy dostają `FakeTimeProvider` przez `Horyzont.Testing`.

### Zależności projektów

| Projekt | Referencje |
| --- | --- |
| Horyzont.Core | — |
| Horyzont.Persistence | Core |
| Horyzont.Net | Core |
| Horyzont.Testing | Core |
| Horyzont.Core.Tests | Core, Testing |
| Horyzont.Persistence.Tests | Persistence, Core, Testing |
| Horyzont.Net.Tests | Net, Core, Testing |
| Horyzont.Architecture.Tests | Core, Persistence, Net |

### CI (`.github/workflows/ci.yml`)

Macierz `windows-latest`, `ubuntu-latest`. Kroki: checkout, `actions/setup-dotnet` z `global-json-file: global.json`, `dotnet restore`, `dotnet build -c Release --no-restore`, `dotnet test -c Release --no-build --filter "Category!=Soak&Category!=Manual"`, `dotnet format --verify-no-changes`. Osobny workflow `nightly.yml` uruchamia co noc testy `Category=Soak`. Zmienną `HORYZONT_COMMIT` dopisuje do `env` pakiet K1-07.

## Etap A: testy akceptacyjne (pisze Sonnet 5.5)

`tests/Horyzont.Architecture.Tests/LayeringTests.cs`:

```csharp
using System.Xml.Linq;

namespace Horyzont.Architecture.Tests;

public sealed class LayeringTests
{
    public static TheoryData<Type> PureAssemblies => new()
    {
        typeof(Horyzont.Core.AssemblyMarker),
        typeof(Horyzont.Persistence.AssemblyMarker),
        typeof(Horyzont.Net.AssemblyMarker),
    };

    [Theory]
    [MemberData(nameof(PureAssemblies))]
    public void PureAssembly_DoesNotReferenceGodot(Type marker)
    {
        var references = marker.Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "");
        Assert.DoesNotContain(references, n => n.StartsWith("Godot", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("src/Horyzont.Core/Horyzont.Core.csproj", "")]
    [InlineData("src/Horyzont.Persistence/Horyzont.Persistence.csproj", "Horyzont.Core")]
    [InlineData("src/Horyzont.Net/Horyzont.Net.csproj", "Horyzont.Core")]
    public void ProjectFile_HasOnlyAllowedReferences(string path, string allowedCsv)
    {
        var allowedProjects = allowedCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var project = XDocument.Load(Path.Combine(RepoRoot(), path));
        var projectRefs = project.Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value));
        var packageRefs = project.Descendants("PackageReference")
            .Select(e => e.Attribute("Include")!.Value);

        Assert.All(projectRefs, r => Assert.Contains(r, allowedProjects));
        Assert.DoesNotContain(packageRefs, p => p.StartsWith("Godot", StringComparison.OrdinalIgnoreCase));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Horyzont.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
```

W `Horyzont.Core.Tests`, `Horyzont.Persistence.Tests` i `Horyzont.Net.Tests` po jednym teście `WiringTests`: zestaw testowanej biblioteki ładuje się (nazwa z `AssemblyMarker`), a `FakeTimeProvider` z `Horyzont.Testing` po `Advance(TimeSpan.FromSeconds(1))` zwraca `GetElapsedTime` równe 1 s. Dzięki temu każdy projekt testów ma od początku co najmniej jeden test, a CI sprawdza podłączenie paczek.

## Etap B: projekt Godota (po K1-01)

```
game/project.godot
game/Horyzont.Game.csproj
game/Horyzont.Game.sln          (tworzy go edytor Godota; commitujesz)
game/scenes/Boot.tscn
game/src/Boot/Boot.cs
game/src/Server/.gitkeep
game/src/Client/.gitkeep
game/src/Shared/.gitkeep
game/assets/.gitkeep
```

`game/Horyzont.Game.csproj`:

```xml
<Project Sdk="Godot.NET.Sdk/4.7.2">
  <PropertyGroup>
    <TargetFramework>$(HoryzontTargetFramework)</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <RootNamespace>Horyzont.Game</RootNamespace>
    <AssemblyName>Horyzont.Game</AssemblyName>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <GodotFloat64>true</GodotFloat64>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../src/Horyzont.Core/Horyzont.Core.csproj" />
    <ProjectReference Include="../src/Horyzont.Persistence/Horyzont.Persistence.csproj" />
    <ProjectReference Include="../src/Horyzont.Net/Horyzont.Net.csproj" />
  </ItemGroup>
</Project>
```

`GodotFloat64` włącza `GODOT_REAL_T_IS_DOUBLE` w kodzie gry; bez tej właściwości Godot.NET.Sdk zakłada pojedynczą precyzję. GodotSharp dochodzi z Godot.NET.Sdk jako referencja z `IsImplicitlyDefined`, więc centralne wersje paczek jej nie blokują.

`project.godot`: nazwa „Horyzont”, główna scena `res://scenes/Boot.tscn`, renderer Forward+, `rendering/rendering_device/driver.windows="vulkan"` (silnik z K1-01 nie ma D3D12), `physics/common/physics_ticks_per_second=60`, `dotnet/project/assembly_name="Horyzont.Game"`.

`game/` nie wchodzi do `Horyzont.sln`: runnery macierzy CI nie mają paczek silnika, więc restore projektu gry by się tam nie udał. Edytor Godota tworzy własny `game/Horyzont.Game.sln` z samym projektem gry; commitujesz go, a referencje do bibliotek z `src/` buduje on przez `ProjectReference`.

`Boot.cs`: czyta argumenty po `--` (`OS.GetCmdlineUserArgs()`). Z `--smoke` wypisuje `BOOT OK`, wersję silnika, `real_t`, wersję .NET i kończy kodem 0. Bez argumentów wypisuje listę trybów (`--server`, `--host`, `--connect <adres>`) jako „do zrobienia w K2” i kończy działanie. Tryby implementuje pakiet 10.

CI: job `game` w `ci.yml` (Windows) pobiera ostatnie udane artefakty `engine-windows-editor` i `engine-nupkgs` z workflow `engine.yml` na `main` (`gh run download`), aktywuje paczki (`python tools/engine/build.py activate --from <katalog z engine-nupkgs>`), wykonuje `dotnet build game/Horyzont.Game.csproj` i `dotnet format game/Horyzont.Game.csproj --verify-no-changes`, a na końcu uruchamia `<edytor> --headless --path game -- --smoke`.

## Etap B: testy akceptacyjne

1. `game/` otwiera się w edytorze z `engine/out/<klucz>/`, buduje C# i uruchamia scenę `Boot`.
2. `<edytor> --headless --path game -- --smoke` wypisuje `BOOT OK` z `real_t` równym `double` i kończy się kodem 0.
3. Job `game` w CI jest zielony.

## Kroki

- [ ] Etap A: `global.json`, `Directory.Build.props`, `tests/Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`.
- [ ] Etap A: projekty bibliotek z `AssemblyMarker`; `Horyzont.Testing` z paczką `FakeTimeProvider`.
- [ ] Etap A: projekty testów z testami od Sonnet 5.5; `Horyzont.sln` (format `.sln`) ze wszystkimi projektami poza `game/`.
- [ ] Etap A: `ci.yml` i `nightly.yml`; zielone CI na obu systemach.
- [ ] Etap A: sprawdzenia ręczne (niżej) opisane w PR.
- [ ] Etap B: `game/` z `Boot`; test akceptacyjny 1 i 2.
- [ ] Etap B: job `game` w CI.

## Odbiór

Etap A: CI zielone na Windows i Linux, test warstw przechodzi, a w PR opisane dwa sprawdzenia ręczne (bez commitowania):

- `<ItemGroup Condition="false"><PackageReference Include="GodotSharp" /></ItemGroup>` dodane do `Horyzont.Core.csproj` wywraca test warstw. MSBuild pomija tę grupę, więc restore przechodzi, a test czytający XML ją widzi.
- Nieużywana zmienna w Core wywraca build.

Etap B: testy akceptacyjne 1–3.

## Pułapki

- `Directory.Build.props` z korzenia obejmuje też `game/`. Projekt Godota musi mieć `TargetFramework` w pliku projektu, bo SDK Godota może nadpisać wartość z `Directory.Build.props`.
- `engine/smoke/` ma własne `Directory.Build.props` i `Directory.Packages.props`; nie usuwaj ich.
- `ImplicitUsings` włączone: nie dodawaj `using System;` i podobnych, bo IDE0005 wywróci build.
- Bez `--format sln` powstanie `Horyzont.slnx`, a `RepoRoot()` w teście warstw nie znajdzie korzenia.
