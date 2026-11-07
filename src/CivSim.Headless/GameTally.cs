using System;
using System.Collections.Generic;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.State;

namespace CivSim.Headless;

/// <summary>
/// What happened in one game, for batch tests of the AI (GDD 8.7; stage 4g): wars by goal, conquests, civilizations that
/// died out, the furthest era, and hunger the AI did not react to. Fed after every turn; reads the state only.
/// </summary>
public sealed class GameTally
{
    /// <summary>Own steps in a row a settlement may go hungry in one place before it counts as hunger without a reaction.</summary>
    public const int HungerSteps = 3;

    /// <summary>Below this share of its food needs, per mille, a settlement is hungry.</summary>
    public const int HungryBelowPerMille = 500;

    private readonly HashSet<(int, int, long)> _wars = [];
    private readonly Dictionary<int, (int Owner, bool Active)> _owners = [];
    private readonly Dictionary<int, (int Steps, int Cell)> _hunger = [];
    private readonly bool[] _dead = new bool[64];
    private readonly int _villageLevel;
    private long _month;

    /// <summary>
    /// Creates the tally for a game that starts in <paramref name="month"/> with <paramref name="eras"/> eras; settlements of
    /// <paramref name="villageLevel"/> and above are settled (they never move, they send settlers instead).
    /// </summary>
    public GameTally(long month, int eras, int villageLevel)
    {
        _month = month;
        _villageLevel = villageLevel;
        EraYears = new long[Math.Max(1, eras)];
        Array.Fill(EraYears, -1);
        EraYears[0] = month / 12;
    }

    /// <summary>Wars declared, by goal (index = <see cref="WarGoal"/>).</summary>
    public int[] WarsByGoal { get; } = new int[Enum.GetValues<WarGoal>().Length];

    /// <summary>
    /// Lived-in settlements taken from one civilization by another: conquered in a campaign or handed over in a peace (not
    /// secessions, not empty sites taken over).
    /// </summary>
    public int Conquests { get; private set; }

    /// <summary>Civilizations that died out, and the year the first one did (-1 = none).</summary>
    public int DiedOut { get; private set; }

    /// <summary>Year the first civilization died out, or -1.</summary>
    public long FirstDeathYear { get; private set; } = -1;

    /// <summary>Most civilizations alive at once.</summary>
    public int PeakAlive { get; private set; }

    /// <summary>Year some civilization first reached each era (index = era id), or -1.</summary>
    public long[] EraYears { get; }

    /// <summary>Settlement-years spent hungry (fed below half).</summary>
    public long HungryYears => _hungryMonths / 12;

    /// <summary>Times a camp (it can move) stayed hungry in the same place for <see cref="HungerSteps"/> own steps in a row.</summary>
    public int HungerWithoutReaction { get; private set; }

    /// <summary>The same for settled villages and larger (they cannot move; they send settlers).</summary>
    public int VillageHungerSteps { get; private set; }

    /// <summary>Changes of government begun (S2 and coups count; chronicle).</summary>
    public int GovernmentChanges { get; private set; }

    private long _hungryMonths;

    /// <summary>Counts the turn that just ended.</summary>
    public void Count(GameState state, IReadOnlyList<string> chronicle)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(chronicle);
        long months = state.Month - _month;
        _month = state.Month;

        foreach (Treaty t in state.Treaties.All)
        {
            if (t.Kind == TreatyKind.War && _wars.Add((t.A.Value, t.B.Value, t.SinceMonth)))
            {
                WarsByGoal[(int)t.Goal]++;
            }
        }

        int alive = 0;
        foreach (CivilizationState c in state.Civilizations)
        {
            alive += c.IsAlive ? 1 : 0;
            if (c.Era < EraYears.Length && EraYears[c.Era] < 0)
            {
                EraYears[c.Era] = state.Month / 12;
            }

            if (!c.IsAlive && c.Id.Value < _dead.Length && !_dead[c.Id.Value])
            {
                _dead[c.Id.Value] = true;
                DiedOut++;
                FirstDeathYear = FirstDeathYear < 0 ? state.Month / 12 : FirstDeathYear;
            }
        }

        PeakAlive = Math.Max(PeakAlive, alive);
        foreach (SettlementState s in state.Settlements)
        {
            int id = s.Id.Value;
            int owner = s.Owner.Value;
            if (_owners.TryGetValue(id, out var before) && before.Active && before.Owner != owner
                && state.FindCivilization(s.Owner) is { } now && !(now.ParentCiv == before.Owner && now.FoundedMonth >= _month - months))
            {
                Conquests++;
            }

            _owners[id] = (owner, s.IsActive);
            if (!s.IsActive || state.FindCivilization(s.Owner) is not { } civ)
            {
                continue;
            }

            bool hungry = s.TotalPopulation() > 0 && s.FedPerMille < HungryBelowPerMille;
            _hungryMonths += hungry ? months : 0;
            if (civ.StepMonths <= 0)
            {
                continue; // not its own step: the AI has not looked yet
            }

            int cell = state.Worlds[s.World.Value].Geometry.Index(s.Cell);
            (int steps, int at) = _hunger.TryGetValue(id, out var h) ? h : (0, cell);
            if (!hungry)
            {
                _hunger.Remove(id);
                continue;
            }

            steps = at == cell ? steps + 1 : 1;
            _hunger[id] = (steps, cell);
            if (steps == HungerSteps)
            {
                if (s.Level >= _villageLevel)
                {
                    VillageHungerSteps++;
                }
                else
                {
                    HungerWithoutReaction++;
                }
            }
        }

        foreach (string line in chronicle)
        {
            GovernmentChanges += line.Contains(" began to change its government to ", StringComparison.Ordinal)
                || line.StartsWith("A coup in the ", StringComparison.Ordinal) ? 1 : 0;
        }
    }
}
