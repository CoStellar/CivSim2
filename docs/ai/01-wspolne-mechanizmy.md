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
   - z szansą równą **ostrożności** cywilizacji (cecha 0–1000, czyli 0–100%) bierze **najlepsze**; od etapu 4f ostrożność
     przesuwa ustrój (`cautionShift` w `data/governments.json`: rada starszych +200‰, despotia −200‰, wynik w 0–1000,
     `Governments.Caution`);
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
ludu się dziś **nie zmieniają** w trakcie gry (zmiana z doświadczeniem: później).

**Przywódca** (etap 4f PR 2, GDD 7.5 i 8.4): każda cecha przywódcy przesuwa 1–2 cechy charakteru o 0,2 na czas jego rządów
(`data/leaders.json`). AI decyduje z cechą = cecha ludu + przesunięcia przywódcy, w granicach 0–1000 (`CivilizationState.Trait`).
Przykład: lud o agresji 0,5 z wodzem-zdobywcą ma agresję 0,7, więc mnożnik podboju 1,2 zamiast 1,0. Nowy przywódca zmienia politykę
od razu. Ustrój na starcie i dobór przywódcy przez radę starszych patrzą na cechy samego ludu (`OwnTrait`). Opis: `docs/systems/leaders.md`.

| Cecha | Do czego jest używana dziś | Gdzie |
| --- | --- | --- |
| agresja | mnożnik celu „podbój”: wypędzenie obcego obozu, wypowiedzenie wojny | G2, D2 |
| ekspansywność | mnożnik celu „ekspansja”: przeniesienie, wędrówka, odłam (od PR B „zostać” ma tylko połowę); **próg odłamu**; technologie udźwigu | G2, G3, G5, U1 |
| handlowość | mnożnik celu „dobrobyt”: wyroby w recepturach i technologiach, umowa handlowa; premia dla wyrobów na wymianę | T1, U1, D1, D2 |
| ciekawość świata | mnożnik celu „nauka” w technologiach; zmniejsza strach przed kosztem technologii (od PR B) | U1 |
| otwartość | pierwsze wrażenie po kontakcie (−20 … +20 sympatii); mnożnik celu „wpływ” (technologie społeczne, nastrój) | R2 → G2, U1 |
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
| receptura na wyroby, umowa handlowa | dobrobyt (`goalOfGoods`) |
| receptura na jedzenie (chleb) | bezpieczeństwo (`goalOfFood`) |
| pakt o nieagresji, sojusz obronny, pokój | bezpieczeństwo |
| technologia | każda część osobno: podstawa i dziedzina nauki → nauka, społeczna → wpływ, wojskowa → podbój, jedzenie i wioski → bezpieczeństwo, wyroby → dobrobyt, udźwig → ekspansja ([uczony](05-uczony.md)) |

**Ważne:** waga celu i mnożnik charakteru zmieniają wynik **tylko wtedy, gdy porównywane działania służą różnym celom** (albo gdy ocena
jest porównywana ze stałym progiem). Jeśli wszystkie opcje mnożymy przez tę samą liczbę, kolejność i szanse w losowaniu się nie
zmieniają. Dlatego od PR B każda opcja mówi, któremu celowi służy, a test `DeadValueTests` pilnuje, żeby każda waga i cecha coś
zmieniała. Szczegóły w [uwagach](08-ocena-i-uwagi.md#1-wagi-i-cechy-które-nic-nie-zmieniają-zamknięte-w-pr-b).

## 6. Histereza (żeby AI się nie miotało)

GDD 8.6. Obecny stan ma przewagę:

| Decyzja | Nowe musi być lepsze o | Parametr |
| --- | --- | --- |
| G2 przeniesienie obozu | 30% od obecnego miejsca | `migration.moveWhenBetterByPercent` |
| G3 daleka wędrówka | 60% od lepszego z (obecne miejsce, najlepsze w zasięgu) | 2 × powyższe, zaszyte w kodzie |
| T1 zmiana receptury | 25% od obecnej | `recipeSwitchBetterByPercent` |

## 7. Rytm cywilizacji: decyzje proaktywne i reaktywne (ADR 0018)

Kod: `src/CivSim.Core/Simulation/WorldClock.cs`.

Czas gry to licznik. Każda cywilizacja ma **własny krok** o długości tury swojej ery (epoka kamienia 50 lat, neolit 25, brąz 20, …),
przypięty do kalendarza (rytm 25 lat: lata 0, 25, 50…). Pętla przeskakuje do najbliższego roku, w którym ktoś ma krok
(„krok świata”). Rada liczy decyzje cywilizacji według tabeli:

| Rodzaj | Decyzje | Kiedy |
| --- | --- | --- |
| proaktywne, szybkie | G2 (przeniesienie głodnego albo zmarzniętego obozu) | w każdym własnym kroku; obóz, który nie głoduje mocno (najedzony ≥ 85%), szuka co 3 własne kroki, gdy `(liczba własnych kroków − 1 + numer obozu) mod 3 = 0` (`searchEverySteps`); na planecie z porami roku na żywo w każdym |
| proaktywne, wolne | G1, G3, G4, G5, G6, T1 | we własnym kroku, który zaczyna okres długości kroku ery (`WorldClock.PlansNow`); na Ziemi to każdy własny krok, poza krótszym krokiem zaraz po zmianie ery |
| proaktywne, strateg | S1 | co 15 własnych kroków (`reviewEverySteps`), pierwszy przegląd w pierwszym kroku, potem przesunięcie `numer cywilizacji mod 15`; od razu w nowej erze |
| proaktywne, dyplomata | D2 | raz na 50 lat (`reviewEveryYears`), we własnym kroku, który zawiera rok przeglądu ([dyplomata](10-dyplomata.md)) |
| reaktywne | D1 (odpowiedź na propozycję), U1 (wybór badania, gdy nic nie jest badane) | w każdym kroku świata, także poza rytmem |
| reaktywne, alarm „wypowiedziano nam wojnę” | D2 | w najbliższym kroku świata |
| reaktywne, alarm „napadnięto nasz obóz” | D2 i G2 wszystkich obozów: szukają bezpieczniejszego miejsca od razu, nawet bez głodu i tuż po przeprowadzce (próg 30% zostaje) | w najbliższym kroku świata |

- Obóz po przeprowadzce odpoczywa **2 własne kroki** (`minStepsBetweenMoves`), zanim znów rozważy G2/G3 (chyba że reaguje na napad).
- Alarmy (`CivilizationState.Alarms`) ustawiają: wypowiedzenie wojny (`Treaties.DeclareWar`) i napad na obóz (`Contacts.RecordAttack`).
  Rada kasuje je po fazie decyzji, więc każdy alarm daje jedną reakcję.
- Cywilizacja, która czeka na swój krok, nie zbiera, nie je, nie rośnie, nie bada i nie widzi nic nowego. W swoim kroku liczy to
  wszystko za cały krok naraz.
- Gdy wszyscy są w tej samej erze, wszystko dzieje się jak przed ADR 0018 (sprawdzone na 3 seedach: statystyki identyczne do
  pierwszego wejścia w neolit).

## 8. Kolejność i rozstrzyganie

Kod: `src/CivSim.Core/Ai/Advisor.cs:136` (`AiCouncil`), `src/CivSim.Core/Commands/CommandQueue.cs`.

- Doradcy działają po kolei: **gubernator → strateg → skarbnik → uczony → dyplomata**. Strateg czyta wartości miejsc policzone przez gubernatora
  w tym samym kroku; jego nowa strategia działa od **następnego** własnego kroku.
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
