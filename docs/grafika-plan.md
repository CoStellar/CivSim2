# Plan grafik: zastępcze teraz, pixel art Patryka później

**Stan:** decyzje Patryka z 2026-11-23 (rozdział 2). Pierwsza paczka grafik zastępczych w 2× jest w grze (rozdział 8). Grafiki zastępcze robi Claude Code (skrypt, paleta, te same
nazwy i rozmiary). Pixel art Patryka podmienia je pod tymi samymi nazwami, po demo albo w trakcie (bez zmian w kodzie: `art/export/`
i paczki w `art/packs/` wygrywają z `art/placeholder/`).

## 1. Co rysuje gra dziś

| Rzecz | Dziś | Źródło |
| --- | --- | --- |
| Biomy (14), wzgórza, góry, rzeki | grafiki zastępcze z etapu 5 (48×42) | `art/placeholder/biome`, `terrain` |
| Budynki (52) | prostokąt w kolorze stanu i dwie litery nazwy („GH”, „Fa”) | kod (`PlayOverlay.DrawBuilding`) |
| Osady (4 poziomy) | kształt i litera poziomu | kod (`DrawSettlement`) |
| Jednostki (19) | znacznik z literą rodzaju i liczbą ludzi | kod |
| Drogi, rów, palisada, mur, akwedukt, mosty | linie i kreski | kod |
| Złoża (22), „???”, znaki rudy | litery i kółka | kod |
| Dobra (29) w pasku i oknach | litera w kółku („F”, „W”, „S”) | kod (`UiKit`) |
| Technologie (70), ery (5), doradcy, myśliciele | tekst | kod |

Pozostałe grafiki zastępcze z etapu 5 (osady „hearth” i „walled”, wędrujący obóz, pole) pochodzą z gry legacy i jej podglądu obserwatora (`MapObjects.cs`). Budynki, osady i jednostki nowej gry rysuje kod z tabeli wyżej.

## 2. Decyzje Patryka (2026-11-23)

- **G1: 2×.** Heks 96×84, miejsce 32×28. Wydajność: grafiki ładują się raz, a mapa z daleka rysuje średnie kolory i znaczniki zamiast obrazków, więc 2× nie zwalnia gry przy oddaleniu. Na testy przybliżenie mapy sięga teraz ×5 (było ×3,5).
- **G2: 3/4 z boku**, światło z lewej góry.
- **G3: grupka figurek, wyodrębniona od heksa pod nią:** obrys, cień pod stopami, pierścień przy zaznaczeniu.
- **G4: jeden wygląd budynku** do demo.
- **G5: wspólne nakładki stanów.**
- **G6: magenta podmieniana w grze.** Patryk dodał: granica i odcień cywilizacji przykrywały grafiki (np. rzekę). Teraz granica jest cieńsza (2,5 zamiast 4), odcień ziemi słabszy (20% zamiast 45%), a rzeka ma ciemniejszy brzeg. **Najważniejsza jest spójność stylu**, potem czytelność.

Pytania z rekomendacjami (zostawione dla historii):

| Nr | Pytanie | Rekomendacja |
| --- | --- | --- |
| G1 | **Rozmiar.** Heks ma 48×42 px, a jedno z 4 miejsc na budynek tylko ok. 16×14 px. To za mało, żeby kuźnia różniła się od garncarza. | **Grafiki w 2× (heks 96×84, miejsce 32×28).** Gra i tak pokazuje mapę mocno przybliżoną, a 2× zmniejszone do 1× nadal jest ostre. Szablon heksa 96×84 zrobię z obecnego 48×42. |
| G2 | **Widok.** Z góry (jak plan) czy 3/4 z boku (dach i ściana)? | **3/4 z boku, światło z lewej góry**, jak dziś w zasadach `art/README.md`. Budynki są wtedy czytelne po sylwetce. |
| G3 | **Jednostki.** Jedna figurka czy grupka? | **Grupka 2–3 figurek na 32×32 (w 2×: 64×64)**, broń i tarcza w kolorze ery. Rozmiar oddziału pokazuje liczba, nie liczba figurek. |
| G4 | **Era w wyglądzie.** Ten sam budynek wygląda inaczej w każdej erze? | **Nie na start.** Jeden wygląd na budynek. Wyjątek: osada (centrum) zmienia się z poziomem (obóz, wioska, miasteczko, miasto). Motyw UI zmienia się z erą. |
| G5 | **Stany budynku.** | **Wspólne nakładki zamiast osobnych grafik:** rusztowanie (budowa), szary filtr (pamięć z mgły), czerwony znak (brak wsadu), dym z komina (praca). Jedna grafika na budynek. |
| G6 | **Kolor cywilizacji.** | Jak dziś: 3 odcienie magenty podmieniane shaderem. Na budynkach: flaga albo dach; na jednostkach: tunika albo tarcza. |

## 3. Pełna lista grafik

Liczby z `data/sim/game.json` (2026-11-23). Rozmiary dla G1 = 2×. Przy 1× wszystkie są dwa razy mniejsze.

### Mapa

| Warstwa | Co | Ile | Rozmiar (2×) | Uwagi |
| --- | --- | --- | --- | --- |
| biom | 14 biomów × 3 warianty | 42 | 96×84 | są zastępcze 48×42; przerobić na 2× |
| teren | wzgórza, góry (po 2 warianty) | 4 | 96×84 | nakładka na biom |
| teren | gaj (drzewa na otwartym terenie), las wg biomu | 6 | 96×84 | `trees`: gaje na trawie, stepie, sawannie |
| teren | rzeka na krawędzi: 6 kierunków × strumień/rzeka | 12 | 96×84 | już są zastępcze |
| prace | droga: ścieżka i droga, 6 kierunków od środka | 12 | 96×84 | składane jak rzeki |
| prace | most | 2 | 96×84 | ścieżka, droga |
| prace | rów, palisada, mur, akwedukt na krawędziach | 4 × 6 | 96×84 | krawędź heksa, składane |
| złoża | 22 złoża + „???” | 23 | 32×32 | ikona w rogu heksa |
| złoża | wielkość: mała, średnia, duża | 3 | 16×16 | kropki obok ikony |
| złoża | znak rudy (poszukiwacze) | 1 | 32×32 | „tu coś jest” |
| obiekty | ruiny osady | 2 | 96×84 | świeże, stare |
| mgła | brzeg mgły, pamięć (szary filtr) | 2 | 96×84 | albo shader |

### Osady i budynki

| Co | Ile | Rozmiar (2×) | Uwagi |
| --- | --- | --- | --- |
| centrum osady: obóz, wioska, miasteczko, miasto | 4 | 96×84 | najważniejsze dla czytelności |
| budynki na 1 miejscu | 19 | 32×28 | chata, garncarz, kowal, kamienny dom, … |
| budynki na 2 miejscach | 20 | 64×28 | spichlerz, stajnia, koszary, młyn, … |
| budynki na całym heksie (4 miejsca) | 13 | 96×84 | pole, pastwisko, kamieniołom, kopalnie, port, zamek, dwór, … |
| nakładki stanów (G5): rusztowanie 3 rozmiary, dym, znak braku, priorytet | 6 | jak budynek | wspólne dla wszystkich |

Budynki według er (do kolejności pracy):
- **epoka kamienia:** gatherers_hut, woodcutter, hut, storehouse, hearth, quarry, toolmaker, hunters_lodge, clay_pit, herbalist, tailor, fishing_hut, raft_landing;
- **neolit:** farm, potter, granary, pasture, stone_house, mason, weaver, trading_post;
- **epoka brązu:** copper_mine, tin_mine, bronze_smith, charcoal_burner, armourer, school, stable, harbour, mint, barracks;
- **epoka żelaza:** iron_mine, blacksmith, weaponsmith, builders_yard, great_storehouse, province_office, siege_workshop, bathhouse, academy, shipyard;
- **średniowiecze:** brickworks, sheepfold, cloth_weaver, manor, steelworks, castle, mill, guildhall, university, sawmill, lime_kiln.

### Jednostki

| Co | Ile | Rozmiar (2×) | Uwagi |
| --- | --- | --- | --- |
| cywile: osadnicy, zwiadowcy, poszukiwacze, mierniczy, tragarze, karawana, mieszkańcy (pospolite ruszenie), myśliwi | 8 | 64×64 | karawana i tragarze z ładunkiem |
| wojsko lądowe: wojownicy, łucznicy, włócznicy, jeźdźcy, miecznicy, kusznicy, rycerze, katapulta | 8 | 64×64 | broń zgodna z erą |
| statki: transportowiec, statek handlowy, okręt wojenny | 3 | 64×64 | na wodzie |
| znaczniki walki: szyk, postawa (atak, obrona, straż), ranni, głód | 6 | 16×16 | ikony nad oddziałem |
| efekty bitwy: strzała, trafienie, szarża | 3 | 32×32 | krótkie animacje 2–4 klatki |

### Interfejs

| Co | Ile | Rozmiar | Uwagi |
| --- | --- | --- | --- |
| ikony dóbr | 29 | 16×16 i 32×32 | pasek zasobów, okna, karawany |
| ikony technologii | 70 | 32×32 | drzewo technologii; 58 podstawowych + 12 ulepszeń |
| kategorie technologii | 8 | 16×16 | podstawy, jedzenie, przemysł, budowa, handel, wojsko, władza, nauka |
| ikony er i ich motyw UI (ramki 9-slice, przyciski w 3 stanach) | 5 × ok. 8 | 16–48 | jak dziś `art/placeholder/ui`, ale na erę |
| alerty (atak, wróg blisko, głód, brak dóbr, brak domów, pełny magazyn, ubrania, racje, materiały) | 9 | 16×16 | |
| zdarzenia (susza, ciężka zima, zaraza, pożar, powódź) | 5 | 32×32 | stare grafiki w `art/legacy/effects` jako odniesienie |
| premie weteranów (Hardened, Shield wall, Forced march) | 3 | 16×16 | |
| portrety: doradcy (4 dziedziny), myśliciele (5 rzadkości) | 9 | 64×64 | ramka koloru rzadkości |
| przyciski paska decyzji (budynki, jednostki, prace) | = budynki + jednostki | 48×48 | mogą brać grafiki budynków i jednostek |

**Razem:** ok. 190 grafik mapy, budynków i jednostek oraz ok. 180 ikon interfejsu. Grafiki zastępcze mogą powstać skryptem w 1–2 sesje.

## 4. Kolejność

1. **Czytelność gry (przed testami demo albo w trakcie):**
   - budynki (52) jako sylwetki w palecie;
   - centra osad (4);
   - jednostki (19);
   - ikony dóbr (29).

   To one dziś są literami.
2. **Mapa:**
   - drogi, mosty, rów, palisada, mur i akwedukt;
   - złoża i „???”;
   - gaje.
3. **Interfejs:**
   - ikony technologii, alerty i zdarzenia;
   - portrety;
   - motywy er.

Pixel art Patryka może iść w tej samej kolejności, jedna era budynków na raz.

## 5. Jak powstaną grafiki zastępcze

- **Skrypt:** `art/placeholder/generate.py` rozszerzony o nową grę: Python + Pillow, tylko kolory z `civsim2_palette.gpl`, stałe ziarna losowania.
- **Budynki:** prosta sylwetka 3/4 (ściana, dach, drzwi) i jeden znak fachu: młot u kowala, dzban u garncarza, kłos na polu. Kolor dachu według ery.
- **Jednostki:** grupka 2–3 figurek z bronią rodzaju. Tunika w magencie cywilizacji.
- **Ikony dóbr i technologii:** proste piktogramy 16/32 px.
- **Opis zestawu:** `manifest.json` z nazwą, rozmiarem i warstwą każdej grafiki. Gra sprawdza paczki według niego, a Patryk ma z niego listę do narysowania.
- **Nazwy:** `building_<klucz>`, `unit_<klucz>`, `good_<klucz>`, `tech_<klucz>`, `settlement_<poziom>`, `work_<rodzaj>_<kierunek>`.

## 6. Inspiracje (do oglądania, nie do kopiowania)

Gry (styl, czytelność, rozwiązania):

| Gra | Na co patrzeć |
| --- | --- |
| *Battle for Wesnoth* | heksy i przejścia terenu, jednostki 72×72 czytelne z daleka |
| *Heroes of Might and Magic III* | obiekty mapy (kopalnie, tartaki) rozpoznawalne po sylwetce i jednym szczególe |
| *Songs of Conquest* | współczesny pixel art strategii, światło, zamki i osady rosnące z poziomem |
| *Age of Empires II* | budynki tego samego rodzaju różne w każdej erze (pomysł na później, G4), mur i palisada na krawędziach |
| *The Settlers II* | tragarze i towary na drogach: logistyka widoczna na mapie |
| *Anno 1404* | ikony dóbr i łańcuchy produkcji; znak „brak surowca” nad budynkiem |
| *Factorio* (tryb ikon) | stan budynku jedną ikoną: brak wsadu, pełny magazyn, brak pracowników |
| *Northgard* | osada z kilku heksów, sezon zimy na mapie |
| *Kingdom: Two Crowns* | ograniczona paleta, nastrój pory roku |

Historia (wygląd budynków i ludzi):
- **epoka kamienia i neolit:** szałasy i ziemianki, Çatalhöyük (domy z płaskimi dachami, wejście przez dach), Skara Brae (kamienne domy);
- **epoka brązu:** Mykeny i Troja (mury z wielkich kamieni), piece do wytopu brązu, rydwany;
- **epoka żelaza:** grodziska celtyckie i Maiden Castle (wały i rowy), okrągłe domy, Biskupin (palisada, drewniane ulice);
- **średniowiecze:** gród z wieżą na kopcu (motte-and-bailey), zamki kamienne, młyny wodne, cechy i ratusze.

Pixel art (technika):
- Lospec: palety i przykłady;
- poradniki Pedra Medeirosa (Saint11) i Raymonda Schlitter (Slynyrd): światło, kontury, małe ikony;
- `art/legacy/`: stare grafiki CivSim jako punkt wyjścia (32×32, ta sama paleta).

## 7. Dla Patryka: jak opisuję każdą grafikę

Dla każdej pozycji z listy: nazwa pliku, rozmiar w px, warstwa, liczba wariantów, paleta `art/palette/civsim2_palette.gpl`, krótki opis
i odniesienie, np.:

> `building_bronze_smith.png`, 32×28 (2×), warstwa obiektów, 1 wariant, paleta CivSim 2. Mały kamienny warsztat z kominem i czerwonym
> żarem w drzwiach, sztaba brązu przy wejściu. Odniesienie: piece do wytopu z epoki brązu; kuźnia z *HoMM III* jako czytelność sylwetki.

## 8. Zasady stylu (spójność przede wszystkim) i stan

Zasady dla grafik zastępczych i dla pixel artu Patryka (skrypt `art/placeholder/game_art.py` ich pilnuje):
- **gęstość 2×:** heks 96×84, miejsce 32×28, jednostka 64×64, dobro 32×32;
- **widok 3/4, światło z lewej góry:** jaśniejsza lewa i górna strona, ciemniejsza prawa i dolna, trzy odcienie na materiał;
- **obrys 1 px** w najciemniejszym brązie (`#1D1710`) wokół każdego obiektu; kafle ziemi bez obrysu;
- **kolor cywilizacji tylko w magentach:** flagi, kalenice dachów, tuniki;
- **dachy według ery budynku:** skóry (epoka kamienia), strzecha (neolit), czerwona dachówka (brąz), łupek (żelazo), ciemny łupek (średniowiecze);
- **znak fachu na jasnej tabliczce:** młot, dzban, kłos, kowadło, płomień, księga…;
- **jednostki:** grupka 2–3 figurek z bronią rodzaju; gra rysuje pod nimi cień;
- **gaje:** okrągłe korony jak w lasach, przy krawędziach heksa, żeby miejsca na budynki zostały czytelne.

Stan (2026-11-23):
- **W grze:** biomy, teren i rzeki w 2×, 52 budynki, 4 centra osad, 19 jednostek i gaje. Na mapie stany budynków pokazują nakładki: rusztowanie, przygaszenie, czerwony znak braku wsadu, złota ramka priorytetu.
- **Gotowe pliki, jeszcze nieużyte:** 29 ikon dóbr. Pasek zasobów i przyciski paska decyzji nadal pokazują litery.
- **Dalej:** ikony w pasku i przyciskach, potem drogi, mury, złoża i interfejs (kolejność z rozdziału 4).

Walidator paczek sprawdza rozmiar każdego pliku z `art/placeholder/manifest.json`. Plik w starym rozmiarze (np. biom 48×42) jest pomijany z komunikatem „must be 96×84 px”.

