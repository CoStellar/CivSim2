# ADR 0017: Charakter i strategia w stanie gry, strateg działa komendą

**Status:** przyjęta, 2026-11-06 (etap 4b).

## Kontekst

Etap 4b daje cywilizacjom charakter (GDD 8.4) i stratega, który ustawia wagi celów i kierunek ekspansji (GDD 8.1, 7.6). Te wartości
zmieniają decyzje AI przez wiele tur, więc muszą przetrwać zapis i wczytanie gry. Muszą też dawać ten sam wynik na każdym komputerze.
Dziennik „Dlaczego?” z ADR 0016 może żyć poza stanem gry, bo nic nie steruje. Charakter i strategia sterują, więc muszą być w stanie.

## Decyzja

- **Charakter w `CivilizationState.Traits`:** 8 cech w tysięcznych. Losujemy je przy tworzeniu gry generatorem wyprowadzonym ze strumienia
  `leaders` i numeru cywilizacji (średnia z dwóch losowań, więc skrajności są rzadsze). Żaden inny strumień się nie przesuwa, więc mapa
  i start gry są takie same jak przed 4b. Zmiana charakteru z doświadczeniem i sukcesja przywódców przyjdą w etapie 4f.
- **Strategia w `CivilizationState`:** wagi celów (`GoalWeights`), cel ekspansji (pole Ziemi), miesiąc następnego przeglądu i era
  ostatniego przeglądu. Strateg ustawia je komendą `SetStrategyCommand`, jak każdy inny doradca, bo każda zmiana stanu to komenda
  (CLAUDE.md, zasada 2). Później gracz wyznaczy kierunki tą samą komendą.
- **Przeglądy co 15 tur, rozłożone:** pierwszy przegląd w pierwszej turze, następny po 15 turach plus przesunięcie zależne od numeru
  cywilizacji. Nowa era wymusza przegląd od razu (GDD 8.1: „wstrząs”).
- **Strateg czyta wartości miejsc od gubernatora** z tej samej tury, więc w radzie działa po nim. Nowa strategia działa od następnej tury.
- **Ostrożność decyduje o losowaniu:** z szansą równą ostrożności cywilizacja bierze najlepsze działanie, w przeciwnym razie losuje
  spośród 3 najlepszych z szansą zależną od oceny. Generator jest wyprowadzony z seeda, doradcy, osady i tury, więc wspólny strumień `ai`
  się nie przesuwa.
- **Obóz szuka nowego miejsca co 3 tury** (rozłożone między obozy), chyba że głoduje (najedzony poniżej 85%) albo planeta ma pory roku na
  żywo. Decyzja zależy tylko od stanu (numer tury i obozu), więc jest deterministyczna także po wczytaniu gry.
- **Górna granica oceny miejsca:** zanim gubernator policzy jedzenie okolicy, liczy tę samą ocenę dla nietkniętej ziemi (dzika żywność
  na 100%). Jeśli nawet ona nie wejdzie do 3 najlepszych, pole jest pomijane. Wynik jest dokładnie taki sam jak bez skrótu, bo zbieranie
  tylko obniża dziką żywność, a każdy czynnik jest niemalejący.
- **Format zapisu 16** (nowe pola cywilizacji). Złote hashe testów zmieniły się, bo stan zawiera charakter.

## Konsekwencje

- Cywilizacje różnią się zachowaniem: ekspansywne wcześniej wysyłają odłamy, ciekawe świata mocniej ważą naukę, nieostrożne częściej
  wybierają drugie albo trzecie najlepsze działanie.
- Zmiany zachowania względem etapu 3 (te same seedy) opisuje `docs/systems/ai.md` i raport w PR.
- Mapa wpływów z GDD 8.2 (zagrożenie, napięcie na granicach) przyjdzie z kontaktami i wojną (4c–4e). Dziś kierunek ekspansji opiera się
  tylko na wartości wolnej ziemi.
