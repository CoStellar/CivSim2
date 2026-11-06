# Plan: czas w latach, każda wartość coś zmienia, priorytety technologii, migracje

Stan na 2026-11-06, po etapie 4d. Odpowiedź na uwagi Patryka do `docs/ai/08-ocena-i-uwagi.md` (punkty 1–5).
Plan do akceptacji: zmienia zasady gry, więc nic z tego nie jest jeszcze w kodzie.

## Punkt 2: czy lata i długość tury do siebie pasują? (sprawdzone w kodzie)

**Krótko:** pamięć relacji i większość temp jest liczona poprawnie w latach. Ale kilka rzeczy dzieje się **raz na turę**, a nie raz na
rok, więc ich siła zależy od długości tury ery. I jest jedna ważna rzecz, o której warto wiedzieć: **dziś cały świat ma jedną długość
tury, ustaloną przez najbardziej rozwiniętą cywilizację** (`GameState.CalendarEra` = najwyższa era wśród żywych). Gdy pierwsza
cywilizacja wejdzie w neolit, wszyscy dostają tury po 25 lat, także plemiona z epoki kamienia.

| Co | Jak liczone | Czy w porządku |
| --- | --- | --- |
| zanikanie wspomnień (napaść 150 lat itd.) | w miesiącach, liniowo | **tak**. W epoce kamienia napaść działa przez 3 tury (100%, 67%, 33%), w erze przemysłowej przez 150 tur. Tak ma być. |
| narastanie sporu, handlu (−1/rok, +0,5/rok) | rok × długość tury | **tak**. W epoce kamienia spór dochodzi do −30 w jednej turze, bo tura to 50 lat. |
| pamięć mapy (300 lat) | w latach | **tak** |
| wzrost ludności, zbieranie, psucie, nauka | na rok × długość tury | **tak** |
| **pogłoski** | szansa „na stulecie” × lata tury, ale tura krótsza niż rok liczy się jak rok | **błąd przy turach < 1 roku**: przy turze 3-miesięcznej 4 razy za dużo pogłosek |
| **przegląd dyplomaty co 50 lat** | sprawdza numer roku | **błąd przy turach < 1 roku**: w roku przeglądu przegląd w każdej turze tego roku |
| **zwiadowca** (15 pól) i **widzenie** (6 pól) | raz na turę | **nie**: w epoce kamienia 1 zwiadowca na 50 lat, w erze informacji 2 na rok. Odkrywanie świata przyspiesza 100 razy tylko przez długość tury |
| **zasięg ruchu obozu** (10 pól) i marsz wędrówki | raz na turę | **nie**: 10 pól na 50 lat w epoce kamienia, 10 pól na 25 lat w neolicie |
| **odstępy AI** (`reviewEveryTurns` 15, `searchEveryTurns` 3, `minTurnsBetweenMoves` 2) | w turach | **nie** (pkt 5 w uwagach) |
| **handel między cywilizacjami** (50% nadwyżki) | raz na turę | **raczej nie**: przy krótszej turze więcej rund wymiany na rok |

**Wniosek:** symulacja nie jest „zepsuta”, ale niektóre zjawiska zmieniają tempo razem z erą zamiast z rokiem. To naturalnie łączy się
z punktem 4: gdy cywilizacje będą miały własne rytmy, trzeba i tak przeliczyć te rzeczy na lata.

## Punkt 4: czas w latach, każda cywilizacja we własnym rytmie

Twoje wymagania: symulacja liczy się w latach; każda cywilizacja podejmuje decyzje co tyle lat, ile wynika z jej ery; decyzje dzielą się
na **reaktywne** (natychmiast, gdy coś się stanie, np. atak) i **proaktywne** (we własnym rytmie).

### Proponowane rozwiązanie: „zegar świata + rytm cywilizacji” (wersja Twojej opcji 1)

- **Krok świata** = najkrótszy rytm wśród żywych cywilizacji (dziś: długość tury najbardziej rozwiniętej). Świat (klimat, ludność,
  gospodarka, ruch) liczy każdy krok, wszystko w latach, tak jak dziś.
- **Rytm cywilizacji** = długość tury **jej własnej** ery (epoka kamienia 50 lat, neolit 25, brąz 20, …). Decyzje proaktywne zapadają
  tylko w jej turze decyzyjnej. Rytmy są rozłożone (nie wszyscy w tym samym kroku).
- **Decyzje reaktywne** zapadają w każdym kroku świata, gdy zajdzie wyzwalacz, niezależnie od rytmu.

| Reaktywne (każdy krok, gdy jest powód) | Proaktywne (we własnym rytmie) |
| --- | --- |
| obóz głoduje albo marznie → szuka miejsca (G2) | strategia (S1) |
| odpowiedź na propozycję umowy (D1) | odłam, osiedlenie, łączenie, łupy, wędrówka (G1, G3–G6) |
| wypowiedziano nam wojnę / napadnięto nas → obrona, wezwanie sojuszników, ucieczka (od 4e: dowódca) | receptury (T1) |
| skończyło się badanie → wybór następnego (U1) | przegląd stosunków: umowy, wojna (D2) |
| klęska (susza, epidemia) → ratunek, np. przeniesienie | ofensywy, zbrojenia (4e) |

Przykład: świat idzie krokami po 20 lat (ktoś jest w epoce brązu). Plemię z epoki kamienia planuje co 50 lat, czyli co 2–3 kroki, ale
gdy ktoś je napadnie, reaguje w tym samym kroku.

**Dlaczego nie Twoja opcja 2 (wszyscy co rok):** neolit trwa dziś ok. 45 tys. lat. Przy kroku co rok to 45 000 kroków po ok. 20 ms,
czyli kwadrans na sam neolit na mapie standard (dziś ok. 40 s). Zysku w zachowaniu prawie nie ma, bo i tak decyzje zapadają co
kilkadziesiąt lat.

**Co jeszcze w tym samym PR (z punktu 2):** odstępy AI w latach (przegląd strategii np. co 100 lat, szukanie miejsca co X lat),
zwiad i widzenie na rok, zasięg ruchu na rok, poprawka pogłosek i rytmu dyplomaty przy turach < 1 roku. Do tego ADR 0018 i nowy hash.

## Punkty 1 i 3: każda wartość coś zmienia; priorytety, a koszt jako przeszkoda

Wspólny pomysł: **każda opcja służy jakiemuś celowi**, a jej ocena to `priorytet × czynnik kosztu`. Priorytet bierze się z sytuacji
(położenie, braki, zagrożenia) razem z wagą celu stratega i charakterem; koszt to przeszkoda do pokonania. Losowanie z 3 najlepszych
zostaje, więc wybór nie zawsze jest najoptymalniejszy.

### Technologie (uczony)

```
priorytet = Σ po tym, co technologia daje:  wartość tego dla NAS × waga celu × cecha
czynnik kosztu = (koszt najtańszej / koszt tej)     (dziś do kwadratu; ciekawa świata cywilizacja mniej boi się kosztu)
ocena = priorytet × czynnik kosztu
```

| Co daje technologia | Wartość dla nas (z położenia i braków) | Cel i cecha |
| --- | --- | --- |
| zbieranie z biomu / złoża (rybołówstwo, łowiectwo, hodowla) | ile takich pól mamy w strefie i znanej okolicy × cena towaru w naszej sieci | dobrobyt; głód podbija |
| receptura (odzież, ceramika, chleb) | ile brakuje towaru, który daje, i jego cena | dobrobyt, handlowość |
| tolerancja zimna (ogień, garbarstwo) | jak zimno jest w naszych obozach | bezpieczeństwo |
| rolnictwo i uprawy | ile żyznej ziemi mamy, jak bardzo głodujemy | dobrobyt, bezpieczeństwo |
| technologie wojskowe (pole `military`, od 4e) | zagrożenie od sąsiadów, plany wojny | podbój × agresja, bezpieczeństwo |
| nauka, społeczeństwo (pole `science`, `society`) | stały udział | nauka × ciekawość, wpływ × otwartość |

Przykład z Twojej wiadomości: cywilizacja nad rzeką ma wiele pól z rybami w strefie, więc rybołówstwo ma wysoki priorytet; cywilizacja
na pustyni nie ma ani jednego, więc priorytet 0, a wyżej wychodzą np. tolerancja upału albo hodowla. Agresywna cywilizacja bardziej
ceni technologie wojskowe, a skarbnik (niżej) wcześniej robi broń.

### Towary i receptury (skarbnik)

Ten sam model: receptura służy celowi swojego wyrobu. Jedzenie → bezpieczeństwo (głód), potrzeby wyższe → dobrobyt, nadwyżka dla
partnera handlowego → dobrobyt × handlowość, broń i zapasy na wojnę (od 4e) → podbój × agresja. „Łańcuchy dostaw”: skarbnik ceni też
recepturę, która daje **wejście** do receptury o wysokim priorytecie (np. narzędzia do broni).

### Każda wartość coś zmienia

| Wartość | Dziś | Proponowana rola |
| --- | --- | --- |
| waga nauki | zawsze 1,0 | strateg podnosi ją, gdy znani sąsiedzi wiedzą więcej (zostajemy w tyle) |
| waga wpływu | nieużywana | technologie społeczne; od 4f ustroje i administracja |
| waga dobrobytu | tylko handel | + uczony i skarbnik (wyżej) |
| waga bezpieczeństwa | tylko pakty | + uczony, skarbnik (zapasy, broń), wioski (rezerwa jedzenia) |
| waga ekspansji przy „zostać” | mnoży „zostać” i „przenieś” jednakowo | **„zostać” bez wagi ekspansji**: ekspansywna cywilizacja chętniej się przenosi (też punkt 5) |
| ciekawość świata | nic | mniejszy lęk przed kosztem technologii; zwiad i wędrówki w nieznane |
| handlowość | tylko umowa handlowa | + produkcja na wymianę |
| liczby zaszyte w kodzie (15) | w kodzie | do `data/ai.json` |

**Test „martwych wartości”:** automatyczny test zmienia po kolei każdą cechę, każdą wagę i każdy parametr AI w ustalonych sytuacjach
i sprawdza, że zmienia się jakaś decyzja. Jeśli coś nic nie zmienia, test nie przechodzi. To pilnuje Twojej zasady na stałe.

## Punkt 5: lepsza ocena ziemi, migracje i odłamy, które stają się nowymi cywilizacjami

Dlaczego dziś cywilizacje stoją w miejscu: (1) wioska nie ma żadnych decyzji gubernatora; (2) obóz osiada prawie od razu, gdy zna
rolnictwo; (3) ocena ziemi nie zna technologii (ryby, zwierzyna) ani rolnictwa; (4) obóz rusza się tylko z głodu albo zimna.

| Zmiana | Co da |
| --- | --- |
| ocena ziemi z technologiami cywilizacji i z polami wody (ryby) | wybrzeża i rzeki są cenione jak naprawdę karmią |
| ocena ziemi dla rolników: plon z najlepszych pól dla prawdziwej liczby rolników | wioski widzą, gdzie jest lepsza ziemia |
| osiedlenie jako wybór: plon z rolnictwa kontra zbieractwo, ekspansywność opóźnia | część plemion dłużej wędruje |
| **wioska: wysłanie osadników** (proaktywnie): pełna albo na wyjałowionej ziemi → grupa idzie do najlepszej znanej ziemi w zasięgu wędrówki i zakłada obóz albo wioskę | rozprzestrzenianie się po kontynencie |
| **wioska: przeprowadzka** (rzadko, duży próg): ziemia dużo gorsza niż znana w okolicy | ucieczka z wyjałowionych terenów |
| wyprawy w nieznane bez głodu: ciekawość i ekspansywność wysyłają grupę ku nieznanym polom w kierunku ekspansji | zasiedlanie pustych kontynentów |
| **odłam staje się nową cywilizacją** (GDD 5.4: „kolonia albo osobna cywilizacja”; GDD 7.3: secesja): osada daleko poza siecią handlową i bez kontaktu z resztą przez np. 200 lat ma szansę się usamodzielnić; dziedziczy technologie i pamięć mapy, charakter rodzica z odchyleniami, dobrą opinię o rodzicu na start | historie „odłam poszedł za góry i stał się nowym ludem” |

Odłam-nowa cywilizacja należy do etapu 4f (secesja), ale prostą wersję „oddalenie i brak kontaktu” można zrobić wcześniej.
Pomiar: wykres „jak daleko od miejsca startu żyją ludzie” w czasie i liczba nowych cywilizacji w 3 seedach.

## Proponowana kolejność

| PR | Zakres | Kiedy |
| --- | --- | --- |
| **A. Zegar** | punkt 4 + poprawki z punktu 2 + AI widzi siłę obcych tylko z pól widzianych w tym roku; ADR 0018 | **przed 4e**, bo wojna (reakcja na atak) potrzebuje decyzji reaktywnych |
| **B. Wartości** | punkty 1 i 3: priorytety technologii i towarów, rola każdej wagi i cechy, test martwych wartości, liczby do danych | po A, przed albo razem z 4e |
| **C. Ziemia i migracje** | punkt 5 bez secesji | po B |
| **D. Nowe cywilizacje z odłamów** | secesja | w 4f |

Każdy PR aktualizuje `docs/ai/` i pokazuje przed/po na wykresach z 3 seedów.
