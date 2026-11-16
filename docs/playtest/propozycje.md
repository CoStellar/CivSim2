# Propozycje zmian z testów gracza

Każda propozycja jest gotowa do wklejenia agentowi na `development` (format: [`README.md`](README.md)). Dowody: [`2026-11-16_sesja-1.md`](2026-11-16_sesja-1.md).
Priorytety: **P1** przed próbą 15 minut Patryka (brama etapu 6), **P2** w etapie 6, **P3** później.
Pomiar do kryteriów: `tools/CivSim.Playtest --seeds 1-10 --minutes 15` (sprawczość) i `CivSim.Headless --batch 10 --map-size small --milestones` (tempo).

## Podsumowanie

| # | Tytuł | Priorytet | Decyzja Patryka | Stan |
| --- | --- | --- | --- | --- |
| P-01 | Kronika gracza tylko o tym, co jego lud wie; przywódcy wyciszeni do brązu | P1 | nie (realizuje ADR 0024/0026) | nowa |
| P-02 | Osadnicy nie wychodzą na śmierć | P1 | nie (naprawa AI) | nowa |
| P-03 | Wojna gracza: wyprawa musi być możliwa, a cichy pokój nie uderza w gracza | P1 | tak (zasada białego pokoju) | nowa |
| P-04 | Wzięta dźwignia nie zatrzymuje ludu: AI proponuje, gracz zatwierdza | P1 | tak (GDD 1.2) | nowa |
| P-05 | Panel „co AI zrobiła za mnie” po ludzku | P1 | nie | nowa |
| P-06 | Wybór technologii, który coś zmienia | P2 | tak (PR 6) | nowa |
| P-07 | Tempo: wróć do ≥ 30/50 ludów z wioską w 5. minucie | P2 | nie | nowa |
| P-08 | Koniec płaskiej ludności po 4. minucie: cel na 3 minuty | P2 | tak | nowa |
| P-09 | Jedna jednostka czasu w kronice | P2 | nie | nowa |
| P-10 | Odmowa rozkazu mówi, ile brakuje | P2 | nie | nowa |
| P-11 | Bez krzątaniny AI (podziel i połącz w tym samym takcie, zadanie wysłane i odwołane po 12 s) | P3 | nie | nowa |
| P-12 | Domyślny cel w `DeclareWarDto` | P3 | nie | nowa |

---

### P-01: Kronika gracza tylko o tym, co jego lud wie; przywódcy wyciszeni do brązu
- Priorytet: P1
- Stan: nowa
- Dźwignia / test sternika: wszystkie; pytanie 4 („co mówi kronika”) i zasada 10 z `CLAUDE.md` (widok przez mgłę wojny)
- Problem (co widzi gracz): ponad dwa wpisy na sekundę, z czego dwie trzecie to śmierć i następcy przywódców, także ludów, których jego lud nie spotkał. Ważne wpisy (technologia, kontakt, wioska) giną.
- Dowód: seed 7, lud 1, 15 min: 2073 wpisy, 1405 o przywódcach, 18% o własnym ludzie, ok. 56 ważnych; wpisy o obcych od 0:00 („Unrest spread in the Chiefdom of Broukhudro”). `GameServer.AddChronicle(string)` nie zna ludu wpisu, więc każdy klient dostaje wszystko. Zapis: `docs/playtest/data/2026-11-16_ekran_gracza_seed7_lud1.txt`.
- Proponowana zmiana:
  1. Wpis kroniki niesie lud (lub ludy), którego dotyczy (`TurnContext.Chronicle(text, group, civ)` już ma `civ`; przenieść go do `ChronicleEntry` w protokole).
  2. Serwer wysyła graczowi tylko wpisy o jego ludzie i o ludach, które jego lud spotkał (`Relation.HasMet`); wpisy świata (pogoda na jego ziemi) po polach z jego pamięci mapy. Obserwator dalej widzi wszystko.
  3. Do epoki brązu nie pisać do kroniki śmierci i następstwa przywódców (ADR 0024: bez postaci z imieniem w pierwszych erach); najprościej flaga w danych, jak inne zamrożenia.
- Kryteria akceptacji: w zapisie ekranu gracza (seed 7, lud 1, styl `ai`) ≤ 20 wpisów na minutę, ≥ 60% o własnym ludzie, 0 wpisów o ludach niespotkanych; test serwera „gracz nie dostaje wpisu o ludzie, którego nie spotkał”.
- Decyzja Patryka: niepotrzebna (realizuje istniejące ADR); limit wpisów na minutę do potwierdzenia w próbie 15 minut.

### P-02: Osadnicy nie wychodzą na śmierć
- Priorytet: P1
- Stan: nowa
- Dźwignia / test sternika: 1 (AI za gracza); pytanie 3 (widoczny skutek: lud nie rośnie bez przyczyny)
- Problem (co widzi gracz): co kilka sekund „Settlers of X starved on the way”; niektóre ludy stoją na ok. 60 ludziach przez 15 minut, bo AI wysyła z nich grupę za grupą na śmierć.
- Dowód: 10 seedów, 15 min, sama AI: 2904 grupy zmarłe w drodze, 917 założonych obozów (tabela w sesji 1, pkt 4). Seed 7, lud 0: 113 grup zmarłych; ten sam lud z graczem, który nic nie robi, ma po 3 minutach 150 ludzi zamiast 63.
- Proponowana zmiana: sprawdzić, skąd wychodzą zmarłe grupy (hipoteza: głodna osada wysyła osadników zawsze, PR T2, a oni nie niosą jedzenia). Najmniejsza wersja: osadnicy wychodzą tylko z jedzeniem na drogę do celu (jak armia: `carryFoodMonths`), a gdy osada go nie ma, ludzie zostają i głodują u siebie (z przyczyną w kronice raz na osadę, nie na grupę). Jeżeli rozpraszanie nadmiaru z limitu poziomu ma zabijać, niech kronika mówi to wprost raz („X: 40 ludzi odeszło, bo osada nie mieści więcej”).
- Kryteria akceptacji: na 10 seedach ≤ 1 grupa zmarła na 3 założone obozy; mediana ludności po 15 min nie spada o więcej niż 10%; ludy z < 100 ludzi w 5. min: ≤ 5/50.
- Decyzja Patryka: niepotrzebna (naprawa AI), ale zaktualizować `docs/ai/` (gubernator).

### P-03: Wojna gracza: wyprawa musi być możliwa, a cichy pokój nie uderza w gracza
- Priorytet: P1
- Stan: nowa
- Dźwignia / test sternika: 4; pytania 3 i 4
- Problem (co widzi gracz): klika „War” i „War campaign...”, dostaje „Not enough warriors stay home”, po ok. 36 s wojna kończy się sama białym pokojem, a gdy znów wypowie wojnę, kronika mówi „breaking their treaty”.
- Dowód: styl `warlord` (przyciski podglądu: wojna o łup, wyprawa z 1/5 ludzi), 50 ludów: 315 wypowiedzeń, 1112 z 1475 rozkazów odrzuconych, 0 przyjętych wypraw. Zapis: `docs/playtest/data/2026-11-16_ekran_gracza_seed7_lud1_wojownik.txt`.
- Proponowana zmiana:
  1. Przycisk wyprawy wysyła tylu wojowników, ilu osada może wysłać (`Campaigns.Sendable` w widoku osady), a nie 1/5 ludzi; widok osady gracza pokazuje liczbę wojowników do wysłania.
  2. Licznik cichego pokoju (`quietPeaceYears`) nie biegnie dla wojny wypowiedzianej przez gracza, dopóki jego wojownicy się zbierają albo gracz ma wolnych wojowników (albo biegnie od pierwszej możliwej wyprawy).
  3. Pokój zawarty automatycznie (cichy) nie jest umową, której złamanie psuje opinię.
- Kryteria akceptacji: styl `warlord` na 10 seedach: ≥ 1 przyjęta wyprawa u ≥ 80% ludów, które wypowiedziały wojnę; 0 wpisów „breaking their treaty” po cichym pokoju.
- Decyzja Patryka: potrzebna dla punktów 2 i 3 (zmiana zasady wojny z 4e: cichy biały pokój).

### P-04: Wzięta dźwignia nie zatrzymuje ludu: AI proponuje, gracz zatwierdza
- Priorytet: P1
- Stan: nowa
- Dźwignia / test sternika: 1, 2, 3; pytanie 5 i zasada „mało dźwigni, każda mocna” (ADR 0027)
- Problem (co widzi gracz): weźmie dźwignię 2 i zapomni wybrać technologię: badania stoją na zawsze („researching nothing”). Weźmie dźwignię 1, żeby osiąść: od tej chwili nikt nie przenosi jego obozów, gdy kończy się dzika żywność, i lud głoduje.
- Dowód: 50 ludów, 15 min: `absent_research` 0,43 ludzi AI i −7,8 technologii; `absent_land` 0,09; `settle_now` 0,09 i 0 wiosek; `absent` (wszystkie dźwignie) 0,12.
- Proponowana zmiana (najmniejsza): gdy dźwignia gracza nie ma decyzji, której lud potrzebuje, AI **przygotowuje** ją jako propozycję w kafelku („Uczony proponuje: Rolnictwo. Zatwierdź / wybierz inną”), a po N sekundach bez odpowiedzi wykonuje ją jak przy karcie (GDD 1.2: „rekomendowana opcja po 20 s”). Dla dźwigni 1: przeprowadzka obozu przy głodzie to propozycja z licznikiem, nie cisza. Gracz, który chce pełnej kontroli, wyłącza licznik.
- Kryteria akceptacji: `absent_research` i `absent_land` ≥ 0,8 ludzi AI po 15 min; w kafelku zawsze widać, co AI proponuje.
- Decyzja Patryka: potrzebna (zmienia znaczenie „biorę dźwignię” w GDD 1.2 z „AI nie dotyka” na „AI proponuje, ja zatwierdzam”).

### P-05: Panel „co AI zrobiła za mnie” po ludzku
- Priorytet: P1
- Stan: nowa
- Dźwignia / test sternika: wszystkie; pytanie 4
- Problem (co widzi gracz): „Split camp: Bea; goal weight 133% (0), character 82% (0)”, „Set strategy; crowding 57%, fed 1000”, „Set research → Fire; serves security, leads to 387”.
- Dowód: zapis ekranu seed 7, lud 1: 174 wpisy panelu w 15 min, wszystkie w tym formacie.
- Proponowana zmiana: każdy wpis panelu = czynność + jedna przyczyna słowami z największego czynnika („Podzieliliśmy Bea: obóz jest pełny (57%)”, „Badamy Ogień: chroni przed zimnem”). Wagi i liczby zostają w dzienniku `--decisions`. Strategia (lever 2, „Set strategy”) nie trafia do panelu, gdy nic się nie zmieniło.
- Kryteria akceptacji: w zapisie ekranu 0 wpisów z „goal weight”, „character”, „fed 1000”; ≤ 4 wpisy panelu na minutę.
- Decyzja Patryka: niepotrzebna.

### P-06: Wybór technologii, który coś zmienia
- Priorytet: P2 (razem z PR 6 `feat/tech-mastery`)
- Stan: nowa
- Dźwignia / test sternika: 2; pytanie 3
- Problem (co widzi gracz): trzy różne plany badań dają ten sam wynik; plan „szybko rolnictwo” daje mniej wiosek niż AI.
- Dowód: `tech_cheapest` 1,02, `tech_dearest` 1,00, `farm_rush` 1,00 ludzi AI; wioski: AI 22/50, `farm_rush` 17/50.
- Proponowana zmiana: przy PR 6 każda z trzech propozycji uczonego pokazuje jedno zdanie „co zyskasz na swojej ziemi” z liczbą (Sid Meier: decyzja potrzebuje informacji i kosztu). Sprawdzić, czemu rolnictwo wcześnie nie daje wioski (próg osiedlenia `settleFarmYieldPercent`? brak rolników?).
- Kryteria akceptacji: różnica median ludności między najlepszym a najgorszym sensownym planem badań ≥ 20% po 15 min, a `farm_rush` daje ≥ tyle wiosek, co AI.
- Decyzja Patryka: potrzebna przy PR 6 (opisy i liczby w propozycjach).

### P-07: Tempo: wróć do ≥ 30/50 ludów z wioską w 5. minucie
- Priorytet: P2
- Stan: nowa
- Dźwignia / test sternika: tempo (ADR 0026)
- Problem: od T3c mniej ludów osiada, i to później.
- Dowód: ludy z wioską w 15 min: 32/50 (`980f264`) → 24/50 (`274a20c`, T3c) → 22/50 (dziś); mediana minuty 4,0 → 7,2 → 6,4. Pliki `docs/playtest/data/2026-11-16_tempo_*.csv`.
- Proponowana zmiana: rozdzielić dwie zmiany T3c (wspólny magazyn i mapa 98×60) i zmierzyć osobno; możliwe, że wspólny magazyn karmi obozy, więc żaden nie czuje głodu, który popycha do rolnictwa.
- Kryteria akceptacji: `--milestones` 10 seedów: ≥ 30/50 ludów z erą 1, mediana ≤ 5 min.
- Decyzja Patryka: niepotrzebna.

### P-08: Koniec płaskiej ludności po 4. minucie: cel na 3 minuty
- Priorytet: P2
- Stan: nowa
- Dźwignia / test sternika: pytanie 5 („partia bez klikania jest ciekawa”)
- Problem (co widzi gracz): po 4. minucie lud ani nie rośnie, ani nie maleje; mapa się prawie nie zmienia.
- Dowód: mediana ludności AI (50 ludów) 262 w 4. min, 284 w 15. min (wykres `img/3_ludnosc_w_czasie.png`).
- Proponowana zmiana: doradca pokazuje graczowi jeden krótki cel na 2–3 minuty wynikający z sytuacji (Crusader Kings: ambicje; RimWorld: rytm zagrożenie–oddech): „zbierz 200 krzemienia przed zimą”, „osiądź nad rzeką”, „spotkaj sąsiada na wschodzie”. Spełniony cel daje widoczną nagrodę (np. nowa osada, wiedza o mapie). Karty z PR 7 mogą być dolną granicą rytmu: coś co najmniej co 2 minuty.
- Kryteria akceptacji: w próbie 15 minut Patryk wskazuje ≥ 3 swoje decyzje (miara z GDD 1.2); mediana ludności rośnie także między 5. a 15. min.
- Decyzja Patryka: potrzebna (nowa rzecz na ekranie sternika).

### P-09: Jedna jednostka czasu w kronice
- Priorytet: P2
- Stan: nowa
- Problem (co widzi gracz): „warriors will be ready in 1 year” (6 s) i „made peace after 600 years of war” (36 s) w tej samej wojnie.
- Dowód: zapis `warlord`, 0:43 i 1:19.
- Proponowana zmiana: kronika liczy czasy trwania w minutach i sekundach partii (ADR 0026: „pasek pokazuje erę i minutę partii, lata tylko w kronice”), a lata kalendarza tylko jako data wpisu.
- Kryteria akceptacji: 0 wpisów z czasem trwania w latach w zapisie ekranu.
- Decyzja Patryka: niepotrzebna.

### P-10: Odmowa rozkazu mówi, ile brakuje
- Priorytet: P2
- Stan: nowa
- Problem: „Not enough warriors stay home.”, „A settlement needs 60 people to split.” bez liczb z tej osady.
- Dowód: 1112 odrzuconych rozkazów `warlord`; `random`: 2307 odrzuceń.
- Proponowana zmiana: odmowa podaje stan: „Bea can send 4 warriors (you asked for 12).”
- Kryteria akceptacji: każda odmowa komend dźwigni 1 i 4 ma liczbę z osady.
- Decyzja Patryka: niepotrzebna.

### P-11: Bez krzątaniny AI
- Priorytet: P3
- Stan: nowa
- Problem: AI w tej samej sekundzie dzieli obóz i dołącza do niego inny („Split camp: Bea” i „Join camp: Dog → Bea”, 1:54), a grupę po krzemień wysyła w 1:36 i odwołuje w 1:48 („stock months 187”, prawdopodobnie przez wspólny magazyn z T3c).
- Proponowana zmiana: gubernator nie łączy z osadą, którą w tym kroku dzieli; zapas materiału liczony przed i po wspólnym magazynie jednakowo.
- Kryteria akceptacji: w zapisie ekranu 0 par „podziel + połącz” tej samej osady w jednym takcie; żadne zadanie nie jest odwoływane szybciej niż po 30 s.
- Decyzja Patryka: niepotrzebna.

### P-12: Domyślny cel w `DeclareWarDto`
- Priorytet: P3
- Stan: nowa
- Problem: `DeclareWarDto(Target, Goal = 2)` domyślnie wypowiada wojnę o odwet, którą serwer odrzuca bez powodu do odwetu („There is nothing to avenge.”). Podgląd podaje cel jawnie, ale następny klient (etap 8) może się na tym potknąć.
- Proponowana zmiana: cel bez wartości domyślnej albo domyślnie łup (1).
- Kryteria akceptacji: test protokołu.
- Decyzja Patryka: niepotrzebna.
