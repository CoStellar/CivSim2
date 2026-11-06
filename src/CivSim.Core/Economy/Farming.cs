using System;
using System.Collections.Generic;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Economy;

/// <summary>
/// Farming (GDD 4.4 and 5.3; stage 3f). The producers of a settled village (a level that cannot move) farm the
/// fertile land cells of its zone, best land first, at most <see cref="PopulationRules.FarmersPerCell"/> on a cell.
/// A farmer grows grain by the cell's fertility and growing season. Farmed land wears out and land left alone recovers
/// up to its natural fertility (decision of 2026-11-05: farmland wears out). Producers who do not farm gather.
/// Runs in the economy phase before gathering.
/// </summary>
public sealed class FarmingSystem(GameContent content) : ITurnSystem
{
    private readonly GameContent _content = content ?? throw new ArgumentNullException(nameof(content));
    private readonly List<long> _keys = [];
    private HexCoord[] _area = [];
    private int[] _worked = [];
    private int[] _wornMonths = [];

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.Economy;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        PopulationRules rules = _content.Population;
        foreach (WorldState world in state.Worlds)
        {
            if (_worked.Length != world.Geometry.CellCount)
            {
                _worked = new int[world.Geometry.CellCount];
                _wornMonths = new int[world.Geometry.CellCount];
            }

            Array.Clear(_worked);
            foreach (SettlementState s in state.Settlements)
            {
                // A village whose civilization waits for its own step still holds its fields (they neither wear nor rest
                // now); it farms them, and they wear, over its whole step when it comes (ADR 0018).
                int months = WorldClock.MonthsOf(state, s.Owner);
                if (months > 0)
                {
                    s.Farmers = 0;
                    s.FoodGathered = 0;
                }

                if (s.World == world.Id && s.IsActive && !_content.SettlementLevels[s.Level].Mobile && rules.GrainGood >= 0)
                {
                    Farm(state, world, s, months);
                }
            }

            Wear(world.Map, state.StepMonths);
        }
    }

    /// <summary>Grain a farmer grows per year on a cell (fertility and growing season), hundredths.</summary>
    public long GrainPerFarmer(WorldMap map, int cell)
    {
        ArgumentNullException.ThrowIfNull(map);
        PopulationRules rules = _content.Population;
        if (_content.Biomes[map.Biome[cell]].IsWater || map.Fertility[cell] < rules.MinFarmFertility)
        {
            return 0;
        }

        int season = WorldGen.ClimateModel.WildFoodPercent(map.GrowingSeason[cell], map.YearHundredths, rules.SurvivableDeadSeasonHundredths);
        return rules.GrainPerFarmerPerYear.Hundredths * (50 + map.Fertility[cell]) / 100 * season / 100;
    }

    private void Farm(GameState state, WorldState world, SettlementState s, int months)
    {
        PopulationRules rules = _content.Population;
        WorldMap map = world.Map;
        int producers = 0;
        ReadOnlySpan<int> people = s.PopulationView;
        for (int k = 0; k < people.Length && k < _content.Strata.Count; k++)
        {
            producers += _content.Strata[k].Gathers ? people[k] : 0;
        }

        // Only farmers farm (stage 4F): the skill's people and apprentices at their share; the others gather.
        if (_content.SkillRules.FarmingSkill >= 0)
        {
            producers = Math.Min(producers, Population.Skills.Workers(_content, s, _content.SkillRules.FarmingSkill));
        }

        if (producers == 0)
        {
            return;
        }

        // The zone's farmable cells, best grain per farmer first (ties by cell index).
        int radius = Math.Min(rules.InfluenceMaxRange, world.Geometry.MaxUniqueRadius);
        if (_area.Length < HexGlobe.RangeSize(radius))
        {
            _area = new HexCoord[HexGlobe.RangeSize(radius)];
        }

        int count = world.Geometry.Range(s.Cell, radius, _area);
        int claim = s.Id.Value + 1;
        _keys.Clear();
        for (int k = 0; k < count; k++)
        {
            int i = world.Geometry.Index(_area[k]);
            long perFarmer = map.Claim[i] == claim ? GrainPerFarmer(map, i) : 0;
            if (perFarmer > 0 && state.Effects.Count > 0)
            {
                // Drought, wildfire and flood lower what a cell gives (cell extraction effects, stage 3h).
                perFarmer = perFarmer * Math.Max(0, 100 + state.Effects.Total(_content.Effects, EffectStat.ExtractionPercent, EffectTarget.Cell, s.World, i)) / 100;
            }

            if (perFarmer > 0)
            {
                _keys.Add((-perFarmer << 24) | (uint)i);
            }
        }

        _keys.Sort();
        int output = 100 + (state.Effects.Count == 0
            ? 0
            : state.Effects.Total(_content.Effects, EffectStat.OutputPercent, EffectTarget.Settlement, s.World, s.Id.Value)
                + state.Effects.Total(_content.Effects, EffectStat.OutputPercent, EffectTarget.Civilization, WorldId.Earth, s.Owner.Value));
        long grainHundredths = 0;
        int left = producers;
        foreach (long key in _keys)
        {
            if (left == 0)
            {
                break;
            }

            int cell = (int)(key & 0xFF_FFFF);
            long perFarmer = -(key >> 24);
            int farmers = Math.Min(left, rules.FarmersPerCell - _worked[cell]);
            if (farmers <= 0)
            {
                continue;
            }

            _worked[cell] += farmers;
            _wornMonths[cell] = months;
            left -= farmers;
            grainHundredths += perFarmer * farmers * months / 12;
        }

        if (months == 0)
        {
            return; // only the fields were marked
        }

        s.Farmers = producers - left;
        long grain = grainHundredths * Math.Max(0, output) / 100;
        if (grain > 0)
        {
            s.Add(rules.GrainGood, Amount.FromHundredths(grain));
            s.FoodGathered += grain;
        }
    }

    // Farmed land wears out in proportion to how fully it is worked; other land recovers to its natural fertility.
    // Fields wear over their farmers' own step; resting land recovers over the world step.
    private void Wear(WorldMap map, int months)
    {
        PopulationRules rules = _content.Population;
        int cap = rules.FarmersPerCell;
        for (int i = 0; i < _worked.Length; i++)
        {
            int natural = map.NaturalFertility[i];
            int fertility = map.Fertility[i];
            long change = _worked[i] > 0
                ? -(long)rules.ExhaustionHundredthsPerYear * _wornMonths[i] / 12 * _worked[i] / cap
                : fertility < natural ? (long)rules.RecoveryHundredthsPerYear * months / 12 : 0;
            if (change == 0)
            {
                continue;
            }

            // Hundredths of a point build up per cell (saved on the map) until they make a whole point.
            long total = map.FertilityRemainder[i] + change;
            int whole = (int)(total / 100);
            sbyte rest = (sbyte)(total % 100);
            if (rest != map.FertilityRemainder[i])
            {
                map.FertilityRemainder.Set(i, rest);
            }
            if (whole != 0)
            {
                int next = Math.Clamp(fertility + whole, 0, _worked[i] > 0 ? 100 : natural);
                if (next != fertility)
                {
                    map.Fertility.Set(i, (byte)next);
                }
            }
        }
    }
}

/// <summary>
/// A camp settles down as a village (GDD 5.3: a village needs agriculture and settling down). The village no longer
/// moves, its producers farm, and it can grow to the village level's size. Moving on later means leaving it
/// (GDD 5.4: settling does not bind a civilization to a place for ever).
/// </summary>
public sealed record SettleCommand(GameContent Content, SettlementId Settlement) : ICommand, IChronicledCommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Economy;

    /// <summary>The level a camp settles down to: the first level after it that cannot move.</summary>
    public static int VillageLevel(GameContent content, int level)
    {
        ArgumentNullException.ThrowIfNull(content);
        for (int l = level + 1; l < content.SettlementLevels.Count; l++)
        {
            if (!content.SettlementLevels[l].Mobile)
            {
                return l;
            }
        }

        return -1;
    }

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        CommandResult own = SettlementRules.CheckOwn(state, civ, Settlement, out SettlementState? s);
        if (!own.Success)
        {
            return own;
        }

        if (!s!.IsActive || !Content.SettlementLevels[s.Level].Mobile)
        {
            return CommandResult.Fail("Only a lived-in camp can settle down.");
        }

        int village = VillageLevel(Content, s.Level);
        if (village < 0)
        {
            return CommandResult.Fail("There is no settled level.");
        }

        int tech = Content.SettlementLevels[village].RequiresTechnology;
        if (tech >= 0 && !state.FindCivilization(civ)!.Knows(tech))
        {
            return CommandResult.Fail($"Settling down needs {Content.Technologies[tech].Name}.");
        }

        return s.TotalPopulation() < Content.Assets.SettleMinPeople
            ? CommandResult.Fail($"A camp needs {Content.Assets.SettleMinPeople} people to settle down.")
            : CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ) => ApplyWithChronicle(state, civ);

    /// <inheritdoc />
    public string? ApplyWithChronicle(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        SettlementState s = state.FindSettlement(Settlement)!;
        s.Level = VillageLevel(Content, s.Level);
        return $"{state.FindCivilization(civ)!.Name} settled down: {s.Name} became a {Content.SettlementLevels[s.Level].Name.ToLowerInvariant()}.";
    }
}
