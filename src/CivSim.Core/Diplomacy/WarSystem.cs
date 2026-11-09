using System;
using CivSim.Core.Content;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Diplomacy;

/// <summary>
/// State phase of war (GDD 9.3; stage 4e, ADR 0021): every turn each war's weariness is worked out again from its years
/// and its dead, and every civilization at war feels the worst of its wars in the mood of its settlements (effect
/// war_weariness, data/effects.json), which lasts one more turn after the last war ends.
/// </summary>
public sealed class WarSystem : ITurnSystem
{
    private readonly GameContent _content;
    private readonly EffectDefinition? _weariness;

    /// <summary>Creates the system.</summary>
    public WarSystem(GameContent content)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        foreach (EffectDefinition e in content.Effects)
        {
            if (e.Key == "war_weariness")
            {
                _weariness = e;
            }
        }
    }

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.State;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        long month = state.Month;
        bool any = false;

        // ADR 0022: a war in which nobody campaigned for some years ends in a white peace (ADR 0025: years of action, in ticks).
        long quiet = _content.Time.ActionTicks(_content.War.QuietPeaceYears * 12L);
        long tick = state.Turn;
        for (int k = state.Treaties.All.Count - 1; k >= 0; k--)
        {
            Treaty t = state.Treaties.All[k];
            if (t.Kind == TreatyKind.War && tick - t.QuietSince >= quiet
                && !state.Episodes.Campaigning(t.A, t.B) && !state.Episodes.Campaigning(t.B, t.A))
            {
                string line = Treaties.Sign(_content, state, t.A, t.B, TreatyKind.Peace, PeaceTerms.White);
                context.Chronicle(line + " Nobody had fought for years.");
            }
        }

        foreach (Treaty t in state.Treaties.All)
        {
            if (t.Kind != TreatyKind.War)
            {
                continue;
            }

            t.WearinessA = Wars.ComputeWeariness(_content.War, t, t.A, month);
            t.WearinessB = Wars.ComputeWeariness(_content.War, t, t.B, month);
            any = true;
        }

        if (!any || _weariness is null || _content.War.MoodAtFullWeariness == 0 || state.WorldMonths == 0)
        {
            return;
        }

        // In the world's pulse, until just after the next one, so that every pulse of a civilization in between still
        // feels it (ADR 0025).
        long until = month + state.WorldMonths + 1;
        foreach (CivilizationState civ in state.Civilizations)
        {
            if (!civ.IsAlive)
            {
                continue;
            }

            int w = Wars.Weariness(state, civ.Id);
            if (w > 0)
            {
                int mood = -(w * _content.War.MoodAtFullWeariness / 100);
                if (mood != 0)
                {
                    state.Effects.Add(_weariness, WorldId.Earth, civ.Id.Value, mood, until);
                }
            }
        }
    }
}
