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
| Strateg | **za skomplikowany w stosunku do skutku** | 6 wag, z czego realnie działa jedna (ekspansja) |
| Charakter | **częściowo martwy** | 3 z 8 cech nic nie zmieniają |
| Skarbnik | dobry pomysł (ceny z popytu), **drobne braki** | osady sieci nie koordynują się |
| Uczony | **za prosty** | bierze najtańsze, nie patrzy, co technologia daje |
| Wiedza AI | **niezgodne z zasadą** | AI zna dzisiejszy stan pól, które tylko pamięta |
| Odstępy czasu | **niezgodne z zasadą „w latach”** | 3 parametry są w turach |

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
| waga nauki × ciekawość | U1: wszystkie technologie | **nie** |
| waga bezpieczeństwa | nigdzie | **nie** |
| waga wpływu | nigdzie | **nie** |
| lojalność | nigdzie | **nie** (czeka na 4d) |

**Skutek:** strateg liczy wagę głodu (bezpieczeństwo i dobrobyt), ale ona **nic nie zmienia**. Handlowość, ciekawość świata i
lojalność **nic nie zmieniają**. Ciekawa uboczna rzecz: im bardziej zatłoczona cywilizacja (wyższa waga ekspansji), tym **rzadziej**
wypędza obce obozy, bo wolne miejsca rosną w ocenie, a obce obozy nie.

Działające cechy: ostrożność (losowanie), ekspansywność (próg odłamu, wolne miejsca kontra podbój), agresja (podbój), otwartość
(pierwsze wrażenie), mściwość (pamięć napaści).

## 2. AI zna dzisiejszy stan pól, które tylko pamięta

`CLAUDE.md` mówi: AI widzi tylko to, co odkryła jej cywilizacja. Dziś AI **wybiera** tylko pola znane (widziane w ostatnich 300
latach), ale **ocenia** je według dzisiejszego stanu: obecnej dzikiej żywności, obecnej temperatury, obecnego mieszkańca i jego
dzisiejszej liczby wojowników. Liczy też jedzenie pól wokół znanego pola, których mogło nie widzieć. G6 (łupy) w ogóle nie sprawdza,
czy osada jest znana.

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

## 5. Odstępy czasu w turach, a nie w latach

Zasada 6 i Twoja decyzja z 2026-11-06: backend ma myśleć w latach. Trzy parametry AI są w turach:

| Parametr | Dziś | W epoce kamienia | W erze przemysłowej |
| --- | --- | --- | --- |
| `reviewEveryTurns` 15 | 15 tur | 750 lat | 15 lat |
| `searchEveryTurns` 3 | 3 tury | 150 lat | 3 lata |
| `minTurnsBetweenMoves` 2 | 2 tury | 100 lat | 2 lata |

**Propozycja:** zamienić na lata (np. przegląd strategii co 100 lat, ale nie rzadziej niż co turę), jak zrobiliśmy z pamięcią relacji w 4c.

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
- **Czego GDD obiecuje, a jeszcze nie ma:** plany wieloetapowe (8.6), mapy zagrożeń i napięcia (8.2), prognozy zasobów („za 12 tur
  zabraknie żelaza”). To zgodne z planem etapów, ale dziś AI reaguje tylko na bieżący stan, nie planuje.

## Pytania do Ciebie (z moją rekomendacją)

1. **Wagi bez skutku (pkt 1).** Rekomendacja: przy okazji 4d dać im skutek (głód → uczony i skarbnik wolą jedzenie; handlowość →
   dyplomata woli umowy handlowe; ciekawość → szybsze badania albo droższe technologie mniej odstraszają). Alternatywa: na razie nic.
2. **Wiedza AI (pkt 2).** Rekomendacja: obce obozy tylko z pól widzianych w tym roku. Małe, szybkie, zgodne z zasadą.
3. **Uczony (pkt 3).** Rekomendacja: wartość technologii z jej skutków i potrzeb, w osobnym małym PR po 4d.
4. **Odstępy w latach (pkt 5).** Rekomendacja: tak, razem z punktem 2.
5. **Technologie w ocenie ziemi (pkt 6)** i **osiedlenie (pkt 4).** Rekomendacja: tak, oba w jednym PR „lepsza ocena ziemi”.
