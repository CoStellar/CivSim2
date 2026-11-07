# CivSim 2 — Roadmapa

**Zakres:** budujemy wyłącznie funkcje oznaczone w `docs/GDD.md` (rozdział 16) jako **CORE** i **CORE (uproszczone)**.
Moduły to przyszłe aktualizacje i nie wchodzą do tego planu. Kultury i religii nie ma w grze.

Priorytet: **symulacja**. Etapy 0–12 to rdzeń, lepszy podgląd z grafikami zastępczymi (etap 5), wszystkie ery i podgląd deweloperski. Pixel art i interfejs gracza
wchodzą na końcu, w etapie 13 (decyzja Patryka z 2026-11-06). Etap kończy się dopiero po spełnieniu kryterium „Gotowe”.
Szczegóły zasad: `docs/GDD.md`.

**Aktualny etap: 3 (gospodarka i ludność): wszystkie PR-y 3a–3h gotowe, próba 2000 tur przeszła; następny etap 4 (AI i dyplomacja)**

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

Gotowe też: **etap 4F** (ludzie i fachy, niżej, decyzja Patryka z 2026-11-06: przed wojną). Teraz: **4e** wojna (niżej, decyzje z 2026-11-07), potem **D** odłamy jako nowe cywilizacje w 4f.
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

Zasady: GDD 9.3, ADR 0021. Plan: `docs/prompts/07-etap-wojna.md`.

Gotowe, gdy: w 3 grach po 1000 tur wojny mają cel i kończą się pokojem z warunkami, wyprawy maszerują z zaopatrzeniem, bitwy liczą teren,
morale i uzbrojenie z technologii, dowódca nie rusza na przegraną wojnę, a zmęczenie wojną widać w nastroju.

- [x] **PR 1:** cel wojny (ziemia, łupy, odwet), wynik i zmęczenie (efekt na nastrój), pokój z warunkami (osada, okup, biały pokój), wspomnienie „napaść bez powodu”; dzisiejsze napaści liczą się do wyniku; `data/war.json`, zapis, hash, `--wars` w Headless; dyplomata wybiera cel i warunki
- [ ] **PR 2:** uzbrojenie z technologii (gołe ręce, drewniane włócznie, proce, kamienne groty; wpisy w danych); wyprawy na mapie: zbiórka (1/3 zostaje w domu), marsz, jedzenie, limit wodza i ludzi na polu z danych er (front na kilku polach gotowy na przyszłe ery); migawka dla podglądu
- [ ] **PR 3:** bitwa w rundach (teren, morale, uzbrojenie, najedzenie, ucieczka, straty), podbój wioski, ucieczka z obozu; tragarze z jedzeniem po trasie marszu; głodna armia napada okoliczne wioski
- [ ] **PR 4:** AI: dowódca (spodziewany wynik bitwy z tego, co widzi, próg szansy w `data/ai.json`; zbiórka, marsz, atak, odwrót, obrona); test martwych wartości; `docs/ai/`
- [ ] **PR 5:** strojenie i statystyki z 3 seedów (wojny, bitwy, straty, zmęczenie), `docs/systems/war.md` z sekcją „Co erę”

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

## Etapy 6–12 — Ery 3–9

Jedna era na etap. W każdym: technologie i zasoby w danych → potrzebne mechaniki z zakresu CORE → AI → podgląd deweloperski → balans w 100 grach.
**W każdym etapie ery zaktualizuj też moduł wojny** (prośba Patryka z 2026-11-07): nowe uzbrojenie, fachy (np. kowale) i jednostki, limit wodza
i liczba ludzi na polu, zaopatrzenie; lista w `docs/systems/war.md`, sekcja „Co erę”.
Kolejne ustroje i warstwy ludności dochodzą razem z erami, w ramach wersji uproszczonych z GDD.

## Etap 13 — Tryb gracza i grafika (na sam koniec CORE)

Decyzja Patryka z 2026-11-06: najważniejsza jest dobrze działająca symulacja, więc tryb gracza to ostatni etap CORE, po wszystkich erach.

Gotowe, gdy: da się rozegrać pełną grę od ery kamienia do ery informacji. **Brama: grywalna gra.**

- [ ] Pixel art Patryka w warstwach w miejsce grafik zastępczych z etapu 5, shader koloru cywilizacji
- [ ] Jeden motyw interfejsu (ramki 9-slice), statyczne grafiki, czcionka Pixelify Sans (GDD 12.4)
- [ ] Rozkazy gracza, delegowanie poziomów decyzji do AI, drzewo technologii, zapis w dowolnym momencie
- [ ] Strojenie wydajności mapy ogromnej
- [ ] Grafiki dla wszystkich er (teren, osady, ulepszenia, jednostki); motywy interfejsu dla er zostają modułem
- [ ] Warunki zwycięstwa: punktowe i bez końca; narzędzia „widoku boga” potrzebne do testów

## Aktualizacje po CORE (poza tym planem)

Moduły z rozdziału 16 GDD, m.in.: gra w sieci, era kosmiczna i inne światy, logistyka fizyczna, długie łańcuchy produkcji,
pieniądz i budżet, polityki, armie wielopolowe, oblężenia, karawany na mapie, motywy interfejsu dla er, animacje,
pozostałe zdarzenia. Kolejność ustalimy po ukończeniu CORE.
