using CivSim.Core.Ai;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Economy;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Economy;

/// <summary>Trade network, internal prices, production and their AI (stage 3e).</summary>
public class TradeProductionTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static int Good(string key) => Content.Good(key).Id;

    private static int Recipe(string key) => Content.Recipes.Count == 0 ? -1 : System.Linq.Enumerable.First(Content.Recipes, r => r.Key == key).Id;

    // A fresh world without the starting camps, and land cells along one row to put settlements on.
    private static (GameState State, WorldState World, HexCoord[] Land) Empty(ulong seed)
    {
        GameState state = GameState.CreateNew(GameSettings.Test(seed), Content, out _);
        state.RemoveSettlements(_ => true);
        WorldState world = state.Worlds[0];
        var land = new System.Collections.Generic.List<HexCoord>();
        for (int i = 0; i < world.Geometry.CellCount && land.Count < 400; i++)
        {
            if (!Content.Biomes[world.Map.Biome[i]].IsWater)
            {
                land.Add(world.Geometry.FromIndex(i));
            }
        }

        return (state, world, land.ToArray());
    }

    private static SettlementState Camp(GameState state, CivId civ, HexCoord cell, int producers, int artisans = 0, int warriors = 0)
    {
        SettlementState s = state.FoundSettlement(civ, state.Worlds[0].Id, cell, "Camp", Content.NewTribeLevel, Content.Goods.Count, Content.Strata.Count);
        s.Population[Content.Strata[0].Id] = producers;
        s.Population[1] = artisans;
        s.Population[2] = warriors;
        return s;
    }

    // Two land cells exactly `cells` apart.
    private static (HexCoord A, HexCoord B) Apart(WorldState world, HexCoord[] land, int cells)
    {
        foreach (HexCoord a in land)
        {
            foreach (HexCoord b in land)
            {
                if (world.Geometry.Distance(a, b) == cells)
                {
                    return (a, b);
                }
            }
        }

        throw new Xunit.Sdk.XunitException("no such pair");
    }

    private static void Run(GameState state, params ITurnSystem[] systems)
    {
        var runner = new TurnRunner(state);
        foreach (ITurnSystem s in systems)
        {
            runner.AddSystem(s);
        }

        runner.RunTurn();
    }

    [Fact]
    public void NetworksLinkOneCivilizationsSettlementsInChains()
    {
        (GameState state, WorldState world, HexCoord[] land) = Empty(31);
        CivId a = state.Civilizations[0].Id;
        CivId b = state.Civilizations[1].Id;
        (HexCoord near1, HexCoord near2) = Apart(world, land, 5);
        (HexCoord far1, HexCoord far2) = Apart(world, land, 20);
        Camp(state, a, near1, 50);
        Camp(state, a, near2, 50);
        Camp(state, b, far1, 50);
        Camp(state, b, far2, 50);

        var networks = TradeNetworks.Build(Content, state);
        Assert.Equal(3, networks.Count);           // a's two camps together, b's two apart
        Assert.Equal([0, 1], networks[0]);
        Assert.Equal(90, TradeNetworks.ArrivingPercent(Content, 5));
        Assert.Equal(10, TradeNetworks.ArrivingPercent(Content, 60));
    }

    [Fact]
    public void FoodGoesToTheHungryFirstAndLosesOnTheWay()
    {
        (GameState state, WorldState world, HexCoord[] land) = Empty(32);
        CivId civ = state.Civilizations[0].Id;
        (HexCoord c1, HexCoord c2) = Apart(world, land, 5);
        SettlementState rich = Camp(state, civ, c1, 10);
        SettlementState hungry = Camp(state, civ, c2, 10);
        rich.Stock[Good("meat")] = Amount.FromUnits(2000);
        rich.Stock[Good("hide_clothing")] = Amount.FromUnits(100);

        Run(state, new TradeSystem(Content));

        // The hungry camp gets this turn's food (50 years × 10 people) and a year of reserve; 10% is lost on 5 cells.
        long needed = (10 * 100L * 50) + (10 * 100L);
        Assert.InRange(hungry.StockView[Good("meat")].Hundredths, needed, needed + 100);
        long sent = 2000_00 - rich.StockView[Good("meat")].Hundredths;
        Assert.InRange(sent, needed * 100 / 90, (needed * 100 / 90) + 200);

        // Clothing (a higher need of producers) is shared too, while the giver keeps its own.
        Assert.True(hungry.StockView[Good("hide_clothing")] > Amount.Zero);
        long ownNeed = 10 * 5L * 50 / 100 * 100 / 100;
        Assert.True(rich.StockView[Good("hide_clothing")].Hundredths >= ownNeed);
    }

    [Fact]
    public void ArtisansWorkTheirRecipeAsFarAsTheInputsGo()
    {
        (GameState state, _, HexCoord[] land) = Empty(33);
        SettlementState s = Camp(state, state.Civilizations[0].Id, land[0], 20, artisans: 2);
        s.RecipeId = Recipe("sew_hide_clothing");
        s.Stock[Good("hides")] = Amount.FromUnits(30);

        Run(state, new ProductionSystem(Content));

        // 2 artisans × 50 years = 100 runs possible, but 30 hides make only 15 clothes.
        Assert.Equal(Amount.FromUnits(15), s.StockView[Good("hide_clothing")]);
        Assert.Equal(Amount.Zero, s.StockView[Good("hides")]);
        Assert.Equal(1500, s.RunsMade);
    }

    [Fact]
    public void ProductionNeverEatsThePeoplesFood()
    {
        (GameState state, _, HexCoord[] land) = Empty(34);
        SettlementState s = Camp(state, state.Civilizations[0].Id, land[0], 10, artisans: 5);
        s.RecipeId = Recipe("bake_bread");
        s.Stock[Good("grain")] = Amount.FromUnits(600);   // the people need 15 people × 50 years = 750
        s.Stock[Good("wood")] = Amount.FromUnits(100);

        Run(state, new ProductionSystem(Content));
        Assert.Equal(0, s.RunsMade);
        Assert.Equal(Amount.FromUnits(600), s.StockView[Good("grain")]);
    }

    [Fact]
    public void PricesFollowSupplyAndDemand()
    {
        (GameState state, _, HexCoord[] land) = Empty(35);
        SettlementState s = Camp(state, state.Civilizations[0].Id, land[0], 100, warriors: 20);
        Amount[] none = Market.Prices(Content, state, [0]);
        Assert.Equal(Content.Good("stone_tools").Value.MultiplyPercent(400, Rounding.HalfAwayFromZero), none[Good("stone_tools")]);
        Assert.Equal(Content.Good("stone").Value.MultiplyPercent(25, Rounding.HalfAwayFromZero), none[Good("stone")]); // nobody needs stone

        s.Stock[Good("stone_tools")] = Amount.FromUnits(1000);
        Amount[] plenty = Market.Prices(Content, state, [0]);
        Assert.True(plenty[Good("stone_tools")] < none[Good("stone_tools")]);
    }

    [Fact]
    public void TheAiMakesWhatIsNeededAndWhatItCanFeed()
    {
        (GameState state, _, HexCoord[] land) = Empty(36);
        SettlementState s = Camp(state, state.Civilizations[0].Id, land[0], 80, artisans: 5, warriors: 15);
        s.Stock[Good("hides")] = Amount.FromUnits(50);

        var queue = new CommandQueue();
        new EconomyAi(Content).IssueCommands(state, queue);
        var commands = new System.Collections.Generic.List<CommandEnvelope>();
        queue.DrainSorted(state, 1, commands);
        SetRecipeCommand set = Assert.IsType<SetRecipeCommand>(Assert.Single(commands).Command);
        Assert.Equal(Recipe("sew_hide_clothing"), set.RecipeId); // tools are wanted too, but there is no flint

        Assert.False(new SetRecipeCommand(Content, s.Id, Recipe("fire_pottery")).Validate(state, s.Owner).Success); // Neolithic
    }

    [Fact]
    public void FullWarehousesSendTheirSurplusToNeighbours()
    {
        (GameState state, WorldState world, HexCoord[] land) = Empty(37);
        CivId civ = state.Civilizations[0].Id;
        (HexCoord c1, HexCoord c2) = Apart(world, land, 3);
        SettlementState full = Camp(state, civ, c1, 10);
        SettlementState empty = Camp(state, civ, c2, 10);
        Amount capacity = Content.SettlementLevels[full.Level].StorageCapacity;
        full.Stock[Good("wood")] = capacity + Amount.FromUnits(100);

        Run(state, new StorageSystem(Content));
        Assert.Equal(capacity, full.TotalStock());
        Assert.Equal(Amount.FromUnits(94), empty.StockView[Good("wood")]); // 6% lost over 3 cells
    }

    [Fact]
    public void FlintRunsOutAndForestsGiveWood()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(38), Content, out _);
        WorldMap map = state.Worlds[0].Map;
        int flint = System.Linq.Enumerable.First(Content.Deposits, d => d.Key == "flint").Id;
        Assert.Contains(Content.Deposit(flint).Gather, q => q.GoodId == Good("flint"));
        Assert.Contains(Content.Biome("deciduous_forest").Gather, q => q.GoodId == Good("wood"));

        var runner = new TurnRunner(state);
        CoreSystems.Register(runner, Content);
        int before = Count(map, flint);
        for (int t = 0; t < 10; t++)
        {
            runner.RunTurn();
        }

        long wood = 0;
        foreach (SettlementState s in state.Settlements)
        {
            wood += s.StockView[Good("wood")].Hundredths;
        }

        Assert.True(wood > 0);
        Assert.True(Count(map, flint) <= before);
    }

    private static int Count(WorldMap map, int deposit)
    {
        int n = 0;
        for (int i = 0; i < map.Deposit.Length; i++)
        {
            n += map.Deposit[i] == deposit ? 1 : 0;
        }

        return n;
    }
}
