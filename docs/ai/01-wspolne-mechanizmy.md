# Wspólne mechanizmy: jak AI liczy i wybiera

[← spis treści](README.md)

Wszyscy doradcy (strateg, gubernator, skarbnik, uczony) używają tych samych narzędzi. Ta strona opisuje je raz, dokładnie.

## 1. Ocena działania (`Evaluation`)

Kod: `src/CivSim.Core/Ai/Utility/Evaluation.cs`. Opis w GDD 8.3, decyzja techniczna w ADR 0016.

Każde możliwe działanie (np. „przenieś obóz na pole X”) dostaje **ocenę**, czyli jedną liczbę całkowitą:

```
ocena = wartość bazowa × czynnik₁ × czynnik₂ × … × waga celu × mnożnik charakteru − koszt
```

- **Wartość bazowa** jest w jednostkach samego działania: dla miejsca to jedzenie na rok w setnych częściach (7552 = 75,52 jedzenia
  na rok), dla receptury wartość dodana, dla technologii stała 1 000 000 000.
- **Czynnik** to liczba w tysięcznych (‰): 1000 = bez zmian, 800 = −20%, 1150 = +15%, **0 = działanie wykluczone**.
  Czynniki mnożymy po kolei z dzieleniem całkowitym: `wynik = wynik × czynnik / 1000`. Bez ułamków, więc wynik jest identyczny na
  każdym komputerze.
- **Waga celu** i **mnożnik charakteru** to też czynniki w tysięcznych (opis niżej). Gdy któryś jest równy 1000, nie jest nawet zapisywany.
- **Koszt** jest odejmowany na końcu. Dziś żaden doradca nie używa kosztu (uczony liczy koszt jako czynnik, nie odejmuje go).
- **Notatka** (`Note`) zapisuje wartość ze świata, która wyjaśnia decyzję, ale nie zmienia oceny (np. „lepsze niż tu 3166”).

Ocena zapamiętuje do 8 czynników (więcej jest liczonych, ale nie zapisywanych).

## 2. Krzywe odpowiedzi (`ResponseCurve`)

Kod: `src/CivSim.Core/Ai/Utility/ResponseCurve.cs`. Zamieniają wartość ze świata (np. odległość) na czynnik w tysięcznych.

| Kształt | Działanie |
| --- | --- |
| prosta (`Linear`) | od punktu (x0, y0) do (x1, y1) linią prostą; poza tym zakresem stała |
| krzywa S (`Smooth`) | jak prosta, ale wolno–szybko–wolno (smoothstep) |
| próg (`Step`) | y0 poniżej progu, y1 od progu |

Wynik nigdy nie spada poniżej 0. **Dziś używane są tylko proste.** Krzywa S i próg istnieją, ale żadna decyzja ich nie używa.

Wszystkie krzywe używane dziś:

| Krzywa | Wejście | Wzór (‰) | Gdzie |
| --- | --- | --- | --- |
| bliskość (jeden ruch) | odległość d w polach | 1000 − 30·d (d=1 → 970, d=10 → 700) | G2, G5 |
| bliskość (wędrówka) | odległość d | 1000 − 10·d (d=11 → 890, d=40 → 600) | G3 |
| bliskość celu ekspansji | odległość d od największej osady | 1000 − 12,5·d (d=40 → 500) | S1 |
| waga ekspansji | zatłoczenie obozów (‰) | 500‰ → 1000, 900‰ → 1600, liniowo | S1 |
| waga głodu | najedzenie (‰) | 600‰ → 1500, 950‰ → 1000, liniowo | S1 |

## 3. Wybór (`Chooser` i `Advisor.Pick`)

Kod: `src/CivSim.Core/Ai/Utility/Chooser.cs`, `src/CivSim.Core/Ai/Advisor.cs:72` (`Pick`).

1. Doradca ocenia kolejne działania w **stałej kolejności** (pola pierścieniami od obozu, receptury i technologie po numerze).
2. Wybór trzyma **3 najlepsze** działania z oceną **większą od zera** (`pickAmongTop: 3` w `data/ai.json`). Przy remisie zostaje
   działanie ocenione wcześniej.
3. Losowanie:
   - z szansą równą **ostrożności** cywilizacji (cecha 0–1000, czyli 0–100%) bierze **najlepsze**;
   - w przeciwnym razie losuje spośród zatrzymanych z wagami proporcjonalnymi do ocen:
     `waga_k = max(1, ocena_k × 1000 / ocena_najlepszego)`.
   - Szansa, że wygra najlepsze: `P = ostrożność + (1 − ostrożność) × waga₁ / Σ wag`.
   - Przykład: oceny 1000, 900, 500, ostrożność 40%. Wagi 1000, 900, 500 (suma 2400). Najlepsze: 0,4 + 0,6 × 1000/2400 = **65%**,
     drugie 0,6 × 900/2400 = **22,5%**, trzecie 0,6 × 500/2400 = **12,5%**.
4. Gdy zatrzymane jest tylko jedno działanie, wygrywa ono bez losowania.

**Skąd losowość.** Generator jest tworzony osobno dla każdej decyzji z: seeda gry, strumienia `ai`, rodzaju doradcy, numeru
osady albo cywilizacji i numeru tury (`Advisor.RandomFor`, `Advisor.cs:91`). Wspólny strumień losowy się nie przesuwa, więc ta
sama gra daje zawsze te same decyzje (determinizm, ADR 0003), a doradców można by kiedyś liczyć równolegle.

**Optymalizacja, która nie zmienia wyniku.** Gubernator najpierw liczy ocenę pola „gdyby nikt tu nic nie zebrał” (górna granica). Jeśli
nawet ona nie weszłaby do 3 najlepszych, pole jest pomijane bez dokładnego liczenia. Wynik jest identyczny jak bez skrótu (ADR 0017).

## 4. Charakter (`Character`, C1)

Kod: `src/CivSim.Core/Ai/Character.cs`. GDD 8.4, ADR 0017.

Każda cywilizacja ma 8 cech w skali 0–1000. Są losowane **raz, przy tworzeniu gry**: każda cecha to średnia z dwóch losowań 0–1000
(więc wartości bliskie 500 są częstsze, skrajne rzadkie). Generator pochodzi ze strumienia `leaders` i numeru cywilizacji. Cechy
się dziś **nie zmieniają** w trakcie gry (zmiana z doświadczeniem i sukcesja przywódców to etap 4f).

| Cecha | Do czego jest używana dziś | Gdzie |
| --- | --- | --- |
| agresja | mnożnik celu „podbój”: wypędzenie obcego obozu, wypowiedzenie wojny | G2, D2 |
| ekspansywność | mnożnik celu „ekspansja”: przeniesienie, wędrówka, odłam; **próg odłamu** | G2, G3, G5 |
| handlowość | mnożnik celu „dobrobyt”: receptury, umowa handlowa | D1, D2 (T1 bez realnego wpływu, patrz [uwagi](08-ocena-i-uwagi.md)) |
| ciekawość świata | mnożnik celu „nauka”: badania | U1 (bez realnego wpływu, patrz uwagi) |
| otwartość | pierwsze wrażenie po kontakcie (−20 … +20 sympatii) | R2 → G2 |
| ostrożność | szansa wzięcia najlepszego działania zamiast losowania; mnożnik celu „bezpieczeństwo”: pakt o nieagresji, pokój | wszystkie wybory, D1, D2 |
| mściwość | jak długo pamięta napaść (150 → 300 lat) | R3 → G2 |
| lojalność | mnożnik sojuszu obronnego; hamuje wojnę łamiącą umowę; szansa dołączenia do wojny sojusznika | D1, D2, D3 |

**Mnożnik charakteru** dla działania służącego celowi = `500 + cecha` ‰, czyli od 0,5 (cecha 0) do 1,5 (cecha 1000). GDD dopuszcza
0,5–2. Przypisanie celów do cech:

| Cel | Cecha |
| --- | --- |
| ekspansja | ekspansywność |
| bezpieczeństwo | ostrożność |
| dobrobyt | handlowość |
| nauka | ciekawość świata |
| wpływ | otwartość |
| podbój | agresja |

## 5. Waga celu

Strateg (S1) ustawia każdej cywilizacji 6 wag celów w tysięcznych (0–3000; dziś w praktyce 1000–1600). Przed pierwszym przeglądem
wszystkie są równe 1000. Doradca mnoży ocenę działania przez wagę celu, któremu ono służy:

| Działanie | Cel |
| --- | --- |
| przeniesienie obozu na wolne miejsce, zajęcie pustej osady, wędrówka, odłam | ekspansja |
| wypędzenie obcego obozu, wypowiedzenie wojny | podbój |
| receptura, umowa handlowa | dobrobyt |
| pakt o nieagresji, sojusz obronny, pokój | bezpieczeństwo |
| technologia | nauka |
| (nic) | wpływ — żadne działanie dziś go nie używa |

**Ważne:** waga celu i mnożnik charakteru zmieniają wynik **tylko wtedy, gdy porównywane działania służą różnym celom** (albo gdy ocena
jest porównywana ze stałym progiem). Jeśli wszystkie opcje mnożymy przez tę samą liczbę, kolejność i szanse w losowaniu się nie
zmieniają. Szczegóły i skutki w [uwagach](08-ocena-i-uwagi.md#1-wagi-i-cechy-które-nic-nie-zmieniają).

## 6. Histereza (żeby AI się nie miotało)

GDD 8.6. Obecny stan ma przewagę:

| Decyzja | Nowe musi być lepsze o | Parametr |
| --- | --- | --- |
| G2 przeniesienie obozu | 30% od obecnego miejsca | `migration.moveWhenBetterByPercent` |
| G3 daleka wędrówka | 60% od lepszego z (obecne miejsce, najlepsze w zasięgu) | 2 × powyższe, zaszyte w kodzie |
| T1 zmiana receptury | 25% od obecnej | `recipeSwitchBetterByPercent` |

## 7. Dwa zegary i rzadsze sprawdzanie

- **Szybki zegar (co turę):** G2 (przeniesienie głodnego obozu). Ale obóz, który nie głoduje mocno (najedzony ≥ 85%), szuka tylko
  co 3 tury, rozłożone między obozy: gdy `(numer tury + numer obozu) mod 3 = 0` (`searchEveryTurns`). Na planecie z porami roku
  na żywo szuka co turę.
- **Wolny zegar (tura decyzyjna):** G1, G3, G4, G5, G6, T1. Na Ziemi każda tura jest decyzyjna.
- **Strateg:** co 15 tur na cywilizację (`reviewEveryTurns`), pierwszy przegląd w turze 1, potem przesunięcie zależne od numeru
  cywilizacji, żeby nie wszyscy liczyli w tej samej turze; od razu po wejściu w nową erę.
- **Uczony:** tylko wtedy, gdy cywilizacja nic nie bada (zaraz po odkryciu technologii).
- Obóz po przeprowadzce odpoczywa **2 tury** (`minTurnsBetweenMoves`), zanim znów rozważy G2/G3.

## 8. Kolejność i rozstrzyganie

Kod: `src/CivSim.Core/Ai/Advisor.cs:136` (`AiCouncil`), `src/CivSim.Core/Commands/CommandQueue.cs`.

- Doradcy działają po kolei: **gubernator → strateg → skarbnik → uczony → dyplomata**. Strateg czyta wartości miejsc policzone przez gubernatora
  w tej samej turze; jego nowa strategia działa od **następnej** tury.
- Gubernator przechodzi cywilizacje po kolei (po numerze), a w cywilizacji obozy po kolei. Wybrane pole jest od razu **rezerwowane na
  tę turę** (`Take`), więc następny obóz go nie wybierze. Wcześniejsze cywilizacje i obozy mają więc pierwszeństwo przy wyborze.
- Komendy rozstrzyga faza 4 w kolejności: kategoria (dyplomacja → gospodarka → ruch), potem cywilizacja (kolejność **przesuwa się co
  turę**, żeby nikt nie był zawsze pierwszy), potem kolejność wydania. Każda komenda jest sprawdzana; jeśli w międzyczasie przestała
  być możliwa (np. ktoś zajął pole), jest odrzucana i przepada. AI spróbuje ponownie przy następnej okazji.

## 9. Dziennik „Dlaczego?” (GDD 8.7)

Kod: `src/CivSim.Core/Ai/Utility/Decision.cs`. Każda wydana komenda zapisuje: turę, cywilizację, doradcę, działanie, osadę, cel
(pole, receptura albo technologia), ocenę i **3 czynniki, które najbardziej ją zmieniły**: najpierw czynniki najdalsze od 1000, potem
notatki, na końcu czynniki równe 1000.

- Zapis nie wchodzi do stanu gry ani do zapisu (tylko wyjaśnia, nie steruje).
- Headless zapisuje go opcją `--decisions why.csv`.
- Przykład: `Civilization 8, Governor, Conquer, ocena 19983: character 112% (0), distance 91% (3), opinion 98% (4)` — wypędzenie
  obcego obozu 3 pola dalej; agresja tej cywilizacji to 620 (mnożnik 1,12), sympatia do ofiary +4.

Uwaga: dziennik pokazuje czynnik „character” także tam, gdzie nie wpłynął na wybór (bo wszystkie opcje miały ten sam). Patrz
[uwagi](08-ocena-i-uwagi.md#7-dziennik-dlaczego-czasem-wprowadza-w-błąd).
