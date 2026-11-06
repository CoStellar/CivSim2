# Ocena: co działa, co jest za proste, co jest zbędne

[← spis treści](README.md)

Ta strona to moja (Claude) uczciwa ocena obecnego AI po przeczytaniu całego kodu i trzech grach po 1000 tur. Niczego tu nie
zmieniam: każda poprawka zmienia zasady gry, więc decydujesz Ty. Na końcu są pytania z rekomendacją.

## Podsumowanie w jednej tabeli

| Obszar | Ocena | Dlaczego |
| --- | --- | --- |
| Wspólna ocena i wybór (czynniki, 3 najlepsze, ostrożność) | **dobre** | jeden wzór dla wszystkich, deterministyczny, wyjaśnialny |
| Gubernator: przeprowadzki, odłamy, wypędzenia | **dobre, rozsądnie złożone** | liczy jedzenie, wygodę, odległość, kierunek, opinię, siłę |
| Gubernator: osiedlenie (G4) | **za proste** | w praktyce „zna rolnictwo i ma 60 osób = osiada” |
| Gubernator: łupy (G6), dołączanie (G1) | proste, ale wystarczy | rzadkie albo oczywiste sytuacje |
| Wioski | **brak AI** | po osiedleniu gubernator nic nie robi |
| Strateg | **za skomplikowany w stosunku do skutku** | 6 wag; ekspansja działa w gubernatorze, głód (bezpieczeństwo, dobrobyt) od 4d tylko w dyplomacji |
| Charakter | **częściowo martwy** | od 4d nic nie zmienia tylko ciekawość świata |
| Dyplomata (4d) | **dobre, z jedną dziurą** | ta sama ocena dla obu stron, reaguje na wojnę i napad poza rytmem (ADR 0018); ale „widzi” dzisiejszą siłę z pamiętanych pól |
| Skarbnik | dobry pomysł (ceny z popytu), **drobne braki** | osady sieci nie koordynują się |
| Uczony | **za prosty** | bierze najtańsze, nie patrzy, co technologia daje |
| Wiedza AI | **niezgodne z zasadą** | AI zna dzisiejszy stan pól, które tylko pamięta |
| Rytm decyzji | **dobre (od ADR 0018)** | każda cywilizacja w rytmie swojej ery, reakcje poza rytmem; zwiad i ruch obozu nadal „na krok” (PR C) |

## 1. Wagi i cechy, które nic nie zmieniają

Waga celu i mnożnik charakteru zmieniają decyzję **tylko wtedy, gdy porównywane opcje służą różnym celom** albo gdy ocenę porównujemy
ze stałym progiem. Jeśli wszystkie opcje pomnożymy przez tę samą liczbę, kolejność i szanse w losowaniu zostają te same (pomijając
zaokrąglenia o 1).

| Wielkość | Gdzie mnoży | Czy zmienia decyzję |
| --- | --- | --- |
| waga ekspansji × ekspansywność | G2: „zostać” i wolne miejsca jednakowo | **nie** (między sobą) |
| ta sama | G2: wolne miejsce kontra obcy obóz (który ma wagę podboju × agresję) | **tak** |
| ta sama | G3: wędrówka kontra „zostać” | **nie** |
| ta sama | G5: próg zatłoczenia | **tak** (jedyne silne działanie) |
| ta sama | G5: wybór miejsca odłamu (wszystkie wolne) | **nie** |
| waga podboju × agresja | G2: obcy obóz | **tak** (waga podboju zawsze 1000, działa tylko agresja) |
| waga dobrobytu × handlowość | T1: wszystkie receptury | **nie** |
| ta sama | D2: umowa handlowa kontra pakt, sojusz, wojna (inne cele) i próg 1,0 | **tak** (od 4d) |
| waga bezpieczeństwa × ostrożność | D1, D2: pakt, pokój; sojusz (× lojalność) | **tak** (od 4d) |
| waga nauki × ciekawość | U1: wszystkie technologie | **nie** |
| waga wpływu | nigdzie | **nie** |
| lojalność | D2 sojusz, hamulec wojny, D3 dołączanie sojusznika | **tak** (od 4d) |

**Skutek (stan 4d):** waga głodu (bezpieczeństwo i dobrobyt) działa tylko w dyplomacji: głodna cywilizacja chętniej zawiera pakty i
pokój. W skarbniku i uczonym dalej nic nie zmienia. **Ciekawość świata nic nie zmienia.** Waga wpływu nie jest nigdzie używana. Ciekawa uboczna rzecz: im bardziej zatłoczona cywilizacja (wyższa waga ekspansji), tym **rzadziej**
wypędza obce obozy, bo wolne miejsca rosną w ocenie, a obce obozy nie.

Działające cechy: ostrożność (losowanie, pakt, pokój), ekspansywność (próg odłamu, wolne miejsca kontra podbój), agresja (podbój,
wojna), handlowość (umowa handlowa), otwartość (pierwsze wrażenie), mściwość (pamięć napaści), lojalność (sojusz, łamanie umów).

## 2. AI zna dzisiejszy stan pól, które tylko pamięta

`CLAUDE.md` mówi: AI widzi tylko to, co odkryła jej cywilizacja. Dziś AI **wybiera** tylko pola znane (widziane w ostatnich 300
latach), ale **ocenia** je według dzisiejszego stanu: obecnej dzikiej żywności, obecnej temperatury, obecnego mieszkańca i jego
dzisiejszej liczby wojowników. Liczy też jedzenie pól wokół znanego pola, których mogło nie widzieć. G6 (łupy) w ogóle nie sprawdza,
czy osada jest znana.

Dyplomata (4d) ma tę samą dziurę: siłę rywala liczy z osad stojących na **znanych** polach, ale z ich **dzisiejszą** liczbą wojowników.

Przykład: plemię widziało dolinę 250 lat temu. Dziś obok mieszka obcy obóz. AI „wie”, ilu ma wojowników, i może go zaatakować,
choć nigdy go nie widziało.

**Propozycja:** obce obozy i ich siła tylko z pól widzianych w tym roku (to tanie: pamięć mapy już zapisuje rok). Jedzenie okolicy
może zostać dzisiejsze, bo przy turach po 50 lat różnica jest mała, a zapamiętywanie wartości kosztuje pamięć.

## 3. Uczony wybiera najtańsze, a nie najpotrzebniejsze

Uczony patrzy tylko na koszt (z inspiracjami i sąsiadami). Nie wie, co technologia daje, i nie widzi potrzeb: głodne plemię nie
szuka technologii jedzenia, zmarznięte nie szuka ognia (chyba że ogień jest akurat najtańszy). Rolnictwo przychodzi wtedy, gdy staje
się najtańsze z dostępnych. W 1000 tur cywilizacja wybiera badanie 7–11 razy, więc każdy wybór ma duże znaczenie.

**Propozycja:** wartość technologii z tego, co daje i czego cywilizacji brakuje (np. „daje jedzenie” × waga głodu, „daje odzież” ×
zimno, „otwiera recepturę na towar o wysokiej cenie” × waga dobrobytu). To dałoby też sens wagom stratega i ciekawości (pkt 1).

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
- **Skarbnik:** osady jednej sieci widzą te same ceny i ten sam zapas, więc mogą wszystkie wybrać tę samą recepturę, choć surowca
  wystarczy dla jednej.
- **Wędrówki i łupy prawie się nie zdarzają** (0–8 na 1000 tur). Próg 60% i rzadka siatka sprawiają, że wędrówka jest wyjątkiem;
  łupy są ostatnie w kolejności.
- **15 liczb zaszytych w kodzie** (lista w [parametrach](07-parametry.md#liczby-zaszyte-w-kodzie-nie-w-danych)), np. próg osiedlenia
  150% czy próg łupów 50%. Zasada 5 mówi: zawartość w danych. Warto je przenieść do `data/ai.json`.
- **Dyplomata: rytm przy krótkich krokach** (naprawione w ADR 0018): przegląd liczy miesiące, więc przy kroku 3-miesięcznym wypada
  raz na 50 lat, a nie 4 razy w roku przeglądu. Tak samo pogłoski liczą szansę w milionowych za dokładne miesiące kroku.
- **Dyplomata: dużo odrzuceń.** W seedzie 42: 666 propozycji, 619 odrzuconych, 32 przyjęte. Próg propozycji (1,0) jest wyższy niż
  próg przyjęcia (0,8), ale druga strona często ocenia gorzej (np. czuje mniejsze zagrożenie). Do strojenia w 4g.
- **Czego GDD obiecuje, a jeszcze nie ma:** plany wieloetapowe (8.6), mapy zagrożeń i napięcia (8.2), prognozy zasobów („za 12 tur
  zabraknie żelaza”). To zgodne z planem etapów, ale dziś AI reaguje tylko na bieżący stan, nie planuje.

## Decyzje Patryka (2026-11-06)

1. **Każda wartość ma na coś wpływać.** Tak (tak miało być od początku).
2. **Lata a długość tury:** sprawdzone; wyniki i poprawki w planie.
3. **Technologie (i nie tylko uczony):** priorytet z położenia, braków i charakteru, koszt jako przeszkoda; decyzja nie zawsze optymalna.
4. **Czas:** symulacja w latach, każda cywilizacja decyduje we własnym rytmie, decyzje reaktywne i proaktywne. **Zrobione:**
   ADR 0018 (licznik świata, kroki przypięte do kalendarza bez utraty lat, wartości er bez zmian).
5. **Ziemia i migracje:** lepsza ocena ziemi; odłamy, które idą na nowe tereny i z czasem stają się osobnymi cywilizacjami.

Plan wykonania: [`docs/prompts/04-ai-czas-wartosci-migracje.md`](../prompts/04-ai-czas-wartosci-migracje.md).
