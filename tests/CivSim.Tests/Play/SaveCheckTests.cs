using System.IO;
using CivSim.Server;
using Xunit;

namespace CivSim.Tests.Play;

/// <summary>Abuse tests 2026-11-24: edited or damaged saves are refused with a reason instead of crashing or freezing the game.</summary>
public sealed class SaveCheckTests
{
    private static SavedGame Good() => new() { Options = new PlayOptions(1, 66, 40, 2), Players = [0], Tick = 100 };

    [Fact]
    public void AGoodSavePasses() => PlaySaves.Check(Good());

    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    public void ASaveWithOddCivilizationsIsRefused(int civs) =>
        Assert.Throws<InvalidDataException>(() => PlaySaves.Check(Good() with { Options = new PlayOptions(1, 66, 40, civs) }));

    [Fact]
    public void ASaveNamingAScenarioOutsideItsFolderIsRefused() =>
        Assert.Throws<InvalidDataException>(() => PlaySaves.Check(Good() with { Options = new PlayOptions(1, 66, 40, 2) { Scenario = "../../game" } }));

    [Fact]
    public void ASaveFarInTheFutureIsRefused() =>
        Assert.Throws<InvalidDataException>(() => PlaySaves.Check(Good() with { Tick = 2_000_000_000 }));

    [Fact]
    public void ACommandOfTheAiIsRefused() =>
        Assert.Throws<InvalidDataException>(() => PlaySaves.Check(Good() with { Commands = [new SavedCommand(5, 1, CivSim.Protocol.Messages.PlayCommandKind.DeclareWar, 0, 0, 0, false, 0, 0, null, 0)] }));
}
