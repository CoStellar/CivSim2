using System;
using CivSim.Core.Numerics;
using Xunit;

namespace CivSim.Tests.Numerics;

public class AmountTests
{
    [Fact]
    public void ConstructionAndFormatting()
    {
        Assert.Equal("3.00", Amount.FromUnits(3).ToString());
        Assert.Equal("1.25", Amount.FromHundredths(125).ToString());
        Assert.Equal("-0.05", Amount.FromHundredths(-5).ToString());
        Assert.Equal(12, Amount.FromHundredths(1299).WholeUnits);
    }

    [Theory]
    [InlineData("12", 1200)]
    [InlineData("0.5", 50)]
    [InlineData("-3.25", -325)]
    [InlineData("+7.01", 701)]
    [InlineData(" 4.10 ", 410)]
    public void ParsesValidText(string text, long hundredths)
    {
        Assert.Equal(hundredths, Amount.Parse(text).Hundredths);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.234")]
    [InlineData("1,5")]
    [InlineData(".5")]
    [InlineData("5.")]
    [InlineData("abc")]
    [InlineData("1e3")]
    [InlineData("99999999999999999999")]
    public void RejectsInvalidText(string text)
    {
        Assert.False(Amount.TryParse(text, out _));
    }

    [Fact]
    public void ArithmeticIsExact()
    {
        Amount a = Amount.Parse("0.10");
        Amount sum = Amount.Zero;
        for (int i = 0; i < 10; i++)
        {
            sum += a;
        }

        Assert.Equal(Amount.FromUnits(1), sum);
        Assert.Equal(Amount.Parse("2.50"), Amount.Parse("5") - Amount.Parse("2.5"));
        Assert.Equal(Amount.Parse("7.50"), Amount.Parse("2.50") * 3);
        Assert.Equal(Amount.Parse("-2.50"), -Amount.Parse("2.50"));
    }

    [Fact]
    public void OverflowThrows()
    {
        Assert.Throws<OverflowException>(() => Amount.MaxValue + Amount.Epsilon);
        Assert.Throws<OverflowException>(() => Amount.MaxValue * 2);
        Assert.Throws<OverflowException>(() => Amount.FromUnits(long.MaxValue));
    }

    [Theory]
    [InlineData(250, 2, Rounding.HalfAwayFromZero, 125)]
    [InlineData(5, 2, Rounding.HalfAwayFromZero, 3)]
    [InlineData(-5, 2, Rounding.HalfAwayFromZero, -3)]
    [InlineData(5, 2, Rounding.Floor, 2)]
    [InlineData(-5, 2, Rounding.Floor, -3)]
    [InlineData(5, 2, Rounding.Ceiling, 3)]
    [InlineData(-5, 2, Rounding.Ceiling, -2)]
    [InlineData(5, 2, Rounding.TowardZero, 2)]
    [InlineData(-5, 2, Rounding.TowardZero, -2)]
    [InlineData(4, 3, Rounding.HalfAwayFromZero, 1)]
    [InlineData(5, 3, Rounding.HalfAwayFromZero, 2)]
    public void DivisionRoundsAsRequested(long hundredths, long divisor, Rounding rounding, long expected)
    {
        Assert.Equal(expected, Amount.FromHundredths(hundredths).Divide(divisor, rounding).Hundredths);
    }

    [Fact]
    public void PercentRatioAndProduct()
    {
        Amount harvest = Amount.Parse("12.34");
        Assert.Equal(Amount.Parse("7.40"), harvest.MultiplyPercent(60));       // 7.404 -> 7.40
        Assert.Equal(Amount.Parse("4.11"), harvest.MultiplyRatio(1, 3));        // 4.1133 -> 4.11
        Assert.Equal(Amount.Parse("4.12"), harvest.MultiplyRatio(1, 3, Rounding.Ceiling));
        Assert.Equal(Amount.Parse("3.75"), Amount.Parse("1.50").Multiply(Amount.Parse("2.50")));
        Assert.Equal(Amount.Parse("1.50"), Amount.Parse("3").Divide(Amount.Parse("2")));
    }

    [Fact]
    public void MulDivDoesNotOverflowInTheMiddle()
    {
        long big = long.MaxValue / 2;
        Assert.Equal(big, IntMath.MulDiv(big, 1_000_000, 1_000_000));
        Assert.Throws<OverflowException>(() => IntMath.MulDiv(long.MaxValue, 2, 1));
        Assert.Throws<DivideByZeroException>(() => IntMath.MulDiv(1, 1, 0));
    }

    [Fact]
    public void SplitAlwaysAddsUpExactly()
    {
        Amount total = Amount.FromUnits(10);
        long[] weights = [1, 1, 1];
        var shares = new Amount[3];
        total.SplitProportionally(weights, shares);

        Assert.Equal(total, shares[0] + shares[1] + shares[2]);
        Assert.Equal(Amount.Parse("3.34"), shares[0]); // ties go to the lowest index
        Assert.Equal(Amount.Parse("3.33"), shares[1]);
        Assert.Equal(Amount.Parse("3.33"), shares[2]);
    }

    [Fact]
    public void SplitRespectsWeightsAndZeros()
    {
        var shares = new Amount[3];
        Amount.Parse("1.00").SplitProportionally([0, 3, 1], shares);
        Assert.Equal(Amount.Zero, shares[0]);
        Assert.Equal(Amount.Parse("0.75"), shares[1]);
        Assert.Equal(Amount.Parse("0.25"), shares[2]);

        Amount.FromUnits(5).SplitProportionally([0, 0, 0], shares);
        Assert.Equal(Amount.Zero, shares[0] + shares[1] + shares[2]);
    }

    [Fact]
    public void ClampMinMaxAndCompare()
    {
        Amount a = Amount.FromUnits(5);
        Assert.Equal(Amount.FromUnits(3), a.Clamp(Amount.Zero, Amount.FromUnits(3)));
        Assert.Equal(a, Amount.Max(a, Amount.FromUnits(2)));
        Assert.True(Amount.FromUnits(1) < a);
        Assert.Equal(1, a.CompareTo(Amount.Zero));
    }
}
