# Ocena: co działa, co jest za proste, co jest zbędne

[← spis treści](README.md)

Ta strona to moja (Claude) uczciwa ocena obecnego AI po przeczytaniu całego kodu i trzech grach po 1000 tur. Każda poprawka
zmienia zasady gry, więc decydujesz Ty. **Stan: po PR B (wartości i priorytety)**; punkty, które PR B zamknął, są oznaczone.

## Podsumowanie w jednej tabeli

| Obszar | Ocena | Dlaczego |
| --- | --- | --- |
| Wspólna ocena i wybór (czynniki, 3 najlepsze, ostrożność) | **dobre** | jeden wzór dla wszystkich, deterministyczny, wyjaśnialny |
| Gubernator: przeprowadzki, odłamy, wypędzenia | **dobre, rozsądnie złożone** | liczy jedzenie, wygodę, odległość, kierunek, opinię, siłę |
| Gubernator: osiedlenie (G4) | **za proste** | w praktyce „zna rolnictwo i ma 60 osób = osiada” |
| Gubernator: łupy (G6), dołączanie (G1) | proste, ale wystarczy | rzadkie albo oczywiste sytuacje |
| Wioski | **brak AI** | po osiedleniu gubernator nic nie robi |
| Strateg | **dobre (od PR B)** | każda z 6 wag na coś wpływa; nauka rośnie, gdy spotkani wiedzą więcej, wpływ z liczbą spotkanych; podbój czeka na 4e |
| Charakter | **żywy (od PR B)** | każda cecha zmienia decyzję (test `DeadValueTests`); ciekawość zmniejsza strach przed kosztem technologii |
| Dyplomata (4d) | **dobre, z jedną dziurą** | ta sama ocena dla obu stron, reaguje na wojnę i napad poza rytmem (ADR 0018); ale „widzi” dzisiejszą siłę z pamiętanych pól |
| Skarbnik | **dobre (od PR B), jedna słabość** | osady sieci koordynują się, jedzenie kontra wyroby, premia za handel; zostało przełączanie odzież ↔ chleb |
| Uczony | **dobre (od PR B)** | priorytet z położenia, braków i charakteru, koszt jako przeszkoda; rolnictwo przychodzi wcześniej (do oceny) |
| Wiedza AI | **częściowo naprawione (PR B)** | obce obozy i ich siła tylko z pól widzianych w ostatnim kroku; jedzenie pól nadal dzisiejsze |
| Rytm decyzji | **dobre (od ADR 0018)** | każda cywilizacja w rytmie swojej ery, reakcje poza rytmem; zwiad i ruch obozu nadal „na krok” (PR C) |

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

## 4. Osiedlenie to w praktyce próg, a nie wybór

Rachunek G4 zakłada, że każde pole strefy uprawia 40 rolników, choć obóz ma ich kilkudziesięciu. Dlatego plon wychodzi zwykle
kilkanaście razy większy od potrzeb: w 539 osiedleniach z trzech gier mediana `farm yield` to ok. 1650%, najmniej 169%, najwięcej
5594%. Próg 150% jest więc spełniony praktycznie zawsze. Nie ma też porównania z dalszym
koczowaniem, wag ani charakteru.

**Propozycja:** liczyć plon dla prawdziwej liczby producentów (najlepsze pola najpierw, jak robi to system rolnictwa) i porównać z
tym, co daje zbieractwo; ekspansywna cywilizacja mogłaby dłużej koczować.

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
odkrywają świat i wędrują szybciej w przeliczeniu na rok. To zmieniamy w PR C (ziemia i migracje), bo zmienia równowagę wędrówek.

## 6. Gubernator nie zna swoich technologii

Wartość miejsca liczy jedzenie pola **bez technologii cywilizacji**, a pól wody w ogóle nie liczy. Plemię z rybołówstwem, które
naprawdę je ryby z wybrzeża, wycenia wybrzeże jak plemię bez rybołówstwa. Tak samo premie z łowiectwa i hodowli. AI wycenia więc
ziemię gorzej, niż ona naprawdę karmi, i rzadziej przenosi się nad morze.

**Propozycja:** liczyć jedzenie z technologiami cywilizacji (system zbierania już to potrafi) i dodawać pola wody, jeśli cywilizacja
zna rybołówstwo.

## 7. Dziennik „Dlaczego?” czasem wprowadza w błąd

Dziennik pokazuje 3 czynniki najdalsze od 1000. W seedzie 7 pierwszym czynnikiem jest `character` w 3600 z 7977 wpisów (45%), choć (pkt 1) nie wpłynął na wybór, bo wszystkie
opcje miały ten sam. Przykład z gry: `Scholar, SetResearch: character 105%, research cost 100%`. Prawdziwy powód to „najtańsze”.

**Propozycja:** w dzienniku pomijać czynniki wspólne dla wszystkich opcji danej decyzji.

## 8. Wioski bez AI

Po osiedleniu gubernator nie robi z wioską nic: nie wyśle odłamu, gdy jest pełna, nie przeniesie ludzi, gdy brakuje jedzenia. Na
[wykresie](09-statystyki.md) widać, że po turze ~300, gdy większość osad jest już wioskami, liczba decyzji spada kilkukrotnie.
To zgodne z planem (budowy i dzielnice to późniejsze etapy), ale warto wiedzieć, że od neolitu AI „śpi”.

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

## Decyzje Patryka (2026-11-06)

1. **Każda wartość ma na coś wpływać.** Tak (tak miało być od początku). **Zrobione:** PR B, test `DeadValueTests`.
2. **Lata a długość tury:** sprawdzone; wyniki i poprawki w planie.
3. **Technologie (i nie tylko uczony):** priorytet z położenia, braków i charakteru, koszt jako przeszkoda; decyzja nie zawsze optymalna.
   **Zrobione:** PR B (uczony, skarbnik, strateg).
4. **Czas:** symulacja w latach, każda cywilizacja decyduje we własnym rytmie, decyzje reaktywne i proaktywne. **Zrobione:**
   ADR 0018 (licznik świata, kroki przypięte do kalendarza bez utraty lat, wartości er bez zmian).
5. **Ziemia i migracje:** lepsza ocena ziemi; odłamy, które idą na nowe tereny i z czasem stają się osobnymi cywilizacjami.

Plan wykonania: [`docs/prompts/04-ai-czas-wartosci-migracje.md`](../prompts/04-ai-czas-wartosci-migracje.md).
