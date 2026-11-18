# Drzewo technologii (stan z `data/sim/game.json`)

60 technologii: 45 bazowych (po 9 na erę) i 15 ulepszeń ery (oznaczone „U”). Nazwy są takie jak w grze.

**Zasady badań:**
- Era otwiera się, gdy lud zna 5 technologii poprzedniej ery (ulepszenia też się liczą).
- Technologia wymaga **doświadczenia**: minut pracy ludzi w budynkach poprzednich technologii. Wyjątek: Foraging jest znane od startu.
- Potem płaci się **cenę** w dobrach (cały lud, najpierw stolica) i czeka się **czas** badania. Naraz bada się jedną technologię.
- Taniej: o 5% za każdego sąsiada, który ją zna (najwyżej kilka), o 5% za każdą umowę handlową, do tego szkoły i efekty. Razem najwyżej 60%.

## Epoka kamienia (Stone Age)

### Foraging — jedzenie

- **Wymaga:** znana od startu. **Cena:** nic. **Czas:** 0 s.
- **Opis w grze:** Gatherers, woodcutters, huts, storehouses, scouts, settlers, warriors.
- Budynek: **Gatherers' hut** (koszt 15 wood, 12 s): 3 miejsc pracy, każdy pracownik na minutę: 4 food, 1 wood; na złożu +100%
- Budynek: **Woodcutter** (koszt 10 wood, 5 stone, 12 s): 3 miejsc pracy, każdy pracownik na minutę: 4 wood
- Budynek: **Huts** (koszt 20 wood, 10 s): domy dla 8 osób
- Budynek: **Storehouse** (koszt 30 wood, 20 s): magazyn +300 każdego dobra
- Jednostka: **Settlers** (8 os., koszt 40 food, 30 wood, 10 s, zakłada osady)
- Jednostka: **Scouts** (2 os., koszt 15 food, 5 s)
- Jednostka: **Porters** (1 os., koszt nic, 0 s, wysyłani przez grę (nie szkoli się ich))
- Jednostka: **Caravan** (2 os., koszt nic, 0 s, wysyłani przez grę (nie szkoli się ich))
- Jednostka: **Levy** (10 os., koszt nic, 3 s, siła 5)
- Jednostka: **Warriors** (10 os., koszt 30 food, 10 wood, 12 s, siła 10)
- Droga: **Track** (2 wood i 2 s na heks, marsz 60% kosztu heksu)

### Fire — jedzenie

- **Wymaga:** Foraging 10 min. **Cena:** 30 wood. **Czas:** 20 s.
- **Opis w grze:** Hearths; gatherers +10%.
- Budynek: **Hearth** (koszt 15 wood, 10 stone, 10 s): domy dla 3 osób
- Efekt: produkcja: Gatherers' hut +10%

### Stone tools — przemysł

- **Wymaga:** Foraging 20 min. **Cena:** 40 food, 20 wood. **Czas:** 30 s.
- **Opis w grze:** Quarries, toolmakers.
- Budynek: **Quarry** (koszt 25 wood, 20 s): 4 miejsc pracy, każdy pracownik na minutę: 2 stone; na złożu +100%; na hills/mountains +100%
- Budynek: **Toolmaker** (koszt 30 wood, 20 stone, 25 s): 2 miejsc pracy, każdy pracownik na minutę: 1 tools z 2 stone, 1 wood; na złożu +50%

### Hunting — jedzenie

- **Wymaga:** Foraging 15 min. **Cena:** 30 food, 20 wood. **Czas:** 25 s.
- **Opis w grze:** Hunters' lodges, archers.
- Budynek: **Hunters' lodge** (koszt 20 wood, 15 s): 3 miejsc pracy, każdy pracownik na minutę: 3 food, 1 hides; na złożu +100%
- Jednostka: **Archers** (8 os., koszt 30 food, 25 wood, 15 s, siła 8, strzela na 2)

### Fishing — jedzenie

- **Wymaga:** Foraging 15 min. **Cena:** 30 food, 20 wood. **Czas:** 25 s.
- **Opis w grze:** Fishing huts.
- Budynek: **Fishing hut** (koszt 20 wood, 15 s): 2 miejsc pracy, każdy pracownik na minutę: 5 food; na złożu +100%; przy wodzie

### Shelters — budownictwo

- **Wymaga:** Foraging 25 min. **Cena:** 40 wood. **Czas:** 30 s.
- **Opis w grze:** Huts give 4 more homes; clay pits.
- Budynek: **Clay pit** (koszt 20 wood, 15 s): 3 miejsc pracy, każdy pracownik na minutę: 3 clay; na złożu +100%; przy wodzie
- Efekt: domy: Huts +4 miejsc

### Herbal lore — nauka

- **Wymaga:** Hunting 15 min. **Cena:** 40 food. **Czas:** 30 s.
- **Opis w grze:** Herbalists; epidemics half as deadly.
- Budynek: **Herbalist** (koszt 20 wood, 10 clay, 20 s): 2 miejsc pracy, każdy pracownik na minutę: nic
- Efekt: epidemia o 50% łagodniejsza

### Sewing — przemysł

- **Wymaga:** Hunting 20 min, Stone tools 10 min. **Cena:** 15 hides, 30 food. **Czas:** 30 s.
- **Opis w grze:** Tailors (clothing); hard winters half as harsh.
- Budynek: **Tailor** (koszt 20 wood, 5 tools, 20 s): 2 miejsc pracy, każdy pracownik na minutę: 1 clothing z 1 hides
- Efekt: sroga zima o 50% łagodniejsza

### Rafts — handel

- **Wymaga:** Fishing 15 min. **Cena:** 50 wood. **Czas:** 30 s.
- **Opis w grze:** Raft landings; units cross rivers faster.
- Budynek: **Raft landing** (koszt 40 wood, 20 s): 2 miejsc pracy, każdy pracownik na minutę: 2 food, 1 wood; przy wodzie
- Efekt: szybkość marszu: wszystko +10%

### Flint blades (U) — wojsko

- **Wymaga:** Stone tools 20 min. **Cena:** 30 stone, 5 tools. **Czas:** 25 s.
- **Opis w grze:** Warriors and levy +20%.
- Efekt: siła: walka wręcz +20%


## Neolit (Neolithic)

### Agriculture — jedzenie

- **Wymaga:** Foraging 40 min, Stone tools 10 min. **Cena:** 80 food, 40 wood, 20 stone. **Czas:** 60 s.
- **Opis w grze:** Farms; camps can become villages.
- Budynek: **Farm** (koszt 30 wood, 5 tools, 20 s): 4 miejsc pracy, każdy pracownik na minutę: 6 food; na złożu +50%; żyzność od 40
- Poziom osady: **Village** (promień 4, domy 40, magazyn 200, awans za 120 wood, 60 stone, 30 clay)

### Pottery — przemysł

- **Wymaga:** Agriculture 15 min, Shelters 10 min. **Cena:** 40 clay, 30 wood. **Czas:** 45 s.
- **Opis w grze:** Potters (pottery), granaries.
- Budynek: **Potter** (koszt 20 wood, 10 stone, 20 s): 2 miejsc pracy, każdy pracownik na minutę: 1 pottery z 2 clay, 1 wood
- Budynek: **Granary** (koszt 30 wood, 30 clay, 25 s): magazyn +250 każdego dobra

### Animal husbandry — jedzenie

- **Wymaga:** Agriculture 20 min, Hunting 20 min. **Cena:** 100 food, 40 wood. **Czas:** 50 s.
- **Opis w grze:** Pastures.
- Budynek: **Pasture** (koszt 30 wood, 20 s): 2 miejsc pracy, każdy pracownik na minutę: 4 food, 2 hides; na złożu +100%

### Masonry — budownictwo

- **Wymaga:** Stone tools 40 min, Agriculture 15 min. **Cena:** 80 stone, 20 tools. **Czas:** 60 s.
- **Opis w grze:** Stone houses, masons; villages can become towns.
- Budynek: **Stone houses** (koszt 40 stone, 10 wood, 25 s): domy dla 16 osób
- Budynek: **Mason** (koszt 30 wood, 10 tools, 25 s): 3 miejsc pracy, każdy pracownik na minutę: nic z 1 stone
- Poziom osady: **Town** (promień 5, domy 60, magazyn 300, awans za 250 wood, 200 stone, 40 pottery, 30 clothing)

### Weaving — przemysł

- **Wymaga:** Animal husbandry 20 min, Sewing 15 min. **Cena:** 30 hides, 10 tools. **Czas:** 50 s.
- **Opis w grze:** Weavers (textiles).
- Budynek: **Weaver** (koszt 30 wood, 5 tools, 25 s): 2 miejsc pracy, każdy pracownik na minutę: 1 textiles z 1 hides, 1 food

### Irrigation — jedzenie

- **Wymaga:** Agriculture 40 min. **Cena:** 60 stone, 15 tools. **Czas:** 55 s.
- **Opis w grze:** Irrigation ditches; farms +25%; droughts 40% milder.
- Budynek: **Irrigation ditch** (koszt 20 wood, 20 stone, 20 s): 2 miejsc pracy, każdy pracownik na minutę: 5 food; przy wodzie; żyzność od 30
- Efekt: produkcja: Farm +25%
- Efekt: susza o 40% łagodniejsza

### Barter — handel

- **Wymaga:** Pottery 15 min. **Cena:** 20 pottery, 60 food. **Czas:** 50 s.
- **Opis w grze:** Trading posts; trade agreements and caravans to other peoples.
- Budynek: **Trading post** (koszt 40 wood, 10 pottery, 30 s): 2 miejsc pracy, każdy pracownik na minutę: nic; magazyn +150 każdego dobra

### Palisade — wojsko

- **Wymaga:** Stone tools 30 min. **Cena:** 80 wood. **Czas:** 40 s.
- **Opis w grze:** Palisades (+40% defence).
- Budynek: **Palisade** (koszt 60 wood, 25 s): obrona osady +40%

### Copper working — przemysł

- **Wymaga:** Masonry 20 min, Pottery 20 min. **Cena:** 60 stone, 40 clay, 30 tools. **Czas:** 70 s.
- **Opis w grze:** Copper mines.
- Budynek: **Copper mine** (koszt 40 wood, 15 tools, 30 s): 4 miejsc pracy, każdy pracownik na minutę: 2 copper

### Polished stone tools (U) — przemysł

- **Wymaga:** Masonry 20 min. **Cena:** 60 stone, 10 tools. **Czas:** 40 s.
- **Opis w grze:** Quarries, woodcutters, clay pits +15%.
- Efekt: produkcja: budynki z grupy „narzędzia” (kamieniołom, drwal, glinianka, kopalnie) +15%

### Hide shields (U) — wojsko

- **Wymaga:** Animal husbandry 20 min. **Cena:** 40 hides. **Czas:** 40 s.
- **Opis w grze:** Infantry +15%.
- Efekt: siła: piechota +15%


## Epoka brązu (Bronze Age)

### Bronze working — przemysł

- **Wymaga:** Copper working 30 min. **Cena:** 40 copper, 80 wood, 30 tools. **Czas:** 80 s.
- **Opis w grze:** Tin mines, bronze smiths, armourers (arms), spearmen.
- Budynek: **Tin mine** (koszt 40 wood, 15 tools, 30 s): 4 miejsc pracy, każdy pracownik na minutę: 2 tin
- Budynek: **Bronze smith** (koszt 40 stone, 30 clay, 30 s): 3 miejsc pracy, każdy pracownik na minutę: 1 bronze z 2 copper, 1 tin, 1 wood
- Budynek: **Armourer** (koszt 40 stone, 30 wood, 30 s): 2 miejsc pracy, każdy pracownik na minutę: 1 arms z 1 bronze, 1 wood
- Jednostka: **Spearmen** (10 os., koszt 40 food, 5 arms, 18 s, siła 16)

### The wheel — handel

- **Wymaga:** Agriculture 60 min, Masonry 20 min. **Cena:** 120 wood, 20 tools. **Czas:** 70 s.
- **Opis w grze:** Cartwrights, chariots; porters and caravans 50% faster.
- Budynek: **Cartwright** (koszt 50 wood, 10 tools, 30 s): 2 miejsc pracy, każdy pracownik na minutę: 1 tools z 2 wood
- Jednostka: **Chariots** (6 os., koszt 50 food, 40 wood, 5 bronze, 20 s, siła 16)
- Efekt: szybkość marszu: tragarze i karawany +50%

### Writing — zarządzanie

- **Wymaga:** Pottery 40 min, Agriculture 60 min. **Cena:** 80 clay, 100 food. **Czas:** 70 s.
- **Opis w grze:** Schools (research −10%).
- Budynek: **School** (koszt 60 stone, 40 clay, 40 s): 3 miejsc pracy, każdy pracownik na minutę: nic; badania −10%

### Horseback riding — wojsko

- **Wymaga:** Animal husbandry 40 min. **Cena:** 150 food, 40 hides. **Czas:** 70 s.
- **Opis w grze:** Stables, horsemen.
- Budynek: **Stable** (koszt 60 wood, 30 s): 2 miejsc pracy, każdy pracownik na minutę: 1 hides z 2 food
- Jednostka: **Horsemen** (6 os., koszt 60 food, 20 hides, 20 s, siła 18)

### Sailing — handel

- **Wymaga:** Fishing 40 min, Rafts 20 min. **Cena:** 150 wood, 20 textiles. **Czas:** 70 s.
- **Opis w grze:** Harbours, galleys.
- Budynek: **Harbour** (koszt 80 wood, 20 stone, 40 s): 3 miejsc pracy, każdy pracownik na minutę: 4 food; przy wodzie
- Jednostka: **Galley** (8 os., koszt 80 wood, 30 food, 25 s, siła 10, pływa, wymaga: Harbour)

### Currency — handel

- **Wymaga:** Barter 30 min, Copper working 20 min. **Cena:** 30 copper, 30 pottery. **Czas:** 70 s.
- **Opis w grze:** Markets; barter at fair worth; research −5%.
- Budynek: **Market** (koszt 60 wood, 40 stone, 10 pottery, 40 s): 3 miejsc pracy, każdy pracownik na minutę: nic; magazyn +200 każdego dobra
- Efekt: badania o 5% tańsze

### Code of laws — zarządzanie

- **Wymaga:** Writing 10 min. **Cena:** 60 clay, 80 food. **Czas:** 60 s.
- **Opis w grze:** Barracks (training ×2 faster); levy trains 50% faster.
- Budynek: **Barracks** (koszt 80 wood, 40 stone, 40 s): szkolenie 50% czasu
- Efekt: szkolenie: Levy o 50% krócej

### Composite bow — wojsko

- **Wymaga:** Hunting 40 min, Bronze working 15 min. **Cena:** 80 wood, 30 hides. **Czas:** 60 s.
- **Opis w grze:** Archers +30%.
- Efekt: siła: Archers +30%

### Brick walls — wojsko

- **Wymaga:** Masonry 40 min, Pottery 30 min. **Cena:** 150 clay, 50 wood. **Czas:** 60 s.
- **Opis w grze:** Brick walls (+70% defence).
- Budynek: **Brick walls** (koszt 120 clay, 30 wood, 35 s): obrona osady +70%

### Bronze weapons (U) — wojsko

- **Wymaga:** Bronze working 20 min. **Cena:** 30 bronze, 40 wood. **Czas:** 60 s.
- **Opis w grze:** Melee +30%.
- Efekt: siła: walka wręcz +30%

### Bronze armour (U) — wojsko

- **Wymaga:** Bronze working 30 min. **Cena:** 40 bronze, 20 hides. **Czas:** 60 s.
- **Opis w grze:** Infantry +20%.
- Efekt: siła: piechota +20%

### Bronze tools (U) — przemysł

- **Wymaga:** Bronze working 25 min. **Cena:** 30 bronze, 20 tools. **Czas:** 55 s.
- **Opis w grze:** Quarries, woodcutters, mines +20%.
- Efekt: produkcja: budynki z grupy „narzędzia” (kamieniołom, drwal, glinianka, kopalnie) +20%


## Epoka żelaza (Iron Age)

### Iron working — przemysł

- **Wymaga:** Bronze working 50 min. **Cena:** 40 bronze, 120 wood, 40 tools. **Czas:** 90 s.
- **Opis w grze:** Iron mines, forges, swordsmen.
- Budynek: **Iron mine** (koszt 50 wood, 20 tools, 35 s): 4 miejsc pracy, każdy pracownik na minutę: 1 iron
- Budynek: **Forge** (koszt 60 stone, 40 wood, 35 s): 3 miejsc pracy, każdy pracownik na minutę: 2 tools, 1 arms z 1 iron, 2 wood
- Jednostka: **Swordsmen** (10 os., koszt 50 food, 10 arms, 20 s, siła 24)

### Mathematics — nauka

- **Wymaga:** Writing 40 min. **Cena:** 100 clay, 150 food. **Czas:** 80 s.
- **Opis w grze:** Observatories (research −15%).
- Budynek: **Observatory** (koszt 100 stone, 45 s): 2 miejsc pracy, każdy pracownik na minutę: nic; badania −15%

### Construction — budownictwo

- **Wymaga:** Masonry 60 min, Mathematics 10 min. **Cena:** 200 stone, 150 wood, 60 tools. **Czas:** 100 s.
- **Opis w grze:** Great storehouses; towns can become cities.
- Budynek: **Great storehouse** (koszt 150 stone, 80 wood, 45 s): magazyn +1000 każdego dobra
- Budynek: **Builders' yard** (koszt 80 stone, 60 wood, 40 s): 3 miejsc pracy, każdy pracownik na minutę: nic z 1 stone
- Poziom osady: **City** (promień 6, domy 90, magazyn 500, awans za 500 wood, 500 stone, 80 textiles, 80 pottery, 60 tools)

### Roads — handel

- **Wymaga:** Construction 10 min, The wheel 30 min. **Cena:** 150 stone, 30 tools. **Czas:** 70 s.
- **Opis w grze:** Paved roads: much faster than tracks, also over hills; major trade routes.
- Droga: **Road** (3 stone, 1 wood i 4 s na heks, marsz 35% kosztu heksu)

### Engineering — wojsko

- **Wymaga:** Construction 20 min, Mathematics 20 min. **Cena:** 200 wood, 60 tools, 20 iron. **Czas:** 90 s.
- **Opis w grze:** Siege workshops, catapults.
- Budynek: **Siege workshop** (koszt 100 wood, 30 tools, 45 s): 3 miejsc pracy, każdy pracownik na minutę: nic z 2 wood
- Jednostka: **Catapult** (6 os., koszt 100 wood, 20 tools, 30 s, siła 8, strzela na 2, oblężenie ×3, wymaga: Siege workshop)

### Medicine — nauka

- **Wymaga:** Herbal lore 40 min, Writing 30 min. **Cena:** 150 food, 40 pottery. **Czas:** 80 s.
- **Opis w grze:** Bathhouses; growth +2% a minute; epidemics 30% milder.
- Budynek: **Bathhouse** (koszt 80 stone, 20 pottery, 40 s): 2 miejsc pracy, każdy pracownik na minutę: nic; domy dla 4 osób
- Efekt: przyrost +2% na minutę
- Efekt: epidemia o 30% łagodniejsza

### Philosophy — nauka

- **Wymaga:** Writing 60 min, Mathematics 20 min. **Cena:** 150 clay, 40 textiles. **Czas:** 90 s.
- **Opis w grze:** Academies; rarer thinkers and advisors.
- Budynek: **Academy** (koszt 120 stone, 20 textiles, 50 s): 3 miejsc pracy, każdy pracownik na minutę: nic; badania −10%
- Efekt: lepsza szansa na rzadszych myślicieli i doradców (+40)

### Shipbuilding — handel

- **Wymaga:** Sailing 40 min, Iron working 20 min. **Cena:** 250 wood, 30 iron. **Czas:** 90 s.
- **Opis w grze:** Shipyards, warships.
- Budynek: **Shipyard** (koszt 150 wood, 30 tools, 50 s): 4 miejsc pracy, każdy pracownik na minutę: nic z 2 wood; przy wodzie
- Jednostka: **Warship** (12 os., koszt 150 wood, 10 arms, 35 s, siła 22, strzela na 2, pływa, wymaga: Shipyard)

### Stone walls — wojsko

- **Wymaga:** Masonry 60 min, Construction 20 min. **Cena:** 250 stone. **Czas:** 80 s.
- **Opis w grze:** Stone walls (+110% defence).
- Budynek: **Stone walls** (koszt 150 stone, 45 s): obrona osady +110%

### Iron weapons (U) — wojsko

- **Wymaga:** Iron working 30 min. **Cena:** 40 iron, 60 wood. **Czas:** 70 s.
- **Opis w grze:** Melee +30%.
- Efekt: siła: walka wręcz +30%

### Chain mail (U) — wojsko

- **Wymaga:** Iron working 40 min. **Cena:** 60 iron, 30 hides. **Czas:** 70 s.
- **Opis w grze:** Infantry and cavalry +25%.
- Efekt: siła: piechota +25%
- Efekt: siła: jazda +25%

### Iron tools (U) — przemysł

- **Wymaga:** Iron working 30 min. **Cena:** 40 iron, 30 tools. **Czas:** 65 s.
- **Opis w grze:** Quarries, woodcutters, mines +25%; farms +15%.
- Efekt: produkcja: budynki z grupy „narzędzia” (kamieniołom, drwal, glinianka, kopalnie) +25%
- Efekt: produkcja: Farm +15%

### Crop rotation (U) — jedzenie

- **Wymaga:** Agriculture 120 min, Irrigation 40 min. **Cena:** 200 food, 40 tools. **Czas:** 70 s.
- **Opis w grze:** Farms +25%.
- Efekt: produkcja: Farm +25%


## Średniowiecze (Middle Ages)

### Feudalism — zarządzanie

- **Wymaga:** Construction 40 min, Horseback riding 30 min. **Cena:** 300 food, 60 iron, 200 stone. **Czas:** 120 s.
- **Opis w grze:** Manors, knights.
- Budynek: **Manor** (koszt 200 stone, 100 wood, 60 s): 4 miejsc pracy, każdy pracownik na minutę: 8 food; domy dla 10 osób
- Jednostka: **Knights** (6 os., koszt 80 food, 15 arms, 20 hides, 30 s, siła 40)

### Steel — przemysł

- **Wymaga:** Iron working 60 min. **Cena:** 120 iron, 200 wood, 80 tools. **Czas:** 120 s.
- **Opis w grze:** Steelworks; all soldiers +10%.
- Budynek: **Steelworks** (koszt 150 stone, 40 iron, 60 s): 3 miejsc pracy, każdy pracownik na minutę: 4 tools, 2 arms z 2 iron, 2 wood
- Efekt: siła: wszystko +10%

### Castles — wojsko

- **Wymaga:** Construction 60 min. **Cena:** 400 stone, 40 iron. **Czas:** 110 s.
- **Opis w grze:** Castles (+220% defence).
- Budynek: **Castle** (koszt 400 stone, 100 wood, 30 iron, 90 s): domy dla 10 osób; obrona osady +220%

### Heavy plough — jedzenie

- **Wymaga:** Agriculture 200 min, Iron working 40 min. **Cena:** 60 iron, 150 wood. **Czas:** 90 s.
- **Opis w grze:** Farms +30%.
- Efekt: produkcja: Farm +30%

### Crossbows — wojsko

- **Wymaga:** Iron working 40 min. **Cena:** 60 iron, 100 wood. **Czas:** 90 s.
- **Opis w grze:** Crossbowmen.
- Jednostka: **Crossbowmen** (8 os., koszt 40 food, 10 arms, 20 s, siła 20, strzela na 2)

### Guilds — przemysł

- **Wymaga:** Currency 60 min, Weaving 60 min. **Cena:** 80 textiles, 60 pottery. **Czas:** 100 s.
- **Opis w grze:** Guild halls; workshops +25%.
- Budynek: **Guild hall** (koszt 150 stone, 30 textiles, 50 s): 3 miejsc pracy, każdy pracownik na minutę: nic
- Efekt: produkcja: warsztaty +25%

### Mills — jedzenie

- **Wymaga:** Agriculture 200 min, The wheel 60 min. **Cena:** 200 stone, 150 wood. **Czas:** 90 s.
- **Opis w grze:** Mills; farms +20%.
- Budynek: **Mill** (koszt 80 wood, 60 stone, 10 tools, 40 s): 3 miejsc pracy, każdy pracownik na minutę: 6 food; przy wodzie
- Efekt: produkcja: Farm +20%

### Chivalry — wojsko

- **Wymaga:** Feudalism 20 min. **Cena:** 200 food, 60 iron. **Czas:** 90 s.
- **Opis w grze:** Knights +30%.
- Efekt: siła: Knights +30%

### University — nauka

- **Wymaga:** Philosophy 40 min, Writing 120 min. **Cena:** 300 stone, 80 textiles. **Czas:** 120 s.
- **Opis w grze:** Universities (research −20%).
- Budynek: **University** (koszt 200 stone, 40 textiles, 40 pottery, 60 s): 4 miejsc pracy, każdy pracownik na minutę: nic; badania −20%

### Steel weapons (U) — wojsko

- **Wymaga:** Steel 30 min. **Cena:** 80 iron, 60 wood. **Czas:** 90 s.
- **Opis w grze:** Melee +30%.
- Efekt: siła: walka wręcz +30%

### Plate armour (U) — wojsko

- **Wymaga:** Steel 40 min. **Cena:** 120 iron, 30 textiles. **Czas:** 100 s.
- **Opis w grze:** Infantry and cavalry +30%.
- Efekt: siła: piechota +30%
- Efekt: siła: jazda +30%

### Longbow (U) — wojsko

- **Wymaga:** Steel 10 min. **Cena:** 150 wood, 20 iron. **Czas:** 80 s.
- **Opis w grze:** Archers and crossbowmen +30%.
- Efekt: siła: jednostki dystansowe +30%

### Heavy cavalry (U) — wojsko

- **Wymaga:** Feudalism 40 min. **Cena:** 200 food, 80 iron. **Czas:** 90 s.
- **Opis w grze:** Cavalry +30%.
- Efekt: siła: jazda +30%

### Trebuchet (U) — wojsko

- **Wymaga:** Engineering 40 min. **Cena:** 250 wood, 40 iron. **Czas:** 90 s.
- **Opis w grze:** Siege engines +100% against settlements.
- Efekt: oblężenie +100%


## Jak gracz dostaje te informacje

1. **Okno Research (klawisz 2):** siatka, w której era to kolumna, a kategoria wiersz.
   - Każda technologia to karta: zielona znana, jasna do zbadania od razu, ciemna zablokowana, niebieska badana. Ulepszenia mają „+” przy nazwie.
   - Nad kolumną widać, ile technologii otwiera erę.
   - Kliknięcie jasnej karty zaczyna badanie.
2. **Podpowiedź karty** (po najechaniu myszą):
   - nazwa i opis w jednym zdaniu;
   - lista wyliczona z danych, czyli to, co technologia naprawdę daje: budynki, jednostki, poziom osady, drogi i efekty z liczbami (dodane 2026-11-18);
   - cena i czas;
   - potrzebne doświadczenie;
   - powód blokady, np. „Needs more experience in Hunting: 4 of 15 worker-minutes” albo „Opens in the Neolithic: know 5 technologies of the Stone Age (we know 3)”.
3. **Górny pasek:** licznik ery „1/5 to the Neolithic” i przycisk badania z postępem.
4. **Kronika:** „We have learned X”, a po otwarciu ery: „Our people enter the …”.
5. **Pasek akcji:** budynki nowej technologii pojawiają się w swoich zakładkach. Podpowiedź kafelka podaje koszt, pracę i produkcję.
6. **Panel osady:**
   - kafelki jednostek pokazują się dopiero po technologii; podpowiedź podaje koszt, siłę i wymagany budynek;
   - przycisk awansu podaje, jakiej technologii brakuje.

**Czego gracz dziś nie widzi:**
- **Strzałki zależności:** w siatce nie ma linii między technologiami; zależności są tylko w podpowiedzi.
- **Postęp doświadczenia:** w podpowiedzi blokady widać tylko pierwsze brakujące doświadczenie, a nie wszystkie naraz.
- **Efekt w praktyce:** po zbadaniu ulepszenia gra nie pokazuje, ile realnie zyskało, np. siła oddziału przed i po.
