using System;
using CivSim.Core.Numerics;

namespace CivSim.Core.WorldGen;

/// <summary>
/// The planet a world is on (GDD 3, stages 1b and 1c-A). Its star and orbit follow the real formulas (decision of
/// 2026-11-05, <see cref="StarOrbit"/>): the light, the star's mass and the year follow from the star's brightness and
/// the distance, and the planet always lies in the habitable zone. The air is CO₂ on top of other gases (nitrogen,
/// oxygen); the CO₂ gives the greenhouse warming and comes from the carbon cycle (<see cref="CarbonCycle"/>) unless set
/// by hand. The climate follows through <see cref="EnergyBalance"/>. All values are integers so they can be saved,
/// hashed and compared exactly.
/// </summary>
/// <param name="AxialTiltTenths">Tilt of the spin axis against the orbit, in tenths of a degree (Earth 234, Uranus 978).</param>
/// <param name="EccentricityThousandths">How stretched the orbit is, in thousandths (Earth 17, Mars 93).</param>
/// <param name="LuminosityThousandths">Brightness of the star in thousandths of the Sun's (Sun 1000).</param>
/// <param name="DistanceThousandths">Distance from the star in thousandths of an astronomical unit (Earth 1000, Mars 1524).</param>
/// <param name="Co2Microbar">CO₂ in the air, millionths of a bar (Earth before industry: 280).</param>
/// <param name="BackgroundPressureHundredths">Pressure of the other gases (nitrogen, oxygen), hundredths of a bar (Earth 100).</param>
/// <param name="VolcanismPercent">Volcanic activity compared with Earth's: how much CO₂ volcanoes give (Earth 100).</param>
public sealed record PlanetParameters(
    int AxialTiltTenths,
    int EccentricityThousandths,
    int LuminosityThousandths,
    int DistanceThousandths,
    int Co2Microbar,
    int BackgroundPressureHundredths,
    int VolcanismPercent)
{
    /// <summary>Earth.</summary>
    public static PlanetParameters Earth { get; } = new(234, 17, 1000, 1000, CarbonCycle.EarthCo2Microbar, 100, 100);

    /// <summary>Smallest and largest allowed values.</summary>
    public static (int Min, int Max) TiltRange => (0, 1800);

    /// <inheritdoc cref="TiltRange"/>
    public static (int Min, int Max) YearRange => (10, 50_000);

    /// <inheritdoc cref="TiltRange"/>
    public static (int Min, int Max) EccentricityRange => (0, 300);

    /// <inheritdoc cref="TiltRange"/>
    public static (int Min, int Max) GreenhouseRange => (0, 1500);

    /// <inheritdoc cref="TiltRange"/>
    public static (int Min, int Max) BackgroundPressureRange => (10, 5_000);

    /// <inheritdoc cref="TiltRange"/>
    public static (int Min, int Max) Co2Range => (1, CarbonCycle.MaxCo2Microbar);

    /// <inheritdoc cref="TiltRange"/>
    public static (int Min, int Max) VolcanismRange => (0, 5_000);

    /// <summary>Greenhouse warming from the CO₂, tenths of a degree Celsius (Earth 330).</summary>
    public int GreenhouseTenths => CarbonCycle.GreenhouseTenths(Co2Microbar);

    /// <summary>Air pressure at sea level, hundredths of a bar: other gases plus CO₂ (Earth 100).</summary>
    public int PressureHundredths => CarbonCycle.PressureHundredths(BackgroundPressureHundredths, Co2Microbar);

    /// <inheritdoc cref="TiltRange"/>
    public static (int Min, int Max) SeasonStrengthRange => (20, 150);

    /// <summary>Sunlight reaching the planet compared with Earth, millionths (S = L / d²; Earth 1 000 000).</summary>
    public long LightMillionths => StarOrbit.LightMillionths(LuminosityThousandths, DistanceThousandths);

    /// <summary>Length of the year in Earth years × 100 (Kepler). Changes only the seasons: the game clock stays in Earth years.</summary>
    public int YearHundredths => (int)((StarOrbit.YearThousandths(LuminosityThousandths, DistanceThousandths) + 5) / 10);

    /// <summary>Mass of the star, thousandths of the Sun's (main sequence).</summary>
    public int StarMassThousandths => StarOrbit.MassThousandths(LuminosityThousandths);

    /// <summary>
    /// How strong the seasons are, in percent of Earth's (decision of 2026-11-05: it follows from the pressure). Thick
    /// air holds and spreads heat between summer and winter: strength = 100 × √(1 bar / pressure), within
    /// <see cref="SeasonStrengthRange"/> (Mars-like 2.5 bar: 63%).
    /// </summary>
    public int SeasonStrengthPercent =>
        (int)Math.Clamp((IntMath.Sqrt(10_000_000_000L / Math.Max(1, PressureHundredths)) + 50) / 100, SeasonStrengthRange.Min, SeasonStrengthRange.Max);

    /// <summary>Throws when a value is outside its range or the planet is outside the habitable zone.</summary>
    public void Validate()
    {
        Check(AxialTiltTenths, TiltRange, "axial tilt");
        Check(EccentricityThousandths, EccentricityRange, "eccentricity");
        Check(LuminosityThousandths, StarOrbit.LuminosityRange, "star luminosity");
        Check(Co2Microbar, Co2Range, "CO2");
        Check(BackgroundPressureHundredths, BackgroundPressureRange, "air pressure");
        Check(VolcanismPercent, VolcanismRange, "volcanism");
        if (DistanceThousandths <= 0)
        {
            throw new ArgumentOutOfRangeException("distance", "The distance must be positive.");
        }

        // Stored values are rounded to thousandths, so allow the rounding of the zone edge.
        long light = LightMillionths;
        if (light < StarOrbit.ZoneOuterMillionths - 3000 || light > StarOrbit.ZoneInnerMillionths + 3000)
        {
            throw new ArgumentOutOfRangeException("distance", $"The light {light / 1000}‰ of Earth's is outside the habitable zone.");
        }

        Check(YearHundredths, YearRange, "year length");
    }

    private static void Check(int value, (int Min, int Max) range, string name)
    {
        if (value < range.Min || value > range.Max)
        {
            throw new ArgumentOutOfRangeException(name, $"The {name} {value} is outside {range.Min}..{range.Max}.");
        }
    }
}

/// <summary>
/// Sunlight over the year at one latitude: the daily mean at every step of the orbit, from the axial tilt, the
/// eccentricity and the light on the planet. Values are shares of the sunlight Earth gets at its average distance, in
/// parts of <see cref="FixedTrig.One"/> (Earth's equator gets about 0.30 on average over the year).
/// </summary>
public static class Insolation
{
    /// <summary>Points of the orbit sampled per year (equal time steps).</summary>
    public const int Steps = 48;

    /// <summary>Where the planet is closest to its star: at the northern winter solstice, like Earth.</summary>
    public static readonly int PerihelionLongitude = FixedTrig.Degrees(270);

    /// <summary>Annual mean, the darkest and the brightest step at a latitude given by its sine.</summary>
    public static (long Mean, long Min, long Max) At(long sinLatitude, PlanetParameters planet) => At(sinLatitude, planet, []);

    /// <summary>
    /// Like <see cref="At(long, PlanetParameters)"/>, and writes the sunlight of every step into <paramref name="steps"/>
    /// (when it has room for <see cref="Steps"/> values). Step 0 is the closest point to the star (northern winter).
    /// </summary>
    public static (long Mean, long Min, long Max) At(long sinLatitude, PlanetParameters planet, Span<long> steps)
    {
        ArgumentNullException.ThrowIfNull(planet);
        return At(sinLatitude, planet.AxialTiltTenths, planet.EccentricityThousandths, planet.LightMillionths, steps);
    }

    /// <summary>
    /// Like <see cref="At(long, PlanetParameters, Span{long})"/>, from the tilt (tenths of a degree), the eccentricity
    /// (thousandths) and the light on the planet (millionths of Earth's).
    /// </summary>
    public static (long Mean, long Min, long Max) At(long sinLatitude, int tiltTenths, int eccentricityThousandths, long lightMillionths, Span<long> steps)
    {
        long one = FixedTrig.One;
        long sinPhi = Math.Clamp(sinLatitude, -one, one);
        long cosPhi = FixedTrig.Complement(sinPhi);
        long sinTilt = FixedTrig.Sin(FixedTrig.TenthsOfDegree(tiltTenths));
        long e = eccentricityThousandths * one / 1000;
        long sum = 0;
        long min = long.MaxValue;
        long max = long.MinValue;
        for (int k = 0; k < Steps; k++)
        {
            // Equal time steps along the orbit (mean anomaly); the true anomaly runs ahead near the star.
            int m = FullTurnPart(k, Steps);
            long equationOfCentre = (2 * e * FixedTrig.Sin(m) / one) + (5 * e * e / one * FixedTrig.Sin(2 * m) / one / 4);
            int nu = m + (int)(equationOfCentre * FixedTrig.FullTurn / (2 * FixedTrig.Pi));
            long ratio = (one + (e * FixedTrig.Cos(nu) / one)) * one / (one - (e * e / one));
            long distance = ratio * ratio / one;

            // Declination of the star: how far north it stands at this point of the year.
            long sinDecl = sinTilt * FixedTrig.Sin(nu + PerihelionLongitude) / one;
            long cosDecl = FixedTrig.Complement(sinDecl);

            // Half the length of the day (hour angle of sunset), then the daily mean on a flat surface.
            long a = sinPhi * sinDecl / one;
            long b = cosPhi * cosDecl / one;
            int sunset = b == 0 ? (a > 0 ? FixedTrig.FullTurn / 2 : 0) : FixedTrig.Acos(-a * one / b);
            long daily = ((FixedTrig.ToRadians(sunset) * a / one) + (b * FixedTrig.Sin(sunset) / one)) * one / FixedTrig.Pi;
            long q = Math.Max(0, daily) * distance / one * lightMillionths / 1_000_000;
            sum += q;
            if (steps.Length >= Steps)
            {
                steps[k] = q;
            }

            min = Math.Min(min, q);
            max = Math.Max(max, q);
        }

        return (sum / Steps, min, max);
    }

    private static int FullTurnPart(int k, int steps) => (int)((long)k * FixedTrig.FullTurn / steps);
}

/// <summary>
/// Climate from sunlight (GDD 3, stages 1b and 1c-A). The annual mean temperature comes from the energy balance of
/// latitude bands (<see cref="EnergyBalance"/>, ADR 0014): Earth gets about 27 °C at the equator and −22 °C at the poles.
/// Seasons swing around it: more with a bigger difference between summer and winter sunlight, far from the sea and on
/// planets with long years; less over water and under thick air.
/// </summary>
public sealed class ClimateModel
{
    private readonly PlanetParameters _planet;
    private readonly int _yearHundredths;
    private readonly long[] _bands;
    private readonly (long Mean, long Min, long Max)[] _table = new (long, long, long)[TableSize + 1];
    private readonly long[] _steps = new long[(TableSize + 1) * Insolation.Steps];

    private const int TableSize = 512;

    /// <summary>Temperature above which plants grow, °C.</summary>
    public const int GrowingAbove = 5;

    /// <summary>Seasonal swing (°C, summer to winter) per unit of sunlight difference, at full continentality.</summary>
    public const int SwingPerSunlight = 121;

    /// <summary>Builds the model for <paramref name="planet"/>.</summary>
    public ClimateModel(PlanetParameters planet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        planet.Validate();
        _planet = planet;
        _yearHundredths = planet.YearHundredths;
        _bands = EnergyBalance.Solve(planet);
        for (int k = 0; k <= TableSize; k++)
        {
            long sin = -FixedTrig.One + (2 * FixedTrig.One * k / TableSize);
            _table[k] = Insolation.At(sin, planet, _steps.AsSpan(k * Insolation.Steps, Insolation.Steps));
        }
    }

    /// <summary>
    /// Temperature at one step of the year (0…<see cref="Insolation.Steps"/> − 1) for a place with this annual mean and
    /// seasonal swing: the swing is spread over the year in the same shape as the sunlight at that latitude, so on a
    /// planet lying on its side each hemisphere gets its long summer and its long winter in turn (live seasons).
    /// </summary>
    public int TemperatureAt(long sinLatitude, int step, int meanTemperature, int swing)
    {
        long s = Math.Clamp(sinLatitude, -FixedTrig.One, FixedTrig.One) + FixedTrig.One;
        int k = (int)Math.Min(TableSize, s * TableSize / (2 * FixedTrig.One));
        (long mean, long min, long max) = _table[k];
        if (max <= min)
        {
            return meanTemperature;
        }

        long q = _steps[(k * Insolation.Steps) + (((step % Insolation.Steps) + Insolation.Steps) % Insolation.Steps)];
        return meanTemperature + (int)((q - mean) * swing / (max - min));
    }

    /// <summary>True when a temperature lets plants grow (<see cref="GrowingAbove"/> to <see cref="GrowingBelow"/> °C).</summary>
    public static bool Grows(int temperature) => temperature > GrowingAbove && temperature < GrowingBelow;

    /// <summary>Sunlight over the year at a latitude (looked up in a table of 513 latitudes).</summary>
    public (long Mean, long Min, long Max) Sunlight(long sinLatitude)
    {
        long s = Math.Clamp(sinLatitude, -FixedTrig.One, FixedTrig.One) + FixedTrig.One;
        int k = (int)Math.Min(TableSize, s * TableSize / (2 * FixedTrig.One));
        return _table[k];
    }

    /// <summary>
    /// Annual mean temperature at sea level, °C: the energy-balance temperature of the latitude, interpolated between
    /// band centres.
    /// </summary>
    public int MeanTemperature(long sinLatitude) => (int)Math.Clamp(RoundToDegree(MeanTemperatureMillionths(sinLatitude)), -60, 80);

    /// <summary>Like <see cref="MeanTemperature"/>, in millionths of a degree (for reports and tests).</summary>
    public long MeanTemperatureMillionths(long sinLatitude)
    {
        long x = Math.Clamp(sinLatitude, -FixedTrig.One, FixedTrig.One) * EnergyBalance.Unit / FixedTrig.One;

        // Position in bands, in millionths of a band: band i has its centre at (i + 0.5).
        long pos = ((x + EnergyBalance.Unit) * EnergyBalance.Bands / 2) - (EnergyBalance.Unit / 2);
        if (pos <= 0)
        {
            return _bands[0];
        }

        int i = (int)(pos / EnergyBalance.Unit);
        if (i >= EnergyBalance.Bands - 1)
        {
            return _bands[^1];
        }

        long f = pos - ((long)i * EnergyBalance.Unit);
        return _bands[i] + ((_bands[i + 1] - _bands[i]) * f / EnergyBalance.Unit);
    }

    /// <summary>Mean temperature of the whole planet, millionths of a degree.</summary>
    public long GlobalMeanMillionths => EnergyBalance.GlobalMean(_bands);

    private static long RoundToDegree(long millionths) =>
        millionths >= 0 ? (millionths + 500_000) / 1_000_000 : -((-millionths + 500_000) / 1_000_000);

    /// <summary>
    /// Difference between the warmest and the coldest time of year, °C. <paramref name="continentalPercent"/> is 100 deep
    /// inland and lower near water.
    /// </summary>
    public int SeasonalSwing(long sinLatitude, int continentalPercent)
    {
        (_, long min, long max) = Sunlight(sinLatitude);
        long year = _yearHundredths;
        long yearPercent = year * 150 / (year + 50); // 100 for Earth; long years let summers and winters go further
        long swing = (max - min) * SwingPerSunlight / FixedTrig.One * continentalPercent / 100 * yearPercent / 100 * _planet.SeasonStrengthPercent / 100;
        return (int)Math.Clamp(swing, 0, 120);
    }

    /// <summary>
    /// Share of the full wild food a cell gives (0–100%) for its growing season and the planet's year (decision of
    /// 2026-11-05: long winters are a harder variant of the simulation). A growing season of 60% of the year or more
    /// gives everything, none gives 40%. A dead season longer than <paramref name="survivableDeadSeasonHundredths"/>
    /// (in Earth years × 100) cuts it further in proportion: gatherers cannot live through winters of many years
    /// until technologies (storage) let them.
    /// </summary>
    public static int WildFoodPercent(int growingSeason, int yearHundredths, int survivableDeadSeasonHundredths)
    {
        long percent = Math.Min(100, 40 + growingSeason);
        long dead = (long)(100 - growingSeason) * yearHundredths / 100;
        if (dead > survivableDeadSeasonHundredths)
        {
            percent = percent * survivableDeadSeasonHundredths / dead;
        }

        return (int)percent;
    }

    /// <summary>Temperature above which plants stop growing (heat), °C.</summary>
    public const int GrowingBelow = 45;

    /// <summary>
    /// Share of the year (0–100%) between <see cref="GrowingAbove"/> and <see cref="GrowingBelow"/> °C, for a year whose
    /// temperature swings like a sine wave around the mean.
    /// </summary>
    public static int GrowingSeason(int meanTemperature, int swing) =>
        Math.Max(0, ShareAbove(meanTemperature, swing, GrowingAbove) - ShareAbove(meanTemperature, swing, GrowingBelow));

    // Share of the year (0–100) warmer than `limit`.
    private static int ShareAbove(int mean, int swing, int limit)
    {
        int half = swing / 2;
        if (mean - half >= limit)
        {
            return 100;
        }

        if (mean + half <= limit)
        {
            return 0;
        }

        long x = (long)(limit - mean) * FixedTrig.One / half;
        return FixedTrig.Acos(x) * 100 / (FixedTrig.FullTurn / 2);
    }
}

/// <summary>
/// When the seasons are played turn by turn (decision of 2026-11-05, "two clocks"). On planets whose year is long enough
/// that one step of <see cref="StepsPerYear"/> lasts at least <see cref="MinStepMonths"/>, a turn is one step of the
/// planet's year (Uranus: 84 years / 8 = 10.5 years) unless the era's turn is already shorter, and the map shows the
/// season of that step. Elsewhere (Earth, Mars) seasons are averaged into the climate.
/// </summary>
public sealed record SeasonRules
{
    /// <summary>Steps of the planet's year when seasons are live.</summary>
    public int StepsPerYear { get; init; } = 8;

    /// <summary>Shortest step that is played: shorter steps would mean far more turns for nothing visible.</summary>
    public int MinStepMonths { get; init; } = 12;

    /// <summary>Share of the full wild food that hunting still gives when nothing grows (winter), percent.</summary>
    public int WinterFoodPercent { get; init; } = 20;

    /// <summary>Months in one step of the planet's year.</summary>
    public int StepMonths(PlanetParameters planet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        return (int)((long)planet.YearHundredths * 12 / 100 / StepsPerYear);
    }

    /// <summary>True when seasons are played turn by turn on this planet.</summary>
    public bool Live(PlanetParameters planet) => StepMonths(planet) >= MinStepMonths;
}
