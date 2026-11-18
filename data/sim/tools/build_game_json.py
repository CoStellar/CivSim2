# Rebuilds the tree, units and buildings of data/sim/game.json (run from the repo root: python3 data/sim/tools/build_game_json.py).
# It edits the file in place and keeps the sections it does not touch.
import json
p='data/sim/game.json'; d=json.load(open(p))

d['goods'] = [
 {"key":"food","name":"Food","worth":1},{"key":"wood","name":"Wood","worth":1},{"key":"stone","name":"Stone","worth":1.2},
 {"key":"clay","name":"Clay","worth":1},{"key":"hides","name":"Hides","worth":1.5},{"key":"tools","name":"Tools","worth":3},
 {"key":"clothing","name":"Clothing","worth":3},{"key":"pottery","name":"Pottery","worth":2.5},{"key":"textiles","name":"Textiles","worth":3},
 {"key":"copper","name":"Copper","worth":4},{"key":"tin","name":"Tin","worth":5},{"key":"bronze","name":"Bronze","worth":8},
 {"key":"iron","name":"Iron","worth":8},{"key":"arms","name":"Arms","worth":10}]
d['buildingStorage'] = 20
d['comment'] += " Storage: goods lie in the settlement where they were made; every finished building adds buildingStorage of room for each good unless it gives its own storage (storehouses). Construction materials are carried by porters from the nearest storehouse (or the settlement centre) and the building time starts when they arrive. groups: tags that technology effects can aim at (e.g. melee, tools)."
d['diplomacy']['tradeTech'] = "barter"
d['diplomacy']['comment'] = "Peoples consider a treaty only when they have known each other for knownMinutes, their settlements are at most borderHexes apart, they have not fought for peaceMinutes and the other's reputation is above minReputation; then a trade agreement when they lack a good the other makes or the other knows more technologies, an alliance when they share an enemy. At most one offer to the same people every offerCooldownMinutes, refusalCooldownMinutes after a refusal. Trade agreements need the barter technology. Barter goods travel in caravans; the AI takes a barter worth acceptWorthPercent of what it gives (100 with currency) and keeps twice what it gives."
d['diplomacy'].update({"knownMinutes":3,"borderHexes":20,"peaceMinutes":10,"minReputation":-20,"offerCooldownMinutes":3,"refusalCooldownMinutes":6})
d.pop('reviewSeconds', None); d['diplomacy'].pop('reviewSeconds', None)
d['settlementLevels'] = [
 {"key":"camp","name":"Camp","radius":3,"housing":25,"storage":150,"upgradePrice":{}},
 {"key":"village","name":"Village","radius":4,"housing":40,"storage":200,"upgradePrice":{"wood":120,"stone":60,"clay":30},"tech":"agriculture"},
 {"key":"town","name":"Town","radius":5,"housing":60,"storage":300,"upgradePrice":{"wood":250,"stone":200,"pottery":40,"clothing":30},"tech":"masonry"},
 {"key":"city","name":"City","radius":6,"housing":90,"storage":500,"upgradePrice":{"wood":500,"stone":500,"textiles":80,"pottery":80,"tools":60},"tech":"construction"}]

FOREST=["deciduous_forest","taiga","jungle","swamp"]
B=[]
def b(key,name,tech,workers,price,secs,**kw):
    x={"key":key,"name":name,"tech":tech,"workers":workers,"price":price,"buildSeconds":secs}; x.update(kw); B.append(x)
# Stone age
b("gatherers_hut","Gatherers' hut","foraging",3,{"wood":15},12,biomes=["grassland","steppe","savanna","deciduous_forest","jungle","taiga","swamp","tundra"],produces={"food":4,"wood":1},bonusDeposit="wild_grain",bonusPercent=100,groups=["food"])
b("woodcutter","Woodcutter","foraging",3,{"wood":10,"stone":5},12,biomes=FOREST,produces={"wood":4},groups=["tools"])
b("hut","Huts","foraging",0,{"wood":20},10,housing=8,groups=["house"])
b("storehouse","Storehouse","foraging",0,{"wood":30},20,storage=300,groups=["store"])
b("hearth","Hearth","fire",0,{"wood":15,"stone":10},10,housing=3,groups=["house"])
b("quarry","Quarry","stone_tools",4,{"wood":25},20,produces={"stone":2},bonusRelief=["hills","mountains"],bonusReliefPercent=100,bonusDeposit="stone",bonusPercent=100,groups=["tools"])
b("toolmaker","Toolmaker","stone_tools",2,{"wood":30,"stone":20},25,consumes={"stone":2,"wood":1},produces={"tools":1},bonusDeposit="flint",bonusPercent=50,groups=["craft"])
b("hunters_lodge","Hunters' lodge","hunting",3,{"wood":20},15,biomes=["grassland","steppe","savanna","deciduous_forest","taiga","jungle","tundra"],produces={"food":3,"hides":1},bonusDeposit="game",bonusPercent=100,groups=["food"])
b("fishing_hut","Fishing hut","fishing",2,{"wood":20},15,needsWater=True,produces={"food":5},bonusDeposit="fish",bonusPercent=100,groups=["food"])
b("clay_pit","Clay pit","shelters",3,{"wood":20},15,needsWater=True,orDeposit="clay",produces={"clay":3},bonusDeposit="clay",bonusPercent=100,groups=["tools"])
b("herbalist","Herbalist","herbal_lore",2,{"wood":20,"clay":10},20,produces={},groups=["care"])
b("tailor","Tailor","sewing",2,{"wood":20,"tools":5},20,consumes={"hides":1},produces={"clothing":1},groups=["craft"])
b("raft_landing","Raft landing","rafts",2,{"wood":40},20,needsWater=True,produces={"food":2,"wood":1},groups=["food"])
# Neolithic
b("farm","Farm","agriculture",4,{"wood":30,"tools":5},20,biomes=["grassland","steppe","savanna","deciduous_forest"],minFertility=40,produces={"food":6},bonusDeposit="wild_grain",bonusPercent=50,groups=["food","farm"])
b("potter","Potter","pottery",2,{"wood":20,"stone":10},20,consumes={"clay":2,"wood":1},produces={"pottery":1},groups=["craft"])
b("granary","Granary","pottery",0,{"wood":30,"clay":30},25,storage=250,groups=["store"])
b("pasture","Pasture","animal_husbandry",2,{"wood":30},20,biomes=["grassland","steppe","savanna","tundra"],produces={"food":4,"hides":2},bonusDeposit="cattle",bonusPercent=100,groups=["food"])
b("stone_house","Stone houses","masonry",0,{"stone":40,"wood":10},25,housing=16,groups=["house"])
b("mason","Mason","masonry",3,{"wood":30,"tools":10},25,consumes={"stone":1},produces={},groups=["craft"])
b("weaver","Weaver","weaving",2,{"wood":30,"tools":5},25,consumes={"hides":1,"food":1},produces={"textiles":1},groups=["craft"])
b("irrigation_ditch","Irrigation ditch","irrigation",2,{"wood":20,"stone":20},20,needsWater=True,minFertility=30,produces={"food":5},groups=["food","farm"])
b("trading_post","Trading post","barter",2,{"wood":40,"pottery":10},30,storage=150,produces={},groups=["store"])
b("palisade","Palisade","palisade",0,{"wood":60},25,defensePercent=40,groups=["fort"])
b("copper_mine","Copper mine","copper_working",4,{"wood":40,"tools":15},30,orDeposit="copper",produces={"copper":2},groups=["tools","mine"])
# Bronze
b("tin_mine","Tin mine","bronze_working",4,{"wood":40,"tools":15},30,orDeposit="tin",produces={"tin":2},groups=["tools","mine"])
b("bronze_smith","Bronze smith","bronze_working",3,{"stone":40,"clay":30},30,consumes={"copper":2,"tin":1,"wood":1},produces={"bronze":1},groups=["craft"])
b("armourer","Armourer","bronze_working",2,{"stone":40,"wood":30},30,consumes={"bronze":1,"wood":1},produces={"arms":1},groups=["craft"])
b("cartwright","Cartwright","the_wheel",2,{"wood":50,"tools":10},30,consumes={"wood":2},produces={"tools":1},groups=["craft"])
b("school","School","writing",3,{"stone":60,"clay":40},40,produces={},researchDiscountPercent=10,groups=["learn"])
b("stable","Stable","horseback_riding",2,{"wood":60},30,consumes={"food":2},produces={"hides":1},groups=["food"])
b("harbour","Harbour","sailing",3,{"wood":80,"stone":20},40,needsWater=True,produces={"food":4},groups=["food"])
b("market","Market","currency",3,{"wood":60,"stone":40,"pottery":10},40,storage=200,produces={},groups=["store"])
b("barracks","Barracks","code_of_laws",0,{"wood":80,"stone":40},40,trainPercent=50,groups=["army"])
b("brick_walls","Brick walls","brick_walls",0,{"clay":120,"wood":30},35,defensePercent=70,groups=["fort"])
# Iron
b("iron_mine","Iron mine","iron_working",4,{"wood":50,"tools":20},35,orDeposit="iron_ore",produces={"iron":1},groups=["tools","mine"])
b("forge","Forge","iron_working",3,{"stone":60,"wood":40},35,consumes={"iron":1,"wood":2},produces={"tools":2,"arms":1},groups=["craft"])
b("observatory","Observatory","mathematics",2,{"stone":100},45,produces={},researchDiscountPercent=15,groups=["learn"])
b("great_storehouse","Great storehouse","construction",0,{"stone":150,"wood":80},45,storage=1000,groups=["store"])
b("builders_yard","Builders' yard","construction",3,{"stone":80,"wood":60},40,consumes={"stone":1},produces={},groups=["craft"])
b("siege_workshop","Siege workshop","engineering",3,{"wood":100,"tools":30},45,consumes={"wood":2},produces={},groups=["army"])
b("bathhouse","Bathhouse","medicine",2,{"stone":80,"pottery":20},40,housing=4,produces={},groups=["care"])
b("academy","Academy","philosophy",3,{"stone":120,"textiles":20},50,produces={},researchDiscountPercent=10,groups=["learn"])
b("shipyard","Shipyard","shipbuilding",4,{"wood":150,"tools":30},50,needsWater=True,consumes={"wood":2},produces={},groups=["army"])
b("stone_walls","Stone walls","stone_walls",0,{"stone":150},45,defensePercent=110,groups=["fort"])
# Middle ages
b("manor","Manor","feudalism",4,{"stone":200,"wood":100},60,produces={"food":8},housing=10,groups=["food"])
b("steelworks","Steelworks","steel",3,{"stone":150,"iron":40},60,consumes={"iron":2,"wood":2},produces={"tools":4,"arms":2},groups=["craft"])
b("castle","Castle","castles",0,{"stone":400,"wood":100,"iron":30},90,defensePercent=220,housing=10,groups=["fort"])
b("mill","Mill","mills",3,{"wood":80,"stone":60,"tools":10},40,needsWater=True,produces={"food":6},groups=["food","farm"])
b("guildhall","Guild hall","guilds",3,{"stone":150,"textiles":30},50,produces={},groups=["craft"])
b("university","University","university",4,{"stone":200,"textiles":40,"pottery":40},60,produces={},researchDiscountPercent=20,groups=["learn"])
for x in B:
    x.setdefault("produces",{}) if x["workers"]>0 else None
d['buildings']=B

U=[]
def u(key,name,tech,people,price,secs,perHex,sight,**kw):
    x={"key":key,"name":name,"tech":tech,"people":people,"price":price,"trainSeconds":secs,"ticksPerHex":perHex,"sight":sight}; x.update(kw); U.append(x)
u("settlers","Settlers","foraging",8,{"food":40,"wood":30},10,6,3,founds=True)
u("scouts","Scouts","foraging",2,{"food":15},5,3,5,hiddenTicksPerHex=6)
u("porters","Porters","foraging",1,{},0,5,2,system=True,cargoPerPerson=20,groups=["carriers"])
u("caravan","Caravan","foraging",2,{},0,6,3,system=True,cargoPerPerson=25,groups=["carriers"])
u("levy","Levy","foraging",10,{},3,7,2,army=True,levy=True,strength=5,groups=["melee"])
u("warriors","Warriors","foraging",10,{"food":30,"wood":10},12,6,3,army=True,strength=10,bonusBiomes=FOREST[:3],bonusPercent=30,groups=["melee","infantry"])
u("archers","Archers","hunting",8,{"food":30,"wood":25},15,6,4,army=True,strength=8,range=2,bonusRelief=["hills","mountains"],bonusPercent=40,groups=["ranged","infantry"])
u("spearmen","Spearmen","bronze_working",10,{"food":40,"arms":5},18,6,3,army=True,strength=16,bonusRelief=["flat"],bonusPercent=20,groups=["melee","infantry"])
u("chariots","Chariots","the_wheel",6,{"food":50,"wood":40,"bronze":5},20,3,4,army=True,strength=16,bonusRelief=["flat"],bonusPercent=50,groups=["cavalry"])
u("horsemen","Horsemen","horseback_riding",6,{"food":60,"hides":20},20,3,5,army=True,strength=18,bonusRelief=["flat"],bonusPercent=40,groups=["cavalry"])
u("galley","Galley","sailing",8,{"wood":80,"food":30},25,4,4,army=True,naval=True,strength=10,needsBuilding="harbour",groups=["ship"])
u("swordsmen","Swordsmen","iron_working",10,{"food":50,"arms":10},20,6,3,army=True,strength=24,groups=["melee","infantry"])
u("catapult","Catapult","engineering",6,{"wood":100,"tools":20},30,9,3,army=True,strength=8,range=2,siege=300,needsBuilding="siege_workshop",groups=["siege"])
u("warship","Warship","shipbuilding",12,{"wood":150,"arms":10},35,3,5,army=True,naval=True,strength=22,range=2,needsBuilding="shipyard",groups=["ship"])
u("crossbowmen","Crossbowmen","crossbows",8,{"food":40,"arms":10},20,6,4,army=True,strength=20,range=2,groups=["ranged","infantry"])
u("knights","Knights","feudalism",6,{"food":80,"arms":15,"hides":20},30,3,5,army=True,strength=40,bonusRelief=["flat"],bonusPercent=30,groups=["cavalry"])
# Sight (decision of 2026-11-18: units saw too much): scouts, riders and ships 3, carriers 1, the rest 2.
SIGHT={"scouts":3,"horsemen":3,"knights":3,"chariots":3,"galley":3,"warship":3,"porters":1,"caravan":1}
for x in U:
    x["sight"]=SIGHT.get(x["key"],2)
d['units']=U

T=[]
def t(key,name,era,exp=None,price=None,secs=0,gives="",effects=None,upgrade=False,known=False):
    x={"key":key,"name":name,"era":era,"gives":gives}
    if known: x["known"]=True
    if exp: x["experience"]=exp
    if price: x["price"]=price
    if secs: x["seconds"]=secs
    if effects: x["effects"]=[{"kind":k,"target":tg,"value":v} for (k,tg,v) in effects]
    if upgrade: x["upgrade"]=True
    T.append(x)
S,N,BR,IR,M="stone_age","neolithic","bronze_age","iron_age","middle_ages"
# Stone age (9)
t("foraging","Foraging",S,known=True,gives="Gatherers, woodcutters, huts, storehouses, scouts, settlers, warriors.")
t("fire","Fire",S,{"foraging":10},{"wood":30},20,"Hearths; gatherers +10%.",[("production","gatherers_hut",10)])
t("stone_tools","Stone tools",S,{"foraging":20},{"food":40,"wood":20},30,"Quarries, toolmakers.")
t("hunting","Hunting",S,{"foraging":15},{"food":30,"wood":20},25,"Hunters' lodges, archers.")
t("fishing","Fishing",S,{"foraging":15},{"food":30,"wood":20},25,"Fishing huts.")
t("shelters","Shelters",S,{"foraging":25},{"wood":40},30,"Huts give 4 more homes; clay pits.",[("housing","hut",4)])
t("herbal_lore","Herbal lore",S,{"hunting":15},{"food":40},30,"Herbalists; epidemics half as deadly.",[("resist","epidemic",50)])
t("sewing","Sewing",S,{"hunting":20,"stone_tools":10},{"hides":15,"food":30},30,"Tailors (clothing); hard winters half as harsh.",[("resist","hard_winter",50)])
t("rafts","Rafts",S,{"fishing":15},{"wood":50},30,"Raft landings; units cross rivers faster.",[("speed","*",10)])
t("flint_blades","Flint blades",S,{"stone_tools":20},{"stone":30,"tools":5},25,"Warriors and levy +20%.",[("strength","melee",20)],True)
# Neolithic (9)
t("agriculture","Agriculture",N,{"foraging":40,"stone_tools":10},{"food":80,"wood":40,"stone":20},60,"Farms; camps can become villages.")
t("pottery","Pottery",N,{"agriculture":15,"shelters":10},{"clay":40,"wood":30},45,"Potters (pottery), granaries.")
t("animal_husbandry","Animal husbandry",N,{"agriculture":20,"hunting":20},{"food":100,"wood":40},50,"Pastures.")
t("masonry","Masonry",N,{"stone_tools":40,"agriculture":15},{"stone":80,"tools":20},60,"Stone houses, masons; villages can become towns.")
t("weaving","Weaving",N,{"animal_husbandry":20,"sewing":15},{"hides":30,"tools":10},50,"Weavers (textiles).")
t("irrigation","Irrigation",N,{"agriculture":40},{"stone":60,"tools":15},55,"Irrigation ditches; farms +25%; droughts 40% milder.",[("production","farm",25),("resist","drought",40)])
t("barter","Barter",N,{"pottery":15},{"pottery":20,"food":60},50,"Trading posts; trade agreements and caravans to other peoples.")
t("palisade","Palisade",N,{"stone_tools":30},{"wood":80},40,"Palisades (+40% defence).")
t("copper_working","Copper working",N,{"masonry":20,"pottery":20},{"stone":60,"clay":40,"tools":30},70,"Copper mines.")
t("polished_stone","Polished stone tools",N,{"masonry":20},{"stone":60,"tools":10},40,"Quarries, woodcutters, clay pits +15%.",[("production","tools",15)],True)
t("hide_shields","Hide shields",N,{"animal_husbandry":20},{"hides":40},40,"Infantry +15%.",[("strength","infantry",15)],True)
# Bronze (9)
t("bronze_working","Bronze working",BR,{"copper_working":30},{"copper":40,"wood":80,"tools":30},80,"Tin mines, bronze smiths, armourers (arms), spearmen.")
t("the_wheel","The wheel",BR,{"agriculture":60,"masonry":20},{"wood":120,"tools":20},70,"Cartwrights, chariots; porters and caravans 50% faster.",[("speed","carriers",50)])
t("writing","Writing",BR,{"pottery":40,"agriculture":60},{"clay":80,"food":100},70,"Schools (research −10%).")
t("horseback_riding","Horseback riding",BR,{"animal_husbandry":40},{"food":150,"hides":40},70,"Stables, horsemen.")
t("sailing","Sailing",BR,{"fishing":40,"rafts":20},{"wood":150,"textiles":20},70,"Harbours, galleys.")
t("currency","Currency",BR,{"barter":30,"copper_working":20},{"copper":30,"pottery":30},70,"Markets; barter at fair worth; research −5%.",[("research","*",5)])
t("code_of_laws","Code of laws",BR,{"writing":10},{"clay":60,"food":80},60,"Barracks (training ×2 faster); levy trains 50% faster.",[("training","levy",50)])
t("composite_bow","Composite bow",BR,{"hunting":40,"bronze_working":15},{"wood":80,"hides":30},60,"Archers +30%.",[("strength","archers",30)])
t("brick_walls","Brick walls",BR,{"masonry":40,"pottery":30},{"clay":150,"wood":50},60,"Brick walls (+70% defence).")
t("bronze_weapons","Bronze weapons",BR,{"bronze_working":20},{"bronze":30,"wood":40},60,"Melee +30%.",[("strength","melee",30)],True)
t("bronze_armour","Bronze armour",BR,{"bronze_working":30},{"bronze":40,"hides":20},60,"Infantry +20%.",[("strength","infantry",20)],True)
t("bronze_tools","Bronze tools",BR,{"bronze_working":25},{"bronze":30,"tools":20},55,"Quarries, woodcutters, mines +20%.",[("production","tools",20)],True)
# Iron (9)
t("iron_working","Iron working",IR,{"bronze_working":50},{"bronze":40,"wood":120,"tools":40},90,"Iron mines, forges, swordsmen.")
t("mathematics","Mathematics",IR,{"writing":40},{"clay":100,"food":150},80,"Observatories (research −15%).")
t("construction","Construction",IR,{"masonry":60,"mathematics":10},{"stone":200,"wood":150,"tools":60},100,"Great storehouses; towns can become cities.")
t("roads","Roads",IR,{"construction":10,"the_wheel":30},{"stone":150,"tools":30},70,"Paved roads: much faster than tracks, also over hills; major trade routes.")
t("engineering","Engineering",IR,{"construction":20,"mathematics":20},{"wood":200,"tools":60,"iron":20},90,"Siege workshops, catapults.")
t("medicine","Medicine",IR,{"herbal_lore":40,"writing":30},{"food":150,"pottery":40},80,"Bathhouses; growth +2% a minute; epidemics 30% milder.",[("growth","*",2),("resist","epidemic",30)])
t("philosophy","Philosophy",IR,{"writing":60,"mathematics":20},{"clay":150,"textiles":40},90,"Academies; rarer thinkers and advisors.",[("luck","*",40)])
t("shipbuilding","Shipbuilding",IR,{"sailing":40,"iron_working":20},{"wood":250,"iron":30},90,"Shipyards, warships.")
t("stone_walls","Stone walls",IR,{"masonry":60,"construction":20},{"stone":250},80,"Stone walls (+110% defence).")
t("iron_weapons","Iron weapons",IR,{"iron_working":30},{"iron":40,"wood":60},70,"Melee +30%.",[("strength","melee",30)],True)
t("chain_mail","Chain mail",IR,{"iron_working":40},{"iron":60,"hides":30},70,"Infantry and cavalry +25%.",[("strength","infantry",25),("strength","cavalry",25)],True)
t("iron_tools","Iron tools",IR,{"iron_working":30},{"iron":40,"tools":30},65,"Quarries, woodcutters, mines +25%; farms +15%.",[("production","tools",25),("production","farm",15)],True)
t("crop_rotation","Crop rotation",IR,{"agriculture":120,"irrigation":40},{"food":200,"tools":40},70,"Farms +25%.",[("production","farm",25)],True)
# Middle ages (9)
t("feudalism","Feudalism",M,{"construction":40,"horseback_riding":30},{"food":300,"iron":60,"stone":200},120,"Manors, knights.")
t("steel","Steel",M,{"iron_working":60},{"iron":120,"wood":200,"tools":80},120,"Steelworks; all soldiers +10%.",[("strength","*",10)])
t("castles","Castles",M,{"stone_walls":0,"construction":60},{"stone":400,"iron":40},110,"Castles (+220% defence).")
t("heavy_plough","Heavy plough",M,{"agriculture":200,"iron_working":40},{"iron":60,"wood":150},90,"Farms +30%.",[("production","farm",30)])
t("crossbows","Crossbows",M,{"composite_bow":0,"iron_working":40},{"iron":60,"wood":100},90,"Crossbowmen.")
t("guilds","Guilds",M,{"currency":60,"weaving":60},{"textiles":80,"pottery":60},100,"Guild halls; workshops +25%.",[("production","craft",25)])
t("mills","Mills",M,{"agriculture":200,"the_wheel":60},{"stone":200,"wood":150},90,"Mills; farms +20%.",[("production","farm",20)])
t("chivalry","Chivalry",M,{"feudalism":20},{"food":200,"iron":60},90,"Knights +30%.",[("strength","knights",30)])
t("university","University",M,{"philosophy":40,"writing":120},{"stone":300,"textiles":80},120,"Universities (research −20%).")
t("steel_weapons","Steel weapons",M,{"steel":30},{"iron":80,"wood":60},90,"Melee +30%.",[("strength","melee",30)],True)
t("plate_armour","Plate armour",M,{"steel":40},{"iron":120,"textiles":30},100,"Infantry and cavalry +30%.",[("strength","infantry",30),("strength","cavalry",30)],True)
t("longbow","Longbow",M,{"composite_bow":0,"steel":10},{"wood":150,"iron":20},80,"Archers and crossbowmen +30%.",[("strength","ranged",30)],True)
t("heavy_cavalry","Heavy cavalry",M,{"feudalism":40},{"food":200,"iron":80},90,"Cavalry +30%.",[("strength","cavalry",30)],True)
t("trebuchet","Trebuchet",M,{"engineering":40,"castles":0},{"wood":250,"iron":40},90,"Siege engines +100% against settlements.",[("siege","*",100)],True)
for x in T:
    if "experience" in x:
        x["experience"]={k:v for k,v in x["experience"].items() if v>0}
CAT={
"foraging":"food","fire":"food","stone_tools":"industry","hunting":"food","fishing":"food","shelters":"building","herbal_lore":"science","sewing":"industry","rafts":"trade","flint_blades":"military",
"agriculture":"food","pottery":"industry","animal_husbandry":"food","masonry":"building","weaving":"industry","irrigation":"food","barter":"trade","palisade":"military","copper_working":"industry","polished_stone":"industry","hide_shields":"military",
"bronze_working":"industry","the_wheel":"trade","writing":"governance","horseback_riding":"military","sailing":"trade","currency":"trade","code_of_laws":"governance","composite_bow":"military","brick_walls":"military","bronze_weapons":"military","bronze_armour":"military","bronze_tools":"industry",
"iron_working":"industry","mathematics":"science","construction":"building","roads":"trade","engineering":"military","medicine":"science","philosophy":"science","shipbuilding":"trade","stone_walls":"military","iron_weapons":"military","chain_mail":"military","iron_tools":"industry","crop_rotation":"food",
"feudalism":"governance","steel":"industry","castles":"military","heavy_plough":"food","crossbows":"military","guilds":"industry","mills":"food","chivalry":"military","university":"science","steel_weapons":"military","plate_armour":"military","longbow":"military","heavy_cavalry":"military","trebuchet":"military"}
for x in T:
    x["category"]=CAT[x["key"]]
d["techCategories"]=["food","industry","building","trade","military","governance","science"]
d["eraAdvanceTechs"]=5
# Roads are part of the map, drawn hex by hex (decision of 2026-11-18): tracks from the start, paved roads with Roads.
d["roads"]={"comment":"Levels of roads. walkPercent: walking cost on them, of the hex's cost (capped at 150 so roads cross rough land). reach: hexes from our nearest settlement. supply: goods a caravan brings to a new camp joined to our roads.",
  "levels":[{"key":"track","name":"Track","tech":"foraging","price":{"wood":2},"seconds":2,"walkPercent":60},
            {"key":"road","name":"Road","tech":"roads","price":{"stone":3,"wood":1},"seconds":4,"walkPercent":35}],
  "reach":25,"supply":{"food":40,"wood":60,"stone":30,"clay":20}}
d["tripFoodPer10Hexes"]=0.5
d['technologies']=T

# remove old unit upgrades fields (now effects); keep terrain bonus
json.dump(d,open(p,'w'),indent=2,ensure_ascii=False); open(p,'a').write('\n')

# validation: experience refs must have a workers-building; buildings/units techs exist
keys={x['key'] for x in T}
work={}
for x in B:
    if x['workers']>0: work.setdefault(x['tech'],[]).append(x['key'])
probs=[]
for x in T:
    for k in x.get('experience',{}):
        if k not in keys: probs.append(('missing',x['key'],k))
        elif k not in work and k!='foraging': probs.append(('noxp',x['key'],k))
for x in B+U:
    if x['tech'] not in keys: probs.append(('tech',x['key'],x['tech']))
base=[x for x in T if not x.get('upgrade')]; up=[x for x in T if x.get('upgrade')]
print(len(base),len(up),probs)
from collections import Counter
print(Counter(x['era'] for x in base))
