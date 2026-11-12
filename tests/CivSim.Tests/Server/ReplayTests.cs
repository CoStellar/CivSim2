using System;
using System.IO;
using System.Linq;
using CivSim.Core.Content;
using CivSim.Core.Hashing;
using CivSim.Core.State;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Server;
using CivSim.Server.Replays;
using CivSim.Server.Saves;
using Xunit;

namespace CivSim.Tests.Server;

/// <summary>Replays (T11): a game is its seed and its commands; hashes per part of the state show where two runs drift apart.</summary>
public class ReplayTests
{
    private static readonly GameContent Content = TestContent.Load();

    // A recorded game with a player (renames, then leaves) and an observer who renames a settlement.
    private static GameServer PlayRecorded(ulong seed, int ticks, int checkpointTicks)
    {
        GameServer server = GameServer.CreateNew(GameSettings.Test(seed), Content);
        server.StartRecording(newGame: true, checkpointTicks);
        (InMemoryConnection player, InMemoryConnection playerEnd) = InMemoryConnection.CreatePair();
        (InMemoryConnection observer, InMemoryConnection observerEnd) = InMemoryConnection.CreatePair();
        server.Accept(playerEnd);
        server.Accept(observerEnd);
        player.Send(new HelloMessage(ProtocolInfo.Version, "Patryk"));
        observer.Send(new HelloMessage(ProtocolInfo.Version, "Watcher", ClientRole.Observer));
        server.Pump();
        for (int t = 0; t < ticks; t++)
        {
            if (t == 3)
            {
                player.Send(new SubmitCommandMessage(0, 1, new RenameCivilizationDto("Rome")));
            }

            if (t == 7)
            {
                observer.Send(new SubmitCommandMessage(0, 2, new RenameSettlementDto(server.State.Settlements[^1].Id.Value, "Ostia")));
            }

            if (t == ticks / 2)
            {
                player.Close(); // the AI takes the civilization back
            }

            if (player.IsOpen)
            {
                player.Send(new EndTurnMessage(server.State.Turn));
                server.Pump();
            }
            else
            {
                server.Pump();
                server.AdvanceTurn(sendViews: false);
            }

            Drain(player);
            Drain(observer);
        }

        return server;
    }

    private static void Drain(IConnection connection)
    {
        while (connection.TryReceive(out _))
        {
        }
    }

    [Fact]
    public void AReplayOfSeedAndCommandsGivesTheSameGame()
    {
        GameServer server = PlayRecorded(seed: 31, ticks: 120, checkpointTicks: 10);
        Assert.Equal("Rome", server.State.Civilizations[0].Name);
        ReplayRecorder recorder = server.Replay!;
        Assert.Contains(recorder.Events, e => e.Kind == ReplayEventKind.Command);
        Assert.Contains(recorder.Events, e => e.Kind == ReplayEventKind.Rename);
        Assert.Equal(2, recorder.Events.Count(e => e.Kind == ReplayEventKind.Control)); // taken and given back

        // Through a file and back.
        using var file = new MemoryStream();
        ReplaySerializer.Write(recorder.ToData(), file);
        file.Position = 0;
        ReplayData data = ReplaySerializer.Read(file);
        Assert.Null(data.StartSave); // a new game: seed and settings are enough
        Assert.True(file.Length < 4096, $"the replay takes {file.Length} bytes");

        ReplayResult result = ReplayPlayer.Play(data, Content);
        Assert.True(result.Matches, result.Divergence?.ToString());
        Assert.Equal(13, result.Checked); // ticks 0, 10, ... 120
        Assert.Equal(GameStateHash.Compute(server.State), GameStateHash.Compute(result.State));
        Assert.Equal("Rome", result.State.Civilizations[0].Name);
        Assert.Contains(result.State.Settlements, s => s.Name == "Ostia");
    }

    [Fact]
    public void AMissingEventShowsWhereTheGamesDriftApart()
    {
        GameServer server = PlayRecorded(seed: 32, ticks: 60, checkpointTicks: 10);
        ReplayData data = server.Replay!.ToData();
        ReplayEvent rename = data.Events.First(e => e.Kind == ReplayEventKind.Command);
        ReplayData broken = data with { Events = [.. data.Events.Where(e => e != rename)] };

        ReplayResult result = ReplayPlayer.Play(broken, Content);
        ReplayDivergence d = Assert.IsType<ReplayDivergence>(result.Divergence);
        Assert.Equal(10, d.Tick); // the first checkpoint after the rename at tick 3
        Assert.Equal(["civilizations"], d.Sections); // only the name differs
    }

    [Fact]
    public void ALoadedGameIsReplayedFromItsSave()
    {
        GameServer first = GameServer.CreateNew(GameSettings.Test(33), Content);
        for (int t = 0; t < 30; t++)
        {
            first.AdvanceTurn(sendViews: false);
        }

        using var save = new MemoryStream();
        SaveGameSerializer.Save(first.State, save);
        save.Position = 0;
        var server = new GameServer(SaveGameSerializer.Load(save), Content);
        server.StartRecording(newGame: false, checkpointTicks: 10);
        for (int t = 0; t < 40; t++)
        {
            server.AdvanceTurn(sendViews: false);
        }

        ReplayData data = server.Replay!.ToData();
        Assert.NotNull(data.StartSave);
        Assert.Equal((30L, 70L), (data.StartTick, data.EndTick));
        ReplayResult result = ReplayPlayer.Play(data, Content);
        Assert.True(result.Matches, result.Divergence?.ToString());
        Assert.Equal(GameStateHash.Compute(server.State), GameStateHash.Compute(result.State));
    }

    [Fact]
    public void ThePartsOfTheHashShowWhatChanged()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(34), Content, out _);
        ulong whole = GameStateHash.Compute(state);
        ulong[] before = GameStateHash.Sections(state);
        Assert.Equal(GameStateHash.SectionNames.Length, before.Length);

        state.Settlements[0].Stock[0] += Core.Numerics.Amount.FromHundredths(1);
        ulong[] after = GameStateHash.Sections(state);
        string[] changed = [.. GameStateHash.SectionNames.Where((_, k) => before[k] != after[k])];
        Assert.Equal(["settlements"], changed);
        Assert.NotEqual(whole, GameStateHash.Compute(state));
    }

    [Fact]
    public void AForeignFileIsNotAReplay()
    {
        using var file = new MemoryStream("CS2S\u0001\0\0\0junk"u8.ToArray());
        Assert.Throws<InvalidDataException>(() => ReplaySerializer.Read(file));
    }
}
