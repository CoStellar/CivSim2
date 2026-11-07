# ADR 0023: Ustroje w danych, tytuł w nazwie i opóźnienie decyzji państwowych

**Status:** przyjęta, 2026-11-07 (PR 1 etapu 4f; Patryk przyjął wszystkie rekomendacje z dokumentu „Etap 4f: państwo — propozycja”,
także zmiany GDD 7.2 i 7.3: opóźnienie w latach i wpływ warstw zależny od ustroju). Zasady w GDD 7.2, 7.3, 5.6 i 16.

## Kontekst

Cywilizacje nie miały ustroju. GDD 7.2 wymaga, żeby ustrój zmieniał tempo decyzji (opóźnienie wdrożenia i częstotliwość pracy
doradców), impulsywność AI, sposób sukcesji, zdolność administracyjną i stabilność, a nazwa cywilizacji niosła tytuł ustroju
(„Chiefdom of Karrut”). GDD 16 każe zrobić 3–4 ustroje z er 1–3, a napięcie społeczne wchłonąć w stabilność.

## Decyzja

- **Ustroje w danych:** `data/governments.json`, wczytywane i sprawdzane przy starcie (`ContentLoader.LoadGovernments`). Nowy ustrój
  w kolejnych erach to tylko wpis w pliku. Dziś: rada starszych i wodzostwo (od epoki kamienia), despotia i republika miejska
  (od epoki brązu, czyli od etapu 6).
- **Stan:** `CivilizationState.Government` (indeks w danych) i zmiana w toku (`PendingGovernment`, `PendingGovernmentMonth`).
  Zapis w formacie 26, hash, protokół w wersji 10 (tytuł i ustrój dla obserwatora).
- **Start:** każdy lud dostaje ustrój, do którego ciągnie jego charakter: suma cecha × skłonność (`leaning`) z danych, remis wygrywa
  pierwszy w pliku. Bez losowania, więc nic nie przesuwa strumieni.
- **Tytuł:** `Governments.Title` składa wzór z danych z rdzeniem nazwy ludu. Rdzeń (`CivilizationState.Name`) się nie zmienia, więc
  kronika dalej mówi „Karrut”; tytuł pokazują podgląd i raporty.
- **Opóźnienie wdrożenia w latach** (`enactYears`), nie w turach: tura epoki kamienia trwa 50 lat, więc „4 tury” z GDD to 200 lat.
  Działa na dwie decyzje państwowe, które dziś istnieją:
  - **zmiana ustroju** (`ChangeGovernmentCommand`): nowy ustrój wchodzi po opóźnieniu obecnego (`GovernmentSystem` w fazie
    State); do tego czasu cywilizacja nie wypowiada wojen ani nie zaczyna drugiej zmiany;
  - **wypowiedzenie wojny** (`DeclareWarCommand`): wojna zaczyna się od razu (ofiara wie i może się bronić), ale armie strony,
    która ją wypowiedziała, ruszają dopiero od `Treaty.ReadyMonth`. Zegar świata robi krok w tym miesiącu, a dowódca wtedy
    planuje wyprawę. Cicha wojna (5 lat bez kampanii) liczy się od gotowości, nie od wypowiedzenia.
- **Tempo AI:** `reviewPercent` skraca albo wydłuża odstęp przeglądów stratega i dyplomaty; `cautionShift` przesuwa ostrożność,
  z którą AI bierze najlepszą opcję wprost (rada spokojniejsza, despotia bardziej impulsywna); `goalPercent` mnoży wagi celów
  stratega (wodzostwo: ekspansja i podbój ×1,2).
- **Później w etapie 4f:** stabilność, wpływ warstw, administracja i sukcesja (PR 2–4) dopiszą kolejne pola do tego samego pliku;
  wybór ustroju przez stratega i zmiana po buncie (PR 4–5).

## Uzupełnienie: przywódcy (PR 2 etapu 4f)

- `data/leaders.json`: cechy przywódcy z przesunięciami cech charakteru, liczba cech, śmiertelność z wiekiem; sukcesja w polach ustroju.
- Charakter, którego używa AI, to cechy ludu plus przesunięcia przywódcy (`LeaderShift` zapisane w stanie, żeby czytać charakter bez
  zawartości gry). Cechy ludu zostają osobno, więc zmiana władcy jest skokowa, a powolna zmiana z doświadczeniem (później) nie miesza się
  z cechami przywódcy.
- Śmierć i następca liczone rok po roku w kroku cywilizacji, z generatora pochodnego (strumień `Leaders`, cywilizacja, miesiąc).
- Kryzys sukcesji to na razie tylko miesiąc końca (`CrisisUntilMonth`); stabilność (PR 3) go odejmie.

## Rozważane alternatywy

- **Ustroje w kodzie** (`enum` z wartościami): szybciej, ale łamie zasadę „zawartość w danych” i blokuje ustroje kolejnych er.
- **Opóźnienie w turach** (jak w GDD): w erach 1–2 decyzja czekałaby 50–200 lat.
- **Wojna wchodzi w życie dopiero po opóźnieniu** (najpierw zapowiedź): uczciwsze dla ofiary, ale potrzebuje nowego stanu
  „zapowiedzianej wojny” w dyplomacji; obecne rozwiązanie daje ofierze ten sam czas na przygotowanie.
- **Tytuł zapisany w stanie:** prościej czytać, ale tytuł trzeba by zmieniać przy każdej zmianie nazwy i ustroju.

## Konsekwencje

- Hash gry się zmienia (inne wagi celów, ostrożność i rytm przeglądów); test determinizmu i złoty hash świata zaktualizowane.
- Nowe ustroje nie wymagają kodu; nowe skutki ustroju (stabilność, administracja) wymagają kodu raz, w PR 2–4.
- Despotia i republika miejska czekają na technologie epoki brązu; sprawdzają je testy.
