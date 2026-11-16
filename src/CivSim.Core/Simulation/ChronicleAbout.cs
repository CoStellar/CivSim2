using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CivSim.Core.State;

namespace CivSim.Core.Simulation;

/// <summary>
/// Whom a chronicle line is about (stage 6, playtest P-01): the civilization it is about (-1 = the whole world), a second
/// one it concerns (-1 = none), the cell of world 0 where it happened (-1 = none) and, for a summary of several lines,
/// the group it sums up. The server sends a player only the lines its people may know.
/// </summary>
public readonly record struct ChronicleAbout(int Civ, int Other = -1, int Cell = -1)
{
    /// <summary>The lines a summary line stands for, or null for a plain line.</summary>
    public ChronicleGroup? Group { get; init; }

    /// <summary>
    /// True for the inner life of <see cref="Civ"/> (its settlers, work groups, unrest): another people reads it only when
    /// it happens on a cell that people knows.
    /// </summary>
    public bool Inner { get; init; }

    /// <summary>
    /// The text <paramref name="civ"/> may read in place of <paramref name="text"/>, or null when it may read nothing of
    /// it: lines about itself and peoples it met (all the peoples a line names), lines on cells it knows and lines about
    /// the whole world.
    /// A summary keeps only the peoples it met (one left: that people's own line).
    /// </summary>
    public string? TextFor(string text, GameState state, Content.GameContent content, CivilizationState civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        if (Group is { } group)
        {
            return group.Text(state, c => group.OwnOnly ? c == civ.Id.Value : Knows(civ, c));
        }

        if (Civ < 0 && Other < 0)
        {
            return Cell < 0 || Perception.MapMemory.Knows(content, state, civ, Cell) ? text : null;
        }

        if (Inner && Civ != civ.Id.Value)
        {
            return Cell >= 0 && Knows(civ, Civ) && Perception.MapMemory.Knows(content, state, civ, Cell) ? text : null;
        }

        // Every people the line names must be known: "A declared war on B" would tell a player who met only A of B.
        return (Civ < 0 || Knows(civ, Civ)) && (Other < 0 || Knows(civ, Other)) ? text : null;
    }

    // A civilization knows itself and every people it met (a war, a rumour, a sighting all count as meeting).
    private static bool Knows(CivilizationState civ, int other) =>
        other >= 0 && (other == civ.Id.Value || civ.Relation(new CivId(other)) is { HasMet: true });
}

/// <summary>One people's part of a summary line: how many lines it had and the first of them.</summary>
public readonly record struct ChronicleGroupMember(int Civ, int Count, string Text);

/// <summary>A summary line of several lines of one kind in a turn ("New leaders in 3 peoples: A (2), B.").</summary>
public sealed class ChronicleGroup(string format, IReadOnlyList<ChronicleGroupMember> members, bool ownOnly = false)
{
    /// <summary>True when a people may read only its own lines of the group (they name peoples others never met).</summary>
    public bool OwnOnly { get; } = ownOnly;

    /// <summary>The group's text with <c>{0}</c> for the number of lines.</summary>
    public string Format { get; } = format;

    /// <summary>The peoples in the line, in first-seen order.</summary>
    public IReadOnlyList<ChronicleGroupMember> Members { get; } = members;

    /// <summary>
    /// The line with only the members <paramref name="sees"/> lets through: null for none, that member's own first line
    /// when it is the only one with a single line, otherwise the summary of those.
    /// </summary>
    public string? Text(GameState state, Func<int, bool> sees)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(sees);
        int lines = 0;
        int shown = 0;
        ChronicleGroupMember only = default;
        foreach (ChronicleGroupMember m in Members)
        {
            if (sees(m.Civ))
            {
                lines += m.Count;
                shown++;
                only = m;
            }
        }

        if (shown == 0)
        {
            return null;
        }

        if (lines == 1)
        {
            return only.Text;
        }

        var sb = new StringBuilder();
        sb.Append(string.Format(CultureInfo.InvariantCulture, Format, lines)).Append(": ");
        bool first = true;
        foreach (ChronicleGroupMember m in Members)
        {
            if (!sees(m.Civ))
            {
                continue;
            }

            sb.Append(first ? string.Empty : ", ")
                .Append(m.Civ >= 0 && state.FindCivilization(new CivId(m.Civ)) is { } c ? c.Name : "no one");
            if (m.Count > 1)
            {
                sb.Append(" (").Append(m.Count.ToString(CultureInfo.InvariantCulture)).Append(')');
            }

            first = false;
        }

        return sb.Append('.').ToString();
    }
}
