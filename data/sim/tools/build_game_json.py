# Rebuilds the tree, units and buildings of data/sim/game.json (run from the repo root: python3 data/sim/tools/build_game_json.py).
# It edits the file in place and keeps the sections it does not touch.
import json
p='data/sim/game.json'; d=json.load(open(p))

d['goods'] = [
 {"key":"food","name":"Food","worth":1},{"key":"wood","name":"Wood","worth":1},{"key":"stone","name":"Stone","worth":1.2},
 {"key":"clay","name":"Clay","worth":1},{"key":"hides","name":"Hides","worth":1.5},{"key":"tools","name":"Tools","worth":3},
 {"key":"clothing","name":"Clothing","worth":3},{"key":"pottery","name":"Pottery","worth":2.5},{"key":"textiles","name":"Textiles","worth":3},
 {"key":"copper","name":"Copper","worth":4},{"key":"tin","name":"Tin","worth":5},{"key":"bronze","name":"Bronze","worth":8},
 {"key":"iron","name":"Iron","worth":8},
 # Weapons and armour of four levels (Patryk, 2026-11-19): a unit is armed with the best it gets; both lie at half room in an armoury.
 {"key":"stone_weapons","name":"Stone weapons","worth":3,"storedIn":"armoury"},{"key":"bronze_weapons","name":"Bronze weapons","worth":9,"storedIn":"armoury"},
 {"key":"iron_weapons","name":"Iron weapons","worth":12,"storedIn":"armoury"},{"key":"steel_weapons","name":"Steel weapons","worth":18,"storedIn":"armoury"},
 {"key":"hide_armour","name":"Hide armour","worth":3,"storedIn":"armoury"},{"key":"bronze_armour","name":"Bronze armour","worth":9,"storedIn":"armoury"},
 {"key":"iron_armour","name":"Iron armour","worth":12,"storedIn":"armoury"},{"key":"steel_armour","name":"Plate armour","worth":18,"storedIn":"armoury"},
 {"key":"horses","name":"Horses","worth":12},{"key":"coins","name":"Coins","worth":1}]
d['buildingStorage'] = 20
EXTRA = " Storage: goods lie in the settlement where they were made; every finished building adds buildingStorage of room for each good unless it gives its own storage (storehouses). Construction materials are carried by porters from the nearest storehouse (or the settlement centre); the work goes as far as the delivered materials allow. groups: tags that technology effects can aim at (e.g. melee, tools)."
d['comment'] = (d['comment'].split(" Storage: goods lie")[0] + EXTRA).replace("idle people forage idleForagePerMinute each", "people told to gather pick up food, wood and stone around their settlement (forage)")
d['diplomacy']['tradeTech'] = "barter"
d['diplomacy']['comment'] = "Peoples consider a treaty only when they have known each other for knownMinutes, their settlements are at most borderHexes apart, they have not fought for peaceMinutes and the other's reputation is above minReputation; then a trade agreement when they lack a good the other makes or the other knows more technologies, an alliance when they share an enemy. At most one offer to the same people every offerCooldownMinutes, refusalCooldownMinutes after a refusal. Trade agreements need the barter technology. Barter goods travel in caravans; the AI takes a barter worth acceptWorthPercent of what it gives (100 with currency) and keeps twice what it gives."
d['diplomacy'].update({"knownMinutes":3,"borderHexes":20,"peaceMinutes":10,"minReputation":-20,"offerCooldownMinutes":3,"refusalCooldownMinutes":6})
d.pop('reviewSeconds', None); d['diplomacy'].pop('reviewSeconds', None)
d['settlementLevels'] = [
 {"key":"camp","name":"Camp","radius":3,"housing":25,"storage":150,"upgradePrice":{}},
 {"key":"village","name":"Village","radius":4,"housing":40,"storage":200,"upgradePrice":{"wood":120,"stone":60,"clay":30},"tech":"agriculture","minPeople":40},
 {"key":"town","name":"Town","radius":5,"housing":60,"storage":300,"upgradePrice":{"wood":250,"stone":200,"pottery":40,"clothing":30},"tech":"code_of_laws","alsoTechs":["masonry"],"minPeople":150},
 {"key":"city","name":"City","radius":6,"housing":90,"storage":500,"upgradePrice":{"wood":500,"stone":500,"textiles":80,"pottery":80,"tools":60},"tech":"civil_service","alsoTechs":["construction"],"minPeople":400}]
# minPeople (Patryk, 2026-11-19, E2): people a settlement must have to reach the level (wide steps: village 40, town 150).

FOREST=["deciduous_forest","taiga","jungle","swamp"]
B=[]
def b(key,name,tech,workers,price,secs,**kw):
    x={"key":key,"name":name,"tech":tech,"workers":workers,"price":price,"buildSeconds":secs}; x.update(kw); B.append(x)
# Stone age
b("gatherers_hut","Gatherers' hut","foraging",3,{"wood":15},12,biomes=["grassland","steppe","savanna","deciduous_forest","jungle","taiga","swamp","tundra"],produces={"food":4,"wood":1},bonusDeposit="wild_grain",bonusPercent=100,winterPercent=50,groups=["food"])
b("woodcutter","Woodcutter","foraging",3,{"wood":15},12,biomes=FOREST,orTrees=True,produces={"wood":4},groups=["tools"])
b("hut","Huts","dwellings",0,{"wood":20},10,housing=8,groups=["house"])
b("storehouse","Storehouse","stockpiling",0,{"wood":30},20,storage=300,groups=["store"])
b("hearth","Hearth","fire",0,{"wood":15,"stone":10},10,housing=3,groups=["house"])
b("quarry","Quarry","stone_tools",4,{"wood":25},20,produces={"stone":2},bonusRelief=["hills","mountains"],bonusReliefPercent=100,bonusDeposit="stone",bonusPercent=100,groups=["tools","quarry"])
b("toolmaker","Toolmaker","stone_tools",2,{"wood":30,"stone":20},25,consumes={"stone":2,"wood":1},produces={"tools":1,"stone_weapons":0.5},bonusDeposit="flint",bonusPercent=50,groups=["craft"])
b("hunters_lodge","Hunters' lodge","hunting",3,{"wood":20},15,biomes=["grassland","steppe","savanna","deciduous_forest","taiga","jungle","tundra"],produces={"food":3,"hides":1},bonusDeposit="game",bonusPercent=100,groups=["food","hunt"])
b("clay_pit","Clay pit","shelters",3,{"wood":20},15,needsWater=True,orDeposit="clay",produces={"clay":3},bonusDeposit="clay",bonusPercent=100,groups=["tools"])
b("herbalist","Herbalist","herbal_lore",2,{"wood":20,"clay":10},20,produces={},supportGroup="hunt",supportPercent=25,healsPerMinute=6,groups=["care"])
b("tailor","Tailor","sewing",2,{"wood":20,"tools":5},20,consumes={"hides":1},produces={"clothing":1,"hide_armour":0.3},groups=["craft"])
# Neolithic
b("fishing_hut","Fishing hut","fishing",2,{"wood":20},15,needsWater=True,produces={"food":5},bonusDeposit="fish",bonusPercent=100,groups=["food"])
b("raft_landing","Raft landing","rafts",2,{"wood":40},20,needsWater=True,crossing=True,produces={"food":2,"wood":1},groups=["food"])
b("farm","Farm","agriculture",4,{"wood":30,"tools":5},20,biomes=["grassland","steppe","savanna","deciduous_forest"],minFertility=40,produces={"food":6},bonusDeposit="wild_grain",bonusPercent=50,winterPercent=50,groups=["food","farm"])
b("potter","Potter","pottery",2,{"wood":20,"stone":10},20,consumes={"clay":2,"wood":1},produces={"pottery":1},groups=["craft"])
b("granary","Granary","pottery",0,{"wood":30,"clay":30},25,storage=250,groups=["store"])
b("pasture","Pasture","animal_husbandry",2,{"wood":30},20,biomes=["grassland","steppe","savanna","tundra"],produces={"food":4,"hides":2},bonusDeposit="cattle",bonusPercent=100,groups=["food"])
b("stone_house","Stone houses","masonry",0,{"stone":40,"wood":10},25,housing=16,warm=True,groups=["house"])
b("mason","Mason","masonry",3,{"wood":30,"tools":10},25,produces={},supportGroup="quarry",supportPercent=25,groups=["care"])
b("weaver","Weaver","weaving",2,{"wood":30,"tools":5},25,consumes={"hides":1,"food":1},produces={"textiles":1},groups=["craft"])
b("trading_post","Trading post","barter",2,{"wood":40,"pottery":10},30,storage=150,produces={},groups=["store","trade"])
b("copper_mine","Copper mine","copper_working",4,{"wood":40,"tools":15},30,orDeposit="copper",produces={"copper":2},groups=["tools","mine"])
# Bronze
b("tin_mine","Tin mine","bronze_working",4,{"wood":40,"tools":15},30,orDeposit="tin",produces={"tin":2},groups=["tools","mine"])
b("bronze_smith","Bronze smith","bronze_working",3,{"stone":40,"clay":30},30,consumes={"copper":2,"tin":1,"wood":1},produces={"bronze":1},groups=["craft"])
b("armourer","Armourer","bronze_weapons",2,{"stone":40,"wood":30},30,consumes={"bronze":1,"wood":1},produces={"bronze_weapons":1,"bronze_armour":0.5},groups=["craft","armoury"])
b("school","School","writing",3,{"stone":60,"clay":40},40,produces={},researchPercent=15,groups=["learn"])
b("stable","Stable","horseback_riding",2,{"wood":60},30,consumes={"food":2},produces={"horses":0.5},bonusDeposit="horses",bonusPercent=100,goodLimit={"horses":16},groups=["food"])
b("harbour","Harbour","sailing",3,{"wood":80,"stone":20},40,needsWater=True,produces={"food":4},groups=["food"])
b("mint","Mint","currency",2,{"stone":60,"wood":20},35,consumes={"copper":1},produces={"coins":10},groups=["craft"])
b("barracks","Barracks","barracks",2,{"wood":80,"stone":40},40,produces={},trainPercent=50,groups=["army","armoury"])
# Iron
b("iron_mine","Iron mine","iron_working",4,{"wood":50,"tools":20},35,orDeposit="iron_ore",produces={"iron":1},groups=["tools","mine"])
b("blacksmith","Blacksmith","iron_working",2,{"stone":40,"wood":30},30,consumes={"iron":1,"wood":1},produces={"tools":2},groups=["craft"])
b("weaponsmith","Weaponsmith","iron_weapons",2,{"stone":40,"wood":30},30,consumes={"iron":1,"wood":1},produces={"iron_weapons":1.5,"iron_armour":1},groups=["craft","armoury"])
b("builders_yard","Builders' yard","mathematics",3,{"stone":80,"wood":60},40,produces={},buildPercent=30,groups=["care"])
b("great_storehouse","Great storehouse","construction",0,{"stone":150,"wood":80},45,storage=1000,groups=["store"])
b("province_office","Province office","civil_service",3,{"stone":120,"wood":60},50,produces={},groups=["govern"])
b("siege_workshop","Siege workshop","engineering",3,{"wood":100,"tools":30},45,produces={},trainPercent=70,groups=["army"])
b("bathhouse","Bathhouse","medicine",2,{"stone":80,"pottery":20},40,produces={},resistEpidemicPercent=30,growthBonus=2,groups=["care"])
b("academy","Academy","philosophy",3,{"stone":120,"textiles":20},50,produces={},researchPercent=20,groups=["learn"])
b("shipyard","Shipyard","shipbuilding",4,{"wood":150,"tools":30},50,needsWater=True,produces={},trainPercent=60,groups=["army"])
# Middle ages
b("manor","Manor","feudalism",4,{"stone":200,"wood":100},60,produces={"food":8},housing=10,winterPercent=50,groups=["food","farm"])
b("steelworks","Steelworks","steel",3,{"stone":150,"iron":40},60,consumes={"iron":2,"wood":2},produces={"tools":4,"steel_weapons":1,"steel_armour":1},groups=["craft","armoury"])
b("castle","Castle","castles",0,{"stone":400,"wood":100,"iron":30},90,defensePercent=220,housing=10,groups=["fort","armoury"])
b("mill","Mill","mills",2,{"wood":80,"stone":60,"tools":10},40,needsWater=True,produces={},supportGroup="farm",supportPercent=20,groups=["care"])
b("guildhall","Guild hall","guilds",3,{"stone":150,"textiles":30},50,produces={},supportGroup="craft",supportPercent=25,groups=["care"])
b("university","University","university",4,{"stone":200,"textiles":40,"pottery":40},60,produces={},researchPercent=25,groups=["learn"])
for x in B:
    x.setdefault("produces",{}) if x["workers"]>0 else None
# Places on a hex (decision of 2026-11-18): 4 per hex. Fields, mines, quarries, harbours and castles take the whole hex (they use
# the land itself); stores, stables and halls take two; houses and workshops one. Huts that gather from the land take two, so
# that a deposit is not worked by four of them at once.
WHOLE={"farm","pasture","quarry","copper_mine","tin_mine","iron_mine","fishing_hut","harbour","raft_landing","castle","manor","shipyard","great_storehouse"}
TWO={"gatherers_hut","woodcutter","hunters_lodge","clay_pit","storehouse","granary","stable","barracks","trading_post","siege_workshop","bathhouse","academy","university","guildhall","steelworks","mill","province_office","builders_yard"}
for x in B:
    x["size"]=4 if x["key"] in WHOLE else 2 if x["key"] in TWO else 1
# Upgrades in place (Patryk, 2026-11-19): a building becomes its better kind once that is known; half of its own price counts
# toward the new one (upgradeCreditPercent), the rest porters bring as for any construction. A bigger kind needs the free places.
UP={"hut":"stone_house","storehouse":"great_storehouse","gatherers_hut":"farm","hunters_lodge":"pasture","fishing_hut":"harbour",
    "farm":"manor","toolmaker":"blacksmith","blacksmith":"steelworks","armourer":"weaponsmith","school":"academy","academy":"university"}
for x in B:
    if x["key"] in UP: x["upgradesTo"]=UP[x["key"]]
d["upgradeCreditPercent"]=50
# Combat (Patryk, 2026-11-19, claude/walka-propozycja.md): every number here.
d["combat"]={"comment":"Battle in steps of stepTicks. A unit fights every enemy unit next to it (ranged: within range, only while standing). Per step it kills engaged people x strength x weapon / armour of the target x terrain x flank x charge x bonus against the target's kind x fed x luck / killDivisor. Engaged people: at most frontPerContact for each enemy it touches. weapon and armour: multipliers for levels 0-4 (armourVsRangedBonus: armour counts this much more against arrows). Flank: damage taken by how many enemy units touch the target (index = count, last for more). Terrain: per level of relief higher than the target (flat 0, hills 1, mountains 2) reliefPercent, at most reliefMaxPercent; per elevationStep of height above it elevationPercent, at most elevationMaxPercent. Cover of the target's hex (forest or grove, palisade or wall) against ranged and melee, lost when enemies touch it from opposite sides. Attacking across a river without a bridge: riverPercent. Roads and swamps: openPercent more taken. Leaving enemies: each enemy next to it strikes once, disengagePercent of a step (cavalry cavalryDisengagePercent), unless another unit of ours stays next to that enemy. Charge: cavalry that walked chargeHexes or more before contact strikes chargePercent once (not against units that stop charges). One army unit to a hex: a unit that stops on another steps aside. Zone of control: a unit stops when it comes next to an enemy, and from there goes on only one hex at a time. Weapons only (shock): no armour, shockTrainPercent of the time. Single player: a battle after calmSeconds of calm slows time to x1. A unit falling to warnPercents of its full size is told in the chronicle. A unit with no way to its target searches again after chaseRetrySeconds and gives it up after chaseGiveUp failures.",
  "stepTicks":5,"killDivisor":400,"frontPerContact":12,
  "weapon":[60,100,140,180,220],"armour":[100,130,170,210,260],"armourVsRangedBonus":20,
  "flank":[100,100,125,150,180],
  "reliefPercent":8,"reliefMaxPercent":16,"elevationStep":20,"elevationPercent":3,"elevationMaxPercent":9,
  "cover":{"trees":{"ranged":-30,"melee":-15},"palisade":{"ranged":-50,"melee":-30},"wall":{"ranged":-50,"melee":-30}},
  "riverPercent":-25,"openPercent":15,"openBiomes":["swamp"],
  "disengagePercent":50,"cavalryDisengagePercent":25,
  "chargeHexes":2,"chargePercent":200,
  "shockTrainPercent":40,"calmSeconds":30,"warnPercents":[50,25],"chaseRetrySeconds":5,"chaseGiveUp":3}
# Choosing the start (Patryk, 2026-11-19: every number in the data).
d["startPhase"]={"comment":"pickSeconds: time a player has to choose. blockRadius: hexes around a taken start where no other people may start. blockFuzz: the shown circle is centred up to this many hexes from the real start and is this much wider, so it hides the start. scoreRadius, roomRadius: hexes looked at when scoring a start (its own land, the room around).",
  "pickSeconds":30,"blockRadius":10,"blockFuzz":3,"scoreRadius":3,"roomRadius":6}
d['buildings']=B

U=[]
def u(key,name,tech,people,price,secs,perHex,sight,**kw):
    x={"key":key,"name":name,"tech":tech,"people":people,"price":price,"trainSeconds":secs,"ticksPerHex":perHex,"sight":sight}; x.update(kw)
    if tech is None: x.pop("tech")
    U.append(x)
u("settlers","Settlers","migration",8,{"food":40,"wood":30},10,6,3,founds=True)
u("scouts","Scouts","scouting",2,{"food":15},5,3,5,hiddenTicksPerHex=6)
# Prospectors and surveyors (Patryk, 2026-11-19): beyond our land only they find what lies in the ground and how good the soil is.
u("prospectors","Prospectors","stone_tools",2,{"food":15,"wood":5},8,4,3,survey="rock")
u("surveyors","Surveyors","scouting",2,{"food":15},6,4,3,survey="soil")
# Porters carry 5 in their hands; Carrying gives baskets (+15 each). Caravans (pack animals) come with Animal husbandry.
u("porters","Porters","foraging",1,{},0,5,2,system=True,cargoPerPerson=5,groups=["carriers"])
# Caravans need the wheel and horses (decision of 2026-11-18): each takes horsesPerCaravan horses, which come back with it.
u("caravan","Caravan","the_wheel",2,{},0,6,3,system=True,cargoPerPerson=25,alsoTechs=["horseback_riding"],horsesPerCaravan=2,groups=["carriers"])
# Residents: the people of a settlement taking up arms against raiders. No technology, weak (decision of 2026-11-18).
# Army units (Patryk, 2026-11-19): strength is drill and kind; weapons and armour (levels 0-4) come from the stores when they
# train. people is the default size; the player picks minPeople-maxPeople, and the price follows the size.
u("levy","Residents",None,10,{},3,7,2,army=True,levy=True,strength=5,groups=["melee"])
u("hunters","Hunters","hunting",8,{"food":20,"wood":10},10,5,3,army=True,strength=6,range=2,becomes={"archery":"archers"},bonusBiomes=FOREST[:3],bonusPercent=20,groups=["ranged","infantry"])
u("warriors","Warriors","flint_weapons",10,{"food":30,"wood":10},12,6,3,army=True,strength=9,bonusBiomes=FOREST[:3],bonusPercent=30,groups=["melee","infantry"])
u("archers","Archers","archery",8,{"food":30,"wood":25},15,6,4,army=True,strength=9,range=3,bonusRelief=["hills","mountains"],bonusPercent=40,groups=["ranged","infantry"])
u("spearmen","Spearmen","bronze_weapons",10,{"food":40},18,6,3,army=True,strength=11,bonusVs={"cavalry":50},stopsCharge=True,groups=["melee","infantry","spear"])
u("horsemen","Horsemen","horseback_riding",6,{"food":40,"horses":6},20,3,5,army=True,strength=12,bonusRelief=["flat"],bonusPercent=20,bonusVs={"ranged":50},groups=["cavalry"])
u("transport","Transport ship","sailing",8,{"wood":80,"food":30},25,4,4,army=True,naval=True,strength=4,carriesArmies=True,carries=20,needsBuilding="harbour",groups=["ship"])
u("trade_ship","Trade ship","sailing",4,{},0,4,3,system=True,naval=True,cargoPerPerson=40,groups=["carriers","ship"])
u("swordsmen","Swordsmen","iron_weapons",10,{"food":50},20,6,3,army=True,strength=13,groups=["melee","infantry"])
u("catapult","Catapult","engineering",6,{"wood":100,"tools":20},30,9,3,army=True,strength=8,range=2,siege=300,needsBuilding="siege_workshop",groups=["siege"])
u("warship","Warship","shipbuilding",12,{"wood":150,"iron":10},35,3,5,army=True,naval=True,strength=22,range=2,carriesArmies=True,carries=30,needsBuilding="shipyard",groups=["ship"])
u("crossbowmen","Crossbowmen","crossbows",8,{"food":40,"wood":20},20,6,4,army=True,strength=12,range=2,pierce=1,groups=["ranged","infantry"])
u("knights","Knights","chivalry",6,{"food":60,"horses":6},30,3,5,army=True,strength=16,bonusRelief=["flat"],bonusPercent=20,bonusVs={"ranged":50},groups=["cavalry"])
# Sight (decision of 2026-11-18: units saw too much): scouts, riders and ships 3, carriers 1, the rest 2.
for x in U:
    if x.get("army") and not x.get("naval") and not x.get("levy") and "siege" not in x.get("groups",[]):
        x["minPeople"]=5; x["maxPeople"]=30
SIGHT={"scouts":3,"prospectors":3,"surveyors":3,"horsemen":3,"knights":3,"transport":3,"warship":3,"porters":1,"caravan":1,"trade_ship":2}
for x in U:
    x["sight"]=SIGHT.get(x["key"],2)
d['units']=U

T=[]
def t(key,name,era,exp=None,price=None,secs=0,gives="",effects=None,upgrade=False,known=False,req=None,made=None):
    x={"key":key,"name":name,"era":era,"gives":gives}
    if req: x["requires"]=req
    if made: x["made"]=made
    if known: x["known"]=True
    if exp: x["experience"]=exp
    if price: x["price"]=price
    if secs: x["seconds"]=secs
    if effects: x["effects"]=[{"kind":k,"target":tg,"value":v} for (k,tg,v) in effects]
    if upgrade: x["upgrade"]=True
    T.append(x)
S,N,BR,IR,M="stone_age","neolithic","bronze_age","iron_age","middle_ages"
# Stone age (decision of 2026-11-18). Basics are cheap and quick and do not count toward the next era; the seven others do.
t("foraging","Foraging",S,known=True,gives="Gatherers' huts and woodcutters; the people of a settlement can gather food, wood and stone around it.")
t("dwellings","Dwellings",S,{"foraging":3},{"wood":10},10,"Huts.")
t("stockpiling","Stockpiling",S,{"foraging":5},{"wood":15},12,"Storehouses.")
t("carrying","Carrying",S,{"foraging":5},{"wood":10,"food":10},12,"Baskets: porters carry 20 instead of 5; tracks.",[("cargo","porters",15)])
t("scouting","Scouting",S,{"foraging":3},{"food":10},8,"Scouts.")
t("migration","Migration",S,{"foraging":10},{"food":20,"wood":10},15,"Settlers.")
t("fire","Fire",S,{"foraging":10},{"wood":30},20,"Hearths; gatherers +10%.",[("production","gatherers_hut",10)])
t("stone_tools","Stone tools",S,{"foraging":20},{"food":40,"wood":20},30,"Quarries, toolmakers.")
t("hunting","Hunting",S,{"foraging":15},{"food":30,"wood":20},25,"Hunters' lodges; hunters with spears and slings.")
t("shelters","Shelters",S,{"foraging":25},{"wood":40},30,"Huts give 2 more homes; clay pits.",[("housing","hut",2)])
t("herbal_lore","Herbal lore",S,{"hunting":15},{"food":40},30,"Herbalists: hunters near them +25%, the wounded heal; growth a little faster; epidemics 15% milder.",[("growth","*",1),("resist","epidemic",15)])
t("sewing","Sewing",S,{"hunting":20,"stone_tools":10},{"hides":15,"food":30},30,"Tailors: clothing makes workers faster, most of all in winter.")
t("flint_weapons","Flint weapons",S,{"stone_tools":15},{"stone":20,"wood":10},20,"Warriors with wooden spears, clubs and slings.")
# Neolithic. Experience is worker-minutes in a technology's buildings or goods made ("made"); "requires": technologies needed besides (decision of 2026-11-18).
t("agriculture","Agriculture",N,{"foraging":40,"stone_tools":10},{"food":80,"wood":40,"stone":20},60,"Farms (the more fertile the land, the more they give); camps can become villages.")
t("pottery","Pottery",N,{"agriculture":15,"shelters":10},{"clay":40,"wood":30},45,"Potters (pottery, used in many later things), granaries.")
t("animal_husbandry","Animal husbandry",N,{"agriculture":20,"hunting":20},{"food":100,"wood":40},50,"Pastures (twice as good on cattle).")
t("masonry","Masonry",N,{"stone_tools":40,"agriculture":15},{"stone":80,"tools":20},60,"Stone houses (more homes, warmer in winter); masons (quarries near them dig more); bridges where roads cross rivers.")
t("weaving","Weaving",N,{"animal_husbandry":20},{"hides":30,"tools":10},50,"Weavers (textiles).",made={"clothing":20})
t("irrigation","Irrigation",N,{"agriculture":40},{"stone":60,"tools":15},55,"Irrigation ditches drawn from water (at most 4 hexes): farms on watered hexes +25%; droughts 40% milder.",[("resist","drought",40)])
t("barter","Barter",N,{"pottery":15},{"pottery":20,"food":60},50,"Trading posts; trade with other peoples (one agreement per trading post).",made={"pottery":30})
t("palisade","Palisade",N,{"stone_tools":30},{"wood":80},40,"Palisades drawn on hexes: they wound enemies attacking through them.")
t("copper_working","Copper working",N,{"masonry":20,"pottery":20},{"stone":60,"clay":40,"tools":30},70,"Copper mines.",made={"tools":30})
t("fishing","Fishing",N,{"hunting":15},{"food":30,"wood":20},25,"Fishing huts by rivers and the sea.")
t("rafts","Rafts",N,{"fishing":15},{"wood":50},30,"Raft landings: our people cross the river next to them.")
t("polished_stone","Polished stone tools",N,{"masonry":20},{"stone":60,"tools":10},40,"Quarries, woodcutters, clay pits +15%.",[("production","tools",15)],True)
t("hide_shields","Hide shields",N,{"animal_husbandry":20},{"hides":40},40,"Warriors and residents +15%.",[("strength","melee",15)],True)
t("hafted_weapons","Hafted weapons",N,{"stone_tools":30},{"stone":30,"tools":5},30,"Warriors and residents +20%.",[("strength","melee",20)],True)
# Bronze
t("bronze_working","Bronze working",BR,{"copper_working":30},{"copper":40,"wood":80,"tools":30},80,"Tin mines, bronze smiths.",made={"copper":30})
t("bronze_weapons","Bronze weapons",BR,{"bronze_working":15},{"bronze":20,"wood":40},50,"Armourers (arms), spearmen.",made={"bronze":20})
t("the_wheel","The wheel",BR,{"agriculture":60,"masonry":20},{"wood":120,"tools":20},70,"With Horseback riding: caravans (they take horses). Porters and caravans 50% faster.",[("speed","carriers",50)])
t("writing","Writing",BR,{"pottery":40},{"clay":80,"food":100},70,"Schools: every worker speeds one branch of research we choose (+15%).",made={"pottery":40})
t("horseback_riding","Horseback riding",BR,{"animal_husbandry":40},{"food":150,"hides":40},70,"Stables (horses, at most 16 each), horsemen.")
t("sailing","Sailing",BR,{"fishing":40,"rafts":20},{"wood":150,"textiles":20},70,"Harbours; transport ships (carry armies) and trade ships (carry goods).")
t("currency","Currency",BR,{"copper_working":20},{"copper":30,"pottery":30},70,"Mints (coins from copper); pay and be paid in coins; research −5%.",[("research","*",5)],req=["barter"])
t("code_of_laws","Code of laws",BR,{"writing":10},{"clay":60,"food":80},60,"A town office: with Masonry, villages can become towns.")
t("archery","Archery",BR,{"hunting":40},{"wood":80,"hides":30},60,"Bows: hunters become archers (stronger, longer reach).")
t("barracks","Barracks",BR,{"writing":10},{"wood":80,"stone":40},50,"Barracks: soldiers wait there between wars; training twice as fast.",req=["code_of_laws"])
t("stone_walls","Stone walls",BR,{"masonry":40},{"stone":150},60,"Stone walls drawn on hexes: enemies cannot pass until they break them.",upgrade=True)
t("bronze_age_arms","Bronze age arms",BR,{"bronze_weapons":20},{"bronze":40,"hides":20},60,"Better weapons and armour: melee +30%, infantry +20%.",[("strength","melee",30),("strength","infantry",20)],True)
t("bronze_tools","Bronze tools",BR,{"bronze_working":25},{"bronze":30,"tools":20},55,"Quarries, woodcutters, mines +20%.",[("production","tools",20)],True)
# Iron
t("iron_working","Iron working",IR,{"bronze_working":50},{"bronze":40,"wood":120,"tools":40},90,"Iron mines, blacksmiths (tools from iron).",made={"bronze":40})
t("iron_weapons","Iron weapons",IR,{"iron_working":20},{"iron":40,"wood":60},70,"Weaponsmiths (arms from iron), swordsmen.")
t("mathematics","Mathematics",IR,{"writing":40},{"clay":100,"food":150},80,"Builders' yards: their workers make building in their settlement up to 30% faster.")
t("construction","Construction",IR,{"masonry":60,"mathematics":10},{"stone":200,"wood":150,"tools":60},100,"Great storehouses; with Civil service, towns can become cities.")
t("civil_service","Civil service",IR,{"writing":60},{"clay":120,"food":150},90,"Province offices; provinces; a larger reach of the administration; with Construction, towns can become cities.",req=["code_of_laws"])
t("roads","Roads",IR,{"mathematics":10,"masonry":30},{"stone":150,"tools":30},70,"Paved roads: much faster than tracks, also over hills; major trade routes.",req=["the_wheel","construction"])
t("engineering","Engineering",IR,{"mathematics":20,"masonry":40},{"wood":200,"tools":60,"iron":20},90,"Siege workshops (their workers build engines faster), catapults; aqueducts drawn from water to towns.",req=["construction"])
t("medicine","Medicine",IR,{"herbal_lore":40,"writing":30},{"food":150,"pottery":40},80,"Bathhouses: in their settlement epidemics are milder and people grow faster.")
t("philosophy","Philosophy",IR,{"writing":60,"mathematics":20},{"clay":150,"textiles":40},90,"Academies (every worker speeds one branch of research, +20%); rarer thinkers and advisors.",[("luck","*",40)])
t("shipbuilding","Shipbuilding",IR,{"sailing":40,"iron_working":20},{"wood":250,"iron":30},90,"Shipyards, warships (fight at sea, carry armies); larger trade ships.")
t("iron_age_arms","Iron age arms",IR,{"iron_weapons":30},{"iron":60,"hides":30},70,"Better weapons and armour: melee +30%, infantry and cavalry +25%.",[("strength","melee",30),("strength","infantry",25),("strength","cavalry",25)],True)
t("iron_tools","Iron tools",IR,{"iron_working":30},{"iron":40,"tools":30},65,"Quarries, woodcutters, mines +25%; farms +15%.",[("production","tools",25),("production","farm",15)],True)
# Middle ages
t("feudalism","Feudalism",M,{"civil_service":20,"horseback_riding":30},{"food":300,"iron":60,"stone":200},120,"Manors; a larger reach of the administration.",req=["construction"])
t("chivalry","Chivalry",M,{"feudalism":20},{"food":200,"iron":60},90,"Knights (they take horses).",req=["horseback_riding"])
t("steel","Steel",M,{"iron_working":60},{"iron":120,"wood":200,"tools":80},120,"Steelworks.",made={"tools":100})
t("castles","Castles",M,{"masonry":60},{"stone":400,"iron":40},110,"Castles (+220% defence).",req=["stone_walls","construction"])
t("heavy_plough","Heavy plough",M,{"agriculture":200,"iron_working":40},{"iron":60,"wood":150},90,"Farms also on poorer land; farms +15%.",[("production","farm",15),("fertility","farm",15)])
t("crossbows","Crossbows",M,{"iron_working":40},{"iron":60,"wood":100},90,"Crossbowmen.",req=["archery"])
t("guilds","Guilds",M,{"currency":60,"weaving":60},{"textiles":80,"pottery":60},100,"Guild halls: workshops in their settlement +25% while they work.")
t("mills","Mills",M,{"agriculture":200},{"stone":200,"wood":150},90,"Mills by rivers: farms in their settlement +20% while they work.",req=["the_wheel"])
t("university","University",M,{"philosophy":40,"writing":120},{"stone":300,"textiles":80},120,"Universities (every worker speeds one branch of research, +25%).")
t("medieval_arms","Medieval arms",M,{"steel":30},{"iron":120,"textiles":30},100,"Steel weapons and plate armour: melee, infantry and cavalry +30%.",[("strength","melee",30),("strength","infantry",30),("strength","cavalry",30)],True)
t("crop_rotation","Crop rotation",M,{"agriculture":120},{"food":200,"tools":40},70,"Farms +25%.",[("production","farm",25)],True,req=["irrigation"])
t("longbow","Longbow",M,{"hunting":60},{"wood":150,"iron":20},80,"Archers and crossbowmen +30%.",[("strength","ranged",30)],True,req=["archery"])
t("trebuchet","Trebuchet",M,{"engineering":40},{"wood":250,"iron":40},90,"Siege engines +100% against settlements.",[("siege","*",100)],True)
for x in T:
    if "experience" in x:
        x["experience"]={k:v for k,v in x["experience"].items() if v>0}
CAT={
"foraging":"basics","dwellings":"basics","stockpiling":"basics","carrying":"basics","scouting":"basics","migration":"basics",
"fire":"food","stone_tools":"industry","hunting":"food","shelters":"building","herbal_lore":"science","sewing":"industry","flint_weapons":"military",
"agriculture":"food","pottery":"industry","animal_husbandry":"food","masonry":"building","weaving":"industry","irrigation":"food","barter":"trade","palisade":"military","copper_working":"industry","fishing":"food","rafts":"trade","polished_stone":"industry","hide_shields":"military","hafted_weapons":"military",
"bronze_working":"industry","bronze_weapons":"military","the_wheel":"trade","writing":"science","horseback_riding":"military","sailing":"trade","currency":"trade","code_of_laws":"governance","archery":"military","barracks":"military","stone_walls":"military","bronze_age_arms":"military","bronze_tools":"industry",
"iron_working":"industry","iron_weapons":"military","mathematics":"science","construction":"building","civil_service":"governance","roads":"trade","engineering":"building","medicine":"science","philosophy":"science","shipbuilding":"trade","iron_age_arms":"military","iron_tools":"industry",
"feudalism":"governance","chivalry":"military","steel":"industry","castles":"military","heavy_plough":"food","crossbows":"military","guilds":"industry","mills":"food","university":"science","medieval_arms":"military","crop_rotation":"food","longbow":"military","trebuchet":"military"
}
for x in T:
    x["category"]=CAT[x["key"]]
d["techCategories"]=["basics","food","industry","building","trade","military","governance","science"]
d["eraAdvanceTechs"]=5
# A new camp starts with a gatherers' hut, paid from the settlers' provisions (Patryk, 2026-11-19, G1).
d["foundWith"]=["gatherers_hut"]
# Ruins (Patryk, 2026-11-19, G2): a settlement empty for emptySeconds becomes a ruin, nobody's; settlers standing on or next to its
# centre take it again, and its buildings come back as sites that need rebuildPercent of their price.
d["ruins"]={"comment":"A settlement empty for emptySeconds (not the last of its people) becomes a ruin: its land is nobody's, its buildings stand ruined. Settlers on or next to its centre take it: the land is claimed again and every ruined building is a site that needs rebuildPercent of its price.","emptySeconds":60,"rebuildPercent":50}
# Every start has copper and tin near it (Patryk, 2026-11-19, M4): placed on free land when the map has none within max hexes.
d["startDeposits"]=[{"deposit":"copper","min":3,"max":7,"depth":1},{"deposit":"tin","min":5,"max":12,"depth":1}]
# Gateways (Patryk, 2026-11-19, M1): to leave an era a people must also know its gateway technology (metal opens the next era).
d["eraGateways"]={"comment":"Era key: technology of that era that must be known (besides eraAdvanceTechs of it) to enter the next era.",
  "neolithic":"copper_working","bronze_age":"bronze_working","iron_age":"iron_working"}
# Roads are part of the map, drawn hex by hex (decision of 2026-11-18): tracks from the start, paved roads with Roads.
d["roads"]={"comment":"Levels of roads. walkPercent: walking cost on them, of the hex's cost (capped at 150 so roads cross rough land). reach: hexes from our nearest settlement. supply: goods carriers bring to a new camp joined to our roads.",
  "levels":[{"key":"track","name":"Track","tech":"carrying","price":{"wood":2},"seconds":2,"walkPercent":60},
            {"key":"road","name":"Road","tech":"roads","price":{"stone":3,"wood":1},"seconds":4,"walkPercent":35}],
  "reach":25,"supply":{"food":40,"wood":60,"stone":30,"clay":20}}
d.pop("tripFoodPer10Hexes", None)
d["survey"]={"comment":"What is known of the land (Patryk, 2026-11-19). On our own land everything is known. Beyond it, seeing a hex shows only the surface deposits; the rest is found by units that survey (units[].survey): standing still, they look at every hex within radius. rock: prospectors find stone, clay, flint and ores, each ore only from the era in eras (a people recognises an ore once it is in the era of the technology that works it, even before it knows the technology); ores not listed are never found in these eras. soil: surveyors find fertility and the deposits in soil.",
  "radius":3,"surface":["game","fish","horses"],"soil":["wild_grain","cattle","sheep"],
  "eras":{"flint":"stone_age","stone":"stone_age","clay":"stone_age","salt":"neolithic","copper":"neolithic","tin":"bronze_age","gold":"bronze_age","silver":"bronze_age","iron_ore":"iron_age","marble":"iron_age","saltpeter":"middle_ages"}}
d['comment'] = d['comment'].replace("Food: every person eats foodPerPersonPerMinute;", "Food: every person eats foodPerPersonPerMinute, paid once a minute at the ration (see rations);").replace(" without food it loses starvePercentPerMinute.", " it does not grow while it has no food (deaths from hunger come at the rations; starvePercentPerMinute is not used).")
# Rations (Patryk, 2026-11-19): no running cost of units, as in RTS games. Every minute each settlement feeds all its people:
# its residents and the units that come from it. Short of food, the idle go hungry first, then the workers, the units last;
# a second missed ration in a row kills some of the hungry.
d["rations"]={"comment":"Every everySeconds a settlement feeds everyone it has: its residents and its units (soldiers, scouts, carriers, settlers on the way), each eating food.foodPerPersonPerMinute for the time since the last ration. Short of food, its units are fed first (fighting ones first), then its workers, then the people without a job: those go hungry first. Hungry people work, gather, fight and walk at hungryPercent. Missed again at the next ration: starvingPercent, and starveLosePercent of the hungry (at least 1) die then and at every further missed ration. One full ration ends it.",
  "everySeconds":60,"hungryPercent":75,"starvingPercent":50,"starveLosePercent":20}
# Trees (Patryk, 2026-11-18 19:35: too little wood, too few trees): forests are full of trees; on open land groves grow in
# clumps, groveChance percent of each biome's hexes. Woodcutters work groves too (at groveWoodPercent), gatherers pick up
# their wood the same.
d["trees"]={"comment":"Groves on open land, in clumps: groveChance percent of a biome's land hexes. A grove gives groveWoodPercent of a forest's wood to woodcutters and gatherers.",
  "groveChance":{"grassland":50,"savanna":40,"steppe":25,"tundra":15,"swamp":50},"groveWoodPercent":60}
# Works on hexes (decision of 2026-11-18): drawn hex by hex like roads, kept under whatever is built on the hex, removed only
# by the player. Ditches and aqueducts run from water (fromWater: their longest reach). Palisades wound enemies standing on
# them; walls stop enemies until broken (strength). Bridges come by themselves where a road crosses a river, after bridgeTech.
d["works"]={"comment":"Works drawn on hexes; price and seconds per hex. ditch: farms on watered hexes +farmPercent. palisade: enemies at war standing on it lose woundPercentPerSecond of their people a second. wall: foreign units cannot enter until its strength (hundredths of damage) is broken. aqueduct: a settlement it reaches grows growthBonus% a minute faster and suffers resistEpidemicPercent less from epidemics.",
  "kinds":[
    {"key":"ditch","name":"Irrigation ditch","tech":"irrigation","price":{"wood":2,"stone":1},"seconds":3,"fromWater":4,"farmPercent":25},
    {"key":"palisade","name":"Palisade","tech":"palisade","price":{"wood":6},"seconds":4,"woundPercentPerSecond":2},
    {"key":"wall","name":"Stone wall","tech":"stone_walls","price":{"stone":12},"seconds":8,"strength":60},
    {"key":"aqueduct","name":"Aqueduct","tech":"engineering","price":{"stone":10,"tools":1},"seconds":6,"fromWater":12,"growthBonus":2,"resistEpidemicPercent":30}],
  "bridgeTech":"masonry","bridgePrice":{"stone":20,"wood":10}}
# Winter, clothing and gathering (decision of 2026-11-18).
d["seasons"]={"comment":"Winter comes every cycleMinutes and lasts winterMinutes (no years: playability first). Work is rounded down to steps of workStepPermille. In winter every worker works at winterWorkPercent; buildings with winterPercent lose that much more (gathering and fields). Peoples are told warnSeconds before.",
  "workStepPermille":25,"cycleMinutes":8,"winterMinutes":2,"warnSeconds":30,"winterWorkPercent":50}
d["clothing"]={"comment":"Every set of clothing a worker has adds bonusPercent. Outside winter only the first set counts (at most 100+bonus); in winter more sets count, up to winterMaxPercent. Worked out per settlement: sets = clothing in store / people. Sets wear out: wearPerPersonPerMinute.",
  "good":"clothing","bonusPercent":15,"winterMaxPercent":110,"wearPerPersonPerMinute":0.03}
d["forage"]={"comment":"People of a settlement told to gather pick up food, wood and stone around it. Per gatherer per minute at richness 100: perMinute. A hex's richness: food and wood by biome, stone by relief; deposits and rivers add. A settlement's richness is the mean over the land hexes in its radius (worked out when it is founded or grows).",
  "perMinute":{"food":0.5,"wood":0.25,"stone":0.1},
  "biomes":{"grassland":[100,10],"steppe":[70,0],"savanna":[80,10],"deciduous_forest":[80,100],"jungle":[90,80],"taiga":[50,100],"swamp":[60,40],"tundra":[30,0],"desert":[10,0],"ice":[0,0]},
  "relief":[5,50,100],"deposits":{"stone":{"stone":100},"flint":{"stone":50},"wild_grain":{"food":50},"game":{"food":30}},"riverFood":20}
d["food"].pop("idleForagePerMinute",None)
d["war"]["injuredPercent"]=30
# E1, E3 (Patryk, 2026-11-19): declaring war costs reputation; the AI pays tribute only after it lost tributeNeedsKills people
# to the war; food in a tribute is at most tributeFoodMaxPercent of the payer's food; truceSeconds after peace.
d["war"].update({"declareReputation":-10,"tributeNeedsKills":5,"tributeFoodMaxPercent":10,"truceSeconds":180})
if "declareReputation" not in d["war"]["comment"]:
    d["war"]["comment"]+=" declareReputation: reputation every declaration of war costs. tributeNeedsKills: people a people must have lost to the war before its AI agrees to pay tribute. tributeFoodMaxPercent: most food (percent of the payer's) a tribute takes. truceSeconds: truce after peace."
d["war"].pop("soldierFoodPerMinute", None)
d["war"]["comment"]=d["war"]["comment"].replace(" Soldiers eat soldierFoodPerMinute each.", " Soldiers are fed by rations (see rations).")
d["rivers"]={"comment":"Until freeFromEra, rivers can be crossed only next to one's own building that gives a crossing (raft landings).","freeFromEra":"bronze_age"}
d['technologies']=T

# remove old unit upgrades fields (now effects); keep terrain bonus
d["research"]["maxSupportPercent"]=50
# Pace of research by era (2026-11-19: about 60 minutes to the Middle Ages): percent of the price, of the experience and goods
# made asked for, and of the time, for the technologies of each era (stone, neolithic, bronze, iron, middle ages).
d["research"]["pace"]={"pricePercent":[40,35,25,25,25],"experiencePercent":[40,35,22,22,22],"secondsPercent":[60,50,35,35,35]}
if "pace:" not in d["research"]["comment"]:
    d["research"]["comment"]+=" pace: percent of the price, of the experience and goods made asked for, and of the time of the technologies of each era (index = era)."
if "maxSupportPercent:" not in d["research"]["comment"]:
    d["research"]["comment"]+=" maxSupportPercent: schools and academies speed a branch of research at most this much."
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
    for k in x.get('requires',[]):
        if k not in keys: probs.append(('requires',x['key'],k))
    for k in x.get('made',{}):
        if k not in {g['key'] for g in d['goods']}: probs.append(('made',x['key'],k))
for x in B+U:
    if x.get('tech','foraging') not in keys: probs.append(('tech',x['key'],x.get('tech')))
base=[x for x in T if not x.get('upgrade')]; up=[x for x in T if x.get('upgrade')]
print(len(base),len(up),probs)
from collections import Counter
print(Counter(x['era'] for x in base))
