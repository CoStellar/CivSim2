using System;
using System.Collections.Generic;
using System.Text.Json;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>An event of the shared pool (drought, hard winter…). Amounts in percent.</summary>
public sealed record SimEvent(int Id, string Key, string Name, int Ticks, bool[] Goods, int ProductionPercent, int EatPercent, int LosePercentPerMinute, bool DestroyBuilding, string Field, string Advice);

public sealed partial class SimGame
{
    /// <summary>Draws the next event for a people when its time has come, ends the one that ran out.</summary>
    private void EventStep(Civ civ)
    {
        IReadOnlyList<SimEvent> pool = Content.Events;
        if (pool.Count == 0)
        {
            return;
        }

        if (civ.Event >= 0 && Ticks >= civ.EventEnd)
        {
            News.Add((civ.Id, $"The {pool[civ.Event].Name.ToLowerInvariant()} is over."));
            civ.Event = -1;
            civ.Dirty = true;
        }

        if (civ.NextEvent == 0)
        {
            civ.NextEvent = Ticks + Content.EventEveryTicks + _events.NextInt(Content.EventSpreadTicks + 1);
        }

        if (civ.Event >= 0 || Ticks < civ.NextEvent)
        {
            return;
        }

        // One no other people has right now.
        var free = new List<int>();
        foreach (SimEvent e in pool)
        {
            bool taken = false;
            foreach (Civ other in Civs)
            {
                taken |= other.Id != civ.Id && other.Event == e.Id;
            }

            if (!taken)
            {
                free.Add(e.Id);
            }
        }

        civ.NextEvent = Ticks + Content.EventEveryTicks + _events.NextInt(Content.EventSpreadTicks + 1);
        if (free.Count == 0)
        {
            return;
        }

        SimEvent ev = pool[free[_events.NextInt(free.Count)]];
        civ.Event = ev.Id;
        civ.EventEnd = Ticks + ev.Ticks;
        civ.Dirty = true;
        News.Add((civ.Id, $"{ev.Name}! It will last about {ev.Ticks / SimContent.TicksPerSecond} s."));
        if (ev.DestroyBuilding)
        {
            var finished = new List<int>();
            foreach (int id in civ.Buildings)
            {
                if (Buildings[id].State == BuildingState.Active)
                {
                    finished.Add(id);
                }
            }

            if (finished.Count > 0)
            {
                Building b = Buildings[finished[_events.NextInt(finished.Count)]];
                b.State = BuildingState.Removed;
                BuildingAt[b.Cell] = -1;
                civ.Buildings.Remove(b.Id);
                Map.MarkCellDirty(b.Cell);
                News.Add((civ.Id, $"The fire burned our {Content.Buildings[b.Type].Name.ToLowerInvariant()}."));
            }
        }
    }

    /// <summary>Production percent of a good under the people's current event.</summary>
    public int EventProduction(Civ civ, int good)
    {
        ArgumentNullException.ThrowIfNull(civ);
        if (civ.Event < 0)
        {
            return 100;
        }

        SimEvent e = Content.Events[civ.Event];
        return e.Goods[good] ? 100 - ((100 - e.ProductionPercent) * Harm(civ, e.Key) / 100) : 100;
    }

    /// <summary>Loads the event pool from <c>game.json</c>.</summary>
    public static List<SimEvent> LoadEvents(JsonElement root, IReadOnlyList<SimGood> goods)
    {
        var list = new List<SimEvent>();
        if (!root.TryGetProperty("events", out JsonElement ev))
        {
            return list;
        }

        foreach (JsonElement e in ev.GetProperty("pool").EnumerateArray())
        {
            var g = new bool[goods.Count];
            if (e.TryGetProperty("goods", out JsonElement ge))
            {
                foreach (JsonElement k in ge.EnumerateArray())
                {
                    foreach (SimGood good in goods)
                    {
                        g[good.Id] |= good.Key == k.GetString();
                    }
                }
            }

            list.Add(new SimEvent(
                list.Count, e.GetProperty("key").GetString()!, e.GetProperty("name").GetString()!, e.GetProperty("seconds").GetInt32() * SimContent.TicksPerSecond, g,
                e.TryGetProperty("productionPercent", out JsonElement pp) ? pp.GetInt32() : 100, e.TryGetProperty("eatPercent", out JsonElement ep) ? ep.GetInt32() : 100,
                e.TryGetProperty("losePercentPerMinute", out JsonElement lp) ? lp.GetInt32() : 0, e.TryGetProperty("destroyBuilding", out JsonElement db) && db.GetBoolean(),
                e.GetProperty("field").GetString()!, e.GetProperty("advice").GetString()!));
        }

        return list;
    }
}
