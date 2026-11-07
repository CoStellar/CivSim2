# ADR 0022: Epizody — krótki zegar dla zdarzeń z przebiegiem

**Status:** przyjęta, 2026-11-07 (Patryk przyjął wszystkie rekomendacje z dokumentu „Czas w grze: długie kroki i krótkie zdarzenia —
propozycja”). Uzupełnia ADR 0018 (zegar świata). Zasady w GDD 6.2, 5.4 i 9.3.

## Kontekst

Krok ery trwa 50 lat w epoce kamienia i 25 w neolicie. Dla urodzeń, zbierania, plonów, handlu, nauki i klimatu to dobre przybliżenie:
same są średnią z wielu lat. Zdarzenia z przebiegiem psują się jednak w takim kroku. Po PR #48 wojny w 3 grach trwały 150–300 lat,
a zmęczenie wojną rosło głównie z lat. Reakcja na napaść przychodziła w następnym kroku świata, w epoce kamienia nawet po 50 latach.

## Decyzja

- **Dwa zegary.** Powolne procesy zostają na krokach ery (ADR 0018). Zdarzenia z przebiegiem dostają **epizody**: byt w stanie gry
  z rodzajem, uczestnikami, miesiącem startu, krokiem w miesiącach (wojna: 1) i końcem.
- **Zegar świata** skacze do najbliższego miesiąca, w którym ktoś ma krok: cywilizacja (krok ery) albo epizod (krok epizodu).
  - **Krok pełny:** ma go co najmniej jedna cywilizacja. Świat liczy klimat, odrastanie, strefy i spory za czas do następnego
    kroku cywilizacji (`GameState.WorldMonths`).
  - **Krok epizodu:** żadna cywilizacja nie ma kroku. Świat nie liczy nic (`WorldMonths` = 0), liczą się tylko epizody i decyzje
    reaktywne (odpowiedzi na propozycje, alarmy).
  - Czas świata nie ginie: krok pełny zawsze obejmuje miesiące aż do następnego kroku cywilizacji, a kroki epizodów leżą w środku.
- **Pierwszy epizod: kampania wojenna** (PR 2 etapu 4e). Wyprawa wojowników wychodzi z osady, idzie po mapie kilka pól na miesiąc,
  walczy u celu i wraca z łupami. Następne PR-y dodają uzbrojenie z technologii, wyprawy z wielu osad, tragarzy, bitwę w rundach
  i dowódcę AI.
- **Wojna:** stan wojny bez kampanii wygasa po 5 latach białym pokojem. Zmęczenie rośnie z miesięcy kampanii i ze strat, a nie z lat kalendarza.
- **Ruch obozu** zostaje w krokach ery: obóz to obszar sezonowych wędrówek, a przeprowadzka to przesunięcie całego obszaru (GDD 5.4).
- **Później:** susza, głód, epidemia i wyprawy odkrywców jako epizody, w osobnym PR po etapie 4e.

## Rozważane alternatywy

- **Krótsze kroki er** (np. 5 lat w epoce kamienia): gra ok. 10 razy wolniejsza, a wojna i tak trwa 5 lat.
- **Wszystko co rok:** ok. 7000 kroków po ok. 40 ms tylko do neolitu (ok. 5 minut na grę), a bitwy trwają krócej niż rok.
- **Wojna statystycznie** (natężenie konfliktu w kroku 50 lat): szybkie, ale bez wypraw, zaopatrzenia, dowódcy i podboju.

## Konsekwencje

- Kroków świata jest więcej (szacunek: 50–300 na grę przy 2–11 wojnach). Krok epizodu musi być tani: systemy świata nie liczą się w nim wcale.
- Podgląd dostaje migawkę co miesiąc kampanii (gra „zwalnia” na wojnie).
- Format zapisu: epizody, `WorldMonths`. Hash obejmuje epizody.
- „Tura” w CSV Headless to dalej krok świata; kroki epizodów też są turami.
