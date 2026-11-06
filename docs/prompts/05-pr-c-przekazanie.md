# PR C (ziemia i migracje): stan do przejęcia (2026-11-06 13:30)

Gałąź: `feat/ai-land` (od `development` po PR #39). PR do `development` jeszcze nie otwarty. Plan: `docs/prompts/04-ai-czas-wartosci-migracje.md`, punkt 5 (bez odłamów jako nowych cywilizacji, to PR D w 4f).

## Zrobione (kod + testy)

- Gubernator wycenia ziemię z technologiami cywilizacji, rybacy liczą pola wody (`GovernorAdvisor.Land`, `UseLand`).
- Osiedlenie: plon dla prawdziwej liczby rolników (`FarmFood`, `Producers`), porównanie ze zbieractwem i ekspansywnością (`settleOverGatheringPercent`).
- G7 wioska wysyła osadników (`DecideVillage`, `AiAction.SendSettlers`): zatłoczona albo wyjałowione pola; 60 osób, najwyżej raz na 100 lat (`villageSettlersEveryYears`), cel poza strefami własnych osad (`Free`).
- G8 wyprawa w nieznane (`Explore`, `AiAction.Explore`).
- Zwiadowcy 2 na stulecie, marsz 20 pól na stulecie (`data/perception.json`).
- Headless: kolumny `settlements`, `spread_cells` w `--stats`.
- Testy: `tests/CivSim.Tests/Ai/LandAndMigrationTests.cs`; `DeadValueTests` i `LandAndMigrationTests` zielone po poprawkach. **Pełnego zestawu po poprawkach nie puszczono.**

## Wyniki (3 seedy, standard, 1000 tur, przed → po)

Osady ~100 → 350–450, ludzie ~2×, odległość od startu 10–12 → 20–24 pola, tura ~2× dłuższa (więcej osad). Trzeba to wyjaśnić w PR (> 10%).

## Do zrobienia

1. `dotnet test` całość + test determinizmu.
2. Benchmark po kolei: build `development` vs `feat/ai-land`, seedy 7/23/42 standard 1000 tur, huge seed 42 300 tur.
3. Wykresy PNG: odległość od startu w czasie, osady/wioski (nowy skrypt w `docs/ai/tools/`), `decisions_chart.py` + akcje SendSettlers i Explore.
4. docs/ai: 08 (pkt 4, 5, 6, 8 rozwiązane), 09 statystyki z `--decisions`, README (commit w nagłówku, wiersz historii); `docs/systems/ai.md`.
   GDD 5.4/8.2, ROADMAP, `docs/systems/perception.md`, 03, 06, 07 są już zaktualizowane.
5. PR do `development` po polsku (co, dlaczego, jak sprawdzić, testy, benchmark, tabela przed/po).

Potem: 4e (wojna), potem D (odłamy w 4f), po zgodzie Patryka.
