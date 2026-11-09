using System;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Perception;
using CivSim.Core.State;

namespace CivSim.Core.Units;

/// <summary>Shared checks of the unit commands (stage 6 PR 3a): the same commands for players and the AI (ADR 0024).</summary>
internal static class UnitCommandRules
{
    public static CommandResult CheckOwn(GameState state, CivId civ, long unitId, out Unit unit)
    {
        ArgumentNullException.ThrowIfNull(state);
        Unit? found = state.Units.Find(unitId);
        unit = found ?? default;
        if (found is null)
        {
            return CommandResult.Fail("There is no such unit.");
        }

        return found.Value.Owner != civ ? CommandResult.Fail("That unit is not ours.") : CommandResult.Ok;
    }

    // Units of a campaign (an army, gathering warriors, carriers on the way) follow their campaign (stage 6 PR 3b).
    public static CommandResult CheckFree(Unit u) =>
        u.Episode >= 0 ? CommandResult.Fail("That unit belongs to a campaign: send its army to attack or call it back.") : CommandResult.Ok;

    // The campaign whose army <paramref name="u"/> is, or null.
    public static Episodes.Episode? CampaignOf(GameState state, Unit u)
    {
        if (u.Episode < 0 || u.Role != UnitRole.Army)
        {
            return null;
        }

        foreach (Episodes.Episode e in state.Episodes.All)
        {
            if (e.Id == u.Episode && !e.Ended && e.Army == u.Id)
            {
                return e;
            }
        }

        return null;
    }
}

/// <summary>
/// Sends a unit to a known cell it can walk to (stage 6 PR 3a): it walks there over the cheapest known way and stands.
/// Settlers sent this way no longer found a camp by themselves (<see cref="FoundCampCommand"/> founds one where they stand);
/// scouts sent this way end their trip and come back over the cheapest way when told to return.
/// </summary>
public sealed record MoveUnitCommand(GameContent Content, long Unit, HexCoord Target) : ICommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Movement;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ)
    {
        CommandResult own = UnitCommandRules.CheckOwn(state, civ, Unit, out Unit u);
        if (!own.Success)
        {
            return own;
        }

        if (!UnitCommandRules.CheckFree(u).Success)
        {
            return UnitCommandRules.CheckFree(u);
        }

        WorldState world = state.Worlds[u.World.Value];
        if (!world.Geometry.Contains(Target))
        {
            return CommandResult.Fail("That cell is off the map.");
        }

        int target = world.Geometry.Index(Target);
        CivilizationState c = state.FindCivilization(civ)!;
        if (!MapMemory.Walkable(Content, world.Map, target) || !MapMemory.Knows(Content, state, c, target))
        {
            return CommandResult.Fail("Units can only be sent to known land.");
        }

        return target != u.Cell && new RouteFinder().RouteTicks(Content, state, c, world, u.Cell, target, Content.Units.Of(u.Kind).TicksPerCell, Content.Units.Of(u.Kind)) < 0
            ? CommandResult.Fail("The unit knows no way there.")
            : CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        Unit u = state.Units.Find(Unit)!.Value;
        u.Target = state.Worlds[u.World.Value].Geometry.Index(Target);
        u.Order = UnitOrder.Move;
        u.Trail.Clear(); // a scout leaves its trail: it comes back over the cheapest way
        u.ForgetPath();
    }
}

/// <summary>Settlers found a camp on the cell they stand on (stage 6 PR 3a), if a camp may be founded there.</summary>
public sealed record FoundCampCommand(GameContent Content, long Unit) : ICommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Movement;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ)
    {
        CommandResult own = UnitCommandRules.CheckOwn(state, civ, Unit, out Unit u);
        if (!own.Success)
        {
            return own;
        }

        if (u.Role != UnitRole.Settlers)
        {
            return CommandResult.Fail("Only settlers found camps.");
        }

        return UnitSystem.CanFound(Content, state, civ, state.Worlds[u.World.Value], u.Cell)
            ? CommandResult.Ok
            : CommandResult.Fail($"A camp needs free dry land at least {Content.Population.MinDistanceBetweenOwnCamps} cells from our others.");
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        Unit u = state.Units.Find(Unit)!.Value;
        u.Target = u.Cell;
        u.Order = UnitOrder.Found; // founded in the unit's next step (UnitSystem: it stands on its goal)
        u.ForgetPath();
    }
}

/// <summary>
/// A unit goes back to its home settlement (the nearest own one when that is gone) and joins it (stage 6 PR 3a). The army of
/// a campaign that is still gathering or marching is called home (as <see cref="Episodes.RecallCampaignCommand"/>); a fleeing
/// army cannot be turned (stage 6 PR 3b).
/// </summary>
public sealed record ReturnUnitCommand(GameContent Content, long Unit) : ICommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Movement;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ)
    {
        CommandResult own = UnitCommandRules.CheckOwn(state, civ, Unit, out Unit u);
        if (!own.Success)
        {
            return own;
        }

        if (UnitCommandRules.CampaignOf(state, u) is { } e)
        {
            return e.Phase is Episodes.CampaignPhase.Muster or Episodes.CampaignPhase.March
                ? CommandResult.Ok
                : CommandResult.Fail("That army is already on its way home.");
        }

        return UnitCommandRules.CheckFree(u);
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        Unit u = state.Units.Find(Unit)!.Value;
        if (UnitCommandRules.CampaignOf(state, u) is { } e)
        {
            e.Phase = Episodes.CampaignPhase.Return;
            e.Foraging = false;
            return;
        }

        u.Order = UnitOrder.Return;
        u.ForgetPath();
    }
}

/// <summary>
/// Sends the army of a campaign against another known settlement of the same enemy (stage 6 PR 3b): it walks there and
/// attacks a month of action after it is within its range. The same command for players and the AI (ADR 0024).
/// </summary>
public sealed record AttackCommand(GameContent Content, long Unit, SettlementId Target) : ICommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Military;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ)
    {
        CommandResult own = UnitCommandRules.CheckOwn(state, civ, Unit, out Unit u);
        if (!own.Success)
        {
            return own;
        }

        if (UnitCommandRules.CampaignOf(state, u) is not { } e || e.Phase != Episodes.CampaignPhase.March)
        {
            return CommandResult.Fail("Only an army on the march can be sent to attack.");
        }

        SettlementState? t = state.FindSettlement(Target);
        if (t is not { IsActive: true } || t.Owner != e.Other || t.World != u.World)
        {
            return CommandResult.Fail("The army can only attack a lived-in settlement of the enemy it marches against.");
        }

        WorldState world = state.Worlds[u.World.Value];
        CivilizationState c = state.FindCivilization(civ)!;
        return MapMemory.Knows(Content, state, c, world.Geometry.Index(t.Cell))
            ? CommandResult.Ok
            : CommandResult.Fail("We do not know where that settlement is.");
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        Unit u = state.Units.Find(Unit)!.Value;
        Episodes.Episode e = UnitCommandRules.CampaignOf(state, u)!;
        e.Target = Target.Value;
        e.GoalTarget = Target.Value;
        e.Foraging = false;
        e.InRangeSince = -1;
    }
}
