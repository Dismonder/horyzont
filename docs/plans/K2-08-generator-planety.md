# K2-08 · Planeta: generator i parametry

| | |
| --- | --- |
| Prowadzi | Sonnet 5.5 (Claude Code) |
| Testy akceptacyjne | Sonnet 5.5 |
| Przegląd | Opus 5.5 przed scaleniem, bo pakiet zmienia kontrakty (nowy plik w `src/Horyzont.Core/`, `engine/engine.json`, `Directory.Packages.props`, nowy wiersz w ZASADY pkt 3); ZASADY pkt 9 |
| Zależy od | K1-01, K1-02 (etapy A i B) |
| Gałąź | etap A `pakiet/k2-08a-silnik-linux`, etap B `pakiet/k2-08-generator` |

**Cel:** powtarzalna kulista planeta o promieniu 4 km z generatora `VoxelGeneratorGraph` (szum wysokości, jaskinie, pięć materiałów, nienaruszalny rdzeń), dająca bitowo te same voxele LOD 0 na Windows i Linux, oraz fabryka skonfigurowanego `VoxelLodTerrain` (Transvoxel, Clipbox, bloki 16³), układ kanałów voxeli, mapowanie materiałów i uprząż testów silnika bez grafiki, na których budują K2-09, K2-11 i K2-13.

## Kontekst

- `docs/ZASADY.md`, punkty 2, 3, 4, 5, 7, 8
- `docs/spec/01-planeta.md`: Model świata (tabela identyfikatorów, tabela „Parametr planety”, lista „Teren”), Architektura („Budowanie silnika”), Sieć (Protokół: skrót builda, „inny generator dałby inny teren”; Sesja krok 2–3; Edycje terenu: kontrola determinizmu), Zapis i odtwarzanie (tabele `meta`, `planets`, `materials`), Testy (wiersze Determinizm, Integracyjne: gdUnit4Net, Wydajność), Ryzyka („Różnice generacji między platformami”, „Moduł w kompilacji z podwójną precyzją”, „Voxel Tools prowadzi głównie jedna osoba”)
- `docs/plan/plan-projektu.md`, tabela K2 (wiersz K2-08: „powtarzalna planeta 4 km; raport zgodności Windows–Linux”)
- `docs/plans/K1-01-pipeline-silnika.md` (cele w `engine.json`, artefakty `engine.yml`, `build.py activate`) i `docs/plans/K1-02-szkielet-rozwiazania.md` (etap B: `game/`, job `game` w CI)
- plany sąsiednie: `K2-09-adapter-voxel-tools.md` (sekcja „Kontrakty pobierane z K2-08”), `K2-13-narzedzia.md` (`MaterialPalette`, wykluczenie materiału rdzenia, test `SoloDigAndFill_RealTerrain`)
- przegląd bramki K1: `AgentTools/logs/results/horyzont-k1-review.md`, „Lekcje do zakodowania w planach K2” (E1–E9 nie dotyczą tego pakietu: E1–E2 robi K2-09, E3–E9 K2-10)

Kod, na którym budujesz (main 884d40d + K1-02B):

- `src/Horyzont.Core/World/PlanetParams.cs` — `PlanetParams(Id, System, Seed, GeneratorProfile, RadiusMeters = 4000, VoxelSizeMeters = 1, Gravity = 9.81, AtmosphereHeightMeters = 1000, CoreDepthMeters = 400, DayLengthSeconds = 1800)`, wewnętrzne `Validate()` (profil 1..64 bajtów UTF-8, `CoreDepthMeters < RadiusMeters`)
- `src/Horyzont.Core/World/PlanetRegistry.cs` — planety w kolejności `PlanetId`; `StateHasher.cs:39-40` już haszuje planety
- `src/Horyzont.Core/World/Blocks/MaterialRegistry.cs` — klucz ↔ numer 1..255, 0 = powietrze; `WorldState.CreateNew(id, galaxySeed, planets, blockTypeKeys, materialKeys)` nadaje numery materiałów od 1 w podanej kolejności (`WorldState.cs:95-133`)
- `src/Horyzont.Core/BuildInfo.cs` — `ComputeBuildHash(string generatorProfileVersion)`: skrót builda w uścisku dłoni już zależy od wersji generatora
- `src/Horyzont.Core/Simulation/IntentValidator.cs:128-129` — zakaz edycji poniżej `RadiusMeters − CoreDepthMeters` (rdzeń)
- `src/Horyzont.Core/World/Terrain/TerrainMath.cs` — `BlockSize = 16`
- konwencja kluczy: `BlockTypeRegistry.MissingKey = "core:missing"`, profil w testach `"core:test/1"`
- `F:/dev/horyzont-k1-02b/game` (gałąź `pakiet/k1-02b-godot`, scalana przed K2): `Horyzont.Game.csproj` (Godot.NET.Sdk 4.7.2, `GodotFloat64`, `net10.0`, referencje do Core, Persistence, Net), `src/Boot/Boot.cs` (`--smoke` → `BOOT OK ... real_t=double`), puste `src/Client`, `src/Server`, `src/Shared`
- `.github/workflows/ci.yml` — job `game` tylko na `windows-latest`: pobiera `engine-windows-editor` i `engine-nupkgs` z ostatniego udanego `engine.yml` na `main`, `build.py activate`, build, format, smoke
- `.github/workflows/engine.yml` — cele z `engine.json`; artefakty: `engine-windows-editor`, `engine-nupkgs`, szablony Windows debug/release, `engine-linux-template` (tylko `template_release`); uruchamia się przy zmianie `engine/**`, `nuget.config`, `Directory.*.props`
- `engine/engine.json` — Godot `4.7.2-stable`, Voxel Tools `v1.7`, `precision=double`; brak edytora Linux
- `nuget.config` — mapowanie źródeł: `Godot.*` i `GodotSharp*` wyłącznie z `engine/nuget-local`

## Warunki startu

Sprawdź na `main` (`git log` i kod, nie pamięć):

1. K1-02B scalony: `game/` na `main`, job `game` w CI zielony.
2. Ostatni `engine.yml` na `main` zielony (artefakty do pobrania istnieją).

Brak → zatrzymaj się i eskaluj (ZASADY pkt 1). Pakiet nie zależy od poprawek bramki K1 (PR #9: B1–B3), bo nie dotyka Persistence, sesji ani appliera.

## Zakres

- **Etap A (silnik):** cel `editor-linux` w `engine/engine.json`, artefakt `engine-linux-editor` w `engine.yml`. Bez niego nie da się uruchomić testów silnika ani raportu na Linuksie, a K2-09 wymaga uprzęży na obu systemach.
- **Core (jeden nowy plik):** `PlanetGenesis` — wyprowadzanie `PlanetId` i ziarna planety z `GalaxySeed` i indeksu planety, planeta domowa z wartościami domyślnymi spec.
- **Gra (`game/src/Planet/`):** profile generatora z wersją i generator `core:planet/1` na `VoxelGeneratorGraph`, kanały voxeli, tabela materiałów terenu i mapowanie `MaterialId` ↔ indeks tekstury, układ LOD i granic, fabryka `VoxelLodTerrain`, zastępczy materiał renderu, tryby deweloperskie `--generator-report` i `--planet-preview`.
- **Uprząż testów silnika:** gdUnit4Net w projekcie gry, job `game` jako macierz Windows i Linux, pomocniki dla testów K2-09 … K2-13.
- **Cykl dnia i nocy (decyzja bramki D12):** czysta funkcja `DayNightCycle.SunDirection` (bez zmian w Core, bez stanu) z testem i węzeł sceny `SunLight`; wpięcie w scenę klienta i zasilanie tickiem serwera robi K2-11.
- **Raport zgodności Windows–Linux:** raport z każdego systemu, narzędzie porównujące, job CI, raport w repozytorium.
- Nowy wiersz w ZASADY pkt 3 (zamrożenie profili generatora i tabeli materiałów).

Poza zakresem:

- `ITerrainService` nad Voxel Tools, nasz strumień (`TerrainStream`), wersje bloków, kodek bloku, edycje i zapis materiału przy `Add` (K2-09; ten pakiet daje mu mapowanie i kanały);
- tworzenie i wczytywanie świata w trybach solo, host, serwer, umieszczenie terenu w `/Root/Server` i `/Root/Client`, wybór ziarna galaktyki przez gracza, eksport serwera dedykowanego (K2-10);
- widzowie graczy, kolizje postaci, kamera deweloperska do gry, wpięcie `SunLight` w scenę klienta z tickiem serwera (K2-11; tryb `--planet-preview` to tylko narzędzie do oceny generatora);
- sumy kontrolne bloków w sieci (K3-16), benchmarki BenchmarkDotNet generacji i meshingu (K4-19), docelowe tekstury i kierunek artystyczny (K8-50), grawitacja (stała w `PlanetParams`, używa K2-11).

## Projekt

### Pliki

```
src/Horyzont.Core/World/PlanetGenesis.cs        identyfikator, ziarno i parametry nowej planety

game/src/Planet/
  PlanetTerrainFactory.cs        PlanetParams + opcje → skonfigurowany VoxelLodTerrain (bez strumienia)
  PlanetTerrainOptions.cs        readonly record struct: kolizje, render
  PlanetTerrainLayout.cs         granice voxeli, liczba LOD, rozmiary bloków
  TerrainChannels.cs             układ kanałów voxeli (const)
  TerrainMaterials.cs            tabela materiałów terenu: klucz ↔ indeks tekstury (tylko dopisywana)
  TerrainMaterialMap.cs          MaterialId świata ↔ indeks tekstury, budowana z MaterialRegistry
  Generators/
    GeneratorProfiles.cs         znane profile, profil bieżący, składnik skrótu builda
    PlanetGeneratorV1.cs         graf profilu core:planet/1 (stałe zamrożone)
    GraphBuilder.cs              opakowanie API VoxelGraphFunction z listą dozwolonych węzłów
    NoiseSeeds.cs                ziarna szumów z PlanetParams.Seed
  Rendering/
    PlanetTerrainMaterial.cs     ShaderMaterial + Texture2DArray zastępczych tekstur
  Diagnostics/
    GeneratorSampler.cs          deterministyczny zbiór próbek bloków
    GeneratorDigest.cs           XxHash64 kanałów bloku
    GeneratorReport.cs           tryb --generator-report
  Dev/
    PlanetPreview.cs             tryb --planet-preview (zrzuty ekranu)
  Sky/
    DayNightCycle.cs             czysta funkcja SunDirection(tick, tickRate, planet) (D12)
    SunLight.cs                  DirectionalLight3D ustawiany z SunDirection (węzeł sceny)
game/src/Boot/DevModes.cs        rozpoznanie trybów deweloperskich; Boot woła je przed resztą
game/shaders/planet_terrain.gdshader
game/tests/
  Support/EngineTestScene.cs     tymczasowy węzeł w drzewie, sprzątanie, WaitFrames
  Harness/HarnessTests.cs
  Planet/*.cs                    testy z tabeli „Testy silnika”
game/.runsettings                GODOT_BIN z env, limit czasu
game/export_presets.cfg          presety windows-release, linux-release (K2-10 dopisuje serwer)
tools/generator/compare_reports.py
tests/Horyzont.Core.Tests/World/PlanetGenesisTests.cs
docs/reports/K2-08-zgodnosc-generatora.md       raport (wynik CI)
```

### Rozpoznanie (krok 2, przed kodem produkcyjnym)

Nazwy API Voxel Tools v1.7 w C# (glue generowane z modułu) zgadujemy tylko do pierwszego spike'u. Wyniki zapisz w „Odstępstwach” i użyj w kodzie:

- **S1** — tworzenie grafu w kodzie: `VoxelGeneratorGraph.GetMainFunction()`, `CreateNode(typeId, position)`, `AddConnection`, `SetNodeParam`, `SetNodeDefaultInput`, `Compile()` (zwraca słownik z błędami); identyfikatory typów węzłów i indeksy parametrów dla: wejścia X/Y/Z, `Constant`, `Add`, `Subtract`, `Multiply`, `Divide`, `Min`, `Max`, `Clamp`, `Abs`, `Sqrt`, `Distance`/`Length`, `Normalize`, `Select`, `Remap`, `Smoothstep`, `FastNoise3D` (zasób `ZN_FastNoiseLite`), `OutputSDF`, `OutputSingleTexture`.
- **S2** — tryb tekstur Transvoxel z jednym indeksem na voxel (oczekiwane `TEXTURES_SINGLE_S4`): kanał (oczekiwany `CHANNEL_INDICES`), głębia, maksymalna liczba indeksów, sposób przekazania indeksu do shadera.
- **S3** — konfiguracja głębi kanałów (zasób formatu voxeli przypisywany terenowi i generatorowi), rozmiar bloku danych `VoxelLodTerrain` (stały 16?), `mesh_block_size`, `voxel_bounds`, `lod_count`, `lod_distance`, `streaming_system = Clipbox`, `full_load_mode_enabled`, `generator_use_gpu`, ustawienia normalmap.
- **S4** — synchroniczne `VoxelGenerator.GenerateBlock(VoxelBuffer, origin, lod)` i `GenerateSingle(pos, channel)` z C#; odczyt surowych bajtów kanału (`GetChannelAsByteArray` lub odpowiednik) i zachowanie dla kanału jednorodnego (kompresja do jednej wartości).
- **S5** — ustawienia kompilacji grafu wpływające na bajty: `sdf_clip_threshold`, `use_subdivision`, `subdivision_size`, `use_optimized_execution_map`, `use_xz_caching`. Każde ustawiamy jawnie.
- **S6** — gdUnit4Net z naszym silnikiem: `gdUnit4.api`, `gdUnit4.test.adapter`, `gdUnit4.analyzers` (najnowsze stabilne zgodne z Godot 4.7) przy mapowaniu `GodotSharp*` → `engine/nuget-local`; uruchomienie `dotnet test` z `GODOT_BIN` na edytorze `double` Windows i Linux bez grafiki.
- **S7** — eksport release Windows i Linux z edytora Windows z szablonami z `engine.yml` (`custom_template` w presecie) i uruchomienie wyeksportowanej gry z `--headless -- --generator-report`.

Gdy S6 nie działa (np. konflikt wersji GodotSharp, którego nie rozwiązuje mapowanie źródeł): własny runner `-- --engine-tests <filtr>` w `DevModes` (odkrywa metody `[EngineTest]` refleksją, kod wyjścia = liczba porażek, wynik w formacie TRX albo JUnit do artefaktu), z tym samym układem `game/tests/`. To jest nazwa, której oczekuje K2-09 jako zapasowej. Wybór zapisz w „Odstępstwach”.

Gdy S2 pokaże, że jeden indeks na voxel nie działa w v1.7: zatrzymaj się i eskaluj do Opus 5.5 (spec wymaga „jeden indeks materiału na voxel”).

### Identyfikatory i parametry planety (Core)

```csharp
namespace Horyzont.Core.World;

/// Derivation of planet identity and parameters from the galaxy seed. Results are stored when a world is
/// created and never re-derived for an existing world, but the function is frozen (golden test values),
/// because subproject 2 derives further planets of the same galaxy from the seed.
public static class PlanetGenesis
{
    public const uint HomePlanetIndex = 0;
    public static PlanetId DerivePlanetId(ulong galaxySeed, uint planetIndex);   // XxHash64(LE galaxySeed ‖ LE planetIndex, seed: 1); 0 → 1
    public static ulong DerivePlanetSeed(ulong galaxySeed, uint planetIndex);    // XxHash64(to samo, seed: 2)
    public static PlanetParams CreatePlanet(ulong galaxySeed, uint planetIndex, string generatorProfile); // System = new SystemId(1), reszta: wartości domyślne PlanetParams
}
```

- Plik w `src/Horyzont.Core/World/` (ścieżka chroniona, przegląd Opus 5.5), ale nie zmienia żadnego formatu: `PlanetParams` i jego blob zostają bez zmian, więc żadnej wersji nie podbijasz. Opisz to w PR.
- Lekcja 2: pakiet nie dodaje pól trwałego stanu. Profil generatora, ziarno i parametry planety już są w `PlanetParams`, zapisie i skrócie stanu (`StateHasher.cs:39-40`). Generator jest funkcją `PlanetParams`; nic z generatora nie trafia do `WorldState`.
- `CreatePlanet` rzuca `ArgumentException` dla pustego profilu, a potem woła wewnętrzne `PlanetParams.Validate()` (ten sam zestaw), więc zwraca tylko planety, które przyjmie `WorldState.CreateNew`.

### Profile generatora (lekcja 1)

Teren nieedytowany nie jest zapisywany: klient i serwer liczą go z generatora (spec: Sieć, „Teren niezmieniony: klient generuje go sam z ziarna”). Każda zmiana wyniku generatora przesuwa więc teren pod istniejącymi edycjami i budowlami starych światów. Dlatego wynik generatora jest formatem:

```csharp
namespace Horyzont.Game.Planet.Generators;

public static class GeneratorProfiles
{
    public const string PlanetV1 = "core:planet/1";
    public const string Current = PlanetV1;                       // profil nowych światów
    public static string BuildHashComponent { get; }              // znane profile posortowane ordinalnie, złączone ';' (dziś "core:planet/1")
    public static bool IsSupported(string profile);
    public static VoxelGeneratorGraph CreateGenerator(PlanetParams planet);   // nieznany profil → NotSupportedException z kluczem
}
```

- Profil raz scalony do `main` nigdy nie zmienia wyniku. Zmiana generatora = nowy profil (`core:planet/2`) w nowej klasie obok starego; stare światy dalej używają swojego profilu z `PlanetParams.GeneratorProfile`. Pilnuje tego złoty skrót każdego profilu (test `GeneratorV1_GoldenDigest`).
- `BuildHashComponent` trafia do `BuildInfo.ComputeBuildHash` (składa K2-10), więc klient i serwer z różnym zestawem profili się nie połączą.
- Każde wejście grafu pochodzi z `PlanetParams` (`Seed`, `RadiusMeters`, `CoreDepthMeters`) albo ze stałych `const` w `PlanetGeneratorV1`. `PlanetParams.VoxelSizeMeters != 1` → `NotSupportedException` (zgodnie z K2-09 `AddPlanet`).
- Nowy wiersz w ZASADY pkt 3: `game/src/Planet/Generators/`, `game/src/Planet/TerrainChannels.cs`, `game/src/Planet/TerrainMaterials.cs` → „istniejący profil daje te same bajty (złoty skrót bez zmian); zmiana generatora = nowy profil obok starego; tabela materiałów i kanały tylko dopisywane”. Do wiersza `engine/engine.json` dopisz: „zmiana `voxel.ref` wymaga niezmienionych złotych skrótów profili generatora; inaczej nowe profile dla nowych światów i decyzja właściciela o starych”. K2-09 dopisuje swój wiersz o `TerrainEditSemantics`.

### Generator `core:planet/1`

Oznaczenia: `p` — pozycja voxela w ramie planety (m), `r = |p|`, `d = p / r`, `R = RadiusMeters`, `C = CoreDepthMeters`. Znak SDF jak w Voxel Tools: ujemny w materii.

```
h      = clamp(continent(d·R)·A_c + detail(d·R)·A_d + ridges(d·R)·A_m·clamp(continent(d·R), 0, 1), −MaxRelief, +MaxRelief)
ground = r − (R + h)
cave   = (CaveWidth − |caves(p)|) · CaveScale − (1 − caveMask(r, h)) · 1000     // > 0 w jaskini
sdf    = min(max(ground, cave), r − (R − C))                                      // rdzeń zawsze pełny
depth  = R + h − r
mat    = r ≤ R − C + 0.5            → core
         depth < SandDepth ∧ h < SandBelowHeight → sand
         depth < DirtDepth           → dirt
         ore(p) > OreThreshold       → ore
         inaczej                     → stone
```

`caveMask` = 1 dla `depth ≥ CaveMinDepth` i `r ≥ R − C + CaveCoreMargin`, 0 poza tym (przejście `Smoothstep` na 4 m). Wartości startowe (`const` w `PlanetGeneratorV1`, strojenie w kroku 9 przed zamrożeniem):

| Stała | Start | Uwagi |
| --- | --- | --- |
| `MaxReliefMeters` | 300 | jawny `Clamp`: dolina nigdy nie sięga rdzenia (C = 400), szczyt daleko pod atmosferą (1000) |
| continent: OpenSimplex2, FBm 4 oktawy, częstotliwość 1/1200, `A_c` | 120 m | |
| detail: OpenSimplex2, FBm 3 oktawy, 1/150, `A_d` | 25 m | |
| ridges: OpenSimplex2, Ridged 4 oktawy, 1/600, `A_m` | 150 m | tylko na kontynentach |
| caves: OpenSimplex2, 1 oktawa, 1/48; `CaveWidth`, `CaveScale` | 0,08; 24 | tunele z `|n| < CaveWidth` |
| `CaveMinDepth`, `CaveCoreMargin` | 6 m; 20 m | wejścia do jaskiń możliwe, rdzeń nienaruszony |
| ore: OpenSimplex2, 1/16, `OreThreshold` | 0,72 | tylko w kamieniu |
| `DirtDepth`, `SandDepth`, `SandBelowHeight` | 3 m; 4 m; −60 m | piasek w nizinach |
| `sdf_clip_threshold`, `use_subdivision`, `subdivision_size`, `use_optimized_execution_map`, `use_xz_caching` | jawne wartości według S5 | wpływają na bajty, więc zamrożone w profilu |

Determinizm (spec: „wszystko, co ma kolizje, generuje CPU skalarnym szumem”):

- Szum wyłącznie przez węzły z zasobem `ZN_FastNoiseLite` (skalarny); żadnych węzłów FastNoise2 (SIMD, wynik zależny od zestawu instrukcji) ani węzłów z funkcjami trygonometrycznymi, `Pow`, `Exp`, `Log` (różne biblioteki matematyczne MSVC i glibc). Dozwolone operacje: `+ − × ÷`, `Min`, `Max`, `Clamp`, `Abs`, `Sqrt`/`Length`/`Normalize`/`Distance` (pierwiastek IEEE jest dokładny), `Select`, `Remap`, `Smoothstep` (wielomian), stałe, wejścia, wyjścia. Bez domeny warp typu z obrotem trygonometrycznym.
- `GraphBuilder` przyjmuje tylko węzły z listy dozwolonych (`AllowedNodeTypes`), a test `GeneratorV1_GraphUsesOnlyAllowedNodes` przegląda skompilowany graf (`GetNodeIds`, typ każdego węzła).
- Ziarna szumów: `NoiseSeeds.For(ulong planetSeed, int slot)` = młodsze 32 bity `XxHash64(LE planetSeed ‖ LE slot)`; sloty `const` (continent 1, detail 2, ridges 3, caves 4, ore 5).
- Generacja GPU wyłączona wszędzie (`generator_use_gpu = false`). Spec dopuszcza GPU dla wizualnych detali dalekich LOD-ów; w tym pakiecie wyłączone (Pytanie do właściciela 3).
- `Compile()` z błędami → `InvalidOperationException` z treścią błędów; graf po kompilacji nie jest już zmieniany (generator czytają wątki robocze Voxel Tools).

### Kanały i materiały (dla K2-09 i K2-13)

```csharp
public static class TerrainChannels   // nazwy enumów i typu formatu według S2/S3
{
    public const VoxelBuffer.ChannelId Sdf = VoxelBuffer.ChannelId.ChannelSdf;          // głębia 16 bitów
    public const VoxelBuffer.ChannelId Material = VoxelBuffer.ChannelId.ChannelIndices; // według S2; głębia według S2 (oczekiwane 8 bitów)
    public const int UsedChannelsMask = (1 << (int)Sdf) | (1 << (int)Material);         // dla TerrainStream._GetUsedChannelsMask (K2-09)
    public static VoxelFormat CreateFormat();   // zasób głębi kanałów przypisywany terenowi (nazwa typu według S3)
}

public static class TerrainMaterials
{
    public const string StoneKey = "core:stone";      // indeks tekstury 0
    public const string DirtKey = "core:dirt";        // 1
    public const string SandKey = "core:sand";        // 2
    public const string OreKey = "core:ore";          // 3
    public const string CoreMaterialKey = "core:bedrock"; // 4, materiał rdzenia (wykluczony z palety K2-13)
    public static IReadOnlyList<(string Key, byte TextureIndex)> All { get; } // kolejność = kolejność rejestracji w nowym świecie
    public static IReadOnlyList<string> Keys { get; }                          // dla WorldState.CreateNew (K2-10)
}

public sealed class TerrainMaterialMap
{
    public static TerrainMaterialMap Build(MaterialRegistry registry);  // klucz spoza TerrainMaterials → InvalidDataException z kluczem
    public byte ToTextureIndex(byte materialId);                        // materialId nieznany w rejestrze → ArgumentOutOfRangeException
    public bool TryGetMaterialId(byte textureIndex, out byte materialId);
}
```

- Voxel przechowuje indeks tekstury Voxel Tools, nie `MaterialId` świata: generator nie zna rejestru, a bajty bloku nie mogą zależeć od kolejności rejestracji. Numery `MaterialRegistry` są własnością świata i mogą się różnić między światami; mapowanie idzie zawsze przez klucz.
- Tabela `TerrainMaterials` jest tylko dopisywana, indeksy nigdy się nie zmieniają (dane bloków w bazach zawierają indeksy). Limit indeksów według S2; do tego czasu najwyżej 16.
- Nieznany klucz materiału w rejestrze świata (zapis z nowszej wersji gry) → świat się nie wczytuje z czytelnym komunikatem; K2-10 woła `Build` przy starcie.
- K2-09 zapisuje materiał przy edycji `Add` przez `ToTextureIndex(op.MaterialId)`; K2-13 czyta materiał voxela w teście przez `TryGetMaterialId`. Plan K2-13 (wiersz „Poza zakresem”) przypisuje samo mapowanie K2-09; obowiązuje podział z K2-09: mapowanie daje K2-08, stosuje K2-09.

### Fabryka terenu i układ LOD

```csharp
public readonly record struct PlanetTerrainOptions(bool Collisions, bool Render)
{
    public static PlanetTerrainOptions Server => new(Collisions: true, Render: false);
    public static PlanetTerrainOptions Client => new(Collisions: true, Render: true);   // także host: jedna instancja (K2-09)
    public static PlanetTerrainOptions Preview => new(Collisions: false, Render: true);
}

public static class PlanetTerrainLayout
{
    public const int DataBlockSize = 16;                 // == TerrainMath.BlockSize (test)
    public const int MeshBlockSize = 16;                 // tylko wizualne; strojenie w K4-19
    public static int HalfExtentMeters(PlanetParams p);  // ceil16(R + MaxReliefMeters + 64)
    public static int LodCount(PlanetParams p);          // 1 + ceil(log2(2·HalfExtent / 16)); R = 4000 → 11
}

public static class PlanetTerrainFactory
{
    public static VoxelLodTerrain Create(PlanetParams planet, PlanetTerrainOptions options);
}
```

`Create` zwraca nowy węzeł poza drzewem, bez strumienia, w środku planety z jednostkową bazą (planety się nie obracają). Ustawia jawnie, bez polegania na wartościach domyślnych modułu:

- generator z `GeneratorProfiles.CreateGenerator(planet)`, mesher `VoxelMesherTransvoxel` w trybie tekstur z S2, format kanałów `TerrainChannels.CreateFormat()`;
- `streaming_system = Clipbox`, `full_load_mode_enabled = false`, `generator_use_gpu = false`, normalmap wyłączone;
- `voxel_bounds` = sześcian `[−H, H)` z `HalfExtentMeters`, `lod_count = LodCount`, `mesh_block_size = MeshBlockSize`, `lod_distance` według pomiaru w kroku 9 (wartość w „Odstępstwach”);
- `Collisions`: generowanie kolizji tylko dla LOD 0 (`collision_lod_count = 1`); `Render`: materiał z `PlanetTerrainMaterial.Create()`, bez `Render` brak materiału i cieni;
- nazwa węzła `Planet_{Id:X16}`.

Spec mówi „około 10” LOD-ów; wzór daje 11 przy R = 4000 i rzeźbie 300 m, bo najdalszy blok musi objąć całą średnicę (8736 m > 8192 m). To zgodne ze spec; test sprawdza wzór, a nie liczbę 10.

Kolejność dla K2-09: `AddPlanet` przypina `TerrainStream` przed dodaniem węzła do drzewa (Voxel Tools zaczyna strumieniowanie po wejściu do drzewa). Bez strumienia (`--planet-preview`, testy generatora) teren działa na samym generatorze.

### Materiał renderu

`PlanetTerrainMaterial.Create()` buduje `ShaderMaterial` z `game/shaders/planet_terrain.gdshader` (triplanarne próbkowanie `Texture2DArray`, indeks z atrybutu wierzchołka według S2) i tablicę 5 tekstur zastępczych 64×64 generowanych w kodzie z ustalonych kolorów (bez plików graficznych). Materiał jest wspólny dla wszystkich planet procesu (tworzony raz przez wywołującego i podawany dalej, nie jako statyczny singleton). Docelowe tekstury: K8-50.

### Uprząż testów silnika (dla K2-09 … K2-13)

- Paczki `gdUnit4.api`, `gdUnit4.test.adapter`, `gdUnit4.analyzers`, `Microsoft.NET.Test.Sdk` w `Directory.Packages.props` (ścieżka chroniona: opis w PR i zielony test dymny silnika); referencje w `Horyzont.Game.csproj` tylko poza konfiguracjami eksportu, a `<Compile Remove="tests/**" Condition="'$(Configuration)' == 'ExportDebug' Or '$(Configuration)' == 'ExportRelease'" />`, żeby testy nie trafiły do buildów gry.
- Testy w `game/tests/<Obszar>/`, przestrzeń nazw `Horyzont.Game.Tests.<Obszar>`, klasy `[TestSuite]`, metody `[TestCase]`, wymagające silnika z `[RequireGodotRuntime]`; nazwy `Metoda_Scenariusz_OczekiwanyWynik` (ZASADY pkt 8).
- Komenda: `dotnet test game/Horyzont.Game.csproj --settings game/.runsettings` ze zmienną `GODOT_BIN` (edytor `double` .NET; w CI z artefaktu, lokalnie z `engine/out/...`).
- `EngineTestScene` (`game/tests/Support/`): `Add(Node)` w tymczasowym węźle pod `SceneTree.Root`, `DisposeAsync` usuwa go i czeka klatkę; `WaitFramesUntil(Func<bool> condition, int maxFrames)` zwraca `false` po limicie (bez `Thread.Sleep`; limit w klatkach plus jawny limit czasu testu w `.runsettings`).
- Testy silnika dzielą jeden proces Godota (stan globalny procesu, lekcja 4): wykonują się sekwencyjnie, każdy sprząta swoje węzły; równoległość wyłączona w `.runsettings`.
- CI: job `game` staje się macierzą `windows-latest` / `ubuntu-latest` (edytor z `engine-windows-editor` albo `engine-linux-editor`): build, format, smoke, testy silnika, raport generatora (artefakt). Testy silnika K2-09 … K2-13 dopisują się do tego joba bez zmian w workflow.

### Raport zgodności Windows–Linux

- `--generator-report <katalog> [--seed <hex u64>]` (w `DevModes`; działa w edytorze i w buildzie wyeksportowanym, bez grafiki). Domyślne ziarno galaktyki `0x484F52595A4F4E54`; planeta `PlanetGenesis.CreatePlanet(seed, 0, GeneratorProfiles.Current)`. Argumenty walidowane: ziarno tylko szesnastkowe, katalog musi istnieć; błąd → kod wyjścia 2.
- `GeneratorSampler` (deterministyczny, bez trygonometrii — `Math.Sin` i podobne dają różne bity na różnych bibliotekach C): kierunki = punkty całkowite na ścianach sześcianu (6 ścian × siatka 3 × 3, współrzędne z {−2, 0, 2} i ±3 na osi ściany), znormalizowane przez `MathF.Sqrt`; dla każdego kierunku kolumna bloków LOD 0 od `R − 330` do `R + 330` co 16 m; 256 bloków jaskiniowych i 64 bloki granicy rdzenia z pozycji całkowitych z `XxHash64(indeks)`; 64 bloki LOD 1–9 (wizualne). Klucz bloku = `floor(pozycja / (16·2^lod))`, lista posortowana (lod, x, y, z) i bez duplikatów.
- `GeneratorDigest`: dla każdego bloku `GenerateBlock` do nowego `VoxelBuffer` z formatem `TerrainChannels`, rozpakowanie kanałów jednorodnych (S4), osobno XxHash64 bajtów kanału SDF i kanału materiału.
- Plik `generator-<platforma>-<runtime>.json`: `schema: 1`, profil, ziarno, promień, platforma, runtime (`editor` albo `template_release`), wersja silnika, `voxel.ref`, commit, lista `{lod, x, y, z, sdf, mat}` (skróty jako 16 cyfr szesnastkowych), mediana czasu generacji bloku w µs (informacyjnie). Kultura niezmienna (`InvariantGlobalization`). Na końcu `GENERATOR REPORT OK blocks=<n> file=<ścieżka>`.
- `tools/generator/compare_reports.py <a.json> <b.json> [...] --out <raport.md>` (Python 3.12, tylko biblioteka standardowa, `ruff format`): wejście jak dane od atakującego (lekcja 6) — sprawdza `schema`, typy, format skrótów, limit 100 000 bloków i rozmiar pliku 50 MB, identyczny profil, ziarno i zbiór kluczy. Wynik: tabela platform, zgodne bloki LOD 0 i LOD > 0 (n/N), pierwsze 20 różnic, czasy. Kod wyjścia 1 przy jakiejkolwiek różnicy LOD 0 (kolizje), różnice LOD > 0 tylko raportowane. `--self-test` sprawdza parser na wbudowanych przypadkach (zgodne, różne, uszkodzone).
- CI: job `generator-consistency` (po macierzy `game`) porównuje raporty edytorów Windows i Linux przy każdym PR dotykającym `game/` albo `engine/`. `nightly.yml` i ręczny `workflow_dispatch`: eksport `windows-release` i `linux-release` (S7), uruchomienie obu buildów z `--generator-report` i porównanie czterech raportów (edytory i buildy release), bo gracze i serwer Linux działają na szablonach release.
- Raport końcowy (cztery runtime'y) trafia do `docs/reports/K2-08-zgodnosc-generatora.md` w tym PR.
- Różnica LOD 0 blokuje scalenie (Pytanie do właściciela 2). Do diagnozy: `--generator-report <katalog> --dump-keys <plik>` zapisuje surowe bajty wskazanych bloków, a `compare_reports.py --raw` podaje liczbę voxeli ze zmianą znaku SDF, maksymalną różnicę SDF i różnice materiału.

### Cykl dnia i nocy (decyzja bramki D12)

```csharp
namespace Horyzont.Game.Planet.Sky;

public static class DayNightCycle
{
    /// <summary>Unit direction from the planet centre towards the sun, in the planet frame, for a server tick.
    /// Pure and stateless: the same tick gives the same direction on every machine and in every mode.</summary>
    public static System.Numerics.Vector3 SunDirection(ulong tick, int tickRate, PlanetParams planet);
}

public sealed partial class SunLight : DirectionalLight3D
{
    public void Apply(ulong tick, int tickRate, PlanetParams planet);   // wątek główny; świeci od słońca do środka planety
}
```

- Doba = `planet.DayLengthSeconds` (domyślnie 1800 s), tick rate z argumentu (`ServerHello.TickRate`). Kąt liczony z `tick mod (DayLengthSeconds · tickRate)` arytmetyką całkowitą (brak utraty precyzji przy dużych tickach), obrót wokół osi +Y planety (bieguny na ±Y, jak w K2-11), tick 0 = słońce w kierunku +X (południe w punkcie startu na równiku). Planety się nie obracają (K2-09), więc obraca się słońce.
- Wynik nie wpływa na symulację ani zapis (lekcja 2), więc trygonometria `Math.Sin/Cos` jest tu dozwolona, inaczej niż w generatorze.
- `DayLengthSeconds ≤ 0` albo `tickRate ≤ 0` → `ArgumentOutOfRangeException` (planeta z `PlanetParams.Validate()` tego nie przepuści, ale argumentu nie ufamy).
- `--planet-preview` może pokazać słońce w stałej pozycji (`SunLight.Apply(0, 60, planet)`).

### Tryb podglądu

`--planet-preview [--seed <hex>] [--screenshots <katalog>]` (wymaga grafiki): teren z `PlanetTerrainOptions.Preview`, kamera z `VoxelViewer` w czterech ustalonych punktach (orbita 12 km od środka, 2 km nad powierzchnią, 100 m, 2 m nad gruntem nad kierunkiem (1, 0, 0)), w każdym czekanie aż teren nie ma zaległych bloków (statystyki Voxel Tools, limit 60 s), zrzut PNG i czas ładowania do logu. Bez `--screenshots` kamera zostaje w ostatnim punkcie. To narzędzie do oceny generatora w PR; sterowaną kamerę deweloperską robi K2-11.

### Kontrakty dla innych pakietów

| Kontrakt | Plik | Kto używa |
| --- | --- | --- |
| `PlanetTerrainFactory.Create(PlanetParams, PlanetTerrainOptions)` → `VoxelLodTerrain` bez strumienia, poza drzewem | `game/src/Planet/PlanetTerrainFactory.cs` | K2-09 (`VoxelTerrainService.AddPlanet`), K2-10 (opcje według trybu) |
| `PlanetTerrainOptions.Server/Client/Preview` | `game/src/Planet/PlanetTerrainOptions.cs` | K2-10 |
| `PlanetTerrainLayout.DataBlockSize`, `LodCount`, `HalfExtentMeters` | `game/src/Planet/PlanetTerrainLayout.cs` | K2-09, K2-11 (zasięg widzów) |
| `TerrainChannels.Sdf`, `Material`, `UsedChannelsMask`, `CreateFormat()` | `game/src/Planet/TerrainChannels.cs` | K2-09 (`TerrainStream`, `TerrainBlockCodec`, `VoxelFormat = 1`) |
| `TerrainMaterials` (klucze, `CoreMaterialKey`, `Keys`), `TerrainMaterialMap.Build/ToTextureIndex/TryGetMaterialId` | `game/src/Planet/` | K2-09 (edycja `Add`), K2-10 (`CreateNew`, kontrola przy starcie), K2-13 (`MaterialPalette`, test materiału) |
| `GeneratorProfiles.Current`, `IsSupported`, `BuildHashComponent`, `CreateGenerator` | `game/src/Planet/Generators/GeneratorProfiles.cs` | K2-10 (nowy świat, start, `BuildInfo.ComputeBuildHash`), K2-09 (płaski generator w złotym teście edycji to jego własny graf testowy) |
| `PlanetGenesis.CreatePlanet/DerivePlanetId/DerivePlanetSeed` | `src/Horyzont.Core/World/PlanetGenesis.cs` | K2-10, K5 (galaktyka) |
| `PlanetTerrainMaterial.Create()` | `game/src/Planet/Rendering/` | K2-10 (klient), K8-50 |
| Uprząż: `game/tests/`, `EngineTestScene`, `.runsettings`, macierz joba `game` Windows/Linux, artefakt `engine-linux-editor` | `game/tests/Support/`, `.github/workflows/ci.yml` | K2-09 … K2-13, K4-19 |
| `--generator-report`, `compare_reports.py`, job `generator-consistency` | `game/src/Planet/Diagnostics/`, `tools/generator/` | K3-16 (częstość różnic), K4-19 |
| `DevModes` (Boot woła przed wyborem trybu gry) | `game/src/Boot/DevModes.cs` | K2-10 zachowuje wywołanie przy przebudowie `Boot` |
| `game/export_presets.cfg` z `windows-release`, `linux-release` | `game/` | K2-10 dopisuje presety serwera (`server-windows`, `server-linux`), tych dwóch nie zmienia |
| `DayNightCycle.SunDirection(ulong tick, int tickRate, PlanetParams planet)`, węzeł `SunLight` (D12) | `game/src/Planet/Sky/` | K2-11 (scena klienta, tick serwera) |
| Etap A: cel `editor-linux`, artefakt `engine-linux-editor` | `engine/engine.json`, `engine.yml` | K2-09 … K2-13 (testy silnika na Linuksie; K2-12 zależy od K2-08A wprost) |

Kontrakty pobierane: `PlanetParams`, `PlanetRegistry`, `MaterialRegistry`, `WorldState.CreateNew`, `BuildInfo.ComputeBuildHash`, `TerrainMath.BlockSize` (Core, bez zmian); `engine.json`, `build.py activate`, artefakty `engine.yml` (K1-01); `game/`, job `game` (K1-02B).

## Testy akceptacyjne (napisz najpierw)

Czysty .NET (`tests/Horyzont.Core.Tests/World/PlanetGenesisTests.cs`, zwykłe CI na Windows i Linux):

| Test | Sprawdza |
| --- | --- |
| `DerivePlanetId_GoldenValues` | trzy pary (ziarno, indeks) → stałe wartości w teście |
| `DerivePlanetId_NeverZero` (FsCheck) | dowolne ziarno i indeks → `PlanetId` ≠ 0 |
| `DerivePlanetSeed_DiffersFromPlanetId` | ziarno planety ≠ `PlanetId.Value` dla tych samych wejść |
| `CreatePlanet_DefaultsMatchSpec` | promień 4000, voxel 1, grawitacja 9,81, atmosfera 1000, rdzeń 400, doba 1800, `System` = 1, profil przekazany |
| `CreatePlanet_EmptyProfile_Throws` | `ArgumentException` |
| `CreatePlanet_ResultPassesWorldCreation` | `WorldState.CreateNew` z wynikiem nie rzuca |

Testy silnika (`game/tests/`, job `game` na Windows i Linux):

| Test | Sprawdza |
| --- | --- |
| `Harness_EngineRuns_RealTIsDouble` | test działa w procesie Godota z `real_t = double` i modułem Voxel Tools (`ClassDB.ClassExists("VoxelLodTerrain")`) |
| `GeneratorV1_SameSeedTwice_IdenticalBlocks` | dwa niezależne generatory tej samej planety → identyczne skróty wszystkich próbek |
| `GeneratorV1_DifferentSeed_DifferentBlocks` | inne ziarno → inne skróty bloków powierzchni |
| `GeneratorV1_GoldenDigest` | skrót zbiorczy pierwszych 128 próbek LOD 0 próbnika z niejednorodnym kanałem SDF (stałe ziarno) = stała w teście; zmiana = nowy profil (ZASADY pkt 3) |
| `GeneratorV1_GraphUsesOnlyAllowedNodes` | każdy węzeł skompilowanego grafu z listy dozwolonych; szum tylko `ZN_FastNoiseLite` |
| `GeneratorV1_SurfaceWithinRelief` | dla 54 kierunków próbnika powierzchnia (zmiana znaku SDF wzdłuż promienia, wyszukiwanie po `GenerateSingle`) w `[R − 300, R + 300]` |
| `GeneratorV1_CoreSolidAndBedrock` | `r ≤ R − C`: SDF < 0 i materiał = indeks `core:bedrock` we wszystkich próbkach rdzenia, także w jaskiniach |
| `GeneratorV1_SpaceEmpty` | `r ≥ R + 316`: SDF > 0 |
| `GeneratorV1_AllMaterialsPresent` | w próbkach występuje każdy z pięciu indeksów |
| `GeneratorV1_CavesExist` | część bloków jaskiniowych ma voxele z SDF > 0 poniżej `R + h − CaveMinDepth` |
| `Generator_UnknownProfile_Throws`, `Generator_VoxelSizeNot1_Throws` | `NotSupportedException` |
| `Factory_ConfiguresTerrain` | Transvoxel w trybie z S2, Clipbox, brak trybu pełnego ładowania, GPU wyłączone, rozmiar bloku danych = `TerrainMath.BlockSize`, `lod_count` i granice ze wzoru, brak strumienia, poza drzewem |
| `Layout_R4000_LodCount11` | wzór dla R = 4000 i dla R = 10 000 (spec: do 10 km) |
| `Factory_TerrainBlocksEqualDirectGeneration` | teren w drzewie z `VoxelViewer` (bez strumienia) po załadowaniu: kopia 8 bloków LOD 0 z `VoxelTool` = `GenerateBlock` bezpośrednio (wątki robocze nie zmieniają bajtów; tego wymaga ścieżka „NotEdited → generator” w K2-09) |
| `MaterialMap_PermutedRegistry_MapsByKey` | świat z materiałami w innej kolejności → mapowanie przez klucz |
| `MaterialMap_UnknownKey_Throws` | `InvalidDataException` z kluczem |
| `MaterialMap_RoundTrip` | `TryGetMaterialId(ToTextureIndex(id)) == id` dla każdego materiału |
| `TerrainMaterials_IndicesFrozen` | tabela (klucz, indeks) = stała lista w teście (tylko dopisywanie) |
| `DayNight_SunDirection_GoldenAndPeriodic` | tick 0 → +X; ćwierć doby → kierunek obrócony o 90° wokół +Y; tick i tick + doba·tickRate → ten sam wektor; wynik jednostkowy ± 1e-6 także dla ticku bliskiego `ulong.MaxValue`; zła doba albo tick rate → `ArgumentOutOfRangeException` |
| `DayNight_SunLight_AppliesDirection` | `SunLight.Apply` ustawia bazę tak, że światło pada wzdłuż −`SunDirection` (± 1e-5) |
| `GeneratorReport_WritesValidFile` | tryb raportu na małym zbiorze → `python -I tools/generator/compare_reports.py <plik> <plik> --out <tmp>` kończy się kodem 0 (ten sam plik dwa razy); gdy Pythona nie ma w środowisku testu, test zgłasza pominięcie z powodem, a CI ma Pythona |

CI i ręczne:

- `generator-consistency`: raporty edytorów Windows i Linux zgodne w 100% dla LOD 0 (job czerwony inaczej).
- `compare_reports.py --self-test` w jobie `generator-consistency`.
- Nocny/ręczny raport czterech runtime'ów zgodny dla LOD 0; wynik w `docs/reports/K2-08-zgodnosc-generatora.md`.
- `Manual`: `--planet-preview --screenshots` na Windows; zrzuty z czterech punktów w PR, bez dziur w terenie i z widocznymi pięcioma materiałami; czasy ładowania punktów w PR (informacyjnie dla K2-11).

## Kroki

- [x] 0. Warunki startu sprawdzone na `main`; wynik w opisie PR. (Sprawdzone 2026-10-09: K1-02B scalony do main (PR #4), ostatni engine.yml na main zielony (run 37836838374), ci.yml na main zielony.)
- [x] 1. **Etap A** (`pakiet/k2-08a-silnik-linux`, osobny mały PR): cel `{ "name": "editor-linux", "runner": "linux", "platform": "linuxbsd", "target": "editor" }` w `engine.json`, upload `engine-linux-editor` w `engine.yml`, opis w PR (zmiana klucza konfiguracji = pełne przebudowanie silnika z cache od zera; uruchom poza godzinami innych PR). Scal po zielonym `engine.yml`; zielony przebieg na `main` jest warunkiem kroku 4. (PR #10, gałąź pakiet/k2-08a-silnik-linux; scalenie po stronie właściciela.)
- [x] 2. Rozpoznanie S1–S7 na gałęzi etapu B (kod próbny usuń przed PR albo zostaw jako test); nazwy API i decyzje do „Odstępstw”. (S1–S7 zapisane w „Odstępstwach”.)
- [x] 3. Core: testy `PlanetGenesisTests` (czerwone), `PlanetGenesis` (zielone); `dotnet build`/`test`/`format` rozwiązania.
- [x] 4. Uprząż: paczki gdUnit4Net (albo runner zapasowy według S6), `.runsettings`, `EngineTestScene`, `Harness_EngineRuns_RealTIsDouble`; job `game` jako macierz Windows/Linux z testami silnika; zielony na obu. (Windows lokalnie: 2 testy silnika zielone; macierz Linux i job `game` na Linuksie: pending-CI.)
- [x] 5. `TerrainChannels`, `TerrainMaterials`, `TerrainMaterialMap` z testami map i zamrożonej tabeli. (S2: tryb `TEXTURES_SINGLE_S4`, kanał `CHANNEL_INDICES` 8 bitów, SDF 16 bitów.)
- [x] 6. `GraphBuilder` z listą dozwolonych węzłów, `NoiseSeeds`; test listy dozwolonych na grafie próbnym. (S1: nazwy węzłów i porty z `get_node_type_info`, patrz Odstępstwa.)
- [x] 7. `PlanetGeneratorV1` i `GeneratorProfiles`: testy kształtu, rdzenia, kosmosu, materiałów, jaskiń, powtarzalności, nieznanego profilu (bez złotego skrótu). (GeneratorSampler, GeneratorDigest i GeneratorProbe powstały już tu, bo testy generatora ich używają; krok 11 dodaje raport. Test `GeneratorV1_GoldenDigest` świadomie pominięty do kroku 10.)
- [x] 8. `PlanetTerrainLayout`, `PlanetTerrainFactory`, `PlanetTerrainMaterial`, shader; testy konfiguracji i `Factory_TerrainBlocksEqualDirectGeneration`. (Odstępstwa: granice voxeli zaokrąglane do oktantu, patrz niżej.)
- [x] 8a. `DayNightCycle`, `SunLight` z testami `DayNight_*` (D12).
- [x] 9. `DevModes` i `--planet-preview`; strojenie stałych generatora i `lod_distance` na zrzutach; zrzuty do PR. Właściciel akceptuje wygląd przed krokiem 10 (Pytanie 6). (zrzuty z czterech punktów w `F:/dev/horyzont-k2-08b-preview/`, `lod_distance` = 96; akceptacja wyglądu przez właściciela czeka na krok 10.)
- [ ] 10. (czeka na akceptację wyglądu przez właściciela, Pytanie 6; nie wykonano) Zamrożenie: `GeneratorV1_GoldenDigest` ze stałą wpisaną po strojeniu; wiersz w ZASADY pkt 3 i dopisek do wiersza `engine.json`.
- [x] 11. `GeneratorSampler`, `GeneratorDigest`, `--generator-report` (z `--dump-keys`), `compare_reports.py` z `--self-test`, job `generator-consistency`, `game/export_presets.cfg`, nocny/ręczny raport release w `nightly.yml`. (Joby `release-windows`, `release-linux`, `release-compare` w `nightly.yml` napisane; uruchomienie w CI: pending-CI.)
- [ ] 12. (częściowo: Windows edytor i release zgodne lokalnie, kolumny Linux pending-CI, raport w `docs/reports/`) Ręczne uruchomienie raportu czterech runtime'ów; `docs/reports/K2-08-zgodnosc-generatora.md` z wynikiem. Różnica LOD 0 → diagnoza `--dump-keys` i eskalacja (Pytanie 2), nie scalanie.
- [ ] 13. (część lokalna wykonana: build, testy, format, smoke, testy silnika zielone; CI, przegląd i scalenie pending) Pełne `dotnet build Horyzont.sln`, `dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"`, `dotnet format Horyzont.sln --verify-no-changes`, `python tools/engine/build.py smoke`, testy silnika lokalnie na Windows; wyniki w PR. CI zielone (macierz `game`, `generator-consistency`), przegląd Opus 5.5, scalenie; CI na `main` zielone po scaleniu (lekcja 4).

Każdy krok bez odhaczenia przy scaleniu ma dopisek „odroczone do K2-xx” z uzasadnieniem (lekcja 5).

## Odbiór

- Testy akceptacyjne z obu tabel przechodzą w CI na Windows i Linux; `generator-consistency` zielony.
- `docs/reports/K2-08-zgodnosc-generatora.md`: cztery runtime'y (edytor i release na Windows i Linux), 100% zgodności LOD 0, wynik LOD > 0, mediany czasu generacji bloku.
- Zrzuty `--planet-preview` z czterech punktów w PR i akceptacja wyglądu przez właściciela przed zamrożeniem złotego skrótu.
- Kontrakty z tabeli „Kontrakty dla innych pakietów” mają w kodzie dokładnie podane nazwy, a każde odstępstwo od nazw jest w „Odstępstwach” (K2-09 krok 0 je przepisuje).
- ZASADY pkt 3 ma nowy wiersz o profilach generatora i tabeli materiałów.

## Pułapki

- **Strojenie po zamrożeniu.** Złoty skrót wpisuj dopiero po akceptacji wyglądu. Po scaleniu każda zmiana `PlanetGeneratorV1`, ziaren szumów, ustawień kompilacji grafu, kanałów albo tabeli materiałów to nowy profil, nie poprawka.
- **Wartości domyślne Voxel Tools.** Domyślne właściwości terenu, grafu i mesher'a mogą się zmienić z wersją modułu; ustawiaj każdą, od której zależą bajty albo układ (lista w „Fabryce” i S5).
- **Trygonometria i biblioteka C.** `Math.Sin`, `MathF.Cos`, `Pow` w C# i węzły trygonometryczne w grafie dają różne bity na MSVC i glibc. Próbnik i generator używają tylko arytmetyki, `Sqrt` i operacji całkowitych.
- **FastNoise2.** Węzły FastNoise2 są szybsze, ale zależą od SIMD procesora; zakazane w generatorze kolizji (lista dozwolonych to wymusza).
- **Kanał jednorodny.** Voxel Tools kompresuje kanał o jednej wartości; skrót liczony z surowej tablicy bez rozpakowania da różne wyniki dla tych samych danych (S4).
- **Generator współdzielony przez wątki.** Graf po `Compile()` jest tylko do odczytu; nie zmieniaj parametrów na żywym terenie.
- **Strumień po wejściu do drzewa.** Przypięcie strumienia po dodaniu węzła do drzewa powoduje generację bloków, które mają edycje w bazie (dotyczy K2-09; fabryka oddaje węzeł poza drzewem).
- **Mapowanie źródeł NuGet.** `GodotSharp*` przychodzi tylko z `engine/nuget-local`; zależność gdUnit4Net od `GodotSharp` musi się rozwiązać do naszej wersji 4.7.2 (`double`). Paczka z nuget.org dałaby `real_t = float` (test `Harness_EngineRuns_RealTIsDouble`).
- **`Directory.Packages.props` uruchamia `engine.yml`.** Zmiana paczek wyzwala workflow silnika; cache według klucza konfiguracji powinien go skrócić — sprawdź w PR.
- **Etap A przebudowuje silnik.** Nowy cel zmienia klucz konfiguracji, więc pierwszy przebieg buduje wszystko od zera (godziny, limit cache 10 GB: przelicz `HORYZONT_SCONS_CACHE_LIMIT` dla pięciu celów).
- **Testy w buildzie gry.** Bez warunku na konfiguracje eksportu testy i paczki gdUnit4 trafią do gry i serwera.
- **Bez grafiki.** `--headless` używa atrapy renderu: meshing i kolizje działają, shader się nie kompiluje; testy nie mogą zależeć od renderu.
- **Precyzja.** Teren Voxel Tools liczy w `float`; przy 4–4,4 km to ok. 0,5 mm, wystarczy. Konwersje `System.Numerics` ↔ `Godot` (double) zawsze jawne.
- **`InvariantGlobalization`.** Liczby w JSON raportu formatuj z `CultureInfo.InvariantCulture`; skróty jako tekst szesnastkowy, nie liczba (JSON traci precyzję powyżej 2^53).
- **Lekcja 3 (atomowy join) nie dotyczy pakietu**; lekcja 6 dotyczy wejść raportu i argumentów trybów deweloperskich (walidacja, limity).

## Pytania do właściciela

| # | Pytanie | Domyślnie |
| --- | --- | --- |
| 1 | Edytor Linux w `engine.json` (dłuższe CI, więcej cache) w osobnym etapie A? | tak, etap A jako osobny PR przed etapem B (precedens K1-02A/B) |
| 2 | Czy różnica bloków LOD 0 między Windows i Linux blokuje scalenie? | tak; LOD > 0 tylko w raporcie. Przy różnicy: diagnoza i decyzja właściciela (np. zgoda z poleganiem na sumach kontrolnych K3-16) |
| 3 | GPU dla wizualnych detali dalekich LOD-ów (spec dopuszcza)? | wyłączone w K2-08; ocena w K8-50 albo K4-19 |
| 4 | Klucze i zestaw materiałów: `core:stone`, `core:dirt`, `core:sand`, `core:ore`, `core:bedrock` (rdzeń)? | tak; tekstury zastępcze do K8-50 |
| 5 | `PlanetGenesis` w Core (przegląd Opus 5.5) czy w grze? | Core: testowalny w zwykłym CI na obu systemach, potrzebny też podprojektowi 2 |
| 6 | Kto akceptuje wygląd planety przed zamrożeniem profilu `core:planet/1`? | właściciel na zrzutach z PR; bez odpowiedzi w 2 dni robocze zamraża Sonnet z wartościami ze strojenia |
| 7 | Raport z buildów release przy każdym PR czy nocnie? | edytory przy każdym PR dotykającym `game/` lub `engine/`; release nocnie, ręcznie i raz w tym PR |
| 8 | `MeshBlockSize` 16 czy 32 (mniej wywołań rysowania z daleka)? | 16; pomiar i ewentualna zmiana w K4-19 (tylko wizualne, bez wpływu na format) |

## Odstępstwa

(wypełnia wykonawca: wyniki S1–S7, faktyczne nazwy API, wybór uprzęży, `lod_distance`, odstępstwa od nazw kontraktów)

**Poprawki przeglądu 2026-10-09 (punkty 1–3 z `F:/dev/k2-08-review.md`).** `nightly.yml` buduje grę w Debug po aktywacji paczek na Windows i Linux (`1e98f07`). Porównywarka odrzuca puste raporty i brak LOD 0 (`ce06881`); różnice surowych bloków LOD 0 zmieniają wynik ogólny i kod wyjścia na 1, a niezgodne zestawy kluczy, puste zrzuty, duplikaty i ucięte rekordy są błędnym wejściem (`2c7ad5d`). Nowe testy negatywne były czerwone przed poprawkami i zielone po nich. Walidacja: `--self-test`, parser YAML, `dotnet build Horyzont.sln`, `dotnet format Horyzont.sln --verify-no-changes` i testy rozwiązania bez Soak/Manual (1099 testów) zielone. W lokalnym katalogu `report` jest tylko raport edytora Windows; porównanie z nim samym nadal daje IDENTICAL (2275/2275 LOD 0, 59/59 LOD > 0), bez potwierdzenia zgodności między runtime'ami. Wynik generatora i kontrakty chronione bez zmian; bez pushowania.

**S6 (uprząż).** gdUnit4Net działa z naszym silnikiem; runner zapasowy `-- --engine-tests` nie jest potrzebny. Paczki: `gdUnit4.api` 5.0.0, `gdUnit4.test.adapter` 3.0.0, `gdUnit4.analyzers` 1.0.0 (najnowsze stabilne; zależą od `GodotSharp >= 4.4.0`, który mapowanie źródeł rozwiązuje do naszego 4.7.2 `double`). `dotnet test game/Horyzont.Game.csproj --settings game/.runsettings` z `GODOT_BIN` = edytor `double` uruchamia `[TestCase]` z `[RequireGodotRuntime]` w prawdziwym procesie Godota (sprawdzone celowo czerwonym asercją). Adapter zapisuje w projekcie katalog `game/gdunit4_testadapter_v5/` (ignorowany w git, oznaczony `generated_code` w `game/.editorconfig`, żeby `dotnet format` go pomijał). `game/tests/.editorconfig` wyłącza CA1707 i CA1822 (nazwy testów z podkreśleniami wg ZASADY pkt 8, metody instancyjne dla frameworka). Wyłączenie z buildów eksportu: `Compile Remove="tests/**"` dla `ExportDebug`/`ExportRelease`, a pakiety testowe są referencjami tylko poza tymi konfiguracjami. Linux: pending-CI (macierz `game` w `ci.yml` napisana, niesprawdzona).

**S1 (graf w kodzie).** API C# (glue `Godot*`, nazwy z `VoxelGraphFunction`): `CreateNode(NodeTypeId, Vector2, uint id = 0)` → `uint`, `AddConnection(srcNode, srcPort, dstNode, dstPort)`, `SetNodeParam(node, index, Variant)`, `GetNodeIds()`, `GetNodeTypeId(node)`, `VoxelGeneratorGraph.Compile()` → słownik `{success, message, node_id}`. Typy i porty (z `get_node_type_info` edytora): `Constant` (param 0 `value`), `InputX/Y/Z`, `Add/Subtract/Multiply/Divide/Min/Max` (wejścia a, b), `Abs/Sqrt` (x), `Clamp` (wejścia x, min, max), `Distance3D` (x0,y0,z0,x1,y1,z1), `Smoothstep` (wejście x, parametry edge0, edge1), `Select` (wejścia a, b, t, parametr threshold; wynik `t < threshold ? a : b`), `FastNoise3D` (param 0 = `Zn_FastNoiseLite`; w C# klasa nazywa się `Zn_FastNoiseLite`), `OutputSdf` (sdf), `OutputSingleTexture` (index). Zamiast `Length`/`Normalize` użyto `Distance3D` do początku układu; `Normalize3D` jest na liście dozwolonych, ale generator go nie używa. Dzielenie przez promień zabezpieczone `Max(r, 1)` (w środku planety r = 0 dałoby NaN).

**S2 (tekstury).** `VoxelMesherTransvoxel.TexturingMode = SingleS4` (`TEXTURES_SINGLE_S4`), kanał `ChannelIndices` w głębi 8 bitów, SDF w 16 bitach; jeden indeks na voxel działa (render sprawdzony na zrzutach). `VoxelGeneratorGraph.TextureMode = Single`. Shader odczytuje indeksy z `CUSTOM1.x` (4 bajty), wagi z `CUSTOM1.y`; materiał to `Texture2DArray` pięciu tekstur 64x64.

**S3 (teren).** Format kanałów: zasób `VoxelFormat` (`SetChannelDepth`) przypisany przez `VoxelLodTerrain.SetFormat`. **Granice voxeli: Voxel Tools zaokrągla je w chwili ustawienia do wielokrotności oktantu `MeshBlockSize << (LodCount − 1)` (dla R = 4000: 16384), więc `lod_count` i `mesh_block_size` trzeba ustawić przed `voxel_bounds`.** Ustawione w złej kolejności (domyślne 4 LOD-y, oktant 128) granice zaokrąglały się do ±4480 i teren z 11 LOD-ami nie ładował żadnego bloku (wykryte testem `Factory_TerrainBlocksEqualDirectGeneration`). Odstępstwo od planu: granice terenu to `[−16384, 16384)` (`PlanetTerrainLayout.BoundsHalfExtentVoxels`, 2 x 2 x 2 oktanty), a nie `[−H, H)` z `H = HalfExtentMeters` (4368); `HalfExtentMeters` i `LodCount` zostają według planu (11 LOD-ów). Rozmiar bloku danych to stałe 16 (`GetDataBlockSize`). `lod_distance` Voxel Tools ogranicza do 128. Dodano `OctreeSizeVoxels`/`BoundsHalfExtentVoxels` do `PlanetTerrainLayout`.

**S4 (odczyt).** Skryptowe API 1.7 nie ma `GenerateSingle`; `GeneratorProbe` woła `GenerateBlock` z buforem 1x1x1. `VoxelBuffer.GetChannelAsByteArray` rozpakowuje kanały jednorodne w locie, więc osobne rozpakowanie nie jest potrzebne. Uwaga dla testów: przy `sdf_clip_threshold` = 1,5 SDF dalej niż 1,5 od powierzchni jest zastąpione stałą (około ±100), a materiał w powietrzu przyciętego bloku nie jest zapisywany (0 = kamień); znak SDF zawsze się zgadza.

**S5 (ustawienia kompilacji).** Jawnie w `PlanetGeneratorV1`: `sdf_clip_threshold` 1,5, `use_subdivision` true, `subdivision_size` 16, `use_optimized_execution_map` true, `use_xz_caching` true (wartości domyślne modułu zapisane jako stałe profilu). Wszystkie szumy: `Zn_FastNoiseLite` OpenSimplex2, rotacja `ImproveXyPlanes`, lakunarność 2, wzmocnienie 0,5, bez warp.

**Kolejność i zakres kroków.** `GeneratorSampler`, `GeneratorDigest` i `GeneratorProbe` powstały w kroku 7 (testy generatora ich potrzebują), a `PlanetTerrainLayout` w kroku 7/8; krok 11 dodaje raport, porównywarkę i CI. `GeneratorV1_GoldenDigest` jest świadomie niezapisany do kroku 10 (zamrożenie po akceptacji wyglądu). Fabryka ma opcjonalny parametr `Material? material` (wspólny materiał dla wielu planet); bez niego przy `Render` tworzy własny `PlanetTerrainMaterial.Create()`. `lod_distance` = 96 (pomiar niżej).

**Krok 9: pomiar `lod_distance` i zrzuty (AMD Radeon RX 9070 XT, Vulkan, seed galaktyki `0x484F52595A4F4E54`, powierzchnia nad +X na promieniu 3998).** Czasy ładowania (do braku zaległych zadań voxel, ms) i liczba bloków siatki dla punktów orbita / 2 km / 100 m / 2 m: `lod_distance` 48: 273 / 408 / 1996 / 821 (bloki siatki 25 / 165 / 1234 / 2064); 96: 306 / 504 / 4007 / 1819 (25 / 165 / 2174 / 4266); 128 (maksimum silnika): 394 / 607 / 6915 / 2500 (25 / 165 / 4105 / 6850). Wybrano 96 jako kompromis (K2-11 stroi dalej z klatkami na sekundę). Zrzuty: `F:/dev/horyzont-k2-08b-preview/planet-preview-{1-orbit,2-high,3-mid,4-ground}.png` (poza repozytorium). Widoczne materiały w tych czterech punktach: kamień, ziemia, ruda; piasek tylko w nizinach (`h < −60`), a skała rdzenia nie jest widoczna bez wykopu. Wartości stałych generatora są startowe z planu, bez strojenia (nic nie wymagało korekty). Akceptacja wyglądu przez właściciela (Pytanie 6) czeka.

**S7 (eksport release).** Z edytora Windows działa `--export-release windows-release ../build/windows-release/Horyzont.exe` z `custom_template/release` wskazującym szablon `godot.windows.template_release.double.x86_64.mono.exe` z `engine/out/<klucz>`; wynik: `Horyzont.exe` (71 MB), `Horyzont.pck` i katalog `data_Horyzont.Game_windows_x86_64/` (nazwa od `AssemblyName`). Uruchomienie `Horyzont.exe --headless -- --generator-report <katalog>` zapisuje `generator-windows-template_release.json`, identyczny na LOD 0 i LOD > 0 z edytorem Windows (2275/2275 i 59/59). Eksport wymagał `Compile Remove="gdunit4_testadapter_v5/**"` w konfiguracjach `ExportDebug`/`ExportRelease` (generowany adapter testów nie kompiluje się bez pakietów testowych; bez tego `dotnet publish` Godota kończył się błędem i powstawał eksport bez zestawów). Wykonywalny z eksportu jest aplikacją okienkową: wyjście trzeba przekierować do pliku. Preset w repozytorium ma puste `custom_template/release`; `nightly.yml` wstawia ścieżki szablonów przez `sed` (ścieżki są lokalne dla maszyny). Eksport Linuksa z edytora Windows (szablon `godot.linuxbsd.template_release.double.x86_64.mono`) i uruchomienie na Linuksie: pending-CI.

**Krok 11 (raport).** `--generator-report <katalog> [--seed hex] [--dump-keys plik]` zapisuje `generator-<platforma>-<runtime>.json` (schemat 1) i opcjonalnie `raw-<platforma>-<runtime>.bin`; `compare_reports.py` waliduje wejście jako niezaufane (schemat, typy, format skrótów, limity), kod wyjścia 0 = LOD 0 identyczny, 1 = różnica LOD 0, 2 = błędne dane; `--self-test` w jobie `generator-consistency`. Nazwa runtime w raporcie: `editor`, `template_release` albo `template_debug`. Przy okazji poprawiono nazwy prywatnych pól statycznych (`_baseColors`, `_known`), które łamały `dotnet format game/Horyzont.Game.csproj --verify-no-changes` w jobie `game`.
