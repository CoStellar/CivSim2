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

    /// <summary>
    /// Whom each line of <see cref="ChronicleLines"/> is about (same index): a player sees only what its people knows
    /// (stage 6, playtest P-01; <c>CLAUDE.md</c> rule 10: the player's view passes through the fog of war).
    /// </summary>
    public IReadOnlyList<ChronicleAbout> ChronicleSubjects => AboutList;

    internal List<string> ChronicleList { get; } = [];

    internal List<ChronicleAbout> AboutList { get; } = [];

    internal void ClearChronicle()
    {
        ChronicleList.Clear();
        AboutList.Clear();
    }

    /// <summary>
    /// Writes a line about the whole world, which every player may read (the world was made, the sea rose). Lines about
    /// a people go through <see cref="Chronicle(string, int, int, int)"/>.
    /// </summary>
    public void Chronicle(string text) => Chronicle(text, -1);

    /// <summary>
    /// Writes a line to the chronicle (GDD 11.1, phase 11) about civilization <paramref name="civ"/> (-1 = the whole
    /// world), a second one it concerns (<paramref name="other"/>, -1 = none) and the cell of world 0 where it happened
    /// (<paramref name="cell"/>, -1 = none). A line about the <paramref name="inner"/> life of a people (its settlers,
    /// its unrest) is read by other peoples only when it happens on a cell they know. Use for rare, notable events only.
    /// </summary>
    public void Chronicle(string text, int civ, int other = -1, int cell = -1, bool inner = false)
    {
        ChronicleList.Add(text);
        AboutList.Add(new ChronicleAbout(civ, other, cell) { Inner = inner });
    }

    /// <summary>
    /// Writes a common line (a new leader, a drought, a new camp) to the chronicle as part of <paramref name="group"/>:
    /// when the group gets one line in a turn the line stays as it is; with more, they become one summary line in the
    /// place of the first, "<paramref name="group"/>: Name (count), Name.", where <c>{0}</c> in the group is the number
    /// of lines and the names are those of the civilizations (by <paramref name="civ"/>, in first-seen order; -1 = no one).
    /// The group is told apart by its text, so pass the same string each time (stage 5b: a calmer chronicle).
    /// With <paramref name="ownOnly"/> a player reads only its own lines of the group (they name peoples others never met).
    /// </summary>
    public void Chronicle(string text, string group, int civ, bool ownOnly = false)
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
            g.OwnOnly = ownOnly;
            ChronicleList.Add(text);
            AboutList.Add(new ChronicleAbout(civ) { Group = ownOnly ? new ChronicleGroup(group, [new ChronicleGroupMember(civ, 1, text)], ownOnly: true) : null });
        }

        g.Count++;
        for (int i = 0; i < g.Civs.Count; i++)
        {
            if (g.Civs[i].Civ == civ)
            {
                g.Civs[i] = (civ, g.Civs[i].Count + 1, g.Civs[i].Text);
                return;
            }
        }

        g.Civs.Add((civ, 1, text));
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

            var members = new ChronicleGroupMember[g.Civs.Count];
            for (int k = 0; k < members.Length; k++)
            {
                (int civ, int count, string text) = g.Civs[k];
                members[k] = new ChronicleGroupMember(civ, count, text);
            }

            var summary = new ChronicleGroup(g.Format, members, g.OwnOnly);
            ChronicleList[g.Index] = summary.Text(State, _ => true)!;
            AboutList[g.Index] = new ChronicleAbout(-1) { Group = summary };
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

        public bool OwnOnly { get; set; }

        public List<(int Civ, int Count, string Text)> Civs { get; } = [];
    }
}
