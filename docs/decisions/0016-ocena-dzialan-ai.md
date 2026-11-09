# ADR 0016: Wspólna ocena działań AI w liczbach całkowitych, dziennik „Dlaczego?” poza stanem gry

**Status:** przyjęta, 2026-11-05 (etap 4a).

## Kontekst

Etap 3 miał trzy osobne, proste AI (plemiona, produkcja, badania), każde z własnym sposobem liczenia. GDD 8.3 wymaga jednej oceny działań
(iloczyn czynników z krzywych × waga celu × charakter − koszt), wyboru spośród 3 najlepszych i zapisu 3 najważniejszych czynników (GDD 8.7).
Przeniesienie nie mogło zmienić symulacji: inaczej nie wiedzielibyśmy, czy zmiana zachowania w etapie 4b wynika z nowych zasad, czy z błędu
przy przenosinach. Zasada determinizmu (ADR 0003) zabrania `float` i `double` w logice gry.

## Decyzja

- **Czynniki w tysięcznych, mnożone po kolei.** Ocena zaczyna od wartości bazowej w jednostkach działania (np. jedzenie w setnych), a każdy czynnik
  mnoży ją przez `czynnik / 1000` z dzieleniem całkowitym. Czynnik z procentów (np. wygoda 80%) to 800‰, a `x × 800 / 1000` daje dokładnie to samo
  co `x × 80 / 100`. Dzięki temu stare reguły dają bit w bit ten sam wynik. Czynnik może przekroczyć 1000 (premia, np. stare obozowisko 110%).
- **Krzywe odpowiedzi w liczbach całkowitych:** prosta, krzywa S (smoothstep na `Int128`) i próg. Wynik nigdy poniżej zera.
- **Koszt odejmowany na końcu.** Uczony: każda technologia ma tę samą wartość, minus dzisiejszy koszt.
- **Wybór:** `Chooser` trzyma 1–3 najlepsze działania (`pickAmongTop` w `data/ai.json`). Remis: wygrywa działanie ocenione wcześniej, czyli w
  stałej kolejności (pola pierścieniami, receptury i technologie po id). Przy jednym miejscu nie losuje nic. Losowanie z 2–3 używa generatora
  wyprowadzonego z seeda, strumienia `ai`, doradcy, osady albo cywilizacji i tury (`RandomStreams.DeriveSeed`), więc nie przesuwa wspólnego
  strumienia i pozwoli kiedyś liczyć doradców równolegle.
- **Na razie `pickAmongTop = 1`.** GDD mówi o losowaniu z 3, ale ostrożność zależy od charakteru (GDD 8.3, 8.4), który przyjdzie w 4b. Do tego czasu
  AI bierze zawsze najlepsze działanie i symulacja jest identyczna jak w etapie 3. Test sprawdza, że losowanie z 3 też jest deterministyczne.
- **Waga celu i charakter są neutralne (1000)** i nie zapisują się jako czynniki. Strateg i charakter w 4b.
- **Dziennik „Dlaczego?” żyje przy AI, nie w stanie gry.** Tylko wyjaśnia, nigdy nie steruje symulacją, więc nie wchodzi do hasha ani do zapisu.
  Lista jest czyszczona co turę i używana ponownie (bez alokacji w stanie ustalonym). Gdy powstanie panel „Dlaczego?” w kliencie, serwer wyśle
  dziennik w widoku gracza (tylko jego decyzje), a kronika dostanie wpisy dla ważnych decyzji (wojna, sojusz).
- **Doradcy działają po kolei** (gubernator, skarbnik, uczony), a każdy przechodzi cywilizacje po id, jak wcześniej. Kolejność wydania komend się
  nie zmienia, więc nie zmienia się też ich kolejność rozstrzygania.

## Konsekwencje

- Złoty hash w `DeterminismTests` i hashe przebiegów 1000 tur (seed 7 i 11) są identyczne jak przed zmianą.
- Nowe działania w etapie 4 (wojna, umowy, zwiad) dodają tylko czynniki i doradców, bez nowego sposobu liczenia.
- Wartości bazowe muszą być na tyle małe, żeby `wartość × czynnik` mieściła się w `long` (do ok. 9·10¹⁵ przy czynniku 1000); wartość technologii
  to 10¹⁵ setnych punktu.
- Dziennik nie przetrwa wczytania gry. Jeśli kiedyś AI ma pamiętać swoje decyzje (plany z GDD 8.6), będzie to osobny stan w zapisie.
