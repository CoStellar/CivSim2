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

        Clock(ref h, state);
        Units(ref h, state);
        EpisodesPart(ref h, state);
        RandomPart(ref h, state);
        Worlds(ref h, state);
        Civilizations(ref h, state);
        Settlements(ref h, state);
        Effects(ref h, state);
        Treaties(ref h, state);
        return h.Value;
    }

    /// <summary>Names of the parts of the state <see cref="Sections"/> hashes one by one, in order (T11).</summary>
    public static readonly string[] SectionNames = ["clock", "units", "episodes", "random", "worlds", "civilizations", "settlements", "effects", "treaties"];

    /// <summary>
    /// The hash of every part of the state on its own (T11), in the order of <see cref="SectionNames"/>: when two runs drift
    /// apart, the parts whose hashes differ say which systems to look at. <see cref="Compute"/> hashes the same parts in one
    /// stream.
    /// </summary>
    public static ulong[] Sections(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var result = new ulong[SectionNames.Length];
        var h = new StateHasher();
        Clock(ref h, state);
        result[0] = h.Value;
        h = new StateHasher();
        Units(ref h, state);
        result[1] = h.Value;
        h = new StateHasher();
        EpisodesPart(ref h, state);
        result[2] = h.Value;
        h = new StateHasher();
        RandomPart(ref h, state);
        result[3] = h.Value;
        h = new StateHasher();
        Worlds(ref h, state);
        result[4] = h.Value;
        h = new StateHasher();
        Civilizations(ref h, state);
        result[5] = h.Value;
        h = new StateHasher();
        Settlements(ref h, state);
        result[6] = h.Value;
        h = new StateHasher();
        Effects(ref h, state);
        result[7] = h.Value;
        h = new StateHasher();
        Treaties(ref h, state);
        result[8] = h.Value;
        return result;
    }

    private static void Clock(ref StateHasher h, GameState state)
    {
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
        h.Add(state.StepMonths);
        h.Add(state.ActionMonth);
        h.Add(state.WorldMonths);
    }

    private static void Units(ref StateHasher h, GameState state)
    {
        h.Add(state.Units.NextId);
        h.Add(state.Units.Count);
        for (int k = 0; k < state.Units.Count; k++)
        {
            Units.Unit u = state.Units[k];
            h.Add(u.Id);
            h.Add((int)u.Kind);
            h.Add(u.Owner.Value);
            h.Add(u.World.Value);
            h.Add(u.Cell);
            h.Add(u.Target);
            h.Add((int)u.Order);
            h.Add(u.Home);
            if (u.Role == CivSim.Core.Units.UnitRole.Workers)
            {
                h.Add(u.Task); // only work groups have a task (stage 6 PR 4a): hashes of games without them stay the same
            }

            foreach (int p in u.People)
            {
                h.Add(p);
            }

            foreach (int p in u.Skilled)
            {
                h.Add(p);
            }

            foreach (int p in u.Trainees)
            {
                h.Add(p);
            }

            foreach (Numerics.Amount a in u.Cargo)
            {
                h.Add(a.Hundredths);
            }

            h.Add(u.Progress);
            h.Add(u.Next);
            h.Add(u.Direction);
            h.Add(u.Trail.Count);
            foreach (int c in u.Trail)
            {
                h.Add(c);
            }

            h.Add(u.Path.Count);
            foreach (int c in u.Path)
            {
                h.Add(c);
            }

            h.Add(u.PathAt);
            h.Add(u.PathGoal);
            h.Add(u.Episode);
            h.Add((int)u.Role);
        }
    }

    private static void EpisodesPart(ref StateHasher h, GameState state)
    {
        h.Add(state.Episodes.NextId);
        h.Add(state.Episodes.Count);
        foreach (Episodes.Episode e in state.Episodes.All)
        {
            h.Add(e.Id);
            h.Add((int)e.Kind);
            h.Add(e.Civ.Value);
            h.Add(e.Other.Value);
            h.Add(e.StartMonth);
            h.Add(e.NextTick);
            h.Add(e.TickMonths);
            h.Add(e.Home);
            h.Add(e.Target);
            h.Add(e.Warriors);
            h.Add(e.Cell);
            h.Add((int)e.Phase);
            h.Add(e.Dead);
            foreach (Numerics.Amount a in e.Loot)
            {
                h.Add(a.Hundredths);
            }

            h.Add(e.GoalTarget);
            h.Add(e.Mustered);
            h.Add(e.ArmsPercent);
            h.Add(e.Morale);
            h.Add(e.MusterUntilTick);
            h.Add(e.HungryMonths);
            h.Add(e.Forays);
            h.Add(e.Foraging ? 1 : 0);
            h.Add(e.LastPrey);
            h.Add(e.Battles);
            h.Add(e.Wins);
            h.Add(e.Killed);
            h.Add(e.ConvoyFrom);
            h.Add(e.ConvoyCell);
            h.Add(e.ConvoyCarriers);
            h.Add(e.Convoys);
            h.Add(e.Army);
            h.Add(e.Convoy);
            h.Add(e.InRangeSince);
            h.Add(e.Fleeing ? 1 : 0);
            Episodes.BattleState fight = e.Fight;
            h.Add(fight.Rounds);
            h.Add(fight.MenA);
            h.Add(fight.MenD);
            h.Add(fight.StrengthA);
            h.Add(fight.StrengthD);
            h.Add(fight.MoraleA);
            h.Add(fight.MoraleD);
            h.Add(fight.LostA);
            h.Add(fight.LostD);
            h.Add(fight.Broken);
            h.Add(fight.Over ? 1 : 0);
            h.Add(e.RoundTick);
            h.Add(e.FightLost);
            h.Add(e.FightKilled);
            h.Add(e.Members.Length);
            for (int m = 0; m < e.Members.Length; m++)
            {
                h.Add(e.Members[m]);
                h.Add(m < e.MemberWarriors.Length ? e.MemberWarriors[m] : 0);
            }

            foreach (Numerics.Amount a in e.Food)
            {
                h.Add(a.Hundredths);
            }

            foreach (Numerics.Amount a in e.ConvoyFood)
            {
                h.Add(a.Hundredths);
            }
        }
    }

    private static void RandomPart(ref StateHasher h, GameState state)
    {
        Span<RandomState> rng = stackalloc RandomState[RandomStreams.Count];
        state.Random.CopyStates(rng);
        foreach (RandomState r in rng)
        {
            h.Add(r.S0);
            h.Add(r.S1);
            h.Add(r.S2);
            h.Add(r.S3);
        }
    }

    private static void Worlds(ref StateHasher h, GameState state)
    {
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
    }

    private static void Civilizations(ref StateHasher h, GameState state)
    {
        h.Add(state.Civilizations.Count);
        foreach (CivilizationState c in state.Civilizations)
        {
            h.Add(c.Id.Value);
            h.Add(c.Name);
            h.Add(c.LanguageSeed);
            h.Add(c.NamesIssued);
            if (c.PlayerLevers != Ai.Lever.None)
            {
                h.Add((int)c.PlayerLevers); // the AI keeps off these (ADR 0027); games without players hash as before
            }

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

            h.Add(c.SeenYear.Length);
            foreach (int seen in c.SeenYear)
            {
                h.Add(seen);
            }

            h.Add(c.Researching);
            h.Add(c.ResearchProgress);
            h.Add(c.LastScience);
            h.Add(c.Traits.Length);
            foreach (int trait in c.Traits)
            {
                h.Add(trait);
            }

            h.Add(c.GoalWeights.Length);
            foreach (int weight in c.GoalWeights)
            {
                h.Add(weight);
            }

            h.Add(c.ExpansionTarget);
            h.Add(c.StrategyReviewMonth);
            h.Add(c.StrategyEra);
            h.Add(c.NextStepMonth);
            h.Add(c.StepStartMonth);
            h.Add(c.StepMonths);
            h.Add(c.Steps);
            h.Add(c.LastPulseTick);
            h.Add((int)c.Alarms);
            h.Add(c.Government);
            h.Add(c.PendingGovernment);
            h.Add(c.PendingGovernmentMonth);
            h.Add(c.LeaderName);
            h.Add(c.LeaderBornMonth);
            h.Add(c.LeaderSinceMonth);
            h.Add(c.LeaderTraits.Length);
            foreach (int t in c.LeaderTraits)
            {
                h.Add(t);
            }

            h.Add(c.LeaderShift.Length);
            foreach (int s in c.LeaderShift)
            {
                h.Add(s);
            }

            h.Add(c.Leaders);
            h.Add(c.CrisisUntilMonth);
            h.Add(c.Capital);
            h.Add(c.Stability);
            h.Add(c.AdminUsed);
            h.Add(c.AnarchyUntilMonth);
            h.Add(c.CapitalLostUntilMonth);
            h.Add(c.ParentCiv);
            h.Add(c.FoundedMonth);
            h.Add(c.RevoltCalmUntilMonth);
            h.Add(c.LanguageMutations.Length);
            foreach (ulong m in c.LanguageMutations)
            {
                h.Add(m);
            }
            h.Add(c.Relations.Count);
            foreach (Diplomacy.Relation r in c.Relations)
            {
                h.Add(r.Other.Value);
                h.Add((int)r.Level);
                h.Add((int)r.Way);
                h.Add(r.FirstMonth);
                h.Add(r.MetMonth);
                h.Add(r.LastContactMonth);
                h.Add(r.Opening);
                h.Add(r.Sympathy);
                h.Add(r.Trust);
                h.Add(r.RumorCell);
                h.Add(r.RumorPeople);
                h.Add(r.RumorFrom);
                h.Add(r.SettledMonth);
                h.Add(r.Memories.Count);
                foreach (Diplomacy.RelationMemory m in r.Memories)
                {
                    h.Add((int)m.Kind);
                    h.Add(m.Sympathy);
                    h.Add(m.Trust);
                    h.Add(m.EndMonth);
                    h.Add(m.FadeMonths);
                }
            }
        }
    }

    private static void Settlements(ref StateHasher h, GameState state)
    {
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
            h.Add(s.Loyalty);
            h.Add(s.FoodGathered);
            h.Add(s.FoodNeeded);
            h.Add(s.FoodEaten);
            h.Add(s.HigherNeedsPerMille);
            h.Add(s.MovedMonth);
            h.Add(s.RecipeId);
            h.Add(s.JourneyTarget);
            h.Add(s.JourneyNext);
            h.Add(s.JourneyStepTicks);
            h.Add(s.JourneyProgress);
            h.Add(s.JourneyPathAt);
            h.Add(s.JourneyPathGoal);
            h.Add(s.JourneyPath.Count);
            foreach (int cell in s.JourneyPath)
            {
                h.Add(cell);
            }
            h.Add(s.ScoutOut ? 1 : 0);
            h.Add(s.ScoutBackMonth);
            h.Add(s.RestUntilMonth);
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

            // Skills (stage 4F): only the skills somebody knows or learns, so a world without skilled people hashes as before.
            ReadOnlySpan<int> skilled = s.SkilledView;
            ReadOnlySpan<int> trainees = s.TraineesView;
            for (int k = 0; k < skilled.Length; k++)
            {
                if (skilled[k] != 0 || trainees[k] != 0)
                {
                    h.Add(k);
                    h.Add(skilled[k]);
                    h.Add(trainees[k]);
                }
            }

            ReadOnlySpan<Numerics.Amount> stock = s.StockView;
            h.Add(stock.Length);
            foreach (Numerics.Amount a in stock)
            {
                h.Add(a.Hundredths);
            }
        }
    }

    private static void Effects(ref StateHasher h, GameState state)
    {
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
    }

    private static void Treaties(ref StateHasher h, GameState state)
    {
        h.Add(state.Treaties.All.Count);
        foreach (Diplomacy.Treaty t in state.Treaties.All)
        {
            h.Add(t.A.Value);
            h.Add(t.B.Value);
            h.Add((int)t.Kind);
            h.Add(t.Initiator.Value);
            h.Add(t.SinceMonth);
            h.Add(t.UntilMonth);
            h.Add(t.GatewayA);
            h.Add(t.GatewayB);
            h.Add(t.LastFlowMonth);
            h.Add(t.LastWorth);
            h.Add(t.TotalWorth);
            h.Add((int)t.Goal);
            h.Add(t.GoalSettlement);
            h.Add(t.Score);
            h.Add(t.DeadA);
            h.Add(t.DeadB);
            h.Add(t.PeopleA);
            h.Add(t.PeopleB);
            h.Add(t.WearinessA);
            h.Add(t.WearinessB);
            AddTerms(ref h, t.Terms);
            h.Add(t.Campaigns);
            h.Add(t.CampaignMonths);
            h.Add(t.LastCampaignTick);
            h.Add(t.ReadyTick);
            h.Add(t.SinceTick);
        }

        h.Add(state.Treaties.Proposals.Count);
        foreach (Diplomacy.TreatyProposal p in state.Treaties.Proposals)
        {
            h.Add(p.From.Value);
            h.Add(p.To.Value);
            h.Add((int)p.Kind);
            h.Add(p.Month);
            h.Add((int)p.Status);
            AddTerms(ref h, p.Terms);
        }
    }

    private static void AddTerms(ref StateHasher h, Diplomacy.PeaceTerms terms)
    {
        h.Add((int)terms.Kind);
        h.Add(terms.Giver.Value);
        h.Add(terms.Settlement);
        h.Add(terms.RansomPercent);
    }
}
