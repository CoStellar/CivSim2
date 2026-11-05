using System;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.State;

namespace CivSim.Core.Commands;

/// <summary>A command that can describe what it did for the chronicle.</summary>
public interface IChronicledCommand
{
    /// <summary>Applies the command like <see cref="ICommand.Apply"/> and returns a chronicle line, or null for none.</summary>
    string? ApplyWithChronicle(GameState state, CivId civ);
}

/// <summary>Checks shared by the settlement commands.</summary>
internal static class SettlementRules
{
    public static CommandResult CheckOwn(GameState state, CivId civ, SettlementId id, out SettlementState? settlement)
    {
        settlement = state.FindSettlement(id);
        CivilizationState? owner = state.FindCivilization(civ);
        if (owner is null || !owner.IsAlive)
        {
            return CommandResult.Fail("Civilization does not exist.");
        }

        if (settlement is null || settlement.Owner != civ)
        {
            return CommandResult.Fail("You do not own that settlement.");
        }

        return settlement.TotalPopulation() == 0 ? CommandResult.Fail("The settlement is empty.") : CommandResult.Ok;
    }

    public static CommandResult CheckTarget(GameContent content, GameState state, SettlementState from, HexCoord target)
    {
        WorldState world = state.Worlds[from.World.Value];
        if (target.Col < 0 || target.Row < 0 || target.Col >= world.Width || target.Row >= world.Height)
        {
            return CommandResult.Fail("The target cell is outside the map.");
        }

        int cell = world.Geometry.Index(target);
        if (content.Biomes[world.Map.Biome[cell]].IsWater)
        {
            return CommandResult.Fail("A camp cannot stand on water.");
        }

        if (world.Geometry.Distance(from.Cell, target) > content.Population.MaxMoveCells)
        {
            return CommandResult.Fail($"The target is more than {content.Population.MaxMoveCells} cells away.");
        }

        short owner = world.Map.Owner[cell];
        if (owner != WorldMap.NoOwner && owner != from.Owner.Value)
        {
            return CommandResult.Fail("Another civilization holds that land.");
        }

        foreach (SettlementState other in state.Settlements)
        {
            if (other.World == from.World && other.Cell == target && other.Id != from.Id && other.TotalPopulation() > 0)
            {
                return CommandResult.Fail("Another settlement already stands there.");
            }
        }

        return CommandResult.Ok;
    }

    public static string Direction(WorldState world, HexCoord from, HexCoord to)
    {
        int dx = to.Col - from.Col;
        int dy = to.Row - from.Row;
        if (Math.Abs(dx) > world.Width / 2)
        {
            dx -= Math.Sign(dx) * world.Width;
        }

        if (Math.Abs(dy) > world.Height / 2)
        {
            dy -= Math.Sign(dy) * world.Height;
        }

        string ns = dy < 0 ? "north" : dy > 0 ? "south" : string.Empty;
        string ew = dx > 0 ? "east" : dx < 0 ? "west" : string.Empty;
        if (Math.Abs(dy) * 2 < Math.Abs(dx))
        {
            ns = string.Empty;
        }

        if (Math.Abs(dx) * 2 < Math.Abs(dy))
        {
            ew = string.Empty;
        }

        return ns + ew;
    }
}

/// <summary>
/// Moves a mobile settlement (a nomad camp) to another cell (GDD 5.4: seasonal or total migration). The people and
/// the warehouse go with it. Settled villages cannot move.
/// </summary>
public sealed record MoveSettlementCommand(GameContent Content, SettlementId Settlement, HexCoord Target) : ICommand, IChronicledCommand
{
    /// <summary>Moves of at least this many cells are written to the chronicle.</summary>
    public const int ChronicleDistance = 8;

    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Movement;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        CommandResult own = SettlementRules.CheckOwn(state, civ, Settlement, out SettlementState? s);
        if (!own.Success)
        {
            return own;
        }

        if (!Content.SettlementLevels[s!.Level].Mobile)
        {
            return CommandResult.Fail("Only camps can move.");
        }

        return s.Cell == Target ? CommandResult.Fail("The camp is already there.") : SettlementRules.CheckTarget(Content, state, s, Target);
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ) => ApplyWithChronicle(state, civ);

    /// <inheritdoc />
    public string? ApplyWithChronicle(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        SettlementState s = state.FindSettlement(Settlement)!;
        HexCoord from = s.Cell;
        s.Cell = Target;
        s.MovedMonth = state.Month;
        WorldState world = state.Worlds[s.World.Value];
        int distance = world.Geometry.Distance(from, Target);
        return distance < ChronicleDistance
            ? null
            : $"{state.FindCivilization(civ)!.Name} moved {s.Name} {distance} cells {SettlementRules.Direction(world, from, Target)}.";
    }
}

/// <summary>
/// Part of a settlement leaves and founds a new camp of the same civilization (GDD 5.4: split; decision of
/// 2026-11-05: in Stage 3 the group stays with its civilization). People leave from every stratum in proportion
/// and take the same share of the warehouse.
/// </summary>
public sealed record SplitSettlementCommand(GameContent Content, SettlementId Settlement, HexCoord Target, int Percent) : ICommand, IChronicledCommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Movement;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        CommandResult own = SettlementRules.CheckOwn(state, civ, Settlement, out SettlementState? s);
        if (!own.Success)
        {
            return own;
        }

        PopulationRules rules = Content.Population;
        if (Percent < 1 || Percent > 90)
        {
            return CommandResult.Fail("Between 1 and 90 percent of the people can leave.");
        }

        if (s!.TotalPopulation() < rules.MinSplitPeople)
        {
            return CommandResult.Fail($"A settlement needs {rules.MinSplitPeople} people to split.");
        }

        WorldState world = state.Worlds[s.World.Value];
        foreach (SettlementState other in state.Settlements)
        {
            if (other.Owner == civ && other.World == s.World && other.TotalPopulation() > 0
                && world.Geometry.Distance(other.Cell, Target) < rules.MinDistanceBetweenOwnCamps)
            {
                return CommandResult.Fail($"New camps must be at least {rules.MinDistanceBetweenOwnCamps} cells from the others.");
            }
        }

        return SettlementRules.CheckTarget(Content, state, s, Target);
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ) => ApplyWithChronicle(state, civ);

    /// <inheritdoc />
    public string? ApplyWithChronicle(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        SettlementState from = state.FindSettlement(Settlement)!;
        CivilizationState owner = state.FindCivilization(civ)!;
        int number = 1;
        foreach (SettlementState other in state.Settlements)
        {
            number += other.Owner == civ ? 1 : 0;
        }

        SettlementState camp = state.FoundSettlement(
            civ, from.World, Target, $"{owner.Name} Camp {number}", Content.NewTribeLevel, from.StockView.Length, from.PopulationView.Length);
        camp.MovedMonth = state.Month;
        Span<int> people = from.Population;
        for (int k = 0; k < people.Length; k++)
        {
            int leaving = (int)((long)people[k] * Percent / 100);
            people[k] -= leaving;
            camp.Population[k] = leaving;
        }

        Span<Amount> stock = from.Stock;
        for (int g = 0; g < stock.Length; g++)
        {
            Amount taken = stock[g].MultiplyPercent(Percent, Rounding.Floor);
            stock[g] -= taken;
            camp.Stock[g] = taken;
        }

        return $"{camp.TotalPopulation()} people left {from.Name} and founded {camp.Name}.";
    }
}
