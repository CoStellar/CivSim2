# Dowódca (K1)

[← spis treści](README.md) · kod: `src/CivSim.Core/Ai/CommanderAdvisor.cs`, `src/CivSim.Core/Episodes/Campaigns.cs` · dane: `data/war.json` ·
GDD 8.1, 9.3 · etap 4e PR 2 (ADR 0022). Pierwsza, prosta wersja: pełny dowódca (decyzje co miesiąc kampanii, armie z wielu osad,
spodziewany wynik bitwy w rundach) przyjdzie w PR 5.

Dowódca wysyła **kampanie**: wyprawy wojowników jednej osady przeciw osadzie wroga. Kampania to epizod z krokiem co miesiąc
([epizody](../systems/episodes.md)): marsz, walka u celu, powrót z łupami.

## K1: Wysłanie kampanii

- **Kiedy:** we własnym kroku cywilizacji i **reaktywnie** w kroku świata zaraz po alarmie: wypowiedzieliśmy wojnę albo
  przystąpiliśmy do niej (nowy alarm „poszliśmy na wojnę”), wypowiedziano nam wojnę, napadnięto nasz obóz. Alarm daje krok świata
  w następnym miesiącu, więc dowódca reaguje po miesiącu, a nie po 50 latach.
- **Dla każdej wojny** naszej cywilizacji, w której nie mamy już własnej kampanii w drodze i nasze zmęczenie wojną jest poniżej 60
  (`peaceAbove`).
- **Kandydaci:** każda nasza osada, której wojownicy nie są w drodze i która może wysłać co najmniej 5 wojowników
  (`minWarriors`) przy 2/3 jej wojowników (`sendPercent`: 1/3 zostaje w domu, decyzja Patryka z 2026-11-07).
- **Cel:** osada, o którą jest wojna (gdy to my ją wypowiedzieliśmy o ziemię i znamy jej położenie), w innym razie najbliższa znana
  osada wroga w zasięgu wędrówki (40 pól).
- **Szansa** (`Campaigns.WinChancePercent`): gdy cel widzieliśmy w ostatnim własnym kroku, to nasz udział w sile
  (`wojownicy / (wojownicy + obrona)`, obrona = ich wojownicy + 25% reszty mieszkańców). Odrzucamy cel z szansą poniżej 60%
  (`minWinChancePercent`). Gdy go nie widzimy, nikt nie wie: wojownicy idą zobaczyć (w ocenie szansa 50%). Na miejscu atakują tylko
  przy prawdziwej szansie co najmniej 60%, w innym razie zawracają (decyzja Patryka: dowódca nie idzie na pewną porażkę).
- **Ocena:** `1000 × szansa × waga celu „podbój” × mnożnik charakteru (agresja)`, odległość tylko w zapisie „Dlaczego?”.
- **Wybór:** najlepsza ocena (jedna kampania na wojnę naraz).
- **Komenda:** `LaunchCampaignCommand` (działanie `LaunchCampaign`). Serwer sprawdza wojnę, znajomość celu, drogę, 1/3 w domu.

## Co się dzieje po decyzji

- Wojownicy wychodzą od razu, marsz 3 pola na miesiąc (`marchCellsPerMonth`) po znanych polach.
- U celu: atak przy szansie ≥ 60%, inaczej odwrót. Walka jak napaść (losowanie udziału siły na strumieniu `combat`).
- Wygrana: łupy (tyle, ile uniosą wojownicy), w wojnie o ziemię zdobycie wioski z ludźmi; wynik wojny +10 (+15 za osadę).
- Powrót: ocaleni i łupy do domu. Każdy miesiąc kampanii dodaje 5 punktów zmęczenia obu stronom wojny.

## Zapis „Dlaczego?”

Czynniki: `win chance`, `distance` (tylko zapis), waga celu i charakter. Cel wpisu: osada wyjścia (`settlement`) i osada celu (`target`).
