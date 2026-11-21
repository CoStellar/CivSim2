# Drzewo technologii (stan z `data/sim/game.json`, gałąź `feat/stone-age`)

65 technologii: 6 podstawowych, 47 zwykłych i 12 ulepszeń ery (oznaczone „U”). Nazwy są takie jak w grze.

**Zasady badań (decyzje z 2026-11-18):**
- **Era** otwiera się, gdy lud zna 5 technologii swojej ery. Technologie podstawowe (gałąź „podstawy”) się nie liczą, ulepszenia tak.
- **Doświadczenie**: 1 punkt to 1 pracownik przez 1 minutę w budynkach danej technologii (Foraging: także ludzie zbierający z okolicy).
- **Wyprodukowane dobra**: niektóre technologie chcą zamiast tego wytworzonych dóbr, np. Barter 30 ceramiki.
- **Najpierw**: niektóre wymagają innej technologii bez doświadczenia w niej.
- Budynek, jednostka albo poziom osady może wymagać **dwóch technologii naraz** (karawany, town, city).
- Potem płaci się **cenę** i czeka się **czas**. Szkoły, akademie i uniwersytety skracają czas w wybranych gałęziach.

## Epoka kamienia (Stone Age)

Liczy się do progu: 7 technologii zwykłych i ulepszenia; podstawowe nie.

### Foraging — podstawy

- **Wymaga:** znana od startu. **Cena:** nic. **Czas:** 0 s.
- **Opis w grze:** Gatherers' huts and woodcutters; the people of a settlement can gather food, wood and stone around it.
- Budynek: **Gatherers' hut** (koszt 15 wood, 12 s): 3 miejsc pracy, każdy pracownik na minutę: 4 food, 1 wood; zimą jeszcze 50%; 2 miejsca na heksie; na złożu +100%
- Budynek: **Woodcutter** (koszt 15 wood, 12 s): 3 miejsc pracy, każdy pracownik na minutę: 4 wood; 2 miejsca na heksie
- Jednostka: **Porters** (1 os., koszt nic, 0 s, wysyłani przez grę (nie szkoli się ich))
- Jednostka: **Residents** (10 os., koszt nic, 3 s, siła 5, mieszkańcy, bez technologii, tylko do obrony)

### Dwellings — podstawy

- **Wymaga:** Foraging 3 pkt. **Cena:** 10 wood. **Czas:** 10 s.
- **Opis w grze:** Huts.
- Budynek: **Huts** (koszt 20 wood, 10 s): domy dla 8 osób; 1 miejsce na heksie

### Stockpiling — podstawy

- **Wymaga:** Foraging 5 pkt. **Cena:** 15 wood. **Czas:** 12 s.
- **Opis w grze:** Storehouses.
- Budynek: **Storehouse** (koszt 30 wood, 20 s): magazyn +300 każdego dobra; 2 miejsca na heksie

### Carrying — podstawy

- **Wymaga:** Foraging 5 pkt. **Cena:** 10 wood, 10 food. **Czas:** 12 s.
- **Opis w grze:** Baskets: porters carry 20 instead of 5; tracks.
- Droga: **Track** (2 wood i 2 s na heks, marsz 60% kosztu heksu)
- Efekt: cargo porters 15

### Scouting — podstawy

- **Wymaga:** Foraging 3 pkt. **Cena:** 10 food. **Czas:** 8 s.
- **Opis w grze:** Scouts.
- Jednostka: **Scouts** (2 os., koszt 15 food, 5 s)

### Migration — podstawy

- **Wymaga:** Foraging 10 pkt. **Cena:** 20 food, 10 wood. **Czas:** 15 s.
- **Opis w grze:** Settlers.
- Jednostka: **Settlers** (8 os., koszt 40 food, 30 wood, 10 s, zakłada osady)

### Fire — jedzenie

- **Wymaga:** Foraging 10 pkt. **Cena:** 30 wood. **Czas:** 20 s.
- **Opis w grze:** Hearths; gatherers +10%.
- Budynek: **Hearth** (koszt 15 wood, 10 stone, 10 s): domy dla 3 osób; 1 miejsce na heksie
- Efekt: produkcja: Gatherers' hut +10%

### Stone tools — przemysł

- **Wymaga:** Foraging 20 pkt. **Cena:** 40 food, 20 wood. **Czas:** 30 s.
- **Opis w grze:** Quarries, toolmakers.
- Budynek: **Quarry** (koszt 25 wood, 20 s): 4 miejsc pracy, każdy pracownik na minutę: 2 stone; cały heks; na złożu +100%; na hills/mountains +100%
- Budynek: **Toolmaker** (koszt 30 wood, 20 stone, 25 s): 2 miejsc pracy, każdy pracownik na minutę: 1 tools z 2 stone, 1 wood; 1 miejsce na heksie; na złożu +50%

### Hunting — jedzenie

- **Wymaga:** Foraging 15 pkt. **Cena:** 30 food, 20 wood. **Czas:** 25 s.
- **Opis w grze:** Hunters' lodges; hunters with spears and slings.
- Budynek: **Hunters' lodge** (koszt 20 wood, 15 s): 3 miejsc pracy, każdy pracownik na minutę: 3 food, 1 hides; 2 miejsca na heksie; na złożu +100%
- Jednostka: **Hunters** (8 os., koszt 20 food, 10 wood, 10 s, siła 6, strzela na 2, zmienia się: z Archery w archers)

### Shelters — budownictwo

- **Wymaga:** Foraging 25 pkt. **Cena:** 40 wood. **Czas:** 30 s.
- **Opis w grze:** Huts give 2 more homes; clay pits.
- Budynek: **Clay pit** (koszt 20 wood, 15 s): 3 miejsc pracy, każdy pracownik na minutę: 3 clay; 2 miejsca na heksie; na złożu +100%; przy wodzie
- Efekt: domy: Huts +2 miejsc

### Herbal lore — nauka

- **Wymaga:** Hunting 15 pkt. **Cena:** 40 food. **Czas:** 30 s.
- **Opis w grze:** Herbalists: hunters near them +25%, the wounded heal; growth a little faster; epidemics 15% milder.
- Budynek: **Herbalist** (koszt 20 wood, 10 clay, 20 s): 2 miejsc pracy, każdy pracownik na minutę: nic; wsparcie: budynki „hunt” w osadzie +25% przy pełnej obsadzie; leczy 6 rannych na minutę; 1 miejsce na heksie
- Efekt: przyrost +1% na minutę
- Efekt: epidemia o 15% łagodniejsza

### Sewing — przemysł

- **Wymaga:** Hunting 20 pkt, Stone tools 10 pkt. **Cena:** 15 hides, 30 food. **Czas:** 30 s.
- **Opis w grze:** Tailors: clothing makes workers faster, most of all in winter.
- Budynek: **Tailor** (koszt 20 wood, 5 tools, 20 s): 2 miejsc pracy, każdy pracownik na minutę: 1 clothing z 1 hides; 1 miejsce na heksie

### Flint weapons — wojsko

- **Wymaga:** Stone tools 15 pkt. **Cena:** 20 stone, 10 wood. **Czas:** 20 s.
- **Opis w grze:** Warriors with wooden spears, clubs and slings.
- Jednostka: **Warriors** (10 os., koszt 30 food, 10 wood, 12 s, siła 10)


## Neolit (Neolithic)

Liczy się do progu: 11 technologii zwykłych i ulepszenia; podstawowe nie.

### Agriculture — jedzenie

- **Wymaga:** Foraging 40 pkt, Stone tools 10 pkt. **Cena:** 80 food, 40 wood, 20 stone. **Czas:** 60 s.
- **Opis w grze:** Farms (the more fertile the land, the more they give); camps can become villages.
- Budynek: **Farm** (koszt 30 wood, 5 tools, 20 s): 4 miejsc pracy, każdy pracownik na minutę: 6 food; zimą jeszcze 50%; cały heks; na złożu +50%; żyzność od 40
- Poziom osady: **Village** (promień 4, domy 40, magazyn 200, awans za 120 wood, 60 stone, 30 clay; wymaga Agriculture)

### Pottery — przemysł

- **Wymaga:** Agriculture 15 pkt, Shelters 10 pkt. **Cena:** 40 clay, 30 wood. **Czas:** 45 s.
- **Opis w grze:** Potters (pottery, used in many later things), granaries.
- Budynek: **Potter** (koszt 20 wood, 10 stone, 20 s): 2 miejsc pracy, każdy pracownik na minutę: 1 pottery z 2 clay, 1 wood; 1 miejsce na heksie
- Budynek: **Granary** (koszt 30 wood, 30 clay, 25 s): magazyn +250 każdego dobra; 2 miejsca na heksie

### Animal husbandry — jedzenie

- **Wymaga:** Agriculture 20 pkt, Hunting 20 pkt. **Cena:** 100 food, 40 wood. **Czas:** 50 s.
- **Opis w grze:** Pastures (twice as good on cattle).
- Budynek: **Pasture** (koszt 30 wood, 20 s): 2 miejsc pracy, każdy pracownik na minutę: 4 food, 2 hides; cały heks; na złożu +100%

### Masonry — budownictwo

- **Wymaga:** Stone tools 40 pkt, Agriculture 15 pkt. **Cena:** 80 stone, 20 tools. **Czas:** 60 s.
- **Opis w grze:** Stone houses (more homes, warmer in winter); masons (quarries near them dig more); bridges where roads cross rivers.
- Budynek: **Stone houses** (koszt 40 stone, 10 wood, 25 s): domy dla 16 osób; ciepłe domy: zimą −35% pracy zamiast −50%; 1 miejsce na heksie
- Budynek: **Mason** (koszt 30 wood, 10 tools, 25 s): 3 miejsc pracy, każdy pracownik na minutę: nic; wsparcie: budynki „quarry” w osadzie +25% przy pełnej obsadzie; 1 miejsce na heksie
- Poziom osady: **Town** (promień 5, domy 60, magazyn 300, awans za 250 wood, 200 stone, 40 pottery, 30 clothing; wymaga Code of laws, Masonry)
- Mosty: drogi same przechodzą przez rzeki (most kosztuje 20 stone, 10 wood)

### Weaving — przemysł

- **Wymaga:** Animal husbandry 20 pkt; wyprodukować 20 clothing. **Cena:** 30 hides, 10 tools. **Czas:** 50 s.
- **Opis w grze:** Weavers (textiles).
- Budynek: **Weaver** (koszt 30 wood, 5 tools, 25 s): 2 miejsc pracy, każdy pracownik na minutę: 1 textiles z 1 hides, 1 food; 1 miejsce na heksie

### Irrigation — jedzenie

- **Wymaga:** Agriculture 40 pkt. **Cena:** 60 stone, 15 tools. **Czas:** 55 s.
- **Opis w grze:** Irrigation ditches drawn from water (at most 4 hexes): farms on watered hexes +25%; droughts 40% milder.
- Stan heksa: **Irrigation ditch** (2 wood, 1 stone i 3 s na heks)
- Efekt: susza o 40% łagodniejsza

### Barter — handel

- **Wymaga:** Pottery 15 pkt; wyprodukować 30 pottery. **Cena:** 20 pottery, 60 food. **Czas:** 50 s.
- **Opis w grze:** Trading posts; trade with other peoples (one agreement per trading post).
- Budynek: **Trading post** (koszt 40 wood, 10 pottery, 30 s): 2 miejsc pracy, każdy pracownik na minutę: nic; magazyn +150 każdego dobra; 2 miejsca na heksie

### Palisade — wojsko

- **Wymaga:** Stone tools 30 pkt. **Cena:** 80 wood. **Czas:** 40 s.
- **Opis w grze:** Palisades drawn on hexes: they wound enemies attacking through them.
- Stan heksa: **Palisade** (6 wood i 4 s na heks)

### Copper working — przemysł

- **Wymaga:** Masonry 20 pkt, Pottery 20 pkt; wyprodukować 30 tools. **Cena:** 60 stone, 40 clay, 30 tools. **Czas:** 70 s.
- **Opis w grze:** Copper mines.
- Budynek: **Copper mine** (koszt 40 wood, 15 tools, 30 s): 4 miejsc pracy, każdy pracownik na minutę: 2 copper; cały heks

### Fishing — jedzenie

- **Wymaga:** Hunting 15 pkt. **Cena:** 30 food, 20 wood. **Czas:** 25 s.
- **Opis w grze:** Fishing huts by rivers and the sea.
- Budynek: **Fishing hut** (koszt 20 wood, 15 s): 2 miejsc pracy, każdy pracownik na minutę: 5 food; cały heks; na złożu +100%; przy wodzie

### Rafts — handel

- **Wymaga:** Fishing 15 pkt. **Cena:** 50 wood. **Czas:** 30 s.
- **Opis w grze:** Raft landings: our people cross the river next to them.
- Budynek: **Raft landing** (koszt 40 wood, 20 s): 2 miejsc pracy, każdy pracownik na minutę: 2 food, 1 wood; nasi przechodzą przy niej przez rzekę; cały heks; przy wodzie

### Polished stone tools (U) — przemysł

- **Wymaga:** Masonry 20 pkt. **Cena:** 60 stone, 10 tools. **Czas:** 40 s.
- **Opis w grze:** Quarries, woodcutters, clay pits +15%.
- Efekt: produkcja: budynki z grupy „narzędzia” (kamieniołom, drwal, glinianka, kopalnie) +15%

### Hide shields (U) — wojsko

- **Wymaga:** Animal husbandry 20 pkt. **Cena:** 40 hides. **Czas:** 40 s.
- **Opis w grze:** Warriors and residents +15%.
- Efekt: siła: walka wręcz +15%

### Hafted weapons (U) — wojsko

- **Wymaga:** Stone tools 30 pkt. **Cena:** 30 stone, 5 tools. **Czas:** 30 s.
- **Opis w grze:** Warriors and residents +20%.
- Efekt: siła: walka wręcz +20%


## Epoka brązu (Bronze Age)

Liczy się do progu: 10 technologii zwykłych i ulepszenia; podstawowe nie.

### Bronze working — przemysł

- **Wymaga:** Copper working 30 pkt; wyprodukować 30 copper. **Cena:** 40 copper, 80 wood, 30 tools. **Czas:** 80 s.
- **Opis w grze:** Tin mines, bronze smiths.
- Budynek: **Tin mine** (koszt 40 wood, 15 tools, 30 s): 4 miejsc pracy, każdy pracownik na minutę: 2 tin; cały heks
- Budynek: **Bronze smith** (koszt 40 stone, 30 clay, 30 s): 3 miejsc pracy, każdy pracownik na minutę: 1 bronze z 2 copper, 1 tin, 1 wood; 1 miejsce na heksie

### Bronze weapons — wojsko

- **Wymaga:** Bronze working 15 pkt; wyprodukować 20 bronze. **Cena:** 20 bronze, 40 wood. **Czas:** 50 s.
- **Opis w grze:** Armourers (arms), spearmen.
- Budynek: **Armourer** (koszt 40 stone, 30 wood, 30 s): 2 miejsc pracy, każdy pracownik na minutę: 1 arms z 1 bronze, 1 wood; 1 miejsce na heksie
- Jednostka: **Spearmen** (10 os., koszt 40 food, 5 arms, 18 s, siła 16)

### The wheel — handel

- **Wymaga:** Agriculture 60 pkt, Masonry 20 pkt. **Cena:** 120 wood, 20 tools. **Czas:** 70 s.
- **Opis w grze:** With Horseback riding: caravans (they take horses). Porters and caravans 50% faster.
- Jednostka: **Caravan** (2 os., koszt nic, 0 s, wysyłani przez grę (nie szkoli się ich), wymaga też: Horseback riding, zabiera 2 konie (wracają z nią))
- Efekt: szybkość marszu: tragarze i karawany +50%

### Writing — nauka

- **Wymaga:** Pottery 40 pkt; wyprodukować 40 pottery. **Cena:** 80 clay, 100 food. **Czas:** 70 s.
- **Opis w grze:** Schools: every worker speeds one branch of research we choose (+15%).
- Budynek: **School** (koszt 60 stone, 40 clay, 40 s): 3 miejsc pracy, każdy pracownik na minutę: nic; każdy pracownik przyspiesza wybraną gałąź badań o 15% (gałąź najwyżej +50%); 1 miejsce na heksie

### Horseback riding — wojsko

- **Wymaga:** Animal husbandry 40 pkt. **Cena:** 150 food, 40 hides. **Czas:** 70 s.
- **Opis w grze:** Stables (horses, at most 32 each), horsemen.
- Budynek: **Stable** (koszt 60 wood, 30 s): 2 miejsc pracy, każdy pracownik na minutę: 1 horses z 2 food; mieści 32 horses; 2 miejsca na heksie; na złożu +100%
- Jednostka: **Caravan** (2 os., koszt nic, 0 s, wysyłani przez grę (nie szkoli się ich), wymaga też: Horseback riding, zabiera 2 konie (wracają z nią))
- Jednostka: **Horsemen** (6 os., koszt 40 food, 6 horses, 20 s, siła 18)

### Sailing — handel

- **Wymaga:** Fishing 40 pkt, Rafts 20 pkt. **Cena:** 150 wood, 20 textiles. **Czas:** 70 s.
- **Opis w grze:** Harbours; transport ships (carry armies) and trade ships (carry goods).
- Budynek: **Harbour** (koszt 80 wood, 20 stone, 40 s): 3 miejsc pracy, każdy pracownik na minutę: 4 food; cały heks; przy wodzie
- Jednostka: **Transport ship** (8 os., koszt 80 wood, 30 food, 25 s, siła 4, pływa, wymaga: Harbour, przewozi 20 żołnierzy)
- Jednostka: **Trade ship** (4 os., koszt nic, 0 s, pływa, wysyłani przez grę (nie szkoli się ich))

### Currency — handel

- **Wymaga:** Copper working 20 pkt; najpierw Barter. **Cena:** 30 copper, 30 pottery. **Czas:** 70 s.
- **Opis w grze:** Mints (coins from copper); pay and be paid in coins; research −5%.
- Budynek: **Mint** (koszt 60 stone, 20 wood, 35 s): 2 miejsc pracy, każdy pracownik na minutę: 10 coins z 1 copper; 1 miejsce na heksie
- Efekt: badania o 5% tańsze

### Code of laws — zarządzanie

- **Wymaga:** Writing 10 pkt. **Cena:** 60 clay, 80 food. **Czas:** 60 s.
- **Opis w grze:** A town office: with Masonry, villages can become towns.
- Poziom osady: **Town** (promień 5, domy 60, magazyn 300, awans za 250 wood, 200 stone, 40 pottery, 30 clothing; wymaga Code of laws, Masonry)

### Archery — wojsko

- **Wymaga:** Hunting 40 pkt. **Cena:** 80 wood, 30 hides. **Czas:** 60 s.
- **Opis w grze:** Bows: hunters become archers (stronger, longer reach).
- Jednostka: **Archers** (8 os., koszt 30 food, 25 wood, 15 s, siła 9, strzela na 3)

### Barracks — wojsko

- **Wymaga:** Writing 10 pkt; najpierw Code of laws. **Cena:** 80 wood, 40 stone. **Czas:** 50 s.
- **Opis w grze:** Barracks: soldiers wait there between wars; training twice as fast.
- Budynek: **Barracks** (koszt 80 wood, 40 stone, 40 s): 2 miejsc pracy, każdy pracownik na minutę: nic; szkolenie 50% czasu przy pełnej obsadzie; 2 miejsca na heksie

### Stone walls (U) — wojsko

- **Wymaga:** Masonry 40 pkt. **Cena:** 150 stone. **Czas:** 60 s.
- **Opis w grze:** Stone walls drawn on hexes: enemies cannot pass until they break them.
- Stan heksa: **Stone wall** (12 stone i 8 s na heks)

### Bronze age arms (U) — wojsko

- **Wymaga:** Bronze weapons 20 pkt. **Cena:** 40 bronze, 20 hides. **Czas:** 60 s.
- **Opis w grze:** Better weapons and armour: melee +30%, infantry +20%.
- Efekt: siła: walka wręcz +30%
- Efekt: siła: piechota +20%

### Bronze tools (U) — przemysł

- **Wymaga:** Bronze working 25 pkt. **Cena:** 30 bronze, 20 tools. **Czas:** 55 s.
- **Opis w grze:** Quarries, woodcutters, mines +20%.
- Efekt: produkcja: budynki z grupy „narzędzia” (kamieniołom, drwal, glinianka, kopalnie) +20%


## Epoka żelaza (Iron Age)

Liczy się do progu: 10 technologii zwykłych i ulepszenia; podstawowe nie.

### Iron working — przemysł

- **Wymaga:** Bronze working 50 pkt; wyprodukować 40 bronze. **Cena:** 40 bronze, 120 wood, 40 tools. **Czas:** 90 s.
- **Opis w grze:** Iron mines, blacksmiths (tools from iron).
- Budynek: **Iron mine** (koszt 50 wood, 20 tools, 35 s): 4 miejsc pracy, każdy pracownik na minutę: 1 iron; cały heks
- Budynek: **Blacksmith** (koszt 40 stone, 30 wood, 30 s): 2 miejsc pracy, każdy pracownik na minutę: 2 tools z 1 iron, 1 wood; 1 miejsce na heksie

### Iron weapons — wojsko

- **Wymaga:** Iron working 20 pkt. **Cena:** 40 iron, 60 wood. **Czas:** 70 s.
- **Opis w grze:** Weaponsmiths (arms from iron), swordsmen.
- Budynek: **Weaponsmith** (koszt 40 stone, 30 wood, 30 s): 2 miejsc pracy, każdy pracownik na minutę: 2 arms z 1 iron, 1 wood; 1 miejsce na heksie
- Jednostka: **Swordsmen** (10 os., koszt 50 food, 10 arms, 20 s, siła 24)

### Mathematics — nauka

- **Wymaga:** Writing 40 pkt. **Cena:** 100 clay, 150 food. **Czas:** 80 s.
- **Opis w grze:** Builders' yards: their workers make building in their settlement up to 30% faster.
- Budynek: **Builders' yard** (koszt 80 stone, 60 wood, 40 s): 3 miejsc pracy, każdy pracownik na minutę: nic; budowa w osadzie do 30% szybsza; 2 miejsca na heksie

### Construction — budownictwo

- **Wymaga:** Masonry 60 pkt, Mathematics 10 pkt. **Cena:** 200 stone, 150 wood, 60 tools. **Czas:** 100 s.
- **Opis w grze:** Great storehouses; with Civil service, towns can become cities.
- Budynek: **Great storehouse** (koszt 150 stone, 80 wood, 45 s): magazyn +1000 każdego dobra; cały heks
- Poziom osady: **City** (promień 6, domy 90, magazyn 500, awans za 500 wood, 500 stone, 80 textiles, 80 pottery, 60 tools; wymaga Civil service, Construction)

### Civil service — zarządzanie

- **Wymaga:** Writing 60 pkt; najpierw Code of laws. **Cena:** 120 clay, 150 food. **Czas:** 90 s.
- **Opis w grze:** Province offices; provinces; a larger reach of the administration; with Construction, towns can become cities.
- Budynek: **Province office** (koszt 120 stone, 60 wood, 50 s): 3 miejsc pracy, każdy pracownik na minutę: nic; 2 miejsca na heksie
- Poziom osady: **City** (promień 6, domy 90, magazyn 500, awans za 500 wood, 500 stone, 80 textiles, 80 pottery, 60 tools; wymaga Civil service, Construction)

### Roads — handel

- **Wymaga:** Mathematics 10 pkt, Masonry 30 pkt; najpierw The wheel, Construction. **Cena:** 150 stone, 30 tools. **Czas:** 70 s.
- **Opis w grze:** Paved roads: much faster than tracks, also over hills; major trade routes.
- Droga: **Road** (3 stone, 1 wood i 4 s na heks, marsz 35% kosztu heksu)

### Engineering — budownictwo

- **Wymaga:** Mathematics 20 pkt, Masonry 40 pkt; najpierw Construction. **Cena:** 200 wood, 60 tools, 20 iron. **Czas:** 90 s.
- **Opis w grze:** Siege workshops (their workers build engines faster), catapults; aqueducts drawn from water to towns.
- Budynek: **Siege workshop** (koszt 100 wood, 30 tools, 45 s): 3 miejsc pracy, każdy pracownik na minutę: nic; szkolenie 70% czasu przy pełnej obsadzie; 2 miejsca na heksie
- Jednostka: **Catapult** (6 os., koszt 100 wood, 20 tools, 30 s, siła 8, strzela na 2, oblężenie ×3, wymaga: Siege workshop)
- Stan heksa: **Aqueduct** (10 stone, 1 tools i 6 s na heks)

### Medicine — nauka

- **Wymaga:** Herbal lore 40 pkt, Writing 30 pkt. **Cena:** 150 food, 40 pottery. **Czas:** 80 s.
- **Opis w grze:** Bathhouses: in their settlement epidemics are milder and people grow faster.
- Budynek: **Bathhouse** (koszt 80 stone, 20 pottery, 40 s): 2 miejsc pracy, każdy pracownik na minutę: nic; epidemie w osadzie do 30% łagodniejsze, przyrost +2%; 2 miejsca na heksie

### Philosophy — nauka

- **Wymaga:** Writing 60 pkt, Mathematics 20 pkt. **Cena:** 150 clay, 40 textiles. **Czas:** 90 s.
- **Opis w grze:** Academies (every worker speeds one branch of research, +20%); rarer thinkers and advisors.
- Budynek: **Academy** (koszt 120 stone, 20 textiles, 50 s): 3 miejsc pracy, każdy pracownik na minutę: nic; każdy pracownik przyspiesza wybraną gałąź badań o 20% (gałąź najwyżej +50%); 2 miejsca na heksie
- Efekt: lepsza szansa na rzadszych myślicieli i doradców (+40)

### Shipbuilding — handel

- **Wymaga:** Sailing 40 pkt, Iron working 20 pkt. **Cena:** 250 wood, 30 iron. **Czas:** 90 s.
- **Opis w grze:** Shipyards, warships (fight at sea, carry armies); larger trade ships.
- Budynek: **Shipyard** (koszt 150 wood, 30 tools, 50 s): 4 miejsc pracy, każdy pracownik na minutę: nic; szkolenie 60% czasu przy pełnej obsadzie; cały heks; przy wodzie
- Jednostka: **Warship** (12 os., koszt 150 wood, 10 arms, 35 s, siła 22, strzela na 2, pływa, wymaga: Shipyard, przewozi 30 żołnierzy)

### Iron age arms (U) — wojsko

- **Wymaga:** Iron weapons 30 pkt. **Cena:** 60 iron, 30 hides. **Czas:** 70 s.
- **Opis w grze:** Better weapons and armour: melee +30%, infantry and cavalry +25%.
- Efekt: siła: walka wręcz +30%
- Efekt: siła: piechota +25%
- Efekt: siła: jazda +25%

### Iron tools (U) — przemysł

- **Wymaga:** Iron working 30 pkt. **Cena:** 40 iron, 30 tools. **Czas:** 65 s.
- **Opis w grze:** Quarries, woodcutters, mines +25%; farms +15%.
- Efekt: produkcja: budynki z grupy „narzędzia” (kamieniołom, drwal, glinianka, kopalnie) +25%
- Efekt: produkcja: Farm +15%


## Średniowiecze (Middle Ages)

Liczy się do progu: 9 technologii zwykłych i ulepszenia; podstawowe nie.

### Feudalism — zarządzanie

- **Wymaga:** Civil service 20 pkt, Horseback riding 30 pkt; najpierw Construction. **Cena:** 300 food, 60 iron, 200 stone. **Czas:** 120 s.
- **Opis w grze:** Manors; a larger reach of the administration.
- Budynek: **Manor** (koszt 200 stone, 100 wood, 60 s): 4 miejsc pracy, każdy pracownik na minutę: 8 food; domy dla 10 osób; zimą jeszcze 50%; cały heks

### Chivalry — wojsko

- **Wymaga:** Feudalism 20 pkt; najpierw Horseback riding. **Cena:** 200 food, 60 iron. **Czas:** 90 s.
- **Opis w grze:** Knights (they take horses).
- Jednostka: **Knights** (6 os., koszt 60 food, 15 arms, 6 horses, 30 s, siła 40)

### Steel — przemysł

- **Wymaga:** Iron working 60 pkt; wyprodukować 100 tools. **Cena:** 120 iron, 200 wood, 80 tools. **Czas:** 120 s.
- **Opis w grze:** Steelworks.
- Budynek: **Steelworks** (koszt 150 stone, 40 iron, 60 s): 3 miejsc pracy, każdy pracownik na minutę: 4 tools, 2 arms z 2 iron, 2 wood; 2 miejsca na heksie

### Castles — wojsko

- **Wymaga:** Masonry 60 pkt; najpierw Stone walls, Construction. **Cena:** 400 stone, 40 iron. **Czas:** 110 s.
- **Opis w grze:** Castles (+220% defence).
- Budynek: **Castle** (koszt 400 stone, 100 wood, 30 iron, 90 s): domy dla 10 osób; obrona osady +220%; cały heks

### Heavy plough — jedzenie

- **Wymaga:** Agriculture 200 pkt, Iron working 40 pkt. **Cena:** 60 iron, 150 wood. **Czas:** 90 s.
- **Opis w grze:** Farms also on poorer land; farms +15%.
- Efekt: produkcja: Farm +15%
- Efekt: fertility farm 15

### Crossbows — wojsko

- **Wymaga:** Iron working 40 pkt; najpierw Archery. **Cena:** 60 iron, 100 wood. **Czas:** 90 s.
- **Opis w grze:** Crossbowmen.
- Jednostka: **Crossbowmen** (8 os., koszt 40 food, 10 arms, 20 s, siła 20, strzela na 2)

### Guilds — przemysł

- **Wymaga:** Currency 60 pkt, Weaving 60 pkt. **Cena:** 80 textiles, 60 pottery. **Czas:** 100 s.
- **Opis w grze:** Guild halls: workshops in their settlement +25% while they work.
- Budynek: **Guild hall** (koszt 150 stone, 30 textiles, 50 s): 3 miejsc pracy, każdy pracownik na minutę: nic; wsparcie: budynki „craft” w osadzie +25% przy pełnej obsadzie; 2 miejsca na heksie

### Mills — jedzenie

- **Wymaga:** Agriculture 200 pkt; najpierw The wheel. **Cena:** 200 stone, 150 wood. **Czas:** 90 s.
- **Opis w grze:** Mills by rivers: farms in their settlement +20% while they work.
- Budynek: **Mill** (koszt 80 wood, 60 stone, 10 tools, 40 s): 2 miejsc pracy, każdy pracownik na minutę: nic; wsparcie: budynki „farm” w osadzie +20% przy pełnej obsadzie; 2 miejsca na heksie; przy wodzie

### University — nauka

- **Wymaga:** Philosophy 40 pkt, Writing 120 pkt. **Cena:** 300 stone, 80 textiles. **Czas:** 120 s.
- **Opis w grze:** Universities (every worker speeds one branch of research, +25%).
- Budynek: **University** (koszt 200 stone, 40 textiles, 40 pottery, 60 s): 4 miejsc pracy, każdy pracownik na minutę: nic; każdy pracownik przyspiesza wybraną gałąź badań o 25% (gałąź najwyżej +50%); 2 miejsca na heksie

### Medieval arms (U) — wojsko

- **Wymaga:** Steel 30 pkt. **Cena:** 120 iron, 30 textiles. **Czas:** 100 s.
- **Opis w grze:** Steel weapons and plate armour: melee, infantry and cavalry +30%.
- Efekt: siła: walka wręcz +30%
- Efekt: siła: piechota +30%
- Efekt: siła: jazda +30%

### Crop rotation (U) — jedzenie

- **Wymaga:** Agriculture 120 pkt; najpierw Irrigation. **Cena:** 200 food, 40 tools. **Czas:** 70 s.
- **Opis w grze:** Farms +25%.
- Efekt: produkcja: Farm +25%

### Longbow (U) — wojsko

- **Wymaga:** Hunting 60 pkt; najpierw Archery. **Cena:** 150 wood, 20 iron. **Czas:** 80 s.
- **Opis w grze:** Archers and crossbowmen +30%.
- Efekt: siła: jednostki dystansowe +30%

### Trebuchet (U) — wojsko

- **Wymaga:** Engineering 40 pkt. **Cena:** 250 wood, 40 iron. **Czas:** 90 s.
- **Opis w grze:** Siege engines +100% against settlements.
- Efekt: oblężenie +100%


## Mechaniki, na które działają technologie

**Zbieranie z okolicy:**
- Gracz zleca osadzie zbieranie. Ludzie bez pracy zbierają wtedy jedzenie, drewno i kamień.
- Ile, zależy od średniej wartości heksów w promieniu osady:
  - jedzenie i drewno od biomu;
  - kamień od rzeźby terenu;
  - premie za złoża i za rzekę.
- Na zbieracza na minutę przy pełnej wartości: 0,5 jedzenia, 0,25 drewna, 0,1 kamienia.

**Zima:**
- Przychodzi co 8 minut i trwa 2 minuty. Gracz dostaje ostrzeżenie 30 s wcześniej.
- Wszyscy pracownicy pracują wtedy na 50%. Zbieracze, pola i zbieranie z okolicy tracą dodatkowo połowę, łowcy i rybacy nie.
- **Ubrania:**
  - każdy zestaw daje +15%;
  - poza zimą liczy się jeden zestaw na osobę (najwyżej 115%);
  - zimą kolejne zestawy, do 110%;
  - zużywają się: 0,03 zestawu na osobę na minutę.
- **Kamienne domy:** ich mieszkańcy tracą zimą 35% zamiast 50%.

**Tragarze i karawany:**
- Tragarze niosą po 5, a z Carrying po 20.
- Towary między osadami niosą tragarze, dopóki lud nie ma The wheel, Horseback riding i koni w stajni. Karawana zabiera 2 konie, które z nią wracają.

**Rzeki:**
- Do epoki brązu nie da się ich przejść.
- Wyjątki:
  - przy własnej przystani (Rafts);
  - przez most na drodze (Masonry).

**Ranni:** 30% poległych to ranni. Zielarze leczą ich w ziemi swojej osady.

**4 miejsca na heksie:**
- Budynek zajmuje 1, 2 albo 4 miejsca.
- Pola, kopalnie, kamieniołomy, porty i zamki zajmują cały heks.

**Stany heksa:**
- rysuje się je jak drogi i zostają pod budynkami;
- usuwa je tylko gracz;
- rodzaje:
  - rów: farma na nawodnionym heksie +25%;
  - palisada: rani wrogów, którzy na niej stoją;
  - mur: blokuje obcych, dopóki go nie rozbiją;
  - akwedukt: szybszy wzrost osady i łagodniejsze epidemie.

**Budynki wsparcia:**
- działają tylko z pracownikami i wzmacniają inne budynki w swojej osadzie;
- przykłady:
  - zielarz: łowcy;
  - kamieniarz: kamieniołomy;
  - młyn: farmy;
  - gildia: warsztaty;
  - łaźnia: wzrost i epidemie;
  - plac budowniczych: budowa;
  - warsztat oblężniczy i stocznia: szkolenie swoich jednostek.

**Nauka:** każdy pracownik szkoły, akademii i uniwersytetu przyspiesza jedną gałąź badań wybraną przez gracza (+15%, +20%, +25%). Gałąź najwyżej +50%.

**Statki:**
- Transport przewozi 20 żołnierzy, okręt wojenny 30. Zatopiony statek zabiera ich ze sobą.
- Handel bez drogi lądowej płynie statkami handlowymi między portami.

**Handel:** do Currency każda umowa handlowa wymaga naszego punktu handlowego.

## Jak gracz dostaje te informacje

1. **Okno Research (klawisz 2):** siatka, w której era to kolumna, a gałąź wiersz.
   - Kolory kart: zielona to znana, jasna do zbadania, ciemna zablokowana, niebieska badana.
   - Ulepszenia mają „+” przy nazwie.
2. **Podpowiedź karty:**
   - opis;
   - lista wyliczona z danych: budynki, jednostki („with X”, gdy potrzeba dwóch technologii), zamiany jednostek, poziomy osad, drogi, stany heksa, mosty, efekty;
   - co trzeba najpierw i co wyprodukować;
   - czy to krok podstawowy;
   - cena i prawdziwy czas (ze szkołami);
   - punkty doświadczenia i powód blokady.
3. **Górny pasek:** licznik ery i zima („WINTER: 1:20 left”, „Winter in 30 s”).
4. **Panel osady:**
   - przycisk zbierania z wartością ziemi;
   - siła pracy w zimie;
   - leczenie;
   - alert „clothing” przed zimą.
5. **Panel budynku:** przy szkołach wybór gałęzi dla każdego pracownika.
6. **Panel heksa:** stany heksa z przyciskiem „usuń”, mosty, zajęte miejsca (np. „2 of 4 places”).
7. **Kronika:**
   - nowe technologie, era i pory roku;
   - zamiana jednostek („Our hunters are now archers”);
   - mosty i przebite mury.

**Czego jeszcze brakuje:**
- strzałek zależności w siatce;
- paska postępu doświadczenia;
- pokazania, ile ulepszenie realnie dało;
- prowincji i limitu administracji z Civil service (przyjdą z zarządcą).

## Jak wygląda drzewo w grze (2026-11-19)

Okno Research (klawisz 2) zajmuje cały ekran. Drzewo jest rysowane metodą Sugiyamy, czyli standardową metodą rysowania grafów warstwowych:
1. **Kolumny (warstwy).** Technologia stoi kolumnę na prawo od najpóźniejszej technologii, której potrzebuje, i nigdy na lewo od pierwszej kolumny swojej ery. Ery to pasy kolumn; era ludu jest lekko podświetlona.
2. **Punkty pośrednie.** Linia, która przeskakuje kolumny, dostaje w każdej mijanej kolumnie własne miejsce (cienki „przystanek”). Dzięki temu biegnie w przerwie obok kart, nigdy pod nimi.
3. **Mniej skrzyżowań.** Kolejność w każdej kolumnie układa się według średniej pozycji sąsiadów (metoda barycentrum), 12 przejść w prawo i w lewo. Na starcie technologie są ułożone według gałęzi, więc gałąź trzyma się razem.
4. **Wysokości.** Karty i przystanki przesuwają się ku średniej wysokości sąsiadów, z zachowaniem kolejności i odstępów. Tłok rozkłada się w górę i w dół, a linie są możliwie proste.

Do tego:
- linie wchodzą w kartę w punktach rozłożonych na jej lewym boku;
- kolory linii: zielone między znanymi, złote od znanej do nowej, szare dalej;
- **najechanie myszą** na technologię podświetla ją, wszystko, czego potrzebuje (aż do początku), i linie między nimi, a reszta blednie;
- pasek z lewej strony karty to gałąź (legenda nad drzewem), ramka to stan.

**Widać wszystkie ery** (Patryk, 2026-11-19), bo gra polega na planowaniu. W innych miejscach (panel heksa, panel osady) widać tylko to, co jest z ery, do której lud doszedł.

## Tempo badań (2026-11-19)

`research.pace` w `data/sim/game.json` skaluje każdą technologię według jej ery: `pricePercent` (cena w dobrach),
`experiencePercent` (doświadczenie i wyroby, których technologia wymaga) i `secondsPercent` (czas badania). Obecnie:

| Era | Cena | Doświadczenie i wyroby | Czas |
| --- | --- | --- | --- |
| Epoka kamienia | 40% | 40% | 60% |
| Neolit | 35% | 35% | 50% |
| Epoka brązu | 25% | 22% | 35% |
| Epoka żelaza | 25% | 22% | 35% |
| Średniowiecze | 25% | 22% | 35% |

Przykład: garncarstwo ma w drzewie cenę 40 gliny i 30 drewna oraz 45 s; w neolicie kosztuje 14 gliny i 10 drewna i trwa 22 s (przed premiami myśliciela i sąsiadów). Okno badań i podpowiedzi
pokazują liczby już po skalowaniu. Pomiar AI (12 ziaren) i powody: `docs/sim/ai.md`, sekcja „Tempo er”.
