# Dyplomata (D1–D3)

[← spis treści](README.md) · kod: `src/CivSim.Core/Ai/DiplomatAdvisor.cs`, `src/CivSim.Core/Diplomacy/` · dane: `data/diplomacy.json` ·
GDD 8.1, 9.1, 9.2, 9.3 · etap 4d (PR #34), cel wojny i warunki pokoju: etap 4e PR 1 (`data/war.json`, ADR 0021)

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
- **ich widziani wojownicy** = suma wojowników w ich osadach stojących na polach, które **widzieliśmy w ostatnim własnym kroku**
  (`MapMemory.SeesNow`, od PR B; wcześniej wystarczyło pole pamiętane do 300 lat),
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
| zmęczenie wojną (od 4e) | nasze zmęczenie wojną, 0…100 (lata wojny + straty, `data/war.json`) | 0 → 0 | 100 → 1500 |
| opinia o pokoju | sympatia | −100 → 300 | +50 → 1500 |
| warunki pokoju (od 4e) | ile warunki są dla nas warte w punktach wyniku wojny (oddanie osady 40, okup: 20 + 2 × procent; ujemne, gdy dajemy) | −50 → 400 | +50 → 1600 |

Czynniki każdego kroku:

| Krok | Czynniki | Cel (waga) | Cecha (mnożnik) |
| --- | --- | --- | --- |
| **umowa handlowa** | opinia × bliskość (bramy ≤ 16 pól, inaczej 0) × zysk z handlu | dobrobyt | handlowość |
| **pakt o nieagresji** | opinia × zaufanie × bliskość (≤ 16 pól) × zagrożenie od nich | bezpieczeństwo | ostrożność |
| **sojusz obronny** | opinia × zaufanie do sojuszu × największe zagrożenie od **innej** cywilizacji (wróg w wojnie = 1500) | bezpieczeństwo | **lojalność** |
| **wojna** | wrogość × powód (0 bez powodu) × przewaga × (gdy łamie umowę: 1000 − lojalność, min. 100) | podbój | agresja |
| **pokój** | zmęczenie wojną × wynik wojny (od 4e PR 6) × opinia o pokoju × warunki pokoju | bezpieczeństwo | ostrożność |

**Powód wojny** (GDD 9.3): trwający spór graniczny z nimi (mieszkają w naszej osadzie) albo **jakiekolwiek** pamiętane wspomnienie
ich napaści albo agresji (od 4e: wypowiedzianej nam wojny o ziemię lub łupy; `Treaties.HasGrievance`). Bez powodu wojna ma ocenę 0.

**Cel wojny** (od 4e, GDD 9.3, ADR 0021): **ziemia**, gdy mieszkają w osadzie, do której mamy roszczenie (`Treaties.ClaimedSettlement`; cel to ta
osada), w innym razie **odwet**. Wojny o łupy dyplomata sam nie wypowiada (to decyzja dowódcy w PR 4).

## D1: Odpowiedź na propozycję (reaktywna)

- **Kiedy:** **w każdym kroku świata**, dla każdej oczekującej propozycji do cywilizacji AI, także gdy ta czeka na swój krok.
  Propozycja złożona w kroku świata N dostaje odpowiedź w kroku N+1; bez odpowiedzi wygasa jako odrzucona.
- **Decyzja:** przyjmij, gdy `ocena ≥ 800` (`acceptAbove: 0.8`) i umowę wciąż można podpisać. Bez losowania.
- **Komenda:** `AnswerProposalCommand` (działanie `AcceptTreaty` / `DeclineTreaty`). Odrzuconej propozycji ta sama strona nie
  powtarza przez 100 lat (`retryYears`), a odrzuconego **pokoju** tylko przez rok (`peaceRetryYears`, od 4e PR 6: wynik wojny zmienia się
  co miesiąc kampanii, więc pokój odrzucony dziś może być przyjęty za rok).
- **Wynik wojny** (od 4e PR 6, krzywa `warScore`): strona, która przegrywa, chce pokoju bardziej: wynik z jej strony −50 i gorzej daje czynnik 5,
  0 i lepiej czynnik 1 (liniowo pomiędzy). Wcześniej przegrany prawie nigdy nie przyjmował warunków, bo nienawiść do wroga (opinia −100)
  dawała czynnik 0,3, a oddanie czegoś jeszcze go obniżało. Od PR 6 opinia −100 daje 0,5 (`peaceOpinion`).

## D2: Przegląd stosunków (proaktywny)

- **Kiedy (proaktywnie):** raz na 50 lat (`reviewEveryYears`), rozłożone między cywilizacje, we własnym kroku: gdy
  `(miesiąc + 84 × numer cywilizacji) mod 600 < długość jej kroku w miesiącach` (84 miesiące = 7 lat, `reviewStaggerYears`). W epoce kamienia (krok 50 lat) więc w **każdym
  własnym kroku**; w neolicie co drugim; przy krokach półrocznych w jednym ze 100 (od ADR 0018 bez powtórek w roku przeglądu).
- **Kiedy (reaktywnie, ADR 0018):** w najbliższym kroku świata po tym, jak ktoś **wypowiedział jej wojnę** albo **napadł jej obóz**,
  także poza rytmem. Wtedy rozważa te same kroki (pokój, sojusz z kimś trzecim, odwet wojną). Od 4e PR 2 alarm daje krok świata
  w następnym miesiącu.
- **Kiedy (tylko pokój, od 4e PR 2):** w **każdym kroku świata** (także w krokach kampanii, co miesiąc), gdy w którejś wojnie nie mamy
  własnej kampanii w drodze, a nasze zmęczenie wojną ≥ 60 albo wynik wojny ≥ ±20 (można żądać lub oferować warunki). Wtedy rozważa
  tylko pokój w takich wojnach (`ReviewWars`).
- **Kandydaci:** dla każdej spotkanej cywilizacji:
  - w wojnie z nią: tylko **pokój** (D3), jeśli ocena ≥ 1000, a gdy nasze zmęczenie wojną ≥ 60 (`war.json` `peaceAbove`), przy każdej
    ocenie powyżej zera;
  - w pokoju: **handel**, **pakt**, **sojusz** (każdy tylko jeśli można go zaproponować: nie obowiązuje, nie czeka, nie odrzucony
    w ostatnich 100 latach, sojusz po pakcie), jeśli ocena ≥ 1000 (`proposeAbove: 1`); **wojna**, jeśli ocena ≥ 1500
    (`declareWarAbove: 1.5`).
- **Wybór:** **jeden** krok spośród wszystkich cywilizacji i rodzajów: 3 najlepsze, ostrożność albo losowanie
  ([wybór](01-wspolne-mechanizmy.md#3-wybór-chooser-i-advisorpick)).
- **Komenda:** `ProposeTreatyCommand` (`ProposeTreaty`) albo `DeclareWarCommand` (`DeclareWar`).

## D3: Wojna i pokój — co się dzieje po decyzji

- **Wypowiedzenie wojny (z celem, od 4e):** ofiara pamięta „agresję” (sympatia −30, zaufanie −15, 100 lat) w procencie z celu:
  ziemia i łupy 100%, odwet 50%; świadkowie (znają obie strony): łupy 100%, ziemia 50%, odwet 0%. Wszystkie umowy z ofiarą znikają. Jeśli któraś zakazywała wojny, ofiara pamięta „złamanie traktatu”
  (sympatia −20, zaufanie −40, 200 lat), a każda cywilizacja, która zna obie, pamięta to w 50% (`witnessPercent`).
- **Sojusznicy ofiary:** każdy dołącza do wojny z szansą `300‰ + 700‰ × lojalność/1000` (`allyJoinBasePerMille`). Dołączył: ofiara
  pamięta „pomoc w wojnie”. Nie dołączył: sojusz się rozpada, ofiara pamięta „złamanie traktatu”. Sojusznik obu stron zostaje z boku.
- **W wojnie:** gubernator (G2) może wypędzać obozy wroga z premią ×1,5 (`warRaidPercent`); dowódca (K1) wysyła kampanie
  ([dowódca](11-dowodca.md)). Wojna, w której przez 5 lat nikt nie prowadził kampanii, kończy się sama białym pokojem.
- **Wynik i zmęczenie (od 4e):** napaść w czasie wojny daje zwycięskiej stronie +10 do wyniku, zdobyta osada +15 więcej (−100…+100).
  Zmęczenie strony = miesiące kampanii w tej wojnie × 5 + procent jej ludzi zabitych w wojnie × 3 (najwyżej 100; od 4e PR 2,
  wcześniej lata wojny × 1). Nastrój wszystkich jej osad spada o
  zmęczenie × 20 / 100 (efekt `war_weariness`).
- **Pokój z warunkami (od 4e):** proponujący liczy warunki z wyniku (`Wars.TermsFor`): przy wyniku co najmniej ±20 przegrany oddaje
  osadę, o którą była wojna o ziemię, a w innym razie płaci jednorazowy okup: (wynik − 20 + 1) × 0,5% każdego zapasu (najwyżej 40%);
  przy bliższym wyniku biały pokój. Druga strona ocenia propozycję z tymi warunkami (D1). Podpisany pokój wykonuje warunki i daje rozejm na 100 lat.
- **Wspólny wróg:** cywilizacje walczące z tym samym wrogiem (a nie ze sobą) mają trwające wspomnienie „wspólny wróg” (+20 sympatii).

## Handel między cywilizacjami (zasada, nie decyzja)

Decyzją jest tylko podpisanie umowy (D1/D2). Sam handel dzieje się automatycznie (`ForeignTradeSystem`) w każdym kroku, w którym krok ma któraś ze stron (za miesiące dłuższego
z ich kroków; ADR 0018): bramy wymieniają
do 50% nadwyżki (`tradeSharePercent`) tych towarów, które druga strona wycenia wyżej po stracie w transporcie (2% na pole), i tylko
tyle, ile drugiej bramie brakuje; barter po równej wartości. Tura z wymianą podtrzymuje wspomnienie „aktywny handel”
(do +30 sympatii, +20 zaufania). Partnerzy handlowi liczą się też jako sąsiedzi przy dyfuzji technologii (U1) i mogą przenieść epidemię.

## Zapis „Dlaczego?”

Czynniki: `opinion`, `trust`, `distance`, `threat`, `trade gain`, `cause`, `strength`, `treaty`, `war weariness`, `peace terms`, waga i charakter.
Cel wpisu to numer drugiej cywilizacji. Przykłady z gier są w [statystykach](09-statystyki.md).
