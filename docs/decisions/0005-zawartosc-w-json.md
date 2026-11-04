# ADR 0005: Zawartość gry w plikach JSON

**Status:** przyjęta, 2026-11-03

## Kontekst

Gra ma dziesiątki technologii, zasobów, budynków i zdarzeń w 10 erach. Patryk ma móc je zmieniać bez programowania.

## Decyzja

Cała zawartość w `data/*.json`, walidowana przy starcie. Kod zna tylko mechanizmy.

## Rozważane alternatywy

Zawartość w kodzie C#: szybsza na start, ale każda zmiana wymaga programisty.

## Konsekwencje

Łatwe strojenie i rozbudowa. Potrzebny dobry walidator z czytelnymi błędami.
