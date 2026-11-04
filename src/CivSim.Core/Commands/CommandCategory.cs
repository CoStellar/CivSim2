namespace CivSim.Core.Commands;

/// <summary>
/// Resolution group of a command (GDD 11.1, phase 4): diplomacy first, then economy, then movement.
/// The numeric value is the resolution order.
/// </summary>
public enum CommandCategory
{
    /// <summary>Treaties, war declarations, names and other civilization-level decisions.</summary>
    Diplomacy = 0,

    /// <summary>Building, production, trade, research choices.</summary>
    Economy = 1,

    /// <summary>Orders that move units, caravans or settlers.</summary>
    Movement = 2,
}
