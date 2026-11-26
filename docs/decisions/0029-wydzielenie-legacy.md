# ADR 0029: Wydzielenie gry legacy z repozytorium nowej gry

**Status:** przyjęta, 2026-11-23 (krok 1 zrobiony w #140; kroki 2–4 po demo, decyzja Patryka 2026-11-23). Rozwija punkt 7 ADR 0028
(„stary kod to legacy, zastępujemy go kawałek po kawałku”).

## Kontekst

Przegląd na grafie wiedzy (2026-11-22) pokazał, że w repo żyją obok siebie dwie gry:

- około 22 tys. linii w `src/CivSim.Core` to gra legacy, której nowa gra nie używa: `Ai` (doradcy), `Commands`, `Diplomacy`,
  `Economy`, `Episodes` (kampanie), `Events`, `Governance`, `Perception`, `Population`, `Research`, `Simulation` (stara pętla tur),
  `State`, `Units`;
- stary serwer tur i podgląd (`GameServer`, `LocalServerHost`, `HelmsmanLog`, `Saves`, `Replays`), stary wczytywacz danych
  (`ContentLoader.cs`, 3067 linii) i stare pliki `data/*.json` z 26 schematami;
- 417 z 774 testów sprawdzało grę legacy.

Nowa gra (`CivSim.Sim`) korzysta z `CivSim.Core` tylko przez `Map`, `Random`, `WorldGen` i część `Content` (biomy, złoża
z `ContentLoader`). Ale **`WorldGen` zależy od starego stanu gry**: generator świata zapisuje wyniki do `GameState`/`WorldState`
i używa `Population` oraz `Simulation`. Dlatego starej gry nie da się dziś po prostu przenieść do osobnego projektu.
Menu klienta też korzysta ze starego podglądu (`LocalServerHost.Describe`: ustawienia planet; tryb podglądu obserwatora).

## Decyzja (propozycja)

Wydzielić grę legacy w czterech krokach; każdy osobnym PR-em, każdy bez zmiany działania nowej gry:

1. **Testy** (zrobione, PR `refactor/legacy-project`): testy gry legacy w osobnym projekcie `tests/CivSim.Legacy.Tests`
   (417 testów). Szybka pętla nowej gry: `CivSim.Sim.Tests` + `CivSim.Tests` (357 testów). `dotnet test` dalej uruchamia wszystko.
2. **Generator świata bez starego stanu:** `WorldGen` zwraca własny wynik (planeta, mapa, klimat), a stara gra przepisuje go do
   `GameState`. To jedyna duża zmiana: dotyka fundamentu, wymaga testu, że mapa wychodzi bit w bit taka sama.
3. **Kod legacy do projektu `src/CivSim.Legacy`:** foldery wymienione wyżej, stary serwer, `ContentLoader` (bez biomów i złóż, które
   przechodzą do nowego wczytywania) i stare pliki danych. `CivSim.Core` zostaje wspólnym fundamentem.
4. **Klient bez legacy:** menu bierze ustawienia planet z danych nowej gry; tryb podglądu obserwatora albo przechodzi na nowy serwer,
   albo znika (decyzja Patryka). Potem projekt `CivSim.Legacy` można przestać budować; historia zostaje w gałęzi `legacy/pre-rework`.

## Konsekwencje

- Krok 1: szybsza pętla testów nowej gry, jaśniejszy podział; nic nie znika.
- Kroki 2–4: mniej kodu do czytania i budowania, graf wiedzy i AI-asystent przestają mieszać dwie gry.
- Ryzyko kroku 2: zmiana w generatorze świata może zmienić mapy; chroni test „ta sama mapa bit w bit” dla kilku seedów.

## Do decyzji Patryka

- Czy robimy kroki 2–4, i kiedy (rekomendacja: po demo, bo nie zmieniają gry, a krok 2 dotyka fundamentu)?
- Czy tryb podglądu obserwatora (stara gra) jest jeszcze potrzebny?
