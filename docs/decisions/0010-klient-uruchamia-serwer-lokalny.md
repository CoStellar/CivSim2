# ADR 0010: Klient uruchamia serwer lokalny, ale nie widzi rdzenia

**Status:** przyjęta, 2026-11-04

## Kontekst

W grze jednoosobowej serwer działa w tym samym procesie co klient (ADR 0008). Ktoś musi go uruchomić:
wczytać dane z `data/`, wygenerować świat i odpalić wątek symulacji. Jednocześnie klient (`game/`) nie może
odwoływać się do `CivSim.Core`, żeby nie dało się „na skróty” czytać stanu gry z pominięciem mgły wojny.

## Decyzja

- Klient odwołuje się do `CivSim.Protocol` i do `CivSim.Server`, ale z serwera używa tylko klasy `LocalServerHost`
  (start, połączenie w pamięci, zamknięcie). Ustawienia gry przekazuje jako `LocalGameOptions` (typ z Protocol, same proste wartości).
- `CivSim.Core` i `CivSim.Data` są kopiowane obok gry jako pliki (bez referencji kompilacji,
  `ReferenceOutputAssembly=false`), a `DisableTransitiveProjectReferences` wyłącza referencje przechodnie.
  Kod w `game/` nie skompiluje się, jeśli spróbuje użyć typu z rdzenia.
- Z serwerem klient rozmawia wyłącznie wiadomościami protokołu, także w podglądzie.

## Rozważane alternatywy

- **Osobny proces serwera:** najczystsza granica, ale dwa procesy do uruchamiania i debugowania już teraz,
  zanim gra sieciowa w ogóle istnieje.
- **Mały projekt „launcher” między klientem a serwerem:** to samo, co dziś, tylko o jeden projekt więcej.
  Dodamy go, jeśli `CivSim.Server` zacznie wystawiać więcej publicznych typów.
- **Pełna referencja do serwera i rdzenia:** wygodna, ale kusi do omijania protokołu.

## Konsekwencje

Gra w Godocie startuje jednym przyciskiem (F5), a granicy klient–serwer pilnuje kompilator.
Serwer dedykowany w przyszłości użyje tego samego `LocalServerHost` (albo jego sieciowej wersji) bez Godota.
