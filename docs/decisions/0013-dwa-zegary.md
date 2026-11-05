# ADR 0013: Dwa zegary: krok świata zależny od planety, wolne decyzje rzadziej

**Status:** przyjęta, 2026-11-05 (decyzja Patryka).

## Kontekst

Tura trwa tyle, ile mówi era (epoka kamienia: 50 lat), a pory roku były uśrednione w klimacie. Na planecie z długim rokiem
(Uran: 84 lata, zima trwa kilkadziesiąt lat) to ukrywa najciekawsze zachowanie: plemiona powinny przenosić się z półkuli,
na którą idzie zima, tam, gdzie jest cieplej. Patryk rozważał krok co rok na każdej planecie.

## Decyzja

- **Krok świata zależy od planety.** Gdy jeden z 8 kroków roku planety trwa co najmniej rok ziemski (rok planety ≥ 8 lat),
  tura trwa 1/8 roku planety (Uran: 10,5 roku), chyba że tura ery jest krótsza. Wtedy pory roku liczą się na żywo: mapa ma
  temperaturę i wzrost roślin bieżącej i następnej tury (`SeasonSystem`), a zbieranie zimą daje tylko polowanie (20% dzikiej żywności).
  Na Ziemi i Marsie nic się nie zmienia: tura ery i uśrednione pory roku. Parametry w `data/planets.json` („seasons”).
- **Decyzje wolne rzadziej niż kroki.** Przeprowadzki z głodu albo zimna AI rozważa w każdej turze. Łączenie obozów, odłamy i
  plądrowanie raz na długość tury ery (`GameState.IsDecisionTurn`). W etapie 4 dojdzie tu rytm decyzji ustroju.
- **Bez trybu „ucieczka przed zimą”.** AI ocenia miejsca: jedzenie teraz i w następnej turze razy wygoda temperatury (granice w
  `data/population.json`, sekcja „comfort”). Efekt `cold_tolerance` (ogień, odzież, domy z przyszłych technologii) obniża granicę
  zimna, więc z lepszą technologią zimne tereny są więcej warte. Wędrówki wynikają z tej oceny.

## Rozważane alternatywy

- **Stały krok co rok na każdej planecie:** epoka kamienia to ok. 100 000 kroków (ok. pół godziny oglądania na maksymalnej
  prędkości i wielokrotnie dłuższe testy), a na Ziemi nic nowego, bo pół roku zimy i tak nie widać w skali mapy.
- **Twarda reguła migracji sezonowej w AI:** prostsza, ale wbrew zasadzie, że decyzje AI wynikają z sytuacji (GDD 8).

## Konsekwencje

- Wszystkie tempa nadal są na rok i przeliczane przez długość tury, więc krótsza tura niczego nie psuje.
- Na Uranie gra trwa więcej tur na ten sam czas (ok. 5 razy więcej w epoce kamienia).
- `minTurnsBetweenMoves` zastąpiło `minYearsBetweenMoves`: na Ziemi to dalej 100 lat, na Uranie 21 lat.
