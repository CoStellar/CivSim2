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
    public const int FormatVersion = 33;

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

            Core.WorldGen.ClimateState c = w.Climate;
            worlds.Add(new WorldSaveData(w.Id.Value, w.Name, w.Width, w.Height, layers,
                new ClimateSaveData(c.Year, (int)c.NorthRegime, (int)c.SouthRegime, c.NorthIce, c.SouthIce, c.Co2Microbar, c.GlobalMeanMillionths, c.SeaLevel, c.StartLandIce)));
        }

        var civs = new List<CivilizationSaveData>(state.Civilizations.Count);
        foreach (CivilizationState c in state.Civilizations)
        {
            civs.Add(new CivilizationSaveData(c.Id.Value, c.Name, c.IsAlive, c.StartCell.Col, c.StartCell.Row, c.Era, c.DiedOutMonth,
                c.KnownTechnologies, c.Researching, c.ResearchProgress, c.LastScience, c.SeenYear,
                c.Traits, c.GoalWeights, c.ExpansionTarget, c.StrategyReviewMonth, c.StrategyEra, SaveRelations(c),
                c.NextStepMonth, c.StepStartMonth, c.StepMonths, c.Steps, (byte)c.Alarms, c.LanguageSeed, c.NamesIssued,
                c.Government, c.PendingGovernment, c.PendingGovernmentMonth,
                c.LeaderName, c.LeaderBornMonth, c.LeaderSinceMonth, c.LeaderTraits, c.LeaderShift, c.Leaders, c.CrisisUntilMonth,
                c.Capital, c.Stability, c.AdminUsed, c.AnarchyUntilMonth, c.CapitalLostUntilMonth,
                c.ParentCiv, c.FoundedMonth, c.RevoltCalmUntilMonth, c.LanguageMutations, c.LastPulseTick));
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
                (int)st.Status, st.AbandonedMonth, st.ClaimCiv, st.ClaimUntilMonth, st.RecipeId, st.RunsMade, st.JourneyTarget,
                st.SkilledView.ToArray(), st.TraineesView.ToArray(), st.Loyalty, st.JourneyNext, st.JourneyStepTicks, st.JourneyProgress,
                st.ScoutOut, st.ScoutBackMonth));
        }

        var effects = new List<EffectSaveData>(state.Effects.Count);
        foreach (EffectInstance e in state.Effects.All)
        {
            effects.Add(new EffectSaveData(e.Id.Value, e.DefinitionId, (int)e.Target, e.World.Value, e.TargetId, e.Strength, e.EndMonth));
        }

        GameSettings s = state.Settings;
        var data = new SaveData(s.Seed, s.MapWidth, s.MapHeight, s.CivilizationCount, state.Turn, streams, worlds, civs, (int)s.WorldType, s.WaterPercent, 0, (int)s.Moisture, (int)s.Relief, (int)s.Resources, (int)s.Inland,
            state.Month, state.NextSettlementId, settlements, state.Effects.NextId, effects,
            s.Planet.AxialTiltTenths, s.Planet.LuminosityThousandths, s.Planet.DistanceThousandths, s.Planet.EccentricityThousandths, s.PlanetName,
            s.Planet.Co2Microbar, s.Planet.BackgroundPressureHundredths, s.Planet.VolcanismPercent,
            s.Planet.StarVariabilityPerMille, s.Planet.StarCycleYears, SaveTreaties(state), SaveProposals(state), state.StepMonths,
            state.WorldMonths, SaveEpisodes(state), state.Episodes.NextId, state.ActionMonth, SaveUnits(state), state.Units.NextId);

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
            Planet = new Core.WorldGen.PlanetParameters(
                data.AxialTiltTenths, data.EccentricityThousandths, data.LuminosityThousandths, data.DistanceThousandths,
                data.Co2Microbar, data.BackgroundPressureHundredths, data.VolcanismPercent)
            {
                StarVariabilityPerMille = data.StarVariabilityPerMille,
                StarCycleYears = data.StarCycleYears,
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
            if (w.Climate is ClimateSaveData c)
            {
                world.Climate.Year = c.Year;
                world.Climate.NorthRegime = (Core.WorldGen.IceRegime)c.NorthRegime;
                world.Climate.SouthRegime = (Core.WorldGen.IceRegime)c.SouthRegime;
                world.Climate.NorthIce = c.NorthIce;
                world.Climate.SouthIce = c.SouthIce;
                world.Climate.Co2Microbar = c.Co2Microbar;
                world.Climate.GlobalMeanMillionths = c.GlobalMeanMillionths;
                world.Climate.SeaLevel = c.SeaLevel;
                world.Climate.StartLandIce = c.StartLandIce;
            }
            if (world.Id.Value == 0)
            {
                world.Map.YearHundredths = settings.Planet.YearHundredths;
            }

            state.AddWorldForLoad(world);
        }

        foreach (CivilizationSaveData c in data.Civilizations)
        {
            var civ = new CivilizationState(new CivId(c.Id), c.Name, PlayerId.Ai) {
                IsAlive = c.IsAlive,
                StartCell = new HexCoord(c.StartCol, c.StartRow),
                Era = c.Era,
                DiedOutMonth = c.DiedOutMonth,
                KnownTechnologies = c.KnownTechnologies ?? [],
                KnownCount = System.Linq.Enumerable.Count(c.KnownTechnologies ?? [], k => k),
                Researching = c.Researching,
                ResearchProgress = c.ResearchProgress,
                LastScience = c.LastScience,
                SeenYear = c.SeenYear ?? [],
                Traits = c.Traits ?? [],
                GoalWeights = c.GoalWeights ?? [],
                ExpansionTarget = c.ExpansionTarget,
                StrategyReviewMonth = c.StrategyReviewMonth,
                StrategyEra = c.StrategyEra,
                NextStepMonth = c.NextStepMonth,
                StepStartMonth = c.StepStartMonth,
                StepMonths = c.StepMonths,
                Steps = c.Steps,
                Alarms = (Core.Simulation.Alarm)c.Alarms,
                LanguageSeed = c.LanguageSeed,
                NamesIssued = c.NamesIssued,
                Government = c.Government,
                PendingGovernment = c.PendingGovernment,
                PendingGovernmentMonth = c.PendingGovernmentMonth,
                LeaderName = c.LeaderName ?? string.Empty,
                LeaderBornMonth = c.LeaderBornMonth,
                LeaderSinceMonth = c.LeaderSinceMonth,
                LeaderTraits = c.LeaderTraits ?? [],
                LeaderShift = c.LeaderShift ?? [],
                Leaders = c.Leaders,
                CrisisUntilMonth = c.CrisisUntilMonth,
                Capital = c.Capital,
                Stability = c.Stability,
                AdminUsed = c.AdminUsed,
                AnarchyUntilMonth = c.AnarchyUntilMonth,
                CapitalLostUntilMonth = c.CapitalLostUntilMonth,
                ParentCiv = c.ParentCiv,
                FoundedMonth = c.FoundedMonth,
                RevoltCalmUntilMonth = c.RevoltCalmUntilMonth,
                LanguageMutations = c.LanguageMutations ?? [],
                LastPulseTick = c.LastPulseTick,
            };
            LoadRelations(civ, c.Relations);
            state.AddCivilizationForLoad(civ);
        }

        state.SetCountersForLoad(data.Month, data.NextSettlementId, data.StepMonths);
        state.SetWorldMonthsForLoad(data.WorldMonths);
        state.SetActionMonthForLoad(data.ActionMonth);
        LoadEpisodes(state, data);
        LoadUnits(state, data);
        foreach (SettlementSaveData st in data.Settlements ?? [])
        {
            var settlement = new SettlementState(
                new SettlementId(st.Id), new CivId(st.Owner), new WorldId(st.World), new HexCoord(st.Col, st.Row), st.Name, st.Level, st.FoundedTurn, st.Stock.Length,
                st.Population?.Length ?? 0, st.Skilled?.Length ?? 0)
            {
                Health = st.Health,
                Mood = st.Mood,
                Loyalty = st.Loyalty,
                FoodGathered = st.FoodGathered,
                FoodNeeded = st.FoodNeeded,
                FoodEaten = st.FoodEaten,
                HigherNeedsPerMille = st.HigherNeedsPerMille,
                MovedMonth = st.MovedMonth,
                Status = (SettlementStatus)st.Status,
                AbandonedMonth = st.AbandonedMonth,
                ClaimCiv = st.ClaimCiv,
                ClaimUntilMonth = st.ClaimUntilMonth,
                RecipeId = st.RecipeId,
                RunsMade = st.RunsMade,
                JourneyTarget = st.JourneyTarget,
                JourneyNext = st.JourneyNext,
                JourneyStepTicks = st.JourneyStepTicks,
                JourneyProgress = st.JourneyProgress,
                ScoutOut = st.ScoutOut,
                ScoutBackMonth = st.ScoutBackMonth,
            };
            for (int g = 0; g < st.Stock.Length; g++)
            {
                settlement.Stock[g] = Core.Numerics.Amount.FromHundredths(st.Stock[g]);
            }

            st.Population?.CopyTo(settlement.Population);
            st.Skilled?.CopyTo(settlement.Skilled);
            st.Trainees?.CopyTo(settlement.Trainees);

            state.AddSettlementForLoad(settlement, data.NextSettlementId);
        }

        state.Effects.SetNextIdForLoad(data.NextEffectId);
        foreach (EffectSaveData e in data.Effects ?? [])
        {
            state.Effects.AddForLoad(
                new EffectInstance(new EffectId(e.Id), e.Definition, (Core.Content.EffectTarget)e.Target, new WorldId(e.World), e.TargetId, e.Strength, e.EndMonth),
                data.NextEffectId);
        }

        LoadTreaties(state, data);
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

    private static List<TreatySaveData> SaveTreaties(GameState state)
    {
        var list = new List<TreatySaveData>(state.Treaties.All.Count);
        foreach (Core.Diplomacy.Treaty t in state.Treaties.All)
        {
            list.Add(new TreatySaveData(t.A.Value, t.B.Value, (byte)t.Kind, t.Initiator.Value, t.SinceMonth, t.UntilMonth,
                t.GatewayA, t.GatewayB, t.LastFlowMonth, t.LastWorth, t.TotalWorth, (byte)t.Goal, t.GoalSettlement, t.Score,
                t.DeadA, t.DeadB, t.PeopleA, t.PeopleB, t.WearinessA, t.WearinessB, SaveTerms(t.Terms), t.Campaigns, t.CampaignMonths, t.LastCampaignTick, t.ReadyTick, t.SinceTick));
        }

        return list;
    }

    private static List<ProposalSaveData> SaveProposals(GameState state)
    {
        var list = new List<ProposalSaveData>(state.Treaties.Proposals.Count);
        foreach (Core.Diplomacy.TreatyProposal p in state.Treaties.Proposals)
        {
            list.Add(new ProposalSaveData(p.From.Value, p.To.Value, (byte)p.Kind, p.Month, (byte)p.Status, SaveTerms(p.Terms)));
        }

        return list;
    }

    private static List<UnitSaveData> SaveUnits(GameState state)
    {
        var list = new List<UnitSaveData>(state.Units.Count);
        foreach (Core.Units.Unit u in state.Units.All)
        {
            list.Add(new UnitSaveData(u.Id, (byte)u.Kind, u.Owner.Value, u.World.Value, u.Cell, u.Target, (byte)u.Order, u.Home,
                [.. u.People], [.. u.Skilled], [.. u.Trainees], Hundredths(u.Cargo), u.Progress, u.Next, u.Direction, [.. u.Trail]));
        }

        return list;
    }

    private static void LoadUnits(GameState state, SaveData data)
    {
        state.Units.SetNextIdForLoad(data.NextUnitId);
        foreach (UnitSaveData u in data.Units ?? [])
        {
            state.Units.Restore(
                new Core.Units.Unit
                {
                    Id = u.Id,
                    Kind = (Core.Units.UnitKind)u.Kind,
                    Owner = new CivId(u.Owner),
                    World = new WorldId(u.World),
                    Cell = u.Cell,
                    Target = u.Target,
                    Order = (Core.Units.UnitOrder)u.Order,
                    Home = u.Home,
                    People = u.People ?? [],
                    Skilled = u.Skilled ?? [],
                    Trainees = u.Trainees ?? [],
                    Cargo = Amounts(u.Cargo),
                    Progress = u.Progress,
                    Next = u.Next,
                    Direction = u.Direction,
                    Trail = [.. u.Trail ?? []],
                },
                data.NextUnitId);
        }
    }

    private static List<EpisodeSaveData> SaveEpisodes(GameState state)
    {
        var list = new List<EpisodeSaveData>(state.Episodes.Count);
        foreach (Core.Episodes.Episode e in state.Episodes.All)
        {
            list.Add(new EpisodeSaveData(e.Id, (byte)e.Kind, e.Civ.Value, e.Other.Value, e.StartMonth, e.NextTick, e.TickMonths,
                e.Home, e.Target, e.Warriors, e.Cell, (byte)e.Phase, e.Dead, Hundredths(e.Loot),
                e.GoalTarget, [.. e.Members], [.. e.MemberWarriors], e.Mustered, e.ArmsPercent, e.Morale, e.MusterUntilTick,
                Hundredths(e.Food), e.HungryMonths, e.Forays, e.Foraging, e.ConvoyFrom, e.ConvoyCell, e.ConvoyCarriers,
                Hundredths(e.ConvoyFood), e.Convoys, e.LastPrey, e.Battles, e.Wins, e.Killed));
        }

        return list;
    }

    private static void LoadEpisodes(GameState state, SaveData data)
    {
        foreach (EpisodeSaveData e in data.Episodes ?? [])
        {
            state.Episodes.AddForLoad(
                new Core.Episodes.Episode
                {
                    Kind = (Core.Episodes.EpisodeKind)e.Kind,
                    Civ = new CivId(e.Civ),
                    Other = new CivId(e.Other),
                    StartMonth = e.StartMonth,
                    NextTick = e.NextTick,
                    TickMonths = e.TickMonths,
                    Home = e.Home,
                    Target = e.Target,
                    Warriors = e.Warriors,
                    Cell = e.Cell,
                    Phase = (Core.Episodes.CampaignPhase)e.Phase,
                    Dead = e.Dead,
                    Loot = Amounts(e.Loot),
                    GoalTarget = e.GoalTarget,
                    Members = e.Members ?? [],
                    MemberWarriors = e.MemberWarriors ?? [],
                    Mustered = e.Mustered,
                    ArmsPercent = e.ArmsPercent,
                    Morale = e.Morale,
                    MusterUntilTick = e.MusterUntilTick,
                    Food = Amounts(e.Food),
                    HungryMonths = e.HungryMonths,
                    Forays = e.Forays,
                    Foraging = e.Foraging,
                    ConvoyFrom = e.ConvoyFrom,
                    ConvoyCell = e.ConvoyCell,
                    ConvoyCarriers = e.ConvoyCarriers,
                    ConvoyFood = Amounts(e.ConvoyFood),
                    Convoys = e.Convoys,
                    LastPrey = e.LastPrey,
                    Battles = e.Battles,
                    Wins = e.Wins,
                    Killed = e.Killed,
                },
                e.Id);
        }

        state.Episodes.SetNextIdForLoad(data.NextEpisodeId);
    }

    private static long[] Hundredths(Core.Numerics.Amount[] amounts)
    {
        long[] list = new long[amounts.Length];
        for (int g = 0; g < list.Length; g++)
        {
            list[g] = amounts[g].Hundredths;
        }

        return list;
    }

    private static Core.Numerics.Amount[] Amounts(long[]? hundredths)
    {
        var list = new Core.Numerics.Amount[hundredths?.Length ?? 0];
        for (int g = 0; g < list.Length; g++)
        {
            list[g] = Core.Numerics.Amount.FromHundredths(hundredths![g]);
        }

        return list;
    }

    private static PeaceTermsSaveData SaveTerms(Core.Diplomacy.PeaceTerms terms) =>
        new((byte)terms.Kind, terms.Giver.Value, terms.Settlement, terms.RansomPercent);

    private static Core.Diplomacy.PeaceTerms LoadTerms(PeaceTermsSaveData? t) => t is null
        ? Core.Diplomacy.PeaceTerms.White
        : new Core.Diplomacy.PeaceTerms((Core.Diplomacy.PeaceTermsKind)t.Kind, new CivId(t.Giver), t.Settlement, t.RansomPercent);

    private static void LoadTreaties(GameState state, SaveData data)
    {
        foreach (TreatySaveData t in data.Treaties ?? [])
        {
            state.Treaties.Add(new Core.Diplomacy.Treaty(new CivId(t.A), new CivId(t.B), (Core.Diplomacy.TreatyKind)t.Kind, new CivId(t.Initiator), t.SinceMonth)
            {
                UntilMonth = t.UntilMonth,
                GatewayA = t.GatewayA,
                GatewayB = t.GatewayB,
                LastFlowMonth = t.LastFlowMonth,
                LastWorth = t.LastWorth,
                TotalWorth = t.TotalWorth,
                Goal = (Core.Diplomacy.WarGoal)t.Goal,
                GoalSettlement = t.GoalSettlement,
                Score = t.Score,
                DeadA = t.DeadA,
                DeadB = t.DeadB,
                PeopleA = t.PeopleA,
                PeopleB = t.PeopleB,
                WearinessA = t.WearinessA,
                WearinessB = t.WearinessB,
                Terms = LoadTerms(t.Terms),
                Campaigns = t.Campaigns,
                CampaignMonths = t.CampaignMonths,
                LastCampaignTick = t.LastCampaignTick,
                ReadyTick = t.ReadyTick,
                SinceTick = t.SinceTick,
            });
        }

        foreach (ProposalSaveData p in data.Proposals ?? [])
        {
            state.Treaties.AddProposalForLoad(new Core.Diplomacy.TreatyProposal(
                new CivId(p.From), new CivId(p.To), (Core.Diplomacy.TreatyKind)p.Kind, p.Month, (Core.Diplomacy.ProposalStatus)p.Status, LoadTerms(p.Terms)));
        }
    }

    private static List<RelationSaveData> SaveRelations(CivilizationState civ)
    {
        var list = new List<RelationSaveData>(civ.Relations.Count);
        foreach (Core.Diplomacy.Relation r in civ.Relations)
        {
            var memories = new List<RelationMemorySaveData>(r.Memories.Count);
            foreach (Core.Diplomacy.RelationMemory m in r.Memories)
            {
                memories.Add(new RelationMemorySaveData((byte)m.Kind, m.Sympathy, m.Trust, m.EndMonth, m.FadeMonths));
            }

            list.Add(new RelationSaveData(r.Other.Value, (byte)r.Level, (byte)r.Way, r.FirstMonth, r.MetMonth, r.LastContactMonth,
                r.Opening, r.Sympathy, r.Trust, r.RumorCell, r.RumorPeople, r.RumorFrom, memories, r.SettledMonth));
        }

        return list;
    }

    private static void LoadRelations(CivilizationState civ, List<RelationSaveData>? relations)
    {
        foreach (RelationSaveData d in relations ?? [])
        {
            Core.Diplomacy.Relation r = civ.AddRelation(new CivId(d.Other));
            r.Level = (Core.Diplomacy.ContactLevel)d.Level;
            r.Way = (Core.Diplomacy.ContactWay)d.Way;
            r.FirstMonth = d.FirstMonth;
            r.MetMonth = d.MetMonth;
            r.LastContactMonth = d.LastContactMonth;
            r.Opening = d.Opening;
            r.Sympathy = d.Sympathy;
            r.Trust = d.Trust;
            r.RumorCell = d.RumorCell;
            r.RumorPeople = d.RumorPeople;
            r.RumorFrom = d.RumorFrom;
            r.SettledMonth = d.SettledMonth;
            foreach (RelationMemorySaveData m in d.Memories ?? [])
            {
                r.Remember(new Core.Diplomacy.RelationMemory
                {
                    Kind = (Core.Diplomacy.RelationEvent)m.Kind,
                    Sympathy = m.Sympathy,
                    Trust = m.Trust,
                    EndMonth = m.EndMonth,
                    FadeMonths = m.FadeMonths,
                }, int.MaxValue, 0);
            }
        }
    }
}
