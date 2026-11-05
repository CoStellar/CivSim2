using System;
using CivSim.Core.Ai.Utility;
using CivSim.Core.Random;
using Xunit;

namespace CivSim.Tests.Ai;

/// <summary>The shared evaluation of AI actions (GDD 8.3; stage 4a): curves, factors, choosing.</summary>
public class UtilityTests
{
    [Fact]
    public void LinearCurvesInterpolateAndStayFlatOutside()
    {
        ResponseCurve c = ResponseCurve.Linear(10, 0, 20, 1000);
        Assert.Equal(0, c.Evaluate(-5));
        Assert.Equal(0, c.Evaluate(10));
        Assert.Equal(500, c.Evaluate(15));
        Assert.Equal(1000, c.Evaluate(20));
        Assert.Equal(1000, c.Evaluate(99));
    }

    [Fact]
    public void CurvesNeverGoBelowZero()
    {
        // The governor's nearness: 1.0 next door, minus 3% per cell (a straight line through -2000 at 100 cells).
        ResponseCurve nearness = ResponseCurve.Linear(0, 1000, 100, 1000 - (1000 * 3));
        Assert.Equal(1000, nearness.Evaluate(0));
        Assert.Equal(970, nearness.Evaluate(1));
        Assert.Equal(10, nearness.Evaluate(33));
        Assert.Equal(0, nearness.Evaluate(34));
        Assert.Equal(0, nearness.Evaluate(1000));
    }

    [Fact]
    public void SmoothCurvesAreSShapedAndSymmetric()
    {
        ResponseCurve c = ResponseCurve.Smooth(0, 0, 100, 1000);
        Assert.Equal(500, c.Evaluate(50));
        Assert.True(c.Evaluate(10) < 100, "slow start");
        Assert.True(c.Evaluate(90) > 900, "slow end");
        Assert.Equal(1000, c.Evaluate(10) + c.Evaluate(90));
    }

    [Fact]
    public void StepCurvesSwitchAtTheThreshold()
    {
        ResponseCurve c = ResponseCurve.Step(12, 0, 1000);
        Assert.Equal(0, c.Evaluate(11));
        Assert.Equal(1000, c.Evaluate(12));
        Assert.Throws<ArgumentException>(() => ResponseCurve.Linear(5, 0, 5, 1000));
    }

    [Fact]
    public void FactorsInPerMilleGiveExactlyThePercentArithmeticOfStage3()
    {
        // The stage 3 AI computed food * comfort / 100 * (100 - d * p) / 100 * (100 + bonus) / 100; the evaluation must
        // give the same integer for every input, or the simulation would change.
        var random = new DeterministicRandom(42);
        ResponseCurve nearness = ResponseCurve.Linear(0, 1000, 100, 1000 - (1000 * 3));
        for (int n = 0; n < 10_000; n++)
        {
            long food = random.NextInt(5_000_000);
            int comfort = random.NextInt(20, 101);
            int distance = random.NextInt(0, 40);
            long old = food * comfort / 100 * Math.Max(0, 100 - (distance * 3)) / 100 * (100 + 10) / 100;

            Evaluation e = Evaluation.Of(food);
            e.Apply(FactorId.Comfort, comfort * 10, comfort);
            e.Apply(FactorId.Distance, nearness, distance);
            e.Apply(FactorId.OwnCampsite, 1100);
            e.Weigh(1000, 1000);
            Assert.Equal(old, e.Value);
        }
    }

    [Fact]
    public void AZeroFactorRulesTheActionOut()
    {
        Evaluation e = Evaluation.Of(1_000_000);
        e.Apply(FactorId.InputsAvailable, 0);
        e.Apply(FactorId.Comfort, 2000);
        Assert.Equal(0, e.Value);
        Assert.False(new Chooser().Offer(1, e));
    }

    [Fact]
    public void NeutralGoalAndCharacterAddNothing()
    {
        Evaluation e = Evaluation.Of(777);
        e.Weigh(1000, 1000);
        Assert.Equal(777, e.Value);
        Assert.Equal(0, e.FactorCount);
        e.Weigh(1500, 500);
        Assert.Equal(582, e.Value); // 777 * 1.5 = 1165, * 0.5 = 582
        Assert.Equal(FactorId.GoalWeight, e[0].Id);
        Assert.Equal(FactorId.Character, e[1].Id);
    }

    [Fact]
    public void CostsAreSubtractedAndNotesDoNotScore()
    {
        Evaluation e = Evaluation.Of(1000);
        e.Note(FactorId.People, 42);
        e.Subtract(FactorId.ResearchCost, 300);
        Assert.Equal(700, e.Value);
        Assert.Equal(300, e.Cost);
        Assert.Equal(1000, e.Base);
    }

    [Fact]
    public void TheWhyListsTheFactorsThatMatteredMost()
    {
        Evaluation e = Evaluation.Of(10_000);
        e.Note(FactorId.Food, 10_000);
        e.Apply(FactorId.Comfort, 950, 95);
        e.Apply(FactorId.Distance, 400, 20);
        e.Apply(FactorId.OwnCampsite, 1100, 1);
        Span<Factor> top = stackalloc Factor[3];
        Assert.Equal(3, e.Top(top));
        Assert.Equal(FactorId.Distance, top[0].Id);
        Assert.Equal(FactorId.OwnCampsite, top[1].Id);
        Assert.Equal(FactorId.Comfort, top[2].Id);
    }

    [Fact]
    public void TheChooserKeepsTheBestAndTheFirstOnTies()
    {
        var chooser = new Chooser(1);
        Assert.True(chooser.Offer(1, Evaluation.Of(50)));
        Assert.False(chooser.Offer(2, Evaluation.Of(50)));
        Assert.True(chooser.Offer(3, Evaluation.Of(60)));
        Assert.False(chooser.Offer(4, Evaluation.Of(10)));
        Assert.Equal(0, chooser.Pick(new DeterministicRandom(1)));
        Assert.Equal(3, chooser.KeyAt(0));
        chooser.Clear();
        Assert.Equal(-1, chooser.Pick(null));
    }

    [Fact]
    public void TheChooserKeepsTheThreeBestInOrder()
    {
        var chooser = new Chooser(3);
        foreach ((int key, long score) in new[] { (1, 10L), (2, 40L), (3, 30L), (4, 40L), (5, 5L), (6, 35L) })
        {
            chooser.Offer(key, Evaluation.Of(score));
        }

        Assert.Equal(3, chooser.Count);
        Assert.Equal([2, 4, 6], new[] { chooser.KeyAt(0), chooser.KeyAt(1), chooser.KeyAt(2) });
        Assert.Equal(35, chooser.Threshold);
        Assert.False(chooser.WouldKeep(35));
        Assert.True(chooser.WouldKeep(36));
        Assert.Equal(0, chooser.Pick(null));
    }

    [Fact]
    public void DrawsAmongTheBestFollowTheScoresAndRepeatWithTheSeed()
    {
        var chooser = new Chooser(3);
        chooser.Offer(1, Evaluation.Of(600));
        chooser.Offer(2, Evaluation.Of(300));
        chooser.Offer(3, Evaluation.Of(100));
        var counts = new int[3];
        var a = new DeterministicRandom(7);
        var b = new DeterministicRandom(7);
        for (int n = 0; n < 10_000; n++)
        {
            int pick = chooser.Pick(a);
            Assert.Equal(pick, chooser.Pick(b));
            counts[pick]++;
        }

        // Weights 1000 : 500 : 166 of 1666.
        Assert.InRange(counts[0], 5700, 6300);
        Assert.InRange(counts[1], 2700, 3300);
        Assert.InRange(counts[2], 700, 1300);
    }

    [Fact]
    public void HugeScoresDoNotOverflowTheDraw()
    {
        var chooser = new Chooser(3);
        chooser.Offer(1, Evaluation.Of(long.MaxValue / 2));
        chooser.Offer(2, Evaluation.Of(long.MaxValue / 3));
        int pick = chooser.Pick(new DeterministicRandom(3));
        Assert.InRange(pick, 0, 1);
    }
}
