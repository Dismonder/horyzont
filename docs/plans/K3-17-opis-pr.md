# K3-17 — blokada szwów kompozycji

Status: NEEDS_DECISION. Implementacja i testy akceptacyjne nie rozpoczęte; krok 0 ujawnił sprzeczność pomiędzy obowiązkową integracją Steam a własnością plików X10.

## Dowody

- `GameComposition.cs:255`: host dostaje zamknięty `CompositeTransport([local, udp])`; lista listenerów jest lokalna dla `Create`, a ServerRuntime powstaje w linii 291.
- `GameComposition.cs:320`: zdalny klient zawsze dostaje UdpTransport; ClientRuntime powstaje w linii 323.
- `GameComposition.cs:361`: jedyny szew platformy jest wywoływany po utworzeniu obu runtime'ów. Pola transportów są readonly; dodanie własnego binder nie zmieni transportu istniejących sesji.
- `GameComposition.cs:521`: start zdalnego klienta bezwarunkowo łączy UDP, co koliduje z `+connect_lobby` bez `--connect`.
- `GameComposition.cs:378`: QuitCoordinator dostaje samo ReleaseStep, bez wymaganego ProcessCleanup.
- `GameComposition.cs:543,570`: brak szwów do ustawienia ready=0 przy RequestShutdown i LeaveLobby po zakończonym Dispose transportów.

## Pytanie do rozstrzygnięcia

Czy właściciel K3-00 uzupełni neutralne szwy w `GameComposition.cs`, czy K3-17 otrzyma wąski wyjątek od X10 na ich dodanie? Potrzebne punkty: wybór transportu i listenerów przed konstrukcją runtime'ów; start platformy po udanym starcie świata i wybór ścieżki połączenia klienta; powiadomienie przy RequestShutdown; LeaveLobby po cleanup transportów; procesowy cleanup koordynatora po udanym ReleaseStep. Implementacja Steam pozostanie wyłącznie w `GameComposition.Platform.cs` i plikach K3-17.

To pytanie dotyczy sprzeczności zakresu, nie domyślnych wariantów P1/P2. Nie wprowadzono obejścia przez refleksję, przebudowanie runtime'ów ani edycję cudzych partial.

## Weryfikacja

Przeczytano ZASADY, README K3, pełne plany K3-00/K3-17 i fragmenty kodu dotyczące blokady. Gałąź `pakiet/k3-17-steam-lobby`, początkowy worktree czysty. Build/test/format: NOT RUN — zmiany wyłącznie dokumentacyjne. Godot/gdUnit/smoke: NOT RUN zgodnie z ograniczeniem użytkownika. Manual Steam i przegląd Opus: pending. Bez pushowania.
