using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Core.Map;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>A change of the game asked for by a player or the AI. Both use the same commands.</summary>
public abstract record SimCommand(int Civ);

/// <summary>Build <paramref name="Type"/> on hex <paramref name="Cell"/>.</summary>
public sealed record BuildCommand(int Civ, int Cell, int Type) : SimCommand(Civ);

/// <summary>Pause (or resume, when <paramref name="Pause"/> is false) a construction.</summary>
public sealed record PauseConstructionCommand(int Civ, int Building, bool Pause) : SimCommand(Civ);

/// <summary>Stop a working building (its people go idle) or start it again.</summary>
public sealed record StopBuildingCommand(int Civ, int Building, bool Stop) : SimCommand(Civ);

/// <summary>Give a building its workers first.</summary>
public sealed record PriorityCommand(int Civ, int Building, bool Priority) : SimCommand(Civ);

/// <summary>Tear a building down (no refund).</summary>
public sealed record DemolishCommand(int Civ, int Building) : SimCommand(Civ);

/// <summary>Upgrade a building in place to its better kind (Patryk, 2026-11-19).</summary>
public sealed record UpgradeBuildingCommand(int Civ, int Building) : SimCommand(Civ);

/// <summary>Raise a settlement one level (camp → village → town → city).</summary>
public sealed record UpgradeSettlementCommand(int Civ, int Settlement) : SimCommand(Civ);

public sealed partial class SimGame
{
    /// <summary>Checks and carries out a command. Returns null when done, otherwise the reason it was refused.</summary>
    public string? Apply(SimCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if ((uint)command.Civ >= (uint)Civs.Count || Civs[command.Civ].Dead)
        {
            return "No such people.";
        }

        Civ civ = Civs[command.Civ];
        if (Picking != command is PickStartCommand)
        {
            return Picking ? "First choose where our people starts." : "The start has been chosen.";
        }

        return command switch
        {
            PickStartCommand c => PickStart(civ, c.Cell),
            BuildCommand c => Build(civ, c.Cell, c.Type),
            PauseConstructionCommand c => PauseConstruction(civ, c.Building, c.Pause),
            StopBuildingCommand c => StopBuilding(civ, c.Building, c.Stop),
            PriorityCommand c => WithOwn(civ, c.Building, b =>
            {
                b.Priority = c.Priority;
                civ.Dirty = true;
                return null;
            }),
            DemolishCommand c => WithOwn(civ, c.Building, b =>
            {
                b.State = BuildingState.Removed;
                TakeOffHex(b);
                civ.Buildings.Remove(b.Id);
                Map.MarkCellDirty(b.Cell);
                civ.Dirty = true;
                return null;
            }),
            UpgradeSettlementCommand c => Upgrade(civ, c.Settlement),
            UpgradeBuildingCommand c => UpgradeBuilding(civ, c.Building),
            DebugCommand c => Debug(civ, c.Action),
            ResumePatrolCommand c => ResumePatrol(civ, c.Unit),
            TrainCommand c => Train(civ, c.Settlement, c.Kind, c.People, c.Shock),
            SplitCommand c => Split(civ, c.Unit, c.People),
            RefitCommand c => Refit(civ, c.Unit),
            StanceCommand c => SetStance(civ, c.Unit, c.Stance),
            GuardCommand c => SetGuard(civ, c.Unit, c.Cells),
            FormationCommand c => Formation(civ, c.Units, c.Cell, c.Facing),
            MoveCommand c => Move(civ, c.Unit, c.Cell, c.Hidden, c.Path),
            PatrolCommand c => Patrol(civ, c.Unit, c.Loop ?? []),
            BuildRoadCommand c => BuildRoad(civ, c.Cells ?? [], c.Level),
            GatherCommand c => Gather(civ, c.Settlement, c.On),
            BuildWorkCommand c => BuildWork(civ, c.Cells ?? [], c.Work),
            SetFocusCommand c => SetFocus(civ, c.Building, c.Worker, c.Category),
            BoardCommand c => Board(civ, c.Unit, c.Ship),
            LandCommand c => Land(civ, c.Ship, c.Cell),
            RemoveWorkCommand c => RemoveWork(civ, c.Cell, c.Work),
            AttackWallCommand c => AttackWall(civ, c.Unit, c.Cell),
            FoundCommand c => Found(civ, c.Unit),
            ResearchCommand c => Research(civ, c.Tech),
            HireCommand c => Hire(civ, c.Advisor),
            DismissAdvisorCommand => Dismiss(civ),
            SendCaravanCommand c => SendCaravan(civ, c.From, c.To, c.Good, c.Amount),
            RouteCommand c => AddRoute(civ, c.From, c.To, c.Good, c.Amount, c.Path, c.KeepAtSource, c.UntilTargetHas),
            BringMaterialsCommand c => BringMaterials(civ, c.Building),
            EndRouteCommand c => EndRoute(civ, c.Route),
            DeclareWarCommand c => DeclareWar(civ, c.Other, surprise: false),
            AttackCommand c => Attack(civ, c.Unit, c.Cell),
            ProposePeaceCommand c => NoPeace ? "This test scenario allows no peace." : ProposePeace(civ, c.Other, c.TributePercent),
            AnswerPeaceCommand c => AnswerPeace(civ, c.From, c.Accept),
            DisbandCommand c => Disband(civ, c.Unit),
            ProposeTreatyCommand c => ProposeTreaty(civ, c.Other, c.Kind),
            AnswerTreatyCommand c => AnswerTreaty(civ, c.From, c.Kind, c.Accept),
            CancelTreatyCommand c => CancelTreaty(civ, c.Other, c.Kind),
            BarterCommand c => Barter(civ, c.Other, c.GiveGood, c.GiveAmount, c.WantGood, c.WantAmount),
            AnswerBarterCommand c => AnswerBarter(civ, c.From, c.Accept),
            _ => "Unknown command.",
        };
    }

    /// <summary>Why <paramref name="type"/> cannot be built on <paramref name="cell"/> by <paramref name="civ"/> (null = it can).</summary>
    public string? CannotBuild(Civ civ, int cell, BuildingType type, bool checkPrice = true)
    {
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(type);
        if ((uint)cell >= (uint)Geo.CellCount)
        {
            return "Outside the map.";
        }

        if (!Knows(civ, type))
        {
            return $"{type.Name} needs {NeedText(type.Tech, type.AlsoTechs)}.";
        }

        if (Array.IndexOf(type.Groups, "road") >= 0)
        {
            // Roads may run beyond our land, on free land we know, but must start from a settlement that pays for them.
            if (OwnerAt[cell] >= 0 && OwnerAt[cell] != civ.Id)
            {
                return "Not on another people's land.";
            }

            if (!civ.Known[cell] || BuildingAt[cell] >= 0 || Content.World.Biomes[Map.Biome[cell]].IsWater || (SettlementAt[cell] >= 0 && Settlements[SettlementAt[cell]].Center == cell))
            {
                return "A road needs free land we know.";
            }

            Settlement? payer = NearestOwn(civ, cell);
            if (payer is null || Geo.Distance(Geo.FromIndex(payer.Center), Geo.FromIndex(cell)) > 25)
            {
                return "Too far from our settlements.";
            }

            return checkPrice && !CanAfford(payer.Stock, type.Price, 100) ? $"Not enough goods in {payer.Name}: a road costs {PriceText(type.Price)}." : null;
        }

        if (OwnerAt[cell] != civ.Id)
        {
            return "Build only on your own land.";
        }

        if (Settlements[SettlementAt[cell]].Center == cell)
        {
            return "The settlement's centre fills this hex.";
        }

        if (SlotsFor(cell, type.Size) == 0)
        {
            int taken = SlotsPerHex - System.Numerics.BitOperations.PopCount((uint)FreeSlots(cell));
            return type.Size == 4 ? $"{type.Name} needs the whole hex ({taken} of 4 places are taken)." : $"No room for {type.Name.ToLowerInvariant()} here: {taken} of 4 places are taken.";
        }

        if (!TerrainAllows(cell, type, civ))
        {
            return $"{type.Name} needs {Needs(type)}.";
        }

        if (checkPrice && !CanAfford(Settlements[SettlementAt[cell]].Stock, type.Price, 100))
        {
            return $"Not enough goods in {Settlements[SettlementAt[cell]].Name}: {type.Name} costs {PriceText(type.Price)}.";
        }

        return null;
    }

    /// <summary>True when the terrain of the hex suits the building.</summary>
    public bool TerrainAllows(int cell, BuildingType type, Civ? civ = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        // A deposit counts once the people knows it (on its own land: from the era that recognises it).
        if (type.OrDeposit != 0 && Map.Deposit[cell] == type.OrDeposit && (civ is null ? Map.DepositDepth[cell] == 0 : KnowsDeposit(civ, cell)))
        {
            return true;
        }

        bool anyBiome = Array.IndexOf(type.Biomes, true) >= 0;
        bool anyRelief = Array.IndexOf(type.Reliefs, true) >= 0;
        if (anyBiome && !type.Biomes[Map.Biome[cell]] && !(type.OrTrees && Trees[cell] == Grove))
        {
            return false;
        }

        if (anyRelief && !type.Reliefs[Map.Relief[cell]])
        {
            return false;
        }

        if (type.NeedsWater && !NextToWater(cell))
        {
            return false;
        }

        if (type.MinFertility > 0 && Map.Fertility[cell] < type.MinFertility - (civ is null ? 0 : Bonus(civ, EffectKind.Fertility, type.Key, type.Groups)))
        {
            return false;
        }

        // Only a deposit (no biome or relief rule) means the deposit is required.
        return type.OrDeposit == 0 || anyBiome || anyRelief || type.NeedsWater;
    }

    /// <summary>True when a hex touches water: a water hex next to it or a river on one of its edges.</summary>
    public bool NextToWater(int cell)
    {
        HexCoord c = Geo.FromIndex(cell);
        for (int d = 0; d < 6; d++)
        {
            var dir = (HexDirection)d;
            if (Map.River[Geo.EdgeIndex(Geo.Edge(c, dir))] > 0 || Content.World.Biomes[Map.Biome[Geo.Index(Geo.Neighbor(c, dir))]].IsWater)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Price as text, e.g. "20 wood, 10 stone".</summary>
    public string PriceText(long[] price)
    {
        ArgumentNullException.ThrowIfNull(price);
        var parts = new System.Collections.Generic.List<string>();
        for (int g = 0; g < price.Length; g++)
        {
            if (price[g] > 0)
            {
                parts.Add($"{price[g] / SimContent.One} {Content.Goods[g].Key}");
            }
        }

        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }

    /// <summary>True when a store holds the price (percent of it).</summary>
    public static bool CanAfford(long[] stock, long[] price, int percent)
    {
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(price);
        for (int g = 0; g < price.Length; g++)
        {
            if (stock[g] < price[g] * percent / 100)
            {
                return false;
            }
        }

        return true;
    }

    private static void Pay(long[] stock, long[] price, int percent)
    {
        for (int g = 0; g < price.Length; g++)
        {
            stock[g] -= price[g] * percent / 100;
        }
    }

    /// <summary>True when all the people's settlements together hold the price (research, hiring, tribute).</summary>
    public bool CivCanAfford(Civ civ, long[] price)
    {
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(price);
        for (int g = 0; g < price.Length; g++)
        {
            long have = 0;
            foreach (int sid in civ.Settlements)
            {
                have += Settlements[sid].Stock[g];
            }

            if (have < price[g])
            {
                return false;
            }
        }

        return true;
    }

    // Pays a price of the whole people: the capital first, then the other settlements in founding order.
    private void CivPay(Civ civ, long[] price)
    {
        for (int g = 0; g < price.Length; g++)
        {
            long need = price[g];
            foreach (int sid in civ.Settlements)
            {
                long take = Math.Min(need, Settlements[sid].Stock[g]);
                Settlements[sid].Stock[g] -= take;
                need -= take;
            }
        }

        SyncTotals(civ);
    }

    /// <summary>What the terrain of a hex must have for <paramref name="t"/>, as text.</summary>
    public string Needs(BuildingType t)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (Array.IndexOf(t.Biomes, true) >= 0)
        {
            var names = new System.Collections.Generic.List<string>();
            for (int i = 0; i < t.Biomes.Length; i++)
            {
                if (t.Biomes[i])
                {
                    names.Add(Content.World.Biomes[i].Key.Replace('_', ' '));
                }
            }

            parts.Add(string.Join(" or ", names));
        }

        if (Array.IndexOf(t.Reliefs, true) >= 0)
        {
            parts.Add(t.Reliefs[2] && t.Reliefs[1] ? "hills or mountains" : t.Reliefs[1] ? "hills" : "mountains");
        }

        if (t.NeedsWater)
        {
            parts.Add("water next to it");
        }

        if (t.MinFertility > 0)
        {
            parts.Add("fertile soil");
        }

        string text = string.Join(", ", parts);
        if (t.OrDeposit != 0)
        {
            string dep = Content.World.Deposit(t.OrDeposit).Key.Replace('_', ' ');
            text = text.Length == 0 ? dep : $"{text} (or {dep} on the hex)";
        }

        return text;
    }

    private string? Build(Civ civ, int cell, int typeId)
    {
        if (typeId <= 0 || typeId >= Content.Buildings.Count)
        {
            return "Unknown building.";
        }

        BuildingType t = Content.Buildings[typeId];
        string? why = CannotBuild(civ, cell, t);
        if (why is not null)
        {
            return why;
        }

        Place(civ, cell, t, SlotsFor(cell, t.Size), null);
        if (t.OrDeposit == 0 && Map.Deposit[cell] != t.BonusDeposit && KnowsDeposit(civ, cell) && Mineable(cell) && !civ.IsAi)
        {
            // M2 (2026-11-19): allowed, the deposit stays, but no mine fits while this stands.
            News.Add((civ.Id, $"{t.Name} stands on a {Content.World.Deposit(Map.Deposit[cell]).Key.Replace('_', ' ')} deposit: the deposit stays, but its mine needs the hex free."));
        }

        return null;
    }

    // A new construction on the hex, in the given places. Credit: materials already at the site (an upgrade's old building).
    private Building Place(Civ civ, int cell, BuildingType t, int slots, long[]? credit)
    {
        int typeId = t.Id;
        int yield = 100;
        if (t.BonusDeposit != 0 && Map.Deposit[cell] == t.BonusDeposit && KnowsDeposit(civ, cell))
        {
            yield += t.BonusPercent;
        }

        if (t.BonusReliefs.Length > 0 && t.BonusReliefs[Map.Relief[cell]])
        {
            yield += t.BonusReliefPercent;
        }

        // A woodcutter on a grove: a grove's share of a forest's wood.
        if (t.OrTrees && Trees[cell] == Grove && !t.Biomes[Map.Biome[cell]])
        {
            yield = yield * Content.GroveWoodPercent / 100;
        }

        int sid = SettlementAt[cell] >= 0 && Settlements[SettlementAt[cell]].Civ == civ.Id ? SettlementAt[cell] : NearestOwn(civ, cell)!.Id;
        var b = new Building
        {
            Id = Buildings.Count,
            Type = typeId,
            Cell = cell,
            Civ = civ.Id,
            Settlement = sid,
            State = BuildingState.Delivery,
            YieldPercent = yield,
            RemainingTicks = Math.Max(1, t.BuildTicks),
            Delivered = new long[Content.Goods.Count],
            OnTheWay = new long[Content.Goods.Count],
            Speed = 100 + SupportOf(civ, sid, x => x.BuildPercent),
            Focus = t.ResearchPercent > 0 ? DefaultFocus(t.Workers) : [],
        };
        Buildings.Add(b);
        civ.Buildings.Add(b.Id);
        PutOnHex(b, slots);
        Map.MarkCellDirty(cell);
        if (credit is not null)
        {
            Deliver(b, credit); // what the old building gives is at the site already: the work may start on it
        }

        SendMaterials(b); // porters carry the price from the nearest storehouse; the work goes as far as the delivered materials allow
        SyncTotals(civ);
        return b;
    }

    private string? PauseConstruction(Civ civ, int id, bool pause) => WithOwn(civ, id, b =>
    {
        BuildingType t = Content.Buildings[b.Type];
        if (pause)
        {
            if (b.State != BuildingState.Construction)
            {
                return "Only a construction can be paused.";
            }

            b.RemainingTicks = Math.Max(1, t.BuildTicks) - WorkDone(b);
            b.State = BuildingState.ConstructionPaused;
            return null;
        }

        if (b.State != BuildingState.ConstructionPaused)
        {
            return "This construction is not paused.";
        }

        long[] stock = Settlements[b.Settlement].Stock;
        if (!CanAfford(stock, t.Price, Content.ResumeCostPercent))
        {
            return $"Resuming costs {Content.ResumeCostPercent}% of the price again.";
        }

        Pay(stock, t.Price, Content.ResumeCostPercent);
        SyncTotals(civ);
        b.State = BuildingState.Delivery;
        Work(b); // goes on as far as the materials at the site allow
        return null;
    });

    private string? StopBuilding(Civ civ, int id, bool stop) => WithOwn(civ, id, b =>
    {
        if (stop && b.State == BuildingState.Active)
        {
            b.State = BuildingState.Stopped;
        }
        else if (!stop && b.State == BuildingState.Stopped)
        {
            b.State = BuildingState.Active;
        }
        else
        {
            return stop ? "Only a working building can be stopped." : "This building is not stopped.";
        }

        civ.Dirty = true;
        return null;
    });

    private string? Upgrade(Civ civ, int sid)
    {
        if ((uint)sid >= (uint)Settlements.Count || Settlements[sid].Civ != civ.Id || Settlements[sid].Abandoned)
        {
            return "Not your settlement.";
        }

        Settlement s = Settlements[sid];
        if (s.Level + 1 >= Content.Levels.Count)
        {
            return "This settlement cannot grow further.";
        }

        SettlementLevel next = Content.Levels[s.Level + 1];
        if (!Knows(civ, next.Tech, next.AlsoTechs))
        {
            return $"A {next.Name.ToLowerInvariant()} needs {NeedText(next.Tech, next.AlsoTechs)}.";
        }

        if (s.People < next.MinPeople)
        {
            return $"A {next.Name.ToLowerInvariant()} needs {next.MinPeople} people; {s.Name} has {s.People}.";
        }

        if (!CanAfford(s.Stock, next.UpgradePrice, 100))
        {
            // G6 (2026-11-19): the goods come first, by caravan from our other settlements; a raid on the way calls it off.
            if (s.UpgradeWaiting)
            {
                return $"The goods for the {next.Name.ToLowerInvariant()} are on their way to {s.Name}.";
            }

            if (!BringForUpgrade(civ, s, next.UpgradePrice))
            {
                return $"A {next.Name.ToLowerInvariant()} costs {PriceText(next.UpgradePrice)}; our settlements together do not have it.";
            }

            s.UpgradeWaiting = true;
            News.Add((civ.Id, $"Caravans carry the goods for the {next.Name.ToLowerInvariant()} to {s.Name}; it grows when they arrive."));
            return null;
        }

        s.UpgradeWaiting = false;
        Pay(s.Stock, next.UpgradePrice, 100);
        s.Level++;
        Claim(s);
        Watch(s);
        civ.Dirty = true;
        News.Add((civ.Id, $"{s.Name} is now a {next.Name.ToLowerInvariant()}."));
        return null;
    }

    // Sends what the settlement lacks for the price from our other settlements (nearest first); false when they lack it too.
    private bool BringForUpgrade(Civ civ, Settlement s, long[] price)
    {
        var missing = new long[price.Length];
        for (int g = 0; g < price.Length; g++)
        {
            missing[g] = Math.Max(0, price[g] - s.Stock[g]);
            long elsewhere = 0;
            foreach (int sid in civ.Settlements)
            {
                elsewhere += sid == s.Id ? 0 : Settlements[sid].Stock[g];
            }

            if (elsewhere < missing[g])
            {
                return false;
            }
        }

        var order = new List<int>(civ.Settlements);
        order.Remove(s.Id);
        order.Sort((a, b) => Distance(Settlements[a].Center, s.Center).CompareTo(Distance(Settlements[b].Center, s.Center)));
        for (int g = 0; g < price.Length; g++)
        {
            foreach (int sid in order)
            {
                if (missing[g] <= 0)
                {
                    break;
                }

                long send = Math.Min(missing[g], Settlements[sid].Stock[g]);
                if (send >= SimContent.One && SendCaravan(civ, sid, s.Id, g, send) is null)
                {
                    Units[^1].ForUpgrade = true;
                    missing[g] -= send;
                }
            }
        }

        return true;
    }

    // Once a second: a settlement whose upgrade goods have all arrived grows.
    private void UpgradeArrivals(Civ civ)
    {
        foreach (int sid in civ.Settlements.ToArray())
        {
            Settlement s = Settlements[sid];
            if (!s.UpgradeWaiting || s.Level + 1 >= Content.Levels.Count)
            {
                continue;
            }

            if (CanAfford(s.Stock, Content.Levels[s.Level + 1].UpgradePrice, 100))
            {
                s.UpgradeWaiting = false;
                if (Upgrade(civ, sid) is string why)
                {
                    News.Add((civ.Id, $"{s.Name} did not grow: {why}"));
                }
            }
            else if (!civ.Units.Exists(id => Units[id].ForUpgrade && Units[id].Target == sid && Units[id].State != UnitState.Gone && Units[id].Job == CarrierJob.ToSettlement))
            {
                s.UpgradeWaiting = false; // the caravans arrived short or turned back
                News.Add((civ.Id, $"Not all the goods for {s.Name} arrived: it does not grow this time."));
            }
        }
    }

    private string? Dismiss(Civ civ)
    {
        if (civ.Advisor is null)
        {
            return "We have no advisor.";
        }

        civ.Advisor = null;
        civ.Dirty = true;
        return null;
    }

    private string? WithOwn(Civ civ, int id, Func<Building, string?> action)
    {
        if ((uint)id >= (uint)Buildings.Count || Buildings[id].Civ != civ.Id || Buildings[id].State == BuildingState.Removed)
        {
            return "Not your building.";
        }

        return action(Buildings[id]);
    }
}
