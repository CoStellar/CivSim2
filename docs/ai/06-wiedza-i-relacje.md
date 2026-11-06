# Co AI wie: pamięć mapy, kontakty, sympatia (W1–W2, R1–R5)

[← spis treści](README.md) · kod: `src/CivSim.Core/Perception/MapMemory.cs`, `src/CivSim.Core/Diplomacy/ContactSystem.cs`,
`src/CivSim.Core/Diplomacy/Relations.cs` · GDD 8.2, 8.5 · systemy: `docs/systems/perception.md`, `docs/systems/relations.md`

To nie są decyzje. To dane, z których AI korzysta przy decyzjach.

## W1: Pamięć mapy

- Co turę każda zamieszkana osada **widzi 6 pól** wokół siebie (`sightCells`).
- Co turę każda zamieszkana osada wysyła **zwiadowcę** w losowym kierunku (strumień `population`): idzie do 15 pól (`scoutCells`)
  prosto po lądzie i lodzie morskim, skręca losowo przy wodzie, widzi pola obok swojej drogi.
- Pole jest **znane** przez 300 lat od ostatniego zobaczenia (`oralMemoryYears`), potem zapomniane, chyba że plemię wróci.
- Kto używa: G2 i G3 (kandydaci muszą być znani), S1 (cel ekspansji musi być znany), W2.

**Ważne zastrzeżenie:** AI wybiera tylko znane pola, ale ocenia je według **obecnego** stanu świata (dzisiejsza dzika żywność,
dzisiejsza temperatura, kto tam dziś mieszka, ilu ma wojowników), a nie według tego, co widziało. Liczy też jedzenie pól wokół
znanego pola, nawet jeśli tych pól nie widziało. Patrz [uwagi](08-ocena-i-uwagi.md#2-ai-zna-dzisiejszy-stan-pól-które-tylko-pamięta).

## W2: Droga wędrówki

Najkrótsza droga (przeszukiwanie wszerz) po polach **znanych** i przechodnich (ląd, lód morski), nie dalej niż 40 pól od startu.
Używana do sprawdzenia, czy G3 ma sens, i potem co turę do marszu.

## R1: Pierwszy kontakt

Cywilizacja poznaje inną (poziom „spotkana”), gdy:

- jej osada albo zwiadowca **zobaczy** zamieszkaną osadę tamtej w tym roku,
- ich **strefy wpływów się stykają** (dwa sąsiednie pola należą do różnych cywilizacji),
- tamta **napadnie** jej obóz (G2 z wypędzeniem).

Pierwszy kontakt to wpis w kronice. Dziś kontakt **nie zmienia żadnej decyzji** poza przez sympatię (R2–R4). Dyplomacja przyjdzie w 4d.

## R2: Pierwsze wrażenie

Przy pierwszym prawdziwym kontakcie:

```
pierwsze wrażenie = (otwartość − 500) / 500 × 20 punktów     (−20 … +20)
                    − 5 punktów, gdy pierwszym kontaktem jest zetknięcie granic
```

Pierwsze wrażenie nie zanika.

## R3: Pamięć napaści

Gdy obóz A próbuje wypędzić obóz B (wygrana czy przegrana), B zapamiętuje: sympatia −50, zaufanie −20, zanika liniowo przez
`150 + 150 × mściwość/1000` lat (150–300 lat). A tylko „widzi” B (kontakt bez wspomnienia).

## R4: Spór graniczny

Gdy cywilizacja mieszka w osadzie, do której inna wciąż ma roszczenie (zajęła porzuconą osadę przed upływem 100 lat roszczenia), ta
druga ma trwające wydarzenie „spór graniczny”: sympatia spada o 1 punkt na rok aż do −30. Po zakończeniu sporu zanika przez 20 lat.

## R5: Pogłoski

Co turę cywilizacja A może usłyszeć od znanej cywilizacji B o cywilizacji C, której A jeszcze nie zna. Szansa na każdą taką parę:
20% na stulecie przeliczone na długość tury (w epoce kamienia 10% na turę), najwyżej jedna pogłoska na turę. Miejsce jest przesunięte
o do 50% odległości, liczba ludzi przekłamana o ±50%. Dziś pogłoski **nie wpływają na decyzje** (tylko kronika).

## Sympatia i zaufanie

```
sympatia = pierwsze wrażenie + Σ (sympatia wspomnienia × siła wspomnienia)      ograniczone do −100 … +100
zaufanie = Σ (zaufanie wspomnienia × siła wspomnienia)                           ograniczone do −100 … +100
siła = 100% w trakcie i w chwili zdarzenia, potem liniowo do 0% przez czas zanikania
```

Najwyżej 12 wspomnień na znaną cywilizację; przy przepełnieniu znika najsłabsze.

| Kto używa | Jak |
| --- | --- |
| G2, wypędzenie obcego obozu | czynnik opinii `1000 − 5 × sympatia` ‰ (sympatia −100 → 1,5; +100 → 0,5) |
| zaufanie | **nikt** (czeka na umowy w 4d) |

Pełna tabela wydarzeń (też tych, które przyjdą w 4d–4f) jest w `data/relations.json` i GDD 8.5.
