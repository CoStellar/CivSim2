using System;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>What the taker does with a fallen settlement (Patryk, 2026-11-20, question 7).</summary>
public enum TakeChoice
{
    /// <summary>It becomes ours.</summary>
    Take = 0,

    /// <summary>We carry off part of its goods; it stays theirs and nobody besieges it for a while.</summary>
    Sack = 1,

    /// <summary>It becomes a ruin; its residents flee to their people's nearest settlement.</summary>
    Raze = 2,
}

/// <summary>Choose what happens to a settlement that fell to us.</summary>
public sealed record TakeSettlementCommand(int Civ, int Settlement, TakeChoice Choice) : SimCommand(Civ);

/// <summary>
/// Sieges and garrisons (Patryk, 2026-11-20, questions 3, 5, 7, 8; data: siege). An enemy army near a settlement blockades it:
/// it works less, trains and starts nothing, and no caravans come or go. Its own army units on its land, its standing guard
/// (from palisades, walls and castles) and a share of its residents defend it. When they are gone it falls and its taker chooses
/// to take it, sack it or raze it.
/// </summary>
public sealed partial class SimGame
{
    /// <summary>Settlements that fell, by what their takers chose (for reports).</summary>
    public int[] Falls { get; } = new int[3];

    /// <summary>The standing guard a settlement's works and buildings give it.</summary>
    public int GuardMax(Settlement s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (s.Abandoned || (uint)s.Civ >= (uint)Civs.Count)
        {
            return 0;
        }

        SiegeRules r = Content.Siege;
        long hundredths = 0;
        if (WorksAt.Length > 0)
        {
            foreach (int cell in s.Cells)
            {
                if (WorksCiv[cell] != s.Civ)
                {
                    continue;
                }

                if (WallBit != 0 && (WorksAt[cell] & WallBit) != 0)
                {
                    hundredths += r.GuardPerWallHexPercent;
                }
                else if (PalisadeBit != 0 && (WorksAt[cell] & PalisadeBit) != 0)
                {
                    hundredths += r.GuardPerPalisadeHexPercent;
                }
            }
        }

        int guard = (int)(hundredths / 100);
        foreach (int id in Civs[s.Civ].Buildings)
        {
            Building b = Buildings[id];
            if (b.Settlement == s.Id && b.State == BuildingState.Active)
            {
                guard += Content.Buildings[b.Type].Guard;
            }
        }

        return guard;
    }

    // Once a second: blockades begin and lift, guards grow back, a taker who did not choose takes the settlement.
    private void SiegeStep()
    {
        SiegeRules r = Content.Siege;
        foreach (Settlement s in Settlements)
        {
            if (s.Abandoned || (uint)s.Civ >= (uint)Civs.Count || Civs[s.Civ].Dead)
            {
                continue;
            }

            s.DefensePct = Defense(s);
            if (s.FallenTo >= 0)
            {
                if (Ticks - s.FallenTick >= r.ChoiceSeconds * SimContent.TicksPerSecond)
                {
                    Take(Civs[s.FallenTo], s, TakeChoice.Take);
                }

                continue;
            }

            if (Ticks >= s.SackedUntil && EnemyArmyNear(s, r.BlockadeRange))
            {
                s.EnemyNearTick = Ticks;
            }

            bool blockaded = s.EnemyNearTick >= 0 && Ticks - s.EnemyNearTick < r.LiftSeconds * SimContent.TicksPerSecond;
            if (blockaded && !s.Besieged)
            {
                BeginSiege(s);
            }
            else if (!blockaded && s.Besieged)
            {
                s.Besieged = false;
                s.SiegeStart = 0;
                s.Fighters = 0;
                s.EnemyNearTick = -1;
                Civs[s.Civ].Dirty = true;
                News.Add((s.Civ, $"The siege of {s.Name} is over."));
            }

            int max = GuardMax(s);
            if (s.Guard > max)
            {
                s.Guard = max;
            }
            else if (!s.Besieged && s.Guard < max)
            {
                s.GuardGrowth += (long)r.GuardRegrowPerMinute * SimContent.One / 60;
                while (s.GuardGrowth >= SimContent.One && s.Guard < max)
                {
                    s.Guard++;
                    s.GuardGrowth -= SimContent.One;
                }
            }
        }
    }

    private void BeginSiege(Settlement s)
    {
        s.Besieged = true;
        s.SiegeBeganTick = Ticks;
        s.SiegeStart = s.People;
        s.Fighters = s.People * Content.War.DefenderPercent / 100;
        Civs[s.Civ].Dirty = true; // it works less now
        int wait = Content.Siege.AssaultAfterTicks / SimContent.TicksPerSecond;
        News.Add((s.Civ, $"{s.Name} is under siege: no caravans come or go, it works at {Content.Siege.BlockadeWorkPercent}% and trains and builds nothing new."
            + (wait > 0 ? $" The assault can begin in {wait} s: send help." : "")));
    }

    // An army unit of a people at war with the settlement's within range of its centre.
    private bool EnemyArmyNear(Settlement s, int range)
    {
        foreach (Civ c in Civs)
        {
            if (c.Dead || c.Id == s.Civ || !AtWar(c.Id, s.Civ))
            {
                continue;
            }

            foreach (int id in c.Units)
            {
                Unit u = Units[id];
                UnitType t = Content.Units[u.Kind];
                if (t.Army && t.Strength > 0 && u.People > 0 && u.State is not (UnitState.Gone or UnitState.Training) && u.Aboard < 0
                    && Distance(u.Cell, s.Center) <= range)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Its own army units standing on its land still defend it.
    private bool Defended(Settlement s)
    {
        foreach (int id in Civs[s.Civ].Units)
        {
            Unit u = Units[id];
            if (Content.Units[u.Kind].Army && u.People > 0 && u.State is not (UnitState.Gone or UnitState.Training) && SettlementAt[u.Cell] == s.Id)
            {
                return true;
            }
        }

        return false;
    }

    // Guard, fighters and units are gone: the settlement falls; its taker chooses (the AI at once).
    private void Fall(Unit attacker, Settlement s)
    {
        s.FallenTo = attacker.Civ;
        s.FallenTick = Ticks;
        NewsAbout(attacker.Civ, s.Civ, $"{s.Name} fell to {Civs[attacker.Civ].Name}.");
        Civ taker = Civs[attacker.Civ];
        if (taker.IsAi)
        {
            Take(taker, s, Ai.SimAi.TakeChoiceFor(this, taker, s));
        }
        else
        {
            News.Add((taker.Id, $"{s.Name} is ours to decide: take it, sack it or raze it (taken in {Content.Siege.ChoiceSeconds} s)."));
        }
    }

    private string? TakeSettlement(Civ civ, int sid, TakeChoice choice)
    {
        if ((uint)sid >= (uint)Settlements.Count || Settlements[sid].FallenTo != civ.Id)
        {
            return "That settlement has not fallen to us.";
        }

        if (!Enum.IsDefined(choice))
        {
            return "Take, sack or raze.";
        }

        Take(civ, Settlements[sid], choice);
        return null;
    }

    private void Take(Civ winner, Settlement s, TakeChoice choice)
    {
        Civ loser = Civs[s.Civ];
        s.FallenTo = -1;
        s.Besieged = false;
        s.EnemyNearTick = -1;
        s.SiegeStart = 0;
        s.Fighters = 0;
        s.Wounds = 0;
        foreach (int id in winner.Units)
        {
            if (Units[id].TargetSettlement == s.Id)
            {
                Units[id].TargetSettlement = -1;
            }
        }

        Falls[(int)choice]++;
        switch (choice)
        {
            case TakeChoice.Sack:
                Sack(winner, s);
                break;
            case TakeChoice.Raze:
                Raze(winner, loser, s);
                break;
            default:
                Capture(winner, s);
                break;
        }

        loser.Dirty = true;
        winner.Dirty = true;
    }

    private void Sack(Civ winner, Settlement s)
    {
        Settlement? home = null;
        foreach (int sid in winner.Settlements)
        {
            Settlement o = Settlements[sid];
            if (home is null || Distance(o.Center, s.Center) < Distance(home.Center, s.Center))
            {
                home = o;
            }
        }

        // 2B (Patryk, 2026-11-20): the loot goes home in a caravan, which can be raided on the way.
        long total = 0;
        var load = new long[s.Stock.Length];
        for (int g = 0; g < s.Stock.Length; g++)
        {
            long loot = s.Stock[g] * Content.Siege.SackPercent / 100;
            s.Stock[g] -= loot;
            load[g] = loot;
            total += loot;
        }

        if (home is not null && total > 0)
        {
            for (int g = 0; g < load.Length; g++)
            {
                home.Stock[g] += load[g]; // the carriers take it from the store they start with, at the sacked settlement
            }

            Unit c = NewCarrier(winner, Content.Caravans, home, s.Center, load, 100);
            c.Job = CarrierJob.ToSettlement;
            c.Target = home.Id;
            Walk(c, NearestStore(home, s.Center));
        }

        s.SackedUntil = Ticks + (Content.Siege.SackImmuneSeconds * SimContent.TicksPerSecond);
        Scores()[(winner.Id * Civs.Count) + s.Civ] += 20;
        NewsAbout(winner.Id, s.Civ, home is null ? $"{winner.Name} sacked {s.Name}." : $"{winner.Name} sacked {s.Name}: a caravan carries {total / SimContent.One} goods to {home.Name}.");
    }

    private void Raze(Civ winner, Civ loser, Settlement s)
    {
        Settlement? refuge = null;
        foreach (int sid in loser.Settlements)
        {
            Settlement o = Settlements[sid];
            if (o != s && (refuge is null || Distance(o.Center, s.Center) < Distance(refuge.Center, s.Center)))
            {
                refuge = o;
            }
        }

        int fled = s.People;
        if (refuge is not null)
        {
            refuge.People += fled;
        }

        s.People = 0;
        for (int g = 0; g < s.Stock.Length; g++)
        {
            s.Stock[g] = s.Stock[g] * Content.RuinKeepPercent / 100; // 3B (Patryk, 2026-11-20): most of the store burns
        }

        Scores()[(winner.Id * Civs.Count) + loser.Id] += 30;
        NewsAbout(winner.Id, loser.Id, refuge is null
            ? $"{winner.Name} razed {s.Name}, the last home of {loser.Name}."
            : $"{winner.Name} razed {s.Name}; {fled} of its people fled to {refuge.Name}.");
        Ruin(loser, s, silent: true);
        if (loser.Settlements.Count == 0)
        {
            Perish(loser, winner);
        }
    }
}
