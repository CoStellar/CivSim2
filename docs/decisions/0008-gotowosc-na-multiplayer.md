# ADR 0008: Gotowość na multiplayer w modelu klient–serwer

**Status:** przyjęta, 2026-11-03

## Kontekst

Patryk chce kiedyś grać w sieci. Gry sieciowej jeszcze nie budujemy, ale dodanie jej później do gry
napisanej jako jednoosobowa zwykle oznacza przepisanie dużej części kodu.

## Decyzja

Od etapu 0 gra działa jako klient i serwer, także dla jednego gracza (serwer lokalny, transport w pamięci).
Serwer jest autorytatywny i jako jedyny liczy symulację. Klient zależy tylko od `CivSim.Protocol`, wysyła
komendy i dostaje widok przefiltrowany przez mgłę wojny swojej cywilizacji. Sieć dojdzie w przyszłej aktualizacji jako
nowy transport za tym samym interfejsem. Serwer to czysty .NET, więc może działać jako serwer dedykowany.

## Rozważane alternatywy

- **Lockstep (każdy klient liczy całą symulację):** mały ruch sieciowy i dobrze pasuje do determinizmu, ale każdy
  klient zna cały stan gry, więc łatwo podejrzeć mapę przeciwników; rozjazdy stanu są trudne do diagnozy.
- **Wbudowany multiplayer Godota:** wymaga Godota po stronie serwera i wiąże logikę z silnikiem.
- **Single player teraz, sieć później:** najszybszy start, ale późniejsza przebudowa kosztowna.

## Konsekwencje

Trochę więcej pracy w etapie 0 (protokół, serwer lokalny) i koszt filtrowania widoczności na serwerze.
W zamian sieć w przyszłej aktualizacji to głównie nowy transport, lobby i obsługa rozłączeń.
