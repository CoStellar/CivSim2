using System;
using CivSim.Core.Numerics;

namespace CivSim.Core.WorldGen;

/// <summary>
/// The planet a world is on (GDD 3, stage 1b). It always lies in the habitable zone of its star. All values are
/// integers so they can be saved, hashed and compared exactly.
/// </summary>
/// <param name="AxialTiltTenths">Tilt of the spin axis against the orbit, in tenths of a degree (Earth 234, Uranus 978).</param>
/// <param name="YearHundredths">Length of the year in Earth years × 100 (Earth 100, Mars 188). Changes only the seasons: the game clock stays in Earth years.</param>
/// <param name="StarBrightnessPercent">Sunlight reaching the planet compared with Earth, in percent (80–120 stays habitable).</param>
/// <param name="EccentricityThousandths">How stretched the orbit is, in thousandths (Earth 17, Mars 93).</param>
public sealed record PlanetParameters(int AxialTiltTenths, int YearHundredths, int StarBrightnessPercent, int EccentricityThousandths)
{
    /// <summary>Earth.</summary>
    public static PlanetParameters Earth { get; } = new(234, 100, 100, 17);

    /// <summary>
    /// How strong the seasons are, in percent of an Earth-like atmosphere and ocean (decision of 2026-11-05). Thick air
    /// and big oceans carry heat between summer and winter and soften the seasons (below 100); thin air sharpens them.
    /// On a planet lying on its side, soft seasons make the middle latitudes livable in their summer, so tribes can follow
    /// the summer from one hemisphere to the other.
    /// </summary>
    public int SeasonStrengthPercent { get; init; } = 100;

    /// <inheritdoc cref="TiltRange"/>
    public static (int Min, int Max) SeasonStrengthRange => (20, 150);

    /// <summary>Smallest and largest allowed values.</summary>
    public static (int Min, int Max) TiltRange => (0, 1800);

    /// <inheritdoc cref="TiltRange"/>
    public static (int Min, int Max) YearRange => (10, 50_000);

    /// <inheritdoc cref="TiltRange"/>
    public static (int Min, int Max) BrightnessRange => (80, 120);

    /// <inheritdoc cref="TiltRange"/>
    public static (int Min, int Max) EccentricityRange => (0, 300);

    /// <summary>Throws when a value is outside its range.</summary>
    public void Validate()
    {
        Check(AxialTiltTenths, TiltRange, "axial tilt");
        Check(YearHundredths, YearRange, "year length");
        Check(StarBrightnessPercent, BrightnessRange, "star brightness");
        Check(EccentricityThousandths, EccentricityRange, "eccentricity");
        Check(SeasonStrengthPercent, SeasonStrengthRange, "season strength");
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
/// eccentricity and the star brightness. Values are shares of the sunlight Earth gets at its average distance, in
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
        long one = FixedTrig.One;
        long sinPhi = Math.Clamp(sinLatitude, -one, one);
        long cosPhi = FixedTrig.Complement(sinPhi);
        long sinTilt = FixedTrig.Sin(FixedTrig.TenthsOfDegree(planet.AxialTiltTenths));
        long e = planet.EccentricityThousandths * one / 1000;
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
            long q = Math.Max(0, daily) * distance / one * planet.StarBrightnessPercent / 100;
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
/// Climate from sunlight (GDD 3, stage 1b, decision of 2026-11-05: seasons are averaged, not played turn by turn).
/// The annual mean temperature follows the yearly sunlight, calibrated so an Earth-like planet has 28 °C at the
/// equator and −27 °C at the poles, with heat carried towards the poles by winds and currents. Seasons swing around it: more with a bigger difference between summer and winter
/// sunlight, far from the sea and on planets with long years; less over water.
/// </summary>
public sealed class ClimateModel
{
    private readonly long _equatorMean;
    private readonly long _poleMean;
    private readonly PlanetParameters _planet;
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
        _equatorMean = Insolation.At(0, PlanetParameters.Earth).Mean;
        _poleMean = Insolation.At(FixedTrig.One, PlanetParameters.Earth).Mean;
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
    /// Annual mean temperature at sea level, °C. Winds and sea currents carry heat towards the poles, so the
    /// temperature falls slowly at first and faster near the poles: with r = 0 at Earth's equatorial sunlight and 1 at
    /// Earth's polar sunlight, T = 28 − 55 × r × (0.3 + 0.7 r). Brighter places than Earth's equator warm by 16.5 °C per
    /// step of r.
    /// </summary>
    public int MeanTemperature(long sinLatitude)
    {
        long q = Sunlight(sinLatitude).Mean;
        long r = (_equatorMean - q) * 1000 / (_equatorMean - _poleMean); // per mille
        long cooling = r <= 0 ? r * 300 / 1000 : r * (300 + (700 * r / 1000)) / 1000;
        return (int)Math.Clamp(28 - (cooling * 55 / 1000), -60, 80);
    }

    /// <summary>
    /// Difference between the warmest and the coldest time of year, °C. <paramref name="continentalPercent"/> is 100 deep
    /// inland and lower near water.
    /// </summary>
    public int SeasonalSwing(long sinLatitude, int continentalPercent)
    {
        (_, long min, long max) = Sunlight(sinLatitude);
        long year = _planet.YearHundredths;
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
