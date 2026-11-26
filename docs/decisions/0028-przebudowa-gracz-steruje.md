# ADR 0028: Przebudowa gry: gracz steruje wszystkim, nowa skala mapy, mało obliczeń w tle

**Status:** przyjęta, 2026-11-17 (decyzje Patryka w rozmowie o pętli rozgrywki). Zastępuje ADR 0024 (w części o sterowaniu),
0026 i 0027. Projekt gry: `docs/projekt-gry.md`. Plan prac: `docs/plan-przebudowy.md`. Zasady wydajności: `docs/wydajnosc.md`.

## Kontekst

Pierwsza próba gry (testy gracza 2026-11-16) i rozmowa po niej pokazały, że gra sternika (pięć dźwigni, AI prowadzi cywilizację gracza,
symulacja wierna przyczynom i latom) nie jest grą, jakiej chce Patryk. Wojna, dźwignie i karty nie miały wspólnego obrazu tego,
co gracz robi minuta po minucie. Symulacja liczy bardzo dużo rzeczy w tle, których gracz nie widzi.

## Decyzja

1. **Gracz jest całą cywilizacją i decyduje o wszystkim** (osady, budowy, badania, wojsko, dyplomacja). Jego cywilizacja nie ma własnej AI:
   sam wykonuje tylko codzienne prace (zbieranie, uprawa, przyrost). Rady daje tylko doradca, którego gracz zatrudni.
2. **Przeciwnik AI to pełny gracz** z tymi samymi komendami co człowiek.
3. **Nowa skala mapy:** cały świat w gęstszych heksach; jeden heks to miejsce na jeden obiekt, osada to zbiór heksów.
   Generacja świata zostaje.
4. **Najpierw gra, potem realizm.** Lata nie są zasadą gry (tylko ozdoba na ekranie); era trwa tyle, ile dobrze się gra.
5. **Jak najmniej obliczeń w tle** (`docs/wydajnosc.md`): liczy się tylko to, co aktywne, w ustalonym rytmie, w sumach, a nie
   po jednym człowieku.
6. **Multiplayer od początku:** wszystkie zmiany stanu przez komendy na serwerze, determinizm zostaje.
7. **Stary kod to legacy** (`docs/legacy.md`): nie rozwijamy go; nowa symulacja powstaje obok, na wspólnym fundamencie, i zastępuje
   go kawałek po kawałku. Stan sprzed przebudowy: gałąź `legacy/pre-rework`.
8. **Cel na ten tydzień:** demo do średniowiecza.

## Konsekwencje

- `docs/GDD.md`, ADR 0024–0027 i `docs/systems/` opisują grę legacy. Nowe zasady są w `docs/projekt-gry.md`.
- Przepada: sternik i dźwignie, karty, ustroje, stabilność, bunty, demografia w tle, czas w latach akcji.
- Zostaje: generacja świata, liczby i losowanie deterministyczne, hasz, zapis, takt, serwer i protokół, klient Godot, system języków,
  mgła wojny, dane w JSON, narzędzia pomiarowe.
