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
| 2 | `feat/live-pace` | tempa pod pulsy: dyplomacja i umowy w czasie rzeczywistym, pomiar „ile minut do neolitu / do końca neolitu”, strojenie pod cel z GDD 1.1; **T1** (sprawdzić nadrabianie taktów przy ×4) | raport minut dla 3 seedów |
| 3a | `feat/units` | osadnicy i zwiadowcy jako oddziały w taktach, rozkazy gracza i AI, wędrówki obozów w taktach, wioski bez przeprowadzek; **T2** siatka przestrzenna, **T3** A\* z kosztem terenu (raz na cel), **T4** pula w płaskich tablicach (takt bez alokacji), **T5** rozkazy i przejścia, **T9** `units.json`, **T12** budżet w opisie PR | osadnicy i zwiadowcy chodzą po mapie |
| 3b | `feat/units-war` | wojownicy i tragarze jako oddziały; kampania = oddziały; stany walki, ucieczki, odpoczynku (T5); zwiadowcy z łowców | armie chodzą po mapie |
| 4 | `feat/area-tasks` | zadania na obszarze: wydobycie, pola, łowiska, budowy w miejscu; te same komendy dla AI i gracza; **T6** mapy wpływu i zagrożenia dla dowódcy, **T9** zadania w JSON | AI i (w podglądzie) gracz zlecają pracę |
| 5 | `feat/start-pick` | start: losowa kolejność, wybór miejsca (5 s mapy, szare bloki, 10–15 s), strefy blokady, wybór AI z błędem, „historia plemienia” | nowy start partii w podglądzie |
| 6 | `feat/tech-mastery` | GDD 6.4: doświadczenie w technologii, kierunek rozwoju, koszt od rozwinięcia poprzedniej, podgląd technologii (5% × liczba znających) | inne tempo rozwoju zależnie od kierunku |
| 7 | `feat/decision-cards` | karty decyzji w backendzie: wydarzenie, rekomendacja doradcy, licznik, decyzja AI po czasie; bez postaci z imieniem w pierwszych erach; **T9** karty w JSON | karty widać w podglądzie |
| 8 | `feat/live-perf` | wydajność: pulsy równolegle, klient rysuje zmiany i płynny ruch; test 300 oddziałów, takt < 30 ms; **T7** sieci handlowe przyrostowo, **T8** pule pamięci migawki, **T10** MultiMesh, **T12** budżet czasu w teście, **T3b** pole przepływu, jeśli pomiar pokaże potrzebę | wynik wydajności |

Kolejność może się przesunąć (np. 6 przed 3), ale PR 1 musi być pierwszy.

## Uzupełnienia techniczne T1–T14 (Patryk, 2026-11-09)

Po przeglądzie planu i kodu (sesja projektu w claude.ai, 15:00) Patryk poprosił o dopisanie wszystkich poniższych rzeczy do planu.
Numery T są wpisane w tabelę PR-ów wyżej. Kolejność ważności: **T2 > T7 > równoległe pulsy (PR 8)**.

| # | Co | Dlaczego | PR | Gotowe, gdy |
| --- | --- | --- | --- | --- |
| T1 | **Nadrabianie taktów:** pętla w `LocalServerHost` już jest najwyżej 1 takt do tyłu; sprawdzić przy ×4 na mapie standard, czy ciężki puls nie robi „spirali” (coraz większe zaległości) | przy ×4 budżet taktu to 25 ms | 2 | pomiar ×4 w opisie PR 2: odsetek taktów spóźnionych |
| T2 | **Siatka przestrzenna** (kubełki na bazie `MapChunks`): „co jest w promieniu N pól” bez porównywania każdej osady i oddziału z każdym; użyć też w ocenie ziemi (PR C) i zbiórce wojowników | dziś porównania każdy z każdym (PR C: tura 16 → 52 ms); oddziały to pomnożą | **3, przed oddziałami** | zapytanie o promień nie zależy od liczby osad; test zgodności z wynikiem „każdy z każdym” |
| T3 | **A\* z kosztem terenu** zamiast BFS w `PathFinder` (heurystyka: odległość z `HexGlobe`, koszt pola z biomu w danych), ścieżka liczona raz przy rozkazie i ponownie tylko przy blokadzie; pamięć ostatnich ścieżek | oddziały w sekundach muszą omijać góry i bagna; BFS nie zna kosztu | 3 | test: droga omija kosztowny teren; brak liczenia ścieżek co takt |
| T3b | **Pole przepływu** (flow field): jedna mapa kierunków do celu dla wielu oddziałów (zbiórka armii, migracja) | opłaca się dopiero przy dziesiątkach oddziałów do jednego celu | 8, tylko jeśli pomiar pokaże potrzebę | — |
| T4 | **Oddziały w płaskich tablicach** (struktura tablic, id liczbowe, pula), bez pełnego ECS | 300 oddziałów nie potrzebuje ECS (Arch, Friflo), a tablice dają szybki takt i zero alokacji | 3 | zero alokacji w takcie z oddziałami (pomiar) |
| T5 | **Stany oddziału** jako prosta maszyna stanów (idzie → walczy → ucieka → wraca → odpoczywa) | taktyka oddziału; tania i przewidywalna | 3 | stany i przejścia opisane w `docs/systems/` i `docs/ai/` |
| T6 | **Mapy wpływu i zagrożenia** liczone w pulsie (nie w takcie) z tego, co cywilizacja widzi; z nich dowódca wybiera cele i drogi | lepsze cele wojenne i bezpieczne trasy tragarzy bez ukrytej wiedzy | 4 (albo 3, jeśli dowódca tego potrzebuje) | decyzje dowódcy w dzienniku „Dlaczego?” pokazują wpływ mapy |
| T7 | **Sieci handlowe przyrostowo:** Union-Find (zbiory rozłączne), przebudowa tylko po zmianie dróg lub osad (znany problem 8) | `TradeNetworks.Build` liczy od zera 6× na turę | 8 (albo wcześniej, gdy puls przekroczy budżet) | ten sam hash co obliczenie od zera; czas pulsu w benchmarku |
| T8 | **Pule pamięci migawki** (`ArrayPool`, wielokrotne tablice), bez LINQ i alokacji w gorących pętlach | ~0,55 MB śmieci na turę, pełne GC = szarpnięcia (znane problemy 1 i 8) | 8 | alokacje na takt i puls w benchmarku |
| T9 | **Nowa zawartość w danych:** definicje oddziałów, zadań na obszarze i kart decyzji w JSON, walidowane przy wczytaniu | zasada 5 z `CLAUDE.md`; strojenie bez kodu | 3, 4, 7 | nowy oddział/zadanie/karta bez zmiany kodu |
| T10 | **Rysowanie oddziałów przez MultiMesh** w Godocie (zamiast `_Draw` w `MapObjects`) i płynne dorysowanie ruchu między taktami | setki obiektów w jednym wywołaniu rysowania | 8 | FPS z 300 oddziałami (pomiar Patryka na jego karcie) |
| T11 | **Zapis partii = seed + lista rozkazów** (powtórka) i **hasz stanu co N taktów z podziałem na systemy** | odtwarzanie błędów, powtórki, wykrywanie rozjazdu determinizmu z wskazaniem systemu | 3 albo osobny mały PR po 2 | test: powtórka z rozkazów daje ten sam hasz **Zrobione** (`feat/replay`, `docs/systems/replay.md`). |
| T12 | **Budżet czasu w teście/benchmarku:** takt < 30 ms i puls w budżecie, sprawdzane przy każdym PR (lokalnie, bez CI) | wydajność od pierwszego dnia | od 3, pełny w 8 | wynik w opisie każdego PR |
| T13 | **Pulsy równolegle** (`Parallel.For` po cywilizacjach, każda pisze do swojego bufora, wyniki łączone w kolejności id, losowanie ze strumieni cywilizacji) | ADR 0025; zgodne z zasadą 4 z `CLAUDE.md` | 8 | ten sam hasz przy 1 i N wątkach |
| T14 | **Dane:** schemat JSON (walidacja kluczy, literówka = błąd przy wczytaniu) i przeładowanie danych w podglądzie bez restartu | szybsze strojenie temp z PR 2 i dalej | 2 albo osobny mały PR | zła nazwa klucza daje czytelny błąd; zmiana JSON widoczna po przeładowaniu **Zrobione** (`feat/data-schema`, `docs/systems/data-schema.md`). |

### Czego unikać (stałe zasady etapu)

- Liczb zmiennoprzecinkowych w symulacji, iteracji po `Dictionary`/`HashSet`, czasu systemowego w decyzjach, `Parallel` bez scalania po id.
- Liczenia ścieżek co takt; LINQ i alokacji w gorących pętlach.
- Przepisywania rdzenia na ECS ani zmiany silnika (ADR 0025).
- GOAP, MCTS i uczenia maszynowego w AI: zostaje Utility AI z krzywymi z danych (`ai.json`, `DeadValueTests`).
- Sieć w etapie 9: serwer autorytatywny z różnicami stanu, nie lockstep (zasada 10 z `CLAUDE.md`, bez zmian).

## PR 4: zadania na obszarze (decyzje Patryka z 2026-11-12, GDD 4.4)

Patryk wybrał rekomendacje: żywność ze strefy automatycznie, reszta przez zadania; jedna grupa robocza na zadanie, bez tragarzy;
grupy można napaść, AI omija zagrożone pola (T6); w PR 4 wycinka, kamieniołom i krzemień, pola, łowiska **i budowa w miejscu** (palisada).

Podział (każdy osobny PR do `development`):

| PR | Gałąź | Co |
| --- | --- | --- |
| 4a | `feat/area-tasks` | `data/tasks.json` (rodzaj, dobro, biomy lub złoże, ludzie, wydajność na osobę na rok, wyczerpanie lub odrastanie, technologia); stan zadań w płaskich tablicach; komendy `StartTaskCommand` / `EndTaskCommand` (gracz i AI); grupa robocza jako oddział (rola `workers` w `units.json`); urobek w pulsie; drewno i krzemień tylko z zadań; zapis, hasz (nowa część `tasks`), protokół, podgląd (grupa na mapie, obszar zadania) |
| 4b | `feat/area-tasks-ai` | gubernator wybiera zadania z cen wewnętrznych i potrzeb (Utility AI, parametry w `ai.json`, `DeadValueTests`); łowiska i pola uprawne jako zadania (rolnictwo z obecnego `Farming` przeniesione na pola zadań); statystyki z 3 seedów, `docs/ai/` |
| 4c | `feat/threat-map` | **T6** mapy wpływu i zagrożenia w pulsie z tego, co cywilizacja widzi; napaść na grupę roboczą, ucieczka; dowódca i gubernator omijają zagrożone pola; dziennik „Dlaczego?” |
| 4d | `feat/build-on-site` | budowa w miejscu: postęp budowy z pracy grupy, palisada (obrona osady w bitwie), dane budowli w JSON gotowe na mur i zbrojownię z etapu 7 |

Do ustalenia po drodze (z rekomendacją; liczby zmienię w danych po pokazaniu efektu):

- Zasięg zadania: pole w strefie wpływów osady albo do 2 pól za nią; obszar = pole i 6 sąsiadów. *Rekomendacja: tak.*
- Wielkość grupy: domyślnie 10 osób na zadanie, najwyżej 1 zadanie na 25 mieszkańców. *Rekomendacja: tak, do strojenia.*
- Krzemień: złoża z `resources.json` (dziś zbierany ze strefy) wyczerpują się; kamień z wzgórz i gór bez wyczerpania w neolicie.
- Palisada: +50% obrony osady w bitwie, budowa ok. 2 lat akcji grupą 10 osób, potrzebuje drewna.

## Do decyzji Patryka po drodze

- Liczby z `data/time.json` (takt, puls, miesiąc akcji) po obejrzeniu PR 1.
- Jak długo trwają umowy i rozejmy w czasie rzeczywistym (PR 2).
