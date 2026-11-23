using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>Declare war openly on another people.</summary>
public sealed record DeclareWarCommand(int Civ, int Other) : SimCommand(Civ);

/// <summary>Send an army unit to attack what stands on a hex (an enemy unit or settlement).</summary>
public sealed record AttackCommand(int Civ, int Unit, int Cell) : SimCommand(Civ);

/// <summary>Offer peace on the given terms (ADR 0030): goods, maybe a settlement, and the length of peace.</summary>
public sealed record ProposePeaceCommand(int Civ, int Other, PeaceTerms Terms) : SimCommand(Civ);

/// <summary>Answer a peace offer from <paramref name="From"/>.</summary>
public sealed record AnswerPeaceCommand(int Civ, int From, bool Accept) : SimCommand(Civ);

/// <summary>Send a unit's people home: they join the settlement whose land the unit stands on.</summary>
public sealed record DisbandCommand(int Civ, int Unit) : SimCommand(Civ);

/// <summary>A peace offer waiting for an answer.</summary>
public sealed record PeaceOffer(int From, int To, PeaceTerms Terms, long Tick);

public sealed partial class SimGame
{
    private int[]? _war;
    private int[]? _score;
    private long[]? _warStart;
    private long[]? _truceUntil;

    /// <summary>Peace offers waiting for an answer.</summary>
    public List<PeaceOffer> PeaceOffers { get; } = [];

    /// <summary>True when the two peoples are at war.</summary>
    public bool AtWar(int a, int b) => a != b && War()[(a * Civs.Count) + b] != 0;

    /// <summary>War score of <paramref name="a"/> against <paramref name="b"/>: enemies killed plus settlements taken.</summary>
    public int Score(int a, int b) => Scores()[(a * Civs.Count) + b];

    private int[] War() => _war ??= new int[Civs.Count * Civs.Count];

    private int[] Scores() => _score ??= new int[Civs.Count * Civs.Count];

    private long[] Truces() => _truceUntil ??= new long[Civs.Count * Civs.Count];

    private void SetWar(int a, int b, bool war)
    {
        War()[(a * Civs.Count) + b] = war ? 1 : 0;
        War()[(b * Civs.Count) + a] = war ? 1 : 0;
        if (war)
        {
            Aggressors()[(a * Civs.Count) + b] = false;
            Aggressors()[(b * Civs.Count) + a] = false;

            // 1C (Patryk, 2026-11-20): the score of a war counts from its start, not over the whole game.
            Scores()[(a * Civs.Count) + b] = 0;
            Scores()[(b * Civs.Count) + a] = 0;
            WarStarts()[(a * Civs.Count) + b] = Ticks;
            WarStarts()[(b * Civs.Count) + a] = Ticks;
        }
    }

    private long[] WarStarts() => _warStart ??= new long[Civs.Count * Civs.Count];

    /// <summary>Tick the war between the two began (0 when it began at the start).</summary>
    public long WarStart(int a, int b) => WarStarts()[(a * Civs.Count) + b];

    // 6C (Patryk, 2026-11-20): reputation has a floor, so it keeps meaning something.
    private void AddReputation(Civ civ, int delta) => civ.Reputation = Math.Max(Content.War.ReputationFloor, civ.Reputation + delta);

    /// <summary>True while a peace made between the two peoples still runs (breaking it costs more reputation).</summary>
    public bool InTruce(int a, int b) => a != b && Truces()[(a * Civs.Count) + b] > Ticks;

    // P2 (Patryk, 2026-11-23): reputation is renewable. Once a minute a bad name heals by war.reputationRegenPerMinute (up to 0)
    // for every people that is not the aggressor of a war going on now.
    private void ReputationStep()
    {
        int regen = Content.War.ReputationRegenPerMinute;
        if (regen <= 0)
        {
            return;
        }

        foreach (Civ c in Civs)
        {
            if (c.Dead || c.Reputation >= 0)
            {
                continue;
            }

            bool aggressor = false;
            for (int o = 0; o < Civs.Count && !aggressor; o++)
            {
                aggressor = o != c.Id && AtWar(c.Id, o) && Aggressor(c.Id, o);
            }

            if (!aggressor)
            {
                c.Reputation = Math.Min(0, c.Reputation + regen);
            }
        }
    }

    /// <summary>Wars a people is in now.</summary>
    public int WarsOf(int civ)
    {
        int n = 0;
        for (int o = 0; o < Civs.Count; o++)
        {
            n += o != civ && !Civs[o].Dead && AtWar(civ, o) ? 1 : 0;
        }

        return n;
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

        if (WarsOf(civ.Id) >= Content.War.MaxWars)
        {
            return $"We already fight {Content.War.MaxWars} wars: make peace somewhere first.";
        }

        if (WarsOf(other) >= Content.War.MaxWars)
        {
            return $"{Civs[other].Name} already fight {Content.War.MaxWars} wars.";
        }

        bool truce = Truces()[(civ.Id * Civs.Count) + other] > Ticks;
        int breakPct = truce && TruceBreaks()[(civ.Id * Civs.Count) + other] > 0 ? TruceBreaks()[(civ.Id * Civs.Count) + other] : 100;
        bool treaty = Has(civ.Id, other, TreatyKind.Trade);
        SetWar(civ.Id, other, true);
        MarkAggressor(civ.Id, other); // 7.3A
        TreatiesOnWar(civ.Id, other);
        if (treaty)
        {
            AddReputation(civ, Content.War.SurpriseReputation / 2); // breaking a treaty is remembered too
        }
        if (surprise || truce)
        {
            AddReputation(civ, Content.War.SurpriseReputation * (truce ? breakPct : 100) / 100); // 3B: a long peace broken costs more
            NewsAbout(civ.Id, other, $"{civ.Name} attacked {Civs[other].Name} {(surprise ? "without declaring war" : "breaking a truce")}.");
        }
        else
        {
            AddReputation(civ, Content.War.DeclareReputation); // E1: no free wars
            NewsAbout(civ.Id, other, $"{civ.Name} declared war on {Civs[other].Name}.");
        }

        return null;
    }

    private readonly List<int> _attackWay = [];

    private string? Attack(Civ civ, int id, int cell)
    {
        if ((uint)id >= (uint)Units.Count || Units[id].Civ != civ.Id || Units[id].State is UnitState.Gone or UnitState.Training)
        {
            return "Not our unit, or it is not ready.";
        }

        Unit u = Units[id];
        if (!Content.Units[u.Kind].Army)
        {
            return "Only soldiers attack.";
        }

        if ((uint)cell >= (uint)Geo.CellCount)
        {
            return "Outside the map.";
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

        // A8 (tests 2026-11-20): a settlement we neither see nor remember is not there for us (the same answer as an empty hex,
        // so clicking around finds no hidden city).
        if (enemySettlement >= 0 && !Visible(civ, cell)
            && !(civ.SeenSettlements.TryGetValue(cell, out SettlementMemory? seen) && seen.Settlement == enemySettlement))
        {
            enemySettlement = -1;
        }

        int victim = enemyUnit >= 0 ? Units[enemyUnit].Civ : enemySettlement >= 0 ? Settlements[enemySettlement].Civ : -1;
        if (victim < 0)
        {
            return "There is no enemy there.";
        }

        // A7 (tests 2026-11-20): refused at once when no way leads there (walls do not count: they can be broken).
        if (u.Aboard < 0 && Distance(u.Cell, cell) > Content.Units[u.Kind].Range
            && !FindSharedPath(u.Cell, cell, _attackWay, Content.Units[u.Kind].Naval, civ.Id, ignoreWalls: true))
        {
            return "No way there: a river or the sea is in the way.";
        }

        if (!AtWar(civ.Id, victim) && DeclareWar(civ, victim, surprise: true) is string refused)
        {
            return refused;
        }

        u.TargetUnit = enemyUnit;
        u.TargetSettlement = enemyUnit >= 0 ? -1 : enemySettlement;
        ChaseTarget(u);
        return null;
    }

    private string? ProposePeace(Civ civ, int other, PeaceTerms terms)
    {
        if ((uint)other >= (uint)Civs.Count || !AtWar(civ.Id, other))
        {
            return "We are not at war with them.";
        }

        if (terms is null)
        {
            return "Name the terms.";
        }

        if (TermsRefused(civ, Civs[other], terms) is string refused)
        {
            return refused;
        }

        PeaceOffers.RemoveAll(o => o.From == civ.Id && o.To == other);
        PeaceOffers.Add(new PeaceOffer(civ.Id, other, terms, Ticks));
        News.Add((other, $"{civ.Name} offer {TermsText(terms, civ.Id, other)}."));
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

        // The goods named may have gone in the meantime: what is still there is given (MakePeace takes at most the store).
        MakePeace(Civs[from], civ, offer.Terms);
        return null;
    }

    /// <summary>Ends the war between two peoples with a white peace of the given length (tests and scenarios).</summary>
    public void MakePeace(Civ a, Civ b, int length = 0)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        MakePeace(a, b, PeaceTerms.White(length));
    }

    private string? Disband(Civ civ, int id)
    {
        if ((uint)id >= (uint)Units.Count || Units[id].Civ != civ.Id || Units[id].State is UnitState.Gone or UnitState.Training)
        {
            return "Not our unit.";
        }

        Unit u = Units[id];
        UnitType kind = Content.Units[u.Kind];
        int sid = SettlementAt[u.Cell];
        if (sid < 0 || Settlements[sid].Civ != civ.Id || Settlements[sid].Abandoned)
        {
            return "Units go home only on our own land.";
        }

        if (u.Fighting)
        {
            return "Not while it fights.";
        }

        // P5 (Patryk, 2026-11-23): soldiers may join a settlement too (to start again when it is all that is left), but they
        // lose everything the unit had: its arms and armour, its supplies and its training. Other units bring their gear home
        // (B6, 2026-11-19 tests). The wounded come home with them.
        Settlement s = Settlements[sid];
        s.People += u.People + u.Injured;
        if (!kind.Army && Geared(kind))
        {
            ReturnGear(s.Stock, u);
        }

        if (u.Cargo is { } cargo)
        {
            // B7 (tests 2026-11-23): what does not fit in the store is lost.
            for (int g = 0; g < cargo.Length && g < s.Stock.Length; g++)
            {
                long room = Math.Max(0, s.Capacity[g] - s.Stock[g]);
                s.Stock[g] += Math.Min(room, Math.Max(0, cargo[g]));
            }

            u.Cargo = null;
        }

        if (kind.Army)
        {
            News.Add((civ.Id, $"Our {kind.Name.ToLowerInvariant()} laid down their arms and joined {s.Name}."));
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
        pct += VetStrength(u);
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
        if (unit.ForUpgrade && (uint)unit.Target < (uint)Settlements.Count && Settlements[unit.Target].UpgradeWaiting)
        {
            Settlements[unit.Target].UpgradeWaiting = false; // G6: raided on the way, the upgrade does not happen
            News.Add((unit.Civ, $"The caravan with goods for {Settlements[unit.Target].Name} was raided: it does not grow this time."));
        }

        if (carrier)
        {
            AddWeariness(unit.Civ, byCiv, Content.Weariness.CaravanRaided * 100L);
        }

        Gone(unit); // first: the site still counts this load as on the way
        LoseLoad(unit, byCiv);
        if (!carrier)
        {
            News.Add((unit.Civ, $"Our {Content.Units[unit.Kind].Name.ToLowerInvariant()} were destroyed by {Civs[byCiv].Name}."));
            News.Add((byCiv, $"We destroyed {Civs[unit.Civ].Name}' {Content.Units[unit.Kind].Name.ToLowerInvariant()}."));
        }
    }

    // Siege (2026-11-20): the blows fall on the guard first, then on the fighting residents (never below the floor); the
    // guard and the fighters strike back, raised by the walls. With nobody left to defend it the settlement falls.
    private void Siege(Unit attacker, Settlement target)
    {
        if (target.FallenTo >= 0 || Ticks < target.SackedUntil)
        {
            return;
        }

        attacker.Fighting = true;
        target.EnemyNearTick = Ticks;
        if (!target.Besieged)
        {
            BeginSiege(target);
        }

        if (Ticks - target.SiegeBeganTick < Content.Siege.AssaultAfterTicks)
        {
            return; // 13B (Patryk, 2026-11-20): first the blockade, the assault after siege.assaultAfterSeconds
        }

        Fought(attacker.Civ);
        Fought(target.Civ);
        UnitType at = Content.Units[attacker.Kind];
        SiegeRules r = Content.Siege;
        int siege = 100 + at.SiegePercent + (at.SiegePercent > 0 ? Bonus(Civs[attacker.Civ], EffectKind.Siege, at.Key, at.Groups) : 0);
        // Power is a second's damage; a step of battle is a part of a second.
        target.Wounds += Luck(Power(attacker)) * siege / 100 / 100 * Content.Combat.StepTicks / SimContent.TicksPerSecond;
        int dead = (int)(target.Wounds / SimContent.One);
        target.Wounds -= dead * SimContent.One;
        int guard = Math.Min(dead, target.Guard);
        target.Guard -= guard;
        dead -= guard;
        int floor = target.SiegeStart * r.ResidentsFloorPercent / 100;
        int residents = Math.Min(dead, Math.Min(target.Fighters, Math.Max(0, target.People - floor)));
        if (residents > 0)
        {
            target.People -= residents;
            target.Idle = Math.Min(target.Idle, target.People); // B6 (tests 2026-11-23): never more idle than people until the next count
            target.Fighters -= residents;
            Civs[target.Civ].Dirty = true;
        }

        if (guard + residents > 0)
        {
            Scores()[(attacker.Civ * Civs.Count) + target.Civ] += guard + residents;
            AddLoss(target.Civ, attacker.Civ, guard + residents);
            Credit(attacker.Civ, attacker.Kind, guard + residents);
        }

        // The guard and the fighting residents strike back, raised by the walls.
        long back = (((long)target.Guard * r.GuardStrength) + ((long)target.Fighters * Content.War.DefenderStrength)) * target.DefensePct;
        attacker.Wounds += Luck(back) / 100 * Content.Combat.StepTicks / SimContent.TicksPerSecond;
        int lost = (int)Math.Min(attacker.People, attacker.Wounds / SimContent.One);
        if (lost > 0)
        {
            attacker.Wounds -= lost * SimContent.One;
            attacker.People -= lost;
            Wound(attacker, lost);
            Scores()[(target.Civ * Civs.Count) + attacker.Civ] += lost;
            AddLoss(attacker.Civ, target.Civ, lost);
            if (attacker.People <= 0)
            {
                Unwatch(attacker);
                attacker.State = UnitState.Gone;
                Civs[attacker.Civ].Units.Remove(attacker.Id);
                News.Add((attacker.Civ, $"Our {Content.Units[attacker.Kind].Name.ToLowerInvariant()} fell before {target.Name}."));
                return;
            }
        }

        if (target.Guard == 0 && (target.Fighters <= 0 || target.People <= floor) && !Defended(target))
        {
            Fall(attacker, target);
        }
    }

    private void Capture(Civ winner, Settlement s, bool byPeace = false)
    {
        Civ loser = Civs[s.Civ];
        AddWeariness(loser.Id, winner.Id, Content.Weariness.Taken * 100L);
        loser.Settlements.Remove(s.Id);
        winner.Settlements.Add(s.Id);
        s.Civ = winner.Id;
        AdoptWorks(s, loser.Id);
        s.Wounds = 0;
        s.Guard = 0; // the garrison of the old owner is gone
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

        if (!byPeace)
        {
            Scores()[(winner.Id * Civs.Count) + loser.Id] += 50;
        }

        Watch(s); // the loser stops seeing from it, the winner starts
        winner.Dirty = true;
        loser.Dirty = true;
        NewsAbout(winner.Id, loser.Id, byPeace ? $"{s.Name} passes from {loser.Name} to {winner.Name} by the peace." : $"{winner.Name} took {s.Name} from {loser.Name}.");
        if (loser.Settlements.Count == 0)
        {
            Perish(loser, winner);
        }
    }

    // A people with no settlement left is no more (P5, 2026-11-23: or with nobody left; then there is no winner).
    private void Perish(Civ loser, Civ? winner)
    {
        loser.Dead = true;
        DisownWorks(System.Linq.Enumerable.Range(0, Geo.CellCount), loser.Id); // A6: its walls stop nobody
        foreach (int uid in loser.Units)
        {
            Unwatch(Units[uid]);
            Units[uid].State = UnitState.Gone;
        }

        loser.Units.Clear();
        loser.People = 0; // B5 (tests 2026-11-23)
        NewsAbout(loser.Id, winner?.Id ?? loser.Id, winner is null ? $"{loser.Name} died out: nobody of them is left." : $"{loser.Name} are no more.");
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

        if (!FindSharedPath(u.Cell, goal, _way, Content.Units[u.Kind].Naval, u.Civ))
        {
            u.ChaseFailTick = Ticks;

            // Walled in: break the wall nearest to us on the way.
            if (u.TargetSettlement >= 0 && NearestEnemyWall(u, goal) is int wall and >= 0)
            {
                AttackWall(Civs[u.Civ], u.Id, wall);
                return;
            }

            if (++u.ChaseFails >= Content.Combat.ChaseGiveUp)
            {
                // Tick spikes (2026-11-20): a settlement out of reach (over the sea) is given up too, not searched for good.
                if (u.TargetUnit < 0 && u.TargetSettlement >= 0)
                {
                    News.Add((u.Civ, $"Our {Content.Units[u.Kind].Name.ToLowerInvariant()} find no way to {Settlements[u.TargetSettlement].Name} and wait for orders."));
                }

                u.TargetUnit = -1;
                u.TargetSettlement = -1;
                u.ChaseFails = 0;
            }

            return;
        }

        u.ChaseFails = 0;
        u.Path.Clear();
        u.Path.AddRange(_way);

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
