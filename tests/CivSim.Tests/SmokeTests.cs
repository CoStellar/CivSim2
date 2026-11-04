using CivSim.Protocol;
using Xunit;

namespace CivSim.Tests;

public class SmokeTests
{
    [Fact]
    public void ProtocolVersionIsPositive()
    {
        Assert.True(ProtocolInfo.Version >= 1);
    }

    [Fact]
    public void HeadlessRunnerExitsCleanly()
    {
        Assert.Equal(0, CivSim.Headless.Program.Main([]));
    }
}
