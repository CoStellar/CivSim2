# ADR 0026: Prototyp sterowania i tempo przed nowymi mechanikami; mniej systemów do epoki brązu

**Status:** przyjęta, 2026-11-13 (decyzja Patryka: „zapisz to wszystko”; dokument „Przegląd planu CivSim 2 z poziomu gracza — propozycja”
w Claude Docs). Uzupełnia ADR 0024 i 0025. Zmienia GDD 1.1, 4.4, 6.3, 6.4, 7.1, 13.2 i `ROADMAP.md` (etapy 6–8). Plan: `docs/prompts/10-plan-gry.md`.

## Kontekst

Pomiar 15 minut dzisiejszej symulacji (9000 taktów, mapa standard, 8 cywilizacji) oczami gracza: 4 minuty bez technologii i bez
niczego do zrobienia, potem wykładniczy wybuch do ok. 25 osad na cywilizację i 20–50 tys. ludzi, 8 → 18 cywilizacji przez secesje,
trzy martwe starty z ośmiu, 1 bitwa. Plan etapu 6 kończył się bramą „Patryk ogląda CSV i podgląd obserwatora”, a pierwsza rozgrywka
miała być w etapie 8. Treści w danych jest mało (14 technologii), ale systemów dużo (klimat z gwiazdą, 4 ustroje ze stabilnością,
warstwy z wpływem politycznym, pamięć krzywd, pakty, sieci handlowe, secesje), a gracz w partii na 15–40 minut nie widzi prawie żadnego.

## Decyzja

1. **Prototyp sterowania** (PR P, `feat/play-civ`) zaraz po zadaniach 4a: w podglądzie deweloperskim gracz prowadzi jedną cywilizację
   przez rolę `Player` i rozkazy protokołu; AI robi resztę. Każdy kolejny PR etapu 6 ma punkt „jak sprawdzić ręcznie w prototypie”.
   Brama etapu 6: Patryk gra 15 minut i ocenia.
2. **Tempo i skala** (PR T, `feat/pace`) przed startem, technologiami i kartami: raport `--milestones` na 10 seedach; cele: pierwsza
   technologia ≤ 1 min, wioska ≤ 3 min, era 1 ≤ 5 min, 3–6 osad w 15. minucie, żadna cywilizacja < 100 ludzi po 5 min; gwarancja startu;
   mapa mała (ok. 128×80) z 4–6 cywilizacjami jako domyślna dla partii do brązu.
3. **Zasady uproszczone:** podgląd technologii tylko z kontaktu i spoza własnej secesji, limit 50%; secesja jako karta decyzji, najwyżej
   raz na 5 minut, limit cywilizacji liczy żywe; karty tylko ze zdarzeń z `cards.json`, najwyżej jedna na 30 s, licznik 20 s czasu gry;
   start bez „z pamięci” (15 s z mapą bez złóż i żyzności); bez „zaliczania” technologii; dwa przełączniki delegowania zamiast pięciu
   poziomów; pasek pokazuje erę i minutę partii, lata tylko w kronice; jeden ustrój do brązu.
4. **Każda technologia odblokowuje jedną widoczną rzecz** (zadanie, oddział, budowla, grafika osady); mnożniki są dodatkiem. Tkactwo
   i pieczenie chleba wycięte do brązu.
5. **Zamrożone do brązu** (flaga w danych, kod zostaje): klimat dynamiczny, ustroje i stabilność, wpływ polityczny warstw, pakty i sojusze,
   sieci handlowe z cenami, dryf dalekich osad, pożar i powódź, odbudowa ruin, wiele światów.
6. **Rozwój widoczny:** grafika osady zmienia się z technologią, pasek postępu każdej cywilizacji, kamienie milowe w kronice z minutą
   partii (PR P); jedna liczba na osadę, „dlaczego” przy zdarzeniach, ekran końca partii (etap 8).
7. Kolejność po PR T: 4b, 5, 6, 7, 4c, 4d, 8. Etap 7 zaczyna się od „wioska rośnie w miasto, większa wciela mniejszą”.

## Rozważane alternatywy

- **Zostawić plan: tempo w etapie 7, gra w etapie 8.** Odrzucone: błędy w „czuciu” gry wyszłyby po 10+ PR-ach, a strojenie tempa
  zmienia założenia startu, technologii i kart.
- **Usunąć zamrożone systemy z repo.** Odrzucone: kod i dane są gotowe na późniejsze ery; flaga kosztuje mniej niż przywracanie.
- **Budżet minut na erę** (odrzucony już w ADR 0024): tempo dalej zależy od gracza i terenu, ale ma mierzone cele.

## Konsekwencje

- Nowy plik `docs/prompts/10-plan-gry.md` z kolejnością i instrukcjami; `09` zostaje dla T1–T14.
- Headless dostaje `--milestones`; każdy PR zmieniający dane dołącza raport z 10 seedów.
- Zamrożone systemy nie liczą się w pulsie; ich testy ładują dane z flagą włączoną.
- `technologies.json` dostaje pole `unlocks.visible`, wymagane przez loader.
