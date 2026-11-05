using System;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.WorldGen;

/// <summary>
/// The climate changes during the game (stage 1c-B, decision of 2026-11-05). Every
/// <see cref="ClimateCycleRules.UpdateYears"/> the orbit moves on its Milankovitch cycles, the ice sheets of both
/// hemispheres grow or melt (<see cref="ClimateCycles"/>), the oceans take up CO₂ in glacials, and the energy balance is
/// solved again with the sheets on the world's real land. Then every cell gets its new temperature, seasons and growing
/// season, and land cells their new biome, so wild food follows the climate. Runs first in the world phase.
/// </summary>
public sealed class ClimateSystem(GameContent content) : ITurnSystem
{
    private PlanetParameters? _planet;
    private ClimateCycles? _cycles;
    private long _northPotential;
    private long _southPotential;
    private long[]? _landShare;
    private WorldMap? _landMap;

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.World;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        WorldState world = state.Worlds[0];
        ClimateState climate = world.Climate;
        long year = state.Month / 12;
        if (year < climate.Year + content.ClimateCycles.UpdateYears)
        {
            return;
        }

        Prepare(state.Settings.Planet, world.Map);
        _cycles!.Advance(climate, year);
        Apply(content, world, _cycles, _landShare!, _northPotential, _southPotential);
    }

    /// <summary>
    /// Solves the climate of <paramref name="world"/> for its <see cref="WorldState.Climate"/> and writes it onto the map
    /// (also used by tests and the headless run to show a given moment).
    /// </summary>
    public static void Apply(GameContent content, WorldState world, ClimateCycles cycles, long[] landShare, long northPotential, long southPotential)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(cycles);
        ClimateState climate = world.Climate;
        PlanetParameters orbit = cycles.OrbitAt(climate.Year);
        long[] sheets = cycles.SheetCover(climate, landShare, northPotential, southPotential);
        int co2 = cycles.GlacialCo2(orbit.Co2Microbar, cycles.LargestSheet(climate, northPotential, southPotential));
        var model = new ClimateModel(orbit with { Co2Microbar = co2 }, sheets);
        climate.Co2Microbar = co2;
        climate.GlobalMeanMillionths = model.GlobalMeanMillionths;

        WorldMap map = world.Map;
        for (int i = 0; i < map.Geometry.CellCount; i++)
        {
            (int t, int swing, int growing) = CellClimate.Of(model, map, i);
            if (map.Temperature[i] != t)
            {
                map.Temperature.Set(i, (sbyte)t);
            }

            if (map.Seasonality[i] != swing)
            {
                map.Seasonality.Set(i, (byte)swing);
            }

            if (map.GrowingSeason[i] != growing)
            {
                map.GrowingSeason.Set(i, (byte)growing);
            }

            if (content.Biomes[map.Biome[i]].Kind == BiomeKind.Land)
            {
                int biome = CellClimate.LandBiome(content, t, map.Humidity[i], (Relief)map.Relief[i], swing, growing);
                if (map.Biome[i] != biome)
                {
                    map.Biome.Set(i, (byte)biome);
                }
            }
        }
    }

    /// <summary>Share of land in each energy-balance band of a map, millionths (bands by the cells' true latitude).</summary>
    public static long[] LandShare(GameContent content, WorldMap map)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(map);
        var land = new long[EnergyBalance.Bands];
        var cells = new long[EnergyBalance.Bands];
        HexGlobe geo = map.Geometry;
        for (int i = 0; i < geo.CellCount; i++)
        {
            long sin = geo.SinLatitude(geo.FromIndex(i), FixedTrig.One);
            int band = (int)Math.Clamp((sin + FixedTrig.One) * EnergyBalance.Bands / (2 * FixedTrig.One), 0, EnergyBalance.Bands - 1);
            cells[band]++;
            if (content.Biomes[map.Biome[i]].Kind == BiomeKind.Land)
            {
                land[band]++;
            }
        }

        for (int b = 0; b < EnergyBalance.Bands; b++)
        {
            land[b] = cells[b] == 0 ? 0 : land[b] * 1_000_000 / cells[b];
        }

        return land;
    }

    /// <summary>
    /// How far each hemisphere can glaciate (millionths), from its summer temperature at 65° at the start of the game
    /// (inland summer: mean plus half the swing at 70% continentality).
    /// </summary>
    public static (long North, long South) Potentials(ClimateCycles cycles)
    {
        ArgumentNullException.ThrowIfNull(cycles);
        var start = new ClimateModel(cycles.OrbitAt(0));
        long north = FixedTrig.Sin(FixedTrig.Degrees(65));
        int summerNorth = start.MeanTemperature(north) + (start.SeasonalSwing(north, 70) / 2);
        int summerSouth = start.MeanTemperature(-north) + (start.SeasonalSwing(-north, 70) / 2);
        return (cycles.Potential(summerNorth), cycles.Potential(summerSouth));
    }

    private void Prepare(PlanetParameters planet, WorldMap map)
    {
        if (_cycles is null || _planet != planet)
        {
            _planet = planet;
            _cycles = new ClimateCycles(planet, content.ClimateCycles);
            (_northPotential, _southPotential) = Potentials(_cycles);
        }

        // The land does not move until the sea level changes (stage 1c-C), so it is measured once per map.
        if (_landShare is null || !ReferenceEquals(_landMap, map))
        {
            _landShare = LandShare(content, map);
            _landMap = map;
        }
    }
}
