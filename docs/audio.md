# Dźwięk i muzyka

Od 2026-11-24 gra ma muzykę, odgłosy otoczenia i efekty. Wszystkie pliki są w `audio/`, a to, co gra przy jakim zdarzeniu,
w `audio/sounds.json`. Kod: `game/scripts/Audio/` (klient, bez wpływu na symulację).

## Skąd są dźwięki

Wszystkie zrobiliśmy sami skryptem `tools/audio/make_audio.py`, więc nie ma licencji do pilnowania (szczegóły: `audio/CREDITS.md`):

- **muzyka**: skrypt układa utwory według terenu i ery i zapisuje je jako nuty (`audio/music/*.mid`, po ok. 20 KB).
  Gra gra je na żywo na instrumentach z `audio/instruments.sf2`. To zestaw FluidR3 GM (licencja MIT) przycięty do 39 używanych
  instrumentów, ok. 50 MB. Gra je biblioteką MeltySynth (MIT, czysty C#). Odtwarzanie zajmuje ok. 1,5% jednego rdzenia;
- **otoczenie i krótkie efekty**: synteza z szumu i fal (wiatr, ptaki, świerszcze, żaby, fale, ogień, rąbanie, kowal, gwar, szczęk broni).

## Muzyka: teren i era (decyzja Patryka z 2026-11-24)

Na każdy z 6 terenów w każdej z 5 er przypadają 3 utwory po ok. 5 minut, razem 90 utworów i temat menu.
Teren nadaje brzmienie, era mówi, jak prymitywnie gra zespół.

| Teren | Biomy | Brzmienie |
| --- | --- | --- |
| góry i zimno (`mountains`) | tundra, lód, tajga | nordyckie: wolne, molowe, bębny taiko, burdony, rogi, skrzypce, chór |
| las (`forest`) | las liściasty | celtyckie: metrum 3/4, flety, fujarki, harfa, dudy w średniowieczu |
| step (`steppe`) | step, łąka, sawanna | rytm jeźdźców (galop), burdony głosu, smyczek jak morin chuur, szamisen |
| pustynia (`desert`) | pustynia | bliskowschodnie: skala frygijska dominantowa, darbuka, ney, obój, oud, kanun |
| mokradła i dżungla (`wetland`) | bagna, dżungla | marimba, kalimba, bębny konga, grzechotki, fletnia |
| wybrzeże (`coast`) | gdy woda zajmuje ≥ 45% widocznych heksów | durowe, kołyszące 3/4, harfa, w średniowieczu akordeon i skrzypce |

| Era | Co gra |
| --- | --- |
| epoka kamienia | bębny, głos, kościany flet (okaryna), burdon; dużo pauz, bez akordów |
| neolit | dochodzą proste flety, kalimba, rozłożone akordy |
| epoka brązu | lira (harfa), rogi, obój, plama akordów |
| epoka żelaza | harfa, smyczki, druga melodia |
| średniowiecze | pełny skład: flet prosty, skrzypce, lutnia, cymbały, chór |

Utwór ma wstęp, temat A, jego warianty, temat B, spokojny przerywnik i zakończenie. Tematy wracają w innych instrumentach.
Na końcu utwór wraca do początku (pętla). Każdy gra 2 razy, potem płynnie przechodzi następny z trzech (`playsBeforeNext`).

**Jak gra wybiera teren:** liczy znane heksy lądu widoczne na ekranie i bierze teren, którego jest najwięcej.
Inny teren musi przeważać przez 20 s (`regionSwitchSeconds`), zanim jego muzyka wejdzie, więc przewijanie mapy jej nie przełącza.
Nowa era zmienia muzykę od razu, po fanfarze.

## Co gra i kiedy

- **Muzyka:** jak wyżej. Menu ma własny temat. Koniec gry: muzyka cichnie, gra fanfara zwycięstwa albo porażki.
- **Otoczenie** (co pół sekundy, według środka ekranu): teren pod kamerą wybiera pętlę (las, łąka, mokradła, pustynia, mróz, morze;
  zimą każdy ląd brzmi mrozem). Blisko własnej osady dochodzi gwar osady, a gdy nasze oddziały walczą, odgłosy bitwy
  (głośno, gdy walka jest na ekranie, ciszej, gdy poza nim). Z daleka teren brzmi ciszej. Pętle przechodzą płynnie w ciągu ~2,5 s.
- **Efekty rozkazów:** budowa, rozbiórka, marsz, atak, szkolenie, karawana (zaraz po wydaniu rozkazu); odmowa serwera; okna.
- **Efekty zdarzeń:** budowa gotowa, nowa osada, odkrycie, wojna, pokój, nowy alert (róg przy ataku i wrogu blisko, dzwonek przy
  pozostałych). Każdy dźwięk ma przerwę, przed którą się nie powtórzy (np. róg najwyżej co 20 s).
- **Kliknięcie** przy każdym przycisku gry.

## Głośność

Pięć suwaków (wszystko, muzyka, otoczenie, efekty, interfejs; otoczenie domyślnie ciche, 40%) i wyłącznik w menu startowym (Settings) i w grze pod H.
Klawisz **K** wycisza i włącza dźwięk. Ustawienia zapisują się w folderze użytkownika (`audio_settings.json`).

## Jak podmienić dźwięk

1. Weź plik `.ogg` albo `.wav` (np. z Kenney.nl, OpenGameArt, Freesound; najlepiej licencja CC0).
2. Nazwij go tak jak nasz (np. `ui_click.ogg`, `amb_forest.ogg`; utwór muzyki jako `.ogg` z nazwą nut, np. `neolithic_forest_2.ogg`) i wrzuć do `audio/custom/`
   (albo do folderu użytkownika `sounds/`, obok `audio_settings.json`). Gra weźmie go zamiast naszego.
3. Wpisz go do tabeli w `audio/CREDITS.md` (źródło, autor, licencja; CC-BY wymaga podania autora).

Czwarty utwór terenu: dodaj `<era>_<teren>_4.mid` (albo `.ogg` w `audio/custom/`), gra sama go znajdzie.
Głośność dźwięku zmienisz w `audio/sounds.json` (`sounds.*.volumeDb`, `cooldown`, `musicGain`).

## Jak wygenerować pliki od nowa

```
apt-get install -y fluidsynth fluid-soundfont-gm ffmpeg && pip install numpy scipy mido
python3 tools/audio/make_audio.py                          # wszystko
python3 tools/audio/make_audio.py --only music,instruments   # tylko nuty i instrumenty (kilkanaście sekund)
python3 tools/audio/preview.py /tmp/odsluch 75               # pierwsze 75 s każdego utworu jako .ogg do odsłuchu
```

Style terenów i er są w `tools/audio/styles.py`, układanie utworów w `music.py`, przycinanie instrumentów w `trim_sf2.py`,
pętle w `ambient.py`, efekty w `sfx.py`. Ten sam seed daje te same nuty.

## Paczka gry

`tools/export/export_windows.sh` kopiuje `audio/` (z `instruments.sf2`) obok exe (jak `data/` i `art/`). Pliki są czytane w czasie gry, bez importu Godota.
