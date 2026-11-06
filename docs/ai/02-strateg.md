# Strateg (S1)

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
| nauka | 1000 | stała |
| wpływ | 1000 | stała |
| podbój | 1000 | stała (dostanie własną wagę z wojnami w 4e) |

Przykłady: obozy pełne w 70% → ekspansja 1300. Ludzie najedzeni w 80% → bezpieczeństwo i dobrobyt 1215.

Charakter **nie** wpływa na wagi stratega. Wpływa osobno, jako mnożnik w ocenie każdego działania.

### Cel ekspansji

Najlepsze **wolne** (niczyje) pole lądu, które cywilizacja **zna** (pamięć mapy), w promieniu wędrówki (40 pól, `journeyCells`)
od serca. Sprawdzane jest tylko co trzecie pole w każdym kierunku (kolumna i rząd podzielne przez 3), żeby liczyć szybko.

```
wartość pola = wartość miejsca do życia × bliskość
wartość miejsca do życia = jedzenie okolicy na rok × wygoda   (dokładnie jak u gubernatora, bez wag i kierunku)
bliskość = 1000 − 12,5 × odległość od serca (‰), czyli 0,5 na granicy 40 pól
```

Wygrywa najwyższa wartość (przy remisie pierwsze sprawdzone). Bez losowania. Jeśli nie ma żadnego pola z wartością > 0, cel to
„brak” (−1). Własna ziemia cywilizacji **nie** może być celem (tylko niczyja).

Cel działa w G2, G3 i G5 jako **premia +15%** dla miejsc bliżej celu niż obecny obóz (`directionBonusPercent`).

### Zapis „Dlaczego?”

Ocena = waga ekspansji. Notatki: zatłoczenie w %, najedzenie w ‰, wartość pola celu. Przykład z gry (seed 7, tura 1):
`Civilization 1, Strategist, SetStrategy, cel 7119, ocena 1000: crowding 33%, fed 1000, food 59917`.

### Kto czyta wynik

| Wynik | Czyta | Skutek |
| --- | --- | --- |
| waga ekspansji | G2, G3, G5 | mnożnik ocen miejsc; **obniża próg odłamu** (G5) |
| waga podboju | G2, D2 | mnożnik oceny wypędzenia i wojny (dziś zawsze 1000) |
| waga dobrobytu | T1, D1, D2 | mnożnik receptur (bez wpływu) i umowy handlowej (działa: konkuruje z innymi krokami i progiem) |
| waga nauki | U1 | mnożnik wszystkich technologii (zawsze 1000) |
| waga bezpieczeństwa | D1, D2 | głodna cywilizacja chętniej proponuje i przyjmuje pakt, sojusz i pokój |
| waga wpływu | nikt | — |
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
  WG --> T1["T1 (bez wpływu)"]
  WG --> DP["D1, D2: handel, pakt, sojusz, pokój"]
```
