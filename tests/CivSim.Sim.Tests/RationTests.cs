using System.Linq;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>
/// Rations (Patryk, 2026-11-19): nobody eats by the tick; every minute a settlement feeds all its people at once, residents and
/// units. Short of food the idle go hungry first, then the workers, the units last.
/// </summary>
public sealed class RationTests
{
    private static readonly SimContent Content = SimContent.Load();

    private static (SimGame G, Civ Civ, Settlement Home, Unit Unit) Setup()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];
        for (int i = 0; i < s.Stock.Length; i++)
        {
            s.Stock[i] = 500 * SimContent.One;
        }

        s.People = 60;

        // Two huts give jobs, so the settlement has both workers and people without a job.
        for (int k = 0; k < 2; k++)
        {
            BuildingType hut = Content.Building("gatherers_hut");
            var b = new Building { Id = g.Buildings.Count, Civ = civ.Id, Type = hut.Id, Cell = s.Center, Settlement = s.Id, State = BuildingState.Active };
            g.Buildings.Add(b);
            civ.Buildings.Add(b.Id);
            g.PlaceForTest(b);
        }

        civ.Dirty = true;
        Assert.Null(g.Apply(new TrainCommand(civ.Id, s.Id, Content.Unit("warriors").Id)));
        for (int i = 0; i < Content.Unit("warriors").TrainTicks + 2; i++)
        {
            g.Tick();
        }

        return (g, civ, s, g.Units.Last(u => u.Civ == civ.Id));
    }

    // Runs to just after the next ration of this people.
    private static void ToNextRation(SimGame g, Civ civ)
    {
        int n = g.TicksToRation(civ);
        for (int i = 0; i < n; i++)
        {
            g.Tick();
        }
    }

    // Runs to just after the next ration with no food at all.
    private static void Starve(SimGame g, Civ civ, Settlement s)
    {
        g.Tick();
        int n = g.TicksToRation(civ);
        for (int i = 0; i < n; i++)
        {
            s.Stock[Content.Food] = 0;
            g.Tick();
        }
    }

    [Fact]
    public void EveryoneEatsOnceAMinuteAndTheRateShowsIt()
    {
        (SimGame g, Civ civ, Settlement home, Unit u) = Setup();
        home.Gathering = false;
        civ.Dirty = true;
        ToNextRation(g, civ);
        (int units, int people, long food) = g.RationOf(home);
        Assert.Equal(1, units);
        Assert.Equal(u.People, people);
        Assert.Equal((home.People + u.People) * g.RationPerPerson(civ), food);

        // Between rations nothing is eaten: the store only gains what its makers make. At the ration, everyone eats at once.
        home.Stock[Content.Food] = 100 * SimContent.One; // well under the room, so nothing is cut off at the top
        long made = home.Rate[Content.Food] + home.Eating;
        g.Tick();
        Assert.True(home.Stock[Content.Food] >= 100 * SimContent.One);
        long before = home.Stock[Content.Food];
        int n = g.TicksToRation(civ);
        for (int i = 0; i < n - 1; i++)
        {
            g.Tick();
        }

        Assert.True(home.Stock[Content.Food] >= before, "nobody eats between rations");
        long last = home.Stock[Content.Food];
        g.Tick();
        Assert.InRange(last - home.Stock[Content.Food], food - SimContent.One - (made / 60), food + SimContent.One);
        Assert.Equal(0, u.Hunger);
        Assert.False(home.Hungry);
    }

    [Fact]
    public void ShortOfFoodTheIdleGoHungryFirstThenTheWorkersAndTheUnitsLast()
    {
        (SimGame g, Civ civ, Settlement home, Unit u) = Setup();
        home.Gathering = false;
        civ.Dirty = true;
        ToNextRation(g, civ);
        long each = g.RationPerPerson(civ);
        int workers = home.People - home.Idle;
        Assert.True(home.Idle > 0 && workers > 0, $"{workers} workers, {home.Idle} idle");

        // Food for the unit and the workers, not for the idle.
        home.Stock[Content.Food] = 0;
        int n = g.TicksToRation(civ);
        for (int i = 0; i < n - 1; i++)
        {
            g.Tick();
        }

        home.Stock[Content.Food] = (u.People + workers) * each;
        int idle = home.Idle;
        g.Tick();
        Assert.Equal(0, u.Hunger);
        Assert.Equal(0, home.HungryWorkers);
        Assert.Equal(idle, home.HungryPeople);
        Assert.True(home.Hungry);
        Assert.Equal(1000, home.WorkersFedPermille);
        Assert.Equal(Content.Rations.HungryPercent * 10, home.IdleFedPermille);

        // Nothing at all: everyone is hungry, the unit too (its first miss). For the residents it is the second missed ration in a
        // row, so some of them die; the unit dies off at its own second miss.
        int people = home.People;
        long power = g.Power(u);
        int unitPeople = u.People;
        Starve(g, civ, home);
        Assert.Equal(1, u.Hunger);
        Assert.Equal(unitPeople, u.People);
        Assert.True(g.Power(u) < power);
        Assert.True(home.People < people, "missed rations in a row kill");
        Assert.Equal(people - home.People, home.RationDead);
        Starve(g, civ, home);
        Assert.Equal(2, u.Hunger);
        Assert.True(u.People < unitPeople);

        // One full ration ends it.
        home.Stock[Content.Food] = 400 * SimContent.One;
        g.Tick();
        ToNextRation(g, civ);
        Assert.Equal(0, u.Hunger);
        Assert.False(home.Hungry);
        Assert.Equal(1000, home.WorkersFedPermille);
    }

    [Fact]
    public void HungryUnitsFightAndWalkWeaker()
    {
        (SimGame g, _, _, Unit u) = Setup();
        Assert.Equal(100, g.FedPercent(u));
        u.Hunger = 1;
        Assert.Equal(Content.Rations.HungryPercent, g.FedPercent(u));
        u.Hunger = 2;
        Assert.Equal(Content.Rations.StarvingPercent, g.FedPercent(u));
    }
}
