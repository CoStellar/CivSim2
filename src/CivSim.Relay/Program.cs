using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace CivSim.Relay;

/// <summary>Starts the relay with its settings and runs it until Ctrl+C or <c>docker stop</c>.</summary>
public static class Program
{
    /// <summary>Entry point.</summary>
    public static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--help") >= 0 || Array.IndexOf(args, "-h") >= 0)
        {
            Console.WriteLine(RelayConfig.Usage);
            return 0;
        }

        RelayConfig config;
        try
        {
            var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (DictionaryEntry e in Environment.GetEnvironmentVariables())
            {
                environment[(string)e.Key] = e.Value as string;
            }

            config = RelayConfig.Load(args, environment);
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine(RelayConfig.Usage);
            return 2;
        }

        using var stop = new ManualResetEventSlim();
        using PosixSignalRegistration term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c =>
        {
            c.Cancel = true;
            stop.Set();
        });
        using PosixSignalRegistration interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, c =>
        {
            c.Cancel = true;
            stop.Set();
        });
        Console.Out.Flush();
        using var server = new RelayServer(config, Console.Out);
        stop.Wait();
        Console.WriteLine("Relay stopped.");
        return 0;
    }
}
