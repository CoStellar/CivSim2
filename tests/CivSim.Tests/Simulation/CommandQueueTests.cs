using System.Collections.Generic;
using CivSim.Core.Commands;
using CivSim.Core.State;
using Xunit;

namespace CivSim.Tests.Simulation;

public class CommandQueueTests
{
    private static readonly List<string> Log = [];

    [Fact]
    public void SortsByCategoryThenCivThenArrival()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(1)); // 4 civs
        var queue = new CommandQueue();
        queue.Enqueue(PlayerId.Ai, new CivId(1), new LoggingCommand(CommandCategory.Movement, "move-c1", Log));
        queue.Enqueue(PlayerId.Ai, new CivId(2), new LoggingCommand(CommandCategory.Economy, "eco-c2-a", Log));
        queue.Enqueue(PlayerId.Ai, new CivId(0), new LoggingCommand(CommandCategory.Diplomacy, "dip-c0", Log));
        queue.Enqueue(PlayerId.Ai, new CivId(2), new LoggingCommand(CommandCategory.Economy, "eco-c2-b", Log));
        queue.Enqueue(PlayerId.Ai, new CivId(0), new LoggingCommand(CommandCategory.Economy, "eco-c0", Log));

        var sorted = new List<CommandEnvelope>();
        queue.DrainSorted(state, turn: 0, sorted);

        Assert.Equal(0, queue.Count);
        Assert.Equal(["dip-c0", "eco-c0", "eco-c2-a", "eco-c2-b", "move-c1"], sorted.ConvertAll(e => ((LoggingCommand)e.Command).Tag));
    }

    [Fact]
    public void CivilizationOrderRotatesEveryTurn()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(1)); // civs 0..3
        Assert.Equal(0, CommandQueue.CivOrder(state, new CivId(0), 0));
        Assert.Equal(0, CommandQueue.CivOrder(state, new CivId(1), 1));
        Assert.Equal(3, CommandQueue.CivOrder(state, new CivId(0), 1));
        Assert.Equal(0, CommandQueue.CivOrder(state, new CivId(0), 4));
        Assert.Equal(int.MaxValue, CommandQueue.CivOrder(state, new CivId(99), 0));
    }

    [Fact]
    public void EveryCivilizationGoesFirstEquallyOften()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(1));
        var firsts = new int[4];
        for (long turn = 0; turn < 400; turn++)
        {
            for (int c = 0; c < 4; c++)
            {
                if (CommandQueue.CivOrder(state, new CivId(c), turn) == 0)
                {
                    firsts[c]++;
                }
            }
        }

        Assert.Equal([100, 100, 100, 100], firsts);
    }
}
