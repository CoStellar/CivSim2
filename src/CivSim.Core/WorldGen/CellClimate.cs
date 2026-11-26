using System;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Numerics;

namespace CivSim.Core.WorldGen;

/// <summary>
/// Climate of one cell from the planet's climate and what the map keeps about the cell (stage 1c-B): its climate
/// latitude, the temperature offset of its height and local noise, and how continental it is. The generator and the
/// climate updates during the game use the same rules, so a world solved again under the same climate is unchanged.
/// </summary>
public static class CellClimate
{
    /// <summary>A sine in parts of <see cref="FixedTrig.One"/> as stored in <see cref="WorldMap.ClimateSine"/>.</summary>
    public static short StoredSine(long sine) =>
        (short)Math.Clamp(IntMath.Divide(sine * WorldMap.ClimateSineOne, FixedTrig.One), -WorldMap.ClimateSineOne, WorldMap.ClimateSineOne);

    /// <summary>The stored climate sine of a cell, back in parts of <see cref="FixedTrig.One"/>.</summary>
    public static long Sine(WorldMap map, int cell)
    {
        ArgumentNullException.ThrowIfNull(map);
        return (long)map.ClimateSine[cell] * FixedTrig.One / WorldMap.ClimateSineOne;
    }

    /// <summary>Annual mean temperature (°C, −30…40), summer–winter swing (°C) and growing season (%) of a cell.</summary>
    public static (int Temperature, int Swing, int Growing) Of(ClimateModel climate, WorldMap map, int cell)
    {
        ArgumentNullException.ThrowIfNull(climate);
        ArgumentNullException.ThrowIfNull(map);
        long sine = Sine(map, cell);
        int temperature = Math.Clamp(climate.MeanTemperature(sine) + map.TemperatureOffset[cell], -30, 40);
        int swing = climate.SeasonalSwing(sine, map.Continentality[cell]);
        return (temperature, swing, ClimateModel.GrowingSeason(temperature, swing));
    }

    /// <summary>
    /// Land biome for a climate (stages 1b and 1c-B): land whose summer stays below freezing is ice; land with almost no
    /// growing season is tundra; very large swings between summer and winter act like drought, so forests give way to
    /// steppe and desert. Otherwise the first biome in data/biomes.json whose temperature and humidity fit.
    /// </summary>
    public static int LandBiome(GameContent content, int temperature, int humidity, Relief relief, int swing, int growing)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (temperature + (swing / 2) < 0)
        {
            temperature = Math.Min(temperature, -12);
        }
        else if (growing < 15 && temperature > -11)
        {
            return content.Biome("tundra").Id;
        }

        humidity = Math.Max(0, humidity - (Math.Max(0, swing - 40) / 2));
        foreach (BiomeDefinition b in content.Biomes)
        {
            if (b.Kind != BiomeKind.Land)
            {
                continue;
            }

            if (temperature < b.MinTemperature || temperature > b.MaxTemperature || humidity < b.MinHumidity || humidity > b.MaxHumidity)
            {
                continue;
            }

            // Swamps only form on flat ground.
            if (b.Key == "swamp" && relief != Relief.Flat)
            {
                continue;
            }

            return b.Id;
        }

        return content.Biome("grassland").Id;
    }
}
