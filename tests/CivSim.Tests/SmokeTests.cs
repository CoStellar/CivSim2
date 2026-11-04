using CivSim.Protocol;
using CivSim.Server;
using Xunit;

namespace CivSim.Tests;

public class SmokeTests
{
    [Fact]
    public void ServerSpeaksCurrentProtocolVersion()
    {
        Assert.Equal(ProtocolInfo.Version, ServerInfo.ProtocolVersion);
    }

    [Fact]
    public void HeadlessRunnerExitsCleanly()
    {
        Assert.Equal(0, CivSim.Headless.Program.Main([]));
    }
}
