using System.Threading;

namespace CivSim.Core.Simulation;

/// <summary>
/// Hands the latest finished snapshot from the simulation thread to readers. The writer replaces the
/// published reference atomically after the snapshot is complete, so a reader always sees a whole turn
/// and never waits for the simulation.
/// </summary>
public sealed class SnapshotBuffer<T>
    where T : class
{
    private T? _latest;
    private long _version;

    /// <summary>The most recently published snapshot, or null before the first turn.</summary>
    public T? Latest => Volatile.Read(ref _latest);

    /// <summary>Increases by one with every publish; lets readers skip work when nothing changed.</summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>Publishes a finished snapshot. Only the simulation thread calls this.</summary>
    public void Publish(T snapshot)
    {
        Volatile.Write(ref _latest, snapshot);
        Interlocked.Increment(ref _version);
    }
}
