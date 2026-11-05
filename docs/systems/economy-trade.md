# Sieć handlowa, ceny i produkcja

**W jednym zdaniu:** osady jednej cywilizacji, które leżą blisko siebie, tworzą sieć handlową i dzielą się zapasami z utratą zależną od odległości; rzemieślnicy wykonują receptury, a AI wybiera, co robić, według cen wewnętrznych sieci (etap 3e).

**Faza tury:** 7. Gospodarka (GDD 11.1): zbieranie → **rozdział w sieci** → **produkcja** → konsumpcja → psucie i magazyny (z **przelewem nadwyżek**). Wybór receptur: faza decyzji (AI), raz na turę ery.

## Dane wejściowe i wyjściowe
- Czyta: osady (`GameState.Settlements`: właściciel, pole, ludność, zapasy, receptura), `data/trade.json`, wartości towarów z `data/goods.json`, receptury z `data/recipes.json`, potrzeby warstw z `data/strata.json` (warstwa `crafts` pracuje przy recepturach), surowce biomów (`gather` w `data/biomes.json`) i złóż (`gather` w `data/resources.json`).
- Zmienia: zapasy osad, wybraną recepturę (`SettlementState.RecipeId`, tylko komendą `SetRecipeCommand`), wykonane cykle (`RunsMade`), zasobność i istnienie złóż nieodnawialnych na mapie.
- Publikuje w migawce: zapasy osad jak dotąd. Ceny i sieci nie są zapisywane: liczą się z bieżącego stanu, więc wynik po wczytaniu gry jest ten sam.

## Przepływ

```mermaid
flowchart LR
  G["Zbieranie: jedzenie, skóry,<br/>drewno z lasów, krzemień i kamień ze złóż"] --> N["Sieci: osady jednej cywilizacji,<br/>łańcuch do 8 pól"]
  N --> T1["1. Jedzenie: tura + rok zapasu"]
  T1 --> T2["2. Potrzeby wyższe: odzież, narzędzia"]
  T2 --> T3["3. Wejścia receptur"]
  T3 --> P["Produkcja: rzemieślnicy × lata tury"]
  P --> C["Konsumpcja"] --> S["Psucie → nadwyżka do sąsiadów z miejscem → reszta przepada"]
  N --> M["Ceny wewnętrzne: wartość × popyt / podaż"]
  M --> AI["AI: receptura o największej wartości dodanej"]
  AI --> R["SetRecipeCommand"]
```

## Zasady i wzory
- **Sieć** (GDD 4, CORE uproszczone, decyzja Patryka z 2026-11-05): zamieszkane osady jednej cywilizacji na jednym świecie, połączone łańcuchem, w którym sąsiednie osady dzieli najwyżej `linkCells` (8) pól. Handel między cywilizacjami przyjdzie z umowami w etapie 4.
- **Koszt odległości:** przy przekazaniu ginie `lossPercentPerCell` (2%) na każde pole odległości, najwyżej 90%. Towary idą od najbliższej osady z nadwyżką.
- **Kolejność przy niedoborach** (GDD 4.5): (1) jedzenie: każda osada dostaje jedzenie na turę i `foodReserveYears` (1 rok) zapasu, oddaje tylko ponad to; (2) towary potrzeb wyższych (odzież, narzędzia): tura + rok potrzeb, dawca zatrzymuje też to, czego potrzebuje jego własna receptura; (3) wejścia receptur. Utrzymanie i budowy (punkty 2 i 3 GDD) nie mają jeszcze czego dzielić.
- **Ceny wewnętrzne** (GDD 4.6): `cena = wartość towaru × popyt / podaż`, w granicach 25–400% wartości. Popyt = rok potrzeb ludzi i wejść receptur w całej sieci, podaż = zapasy sieci. Wszystkie rodzaje jedzenia mają wspólny stosunek popytu do podaży (ludzie jedzą dowolne jedzenie). Towar, którego nikt nie potrzebuje, kosztuje 25% wartości.
- **Produkcja** (GDD 4.4, receptury jednoetapowe): cykle na turę = `rzemieślnicy × lata tury / (pracownicy receptury × lata receptury)`, ograniczone wejściami w magazynie. Jedzenie jako wejście (chleb) nigdy nie zabiera jedzenia, którego ludzie potrzebują w tej turze.
- **AI** (`EconomyAi`, do doradcy skarbnika w etapie 4): raz na turę ery każda osada z rzemieślnikami bierze recepturę o największej wartości dodanej (`wyjścia − wejścia` po cenach sieci) spośród tych, których wejścia sieć ma; żadną, gdy nic się nie opłaca. Receptura musi należeć do osiągniętej ery (technologie w etapie 3f).
- **Nadwyżka magazynu** (decyzja Patryka: bez pieniędzy nie ma sprzedaży z rabatem): najpierw jedzie do osad sieci z wolnym miejscem, najbliższych najpierw, z tą samą utratą; reszta przepada.
- **Surowce przy zbieraniu:** drewno z biomów (`gather`: las liściasty 6, tajga 5, dżungla 4… na rok z nietkniętego pola), krzemień (2) i kamień (3) ze złóż widocznych od startu, w tej samej części co zebrane jedzenie. Złoża nieodnawialne tracą zebrane jednostki i znikają, gdy się wyczerpią (GDD 4.4).

## Pliki danych
- `data/trade.json`: `linkCells`, `lossPercentPerCell`, `foodReserveYears`, `priceRangePercent`.
- `data/goods.json`: `value` każdego towaru (dzikie rośliny = 1, narzędzia kamienne 8, odzież ze skór 6…).
- `data/strata.json`: `crafts` (rzemieślnicy).
- `data/biomes.json`, `data/resources.json`: `gather`.
- `data/recipes.json`: receptury.

## Testy
- `TradeProductionTests`: sieci łączą osady jednej cywilizacji w łańcuch; jedzenie trafia do głodnych z utratą 10% na 5 polach, odzież też, a dawca zatrzymuje swoje; rzemieślnicy robią tyle, ile pozwalają wejścia; produkcja nie zjada jedzenia ludzi; ceny rosną przy braku i spadają przy nadmiarze; AI wybiera odzież, gdy są skóry, a nie ma krzemienia; receptura z neolitu jest odrzucana w epoce kamienia; pełny magazyn oddaje nadwyżkę sąsiadowi; krzemień się wyczerpuje, a lasy dają drewno.

## Wydajność
- Mapa standard, seed 7, 500 tur: tura 25,2 ms (przed etapem 19,9 ms), z tego gospodarka 7,6 ms (było 4,1) i decyzje 13,9 ms (było 12,3). Rozdział w sieci ok. 1,4 ms (największa sieć ma 80 osad; odległości i kolejność dawców liczone raz na sieć, bufory używane ponownie), AI gospodarcza ok. 0,7 ms, przelew nadwyżek ok. 0,5 ms (pomijany, gdy sieć nie ma wolnego miejsca).
- Sieci budujemy trzy razy na turę (rozdział, magazyny, AI): ok. 0,4 ms każda. Można je liczyć raz, gdy będzie potrzeba.
- Wynik (seed 7, 1000 tur): średnio ok. 10 000 ludzi zamiast 9 400, nastrój 65 zamiast 50 (odzież i narzędzia), zdrowie 81,5 zamiast 84,7.
