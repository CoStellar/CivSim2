# Nowa symulacja: osada i gospodarka (etapy A i B, ADR 0028)

**W jednym zdaniu:** lud ma jeden wspólny magazyn, osady to zbiory heksów, a każdy budynek stoi na jednym heksie i daje
miejsca pracy; ludzie sami obsadzają wolne miejsca, a gospodarka to suma temp przeliczana tylko po zmianie.

Kod: `src/CivSim.Sim` (bez Godota), dane: `data/sim/game.json`, serwer: `src/CivSim.Server/PlayHost.cs`,
ekran: `game/scripts/Play/`. Testy: `tests/CivSim.Sim.Tests`, `tests/CivSim.Tests/Play`.

## Mapa

- Generator świata bez zmian, tylko więcej heksów: `density` w `game.json` (dziś 7) mnoży powierzchnię mapy.
  Mała mapa legacy 98×60 daje 258×160 heksów. Ten sam seed daje te same kontynenty.
- Heks to miejsce na jeden budynek. Teren (biom, wzgórza, góry, woda obok, złoże na heksie) decyduje, co można postawić.

## Osada

- Środek osady i ziemia w promieniu poziomu: obóz 2, wioska 3, miasteczko 4, miasto 5 heksów.
- Poziom daje domy i magazyn; awans kosztuje dobra i od razu poszerza ziemię.
- Ludzie rosną 5% na minutę (co najmniej 1), dopóki są domy i jedzenie. Bez jedzenia ubywa 12% na minutę.

## Budynki

| Budynek | Praca | Co daje (na pracownika na minutę) | Gdzie |
| --- | --- | --- | --- |
| Szałas zbieraczy | 3 | 4 jedzenia i 1 drewna (×2 na dzikim zbożu) | lądy z roślinnością |
| Szałas myśliwych | 3 | 3 jedzenia, 1 skóra (×2 przy zwierzynie) | trawy, lasy, tundra |
| Chata rybacka | 2 | 5 jedzenia (×2 przy rybach) | woda obok |
| Drwal | 3 | 4 drewna | lasy, bagna |
| Kamieniołom | 4 | 3 kamienia (×2 na złożu kamienia) | wzgórza, góry albo złoże kamienia |
| Glinianka | 3 | 3 gliny | woda obok albo złoże gliny |
| Wytwórca narzędzi | 2 | 1 narzędzie z 2 kamieni i 1 drewna | wszędzie |
| Chaty | – | 8 domów | wszędzie |
| Spichlerz | – | +300 każdego dobra w magazynie | wszędzie |

Każdy człowiek je 1 jedzenia na minutę, a bez pracy sam zbiera 0,6. Budowa jest płacona z góry i trwa 10–25 s.
Wstrzymaną budowę wznawia się za 15% ceny.

## Jak liczymy (docs/wydajnosc.md)

- **Tempa zamiast sztuk:** lud ma tempo każdego dobra na minutę. Liczymy je od nowa tylko po zmianie: koniec budowy, zatrzymanie,
  pierwszeństwo, przyrost, brak składnika.
- **Terminy:** koniec budowy to wpis w kolejce terminów, nic nie sprawdza co takt.
- **Rytmy:** gospodarka co 10 taktów (1 s), ludność co 50 taktów (5 s), rozłożone po numerze ludu.
- **Usypianie:** budynek bez składnika śpi, dopóki składnik nie wróci; wtedy budzi się sam.
