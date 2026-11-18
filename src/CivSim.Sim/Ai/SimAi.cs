using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;
using CivSim.Sim.State;

namespace CivSim.Sim.Ai;

/// <summary>
/// The AI player (ADR 0028): it plays a people with the same commands as a person and sees only what its people has seen.
/// It thinks every <see cref="ThinkEveryTicks"/> ticks, staggered by people, and does at most a few things each time, so it
/// costs little (docs/wydajnosc.md). Its character (aggression, expansion) shows only in what it does.
/// </summary>
public static class SimAi
{
    /// <summary>Ticks between two thoughts of one AI people.</summary>
    public const int ThinkEveryTicks = 30;

    /// <summary>No AI starts a war before this tick (8 minutes): every people gets time to settle.</summary>
    public const long PeacefulStartTicks = 8 * SimContent.TicksPerMinute;

    private static readonly string[] FoodBuildings = ["farm", "fishing_hut", "gatherers_hut", "hunters_lodge", "pasture", "manor"];

    /// <summary>One thought: looks at the people's state and issues commands through <see cref="SimGame.Apply"/>.</summary>
    public static void Think(SimGame g, Civ civ)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(civ);
        if (civ.Dirty)
        {
            g.Recompute(civ);
        }

        int actions = 0;
        AnswerPeace(g, civ);
        Diplomacy(g, civ);
        Supply(g, civ);
        Research(g, civ);
        Hire(g, civ);
        actions += Economy(g, civ) ? 1 : 0;
        if (actions == 0)
        {
            actions += Upgrade(g, civ) ? 1 : 0;
        }

        Scouts(g, civ);
        Settlers(g, civ);
        if (actions == 0)
        {
            Army(g, civ);
        }

        War(g, civ);
    }

    private static int Key(SimGame g, string key)
    {
        foreach (BuildingType b in g.Content.Buildings)
        {
            if (b.Key == key)
            {
                return b.Id;
            }
        }

        return -1;
    }

    private static bool Affordable(Civ civ, long[] price, int keepPercent = 0)
    {
        for (int i = 0; i < price.Length; i++)
        {
            if (civ.Stock[i] < price[i] * (100 + keepPercent) / 100)
            {
                return false;
            }
        }

        return true;
    }

    // Builds the building on the best free hex of any own settlement: a bonus deposit first, then the hex nearest the centre.
    private static bool TryBuild(SimGame g, Civ civ, string key)
    {
        int id = Key(g, key);
        if (id < 0)
        {
            return false;
        }

        BuildingType t = g.Content.Buildings[id];
        if (!SimGame.Knows(civ, t) || !Affordable(civ, t.Price))
        {
            return false;
        }

        int best = -1;
        int bestScore = int.MinValue;
        foreach (int sid in civ.Settlements)
        {
            Settlement s = g.Settlements[sid];
            if (!SimGame.CanAfford(s.Stock, t.Price, 100))
            {
                continue; // goods lie in each settlement: build where the materials are
            }

            foreach (int cell in s.Cells)
            {
                if (g.CannotBuild(civ, cell, t, checkPrice: false) is not null)
                {
                    continue;
                }

                int score = -g.Geo.Distance(g.Geo.FromIndex(cell), g.Geo.FromIndex(s.Center)) + (s.Idle * 2);
                if (t.BonusDeposit != 0 && g.Map.Deposit[cell] == t.BonusDeposit && g.Map.DepositDepth[cell] == 0)
                {
                    score += 20;
                }

                if (t.BonusReliefs.Length > 0 && t.BonusReliefs[g.Map.Relief[cell]])
                {
                    score += 15;
                }

                if (score > bestScore)
                {
                    best = cell;
                    bestScore = score;
                }
            }
        }

        return best >= 0 && g.Apply(new BuildCommand(civ.Id, best, id)) is null;
    }

    private static int Under(SimGame g, Civ civ)
    {
        int n = 0;
        foreach (int id in civ.Buildings)
        {
            if (g.Buildings[id].State is BuildingState.Delivery or BuildingState.Construction or BuildingState.ConstructionPaused)
            {
                n++;
            }
        }

        return n;
    }

    private static bool Economy(SimGame g, Civ civ)
    {
        if (Under(g, civ) >= 1 + civ.Settlements.Count)
        {
            return false;
        }

        long food = civ.Rate[g.Content.Food];
        if (food < 3 * SimContent.One && !Building(g, civ, FoodBuildings) && Count(g, civ, FoodBuildings) <= civ.People / 5)
        {
            foreach (string k in FoodBuildings)
            {
                if (TryBuild(g, civ, k))
                {
                    return true;
                }
            }
        }

        int idle = 0;
        bool crowded = false;
        foreach (int sid in civ.Settlements)
        {
            Settlement s = g.Settlements[sid];
            idle += s.Idle;
            crowded |= s.People >= s.Housing - 4;
        }

        if (crowded && (TryBuild(g, civ, "stone_house") || TryBuild(g, civ, "hut")))
        {
            return true;
        }

        for (int i = 0; i < civ.Stock.Length; i++)
        {
            if (civ.Stock[i] >= civ.Capacity[i] - SimContent.One && civ.Rate[i] > 0)
            {
                if (TryBuild(g, civ, "great_storehouse") || TryBuild(g, civ, "storehouse"))
                {
                    return true;
                }
            }
        }

        // What teaches the experience the next technology lacks comes first, even without idle hands: it gets workers first.
        string? learn = Teacher(g, civ);
        if (learn is not null && !Has(g, civ, learn) && TryBuild(g, civ, learn))
        {
            g.Apply(new PriorityCommand(civ.Id, civ.Buildings[^1], true));
            return true;
        }

        if (idle < 3)
        {
            return false;
        }

        // Work for the idle: the good we have least of.

        string[] order = (Wanted(g, civ) ?? Lowest(g, civ)) switch
        {
            "wood" => ["woodcutter", "gatherers_hut"],
            "stone" => ["quarry"],
            "clay" => ["clay_pit"],
            "tools" => ["toolmaker", "blacksmith", "steelworks"],
            "copper" => ["copper_mine"],
            "tin" => ["tin_mine"],
            "bronze" => ["bronze_smith"],
            "iron" => ["iron_mine"],
            _ => ["gatherers_hut", "hunters_lodge"],
        };
        foreach (string k in order)
        {
            if (TryBuild(g, civ, k))
            {
                return true;
            }
        }

        foreach (string k in new[] { "school", "academy", "farm", "pasture", "woodcutter", "quarry", "mason", "clay_pit", "toolmaker", "copper_mine", "tin_mine", "bronze_smith", "iron_mine", "blacksmith", "potter", "tailor", "herbalist", "gatherers_hut", "hunters_lodge" })
        {
            if (TryBuild(g, civ, k))
            {
                return true;
            }
        }

        return false;
    }

    // True when the people has a building of this kind (built or being built).
    private static bool Has(SimGame g, Civ civ, string key)
    {
        foreach (int id in civ.Buildings)
        {
            if (g.Content.Buildings[g.Buildings[id].Type].Key == key)
            {
                return true;
            }
        }

        return false;
    }

    private static int Count(SimGame g, Civ civ, string[] keys)
    {
        int n = 0;
        foreach (int id in civ.Buildings)
        {
            n += Array.IndexOf(keys, g.Content.Buildings[g.Buildings[id].Type].Key) >= 0 ? 1 : 0;
        }

        return n;
    }

    // True when a building of one of these kinds is being built.
    private static bool Building(SimGame g, Civ civ, string[] keys)
    {
        foreach (int id in civ.Buildings)
        {
            Building b = g.Buildings[id];
            if (b.State is BuildingState.Delivery or BuildingState.Construction or BuildingState.ConstructionPaused && Array.IndexOf(keys, g.Content.Buildings[b.Type].Key) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    // A building with jobs whose workers give the experience the nearest technology still lacks.
    private static string? Teacher(SimGame g, Civ civ)
    {
        foreach (SimTech t in g.Content.Techs)
        {
            if (civ.KnownTech[t.Id])
            {
                continue;
            }

            // Goods to make: the first building that makes the missing good.
            foreach ((int good, long amount) in t.Made)
            {
                if (civ.Made[good] / SimContent.TicksPerMinute >= amount)
                {
                    continue;
                }

                foreach (BuildingType b in g.Content.Buildings)
                {
                    if (b.Id > 0 && b.Produces[good] > 0 && SimGame.Knows(civ, b))
                    {
                        return b.Key;
                    }
                }
            }

            foreach ((int req, long need) in t.Experience)
            {
                if (!civ.KnownTech[req] || civ.Experience[req] >= need)
                {
                    continue;
                }

                foreach (BuildingType b in g.Content.Buildings)
                {
                    if (b.Id > 0 && b.Tech == req && b.Workers > 0 && (b.Consumes.Length == 0 || NoInputsMissing(civ, b)))
                    {
                        bool any = false;
                        foreach (int sid in civ.Settlements)
                        {
                            foreach (int cell in g.Settlements[sid].Cells)
                            {
                                if (g.CannotBuild(civ, cell, b, checkPrice: false) is null)
                                {
                                    any = true;
                                    break;
                                }
                            }

                            if (any)
                            {
                                break;
                            }
                        }

                        if (any)
                        {
                            return b.Key;
                        }
                    }
                }
            }
        }

        return null;
    }

    private static bool NoInputsMissing(Civ civ, BuildingType b)
    {
        for (int i = 0; i < b.Consumes.Length; i++)
        {
            if (b.Consumes[i] > 0 && civ.Rate[i] <= 0 && civ.Stock[i] < 20 * SimContent.One)
            {
                return false;
            }
        }

        return true;
    }

    // A good the next technology's price lacks.
    private static string? Wanted(SimGame g, Civ civ)
    {
        foreach (SimTech t in g.Content.Techs)
        {
            if (g.CannotResearch(civ, t.Id) is not null)
            {
                continue;
            }

            long[] price = g.ResearchPrice(civ, t.Id);
            for (int i = 0; i < price.Length; i++)
            {
                if (civ.Stock[i] < price[i] && i != g.Content.Food)
                {
                    return g.Content.Goods[i].Key;
                }
            }
        }

        return null;
    }

    // The known good (one we can make) whose stock is lowest.
    private static string Lowest(SimGame g, Civ civ)
    {
        string[] makeable = ["wood", "stone", "clay", "tools", "copper", "tin", "bronze", "iron"];
        string best = "";
        long bestStock = long.MaxValue;
        foreach (string k in makeable)
        {
            int good = g.Content.GoodId(k);
            bool can = false;
            foreach (BuildingType b in g.Content.Buildings)
            {
                can |= b.Id > 0 && b.Produces[good] > 0 && SimGame.Knows(civ, b);
            }

            if (can && civ.Stock[good] < bestStock)
            {
                best = k;
                bestStock = civ.Stock[good];
            }
        }

        return best;
    }

    private static void Research(SimGame g, Civ civ)
    {
        if (civ.Researching >= 0)
        {
            return;
        }

        int pick = -1;
        int bestScore = int.MinValue;
        foreach (SimTech t in g.Content.Techs)
        {
            if (g.CannotResearch(civ, t.Id) is not null)
            {
                continue;
            }

            // Warlike peoples like the technologies of arms, builders the others; earlier eras first, the cheap basics before all.
            int score = (-t.Era * 10) + (t.Basic ? 30 : 0);
            foreach (UnitType u in g.Content.Units)
            {
                if (u.Army && u.Tech == t.Id)
                {
                    score += (civ.Aggression - 50) / 5;
                }
            }

            if (score > bestScore && Affordable(civ, g.ResearchPrice(civ, t.Id)))
            {
                pick = t.Id;
                bestScore = score;
            }
        }

        if (pick >= 0)
        {
            g.Apply(new ResearchCommand(civ.Id, pick));
        }
    }

    private static void Hire(SimGame g, Civ civ)
    {
        if (civ.ThinkerOffer is { } p && civ.Thinker is { } cur && p.Rarity > cur.Rarity && Affordable(civ, g.HirePrice(p, false), 50))
        {
            g.Apply(new HireCommand(civ.Id, false));
        }
    }

    private static bool Upgrade(SimGame g, Civ civ)
    {
        foreach (int sid in civ.Settlements)
        {
            Settlement s = g.Settlements[sid];
            if (s.Level + 1 < g.Content.Levels.Count && s.People >= s.Housing - 2 && Affordable(civ, g.Content.Levels[s.Level + 1].UpgradePrice, 20)
                && g.Apply(new UpgradeSettlementCommand(civ.Id, sid)) is null)
            {
                return true;
            }
        }

        return false;
    }

    private static int Own(SimGame g, Civ civ, Func<UnitType, bool> kind, bool includeTraining = true)
    {
        int n = 0;
        foreach (int id in civ.Units)
        {
            Unit u = g.Units[id];
            if (kind(g.Content.Units[u.Kind]) && (includeTraining || u.State != UnitState.Training))
            {
                n++;
            }
        }

        return n;
    }

    private static void Scouts(SimGame g, Civ civ)
    {
        UnitType scouts = g.Content.Unit("scouts");
        if (Own(g, civ, t => t.Key == "scouts") == 0 && g.Ticks < 20 * SimContent.TicksPerMinute && Affordable(civ, scouts.Price, 50))
        {
            g.Apply(new TrainCommand(civ.Id, civ.Settlements[0], scouts.Id));
        }

        foreach (int id in civ.Units.ToArray())
        {
            Unit u = g.Units[id];
            if (u.Kind == scouts.Id && u.State == UnitState.Idle)
            {
                // Walk to an unknown land hex some way off; the hex is picked from the AI's own random stream.
                for (int tries = 0; tries < 12; tries++)
                {
                    HexCoord c = g.Geo.FromIndex(u.Cell);
                    int dc = g.AiRandom.NextInt(-14, 15);
                    int dr = g.AiRandom.NextInt(-10, 11);
                    int cell = g.Geo.Index(g.Geo.Wrap(c.Col + dc, Math.Clamp(c.Row + dr, 0, g.Geo.Height - 1)));
                    if (!civ.Known[cell] && g.Apply(new MoveCommand(civ.Id, u.Id, cell, false)) is null)
                    {
                        break;
                    }
                }
            }
        }
    }

    private static void Settlers(SimGame g, Civ civ)
    {
        UnitType settlers = g.Content.Unit("settlers");
        int cap = 2 + (civ.Expansion / 25) + g.EraOf(civ);
        if (Own(g, civ, t => t.Founds) == 0 && civ.Settlements.Count < cap && civ.Rate[g.Content.Food] > 0)
        {
            foreach (int sid in civ.Settlements)
            {
                Settlement s = g.Settlements[sid];
                if (s.People >= 20 && Affordable(civ, settlers.Price, 30))
                {
                    g.Apply(new TrainCommand(civ.Id, sid, settlers.Id));
                    break;
                }
            }
        }

        foreach (int id in civ.Units.ToArray())
        {
            Unit u = g.Units[id];
            if (u.Kind != settlers.Id || u.State != UnitState.Idle)
            {
                continue;
            }

            if (g.CannotFound(u.Cell) is null)
            {
                g.Apply(new FoundCommand(civ.Id, u.Id));
                continue;
            }

            int site = BestSite(g, civ, u.Cell);
            if (site >= 0)
            {
                g.Apply(new MoveCommand(civ.Id, u.Id, site, false));
            }
        }
    }

    // A known free land hex 6–12 hexes away where a camp may stand, with the most food-friendly land around.
    private static int BestSite(SimGame g, Civ civ, int from)
    {
        HexCoord c = g.Geo.FromIndex(from);
        int best = -1;
        int bestScore = int.MinValue;
        var ring = new HexCoord[HexGlobe.RingSize(12)];
        for (int r = 6; r <= 12; r += 2)
        {
            int n = g.Geo.Ring(c, r, ring);
            for (int k = 0; k < n; k += 2)
            {
                int cell = g.Geo.Index(ring[k]);
                if (!civ.Known[cell] || g.CannotFound(cell) is not null)
                {
                    continue;
                }

                int score = g.Content.World.Biomes[g.Map.Biome[cell]].Food * 10 + (g.NextToWater(cell) ? 15 : 0) - r;
                if (score > bestScore)
                {
                    best = cell;
                    bestScore = score;
                }
            }
        }

        return best;
    }

    private static UnitType? BestSoldier(SimGame g, Civ civ)
    {
        UnitType? best = null;
        foreach (UnitType t in g.Content.Units)
        {
            if (t.Army && !t.Levy && !t.Naval && SimGame.Knows(civ, t) && (best is null || t.Strength > best.Strength))
            {
                best = t;
            }
        }

        return best;
    }

    private static void Army(SimGame g, Civ civ)
    {
        int soldiers = 0;
        foreach (int id in civ.Units)
        {
            Unit u = g.Units[id];
            soldiers += g.Content.Units[u.Kind].Army ? u.People : 0;
        }

        int want = civ.People * (5 + (civ.Aggression / 5)) / 100;
        if (soldiers >= want || civ.Rate[g.Content.Food] < 2 * SimContent.One)
        {
            return;
        }

        UnitType? t = BestSoldier(g, civ);
        if (t is null || !Affordable(civ, t.Price, 20))
        {
            return;
        }

        foreach (int sid in civ.Settlements)
        {
            if (g.Settlements[sid].People >= t.People + 15)
            {
                g.Apply(new TrainCommand(civ.Id, sid, t.Id));
                return;
            }
        }
    }

    private static void War(SimGame g, Civ civ)
    {
        long power = 0;
        var idle = new List<Unit>();
        foreach (int id in civ.Units)
        {
            Unit u = g.Units[id];
            if (g.Content.Units[u.Kind].Army && u.State != UnitState.Training)
            {
                power += g.Power(u);
                if (u.TargetSettlement < 0 && u.TargetUnit < 0)
                {
                    idle.Add(u);
                }
            }
        }

        // Defend: enemies seen on our land are attacked first.
        foreach (Unit enemy in g.ActiveUnits())
        {
            if (enemy.Civ == civ.Id || !g.Content.Units[enemy.Kind].Army)
            {
                continue;
            }

            int owner = g.OwnerAt[enemy.Cell];
            if (owner == civ.Id && g.AtWar(civ.Id, enemy.Civ) && g.Sees(civ, enemy))
            {
                foreach (Unit u in idle)
                {
                    g.Apply(new AttackCommand(civ.Id, u.Id, enemy.Cell));
                }

                return;
            }
        }

        foreach (Civ other in g.Civs)
        {
            if (other.Id == civ.Id || other.Dead || !g.Met(civ, other))
            {
                continue;
            }

            Settlement? target = NearestKnownSettlement(g, civ, other);
            if (g.AtWar(civ.Id, other.Id))
            {
                // Peace when the war has turned against us or dragged on; the course of the war sets the terms.
                int mine = g.Score(civ.Id, other.Id);
                int theirs = g.Score(other.Id, civ.Id);
                if ((theirs > mine + 20 || g.AiRandom.NextInt(100) < 3) && !g.PeaceOffers.Exists(o => o.From == civ.Id && o.To == other.Id))
                {
                    g.Apply(new ProposePeaceCommand(civ.Id, other.Id, g.SuggestedTribute(civ.Id, other.Id)));
                }

                if (target is not null && idle.Count > 0 && power > 0)
                {
                    foreach (Unit u in idle)
                    {
                        g.Apply(new AttackCommand(civ.Id, u.Id, target.Center));
                    }
                }

                continue;
            }

            // Start a war only after the first minutes, when strong enough against the target's defenders and warlike enough;
            // allies are left alone, trade partners only by the most warlike.
            if (target is null || civ.Aggression < 40 || idle.Count == 0 || g.Ticks < PeacefulStartTicks
                || g.Has(civ.Id, other.Id, TreatyKind.Alliance) || (g.Has(civ.Id, other.Id, TreatyKind.Trade) && civ.Aggression < 85))
            {
                continue;
            }

            long defence = (long)target.People * g.Content.War.DefenderPercent / 100 * g.Content.War.DefenderStrength * g.Defense(target);
            if (power > defence * 2 && g.AiRandom.NextInt(100) < civ.Aggression / 10)
            {
                if (civ.Aggression < 75)
                {
                    g.Apply(new DeclareWarCommand(civ.Id, other.Id));
                }

                foreach (Unit u in idle)
                {
                    g.Apply(new AttackCommand(civ.Id, u.Id, target.Center));
                }

                return;
            }
        }
    }

    private static Settlement? NearestKnownSettlement(SimGame g, Civ civ, Civ other)
    {
        Settlement? best = null;
        int bestD = int.MaxValue;
        HexCoord home = g.Geo.FromIndex(g.Settlements[civ.Settlements[0]].Center);
        foreach (int sid in other.Settlements)
        {
            Settlement s = g.Settlements[sid];
            if (!civ.Known[s.Center])
            {
                continue;
            }

            int d = g.Geo.Distance(home, g.Geo.FromIndex(s.Center));
            if (d < bestD)
            {
                best = s;
                bestD = d;
            }
        }

        return best;
    }

    // Answers treaty offers at once. Offers a treaty only when every condition of docs/sim holds (known long enough, close,
    // no recent fighting, good name, and something to gain); the cooldowns live in the treaty rules themselves.
    private static void Diplomacy(SimGame g, Civ civ)
    {
        foreach (TreatyOffer offer in g.TreatyOffers.ToArray())
        {
            if (offer.To != civ.Id)
            {
                continue;
            }

            Civ from = g.Civs[offer.From];
            bool accept = from.Reputation > g.Content.TreatyMinReputation && Interest(g, civ, from, offer.Kind) &&
                (offer.Kind == TreatyKind.Trade ? civ.Aggression < 85 : civ.Aggression < 70 && from.Reputation >= 0);
            g.Apply(new AnswerTreatyCommand(civ.Id, offer.From, offer.Kind, accept));
        }

        foreach (Civ other in g.Civs)
        {
            if (!g.TreatyConditions(civ, other) || g.Ticks < g.LastOffer(civ.Id, other.Id))
            {
                continue;
            }

            if (!g.Has(civ.Id, other.Id, TreatyKind.Trade) && civ.Aggression < 70 && Interest(g, civ, other, TreatyKind.Trade))
            {
                g.Apply(new ProposeTreatyCommand(civ.Id, other.Id, TreatyKind.Trade));
                return;
            }

            if (g.Has(civ.Id, other.Id, TreatyKind.Trade) && !g.Has(civ.Id, other.Id, TreatyKind.Alliance) && civ.Aggression < 50
                && other.Reputation >= 0 && Interest(g, civ, other, TreatyKind.Alliance))
            {
                g.Apply(new ProposeTreatyCommand(civ.Id, other.Id, TreatyKind.Alliance));
                return;
            }
        }
    }

    // Feeds a hungry settlement from the one with the most food, with a caravan.
    private static void Supply(SimGame g, Civ civ)
    {
        // Constructions whose porters were killed: the AI sends the missing materials again when it has them.
        foreach (int id in civ.Buildings)
        {
            Building b = g.Buildings[id];
            if (b.State == BuildingState.Delivery && b.Porters == 0)
            {
                g.Apply(new BringMaterialsCommand(civ.Id, b.Id));
            }
        }

        foreach (int sid in civ.Settlements)
        {
            Settlement s = g.Settlements[sid];
            if (!s.Hungry)
            {
                continue;
            }

            Settlement? rich = null;
            foreach (int o in civ.Settlements)
            {
                Settlement r = g.Settlements[o];
                if (r != s && r.Stock[g.Content.Food] > 60 * SimContent.One && (rich is null || r.Stock[g.Content.Food] > rich.Stock[g.Content.Food]))
                {
                    rich = r;
                }
            }

            if (rich is not null && !Carrying(g, civ, s.Id))
            {
                g.Apply(new SendCaravanCommand(civ.Id, rich.Id, s.Id, g.Content.Food, 40 * SimContent.One));
            }
        }
    }

    private static bool Carrying(SimGame g, Civ civ, int to)
    {
        foreach (int id in civ.Units)
        {
            Unit u = g.Units[id];
            if (u.Job == CarrierJob.ToSettlement && u.Target == to)
            {
                return true;
            }
        }

        return false;
    }

    // What the AI gains: trade when the other makes a good it lacks, or knows a craft it does not (seen in their land);
    // an alliance when both fight the same enemy.
    private static bool Interest(SimGame g, Civ civ, Civ other, TreatyKind kind)
    {
        if (kind == TreatyKind.Alliance)
        {
            foreach (Civ third in g.Civs)
            {
                if (third.Id != civ.Id && third.Id != other.Id && g.AtWar(civ.Id, third.Id) && g.AtWar(other.Id, third.Id))
                {
                    return true;
                }
            }

            return false;
        }

        foreach (int id in other.Buildings)
        {
            Building b = g.Buildings[id];
            if (!civ.Known[b.Cell] || b.State != BuildingState.Active)
            {
                continue;
            }

            BuildingType t = g.Content.Buildings[b.Type];
            if (!SimGame.Knows(civ, t))
            {
                return true;
            }

            for (int i = 0; i < t.Produces.Length; i++)
            {
                if (t.Produces[i] > 0 && civ.Rate[i] <= 0 && civ.Stock[i] < 20 * SimContent.One)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void AnswerPeace(SimGame g, Civ civ)
    {
        foreach (PeaceOffer offer in g.PeaceOffers.ToArray())
        {
            if (offer.To != civ.Id)
            {
                continue;
            }

            // Accept terms no worse than the course of the war suggests, with some give from peaceful characters.
            int fair = g.SuggestedTribute(offer.From, civ.Id);
            bool accept = offer.TributePercent <= fair + ((100 - civ.Aggression) / 10);
            g.Apply(new AnswerPeaceCommand(civ.Id, offer.From, accept));
        }
    }
}
