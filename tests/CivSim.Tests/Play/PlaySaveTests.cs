using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Server;
using Xunit;

namespace CivSim.Tests.Play;

/// <summary>Saving and loading the game (sprint to the demo, 2026-11-20).</summary>
public sealed class PlaySaveTests
{
    [Fact]
    public void ASaveLoadsAsTheSameGame()
    {
        string name = "test save " + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string path = PlaySaves.PathOf(name);
        try
        {
            using (PlayHost host = PlayHost.Start(new PlayOptions(3, 66, 40, 2) { StartPhase = false }))
            {
                IConnection c = host.Connect();
                c.Send(new HelloMessage(ProtocolInfo.Version, "Test"));
                c.Send(new SetSpeedMessage(SimulationSpeed.Max));
                int hut = -1;
                bool built = false;
                long tick = 0;
                SaveGameResultMessage? result = null;
                var clock = Stopwatch.StartNew();
                while (clock.Elapsed < TimeSpan.FromSeconds(60) && result is null)
                {
                    while (c.TryReceive(out IProtocolMessage? m))
                    {
                        switch (m)
                        {
                            case PlayContentMessage pc:
                                hut = pc.Buildings.First(b => b.Key == "gatherers_hut").Id;
                                c.Send(new PlacesRequestMessage(hut));
                                break;
                            case PlacesMessage places when places.Cells.Length >= 2 && !built:
                                c.Send(new PlayCommandMessage(1, PlayCommandKind.Build, hut, places.Cells[0], places.Cells[1], false));
                                built = true;
                                break;
                            case PlayViewMessage pv:
                                tick = pv.Tick;
                                if (built && tick > 600)
                                {
                                    c.Send(new SaveGameRequestMessage(name));
                                }

                                break;
                            case SaveGameResultMessage r:
                                result = r;
                                break;
                        }
                    }

                    System.Threading.Thread.Sleep(5);
                }

                Assert.Null(host.Error);
                Assert.NotNull(result);
                Assert.True(result.Saved, result.Text);
            }

            Assert.True(File.Exists(path));
            SavedGame save = PlaySaves.Read(path);
            Assert.NotEmpty(save.Commands);
            Assert.Contains(PlaySaves.List(), s => s.Name == name);

            using PlayHost loaded = PlayHost.Start(new PlayOptions(0) { LoadPath = path });
            IConnection l = loaded.Connect();
            l.Send(new HelloMessage(ProtocolInfo.Version, "Test"));
            string? news = null;
            var wait = Stopwatch.StartNew();
            while (wait.Elapsed < TimeSpan.FromSeconds(60) && news is null)
            {
                while (l.TryReceive(out IProtocolMessage? m))
                {
                    if (m is PlayViewMessage v && v.News.FirstOrDefault(x => x.StartsWith("Loaded", StringComparison.Ordinal)) is { } n)
                    {
                        news = n;
                        Assert.True(v.Tick >= save.Tick);
                    }
                }

                System.Threading.Thread.Sleep(5);
            }

            Assert.Null(loaded.Error);
            Assert.Equal($"Loaded: {name}.", news); // the same game: the hash matched
        }
        finally
        {
            File.Delete(path);
        }
    }
}
