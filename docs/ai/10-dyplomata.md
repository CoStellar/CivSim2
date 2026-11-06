# Dyplomata (D1–D3)

[← spis treści](README.md) · kod: `src/CivSim.Core/Ai/DiplomatAdvisor.cs`, `src/CivSim.Core/Diplomacy/` · dane: `data/diplomacy.json` ·
GDD 8.1, 9.1, 9.2 · etap 4d (PR #34)

Dyplomata prowadzi stosunki z cywilizacjami, które jego cywilizacja **naprawdę spotkała** (R1). Ma dwa rodzaje decyzji:
**reaktywne** (odpowiedź na propozycję w każdym kroku świata; przegląd po wypowiedzianej wojnie albo napadzie) i **proaktywne**
(przegląd stosunków, co 50 lat we własnym kroku). Rytmy: [wspólne mechanizmy, pkt 7](01-wspolne-mechanizmy.md#7-rytm-cywilizacji-decyzje-proaktywne-i-reaktywne-adr-0018).

## Umowy i stany

| Rodzaj | Co daje | Zakazuje wypędzania obozów drugiej strony | Złamanie wojną to „zdrada” |
| --- | --- | --- | --- |
| wojna | stan; wypędzanie obozów wroga jest wtedy warte +50% (G2) | — | — |
| pokój | kończy wojnę; rozejm na 100 lat | tak | tak |
| umowa handlowa | wymiana nadwyżek między „bramami” (D-handel niżej) | nie | nie |
| pakt o nieagresji | — | tak | tak |
| sojusz obronny (wymaga paktu) | sojusznik może dołączyć do wojny | tak | tak |

Dziś żadna umowa nie wymaga technologii (pole `requires` istnieje w danych).

## Wspólna ocena kroku dyplomatycznego

`Evaluate`, `DiplomatAdvisor.cs:93`. Ta sama ocena służy do **proponowania** i do **odpowiadania**: druga strona liczy dokładnie to
samo, ze swojego punktu widzenia (GDD 9.1).

```
ocena = 1000 × czynniki × waga celu × mnożnik charakteru
```

Dane wejściowe liczone raz na krok świata, gdy ktoś ma przegląd albo odpowiedź (`Prepare`):

- **odległość** = najmniejsza odległość między osadami obu cywilizacji (te dwie osady to „bramy”),
- **nasi wojownicy** = suma wojowników we wszystkich naszych osadach,
- **ich widziani wojownicy** = suma wojowników w ich osadach stojących na polach, które **znamy** (pamięć mapy W1),
- **sympatia** i **zaufanie** z pamięci relacji (−100 … +100).

Krzywe (proste, w tysięcznych; poza zakresem płaskie):

| Czynnik | Wejście | Od | Do |
| --- | --- | --- | --- |
| opinia | sympatia | −50 → 0 | +50 → 2000 |
| zaufanie | zaufanie | −50 → 0 | +50 → 2000 |
| zaufanie do sojuszu | zaufanie | 0 → 0 | +30 → 1500 |
| zagrożenie | ich widziani wojownicy / nasi, % | 50% → 500 | 200% → 1500 |
| zysk z handlu | ile obie strony zyskałyby, % wartości towarów | 0% → 0 | 40% → 1500 |
| wrogość | sympatia | −50 → 2000 | 0 → 0 (sympatia ≥ 0 wyklucza wojnę) |
| przewaga | nasi / (ich + ich sojusznicy), % | 120% → 0 | 250% → 1500 |
| zmęczenie wojną | lata wojny | 0 → 0 | 150 lat → 1500 |
| opinia o pokoju | sympatia | −100 → 300 | +50 → 1500 |

Czynniki każdego kroku:

| Krok | Czynniki | Cel (waga) | Cecha (mnożnik) |
| --- | --- | --- | --- |
| **umowa handlowa** | opinia × bliskość (bramy ≤ 16 pól, inaczej 0) × zysk z handlu | dobrobyt | handlowość |
| **pakt o nieagresji** | opinia × zaufanie × bliskość (≤ 16 pól) × zagrożenie od nich | bezpieczeństwo | ostrożność |
| **sojusz obronny** | opinia × zaufanie do sojuszu × największe zagrożenie od **innej** cywilizacji (wróg w wojnie = 1500) | bezpieczeństwo | **lojalność** |
| **wojna** | wrogość × powód (0 bez powodu) × przewaga × (gdy łamie umowę: 1000 − lojalność, min. 100) | podbój | agresja |
| **pokój** | zmęczenie wojną × opinia o pokoju | bezpieczeństwo | ostrożność |

**Powód wojny** (GDD 9.3): trwający spór graniczny z nimi (mieszkają w naszej osadzie) albo **jakiekolwiek** pamiętane wspomnienie
ich napaści. Bez powodu wojna ma ocenę 0.

## D1: Odpowiedź na propozycję (reaktywna)

- **Kiedy:** **w każdym kroku świata**, dla każdej oczekującej propozycji do cywilizacji AI, także gdy ta czeka na swój krok.
  Propozycja złożona w kroku świata N dostaje odpowiedź w kroku N+1; bez odpowiedzi wygasa jako odrzucona.
- **Decyzja:** przyjmij, gdy `ocena ≥ 800` (`acceptAbove: 0.8`) i umowę wciąż można podpisać. Bez losowania.
- **Komenda:** `AnswerProposalCommand` (działanie `AcceptTreaty` / `DeclineTreaty`). Odrzuconej propozycji ta sama strona nie
  powtarza przez 100 lat (`retryYears`).

## D2: Przegląd stosunków (proaktywny)

- **Kiedy (proaktywnie):** raz na 50 lat (`reviewEveryYears`), rozłożone między cywilizacje, we własnym kroku: gdy
  `(miesiąc + 84 × numer cywilizacji) mod 600 < długość jej kroku w miesiącach`. W epoce kamienia (krok 50 lat) więc w **każdym
  własnym kroku**; w neolicie co drugim; przy krokach półrocznych w jednym ze 100 (od ADR 0018 bez powtórek w roku przeglądu).
- **Kiedy (reaktywnie, ADR 0018):** w najbliższym kroku świata po tym, jak ktoś **wypowiedział jej wojnę** albo **napadł jej obóz**,
  także poza rytmem. Wtedy rozważa te same kroki (pokój, sojusz z kimś trzecim, odwet wojną).
- **Kandydaci:** dla każdej spotkanej cywilizacji:
  - w wojnie z nią: tylko **pokój** (D3), jeśli ocena ≥ 1000;
  - w pokoju: **handel**, **pakt**, **sojusz** (każdy tylko jeśli można go zaproponować: nie obowiązuje, nie czeka, nie odrzucony
    w ostatnich 100 latach, sojusz po pakcie), jeśli ocena ≥ 1000 (`proposeAbove: 1`); **wojna**, jeśli ocena ≥ 1500
    (`declareWarAbove: 1.5`).
- **Wybór:** **jeden** krok spośród wszystkich cywilizacji i rodzajów: 3 najlepsze, ostrożność albo losowanie
  ([wybór](01-wspolne-mechanizmy.md#3-wybór-chooser-i-advisorpick)).
- **Komenda:** `ProposeTreatyCommand` (`ProposeTreaty`) albo `DeclareWarCommand` (`DeclareWar`).

## D3: Wojna i pokój — co się dzieje po decyzji

- **Wypowiedzenie wojny:** wszystkie umowy z ofiarą znikają. Jeśli któraś zakazywała wojny, ofiara pamięta „złamanie traktatu”
  (sympatia −20, zaufanie −40, 200 lat), a każda cywilizacja, która zna obie, pamięta to w 50% (`witnessPercent`).
- **Sojusznicy ofiary:** każdy dołącza do wojny z szansą `300‰ + 700‰ × lojalność/1000` (`allyJoinBasePerMille`). Dołączył: ofiara
  pamięta „pomoc w wojnie”. Nie dołączył: sojusz się rozpada, ofiara pamięta „złamanie traktatu”. Sojusznik obu stron zostaje z boku.
- **W wojnie:** gubernator (G2) może wypędzać obozy wroga z premią ×1,5 (`warRaidPercent`). Armii jeszcze nie ma (etap 4e).
- **Pokój:** proponuje go dyplomata (D2); druga strona ocenia tak samo (D1). Podpisany pokój to rozejm na 100 lat.
- **Wspólny wróg:** cywilizacje walczące z tym samym wrogiem (a nie ze sobą) mają trwające wspomnienie „wspólny wróg” (+20 sympatii).

## Handel między cywilizacjami (zasada, nie decyzja)

Decyzją jest tylko podpisanie umowy (D1/D2). Sam handel dzieje się automatycznie (`ForeignTradeSystem`) w każdym kroku, w którym krok ma któraś ze stron (za miesiące dłuższego
z ich kroków; ADR 0018): bramy wymieniają
do 50% nadwyżki (`tradeSharePercent`) tych towarów, które druga strona wycenia wyżej po stracie w transporcie (2% na pole), i tylko
tyle, ile drugiej bramie brakuje; barter po równej wartości. Tura z wymianą podtrzymuje wspomnienie „aktywny handel”
(do +30 sympatii, +20 zaufania). Partnerzy handlowi liczą się też jako sąsiedzi przy dyfuzji technologii (U1) i mogą przenieść epidemię.

## Zapis „Dlaczego?”

Czynniki: `opinion`, `trust`, `distance`, `threat`, `trade gain`, `cause`, `strength`, `treaty`, `years at war`, waga i charakter.
Cel wpisu to numer drugiej cywilizacji. Przykłady z gier są w [statystykach](09-statystyki.md).
