# Wszystkie liczby, od których zależą decyzje AI

**Etap 6 PR 2 (decyzja Patryka z 2026-11-09):** wszystkie czasy wojny, dyplomacji i polityki (rozejm, ponowienie propozycji, pamięć krzywd, kryzys, bezkrólewie, spokój po buncie, roszczenia, przegląd dyplomaty, trzymanie ustroju) liczą się w **latach akcji**: 1 rok = 12 miesięcy akcji = 60 taktów = 6 s przy ×1 (`data/time.json`). Długie wartości skrócone (w tabelach „było …”).

[← spis treści](README.md)

Każdy parametr: gdzie jest, jaka wartość, w jakiej jednostce i na którą decyzję wpływa. Zmiana wartości w pliku JSON nie wymaga
kodu; liczby „zaszyte w kodzie” wymagają.

## `data/ai.json`

| Klucz | Wartość | Jednostka | Decyzje | Znaczenie |
| --- | --- | --- | --- | --- |
| `pickAmongTop` | 3 | działania | wszystkie | spośród ilu najlepszych AI losuje (1 = zawsze najlepsze) |
| `playerOrderHoldYears` | 10 | lata akcji (10 = 60 s przy ×1) | G1–G7, zwiad | jak długo AI nie rusza osady, której gracz wydał rozkaz, i obozu założonego przez osadników gracza (etap 6 PR P1, `docs/systems/levers.md`) |
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
| `villageHungryFedPercent` | 70 | % | G7 | wioska najedzona poniżej tego wysyła osadników, żeby było mniej ust do wykarmienia (poprawki po 4g) |
| `villageHungrySettlersEveryYears` | 20 | lata | G7 | głodna wioska wysyła najwyżej jedną grupę na tyle lat (poprawki po 4g) |
| `exploreAbovePerMille` | 1500 | ‰ | G8 | próg zapału odkrywcy: waga ekspansji × ekspansywność × ciekawość (PR C) |
| `exploreLookCells` | 3 | pola | G8 | w jakim promieniu liczy się nieznany ląd (PR C) |
| `exploreUnknownPercent` | 30 | % | G8 | ile lądu wokół musi być nieznane (PR C) |
| `exploreGroupPercent` | 25 | % | G8 | jaka część obozu idzie na wyprawę (PR C) |
| `plunderBelowStoragePercent` | 50 | % | G6 | poniżej jakiego zapełnienia magazynu obóz zbiera porzucone dobra (dawniej w kodzie) |
| `uncomfortableBelowPercent` | 100 | % | G2 | poniżej tej wygody obóz szuka nowego miejsca (dawniej w kodzie) |
| `opinionSwingPercent` | 50 | % | G2 | jak mocno sympatia zmienia chęć wypędzenia: ±50% przy pełnej sympatii albo nienawiści (dawniej „/ 20” w kodzie) |
| `reviewStaggerYears` | 7 | lata | D2 | rozłożenie przeglądów dyplomaty między cywilizacje (dawniej 84 miesiące w kodzie) |
| `overloadExpansionMinPercent` | 50 | % | S1 | przy przeciążonej administracji waga ekspansji × pojemność / zużycie, ale nie mniej niż tyle (etap 4f PR 5) |
| `unrestSecurityPercent` | 150 | % | S1 | w niepokojach (stabilność poniżej 30) waga bezpieczeństwa razy tyle (etap 4f PR 5) |
| `unrestWarPercent` | 50 | % | D2 | w niepokojach ocena wojny razy tyle (etap 4f PR 5) |
| `governmentChangeMarginPercent` | 30 | % | S2 | o ile lepiej musi pasować nowy ustrój, żeby strateg go wprowadził (histereza; etap 4f PR 5) |
| `governmentStabilityWeight` | 40 | punkty dopasowania | S2 | w niepokojach każdy punkt premii stabilności ustroju dodaje tyle do dopasowania (etap 4f PR 5) |
| `governmentKeepYears` | 20 (było 200) | lata akcji | S2 | tyle lat akcji (20 = 2 min przy ×1) po zmianie ustroju (także po przewrocie) strateg nie rozważa kolejnej (etap 4f PR 5; etap 6 PR 2) |

### `data/ai.json` → `commander` (dowódca, etap 4e PR 5)

| Klucz | Wartość | Jednostka | Decyzje | Znaczenie |
| --- | --- | --- | --- | --- |
| `minWinChancePercent` | 60 | % spodziewanych wygranych | K1 | najmniejsza szansa ze spodziewanej bitwy, przy której dowódca wysyła armię na widziany cel (wcześniej w `war.json`) |
| `maxLossPercent` | 50 | % armii | K1 | najwięcej spodziewanych strat, przy których dowódca wysyła armię |
| `minFoodMonths` | 1 | miesiące | K1, K2 | zapas jedzenia ponad miesiące marszu poza zasięgiem tragarzy tam i z powrotem (wcześniej w `war.json`: jedzenie na start) |
| `recallChancePercent` | 45 | % | K2 | w czasie kampanii: poniżej tej szansy dowódca woła armię do domu |

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
| `reviewEveryYears` | 50 | lata akcji | D2 | co ile lat akcji przegląd stosunków (50 = 5 min przy ×1; etap 6 PR 2) |
| `proposeAbove` | 1 | ocena | D2 | próg propozycji umowy i pokoju |
| `acceptAbove` | 0,8 | ocena | D1 | próg przyjęcia |
| `declareWarAbove` | 1,5 | ocena | D2 | próg wypowiedzenia wojny |
| `retryYears` | 20 (było 100) | lata akcji | D2 | po odmowie nie proponuje tego samego (20 = 2 min przy ×1) |
| `peaceRetryYears` | 1 | lata | D2 | po odmowie pokoju proponuje go znowu po roku (od 4e PR 6) |
| `reachCells` | 16 | pola | D1, D2, handel | zasięg handlu i paktu; od etapu 5b także wojny o odwet (jak wcześniej wojny o łupy) |
| `tradeSharePercent` | 50 | **% na krok** | handel | jaka część nadwyżki idzie przez bramę (w kroku, który ma któraś ze stron) |
| `witnessPercent` | 50 | % | D3 | jak mocno złamanie umowy pamiętają świadkowie |
| `allyJoinBasePerMille` | 300 | ‰ | D3 | szansa dołączenia sojusznika przy lojalności 0 |
| `warRaidPercent` | 150 | % | G2 | premia za wypędzanie obozów wroga w wojnie |
| `curves.*` | patrz [dyplomata](10-dyplomata.md) | | D1, D2 | 15 krzywych czynników (od 4e: `warWeariness` liczy zmęczenie 0…100 zamiast lat, nowa `peaceTerms`; od PR 6 nowa `warScore` −50 → 5, 0 → 1, a `peaceOpinion` przy −100 daje 0,5 zamiast 0,3; po 4g nowe `greed`, `lootNeed`, `lootGain`, `conquestChance`, a `threat` zaczyna od 0,8 przy 0% i `allianceTrust` od 0,5 przy zaufaniu 0) |
| `treaties[].years` | pokój: 100 | lata | D3 | rozejm po wojnie |

## `data/war.json` (od etapu 4e)

| Klucz | Wartość | Jednostka | Decyzje | Znaczenie |
| --- | --- | --- | --- | --- |
| `goals[].victimPercent` | ziemia 100, łupy 100, odwet 50 | % | D3 | jak mocno ofiara pamięta agresję |
| `goals[].witnessPercent` | ziemia 50, łupy 100, odwet 0 | % | D3 | jak mocno pamiętają ją świadkowie |
| `scorePerWin` | 10 | punkty wyniku | D3, D1 | wygrana walka |
| `scorePerSettlement` | 15 | punkty wyniku | D3, D1 | zdobyta osada (ponad walkę) |
| `wearinessPerCampaignMonth` | 5 | punkty na miesiąc kampanii | D1, D2, K1 | zmęczenie z miesięcy kampanii (od PR 2; wcześniej `wearinessPerYear` 1) |
| `campaignStepMonths` | 1 | miesiące | — | krok kampanii (epizodu) |
| `marchCellsPerMonth` | 3 | pola na miesiąc | K1 | marsz kampanii |
| `quietPeaceYears` | 5 | lata | — | wojna bez kampanii kończy się białym pokojem |
| `armySightCells` | 1 | pola | K1, K2 | armia w drodze widzi osady w tej odległości (od PR 5); `minWinChancePercent` przeszło do `ai.json` → `commander` |
| `sendPercent` | 67 | % wojowników osady | K1 | ilu wojowników może wyjść (1/3 zostaje w domu) |
| `minWarriors` | 5 | wojownicy | K1 | najmniejsza kampania (cała armia) |
| `musterCells` | 6 | pola | K1 | z jak daleka od osady wyjścia dołączają wojownicy innych osad (od PR 3) |
| `armaments[]` | gołe ręce 50, włócznie hartowane w ogniu 70, proce 80, kamienne groty 100 | % siły | K1 | uzbrojenie (technologia, dobro, zużycie na wojownika); dowódca liczy z niego siłę obu stron (od PR 3) |
| `battle.*` | 6 rund, 10% siły zabija, szczęście 50–150%, −3 morale na % strat, ucieczka poniżej 40, obrońcy z morale 100, głodni 75%, ucieczka −10%; teren: wzgórza +25, góry +50, rzeka +25, lasy, dżungla i bagna +25 | — | K1 (teren w szansie) | bitwa w rundach (od PR 4); dowódca widzi teren celu w szansie (bez rzeki, bo nie wie, skąd przyjdzie) |
| `supply.*` | jedzenie 1 na wojownika na rok, zapas na 3 miesiące, tragarze poniżej 3 miesięcy (od PR 6; było 2), do 12 pól, 10% ludzi, 6 pól na miesiąc (od PR 6; było 4); głód −20 morale na miesiąc, najedzeni +10, odwrót poniżej 30, napad na wioskę do 4 pól | | K1, K2 | zaopatrzenie armii (od PR 3); od PR 6 tragarze idą z osady, która może dać najwięcej jedzenia, a armia przy zbiórce je z zapasów domu |
| `wearinessPerLossPercent` | 3 | punkty na % ludzi zabitych | D1, D2 | zmęczenie ze strat |
| `peaceAbove` | 60 | punkty zmęczenia | D2 | od tego zmęczenia strona szuka pokoju w każdym kroku i przy każdej ocenie |
| `moodAtFullWeariness` | 20 | punkty nastroju | — | spadek nastroju przy zmęczeniu 100 |
| `termsAboveScore` | 20 | punkty wyniku | D2, D1 | od tego wyniku zwycięzca żąda warunków |
| `ransomPercentPerScorePoint` | 0,5 | % zapasów na punkt | D2, D1 | wysokość okupu |
| `maxRansomPercent` | 40 | % zapasów | D2, D1 | największy okup |
| `cedeWorth` | 40 | punkty wyniku | D1, D2 | ile warte jest oddanie osady przy ocenie warunków |

`data/eras.json` (od 4e PR 3): `armyLimit` 50 (najwięcej wojowników w armii jednego wodza) i `troopsPerCell` 50 (wojownicy na polu; większa armia
stoi na kilku polach, front). Dowódca nie zbiera więcej niż `armyLimit`.

## `data/governments.json` (od etapu 4f)

Każdy ustrój ma te same pola; wartości w [opisie systemu](../systems/governments.md). Dziś działają rada starszych i wodzostwo,
despotia i republika miejska czekają na epokę brązu.

| Parametr | Rada starszych | Wodzostwo | Jednostka | Decyzje | Znaczenie |
| --- | ---: | ---: | --- | --- | --- |
| `enactYears` | 4 | 1 | lata | K1, D2 | po tylu latach armie ruszają na wypowiedzianą wojnę i wchodzi nowy ustrój; w tym czasie bez wypowiedzeń wojny |
| `reviewPercent` | 150 | 75 | % odstępu | S1, D2 | tempo przeglądów stratega i dyplomaty |
| `cautionShift` | +200 | 0 | ‰ ostrożności | wszystkie z wyborem | jak często AI bierze najlepszą opcję wprost |
| `goalPercent` | bezpieczeństwo 110 | ekspansja 120, podbój 120 | % wagi | S1 (i przez wagi wszystkie) | przechylenie wag celów |
| `leaning` | ostrożność 1, otwartość 1 | agresja 1, ekspansywność 1 | waga cechy | start gry, S2 | który ustrój lud dostaje na start i który wybiera strateg (od PR 5) |
| `stabilityBonus`, `administration`, `strataInfluence` | +10, 250, myśliciele i przywódcy x4 | 0, 200, wojownicy i przywódcy x4 | punkty, punkty, waga | S1, S2, D2 (od PR 5) | stabilność i limit administracji ([opis](../systems/stability.md)) |

## `data/stability.json` (od etapu 4f PR 3)

Od PR 5 AI czyta stabilność wprost: `unrestBelow` włącza reakcje S1, S2 i D2 (parametry w `ai.json` wyżej). Pozostałe działają pośrednio:

| Parametr | Wartość | Decyzje | Znaczenie |
| --- | --- | --- | --- |
| `anarchyYears`, `anarchyPenalty` | 2 lata, 20 | D2 | po zmianie ustroju 2 lata bez wypowiedzeń wojny i bez kolejnej zmiany |
| `unrestBelow`, `unrestOutputPercentPerPoint` | 30, 1% | S1, S2, D2; wszystkie przez zapasy | niepokoje obniżają wydajność osad, więc mniej jedzenia i dóbr do ocen |
| `revoltBelow`, `revoltPerMillePerYear`, `revoltCalmYears` (od PR 4) | 15, 20‰, 20 lat | — (bunt to reguła, nie decyzja AI) | bunt poniżej 15: 2% na rok, potem 20 lat spokoju |
| `secessionLoyaltyBelow`, `secessionMinPeople`, `maxCivilizations`, `secessionCharacterShift` (od PR 4) | 5, 200, 64, ±150‰ | wszystkie nowego ludu | kto się odłącza; nowy lud ma charakter rodzica ±0,15 na cechę i inny ustrój |
| pozostałe (`crisisPenalty` 15, `capitalLostPenalty` 15 na 5 lat, `adminPerSettlement` 10, `adminPerRoadCost` 1, `roadSearchCost` 60, `overloadPenaltyPer100Percent` 10, `overloadMaxPenalty` 25, `loyaltyPer10RoadCost` 5) | | — | skład stabilności i lojalności |

## `data/leaders.json` (od etapu 4f PR 2)

| Parametr | Wartość | Decyzje | Znaczenie |
| --- | --- | --- | --- |
| `traits[].shifts` | zdobywca: agresja +200; budowniczy: ekspansywność +200; uczony: ciekawość +200; kupiec: handlowość i otwartość +200; ostrożny: ostrożność +200; paranoik: otwartość −200, mściwość +200 | wszystkie przez charakter | przesunięcie cech na czas rządów |
| `traitCount` | 2–3 | wszystkie | ile cech ma przywódca |
| `deathByAge` | 20 lat 10‰, 40: 20‰, 50: 35‰, 60: 60‰, 70: 120‰, 80: 250‰, 90: 500‰ na rok | — | jak często zmienia się przywódca (i charakter) |
| `startAge`, `councilCandidates` | 25–45, 3 | — | wiek pierwszych przywódców; ilu kandydatów waży rada |
| `governments.json` sukcesja | rada: 40–60 lat, bez kryzysu; wódz: 22–40, kryzys zawsze 5 lat; król: 15–35, kryzys 30%; konsul: 35–55, kadencja 10 lat | — | następca i kryzys (stabilność od PR 3) |

## `data/skills.json` (od etapu 4F)

| Parametr | Wartość | Na co wpływa w AI |
| --- | --- | --- |
| `rules.farmingSkill` | `farmer` | G4: osiedlenie wymaga choć jednego rolnika; plon z pól liczony dla rolników |
| `skills[farmer].jobsPercent` | 50 | G4: ilu producentów wioska wyuczy na rolników (reszta dalej zbiera) |
| `jobsPercent` rzemiosł | 10–15 | T1 (skarbnik): receptura jest warta tyle, ilu ma fachowców (`Demand.Runs`) |
| `rules.apprenticeWorkPercent` | 50 | T1 i G4 pośrednio: uczeń liczy się za pół fachowca |

Pozostałe reguły (mistrzowie, uczniowie, przenoszenie) nie są decyzjami AI, tylko zasadami systemu fachów: `docs/systems/skills.md`.

## Inne pliki

| Plik i klucz | Wartość | Decyzje | Znaczenie |
| --- | --- | --- | --- |
| `settlements.json` `settle.minPeople` | 60 | G4 | najmniejszy obóz, który może osiąść |
| `settlements.json` poziomy | obóz: do 150 osób, magazyn 300, ruchomy; wioska: do 1000, 2000, wymaga rolnictwa | G4, G5, G6, S1 | pojemność i magazyn |
| `settlements.json` `carrying.perPerson` | 10 | G1, G2, G5, G6 | ile dóbr niesie osoba |
| `settlements.json` `abandonment.claimYears` | 20 (było 100) | R4 | jak długo trwa roszczenie do porzuconej osady, w latach akcji (2 min przy ×1) |
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
| 5 poziomów szczęścia na stronę (25 bitew) | `Battles.ExpectSteps` | K1, K2 | ile bitew dowódca rozgrywa w głowie |
| 0,5% oceny na 1% strat | `CommanderAdvisor.Weigh` | K1 | jak spodziewane straty obniżają ocenę kampanii |
| kolejność G1 → G2 → G3 → G4 → G5 → G6 | `Decide` | gubernator | priorytet decyzji obozu |
