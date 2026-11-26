using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Relay;
using CivSim.Server;
using Xunit;

namespace CivSim.Tests.Play;

// The relay (MP6): a game server opens a room, players join with the code, the relay only passes bytes.
// One class, so the TLS test's certificate check never meets another relay test running at the same time.
public class RelayTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void TwoPlayersMeetInTheLobbyThroughTheRelayWithTheRoomCode()
    {
        using var relay = Relay(new RelayConfig());
        using PlayLobby lobby = PlayLobby.Create(new PlayOptions(4, 40, 24, 3) { StartPhase = false, HumanSeats = 2 }, new LobbyConfig { GameName = "Test" });
        using var room = new RelayHost(lobby.Accept, "127.0.0.1", relay.Port, "", Timeout);
        lobby.Relay = $"Relay {room.Address}, code {room.Code}";
        Assert.Matches("^[A-Z0-9]{3}-[A-Z0-9]{3}$", room.Code);

        using TcpConnection ala = TcpConnection.JoinRelay("127.0.0.1", relay.Port, room.Code, Timeout);
        using TcpConnection ola = TcpConnection.JoinRelay("127.0.0.1", relay.Port, room.Code.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal), Timeout);
        ala.Send(new HelloMessage(ProtocolInfo.Version, "Ala"));
        ola.Send(new HelloMessage(ProtocolInfo.Version, "Ola"));
        LobbyStateMessage? last = null;
        Protocol.TcpConnectionTests.WaitUntil(() =>
        {
            lobby.Pump();
            while (ola.TryReceive(out IProtocolMessage? m))
            {
                last = m as LobbyStateMessage ?? last;
            }

            return last is { Seats.Count: 2 };
        });

        Assert.Equal($"Relay 127.0.0.1:{relay.Port}, code {room.Code}", last!.Settings[0]);
        Assert.Equal(2, relay.Players);
        Assert.Equal(1, relay.Rooms);
        Assert.All(room.Connections, c => Assert.Null(c.RemoteIp)); // the game never learns the players' addresses
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void TheRelayTurnsAwayAWrongPasswordAnUnknownCodeAndGuessing()
    {
        string file = Path.GetTempFileName();
        File.WriteAllText(file, "# passwords\notter\n");
        try
        {
            using var relay = Relay(new RelayConfig { PasswordsFile = file });
            Action<IConnection> nobody = c => c.Close();
            Assert.Equal("This relay needs a password to open games.", Assert.Throws<RelayException>(() => new RelayHost(nobody, "127.0.0.1", relay.Port, "", Timeout)).Message);
            Assert.Equal("Wrong password to open games on this relay.", Assert.Throws<RelayException>(() => new RelayHost(nobody, "127.0.0.1", relay.Port, "beaver", Timeout)).Message);
            using (new RelayHost(nobody, "127.0.0.1", relay.Port, "otter", Timeout))
            {
                Assert.Equal(1, relay.Rooms);
            }

            Assert.StartsWith("\"hello\" is not a room code", Assert.Throws<RelayException>(() => TcpConnection.JoinRelay("127.0.0.1", relay.Port, "hello", Timeout)).Message);
            for (int i = 0; i < 20; i++)
            {
                Assert.StartsWith("No game with the code AAA-AAA", Assert.Throws<RelayException>(() => TcpConnection.JoinRelay("127.0.0.1", relay.Port, "AAA-AAA", Timeout)).Message);
            }

            Assert.Equal("Too many wrong codes; try again in 10 minutes.", Assert.Throws<RelayException>(() => TcpConnection.JoinRelay("127.0.0.1", relay.Port, "AAA-AAA", Timeout)).Message);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void PasswordsHaveALastDayAndAGraceWeekAndAreReadAgainWhenTheFileChanges()
    {
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "otter 2026-12-31\nbeaver 2026-11-20\nwolf 2026-11-01\nbad 31.12.2026\n");
            DateTime now = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
            var log = new List<string>();
            var passwords = new RelayPasswords(file, 7, () => now, log.Add);
            Assert.True(passwords.Check("otter"));
            Assert.True(passwords.Check("beaver")); // six days after its last day: still works
            Assert.False(passwords.Check("wolf"));
            Assert.False(passwords.Check("bad"));
            Assert.False(passwords.Check(""));
            Assert.Contains(log, l => l.Contains("31.12.2026", StringComparison.Ordinal));

            now = now.AddDays(2);
            Assert.False(passwords.Check("beaver")); // eight days after: no more

            File.WriteAllText(file, "lynx\n");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));
            Assert.False(passwords.Check("lynx")); // looked at once a minute
            now = now.AddMinutes(1);
            Assert.True(passwords.Check("lynx"));
            Assert.False(passwords.Check("otter"));
            Assert.True(new RelayPasswords("", 7, () => now, log.Add).Check("anything")); // no file: open to all
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void AGameServerThatLostTheRelayGetsTheSameCodeBack()
    {
        using var relay = Relay(new RelayConfig());
        (Socket socket, Stream stream) = RelayWire.Connect("127.0.0.1", relay.Port, Timeout, RelayWire.Open, "", "", "", "test");
        string[] first = RelayWire.ReadAnswer(stream);
        stream.Dispose();
        socket.Dispose();

        (socket, stream) = RelayWire.Connect("127.0.0.1", relay.Port, Timeout, RelayWire.Open, "", first[0], "not the secret", "test");
        string[] stranger = RelayWire.ReadAnswer(stream);
        Assert.NotEqual(first[0], stranger[0]); // without the secret: another code
        stream.Dispose();
        socket.Dispose();

        (socket, stream) = RelayWire.Connect("127.0.0.1", relay.Port, Timeout, RelayWire.Open, "", first[0], first[1], "test");
        string[] again = RelayWire.ReadAnswer(stream);
        Assert.Equal(first[0], again[0]);
        stream.Dispose();
        socket.Dispose();
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void ARelayWithACertificateSpeaksTls()
    {
        string dir = Directory.CreateTempSubdirectory("civsim-relay-").FullName;
        try
        {
            using RSA key = RSA.Create(2048);
            var request = new CertificateRequest("CN=relay.test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 made = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            File.WriteAllText(Path.Combine(dir, "fullchain.pem"), made.ExportCertificatePem());
            File.WriteAllText(Path.Combine(dir, "privkey.pem"), key.ExportPkcs8PrivateKeyPem());
            using var relay = Relay(new RelayConfig { CertificateFile = Path.Combine(dir, "fullchain.pem"), KeyFile = Path.Combine(dir, "privkey.pem") });
            byte[] seen = [];
            RelayWire.TestCertificateCheck = (_, cert, _, _) =>
            {
                seen = cert?.GetCertHash() ?? [];
                return true;
            };
            try
            {
                using var host = PlayHost.Create(new PlayOptions(4, 40, 24, 2) { StartPhase = false });
                using var room = new RelayHost(c => host.Accept(c), "127.0.0.1", relay.Port, "", Timeout);
                using TcpConnection player = TcpConnection.JoinRelay("127.0.0.1", relay.Port, room.Code, Timeout);
                player.Send(new HelloMessage(ProtocolInfo.Version, "Ala"));
                Protocol.TcpConnectionTests.WaitUntil(() =>
                {
                    host.RunTicks(1);
                    return player.TryReceive(out IProtocolMessage? m) && m is WelcomeMessage;
                });
                Assert.Equal(made.GetCertHash(), seen);
            }
            finally
            {
                RelayWire.TestCertificateCheck = null;
            }
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SettingsComeFromTheFileThenTheEnvironmentThenTheCommandLine()
    {
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "{ // notes are allowed\n \"port\": 7800, \"maxRooms\": 5, \"passwordsFile\": \"/data/passwords.txt\" }");
            var env = new Dictionary<string, string?> { ["CIVSIM_RELAY_MAXROOMS"] = "6", ["CIVSIM_RELAY_ROOMKBPS"] = "512" };
            RelayConfig c = RelayConfig.Load(["--config", file, "--room-kbps", "1024"], env);
            Assert.Equal(7800, c.Port);
            Assert.Equal(6, c.MaxRooms);
            Assert.Equal(1024, c.RoomKBps);
            Assert.Equal("/data/passwords.txt", c.PasswordsFile);
            Assert.Throws<ArgumentException>(() => RelayConfig.Load(["--port", "99999"], new Dictionary<string, string?>()));
            Assert.Throws<ArgumentException>(() => RelayConfig.Load(["--colour", "red"], new Dictionary<string, string?>()));
            Assert.Throws<ArgumentException>(() => RelayConfig.Load(["--certificate-file", "a.pem"], new Dictionary<string, string?>()));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void RoomCodesAreReadAsTyped()
    {
        Assert.Equal("K7MQ4D", RelayWire.NormalizeCode(" k7m-q4d "));
        Assert.Null(RelayWire.NormalizeCode("K7M-Q4"));
        Assert.Null(RelayWire.NormalizeCode("K7M-Q4DD"));
        Assert.Null(RelayWire.NormalizeCode("O0O-I1I")); // look-alikes are never in a code
        Assert.Equal("K7M-Q4D", RelayWire.ShowCode("K7MQ4D"));
        Assert.True(RelayWire.UsesTls("relay.example.com"));
        Assert.False(RelayWire.UsesTls("203.0.113.5"));
        Assert.False(RelayWire.UsesTls("localhost"));
        Assert.False(RelayWire.UsesTls("relay")); // the relay's container in docker-compose
        Assert.True(RelayWire.UsesTls("civsim.example.com."));
    }

    private static RelayServer Relay(RelayConfig config)
    {
        config.Port = 0;
        return new RelayServer(config, TextWriter.Null, address: IPAddress.Loopback);
    }
}
