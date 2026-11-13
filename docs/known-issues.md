# Znane problemy

Lista problemów, które wiemy, że aplikacja ma, a których jeszcze nie naprawiono. Czytaj ją na starcie sesji razem z `ROADMAP.md`.
Zanim zaczniesz coś „naprawiać”, sprawdź, czy problem nie jest tu opisany (z przyczyną i planem). Gdy naprawisz punkt, usuń go
z listy (albo przenieś do „Naprawione” z numerem PR), a gdy znajdziesz nowy, dopisz go z datą, źródłem i tym, co już wiadomo.

Źródła:
- długi bieg 4 h z 2026-11-08: [`long-runs/2026-11-08_4h_seed42/`](long-runs/2026-11-08_4h_seed42/README.md) (raport, wykresy, podsumowanie);
- ocena AI: [`ai/08-ocena-i-uwagi.md`](ai/08-ocena-i-uwagi.md) (szczegóły decyzji AI, pkt 19 = etap 5b);
- testy wsadowe: [`ai/12-testy-wsadowe.md`](ai/12-testy-wsadowe.md).

## Otwarte

| # | Problem | Skąd wiemy | Co wiadomo | Co dalej |
| --- | --- | --- | --- | --- |
| 1 | **FPS po poprawkach nie jest zmierzony.** Przed etapem 5b: średnio 52 FPS, 15% klatek poniżej 30, szarpnięcie ~145 ms co ~10 s (RTX 3060 Ti). | długi bieg 4 h | FPS szedł za pracą okna na klatkę i za pełnymi GC (gen2 co ~1,4 s). Etap 5b zmniejszył przerysowania obiektów i śmieci w pamięci, ale w chmurze nie da się uruchomić Godota. | Patryk: `-- --seed 42 --map-size standard --benchmark 3000` (podaje też liczbę pełnych GC) albo nowy długi bieg. |
| 2 | **Większość wojn o odwet kończy się bez walki** (~2/3 na kontynentach, seed 7). | `ai/08` pkt 19 | Głównie rodzic przeciw ludowi, który się od niego odłączył (secesja daje powód do odwetu). Rodzic nie ma wolnych wojowników albo dowódca uznaje szansę za zbyt małą; po 5 latach bez kampanii wojna kończy się sama białym pokojem. | Kandydat: dyplomata pyta dowódcę (K1) o szansę przed wypowiedzeniem wojny o odwet, jak przy wojnie o ziemię. Zmiana AI: zaproponuj Patrykowi. |
| 3 | **Brak dużych miast.** Wioska nigdy nie awansuje na miasto. | problem 4 Patryka | Nie ma kodu awansu wioska → miasto; GDD 5.3 wymaga do miasta rzemiosła i wody. | Decyzja Patryka z 2026-11-09: wioska zostaje na miejscu, rośnie w miasto na większym obszarze, większa wciela mniejszą przy zetknięciu; etap 7 (`ROADMAP.md`). |
| 5 | **Limit 64 cywilizacji zatrzymuje dryf.** | testy wsadowe etapu 5b | Limit liczy też wymarłe ludy. Gdy gra ma 64 ludy (1 z 8 gier w 2000 turach), secesje i dryf stają, a duże państwa mogą znowu rosnąć. | Obserwować w długich biegach; ewentualnie liczyć tylko żywe ludy (zmiana zasady GDD 3: zapytaj Patryka). |
| 6 | **Przywódcy przy długich turach.** Przy turach 25–50 lat przywódca zmienia się prawie co turę, więc jego cechy mało znaczą. | długi bieg 4 h | Kronika łączy już te zmiany w jedną linię na turę (etap 5b), ale sama mechanika się nie zmieniła. | Decyzja Patryka z 2026-11-09: w pierwszych erach bez postaci z imieniem (etap 6, ADR 0024). |
| 7 | **Jeden niestabilny test serwera.** | pamięć projektu | `ObserverLayerTests.ObserverGetsLayersAndTheFogOfAWatchedCivilization` raz na kilka przebiegów pada w całym zestawie, a sam przechodzi. | Znaleźć przyczynę (prawdopodobnie zależność od czasu); nie wyłączać testu. |
| 12 | **Puls świata co 300 taktów trwa 35–40 ms** (cel: takt < 30 ms w najgorszym przypadku). | pomiar PR 4a, 2026-11-13 (`--report`, seed 7, standard, 9000 taktów) | Wszystkie takty powyżej 30 ms (10–12 na 9000) to faza świata w takcie pulsu klimatu; było tak przed PR 4a. | PR 8 (wydajność): rozłożyć klimat na kilka taktów albo liczyć go równolegle; zamrożenie klimatu dynamicznego do brązu (ADR 0026, PR T) może go też skrócić. |
| 14 | **Gracz nie ma komend dla części dźwigni.** | przegląd rozkazów gracza, 2026-11-13 | Protokół nie ma komend wyboru badania, strategii, umów, wypowiedzenia wojny, wyprawy wojennej ani zmiany ustroju. Gracz, który weźmie dźwignię 2, 4 albo 5, nie ma czym jej prowadzić (a badanie wtedy stoi). Dźwignia 1 i 3 mają komendy (osadnicy, przeprowadzka, zadania). | P2: dopisać komendy wyboru badania i wojny/pokoju/wyprawy (te same, które wydaje AI) i pokazywać kafelki 2, 4, 5 jako „nie do wzięcia”, dopóki nie mają komend. |
| 15 | **Oddział przesunięty przez gracza czeka bez końca.** | przegląd rozkazów gracza, 2026-11-13 | Zwiadowcy i osadnicy po rozkazie „idź” stoją na miejscu (rozkaz wykonany), bo AI nie wydaje rozkazów oddziałom. Zwiadowcy: osada nie wyśle następnych, dopóki ci nie wrócą. Osadnicy: zjadają zapas i głodują. Grupa robocza po „idź” przestaje pracować. | P2: po dojściu zwiadowcy wracają sami; osadnicy czekają na „załóż obóz” z widocznym licznikiem jedzenia; w podglądzie przycisk „wróć”. |
| 13 | **Grupa robocza zagładza mały obóz.** | pomiar PR 4a, 2026-11-13 | 10 z 25 ludzi obozu poza zbieraniem jedzenia + ich jedzenie z magazynu: przy zleceniu wycinki każdemu obozowi od 25 ludzi świat spadł do 6 osad w 15 minut (`docs/systems/tasks.md`). | PR 4b: gubernator liczy koszt grupy (jedzenie i zbieracze mniej); może też mniejsza grupa w małej osadzie albo `peoplePerTask` wyżej. |
| 8 | **Ekonomia i decyzje AI to nadal większość czasu tury.** | długi bieg 4 h, benchmark 5b | Po 5b tura średnio 12 ms (95% poniżej 48 ms). `TradeNetworks.Build` liczy sieci od zera 6 razy na turę (każda osada z każdą); alokacje ~0,55 MB na turę, głównie migawka. | Gdy tura znów urośnie (więcej er, większe mapy): pamięć sieci handlowych w obrębie tury, pula tablic migawki. |

## Naprawione

| Problem | PR | Szczegóły |
| --- | --- | --- |
| Wędrówka obozu liczyła drogę przy każdym polu (10) | etap 6, `feat/battle-time` | droga raz na cel, zapamiętana i w zapisie; zajęte pola po drodze mijane |
| Zwiadowcy bez ludzi; wojownicy maszerowali 12 razy szybciej niż osadnicy (9, 11 z PR 3a) | etap 6 PR 3b | zwiadowcy biorą myśliwych; armie i tragarze jako oddziały z prędkością z `units.json` |
| Zapytanie siatki osad z bardzo dużym promieniem zawieszało takt | #69 | promień większy niż mapa przegląda każdy kubełek raz |
| Zalew epidemii i katastrof (~67 zdarzeń na turę) | #63 (`14cd39c`) | epidemie tylko w dużych cywilizacjach, katastrofy 5× rzadsze |
| Wymieranie plemion łowców (8 z 12 ludów w 135 s) | #63 (`9071460`) | własna ziemia bandy, łagodniejszy głód |
| Skaczące nazwy cywilizacji, brak stolic | #63 (`40ab078`) | stolica w największej wiosce, nazwa nad skupiskiem wokół stolicy |
| Neolityczne imperia na całe wyspy | #63 (`2ffc54f`) | zasięg władzy ustroju i dryf dalekich osad |
| Za dużo komunikatów w kronice | #63 (`3906daf`) | częste zdania łączone w jedną linię na turę |
| Tura 93 ms | #63 (`6be77cb`) | tura średnio 12 ms na seedzie 42 |
| Wojny z zemsty za morze, długi bieg bez końca | #63 (`b93f786`) | odwet tylko w zasięgu 16 pól; bieg kończy się po 10 000 lat bez zmian |
| Wiele wojen spowalnia czas gry (lud w wojnie liczył co miesiąc, 2000 tur ≈ 11 600 lat) | etap 6 PR 1 (`feat/live-clock`) | zegar gry na żywo (ADR 0025): kampanie i liczniki wojny w taktach, świat nigdy nie czeka na wojnę |
