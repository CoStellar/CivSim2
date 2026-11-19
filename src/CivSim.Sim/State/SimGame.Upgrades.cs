using System;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>
/// Upgrades in place (Patryk, 2026-11-19): a building becomes its better kind (huts → stone houses, toolmaker → blacksmith…)
/// once that kind is known. The old building is taken down and the new one is built on its places (and free ones, when it
/// is bigger); half of the old price (<c>upgradeCreditPercent</c>) is already at the site, porters bring the rest.
/// </summary>
public sealed partial class SimGame
{
    /// <summary>Why building <paramref name="b"/> cannot be upgraded now (null = it can), and what it would become.</summary>
    public string? CannotUpgrade(Civ civ, Building b, out BuildingType? to)
    {
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(b);
        to = null;
        BuildingType from = Content.Buildings[b.Type];
        if (from.UpgradesTo <= 0)
        {
            return $"{from.Name} has no better kind.";
        }

        to = Content.Buildings[from.UpgradesTo];
        if (b.Civ != civ.Id || b.State is not (BuildingState.Active or BuildingState.Stopped))
        {
            return "Only a finished building of ours can be upgraded.";
        }

        if (!Knows(civ, to))
        {
            return $"{to.Name} needs {NeedText(to.Tech, to.AlsoTechs)}.";
        }

        // Its own places count as free for the new kind.
        int slots = UpgradeSlots(b, to);
        if (slots == 0)
        {
            return $"{to.Name} needs {(to.Size == 4 ? "the whole hex" : $"{to.Size} places")} here: free the other places first.";
        }

        if (!TerrainAllows(b.Cell, to, civ))
        {
            return $"{to.Name} needs {Needs(to)}.";
        }

        long[] rest = UpgradeRest(from, to);
        return CanAfford(Settlements[b.Settlement].Stock, rest, 100) ? null : $"Not enough goods in {Settlements[b.Settlement].Name}: the upgrade costs {PriceText(rest)}.";
    }

    /// <summary>What the upgrade of <paramref name="from"/> to <paramref name="to"/> still costs after the credit.</summary>
    public long[] UpgradeRest(BuildingType from, BuildingType to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        long[] credit = UpgradeCredit(from, to);
        var rest = new long[to.Price.Length];
        for (int g = 0; g < rest.Length; g++)
        {
            rest[g] = to.Price[g] - credit[g];
        }

        return rest;
    }

    private long[] UpgradeCredit(BuildingType from, BuildingType to)
    {
        var credit = new long[to.Price.Length];
        for (int g = 0; g < credit.Length; g++)
        {
            credit[g] = Math.Min(to.Price[g], from.Price[g] * Content.UpgradeCreditPercent / 100);
        }

        return credit;
    }

    // Places the new kind would take: its own first, then free ones, in the usual sets.
    private int UpgradeSlots(Building b, BuildingType to)
    {
        int free = FreeSlots(b.Cell) | b.Slots;
        foreach (int set in UpgradeSets(to.Size))
        {
            if ((free & set) == set && (set & b.Slots) != 0)
            {
                return set;
            }
        }

        return 0;
    }

    private static int[] UpgradeSets(int size) => size switch
    {
        4 => [15],
        2 => [3, 12, 5, 10],
        _ => [1, 2, 4, 8],
    };

    private string? UpgradeBuilding(Civ civ, int id)
    {
        if ((uint)id >= (uint)Buildings.Count)
        {
            return "Unknown building.";
        }

        Building old = Buildings[id];
        string? why = CannotUpgrade(civ, old, out BuildingType? to);
        if (why is not null)
        {
            return why;
        }

        BuildingType from = Content.Buildings[old.Type];
        int slots = UpgradeSlots(old, to!);
        old.State = BuildingState.Removed;
        TakeOffHex(old);
        civ.Buildings.Remove(old.Id);
        civ.Dirty = true;
        Building b = Place(civ, old.Cell, to!, slots, UpgradeCredit(from, to!));
        b.Priority = old.Priority;
        News.Add((civ.Id, $"{from.Name} in {Settlements[b.Settlement].Name} is being rebuilt as {to!.Name.ToLowerInvariant()}."));
        return null;
    }
}
