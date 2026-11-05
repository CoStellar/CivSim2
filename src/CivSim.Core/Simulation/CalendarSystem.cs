using System;
using CivSim.Core.Content;

namespace CivSim.Core.Simulation;

/// <summary>
/// Moves game time forward by the length of the finished turn (GDD 6.2) in the record phase, before the snapshot
/// is published.
/// </summary>
public sealed class CalendarSystem(GameContent content) : ITurnSystem
{
    private readonly GameContent _content = content ?? throw new ArgumentNullException(nameof(content));

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.Record;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.State.Month += context.State.MonthsPerTurn(_content);
    }
}

/// <summary>Removes effects whose time is up, at the start of the turn (GDD 11.1, phase 1).</summary>
public sealed class EffectExpirySystem : ITurnSystem
{
    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.World;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.State.Effects.RemoveExpired(context.State.Month);
    }
}

/// <summary>Registers the core simulation systems in their phases, in a fixed order, and the tribe AI.</summary>
public static class CoreSystems
{
    /// <summary>Adds every core system to <paramref name="runner"/>.</summary>
    public static void Register(TurnRunner runner, GameContent content)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(content);
        var years = new Economy.YieldYears(content.Population.YieldSwingPercent);
        runner.AddSystem(new WorldGen.ClimateSystem(content));
        runner.AddSystem(new WorldGen.SeasonSystem(content));
        runner.AddSystem(new EffectExpirySystem());
        runner.AddSystem(new Population.DecaySystem(content));
        runner.AddSystem(new Economy.WildFoodSystem(content, years));
        runner.AddSystem(new Population.InfluenceSystem(content, TurnPhase.Movement));
        runner.AddSystem(new Economy.GatheringSystem(content, years));
        runner.AddSystem(new Economy.TradeSystem(content));
        runner.AddSystem(new Economy.ProductionSystem(content));
        runner.AddSystem(new Economy.ConsumptionSystem(content));
        runner.AddSystem(new Economy.StorageSystem(content));
        runner.AddSystem(new Population.PopulationSystem(content));
        runner.AddSystem(new Population.AbandonmentSystem(content));
        runner.AddSystem(new Population.InfluenceSystem(content));
        runner.AddCommandSource(new Ai.TribeAi(content));
        runner.AddCommandSource(new Ai.EconomyAi(content));
        runner.AddSystem(new CalendarSystem(content));
    }
}
