# ADR 0031: Multiplayer przez sieć: serwer autorytatywny, TCP, pośrednik na VPS

**Status:** przyjęta, 2026-11-25 (decyzje Patryka z 4 rund pytań). Pełny plan, pomiary, research i wszystkie odpowiedzi: `docs/multiplayer/plan.md`.
Rozwija ADR 0008 (gotowość na multiplayer) i ADR 0010 (klient uruchamia serwer lokalny).

## Kontekst

Gra od początku działa jako klient i serwer: serwer (`PlayHost`) jako jedyny liczy symulację, sprawdza rozkazy i wysyła każdemu graczowi
widok przez jego mgłę wojny; połączenie jest dziś tylko w pamięci. Patryk chce grać ze znajomymi przez internet bez VPN, bez Steam i EOS,
najtaniej, z możliwością hostowania na VPS. Pomiar 2026-11-25: 3–5 KB/s na gracza, serwer ok. 9% jednego rdzenia i 110–215 MB przy 1–8 graczach.

## Decyzja

1. **Serwer autorytatywny** zostaje (bez lockstepu): klient nie dostaje pełnego stanu, nie ma rozjazdów stanu.
2. **TCP z czystego .NET** za istniejącym `IConnection`; ramka = długość + dzisiejsza ramka MessagePack; Brotli dla dużych wiadomości.
3. **Gdzie liczy się gra:** u gospodarza (gra + serwer w jednym oknie), a docelowo także na VPS (serwer bez okna w Dockerze, najwyżej 2 partie naraz).
4. **Pośrednik (relay)** na VPS Patryka w Dockerze przekazuje cały ruch (ukrywa adresy IP, działa przy każdym NAT). Gry zakłada się hasłem
   z pliku z datami ważności; ten sam obraz jest publiczny, każdy może postawić własnego pośrednika. W grze: IP i port serwera albo pośrednika.
5. **Zasady partii:** 2–8 ludzi, do 8 ludów; wolne miejsca prowadzi AI; ×1/×2, zmiana głosowaniem; 3 pauzy na gracza; rozłączony lud przejmuje AI;
   nowi gracze obserwują albo przejmują lud AI po głosowaniu; wyrzucenie przez gospodarza albo głosowanie; prawo głosu po 3 minutach gry;
   upadły gracz widzi całą mapę; czat do wszystkich i do sojuszników; pseudonim bez kont.
6. **Wersje gry** `vRR.M.N` (np. `v26.11.1`); serwer i klient sprawdzają wersję gry i skrót danych.
7. **Gałąź `server`** z samym kodem serwera i pośrednika, aktualizowana automatycznie z każdą wersją, z CI i obrazami w GHCR.
8. **Testy:** szybkie lokalnie, pełne w CI (repo publiczne, GitHub Actions za darmo).

## Rozważane alternatywy

- **Lockstep** (jak Age of Empires): mały ruch, ale każdy klient zna cały stan (podglądanie mapy) i trzeba wykrywać rozjazdy.
- **Wbudowany multiplayer Godota:** wiązałby serwer z silnikiem.
- **Pośrednik tylko do zapoznania (hole punching):** zawodny przy TCP i CGNAT, zdradza adresy IP; zostaje jako pomysł na oszczędność.
- **VPN (Tailscale/ZeroTier), Steam, EOS:** odrzucone przez Patryka na teraz.

## Konsekwencje

- Prace w kolejności CI1 (testy i CI), W1 (zaawansowane ustawienia świata), MP1–MP6 (`docs/multiplayer/plan.md`, rozdz. 13).
- Do naprawy w serwerze: lud rozłączonego gracza (znany problem 19), wyjątek jednego klienta zatrzymujący grę, dołączenia w zapisie (problem 11).
- Wszystko, co klient czyta z sieci, jest traktowane jako niezaufane (limity ramek, brak kodu i plików z sieci).
