# Grafika

| Katalog | Co tu jest |
| --- | --- |
| `palette/` | Paleta CivSim 2: `civsim2_palette.gpl` (Aseprite, GIMP), `.hex` (Lospec), `_1x.png`, próbnik `_swatch.png` |
| `templates/` | Szablon heksa 48×42 (`hex_template_48x42.png`, powiększony `_x8`), podgląd łączenia heksów |
| `legacy/` | Grafiki ze starego CivSim jako materiał odniesienia (32×32) |
| `src/` | Pliki źródłowe LibreSprite (`.ase`) |
| `export/` | PNG gotowe do gry, w podkatalogach według warstw |

## Zasady

- **Heks:** flat-top 48×42 px. Kolumny co 36 px, rzędy co 42 px, co druga kolumna przesunięta o 21 px w dół.
  Maska w szablonie jest wyliczona co do piksela i sprawdzona: heksy łączą się bez szpar i nakładania.
- **Narzędzie:** LibreSprite (darmowy fork Aseprite). Źródła `.ase` w `src/`, gotowe PNG w `export/`.
- **Paleta:** tylko kolory z `civsim2_palette.gpl`. Ostatnie 8 kolorów (rozszerzenie) czeka na akceptację.
- **Warstwy (od dołu):** `biome/` → `terrain/` (rzeźba, cechy, rzeki na krawędziach) → `objects/` → `units/`.
- **Kolor cywilizacji:** `#FF00FF`, `#C000C0`, `#800080`. Shader podmienia je na barwy cywilizacji.
- **Światło z lewej góry**, kontury ciemniejszym odcieniem wypełnienia, bez półprzezroczystych krawędzi.
- **Rozmiary:** biomy, teren, osady, ulepszenia 48×42; jednostki 32×32; ikony złóż 24×24; ikony UI 16×16; portrety 64×64.
- **Interfejs:** motyw na każdą erę, ramki 9-slice (rogi 16×16, krawędzie, tło, przyciski w 3 stanach).
- **Nazwy plików:** `kategoria_nazwa_wariant.png`, po angielsku, np. `terrain_grassland_02.png`.

Pixel art jest potrzebny od etapu 5. Wcześniej podgląd rysuje kolorowe heksy z kodu.
