# ADR 0009: Zakres CORE, moduły jako aktualizacje, gra bez kultury

**Status:** przyjęta, 2026-11-03

## Kontekst

Dokument projektu opisuje dużo funkcji. Zrobienie wszystkich naraz wydłużyłoby projekt o lata
i utrudniło dojście do działającego symulatora.

## Decyzja

Każda funkcja z GDD ma ocenę w rozdziale 16: CORE, CORE (uproszczone), Moduł albo Wytnij.
Bieżący plan obejmuje tylko CORE i CORE (uproszczone). Moduły to przyszłe aktualizacje.
Kultura (punkty kultury, asymilacja, zwycięstwo kulturowe) zostaje usunięta całkowicie, tak jak religia (ADR 0007).
CivSim 2 jest symulatorem rozwoju gospodarczego.

## Rozważane alternatywy

Pełny zakres od razu: za długo i zbyt ryzykownie. Kultura jako uproszczony wskaźnik: nie wnosi wiele do symulacji gospodarczej.

## Konsekwencje

Szybsze dojście do demo symulatora. Architektura nadal musi zostawiać miejsce na moduły.
Zwycięstwo kulturowe zastąpiło gospodarcze; asymilację zastąpiła prosta lojalność podbitej ludności (moduł).
