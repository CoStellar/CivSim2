# Testy z perspektywy gracza (playtest)

Baza wiedzy z testów gry oczami gracza, który nie zna kodu: czy jest co robić, czy widać postęp ludu i czy gracz czuje, że to **jego**
decyzje prowadzą lud w górę (sprawczość, ADR 0027). Prowadzi ją osobny wątek „Testuj grę z perspektywy gracza” (prośba Patryka
z 2026-11-16). Kod gry się tu nie zmienia: są tu pomiary, wnioski i propozycje do przekazania agentowi na `development`.

## Pliki

| Plik | Co zawiera |
| --- | --- |
| [`propozycje.md`](propozycje.md) | **lista propozycji zmian** w stałym formacie (do wklejenia agentowi), z priorytetem i stanem |
| [`2026-11-16_sesja-1.md`](2026-11-16_sesja-1.md) | pierwsza sesja: pomiar sprawczości (11 stylów gracza × 50 ludów), kronika gracza, tempo, regresja |
| [`porownanie-z-grami.md`](porownanie-z-grami.md) | co robią lepiej RimWorld, Old World, WorldBox, Crusader Kings, Victoria, Songs of Syx (ze źródłami) |
| `data/` | surowe CSV z pomiarów (do powtórzenia wykresów) |
| `img/` | wykresy PNG |

## Narzędzie: gracz-bot (`tools/CivSim.Playtest`)

Skryptowany „gracz” łączy się z prawdziwym serwerem przez protokół (rola `Player`, jak podgląd w Godocie), bierze dźwignie i wydaje
rozkazy raz na sekundę gry według stylu. Po każdej minucie zapisuje, jak radzi sobie jego lud. Porównanie stylów na tym samym seedzie
i tym samym ludzie odpowiada na pytanie: **czy sterowanie gracza zmienia wynik?**

```
dotnet build tools/CivSim.Playtest -c Release
dotnet tools/CivSim.Playtest/bin/Release/net10.0/CivSim.Playtest.dll --seeds 1-10 --civs all --minutes 15 --out playtest.csv
# jeden lud, z zapisem tego, co gracz widzi na ekranie (kronika i panel „co AI zrobiła za mnie”):
dotnet .../CivSim.Playtest.dll --seeds 7 --civs 1 --minutes 15 --styles ai --out one.csv --transcript tr
python3 docs/playtest/tools/agency_chart.py playtest.csv docs/playtest/img
```

Style gracza (`--styles`, domyślnie wszystkie):

| Styl | Co robi | Po co |
| --- | --- | --- |
| `ai` | łączy się jako gracz, nie bierze żadnej dźwigni | punkt odniesienia (= partia AI) |
| `absent` | bierze wszystkie 5 dźwigni i nic nie robi | ile lud zależy od decyzji (AI czy gracza) w ogóle |
| `absent_research`, `absent_land`, `absent_war` | bierze jedną dźwignię (2, 1 albo 4) i nic nie robi | ile waży każda dźwignia osobno |
| `tech_cheapest`, `tech_dearest` | dźwignia 2: zawsze najtańsza / najdroższa technologia | czy kolejność technologii coś zmienia |
| `farm_rush` | dźwignia 2: ogień, krzemień, rolnictwo, garncarstwo, nawadnianie… | „chcę szybko osiąść” |
| `settle_now` | dźwignie 1 i 2: jak `farm_rush` i „osiądź” na każdy obóz co 5 s | najprostszy plan nowego gracza |
| `warlord` | dźwignia 4: wojna z każdym poznanym ludem, wyprawa co minutę z 1/3 ludzi | gracz agresywny |
| `random` | wszystkie dźwignie, losowy rozkaz co ok. 10 s | gracz, który klika bez planu |

Bot czyta tylko to, co dostaje w widoku gracza (`GameViewMessage`), z jednym wyjątkiem: `warlord` sprawdza w stanie serwera, z kim
lud się zetknął (widok nie ma jeszcze listy znanych ludów). Wyniki są deterministyczne (ten sam seed = te same liczby).

## Format propozycji

Każda propozycja w `propozycje.md` ma ten sam układ, żeby dało się ją wkleić agentowi bez przeróbek:

```
### P-NN: krótki tytuł
- Priorytet: P1 (przed próbą 15 minut Patryka) | P2 (w etapie 6) | P3 (później)
- Stan: nowa | przekazana | zrobiona (PR) | odrzucona (dlaczego)
- Dźwignia / test sternika: której dźwigni dotyczy, które z 5 pytań GDD 1.2 nie przechodzi
- Problem (co widzi gracz): jedno, dwa zdania bez żargonu
- Dowód: liczby z pomiaru, plik, seed, jak powtórzyć
- Proponowana zmiana: co zmienić (dane albo kod), najmniejsza wersja
- Kryteria akceptacji: mierzalne, z tym samym pomiarem
- Decyzja Patryka: potrzebna / niepotrzebna (zmiana zasad GDD wymaga zgody)
```

## Jak przeglądać nową funkcję (każdy PR scalony do `development`)

1. `git fetch` i nowa gałąź `test/playtest-<funkcja>` od `development`.
2. Pomiar sprawczości (10 seedów, wszystkie style) przed i po; wykres `agency_chart.py`.
3. Zapis ekranu gracza (`--transcript`) dla 2 ludów: co widać w pierwszych 3 minutach, ile wpisów kroniki na minutę, ile o własnym ludzie.
4. Test sternika z GDD 1.2 dla funkcji: 5 pytań, odpowiedź z liczbami.
5. Wpis w nowym pliku sesji i nowe albo zaktualizowane propozycje w `propozycje.md`.
