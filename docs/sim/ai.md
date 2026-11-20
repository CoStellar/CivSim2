# AI nowej gry (ADR 0028): wszystkie decyzje

AI nowej gry jest w `src/CivSim.Sim/Ai/SimAi.cs` i `SimAi.Care.cs`. Ta strona opisuje każdą jego decyzję.

**Stan:** N1–N25 według commita `8b03118` (2026-11-19; ścieżki `plik:linia` dla tego commita), N26–N33 z PR-ów #108–#112 (2026-11-20; ścieżki z nazwą metody).

## Zasady ogólne

- **Rytm:** AI myśli co 30 taktów, czyli co 3 sekundy gry (`SimAi.ThinkEveryTicks`). Każdy lud ma przesunięty takt (`Ticks + civ.Id × 7`), więc nie myślą wszyscy naraz.
- **Te same komendy co gracz.** AI niczego nie zmienia samo: wydaje komendy, a gra sprawdza je tak samo jak komendy gracza (`SimGame.Apply`). Odrzucona komenda przepada.
- **Wie tylko to, co gracz:**
  - widzi wyłącznie heksy, które jego lud odkrył (`civ.Known`);
  - złoża i glebę zna na swojej ziemi, a poza nią tylko to, co znaleźli jego poszukiwacze i mierniczy (`KnowsDeposit`, `KnowsSoil`);
  - start wybiera z tego, co widać podczas wyboru (rozdział niżej).
- **Charakter:** każdy lud losuje wojowniczość 0–100 (`Aggression`), ekspansję 0–100 (`Expansion`) i błąd wyboru startu 0–30% (`StartError`). Charakter widać tylko po działaniach.
- **Kolejność w jednym myśleniu** (`Ai/SimAi.cs:25`):
  1. odpowiedzi na pokój (N20);
  2. dyplomacja (N19);
  3. zaopatrzenie (N18);
  4. badanie (N8) i zatrudnianie (N9);
  5. chude czasy przed zimą (N2);
  6. budowa (N3–N6), a gdy nic nie zbudował: awans osady (N10) i przebudowa (N11);
  7. prace na heksach (N7) i zwiadowcy (N12);
  8. osadnicy (N14);
  9. wojsko (N16), gdy w tym myśleniu nic nie zbudował;
  10. wojna (N17);
  11. poszukiwacze i mierniczy (N13).

  W jednym myśleniu AI buduje najwyżej jedną rzecz.

## Katalog decyzji

| Id | Decyzja | Kiedy | Co robi | Liczby | Kod |
| --- | --- | --- | --- | --- | --- |
| N1 | Wybór startu | w fazie startu, w swojej kolejce | losuje miejsce spośród wolnych heksów z oceną „widać przy wyborze” (`Sites.Seen`) nie gorszą niż (100 − błąd)% najlepszej | błąd 0–30% | `State/SimGame.Start.cs:301` |
| N2 | Chude czasy | zima za mniej niż minutę albo trwa, a zapas jedzenia < 3 minut jedzenia ludu (najwyżej 80% magazynu) | buduje łowców, rybaków, pastwisko, farmę albo zbieraczy; nie trenuje wtedy osadników ani wojska | 3 minuty, 80% | `Ai/SimAi.Care.cs:21`, `:35` |
| N3 | Głodny obóz | osada głoduje albo ma ujemne jedzenie i mniej niż 2 racje w zapasie, a nie buduje już jedzenia | buduje jedzenie na własnej ziemi tej osady, najbliżej centrum | 2 racje | `Ai/SimAi.Care.cs:54` |
| N4 | Gospodarka | mniej budów naraz niż 1 + liczba osad | po kolei: jedzenie, gdy netto < 3 na minutę albo głód; domy, gdy ludzi ≥ domy − 4; magazyn, gdy coś jest pełne; budynek uczący brakującego doświadczenia (z pierwszeństwem); praca dla bezrobotnych (≥ 3) według brakującego dobra do badania albo najmniejszego zapasu | netto 3/min, domy −4, 3 bezrobotnych | `Ai/SimAi.cs:167` |
| N5 | Wybór heksa pod budynek | przy każdej budowie | heks w osadzie, która ma materiały: bliżej centrum, więcej bezrobotnych, +20 za znane złoże z premią, +15 za premiową rzeźbę | +20, +15 | `Ai/SimAi.cs:100` |
| N6 | Opieka | jak N4 | narzędziownia, gdy brak narzędzi i jest kamieniołom; krawiec na 3 minuty przed zimą, gdy ubrań < 1 na osobę; budynki wsparcia (zielarz przy łowcach, kamieniarz przy kamieniołomie, młyn przy 2 farmach, gildia przy 3 warsztatach, łaźnia od 50 ludzi, szkoła od 40, akademia od 70, uniwersytet od 90); włącza zbieranie | progi w kolumnie „Co robi” | `Ai/SimAi.Care.cs:93` |
| N7 | Prace na heksach | jedna naraz | w czasie wojny pierścień muru (promień 1) i palisady (promień 2); rów od wody do suchej farmy; akwedukt do miasteczka | promienie 1 i 2 | `Ai/SimAi.Care.cs:197` |
| N8 | Badanie | nic nie badamy | technologia, na którą nas stać: wcześniejsze ery przed późniejszymi (−10 za erę), podstawy +30, technologie broni ± (wojowniczość − 50) / 5 | −10, +30 | `Ai/SimAi.cs:433` |
| N9 | Myśliciel | jest oferta rzadszego myśliciela, a koszt zostawia połowę zapasów | zatrudnia | 50% | `Ai/SimAi.cs:472` |
| N10 | Awans osady | ludzi ≥ domy − 2, cena zostawia 20% zapasów | awansuje osadę | −2, 20% | `Ai/SimAi.cs:480` |
| N11 | Przebudowa (nowa 2026-11-19) | nic innego nie zbudował; lepszy rodzaj znany, miejsce i teren pasują | przebudowuje budynek w lepszy, najpierw domy w pełnych osadach, gdy reszta ceny zostawia 20% zapasów | domy −4, 20% | `Ai/SimAi.cs:497` |
| N12 | Zwiadowcy | jeden, przez pierwsze 20 minut, gdy stać przy zostawieniu połowy | trenuje; stojący idzie na losowy nieznany heks w promieniu ok. 14 × 10 | 20 min, 50% | `Ai/SimAi.cs:547` |
| N13 | Poszukiwacze i mierniczy (nowa 2026-11-19) | po 15. minucie, ≥ 3 osady, bez głodu, bez osadników w drodze i bez innego szkolenia | jeden naraz: mierniczy w epoce kamienia, poszukiwacz od neolitu; po zbadaniu miejsca idzie na niezbadaną wolną ziemię do ok. 12 heksów od losowej osady (cel ze skrótu, nie z losowania AI) | 15 min, 3 osady | `Ai/SimAi.cs:579` |
| N14 | Osadnicy | żadnych w drodze, osad < 2 + ekspansja/25 + era, jedzenie netto > 0, osada ≥ 20 ludzi, cena zostawia 30% | trenuje; osadnicy zakładają obóz, gdzie stoją, jeśli wolno, inaczej idą do najlepszego miejsca (N15) | 20 ludzi, 30% | `Ai/SimAi.cs:643` |
| N15 | Miejsce nowej osady | osadnicy szukają | znany wolny heks 6–12 od osadników: jedzenie biomu × 10, +15 przy wodzie, − odległość, + znana gleba w promieniu 2 (żyzność / 40 na heks) + 6 za każde znane złoże skalne, + 4 za złoże z jedzeniem (zmiana 2026-11-19) | ×10, +15, /40, +6, +4 | `Ai/SimAi.cs:684`, `:714` |
| N16 | Wojsko | żołnierzy < ludzie × (5 + wojowniczość/5)%, jedzenie netto ≥ 2, cena zostawia 20% | najsilniejszy znany rodzaj; trenuje go osada, która ma ≥ ludzi oddziału + 15 i najwięcej jedzenia na zbyciu, co najmniej tyle, ile oddział je (racje) | 5–25% ludzi | `Ai/SimAi.cs:751` |
| N17 | Wojna | co myślenie | broni swojej ziemi przed widocznym wrogiem; w wojnie atakuje najbliższą znaną osadę i proponuje pokój, gdy przegrywa (wynik wroga > nasz + 20) albo losowo (3%); nową wojnę zaczyna po 8. minucie, przy wojowniczości ≥ 40, z siłą > 2 × obrona celu i szansą wojowniczość/10 %; sojuszników nie rusza, partnerów handlu tylko przy wojowniczości ≥ 85; przy wojowniczości ≥ 75 atakuje bez wypowiedzenia | 8 min, 40, × 2, +20, 3% | `Ai/SimAi.cs:791` |
| N18 | Zaopatrzenie | co myślenie | ponawia materiały na budowy, których tragarze zginęli; głodnej osadzie wysyła karawanę 40 jedzenia z osady, która ma najwięcej (> 60) | 40, 60 | `Ai/SimAi.cs:947` |
| N19 | Umowy | oferta do nas albo warunki umowy spełnione | przyjmuje handel, gdy proponujący ma dobrą reputację, jest korzyść (dobro albo rzemiosło, którego nam brak), a wojowniczość < 85; sojusz przy wspólnym wrogu i wojowniczości < 70; sam proponuje handel (wojowniczość < 70) i sojusz (< 50) | 85, 70, 50 | `Ai/SimAi.cs:909`, `:1000` |
| N20 | Odpowiedź na pokój | oferta do nas | przyjmuje daninę nie gorszą niż sugerowana z przebiegu wojny plus (100 − wojowniczość)/10 punktów | /10 | `Ai/SimAi.cs:1041` |
| N21 | Oszczędzanie na technologię | żadnej technologii nie stać | zapamiętuje cenę tej, którą chce najbardziej (najlepszy wynik minus cena/20); budynki (oprócz jedzenia, nauczycieli i wytwórców braków), osadnicy wyjęci, wojsko i ulepszenia zostawiają te dobra | cena/20 | `Ai/SimAi.cs` (`Research`, `Affordable`) |
| N22 | Wytwórca braku | oszczędza, a dobra brak i nikt go nie robi | buduje wytwórcę (np. narzędziarz na narzędzia) nawet bez wolnych rąk, z pierwszeństwem | — | `Ai/SimAi.cs` (`Economy`, `Makers`) |
| N23 | Nauczyciel bez surowca | nauczyciel (budynek dający doświadczenie) nie ma wejść | najpierw buduje wytwórcę surowca (kamieniołom dla narzędziarza); nauczyciel, który już stoi, dostaje pierwszeństwo | 20 sztuk zapasu | `Ai/SimAi.cs` (`Teacher`, `Economy`) |
| N24 | Bez bliźniaków i pustych miejsc pracy | budynek tego rodzaju jest w budowie albo wolnych miejsc pracy jest tyle, co bezczynnych ludzi | nie zaczyna nowego miejsca pracy (wyjątki: nauczyciel, wytwórca braku, jedzenie przy głodzie); budynek z pracownikami stawia w osadzie z wolnymi rękami. Po 40 min (12 ziaren): puste budynki 23% → 15%, wolne miejsca pracy 185 → 85 | — | `Ai/SimAi.cs` (`TryBuild`) |
| N25 | Droga do bramy ery | brama obecnej ery nieznana | technologie na drodze do bramy (ona sama, jej wymagania i technologie, w których wymaga doświadczenia, dwa kroki wstecz) dostają +25 w wyborze badania, a ich budynki-nauczyciele idą pierwsze | +25 | `Ai/SimAi.cs` (`GatewayWay`) |
| N26 | Nauczyciel przed kolejką | badanie czeka na nauczyciela, budów mniej niż 2 + liczba osad | stawia nauczyciela przed innymi budowami; może go postawić w osadzie bez wolnych rąk, tam gdzie są towary | 2 + osady | `Ai/SimAi.cs` (`Economy`) |
| N27 | Dowóz na budowę (#109) | budynek, na który czeka badanie, da się postawić tylko w osadzie bez jego ceny | karawany z najbogatszych osad przywożą brakującą cenę; następna decyzja stawia budynek | — | `Ai/SimAi.cs` (`Fund`) |
| N28 | Najpierw producent ceny (#109) | budynek do nauki kosztuje towar, którego nikt nie robi | stawia producenta tego towaru (np. narzędziarza przed farmą) | — | `Ai/SimAi.cs` (`PriceMaker`) |
| N29 | Surowce dla warsztatów (#109) | pracujący budynek ma w osadzie < 10 sztuk surowca | karawana z osady, która ma go najwięcej (zostawia 20, wiezie do 30); najwyżej 2 nowe na decyzję, jedna na towar i cel | 10, 20, 30, 2 | `Ai/SimAi.cs` (`Inputs`) |
| N30 | Złoża dla osadników (#109) | brama ery czeka na towar z kopalni, a żadna osada nie ma złoża | miejsce obozu przy znanym takim złożu (promień 2) dostaje +60; budynek przy surowcach dostaje do +20 za surowiec | +60, +20 | `Ai/SimAi.cs` (`WantedDeposits`, `InputsAtHand`) |
| N31 | Po zdobyciu osady (#110) | osada padła przed naszym wojskiem | zajmuje, gdy leży do 20 heksów od naszej najbliższej osady, dalej łupi; burzy tylko ostatnią osadę wroga przy wojowniczości ≥ 75. Obrona celu przy decyzji o wojnie liczy też straż | 20 heksów, 75 | `Ai/SimAi.cs` (`TakeChoiceFor`) |
| N32 | Jedzenie dla armii w polu (#111) | oddział poza zasięgiem zaopatrzenia ma jedzenia na < 1,5 minuty i nikt mu go nie wiezie | karawana z domu, gdy dom ma > 2 × brak + 40; głodny bez dostawy wraca do domu | 1,5 min, ×2 + 40 | `Ai/SimAi.cs` (`FieldSupply`) |
| N33 | Premie rodzajów (#112) | rodzaj jednostki osiągnął poziom | łucznicy i machiny: Hardened; jazda: na zmianę Forced march i Hardened; piechota: na zmianę Hardened i Shield wall | — | `Ai/SimAi.cs` (`VeteranChoiceFor`) |

## Zmiany z 2026-11-19

1. **Racje:** jedzenie netto (N4, N14, N16) i chude czasy (N3) liczą racje mieszkańców i jednostek. Tempo jedzenia w osadzie pokazuje je na minutę. Wojsko trenuje osada, która je wyżywi (N16).
2. **Bez ukrytej wiedzy** (znany problem 23):
   - start wybierany z oceny bez złóż i gleby, jak widzi je gracz w fazie wyboru (N1);
   - miejsce nowej osady liczone tylko z tego, co lud zna (N15);
   - premia za złoże pod budynkiem tylko dla znanych złóż (N5).
3. **Poszukiwacze i mierniczy** (N13): późno i po jednym, bo każdy ich koszt w pierwszych minutach mocno spowalnia lud.
4. **Przebudowa budynków** (N11): w 40-minutowych grach jeszcze się nie zdarza, bo AI rzadko dochodzi do technologii lepszych rodzajów.

## Pomiar: 12 ziaren

`dotnet run --project src/CivSim.Headless -c Release -- --sim-report --seeds 12` (4 ludy, 40 minut, mapa 66 × 40, ok. 25 s). Pierwsze ziarno ma wartość 1, każde następne o 1 więcej.

![AI nowej gry, 12 ziaren](img/nowa_gra_12_ziaren.png)

| Miara | Średnia | Mediana | Rozrzut |
| --- | --- | --- | --- |
| Ludzie (4 ludy razem) | 203 | 217 | ± 80 (39%) |
| Osady | 10,0 | 11 | ± 2,5 |
| Jednostki | 12,2 | 12 | ± 2,9 |
| Żołnierze (ludzie) | 33 | 36 | ± 15 |
| Wymarłe ludy | 0,4 | 0 | ± 0,6 |
| Era (średnia ludów) | 0,9 | 1 | ± 0,1 |
| Próbki z głodem | 44% | 44% | ± 6 |
| Wypowiedziane wojny | 6,9 | 3 | ± 8 |
| Budynki | 294 | 294 | ± 61 |
| Znane technologie | 13,8 | 14 | ± 0,8 |
| Pierwszy neolit | 14. minuta | | we wszystkich 12 grach |

**Ważne:** wynik jednej gry zmienia się o 20–40% od drobnych zmian (np. zabrania stolicy 4 ludzi w 10. minucie). Dlatego zmian AI nie oceniamy na 3 ziarnach. Błąd średniej z 12 ziaren dla ludzi to ok. ± 23.

| Wersja | Ludzie (średnia z 12) |
| --- | --- |
| Przed poszukiwaczami (`410054c`) | 233 |
| Poszukiwacze, AI trenuje je od 10. minuty i 2 osad (`8ff536a`) | 198 |
| AI bez poszukiwaczy | 212 |
| Obecnie (`8b03118`: po 15. minucie, 3 osady, jeden naraz) | 203 |

## Tempo er (2026-11-19, cel: ok. 60 minut do średniowiecza)

Pomiar sondą „na co czeka badanie” (6 ziaren, próbka co 10 s): AI w neolicie badała przez 8% czasu. Przez resztę czekała:
- na dobra do ceny (najczęściej drewno na ogień i narzędzia), bo wydawała je od razu na budynki;
- na doświadczenie, bo nauczyciela (np. narzędziarza) nie budowała przez kilkanaście minut albo stał bez surowca lub bez ludzi.

Do tego AI stawiała dziesiątki pustych tartaków: budynek szedł tam, gdzie leżało drewno, a nie tam, gdzie byli wolni ludzie.
Naprawy: N21–N24 oraz tempo badań w danych (`research.pace`: procent ceny, doświadczenia i czasu dla każdej ery).

| 12 ziaren, 4 ludy | Przed | Po |
| --- | --- | --- |
| Pierwszy neolit | 14,2 min | 6,8 min |
| Pierwsza epoka brązu | 54,8 min (w 5 z 12 gier w 80 min) | 17,4 min (we wszystkich) |
| Pierwsza epoka żelaza | brak w 80 min | 39,1 min (we wszystkich) |
| Pierwsze średniowiecze | brak | brak w 80 min |
| Ludzie po 40 min | 224 | 646 |
| Osady po 40 min | 10,8 | 19,0 |
| Próbki z głodem (40 min) | 44% | 9% |

`research.pace` po strojeniu: cena 40/35/25/25/25%, doświadczenie i wyroby 40/35/22/22/22%, czas 60/50/35/35/35% (epoka kamienia, neolit, brąz, żelazo, średniowiecze).
Dalej obniżanie liczb nie przyspiesza AI: w epoce żelaza blokuje ją struktura drzewa, nie ceny (punkt 4 poniżej).

## Po bramach ery i decyzjach z testów (2026-11-19, 12 ziaren, 70 min)

| Miara | Bez bram | Z bramami (M1), miedzią przy starcie (M4) i N25 |
| --- | --- | --- |
| Pierwsza epoka brązu | 17,4 min | 38,8 min |
| Pierwsza epoka żelaza | 39,1 min | 59,2 min (w 4 z 12 gier) |
| Głód (próbki) | 13% | 6% |
| Puste budynki z miejscami pracy | 15% | 4% |

Brama spowalnia AI, bo obróbka miedzi wymaga doświadczenia w kamieniarstwie i garncarstwie, a te z kolei doświadczenia w rolnictwie.
W ziarnie 3 farma mogła stanąć od 16. minuty, a AI postawiła ją dopiero w 28.: decyzje gospodarki przed nauczycielem (domy, magazyny) zabierają jej ruch. To następna rzecz do poprawy.

## Ocena i uwagi (uczciwie)

1. **Głód jest wysoki:** 44% próbek ma głodną osadę. Flaga głodu trwa całą minutę po nieudanej racji, więc liczba jest wyższa niż przed racjami, ale nowe obozy naprawdę głodują (znany problem 20). Do poprawy: osadnicy z większym zapasem albo budowa zbieraczy w nowym obozie od razu.
2. **Wojny są losowe:** od 0 do 21 wypowiedzeń na grę. Decyzja N17 patrzy tylko na siłę i charakter, nie na korzyść. Czeka na nowy system walki (kwestionariusz `claude/walka-burza-mozgow.md`).
3. **Badanie jest proste:** N8 to „najwcześniejsza era, podstawy najpierw”. Nie patrzy na teren ani na to, czego lud potrzebuje. W 40 minutach AI kończy w neolicie (era 0,9 średnio).
4. **Brak statków** (znany problem 19) i brak zarządcy i prowincji (21).
5. **Liczby są w kodzie**, nie w danych: progi z kolumny „Liczby”. Przeniesienie ich do `data/sim/ai.json` jest w planie razem ze strojeniem po nowym systemie walki.
4. **Średniowiecze poza zasięgiem AI:** droga do epoki żelaza i dalej idzie przez miedź. Doświadczenie w obróbce miedzi daje tylko kopalnia miedzi, a ta wymaga znanego złoża na własnej ziemi. Bez miedzi nie ma brązu, a bez brązu ani epoki żelaza, ani średniowiecza. Podobnie matematyka: doświadczenie daje tylko plac budowniczych, a prawo i budownictwo nie mają żadnego budynku-nauczyciela. Do decyzji Patryka: drugi nauczyciel dla tych technologii albo osadnicy AI, którzy celują w złoża miedzi.
5. **Dużo ludzi:** po 80 minutach 4 ludy mają średnio ok. 8900 ludzi. To dobrze dla tempa, ale trzeba sprawdzić czas taktu na dużej mapie.
