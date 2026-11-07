# Etap 4e: wojna — plan

Decyzje Patryka z 2026-11-07 (dokument „Etap 4e: wojna — propozycja” w Claude Docs). Zasady: GDD 9.3, ADR 0021. Etap w `ROADMAP.md`.

## Decyzje

| # | Pytanie | Decyzja |
| --- | --- | --- |
| 1 | Czym walczą ludzie przed kowalami? | Uzbrojenie, na jakie lud stać, z technologii: drewniane włócznie, kamienne groty, proce („jak Dawid”). Broń z metalu z kowalami w erze 3. |
| 2 | Jedno pole na wyprawę? | Tak w erach 1–2, ale mechanika gotowa na przyszłe ery (front na kilku polach z danych er). |
| 3 | Ilu wojowników wychodzi? | Dowódca decyduje, 1/3 zostaje w domu. Dowódca nie rusza na wojnę, którą na pewno przegra: liczy szanse z tego, co widzi. |
| 4 | Pokonani w osadzie? | Podbój: wioska przechodzi z ludźmi. Niewolnictwo i jeńcy w przyszłości. |
| 5 | Cele i okup? | Ziemia, łupy, odwet; jednorazowy okup. Prosto. |
| 6 | Zmęczenie i nastrój? | Tak. Do tego łańcuch dostaw: tragarze z okolicznych wiosek najeźdźcy donoszą jedzenie trasą wojska; armia bez jedzenia traci morale, walczy słabiej i napada okoliczne wioski. |
| 7 | Otwarte z 4F | Wioski +5‰ urodzeń (PR #47); wymogi rolników bez zmian. |
| — | Prośba | Moduł wojny aktualizować co erę: przypomnienie w `CLAUDE.md`, `ROADMAP.md`, `docs/systems/war.md` („Co erę”). |

## Uzbrojenie er 1–2 (pierwsze liczby do strojenia)

| Poziom | Technologia | Zużywa (na wojownika na wyprawę) | Siła |
| --- | --- | --- | ---: |
| gołe ręce i kije | — | — | 50% |
| drewniane włócznie hartowane w ogniu | ogień | drewno 0,2 | 70% |
| proce | garbarstwo | skóry 0,1 | 80% |
| włócznie i topory z kamiennym grotem | obróbka krzemienia | narzędzia kamienne 0,2 | 100% |

Wojownik bierze najlepszy poziom, na który osada ma dobra. Era 3: brąz (kowal) ok. 150%, era 4: żelazo ok. 200%.

## Zmiana z 2026-11-07: epizody (ADR 0022)

Wojny w PR #48 trwały 150–300 lat, bo walka liczyła się w krokach ery (50 lat). Patryk przyjął propozycję „Czas w grze: długie kroki
i krótkie zdarzenia”: kampania to epizod z krokiem co miesiąc; zegar świata zatrzymuje się na nim, ale liczy tylko epizod.
Wszystkie rekomendacje przyjęte: krok kampanii = miesiąc; obóz = obszar sezonowych wędrówek (bez epizodów); wojna bez kampanii
wygasa po 5 latach; susza, głód i epidemia jako epizody po etapie 4e; #48 do scalenia bez zmian.

## PR-y

1. **Cel, wynik, zmęczenie, pokój z warunkami** (`feat/war-goals`, #48). Zrobione.
2. **Epizody i pierwsza kampania** (`feat/war-episodes`).
   - Stan: `EpisodeBook` w `GameState` (rodzaj, uczestnicy, start, następny krok, krok w miesiącach, dane kampanii: osada wyjścia, cel, wojownicy, pole, droga, faza, łupy).
   - Zegar: `WorldClock.Begin` bierze najbliższy krok cywilizacji albo epizodu; `GameState.WorldMonths` = miesiące do następnego kroku cywilizacji w kroku pełnym, 0 w kroku epizodu; systemy świata liczą `WorldMonths` i nic w kroku epizodu.
   - `CampaignSystem` (faza ruchu i walki): marsz `marchCellsPerMonth` pól na miesiąc po znanych polach, walka u celu (na razie szansa jak przy napaści: udział siły), łupy (ile uniosą wojownicy), powrót, koniec.
   - Komenda `LaunchCampaignCommand` (osada, cel, ilu wojowników). AI: prosty wybór kampanii w czasie wojny, gdy szansa z widzianej siły ≥ progu z `data/ai.json`.
   - Wojna bez kampanii wygasa po `quietPeaceYears` (5) białym pokojem; zmęczenie = miesiące kampanii × `wearinessPerCampaignMonth` + straty × `wearinessPerLossPercent`.
   - Zapis, hash, migawka (wyprawy w podglądzie jak zwiadowcy), `--wars` z liczbą kampanii.
3. **Uzbrojenie i wyprawy z wielu osad** (`feat/war-armies`): `armaments` w danych; zbiórka z wielu osad (1/3 zostaje); dane er: `armyLimit`, `troopsPerCell`; tragarze; głód wyprawy i napady na wioski.
4. **Bitwa i podbój** (`feat/war-battles`): rundy (strumień `combat`), teren, morale, najedzenie, ucieczka; podbój wioski i ucieczka z obozu.
5. **Dowódca AI** (`feat/war-commander`): spodziewany wynik bitwy bez losowania, z tego, co widzi; decyzje co miesiąc kampanii; `DeadValueTests`; `docs/ai/`.
6. **Strojenie i statystyki** (`feat/war-tuning`).

## Pułapki

- Determinizm: bitwy tylko na strumieniu `combat`, iteracja po id, liczby całkowite (procenty, tysięczne).
- AI widzi armie wroga tylko na polach widzianych w ostatnim kroku (`MapMemory`), siłę z tego, co widzi; bez premii.
- Zero alokacji w pętli tury: bufory wielokrotnego użytku (wzór: `PathFinder`, `TradeSystem`).
- Komendy: każda zmiana stanu przez `ICommand`; armie dostają własne komendy (zbiórka, marsz, atak, odwrót).
