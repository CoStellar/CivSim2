using System.IO;
using System.Linq;
using CivSim.Core.Ai;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Map;
using CivSim.Core.Perception;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Data;
using CivSim.Server.Saves;
using Xunit;

namespace CivSim.Tests.Diplomacy;

/// <summary>Discovery of civilizations, rumors and the memory of relations (GDD 8.5; stage 4c).</summary>
public class ContactTests
{
    private static readonly GameContent Content = TestContent.Load();

    private static GameState NewState(ulong seed) => GameState.CreateNew(GameSettings.Test(seed), Content, out _);

    // Only sight and contact: nothing else moves, so the test decides who stands where.
    private static TurnRunner SightOnly(GameState state)
    {
        var runner = new TurnRunner(state);
        runner.AddSystem(new PerceptionSystem(Content));
        runner.AddSystem(new ContactSystem(Content));
        return runner;
    }

    // Moves the camps into the four corners of the small test map and clears the zones, so nobody sees anybody.
    private static void Spread(GameState state)
    {
        WorldState earth = state.Worlds[0];
        for (int k = 0; k < state.Settlements.Count; k++)
        {
            state.Settlements[k].Cell = earth.Geometry.Wrap(k % 2 * 33, 10 + (k / 2 * 20));
        }

        for (int i = 0; i < earth.Geometry.CellCount; i++)
        {
            earth.Map.Owner.Set(i, WorldMap.NoOwner);
        }
    }

    [Fact]
    public void NobodyKnowsAnybodyAtTheStart()
    {
        GameState state = NewState(3);
        Assert.All(state.Civilizations, c => Assert.Empty(c.Relations));
    }

    [Fact]
    public void ACampSeenBySettlementsOrScoutsBringsFirstContact()
    {
        GameState state = NewState(3);
        SettlementState a = state.Settlements[0];
        SettlementState b = state.Settlements[1];
        b.Cell = state.Worlds[0].Geometry.Wrap(a.Cell.Col + 3, a.Cell.Row);
        TurnRunner runner = SightOnly(state);

        runner.RunTurn();

        CivilizationState civA = state.FindCivilization(a.Owner)!;
        Relation r = civA.Relation(b.Owner)!;
        Assert.True(r.HasMet);
        Assert.Equal(state.Month, r.MetMonth);
        Assert.Contains(runner.LastChronicle, l => l.StartsWith("First contact", System.StringComparison.Ordinal));
        Assert.True(state.FindCivilization(b.Owner)!.Relation(a.Owner)!.HasMet, "both camps see each other");

        // A second turn is no new first contact.
        runner.RunTurn();
        Assert.DoesNotContain(runner.LastChronicle, l => l.StartsWith("First contact", System.StringComparison.Ordinal));
    }

    [Fact]
    public void TouchingZonesBringContactWithAWorseFirstOpinion()
    {
        GameState state = NewState(3);
        Spread(state);
        WorldState earth = state.Worlds[0];
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        int cell = earth.Geometry.Index(earth.Geometry.Wrap(40, 20));
        int next = earth.Geometry.Index(earth.Geometry.Wrap(41, 20));
        earth.Map.Owner.Set(cell, (short)a.Id.Value);
        earth.Map.Owner.Set(next, (short)b.Id.Value);

        SightOnly(state).RunTurn();

        Relation r = a.Relation(b.Id)!;
        Assert.Equal(ContactWay.Border, r.Way);
        Assert.Equal(Contacts.Opening(Content.Relations, a, ContactWay.Sighted) + Content.Relations.BorderOpening, r.Opening);
    }

    [Fact]
    public void OpennessSetsTheFirstOpinion()
    {
        var closed = new CivilizationState(new CivId(0), "Closed", PlayerId.Ai) { Traits = new int[Character.TraitCount] };
        var open = new CivilizationState(new CivId(1), "Open", PlayerId.Ai) { Traits = Enumerable.Repeat(1000, Character.TraitCount).ToArray() };
        Assert.Equal(-Content.Relations.OpeningSympathy, Contacts.Opening(Content.Relations, closed, ContactWay.Sighted));
        Assert.Equal(Content.Relations.OpeningSympathy, Contacts.Opening(Content.Relations, open, ContactWay.Sighted));
        Assert.Equal(0, Contacts.Opening(Content.Relations, new CivilizationState(new CivId(2), "Average", PlayerId.Ai), ContactWay.Sighted));
    }

    [Fact]
    public void AnAttackIsRememberedAndFadesTheLongerTheMoreVengeful()
    {
        GameState state = NewState(4);
        SettlementState attacker = state.Settlements[0];
        SettlementState defender = state.Settlements[1];
        attacker.Population[2] = 200;
        defender.Cell = state.Worlds[0].Geometry.Wrap(attacker.Cell.Col + 3, attacker.Cell.Row);
        CivilizationState victim = state.FindCivilization(defender.Owner)!;
        victim.Traits = new int[Character.TraitCount];
        victim.Traits[(int)Trait.Openness] = Character.Average;

        string? line = new ConquerSettlementCommand(Content, attacker.Id, defender.Id).ApplyWithChronicle(state, attacker.Owner);

        Relation r = victim.Relation(attacker.Owner)!;
        Assert.Equal(ContactWay.Attack, r.Way);
        Assert.Contains("first meeting", line, System.StringComparison.Ordinal);
        RelationEventDefinition attack = Content.Relations.Events[(int)RelationEvent.Attack];
        Assert.Equal(attack.Sympathy, r.Sympathy);
        Assert.Equal(attack.Trust, r.Trust);
        Assert.True(state.FindCivilization(attacker.Owner)!.Relation(victim.Id)!.HasMet, "the attacker knows its victims");

        // Vengefulness 0: the memory fades over the plain years, half of it is gone half way.
        int fade = attack.Years * 12;
        Assert.Equal(fade, r.Memories[0].FadeMonths);
        r.Recount(state.Month + (fade / 2));
        Assert.Equal(attack.Sympathy / 2, r.Sympathy);
        r.Forget(state.Month + fade);
        Assert.Empty(r.Memories);

        // A fully vengeful civilization remembers for vengefulYears.
        victim.Traits[(int)Trait.Vengefulness] = 1000;
        Contacts.RecordAttack(Content, state, victim.Id, attacker.Owner);
        Assert.Equal(attack.VengefulYears * 12, r.Memories[^1].FadeMonths);
    }

    [Fact]
    public void ABorderDisputeGrowsWhileItLastsThenFades()
    {
        RelationRules rules = Content.Relations;
        RelationEventDefinition dispute = rules.Events[(int)RelationEvent.BorderDispute];
        var r = new Relation(new CivId(1)) { Level = ContactLevel.Met };
        for (int turn = 0; turn < 3; turn++)
        {
            Contacts.Sustain(rules, r, RelationEvent.BorderDispute, turn * 600, 600);
        }

        RelationMemory m = Assert.Single(r.Memories);
        Assert.True(m.IsOngoing);
        Assert.Equal(System.Math.Max(dispute.Sympathy, dispute.SympathyPerYear * 150), m.Sympathy);

        Contacts.End(r, RelationEvent.BorderDispute, 1200);
        r.Recount(1200);
        Assert.Equal(m.Sympathy, r.Sympathy);
        r.Forget(1200 + (dispute.Years * 12));
        Assert.Empty(r.Memories);
    }

    [Fact]
    public void ATakenOverClaimedSettlementIsABorderDispute()
    {
        GameState state = NewState(4);
        Spread(state);
        SettlementState lost = state.Settlements[0];
        SettlementState taker = state.Settlements[1];
        CivilizationState claimer = state.FindCivilization(lost.Owner)!;
        Contacts.Meet(Content, state, claimer, taker.Owner, ContactWay.Sighted);
        lost.ClaimCiv = lost.Owner.Value;
        lost.ClaimUntilMonth = state.Month + 10_000;
        lost.Owner = taker.Owner;

        SightOnly(state).RunTurn();

        Relation r = claimer.Relation(taker.Owner)!;
        Assert.Contains(r.Memories, m => m.Kind == RelationEvent.BorderDispute && m.IsOngoing);
        Assert.True(r.Sympathy < r.Opening);
    }

    [Fact]
    public void RumorsTellOfPeoplesMetByOthersAndRealContactReplacesThem()
    {
        GameState state = NewState(4);
        Spread(state);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        CivilizationState c = state.Civilizations[2];
        Contacts.Meet(Content, state, a, b.Id, ContactWay.Sighted);
        Contacts.Meet(Content, state, b, c.Id, ContactWay.Sighted);
        TurnRunner runner = SightOnly(state);
        for (int t = 0; t < 200 && a.Relation(c.Id) is null; t++)
        {
            runner.RunTurn();
        }

        Relation rumor = a.Relation(c.Id)!;
        Assert.Equal(ContactLevel.Rumor, rumor.Level);
        Assert.False(rumor.HasMet);
        Assert.Equal(b.Id.Value, rumor.RumorFrom);
        Assert.True(rumor.RumorPeople > 0);
        Assert.InRange(rumor.RumorCell, 0, state.Worlds[0].Geometry.CellCount - 1);
        Assert.Equal(0, Contacts.Sympathy(a, c.Id));

        Assert.True(Contacts.Meet(Content, state, a, c.Id, ContactWay.Sighted), "real contact after a rumor is a first contact");
        Assert.Equal(-1, rumor.RumorCell);
        Assert.True(rumor.HasMet);
    }

    [Fact]
    public void TheWeakestMemoryMakesRoomWhenFull()
    {
        var r = new Relation(new CivId(1));
        for (int k = 0; k < 5; k++)
        {
            r.Remember(new RelationMemory { Kind = RelationEvent.Attack, Sympathy = -1000 * (k + 1), EndMonth = 0, FadeMonths = 1200 }, 3, 0);
        }

        Assert.Equal(3, r.Memories.Count);
        Assert.Equal([-3000, -4000, -5000], r.Memories.Select(m => m.Sympathy).ToArray());
    }

    [Fact]
    public void RelationsStaySortedAndSurviveSaving()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(9), Content, out _);
        var runner = new TurnRunner(state);
        CoreSystems.Register(runner, Content);
        CivilizationState a = state.Civilizations[0];
        Contacts.Meet(Content, state, a, state.Civilizations[3].Id, ContactWay.Sighted);
        Contacts.Meet(Content, state, a, state.Civilizations[1].Id, ContactWay.Border);
        Contacts.RecordAttack(Content, state, a.Id, state.Civilizations[2].Id);
        Assert.Equal([1, 2, 3], a.Relations.Select(r => r.Other.Value).ToArray());
        for (int t = 0; t < 5; t++)
        {
            runner.RunTurn();
        }

        using var stream = new MemoryStream();
        SaveGameSerializer.Save(state, stream);
        stream.Position = 0;
        GameState loaded = SaveGameSerializer.Load(stream);
        CivilizationState copy = loaded.FindCivilization(a.Id)!;
        Assert.Equal(a.Relations.Count, copy.Relations.Count);
        for (int k = 0; k < a.Relations.Count; k++)
        {
            Assert.Equal(a.Relations[k].Other, copy.Relations[k].Other);
            Assert.Equal(a.Relations[k].Sympathy, copy.Relations[k].Sympathy);
            Assert.Equal(a.Relations[k].Memories.ToArray(), copy.Relations[k].Memories.ToArray());
        }

        Assert.Equal(Core.Hashing.GameStateHash.Compute(state), Core.Hashing.GameStateHash.Compute(loaded));
    }

    [Fact]
    public void TheEventTableComesFromData()
    {
        Assert.Equal(System.Enum.GetValues<RelationEvent>().Length, Content.Relations.Events.Count);
        for (int k = 0; k < Content.Relations.Events.Count; k++)
        {
            Assert.Equal((RelationEvent)k, Content.Relations.Events[k].Kind);
        }

        RelationEventDefinition trade = Content.Relations.Events[(int)RelationEvent.ActiveTrade];
        Assert.Equal(50, trade.SympathyPerYear); // "0.5" in the file
        Assert.Equal(-5000, Content.Relations.Events[(int)RelationEvent.Attack].Sympathy);
    }

    [Fact]
    public void BrokenRelationDataIsRejected()
    {
        string dir = Path.Combine(Path.GetTempPath(), "civsim-relations-" + System.Environment.ProcessId);
        Directory.CreateDirectory(dir);
        try
        {
            foreach (string file in Directory.GetFiles(ContentLoader.DefaultDirectory, "*.json"))
            {
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
            }

            string path = Path.Combine(dir, "relations.json");
            string text = File.ReadAllText(path)
                .Replace("{\"kind\": \"war_help\", \"sympathy\": 30, \"trust\": 25, \"years\": 200},", string.Empty, System.StringComparison.Ordinal)
                .Replace("\"sympathyPerYear\": -1", "\"sympathyPerYear\": 1", System.StringComparison.Ordinal);
            File.WriteAllText(path, text);

            ContentException e = Assert.Throws<ContentException>(() => ContentLoader.Load(dir));
            Assert.Contains(e.Errors, x => x.Contains("missing kind 'war_help'", System.StringComparison.Ordinal));
            Assert.Contains(e.Errors, x => x.Contains("sympathyPerYear: must lead towards sympathy", System.StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
