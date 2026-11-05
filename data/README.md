# Game content (data)

Only CORE features are covered here (see `docs/GDD.md`, section 16). All game content lives here as JSON, validated on load. Adding a technology, resource or event
must not require code changes. All names and texts are in English.

Planned files (created stage by stage, see `ROADMAP.md`):

| File | Content | Stage |
| --- | --- | --- |
| `biomes.json` | biomes, movement cost, base yields, materials gathered (wood), colours for the debug viewer | 1, 3e |
| `resources.json` | natural deposits placed by the world generator | 1 |
| `goods.json` | goods in settlement warehouses, category, spoilage per year (only food spoils in CORE), base value for internal prices | 3a, 3e |
| `perception.json` | sight, scouts, oral memory, journeys | 3g |
| `trade.json` | trade network links, transport loss per cell, food reserve, price range | 3e |
| `settlements.json` | settlement levels (population, storage capacity, mobile) and the new tribe's camp | 3a |
| `eras.json` | eras, turn length in months, threshold technology | 3a, 3f |
| `technologies.json` | technology graph, costs, inspirations, diffusion, unlocks (recipes, gathering, effects) | 3f |
| `recipes.json` | one-step production recipes (inputs → outputs, workers, years) | 3a |
| `buildings.json` | buildings and tile improvements | 3 |
| `strata.json` | population strata, target shares and needs (3 at start, more per era) | 3b |
| `population.json` | growth, hunger, health, mood and gathering rules, the new tribe's people | 3b |
| `events.json` | random events: conditions, chances, softening technologies, effects, fertility, spreading, chronicle text | 3h |
| `effects.json` | effect types: target, changed stat, stacking rule | 3a |
| `governments.json` | governments: decision speed, stability decay | 4 |
| `units.json` | units by era, strength, upkeep, tile capacity | 4 |
| `leader_traits.json` | leader traits and AI weight modifiers | 4 |
| `ui_theme.json` | the single UI theme (9-slice assets, accents) | 5 |

Rates are always defined **per year** and converted by the era's turn length.
