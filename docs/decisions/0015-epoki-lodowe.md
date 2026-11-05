# ADR 0015: Epoki lodowe: cykle Milankovicia, lądolody Paillarda, klimat zmienia się w trakcie gry

**Status:** przyjęta, 2026-11-05 (decyzje Patryka: wariant A, start w ciepłym momencie; osady na zalanych polach przenoszą się, zostają ruiny pod wodą).

## Kontekst

Decyzja z 2026-11-05: klimat ma się zmieniać w trakcie gry przez cykle Milankovicia i zmienność gwiazdy, a epoki lodowe mają być o 4–6 °C
chłodniejsze. Prototyp pokazał, że sam bilans energii z ADR 0014 reaguje na zmiany orbity o mniej niż 1 °C, bo średnie roczne nasłonecznienie
prawie się nie zmienia. Nawet lądolód rosnący przy zimnym lecie na 65° (model Imbrie i Imbrie 1980) sięgał tylko ok. 13–50% i nie dawał wyraźnych
okresów ciepłych: zimne fazy precesji (co 23 tys. lat) są krótsze niż czas narastania lodu.

## Decyzja

**Orbita w czasie** (`ClimateCycles`, dane w `data/planets.json`, sekcja `climateCycles`):
- Nachylenie osi waha się o ±1,2° wokół nachylenia planety co 41 tys. lat (Ziemia 22,2–24,6°).
- Mimośród rośnie od wartości planety o najwyżej 0,042 w dwóch cyklach: 405 i 100 tys. lat (Ziemia 0,017–0,059).
- Peryhelium obiega pory roku co 23 tys. lat (precesja).
- Zmienność gwiazdy: nowe ustawienia planety `starVariabilityPerMille` (0–100‰ w obie strony) i `starCycleYears`; domyślnie gwiazda jest stała.
- **Start w ciepłym momencie:** największe nachylenie i najmniejszy mimośród, bez lądolodów. Pierwsze zlodowacenie zaczyna się na Ziemi po ok. 15–20 tys. lat gry.

**Lądolody** (model Paillarda 1998, osobno dla każdej półkuli):
- Napęd: słońce półrocza letniego na 65°, mierzone w odchyleniach od jego średniej z 400 tys. lat danej planety.
- Trzy stany: okres ciepły (lód topnieje przez ok. 10 tys. lat) → zlodowacenie, gdy lato słabnie poniżej −0,75 odchylenia (lód rośnie przez ok. 50 tys. lat)
  → pełne zlodowacenie, gdy objętość przekroczy 0,8 → koniec, gdy lato znów przekroczy średnią. Mocne lato topi lód szybciej, niż słabe go buduje.
  Wynik na Ziemi: epoka lodowa co ok. 115–120 tys. lat, gwałtowne ocieplenia, ok. 20% czasu ciepłe.
- Skutek dla klimatu: brzeg lądolodu schodzi z 70° do 40° szerokości, ale tylko na **prawdziwym lądzie mapy** (udział lądu w każdym pasie). Lądolód ma
  albedo 0,75 (świeży śnieg, więcej niż lód morski 0,62). Oceany pochłaniają 32% CO₂ (Ziemia 280 → 190 ppm).
- Planeta musi móc się zlodowacić: półkula, której lato na 65° ma na starcie 5 °C lub mniej, dostaje pełne epoki lodowe, a 15 °C lub więcej żadnych.
  Ziemia: północ 100%, południe 90%. Uran (bez zimnych biegunów): 0%.
- Wynik: Ziemia ochładza się w szczycie zlodowacenia z 14,5 do 9,5 °C (−5,0 °C), Mars o −4,6 °C, Uran wcale.

**Klimat w trakcie gry** (`ClimateSystem`, pierwszy w fazie świata):
- Co 500 lat gry: orbita, lód obu półkul, CO₂, a potem bilans energii z lądolodami. Każde pole dostaje nową temperaturę, różnicę lato–zima i sezon
  wzrostu, a pola lądu nowy biom. Dzika żywność i AI plemion reagują same, bo liczą z tych warstw.
- Generator zapisuje na mapie to, czego klimat nie zmienia (nowe warstwy `climate_sine`, `temperature_offset`, `continentality`), i używa tych samych
  reguł (`CellClimate`), więc ponowne przeliczenie klimatu startowego niczego nie zmienia (test).
- Stan klimatu świata (`ClimateState`: rok, stany i objętości lodu, bieżące CO₂, średnia temperatura) jest w zapisie i w hashu.

## Rozważane alternatywy

- **B: sama fizyka bilansu energii:** epok lodowych praktycznie nie ma (< 1 °C).
- **C: epoki lodowe jako zdarzenia losowe:** proste, ale nie wynikają z orbity ani z planety.
- **Imbrie i Imbrie (1980):** prostszy, ale bez wyraźnych okresów ciepłych i bez cyklu ok. 100 tys. lat.

## Konsekwencje

- Format zapisu 11, nowe złote hashe (start w ciepłym momencie zmienia nachylenie na starcie: Ziemia 24,6° zamiast 23,4°).
- Przeliczenie klimatu kosztuje ok. 20 ms co 500 lat (na mapie standard co 10 tur epoki kamienia) i przydziela pamięć na nowy model klimatu.
- Poziom morza zmienia się razem z lodem od etapu 1c-C (niżej, „Poziom morza”).
- Cywilizacje nie wpływają jeszcze na CO₂; miejsce na to jest w `ClimateState.Co2Microbar` (era przemysłowa, mechanizm efektów).

## Poziom morza (etap 1c-C, decyzja Patryka z 2026-11-05)

- Woda uwięziona w lądolodach obniża morze: `poziom = 100 − 120 m × średni udział lądolodu obu półkul / 40 m na stopień wysokości`
  (dane: `glacialSeaDropMeters`, `seaMetersPerStep`). W pełni zlodowacenia morze leży 3 stopnie niżej (97): odsłania się ok. 6–7% oceanu,
  czyli płytkie szelfy, jak w ostatnim zlodowaceniu.
- **Morze może też wznieść się ponad poziom startowy** (uwaga Patryka z 2026-11-05: klimat może się ocieplić, później także przez działalność ludzi):
  gdy topnieje stały lód lądowy ze startu (pola lądu z biomem lodu; Ziemia: 13% lądu), morze rośnie o `70 m × stopiona część`
  (dane: `meltSeaRiseMeters`; Ziemia: Grenlandia i Antarktyda to ok. 70 m). Przy całym stopionym lodzie morze stoi 2 stopnie wyżej (102)
  i zalewa ok. 2% lądu, najniższe wybrzeża. Złoża na zalanych polach zostają w danych i wracają, gdy morze się cofnie.
- Odsłonięte dno to nowy ląd (płaski, wilgotność sąsiadów, pełna dzika żywność); biom nadaje mu zaraz potem przeliczenie klimatu. Gdy lód topnieje,
  morze zabiera ten ląd z powrotem: znikają rzeki na jego krawędziach, właściciel i cechy terenu. Rodzaje wody (wybrzeże, płytkie, głębokie) liczymy
  na nowo; jeziora zostają.
- **Osady na zalanych polach** (decyzja Patryka): mieszkańcy przenoszą się z tym, co uniosą, na najbliższe suche pole bez innej zamieszkanej osady
  (do 30 pól), a to, co zostawili, i każda pusta osada na zalanym polu stają się ruinami pod wodą. Ruin pod wodą nie da się zająć ani ograbić;
  rozpadają się jak zwykłe ruiny.
- **Lód morski** (`IceField`) zmienia się co 500 lat razem z klimatem: morze zamarza, gdzie średnia roczna spada do −8 °C. W zlodowaceniu lód morski
  łączy masywy lądowe. Plemiona przeprowadzają się na razie skokiem do 10 pól bez liczenia drogi; lód jako droga zacznie mieć znaczenie przy
  wędrówkach przez kilka tur (etap 3g). Przy bardzo zimnej planecie zamarza cały ocean („Ziemia-śnieżka”: np. 80% światła przy stałych 280 ppm CO₂
  daje średnio −48 °C); cykl węglowy temu zapobiega, bo pod lodem wietrzenie ustaje i CO₂ rośnie.
- Po zmianie wybrzeża udział lądu w pasach (pod lądolody) mierzymy od nowa. Poziom morza jest w stanie klimatu świata (zapis, format 12, i hash).
- Wynik (Ziemia, seed 42): w 59 tys. roku morze opada o 80 m (971 pól nowego lądu), w 107 tys. o 120 m (kolejne 865 pól), a po końcu zlodowacenia
  (112–119 tys. lat) wraca. Między środkowymi kontynentami powstaje pomost lądowy, a na wschodzie wynurza się wyspa.
