using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using CivSim.Core.Map;
using CivSim.Core.Random;
using CivSim.Core.State;
using MessagePack;

namespace CivSim.Server.Saves;

/// <summary>
/// Writes and reads saved games: the 4-byte magic "CS2S", a 4-byte format version (little-endian),
/// then the MessagePack body (LZ4-compressed). Saves always happen between turns, so the state is consistent.
/// </summary>
public static class SaveGameSerializer
{
    /// <summary>Current save format version. Bump on every incompatible change and keep a loader for old versions.</summary>
    public const int FormatVersion = 9;

    private static readonly byte[] Magic = "CS2S"u8.ToArray();

    private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard
        .WithCompression(MessagePackCompression.Lz4BlockArray)
        .WithSecurity(MessagePackSecurity.UntrustedData);

    /// <summary>Writes <paramref name="state"/> to <paramref name="stream"/>.</summary>
    public static void Save(GameState state, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(stream);

        Span<RandomState> rng = stackalloc RandomState[RandomStreams.Count];
        state.Random.CopyStates(rng);
        var streams = new List<RandomStateData>(RandomStreams.Count);
        foreach (RandomState r in rng)
        {
            streams.Add(new RandomStateData(r.S0, r.S1, r.S2, r.S3));
        }

        var worlds = new List<WorldSaveData>(state.Worlds.Count);
        foreach (WorldState w in state.Worlds)
        {
            var layers = new List<MapLayerSaveData>(w.Map.Layers.Count);
            foreach (IMapLayerInfo layer in w.Map.Layers)
            {
                layers.Add(new MapLayerSaveData(layer.Name, layer.AsBytes().ToArray()));
            }

            worlds.Add(new WorldSaveData(w.Id.Value, w.Name, w.Width, w.Height, layers));
        }

        var civs = new List<CivilizationSaveData>(state.Civilizations.Count);
        foreach (CivilizationState c in state.Civilizations)
        {
            civs.Add(new CivilizationSaveData(c.Id.Value, c.Name, c.IsAlive, c.StartCell.Col, c.StartCell.Row, c.Era, c.DiedOutMonth));
        }

        var settlements = new List<SettlementSaveData>(state.Settlements.Count);
        foreach (SettlementState st in state.Settlements)
        {
            ReadOnlySpan<Core.Numerics.Amount> view = st.StockView;
            var stock = new long[view.Length];
            for (int g = 0; g < stock.Length; g++)
            {
                stock[g] = view[g].Hundredths;
            }

            settlements.Add(new SettlementSaveData(
                st.Id.Value, st.Owner.Value, st.World.Value, st.Cell.Col, st.Cell.Row, st.Name, st.Level, st.FoundedTurn, stock,
                st.PopulationView.ToArray(), st.Health, st.Mood, st.FoodGathered, st.FoodNeeded, st.FoodEaten, st.HigherNeedsPerMille, st.MovedMonth,
                (int)st.Status, st.AbandonedMonth, st.ClaimCiv, st.ClaimUntilMonth));
        }

        var effects = new List<EffectSaveData>(state.Effects.Count);
        foreach (EffectInstance e in state.Effects.All)
        {
            effects.Add(new EffectSaveData(e.Id.Value, e.DefinitionId, (int)e.Target, e.World.Value, e.TargetId, e.Strength, e.EndMonth));
        }

        GameSettings s = state.Settings;
        var data = new SaveData(s.Seed, s.MapWidth, s.MapHeight, s.CivilizationCount, state.Turn, streams, worlds, civs, (int)s.WorldType, s.WaterPercent, 0, (int)s.Moisture, (int)s.Relief, (int)s.Resources, (int)s.Inland,
            state.Month, state.NextSettlementId, settlements, state.Effects.NextId, effects,
            s.Planet.AxialTiltTenths, s.Planet.YearHundredths, s.Planet.StarBrightnessPercent, s.Planet.EccentricityThousandths, s.PlanetName, s.Planet.SeasonStrengthPercent);

        Span<byte> header = stackalloc byte[8];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], FormatVersion);
        stream.Write(header);
        MessagePackSerializer.Serialize(stream, data, Options);
    }

    /// <summary>Reads a saved game. Throws <see cref="InvalidDataException"/> for foreign or newer files.</summary>
    public static GameState Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> header = stackalloc byte[8];
        stream.ReadExactly(header);
        if (!header[..4].SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a CivSim 2 save file.");
        }

        int version = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Save format {version} is not supported (this build reads {FormatVersion}).");
        }

        SaveData data;
        try
        {
            data = MessagePackSerializer.Deserialize<SaveData>(stream, Options);
        }
        catch (MessagePackSerializationException ex)
        {
            throw new InvalidDataException("The save file is damaged.", ex);
        }

        var settings = new GameSettings(data.Seed, data.MapWidth, data.MapHeight, data.CivilizationCount)
        {
            WorldType = (Core.WorldGen.WorldType)data.WorldType,
            WaterPercent = data.WaterPercent,
            Planet = new Core.WorldGen.PlanetParameters(data.AxialTiltTenths, data.YearHundredths, data.StarBrightnessPercent, data.EccentricityThousandths)
            {
                SeasonStrengthPercent = data.SeasonStrengthPercent,
            },
            PlanetName = data.PlanetName,
            Moisture = (Core.WorldGen.WorldMoisture)data.Moisture,
            Inland = (Core.WorldGen.WorldInland)data.Inland,
            Relief = (Core.WorldGen.WorldRelief)data.Relief,
            Resources = (Core.WorldGen.WorldResources)data.Resources,
        };
        GameState state = GameState.CreateForLoad(settings, data.Turn);

        var rng = new RandomState[data.RandomStreams.Count];
        for (int i = 0; i < rng.Length; i++)
        {
            RandomStateData r = data.RandomStreams[i];
            rng[i] = new RandomState(r.S0, r.S1, r.S2, r.S3);
        }

        state.Random.RestoreStates(rng);

        foreach (WorldSaveData w in data.Worlds)
        {
            var world = new WorldState(new WorldId(w.Id), w.Name, w.Width, w.Height);
            LoadLayers(world.Map, w.Layers);
            if (world.Id.Value == 0)
            {
                world.Map.YearHundredths = settings.Planet.YearHundredths;
            }

            state.AddWorldForLoad(world);
        }

        foreach (CivilizationSaveData c in data.Civilizations)
        {
            state.AddCivilizationForLoad(new CivilizationState(new CivId(c.Id), c.Name, PlayerId.Ai) {
                IsAlive = c.IsAlive,
                StartCell = new HexCoord(c.StartCol, c.StartRow),
                Era = c.Era,
                DiedOutMonth = c.DiedOutMonth,
            });
        }

        state.SetCountersForLoad(data.Month, data.NextSettlementId);
        foreach (SettlementSaveData st in data.Settlements ?? [])
        {
            var settlement = new SettlementState(
                new SettlementId(st.Id), new CivId(st.Owner), new WorldId(st.World), new HexCoord(st.Col, st.Row), st.Name, st.Level, st.FoundedTurn, st.Stock.Length,
                st.Population?.Length ?? 0)
            {
                Health = st.Health,
                Mood = st.Mood,
                FoodGathered = st.FoodGathered,
                FoodNeeded = st.FoodNeeded,
                FoodEaten = st.FoodEaten,
                HigherNeedsPerMille = st.HigherNeedsPerMille,
                MovedMonth = st.MovedMonth,
                Status = (SettlementStatus)st.Status,
                AbandonedMonth = st.AbandonedMonth,
                ClaimCiv = st.ClaimCiv,
                ClaimUntilMonth = st.ClaimUntilMonth,
            };
            for (int g = 0; g < st.Stock.Length; g++)
            {
                settlement.Stock[g] = Core.Numerics.Amount.FromHundredths(st.Stock[g]);
            }

            st.Population?.CopyTo(settlement.Population);

            state.AddSettlementForLoad(settlement, data.NextSettlementId);
        }

        state.Effects.SetNextIdForLoad(data.NextEffectId);
        foreach (EffectSaveData e in data.Effects ?? [])
        {
            state.Effects.AddForLoad(
                new EffectInstance(new EffectId(e.Id), e.Definition, (Core.Content.EffectTarget)e.Target, new WorldId(e.World), e.TargetId, e.Strength, e.EndMonth),
                data.NextEffectId);
        }

        return state;
    }

    private static void LoadLayers(WorldMap map, IReadOnlyList<MapLayerSaveData> saved)
    {
        if (saved.Count != map.Layers.Count)
        {
            throw new InvalidDataException($"Save has {saved.Count} map layers, this build expects {map.Layers.Count}.");
        }

        for (int i = 0; i < saved.Count; i++)
        {
            IMapLayerInfo layer = map.Layers[i];
            if (saved[i].Name != layer.Name)
            {
                throw new InvalidDataException($"Map layer {i} is '{saved[i].Name}', expected '{layer.Name}'.");
            }

            try
            {
                layer.LoadBytes(saved[i].Bytes);
            }
            catch (System.ArgumentException ex)
            {
                throw new InvalidDataException(ex.Message, ex);
            }
        }

        map.MarkAllDirty();
    }

    /// <summary>Saves to a file, writing a temporary file first so a crash never leaves a half-written save.</summary>
    public static void SaveToFile(GameState state, string path)
    {
        string temp = path + ".tmp";
        using (FileStream fs = File.Create(temp))
        {
            Save(state, fs);
        }

        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Loads from a file.</summary>
    public static GameState LoadFromFile(string path)
    {
        using FileStream fs = File.OpenRead(path);
        return Load(fs);
    }
}
