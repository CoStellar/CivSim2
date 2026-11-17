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

- Środek osady i ziemia w promieniu poziomu: obóz 3, wioska 4, miasteczko 5, miasto 6 heksów.
- Poziom daje domy i magazyn; awans kosztuje dobra i od razu poszerza ziemię.
- Ludzie rosną 8% na minutę (co najmniej 1), dopóki są domy i jedzenie. Bez jedzenia ubywa 12% na minutę.

## Budynki

| Budynek | Praca | Co daje (na pracownika na minutę) | Gdzie |
| --- | --- | --- | --- |
| Szałas zbieraczy | 3 | 4 jedzenia i 1 drewna (×2 na dzikim zbożu) | lądy z roślinnością |
| Szałas myśliwych | 3 | 3 jedzenia, 1 skóra (×2 przy zwierzynie) | trawy, lasy, tundra |
| Chata rybacka | 2 | 5 jedzenia (×2 przy rybach) | woda obok |
| Drwal | 3 | 4 drewna | lasy, bagna |
| Kamieniołom | 4 | 2 kamienia (×2 na wzgórzach i w górach, ×2 na złożu kamienia) | wszędzie |
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

## Technologie, myśliciele, doradca (etap D)

- 16 technologii w 5 erach (epoka kamienia → średniowiecze), `technologies` w `game.json`.
- Technologia wymaga doświadczenia w poprzednich: to minuty pracy ludzi w budynkach danej technologii (ludzie bez pracy dają trochę doświadczenia w zbieractwie).
  Potem płaci się cenę w dobrach i czeka.
- Cena spada o 10% za każdy poznany lud, który ją zna (najwyżej 40%), i o 10–15% za szkołę i obserwatorium.
- Myśliciel (jeden) ma rzadkość: zwykły, uzdolniony, wizjoner, rzadki, legendarny.
  - Rzadkość obniża cenę albo wymagane doświadczenie; wizjoner obniża doświadczenie, ale podnosi cenę.
  - Po 12 minutach odchodzi; następca (II, III…) traci 20% przewagi na pokolenie.
  - Co 3 minuty pojawia się kandydat, którego można zatrudnić.
- Doradca (najwyżej jeden):
  - kandydat co 4 minuty, z jedną albo dwiema dziedzinami (gospodarka, wzrost, badania, ekspansja);
  - je 1 jedzenia na minutę;
  - daje jedną radę naraz, tylko w swoich dziedzinach.
- Imiona ludów, osad i ludzi pochodzą z systemu języków.

## Wojna (etap E)

- Jednostki:
  - pospolite ruszenie (darmowe, słabe, wraca do pracy);
  - wojownicy, łucznicy (strzelają na 2 heksy), włócznicy, jeźdźcy, miecznicy, rycerze.

  Każdy rodzaj ma teren, na którym jest mocniejszy, i ulepszenia z technologii (np. wojownicy +30% z brązem).
- Żołnierze jedzą 1 jedzenia na minutę.
- Walka co sekundę: zabici = ludzie × siła × teren i ulepszenia × szczęście 80–120%. Wojsko samo bije wrogów w zasięgu, a gracz wskazuje cel prawym kliknięciem.
- Oblężenie osady:
  - broni się 30% mieszkańców;
  - umocnienia podnoszą obronę: palisada +40%, mury +90%, zamek +200%;
  - poniżej 6 mieszkańców osada przechodzi do zwycięzcy z ziemią i budynkami;
  - lud bez osad znika.
- Wojna to stan:
  - atak bez wypowiedzenia albo w czasie rozejmu psuje reputację;
  - pokój proponuje się z daniną liczoną z przebiegu wojny (zabici i zdobyte osady);
  - po pokoju jest rozejm na 3 minuty.

## Gracz AI (etap F)

Te same komendy co gracz, co 3 sekundy, widzi tylko to, co odkrył jego lud. Charakter (wojowniczość, ekspansja) jest losowany i widać go
tylko po działaniach.

- **Buduje:**
  - najpierw to, co daje doświadczenie potrzebne do następnej technologii;
  - potem jedzenie, domy, magazyn i dobra potrzebne do ceny badania.
- **Bada, zatrudnia, rozwija się:** bada, zatrudnia lepszych myślicieli, awansuje osady, wysyła zwiadowców i osadników.
- **Walczy:**
  - utrzymuje wojsko według charakteru;
  - broni swojej ziemi;
  - przez pierwsze 8 minut nikogo nie atakuje;
  - wojnę zaczyna, gdy ma dwa razy więcej siły niż obrona celu;
  - pokój proponuje, gdy przegrywa.

## Start, zdarzenia, podsumowanie (etapy B i G)

- **Ocena miejsc startu** jest liczona raz po generacji: 7 kryteriów od 0 do 100 i ocena łączna z wagami:

  | kryterium | waga |
  | --- | --- |
  | ludność (dzika żywność, ryby, zwierzyna, woda) | 30 |
  | rolnictwo (żyzność, woda) | 20 |
  | wojsko (wzgórza, trudny teren, metale) | 10 |
  | materiały (kamień, glina, krzemień, las) | 15 |
  | miejsce na rozrost (ląd w promieniu 6) | 10 |
  | przyszłość (miedź, cyna, żelazo) | 5 |
  | bezpieczeństwo (długi sezon wegetacji) | 10 |

- **Faza startu:**
  - ludy wybierają po kolei, w losowej kolejności;
  - gracz ma 30 s i widzi całą mapę bez złóż i żyzności;
  - wokół zajętych miejsc jest strefa blokady (15 heksów);
  - AI wybiera losowo spośród miejsc nie gorszych niż (100 − X)% najlepszego wolnego, gdzie X to losowe 0–30% dla każdego ludu;
  - gdy gracz nie zdąży, za niego wybiera AI;
  - po wyborze kronika podaje trzy zdania historii plemienia.
- **Zdarzenia ze wspólnej puli:**
  - susza, sroga zima, epidemia, pożar, powódź;
  - każdy lud dostaje jedno co 8–10 minut, nigdy to samo co inny lud w tej chwili;
  - zdarzenie trwa 20–90 s;
  - radę daje tylko doradca z pasującą dziedziną.
- **Podsumowanie (okno Summary):**
  - ocena naszego startu wobec najlepszego wolnego miejsca w chwili wyboru, z kryteriami;
  - historia minuta po minucie naszego ludu i ludów, które poznaliśmy: ludność, osady, budynki, technologie, żołnierze, jedzenie, zdarzenie.
