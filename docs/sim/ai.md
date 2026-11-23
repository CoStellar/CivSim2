# AI nowej gry (ADR 0028): wszystkie decyzje

AI nowej gry jest w `src/CivSim.Sim/Ai/`: `SimAi.cs` (rytm myślenia i wspólne narzędzia), `SimAi.Economy.cs` (budowa, badania, awanse), `SimAi.Expansion.cs` (zwiadowcy, poszukiwacze, osadnicy), `SimAi.War.cs` (wojsko i wojna), `SimAi.Diplomacy.cs`, `SimAi.Supply.cs` (karawany i zaopatrzenie) i `SimAi.Care.cs` (opieka). Podział z 2026-11-22; starsze ścieżki `SimAi.cs:linia` w tabelach niżej wskazują stan przed podziałem. Ta strona opisuje każdą jego decyzję.

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
| N4 | Gospodarka | mniej budów naraz niż 1 + liczba osad | po kolei: jedzenie, gdy netto < 3 na minutę, głód albo poza zimą produkcja jedzenia < zjadanie + 30% (`ai.foodMarginPercent`), do 1 + ludzi/400 budów jedzenia naraz (`ai.foodSitesPerPeople`); domy, gdy ludzi ≥ domy − 4, poza zimą i bez braku jedzenia; magazyn, gdy coś jest pełne; budynek uczący brakującego doświadczenia (z pierwszeństwem); praca dla bezrobotnych (≥ 3) według brakującego dobra do badania albo najmniejszego zapasu | netto 3/min, zapas 30%, 400 ludzi na budowę, domy −4, 3 bezrobotnych | `Ai/SimAi.cs:167` |
| N5 | Wybór heksa pod budynek | przy każdej budowie | heks w osadzie, która ma materiały: bliżej centrum, więcej bezrobotnych, +20 za znane złoże z premią, +15 za premiową rzeźbę | +20, +15 | `Ai/SimAi.cs:100` |
| N6 | Opieka | jak N4 | narzędziownia, gdy brak narzędzi i jest kamieniołom; krawiec na 3 minuty przed zimą, gdy ubrań < 1 na osobę; budynki wsparcia (zielarz przy łowcach, kamieniarz przy kamieniołomie, młyn przy 2 farmach, gildia przy 3 warsztatach, łaźnia od 50 ludzi, szkoła od 40, akademia od 70, uniwersytet od 90); włącza zbieranie | progi w kolumnie „Co robi” | `Ai/SimAi.Care.cs:93` |
| N7 | Prace na heksach | jedna naraz | w czasie wojny pierścień muru (promień 1) i palisady (promień 2); rów od wody do suchej farmy; akwedukt do miasteczka | promienie 1 i 2 | `Ai/SimAi.Care.cs:197` |
| N8 | Badanie | nic nie badamy | technologia, na którą nas stać: wcześniejsze ery przed późniejszymi (−10 za erę), podstawy +30, technologie broni ± (wojowniczość − 50) / 5 | −10, +30 | `Ai/SimAi.cs:433` |
| N9 | Myśliciel | jest oferta rzadszego myśliciela, a koszt zostawia połowę zapasów | zatrudnia | 50% | `Ai/SimAi.cs:472` |
| N10 | Awans osady | ludzi ≥ domy − 2, cena zostawia 20% zapasów, lud nie głoduje i ma zapas jedzenia na zimę (jak N4) | awansuje osadę | −2, 20% | `Ai/SimAi.cs:480` |
| N11 | Przebudowa (nowa 2026-11-19) | nic innego nie zbudował; lepszy rodzaj znany, miejsce i teren pasują | przebudowuje budynek w lepszy, najpierw domy w pełnych osadach (domy tylko bez braku jedzenia, jak N4), gdy reszta ceny zostawia 20% zapasów | domy −4, 20% | `Ai/SimAi.cs:497` |
| N12 | Zwiadowcy | jeden, przez pierwsze 20 minut, gdy stać przy zostawieniu połowy | trenuje; stojący idzie na losowy nieznany heks w promieniu ok. 14 × 10 | 20 min, 50% | `Ai/SimAi.cs:547` |
| N13 | Poszukiwacze i mierniczy (nowa 2026-11-19) | po 15. minucie, ≥ 3 osady, bez głodu, bez osadników w drodze i bez innego szkolenia | jeden naraz: mierniczy w epoce kamienia, poszukiwacz od neolitu; po zbadaniu miejsca idzie na niezbadaną wolną ziemię do ok. 12 heksów od losowej osady (cel ze skrótu, nie z losowania AI) | 15 min, 3 osady | `Ai/SimAi.cs:579` |
| N14 | Osadnicy | żadnych w drodze, osad < 2 + ekspansja/25 + era, jedzenie netto > 0, osada ≥ 20 ludzi, cena zostawia 30% | trenuje; osadnicy zakładają obóz, gdzie stoją, jeśli wolno, inaczej idą do najlepszego miejsca (N15) | 20 ludzi, 30% | `Ai/SimAi.cs:643` |
| N15 | Miejsce nowej osady | osadnicy szukają | znany wolny heks 6–12 od osadników: jedzenie biomu × 10, +15 przy wodzie, − odległość, + znana gleba w promieniu 2 (żyzność / 40 na heks) + 6 za każde znane złoże skalne, + 4 za złoże z jedzeniem (zmiana 2026-11-19) | ×10, +15, /40, +6, +4 | `Ai/SimAi.cs:684`, `:714` |
| N16 | Wojsko | żołnierzy < ludzie × (5 + wojowniczość/5)%, jedzenie netto ≥ 2, cena zostawia 20% | najsilniejszy znany rodzaj; trenuje go osada, która ma ≥ ludzi oddziału + 15 i najwięcej jedzenia na zbyciu, co najmniej tyle, ile oddział je (racje) | 5–25% ludzi | `Ai/SimAi.cs:751` |
| N17 | Wojna | co myślenie | broni swojej ziemi przed widocznym wrogiem; w wojnie atakuje najbliższą znaną osadę i proponuje pokój, gdy przegrywa (wynik wroga > nasz + 20), gdy jest zmęczona wojną (≥ 60, od 2026-11-23) albo losowo (3%); warunki: N38; nową wojnę zaczyna po 15. minucie (`ai.peacefulStartMinutes`), przy wojowniczości ≥ 40, z siłą > 2 × obrona celu i szansą wojowniczość/10 %; sojuszników nie rusza, partnerów handlu tylko przy wojowniczości ≥ 85; przy wojowniczości ≥ 75 atakuje bez wypowiedzenia | 15 min, 40, × 2, +20, 3% | `Ai/SimAi.cs:791` |
| N18 | Zaopatrzenie | co myślenie | ponawia materiały na budowy, których tragarze zginęli; osadzie głodnej albo z ujemnym jedzeniem i zapasem na mniej niż rację wysyła karawanę z jedzeniem na 2 minuty jej jedzenia (co najmniej 40) z osady, która ma najwięcej ponad własne 2 minuty (co najmniej 60) | 2 min, 40, 60 | `Ai/SimAi.cs:947` |
| N19 | Umowy | oferta do nas albo warunki umowy spełnione | przyjmuje handel, gdy proponujący ma dobrą reputację, jest korzyść (dobro albo rzemiosło, którego nam brak), a wojowniczość < 85; sojusz przy wspólnym wrogu i wojowniczości < 70; sam proponuje handel (wojowniczość < 70) i sojusz (< 50) | 85, 70, 50 | `Ai/SimAi.cs:909`, `:1000` |
| N20 | Odpowiedź na pokój (od 2026-11-23: ADR 0030, 5A) | oferta do nas | liczy wartość warunków (dobra po `worth`, osada: 10 jedzenia na mieszkańca + połowa ceny budynków); przyjmuje, gdy dostaje co najmniej tyle, ile według przebiegu wojny powinna dostać (albo oddaje najwyżej tyle, ile powinna oddać), z zapasem 20% × (100 − wojowniczość)/100; przegrywając woli długi pokój (zapas +10 punktów przy 10 min, −10 przy 3 min), wygrywając krótki; oddaje cokolwiek dopiero po 5 zabitych albo przy zmęczeniu ≥ 60 (E1) | `peace.aiGiveMarginPercent` 20, `aiLosingLengthPercent`, `aiWinningLengthPercent`, `weariness.aiPeaceAt` 60 | `Ai/SimAi.Diplomacy.cs` (`AcceptsPeace`) |
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
| N34 | Droga do celu i zwiad bez skoków (sprint 2026-11-20) | wybór osady do ataku; zwiadowca szuka nowego celu | atakuje tylko osadę, do której jest droga z miejsca jego wojska (pamięta wynik 2 min, szuka najwyżej 20 000 heksów); zwiadowca próbuje tylko heksów lądu, jedno szukanie drogi (do 1500 heksów) na myślenie, a gdy nic nie wyszło, czeka 30 s; nie wypowiada wojny ponad `war.maxWars` | 2 min, 20 000, 1500, 30 s | `Ai/SimAi.cs` (`WayTo`, `Scouts`, `War`) |
| N35 | Odwrót rannych (roadmapa 7, 2026-11-21) | co myślenie, w czasie wojny | oddział poza naszą ziemią z co najmniej połową rannych albo mniej niż 30% swojego rozmiaru idzie do domu, leczy się i go pilnuje; nie rusza na wyprawy | `aiBattle.retreatInjuredPercent` 50, `retreatSizePercent` 30 | `Ai/SimAi.cs` (`Hurt`) |
| N36 | Bitwa w polu i flanka (roadmapa 7) | co myślenie | widoczna wroga armia do 5 heksów od naszego zdrowego żołnierza: atakują ją wszyscy nasi żołnierze w tej odległości, więc otaczają ten sam cel (flanka); dopiero potem obrona ziemi i wyprawy | `aiBattle.engageHexes` 5 | `Ai/SimAi.cs` (`FieldBattle`) |
| N37 | Zbiórka w szyku przed wyprawą (roadmapa 7) | wyprawa na osadę | punkt zbiórki: nasza osada najbliższa celu; żołnierze dalej niż 3 heksy (ale do 30) idą tam w szyku (piechota z przodu, łucznicy z tyłu, jazda na skrzydłach); marsz na cel, gdy stoi tam 70% siły albo inni już idą na ten cel | `aiBattle.musterHexes` 3, `musterFromHexes` 30, `musterPercent` 70 | `Ai/SimAi.cs` (`Offensive`) |
| N38 | Warunki pokoju AI (ADR 0030, 2026-11-23) | AI proponuje pokój (N17) | wygrywając: podpowiedź gry (osada, którą wolno wziąć, i dobra, których jej brakuje, na wartość z przebiegu wojny), pokój 3 min; przegrywając: biały pokój na 10 min, a przy zmęczeniu ≥ 80 dobra na wartość, którą powinna oddać | `weariness.aiPayAt` 80 | `Ai/SimAi.War.cs` (`PeaceOffer`), `SimGame.SuggestTerms` |
| N39 | Stałe trasy z wsadem (2026-11-23) | osada ma < 6 minut pełnego zużycia wsadu swoich warsztatów | stała trasa z osady, która ma najwięcej do oddania: 3 minuty zużycia na kurs, dopóki cel ma < 6 minut; źródło zatrzymuje 2 minuty swojego zużycia; trasę bez zapasu w źródle zamyka; najwyżej 4 trasy na osadę | `ai.inputRouteTripMinutes` 3, `inputRouteTargetMinutes` 6, `inputRouteKeepMinutes` 2, `inputRoutesPerSettlement` 4 | `Ai/SimAi.Supply.cs` (`InputRoutes`) |
| N33 | Premie rodzajów (#112) | rodzaj jednostki osiągnął poziom | łucznicy i machiny: Hardened; jazda: na zmianę Forced march i Hardened; piechota: na zmianę Hardened i Shield wall | — | `Ai/SimAi.cs` (`VeteranChoiceFor`) |

## Warsztaty i ich surowce (2026-11-22)

Po 120 minutach 60% warsztatów, które zużywają towary, stało bez wsadu (12 seedów), głównie kowale bez węgla drzewnego i żelaza oraz brązownicy bez cyny. Przyczyny: lista „praca dla bezrobotnych” stawiała kowala i brązownika bez patrzenia na surowce; przebudowa zamieniała narzędziarzy w kowali; nowy wytwórca surowca powstawał dopiero przy zapasie < 5 i zerowym przyroście, a warsztat bez wsadu nic nie zużywa, więc bilans wyglądał dobrze.

1. **Zapotrzebowanie zamiast bilansu:** AI liczy, ile wszystkie jej pracujące (i budowane) warsztaty zużyłyby przy pełnej obsadzie (`Demand`).
2. **Nowy warsztat tylko z pokrytym wsadem:** z listy „praca dla bezrobotnych” i jako drugi (i kolejny) wytwórca dobra AI stawia warsztat tylko wtedy, gdy produkcja każdego jego surowca pokrywa zapotrzebowanie razem z nim albo w magazynie leży 10 minut jego zużycia (`InputsCovered`). Pierwszy warsztat danego rodzaju może stanąć zawsze, żeby jego surowce dostały wytwórców.
3. **Przebudowa** (N11) w rodzaj, którego wsadu nie starcza, czeka.
4. **Wytwórca surowca** (późne ery, `InputMaker`): gdy produkcja dobra jest mniejsza niż zapotrzebowanie, a zapas < 20 (było: zero produkcji i zapas < 5).

Pomiar `--sim-report --seeds 12 --minutes 120` (mapa 98×60, 4 ludy), przed → po: warsztaty bez wsadu 60,2% → 41,4%, ludzie 12 196 → 13 145, głód 3,8% → 3,4%, technologie 56,3 → 56,3, epoka brązu 33,2 → 34,2 min, żelaza 60,9 → 60,0 min (11 z 12 gier; było 12), średniowiecze 70,8 → 69,1 min (11 z 12). Gra 5 nie doszła do żelaza: nikt na jej mapie nie znalazł cyny (wcześniej przebieg był szczęśliwszy). Większe karawany z wsadem (5 minut zużycia) sprawdzone i odrzucone: nie pomagały.

## Stałe trasy z wsadem dla warsztatów (2026-11-23, znany problem 13)

Pojedyncze karawany z wsadem (do 30 sztuk, gdy w osadzie zostało < 10) przychodziły za późno dla stale pracujących warsztatów.
Teraz (N39, `SimAi.Supply.cs`, `InputRoutes`):

1. **Zużycie przy pełnej obsadzie:** AI liczy, ile warsztaty każdej osady zużyłyby na minutę.
2. **Trasa zamiast karawany:** osada, która ma mniej niż 6 minut tego zużycia (`ai.inputRouteTargetMinutes`), dostaje stałą trasę
   z naszej osady, która ma tego dobra najwięcej do oddania. Trasa wozi 3 minuty zużycia na kurs (`inputRouteTripMinutes`) i jeździ,
   dopóki cel ma mniej niż 6 minut zużycia. Źródło zatrzymuje 2 minuty własnego zużycia (`inputRouteKeepMinutes`).
   Warsztat bez wsadu nic nie zużywa, więc AI patrzy na zapas względem pełnego zużycia, a nie na bilans.
3. **Zamknięcie trasy:** gdy źródło nie ma już nic do oddania i samo tego nie robi, AI kończy trasę. Lepsze źródło może wtedy przejąć dowóz.
4. **Limit:** najwyżej 4 trasy na każdą osadę ludu (`inputRoutesPerSettlement`). Przy 2 część ludów stała na limicie.
   Pojedyncza karawana zostaje tam, gdzie nie ma trasy.

Pomiar `--sim-report --seeds 12 --minutes 120` przed → po: warsztaty bez wsadu 36,1% → 28,0% (mediana 41% → 30%), głód 4,1% → 2,2%,
ludzie 11 704 → 11 625, epoka żelaza 56,7 → 57,7 min, średniowiecze 66,1 → 68,1 min (w granicach rozrzutu).
Raport pokazuje teraz, czego brakuje stojącym warsztatom: po zmianie głównie żelaza (328) i węgla drzewnego (277), a lud prawie zawsze
ma je gdzie indziej. **Zostaje:** produkcja żelaza i węgla bywa kilka razy mniejsza niż zużycie (np. żelazo: wydobycie 16 na minutę,
zużycie 90). Tego trasy nie naprawią: AI stawia więcej kuźni, niż starcza wsadu (znany problem 13).

## Jedzenie dużych ludów (2026-11-22)

Długie biegi (6 seedów, 120 minut) pokazały, że największe ludy (4000+ osób) od ok. 90. minuty przerastają swoje jedzenie: latem jedzenia jest tyle, ile się zjada, zimą zapas znika i ludzie umierają (do 3000 głodnych w jednym ludzie). Przyczyna: stałe progi AI, które nie rosną z ludem (+3 jedzenia na minutę, jedna budowa jedzenia naraz, karawana 40 jedzenia), a domy i awanse liczyły tylko zapas w magazynie.

1. **Zapas na zimę:** poza zimą AI chce produkcji jedzenia o 30% większej niż zjadanie (`ai.foodMarginPercent`); bez tego buduje jedzenie i nie stawia domów, nie awansuje osad i nie przebudowuje domów.
2. **Kilka budów jedzenia naraz:** jedna więcej na każde 400 ludzi (`ai.foodSitesPerPeople`).
3. **Karawana z jedzeniem na miarę osady:** 2 minuty jej jedzenia zamiast stałych 40; rusza też, gdy osada traci jedzenie i ma mniej niż rację.

Pomiar `--sim-report` (mapa 98×60, 4 ludy), przed → po: 40 min, 12 seedów: głód 11,8% → 4,8% próbek, ludzie 468 → 543, neolit 6,2 → 5,8 min, brąz 32,2 → 32,5 min; 120 min, 6 seedów: głód 11,0% → 4,2%, wymarłe ludy 0,2 → 0, średniowiecze 74,0 → 71,3 min. Zostaje: gdy na ziemi ludu brak miejsca na nowe budynki, ludzie i tak dorastają do domów (seed 1, jeden lud); AI tego nie zatrzyma bez zmiany zasady wzrostu.

## Pokój z negocjacji i zmęczenie wojną (2026-11-23, ADR 0030)

AI ocenia każdą propozycję pokoju w wartości (N20) i sama proponuje warunki z podpowiedzi gry (N38). Zmęczenie wojną (opis w `docs/sim/walka.md`)
pcha ją do pokoju od 60 punktów i do oddania dóbr od 80. Pomiar `--sim-report --seeds 12 --minutes 120` przed → po: wypowiedziane wojny 58,4 → 43,2
na grę, pokoje 48,6 na grę, osady przekazane w pokoju 2,4 na grę, zajęte zbrojnie 3,3 → 4,1, głód 2,3% → 3,1% próbek, ludzie 12 066 → 13 271,
warsztaty bez wsadu 37,2% → 34,4%. Rozrzut 12 gier jest duży, więc różnice poniżej ok. 20% to szum.

**Uwaga:** ok. 45 wojen i pokojów na grę 4 ludów w 120 minut to dużo: wojny są krótkie. Przyczyny: losowa propozycja pokoju (3% na myślenie)
i częste wypowiadanie wojen przez wojownicze ludy. Do oceny w testach demo (`docs/known-issues.md`, punkt 16).

## Decyzje po testach z 2026-11-20 (16B, 17B i AI do 7A)

1. **Domy przy zapasie jedzenia (16B):** AI stawia nowe domy tylko wtedy, gdy ma jedzenie na 3 minuty jedzenia wszystkich (`ai.homesFoodMinutes`).
2. **Mały lud (17B):** poniżej 30 osób (`ai.smallPeople`) AI nie szkoli oddziałów (zwiadowców, poszukiwaczy, wojska; osadników tak) i nie stawia nowego budynku z pracownikami, dopóki ma wolne miejsca pracy. Zwiadowcy wychodzą, gdy lud dorośnie do 30 osób, najpóźniej do 30. minuty (było: do 20.).
3. **Droga do epoki żelaza (7A):**
   - gdy brama ery jest znana, AI najpierw buduje to, co robi dobra potrzebne do wejścia w następną erę (brąz), a gdy wytwórca stoi bez surowca, buduje jego dostawcę (kopalnię cyny);
   - złoża potrzebne do tego (cyna, miedź) i złoża kopalń dających brakujące doświadczenie (ruda żelaza) przyciągają osadników: osada stawia się do 2 heksów od znanego złoża, do 25 heksów od nas, i może to być jedna osada ponad zwykły limit AI;
   - poszukiwacze szukają do 22 heksów od osad, dopóki takiego złoża nie znają.

Pomiar (`--sim-report --seeds 12 --minutes 120`, mapa 98×60; development → wszystkie decyzje z 2026-11-20):

| | Przed | Po |
| --- | --- | --- |
| Ludzie (średnio) | 16 848 | 11 154 |
| Głód (% próbek) | 31,8 | 10,8 |
| Era (średnia ludów) | 3,2 | 3,5 |
| Technologie | 43,9 | 52,1 |
| Średniowiecze: ile gier | 10 z 12 | 12 z 12 |
| Pierwszy brąz / żelazo / średniowiecze [min] | 32,6 / 44,4 / 68,3 | 37,0 / 59,1 / 73,0 |
| Wojny na grę | 16,8 | 39,3 |
| Zajęte / złupione osady | 0,6 / 9,4 | 4,1 / 9,1 |
| Wymarłe ludy na grę | 0,0 | 0,2 |

## Zmiany z testów 2026-11-20

1. **Awans karawanami (A1):** po nieudanym awansie AI czeka 60 s (`upgradeCaravans.aiRetrySeconds`) i nie próbuje, gdy towary już jadą. Pomiar (mapa 98×60, 4 ludy, 100 min): nieudane awanse 673 → 12 (seed 1) i 751 → 7 (seed 2).
2. **Zaopatrzenie (A2):** bez drogi do oddziału AI nie wysyła karawany ani nie cofa oddziału przez 60 s.
3. **Cel wojny (A3, A9):** AI szuka celu tylko u ludów, z którymi walczy albo może zacząć wojnę; zna tylko osady, które widzi albo pamięta (`SeenSettlements`), a obronę zapamiętanej ocenia z tego, co widziała (ludzie, straż, obrona). Pamięć „czy jest droga” trzyma się celu i pierwszej osady AI, nie heksu armii.

Czas taktu (sama symulacja, wszystkie ludy AI, przed → po):

| Mapa | Ludy | Minuty | Średnio [ms] | 99% taktów poniżej [ms] | 99,9% poniżej [ms] | Najdłuższy [ms] |
| --- | --- | --- | --- | --- | --- | --- |
| 98×60 | 8 | 120 | 0,55 → 0,37 | 8,3 → 2,9 | 23 → 6 | 77 → 69 |
| 194×120 | 16 | 90 | 1,11 → 0,53 | 16,8 → 3,7 | 77 → 11 | 158 → 91 |

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

## Tempo pierwszych 15 minut (roadmapa 6, 2026-11-21)

Zapas na starcie: 120 jedzenia, **100 drewna** (było 60), **20 kamienia** (bez zmian). Pomiar: 12 gier × 4 ludy, pierwsze 15 minut, `development` → po zmianie:

| | przed | po |
| --- | --- | --- |
| pierwsze chaty | 7,4 min | 1,9 min |
| pierwszy kamieniołom | 9,4 min (47/48) | 7,8 min (48/48) |
| pierwszy narzędziarz | 11,8 min (35/48 ludów do 15. min) | 10,4 min (48/48) |
| pierwsza farma do 15. min | 9/48 ludów | 14/48 |
| drewno w 5. min (mediana) | 8 | 14 |
| drewno w 10. min (mediana) | 18 | 20 |
| neolit | 7,6 min | 5,9 min |
