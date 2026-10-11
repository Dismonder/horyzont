# Zasady projektu

Ten plik obowiązuje każdego wykonawcę: człowieka, Claude Code, Codex i Antigravity. Gdy plan pakietu jest sprzeczny z tym plikiem, wygrywa ten plik. Gdy kod jest sprzeczny ze specyfikacją, wygrywa specyfikacja, dopóki nie zostanie zmieniona po przeglądzie Opus 5.5.

## 1. Źródła prawdy

| Co | Gdzie |
| --- | --- |
| Co budujemy i dlaczego | [spec/](spec/) (specyfikacje podprojektów 1–6) |
| Kto robi który pakiet | [plan/plan-projektu.md](plan/plan-projektu.md) |
| Jak zrobić konkretny pakiet | [plans/](plans/README.md) |
| Jak piszemy kod | ten plik |

Każda sesja pracy zaczyna się od przeczytania tego pliku i planu pakietu. Niejasność albo sprzeczność w planie zatrzymuje pracę: zapisz pytanie w opisie PR i eskaluj, nie zgaduj.

## 2. Architektura i granice

- Projekty `Horyzont.Core`, `Horyzont.Persistence` i `Horyzont.Net` nie mogą odwoływać się do Godota (pilnuje tego test w `tests/Horyzont.Architecture.Tests`).
- Kierunek zależności: `Core` ← `Persistence`, `Core` ← `Net`, wszystkie ← `game/`. Core nie zależy od niczego poza BCL, `Microsoft.Extensions.Logging.Abstractions` i `System.IO.Hashing`.
- Wątki robocze (meshing, strumień terenu) czytają wyłącznie migawki skopiowane na wątku głównym; nigdy `WorldState`, `Construct` ani węzłów Godota.
- Serwer jest autorytatywny. Klient wysyła wyłącznie intencje i wejście; jedyne lokalne zmiany po stronie klienta to przewidywany ruch własnej postaci i podgląd własnych edycji, które serwer potwierdza albo nadpisuje.
- `ITerrainService` woła się wyłącznie na wątku głównym Godota (`VoxelTerrainService` rzuca `InvalidOperationException` na innym wątku), więc `ServerSimulation` działa na wątku głównym. Strumień Voxel Tools (wątki robocze) czyta tylko `ITerrainBlockSource` i nie dotyka `WorldState`, `ServerSimulation` ani drzewa sceny (K2-09).
- Symulacja serwera, sesje i `ITerrainService` działają wyłącznie na wątku głównym Godota; inne wątki (writer, Voxel Tools, konsola, sygnały) komunikują się z nim tylko kolejkami albo flagami.
- Zero mutowalnego stanu statycznego. Zależności składa jedno miejsce startowe (composition root) w `game/src/Boot/`.
- Jedynym composition root jest `GameComposition` (`game/src/Boot/`); węzły `/Root/Server` i `/Root/Client` dostają zależności metodą `Initialize` przed wejściem do drzewa. Węzeł serwera tyka z priorytetem fizyki -100, klienta 0, więc w hoście tick serwera poprzedza `Poll` klienta lokalnego; host ma jedną instancję terenu (`SharedTerrainProvider`).
- Każda zmiana świata to operacja z `Horyzont.Core.Operations`, stosowana przez `ServerSimulation` i zapisywana w dzienniku. Nie ma innej drogi zmiany stanu świata.
- Lot, zużycie paliwa i pozy statków poza dziennikiem — wyłącznie `UpdateConstructKinematics`/`UpdateShipState` z `ShipFlightSystem` (K5-24); paliwo dodaje tylko operacja `ShipFuelAddOp`.
- Kanoniczny stan każdego bytu to rama rodzica (`FrameRef`), licznik przepięć `ParentSeq` i poza lokalna w tej ramie (`System.Numerics.Vector3` i `Quaternion`, metry). Pozycja w świecie jest zawsze wartością pochodną: zapis, dziennik i sieć znają wyłącznie pozycje lokalne.
- Ramy składa się tylko w podwójnej precyzji (`Vector3D`, `QuaternionD` i `FrameMath` w Core, od K5-20). Pojedyncza precyzja wystarcza wewnątrz ramy planety albo budowli.
- Konwersja między Godotem (`double`) a ramą planety (`System.Numerics`, `float`) odbywa się wyłącznie przez `PlanetFrames` (`game/src/Shared/Frames/`, K2-11), z kontrolą skończoności. Nigdzie indziej nie rzutuje się wektorów ani kwaternionów Godota na `System.Numerics`.
- Świat Godota = rama układu, bez pływającego początku (K5-20). `Vector3D`/`QuaternionD`/`PoseD` ↔ Godot wyłącznie przez `GodotFrames`. Wejście `PlanetFrames` to przestrzeń lokalna węzła planety: wartości globalne (`GlobalPosition`, `GlobalTransform`, promienie fizyki) przechodzą przez `PlanetSpace` (`FrameNodes.SpaceOf`/`PlanetSpaceOf`), nigdy wprost.
- Fazy ticka serwera: `Frames` → `Characters` → `Compose` (`ServerTickPhase`; w obrębie fazy kolejność rejestracji). Ruch budowli tylko przez `ServerSimulation.UpdateConstructKinematics` i tylko w trybie `Ship`.
- Pasażer (byt, którego łańcuch ram przechodzi przez budowlę `Ship`) jest widoczny u odbiorcy wtedy i tylko wtedy, gdy jego statek: dostaje podmiot zainteresowania najbliższego statku w łańcuchu, pojawia się po nim i znika przed nim (K5-26).
- Migawki bytu w ramie budowli (i samej budowli w trybie `Ship`) dostaje odbiorca dopiero, gdy każda budowla w łańcuchu ram jest u niego na żywo (`ConstructStateSender.IsLive`) — najpierw pełny stan, potem migawki (K5-26).
- `EntitySpawn.KindData` gracza = komórka fotela (`EntitySpawnSeat`: puste albo `[1][x i32][y i32][z i32]`); dekoduje tylko klient (`EntityMirror`) z pełną walidacją (K5-26).
- Planeta domowa to `WorldState.HomePlanet` (serwer) albo `hello.Planets[0]` (klient; kolejność gwarantuje `CreateHello`), nigdy `Planets.All[0]` — rejestr planet jest posortowany po id, a układ gwiezdny ma kilka ciał (K5-21).
- Tereny planet są dziećmi węzłów `Planet_<id>` (`FrameNodes`) z transformacją tożsamościową; API terenu (`IsAreaLoaded`, `LoadAreaBlocking`, `Raycast`, `ApplyEdit`, pozycje widzów pod kotwicą klienta) przyjmuje współrzędne ramy planety (K5-22).
- Teren ciała istnieje tylko, gdy jest aktywny: na serwerze tworzy i wygasza go wyłącznie `ServerPlanetTerrains` (zapotrzebowanie graczy, poruszających się statków i dzierżaw; wygaszenie 60 s po ostatnim; planeta domowa zawsze), na kliencie `ClientPlanetTerrains` przez `IClientTerrainProvider.Activate`/`Release` (host: dzierżawa terenu serwera). Nikt inny nie tworzy węzłów terenu dla `Planets.All` ani `hello.Planets` (K5-22).
- Trójplanarne terenu i impostory liczone w przestrzeni modelu planety (wierzchołek bloku siatki albo impostora przed `MODEL_MATRIX`), nigdy w świecie ani widoku (K5-22).
- Promień intencji narzędzi zawsze zaczyna się w oku liczonym wzorem serwera ze stóp (`AimRayBuilder.EyeFromFeet`), także w widoku z trzeciej osoby; wektory z Godota normalizuje się dopiero po konwersji do `float` (długość poza 1 ± 0,01 albo wartość nieskończona oznacza „nie wysyłaj”). Klient nie stosuje lokalnie edycji terenu, który dzieli z serwerem (solo i host): predykcja tylko przez `IEditPrediction` na osobnej kopii terenu zdalnego klienta (K3-16).
- Gracz w dzienniku, zapisie i sieci to wewnętrzny `PlayerId`. Tożsamość platformy (`PlatformIdentity`, np. SteamID) służy tylko do łączenia z kontem, banów i administratorów.
- Tożsamość Steam jest wiarygodna tylko przez endpoint Steam P2P: `HandshakeValidator` odrzuca (`IdentityMismatch`) zarówno SteamID różny od endpointu, jak i tożsamość Steam zgłoszoną przez UDP albo inny endpoint. Lokalny klient hosta i połączenia UDP używają tożsamości Dev. Metadane lobby Steam są informacyjne i nigdy nie zastępują handshake; endpoint P2P pochodzi z właściciela lobby, nie z identyfikatora lobby.

## 3. Kontrakty chronione

Każda zmiana w `src/Horyzont.Core/` wymaga przeglądu Opus 5.5, bo cały Core definiuje formaty dziennika, zapisu i protokołu. Poza przeglądem zmiana w tych ścieżkach wymaga podbicia odpowiedniej wersji:

| Ścieżka | Co podbić przy zmianie |
| --- | --- |
| `src/Horyzont.Core/Operations/`, `src/Horyzont.Core/Intents/`, `src/Horyzont.Core/Simulation/OperationApplier.cs`, `src/Horyzont.Core/Simulation/TerrainRules.cs` (stałe semantyki operacji: margines, limity) | wersję formatu operacji albo intencji w kodeku (K1-04); stare dzienniki muszą odtwarzać się identycznie, więc zmiana semantyki stosowania też jest zmianą formatu |
| `src/Horyzont.Core/Ids/` (w tym `FrameRef`, `PlatformIdentity`), `src/Horyzont.Core/World/` (typy zapisywane: `BlockState`, `BlockOrientation` (tabela 24 orientacji jest formatem `BlockState`), `CellPos`, `ChunkPos`, `Face`, `BlockShape`, `ConstructMode`, `TerrainBlockKey`, `TerrainBlockVersion`, `PlanetParams`, `RegistryEntry`), `src/Horyzont.Core/Checkpoints/` | wersję formatu każdej ramki, która zawiera zmieniony typ (operacje, intencje, komunikaty, bloby), i odczyt starej wersji w Persistence |
| `src/Horyzont.Core/World/Blocks/BlockTypeDef.cs` (masa i funkcje bloku, K5-23; wyjątek od wiersza `World/` powyżej) | nic do podbicia: treść, nie format; plik treści `blocks.json` ma własną wersję formatu (`BlockTypeCatalog`), a nie trafia do dziennika ani checkpointu |
| `src/Horyzont.Core/Hashing/` | opis w PR: skróty zapisane w testach i raportach przestają się zgadzać |
| `src/Horyzont.Core/Protocol/`, `src/Horyzont.Core/Serialization/` | `ProtocolConstants.ProtocolVersion` albo wersję formatu komunikatu |
| `src/Horyzont.Core/Motion/SurfaceFrame.cs` (konwencja kąta `PlayerInput.Yaw`: rama odniesienia „północy”, zwrot obrotu, przełączenie przy biegunach) | `ProtocolConstants.ProtocolVersion`: `SurfaceFrame` definiuje znaczenie pola `Yaw`, więc klient i serwer o różnych konwencjach rozjeżdżają się bez błędu kodeka |
| `src/Horyzont.Persistence/Migrations/` | nowa migracja z następnym wolnym numerem w chwili scalenia do `main`; istniejących migracji nigdy się nie edytuje, a numery są ciągłe od 1 |
| `engine/engine.json`, `nuget.config`, `global.json`, `Directory.Build.props`, `Directory.Packages.props` | opis zmiany w PR i zielony test dymny silnika; zmiana `voxel.ref` w `engine/engine.json` wymaga niezmienionych złotych skrótów profili generatora (`GeneratorV1_GoldenDigest`), inaczej nowe profile dla nowych światów i decyzja właściciela o starych |
| `game/src/Planet/Generators/`, `game/src/Planet/TerrainChannels.cs`, `game/src/Planet/TerrainMaterials.cs` | nic do podbicia: istniejący profil daje te same bajty (złoty skrót bez zmian, test `GeneratorV1_GoldenDigest`); zmiana generatora = nowy profil (`core:planet/2`) obok starego; tabela materiałów i kanały tylko dopisywane |
| `game/src/Terrain/TerrainEditSemantics.cs`, `voxel.ref` w `engine/engine.json` (semantyka edycji terenu, K2-09) | podbicie `TerrainEditSemantics.SemanticsVersion` i aktualizacja skrótu złotego `EditSemantics_GoldenDigest` z opisem w PR; ogon dziennika zapisany inną wersją semantyki (`meta.terrain_semantics`) nie jest powtarzany, świat otwiera się w trybie bezpiecznym |
| `tests/Horyzont.Core.Tests/Golden/TerrainJournals/`, `tests/Horyzont.Core.Tests/TerrainJournalGoldenCorpusTests.cs` (zamrożony korpus złoty dzienników terenu, K2-09) | nigdy się nie zmieniają; test, który wymagałby zmiany korpusu albo pliku testu, oznacza zmianę semantyki stosowania, czyli podbicie wersji formatu operacji z powtórką starej wersji |
| `src/Horyzont.Core/Space/` (typy zapisywane: `StarSystemParams`, `BodyPlacement`; `StarSystemLayout.V1` i `StarSystemGenesis` zamrożone jak `PlanetGenesis`, K5-21) | zmiana zapisu: wersja formatu blobu 10 i `ProtocolConstants.ProtocolVersion`; zmiana rozmieszczenia albo parametrów ciał: nowy `StarSystemLayout` (`V2` obok `V1`), nigdy edycja `V1` |
| `M0002_Space` (K5-21) uzupełnia układ starych światów przez `StarSystemLayout.V1` | nic: migracji się nie edytuje; inny układ = nowa wersja układu w nowej migracji |
| `src/Horyzont.Core/Ships/Flight/` (model lotu wspólny z predykcją pilota, K5-24) | zmiana zachowania: opis w PR i `ProtocolConstants.ProtocolVersion`, bo predykcja klienta i serwer muszą liczyć tak samo |

Zmiana `OperationApplier.cs` albo sygnatury `ITerrainService.ApplyEdit` bez podbicia wersji formatu operacji jest dozwolona tylko wtedy, gdy zamrożony korpus złoty dzienników terenu (`TerrainJournalGoldenCorpusTests`) i złote skróty `DeterminismTests` przechodzą bez żadnej zmiany w swoich plikach (`git diff <commit korpusu>..HEAD` na tych ścieżkach pusty).

Walidator intencji (`src/Horyzont.Core/Simulation/IntentValidator.cs`, `BlockOverlap.cs`, `SlidingWindowLimiter.cs`, `PlayerCapsule.cs`) i `GameRules` nie są ścieżkami formatu: zmiana warunków przyjęcia intencji nie zmienia ani dziennika, ani powtórki (operacje zapisują skutek, nie intencję), więc nie podbija wersji. Wymaga przeglądu Opus jak każda zmiana w Core, a zamrożony korpus i złote skróty muszą zostać bez zmian. Limity edycji terenu są twarde: najwyżej `TerrainEditsPerSecond` przyjętych edycji w każdym oknie jednej sekundy (kubełek plus okno przesuwne), a zasięg liczy się od oka serwera (`PlayerState.EyePosition`), nie od zgłoszonego początku promienia (K2-13).

Zamrożone na zawsze, bez względu na wersję protokołu: początek ramki `ClientHello` (typ, wersja formatu, `ProtocolVersion`) i cały format `HandshakeReject`. Dzięki temu klient każdej wersji dostaje czytelną prośbę o aktualizację. Każda zmiana formatu `ClientHello` podbija też `ProtocolVersion`.

Nowe kody operacji, intencji, odmów, komunikatów i blobów biorą numery wyłącznie z zakresu swojego podprojektu (tabela zakresów w K1-03). Dzięki temu podprojekty 2 i 3 powstają równolegle bez konfliktów numeracji.

## 4. Determinizm (Core)

Kod stosujący operacje (`OperationApplier`, `ServerSimulation`, wszystko, co liczy skrót stanu) musi dawać ten sam wynik przy każdym uruchomieniu:

- bez `DateTime.Now`, `Stopwatch`, `Environment.TickCount`; czas symulacji to `Tick`, a czas rzeczywisty (sieć, zapis, limity czasu) tylko przez `TimeProvider` przekazany z zewnątrz;
- bez `Random` przy stosowaniu operacji; losowość tylko przy rozwiązywaniu intencji, a jej wynik trafia do operacji;
- bez iteracji po `Dictionary`/`HashSet`, gdy kolejność wpływa na wynik; używaj `SortedDictionary` albo jawnego sortowania;
- bez równoległości w stosowaniu operacji;
- bez czytania treści gry (przedmioty, receptury, pojemności, generatory) w warunkach wstępnych appliera: operacja niesie każdą wartość z treści, od której zależy jej stosowanie (np. limit stosu, pojemność zbiornika), więc ogon dziennika odtwarza się tak samo po aktualizacji treści;
- bez pytania terenu ani generatora przy stosowaniu: istnienie obiektów generatora i trafienia w teren sprawdza walidator, a wynik trafia do operacji;
- walidator intencji może czytać treść (`BlockContentTable`) i teren (`ITerrainService.TrySampleSolid`); applier `ShipModeSetOp` nigdy: warunki wstępne appliera to tylko stan świata, a wszystko z treści albo terenu trafia do operacji lub zostaje w walidatorze (K5-23);
- warunki wstępne `ShipFuelAddOp` nie czytają `ShipState` (poza skrótem): tylko budowla w trybie statku i poprawne wartości; pojemność zbiorników niesie operacja (K5-24);
- stan zmienia się tylko operacjami: także zdarzenia czasowe (odrastanie, rozliczenie stacji, znikanie przedmiotów) są operacjami serwera, a checkpoint zapisuje wyłącznie ich skutki;
- wartości zmiennoprzecinkowe w skrótach i formatach zapisywane bitowo; NaN i nieskończoności odrzucane na granicy (walidacja, deserializacja).

## 5. Styl C#

- Kod, identyfikatory i komentarze po angielsku; dokumentacja w `docs/` po polsku.
- Ustawienia z `Directory.Build.props`: `Nullable` włączone, ostrzeżenia jako błędy, analizatory `latest-recommended`, `InvariantGlobalization`.
- Przestrzenie nazw na poziomie pliku, jeden publiczny typ na plik, nazwa pliku = nazwa typu.
- Klasy `sealed`, chyba że dziedziczenie jest zaplanowane. Małe niemutowalne dane to `readonly record struct`, zamknięte hierarchie to `abstract record` z konstruktorem `private protected`.
- Pola prywatne `_camelCase`, stałe i publiczne składowe `PascalCase`, interfejsy z prefiksem `I`, metody asynchroniczne z sufiksem `Async`.
- Publiczne API w Core, Persistence i Net ma komentarze XML (`GenerateDocumentationFile` z ostrzeżeniami jako błędami to wymusza).
- Bez `async void` (wyjątek: handlery sygnałów Godota, które same łapią wyjątki), bez `.Result` i `.Wait()` na wątku gry.

## 6. Wydajność w pętlach ticku

W kodzie wołanym co tick (symulacja, sieć, meshing) obowiązuje:

- brak alokacji w stanie ustalonym: `Span<T>`, `stackalloc` dla małych buforów, `ArrayPool<T>` dla dużych;
- brak LINQ, lambd przechwytujących zmienne i boxingu;
- kolekcje alokowane raz i czyszczone (`Clear()`), nie tworzone od nowa;
- test alokacji (`GC.GetAllocatedBytesForCurrentThread`) dla każdej gorącej ścieżki, którą plan pakietu tak oznacza.

## 7. Błędy i logowanie

- Walidacja danych gry zwraca wynik (`RejectReason`, `ValidationResult`), nie rzuca wyjątków.
- Wyjątki tylko dla złamanych niezmienników (błąd w naszym kodzie) i dla uszkodzonych danych z sieci albo dysku (`ProtocolViolationException`, łapany na granicy połączenia).
- Kolejność zamykania serwera: `Shutdown` sesji → `Poll` do opróżnienia → `OnShutdown` uczestników (K2-11 `DespawnAll`) → `LeaveAllOnlinePlayers` → checkpoint → `FlushAsync` → `DisposeAsync` writera → `VoxelTerrainService.ShutdownAsync` (zamyka strumienie wszystkich aktywnych węzłów planet, K5-22) → `ServerTerrainBlocks.CloseAsync` → (`true`) `TerrainBlockStore.Close()` → krok 7a: zadania systemów gry po terenie (K2-13: `ConstructWorld.ShutdownAsync`, `ServerRuntime.AddAfterTerrainShutdown`) → `Dispose` transportu → klient lokalny.
- Każde wyjście z procesu (zamknięcie okna, SIGINT/SIGTERM/SIGQUIT, polecenie `stop`) przechodzi przez `QuitCoordinator` i tę samą kolejność zamykania; handler sygnału tylko ustawia flagę.
- Nigdy nie połykaj wyjątków. `catch` bez ponownego rzucenia musi zalogować błąd i mieć komentarz, dlaczego to bezpieczne.
- Logowanie przez `ILogger<T>` (`Microsoft.Extensions.Logging.Abstractions`), wyłącznie metodami generowanymi atrybutem `[LoggerMessage]`: nie alokują w gorących ścieżkach, a bezpośrednie `logger.LogX(...)` łamie regułę CA1848 przy ostrzeżeniach jako błędach. W `game/` adapter pisze do pliku z rotacją i do konsoli Godota. Każdy wpis o sesji ma identyfikator gracza, a o operacji jej `OpId`.

## 8. Testy

- TDD: test akceptacyjny pakietu powstaje przed kodem, a każda poprawka błędu zaczyna się od testu, który ten błąd pokazuje.
- xUnit; nazwy `Metoda_Scenariusz_OczekiwanyWynik`, po angielsku. Testy właściwości w FsCheck.
- xUnit v3 przez paczkę `xunit.v3.mtp-off` (z `xunit.runner.visualstudio` i `Microsoft.NET.Test.Sdk`), a właściwości przez `FsCheck.Xunit.v3`. SDK .NET 10 uruchamia projekty Microsoft Testing Platform tylko w trybie MTP, który nie zna składni `--filter "Category!=Soak&Category!=Manual"`; wariant `mtp-off` zostawia VSTest i komendy z punktu 10. Projekt testów ma `<Using Include="Xunit" />`.
- Testy są deterministyczne: bez `Thread.Sleep` i realnego czasu; czas przez `FakeTimeProvider` (paczka `Microsoft.Extensions.TimeProvider.Testing`, dostępna przez `tests/Horyzont.Testing`), sieć przez `InMemoryTransport` z `Horyzont.Net`, teren przez `FakeTerrainService` z `tests/Horyzont.Testing`.
- Prawdziwy czas wolno mieć tylko w testach transportów UDP i Steam oraz w testach `Soak`; każdy taki test ma jawny limit czasu.
- Kategorie przez `[Trait("Category", ...)]`: bez kategorii (szybkie, zawsze w CI), `Slow` (CI), `Soak` (nocne), `Manual` (wymagają człowieka albo Steama; wynik wpisuje się do PR).
- Pakiet nie jest skończony, dopóki jego testy akceptacyjne nie przechodzą w CI.

## 9. Git i przegląd

- Gałąź na pakiet: `pakiet/k1-03-core` (identyfikator pakietu małymi literami plus krótka nazwa).
- Commity małe, w formacie Conventional Commits po angielsku: `feat:`, `fix:`, `test:`, `refactor:`, `perf:`, `docs:`, `build:`, `ci:`, `chore:`.
- Jeden PR na pakiet. Opis PR: co zrobiono, wynik testów akceptacyjnych, odstępstwa od planu, otwarte pytania.
- Scalanie tylko z zielonym CI i po przeglądzie według reguł z planu projektu: pakiet Opus → drugi przegląd GPT-6.1 Sol; pakiet GPT-6.1 Sol → Opus 5.5; pakiet Sonnet 5.5 → Opus 5.5 przy zmianie kontraktów, inaczej na bramce; pakiet Gemini 3.8 Flash → Sonnet 5.5.
- Historia liniowa: squash albo rebase.

## 10. Komendy

```
dotnet build Horyzont.sln
dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"
dotnet format Horyzont.sln --verify-no-changes
python tools/engine/build.py all        # pełne budowanie silnika (K1-01)
python tools/engine/build.py smoke      # sam test dymny silnika
```

## 11. Definicja ukończenia pakietu

- [ ] Testy akceptacyjne z planu pakietu przechodzą w CI.
- [ ] Przegląd według reguł z punktu 9, uwagi rozwiązane.
- [ ] Brak `TODO` bez odnośnika do zgłoszenia.
- [ ] Checklista w planie pakietu odhaczona, a odstępstwa opisane w planie.
- [ ] Nowe zasady wynikłe z pakietu dopisane do tego pliku.

## 12. Eskalacja

Po dwóch nieudanych podejściach do tego samego testu wykonawca przekazuje zadanie wyżej (Gemini 3.8 Flash → Sonnet 5.5 → GPT-6.1 Sol albo Opus 5.5) i załącza: test, log, opis prób. Zmiana kontraktu chronionego zawsze trafia do Opus 5.5.
