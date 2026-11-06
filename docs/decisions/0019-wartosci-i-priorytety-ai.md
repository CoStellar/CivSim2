# ADR 0019: Wartości i priorytety AI: każda opcja służy celowi, ocena = priorytet × przeszkoda

**Status:** przyjęta, 2026-11-06 (decyzje Patryka: „każda wartość ma na coś wpływać”, „priorytet z położenia, braków i charakteru,
koszt jako przeszkoda”; plan `docs/prompts/04-ai-czas-wartosci-migracje.md`, PR B).

## Kontekst

Przegląd AI (`docs/ai/08-ocena-i-uwagi.md`) pokazał, że część wag celów i cech charakteru niczego nie zmieniała: mnożyła wszystkie
opcje jednej decyzji tą samą liczbą (skarbnik: dobrobyt × handlowość na wszystkich recepturach; uczony: nauka × ciekawość na
wszystkich technologiach; gubernator: waga ekspansji na „zostać” i na przeprowadzce). Waga wpływu nie była używana nigdzie. Uczony
wybierał najtańszą technologię, nie patrząc, co daje. Kilkanaście liczb strojenia było zaszytych w kodzie, a AI liczyła siłę obcych
obozów, których dawno nie widziała.

## Decyzja

- **Każda opcja mówi, któremu celowi służy.** Waga celu i mnożnik charakteru działają wtedy, gdy opcje jednej decyzji służą różnym
  celom. Jedzenie służy bezpieczeństwu, pozostałe towary dobrobytowi (`goalOfFood`, `goalOfGoods` w `data/ai.json`).
- **Ocena = priorytet × przeszkoda.** Uczony: priorytet = co technologia da tej cywilizacji na rok (zbieranie na polach strefy,
  receptury dla jej rzemieślników, efekty według potrzeb, wioski według żyzności), każda część ważona swoim celem i charakterem, plus
  25% technologii, do której prowadzi; przeszkoda = koszt względem najtańszej, łagodzony ciekawością. Wartości w setnych jednostki
  wymiany (`Amount`), na rok, w liczbach całkowitych.
- **Skarbnik koordynuje sieć:** osady wybierają receptury po kolei i widzą popyt i podaż po wyborach poprzednich; własny popyt osady
  nie podbija jej ceny surowca; cena wyrobu uwzględnia rok własnej produkcji; premia dla wyrobów na wymianę, gdy towary płyną do
  partnera.
- **Strateg:** waga nauki z przewagi technologicznej spotkanych, waga wpływu z liczby spotkanych.
- **Wiedza:** obce obozy i wojownicy tylko z pól widzianych w ostatnim własnym kroku (`MapMemory.SeesNow`).
- **Liczby strojenia w danych:** wszystkie progi i współczynniki AI w `data/ai.json` (wersja 3, sekcja `scholar`).
- **Test „martwych wartości”** (`tests/CivSim.Tests/Ai/DeadValueTests.cs`): zmiana każdej cechy, wagi celu i parametru musi zmienić
  co najmniej jedną decyzję w zapisanych sytuacjach z gier. Nowy parametr bez wpływu wywala test.

## Rozważane alternatywy

- **Zostawić mnożniki i dodać losowy szum:** opcje różniłyby się, ale wagi nadal nie znaczyłyby nic konkretnego.
- **Priorytet uczonego z cen w sieci zamiast wartości bazowych:** dokładniejsze, ale ceny skaczą z plonami, a badanie trwa dziesiątki
  lat. Zostawione na później.
- **Test wpływu na długich grach (100 gier na wartość):** pewniejszy, ale trwałby godziny. Zapisane sytuacje dają wynik w kilka sekund.

## Konsekwencje

- Rolnictwo przychodzi wcześniej (pierwszy neolit o ok. 2000–2900 lat wcześniej w 3 seedach), więcej ludzi i żywych cywilizacji na
  końcu, mniej decyzji obozów, o 50–80% mniej wypędzeń (statystyki w `docs/ai/09-statystyki.md`). Tempo można stroić w `data/ai.json`
  (`scholar.villageWorthPerPerson`).
- Przełączanie receptur „praca ↔ brak” prawie znika; zostaje przełączanie odzież ↔ chleb przy skokach ceny jedzenia.
- Nowe wartości kontrolne hashy nie są potrzebne w testach (porównują dwa przebiegi), ale gry z tym samym seedem dają inną historię
  niż przed PR B. Format zapisu bez zmian.
