using System;
using System.Runtime.InteropServices;

namespace CivSim.Core.Map;

/// <summary>
/// One value per cell (or per edge), stored in a flat array (structure of arrays). Changing a value through
/// <see cref="Set"/> marks its chunk dirty. Bulk writers (world generation, loading) use <see cref="Raw"/>
/// and then call <see cref="WorldMap.MarkAllDirty"/>.
/// </summary>
public sealed class MapLayer<T>
    where T : unmanaged, IEquatable<T>
{
    private readonly T[] _values;
    private readonly WorldMap _owner;
    private readonly int _valuesPerCell;

    internal MapLayer(string name, WorldMap owner, int valuesPerCell, T initial)
    {
        Name = name;
        _owner = owner;
        _valuesPerCell = valuesPerCell;
        _values = new T[owner.Geometry.CellCount * valuesPerCell];
        if (!initial.Equals(default))
        {
            Array.Fill(_values, initial);
        }
    }

    /// <summary>Layer name (used in saves and debugging).</summary>
    public string Name { get; }

    /// <summary>Number of values (cells, or cells × 3 for edge layers).</summary>
    public int Length => _values.Length;

    /// <summary>Value at a flat index.</summary>
    public T this[int index] => _values[index];

    /// <summary>All values, read-only.</summary>
    public ReadOnlySpan<T> Values => _values;

    /// <summary>All values, writable, without dirty tracking. Only for bulk writers.</summary>
    public Span<T> Raw => _values;

    /// <summary>The layer as bytes (little-endian on every supported platform), for hashing and saving.</summary>
    public ReadOnlySpan<byte> AsBytes() => MemoryMarshal.AsBytes(_values.AsSpan());

    /// <summary>Changes one value and marks its chunk dirty when the value really changed.</summary>
    public void Set(int index, T value)
    {
        if (_values[index].Equals(value))
        {
            return;
        }

        _values[index] = value;
        _owner.MarkCellDirty(index / _valuesPerCell);
    }

    /// <summary>Overwrites the layer from saved bytes.</summary>
    internal void LoadBytes(ReadOnlySpan<byte> bytes)
    {
        Span<byte> target = MemoryMarshal.AsBytes(_values.AsSpan());
        if (bytes.Length != target.Length)
        {
            throw new ArgumentException($"Layer '{Name}' expects {target.Length} bytes, got {bytes.Length}.");
        }

        bytes.CopyTo(target);
    }
}
