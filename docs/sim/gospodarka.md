# Nowa symulacja: osada i gospodarka (etapy A i B, ADR 0028)

**W jednym zdaniu:** każda osada ma swój zapas i swoje heksy, na heksie są 4 miejsca na budynki, budynki dają miejsca pracy,
ludzie sami je obsadzają, a gospodarka to suma temp przeliczana tylko po zmianie.

Stan opisu: gałąź `feat/stone-age` (2026-11-18). Drzewo technologii i mechaniki z nim związane (zima, zbieranie z okolicy,
stany heksa, budynki wsparcia, statki) opisuje szczegółowo `docs/sim/technologie.md`.

Kod: `src/CivSim.Sim` (bez Godota), dane: `data/sim/game.json`, serwer: `src/CivSim.Server/PlayHost.cs`,
ekran: `game/scripts/Play/`. Testy: `tests/CivSim.Sim.Tests`, `tests/CivSim.Tests/Play`.

## Mapa

- Generator świata bez zmian, tylko więcej heksów: `density` w `game.json` (dziś 7) mnoży powierzchnię mapy.
  Mała mapa legacy 98×60 daje 258×160 heksów. Ten sam seed daje te same kontynenty.
- **Na heksie są 4 miejsca** (decyzja z 2026-11-18):
  - budynek zajmuje 1 miejsce (domy, warsztaty, zielarz, szkoła), 2 miejsca (magazyny, stajnie, chaty zbieraczy, hale) albo cały heks (pola, kopalnie, kamieniołomy, porty, zamki);
  - środek osady zajmuje cały heks.
- Teren (biom, wzgórza, góry, woda obok, złoże na heksie) decyduje, co można postawić.
- **Drzewa** (Patryk, 2026-11-18 19:35: za mało drewna, za mało drzew):
  - lasy są pełne drzew;
  - na otwartym lądzie rosną kępy, skupiskami: 50% heksów łąki i bagien, 40% sawanny, 25% stepu, 15% tundry (`trees` w `game.json`);
  - drwal może stanąć na kępie i daje tam 60% tego, co w lesie;
  - zbieranie z okolicy liczy drewno kęp tak samo;
  - kępy liczą się też do oceny miejsc startu.
  - Wynik na 4 ziarnach: każdy lud ma w swojej ziemi co najmniej 6 heksów z drzewami (zwykle 10–30), a wartość drewna wynosi 24–72%. Wcześniej co czwarty lud nie miał żadnego drzewa (10%).
- **Stany heksa** (rów, palisada, mur, akwedukt, most) leżą na heksie obok budynków i zostają pod nimi; usuwa je tylko gracz.

## Osada

- Środek osady i ziemia w promieniu poziomu: obóz 3, wioska 4, miasteczko 5, miasto 6 heksów.
- Poziom daje domy i magazyn; awans kosztuje dobra i od razu poszerza ziemię.
- Ludzie rosną 8% na minutę (co najmniej 1), dopóki są domy i jedzenie w magazynie. Głód i śmierć z głodu przychodzą przy racjach (niżej).

## Budynki

| Budynek | Praca | Co daje (na pracownika na minutę) | Gdzie |
| --- | --- | --- | --- |
| Szałas zbieraczy | 3 | 4 jedzenia i 1 drewna (×2 na dzikim zbożu; zimą jeszcze ×0,5) | lądy z roślinnością |
| Szałas myśliwych | 3 | 3 jedzenia, 1 skóra (×2 przy zwierzynie) | trawy, lasy, tundra |
| Chata rybacka | 2 | 5 jedzenia (×2 przy rybach) | woda obok |
| Drwal | 3 | 4 drewna | lasy, bagna |
| Kamieniołom | 4 | 2 kamienia (×2 na wzgórzach i w górach, ×2 na złożu kamienia) | wszędzie |
| Glinianka | 3 | 3 gliny | woda obok albo złoże gliny |
| Wytwórca narzędzi | 2 | 1 narzędzie z 2 kamieni i 1 drewna | wszędzie |
| Chaty (Dwellings) | – | 8 domów (+2 z Shelters) | wszędzie |
| Magazyn (Stockpiling) | – | +300 każdego dobra w magazynie | wszędzie |

Pełna lista budynków, z rozmiarem na heksie, jest w `docs/sim/technologie.md`.

- Każdy człowiek je 1 jedzenia na minutę.
- **Zbieranie z okolicy:** gracz włącza je w osadzie (na starcie jest włączone). Ludzie bez pracy zbierają wtedy na minutę 0,5 jedzenia, 0,25 drewna i 0,1 kamienia, razy wartość ziemi osady:
  - jedzenie i drewno zależą od biomu;
  - kamień od rzeźby terenu;
  - złoża i rzeki dodają;
  - wartość ziemi liczona jest raz, przy założeniu osady i gdy ziemia rośnie.
- **Siła pracy** osady (zima, ubrania, kamienne domy) mnoży całą produkcję. Szczegóły są w `docs/sim/technologie.md`, rozdział „Mechaniki”.
- Budowa:
  - cenę niosą tragarze i praca idzie tak daleko, jak pozwalają materiały;
  - trwa 10–25 s, a plac budowniczych w osadzie skraca ją do 30%;
  - wstrzymaną budowę wznawia się za 15% ceny.

## Jak liczymy (docs/wydajnosc.md)

- **Tempa zamiast sztuk:** lud ma tempo każdego dobra na minutę. Liczymy je od nowa tylko po zmianie: koniec budowy, zatrzymanie,
  pierwszeństwo, przyrost, brak składnika.
- **Terminy:** koniec budowy to wpis w kolejce terminów, nic nie sprawdza co takt.
- **Rytmy:** gospodarka co 10 taktów (1 s), ludność co 50 taktów (5 s), rozłożone po numerze ludu.
- **Usypianie:** budynek bez składnika śpi, dopóki składnik nie wróci; wtedy budzi się sam.

## Technologie, myśliciele, doradca (etap D)

- 65 technologii w 5 erach (epoka kamienia → średniowiecze), `technologies` w `game.json`: 6 podstawowych, 47 zwykłych, 12 ulepszeń.
- **Doświadczenie:**
  - zwykle punkty doświadczenia: 1 punkt to 1 pracownik przez 1 minutę w budynkach danej technologii (zbierający z okolicy dają trochę punktów zbieractwa);
  - czasem zamiast punktów wyprodukowane dobra, np. Barter 30 ceramiki;
  - czasem technologia wymagana „najpierw”.
- Potem płaci się cenę w dobrach i czeka.
- Cena spada o 10% za każdy poznany lud, który ją zna (najwyżej 40%), i o 5% za każdą umowę handlową.
- **Szkoły, akademie, uniwersytety** skracają czas badania: każdy pracownik przyspiesza jedną wybraną gałąź (+15/20/25%), gałąź najwyżej +50%.
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
  - mieszkańcy (Residents): darmowi, słabi, bez technologii, tylko do obrony;
  - łowcy (włócznie i proce, zasięg 2), którzy po Archery sami stają się łucznikami (zasięg 3);
  - wojownicy (Flint weapons), włócznicy, jeźdźcy i rycerze (kosztują konie), miecznicy, kusznicy, katapulty;
  - statki: transport (20 żołnierzy na pokładzie), okręt (30), statek handlowy.

  Każdy rodzaj ma teren, na którym jest mocniejszy, i ulepszenia z technologii: jedno ulepszenie broni i pancerza na erę.
- **Ranni:** 30% poległych to ranni. Zielarze leczą ich w ziemi swojej osady.
- Żołnierze jedzą 1 jedzenia na minutę.
- Walka co sekundę: zabici = ludzie × siła × teren i ulepszenia × szczęście 80–120%. Wojsko samo bije wrogów w zasięgu, a gracz wskazuje cel prawym kliknięciem.
- Oblężenie osady:
  - broni się 30% mieszkańców;
  - zamek podnosi obronę o 220%;
  - palisada (stan heksa) rani wrogów, którzy na niej stoją: 2% ludzi na sekundę;
  - mur (stan heksa) zatrzymuje obcych, dopóki go nie rozbiją; wojsko, które nie ma drogi do osady, samo atakuje najbliższy mur;
  - poniżej 6 mieszkańców osada przechodzi do zwycięzcy z ziemią i budynkami;
  - lud bez osad znika.
- Wojna to stan:
  - atak bez wypowiedzenia albo w czasie rozejmu psuje reputację;
  - pokój proponuje się z daniną liczoną z przebiegu wojny (zabici i zdobyte osady);
  - po pokoju jest rozejm na 3 minuty.

## Gracz AI (etap F)

Pełny opis decyzji (N1–N20), liczby i pomiar z 12 ziaren: [`docs/ai/13-ai-nowej-gry.md`](../ai/13-ai-nowej-gry.md). Pomiar: `dotnet run --project src/CivSim.Headless -c Release -- --sim-report --seeds 12`.

Te same komendy co gracz, co 3 sekundy, widzi tylko to, co odkrył jego lud. Charakter (wojowniczość, ekspansja) jest losowany i widać go
tylko po działaniach.

- **Buduje:**
  - najpierw jedzenie w głodnej osadzie (albo takiej, która zjada ostatnie dwie minuty zapasu), na jej własnej ziemi;
  - przed zimą (minuta przed i w zimie), gdy zapas jedzenia jej nie wystarczy: myśliwych i rybaków, bez osadników i wojska;
  - to, co daje doświadczenie albo dobra potrzebne do następnej technologii;
  - jedzenie, domy, magazyn i dobra potrzebne do ceny badania;
  - narzędzia, gdy ich brak (po kamieniołomie); krawców przed zimą;
  - budynki wsparcia tam, gdzie mają co wzmacniać: zielarza przy łowcach, kamieniarza przy kamieniołomie, młyn przy farmach, gildię przy warsztatach, a łaźnię, szkołę, akademię i uniwersytet w większych osadach.
- **Rysuje stany heksa** (jeden naraz):
  - rów od wody do suchej farmy;
  - pierścień muru i palisady wokół osady w czasie wojny;
  - akwedukt do miasteczka.
- **Pomiar** (3 ziarna, 4 ludy, 40 minut, przed i po zmianach z 2026-11-18): ludzi o 29% więcej (967 zamiast 750), próbek z głodem o jedną trzecią mniej, wymarły 1 lud zamiast 2.
- **Czego AI jeszcze nie robi:** nie wsiada na statki i nie handluje drogą morską z własnej woli.
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
  - wokół zajętych miejsc jest strefa blokady: 10 heksów (do 2026-11-19 było 15, co zajmowało za dużo kontynentu);
  - wszystkie liczby fazy startu są w danych, w `startPhase`: `pickSeconds` 30, `blockRadius` 10, `blockFuzz` 3, `scoreRadius` 3, `roomRadius` 6;
  - koło pokazane graczowi nie zdradza miejsca (2026-11-19): jego środek leży losowo (z ziarna) do 3 heksów od prawdziwego startu, a promień jest o 3 większy (13), więc zawsze zakrywa całą blokadę; serwer sprawdza to samo koło, które widzi gracz, i nie ma kropki w środku;
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
- **Tragarze** niosą po 5 sztuk na osobę, a po Carrying (kosze) po 20.
- **Towary między osadami** niosą tragarze, dopóki lud nie zna The wheel i Horseback riding i nie ma w osadzie 2 koni. Wtedy idzie karawana, a konie wracają z nią. Konie mieszkają tylko w stajniach, po 16 w każdej.
- **Bez drogi lądowej** wymiana z innym ludem płynie statkami handlowymi między portami (Sailing).
- **Karawany (2 osoby, 25 sztuk na osobę):**
  - między własnymi osadami: raz albo stała trasa, przy czym kurs rusza, gdy jest co najmniej 10 sztuk;
  - wymiana z innym ludem: dwie karawany, nasza i ich, a towar zmienia właściciela dopiero po dojściu;
  - danina po wojnie też idzie karawaną;
  - jeśli nie ma drogi lądem ani morzem między portami, wymiany nie da się zawrzeć;
  - zabita karawana traci ładunek, jak tragarze.
- **Umowy z warunkami:**
  - znają się od co najmniej 3 minut;
  - osady są nie dalej niż 20 heksów od siebie (licząc od granic);
  - od ostatniej wojny minęło 10 minut;
  - reputacja tamtego ludu jest wyższa niż −20;
  - AI ma w tym interes: widzi u nich budynek technologii, której nie zna, albo dobra, którego jej brakuje; sojusz chce przy wspólnym wrogu;
  - najwyżej jedna propozycja do danego ludu na 3 minuty, po odmowie 6 minut przerwy;
  - umowa handlowa wymaga Handlu wymiennego (Barter), a do Currency także osobnego punktu handlowego na każdą umowę.
- **Drzewo:** szczegóły w `docs/sim/technologie.md` (od 2026-11-18: 65 technologii).
  - Ulepszenia działają jako efekty: siła jednostek według grup (walka wręcz, piechota, dystans, jazda), produkcja budynków według grup (narzędzia, farmy, warsztaty), odporność na zdarzenia, szybkość, szkolenie, badania, przyrost, szczęście w losowaniu myślicieli i doradców, oblężenia, obrona.
- **Nowe zasady:**
  - drogi poza granicami osad (ruch po drodze 2× szybszy); od Masonry drogi same budują mosty przez rzeki;
  - rzeki zatrzymują piechurów do epoki brązu, chyba że przy własnej przystani albo po moście;
  - statki pływają tylko po wodzie, a szkoli się je w porcie albo stoczni, w osadzie nad wodą;
  - machiny oblężnicze zadają 4× więcej obrażeń osadom (trebusz jeszcze +100%).
- **Nowe dobra:** odzież, naczynia, tkaniny, uzbrojenie. Osada staje się miasteczkiem za naczynia i odzież, a miastem za tkaniny i naczynia.

## Co wiemy o ziemi: poszukiwacze i mierniczy (decyzja Patryka z 2026-11-19)

- **Na naszej ziemi** wiemy wszystko: złoża (rudy od ery, w której się je rozpoznaje) i żyzność. Gdy ziemia osady rośnie, nowe heksy też są znane.
- **Poza naszą ziemią** samo zobaczenie heksa pokazuje teren i to, co widać gołym okiem: zwierzynę, ryby i dzikie konie (`survey.surface`). Reszty nie znamy, dopóki jej nie zbadamy.
- **Trzy rodzaje zwiadowców:**

| Jednostka | Technologia | Co znajduje |
| --- | --- | --- |
| Zwiadowcy | Scouting | teren, ludy, osady, wojsko (jak dotąd) |
| Poszukiwacze (prospectors) | Stone tools | kamień, glina, krzemień i rudy |
| Mierniczy (surveyors) | Scouting | żyzność ziemi, dzikie zboże, bydło, owce (`survey.soil`) |

- **Badają tylko stojąc** i od razu cały krąg 3 heksów wokół siebie (`survey.radius`). Idąca jednostka nie bada nic. Ich wzrok to też 3 heksy.
- **Rudy rozpoznaje się od ery technologii, która je wykorzystuje**, nawet zanim lud ją zna (`survey.eras`): miedź od neolitu, cynę, złoto i srebro od epoki brązu, żelazo i marmur od epoki żelaza, saletrę od średniowiecza. Węgla, ropy itp. w tych erach nie znajdzie się wcale. Heks zbadany we wcześniejszej erze trzeba zbadać jeszcze raz, żeby znaleźć rudy nowej ery.
- **Znalezisko zostaje na zawsze** i od razu trafia na mapę ludu. Kronika podaje, co znaleziono.
- **Budynki na złożach** (kopalnie, glinianki, premie za złoże) liczą tylko złoża, które lud zna. Przy okazji naprawiony błąd: kopalnie miedzi, cyny i żelaza nie dawały się zbudować, bo te rudy leżą płytko pod ziemią, a gra uznawała tylko złoża na powierzchni.
- **Panel jednostki:**
  - stojąca: „Standing: they look at … within 3 hexes”;
  - idąca: „Walking: … only when they stand”;
  - lista „Found around” z odległością; kliknięcie pokazuje miejsce na mapie.
- **Panel heksa** poza naszą ziemią: „Soil not surveyed” albo żyzność, oraz „Rock not prospected in this era”.
- **AI:** gdy ma dwie osady i po 10. minucie, trenuje po jednym poszukiwaczu i mierniczym. Wysyła je na niezbadaną ziemię w promieniu ok. 12 heksów od swoich osad. Cele losuje z własnego skrótu, nie ze strumienia losowego AI.
- **Pomiar** (6 ziaren, 4 ludy, 40 minut): ludzi 1370 → ok. 960, wymarłych ludów 5 → 4.
  - Taka sama zmiana bez nowych jednostek (zabranie 4 ludzi i 30 jedzenia stolicy w 10. minucie) daje 1079.
  - Gra AI jest więc bardzo czuła na drobne zmiany, a ten spadek jest w granicach tego rozrzutu.
  - Do strojenia AI potrzeba więcej ziaren (znany problem 23).
- **Scenariusz testowy:** `05_prospecting`.

## Ulepszanie budynków na miejscu (decyzja Patryka z 2026-11-19)

- Budynek przechodzi w swój lepszy rodzaj, gdy lud zna jego technologię (pole `upgradesTo` w danych):

| Z | Na |
| --- | --- |
| Huts | Stone house |
| Storehouse | Great storehouse |
| Gatherers' hut | Farm |
| Hunters' lodge | Pasture |
| Fishing hut | Harbour |
| Farm | Manor |
| Toolmaker | Blacksmith → Steelworks |
| Armourer | Weaponsmith |
| School | Academy → University |

- Stary budynek znika, nowy buduje się na jego miejscach. Większy rodzaj potrzebuje wolnych miejsc obok (np. farma zajmuje cały heks).
- Połowa ceny starego budynku liczy się do nowego (`upgradeCreditPercent` 50, nie więcej niż cena nowego); resztę noszą tragarze jak przy każdej budowie.
- W czasie przebudowy budynek nie działa (domy też: chaty znikają, dopóki nie stanie kamienny dom).
- Warunki terenu nowego rodzaju muszą pasować (farma potrzebuje żyznej ziemi).

## Podgląd heksa (2026-11-19)

Po kliknięciu heksa z budynkami lewy panel pokazuje:
- ziemię (biom, rzeźba, czyja, złoże, zagajnik);
- cztery miejsca heksa jako obrazek 2 × 2: każdy budynek to blok z ramką w kolorze stanu (pracuje, czeka, stoi, budowa z procentem), wolne miejsca są puste;
- „Protection and works”: każdy rodzaj (rów, palisada, mur, akwedukt, most) z „yes” albo „no” i technologią, której brakuje, oraz przyciski usuwania;
- każdy budynek osobno: stan, pracownicy, produkcja, ulepszenie (koszt albo powód, czemu nie teraz; „Later: …” przed technologią) i przyciski w jednym rzędzie: ulepsz, priorytet, stop/start, wyburz.

## Racje (decyzja Patryka z 2026-11-19)

Jak w RTS-ach: jednostka nie kosztuje nic, kiedy żyje. Nikt też nie je „po trochu” co takt.

- **Raz na minutę każda osada karmi wszystkich swoich ludzi naraz:** mieszkańców i jednostki, które z niej wyszły (wojsko, zwiadowców, tragarzy, osadników w drodze, statki). Każda osoba zjada 1 jedzenie (`food.foodPerPersonPerMinute`), zdarzenia jak uczta czy klęska to zmieniają.
- **Kolejność, gdy jedzenia brakuje:** najpierw dostają jednostki (walczące przed resztą, potem od najstarszej), potem pracownicy budynków, na końcu ludzie bez pracy. Czyli **pierwsi głodują bezrobotni, potem pracujący, na końcu jednostki**.
- **Głodni pracują słabiej:**
  - po pierwszej nieudanej racji 75% (`rations.hungryPercent`): budynki tylu, ilu pracowników głoduje, zbieranie bezrobotnych, siła i szybkość jednostek;
  - po drugiej z rzędu 50% (`starvingPercent`) i ginie 20% głodnych (`starveLosePercent`, co najmniej 1); to samo przy każdej następnej;
  - jedna pełna racja kończy głód.
- **Osadnicy** jedzą najpierw z zapasów, które niosą do nowego obozu.
- **Tempo jedzenia w osadzie** (+/- przy jedzeniu) nadal pokazuje wszystko na minutę: produkcję minus racje mieszkańców i jednostek. Magazyn rośnie przez minutę i spada raz, przy racji.
- **Usunięte:** ciągłe jedzenie żołnierzy z pierwszej osady, powrót patroli do osady po jedzenie i jedzenie na drogę tragarzy i karawan (`tripFoodPer10Hexes`).
- **Co widzi gracz:**
  - w panelu osady linia „Ration: X food for N residents and M in K units in S s”, czerwona przy głodzie, z liczbą głodnych (ilu to pracownicy) i zmarłych;
  - w panelu jednostki „Fed by X: ration in S s” albo HUNGRY / STARVING;
  - alarm „rations”, gdy do następnej racji zabraknie jedzenia, i alarm głodu z liczbą głodnych;
  - okno **6 Units** (także klawisz P i przycisk „Patrols N” na górze): wszystkie jednostki, najpierw patrole i głodni, z kolumną „Fed by”, przyciskiem „Stop patrol” przy każdej i „Stop all patrols”.
- **Patrole** chodzą po swojej pętli w kółko, aż gracz je zatrzyma. Zatrzymana jednostka pamięta ostatnią pętlę: „Resume” wysyła ją na tę samą trasę, „Recall” odsyła do osady, która ją karmi. W oknie Units działa to dla jednej jednostki, dla zaznaczonych (kolumna Pick, „pick patrols”) i dla wszystkich.
- **AI:** szkoli wojsko w osadzie, która ma najwięcej jedzenia na zbyciu i wyżywi nowy oddział.
- **Pomiar** (3 ziarna, 4 ludy, 40 minut; przed → po): ludzi 728 → 953, osad 40 → 37, jednostek 41 → 43, wymarłych ludów 0 → 1. Próbek „lud głoduje” 615 → 1078, bo flaga głodu trwa teraz całą minutę po nieudanej racji (wcześniej tylko przy pustym magazynie). Jednostki głodne w 3–4% próbek.

## Ery jako poziomy, kategorie technologii, alarmy (decyzje z 2026-11-18)

- **Ery to poziomy ludu.**
  - Następna era otwiera się, gdy lud zna 5 technologii swojej obecnej ery. Ulepszenia też się liczą, technologie podstawowe nie.
  - Liczba jest w `eraAdvanceTechs` w `data/sim/game.json`.
  - Technologii z ery, do której lud jeszcze nie doszedł, nie da się zacząć.
  - W obrębie ery nadal trzeba iść po drzewie, bo technologia wymaga doświadczenia w poprzednich.
- **Kategorie technologii:** podstawy, jedzenie, przemysł, budownictwo, handel, wojsko, zarządzanie, nauka. Okno Research grupuje według nich.
- **Alarmy** (liczone na żądanie dla widoku, nie zapisywane):
  - atak na osadę;
  - wróg blisko (wojsko ludu, z którym walczymy, do 3 heksów za ziemią osady);
  - głód;
  - brak dobra, przez który budynek stoi;
  - brak domów (osada nie rośnie);
  - pełny magazyn dobra, które osada wytwarza;
  - budowa bez materiałów;
  - za mało ubrań przed zimą (mniej niż 1 zestaw na osobę na minutę przed zimą i w zimie);
  - za mało jedzenia na następną rację (racje).
- **Odpowiedź na brak:** w panelu osady są przyciski „Bring 20 X from Y” (jeden kurs) i „Supply route” (stała trasa), z najbliższych osad, które mają tego dobra co najmniej 40.

## Decyzje po testach z 2026-11-19 (`claude/decyzje-po-testach.md`)

| Nr | Zasada | Dane |
| --- | --- | --- |
| M1 | Wyjście z ery wymaga też technologii-bramy tej ery: neolit → obróbka miedzi, epoka brązu → obróbka brązu, epoka żelaza → obróbka żelaza. Karta bramy ma złotą ramkę i znak „»”, a podpowiedź mówi, do której ery prowadzi | `eraGateways` |
| M2, M3 | Budować na złożu wolno, złoże nie znika; gracz dostaje w kronice uwagę, że kopalnia potrzebuje wolnego heksu. AI omija znane złoża, które da się wydobywać | — |
| M4 | Każdy start ma miedź w odległości 3–7 heksów i cynę w odległości 5–12; gdy mapa ich tam nie ma, gra kładzie je na wolnym lądzie | `startDeposits` |
| G1 | Nowy obóz od razu ma chatę zbieraczy, opłaconą z zapasów osadników (40 jedzenia i 30 drewna) | `foundWith` |
| E1 | Każde wypowiedzenie wojny kosztuje 10 punktów reputacji; AI płaci daninę dopiero, gdy straciła w tej wojnie co najmniej 5 ludzi | `war.declareReputation`, `war.tributeNeedsKills` |
| E2 | Awans osady wymaga ludzi: wioska 40, miasteczko 150, miasto 400 | `settlementLevels[].minPeople` |
| E3 | Danina zabiera najwyżej 10% jedzenia płacącego | `war.tributeFoodMaxPercent` |
| E5 | Doświadczenie liczy się tylko wtedy, gdy budynek naprawdę pracuje (ma surowiec i miejsce na wyroby) | — |
| G3 | Stała trasa ma dwa progi: ile zostaje w źródle i do ilu napełniać cel (0 = bez limitu). Ustawia się je w panelu osady nad przyciskami tras | `RouteCommand.KeepAtSource`, `UntilTargetHas` |
| — | Rozejm po pokoju trwa `war.truceSeconds` = 180 s | `war.truceSeconds` |
| M6 | Złoża z ery, do której lud jeszcze nie doszedł, widać jako „???” (na mapie i w opisie heksa). Po wejściu w tę erę lud wie, co to jest, bez ponownego badania skał | `survey.eras` |
| G2 | Osada pusta przez 60 s (nie ostatnia ludu) zmienia się w ruinę: ziemia jest niczyja, budynki stoją zrujnowane. Osadnicy na środku ruiny albo obok przejmują ją dla swojego ludu, a budynki wracają jako budowy za 50% ceny | `ruins.emptySeconds`, `ruins.rebuildPercent` |
| G6 | Gdy osadzie brakuje towarów na awans, karawany z innych osad przywożą brakujące towary (najpierw z najbliższych). Awans kończy się, gdy wszystko dojedzie. Napad na taką karawanę odwołuje awans | — |
| G5 | Przełącznik: ubrania zużywają się tylko zimą (domyślnie wyłączony, do decyzji) | `clothing.wearOnlyInWinter` |
