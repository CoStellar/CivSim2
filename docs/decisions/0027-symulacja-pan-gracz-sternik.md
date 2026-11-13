# ADR 0027: Symulacja jest panem, gracz sternikiem

**Status:** przyjęta, 2026-11-13 (decyzja Patryka: „niech symulacja będzie panem, a gracz sternikiem; będziemy się tego trzymać przez cały
rozwój cywilizacji”). Dokument: „CivSim 2: rozgrywka sternika — zasady na wszystkie ery” w Claude Docs
(https://claude.ai/artifact/RfXQsHCry1K2wCsJLkFcUr). Zmienia GDD 1.1 i dodaje GDD 1.2. Uzupełnia ADR 0024 (gra na żywo) i 0026.

## Kontekst

Wierne pokazanie rozwoju cywilizacji przez wieki i sterowanie nim przez gracza ciągną w przeciwne strony: pierwsze wymaga wielu
agentów, wolnych procesów i przyczynowości, drugie tempa, sprawczości i czytelności. Gry, które to pogodziły, wybrały pana i służącą:
Civilization i Rise of Nations mają grę jako pana, a historię jako kostium; Crusader Kings i Dwarf Fortress mają symulację jako pana,
a gracz jest sternikiem z kilkoma mocnymi dźwigniami. ADR 0024 („gracz steruje oddziałami jak w RTS-ie”) ciągnął w stronę operatora,
a cały dotychczasowy fundament projektu (filar 2, AI, która sama sensownie decyduje) ma wartość w wariancie sternika.

## Decyzja

1. **Symulacja jest panem, gracz sternikiem.** Świat rozwija się sam, według tych samych zasad dla wszystkich; gracz wybiera kierunek
   i patrzy, jak jego wybory odbijają się w historii ludu.
2. **Pięć dźwigni gracza, na wszystkie ery:** gdzie żyjemy; dokąd zmierzamy; co robimy na ziemi; z kim i jak; odpowiedzi na karty.
   Era dodaje opcje do dźwigni, nigdy nowy rodzaj. Sterowanie oddziałami jest narzędziem dźwigni 1 i 4, nie filarem.
3. **Gracz, który nie klika, ma dalej ciekawą partię.** Każdą dźwignię można wziąć i oddać AI w dowolnej chwili (pięć kafelków
   „biorę / oddaję” w panelu doradców).
4. **Każda decyzja ma skutek widoczny w 2–3 minuty, a kronika mówi dlaczego.** Co tego nie spełnia, nie jest dźwignią gracza.
5. **Wierność przyczynom, nie latom.** Lata są podpisem w kronice.
6. **Test sternika** (pięć pytań z GDD 1.2) obowiązuje każdą mechanikę, erę i PR; `docs/systems/` ma sekcję „Test sternika”.
7. Każda nowa era zaczyna się od wiersza tabeli er z GDD 1.2 (co nowego w dźwigniach, które systemy odmrozić), a dopiero potem od
   technologii i danych.

## Rozważane alternatywy

- **Gra jako pan, symulacja jako kostium** (Rise of Nations): odrzucone; wymagałoby cięcia ludności, fachów i doradców, czyli tego,
  co jest zbudowane i działa.
- **Operator oddziałów** (Age of Empires): odrzucone; przy grupach ludzi i gospodarce prowadzonej przez doradców szybkość rąk nie ma celu.
- **Obie rzeczy w pełni naraz:** za wolne dla gracza i za szarpane dla symulacji.

## Konsekwencje

- GDD 1.1 opisuje gracza jako sternika; nowy GDD 1.2 z pętlą, dźwigniami, przebiegiem partii, tabelą er, ekranem i testem sternika.
- PR P (prototyp) buduje kafelki dźwigni z „biorę / oddaję”, nie tylko rozkazy dla oddziałów; test automatyczny: 15 minut z rolą gracza
  bez rozkazów daje te same kamienie milowe co AI.
- Zadania zleca gracz tylko w osadach, które wziął w ręce; uczony proponuje trzy technologie; prawa i ustroje wchodzą wyłącznie kartami.
- Multiplayer (etap 9) nie wymaga innej gry: rozłączonego prowadzi AI, która i tak prowadzi wszystko, czego gracz nie wziął.
