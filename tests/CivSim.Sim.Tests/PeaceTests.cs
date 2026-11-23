using System.Collections.Generic;
using System.Linq;
using CivSim.Core.Map;
using CivSim.Sim.Ai;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>Peace from negotiation and war weariness (ADR 0030, Patryk 2026-11-23).</summary>
public sealed class PeaceTests
{
    private static readonly SimContent Content = SimContent.Load();

    private static void Run(SimGame g, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            g.Tick();
        }
    }

    // Two peoples at war for longer than tributeAfterSeconds, the first ahead by killing some of the second's soldiers.
    private static SimGame AheadInAWar(int killed = 30)
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        for (ulong seed = 2; !g.Reachable(g.Civs[1], g.Civs[0]) && seed < 40; seed++)
        {
            g = SimGame.Create(Content, new SimSettings(seed, 66, 40, 2) { Ai = false }).Basics();
        }

        Civ a = g.Civs[0];
        Civ b = g.Civs[1];
        a.Known[g.Settlements[b.Settlements[0]].Center] = true;
        Assert.Null(g.Apply(new DeclareWarCommand(b.Id, a.Id))); // b started it (7.3A)
        Run(g, Content.War.TributeAfterTicks + 1);
        g.KillForTest(b, a, killed);
        foreach (int sid in b.Settlements)
        {
            for (int k = 0; k < g.Settlements[sid].Stock.Length; k++)
            {
                g.Settlements[sid].Stock[k] = 100 * SimContent.One;
            }
        }

        g.SyncForTest(b);
        return g;
    }

    [Fact]
    public void OnlyTheSideTheWarFavoursMayDemand()
    {
        SimGame g = AheadInAWar();
        Civ a = g.Civs[0];
        Civ b = g.Civs[1];
        int wood = Content.GoodId("wood");
        Assert.True(g.Favour(a.Id, b.Id) > 0);
        Assert.True(g.Favour(b.Id, a.Id) < 0);
        var demand = new PeaceTerms(false, 0, -1, [(wood, 10 * SimContent.One)]);
        Assert.NotNull(g.Apply(new ProposePeaceCommand(b.Id, a.Id, demand)));
        Assert.Null(g.Apply(new ProposePeaceCommand(a.Id, b.Id, demand)));
        Assert.NotNull(g.Apply(new ProposePeaceCommand(a.Id, b.Id, new PeaceTerms(false, 0, -1, [(wood, 1000 * SimContent.One)])))); // more than they have
    }

    [Fact]
    public void TheAggressorWhoLosesGivesAQuarterMore()
    {
        SimGame g = AheadInAWar();
        long fair = g.FairWorth(0, 1);
        long plain = g.Wealth(g.Civs[1]) * g.Favour(0, 1) / 100;
        Assert.True(g.Aggressor(1, 0));
        Assert.Equal(plain * (100 + Content.Peace.AggressorExtraPercent) / 100, fair);
    }

    [Fact]
    public void TheAiTakesFairTermsAndRefusesGreedyOnes()
    {
        SimGame g = AheadInAWar();
        Civ a = g.Civs[0];
        Civ b = g.Civs[1];
        b.IsAi = true;
        b.Aggression = 100; // no give at all
        PeaceTerms fair = g.SuggestTerms(a.Id, b.Id, 1);
        Assert.False(fair.IsWhite);
        Assert.True(g.TermsWorth(fair) <= g.FairWorth(a.Id, b.Id)); // whole units, rounded down
        // Greedy: a fifth more than the war asks, in whole loads of the goods they have.
        var asked = new List<(int, long)>();
        long worth = 0;
        foreach (int k in Enumerable.Range(0, Content.Goods.Count).Where(k => b.Stock[k] > 0 && k != Content.Food))
        {
            if (worth * 100 > g.FairWorth(a.Id, b.Id) * 120)
            {
                break;
            }

            asked.Add((k, b.Stock[k]));
            worth += g.Worth(k, b.Stock[k]);
        }

        var greedy = new PeaceTerms(false, 1, -1, asked);
        Assert.True(g.TermsWorth(greedy) > g.FairWorth(a.Id, b.Id));
        Assert.False(SimAi.AcceptsPeace(g, b, new PeaceOffer(a.Id, b.Id, greedy, g.Ticks)));
        Assert.True(SimAi.AcceptsPeace(g, b, new PeaceOffer(a.Id, b.Id, fair, g.Ticks)));
    }

    [Fact]
    public void ALosingAiLikesALongPeaceAndAWinningAiAShortOne()
    {
        SimGame g = AheadInAWar();
        Civ a = g.Civs[0];
        Civ b = g.Civs[1];
        b.Aggression = 50;
        a.Aggression = 50;
        int wood = Content.GoodId("wood");
        b.Stock[wood] = 100_000 * SimContent.One; // plenty, so the demand below does not change what b has much
        long fair = g.FairWorth(a.Id, b.Id);

        // The loser b is asked for 15% more than the war asks: refused for 3 minutes of peace, taken for 10.
        long units = (fair * 115 / 100 / g.Worth(wood, SimContent.One)) + 1;
        var shortPeace = new PeaceTerms(false, 0, -1, [(wood, units * SimContent.One)]);
        var longPeace = shortPeace with { Length = 2 };
        Assert.False(SimAi.AcceptsPeace(g, b, new PeaceOffer(a.Id, b.Id, shortPeace, g.Ticks)));
        Assert.True(SimAi.AcceptsPeace(g, b, new PeaceOffer(a.Id, b.Id, longPeace, g.Ticks)));

        // The winner a takes a white peace from nobody: the war gives it more.
        Assert.False(SimAi.AcceptsPeace(g, a, new PeaceOffer(b.Id, a.Id, PeaceTerms.White(2), g.Ticks)));
    }

    [Fact]
    public void ASettlementPassesInAPeaceButNeverTheFirst()
    {
        SimGame g = AheadInAWar();
        Civ a = g.Civs[0];
        Civ b = g.Civs[1];
        Settlement first = g.Settlements[b.Settlements[0]];
        Settlement camp = g.FoundSettlement(b, SiteNear(g, g.Settlements[a.Settlements[0]]), 12, new long[Content.Goods.Count]);
        a.SeenSettlements[camp.Center] = new SettlementMemory(camp.Id, b.Id, camp.Name, camp.Level, camp.Center); // our scouts saw it
        g.Tick();

        Assert.NotNull(g.SettlementTermRefused(a, first));
        Assert.Null(g.SettlementTermRefused(a, camp)); // within 12 hexes of our land
        Assert.NotNull(g.Apply(new ProposePeaceCommand(a.Id, b.Id, new PeaceTerms(false, 0, first.Id, []))));
        Assert.Null(g.Apply(new ProposePeaceCommand(a.Id, b.Id, new PeaceTerms(false, 0, camp.Id, []))));
        Assert.Null(g.Apply(new AnswerPeaceCommand(b.Id, a.Id, true)));
        Assert.Equal(a.Id, camp.Civ);
        Assert.Contains(camp.Id, a.Settlements);
        Assert.Equal(12, camp.People);
        Assert.False(g.AtWar(a.Id, b.Id));
        Assert.Equal(1, g.SettlementsByPeace);
    }

    [Fact]
    public void WearinessGrowsInWarAndFallsInPeace()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ a = g.Civs[0];
        Civ b = g.Civs[1];
        a.Known[g.Settlements[b.Settlements[0]].Center] = true;
        Assert.Null(g.Apply(new DeclareWarCommand(a.Id, b.Id)));
        Run(g, 10 * SimContent.TicksPerMinute);
        int w = g.Weariness(a.Id, b.Id);
        Assert.InRange(w, 10 * Content.Weariness.PerMinute, 100);
        g.KillForTest(b, a, 10);
        Assert.True(g.Weariness(b.Id, a.Id) > g.Weariness(a.Id, b.Id), "losses weary the loser");
        g.MakePeace(a, b);
        Run(g, 2 * SimContent.TicksPerMinute);
        Assert.Equal(w - (2 * Content.Weariness.PeaceFallPerMinute), g.Weariness(a.Id, b.Id));
    }

    [Fact]
    public void HighWearinessSlowsOnlyTheWorkshops()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ a = g.Civs[0];
        a.Known[g.Settlements[g.Civs[1].Settlements[0]].Center] = true;
        Assert.Null(g.Apply(new DeclareWarCommand(a.Id, 1)));
        Assert.Equal(0, g.WarWorkLoss(a));
        g.WearyForTest(a, g.Civs[1], 100);
        Assert.Equal(Content.Weariness.WorkMaxLossPercent, g.WarWorkLoss(a));
        g.WearyForTest(a, g.Civs[1], 75);
        Assert.Equal(Content.Weariness.WorkMaxLossPercent / 2, g.WarWorkLoss(a));
    }

    // A free hex for a settlement 6-11 hexes from a settlement.
    private static int SiteNear(SimGame g, Settlement home)
    {
        HexCoord c = g.Geo.FromIndex(home.Center);
        for (int r = 6; r < 12; r++)
        {
            var ring = new HexCoord[HexGlobe.RingSize(r)];
            int n = g.Geo.Ring(c, r, ring);
            for (int k = 0; k < n; k++)
            {
                int i = g.Geo.Index(ring[k]);
                if (g.CannotFound(i) is null)
                {
                    return i;
                }
            }
        }

        throw new Xunit.Sdk.XunitException("no free site near home");
    }
}
