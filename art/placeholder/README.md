# Grafiki zastępcze (etap 5)

Proste grafiki, które gra rysuje, dopóki nie ma pixel artu Patryka. Robi je skrypt `generate.py` (grafiki nowej gry w 2×:
`game_art.py`, zasady stylu w `docs/grafika-plan.md`, rozdział 8)
(Python 3 + Pillow, `python3 art/placeholder/generate.py` z katalogu głównego repo), zawsze tak samo (stałe ziarna losowania).

- Tylko kolory z `art/palette/civsim2_palette.gpl` i trzy magenty koloru cywilizacji (`#FF00FF`, `#C000C0`, `#800080`).
- Bez półprzezroczystych pikseli, światło z lewej góry, kontury ciemniejszym odcieniem.
- Rozmiary i nazwy z GDD 12.3. **Podmiana:** podgląd szuka każdego pliku najpierw w `art/export/<warstwa>/`, a dopiero potem
  tutaj. Twój plik pod tą samą nazwą w `art/export/` zastępuje zastępczy bez zmian w kodzie.

| Warstwa (katalog) | Pliki | Rozmiar | Sztuk |
| --- | --- | --- | --- |
| `biome/` | `biome_<biom>_01..03` (14 biomów z `data/biomes.json`) | 96×84 | 42 |
| `terrain/` | `terrain_hills_01..02`, `terrain_mountains_01..02` | 96×84 | 4 |
| `terrain/` | `terrain_oasis_01`, `terrain_reef_01`, `terrain_ice_field_01` (cechy terenu) | 96×84 | 3 |
| `terrain/` | `terrain_river_<n,ne,se,s,sw,nw>_<stream,river>` (rzeka na jednej krawędzi) | 96×84 | 12 |
| `terrain/` | `terrain_grove_01..03` (gaje na otwartym terenie, nowa gra) | 96×84 | 3 |
| `buildings/` | `building_<klucz>` (każdy budynek z `data/sim/game.json`): 1 miejsce, 2 miejsca, cały heks | 32×28, 64×28, 96×84 | 52 |
| `buildings/` | `settlement_<camp,village,town,city>` (centrum osady nowej gry) | 96×84 | 4 |
| `units/` | `unit_<klucz>` (każda jednostka nowej gry) | 64×64 | 19 |
| `goods/` | `good_<klucz>` (każde dobro nowej gry) | 32×32 | 29 |
| `objects/` | `deposit_<złoże>` (z `data/resources.json`) | 24×24 | 22 |
| `objects/` | `settlement_<camp,hearth,village,walled,town>_<active,abandoned,ruins>` (wygląd z `settlements.json` looks) | 48×42 | 15 |
| `objects/` | `improvement_field_01` (pole uprawne) | 48×42 | 1 |
| `units/` | `unit_migrants_idle_01` (wędrujący obóz), `unit_scout_idle_01` | 32×32 | 2 |
| `effects/` | `event_<efekt>` (efekty zdarzeń na polach i osadach z `data/effects.json`) | 24×24 | 6 |
| `techs/` | `tech_<klucz>` (każda technologia z `data/sim/game.json`; skrypt `icon_art.py`) | 32×32 | 70 |
| `ui/` | `category_<gałąź>` (gałęzie drzewa badań) | 16×16 | 8 |
| `ui/` | `era_<era>` (znak ery obok jej nazwy w górnym pasku) | 32×32 | 5 |
| `ui/` | `event_<zdarzenie>` (susza, ciężka zima, zaraza, pożar, powódź; panel zdarzenia) | 32×32 | 5 |
| `ui/` | `veteran_<wybór>` (premie weteranów), `window_<okno>` (przyciski okien) | 16×16 | 3 + 6 |
| `ui/` | `advisor` (doradca w pasku alertów), `alert_11` (alert „Strangers”) | 32×32 | 2 |
| `ui/` | `<element>_<era>` (motyw interfejsu ery: ramki, przyciski, zakładki; epoka kamienia bez przyrostka) | 24×24, 24×16, 12×12 | 36 |
| `portraits/` | `advisor_<dziedzina>_01..12`, `thinker_<rzadkość>_01..12` (portrety z losowym wyglądem) | 64×92 | 48 + 60 |
| `emblems/` | `emblem_<era>_<motyw>` (herb ludu: 8 motywów × 5 er) | 40×40 | 40 |

Paczka płaska `art/packs/flat/` powstaje tym samym skryptem: `python3 art/placeholder/generate.py --flat` (bez faktury starego CivSim,
płaskie kolory jak w pierwszej wersji grafik zastępczych; w paczce zostają tylko pliki, które różnią się od zastępczych).

Nowy biom, złoże albo efekt w danych: uruchom skrypt ponownie. Brakujący plik podgląd zastępuje kolorem z danych.
