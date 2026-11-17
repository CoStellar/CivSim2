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
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10) && !built)
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
                            hut = b.Key == "hut" ? b.Id : hut;
                        }

                        break;
                    case MapChunkMessage:
                        chunks++;
                        break;
                    case PlayViewMessage pv:
                        view = pv;
                        built |= pv.Buildings.Count > 0;
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
        Assert.Contains(view!.Settlements, s => s.Civ == view.Civ);
        Assert.True(built, "the hut was not built");
    }
}
