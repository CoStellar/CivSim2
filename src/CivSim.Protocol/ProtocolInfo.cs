namespace CivSim.Protocol;

/// <summary>Version information shared by the client and the server.</summary>
public static class ProtocolInfo
{
    /// <summary>Protocol version. Bump on every incompatible message change and add a compatibility test.</summary>
    public const int Version = 17; // 17: StartTaskDto and the task of work groups in UnitView (stage 6 PR 4a)
}
