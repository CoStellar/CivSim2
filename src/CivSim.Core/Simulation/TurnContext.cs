using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CivSim.Core.Commands;
using CivSim.Core.Random;
using CivSim.Core.State;

namespace CivSim.Core.Simulation;

/// <summary>Everything a system may use while a turn runs.</summary>
public sealed class TurnContext
{
    internal TurnContext(GameState state)
    {
        State = state;
    }

    /// <summary>The game state being advanced.</summary>
    public GameState State { get; }

    /// <summary>Per-system random streams.</summary>
    public RandomStreams Random => State.Random;

    /// <summary>The turn being computed (completed turns + 1).</summary>
    public long CurrentTurn => State.Turn + 1;

    /// <summary>Commands for this turn, in resolution order. Filled before phase 4.</summary>
    public IReadOnlyList<CommandEnvelope> Commands => CommandList;

    internal List<CommandEnvelope> CommandList { get; } = [];

    /// <summary>Chronicle lines written during this turn (cleared when the turn starts).</summary>
    public IReadOnlyList<string> ChronicleLines => ChronicleList;

    internal List<string> ChronicleList { get; } = [];

    /// <summary>Writes a line to the chronicle (GDD 11.1, phase 11). Use for rare, notable events only.</summary>
    public void Chronicle(string text) => ChronicleList.Add(text);

    /// <summary>
    /// Writes a common line (a new leader, a drought, a new camp) to the chronicle as part of <paramref name="group"/>:
    /// when the group gets one line in a turn the line stays as it is; with more, they become one summary line in the
    /// place of the first, "<paramref name="group"/>: Name (count), Name.", where <c>{0}</c> in the group is the number
    /// of lines and the names are those of the civilizations (by <paramref name="civ"/>, in first-seen order; -1 = no one).
    /// The group is told apart by its text, so pass the same string each time (stage 5b: a calmer chronicle).
    /// </summary>
    public void Chronicle(string text, string group, int civ)
    {
        Group? g = null;
        for (int i = 0; i < _groupCount; i++)
        {
            if (string.Equals(_groups[i].Format, group, System.StringComparison.Ordinal))
            {
                g = _groups[i];
                break;
            }
        }

        if (g is null)
        {
            if (_groupCount == _groups.Count)
            {
                _groups.Add(new Group());
            }

            g = _groups[_groupCount++];
            g.Format = group;
            g.Index = ChronicleList.Count;
            g.Count = 0;
            g.Civs.Clear();
            ChronicleList.Add(text);
        }

        g.Count++;
        for (int i = 0; i < g.Civs.Count; i++)
        {
            if (g.Civs[i].Civ == civ)
            {
                g.Civs[i] = (civ, g.Civs[i].Count + 1);
                return;
            }
        }

        g.Civs.Add((civ, 1));
    }

    // Turns the groups of this turn with more than one line into their summary lines.
    internal void FinishGroups()
    {
        for (int i = 0; i < _groupCount; i++)
        {
            Group g = _groups[i];
            if (g.Count < 2)
            {
                continue;
            }

            var sb = new StringBuilder();
            sb.Append(string.Format(CultureInfo.InvariantCulture, g.Format, g.Count)).Append(": ");
            for (int k = 0; k < g.Civs.Count; k++)
            {
                (int civ, int count) = g.Civs[k];
                sb.Append(k == 0 ? string.Empty : ", ")
                    .Append(civ >= 0 && State.FindCivilization(new CivId(civ)) is { } c ? c.Name : "no one");
                if (count > 1)
                {
                    sb.Append(" (").Append(count.ToString(CultureInfo.InvariantCulture)).Append(')');
                }
            }

            ChronicleList[g.Index] = sb.Append('.').ToString();
        }

        _groupCount = 0;
    }

    private readonly List<Group> _groups = [];
    private int _groupCount;

    private sealed class Group
    {
        public string Format { get; set; } = string.Empty;

        public int Index { get; set; }

        public int Count { get; set; }

        public List<(int Civ, int Count)> Civs { get; } = [];
    }
}
