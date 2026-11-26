using System;
using System.Collections.Generic;
using System.Globalization;
using CivSim.Core.Ai;
using CivSim.Core.Ai.Utility;
using CivSim.Core.Content;
using CivSim.Core.State;
using CivSim.Protocol.Messages;

namespace CivSim.Server;

/// <summary>
/// What the AI did for each civilization led by a player (ADR 0027, stage 6 PR P): the last decisions of the council for that
/// civilization, each told in plain words (what it did and the one reason that weighed most; playtest P-05, 2026-11-16), so the
/// player sees what happens on the levers they did not take. Weights and numbers stay in the <c>--decisions</c> log. Decisions on
/// no lever (recipes) are left out, a strategy only when its direction moves far, and the same action on the same target
/// within half a minute is one line ("Split camps Bea, Loan: ..."). Session data: not saved and not hashed, the simulation does
/// not read it.
/// </summary>
public sealed class HelmsmanLog(GameContent content, int keep = 30)
{
    private readonly GameContent _content = content ?? throw new ArgumentNullException(nameof(content));
    private readonly Dictionary<int, List<Entry>> _byCiv = [];
    private readonly Dictionary<int, int> _heading = []; // the last expansion target of each civilization's strategy

    /// <summary>A strategy whose target moves less than this many cells is the same direction for the player.</summary>
    public const int HeadingCells = 8;

    /// <summary>The same action on the same target within this many seconds of game time adds its place to one line.</summary>
    public const int MergeSeconds = 30;

    /// <summary>Keeps the decisions of the last decisions phase made for civilizations led by a player.</summary>
    public void Add(GameState state, DecisionLog log)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(log);
        foreach (Decision d in log.Entries)
        {
            CivilizationState? civ = state.FindCivilization(d.Civ);
            Lever lever = Levers.Of(d.Action);
            if (civ is null || civ.Controller == PlayerId.Ai || lever == Lever.None || !NewHeading(state, d))
            {
                continue;
            }

            if (!_byCiv.TryGetValue(d.Civ.Value, out List<Entry>? list))
            {
                list = [];
                _byCiv[d.Civ.Value] = list;
            }

            string? place = Place(state, d.Action, d.Subject);
            string? target = Target(state, d);
            if (d.Action != AiAction.SetStrategy && Recent(list, d, target) is { } same)
            {
                if (place is not null && !same.Places.Contains(place))
                {
                    same.Places.Add(place);
                }

                continue;
            }

            var entry = new Entry(d.Turn, d.Action, Levers.Number(lever), target, Reason(d));
            if (place is not null)
            {
                entry.Places.Add(place);
            }

            list.Add(entry);
            if (list.Count > keep)
            {
                list.RemoveRange(0, list.Count - keep);
            }
        }
    }

    /// <summary>The kept decisions for <paramref name="civ"/>, oldest first.</summary>
    public IReadOnlyList<AiDecisionView> For(CivId civ)
    {
        if (!_byCiv.TryGetValue(civ.Value, out List<Entry>? list))
        {
            return [];
        }

        var views = new AiDecisionView[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            views[i] = new AiDecisionView(list[i].Turn, list[i].Lever, list[i].Text());
        }

        return views;
    }

    // The line of the same action on the same target from the last half minute, if any.
    private Entry? Recent(List<Entry> list, in Decision d, string? target)
    {
        long since = d.Turn - ((long)MergeSeconds * Math.Max(1, _content.Time.TicksPerSecond));
        for (int i = list.Count - 1; i >= 0 && list[i].Turn >= since; i--)
        {
            if (list[i].Action == d.Action && string.Equals(list[i].Target, target, StringComparison.Ordinal))
            {
                return list[i];
            }
        }

        return null;
    }

    // A strategy is news only when its direction moves far; the review itself repeats every few steps and the target
    // shifts a few cells each time.
    private bool NewHeading(GameState state, in Decision d)
    {
        if (d.Action != AiAction.SetStrategy)
        {
            return true;
        }

        bool known = _heading.TryGetValue(d.Civ.Value, out int before);
        Core.Map.HexGlobe geo = state.Worlds[0].Geometry;
        bool far = !known || (before < 0) != (d.Target < 0)
            || (d.Target >= 0 && geo.Distance(geo.FromIndex(before), geo.FromIndex(d.Target)) >= HeadingCells);
        if (far)
        {
            _heading[d.Civ.Value] = d.Target; // remembered only when shown, so slow drifts add up
        }

        return far;
    }

    // The reason in words: the first of the decision's factors (most important first) that says something to a player.
    // Goal weights, character and distances are the AI's own scales, so they never stand as a reason.
    private string? Reason(in Decision d)
    {
        foreach (Factor f in d.Factors())
        {
            if (Words(d.Action, f) is { } words)
            {
                return words;
            }
        }

        return Default(d.Action);
    }

    private string? Words(AiAction action, Factor f)
    {
        long n = f.Input;
        return f.Id switch
        {
            FactorId.Crowding when n >= 50 => Say($"the camp is {n}% full"),
            FactorId.Fed when n < 1000 => "our people go hungry",
            FactorId.BetterThanHere => "the land there is better",
            FactorId.WornLand => "the land here is worn out",
            FactorId.FarmYield => Say($"fields there would feed {n}% of the people"),
            FactorId.OverGathering => "gathering no longer feeds us",
            FactorId.People when action == AiAction.JoinCamp => Say($"only {n} people, too few to live alone"),
            FactorId.Unknown when n > 0 => Say($"{n} unknown cells nearby"),
            FactorId.ServesGoal => Goal((Goal)n),
            FactorId.LeadsTo => "it opens the way to more",
            FactorId.ResearchCost => "it is within reach",
            FactorId.TradeGain => "trade with them pays",
            FactorId.Opinion when n < 0 => "they wronged us",
            FactorId.Loot => "for their goods",
            FactorId.Strength when n > 100 => Say($"we are stronger ({n}%)"),
            FactorId.WinChance => Say($"chance to win {n}%"),
            FactorId.FoodMonths when action == AiAction.RecallCampaign => "food is running out",
            FactorId.ExpectedLoss => Say($"we would lose {n}% of the warriors"),
            FactorId.Weariness => "our people are tired of war",
            FactorId.War => "we are at war with them",
            FactorId.Shortage when action == AiAction.RecallTask => n < 0 ? "we need none" : Say($"{n} months of it in store"),
            FactorId.Shortage => n < 0 ? null : n == 0 ? "none of it in store" : Say($"only {n} months of it in store"),
            FactorId.Stability => "to calm the unrest",
            FactorId.Administration => "the land has grown too large to rule",
            _ => null,
        };
    }

    private static string Say(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    private static string Goal(Goal goal) => goal switch
    {
        Core.Ai.Goal.Expansion => "to spread to new land",
        Core.Ai.Goal.Security => "to keep us safe",
        Core.Ai.Goal.Prosperity => "to live better",
        Core.Ai.Goal.Science => "to learn more",
        Core.Ai.Goal.Influence => "to be heard by others",
        _ => "to win wars",
    };

    // What is true of every decision of the kind, when none of its factors says more.
    private static string? Default(AiAction action) => action switch
    {
        AiAction.SplitCamp or AiAction.SendSettlers => "the people have grown",
        AiAction.MoveCamp or AiAction.Journey => "looking for better land",
        AiAction.Explore => "to learn what lies around",
        AiAction.JoinCamp => "too few people to live alone",
        AiAction.Occupy => "it stood empty",
        AiAction.Plunder => "goods were left there",
        AiAction.Conquer => "we are at war with them",
        AiAction.SetStrategy => "the land around has changed",
        _ => null,
    };

    // Subjects are settlement ids for the governor's and the commander's actions (research and diplomacy have none).
    private static string? Place(GameState state, AiAction action, int id)
    {
        return action is AiAction.SetResearch or AiAction.SetStrategy or AiAction.ProposeTreaty or AiAction.AcceptTreaty or AiAction.DeclineTreaty or AiAction.DeclareWar
            or AiAction.ChangeGovernment
            ? null
            : state.FindSettlement(new SettlementId(id))?.Name;
    }

    private string? Target(GameState state, in Decision d)
    {
        switch (d.Action)
        {
            case AiAction.SetResearch:
                return d.Target >= 0 && d.Target < _content.Technologies.Count ? _content.Technologies[d.Target].Name : null;
            case AiAction.ChangeGovernment:
                return d.Target >= 0 && d.Target < _content.Governments.Count ? _content.Governments[d.Target].Name : null;
            case AiAction.StartTask:
            case AiAction.RecallTask:
                return d.Target >= 0 && d.Target < _content.Tasks.Kinds.Count ? _content.Tasks.Kinds[d.Target].Name : null;
            case AiAction.ProposeTreaty:
            case AiAction.AcceptTreaty:
            case AiAction.DeclineTreaty:
            case AiAction.DeclareWar:
                return state.FindCivilization(new CivId(d.Target))?.Name;
            case AiAction.Plunder:
            case AiAction.Conquer:
            case AiAction.Occupy:
            case AiAction.JoinCamp:
            case AiAction.LaunchCampaign:
            case AiAction.RecallCampaign:
                return state.FindSettlement(new SettlementId(d.Target))?.Name;
            default:
                return null;
        }
    }

    // One line of the panel; places grow when the same action repeats in the same turn.
    private sealed class Entry(long turn, AiAction action, int lever, string? target, string? reason)
    {
        public long Turn { get; } = turn;

        public AiAction Action { get; } = action;

        public int Lever { get; } = lever;

        public string? Target { get; } = target;

        public List<string> Places { get; } = [];

        // "Split camps Bea, Loan: the people have grown." / "Research Fire: to keep us safe."
        public string Text()
        {
            string places = string.Join(", ", Places);
            string what = Action switch
            {
                AiAction.MoveCamp => Many("Moved camp", "Moved camps") + " " + places,
                AiAction.SplitCamp => Many("Split camp", "Split camps") + " " + places,
                AiAction.Journey => places + " set out on a journey",
                AiAction.Settle => places + Many(" settled down as a village", " settled down as villages"),
                AiAction.JoinCamp => places + " joined " + Target,
                AiAction.Plunder => places + " took the goods left in " + Target,
                AiAction.Conquer => places + " drove out " + Target,
                AiAction.Occupy => "People of " + places + " moved into " + Target,
                AiAction.SetResearch => "Research " + Target,
                AiAction.SetStrategy => "New direction for our people",
                AiAction.ProposeTreaty => "Offered a treaty to " + Target,
                AiAction.AcceptTreaty => "Accepted the treaty of " + Target,
                AiAction.DeclineTreaty => "Turned down the treaty of " + Target,
                AiAction.DeclareWar => "Declared war on " + Target,
                AiAction.SendSettlers => places + " sent settlers",
                AiAction.Explore => places + " sent explorers",
                AiAction.LaunchCampaign => "Warriors of " + places + " march on " + Target,
                AiAction.RecallCampaign => "Called home the warriors marching on " + Target,
                AiAction.ChangeGovernment => "Changed government to " + Target,
                AiAction.StartTask => places + " sent a work group: " + Target,
                AiAction.RecallTask => places + " called its work group home: " + Target,
                _ => Action.ToString(),
            };

            return reason is null ? what + "." : what + ": " + reason + ".";
        }

        private string Many(string one, string more) => Places.Count > 1 ? more : one;
    }
}
