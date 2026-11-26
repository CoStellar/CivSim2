# CivSim 2

A real-time strategy game on a hex-grid planet: "Civilization 6 in real time". The player is the whole civilization and decides
everything: where to settle, what to build on which hex, what to research, where the army marches and whom to make peace with.
AI peoples play by the same rules with the same commands.

> There was never a CivSim 1. Don't ask.

## Status

Sprint to a playable demo up to the Middle Ages. Tasks and state (Polish): [`ROADMAP.md`](ROADMAP.md).
Game design (Polish): [`docs/projekt-gry.md`](docs/projekt-gry.md); rules with numbers: [`docs/sim/`](docs/sim/).

## Getting started

Setup from an empty computer (Polish): [`docs/SETUP.md`](docs/SETUP.md). In short: .NET SDK 10 and Godot 4.7.2 .NET, then open `game/`
in Godot, or run the tools:

```
dotnet build -c Release
dotnet test tests/CivSim.Sim.Tests -c Release          # the new game
dotnet test tests/CivSim.Tests -c Release              # protocol, server, data, map and world generation
dotnet test tests/CivSim.Legacy.Tests -c Release       # the legacy game (ADR 0029)
dotnet run --project src/CivSim.Headless -c Release -- --sim-report --seeds 12
```

## Layout

| Folder | What |
| --- | --- |
| `src/CivSim.Sim` | the game simulation (no Godot) |
| `src/CivSim.Core` | map, world generation, numbers, random numbers, hashing, names |
| `src/CivSim.Protocol`, `src/CivSim.Server` | client–server messages and the authoritative game server |
| `src/CivSim.Headless` | headless runs and measurements |
| `game/` | Godot client |
| `data/sim/` | all game data, test scenarios |
| `tests/` | tests |
| `docs/` | design and rules (Polish); old material in `docs/legacy/` |

Not in the game at all: culture and religion as mechanics.
