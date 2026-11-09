# Długi bieg 4 h — seed 42, mapa standardowa (archipelag), 8 listopada 2026

Analiza z 2026-11-08 (wątek „długi bieg 4 h”). Pełne CSV zostały u Patryka: `I:\CivSim\CivSim2\long_runs\2026-11-08_09-20_seed42_standard`

## Najkrócej

- **Stabilność techniczna: dobra.** 4 h bez błędu, bez tury dłuższej niż 0,5 s, pamięć płaska (960 → 1059 MB, szczyt 1105 MB), okno nigdy nie zostało w tyle za serwerem.
- **Wydajność: za słabo.** Tura średnio **93 ms** (cel dla mapy standardowej < 50 ms). FPS średnio **52**, 15% klatek poniżej 30 FPS.
- **Symulacja: po 2,5 minuty świat zamarł.** 8 z 12 cywilizacji wymarło w pierwszych 135 sekundach, potem przez 3 h 57 min (3,8 mln lat gry!) 4 cywilizacje stały w miejscu, bez ani jednej wojny.

## Co z tego naprawiono (etap 5b, PR #63)

Patryk zatwierdził całą listę 2026-11-08 („wszystko robimy”). Każdy punkt ma osobny commit; szczegóły w `ROADMAP.md`, etap 5b.

| Problem z biegu | Poprawka | Commit | Stan |
| --- | --- | --- | --- |
| zalew epidemii i katastrof | epidemie tylko w cywilizacjach od 10 000 ludzi (osady od 300), katastrofy 5× rzadsze | `14cd39c` | naprawione ([wykres](img/6_zdarzenia_przed_po.png)) |
| łowcy wymierają (8 z 12 ludów) | „własna ziemia” bandy, łagodniejszy głód w obozie | `9071460` | naprawione (0 wymarłych w 8 grach zamiast 2,8) |
| skaczące nazwy, brak stolic | stolica przenosi się do największej wioski; nazwa nad skupiskiem wokół stolicy | `40ab078` | naprawione |
| neolityczne imperia na całe wyspy | zasięg władzy ustroju i dryf dalekich osad (GDD 7.3) | `2ffc54f` | naprawione ([mapa](img/8_mapa_wplywow_przed_po.png)) |
| za dużo informacji na ekranie | częste zdania łączone w jedną linię na turę | `3906daf` | naprawione (kronika ~3× krótsza) |
| tura 93 ms, nierówny FPS | rzadsze przerysowania obiektów, mniej śmieci w pamięci | `6be77cb` | tura naprawiona (12 ms); **FPS niezmierzony** |
| wojny z zemsty bez walki, długi bieg bez końca | odwet tylko w zasięgu 16 pól; koniec biegu po 10 000 lat bez zmian | `b93f786` | częściowo (zob. [znane problemy](../../known-issues.md)) |

Porównanie przed i po na 8 grach: [7_przed_po_poprawkach.png](img/7_przed_po_poprawkach.png).

## 1. Wydajność

| Co | Wynik | Cel / ocena |
| --- | --- | --- |
| Czas tury | śr. 93 ms, 95% < 116 ms, max 475 ms | cel < 50 ms — **2× za wolno** |
| Z czego | decyzje AI 40 ms, ekonomia 38 ms (razem 84%) | reszta < 5 ms każda |
| Procesor | 16,5% (≈ 2 z 12 wątków) | symulacja praktycznie jednowątkowa |
| Pamięć | 1059 MB na końcu, płasko w drugiej połowie | OK (brak wycieku) |
| FPS | śr. 52, zakres 38–70, 15% klatek < 30 FPS | nierówno |
| Najdłuższa przerwa między klatkami | ~145 ms w prawie każdym 10-sekundowym oknie | regularne „szarpnięcie” |
| GC | 10 484 pełne sprzątania (gen2) = co ~1,4 s; łącznie 68 s pauz | dużo |

Co z tego wynika dla FPS (problem 1):
- FPS niemal idealnie idzie za czasem pracy okna na klatkę (korelacja −0,97) i za pauzami sprzątania pamięci (−0,64).
- Mapa (osady, napisy, jednostki) jest **przerysowywana od zera przy każdej nowej migawce**, czyli ~10 razy na sekundę przy maksymalnej prędkości, plus przeliczanie zabarwienia fragmentów mapy.
- Symulacja i okno to jeden proces .NET, więc pełne sprzątanie pamięci po stronie symulacji zatrzymuje też okno. Stąd regularne szarpnięcia ~145 ms.

## 2. Symulacja

### Przebieg
| Czas biegu | Co się działo |
| --- | --- |
| 0–9 s | 4 cywilizacje odkrywają rolnictwo (rok 7100–10 225). Pierwsze 2 wymierają |
| 21 s | jedyne 2 wojny w całym biegu (Osxou ↔ Dreakreara, „for loot”), zakończone białym pokojem bez walki |
| do 135 s | wymiera łącznie 8 cywilizacji — **dokładnie te, które nie odkryły rolnictwa** |
| 135 s – 4 h | 4 cywilizacje (wszystkie neolityczne), ~570 osad, ~400 tys. ludzi, sojusze i handel, **zero wojen** |
| koniec | rok 3 823 725, wciąż neolit (dalszych er jeszcze nie ma) |

### Anomalie
1. **Przepaść między łowcami a rolnikami.** Odtworzyłem początek gry w chmurze (2500 tur, wykres 2): plemiona bez rolnictwa stoją na **25–40 osobach** (część na 300–700) przez 60 tys. lat. Rolnictwo daje skok **×100** (do 40–80 tys.) w ciągu ~1000 lat. Takie mikroplemiona giną od losowych katastrof i epidemii. U Patryka wszystkie 8 zginęło bez wojny (przed każdym wymarciem: niepokoje, zamach stanu, kolejni przywódcy).
2. **Neolityczne „imperia” (problem 5).** Każda z 4 cywilizacji ma ~140 osad i zajmuje całą wyspę (mapa 5). Rozrost nie ma hamulca: osady zakładają nowe co turę (1,2 nowej osady na turę przez 4 h).
3. **Brak inwazji (problem 6).** W tym biegu wojen w ogóle nie było: archipelag, a w neolicie nie ma łodzi, więc sąsiedzi nie mają się jak dosięgnąć; ci, którzy mogą, mają pakty o nieagresji i sojusze obronne. Problem z chmury (~7000 wojen „z zemsty”) **tu się nie pojawił** — to inny seed/mapa (kontynenty). Trzeba go sprawdzić osobno.
4. **Przywódcy zmieniają się co turę.** Tura trwa 25–50 lat, więc „Elder X died at 65; 6 more leaders followed” pojawia się co turę. Cechy przywódcy (zdobywca, ostrożny…) przestają mieć znaczenie, a „2 lata anarchii” są krótsze niż tura.
5. **Neolit bez końca.** 3,8 mln lat w neolicie. To oczekiwane, bo kolejne ery dopiero w etapach 6–12, ale bieg powyżej ~5 minut niczego nowego już nie mówi.
6. **Zalew zdarzeń (problem 2).** Średnio **67 zdarzeń na turę**: 41 katastrof naturalnych, 19 epidemii, 5 wędrówek/zmian przywódców. Przy 10 turach/s to ~670 komunikatów na sekundę. Limit 1 mln linii w events.csv osiągnięty po 3 h 54 min.
7. **Skaczące nazwy cywilizacji (problem 3) — przyczyna znaleziona.** Napis stoi nad **najludniejszą osadą** cywilizacji, a ta zmienia się prawie co turę. Stąd skakanie.
8. **Brak stolic (problem 4).** W danych nie ma pojęcia stolicy; „najludniejsza osada” jest tylko do napisu.

## 3. Proponowane poprawki (kolejność)

| # | Poprawka | Problem Patryka | Zmienia zasady GDD? |
| --- | --- | --- | --- |
| 1 | **Stolica** jako stała osada cywilizacji (zmienia się tylko przy jej utracie/przeniesieniu); napis nad środkiem największego skupiska osad, z wygładzaniem, przeliczany rzadko | 3, 4 | tak (stolica) |
| 2 | **Tempo neolitu:** limit zasięgu władzy (osady dalej niż X dni drogi od stolicy odłączają się jako nowe plemiona), wolniejsze zakładanie osad, mniejszy skok ludności po rolnictwie; plemiona łowców większe i trwalsze (min. kilkaset osób, mniej śmiertelne katastrofy) | 5, 7 | tak — najpierw propozycja do GDD |
| 3 | **Wydajność okna:** przerysowanie obiektów tylko co N migawek / tylko zmienione fragmenty, limit przerysowań na sekundę przy prędkości Max, mniej alokacji w migawkach (mniej pełnych GC) | 1 | nie |
| 4 | **Wydajność symulacji:** profil decyzji AI i ekonomii, równoległe liczenie AI na migawce (już dozwolone przez CLAUDE.md), cel < 50 ms | 1 | nie |
| 5 | **Zdarzenia:** łączenie drobnych (katastrofy, epidemie, wędrówki) w podsumowania na cywilizację; na ekranie tylko ważne (wojny, wymarcia, ery, przewroty) | 2 | nie |
| 6 | **Przywódcy przy długich turach:** jeden komunikat na turę, ewentualnie „rada/dynastia” zamiast pojedynczej osoby w erach 50/25 lat | — | tak (drobna) |
| 7 | **Inwazje i wojny z zemsty:** osobny bieg na kontynentach (seed 7), sprawdzić, czy wojna kończy się podbojem/zniszczeniem i dlaczego „zemsta” kończy się bez walk | 6, 8 | prawdopodobnie |
| 8 | **Długi bieg:** opcja zatrzymania po X latach gry lub gdy świat stoi w miejscu; kolejny test na kontynentach | — | nie |

## Wykresy
- [1_przebieg_swiata.png](img/1_przebieg_swiata.png) — żywe cywilizacje, ludność, wojny (oś czasu logarytmiczna)
- [2_ludnosc_cywilizacji.png](img/2_ludnosc_cywilizacji.png) — ludność każdej cywilizacji (odtworzenie w chmurze)
- [3_wydajnosc.png](img/3_wydajnosc.png) — fazy tury i rozkład FPS
- [4_zdarzenia_na_ture.png](img/4_zdarzenia_na_ture.png) — ile czego dzieje się na turę
- [5_mapa_wplywow_odtworzenie.png](img/5_mapa_wplywow_odtworzenie.png) — mapa wpływów po 2500 turach (odtworzenie)

Uwaga: odtworzenie w chmurze używa tego samego seeda, rozmiaru i typu świata, ale przebieg nie pokrył się co do tury (prawdopodobnie inne ustawienie z menu startowego). Pokazuje mechanizm, nie dokładnie te same cywilizacje.
