using System;
using System.Collections.Generic;
using CivSim.Protocol.Messages;

namespace CivSim.Protocol.Client;

/// <summary>What an order needs after it is chosen: nothing more, a cell on the map, or a settlement on the map.</summary>
public enum HelmTarget
{
    /// <summary>The order is sent at once.</summary>
    None = 0,

    /// <summary>The player clicks a cell.</summary>
    Cell = 1,

    /// <summary>The player clicks a cell with a settlement.</summary>
    Settlement = 2,
}

/// <summary>An order the player may give to what is selected: key, button text, the lever it belongs to (1–5) and what it still needs.</summary>
/// <param name="Key">Stable key: <c>move</c>, <c>split</c>, <c>join</c>, <c>settle</c>, <c>task:KIND</c>, <c>raid</c>, <c>campaign</c>, <c>go</c>, <c>found</c>, <c>return</c>, <c>attack</c>.</param>
/// <param name="Label">Button text.</param>
/// <param name="Lever">Lever of the order (1 where we live, 3 on the land, 4 with whom).</param>
/// <param name="Target">What the player clicks next.</param>
/// <param name="Help">Tooltip: what the order does.</param>
public sealed record HelmAction(string Key, string Label, int Lever, HelmTarget Target, string Help);

/// <summary>What the player selected on the map: one of its settlements or one of its units (stage 6 PR P2).</summary>
/// <param name="Settlement">The player's settlement on the cell, or null.</param>
/// <param name="Unit">The player's unit on the cell, or null.</param>
public sealed record HelmSelection(SettlementView? Settlement, UnitView? Unit)
{
    /// <summary>Nothing of the player's is selected.</summary>
    public bool IsEmpty => Settlement is null && Unit is null;
}

/// <summary>
/// The player's orders as the client offers them (stage 6 PR P2, ADR 0027): from the player's view only, the orders that fit what
/// is selected and the wire commands they become. The server checks every command; this only decides which buttons to show.
/// No state: every call reads the latest view.
/// </summary>
public static class HelmActions
{
    /// <summary>Share of a settlement's people that leaves with settlers the player sends (percent).</summary>
    public const int SplitPercent = 30;

    /// <summary>Names of the five levers, index = lever − 1 (GDD 1.2).</summary>
    public static readonly IReadOnlyList<string> LeverNames = ["Where we live", "Where we head", "On the land", "With whom", "Cards"];

    /// <summary>What each lever covers (tooltips).</summary>
    public static readonly IReadOnlyList<string> LeverHelp =
    [
        "Moving camps, splitting off settlers, joining camps, settling down, scouts.",
        "What we research next.",
        "Work groups on the land: woodcutting, flint, stone.",
        "War, peace and treaties, raids and war campaigns.",
        "Government (decision cards come later).",
    ];

    /// <summary>Treaty kinds for <see cref="ProposeTreatyDto"/>, index = kind − 1.</summary>
    public static readonly IReadOnlyList<string> TreatyNames = ["peace", "trade", "non-aggression", "alliance"];

    /// <summary>True when the player holds lever <paramref name="lever"/> (1–5) in <paramref name="playerLevers"/>.</summary>
    public static bool Holds(int playerLevers, int lever) => lever is >= 1 and <= 5 && (playerLevers & (1 << (lever - 1))) != 0;

    /// <summary>The newest AI decision on <paramref name="lever"/> for the player's civilization, or null.</summary>
    public static AiDecisionView? LastAiDecision(GameViewMessage view, int lever)
    {
        ArgumentNullException.ThrowIfNull(view);
        AiDecisionView? best = null;
        foreach (AiDecisionView d in view.AiDecisions ?? [])
        {
            if (d.Lever == lever && (best is null || d.Turn >= best.Turn))
            {
                best = d;
            }
        }

        return best;
    }

    /// <summary>What of <paramref name="civ"/> stands on the cell: its settlement first, otherwise its unit (the newest when several).</summary>
    public static HelmSelection Select(GameViewMessage view, int civ, int world, int col, int row)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new HelmSelection(SettlementAt(view, world, col, row, civ), UnitAt(view, civ, world, col, row));
    }

    /// <summary>The active settlement on the cell (of <paramref name="owner"/>, or of anybody when it is −1), or null.</summary>
    public static SettlementView? SettlementAt(GameViewMessage view, int world, int col, int row, int owner = -1)
    {
        ArgumentNullException.ThrowIfNull(view);
        foreach (SettlementView s in view.Settlements ?? [])
        {
            if (s.Status == 0 && s.World == world && s.Col == col && s.Row == row && (owner < 0 || s.Owner == owner))
            {
                return s;
            }
        }

        return null;
    }

    /// <summary>People in a settlement (0 when the view does not show them).</summary>
    public static int People(SettlementView s)
    {
        ArgumentNullException.ThrowIfNull(s);
        int n = 0;
        foreach (int p in s.Population ?? [])
        {
            n += p;
        }

        return n;
    }

    /// <summary>Orders for what is selected (empty when nothing of the player's is).</summary>
    public static IReadOnlyList<HelmAction> For(HelmSelection selection, ContentMessage? content)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var list = new List<HelmAction>();
        if (selection.Unit is { } u)
        {
            list.Add(new HelmAction("go", "Go to...", 1, HelmTarget.Cell, "Walk to a cell and wait there (scouts look around and come back)."));
            if (u.Role == 0)
            {
                list.Add(new HelmAction("found", "Found camp here", 1, HelmTarget.None, "The settlers found a camp where they stand."));
            }

            if (u.Role == 2)
            {
                list.Add(new HelmAction("attack", "Attack...", 4, HelmTarget.Settlement, "March on a settlement and fight."));
            }

            list.Add(new HelmAction("return", "Return home", 1, HelmTarget.None, "Walk back and join the home settlement."));
        }

        if (selection.Settlement is not null)
        {
            list.Add(new HelmAction("move", "Move camp to...", 1, HelmTarget.Cell, "The whole camp walks to a new place."));
            list.Add(new HelmAction("split", "Send settlers to...", 1, HelmTarget.Cell, $"About {SplitPercent}% of the people leave to found a camp there."));
            list.Add(new HelmAction("join", "Join camp...", 1, HelmTarget.Settlement, "Leave this place and join another of our camps."));
            list.Add(new HelmAction("settle", "Settle down", 1, HelmTarget.None, "Stay here for good (needs farming)."));
            foreach (TaskInfo t in content?.Tasks ?? [])
            {
                list.Add(new HelmAction("task:" + t.Key, t.Name + "...", 3, HelmTarget.Cell, $"Send a work group to a cell: {t.Name}."));
            }

            list.Add(new HelmAction("raid", "Raid...", 4, HelmTarget.Settlement, "Warriors raid a settlement for its stores and come back."));
            list.Add(new HelmAction("campaign", "War campaign...", 4, HelmTarget.Settlement, "Send an army against a settlement of an enemy we are at war with."));
        }

        return list;
    }

    /// <summary>
    /// The wire command of <paramref name="action"/> for <paramref name="selection"/>, with the cell the player clicked (for cell and
    /// settlement targets) and the settlement on it (for settlement targets); null when the target is missing.
    /// </summary>
    public static ICommandDto? Build(HelmAction action, HelmSelection selection, int col = -1, int row = -1, SettlementView? target = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(selection);
        if ((action.Target == HelmTarget.Cell && col < 0) || (action.Target == HelmTarget.Settlement && target is null))
        {
            return null;
        }

        if (selection.Unit is { } u && action.Key is "go" or "found" or "return" or "attack")
        {
            return action.Key switch
            {
                "go" => new MoveUnitDto(u.Id, col, row),
                "found" => new FoundCampDto(u.Id),
                "return" => new ReturnUnitDto(u.Id),
                _ => new AttackDto(u.Id, target!.Id),
            };
        }

        if (selection.Settlement is not { } s)
        {
            return null;
        }

        if (action.Key.StartsWith("task:", StringComparison.Ordinal))
        {
            return new StartTaskDto(s.Id, action.Key["task:".Length..], col, row);
        }

        return action.Key switch
        {
            "move" => new MoveSettlementDto(s.Id, col, row),
            "split" => new SplitSettlementDto(s.Id, col, row, SplitPercent),
            "join" => new AbandonSettlementDto(s.Id, target!.Id),
            "settle" => new SettleDto(s.Id),
            "raid" => new PlunderSettlementDto(s.Id, target!.Id),
            "campaign" => new LaunchCampaignDto(s.Id, target!.Id, Math.Max(1, People(s) / 5)),
            _ => null,
        };
    }

    private static UnitView? UnitAt(GameViewMessage view, int civ, int world, int col, int row)
    {
        UnitView? best = null;
        foreach (UnitView u in view.Units ?? [])
        {
            if (u.Owner == civ && u.World == world && u.Col == col && u.Row == row && (best is null || u.Id > best.Id))
            {
                best = u;
            }
        }

        return best;
    }
}
