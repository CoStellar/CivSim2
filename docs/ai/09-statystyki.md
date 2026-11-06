# Statystyki: co AI robi w prawdziwych grach

[← spis treści](README.md)

Trzy gry bez grafiki: mapa standard (256×160), 12 cywilizacji, 1000 kroków świata (ok. 27–29 tys. lat), seedy 7, 23 i 42,
gałąź `feat/ai-land` (PR C: ziemia i migracje). W nawiasach liczby sprzed PR C (`development`, commit `3233763`, po PR B).
Polecenie:

```
dotnet run --project src/CivSim.Headless -c Release -- --seed 7 --turns 1000 --map-size standard --decisions why7.csv --stats stats7.csv
```

Wykres: `python3 docs/ai/tools/decisions_chart.py <katalog z why7.csv, why23.csv, why42.csv>` (potrzebuje matplotlib).

![Ile decyzji podjęło AI](img/decyzje_1000_tur.png)

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

## Liczba decyzji w 1000 tur (PR C; w nawiasach przed PR C)

| Decyzja | Id | Seed 7 | Seed 23 | Seed 42 |
| --- | --- | ---: | ---: | ---: |
| strateg ustawia strategię | S1 | 550 (585) | 497 (609) | 484 (481) |
| uczony wybiera badanie | U1 | 103 (112) | 113 (132) | 101 (92) |
| skarbnik zmienia recepturę | T1 | 4992 (1910) | 2702 (1354) | 2966 (1396) |
| obóz przenosi się | G2 | 2244 (1099) | 1516 (797) | 2616 (1794) |
| zajęcie pustej osady (przeniesienie albo odłam) | G2, G5 | 304 (140) | 281 (139) | 276 (320) |
| wypędzenie obcego obozu | G2 | 95 (59) | 5 (22) | 24 (16) |
| odłam zakłada nowy obóz | G5 | 781 (697) | 773 (837) | 677 (864) |
| mały obóz dołącza do innego | G1 | 778 (545) | 727 (609) | 761 (731) |
| obóz osiada (wioska) | G4 | 428 (164) | 541 (252) | 500 (161) |
| wioska wysyła osadników | G7 | 424 (–) | 473 (–) | 469 (–) |
| wyprawa w nieznane | G8 | 8 (–) | 12 (–) | 97 (–) |
| daleka wędrówka | G3 | 32 (2) | 38 (1) | 23 (11) |
| zabranie porzuconych dóbr | G6 | 1 (1) | 1 (1) | 1 (10) |
| dyplomata proponuje umowę albo pokój | D2 | 184 (147) | 98 (65) | 613 (269) |
| dyplomata przyjmuje propozycję | D1 | 44 (25) | 23 (16) | 30 (30) |
| dyplomata odrzuca propozycję | D1 | 114 (106) | 63 (38) | 565 (223) |
| dyplomata wypowiada wojnę | D2 | 10 (8) | 0 (2) | 1 (2) |
| **razem** | | **11092** (5600) | **7863** (4874) | **10204** (6400) |

Tabela decyzji po PR B (z liczbami sprzed PR B) jest w historii tej strony w gicie (commit `3233763`).

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

Wiersze bez „(4d)”, „(PR B)” i „(PR C)” pochodzą z gier w wersji 4c; reguły tych decyzji zmieniły się tylko w szczegółach (G2: umowy, wojna,
widzenie obcych obozów i połowa wagi ekspansji dla „zostać”, patrz gubernator). Od PR #38 cywilizacje mają nazwy zamiast numerów.

Jak czytać czynnik: `distance 73% (9)` znaczy „bliskość obniżyła ocenę do 73%, bo miejsce jest 9 pól dalej”. Liczba w nawiasie to
wartość ze świata. Notatki bez procentu (np. `food 59917`, `people 13`) tylko wyjaśniają.
