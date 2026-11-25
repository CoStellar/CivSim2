using System;
using System.Text.RegularExpressions;
using CivSim.Protocol;
using Xunit;

namespace CivSim.Tests.Protocol;

/// <summary>The game version is year.month.release (ADR 0031) and the same in the code and in the built files.</summary>
public class GameVersionTests
{
    [Fact]
    public void TheGameVersionIsYearMonthRelease()
    {
        Match m = Regex.Match(ProtocolInfo.GameVersion, @"^(\d{2})\.(\d{1,2})\.(\d+)$");
        Assert.True(m.Success, $"\"{ProtocolInfo.GameVersion}\" is not year.month.release");
        Assert.InRange(int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), 1, 12);
    }

    [Fact]
    public void TheBuiltFilesCarryTheGameVersion()
    {
        Version? built = typeof(ProtocolInfo).Assembly.GetName().Version;
        Assert.NotNull(built);
        Assert.Equal(ProtocolInfo.GameVersion, $"{built.Major}.{built.Minor}.{built.Build}");
    }
}
