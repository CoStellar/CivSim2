# ADR 0003: Determinizm symulacji

**Status:** przyjęta, 2026-11-03

## Kontekst

Chcemy powtarzalnych błędów, zapisu gry przez seed + komendy i furtki do multiplayera.

## Decyzja

Własny PRNG ze strumieniami na system, ekonomia na liczbach całkowitych (setne części), stała kolejność iteracji, AI równoległe tylko czyta migawkę, test hasha stanu w każdym PR.

## Rozważane alternatywy

Zwykły `System.Random` i `float`: szybciej na start, ale wyniki zależą od platformy i kolejności wątków.

## Konsekwencje

Każdy błąd da się odtworzyć. Kod musi przestrzegać reguł z `CLAUDE.md`, a test determinizmu pilnuje ich automatycznie.
