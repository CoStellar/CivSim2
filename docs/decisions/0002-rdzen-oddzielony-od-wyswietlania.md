# ADR 0002: Rdzeń symulacji oddzielony od wyświetlania

**Status:** przyjęta, 2026-11-03

## Kontekst

Stary CivSim zacinał się po czasie, bo logika była sklejona ze Swingiem.

## Decyzja

`CivSim.Core` nie zależy od Godota. Działa na osobnym wątku, przyjmuje komendy i publikuje niezmienne migawki stanu. Godot tylko je rysuje.

## Rozważane alternatywy

Logika w skryptach Godota: prostsze na start, ale wiąże wydajność symulacji z klatkami obrazu i uniemożliwia testy bez okna.

## Konsekwencje

Możliwe testy i benchmarki bez grafiki, UI zawsze płynne. Trzeba utrzymywać format migawki.
