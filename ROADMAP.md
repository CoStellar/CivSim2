# CivSim 2 — Roadmapa

**Zakres:** budujemy wyłącznie funkcje oznaczone w `docs/GDD.md` (rozdział 16) jako **CORE** i **CORE (uproszczone)**.
Moduły to przyszłe aktualizacje i nie wchodzą do tego planu. Kultury i religii nie ma w grze.

**Kierunek od 2026-11-09 (ADR 0024, 0025): gra na żywo**, a od 2026-11-13 (ADR 0027) **symulacja jest panem, gracz sternikiem**: pięć dźwigni
(GDD 1.2), każdą można oddać AI, sterowanie oddziałami to narzędzie, nie filar; backend liczy się w taktach i pulsach, lata to tylko licznik. Kolejność: backend na żywo (etap 6),
epoka brązu (etap 7), grywalne demo solo (etap 8), gra w sieci (etap 9), potem kolejne ery. Dawne etapy 6–13 (ery 3–9, tryb gracza
na końcu) są zastąpione. Etap kończy się dopiero po spełnieniu kryterium „Gotowe”. Szczegóły zasad: `docs/GDD.md` (1.1, 6.4).

**Aktualny etap: 6 (backend gry na żywo): plan w `docs/prompts/09-etap-gra-na-zywo.md` (T1–T14) i `docs/prompts/10-plan-gry.md` (kolejność od PR 4a, ADR 0026); gotowe PR 1, 2, 3a, 3b, bitwa w czasie, T11 (powtórka), T14 (schematy); następne: 4a zadania, P prototyp sterowania, T tempo i skala**

---

## Etap 0 — Fundamenty

Gotowe, gdy: 10 000 pustych tur przechodzi bez okna, ten sam seed daje ten sam hash stanu, CI jest zielone na 3 systemach.

- [x] Solucja .NET `CivSim2.sln` z projektami: `CivSim.Core`, `CivSim.Data`, `CivSim.Protocol`, `CivSim.Server`, `CivSim.Headless`, `CivSim.Tests`
- [x] `Directory.Build.props`: nullable, ostrzeżenia jako błędy, deterministyczny build, wspólna wersja .NET (aktualny LTS)
- [x] Deterministyczny PRNG z nazwanymi strumieniami + testy (rozkład, powtarzalność, niezależność strumieni)
- [x] Typ `Amount` (liczby całkowite w setnych częściach) + testy arytmetyki i zaokrągleń
- [x] Szkielet komend: `ICommand`, kolejka, rozstrzyganie w stałej kolejności z rotacją cywilizacji co turę
- [x] Szkielet tury: 11 faz z `docs/GDD.md` (rozdz. 11.1), na razie puste, z pomiarem czasu każdej fazy
- [x] Stan gry z listą światów (świat 0 = Ziemia) i publikacją niezmiennych migawek (double buffer)
- [x] Hash stanu gry do testów determinizmu
- [x] Granica klient–serwer: `CivSim.Protocol` (wersjonowane wiadomości, interfejs transportu, transport w pamięci), `CivSim.Server` uruchamiany lokalnie, komendy z identyfikatorem gracza + test „komenda przez protokół = ten sam stan co bezpośrednio”
- [x] Zapis i odczyt (MessagePack) z wersją formatu + test „zapisz, wczytaj, porównaj hash”
- [x] `CivSim.Headless`: `--seed --turns --map-size --report out.csv`
- [x] Benchmark 10 000 pustych tur: czas tury i pamięć w raporcie
- [x] GitHub Actions: build + testy na Ubuntu, Windows, macOS; benchmark na Ubuntu
- [ ] Understand Anything: instalacja, pierwszy `/understand`, `--auto-update`, graf w repo
- [x] `docs/systems/turn-loop.md`, `docs/systems/determinism.md`, `docs/systems/client-server.md`

## Etap 1 — Mapa

Gotowe, gdy: 100 map na różnych seedach generuje się bez błędów walidatora, a Patryk obejrzał je jako PNG.

- [x] Moduł `HexTorus`: współrzędne osiowe, flat-top, zawijanie w obu osiach, sąsiedzi, odległość, linia, okręgi
- [x] Fragmenty 32×32 i flagi zmian
- [x] Generator: kontynenty, wysokość, poziom morza, temperatura (równik w środku, bieguny na szwie), wilgotność, biomy
- [x] Rzeki na krawędziach heksów, jeziora, żyzność
- [x] Złoża z głębokością, pozycje startowe z testem uczciwości (cuda natury pominięte: w GDD 16 to „Moduł”)
- [x] Biomy i surowce w `data/` (JSON) + walidator
- [x] Rozmiary map: testowa, standard, duża, ogromna
- [x] Eksport mapy do PNG z `CivSim.Headless` (kolorowe heksy, rzeki, złoża), żeby Patryk oceniał mapy bez Godota
- [x] Test 100 seedów + benchmark generowania mapy ogromnej
- [x] `docs/systems/map.md`, `docs/systems/worldgen.md`

## Etap 1b — Świat jak planeta (dodany 2026-11-05)

Gotowe, gdy: mapa zachowuje się jak kula, a Patryk obejrzał presety Ziemia, Mars i Uran oraz własne ustawienia jako PNG.

- [x] Decyzja: walec heksów z przejściem przez bieguny (ADR 0012, GDD 3, zasada nr 8 w `CLAUDE.md`)
- [x] Topologia kuli w `HexGlobe` (dawniej `HexTorus`); rozmiary map 66×40, 258×160, 386×240, 514×320; rzędy o równej powierzchni
- [x] Parametry planety: nachylenie osi, długość roku, jasność gwiazdy, mimośród; presety Ziemia, Mars, Uran w `data/planets.json`
- [x] Klimat, uśrednione pory roku i sezon wzrostu z nasłonecznienia; biomy i dzika żywność biorą je pod uwagę
- [x] Eksport PNG (nowe widoki: pory roku, sezon wzrostu, globus) i podgląd (bez zawijania pionowego, nowe nakładki)
- [x] Dwa zegary (ADR 0013): pory roku na żywo przy długim roku, wolne decyzje raz na turę ery, AI ocenia wygodę temperatury (efekt `cold_tolerance`)
- [ ] Patryk ogląda mapy trzech presetów i własnych ustawień
- Pomysł na później: „klocki pomocnicze” na biegunach (pola o innym kształcie i rozmiarze, które łączą wszystkie heksy polarnego rzędu), żeby przejście przez biegun było dokładne.

## Etap 1c — Gwiazda, atmosfera i wahania klimatu (decyzje z 2026-11-05)

Gotowe, gdy: Ziemia, Mars i Uran liczą się z prawdziwych wzorów (gwiazda, orbita, atmosfera), a w trakcie gry przychodzą epoki lodowe.
Szczegóły i wzory: `docs/prompts/02-etap-1c-gwiazda-atmosfera.md`.

- [x] 1c-A: jasność gwiazdy, odległość i rok (Kepler, masa z jasności), strefa zamieszkiwalna z dopasowaniem do granicy
- [x] 1c-A: atmosfera z cyklu węglowego (wulkanizm, CO₂ w równowadze, efekt cieplarniany i ciśnienie z CO₂) i model bilansu energii zamiast obecnego wzoru (ADR 0014)
- [ ] Patryk ogląda mapy trzech presetów po 1c-A
- Na później: CO₂ z działań cywilizacji (spalanie, wycinanie lasów) przez mechanizm efektów w erze przemysłowej; wietrzenie liczone z prawdziwego lądu na mapie
- [x] 1c-B: cykle Milankovicia (start w ciepłym momencie), lądolody Paillarda na prawdziwym lądzie, zmienność gwiazdy: klimat, biomy i dzika żywność zmieniają się co 500 lat gry (ADR 0015)
- [x] 1c-C: poziom morza zmienia się w trakcie gry: −120 m w pełni zlodowacenia, do +70 m po stopieniu lodu lądowego ze startu, pomosty lądowe, lód morski zmienia się z klimatem, osady z zalanych pól przenoszą się, ruiny pod wodą (ADR 0015)

## Etap 2 — Podgląd symulacji (Godot)

Gotowe, gdy: podgląd działa w 60 FPS na mapie ogromnej przy symulacji w tle.

Stan: wszystko zrobione; bez karty graficznej klient potrzebuje ok. 3 ms na klatkę na mapie ogromnej.
Został pomiar FPS na komputerze Patryka. Nakładki właściciela i strefy wpływów zapełnią się w etapie 3.

- [x] Projekt Godota 4 (.NET) w `game/`, pełny ekran, kamera, zoom, zawijanie mapy przy przewijaniu
- [x] Podgląd łączy się z lokalnym serwerem przez `CivSim.Protocol` (nie czyta rdzenia bezpośrednio)
- [x] Rysowanie kolorowych heksów z widoku od serwera, tylko widoczne fragmenty
- [x] Inspektor pola, prędkości symulacji (pauza, ×1, ×2, ×5, maks.), kronika, proste wykresy
- [x] Nakładki: biomy, żyzność, złoża, właściciel, strefa wpływów
- [x] `docs/systems/viewer.md`

## Etap 3 — Gospodarka i ludność (ery 1–2)

Gotowe, gdy: plemiona same zbierają, wędrują, osiedlają się i rosną przez 2000 tur bez załamań.

**Próba 2000 tur (2026-11-05, z 3h, mapa standard, 12 cywilizacji):**

| Seed | Ludzie po 2000 turach | Żywe cywilizacje | Wioski | Średnia tura |
| --- | --- | --- | --- | --- |
| 7 | 110 tys. | 11 | 91 | 28 ms |
| 11 | 68 tys. | 12 | 63 | 23 ms |
| 23 | 153 tys. | 9 | 116 | 29 ms |
| 42 | 105 tys. | 9 | 76 | 26 ms |

We wszystkich przebiegach ludność rośnie skokowo po rolnictwie (tury 200–300), potem trzyma się stabilnie. Nie ma załamań ani wymierania; pojedyncze cywilizacje znikają po drodze. Wszystkie stoją w neolicie, bo technologie epoki brązu jeszcze nie istnieją w danych. Czas tury jest daleko poniżej celu 50 ms.

Plan: 8 PR-ów (3a–3h). Gotowe: **3a** (towary, magazyny osad z jedną pojemnością, psucie żywności na rok, receptury w danych, mechanizm efektów, czas gry w miesiącach), **3b** (3 warstwy ludności z potrzebami, zbieranie z okolicy osady, wzrost, głód, jedna wartość zdrowia, nastrój), **3c** (strefy wpływów, wyczerpywanie i odrastanie dzikiej żywności, lata dobre i złe, przenoszenie obozów, odłamy, prosta AI plemienna), **3d** (osady jako majątek), **3e** (sieć handlowa bez transportu, ceny wewnętrzne, produkcja z receptur, priorytety przy niedoborach, drewno, krzemień i kamień ze zbierania), **3f** (14 technologii er 1–2, nauka z ludności, inspiracje, dyfuzja od sąsiadów, ery z technologii), **3g** (pamięć mapy, zwiad, wędrówki), **3h** (susza, pożar, powódź, epidemia z danych). Podbój przy migracji przechodzi do 3d.

- [x] Zasoby, magazyny osad (jedna pojemność), psucie żywności, sieć handlowa bez fizycznego transportu (koszt zależny od odległości), receptury jednoetapowe, ceny wewnętrzne, priorytety przy niedoborach (3e, `docs/systems/economy-trade.md`)
- [ ] Ludność: 3 warstwy (wytwórcy, rzemieślnicy, wojownicy), potrzeby, wzrost, głód, jedna wartość zdrowia, migracje
- [x] Koczownicy, obozy, strefa wpływów, migracje (sezonowe, odłam, całkowita, z podbojem)
- [x] Osady jako majątek: porzucanie, dwa stany (opuszczona, ruiny), przejmowanie, odbudowa, roszczenia (odbudowa budynków razem z budynkami)
- [x] Technologie er 1–2 w danych, badania, dyfuzja, inspiracje (3f, `docs/systems/research.md`)
- [x] Osiedlanie się: pola uprawne z rolnictwa, obozy stają się wioskami, wioski rosną ponad 150 osób, ziemia jałowieje (`docs/systems/farming.md`)
- [x] Zwiad i pamięć mapy (pamięć ustna; mapy rysowane z pismem w erze brązu), mgła wojny, filtrowanie widoku gracza na serwerze (3g, `docs/systems/perception.md`); wędrówki trwające kilka tur do znanych dalekich miejsc, także po lodzie morskim i pomostach lądowych z epok lodowych (żeby na planetach leżących na boku plemiona mogły iść za latem na drugą półkulę)
- [x] Mechanizm efektów + zdarzenia: susza, pożar, powódź, epidemia (3h, `docs/systems/events.md`; szansa z warunków świata, łagodzenie technologiami, epidemia po sieci handlowej, powódź zostawia żyzną ziemię)

## Etap 4 — AI i dyplomacja

Gotowe, gdy: 100 gier AI kontra AI daje sensowne historie (wojny, sojusze, upadki) bez absurdów. **Brama: demo symulatora.**

Punkt startu, proponowany podział na PR-y (4a–4g) i pytania do decyzji: `docs/prompts/03-etap-4-ai-dyplomacja.md`.

Gotowe: **4a** (wspólna ocena działań: czynniki w tysięcznych z krzywych, waga celu, charakter, koszt; wybór z 1–3 najlepszych; rada doradców:
gubernator, skarbnik, uczony; dziennik „Dlaczego?” i `--decisions` w Headless; symulacja bit w bit taka sama jak w etapie 3; `docs/systems/ai.md`, ADR 0016),
**4b** (charakter z 8 cech, strateg z wagami celów i kierunkiem ekspansji, losowanie z 3 najlepszych zależne od ostrożności, histereza receptur, rzadsze szukanie miejsc; ADR 0017),
**4c** (odkrywanie cywilizacji: zwiad, zetknięcie stref, napaść; pogłoski od znajomych; pamięć wydarzeń z zanikaniem, sympatia i zaufanie; spór o przejętą osadę;
opinia o sąsiedzie w decyzji o wypędzeniu obozu; wpisy w kronice; `--relations` w Headless; `docs/systems/relations.md`, `data/relations.json`),
**4d** (dyplomacja i handel między cywilizacjami: wojna jako stan, pokój z rozejmem, umowa handlowa, pakt o nieagresji, sojusz obronny; dyplomata AI składa
propozycje, a druga strona ocenia je tą samą oceną; złamanie umowy psuje opinię u poszkodowanego i świadków; sojusznik przystępuje do wojny albo zawodzi;
handel barterowy między najbliższymi osadami partnerów bez karawan; handel buduje sympatię i zaufanie, przenosi technologie i epidemie; wspólny wróg;
`--treaties` w Headless; `docs/systems/diplomacy.md`, `data/diplomacy.json`),
**zegar** (przed 4e, decyzja Patryka z 2026-11-06, ADR 0018: licznik lat świata, każda cywilizacja liczy się i decyduje we własnym rytmie swojej ery,
kroki przypięte do kalendarza bez utraty lat; decyzje reaktywne na wojnę, napad, propozycję i koniec badania; odstępy AI w własnych krokach; zapis w formacie 19),
**B: wartości** (decyzja Patryka z 2026-11-06, ADR 0019: każda opcja służy celowi; uczony liczy priorytet technologii z położenia, braków i
charakteru, a koszt jest przeszkodą łagodzoną ciekawością; skarbnik koordynuje osady sieci, jedzenie kontra wyroby, premia za handel;
strateg: waga nauki i wpływu; obce obozy tylko z pól widzianych teraz; liczby strojenia w `data/ai.json`; test martwych wartości),
**C: ziemia i migracje** (gubernator wycenia ziemię z technologiami cywilizacji i wodą dla rybaków; plon rolny dla prawdziwej liczby rolników,
osiedlenie porównane ze zbieractwem i ekspansywnością; wioska wysyła osadników, gdy jest pełna albo ma wyjałowione pola; wyprawy w nieznane
dla ciekawych i ekspansywnych; zwiadowcy i marsz na stulecie; kolumny `settlements` i `spread_cells` w statystykach Headless).

Gotowe też: **etap 4F** (ludzie i fachy, niżej, decyzja Patryka z 2026-11-06: przed wojną) i **4e** wojna (niżej, decyzje z 2026-11-07). Teraz: **4f** państwo (niżej), potem **4g** (100 gier i raport), a dopiero potem nowe ery (decyzja Patryka z 2026-11-07).
Przeprowadzka całej wioski (z planu C) jeszcze nie: czeka na koszt porzucanych pól i budynków (etap budowli).

- [ ] Doradcy: strateg, dyplomata, skarbnik, gubernatorzy, dowódca, uczony
- [ ] Mapy wpływów, prognozy zasobów, kierunek ekspansji (kierunek ekspansji z wartości wolnej ziemi gotowy w 4b)
- [ ] Ocena działań (utility AI) — dopracowanie na podstawie testów wsadowych (szkielet gotowy w 4a)
- [ ] Charakter cywilizacji i przywódcy, sukcesja (charakter cywilizacji gotowy w 4b)
- [ ] Odkrywanie cywilizacji (zwiad, kontakt, pogłoski), pamięć relacji, umowy (pokój, wojna, handel, sojusz), wojna z celem, wynikiem i zmęczeniem, armie na jednym polu, uproszczone zaopatrzenie, plemiona niezależne
- [ ] 3–4 ustroje z tempem decyzji (ustrój zmienia spadek stabilności), stabilność, bunty, secesja, administracja
- [ ] W 4f: **tytuł ustroju w nazwie cywilizacji** (wzór tytułu w danych ustroju + rdzeń z języka ludu, GDD 5.6 i 7.2) i **język nowej cywilizacji z secesji** dziedziczony po rodzicu z mutacją (`NameLanguage.Mutate`, decyzja Patryka z 2026-11-06)
- [x] **Nazwy z języków ludów** (GDD 5.6, `docs/systems/names.md`): język na cywilizację z `data/names.json`, nazwy ludów i osad, zmiana nazwy przez obserwatora z propozycjami w tym samym języku
- [ ] Panel „Dlaczego?” i wpisy w kronice

## Etap 4F — Ludzie, fachy i przyczyny rozwoju (decyzja Patryka z 2026-11-06)

Przed 4e (wojna potrzebuje kowali i broni). Zasady: GDD 5.1, 5.2, 6.1, ADR 0020. Plan, graf i liczby: `docs/prompts/06-etap-fachy.md`.

Gotowe, gdy: w 3 grach po 1000 tur technologie przychodzą dopiero po zebraniu fachowców, ludzie uczą się fachów i przenoszą je między osadami, przyrost w epoce kamienia jest ok. 3 razy wolniejszy, a śmiertelność dzieci zmienia się z technologiami według danych historycznych.

- [x] **PR 1:** fachy w danych (`data/skills.json`) i stanie osad; przycinanie do ludzi warstwy; kolumny w `--stats`; zapis (format 21); gra bez zmian (ten sam hash)
- [x] **PR 2:** nauka fachu (mistrzowie, uczniowie za pół, lata nauki), przenoszenie między osadami sieci, utrata fachu; plony i receptury liczą fachowców
- [x] **PR 3:** warstwy myślicieli i przywódców (starszyzna, wódz); wymogi ludzi w technologiach (`requiresPeople`), nauka głównie od myślicieli; AI: uczony widzi braki ludzi (decyzja AI „ile uczyć” odłożona: nauka fachu to reguła systemu, do decyzji Patryka)
- [x] **PR 4:** urodzenia, śmiertelność dzieci i zgony dorosłych jako efekty technologii (dane historyczne: start +1,0‰ na rok, ogień +1,6‰, garncarstwo +2,2‰, wioska −2‰); wykres ludności przed/po (`docs/ai/img/ludnosc_przed_po.png`)
- [x] **PR 5:** graf technologii i fachów rysowany z danych (`docs/systems/graf-technologii.md`, skrypt `docs/systems/tools/tech_graph.py`); test osiągalności od zera i braku pętli (`TechnologyGraphTests`, sprawdzenie przy wczytywaniu danych); `docs/systems/skills.md`, `docs/ai/` (statystyki z 3 seedów po całym etapie)

## Etap 4e — Wojna (decyzje Patryka z 2026-11-07)

Zasady: GDD 6.2, 9.3, ADR 0021 i 0022 (epizody: kampanie co miesiąc wewnątrz długich kroków, decyzja Patryka z 2026-11-07). Plan: `docs/prompts/07-etap-wojna.md`.

Gotowe, gdy: w 3 grach po 1000 tur wojny mają cel i kończą się pokojem z warunkami, wyprawy maszerują z zaopatrzeniem, bitwy liczą teren,
morale i uzbrojenie z technologii, dowódca nie rusza na przegraną wojnę, a zmęczenie wojną widać w nastroju.

- [x] **PR 1:** cel wojny (ziemia, łupy, odwet), wynik i zmęczenie (efekt na nastrój), pokój z warunkami (osada, okup, biały pokój), wspomnienie „napaść bez powodu”; dzisiejsze napaści liczą się do wyniku; `data/war.json`, zapis, hash, `--wars` w Headless; dyplomata wybiera cel i warunki
- [x] **PR 2:** epizody (krótki zegar w miesiącach, kroki pełne i kroki epizodów, `WorldMonths`, zapis, hash, determinizm) i pierwsza kampania: wyprawa wojowników z jednej osady, marsz po mapie, walka u celu, powrót z łupami; wojna bez kampanii wygasa po 5 latach; zmęczenie z miesięcy kampanii i strat; prosty wybór kampanii przez AI z progiem szansy (kampanie w migawce dla podglądu: przeniesione do PR 3)
- [x] **PR 3:** kampanie w migawce i w podglądzie; uzbrojenie z technologii (gołe ręce, drewniane włócznie, proce, kamienne groty; wpisy w danych); wyprawy z wielu osad (1/3 zostaje w domu), limit wodza i ludzi na polu z danych er (front na kilku polach gotowy na przyszłe ery); tragarze z jedzeniem po trasie, głodna wyprawa napada wioski (zrobione: `feat/war-armies`; dowódca nie wysyła armii bez jedzenia na miesiąc, raport `--campaigns`, zapis w formacie 24, protokół 9)
- [x] **PR 4:** bitwa w rundach (teren, morale, uzbrojenie, najedzenie, ucieczka, straty), podbój wioski z ludźmi, ucieczka z obozu (zrobione: `feat/war-battles`, `data/war.json` `battle`, zapis w formacie 25)
- [x] **PR 5:** AI: dowódca (spodziewany wynik bitwy z tego, co widzi, próg szansy w `data/ai.json`; zbiórka, marsz, atak, czekanie, odwrót, obrona co miesiąc kampanii); test martwych wartości; `docs/ai/`. Zrobione: bitwa w głowie bez losowania, droga i jedzenie, odwołanie co miesiąc (K2), atak miesiąc po dojściu. Czekanie i obrona armią: z wieloma armiami (PR 6 albo późniejsze ery)
- [x] **PR 6:** strojenie i statystyki z 3 seedów (wojny, kampanie, bitwy, straty, zmęczenie), `docs/systems/war.md` z sekcją „Co erę”. Zrobione: zaopatrzenie (głód 42% → 23% miesięcy wypraw), jedzenie na całą wyprawę, czynnik „wynik wojny”, długi przebieg 10 000 tur; cicha wojna zostaje białym pokojem (decyzja Patryka 2026-11-07)
- [ ] Po etapie 4e: susza, głód, epidemia i wyprawy odkrywców jako epizody (ADR 0022)

## Etap 4f — Państwo: ustroje, przywódcy, stabilność, secesja (decyzja Patryka z 2026-11-07: przed nowymi erami)

Zasady: GDD 7.2, 7.3, 7.5, 5.6; ADR 0023. Plan i pytania do decyzji: `docs/prompts/08-etap-4f-panstwo.md`
(dokument „Etap 4f: państwo — propozycja” w Claude Docs).

Gotowe, gdy: w 3 grach po 1000 tur cywilizacje mają ustroje i przywódców, stabilność spada przy zbyt dużym państwie, a dalekie osady
odłączają się jako nowe cywilizacje z językiem rodzica i własnym tytułem.

- [x] **PR 1:** ustroje w danych (`data/governments.json`: rada starszych i wodzostwo od startu, despotia i republika miejska od brązu), ustrój z charakteru na starcie, tytuł w nazwie („Chiefdom of Karrut”) w podglądzie i raporcie, opóźnienie decyzji w latach (zmiana ustroju, armie po wypowiedzeniu wojny), tempo i ostrożność AI; zapis w formacie 26, protokół 10 (`feat/state-governments`)
- [x] **PR 2:** przywódca z imieniem z języka ludu, wiekiem i cechami (`data/leaders.json`); starzenie i śmierć rok po roku, sukcesja według ustroju (rada wybiera pasującego, wódz: kryzys, konsul: kadencja); cechy przywódcy przesuwają charakter; przywódca w panelu podglądu; zapis 27, protokół 11 (`feat/state-leaders`)
- [x] **PR 3:** stolica, administracja (koszt drogi od stolicy), stabilność 0–100 z nastroju ważonego wpływem warstw; niepokoje poniżej 30 (wydajność −1% za punkt), bezkrólewie 2 lata po zmianie ustroju, lojalność osad; zapis 28, protokół 12 (`feat/state-stability`)
- [x] **PR 4:** bunty poniżej 15: przewrót w rdzeniu albo secesja dalekich osad jako nowa cywilizacja (język z mutacją, własny ustrój i tytuł); rodzic pamięta secesję (powód odwetu); zapis 29, protokół 13 (`feat/state-revolts`)
- [x] **PR 5:** AI państwa: strateg hamuje ekspansję przy przeciążeniu, w niepokojach dba o bezpieczeństwo i wybiera ustrój pasujący do ludu (S2, histereza 30%); dyplomata unika wojny w niepokojach i bezkrólewiu; pokój zamyka rachunek krzywd (koniec pętli odwetu); test martwych wartości, `docs/ai/`, statystyki z 3 seedów; zapis 30 (`feat/state-ai`)

## Etap 4g — Testy wsadowe: 100 gier i raport (GDD 8.7)

- [x] Tryb wsadowy w `CivSim.Headless` (`--batch N --batch-out games.csv --jobs N`): N gier na kolejnych seedach naraz, wiersz CSV na grę
  (wojny według celu, umowy, podboje, wymarłe ludy, secesje, przewroty, zmiany ustroju, stabilność, ery, głód bez reakcji), każda gra
  bit w bit jak pojedyncza (`feat/batch-games`)
- [x] 100 gier i raport dla Patryka: [`docs/ai/12-testy-wsadowe.md`](docs/ai/12-testy-wsadowe.md), wykres `docs/ai/img/100_gier.png`, dane `docs/ai/dane/100_gier.csv`
- [x] Poprawki z raportu (decyzja Patryka z 2026-11-07: „Poprawki AI wszystkie”): głodna wioska wysyła osadników, mała przenosi się do
  najedzonej sąsiedniej osady; wojny o łupy i o ziemię (wioska, którą armia zdobędzie) bez łamania umów; pakty i sojusze przeciw wspólnemu
  wrogowi. Porównanie 100 gier w `docs/ai/12-testy-wsadowe.md`: głodujące wioski 21 zamiast 322 na grę, łupy w 81 grach, podboje w 19, sojusze 4 na grę (`feat/ai-fixes`)

## Etap 5 — Lepszy podgląd: warstwy, grafiki zastępcze, menu startowe (decyzja Patryka z 2026-11-06)

Zaraz po etapie 4. Cel: łatwiej oceniać symulację, bez grzebania w argumentach uruchomienia w Godocie.

Gotowe, gdy: Patryk uruchamia podgląd, ustawia świat i symulację w menu, startuje grę i ogląda mapę z kilkoma warstwami naraz, na których widać osady, jednostki i złoża.

- [x] **Menu startowe podglądu:** ustawienia świata z GDD 3 (seed, rozmiar mapy, typ świata, procent wody, klimat, wilgotność, rzeźba, zasoby, planeta lub własne parametry gwiazdy i orbity, liczba cywilizacji) i symulacji (prędkość, limit tur, raport CSV); podgląd planety z prawdziwych wzorów; przycisk „Start” uruchamia serwer z tymi ustawieniami; ostatnie ustawienia zapamiętane; argumenty uruchomienia dalej działają
- [x] **Widok warstw:** biomy z terenem jako podkład, warstwy pól mieszane z przezroczystością (wysokość i głębokość wody, cieniowanie rzeźby, temperatura, wilgotność, żyzność, sezon wzrostu, pory roku, dzika żywność, złoża, właściciel, strefy wpływów, pamięć mapy i mgła śledzonej cywilizacji) i warstwy rysowane (rzeki, pola, granice, osady, jednostki, zdarzenia, stosunki); włączanie ikonami w pasku, przezroczystość i kolejność w oknie ustawień warstw; granice po krawędziach heksów
- [x] **Grafiki zastępcze (placeholdery):** skrypt `art/placeholder/generate.py` w palecie, nazwy i rozmiary z GDD 12.3: biomy (3 warianty), wzgórza i góry, cechy terenu, rzeki (6 krawędzi × 2 szerokości), ikony złóż, osady (obóz, wioska, miasteczko × 3 stany), pole uprawne, wędrujący obóz i zwiadowca, znaczniki zdarzeń, ikony warstw i ramki interfejsu; kolor cywilizacji przez magenty i shader; pliki w `art/export/` podmieniają je pod tymi samymi nazwami
- [x] **Śledzenie cywilizacji** (prośba Patryka z 2026-11-06): tabela wszystkich cywilizacji, wykresy ludności, osad i technologii w czasie, panel śledzonej (ludność, osady, jedzenie, badania, charakter, cele, zapasy, stosunki ze wspomnieniami), kamera „Go” i „Track”, kronika tylko o niej
- [x] **Pikselowy interfejs** (prośba Patryka z 2026-11-06): nieprzezroczyste ramki 9-slice z grafik zastępczych, czcionka Pixelify Sans (wcześniej niż w etapie 13), mniejsza kronika, panele do schowania
- [x] **Paczki grafik** (prośba Patryka z 2026-11-06): folder z PNG w `art/packs/` lub w folderze użytkownika, wybór w menu, sprawdzenie nazw, formatu i rozmiarów przy uruchomieniu, lista złych plików w menu, w `art_check.txt` i w konsoli
- [x] Aktualizacja `docs/systems/viewer.md`
- [ ] Patryk ogląda podgląd u siebie (FPS na karcie graficznej, czy warstwy i panele są wygodne)

## Etap 5b — Poprawki po 4-godzinnym biegu (decyzja Patryka z 2026-11-08: „wszystko robimy”)

Przed nowymi erami. Analiza biegu: tura 93 ms (cel 50), FPS ok. 52, 8 z 12 cywilizacji wymiera w 2,5 minuty, 4 neolityczne „imperia” po ok. 140 osad, prawie zero wojen, ok. 67 zdarzeń na turę.

- [x] **Zdarzenia:** epidemie tylko w cywilizacjach od 10 000 ludzi (i osadach od 300), nie przechodzą do mniejszych; susza, pożar i powódź 5× rzadziej
- [x] **Plemiona łowców trwalsze:** „własna ziemia” (sąsiedzi nie zabierają bandzie pól, na których żyje) i łagodniejszy głód w obozach; w 8 grach 0 wymarłych cywilizacji zamiast 2,8
- [x] **Stolice i stabilne nazwy:** stolica przenosi się z obozu do największej wioski, gdy lud osiądzie, potem tylko przy utracie; złoty znak nad stolicą; nazwa cywilizacji nad środkiem skupiska osad wokół stolicy, bez skakania
- [x] **Hamulec neolitu:** zasięg władzy od stolicy, dalekie osady odłączają się jako nowe plemiona; brak imperiów przed epoką brązu (zmiana GDD)
- [x] **Spokojniejszy ekran:** przywódcy jednym komunikatem na turę, drobne zdarzenia w podsumowaniach na cywilizację
- [x] **Wydajność:** płynniejszy obraz (rzadsze i mniejsze przerysowania, mniej sprzątania pamięci), tura < 50 ms (seed 42, 8000 tur: śr. 12 ms, było 41 ms; FPS do sprawdzenia u Patryka)
- [x] **Długi bieg:** koniec, gdy świat stoi w miejscu (10 000 lat bez zmian); test na kontynentach (seed 7): wojna z zemsty tylko na sąsiadów w zasięgu, o połowę mniej wojen bez walki

## Etap 6 — Backend gry na żywo (decyzje Patryka z 2026-11-09, ADR 0024 i 0025)

Cel: sprawdzić w backendzie i w podglądzie deweloperskim, czy gra działa tak, jak chce Patryk, zanim powstanie interfejs gracza.
Plan PR-ów zrobimy na starcie etapu (`docs/prompts/09-etap-gra-na-zywo.md`) i pokażemy Patrykowi.

Gotowe, gdy: na mapie małej 4–6 cywilizacji rozwija się na żywo od neolitu, oddziały chodzą w sekundach, wojna nie zatrzymuje
świata, cywilizacje różnią się tempem zależnie od terenu i kierunku, tempo spełnia cele z ADR 0026 (pierwsza technologia ≤ 1 min,
wioska ≤ 3 min, era 1 ≤ 5 min, 3–6 osad w 15. minucie), a takt mieści się w celu wydajności.
**Brama (ADR 0026): Patryk gra 15 minut w prototypie sterowania i mówi, co nudzi i czego brakuje.**

- [x] **Takt i pulsy** (ADR 0025, PR 1 `feat/live-clock`): stały takt dla akcji, pulsy cywilizacji rozłożone między takty, licznik lat jako etykieta; koniec osobnego zegara epizodów; liczniki wojny w taktach; prędkości ×1/×2/×4; nowe złote hashe, zapis 31, protokół 14 (liczenie pulsów równolegle: PR 8)
- [x] **Czas polityki** (PR 2 `feat/live-pace`, decyzja Patryka z 2026-11-09): rozejmy, propozycje, pamięć krzywd, kryzysy, bezkrólewie, bunty, roszczenia, opóźnienie ustroju i przegląd dyplomaty w latach akcji (rok = 6 s przy ×1); za długie wartości skrócone; **T1** pomiar spóźnionych taktów przy ×4 (0,3–3,3%, spirali zaległości nie ma)
- [ ] **Tempa rozwoju pod cel z GDD 1.1** (przyrost, nauka; dobre AI ok. 15 min do wszystkich technologii brązu): przy etapie 7, gdy będzie epoka brązu (dziś neolit po 6–7,5 min, wszystkie technologie er 1–2 po 7–9 min)
- [x] **Oddziały, część 1** (PR 3a `feat/units`): osadnicy i zwiadowcy jako widoczne oddziały w taktach; droga A* z kosztem terenu z biomów, liczona raz na cel (**T3**); pula oddziałów w płaskich tablicach, takt bez alokacji (**T4**); rozkazy i przejścia (**T5**: stoi, idzie, zakłada, zwiaduje, wraca); dane w `units.json` (**T9**); siatka przestrzenna osad (**T2**); rozkazy gracza i AI (idź, załóż obóz, wracaj); wędrówki obozów pole po polu; wioski już się nie przenoszą (decyzja Patryka z 2026-11-09); podgląd rysuje oddziały płynnie; zapis 33, protokół 15
- [x] **Oddziały, część 2** (PR 3b `feat/units-war`): rodzaje oddziałów w danych z rolą, technologią, zasięgiem walki i własnym terenem (gotowe na strzelców, jazdę, wojska zmotoryzowane); armia i tragarze jako oddziały (wojownicy 0,67 pola/s, decyzja Patryka); zbiórka pieszo, bitwa w zasięgu po miesiącu, ucieczka bez zawracania, odpoczynek wojowników; rozkazy armii (atak na inną osadę, odwołanie); zwiadowcy biorą myśliwych; wartości wojny na miesiąc przeskalowane; zapis 34, protokół 16
- [x] **Bitwa w czasie i wędrówka z pamięcią drogi** (`feat/battle-time`, decyzja Patryka z 2026-11-09, T5): bitwa to faza kampanii z jedną rundą na sekundę (`battle.roundTicks`), armia maleje na mapie, w trakcie bitwy bez odwołania; pokój przerywa bitwę; wędrówka obozu liczy drogę raz na cel i ją zapamiętuje (znany problem 10); zapis 35
- [x] **Powtórka i hasz systemów** (**T11**, `feat/replay`): zapis partii = seed + rozkazy (plik `.civreplay`, Headless `--record` i `--replay`, długi bieg zapisuje powtórkę w swoim folderze); hasz co 600 taktów z podziałem na 9 części stanu; odtworzenie pokazuje pierwszy takt i części, które się różnią (`docs/systems/replay.md`)
- [x] **Dane** (**T14**, `feat/data-schema`): schemat JSON dla każdego pliku w `data/schema/` (literówka w kluczu = błąd przy wczytaniu z podpowiedzią nazwy), F5 w podglądzie wczytuje dane od nowa i zaczyna tę samą partię, złe dane pokazują listę błędów zamiast zamykać podgląd (`docs/systems/data-schema.md`)
- [ ] **Zadania na obszarze** (decyzje Patryka z 2026-11-12, GDD 4.4, plan 4a–4d w `docs/prompts/09`): żywność ze strefy automatycznie, drewno, krzemień, ryby i pola tylko z zadań; jedna grupa robocza na zadanie; budowa w miejscu (palisada); te same komendy dla AI i gracza; mapy wpływu i zagrożenia (**T6**), napaść na grupy robocze; zadania w danych (**T9**)
- [ ] **Prototyp sterowania** (PR P `feat/play-civ`, ADR 0026): „prowadź cywilizację” w podglądzie przez rolę `Player`, widok przez mgłę, rozkazy protokołu klikane na mapie, panel „co AI zrobiła za mnie”, pauza; grafika osady wg technologii, pasek postępu, kamienie milowe w kronice
- [ ] **Tempo i skala** (PR T `feat/pace`, ADR 0026): raport `--milestones` na 10 seedach; przyrost gaśnie z gęstością osad, limit osadników z administracji, gwarancja startu, mapa mała jako domyślna; zamrożenie do brązu: klimat dynamiczny, ustroje i stabilność, wpływ warstw, pakty i sojusze, sieci z cenami, dryf, pożar i powódź, odbudowa ruin
- [ ] **Start** (uproszczony, ADR 0026): losowa kolejność, 15 s z mapą bez złóż i żyzności, strefy blokady, wybór AI z błędem; trzy zdania „historii plemienia"
- [ ] **Technologie (GDD 6.4, ADR 0026):** doświadczenie w technologii, kierunek rozwoju, koszt zależny od rozwinięcia poprzedniej, podgląd technologii (5% × liczba znających z kontaktu, limit 50%); każda technologia odblokowuje jedną widoczną rzecz (`unlocks.visible`); tkactwo i chleb wycięte do brązu
- [ ] **Karty decyzji w backendzie** (ADR 0026): tylko ze zdarzeń w `cards.json`, najwyżej jedna na 30 s, licznik 20 s czasu gry, decyzja AI po czasie; secesja jako karta
- [ ] **Bez postaci z imieniem i z jednym ustrojem (rada starszych) do epoki brązu** (ADR 0026); wódz i ustroje jako karty od brązu
- [ ] **Wydajność:** mapa standard, 6–8 cywilizacji, 300 oddziałów, takt < 30 ms w najgorszym przypadku; klient rysuje tylko zmiany i płynny ruch; sieci handlowe przyrostowo (**T7**), pule pamięci migawki (**T8**), MultiMesh w podglądzie (**T10**), budżet czasu w każdym PR (**T12**, od PR 3a), pulsy równolegle (**T13**), pole przepływu, jeśli pomiar pokaże potrzebę (**T3b**)
- [ ] **Podgląd deweloperski:** wybór miejsca i proste rozkazy do testów, pauza, prędkości ×1/×2/×4
- [ ] Dokumentacja: `docs/systems/` (zegar, oddziały, zadania), `docs/ai/`, `known-issues.md`

## Etap 7 — Epoka brązu

Zaczyna się od „wioska rośnie w miasto, większa wciela mniejszą” (ADR 0026), bo to domyka problem za wielu osad. Potem pogłębiona era 3: technologie, fachy, miasta i awans osad (GDD 5.3), pismo, wytop i metal, fortyfikacje (mur) i zbrojownia, moduł
wojny według „Co erę” w `docs/systems/war.md`.

Osady (decyzja Patryka z 2026-11-09): wioska zostaje tam, gdzie powstała, ale rośnie w miasto i zajmuje coraz większy obszar; gdy
dwie osady zetkną się przez rozrost, większa wciela mniejszą jako swoją część.

Strojenie temp (decyzja Patryka z 2026-11-09: dopracowanie w etapie 7): zwiad oddziałami po PR 3a daje mniej wiedzy i kontaktów niż
dawne wyprawy w pulsie (ok. 450 znanych pól zamiast 1100, neolit 0,6–1,9 min później); dłuższe wyprawy (`tripCells`), więcej grup
na osadę albo inne tempo razem z celem „brąz w 15–40 min”.

Gotowe, gdy: cel „wszystkie technologie epoki brązu” osiąga dobre AI w ok. 15 min, średnie w 20–25 min, słabe w 30–40 min (GDD 1.1).

## Etap 8 — Grywalne demo solo

Interfejs gracza: wybór miejsca na starcie, sterowanie oddziałami, zadania na obszarze, miasta, fortyfikacje i uzbrojenie, kierunek
rozwoju, karty decyzji, delegowanie poziomów decyzji do AI, pauza i prędkości, pasek celu partii, poziomy trudności (tempo AI),
widz po upadku ludu. Pixel art Patryka w miejsce grafik zastępczych tam, gdzie jest gotowy.

Z ADR 0026: dwa przełączniki delegowania (osady same zlecają zadania, dowódca sam prowadzi wojnę) zamiast pięciu poziomów; pasek z erą i minutą partii; jedna liczba na osadę ze strzałką; „dlaczego” przy zdarzeniach; ekran końca partii; poziomy trudności jako błąd AI z `ai.json`.

Gotowe, gdy: da się rozegrać partię solo od neolitu do celu „wszystkie technologie brązu”. **Brama: grywalna gra.**

## Etap 9 — Gra w sieci

Kilku graczy na jednym serwerze (ADR 0008, 0010), prędkość ustala gospodarz, kilka pauz na gracza, karty decyzji nie zatrzymują
świata, rozłączoną cywilizację przejmuje AI, widzowie.

## Kolejne ery

Jedna era na etap, pogłębiona (decyzja Patryka z 2026-11-09: bez cięcia, nawet jeśli to rok pracy): technologie i fachy, nowe
mechaniki tam, gdzie era ich wymaga, moduł wojny „Co erę”, nowy cel na pasku (pierwszy metal, elektryczność…). Kosmos i inne
planety później.

## Aktualizacje po CORE (poza tym planem)

Moduły z rozdziału 16 GDD, m.in.: gra w sieci, era kosmiczna i inne światy, logistyka fizyczna, długie łańcuchy produkcji,
pieniądz i budżet, polityki, armie wielopolowe, oblężenia, karawany na mapie, motywy interfejsu dla er, animacje,
pozostałe zdarzenia. Kolejność ustalimy po ukończeniu CORE.
