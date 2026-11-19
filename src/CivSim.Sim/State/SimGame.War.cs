using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>Declare war openly on another people.</summary>
public sealed record DeclareWarCommand(int Civ, int Other) : SimCommand(Civ);

/// <summary>Send an army unit to attack what stands on a hex (an enemy unit or settlement).</summary>
public sealed record AttackCommand(int Civ, int Unit, int Cell) : SimCommand(Civ);

/// <summary>
/// Offer peace. <paramref name="TributePercent"/> above 0: the other people pays that share of every good to us; below 0: we pay.
/// </summary>
public sealed record ProposePeaceCommand(int Civ, int Other, int TributePercent) : SimCommand(Civ);

/// <summary>Answer a peace offer from <paramref name="From"/>.</summary>
public sealed record AnswerPeaceCommand(int Civ, int From, bool Accept) : SimCommand(Civ);

/// <summary>Send a unit's people home: they join the settlement whose land the unit stands on.</summary>
public sealed record DisbandCommand(int Civ, int Unit) : SimCommand(Civ);

/// <summary>A peace offer waiting for an answer.</summary>
public sealed record PeaceOffer(int From, int To, int TributePercent, long Tick);

public sealed partial class SimGame
{
    private int[]? _war;
    private int[]? _score;
    private long[]? _truceUntil;

    /// <summary>Peace offers waiting for an answer.</summary>
    public List<PeaceOffer> PeaceOffers { get; } = [];

    /// <summary>True when the two peoples are at war.</summary>
    public bool AtWar(int a, int b) => a != b && War()[(a * Civs.Count) + b] != 0;

    /// <summary>War score of <paramref name="a"/> against <paramref name="b"/>: enemies killed plus settlements taken.</summary>
    public int Score(int a, int b) => Scores()[(a * Civs.Count) + b];

    /// <summary>The tribute in percent that the course of the war suggests <paramref name="b"/> pays <paramref name="a"/> (negative: a pays).</summary>
    public int SuggestedTribute(int a, int b) => Math.Clamp((Score(a, b) - Score(b, a)) / 5, -30, 30);

    private int[] War() => _war ??= new int[Civs.Count * Civs.Count];

    private int[] Scores() => _score ??= new int[Civs.Count * Civs.Count];

    private long[] Truces() => _truceUntil ??= new long[Civs.Count * Civs.Count];

    private void SetWar(int a, int b, bool war)
    {
        War()[(a * Civs.Count) + b] = war ? 1 : 0;
        War()[(b * Civs.Count) + a] = war ? 1 : 0;
    }

    private string? DeclareWar(Civ civ, int other, bool surprise)
    {
        if ((uint)other >= (uint)Civs.Count || other == civ.Id || Civs[other].Dead)
        {
            return "No such people.";
        }

        if (!Met(civ, Civs[other]) && !Met(Civs[other], civ) && !surprise)
        {
            return "We have not met this people.";
        }

        if (AtWar(civ.Id, other))
        {
            return "We are already at war with them.";
        }

        bool truce = Truces()[(civ.Id * Civs.Count) + other] > Ticks;
        bool treaty = Has(civ.Id, other, TreatyKind.Trade);
        SetWar(civ.Id, other, true);
        TreatiesOnWar(civ.Id, other);
        if (treaty)
        {
            civ.Reputation += Content.War.SurpriseReputation / 2; // breaking a treaty is remembered too
        }
        if (surprise || truce)
        {
            civ.Reputation += Content.War.SurpriseReputation;
            NewsAbout(civ.Id, other, $"{civ.Name} attacked {Civs[other].Name} {(surprise ? "without declaring war" : "breaking a truce")}.");
        }
        else
        {
            NewsAbout(civ.Id, other, $"{civ.Name} declared war on {Civs[other].Name}.");
        }

        return null;
    }

    private string? Attack(Civ civ, int id, int cell)
    {
        if ((uint)id >= (uint)Units.Count || Units[id].Civ != civ.Id || Units[id].State is UnitState.Gone or UnitState.Training)
        {
            return "Not your unit, or it is not ready.";
        }

        Unit u = Units[id];
        if (!Content.Units[u.Kind].Army)
        {
            return "Only soldiers attack.";
        }

        int enemyUnit = -1;
        foreach (Unit other in Units)
        {
            if (other.Cell == cell && other.Civ != civ.Id && other.State is UnitState.Idle or UnitState.Moving && Sees(civ, other))
            {
                enemyUnit = other.Id;
                break;
            }
        }

        int enemySettlement = SettlementAt[cell] >= 0 && Settlements[SettlementAt[cell]].Center == cell && Settlements[SettlementAt[cell]].Civ != civ.Id
            ? SettlementAt[cell] : -1;
        int victim = enemyUnit >= 0 ? Units[enemyUnit].Civ : enemySettlement >= 0 ? Settlements[enemySettlement].Civ : -1;
        if (victim < 0)
        {
            return "There is no enemy there.";
        }

        if (!AtWar(civ.Id, victim))
        {
            DeclareWar(civ, victim, surprise: true);
        }

        u.TargetUnit = enemyUnit;
        u.TargetSettlement = enemyUnit >= 0 ? -1 : enemySettlement;
        ChaseTarget(u);
        return null;
    }

    private string? ProposePeace(Civ civ, int other, int tribute)
    {
        if ((uint)other >= (uint)Civs.Count || !AtWar(civ.Id, other))
        {
            return "We are not at war with them.";
        }

        PeaceOffers.RemoveAll(o => o.From == civ.Id && o.To == other);
        PeaceOffers.Add(new PeaceOffer(civ.Id, other, Math.Clamp(tribute, -50, 50), Ticks));
        News.Add((other, $"{civ.Name} offer peace" + (tribute > 0 ? $" if we pay {tribute}% of our goods." : tribute < 0 ? $" and {-tribute}% of their goods." : ".")));
        return null;
    }

    private string? AnswerPeace(Civ civ, int from, bool accept)
    {
        PeaceOffer? offer = PeaceOffers.Find(o => o.From == from && o.To == civ.Id);
        if (offer is null)
        {
            return "There is no such offer.";
        }

        PeaceOffers.Remove(offer);
        if (!accept)
        {
            News.Add((from, $"{civ.Name} refused our peace."));
            return null;
        }

        MakePeace(Civs[from], civ, offer.TributePercent);
        return null;
    }

    /// <summary>Ends the war between two peoples; the tribute goes from <paramref name="b"/> to <paramref name="a"/> (negative: back).</summary>
    public void MakePeace(Civ a, Civ b, int tributePercent)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        Civ payer = tributePercent >= 0 ? b : a;
        Civ taker = tributePercent >= 0 ? a : b;
        int pct = Math.Abs(tributePercent);
        if (pct > 0 && payer.Settlements.Count > 0 && taker.Settlements.Count > 0)
        {
            // The tribute travels in a caravan from the payer's settlement nearest the taker.
            (Settlement from, Settlement to) = ClosestPair(payer, taker);
            var load = new long[Content.Goods.Count];
            for (int g = 0; g < load.Length; g++)
            {
                long total = 0;
                foreach (int sid in payer.Settlements)
                {
                    total += Settlements[sid].Stock[g];
                }

                load[g] = total * pct / 100;
                TakeFromCiv(payer, from, g, load[g]);
                load[g] = Math.Min(load[g], from.Stock[g]);
            }

            Unit c = NewCarrier(payer, Content.Caravans, from, NearestStore(from, to.Center), load, 100);
            c.Job = CarrierJob.ToSettlement;
            c.Target = to.Id;
            Walk(c, NearestStore(to, from.Center));
            SyncTotals(payer);
        }

        SetWar(a.Id, b.Id, false);
        PeaceMade(a.Id, b.Id);
        long truce = Ticks + (3 * SimContent.TicksPerMinute);
        Truces()[(a.Id * Civs.Count) + b.Id] = truce;
        Truces()[(b.Id * Civs.Count) + a.Id] = truce;
        foreach (Unit u in Units)
        {
            if ((u.Civ == a.Id && (u.TargetUnit >= 0 && Units[u.TargetUnit].Civ == b.Id || u.TargetSettlement >= 0 && Settlements[u.TargetSettlement].Civ == b.Id))
                || (u.Civ == b.Id && (u.TargetUnit >= 0 && Units[u.TargetUnit].Civ == a.Id || u.TargetSettlement >= 0 && Settlements[u.TargetSettlement].Civ == a.Id)))
            {
                u.TargetUnit = -1;
                u.TargetSettlement = -1;
            }
        }

        NewsAbout(a.Id, b.Id, $"{a.Name} and {b.Name} made peace" + (pct > 0 ? $"; {payer.Name} pay {pct}% of their goods." : "."));
    }

    private string? Disband(Civ civ, int id)
    {
        if ((uint)id >= (uint)Units.Count || Units[id].Civ != civ.Id || Units[id].State is UnitState.Gone or UnitState.Training)
        {
            return "Not your unit.";
        }

        Unit u = Units[id];
        int sid = SettlementAt[u.Cell];
        if (sid < 0 || Settlements[sid].Civ != civ.Id)
        {
            return "Units go home only on our own land.";
        }

        // B6 (2026-11-19 tests): arms and whatever it carries go back to the store, not only the people.
        Settlement s = Settlements[sid];
        s.People += u.People;
        if (Geared(Content.Units[u.Kind]))
        {
            ReturnGear(s.Stock, u);
        }

        if (u.Cargo is { } cargo)
        {
            for (int g = 0; g < cargo.Length && g < s.Stock.Length; g++)
            {
                s.Stock[g] += Math.Max(0, cargo[g]);
            }

            u.Cargo = null;
        }

        SyncTotals(civ);
        Unwatch(u);
        u.State = UnitState.Gone;
        civ.Units.Remove(u.Id);
        civ.Dirty = true;
        return null;
    }

    /// <summary>Fighting strength of a unit now, before luck: people × strength × terrain and upgrades.</summary>
    public long Power(Unit u)
    {
        ArgumentNullException.ThrowIfNull(u);
        UnitType t = Content.Units[u.Kind];
        int pct = 100;
        if ((t.BonusBiomes.Length > 0 && t.BonusBiomes[Map.Biome[u.Cell]]) || (t.BonusReliefs.Length > 0 && t.BonusReliefs[Map.Relief[u.Cell]]))
        {
            pct += t.BonusPercent;
        }

        Civ civ = Civs[u.Civ];
        foreach ((int tech, int up) in t.Upgrades)
        {
            if (civ.KnownTech[tech])
            {
                pct += up;
            }
        }

        pct += Bonus(civ, EffectKind.Strength, t.Key, t.Groups); // drill of each era
        return (long)u.People * t.Strength * pct * FedPercent(u) / 100 * WeaponPercent(u) / 100;
    }

    /// <summary>Defence of a settlement in percent from its finished fortifications.</summary>
    public int Defense(Settlement s)
    {
        ArgumentNullException.ThrowIfNull(s);
        int pct = 100;
        foreach (int id in Civs[s.Civ].Buildings)
        {
            Building b = Buildings[id];
            if (b.Settlement == s.Id && b.State == BuildingState.Active && Content.Buildings[b.Type].DefensePercent > 0)
            {
                BuildingType t = Content.Buildings[b.Type];
                pct += t.DefensePercent + Bonus(Civs[s.Civ], EffectKind.Defense, t.Key, t.Groups);
            }
        }

        return pct;
    }

    // News about two peoples reaches them and everyone who has met one of them.
    private void NewsAbout(int a, int b, string text)
    {
        foreach (Civ c in Civs)
        {
            if (c.Id == a || c.Id == b || Met(c, Civs[a]) || Met(c, Civs[b]))
            {
                News.Add((c.Id, text));
            }
        }
    }

    private int Distance(int a, int b) => Geo.Distance(Geo.FromIndex(a), Geo.FromIndex(b));

    // The nearest enemy within reach (ties: the lowest id), found by looking at the hexes in reach only.
    private Unit? NearestEnemy(Unit u, int reach)
    {
        Unit? best = null;
        int bestD = int.MaxValue;
        Span<HexCoord> area = stackalloc HexCoord[HexGlobe.RangeSize(reach)];
        int n = Geo.Range(Geo.FromIndex(u.Cell), reach, area);
        for (int k = 0; k < n; k++)
        {
            int cell = Geo.Index(area[k]);
            foreach (int id in UnitsAt(cell))
            {
                Unit other = Units[id];
                if (other.Civ == u.Civ || other.State is UnitState.Gone or UnitState.Training || !AtWar(u.Civ, other.Civ))
                {
                    continue;
                }

                int d = Distance(u.Cell, other.Cell);
                if (d < bestD || (d == bestD && best is not null && other.Id < best.Id))
                {
                    best = other;
                    bestD = d;
                }
            }
        }

        return best;
    }

    private long Luck(long damage) => damage * _combat.NextInt(Content.War.LowLuckPercent, Content.War.HighLuckPercent + 1) / 100;

    // Of the fallen, a share is wounded, not dead: they stay with the unit until herbalists heal them (shares add up, so one
    // fallen at a time still gives wounded).
    private void Wound(Unit u, int fallen)
    {
        u.InjuredShare += fallen * Content.InjuredPercent;
        u.Injured += u.InjuredShare / 100;
        u.InjuredShare %= 100;
    }

    // A unit was wiped out by another people (carriers lose their load).
    internal void Destroyed(Unit unit, int byCiv)
    {
        bool carrier = unit.Cargo is not null && Content.Units[unit.Kind].System;
        Gone(unit); // first: the site still counts this load as on the way
        LoseLoad(unit, byCiv);
        if (!carrier)
        {
            News.Add((unit.Civ, $"Our {Content.Units[unit.Kind].Name.ToLowerInvariant()} were destroyed by {Civs[byCiv].Name}."));
            News.Add((byCiv, $"We destroyed {Civs[unit.Civ].Name}' {Content.Units[unit.Kind].Name.ToLowerInvariant()}."));
        }
    }

    private void Siege(Unit attacker, Settlement target)
    {
        attacker.Fighting = true;
        target.Besieged = true;
        Fought(attacker.Civ);
        Fought(target.Civ);
        UnitType at = Content.Units[attacker.Kind];
        int siege = 100 + at.SiegePercent + (at.SiegePercent > 0 ? Bonus(Civs[attacker.Civ], EffectKind.Siege, at.Key, at.Groups) : 0);
        // Power is a second's damage; a step of battle is a part of a second.
        target.Wounds += Luck(Power(attacker)) * siege / 100 / 100 * Content.Combat.StepTicks / SimContent.TicksPerSecond;
        int dead = (int)Math.Min(target.People, target.Wounds / SimContent.One);
        if (dead > 0)
        {
            target.Wounds -= dead * SimContent.One;
            target.People -= dead;
            Civs[target.Civ].Dirty = true;
            Scores()[(attacker.Civ * Civs.Count) + target.Civ] += dead;
        }

        // The defenders strike back, raised by the walls.
        long defenders = (long)target.People * Content.War.DefenderPercent / 100;
        long back = defenders * Content.War.DefenderStrength * Defense(target);
        attacker.Wounds += Luck(back) / 100 * Content.Combat.StepTicks / SimContent.TicksPerSecond;
        int lost = (int)Math.Min(attacker.People, attacker.Wounds / SimContent.One);
        if (lost > 0)
        {
            attacker.Wounds -= lost * SimContent.One;
            attacker.People -= lost;
            Wound(attacker, lost);
            Scores()[(target.Civ * Civs.Count) + attacker.Civ] += lost;
            if (attacker.People <= 0)
            {
                Unwatch(attacker);
                attacker.State = UnitState.Gone;
                Civs[attacker.Civ].Units.Remove(attacker.Id);
                News.Add((attacker.Civ, $"Our {Content.Units[attacker.Kind].Name.ToLowerInvariant()} fell before {target.Name}."));
                return;
            }
        }

        if (target.People < Content.War.CaptureBelowPeople)
        {
            Capture(Civs[attacker.Civ], target);
        }
    }

    private void Capture(Civ winner, Settlement s)
    {
        Civ loser = Civs[s.Civ];
        loser.Settlements.Remove(s.Id);
        winner.Settlements.Add(s.Id);
        s.Civ = winner.Id;
        s.Wounds = 0;
        foreach (int cell in s.Cells)
        {
            OwnerAt[cell] = (short)winner.Id;
            Map.Owner.Set(cell, (short)winner.Id);
        }

        foreach (Building b in Buildings)
        {
            if (b.Settlement == s.Id && b.State != BuildingState.Removed)
            {
                loser.Buildings.Remove(b.Id);
                winner.Buildings.Add(b.Id);
                b.Civ = winner.Id;
                b.Priority = false;
            }
        }

        Scores()[(winner.Id * Civs.Count) + loser.Id] += 50;
        Watch(s); // the loser stops seeing from it, the winner starts
        winner.Dirty = true;
        loser.Dirty = true;
        NewsAbout(winner.Id, loser.Id, $"{winner.Name} took {s.Name} from {loser.Name}.");
        if (loser.Settlements.Count == 0)
        {
            loser.Dead = true;
            foreach (int uid in loser.Units)
            {
                Unwatch(Units[uid]);
                Units[uid].State = UnitState.Gone;
            }

            loser.Units.Clear();
            NewsAbout(loser.Id, winner.Id, $"{loser.Name} are no more.");
        }
    }

    // Walk toward the target until it is within reach.
    // The wall of the target's people nearest to the unit, within a few hexes of the goal (−1 = none).
    private int NearestEnemyWall(Unit u, int goal)
    {
        int best = -1;
        Span<HexCoord> area = stackalloc HexCoord[HexGlobe.RangeSize(7)];
        int n = Geo.Range(Geo.FromIndex(goal), 7, area);
        for (int k = 0; k < n; k++)
        {
            int c = Geo.Index(area[k]);
            if (WalledAgainst(u.Civ, c) && (best < 0 || Distance(u.Cell, c) < Distance(u.Cell, best)))
            {
                best = c;
            }
        }

        return best;
    }

    private void ChaseTarget(Unit u)
    {
        int goal = u.TargetUnit >= 0 ? Units[u.TargetUnit].Cell : u.TargetSettlement >= 0 ? Settlements[u.TargetSettlement].Center : -1;
        if (goal < 0 || Distance(u.Cell, goal) <= Content.Units[u.Kind].Range)
        {
            return;
        }

        // B2/B3 (2026-11-19 tests): walking on, a way that still ends within reach of the goal is kept (no new search
        // every half second); after a failed search the next one waits combat.chaseRetrySeconds, and after
        // combat.chaseGiveUp failures the unit gives the target up (its stance or guard finds another).
        int reach = Content.Units[u.Kind].Range;
        if (u.State == UnitState.Moving && u.PathIndex < u.Path.Count && Distance(u.Path[^1], goal) <= reach)
        {
            return;
        }

        if (Ticks - u.ChaseFailTick < Content.Combat.ChaseRetrySeconds * SimContent.TicksPerSecond)
        {
            return;
        }

        if (!PathFor(u, goal))
        {
            u.ChaseFailTick = Ticks;
            if (++u.ChaseFails >= Content.Combat.ChaseGiveUp && u.TargetUnit >= 0)
            {
                u.TargetUnit = -1;
                u.ChaseFails = 0;
                return;
            }

            // Walled in: break the wall nearest to us on the way.
            if (u.TargetSettlement >= 0 && NearestEnemyWall(u, goal) is int wall and >= 0)
            {
                AttackWall(Civs[u.Civ], u.Id, wall);
            }

            return;
        }

        u.ChaseFails = 0;

        // Stop short of the goal: as far as the unit's reach.
        int keep = Math.Max(0, u.Path.Count - Content.Units[u.Kind].Range);
        u.Path.RemoveRange(keep, u.Path.Count - keep);
        u.PathIndex = 0;
        if (u.Path.Count == 0)
        {
            u.State = UnitState.Idle;
            return;
        }

        bool wasMoving = u.State == UnitState.Moving;
        u.State = UnitState.Moving;
        if (!wasMoving)
        {
            u.NextTick = Ticks + StepTicks(u, u.Path[0]);
            ScheduleUnit(u, UnitStep);
        }
    }
}
