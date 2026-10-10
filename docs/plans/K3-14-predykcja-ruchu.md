# K3-14 · Predykcja, korekta i interpolacja ruchu

| | |
| --- | --- |
| Prowadzi | Opus 5.5 |
| Testy akceptacyjne | Opus 5.5, przed implementacją; podzadania mechaniczne Gemini 3.8 Flash |
| Przegląd | GPT-6.1 Sol; każda ewentualna zmiana Core także Opus 5.5 |
| Zależy od | K3-00 (szwy); K1-07, K2-11 (graf z planu projektu); integracja pełnych migawek i zdalnych widoków z K3-15 na bramce K3, bez dodatkowej zależności startu |
| Gałąź | `pakiet/k3-14-predykcja-ruchu` |

**Cel:** przy RTT 100 ms i 5% utraty wejść/migawek własna postać reaguje natychmiast, korekta małego błędu nie przeskakuje wizualnie, duży błąd przestawia postać od razu, a inni gracze są interpolowani z buforem 100 ms.

## Wspólne ustalenia K3

Domyślnie start po małym PR [K3-00 szwy](K3-00-szwy.md) na bramce K2. Obowiązują [README: X1–X11](README.md#x1-szwy-przed-startem-pakietów): jeden dispatch S1, budżety S4, pełne migawki S5, readiness S6, viewers S7, narzędzia S8 i własne partial S9. Rekomendowane scalanie 15 → 16 → 14 → 17 → 18; integrację wspólnego punktu wykonuje pakiet scalany później, kolejność nie warunkuje startu. Bez odpowiedzi właściciela pakiet rusza z wariantem domyślnym.

## Kontekst

- `docs/ZASADY.md`: pkt 2 (autorytet serwera, tylko ruch własny przewidywany, lokalna rama i `PlanetFrames`), 3 (kontrakty chronione), 4 (czas i determinizm), 6 (gorące ścieżki), 8 (TDD i kontrolowany czas).
- `docs/spec/01-planeta.md:190–240`: kanały (194), sesja (210–216), migawki 20 Hz (220), replay (221), interpolacja 100 ms (222), zainteresowanie 500 m (236), jedno wejście na tick (238), 50 KB/s (240).
- `docs/spec/01-planeta.md:294–314`: mały/duży błąd (305), timeout 10 s i nowa sesja (306), obsługa niepoprawnych pakietów (301).
- `docs/spec/01-planeta.md:316–344`: test sieci (327), testy botów (328–330), kryteria płynności, 8 graczy i ticku (338–343).
- `docs/plan/plan-projektu.md:95–99`: pięć niezależnych pakietów K3 i ich zależności. Numerację linii sprawdzono w gałęzi `pakiet/k2-bramka`; przy rebase zweryfikować położenie tabeli.
- `docs/plans/README.md:78–80`: D1/D4/D8. `K2-11-postac-i-kamera.md:70,295,354,365,426,481`: odroczenie wygładzania/interpolacji, sekwencje i powtórzenia, P4, test łącza oraz ograniczenia walidatora pełnej migawki.
- `docs/plans/K1-03-core-stan-i-operacje.md:533`: także budżet migawek i priorytet odległości przypisano K3-14; realizacja poniżej dostarcza politykę dla nadawcy K3-15.

### Kod, na którym pakiet buduje

Stan tej gałęzi, nazwy i linie sprawdzone w kodzie; plany K2 opisują również historyczne API, których nie należy kopiować.

| Co | Gdzie |
| --- | --- |
| `PredictionBuffer.Capacity = 128`, `Record(uint, in MotorInput, in PredictedState)`, `Acknowledge(uint, in PredictedState)`, `CopyUnacknowledged(Span<PredictionEntry>)`, `ReplaceUnacknowledgedStates(ReadOnlySpan<PredictedState>)`, `Clear()` | `src/Horyzont.Client/Movement/PredictionBuffer.cs:15` |
| `CorrectionPolicy.SnapDistance = 0.02f`, `VelocityTolerance = 0.1f`, `MaxUnacknowledged = 120`, `NeedsCorrection` | `src/Horyzont.Client/Movement/CorrectionPolicy.cs:10` |
| `OwnState` (m.in. `Planet`, `ParentSeq`, `ServerTick`, `LastProcessedInputSeq`), `ToPredicted(float pitch, bool onFloor)` | `src/Horyzont.Client/Movement/OwnState.cs:18` |
| `OwnStateValidator.Validate(Snapshot, out OwnState)`, `NoteSent(uint)`, `FromHello(ServerHello, ILogger<OwnStateValidator>)` | `src/Horyzont.Client/Movement/OwnStateValidator.cs:82` |
| `InputSequencer.Record(uint clientTick, in MotorInput)`, `TryTakeBatch(out uint, out IReadOnlyList<PlayerInput>)`; 2 nowe + 3 powtórzenia | `src/Horyzont.Client/Movement/InputSequencer.cs:55` |
| `LocalPlayer.ProcessSnapshot()`, `Replay(OwnState)` i `UpdateCamera`; dziś każda korekta ustawia `_snapCamera = true` | `game/src/Client/Character/LocalPlayer.cs:387`, `:451`, `:519` |
| `PlayerBody.Place(Vector3, Vector3, Quaternion, MovementMode, bool)`, `Step(in MotorInput)`, `SetVisualVisible(bool)` | `game/src/Shared/Character/PlayerBody.cs:118`, `:139`, `:104` |
| `CameraRig.ResolveArm`, `Apply(PlanetId, Vector3, Vector3, Vector3, bool teleport)` | `game/src/Client/Character/CameraRig.cs:69`, `:100` |
| `PlanetFrames.ToPlanetLocal`, `ToGodot`, `ToNumerics` | `game/src/Shared/Frames/PlanetFrames.cs:24` |
| `ClientRuntime.AddListener`, `IsHostLocal`; `IClientSessionListener` | `src/Horyzont.Client/Session/ClientRuntime.cs:49`, `:42`; `IClientSessionListener.cs:7` |
| `OwnStateSender.Send(Tick)`, `SendEveryTicks = 3`, pełna własna migawka | `game/src/Server/Characters/OwnStateSender.cs:49`, `:16` |
| `ServerSession.GetRtt(PlayerId)`, `Interest`; `InterestManager.IsVisible(PlayerId, EntityId)` | `src/Horyzont.Net/Session/ServerSession.cs:207`, `:80`; `InterestManager.cs:131` |
| `LinkConditions(Latency, Jitter, LossRate, DuplicateRate, Seed)`; `Latency` jest jednokierunkowa | `src/Horyzont.Net/Transport/InMemory/LinkConditions.cs:10` |
| `ProtocolVersion = 3`, ramka zawodna najwyżej 1150 B | `src/Horyzont.Core/Protocol/ProtocolConstants.cs:7`, `:16` |

## Zakres

- Zachowanie replay K2 i oddzielenie natychmiastowej korekty fizyki od wygładzania prezentacji postaci/kamery.
- Interpolacja pełnych, zrekonstruowanych stanów innych graczy; ograniczone zachowanie przy brakujących próbkach, zmiana ramy i despawn.
- Polityka budżetu/prioritetu odległości migawek, konsumowana przez K3-15; diagnostyka płynności dla testów i K4-19.
- Deterministyczny test łącza oraz testy Godota dla kamery, fizyki i prezentacji.

## Poza zakresem

- Kompresja różnicowa, historia baseline, `SnapshotAck`, spawn/despawn, budowanie pełnych migawek i zainteresowanie: K3-15. Nie zastępować tu `OwnStateSender`.
- Teren i jego predykcja: K3-16; Steam: K3-17; HUD/menu: K3-18. Walkę, kompensację trafień i ramy statków prowadzą późniejsze pakiety.
- Nowa konwencja yaw, zmiana ruchu serwera, protokołu wejść lub formatu operacji. Nie ma potrzeby zmieniać Core dla samego wygładzania.

## Projekt

### Pliki

Nowe API w tej sekcji są projektowane, nie istnieją w K2.

| Plik | Zmiana |
| --- | --- |
| `src/Horyzont.Client/Movement/PresentationCorrection.cs` (nowy) | Stan przesunięcia i obrotu prezentacji, bez wpływu na fizykę |
| `src/Horyzont.Client/Movement/RemoteMotionSample.cs`, `RemoteMotionBuffer.cs`, `RemoteMotionPolicy.cs`, `MotionStatistics.cs` (nowe, po jednym typie) | Ring 32 próbek/bytu, interpolacja, liczniki |
| `game/src/Client/Character/LocalPlayer.cs`, `CameraRig.cs` | Korekta przed/po replay, aktualizacja prezentacji i poprawna kolizja ramienia TPP |
| `game/src/Client/Character/RemotePlayerPresentation.cs` (nowy) | Wyłącznie wizualny `Node3D`, bez `CharacterBody3D` i symulowania cudzych wejść |
| `game/src/Shared/Character/PlayerBody.cs` | Wewnętrzny punkt zastosowania pozy wizualnej własnej postaci; kapsuła zostaje w pozycji przewidywanej |
| `src/Horyzont.Server/Replication/SnapshotSchedulingPolicy.cs`, `SnapshotCandidate.cs` (nowe) | Polityka wyboru dla K3-15, bez kodeka i transportu |
| `tests/Horyzont.Client.Tests/Movement/PresentationCorrectionTests.cs`, `RemoteMotionBufferTests.cs`, `MotionLinkTests.cs` (nowe) | Czyste testy |
| `tests/Horyzont.Server.Tests/Replication/SnapshotSchedulingPolicyTests.cs` (nowy) | Budżet, priorytet i brak głodzenia |
| `game/tests/Character/MotionPresentationTests.cs` (nowy) | Kamera, replay, niezależna prezentacja |
| `game/src/Boot/GameComposition.Motion.cs` | Rejestracja prezentacji i polityki w szwie S9 |
| `src/Horyzont.Server/Replication/MotionSnapshotBudget.cs` (nowy) | `ISnapshotBudget` z S4: wiadro 20 480 B/s, pojemność 2 300 B, `TimeProvider`; `GetByteBudget` ≤ 1 024 B zwykle, 1 150 B dla wymuszonej pełnej |
| `tests/Horyzont.Server.Tests/Replication/MotionSnapshotBudgetTests.cs` (nowy) | Rozliczenie rzeczywistych bajtów i czasu |
| `src/Horyzont.Client/Movement/RemoteMotionSampleAdapter.cs` (nowy) | `int Extract(Snapshot full, EntityId own, Span<RemoteMotionSample> into)`; pomija własny byt i byty bez pełnych pól; rejestrowany jako `IFullSnapshotSink` |

### Kontrakty wystawiane i konsumowane

1. Nowy `PresentationCorrection`: `void Reconcile(in PredictedState before, in PredictedState after, bool discontinuity)`, `void Advance(float deltaSeconds)`, `Vector3 PositionOffset { get; }`, `Quaternion RotationOffset { get; }`, `void Reset()`. `before/after` to bieżący stan przed i po **całym** replay, nie stary stan z potwierdzonego ticku. `Reconcile` zachowuje poprzednią widoczną pozę także przy kolejnej korekcie w czasie wygaszania.
2. Nowy `RemoteMotionSample(EntityId Entity, FrameRef Frame, uint ParentSeq, Tick ServerTick, Vector3 Position, Quaternion Rotation, Vector3 Velocity, byte MotionFlags)`; `RemoteMotionBuffer.Push(in RemoteMotionSample) → bool`, `TrySample(double serverTick, out RemoteMotionSample) → bool`, `Clear()`. Bufor nie rozwiązuje delty protokołu. K3-15 przekazuje pełny stan po swojej rekonstrukcji; K3-14 nie interpretuje nieobecnych pól jako zera.
3. Nowy `RemotePlayerPresentation.SetSample(in RemoteMotionSample)`; composition root K3-15 tworzy/usuwa go przy zdarzeniach bytu. W testach K3-14 pełne próbki mogą pochodzić z fixture. Żaden pakiet nie tworzy drugiej lokalnej postaci.
4. Granica uzgodnienia z równoległym planem K3-15: jego **nowy, planowany** `SnapshotDecoder.TryReconstruct(Snapshot delta, out Snapshot full)` zwraca semantycznie pełny `Snapshot` z baseline 0. Ten sam wynik trafia pojedynczą ścieżką do istniejącego `OwnStateValidator.Validate(Snapshot, out OwnState)` i do adaptera `RemoteMotionSample`; nie dodawać drugiego dekodera ani przeciążenia walidatora dla surowych delt. Zachować kontrolę ramy, własnego bytu, sekwencji i skończoności; przy pominiętym własnym bycie nie generować korekty. `OwnStateValidator.cs` edytuje wyłącznie K3-15; K3-14 go nie zmienia. Pełne migawki docierają do `LocalPlayer` przez `IFullSnapshotSink` (S5).
5. Nowy `SnapshotCandidate(EntityId Entity, float Distance, int EncodedBytes, int OmittedRounds, bool IsOwnPlayer)` i `SnapshotSchedulingPolicy.Select(ReadOnlySpan<SnapshotCandidate> candidates, Span<int> selectedIndices, int byteBudget) → int`. `EncodedBytes` obejmuje rzeczywisty wkład pól i ewentualnej ramy po kodowaniu K3-15; nadawca odejmuje nagłówek i weryfikuje całą ramkę. Wyjściowe indeksy wskazują kolejność wyboru. Nie używać LINQ ani sortowania alokującego.
6. Nowy `MotionStatistics` jako niemutowalny odczyt liczników korekt małych/dużych, maksimum przesunięcia prezentacji, liczby niedoborów próbek i bieżącego opóźnienia interpolacji. `MotionStatistics` udostępnia `LocalPlayer.Motion`; K3-18 w K3 go nie wyświetla (`HudSample` nie ma pól ruchu), odczyt służy testom i K4-19. Render nie formatuje tekstu co tick. Zastąpić tworzenie `LastCorrection` przy każdym replay licznikami; tekst diagnostyczny budować na żądanie.

Integracja z równoległym planem K3-15: `ISnapshotBudget.GetByteBudget(PlayerId, Tick)` z K3-00 (S4) konsumuje token bucket K3-14 przez nowy adapter `MotionSnapshotBudget` w `src/Horyzont.Server/Replication/MotionSnapshotBudget.cs`; koszt faktycznie wysłanej ramki rozlicza nowa metoda adaptera `Debit(PlayerId player, int encodedBytes)`. `SnapshotBuilder` z K3-15 wywołuje politykę selekcji i adapter. Podstawowe 1150 B K3-15 jest fallbackiem przed integracją, nie konkurencyjnym finalnym limitem. Inni gracze nie mają u klienta ciał fizycznych: serwer nie koliduje graczy ze sobą (`PlayerBody.CollisionMask = TerrainMask | ConstructsMask`), więc proxy kolizji powodowałoby korekty. K3-14 posiada wizualną pozę (`RemotePlayerPresentation`), K3-15 wyłącznie lifecycle węzła przy `EntitySpawn`/`EntityDespawn`.

### Decyzje

- **Korekta:** tolerancje fizyki pozostają 0,02 m / 0,1 m/s / zmiana trybu. Po `AckOutcome.Correct` kapsuła natychmiast wraca do serwera i powtarza niepotwierdzone wejścia. Nie podnosić `SnapDistance` do progu „dużego błędu”.
- **Prezentacja:** błąd bieżącej pozy po replay < 1 m i obrotu < 45° wygasza się liniowo do zera w 100 ms. >= 1 m, >= 45°, brak historii ACK, zmiana ramy/`ParentSeq`, teleport, inicjalizacja lub `Frozen` powodują `Reset` i natychmiastową zmianę. Granice dokładne, bez arbitralnej tolerancji. Offset nie zmienia `PredictedFeet`, `BodyRotation`, `Eye` ani promienia intencji. Kamera ma przesunięcie wizualne; TPP ponownie ogranicza ramię promieniem od fizycznego oka, aby nie przechodzić przez ścianę.
- **Kamera:** mała korekta nie ustawia `_snapCamera`; nie resetuje interpolacji Godota ani kierunku myszy. Poza fizycznego ciała służy ruchowi i celowaniu, poza prezentacji służy widocznej siatce i kamerze. Nie stosować wygładzania dwa razy (własny offset plus automatyczna interpolacja całego `PlayerBody`). FPP/TPP i bieguny mają testy.
- **Historia:** zachować 128 wpisów, pauzę przy 120 i maksymalnie 128 kroków replay. Podczas pauzy wejścia nadal mają ciągłą numerację i idą do serwera. ACK spoza historii oznacza snap; po rozłączeniu wyczyścić historię i prezentację. Nie zwiększać bufora w reakcji na dłuższy RTT.
- **Czas innych graczy:** próbka renderowana dla ticku serwera minus 6 ticków (100 ms przy 60 Hz). Zegar nowej sesji startuje z `ServerHello`, jest prowadzony przez wstrzyknięty `TimeProvider`; próbki korygują fazę bez cofania kursora renderu. Nie zakładać RTT = opóźnienie jednokierunkowe. Korektę fazy ograniczyć do 1 ticku/s; błąd > 30 ticków resetuje zegar i bufory.
- **Interpolacja:** pozycja `Vector3.Lerp`, obrót najkrótszym łukiem `Quaternion.Slerp`, normalizacja; bez Hermite overshoot. W obrębie jednej ramy i `ParentSeq`. Starsze/duplikowane ticki odrzucać. Zmiana rodzica czy odległość > 3 m między sąsiednimi próbkami resetuje ring (dev lot > 3 m/50 ms dopuszczać według maksymalnej prędkości trybu, nie uznawać każdego szybkiego lotu za teleport).
- **Brak próbek:** najwyżej 3 ticki (50 ms) ekstrapolacji ze znanej prędkości; potem trzymać ostatnią pozę i zwiększać licznik. Brak własnego bytu w częściowej migawce nie jest korektą ani despawn. Przy `EntityDespawn` K3-15 usuwa ring i widok; przy respawn ring jest nowy.
- **Budżet:** normalny limit migawek 20 480 B/s/klienta (X3), najwyżej 1024 B całej ramki przy 20 Hz, nigdy > 1150 B. Własna postać pierwsza; następnie inne rosnąco według klucza `clamp(distance, 1 m, 550 m) / (1 + omittedRounds)` (mniejszy wcześniej), remis po `EntityId`; byt pominięty co najmniej 10 rund z rzędu jest wybierany bezwarunkowo zaraz po własnym, malejąco po `omittedRounds`, potem `EntityId`; pominięcie zwiększa wiek, wysłanie zeruje. Wymuszone pełne ramki mogą użyć 1150 B, ale rozliczane są w token bucket 20 480 B/s (X3) z pojemnością 2300 B. K3-15 odpowiada za selekcję, actual bytes i ACK; K3-14 za politykę oraz testy. Wspólny cel 50 KB/s jest mierzony na bramce z K3-16.
- **Chronione:** domyślnie brak zmian Core i kodów. Jeśli integracja wymaga zmiany `Protocol/` lub `Serialization/`, zatrzymać ten krok do przeglądu Opus i podbić `ProtocolVersion = main w chwili scalenia + 1` (D4; K2 kończy z 3); aktualizować test i opis po rebase. Zmiana operacji/intencji wymaga ich formatu i replay starej wersji, nie samego protokołu. Nie zmieniać `SurfaceFrame`. Jeśli K3-14 jednak zmieni Protocol/Serialization, stosuje X4–X6 (istniejący test `ProtocolVersion_MatchesDocumentedMergeValue`, `Constants_GoldenValues`, fixture protokołu i routera).

### Odroczenia przejęte

| Źródło | Rozstrzygnięcie |
| --- | --- |
| K2-11:70,108,328,354, P4:365 | `PredictionBuffer` pozostaje; małe korekty wygładza `PresentationCorrection`, zdalny ruch ring 100 ms |
| K2-11:426 | Nowy test RTT rozróżnia RTT 100 ms od starego określenia „opóźnienie 100 ms”; powtórzyć także wariant 100 ms **w każdą stronę** jako test odporności, bez wymagania tego samego wyniku wizualnego |
| K2-11:481 | Pełne migawki z nowego dekodera K3-15 przekazywane do obecnej metody walidatora, bez dublowania dekodera delty |
| K1-03:533 | Polityka budżetu i priorytetu należy tu; jej wpięcie do pełnego nadawcy jest krokiem integracji K3-15 |

### Równoległość i mapa kolizji

Po K3-00 K3-14, K3-15, K3-16, K3-17 i K3-18 mogą rozpocząć się równolegle po swoich zależnościach K2. Zależność integracyjna nie zmienia grafu startu.

| Pakiety / pliki wspólne | Reguła scalania |
| --- | --- |
| 14/15: ruch i kompozycja | `LocalPlayer.cs`: wyłącznie K3-14 (po S5–S7). `OwnStateValidator.cs`: wyłącznie K3-15. Kompozycja: K3-14 tylko `GameComposition.Motion.cs`. Integrację pakietu scalanego później wykonuje ten pakiet (X10); jedna ścieżka korekty |
| 14/15: budżet nadawcy | 14 posiada `SnapshotSchedulingPolicy`; 15 posiada swoją implementację nadawcy. Test polityki samodzielny, test actual bytes po integracji |
| 14/16: widzowie i gotowość | K3-16 czyta widzów przez `ITerrainViewerSource` (S7) i gotowość przez `IClientWorldReadiness` (S6); nie edytuje `LocalPlayer.cs` |
| 14/17/18: kompozycja i statystyki | Własne pliki partial S9; 18 nie wyświetla `MotionStatistics` w K3, 17 nie dotyka ruchu |
| Wszystkie: CI i gdUnit4 | Jeden self-hosted runner Windows; jedna stała pipe gdUnit4, bez równoległych testów silnika. Linux CI wyłączone decyzją 2026-10-09 |

## Testy akceptacyjne (napisz najpierw)

Fixture xUnit: `FakeTimeProvider`, `InMemoryTransport`, 60 Hz fizyki/20 Hz stanów; `LinkConditions` 50 ms w każdą stronę, 5% `LossRate`, seedy 14, 140, 1400; osobny wariant jitter 0–10 ms i duplikacja 1%. Rozgrzać ścieżkę przed pomiarem alokacji. Integracja Godota jest seryjna; testy używające prawdziwego zegara silnika oznaczyć `Slow` z limitem 60 s i opisać wyjątek od kontrolowanego czasu (nie używać `Thread.Sleep`).

| Test | Scenariusz → oczekiwany wynik |
| --- | --- |
| `Reconcile_SmallError_PhysicsCorrectsAndPresentationStaysContinuous` | 0,2 m błędu; fizyka poprawna natychmiast, pierwszy offset zachowuje widoczną pozę, po 100 ms zero |
| `Reconcile_RepeatedCorrection_PreservesCurrentVisualPose` | Druga korekta po 40 ms; bez skoku i bez kumulacji przesunięcia fizyki |
| `Reconcile_LargeErrorOrFrameChange_SnapsImmediately` | 1 m, 45°, nowy `ParentSeq`, `Frozen`, brak historii; offset zero, reset interpolacji |
| `Replay_UnacknowledgedInputs_MatchesImmediateAuthoritativeReplay` | Ta sama skwantowana historia, 1..128 wpisów; identyczna poza fizyki i ciągła numeracja |
| `Acknowledge_StaleOrMissingHistory_DoesNotReplayTwice` | Stary ACK ignorowany, brak wpisu powoduje pojedynczy snap, pauza nie przepełnia ring |
| `TrySample_TwoSamples_InterpolatesAtSixTickDelay` | Ticki 100/103; kursor 107, próbka tick 101; liniowa poza i najkrótszy obrót |
| `TrySample_PacketGap_ExtrapolatesForThreeTicksThenHolds` | Utrata 4 migawek; limit 50 ms, potem zamrożona poza, bez nieograniczonego dryfu |
| `Push_StaleTickOrParentChange_DropsOrResetsSamples` | Duplikat, stary tick, nowe `ParentSeq`, respawn; żadnego mieszania ram |
| `AdvanceClock_JitterAndDelayedSnapshot_NeverMovesRenderTimeBackwards` | 10 ms jitter, stare pakiety i dryf; monotoniczny kursor, bufor ograniczony do 32 |
| `Move_Rtt100msAndFivePercentLoss_ConvergesWithoutCorrectionJumps` | 60 s marszu/sprintu/skoku i zatrzymanie; po zatrzymaniu błąd fizyki < 1 cm w ≤ 2 dostarczone migawki; przy małych korektach skok prezentacji wynikający z korekty ≤ 2 cm/klatkę, zero nieuzasadnionych resetów |
| `UpdateCamera_CorrectionNearWall_DoesNotCrossWallOrChangeAimEye` | FPP i TPP, ściana, biegun; kamera bez przenikania, `Eye` i intencja według fizyki |
| `Disconnect_Reconnect_ClearsPredictionAndInterpolation` | Nowa sesja; brak dawnych ACK, offsetów, próbek i zegara |
| `Select_TightBudget_PrioritizesOwnAndEventuallySendsFarEntities` | własny + 7 innych, budżet mieszczący własny + 2 inne, odległości 1–500 m; każdy byt wysłany co najmniej raz w każdym oknie 20 rund; remis deterministyczny; ramka ≤ limitu |
| `AdvanceAndSample_WarmedUp_AllocatesZeroBytes` | 10 000 wywołań wygaszania/próbkowania; 0 B `GC.GetAllocatedBytesForCurrentThread` |
| `Select_WarmedUp_AllocatesZeroBytes` | 8 i 22 kandydatów; 0 B; adapter migawek rozlicza alokacje osobno w K3-15 |
| `Host_PerfectLink_HasNoSpuriousCorrections` | Host jedna fizyka terenu; brak nowych korekt względem K2 |

## Kroki

- [ ] 0. Rebase na scalone K2 i K3-00; sprawdzić nazwy oraz zastosować szwy S4–S7 i własność plików według X10. Nie zmieniać cudzych testów akceptacyjnych bez zgody.
- [ ] 1. Testy czyste prezentacji i ringów, następnie implementacja; mały commit Conventional Commits po zielonym kroku.
- [ ] 2. Testy replay/pauzy/ACK; adapter pełnej migawki do próbek, bez regresji `Validate(Snapshot, ...)`.
- [ ] 3. Testy polityki budżetu, implementacja bez alokacji i API dla K3-15.
- [ ] 4. Testy Godota, odseparować siatkę/kamerę od kapsuły; zdalny widok tylko wizualny. Sprawdzić FPP/TPP, bieguny i ściany.
- [ ] 5. Test łącza, pomiary płynności i alokacji; raport seedów, błędów i niedoborów próbek. Po dwóch nieudanych próbach tego samego testu eskalować z testem i logiem zgodnie z ZASADY pkt 12.
- [ ] 6. Po scaleniu K3-15 (albo w K3-15, jeśli scala później — X10) powtórzyć scenariusz na prawdziwych migawkach delta/spawn/despawn i sprawdzić actual bytes. Bez tej integracji nie ogłaszać kompletnego odbioru co-op.
- [ ] 7. Pełne `dotnet build Horyzont.sln`, `dotnet test Horyzont.sln --filter "Category!=Soak&Category!=Manual"`, `dotnet format Horyzont.sln --verify-no-changes`, `python tools/engine/build.py smoke`; seryjne gdUnit4 na Windows. Wyniki i ograniczenia do PR; zielone CI po scaleniu.

## Odbiór

- Wszystkie testy z tabeli zielone, RTT 100 ms/5% utraty spełnia liczbowy próg i ręczna ocena FPP/TPP (`Manual`, 5 min, Windows) nie wykazuje widocznych skoków małych korekt.
- Duży błąd pozostaje natychmiastową korektą; fizyka i narzędzia nigdy nie czytają wygładzonego oka.
- K3-15 używa tej polityki i przekazuje zrekonstruowane próbki; ramki mieszczą się w limicie i budżecie.
- Raport build/test/format/smoke, pomiary alokacji i przegląd Sol w PR. Linux: `VALIDATION_NOT_PERFORMED`, nie wymagać wyłączonego joba ani potwierdzać zgodności między systemami bez pomiaru.

## Odstępstwa i decyzje wykonawcy

## Pułapki

- Stary `OwnStateValidator` odrzuca baseline != 0; podanie mu surowej delty z K3-15 zepsuje predykcję.
- `LastCompared` dotyczy przeszłości; offset policzony z niego przeniesie klienta wstecz zamiast zachować dzisiejszą widoczną pozę.
- Utrata niezawodnej informacji o spawn/despawn nie jest symulowana przez `LossRate` łącza; test osobno wstrzymuje kanał i kontroluje kolejność.
- Przesunięcie całego `PlayerBody` dla wygładzania zmieni kolizje; zdalny widok z kapsułą uruchomi podwójną fizykę.
- Inni gracze są wyłącznie wizualni; ciała fizyczne/proxy kolizji dawałyby rozjazd z serwerem (X9).
- Dwie implementacje budżetu u 14 i 15 albo dwa kursory czasu dadzą różne wyniki mimo zielonych testów jednostkowych.

## Pytania do właściciela

Warianty domyślne obowiązują przy starcie bez odpowiedzi (README:42); sprzeczność ze specyfikacją wymaga eskalacji, nie wyboru wariantu przez wykonawcę.

| ID | Pytanie | Domyślny wariant / alternatywa |
| --- | --- | --- |
| P1 | Czy próg dużej korekty 1 m / 45° i wygaszanie 100 ms przyjmujemy na K3? | Tak; alternatywa: zmiana wartości po raporcie, bez zmiany tolerancji fizyki 2 cm |
| P2 | Czy przy dłuższej luce preferujemy zatrzymanie cudzej postaci po 50 ms ekstrapolacji? | Tak; alternatywa: sam hold bez ekstrapolacji, nadal bufor 100 ms |
| X1 | Czy przyjmujemy K3-00 przed równoległym startem pakietów? | Domyślnie tak: S1–S9 na bramce K2 bez Core/protokołu; alternatywa po odrzuceniu: X10 „integruje pakiet scalany później”, K3-14 krok 6 i K3-16 krok 6 dopiero po scaleniu K3-15 |
| X3 | Czy przyjmujemy wspólny podział pasma? | Domyślnie migawki 20 480 B/s / 2 300 B, niezawodne wspólne 27 000 B/s / 65 536 B, sterowanie poza budżetem (~2,5 KB/s); alternatywa: strojenie podziału po raporcie actual bytes, limit ≤50 000 B/s, bez osobnego wiadra terenu |
