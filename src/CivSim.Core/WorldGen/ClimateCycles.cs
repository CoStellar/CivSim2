using System;
using CivSim.Core.Numerics;

namespace CivSim.Core.WorldGen;

/// <summary>
/// Rules of the slow climate cycles (stage 1c-B, data/planets.json "climateCycles"; decision of 2026-11-05). The orbit
/// wobbles (Milankovitch cycles), ice sheets grow and melt (Paillard 1998), and the climate is solved again every
/// <see cref="UpdateYears"/>. All values are integers: years, tenths of a degree, thousandths and millionths.
/// </summary>
public sealed record ClimateCycleRules
{
    /// <summary>How often the climate is solved again, in years of game time.</summary>
    public int UpdateYears { get; init; } = 500;

    /// <summary>Swing of the axial tilt either side of the planet's tilt, tenths of a degree (Earth: 22.1–24.5°).</summary>
    public int TiltAmplitudeTenths { get; init; } = 12;

    /// <summary>Period of the tilt cycle, years.</summary>
    public int TiltPeriodYears { get; init; } = 41_000;

    /// <summary>Half the rise of the eccentricity above the planet's in the long cycle, thousandths (the game starts at the lowest).</summary>
    public int LongEccentricityThousandths { get; init; } = 10;

    /// <summary>Period of the long eccentricity cycle, years.</summary>
    public int LongEccentricityYears { get; init; } = 405_000;

    /// <summary>Half the rise of the eccentricity in the short cycle, thousandths (both together: Earth 0.017–0.059).</summary>
    public int ShortEccentricityThousandths { get; init; } = 11;

    /// <summary>Period of the short eccentricity cycle, years.</summary>
    public int ShortEccentricityYears { get; init; } = 100_000;

    /// <summary>Time for the perihelion to go once round the seasons (precession), years.</summary>
    public int PrecessionYears { get; init; } = 23_000;

    /// <summary>A warm period turns glacial when summer sunlight falls below this, millionths of its spread (−0.75).</summary>
    public long GlacialBelow { get; init; } = -750_000;

    /// <summary>A glacial becomes a full glacial once the ice volume passes this, millionths (0.8).</summary>
    public long FullGlacialVolume { get; init; } = 800_000;

    /// <summary>A full glacial ends when summer sunlight rises above this, millionths of its spread (0).</summary>
    public long WarmAbove { get; init; }

    /// <summary>Years for the ice to melt in a warm period.</summary>
    public int MeltYears { get; init; } = 10_000;

    /// <summary>Years for the ice to grow in a glacial.</summary>
    public int GrowYears { get; init; } = 50_000;

    /// <summary>Years over which summer sunlight adds or removes a whole ice volume.</summary>
    public int SunlightYears { get; init; } = 25_000;

    /// <summary>Ice volume (millionths) that counts as the full ice sheet for the climate (0.9).</summary>
    public long FullSheetVolume { get; init; } = 900_000;

    /// <summary>Latitude of the ice sheets' edge without ice and at the full sheet, degrees.</summary>
    public (int Start, int Full) SheetEdgeDegrees { get; init; } = (70, 40);

    /// <summary>CO₂ the oceans take up at the full sheet, percent (Earth: 280 → 190 ppm).</summary>
    public int GlacialCo2DropPercent { get; init; } = 32;

    /// <summary>
    /// Summer temperature at 65° (°C) at and below which a hemisphere gets the full ice ages, and at and above which it
    /// gets none (warm planets have no ice ages, cold ones have them in full).
    /// </summary>
    public (int Full, int None) GlaciationSummerDegrees { get; init; } = (5, 15);

    /// <summary>Samples of summer sunlight (one per 1000 years) used to measure its average and spread.</summary>
    public int SpreadSamples { get; init; } = 400;
}

/// <summary>Regime of one hemisphere's ice (Paillard 1998).</summary>
public enum IceRegime : byte
{
    /// <summary>Warm period: the ice melts.</summary>
    Interglacial = 0,

    /// <summary>The ice grows.</summary>
    Glacial = 1,

    /// <summary>Full glacial: waits for strong summer sun to end it.</summary>
    FullGlacial = 2,
}

/// <summary>The slow climate of a world (stage 1c-B): ice of both hemispheres and the current CO₂. Saved and hashed.</summary>
public sealed class ClimateState
{
    /// <summary>Game year of the last climate update.</summary>
    public long Year { get; set; }

    /// <summary>Ice regime of the northern hemisphere.</summary>
    public IceRegime NorthRegime { get; set; }

    /// <summary>Ice regime of the southern hemisphere.</summary>
    public IceRegime SouthRegime { get; set; }

    /// <summary>Ice volume of the northern hemisphere, millionths (0 = none).</summary>
    public long NorthIce { get; set; }

    /// <summary>Ice volume of the southern hemisphere, millionths.</summary>
    public long SouthIce { get; set; }

    /// <summary>CO₂ in the air now, millionths of a bar (0 until the first update: the planet's own).</summary>
    public int Co2Microbar { get; set; }

    /// <summary>Mean temperature of the planet at the last update, millionths of °C (for reports).</summary>
    public long GlobalMeanMillionths { get; set; }
}

/// <summary>
/// The Milankovitch cycles and the ice sheets (stage 1c-B). The game starts at a warm moment (decision of 2026-11-05):
/// the tilt is at its largest and the eccentricity at its smallest, so summers are mild everywhere and no ice grows.
/// </summary>
public sealed class ClimateCycles
{
    private const long Unit = 1_000_000;
    private readonly PlanetParameters _planet;
    private readonly ClimateCycleRules _rules;
    private long _sunMean;
    private long _sunSpread;

    /// <summary>Prepares the cycles of <paramref name="planet"/> (its values are the middle of the cycles).</summary>
    public ClimateCycles(PlanetParameters planet, ClimateCycleRules rules)
    {
        ArgumentNullException.ThrowIfNull(planet);
        ArgumentNullException.ThrowIfNull(rules);
        _planet = planet;
        _rules = rules;
    }

    // Average and spread of the northern summer sunlight at 65° over many cycles: the ice model reads it in spreads.
    // Measured on first use, so building the cycles only to read the starting orbit is cheap.
    private void MeasureSunlight()
    {
        if (_sunSpread > 0)
        {
            return;
        }

        ClimateCycleRules rules = _rules;
        long sum = 0;
        long sumSq = 0;
        int n = Math.Max(2, rules.SpreadSamples);
        for (int k = 0; k < n; k++)
        {
            long q = SummerSunlight(OrbitAt(k * 1000L), north: true);
            sum += q;
            sumSq += q * q;
        }

        _sunMean = sum / n;
        long variance = Math.Max(1, (sumSq / n) - (_sunMean * _sunMean));
        _sunSpread = Math.Max(1, IntMath.Sqrt(variance));
    }

    /// <summary>The planet as it is in game year <paramref name="year"/>: tilt, eccentricity, perihelion and star brightness.</summary>
    public PlanetParameters OrbitAt(long year)
    {
        int tilt = _planet.AxialTiltTenths + (int)(_rules.TiltAmplitudeTenths * Cos(year, _rules.TiltPeriodYears) / FixedTrig.One);
        long rise = (_rules.LongEccentricityThousandths * (FixedTrig.One - Cos(year, _rules.LongEccentricityYears)))
            + (_rules.ShortEccentricityThousandths * (FixedTrig.One - Cos(year, _rules.ShortEccentricityYears)));
        int eccentricity = _planet.EccentricityThousandths + (int)(rise / FixedTrig.One);
        int perihelion = (int)((_planet.PerihelionTenths + (year % _rules.PrecessionYears * 3600 / _rules.PrecessionYears)) % 3600);
        int luminosity = _planet.LuminosityThousandths;
        if (_planet.StarVariabilityPerMille != 0 && _planet.StarCycleYears > 0)
        {
            long wave = FixedTrig.Sin(Angle(year, _planet.StarCycleYears));
            luminosity = (int)Math.Max(1, luminosity + (luminosity * _planet.StarVariabilityPerMille * wave / 1000 / FixedTrig.One));
        }

        return _planet with
        {
            AxialTiltTenths = Math.Clamp(tilt, PlanetParameters.TiltRange.Min, PlanetParameters.TiltRange.Max),
            EccentricityThousandths = Math.Clamp(eccentricity, PlanetParameters.EccentricityRange.Min, PlanetParameters.EccentricityRange.Max),
            PerihelionTenths = perihelion,
            LuminosityThousandths = luminosity,
        };
    }

    /// <summary>
    /// Summer sunlight at 65° in a hemisphere, in spreads from its long-run average (millionths): below zero summers
    /// are colder than usual and ice can grow.
    /// </summary>
    public long SummerForcing(PlanetParameters orbit, bool north)
    {
        ArgumentNullException.ThrowIfNull(orbit);
        MeasureSunlight();
        return (SummerSunlight(orbit, north) - _sunMean) * Unit / _sunSpread;
    }

    /// <summary>
    /// Advances both hemispheres' ice from <see cref="ClimateState.Year"/> to <paramref name="year"/> in steps of
    /// <see cref="ClimateCycleRules.UpdateYears"/> (Paillard 1998: three regimes, slow growth, fast melting).
    /// </summary>
    public void Advance(ClimateState state, long year)
    {
        ArgumentNullException.ThrowIfNull(state);
        long step = Math.Max(1, _rules.UpdateYears);
        while (state.Year + step <= year)
        {
            state.Year += step;
            PlanetParameters orbit = OrbitAt(state.Year);
            (IceRegime rn, long vn) = Step(state.NorthRegime, state.NorthIce, SummerForcing(orbit, north: true), step);
            (IceRegime rs, long vs) = Step(state.SouthRegime, state.SouthIce, SummerForcing(orbit, north: false), step);
            state.NorthRegime = rn;
            state.NorthIce = vn;
            state.SouthRegime = rs;
            state.SouthIce = vs;
        }
    }

    /// <summary>
    /// Share of each energy-balance band covered by ice sheets, millionths: the sheets spread from
    /// <see cref="ClimateCycleRules.SheetEdgeDegrees"/> start to full as the ice grows, only over land
    /// (<paramref name="landShare"/>, millionths per band) and only as far as the hemisphere can glaciate
    /// (<paramref name="northPotential"/>, <paramref name="southPotential"/>, millionths).
    /// </summary>
    public long[] SheetCover(ClimateState state, long[] landShare, long northPotential, long southPotential)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(landShare);
        var cover = new long[EnergyBalance.Bands];
        long north = SheetShare(state.NorthIce, northPotential);
        long south = SheetShare(state.SouthIce, southPotential);
        for (int i = 0; i < EnergyBalance.Bands; i++)
        {
            long sin = EnergyBalance.BandSine(i);
            long share = sin >= 0 ? north : south;
            if (share <= 0)
            {
                continue;
            }

            int edge = _rules.SheetEdgeDegrees.Start - (int)((_rules.SheetEdgeDegrees.Start - _rules.SheetEdgeDegrees.Full) * share / Unit);
            long edgeSin = FixedTrig.Sin(FixedTrig.Degrees(edge)) * Unit / FixedTrig.One;
            if (Math.Abs(sin) >= edgeSin)
            {
                cover[i] = landShare[i] * Math.Min(Unit, 3 * share) / Unit;
            }
        }

        return cover;
    }

    /// <summary>The larger of the two hemispheres' sheets, millionths of the full sheet (for CO₂ and reports).</summary>
    public long LargestSheet(ClimateState state, long northPotential, long southPotential)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Math.Max(SheetShare(state.NorthIce, northPotential), SheetShare(state.SouthIce, southPotential));
    }

    /// <summary>CO₂ with the oceans' glacial uptake for this sheet share, millionths of a bar.</summary>
    public int GlacialCo2(int co2Microbar, long sheetShare) =>
        (int)Math.Max(1, co2Microbar - ((long)co2Microbar * _rules.GlacialCo2DropPercent * sheetShare / 100 / Unit));

    /// <summary>
    /// How much a hemisphere can glaciate (millionths), from its summer temperature at 65° in °C: full at
    /// <see cref="ClimateCycleRules.GlaciationSummerDegrees"/> Full and below, none at None and above.
    /// </summary>
    public long Potential(int summerTemperature)
    {
        (int full, int none) = _rules.GlaciationSummerDegrees;
        return Math.Clamp((none - summerTemperature) * Unit / Math.Max(1, none - full), 0, Unit);
    }

    private long SheetShare(long volume, long potential) =>
        Math.Clamp(volume * Unit / Math.Max(1, _rules.FullSheetVolume), 0, Unit) * potential / Unit;

    private (IceRegime Regime, long Volume) Step(IceRegime regime, long volume, long forcing, long years)
    {
        IceRegime r = regime switch
        {
            IceRegime.Interglacial when forcing < _rules.GlacialBelow => IceRegime.Glacial,
            IceRegime.Glacial when volume > _rules.FullGlacialVolume => IceRegime.FullGlacial,
            IceRegime.FullGlacial when forcing > _rules.WarmAbove => IceRegime.Interglacial,
            _ => regime,
        };
        (long target, long tau) = r == IceRegime.Interglacial ? (0L, (long)_rules.MeltYears) : (Unit, (long)_rules.GrowYears);

        // Paillard's softened forcing: strong summers melt more than weak ones grow, f' = (f + √(4 + f²)) / 2 − 1.
        long soft = ((forcing + (long)IntMath.Sqrt(((Int128)forcing * forcing) + (4 * (Int128)Unit * Unit))) / 2) - Unit;
        long change = ((target - volume) * years / tau) - (soft * years / _rules.SunlightYears);
        return (r, Math.Max(0, volume + change));
    }

    // Mean of the brighter half of the year at 65° (the "caloric summer"), in parts of FixedTrig.One.
    private static long SummerSunlight(PlanetParameters orbit, bool north)
    {
        Span<long> steps = stackalloc long[Insolation.Steps];
        long sin = FixedTrig.Sin(FixedTrig.Degrees(north ? 65 : -65));
        Insolation.At(sin, orbit.AxialTiltTenths, orbit.EccentricityThousandths, orbit.LightMillionths, steps, orbit.PerihelionTenths);
        steps.Sort();
        long sum = 0;
        for (int k = Insolation.Steps / 2; k < Insolation.Steps; k++)
        {
            sum += steps[k];
        }

        return sum / (Insolation.Steps / 2);
    }

    private static int Angle(long year, int period) => (int)(year % period * FixedTrig.FullTurn / period);

    private static long Cos(long year, int period) => FixedTrig.Cos(Angle(year, period));
}
