using System;
using CivSim.Core;

namespace CivSim.Headless;

/// <summary>Entry point for running the simulation without a window.</summary>
public static class Program
{
    /// <summary>Runs the headless simulation. Arguments are added in Stage 0.</summary>
    public static int Main(string[] args)
    {
        Console.WriteLine($"{CoreInfo.Name} headless runner (no simulation yet).");
        return 0;
    }
}
