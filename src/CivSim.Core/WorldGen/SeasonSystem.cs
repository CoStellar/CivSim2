using System;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.WorldGen;

/// <summary>
/// Live seasons (decision of 2026-11-05, "two clocks"): on planets with long years every turn is one step of the year,
/// and this system writes the season of the current and the next turn onto the map: the temperature in the middle of
/// the turn and the share of the turn in which plants grow. Gathering uses the current season; the tribe AI looks at
/// the next one. Runs first in the world phase. On other planets it only clears the flag.
/// </summary>
public sealed class SeasonSystem(GameContent content) : ITurnSystem
{
    private ClimateModel? _climate;
    private PlanetParameters? _planet;

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.World;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        WorldState world = state.Worlds[0];
        PlanetParameters planet = state.Settings.Planet;
        world.Map.YearHundredths = planet.YearHundredths;
        world.Map.LiveSeasons = content.Seasons.Live(planet);
        if (!world.Map.LiveSeasons)
        {
            return;
        }

        if (_climate is null || _planet != planet)
        {
            _climate = new ClimateModel(planet);
            _planet = planet;
        }

        int turn = Math.Max(1, state.StepMonths);
        long yearMonths = Math.Max(1, (long)planet.YearHundredths * 12 / 100);
        Write(world, state.Month, turn, yearMonths, world.Map.SeasonTemperature, world.Map.SeasonGrowth);
        Write(world, state.Month + turn, turn, yearMonths, world.Map.NextSeasonTemperature, world.Map.NextSeasonGrowth);
    }

    private void Write(WorldState world, long month, int turnMonths, long yearMonths, MapLayer<sbyte> temperature, MapLayer<byte> growth)
    {
        // The turn covers steps first…last of the 48 steps of the year.
        int first = (int)(month % yearMonths * Insolation.Steps / yearMonths);
        int last = (int)((month + turnMonths - 1) % yearMonths * Insolation.Steps / yearMonths);
        int count = ((last - first + Insolation.Steps) % Insolation.Steps) + 1;
        int middle = first + (count / 2);
        WorldMap map = world.Map;
        HexGlobe geo = world.Geometry;
        for (int i = 0; i < geo.CellCount; i++)
        {
            long sinLat = geo.SinLatitude(geo.FromIndex(i), FixedTrig.One);
            int mean = map.Temperature[i];
            int swing = map.Seasonality[i];
            int growing = 0;
            for (int k = 0; k < count; k++)
            {
                growing += ClimateModel.Grows(_climate!.TemperatureAt(sinLat, first + k, mean, swing)) ? 1 : 0;
            }

            temperature.Set(i, (sbyte)Math.Clamp(_climate!.TemperatureAt(sinLat, middle, mean, swing), -100, 100));
            growth.Set(i, (byte)(growing * 100 / count));
        }
    }
}
