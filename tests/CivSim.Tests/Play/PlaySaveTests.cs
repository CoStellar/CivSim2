using System;
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
            // Driven without the server's thread and clock (RunTicks), so a busy machine cannot make it run out of time.
            using (PlayHost host = PlayHost.Create(new PlayOptions(3, 66, 40, 2) { StartPhase = false }))
            {
                host.AutosaveOn = false;
                IConnection c = host.Connect();
                c.Send(new HelloMessage(ProtocolInfo.Version, "Test"));
                c.Send(new SetSpeedMessage(SimulationSpeed.Max));
                int hut = -1;
                bool built = false;
                long tick = 0;
                SaveGameResultMessage? result = null;
                for (int step = 0; step < 4000 && result is null; step++)
                {
                    host.RunTicks(1);
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
                }

                Assert.Null(host.Error);
                Assert.NotNull(result);
                Assert.True(result.Saved, result.Text);
            }

            Assert.True(File.Exists(path));
            SavedGame save = PlaySaves.Read(path);
            Assert.NotEmpty(save.Commands);
            Assert.Contains(PlaySaves.List(), s => s.Name == name);

            using PlayHost loaded = PlayHost.Create(new PlayOptions(0) { LoadPath = path });
            loaded.AutosaveOn = false;
            IConnection l = loaded.Connect();
            l.Send(new HelloMessage(ProtocolInfo.Version, "Test"));
            string? news = null;
            for (int step = 0; step < 50 && news is null; step++)
            {
                loaded.RunTicks(1);
                while (l.TryReceive(out IProtocolMessage? m))
                {
                    if (m is PlayViewMessage v && v.News.FirstOrDefault(x => x.StartsWith("Loaded", StringComparison.Ordinal)) is { } n)
                    {
                        news = n;
                        Assert.True(v.Tick >= save.Tick);
                    }
                }
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
