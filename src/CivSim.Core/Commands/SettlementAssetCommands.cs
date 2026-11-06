using System;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Population;
using CivSim.Core.Random;
using CivSim.Core.State;

namespace CivSim.Core.Commands;

/// <summary>
/// Everyone leaves a settlement and joins another settlement of the same civilization within reach (GDD 5.5:
/// abandonment by choice). They take what they can carry; the settlement is abandoned with the rest.
/// </summary>
public sealed record AbandonSettlementCommand(GameContent Content, SettlementId Settlement, SettlementId Into) : ICommand, IChronicledCommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Movement;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        CommandResult own = SettlementRules.CheckOwn(state, civ, Settlement, out SettlementState? from);
        if (!own.Success)
        {
            return own;
        }

        CommandResult reach = SettlementRules.CheckReach(Content, state, from!, Into, out SettlementState? into);
        if (!reach.Success)
        {
            return reach;
        }

        return into!.Owner != civ || !into.IsActive ? CommandResult.Fail("People can only join a settlement of their own.") : CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ) => ApplyWithChronicle(state, civ);

    /// <inheritdoc />
    public string? ApplyWithChronicle(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        SettlementState from = state.FindSettlement(Settlement)!;
        SettlementState into = state.FindSettlement(Into)!;
        int people = from.TotalPopulation();
        SettlementAssets.Carry(from, into, SettlementAssets.CarryCapacity(Content, state, civ, people));
        for (int k = 0; k < from.Population.Length; k++)
        {
            into.Population[k] += from.Population[k];
        }

        SettlementAssets.Abandon(Content, state, from);
        return Content.SettlementLevels[from.Level].Mobile ? null : $"The people of {from.Name} left for {into.Name}.";
    }
}

/// <summary>
/// A group from one settlement moves into an abandoned settlement or ruins (GDD 5.5: takeover). The place must be on
/// land the civilization holds or nobody holds; a claim of its former owner does not stop the takeover but stays
/// (a border dispute for Stage 4). <see cref="Percent"/> of the people go, with what they can carry; when everyone
/// goes, the old settlement is abandoned.
/// </summary>
public sealed record OccupySettlementCommand(GameContent Content, SettlementId From, SettlementId Target, int Percent) : ICommand, IChronicledCommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Movement;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        CommandResult own = SettlementRules.CheckOwn(state, civ, From, out SettlementState? from);
        if (!own.Success)
        {
            return own;
        }

        if (Percent < 1 || Percent > 100)
        {
            return CommandResult.Fail("Between 1 and 100 percent of the people can go.");
        }

        if (Percent < 100 && from!.TotalPopulation() < Content.Population.MinSplitPeople)
        {
            return CommandResult.Fail($"A settlement needs {Content.Population.MinSplitPeople} people to send a group.");
        }

        CommandResult reach = SettlementRules.CheckReach(Content, state, from!, Target, out SettlementState? target);
        return reach.Success ? SettlementRules.CheckEmptyOnOpenLand(state, civ, target!) : reach;
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ) => ApplyWithChronicle(state, civ);

    /// <inheritdoc />
    public string? ApplyWithChronicle(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        SettlementState from = state.FindSettlement(From)!;
        SettlementState target = state.FindSettlement(Target)!;
        Span<int> people = from.Population;
        for (int k = 0; k < people.Length; k++)
        {
            int going = Percent == 100 ? people[k] : (int)((long)people[k] * Percent / 100);
            people[k] -= going;
            target.Population[k] = going;
        }

        SettlementAssets.Carry(from, target, SettlementAssets.CarryCapacity(Content, state, civ, target.TotalPopulation()), Percent);
        if (from.TotalPopulation() == 0)
        {
            SettlementAssets.Abandon(Content, state, from);
        }

        CivId former = target.Owner;
        target.Owner = civ;
        target.Status = SettlementStatus.Active;
        target.MovedMonth = state.Month;
        if (target.Name.StartsWith(SettlementAssets.OldSitePrefix, StringComparison.Ordinal))
        {
            target.Name = target.Name[SettlementAssets.OldSitePrefix.Length..];
        }

        if (target.ClaimCiv == civ.Value)
        {
            target.ClaimCiv = SettlementState.NoClaim;
        }

        if (target.HasClaim(state.Month))
        {
            return $"{state.FindCivilization(civ)!.Name} took over {target.Name}, claimed by {state.FindCivilization(new CivId(target.ClaimCiv))!.Name}.";
        }

        return former != civ && !Content.SettlementLevels[target.Level].Mobile
            ? $"{state.FindCivilization(civ)!.Name} resettled {target.Name}."
            : null;
    }
}

/// <summary>
/// People from a settlement take what they can carry from an abandoned settlement or ruins within reach (GDD 5.5:
/// plundering what was left behind).
/// </summary>
public sealed record PlunderSettlementCommand(GameContent Content, SettlementId From, SettlementId Target) : ICommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Economy;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        CommandResult own = SettlementRules.CheckOwn(state, civ, From, out SettlementState? from);
        if (!own.Success)
        {
            return own;
        }

        CommandResult reach = SettlementRules.CheckReach(Content, state, from!, Target, out SettlementState? target);
        return reach.Success ? SettlementRules.CheckEmptyOnOpenLand(state, civ, target!) : reach;
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        SettlementState from = state.FindSettlement(From)!;
        SettlementState target = state.FindSettlement(Target)!;
        SettlementAssets.Carry(target, from, SettlementAssets.CarryCapacity(Content, state, civ, from.TotalPopulation()));
    }
}

/// <summary>
/// Migration with conquest (GDD 5.4, simplified until the wars of Stage 4): a camp attacks the camp of another
/// civilization within reach. Strength is the attackers' warriors against the defenders' warriors plus part of everyone
/// else; the chance to win is the attackers' share of the total strength (combat random stream). The winners take the
/// cell and what the losers cannot carry; the expelled people flee to the nearest free land, or to another settlement
/// of their own, losing some on the way. Incorporating the defeated (loyalty) and tribute are later modules.
/// </summary>
public sealed record ConquerSettlementCommand(GameContent Content, SettlementId From, SettlementId Target) : ICommand, IChronicledCommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Movement;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        CommandResult own = SettlementRules.CheckOwn(state, civ, From, out SettlementState? from);
        if (!own.Success)
        {
            return own;
        }

        if (!Content.SettlementLevels[from!.Level].Mobile)
        {
            return CommandResult.Fail("Only camps can move to conquer.");
        }

        if (SettlementAssets.Warriors(Content, from) == 0)
        {
            return CommandResult.Fail("The camp has no warriors.");
        }

        CommandResult reach = SettlementRules.CheckReach(Content, state, from, Target, out SettlementState? target);
        if (!reach.Success)
        {
            return reach;
        }

        return target!.Owner == civ || !target.IsActive ? CommandResult.Fail("Only settlements of other civilizations can be attacked.") : CommandResult.Ok;
    }

    /// <summary>Strength of the defence of <paramref name="s"/>: its warriors plus a share of everyone else.</summary>
    public static long DefenceStrength(GameContent content, SettlementState s)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(s);
        int warriors = SettlementAssets.Warriors(content, s);
        return warriors + ((long)(s.TotalPopulation() - warriors) * content.Population.DefenderNonWarriorPercent / 100);
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ) => ApplyWithChronicle(state, civ);

    /// <inheritdoc />
    public string? ApplyWithChronicle(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        PopulationRules rules = Content.Population;
        SettlementState from = state.FindSettlement(From)!;
        SettlementState target = state.FindSettlement(Target)!;
        string attackers = state.FindCivilization(civ)!.Name;
        string defenders = state.FindCivilization(target.Owner)!.Name;
        long attack = SettlementAssets.Warriors(Content, from);
        long defence = Math.Max(1, DefenceStrength(Content, target));
        // The defenders remember the attack, won or lost (GDD 8.5).
        string met = Diplomacy.Contacts.RecordAttack(Content, state, target.Owner, civ) ? " It was their first meeting." : string.Empty;
        bool won = state.Random[RandomStreamId.Combat].Chance((int)Math.Min(int.MaxValue / 2, attack), (int)Math.Min(int.MaxValue / 2, attack + defence));
        if (!won)
        {
            SettlementAssets.Lose(Content, from, rules.LoserWarriorLossPercent, warriorsOnly: true);
            SettlementAssets.Lose(Content, target, rules.WinnerWarriorLossPercent, warriorsOnly: true);
            return $"{defenders} beat back {attackers} at {target.Name}.{met}";
        }

        SettlementAssets.Lose(Content, from, rules.WinnerWarriorLossPercent, warriorsOnly: true);
        SettlementAssets.Lose(Content, target, rules.LoserWarriorLossPercent, warriorsOnly: true);
        SettlementAssets.Lose(Content, target, rules.FleeingLossPercent, warriorsOnly: false);
        HexCoord cell = target.Cell;
        (string flight, SettlementState? left) = Flee(state, target);

        // The winners move in and take what the losers left (the warehouse limit cuts off the rest later).
        SettlementAssets.Relocate(Content, state, from, cell);
        if (left is not null)
        {
            SettlementAssets.Carry(left, from, Numerics.Amount.MaxValue);
        }

        if (target.TotalPopulation() == 0)
        {
            SettlementAssets.Abandon(Content, state, target);
        }

        return $"{attackers} drove {defenders} out of {target.Name}; {flight}.{met}";
    }

    // The defeated take what they can carry to the nearest free land cell, or join their nearest settlement.
    // Returns the chronicle words and the settlement holding what stayed behind on the cell (null when nothing did).
    private (string Text, SettlementState? Left) Flee(GameState state, SettlementState s)
    {
        WorldState world = state.Worlds[s.World.Value];
        HexGlobe geo = world.Geometry;
        int radius = Math.Min(Content.Population.MaxMoveCells, geo.MaxUniqueRadius);
        Span<HexCoord> cells = new HexCoord[HexGlobe.RangeSize(radius)];
        int count = geo.Range(s.Cell, radius, cells);
        for (int k = 1; k < count; k++)
        {
            HexCoord c = cells[k];
            int i = geo.Index(c);
            short owner = world.Map.Owner[i];
            if (!Content.Biomes[world.Map.Biome[i]].IsWater && (owner == WorldMap.NoOwner || owner == s.Owner.Value) && !Occupied(state, s.World, c))
            {
                SettlementState? site = SettlementAssets.Relocate(Content, state, s, c);
                return ("the survivors fled to new land", site);
            }
        }

        SettlementState? refuge = null;
        int best = int.MaxValue;
        foreach (SettlementState other in state.Settlements)
        {
            if (other.Owner == s.Owner && other.Id != s.Id && other.IsActive && other.World == s.World)
            {
                int d = geo.Distance(s.Cell, other.Cell);
                if (d < best)
                {
                    best = d;
                    refuge = other;
                }
            }
        }

        if (refuge is null)
        {
            s.Population.Clear();
            return ("the survivors scattered", s);
        }

        SettlementAssets.Carry(s, refuge, SettlementAssets.CarryCapacity(Content, state, s.Owner, s.TotalPopulation()));
        for (int k = 0; k < s.Population.Length; k++)
        {
            refuge.Population[k] += s.Population[k];
        }

        s.Population.Clear();
        return ($"the survivors went to {refuge.Name}", s);
    }

    private static bool Occupied(GameState state, WorldId world, HexCoord cell)
    {
        foreach (SettlementState other in state.Settlements)
        {
            if (other.IsActive && other.World == world && other.Cell == cell)
            {
                return true;
            }
        }

        return false;
    }
}
