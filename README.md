# CivSim 2

A turn-based 4X economic development simulator in pixel art. Civilizations start as nomadic tribes on a hex-grid
globe and, if they survive, reach the space age. Every decision an AI
civilization makes comes from its situation: its neighbours, its borders, its resources, its memory and
its leader.

The simulation comes first. Every civilization is run by AI on the same rules a player will use later.

> There was never a CivSim 1. Don't ask.

## Status

Early development: **Stage 0 (foundations)**. Repository skeleton and Godot project are in place.

## Project plan

The plan covers only features marked **CORE** in the design document (`docs/GDD.md`, chapter 16).
Simulation first: Stages 0–12 build the simulator for all eras with a developer viewer (placeholder art from Stage 5); pixel art and the
player interface arrive last, in Stage 13. A stage is finished only when its "done when" check passes.
Full task lists (Polish): [`ROADMAP.md`](ROADMAP.md).

| Stage | Goal | Done when |
| --- | --- | --- |
| 0. Foundations | .NET solution, deterministic random numbers, command queue, empty turn loop, client–server boundary, save/load, headless runner, CI | 10,000 empty turns run without a window, the same seed gives the same state hash, CI is green on Windows, Linux and macOS |
| 1. Map | Hex grid on a globe (wraps east–west, crosses the poles), world generator (continents, climate, biomes, rivers, deposits, start positions), PNG map export | 100 maps on different seeds generate without validator errors and have been reviewed as PNGs |
| 2. Simulation viewer | Godot viewer connected to the local server: camera, zoom, map overlays, tile inspector, speed controls, chronicle, charts | The viewer runs at 60 FPS on the huge map while the simulation runs |
| 3. Economy and population (eras 1–2) | Resources, storage, trade network, population layers, nomads and settlements, migrations, technologies, scouting and fog of war, events (drought, fire, flood, epidemic) | Tribes gather, migrate, settle and grow on their own for 2,000 turns without collapsing |
| 4. AI and diplomacy | AI advisors, utility-based decisions, leader personalities, contact, treaties, wars, governments, stability, revolts, "Why?" panel | 100 AI-vs-AI games produce sensible histories. **Milestone: simulator demo** |
| 5. Better viewer | Start menu with world and simulation settings, layered map view with toggleable semi-transparent layers, placeholder art | The world is set up in the menu and several layers are visible at once |
| 6–12. Eras 3–9 | One era per stage: data, mechanics, AI, developer viewer, balance across 100 games | Each era is balanced in batch tests |
| 13. Player mode and art | Pixel art for all eras, UI theme, player orders, delegation to AI, tech tree, victory conditions | A full game can be played from the stone age to the information age. **Milestone: playable game** |

After CORE: optional modules such as online multiplayer, the space age and other worlds, physical logistics,
money and budgets, sieges and animations. Their order will be decided once CORE is complete.

Not in the game at all: culture and religion as mechanics.

## Getting started

Install Git, the .NET 10 SDK and Godot 4.7.2 (.NET edition), then open `game/project.godot` in Godot.
Step-by-step guide (Polish): [`docs/SETUP.md`](docs/SETUP.md).

## Tech

- Simulation core: C# / .NET 10 (LTS), no engine dependencies, deterministic, runs headless
- Client–server from day one: authoritative server (also runs locally in single player), Godot client
- Viewer and game: Godot 4.7.2 (.NET)
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
