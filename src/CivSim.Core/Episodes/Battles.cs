using System;
using System.Collections.Generic;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Random;
using CivSim.Core.State;

namespace CivSim.Core.Episodes;

/// <summary>Defence bonus of one biome (data/war.json battle.terrain.biomes).</summary>
/// <param name="Biome">Biome id.</param>
/// <param name="Percent">Extra strength of the defenders there, percent.</param>
public readonly record struct BiomeDefence(int Biome, int Percent);

/// <summary>Rules of battles in rounds (data/war.json battle; GDD 9.3, stage 4e PR 4).</summary>
public sealed record BattleRules
{
    /// <summary>Most rounds of a battle; when nobody breaks by then, the defenders hold.</summary>
    public int Rounds { get; init; } = 6;

    /// <summary>
    /// Ticks between two rounds of a battle a campaign fights (stage 6, decision of 2026-11-09: a round a second at x1),
    /// so the battle can be seen on the map. 0 fights all rounds at once.
    /// </summary>
    public int RoundTicks { get; init; } = 10;

    /// <summary>Men a side kills per round, percent of its armed strength in warriors (100 = one stone-spear warrior).</summary>
    public int CasualtyPercent { get; init; } = 10;

    /// <summary>Luck of a side in a round: its kills are multiplied by a number drawn from this range, percent.</summary>
    public (int Min, int Max) RollPercent { get; init; } = (50, 150);

    /// <summary>Morale a side loses per percent of its men lost in a round.</summary>
    public int MoralePerLossPercent { get; init; } = 3;

    /// <summary>A side whose morale falls below this breaks and flees.</summary>
    public int BreakMorale { get; init; } = 40;

    /// <summary>Morale of defenders at the start of a battle (they defend their homes).</summary>
    public int DefenderMorale { get; init; } = 100;

    /// <summary>Strength of a hungry army (no food left), percent.</summary>
    public int HungryStrengthPercent { get; init; } = 75;

    /// <summary>The side that breaks loses this share of its remaining men while fleeing, percent.</summary>
    public int RoutLossPercent { get; init; } = 10;

    /// <summary>Extra strength of defenders in hills, percent.</summary>
    public int HillsPercent { get; init; } = 25;

    /// <summary>Extra strength of defenders in mountains, percent.</summary>
    public int MountainsPercent { get; init; } = 50;

    /// <summary>Extra strength of defenders behind a river (on the edge the attackers come over), percent.</summary>
    public int RiverPercent { get; init; } = 25;

    /// <summary>Extra strength of defenders in some biomes (forests, swamps), percent.</summary>
    public IReadOnlyList<BiomeDefence> Biomes { get; init; } = [];
}

/// <summary>What happened in a battle.</summary>
/// <param name="AttackersWon">True when the defenders broke.</param>
/// <param name="Rounds">Rounds fought.</param>
/// <param name="AttackersLost">Attackers killed (in the rounds and while fleeing).</param>
/// <param name="DefendersLost">Defenders killed (in the rounds and while fleeing).</param>
/// <param name="AttackerMorale">Morale of the attackers at the end.</param>
/// <param name="Broken">0 nobody broke (the defenders held), 1 the attackers broke, 2 the defenders broke.</param>
public readonly record struct BattleResult(bool AttackersWon, int Rounds, int AttackersLost, int DefendersLost, int AttackerMorale, int Broken);

/// <summary>
/// A battle under way (stage 6): the sides between two rounds. A battle of a campaign is fought one round every
/// <see cref="BattleRules.RoundTicks"/> ticks, so it lasts some seconds of the live game and can be seen on the map.
/// </summary>
public struct BattleState
{
    /// <summary>Rounds fought so far.</summary>
    public int Rounds { get; set; }

    /// <summary>Attackers still fighting.</summary>
    public int MenA { get; set; }

    /// <summary>Defenders still fighting.</summary>
    public int MenD { get; set; }

    /// <summary>Armed strength of the attackers left, warrior-percent.</summary>
    public long StrengthA { get; set; }

    /// <summary>Armed strength of the defenders left, warrior-percent.</summary>
    public long StrengthD { get; set; }

    /// <summary>Morale of the attackers.</summary>
    public int MoraleA { get; set; }

    /// <summary>Morale of the defenders.</summary>
    public int MoraleD { get; set; }

    /// <summary>Attackers killed so far (in the rounds and while fleeing).</summary>
    public int LostA { get; set; }

    /// <summary>Defenders killed so far (in the rounds and while fleeing).</summary>
    public int LostD { get; set; }

    /// <summary>0 nobody broke, 1 the attackers broke, 2 the defenders broke.</summary>
    public int Broken { get; set; }

    /// <summary>True when the battle is over.</summary>
    public bool Over { get; set; }

    /// <summary>The battle as a result (meaningful once <see cref="Over"/>).</summary>
    public readonly BattleResult Result => new(Broken == 2, Rounds, LostA, LostD, MoraleA, Broken);
}

/// <summary>The battle a commander expects, without drawing (stage 4e PR 5).</summary>
/// <param name="ChancePercent">Share of the played-through battles the attackers win, percent.</param>
/// <param name="LossPercent">Attackers the commander expects to lose, percent of the army.</param>
public readonly record struct ExpectedBattle(int ChancePercent, int LossPercent);

/// <summary>
/// Battles in rounds (GDD 9.3; stage 4e PR 4): in every round each side kills a share of the other in proportion to its
/// armed strength (men × arms × morale, the defenders with the terrain), times its luck of the round (stream
/// <c>combat</c>). Losses cost morale; the side whose morale falls below the limit breaks and flees, losing more men on
/// the run. When nobody breaks within the rounds, the defenders hold. All in whole numbers.
/// </summary>
public static class Battles
{
    /// <summary>Extra strength of defenders at <paramref name="target"/> attacked from <paramref name="from"/>, percent (terrain).</summary>
    public static int TerrainPercent(GameContent content, WorldState world, int target, int from)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(world);
        BattleRules rules = content.War.Battle;
        int percent = (Relief)world.Map.Relief[target] switch
        {
            Relief.Hills => rules.HillsPercent,
            Relief.Mountains => rules.MountainsPercent,
            _ => 0,
        };
        byte biome = world.Map.Biome[target];
        foreach (BiomeDefence b in rules.Biomes)
        {
            percent += b.Biome == biome ? b.Percent : 0;
        }

        if (from != target && from >= 0)
        {
            HexGlobe geo = world.Geometry;
            HexCoord at = geo.FromIndex(target);
            HexCoord other = geo.FromIndex(from);
            for (int d = 0; d < HexDirections.Count; d++)
            {
                if (geo.Neighbor(at, (HexDirection)d) == other)
                {
                    percent += world.Map.River[geo.EdgeIndex(geo.Edge(at, (HexDirection)d))] != 0 ? rules.RiverPercent : 0;
                    break;
                }
            }
        }

        return percent;
    }

    /// <summary>
    /// Fights a battle. <paramref name="attack"/> and <paramref name="defence"/> are the armed strengths of the sides in
    /// warrior-percent (terrain, hunger and morale already in), <paramref name="attackers"/> and <paramref name="defenders"/>
    /// their men.
    /// </summary>
    public static BattleResult Fight(BattleRules rules, DeterministicRandom rng, int attackers, long attack, int attackMorale, int defenders, long defence)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return Run(rules, rng, 0, 0, attackers, attack, attackMorale, defenders, defence);
    }

    /// <summary>
    /// The battle the commander expects (stage 4e PR 5), without drawing: the same rounds played with steady luck,
    /// <see cref="ExpectSteps"/> levels for each side from the worst to the best of <see cref="BattleRules.RollPercent"/>,
    /// every pair once (fractions of a man rounded). The chance is the share of these battles the attackers win, the
    /// losses their average losses. Inputs as in <see cref="Fight"/>, as the commander sees them.
    /// </summary>
    public static ExpectedBattle Expect(BattleRules rules, int attackers, long attack, int attackMorale, int defenders, long defence)
    {
        ArgumentNullException.ThrowIfNull(rules);
        int wins = 0;
        long lost = 0;
        for (int a = 0; a < ExpectSteps; a++)
        {
            for (int d = 0; d < ExpectSteps; d++)
            {
                BattleResult r = Run(rules, null, Luck(rules, a), Luck(rules, d), attackers, attack, attackMorale, defenders, defence);
                wins += r.AttackersWon ? 1 : 0;
                lost += r.AttackersLost;
            }
        }

        const int All = ExpectSteps * ExpectSteps;
        int lossPercent = attackers <= 0 ? 100 : (int)Math.Min(100, lost * 100 / All / attackers);
        return new ExpectedBattle(wins * 100 / All, lossPercent);
    }

    /// <summary>Levels of luck per side the commander plays through in <see cref="Expect"/>.</summary>
    public const int ExpectSteps = 5;

    private static int Luck(BattleRules rules, int step) =>
        rules.RollPercent.Min + ((rules.RollPercent.Max - rules.RollPercent.Min) * step / (ExpectSteps - 1));

    /// <summary>
    /// Starts a battle fought round by round over time (stage 6, a round every <see cref="BattleRules.RoundTicks"/> ticks):
    /// the sides as in <see cref="Fight"/>. Play it with <see cref="Round"/> until it is over.
    /// </summary>
    public static BattleState Begin(BattleRules rules, int attackers, long attack, int attackMorale, int defenders, long defence)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return new BattleState
        {
            MenA = Math.Max(0, attackers),
            MenD = Math.Max(0, defenders),
            StrengthA = Math.Max(0, attack),
            StrengthD = Math.Max(0, defence),
            MoraleA = Math.Clamp(attackMorale, 0, 100),
            MoraleD = Math.Clamp(rules.DefenderMorale, 0, 100),
        };
    }

    /// <summary>
    /// Fights the next round of <paramref name="b"/> (luck drawn from <paramref name="rng"/>). Returns the men each side
    /// lost in this round, the flight losses included when a side broke. When <see cref="BattleState.Over"/> is true
    /// afterwards, nothing more is fought.
    /// </summary>
    public static (int LostA, int LostD) Round(BattleRules rules, DeterministicRandom rng, ref BattleState b)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return Step(rules, rng, 0, 0, ref b);
    }

    // The rounds of a battle: luck drawn from rng, or steady (luckA, luckD) with fractions rounded when rng is null.
    private static BattleResult Run(BattleRules rules, DeterministicRandom? rng, int luckA, int luckD, int attackers, long attack, int attackMorale, int defenders, long defence)
    {
        BattleState b = Begin(rules, attackers, attack, attackMorale, defenders, defence);
        while (!b.Over)
        {
            Step(rules, rng, luckA, luckD, ref b);
        }

        return b.Result;
    }

    // One round (or, when there is nobody left to fight or no round left, the end without fighting).
    private static (int LostA, int LostD) Step(BattleRules rules, DeterministicRandom? rng, int luckA, int luckD, ref BattleState b)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (b.Over)
        {
            return (0, 0);
        }

        if (b.Rounds >= rules.Rounds || b.MenA <= 0 || b.MenD <= 0)
        {
            b.Over = true; // nobody broke: the defenders hold
            return (0, 0);
        }

        b.Rounds++;
        int killD = Math.Min(b.MenD, Kills(rules, rng, b.StrengthA, luckA));
        int killA = Math.Min(b.MenA, Kills(rules, rng, b.StrengthD, luckD));
        b.MoraleA = Math.Max(0, b.MoraleA - (rules.MoralePerLossPercent * killA * 100 / b.MenA));
        b.MoraleD = Math.Max(0, b.MoraleD - (rules.MoralePerLossPercent * killD * 100 / b.MenD));
        b.StrengthA = b.MenA - killA > 0 ? b.StrengthA * (b.MenA - killA) / b.MenA : 0;
        b.StrengthD = b.MenD - killD > 0 ? b.StrengthD * (b.MenD - killD) / b.MenD : 0;
        b.MenA -= killA;
        b.MenD -= killD;
        bool breakA = b.MenA == 0 || b.MoraleA < rules.BreakMorale;
        bool breakD = b.MenD == 0 || b.MoraleD < rules.BreakMorale;
        if (breakA || breakD)
        {
            // Both shaken: the side with less heart left flees (the attackers on a tie), losing more men on the run.
            b.Broken = breakA && (!breakD || b.MoraleA <= b.MoraleD) ? 1 : 2;
            b.Over = true;
            if (b.Broken == 1)
            {
                int rout = (int)IntMath.MulDiv(b.MenA, rules.RoutLossPercent, 100, Rounding.Ceiling);
                b.MenA -= rout;
                killA += rout;
            }
            else
            {
                int rout = (int)IntMath.MulDiv(b.MenD, rules.RoutLossPercent, 100, Rounding.Ceiling);
                b.MenD -= rout;
                killD += rout;
            }
        }
        else if (b.Rounds >= rules.Rounds)
        {
            b.Over = true; // nobody broke within the rounds: the defenders hold
        }

        b.LostA += killA;
        b.LostD += killD;
        return (killA, killD);
    }

    // Men killed by a side of strength str in one round: str × casualty% × luck%, the fraction of a man by chance (or
    // rounded, when the luck is steady).
    private static int Kills(BattleRules rules, DeterministicRandom? rng, long str, int steady)
    {
        int luck = rng is null ? steady : rng.NextInt(rules.RollPercent.Min, rules.RollPercent.Max + 1);
        long scaled = str * rules.CasualtyPercent * luck; // warrior-percent × percent × percent
        const long PerMan = 100L * 100 * 100;
        long whole = scaled / PerMan;
        long rest = scaled % PerMan;
        if (rng is null)
        {
            whole += rest * 2 >= PerMan ? 1 : 0;
        }
        else if (rest > 0 && rng.Chance((int)(rest / 100), (int)(PerMan / 100)))
        {
            whole++;
        }

        return (int)Math.Min(int.MaxValue, whole);
    }
}
