# Etap 4f: państwo — plan

Propozycja dla Patryka z 2026-11-07: dokument „Etap 4f: państwo — propozycja” w Claude Docs
(https://claude.ai/artifact/MMaSwUrRaWgSDmoXUrPCGn). Patryk wybrał 2026-11-07 „najpierw 4f i 4g, potem nowe ery”. Zasady: GDD 7.2,
7.3, 7.5, 5.6; ADR 0023. Etap w `ROADMAP.md`.

## Decyzje (Patryk 2026-11-07: „Z twoimi rekomendacjami idź dalej”, wszystkie przyjęte; GDD 7.2, 7.3 i 7.5 zaktualizowane)

| # | Pytanie | Decyzja |
| --- | --- | --- |
| 1 | Które ustroje teraz? | Rada starszych i wodzostwo od razu; despotia i republika miejska w danych od epoki brązu. |
| 2 | Ustrój na starcie? | Z charakteru: agresja i ekspansywność → wodzostwo, ostrożność i otwartość → rada starszych. |
| 3 | Opóźnienie w turach czy w latach? | W latach (0, 1, 2, 4); wpisane w GDD 7.2. |
| 4 | Na co działa opóźnienie? | Zmiana ustroju i wypowiedzenie wojny (armie czekają). |
| 5 | Kiedy ustrój się zmienia? | Strateg (rzadko, z histerezą) albo bunt; bezkrólewie 2 lata. |
| 6 | Stolica? | Osada przywódcy: pierwszy obóz, potem największa osada. |
| 7 | Wpływ warstw w stabilności? | Zależny od ustroju; nastrój warstwy = nastrój osady; wpisane w GDD 7.3. |
| 8 | Nowe państwo po secesji? | Nowa cywilizacja: język z mutacją, inny ustrój, przesunięty charakter, kopia technologii i mapy; wojna tylko z decyzji dyplomaty. |
| 9 | Cechy przywódcy? | 6 cech z GDD 7.5, każda przesuwa 1–2 cechy charakteru o 0,2. |

## PR-y

1. **Ustroje** (`feat/state-governments`). Zrobione: `data/governments.json`, `Core.Governance.Governments`, `ChangeGovernmentCommand`,
   `GovernmentSystem` (faza State), `Treaty.ReadyMonth`, tytuł dla obserwatora (protokół 10), zapis 26, `governments:` w Headless,
   `docs/systems/governments.md`, ADR 0023, `docs/ai/`.
2. **Przywódca i sukcesja** (`feat/state-leaders`, zrobione): strumień `Leaders` (już istnieje, dziś losuje charakter), imię z `NameLanguage`, wiek, cechy, śmierć,
   sukcesja według pola ustroju, cechy przywódcy dodawane do charakteru (osobno od cech ludu, żeby zmiana władcy była skokowa).
3. **Stolica, administracja, stabilność:** faza State; koszt drogi przez `HexGlobe` i koszty ruchu biomów; wpływ warstw w danych ustroju;
   stabilność w stanie cywilizacji (zapis, hash, podgląd); niepokoje jako efekt z `data/effects.json`.
4. **Bunty i secesja:** nowe `CivilizationState` w trakcie gry (id = następny wolny, limit 64 z bitów w `DiplomacySystem`), przeniesienie
   osad, relacje i wspomnienie secesji, `NameLanguage.Mutate`, przewrót (nowy przywódca, ustrój, bezkrólewie).
5. **AI państwa i strojenie:** wybór ustroju przez stratega, reakcje na stabilność, pętla odwetu (gaśnięcie urazu po wojnie, koszt
   zerwania rozejmu), `DeadValueTests`, `docs/ai/`, statystyki z 3 seedów.

## Pułapki

- Determinizm: nowe cywilizacje w środku gry muszą dostać id i kolejność iteracji niezależną od słowników; losowanie tylko na strumieniach
  `Leaders` (przywódcy) i nowym strumieniu dla buntów.
- Tablice na cywilizację (np. bitmaski wspólnego wroga, `SeenYear`, tablice per cywilizacja w Headless) muszą rosnąć przy secesji.
- Tytuł liczony z danych, nie zapisywany: kronika używa rdzenia nazwy.
