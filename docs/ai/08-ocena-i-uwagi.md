# Ocena: co działa, co jest za proste, co jest zbędne

[← spis treści](README.md)

Ta strona to moja (Claude) uczciwa ocena obecnego AI po przeczytaniu całego kodu i trzech grach po 1000 tur. Każda poprawka
zmienia zasady gry, więc decydujesz Ty. **Stan: po PR C (ziemia i migracje)**; punkty, które PR B i PR C zamknęły, są oznaczone.

## Podsumowanie w jednej tabeli

| Obszar | Ocena | Dlaczego |
| --- | --- | --- |
| Wspólna ocena i wybór (czynniki, 3 najlepsze, ostrożność) | **dobre** | jeden wzór dla wszystkich, deterministyczny, wyjaśnialny |
| Gubernator: przeprowadzki, odłamy, wypędzenia | **dobre, rozsądnie złożone** | liczy jedzenie, wygodę, odległość, kierunek, opinię, siłę |
| Gubernator: osiedlenie (G4) | **dobre (od PR C)** | plon dla prawdziwych rolników, porównanie ze zbieractwem i ekspansywnością |
| Gubernator: łupy (G6), dołączanie (G1) | proste, ale wystarczy | rzadkie albo oczywiste sytuacje |
| Wioski | **proste (od PR C, głód po 4g)** | wysyłają osadników (G7), gdy są zatłoczone, pola wyjałowione albo głodują; mała głodna wioska przenosi się do sąsiedniej; budowy i dzielnice w późniejszych etapach |
| Strateg | **dobre (od PR B)** | każda z 6 wag na coś wpływa; nauka rośnie, gdy spotkani wiedzą więcej, wpływ z liczbą spotkanych; podbój czeka na 4e |
| Charakter | **żywy (od PR B)** | każda cecha zmienia decyzję (test `DeadValueTests`); ciekawość zmniejsza strach przed kosztem technologii |
| Dyplomata (4d, po 4g) | **dobre** | ta sama ocena dla obu stron, reaguje na wojnę i napad poza rytmem (ADR 0018); od 4g także wojny z chciwości (łupy, ziemia) i sojusze przeciw wspólnemu wrogowi |
| Skarbnik | **dobre (od PR B), jedna słabość** | osady sieci koordynują się, jedzenie kontra wyroby, premia za handel; zostało przełączanie odzież ↔ chleb |
| Uczony | **dobre (od PR B)** | priorytet z położenia, braków i charakteru, koszt jako przeszkoda; rolnictwo przychodzi wcześniej (do oceny) |
| Wiedza AI | **częściowo naprawione (PR B, PR C)** | obce obozy i ich siła tylko z pól widzianych w ostatnim kroku; ziemia wyceniana z technologiami cywilizacji; jedzenie pól nadal dzisiejsze |
| Wyprawy (G8) | **nowe, rzadkie** | 8–97 na grę; zależą od ciekawości i ekspansywności |
| Rytm decyzji | **dobre (od ADR 0018, PR C)** | każda cywilizacja w rytmie swojej ery, reakcje poza rytmem; zwiad i marsz obozu na stulecie |
| Wydajność | **do pilnowania (PR C)** | 3–4× więcej osad; stare systemy porównują każdą osadę z każdą (patrz pkt 10) |
| Ustroje (etap 4f, PR 1, PR 5) | **proste** | ustrój przechyla wagi, ostrożność i tempo; od PR 5 strateg zmienia go, gdy inny pasuje wyraźnie lepiej (S2); pętla odwetu zamknięta (pkt 14) |
| Przywódcy (etap 4f, PR 2) | **proste** | cechy przywódcy przesuwają charakter, rada wybiera pasującego; kryzys sukcesji obniża stabilność od PR 3 (pkt 15) |
| Stabilność (etap 4f, PR 3, PR 5) | **AI reaguje prosto** | przeciążenie hamuje ekspansję, niepokoje: bezpieczeństwo w górę, wojna w dół, spokojniejszy ustrój (pkt 16) |
| Bunty i secesja (etap 4f, PR 4) | **reguła, nie decyzja** | nowe ludy powstają z dalekich osad; AI rodzica może się mścić (pkt 17) |
| Testy wsadowe (etap 4g) | **4 dziury poprawione** | po poprawkach: głodujące wioski 21 zamiast 322 na grę, wojny o łupy w 81 grach, podboje w 19, sojusze 4 na grę (pkt 18) |
| Po 4-godzinnym biegu (etap 5b) | **odwet tylko w zasięgu** | wojna o odwet wymaga sąsiada w zasięgu 16 pól; zasięg władzy i dryf dają ~40 małych ludów zamiast ~11 dużych, więc wojen i podbojów jest dużo więcej (pkt 19) |

## 1. Wagi i cechy, które nic nie zmieniają (zamknięte w PR B)

Waga celu i mnożnik charakteru zmieniają decyzję **tylko wtedy, gdy porównywane opcje służą różnym celom** albo gdy ocenę porównujemy
ze stałym progiem. Jeśli wszystkie opcje pomnożymy przez tę samą liczbę, kolejność i szanse w losowaniu zostają te same. Przed PR B
tak było w skarbniku (dobrobyt × handlowość na wszystkich recepturach), u uczonego (nauka × ciekawość na wszystkich technologiach), w
G2 i G3 („zostać” dostawało tę samą wagę ekspansji co przeprowadzka), a waga wpływu nie była używana nigdzie.

Po PR B:

| Wielkość | Gdzie działa |
| --- | --- |
| waga ekspansji × ekspansywność | G2, G3: przeprowadzka kontra „zostać” (zostać ma połowę odchylenia); G5: próg odłamu; G2: wolne miejsce kontra obcy obóz; U1: udźwig |
| waga podboju × agresja | G2: obcy obóz; D2: wojna; U1: technologie wojskowe |
| waga dobrobytu × handlowość | T1: wyroby kontra jedzenie; U1: technologie wyrobów; D2: umowa handlowa |
| handlowość (sama) | T1: premia dla wyrobów cennych, gdy towary płyną do partnera |
| waga bezpieczeństwa × ostrożność | T1: jedzenie; U1: jedzenie, ogień, wioski; D1, D2: pakt, sojusz, pokój |
| waga nauki × ciekawość | U1: część „podstawa” i dziedzina nauki; ciekawość zmniejsza też strach przed kosztem |
| waga wpływu | U1: technologie społeczne, nastrój |
| lojalność, otwartość, mściwość | D2, D3; pierwsze wrażenie; długość pamięci napaści |

**Test, który tego pilnuje:** `tests/CivSim.Tests/Ai/DeadValueTests.cs`. Bierze zapisane sytuacje z gier (obozy z epoki kamienia,
wioski neolitu z handlem, kilka sytuacji ułożonych celowo: wędrówka, wypędzenie, łupy, rynek), zmienia po kolei każdą cechę (0 i
1000), każdą wagę celu (300 i 3000) i każdy parametr `data/ai.json` (0, połowa, podwojenie, 10 razy, milion) i sprawdza, czy choć
jedna decyzja (albo opinia o sąsiedzie po kilku krokach) wychodzi inna. Wartość, która niczego nie zmienia, wywala test.

Uczciwie: test sprawdza, że wartość **na coś** wpływa, a nie że wpływa **sensownie**. Mściwość zmienia tylko długość pamięci napaści,
więc w teście jest jedna napaść ułożona z góry; premia handlowa działa tylko w sytuacji „rynek” (wioski z handlem i surowcami do
chleba i odzieży naraz).

## 2. AI zna dzisiejszy stan pól, które tylko pamięta

`CLAUDE.md` mówi: AI widzi tylko to, co odkryła jej cywilizacja. Dziś AI **wybiera** tylko pola znane (widziane w ostatnich 300
latach), ale **ocenia** je według dzisiejszego stanu: obecnej dzikiej żywności, obecnej temperatury, obecnego mieszkańca i jego
dzisiejszej liczby wojowników. Liczy też jedzenie pól wokół znanego pola, których mogło nie widzieć. G6 (łupy) w ogóle nie sprawdza,
czy osada jest znana.

Dyplomata (4d) ma tę samą dziurę: siłę rywala liczy z osad stojących na **znanych** polach, ale z ich **dzisiejszą** liczbą wojowników.

Przykład: plemię widziało dolinę 250 lat temu. Dziś obok mieszka obcy obóz. AI „wie”, ilu ma wojowników, i może go zaatakować,
choć nigdy go nie widziało.

**Zrobione w PR B:** obce obozy (G2) i wojownicy rywala (dyplomata) liczą się tylko z pól widzianych w ostatnim własnym kroku
(`MapMemory.SeesNow`). Łupy (G6) tylko ze znanych pól. Jedzenie okolicy zostaje dzisiejsze, bo przy krokach po 50 lat różnica jest
mała, a zapamiętywanie wartości kosztuje pamięć. Skutek w grach: wypędzeń jest o 50–80% mniej.

## 3. Uczony: priorytet zamiast „najtańsze” (zamknięte w PR B)

Dawniej uczony patrzył tylko na koszt. Od PR B technologia ma priorytet z tego, co daje tej cywilizacji (zbieranie na jej polach,
receptury dla jej rzemieślników, efekty, wioski z żyzności), ważony celami i charakterem, plus 25% najlepszej technologii „za nią”;
koszt jest przeszkodą, którą ciekawość zmniejsza. Opis: [uczony](05-uczony.md).

**Do Twojej oceny:** rolnictwo przychodzi teraz wcześniej (pierwszy neolit o ok. 2000–2900 lat wcześniej, [statystyki](09-statystyki.md)).
Jeśli to za szybko, najprostsza gałka to `scholar.villageWorthPerPerson` (0,1). Zostały uproszczenia: uczony patrzy na wartości
bazowe towarów (nie na ceny w sieci) i tylko jeden krok dalej.

## 4. Osiedlenie to w praktyce próg, a nie wybór (zamknięte w PR C)

Rachunek G4 zakłada, że każde pole strefy uprawia 40 rolników, choć obóz ma ich kilkudziesięciu. Dlatego plon wychodzi zwykle
kilkanaście razy większy od potrzeb: w 539 osiedleniach z trzech gier mediana `farm yield` to ok. 1650%, najmniej 169%, najwięcej
5594%. Próg 150% jest więc spełniony praktycznie zawsze. Nie ma też porównania z dalszym
koczowaniem, wag ani charakteru.

**Propozycja:** liczyć plon dla prawdziwej liczby producentów (najlepsze pola najpierw, jak robi to system rolnictwa) i porównać z
tym, co daje zbieractwo; ekspansywna cywilizacja mogłaby dłużej koczować.

**Zrobione (PR C):** plon liczymy dla producentów obozu na najlepszych polach strefy (najwyżej `farmersPerCell` na pole), a
osiedlenie wymaga też, żeby rolnictwo dało więcej niż zbieractwo razy waga ekspansji i ekspansywność
(`settleOverGatheringPercent` = 100%). W 1469 osiedleniach z trzech gier mediana `farm yield` to 280% (najmniej 151%,
najwięcej 363%), a `over gathering` 292% (101–1006). Próg 150% jest teraz prawdziwą granicą. Osiedleń jest 2–3 razy więcej niż
przed PR C (428–541 zamiast 161–252), bo większość nowych obozów zakładają osadnicy z wiosek (G7) i one też osiadają.

## 5. Odstępy czasu: rozwiązane przez zegar świata (ADR 0018)

Do etapu 4d trzy parametry AI liczyły **tury świata**, a długość tury ustalała najbardziej rozwinięta cywilizacja. Plemię z epoki
kamienia w świecie, w którym ktoś wszedł w neolit, robiło przegląd strategii co 375 lat zamiast 750. Od ADR 0018 liczą **własne
kroki cywilizacji**, czyli jej rytm:

| Parametr | Wartość | W epoce kamienia | W neolicie | W erze przemysłowej |
| --- | --- | --- | --- | --- |
| `reviewEverySteps` | 15 kroków | 750 lat | 375 lat | 15 lat |
| `searchEverySteps` | 3 kroki | 150 lat | 75 lat | 3 lata |
| `minStepsBetweenMoves` | 2 kroki | 100 lat | 50 lat | 2 lata |

To celowo nie są lata: Twoja zasada „każda cywilizacja decyduje co tyle lat, ile wynika z jej ery” oznacza, że rozwinięta cywilizacja
planuje częściej. Tempa (wzrost, psucie, pamięć relacji) dalej są na rok.

Uwaga do zostawionej rzeczy: zwiadowca (15 pól) i ruch obozu (10 pól) to nadal „na własny krok”, więc rozwinięte cywilizacje
odkrywają świat i wędrują szybciej w przeliczeniu na rok. **Zrobione (PR C):** zwiadowcy to 2 wyprawy na stulecie
(`scoutsPerCentury`), a obóz w drodze idzie 20 pól na stulecie (`marchCellsPerCentury`, najwyżej jeden ruch na krok);
`data/perception.json`.

## 6. Gubernator nie zna swoich technologii (zamknięte w PR C)

Wartość miejsca liczy jedzenie pola **bez technologii cywilizacji**, a pól wody w ogóle nie liczy. Plemię z rybołówstwem, które
naprawdę je ryby z wybrzeża, wycenia wybrzeże jak plemię bez rybołówstwa. Tak samo premie z łowiectwa i hodowli. AI wycenia więc
ziemię gorzej, niż ona naprawdę karmi, i rzadziej przenosi się nad morze.

**Propozycja:** liczyć jedzenie z technologiami cywilizacji (system zbierania już to potrafi) i dodawać pola wody, jeśli cywilizacja
zna rybołówstwo.

**Zrobione (PR C):** jedzenie miejsca liczymy z technologiami zbierania cywilizacji (`GovernorAdvisor.Land`, wspólne dla
cywilizacji o tym samym zestawie), a rybacy liczą pola wody przy lądzie swojej strefy.

## 7. Dziennik „Dlaczego?” czasem wprowadza w błąd

Dziennik pokazuje 3 czynniki najdalsze od 1000. W seedzie 7 pierwszym czynnikiem jest `character` w 3600 z 7977 wpisów (45%), choć (pkt 1) nie wpłynął na wybór, bo wszystkie
opcje miały ten sam. Przykład z gry: `Scholar, SetResearch: character 105%, research cost 100%`. Prawdziwy powód to „najtańsze”.

**Propozycja:** w dzienniku pomijać czynniki wspólne dla wszystkich opcji danej decyzji.

## 8. Wioski bez AI (zamknięte w PR C)

Po osiedleniu gubernator nie robi z wioską nic: nie wyśle odłamu, gdy jest pełna, nie przeniesie ludzi, gdy brakuje jedzenia. Na
[wykresie](09-statystyki.md) widać, że po turze ~300, gdy większość osad jest już wioskami, liczba decyzji spada kilkukrotnie.
To zgodne z planem (budowy i dzielnice to późniejsze etapy), ale warto wiedzieć, że od neolitu AI „śpi”.

**Zrobione (PR C):** wioska wysyła 60 osadników (G7), gdy jest zatłoczona (od 80% miejsc, wcześniej dla ekspansywnych) albo gdy
jej pola mają mniej niż 70% naturalnej żyzności; najwyżej raz na 100 lat, w wolne miejsce poza strefami własnych osad.
W 1000 tur to 424–473 grup na grę. Ludność na końcu jest 1,6–2,3 razy większa, a ludzie żyją średnio 20–24 pola od startu
zamiast 10–12 ([wykres](09-statystyki.md#ziemia-i-migracje-pr-c)). Decyzji po turze 300 jest więcej niż przed PR C, ale nadal
mniej niż na początku gry, bo wioski poza osadnikami nic nie robią.


## 9. Inne drobne rzeczy

- **Pierwszeństwo przy wyborze pól:** gubernator liczy cywilizacje zawsze w tej samej kolejności (po numerze), a wybrane pole jest
  rezerwowane. Cywilizacja 1 zawsze wybiera pierwsza. Rozstrzyganie komend rotuje kolejność, ale rezerwacja już nie. Mały, stały przechył.
- **Odłamy i łączenie:** w 1000 tur jest ~1400–1600 odłamów i ~1250–1450 dołączeń. Możliwe, że wiele odłamów się kurczy i wraca
  (nie sprawdziłem tego dokładnie). Warto zbadać w raporcie 100 gier (4g).
- **Skarbnik (PR B):** osady jednej sieci wybierają po kolei i widzą wybory poprzednich (zamknięte). Przełączanie „praca ↔ brak
  receptury” prawie zniknęło. **Zostało przełączanie odzież ↔ chleb** (700–950 zmian na grę): cena jedzenia skacze z plonami o więcej
  niż histereza 25%. Możliwa poprawka: liczyć cenę jedzenia ze średniej z kilku kroków albo podnieść `recipeSwitchBetterByPercent`.
  Nie zmieniałem tego, bo to strojenie, a nie błąd.
- **Wędrówki i łupy prawie się nie zdarzają** (0–8 na 1000 tur). Próg 60% i rzadka siatka sprawiają, że wędrówka jest wyjątkiem;
  łupy są ostatnie w kolejności.
- **Liczby zaszyte w kodzie (PR B):** 11 liczb strojenia przeniesionych do `data/ai.json` (próg osiedlenia, łupów, wędrówki, wygody,
  siła opinii, siatka i bliskość dalekich miejsc, rozłożenie przeglądów). W kodzie zostały tylko części metody
  ([lista](07-parametry.md#liczby-zaszyte-w-kodzie-nie-w-danych)).
- **Dyplomata: rytm przy krótkich krokach** (naprawione w ADR 0018): przegląd liczy miesiące, więc przy kroku 3-miesięcznym wypada
  raz na 50 lat, a nie 4 razy w roku przeglądu. Tak samo pogłoski liczą szansę w milionowych za dokładne miesiące kroku.
- **Dyplomata: dużo odrzuceń.** W seedzie 42: 666 propozycji, 619 odrzuconych, 32 przyjęte. Próg propozycji (1,0) jest wyższy niż
  próg przyjęcia (0,8), ale druga strona często ocenia gorzej (np. czuje mniejsze zagrożenie). Do strojenia w 4g.
- **Czego GDD obiecuje, a jeszcze nie ma:** plany wieloetapowe (8.6), mapy zagrożeń i napięcia (8.2), prognozy zasobów („za 12 tur
  zabraknie żelaza”). To zgodne z planem etapów, ale dziś AI reaguje tylko na bieżący stan, nie planuje.

## 10. Wydajność: stare systemy porównują każdą osadę z każdą (PR C)

Po PR C osad jest 3–4 razy więcej (350–450 zamiast 80–130 na mapie standard po 1000 tur). Kilka systemów sprzed PR C sprawdza
każdą parę osad: sieci handlowe (`TradeNetworks.Build`, budowane 5 razy na turę), przygotowanie dyplomaty, `CivFacts.Measure` i
wysyłanie nadwyżek z magazynów. Par jest ~11 razy więcej, więc tura wydłużyła się z 16–19 ms do 37–52 ms (cel 50 ms).
Koszt na jedną osadę jest taki jak przed PR C.

W PR C przyspieszyłem trzy rzeczy bez zmiany wyniku (hash stanu identyczny): odległość na kuli pomija obrazy pola, które nie mogą
być bliżej, nadwyżki sortują osady po wyliczonej raz odległości, a sieci nie liczą odległości dla osad już połączonych. To dało
ok. 30–40% (seed 23: 81 → 52 ms).

**Propozycja na później:** siatka przestrzenna osad (kubełki po kilka pól), żeby pary liczyć tylko dla bliskich osad, i jedna
budowa sieci handlowych na turę. To zmiana kilku systemów naraz, więc osobny PR. Gdyby gra miała być szybsza od razu, można
rzadziej wysyłać osadników (`villageSettlersEveryYears`) – ale to zmienia tempo gry, więc decyzja należy do Ciebie.

## 11. Fachy: AI wie o nich jeszcze mało (etap 4F, PR 2)

Od PR 2 dobra z technologii, pola i receptury dają tyle, ilu jest fachowców. AI wie o tym w dwóch miejscach: skarbnik wycenia
recepturę jej fachowcami (receptura bez fachowców jest warta zero), a gubernator osiada tylko z rolnikiem i liczy rolników, których
wioska wyuczy. Od PR 3 uczony wybiera tylko technologie, dla których cywilizacja ma ludzi. **Za proste:** wycena ziemi (G2, G5,
wędrówki, cel ekspansji) liczy pełne dobra technologii, także wtedy, gdy obóz nie ma myśliwych ani rybaków; uczony nie wybiera
„drogi”, która najpierw wyuczy brakujących ludzi (widzi tylko to, co może badać teraz); nauka fachu jest regułą systemu, a nie
decyzją AI (ile uczyć). To ostatnie zostawiam do decyzji Patryka: reguła działa, a decyzja AI ma sens dopiero z kosztem nauki
(budynki, szkoły) w późniejszych erach.

**Skutek uboczny do obserwacji:** w środkowych stuleciach jest więcej obozów i przeprowadzek niż przed PR 2, bo obóz osiada dopiero,
gdy ma rolnika, a rolnictwo rozchodzi się z mistrzami, nie od razu do wszystkich. Liczby: `09-statystyki.md`.

## 12. Wojna: zmęczenie rosło głównie z lat (etap 4e, PR 1; poprawione w PR 2)

- **Co jest za proste:** w epoce kamienia krok ma 50 lat, a zmęczenie rośnie o 1 punkt na rok, więc po 2 krokach każda strona ma 100
  niezależnie od strat i wyniku. Pokój przychodzi „z nudów”, a nie z przegranych bitew. Straty w napaściach są małe (kilka osób).
- **Cel wojny wybiera dyplomata z prostej reguły** (roszczenie → ziemia, inaczej odwet). Nie waży łupów ani wartości osady.
- **PR 2 (ADR 0022):** walka toczy się w kampaniach co miesiąc, zmęczenie rośnie z miesięcy kampanii i strat, a wojna bez kampanii
  wygasa po 5 latach. Wojny trwają teraz 5–6 lat (w tym kilka do kilkunastu miesięcy kampanii).
- **Co dalej jest za proste:**
  - przegrany prawie nigdy nie przyjmuje warunków (opinia o wrogu −100 daje czynnik 0,3, a oddanie czegoś jeszcze go obniża), a po
    odmowie ta sama propozycja nie wraca przez 100 lat, więc wojny kończą się zwykle cichym białym pokojem bez okupu;
  - dowódca widzi tylko cele w zasięgu wzroku; resztę „idzie zobaczyć”;
  - jedna osada na kampanię, walka jednym rzutem, bez terenu i morale.
- **Plan:** PR 3 (armie z wielu osad, uzbrojenie, tragarze), PR 4 (bitwa w rundach), PR 5 (dowódca co miesiąc), PR 6 (strojenie,
  m.in. czynnik „przegrywam” w ocenie pokoju i krótszy `retryYears` dla pokoju).

## 13. Dowódca: bitwa w głowie i odwołanie co miesiąc (etap 4e, PR 5)

- **Co działa:** dowódca rozgrywa 25 bitew bez losowania na tych samych zasadach co prawdziwa bitwa, więc zna skutek terenu, uzbrojenia,
  morale i głodu, a nie tylko „udział w sile”. Nie zna niczego, czego jego cywilizacja nie widzi. Liczy drogę poza zasięgiem tragarzy:
  „głód złamał” spadł z 32 do 5 kampanii w 3 grach. Co miesiąc może zawołać armię (K2): to 38 odwołań w 3 grach (wszystkie, gdy armia
  zobaczyła z bliska zbyt silny cel), zamiast reguły systemu „zobaczyli, jak silny jest cel, i zawrócili” (40 w PR 4).
- **Co jest za proste:**
  - szczęście jest stałe przez całą spodziewaną bitwę (5 poziomów na stronę); prawdziwa bitwa losuje je co rundę, więc dowódca
    trochę przecenia skrajne wyniki (test pozwala na ±25 punktów różnicy);
  - armie wroga w polu nie bronią celu: dowódca liczy tylko obrońców w osadzie. Obrona armią przyjdzie z wieloma armiami;
  - dowódca nie czeka (np. aż dojdą tragarze albo wróg osłabnie), tylko idzie dalej albo wraca;
  - zapas jedzenia: armia bierze najwyżej 3 miesiące, a tragarze niosą mało, więc kampanie wciąż głodują średnio 2–4 miesiące
    (tylko rzadziej się przez to łamią). To strojenie zaopatrzenia na PR 6.
- **Po strojeniu (PR 6):** głód spadł z 42% do 23% miesięcy wypraw; dowódca liczy jedzenie okolicy na całą wyprawę, przez co
  w niektórych grach wysyła mało armii (seed 23: 3 kampanie w 5 wojnach). Wojny wciąż kończą się zwykle ciszą i białym pokojem (decyzja
  Patryka: tak zostaje), więc zwycięstwa rzadko coś dają poza łupami. Do przemyślenia przy kolejnych erach: wojny o ziemię są rzadkie.
- **Co może być zbędne:** próg strat `maxLossPercent` 50 rzadko decyduje sam, bo przy szansie ≥ 60% straty są zwykle małe; zostaje,
  bo test martwych wartości pokazuje, że zmienia decyzje, i będzie ważniejszy z murami i lepszą bronią obrońców.

## 14. Ustroje i pętla odwetu (etap 4f, PR 1)

- **Co działa:** ustrój z danych zmienia cztery rzeczy naraz: wagi celów stratega, ostrożność przy wyborze, tempo przeglądów
  stratega i dyplomaty i opóźnienie decyzji państwowych (wojna, zmiana ustroju). W 3 grach oba ustroje występują (6–7 wodzostw,
  3–7 rad starszych na 10–12 żywych cywilizacji). Rada starszych wypowiada wojnę, ale jej armie czekają 4 lata.
- **Co jest za proste:**
  - ustrój dobiera się raz, z charakteru, na starcie; AI go nie zmienia (wybór ustroju przez stratega: PR 5, po buncie: PR 4);
  - przechylenie wag działa tylko tam, gdzie opcje służą różnym celom (jak każda waga celu, pkt 1); wodzostwo częściej wybiera wojnę,
    bo wojna jest porównywana ze stałym progiem `declareWarAbove`.
- **Pętla odwetu (stary problem, teraz wyraźniejszy):** jedna cywilizacja wypowiada wojnę o odwet temu samemu sąsiadowi w każdym swoim
  kroku (co 50 lat), łamiąc rozejm; po kampanii przychodzi cisza i biały pokój, a uraz zostaje. Seed 23: 47 z 48 wojen wypowiedziała
  cywilizacja 10 (wodzostwo); seed 7: 23 z 30 cywilizacja 0; seed 42: 11 z 26. W PR 6 bez ustrojów było podobnie, tylko rzadziej
  (seed 7: 32 wojny, 25 z zerwaniem umowy). Do strojenia w PR 5 etapu 4f: odwet powinien gasnąć po wojnie, a zerwanie rozejmu
  kosztować więcej.
- **Zamknięte w PR 5 etapu 4f:** podpisany pokój zamyka rachunek krzywd (`Relation.SettledMonth`): napaść, agresja i secesja sprzed
  pokoju przestają być powodem do odwetu (nadal obniżają sympatię i zaufanie). Nowa krzywda po pokoju jest nowym powodem. Wyniki w
  [statystykach](09-statystyki.md#ai-wobec-państwa-etap-4f-pr-5). Biały pokój po cichej wojnie też jest pokojem, więc też zamyka rachunek.

## 15. Przywódcy (etap 4f, PR 2)

- **Co działa:** każdy lud ma przywódcę z imieniem w swoim języku, wiekiem i 2–3 cechami. Cechy dodają się do charakteru ludu
  (np. kupiec: handlowość i otwartość +200‰), więc wszyscy doradcy AI widzą inny charakter, póki ten przywódca rządzi. Rada starszych
  wybiera z 3 kandydatów tego, który najbardziej pasuje do ludu (uczonych lud dostaje uczonego w około 80% wyborów zamiast 42%).
- **Co jest za proste:**
  - przywódca nie podejmuje własnych decyzji, tylko przesuwa charakter; nie ma ambicji ani zdrady;
  - kryzys sukcesji jest zapisany (`CrisisUntilMonth`), ale jeszcze nic nie zmienia: zacznie obniżać stabilność w PR 3;
  - ustrój nadal nie zależy od przywódcy (wybór ustroju przez AI: PR 5).
- **Ryzyko:** cechy przywódcy dodają losowości do wojen (patrz [statystyki](09-statystyki.md#przywódcy-etap-4f-pr-2)); to zamierzone,
  ale po strojeniu w PR 5 trzeba sprawdzić, czy ostrożny lud z wojowniczym wodzem nie wpada w pętlę odwetu częściej niż inne.

## 16. Stabilność i AI (etap 4f, PR 3; reakcja AI od PR 5)

- **Co działa:** stabilność 0–100 z nastroju osad ważonego wpływem warstw, premii ustroju i kar (administracja, kryzys sukcesji,
  bezkrólewie, utrata stolicy); poniżej 30 niepokoje obniżają wydajność. W 3 grach 8–12% lat cywilizacji to niepokoje.
- **Czego brakuje (celowo, PR 5):** AI nie widzi stabilności. Strateg nie hamuje ekspansji przy przeciążonej administracji i nie
  zmienia ustroju, gdy jest źle; dyplomata nie unika wojny przy niskiej stabilności. Dziś stabilność jest więc karą, której AI nie
  rozumie: wielkie plemiona żyją stale z karą za administrację.
- **Od PR 5:** strateg mnoży ekspansję przez pojemność / zużycie administracji (nie mniej niż 50%), w niepokojach podnosi
  bezpieczeństwo × 1,5 i w każdym przeglądzie sprawdza, czy inny ustrój nie pasuje wyraźnie lepiej (w niepokojach liczy się premia
  stabilności ustroju). Dyplomata w niepokojach ocenia wojnę o połowę niżej i nie rozważa jej w bezkrólewiu.
- **Nadal za proste:** AI nie ma działań „na stabilność” (np. świąt, ustępstw dla warstw, rezygnacji z dalekich osad); reaguje tylko
  wagami i ustrojem. Zmiana ustroju kosztuje 2 lata bezkrólewia (−20), więc w niepokojach może chwilowo pogorszyć sprawę.
- **Co jest za proste:** nastrój warstwy = nastrój osady (warstwy nie mają jeszcze osobnych potrzeb politycznych); lojalność osad
  jest liczona, ale niczego jeszcze nie zmienia (bunty w PR 4).

## 17. Bunty i secesja (etap 4f, PR 4)

- **Co działa:** poniżej stabilności 15 wybucha bunt. Dalekie osady bez lojalności odłączają się jako nowy lud z językiem córką, innym
  ustrojem i charakterem bliskim rodzicowi; gdy ich nie ma, władzę bierze nowy przywódca z innym ustrojem. W 3 grach 1 secesja i 2–4
  przewroty na grę. Rodzic pamięta secesję i ma powód do odwetu, ale wojnę wybiera jego dyplomata.
- **Co jest za proste:**
  - bunt to reguła, nie decyzja AI; AI nie próbuje mu zapobiec (PR 5: reakcja stratega na niską stabilność);
  - nowy lud dostaje kopię kontaktów rodzica, ale inni nie dowiadują się od razu, że powstał (spotykają go zwykłą drogą);
  - nie ma wojny domowej (dwie strony walczące o całość): to mogłoby być rozszerzenie po etapie 4g.
- **Do obserwacji:** secesje są duże (połowa państwa naraz). Jeśli Patryk woli mniejsze, wystarczy obniżyć `secessionLoyaltyBelow`
  albo dać buntownikom osobny, mniejszy zasięg zamiast `linkCells` (dziś to samo, co w sieci handlowej).

## 18. Co pokazało 100 gier (etap 4g)

Pełny raport: [12-testy-wsadowe.md](12-testy-wsadowe.md). Najważniejsze dziury, od najpoważniejszej:

1. **Wioski głodują bez reakcji:** mediana 322 razy na grę wioska głoduje 3 własne kroki z rzędu; G7 (osadnicy) nie patrzy na głód.
   Obozy reagują dobrze (mediana 4).
2. **Żadna zamieszkana osada nie zmienia właściciela na wojnie** (0 w 100 grach): podbój tylko celu wojny o ziemię, a ten jest rzadki.
3. **Nikt nie wypowiada wojny o łupy** (0 w 100 grach); 94% wojen to odwet.
4. **Sojusze rzadko działają:** mediana 1 sojusz na grę, sojusznik przystąpił do wojny w 17 grach.

Co działa: wojny się kończą (pokojów tyle, co wojen), dyplomacja i handel w każdej grze, w 95 grach wymiera co najmniej jeden lud,
ustroje zmieniają się około raz na lud na 1000 lat, czas tury 27 ms.

**Po poprawkach AI (gałąź `feat/ai-fixes`, decyzja Patryka „Poprawki AI wszystkie”):** wszystkie cztery punkty poprawione, porównanie
na tych samych 100 seedach w [12-testy-wsadowe.md](12-testy-wsadowe.md#po-poprawkach-ai-decyzja-patryka-z-2026-11-07-poprawki-ai-wszystkie):

1. Głodujące wioski: mediana **21** zamiast 322 (głodna wioska wysyła osadników co 20 lat, mała przenosi się do najedzonej sąsiedniej osady).
2. Podboje: osada zdobyta na wojnie w **19** grach zamiast 0 (wojna o wioskę, którą armia zdobędzie z szansą ≥ 75%). Wciąż rzadkie; do
   sprawdzenia w erze brązu.
3. Wojny o łupy: w **81** grach (mediana 4), tylko gdy lud głoduje, a sąsiad ma pełniejsze spichlerze i jest słabszy.
4. Sojusze: mediana **4** zamiast 1, sojusznik przystąpił do wojny w **38** grach zamiast 17.

Za proste albo do pilnowania: wojny z chciwości nigdy nie łamią umowy (zasada w kodzie, nie cecha charakteru; lojalność liczy się tylko
przy odwecie), a ocena łupów zakłada, że AI widzi zapasy w osadach, które widzi teraz (bez szpiegów i pogłosek).

## 19. Po 4-godzinnym biegu Patryka (etap 5b)

**Co zmieniono w AI:** jedna rzecz. Wojna o odwet (D2) dostała ten sam warunek zasięgu co wojna o łupy: najbliższe osady obu ludów
najwyżej 16 pól od siebie (`reachCells`). Wcześniej lud mógł wypowiedzieć wojnę z zemsty komuś za morzem albo na drugim końcu
kontynentu; armia nie miała jak dojść, więc po 5 latach bez walki wojna kończyła się sama białym pokojem (problem 8 Patryka).
Na kontynentach (seed 7, 6000 tur) wojen o odwet jest 178 zamiast 336.

**Co zmieniło się wokół AI (reguły, nie decyzje):** zasięg władzy ustroju i dryf dalekich osad (`docs/systems/stability.md`),
silniejsze plemiona łowców (własna ziemia), stolice. Skutek dla AI: zamiast ~11 dużych ludów jest ~40 małych (8 gier × 2000 tur),
więc dyplomaci mają dużo więcej sąsiadów. Wojen jest ok. 12× więcej (201 zamiast 16,5 na grę), podbojów 20× więcej (29,6 zamiast 1,5),
a krok świata częściej trwa miesiąc (wojny), więc 2000 tur to ok. 11 600 lat zamiast 48 000.

**Za proste albo do pilnowania:**
- Nadal około 2/3 wojen o odwet kończy się bez kampanii. Teraz to głównie wojny rodzica z ludem, który się od niego odłączył
  (secesja to powód odwetu, decyzja Patryka z 2026-11-07), gdy rodzic nie ma wolnych wojowników albo dowódca uznaje szansę za zbyt
  małą. Kandydat do następnego kroku: dyplomata pyta dowódcę (K1) o szansę przed wypowiedzeniem wojny o odwet, jak przy wojnie o ziemię.
- Przy 64 ludach (limit gry) dryf i secesje stają; większe państwa mogą wtedy znowu rosnąć. W 2000 turach limit osiągnęła 1 z 8 gier (seed 102).

## 20. Zegar gry na żywo (etap 6 PR 1, ADR 0025)

- **Co się zmieniło:** AI decyduje w pulsach (co 3 s przy ×1), a kampanie i liczniki wojny idą w taktach. Same wzory decyzji są
  bez zmian, więc w pulsie AI zachowuje się jak w dawnym kroku.
- **Za szybkie w czasie rzeczywistym** (PR 1): wszystko, co liczyło się w latach licznika: przegląd dyplomaty co 50 lat (ok. 6 s w
  neolicie), rozejm po wojnie, pamięć krzywd. **PR 2 (decyzja Patryka z 2026-11-09):** te czasy idą teraz w latach akcji (rok = 6 s),
  a za długie wartości są skrócone. Panowanie przywódców zostaje w latach licznika (ok. 5 s): w pierwszych erach postaci z imieniem
  znikną (PR 7).
- **Skutek PR 2, uczciwie:** dyplomacja zwolniła bardzo mocno. W 30 minutach (3 seedy) umów handlowych jest 60–73 zamiast 273–295,
  sojuszy 11–17 zamiast 233–263, wojen 41–52 zamiast 66–152, a sojusznicy prawie nie przystępują do wojen. Powód: planowy przegląd
  stosunków przychodzi raz na 5 minut (rada 7,5 min), a reszta to tylko reakcje na alarmy. Dla gracza to raczej dobrze (świat AI nie
  „miga” umowami), ale świat może wydać się senny. Jeśli tak, najprostsza zmiana to `reviewEveryYears` 50 → 10–20 (1–2 min).
- **Wojny trwają ok. 250 lat licznika** (mediana, seed 7, 9000 taktów), bo cicha wojna kończy się po 300 taktach (30 s), a w neolicie
  licznik idzie ok. 8 lat na sekundę. Czy 30 s bez walki to dobry czas na koniec wojny, do decyzji Patryka w PR 2.
- **Uczciwie:** dawne testy jednostkowe AI liczą na „puls co takt” (`TestContent`); zachowanie z prawdziwym zegarem sprawdzają testy
  zegara, epizodów, determinizmu i gry całe w Headless.

## 21. Dźwignie sternika (etap 6 PR P1, ADR 0027)

- **Co się zmieniło:** do tej pory doradcy pomijali cywilizację gracza w całości, więc gdy gracz przejmował lud, nikt nie przenosił
  obozów, nie badał i nie odpowiadał na propozycje. Teraz AI prowadzi wszystko poza dźwigniami, które gracz wziął.
- **Uczciwie, co jest za proste:**
  - przyjęta komenda gracza nie blokuje AI w tej samej dźwigni, jeśli gracz jej nie wziął: gracz może przenieść obóz, a gubernator w
    następnym pulsie przeniesie go gdzie indziej. Wzięcie dźwigni jest jedynym sposobem, żeby AI nie poprawiało gracza;
  - propozycje umów do gracza, który trzyma dźwignię 4, czekają bez odpowiedzi, aż wygasną (odpowiedź przyjdzie z kartami w PR 7);
  - gracz, który trzyma dźwignię 2 i nie wybiera badania, nie bada nic.
- **Decyzja do oceny przy prototypie:** czy rozkaz gracza na dźwigni AI ma ją „przytrzymać” na kilka pulsów (pierwszy pomysł z planu)
  zamiast wymagać wzięcia dźwigni.

## Decyzje Patryka (2026-11-06)

1. **Każda wartość ma na coś wpływać.** Tak (tak miało być od początku). **Zrobione:** PR B, test `DeadValueTests`.
2. **Lata a długość tury:** sprawdzone; wyniki i poprawki w planie.
3. **Technologie (i nie tylko uczony):** priorytet z położenia, braków i charakteru, koszt jako przeszkoda; decyzja nie zawsze optymalna.
   **Zrobione:** PR B (uczony, skarbnik, strateg).
4. **Czas:** symulacja w latach, każda cywilizacja decyduje we własnym rytmie, decyzje reaktywne i proaktywne. **Zrobione:**
   ADR 0018 (licznik świata, kroki przypięte do kalendarza bez utraty lat, wartości er bez zmian).
5. **Ziemia i migracje:** lepsza ocena ziemi; odłamy, które idą na nowe tereny i z czasem stają się osobnymi cywilizacjami.
   **Zrobione w części:** PR C (ocena ziemi z technologiami, osiedlenie, osadnicy z wiosek, wyprawy, zwiad i marsz na stulecie).
   Odłamy jako nowe cywilizacje: PR D (etap 4f).

Plan wykonania: [`docs/prompts/04-ai-czas-wartosci-migracje.md`](../prompts/04-ai-czas-wartosci-migracje.md).
