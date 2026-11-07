# Dowódca (K1)

[← spis treści](README.md) · kod: `src/CivSim.Core/Ai/CommanderAdvisor.cs`, `src/CivSim.Core/Episodes/Campaigns.cs` · dane: `data/war.json` ·
GDD 8.1, 9.3 · etap 4e PR 2–3 (ADR 0022). Prosta wersja: pełny dowódca (decyzje co miesiąc kampanii, spodziewany wynik bitwy
w rundach) przyjdzie w PR 5.

Dowódca wysyła **kampanie**: armie zebrane z osady wyjścia i z własnych osad w pobliżu (od PR 3) przeciw osadzie wroga. Kampania to epizod z krokiem co miesiąc
([epizody](../systems/episodes.md)): marsz, walka u celu, powrót z łupami.

## K1: Wysłanie kampanii

- **Kiedy:** we własnym kroku cywilizacji i **reaktywnie** w kroku świata zaraz po alarmie: wypowiedzieliśmy wojnę albo
  przystąpiliśmy do niej (nowy alarm „poszliśmy na wojnę”), wypowiedziano nam wojnę, napadnięto nasz obóz. Alarm daje krok świata
  w następnym miesiącu, więc dowódca reaguje po miesiącu, a nie po 50 latach.
- **Dla każdej wojny** naszej cywilizacji, w której nie mamy już własnej kampanii w drodze i nasze zmęczenie wojną jest poniżej 60
  (`peaceAbove`).
- **Kandydaci:** każda nasza osada (osada wyjścia), której wojownicy nie są w drodze i może wysłać 2/3 swoich wojowników
  (`sendPercent`: 1/3 zostaje w domu, decyzja Patryka z 2026-11-07).
- **Zbiórka** (od PR 3): do armii dołączają 2/3 wojowników każdej naszej osady do 6 pól (`musterCells`) od osady wyjścia, której
  wojownicy nie są w drodze, najpierw najbliższe (remis: niższy numer osady), aż do limitu wodza ery (`armyLimit` 50 w erach 1–2).
  Cała armia musi mieć co najmniej 5 wojowników (`minWarriors`).
- **Jedzenie** (od PR 3): osady armii mogą dać połowę swojego jedzenia (`supplySharePercent`). Gdy to mniej niż na 1 miesiąc
  (`minFoodMonths`), dowódca nie wysyła armii.
- **Cel:** osada, o którą jest wojna (gdy to my ją wypowiedzieliśmy o ziemię i znamy jej położenie), w innym razie najbliższa znana
  osada wroga w zasięgu wędrówki (40 pól).
- **Szansa** (`Campaigns.WinChancePercent`): gdy cel widzieliśmy w ostatnim własnym kroku, to nasz udział w sile uzbrojonej
  (od PR 3: każdy wojownik z najlepszym uzbrojeniem, na jakie stać jego osadę, przeciw obronie: ich wojownicy z uzbrojeniem z ich
  zapasów + 25% reszty mieszkańców z gołymi rękami; od PR 4 obrona z premią terenu pola celu). Na miejscu wojownicy liczą szansę jeszcze
  raz, z terenem i rzeką od strony, z której przyszli, i z głodem; przy szansie od 60% wchodzą do bitwy w rundach. Odrzucamy cel z szansą poniżej 60%
  (`minWinChancePercent`). Gdy go nie widzimy, nikt nie wie: wojownicy idą zobaczyć (w ocenie szansa 50%). Na miejscu atakują tylko
  przy prawdziwej szansie co najmniej 60%, w innym razie zawracają (decyzja Patryka: dowódca nie idzie na pewną porażkę).
- **Ocena:** `1000 × szansa × waga celu „podbój” × mnożnik charakteru (agresja)`, odległość tylko w zapisie „Dlaczego?”.
- **Wybór:** najlepsza ocena (jedna kampania na wojnę naraz).
- **Komenda:** `LaunchCampaignCommand` (działanie `LaunchCampaign`) z listą osad, które dołączają (`Others`). Serwer sprawdza wojnę,
  znajomość celu, drogę, 1/3 w domu w każdej osadzie, odległość osad od osady wyjścia i limit wodza.

## Co się dzieje po decyzji

- Wojownicy wychodzą od razu, biorą uzbrojenie (zużywają dobra) i jedzenie na 3 miesiące; z dalszych osad najpierw idą do osady
  wyjścia (zbiórka trwa tyle miesięcy, ile marszu ma najdalsza), potem marsz 3 pola na miesiąc (`marchCellsPerMonth`) po znanych polach.
- Gdy jedzenia zostaje na mniej niż 2 miesiące, najbliższa nasza osada z jedzeniem (do 12 pól) wysyła tragarzy; głodna armia traci
  morale, napada pobliską wioskę wroga po jedzenie, a przy morale poniżej 30 zawraca (to reguły systemu, nie decyzje dowódcy).
- U celu: atak przy szansie ≥ 60%, inaczej odwrót. Walka jak napaść (losowanie udziału siły na strumieniu `combat`).
- Wygrana: łupy (tyle, ile uniosą wojownicy), w wojnie o ziemię zdobycie wioski z ludźmi; wynik wojny +10 (+15 za osadę).
- Powrót: ocaleni i łupy do domu. Każdy miesiąc kampanii dodaje 5 punktów zmęczenia obu stronom wojny.

## Zapis „Dlaczego?”

Czynniki: `win chance`, `distance` i `food months` (tylko zapis), waga celu i charakter. Cel wpisu: osada wyjścia (`settlement`) i osada celu (`target`).
