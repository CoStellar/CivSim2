# Uczony (U1)

[← spis treści](README.md) · kod: `src/CivSim.Core/Research/ResearchSystem.cs` (`ScholarAdvisor`) · GDD 6.1, 8.1 · system: `docs/systems/research.md`

Od PR B (przebudowa AI, punkt 3 planu Patryka) uczony nie bierze już „najtańszego”. Każda technologia ma **priorytet**: ile da
właśnie tej cywilizacji (z jej położenia, braków i charakteru). Koszt jest **przeszkodą**, którą ciekawość świata zmniejsza.

## U1: Wybór technologii do badania

**Komenda:** `SetResearchCommand(technologia)`. Odrzucana, gdy technologia jest już znana, jej wymagania nie są spełnione albo (od etapu 4F) cywilizacja nie ma potrzebnych ludzi.

### Kiedy

Dla każdej żywej cywilizacji AI, która **nic nie bada** (na starcie i zaraz po odkryciu technologii, bo system badań zeruje wybór).
To decyzja **reaktywna** (ADR 0018): zapada w najbliższym kroku świata, także poza rytmem cywilizacji. Punkty zebrane ponad koszt
przechodzą na następną technologię, więc przerwy w badaniu nie ma.

### Kandydaci

Każda technologia **dostępna**: jeszcze nieznana, wszystkie wymagane (`requires`) znane, a jeśli ma listę „jedna z” (`requiresAny`),
to co najmniej jedna z niej znana. **Od etapu 4F (PR 3)** także z ludźmi, których potrzebuje (`requiresPeople`, np. łowiectwo
zespołowe: 5 krzemieniarzy; `Technologies.CanResearch`). Uczony wybiera też od nowa, gdy obecnego badania nie da się skończyć, bo
ludzi ubyło (punkty zbierają się dalej, nic nie przepada). Gdy żadna technologia nie ma swoich ludzi, uczony czeka.

### Co uczony wie o swojej cywilizacji

Liczone raz na krok świata (`CivFacts.Measure`), tylko z własnej strefy wpływów i własnych osad:

| Wielkość | Skąd |
| --- | --- |
| ludzie | suma mieszkańców |
| pola złóż i biomów | pola strefy z każdym złożem i biomem; woda w strefie i tuż przy niej liczy się raz |
| pola lądu i „plon rolny” | pola lądu strefy; dla pól o żyzności ≥ 20: suma (50 + żyzność), czyli tyle, ile mnoży plon rolnika |
| najzimniejsza zima | najniższa temperatura zimy przy jej obozach |
| towary | zapasy we wszystkich magazynach |
| rzemieślnicy | ilu rzemieślników mają jej osady |

### Priorytet: co technologia daje

Wartość liczona w setnych częściach jednostki wymiany **na rok**. Każda część trafia do celu, któremu służy, i jest mnożona przez
wagę tego celu i charakter (patrz [wspólne mechanizmy](01-wspolne-mechanizmy.md)). Liczby w nawiasach to parametry z
`data/ai.json` → `scholar`.

| Część | Wzór | Cel |
| --- | --- | --- |
| podstawa | ludzie × 0,01 (`baseWorthPerPerson`) | nauka |
| dziedzina | ludzie × 0,02 (`fieldWorthPerPerson`) dla dziedzin nauka, społeczeństwo, wojsko | nauka, wpływ, podbój |
| zbieranie | pola strefy z biomem lub złożem × dodatkowy plon × wartość towaru × 100% (`gatherWorthPercent`) | jedzenie → bezpieczeństwo, reszta → dobrobyt |
| receptury | (wartość wyjść − wartość wejść) × rzemieślnicy / (pracownicy × lata); × 25% (`missingInputPercent`), gdy brakuje surowca | cel pierwszego wyjścia |
| efekty | punkty efektu × ludzie / 100 × wartość punktu (`effects`) | patrz niżej |
| wioski | ludzie × 0,1 (`villageWorthPerPerson`) × plon rolny / (150 × pola lądu) | bezpieczeństwo |

**Wartość punktu efektu** (`scholar.effects`, za 100 osób):

| Efekt | Cel | Za punkt | Uwagi |
| --- | --- | --- | --- |
| `output_percent` (wydajność) | bezpieczeństwo | 1 | |
| `cold_tolerance_degrees` (odporność na zimno) | bezpieczeństwo | 2 | tylko tyle stopni, ile brakuje: najzimniejsza zima poniżej granicy zimna (−5 °C) po odjęciu znanej odporności |
| `mood` (nastrój) | wpływ | 0,5 | |
| `spoilage_percent` (psucie) | bezpieczeństwo | 0,1 | mniej znaczy lepiej |
| `carry_capacity_percent` (udźwig) | ekspansja | 0,2 | |

Na końcu dochodzi **droga dalej**: 25% (`leadsToPercent`) wartości najlepszej nieznanej technologii, która wymaga tej. Dzięki temu
ogień i krzemień mają sens, nawet gdy same dają mało, bo prowadzą do rolnictwa.

```
priorytet = Σ (część × waga celu × charakter celu) + 25% × najlepsza technologia „za nią”      (co najmniej 1)
```

### Koszt jako przeszkoda

```
koszt dziś = koszt bazowy × (100% − inspiracje − dyfuzja), ale nie mniej niż 25% kosztu bazowego
r = najtańsza dostępna / koszt dziś                                   (‰, najtańsza ma 1000)
przeszkoda = r × (r + (1 − r) × ciekawość × ulga)                    (ulga: curiosityCostReliefPercent = 100%)
ocena = priorytet × przeszkoda
```

| Technologia | Ciekawość 0 | Ciekawość 500 | Ciekawość 1000 |
| --- | --- | --- | --- |
| najtańsza | 100% | 100% | 100% |
| 2 razy droższa | 25% | 37,5% | 50% |
| 3 razy droższa | 11% | 18,5% | 33% |

Ciekawa cywilizacja mniej boi się drogich technologii: od PR B ciekawość świata naprawdę zmienia wybór (dawniej mnożyła wszystkie
technologie jednakowo). **Inspiracje** (`data/technologies.json`) i **dyfuzja** (sąsiad, który zna technologię, −20%, najwyżej 2
sąsiadów) działają jak wcześniej.

Wybór: 3 najlepsze, ostrożność albo losowanie (jak u wszystkich doradców), więc decyzja nie zawsze jest optymalna.

### Zapis „Dlaczego?”

Notatki: `serves <cel>` (cel, który dał największą część), `leads to` (ile dodała droga dalej), czynnik `research cost` (z kosztem dziś
w setnych punktu). Przykład (seed 7, tura 1): `Niakiia, Scholar, SetResearch 0 (ogień), ocena 1044: serves security, leads to 322,
research cost 100% (420000)`. Ogień służy tu bezpieczeństwu (odporność na zimno przy mroźnej zimie), a 322 z oceny to droga dalej
(do rolnictwa i tradycji). Koszt 4200 zamiast 6000, bo zadziałała inspiracja „zima poniżej 0 °C” (−30%).

### Przykłady z gier

- Plemię nad morzem: rybołówstwo ma dużą część „zbieranie” (12 pól wybrzeża × 4 ryby × wartość ryby), plemię w głębi lądu prawie
  zerową (test `FishingIsWorthMoreOnTheCoast`).
- Plemię w mroźnym klimacie: ogień daje 4 stopnie odporności, które naprawdę są potrzebne; w ciepłym klimacie ta część jest zerowa
  (test `FireIsWorthMoreInTheCold`).
- Rolnictwo: część „wioski” rośnie z żyznością strefy; na jałowej ziemi zostaje tylko zbieranie dzikiego zboża
  (test `FarmingIsWorthLessOnBarrenLand`).
- Skutek w grach: rolnictwo jest zwykle 2. albo 3. technologią, a pierwsza cywilizacja wchodzi w neolit o 40–60 tur wcześniej niż
  przed PR B (patrz [statystyki](09-statystyki.md)).

### Co uczony bierze pod uwagę, a czego nie

| Bierze pod uwagę | Nie bierze pod uwagę |
| --- | --- |
| koszt dziś (inspiracje, sąsiedzi) i ciekawość | ceny w sieci handlowej (wartości towarów są bazowe) |
| co technologia daje: zbieranie, receptury, efekty, wioski | technologie dalej niż jeden krok |
| strefę: złoża, biomy, wodę, żyzność, zimę | co mają sąsiedzi do wymiany |
| wagi stratega i charakter (każdy cel osobno) | |
