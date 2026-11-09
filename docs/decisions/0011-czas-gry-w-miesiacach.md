# ADR 0011: Czas gry w miesiącach, tempa na rok

**Status:** przyjęta, 2026-11-05

## Kontekst

CLAUDE.md (zasada 6) każe definiować wszystkie tempa na rok i przeliczać je przez długość tury ery. Tury trwają od 50 lat
(era kamienia) do kwartału (era kosmiczna, GDD 6.2). Efekty (GDD 10.1) i zdarzenia mają czas trwania, który nie może
zależeć od tego, ile lat ma akurat tura. Patryk potwierdził 2026-11-05, że liczby psucia z GDD 4.3 są na rok.

## Decyzja

- Stan gry trzyma czas jako liczbę miesięcy od startu (`GameState.Month`), a nie tylko numer tury. Na koniec tury
  `CalendarSystem` dodaje długość tury z `data/eras.json` (`monthsPerTurn`, np. 600 = 50 lat).
- Efekty mają miesiąc końca (`EndMonth`) zamiast liczby tur. Czas trwania podaje się w latach.
- Tempa przeliczamy liczbami całkowitymi: pełne lata jako procent składany, reszta roku proporcjonalnie.
- Długość tury bierzemy z ery najbardziej zaawansowanej żywej cywilizacji. To zasada **tymczasowa** do etapu 3f,
  gdzie pojawią się technologie i zmiana ery; wtedy wrócimy do niej z Patrykiem.

## Rozważane alternatywy

- **Czas w latach:** prostszy, ale ery 9–10 mają tury krótsze niż rok.
- **Czas w dniach:** dokładniejszy niż trzeba; miesiąc wystarcza na najkrótszą turę (kwartał).
- **Tempa na turę:** łamią zasadę 6 i psują ekonomię przy zmianie ery.

## Konsekwencje

Każdy nowy system, który ma coś „na rok”, czyta długość tury z `GameState.MonthsPerTurn(content)`. Zapis gry
(format 5) i hash stanu zawierają czas w miesiącach.
