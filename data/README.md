# Game content (data)

Only CORE features are covered here (see `docs/GDD.md`, section 16). All game content lives here as JSON, validated on load. Adding a technology, resource or event
must not require code changes. All names and texts are in English.

Planned files (created stage by stage, see `ROADMAP.md`):

| File | Content | Stage |
| --- | --- | --- |
| `biomes.json` | biomes, movement cost, base yields, colours for the debug viewer | 1 |
| `resources.json` | raw resources and goods, category, spoilage per year, storage class | 1 / 3 |
| `eras.json` | eras, years per turn, entry thresholds, UI theme id | 3 |
| `technologies.json` | technology graph, costs, inspirations, unlocks | 3 |
| `recipes.json` | production recipes (inputs → outputs, workers, time) | 3 |
| `buildings.json` | buildings and tile improvements | 3 |
| `strata.json` | population strata and their needs (3 at start, more per era) | 3 |
| `events.json` | random events: conditions, chances, effects, chronicle text | 3 |
| `effects.json` | effect definitions (damage, mobility, depopulation…) | 3 |
| `governments.json` | governments: decision speed, stability decay | 4 |
| `units.json` | units by era, strength, upkeep, tile capacity | 4 |
| `leader_traits.json` | leader traits and AI weight modifiers | 4 |
| `ui_theme.json` | the single UI theme (9-slice assets, accents) | 5 |

Rates are always defined **per year** and converted by the era's turn length.
