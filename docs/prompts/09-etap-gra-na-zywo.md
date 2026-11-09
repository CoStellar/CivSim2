# Etap 6 — backend gry na żywo: plan PR-ów

Stan na 2026-11-09. Zasady: ADR 0024 (gra na żywo), ADR 0025 (takt, pulsy, licznik lat), GDD 1.1 i 6.4, ROADMAP etap 6.
Patryk dał zielone światło na zmiany na `development` 2026-11-09 („Możesz zaczynać wprowadzać zmiany na DEV”).

## Jak przejdziemy z kroków w latach na takty (najważniejszy pomysł)

Dzisiejszy „krok cywilizacji” (ADR 0018) zostaje jako **puls**: w pulsie cywilizacja liczy gospodarkę, ludność, naukę i decyzje
AI dokładnie tak jak dziś, za tyle miesięcy, ile podaje jej era (neolit 25 lat). Zmienia się tylko **kiedy** to się dzieje:

- jedna obrotka pętli to **takt** (domyślnie 10 na sekundę przy prędkości ×1);
- każda cywilizacja ma puls co 30 taktów (3 s), ale każda w innym takcie, więc obciążenie rozkłada się równo;
- świat (klimat, odrastanie, morze) ma swój puls co 30 taktów i przesuwa licznik lat o długość kroku ery najbardziej rozwiniętej
  cywilizacji;
- akcja (kampanie wojenne, później oddziały) liczy się w taktach: jeden „miesiąc akcji” kampanii trwa 5 taktów (0,5 s), więc
  wojna **nie zatrzymuje już świata** (znany problem 4);
- liczniki wojny (gotowość do wymarszu, cicha wojna, ostatnia kampania) liczą się w taktach, a nie w latach licznika.

Dzięki temu w PR 1 gospodarka i AI zachowują się w pulsie tak samo jak dziś, a zmienia się harmonogram. Strojenie temp pod minuty
to osobny PR.

Liczby na start (`data/time.json`, do strojenia): 10 taktów na sekundę, puls co 30 taktów, miesiąc akcji = 5 taktów.

## PR-y

| PR | Gałąź | Co | Efekt dla Patryka |
| --- | --- | --- | --- |
| 1 | `feat/live-clock` | takt i pulsy (`data/time.json`, nowy `WorldClock`), kampanie i liczniki wojny w taktach, prędkości ×1/×2/×4 w serwerze i podglądzie, nowy format zapisu, nowe złote hashe | podgląd chodzi płynnie na żywo; wojna nie spowalnia świata |
| 2 | `feat/live-pace` | tempa pod pulsy: dyplomacja i umowy w czasie rzeczywistym, pomiar „ile minut do neolitu / do końca neolitu”, strojenie pod cel z GDD 1.1 | raport minut dla 3 seedów |
| 3 | `feat/units` | oddziały: osadnicy, wojownicy, zwiadowcy, tragarze jako jednostki w ruchu (takt); kampania = oddziały | oddziały chodzą po mapie |
| 4 | `feat/area-tasks` | zadania na obszarze: wydobycie, pola, łowiska, budowy w miejscu; te same komendy dla AI i gracza | AI i (w podglądzie) gracz zlecają pracę |
| 5 | `feat/start-pick` | start: losowa kolejność, wybór miejsca (5 s mapy, szare bloki, 10–15 s), strefy blokady, wybór AI z błędem, „historia plemienia” | nowy start partii w podglądzie |
| 6 | `feat/tech-mastery` | GDD 6.4: doświadczenie w technologii, kierunek rozwoju, koszt od rozwinięcia poprzedniej, podgląd technologii (5% × liczba znających) | inne tempo rozwoju zależnie od kierunku |
| 7 | `feat/decision-cards` | karty decyzji w backendzie: wydarzenie, rekomendacja doradcy, licznik, decyzja AI po czasie; bez postaci z imieniem w pierwszych erach | karty widać w podglądzie |
| 8 | `feat/live-perf` | wydajność: pulsy równolegle, klient rysuje zmiany i płynny ruch; test 300 oddziałów, takt < 30 ms | wynik wydajności |

Kolejność może się przesunąć (np. 6 przed 3), ale PR 1 musi być pierwszy.

## Do decyzji Patryka po drodze

- Liczby z `data/time.json` (takt, puls, miesiąc akcji) po obejrzeniu PR 1.
- Jak długo trwają umowy i rozejmy w czasie rzeczywistym (PR 2).
