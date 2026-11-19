# Drzewo technologii (stan z `data/sim/game.json`)

45 technologii bazowych (po 9 na erę) i 15 ulepszeń ery (broń, zbroje, łuki, narzędzia, rolnictwo).

**Jak się bada:** technologia wymaga doświadczenia w poprzednich (minuty pracy ludzi w budynkach tamtej technologii), potem ceny w dobrach (płaci cały lud, najpierw stolica) i czasu.

**Jak czytać tabelę:** kolumna „Wymaga” podaje minuty pracy w budynkach danej technologii; „Czas” to czas wynalazku w sekundach gry; „U” oznacza ulepszenie ery. Opisy w kolumnie „Daje” są po angielsku, tak jak w grze.

## Epoka kamienia

| Technologia | U | Wymaga | Cena | Czas | Daje |
| --- | --- | --- | --- | --- | --- |
| Foraging |  | – | – | – | Gatherers, woodcutters, huts, storehouses, scouts, settlers, warriors. |
| Fire |  | foraging 10 | 30 wood | 20 | Hearths; gatherers +10%. |
| Stone tools |  | foraging 20 | 40 food, 20 wood | 30 | Quarries, toolmakers. |
| Hunting |  | foraging 15 | 30 food, 20 wood | 25 | Hunters' lodges, archers. |
| Fishing |  | foraging 15 | 30 food, 20 wood | 25 | Fishing huts. |
| Shelters |  | foraging 25 | 40 wood | 30 | Huts give 4 more homes; clay pits. |
| Herbal lore |  | hunting 15 | 40 food | 30 | Herbalists; epidemics half as deadly. |
| Sewing |  | hunting 20, stone_tools 10 | 15 hides, 30 food | 30 | Tailors (clothing); hard winters half as harsh. |
| Rafts |  | fishing 15 | 50 wood | 30 | Raft landings; units cross rivers faster. |
| Flint blades | U | stone_tools 20 | 30 stone, 5 tools | 25 | Warriors and levy +20%. |

## Neolit

| Technologia | U | Wymaga | Cena | Czas | Daje |
| --- | --- | --- | --- | --- | --- |
| Agriculture |  | foraging 40, stone_tools 10 | 80 food, 40 wood, 20 stone | 60 | Farms; camps can become villages. |
| Pottery |  | agriculture 15, shelters 10 | 40 clay, 30 wood | 45 | Potters (pottery), granaries. |
| Animal husbandry |  | agriculture 20, hunting 20 | 100 food, 40 wood | 50 | Pastures. |
| Masonry |  | stone_tools 40, agriculture 15 | 80 stone, 20 tools | 60 | Stone houses, masons; villages can become towns. |
| Weaving |  | animal_husbandry 20, sewing 15 | 30 hides, 10 tools | 50 | Weavers (textiles). |
| Irrigation |  | agriculture 40 | 60 stone, 15 tools | 55 | Irrigation ditches; farms +25%; droughts 40% milder. |
| Barter |  | pottery 15 | 20 pottery, 60 food | 50 | Trading posts; trade agreements and caravans to other peoples. |
| Palisade |  | stone_tools 30 | 80 wood | 40 | Palisades (+40% defence). |
| Copper working |  | masonry 20, pottery 20 | 60 stone, 40 clay, 30 tools | 70 | Copper mines. |
| Polished stone tools | U | masonry 20 | 60 stone, 10 tools | 40 | Quarries, woodcutters, clay pits +15%. |
| Hide shields | U | animal_husbandry 20 | 40 hides | 40 | Infantry +15%. |

## Epoka brązu

| Technologia | U | Wymaga | Cena | Czas | Daje |
| --- | --- | --- | --- | --- | --- |
| Bronze working |  | copper_working 30 | 40 copper, 80 wood, 30 tools | 80 | Tin mines, bronze smiths, armourers (arms), spearmen. |
| The wheel |  | agriculture 60, masonry 20 | 120 wood, 20 tools | 70 | Cartwrights, chariots; porters and caravans 50% faster. |
| Writing |  | pottery 40, agriculture 60 | 80 clay, 100 food | 70 | Schools (research −10%). |
| Horseback riding |  | animal_husbandry 40 | 150 food, 40 hides | 70 | Stables, horsemen. |
| Sailing |  | fishing 40, rafts 20 | 150 wood, 20 textiles | 70 | Harbours, galleys. |
| Currency |  | barter 30, copper_working 20 | 30 copper, 30 pottery | 70 | Markets; barter at fair worth; research −5%. |
| Code of laws |  | writing 10 | 60 clay, 80 food | 60 | Barracks (training ×2 faster); levy trains 50% faster. |
| Composite bow |  | hunting 40, bronze_working 15 | 80 wood, 30 hides | 60 | Archers +30%. |
| Brick walls |  | masonry 40, pottery 30 | 150 clay, 50 wood | 60 | Brick walls (+70% defence). |
| Bronze weapons | U | bronze_working 20 | 30 bronze, 40 wood | 60 | Melee +30%. |
| Bronze armour | U | bronze_working 30 | 40 bronze, 20 hides | 60 | Infantry +20%. |
| Bronze tools | U | bronze_working 25 | 30 bronze, 20 tools | 55 | Quarries, woodcutters, mines +20%. |

## Epoka żelaza

| Technologia | U | Wymaga | Cena | Czas | Daje |
| --- | --- | --- | --- | --- | --- |
| Iron working |  | bronze_working 50 | 40 bronze, 120 wood, 40 tools | 90 | Iron mines, forges, swordsmen. |
| Mathematics |  | writing 40 | 100 clay, 150 food | 80 | Observatories (research −15%). |
| Construction |  | masonry 60, mathematics 10 | 200 stone, 150 wood, 60 tools | 100 | Great storehouses; towns can become cities. |
| Roads |  | construction 10, the_wheel 30 | 150 stone, 30 tools | 70 | Roads: walking on them twice as fast. |
| Engineering |  | construction 20, mathematics 20 | 200 wood, 60 tools, 20 iron | 90 | Siege workshops, catapults. |
| Medicine |  | herbal_lore 40, writing 30 | 150 food, 40 pottery | 80 | Bathhouses; growth +2% a minute; epidemics 30% milder. |
| Philosophy |  | writing 60, mathematics 20 | 150 clay, 40 textiles | 90 | Academies; rarer thinkers and advisors. |
| Shipbuilding |  | sailing 40, iron_working 20 | 250 wood, 30 iron | 90 | Shipyards, warships. |
| Stone walls |  | masonry 60, construction 20 | 250 stone | 80 | Stone walls (+110% defence). |
| Iron weapons | U | iron_working 30 | 40 iron, 60 wood | 70 | Melee +30%. |
| Chain mail | U | iron_working 40 | 60 iron, 30 hides | 70 | Infantry and cavalry +25%. |
| Iron tools | U | iron_working 30 | 40 iron, 30 tools | 65 | Quarries, woodcutters, mines +25%; farms +15%. |
| Crop rotation | U | agriculture 120, irrigation 40 | 200 food, 40 tools | 70 | Farms +25%. |

## Średniowiecze

| Technologia | U | Wymaga | Cena | Czas | Daje |
| --- | --- | --- | --- | --- | --- |
| Feudalism |  | construction 40, horseback_riding 30 | 300 food, 60 iron, 200 stone | 120 | Manors, knights. |
| Steel |  | iron_working 60 | 120 iron, 200 wood, 80 tools | 120 | Steelworks; all soldiers +10%. |
| Castles |  | construction 60 | 400 stone, 40 iron | 110 | Castles (+220% defence). |
| Heavy plough |  | agriculture 200, iron_working 40 | 60 iron, 150 wood | 90 | Farms +30%. |
| Crossbows |  | iron_working 40 | 60 iron, 100 wood | 90 | Crossbowmen. |
| Guilds |  | currency 60, weaving 60 | 80 textiles, 60 pottery | 100 | Guild halls; workshops +25%. |
| Mills |  | agriculture 200, the_wheel 60 | 200 stone, 150 wood | 90 | Mills; farms +20%. |
| Chivalry |  | feudalism 20 | 200 food, 60 iron | 90 | Knights +30%. |
| University |  | philosophy 40, writing 120 | 300 stone, 80 textiles | 120 | Universities (research −20%). |
| Steel weapons | U | steel 30 | 80 iron, 60 wood | 90 | Melee +30%. |
| Plate armour | U | steel 40 | 120 iron, 30 textiles | 100 | Infantry and cavalry +30%. |
| Longbow | U | steel 10 | 150 wood, 20 iron | 80 | Archers and crossbowmen +30%. |
| Heavy cavalry | U | feudalism 40 | 200 food, 80 iron | 90 | Cavalry +30%. |
| Trebuchet | U | engineering 40 | 250 wood, 40 iron | 90 | Siege engines +100% against settlements. |
