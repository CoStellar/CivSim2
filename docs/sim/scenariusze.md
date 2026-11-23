# Scenariusze testowe i tryb debug (2026-11-19)

Scenariusz to gra, która zaczyna się w wybranym momencie rozwoju. Każdy lud jest ustawiony tak, jak opisuje plik. Służy do testowania dalszych technologii, wojny i reszty bez grania do nich od zera.

- **Pliki:** `data/sim/scenarios/*.json`. Opis pól jest w `data/sim/scenarios/README.md`, a wszystkie pola są opcjonalne.
- **Start:**
  - w menu startowym sekcja „Test scenarios” (przycisk Play przy każdym scenariuszu);
  - z wiersza poleceń `--play --play-scenario <nazwa pliku bez .json>`.
- **Co da się ustawić dla każdego ludu:**
  - erę (zna wszystko z er wcześniejszych, razem z doświadczeniem i wyprodukowanymi dobrami, których wymagają dalsze technologie) i dodatkowe technologie;
  - poziom i liczbę ludzi pierwszej osady oraz jej zapasy;
  - gotowe budynki (stają na pierwszych heksach, gdzie mogą stać);
  - gotowe jednostki;
  - dodatkowe osady wokół i liczbę ich mieszkańców;
  - charakter AI (agresja, ekspansja) i to, czy lud prowadzi AI.
- **Dla całej gry:** ziarno i wielkość świata, liczba ludów, odkryta mapa, wojny od startu.
- **Klawisze debug** (tylko w scenariuszach; w rogu ekranu widać „TEST”):

| Klawisz | Co robi |
| --- | --- |
| F5 | +200 każdego dobra w każdej osadzie |
| F6 | kończy obecne badanie |
| F7 | odkrywa mapę |
| F8 | +10 ludzi w każdej osadzie |
| F9 | następna era (zna całą obecną) |

  Idą przez serwer jako zwykła komenda, a w normalnej grze serwer je odrzuca.
- **Scenariusze na start:**
  1. `01_neolithic_start`: wszyscy wchodzą w neolit z wioską 40 osób;
  2. `02_bronze_age_empire`: my w epoce brązu, z miasteczkiem i dwiema wioskami, reszta w neolicie;
  3. `03_war_bronze`: wojna od startu z ludem 1, obie strony w epoce brązu z wojskiem gotowym do walki; bez pokoju (`noPeace`, decyzja Patryka 2026-11-23: scenariusz testuje samą walkę);
  4. `04_middle_ages`: my w średniowieczu, z miastem i trzema miasteczkami, reszta w epoce żelaza.
  5. `11_peace` (2026-11-23, ADR 0030): wojna od startu z ludem 1, który ma miasteczko i dwie wioski; test okna pokoju (Peoples):
     podpowiedź warunków, dobra, osada oblegana przez nasze wojsko, długość pokoju i zmęczenie wojną.
  6. `12_roads` (2026-11-23, grafiki): nasze miasto w średniowieczu z gotowymi drogami w sześciu kierunkach (na zmianę droga
     i ścieżka, z mostami na rzekach), rowem (pierścień 2), murem (pierścień 3) i akweduktem (pierścień 4). Do oglądania grafik
     dróg i prac. Nowe klucze scenariusza: `roadStar` i `workRings`.
  7. `13_deposits` (2026-11-23, grafiki): my w epoce brązu z miasteczkiem; cała mapa znana i przeszukana (nowy klucz
     `surveyed`), więc widać wszystkie złoża: naszych er z obrazkami, rudy późniejszych er jako „???”.
- **Testy:** `ScenarioTests` sprawdza każdy plik: era, poziom, budynki, jednostki i wojny. Każdą grę puszcza też na 2 minuty. Tak samo będę testował wojnę (scenariusz zamiast ręcznego ustawiania w każdym teście).

## Zapis i wczytanie partii (2026-11-20)

- **Zapis:** przycisk „Save” na górnym pasku albo F5. Gra zapisuje się też sama co 5 minut gry jako „autosave”.
- **Wczytanie:** menu startowe, sekcja „Load game” (najnowsze na górze). Pliki `.civsave` leżą w folderze danych użytkownika
  (`CivSim2/saves`), przycisk „Open saves folder” go otwiera.
- **Jak to działa:** gra jest deterministyczna, więc zapis to ustawienia partii i każda komenda graczy z taktem, w którym weszła.
  Wczytanie zaczyna tę samą grę i odgrywa komendy jeszcze raz, potem sprawdza hasz stanu. Gdy hasz się nie zgadza (zapis ze starszej
  wersji zasad albo danych), gra idzie dalej, ale kronika mówi, że stan jest inny. Kod: `src/CivSim.Server/PlaySaves.cs`, `PlayHost.Replay`.
- **Czas wczytania:** tyle, ile symulacja liczy grę bez okna (godzina gry z 4 ludami: kilkanaście sekund).
