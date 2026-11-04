# CivSim 2

A turn-based 4X economic development simulator in pixel art. Civilizations start as nomadic tribes on a hex-grid
planet that wraps in every direction and, if they survive, reach the space age. Every decision an AI
civilization makes comes from its situation: its neighbours, its borders, its resources, its memory and
its leader.

The simulation comes first. Every civilization is run by AI on the same rules a player will use later.

> There was never a CivSim 1. Don't ask.

## Status

Early development. See [`ROADMAP.md`](ROADMAP.md).

## Tech

- Simulation core: C# / .NET, no engine dependencies, deterministic, runs headless
- Client–server from day one: authoritative server (also runs locally in single player), Godot client
- Viewer and game: Godot 4 (.NET)
- Content: JSON data files in `data/`
- Platforms: Windows, Linux, macOS

## Repository layout

| Path | What lives there |
| --- | --- |
| `src/` | Simulation core, data loading, headless runner |
| `tests/` | Unit, determinism and long-run tests |
| `game/` | Godot project |
| `data/` | Game content (technologies, resources, events…) |
| `art/` | Pixel art, palette, templates |
| `docs/` | Design document (Polish), decisions, system docs |
