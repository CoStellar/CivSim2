# Dowódca (K1, K2)

[← spis treści](README.md) · kod: `src/CivSim.Core/Ai/CommanderAdvisor.cs`, `src/CivSim.Core/Episodes/Campaigns.cs`,
`src/CivSim.Core/Episodes/Battles.cs` · dane: `data/ai.json` (sekcja `commander`), `data/war.json` · GDD 8.1, 9.3 · etap 4e PR 2–5 (ADR 0022).

Dowódca wysyła **kampanie**: armie zebrane z osady wyjścia i z własnych osad w pobliżu (od PR 3) przeciw osadzie wroga. Kampania to epizod z krokiem co miesiąc
([epizody](../systems/episodes.md)): zbiórka, marsz, bitwa u celu, powrót z łupami. Od PR 5 dowódca **rozgrywa bitwę w głowie** z tego, co widzi,
liczy drogę i jedzenie i **co miesiąc kampanii patrzy jeszcze raz**: może zawołać armię do domu (K2).

## Spodziewana bitwa (od PR 5)

`Battles.Expect` gra tę samą bitwę w rundach co prawdziwa (`Battles.Fight`), ale **bez losowania**: szczęście każdej strony jest stałe
przez całą bitwę, 5 poziomów od najgorszego do najlepszego z `battle.rollPercent` (50, 75, 100, 125, 150%), każda para raz, czyli 25 bitew;
ułamek człowieka jest zaokrąglany. Wynik:

- **szansa** = w ilu z 25 bitew wygrywają atakujący, w procentach (krok 4%);
- **spodziewane straty** = średnio ilu atakujących pada, w procentach armii.

Dane wejściowe to tylko to, co cywilizacja **widzi teraz**: cel widziany w ostatnim własnym kroku albo w zasięgu wzroku jej armii
(`armySightCells` 1 pole w `data/war.json`). Wtedy dowódca zna obrońców (wojownicy + 25% reszty), ich uzbrojenie z zapasów i teren pola celu
(wzgórza, góry, lasy, bagna, a przy planie także rzekę na krawędzi, przez którą przyjdzie atak). Swoją armię zna dokładnie: uzbrojenie,
morale i głód. Gdy celu nie widzi, nie zgaduje: wojownicy idą zobaczyć (w ocenie szansa 50%), a decyzję podejmie K2, gdy armia stanie przy celu.

Test `CommanderTests` sprawdza, że spodziewana szansa jest blisko tego, jak często atakujący wygrywają w 300 prawdziwych bitwach (±25 punktów).

## K1: Wysłanie kampanii

- **Kiedy:** we własnym kroku cywilizacji i **reaktywnie** w kroku świata zaraz po alarmie: wypowiedzieliśmy wojnę albo
  przystąpiliśmy do niej (nowy alarm „poszliśmy na wojnę”), wypowiedziano nam wojnę, napadnięto nasz obóz. Alarm daje krok świata
  w następnym miesiącu, więc dowódca reaguje po miesiącu, a nie po 50 latach.
- **Dla każdej wojny** naszej cywilizacji, w której nie mamy już własnej kampanii w drodze i nasze zmęczenie wojną jest poniżej 60
  (`peaceAbove`).
- **Opóźnienie ustroju** (etap 4f): w wojnie, którą wypowiedzieliśmy, armie ruszają dopiero od miesiąca gotowości
  (`Treaty.ReadyMonth` = wypowiedzenie + `enactYears` ustroju: rada starszych 4 lata, wodzostwo 1 rok). Zegar świata robi krok w tym
  miesiącu i dowódca wtedy planuje. Obrońca może wysłać armię od razu. Cicha wojna (5 lat) liczy się od gotowości.
- **Kandydaci:** każda nasza osada (osada wyjścia), której wojownicy nie są w drodze i może wysłać 2/3 swoich wojowników
  (`sendPercent`: 1/3 zostaje w domu, decyzja Patryka z 2026-11-07).
- **Zbiórka** (od PR 3): do armii dołączają 2/3 wojowników każdej naszej osady do 6 pól (`musterCells`) od osady wyjścia, której
  wojownicy nie są w drodze, najpierw najbliższe (remis: niższy numer osady), aż do limitu wodza ery (`armyLimit` 50 w erach 1–2).
  Cała armia musi mieć co najmniej 5 wojowników (`minWarriors`).
- **Cel:** osada, o którą jest wojna (gdy to my ją wypowiedzieliśmy o ziemię i znamy jej położenie), w innym razie najbliższa znana
  osada wroga w zasięgu wędrówki (40 pól).
- **Bitwa** (od PR 5): gdy cel widać, dowódca rozgrywa spodziewaną bitwę (wyżej) dla całej armii z morale 100. Odrzuca cel, gdy szansa
  jest poniżej 60% (`commander.minWinChancePercent`) albo spodziewane straty powyżej 50% armii (`commander.maxLossPercent`).
  Najpierw liczy bez rzeki (rzeka pomaga tylko obrońcom, więc jeśli już wtedy przegrywa, nie szuka drogi), potem z polem, z którego przyjdzie atak.
- **Droga:** musi istnieć droga po znanych polach (najkrótsza, do 40 pól).
- **Odpoczynek** (etap 6 PR 3b): osada, której wojownicy odpoczywają po kampanii (`restMonths`, 6 miesięcy akcji), nie jest osadą
  wyjścia ani nie dołącza do zbiórki.
- **Czas marszu** (etap 6 PR 3b): miesiące akcji drogi armii z A* (takty drogi jej rodzaju po terenie / takty miesiąca akcji); pola
  poza zasięgiem tragarzy i zbiórka po otwartym terenie (15 taktów na pole = 3 miesiące akcji). Wcześniej 3 pola na miesiąc.
- **Jedzenie i zaopatrzenie** (od PR 5): armia bierze jedzenie najwyżej na 36 miesięcy (`supply.carryFoodMonths`, od etapu 6 PR 3b; było 3), tyle, ile uniosą wojownicy, i najwyżej
  połowę zapasów osad (`supplySharePercent`). Tragarze dochodzą do 12 pól od naszej osady (`supply.carrierCells`). Dowódca liczy, ile pól
  celu leży **poza zasięgiem tragarzy** (odległość celu od najbliższej naszej osady minus 12), ile to miesięcy marszu (3 miesiące na pole od etapu 6 PR 3b),
  i wysyła armię tylko wtedy, gdy jedzenie wystarczy na te miesiące **tam i z powrotem** i jeszcze na 1 miesiąc (`commander.minFoodMonths`):
  `miesiące jedzenia ≥ 2 × miesiące poza zasięgiem + 1`. Przy zapasie na 36 miesięcy cel może leżeć najwyżej 5 pól poza zasięgiem tragarzy.
- **Jedzenie na całą wyprawę** (od PR 6): zapasy, które mogą dać nasze osady do 12 pól od osady wyjścia albo od celu (połowa ich jedzenia),
  muszą wystarczyć na zbiórkę, marsz tam, miesiąc u celu, marsz z powrotem i jeszcze 1 miesiąc (`minFoodMonths`). Wcześniej dowódca liczył
  tylko to, co wojownicy niosą, a tragarze nie mieli skąd brać jedzenia, więc armie głodowały w zasięgu własnych osad.
- **Ocena:** `1000 × szansa × (1 − 0,5% × spodziewane straty) × waga celu „podbój” × mnożnik charakteru (agresja)`; odległość, miesiące
  jedzenia i pola poza zaopatrzeniem tylko w zapisie „Dlaczego?”.
- **Wybór:** najlepsza ocena (jedna kampania na wojnę naraz).
- **Komenda:** `LaunchCampaignCommand` (działanie `LaunchCampaign`) z listą osad, które dołączają (`Others`). Serwer sprawdza wojnę,
  znajomość celu, drogę, 1/3 w domu w każdej osadzie, odległość osad od osady wyjścia i limit wodza.

## K2: Odwołanie kampanii (co miesiąc, od PR 5)

- **Kiedy:** w każdym kroku, także w krokach epizodów (co miesiąc kampanii), dla każdej naszej armii, która się zbiera albo maszeruje.
  Decyzja jest w fazie decyzji, a armia rusza w fazie walki, więc dowódca widzi stan po poprzednim miesiącu.
- **Bitwa:** gdy cel widać, dowódca rozgrywa spodziewaną bitwę z obecną armią (wojownicy, morale, głód; gdy armia stoi obok celu, także z rzeką
  od jej strony). Przy szansie poniżej 45% (`commander.recallChancePercent`) woła armię do domu. Próg jest niższy niż 60% przy wysłaniu,
  żeby armia nie zawracała od drobnej zmiany.
- **Jedzenie:** gdy armia nie napada właśnie wioski po jedzenie, dowódca liczy miesiące jedzenia, które ma (niesione, w łupach i u tragarzy w drodze),
  i miesiące marszu poza zasięgiem tragarzy: do celu i od celu z powrotem. Gdy jedzenia jest mniej, woła armię do domu, zanim zacznie głodować.
- **Komenda:** `RecallCampaignCommand` (działanie `RecallCampaign`): armia zawraca, ocaleni wracają do swoich osad. Serwer przyjmuje ją
  tylko od właściciela kampanii, która się zbiera albo maszeruje.
- **Atak:** armia, która doszła do pola obok celu, atakuje dopiero w następnym miesiącu. Ten miesiąc daje dowódcy czas, żeby zobaczyć cel i
  ewentualnie zawołać armię (wcześniej system sam zawracał armię przed silnym celem; od PR 5 to decyzja dowódcy, a gracz w trybie gracza
  zdecyduje sam).

## Co się dzieje po decyzji

- Wojownicy wychodzą od razu, biorą uzbrojenie (zużywają dobra) i jedzenie na 36 miesięcy; z dalszych osad najpierw idą do osady
  wyjścia jako oddziały (zbiórka trwa, aż wszyscy dojdą), potem armia idzie jako oddział najtańszą znaną drogą (etap 6 PR 3b,
  `docs/systems/units.md`).
- Gdy jedzenia zostaje na mniej niż 2 miesiące, najbliższa nasza osada z jedzeniem (do 12 pól) wysyła tragarzy; głodna armia traci
  morale, napada pobliską wioskę wroga po jedzenie, a przy morale poniżej 30 zawraca (to reguły systemu, nie decyzje dowódcy).
- U celu: armia staje, gdy cel jest w zasięgu jej rodzaju (wojownicy: pole obok), i miesiąc akcji później atakuje, chyba że dowódca ją
  odwoła (K2). Pobita armia ucieka i dowódca już jej nie zawraca; ocaleni odpoczywają w domu. Bitwa w rundach na strumieniu `combat` (PR 4, [war.md](../systems/war.md)).
- Wygrana: łupy (tyle, ile uniosą wojownicy), w wojnie o ziemię zdobycie wioski z ludźmi; wynik wojny +10 (+15 za osadę).
- Powrót: ocaleni i łupy do domu. Każdy miesiąc kampanii dodaje 5 punktów zmęczenia obu stronom wojny.

## Zapis „Dlaczego?”

- **K1:** `win chance` i `expected losses` (od PR 5) liczą się w ocenie; `distance`, `food months` i `cells beyond supply` (od PR 5) tylko w zapisie;
  waga celu i charakter. Cel wpisu: osada wyjścia (`settlement`) i osada celu (`target`).
- **K2:** `win chance` i `expected losses` (gdy cel widać), `food months`, a przy braku jedzenia `cells beyond supply`. Przykład z seedu 23:
  `RecallCampaign, win chance 20%, expected losses 9%, food months 0`.
