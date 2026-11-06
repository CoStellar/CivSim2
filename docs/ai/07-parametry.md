# Wszystkie liczby, od których zależą decyzje AI

[← spis treści](README.md)

Każdy parametr: gdzie jest, jaka wartość, w jakiej jednostce i na którą decyzję wpływa. Zmiana wartości w pliku JSON nie wymaga
kodu; liczby „zaszyte w kodzie” wymagają.

## `data/ai.json`

| Klucz | Wartość | Jednostka | Decyzje | Znaczenie |
| --- | --- | --- | --- | --- |
| `pickAmongTop` | 3 | działania | wszystkie | spośród ilu najlepszych AI losuje (1 = zawsze najlepsze) |
| `reviewEveryTurns` | 15 | **tury** | S1 | co ile tur strateg robi przegląd |
| `directionBonusPercent` | 15 | % | G2, G3, G5 | premia dla miejsc w stronę celu ekspansji |
| `recipeSwitchBetterByPercent` | 25 | % | T1 | o ile nowa receptura musi być lepsza |
| `crowdingLowPercent` | 50 | % | S1 | do tego zatłoczenia waga ekspansji minimalna |
| `crowdingHighPercent` | 90 | % | S1 | od tego zatłoczenia waga ekspansji maksymalna |
| `expansionWeightMin` | 1000 | ‰ | S1 | najniższa waga ekspansji |
| `expansionWeightMax` | 1600 | ‰ | S1 | najwyższa waga ekspansji |
| `fedWellPercent` | 95 | % | S1 | od tego najedzenia waga głodu 1000 |
| `fedBadlyPercent` | 60 | % | S1 | do tego najedzenia waga głodu maksymalna |
| `hungerWeightMax` | 1500 | ‰ | S1 | najwyższa waga bezpieczeństwa i dobrobytu |
| `splitMinPercentOfLevel` | 50 | % | G5 | najniższy próg zatłoczenia dla odłamu |
| `searchEveryTurns` | 3 | **tury** | G2 | co ile tur szuka nie mocno głodny obóz |
| `starvingFedPercent` | 85 | % | G2 | poniżej tego najedzenia obóz szuka co turę |

## `data/population.json`

| Klucz | Wartość | Jednostka | Decyzje | Znaczenie |
| --- | --- | --- | --- | --- |
| `migration.maxMoveCells` | 10 | pola | G1, G2, G5, G6, marsz G3 | zasięg jednego ruchu |
| `migration.moveWhenFedBelowPercent` | 95 | % | G2 | poniżej tego obóz jest „głodny” |
| `migration.moveWhenBetterByPercent` | 30 | % | G2 (G3: × 2) | histereza przeprowadzki |
| `migration.minTurnsBetweenMoves` | 2 | **tury** | G2, G3 | odpoczynek po przeprowadzce |
| `migration.distancePenaltyPercentPerCell` | 3 | % na pole | G2, G5 | spadek bliskości |
| `migration.splitAbovePercentOfLevel` | 80 | % | G5 | bazowy próg zatłoczenia |
| `migration.splitPercent` | 40 | % | G5 | jaka część ludzi odchodzi |
| `migration.minSplitPeople` | 60 | osoby | G5 | najmniejszy obóz, który się dzieli |
| `migration.minDistanceBetweenOwnCamps` | 4 | pola | G5 | odstęp nowego obozu od własnych |
| `migration.splitNeedsFoodPercent` | 100 | % | G5 | jaką część potrzeb odłamu musi dać ziemia |
| `migration.mergeBelowPeople` | 15 | osoby | G1 | poniżej obóz dołącza do innego |
| `migration.ownCampsiteBonusPercent` | 10 | % | G2, G5 | premia za stare obozowisko |
| `migration.attackAboveStrengthPercent` | 150 | % | G2 | wymagana przewaga do wypędzenia |
| `comfort.coldBelow` | −5 | °C | G2, G3, G5, S1 | granica zimna |
| `comfort.hotAbove` | 38 | °C | j.w. | granica upału |
| `comfort.percentPerDegree` | 2 | % na °C | j.w. | spadek wygody |
| `comfort.minPercent` | 5 | % | j.w. | najniższa wygoda |
| `influence.baseRange` | 3 | koszt ruchu | G2, G3, G5, S1 | jak daleko sięga „ziemia miejsca” |
| `influence.maxRange` | 8 | pola | G4 | promień liczenia plonu z pól |
| `influence.hillsCost` / `mountainsCost` | 1 / 2 | koszt ruchu | j.w. | wzgórza i góry zmniejszają „ziemię miejsca” |
| `gathering.foodPerBiomeFoodPerYear` | 4 | jedzenie | wartość miejsca | jedzenie pola na rok na punkt jedzenia biomu |
| `conquest.defenderNonWarriorPercent` | 25 | % | G2 | ile nie-wojowników liczy się do obrony |
| `farming.grainPerFarmerPerYear` | 3 | zboże | G4 | plon jednego rolnika |
| `farming.farmersPerCell` | 40 | osoby | G4 | rolników na pole w rachunku osiedlenia |
| `farming.minFertility` | 20 | punkty | G4 | pole słabsze nie daje zboża |

## `data/diplomacy.json` (od 4d)

| Klucz | Wartość | Jednostka | Decyzje | Znaczenie |
| --- | --- | --- | --- | --- |
| `reviewEveryYears` | 50 | lata | D2 | co ile lat przegląd stosunków |
| `proposeAbove` | 1 | ocena | D2 | próg propozycji umowy i pokoju |
| `acceptAbove` | 0,8 | ocena | D1 | próg przyjęcia |
| `declareWarAbove` | 1,5 | ocena | D2 | próg wypowiedzenia wojny |
| `retryYears` | 100 | lata | D2 | po odmowie nie proponuje tego samego |
| `reachCells` | 16 | pola | D1, D2, handel | zasięg handlu i paktu |
| `tradeSharePercent` | 50 | **% na turę** | handel | jaka część nadwyżki idzie przez bramę |
| `witnessPercent` | 50 | % | D3 | jak mocno złamanie umowy pamiętają świadkowie |
| `allyJoinBasePerMille` | 300 | ‰ | D3 | szansa dołączenia sojusznika przy lojalności 0 |
| `warRaidPercent` | 150 | % | G2 | premia za wypędzanie obozów wroga w wojnie |
| `curves.*` | patrz [dyplomata](10-dyplomata.md) | | D1, D2 | 9 krzywych czynników |
| `treaties[].years` | pokój: 100 | lata | D3 | rozejm po wojnie |

## Inne pliki

| Plik i klucz | Wartość | Decyzje | Znaczenie |
| --- | --- | --- | --- |
| `settlements.json` `settle.minPeople` | 60 | G4 | najmniejszy obóz, który może osiąść |
| `settlements.json` poziomy | obóz: do 150 osób, magazyn 300, ruchomy; wioska: do 1000, 2000, wymaga rolnictwa | G4, G5, G6, S1 | pojemność i magazyn |
| `settlements.json` `carrying.perPerson` | 10 | G1, G2, G5, G6 | ile dóbr niesie osoba |
| `settlements.json` `abandonment.claimYears` | 100 | R4 | jak długo trwa roszczenie do porzuconej osady |
| `perception.json` `sightCells` | 6 | W1 | widzenie osady |
| `perception.json` `scoutCells` | 15 | W1 | zasięg zwiadowcy |
| `perception.json` `oralMemoryYears` | 300 | W1 | jak długo pamięta pole |
| `perception.json` `journeyCells` | 40 | G3, S1 | zasięg wędrówki i szukania celu |
| `strata.json` potrzeby | 1 jedzenie/rok (producent, rzemieślnik), 1,2 (wojownik) | G4, G5 | potrzeby jedzenia |
| `technologies.json` `diffusionPercent`, `maxNeighbours`, `contactCells`, `minCostPercent` | 20, 2, 12, 25 | U1 | koszt dziś |
| `technologies.json` inspiracje | różne | U1 | obniżki kosztu |
| `trade.json` `linkCells` | 8 | T1 | zasięg sieci handlowej |
| `trade.json` `priceRangePercent` | 25–400 | T1 | granice cen wewnętrznych |
| `goods.json` wartości towarów | różne | T1 | podstawa cen |
| `relations.json` | patrz [06](06-wiedza-i-relacje.md) | G2 przez sympatię | pamięć relacji |
| `eras.json` `monthsPerTurn` | 600 (kamień), 300 (neolit), … | wszystkie zegary | długość tury |
| `effects.json` `cold_tolerance` | ogień +4, garbarstwo +4 | wygoda | przesuwa granicę zimna |

## Liczby zaszyte w kodzie (nie w danych)

| Liczba | Gdzie | Decyzje | Znaczenie |
| --- | --- | --- | --- |
| 500 + cecha | `Character.Multiplier` | wszystkie | zakres mnożnika charakteru 0,5–1,5 |
| średnia z 2 losowań | `Character.Roll` | C1 | rozkład cech |
| 1000 − 10 × odległość | `GovernorAdvisor._farNearness` | G3 | bliskość dalekiego miejsca |
| co 3. pole | `FarSite`, `StrategistAdvisor.Target` | G3, S1 | rzadka siatka kandydatów |
| 2 × próg przeprowadzki | `Decide` | G3 | histereza wędrówki (60%) |
| 2 × promień wędrówki | `StrategistAdvisor.Target` | S1 | bliskość celu (0,5 na 40 polach) |
| plon × 2 ≥ potrzeby × 3 | `Decide` | G4 | próg osiedlenia 150% |
| magazyn < 50% | `Decide` | G6 | kiedy zbierać porzucone dobra |
| wygoda < 100% | `Decide` | G2 | „zmarznięty” wyzwala szukanie |
| sympatia / 20 | `Place` | G2 | siła czynnika opinii |
| 1 000 000 000 | `ScholarAdvisor.TechnologyWorth` | U1 | wartość każdej technologii |
| kwadrat ilorazu kosztów | `ScholarAdvisor` | U1 | jak mocno taniość przeważa |
| 3000 | `SetStrategyCommand.MaxWeight` | S1 | najwyższa dozwolona waga celu |
| 3 | `Chooser.MaxRoom` | wszystkie | najwięcej działań w losowaniu |
| kolejność G1 → G2 → G3 → G4 → G5 → G6 | `Decide` | gubernator | priorytet decyzji obozu |
