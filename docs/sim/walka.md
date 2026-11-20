# Walka (nowa gra, 2026-11-19)

Decyzje Patryka: `claude/walka-burza-mozgow.md` (kwestionariusz) i `claude/walka-propozycja.md` (propozycja i odpowiedzi z 12:01).
Wszystkie liczby są w `data/sim/game.json`, sekcja `combat` (budowana przez `data/sim/tools/build_game_json.py`).
Kod: `src/CivSim.Sim/State/SimGame.Combat.cs` (rdzeń), `SimGame.War.cs` (wojna, oblężenia, zdobycie),
`game/scripts/Play/PlayGame.Combat.cs` (tryb walki w kliencie).

## Krok walki

Co `stepTicks` = 5 taktów (0,5 s przy ×1) każdy oddział wojska uderza:

- **wręcz** (zasięg 1): każdego wroga na sąsiednim heksie (albo na tym samym); przy każdym walczy co najwyżej
  `frontPerContact` = 12 jego ludzi (gdy styka się z trzema wrogami, a ma 15 ludzi, przy każdym walczy 5);
- **dystans** (łucznicy, kusznicy, myśliwi): jednego wroga w zasięgu, **tylko gdy stoi** (w marszu nie strzela);
  strzelają wszyscy jego ludzie.

Najpierw liczone są wszystkie uderzenia kroku, potem straty schodzą naraz, więc kolejność oddziałów nie ma znaczenia.

```mermaid
flowchart LR
  A[krok co 0,5 s] --> B[kto kogo dotyka]
  B --> C[obrażenia każdego uderzenia]
  C --> D[suma na oddział]
  D --> E[straty: ludzie, ranni, wynik wojny]
  E --> F[rozkazy: pościg, straż, postawa agresywna]
```

## Wzór obrażeń

Obrażenia w setnych częściach człowieka za jeden krok:

```
walczący × siła × broń / 100
  × (100 + premie) / 100 × najedzenie / 100
  × 100 / zbroja
  × (100 + teren) / 100 × flanka / 100 × (szarża albo odejście) / 100
  × 100 / killDivisor          (killDivisor = 400)
  × szczęście 80–120 %
```

| Czynnik | Wartości (dane) |
| --- | --- |
| broń, poziom 0–4 (gołe ręce, kamień, brąz, żelazo, stal) | ×0,6 / 1,0 / 1,4 / 1,8 / 2,2 |
| zbroja, poziom 0–4 (brak, skóra, brąz, żelazo, płyty): dzieli obrażenia | 1,0 / 1,3 / 1,7 / 2,1 / 2,6 |
| zbroja przeciw strzałom | jej nadwyżka ponad 1,0 liczy się o 20 % mocniej (np. 1,7 → 1,84) |
| kusze (`pierce`) | omijają 1 poziom zbroi celu |
| premie rodzaju | włócznicy +50 % na jazdę i zatrzymują szarżę; jeźdźcy i rycerze +50 % na strzelców, +20 % na równinie; wojownicy +30 % w lesie |
| flanka: ilu wrogów wręcz dotyka celu | 1 → ×1,0; 2 → ×1,25; 3 → ×1,5; 4 i więcej → ×1,8 |
| rzeźba | +8 % za każdy poziom wyżej od celu (równina 0, wzgórza 1, góry 2), najwyżej ±16 % |
| wysokość | +3 % za każde 20 jednostek wysokości ponad celem, najwyżej ±9 % |
| osłona heksu celu | las lub zagajnik: −30 % od strzał, −15 % wręcz; własna palisada: −50 % / −30 %; mur: −50 % / −30 % |
| utrata osłony | gdy cel dotykają wrogowie z dwóch stron, które się nie stykają |
| rzeka bez mostu (wręcz) | −25 % |
| otwarty teren (droga, bagno) | cel dostaje +15 % |
| głód | najedzony 100 %, głodny 75 %, umierający z głodu 50 % (sekcja `rations`) |

Siła rodzajów po zmianie (broń i zbroja dają resztę): mieszkańcy 5, myśliwi 6, wojownicy 9, łucznicy 9, włócznicy 11,
jeźdźcy 12, kusznicy 12, miecznicy 13, rycerze 16. Technologie z premiami „strength” działają dalej (wyszkolenie).

### Przykład

12 wojowników z bronią z kamienia (×1,0), bez technologii z premią, uderza 12 wojowników w skórzanych zbrojach (1,3), na równym polu, jeden na jednego:
12 × 9 × 100/100 = 108 → × 100/130 = 83 → × 100/400 = 20,8 setnych, czyli około 0,21 człowieka na krok i 0,42 na sekundę.
Ponieważ obie strony tracą ludzi naraz (a z nimi siłę), oddział 12 ludzi pada po około 30–40 s walki jeden na jednego. Przy trzech wrogach wokół (flanka ×1,5, każdy wróg walczy pełną dwunastką) pada po około 7–10 s. Bitwa 7 na 7 oddziałów po 15 ludzi w scenariuszu z AI trwała około 50 s.

## Broń i zbroja jako towary

Dziesięć towarów zamiast dawnego `arms`: `stone_weapons`, `bronze_weapons`, `iron_weapons`, `steel_weapons`, `hide_armour`,
`bronze_armour`, `iron_armour`, `steel_armour`. Są oznaczone `storedIn: "armoury"`: budynki z grupy `armoury` (koszary, płatnerz,
kowal broni, huta stali, zamek) mieszczą ich dwa razy więcej, czyli w swoim magazynie zajmują połowę miejsca.

| Kto robi | Co |
| --- | --- |
| narzędziarz (kamień) | 1 narzędzia i 0,5 broni z kamienia |
| krawiec | 1 odzież i 0,3 zbroi skórzanej |
| płatnerz (brąz) | 1 broń z brązu i 0,5 zbroi z brązu |
| kowal broni (żelazo) | 1,5 broni z żelaza i 1 zbroja z żelaza |
| huta stali | 4 narzędzia, 1 broń ze stali i 1 zbroja płytowa |

Przy szkoleniu oddział bierze najlepszy poziom, którego starczy dla **wszystkich** jego ludzi (1 sztuka na człowieka).
„Tylko broń” (`Shock`) nie bierze zbroi i szkoli się w 40 % czasu. **Uzbrojenie** (`Refit`) na własnej ziemi oddaje stare
do magazynu i bierze najlepsze; nigdy nie daje gorszego.

## Wielkość oddziału i dzielenie

Piechota i jazda mają `minPeople` 5 i `maxPeople` 30. Cena i czas szkolenia rosną z wielkością (20 wojowników kosztuje 60 jedzenia
zamiast 30). **Dzielenie** (`Split`) robi z części nowy oddział tego samego rodzaju i uzbrojenia na wolnym heksie obok; obie części
muszą mieć co najmniej `minPeople`. **Jeden oddział wojska na heks**: oddział, który staje tam, gdzie stoi inny, schodzi na wolny heks
obok (przejść można).

## Ruch w walce

- **Strefa kontroli:** oddział idący obok wroga staje; dalej idzie po jednym heksie.
- **Odejście:** każdy wróg wręcz, od którego oddział odchodzi, uderza raz za `disengagePercent` = 50 % kroku (jazda
  `cavalryDisengagePercent` = 25 %). Nie uderza, gdy przy tym wrogu zostaje inny nasz oddział wręcz (dlatego oddzielona część
  odchodzi bez straty, gdy reszta walczy) i gdy wróg jest bierny.
- **Szarża:** jazda, która przeszła co najmniej `chargeHexes` = 2 heksy od ostatniego kontaktu, w pierwszym kroku uderza za
  `chargePercent` = 200 %. Na włóczników nie działa. Jazda może odskoczyć (traci tylko ćwierć kroku) i wrócić z nową szarżą.

## Postawy, straż, szyk

| Postawa | Co robi |
| --- | --- |
| Trzymaj (domyślna) | uderza to, co dotyka (strzelcy: co w zasięgu), nie goni |
| Agresywna | idzie na wrogów, których widzi w swoim zasięgu wzroku |
| Bierna | uderza tylko wroga, na którego go wysłano (do przejścia obok albo wycofania) |

**Straż:** oddział z namalowanymi heksami idzie na wroga, który na nich stanie, a potem wraca na pierwszy z nich.
**Szyk** (`Formation`): wręcz w linii na wskazanym heksie w poprzek kierunku marszu, jazda na końcach linii, strzelcy rząd za nią,
machiny oblężnicze dwa rzędy za nią. Linia poprzeczna na heksach to zygzak: krok 60° w bok i krok 120° na zmianę.

## Czas

Gra jednoosobowa: pierwsze starcie po `calmSeconds` = 30 s spokoju zwalnia czas do ×1 (w kronice: „Battle! Time runs at ×1.”).
Oblężenia liczą się w tym samym kroku: dawna sekundowa siła oblężenia jest mnożona przez 0,5.

## Sterowanie w kliencie (tryb walki, klawisz F)

| Wejście | Co robi |
| --- | --- |
| lewy klik | wybiera nasz oddział wojska na heksie |
| przeciągnięcie lewym | ramka: wszystkie nasze oddziały wojska w środku (z Shiftem dokłada) |
| Ctrl + klik | oddział i wszystkie oddziały tego rodzaju stojące obok siebie w łańcuchu |
| Shift + przeciągnięcie | maluje heksy straży wybranych oddziałów |
| prawy klik z kilkoma oddziałami | szyk na wskazany heks (na wroga: atak każdego) |
| Z / X / C | postawa: trzymaj / agresywna / bierna |
| V / B / N | podziel na pół / uzbrój w osadzie / koniec straży |

W panelu osady nad kafelkami szkolenia jest suwak wielkości (lewy koniec: zwykła wielkość rodzaju) i pole „tylko broń”.
W panelu oddziału jest sekcja „Battle”: broń i zbroja z mnożnikami, straty w ostatnim pół sekundy, przyciski postaw,
dzielenia, uzbrojenia i straży.

## Co widać w bitwie (paczka 2)

Bez przewidywania wyniku, tylko to, co się dzieje:

- **pasek ludzi** nad żetonem każdego oddziału wojska: zielony powyżej połowy wielkości, żółty do ćwierci, czerwony poniżej;
  ranni na szaro (wielkość to liczba ludzi przy szkoleniu albo podziale);
- **straty** wyskakują nad oddziałem („-2”) i znikają po 1,4 s; „Charge!” przy szarży;
- **strzałki** od oddziału do tego, kogo uderzył w ostatnim kroku: żółte nasze, pomarańczowe wroga, czerwone i grubsze,
  gdy cel biją dwa albo więcej oddziałów (flanka); strzały przerywaną linią;
- **heks pod kursorem w trybie walki**: obwódka zielona (dobra osłona albo wyżej od najbliższego wroga), żółta (trochę),
  czerwona (otwarty teren albo niżej) i napisy z liczbami: osłona od strzał i wręcz, otwarty teren, wysokość wobec
  najbliższego widocznego wroga (strzałka w górę lub w dół);
- **kronika**: „Our warriors are down to 6 of 12” przy połowie i ćwierci (`warnPercents`).

## Scenariusze testowe

`06_battle_field` (pole, równe uzbrojenie), `07_battle_arms` (uzbrojenie, wielkość, dzielenie), `08_battle_cavalry`
(szarża, odskok jazdy, łucznicy, wysokość), `09_battle_ai` (wszystko przeciw atakującej AI). Opis pól: `data/sim/scenarios/README.md`.
Zrzut z ekranu: `--play --play-scenario 06_battle_field --play-window Battle --play-screenshot x.png --play-seconds 9`.

## Testy

`tests/CivSim.Sim.Tests/CombatTests.cs`: uzbrojenie z magazynu, wielkość i cena, połowa miejsca w zbrojowni, broń i zbroja
w obrażeniach, flanka, wysokość i las, strefa kontroli, odejście (½, jazda ¼, nic przy osłonie), szarża, łucznicy tylko stojąc,
dzielenie, szyk.

## Oblężenie i garnizon (decyzje Patryka z 2026-11-20)

Kod: `src/CivSim.Sim/State/SimGame.Siege.cs`, `Siege` w `SimGame.War.cs`. Dane: sekcja `siege`.

| Etap | Co się dzieje | Dane |
| --- | --- | --- |
| Otoczenie | Oddział wojska wroga (w stanie wojny) w promieniu 2 od środka osady. Osada pracuje na 50%, nie szkoli, nie zaczyna budów, karawany nie wyjeżdżają ani nie przyjeżdżają. Kończy się 30 s po odejściu ostatniego takiego oddziału | `blockadeRange` 2, `blockadeWorkPercent` 50, `liftSeconds` 30 |
| Głód | Osada pracuje słabiej i nie dostaje dostaw, więc z czasem głoduje (zwykłe racje) | `rations` |
| Szturm | Oddział z celem na osadzie uderza jak dotąd (machiny mocniej). Ciosy idą najpierw w straż, potem w walczących mieszkańców | `combat`, `war` |
| Upadek | Brak straży, brak walczących mieszkańców i żadnego oddziału obrońcy na ziemi osady: osada pada. Zdobywca ma 20 s na wybór, potem osada jest zajęta | `choiceSeconds` 20 |

**Kto broni:**
- **Oddziały obrońcy** stojące na ziemi swojej osady: dostają obrażenia podzielone przez obronę osady (np. przy 220% z zamkiem ponad 2 razy mniej).
- **Straż**: stała załoga z umocnień. 0,5 człowieka za heks palisady i 1 za heks muru na ziemi osady (`guardPerPalisadeHexPercent` 50,
  `guardPerWallHexPercent` 100) plus straż budynków (zamek 40, pole `guard`). Siła 8 na człowieka (`guardStrength`). Odrasta 3 na minutę
  (`guardRegrowPerMinute`), gdy nie ma oblężenia, i nic nie kosztuje.
- **Mieszkańcy**: walczy 30% mieszkańców z chwili otoczenia (`war.defenderPercent`), siła 6 (`war.defenderStrength`).
  Tylko oni giną w obronie, a walka nie zbija mieszkańców poniżej 40% tej liczby (`residentsFloorPercent`). Reszta jest bezbronna.

Straż i walczący mieszkańcy oddają ciosy razy obrona osady w procentach.

**Wybór po zdobyciu** (komenda `TakeSettlementCommand`, w kliencie trzy przyciski w opisie osady):

| Wybór | Skutek |
| --- | --- |
| Zajmij | Osada przechodzi do zdobywcy z ludźmi i budynkami (straż starego właściciela znika) |
| Złup | 50% każdego towaru trafia do najbliższej osady zdobywcy. Osada zostaje u właściciela i przez 180 s nikt jej nie oblega (`sackPercent`, `sackImmuneSeconds`) |
| Zburz | Osada staje się ruiną (G2). Mieszkańcy uciekają do najbliższej osady swojego ludu; gdy to była ostatnia, lud ginie |

AI wybiera: zajmij, gdy osada leży do 20 heksów od jego najbliższej osady; złup, gdy dalej; zburz tylko ostatnią osadę wroga
i tylko przy agresji 75 lub więcej.

**Oddziałów wojska nie da się rozwiązać** (decyzja 10): wojownik zostaje wojownikiem do końca.

## Zaopatrzenie (decyzja Patryka z 2026-11-20, pytanie 1: D)

Kod: `SimGame.Rations.cs` (`InSupply`, `ProvisionsMax`, `LandFood`), `SupplyUnit` w `SimGame.Logistics.cs`. Dane: sekcja `supply`.

| Gdzie stoi oddział wojska | Skąd je |
| --- | --- |
| Na ziemi swojego ludu albo do 3 heksów od niej (`rangeHexes`) | Z racji osady jak dotąd, a do tego osada napełnia mu juki do pełna |
| Dalej (w polu) | Najpierw ziemia: tyle, ile zebrałoby 10 zbieraczy na tym heksie (`landHands` 10, `forage.perMinute` jedzenia przy bogactwie heksa, razy `landPercent` 100%). Potem juki. Bez jedzenia głoduje (75%, potem 50% siły i straty), osada go nie karmi |

- **Juki:** jedzenie na `carryMinutes` = 4 minuty dla wszystkich ludzi oddziału (np. 30 wojowników: 120 jedzenia). Nowy oddział wychodzi z pełnymi jukami z osady (o ile ma jedzenie). Przy dzieleniu oddziału juki dzielą się po równo na ludzi.
- **Ziemia:** mały oddział żyje z ziemi dłużej. 10 wojowników na bogatym heksie dostaje ok. połowę racji, a 30 tylko ok. 1/6.
- **Dostawa:** komenda `SupplyUnitCommand` (w kliencie przycisk „Send N food from …” w opisie oddziału w polu). Karawana z jedzeniem idzie za oddziałem, a jeśli on się przesunął, idzie dalej (najwyżej 6 razy). Napad na nią zabiera jedzenie.
- **AI:** oddział w polu z jedzeniem na mniej niż 1,5 minuty dostaje karawanę z domu, jeśli dom ma ponad 2 razy tyle jedzenia i 40 zapasu. Głodny oddział bez dostawy wraca do domu.

## Limit oddziałów i doświadczenie rodzajów (decyzje Patryka z 2026-11-20, pytania 9 i 11)

Kod: `src/CivSim.Sim/State/SimGame.Veterans.cs`. Dane: `armyLimit`, `veterans`.

**Limit oddziałów wojska** (liczba oddziałów, nie ludzi; pospolite ruszenie się nie liczy):

| Źródło | Ile |
| --- | --- |
| Każda osada według poziomu: obóz, wioska, miasteczko, miasto | 2, 3, 5, 8 (`perLevel`) |
| Każde koszary, każdy zamek | +2 (`perBuilding`) |
| Code of laws, Civil service, Feudalism | +2, +3, +3 (`perTech`) |

Przy limicie szkolenie i dzielenie oddziałów są odrzucane. Dużą armię da się więc mieć, ale trzeba na nią rozbudować państwo,
a jedzenie i tak kosztuje przy każdej racji.

**Doświadczenie należy do rodzaju jednostki, nie do oddziału.** Każdy wróg zabity przez oddział danego rodzaju (w polu i przy oblężeniu)
dodaje 1 punkt temu rodzajowi. Progi poziomów: 20, 60, 140 i 300 zabitych (`levels`). Na każdym poziomie lud wybiera jedną z 3 premii
dla wszystkich oddziałów tego rodzaju, obecnych i przyszłych. Premie się sumują.

| Premia | Działanie |
| --- | --- |
| Hardened | +10% obrażeń |
| Shield wall | 10% mniej otrzymanych obrażeń |
| Forced march | +10% szybkości marszu |

Gracz wybiera w opisie oddziału (przyciski pojawiają się po nowym poziomie). AI wybiera od razu:
- łucznicy i machiny: Hardened;
- jazda: na zmianę Forced march i Hardened;
- piechota: na zmianę Hardened i Shield wall.

## Czego jeszcze nie ma (kolejne paczki)

AI walki (formacje, flanki, odskoki jazdy).
