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

Nowy biom, złoże albo efekt w danych: uruchom skrypt ponownie. Brakujący plik podgląd zastępuje kolorem z danych.
