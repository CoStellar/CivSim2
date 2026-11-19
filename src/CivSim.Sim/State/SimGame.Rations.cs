using System;
using System.Collections.Generic;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>
/// Rations (Patryk, 2026-11-19): units cost nothing while they live, as in RTS games, and nobody eats by the tick. Instead
/// every minute each settlement feeds all its people at once: its residents and the units that come from it. Short of food,
/// its units are fed first, then its workers, then the people without a job: the idle go hungry first. Hungry people work,
/// gather, fight and walk weaker; missing the next ration too makes them starving, and at that and every further missed
/// ration some of them die. One full ration ends it.
/// </summary>
public sealed partial class SimGame
{
    private readonly List<Unit> _toFeed = [];
    private readonly List<int> _rationOrder = [];

    /// <summary>Ticks until a people's next ration.</summary>
    public int TicksToRation(Civ civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        int every = Content.Rations.EveryTicks;
        return every - (int)((Ticks + (civ.Id * 17)) % every);
    }

    /// <summary>Food one person eats at a ration (hundredths), after the event of the people.</summary>
    public long RationPerPerson(Civ civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return Content.FoodPerPersonPerMinute * Content.Rations.EveryTicks / SimContent.TicksPerMinute * EatPercent(civ) / 100;
    }

    /// <summary>Units a settlement feeds, their people, and the food of its whole next ration (residents, units, advisor).</summary>
    public (int Units, int People, long Food) RationOf(Settlement s)
    {
        ArgumentNullException.ThrowIfNull(s);
        int units = 0;
        int people = 0;
        foreach (int uid in Civs[s.Civ].Units)
        {
            Unit u = Units[uid];
            if (u.State != UnitState.Gone && FedBy(u)?.Id == s.Id)
            {
                units++;
                people += u.People;
            }
        }

        return (units, people, s.Eating * Content.Rations.EveryTicks / SimContent.TicksPerMinute);
    }

    /// <summary>Strength and speed of a unit in percent: lower when it is hungry or starving.</summary>
    public int FedPercent(Unit u)
    {
        ArgumentNullException.ThrowIfNull(u);
        return HungerPercent(u.Hunger);
    }

    // How much a people eats in percent (events such as a feast or a famine change it).
    private int EatPercent(Civ civ) =>
        civ.Event >= 0 ? 100 + ((Content.Events[civ.Event].EatPercent - 100) * Harm(civ, Content.Events[civ.Event].Key) / 100) : 100;

    private int HungerPercent(int missed) => missed switch
    {
        <= 0 => 100,
        1 => Content.Rations.HungryPercent,
        _ => Content.Rations.StarvingPercent,
    };

    // People of the units a settlement feeds (for its eating rate).
    private int UnitPeopleOf(Civ civ, Settlement s)
    {
        int people = 0;
        foreach (int uid in civ.Units)
        {
            Unit u = Units[uid];
            if (u.State != UnitState.Gone && FedBy(u)?.Id == s.Id)
            {
                people += u.People;
            }
        }

        return people;
    }

    // The settlement that feeds a unit: its home while we hold it, else our nearest one (null = none left).
    private Settlement? FedBy(Unit u)
    {
        if (u.Home >= 0 && u.Home < Settlements.Count && Settlements[u.Home].Civ == u.Civ && !Settlements[u.Home].Abandoned)
        {
            return Settlements[u.Home];
        }

        return NearestOwn(Civs[u.Civ], u.Cell);
    }

    private void RationStep(Civ civ)
    {
        if (civ.Settlements.Count == 0)
        {
            return;
        }

        if (civ.Dirty)
        {
            Recompute(civ); // the right number of people and jobs
        }

        long each = RationPerPerson(civ);
        RationRules r = Content.Rations;
        _rationOrder.Clear();
        _rationOrder.AddRange(civ.Settlements);
        int capital = civ.Settlements[0];
        foreach (int sid in _rationOrder)
        {
            Settlement s = Settlements[sid];
            if (s.Abandoned)
            {
                continue;
            }

            if (sid == capital && civ.Advisor is not null)
            {
                s.Stock[Content.Food] = Math.Max(0, s.Stock[Content.Food] - (Content.AdvisorUpkeep * r.EveryTicks / SimContent.TicksPerMinute));
            }

            // Its units first (fighting ones first, then the oldest): they are the last to go hungry.
            _toFeed.Clear();
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (int uid in civ.Units)
                {
                    Unit u = Units[uid];
                    if (u.State != UnitState.Gone && u.Fighting == (pass == 0) && FedBy(u)?.Id == s.Id)
                    {
                        _toFeed.Add(u);
                    }
                }
            }

            s.RationShort = 0;
            s.RationDead = 0;
            foreach (Unit u in _toFeed)
            {
                u.Home = s.Id;
                long need = u.People * each;
                if (Content.Units[u.Kind].Founds && u.Cargo is { } load && load[Content.Food] > 0)
                {
                    // Settlers eat their provisions first (what is left goes to the new camp).
                    long own = Math.Min(load[Content.Food], need);
                    load[Content.Food] -= own;
                    need -= own;
                }

                if (s.Stock[Content.Food] >= need)
                {
                    s.Stock[Content.Food] -= need;
                    u.Hunger = 0;
                    continue;
                }

                s.RationShort++;
                u.Hunger = (byte)Math.Min(2, u.Hunger + 1);
                if (u.Hunger == 2)
                {
                    // The second missed ration in a row, and every one after it.
                    int dead = Math.Max(1, u.People * r.StarveLosePercent / 100);
                    u.People -= dead;
                    u.Injured = Math.Min(u.Injured, Math.Max(0, u.People));
                    s.RationDead += dead;
                    if (u.People <= 0)
                    {
                        Gone(u);
                        News.Add((civ.Id, $"Our {Content.Units[u.Kind].Name.ToLowerInvariant()} starved to death: {s.Name} had no food for them."));
                    }
                }
            }

            // Then the residents, a whole person at a time: workers before the people without a job.
            int workers = Math.Clamp(s.People - s.Idle, 0, s.People);
            int idle = s.People - workers;
            long fed = each > 0 ? s.Stock[Content.Food] / each : s.People;
            int fedWorkers = (int)Math.Min(workers, fed);
            int fedIdle = (int)Math.Min(idle, fed - fedWorkers);
            s.Stock[Content.Food] -= (fedWorkers + fedIdle) * each;
            s.HungryWorkers = workers - fedWorkers;
            s.HungryPeople = s.HungryWorkers + (idle - fedIdle);
            s.HungerStreak = s.HungryPeople > 0 ? s.HungerStreak + 1 : 0;
            int pct = HungerPercent(s.HungerStreak);
            s.WorkersFedPermille = workers == 0 ? 1000 : 1000 - (s.HungryWorkers * (100 - pct) * 10 / workers);
            s.IdleFedPermille = idle == 0 ? 1000 : 1000 - ((idle - fedIdle) * (100 - pct) * 10 / idle);
            if (s.HungerStreak >= 2 && s.People > 1)
            {
                int died = Math.Min(s.People - 1, Math.Max(1, s.HungryPeople * r.StarveLosePercent / 100));
                s.People -= died;
                s.RationDead += died;
            }

            bool was = s.Hungry;
            s.Hungry = s.HungryPeople > 0;
            civ.Dirty = true;
            if (s.RationDead > 0)
            {
                News.Add((civ.Id, $"{s.Name} missed another ration: {s.RationDead} people starved to death. Bring food or build food makers."));
            }
            else if (s.Hungry && !was)
            {
                News.Add((civ.Id, $"{s.Name} could not feed {s.HungryPeople} people" + (s.RationShort > 0 ? $" and {s.RationShort} units" : "") +
                    ": they are hungry and work weaker. Another missed ration kills some."));
            }
            else if (s.RationShort > 0)
            {
                News.Add((civ.Id, $"{s.Name} could not feed {s.RationShort} of its units: they are hungry, fight and walk weaker."));
            }
        }
    }
}
