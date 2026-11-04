namespace CivSim.Core.Commands;

/// <summary>Outcome of validating or applying a command.</summary>
public readonly record struct CommandResult(bool Success, string? Reason)
{
    /// <summary>The command is valid / was applied.</summary>
    public static CommandResult Ok => new(true, null);

    /// <summary>The command is rejected with an English, player-readable reason.</summary>
    public static CommandResult Fail(string reason) => new(false, reason);
}
