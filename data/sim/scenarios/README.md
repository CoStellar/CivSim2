# Test scenarios

Each `*.json` here is a game that starts at a chosen moment of development (Patryk, 2026-11-19). They show up in the start
menu under "Test scenario"; from the command line: `--play --play-scenario <file name without .json>`.

Fields (all optional):

| Field | Meaning |
| --- | --- |
| `name`, `description` | shown in the menu |
| `seed`, `width`, `height`, `peoples` | the world (width and height of the legacy map; the game map is denser) |
| `revealMap` | everyone knows the whole map (and so has met everyone) |
| `debug` | the debug keys work (default true): F5 +200 of every good, F6 finish the research, F7 reveal the map, F8 +10 people, F9 next era |
| `wars` | pairs of peoples at war from the start, e.g. `[[0, 1]]` |
| `civs` | one entry per people, in order; people 0 is the player |

Per people:

| Field | Meaning |
| --- | --- |
| `era` | era key: every technology of the eras before is known, with the experience and goods made that later ones ask for |
| `techs` | more technologies known (keys) |
| `level` | level of the first settlement (`camp`, `village`, `town`, `city`) |
| `people` | people of the first settlement |
| `stock` | goods in the first settlement (key: amount) |
| `buildings` | finished buildings in the first settlement (key: count), on the first hexes where they may stand |
| `units` | units ready at the first settlement (key: count) |
| `settlements`, `settlementPeople` | more settlements founded around the first, and their people |
| `aggression`, `expansion` | AI character 0–100 |
| `ai` | played by the AI (default: everyone but people 0) |

Keys are those of `data/sim/game.json` (eras, technologies, goods, buildings, units, settlement levels).
