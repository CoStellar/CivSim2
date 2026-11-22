# Znane problemy (nowa gra)

Problemy, o których wiemy, a których jeszcze nie naprawiono. Gdy naprawisz punkt, przenieś go do „Naprawione” z numerem PR;
gdy znajdziesz nowy, dopisz go z datą, źródłem i tym, co już wiadomo. Problemy gry legacy: `docs/legacy/known-issues.md` (nie zajmujemy się nimi).

## Otwarte

| # | Problem | Skąd wiemy | Co wiadomo | Co dalej |
| --- | --- | --- | --- | --- |
| 1 | **FPS klienta nie jest zmierzony.** | — | W chmurze Godot działa pod xvfb (zrzuty ekranu są możliwe), ale rysuje procesor, więc FPS nic nie mówi. | Pomiar na komputerze Patryka. |
| 3 | **Niestabilny test serwera** `ObserverLayerTests.ObserverGetsLayersAndTheFogOfAWatchedCivilization`: raz na kilka przebiegów pada w całym zestawie, sam przechodzi. | pamięć projektu | W przebiegach z 2026-11-20 nie padł. | Gdy padnie: przyczyna (zależność od czasu?), nie wyłączać testu. |
| 4 | **AI nie korzysta ze statków.** Nie wsiada na transportowce i nie szuka handlu morskiego. | przegląd 2026-11-18 | U gracza statki działają. | Po demo; do demo mapy kontynentalne. |
| 5 | **AI trochę głoduje**, głównie w świeżych obozach. | `--sim-report`, 2026-11-22: 9,8% próbek z głodem (było 7–8%) | Ludzi przybywa szybciej od startu ze 100 drewna (#129), jedzenia nie przybywa tak samo. | Ocena Patryka w testach demo; jeśli przeszkadza, AI stawia chaty zbieraczy wcześniej. |
| 6 | **Civil service nie daje prowincji ani limitu administracji.** | plan technologii, 2026-11-18 | Daje urząd prowincji, awans do city (z Construction) i +3 do limitu oddziałów. | Etap „zarządca i prowincje” (po demo). |
| 8 | **Warsztaty bez wsadu** (24% próbek), głównie odlewnie brązu bez cyny i kuźnie bez węgla w swojej osadzie. | `--sim-report`, 2026-11-22 | Było 30% przed #132 (regiony cyny, AI wybiera większe złoża) i #127 (AI dostawia smolarzy). Cyna leży zwykle w innej osadzie albo dalej. | Przy strojeniu AI po demo: dowóz wsadu między osadami przy mniejszym zapasie. |
| 9 | **AI głównie łupi, rzadko zajmuje** (0,8 zajętej, 4,8 złupionych na grę). | `--sim-report`, 2026-11-22 | Mniej łupienia niż 2026-11-20 (11 na grę) dzięki bitwom w polu i zbiórce (#130). | Ocena Patryka w testach demo. |
| 11 | **Multiplayer: zapis nie wie, kiedy dołączył drugi gracz.** | przegląd A13, 2026-11-20 | Wczytanie oddaje lud graczowi od początku gry, a na żywo AI grała za niego do chwili dołączenia. W grze jednoosobowej bez znaczenia. | Przy multiplayerze: zapisywać dołączenie jako komendę. |
| 12 | **Scenariusz 03 („War test”) szybko kończy się pokojem**: oba ludy AI mijają się w drodze na stolice i godzą w 2. minucie. | przejście scenariuszy, 2026-11-22 | Scenariusz pozwala na pokój. | Jeśli ma testować samą walkę: `noPeace`. |

## Naprawione

| Problem | PR |
| --- | --- |
| Scenariusze: dodatkowe osady (`settlements`) zaczynały z pustym magazynem (w 04 jedzenie kończyło się po 28 s); teraz dostają zapas startowy (120 jedzenia, 100 drewna, 20 kamienia) | `docs/known-issues-before-demo` |
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
