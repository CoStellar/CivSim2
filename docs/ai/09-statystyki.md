# Statystyki: co AI robi w prawdziwych grach

[← spis treści](README.md)

Trzy gry bez grafiki: mapa standard (256×160), 12 cywilizacji, 1000 tur, seedy 7, 23 i 42, commit `f2232a9`. Polecenie:

```
dotnet run --project src/CivSim.Headless -c Release -- --seed 7 --turns 1000 --map-size standard --decisions why7.csv
```

Wykres: `python3 docs/ai/tools/decisions_chart.py <katalog z why7.csv, why23.csv, why42.csv>` (potrzebuje matplotlib).

![Ile decyzji podjęło AI](img/decyzje_1000_tur.png)

## Liczba decyzji w 1000 tur

| Decyzja | Id | Seed 7 | Seed 23 | Seed 42 |
| --- | --- | ---: | ---: | ---: |
| strateg ustawia strategię | S1 | 648 | 746 | 693 |
| uczony wybiera badanie | U1 | 83 | 127 | 99 |
| skarbnik zmienia recepturę | T1 | 1928 | 2504 | 2901 |
| obóz przenosi się | G2 | 1931 | 1674 | 2519 |
| zajęcie pustej osady (przeniesienie albo odłam) | G2, G5 | 370 | 369 | 426 |
| wypędzenie obcego obozu | G2 | 148 | 224 | 88 |
| odłam zakłada nowy obóz | G5 | 1422 | 1587 | 1451 |
| mały obóz dołącza do innego | G1 | 1257 | 1440 | 1330 |
| obóz osiada (wioska) | G4 | 189 | 205 | 145 |
| daleka wędrówka | G3 | 0 | 7 | 7 |
| zabranie porzuconych dóbr | G6 | 1 | 5 | 8 |
| **razem** | | **7977** | **8888** | **9667** |

Co z tego widać:

- Większość decyzji zapada w pierwszych 300 turach (epoka kamienia i początek neolitu), kiedy osady są obozami. Potem prawie wszystko
  to wioski, a wioski nie mają decyzji gubernatora ([uwagi, pkt 8](08-ocena-i-uwagi.md#8-wioski-bez-ai)).
- Odłamów i dołączeń jest podobnie dużo ([uwagi, pkt 9](08-ocena-i-uwagi.md#9-inne-drobne-rzeczy)).
- Wędrówki i łupy prawie się nie zdarzają.

## Przykładowe wpisy „Dlaczego?” (jeden na rodzaj decyzji)

| Seed, tura | Cywilizacja | Decyzja | Ocena | 3 najważniejsze czynniki |
| --- | --- | --- | ---: | --- |
| 7, 1 | Civilization 1 | S1 SetStrategy | 1000 | crowding 33%, fed 1000, food 59917 |
| 7, 1 | Civilization 1 | U1 SetResearch (ogień) | 1 052 000 000 | character 105%, research cost 100% (koszt 4200) |
| 23, 3 | Civilization 4 | G2 MoveCamp | 46 209 | distance 73% (9 pól), direction 115% (2 pola bliżej celu), character 89% |
| 7, 25 | Civilization 8 | G2 Conquer | 19 983 | character 112% (agresja 620), distance 91% (3 pola), opinion 98% (sympatia +4) |
| 7, 26 | Civilization 10 | G2 Occupy | 23 112 | distance 79% (7 pól), direction 115%, own old campsite 110% |
| 23, 24 | Civilization 1 | G3 Journey | 37 161 | goal weight 137% (ekspansja), character 81%, distance 83% (17 pól) |
| 7, 168 | Civilization 1 | G4 Settle | 126 000 | farm yield 1563%, people 78 |
| 7, 5 | Civilization 1 | G5 SplitCamp | 78 669 | character 120%, direction 115%, distance 88% (4 pola) |
| 7, 21 | Civilization 10 | G1 JoinCamp | 13 | people 13 |
| 7, 419 | Civilization 4 | G6 Plunder | 22 081 | goods left 22081 |
| 7, 27 | Civilization 4 | T1 SetRecipe (narzędzia kamienne) | 274 487 | character 87%, value added 313700, inputs available 100% |

Jak czytać czynnik: `distance 73% (9)` znaczy „bliskość obniżyła ocenę do 73%, bo miejsce jest 9 pól dalej”. Liczba w nawiasie to
wartość ze świata. Notatki bez procentu (np. `food 59917`, `people 13`) tylko wyjaśniają.
