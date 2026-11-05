using System;
using System.Collections.Generic;

namespace CivSim.Core.WorldGen;

/// <summary>
/// Values a player, a preset or the command line sets on top of a starting planet (stage 1c-A). Every value is
/// optional; a missing one comes from the starting planet. Star and orbit values are in thousandths.
/// </summary>
public sealed record PlanetOverrides
{
    /// <summary>Axial tilt, tenths of a degree.</summary>
    public int? AxialTiltTenths { get; init; }

    /// <summary>Eccentricity, thousandths.</summary>
    public int? EccentricityThousandths { get; init; }

    /// <summary>Star brightness, thousandths of the Sun's.</summary>
    public long? LuminosityThousandths { get; init; }

    /// <summary>Distance, thousandths of an astronomical unit.</summary>
    public long? DistanceThousandths { get; init; }

    /// <summary>Year, thousandths of an Earth year.</summary>
    public long? YearThousandths { get; init; }

    /// <summary>Light on the planet, thousandths of Earth's.</summary>
    public long? LightThousandths { get; init; }

    /// <summary>CO₂, millionths of a bar. Missing: from the carbon cycle (or the starting planet's when it was set by hand).</summary>
    public int? Co2Microbar { get; init; }

    /// <summary>Greenhouse warming, tenths of a degree: sets the CO₂ that gives it. Cannot be combined with <see cref="Co2Microbar"/>.</summary>
    public int? GreenhouseTenths { get; init; }

    /// <summary>Total air pressure, hundredths of a bar: the other gases are set so that, with the CO₂, it gives this.</summary>
    public int? PressureHundredths { get; init; }

    /// <summary>Volcanic activity, percent of Earth's.</summary>
    public int? VolcanismPercent { get; init; }
}

/// <summary>Builds a planet from a starting planet and <see cref="PlanetOverrides"/> (decisions of 2026-11-05).</summary>
public static class PlanetSetup
{
    /// <summary>
    /// The planet with the overrides applied. Star and orbit: no value keeps the starting star and distance; a distance
    /// or a light alone keeps the starting star; a brightness or a year alone keeps the starting light (an 84-year year
    /// on an Earth-like planet gets a bright, heavy star far away); two values are solved together
    /// (<see cref="StarOrbit.Solve"/>; the later one in the order brightness, distance, year, light is kept if the zone
    /// is in the way). Air: CO₂ set by hand (directly
    /// or through the greenhouse warming) wins; otherwise, when <paramref name="autoCo2"/> is true, it comes from the
    /// carbon cycle for the final star, orbit, air and volcanism, and stays as in the starting planet when false.
    /// </summary>
    /// <returns>The planet and plain-English notes on values that had to be adjusted.</returns>
    public static (PlanetParameters Planet, IReadOnlyList<string> Notes) Build(PlanetParameters start, bool autoCo2, PlanetOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(overrides);
        var notes = new List<string>();
        var given = new List<(OrbitQuantity Kind, long Value)>(4);
        Add(given, OrbitQuantity.Luminosity, overrides.LuminosityThousandths);
        Add(given, OrbitQuantity.Distance, overrides.DistanceThousandths);
        Add(given, OrbitQuantity.Year, overrides.YearThousandths);
        Add(given, OrbitQuantity.Light, overrides.LightThousandths);

        int l = start.LuminosityThousandths;
        int d = start.DistanceThousandths;
        bool clamped = false;
        switch (given.Count)
        {
            case 0:
                break;
            case 1 when given[0].Kind is OrbitQuantity.Light or OrbitQuantity.Distance:
                (l, d, clamped) = StarOrbit.Solve(given[0].Kind, given[0].Value, OrbitQuantity.Luminosity, l);
                break;
            case 1:
                (l, d, clamped) = StarOrbit.Solve(given[0].Kind, given[0].Value, OrbitQuantity.Light, Math.Max(1, (start.LightMillionths + 500) / 1000));
                break;
            case 2:
                (l, d, clamped) = StarOrbit.Solve(given[0].Kind, given[0].Value, given[1].Kind, given[1].Value);
                break;
            default:
                throw new ArgumentException("Give at most two of star brightness, distance, year and light: the others follow from them.");
        }

        if (clamped)
        {
            notes.Add("The planet would have left the habitable zone, so it was placed on the zone's edge.");
        }

        if (overrides.Co2Microbar is not null && overrides.GreenhouseTenths is not null)
        {
            throw new ArgumentException("Set either the CO2 or the greenhouse warming: one follows from the other.");
        }

        var planet = start with
        {
            AxialTiltTenths = overrides.AxialTiltTenths ?? start.AxialTiltTenths,
            EccentricityThousandths = overrides.EccentricityThousandths ?? start.EccentricityThousandths,
            LuminosityThousandths = l,
            DistanceThousandths = d,
            VolcanismPercent = overrides.VolcanismPercent ?? start.VolcanismPercent,
        };

        int? co2 = overrides.Co2Microbar ?? (overrides.GreenhouseTenths is int gh ? CarbonCycle.Co2ForGreenhouse(gh) : null);
        int background = planet.BackgroundPressureHundredths;
        if (co2 is null && autoCo2)
        {
            // The total pressure depends on the CO₂ and the CO₂ on the pressure: settle the other gases first.
            if (overrides.PressureHundredths is int total)
            {
                background = Math.Max(PlanetParameters.BackgroundPressureRange.Min, total - CarbonCycle.PressureHundredths(0, planet.Co2Microbar));
            }

            co2 = CarbonCycle.EquilibriumCo2Microbar(planet.AxialTiltTenths, planet.EccentricityThousandths, planet.LightMillionths, background, planet.VolcanismPercent);
        }

        int finalCo2 = co2 ?? planet.Co2Microbar;
        if (overrides.PressureHundredths is int p)
        {
            background = p - CarbonCycle.PressureHundredths(0, finalCo2);
            if (background < PlanetParameters.BackgroundPressureRange.Min)
            {
                background = PlanetParameters.BackgroundPressureRange.Min;
                notes.Add("The CO2 alone is thicker than the pressure asked for, so the pressure is higher.");
            }
        }

        planet = planet with { Co2Microbar = finalCo2, BackgroundPressureHundredths = background };
        planet.Validate();
        return (planet, notes);
    }

    private static void Add(List<(OrbitQuantity, long)> list, OrbitQuantity kind, long? value)
    {
        if (value is long v)
        {
            list.Add((kind, v));
        }
    }
}
