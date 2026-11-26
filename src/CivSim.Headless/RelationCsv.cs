using System;
using System.Globalization;
using System.IO;
using CivSim.Core.Diplomacy;
using CivSim.Core.State;

namespace CivSim.Headless;

/// <summary>Writes what every civilization knows of the others (GDD 8.5; stage 4c) as CSV rows, one per known civilization.</summary>
public static class RelationCsv
{
    /// <summary>Column names.</summary>
    public const string Header = "turn,year,civ,other,level,way,first_year,met_year,sympathy,trust,memories,attacks,disputed";

    /// <summary>Writes one row for every relation of every living civilization.</summary>
    public static void Write(TextWriter writer, GameState state)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(state);
        long year = state.Month / 12;
        foreach (CivilizationState civ in state.Civilizations)
        {
            if (!civ.IsAlive)
            {
                continue;
            }

            foreach (Relation r in civ.Relations)
            {
                int attacks = 0;
                bool disputed = false;
                foreach (RelationMemory m in r.Memories)
                {
                    attacks += m.Kind == RelationEvent.Attack ? 1 : 0;
                    disputed |= m.Kind == RelationEvent.BorderDispute && m.IsOngoing;
                }

                string other = state.FindCivilization(r.Other)?.Name ?? r.Other.Value.ToString(CultureInfo.InvariantCulture);
                writer.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{state.Turn},{year},{civ.Name},{other},{r.Level},{r.Way},{r.FirstMonth / 12},{(r.HasMet ? r.MetMonth / 12 : -1)},"
                    + $"{r.Sympathy / 100.0:0.00},{r.Trust / 100.0:0.00},{r.Memories.Count},{attacks},{(disputed ? 1 : 0)}"));
            }
        }
    }
}
