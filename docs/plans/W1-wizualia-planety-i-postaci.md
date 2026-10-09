# W1: Wygląd planety i postaci

| | |
| --- | --- |
| Prowadzi | Opus 5.5 (sesja z 2026-10-10, właściciel nieobecny: decyzje domyślne opisane niżej) |
| Gałąź | `wizualia/planeta-i-postac` od `pakiet/k2-13-narzedzia` |
| Zależy od | K2-08 … K2-13 (teren, postać, kamera, narzędzia) |
| Przegląd | Opus 5.5 na bramce K2; zmiany nie dotykają `src/` (Core, Persistence, Net) ani kontraktów z punktu 3 ZASAD |

## Cel

Planeta i postać mają wyglądać jak gra, a nie jak test silnika: zamiast brązowej kuli z kapsułą jest planeta z oceanami, plażami, łąkami, lasami, skałami i śniegiem na szczytach, niebo z porami dnia i gwiazdami, a gracz jest astronautą z animacjami ruchu.

## Zasada nadrzędna: tylko wygląd

Nic z tego pakietu nie trafia do symulacji, dziennika, zapisu ani sieci:

- dekoracje i ocean nie mają kolizji, więc predykcja klienta i serwer liczą ruch tak samo jak przed pakietem;
- generator terenu `core:planet/1` i materiały wokseli są nietknięte: trawę, plaże, skały i śnieg maluje shader z wysokości i nachylenia;
- `VoxelInstancer` ma elementy nietrwałe (`Persistent = false`), więc nie zapisuje niczego w strumieniu terenu;
- serwer dedykowany nie tworzy żadnego z tych węzłów (`PlanetTerrainOptions.Render` jest wyłączone), a ciało gracza na serwerze ma pusty węzeł `Visual`.

## Co powstało

| Część | Pliki |
| --- | --- |
| Postać „Explorer” (skafander, 17 kości, animacje Idle, Walk, Run, Sprint, Jump, Fall, Fly) | `tools/art/explorer_character.py` → `game/assets/characters/explorer.glb`; `game/src/Client/Character/CharacterVisual.cs` |
| Teren: tekstury z szumu (9 warstw) i malowanie powierzchni | `game/src/Planet/Rendering/PlanetTerrainMaterial.cs`, `game/shaders/planet_terrain.gdshader`, `PlanetLook.cs` |
| Niebo, mgła, gwiazdy obracające się ze słońcem, kolor słońca o świcie i zmierzchu, mgła pod wodą | `game/shaders/planet_sky.gdshader`, `PlanetSky.cs`, `ClientDayNight.cs` |
| Ocean (fale, kolor od głębokości, piana przy brzegu) | `game/shaders/planet_ocean.gdshader`, `PlanetOcean.cs` |
| Poświata atmosfery widziana z orbity | `game/shaders/planet_atmosphere.gdshader`, `PlanetAtmosphere.cs` |
| Warstwa chmur (450 m nad promieniem, bez cienia) | `game/shaders/planet_clouds.gdshader`, `PlanetClouds.cs` |
| Roślinność i skały (sosny, drzewa liściaste, krzewy, trawa, kwiaty, kamienie, głazy, kryształy), kołysanie na wietrze | `tools/art/planet_flora.py` → `game/assets/planet/flora.glb`; `PlanetFlora.cs`, `game/shaders/foliage.gdshader` |

Podgląd `--planet-preview` używa tego samego nieba i oświetlenia, więc nadaje się do oceny wyglądu.

## Pochodzenie zasobów

Oba modele powstają w całości ze skryptów Blendera w `tools/art/` (stałe ziarna, bez zewnętrznych tekstur ani modeli), tekstury terenu z szumu w kodzie. Odtworzenie:

```
blender --background --factory-startup --python tools/art/explorer_character.py -- --out game/assets/characters/explorer.glb --preview <katalog>
blender --background --factory-startup --python tools/art/planet_flora.py -- --out game/assets/planet/flora.glb --preview <katalog>
```

Pliki `.glb` są w Git LFS (`.gitattributes`), dlatego zadania CI uruchamiające Godota pobierają LFS (`lfs: true` w `ci.yml` i `nightly.yml`).

## Decyzje podjęte bez właściciela (do potwierdzenia)

- Poziom morza −42 m względem promienia nominalnego (`PlanetLook.SeaLevelMeters`); woda jest tylko wizualna, postać chodzi po dnie (pływanie to osobny pakiet).
- Drzewa i skały bez kolizji: to placeholder obiektów generatora z wytrzymałością z K6-31, które je zastąpią.
- Postać obraca się w stronę ruchu (ciało nadal trzyma odchylenie kamery), więc chodzenie tyłem pokazuje twarz postaci.
- Gałąź wychodzi od K2-13, bo pakiety K2 nie są jeszcze na `main`.

## Poza zakresem

- Inni gracze nadal nie są rysowani u klienta: replikacja bytów to K3. `CharacterVisual.Attach` przyjmie każde `PlayerBody`, więc K3 podepnie model jednym wywołaniem.
- Pływanie, obiekty do zbierania, stwory, dźwięk.

## Testy

- `game/tests/Character/CharacterVisualTests.cs`: wybór animacji według trybu i prędkości, model z kośćmi i wszystkimi animacjami pod `Visual`, ciało bez dodatkowych kolizji.
- `PlanetTerrainTests.TerrainMaterial_Create_HasShaderAndLayers` sprawdza teraz `PlanetTerrainMaterial.LayerCount` (9) i to, że warstwy wokseli (0–4) leżą przed malowanymi (5–8); wcześniej oczekiwał dokładnie 5 warstw.

## Kroki

- [x] Model postaci, animacje, `CharacterVisual`, test.
- [x] Tekstury i shader terenu.
- [x] Niebo, mgła, gwiazdy, atmosfera z orbity.
- [x] Ocean.
- [x] Roślinność, skały i kryształy, wiatr.
- [x] Chmury.
- [ ] Pełne `dotnet build`, `dotnet test`, testy silnika i dymne (wynik w opisie PR).
- [ ] PR do przeglądu (po powrocie właściciela).
