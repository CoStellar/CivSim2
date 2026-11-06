# Wszystkie liczby, od których zależą decyzje AI

[← spis treści](README.md)

Każdy parametr: gdzie jest, jaka wartość, w jakiej jednostce i na którą decyzję wpływa. Zmiana wartości w pliku JSON nie wymaga
kodu; liczby „zaszyte w kodzie” wymagają.

## `data/ai.json`

| Klucz | Wartość | Jednostka | Decyzje | Znaczenie |
| --- | --- | --- | --- | --- |
| `pickAmongTop` | 3 | działania | wszystkie | spośród ilu najlepszych AI losuje (1 = zawsze najlepsze) |
| `reviewEverySteps` | 15 | własne kroki | S1 | co ile własnych kroków cywilizacji strateg robi przegląd (ADR 0018) |
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
| `searchEverySteps` | 3 | własne kroki | G2 | co ile własnych kroków szuka nie mocno głodny obóz |
| `starvingFedPercent` | 85 | % | G2 | poniżej tego najedzenia obóz szuka co turę |
| `goalOfFood` | `security` | cel | T1, U1 | któremu celowi służy jedzenie (PR B) |
| `goalOfGoods` | `prosperity` | cel | T1, U1 | któremu celowi służą pozostałe towary (PR B) |
| `tradeGoodsBonusPercent` | 50 | % × handlowość | T1 | premia dla wyrobów cennych, gdy towary płyną do partnera handlowego (PR B) |
| `scienceBehindTechs` | 4 | technologie | S1 | przy takiej przewadze najlepszego spotkanego waga nauki jest najwyższa (PR B) |
| `scienceWeightMax` | 1500 | ‰ | S1 | najwyższa waga nauki (PR B) |
| `influenceMetCivs` | 4 | cywilizacje | S1 | przy tylu spotkanych waga wpływu jest najwyższa (PR B) |
| `influenceWeightMax` | 1300 | ‰ | S1 | najwyższa waga wpływu (PR B) |
| `moveExpansionSharePercent` | 50 | % | G2, G3 | jaką część odchylenia wagi ekspansji i ekspansywności dostaje przeprowadzka ponad „zostać” (PR B) |
| `journeyBetterByPercent` | 60 | % | G3 | o ile wędrówka musi być lepsza (dawniej 2 × próg G2 w kodzie) |
| `journeyPenaltyPerMillePerCell` | 10 | ‰ na pole | G3 | spadek bliskości dalekiego miejsca (dawniej w kodzie) |
| `farSearchEveryCells` | 3 | pola | G3, S1 | gęstość siatki dalekich kandydatów (dawniej w kodzie) |
| `targetZeroAtJourneys` | 2 | promienie wędrówki | S1 | w jakiej odległości cel ekspansji traci całą wartość (dawniej w kodzie) |
| `settleFarmYieldPercent` | 150 | % | G4 | ile potrzeb musi dać plon z pól prawdziwych producentów, żeby osiąść (dawniej w kodzie) |
| `settleOverGatheringPercent` | 100 | % × zapał | G4 | ile tego, co obóz zbiera dziś, musi dać rolnictwo; ekspansywni chcą więcej (PR C) |
| `villageSettlersAbovePercent` | 80 | % / zapał | G7 | od jakiego zapełnienia wioska wysyła osadników (PR C) |
| `villageWornBelowPercent` | 70 | % | G7 | poniżej jakiej żyzności pól (wobec naturalnej) wioska wysyła osadników (PR C) |
| `villageSettlersPeople` | 60 | osoby | G7 | ilu osadników wychodzi (PR C) |
| `villageSettlersEveryYears` | 100 | lata | G7 | najwyżej jedna grupa osadników z wioski na tyle lat (PR C) |
| `exploreAbovePerMille` | 1500 | ‰ | G8 | próg zapału odkrywcy: waga ekspansji × ekspansywność × ciekawość (PR C) |
| `exploreLookCells` | 3 | pola | G8 | w jakim promieniu liczy się nieznany ląd (PR C) |
| `exploreUnknownPercent` | 30 | % | G8 | ile lądu wokół musi być nieznane (PR C) |
| `exploreGroupPercent` | 25 | % | G8 | jaka część obozu idzie na wyprawę (PR C) |
| `plunderBelowStoragePercent` | 50 | % | G6 | poniżej jakiego zapełnienia magazynu obóz zbiera porzucone dobra (dawniej w kodzie) |
| `uncomfortableBelowPercent` | 100 | % | G2 | poniżej tej wygody obóz szuka nowego miejsca (dawniej w kodzie) |
| `opinionSwingPercent` | 50 | % | G2 | jak mocno sympatia zmienia chęć wypędzenia: ±50% przy pełnej sympatii albo nienawiści (dawniej „/ 20” w kodzie) |
| `reviewStaggerYears` | 7 | lata | D2 | rozłożenie przeglądów dyplomaty między cywilizacje (dawniej 84 miesiące w kodzie) |

### `data/ai.json` → `scholar` (uczony, PR B)

| Klucz | Wartość | Jednostka | Znaczenie |
| --- | --- | --- | --- |
| `baseWorthPerPerson` | 0,01 | jedn. wymiany na osobę na rok | każda technologia (cel: nauka) |
| `fieldWorthPerPerson` | 0,02 | j.w. | dziedzina nauka, społeczeństwo, wojsko (cele: nauka, wpływ, podbój) |
| `gatherWorthPercent` | 100 | % | jak liczyć dodatkowe plony zbierania na polach strefy |
| `missingInputPercent` | 25 | % | ile wart jest przepis, gdy brakuje surowca |
| `villageWorthPerPerson` | 0,1 | jedn. na osobę na rok | wioski (rolnictwo), mnożone przez żyzność strefy; **najmocniejsza gałka tempa neolitu** |
| `leadsToPercent` | 25 | % | jaka część najlepszej technologii „za nią” dochodzi do wartości |
| `curiosityCostReliefPercent` | 100 | % | jak mocno ciekawość zmniejsza strach przed kosztem |
| `effects[]` | patrz [uczony](05-uczony.md#priorytet-co-technologia-daje) | jedn. za punkt na 100 osób | wartość punktu efektu: wydajność, odporność na zimno, nastrój, psucie, udźwig |

Test `DeadValueTests` sprawdza każdy z tych parametrów (i każdą cechę oraz wagę celu): zmiana wartości musi zmienić jakąś decyzję
w zapisanych sytuacjach z gier. Parametr, który niczego nie zmienia, wywala test.

## `data/population.json`

| Klucz | Wartość | Jednostka | Decyzje | Znaczenie |
| --- | --- | --- | --- | --- |
| `migration.maxMoveCells` | 10 | pola | G1, G2, G5, G6, G7, G8, marsz G3 | zasięg jednego ruchu |
| `migration.moveWhenFedBelowPercent` | 95 | % | G2 | poniżej tego obóz jest „głodny” |
| `migration.moveWhenBetterByPercent` | 30 | % | G2 (G3: × 2) | histereza przeprowadzki |
| `migration.minStepsBetweenMoves` | 2 | własne kroki | G2, G3 | odpoczynek po przeprowadzce (nie dotyczy reakcji na napad) |
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
| `farming.farmersPerCell` | 40 | osoby | G4 | najwięcej rolników na pole (rachunek osiedlenia jak system rolnictwa) |
| `gathering.foodPerWorkerPerYear` | 2 | jedzenie | G4 | ile zbiera producent na rok (porównanie z rolnictwem, PR C) |
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
| `tradeSharePercent` | 50 | **% na krok** | handel | jaka część nadwyżki idzie przez bramę (w kroku, który ma któraś ze stron) |
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
| `perception.json` `scoutCells` | 15 | W1 | zasięg jednego zwiadowcy |
| `perception.json` `scoutsPerCentury` | 2 | W1 | zwiadowców na stulecie z każdej osady (PR C, tempo na rok) |
| `perception.json` `marchCellsPerCentury` | 20 | G3 | pól marszu wędrówki na stulecie (PR C; najwyżej `maxMoveCells` na krok) |
| `perception.json` `oralMemoryYears` | 300 | W1 | jak długo pamięta pole |
| `perception.json` `journeyCells` | 40 | G3, S1 | zasięg wędrówki i szukania celu |
| `strata.json` potrzeby | 1 jedzenie/rok (producent, rzemieślnik), 1,2 (wojownik) | G4, G5 | potrzeby jedzenia |
| `technologies.json` `diffusionPercent`, `maxNeighbours`, `contactCells`, `minCostPercent` | 20, 2, 12, 25 | U1 | koszt dziś |
| `technologies.json` inspiracje | różne | U1 | obniżki kosztu |
| `trade.json` `linkCells` | 8 | T1 | zasięg sieci handlowej |
| `trade.json` `priceRangePercent` | 25–400 | T1 | granice cen wewnętrznych |
| `goods.json` wartości towarów | różne | T1 | podstawa cen |
| `relations.json` | patrz [06](06-wiedza-i-relacje.md) | G2 przez sympatię | pamięć relacji |
| `eras.json` `monthsPerTurn` | 600 (kamień), 300 (neolit), 240 (brąz), … | wszystkie zegary | rytm cywilizacji w tej erze: długość jej kroku (ADR 0018) |
| `relations.json` `rumorChancePercentPerCentury` | 20 | R5 | szansa pogłoski na parę, przeliczona na miesiące własnego kroku |
| `effects.json` `cold_tolerance` | ogień +4, garbarstwo +4 | wygoda | przesuwa granicę zimna |

## Liczby zaszyte w kodzie (nie w danych)

Od PR B zostały tylko liczby, które są częścią samej metody (nie strojenia):

| Liczba | Gdzie | Decyzje | Znaczenie |
| --- | --- | --- | --- |
| 500 + cecha | `Character.Multiplier` | wszystkie | zakres mnożnika charakteru 0,5–1,5 |
| średnia z 2 losowań | `Character.Roll` | C1 | rozkład cech |
| 150 × pola lądu | `ScholarAdvisor.Priorities` | U1 | „plon rolny” pełnej żyzności (50 + 100) jako 100% |
| kwadrat ilorazu kosztów (bez ciekawości) | `ScholarAdvisor.CostFactor` | U1 | jak mocno taniość przeważa |
| alarmy `WarDeclared`, `Attacked` | `WorldClock.cs`, `Treaties.DeclareWar`, `Contacts.RecordAttack` | D2, G2 | co budzi reakcję poza rytmem |
| 3000 | `SetStrategyCommand.MaxWeight` | S1 | najwyższa dozwolona waga celu |
| 3 | `Chooser.MaxRoom` | wszystkie | najwięcej działań w losowaniu |
| kolejność G1 → G2 → G3 → G4 → G5 → G6 | `Decide` | gubernator | priorytet decyzji obozu |
