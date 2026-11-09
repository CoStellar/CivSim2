# Strateg (S1, S2)

[← spis treści](README.md) · kod: `src/CivSim.Core/Ai/StrategistAdvisor.cs` · GDD 8.1, 7.6 · ADR 0017

Strateg nie wydaje rozkazów osadom. Ustawia cywilizacji **wagi 6 celów** i **cel ekspansji** (pole na mapie), a pozostali doradcy
mnożą przez te wagi swoje oceny.

## S1: Ustawienie strategii

**Komenda:** `SetStrategyCommand(wagi[6], pole celu, miesiąc następnego przeglądu, era)`.
Komenda zapisuje w cywilizacji: `GoalWeights`, `ExpansionTarget`, `StrategyReviewMonth`, `StrategyEra`. Odrzucana, gdy wag nie
jest 6, któraś jest spoza 0–3000 albo pole nie należy do Ziemi.

### Kiedy

Dla każdej żywej cywilizacji AI, **we własnym kroku** (ADR 0018), gdy **nadszedł miesiąc przeglądu** albo **cywilizacja jest w innej
erze niż przy ostatnim przeglądzie**. Pierwszy przegląd: pierwszy krok (wszyscy). Następny: za 15 własnych kroków
(`reviewEverySteps`) plus przesunięcie `numer cywilizacji mod 15` (tylko po pierwszym przeglądzie), potem co 15 własnych kroków.
Miesiąc przeglądu = teraz + kroki × rytm jej ery. W epoce kamienia 15 kroków = 750 lat, w neolicie 375 lat, w brązie 300 lat,
niezależnie od tego, w jakiej erze są inni.

**Ustrój zmienia tempo** (etap 4f, GDD 7.2): liczba kroków to `reviewEverySteps × reviewPercent / 100` z `data/governments.json`
(rada starszych 150% = 22 kroki, wodzostwo 75% = 11 kroków).

Strateg działa **po gubernatorze**, więc nowe wagi obowiązują od następnego własnego kroku.

### Dane wejściowe

Liczone tylko z własnych, zamieszkanych osad na Ziemi:

| Wielkość | Wzór |
| --- | --- |
| ludzie | suma mieszkańców wszystkich osad |
| miejsce | suma maksymalnej liczby mieszkańców poziomów osad (obóz 150, wioska 1000, …) |
| **zatłoczenie** (‰) | ludzie × 1000 / miejsce |
| **najedzenie** (‰) | średnia ważona liczbą mieszkańców z `FedPerMille` osad (ile potrzeb jedzenia zaspokoiła poprzednia tura) |
| „serce” | osada z największą liczbą mieszkańców (przy remisie pierwsza na liście) |

Gdy cywilizacja nie ma ludzi, przegląd jest pomijany.

### Wagi celów

| Cel | Waga | Wzór |
| --- | --- | --- |
| ekspansja | 1000–1600 | prosta: zatłoczenie ≤ 50% → 1000; ≥ 90% → 1600; pomiędzy liniowo (`crowdingLowPercent`, `crowdingHighPercent`, `expansionWeightMin/Max`) |
| bezpieczeństwo | 1000–1500 | prosta: najedzenie ≥ 95% → 1000; ≤ 60% → 1500 (`fedWellPercent`, `fedBadlyPercent`, `hungerWeightMax`) |
| dobrobyt | 1000–1500 | ta sama liczba co bezpieczeństwo |
| nauka | 1000–1500 | prosta: spotkani nie znają więcej technologii → 1000; najlepszy ze spotkanych zna o 4 więcej → 1500 (`scienceBehindTechs`, `scienceWeightMax`); od PR B |
| wpływ | 1000–1300 | prosta: 0 spotkanych cywilizacji → 1000; 4 i więcej → 1300 (`influenceMetCivs`, `influenceWeightMax`); od PR B |
| podbój | 1000 | stała (dostanie własną wagę z wojnami w 4e; już dziś działa przez agresję i technologie wojskowe) |

Przykłady: obozy pełne w 70% → ekspansja 1300. Ludzie najedzeni w 80% → bezpieczeństwo i dobrobyt 1215. Sąsiad zna 2
technologie więcej → nauka 1250. Spotkane 2 cywilizacje → wpływ 1150. Liczą się tylko żywe cywilizacje, które już spotkała (AI nie wie
nic o pozostałych).

Charakter **nie** wpływa na wagi stratega. Wpływa osobno, jako mnożnik w ocenie każdego działania.

**Ustrój przechyla wagi** (etap 4f): po policzeniu każda waga jest mnożona przez `goalPercent / 100` ustroju
(`Governments.Weight`). Wodzostwo: ekspansja i podbój ×1,2; rada starszych: bezpieczeństwo ×1,1; despotia: podbój ×1,2 i
bezpieczeństwo ×1,1; republika miejska: dobrobyt i nauka ×1,2, podbój ×0,9. Przykład: wodzostwo z obozami pełnymi w 70% ma
ekspansję 1300 × 1,2 = 1560, a podbój 1200 zamiast 1000.

**Państwo przechyla wagi** (etap 4f PR 5, GDD 7.3): po ustroju strateg patrzy na stabilność i administrację.

- **Przeciążone państwo rozrasta się wolniej:** gdy zużyta administracja przekracza pojemność ustroju, waga ekspansji jest mnożona
  przez `max(overloadExpansionMinPercent, pojemność × 100 / zużycie) / 100`. Przykład: wodzostwo (pojemność 200) zużywa 300 →
  ekspansja × 67%; zużywa 600 → × 50% (podłoga 50%).
- **Niepokoje każą dbać o bezpieczeństwo:** gdy stabilność jest poniżej 30 (`unrestBelow` w `data/stability.json`), waga
  bezpieczeństwa × `unrestSecurityPercent` / 100 = × 1,5 (najwyżej 3000).

W zapisie „Dlaczego?” dochodzą wtedy notatki *administration* (zużycie w % pojemności) i *stability*.

### Cel ekspansji

Najlepsze **wolne** (niczyje) pole lądu, które cywilizacja **zna** (pamięć mapy), w promieniu wędrówki (40 pól, `journeyCells`)
od serca. Sprawdzane jest tylko co trzecie pole w każdym kierunku (kolumna i rząd podzielne przez 3, `farSearchEveryCells`), żeby
liczyć szybko.

```
wartość pola = wartość miejsca do życia × bliskość
wartość miejsca do życia = jedzenie okolicy na rok × wygoda   (dokładnie jak u gubernatora, bez wag i kierunku)
bliskość = 1000 → 0 liniowo do 2 promieni wędrówki (targetZeroAtJourneys), czyli 0,5 na granicy 40 pól
```

Wygrywa najwyższa wartość (przy remisie pierwsze sprawdzone). Bez losowania. Jeśli nie ma żadnego pola z wartością > 0, cel to
„brak” (−1). Własna ziemia cywilizacji **nie** może być celem (tylko niczyja).

Cel działa w G2, G3 i G5 jako **premia +15%** dla miejsc bliżej celu niż obecny obóz (`directionBonusPercent`).

### Zapis „Dlaczego?”

Ocena = waga ekspansji. Notatki: zatłoczenie w %, najedzenie w ‰, wartość pola celu. Przykład z gry (seed 7, tura 1):
`Civilization 1, Strategist, SetStrategy, cel 7119, ocena 1000: crowding 33%, fed 1000, food 59917`.

### Kto czyta wynik (S1)

| Wynik | Czyta | Skutek |
| --- | --- | --- |
| waga ekspansji | G2, G3, G5, U1 | mnożnik ocen miejsc; „zostań” dostaje tylko połowę jej odchylenia od 1000 (od PR B), więc wysoka waga zachęca do przeprowadzki i wędrówki; **obniża próg odłamu** (G5); technologie udźwigu |
| waga podboju | G2, D2 | mnożnik oceny wypędzenia i wojny (dziś zawsze 1000) |
| waga dobrobytu | T1, U1, D1, D2 | wyroby w recepturach (konkurują z jedzeniem), technologie dające wyroby, umowa handlowa |
| waga nauki | U1 | część „podstawa” i dziedzina nauki w każdej technologii (konkuruje z innymi częściami) |
| waga bezpieczeństwa | T1, U1, D1, D2 | jedzenie w recepturach, technologie jedzenia, ognia i wiosek; pakt, sojusz i pokój |
| waga wpływu | U1 | technologie społeczne (dziedzina „społeczeństwo”) i nastrój |
| cel ekspansji | G2, G3, G5 | +15% dla miejsc w jego stronę |

```mermaid
flowchart LR
  O["Osady cywilizacji"] --> Z["zatłoczenie"] --> WE["waga ekspansji 1,0–1,6"]
  O --> N["najedzenie"] --> WG["waga bezpieczeństwa i dobrobytu 1,0–1,5"]
  O --> H["serce = największa osada"] --> C["cel ekspansji<br/>najlepsza znana niczyja ziemia ≤ 40 pól"]
  M["pamięć mapy"] --> C
  G["wartości miejsc od gubernatora"] --> C
  WE --> G2["G2, G3, G5"]
  C --> G2
  WG --> T1["T1: jedzenie kontra wyroby"]
  K["spotkani: ich technologie i liczba"] --> WN["waga nauki 1,0–1,5<br/>waga wpływu 1,0–1,3"] --> U1["U1"]
  WG --> DP["D1, D2: handel, pakt, sojusz, pokój"]
```

## S2: Wybór ustroju

**Komenda:** `ChangeGovernmentCommand(ustrój)` (ta sama, którą wyda gracz). Kod: `StrategistAdvisor.ChooseGovernment`.
Od etapu 4f PR 5 (GDD 7.2: „AI wybiera ustrój według cech”).

### Kiedy

W każdym przeglądzie stratega (S1), zaraz po ustawieniu wag. Rzadko: wodzostwo co 11 kroków, rada co 22.

### Wzór

```
dopasowanie(ustrój) = Σ cecha × skłonność ustroju do cechy      (charakter z cechami przywódcy, skłonności z governments.json)
                    + w niepokojach: premia stabilności ustroju × governmentStabilityWeight (40)
```

Strateg bierze najlepiej dopasowany ustrój dostępny w swojej erze. Zmienia go tylko, gdy:

1. jest inny niż obecny;
2. pasuje **wyraźnie lepiej**: `najlepszy × 100 > obecny × (100 + governmentChangeMarginPercent)`, czyli o ponad 30% (histereza,
   żeby lud nie skakał między ustrojami co przegląd; zmiana kosztuje 2 lata anarchii);
3. zmiana jest dozwolona (`Governments.CannotChange`: nie w anarchii, nie w trakcie innej zmiany, ustrój dostępny w erze);
4. od ostatniej zmiany ustroju (także po przewrocie) minęło co najmniej `governmentKeepYears` (200 lat): reformy potrzebują czasu.
   Bez tego w pierwszej próbie lud w niepokojach brał radę (premia stabilności), a gdy wracał spokój, wracał do wodzostwa
   (pasuje do cech) i znów wpadał w niepokoje: w seedzie 7 jeden lud zmienił ustrój 10 razy.

Przykład: ostrożny (900) i otwarty (900) lud pod wodzem: rada 1800 wobec wodzostwa 0 → zmiana. Lud ostrożny 150, otwarty 150,
agresywny 140, ekspansywny 140: rada 300 wobec 280 to za mało (potrzeba 364). Ten sam lud w niepokojach: rada 300 + 10 × 40 = 700
wobec 280 → zmiana, bo rada daje +10 stabilności.

Nowy przywódca przesuwa charakter (cechy przywódcy), więc **zmiana władcy może po latach przynieść zmianę ustroju**.

### Zapis „Dlaczego?”

Ocena = dopasowanie najlepszego. Notatki: *suitability* (najlepszy wobec obecnego, %), *stability*.

