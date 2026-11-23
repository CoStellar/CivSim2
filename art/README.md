# Grafika

| Katalog | Co tu jest |
| --- | --- |
| `palette/` | Paleta CivSim 2: `civsim2_palette.gpl` (Aseprite, GIMP), `.hex` (Lospec), `_1x.png`, próbnik `_swatch.png` |
| `templates/` | Szablon heksa 48×42 (`hex_template_48x42.png`, powiększony `_x8`), podgląd łączenia heksów |
| `legacy/` | Grafiki ze starego CivSim jako materiał odniesienia (32×32) |
| `src/` | Pliki źródłowe LibreSprite (`.ase`) |
| `export/` | PNG gotowe do gry, w podkatalogach według warstw |
| `placeholder/` | Grafiki zastępcze etapu 5 (skrypt `generate.py`) i `manifest.json`: lista wszystkich nazw i rozmiarów grafik gry |
| `packs/` | Paczki grafik (szaty graficzne) do wyboru w menu; kolejność: paczka → `export/` → `placeholder/` (opis w `packs/README.md`) |

## Zasady

- **Heks (od 2026-11-23, decyzja G1): 96×84 px** (dwa razy szablon 48×42). Kolumny co 72 px, rzędy co 84 px, co druga kolumna
  przesunięta o 42 px w dół. Szablon: `templates/hex_template_96x84.png` (powiększony piksel w piksel z 48×42, który jest
  wyliczony co do piksela: heksy łączą się bez szpar i nakładania).
- **Narzędzie:** LibreSprite (darmowy fork Aseprite). Źródła `.ase` w `src/`, gotowe PNG w `export/`.
- **Paleta:** tylko kolory z `civsim2_palette.gpl`. Ostatnie 8 kolorów (rozszerzenie) czeka na akceptację.
- **Warstwy (od dołu):** `biome/` → `terrain/` (rzeźba, cechy, rzeki na krawędziach) → `objects/` → `units/`; do tego `effects/`
  (znaczniki zdarzeń) i `ui/` (ikony warstw 16×16, ramki interfejsu 9-slice).
- **Kolor cywilizacji:** `#FF00FF`, `#C000C0`, `#800080`. Shader podmienia je na barwy cywilizacji.
- **Światło z lewej góry**, kontury ciemniejszym odcieniem wypełnienia, bez półprzezroczystych krawędzi.
- **Rozmiary nowej gry (2×, `docs/grafika-plan.md`):** biomy i teren 96×84; budynki 32×28 (1 miejsce), 64×28 (2 miejsca),
  96×84 (cały heks); centra osad 96×84; jednostki 64×64; ikony dóbr 32×32. Stare grafiki gry legacy (osady `objects/`, złoża
  24×24, jednostki 32×32, ikony UI 16×16) bez zmian. Dokładna lista: `placeholder/manifest.json`.
- **Interfejs:** motyw na każdą erę, ramki 9-slice (rogi 16×16, krawędzie, tło, przyciski w 3 stanach).
- **Nazwy plików:** `kategoria_nazwa_wariant.png`, po angielsku, np. `terrain_grassland_02.png`.

Pixel art jest potrzebny w etapie 13 (tryb gracza, na końcu CORE). Od etapu 5 podgląd używa grafik zastępczych tych samych rozmiarów i nazw, które Twoje pliki później podmienią; wcześniej rysuje kolorowe heksy z kodu.
