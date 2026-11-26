using System.Collections.Generic;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Protocol.Messages;

namespace CivSim.Server;

/// <summary>
/// Builds the view one player may see. Today every civilization's name is public; fog of war and map memory
/// (Stage 3g) will filter here, on the server, so the client never receives hidden information.
/// </summary>
public static class ViewBuilder
{
    /// <summary>
    /// Creates the view of <paramref name="viewer"/> from a finished snapshot. <paramref name="knownEarthCells"/> is
    /// null for observers (they see everything); for a player it marks the Earth cells the player knows, and
    /// foreign settlements elsewhere are left out. Warehouse contents are sent only for the player's own settlements.
    /// </summary>
    public static GameViewMessage Build(
        GameSnapshot snapshot, CivId viewer, bool[]? knownEarthCells = null, int earthWidth = 0, System.Func<int, bool>? seesNow = null)
    {
        var worlds = new List<WorldView>(snapshot.Worlds.Length);
        foreach (WorldSnapshot w in snapshot.Worlds)
        {
            worlds.Add(new WorldView(w.Id.Value, w.Name, w.Width, w.Height));
        }

        var civs = new List<CivilizationView>(snapshot.Civilizations.Length);
        foreach (CivilizationSnapshot c in snapshot.Civilizations)
        {
            civs.Add(new CivilizationView(c.Id.Value, c.Name, c.IsAlive, c.DiedOutMonth));
        }

        bool observer = knownEarthCells is null;
        var settlements = new List<SettlementView>(snapshot.Settlements.Length);
        foreach (SettlementSnapshot st in snapshot.Settlements)
        {
            bool own = st.Owner == viewer;
            if (!observer && !own && (st.World != WorldId.Earth || !knownEarthCells![(st.Row * earthWidth) + st.Col]))
            {
                continue;
            }

            // Whether a settlement stands empty and who claims it can be seen from outside; the warehouse cannot.
            bool details = observer || own;
            SettlementView view = details
                ? new SettlementView(
                    st.Id.Value, st.Owner.Value, st.World.Value, st.Col, st.Row, st.Name, st.Level, [.. st.Stock],
                    [.. st.Population], st.Health, st.Mood, st.FoodGathered, st.FoodNeeded, st.FoodEaten) with { Loyalty = st.Loyalty }
                : new SettlementView(st.Id.Value, st.Owner.Value, st.World.Value, st.Col, st.Row, st.Name, st.Level, null);
            settlements.Add(view with
            {
                Status = (int)st.Status,
                AbandonedMonth = st.AbandonedMonth,
                ClaimCiv = st.ClaimCiv,
                // Claims run in months of action; the viewer gets them on its year counter (remaining months from now).
                ClaimUntilMonth = snapshot.Month + (st.ClaimUntilMonth - snapshot.ActionMonth),
            });
        }

        // The capital is shown only where the viewer sees the settlement itself.
        for (int k = 0; k < civs.Count; k++)
        {
            int capital = snapshot.Civilizations[k].Capital;
            if (capital >= 0 && settlements.Exists(s => s.Id == capital))
            {
                civs[k] = civs[k] with { Capital = capital };
            }
        }

        // Units (stage 6 PR 3a): a player sees its own and the foreign ones on Earth cells it sees now.
        var units = new List<UnitView>(snapshot.Units.Length);
        foreach (UnitSnapshot u in snapshot.Units)
        {
            if (!observer && u.Owner != viewer && (u.World != WorldId.Earth || seesNow is null || !seesNow(u.Cell)))
            {
                continue;
            }

            units.Add(new UnitView(
                u.Id, (int)u.Kind, u.Owner.Value, u.World.Value, u.Col, u.Row, u.NextCol, u.NextRow, u.ProgressPermille, u.People, (int)u.Order) { Role = (int)u.Role, Task = u.Task, TargetCol = u.TargetCol, TargetRow = u.TargetRow, FoodMonths = u.Owner == viewer || observer ? u.FoodMonths : -1 });
        }

        return new GameViewMessage(
            snapshot.Turn, viewer.Value, worlds, civs, Settlements: settlements, Month: snapshot.Month, LiveSeasons: snapshot.LiveSeasons, Units: units);
    }
}
