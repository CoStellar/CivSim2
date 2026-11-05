using System;
using CivSim.Core.Random;
using CivSim.Core.State;

namespace CivSim.Core.Hashing;

/// <summary>Computes the determinism hash of a whole <see cref="GameState"/>.</summary>
public static class GameStateHash
{
    /// <summary>
    /// Hashes every piece of state that affects the future of the game, in id order. Two runs with the same
    /// seed and commands must produce the same value on every platform. Who controls a civilization (AI or a
    /// player) is session data, not game state, so it is not hashed.
    /// </summary>
    public static ulong Compute(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var h = new StateHasher();

        h.Add(state.Settings.Seed);
        h.Add(state.Settings.MapWidth);
        h.Add(state.Settings.MapHeight);
        h.Add(state.Settings.CivilizationCount);
        h.Add((int)state.Settings.WorldType);
        h.Add(state.Settings.WaterPercent);
        h.Add(state.Settings.Planet.AxialTiltTenths);
        h.Add(state.Settings.Planet.EccentricityThousandths);
        h.Add(state.Settings.Planet.LuminosityThousandths);
        h.Add(state.Settings.Planet.DistanceThousandths);
        h.Add(state.Settings.Planet.Co2Microbar);
        h.Add(state.Settings.Planet.BackgroundPressureHundredths);
        h.Add(state.Settings.Planet.VolcanismPercent);
        h.Add(state.Settings.Planet.StarVariabilityPerMille);
        h.Add(state.Settings.Planet.StarCycleYears);
        h.Add(state.Settings.PlanetName);
        h.Add((int)state.Settings.Moisture);
        h.Add((int)state.Settings.Inland);
        h.Add((int)state.Settings.Relief);
        h.Add((int)state.Settings.Resources);
        h.Add(state.Turn);

        Span<RandomState> rng = stackalloc RandomState[RandomStreams.Count];
        state.Random.CopyStates(rng);
        foreach (RandomState r in rng)
        {
            h.Add(r.S0);
            h.Add(r.S1);
            h.Add(r.S2);
            h.Add(r.S3);
        }

        h.Add(state.Worlds.Count);
        foreach (WorldState w in state.Worlds)
        {
            h.Add(w.Id.Value);
            h.Add(w.Name);
            h.Add(w.Width);
            h.Add(w.Height);
            foreach (Map.IMapLayerInfo layer in w.Map.Layers)
            {
                h.Add(layer.Name);
                h.Add(layer.AsBytes());
            }

            h.Add(w.Climate.Year);
            h.Add((int)w.Climate.NorthRegime);
            h.Add((int)w.Climate.SouthRegime);
            h.Add(w.Climate.NorthIce);
            h.Add(w.Climate.SouthIce);
            h.Add(w.Climate.Co2Microbar);
            h.Add(w.Climate.GlobalMeanMillionths);
            h.Add(w.Climate.SeaLevel);
            h.Add(w.Climate.StartLandIce);
        }

        h.Add(state.Civilizations.Count);
        foreach (CivilizationState c in state.Civilizations)
        {
            h.Add(c.Id.Value);
            h.Add(c.Name);
            h.Add(c.IsAlive);
            h.Add(c.StartCell.Col);
            h.Add(c.StartCell.Row);
            h.Add(c.Era);
            h.Add(c.DiedOutMonth);
            h.Add(c.KnownTechnologies.Length);
            foreach (bool known in c.KnownTechnologies)
            {
                h.Add(known);
            }

            h.Add(c.Researching);
            h.Add(c.ResearchProgress);
            h.Add(c.LastScience);
        }

        h.Add(state.Month);
        h.Add(state.NextSettlementId);
        h.Add(state.Settlements.Count);
        foreach (SettlementState s in state.Settlements)
        {
            h.Add(s.Id.Value);
            h.Add(s.Owner.Value);
            h.Add(s.World.Value);
            h.Add(s.Cell.Col);
            h.Add(s.Cell.Row);
            h.Add(s.Name);
            h.Add(s.Level);
            h.Add(s.FoundedTurn);
            h.Add(s.Health);
            h.Add(s.Mood);
            h.Add(s.FoodGathered);
            h.Add(s.FoodNeeded);
            h.Add(s.FoodEaten);
            h.Add(s.HigherNeedsPerMille);
            h.Add(s.MovedMonth);
            h.Add(s.RecipeId);
            h.Add(s.RunsMade);
            h.Add((int)s.Status);
            h.Add(s.AbandonedMonth);
            h.Add(s.ClaimCiv);
            h.Add(s.ClaimUntilMonth);
            ReadOnlySpan<int> people = s.PopulationView;
            h.Add(people.Length);
            foreach (int p in people)
            {
                h.Add(p);
            }

            ReadOnlySpan<Numerics.Amount> stock = s.StockView;
            h.Add(stock.Length);
            foreach (Numerics.Amount a in stock)
            {
                h.Add(a.Hundredths);
            }
        }

        h.Add(state.Effects.NextId);
        h.Add(state.Effects.Count);
        foreach (EffectInstance e in state.Effects.All)
        {
            h.Add(e.Id.Value);
            h.Add(e.DefinitionId);
            h.Add((int)e.Target);
            h.Add(e.World.Value);
            h.Add(e.TargetId);
            h.Add(e.Strength);
            h.Add(e.EndMonth);
        }

        return h.Value;
    }
}
