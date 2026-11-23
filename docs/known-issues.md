# Znane problemy (nowa gra)

Problemy, o których wiemy, a których jeszcze nie naprawiono. Gdy naprawisz punkt, przenieś go do „Naprawione” z numerem PR;
gdy znajdziesz nowy, dopisz go z datą, źródłem i tym, co już wiadomo. Problemy gry legacy: `docs/legacy/known-issues.md` (nie zajmujemy się nimi).

## Otwarte

| # | Problem | Skąd wiemy | Co wiadomo | Co dalej |
| --- | --- | --- | --- | --- |
| 3 | **Niestabilny test serwera** `ObserverLayerTests.ObserverGetsLayersAndTheFogOfAWatchedCivilization`: raz na kilka przebiegów pada w całym zestawie, sam przechodzi. | pamięć projektu | W przebiegach z 2026-11-20 nie padł. | Gdy padnie: przyczyna (zależność od czasu?), nie wyłączać testu. |
| 4 | **AI nie korzysta ze statków.** Nie wsiada na transportowce i nie szuka handlu morskiego. | przegląd 2026-11-18 | U gracza statki działają. | Po demo; do demo mapy kontynentalne. |
| 5 | **AI trochę głoduje**, głównie w świeżych obozach. | `--sim-report`, 2026-11-22: 4,8% próbek z głodem po `fix/ai-food-scale` (było 11,8%) | Ludzi przybywa szybciej od startu ze 100 drewna (#129). Duży lud bez miejsca na nowe budynki dorasta do domów i głoduje co zimę (1 lud z 24 w 120 min): wzrost patrzy tylko na domy i jedzenie w magazynie. | Zasada wzrostu „wariant C” weszła w #142 (wzrost staje, gdy jedzenia ubywa i zostały < 2 racje). Zmierzyć ponownie (ROADMAP 11), potem ocena Patryka w testach demo. |
| 13 | **Warsztaty bez wsadu w późnej grze:** po 120 minutach 21% pracujących budynków, które zużywają dobra, stoi bez wsadu (było 60%, potem 41%, 36% i 28%). | `--sim-report --seeds 12 --minutes 120`, 2026-11-23, po `fix/ai-input-makers` | Braki: głównie żelazo (345 warsztatów) i miedź (165, w tym 103 bez miedzi w całym ludzie). Węgiel drzewny już nie brakuje. Kopalnie żelaza i miedzi potrzebują znanego złoża. Pomogły #127, #132, #139, stałe trasy i więcej wytwórców wsadu. | Zrobione 2026-11-23 (`fix/ai-ore-prospectors`): do 2 poszukiwaczy, którzy szukają dalej, gdy warsztatom brakuje rudy bez znanego wolnego złoża. Po 120 min: 18,8% bez wsadu (było 20,9%), warsztaty bez miedzi nigdzie w ludzie 103 → 6. Zostaje: za mało kopalń żelaza przy dużych ludach. |
| 14 | **Wycieki przy wyjściu z klienta:** Godot zgłasza 1 RID czcionki, 1 CanvasItem i 6 obiektów. | log Godota, komputer Patryka, 2026-11-22 | Bez wpływu na grę. | Przy porządkach klienta. |
| 6 | **Civil service nie daje prowincji ani limitu administracji.** | plan technologii, 2026-11-18 | Daje urząd prowincji, awans do city (z Construction) i +3 do limitu oddziałów. | Etap „zarządca i prowincje” (po demo). |
| 9 | **AI głównie łupi, rzadko zajmuje** (0,8 zajętej, 4,8 złupionych na grę). | `--sim-report`, 2026-11-22 | Mniej łupienia niż 2026-11-20 (11 na grę) dzięki bitwom w polu i zbiórce (#130). | Ocena Patryka w testach demo. |
| 11 | **Multiplayer: zapis nie wie, kiedy dołączył drugi gracz.** | przegląd A13, 2026-11-20 | Wczytanie oddaje lud graczowi od początku gry, a na żywo AI grała za niego do chwili dołączenia. W grze jednoosobowej bez znaczenia. | Przy multiplayerze: zapisywać dołączenie jako komendę. |
| 16 | **Dużo krótkich wojen:** ok. 43 wypowiedzianych wojen i 49 pokojów na grę 4 ludów w 120 minut (przed pokojem z negocjacji 58 wojen). | `--sim-report --seeds 12 --minutes 120`, 2026-11-23 | AI proponuje pokój losowo (3% na myślenie) i wojownicze ludy szybko wypowiadają kolejną wojnę po końcu pokoju. Dłuższe pokoje (3B) już zmniejszyły liczbę wojen o ok. 26%. | Ocena Patryka w testach demo: czy wojny mają być rzadsze i dłuższe (np. bez losowego pokoju albo dłuższa przerwa po pokoju u AI). |

## Naprawione

| Problem | PR |
| --- | --- |
| Za mało smolarzy i innych wytwórców wsadu: AI stawiała nowego dopiero przy zapasie < 20, więc duży zapas rozrzucony po osadach blokował budowę. Teraz buduje, dopóki zapas nie starcza na 10 minut niedoboru (warsztaty bez wsadu 28% → 21%, braki węgla drzewnego 277 → 6) | `fix/ai-input-makers` |
| Warsztaty bez wsadu z powodu transportu: AI wysyłała pojedyncze karawany. Teraz zakłada stałe trasy z wsadem od osady, która go ma (36,1% → 28,0% warsztatów bez wsadu). Problem 13 zostaje z powodu za małej produkcji | `fix/ai-input-routes` |
| Długie takty przy wyprawach wojennych: każdy oddział osobno szukał drogi do dalekiego celu (przy ataku i w marszu). Teraz żołnierze wysłani na ten sam cel do 3 heksów od siebie dzielą jedno szukanie (`march`). Pomiar `--sim-report --seeds 8 --minutes 120 --serial`: takty ponad 25 ms 7,9 → 3,0 na grę, najgorszy takt 48,8 → 40,6 ms (resztę zawyża pierwsza kompilacja kodu w procesie) | `fix/march-shared-path` |
| Scenariusz 03 („War test”) kończył się pokojem w 2. minucie; teraz testuje samą walkę (`noPeace`, decyzja Patryka 2026-11-23) | ten PR |
| `data/sim/game.json` nie był sprawdzany w menu ani przy Reload data, a literówka w nazwie klucza dawała po cichu wartość domyślną; teraz menu, Reload data, start nowej gry i test pokazują nieznane klucze. Przy okazji usunięty martwy klucz `war.levyStrength` (nikt go nie czytał) | #135 |
| Lud bez miejsca na nowe pola dorastał do domów i głodował co zimę (wzrost patrzył tylko na domy i jedzenie w magazynie); teraz wzrost staje, gdy jedzenia ubywa i zostało < 2 racje (wariant C) | #142 |
| FPS klienta zmierzony na komputerze Patryka (Ryzen 5 3600XT, RTX 3060 Ti, 2560×1440, V-Sync 180 Hz): menu 180, gra ręczna 6 min średnio 165 (najniżej 98), dwie gry skryptowe po 10 min (×4 i maks.) średnio 180 | pomiar 2026-11-22 |
| Duże ludy AI głodowały od 90. minuty (stałe progi jedzenia, domy i awanse bez zapasu na zimę, karawana 40 jedzenia) | #134 |
| Scenariusze: dodatkowe osady (`settlements`) zaczynały z pustym magazynem (w 04 jedzenie kończyło się po 28 s); teraz dostają zapas startowy (120 jedzenia, 100 drewna, 20 kamienia) | #133 |
| Wolny neolit, pierwszy narzędziarz ok. 13. minuty: teraz neolit 6,2 min, narzędziarz 10 min u wszystkich ludów | #129 |
| Głód oddziału w polu niewidoczny w panelu: panel pokazuje juki, stan głodu i dowóz | sprawdzone 2026-11-21 (#131) |
| Testy 2026-11-20: wczytanie gry z wyborem startu dawało inną grę (A13); widok zmieniał stan i hash gry (A14, A11, A21: zapisy po śmierci ludu gracza); gracz wybierający start jako pierwszy miał 1 s zamiast 30 | `fix/saves-determinism` |
| Testy 2026-11-20: awans karawanami u AI padał w 775 z 777 prób (A1); pętla szukania drogi dla karawan z jedzeniem (A2) i pamięć drogi AI po heksie armii (A3); AI znała osady z odkrytych heksów (A9) | `fix/caravans-and-supply` |
| Testy 2026-11-20: scenariusze bez drogi do wroga (A4), puste juki w scenariuszach (A5), mury zdobytego miasta u starego właściciela (A6), atak bez drogi przyjmowany po cichu (A7), wyciek przez mgłę przy ataku (A8), konie poniżej zera (A10), badanie znanej technologii po debug (B8) | `fix/war-and-scenarios` |
| Testy 2026-11-20: niejasne odmowy (A12), pasek dóbr zasłaniał erę (A15), kółko myszy nad oknem (A16), ucięte alerty (A17), zaznaczanie w oknie Units (A18), odmowa budowy poza ziemią (A19), trasa przez mgłę (A20), brak ekranu końca gry (A22), `--play-window` w scenariuszach (A23) | `fix/messages-and-ui` |
| Skoki czasu taktu do 300 ms: armie szukały drogi do osad za morzem dwa razy na sekundę, zwiadowcy szukali drogi do 12 razy na myślenie, szukanie drogi pytało o nazwę muru na każdym kroku | sprint 2026-11-20 (`feat/war-limit-ticks`) |
| AI nie stawiało kopalni miedzi w młodych obozach (złoże w osadzie bez towarów) | #109 |
| Atak na heks spoza mapy rzucał wyjątek | #111 |
| Ludy AI ginęły w wojnach w pierwszych 20–40 minutach (teraz 0,1–0,2 na grę) | #110 (oblężenie) |
| Gra stawała po odmowie ruchu, armia AI stała całą bitwę, wyjątki dla pustych list, rozwiązany oddział gubił broń | #106 |
| AI wybierało miejsca osad z wiedzy o całej mapie | #104 |
