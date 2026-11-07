using System;
using System.Collections.Generic;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Perception;
using CivSim.Core.Population;
using CivSim.Core.Random;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Episodes;

/// <summary>
/// The rules of a war campaign (GDD 9.3; stage 4e PR 2, ADR 0022): who may march where, taking and giving back warriors,
/// and the fight at the target. Simplified until the next PRs bring arms, armies of several settlements, carriers and
/// battles in rounds.
/// </summary>
public static class Campaigns
{
    /// <summary>Why <paramref name="civ"/> may not send <paramref name="warriors"/> from <paramref name="home"/> against <paramref name="target"/>, or null.</summary>
    public static string? CannotLaunch(GameContent content, GameState state, CivId civ, int home, int target, int warriors)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        SettlementState? h = state.FindSettlement(new SettlementId(home));
        SettlementState? t = state.FindSettlement(new SettlementId(target));
        CivilizationState? c = state.FindCivilization(civ);
        if (c is not { IsAlive: true } || h is not { IsActive: true } || h.Owner != civ)
        {
            return "The campaign needs a lived-in settlement of ours.";
        }

        if (t is not { IsActive: true } || t.Owner == civ || t.World != h.World || h.World.Value != 0)
        {
            return "The target must be a lived-in settlement of another civilization.";
        }

        if (!state.Treaties.AtWar(civ, t.Owner))
        {
            return "We are not at war with them.";
        }

        WorldState world = state.Worlds[0];
        if (!MapMemory.Knows(content, state, c, world.Geometry.Index(t.Cell)))
        {
            return "We do not know where that settlement is.";
        }

        if (state.Episodes.OutFrom(civ, home))
        {
            return "Warriors of that settlement are already out.";
        }

        int most = SettlementAssets.Warriors(content, h) * content.War.SendPercent / 100;
        if (warriors < content.War.MinWarriors || warriors > most)
        {
            return "Too few warriors, or not enough stay home.";
        }

        return MapMemory.Path(content, state, c, world, world.Geometry.Index(h.Cell), world.Geometry.Index(t.Cell)) is null
            ? "We know no way there."
            : null;
    }

    /// <summary>Takes up to <paramref name="count"/> warriors out of the fighting strata of <paramref name="s"/>; returns how many.</summary>
    public static int TakeWarriors(GameContent content, SettlementState s, int count)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(s);
        int taken = 0;
        Span<int> people = s.Population;
        for (int k = 0; k < people.Length && k < content.Strata.Count && taken < count; k++)
        {
            if (content.Strata[k].Fights)
            {
                int n = Math.Min(people[k], count - taken);
                people[k] -= n;
                taken += n;
            }
        }

        return taken;
    }

    /// <summary>Gives <paramref name="count"/> warriors back to the first fighting stratum of <paramref name="s"/>.</summary>
    public static void GiveWarriors(GameContent content, SettlementState s, int count)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(s);
        Span<int> people = s.Population;
        for (int k = 0; k < people.Length && k < content.Strata.Count; k++)
        {
            if (content.Strata[k].Fights)
            {
                people[k] += count;
                return;
            }
        }
    }

    /// <summary>
    /// The chance, percent, that <paramref name="warriors"/> win against the defence of <paramref name="target"/> as
    /// <paramref name="civ"/> sees it (the share of the strength), or -1 when the settlement is not in sight in its last
    /// own step: then nobody knows, and the warriors go to look (a campaign that arrives and sees it would lose turns back).
    /// </summary>
    public static int WinChancePercent(GameContent content, GameState state, CivilizationState civ, SettlementState target, int warriors)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(target);
        int cell = state.Worlds[target.World.Value].Geometry.Index(target.Cell);
        return MapMemory.SeesNow(state, civ, cell) ? Chance(content, target, warriors) : -1;
    }

    /// <summary>The chance, percent, that <paramref name="warriors"/> win against the defence of <paramref name="target"/> (its true strength).</summary>
    public static int Chance(GameContent content, SettlementState target, int warriors)
    {
        long defence = Math.Max(1, ConquerSettlementCommand.DefenceStrength(content, target));
        return (int)(warriors * 100L / Math.Max(1, warriors + defence));
    }
}

/// <summary>
/// Sends warriors of one settlement on a campaign against an enemy settlement (GDD 9.3; stage 4e PR 2, ADR 0022): they
/// leave at once and march in steps of a month; a third of the settlement's warriors stays home.
/// </summary>
public sealed record LaunchCampaignCommand(GameContent Content, SettlementId Home, SettlementId Target, int Warriors) : ICommand, IChronicledCommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Military;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ) =>
        Campaigns.CannotLaunch(Content, state, civ, Home.Value, Target.Value, Warriors) is { } reason ? CommandResult.Fail(reason) : CommandResult.Ok;

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ) => ApplyWithChronicle(state, civ);

    /// <inheritdoc />
    public string? ApplyWithChronicle(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        SettlementState home = state.FindSettlement(Home)!;
        SettlementState target = state.FindSettlement(Target)!;
        int taken = Campaigns.TakeWarriors(Content, home, Warriors);
        state.Episodes.Begin(new Episode
        {
            Kind = EpisodeKind.Campaign,
            Civ = civ,
            Other = target.Owner,
            StartMonth = state.Month,
            NextMonth = state.Month + Content.War.CampaignStepMonths,
            TickMonths = Content.War.CampaignStepMonths,
            Home = home.Id.Value,
            Target = target.Id.Value,
            Warriors = taken,
            Cell = state.Worlds[0].Geometry.Index(home.Cell),
            Phase = CampaignPhase.March,
            Loot = new Amount[Content.Goods.Count],
        });
        Treaty? war = state.Treaties.Find(civ, target.Owner, TreatyKind.War);
        if (war is not null)
        {
            war.Campaigns++;
            war.LastCampaignMonth = state.Month;
        }

        return $"{state.FindCivilization(civ)!.Name} sent {taken} warriors from {home.Name} against {target.Name}.";
    }
}

/// <summary>
/// Combat phase (GDD 11.1 phase 6; stage 4e PR 2, ADR 0022): every campaign whose step is due marches
/// <see cref="WarRules.MarchCellsPerMonth"/> cells over known land, fights when it reaches its target, takes what its
/// warriors can carry and goes home, where the survivors and the loot rejoin their settlement. A campaign turns back
/// when peace is made, the target is gone or the way is lost.
/// </summary>
public sealed class CampaignSystem(GameContent content) : ITurnSystem
{
    private const ulong FightSalt = 0xCA0000UL;
    private readonly GameContent _content = content ?? throw new ArgumentNullException(nameof(content));
    private readonly PathFinder _paths = new();

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.Combat;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        EpisodeBook book = state.Episodes;
        if (book.Count == 0)
        {
            return;
        }

        for (int k = 0; k < book.All.Count; k++)
        {
            Episode e = book.All[k];
            if (e.Ended || e.Kind != EpisodeKind.Campaign || e.NextMonth > state.Month)
            {
                continue;
            }

            Step(context, state, e);
            e.NextMonth = state.Month + e.TickMonths;
        }

        book.RemoveEnded();
    }

    private void Step(TurnContext context, GameState state, Episode e)
    {
        CivilizationState? civ = state.FindCivilization(e.Civ);
        if (civ is not { IsAlive: true })
        {
            e.Ended = true;
            return;
        }

        Treaty? war = state.Treaties.Find(e.Civ, e.Other, TreatyKind.War);
        if (war is not null)
        {
            war.CampaignMonths += e.TickMonths;
            war.LastCampaignMonth = state.Month;
        }

        WorldState world = state.Worlds[0];
        if (e.Phase == CampaignPhase.March)
        {
            SettlementState? target = state.FindSettlement(new SettlementId(e.Target));
            if (war is null || target is not { IsActive: true } || target.Owner != e.Other)
            {
                e.Phase = CampaignPhase.Return;
            }
            else
            {
                List<int>? path = _paths.Find(_content, state, civ, world, e.Cell, world.Geometry.Index(target.Cell));
                if (path is null)
                {
                    e.Phase = CampaignPhase.Return;
                }
                else if (path.Count <= _content.War.MarchCellsPerMonth)
                {
                    e.Cell = path.Count >= 2 ? path[^2] : e.Cell; // the warriors stand next to the settlement they attack
                    // Now they see it: they attack only with a good chance, or else go home (decision of 2026-11-07).
                    context.Chronicle(Campaigns.Chance(_content, target, e.Warriors) >= _content.War.MinWinChancePercent
                        ? Fight(state, e, civ, target, war)
                        : $"The warriors of {civ.Name} saw how strong {target.Name} was and turned back.");
                    e.Phase = CampaignPhase.Return;
                }
                else
                {
                    e.Cell = path[_content.War.MarchCellsPerMonth - 1];
                }

                return;
            }
        }

        GoHome(context, state, e, civ, world);
    }

    private void GoHome(TurnContext context, GameState state, Episode e, CivilizationState civ, WorldState world)
    {
        if (e.Warriors <= 0)
        {
            e.Ended = true;
            return;
        }

        SettlementState? home = state.FindSettlement(new SettlementId(e.Home));
        if (home is not { IsActive: true } || home.Owner != e.Civ)
        {
            home = Nearest(state, world, e.Civ, e.Cell);
            if (home is null)
            {
                e.Ended = true; // nowhere to go back to: the warriors scatter
                return;
            }

            e.Home = home.Id.Value;
        }

        List<int>? path = _paths.Find(_content, state, civ, world, e.Cell, world.Geometry.Index(home.Cell));
        if (path is not null && path.Count > _content.War.MarchCellsPerMonth)
        {
            e.Cell = path[_content.War.MarchCellsPerMonth - 1];
            return;
        }

        Campaigns.GiveWarriors(_content, home, e.Warriors);
        Span<Amount> stock = home.Stock;
        for (int g = 0; g < e.Loot.Length && g < stock.Length; g++)
        {
            stock[g] += e.Loot[g];
        }

        e.Ended = true;
        context.Chronicle($"{civ.Name}'s warriors came home to {home.Name}: {e.Warriors} of them.");
    }

    // The fight at the target (until battles in rounds, PR 4): the chance to win is the attackers' share of the strength.
    private string Fight(GameState state, Episode e, CivilizationState civ, SettlementState target, Treaty war)
    {
        PopulationRules rules = _content.Population;
        string attackers = civ.Name;
        CivId defender = target.Owner;
        string defenders = state.FindCivilization(defender)?.Name ?? defender.ToString();
        long attack = e.Warriors;
        long defence = Math.Max(1, ConquerSettlementCommand.DefenceStrength(_content, target));
        Contacts.RecordAttack(_content, state, defender, e.Civ);
        DeterministicRandom rng = state.Random.Derive(RandomStreamId.Combat, FightSalt | (ulong)e.Id, state.Turn);
        bool won = rng.Chance((int)Math.Min(int.MaxValue / 2, attack), (int)Math.Min(int.MaxValue / 2, attack + defence));
        int before = target.TotalPopulation();
        int lost = (int)IntMath.MulDiv(e.Warriors, won ? rules.WinnerWarriorLossPercent : rules.LoserWarriorLossPercent, 100, Rounding.Ceiling);
        e.Warriors -= lost;
        e.Dead += lost;
        SettlementAssets.Lose(_content, target, won ? rules.LoserWarriorLossPercent : rules.WinnerWarriorLossPercent, warriorsOnly: true);
        int killed = before - target.TotalPopulation();
        if (!won)
        {
            Wars.RecordFight(_content, state, defender, e.Civ, false, killed, lost);
            return $"{defenders} beat back the warriors of {attackers} at {target.Name}.";
        }

        bool taken = war.Goal == WarGoal.Land && war.GoalSettlement == target.Id.Value && !_content.SettlementLevels[target.Level].Mobile;
        string loot = Plunder(state, e, target);
        if (taken)
        {
            // Decision of 2026-11-07: a conquered village keeps its people, who now belong to the winner.
            target.Owner = e.Civ;
            target.ClaimCiv = defender.Value;
            target.ClaimUntilMonth = state.Month + (_content.Assets.ClaimYears * 12L);
        }

        Wars.RecordFight(_content, state, e.Civ, defender, taken, lost, killed);
        return taken
            ? $"{attackers} took {target.Name} from {defenders}{loot}."
            : $"{attackers} raided {target.Name} of {defenders}{loot}.";
    }

    // The winners take what their warriors can carry, the same share of every store.
    private string Plunder(GameState state, Episode e, SettlementState target)
    {
        Amount capacity = SettlementAssets.CarryCapacity(_content, state, e.Civ, e.Warriors);
        Span<Amount> stock = target.Stock;
        long total = 0;
        for (int g = 0; g < stock.Length; g++)
        {
            total += stock[g].Hundredths;
        }

        if (total <= 0 || capacity.Hundredths <= 0)
        {
            return string.Empty;
        }

        long share = Math.Min(total, capacity.Hundredths);
        long carried = 0;
        for (int g = 0; g < stock.Length && g < e.Loot.Length; g++)
        {
            long take = stock[g].Hundredths * share / total;
            stock[g] -= Amount.FromHundredths(take);
            e.Loot[g] += Amount.FromHundredths(take);
            carried += take;
        }

        return carried > 0 ? $" and carried off {carried / 100} loads of goods" : string.Empty;
    }

    private static SettlementState? Nearest(GameState state, WorldState world, CivId civ, int cell)
    {
        HexCoord at = world.Geometry.FromIndex(cell);
        SettlementState? best = null;
        int bestDistance = int.MaxValue;
        foreach (SettlementState s in state.Settlements)
        {
            if (s.IsActive && s.Owner == civ && s.World == world.Id)
            {
                int d = world.Geometry.Distance(at, s.Cell);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = s;
                }
            }
        }

        return best;
    }
}
