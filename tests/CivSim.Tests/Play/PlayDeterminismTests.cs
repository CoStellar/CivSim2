using System;
using System.IO;
using System.Linq;
using CivSim.Core.Map;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Server;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Tests.Play;

/// <summary>Saves and views without the server thread (tests 2026-11-20: A11, A13, A14, A21).</summary>
public sealed class PlayDeterminismTests
{
    // A13: a game with the start phase saves and loads as the same game (the pick has tick 0 and comes in its turn).
    [Fact]
    public void ASaveWithTheStartPhaseLoadsAsTheSameGame()
    {
        string name = "test pick " + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string path = PlaySaves.PathOf(name);
        try
        {
            ulong saved;
            using (PlayHost host = PlayHost.Create(new PlayOptions(5, 66, 40, 4) { StartPhase = true }))
            {
                host.AutosaveOn = false;
                IConnection c = host.Connect();
                c.Send(new HelloMessage(ProtocolInfo.Version, "Test"));
                SimGame g = host.Game;
                int guard = 0;
                host.RunTicks(1, viewEvery: 0);
                while (g.Picking && g.PickOrder[g.PickIndex] != 0 && guard++ < 10_000)
                {
                    host.RunTicks(1, viewEvery: 0);
                }

                Assert.True(g.Picking, "the player must still be choosing");
                host.RunTicks(37, viewEvery: 0); // the player takes a few seconds to choose
                HexCoord at = g.Geo.FromIndex(FreeStart(g)); // not AiStart: that draws from the AI's random stream
                c.Send(new PlayCommandMessage(1, PlayCommandKind.PickStart, 0, at.Col, at.Row, false));
                while (g.Picking && guard++ < 10_000)
                {
                    host.RunTicks(1, viewEvery: 0);
                }

                Assert.Equal(g.Geo.Index(at), g.StartCell[0]);
                host.RunTicks(1200, viewEvery: 7);
                c.Send(new SaveGameRequestMessage(name));
                host.RunTicks(0);
                saved = host.StateHash;
            }

            SavedGame save = PlaySaves.Read(path);
            Assert.Contains(save.Commands, x => x.Kind == PlayCommandKind.PickStart && x.Tick == 0);
            using PlayHost loaded = PlayHost.Create(new PlayOptions(0) { LoadPath = path });
            loaded.AutosaveOn = false;
            loaded.RunTicks(0);
            Assert.Equal(saved, loaded.StateHash);
            Assert.Equal(save.Hash, loaded.StateHash);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // A14/B1: building views (of the player and of every AI people) changes nothing in the game.
    [Trait("Category", "Slow")]
    [Fact]
    public void ViewsDoNotChangeTheGame()
    {
        ulong Play(bool views)
        {
            using PlayHost host = PlayHost.Create(new PlayOptions(3, 66, 40, 4) { StartPhase = false });
            host.AutosaveOn = false;
            IConnection c = host.Connect();
            c.Send(new HelloMessage(ProtocolInfo.Version, "Test"));
            host.RunTicks(0);
            host.Game.Civs[0].IsAi = true; // the AI plays for the player too, so its people gets commands and dirt
            for (int t = 0; t < 6000; t++)
            {
                host.RunTicks(1, viewEvery: views ? 1 : 0);
                if (views && t % 3 == 0)
                {
                    host.ViewEveryPeople();
                }

                Assert.Equal(host.StateHash, Hash(host, () => { if (views) { host.ViewEveryPeople(); } }));
            }

            return host.StateHash;
        }

        Assert.Equal(Play(views: false), Play(views: true));
    }

    private static int FreeStart(SimGame g)
    {
        int best = -1;
        for (int i = 0; i < g.Geo.CellCount; i++)
        {
            if (g.CannotStart(i) is null && (best < 0 || g.Sites!.Seen[i] > g.Sites.Seen[best]))
            {
                best = i;
            }
        }

        return best;
    }

    private static ulong Hash(PlayHost host, Action view)
    {
        view();
        return host.StateHash;
    }
}
