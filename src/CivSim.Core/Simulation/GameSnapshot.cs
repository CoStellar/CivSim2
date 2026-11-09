using System.Collections.Immutable;
using System.Runtime.InteropServices;
using CivSim.Core.State;

namespace CivSim.Core.Simulation;

/// <summary>
/// Immutable picture of the game after a turn. Readers on other threads (server views, viewer) use only this,
/// never the live <see cref="GameState"/>.
/// </summary>
public sealed record GameSnapshot(long Turn, ImmutableArray<WorldSnapshot> Worlds, ImmutableArray<CivilizationSnapshot> Civilizations)
{
    /// <summary>Game time in months since the start.</summary>
    public long Month { get; init; }

    /// <summary>Months of action (ADR 0025): the clock of claims, truces and other political timers.</summary>
    public long ActionMonth { get; init; }

    /// <summary>True when seasons are played turn by turn on the first world.</summary>
    public bool LiveSeasons { get; init; }

    /// <summary>All settlements in id order.</summary>
    public ImmutableArray<SettlementSnapshot> Settlements { get; init; } = [];

    /// <summary>All units walking, oldest first (stage 6 PR 3a).</summary>
    public ImmutableArray<UnitSnapshot> Units { get; init; } = [];

    /// <summary>Copies the public parts of <paramref name="state"/>.</summary>
    public static GameSnapshot From(GameState state)
    {
        var worlds = ImmutableArray.CreateBuilder<WorldSnapshot>(state.Worlds.Count);
        var changed = new System.Collections.Generic.List<int>();
        foreach (WorldState w in state.Worlds)
        {
            changed.Clear();
            w.Map.Dirty.CopyTo(changed);
            worlds.Add(new WorldSnapshot(w.Id, w.Name, w.Width, w.Height, [.. changed]));
        }

        var civs = ImmutableArray.CreateBuilder<CivilizationSnapshot>(state.Civilizations.Count);
        foreach (CivilizationState c in state.Civilizations)
        {
            civs.Add(new CivilizationSnapshot(c.Id, c.Name, c.Controller, c.IsAlive) { DiedOutMonth = c.DiedOutMonth, Capital = c.Capital });
        }

        var settlements = ImmutableArray.CreateBuilder<SettlementSnapshot>(state.Settlements.Count);
        foreach (SettlementState st in state.Settlements)
        {
            System.ReadOnlySpan<Numerics.Amount> view = st.StockView;
            var stock = new long[view.Length];
            for (int g = 0; g < stock.Length; g++)
            {
                stock[g] = view[g].Hundredths;
            }

            settlements.Add(new SettlementSnapshot(st.Id, st.Owner, st.World, st.Cell.Col, st.Cell.Row, st.Name, st.Level, ImmutableCollectionsMarshal.AsImmutableArray(stock))
            {
                Population = ImmutableCollectionsMarshal.AsImmutableArray(st.PopulationView.ToArray()),
                Health = st.Health,
                Mood = st.Mood,
                Loyalty = st.Loyalty,
                FoodGathered = st.FoodGathered,
                FoodNeeded = st.FoodNeeded,
                FoodEaten = st.FoodEaten,
                Status = st.Status,
                AbandonedMonth = st.AbandonedMonth,
                ClaimCiv = st.ClaimCiv,
                ClaimUntilMonth = st.ClaimUntilMonth,
            });
        }

        var units = ImmutableArray.CreateBuilder<UnitSnapshot>(state.Units.Count);
        for (int k = 0; k < state.Units.Count; k++)
        {
            Units.Unit u = state.Units[k];
            Map.HexGlobe geo = state.Worlds[u.World.Value].Geometry;
            Map.HexCoord at = geo.FromIndex(u.Cell);
            Map.HexCoord next = u.Next >= 0 ? geo.FromIndex(u.Next) : at;
            units.Add(new UnitSnapshot(
                u.Id, u.Kind, u.Role, u.Owner, u.World, at.Col, at.Row, next.Col, next.Row,
                u.Next >= 0 && u.StepCost > 0 ? (int)System.Math.Min(1000, u.Progress * 1000L / u.StepCost) : 0,
                u.TotalPeople(), u.Order, u.Cell));
        }

        return new GameSnapshot(state.Turn, worlds.MoveToImmutable(), civs.MoveToImmutable())
        {
            Units = units.MoveToImmutable(),
            Month = state.Month,
            ActionMonth = state.ActionMonth,
            LiveSeasons = state.Worlds[0].Map.LiveSeasons,
            Settlements = settlements.MoveToImmutable(),
        };
    }
}

/// <summary>
/// Immutable copy of a world's public data. <paramref name="ChangedChunks"/> lists the map chunks that changed
/// in the turn (ascending ids), so readers can redraw or resend only those.
/// </summary>
public sealed record WorldSnapshot(WorldId Id, string Name, int Width, int Height, ImmutableArray<int> ChangedChunks);

/// <summary>Immutable copy of a civilization's public data.</summary>
public sealed record CivilizationSnapshot(CivId Id, string Name, PlayerId Controller, bool IsAlive)
{
    /// <summary>Game month in which the civilization died out (when not alive).</summary>
    public long DiedOutMonth { get; init; }

    /// <summary>Settlement id of the capital, or -1 when none (GDD 7.3).</summary>
    public int Capital { get; init; } = -1;
}

/// <summary>Immutable copy of a settlement. <paramref name="Stock"/> holds hundredths per good id.</summary>
public sealed record SettlementSnapshot(SettlementId Id, CivId Owner, WorldId World, int Col, int Row, string Name, int Level, ImmutableArray<long> Stock)
{
    /// <summary>People per stratum id.</summary>
    public ImmutableArray<int> Population { get; init; } = [];

    /// <summary>Health 0–100.</summary>
    public int Health { get; init; }

    /// <summary>Mood 0–100.</summary>
    public int Mood { get; init; }

    /// <summary>Loyalty to the owner 0–100 (GDD 7.3).</summary>
    public int Loyalty { get; init; }

    /// <summary>Food gathered in the last turn (hundredths).</summary>
    public long FoodGathered { get; init; }

    /// <summary>Food needed in the last turn (hundredths).</summary>
    public long FoodNeeded { get; init; }

    /// <summary>Food eaten in the last turn (hundredths).</summary>
    public long FoodEaten { get; init; }

    /// <summary>Active, abandoned or ruins.</summary>
    public SettlementStatus Status { get; init; }

    /// <summary>Month of abandonment (when not active).</summary>
    public long AbandonedMonth { get; init; }

    /// <summary>Civilization with a claim, or <see cref="SettlementState.NoClaim"/>.</summary>
    public int ClaimCiv { get; init; } = SettlementState.NoClaim;

    /// <summary>Last month of the claim.</summary>
    public long ClaimUntilMonth { get; init; }
}

/// <summary>
/// Immutable copy of a unit (stage 6 PR 3a): where it stands, the cell it walks to next and how far it got there
/// (<paramref name="ProgressPermille"/>, 0–1000), so a client can draw it between the two cells.
/// </summary>
public sealed record UnitSnapshot(
    long Id, int Kind, Units.UnitRole Role, CivId Owner, WorldId World, int Col, int Row, int NextCol, int NextRow, int ProgressPermille,
    int People, Units.UnitOrder Order, int Cell);
