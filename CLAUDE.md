# CivSim 2 — instrukcje dla Claude Code

Czytasz ten plik na początku każdej sesji. Jest źródłem zasad pracy w tym repo.

**Teraz: sprint do grywalnego demo (do średniowiecza).** Lista prac i stan: `ROADMAP.md`.

- **Do czego dążymy teraz:** partia ok. 60 minut przeciw kilku ludom AI, od wyboru startu do celu partii. Ostatnie prace przed demo
  (ROADMAP „Do demo”): pokój z negocjacji i zmęczenie wojną (ADR 0030), AI bez bezczynnych warsztatów, płynne takty przy wyprawach,
  potem runda testów Patryka.
- **Potem (po demo):** wydzielenie legacy (ADR 0029, kroki 2–4), zarządca i prowincje, AI na statkach, pixel art Patryka,
  multiplayer przez sieć, kolejne ery.

Na starcie sesji przeczytaj `ROADMAP.md`, `docs/known-issues.md` i ostatnie commity (`git log --oneline -20 origin/development`).

## Gra

CivSim 2 to strategia na żywo, „Cywilizacja 6 w czasie rzeczywistym”, na heksagonalnej mapie-planecie (wschód–zachód zawija się,
przez bieguny przechodzi się na drugą stronę). **Gracz jest całą cywilizacją:** buduje na heksach, bada technologie, prowadzi wojsko
i dyplomację. Jego lud nie ma własnej AI; sam wykonuje tylko codzienne prace. Przeciwnik AI to pełny gracz z tymi samymi komendami.
**Najpierw gra, potem realizm.**

Dokumenty gry (po polsku):

| Plik | Co |
| --- | --- |
| `docs/projekt-gry.md` | projekt gry: zasady i zakres (kopia robocza w notatkach Projektu: `claude/projekt-gry.md`) |
| `docs/sim/gospodarka.md` | gospodarka, osady, racje, zima, logistyka, wszystkie decyzje z liczbami |
| `docs/sim/technologie.md` | drzewo technologii, budynki, mechaniki |
| `docs/sim/walka.md` | walka, oblężenie, zaopatrzenie, limit oddziałów, doświadczenie rodzajów |
| `docs/sim/ai.md` | każda decyzja AI (N1–…) z warunkami, progami i pomiarami |
| `docs/sim/scenariusze.md` | scenariusze testowe i tryb debug |
| `docs/wydajnosc.md` | zasady wydajności (usypianie, terminy, stałe rytmy, sumy zamiast osób) |
| `docs/known-issues.md` | znane problemy nowej gry |
| `docs/multiplayer/plan.md` | plan gry przez sieć: decyzje Patryka, pośrednik, serwer na VPS, bezpieczeństwo, kroki MP1–MP6 |
| `docs/decisions/` | decyzje architektoniczne (ADR) |

Decyzje Patryka z rozmów są też w notatkach Projektu (`claude/decyzje-*.md`, `claude/runda-testow-*.md`).

## Legacy

Kod i dokumenty sprzed przebudowy (ADR 0028, 2026-11-17) zostają w repo, ale **nie zajmujemy się nimi i nie odnosimy się do nich**:
nie czytamy, nie naprawiamy, nie rozwijamy, nowy kod od nich nie zależy. Opis, co jest legacy: `docs/legacy/README.md`
(tam są też stare GDD, opisy systemów, AI i roadmapa). Legacy jest m.in. w `src/CivSim.Core` (poza mapą, generacją świata, liczbami,
losowaniem, haszem i nazwami) i w plikach `data/*.json` poza `data/sim/`.

## Role i sposób pracy

- **Claude Code:** cały kod, testy, dane, dokumentacja techniczna, pomiary.
- **Patryk (właściciel):** decyzje projektowe, grafika, granie i ocena. Nie pisze w C#. Tłumacz po polsku, prosto, bez żargonu,
  i pokazuj efekty liczbami (tabele, pomiary, zrzuty ekranu).
- **Zmiana zasad gry wymaga zgody Patryka.** Najpierw propozycja (najlepiej kwestionariusz z wariantami i rekomendacją), potem kod.
- **Po każdym wdrożeniu:** szczegółowe wyjaśnienie z liczbami (przed/po z `--sim-report`).
- **Przed otwarciem PR:** sprawdź otwarte PR-y względem siebie (konflikty, zależności, kolejność merge'a).
- **Testy uruchamiaj wtedy, gdy są potrzebne** (przed commitem zmian w kodzie, nie po każdej drobnej edycji).
- **Jeden skrypt zamiast wielu wywołań narzędzi.** Pracę z wieloma krokami (testy, pomiary, biegi harnessu, wykresy) zapisz jako jeden
  skrypt (bash/Python/C#), uruchom go raz i czytaj tylko podsumowanie, a nie wyniki każdego kroku. Najpierw sprawdź, co już jest:
  `--sim-report`/`--sim-scenario` w `CivSim.Headless`, `data/sim/tools/`, `tools/audio/`, `tools/export/`. Skrypt, który przyda się
  znowu, trzymaj w `tools/<temat>/` z nagłówkiem (co robi, czego wymaga, jak uruchomić); jednorazowe zostaw w katalogu tymczasowym.
- **Wszystkie liczby w danych** (`data/sim/game.json`), nie w kodzie. Klucze czytaj przez `Prop`/`TryProp`/`Entries` (`Content/JsonRead.cs`),
  nie przez `GetProperty`: inaczej kontrola danych (menu, `GameDataTests`) zgłosi nowy klucz jako nieznany.
- **Czego lud nie zna, tego nie widać** (złoża późniejszych er jako „???”, budynki i jednostki dopiero po technologii). Wyjątek: drzewo technologii.
- **Gałęzie:** `feat/<opis>` albo `fix/<opis>` od `development`. PR do `development`, opis po polsku: co, dlaczego, liczby, wyniki testów.
  **Merge robi Patryk.** Nigdy nie pushuj do `main`.
- **Nie commituj** nieśledzonych `tools/CivSim.Playtest` ani `docs/playtest` (to drugie jest śledzone, ale go nie zmieniaj).

## Języki

- Rozmowa z Patrykiem, opisy PR, dokumenty w `docs/`: **po polsku**, prosto.
- Kod, komentarze, nazwy, commity, dane, teksty w grze: **po angielsku**.

## Struktura (nowa gra)

```
src/CivSim.Sim/            symulacja nowej gry (State/SimGame*.cs, Ai/SimAi*.cs, Content/SimContent.cs)
src/CivSim.Core/           fundament: Map (HexGlobe), WorldGen, Numerics, Random, Hashing, Naming (reszta to legacy)
src/CivSim.Protocol/       wiadomości klient–serwer (MessagePack, wersja w ProtocolInfo.cs)
src/CivSim.Server/         PlayHost.cs: serwer gry, widok gracza filtrowany mgłą, komendy
src/CivSim.Headless/       --sim-report, --sim-scenario i inne pomiary bez okna
game/                      klient Godot 4.7.2 .NET (scripts/Play/PlayGame*.cs, PlayOverlay*.cs, UiKit, UiItems)
data/sim/game.json         wszystkie dane gry (budowany skryptem, patrz niżej)
data/sim/tools/build_game_json.py   skrypt danych: edytuje game.json w miejscu; zmiany danych rób tutaj
data/sim/scenarios/        scenariusze testowe (README.md w środku)
tests/CivSim.Sim.Tests/    testy symulacji
tests/CivSim.Tests/        testy protokołu, nowego serwera (PlayHost), danych i wspólnego fundamentu (mapa, losowanie, generator świata)
tests/CivSim.Legacy.Tests/ testy gry legacy (stary serwer tur, doradcy AI, dyplomacja, gospodarka...; ADR 0029)
docs/sim/                  opis nowej gry
```

## Polecenia

```
export PATH=$HOME/.dotnet:$PATH                                 # w sesji w chmurze bez .NET: apt-get install -y dotnet-sdk-10.0
python3 data/sim/tools/build_game_json.py                      # po zmianie skryptu danych
dotnet build -c Release -v q                                     # ostrzeżenia są błędami
dotnet test tests/CivSim.Sim.Tests -c Release -v q
dotnet test tests/CivSim.Tests -c Release -v q
dotnet test tests/CivSim.Legacy.Tests -c Release -v q              # gra legacy; przed PR i tak całość: dotnet test -c Release
dotnet run --project src/CivSim.Headless -c Release -- --sim-report --seeds 12 --minutes 75 [--size 98x60]   # pomiar AI i tempa er (domyślnie mapa „Normal” 98×60)
dotnet run --project src/CivSim.Headless -c Release -- --sim-scenario <nazwa> [--minutes N] [--ai-all]
cd game && dotnet build                                         # klient
```

Zrzuty ekranu klienta (xvfb, w katalogu `game`): `--play [--play-scenario <nazwa>] --play-window <okno> --play-screenshot /tmp/x.png --play-seconds N`.

Protokół: po zmianie wiadomości podbij `ProtocolInfo.Version` i oczekiwany nagłówek w `ProtocolSerializerTests.WireFormatIsStable`.

## Architektura — twarde zasady

1. **Symulacja bez grafiki.** `CivSim.Sim` i `CivSim.Core` nie zależą od Godota; działają w `CivSim.Headless` i testach.
2. **Wszystko przez komendy.** Każda zmiana stanu to komenda (`SimGame.Apply`). AI i gracz wydają te same komendy, a gra sprawdza je
   tak samo. AI niczego nie zmienia bezpośrednio.
3. **Determinizm:** własny PRNG ze strumieniami; zakaz `System.Random`, `Guid.NewGuid`, `DateTime.Now` w logice; ekonomia na liczbach
   całkowitych (setne części jednostki, `SimContent.One`); stała kolejność iteracji; hasz stanu w `SimGame.Hash.cs` (dopisz do niego nowy stan).
4. **Dane w JSON**, wczytywane i sprawdzane przy starcie (`SimContent`).
5. **Czas w taktach** (10 na sekundę). Każdy system ma stały rytm (`docs/wydajnosc.md`); wartości w danych na sekundę albo minutę gry.
6. **Klient–serwer od początku.** Klient (`game/`) zna tylko `CivSim.Protocol`; serwer jest autorytatywny i filtruje widok mgłą wojny.
   Transport w pamięci za tym samym interfejsem co przyszły sieciowy. Multiplayer przez sieć to przyszła aktualizacja.
7. **Mapa:** heksy flat-top, współrzędne osiowe, kula; odległości i sąsiedztwa tylko przez `HexGlobe`.
8. **AI bez premii i ukrytej wiedzy:** widzi tylko to, co odkrył jej lud.

## Czego nie robić

- Nie dodawaj kultury ani religii.
- Nie zmieniaj zasad gry bez zgody Patryka.
- Nie rozwijaj legacy i nie uzależniaj od niego nowej gry.
- Nie omijaj granicy klient–serwer.
- Nie planuj eksportu do przeglądarki.
