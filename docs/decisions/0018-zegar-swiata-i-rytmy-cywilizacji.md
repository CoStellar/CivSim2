# ADR 0018: Zegar świata i rytmy cywilizacji, decyzje reaktywne i proaktywne

**Status:** przyjęta, 2026-11-06 (decyzja Patryka). Zastępuje część ADR 0013 o „turze decyzyjnej” (`IsDecisionTurn`).

## Kontekst

Do etapu 4d cały świat miał jedną długość tury, ustaloną przez najbardziej rozwiniętą cywilizację. Gdy pierwsza cywilizacja weszła
w neolit, wszyscy dostawali tury po 25 lat, także plemiona z epoki kamienia. Przez to tempo zjawisk liczonych „raz na turę” (zwiad,
widzenie, ruch obozu, odstępy decyzji AI, handel między cywilizacjami) zależało od cudzej ery. Patryk chce (2026-11-06), żeby:

- czas gry płynął w latach jak licznik, który sam niczego nie liczy;
- każda cywilizacja liczyła swój stan i podejmowała decyzje tylko co tyle lat, ile wynika z **jej** ery;
- decyzje dzieliły się na **reaktywne** (gdy coś się stanie, np. ktoś nas zaatakował, także poza rytmem) i **proaktywne** (we własnym
  rytmie);
- żadna cywilizacja nie traciła lat, gdy długości kroków różnych er nie są swoimi wielokrotnościami (np. 50 i 20 lat).

## Decyzja

- **Licznik świata** to `GameState.Month`. Pętla nie obraca się co rok: przeskakuje od razu do najbliższego miesiąca, w którym ktoś
  ma krok. To jest to samo co licznik co rok, tylko bez pustych obrotów.
- **Rytm cywilizacji** = długość tury jej ery z `data/eras.json` (epoka kamienia 50 lat, neolit 25, brąz 20, …). Na planecie z porami
  roku na żywo krok jest krótszy, jak w ADR 0013 (Uran: 10,5 roku).
- **Kroki są przypięte do kalendarza:** rytm 25 lat daje kroki w latach 0, 25, 50…, rytm 20 lat w latach 0, 20, 40… Krok trwa zawsze
  do następnego kroku, więc cywilizacja liczy dokładnie tyle miesięcy, ile minęło. Po zmianie ery pierwszy krok bywa krótszy (neolit
  kończy się w roku 75, brąz idzie co 20 lat: pierwszy krok brązu trwa 5 lat, do roku 80). Wartości z `eras.json` nie muszą się
  dzielić; zostają bez zmian.
- **Krok świata** (jedna „tura” pętli i jedna migawka dla widoku) trwa od teraz do najbliższego kroku kogokolwiek.
- **W kroku cywilizacji** liczy się wszystko, co jej dotyczy, za cały jej krok: zbieranie, rolnictwo (zużycie pól), sieć handlowa,
  receptury, jedzenie, psucie, ludność, zdarzenia (susza, epidemia), nauka, widzenie i zwiad, marsz wędrówki, pogłoski, handel z
  partnerem (gdy krok ma któraś ze stron) i jej decyzje proaktywne. Cywilizacja, która czeka, nie zmienia się wcale.
- **W kroku świata** liczy się to, co nie należy do nikogo, za miesiące kroku świata: klimat, pory roku, poziom morza, odrastanie
  dzikiej żywności na polach poza strefami osad, odpoczynek ziemi, rozpad ruin, wygasanie efektów, strefy wpływów, granice i spory
  graniczne, wojny i rozejmy, psucie dóbr w opuszczonych osadach.
- **Decyzje proaktywne** (we własnym kroku): strategia (S1, co 15 własnych kroków i od razu w nowej erze), decyzje obozów (G1–G6),
  receptury (T1), przegląd stosunków (D2, raz na 50 lat, w kroku, który zawiera rok przeglądu).
- **Decyzje reaktywne** (w najbliższym kroku świata, także poza rytmem):
  - odpowiedź na propozycję umowy (D1);
  - wybór nowego badania, gdy poprzednie się skończyło (U1);
  - **wypowiedziano nam wojnę** → przegląd stosunków (D2: pokój, sojusze, odwet);
  - **napadnięto nasz obóz** → przegląd stosunków (D2) i obozy od razu szukają bezpieczniejszego miejsca (G2), nawet tuż po
    przeprowadzce.
  Wyzwalacze to „alarmy” cywilizacji (`CivilizationState.Alarms`), kasowane po fazie decyzji. Od etapu 4e dojdzie do nich obrona.
- **Odstępy AI liczą własne kroki:** `reviewEverySteps` (15), `searchEverySteps` (3), `minStepsBetweenMoves` (2). Dawniej liczyły
  tury świata. Pogłoski i przegląd dyplomaty liczą dokładne miesiące, więc kroki krótsze niż rok już ich nie powtarzają.
- Format zapisu 19: zegar każdej cywilizacji (`NextStepMonth`, `StepStartMonth`, `StepMonths`, `Steps`, `Alarms`) i długość kroku świata.

## Rozważane alternatywy

- **Krok świata = najkrótszy rytm, świat liczy wszystko w każdym kroku** (pierwsza propozycja z planu
  `docs/prompts/04-ai-czas-wartosci-migracje.md`): prostsze, ale plemię z epoki kamienia nadal liczyłoby gospodarkę co 25 lat,
  a Patryk chce, żeby czekająca cywilizacja nie liczyła nic.
- **Wszyscy liczą wszystko co rok:** neolit to ok. 45 tys. kroków po ok. 20 ms, kwadrans na mapie standard (dziś ok. 40 s), bez
  zysku w zachowaniu.
- **Kroki nieprzypięte do kalendarza (każdy co swoje lata od zmiany ery):** cywilizacje tej samej ery liczyłyby w różnych latach,
  więc kroków świata (a z nimi klimatu i stref wpływów) byłoby wielokrotnie więcej.
- **Zmiana długości tur er tak, żeby się dzieliły (np. brąz 25 albo 10 lat):** niepotrzebna, bo każdy krok liczy dokładny czas.
  Jedyny skutek 20 lat: w świecie z epoką brązu jest trochę więcej kroków świata (lata 20, 25, 40, 50, 60, 75…).

## Konsekwencje

- Gdy wszyscy są w tej samej erze, symulacja liczy się dokładnie jak wcześniej: statystyki z 3 seedów są identyczne aż do tury,
  w której pierwsza cywilizacja wchodzi w neolit.
- Po wejściu kogoś w neolit plemiona z epoki kamienia nadal liczą co 50 lat (wcześniej co 25), więc zwiadowców, ruchów i odstępów
  decyzji jest u nich o połowę mniej, a wyniki gier się zmieniają (nowe wartości kontrolne hashy).
- „Tura” w nagłówkach CSV i w `--turns` to krok świata. Lata gry są w kolumnie `year`.
- Gracz (etap 13) dostanie ten sam rytm co AI swojej ery; reakcje na wydarzenia przyjdą jako komunikat „wymaga uwagi”.
