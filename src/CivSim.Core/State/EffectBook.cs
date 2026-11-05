using System;
using System.Collections.Generic;
using CivSim.Core.Content;

namespace CivSim.Core.State;

/// <summary>
/// One active effect (GDD 10.1): a modifier of type <see cref="DefinitionId"/> on one target, with a strength and
/// an end date in game months. <see cref="TargetId"/> is a cell index (cell targets, on <see cref="World"/>),
/// a settlement id or a civilization id.
/// </summary>
public readonly record struct EffectInstance(
    EffectId Id,
    int DefinitionId,
    EffectTarget Target,
    WorldId World,
    int TargetId,
    int Strength,
    long EndMonth)
{
    /// <summary>End month of an effect that never expires on its own (removed by code, e.g. a building).</summary>
    public const long Permanent = long.MaxValue;
}

/// <summary>
/// All active effects of a game, kept sorted by id (= order of creation), so iteration is deterministic.
/// Events, buildings, technologies and policies add effects here; systems ask for the total of a stat.
/// </summary>
public sealed class EffectBook
{
    private readonly List<EffectInstance> _effects = [];

    /// <summary>Id the next effect will get.</summary>
    public long NextId { get; private set; }

    /// <summary>Active effects in id order.</summary>
    public IReadOnlyList<EffectInstance> All => _effects;

    /// <summary>Number of active effects.</summary>
    public int Count => _effects.Count;

    // Number of active effects per target (index for Total: most cells and settlements have none).
    private readonly Dictionary<long, int> _perTarget = [];

    private static long Key(EffectTarget target, WorldId world, int targetId) => ((long)target << 48) | ((long)world.Value << 32) | (uint)targetId;

    private void Track(EffectInstance e, int change)
    {
        long key = Key(e.Target, e.World, e.TargetId);
        int n = (_perTarget.TryGetValue(key, out int c) ? c : 0) + change;
        if (n <= 0)
        {
            _perTarget.Remove(key);
        }
        else
        {
            _perTarget[key] = n;
        }
    }

    /// <summary>
    /// Adds an effect following the stacking rule of its type: <see cref="EffectStacking.Replace"/> removes older
    /// effects of the same type on the same target; <see cref="EffectStacking.Strongest"/> keeps only the one with the
    /// largest absolute strength (a weaker newcomer is dropped and null is returned).
    /// </summary>
    public EffectId? Add(EffectDefinition definition, WorldId world, int targetId, int strength, long endMonth)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Stacking != EffectStacking.Sum)
        {
            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                EffectInstance e = _effects[i];
                if (e.DefinitionId != definition.Id || e.TargetId != targetId || e.World != world)
                {
                    continue;
                }

                if (definition.Stacking == EffectStacking.Strongest && Math.Abs(e.Strength) > Math.Abs(strength))
                {
                    return null;
                }

                Track(_effects[i], -1);
                _effects.RemoveAt(i);
            }
        }

        var effect = new EffectInstance(new EffectId(NextId++), definition.Id, definition.Target, world, targetId, strength, endMonth);
        _effects.Add(effect);
        Track(effect, 1);
        return effect.Id;
    }

    /// <summary>Removes an effect by id. Returns false when it is not active.</summary>
    public bool Remove(EffectId id)
    {
        int i = IndexOf(id);
        if (i < 0)
        {
            return false;
        }

        Track(_effects[i], -1);
        _effects.RemoveAt(i);
        return true;
    }

    /// <summary>Removes every effect that ended at or before <paramref name="month"/>. Returns how many were removed.</summary>
    public int RemoveExpired(long month)
    {
        int write = 0;
        for (int read = 0; read < _effects.Count; read++)
        {
            if (_effects[read].EndMonth > month)
            {
                _effects[write++] = _effects[read];
            }
            else
            {
                Track(_effects[read], -1);
            }
        }

        int removed = _effects.Count - write;
        if (removed > 0)
        {
            _effects.RemoveRange(write, removed);
        }

        return removed;
    }

    /// <summary>
    /// Sum of the strengths of all effects that change <paramref name="stat"/> on the target. Effects of different
    /// types add up; stacking rules already apply within one type when effects are added.
    /// </summary>
    public int Total(IReadOnlyList<EffectDefinition> definitions, EffectStat stat, EffectTarget target, WorldId world, int targetId)
    {
        if (!_perTarget.ContainsKey(Key(target, world, targetId)))
        {
            return 0;
        }

        int total = 0;
        foreach (EffectInstance e in _effects)
        {
            if (e.Target == target && e.TargetId == targetId && e.World == world && definitions[e.DefinitionId].Stat == stat)
            {
                total += e.Strength;
            }
        }

        return total;
    }

    /// <summary>Adds an effect while loading a save. Ids must arrive in ascending order.</summary>
    public void AddForLoad(EffectInstance effect, long nextId)
    {
        if (_effects.Count > 0 && _effects[^1].Id.Value >= effect.Id.Value)
        {
            throw new InvalidOperationException("Effects must be added in ascending id order.");
        }

        _effects.Add(effect);
        Track(effect, 1);
        NextId = nextId;
    }

    /// <summary>Sets the next id while loading a save with no active effects.</summary>
    public void SetNextIdForLoad(long nextId) => NextId = nextId;

    private int IndexOf(EffectId id)
    {
        int lo = 0;
        int hi = _effects.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int cmp = _effects[mid].Id.Value.CompareTo(id.Value);
            if (cmp == 0)
            {
                return mid;
            }

            if (cmp < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return -1;
    }
}
