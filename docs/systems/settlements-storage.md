# Osady i magazyny

**W jednym zdaniu:** każda osada ma własny magazyn z towarami; żywność psuje się co rok, a wszystko ponad pojemność magazynu przepada.

**Faza tury:** 7. Gospodarka, na samym końcu (psucie i limity magazynów, GDD 11.1). Czas gry przesuwa się w fazie 11 (zapis).

## Dane wejściowe i wyjściowe
- Czyta: `GameState.Settlements`, długość tury z `data/eras.json` (era najbardziej zaawansowanej żywej cywilizacji), tempo psucia z `data/goods.json`, pojemność poziomu osady z `data/settlements.json`.
- Zmienia: zapasy w magazynach osad (`SettlementState.Stock`), czas gry `GameState.Month`.
- Publikuje w migawce: każdą osadę (właściciel, pole, nazwa, poziom, zapasy) i czas gry. Serwer wysyła graczowi zapasy tylko jego własnych osad; cudze osady widzi bez zawartości magazynu, i tylko na polach, które zna. Obserwator widzi wszystko.

## Przepływ

```mermaid
flowchart LR
  N["Nowa gra"] --> C["Obóz plemienny na polu startowym<br/>+ zapasy z newTribe"]
  C --> T["Tura"]
  T --> S["Psucie: zapas × udział, który przetrwał turę"]
  S --> L["Limit: nadwyżka ponad pojemność<br/>znika proporcjonalnie"]
  L --> M["Czas gry += długość tury"]
  M --> V["Migawka → widok gracza i obserwatora"]
```

## Zasady i wzory
- **Towary są tylko w magazynach osad** (GDD 4). Nie ma skarbca cywilizacji.
- **Start:** każde plemię dostaje jeden obóz (poziom `camp`) na polu startowym z generatora i zapasy z `newTribe.stock` (40 roślin, 30 mięsa, 10 skór, 20 drewna, 10 krzemienia, 5 narzędzi kamiennych).
- **Czas gry** liczymy w miesiącach (`GameState.Month`), bo w erze informacji tura trwa pół roku, a w kosmicznej kwartał. Rok w podglądzie = miesiące / 12.
- **Długość tury** bierzemy z ery najbardziej zaawansowanej żywej cywilizacji. To zasada tymczasowa: ostatecznie ustalimy ją razem z erami w etapie 3f.
- **Psucie** (GDD 4.3, zgoda Patryka z 2026-11-05: liczby są **na rok**): z zapasu zostaje `(1 − tempo)^lata`. Pełne lata liczymy jako procent składany, resztę roku proporcjonalnie. Przykład: zboże (1% na rok) po turze 50 lat ma 60,5% zapasu, ryby (8%) tylko 1,5%. Zapasy żywności są więc buforem na złe lata, nie oszczędnościami na stulecia. Według GDD 16 psuje się tylko żywność; walidator danych tego pilnuje.
- **Pojemność** (CORE uproszczone): jedna liczba na osadę, z poziomu osady (obóz 300 jednostek, wioska 2000…). Nadwyżka znika ze wszystkich towarów proporcjonalnie do ich ilości. Sprzedaż nadwyżki z rabatem przyjdzie z siecią handlową (etap 3e).
- Wszystko na liczbach całkowitych (`Amount` w setnych częściach, udział psucia w miliardowych), więc wynik jest identyczny na każdym komputerze.

## Pliki danych
- `data/goods.json`: 16 towarów er 1–2, kategoria (żywność, masowe, cenne, płyny), psucie na rok, kolor.
- `data/settlements.json`: 6 poziomów osad (ludność, pojemność, czy się przemieszcza) i start plemienia.
- `data/eras.json`: 10 er i długość tury w miesiącach (GDD 6.2).
- `data/recipes.json`: 6 receptur jednoetapowych. Na razie tylko wczytywane i sprawdzane; produkcja ruszy w etapie 3e.

## Testy
- `StorageTests`: dane się wczytują; zepsute dane zgłaszają każdy błąd; psucie zgodne ze wzorem procentu składanego; każde plemię ma obóz z zapasami; zboże po turze ma 60,50 z 100, narzędzia się nie psują; nadwyżka znika proporcjonalnie; zapis i odczyt zachowuje osady i efekty; gra z gospodarką jest deterministyczna; gracz widzi tylko swój magazyn.

## Wydajność
Mapa standardowa, 10 000 tur, Release: średnio 0,012 ms na turę (wcześniej 0,003 ms), 5,5 KB alokacji na turę (wcześniej 1 KB; to kopie zapasów w migawce). Mapa ogromna: 0,027 ms, 13,9 KB. Daleko od celów (50 ms i 250 ms).
