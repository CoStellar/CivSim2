# Statystyki: co AI robi w prawdziwych grach

[← spis treści](README.md)

Trzy gry bez grafiki: mapa standard (256×160), 12 cywilizacji, 1000 kroków świata (ok. 27–29 tys. lat), seedy 7, 23 i 42,
gałąź `feat/ai-values` (PR B: wartości i priorytety). W nawiasach liczby sprzed PR B (`development`, commit `fbcc0d9`). Polecenie:

```
dotnet run --project src/CivSim.Headless -c Release -- --seed 7 --turns 1000 --map-size standard --decisions why7.csv --stats stats7.csv
```

Wykres: `python3 docs/ai/tools/decisions_chart.py <katalog z why7.csv, why23.csv, why42.csv>` (potrzebuje matplotlib).

![Ile decyzji podjęło AI](img/decyzje_1000_tur.png)

Przed PR B i po nim: ludność, technologie i zmiany receptur (`python3 docs/ai/tools/values_chart.py <przed> <po>`, katalogi ze
`stats*.csv` i `why*.csv`):

![Wartości AI przed i po](img/wartosci_przed_po.png)

Rytmy cywilizacji i ludność przed zegarem i po nim (ADR 0018, `python3 docs/ai/tools/clock_chart.py <przed> <po>`):

![Rytmy cywilizacji i ludność przed/po](img/zegar_rytmy.png)

## Najważniejsze liczby przed PR B i po nim

| Seed | Pierwszy neolit (krok, rok) | Ludzie na końcu | Żywe cywilizacje | Technologie (średnio) | Wioski |
| --- | --- | ---: | ---: | ---: | ---: |
| 7 | 120, rok 5850 (160, rok 7800) | 113 050 (106 452) | 10 (8) | 10,4 (7,8) | 80 (79) |
| 23 | 100, rok 4800 (140, rok 6875) | 163 527 (117 698) | 9 (7) | 14,0 (13,0) | 129 (81) |
| 42 | 90, rok 4425 (150, rok 7350) | 117 473 (103 500) | 8 (7) | 9,5 (10,6) | 91 (66) |

## Liczba decyzji w 1000 tur

| Decyzja | Id | Seed 7 | Seed 23 | Seed 42 |
| --- | --- | ---: | ---: | ---: |
| strateg ustawia strategię | S1 | 585 (436) | 609 (488) | 481 (479) |
| uczony wybiera badanie | U1 | 112 (84) | 132 (116) | 92 (107) |
| skarbnik zmienia recepturę | T1 | 1910 (2514) | 1354 (1926) | 1396 (2737) |
| obóz przenosi się | G2 | 1099 (1639) | 797 (1426) | 1794 (2413) |
| zajęcie pustej osady (przeniesienie albo odłam) | G2, G5 | 140 (248) | 139 (266) | 320 (379) |
| wypędzenie obcego obozu | G2 | 59 (138) | 22 (118) | 16 (39) |
| odłam zakłada nowy obóz | G5 | 697 (1142) | 837 (1368) | 864 (1438) |
| mały obóz dołącza do innego | G1 | 545 (978) | 609 (1225) | 731 (1345) |
| obóz osiada (wioska) | G4 | 164 (163) | 252 (174) | 161 (134) |
| daleka wędrówka | G3 | 2 (3) | 1 (2) | 11 (8) |
| zabranie porzuconych dóbr | G6 | 1 (0) | 1 (2) | 10 (1) |
| dyplomata proponuje umowę albo pokój | D2 | 147 (273) | 65 (110) | 269 (696) |
| dyplomata przyjmuje propozycję | D1 | 25 (40) | 16 (19) | 30 (37) |
| dyplomata odrzuca propozycję | D1 | 106 (199) | 38 (78) | 223 (641) |
| dyplomata wypowiada wojnę | D2 | 8 (19) | 2 (3) | 2 (1) |
| **razem** | | **5600** (7876) | **4874** (7321) | **6400** (10455) |

Co z tego widać:

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
| 7, 5 | Civilization 1 | G5 SplitCamp | 78 669 | character 120%, direction 115%, distance 88% (4 pola) |
| 7, 21 | Civilization 10 | G1 JoinCamp | 13 | people 13 |
| 7, 419 | Civilization 4 | G6 Plunder | 22 081 | goods left 22081 |
| 7, 27 (PR B) | Lakhaem | T1 SetRecipe (narzędzia kamienne) | 274 488 | value added 313700, serves prosperity, inputs available 100% |
| 7, 55 (PR B) | Rishboy | T1 SetRecipe (narzędzia kamienne) | 527 438 | value added 313700, serves prosperity, for trade 363 |
| 7, 12 (4d) | Civilization 8 | D2 ProposeTreaty → Civ 11 | 1 042 | character 91%, opinion 108% (sympatia +4), trade gain 105% (28%) |
| 7, 13 (4d) | Civilization 12 | D1 AcceptTreaty ← Civ 7 | 1 353 | character 124%, trade gain 108% (29%), opinion 100% |
| 7, 20 (4d) | Civilization 12 | D1 DeclineTreaty ← Civ 7 | 695 | threat 70% (ich wojownicy 81% naszych), character 98%, opinion 100% |
| 7, 43 (4d) | Civilization 12 | D2 DeclareWar → Civ 7 | 2 134 | opinion 153% (sympatia −38), strength 150% (przewaga 293%), character 93% |

Wiersze bez „(4d)” i „(PR B)” pochodzą z gier w wersji 4c; reguły tych decyzji zmieniły się tylko w szczegółach (G2: umowy, wojna,
widzenie obcych obozów i połowa wagi ekspansji dla „zostać”, patrz gubernator). Od PR #38 cywilizacje mają nazwy zamiast numerów.

Jak czytać czynnik: `distance 73% (9)` znaczy „bliskość obniżyła ocenę do 73%, bo miejsce jest 9 pól dalej”. Liczba w nawiasie to
wartość ze świata. Notatki bez procentu (np. `food 59917`, `people 13`) tylko wyjaśniają.
