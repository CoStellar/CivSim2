using System.Linq;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Episodes;
using CivSim.Core.Map;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.Units;
using Xunit;

namespace CivSim.Tests.Episodes;

/// <summary>Armies and carriers as units (stage 6 PR 3b): flight, rest, carriers, attack orders, range, kinds, scouts.</summary>
public class WarUnitsTests
{
    private static readonly GameContent Content = TestContent.Load();

    private static int Warriors => Content.Strata.Single(s => s.Key == "warriors").Id;

    private static int Producers => Content.Strata[0].Id;

    [Fact]
    public void ABeatenArmyFleesAndNobodyTurnsIt()
    {
        (GameState state, HexCoord start) = ArmyTests.Strip(101, 8);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState home = ArmyTests.Village(state, a, start, 0, 120, 30, 200);
        SettlementState target = ArmyTests.Village(state, b, start, 4, 300, 60, 200);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        new LaunchCampaignCommand(Content, home.Id, target.Id, 20).ApplyWithChronicle(state, a.Id);
        Episode e = state.Episodes.All[0];
        TurnRunner runner = ArmyTests.Runner(state);
        for (int t = 0; t < 100 && e.Battles == 0; t++)
        {
            runner.RunTurn();
        }

        Assert.Equal(1, e.Battles);
        Assert.Equal(0, e.Wins); // 20 against 60 warriors and a share of 300 others
        Assert.True(e.Fleeing);
        runner.RunTurn();
        Unit army = state.Units.Find(e.Army)!.Value;
        Assert.Equal(UnitOrder.Flee, army.Order);
        Assert.False(new ReturnUnitCommand(Content, army.Id).Validate(state, a.Id).Success, "already fleeing home");
        Assert.False(new RecallCampaignCommand(e.Id).Validate(state, a.Id).Success);
    }

    [Fact]
    public void WarriorsRestAfterTheyComeHome()
    {
        (GameState state, HexCoord start) = ArmyTests.Strip(102, 8);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState home = ArmyTests.Village(state, a, start, 0, 120, 30, 200);
        SettlementState target = ArmyTests.Village(state, b, start, 4, 20, 4);
        SettlementState other = ArmyTests.Village(state, b, start, 7, 20, 4);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        new LaunchCampaignCommand(Content, home.Id, target.Id, 20).ApplyWithChronicle(state, a.Id);
        TurnRunner runner = ArmyTests.Runner(state);
        ArmyTests.RunOut(runner, state);

        // Home again: its warriors rest restMonths months of action before they can go again.
        Assert.Equal(state.ActionMonth + Content.War.RestMonths, home.RestUntilMonth, 1.0);
        System.Array.Fill(a.SeenYear, (int)(state.Month / 12) + 1); // they still know where the enemy lives
        var again = new LaunchCampaignCommand(Content, home.Id, other.Id, 10);
        Assert.Contains("rest", again.Validate(state, a.Id).Reason, System.StringComparison.Ordinal);
        for (int t = 0; t <= Content.Time.ActionTicks(Content.War.RestMonths); t++)
        {
            runner.RunTurn();
        }

        System.Array.Fill(a.SeenYear, (int)(state.Month / 12) + 1);
        Assert.True(again.Validate(state, a.Id).Success, again.Validate(state, a.Id).Reason);
    }

    [Fact]
    public void CarriersWalkAsAUnitAndGoHome()
    {
        // As in a game, carriers walk faster than the army (here 1 tick a cell against 3) and catch up with it.
        UnitRules rules = Content.Units;
        UnitKindDefinition[] kinds = [.. rules.Kinds.Select(k => k.Role != UnitRole.Army ? k : new UnitKindDefinition
        {
            Id = k.Id, Key = k.Key, Name = k.Name, Role = k.Role, TicksPerCell = 3, SightCells = k.SightCells, RangeCells = k.RangeCells,
        })];
        GameContent content = Content.WithUnits(rules with { Kinds = kinds });
        (GameState state, HexCoord start) = ArmyTests.Strip(103, 14);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState depot = ArmyTests.Village(state, a, start, 0, 100, 2, 500);
        SettlementState home = ArmyTests.Village(state, a, start, 2, 120, 30);
        SettlementState target = ArmyTests.Village(state, b, start, 14, 20, 4);
        Treaties.DeclareWar(content, state, a.Id, b.Id, WarGoal.Loot, -1);
        new LaunchCampaignCommand(content, home.Id, target.Id, 20).ApplyWithChronicle(state, a.Id);
        Episode e = state.Episodes.All[0];
        var runner = new TurnRunner(state);
        runner.AddSystem(new UnitSystem(content));
        runner.AddSystem(new CampaignSystem(content));
        runner.AddSystem(new WarSystem(content));
        runner.AddSystem(new CalendarSystem(content));
        WorldClock.Begin(content, state);
        runner.RunTurn();
        runner.RunTurn();

        Unit carriers = Assert.Single(state.Units.All, u => u.Role == UnitRole.Carriers);
        Assert.Equal(e.Id, carriers.Episode);
        Assert.Equal(e.ConvoyCarriers, carriers.TotalPeople());
        Assert.Equal(depot.Id.Value, carriers.Home);

        // They reach the army, hand the food over and walk home, where they join their settlement again.
        for (int t = 0; t < 300 && (state.Episodes.Count > 0 || state.Units.All.Any(u => u.Role == UnitRole.Carriers)); t++)
        {
            runner.RunTurn();
        }

        Assert.True(e.Convoys > 0, "the carriers reached the army");
        Assert.DoesNotContain(state.Units.All, u => u.Role == UnitRole.Carriers);
        Assert.Equal(100, depot.Population[Producers]);
    }

    [Fact]
    public void AnArmyOnTheMarchCanBeSentAgainstAnotherSettlement()
    {
        (GameState state, HexCoord start) = ArmyTests.Strip(104, 12);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState home = ArmyTests.Village(state, a, start, 0, 120, 30, 200);
        SettlementState far = ArmyTests.Village(state, b, start, 11, 20, 4);
        SettlementState near = ArmyTests.Village(state, b, start, 5, 20, 4);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        new LaunchCampaignCommand(Content, home.Id, far.Id, 20).ApplyWithChronicle(state, a.Id);
        Episode e = state.Episodes.All[0];
        TurnRunner runner = ArmyTests.Runner(state);
        ArmyTests.FirstStep(runner);
        Unit army = state.Units.Find(e.Army)!.Value;

        Assert.False(new MoveUnitCommand(Content, army.Id, start).Validate(state, a.Id).Success, "armies follow their campaign");
        Assert.False(new AttackCommand(Content, army.Id, home.Id).Validate(state, a.Id).Success, "not our own settlement");
        var attack = new AttackCommand(Content, army.Id, near.Id);
        Assert.True(attack.Validate(state, a.Id).Success, attack.Validate(state, a.Id).Reason);
        attack.Apply(state, a.Id);
        for (int t = 0; t < 100 && e.Battles == 0; t++)
        {
            runner.RunTurn();
        }

        Assert.Equal(near.Id.Value, e.Target);
        Assert.Equal(1, e.Battles);
        Assert.Equal(4, far.Population[Warriors]); // the far village was left alone
    }

    [Fact]
    public void AnArmyFightsFromAsFarAsItsKindReaches()
    {
        // A kind of later eras that fights from 3 cells (bows, guns): the army stands 3 cells before its target.
        UnitRules rules = Content.Units;
        UnitKindDefinition[] kinds = [.. rules.Kinds.Select(k => k.Role != UnitRole.Army ? k : new UnitKindDefinition
        {
            Id = k.Id, Key = k.Key, Name = k.Name, Role = k.Role, TicksPerCell = k.TicksPerCell, SightCells = k.SightCells, RangeCells = 3,
        })];
        GameContent ranged = Content.WithUnits(rules with { Kinds = kinds });
        (GameState state, HexCoord start) = ArmyTests.Strip(105, 10);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState home = ArmyTests.Village(state, a, start, 0, 120, 30, 200);
        SettlementState target = ArmyTests.Village(state, b, start, 8, 20, 4);
        Treaties.DeclareWar(ranged, state, a.Id, b.Id, WarGoal.Loot, -1);
        new LaunchCampaignCommand(ranged, home.Id, target.Id, 20).ApplyWithChronicle(state, a.Id);
        Episode e = state.Episodes.All[0];
        var runner = new TurnRunner(state);
        runner.AddSystem(new UnitSystem(ranged));
        runner.AddSystem(new CampaignSystem(ranged));
        runner.AddSystem(new WarSystem(ranged));
        runner.AddSystem(new CalendarSystem(ranged));
        WorldClock.Begin(ranged, state);
        HexGlobe geo = state.Worlds[0].Geometry;
        int closest = int.MaxValue;
        for (int t = 0; t < 100 && e.Battles == 0; t++)
        {
            runner.RunTurn();
            closest = System.Math.Min(closest, geo.Distance(geo.FromIndex(e.Cell), target.Cell));
        }

        Assert.Equal(1, e.Battles);
        Assert.Equal(3, closest);
    }

    [Fact]
    public void ACampaignMarchesAsTheBestArmyKindItKnows()
    {
        // A second army kind that needs a technology: known, the campaign marches as it; unknown, as the first.
        UnitRules rules = Content.Units;
        int tech = Content.Technology("fire").Id;
        UnitKindDefinition better = new()
        {
            Id = rules.Kinds.Count, Key = "spearmen", Name = "Spearmen", Role = UnitRole.Army, RequiresTechnology = tech, TicksPerCell = 1, SightCells = 1,
        };
        GameContent content = Content.WithUnits(rules with { Kinds = [.. rules.Kinds, better] });
        CivilizationState civ = new GameStateBuilderProbe(content).Civ;
        Assert.Equal(content.Units.First(UnitRole.Army).Id, Campaigns.ArmyKind(content, civ).Id);
        Core.Research.Technologies.EnsureSize(content, civ);
        civ.KnownTechnologies[tech] = true;
        Assert.Equal(better.Id, Campaigns.ArmyKind(content, civ).Id);
    }

    [Fact]
    public void ScoutsTakeHuntersAlongAndBringThemBack()
    {
        (GameState state, HexCoord start) = ArmyTests.Strip(106, 4);
        CivilizationState a = state.Civilizations[0];
        SettlementState home = ArmyTests.Village(state, a, start, 0, 60, 5, 100);
        int hunter = Content.Skills.First(s => s.Key == "hunter").Id;
        home.Skilled[hunter] = 2;
        home.ScoutOut = false;
        int people = home.TotalPopulation();
        var runner = new TurnRunner(state);
        runner.AddSystem(new UnitSystem(Content));
        WorldClock.Begin(Content, state);
        runner.RunTurn();

        Unit scouts = Assert.Single(state.Units.All, u => u.Role == UnitRole.Scouts);
        UnitKindDefinition kind = Content.Units.Of(scouts.Kind);
        Assert.Equal(kind.People, scouts.TotalPeople());
        Assert.Equal(2, scouts.Skilled[hunter]); // hunters first
        Assert.Equal(0, home.Skilled[hunter]);
        Assert.Equal(people - kind.People, home.TotalPopulation());
        for (int t = 0; t < 200 && !scouts.Gone && state.Units.Find(scouts.Id) is not null; t++)
        {
            runner.RunTurn();
        }

        Assert.Null(state.Units.Find(scouts.Id));
        Assert.Equal(people, home.TotalPopulation());
        Assert.Equal(2, home.Skilled[hunter]);
    }

    // A civilization of a new game of the content, to ask which kind its armies march as.
    private sealed class GameStateBuilderProbe(GameContent content)
    {
        public CivilizationState Civ { get; } = GameState.CreateNew(GameSettings.Test(107), content, out _).Civilizations[0];
    }
}
