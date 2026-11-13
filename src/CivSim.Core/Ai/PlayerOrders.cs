using System;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Episodes;
using CivSim.Core.State;
using CivSim.Core.Units;

namespace CivSim.Core.Ai;

/// <summary>
/// A player's order holds (stage 6 PR P1, ADR 0027; Patryk 2026-11-13: "a decision of the player is carried out to the end"). What a
/// player's command acted on is held from the AI:
/// <list type="bullet">
/// <item>a settlement the player gave an order to (moved, split, joined, raided from, gave a task) and a camp the player's settlers
/// founded, for <see cref="AiRules.PlayerOrderHoldYears"/> years of action, and as long as a work group the player sent from it is out;</item>
/// <item>a unit the player gave an order to, until it is gone (settlers, scouts, work groups);</item>
/// <item>a war campaign the player directed, until it ends: the AI does not recall it and does not make peace with its enemy meanwhile.</item>
/// </list>
/// The AI does not issue commands against what is held (<see cref="Advisor"/>), and the turn refuses any it issued in the same tick
/// (<see cref="Simulation.TurnRunner"/>). The world itself still acts: hunger, a lost battle, a place taken by others.
/// </summary>
public static class PlayerOrders
{
    /// <summary>True when an AI command <paramref name="command"/> of <paramref name="civ"/> would undo or overrule a player's order that still holds.</summary>
    public static bool HeldAgainstAi(GameState state, CivId civ, ICommand command)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        switch (command)
        {
            case MoveSettlementCommand m:
                return Held(m.Content, state, m.Settlement);
            case SplitSettlementCommand s:
                return Held(s.Content, state, s.Settlement);
            case AbandonSettlementCommand a:
                return Held(a.Content, state, a.Settlement);
            case OccupySettlementCommand o:
                return Held(o.Content, state, o.From);
            case PlunderSettlementCommand p:
                return Held(p.Content, state, p.From);
            case ConquerSettlementCommand c:
                return Held(c.Content, state, c.From);
            case Perception.JourneyCommand j:
                return Held(j.Content, state, j.Settlement);
            case StartTaskCommand t:
                return Held(t.Content, state, t.Settlement);
            case RecallCampaignCommand r:
                return state.Episodes.Find(r.Episode) is { ByPlayer: true, Ended: false };
            case ProposeTreatyCommand { Kind: TreatyKind.Peace } peace:
                return PlayerCampaignAgainst(state, civ, peace.Other);
            case AnswerProposalCommand { Kind: TreatyKind.Peace, Accept: true } yes:
                return PlayerCampaignAgainst(state, civ, yes.From);
            default:
                return false;
        }
    }

    /// <summary>
    /// After a player's command was applied: marks what it acted on as held (the settlement, the new unit, the directed campaign).
    /// </summary>
    public static void Mark(GameState state, CivId civ, ICommand command, long unitsBefore)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        switch (command)
        {
            case MoveSettlementCommand m:
                Hold(state, m.Settlement);
                break;
            case SplitSettlementCommand s:
                Hold(state, s.Settlement);
                MarkNewUnits(state, unitsBefore); // the settlers: the camp they found is held too (UnitSystem)
                break;
            case AbandonSettlementCommand a:
                Hold(state, a.Into); // the camp it joined
                break;
            case OccupySettlementCommand o:
                Hold(state, o.From);
                break;
            case PlunderSettlementCommand p:
                Hold(state, p.From);
                break;
            case ConquerSettlementCommand c:
                Hold(state, c.From);
                break;
            case StartTaskCommand t:
                Hold(state, t.Settlement);
                MarkNewUnits(state, unitsBefore); // the work group: its home is held while it is out
                break;
            case MoveUnitCommand mu:
                MarkUnit(state, mu.Unit);
                break;
            case FoundCampCommand f:
                MarkUnit(state, f.Unit);
                break;
            case ReturnUnitCommand ru:
                MarkUnit(state, ru.Unit);
                break;
            case AttackCommand at:
                MarkUnit(state, at.Unit);
                break;
        }
    }

    /// <summary>True when the player's order on <paramref name="settlement"/> still holds.</summary>
    public static bool Held(GameContent content, GameState state, SettlementId settlement)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        SettlementState? s = state.FindSettlement(settlement);
        if (s is null)
        {
            return false;
        }

        if (s.PlayerOrderTick >= 0 && state.Turn < s.PlayerOrderTick + HoldTicks(content))
        {
            return true;
        }

        UnitBook book = state.Units;
        for (int k = 0; k < book.Count; k++)
        {
            Unit u = book[k];
            if (!u.Gone && u.ByPlayer && u.Role == UnitRole.Workers && u.Home == s.Id.Value)
            {
                return true; // a work group the player sent from it is out
            }
        }

        return false;
    }

    /// <summary>How long a player's order on a settlement holds, in ticks.</summary>
    public static long HoldTicks(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.Time.ActionTicks(12L * content.Ai.PlayerOrderHoldYears);
    }

    /// <summary>Marks <paramref name="s"/> as ordered by a player now.</summary>
    public static void Hold(GameState state, SettlementId s)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.FindSettlement(s) is { } settlement)
        {
            settlement.PlayerOrderTick = state.Turn;
        }
    }

    private static void MarkUnit(GameState state, long id)
    {
        if (state.Units.Find(id) is { } u)
        {
            u.ByPlayer = true;
            if (u.Role == UnitRole.Army && UnitCommandRules.CampaignOf(state, u) is { } e)
            {
                e.ByPlayer = true; // the player directs this campaign now
            }
        }
    }

    private static void MarkNewUnits(GameState state, long unitsBefore)
    {
        for (long id = unitsBefore; id < state.Units.NextId; id++)
        {
            MarkUnit(state, id);
        }
    }

    // A campaign the player directs against <paramref name="other"/> is under way.
    private static bool PlayerCampaignAgainst(GameState state, CivId civ, CivId other)
    {
        foreach (Episode e in state.Episodes.All)
        {
            if (e.ByPlayer && !e.Ended && e.Kind == EpisodeKind.Campaign && e.Civ == civ && e.Other == other)
            {
                return true;
            }
        }

        return false;
    }
}
