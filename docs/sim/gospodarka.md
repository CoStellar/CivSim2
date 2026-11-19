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

## Dyplomacja, handel, cele, pauza (etapy G i H)

- **Umowa handlowa:** możliwa z ludem, który nas zna albo którego znamy, w czasie pokoju.
  - Pozwala na wymianę towar za towar.
  - Każdy partner obniża cenę badań o 5%.
  - AI proponuje umowy co minutę, jeśli nie jest bardzo wojownicze; nie podpisuje ich z ludami o złej reputacji (poniżej −25).
- **Sojusz:**
  - wymaga umowy handlowej;
  - sojusznik przystępuje do wojny wypowiedzianej swojemu partnerowi;
  - AI nie atakuje sojuszników, a partnerów handlowych tylko najbardziej wojownicze.
- **Wymiana:** „daję X dobra A za Y dobra B”, według wartości dóbr z `game.json`: jedzenie 1, drewno 1, kamień 1,2, skóry 1,5, narzędzia 3, miedź 4, cyna 5, brąz i żelazo 8.
  - AI przyjmuje, gdy dostaje co najmniej 110% wartości tego, co daje, i zostaje jej co najmniej dwa razy tyle.
  - Człowiek odpowiada na ofertę w oknie Peoples.
- **Wojna łamie umowy.** Wypowiedzenie wojny partnerowi handlowemu kosztuje połowę kary za atak z zaskoczenia.
- **Cel partii** wybiera się w menu przy „Play new game”:
  - pierwszy lud w średniowieczu;
  - ostatni lud na mapie;
  - najlepszy wynik po 2 godzinach.

  Wynik = ludzie + 10 × osady + 20 × technologie + żołnierze. Sprawdzany co minutę.
- **Pauza:**
  - gracz sam pauzuje bez limitu;
  - przy kilku graczach każdy ma 3 pauzy;
  - pauzę innego gracza można przerwać dopiero po 30 s;
  - każdy gracz dostaje kolejny wolny lud.

## Towary na mapie, tragarze, karawany (decyzje z 2026-11-17, wieczór)

- **Zapas jest osobno w każdej osadzie.**
  - Miejsce na każde dobro: osada ma swoje (obóz 150, wioska 200, miasteczko 300, miasto 500), każdy gotowy budynek dodaje 20, magazyn 300, spichlerz 250, wielki magazyn 1000.
  - Produkcja trafia do zapasu osady, w której stoi budynek; ludzie jedzą z zapasu swojej osady.
  - Żołnierze w polu i doradca jedzą ze stolicy (pierwszej osady).
- **Budynki śpią i budzą się same.**
  - Budynek śpi, gdy brakuje mu składnika (mniej niż 1 sztuka w osadzie) albo gdy wszystko, co robi, jest pełne (98% miejsca).
  - Co sekundę gra porównuje, które dobra w osadzie są puste albo pełne. Tylko gdy to się zmieni, przelicza tempa, a budynki same się budzą.
- **Dostawy na budowę:**
  1. Cena schodzi z zapasu osady. Tragarze wychodzą z najbliższego magazynu tej osady albo ze środka, osobna grupa na każde dobro (1 osoba na 20 sztuk).
  2. Idą 0,5 s na płaski heks; droga przyspiesza ich 2×, koło o 50%.
  3. Praca idzie tak daleko, jak pozwalają materiały na miejscu: przy połowie ceny (liczonej w sztukach) da się zbudować połowę. Potem budowa czeka na resztę.
  4. Po dojściu tragarze wracają do pracy w osadzie.
- **Napad na tragarzy albo karawanę (decyzja z 2026-11-17, 22:54):**
  - ładunek przepada, nikt go nie przejmuje;
  - właściciel i napastnik dostają wiadomość;
  - nic nie dzieje się samo: budowa stoi na tym, co doszło, a gracz klika „Bring the missing materials” (płaci jeszcze raz tylko za brakującą część);
  - AI wysyła brakujące materiały sama, gdy ma je w zapasie.
- **Karawany jeżdżą tylko lądem.** Wodne przyjdą później, razem z flotą.
- **Karawany (2 osoby, 25 sztuk na osobę):**
  - między własnymi osadami: raz albo stała trasa, przy czym kurs rusza, gdy jest co najmniej 10 sztuk;
  - wymiana z innym ludem: dwie karawany, nasza i ich, a towar zmienia właściciela dopiero po dojściu;
  - danina po wojnie też idzie karawaną;
  - jeśli nie ma drogi lądem, wymiany nie da się zawrzeć;
  - zabita karawana traci ładunek, jak tragarze.
- **Umowy z warunkami:**
  - znają się od co najmniej 3 minut;
  - osady są nie dalej niż 20 heksów od siebie (licząc od granic);
  - od ostatniej wojny minęło 10 minut;
  - reputacja tamtego ludu jest wyższa niż −20;
  - AI ma w tym interes: widzi u nich budynek technologii, której nie zna, albo dobra, którego jej brakuje; sojusz chce przy wspólnym wrogu;
  - najwyżej jedna propozycja do danego ludu na 3 minuty, po odmowie 6 minut przerwy;
  - umowa handlowa wymaga Handlu wymiennego.
- **Drzewo:** 45 technologii bazowych i 15 ulepszeń ery, szczegóły w `docs/sim/technologie.md`.
  - Ulepszenia działają jako efekty: siła jednostek według grup (walka wręcz, piechota, dystans, jazda), produkcja budynków według grup (narzędzia, farmy, warsztaty), odporność na zdarzenia, szybkość, szkolenie, badania, przyrost, szczęście w losowaniu myślicieli i doradców, oblężenia, obrona.
- **Nowe zasady:**
  - drogi poza granicami osad (ruch po drodze 2× szybszy);
  - statki pływają tylko po wodzie, a szkoli się je w porcie albo stoczni, w osadzie nad wodą;
  - machiny oblężnicze zadają 4× więcej obrażeń osadom (trebusz jeszcze +100%).
- **Nowe dobra:** odzież, naczynia, tkaniny, uzbrojenie. Osada staje się miasteczkiem za naczynia i odzież, a miastem za tkaniny i naczynia.

## Ery jako poziomy, kategorie technologii, alarmy (decyzje z 2026-11-18)

- **Ery to poziomy ludu.**
  - Następna era otwiera się, gdy lud zna 5 technologii swojej obecnej ery. Ulepszenia też się liczą.
  - Liczba jest w `eraAdvanceTechs` w `data/sim/game.json`.
  - Technologii z ery, do której lud jeszcze nie doszedł, nie da się zacząć.
  - W obrębie ery nadal trzeba iść po drzewie, bo technologia wymaga doświadczenia w poprzednich.
- **Kategorie technologii:** jedzenie, przemysł, budownictwo, handel, wojsko, zarządzanie, nauka. Okno Research grupuje według nich.
- **Jedzenie na drogę:** tragarze i karawany przy wyjściu biorą 0,5 jedzenia na osobę na każde 10 heksów drogi tam i z powrotem (`tripFoodPer10Hexes`). Jeśli jedzenia brakuje, idą z tym, co jest.
- **Alarmy** (liczone na żądanie dla widoku, nie zapisywane):
  - atak na osadę;
  - wróg blisko (wojsko ludu, z którym walczymy, do 3 heksów za ziemią osady);
  - głód;
  - brak dobra, przez który budynek stoi;
  - brak domów (osada nie rośnie);
  - pełny magazyn dobra, które osada wytwarza;
  - budowa bez materiałów.
- **Odpowiedź na brak:** w panelu osady są przyciski „Bring 20 X from Y” (jeden kurs) i „Supply route” (stała trasa), z najbliższych osad, które mają tego dobra co najmniej 40.
