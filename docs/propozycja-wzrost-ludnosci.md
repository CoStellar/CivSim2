# Propozycja: wzrost ludności a jedzenie (do decyzji Patryka)

**Stan:** propozycja z 2026-11-22. Zmienia zasadę gry (`docs/projekt-gry.md`, `docs/sim/gospodarka.md`), więc czeka na Twoją decyzję.
Kod nie jest zmieniony; liczby niżej pochodzą z próbnych biegów.

## Problem

Dziś osada rośnie o 8% ludzi na minutę, **dopóki ma wolne domy i choć 1 jedzenia w magazynie**. Nie patrzy, czy jedzenia
przybywa. Duży lud, który nie ma już miejsca na nowe pola, dorasta do domów, a każda zima (produkcja jedzenia spada wtedy mniej
więcej do 1/3) zjada zapas i zabija część ludzi. Po wiośnie wolne domy znów się zapełniają i cykl wraca. AI nie może tego zatrzymać,
bo ludzie rodzą się sami (długie biegi z 2026-11-22: jeden lud z 24 głodował tak co zimę).

## Warianty (12 gier × 120 minut, mapa Normal 98×60, 4 ludy, AI z poprawkami z PR #134 i #139)

| | A: jak dziś | B: wzrost tylko przy nadwyżce | **C: wzrost staje, gdy osada traci jedzenie i ma go na mniej niż 2 racje** |
| --- | --- | --- | --- |
| głód (% próbek) | 3,4% | 1,7% | **2,2%** |
| ludzie po 120 min | 13 145 | 4 296 | **12 372** |
| miasta (poziom 2+) | 18,4 | 6,1 | **20,2** |
| technologie | 56,3 | 52,1 | **56,8** |
| epoka brązu (min) | 34,2 | 42,5 | **35,0** |
| epoka żelaza (min) | 60,0 (11 z 12 gier) | 69,3 (12 z 12) | **58,8 (12 z 12)** |
| średniowiecze (min) | 69,1 (11 z 12) | 81,2 (12 z 12) | **67,0 (12 z 12)** |

- **B** jest za ostre: ludzi 3 razy mniej, miast 3 razy mniej, epoki o 8–12 minut później.
- **C** zostawia tempo gry (a nawet je przyspiesza), głodu jest o jedną trzecią mniej, a wszystkie 12 gier dochodzi do średniowiecza.

## Rekomendacja: wariant C

Zasada dla gracza, jednym zdaniem: **„Osada przestaje rosnąć, gdy jedzenie jej ubywa, a zostało go na mniej niż dwie racje.”**
Gracz widzi to w panelu osady (podpowiedź „nie rośnie: za mało jedzenia”), a przyczyna trafia do kroniki.

Test sternika: decyzja gracza (budowa pól, zapas na zimę) ma widoczny skutek (osada rośnie albo stoi) i przyczynę w panelu.

## Co zrobię po Twojej zgodzie

1. Zasada w kodzie (`SimGame.PopulationStep`), próg (2 racje) w `data/sim/game.json`.
2. Opis w `docs/projekt-gry.md` i `docs/sim/gospodarka.md`.
3. Podpowiedź w panelu osady i wpis w kronice.
4. Test jednostkowy i nowy pomiar `--sim-report`.
