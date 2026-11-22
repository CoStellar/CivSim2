using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;
using CivSim.Sim.State;

namespace CivSim.Sim.Ai;

// The AI's war: soldiers, wars, field battles, offensives and the way to the target (split from SimAi.cs, 2026-11-22).
public static partial class SimAi
{
    private static UnitType? BestSoldier(SimGame g, Civ civ)
    {
        UnitType? best = null;
        foreach (UnitType t in g.Content.Units)
        {
            if (t.Army && !t.Levy && !t.Naval && SimGame.Knows(civ, t) && (best is null || t.Strength > best.Strength))
            {
                best = t;
            }
        }

        return best;
    }

    private static void Army(SimGame g, Civ civ)
    {
        int soldiers = 0;
        foreach (int id in civ.Units)
        {
            Unit u = g.Units[id];
            soldiers += g.Content.Units[u.Kind].Army ? u.People : 0;
        }

        int want = civ.People * (5 + (civ.Aggression / 5)) / 100;
        if (soldiers >= want || NetFood(g, civ) < 2 * SimContent.One || Small(g, civ))
        {
            return;
        }

        UnitType? t = BestSoldier(g, civ);
        if (t is null || !Affordable(civ, t.Price, 20))
        {
            return;
        }

        // Rations: the settlement that trains them feeds them, so the one with the most food to spare trains them, and only if
        // it can feed them as well as its own people.
        Settlement? best = null;
        foreach (int sid in civ.Settlements)
        {
            Settlement s = g.Settlements[sid];
            if (s.People >= t.People + 15 && s.Rate[g.Content.Food] >= t.People * g.Content.FoodPerPersonPerMinute
                && (best is null || s.Rate[g.Content.Food] > best.Rate[g.Content.Food]))
            {
                best = s;
            }
        }

        if (best is not null)
        {
            g.Apply(new TrainCommand(civ.Id, best.Id, t.Id));
        }
    }

    private static void War(SimGame g, Civ civ)
    {
        long power = 0;
        var idle = new List<Unit>();
        var fresh = new List<Unit>();
        AiBattleRules rules = g.Content.AiBattle;
        foreach (int id in civ.Units)
        {
            Unit u = g.Units[id];
            if (!g.Content.Units[u.Kind].Army || u.State == UnitState.Training || g.Content.Units[u.Kind].Naval || u.Aboard >= 0)
            {
                continue;
            }

            // Roadmap 7 (2026-11-21): a badly hurt unit at war goes home to heal and guards it; it does not go on campaign.
            bool hurt = Hurt(u, rules);
            if (hurt && g.OwnerAt[u.Cell] != civ.Id && AtWarAny(g, civ) && Home(g, civ, u) is int home && !(u.State == UnitState.Moving && u.Path.Count > 0 && u.Path[^1] == home))
            {
                g.Apply(new MoveCommand(civ.Id, u.Id, home, false));
                continue;
            }

            power += g.Power(u);
            if (u.TargetSettlement < 0 && u.TargetUnit < 0 && !(u.State == UnitState.Moving && g.OwnerAt[u.Cell] != civ.Id && hurt))
            {
                idle.Add(u);
                if (!hurt)
                {
                    fresh.Add(u);
                }
            }
        }

        // Field battle (roadmap 7): an enemy army close to one of our soldiers is fought before anything else, all of our
        // soldiers near it on the same enemy, so they surround it (flank).
        if (FieldBattle(g, civ, rules))
        {
            return;
        }

        // Defend: enemies seen on our land are attacked first.
        foreach (Unit enemy in g.ActiveUnits())
        {
            if (enemy.Civ == civ.Id || !g.Content.Units[enemy.Kind].Army)
            {
                continue;
            }

            int owner = g.OwnerAt[enemy.Cell];
            if (owner == civ.Id && g.AtWar(civ.Id, enemy.Civ) && g.Sees(civ, enemy))
            {
                foreach (Unit u in idle)
                {
                    g.Apply(new AttackCommand(civ.Id, u.Id, enemy.Cell));
                }

                return;
            }
        }

        foreach (Civ other in g.Civs)
        {
            if (other.Id == civ.Id || other.Dead || !g.Met(civ, other))
            {
                continue;
            }

            bool atWar = g.AtWar(civ.Id, other.Id);
            if (!atWar && (civ.Aggression < 40 || idle.Count == 0 || g.Ticks < (long)g.Content.AiPeacefulStartMinutes * SimContent.TicksPerMinute
                || g.WarsOf(civ.Id) >= g.Content.War.MaxWars || g.WarsOf(other.Id) >= g.Content.War.MaxWars
                || g.Has(civ.Id, other.Id, TreatyKind.Alliance) || (g.Has(civ.Id, other.Id, TreatyKind.Trade) && civ.Aggression < 85)))
            {
                continue; // A3 (tests 2026-11-20): no war to start, so no search for a target
            }

            Settlement? target = atWar && (idle.Count == 0 || power <= 0) ? null : NearestKnownSettlement(g, civ, other);
            if (atWar)
            {
                // Peace when the war has turned against us or dragged on; the course of the war sets the terms.
                int mine = g.Score(civ.Id, other.Id);
                int theirs = g.Score(other.Id, civ.Id);
                if ((theirs > mine + 20 || g.AiRandom.NextInt(100) < 3) && !g.PeaceOffers.Exists(o => o.From == civ.Id && o.To == other.Id))
                {
                    bool early = g.Ticks - g.WarStart(civ.Id, other.Id) < g.Content.War.TributeAfterTicks; // 1C: no tribute yet
                    g.Apply(new ProposePeaceCommand(civ.Id, other.Id, early ? 0 : g.SuggestedTribute(civ.Id, other.Id)));
                }

                if (target is not null && fresh.Count > 0 && power > 0)
                {
                    Offensive(g, civ, fresh, target, rules);
                }

                continue;
            }

            // Start a war only after the first minutes, when strong enough against the target's defenders and warlike enough;
            // allies are left alone, trade partners only by the most warlike.
            if (target is null)
            {
                continue;
            }

            // A9 (tests 2026-11-20): the defence as we see it now, or as we last saw it.
            (int people, int guard, int defensePct) = g.Visible(civ, target.Center)
                ? (target.People, target.Guard, g.Defense(target))
                : civ.SeenSettlements.TryGetValue(target.Center, out SettlementMemory? seen) ? (seen.People, seen.Guard, seen.DefensePercent) : (target.People, target.Guard, g.Defense(target));
            long defence = (((long)people * g.Content.War.DefenderPercent / 100 * g.Content.War.DefenderStrength) + ((long)guard * g.Content.Siege.GuardStrength)) * defensePct;
            int warlike = civ.Reputation < g.Content.War.BadReputation ? civ.Aggression / 20 : civ.Aggression / 10; // 6C: a bad name, half as often
            if (power > defence * 2 && g.AiRandom.NextInt(100) < warlike)
            {
                if (civ.Aggression < 75)
                {
                    g.Apply(new DeclareWarCommand(civ.Id, other.Id));
                }

                Offensive(g, civ, fresh.Count > 0 ? fresh : idle, target, rules);
                return;
            }
        }
    }

    // A unit at war that lost too many: more than injuredPercent of its people wounded, or fewer than sizePercent of its size.
    private static bool Hurt(Unit u, AiBattleRules r) =>
        u.People > 0 && ((long)u.Injured * 100 >= (long)u.People * r.RetreatInjuredPercent || (u.Size > 0 && (long)u.People * 100 < (long)u.Size * r.RetreatSizePercent));

    private static bool AtWarAny(SimGame g, Civ civ)
    {
        foreach (Civ other in g.Civs)
        {
            if (other.Id != civ.Id && !other.Dead && g.AtWar(civ.Id, other.Id))
            {
                return true;
            }
        }

        return false;
    }

    // The centre of the unit's home settlement, or of our settlement nearest to it.
    private static int? Home(SimGame g, Civ civ, Unit u)
    {
        if (u.Home >= 0 && g.Settlements[u.Home].Civ == civ.Id && !g.Settlements[u.Home].Abandoned)
        {
            return g.Settlements[u.Home].Center;
        }

        int best = -1;
        int bestDist = int.MaxValue;
        foreach (int sid in civ.Settlements)
        {
            int d = Hexes(g, g.Settlements[sid].Center, u.Cell);
            if (d < bestDist)
            {
                bestDist = d;
                best = g.Settlements[sid].Center;
            }
        }

        return best >= 0 ? best : null;
    }

    // Roadmap 7 (2026-11-21): a seen enemy army within engageHexes of a fresh soldier of ours is attacked by every fresh soldier
    // within engageHexes of it: the same target from several sides is the flank. Soldiers already on it are left alone.
    private static bool FieldBattle(SimGame g, Civ civ, AiBattleRules r)
    {
        bool any = false;
        foreach (int id in civ.Units)
        {
            Unit u = g.Units[id];
            UnitType t = g.Content.Units[u.Kind];
            if (!t.Army || t.Naval || u.State == UnitState.Training || u.Aboard >= 0 || Hurt(u, r) || u.TargetUnit >= 0)
            {
                continue;
            }

            Unit? enemy = null;
            int best = r.EngageHexes + 1;
            foreach (Unit e in g.ActiveUnits())
            {
                if (e.Civ == civ.Id || e.Aboard >= 0 || !g.Content.Units[e.Kind].Army || g.Content.Units[e.Kind].Naval || !g.AtWar(civ.Id, e.Civ))
                {
                    continue;
                }

                int d = Hexes(g, u.Cell, e.Cell);
                if (d < best && g.Sees(civ, e))
                {
                    best = d;
                    enemy = e;
                }
            }

            if (enemy is not null && g.Apply(new AttackCommand(civ.Id, u.Id, enemy.Cell)) is null)
            {
                any = true;
            }
        }

        return any;
    }

    // Roadmap 7 (2026-11-21): an offensive starts from a muster: our settlement nearest the target. Soldiers farther than
    // musterHexes from it gather there in formation first (melee in front, archers behind, riders on the wings); once
    // musterPercent of the power stands there (or others are already at the target), all march on the target.
    private static void Offensive(SimGame g, Civ civ, List<Unit> units, Settlement target, AiBattleRules r)
    {
        if (units.Count == 0)
        {
            return;
        }

        bool underway = false;
        foreach (int id in civ.Units)
        {
            underway |= g.Units[id].TargetSettlement == target.Id;
        }

        int muster = -1;
        int bestDist = int.MaxValue;
        foreach (int sid in civ.Settlements)
        {
            int d = Hexes(g, g.Settlements[sid].Center, target.Center);
            if (d < bestDist)
            {
                bestDist = d;
                muster = g.Settlements[sid].Center;
            }
        }

        long all = 0;
        long there = 0;
        int coming = 0;
        var away = new List<int>();
        foreach (Unit u in units)
        {
            long p = g.Power(u);
            all += p;
            int d = muster < 0 ? 0 : Hexes(g, u.Cell, muster);
            if (d <= r.MusterHexes)
            {
                there += p;
            }
            else if (d <= r.MusterFromHexes && u.State != UnitState.Moving)
            {
                away.Add(u.Id);
            }
            else if (d <= r.MusterFromHexes)
            {
                coming++;
            }
        }

        if (underway || muster < 0 || there * 100 >= all * r.MusterPercent || (away.Count == 0 && coming == 0))
        {
            foreach (Unit u in units)
            {
                g.Apply(new AttackCommand(civ.Id, u.Id, target.Center));
            }

            return;
        }

        if (away.Count > 0)
        {
            g.Apply(new FormationCommand(civ.Id, [.. away], muster));
        }
    }

    /// <summary>
    /// The bonus the AI picks for a level of a kind of unit (2026-11-20): archers and machines hit harder, cavalry marches faster,
    /// the infantry of the front alternates more damage and a shield wall.
    /// </summary>
    public static int VeteranChoiceFor(SimGame g, Civ civ, int kind)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(civ);
        VeteranOption[] options = g.Content.Veterans.Options;
        UnitType t = g.Content.Units[kind];
        string want = t.Ranged || t.SiegePercent > 0 ? "hardened"
            : Array.IndexOf(t.Groups, "cavalry") >= 0 ? (civ.KindLevel[kind] % 2 == 1 ? "forced_march" : "hardened")
            : civ.KindLevel[kind] % 2 == 1 ? "hardened" : "shield_wall";
        int pick = Array.FindIndex(options, o => o.Key == want);
        return pick >= 0 ? pick : 0;
    }

    /// <summary>
    /// What the AI does with a settlement that fell to it (2026-11-20): it takes one within siege.aiTakeWithinHexes (12) of its own, sacks one farther away (it could
    /// not hold it), and razes it only when it is the loser's last home.
    /// </summary>
    public static TakeChoice TakeChoiceFor(SimGame g, Civ civ, Settlement s)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(s);
        if (g.Civs[s.Civ].Settlements.Count <= 1 && civ.Aggression >= 75)
        {
            return TakeChoice.Raze;
        }

        int nearest = int.MaxValue;
        foreach (int sid in civ.Settlements)
        {
            nearest = Math.Min(nearest, g.Geo.Distance(g.Geo.FromIndex(g.Settlements[sid].Center), g.Geo.FromIndex(s.Center)));
        }

        return nearest <= g.Content.Siege.AiTakeWithinHexes ? TakeChoice.Take : TakeChoice.Sack; // 2B: 12 hexes (was 20)
    }

    // Tick spikes (2026-11-20): armies sent to a settlement beyond the sea searched a way twice a second for good. The AI
    // remembers for two minutes which settlements its armies can reach from home. A3 (tests 2026-11-20): the answer is kept per
    // target settlement and searched from our first settlement, not from the army's hex (it moves, so the old key never hit).
    private static bool WayTo(SimGame g, Civ civ, Settlement s)
    {
        int from = g.Settlements[civ.Settlements[0]].Center;
        long key = ((long)s.Id * g.Geo.CellCount) + from;
        if (civ.AiWayTo.TryGetValue(key, out (bool Way, long Tick) known) && g.Ticks - known.Tick < 2 * SimContent.TicksPerMinute)
        {
            return known.Way;
        }

        if (civ.AiWayTo.Count > 256)
        {
            civ.AiWayTo.Clear(); // answers about settlements long gone
        }

        bool way = g.FindPath(from, s.Center, WayScratch, false, civ.Id, 20_000);
        civ.AiWayTo[key] = (way, g.Ticks);
        return way;
    }

    [ThreadStatic]
    private static List<int>? _wayScratch;

    private static List<int> WayScratch => _wayScratch ??= [];

    // A9 (tests 2026-11-20): only settlements the people sees now or remembers having seen, not every hex it once uncovered.
    private static Settlement? NearestKnownSettlement(SimGame g, Civ civ, Civ other)
    {
        Settlement? best = null;
        int bestD = int.MaxValue;
        HexCoord home = g.Geo.FromIndex(g.Settlements[civ.Settlements[0]].Center);
        foreach (int sid in other.Settlements)
        {
            Settlement s = g.Settlements[sid];
            bool known = g.Visible(civ, s.Center) || (civ.SeenSettlements.TryGetValue(s.Center, out SettlementMemory? m) && m.Settlement == s.Id && m.Civ == other.Id);
            if (!known)
            {
                continue;
            }

            int d = g.Geo.Distance(home, g.Geo.FromIndex(s.Center));
            if (d < bestD && WayTo(g, civ, s))
            {
                best = s;
                bestD = d;
            }
        }

        return best;
    }
}
