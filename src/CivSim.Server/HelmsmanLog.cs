using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CivSim.Core.Ai;
using CivSim.Core.Ai.Utility;
using CivSim.Core.Content;
using CivSim.Core.State;
using CivSim.Protocol.Messages;

namespace CivSim.Server;

/// <summary>
/// What the AI did for each civilization led by a player (ADR 0027, stage 6 PR P): the last decisions of the council for that
/// civilization, with their lever and their reasons, so the player sees what happens on the levers they did not take. Session data:
/// not saved and not hashed, the simulation does not read it.
/// </summary>
public sealed class HelmsmanLog(GameContent content, int keep = 30)
{
    private readonly GameContent _content = content ?? throw new ArgumentNullException(nameof(content));
    private readonly Dictionary<int, List<AiDecisionView>> _byCiv = [];
    private readonly StringBuilder _text = new();

    /// <summary>Keeps the decisions of the last decisions phase made for civilizations led by a player.</summary>
    public void Add(GameState state, DecisionLog log)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(log);
        foreach (Decision d in log.Entries)
        {
            CivilizationState? civ = state.FindCivilization(d.Civ);
            if (civ is null || civ.Controller == PlayerId.Ai)
            {
                continue;
            }

            if (!_byCiv.TryGetValue(d.Civ.Value, out List<AiDecisionView>? list))
            {
                list = [];
                _byCiv[d.Civ.Value] = list;
            }

            list.Add(new AiDecisionView(d.Turn, Levers.Number(Levers.Of(d.Action)), Text(state, d)));
            if (list.Count > keep)
            {
                list.RemoveRange(0, list.Count - keep);
            }
        }
    }

    /// <summary>The kept decisions for <paramref name="civ"/>, oldest first.</summary>
    public IReadOnlyList<AiDecisionView> For(CivId civ) =>
        _byCiv.TryGetValue(civ.Value, out List<AiDecisionView>? list) ? list.ToArray() : [];

    // "Move camp: Thugreae; better than here 40% (1200), crowding 85%": what, where, and the first two reasons.
    private string Text(GameState state, in Decision d)
    {
        _text.Clear();
        _text.Append(Words(d.Action.ToString()));
        string? subject = Place(state, d.Action, d.Subject);
        if (subject is not null)
        {
            _text.Append(": ").Append(subject);
        }

        string? target = Target(state, d);
        if (target is not null)
        {
            _text.Append(" → ").Append(target);
        }

        AppendReason(d.First, "; ");
        AppendReason(d.Second, ", ");
        return _text.ToString();
    }

    private void AppendReason(Factor f, string separator)
    {
        if (f.Id != FactorId.None)
        {
            _text.Append(separator).Append(Decision.Describe(f));
        }
    }

    // Subjects are settlement ids for the governor's and the commander's actions (research and diplomacy have none).
    private string? Place(GameState state, AiAction action, int id)
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

    // "SendSettlers" → "Send settlers".
    private static string Words(string name)
    {
        var b = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c))
            {
                b.Append(' ').Append(char.ToLower(c, CultureInfo.InvariantCulture));
            }
            else
            {
                b.Append(c);
            }
        }

        return b.ToString();
    }
}
