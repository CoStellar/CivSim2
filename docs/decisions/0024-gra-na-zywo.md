# ADR 0024: Gra na żywo między RTS-em a strategią: gracz ponad światem

**Status:** przyjęta, 2026-11-09 (decyzje Patryka; dokument „Wizja gry: CivSim 2 na żywo — propozycja” w Claude Docs).
Zmienia GDD rozdz. 1 (wizja, „czego nie robimy”), 6 (technologie), 13 (tryby i koniec gry) i 14 (kolejność prac).
Wdrożenie: etapy 6–8 w `ROADMAP.md`.

## Kontekst

Po etapie 5b symulacja działa stabilnie (tura ok. 12 ms), ale wygląda „bez sensu”: w danych są 2 ery z 10, świat staje po neolicie,
a czas liczy się w latach historii (krok cywilizacji 25–50 lat). GDD zakładał turową grę 4X na ok. 10 h i tryb gracza dopiero
na końcu CORE (etap 13). Patryk chce gry, którą da się ograć: na żywo, krótszej, z wpływem gracza na rozwój i z innymi graczami.

Research (Civilization IV, Rise of Nations, Empire Earth, Spore, gry Paradoxu, Millennia, The Universim) pokazał, że gry nie
symulują historii w czasie gracza: długość ery zależy od liczby ciekawych decyzji, a lata są podpisem.

## Decyzja

**Kształt gry.** Gra na żywo, między klasycznym RTS-em a strategią czasu rzeczywistego z pauzą. Gracz to byt ponad światem:
rządzi całą cywilizacją i steruje jej jednostkami. Czego gracz nie zdąży zrobić, robi za niego AI (te same doradcy, co
u cywilizacji komputerowych, filar 1).

**Co robi gracz:**
- steruje oddziałami w ruchu jak w RTS-ie: osadnicy, wojownicy, zwiadowcy, tragarze z jedzeniem (później łodzie i inne);
- zleca **zadania na obszarze**: wydobycie surowca, pola, łowiska, a potem budowy w danym miejscu (mur, zbrojownia);
  ludzie z osady sami tam chodzą, gracz nie klika pojedynczych pracowników;
- zakłada miasta i awansuje je, buduje fortyfikacje, uzbraja się;
- wybiera kierunek rozwoju ludu (np. góry i kopanie, morze i łodzie), który z czasem owocuje (GDD 6.4);
- odpowiada na **karty decyzji**: ważna sprawa z rekomendacją doradcy i przyczyną; licznik ok. 20 s, potem decyduje AI.
  Nie ma sztywnego celu liczby decyzji na minutę.

**Start partii.** Każda cywilizacja zaczyna od zera w neolicie, w innych warunkach, bo teren jest inny. Epoka kamienia to tylko
„historia plemienia” (opis), bez rozgrywki i bez generowania historii świata przed grą.
- Kolejność wyboru jest losowana; gracz może zamiast tego wybrać, że idzie pierwszy.
- Wybierający przez ok. 5 s widzi mapę, potem już tylko ląd jako szare bloki; ma 10–15 s na wybór miejsca z pamięci.
- Wokół wybranego miejsca powstaje losowa strefa blokady: kolejne cywilizacje mogą stanąć blisko, ale nie za blisko.
- AI wybiera w swojej kolejce według oceny terenu doradcy osiedlenia, z losowym błędem.

**Rozwój i ery.** Nie ma budżetu minut na erę. Każda cywilizacja przechodzi do kolejnej ery we własnym tempie, zależnym od
terenu i umiejętności gracza; ktoś może być w brązie, gdy inny jest już w średniowieczu. Z każdą erą zmienia się tempo gry
i to, na czym gracz się skupia. W pierwszych erach nie ma postaci z imieniem (przywódca żyłby kilka sekund).

**Technologie (GDD 6.4):**
- technologia ma stały koszt; im lepiej rozwinięta (bardziej doświadczona) technologia poprzedzająca, tym taniej;
- **doświadczenie w technologii** rośnie od używania i wzmacnia jej efekt (rolnictwo: plony, łodzie: siła);
- kierunek rozwoju zwiększa szansę na pokrewne odkrycia (góry → rzemiosło i rudy, rybołówstwo → lepsze łodzie);
- technologię można rozwijać albo tylko „zaliczyć”, jeśli jest potrzebna do nowej ery;
- **podejrzenie technologii** przez kontakt (zwiadowca, kupiec, sąsiedztwo): 5% zniżki za każdą cywilizację, która już ją zna,
  do 50%; przodujący płaci najwięcej, nikt nie jest „niesiony”;
- koszt = koszt bazowy × (1 − zniżka za podgląd) × (1 − zniżka za rozwinięcie poprzedniej).

**Koniec partii.** Cel ustalany na starcie na pasku postępu: pierwszy metal, pierwsza elektryczność, lot w kosmos albo podbój
obszaru na innej planecie. W pierwszej wersji dla graczy (do epoki brązu) celem jest odkrycie wszystkich technologii brązu.
Tempo AI (poziomy trudności): dobre ok. 15 min, średnie 20–25 min, słabe 30–40 min do tego celu.

**Widzowie.** Gracz, którego lud upadł, zostaje widzem i widzi wszystkie cywilizacje graczy (podgląd jak dziś w trybie obserwatora).

**Tryby i rozmiar.** Najpierw gra solo: ta sama gra z większą liczbą graczy AI. Gra w sieci później, na tej samej architekturze
(ADR 0008, 0010). Pauza w singlu zawsze, prędkości ×1/×2/×4. Na start mapa standard i 6–8 cywilizacji.

**Zakres.** Ery pogłębiamy zamiast je ciąć; mogą przybywać z czasem. Kosmos i późne ery później.

**Kolejność prac.** Najpierw backend gry na żywo i sprawdzenie, czy działa tak, jak chce Patryk. Grywalne demo dopiero po erze brązu.

## Rozważane alternatywy

- **Klasyczny RTS (Age of Empires):** pojedyncze jednostki i szybkość rąk; ludność jako grupy, gospodarka i doradcy straciliby
  sens. Wzięliśmy z niego sterowanie oddziałami, nie mikrozarządzanie.
- **Strategia na żywo z budżetem minut na erę i historią świata generowaną przed grą** (pierwsza propozycja z 9.11): odrzucone;
  Patryk chce startu od zera i tempa zależnego od gracza.
- **Tury, tylko szybsze (Civilization):** nie daje gry na żywo z innymi graczami.

## Konsekwencje

- GDD rozdz. 1 przestaje zakazywać czasu rzeczywistego: walkę nadal liczy symulacja (nie zręczność), ale toczy się na żywo.
- Tryb gracza nie jest już ostatnim etapem CORE; demo po erze brązu.
- Model czasu zmienia się w ADR 0025.
- Ustroje, przywódcy, stabilność i wojna z etapu 4 zostają, ale część zachowań (np. przywódca co turę) przestaje mieć sens
  w pierwszych erach i będzie przejrzana przy wdrożeniu.
