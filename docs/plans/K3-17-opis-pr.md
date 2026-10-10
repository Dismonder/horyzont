# K3-17 — opis PR (Steam: lobby i zaproszenia)

Status: blokada kroku 0 ROZSTRZYGNIĘTA przez szew S10 w K3-00 (ICompositionPlatform, PlatformSession, ReleaseTrackedTransport, GameEnvironment.ProcessCleanup, wczesny `static partial ConfigurePlatform` oraz instancyjny `partial ConfigurePlatform()`). K3-17 nie edytuje `GameComposition.cs`; Steam jest składany w `GameComposition.Platform.cs` i plikach pakietu.

## Co zrobiono

Uzupełniane na bieżąco w trakcie realizacji; stan końcowy w sekcji „Weryfikacja”.

## Historia blokady (zamknięta)

Pierwszy wykonawca zatrzymał się na kroku 0 (NEEDS_DECISION), bo S9 `ConfigurePlatform()` wywoływał się po utworzeniu ServerRuntime/ClientRuntime i nie było szwów wyboru transportu/listenerów, startu połączenia platformowego ani lifecycle. Decyzja właściciela K3-00 (Opus 5.5): dodać S10 w K3-00 zamiast wyjątku od X10. Szczegóły: [K3-00-szwy.md](K3-00-szwy.md), sekcja S10.

## Weryfikacja

Patrz końcowy raport wykonawcy w opisie PR (komendy i wyniki). Godot, gdUnit i smoke silnika nie były uruchamiane lokalnie (ograniczenie środowiska); Manual Steam i przegląd Opus (reguła tożsamości w `HandshakeValidator`) pozostają pending.
