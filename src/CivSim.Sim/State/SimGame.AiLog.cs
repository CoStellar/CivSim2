using System.Collections.Generic;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>What an AI decision was about (the AI decision log, tests 2026-11-23).</summary>
public enum AiLogKind
{
    /// <summary>Starting, sparing or scouting a war target.</summary>
    War = 0,

    /// <summary>Offering or answering peace.</summary>
    Peace = 1,

    /// <summary>Treaties.</summary>
    Diplomacy = 2,

    /// <summary>The technology it researches.</summary>
    Research = 3,

    /// <summary>Settlers and new settlements.</summary>
    Expansion = 4,
}

/// <summary>One AI decision: when, which people, about what and why.</summary>
public sealed record AiLogEntry(long Tick, int Civ, AiLogKind Kind, string Text);

/// <summary>
/// The AI decision log (Patryk, 2026-11-23): off by default, so it costs nothing in a game; the headless report and scenarios
/// switch it on. It does not touch the game state (not in the hash). The AI checks <see cref="AiLogOn"/> before writing the text.
/// </summary>
public sealed partial class SimGame
{
    /// <summary>Most entries kept; the older half goes when it fills.</summary>
    public const int AiLogMax = 50_000;

    /// <summary>True while the AI writes its decisions down.</summary>
    public bool AiLogOn { get; set; }

    /// <summary>The decisions written down, oldest first.</summary>
    public List<AiLogEntry> AiLogEntries { get; } = [];

    // When a people last wrote each text: the same decision again within a minute is not written twice.
    private readonly Dictionary<(int Civ, string Text), long> _aiLogLast = [];

    /// <summary>Writes an AI decision down (only while <see cref="AiLogOn"/>).</summary>
    public void AiLog(Civ civ, AiLogKind kind, string text)
    {
        System.ArgumentNullException.ThrowIfNull(civ);
        if (!AiLogOn)
        {
            return;
        }

        if (_aiLogLast.TryGetValue((civ.Id, text), out long last) && Ticks - last < SimContent.TicksPerMinute)
        {
            return;
        }

        _aiLogLast[(civ.Id, text)] = Ticks;
        if (AiLogEntries.Count >= AiLogMax)
        {
            AiLogEntries.RemoveRange(0, AiLogMax / 2);
        }

        AiLogEntries.Add(new AiLogEntry(Ticks, civ.Id, kind, text));
    }
}
