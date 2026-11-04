using System.IO;
using CivSim.Core.Commands;
using CivSim.Core.Hashing;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Server.Saves;
using CivSim.Tests.Simulation;
using Xunit;

namespace CivSim.Tests.Server;

public class SaveGameTests
{
    private static GameState RoundTrip(GameState state)
    {
        using var ms = new MemoryStream();
        SaveGameSerializer.Save(state, ms);
        ms.Position = 0;
        return SaveGameSerializer.Load(ms);
    }

    [Fact]
    public void SaveLoadKeepsTheHash()
    {
        TurnRunner runner = DeterminismTests.CreateRunner(31);
        runner.Commands.Enqueue(PlayerId.Ai, new CivId(2), new RenameCivilizationCommand("Saved"));
        for (int i = 0; i < 50; i++)
        {
            runner.RunTurn();
        }

        GameState loaded = RoundTrip(runner.State);

        Assert.Equal(GameStateHash.Compute(runner.State), GameStateHash.Compute(loaded));
        Assert.Equal(runner.State.FindCivilization(new CivId(2))!.Name, loaded.FindCivilization(new CivId(2))!.Name);
    }

    [Fact]
    public void LoadedGameContinuesExactlyLikeTheOriginal()
    {
        TurnRunner original = DeterminismTests.CreateRunner(8);
        for (int i = 0; i < 30; i++)
        {
            original.RunTurn();
        }

        var resumed = new TurnRunner(RoundTrip(original.State));
        resumed.AddSystem(new RandomConsumer());
        resumed.AddCommandSource(new RandomRenamer());
        for (int i = 0; i < 70; i++)
        {
            original.RunTurn();
            resumed.RunTurn();
        }

        Assert.Equal(GameStateHash.Compute(original.State), GameStateHash.Compute(resumed.State));
    }

    [Fact]
    public void FileSaveWorks()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(3));
        string path = Path.Combine(Path.GetTempPath(), $"civsim2-test-{System.Environment.ProcessId}.civsave");
        try
        {
            SaveGameSerializer.SaveToFile(state, path);
            Assert.Equal(GameStateHash.Compute(state), GameStateHash.Compute(SaveGameSerializer.LoadFromFile(path)));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ForeignOrNewerFilesAreRejected()
    {
        using var foreign = new MemoryStream("NOPE\u0001\0\0\0"u8.ToArray());
        Assert.Throws<InvalidDataException>(() => SaveGameSerializer.Load(foreign));

        using var newer = new MemoryStream();
        SaveGameSerializer.Save(GameState.CreateNew(GameSettings.Test(1)), newer);
        byte[] bytes = newer.ToArray();
        bytes[4] = 99;
        Assert.Throws<InvalidDataException>(() => SaveGameSerializer.Load(new MemoryStream(bytes)));
    }
}
