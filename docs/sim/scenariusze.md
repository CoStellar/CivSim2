# Scenariusze testowe i tryb debug (2026-11-19)

Scenariusz to gra, która zaczyna się w wybranym momencie rozwoju. Każdy lud jest ustawiony tak, jak opisuje plik. Służy do testowania dalszych technologii, wojny i reszty bez grania do nich od zera.

- **Pliki:** `data/sim/scenarios/*.json`. Opis pól jest w `data/sim/scenarios/README.md`, a wszystkie pola są opcjonalne.
- **Start:**
  - w menu startowym lista „Test: …” obok „Play new game”;
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
  3. `03_war_bronze`: wojna od startu z ludem 1, obie strony w epoce brązu z wojskiem gotowym do walki;
  4. `04_middle_ages`: my w średniowieczu, z miastem i trzema miasteczkami, reszta w epoce żelaza.
- **Testy:** `ScenarioTests` sprawdza każdy plik: era, poziom, budynki, jednostki i wojny. Każdą grę puszcza też na 2 minuty. Tak samo będę testował wojnę (scenariusz zamiast ręcznego ustawiania w każdym teście).
