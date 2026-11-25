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
| 8 | Pokój z negocjacji: zasoby, osada, okres pokoju, podpowiedź łupów z przebiegu wojny | zrobione: PR `feat/peace-negotiation` (ADR 0030: warunki z dóbr, osady i długości pokoju, zmęczenie wojną) |
| 9 | Drobne rzeczy w kliencie: głód oddziału w polu w panelu, przegląd tekstów | zrobione: #131 |
| 10 | Pomiar FPS na komputerze Patryka | zrobione 2026-11-22: średnio 165–180 FPS |
| 11 | Nowy pomiar `--sim-report` po PR #134–#142 (głód, warsztaty, takty, wojny) | zrobione 2026-11-23 (liczby w `docs/sim/ai.md`, sekcja o pokoju) |
| 12 | Dowóz wsadu do warsztatów stałymi trasami (known-issues #13) | zrobione: PR `fix/ai-input-routes` (warsztaty bez wsadu 36% → 28%); zostaje za mała produkcja żelaza i węgla |
| 13 | Wspólna droga dla oddziałów jednej wyprawy (known-issues #15) | zrobione: PR `fix/march-shared-path` (takty ponad 25 ms 7,9 → 3,0 na grę) |
| 14 | Scenariusz 03 testuje samą walkę (`noPeace`, decyzja Patryka 2026-11-23) | zrobione |
| 16 | Zmiany po testach z 2026-11-23: krótszy neolit, badania bez doświadczenia, przeżycie biernego ludu, AI z oceną zysku i zwiadem, odnawialna reputacja, alarm „obcy”, przegrana przy zerze ludzi, dziennik decyzji AI | zrobione: PR z `claude/project-thread-wrj4qa`; zostaje cel średniowiecza w 45. minucie (known-issues 17) |
| 17 | Sterowanie z klawiatury (kursor klawiatury, Q/E/R/J, Home, Page Up/Down) i instrukcja gry w menu i pod F1 (`docs/sterowanie-i-instrukcja.md`) | zrobione: PR `claude/project-thread-ea3s8b` |
| 18 | Dźwięk: muzyka według terenu i ery (3 utwory po 5 min na teren i erę, grane z nut), ciche otoczenie, efekty rozkazów i zdarzeń, suwaki głośności (`docs/audio.md`) | zrobione: PR `claude/project-thread-ex38kq`; ocena na słuch: Patryk (known-issues 18) |
| 15 | Runda testów demo na komputerze Patryka | Patryk, po 8 i 11–13 |

## Po demo

- Wydzielenie gry legacy, kroki 2–4 ADR 0029 (decyzja Patryka 2026-11-23: po demo).

- Zarządca i prowincje (`claude/decyzje-skalowanie.md`).
- AI na statkach.
- Przenoszenie mieszkańców za opłatą (złoże pod osadą) i przenosiny osad.
- Grafika pixel art Patryka w miejsce grafik zastępczych. Plan, lista wszystkich grafik i inspiracje: `docs/grafika-plan.md` (decyzje G1–G6 czekają na Patryka); grafiki zastępcze nowej gry mogą wejść już w trakcie testów demo.
- Multiplayer przez sieć: patrz niżej.

## Multiplayer (osobny wątek, równolegle z poprawkami gry; ADR 0031, `docs/multiplayer/plan.md`)

| # | Zadanie | Stan |
| --- | --- | --- |
| CI1 | Podział testów (szybkie lokalnie, pełne w CI), CI na `development`, wersja gry `v26.11.1` | zrobione: #182 |
| W1 | Zaawansowane ustawienia świata w „New game” (zwijana sekcja) | w PR |
| MP1 | Połączenie TCP, serwer bez okna, pomiary w serwerze | do zrobienia |
| MP2 | Serwer dla kilku ludzi (przejęcie przez AI, błędy klienta, dołączenia w zapisie, prędkość, wersje) | do zrobienia |
| MP3 | Konfiguracja serwera i lobby, menu Multiplayer | do zrobienia |
| MP4 | Gra z ludźmi: czat, głosowania, obserwatorzy, wyrzucanie | do zrobienia |
| MP5 | Zapis i wczytanie partii wieloosobowej | do zrobienia |
| MP6 | Pośrednik, Docker, gałąź `server` z CI, instrukcja VPS | do zrobienia |
