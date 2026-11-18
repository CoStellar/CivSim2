namespace CivSim.Sim.State;

public sealed partial class SimGame
{
    /// <summary>Hash of the game state for determinism tests (FNV-1a over the state that matters).</summary>
    public ulong StateHash()
    {
        ulong h = 14695981039346656037UL;
        void Add(long v)
        {
            h = (h ^ (ulong)v) * 1099511628211UL;
        }

        Add(Ticks);
        long roads = 0;
        for (int i = 0; i < RoadAt.Length; i++)
        {
            roads = (roads * 31) + RoadAt[i];
        }

        Add(roads);
        foreach (Civ c in Civs)
        {
            Add(c.People);
            Add(c.Hungry ? 1 : 0);
            Add(c.Era);
            foreach (long s in c.Stock)
            {
                Add(s);
            }

            foreach (long r in c.Remainder)
            {
                Add(r);
            }

            foreach (long m in c.Made)
            {
                Add(m);
            }
        }

        for (int i = 0; i < WorksAt.Length; i++)
        {
            if (WorksAt[i] != 0)
            {
                Add(i);
                Add(WorksAt[i]);
                Add(WallLeft[i]);
            }
        }

        foreach (Settlement s in Settlements)
        {
            Add(s.Civ);
            Add(s.Level);
            Add(s.People);
            Add(s.GrowthRemainder);
            Add(s.Cells.Count);
            Add(s.Gathering ? 1 : 0);
            foreach (long v in s.Stock)
            {
                Add(v);
            }
        }

        foreach (Building b in Buildings)
        {
            Add(b.Type);
            Add(b.Cell);
            Add((long)b.State);
            Add(b.FinishTick);
            Add(b.Speed);
            Add(b.WorkCap);
            Add(b.Porters);
            Add(b.Workers);
            Add(b.Starved ? 1 : 0);
        }

        return h;
    }
}
