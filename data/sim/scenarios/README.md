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
| `noPeace` | nobody can propose peace (battles to the end; the battle scenarios use it) |
| `field`, `fieldGap` | two peoples whose armies start on a field near the first settlement of the first, in formation facing each other `fieldGap` hexes apart (default 4); scouts of the first stand two hexes ahead of its line |
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
| `unitPeople` | size of every army unit it starts with (within the kind's 5–30; default: the kind's usual size) |
| `wallRing` | a finished stone wall on every hex of its land this far from its centre (for example 2; 0 = none) |
| `weapon`, `armour` | levels 0–4 of its starting soldiers' arms; without them they take the best the stock holds |
| `settlements`, `settlementPeople` | more settlements founded around the first, and their people (each with the start stock) |
| `aggression`, `expansion` | AI character 0–100 |
| `ai` | played by the AI (default: everyone but people 0) |

Keys are those of `data/sim/game.json` (eras, technologies, goods, buildings, units, settlement levels).

Battle scenarios (2026-11-19): `06_battle_field` (open field, equal arms: selecting, formations, stances, flank, zone of
control, leaving), `07_battle_arms` (refit, sizes, weapons only, split, how much the arms count), `08_battle_cavalry` (charges,
riders breaking away, archers standing, height), `09_battle_ai` (all of it against an attacking AI). In the first three the
enemy stands still (no AI).

War scenario (2026-11-20): `10_siege_supply`: march on a far enemy town that does not attack; provisions in the field, food
caravans to units, the blockade, guard and fighting residents, then take, sack or raze.
