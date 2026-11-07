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

    // The rounds of a battle: luck drawn from rng, or steady (luckA, luckD) with fractions rounded when rng is null.
    private static BattleResult Run(BattleRules rules, DeterministicRandom? rng, int luckA, int luckD, int attackers, long attack, int attackMorale, int defenders, long defence)
    {
        ArgumentNullException.ThrowIfNull(rules);
        int menA = Math.Max(0, attackers);
        int menD = Math.Max(0, defenders);
        long strA = Math.Max(0, attack);
        long strD = Math.Max(0, defence);
        int moraleA = Math.Clamp(attackMorale, 0, 100);
        int moraleD = Math.Clamp(rules.DefenderMorale, 0, 100);
        int lostA = 0;
        int lostD = 0;
        int broken = 0;
        int round = 0;
        while (round < rules.Rounds && menA > 0 && menD > 0)
        {
            round++;
            int killD = Math.Min(menD, Kills(rules, rng, strA, luckA));
            int killA = Math.Min(menA, Kills(rules, rng, strD, luckD));
            moraleA = Math.Max(0, moraleA - (rules.MoralePerLossPercent * killA * 100 / menA));
            moraleD = Math.Max(0, moraleD - (rules.MoralePerLossPercent * killD * 100 / menD));
            strA = menA - killA > 0 ? strA * (menA - killA) / menA : 0;
            strD = menD - killD > 0 ? strD * (menD - killD) / menD : 0;
            menA -= killA;
            menD -= killD;
            lostA += killA;
            lostD += killD;
            bool breakA = menA == 0 || moraleA < rules.BreakMorale;
            bool breakD = menD == 0 || moraleD < rules.BreakMorale;
            if (breakA || breakD)
            {
                // Both shaken: the side with less heart left flees (the attackers on a tie).
                broken = breakA && (!breakD || moraleA <= moraleD) ? 1 : 2;
                break;
            }
        }

        if (broken == 1)
        {
            lostA += (int)IntMath.MulDiv(menA, rules.RoutLossPercent, 100, Rounding.Ceiling);
        }
        else if (broken == 2)
        {
            lostD += (int)IntMath.MulDiv(menD, rules.RoutLossPercent, 100, Rounding.Ceiling);
        }

        return new BattleResult(broken == 2, round, lostA, lostD, moraleA, broken);
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
