using System;
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
        return command switch
        {
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
                BuildingAt[b.Cell] = -1;
                civ.Buildings.Remove(b.Id);
                Map.MarkCellDirty(b.Cell);
                civ.Dirty = true;
                return null;
            }),
            UpgradeSettlementCommand c => Upgrade(civ, c.Settlement),
            TrainCommand c => Train(civ, c.Settlement, c.Kind),
            MoveCommand c => Move(civ, c.Unit, c.Cell, c.Hidden),
            FoundCommand c => Found(civ, c.Unit),
            ResearchCommand c => Research(civ, c.Tech),
            HireCommand c => Hire(civ, c.Advisor),
            DismissAdvisorCommand => Dismiss(civ),
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

        if (!civ.KnownTech[type.Tech])
        {
            return $"{type.Name} needs {Content.Techs[type.Tech].Name}.";
        }

        if (OwnerAt[cell] != civ.Id)
        {
            return "Build only on your own land.";
        }

        if (BuildingAt[cell] >= 0 || Settlements[SettlementAt[cell]].Center == cell)
        {
            return "This hex is taken.";
        }

        if (!TerrainAllows(cell, type))
        {
            return $"{type.Name} needs {Needs(type)}.";
        }

        if (checkPrice && !CanAfford(civ, type.Price, 100))
        {
            return $"Not enough goods: {type.Name} costs {PriceText(type.Price)}.";
        }

        return null;
    }

    /// <summary>True when the terrain of the hex suits the building.</summary>
    public bool TerrainAllows(int cell, BuildingType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.OrDeposit != 0 && Map.Deposit[cell] == type.OrDeposit && Map.DepositDepth[cell] == 0)
        {
            return true;
        }

        bool anyBiome = Array.IndexOf(type.Biomes, true) >= 0;
        bool anyRelief = Array.IndexOf(type.Reliefs, true) >= 0;
        if (anyBiome && !type.Biomes[Map.Biome[cell]])
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

        if (type.MinFertility > 0 && Map.Fertility[cell] < type.MinFertility)
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

    private static bool CanAfford(Civ civ, long[] price, int percent)
    {
        for (int g = 0; g < price.Length; g++)
        {
            if (civ.Stock[g] < price[g] * percent / 100)
            {
                return false;
            }
        }

        return true;
    }

    private static void Pay(Civ civ, long[] price, int percent)
    {
        for (int g = 0; g < price.Length; g++)
        {
            civ.Stock[g] -= price[g] * percent / 100;
        }
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

        Pay(civ, t.Price, 100);
        int yield = 100;
        if (t.BonusDeposit != 0 && Map.Deposit[cell] == t.BonusDeposit && Map.DepositDepth[cell] == 0)
        {
            yield += t.BonusPercent;
        }

        var b = new Building
        {
            Id = Buildings.Count,
            Type = typeId,
            Cell = cell,
            Civ = civ.Id,
            Settlement = SettlementAt[cell],
            State = BuildingState.Construction,
            FinishTick = Ticks + Math.Max(1, t.BuildTicks),
            YieldPercent = yield,
        };
        Buildings.Add(b);
        civ.Buildings.Add(b.Id);
        BuildingAt[cell] = b.Id;
        Map.MarkCellDirty(cell);
        Schedule(b);
        return null;
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

            b.RemainingTicks = (int)(b.FinishTick - Ticks);
            b.State = BuildingState.ConstructionPaused;
            return null;
        }

        if (b.State != BuildingState.ConstructionPaused)
        {
            return "This construction is not paused.";
        }

        if (!CanAfford(civ, t.Price, Content.ResumeCostPercent))
        {
            return $"Resuming costs {Content.ResumeCostPercent}% of the price again.";
        }

        Pay(civ, t.Price, Content.ResumeCostPercent);
        b.State = BuildingState.Construction;
        b.FinishTick = Ticks + Math.Max(1, b.RemainingTicks);
        Schedule(b);
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
        if (next.Tech >= 0 && !civ.KnownTech[next.Tech])
        {
            return $"A {next.Name.ToLowerInvariant()} needs {Content.Techs[next.Tech].Name}.";
        }

        if (!CanAfford(civ, next.UpgradePrice, 100))
        {
            return $"A {next.Name.ToLowerInvariant()} costs {PriceText(next.UpgradePrice)}.";
        }

        Pay(civ, next.UpgradePrice, 100);
        s.Level++;
        Claim(s);
        Reveal(civ, s.Center, next.Radius + 4);
        civ.Dirty = true;
        News.Add((civ.Id, $"{s.Name} is now a {next.Name.ToLowerInvariant()}."));
        return null;
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
