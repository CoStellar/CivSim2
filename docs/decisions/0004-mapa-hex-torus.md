# ADR 0004: Mapa: heksy flat-top na torusie

**Status:** przyjęta, 2026-11-03; zawijanie zastąpione 2026-11-05 przez [ADR 0012](0012-mapa-kula.md)

## Kontekst

Mapa ma przypominać planetę: idąc w dowolnym kierunku, wraca się do punktu wyjścia.

## Decyzja

Heksy z płaskim bokiem u góry, współrzędne osiowe, zawijanie w obu osiach (torus), parzysta liczba rzędów. Równik w środku mapy, bieguny łączą się na szwie. Fragmenty 32×32. Wiele światów (planet) o różnych rozmiarach.

## Rozważane alternatywy

Walec (zawijanie tylko wschód–zachód) jak w Civilization: prostszy, ale nie spełnia założenia „jak planeta”.

## Konsekwencje

Wszystkie odległości i sąsiedztwa przez jeden moduł `HexTorus`. Grafika heksa: 48×42 px.
