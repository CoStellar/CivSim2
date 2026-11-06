# Zwiad i pamięć mapy

**W jednym zdaniu:** każda cywilizacja zna tylko pola, które widziały jej osady i zwiadowcy, a zapomina te, do których nie wraca przez kilka pokoleń; AI wybiera miejsca tylko spośród znanych pól, serwer pokazuje graczowi tylko jego wiedzę, a obóz może wędrować przez kilka tur do znanego dalekiego miejsca, także po lodzie morskim i pomostach lądowych (etap 3g, GDD 8.2, CORE uproszczone: pamięć ustna).

**Faza tury:** 2. Percepcja (widzenie i zwiady), 5. Ruch (wędrówki). Decyzja o wędrówce: faza decyzji (AI plemion, komenda `JourneyCommand`).

## Dane wejściowe i wyjściowe
- Czyta: `data/perception.json` (`sightCells`, `scoutCells`, `oralMemoryYears`, `journeyCells`), osady, biomy i lód morski (`IceField`), czas gry.
- Zmienia: `CivilizationState.SeenYear` (dla każdego pola Ziemi rok ostatniej obserwacji + 1; 0 = nigdy), `SettlementState.JourneyTarget`, położenie wędrujących obozów, kronika („set out”, „reached the end of its journey”).
- Publikuje: serwer liczy znane pola gracza z pamięci co turę i wysyła fragmenty mapy, które stały się znane albo zapomniane (mgła wojny).

## Przepływ

```mermaid
flowchart LR
  S["Osady: widzą 6 pól wokół"] --> M["Pamięć: rok ostatniej obserwacji"]
  Z["Zwiadowca z każdej osady:<br/>15 pól w losowym kierunku"] --> M
  M --> K{"widziane w ciągu 300 lat?"}
  K -->|tak| KN["Znane pole"]
  KN --> AI["AI: miejsca tylko ze znanych pól"]
  KN --> V["Serwer: widok gracza"]
  AI --> J["JourneyCommand: daleki cel do 40 pól"]
  J --> W["Ruch: do 10 pól na turę po znanym lądzie i lodzie"]
```

## Zasady i wzory
- **Widzenie:** każda zamieszkana osada widzi pola w promieniu `sightCells` (6). Na starcie gry cywilizacja zna 127 pól wokół obozu.
- **Zwiady** (GDD 8.2: do pamięci trafia to, co widzą łowcy i zwiadowcy): co turę każda osada wysyła jednego zwiadowcę w losowym kierunku (strumień losowy „population”, w kolejności osad). Idzie prosto do `scoutCells` (15) pól po lądzie i lodzie, a przy wodzie skręca; widzi pola obok swojej drogi.
- **Pamięć ustna:** pole jest znane przez `oralMemoryYears` (300 lat) od ostatniej obserwacji. Mapy rysowane (trwałe) przyjdą z pismem w erze brązu.
- **AI:** miejsca do przeprowadzki, odłamu i podboju wybiera tylko spośród znanych pól. Głodny albo zmarznięty obóz, który nie ma lepszego miejsca w zasięgu jednego ruchu, raz na turę ery szuka znanego lądu do 40 pól dalej (co trzecie pole w obu kierunkach, kara 1% za pole odległości). Rusza, gdy daleki cel jest wart ponad 1,6 raza tyle co obecne miejsce i istnieje znana droga.
- **Wędrówka:** obóz idzie najkrótszą znaną drogą po lądzie i lodzie morskim (pomosty lądowe z epok lodowych to zwykły ląd), do `maxMoveCells` (10) pól na turę, i zatrzymuje się tylko na lądzie, gdzie nikt nie mieszka. Kończy się po dojściu albo gdy drogi już nie ma (zapomniana ziemia, stopiony lód). Wioski nie wędrują.
- **Złoża w widoku:** na razie gracz widzi wszystko na znanych polach. Ukrywanie złóż nieznanych surowców (GDD 8.2) przyjdzie razem z widokiem gracza w etapie 13.

## Pliki danych
- `data/perception.json`.

## Testy
- `MapMemoryTests`: start z 127 znanymi polami; pamięć zanika po 300 latach i wraca, gdy plemię znów widzi pole; zwiadowcy poszerzają wiedzę; wędrówka tylko do znanego lądu, 10 pól na turę, koniec u celu; po lodzie morskim da się chodzić.
- `LocalServerHostTests.PlayerOnlyReceivesCellsItKnows`: gracz dostaje na starcie 127 znanych pól.

## Wydajność i wyniki
- Seed 7, mapa standard, 1000 tur: tura 19,7 ms (bez etapu 3g: 13,6 ms, ale wtedy żyło 90 tys. zamiast 111 tys. ludzi); percepcja 0,3 ms, alokacje 280 KB na turę (szukanie drogi używa tych samych buforów).
- Seed 42, 3000 tur: cywilizacje znają średnio ok. 1100 pól; 9 wędrówek, wszystkie doszły do celu. Wędrówek jest mało, bo plemiona odchodzą tylko z głodu albo zimna; ekspansję w nieznane pokieruje „kierunek ekspansji” i doradca dowódca w etapie 4.
