using System.Collections.Generic;

namespace CivSim.Protocol.Hosting;

/// <summary>
/// What the start menu offers (stage 5): the planet presets with their values, so the menu can show what a blank
/// field means. Plain values only; the client never reads the data files itself.
/// </summary>
public sealed record LocalSetupInfo(IReadOnlyList<PlanetPresetInfo> Planets);

/// <summary>
/// One planet preset (data/planets.json) in the units of <see cref="LocalGameOptions"/>. Star and orbit values the
/// preset does not fix are null (they follow from the other two).
/// </summary>
public sealed record PlanetPresetInfo(
    string Key,
    string Name,
    int AxialTiltTenths,
    int EccentricityPerMille,
    long LuminosityThousandths,
    long DistanceThousandths,
    long YearHundredths,
    long LightThousandths,
    int VolcanismPercent,
    int PressureHundredths,
    int WaterPercent,
    string Moisture,
    string Relief);

/// <summary>Result of checking start settings without generating a world: the planet facts, notes and any error.</summary>
public sealed record PlanetPreview(IReadOnlyList<string> Lines, IReadOnlyList<string> Notes, string? Error);
