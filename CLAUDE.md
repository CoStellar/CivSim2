# CivSim 2 — instrukcje dla Claude Code

Czytasz ten plik na początku każdej sesji. Jest źródłem zasad pracy w tym repo.
Pełny projekt gry: `docs/GDD.md`. Plan prac i stan: `ROADMAP.md`. Decyzje architektoniczne: `docs/decisions/`.

## Projekt w skrócie

CivSim 2 to turowa gra 4X w pixel arcie na heksagonalnej mapie-planecie (kula: wschód–zachód zawija się, przez bieguny przechodzi się na drugą stronę planety).
Cywilizacje przechodzą od koczowniczych plemion do ery kosmicznej. **Najpierw powstaje symulator**
(wszystkie cywilizacje prowadzi AI), dopiero potem tryb gracza na tych samych zasadach.

## Zakres

Budujemy **tylko** funkcje oznaczone w `docs/GDD.md`, rozdział 16, jako **CORE** albo **CORE (uproszczone)**.
Funkcje oznaczone jako „Moduł” to przyszłe aktualizacje: nie implementuj ich, ale nie blokuj ich architekturą
(dane w JSON, mechanizm efektów, komendy zostawiają na nie miejsce). Funkcji „Wytnij” nie robimy wcale.
CivSim 2 to symulator rozwoju gospodarczego: **nie ma kultury ani religii** jako mechanik.

## Role

- **Ty (Claude Code):** cały kod, testy, pliki danych, dokumentacja techniczna, benchmarki.
- **Patryk (właściciel):** decyzje projektowe, grafika pixel art, ocena, czy symulacja zachowuje się sensownie.
  Patryk nie pisze już w C# ani w Javie. Tłumacz po polsku, prosto, bez żargonu, i pokazuj efekty
  (wykresy, mapy PNG, raporty), a nie tylko kod.

## Języki

- Rozmowa z Patrykiem, plany sesji i opisy PR: **po polsku**.
- Kod, komentarze, nazwy, komunikaty commitów, pliki danych, teksty w grze: **wyłącznie po angielsku**.
- `docs/GDD.md`, `ROADMAP.md`, `docs/decisions/` i `docs/systems/`: po polsku (dla Patryka).

## Jak wygląda każda sesja

1. **Start:** przeczytaj `CLAUDE.md`, aktualny etap w `ROADMAP.md`, odpowiedni rozdział `docs/GDD.md`,
   ostatnie commity (`git log --oneline -20`) i graf wiedzy (`/understand-chat`, gdy potrzebujesz kontekstu).
2. **Plan sesji** (po polsku, zanim zaczniesz pisać kod):
   - **Ja zrobię:** lista zadań z `ROADMAP.md`.
   - **Ty zrób:** grafiki lub decyzje. Grafikę opisuj zawsze tak: nazwa pliku, rozmiar w px, warstwa,
     liczba wariantów, paleta `art/palette/civsim2_palette.gpl`, krótki opis i odniesienie (np. do `art/legacy/`).
   - **Do decyzji:** pytania, bez których nie możesz iść dalej, z Twoją rekomendacją.
3. **Praca:** osobna gałąź `feat/<krótki-opis>` albo `fix/<...>`, małe commity.
4. **PR:** opis po polsku: co, dlaczego, jak sprawdzić, wyniki testów, benchmark, raport `/understand-diff`.
   Nigdy nie pushuj bezpośrednio do `main`. Merge robi Patryk.
5. **Koniec sesji:** odhacz zadania w `ROADMAP.md`, zaktualizuj `docs/systems/`, upewnij się, że graf
   wiedzy jest aktualny, i napisz krótkie podsumowanie: co działa, co zostało, co Patryk ma zrobić.

Jeśli zadanie zmienia zasady z `docs/GDD.md`, **najpierw zaproponuj zmianę Patrykowi**, potem ją wprowadź
w GDD i w kodzie. Nie zmieniaj zasad gry po cichu.

## Architektura — twarde zasady

1. **Rdzeń bez grafiki.** `CivSim.Core` nie ma żadnej zależności od Godota ani od UI. Musi działać w
   `CivSim.Headless` i w testach.
2. **Wszystko przez komendy.** Każda zmiana stanu gry to komenda (`ICommand`). AI i gracz wydają te same
   komendy. Godot nigdy nie modyfikuje stanu bezpośrednio: wysyła komendy, czyta migawki.
3. **Osobny wątek symulacji.** Po każdej turze rdzeń publikuje niezmienną migawkę (double buffer).
   UI czyta tylko ostatnią gotową migawkę.
4. **Determinizm** (ten sam seed + te same komendy = identyczny stan, bit w bit, na każdym systemie):
   - własny PRNG (np. PCG32 lub xoshiro256**) z osobnymi strumieniami na system (`world`, `ai`, `combat`, `events`…);
     **zakaz** `System.Random`, `Guid.NewGuid`, `DateTime.Now` i haszy zależnych od procesu w logice gry;
   - ilości zasobów i ekonomia na liczbach całkowitych (typ `Amount` w setnych częściach jednostki), bez `float`/`double`;
     tam, gdzie liczby ułamkowe są nieuniknione (klimat, oceny AI), używaj fixed-point albo zaokrąglaj deterministycznie;
   - iteracja zawsze w stałej kolejności identyfikatorów; nie polegaj na kolejności `Dictionary`/`HashSet`;
   - AI liczone równolegle wyłącznie czyta migawkę, a jej komendy są sortowane przed rozstrzygnięciem;
   - test determinizmu (hash stanu po N turach dla dwóch przebiegów) musi przechodzić lokalnie przed każdym PR.
5. **Zawartość w danych.** Technologie, zasoby, budynki, receptury, jednostki, ustroje, polityki, zdarzenia,
   biomy i motywy UI są w plikach JSON w `data/`, walidowanych przy starcie. Nowa technologia nie wymaga kodu.
6. **Tempa na rok.** Wszystkie tempa (wzrost, psucie, odrastanie) definiuj na rok i przeliczaj przez długość tury ery.
7. **Wydajność od pierwszego dnia:**
   - pola mapy w płaskich tablicach (struktura tablic), byty identyfikowane liczbami;
   - zero alokacji w głównej pętli tury, pule obiektów;
   - mapa podzielona na fragmenty 32×32, przeliczane tylko przy zmianach (dirty flags);
   - historia i statystyki o stałej pamięci (próbkowanie coraz rzadsze z wiekiem).
8. **Mapa:** heksy flat-top, współrzędne osiowe (q, r), kula (ADR 0012): zawijanie wschód–zachód, przejście przez biegun
   na drugą stronę planety, rzędy jako pasy o równej powierzchni. Szerokość = 4k + 2, wysokość parzysta.
   Wszystkie odległości i sąsiedztwa idą przez jeden moduł `HexGlobe`, nigdy ręcznie.
9. **Wiele światów:** stan gry od początku przechowuje listę światów (planet); Ziemia to świat 0.
10. **Gotowość na multiplayer (klient–serwer).** Gry sieciowej nie budujemy (to przyszła aktualizacja), ale od etapu 0:
    - gra zawsze działa jako klient i serwer; w grze jednoosobowej serwer startuje lokalnie w tym samym procesie,
      a połączenie to transport w pamięci (bez sieci) za tym samym interfejsem, co późniejszy transport sieciowy;
    - klient (`CivSim.Game`) **nie odwołuje się do `CivSim.Core`**, tylko do `CivSim.Protocol`: wysyła komendy, dostaje widok;
    - serwer jest autorytatywny: sprawdza każdą komendę (uprawnienia, zasoby, widoczność celu) i jako jedyny liczy symulację;
    - widok dla gracza jest filtrowany przez mgłę wojny i pamięć mapy jego cywilizacji; klient nigdy nie dostaje pełnego stanu
      (dlatego nie lockstep);
    - każda komenda niesie identyfikator gracza i cywilizacji, nawet w grze jednoosobowej;
    - wiadomości protokołu są wersjonowane i serializowane (MessagePack), z testami zgodności;
    - tura kończy się, gdy wszyscy gracze są gotowi albo minie limit czasu; rozłączoną cywilizację przejmuje AI.

## Struktura repo

```
src/CivSim.Core/        logika symulacji (bez Godota)
src/CivSim.Data/        ładowanie i walidacja JSON
src/CivSim.Protocol/    wiadomości klient–serwer, wersja protokołu, transport w pamięci (sieciowy w przyszłej aktualizacji)
src/CivSim.Server/      serwer gry: tury, sprawdzanie komend, filtrowanie widoczności, zapis (czysty .NET, bez Godota)
src/CivSim.Headless/    uruchamianie bez okna, benchmarki, raporty CSV, eksport mapy do PNG
tests/CivSim.Tests/     testy jednostkowe, determinizmu i długich przebiegów
game/                   klient w Godocie (od etapu 2), zależy tylko od Protocol
data/                   zawartość gry w JSON
art/                    grafiki Patryka (palette/, templates/, legacy/, src/, export/)
docs/                   GDD, decyzje, opisy systemów, prompty
.understand-anything/   graf wiedzy projektu (commitowany)
```

## Testy, benchmarki i CI

- xUnit. Każdy system ma testy jednostkowe.
- **Test determinizmu:** dwa przebiegi z tym samym seedem → identyczny hash stanu.
- **Test długiego przebiegu:** 10 000 tur na stałym seedzie, raport czasu tury i pamięci.
- Cele wydajności (dostrajane po pomiarach):

| Mapa | Wymiary | Cywilizacje | Czas tury | Pamięć po 10 000 tur |
| --- | --- | --- | --- | --- |
| Standard | 256×160 | 12 | < 50 ms | < 1 GB |
| Duża | 384×240 | 20 | < 120 ms | < 1,5 GB |
| Ogromna | 512×320 | 32 | < 250 ms | < 3 GB |

- PR, który pogarsza czas tury lub pamięć o więcej niż 10%, wymaga wyjaśnienia w opisie.
- Testy i benchmarki uruchamiamy **lokalnie** przed każdym PR i wpisujemy wyniki do opisu (decyzja Patryka z 2026-11-05: nie polegamy na CI). Testy mają być krótkie: cały zestaw w kilka sekund, długie przebiegi tylko w benchmarku `CivSim.Headless`.

## Zrozumiałość kodu: Understand Anything

Repo od etapu 0 korzysta z pluginu Understand Anything (graf wiedzy projektu dla Claude Code).

- Instalacja (raz, w Claude Code):
  `/plugin marketplace add Lum1104/Understand-Anything` i `/plugin install understand-anything`.
  To projekt społeczności, nie Anthropic. Przed instalacją pokaż Patrykowi wersję, którą instalujesz.
- Pierwsza analiza: `/understand`. Zużywa sporo tokenów; kolejne analizują tylko zmienione pliki.
- Włącz automatyczną aktualizację po commitach: `/understand --auto-update`.
- Commituj `.understand-anything/` **oprócz** `intermediate/` i `diff-overlay.json` (są w `.gitignore`).
  Jeśli graf przekroczy 10 MB, przenieś go do Git LFS.
- W każdym PR dołącz wynik `/understand-diff`.
- Dla Patryka: `/understand-dashboard` (graf), `/understand-domain` (przepływy, np. fazy tury),
  `/understand-explain <plik>`, `/understand-chat <pytanie>`.

## Dokumentacja techniczna

- Każdy system rdzenia ma plik `docs/systems/<system>.md` według szablonu z `docs/systems/README.md`,
  z diagramem przepływu danych w Mermaid.
- Ważna decyzja techniczna = nowy plik ADR w `docs/decisions/` (numerowany, po polsku).
- Publiczne klasy i metody w kodzie mają krótkie komentarze XML po angielsku.

## Grafika (kiedy prosisz Patryka o pliki)

- Narzędzie Patryka: LibreSprite (pliki `.ase`). Paleta: `art/palette/civsim2_palette.gpl` (45 kolorów ze starego CivSim + 8 rozszerzenia).
- Heks: flat-top **48×42 px**, kolumny co 36 px, rzędy co 42 px, co druga kolumna przesunięta o 21 px.
  Maska: `art/templates/hex_template_48x42.png`.
- Warstwy od dołu: biom → teren (rzeźba, cechy, rzeki na krawędziach) → obiekty → jednostki.
- Kolor cywilizacji: zarezerwowane odcienie magenty `#FF00FF`, `#C000C0`, `#800080` (podmieniane shaderem).
- Do etapu 4 podgląd używa kolorowych heksów generowanych w kodzie; pixel art wchodzi od etapu 5.

## Czego nie robić

- Nie dodawaj religii ani kultury.
- Nie implementuj modułów z rozdziału 16 GDD bez zgody Patryka.
- Nie dawaj AI żadnych premii ani ukrytej wiedzy; widzi tylko to, co odkryła jej cywilizacja.
- Nie buduj interfejsu gracza ani grafiki przed etapem 5 (wyjątek: podgląd deweloperski).
- Nie planuj eksportu do przeglądarki (Godot nie eksportuje oficjalnie C# do web).
- Nie buduj gry w sieci (przyszła aktualizacja) i nie wprowadzaj skrótów omijających granicę klient–serwer (np. klient czytający stan rdzenia bezpośrednio).
- Nie zmieniaj zasad gry bez zgody Patryka.
