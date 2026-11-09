using System;
using System.Collections.Generic;
using CivSim.Core.State;

namespace CivSim.Core.Diplomacy;

/// <summary>
/// The lived-in settlements on Earth of every civilization, by civilization index, in settlement order (stage 4d).
/// Built once per use; the lists are reused, so building allocates only when the game grows.
/// </summary>
public sealed class SettlementsByCiv
{
    private readonly List<List<int>> _lists = [];

    /// <summary>Indices into <see cref="GameState.Settlements"/> of civilization index <paramref name="civIndex"/>.</summary>
    public IReadOnlyList<int> Of(int civIndex) => _lists[civIndex];

    /// <summary>Sorts the settlements of <paramref name="state"/> by owner.</summary>
    public void Build(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        int n = state.Civilizations.Count;
        while (_lists.Count < n)
        {
            _lists.Add([]);
        }

        for (int c = 0; c < _lists.Count; c++)
        {
            _lists[c].Clear();
        }

        WorldState earth = state.Worlds[0];
        IReadOnlyList<SettlementState> all = state.Settlements;
        for (int i = 0; i < all.Count; i++)
        {
            SettlementState s = all[i];
            int c = s.IsActive && s.World == earth.Id ? state.IndexOfCivilization(s.Owner) : -1;
            if (c >= 0)
            {
                _lists[c].Add(i);
            }
        }
    }

    /// <summary>
    /// Cells between the nearest settlements of civilization indices <paramref name="x"/> and <paramref name="y"/>,
    /// with the settlements (the first pair on ties); int.MaxValue and -1 when either has none.
    /// </summary>
    public int Nearest(GameState state, int x, int y, out int settlementX, out int settlementY)
    {
        ArgumentNullException.ThrowIfNull(state);
        settlementX = -1;
        settlementY = -1;
        int best = int.MaxValue;
        Map.HexGlobe geo = state.Worlds[0].Geometry;
        foreach (int i in _lists[x])
        {
            Map.HexCoord ci = state.Settlements[i].Cell;
            foreach (int j in _lists[y])
            {
                int d = geo.Distance(ci, state.Settlements[j].Cell);
                if (d < best)
                {
                    best = d;
                    settlementX = i;
                    settlementY = j;
                }
            }
        }

        return best;
    }
}
