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
    public const int FormatVersion = 3;

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
            civs.Add(new CivilizationSaveData(c.Id.Value, c.Name, c.IsAlive, c.StartCell.Col, c.StartCell.Row));
        }

        GameSettings s = state.Settings;
        var data = new SaveData(s.Seed, s.MapWidth, s.MapHeight, s.CivilizationCount, state.Turn, streams, worlds, civs, (int)s.WorldType, s.WaterPercent);

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
            state.AddWorldForLoad(world);
        }

        foreach (CivilizationSaveData c in data.Civilizations)
        {
            state.AddCivilizationForLoad(new CivilizationState(new CivId(c.Id), c.Name, PlayerId.Ai) {
                IsAlive = c.IsAlive,
                StartCell = new HexCoord(c.StartCol, c.StartRow),
            });
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
