using System;
using CivSim.Core.Ai.Utility;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.State;

namespace CivSim.Core.Ai;

/// <summary>
/// The strategist (GDD 8.1, 7.6; stage 4b). It gives no orders: every few own steps of its civilization, and at once when it enters
/// a new era, it sets the goal weights the other advisors multiply their scores by, and an expansion target. Crowded
/// camps raise expansion, hunger raises prosperity and security; conquest stays neutral until wars (stage 4e). The target is the best free land the civilization knows within a journey of its largest
/// settlement.
/// </summary>
public sealed class StrategistAdvisor : Advisor
{
    private readonly GovernorAdvisor _governor;
    private HexCoord[] _area = [];

    /// <summary>Creates the strategist; it reads place values from <paramref name="governor"/>, which must run first each turn.</summary>
    public StrategistAdvisor(GameContent content, GovernorAdvisor governor)
        : base(content, AdvisorKind.Strategist)
    {
        _governor = governor ?? throw new ArgumentNullException(nameof(governor));
    }

    /// <inheritdoc />
    public override void IssueCommands(GameState state, CommandQueue queue)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(queue);
        AiRules rules = Content.Ai;
        for (int c = 0; c < state.Civilizations.Count; c++)
        {
            CivilizationState civ = state.Civilizations[c];
            // A planned decision: only in the civilization's own steps (ADR 0018).
            if (!Simulation.WorldClock.Steps(civ) || civ.Controller != PlayerId.Ai || (state.Month < civ.StrategyReviewMonth && civ.Era == civ.StrategyEra))
            {
                continue;
            }

            // The situation: how full the camps are and how well the people ate.
            long people = 0;
            long room = 0;
            long fed = 0;
            SettlementState? heart = null;
            foreach (SettlementState s in state.Settlements)
            {
                if (s.Owner != civ.Id || !s.IsActive || s.World != WorldId.Earth)
                {
                    continue;
                }

                int p = s.TotalPopulation();
                people += p;
                room += Content.SettlementLevels[s.Level].MaxPopulation;
                fed += (long)p * s.FedPerMille;
                if (heart is null || p > heart.TotalPopulation())
                {
                    heart = s;
                }
            }

            if (people == 0 || heart is null)
            {
                continue;
            }

            int crowding = (int)(people * 1000 / Math.Max(1, room));
            int fedPerMille = (int)(fed / people);
            var weights = new int[Character.GoalCount];
            int expansion = ResponseCurve.Linear(rules.CrowdingLowPercent * 10, rules.ExpansionWeightMin, rules.CrowdingHighPercent * 10, rules.ExpansionWeightMax).Evaluate(crowding);
            int hunger = ResponseCurve.Linear(rules.FedBadlyPercent * 10, rules.HungerWeightMax, rules.FedWellPercent * 10, Factor.Neutral).Evaluate(fedPerMille);
            weights[(int)Goal.Expansion] = expansion;
            weights[(int)Goal.Security] = hunger;
            weights[(int)Goal.Prosperity] = hunger;
            weights[(int)Goal.Science] = Factor.Neutral;
            weights[(int)Goal.Influence] = Factor.Neutral;
            // Camps only look for a camp to drive out when they are hungry already, so hunger must not count twice;
            // conquest gets its own weight with wars in stage 4e.
            weights[(int)Goal.Conquest] = Factor.Neutral;

            (int target, long targetValue) = Target(state, civ, heart);

            // Reviews are spread over the civilizations: the first one waits a share of the interval.
            int turns = rules.ReviewEverySteps + (civ.StrategyEra < 0 ? c % rules.ReviewEverySteps : 0);
            long next = state.Month + ((long)turns * Simulation.WorldClock.RhythmMonths(Content, state, civ));

            Evaluation why = Evaluation.Of(expansion);
            why.Note(FactorId.Crowding, crowding / 10);
            why.Note(FactorId.Fed, fedPerMille);
            why.Note(FactorId.Food, targetValue);
            Issue(state, queue, civ.Id, new SetStrategyCommand(Content, weights, target, next, civ.Era), AiAction.SetStrategy, heart.Id.Value, target, why);
        }
    }

    // The best known free land (nobody's) within a journey of the heart, on a sparse lattice; nearer land counts more.
    private (int Cell, long Value) Target(GameState state, CivilizationState civ, SettlementState heart)
    {
        WorldState world = state.Worlds[0];
        HexGlobe geo = world.Geometry;
        int radius = Math.Min(Content.Perception.JourneyCells, geo.MaxUniqueRadius);
        if (_area.Length < HexGlobe.RangeSize(radius))
        {
            _area = new HexCoord[HexGlobe.RangeSize(radius)];
        }

        ResponseCurve nearness = ResponseCurve.Linear(0, Factor.Neutral, 2L * radius, 0);
        int count = geo.Range(heart.Cell, radius, _area);
        int best = CivilizationState.NoTarget;
        long bestValue = 0;
        for (int k = 0; k < count; k++)
        {
            HexCoord cell = _area[k];
            if ((cell.Col % 3) != 0 || (cell.Row % 3) != 0)
            {
                continue;
            }

            int i = geo.Index(cell);
            if (Content.Biomes[world.Map.Biome[i]].IsWater || world.Map.Owner[i] != WorldMap.NoOwner
                || !Perception.MapMemory.Knows(Content, state, civ, i))
            {
                continue;
            }

            long value = _governor.SiteValue(state, world, civ, cell) * nearness.Evaluate(geo.Distance(heart.Cell, cell)) / Factor.Neutral;
            if (value > bestValue)
            {
                best = i;
                bestValue = value;
            }
        }

        return (best, bestValue);
    }
}

/// <summary>
/// Sets a civilization's goal weights and expansion target (GDD 8.1, 7.6; stage 4b). The AI strategist sends it; a
/// player will set directions with it too.
/// </summary>
/// <param name="Content">Game content.</param>
/// <param name="Weights">Weight per goal, per mille, indexed by <see cref="Goal"/>.</param>
/// <param name="Target">Earth cell to expand towards, or <see cref="CivilizationState.NoTarget"/>.</param>
/// <param name="ReviewMonth">Game month of the next review.</param>
/// <param name="Era">Era the strategy was made in.</param>
public sealed record SetStrategyCommand(GameContent Content, int[] Weights, int Target, long ReviewMonth, int Era) : ICommand
{
    /// <summary>Highest goal weight, per mille.</summary>
    public const int MaxWeight = 3000;

    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Economy;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        CivilizationState? c = state.FindCivilization(civ);
        if (c is null || !c.IsAlive)
        {
            return CommandResult.Fail("Civilization does not exist.");
        }

        if (Weights is null || Weights.Length != Character.GoalCount || Array.Exists(Weights, w => w < 0 || w > MaxWeight))
        {
            return CommandResult.Fail($"Give {Character.GoalCount} goal weights of 0 to {MaxWeight}.");
        }

        return Target < CivilizationState.NoTarget || Target >= state.Worlds[0].Geometry.CellCount
            ? CommandResult.Fail("The expansion target is not a cell of Earth.")
            : CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        CivilizationState c = state.FindCivilization(civ)!;
        c.GoalWeights = (int[])Weights.Clone();
        c.ExpansionTarget = Target;
        c.StrategyReviewMonth = ReviewMonth;
        c.StrategyEra = Era;
    }
}
