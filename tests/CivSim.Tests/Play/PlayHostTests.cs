using System.Linq;
using System;
using System.Diagnostics;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Server;
using Xunit;

namespace CivSim.Tests.Play;

public sealed class PlayHostTests
{
    [Fact]
    public void APlayerGetsTheMapItsViewAndCanBuild()
    {
        using PlayHost host = PlayHost.Start(new PlayOptions(3, 66, 40, 2) { StartPhase = false });
        IConnection c = host.Connect();
        c.Send(new HelloMessage(ProtocolInfo.Version, "Test"));
        c.Send(new SetSpeedMessage(SimulationSpeed.Max));
        PlayContentMessage? content = null;
        PlayViewMessage? view = null;
        int chunks = 0;
        int hut = -1;
        bool asked = false;
        bool built = false;
        bool ours = false;
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10) && !(built && view is not null))
        {
            while (c.TryReceive(out IProtocolMessage? m))
            {
                switch (m)
                {
                    case PlayContentMessage pc:
                        content = pc;
                        hut = pc.Buildings.Count - 1;
                        foreach (PlayBuildingInfo b in pc.Buildings)
                        {
                            hut = b.Key == "gatherers_hut" ? b.Id : hut; // known from the start (huts need Dwellings)
                        }

                        break;
                    case MapChunkMessage:
                        chunks++;
                        break;
                    case PlayViewMessage pv:
                        view = pv;
                        break;
                    case PlayObjectsMessage objects:
                        built |= objects.Buildings.Count > 0;
                        ours |= view is not null && objects.Settlements.Any(s => s.Civ == view.Civ);
                        ours |= objects.Settlements.Count > 0;
                        break;
                    case PlacesMessage places when places.Cells.Length >= 2:
                        c.Send(new PlayCommandMessage(1, PlayCommandKind.Build, hut, places.Cells[0], places.Cells[1], false));
                        break;
                    case CommandRejectedMessage rejected:
                        Assert.Fail(rejected.Reason);
                        break;
                }
            }

            if (content is not null && !asked)
            {
                c.Send(new PlacesRequestMessage(hut));
                asked = true;
            }

            System.Threading.Thread.Sleep(5);
        }

        Assert.Null(host.Error);
        Assert.NotNull(content);
        Assert.True(chunks > 0);
        Assert.NotNull(view);
        Assert.True(ours, "our settlement came with the objects");
        Assert.True(built, "the hut was not built");
    }
}

public sealed class PlayPauseTests
{
    [Fact]
    public void WithTwoPlayersAPauseIsProtectedAndCounted()
    {
        using PlayHost host = PlayHost.Start(new PlayOptions(3, 66, 40, 2) { StartPhase = false });
        IConnection a = host.Connect();
        IConnection b = host.Connect();
        a.Send(new HelloMessage(ProtocolInfo.Version, "A"));
        b.Send(new HelloMessage(ProtocolInfo.Version, "B"));
        int civA = Welcome(a);
        int civB = Welcome(b);
        Assert.NotEqual(civA, civB);
        a.Send(new SetSpeedMessage(SimulationSpeed.X1));
        System.Threading.Thread.Sleep(50);
        a.Send(new SetSpeedMessage(SimulationSpeed.Paused));
        System.Threading.Thread.Sleep(100);
        b.Send(new SetSpeedMessage(SimulationSpeed.X1));
        Assert.Contains("protected", Rejection(b), StringComparison.Ordinal);
    }

    private static int Welcome(IConnection c)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            while (c.TryReceive(out IProtocolMessage? m))
            {
                if (m is WelcomeMessage w)
                {
                    return w.CivId;
                }
            }

            System.Threading.Thread.Sleep(5);
        }

        return -1;
    }

    private static string Rejection(IConnection c)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            while (c.TryReceive(out IProtocolMessage? m))
            {
                if (m is CommandRejectedMessage r)
                {
                    return r.Reason;
                }
            }

            System.Threading.Thread.Sleep(5);
        }

        return "";
    }
}
