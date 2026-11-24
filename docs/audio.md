# Dźwięk i muzyka

Od 2026-11-24 gra ma muzykę, odgłosy otoczenia i efekty. Wszystkie pliki są w `audio/`, a to, co gra przy jakim zdarzeniu,
w `audio/sounds.json`. Kod: `game/scripts/Audio/` (klient, bez wpływu na symulację).

## Skąd są dźwięki

Wszystkie zrobiliśmy sami skryptem `tools/audio/make_audio.py`, więc nie ma licencji do pilnowania (szczegóły: `audio/CREDITS.md`):

- **muzyka**: skrypt układa utwór według stylu ery (skala, tempo, instrumenty, rytm bębnów) i gra go na instrumentach FluidR3
  (licencja MIT) przez FluidSynth;
- **otoczenie i krótkie efekty**: synteza z szumu i fal (wiatr, ptaki, świerszcze, żaby, fale, ogień, rąbanie, kowal, gwar, szczęk broni).

| Era | Skala | Instrumenty | Utwory |
| --- | --- | --- | --- |
| menu | dorycka | smyczki, harfa, waltornia, flet | `menu` |
| epoka kamienia | pentatonika molowa | taiko, szakuhachi, okaryna, chór, ciepła plama | `stone_age_1`, `stone_age_2` |
| neolit | pentatonika durowa | fletnia Pana, okaryna, kalimba, konga, wiolonczela | `neolithic_1`, `neolithic_2` |
| epoka brązu | frygijska dominantowa | obój, szałamaja, harfa, bongosy, tamburyn | `bronze_age_1`, `bronze_age_2` |
| epoka żelaza | eolska | flet, klarnet, harfa, smyczki, waltornia, kotły | `iron_age_1`, `iron_age_2` |
| średniowiecze | dorycka, 3/4 | flet prosty, skrzypce, obój, lutnia, cymbały | `middle_ages_1`, `middle_ages_2` |

## Co gra i kiedy

- **Muzyka:** dwa utwory ery na zmianę, między nimi 15–45 s ciszy. Nowa era: fanfara i płynne przejście do muzyki nowej ery.
  Menu ma własny temat. Koniec gry: muzyka cichnie, gra fanfara zwycięstwa albo porażki.
- **Otoczenie** (co pół sekundy, według środka ekranu): teren pod kamerą wybiera pętlę (las, łąka, mokradła, pustynia, mróz, morze;
  zimą każdy ląd brzmi mrozem). Blisko własnej osady dochodzi gwar osady, a gdy nasze oddziały walczą, odgłosy bitwy
  (głośno, gdy walka jest na ekranie, ciszej, gdy poza nim). Z daleka teren brzmi ciszej. Pętle przechodzą płynnie w ciągu ~2,5 s.
- **Efekty rozkazów:** budowa, rozbiórka, marsz, atak, szkolenie, karawana (zaraz po wydaniu rozkazu); odmowa serwera; okna.
- **Efekty zdarzeń:** budowa gotowa, nowa osada, odkrycie, wojna, pokój, nowy alert (róg przy ataku i wrogu blisko, dzwonek przy
  pozostałych). Każdy dźwięk ma przerwę, przed którą się nie powtórzy (np. róg najwyżej co 20 s).
- **Kliknięcie** przy każdym przycisku gry.

## Głośność

Pięć suwaków (wszystko, muzyka, otoczenie, efekty, interfejs) i wyłącznik w menu startowym (Settings) i w grze pod H.
Klawisz **K** wycisza i włącza dźwięk. Ustawienia zapisują się w folderze użytkownika (`audio_settings.json`).

## Jak podmienić dźwięk

1. Weź plik `.ogg` albo `.wav` (np. z Kenney.nl, OpenGameArt, Freesound; najlepiej licencja CC0).
2. Nazwij go tak jak nasz (np. `ui_click.ogg`, `stone_age_1.ogg`, `amb_forest.ogg`) i wrzuć do `audio/custom/`
   (albo do folderu użytkownika `sounds/`, obok `audio_settings.json`). Gra weźmie go zamiast naszego.
3. Wpisz go do tabeli w `audio/CREDITS.md` (źródło, autor, licencja; CC-BY wymaga podania autora).

Żeby dodać trzeci utwór ery albo zmienić głośność dźwięku, zmień `audio/sounds.json` (`music`, `sounds.*.volumeDb`, `cooldown`).

## Jak wygenerować pliki od nowa

```
apt-get install -y fluidsynth fluid-soundfont-gm ffmpeg && pip install numpy scipy mido
python3 tools/audio/make_audio.py                     # wszystko (ok. 3 min)
python3 tools/audio/make_audio.py --only menu,ui_click  # wybrane
```

Style er są w `tools/audio/music.py` (`STYLES`), pętle w `ambient.py`, efekty w `sfx.py`. Ten sam seed daje te same nuty.

## Paczka gry

`tools/export/export_windows.sh` kopiuje `audio/` obok exe (jak `data/` i `art/`). Pliki są czytane w czasie gry, bez importu Godota.
