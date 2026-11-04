using System;

namespace CivSim.Core.Random;

/// <summary>
/// Holds one independent <see cref="DeterministicRandom"/> per <see cref="RandomStreamId"/>,
/// all derived from a single game seed.
/// </summary>
public sealed class RandomStreams
{
    /// <summary>Number of streams (highest <see cref="RandomStreamId"/> + 1).</summary>
    public const int Count = (int)RandomStreamId.Leaders + 1;

    private readonly DeterministicRandom[] _streams = new DeterministicRandom[Count];

    /// <summary>Creates all streams from <paramref name="gameSeed"/>.</summary>
    public RandomStreams(ulong gameSeed)
    {
        GameSeed = gameSeed;
        for (int i = 0; i < Count; i++)
        {
            _streams[i] = new DeterministicRandom(StreamSeed(gameSeed, (ulong)i));
        }
    }

    /// <summary>The seed the game was started with.</summary>
    public ulong GameSeed { get; }

    /// <summary>Returns the generator for <paramref name="id"/>. The instance lives as long as this object.</summary>
    public DeterministicRandom this[RandomStreamId id] => _streams[(int)id];

    /// <summary>
    /// Creates a generator that depends only on the game seed, stream, entity and turn, not on how many
    /// numbers other code drew. Use it for work that runs in parallel (e.g. each civilization's AI).
    /// </summary>
    public DeterministicRandom Derive(RandomStreamId id, ulong entityId, long turn)
    {
        ulong seed = SplitMix64.Combine(SplitMix64.Combine(StreamSeed(GameSeed, (ulong)id), entityId), (ulong)turn);
        return new DeterministicRandom(seed);
    }

    /// <summary>Copies every stream's state into <paramref name="destination"/> (length <see cref="Count"/>).</summary>
    public void CopyStates(Span<RandomState> destination)
    {
        if (destination.Length < Count)
        {
            throw new ArgumentException($"Need room for {Count} states.", nameof(destination));
        }

        for (int i = 0; i < Count; i++)
        {
            destination[i] = _streams[i].State;
        }
    }

    /// <summary>Restores every stream's state from <paramref name="states"/> (length <see cref="Count"/>).</summary>
    public void RestoreStates(ReadOnlySpan<RandomState> states)
    {
        if (states.Length != Count)
        {
            throw new ArgumentException($"Expected {Count} states, got {states.Length}.", nameof(states));
        }

        for (int i = 0; i < Count; i++)
        {
            _streams[i].SetState(states[i]);
        }
    }

    private static ulong StreamSeed(ulong gameSeed, ulong streamIndex)
    {
        return SplitMix64.Combine(gameSeed, streamIndex + 1);
    }
}
