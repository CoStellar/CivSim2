# Paczki grafik (szaty graficzne)

Każdy może podmienić grafiki gry bez zmian w kodzie: wystarczy folder z plikami PNG pod tymi samymi nazwami i w tych samych
rozmiarach co grafiki zastępcze w `art/placeholder/`. Pełna lista nazw i rozmiarów: `art/placeholder/manifest.json`
(spis w `art/placeholder/README.md`).

## Gdzie wrzucić paczkę
- `art/packs/<nazwa-paczki>/` obok gry (paczki dołączone do gry), albo
- folder użytkownika `user://art_packs/<nazwa-paczki>/`; w menu startowym przycisk **„Open packs folder”** otwiera go
  (Windows: `%APPDATA%\Godot\app_userdata\CivSim 2\art_packs`, Linux: `~/.local/share/godot/app_userdata/CivSim 2/art_packs`).
  Paczka użytkownika o tej samej nazwie folderu wygrywa z paczką gry.

## Jak wygląda paczka
```
moja-paczka/
  pack.json              opcjonalnie: {"name": "Moja paczka", "author": "Patryk", "description": "Cieplejsze biomy"}
  biome/biome_grassland_01.png      48×42
  objects/settlement_camp_active.png 48×42
  ui/frame_panel.png                24×24 (ramka 9-slice)
  ...
```
Paczka może zawierać dowolną część plików. Brakujące grafiki biorą się z `art/export/` (grafiki Patryka), a potem z `art/placeholder/`.
Kolejność: **paczka → art/export → grafiki zastępcze.** Paczka może też zmienić wygląd interfejsu (pliki w `ui/`).

## Sprawdzenie przy uruchomieniu
Gra sprawdza każdy plik paczki i `art/export/` zaraz po starcie (i po kliknięciu **„Check again”** w menu). Plik jest używany tylko,
gdy ma znaną nazwę, jest poprawnym PNG i ma dokładnie wymagany rozmiar. Pozostałe są pomijane (rysuje się grafika domyślna), a lista
problemów pojawia się:
- w menu startowym, w sekcji **Art**, pod wyborem paczki,
- w pliku `art_check.txt` w folderze paczki,
- w konsoli Godota (linie `art: ...`).

Przykładowy raport:
```
Art check of Test pack: 3 of 140 pictures used, 5 problems.
- biome/biome_desert_01.png: is 50×40 px, must be 48×42 px; the default picture is used
- biome/biome_volcano_01.png: unknown name; ignored (see art/placeholder/manifest.json for the names the game uses)
- objects/deposit_fish.png: cannot be read as a PNG; the default picture is used
- settlement_camp_active.png: wrong folder; ignored (it belongs in objects/settlement_camp_active.png)
- ui/frame_panel.ase: not a .png file; ignored (export from LibreSprite as PNG)
```

Zasady rysowania (paleta, magenty koloru cywilizacji, światło z lewej góry) są w `art/README.md`. Kolor cywilizacji działa w każdej
paczce: piksele `#FF00FF`, `#C000C0` i `#800080` dostają kolor właściciela.
