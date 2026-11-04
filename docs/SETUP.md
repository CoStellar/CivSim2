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
   Godot sam zbuduje kod C# i otworzy okno z napisem:
   **„CivSim 2 - client ready (protocol v1)”**.

Jeśli widzisz ten napis, wszystko działa: Godot, .NET i połączenie klienta z biblioteką protokołu.

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
