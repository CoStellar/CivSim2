using System;
using System.Collections.Generic;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.State;

namespace CivSim.Core.Research;

/// <summary>What technologies give a civilization when gathering (stage 3f): extra goods by deposit and by biome.</summary>
public sealed class TechBenefits
{
    private static readonly IReadOnlyList<GoodQuantity> None = [];
    private readonly List<GoodQuantity>?[] _byDeposit;
    private readonly List<GoodQuantity>?[] _byBiome;

    private TechBenefits(int deposits, int biomes)
    {
        _byDeposit = new List<GoodQuantity>?[deposits + 1];
        _byBiome = new List<GoodQuantity>?[biomes];
    }

    /// <summary>Benefits of a civilization that knows nothing.</summary>
    public static TechBenefits Empty(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new TechBenefits(content.Deposits.Count, content.Biomes.Count);
    }

    /// <summary>True when some water biome gives goods (fishing): gathering then also works the shore.</summary>
    public bool FromWater { get; private set; }

    /// <summary>Extra goods per year from a cell with this deposit.</summary>
    public IReadOnlyList<GoodQuantity> ForDeposit(int deposit) =>
        deposit > 0 && deposit < _byDeposit.Length ? (IReadOnlyList<GoodQuantity>?)_byDeposit[deposit] ?? None : None;

    /// <summary>Extra goods per year from a cell of this biome.</summary>
    public IReadOnlyList<GoodQuantity> ForBiome(int biome) =>
        biome >= 0 && biome < _byBiome.Length ? (IReadOnlyList<GoodQuantity>?)_byBiome[biome] ?? None : None;

    /// <summary>The benefits of the technologies <paramref name="civ"/> knows.</summary>
    public static TechBenefits Of(GameContent content, CivilizationState civ)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(civ);
        var b = new TechBenefits(content.Deposits.Count, content.Biomes.Count);
        foreach (TechnologyDefinition t in content.Technologies)
        {
            if (!civ.Knows(t.Id))
            {
                continue;
            }

            foreach (GatherBonus g in t.Gather)
            {
                List<GoodQuantity> list = g.DepositId > 0
                    ? b._byDeposit[g.DepositId] ??= []
                    : b._byBiome[g.BiomeId] ??= [];
                list.AddRange(g.Goods);
                b.FromWater |= g.BiomeId >= 0 && content.Biomes[g.BiomeId].IsWater;
            }
        }

        return b;
    }
}

/// <summary>
/// What a civilization's situation offers for inspirations (GDD 6.1; stage 3f), measured once per turn: deposits,
/// biomes and rivers in its influence zone, its coldest camp winter, its people, its goods, and its neighbours.
/// </summary>
public sealed class CivFacts
{
    /// <summary>Deposit ids found in the zone.</summary>
    public HashSet<int> Deposits { get; } = [];

    /// <summary>Biome ids found in the zone.</summary>
    public HashSet<int> Biomes { get; } = [];

    /// <summary>A river runs through the zone.</summary>
    public bool River { get; set; }

    /// <summary>Coldest winter temperature at one of its camps, °C.</summary>
    public int ColdestWinter { get; set; } = int.MaxValue;

    /// <summary>People of the civilization.</summary>
    public long People { get; set; }

    /// <summary>Goods in all its warehouses, hundredths, by good id.</summary>
    public long[] Goods { get; set; } = [];

    /// <summary>Indices of other living civilizations with a settlement within contact distance, or trading with this one.</summary>
    public List<int> Neighbours { get; } = [];

    /// <summary>Measures every living civilization (index = civilization index in the state).</summary>
    public static CivFacts[] Measure(GameContent content, GameState state)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        int civs = state.Civilizations.Count;
        var facts = new CivFacts[civs];
        for (int c = 0; c < civs; c++)
        {
            facts[c] = new CivFacts { Goods = new long[content.Goods.Count] };
        }

        // Settlement id → owner index, for reading the claim layer.
        var ownerOf = new int[Math.Max(1, state.NextSettlementId)];
        Array.Fill(ownerOf, -1);
        IReadOnlyList<SettlementState> all = state.Settlements;
        for (int n = 0; n < all.Count; n++)
        {
            SettlementState s = all[n];
            if (!s.IsActive)
            {
                continue;
            }

            int c = state.IndexOfCivilization(s.Owner);
            if (c < 0)
            {
                continue;
            }

            if (s.Id.Value < ownerOf.Length)
            {
                ownerOf[s.Id.Value] = c;
            }

            CivFacts f = facts[c];
            f.People += s.TotalPopulation();
            ReadOnlySpan<Amount> stock = s.StockView;
            for (int g = 0; g < stock.Length && g < f.Goods.Length; g++)
            {
                f.Goods[g] += stock[g].Hundredths;
            }

            WorldMap smap = state.Worlds[s.World.Value].Map;
            int si = state.Worlds[s.World.Value].Geometry.Index(s.Cell);
            f.ColdestWinter = Math.Min(f.ColdestWinter, smap.Temperature[si] - (smap.Seasonality[si] / 2));
        }

        Span<HexCoord> n6 = stackalloc HexCoord[6];
        foreach (WorldState world in state.Worlds)
        {
            WorldMap map = world.Map;
            HexGlobe geo = world.Geometry;
            ReadOnlySpan<int> claim = map.Claim.Values;
            for (int i = 0; i < claim.Length; i++)
            {
                int id = claim[i] - 1;
                if (id < 0 || id >= ownerOf.Length || ownerOf[id] < 0)
                {
                    continue;
                }

                CivFacts f = facts[ownerOf[id]];
                f.Biomes.Add(map.Biome[i]);
                if (map.Deposit[i] != 0)
                {
                    f.Deposits.Add(map.Deposit[i]);
                }

                if (!f.River)
                {
                    HexCoord c = geo.FromIndex(i);
                    for (int d = 0; d < 6 && !f.River; d++)
                    {
                        f.River = map.River[geo.EdgeIndex(geo.Edge(c, (HexDirection)d))] != 0;
                    }
                }

                // Water next to the zone counts too (coasts, lakes).
                geo.GetNeighbors(geo.FromIndex(i), n6);
                foreach (HexCoord nc in n6)
                {
                    int j = geo.Index(nc);
                    if (content.Biomes[map.Biome[j]].IsWater)
                    {
                        f.Biomes.Add(map.Biome[j]);
                        if (map.Deposit[j] != 0)
                        {
                            f.Deposits.Add(map.Deposit[j]);
                        }
                    }
                }
            }
        }

        // Neighbours: another civilization with an active settlement close to one of ours.
        int contact = content.Research.ContactCells;
        for (int a = 0; a < all.Count; a++)
        {
            SettlementState sa = all[a];
            int ca = sa.IsActive ? state.IndexOfCivilization(sa.Owner) : -1;
            if (ca < 0)
            {
                continue;
            }

            HexGlobe geo = state.Worlds[sa.World.Value].Geometry;
            for (int b = a + 1; b < all.Count; b++)
            {
                SettlementState sb = all[b];
                if (!sb.IsActive || sb.Owner == sa.Owner || sb.World != sa.World)
                {
                    continue;
                }

                int cb = state.IndexOfCivilization(sb.Owner);
                if (cb < 0 || facts[ca].Neighbours.Contains(cb) || geo.Distance(sa.Cell, sb.Cell) > contact)
                {
                    continue;
                }

                facts[ca].Neighbours.Add(cb);
                facts[cb].Neighbours.Add(ca);
            }
        }

        // Trade partners (GDD 9.2; stage 4d): merchants carry knowledge, however far apart the partners live.
        foreach (Diplomacy.Treaty t in state.Treaties.All)
        {
            int ca = state.IndexOfCivilization(t.A);
            int cb = state.IndexOfCivilization(t.B);
            if (t.Kind != Diplomacy.TreatyKind.Trade || t.LastFlowMonth < 0 || ca < 0 || cb < 0 || facts[ca].Neighbours.Contains(cb))
            {
                continue;
            }

            facts[ca].Neighbours.Add(cb);
            facts[cb].Neighbours.Add(ca);
        }

        foreach (CivFacts f in facts)
        {
            f.Neighbours.Sort();
        }

        return facts;
    }
}

/// <summary>Rules of the technology graph (GDD 6; stage 3f): availability, cost with inspirations and diffusion, eras.</summary>
public static class Technologies
{
    /// <summary>True when the civilization may research the technology: unknown, and its requirements known.</summary>
    public static bool Available(GameContent content, CivilizationState civ, int technology)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(civ);
        if (technology < 0 || technology >= content.Technologies.Count || civ.Knows(technology))
        {
            return false;
        }

        TechnologyDefinition t = content.Technologies[technology];
        foreach (int r in t.Requires)
        {
            if (!civ.Knows(r))
            {
                return false;
            }
        }

        if (t.RequiresAny.Count == 0)
        {
            return true;
        }

        foreach (int r in t.RequiresAny)
        {
            if (civ.Knows(r))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Percent of the base cost the civilization pays now (inspirations and neighbours that know it).</summary>
    public static int CostPercent(GameContent content, GameState state, CivFacts[] facts, int civIndex, int technology)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(facts);
        CivFacts f = facts[civIndex];
        TechnologyDefinition t = content.Technologies[technology];
        int cut = 0;
        foreach (Inspiration ins in t.Inspirations)
        {
            bool met = ins.Kind switch
            {
                InspirationKind.GoodInNetwork => ins.Id >= 0 && f.Goods[ins.Id] >= ins.Amount.Hundredths,
                InspirationKind.DepositInZone => f.Deposits.Contains(ins.Id),
                InspirationKind.BiomeInZone => f.Biomes.Contains(ins.Id),
                InspirationKind.RiverInZone => f.River,
                InspirationKind.WinterBelow => f.ColdestWinter <= ins.Value,
                InspirationKind.PeopleAtLeast => f.People >= ins.Value,
                _ => false,
            };
            cut += met ? ins.Percent : 0;
        }

        int knowing = 0;
        foreach (int n in f.Neighbours)
        {
            knowing += state.Civilizations[n].IsAlive && state.Civilizations[n].Knows(technology) ? 1 : 0;
        }

        cut += Math.Min(knowing, content.Research.MaxNeighbours) * content.Research.DiffusionPercent;
        return Math.Max(content.Research.MinCostPercent, 100 - cut);
    }

    /// <summary>Cost now, hundredths of a research point.</summary>
    public static long Cost(GameContent content, GameState state, CivFacts[] facts, int civIndex, int technology)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.Technologies[technology].Cost.Hundredths * CostPercent(content, state, facts, civIndex, technology) / 100;
    }

    /// <summary>Research points the civilization's people make in <paramref name="months"/>, hundredths.</summary>
    public static long Science(GameContent content, GameState state, CivId civ, int months)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        long perThousandYears = 0;
        foreach (SettlementState s in state.Settlements)
        {
            if (!s.IsActive || s.Owner != civ)
            {
                continue;
            }

            ReadOnlySpan<int> people = s.PopulationView;
            for (int k = 0; k < people.Length && k < content.Strata.Count; k++)
            {
                perThousandYears += (long)people[k] * content.Strata[k].SciencePerThousandPerYear;
            }
        }

        return perThousandYears * months * 100 / (12 * 1000);
    }

    /// <summary>
    /// Learns a technology: marks it known, adds its permanent effects, and moves the civilization into later eras when
    /// it qualifies. Returns chronicle lines.
    /// </summary>
    public static List<string> Learn(GameContent content, GameState state, CivilizationState civ, int technology)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        var lines = new List<string>();
        EnsureSize(content, civ);
        if (civ.KnownTechnologies[technology])
        {
            return lines;
        }

        civ.KnownTechnologies[technology] = true;
        civ.KnownCount++;
        TechnologyDefinition t = content.Technologies[technology];
        foreach (TechEffect e in t.Effects)
        {
            state.Effects.Add(content.Effects[e.EffectId], WorldId.Earth, civ.Id.Value, e.Strength, EffectInstance.Permanent);
        }

        lines.Add($"{civ.Name} learned {t.Name}.");
        while (civ.Era + 1 < content.Eras.Count && QualifiesForEra(content, civ, civ.Era + 1))
        {
            civ.Era++;
            lines.Add($"{civ.Name} entered the {content.Eras[civ.Era].Name}.");
        }

        return lines;
    }

    /// <summary>True when the civilization knows the era's threshold technology and enough of the previous era's.</summary>
    public static bool QualifiesForEra(GameContent content, CivilizationState civ, int era)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(civ);
        int threshold = content.Eras[era].ThresholdTechnology;
        if (threshold < 0 || !civ.Knows(threshold))
        {
            return false;
        }

        int total = 0;
        int known = 0;
        foreach (TechnologyDefinition t in content.Technologies)
        {
            if (t.Era == era)
            {
                total++;
                known += civ.Knows(t.Id) ? 1 : 0;
            }
        }

        return total == 0 || known * 100 >= total * content.Research.EraSharePercent;
    }

    /// <summary>True when one of the technologies the civilization knows unlocks the recipe.</summary>
    public static bool KnowsRecipe(GameContent content, CivilizationState civ, int recipe)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(civ);
        foreach (TechnologyDefinition t in content.Technologies)
        {
            if (!civ.Knows(t.Id))
            {
                continue;
            }

            for (int k = 0; k < t.Recipes.Count; k++)
            {
                if (t.Recipes[k] == recipe)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Grows the civilization's known-technology table to the number of technologies.</summary>
    public static void EnsureSize(GameContent content, CivilizationState civ)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(civ);
        if (civ.KnownTechnologies.Length < content.Technologies.Count)
        {
            bool[] grown = new bool[content.Technologies.Count];
            Array.Copy(civ.KnownTechnologies, grown, civ.KnownTechnologies.Length);
            civ.KnownTechnologies = grown;
        }
    }
}
