# Przygotowanie komputera do pracy z CivSim 2

Ta instrukcja prowadzi od pustego komputera do uruchomionego projektu w Godocie.
Działa na Windowsie, Linuksie i macOS.

## 1. Zainstaluj trzy programy

| Program | Wersja | Skąd |
| --- | --- | --- |
| Git | dowolna aktualna | https://git-scm.com/downloads (na Windowsie możesz też użyć GitHub Desktop) |
| .NET SDK | **10** (aktualny LTS) | https://dotnet.microsoft.com/download/dotnet/10.0, kolumna „SDK” |
| Godot | **4.7.2, wersja .NET** | https://godotengine.org/download, przycisk **Godot Engine - .NET** |

Ważne:

- Godot musi być w wersji **.NET** (dawniej „Mono”). Zwykła wersja nie otworzy skryptów C#.
- Wersja Godota powinna się zgadzać z tą w repo (4.7.2). Jest zapisana w `global.json` i w `game/CivSim.Game.csproj`.
  Jeśli kiedyś zmienisz Godota na nowszy, powiedz mi, a podbiję wersję w repo.
- Po instalacji .NET SDK uruchom komputer ponownie (albo przynajmniej wyloguj się), żeby Godot znalazł polecenie `dotnet`.

Sprawdzenie: w terminalu (Windows: PowerShell) wpisz `dotnet --version`. Powinno pokazać numer zaczynający się od `10.`.

## 2. Pobierz repo

```
git clone https://github.com/CoStellar/CivSim2.git
```

Albo w GitHub Desktop: **File → Clone repository → CoStellar/CivSim2**.

## 3. Otwórz projekt w Godocie

1. Uruchom Godota. Pojawi się Menedżer projektów.
2. Kliknij **Import** i wskaż plik `game/project.godot` z pobranego repo.
3. Kliknij **Import & Edit**. Za pierwszym razem Godot chwilę indeksuje pliki.
4. Naciśnij **F5** (albo trójkąt „Run Project” w prawym górnym rogu).
   Godot sam zbuduje kod C# i otworzy okno z mapą świata. U góry jest numer tury i przyciski prędkości,
   po lewej inspektor wybranego pola, po prawej kronika, na dole wykres czasu tury.

Jeśli widzisz mapę, wszystko działa: Godot, .NET, lokalny serwer i połączenie klienta z serwerem.

### Sterowanie podglądem

| Klawisz / mysz | Działanie |
| --- | --- |
| WASD, strzałki, przeciąganie prawym lub środkowym przyciskiem | przesuwanie mapy (zawija się w obu kierunkach) |
| kółko myszy, `+` / `-` | zoom |
| lewy przycisk | wybór pola |
| Spacja | pauza / ×1 |
| 1–5 | pauza, ×1, ×2, ×5, maks. |
| Tab | następna nakładka (biomy, wysokość, temperatura, wilgotność, żyzność, złoża, właściciel, wpływy) |

### Inny świat w podglądzie

W Godocie: **Project → Project Settings**, włącz **Advanced Settings** (prawy górny róg) i wpisz `main run args` w pole **Filter Settings**
(to pole jest w sekcji **Editor → Run**). Albo: **Debug → Customize Run Instances… → Enable Main Run Args**. Wpisz np.:

```
-- --seed 42 --map-size huge --climate cold --relief mountainous --civs 20
```

Te same ustawienia co w trybie bez okna (niżej): `--seed`, `--map-size` (test, standard, large, huge), `--world-type`,
`--water`, `--climate`, `--moisture`, `--relief`, `--resources`, `--civs`, oraz `--speed` (paused, x1, x2, x5, max).
Osobne `--` na początku jest zalecane. Bez niego też zadziała, z wyjątkiem `--benchmark`, które Godot bierze wtedy za własną opcję.
`--benchmark 600` przelatuje kamerą nad mapą przez 600 klatek, wypisuje w panelu Output średni czas klatki i zamyka grę.

## Co jest gdzie

| Folder | Co tam jest | Czy tu pracujesz |
| --- | --- | --- |
| `game/` | projekt Godota (sceny, skrypty klienta) | tak, w edytorze Godota |
| `art/` | Twoje pliki LibreSprite i paleta | tak; do etapu 5 grafika nie trafia jeszcze do `game/` |
| `src/`, `tests/` | symulacja, serwer, testy (C#) | nie, to moja część |
| `data/` | zawartość gry w JSON | czasem, przy balansie |
| `docs/` | GDD, decyzje, opisy systemów | czytasz i akceptujesz zmiany |

Godot tworzy w `game/` ukryty folder `.godot/`. To pamięć podręczna edytora, nie trafia do repo.

## Gdy coś nie działa

- **„.NET SDK not found” albo skrypty się nie budują:** zainstaluj .NET 10 SDK (nie samo „Runtime”) i uruchom komputer ponownie.
- **Godot nie widzi C# / brak przycisku „Build”:** masz zwykłą wersję Godota zamiast wersji .NET.
- **Błąd o wersji `Godot.NET.Sdk`:** wersja Godota różni się od 4.7.2. Zainstaluj 4.7.2 albo daj mi znać, którą masz.
- **Cokolwiek innego:** skopiuj treść błędu z dolnego panelu „Output” w Godocie i wklej mi ją.

## Dla Claude (sprawdzenie bez okna)

```
dotnet build CivSim2.sln
dotnet test CivSim2.sln
```

Projekt Godota (`game/CivSim.Game.csproj`) jest częścią solucji, więc `dotnet build` buduje też klienta.
Klient odwołuje się tylko do `CivSim.Protocol` (`DisableTransitiveProjectReferences`), więc użycie typów z `CivSim.Core`
w `game/` kończy się błędem kompilacji.

## Uruchomienie symulacji bez okna

W katalogu repo, w terminalu:

```
dotnet run -c Release --project src/CivSim.Headless -- --seed 1 --turns 10000 --map-size standard --report reports/bench.csv
```

- `--seed`: liczba startowa gry (ten sam seed = ta sama gra),
- `--turns`: ile tur policzyć,
- `--map-size`: `test`, `standard`, `large` albo `huge`,
- `--report`: plik CSV z czasem każdej fazy każdej tury (otworzysz go w Excelu albo LibreOffice),
- `--stats`: plik CSV z ludnością każdej cywilizacji, zapasem jedzenia, średnim zdrowiem i nastrojem; `--stats-every N` zapisuje co N tur (domyślnie 10),
- `--world-type`: `continents` (domyślnie), `pangea` albo `archipelago`,
- `--water`: procent wody, 30–80 (domyślnie 60),
- `--png <folder>`: zapisuje podgląd mapy jako obrazy PNG (biomy, wysokość, temperatura, wilgotność, żyzność, złoża, strefy wpływów, dzika żywność); przy `--turns` większym od 0 dodatkowo strefy i dzika żywność po ostatniej turze,
- `--png-scale`: wielkość heksa na obrazie (1–12, domyślnie 3, czyli heks szeroki na 12 px),
- `--climate`: `temperate` (domyślnie), `cold` albo `hot`,
- `--moisture`: `normal` (domyślnie), `dry` albo `wet`,
- `--inland`: wilgotność w głębi lądu, `normal` (domyślnie), `dry` (suche wnętrza kontynentów) albo `humid` (lasy daleko od morza); działa też w podglądzie w Godocie,
- `--relief`: `normal` (domyślnie), `flat` albo `mountainous`,
- `--resources`: `normal` (domyślnie), `scarce` albo `rich`,
- `--civs`: liczba cywilizacji 1–64 (domyślnie zależna od rozmiaru mapy).

Sam podgląd mapy, bez liczenia tur:

```
dotnet run -c Release --project src/CivSim.Headless -- --seed 7 --turns 0 --map-size standard --png maps
```

Na końcu program wypisuje podsumowanie świata (woda, rzeki, jeziora, starty, wynik walidatora), hash stanu (dwa uruchomienia z tym samym seedem muszą dać ten sam), czasy tur i zużycie pamięci.
