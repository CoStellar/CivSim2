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

## PR-y

1. **Cel, wynik, zmęczenie, pokój z warunkami** (`feat/war-goals`).
   - `data/war.json`: cele (ziemia, łupy, odwet; ile świadkowie pamiętają napaść), wynik (+ za wygraną walkę, + za zdobytą osadę), zmęczenie (na rok, na procent strat, próg pokoju, nastrój za punkt), okup (procent zapasów na punkt wyniku), próg warunków.
   - `Treaty`: cel, osada celu, wynik, straty i zmęczenie stron. `DeclareWarCommand` z celem. Wspomnienie „napaść bez powodu” (`RelationEvent.Aggression`).
   - `WarSystem` (faza walki): zmęczenie co krok cywilizacji, efekt `war_weariness` (nastrój).
   - Napaści (`ConquerSettlementCommand`) w czasie wojny dodają do wyniku i strat.
   - Pokój: propozycja z warunkami (oddanie osady, okup, biały); `Sign` stosuje warunki.
   - Dyplomata: cel z powodu (zabrana osada → ziemia, napaść → odwet), pokój przy zmęczeniu z warunkami z wyniku, ocena warunków przy odpowiedzi.
   - Zapis (format 22), hash, `--wars` w Headless (CSV: strony, cel, początek, koniec, wynik, warunki, straty).
2. **Uzbrojenie i wyprawy** (`feat/war-armies`): `armaments` w danych; `ArmyState` (wódz, ludzie, uzbrojenie, jedzenie, morale, pole, trasa); dane er: `armyLimit`, `troopsPerCell`; zbiórka z osad (1/3 zostaje), marsz, powrót i rozwiązanie; migawka i warstwa jednostek w podglądzie.
3. **Bitwa, podbój, zaopatrzenie** (`feat/war-battles`): bitwa w rundach (strumień `combat`), teren z biomów i rzeźby, morale, najedzenie, ucieczka; podbój wioski i ucieczka z obozu; tragarze z jedzeniem; głodna armia napada wioski. Wypędzenie obozu w czasie wojny zastępuje bitwa wyprawy.
4. **Dowódca AI** (`feat/war-commander`): spodziewany wynik bitwy bez losowania, z tego, co widzi; próg szansy i inne liczby w `data/ai.json`; zbiórka, cel marszu, atak, czekanie, odwrót, obrona po alarmie; `DeadValueTests`; `docs/ai/`.
5. **Strojenie i statystyki** (`feat/war-tuning`): 3 seedy, wykresy wojen, bitew, strat i zmęczenia; `docs/systems/war.md`.

## Pułapki

- Determinizm: bitwy tylko na strumieniu `combat`, iteracja po id, liczby całkowite (procenty, tysięczne).
- AI widzi armie wroga tylko na polach widzianych w ostatnim kroku (`MapMemory`), siłę z tego, co widzi; bez premii.
- Zero alokacji w pętli tury: bufory wielokrotnego użytku (wzór: `PathFinder`, `TradeSystem`).
- Komendy: każda zmiana stanu przez `ICommand`; armie dostają własne komendy (zbiórka, marsz, atak, odwrót).
