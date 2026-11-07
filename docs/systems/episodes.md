# Epizody (krótki zegar)

**W jednym zdaniu:** zdarzenia z przebiegiem, które w rzeczywistości trwają tygodnie albo miesiące (dziś kampanie wojenne), liczą się we własnych krokach co miesiąc, a reszta świata zostaje na długich krokach er (ADR 0022, GDD 6.2).

**Faza tury:** zegar w fazie zapisu (`CalendarSystem`, `WorldClock.Begin` i `WorldClock.Shorten`); kampanie w fazie walki (`CampaignSystem`, faza 6); decyzje dowódcy i dyplomaty w fazie decyzji.

**Stan:** etap 4e PR 2: silnik epizodów i pierwsza kampania; PR 3: armie z wielu osad, uzbrojenie, tragarze, głód, armie w podglądzie; PR 4: bitwa w rundach i podbój.

## Dane wejściowe i wyjściowe
- Czyta: `data/war.json` (krok kampanii, marsz, ciche wygasanie wojny, progi dowódcy), kroki cywilizacji, wojny, alarmy.
- Zmienia: `GameState.Episodes` (lista epizodów), `GameState.StepMonths` (długość kroku świata) i `GameState.WorldMonths` (ile miesięcy liczy sam świat), wojowników i zapasy osad, wynik i zmęczenie wojny.
- Publikuje: kronika (wyprawa wyszła, najazd, zdobycie, odwrót, głód, napad po jedzenie, powrót); Headless `--wars` (kampanie, miesiące kampanii) i `--campaigns` (jeden wiersz na kampanię, PR 3); migawka dla podglądu: `ObserverDetailsMessage.Armies` (`ArmyView`: pole, cel, wojownicy, front, uzbrojenie, morale, jedzenie, faza, tragarze; protokół 9).

## Przepływ

```mermaid
flowchart TD
  B["Początek kroku świata<br/>WorldClock.Begin"] --> P{"Czy jakaś cywilizacja<br/>ma krok?"}
  P -- tak --> F["Krok pełny: świat liczy klimat, odrastanie,<br/>strefy za miesiące do następnego kroku cywilizacji<br/>(WorldMonths)"]
  P -- nie --> E["Krok epizodów: świat czeka<br/>(WorldMonths = 0)"]
  F --> D["Decyzje: cywilizacje w kroku + alarmy<br/>dowódca wysyła kampanię, dyplomata myśli o pokoju"]
  E --> D
  D --> C["Walka: kampanie, których krok przypada teraz<br/>jedzenie, zbiórka, marsz 3 pola, walka u celu,<br/>tragarze, powrót"]
  C --> S["Shorten: kampania, alarm albo cicha wojna<br/>skracają krok świata"]
  S --> N["Następny krok świata = najbliższy krok<br/>cywilizacji, epizodu, alarmu albo końca cichej wojny"]
```

## Zasady i wzory
- **Krok świata** trwa do najbliższego z: kroku jakiejś cywilizacji (rytm ery, ADR 0018), kroku epizodu (kampania: co miesiąc), reakcji na alarm (następny miesiąc), końca cichej wojny (5 lat bez kampanii).
- **Krok pełny** (ktoś ma krok ery): świat liczy klimat, pory roku, odrastanie dzikiej żywności, odpoczynek ziemi, strefy i spory za `WorldMonths` = miesiące do następnego kroku cywilizacji. Kroki epizodów leżą w środku tego okresu i nie liczą go drugi raz.
- **Krok epizodów:** żadna cywilizacja nie ma kroku; systemy świata nie liczą nic, liczą się tylko kampanie, odpowiedzi na propozycje i decyzje po alarmach.
- **Skrócenie kroku** (`WorldClock.Shorten`, przed upływem czasu): gdy w kroku wysłano kampanię, wypowiedziano wojnę (alarm) albo cicha wojna kończy się wcześniej, krok świata kończy się wcześniej. Kroki cywilizacji (gospodarka liczona na cały ich krok) się nie zmieniają.
- **Kampania** (`LaunchCampaignCommand`, `CampaignSystem`):
  - wychodzą wojownicy osady wyjścia i osad do 6 pól od niej (PR 3): z każdej najwyżej 2/3 (1/3 zostaje w domu), razem co najmniej 5 i najwyżej limit wodza ery (50);
  - zbiórka (faza `Muster`): tyle miesięcy, ile najdalsza osada idzie do osady wyjścia; potem marsz;
  - każdy krok: armia je (najpierw niesione jedzenie, potem jedzenie z łupów); głodna traci morale, napada pobliską wioskę wroga, przy morale < 30 zawraca; tragarze idą za nią (szczegóły: `docs/systems/war.md`);
  - marsz: 3 pola na miesiąc po znanych polach (ok. 150 km na pole; ok. 15 km dziennie);
  - u celu wojownicy widzą osadę: atakują tylko przy szansie co najmniej 60%, inaczej wracają;
  - walka (od PR 4): bitwa w rundach na strumieniu `combat` (`Battles.Fight`, szczegóły w `docs/systems/war.md`): siła z uzbrojeniem, morale, głodem i terenem obrońców, straty co rundę, ucieczka przy niskim morale;
  - wygrana: łupy (tyle, ile uniosą wojownicy, ten sam udział każdego zapasu); w wojnie o ziemię zdobyta wioska przechodzi z ludźmi do zwycięzcy, a pokonani mają do niej roszczenie; ludzie pobitego obozu uciekają do swojej najbliższej osady;
  - powrót: ocaleni wracają do osad, z których przyszli (w proporcji), łupy i resztka jedzenia do osady wyjścia (albo do najbliższej własnej, gdy domu już nie ma).
- **Wojna** (`WarSystem`): wojna bez kampanii przez 5 lat kończy się białym pokojem; zmęczenie = miesiące kampanii × 5 + procent zabitych × 3.

## Pliki danych
- `data/war.json`: `campaignStepMonths` 1, `marchCellsPerMonth` 3, `quietPeaceYears` 5, `wearinessPerCampaignMonth` 5, `minWinChancePercent` 60, `sendPercent` 67, `minWarriors` 5 (ostatnie trzy przejdą do `data/ai.json` z dowódcą w PR 5).

## Testy
- `ArmyTests` (PR 3): patrz `docs/systems/war.md`.
- `EpisodeTests`: kampania dodaje kroki co miesiąc, a świat czeka; cicha wojna kończy się po 5 latach, a nie w następnym długim kroku; kampania zawraca przed zbyt silnym celem; kampania wymaga wojny i zostawia 1/3 w domu; zapis w trakcie marszu i dalsza gra dają ten sam hash.
- `DeterminismTests`, `WorldGeneratorTests`: nowe wartości kontrolne hashy.

## Wydajność
- Krok epizodów kosztuje ułamek milisekundy (świat nie liczy nic). Pomiar całej gry: w opisie PR.
