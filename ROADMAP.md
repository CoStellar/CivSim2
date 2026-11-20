# CivSim 2 — sprint do demo

**Cel: grywalne demo do średniowiecza.** Partia ok. 60 minut przeciw kilku ludom AI, od wyboru miejsca startu do celu partii.
Projekt gry: `docs/projekt-gry.md`. Stara roadmapa (legacy): `docs/legacy/ROADMAP.md`.

## Gotowe (stan 2026-11-20, `development`)

- [x] Mapa-planeta, wybór miejsca startu z oceną, trzy zdania historii plemienia.
- [x] Osady z heksów, 4 miejsca na heksie, budynki z miejscami pracy, magazyny, domy, przyrost, racje, zima i ubrania.
- [x] Logistyka na mapie: tragarze, karawany, trasy z progami, drogi i mosty, stany heksa (rów, palisada, mur, akwedukt).
- [x] Ekspansja: osadnicy z zapasem, awans osad (minimum ludzi, dowóz towarów), ruiny i ich przejmowanie.
- [x] Mgła wojny z pamięcią, zwiadowcy, poszukiwacze i mierniczy, złoża późniejszych er jako „???”.
- [x] Drzewo technologii do średniowiecza z bramami er, myśliciele, doradca, zdarzenia, statki.
- [x] Walka na żywo: szyki, postawy, straż, broń i zbroja jako towary, dzielenie oddziałów, flanka, teren.
- [x] Oblężenie (otoczenie, straż, obrona mieszkańców, zajmij/złup/zburz), zaopatrzenie armii, limit oddziałów, doświadczenie rodzajów.
- [x] Dyplomacja: wojna jako stan, pokój z daniną, umowy handlu i sojuszu, wymiana, reputacja.
- [x] AI z tymi samymi komendami: gospodarka, towary między osadami, badania, ekspansja, wojna, zaopatrzenie.
- [x] Cele partii, wynik, podsumowanie; scenariusze testowe i tryb debug.

## Do demo

| # | Zadanie | Stan |
| --- | --- | --- |
| 1 | Poprawki po rundzie testów 2 (`claude/runda-testow-2.md`) | czeka na wyniki Patryka |
| 2 | Zapis i wczytanie partii | do zrobienia |
| 3 | Skoki czasu taktu (najgorszy takt 212 ms w scenariuszu 10) | do zrobienia |
| 4 | Najwyżej 2–3 wojny naraz u jednego ludu (projekt gry, rozdział 8) | do zrobienia |
| 5 | Późne ery: węgiel drzewny, cegła, deski, wełna i sukno, zaprawa; koszty nowej ery; przebudowa budynku w lepszy i zastąpienie na heksie | do zrobienia (decyzje 13–15 w `claude/decyzje-wojna-i-pozne-ery.md`) |
| 6 | Tempo pierwszych 15 minut (brak drewna na starcie, pierwszy narzędziarz w 13. minucie) | do zrobienia, potem ocena Patryka |
| 7 | AI walki: szyk, flanki, odwrót rannych | do zrobienia |
| 8 | Pokój z negocjacji: zasoby, osada, okres pokoju, podpowiedź łupów z przebiegu wojny | do zrobienia |
| 9 | Drobne rzeczy w kliencie: głód oddziału w polu w panelu, przegląd tekstów | do zrobienia |
| 10 | Pomiar FPS na komputerze Patryka | Patryk |

## Po demo

- Zarządca i prowincje (`claude/decyzje-skalowanie.md`).
- AI na statkach.
- Przenoszenie mieszkańców za opłatą (złoże pod osadą) i przenosiny osad.
- Grafika pixel art Patryka w miejsce grafik zastępczych.
- Multiplayer przez sieć.
