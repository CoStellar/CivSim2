# Kod i dokumenty legacy

Od 2026-11-17 (ADR 0028) gra jest przebudowywana. Stan sprzed przebudowy: gałąź `legacy/pre-rework`.

**Legacy znaczy:** nie rozwijamy, nie naprawiamy (chyba że blokuje nową grę), nie dopisujemy funkcji. Kod zostaje, dopóki nowa
symulacja go nie zastąpi; wtedy usuwamy go w osobnym PR. Nowy kod nie zależy od legacy (wolno korzystać tylko z części „zostaje”).

## Zostaje (wspólny fundament nowej gry)

| Gdzie | Co |
| --- | --- |
| `src/CivSim.Core/WorldGen`, `src/CivSim.Core/Map` | generacja świata, heksy, kula |
| `src/CivSim.Core/Numerics`, `Random`, `Hashing` | liczby całkowite, losowanie deterministyczne, hasz |
| `src/CivSim.Core/Naming` | języki i imiona (myśliciele, doradcy, osady) |
| `src/CivSim.Core/Content` | mechanizm wczytywania danych JSON (pliki danych w dużej części nowe) |
| `src/CivSim.Protocol`, `src/CivSim.Server`, `game/` | serwer, protokół, klient Godot (nowe wiadomości i ekran) |
| `src/CivSim.Headless`, `tools/` | narzędzia pomiarowe (do przerobienia pod nowe zasady) |

## Legacy

| Gdzie | Co | Co zastąpi |
| --- | --- | --- |
| `src/CivSim.Core/Ai` | doradcy, dźwignie, sternik | gracz AI z tymi samymi komendami co człowiek |
| `src/CivSim.Core/Population`, `Economy`, `State` (osady) | demografia, magazyny osad, receptury | osady z heksów, budynki z miejscami pracy |
| `src/CivSim.Core/Episodes`, `Units` | kampanie, oddziały | oddziały na heksach, walka na żywo |
| `src/CivSim.Core/Diplomacy` | dyplomacja z przeglądami co 50 lat, wojna | wojna jako stan, negocjacje, reputacja |
| `src/CivSim.Core/Research` | badania w myślicielach | doświadczenie, koszt w zasobach, czas, myśliciele jako przedmioty |
| `src/CivSim.Core/Governance`, `Events` | ustroje, stabilność, bunty, zdarzenia | wyrzucone; zdarzenia ze wspólnej puli |
| `src/CivSim.Core/Simulation` (pętla tury), `Commands`, `Perception` | pętla taktów i pulsów, komendy legacy, postrzeganie | nowa pętla rytmów; mgła wojny przeniesiona |
| `docs/GDD.md`, `docs/systems/`, `docs/ai/`, `docs/prompts/`, ADR 0024–0027 | opis gry sternika | `docs/projekt-gry.md`, `docs/plan-przebudowy.md`, nowe `docs/sim/` |
