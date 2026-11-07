# Statystyki: co AI robi w prawdziwych grach

[← spis treści](README.md)

Trzy gry bez grafiki: mapa standard (256×160), 12 cywilizacji, 1000 kroków świata (ok. 27–29 tys. lat), seedy 7, 23 i 42,
gałąź `feat/skills-learning` (etap 4F, PR 2: nauka fachu). W nawiasach liczby sprzed PR 2 (`development`, commit `448a104`,
po PR C i PR 1 etapu 4F).
Polecenie:

```
dotnet run --project src/CivSim.Headless -c Release -- --seed 7 --turns 1000 --map-size standard --decisions why7.csv --stats stats7.csv
```

Wykres: `python3 docs/ai/tools/decisions_chart.py <katalog z why7.csv, why23.csv, why42.csv>` (potrzebuje matplotlib).

![Ile decyzji podjęło AI](img/decyzje_1000_tur.png)

## Ludzie w technologiach (etap 4F, PR 3)

Technologie i nauka przed PR 3 i po nim (`python3 docs/ai/tools/research_chart.py <katalog>`):

![Badania przed i po](img/badania_przed_po.png)

| Seed | Pierwszy neolit (krok, rok) | Technologie na końcu (średnio, najwięcej) | Ludzie na końcu | Żywe cywilizacje |
| --- | --- | --- | ---: | ---: |
| 7 | 120, rok 5800 (110, rok 5500) | 9,4 / 14 (9,5 / 14) | 238 508 (225 307) | 12 (11) |
| 23 | 120, rok 5825 (100, rok 5000) | 13,6 / 14 (9,2 / 14) | 325 519 (279 639) | 9 (9) |
| 42 | 100, rok 4800 (100, rok 4825) | 9,6 / 14 (10,8 / 14) | 221 459 (279 158) | 10 (8) |

- **Nauka ma to samo tempo** (prawy wykres): myśliciele dają jej większość, a liczby przeskalowałem tak, żeby cywilizacja o zwykłym
  składzie badała jak przedtem.
- **Neolit 0–20 kroków później:** rolnictwo czeka na 10 myślicieli (cywilizacja ok. 330 ludzi).
- **Małe cywilizacje stają na 8–10 technologiach:** nie mają np. 30 rolników do nawadniania ani 20 do garncarstwa i chleba. Duże
  dochodzą do wszystkich 14.
- Gra jest chaotyczna: wyniki seedów rozjeżdżają się w obie strony (seed 23 więcej ludzi i technologii, seed 42 mniej).

## Fachy (etap 4F, PR 2)

Ludność, obozy i fachowcy przed PR 2 i po nim (`python3 docs/ai/tools/skills_chart.py <katalog>`, pliki `before*.csv` i
`after*.csv` z `--stats`):

![Fachy przed i po](img/fachy_przed_po.png)

| Seed | Pierwszy neolit (krok, rok) | Ludzie na końcu | Żywe cywilizacje | Technologie (średnio) | Wioski | Obozy | Odległość od startu (pola) |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 7 | 110, rok 5500 (110, rok 5275) | 225 307 (216 297) | 11 (10) | 9,5 (9,2) | 260 (334) | 35 (12) | 19 (20) |
| 23 | 100, rok 5000 (100, rok 4950) | 279 639 (285 848) | 9 (7) | 9,2 (14,0) | 396 (443) | 39 (3) | 27 (22) |
| 42 | 100, rok 4825 (90, rok 4475) | 279 158 (266 084) | 8 (7) | 10,8 (12,1) | 335 (407) | 34 (3) | 24 (24) |

Fachowcy na końcu (seed 23): rolnicy 103 tys., myśliwi 56 tys., rybacy 38 tys., pasterze 15 tys., krzemieniarze 4,1 tys.,
garncarze 2,4 tys., kamieniarze 1,1 tys., piekarze 870, tkacze 770, garbarze 600.

Co z tego widać:

- **Rolnictwo rozchodzi się z mistrzami.** Obóz osiada dopiero z rolnikiem, więc wioski powstają wolniej, a część ludzi zostaje
  koczownikami na stałe (30–40 obozów zamiast 3–12). Przeprowadzek obozów jest 3–4 razy więcej (6700–8600 zamiast 1500–2600),
  bo tych obozów jest więcej. Historycznie zbieracze też długo żyli obok rolników.
- **Osiedlenie:** mediana jedzenia wioski przy osiedleniu 183% potrzeb (150–279%), z rolnikami jako połową producentów.
- **Receptur jest mniej** (1500–1900 zmian zamiast 2700–5000): receptura bez fachowców jest warta zero, więc skarbnik rzadziej skacze
  między nimi.
- **Wysyłanie osadników** (G7) 600–900 razy zamiast 420–470: wioski dzielą się częściej, bo mniej obozów osiada samodzielnie.
- **Neolit 0–10 kroków później**, średnio mniej technologii u żywych cywilizacji, ale więcej cywilizacji przeżywa (także małe).
  To jeszcze bez wymogów ludzi w technologiach (PR 3).

## Ziemia i migracje (PR C)

Przed PR C i po nim: jak daleko od startu żyją ludzie, zamieszkane osady i wioski (`python3 docs/ai/tools/land_chart.py <katalog>`,
pliki `before*.csv` i `after*.csv` z `--stats`):

![Ziemia i migracje przed i po](img/ziemia_przed_po.png)

| Seed | Pierwszy neolit (krok, rok) | Ludzie na końcu | Żywe cywilizacje | Technologie (średnio) | Zamieszkane osady | Wioski | Odległość od startu (pola) |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 7 | 110, rok 5275 (120, rok 5850) | 216 297 (113 050) | 10 (10) | 9,2 (10,4) | 346 (83) | 334 (80) | 20 (12) |
| 23 | 100, rok 4950 (100, rok 4800) | 285 848 (163 527) | 7 (9) | 14,0 (14,0) | 446 (129) | 443 (129) | 22 (10) |
| 42 | 90, rok 4475 (90, rok 4425) | 266 084 (117 473) | 7 (8) | 12,1 (9,5) | 410 (97) | 407 (91) | 24 (11) |

Co z tego widać:

- **Ludzie rozchodzą się po świecie.** Przed PR C liczba osad spadała po turze ~200 (obozy osiadały i stały w miejscu), a ludzie
  zostawali ok. 10–12 pól od startu. Teraz wioski wysyłają osadników (G7, 424–473 grup na grę), osad przybywa do tury ~500–800,
  a ludzie żyją średnio 20–24 pola od startu. Ludności jest 1,6–2,3 razy więcej.
- **Osiedlenie jest prawdziwym rachunkiem.** Mediana plonu z pól przy osiedleniu spadła z ok. 1640% do 280%, a rolnictwo musi
  dać więcej niż zbieractwo (mediana `over gathering` 292%). Osiedleń jest 2–3 razy więcej, bo osiadają też obozy osadników.
- **Wyprawy w nieznane (G8) są rzadkie**: 8, 12 i 97 na grę, prawie tylko w cywilizacjach ciekawych i ekspansywnych
  (`exploreAbovePerMille` = 1,5). Dalekich wędrówek (G3) jest więcej (23–38 zamiast 1–11), bo obóz idzie teraz 20 pól na stulecie.
- **Więcej zmian receptur** (2700–5000 zamiast 1350–1900): osad jest więcej, a każda wybiera recepturę.
- **Neolit w tym samym czasie** (±10 kroków): zmiana dotyczy tego, co dzieje się po osiedleniu, a nie tempa rolnictwa.
- Tura jest dłuższa, bo osad jest więcej ([uwagi, pkt 10](08-ocena-i-uwagi.md)).

Przed PR B i po nim: ludność, technologie i zmiany receptur (`python3 docs/ai/tools/values_chart.py <przed> <po>`, katalogi ze
`stats*.csv` i `why*.csv`):

![Wartości AI przed i po](img/wartosci_przed_po.png)

Rytmy cywilizacji i ludność przed zegarem i po nim (ADR 0018, `python3 docs/ai/tools/clock_chart.py <przed> <po>`):

![Rytmy cywilizacji i ludność przed/po](img/zegar_rytmy.png)

## Najważniejsze liczby przed PR B i po nim (historia)

| Seed | Pierwszy neolit (krok, rok) | Ludzie na końcu | Żywe cywilizacje | Technologie (średnio) | Wioski |
| --- | --- | ---: | ---: | ---: | ---: |
| 7 | 120, rok 5850 (160, rok 7800) | 113 050 (106 452) | 10 (8) | 10,4 (7,8) | 80 (79) |
| 23 | 100, rok 4800 (140, rok 6875) | 163 527 (117 698) | 9 (7) | 14,0 (13,0) | 129 (81) |
| 42 | 90, rok 4425 (150, rok 7350) | 117 473 (103 500) | 8 (7) | 9,5 (10,6) | 91 (66) |

## Liczba decyzji w 1000 tur (etap 4F PR 2; w nawiasach przed PR 2)

| Decyzja | Id | Seed 7 | Seed 23 | Seed 42 |
| --- | --- | ---: | ---: | ---: |
| strateg ustawia strategię | S1 | 612 (550) | 501 (497) | 519 (484) |
| uczony wybiera badanie | U1 | 113 (103) | 97 (113) | 102 (101) |
| skarbnik zmienia recepturę | T1 | 1523 (4992) | 1905 (2702) | 1878 (2966) |
| obóz przenosi się | G2 | 6984 (2244) | 6686 (1516) | 8583 (2616) |
| zajęcie pustej osady (przeniesienie albo odłam) | G2, G5 | 496 (304) | 473 (281) | 577 (276) |
| wypędzenie obcego obozu | G2 | 129 (95) | 13 (5) | 34 (24) |
| odłam zakłada nowy obóz | G5 | 833 (781) | 880 (773) | 865 (677) |
| mały obóz dołącza do innego | G1 | 1063 (778) | 1163 (727) | 1590 (761) |
| obóz osiada (wioska) | G4 | 371 (428) | 473 (541) | 437 (500) |
| wioska wysyła osadników | G7 | 607 (424) | 739 (473) | 895 (469) |
| wyprawa w nieznane | G8 | 6 (8) | 16 (12) | 231 (97) |
| daleka wędrówka | G3 | 139 (32) | 36 (38) | 88 (23) |
| zabranie porzuconych dóbr | G6 | 3 (1) | 0 (1) | 0 (1) |
| dyplomata proponuje umowę albo pokój | D2 | 374 (184) | 351 (98) | 882 (613) |
| dyplomata przyjmuje propozycję | D1 | 29 (44) | 19 (23) | 25 (30) |
| dyplomata odrzuca propozycję | D1 | 330 (114) | 318 (63) | 836 (565) |
| dyplomata wypowiada wojnę | D2 | 8 (10) | 2 (0) | 7 (1) |
| **razem** | | **13620** (11092) | **13672** (7863) | **17549** (10204) |

Tabela po PR C (z liczbami sprzed PR C) jest w historii tej strony w gicie (commit `448a104`).

Co widać po PR B (historia):

- **Rolnictwo przychodzi wcześniej.** Uczony wycenia wioski z żyzności strefy, a ogień i krzemień z drogi do rolnictwa, więc
  rolnictwo jest zwykle 2. albo 3. badaniem. Pierwszy neolit wypada o 40–60 kroków (ok. 2000–2900 lat) wcześniej. **To duża zmiana
  tempa gry, do Twojej oceny.** Jeśli neolit ma przychodzić później, wystarczy obniżyć `scholar.villageWorthPerPerson` w
  `data/ai.json` (dziś 0,1).
- **Mniej decyzji obozów** (przeprowadzki, odłamy, dołączenia o ok. 30–50% mniej): obozy wcześniej stają się wioskami, a wioski nie
  mają decyzji gubernatora. Więcej ludzi i więcej żywych cywilizacji na końcu.
- **Mniej wypędzeń** (o 50–80%): obcy obóz można zaatakować tylko wtedy, gdy cywilizacja widziała go w swoim ostatnim kroku.
- **Mniej dyplomacji:** mniej propozycji i odrzuceń, bo mniej cywilizacji się kłóci o te same obozowiska. Wojen 2–8 (1–19).
- **Strateg robi więcej przeglądów** w seedach 7 i 23, bo więcej cywilizacji przeżywa i wcześniej zmienia erę (przegląd od razu po
  wejściu w nową erę).
- **Skarbnik:** przełączanie „receptura 1 ↔ brak receptury” prawie zniknęło (1811, 1109 i 1950 zmian → 242, 0 i 0). Zostało
  przełączanie odzież ↔ chleb (955, 703 i 907 zmian): cena jedzenia skacze z plonami, więc raz wygrywa chleb, raz odzież. To już
  prawdziwa zmiana sytuacji, ale jest jej dużo ([uwagi](08-ocena-i-uwagi.md)).
- Gra jest chaotyczna: od neolitu każda drobna zmiana prowadzi do innej historii, więc pojedyncze różnice w tabeli nie są „skutkiem”
  PR B. Wnioski wyżej powtarzają się we wszystkich trzech seedach.

## Przykładowe wpisy „Dlaczego?” (jeden na rodzaj decyzji)

| Seed, tura | Cywilizacja | Decyzja | Ocena | 3 najważniejsze czynniki |
| --- | --- | --- | ---: | --- |
| 7, 1 | Civilization 1 | S1 SetStrategy | 1000 | crowding 33%, fed 1000, food 59917 |
| 7, 1 (PR B) | Niakiia | U1 SetResearch (ogień) | 1 044 | serves security, leads to 322, research cost 100% (koszt 4200) |
| 23, 3 | Civilization 4 | G2 MoveCamp | 46 209 | distance 73% (9 pól), direction 115% (2 pola bliżej celu), character 89% |
| 7, 25 | Civilization 8 | G2 Conquer | 19 983 | character 112% (agresja 620), distance 91% (3 pola), opinion 98% (sympatia +4) |
| 7, 26 | Civilization 10 | G2 Occupy | 23 112 | distance 79% (7 pól), direction 115%, own old campsite 110% |
| 23, 24 | Civilization 1 | G3 Journey | 37 161 | goal weight 137% (ekspansja), character 81%, distance 83% (17 pól) |
| 7, 168 | Civilization 1 | G4 Settle | 126 000 | farm yield 1563%, people 78 |
| 7, 84 (PR C) | Lakhaem | G4 Settle | 28 314 | farm yield 299%, over gathering 191, people 92 |
| 7, 83 (4F) | Lakhaem | G4 Settle | 12 600 | farm yield 159%, over gathering 100, people 77 |
| 7, 93 (PR C) | Lakhaem | G7 SendSettlers | 10 538 | character 76%, distance 82% (6 pól), food 16713 |
| 42, 2 (PR C) | Osxou | G8 Explore | 37 | character 155%, direction 115% (4), unknown 21 |
| 7, 5 | Civilization 1 | G5 SplitCamp | 78 669 | character 120%, direction 115%, distance 88% (4 pola) |
| 7, 21 | Civilization 10 | G1 JoinCamp | 13 | people 13 |
| 7, 419 | Civilization 4 | G6 Plunder | 22 081 | goods left 22081 |
| 7, 27 (PR B) | Lakhaem | T1 SetRecipe (narzędzia kamienne) | 274 488 | value added 313700, serves prosperity, inputs available 100% |
| 7, 55 (PR B) | Rishboy | T1 SetRecipe (narzędzia kamienne) | 527 438 | value added 313700, serves prosperity, for trade 363 |
| 7, 12 (4d) | Civilization 8 | D2 ProposeTreaty → Civ 11 | 1 042 | character 91%, opinion 108% (sympatia +4), trade gain 105% (28%) |
| 7, 13 (4d) | Civilization 12 | D1 AcceptTreaty ← Civ 7 | 1 353 | character 124%, trade gain 108% (29%), opinion 100% |
| 7, 20 (4d) | Civilization 12 | D1 DeclineTreaty ← Civ 7 | 695 | threat 70% (ich wojownicy 81% naszych), character 98%, opinion 100% |
| 7, 43 (4d) | Civilization 12 | D2 DeclareWar → Civ 7 | 2 134 | opinion 153% (sympatia −38), strength 150% (przewaga 293%), character 93% |

Wiersze bez „(4d)”, „(PR B)”, „(PR C)” i „(4F)” pochodzą z gier w wersji 4c; reguły tych decyzji zmieniły się tylko w szczegółach (G2: umowy, wojna,
widzenie obcych obozów i połowa wagi ekspansji dla „zostać”, patrz gubernator). Od PR #38 cywilizacje mają nazwy zamiast numerów.

Jak czytać czynnik: `distance 73% (9)` znaczy „bliskość obniżyła ocenę do 73%, bo miejsce jest 9 pól dalej”. Liczba w nawiasie to
wartość ze świata. Notatki bez procentu (np. `food 59917`, `people 13`) tylko wyjaśniają.
