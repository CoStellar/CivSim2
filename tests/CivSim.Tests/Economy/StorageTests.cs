using System.Collections.Generic;
using System.IO;
using System.Linq;
using CivSim.Core.Content;
using CivSim.Core.Economy;
using CivSim.Core.Hashing;
using CivSim.Core.Numerics;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using CivSim.Data;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Server;
using CivSim.Server.Saves;
using Xunit;

namespace CivSim.Tests.Economy;

public class StorageTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static TurnRunner NewGame(ulong seed)
    {
        GameState state = GameState.CreateNew(GameSettings.Test(seed), Content, out WorldGenReport _);
        var runner = new TurnRunner(state);
        CoreSystems.Register(runner, Content);
        return runner;
    }

    private static int Good(string key) => Content.Good(key).Id;

    [Fact]
    public void EconomyDataLoads()
    {
        Assert.Equal(16, Content.Goods.Count);
        Assert.Equal(6, Content.Recipes.Count);
        Assert.Equal(6, Content.SettlementLevels.Count);
        Assert.Equal(10, Content.Eras.Count);
        Assert.Equal(14, Content.Effects.Count);
        Assert.Equal(600, Content.Eras[0].MonthsPerTurn);
        Assert.Equal(100, Content.Good("grain").SpoilageBasisPointsPerYear);
        Assert.Equal("camp", Content.SettlementLevels[Content.NewTribeLevel].Key);
        Assert.NotEmpty(Content.NewTribeStock);
        for (int i = 0; i < Content.Goods.Count; i++)
        {
            Assert.Equal(i, Content.Goods[i].Id);
        }

        RecipeDefinition tools = Content.Recipes[0];
        Assert.Equal("knap_stone_tools", tools.Key);
        Assert.Equal([new GoodQuantity(Good("wood"), Amount.FromUnits(1)), new GoodQuantity(Good("flint"), Amount.FromUnits(2))], tools.Inputs);
    }

    [Fact]
    public void BrokenEconomyDataReportsEveryError()
    {
        string dir = Path.Combine(Path.GetTempPath(), "civsim2-economy-" + System.Environment.ProcessId);
        Directory.CreateDirectory(dir);
        try
        {
            foreach (string file in Directory.GetFiles(ContentLoader.DefaultDirectory, "*.json"))
            {
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
            }

            Replace(dir, "goods.json", "\"spoilagePerYear\": \"10\"", "\"spoilagePerYear\": \"0.125\"");
            Replace(dir, "recipes.json", "\"flax\": \"3\"", "\"silk\": \"3\"");
            Replace(dir, "settlements.json", "\"level\": \"camp\"", "\"level\": \"tent\"");
            Replace(dir, "effects.json", "\"stacking\": \"sum\"", "\"stacking\": \"often\"");

            ContentException ex = Assert.Throws<ContentException>(() => ContentLoader.Load(dir));
            Assert.Contains(ex.Errors, e => e.Contains("spoilagePerYear", System.StringComparison.Ordinal));
            Assert.Contains(ex.Errors, e => e.Contains("silk", System.StringComparison.Ordinal));
            Assert.Contains(ex.Errors, e => e.Contains("tent", System.StringComparison.Ordinal));
            Assert.Contains(ex.Errors, e => e.Contains("often", System.StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void Replace(string dir, string file, string from, string to)
    {
        string path = Path.Combine(dir, file);
        string text = File.ReadAllText(path);
        Assert.Contains(from, text, System.StringComparison.Ordinal);
        File.WriteAllText(path, text.Replace(from, to, System.StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(100, 600)] // grain: 1% a year for 50 years
    [InlineData(800, 600)] // fish: 8% a year for 50 years
    [InlineData(400, 300)] // bread: 4% a year for 25 years
    public void SpoilageMatchesCompoundInterest(int basisPoints, int months)
    {
        // Floating point only in the test, to check the integer result against the textbook formula.
        double expected = System.Math.Pow(1 - (basisPoints / 10_000.0), months / 12) * Spoilage.One;
        Assert.InRange(Spoilage.KeptShare(basisPoints, months), expected - 1000, expected + 1000);
    }

    [Theory]
    [InlineData(100, 12, 990_000_000)]
    [InlineData(100, 6, 995_000_000)] // half a year loses half of the yearly share
    [InlineData(0, 600, Spoilage.One)]
    [InlineData(10_000, 12, 0)]
    public void SpoilageCompoundsPerYear(int basisPoints, int months, long expected)
    {
        Assert.Equal(expected, Spoilage.KeptShare(basisPoints, months));
    }

    [Fact]
    public void EveryTribeStartsWithACampAndSupplies()
    {
        GameState state = NewGame(3).State;
        Assert.Equal(state.Civilizations.Count, state.Settlements.Count);
        for (int i = 0; i < state.Settlements.Count; i++)
        {
            SettlementState camp = state.Settlements[i];
            Assert.Equal(new SettlementId(i), camp.Id);
            Assert.Equal(state.Civilizations[i].Id, camp.Owner);
            Assert.Equal(state.Civilizations[i].StartCell, camp.Cell);
            Assert.Equal(Content.NewTribeLevel, camp.Level);
            Assert.Equal(Amount.FromUnits(40), camp.StockView[Good("wild_plants")]);
        }
    }

    [Fact]
    public void FoodSpoilsByTheYearlyRateAndToolsDoNot()
    {
        TurnRunner runner = NewGame(4);
        SettlementState camp = runner.State.Settlements[0];
        camp.Stock.Clear();
        camp.Population.Clear(); // nobody gathers or eats: only spoilage changes the stock
        camp.Add(Good("grain"), Amount.FromUnits(100));
        camp.Add(Good("stone_tools"), Amount.FromUnits(5));

        runner.RunTurn();

        Assert.Equal(Amount.FromHundredths(6050), camp.StockView[Good("grain")]); // 100 × 0.99^50, rounded down
        Assert.Equal(Amount.FromUnits(5), camp.StockView[Good("stone_tools")]);
        Assert.Equal(600, runner.State.Month);
    }

    [Fact]
    public void EverythingAboveCapacityIsLostInProportion()
    {
        TurnRunner runner = NewGame(5);
        SettlementState camp = runner.State.Settlements[0];
        camp.Stock.Clear();
        camp.Population.Clear();
        camp.Add(Good("stone"), Amount.FromUnits(300));
        camp.Add(Good("wood"), Amount.FromUnits(100));

        runner.RunTurn();

        Assert.Equal(Content.SettlementLevels[camp.Level].StorageCapacity, camp.TotalStock());
        Assert.Equal(Amount.FromUnits(225), camp.StockView[Good("stone")]);
        Assert.Equal(Amount.FromUnits(75), camp.StockView[Good("wood")]);
    }

    [Fact]
    public void TakingMoreThanStoredChangesNothing()
    {
        SettlementState camp = NewGame(6).State.Settlements[0];
        Amount before = camp.StockView[Good("meat")];
        Assert.False(camp.TryTake(Good("meat"), before + Amount.Epsilon));
        Assert.True(camp.TryTake(Good("meat"), before));
        Assert.Equal(Amount.Zero, camp.StockView[Good("meat")]);
    }

    [Fact]
    public void GamesWithTheEconomyAreDeterministic()
    {
        static ulong Run()
        {
            TurnRunner runner = NewGame(2026);
            for (int i = 0; i < 50; i++)
            {
                runner.RunTurn();
            }

            return GameStateHash.Compute(runner.State);
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void SettlementsAndEffectsSurviveSaveAndLoad()
    {
        TurnRunner runner = NewGame(8);
        runner.RunTurn();
        runner.State.Effects.Add(Content.Effect("damage"), WorldId.Earth, 0, -30, runner.State.Month + 120);
        runner.State.Effects.Add(Content.Effect("extraction_reduction"), WorldId.Earth, 77, -50, runner.State.Month + 60);
        using var ms = new MemoryStream();
        SaveGameSerializer.Save(runner.State, ms);
        ms.Position = 0;
        GameState loaded = SaveGameSerializer.Load(ms);

        Assert.Equal(GameStateHash.Compute(runner.State), GameStateHash.Compute(loaded));
        Assert.Equal(runner.State.Settlements.Count, loaded.Settlements.Count);
        Assert.Equal(runner.State.Effects.Count, loaded.Effects.Count); // ours and any laid by events
        Assert.True(loaded.Effects.Count >= 2);
        Assert.Equal(600, loaded.Month);
    }

    [Fact]
    public void PlayersSeeOnlyTheirOwnWarehouses()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(9), Content, out WorldGenReport _);
        var server = new GameServer(state, Content);
        (InMemoryConnection player, InMemoryConnection playerEnd) = InMemoryConnection.CreatePair();
        (InMemoryConnection observer, InMemoryConnection observerEnd) = InMemoryConnection.CreatePair();
        server.Accept(playerEnd);
        server.Accept(observerEnd);
        player.Send(new HelloMessage(ProtocolInfo.Version, "Player"));
        observer.Send(new HelloMessage(ProtocolInfo.Version, "Viewer", ClientRole.Observer));
        server.Pump();

        GameViewMessage playerView = FirstView(player);
        GameViewMessage observerView = FirstView(observer);

        Assert.Equal(state.Settlements.Count, observerView.Settlements!.Count);
        Assert.All(observerView.Settlements, s => Assert.NotNull(s.Stock));
        SettlementView own = Assert.Single(playerView.Settlements!, s => s.Owner == playerView.ViewerCivId);
        Assert.Equal(Content.Goods.Count, own.Stock!.Length);
        Assert.All(playerView.Settlements!, s => Assert.True(s.Owner == playerView.ViewerCivId || s.Stock is null));
    }

    private static GameViewMessage FirstView(IConnection connection)
    {
        while (connection.TryReceive(out IProtocolMessage? m))
        {
            if (m is GameViewMessage view)
            {
                return view;
            }
        }

        throw new Xunit.Sdk.XunitException("No view received.");
    }
}

public class EffectBookTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    [Fact]
    public void SumEffectsAddUp()
    {
        var book = new EffectBook();
        EffectDefinition mood = Content.Effect("mood_drop");
        book.Add(mood, WorldId.Earth, 3, -5, 1000);
        book.Add(mood, WorldId.Earth, 3, -7, 1000);
        book.Add(mood, WorldId.Earth, 4, -100, 1000);
        Assert.Equal(-12, book.Total(Content.Effects, EffectStat.Mood, EffectTarget.Settlement, WorldId.Earth, 3));
    }

    [Fact]
    public void StrongestEffectWinsAndWeakerOnesAreDropped()
    {
        var book = new EffectBook();
        EffectDefinition reduce = Content.Effect("extraction_reduction");
        Assert.NotNull(book.Add(reduce, WorldId.Earth, 10, -33, 1000));
        Assert.NotNull(book.Add(reduce, WorldId.Earth, 10, -50, 1000));
        Assert.Null(book.Add(reduce, WorldId.Earth, 10, -20, 1000));
        Assert.Equal(1, book.Count);
        Assert.Equal(-50, book.Total(Content.Effects, EffectStat.ExtractionPercent, EffectTarget.Cell, WorldId.Earth, 10));
    }

    [Fact]
    public void ReplaceKeepsTheNewestEffect()
    {
        var replace = new EffectDefinition(99, "test_replace", "Test", EffectTarget.Civilization, EffectStat.Mood, EffectStacking.Replace);
        var book = new EffectBook();
        book.Add(replace, WorldId.Earth, 1, -40, 1000);
        book.Add(replace, WorldId.Earth, 1, 10, 1000);
        Assert.Equal(10, Assert.Single(book.All).Strength);
    }

    [Fact]
    public void EffectsExpireWithGameTime()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(1), Content, out WorldGenReport _);
        var runner = new TurnRunner(state);
        CoreSystems.Register(runner, Content);
        EffectId longer = state.Effects.Add(Content.Effect("damage"), WorldId.Earth, 0, -30, state.Month + (60 * 12))!.Value; // 60 years
        EffectId shorter = state.Effects.Add(Content.Effect("depopulation"), WorldId.Earth, 0, 5, state.Month + (20 * 12))!.Value; // 20 years
        bool Has(EffectId id) => state.Effects.All.Any(e => e.Id == id); // events may lay effects of their own

        runner.RunTurn(); // year 0 -> 50: nothing has expired at the start of the turn
        Assert.True(Has(longer) && Has(shorter));
        runner.RunTurn(); // the 20-year effect is gone at the start of this turn
        Assert.True(Has(longer) && !Has(shorter));
        runner.RunTurn(); // year 100: the 60-year effect is gone too
        Assert.False(Has(longer));
    }

    [Fact]
    public void RemovingByIdWorks()
    {
        var book = new EffectBook();
        EffectId id = book.Add(Content.Effect("damage"), WorldId.Earth, 2, -10, EffectInstance.Permanent)!.Value;
        Assert.True(book.Remove(id));
        Assert.False(book.Remove(id));
        Assert.Equal(1, book.NextId);
    }
}
