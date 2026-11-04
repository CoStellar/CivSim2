using System;

namespace CivSim.Core.State;

/// <summary>Identifies a civilization. Ids are dense, start at 0 and are never reused within a game.</summary>
public readonly record struct CivId(int Value) : IComparable<CivId>
{
    /// <inheritdoc />
    public int CompareTo(CivId other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public override string ToString() => $"civ{Value}";
}

/// <summary>Identifies a player (human or AI controller) connected to the server.</summary>
public readonly record struct PlayerId(int Value) : IComparable<PlayerId>
{
    /// <summary>The built-in AI controller.</summary>
    public static PlayerId Ai => new(0);

    /// <inheritdoc />
    public int CompareTo(PlayerId other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public override string ToString() => $"player{Value}";
}

/// <summary>Identifies a world (planet). World 0 is Earth.</summary>
public readonly record struct WorldId(int Value) : IComparable<WorldId>
{
    /// <summary>The home world.</summary>
    public static WorldId Earth => new(0);

    /// <inheritdoc />
    public int CompareTo(WorldId other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public override string ToString() => $"world{Value}";
}
