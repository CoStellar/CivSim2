using CivSim.Sim.Content;
using CivSim.Sim.State;

namespace CivSim.Sim.Tests;

/// <summary>Shared set-up for the tests.</summary>
internal static class TestKit
{
    /// <summary>Every civilization knows the basics of the Stone Age and Flint weapons (warriors), as after its first minutes.</summary>
    public static SimGame Basics(this SimGame g)
    {
        foreach (Civ c in g.Civs)
        {
            foreach (SimTech t in g.Content.Techs)
            {
                if (t.Basic || t.Key == "flint_weapons")
                {
                    c.KnownTech[t.Id] = true;
                }
            }
        }

        return g;
    }
}
