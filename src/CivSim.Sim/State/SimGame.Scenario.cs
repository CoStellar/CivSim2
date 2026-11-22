using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>Debug actions, only in a scenario with <c>debug</c> on.</summary>
public enum DebugAction
{
    /// <summary>+200 of every good in every settlement.</summary>
    Goods = 0,

    /// <summary>The current research is done at once.</summary>
    FinishResearch = 1,

    /// <summary>The whole map is known.</summary>
    RevealMap = 2,

    /// <summary>+10 people in every settlement.</summary>
    People = 3,

    /// <summary>The next era: every technology of the current one is known.</summary>
    NextEra = 4,
}

/// <summary>A debug action for a people (scenarios only).</summary>
public sealed record DebugCommand(int Civ, DebugAction Action) : SimCommand(Civ);

/// <summary>
/// Test scenarios (Patryk, 2026-11-19): a game set up at a chosen moment of development, so later technologies, war and
/// the rest can be tested without playing up to them. The game is created as usual (without the start phase), then every
/// people is set up as the scenario says.
/// </summary>
public sealed partial class SimGame
{
    /// <summary>Test scenarios: no peace can be proposed (battles are fought to the end).</summary>
    public bool NoPeace { get; private set; }

    /// <summary>The debug keys work in this game.</summary>
    public bool DebugOn { get; private set; }

    /// <summary>Creates a game from a scenario.</summary>
    public static SimGame FromScenario(SimContent content, SimScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(scenario);
        SimGame g = Create(content, new SimSettings(scenario.Seed, scenario.Width, scenario.Height, Math.Max(scenario.Peoples, scenario.Civs.Count)) { StartPhase = false, NearStarts = false });
        g.DebugOn = scenario.Debug;
        for (int i = 0; i < g.Civs.Count; i++)
        {
            g.Civs[i].IsAi = i != 0;
            if (i < scenario.Civs.Count)
            {
                g.SetUp(g.Civs[i], scenario.Civs[i]);
            }
        }

        if (scenario.RevealMap)
        {
            foreach (Civ c in g.Civs)
            {
                g.Reveal(c);
            }
        }

        foreach (int[] w in scenario.Wars)
        {
            if (w.Length == 2 && w[0] != w[1] && (uint)w[0] < (uint)g.Civs.Count && (uint)w[1] < (uint)g.Civs.Count)
            {
                g.SetWar(w[0], w[1], true);
            }
        }

        g.NoPeace = scenario.NoPeace;
        if (scenario.Field is [int fa, int fb] && fa != fb && (uint)fa < (uint)g.Civs.Count && (uint)fb < (uint)g.Civs.Count && !g.PlaceField(fa, fb, scenario.FieldGap))
        {
            g.News.Add((0, "Scenario: no field between the two peoples."));
        }

        foreach (Civ c in g.Civs)
        {
            c.Dirty = true;
            g.Recompute(c);
            g.SyncTotals(c);
        }

        return g;
    }

    private void SetUp(Civ civ, ScenarioPeople p)
    {
        if (p.Ai is bool ai)
        {
            civ.IsAi = ai;
        }

        civ.Aggression = p.Aggression ?? civ.Aggression;
        civ.Expansion = p.Expansion ?? civ.Expansion;

        // Technologies: all of the eras before, then the named ones with what they need.
        int era = p.Era.Length == 0 ? 0 : IndexOf(Content.Eras, e => e.Key == p.Era);
        if (era < 0)
        {
            throw new ArgumentException($"Scenario: unknown era '{p.Era}'.");
        }

        foreach (SimTech t in Content.Techs)
        {
            if (t.Era < era || t.Basic)
            {
                Learn(civ, t.Id);
            }
        }

        foreach (string key in p.Techs)
        {
            int id = IndexOf(Content.Techs, t => t.Key == key);
            if (id < 0)
            {
                throw new ArgumentException($"Scenario: unknown technology '{key}'.");
            }

            Learn(civ, id);
        }

        civ.Era = Math.Max(civ.Era, era);
        AdvanceEra(civ, false);
        if (civ.Settlements.Count == 0)
        {
            return;
        }

        Settlement s = Settlements[civ.Settlements[0]];
        if (p.People > 0)
        {
            s.People = p.People;
        }

        if (p.Level.Length > 0)
        {
            int level = IndexOf(Content.Levels, l => l.Key == p.Level);
            if (level < 0)
            {
                throw new ArgumentException($"Scenario: unknown settlement level '{p.Level}'.");
            }

            s.Level = level;
            Claim(s);
            Watch(s);
        }

        foreach ((string key, double amount) in p.Stock)
        {
            int g = IndexOf(Content.Goods, x => x.Key == key);
            if (g < 0)
            {
                throw new ArgumentException($"Scenario: unknown good '{key}'.");
            }

            s.Stock[g] = (long)Math.Round(amount * SimContent.One);
        }

        foreach ((string key, int count) in p.Buildings)
        {
            BuildingType t = Content.Building(key);
            for (int k = 0; k < count; k++)
            {
                if (!PlaceFinished(civ, s, t))
                {
                    News.Add((civ.Id, $"Scenario: no room for {t.Name} in {s.Name}."));
                    break;
                }
            }
        }

        if (p.WallRing > 0)
        {
            int wall = IndexOf(Content.Works, w => w.Key == "wall");
            if (wall < 0)
            {
                throw new ArgumentException("Scenario: no wall work in the data.");
            }

            foreach (int cell in s.Cells)
            {
                if (Distance(cell, s.Center) == p.WallRing && OwnerAt[cell] == civ.Id)
                {
                    FinishWorkHex(civ, Content.Works[wall], cell);
                }
            }
        }

        foreach ((string key, int count) in p.Units)
        {
            UnitType t = Content.Unit(key);
            for (int k = 0; k < count; k++)
            {
                int kind = Upgraded(civ, t.Id);
                UnitType ut = Content.Units[kind];
                int people = p.UnitPeople > 0 && ut.MaxPeople > 0 ? Math.Clamp(p.UnitPeople, ut.MinPeople, ut.MaxPeople) : ut.People;
                var u = new Unit { Id = Units.Count, Civ = civ.Id, Kind = kind, People = people, Size = people, Cell = s.Center, State = UnitState.Idle, Home = s.Id };
                if (Geared(ut))
                {
                    if (p.Weapon >= 0 || p.Armour >= 0)
                    {
                        u.Weapon = (byte)Math.Clamp(p.Weapon, 0, 4);
                        u.Armour = (byte)Math.Clamp(p.Armour, 0, 4);
                    }
                    else
                    {
                        TakeGear(s.Stock, u, shock: false);
                    }
                }

                Units.Add(u);
                civ.Units.Add(u.Id);
                Watch(u);
                FillPacks(u); // A5 (tests 2026-11-20): they left with empty packs and starved a minute out
            }
        }

        for (int k = 0; k < p.Settlements; k++)
        {
            int cell = FreeSiteNear(s.Center);
            if (cell < 0)
            {
                break;
            }

            FoundSettlement(civ, cell, p.SettlementPeople, (long[])Content.StartStock.Clone()); // 2026-11-22: not with empty stores (food ran out in 0:28)
        }

        civ.Dirty = true;
    }

    private static int IndexOf<T>(IReadOnlyList<T> list, Predicate<T> match)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (match(list[i]))
            {
                return i;
            }
        }

        return -1;
    }

    // Knows a technology and has the experience and goods made that what comes after it asks for.
    private void Learn(Civ civ, int tech)
    {
        civ.KnownTech[tech] = true;
        if (tech < civ.Experience.Length)
        {
            civ.Experience[tech] = Math.Max(civ.Experience[tech], 1_000_000L * SimContent.TicksPerMinute);
        }

        foreach ((int g, long amount) in Content.Techs[tech].Made)
        {
            civ.Made[g] = Math.Max(civ.Made[g], amount);
        }
    }

    // A finished building on the first free hex of the settlement's land where it may stand.
    private bool PlaceFinished(Civ civ, Settlement s, BuildingType t)
    {
        foreach (int cell in s.Cells)
        {
            if (CannotBuild(civ, cell, t, checkPrice: false) is null)
            {
                Building b = Place(civ, cell, t, SlotsFor(cell, t.Size), (long[])t.Price.Clone());
                b.State = BuildingState.Active;
                b.RemainingTicks = 0;
                b.WorkCap = Math.Max(1, t.BuildTicks);
                return true;
            }
        }

        return false;
    }

    // The nearest hex where settlers may found a camp, a little away from the centre.
    private int FreeSiteNear(int center)
    {
        int radius = Content.FoundMinDistance * 4;
        var area = new HexCoord[HexGlobe.RangeSize(radius)];
        int n = Geo.Range(Geo.FromIndex(center), radius, area);
        int best = -1;
        int bestD = int.MaxValue;
        for (int k = 0; k < n; k++)
        {
            int cell = Geo.Index(area[k]);
            int d = Geo.Distance(Geo.FromIndex(center), area[k]);
            if (d >= Content.FoundMinDistance + 2 && d < bestD && CannotFound(cell) is null && Map.Relief[cell] < 2)
            {
                best = cell;
                bestD = d;
            }
        }

        return best;
    }

    // Every hex known, as last seen now.
    private void Reveal(Civ civ)
    {
        for (int i = 0; i < Geo.CellCount; i++)
        {
            if (!civ.Known[i])
            {
                civ.Known[i] = true;
                if (civ.Vision[i] == 0)
                {
                    Remember(civ, i);
                }

                civ.NewlyKnown.Add(Map.Chunks.ChunkOfIndex(i));
            }
        }
    }

    private string? Debug(Civ civ, DebugAction action)
    {
        if (!DebugOn)
        {
            return "Debug actions work only in test scenarios.";
        }

        switch (action)
        {
            case DebugAction.Goods:
                foreach (int sid in civ.Settlements)
                {
                    for (int g = 0; g < Settlements[sid].Stock.Length; g++)
                    {
                        Settlements[sid].Stock[g] += 200 * SimContent.One;
                    }
                }

                break;
            case DebugAction.FinishResearch when civ.Researching >= 0:
                civ.ResearchEnd = Ticks + 1;
                _schedule.Enqueue((DoneResearch, civ.Id), (civ.ResearchEnd, DoneResearch, civ.Id));
                break;
            case DebugAction.RevealMap:
                Reveal(civ);
                break;
            case DebugAction.People:
                foreach (int sid in civ.Settlements)
                {
                    Settlements[sid].People += 10;
                }

                break;
            case DebugAction.NextEra:
                foreach (SimTech t in Content.Techs)
                {
                    if (t.Era <= civ.Era)
                    {
                        Learn(civ, t.Id);
                    }
                }

                AdvanceEra(civ, true);
                if (civ.Researching >= 0 && civ.KnownTech[civ.Researching])
                {
                    civ.Researching = -1; // B8 (tests 2026-11-20): the debug key taught what was being researched
                }

                break;
            default:
                return "Nothing to do.";
        }

        civ.Dirty = true;
        News.Add((civ.Id, $"Debug: {action}."));
        return null;
    }
}
