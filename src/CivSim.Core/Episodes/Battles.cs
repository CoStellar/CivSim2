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
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(rng);
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
            int killD = Math.Min(menD, Kills(rules, rng, strA));
            int killA = Math.Min(menA, Kills(rules, rng, strD));
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
            int rout = (int)IntMath.MulDiv(menA, rules.RoutLossPercent, 100, Rounding.Ceiling);
            lostA += rout;
        }
        else if (broken == 2)
        {
            int rout = (int)IntMath.MulDiv(menD, rules.RoutLossPercent, 100, Rounding.Ceiling);
            lostD += rout;
        }

        return new BattleResult(broken == 2, round, lostA, lostD, moraleA, broken);
    }

    // Men killed by a side of strength str in one round: str × casualty% × luck%, the fraction of a man by chance.
    private static int Kills(BattleRules rules, DeterministicRandom rng, long str)
    {
        int luck = rng.NextInt(rules.RollPercent.Min, rules.RollPercent.Max + 1);
        long scaled = str * rules.CasualtyPercent * luck; // warrior-percent × percent × percent
        const long PerMan = 100L * 100 * 100;
        long whole = scaled / PerMan;
        long rest = scaled % PerMan;
        if (rest > 0 && rng.Chance((int)(rest / 100), (int)(PerMan / 100)))
        {
            whole++;
        }

        return (int)Math.Min(int.MaxValue, whole);
    }
}
