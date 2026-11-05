# CivSim 2 — Roadmapa

**Zakres:** budujemy wyłącznie funkcje oznaczone w `docs/GDD.md` (rozdział 16) jako **CORE** i **CORE (uproszczone)**.
Moduły to przyszłe aktualizacje i nie wchodzą do tego planu. Kultury i religii nie ma w grze.

Priorytet: **symulacja**. Etapy 0–4 to rdzeń i prosty podgląd deweloperski. Pixel art i interfejs gracza
wchodzą od etapu 5. Etap kończy się dopiero po spełnieniu kryterium „Gotowe”.
Szczegóły zasad: `docs/GDD.md`.

**Aktualny etap: 1b (świat), potem 3e**

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

Plan: 8 PR-ów (3a–3h). Gotowe: **3a** (towary, magazyny osad z jedną pojemnością, psucie żywności na rok, receptury w danych, mechanizm efektów, czas gry w miesiącach), **3b** (3 warstwy ludności z potrzebami, zbieranie z okolicy osady, wzrost, głód, jedna wartość zdrowia, nastrój), **3c** (strefy wpływów, wyczerpywanie i odrastanie dzikiej żywności, lata dobre i złe, przenoszenie obozów, odłamy, prosta AI plemienna). Podbój przy migracji przechodzi do 3d.

- [ ] Zasoby, magazyny osad (jedna pojemność), psucie żywności, sieć handlowa bez fizycznego transportu (koszt zależny od odległości), receptury jednoetapowe, ceny wewnętrzne, priorytety przy niedoborach
- [ ] Ludność: 3 warstwy (wytwórcy, rzemieślnicy, wojownicy), potrzeby, wzrost, głód, jedna wartość zdrowia, migracje
- [x] Koczownicy, obozy, strefa wpływów, migracje (sezonowe, odłam, całkowita, z podbojem)
- [x] Osady jako majątek: porzucanie, dwa stany (opuszczona, ruiny), przejmowanie, odbudowa, roszczenia (odbudowa budynków razem z budynkami)
- [ ] Technologie er 1–2 w danych, badania, dyfuzja, inspiracje
- [ ] Zwiad i pamięć mapy (pamięć ustna, mapy rysowane), mgła wojny, filtrowanie widoku gracza na serwerze
- [ ] Mechanizm efektów + zdarzenia: susza, pożar, powódź, epidemia

## Etap 4 — AI i dyplomacja

Gotowe, gdy: 100 gier AI kontra AI daje sensowne historie (wojny, sojusze, upadki) bez absurdów. **Brama: demo symulatora.**

- [ ] Doradcy: strateg, dyplomata, skarbnik, gubernatorzy, dowódca, uczony
- [ ] Mapy wpływów, prognozy zasobów, kierunek ekspansji
- [ ] Ocena działań (utility AI) — dopracowanie na podstawie testów wsadowych
- [ ] Charakter cywilizacji i przywódcy, sukcesja
- [ ] Odkrywanie cywilizacji (zwiad, kontakt, pogłoski), pamięć relacji, umowy (pokój, wojna, handel, sojusz), wojna z celem, wynikiem i zmęczeniem, armie na jednym polu, uproszczone zaopatrzenie, plemiona niezależne
- [ ] 3–4 ustroje z tempem decyzji (ustrój zmienia spadek stabilności), stabilność, bunty, secesja, administracja
- [ ] Panel „Dlaczego?” i wpisy w kronice

## Etap 5 — Tryb gracza i grafika

Gotowe, gdy: da się rozegrać pełną grę do końca ery 2. **Brama: grywalne demo.**

- [ ] Pixel art Patryka w warstwach, shader koloru cywilizacji
- [ ] Jeden motyw interfejsu (ramki 9-slice), statyczne grafiki
- [ ] Rozkazy gracza, delegowanie poziomów decyzji do AI, drzewo technologii, zapis w dowolnym momencie
- [ ] Warunki zwycięstwa: punktowe i bez końca; narzędzia „widoku boga” potrzebne do testów

## Etapy 6–12 — Ery 3–9

Jedna era na etap. W każdym: technologie i zasoby w danych → potrzebne mechaniki z zakresu CORE → AI → grafiki → balans w 100 grach.
Kolejne ustroje i warstwy ludności dochodzą razem z erami, w ramach wersji uproszczonych z GDD.

## Aktualizacje po CORE (poza tym planem)

Moduły z rozdziału 16 GDD, m.in.: gra w sieci, era kosmiczna i inne światy, logistyka fizyczna, długie łańcuchy produkcji,
pieniądz i budżet, polityki, armie wielopolowe, oblężenia, karawany na mapie, motywy interfejsu dla er, animacje,
pozostałe zdarzenia. Kolejność ustalimy po ukończeniu CORE.
