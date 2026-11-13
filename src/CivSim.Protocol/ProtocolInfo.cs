namespace CivSim.Protocol;

/// <summary>Version information shared by the client and the server.</summary>
public static class ProtocolInfo
{
    /// <summary>Protocol version. Bump on every incompatible message change and add a compatibility test.</summary>
    public const int Version = 19; // 17: area tasks (stage 6 PR 4a); 18: levers of the helmsman, the AI's decisions for a player, choosing a civilization (PR P1); 19: player commands for levers 1, 2, 4, 5, research and proposals in the player's view
}
