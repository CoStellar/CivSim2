# CivSim 2 — sprint do demo

**Cel: grywalne demo do średniowiecza.** Partia ok. 60 minut przeciw kilku ludom AI, od wyboru miejsca startu do celu partii.
Projekt gry: `docs/projekt-gry.md`. Stara roadmapa (legacy): `docs/legacy/ROADMAP.md`.

## Gotowe (stan 2026-11-23, `development`)

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
| 1 | Poprawki po rundzie testów 2 (`claude/runda-testow-2.md`) i po raporcie z 2026-11-20 | zrobione: PR #116–#126 (decyzje w `claude/decyzje-testy-2026-11-20.md`) |
| 2 | Zapis i wczytanie partii | zrobione (przycisk Save / F5, autozapis co 5 min, sekcja Load game w menu) |
| 3 | Skoki czasu taktu (najgorszy takt 212 ms w scenariuszu 10) | zrobione (#114); dziś najgorszy takt w scenariuszach 60–170 ms, średnio 0,1–0,3 ms |
| 4 | Najwyżej 2–3 wojny naraz u jednego ludu (projekt gry, rozdział 8) | zrobione (#114, `war.maxWars` = 3) |
| 5 | Późne ery: węgiel drzewny, cegła, deski, wełna i sukno, zaprawa; koszty nowej ery; przebudowa budynku w lepszy i zastąpienie na heksie | zrobione: #127 (materiały i koszty), #128 (zastąpienie budynku) |
| 6 | Tempo pierwszych 15 minut (brak drewna na starcie, pierwszy narzędziarz w 13. minucie) | zrobione: #129 (100 drewna na start: narzędziarz w 10. minucie u wszystkich ludów); ocena Patryka w testach demo |
| 7 | AI walki: szyk, flanki, odwrót rannych | zrobione: #130 |
| 8 | Pokój z negocjacji: zasoby, osada, okres pokoju, podpowiedź łupów z przebiegu wojny | decyzje Patryka 2026-11-23: wszystkie rekomendacje (`docs/decisions/0030-pokoj-z-negocjacji.md`); czeka na decyzję o zmęczeniu wojną, potem jeden PR |
| 9 | Drobne rzeczy w kliencie: głód oddziału w polu w panelu, przegląd tekstów | zrobione: #131 |
| 10 | Pomiar FPS na komputerze Patryka | zrobione 2026-11-22: średnio 165–180 FPS |
| 11 | Nowy pomiar `--sim-report` po PR #134–#142 (głód, warsztaty, takty, wojny) | do zrobienia |
| 12 | Dowóz wsadu do warsztatów stałymi trasami (known-issues #13) | do zrobienia |
| 13 | Wspólna droga dla oddziałów jednej wyprawy (known-issues #15) | do zrobienia |
| 14 | Scenariusz 03 testuje samą walkę (`noPeace`, decyzja Patryka 2026-11-23) | zrobione |
| 15 | Runda testów demo na komputerze Patryka | Patryk, po 8 i 11–13 |

## Po demo

- Wydzielenie gry legacy, kroki 2–4 ADR 0029 (decyzja Patryka 2026-11-23: po demo).

- Zarządca i prowincje (`claude/decyzje-skalowanie.md`).
- AI na statkach.
- Przenoszenie mieszkańców za opłatą (złoże pod osadą) i przenosiny osad.
- Grafika pixel art Patryka w miejsce grafik zastępczych.
- Multiplayer przez sieć.
