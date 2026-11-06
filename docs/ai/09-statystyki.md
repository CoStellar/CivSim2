# Statystyki: co AI robi w prawdziwych grach

[← spis treści](README.md)

Trzy gry bez grafiki: mapa standard (256×160), 12 cywilizacji, 1000 tur, seedy 7, 23 i 42, commit `8cc73e1` (etap 4d). Polecenie:

```
dotnet run --project src/CivSim.Headless -c Release -- --seed 7 --turns 1000 --map-size standard --decisions why7.csv
```

Wykres: `python3 docs/ai/tools/decisions_chart.py <katalog z why7.csv, why23.csv, why42.csv>` (potrzebuje matplotlib).

![Ile decyzji podjęło AI](img/decyzje_1000_tur.png)

## Liczba decyzji w 1000 tur

| Decyzja | Id | Seed 7 | Seed 23 | Seed 42 |
| --- | --- | ---: | ---: | ---: |
| strateg ustawia strategię | S1 | 583 | 608 | 678 |
| uczony wybiera badanie | U1 | 79 | 113 | 106 |
| skarbnik zmienia recepturę | T1 | 1539 | 1113 | 1527 |
| obóz przenosi się | G2 | 1800 | 1283 | 2507 |
| zajęcie pustej osady (przeniesienie albo odłam) | G2, G5 | 277 | 295 | 438 |
| wypędzenie obcego obozu | G2 | 137 | 132 | 36 |
| odłam zakłada nowy obóz | G5 | 1156 | 1291 | 1433 |
| mały obóz dołącza do innego | G1 | 1018 | 1158 | 1340 |
| obóz osiada (wioska) | G4 | 148 | 172 | 143 |
| daleka wędrówka | G3 | 3 | 1 | 6 |
| zabranie porzuconych dóbr | G6 | 0 | 0 | 6 |
| dyplomata proponuje umowę albo pokój | D2 | 230 | 112 | 666 |
| dyplomata przyjmuje propozycję | D1 | 39 | 21 | 32 |
| dyplomata odrzuca propozycję | D1 | 158 | 81 | 619 |
| dyplomata wypowiada wojnę | D2 | 19 | 5 | 1 |
| **razem** | | **7186** | **6385** | **9538** |

Co z tego widać:

- Większość decyzji zapada w pierwszych 200–300 turach (epoka kamienia i początek neolitu), kiedy osady są obozami. Potem prawie wszystko
  to wioski, a wioski nie mają decyzji gubernatora ([uwagi, pkt 8](08-ocena-i-uwagi.md#8-wioski-bez-ai)).
- Odłamów i dołączeń jest podobnie dużo ([uwagi, pkt 9](08-ocena-i-uwagi.md#9-inne-drobne-rzeczy)).
- Wędrówki i łupy prawie się nie zdarzają.
- Dyplomacja: na 12 cywilizacji 1–19 wojen w 1000 tur; większość propozycji jest odrzucana ([uwagi, pkt 9](08-ocena-i-uwagi.md#9-inne-drobne-rzeczy)).
- Liczby różnią się od wersji 4c, bo dyplomacja zmienia przebieg gry (pakty blokują wypędzenia, wojny je zachęcają).

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
| 7, 12 (4d) | Civilization 8 | D2 ProposeTreaty → Civ 11 | 1 042 | character 91%, opinion 108% (sympatia +4), trade gain 105% (28%) |
| 7, 13 (4d) | Civilization 12 | D1 AcceptTreaty ← Civ 7 | 1 353 | character 124%, trade gain 108% (29%), opinion 100% |
| 7, 20 (4d) | Civilization 12 | D1 DeclineTreaty ← Civ 7 | 695 | threat 70% (ich wojownicy 81% naszych), character 98%, opinion 100% |
| 7, 43 (4d) | Civilization 12 | D2 DeclareWar → Civ 7 | 2 134 | opinion 153% (sympatia −38), strength 150% (przewaga 293%), character 93% |

Wiersze bez „(4d)” pochodzą z gier w wersji 4c; reguły tych decyzji się nie zmieniły (poza G2: umowy i wojna, patrz gubernator).

Jak czytać czynnik: `distance 73% (9)` znaczy „bliskość obniżyła ocenę do 73%, bo miejsce jest 9 pól dalej”. Liczba w nawiasie to
wartość ze świata. Notatki bez procentu (np. `food 59917`, `people 13`) tylko wyjaśniają.
