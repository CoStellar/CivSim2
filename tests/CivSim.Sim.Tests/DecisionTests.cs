using System.Linq;
using CivSim.Sim.Ai;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>Rules decided after the tests of 2026-11-19 (claude/decyzje-po-testach.md).</summary>
public sealed class DecisionTests
{
    private static readonly SimContent Content = SimContent.Load();

    [Fact]
    public void AnEraNeedsItsGatewayTechnology()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ civ = g.Civs[0];
        civ.Era = 1;
        int gate = Content.GatewayOf(1);
        Assert.Equal("copper_working", Content.Techs[gate].Key);
        foreach (SimTech t in Content.Techs.Where(t => t.Era == 1 && !t.Basic && t.Id != gate).Take(Content.EraAdvanceTechs + 1))
        {
            civ.KnownTech[t.Id] = true;
        }

        g.AdvanceEra(civ, tell: false);
        Assert.Equal(1, civ.Era); // enough technologies, but no gateway
        civ.KnownTech[gate] = true;
        g.AdvanceEra(civ, tell: false);
        Assert.Equal(2, civ.Era);
    }

    [Fact]
    public void TheAiPaysNoTributeBeforeItLostPeople()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ a = g.Civs[0];
        Civ b = g.Civs[1];
        a.Known[g.Settlements[b.Settlements[0]].Center] = true;
        Assert.Null(g.Apply(new DeclareWarCommand(a.Id, b.Id)));
        Assert.Null(g.Apply(new ProposePeaceCommand(a.Id, b.Id, 5)));
        b.IsAi = true;
        SimAi.Think(g, b);
        Assert.True(g.AtWar(a.Id, b.Id)); // refused: no battle yet (E1)
        Assert.Null(g.Apply(new ProposePeaceCommand(a.Id, b.Id, 0)));
        SimAi.Think(g, b);
        Assert.False(g.AtWar(a.Id, b.Id)); // a plain peace is fine
    }

    [Fact]
    public void ARouteKeepsWhatTheSourceNeeds()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ civ = g.Civs[0];
        Settlement home = g.Settlements[civ.Settlements[0]];
        int wood = Content.GoodId("wood");
        var t = new TradeRoute { Civ = civ.Id, From = home.Id, To = home.Id, Good = wood, Amount = 20 * SimContent.One, KeepAtSource = 50 * SimContent.One };
        Assert.Equal(50 * SimContent.One, t.KeepAtSource);
        Assert.NotNull(g.Apply(new RouteCommand(civ.Id, home.Id, home.Id, wood, 20 * SimContent.One, null, 50 * SimContent.One)));
    }

    [Fact]
    public void ADepositOfALaterEraShowsAsUnknownUntilThatEra()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ civ = g.Civs[0];
        Settlement home = g.Settlements[civ.Settlements[0]];
        int iron = Content.World.Deposits.First(d => d.Key == "iron_ore").Id;
        int cell = home.Cells.First(c => c != home.Center);
        g.Map.Deposit.Set(cell, (ushort)iron);
        civ.Era = 1;
        Assert.True(g.DepositHidden(civ, cell));
        Assert.False(g.KnowsDeposit(civ, cell));
        civ.Era = Content.DepositEra[iron];
        Assert.False(g.DepositHidden(civ, cell));
        Assert.True(g.KnowsDeposit(civ, cell)); // learnt with the era, nobody had to look again (M6)
    }
}
