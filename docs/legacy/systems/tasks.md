# Zadania na obszarze

**W jednym zdaniu:** osada wysyła grupę roboczą (oddział) na pole w swojej okolicy: wycinka, kopalnia krzemienia albo kamieniołom;
grupa idzie tam, zostaje i pracuje, a urobek trafia do magazynu osady w pulsie jej cywilizacji (etap 6 PR 4a, GDD 4.4 „Zadania na
obszarze”, decyzje Patryka z 2026-11-12, ADR 0026 i 0027).

**Faza tury:** rozkaz `StartTaskCommand` w fazie decyzji (kategoria ekonomia); marsz grupy co takt w fazie 5. Ruch (`UnitSystem`,
rozkaz `Work`); urobek, jedzenie grupy i wyczerpanie złoża w pulsie cywilizacji, w fazie gospodarki zaraz po zbieraniu ze strefy
(`TaskSystem`). Powrót grupy to zwykły rozkaz „wróć” (`ReturnUnitCommand`).

## Dane wejściowe i wyjściowe
- Czyta: `data/tasks.json` (rodzaje zadań, zasięg, limity), rola `workers` w `data/units.json` (prędkość grupy), wartości `gather`
  biomów (`biomes.json`), złoża z `resources.json` (rodzaj, głębokość, zasobność), pamięć mapy cywilizacji, strefę wpływów osady
  (`WorldMap.Claim`), właściciela pól, technologie cywilizacji, fachy osady.
- Zmienia: `GameState.Units` (nowa grupa robocza z polem `Task`), ludzi i fachowców osady (wyjście i powrót), magazyn osady (urobek,
  jedzenie grupy), zasobność złoża (`DepositRichness`, wyczerpane złoże znika), kronikę.
- Publikuje: `GameSnapshot.Units` i `UnitView` z rolą 4 (grupa robocza), rodzajem zadania i polem zadania (`Task`, `TargetCol`,
  `TargetRow`, protokół 17); `ContentMessage.Tasks` (rodzaje zadań: id, klucz, nazwa, dobro).

## Przepływ

```mermaid
flowchart LR
  C["StartTaskCommand<br/>(gracz albo AI)"] --> V{"sprawdzenie:<br/>nasza osada, technologia, znane pole,<br/>strefa + 2 pola, pole pasuje,<br/>1 zadanie na 25 ludzi, wolni ludzie, droga"}
  V -->|tak| G["grupa robocza: fachowcy zadania,<br/>reszta wytwórcy"]
  G --> M["takt: marsz (A*, raz na cel)"]
  M --> W["rozkaz Work na polu zadania"]
  W --> P["puls cywilizacji:<br/>grupa je z magazynu osady"]
  P -->|brak jedzenia| R["wraca"]
  P --> Y["urobek do magazynu osady<br/>złoże traci zasobność"]
  Y -->|złoże wyczerpane| R
  R --> H["ludzie wracają do osady"]
```

## Zasady

- **Gdzie:** pole należące do strefy osady albo najwyżej `rangeBeyondZoneCells` (2) pola od jej strefy; znane cywilizacji, lądowe,
  nie cudze. Na jednym polu pracuje najwyżej jedna grupa danej cywilizacji.
- **Ile:** osada prowadzi jedno zadanie na każde `peoplePerTask` (25) ludzi (grupy w terenie dalej się liczą do osady). Grupa ma
  `defaultPeoplePerGroup` (10) ludzi, najwyżej `maxPeoplePerGroup` (30). W osadzie zostaje co najmniej 1 osoba.
- **Kto idzie:** najpierw fachowcy z fachem zadania (łupacze krzemienia do kopalni krzemienia, kamieniarze do kamieniołomu), ze swojej warstwy; reszta
  to wytwórcy, którzy nie mają innego fachu (myśliwi, rybacy i rolnicy zostają przy swojej pracy).
- **Rodzaje** (`tasks.json`, do strojenia):

| Rodzaj | Źródło | Dobro | Urobek na osobę na rok | Technologia | Fach |
| --- | --- | --- | --- | --- | --- |
| wycinka (`woodcutting`) | biom: wartość `gather` drewna | drewno | 0,5 × wartość biomu (las liściasty 6 → 3, tajga 5 → 2,5, łąka 0,5 → 0,25) | — | — |
| kopalnia krzemienia (`flint_mine`) | złoże krzemienia na powierzchni | krzemień | 1 | obróbka krzemienia | łupacz krzemienia |
| kamieniołom (`quarry`) | złoże kamienia na powierzchni | kamień | 1 | kamieniarstwo | kamieniarz |
| pole (`field`, od PR 4b-2) | żyzny ląd (żyzność ≥ 20), tylko wioska (`settledOnly`) | zboże | reguła rolnictwa z `farming.md`: 3 × (50 + żyzność) / 100 × sezon, najwyżej 40 rolników na pole | rolnictwo | rolnik |
| łowisko (`fishing_ground`, od PR 4b-2) | ląd obok wody (`waters`: wybrzeże 4, jezioro 4, płytkie morze 2) | ryby | 0,75 × najlepsza woda obok (wybrzeże → 3) | rybołówstwo | rybak |

- **Urobek w pulsie:** ludzie × urobek na osobę na rok × miesiące pulsu / 12. Przykład z testu: 10 ludzi w lesie liściastym przez rok
  = 10 × 0,5 × 6 = 30 drewna.
- **Złoża się wyczerpują** (GDD 4.4): zasobność jest w całych jednostkach; grupa bierze najwyżej to, co zostało. Puste złoże znika,
  a grupa wraca do domu („Flint Mine of X ran out; the work group comes home”). Las w tym PR się nie wyczerpuje (odrastanie lasu
  przyjdzie, gdy wycinka będzie miała znaczenie dla AI, PR 4b).
- **Jedzenie:** grupa je razem ze swoją osadą, w pulsie, z jej magazynu (`war.json foodPerWarriorPerYear` na osobę). Gdy jedzenia
  brakuje, wraca. Grupa robocza to realny koszt: 10 ludzi mniej zbiera jedzenie. Pomiar z 2026-11-13: zlecanie wycinki każdemu obozowi
  od 25 ludzi co sekundę zagłodziło świat (6 osad zamiast 232 po 15 minutach), więc AI w 4b musi liczyć ten koszt.
- **Dom zniknął** (osada porzucona albo zdobyta): grupa idzie do najbliższej własnej osady (jak każdy oddział wracający do domu).
- **Zbieranie ze strefy:** od PR 4b-1 `zoneGathersMaterials` jest `false`: drewno (dobra zadań z biomu) i złoża zadań (krzemień,
  kamień) przychodzą **tylko z zadań**, a żywność dalej ze strefy (decyzja Patryka z 2026-11-12). Wartość `true` przywraca zbieranie
  ze strefy jak w 4a (wtedy AI nie zleca zadań).
- **Pola i łowiska** (PR 4b-2, decyzja Patryka z 2026-11-16: każde pole to zadanie): `zoneFarms: false` i `zoneFishes: false`.
  Wioska uprawia **tylko** pola, na które wysłała grupę; bez zadania pola nie ma zboża (wytwórcy w domu dalej zbierają dziką żywność).
  Ryby przychodzą tylko z łowisk; woda strefy nie daje już ryb zbieraczom. Pole jałowieje i odpoczywa jak dawniej
  (`FarmingSystem` znaczy pola grup przy pracy), a susza i nawadnianie zmieniają plon pola jak dawniej plon strefy. Pole, którego
  żyzność spadnie poniżej 20, kończy się samo, bez wpisu w kronice (wyjałowione pole to zwykły rytm rolnictwa; kopalnia, która się
  wyczerpie, dalej pisze „Flint Mine of X ran out”; poprawka z 2026-11-16). Grupa najpierw oddaje plon do magazynu, potem z niego je.
- **Grupy to ludzie osady** (PR 4b-2): ludzie w grupach roboczych liczą się do urodzeń, zgonów dorosłych i limitu poziomu osady
  (`PopulationSystem`), do dryfu warstw i do fachów (`SkillSystem`: mistrzowie w polu dalej uczą, a ich ludzie potrzebują fachu).
  Głód zabija tylko w domu (grupa je w pulsie albo wraca), a w domu zostaje co najmniej 1 osoba. Bez tego wioska, której rolnicy
  pracowali w polu, przestawała rosnąć i uczyć (ludność w 3 seedach spadała o 30–75%).
- **AI (od PR 4b-1):** gubernator, decyzja G9 (`TaskPlanner.cs`, opis w `docs/ai/03-gubernator.md#g9`): sieć osad wysyła grupę po
  materiał, którego ma na mniej niż 2 lata zużycia rzemieślników, z osady najedzonej w ≥ 95%, i odwołuje ją przy zapasie na 4 lata.
  Te same komendy co gracz (`StartTaskCommand`, `ReturnUnitCommand`), dźwignia 3. Od PR 4b-2 także pola i łowiska: każdy rolnik i
  rybak w domu idzie na najlepsze wolne pole albo łowisko; grupa pola wraca, gdy jej pole daje mniej niż 70% najlepszego wolnego, a nowe pole AI bierze tylko na ziemi, która odpoczęła do
  70% naturalnej żyzności (bez tego pole wyjałowione do 19 odrastało do 20 i było brane znowu: 693 pola w 15 min u jednego ludu).

## Test sternika (ADR 0027)

1. **Dźwignia:** 3 „co robimy na ziemi”.
2. **AI robi to samo, gdy gracz nie klika:** od 4b-1 gubernator wydaje ten sam `StartTaskCommand` i `ReturnUnitCommand` (G9); gracz,
   który weźmie dźwignię 3, zleca zadania sam, a AI przestaje.
3. **Widać w 2–3 minuty:** grupa idzie na pole (dwie postacie z siekierkami w kolorze ludu), pole zadania ma obrys w kolorze ludu,
   magazyn osady rośnie, puste złoże znika z mapy.
4. **Kronika:** „The work group of X ran out of food and comes home”, „Flint Mine of X ran out; the work group comes home”.
   Brakuje zdania przy starcie zadania (dojdzie z dziennikiem „dlaczego” gubernatora w 4b).
5. **Partia bez klikania:** bez zmian względem `development` (ten sam hasz stanu), bo AI jeszcze nie zleca zadań.

## Pliki danych
`data/tasks.json` (+ `data/schema/tasks.schema.json`), rola `workers` w `data/units.json`, grafika `art/placeholder/units/unit_workers_idle_01.png`.

## Testy
`tests/CivSim.Tests/Units/TaskTests.cs`: dane; zasady komendy (rodzaj, zasięg, wielkość grupy, cudza osada, złoże i technologia,
jedno zadanie na pole, limit na 25 ludzi); marsz i urobek w pulsie (30 drewna za rok, nic bez pulsu); wyczerpanie złoża i powrót;
odwołanie grupy; brak jedzenia; zapis i hasz; komenda z protokołu; zbieranie ze strefy z flagą wyłączoną (drewna 0, żywność bez zmian).

## Wydajność
Mapa standard, seed 7, 8 cywilizacji, 9000 taktów (15 minut przy ×1), Release:

| Bieg | Średni takt | 95% taktów poniżej | Takty > 30 ms | Oddziały na końcu |
| --- | --- | --- | --- | --- |
| `development` przed PR | 2,38–2,40 ms | 7,7 ms | 12 (z raportu) | 96 |
| ten PR, bez grup | 2,26–2,49 ms | 7,1 ms | 10 (z raportu) | 96 |
| ten PR, `--work-groups 400` (197 grup, 153 przy pracy) | 2,04 ms | 6,8 ms | 7 | 312 |

Wszystkie takty powyżej 30 ms to puls świata (klimat) co 300 taktów, 35–40 ms, także przed tym PR (znany problem 12).
`TaskSystem` przegląda oddziały co takt, ale liczy tylko w pulsie właściciela; bez alokacji.
Pomiar: `CivSim.Headless --map-size standard --seed 7 --turns 9000 --civs 8 --quiet --work-groups 400` (opcja tylko do pomiaru:
zleca wycinkę każdej osadzie od 100 ludzi raz, przez zwykłą komendę; to nie jest AI).
