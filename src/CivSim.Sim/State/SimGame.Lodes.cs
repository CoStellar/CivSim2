using System;
using System.Collections.Generic;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>An ore sign (2026-11-22): a circle on seen land where an ore of the civilization's era lies.</summary>
public readonly record struct OreSign(int Deposit, int Center);

/// <summary>A prospector's order to search the ore signs one by one (nearest first).</summary>
public sealed record SearchSignsCommand(int Civ, int Unit) : SimCommand(Civ);

/// <summary>
/// Lodes (Patryk, 2026-11-22). Every deposit is small, medium or large: a mine on it, or a bonus from it, gives that share
/// of the usual (<c>lodes.sizes</c>, kept in the map's deposit richness as percent). Rare ores and horses gather in regions
/// of the world (<c>lodes.regional</c>): outside them only some stay, as many are added inside, and inside they are more often
/// large. Prospecting shows ore signs: for each ore of the civilization's era, circles on land it has seen near its settlements,
/// with the ore somewhere inside; prospectors can be sent to search them one by one.
/// </summary>
public sealed partial class SimGame
{
    /// <summary>What a mine or a bonus on the hex's deposit gives, percent of the usual (100 with no sizes).</summary>
    public int DepositSizePercent(int cell)
    {
        int p = Map.DepositRichness[cell];
        return Map.Deposit[cell] == 0 || p <= 0 || Content.Lodes.Sizes.Length == 0 ? 100 : p;
    }

    /// <summary>The size of the hex's deposit as a word ("small"), or "" with no sizes.</summary>
    public string DepositSizeName(int cell)
    {
        int p = DepositSizePercent(cell);
        foreach (LodeSize z in Content.Lodes.Sizes)
        {
            if (z.Percent == p)
            {
                return z.Name;
            }
        }

        return "";
    }

    /// <summary>Ore signs of a civilization now (empty without the technology).</summary>
    public IReadOnlyList<OreSign> SignsOf(Civ civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return civ.Signs;
    }

    // Sizes and regions, once, right after the world is made (before the starts get their metals).
    private void ShapeLodes()
    {
        LodeRules r = Content.Lodes;
        if (r.Sizes.Length == 0)
        {
            return;
        }

        ulong z = (Settings.Seed * 0x9E3779B97F4A7C15UL) ^ 0x4C4F444553UL; // own sequence: the other streams stay as they were
        int cells = Map.Geometry.CellCount;
        var inRegion = new bool[cells];
        var land = new List<int>();
        for (int i = 0; i < cells; i++)
        {
            if (!Content.World.Biomes[Map.Biome[i]].IsWater)
            {
                land.Add(i);
            }
        }

        foreach (int d in r.Regional)
        {
            DepositDefinition def = Content.World.Deposit(d);
            int count = Math.Max(1, land.Count * r.Regions / 10000);
            var centres = new int[count];
            for (int k = 0; k < count && land.Count > 0; k++)
            {
                centres[k] = land[(int)(Next(ref z) % (ulong)land.Count)];
            }

            var inside = new List<int>();
            int removed = 0;
            foreach (int i in land)
            {
                bool here = false;
                foreach (int c in centres)
                {
                    if (Distance(i, c) <= r.RegionHexes)
                    {
                        here = true;
                        break;
                    }
                }

                if (here)
                {
                    inRegion[i] |= Map.Deposit[i] == 0 || Map.Deposit[i] == d;
                    if (Map.Deposit[i] == 0 && Allows(def, i))
                    {
                        inside.Add(i);
                    }
                }
                else if (Map.Deposit[i] == d && (int)(Next(ref z) % 100) >= r.OutsideKeepPercent)
                {
                    Map.Deposit.Set(i, 0);
                    Map.DepositDepth.Set(i, 0);
                    removed++;
                }
            }

            for (int k = 0; k < removed && inside.Count > 0; k++)
            {
                int pick = (int)(Next(ref z) % (ulong)inside.Count);
                int cell = inside[pick];
                inside[pick] = inside[^1];
                inside.RemoveAt(inside.Count - 1);
                Map.Deposit.Set(cell, (ushort)d);
                Map.DepositDepth.Set(cell, (byte)def.Depth);
            }
        }

        int all = 0;
        int allRegion = 0;
        foreach (LodeSize s in r.Sizes)
        {
            all += s.Weight;
            allRegion += s.RegionWeight;
        }

        for (int i = 0; i < cells; i++)
        {
            int d = Map.Deposit[i];
            if (d == 0)
            {
                continue;
            }

            bool regional = inRegion[i] && Array.IndexOf(r.Regional, d) >= 0;
            int roll = (int)(Next(ref z) % (ulong)Math.Max(1, regional ? allRegion : all));
            int size = 0;
            for (int k = 0; k < r.Sizes.Length; k++)
            {
                roll -= regional ? r.Sizes[k].RegionWeight : r.Sizes[k].Weight;
                if (roll < 0)
                {
                    size = k;
                    break;
                }
            }

            Map.DepositRichness.Set(i, r.Sizes[size].Percent);
        }
    }

    private bool Allows(DepositDefinition def, int cell)
    {
        string biome = Content.World.Biomes[Map.Biome[cell]].Key;
        bool b = false;
        foreach (string k in def.Biomes)
        {
            b |= k == biome;
        }

        bool rel = false;
        foreach (Relief x in def.Relief)
        {
            rel |= (int)x == Map.Relief[cell];
        }

        return b && rel;
    }

    private static ulong Next(ref ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        ulong x = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    // Ore signs, worked out again every recountSeconds for civilizations with Prospecting: the nearest unfound ores of the era on
    // land the civilization has seen, within reach of its settlements and not on its own land (that it knows already).
    private void UpdateSigns()
    {
        LodeRules r = Content.Lodes;
        if (r.SignTech < 0 || Ticks % r.RecountTicks != 0)
        {
            return;
        }

        foreach (Civ civ in Civs)
        {
            civ.Signs.Clear();
            if (civ.Dead || !civ.KnownTech[r.SignTech] || civ.Era >= r.EraOres.Length || r.EraOres[civ.Era].Length == 0 || civ.Settlements.Count == 0)
            {
                continue;
            }

            foreach (int ore in r.EraOres[civ.Era])
            {
                var found = new List<(int Dist, int Cell)>();
                for (int i = 0; i < Map.Geometry.CellCount; i++)
                {
                    if (Map.Deposit[i] != ore || !civ.Known[i] || OwnerAt[i] == civ.Id || KnowsDeposit(civ, i))
                    {
                        continue;
                    }

                    int best = int.MaxValue;
                    foreach (int sid in civ.Settlements)
                    {
                        best = Math.Min(best, Distance(Settlements[sid].Center, i));
                    }

                    if (best <= r.ReachHexes)
                    {
                        found.Add((best, i));
                    }
                }

                found.Sort();
                for (int k = 0; k < found.Count && k < r.PerOre; k++)
                {
                    civ.Signs.Add(new OreSign(ore, SignCentre(civ, found[k].Cell)));
                }
            }
        }
    }

    // The centre of a sign: up to offsetHexes from the ore, the same for a civilization and an ore all game (from a hash).
    private int SignCentre(Civ civ, int ore)
    {
        LodeRules r = Content.Lodes;
        ulong z = ((ulong)(ore + 1) * 0x9E3779B97F4A7C15UL) ^ ((ulong)(civ.Id + 1) * 0xD6E8FEB86659FD93UL) ^ Settings.Seed;
        HexCoord at = Geo.FromIndex(ore);
        int steps = (int)(Next(ref z) % (ulong)(r.OffsetHexes + 1));
        for (int s = 0; s < steps; s++)
        {
            HexCoord n = Geo.Neighbor(at, (HexDirection)(Next(ref z) % 6));
            if (!Content.World.Biomes[Map.Biome[Geo.Index(n)]].IsWater)
            {
                at = n;
            }
        }

        return Geo.Index(at);
    }

    // A prospector searching the signs: once it has looked around where it stands, it walks to the nearest sign whose centre
    // it has not looked at in this era; with none left it stops searching.
    private void SearchSigns()
    {
        foreach (Unit u in Units)
        {
            if (!u.SearchingSigns || u.State != UnitState.Idle || u.SurveyedAt != (u.Cell * 16) + Civs[u.Civ].Era)
            {
                continue;
            }

            Civ civ = Civs[u.Civ];
            int best = -1;
            int bestDist = int.MaxValue;
            foreach (OreSign s in civ.Signs)
            {
                int d = Distance(u.Cell, s.Center);
                if (d > 0 && d < bestDist && !RockLookedAt(civ, s.Center))
                {
                    bestDist = d;
                    best = s.Center;
                }
            }

            if (best < 0 || Apply(new MoveCommand(civ.Id, u.Id, best, false)) is not null)
            {
                u.SearchingSigns = false;
                if (best < 0)
                {
                    News.Add((civ.Id, $"Our {Content.Units[u.Kind].Name.ToLowerInvariant()} have searched every ore sign."));
                }
            }
            else
            {
                u.SearchingSigns = true; // MoveCommand clears orders; the search goes on
            }
        }
    }

    private string? SearchSignsOrder(Civ civ, int id)
    {
        if ((uint)id >= (uint)Units.Count || Units[id].Civ != civ.Id || Units[id].State == UnitState.Training)
        {
            return "Not our unit, or it is not ready.";
        }

        Unit u = Units[id];
        if (Content.Units[u.Kind].Survey != 1)
        {
            return "Only prospectors search ore signs.";
        }

        if (Content.Lodes.SignTech < 0 || !civ.KnownTech[Content.Lodes.SignTech])
        {
            return $"Ore signs need {Content.Techs[Math.Max(0, Content.Lodes.SignTech)].Name}.";
        }

        if (civ.Signs.Count == 0)
        {
            return "No ore signs now: see more land near our settlements, or wait for the next era.";
        }

        u.SearchingSigns = true;
        if (u.State == UnitState.Idle && u.SurveyedAt != (u.Cell * 16) + civ.Era)
        {
            return null; // it looks around here first, then goes on
        }

        u.SurveyedAt = (u.Cell * 16) + civ.Era; // walking: the next sign at once
        if (u.State != UnitState.Idle)
        {
            u.State = UnitState.Idle;
            u.Path.Clear();
        }

        return null;
    }
}
